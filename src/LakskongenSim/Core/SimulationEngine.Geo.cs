using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The engine's reads of the real coast (docs/geo/STAGE23_PLAN.md track A,
/// A2 and A3): the per-game plot catalogue that binds hav_1 to a Havbruk til
/// havs candidate, the zones' anchor localities, and the rival layout over the
/// real salmonid localities. Everything here is display: no SimRng stream, no
/// GameState field, no tick reads it, and a load rebuilds it from the saved
/// home area and the embedded data.
/// </summary>
public partial class SimulationEngine
{
    private RivalLayoutResult? _rivalLayout;

    /// <summary>
    /// How many times this engine built its rival layout (tests: reads build
    /// it once per roster and anchor signature, not once per read).
    /// </summary>
    internal int RivalLayoutBuilds { get; private set; }

    /// <summary>
    /// Point <see cref="PlotCatalog"/> at the home area's geo catalogue
    /// (<see cref="GeoPlotCatalogs.ForHomeArea"/>): hav_1 bound to the nearest
    /// Havbruk til havs candidate. A game with no home area keeps the default
    /// catalogue. Called at the end of <see cref="StartScenario"/> and in
    /// <see cref="AdoptSavedState"/> before the plots rejoin their templates
    /// (the load has assigned a pre-S2-02 save's area by then).
    /// </summary>
    internal void SelectGeoPlotCatalog()
        => PlotCatalog = GeoPlotCatalogs.ForHomeArea(State?.HomeProductionArea ?? 0);

    /// <summary>
    /// Forget the previous game's geo reads (called from <see cref="Reset"/>):
    /// back to the default plot catalogue, no cached layout, no cached home
    /// counts. GameBridge keeps one engine for the whole session, and a New
    /// Game with no scenario never reaches <see cref="SelectGeoPlotCatalog"/>,
    /// so without this it kept the last scenario's hav_1 binding until a save
    /// and load moved it (captain's review of track A, finding 2).
    /// </summary>
    private void ResetGeoReads()
    {
        PlotCatalog = SitePlotCatalog.Default;
        _rivalLayout = null;
        _homeSiteCounts = null;
    }

    /// <summary>
    /// The real open-sea candidate behind a plot, or null when the plot is not
    /// bound (off_1, off_2, the pads, an unknown id, or a game on the default
    /// catalogue).
    /// </summary>
    public GeoPlotBinding? GeoBindingOf(string plotId)
    {
        if (plotId != GeoSitePlotGeometry.BoundPlotId) return null;
        int area = State?.HomeProductionArea ?? 0;
        var geometry = GeoPlotCatalogs.GeometryFor(area);
        if (geometry == null || !ReferenceEquals(PlotCatalog, GeoPlotCatalogs.ForHomeArea(area))) return null;
        var c = geometry.Candidate;
        return new GeoPlotBinding(plotId, c.Id, c.Area ?? "", c.Stage ?? "", c.StageFactor ?? 1.0, c.Po, c.DepthM,
            c.Score, c.Binding, geometry.DistanceKm, c.X, c.Y, c.Lon, c.Lat, c.Label);
    }

    /// <summary>
    /// The anchor of every zone the game holds (its own area) and of every
    /// expansion-catalogue zone it does not, game zones first in their order,
    /// then the catalogue by area.
    /// </summary>
    public IReadOnlyList<GeoZoneAnchorView> GetZoneAnchors()
    {
        var views = new List<GeoZoneAnchorView>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (State?.Zones != null)
            foreach (var zone in State.Zones)
            {
                if (zone == null || !seen.Add(zone.Id ?? "")) continue;
                int area = ProductionAreaOf(zone);
                views.Add(ZoneAnchorViewOf(zone.Id ?? "", area, ScenarioGeoAnchors.AnchorOf(zone.Id ?? "", area), inGame: true));
            }
        foreach (var catalogue in ExpansionZones.All)
            if (seen.Add(catalogue.ZoneId))
                views.Add(ZoneAnchorViewOf(catalogue.ZoneId, catalogue.ProductionArea,
                    ScenarioGeoAnchors.AnchorOf(catalogue.ZoneId, catalogue.ProductionArea), inGame: false));
        return views;
    }

    private static GeoZoneAnchorView ZoneAnchorViewOf(string zoneId, int area, GeoAnchor anchor, bool inGame)
        => new(zoneId, area, anchor.LocalityId, anchor.Name, anchor.X, anchor.Y, anchor.Source, inGame);

    /// <summary>The localities the game's own zones are drawn at: kept out of the rival layout.</summary>
    private HashSet<int> GameAnchorLocalityIds()
    {
        var ids = new HashSet<int>();
        if (State?.Zones == null) return ids;
        foreach (var zone in State.Zones)
            if (zone != null)
                ids.Add(ScenarioGeoAnchors.AnchorOf(zone.Id ?? "", ProductionAreaOf(zone)).LocalityId);
        return ids;
    }

