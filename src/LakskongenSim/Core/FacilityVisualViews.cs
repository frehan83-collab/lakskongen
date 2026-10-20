using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// How far a construction project has come, as the 3D presenter draws it
/// (roadmap F2-20, docs/FARM_TYPES_SPEC.md §6.3). A pure read of the
/// engine's <see cref="ConstructionProject"/> (land, F2-14) or offshore
/// construction view (F2-07); a unit built at once (a scenario start, a test)
/// or a legacy unit has <see cref="FacilityBuildStatus.None"/> and, while it
/// is not built, a phase derived from its own 0-1 progress.
/// </summary>
/// <param name="ProjectId">The project ("" for none).</param>
/// <param name="Status">One of <see cref="FacilityBuildStatus"/>.</param>
/// <param name="PhaseKey">The phase under way (the project's own key: <see cref="LandConstructionPhases"/>, <see cref="OffshoreProfiles"/>); <see cref="FacilityBuildStatus.Operational"/> once built.</param>
/// <param name="PhaseIndex">Its index (= <paramref name="PhaseCount"/> once built).</param>
/// <param name="PhaseCount">Phases of the build.</param>
/// <param name="PhaseProgress">Worked share of the phase (0-1; 1 once built).</param>
/// <param name="Progress">Share of the whole build done (0-1).</param>
/// <param name="Suspended">True on a closed-season day the phase stands still.</param>
/// <param name="WaitingReason">Why a waiting offshore project waits ("tow", "sheltering", "funds"), else "".</param>
public sealed record FacilityBuildView(string ProjectId, string Status, string PhaseKey, int PhaseIndex, int PhaseCount,
    double PhaseProgress, double Progress, bool Suspended, string WaitingReason)
{
    /// <summary>True once the build is done (or there never was one).</summary>
    public bool IsBuilt => PhaseIndex >= PhaseCount;
}

/// <summary>The statuses a <see cref="FacilityBuildView"/> reports.</summary>
public static class FacilityBuildStatus
{
    /// <summary>Built at once (scenario, test) or a legacy unit: no project.</summary>
    public const string None = "none";
    /// <summary>The project waits (an offshore tow, money).</summary>
    public const string Waiting = "waiting";
    /// <summary>A phase is under way.</summary>
    public const string Building = "building";
    /// <summary>The project is done.</summary>
    public const string Complete = "complete";
    /// <summary>The phase key of a built unit.</summary>
    public const string Operational = "operational";
}

/// <summary>One tank of a land unit as the presenter draws it (F2-20).</summary>
/// <param name="Id">The tank's id ("&lt;site&gt;/tNN").</param>
/// <param name="Number">Its number on the site.</param>
/// <param name="Role">"grow_out", "pre_grow_out" or "post_smolt".</param>
/// <param name="DiameterM">Inside diameter (m).</param>
/// <param name="Ready">True when it takes fish today.</param>
/// <param name="Stocked">True when it holds live fish.</param>
/// <param name="FishCount">Live fish in it.</param>
public sealed record FacilityTankView(string Id, int Number, string Role, double DiameterM, bool Ready, bool Stocked, long FishCount);

/// <summary>One lot (or post-smolt module) of a land unit as the presenter draws it (F2-20).</summary>
/// <param name="LotId">The lot's id ("&lt;site&gt;/lotN").</param>
/// <param name="Template">"hfs_lot", "hfs_pilot_lot", "ras_hall" or "post_smolt_module".</param>
/// <param name="Tanks">Its tanks in tank order (grow-out first).</param>
/// <param name="Build">The project building it.</param>
/// <param name="Expansion">The project growing a pilot into a full lot, or null.</param>
public sealed record FacilityLotView(string LotId, string Template, IReadOnlyList<FacilityTankView> Tanks,
    FacilityBuildView Build, FacilityBuildView? Expansion);

