using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The post-smolt bridge on seawater (roadmap F2-13, docs/FARM_TYPES_SPEC.md
/// §2B-2C, §3.3, §3.4, §6.5 "Post-smolt planner"): a land tank's fish carry a
/// quality tier to sea by their weight and their salinity record (SuperSmolt
/// from 400 g after 30 days at ≥ 25 ppt, large from 200 g), whose mortality
/// and growth carryover the sea phase applies (the hooks in
/// <c>Farm/Land/LandHooks.cs</c>); a pad with a river intake blends its
/// post-smolt water down (<see cref="SetSalinity"/>; under 12 ppt for more than
/// 60 days the fish desmoltify and die twice as often in the transfer); the
/// player moves a tank's fish to a fjord site by wellboat
/// (<see cref="TransferTankToSea"/>, a "transfer" job of 1-3 days, the MTB
/// checked when booked and when the boat comes) or sells them
/// (<see cref="SellPostSmolt"/>).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The post-smolt bridge's calibration (<c>facilities.land.smolt</c>).</summary>
    private LandSmoltCalibration SmoltBridge => LandCalibration.Smolt;

    // ── Salinity ───────────────────────────────────────────────────────────

    /// <summary>True when the pad under <paramref name="site"/> has a river intake to blend its water with.</summary>
    public bool LandPadHasRiverIntake(RASFacility site)
        => site != null && (FindPlot(site.PlotId) ?? SitePlots.FirstOrDefault(p => p.Id == site.PlotId))?.HasRiverIntake == true;

    /// <summary>
    /// Salinity of a land site's tank water (ppt): a post-smolt site on a pad
    /// with a river intake runs its blend (<see cref="RASFacility.SalinityPpt"/>);
    /// every other site draws full-strength seawater.
    /// </summary>
    public double LandSalinityOf(RASFacility site)
    {
        if (site == null || !site.HasLots || site.Kind != LandSiteKind.PostSmolt || !LandPadHasRiverIntake(site))
            return HoldingWater.SeawaterSalinityPsu;
        return double.IsFinite(site.SalinityPpt)
            ? Math.Clamp(site.SalinityPpt, SmoltBridge.MinBlendSalinityPpt, SmoltBridge.SeawaterSalinityPpt)
            : HoldingWater.SeawaterSalinityPsu;
    }

    /// <summary>
    /// The H2S sulphate factor of a land site's water (spec §3.3: salinity /
    /// 35, [V:6]): the share of seawater's sulphate its biofilm can reduce.
    /// F2-16's H2S index multiplies its daily rise by it.
    /// </summary>
    public double LandSulphateFactorOf(RASFacility site) => PostSmoltBridge.SulphateFactor(LandSalinityOf(site), SmoltBridge);

    /// <summary>
    /// Set the salinity of a post-smolt site's water (spec §3.4 <c>SetSalinity</c>):
    /// a blend of river and fjord water, from the calibration's floor to full
    /// seawater. Fails: not_found, wrong_site_kind (a grow-out site),
    /// no_river_intake (the pad has none), out_of_range. Takes effect in the
    /// next land tick.
    /// </summary>
    public CommandResult SetSalinity(string siteId, double salinityPpt)
    {
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        if (site.Kind != LandSiteKind.PostSmolt)
            return CommandResult.Fail(LandSiteReasons.WrongSiteKind, $"'{site.Name}' is a grow-out site: its water is seawater");
        if (!LandPadHasRiverIntake(site))
            return CommandResult.Fail(LandSiteReasons.NoRiverIntake, $"The pad under '{site.Name}' has no river intake");
        if (!(salinityPpt >= SmoltBridge.MinBlendSalinityPpt && salinityPpt <= SmoltBridge.SeawaterSalinityPpt))
            return CommandResult.Fail(LandSiteReasons.OutOfRange,
                $"Salinity {SmoltBridge.MinBlendSalinityPpt:F0}-{SmoltBridge.SeawaterSalinityPpt:F0} ppt");
        site.SalinityPpt = salinityPpt;
        return CommandResult.Success($"{site.Name} runs at {salinityPpt:F1} ppt", salinityPpt, id: site.Id);
    }

    // ── Tiers ──────────────────────────────────────────────────────────────

    /// <summary>The tier the fish in <paramref name="tank"/> would carry to sea today (Standard when it is empty).</summary>
    public SmoltQualityTier PostSmoltTierOf(RASFacility site, LandTank tank)
    {
        var fish = site?.PopulationIn(tank);
        if (fish == null || fish.TotalCount <= 0) return SmoltQualityTier.Standard;
        return PostSmoltBridge.TierOf(fish.GetTotalBiomassKg() / fish.TotalCount, tank.SeawaterDays, SmoltBridge);
    }

    /// <summary>True when the fish in <paramref name="tank"/> have desmoltified (more than 60 days in a row under 12 ppt).</summary>
    public bool IsTankDesmoltified(LandTank tank)
        => tank != null && PostSmoltBridge.IsDesmoltified(tank.LowSalinityDays, SmoltBridge);

    /// <summary>
    /// The day's salinity record of every stocked tank (the land tick): a day
    /// at or above the SuperSmolt salinity extends the seawater record, any
    /// other day ends it; a day under the desmoltification salinity extends
    /// the low-salinity run, any other day ends it.
    /// </summary>
    private void NoteLandSalinityDay(RASFacility site)
    {
        double salinity = LandSalinityOf(site);
        bool seawater = PostSmoltBridge.CountsAsSeawater(salinity, SmoltBridge);
        bool low = PostSmoltBridge.DesmoltifiesAt(salinity, SmoltBridge);
        foreach (var tank in site.AllTanks())
        {
            var fish = site.PopulationIn(tank);
            if (fish == null || fish.TotalCount <= 0) continue;
            tank.SeawaterDays = seawater ? tank.SeawaterDays + 1 : 0;
            tank.LowSalinityDays = low ? tank.LowSalinityDays + 1 : 0;
        }
    }

    // ── Transfer to sea ────────────────────────────────────────────────────

    /// <summary>
    /// What moving <paramref name="count"/> fish (0 = the whole tank) of a land
    /// tank to sea site <paramref name="seaSiteId"/> would do, and whether it may
    /// be booked: the tier, the wellboat days and hire, the handling deaths, and
    /// the destination's checks (its MTB, the S2-01a plan, its empty pens). A
    /// pure read; <see cref="TransferTankToSea"/> runs the same checks.
    /// </summary>
    public LandTransferPreview PreviewTransferTankToSea(string siteId, string tankId, string seaSiteId, int count)
    {
        if (State == null || FinanceManager == null)
            return LandTransferPreview.Refused("engine_not_ready", "Engine not initialized");
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return LandTransferPreview.Refused(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        var tank = site.FindTank(tankId);
        if (tank == null)
            return LandTransferPreview.Refused(LandSiteReasons.TankNotFound, $"No tank '{tankId}' at '{site.Name}'");
        var fish = site.PopulationIn(tank);
        if (fish == null || fish.TotalCount <= 0)
            return LandTransferPreview.Refused("holding_empty", $"{tank.Id} holds no fish") with { TankId = tank.Id };

        var cal = SmoltBridge;
        int moving = count == 0 ? fish.TotalCount : count;
        double weightKg = fish.GetTotalBiomassKg() / fish.TotalCount;
        var tier = PostSmoltBridge.TierOf(weightKg, tank.SeawaterDays, cal);
        bool desmolt = IsTankDesmoltified(tank);
        double mortality = PostSmoltBridge.TransferMortalityShare(weightKg, desmolt, CrewEffects.HasRole(State, "veterinaer"), cal);
        double tonnes = Math.Max(0, moving) * weightKg / 1000.0;
        int days = PostSmoltBridge.TransferDays(tonnes, cal);
        decimal hire = cal.TransferHireNokPerDay * days;
        var strain = SmoltStrainRegistry.Get(string.IsNullOrEmpty(tank.StrainId) ? "standard" : tank.StrainId);
        int deaths = (int)Math.Round(Math.Max(0, moving) * mortality, MidpointRounding.AwayFromZero);
        var preview = new LandTransferPreview(true, "", "", site.Id, tank.Id, seaSiteId ?? "", Math.Max(0, moving), weightKg, tier,
            tank.SeawaterDays, tank.LowSalinityDays, desmolt, days, hire, State.Day + days, mortality, deaths,
            Math.Max(0, moving) * SmoltTierCostPerFish(tier, strain, SmoltSource.Purchased),
            (decimal)(Math.Max(0, moving) * weightKg) * cal.SalePriceNokPerKgLive,
            strain.Id, SeaDaysSavedBy(seaSiteId, weightKg, tier, strain));

        if (moving <= 0 || moving > fish.TotalCount)
            return preview with { Allowed = false, Reason = LandSiteReasons.OutOfRange, Message = $"Move 1-{fish.TotalCount:N0} fish" };
        if (weightKg < cal.MinTransferWeightKg)
            return preview with { Allowed = false, Reason = LandSiteReasons.BelowMinWeight, Message =
                $"The fish weigh {weightKg * 1000.0:F0} g, under the {cal.MinTransferWeightKg * 1000.0:F0} g of a smolt" };
        var sea = State.SeaSites.FirstOrDefault(s => s.Id == seaSiteId);
        if (sea == null)
            return preview with { Allowed = false, Reason = LandSiteReasons.SiteNotFound, Message = $"No sea site '{seaSiteId}'" };
        if (sea.Kind != SiteKind.Fjord)
            return preview with { Allowed = false, Reason = LandSiteReasons.OffshoreUnit, Message =
                $"'{sea.Label}' is an offshore unit: smolts go in through its own verb" };
        if (site.Transfers.Any(j => j.TankId == tank.Id) || days > cal.MaxTransferDays)
            return preview with { Allowed = false, Reason = LandSiteReasons.NoWellboatSlot, Message = days > cal.MaxTransferDays
                ? $"{tonnes:N0} t take {days} wellboat days, more than a slot's {cal.MaxTransferDays}"
                : $"A wellboat is already booked for {tank.Id}" };
        var destination = CheckTransferDestination(sea, moving - deaths, weightKg, tier, strain, PendingTransferTonnesTo(sea.Id));
        if (destination != null)
            return preview with { Allowed = false, Reason = destination.ErrorKey ?? "", Message = destination.Message };
        if (FinanceManager.Balance < hire)
            return preview with { Allowed = false, Reason = LandSiteReasons.InsufficientFunds, Message = $"Need {hire:N0} NOK for the wellboat" };
        return preview with { Message = $"{moving:N0} fish ({tier}) to {sea.Label} in {days} d" };
    }

    /// <summary>
    /// Book a wellboat to move <paramref name="count"/> fish (0 = the whole
    /// tank) of a land tank to a fjord site (spec §3.4 <c>TransferTankToSea</c>,
    /// WellboatJob "transfer"): the hire is paid now; the boat comes in 1-3 days
    /// (one a started 450 t) and the fish go into the site's empty pens at
    /// their weight, age, genome and vaccination, with the tier they have that
    /// day and no lice; the handling kills its share (twice it for desmoltified
    /// fish). The destination's MTB is checked now and again when the boat
    /// comes. Fails: as <see cref="PreviewTransferTankToSea"/>. Id = the job.
    /// </summary>
    public CommandResult TransferTankToSea(string siteId, string tankId, string seaSiteId, int count)
    {
        if (FindLandSite(siteId) is { HasLots: true } synced) SyncLandSite(synced);
        var preview = PreviewTransferTankToSea(siteId, tankId, seaSiteId, count);
        if (!preview.Allowed)
            return CommandResult.Fail(preview.Reason, preview.Message);
        var site = FindLandSite(siteId)!;
        var tank = site.FindTank(tankId)!;
        var fish = site.PopulationIn(tank)!;
        if (!TryPayCost(preview.HireNok, CostBooking.Opex, "wellboat"))
            return CommandResult.Fail(LandSiteReasons.InsufficientFunds, $"Need {preview.HireNok:N0} NOK for the wellboat");
        site.Account.TransferNok += preview.HireNok;

        State.WellboatJobsBooked++;
        var job = new LandTransferJob
        {
            Boat = new Logistics.WellboatJob
            {
                SiteId = site.Id,
                Kind = LandTransferJob.Kind,
                BookedDay = State.Day,
                Sequence = State.WellboatJobsBooked,
                DueDay = State.Day + preview.Days,
                HireCost = preview.HireNok,
            },
            TankId = tank.Id,
            PopulationId = fish.Id,
            DestinationSiteId = preview.SeaSiteId,
            Fish = preview.Fish,
            Days = preview.Days,
            StrainId = preview.StrainId,
            BookedWeightKg = preview.MeanWeightKg,
            BookedTier = preview.Tier,
        };
        site.Transfers.Add(job);
        var sea = State.SeaSites.First(s => s.Id == preview.SeaSiteId);
        LogFacilityEvent("Brønnbåt bestilt til postsmolt",
            SimText.Nb($"{site.Name}: {preview.Fish:N0} fisk ({preview.MeanWeightKg * 1000.0:F0} g) fra {tank.Id} til {sea.Label}, brønnbåten kommer om {preview.Days} d"),
            -preview.HireNok);
        return CommandResult.Success($"Transfer of {preview.Fish:N0} fish from {tank.Id} to {sea.Label} booked for day {job.Boat.DueDay}",
            preview.Fish, job.Boat.DueDay, job.Id);
    }

    /// <summary>
    /// The transfers whose boat comes today (the land tick, after the day's
    /// water): each runs if the fish are still there and the destination still
    /// takes them (its MTB, the plan, its pens); otherwise it fails, the fish
    /// stay, and the hire is spent. A day the destination's sea rules out the
    /// handling (F2 hook <see cref="OperationPossible"/>) the boat waits a day.
    /// </summary>
    private void RunDueLandTransfers(RASFacility site)
    {
        if (site.Transfers.Count == 0) return;
        foreach (var job in site.Transfers.ToList())
        {
            // The boat comes on its due day, as a wellboat job does: in the
            // step that ends on DueDay (the in-day day is DueDay - 1).
            if (job.Boat.DueDay > State.Day + 1) continue;
            var sea = State.SeaSites.FirstOrDefault(s => s.Id == job.DestinationSiteId);
            if (sea != null && !OperationPossible(sea, LandTransferJob.Kind, State.Day)) continue;
            site.Transfers.Remove(job);
            var outcome = ExecuteLandTransfer(site, job, sea);
            _dayNotes.Add(new DayNote
            {
                Kind = "wellboat_transfer",
                SiteId = site.Id,
                Success = outcome.Ok,
                Message = outcome.Message,
            });
        }
    }

    private CommandResult ExecuteLandTransfer(RASFacility site, LandTransferJob job, SeaSite? sea)
    {
        var tank = site.FindTank(job.TankId);
        var fish = tank == null ? null : site.PopulationIn(tank);
        CommandResult Failed(string key, string message)
        {
            LogFacilityEvent("Brønnbåt returnerte", SimText.Nb($"{site.Name}: flyttingen fra {job.TankId} ble ikke gjennomført ({message})"), 0m);
            return CommandResult.Fail(key, message);
        }
        if (sea == null)
            return Failed(LandSiteReasons.SiteNotFound, $"no sea site '{job.DestinationSiteId}'");
        if (tank == null || fish == null || fish.TotalCount <= 0 || fish.Id != job.PopulationId)
            return Failed("holding_empty", $"{job.TankId} no longer holds the booked fish");

        var cal = SmoltBridge;
        int moving = Math.Min(job.Fish, fish.TotalCount);
        double weightKg = fish.GetTotalBiomassKg() / fish.TotalCount;
        var tier = PostSmoltBridge.TierOf(weightKg, tank.SeawaterDays, cal);
        bool desmolt = IsTankDesmoltified(tank);
        double mortality = PostSmoltBridge.TransferMortalityShare(weightKg, desmolt, CrewEffects.HasRole(State, "veterinaer"), cal);
        var strain = SmoltStrainRegistry.Get(string.IsNullOrEmpty(job.StrainId) ? "standard" : job.StrainId);

        // Each cohort's share of the fish moves; the handling kills its share first.
        var takes = ShareOfCohorts(fish, moving);
        int deaths = takes.Sum(t => (int)Math.Round(t.Count * mortality, MidpointRounding.AwayFromZero));
        int arriving = takes.Sum(t => t.Count) - deaths;
        var check = CheckTransferDestination(sea, arriving, weightKg, tier, strain, PendingTransferTonnesTo(sea.Id));
        if (check != null)
            return Failed(check.ErrorKey ?? "", check.Message);
        var forecast = TransferForecast(sea, weightKg, tier, strain);
        var allocation = AllocateToPens(sea, arriving, forecast);
        if (allocation == null)
            return Failed(PenReasons.PensFull, $"the empty pens at {sea.Label} no longer take {arriving:N0} fish");

        int ageDays = 0, dead = 0, moved = 0;
        double cvWeighted = 0.0;
        Genome? genome = null;
        int largest = -1;
        foreach (var (cohort, n) in takes)
        {
            if (n <= 0) continue;
            int lost = (int)Math.Round(n * mortality, MidpointRounding.AwayFromZero);
            dead += fish.RemoveDead(cohort, lost, MortalityCause.Handling);
            int left = fish.RemoveAlive(cohort, n - lost, FishRemoval.TransferOut);
            moved += left;
            cvWeighted += left * cohort.WeightCv;
            if (left > largest) { largest = left; ageDays = cohort.AgeDays; genome = cohort.Genetics; }
        }
        if (moved <= 0)
            return Failed("holding_empty", "every fish died in the handling");
        allocation = AllocateToPens(sea, moved, forecast) ?? allocation;
        double cv = cvWeighted / moved;
        foreach (var (pen, n) in allocation)
        {
            if (n <= 0) continue;
            var placed = PlacePopulation(sea, pen, new FishPopulation.Cohort(ageDays, n, weightKg, LifeStage.Smolt, genome ?? strain.BuildGenome())
            {
                Quality = tier,
                WeightCv = cv,
            }, 0.0); // land fish carry no lice to sea
            placed.VaccinatedDay = fish.VaccinatedDay;
        }
        sea.LastStockedDay = State.Day;
        site.Account.TransferredFish += moved;
        if (fish.TotalCount <= 0)
        {
            site.Tanks.Remove(fish);
            State.Populations.Remove(fish);
            tank.Pen.PopulationId = "";
            tank.ClearFishRecord();
        }
        LogFacilityEvent("Postsmolt flyttet til sjø",
            SimText.Nb($"{site.Name}: {moved:N0} postsmolt ({weightKg * 1000.0:F0} g, {TierName(tier)}) fra {job.TankId} til {sea.Label}, {dead:N0} døde under håndteringen"), 0m);
        return CommandResult.Success($"Moved {moved:N0} post-smolt ({tier}) from {job.TankId} to {sea.Label}", moved, dead, sea.Id);
    }

    /// <summary>Norwegian name of a smolt tier for the event log.</summary>
    private static string TierName(SmoltQualityTier tier) => tier switch
    {
        SmoltQualityTier.SuperSmolt => "supersmolt",
        SmoltQualityTier.Large => "storsmolt",
        _ => "standard smolt",
    };

    /// <summary>
    /// The destination's refusal of <paramref name="fish"/> fish of
    /// <paramref name="weightKg"/> at <paramref name="tier"/>, or null: its MTB
    /// with the transfers already booked to it, the S2-01a plan's peak, its
    /// empty pens under § 47a and § 25.
    /// </summary>
    private CommandResult? CheckTransferDestination(SeaSite sea, int fish, double weightKg, SmoltQualityTier tier, SmoltStrain strain,
        double pendingTonnes)
    {
        double incomingTonnes = fish * weightKg / 1000.0;
        if (sea.CurrentBiomassTonnes + pendingTonnes + incomingTonnes > sea.MABTonnes)
            return CommandResult.Fail(LandSiteReasons.MabExceeded,
                $"{incomingTonnes:F1} t would put {sea.Label} over its MTB ({sea.CurrentBiomassTonnes + pendingTonnes:F1} + {incomingTonnes:F1} > {sea.MABTonnes:F0} t)");
        var capacity = CheckStockingCapacity(sea, fish, weightKg);
        if (capacity != null) return capacity;
        return CheckSitePenRoom(sea, fish, TransferForecast(sea, weightKg, tier, strain), out _);
    }

    /// <summary>The S2-01b plan of fish of <paramref name="weightKg"/> at their tier and genome stocked at <paramref name="sea"/> today.</summary>
    private PenDensityForecast TransferForecast(SeaSite sea, double weightKg, SmoltQualityTier tier, SmoltStrain strain)
        => PenDensityPlanner.Project(Growth?.Law ?? TgcGrowthLaw.Default,
            PenPlanFor(sea, weightKg, strain.BuildGenome().GetGrowthMultiplier() * SmoltQualityRegistry.Get(tier).GrowthMultiplier));

    /// <summary>Live tonnes every land site has booked to sea site <paramref name="seaSiteId"/> and not yet moved.</summary>
    private double PendingTransferTonnesTo(string seaSiteId)
        => LandSites.SelectMany(s => s.Transfers).Where(j => j.DestinationSiteId == seaSiteId).Sum(j => j.Fish * j.BookedWeightKg / 1000.0);

    /// <summary>Each cohort's share of <paramref name="count"/> fish of <paramref name="population"/> (the remainder to the largest cohorts first).</summary>
    private static List<(FishPopulation.Cohort Cohort, int Count)> ShareOfCohorts(FishPopulation population, int count)
    {
        var live = population.Cohorts.Where(c => c.Count > 0).ToList();
        int total = live.Sum(c => c.Count);
        var takes = new List<(FishPopulation.Cohort, int)>();
        if (total <= 0 || count <= 0) return takes;
        if (count >= total) return live.Select(c => (c, c.Count)).ToList();
        int assigned = 0;
        var counts = new int[live.Count];
        for (int i = 0; i < live.Count; i++)
        {
            counts[i] = (int)Math.Floor((double)count * live[i].Count / total);
            assigned += counts[i];
        }
        foreach (int i in Enumerable.Range(0, live.Count).OrderByDescending(i => live[i].Count).ThenBy(i => i))
        {
            if (assigned >= count) break;
            if (counts[i] < live[i].Count) { counts[i]++; assigned++; }
        }
        for (int i = 0; i < live.Count; i++)
            takes.Add((live[i], counts[i]));
        return takes;
    }

    /// <summary>
    /// Sea days a fish of <paramref name="weightKg"/> at <paramref name="tier"/>
    /// saves at <paramref name="seaSiteId"/> against a standard 100 g smolt:
    /// the S2-01a plan's days to the harvest weight for each (0 without a site).
    /// The planner's "lice cycles saved at sea" read off the plan.
    /// </summary>
    private int SeaDaysSavedBy(string? seaSiteId, double weightKg, SmoltQualityTier tier, SmoltStrain strain)
    {
        var sea = State?.SeaSites?.FirstOrDefault(s => s.Id == seaSiteId);
        if (sea == null || sea.Kind != SiteKind.Fjord) return 0;
        var law = Growth?.Law ?? TgcGrowthLaw.Default;
        double genome = strain.BuildGenome().GetGrowthMultiplier();
        var standard = PenDensityPlanner.Project(law, PenPlanFor(sea, SmoltStrainRegistry.Get("standard").ReleaseWeightKg, genome));
        var own = TransferForecast(sea, weightKg, tier, strain);
        return Math.Max(0, standard.PeakDay - own.PeakDay);
    }

    // ── Sale ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sell <paramref name="count"/> fish (0 = the whole tank) of a post-smolt
    /// site's tank alive to another farmer (spec §3.4 <c>SellPostSmolt</c>): the
    /// calibration's price a kg live weight, booked as revenue
    /// (<c>post_smolt_sales</c>, a land site's: out of the rent base); the fish
    /// leave the tank through the mortality ledger as moved alive. Fails:
    /// not_found, wrong_site_kind (a grow-out site harvests), tank_not_found,
    /// holding_empty, out_of_range, below_min_weight.
    /// </summary>
    public CommandResult SellPostSmolt(string siteId, string tankId, int count)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        if (site.Kind != LandSiteKind.PostSmolt)
            return CommandResult.Fail(LandSiteReasons.WrongSiteKind, $"'{site.Name}' is a grow-out site: harvest its fish");
        SyncLandSite(site);
        var tank = site.FindTank(tankId);
        if (tank == null)
            return CommandResult.Fail(LandSiteReasons.TankNotFound, $"No tank '{tankId}' at '{site.Name}'");
        var fish = site.PopulationIn(tank);
        if (fish == null || fish.TotalCount <= 0)
            return CommandResult.Fail("holding_empty", $"{tank.Id} holds no fish");
        int selling = count == 0 ? fish.TotalCount : count;
        if (selling <= 0 || selling > fish.TotalCount)
            return CommandResult.Fail(LandSiteReasons.OutOfRange, $"Sell 1-{fish.TotalCount:N0} fish");
        double weightKg = fish.GetTotalBiomassKg() / fish.TotalCount;
        if (weightKg < SmoltBridge.MinTransferWeightKg)
            return CommandResult.Fail(LandSiteReasons.BelowMinWeight,
                $"The fish weigh {weightKg * 1000.0:F0} g, under the {SmoltBridge.MinTransferWeightKg * 1000.0:F0} g of a smolt");

        double soldKg = 0.0;
        int sold = 0;
        foreach (var (cohort, n) in ShareOfCohorts(fish, selling))
        {
            double kg = cohort.MeanWeightKg;
            int gone = fish.RemoveAlive(cohort, n, FishRemoval.TransferOut);
            sold += gone;
            soldKg += gone * kg;
        }
        decimal revenue = (decimal)soldKg * SmoltBridge.SalePriceNokPerKgLive;
        ReceiveIncome(revenue, "post_smolt_sales");
        State.TotalSalesRevenueNok += revenue;
        site.Account.PostSmoltRevenueNok += revenue;
        site.Account.SoldLiveKg += soldKg;
        if (fish.TotalCount <= 0)
        {
            site.Tanks.Remove(fish);
            State.Populations.Remove(fish);
            tank.Pen.PopulationId = "";
            tank.ClearFishRecord();
        }
        LogFacilityEvent("Postsmolt solgt",
            SimText.Nb($"{site.Name}: solgte {sold:N0} postsmolt ({weightKg * 1000.0:F0} g) fra {tank.Id} for {revenue:N0} kr"), revenue);
        return CommandResult.Success($"Sold {sold:N0} post-smolt from {tank.Id} for {revenue:N0} NOK", sold, (double)revenue, tank.Id);
    }
}

