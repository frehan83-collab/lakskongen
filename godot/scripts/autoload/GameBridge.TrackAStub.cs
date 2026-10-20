using Godot;

// ═══════════════════════════════════════════════════════════════════════════
// TRACK-A STUB (geo stages 2-3, docs/geo/STAGE23_PLAN.md §3 "Geo bridge lane")
// — DELETE AT MERGE. Stands in for track A's GameBridge.Geo.cs
// GetGeoPlotBinding until A2 is on geo/int, so PlotLens's hav_1 rows (track B)
// can be built and checked. Same name, signature and keys as the plan; it
// reads the stub SimulationEngine.GeoBindingOf (src/LakskongenSim/Core/
// TrackAStub.Geo.cs). Once A's GameBridge.Geo.cs is merged the two
// GetGeoPlotBinding members collide (CS0111): the merge captain deletes this
// file and its .uid, the engine stub and the STUB sim·geo facade block.
// ═══════════════════════════════════════════════════════════════════════════
public partial class GameBridge : Node
{
    /// <summary>
    /// STUB. hav_1's Havbruk til havs binding. Keys: bound, plot_id,
    /// candidate_id, area, stage, stage_factor, po, depth_m, score, binding,
    /// distance_km, x, y, lon, lat, near ({"bound": false, "plot_id"} for an
    /// unbound plot).
    /// </summary>
    public Godot.Collections.Dictionary GetGeoPlotBinding(string plotId)
    {
        var b = SimulationEngine?.GeoBindingOf(plotId);
        if (b == null)
            return new Godot.Collections.Dictionary { ["bound"] = false, ["plot_id"] = plotId ?? "" };
        return new Godot.Collections.Dictionary
        {
            ["bound"] = true,
            ["plot_id"] = b.PlotId,
            ["candidate_id"] = b.CandidateId,
            ["area"] = b.Area,
            ["stage"] = b.Stage,
            ["stage_factor"] = b.StageFactor,
            ["po"] = b.Po,
            ["depth_m"] = b.DepthM,
            ["score"] = b.Score,
            ["binding"] = b.Binding,
            ["distance_km"] = b.DistanceKm,
            ["x"] = b.X,
            ["y"] = b.Y,
            ["lon"] = b.Lon,
            ["lat"] = b.Lat,
            ["near"] = b.Near,
        };
    }
}