/// <summary>
/// One farm-type unit as the 3D presenter draws it (roadmap F2-20,
/// docs/FARM_TYPES_SPEC.md §4, §5, §6.3): what stands on which plot, its lots
/// and tanks (land) or zones (offshore), how far its build has come and
/// whether it feeds. A pure read of the engine's units; the presenter never
/// decides anything from it.
/// </summary>
/// <param name="UnitId">The land site's or offshore sea site's id.</param>
/// <param name="Name">The name the player reads.</param>
/// <param name="Family">"land" or "hav" (<see cref="FarmTypeIds"/>).</param>
/// <param name="Variant">"hfs", "ras", "post_smolt", "of1" or "havfarm" (<see cref="FarmTypeIds"/>).</param>
/// <param name="PlotId">The plot it stands on.</param>
/// <param name="PlotX">The plot's centre x (world metres).</param>
/// <param name="PlotZ">The plot's centre z (world metres).</param>
/// <param name="Lots">A land unit's lots or modules (empty offshore).</param>
/// <param name="Zones">An offshore unit's zones (0 on land).</param>
/// <param name="StockedZones">Offshore zones holding fish (0 on land).</param>
/// <param name="Build">The unit's own build: a land site's first lot (the one that blasted the pad), an offshore unit's project.</param>
/// <param name="Operational">True once a tank takes fish (land) or the unit is built (offshore).</param>
/// <param name="Feeding">True when it feeds today: built, fish aboard, an offshore unit not locked down by the sea.</param>
/// <param name="FishCount">Live fish.</param>
/// <param name="BiomassTonnes">Standing biomass (t).</param>
/// <param name="HasRiverIntake">True when the unit's pad draws river water (pad_delta, spec §4).</param>
/// <param name="Legacy">True for a unit with no lots or project (a pre-F2 site, a legacy build): its lots are derived from its size.</param>
public sealed record FacilityVisualView(string UnitId, string Name, string Family, string Variant, string PlotId,
    double PlotX, double PlotZ, IReadOnlyList<FacilityLotView> Lots, int Zones, int StockedZones, FacilityBuildView Build,
    bool Operational, bool Feeding, long FishCount, double BiomassTonnes, bool HasRiverIntake, bool Legacy)
{
    /// <summary>Every tank of the unit, lot by lot.</summary>
    public int TankCount => Lots.Sum(l => l.Tanks.Count);
}

/// <summary>The facility presenter's read (F2-20): every land site and offshore unit as the 3D scene draws it.</summary>
public partial class SimulationEngine
{
    /// <summary>Role ids the presenter reads, by <see cref="TankRole"/>.</summary>
    public static string TankRoleId(TankRole role) => role switch
    {
        TankRole.GrowOut => "grow_out",
        TankRole.PreGrowOut => "pre_grow_out",
        TankRole.PostSmolt => "post_smolt",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown tank role"),
    };

    /// <summary>Template ids the presenter reads, by <see cref="LandTemplate"/>.</summary>
    public static string LandTemplateId(LandTemplate template) => template switch
    {
        LandTemplate.HfsLot => "hfs_lot",
        LandTemplate.HfsPilotLot => "hfs_pilot_lot",
        LandTemplate.RasHall => "ras_hall",
        LandTemplate.PostSmoltModule => "post_smolt_module",
        _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown land template"),
    };

    /// <summary>
    /// The company's farm-type units for the 3D presenter: land sites first
    /// (list order), then offshore units (list order). Reads only: no state,
    /// stream or cache changes (FacilityVisualViewsTests compares the save
    /// and every stream before and after).
    /// </summary>
    public IReadOnlyList<FacilityVisualView> FacilityVisualViews()
    {
        var views = new List<FacilityVisualView>();
        if (State == null)
            return views;
        foreach (var site in LandSites)
            views.Add(LandVisualOf(site));
        foreach (var site in OffshoreUnits)
        {
            if (site.Offshore != null)
                views.Add(OffshoreVisualOf(site));
        }
        return views;
    }

    // ── Land ─────────────────────────────────────────────────────────────────

    private FacilityVisualView LandVisualOf(RASFacility site)
    {
        var plot = FindPlot(site.PlotId);
        bool postSmolt = site.Kind == LandSiteKind.PostSmolt;
        var lots = site.HasLots ? site.Lots.Select(lot => LotVisualOf(site, lot)).ToList() : LegacyLots(site);
        // The site's own build: the first lot's (it blasts the pad and brings the quay, the hall and the intake).
        var build = lots.Count > 0 ? lots[0].Build : NoBuild(true, LandPhaseKeys(LandTemplate.HfsLot));
        bool operational = site.HasLots ? site.Lots.Any(IsLotCommissionedNow) : site.IsOperational;
        long fish = site.Tanks.Sum(p => (long)p.TotalCount);
        string variant = postSmolt ? FarmTypeIds.PostSmolt
            : site.HasLots && site.Lots.All(l => l.Template == LandTemplate.RasHall) ? FarmTypeIds.Ras
            : FarmTypeIds.Hfs;
        return new FacilityVisualView(site.Id, site.Name, FarmTypeIds.Land, variant, site.PlotId,
            plot?.WorldX ?? 0.0, plot?.WorldZ ?? 0.0, lots, 0, 0, build, operational, operational && fish > 0, fish,
            site.GetTotalBiomassKg() / 1000.0, plot?.HasRiverIntake ?? false, !site.HasLots);
    }

