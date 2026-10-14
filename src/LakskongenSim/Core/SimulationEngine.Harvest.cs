using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Harvest;
using LakskongenSim.Processing;

namespace LakskongenSim.Core;

/// <summary>
/// The harvest pipeline (roadmap S2-04). A live kilo is no longer sold at the
/// gutted-weight price: fish fast, a wellboat (or a land farm's pipe) takes
/// them to slaughter within the slaughterhouse's daily capacity, they are
/// gutted at the HOG yield, sorted into size classes and grades and sold at
/// the reference price times the size-class and grade factors; the harvest
/// cost is booked as cost of goods sold (review F270).
/// <para>
/// Seam for the farm types: a holding (sea site, land facility, offshore farm)
/// supplies its pens, its route (<see cref="HarvestRoute.For"/>: wellboat to
/// the slaughterhouse, or the land farm's own hall) and each pen's condition
/// (<see cref="HarvestCondition"/>: cold-water memory, delousing injuries,
/// lice, a holding's own downgrade); the pricing never reads a zone or a site.
/// </para>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Harvest calibration in use (S2-04).</summary>
    internal HarvestCalibration HarvestCalibration => _calibration.Harvest;

    /// <summary>
    /// What holds the fish of a harvest: its pens, its kind, its route and
    /// whether its sales count for the resource-rent tax (sea-phase fish do;
    /// land-based tanks never do).
    /// </summary>
    private sealed record HarvestHolding(string Id, string Label, HoldingKind Kind,
        List<FishPopulation> Pens, bool ResourceRentLiable, SeaSite? Site, HarvestRoute Route);

    private HarvestHolding? FindHarvestHolding(string holdingId)
    {
        if (State == null || string.IsNullOrEmpty(holdingId)) return null;
        var cal = HarvestCalibration;
        var site = State.SeaSites.FirstOrDefault(s => s.Id == holdingId);
        if (site != null)
            return new HarvestHolding(site.Id, site.Label, HoldingKind.SeaPen, site.Pens, true, site,
                HarvestRoute.For(HoldingKind.SeaPen, cal, site.Id));
        var ras = RASFacilities.FirstOrDefault(f => f.Id == holdingId);
        if (ras != null)
            return new HarvestHolding(ras.Id, ras.Name, HoldingKind.LandTank, ras.Tanks, false, null,
                HarvestRoute.For(HoldingKind.LandTank, cal, ras.Id));
        var offshore = OffshoreFarms.FirstOrDefault(f => f.Id == holdingId);
        if (offshore != null)
            return new HarvestHolding(offshore.Id, offshore.Name, HoldingKind.Offshore, offshore.Pens, true, null,
                HarvestRoute.For(HoldingKind.Offshore, cal, offshore.Id));
        return null;
    }

    /// <summary>What the fish of <paramref name="pen"/> carry to the slaughterhouse.</summary>
    private HarvestCondition HarvestConditionOf(FishPopulation pen, HarvestHolding holding)
    {
        int fish = pen.TotalCount;
        double meanKg = fish > 0 ? pen.GetTotalBiomassKg() / fish : 0.0;
        return new HarvestCondition(
            meanKg,
            pen.ColdExposure,
            pen.TreatmentHarm.InjuryLoad,
            holding.Site?.Lice.AdultFemalePerFish ?? 0.0);
    }

    /// <summary>Weight CV of a cohort, or the CV of a standard cohort of its weight when not yet known.</summary>
    private double CohortCv(FishPopulation.Cohort cohort)
        => cohort.WeightCv > 0.0 ? cohort.WeightCv : (Growth?.Law ?? TgcGrowthLaw.Default).InitialWeightCv(cohort.MeanWeightKg);

    /// <summary>
    /// Fish to take from each cohort of <paramref name="pen"/> for
    /// <paramref name="fish"/> fish in all: proportional to the cohorts' counts,
    /// the remainder by the largest fractional share (ties to the earlier cohort).
    /// </summary>
    private static int[] PlanCohortTakes(FishPopulation pen, int fish)
    {
        var takes = new int[pen.Cohorts.Count];
        int total = pen.TotalCount;
        if (fish <= 0 || total <= 0) return takes;
        if (fish >= total)
        {
            for (int i = 0; i < takes.Length; i++) takes[i] = Math.Max(0, pen.Cohorts[i].Count);
            return takes;
        }
        var remainders = new double[takes.Length];
        int assigned = 0;
        for (int i = 0; i < takes.Length; i++)
        {
            double exact = (double)pen.Cohorts[i].Count * fish / total;
            takes[i] = (int)Math.Floor(exact);
            remainders[i] = exact - takes[i];
            assigned += takes[i];
        }
        while (assigned < fish)
        {
            int best = -1;
            for (int i = 0; i < takes.Length; i++)
                if (takes[i] < pen.Cohorts[i].Count && (best < 0 || remainders[i] > remainders[best]))
                    best = i;
            if (best < 0) break;
            takes[best]++;
            remainders[best] = -1.0;
            assigned++;
        }
        return takes;
    }

    /// <summary>Add <paramref name="fish"/> fish of <paramref name="pen"/> to <paramref name="lot"/>; remove them from the pen when <paramref name="remove"/>.</summary>
    private void AddPenToLot(HarvestLot lot, FishPopulation pen, int fish, HarvestHolding holding, bool remove)
    {
        var condition = HarvestConditionOf(pen, holding);
        var takes = PlanCohortTakes(pen, fish);
        for (int i = 0; i < takes.Length; i++)
        {
            if (takes[i] <= 0) continue;
            var cohort = pen.Cohorts[i];
            lot.AddFish(takes[i], cohort.MeanWeightKg, CohortCv(cohort), condition);
            if (remove) cohort.Count -= takes[i];
        }
        if (remove) pen.Cohorts.RemoveAll(c => c.Count <= 0);
    }

    /// <summary>Drop emptied pens from the holding and the game (their fish are all sold or dead).</summary>
    private void RemoveEmptyPens(HarvestHolding holding)
    {
        foreach (var pen in holding.Pens.Where(p => p.TotalCount <= 0).ToList())
        {
            pen.Cohorts.Clear();
            holding.Pens.Remove(pen);
            State.Populations.Remove(pen);
            Mortality?.ClearEnvironmentalStress(pen.Id);
        }
    }

    /// <summary>
    /// The pens of <paramref name="holding"/> a harvest takes: the listed ones
    /// (all pens with fish when none are listed). Null with an error when a
    /// listed pen is not in the holding.
    /// </summary>
    private List<FishPopulation>? SelectHarvestPens(HarvestHolding holding, IReadOnlyCollection<string>? penIds,
        out string errorKey, out string message)
    {
        errorKey = "";
        message = "";
        if (penIds == null || penIds.Count == 0)
            return holding.Pens.Where(p => p.TotalCount > 0).ToList();
        var pens = new List<FishPopulation>();
        foreach (var id in penIds.Distinct())
        {
            var pen = holding.Pens.FirstOrDefault(p => p.Id == id);
            if (pen == null)
            {
                errorKey = "pen_not_found";
                message = $"Pen '{id}' is not at '{holding.Id}'";
                return null;
            }
            if (pen.TotalCount > 0) pens.Add(pen);
        }
        return pens;
    }

    /// <summary>Fish a harvest of <paramref name="fraction"/> takes from a pen of <paramref name="fish"/> fish.</summary>
    private static int FishToHarvest(int fish, double fraction)
        => fraction >= 1.0 ? fish : Math.Min(fish, Math.Max(1, (int)Math.Round(fish * fraction, MidpointRounding.AwayFromZero)));

    private static bool ValidFraction(double fraction) => double.IsFinite(fraction) && fraction > 0.0 && fraction <= 1.0;

    // ── Express harvest (the immediate verbs) ──────────────────────────────

    /// <summary>
    /// Harvest now (S2-04 express): the chosen pens' fish (all pens when none
    /// are listed; <paramref name="fraction"/> of each pen's fish) go today on
    /// a spot wellboat to the slaughterhouse's waiting pen, where they fast, and
    /// are sold at today's price through the pipeline: HOG yield, size classes,
    /// grades, the harvest cost plus the express surcharge. Value = live kg,
    /// Value2 = gross sales (NOK), Id = the holding. Fails before any mutation:
    /// holding_not_found / invalid_fraction / pen_not_found / holding_empty.
    /// </summary>
    public CommandResult HarvestNow(string holdingId, IReadOnlyCollection<string>? penIds = null, double fraction = 1.0)
        => HarvestNowAs(holdingId, penIds, fraction, HarvestMode.Express, "holding_empty",
            $"'{holdingId}' has no fish to harvest");

    /// <summary>The express harvest core shared by the site, facility and hired-wellboat verbs.</summary>
    private CommandResult HarvestNowAs(string holdingId, IReadOnlyCollection<string>? penIds, double fraction,
        HarvestMode mode, string emptyKey, string emptyMessage)
    {
        if (State == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var holding = FindHarvestHolding(holdingId);
        if (holding == null)
            return CommandResult.Fail("holding_not_found", $"'{holdingId}' not found");
        if (!ValidFraction(fraction))
            return CommandResult.Fail("invalid_fraction", $"Harvest share must be in (0, 1] (got {fraction})");
        var pens = SelectHarvestPens(holding, penIds, out var errorKey, out var errorMessage);
        if (pens == null)
            return CommandResult.Fail(errorKey, errorMessage);

        var preview = new HarvestLot(HarvestCalibration);
        foreach (var pen in pens)
            AddPenToLot(preview, pen, FishToHarvest(pen.TotalCount, fraction), holding, remove: false);
        if (preview.LiveKg < 0.1)
            return CommandResult.Fail(emptyKey, emptyMessage);

        var lot = new HarvestLot(HarvestCalibration);
        foreach (var pen in pens)
        {
            AddPenToLot(lot, pen, FishToHarvest(pen.TotalCount, fraction), holding, remove: true);
            // Fish left behind feed again, unless a planned order still owes them.
            if (pen.TotalCount > 0 && PenInOpenOrder(new[] { pen }) == null) StopFasting(pen);
        }
        var report = SellLot(lot, holding, mode, orderId: "");
        RemoveEmptyPens(holding);

        var message = SimText.Nb($"Høstet {lot.LiveKg:N0} kg ({lot.Fish:N0} fisk) på {holding.Label}");
        return CommandResult.Success(message, lot.LiveKg, (double)report.GrossRevenueNok, holding.Id);
    }

    // ── The sale ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sell a slaughtered lot and book it (S2-04): contracts first (oldest first,
    /// at their locked prices for the HOG kilos they take), the rest at today's
    /// spot; each kilo at the size-class and grade factors (the lot's value
    /// factor), the processing plant's VAP premium and a forced harvest's
    /// penalty on top. Books the gross sale as harvest revenue and the
    /// harvest cost (transport, slaughter, any surcharge) as cost of goods
    /// sold, adds the gutted kilos to the resource-rent base of liable
    /// holdings, moves the market and files the report.
    /// </summary>
    private HarvestReport SellLot(HarvestLot lot, HarvestHolding holding, HarvestMode mode, string orderId,
        decimal priceMultiplier = 1m, string revenueCategory = "harvest")
    {
        var cal = HarvestCalibration;
        var route = holding.Route;
        decimal spot = MarketSimulation?.PricePerKg ?? 0m;
        decimal valueFactor = (decimal)lot.ValueFactor;

        decimal contractRevenue = 0m;
        double contractKg = 0.0;
        double remainingKg = lot.HogKg;
        if (ContractSystem != null && mode != HarvestMode.Forced)
        {
            foreach (var c in ContractSystem.ActiveContracts.OrderBy(c => c.SignedAtWeek).ToList())
            {
                if (remainingKg <= 0.1) break;
                int deliver = (int)Math.Min(remainingKg, c.RemainingKg);
                if (deliver <= 0) continue;
                var (payment, _) = ContractSystem.FulfillContract(c.Id, deliver);
                contractRevenue += payment * valueFactor;
                contractKg += deliver;
                remainingKg -= deliver;
            }
        }
        decimal gross = (contractRevenue + (decimal)remainingKg * valueFactor * spot) * priceMultiplier;

        decimal vap = 1m;
        if (State.OwnsProcessingPlant)
        {
            // VAP fillet line (Phase 3.5): +12% when a plant is owned; a pale
            // grade-C recipe halves the premium to +6% (Phase 4.4).
            string recipeGrade = FeedRecipe.Grade(State.RecipeProteinPct, State.RecipeAstaxPpm);
            vap = FeedRecipe.VapMult(recipeGrade);
            gross *= vap;
            if (recipeGrade == "C")
                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Blek filet (C)",
                    Category = EventCategory.Market,
                    Description = "Lav astaxanthin i fôret — VAP-premie halvert til +6%",
                    BalanceChange = 0m
                });
        }

        decimal hog = (decimal)lot.HogKg;
        decimal transport = mode == HarvestMode.HiredWellboat ? 0m : hog * route.TransportCostPerKgHogNok;
        decimal slaughter = hog * route.SlaughterCostPerKgHogNok;
        // The express surcharge buys a spot wellboat and the slaughterhouse's waiting pen;
        // a land farm's own hall needs neither.
        decimal surcharge = (mode is HarvestMode.Express or HarvestMode.Forced) && route.NeedsWellboat
            ? hog * cal.ExpressSurchargePerKgHogNok : 0m;

        ReceiveIncome(gross, revenueCategory);
        PayCost(transport + slaughter + surcharge, CostBooking.Cogs, "harvest");
        State.HarvestCostThisTaxYear += transport + slaughter + surcharge;
        State.TotalSalesRevenueNok += gross;
        State.TotalHarvestRevenueNok += gross;
        State.TotalHarvestedHogKg += lot.HogKg;
        State.TotalHarvestedLiveKg += lot.LiveKg;
        if (holding.ResourceRentLiable)
            RecordHarvestForResourceRent(lot.HogKg);

        // P15: the player's supply moves the market, in gutted tonnes like the
        // national weekly harvest it is compared with.
        MarketSimulation?.ApplyPlayerHarvest(lot.HogKg / 1000.0);

        var report = new HarvestReport
        {
            Id = $"harvest-d{State.Day}-{holding.Id}-b{++State.HarvestBatchesSold}",
            Day = State.Day,
            HoldingId = holding.Id,
            HoldingLabel = holding.Label,
            OrderId = orderId,
            Mode = mode,
            Route = route.Kind,
            Fish = lot.Fish,
            LiveKg = lot.LiveKg,
            HogKg = lot.HogKg,
            MeanLiveWeightKg = lot.MeanLiveWeightKg,
            WeightCv = lot.WeightCv,
            HogKgBySizeClass = lot.HogKgBySizeClassArray().ToList(),
            SuperiorShare = lot.SuperiorShare,
            OrdinaryShare = lot.OrdinaryShare,
            ProductionShare = lot.ProductionShare,
            SizeFactor = lot.SizeFactor,
            GradeFactor = lot.GradeFactor,
            ReferenceEquivalentKg = lot.ValueKg,
            ReferencePriceNok = spot,
            ContractHogKg = contractKg,
            ContractRevenueNok = contractRevenue * priceMultiplier * vap,
            VapMultiplier = vap,
            PriceMultiplier = priceMultiplier,
            GrossRevenueNok = gross,
            TransportCostNok = transport,
            SlaughterCostNok = slaughter,
            SurchargeNok = surcharge
        };
        State.HarvestReports.Add(report);
        int keep = Math.Max(1, cal.ReportsKept);
        if (State.HarvestReports.Count > keep)
            State.HarvestReports.RemoveRange(0, State.HarvestReports.Count - keep);

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Harvest",
            Category = EventCategory.Market,
            Description = SimText.Nb(
                $"Høstet {lot.LiveKg:N0} kg ({lot.Fish:N0} fisk) på {holding.Label}: {lot.HogKg:N0} kg sløyd, {lot.SuperiorShare:P0} superior, {gross:N0} kr brutto, {report.HarvestCostNok:N0} kr slakting og transport"),
            BalanceChange = report.NetRevenueNok
        });
        State.TrimEventLog();
        return report;
    }

    /// <summary>
    /// Sell <paramref name="harvestTonnesLive"/> live tonnes of a sea site's
    /// fish in a forced harvest (Mattilsynet): the site's pens contribute in
    /// proportion to their biomass, priced through the pipeline at
    /// <paramref name="priceMultiplier"/> of the reference price, with the
    /// express harvest cost. The fish are not removed here (the caller reduces
    /// the site's biomass). Returns the net cash the sale added.
    /// </summary>
    internal decimal SellForcedHarvest(SeaSite site, double harvestTonnesLive, decimal priceMultiplier)
    {
        var holding = FindHarvestHolding(site.Id);
        double biomassKg = site.Pens.Sum(p => p.GetTotalBiomassKg());
        if (holding == null || biomassKg <= 0.0 || harvestTonnesLive <= 0.0) return 0m;
        double share = Math.Clamp(harvestTonnesLive * 1000.0 / biomassKg, 0.0, 1.0);
        var lot = new HarvestLot(HarvestCalibration);
        foreach (var pen in site.Pens)
            AddPenToLot(lot, pen, (int)Math.Round(pen.TotalCount * share, MidpointRounding.AwayFromZero), holding, remove: false);
        if (lot.HogKg <= 0.0) return 0m;
        var report = SellLot(lot, holding, HarvestMode.Forced, orderId: "", priceMultiplier, "forced_harvest");
        return report.NetRevenueNok;
    }

    // ── Fasting and the condition the fish carry ─────────────────────────────

    /// <summary>Stop feeding <paramref name="pen"/> before slaughter (no-op when already fasting).</summary>
    internal void StartFasting(FishPopulation pen)
    {
        if (pen.IsFasting) return;
        pen.FastingSinceDay = State?.Day ?? 0;
        pen.FastingDegreeDays = 0.0;
    }

    /// <summary>Feed <paramref name="pen"/> again (its harvest is done or called off).</summary>
    internal static void StopFasting(FishPopulation pen)
    {
        pen.FastingSinceDay = -1;
        pen.FastingDegreeDays = 0.0;
    }

    /// <summary>True when <paramref name="pen"/> has fasted long enough to be slaughtered.</summary>
    internal bool FastingComplete(FishPopulation pen)
    {
        var cal = HarvestCalibration;
        return pen.IsFasting
            && State.Day - pen.FastingSinceDay >= cal.MinFastingDays
            && pen.FastingDegreeDays >= cal.FastingDegreeDays - 1e-9;
    }

    /// <summary>
    /// One day of the harvest condition a population carries (called by the
    /// growth step with the population's own water): fasting degree-days and
    /// the cold-water memory behind winter wounds.
    /// </summary>
    internal void TrackHarvestCondition(FishPopulation population, in HoldingWater water)
    {
        if (population.IsFasting)
            population.FastingDegreeDays += Math.Max(0.0, water.TemperatureC);
        population.ColdExposure = HarvestGrading.NextColdExposure(HarvestCalibration, population.ColdExposure, water.TemperatureC);
    }
}
