using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;

// Geo stages 2-3, track A (docs/geo/STAGE23_PLAN.md §3 "Geo bridge lane"): the
// real coast for the map. Pull-only reads, no signals, nothing written: the
// production areas, hav_1's open-sea binding, the Havbruk til havs candidates,
// "which area is this point in", the localities with their rival owners (packed
// arrays, built once per layout signature), an area's rival summary and the
// zones' anchor localities. SimProxies.gd and SimBridge.gd forward these in the
// sim·geo lane block.
public partial class GameBridge : Node
{
    /// <summary>
    /// The localities' kinds as kind bytes index them: placement × group, in the
    /// order localities.geojson validates them (sea, land, offshore ×
    /// salmonid, marine_fish, shellfish, algae, other).
    /// </summary>
    private static readonly string[] GeoKinds =
        (from placement in new[] { "sea", "land", "offshore" }
         from grp in new[] { "salmonid", "marine_fish", "shellfish", "algae", "other" }
         select $"{placement}_{grp}").ToArray();

    /// <summary>The localities' stages as stage bytes index them.</summary>
    private static readonly string[] GeoStages = { "growout", "smolt", "broodstock", "ranching", "nursery", "other" };

    /// <summary>The static half of GetGeoSites (ids, positions, kind, stage, area, MTB, names), built once per process.</summary>
    private static readonly Lazy<GeoSiteArrays> GeoSiteStatic = new(BuildGeoSiteArrays);

    private string _geoOwnerSignature = "";
    private int[] _geoOwnerIndex = Array.Empty<int>();
    private Godot.Collections.Array<Godot.Collections.Dictionary> _geoOwners = new();

    private sealed record GeoSiteArrays(int[] Ids, int[] X, int[] Y, byte[] Kind, byte[] Stage, byte[] Po, float[] MtbT, string[] Names);

    private static GeoSiteArrays BuildGeoSiteArrays()
    {
        var all = GeoLocalities.Default.All;
        var kindIndex = GeoKinds.Select((k, i) => (k, i)).ToDictionary(t => t.k, t => (byte)t.i, StringComparer.Ordinal);
        var stageIndex = GeoStages.Select((s, i) => (s, i)).ToDictionary(t => t.s, t => (byte)t.i, StringComparer.Ordinal);
        byte other = stageIndex["other"];
        return new GeoSiteArrays(
            all.Select(l => l.Id).ToArray(),
            all.Select(l => l.X).ToArray(),
            all.Select(l => l.Y).ToArray(),
            all.Select(l => kindIndex[l.Kind]).ToArray(),
            all.Select(l => stageIndex.TryGetValue(l.Stage, out byte s) ? s : other).ToArray(),
            all.Select(l => (byte)l.Po).ToArray(),
            all.Select(l => (float)(l.MtbT ?? 0.0)).ToArray(),
            all.Select(l => l.Name).ToArray());
    }

