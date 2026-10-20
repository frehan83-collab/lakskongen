using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// Open-pen candidates in the U2-01/F2-31 build flow (geo stages 2-3, track B;
/// docs/geo/STAGE23_PLAN.md §2 B): the 12 ranked candidates of a production
/// area (<c>candidates.json</c>) as the places a fjord locality is built,
/// with the 'why here' the dialog explains, and the build at a candidate.
/// <list type="bullet">
/// <item><b>Reads are pure.</b> <see cref="GetSeaCandidates"/>,
/// <see cref="PlanSiteBuildAt"/> and <see cref="GetPlayerSiteLocations"/> read
/// the embedded geo data and the state; no RNG stream, no state change.</item>
/// <item><b>The build is U2-01's.</b> <see cref="BuildSeaSiteAt"/> runs the
/// candidate checks, then the unchanged <see cref="BuildSeaSite"/> (the same
/// gate, cost, licence and catalogue zone), then records the candidate on the
/// new site (<see cref="SeaSite.CandidateId"/>). The licence stays per
/// zone (ruling OD7): a candidate rides on the zone the build uses.</item>
/// <item><b>Explanation only.</b> The candidate's components explain the
/// choice; the site's water still comes from its area and zone (ruling OD3).</item>
/// </list>
/// </summary>
public partial class SimulationEngine
{
    // ─── The area's candidates ─────────────────────────────────────────────

    /// <summary>
    /// The production area's open-pen candidates in rank order (12; empty for a
    /// number outside 1-13), each with its weights and costs, who holds it, the
    /// zone a build there uses and the combined verdict. Pure.
    /// </summary>
    public IReadOnlyList<SeaCandidateView> GetSeaCandidates(int productionArea)
    {
        var candidates = GeoCandidates.Default.OpenPensOf(productionArea);
        if (candidates.Count == 0) return Array.Empty<SeaCandidateView>();
        string buildZone = SeaBuildZoneOf(productionArea);
        var views = new List<SeaCandidateView>(candidates.Count);
        bool suggested = false;
        foreach (var candidate in candidates)
        {
            var check = CandidateCheck(buildZone, candidate.Id);
            bool suggest = !suggested && check.Allowed;
            suggested |= suggest;
            views.Add(ViewOf(candidate, buildZone, check, suggest));
        }
        return views;
    }