/// <summary>What moving a land tank's fish to sea would do (<see cref="SimulationEngine.PreviewTransferTankToSea"/>).</summary>
/// <param name="Allowed">True when the transfer may be booked.</param>
/// <param name="Reason">Refusal key, "" when allowed.</param>
/// <param name="Message">Why, for logs.</param>
/// <param name="SiteId">The land site.</param>
/// <param name="TankId">The tank.</param>
/// <param name="SeaSiteId">The destination.</param>
/// <param name="Fish">Fish to move.</param>
/// <param name="MeanWeightKg">Their mean live weight today (kg).</param>
/// <param name="Tier">The tier they would carry to sea today.</param>
/// <param name="SeawaterDays">Days in a row at or above the SuperSmolt salinity.</param>
/// <param name="LowSalinityDays">Days in a row under the desmoltification salinity.</param>
/// <param name="Desmoltified">True when the fish have desmoltified.</param>
/// <param name="Days">Wellboat days (1-3 a slot).</param>
/// <param name="HireNok">The wellboat hire.</param>
/// <param name="DueDay">Day the boat comes if booked today.</param>
/// <param name="MortalityShare">Share the handling kills.</param>
/// <param name="ExpectedDeaths">Fish the handling kills.</param>
/// <param name="BoughtValueNok">What the same fish would cost bought at their tier (the own source's saving).</param>
/// <param name="SaleValueNok">What selling them alive instead would bring.</param>
/// <param name="StrainId">The strain the fish were stocked as.</param>
/// <param name="SeaDaysSaved">Sea days their tier and weight save at the destination against a standard smolt (the plan's).</param>
public sealed record LandTransferPreview(bool Allowed, string Reason, string Message, string SiteId, string TankId, string SeaSiteId,
    int Fish, double MeanWeightKg, SmoltQualityTier Tier, int SeawaterDays, int LowSalinityDays, bool Desmoltified, int Days,
    decimal HireNok, int DueDay, double MortalityShare, int ExpectedDeaths, decimal BoughtValueNok, decimal SaleValueNok,
    string StrainId, int SeaDaysSaved)
{
    /// <summary>A refusal before the tank's fish are known.</summary>
    public static LandTransferPreview Refused(string reason, string message)
        => new(false, reason, message, "", "", "", 0, 0.0, SmoltQualityTier.Standard, 0, 0, false, 0, 0m, 0, 0.0, 0, 0m, 0m, "", 0);
}
