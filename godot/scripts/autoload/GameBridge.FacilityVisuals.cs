using Godot;
using LakskongenSim.Core;
using Farm = LakskongenSim.Farm;

// GameBridge.FacilityVisuals.cs — partial class slice (roadmap F2-20; gfx's).
// <summary>
// The facility presenter's read: every land site and offshore unit as the 3D
// scene draws it (plot, lots, modules, tanks, the construction phase, fish),
// a thin projection of SimulationEngine.FacilityVisualViews (pure read). Plus
// two QA-only verbs that UiFlowTests and the QA capture scenes call on the
// autoload (never the player's UI): QaPlaceFarmTypes builds the farm-type QA
// set (one HFS lot, one post-smolt site, one OF1-class unit) through the
// engine's own build verbs, and QaSetFacilityConstruction sets a unit's build
// progress so a test or a shot can stand at a chosen construction phase.
// </summary>
/// <summary>
/// GameBridge partial — F2-20 facility presenter (gfx's slice).
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// The farm-type units for the 3D presenter, land sites first. Keys:
    /// "unit_id", "name", "family" ("land", "hav"), "variant" ("hfs",
    /// "post_smolt", "of1", "havfarm"), "plot_id", "plot_x", "plot_z", "lots",
    /// "grow_out_tanks_per_lot", "pre_grow_tanks_per_lot", "modules",
    /// "tanks_per_module", "zones", "tanks", "stocked_tanks", "phase_index",
    /// "phase_count", "phase_key" (a FacilityConstructionPhases key or
    /// "operational"), "phase_progress" (0-1), "from_project", "operational",
    /// "feeding", "fish_count", "biomass_tonnes", "has_river_intake".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetFacilityVisuals()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var v in SimulationEngine.FacilityVisualViews())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["unit_id"] = v.UnitId,
                ["name"] = v.Name,
                ["family"] = v.Family,
                ["variant"] = v.Variant,
                ["plot_id"] = v.PlotId,
                ["plot_x"] = v.PlotX,
                ["plot_z"] = v.PlotZ,
                ["lots"] = v.Lots,
                ["grow_out_tanks_per_lot"] = v.GrowOutTanksPerLot,
                ["pre_grow_tanks_per_lot"] = v.PreGrowTanksPerLot,
                ["modules"] = v.Modules,
                ["tanks_per_module"] = v.TanksPerModule,
                ["zones"] = v.Zones,
                ["tanks"] = v.Tanks,
                ["stocked_tanks"] = v.StockedTanks,
                ["phase_index"] = v.PhaseIndex,
                ["phase_count"] = v.PhaseCount,
                ["phase_key"] = v.PhaseKey,
                ["phase_progress"] = v.PhaseProgress,
                ["from_project"] = v.FromProject,
                ["operational"] = v.Operational,
                ["feeding"] = v.Feeding,
                ["fish_count"] = v.FishCount,
                ["biomass_tonnes"] = v.BiomassTonnes,
                ["has_river_intake"] = v.HasRiverIntake,
            });
        }
        return result;
    }

    /// <summary>
    /// QA-only (F2-20): build the farm-type QA set through the engine's build
    /// verbs, paid by a QA grant: one HFS lot (a small grow-out land site:
    /// 5,000 t a year, one lot, on pad_a), one post-smolt site (pad_delta) and
    /// one OF1-class unit (off_1). Keys: "success", "message", "land_id",
    /// "post_smolt_id", "offshore_id". Not reachable from player UI.
    /// </summary>
    public Godot.Collections.Dictionary QaPlaceFarmTypes() => QaPlaceFarmTypeSet(1, 1);

    /// <summary>
    /// QA-only (F2-20): <see cref="QaPlaceFarmTypes"/> with the grow-out site
    /// at <paramref name="growOutLots"/> lots (1-4; 4 is the full campus of the
    /// spec §5 budget) and <paramref name="offshoreUnits"/> offshore units
    /// (1-2): the OF1-class unit on off_1, then a Havfarm-class unit on the
    /// next slot its class fits (off_2; the P0-04 run's "a campus, 2 units and
    /// 12 sites"; no OF1-class unit fits a second slot). Extra keys:
    /// "offshore_ids" (every unit built). Not reachable from player UI.
    /// </summary>
    public Godot.Collections.Dictionary QaPlaceFarmTypeSet(int growOutLots, int offshoreUnits)
    {
        var result = new Godot.Collections.Dictionary { ["success"] = false };
        var engine = SimulationEngine;
        if (engine?.State == null || engine.FinanceManager == null)
        {
            result["message"] = "engine_not_ready";
            return result;
        }
        if (growOutLots < 1 || growOutLots > 4 || offshoreUnits < 1 || offshoreUnits > 2)
        {
            result["message"] = "invalid_set";
            return result;
        }
        if (!QaGrantCash(QaGrantCashMaxNok))
        {
            result["message"] = "grant_refused";
            return result;
        }
        var land = engine.BuildRASFacility("", Farm.RASScale.Small);
        var postSmolt = engine.BuildPostSmoltFacility("");
        if (land.Ok)
            QaSetLandLots(land.Id, growOutLots);
        var offshoreIds = new Godot.Collections.Array<string>();
        bool offshoreOk = true;
        string offshoreMessage = "";
        for (int i = 0; i < offshoreUnits; i++)
        {
            // Each unit is paid like a player's: top the QA grant up first.
            if (i > 0) QaGrantCash(QaGrantCashMaxNok);
            var offshore = engine.BuildOffshoreFarm("", i == 0 ? Farm.OffshoreType.SemiSubmersible : Farm.OffshoreType.ShipShaped);
            offshoreOk &= offshore.Ok;
            offshoreMessage += (i > 0 ? " | " : "") + offshore.Message;
            if (offshore.Ok) offshoreIds.Add(offshore.Id ?? "");
        }
        result["success"] = land.Ok && postSmolt.Ok && offshoreOk;
        result["message"] = $"{land.Message} | {postSmolt.Message} | {offshoreMessage}";
        result["land_id"] = land.Id ?? "";
        result["post_smolt_id"] = postSmolt.Id ?? "";
        result["offshore_id"] = offshoreIds.Count > 0 ? offshoreIds[0] : "";
        result["offshore_ids"] = offshoreIds;
        EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    /// <summary>
    /// QA-only (F2-20): stand grow-out land site <paramref name="unitId"/> at
    /// <paramref name="lots"/> grow-out lots (1-4) by its yearly capacity, so
    /// the presenter draws that many lots (the full campus of the budget
    /// shots). Refuses a post-smolt site, an unknown unit, a lot count
    /// outside 1-4 or more lots than its pad holds. Not reachable from player UI.
    /// </summary>
    public bool QaSetLandLots(string unitId, int lots)
    {
        var engine = SimulationEngine;
        if (engine?.State == null || lots < 1 || lots > 4) return false;
        var land = engine.FindLandSite(unitId);
        if (land == null || land.Kind != LakskongenSim.Farm.LandSiteKind.GrowOut) return false;
        var plot = engine.FindPlot(land.PlotId);
        if (plot != null && plot.PhaseLots < lots) return false;
        land.CapacityTonnesPerYear = SimulationEngine.GrowOutCapacityForLots(lots);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// QA-only (F2-20): stand a land site or offshore unit at build progress
    /// <paramref name="progress"/> (0-1; 1 = operational), as if construction
    /// had started that many of its days ago, so the daily progress update
    /// keeps it there on a paused clock and advances it from there otherwise.
    /// Refuses an unknown unit or a progress outside 0-1. Not reachable from
    /// player UI.
    /// </summary>
    public bool QaSetFacilityConstruction(string unitId, double progress)
    {
        var engine = SimulationEngine;
        if (engine?.State == null || !double.IsFinite(progress) || progress < 0.0 || progress > 1.0) return false;
        int day = engine.State.Day;
        var land = engine.FindLandSite(unitId);
        if (land != null)
        {
            land.ConstructionStartDay = day - (int)System.Math.Round(progress * land.ConstructionDays);
            land.ConstructionProgress = progress;
            EmitSignal(SignalName.GameStateUpdated);
            return true;
        }
        var unit = engine.FindOffshoreUnit(unitId)?.Offshore;
        if (unit != null)
        {
            unit.ConstructionStartDay = day - (int)System.Math.Round(progress * unit.ConstructionDays);
            unit.ConstructionProgress = progress;
            EmitSignal(SignalName.GameStateUpdated);
            return true;
        }
        return false;
    }
}
