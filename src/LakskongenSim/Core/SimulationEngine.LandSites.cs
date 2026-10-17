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

    private readonly Dictionary<FishPopulation, double> _landFeedKg = new(ReferenceEqualityComparer.Instance);
    private int _landFeedStep = -1;

    /// <summary>
    /// The biology fed <paramref name="population"/> in a land tank
    /// <paramref name="kg"/> today (<see cref="GrowAndFeedOneDay"/>). Kept for
    /// the step only: the land tick reads it later in the same step; nothing
    /// is saved.
    /// </summary>
    internal void NoteLandFeed(FishPopulation population, double kg)
    {
        if (_landFeedStep != HoldingIndexBuilds)
        {
            _landFeedKg.Clear();
            _landFeedStep = HoldingIndexBuilds;
        }
        _landFeedKg.TryGetValue(population, out double before);
        _landFeedKg[population] = before + Math.Max(0.0, kg);
    }

    /// <summary>Feed <paramref name="population"/> ate in this step (kg; 0 outside the step that fed it).</summary>
    internal double LandFeedTodayKg(FishPopulation population)
        => _landFeedStep == HoldingIndexBuilds && _landFeedKg.TryGetValue(population, out double kg) ? kg : 0.0;

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
            var project = FindProject(lot.ConstructionProjectId);
            if (project != null)
            {
                int first = project.Phases.FindIndex(p => p.Key == LandConstructionPhases.FirstTanks);
                if (lot.FirstTanksDay < 0 && first >= 0 && project.PhaseCompletedDays.Count > first)
                    lot.FirstTanksDay = project.PhaseCompletedDays[first];
                if (lot.CompleteDay < 0 && project.Status == ConstructionStatus.Complete && project.PhaseCompletedDays.Count > 0)
                {
                    lot.CompleteDay = project.PhaseCompletedDays[^1];
                    if (lot.FirstTanksDay < 0) lot.FirstTanksDay = lot.CompleteDay;
                }
            }
            var expansion = FindProject(lot.ExpansionProjectId);
            if (expansion != null && expansion.Status == ConstructionStatus.Complete && lot.Template == LandTemplate.HfsPilotLot)
                site.Lots[i] = ExpandedLot(site, lot, expansion.PhaseCompletedDays[^1]);
        }

        // Tanks follow the fish: emptied tanks free, fish in no tank seated.
        var present = new HashSet<string>(site.Tanks.Select(p => p.Id), StringComparer.Ordinal);
        foreach (var tank in site.AllTanks())
            if (!tank.Pen.IsEmpty && !present.Contains(tank.Pen.PopulationId))
                tank.Pen.PopulationId = "";
        foreach (var population in site.Tanks)
        {
            if (site.TankOf(population) != null) continue;
            var free = ReadyEmptyTanks(site).FirstOrDefault();
            if (free == null) break;
            free.Pen.PopulationId = population.Id;
        }
        RefreshLandMirrors(site);
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
        var preview = PreviewLandStocking(siteId, tankId, count, strainId);
        if (!preview.Allowed)
            return CommandResult.Fail(preview.Reason, preview.Message, preview.MaxSmolts);
        var site = FindLandSite(siteId)!;
        var tank = site.FindTank(tankId)!;
        if (!TryPayCost(preview.CostNok, CostBooking.Cogs, "smolts"))
            return CommandResult.Fail(LandSiteReasons.InsufficientFunds,
                $"Insufficient funds: need {preview.CostNok:N0} NOK for {count:N0} smolts", (double)preview.CostNok);
        AttributeUnitCost(site.Id, HoldingKind.LandTank, preview.CostNok); // F2-04 ring-fence: land is never in the rent base

        var strain = SmoltStrainRegistry.Get(strainId);
        var population = new FishPopulation { ZoneId = site.ZoneId, SiteId = site.Id, SeaLiceLoad = 0.0 };
        population.InjectDependencies(Growth, Mortality);
        population.Cohorts.Add(new FishPopulation.Cohort(Hatchery.MaturationDays, count, preview.ReleaseWeightKg,
            LifeStage.Smolt, strain.BuildGenome()) { Quality = preview.Tier });
        site.Tanks.Add(population);
        State.Populations.Add(population);
        tank.Pen.PopulationId = population.Id;
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
        SyncLandSite(site);
        var tank = site.FindTank(tankId);
        var lot = tank == null ? null : site.LotOf(tank);
        if (tank == null || lot == null)
            return LandStockingPreview.Refused(LandSiteReasons.TankNotFound, $"No tank '{tankId}' at '{site.Name}'");

        var cal = LandCalibration;
        var t = cal.For(lot.Template);
        var strain = SmoltStrainRegistry.Get(strainId);
        double releaseKg = t.PlanReleaseWeightKg;
        var tier = TierOfWeight(releaseKg);
        double growth = strain.BuildGenome().GetGrowthMultiplier() * SmoltQualityRegistry.Get(tier).GrowthMultiplier;
        var plan = LandStockingPlan.For(t, cal.Production) with { TemperatureC = site.TargetWaterTemp, GrowthMultiplier = growth };
        var verdict = LandStockingPlanner.Check(Growth.Law, plan, tank.VolumeM3, Math.Max(0, count), t.MaxDensityKgM3);
        double hogPerSmolt = t.PlanHarvestWeightKg * cal.Production.CycleSurvival * cal.Production.HogPerLw;
        double nameplateKg = AnnualProductionCapTonnes(site) * 1000.0;
        double plannedKg = PlannedProductionKg(site);
        int annualRoom = hogPerSmolt > 0.0 ? (int)Math.Max(0.0, Math.Floor((nameplateKg - plannedKg) / hogPerSmolt)) : int.MaxValue;
        decimal cost = Math.Max(0, count) * SmoltTierCostPerFish(tier, strain, SmoltSource.Purchased);
        int maxSmolts = Math.Min(verdict.MaxSmolts, annualRoom);
        var preview = new LandStockingPreview(true, "", "", tank.Id, releaseKg, tier, verdict.PeakDensityKgM3,
            verdict.Forecast.CycleDays, maxSmolts, verdict.MaxSmolts, annualRoom, Math.Max(0, count) * hogPerSmolt, cost);

        int index = lot.Tanks.IndexOf(tank);
        if (count <= 0)
            return preview with { Allowed = false, Reason = LandSiteReasons.OutOfRange, Message = "Stock at least one fish" };
        if (!lot.IsTankReady(index))
            return preview with { Allowed = false, Reason = LandSiteReasons.NotOperational, Message = $"{tank.Id} is not built yet" };
        if (lot.Biofilter != BiofilterType.None
            && LandSiteTemplates.BiofilterMaturity(State.Day - lot.FirstTanksDay, t, cal.Tan) < 1.0)
            return preview with { Allowed = false, Reason = LandSiteReasons.BiofilterImmature, Message = $"The biofilter of {lot.Id} is still maturing" };
        if (!tank.Pen.IsEmpty)
            return preview with { Allowed = false, Reason = LandSiteReasons.TankOccupied, Message = $"{tank.Id} already holds fish" };
        if (!verdict.Allowed)
            return preview with { Allowed = false, Reason = verdict.Reason, Message =
                $"{count:N0} fish would peak at {verdict.PeakDensityKgM3:F1} kg/m³ in {tank.Id} (limit {t.MaxDensityKgM3:F0})" };
        if (count > annualRoom)
            return preview with { Allowed = false, Reason = LandSiteReasons.AnnualCapacity, Message =
                $"The rolling year's plan would pass the site's {nameplateKg / 1000.0:N0} t HOG a year" };
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
        double nameplate = site.Lots.Where(l => l.IsCommissioned).Sum(l => cal.For(l.Template).NameplateTonnesHogPerYear);
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
                HoldingWater.SeawaterSalinityPsu, Math.Clamp(site.BiofilterEfficiency, 0.0, 1.0));
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
        BookLandCost(site, power, "land_energy");
        BookLandCost(site, oxygen, "land_oxygen");
        BookLandCost(site, wages, "land_staff");
    }

    private void BookLandCost(RASFacility site, decimal amount, string category)
    {
        if (amount <= 0m) return;
        PayCost(amount, CostBooking.Opex, category);
        AttributeUnitCost(site.Id, HoldingKind.LandTank, amount); // F2-04 ring-fence
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
public sealed record LandStockingPreview(bool Allowed, string Reason, string Message, string TankId, double ReleaseWeightKg,
    SmoltQualityTier Tier, double PeakDensityKgM3, int CycleDays, int MaxSmolts, int DensityRoomSmolts, int AnnualRoomSmolts,
    double PlannedHogKg,
    decimal CostNok)
{
    /// <summary>A refusal before the tank is known.</summary>
    public static LandStockingPreview Refused(string reason, string message)
        => new(false, reason, message, "", 0.0, SmoltQualityTier.Standard, 0.0, 0, 0, 0, 0, 0.0, 0m);
}
