using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// Real pens (roadmap S2-01b). A sea site is a frame mooring of 6-12 pens
/// (<see cref="SeaSite.PhysicalPens"/>), each a production unit the regulator
/// holds to at most 200,000 fish (akvakulturdriftsforskriften § 47a) and
/// 25 kg/m³ (§ 25) in the volume its net encloses.
/// <list type="bullet">
/// <item><b>Stocking</b> picks a pen and a count
/// (<see cref="StockSmoltsInPen"/>): the pen must be empty, the count within
/// § 47a, and the plan's peak density within § 25, projected on the S2-03
/// growth law (<see cref="PenDensityPlanner"/>); then the S2-01a MTB check.
/// The site verb (<see cref="StockSmolts(string,int,string,SmoltQualityTier)"/>)
/// spreads its smolts like a farmer over the fewest empty pens that hold them
/// under the same limits.</item>
/// <item><b>Populations map to pens</b> (<see cref="Pen.PopulationId"/>): the
/// biology tick reads each population's density in its own pen
/// (<see cref="DensityVolumeM3"/>), a harvest takes pens by their pen id and
/// frees them, and a delousing can treat chosen pens.</item>
/// <item><b>Saves</b> from before S2-01b get one pen per population on load
/// (<see cref="MigratePensAfterLoad"/>).</item>
/// </list>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The density limit of a production unit (kg/m³, § 25).</summary>
    public double PenDensityLimitKgPerM3 => _calibration.Capacity.MaxStockingDensityKgPerM3;

    /// <summary>The fish limit of a production unit in the sea (§ 47a).</summary>
    public int MaxFishPerPen => _calibration.Pens.MaxFishPerPen;

    /// <summary>
    /// The fish limit a stocking is held to: § 47a under capacity v1 (the
    /// licence register, every scenario game); none in an unregulated game (a
    /// bare engine, or the "rules before S2-01" replay of the strategy probe),
    /// where only the pen's density limits how many fish it takes, as the
    /// farmer's own practice (the wave-2 density proxy spread fish the same way).
    /// </summary>
    private int PenFishLimit => CapacityRulesActive ? MaxFishPerPen : int.MaxValue;

    // ─── Layout ────────────────────────────────────────────────────────────

    /// <summary>The standard layout of a site the player builds (calibration <c>pens</c>).</summary>
    internal PenLayout BuiltSitePenLayout => PenLayout.Standard(_calibration.Pens);

    /// <summary>
    /// A save written before S2-01b has populations but no pens: give each of
    /// its sea sites its locality's layout (the active scenario's
    /// <c>pen_layout</c> for a scenario site, else the standard layout) with at
    /// least one pen per population, and put each population in its own pen,
    /// in list order. A population too large for its pen (stocked in one go
    /// before the per-pen limits, up to 560,000 smolts under S2-01a) keeps the
    /// water its density was simulated in, the wave-2 density proxy
    /// (<see cref="StockingVolumeM3"/>): its pen is grown to that volume, a
    /// grandfathered unit as § 70 exempts units stocked before § 47a. Sites
    /// that have pens are left as they are.
    /// </summary>
    internal void MigratePensAfterLoad()
    {
        if (State?.SeaSites == null) return;
        foreach (var site in State.SeaSites)
        {
            if (site.PhysicalPens is { Count: > 0 }) continue;
            var layout = LegacyLayoutOf(site);
            site.PhysicalPens = new PenLayout(Math.Max(layout.Count, site.Pens.Count), layout.Geometry).Build(site.Id);
            for (int i = 0; i < site.Pens.Count; i++)
            {
                var population = site.Pens[i];
                var pen = site.PhysicalPens[i];
                pen.PopulationId = population.Id;
                double legacyVolume = StockingVolumeM3(population);
                if (legacyVolume > pen.VolumeM3)
                {
                    pen.VolumeM3 = legacyVolume;
                    pen.CircumferenceM = PenGeometry.CircumferenceFor(legacyVolume, pen.NetWallDepthM, pen.ConeDepthM);
                }
            }
        }
    }

    /// <summary>The layout a legacy site gets: its scenario locality's, else the standard one.</summary>
    private PenLayout LegacyLayoutOf(SeaSite site)
    {
        var scenario = string.IsNullOrEmpty(State.ActiveScenarioId) ? null : Scenario?.GetScenario(State.ActiveScenarioId);
        var definition = scenario?.StartingSites?.FirstOrDefault(s => s.Id == site.Id);
        return definition != null ? definition.ResolvePenLayout(_calibration.Pens) : BuiltSitePenLayout;
    }

    // ─── The plan ──────────────────────────────────────────────────────────

    /// <summary>
    /// The plan a stocking at <paramref name="site"/> today is checked
    /// against: smolts of <paramref name="releaseWeightKg"/> growing at
    /// <paramref name="growthMultiplier"/> in the site's production area, to
    /// the plan's harvest weight with the plan's survival (S2-01a's plan).
    /// </summary>
    private PenStockingPlan PenPlanFor(SeaSite site, double releaseWeightKg, double growthMultiplier)
        => new(ProductionAreaOfSite(site), State.Day, releaseWeightKg, growthMultiplier,
            _calibration.Biology.TargetHarvestWeightKg,
            CapacityPlanner.DailySurvivalFor(_calibration.Health.SeaMortalityPerCycle, _calibration.Biology.GrowthToHarvestDays),
            _calibration.Capacity.PlanningHorizonDays);

    /// <summary>The plan's peak kilos per smolt of a strain and quality stocked at a site today (S2-03 growth law).</summary>
    private PenDensityForecast ForecastPenStocking(SeaSite site, SmoltStrain strain, SmoltQualityTier qualityTier)
    {
        double growth = strain.BuildGenome().GetGrowthMultiplier() * SmoltQualityRegistry.Get(qualityTier).GrowthMultiplier;
        return PenDensityPlanner.Project(Growth?.Law ?? TgcGrowthLaw.Default, PenPlanFor(site, strain.ReleaseWeightKg, growth));
    }

    /// <summary>The plan's peak kilos per fish of a transfer of <paramref name="weightKg"/> fish (post-smolt to sea).</summary>
    private PenDensityForecast ForecastPenStocking(SeaSite site, double weightKg)
        => PenDensityPlanner.Project(Growth?.Law ?? TgcGrowthLaw.Default, PenPlanFor(site, weightKg, 1.0));

    /// <summary>
    /// Most fish of <paramref name="forecast"/> <paramref name="pen"/> can take:
    /// 0 when it holds fish, else the lesser of § 47a's count and the count
    /// whose plan peak stays within § 25.
    /// </summary>
    private int PenRoom(SeaSite site, Pen pen, in PenDensityForecast forecast)
        => site.IsPenFree(pen)
            ? Math.Min(PenFishLimit, forecast.MaxSmolts(pen.VolumeM3, PenDensityLimitKgPerM3))
            : 0;

    /// <summary>The fish the site's empty pens can take under the per-pen limits.</summary>
    private long SitePenRoom(SeaSite site, in PenDensityForecast forecast)
    {
        long room = 0;
        foreach (var pen in site.PhysicalPens)
            room += PenRoom(site, pen, forecast);
        return room;
    }

    /// <summary>
    /// How the site verb spreads <paramref name="count"/> fish: over the
    /// fewest empty pens (in mooring order) whose room holds them, in shares
    /// proportional to each pen's room (the remainder by the largest fraction,
    /// ties to the earlier pen). Null when the empty pens cannot hold them.
    /// </summary>
    internal List<(Pen Pen, int Count)>? AllocateToPens(SeaSite site, int count, in PenDensityForecast forecast)
    {
        if (count <= 0) return null;
        var chosen = new List<(Pen Pen, int Room)>();
        long total = 0;
        foreach (var pen in site.PhysicalPens.OrderBy(p => p.Number))
        {
            int room = PenRoom(site, pen, forecast);
            if (room <= 0) continue;
            chosen.Add((pen, room));
            total += room;
            if (total >= count) break;
        }
        if (total < count) return null;

        var takes = new int[chosen.Count];
        var remainders = new double[chosen.Count];
        int assigned = 0;
        for (int i = 0; i < chosen.Count; i++)
        {
            double exact = (double)count * chosen[i].Room / total;
            takes[i] = Math.Min(chosen[i].Room, (int)Math.Floor(exact));
            remainders[i] = exact - takes[i];
            assigned += takes[i];
        }
        while (assigned < count)
        {
            int best = -1;
            for (int i = 0; i < chosen.Count; i++)
                if (takes[i] < chosen[i].Room && (best < 0 || remainders[i] > remainders[best]))
                    best = i;
            if (best < 0) return null;
            takes[best]++;
            remainders[best] = -1.0;
            assigned++;
        }
        var allocation = new List<(Pen Pen, int Count)>();
        for (int i = 0; i < chosen.Count; i++)
            if (takes[i] > 0) allocation.Add((chosen[i].Pen, takes[i]));
        return allocation;
    }

    /// <summary>The pen-room refusal of a site stocking, or null when its empty pens hold the fish.</summary>
    private CommandResult? CheckSitePenRoom(SeaSite site, int count, in PenDensityForecast forecast,
        out List<(Pen Pen, int Count)>? allocation)
    {
        allocation = AllocateToPens(site, count, forecast);
        if (allocation != null) return null;
        long room = SitePenRoom(site, forecast);
        return CommandResult.Fail(PenReasons.PensFull,
            $"The empty pens at {site.Id} take {room:N0} of {count:N0} fish within {MaxFishPerPen:N0} fish and " +
            $"{PenDensityLimitKgPerM3:F0} kg/m³ a pen");
    }

    // ─── Checks and previews ───────────────────────────────────────────────

    /// <summary>
    /// What stocking <paramref name="count"/> smolts of <paramref name="strainId"/>
    /// in one pen would do: the per-pen limits (§ 47a, § 25 on the plan's
    /// projected peak) and the S2-01a capacity check, with the most smolts the
    /// pen can take now. <paramref name="penKey"/> is the pen's id or number.
    /// </summary>
    public PenStockingPreview PreviewPenStocking(string siteId, string penKey, int count, string strainId = "standard",
        SmoltQualityTier qualityTier = SmoltQualityTier.Standard)
    {
        var site = State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return new PenStockingPreview(false, "site_not_found", $"Site '{siteId}' not found", "", 0, 0.0,
                PenFishLimit, PenDensityLimitKgPerM3, 0.0, State?.Day ?? 0, 0.0, 0);
        return EvaluatePenStocking(site, site.FindPen(penKey), penKey, Math.Max(0, count),
            SmoltStrainRegistry.Get(strainId), qualityTier, includeMaxCount: true);
    }

    private PenStockingPreview EvaluatePenStocking(SeaSite site, Pen? pen, string penKey, int count, SmoltStrain strain,
        SmoltQualityTier qualityTier, bool includeMaxCount)
    {
        int today = State.Day;
        double limit = PenDensityLimitKgPerM3;
        if (pen == null)
            return new PenStockingPreview(false, PenReasons.NotFound, $"Site '{site.Id}' has no pen '{penKey}'",
                "", 0, 0.0, PenFishLimit, limit, 0.0, today, 0.0, 0);

        var forecast = ForecastPenStocking(site, strain, qualityTier);
        double density = forecast.PeakDensityKgPerM3(count, pen.VolumeM3);
        int peakDay = today + forecast.PeakDay;
        int maxCount = 0;
        if (includeMaxCount && site.IsPenFree(pen))
        {
            int mtbRoom = EvaluateStocking(site, 0, strain.ReleaseWeightKg, includeMaxCount: true).MaxCount;
            maxCount = Math.Min(PenRoom(site, pen, forecast), mtbRoom);
        }

        PenStockingPreview Verdict(bool allowed, string key, string message)
            => new(allowed, key, message, pen.Id, pen.Number, pen.VolumeM3, PenFishLimit, limit, density, peakDay,
                forecast.WeightAtPeakKg, maxCount);

        if (!site.IsPenFree(pen))
            return Verdict(false, PenReasons.Occupied,
                $"Pen {pen.Number} at {site.Id} holds fish: one stocking per pen");
        if (count > PenFishLimit)
            return Verdict(false, PenReasons.FishLimit,
                $"{count:N0} fish in pen {pen.Number} is over the {MaxFishPerPen:N0} a production unit may hold");
        if (density > limit)
            return Verdict(false, PenReasons.Density,
                $"{count:N0} smolts in pen {pen.Number} ({pen.VolumeM3:N0} m³) peak at {density:F1} kg/m³ on day {peakDay}, " +
                $"over {limit:F0} kg/m³");
        var capacity = EvaluateStocking(site, count, strain.ReleaseWeightKg, includeMaxCount: false);
        if (!capacity.Allowed)
            return Verdict(false, capacity.ReasonKey, capacity.Message);
        return Verdict(true, CapacityReasons.None,
            $"Pen {pen.Number}: plan peak {density:F1} of {limit:F0} kg/m³ on day {peakDay}");
    }

    // ─── Stocking ──────────────────────────────────────────────────────────

    /// <summary>
    /// Stock <paramref name="count"/> smolts in one pen of a site (S2-01b): the
    /// player's stocking verb. Refused before any mutation when the pen is not
    /// found (pen_not_found) or holds fish (pen_occupied), when the count is
    /// over § 47a's 200,000 (pen_fish_limit), when the plan's projected peak
    /// density is over § 25's 25 kg/m³ (pen_density), by the S2-01a capacity
    /// check (mtb_*), or for want of money. Charges the smolts like
    /// <see cref="StockSmolts(string,int,string,SmoltQualityTier)"/>.
    /// </summary>
    public CommandResult StockSmoltsInPen(string siteId, string penKey, int count, string strainId = "standard",
        SmoltQualityTier qualityTier = SmoltQualityTier.Standard)
    {
        if (State?.SeaSites == null || Hatchery == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (count <= 0)
            return CommandResult.Fail("invalid_count", $"Smolt count must be positive (got {count})");
        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");

        var strain = SmoltStrainRegistry.Get(strainId);
        var verdict = EvaluatePenStocking(site, site.FindPen(penKey), penKey, count, strain, qualityTier, includeMaxCount: false);
        if (!verdict.Allowed)
            return CommandResult.Fail(verdict.ReasonKey, verdict.Message);
        // The old per-site check stays (an unregulated game has only this one).
        double incomingTonnes = count * strain.ReleaseWeightKg / 1000.0;
        if (site.CurrentBiomassTonnes + incomingTonnes > site.MABTonnes)
            return CommandResult.Fail("mab_exceeded",
                $"Stocking {count:N0} smolts ({incomingTonnes:F1} t) would put {siteId} over its MAB " +
                $"({site.CurrentBiomassTonnes:F1} + {incomingTonnes:F1} > {site.MABTonnes:F0} t)");

        var pen = site.FindPen(penKey)!;
        return PlaceSmolts(site, new List<(Pen, int)> { (pen, count) }, strain, SmoltQualityRegistry.Get(qualityTier));
    }

    /// <summary>
    /// Pay for and put <paramref name="allocation"/>'s smolts in their pens: one
    /// population per pen, the strain's lice load seeded once into the site's
    /// lice model, the smolts booked as cost of goods sold, one event-log entry.
    /// The checks are the caller's; only money can still refuse.
    /// </summary>
    internal CommandResult PlaceSmolts(SeaSite site, List<(Pen Pen, int Count)> allocation, SmoltStrain strain,
        SmoltQuality quality)
    {
        int count = allocation.Sum(a => a.Count);
        // Calibrated smolt price × strain premium (review F269: the old path
        // charged the hatchery rearing cost, ~54.75 NOK/fish vs 12 calibrated).
        decimal stockingCost = count * _balancing.SmoltCostPerFish * (decimal)strain.CostMultiplier;
        if (FinanceManager.Balance < stockingCost)
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {stockingCost:N0} NOK for {count:N0} smolts");

        foreach (var (pen, n) in allocation)
            PlacePopulation(site, pen, new FishPopulation.Cohort(Hatchery.MaturationDays, n, strain.ReleaseWeightKg,
                LifeStage.Smolt, strain.BuildGenome()) { Quality = quality.Tier }, strain.InitialLiceLoad);
        site.LastStockedDay = State.Day;

        // Phase 2.1: seed the SITE's lice model from the incoming smolts
        // (no Reset — that would wipe resistance built up by earlier
        // cohorts). Without this the model sits at 0 lice forever and
        // AdvanceDay early-outs.
        if (strain.InitialLiceLoad > 0)
            site.Lice.SeedExternalInfection(Math.Max(0.05, strain.InitialLiceLoad));

        // Smolts are cost of goods (review F1132: they never reached the P&L).
        PayCost(stockingCost, CostBooking.Cogs, "smolts");

        var message = SimText.Nb($"Satte ut {count:N0} smolt ({strain.Name}, {strain.ReleaseWeightKg * 1000:F0} g) på {site.Label}, {PenListText(allocation.Select(a => a.Pen))}");
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Utsett av smolt",
            Category = EventCategory.Equipment,
            Description = SimText.Nb($"{message} for {stockingCost:N0} kr"),
            BalanceChange = -stockingCost
        });

        return CommandResult.Success(message, count, id: site.Id);
    }

    /// <summary>
    /// Put a new population of <paramref name="cohort"/> in <paramref name="pen"/>
    /// and add it to the game. Returns the population. A fishless population
    /// still mapped to the pen (every fish died) loses the pen and stays in
    /// the site's list until a harvest clears empty populations, as before
    /// S2-01b (clearing it here moved the RNG streams of every later day).
    /// </summary>
    internal FishPopulation PlacePopulation(SeaSite site, Pen pen, FishPopulation.Cohort cohort, double liceLoad)
    {
        var population = new FishPopulation { ZoneId = site.ZoneId, SeaLiceLoad = liceLoad };
        population.InjectDependencies(Growth, Mortality);
        population.Cohorts.Add(cohort);
        site.Stock(population, pen);
        State.Populations.Add(population);
        return population;
    }

    /// <summary>"merd 3" or "merd 2, 3 og 5": the pens of a stocking for the event log.</summary>
    private static string PenListText(IEnumerable<Pen> pens)
    {
        var numbers = pens.Select(p => p.Number).OrderBy(n => n).ToList();
        if (numbers.Count == 0) return "";
        if (numbers.Count == 1) return $"merd {numbers[0]}";
        return $"merd {string.Join(", ", numbers.Take(numbers.Count - 1))} og {numbers[^1]}";
    }

    // ─── Views ─────────────────────────────────────────────────────────────

    /// <summary>The pens of a site in mooring order, as the game reads them (empty when the site is unknown).</summary>
    public IReadOnlyList<PenView> PenViewsOf(string siteId)
    {
        var site = State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return Array.Empty<PenView>();
        var views = new List<PenView>(site.PhysicalPens.Count);
        foreach (var pen in site.PhysicalPens.OrderBy(p => p.Number))
        {
            var population = site.PopulationIn(pen);
            int fish = population?.TotalCount ?? 0;
            double biomass = population?.GetTotalBiomassKg() ?? 0.0;
            double cv = population != null && fish > 0 ? SizeSpreadOf(new[] { population }).WeightCv : 0.0;
            views.Add(new PenView(pen.Id, pen.Number, fish > 0 ? population!.Id : "", fish, biomass,
                fish > 0 ? biomass / fish : 0.0, cv, population?.SeaLiceLoad ?? 0.0, pen.DensityKgPerM3(biomass),
                pen.VolumeM3, pen.CircumferenceM, pen.NetDepthM, population?.Ration?.EffectiveFraction ?? 1.0,
                population?.IsFasting ?? false));
        }
        return views;
    }

    // ─── Per-pen verbs ─────────────────────────────────────────────────────

    /// <summary>
    /// The populations of <paramref name="site"/> named by <paramref name="keys"/>:
    /// a pen's id or number (S2-01b) or a population id (S2-04's pen ids).
    /// An empty pen names no fish. Null with the key when one names nothing at
    /// the site.
    /// </summary>
    internal List<FishPopulation>? PopulationsNamed(SeaSite site, IEnumerable<string> keys, out string unknownKey)
    {
        unknownKey = "";
        var populations = new List<FishPopulation>();
        foreach (var key in keys.Distinct())
        {
            var population = site.Pens.FirstOrDefault(p => p.Id == key);
            if (population == null)
            {
                var pen = site.FindPen(key);
                if (pen == null) { unknownKey = key; return null; }
                population = site.PopulationIn(pen);
            }
            if (population != null && !populations.Contains(population)) populations.Add(population);
        }
        return populations;
    }

    /// <summary>
    /// Delouse chosen pens of a site (S2-01b): only their fish are handled
    /// (handling deaths, injuries, cost per fish treated), and the site's lice
    /// model, a mean over all the site's fish, loses the method's kill on the
    /// treated share of the fish. Gill damage lands in the same share. With
    /// every pen chosen it is <see cref="DelouseSite"/>. Fails before any
    /// mutation: unknown_method / site_not_found / pen_not_found / site_empty /
    /// insufficient_funds.
    /// </summary>
    public CommandResult DelousePens(string siteId, string method, IReadOnlyCollection<string> penKeys)
    {
        if (penKeys == null || penKeys.Count == 0)
            return DelouseSite(siteId, method);
        if (!DelousingSystem.TryParseMethodName(method, out var delousingMethod))
            return CommandResult.Fail("unknown_method", $"Invalid delousing method '{method}'");
        if (State?.SeaSites == null || SeaLice == null || IntegratedDisease == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");
        var pens = PopulationsNamed(site, penKeys, out var unknown);
        if (pens == null)
            return CommandResult.Fail(PenReasons.NotFound, $"Site '{siteId}' has no pen '{unknown}'");
        pens = pens.Where(p => p.TotalCount > 0).ToList();
        int treatedFish = pens.Sum(p => p.TotalCount);
        int siteFish = site.Pens.Sum(p => Math.Max(0, p.TotalCount));
        if (treatedFish <= 0)
            return CommandResult.Fail("site_empty", $"The chosen pens at '{siteId}' have no fish to treat");
        if (treatedFish >= siteFish)
            return DelouseSite(siteId, method);

        var profile = DelousingSystem.GetProfile(delousingMethod);
        decimal estimate = treatedFish * profile.CostPerFish;
        if (FinanceManager.Balance < estimate)
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {estimate:N0} NOK to treat {treatedFish:N0} fish");

        double share = (double)treatedFish / siteFish;
        var outcome = ApplySiteDelousing(site, delousingMethod, SeaTemperatureAtSite(site),
            ComputeSiteTreatmentMortalityMultiplier(site), pens, share);
        if (!outcome.Applied)
            return CommandResult.Fail("cancelled", $"Treatment cancelled: {outcome.CancelReason}");

        PayCost(outcome.Cost, CostBooking.Opex, "delousing");
        site.GillScore = Math.Max(0.0, site.GillScore - outcome.GillDamage * 50.0 * share);
        var treatedPens = pens.Select(p => site.PenOf(p)).Where(p => p != null).Select(p => p!);
        string description = SimText.Nb($"Avlusing ({DelousingName(delousingMethod)}) på {site.Label}, {PenListText(treatedPens)}: {outcome.TotalFish:N0} fisk behandlet, {profile.Efficacy:P0} virkning på {share:P0} av fisken, {outcome.TotalDeaths:N0} døde under håndteringen");
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Delousing Treatment",
            Category = EventCategory.Disease,
            Description = description,
            BalanceChange = -outcome.Cost
        });
        return CommandResult.Success(description, outcome.TotalFish, outcome.TotalDeaths, siteId);
    }
}
