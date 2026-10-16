using Godot;
using LakskongenSim.Core;
using Farm = LakskongenSim.Farm;

// GameBridge.LandSites.cs — partial class slice (roadmap F2-03; the land lane's).
// <summary>
// Land slice: the land sites (grow-out and post-smolt) and the land pads they
// stand on, as lists. Thin projection of the engine's views
// (SimulationEngine.LandSiteViews, SitePlotViews; Core/FarmUnitViews.cs).
// F2-03 owns the list verbs above the lane block; the land lane (F2-12 water
// and tanks, F2-13 post-smolt, F2-14 permits and construction, F2-15 biology,
// F2-16 incidents) appends its verbs only inside the marked block, and the sea
// lane never edits this file (FARM_TYPES_SPEC §7.6).
// </summary>
/// <summary>
/// GameBridge partial — F2-03 land sites and pads (the land lane's slice).
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// The company's land sites, grow-out and post-smolt, in list order. Keys:
    /// "id", "name", "kind" ("GrowOut", "PostSmolt"), "plot_id", "zone_id",
    /// "tanks", "tank_volume_m3", "capacity_tonnes_per_year", "modules",
    /// "fish_capacity" (a post-smolt site's fish at once), "transfer_weight_g",
    /// "populations", "fish_count", "biomass_tonnes", "mean_weight_kg",
    /// "water_temp_c", "oxygen_mgl", "co2_mgl", "operational",
    /// "construction_progress", "employee_count".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetLandSites()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var site in SimulationEngine.LandSiteViews())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = site.Id,
                ["name"] = site.Name,
                ["kind"] = site.Kind,
                ["plot_id"] = site.PlotId,
                ["zone_id"] = site.ZoneId,
                ["tanks"] = site.Tanks,
                ["tank_volume_m3"] = site.TankVolumeM3,
                ["capacity_tonnes_per_year"] = site.CapacityTonnesPerYear,
                ["modules"] = site.Modules,
                ["fish_capacity"] = site.FishCapacity,
                ["transfer_weight_g"] = site.TransferWeightG,
                ["populations"] = site.Populations,
                ["fish_count"] = site.FishCount,
                ["biomass_tonnes"] = site.BiomassTonnes,
                ["mean_weight_kg"] = site.MeanWeightKg,
                ["water_temp_c"] = site.WaterTemperatureC,
                ["oxygen_mgl"] = site.OxygenMgPerL,
                ["co2_mgl"] = site.Co2MgPerL,
                ["operational"] = site.Operational,
                ["construction_progress"] = site.ConstructionProgress,
                ["employee_count"] = site.EmployeeCount,
            });
        }
        return result;
    }

    /// <summary>
    /// The land pads (spec §4: pad_a "Storholmen", pad_b "Skjærvika",
    /// pad_delta "Elveøyra", then any overflow pad a migrated save uses), free
    /// or held. Keys as PlotDictionary (GameBridge.Offshore.cs).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetLandPlots()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var plot in SimulationEngine.SitePlotViews(Farm.PlotKind.LandPad))
            result.Add(PlotDictionary(plot));
        return result;
    }

    // ═══ BEGIN sim·land lane block (F2-12 … F2-16): append land verbs below this line ═══
    // ═══ END sim·land lane block ═══
}
