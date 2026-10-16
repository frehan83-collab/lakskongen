using Godot;
using LakskongenSim.Core;
using Farm = LakskongenSim.Farm;

// GameBridge.Offshore.cs — partial class slice (roadmap F2-03; the sea lane's).
// <summary>
// Offshore slice: the offshore units (offshore sea sites) and the offshore
// slots they stand on, as lists. Thin projection of the engine's views
// (SimulationEngine.OffshoreUnitViews, SitePlotViews; Core/FarmUnitViews.cs).
// F2-03 owns the list verbs above the lane block; the sea lane (F2-06 metocean
// and forecast, F2-07 operations, F2-08 incidents, F2-09 licences, F2-11 the
// area round) appends its verbs only inside the marked block, and the land
// lane never edits this file (FARM_TYPES_SPEC §7.6).
// </summary>
/// <summary>
/// GameBridge partial — F2-03 offshore units and slots (the sea lane's slice).
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// The company's offshore units, in the site list's order. Keys: "id",
    /// "name", "plot_id", "zone_id", "farm_type", "zones", "occupied_zones",
    /// "mtb_tonnes", "fish_count", "biomass_tonnes", "mean_weight_kg",
    /// "design_hs_m", "hs100_m" (its slot's), "external_lice_multiplier",
    /// "lice_per_fish", "operational", "construction_progress",
    /// "storm_lockdown", "wave_height_m", "crew_days_since_rotation",
    /// "crew_rotation_days", "structural_integrity", "employee_count", and
    /// "pens" (its zones, keys as GetSitePens).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOffshoreUnits()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var unit in SimulationEngine.OffshoreUnitViews())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = unit.Id,
                ["name"] = unit.Name,
                ["plot_id"] = unit.PlotId,
                ["zone_id"] = unit.ZoneId,
                ["farm_type"] = unit.FarmType,
                ["zones"] = unit.Zones,
                ["occupied_zones"] = unit.OccupiedZones,
                ["mtb_tonnes"] = unit.MtbTonnes,
                ["fish_count"] = unit.FishCount,
                ["biomass_tonnes"] = unit.BiomassTonnes,
                ["mean_weight_kg"] = unit.MeanWeightKg,
                ["design_hs_m"] = unit.DesignHsM,
                ["hs100_m"] = unit.Hs100M,
                ["external_lice_multiplier"] = unit.ExternalLiceMultiplier,
                ["lice_per_fish"] = unit.LicePerFish,
                ["operational"] = unit.Operational,
                ["construction_progress"] = unit.ConstructionProgress,
                ["storm_lockdown"] = unit.StormLockdown,
                ["wave_height_m"] = unit.WaveHeightM,
                ["crew_days_since_rotation"] = unit.CrewDaysSinceRotation,
                ["crew_rotation_days"] = unit.CrewRotationDays,
                ["structural_integrity"] = unit.StructuralIntegrity,
                ["employee_count"] = unit.EmployeeCount,
                ["pens"] = PenArrayOf(unit.Id),
            });
        }
        return result;
    }

    /// <summary>
    /// The offshore slots (spec §4: off_1 "Ytre kyst", off_2 "Havkanten",
    /// hav_1 "Havområde", then any overflow slot a migrated save uses), free
    /// or held. Keys as <see cref="PlotDictionary"/>.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOffshorePlots()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var plot in SimulationEngine.SitePlotViews(Farm.PlotKind.OffshoreSlot))
            result.Add(PlotDictionary(plot));
        return result;
    }

    /// <summary>
    /// One plot as a dictionary (GetOffshorePlots, GetLandPlots): "id", "name",
    /// "kind" ("LandPad", "OffshoreSlot"), "x", "z" (world metres),
    /// "real_distance_km", "owner_unit_id" ("" when free), "free", "overflow",
    /// "pre_zoned", "width_m", "depth_m", "grow_out_lots", "post_smolt_modules",
    /// "grid_capacity_mw", "exposure" ("" on a pad), "hs100_m", "water_depth_m",
    /// "external_lice_multiplier".
    /// </summary>
    private static Godot.Collections.Dictionary PlotDictionary(SitePlotView plot) => new()
    {
        ["id"] = plot.Id,
        ["name"] = plot.Name,
        ["kind"] = plot.Kind.ToString(),
        ["x"] = plot.WorldX,
        ["z"] = plot.WorldZ,
        ["real_distance_km"] = plot.RealDistanceKm,
        ["owner_unit_id"] = plot.OwnerUnitId,
        ["free"] = plot.IsFree,
        ["overflow"] = plot.IsOverflow,
        ["pre_zoned"] = plot.PreZoned,
        ["width_m"] = plot.PadWidthM,
        ["depth_m"] = plot.PadDepthM,
        ["grow_out_lots"] = plot.GrowOutLots,
        ["post_smolt_modules"] = plot.PostSmoltModules,
        ["grid_capacity_mw"] = plot.GridCapacityMW,
        ["exposure"] = plot.Exposure,
        ["hs100_m"] = plot.Hs100M,
        ["water_depth_m"] = plot.WaterDepthM,
        ["external_lice_multiplier"] = plot.LiceExternalMultiplier,
    };

    // ═══ BEGIN sim·sea lane block (F2-06 … F2-11): append offshore verbs below this line ═══
    // ═══ END sim·sea lane block ═══
}