    /// <summary>
    /// The zone a build in the area uses: the company's zone there with the most
    /// fjord sites (ties by ordinal id; the expansion map's zone order), else the
    /// area's catalogue zone ("" for a number outside 1-13).
    /// </summary>
    internal string SeaBuildZoneOf(int productionArea)
    {
        if (State?.Zones == null || State.SeaSites == null) return ExpansionZones.OfArea(productionArea)?.ZoneId ?? "";
        var own = State.Zones
            .Where(z => ProductionAreaOf(z) == productionArea)
            .OrderByDescending(z => FjordSites().Count(s => s.ZoneId == z.Id))
            .ThenBy(z => z.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return own?.Id ?? ExpansionZones.OfArea(productionArea)?.ZoneId ?? "";
    }

    private SeaCandidateView ViewOf(GeoCandidate candidate, string buildZone, ExpansionVerdict check, bool suggested)
    {
        var rules = GeoSuitabilityRules.Default;
        var costs = rules.ComponentOrder
            .Append(GeoSuitabilityRules.RiverTerm)
            .Select(term => new KeyValuePair<string, double>(term, rules.ComponentCost(candidate, term)))
            .ToArray();
        var holder = SiteAtCandidate(candidate.Id);
        var verdict = check.Allowed ? BuildVerdictOf(buildZone) : check;
        return new SeaCandidateView(candidate, rules.EffectiveWeights, costs, holder?.Id ?? "", holder?.Label ?? "",
            suggested, buildZone, verdict);
    }

    /// <summary>The company's sea site built at the candidate, or null.</summary>
    private SeaSite? SiteAtCandidate(string candidateId)
        => State?.SeaSites?.FirstOrDefault(s => s.CandidateId == candidateId);

    /// <summary>
    /// The candidate checks of a build in a zone, in order: the engine is
    /// ready, the id names an open-pen candidate, the candidate lies in the
    /// zone's production area, no company site stands there, its suitability
    /// cell is legal (<see cref="SeaCandidateReasons"/>).
    /// </summary>
    internal ExpansionVerdict CandidateCheck(string zoneId, string candidateId)
        => CandidateCheck(zoneId, candidateId, IsSuitable);

    /// <summary>
    /// <see cref="CandidateCheck(string, string)"/> with the suitability test
    /// given (tests reach the last check, which the shipped data never fails).
    /// </summary>
    internal ExpansionVerdict CandidateCheck(string zoneId, string candidateId, Func<GeoCandidate, bool> isSuitable)
    {
        if (State?.SeaSites == null || State.Zones == null || FinanceManager == null)
            return ExpansionVerdict.No(SeaCandidateReasons.EngineNotReady);   // as BuildSeaSite
        var candidate = GeoCandidates.Default.Find(candidateId);
        if (candidate == null || !candidate.IsOpenPen) return ExpansionVerdict.No(SeaCandidateReasons.CandidateNotFound);
        if (candidate.Po != ProductionAreaOfZone(zoneId)) return ExpansionVerdict.No(SeaCandidateReasons.CandidateWrongArea);
        if (SiteAtCandidate(candidate.Id) != null) return ExpansionVerdict.No(SeaCandidateReasons.CandidateTaken);
        if (!isSuitable(candidate)) return ExpansionVerdict.No(SeaCandidateReasons.CandidateNotSuitable);
        return ExpansionVerdict.Yes;
    }

    /// <summary>The candidate's 500 m suitability cell is legal for an open pen (R &gt; 0).</summary>
    private static bool IsSuitable(GeoCandidate candidate)
        => GeoSuitability.Default.CellAt(candidate.Po, candidate.X, candidate.Y).Legal;

    // ─── Preview and build ─────────────────────────────────────────────────

    /// <summary>
    /// What building a locality at a candidate costs, holds and adds (pure):
    /// the zone's <see cref="PlanSiteBuild"/> with every figure unchanged, the
    /// candidate's view and its own checks.
    /// </summary>
    public SeaCandidateBuildPlan PlanSiteBuildAt(string zoneId, string candidateId)
    {
        var plan = PlanSiteBuild(zoneId);
        var candidate = GeoCandidates.Default.Find(candidateId);
        var check = CandidateCheck(zoneId, candidateId);
        SeaCandidateView? view = null;
        if (candidate != null && candidate.IsOpenPen && State?.SeaSites != null)
        {
            string buildZone = SeaBuildZoneOf(candidate.Po);
            view = ViewOf(candidate, buildZone, CandidateCheck(buildZone, candidate.Id), false);
        }
        return new SeaCandidateBuildPlan(plan, view, check);
    }

    /// <summary>
    /// Build a fjord locality at an open-pen candidate: the candidate checks
    /// (refused with their <see cref="SeaCandidateReasons"/> key), then the
    /// unchanged U2-01 build in the zone (<see cref="BuildSeaSite"/>: its gate,
    /// cost, licence and catalogue zone, refused with its own keys), then the
    /// new site records the candidate. Its name stays the generated place name
    /// (<see cref="SiteNames.NextFor"/>); the candidate's label is where it lies.
    /// </summary>
    public CommandResult BuildSeaSiteAt(string zoneId, string candidateId, string name = "")
    {
        var check = CandidateCheck(zoneId, candidateId);
        if (!check.Allowed)
            return CommandResult.Fail(check.ReasonKey, $"Cannot build at candidate '{candidateId}' in zone '{zoneId}': {check.ReasonKey}");
        var result = BuildSeaSite(zoneId, name);
        if (!result.Ok) return result;
        var site = State.SeaSites.First(s => s.Id == result.Id);
        site.CandidateId = candidateId;
        return result;
    }

    // ─── Where the company's sites lie ─────────────────────────────────────

    /// <summary>
    /// One row per fjord locality, in list order: a site built at a candidate
    /// lies at the candidate ("candidate"); any other site at its zone's anchor
    /// locality (<see cref="ScenarioGeoAnchors"/>, "anchor", approximate).
    /// Display only. Pure.
    /// </summary>
    public IReadOnlyList<PlayerSiteLocation> GetPlayerSiteLocations()
    {
        if (State?.SeaSites == null) return Array.Empty<PlayerSiteLocation>();
        var rows = new List<PlayerSiteLocation>();
        foreach (var site in FjordSites())
        {
            var candidate = string.IsNullOrEmpty(site.CandidateId) ? null : GeoCandidates.Default.Find(site.CandidateId);
            if (candidate != null)
            {
                rows.Add(new PlayerSiteLocation(site.Id, site.Label, candidate.Label, candidate.Po, candidate.Id,
                    candidate.X, candidate.Y, PlayerSiteLocation.CandidateSource));
                continue;
            }
            int area = ProductionAreaOfSite(site);
            var anchor = ScenarioGeoAnchors.AnchorOf(site.ZoneId, area);
            rows.Add(new PlayerSiteLocation(site.Id, site.Label, anchor.Name, area, "", anchor.X, anchor.Y,
                PlayerSiteLocation.AnchorSource));
        }
        return rows;
    }
}
