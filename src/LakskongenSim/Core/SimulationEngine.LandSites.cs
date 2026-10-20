using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>Why a land verb refused (spec §3.4's fail codes; text keys <c>LAND_REASON_*</c>).</summary>
public static class LandSiteReasons
{
    /// <summary>No land site, lot or project with that id.</summary>
    public const string NotFound = "not_found";
    /// <summary>No tank with that id at the site.</summary>
    public const string TankNotFound = "tank_not_found";
    /// <summary>The tank is not built yet (its lot is under construction).</summary>
    public const string NotOperational = "not_operational";
    /// <summary>The lot's biofilter has not matured yet.</summary>
    public const string BiofilterImmature = "biofilter_immature";
    /// <summary>The tank already holds fish.</summary>
    public const string TankOccupied = "tank_occupied";
    /// <summary>The stocking's projected peak passes the tank's density limit.</summary>
    public const string DensityExceeded = LandStockingPlanner.DensityExceeded;
    /// <summary>The stocking would plan more than a year's nameplate production in a rolling year.</summary>
    public const string AnnualCapacity = "annual_capacity";
    /// <summary>A number outside its range.</summary>
    public const string OutOfRange = "out_of_range";
    /// <summary>Not enough cash.</summary>
    public const string InsufficientFunds = "insufficient_funds";
    /// <summary>The farm type is still locked (research, an operating module, a zoned pad).</summary>
    public const string NotUnlocked = "not_unlocked";
    /// <summary>Another unit holds the plot, or the plot is no land pad.</summary>
    public const string PlotTaken = "plot_taken";
    /// <summary>A permit the build needs is not approved.</summary>
    public const string PermitsMissing = "permits_missing";
    /// <summary>The pad's grid cannot carry the build's electrical load.</summary>
    public const string GridCapacity = "grid_capacity";
    /// <summary>The pad holds no more lots or modules of this kind.</summary>
    public const string LotsExhausted = "lots_exhausted";
    /// <summary>The moratorium flag refuses new land applications.</summary>
    public const string Moratorium = "moratorium";
    /// <summary>A case of this kind is already pending on the plot.</summary>
    public const string AlreadyPending = "already_pending";
    /// <summary>The case is not refused (only a refusal can be appealed or re-applied).</summary>
    public const string NotRefused = "not_refused";
    /// <summary>The site or project is not in a state the verb acts on.</summary>
    public const string NotReady = "not_ready";
    /// <summary>Nothing to apply for: every permit the plan needs is approved or pending.</summary>
    public const string NothingToApply = "nothing_to_apply";
    /// <summary>The upgrade is already in place.</summary>
    public const string AlreadyActive = "already_active";
    /// <summary>The stocking would plan more than the site's competence lets it run (F2-15's utilisation cap).</summary>
    public const string UtilCap = "util_cap";
    /// <summary>The pad has no river intake to blend the water with (F2-13).</summary>
    public const string NoRiverIntake = "no_river_intake";
    /// <summary>The fish are lighter than a smolt that may go to sea or be sold (F2-13).</summary>
    public const string BelowMinWeight = "below_min_weight";
    /// <summary>The transfer needs more wellboat days than a slot holds, or the tank already has a boat booked (F2-13).</summary>
    public const string NoWellboatSlot = "no_wellboat_slot";
    /// <summary>The verb belongs to the other land site kind (grow-out or post-smolt).</summary>
    public const string WrongSiteKind = "wrong_site_kind";
    /// <summary>No sea site with that id.</summary>
    public const string SiteNotFound = "site_not_found";
    /// <summary>The destination is an offshore unit: its own verbs stock it.</summary>
    public const string OffshoreUnit = "offshore_unit";
    /// <summary>The fish would put the destination over its MTB.</summary>
    public const string MabExceeded = "mab_exceeded";
    /// <summary>The planned week cannot take the harvest: it is past, too close, too far ahead, or the hall is full that week (F2-15).</summary>
    public const string NoSlot = "no_slot";
    /// <summary>The tank is already in another harvest plan (F2-15).</summary>
    public const string AlreadyPlanned = "already_planned";
}

/// <summary>
/// Land sites (roadmap F2-12, docs/FARM_TYPES_SPEC.md §3.2-§3.4): the land
/// lane's engine verbs and its day. A land site (<see cref="RASFacility"/>)
/// with lots holds its fish in physical tanks (<see cref="LandTank"/>, one
/// population each, wrapping S2-01b's <see cref="Pen"/>); the land tick solves
/// each lot's water, heat, oxygen and electricity on the day's feed
/// (<see cref="LandLotDaySolver"/>) and books the energy, the liquid oxygen
/// and the staff as opex, ring-fenced out of the resource-rent base.
/// <list type="bullet">
/// <item><see cref="CommissionLandSite"/>: a site built at once on a pad (a scenario
/// start or a test: no permits, no payment); the player's build with permits and
/// phased construction is <c>BuildLandSite</c> (F2-14, <c>.FacilityPermits.cs</c>).</item>
/// <item><see cref="StockLandTank"/>, <see cref="PreviewLandStocking"/>,
/// <see cref="HarvestLandTank"/>: one cohort a tank, refused when its projected
/// peak passes the template's density limit (85 kg/m³, [L:R2]) or the rolling
/// year's plan passes the site's nameplate production.</item>
/// <item><see cref="SetVannstrategi"/> and <see cref="InstallHeatRecovery"/>.</item>
/// </list>
/// A pre-F2 land site (no lots) keeps the pre-F2 tick (<see cref="RASFacility.TickDay"/>).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The land lane's calibration (<c>facilities.land</c>).</summary>
    public LandCalibration LandCalibration => _calibration.Facilities.Land;

    /// <summary>
    /// Use <paramref name="facilities"/> as the facilities section (a scenario
    /// lever such as the moratorium flag, or a test). Only the facilities
    /// section changes; the rest of the registry is kept.
    /// </summary>
    internal void UseFacilitiesCalibration(FacilitiesCalibration facilities)
        => _calibration = _calibration with { Facilities = facilities ?? throw new ArgumentNullException(nameof(facilities)) };

    /// <summary>The variant of the farm-type catalogue a land template builds.</summary>
    public static string FarmTypeVariantOf(LandTemplate template) => template switch
    {
        LandTemplate.PostSmoltModule => FarmTypeIds.PostSmolt,
        LandTemplate.RasHall => FarmTypeIds.Ras,
        _ => FarmTypeIds.Hfs,
    };

    // ── The day's feed per land population (the land tick's input) ─────────

    private readonly Dictionary<FishPopulation, LandFeedDay> _landFeedKg = new(ReferenceEqualityComparer.Instance);
    private int _landFeedStep = -1;

    /// <summary>One land population's feed in a step: eaten (kg), delivered (kg) and its price (NOK).</summary>
    private readonly record struct LandFeedDay(double EatenKg, double DeliveredKg, decimal CostNok);

    /// <summary>
    /// The biology fed <paramref name="population"/> in a land tank
    /// <paramref name="kg"/> today (<see cref="GrowAndFeedOneDay"/>), delivering
    /// <paramref name="deliveredKg"/> at <paramref name="costNok"/> (F2-15: the
    /// site's own books). Kept for the step only: the land tick reads it later
    /// in the same step; nothing is saved.
    /// </summary>
    internal void NoteLandFeed(FishPopulation population, double kg, double deliveredKg = 0.0, decimal costNok = 0m)
    {
        if (_landFeedStep != HoldingIndexBuilds)
        {
            _landFeedKg.Clear();
            _landFeedStep = HoldingIndexBuilds;
        }
        _landFeedKg.TryGetValue(population, out var before);
        _landFeedKg[population] = new LandFeedDay(before.EatenKg + Math.Max(0.0, kg),
            before.DeliveredKg + Math.Max(0.0, deliveredKg), before.CostNok + Math.Max(0m, costNok));
    }

    /// <summary>Feed <paramref name="population"/> ate in this step (kg; 0 outside the step that fed it).</summary>
    internal double LandFeedTodayKg(FishPopulation population)
        => _landFeedStep == HoldingIndexBuilds && _landFeedKg.TryGetValue(population, out var day) ? day.EatenKg : 0.0;

    /// <summary>Feed delivered to <paramref name="population"/> in this step (kg) and its price (NOK); 0 outside the step.</summary>
    internal (double DeliveredKg, decimal CostNok) LandFeedBoughtToday(FishPopulation population)
        => _landFeedStep == HoldingIndexBuilds && _landFeedKg.TryGetValue(population, out var day)
            ? (day.DeliveredKg, day.CostNok)
            : (0.0, 0m);

    // ── Building a site and its lots ───────────────────────────────────────

    /// <summary>
    /// A land site built at once on <paramref name="plotId"/> (a scenario start
    /// or a test, spec §3.1 decision 5): <paramref name="lots"/> units of
    /// <paramref name="template"/>, every tank ready today, no permit and no
    /// payment (the capital is the scenario's). Fails before any change:
    /// engine_not_ready, not_found (no such pad), plot_taken, out_of_range,
    /// lots_exhausted.
    /// </summary>
    public CommandResult CommissionLandSite(string plotId, LandTemplate template, int lots = 1, string name = "")
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (!Enum.IsDefined(template) || lots < 1)
            return CommandResult.Fail(LandSiteReasons.OutOfRange, $"Unknown template '{template}' or {lots} lots");
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        if (plot == null || plot.Kind != PlotKind.LandPad)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land pad '{plotId}'");
        if (!plot.IsFree)
            return CommandResult.Fail(LandSiteReasons.PlotTaken, $"'{plotId}' is held by '{plot.OwnerUnitId}'");
        if (lots > PadRoomFor(plot, template))
            return CommandResult.Fail(LandSiteReasons.LotsExhausted, $"'{plotId}' holds {PadRoomFor(plot, template)} units of {template}");

        var site = NewLandSite(plot, template, name);
        for (int i = 0; i < lots; i++)
            AddLot(site, template, "", State.Day);
        RefreshLandMirrors(site);
        ApplyDriftsplanDefaults(site.Id); // F2-05: the land lane's default standing orders (F2-15/F2-16 add them)
        LogFacilityBuilt("Landanlegg i drift",
            SimText.Nb($"{site.Name} er i drift på {plot.Name} ({site.AllTanks().Count()} kar)"), 0m);
        return CommandResult.Success($"Land site '{site.Name}' placed on {plot.Id} with {lots} × {template}",
            lots, site.AllTanks().Count(), site.Id);
    }

    /// <summary>Units of <paramref name="template"/> a pad holds (grow-out lots, or post-smolt modules).</summary>
    internal static int PadRoomFor(SitePlot plot, LandTemplate template)
        => template == LandTemplate.PostSmoltModule ? plot.PostSmoltModules : plot.PhaseLots;

    /// <summary>
    /// A new land site on <paramref name="plot"/> (claimed now): grow-out, or
    /// post-smolt for the post-smolt module; no lot yet, the template's water
    /// strategy (reuse, setpoint, the intakes blended toward it).
    /// </summary>
    internal RASFacility NewLandSite(SitePlot plot, LandTemplate template, string name)
    {
        bool postSmolt = template == LandTemplate.PostSmoltModule;
        var kind = postSmolt ? LandSiteKind.PostSmolt : LandSiteKind.GrowOut;
        string id = postSmolt
            ? NextFacilityId("postsmolt", PostSmoltSites.Select(f => f.Id).Concat(PostSmoltFacilities.Select(f => f.Id)))
            : NextFacilityId("ras", RASFacilities.Where(f => f.Kind == LandSiteKind.GrowOut).Select(f => f.Id));
        var t = LandCalibration.For(template);
        var strategy = LandSiteTemplates.DefaultStrategy(t);
        var site = new RASFacility
        {
            Id = id,
            Name = FacilityName(name, postSmolt ? "Postsmoltanlegg" : "Landanlegg", RASFacilities.Count(f => f.Kind == kind)),
            Kind = kind,
            RecirculationRate = strategy.Reuse,
            DeepIntakeShare = strategy.DeepIntakeShare,
            TargetWaterTemp = strategy.SetpointC,
            CurrentWaterTemp = strategy.SetpointC,
            BlendTowardSetpoint = strategy.BlendTowardSetpoint,
            ConstructionStartDay = State.Day,
            ConstructionProgress = 0.0,
            TankCount = 0,
            EmployeeCount = 0,
            CapacityTonnesPerYear = 0.0,
            DailyEnergyKWh = 0.0,
        };
        PutOnPad(site, plot);
        RASFacilities.Add(site);
        return site;
    }

    /// <summary>
    /// Add a lot of <paramref name="template"/> to <paramref name="site"/>,
    /// its tanks numbered on from the site's last: built by
    /// <paramref name="projectId"/>, or ready at once on <paramref name="readyDay"/>
    /// when the project is "". HFS lots carry heat recovery (the energy anchor
    /// of spec §3.3, [V:11]); a full HFS lot hands over half its grow-out tanks
    /// at the first commissioning ([L:R1]).
    /// </summary>
    internal LandLot AddLot(RASFacility site, LandTemplate template, string projectId, int readyDay)
    {
        var cal = LandCalibration;
        var t = cal.For(template);
        int nextTank = site.AllTanks().Select(k => k.Pen.Number).DefaultIfEmpty(0).Max() + 1;
        int index = site.Lots.Count + 1;
        while (site.Lots.Any(l => l.Id == LandLot.ComposeId(site.Id, index))) index++;
        var lot = LandSiteTemplates.BuildLot(site.Id, index, template, cal, nextTank);
        lot.HeatRecovery = template is LandTemplate.HfsLot or LandTemplate.HfsPilotLot;
        lot.ConstructionProjectId = projectId ?? "";
        lot.FirstTankCount = template == LandTemplate.HfsLot
            ? Math.Clamp((int)Math.Round(t.GrowOutTanks * cal.Build.FirstTanksShare, MidpointRounding.AwayFromZero), 1, lot.Tanks.Count)
            : lot.Tanks.Count;
        if (string.IsNullOrEmpty(projectId))
        {
            lot.FirstTanksDay = readyDay;
            lot.CompleteDay = readyDay;
        }
        site.Lots.Add(lot);
        return lot;
    }

    /// <summary>
    /// The pre-F2 fields the old panels, the views and the carbon ledger read,
    /// from the lots: tanks and their mean volume, the nameplate production and
    /// staff of the commissioned lots, operational once a tank is ready.
    /// </summary>
    internal void RefreshLandMirrors(RASFacility site)
    {
        if (!site.HasLots) return;
        var cal = LandCalibration;
        var tanks = site.AllTanks().ToList();
        site.TankCount = tanks.Count;
        site.TankVolumeM3 = tanks.Count > 0 ? tanks.Average(k => k.VolumeM3) : 0.0;
        site.CapacityTonnesPerYear = site.Lots.Where(l => l.IsCommissioned).Sum(l => cal.For(l.Template).NameplateTonnesHogPerYear);
        site.EmployeeCount = site.Lots.Where(l => l.IsCommissioned).Sum(l => cal.For(l.Template).Staff);
        site.ConstructionProgress = site.Lots.Any(l => l.IsCommissioned) ? 1.0 : LandBuildProgressOf(site);
    }

    /// <summary>Progress (0-1, below 1) of the site's first build while no tank is ready.</summary>
    private double LandBuildProgressOf(RASFacility site)
    {
        var project = State.ConstructionProjects.FirstOrDefault(p => site.Lots.Any(l => l.ConstructionProjectId == p.Id));
        return project == null ? 0.0 : Math.Min(0.999, ProjectProgressOf(project));
    }

    // ── Construction state and the tanks' fish (kept in step) ──────────────

    /// <summary>
    /// Bring a land site in step with its construction projects and its fish:
    /// a lot's first tanks and its completion take the days its project's
    /// phases ended (F2-14); a pilot's finished expansion makes it a full lot;
    /// a tank whose fish are gone is empty; fish at the site in no tank go to
    /// the first ready empty tank; the pre-F2 fields follow. Idempotent.
    /// </summary>
    internal void SyncLandSite(RASFacility site)
    {
        if (site == null || !site.HasLots || State == null) return;
        for (int i = 0; i < site.Lots.Count; i++)
        {
            var lot = site.Lots[i];
            (lot.FirstTanksDay, lot.CompleteDay) = LotDaysNow(lot);
            var expansion = FindProject(lot.ExpansionProjectId);
            if (expansion != null && expansion.Status == ConstructionStatus.Complete && lot.Template == LandTemplate.HfsPilotLot)
                site.Lots[i] = ExpandedLot(site, lot, expansion.PhaseCompletedDays[^1]);
        }

        // Tanks follow the fish: emptied tanks free, fish in no tank seated.
        var present = new HashSet<string>(site.Tanks.Select(p => p.Id), StringComparer.Ordinal);
        foreach (var tank in site.AllTanks())
            if (!tank.Pen.IsEmpty && !present.Contains(tank.Pen.PopulationId))
            {
                tank.Pen.PopulationId = "";
                tank.ClearFishRecord(); // F2-13/F2-15: the record and plan leave with the fish
            }
        foreach (var population in site.Tanks)
        {
            if (site.TankOf(population) != null) continue;
            var free = ReadyEmptyTanks(site).FirstOrDefault();
            if (free == null) break;
            free.Pen.PopulationId = population.Id;
        }
        RefreshLandMirrors(site);
    }

    /// <summary>
    /// A lot's first-tanks and completion days as they stand now: its own, or
    /// the days its construction project's phases ended (a pure read: the land
    /// tick and the commands write them, <see cref="SyncLandSite"/>). -1 for a
    /// day not reached.
    /// </summary>
    internal (int FirstTanksDay, int CompleteDay) LotDaysNow(LandLot lot)
    {
        int first = lot.FirstTanksDay, complete = lot.CompleteDay;
        var project = FindProject(lot.ConstructionProjectId);
        if (project != null)
        {
            int index = project.Phases.FindIndex(p => p.Key == LandConstructionPhases.FirstTanks);
            if (first < 0 && index >= 0 && project.PhaseCompletedDays.Count > index)
                first = project.PhaseCompletedDays[index];
            if (complete < 0 && project.Status == ConstructionStatus.Complete && project.PhaseCompletedDays.Count > 0)
            {
                complete = project.PhaseCompletedDays[^1];
                if (first < 0) first = complete;
            }
        }
        return (first, complete);
    }

    /// <summary>Day the lot's first tanks took fish, as it stands now (-1 when not yet; a pure read).</summary>
    public int LotFirstTanksDayNow(LandLot lot) => LotDaysNow(lot).FirstTanksDay;

    /// <summary>Day the whole lot was ready, as it stands now (-1 when not yet; a pure read).</summary>
    public int LotCompleteDayNow(LandLot lot) => LotDaysNow(lot).CompleteDay;

    /// <summary>True when the lot has a tank ready for fish now (a pure read).</summary>
    public bool IsLotCommissionedNow(LandLot lot)
    {
        var (first, complete) = LotDaysNow(lot);
        return first >= 0 || complete >= 0;
    }

    /// <summary>True when tank <paramref name="index"/> (tank order) of the lot takes fish now (a pure read).</summary>
    public bool IsTankReadyNow(LandLot lot, int index)
    {
        if (lot == null || index < 0 || index >= lot.Tanks.Count) return false;
        var (first, complete) = LotDaysNow(lot);
        return complete >= 0 || (first >= 0 && index < lot.FirstTankCount);
    }

    /// <summary>The ready, empty tanks of a site, in tank order.</summary>
    private static IEnumerable<LandTank> ReadyEmptyTanks(RASFacility site)
    {
        foreach (var lot in site.Lots)
            for (int i = 0; i < lot.Tanks.Count; i++)
                if (lot.IsTankReady(i) && lot.Tanks[i].Pen.IsEmpty)
                    yield return lot.Tanks[i];
    }

    /// <summary>
    /// A pilot lot grown into a full HFS lot on <paramref name="day"/>: the
    /// same id, its 5 tanks with their fish and water, and the full lot's other
    /// grow-out and pre-grow-out tanks (8 + 1), every tank ready; the full
    /// lot's heat pumps.
    /// </summary>
    private LandLot ExpandedLot(RASFacility site, LandLot pilot, int day)
    {
        var cal = LandCalibration;
        var full = cal.Hfs;
        int nextTank = site.AllTanks().Select(k => k.Pen.Number).DefaultIfEmpty(0).Max() + 1;
        var lot = new LandLot
        {
            Id = pilot.Id,
            Template = LandTemplate.HfsLot,
            HeatPumpMWe = full.HeatPumpMWe,
            HeatRecovery = pilot.HeatRecovery,
            Biofilter = pilot.Biofilter,
            BiofilterMaturity = pilot.BiofilterMaturity,
            ConstructionProjectId = pilot.ConstructionProjectId,
            FirstTanksDay = pilot.FirstTanksDay >= 0 ? pilot.FirstTanksDay : day,
            CompleteDay = day,
        };
        lot.Tanks.AddRange(pilot.Tanks);
        int growOut = Math.Max(0, full.GrowOutTanks - pilot.Tanks.Count(k => k.Role == TankRole.GrowOut));
        int preGrow = Math.Max(0, full.PreGrowTanks - pilot.Tanks.Count(k => k.Role == TankRole.PreGrowOut));
        for (int k = 0; k < growOut; k++)
            lot.Tanks.Add(LandTank.Build(site.Id, nextTank++, TankRole.GrowOut, full.GrowOutDiameterM, full.WaterDepthM));
        for (int k = 0; k < preGrow; k++)
            lot.Tanks.Add(LandTank.Build(site.Id, nextTank++, TankRole.PreGrowOut, full.PreGrowDiameterM, full.WaterDepthM));
        foreach (var tank in lot.Tanks.Skip(pilot.Tanks.Count))
            tank.TemperatureC = site.TargetWaterTemp;
        lot.FirstTankCount = lot.Tanks.Count;
        return lot;
    }

    private ConstructionProject? FindProject(string? projectId)
        => string.IsNullOrEmpty(projectId) || State?.ConstructionProjects == null
            ? null
            : State.ConstructionProjects.FirstOrDefault(p => p.Id == projectId);

    /// <summary>Progress of a project (0-1): finished phases' planned days and the current phase's worked share, over all planned days.</summary>
    internal double ProjectProgressOf(ConstructionProject project)
    {
        if (project.Status == ConstructionStatus.Complete) return 1.0;
        double total = project.Phases.Sum(p => (double)p.PlannedDays);
        if (!(total > 0.0)) return 0.0;
        double done = project.Phases.Take(Math.Min(project.CurrentPhase, project.Phases.Count)).Sum(p => (double)p.PlannedDays);
        var current = project.Current;
        if (current != null && project.Status == ConstructionStatus.Building)
            done += PhaseProgressOf(project) * current.PlannedDays;
        return Math.Clamp(done / total, 0.0, 1.0);
    }

    /// <summary>Worked share of the current phase (0-1): days worked (not standing still) over its days.</summary>
    internal double PhaseProgressOf(ConstructionProject project)
    {
        if (project.Status != ConstructionStatus.Building || project.PhaseDays <= 0) return 0.0;
        int worked = State.Day - project.PhaseStartDay - project.SuspendedDays;
        return Math.Clamp((double)worked / project.PhaseDays, 0.0, 1.0);
    }

    // ── Stocking, harvest ──────────────────────────────────────────────────

    /// <summary>
    /// Stock <paramref name="count"/> smolts of <paramref name="strainId"/> in
    /// an empty, ready tank at the template's release weight (400 g on an HFS
    /// lot, [L:R1]), bought at the smolt price of their size tier (the F2-13
    /// hook). Refused before any change: engine_not_ready, not_found,
    /// tank_not_found, out_of_range, not_operational (the tank is not built),
    /// biofilter_immature, tank_occupied, density_exceeded (the plan's peak
    /// over the template's limit, spec §3.3), annual_capacity (the rolling
    /// year's planned production over the site's nameplate or permit),
    /// insufficient_funds. Land fish carry no lice.
    /// </summary>
    public CommandResult StockLandTank(string siteId, string tankId, int count, string strainId = "standard")
    {
        if (FindLandSite(siteId) is { HasLots: true } synced) SyncLandSite(synced);
        var preview = PreviewLandStocking(siteId, tankId, count, strainId);
        if (!preview.Allowed)
            return CommandResult.Fail(preview.Reason, preview.Message, preview.MaxSmolts);
        var site = FindLandSite(siteId)!;
        var tank = site.FindTank(tankId)!;
        if (!TryPayCost(preview.CostNok, CostBooking.Cogs, "smolts"))
            return CommandResult.Fail(LandSiteReasons.InsufficientFunds,
                $"Insufficient funds: need {preview.CostNok:N0} NOK for {count:N0} smolts", (double)preview.CostNok);
        AttributeUnitCost(site.Id, HoldingKind.LandTank, preview.CostNok); // F2-04 ring-fence: land is never in the rent base
        site.Account.SmoltNok += preview.CostNok; // F2-15: the site's own books

        var strain = SmoltStrainRegistry.Get(strainId);
        var population = new FishPopulation { ZoneId = site.ZoneId, SiteId = site.Id, SeaLiceLoad = 0.0 };
        population.InjectDependencies(Growth, Mortality);
        population.Cohorts.Add(new FishPopulation.Cohort(Hatchery.MaturationDays, count, preview.ReleaseWeightKg,
            LifeStage.Smolt, strain.BuildGenome()));
        // The size tier prices the fish (the F2-13 hook); its growth carryover is a
        // sea phase's (P5), so a fish that stays on land grows on the land plan.
        site.Tanks.Add(population);
        State.Populations.Add(population);
        tank.Pen.PopulationId = population.Id;
        // F2-13/F2-15: the fish's record, plan (with the plan's daily hazard) and feeding plan.
        NoteLandStocking(site, tank, population, strain, 1.0 - preview.DailySurvival);
        site.StockingLog.Add(new LandStockingRecord(State.Day, tank.Id, count, preview.PlannedHogKg));
        PruneStockingLog(site);

        LogFacilityEvent("Utsett i landkar",
            SimText.Nb($"Satte ut {count:N0} fisk ({preview.ReleaseWeightKg * 1000:N0} g) i {tank.Id} på {site.Name}"), -preview.CostNok);
        return CommandResult.Success($"Stocked {count:N0} fish in {tank.Id}", count, (double)preview.CostNok, tank.Id);
    }

    /// <summary>
    /// What stocking <paramref name="count"/> smolts in a tank would do, and
    /// whether it is allowed (a pure read: <see cref="StockLandTank"/> runs the
    /// same check). With <paramref name="count"/> 0 it reports the room.
    /// </summary>
    public LandStockingPreview PreviewLandStocking(string siteId, string tankId, int count, string strainId = "standard")
    {
        if (State == null || FinanceManager == null)
            return LandStockingPreview.Refused("engine_not_ready", "Engine not initialized");
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return LandStockingPreview.Refused(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        var tank = site.FindTank(tankId);
        var lot = tank == null ? null : site.LotOf(tank);
        if (tank == null || lot == null)
            return LandStockingPreview.Refused(LandSiteReasons.TankNotFound, $"No tank '{tankId}' at '{site.Name}'");

        var cal = LandCalibration;
        var t = cal.For(lot.Template);
        var strain = SmoltStrainRegistry.Get(strainId);
        double releaseKg = t.PlanReleaseWeightKg;
        var tier = TierOfWeight(releaseKg);
        double growth = strain.BuildGenome().GetGrowthMultiplier(); // the cohort's: land fish carry no sea-phase tier bonus
        var basePlan = LandStockingPlan.For(t, cal.Production);
        var plan = basePlan with { TemperatureC = site.TargetWaterTemp, GrowthMultiplier = basePlan.GrowthMultiplier * growth };
        var verdict = LandStockingPlanner.Check(Growth.Law, plan, tank.VolumeM3, Math.Max(0, count), t.MaxDensityKgM3);
        double hogPerSmolt = t.PlanHarvestWeightKg * cal.Production.CycleSurvival * cal.Production.HogPerLw;
        double nameplateKg = AnnualProductionCapTonnes(site) * 1000.0;
        double plannedKg = PlannedProductionKg(site);
        int annualRoom = hogPerSmolt > 0.0 ? (int)Math.Max(0.0, Math.Floor((nameplateKg - plannedKg) / hogPerSmolt)) : int.MaxValue;
        decimal cost = Math.Max(0, count) * SmoltTierCostPerFish(tier, strain, SmoltSource.Purchased);
        // F2-15: a grow-out site runs at most its utilisation cap of the nameplate (competence).
        double utilisationCap = LandUtilisationCapOf(site);
        int utilisationRoom = hogPerSmolt > 0.0 && utilisationCap < 1.0
            ? (int)Math.Max(0.0, Math.Floor((Math.Min(nameplateKg, utilisationCap * LandNameplateTonnesNow(site) * 1000.0) - plannedKg) / hogPerSmolt))
            : int.MaxValue;
        int maxSmolts = Math.Min(Math.Min(verdict.MaxSmolts, annualRoom), utilisationRoom);
        var preview = new LandStockingPreview(true, "", "", tank.Id, releaseKg, tier, verdict.PeakDensityKgM3,
            verdict.Forecast.CycleDays, maxSmolts, verdict.MaxSmolts, annualRoom, Math.Max(0, count) * hogPerSmolt, cost,
            utilisationCap, utilisationRoom, verdict.Forecast.DailySurvival);

        int index = lot.Tanks.IndexOf(tank);
        if (count <= 0)
            return preview with { Allowed = false, Reason = LandSiteReasons.OutOfRange, Message = "Stock at least one fish" };
        if (!IsTankReadyNow(lot, index))
            return preview with { Allowed = false, Reason = LandSiteReasons.NotOperational, Message = $"{tank.Id} is not built yet" };
        if (lot.Biofilter != BiofilterType.None
            && LandSiteTemplates.BiofilterMaturity(State.Day - LotDaysNow(lot).FirstTanksDay, t, cal.Tan) < 1.0)
            return preview with { Allowed = false, Reason = LandSiteReasons.BiofilterImmature, Message = $"The biofilter of {lot.Id} is still maturing" };
        if (site.PopulationIn(tank) != null)
            return preview with { Allowed = false, Reason = LandSiteReasons.TankOccupied, Message = $"{tank.Id} already holds fish" };
        if (!verdict.Allowed)
            return preview with { Allowed = false, Reason = verdict.Reason, Message =
                $"{count:N0} fish would peak at {verdict.PeakDensityKgM3:F1} kg/m³ in {tank.Id} (limit {t.MaxDensityKgM3:F0})" };
        if (count > annualRoom)
            return preview with { Allowed = false, Reason = LandSiteReasons.AnnualCapacity, Message =
                $"The rolling year's plan would pass the site's {nameplateKg / 1000.0:N0} t HOG a year" };
        if (count > utilisationRoom)
            return preview with { Allowed = false, Reason = LandSiteReasons.UtilCap, Message =
                $"The rolling year's plan would pass {utilisationCap:P0} of the nameplate, all the site's competence runs" };
        return preview;
    }

    /// <summary>The smolt tier of fish stocked at <paramref name="weightKg"/> (the post-smolt transfer's tiers).</summary>
    private static SmoltQualityTier TierOfWeight(double weightKg)
    {
        double grams = weightKg * 1000.0;
        return grams >= SuperSmoltTransferWeightG ? SmoltQualityTier.SuperSmolt
            : grams >= LargeSmoltTransferWeightG ? SmoltQualityTier.Large
            : SmoltQualityTier.Standard;
    }

    /// <summary>
    /// The site's yearly production cap (t HOG): the nameplate of its built
    /// lots, and no more than the land permit on its pad allows (a site built
    /// at once has no permit and only its nameplate).
    /// </summary>
    public double AnnualProductionCapTonnes(RASFacility site)
    {
        if (site == null) throw new ArgumentNullException(nameof(site));
        var cal = LandCalibration;
        double nameplate = site.Lots.Where(IsLotCommissionedNow).Sum(l => cal.For(l.Template).NameplateTonnesHogPerYear);
        double permitted = PermittedProductionTonnes(site.PlotId, PermitKind.LandAquaculture);
        return permitted > 0.0 ? Math.Min(nameplate, permitted) : nameplate;
    }

    /// <summary>Planned HOG production (kg) of the fish the site stocked in the last 365 days.</summary>
    private double PlannedProductionKg(RASFacility site)
    {
        int from = State.Day - SimCalendar.DaysPerYear;
        return site.StockingLog.Where(r => r.Day > from).Sum(r => r.PlannedHogKg);
    }

    private void PruneStockingLog(RASFacility site)
    {
        int from = State.Day - SimCalendar.DaysPerYear;
        site.StockingLog.RemoveAll(r => r.Day <= from);
    }

    /// <summary>
    /// Harvest every fish of one land tank now through the S2-04 pipeline (the
    /// site's own hall route; land pays no resource rent). Fails:
    /// engine_not_ready, not_found, tank_not_found, holding_empty.
    /// </summary>
    public CommandResult HarvestLandTank(string siteId, string tankId)
    {
        if (State == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        SyncLandSite(site);
        var tank = site.FindTank(tankId);
        if (tank == null)
            return CommandResult.Fail(LandSiteReasons.TankNotFound, $"No tank '{tankId}' at '{site.Name}'");
        var population = site.PopulationIn(tank);
        if (population == null || population.TotalCount <= 0)
            return CommandResult.Fail("holding_empty", $"{tank.Id} holds no fish");
        var result = HarvestNow(site.Id, new[] { population.Id });
        SyncLandSite(site);
        return result;
    }

    /// <summary>
    /// Grade a tank (spec §3.4 <c>GradeTank</c>): move <paramref name="share"/>
    /// of its fish (each cohort's share, rounded) into an empty, ready tank of
    /// the same site, as from a pre-grow-out tank to grow-out tanks. The whole
    /// tank moves as it is; a share splits off a new population (the fish leave
    /// the source through the mortality ledger as a transfer). Refused before
    /// any change: not_found, tank_not_found, out_of_range (a share outside
    /// (0, 1], the same tank), holding_empty, not_operational (the target is
    /// not built), tank_occupied, density_exceeded (the moved fish's projected
    /// peak in the target over its limit).
    /// </summary>
    public CommandResult GradeTank(string siteId, string fromTankId, string toTankId, double share)
    {
        if (State == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        SyncLandSite(site);
        var from = site.FindTank(fromTankId);
        var to = site.FindTank(toTankId);
        if (from == null || to == null)
            return CommandResult.Fail(LandSiteReasons.TankNotFound, $"No tank '{(from == null ? fromTankId : toTankId)}' at '{site.Name}'");
        if (!(share > 0.0 && share <= 1.0) || ReferenceEquals(from, to))
            return CommandResult.Fail(LandSiteReasons.OutOfRange, "Grade a share in (0, 1] into another tank");
        var source = site.PopulationIn(from);
        if (source == null || source.TotalCount <= 0)
            return CommandResult.Fail("holding_empty", $"{from.Id} holds no fish");
        var toLot = site.LotOf(to)!;
        if (!IsTankReadyNow(toLot, toLot.Tanks.IndexOf(to)))
            return CommandResult.Fail(LandSiteReasons.NotOperational, $"{to.Id} is not built yet");
        if (site.PopulationIn(to) != null)
            return CommandResult.Fail(LandSiteReasons.TankOccupied, $"{to.Id} already holds fish");

        var moves = source.Cohorts.Select(c => (Cohort: c,
            Count: share >= 1.0 ? c.Count : (int)Math.Round(c.Count * share, MidpointRounding.AwayFromZero))).ToList();
        int fish = moves.Sum(m => m.Count);
        if (fish <= 0)
            return CommandResult.Fail(LandSiteReasons.OutOfRange, "The share moves no fish");
        var cal = LandCalibration;
        var t = cal.For(toLot.Template);
        double meanKg = moves.Sum(m => m.Count * m.Cohort.MeanWeightKg) / fish;
        var basePlan = LandStockingPlan.For(t, cal.Production);
        var plan = basePlan with
        {
            ReleaseWeightKg = meanKg,
            HarvestWeightKg = Math.Max(meanKg, t.PlanHarvestWeightKg),
            TemperatureC = site.TargetWaterTemp,
            GrowthMultiplier = basePlan.GrowthMultiplier
                * source.Cohorts.Where(c => c.Count > 0).Select(CohortGrowthMultiplier).DefaultIfEmpty(1.0).Max(),
        };
        var verdict = LandStockingPlanner.Check(Growth.Law, plan, to.VolumeM3, fish, t.MaxDensityKgM3);
        if (!verdict.Allowed)
            return CommandResult.Fail(verdict.Reason,
                $"{fish:N0} fish would peak at {verdict.PeakDensityKgM3:F1} kg/m³ in {to.Id} (limit {t.MaxDensityKgM3:F0})", verdict.MaxSmolts);

        if (share >= 1.0)
        {
            to.Pen.PopulationId = source.Id;
            from.Pen.PopulationId = "";
            to.TakeFishRecordOf(from); // F2-13/F2-15: the record and plan move with the fish
            from.ClearFishRecord();
        }
        else
        {
            var split = new FishPopulation { ZoneId = site.ZoneId, SiteId = site.Id, SeaLiceLoad = 0.0, VaccinatedDay = source.VaccinatedDay };
            split.InjectDependencies(Growth, Mortality);
            foreach (var (cohort, n) in moves)
            {
                int moved = source.RemoveAlive(cohort, n, FishRemoval.TransferOut);
                if (moved <= 0) continue;
                split.Cohorts.Add(new FishPopulation.Cohort(cohort.AgeDays, moved, cohort.MeanWeightKg, cohort.Stage, cohort.Genetics)
                {
                    Quality = cohort.Quality,
                    WeightCv = cohort.WeightCv,
                });
            }
            split.Ration = source.Ration;
            site.Tanks.Add(split);
            State.Populations.Add(split);
            to.Pen.PopulationId = split.Id;
            to.TakeFishRecordOf(from);
        }
        LogFacilityEvent("Sortering av kar", SimText.Nb($"{site.Name}: {fish:N0} fisk flyttet fra {from.Id} til {to.Id}"), 0m);
        return CommandResult.Success($"Graded {fish:N0} fish from {from.Id} to {to.Id}", fish, verdict.PeakDensityKgM3, to.Id);
    }

    // ── Water strategy and heat recovery ───────────────────────────────────

    /// <summary>Lowest tank setpoint a Vannstrategi may set (°C): the calibration's range of the template setpoint.</summary>
    public const double MinSetpointC = 6.0;

    /// <summary>Highest tank setpoint a Vannstrategi may set (°C): the calibration's range of the template setpoint.</summary>
    public const double MaxSetpointC = 16.0;

    /// <summary>
    /// The site's Vannstrategi (spec §3.4): reuse r (0 to the calibration's
    /// highest), the deep-intake share w (0-1, used when not blending), the
    /// setpoint (6-16 °C), and whether the intakes are blended toward the
    /// setpoint every day. Fails: not_found, out_of_range. Changes the water
    /// from the next land tick.
    /// </summary>
    public CommandResult SetVannstrategi(string siteId, double reuse, double deepIntakeShare, double setpointC, bool blendTowardSetpoint)
    {
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        double maxReuse = LandCalibration.Intake.MaxReuse;
        if (!(reuse >= 0.0 && reuse <= maxReuse) || !(deepIntakeShare >= 0.0 && deepIntakeShare <= 1.0)
            || !(setpointC >= MinSetpointC && setpointC <= MaxSetpointC))
            return CommandResult.Fail(LandSiteReasons.OutOfRange,
                $"Reuse 0-{maxReuse}, deep share 0-1 and setpoint {MinSetpointC}-{MaxSetpointC} °C");
        site.RecirculationRate = reuse;
        site.DeepIntakeShare = deepIntakeShare;
        site.TargetWaterTemp = setpointC;
        site.BlendTowardSetpoint = blendTowardSetpoint;
        return CommandResult.Success($"Vannstrategi at {site.Name}: r {reuse:F2}, w {(blendTowardSetpoint ? "blend" : deepIntakeShare.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))}, {setpointC:F1} °C",
            reuse, setpointC, site.Id);
    }

    /// <summary>
    /// Fit heat recovery to a lot (spec §3.4): its heat pumps then run at the
    /// heat-recovery COP. Costs the calibration's capex per MW of heat-pump
    /// input, depreciated as the site's. Fails: not_found, already_active,
    /// insufficient_funds.
    /// </summary>
    public CommandResult InstallHeatRecovery(string lotId)
    {
        var site = LandSites.FirstOrDefault(s => s.Lots.Any(l => l.Id == lotId));
        var lot = site?.Lots.First(l => l.Id == lotId);
        if (site == null || lot == null)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No lot '{lotId}'");
        if (lot.HeatRecovery)
            return CommandResult.Fail(LandSiteReasons.AlreadyActive, $"{lotId} already recovers its heat");
        decimal cost = LandCalibration.Build.HeatRecoveryCapexNokPerMWe * (decimal)lot.HeatPumpMWe;
        if (!TryPayCost(cost, CostBooking.Capex, "facilities"))
            return CommandResult.Fail(LandSiteReasons.InsufficientFunds, $"Need {cost:N0} NOK", (double)cost);
        lot.HeatRecovery = true;
        RegisterFacilityAsset(site.Id, FacilityAssetKind.LandSite, cost, State.Day);
        return CommandResult.Success($"Heat recovery fitted to {lotId}", (double)cost, id: lotId);
    }

    // ── The day (LandSiteTickSystem) ───────────────────────────────────────

    /// <summary>Uniforms the land tick draws for a site each day: reserved for F2-16's incident layer (spec §3.7).</summary>
    public const int LandDrawsPerDay = 10;

    /// <summary>
    /// One day of an F2 land site (spec §3.3 "LandSiteTickSystem"): its lots in
    /// step with their construction and fish, then each commissioned lot's
    /// water, heat, oxygen and electricity solved on the day's feed (the
    /// biology fed the fish first; the appetite it leaves is read tomorrow).
    /// The site's stream gives exactly <see cref="LandDrawsPerDay"/> uniforms,
    /// reserved for F2-16 ("draw, then decide"). Books the electricity at the
    /// power price, the liquid oxygen and the staff of the commissioned lots as
    /// opex, each attributed to the site (out of the rent base).
    /// </summary>
    internal void TickLandSiteDay(RASFacility site, SimRngStream stream)
    {
        SyncLandSite(site);
        for (int i = 0; i < LandDrawsPerDay; i++) stream.NextDouble();

        var cal = LandCalibration;
        var strategy = site.WaterStrategy;
        var intake = LandIntakeOf(site);
        double shallowC = SeaTemperatureOfZone(site.ZoneId);
        var energy = default(LandEnergyDay);
        double lox = 0.0, waterKg = 0.0, tSum = 0.0, doSum = 0.0, co2Sum = 0.0;
        double emptyLotC = site.TargetWaterTemp;
        int staff = 0;
        foreach (var lot in site.Lots)
        {
            if (!lot.IsCommissioned) continue;
            var t = cal.For(lot.Template);
            staff += t.Staff;
            lot.BiofilterMaturity = lot.Biofilter == BiofilterType.None
                ? 1.0
                : LandSiteTemplates.BiofilterMaturity(State.Day - lot.FirstTanksDay, t, cal.Tan);
            var loads = new TankLoad[lot.Tanks.Count];
            for (int k = 0; k < lot.Tanks.Count; k++)
            {
                var population = lot.IsTankReady(k) ? site.PopulationIn(lot.Tanks[k]) : null;
                loads[k] = population == null || population.TotalCount <= 0
                    ? default
                    : new TankLoad(population.GetTotalBiomassKg(), LandFeedTodayKg(population));
            }
            var input = new LandLotDayInput(lot, strategy, intake, shallowC, loads,
                LandSalinityOf(site), Math.Clamp(site.BiofilterEfficiency, 0.0, 1.0));
            var day = LandLotDaySolver.Solve(input, cal);
            LandLotDaySolver.Apply(lot, day);
            energy += day.Energy;
            lox += day.LoxPurchasedKg;
            for (int k = 0; k < lot.Tanks.Count; k++)
            {
                if (!loads[k].Stocked) continue;
                waterKg += loads[k].BiomassKg;
                tSum += loads[k].BiomassKg * lot.Tanks[k].TemperatureC;
                doSum += loads[k].BiomassKg * lot.Tanks[k].DoMgL;
                co2Sum += loads[k].BiomassKg * lot.Tanks[k].Co2MgL;
            }
            emptyLotC = day.Heat.TankC;
        }

        // The pre-F2 fields the old panels and the carbon ledger read.
        site.DailyEnergyKWh = energy.TotalKWh;
        site.DailyLoxKg = lox;
        site.DailyLoxUpstreamKWh = energy.LoxUpstreamKWh;
        if (waterKg > 0.0)
        {
            site.CurrentWaterTemp = tSum / waterKg;
            site.OxygenLevel = doSum / waterKg;
            site.CO2Level = co2Sum / waterKg;
        }
        else
        {
            site.CurrentWaterTemp = emptyLotC;
        }

        decimal power = (decimal)(energy.TotalKWh * cal.Energy.PowerPriceNokPerKWh);
        decimal oxygen = (decimal)(lox * cal.Oxygen.LoxPriceNokPerKg);
        decimal wages = staff * cal.Operations.StaffCostNokPerFteYear / SimCalendar.DaysPerYear;
        if (power > 0m) PayCost(power, CostBooking.Opex, "land_energy");
        if (oxygen > 0m) PayCost(oxygen, CostBooking.Opex, "land_oxygen");
        if (wages > 0m) PayCost(wages, CostBooking.Opex, "land_staff");
        AttributeUnitCost(site.Id, HoldingKind.LandTank, power + oxygen + wages); // F2-04 ring-fence

        // F2-13/F2-15: the site's own books, its fish against their plan, its
        // competence, its other running costs, its planned harvests and its
        // transfers to sea (SimulationEngine.LandBiology.cs, .PostSmolt.cs).
        site.Account.EnergyNok += power;
        site.Account.OxygenNok += oxygen;
        site.Account.StaffNok += wages;
        site.Account.EnergyKWh += energy.TotalKWh;
        CloseLandSiteDay(site);
    }

    /// <summary>
    /// The intake side of a site's pad (spec §3.2): the pad's lift and pumps,
    /// the deep line its computed intake reach (F2-19; 0 when no terrain gives
    /// it), UV on the intake (new plants disinfect, [L:L2]).
    /// </summary>
    internal LandIntakeSite LandIntakeOf(RASFacility site)
    {
        var energy = LandCalibration.Energy;
        var plot = FindPlot(site.PlotId) ?? SitePlots.FirstOrDefault(p => p.Id == site.PlotId);
        return plot == null
            ? LandIntakeSite.FromCalibration(energy, 0.0, 0.0)
            : new LandIntakeSite(plot.StaticLiftM, 0.0, Math.Max(0.0, plot.DeepIntakeReachM) / 1000.0, plot.PumpCapacityM3sPerLot);
    }
}

/// <summary>One stocking of a land site, kept for a rolling year (the nameplate check).</summary>
/// <param name="Day">Sim day of the stocking.</param>
/// <param name="TankId">The tank stocked.</param>
/// <param name="Smolts">Fish stocked.</param>
/// <param name="PlannedHogKg">The plan's harvest of them (kg HOG).</param>
public sealed record LandStockingRecord(int Day, string TankId, int Smolts, double PlannedHogKg);

/// <summary>What a land stocking would do (<see cref="SimulationEngine.PreviewLandStocking"/>).</summary>
/// <param name="Allowed">True when the stocking may go ahead.</param>
/// <param name="Reason">Refusal key (<see cref="LandSiteReasons"/>), "" when allowed.</param>
/// <param name="Message">Why, for logs.</param>
/// <param name="TankId">The tank.</param>
/// <param name="ReleaseWeightKg">Weight stocked (the template's plan).</param>
/// <param name="Tier">Smolt tier of that weight.</param>
/// <param name="PeakDensityKgM3">Projected peak density of the stocking.</param>
/// <param name="CycleDays">Days the plan takes to the harvest weight.</param>
/// <param name="MaxSmolts">Most fish the tank takes now (density and the rolling year's room).</param>
/// <param name="DensityRoomSmolts">Most fish the tank's density limit takes on the plan.</param>
/// <param name="AnnualRoomSmolts">Fish the rolling year's nameplate leaves room for.</param>
/// <param name="PlannedHogKg">The plan's harvest of the stocking (kg HOG).</param>
/// <param name="CostNok">Price of the smolts.</param>
/// <param name="UtilisationCap">The site's utilisation cap (share of nameplate a year; 1 at a post-smolt site, F2-15).</param>
/// <param name="UtilisationRoomSmolts">Fish the utilisation cap leaves room for in the rolling year (int.MaxValue when it does not bind).</param>
/// <param name="DailySurvival">The plan's share of the fish alive the next day (its cycle survival over its cycle days, F2-15).</param>
public sealed record LandStockingPreview(bool Allowed, string Reason, string Message, string TankId, double ReleaseWeightKg,
    SmoltQualityTier Tier, double PeakDensityKgM3, int CycleDays, int MaxSmolts, int DensityRoomSmolts, int AnnualRoomSmolts,
    double PlannedHogKg,
    decimal CostNok, double UtilisationCap = 1.0, int UtilisationRoomSmolts = int.MaxValue, double DailySurvival = 1.0)
{
    /// <summary>A refusal before the tank is known.</summary>
    public static LandStockingPreview Refused(string reason, string message)
        => new(false, reason, message, "", 0.0, SmoltQualityTier.Standard, 0.0, 0, 0, 0, 0, 0.0, 0m);
}
