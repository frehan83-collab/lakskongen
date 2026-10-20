using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// One farm-type unit as the 3D presenter draws it (roadmap F2-20,
/// docs/FARM_TYPES_SPEC.md §5, §6.3): what stands on which plot, how much of
/// it, and how far its construction has come. A pure read of the engine's
/// state; the presenter never decides anything from it.
/// </summary>
/// <param name="UnitId">The land site's or offshore sea site's id.</param>
/// <param name="Name">The name the player reads.</param>
/// <param name="Family">"land" or "hav" (<see cref="FarmTypeIds"/>).</param>
/// <param name="Variant">"hfs", "post_smolt", "of1" or "havfarm" (<see cref="FarmTypeIds"/>).</param>
/// <param name="PlotId">The plot it stands on.</param>
/// <param name="PlotX">The plot's centre x (world metres).</param>
/// <param name="PlotZ">The plot's centre z (world metres).</param>
/// <param name="Lots">Grow-out lots (0 for a post-smolt site or an offshore unit).</param>
/// <param name="GrowOutTanksPerLot">Grow-out tanks in a lot.</param>
/// <param name="PreGrowTanksPerLot">Pre-grow-out tanks in a lot.</param>
/// <param name="Modules">Post-smolt modules (0 elsewhere).</param>
/// <param name="TanksPerModule">Tanks in a post-smolt module.</param>
/// <param name="Zones">An offshore unit's zones (0 on land).</param>
/// <param name="Tanks">Every tank the unit has (lots × their tanks + modules × theirs; 0 offshore).</param>
/// <param name="StockedTanks">Tanks (or offshore zones) holding fish.</param>
/// <param name="PhaseIndex">The construction phase under way (0-based; = <paramref name="PhaseCount"/> once built).</param>
/// <param name="PhaseCount">Construction phases of the unit's kind.</param>
/// <param name="PhaseKey">The phase's key ("blast", "tanks", "tow" …; "operational" once built).</param>
/// <param name="PhaseProgress">0-1 through the current phase (1 once built).</param>
/// <param name="FromProject">True when the phase comes from a <see cref="ConstructionProject"/>, false when derived from the unit's progress.</param>
/// <param name="Operational">True once built.</param>
/// <param name="Feeding">True on a day the unit feeds (fish aboard, built, not locked down by the sea).</param>
/// <param name="FishCount">Live fish.</param>
/// <param name="BiomassTonnes">Standing biomass (t).</param>
/// <param name="HasRiverIntake">True when the unit's pad draws river water (pad_delta, spec §4).</param>
public sealed record FacilityVisualView(string UnitId, string Name, string Family, string Variant, string PlotId,
    double PlotX, double PlotZ, int Lots, int GrowOutTanksPerLot, int PreGrowTanksPerLot, int Modules, int TanksPerModule,
    int Zones, int Tanks, int StockedTanks, int PhaseIndex, int PhaseCount, string PhaseKey, double PhaseProgress,
    bool FromProject, bool Operational, bool Feeding, long FishCount, double BiomassTonnes, bool HasRiverIntake);

/// <summary>
/// The visible construction phases of a land site and an offshore unit
/// (spec §6.3), with the share of a build each takes when a unit carries no
/// <see cref="ConstructionProject"/> (the legacy build verbs only advance a
/// 0-1 progress). The shares are the real schedules' proportions: a land
/// grow-out campus "first tanks at 22 months, complete at 30" [L:R1] (site
/// works and blasting ~5 months, foundations ~4, tank rings ~11, the hall ~4,
/// intake and outfall ~3, commissioning ~3); an OF1-class unit 18 months at
/// the yard [O:R1, O:R33], an 11-week voyage [UX: FFE 2017] and ≈ 45 days of
/// tow, mooring and certificate (spec §2A, derived). Presentation only: no
/// sim rule reads them.
/// </summary>
public static class FacilityConstructionPhases
{
    /// <summary>Land phases (spec §6.3 "Land phase"): blast pad, foundations, tank rings rise, treatment hall, intake and outfall, commissioning.</summary>
    public static readonly IReadOnlyList<string> Land = new[] { "blast", "foundations", "tanks", "hall", "intake", "commissioning" };