    private FacilityLotView LotVisualOf(RASFacility site, LandLot lot)
    {
        var tanks = new List<FacilityTankView>(lot.Tanks.Count);
        for (int i = 0; i < lot.Tanks.Count; i++)
        {
            var tank = lot.Tanks[i];
            long fish = site.PopulationIn(tank)?.TotalCount ?? 0;
            tanks.Add(new FacilityTankView(tank.Pen.Id, tank.Pen.Number, TankRoleId(tank.Role), tank.DiameterM,
                IsTankReadyNow(lot, i), fish > 0, fish));
        }
        var keys = LandPhaseKeys(lot.Template);
        var build = BuildOf(FindProject(lot.ConstructionProjectId), keys, IsLotCommissionedNow(lot) && LotCompleteDayNow(lot) >= 0);
        var expansionProject = FindProject(lot.ExpansionProjectId);
        var expansion = expansionProject == null ? null : BuildOf(expansionProject, keys, false);
        return new FacilityLotView(lot.Id, LandTemplateId(lot.Template), tanks, build, expansion);
    }

    /// <summary>
    /// A pre-F2 or legacy-built land site's lots, derived from its size the
    /// way F2-03's migration counts them (grow-out: min(4, ceil(t/yr / 7,900))
    /// full HFS lots; post-smolt: its modules of 2 tanks); its fish fill the
    /// tanks in order; every lot shares the site's own 0-1 progress, laid over
    /// the template's phase plan.
    /// </summary>
    private List<FacilityLotView> LegacyLots(RASFacility site)
    {
        bool postSmolt = site.Kind == LandSiteKind.PostSmolt;
        var template = postSmolt ? LandTemplate.PostSmoltModule : LandTemplate.HfsLot;
        int lots = postSmolt ? PostSmoltModulesOf(site) : GrowOutLotsFor(site.CapacityTonnesPerYear);
        var t = LandCalibration.For(template);
        var build = LegacyBuild(site.ConstructionProgress, LandPhasePlan(template));
        var fish = site.Tanks.Select(p => (long)p.TotalCount).Where(n => n > 0).ToList();
        var views = new List<FacilityLotView>(lots);
        int number = 1, stocked = 0;
        for (int l = 1; l <= lots; l++)
        {
            var tanks = new List<FacilityTankView>();
            var growRole = postSmolt ? TankRole.PostSmolt : TankRole.GrowOut;
            void Add(TankRole role, double diameter)
            {
                long n = stocked < fish.Count ? fish[stocked] : 0;
                stocked++;
                tanks.Add(new FacilityTankView($"{site.Id}/t{number:00}", number, TankRoleId(role), diameter, site.IsOperational, n > 0, n));
                number++;
            }
            for (int k = 0; k < t.GrowOutTanks; k++) Add(growRole, t.GrowOutDiameterM);
            for (int k = 0; k < t.PreGrowTanks; k++) Add(TankRole.PreGrowOut, t.PreGrowDiameterM);
            views.Add(new FacilityLotView(LandLot.ComposeId(site.Id, l), LandTemplateId(template), tanks, build, null));
        }
        return views;
    }

    /// <summary>The phase keys of a land template's build, in order (phases of 0 days are not built, F2-14).</summary>
    public IReadOnlyList<string> LandPhaseKeys(LandTemplate template)
        => LandPhasePlan(template).Where(p => p.Days > 0).Select(p => p.Key).ToList();

    private IReadOnlyList<(string Key, int Days)> LandPhasePlan(LandTemplate template)
    {
        var plan = LandConstructionPhases.PlanOf(LandCalibration.Build, template, LandBuildKind.NewLot);
        return new[]
        {
            (LandConstructionPhases.SiteWorks, plan.SiteWorksDays),
            (LandConstructionPhases.TankRings, plan.TankRingsDays),
            (LandConstructionPhases.TreatmentHall, plan.TreatmentHallDays),
            (LandConstructionPhases.SeaWorks, plan.SeaWorksDays),
            (LandConstructionPhases.FirstTanks, plan.FirstTanksDays),
            (LandConstructionPhases.Completion, plan.CompletionDays),
        }.Where(p => p.Item2 > 0).ToList();
    }

    // ── Offshore ─────────────────────────────────────────────────────────────

    private FacilityVisualView OffshoreVisualOf(SeaSite site)
    {
        var unit = site.Offshore!;
        var plot = FindPlot(site.PlotId);
        long fish = site.Pens.Sum(p => (long)p.TotalCount);
        int zones = site.PhysicalPens.Count;
        int stocked = site.PhysicalPens.Count(p => site.PopulationIn(p)?.TotalCount > 0);
        string variant = unit.FarmType == OffshoreType.ShipShaped ? FarmTypeIds.Havfarm : FarmTypeIds.Of1;
        var build = OffshoreBuildOf(site, unit);
        bool operational = unit.IsOperational;
        return new FacilityVisualView(site.Id, site.Label, FarmTypeIds.Hav, variant, site.PlotId,
            plot?.WorldX ?? 0.0, plot?.WorldZ ?? 0.0, Array.Empty<FacilityLotView>(), zones, stocked, build, operational,
            operational && fish > 0 && !unit.IsStormLockdown, fish, site.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0,
            false, OffshoreProjectOf(site) == null);
    }