    /// <summary>
    /// The rival layout for the current roster and the game's anchors
    /// (<see cref="RivalLayout.Build"/>), built again only when its signature
    /// (<see cref="RivalLayout.SignatureOf"/>: the majors' ids, MAB, home
    /// areas and names, and the anchor ids) changes: a licence win, a merger,
    /// a new zone, another game's roster.
    /// </summary>
    public RivalLayoutResult GetRivalLayout()
    {
        var roster = CompetitorAI?.Competitors?.ToArray() ?? Array.Empty<CompetitorCompany>();
        var excluded = GameAnchorLocalityIds();
        string signature = RivalLayout.SignatureOf(roster, excluded);
        if (_rivalLayout != null && _rivalLayout.Signature == signature) return _rivalLayout;
        _rivalLayout = RivalLayout.Build(roster, excluded);
        RivalLayoutBuilds++;
        return _rivalLayout;
    }

    /// <summary>
    /// The signature <see cref="GetRivalLayout"/> would have now, without
    /// building it: the map refetches the layout only when this changes.
    /// </summary>
    public string RivalLayoutSignature()
        => RivalLayout.SignatureOf(CompetitorAI?.Competitors?.ToArray() ?? Array.Empty<CompetitorCompany>(), GameAnchorLocalityIds());

    /// <summary>
    /// A production area's rival picture: every locality there by kind, the
    /// producing sea grow-out MTB, and the five owners with the most salmonid
    /// sites there (then MTB, then id). An area outside 1-13 is empty.
    /// </summary>
    public RivalAreaSummary GetRivalSummary(int productionArea)
    {
        if (!ProductionAreas.IsValid(productionArea))
            return new RivalAreaSummary(productionArea, Array.Empty<KeyValuePair<string, int>>(), 0.0, Array.Empty<RivalOwnerPresence>());
        var layout = GetRivalLayout();
        var byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var sites = new Dictionary<int, int>();
        var tonnes = new Dictionary<int, double>();
        double seaGrowout = 0.0;
        foreach (var l in GeoLocalities.Default.All)
        {
            if (l.Po != productionArea) continue;
            byKind[l.Kind] = byKind.TryGetValue(l.Kind, out int n) ? n + 1 : 1;
            if (l.IsSeaGrowoutProducing) seaGrowout += l.MtbT ?? 0.0;
            int owner = layout.OwnerIndexOf(l.Id);
            if (owner < 0) continue;
            sites[owner] = sites.TryGetValue(owner, out int s) ? s + 1 : 1;
            tonnes[owner] = (tonnes.TryGetValue(owner, out double t) ? t : 0.0) + (l.MtbT ?? 0.0);
        }
        var top = sites
            .OrderByDescending(kv => kv.Value)
            .ThenByDescending(kv => tonnes[kv.Key])
            .ThenBy(kv => layout.Owners[kv.Key].Id, StringComparer.Ordinal)
            .Take(5)
            .Select(kv => new RivalOwnerPresence(layout.Owners[kv.Key].Id, layout.Owners[kv.Key].Name,
                layout.Owners[kv.Key].IsMajor, kv.Value, tonnes[kv.Key]))
            .ToArray();
        return new RivalAreaSummary(productionArea, byKind.ToArray(), seaGrowout, top);
    }

    /// <summary>
    /// The roster ordered for the 3D fjord: by producing sea grow-out sites in
    /// the home production area (most first), then roster order (a stable
    /// sort), read from the cached layout. A game with no home area
    /// (<see cref="ProductionAreas.NationalReference"/>) counts none, so its
    /// order is the roster's.
    /// </summary>
    public IReadOnlyList<RivalHomePresence> CompetitorsByHomePresence()
    {
        var roster = CompetitorAI?.Competitors;
        if (roster == null || roster.Count == 0) return Array.Empty<RivalHomePresence>();
        var layout = GetRivalLayout();
        int home = HomeProductionArea;
        var counts = HomeSiteCounts(layout, home);
        return roster
            .Select(c => new RivalHomePresence(c, counts.TryGetValue(c.Id, out int n) ? n : 0))
            .OrderByDescending(p => p.HomePoSites)   // OrderBy is stable: roster order breaks ties
            .ToArray();
    }

    private (string Signature, int Home, Dictionary<string, int> Counts)? _homeSiteCounts;

    /// <summary>
    /// Each owner's producing sea grow-out sites in the home area, cached per
    /// layout and area. Outside 1-13 there is no home area: every count is 0
    /// (area 0 is not the localities with no area, one of which is a producing
    /// sea site a major can hold).
    /// </summary>
    private Dictionary<string, int> HomeSiteCounts(RivalLayoutResult layout, int home)
    {
        if (_homeSiteCounts is { } cached && cached.Signature == layout.Signature && cached.Home == home)
            return cached.Counts;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        bool hasHome = ProductionAreas.IsValid(home);
        for (int owner = 0; owner < layout.Owners.Count; owner++)
        {
            int count = 0;
            if (hasHome)
                foreach (int id in layout.SitesOf(owner))
                    if (GeoLocalities.Default.TryGet(id, out var l) && l.Po == home && l.IsSeaGrowoutProducing)
                        count++;
            counts[layout.Owners[owner].Id] = count;
        }
        _homeSiteCounts = (layout.Signature, home, counts);
        return counts;
    }
}