    /// <summary>Their months of a 30-month build [L:R1] (5 + 4 + 11 + 4 + 3 + 3).</summary>
    public static readonly IReadOnlyList<double> LandMonths = new[] { 5.0, 4.0, 11.0, 4.0, 3.0, 3.0 };

    /// <summary>Offshore phases (spec §6.3 "Offshore phase", the yard contract folded into the yard): yard, voyage, tow, mooring, commissioning.</summary>
    public static readonly IReadOnlyList<string> Offshore = new[] { "yard", "voyage", "tow", "mooring", "commissioning" };

    /// <summary>Their days of a ≈ 670-day build: 18 months at the yard [O:R1], an 11-week voyage, then tow, mooring and certificate (≈ 45 d, spec §2A).</summary>
    public static readonly IReadOnlyList<double> OffshoreDays = new[] { 548.0, 77.0, 10.0, 20.0, 15.0 };

    /// <summary>The phase key a unit shows once built.</summary>
    public const string Operational = "operational";

    /// <summary>
    /// The phase (index, 0-1 progress within it) a build at
    /// <paramref name="progress"/> (0-1) of <paramref name="weights"/> stands
    /// in; (count, 1) once complete.
    /// </summary>
    public static (int index, double progress) PhaseAt(IReadOnlyList<double> weights, double progress)
    {
        if (weights.Count == 0)
            throw new ArgumentException("no phases", nameof(weights));
        if (!(progress < 1.0))
            return (weights.Count, 1.0);
        double total = weights.Sum();
        double at = Math.Max(0.0, progress) * total;
        for (int i = 0; i < weights.Count; i++)
        {
            if (at < weights[i])
                return (i, at / weights[i]);
            at -= weights[i];
        }
        return (weights.Count - 1, 1.0);
    }

    /// <summary>
    /// The visible phase of a <see cref="ConstructionProject"/> on day
    /// <paramref name="day"/>: its current phase mapped onto
    /// <paramref name="visible"/> by key when the lane named it so, else by
    /// its share of the project; the progress through it from its start and
    /// length (0 while it waits).
    /// </summary>
    public static (int index, double progress) PhaseOf(ConstructionProject project, IReadOnlyList<string> visible, int day)
    {
        if (project.Status == ConstructionStatus.Complete || project.CurrentPhase >= project.Phases.Count)
            return (visible.Count, 1.0);
        double within = project.Status == ConstructionStatus.Building && project.PhaseDays > 0
            ? Math.Clamp((day - project.PhaseStartDay) / (double)project.PhaseDays, 0.0, 1.0)
            : 0.0;
        string key = project.Phases[project.CurrentPhase].Key;
        for (int i = 0; i < visible.Count; i++)
        {
            if (string.Equals(visible[i], key, StringComparison.Ordinal))
                return (i, within);
        }
        double share = (project.CurrentPhase + within) / project.Phases.Count;
        double scaled = share * visible.Count;
        int index = Math.Min(visible.Count - 1, (int)Math.Floor(scaled));
        return (index, Math.Clamp(scaled - index, 0.0, 1.0));
    }
}