    private FacilityBuildView OffshoreBuildOf(SeaSite site, OffshoreUnit unit)
    {
        var project = OffshoreProjectOf(site);
        var cal = OffshoreProfiles.ClassOf(unit.FarmType, OffshoreCal);
        if (project == null)
        {
            if (cal == null)
                return NoBuild(true, Array.Empty<string>());
            var plan = OffshoreProfiles.PhasesFor(cal, OffshoreCal.Units.Payments, OffshoreCal.Operability)
                .Select(p => (p.Key, p.PlannedDays)).ToList();
            return unit.IsOperational ? NoBuild(true, plan.Select(p => p.Key).ToList()) : LegacyBuild(unit.ConstructionProgress, plan);
        }
        var view = GetOffshoreConstruction(site.Id)!;
        int count = project.Phases.Count;
        bool complete = project.Status == ConstructionStatus.Complete || unit.IsOperational;
        string status = complete ? FacilityBuildStatus.Complete
            : project.Status == ConstructionStatus.Building ? FacilityBuildStatus.Building
            : FacilityBuildStatus.Waiting;
        int index = complete ? count : Math.Clamp(project.CurrentPhase, 0, count);
        string key = complete ? FacilityBuildStatus.Operational : project.Phases[index].Key;
        return new FacilityBuildView(project.Id, status, key, index, count, complete ? 1.0 : view.PhaseProgress,
            complete ? 1.0 : view.OverallProgress, false, complete ? "" : view.WaitingReason);
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    /// <summary>A land project's build view (or none: a lot built at once).</summary>
    private FacilityBuildView BuildOf(ConstructionProject? project, IReadOnlyList<string> keys, bool done)
    {
        if (project == null)
            return NoBuild(true, keys);
        int count = project.Phases.Count;
        if (project.Status == ConstructionStatus.Complete || done)
            return new FacilityBuildView(project.Id, FacilityBuildStatus.Complete, FacilityBuildStatus.Operational, count, count, 1.0, 1.0, false, "");
        int index = Math.Clamp(project.CurrentPhase, 0, Math.Max(0, count - 1));
        var phase = project.Current;
        bool building = project.Status == ConstructionStatus.Building;
        bool suspended = building && phase is { SuspendedInClosedMonths: true } && phase.ClosedMonths.Contains(SimCalendar.Month(State.Day));
        return new FacilityBuildView(project.Id, building ? FacilityBuildStatus.Building : FacilityBuildStatus.Waiting,
            project.Phases[index].Key, index, count, PhaseProgressOf(project), ProjectProgressOf(project), suspended, "");
    }

    private static FacilityBuildView NoBuild(bool built, IReadOnlyList<string> keys)
        => new("", FacilityBuildStatus.None, built ? FacilityBuildStatus.Operational : (keys.Count > 0 ? keys[0] : ""),
            built ? keys.Count : 0, keys.Count, built ? 1.0 : 0.0, built ? 1.0 : 0.0, false, "");

    /// <summary>
    /// A legacy unit's phase: its 0-1 progress laid over the plan's days
    /// (presentation only: the unit's own day count is what the sim runs).
    /// </summary>
    internal static FacilityBuildView LegacyBuild(double progress, IReadOnlyList<(string Key, int Days)> plan)
    {
        int count = plan.Count;
        if (count == 0 || !(progress < 1.0))
            return new FacilityBuildView("", FacilityBuildStatus.None, FacilityBuildStatus.Operational, count, count, 1.0, 1.0, false, "");
        double total = plan.Sum(p => (double)Math.Max(0, p.Days));
        double at = Math.Max(0.0, progress) * total;
        for (int i = 0; i < count; i++)
        {
            double days = Math.Max(0, plan[i].Days);
            if (at < days || i == count - 1)
                return new FacilityBuildView("", FacilityBuildStatus.None, plan[i].Key, i, count,
                    days > 0 ? Math.Clamp(at / days, 0.0, 1.0) : 1.0, Math.Clamp(progress, 0.0, 1.0), false, "");
            at -= days;
        }
        return new FacilityBuildView("", FacilityBuildStatus.None, plan[^1].Key, count - 1, count, 1.0, progress, false, "");
    }
}
