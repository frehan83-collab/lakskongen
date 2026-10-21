using Godot;
using System.Linq;
using LakskongenSim.Core;

// Geo stages 2-3, track B (docs/geo/STAGE23_PLAN.md §3 "Candidates bridge
// lane"): the open-pen candidates of a production area for the build dialog's
// Sjø step and the map, the build plan and the build at a candidate, and where
// the company's fjord localities lie. A thin projection of
// SimulationEngine.SeaCandidates.cs: every refusal travels as a reason key with
// its translation key (SEA_CANDIDATE_REASON_* for a candidate check,
// EXPANSION_REASON_* for the zone's build gate), never as text the UI parses.
// GameBridge.Expansion.cs is untouched (its signatures are pinned).
public partial class GameBridge : Node
{
    /// <summary>The translation key of a candidate check's reason ("" when allowed).</summary>
    private static string SeaCandidateLabelKey(string reasonKey)
        => string.IsNullOrEmpty(reasonKey) ? "" : "SEA_CANDIDATE_REASON_" + reasonKey.ToUpperInvariant();

    /// <summary>
    /// The translation key of a combined verdict: a candidate reason's own key,
    /// else the expansion reason's (the zone's build gate and cost).
    /// </summary>
    private static string SeaBuildLabelKey(string reasonKey)
        => reasonKey is SeaCandidateReasons.CandidateNotFound or SeaCandidateReasons.CandidateWrongArea
            or SeaCandidateReasons.CandidateTaken or SeaCandidateReasons.CandidateNotSuitable
            ? SeaCandidateLabelKey(reasonKey)
            : ExpansionLabelKey(reasonKey);

    private static Godot.Collections.Dictionary CandidateRow(SeaCandidateView v)
    {
        var c = v.Candidate;
        var components = new Godot.Collections.Dictionary();
        foreach (var pair in c.Components)
            components[pair.Key] = pair.Value;
        var weights = new Godot.Collections.Dictionary();
        foreach (var pair in v.ComponentCosts)
            if (v.Weights.TryGetValue(pair.Key, out double w))
                weights[pair.Key] = w;
        var costs = new Godot.Collections.Dictionary();
        foreach (var pair in v.ComponentCosts)
            costs[pair.Key] = pair.Value;
        var ocean = new Godot.Collections.Dictionary();
        if (c.Ocean != null)
        {
            ocean["current_speed_ms"] = c.Ocean.CurrentSpeedMs;
            ocean["hs_m"] = c.Ocean.HsM;
            ocean["o2_mmol_m3"] = c.Ocean.O2MmolM3;
        }
        return new Godot.Collections.Dictionary
        {
            ["id"] = c.Id,
            ["po"] = c.Po,
            ["rank"] = c.Rank,
            ["label"] = c.Label,
            ["near"] = c.Near,
            ["score"] = c.Score,
            ["components"] = components,
            ["weights"] = weights,
            ["component_costs"] = costs,
            ["binding"] = c.Binding,
            ["river_factor"] = c.RiverFactor,
            ["depth_m"] = c.DepthM,
            ["openness"] = c.Openness,
            ["nearest_farm_km"] = c.NearestFarmKm,
            ["nearest_river_km"] = c.NearestRiverKm,
            ["ocean"] = ocean,
            ["x"] = c.X,
            ["y"] = c.Y,
            ["lon"] = c.Lon,
            ["lat"] = c.Lat,
            ["taken_by_site_id"] = v.TakenBySiteId,
            ["taken_by_site_label"] = v.TakenBySiteLabel,
            ["suggested"] = v.Suggested,
            ["build_zone_id"] = v.BuildZoneId,
            ["can_build"] = v.Verdict.Allowed,
            ["build_reason_key"] = v.Verdict.ReasonKey,
            ["build_label_key"] = SeaBuildLabelKey(v.Verdict.ReasonKey),
        };
    }