/// <summary>The facility presenter's read (F2-20): every land site and offshore unit as the 3D scene draws it.</summary>
public partial class SimulationEngine
{
    /// <summary>
    /// The company's farm-type units for the 3D presenter, land sites first
    /// (list order), then offshore units (list order). A unit's construction
    /// phase comes from its <see cref="ConstructionProject"/> when it has one,
    /// else from its own 0-1 progress over <see cref="FacilityConstructionPhases"/>.
    /// Reads only.
    /// </summary>
    public IReadOnlyList<FacilityVisualView> FacilityVisualViews()
    {
        var views = new List<FacilityVisualView>();
        if (State == null)
            return views;
        var hfs = LakskongenSim.Calibration.LandCalibration.Default.Hfs;
        foreach (var site in LandSites)
        {
            bool postSmolt = site.Kind == LandSiteKind.PostSmolt;
            int modules = postSmolt ? PostSmoltModulesOf(site) : 0;
            int lots = postSmolt ? 0 : GrowOutLotsFor(site.CapacityTonnesPerYear);
            int growPerLot = postSmolt ? 0 : hfs.GrowOutTanks;
            int prePerLot = postSmolt ? 0 : hfs.PreGrowTanks;
            int perModule = postSmolt ? PostSmoltTanksPerModule : 0;
            int tanks = lots * (growPerLot + prePerLot) + modules * perModule;
            long fish = site.Tanks.Sum(p => (long)p.TotalCount);
            int stocked = Math.Min(tanks, site.Tanks.Count(p => p.TotalCount > 0));
            var (index, progress, fromProject) = PhaseOfUnit(site.Id, FacilityConstructionPhases.Land,
                FacilityConstructionPhases.LandMonths, site.ConstructionProgress);
            var plot = FindPlot(site.PlotId);
            views.Add(new FacilityVisualView(site.Id, site.Name, FarmTypeIds.Land,
                postSmolt ? FarmTypeIds.PostSmolt : FarmTypeIds.Hfs, site.PlotId, plot?.WorldX ?? 0.0, plot?.WorldZ ?? 0.0,
                lots, growPerLot, prePerLot, modules, perModule, 0, tanks, stocked, index,
                FacilityConstructionPhases.Land.Count, KeyOf(FacilityConstructionPhases.Land, index), progress, fromProject,
                site.IsOperational, site.IsOperational && fish > 0, fish, site.GetTotalBiomassKg() / 1000.0,
                plot?.HasRiverIntake ?? false));
        }
        foreach (var site in OffshoreUnits)
        {
            var unit = site.Offshore;
            if (unit == null)
                continue;
            long fish = site.Pens.Sum(p => (long)p.TotalCount);
            int zones = site.PhysicalPens.Count;
            int stocked = site.PhysicalPens.Count(p => site.PopulationIn(p)?.TotalCount > 0);
            var (index, progress, fromProject) = PhaseOfUnit(site.Id, FacilityConstructionPhases.Offshore,
                FacilityConstructionPhases.OffshoreDays, unit.ConstructionProgress);
            var plot = FindPlot(site.PlotId);
            string variant = unit.FarmType == OffshoreType.ShipShaped ? FarmTypeIds.Havfarm : FarmTypeIds.Of1;
            views.Add(new FacilityVisualView(site.Id, site.Label, FarmTypeIds.Hav, variant, site.PlotId,
                plot?.WorldX ?? 0.0, plot?.WorldZ ?? 0.0, 0, 0, 0, 0, 0, zones, 0, stocked, index,
                FacilityConstructionPhases.Offshore.Count, KeyOf(FacilityConstructionPhases.Offshore, index), progress, fromProject,
                unit.IsOperational, unit.IsOperational && fish > 0 && !unit.IsStormLockdown, fish,
                site.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0, false));
        }
        return views;
    }

    /// <summary>A unit's visible construction phase: its project's, else its own progress over the weights.</summary>
    private (int index, double progress, bool fromProject) PhaseOfUnit(string unitId, IReadOnlyList<string> visible,
        IReadOnlyList<double> weights, double unitProgress)
    {
        var project = State.ConstructionProjects.LastOrDefault(p => p.UnitId == unitId);
        if (project != null)
        {
            var (i, p) = FacilityConstructionPhases.PhaseOf(project, visible, State.Day);
            return (i, p, true);
        }
        var (index, progress) = FacilityConstructionPhases.PhaseAt(weights, unitProgress);
        return (index, progress, false);
    }

    private static string KeyOf(IReadOnlyList<string> phases, int index)
        => index < phases.Count ? phases[index] : FacilityConstructionPhases.Operational;

    /// <summary>
    /// The yearly capacity (t) a grow-out land site of <paramref name="lots"/>
    /// lots has: the lots (clamped 1-4) times a lot's tonnage, so
    /// <see cref="GrowOutLotsFor"/> gives the lots back. For QA tooling that
    /// stands a site at a chosen size (the full 4-lot campus of the F2-20
    /// budget).
    /// </summary>
    public static double GrowOutCapacityForLots(int lots)
        => Math.Clamp(lots, 1, MaxGrowOutLots) * GrowOutTonnesPerLot;
}