    /// <summary>
    /// The 13 production areas (PO1 first). Keys: id (1-13), code, name,
    /// official_traffic_light (the source's green/yellow/red, not the engine's
    /// light), tint ("#RRGGBB"), area_km2, sea_area_km2, label_x, label_y
    /// (EPSG:25833 m), label_lon, label_lat.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGeoProductionAreas()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var a in GeoProductionAreas.Default.All)
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = a.Number,
                ["code"] = a.Code,
                ["name"] = a.Name,
                ["official_traffic_light"] = a.OfficialTrafficLight,
                ["tint"] = a.Tint,
                ["area_km2"] = a.AreaKm2,
                ["sea_area_km2"] = a.SeaAreaKm2,
                ["label_x"] = a.Label25833.X,
                ["label_y"] = a.Label25833.Y,
                ["label_lon"] = a.Label.Lon,
                ["label_lat"] = a.Label.Lat,
            });
        return result;
    }

    /// <summary>
    /// The real open-sea area behind a plot. Keys: bound (false for off_1,
    /// off_2, the pads, an unknown id or a game on the default catalogue),
    /// plot_id, and when bound: candidate_id, area, stage, stage_factor, po,
    /// depth_m, score, binding, distance_km (from the home area's label point),
    /// x, y (EPSG:25833 m), lon, lat, near, home_po (the home area measured
    /// from: a po other than home_po is the nearest open-sea area of a home
    /// area with none of its own, which the lens says).
    /// </summary>
    public Godot.Collections.Dictionary GetGeoPlotBinding(string plotId)
    {
        var engine = SimulationEngine;
        var binding = engine?.State != null ? engine.GeoBindingOf(plotId ?? "") : null;
        if (binding == null || engine?.State == null)
            return new Godot.Collections.Dictionary { ["bound"] = false, ["plot_id"] = plotId ?? "" };
        return new Godot.Collections.Dictionary
        {
            ["bound"] = true,
            ["plot_id"] = binding.PlotId,
            ["candidate_id"] = binding.CandidateId,
            ["area"] = binding.Area,
            ["stage"] = binding.Stage,
            ["stage_factor"] = binding.StageFactor,
            ["po"] = binding.Po,
            ["depth_m"] = binding.DepthM,
            ["score"] = binding.Score,
            ["binding"] = binding.Binding,
            ["distance_km"] = binding.DistanceKm,
            ["x"] = binding.X,
            ["y"] = binding.Y,
            ["lon"] = binding.Lon,
            ["lat"] = binding.Lat,
            ["near"] = binding.Near,
            ["home_po"] = engine.State.HomeProductionArea,
        };
    }

    /// <summary>distance_km in <see cref="GetGeoOffshoreCandidates"/> when the game has no home area to measure from.</summary>
    private const double NoHomeDistanceKm = -1.0;

    /// <summary>
    /// All 33 Havbruk til havs candidates in file order. Keys: id,
    /// candidate_id, area, stage, stage_factor, po, rank, depth_m, score,
    /// binding, distance_km (from the home area's label point, measured as
    /// hav_1's binding measures it; <see cref="NoHomeDistanceKm"/> (-1) in a
    /// game with no home area, such as a game with no scenario or before any
    /// game), x, y, lon, lat, near, bound (true on the one hav_1 is bound to)
    /// and bound_plot_id ("hav_1" there, "" elsewhere).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGeoOffshoreCandidates()
    {
        var engine = SimulationEngine;
        string boundId = engine?.State != null ? engine.GeoBindingOf(GeoSitePlotGeometry.BoundPlotId)?.CandidateId ?? "" : "";
        int home = engine?.State != null ? engine.HomeProductionArea : ProductionAreas.NationalReference;
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var c in GeoCandidates.Default.Offshore)
        {
            bool bound = c.Id == boundId;
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = c.Id,
                ["candidate_id"] = c.Id,
                ["area"] = c.Area ?? "",
                ["stage"] = c.Stage ?? "",
                ["stage_factor"] = c.StageFactor ?? 1.0,
                ["po"] = c.Po,
                ["rank"] = c.Rank,
                ["depth_m"] = c.DepthM,
                ["score"] = c.Score,
                ["binding"] = c.Binding,
                ["distance_km"] = GeoSitePlotGeometry.DistanceKmFrom(home, c) ?? NoHomeDistanceKm,
                ["x"] = c.X,
                ["y"] = c.Y,
                ["lon"] = c.Lon,
                ["lat"] = c.Lat,
                ["near"] = c.Label,
                ["bound"] = bound,
                ["bound_plot_id"] = bound ? GeoSitePlotGeometry.BoundPlotId : "",
            });
        }
        return result;
    }

    /// <summary>
    /// The production area (1-13) holding an EPSG:25833 point, or 0: the
    /// polygons, for points the coarse po_index image does not cover (land,
    /// inner waters). Display only.
    /// </summary>
    public int GeoAreaAt(double x, double y)
        => double.IsFinite(x) && double.IsFinite(y) ? GeoProductionAreas.Default.AreaAtUtm(x, y) : 0;

    /// <summary>
    /// The rival layout's signature ("" before a game): the map refetches
    /// GetGeoSites only when it changes (a licence win, a merger, a new zone).
    /// </summary>
    public string GetGeoSitesSignature()
        => SimulationEngine?.State != null ? SimulationEngine.RivalLayoutSignature() : "";

    /// <summary>
    /// Every locality (1,781, by id) as packed arrays. Keys: signature; ids, x,
    /// y (PackedInt32Array, EPSG:25833 m); kind, stage, po (PackedByteArray:
    /// indexes into kinds and stages, po 0 = none); mtb_t (PackedFloat32Array,
    /// 0 when the capacity is not in tonnes); owner (PackedInt32Array: index
    /// into owners, -1 for a non-salmonid site, one outside the pool, a
    /// game's own anchor or no game); names (PackedStringArray); kinds and
    /// stages (PackedStringArray); owners (Array of {id, name, is_major}).
    /// The static arrays are built once per process and the owner arrays once
    /// per layout signature.
    /// </summary>
    public Godot.Collections.Dictionary GetGeoSites()
    {
        var s = GeoSiteStatic.Value;
        RefreshGeoOwners(s);
        return new Godot.Collections.Dictionary
        {
            ["signature"] = _geoOwnerSignature,
            ["ids"] = s.Ids,
            ["x"] = s.X,
            ["y"] = s.Y,
            ["kind"] = s.Kind,
            ["stage"] = s.Stage,
            ["po"] = s.Po,
            ["mtb_t"] = s.MtbT,
            ["owner"] = _geoOwnerIndex,
            ["names"] = s.Names,
            ["kinds"] = GeoKinds,
            ["stages"] = GeoStages,
            ["owners"] = _geoOwners.Duplicate(true),
        };
    }

    private void RefreshGeoOwners(GeoSiteArrays s)
    {
        var engine = SimulationEngine;
        if (engine?.State == null)
        {
            _geoOwnerSignature = "";
            _geoOwnerIndex = Enumerable.Repeat(-1, s.Ids.Length).ToArray();
            _geoOwners = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            return;
        }
        var layout = engine.GetRivalLayout();
        if (layout.Signature == _geoOwnerSignature && _geoOwnerIndex.Length == s.Ids.Length) return;
        _geoOwnerIndex = s.Ids.Select(layout.OwnerIndexOf).ToArray();
        var owners = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var o in layout.Owners)
            owners.Add(new Godot.Collections.Dictionary { ["id"] = o.Id, ["name"] = o.Name, ["is_major"] = o.IsMajor });
        _geoOwners = owners;
        _geoOwnerSignature = layout.Signature;
    }

    /// <summary>
    /// A production area's rival picture. Keys: po; sites_by_kind (Dictionary
    /// kind → count, every locality in the area); sea_growout_mtb_t (the
    /// producing sea grow-out MTB, t); top_owners (Array of {id, name,
    /// is_major, sites, mtb_t}: the five owners with the most salmonid sites
    /// there).
    /// </summary>
    public Godot.Collections.Dictionary GetRivalSummary(int po)
    {
        var byKind = new Godot.Collections.Dictionary();
        var top = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var result = new Godot.Collections.Dictionary
        {
            ["po"] = po,
            ["sites_by_kind"] = byKind,
            ["sea_growout_mtb_t"] = 0.0,
            ["top_owners"] = top,
        };
        if (SimulationEngine?.State == null) return result;
        var summary = SimulationEngine.GetRivalSummary(po);
        foreach (var kv in summary.SitesByKind)
            byKind[kv.Key] = kv.Value;
        foreach (var o in summary.TopOwners)
            top.Add(new Godot.Collections.Dictionary
            {
                ["id"] = o.Id,
                ["name"] = o.Name,
                ["is_major"] = o.IsMajor,
                ["sites"] = o.Sites,
                ["mtb_t"] = o.MtbT,
            });
        result["sea_growout_mtb_t"] = summary.SeaGrowoutMtbT;
        return result;
    }

    /// <summary>
    /// The anchor locality of every zone of the game and of the expansion
    /// catalogue. Keys: zone_id, production_area, locality_id, name, x, y
    /// (EPSG:25833 m), source ("authored", "municipality" or "fallback"),
    /// in_game. Approximate by design: the sim's zones have no coordinates.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetZoneAnchors()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var a in SimulationEngine.GetZoneAnchors())
            result.Add(new Godot.Collections.Dictionary
            {
                ["zone_id"] = a.ZoneId,
                ["production_area"] = a.ProductionArea,
                ["locality_id"] = a.LocalityId,
                ["name"] = a.Name,
                ["x"] = a.X,
                ["y"] = a.Y,
                ["source"] = a.Source,
                ["in_game"] = a.InGame,
            });
        return result;
    }
}