    /// <summary>
    /// A production area's 12 open-pen candidates by rank (area 0: the
    /// company's home area). A row: id, po, rank, label (the readable place),
    /// near (the map label as shipped), score (0-1), components {name: 0-1},
    /// weights {component: weight}, component_costs {term: score lost, river
    /// last}, binding, river_factor, depth_m, openness, nearest_farm_km,
    /// nearest_river_km, ocean {current_speed_ms, hs_m, o2_mmol_m3} or {}, x, y
    /// (EPSG:25833), lon, lat, taken_by_site_id, taken_by_site_label, suggested
    /// (the first free one), build_zone_id, can_build,
    /// build_reason_key, build_label_key. Empty before a game starts.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSeaCandidates(int productionArea)
    {
        var rows = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var engine = SimulationEngine;
        if (engine?.State == null) return rows;
        int area = productionArea > 0 ? productionArea : engine.HomeProductionArea;
        foreach (var view in engine.GetSeaCandidates(area))
            rows.Add(CandidateRow(view));
        return rows;
    }

    /// <summary>
    /// What building a locality at a candidate costs, holds and adds: every key
    /// of <see cref="PlanSiteBuild"/> with the same figures, "allowed",
    /// "reason_key" and "label_key" for the combined verdict (the candidate
    /// checks first), plus "candidate" (its row, {} for an unknown id),
    /// "candidate_reason_key" and "candidate_label_key" (the checks alone).
    /// </summary>
    public Godot.Collections.Dictionary PlanSiteBuildAt(string zoneId, string candidateId)
    {
        var engine = SimulationEngine;
        if (engine?.State == null) return NotReady();
        var at = engine.PlanSiteBuildAt(zoneId, candidateId);
        var result = PlanSiteBuild(zoneId);
        result["allowed"] = at.Verdict.Allowed;
        result["reason_key"] = at.Verdict.ReasonKey;
        result["label_key"] = SeaBuildLabelKey(at.Verdict.ReasonKey);
        result["candidate"] = at.Candidate != null ? CandidateRow(at.Candidate) : new Godot.Collections.Dictionary();
        result["candidate_reason_key"] = at.CandidateVerdict.ReasonKey;
        result["candidate_label_key"] = SeaCandidateLabelKey(at.CandidateVerdict.ReasonKey);
        return result;
    }

    /// <summary>
    /// Build a fjord locality at a candidate (the BuildSiteDialog's confirm).
    /// Keys: those of <see cref="BuildExpansionSite"/> ("success",
    /// "reason_key", "label_key", "message", "zone_id", "site_id",
    /// "site_label") plus "candidate_id".
    /// </summary>
    public Godot.Collections.Dictionary BuildExpansionSiteAt(string zoneId, string candidateId)
    {
        var engine = SimulationEngine;
        if (engine?.State == null) return NotReady();
        var outcome = engine.BuildSeaSiteAt(zoneId, candidateId);
        var result = new Godot.Collections.Dictionary
        {
            ["success"] = outcome.Ok,
            ["reason_key"] = outcome.ErrorKey,
            ["label_key"] = SeaBuildLabelKey(outcome.ErrorKey),
            ["message"] = outcome.Message,
            ["zone_id"] = zoneId,
            ["site_id"] = outcome.Ok ? outcome.Id : "",
            ["site_label"] = outcome.Ok
                ? engine.State.SeaSites.FirstOrDefault(s => s.Id == outcome.Id)?.Label ?? outcome.Id
                : "",
            ["candidate_id"] = outcome.Ok ? candidateId : "",
        };
        if (outcome.Ok)
        {
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, true);
            EmitSignal(SignalName.GameStateUpdated);
        }
        return result;
    }

    /// <summary>
    /// Where the company's fjord localities lie (display only). A row: site_id,
    /// site_label (the site's name), label (the place), po, candidate_id ("" when
    /// none), x, y (EPSG:25833) and source: "candidate" (built at a candidate,
    /// exact) or "anchor" (the zone's anchor locality, approximate).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetPlayerSiteLocations()
    {
        var rows = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var engine = SimulationEngine;
        if (engine?.State == null) return rows;
        foreach (var l in engine.GetPlayerSiteLocations())
            rows.Add(new Godot.Collections.Dictionary
            {
                ["site_id"] = l.SiteId,
                ["site_label"] = l.SiteLabel,
                ["label"] = l.Label,
                ["po"] = l.Po,
                ["candidate_id"] = l.CandidateId,
                ["x"] = l.X,
                ["y"] = l.Y,
                ["source"] = l.Source,
            });
        return rows;
    }
}
