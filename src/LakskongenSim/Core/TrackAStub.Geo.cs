// ═══════════════════════════════════════════════════════════════════════════
// TRACK-A STUB (geo stages 2-3, docs/geo/STAGE23_PLAN.md §3) — DELETE AT MERGE.
//
// Track B (geo/sea-candidates) codes against track A's not-yet-merged
// interfaces by their exact §3 names. This file stands in for them until
// track A (geo/sim-data-2: A2 SimulationEngine.Geo.cs / GeoViews.cs, A3
// Environment/Geo/ScenarioGeoAnchors.cs) is on geo/int:
//   - ScenarioGeoAnchors.AnchorOf(zoneId, productionArea) and GeoAnchor
//     (used by SimulationEngine.GetPlayerSiteLocations, 'anchor' rows);
//   - SimulationEngine.GeoBindingOf(plotId) and GeoPlotBinding (used by the
//     stub bridge GameBridge.TrackAStub.cs for PlotLens's hav_1 rows).
// Once A's code is merged these types collide (CS0101/CS0111): the merge
// captain deletes this file, godot/scripts/autoload/GameBridge.TrackAStub.cs
// (+ .uid) and the STUB sim·geo lane block in SimProxies.gd/SimBridge.gd.
// Nothing here is saved, traced or read on a tick.
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>STUB for track A's anchor record (plan §3 "Zone anchors and rival layout").</summary>
    public sealed record GeoAnchor(int LocalityId, int X, int Y, string Name, string Source);

    /// <summary>
    /// STUB for track A's ScenarioGeoAnchors (A3): no authored table. A catalogue
    /// zone anchors to the first producing sea grow-out locality by id in its
    /// municipality and its own area ("municipality"); any other zone to the
    /// area's producing locality with the smallest stable hash ("fallback").
    /// </summary>
    public static class ScenarioGeoAnchors
    {
        public static GeoAnchor AnchorOf(string zoneId, int productionArea)
        {
            var all = GeoLocalities.Default.SeaGrowoutProducing;
            var pool = all.Where(l => l.Po == productionArea).ToList();
            if (pool.Count == 0) pool = all.ToList();
            if (ExpansionZones.TryGet(zoneId, out var zone))
            {
                var local = pool.Where(l => string.Equals(l.Municipality, zone.Name, StringComparison.Ordinal))
                    .OrderBy(l => l.Id).FirstOrDefault();
                if (local != null) return new GeoAnchor(local.Id, local.X, local.Y, local.Name, "municipality");
            }
            var pick = pool.OrderBy(l => StableHash.Of("geo_anchor|" + (zoneId ?? ""), l.Id)).ThenBy(l => l.Id).First();
            return new GeoAnchor(pick.Id, pick.X, pick.Y, pick.Name, "fallback");
        }
    }
}

namespace LakskongenSim.Core
{
    /// <summary>STUB for track A's hav_1 binding record (plan §3 "Geo plot catalogue").</summary>
    public sealed record GeoPlotBinding(string PlotId, string CandidateId, string Area, string Stage, double StageFactor,
        int Po, double DepthM, double Score, string Binding, double DistanceKm, long X, long Y, double Lon, double Lat,
        string Near);

    public partial class SimulationEngine
    {
        /// <summary>
        /// STUB for track A's GeoBindingOf (A2): hav_1 binds to the Havbruk til
        /// havs candidate with the least integer squared distance from the home
        /// area's label point (ties: higher score, lower rank, ordinal id); every
        /// other plot is unbound (null). Display only; the plot catalogue is untouched.
        /// </summary>
        public GeoPlotBinding? GeoBindingOf(string plotId)
        {
            if (plotId != "hav_1" || State == null) return null;
            int home = HomeProductionArea;
            if (!ProductionAreas.IsValid(home)) return null;
            var (lx, ly) = GeoProductionAreas.Default.Get(home).Label25833;
            long D2(GeoCandidate c) => (c.X - lx) * (c.X - lx) + (c.Y - ly) * (c.Y - ly);
            var best = GeoCandidates.Default.Offshore
                .OrderBy(D2).ThenByDescending(c => c.Score).ThenBy(c => c.Rank).ThenBy(c => c.Id, StringComparer.Ordinal)
                .First();
            return new GeoPlotBinding(plotId, best.Id, best.Area ?? "", best.Stage ?? "", best.StageFactor ?? 1.0,
                best.Po, best.DepthM, best.Score, best.Binding, Math.Sqrt(D2(best)) / 1000.0, best.X, best.Y,
                best.Lon, best.Lat, best.Near);
        }
    }
}
