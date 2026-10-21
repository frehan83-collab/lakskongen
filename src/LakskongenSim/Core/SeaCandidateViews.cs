using System.Collections.Generic;
using LakskongenSim.Environment.Geo;

namespace LakskongenSim.Core;

/// <summary>
/// Stable reason keys of a build at an open-pen candidate (geo stages 2-3,
/// track B): why a candidate cannot carry a new locality. Checked in this
/// order, before the zone's own build gate (<see cref="ExpansionReasons"/>).
/// The UI maps each to <c>SEA_CANDIDATE_REASON_*</c> and renders it as text;
/// nothing decides by reading a message. Separate from ExpansionReasons,
/// whose 11 keys are pinned (tests/test_expansion_contract.py).
/// </summary>
public static class SeaCandidateReasons
{
    /// <summary>The game has not started.</summary>
    public const string EngineNotReady = "engine_not_ready";

    /// <summary>No open-pen candidate has this id (an unknown id, or an offshore candidate).</summary>
    public const string CandidateNotFound = "candidate_not_found";

    /// <summary>The candidate lies in another production area than the zone the build would use.</summary>
    public const string CandidateWrongArea = "candidate_wrong_area";

    /// <summary>One of the company's sites already stands there: a candidate carries one locality.</summary>
    public const string CandidateTaken = "candidate_taken";

    /// <summary>The candidate's suitability cell is not legal for an open pen (an integrity guard: the shipped data has none).</summary>
    public const string CandidateNotSuitable = "candidate_not_suitable";
}

/// <summary>
/// One open-pen candidate as the build dialog's Sjø step lists it: the
/// shipped siting data (<see cref="GeoCandidate"/>), the published weights
/// and what each term costs the score, who holds it, and whether a locality
/// can be built there now. Pure: reading it changes nothing.
/// </summary>
/// <param name="Candidate">The candidate as shipped (score, components, binding, depth, distances, ocean, position).</param>
/// <param name="Weights">The weight of each score component in this build (suitability_rules.json effective_weights).</param>
/// <param name="ComponentCosts">What each weighted component and the river factor cost the score, in the pipeline's order (river last).</param>
/// <param name="TakenBySiteId">The company's site that stands at the candidate ("" when it is free).</param>
/// <param name="TakenBySiteLabel">That site's name as the player reads it ("" when free).</param>
/// <param name="Suggested">True for the first free candidate by rank: the one the dialog preselects.</param>
/// <param name="BuildZoneId">The zone a build here uses: the company's zone in the area with the most fjord sites (ties by id), else the area's catalogue zone.</param>
/// <param name="Verdict">The candidate checks (<see cref="SeaCandidateReasons"/>), then the build verdict of <paramref name="BuildZoneId"/>.</param>
public sealed record SeaCandidateView(
    GeoCandidate Candidate,
    IReadOnlyDictionary<string, double> Weights,
    IReadOnlyList<KeyValuePair<string, double>> ComponentCosts,
    string TakenBySiteId,
    string TakenBySiteLabel,
    bool Suggested,
    string BuildZoneId,
    ExpansionVerdict Verdict)
{
    /// <summary>"PO03-01".</summary>
    public string Id => Candidate.Id;

    /// <summary>The production area (1-13).</summary>
    public int Po => Candidate.Po;

    /// <summary>1..12 inside the area.</summary>
    public int Rank => Candidate.Rank;

    /// <summary>The readable place label ("Hille").</summary>
    public string Label => Candidate.Label;

    /// <summary>True while one of the company's sites stands at the candidate.</summary>
    public bool Taken => TakenBySiteId.Length > 0;
}

/// <summary>
/// What building a locality at a candidate costs, holds and adds: the zone's
/// <see cref="SiteBuildPlan"/> unchanged (the same ledger as a build without a
/// candidate), the candidate and its own checks.
/// </summary>
/// <param name="Plan">The zone's build plan (<c>PlanSiteBuild</c>), every figure unchanged.</param>
/// <param name="Candidate">The candidate's view, or null for an id that names no open-pen candidate.</param>
/// <param name="CandidateVerdict">The candidate checks alone (<see cref="SeaCandidateReasons"/>).</param>
public sealed record SeaCandidateBuildPlan(SiteBuildPlan Plan, SeaCandidateView? Candidate, ExpansionVerdict CandidateVerdict)
{
    /// <summary>The build's verdict: the candidate checks first, then the zone's build gate and cost.</summary>
    public ExpansionVerdict Verdict => CandidateVerdict.Allowed ? Plan.Verdict : CandidateVerdict;
}

/// <summary>
/// Where one of the company's fjord localities lies on the real coast
/// (display only; nothing in the simulation reads it).
/// </summary>
/// <param name="SiteId">The site's id.</param>
/// <param name="SiteLabel">The site's name as the player reads it.</param>
/// <param name="Label">The place: the candidate's label, or the anchor locality's name.</param>
/// <param name="Po">The production area the point lies in.</param>
/// <param name="CandidateId">The candidate the site was built at ("" for an anchored site).</param>
/// <param name="X">EPSG:25833 easting, m.</param>
/// <param name="Y">EPSG:25833 northing, m.</param>
/// <param name="Source">"candidate" (exact: built at a candidate) or "anchor" (approximate: the zone's anchor locality).</param>
public sealed record PlayerSiteLocation(string SiteId, string SiteLabel, string Label, int Po, string CandidateId,
    long X, long Y, string Source)
{
    /// <summary>The source of a site built at a candidate: its position is the candidate's.</summary>
    public const string CandidateSource = "candidate";

    /// <summary>The source of any other site: the zone's anchor locality, an approximate position.</summary>
    public const string AnchorSource = "anchor";

    /// <summary>True when the position is the zone's anchor (approximate), not the site's own.</summary>
    public bool Approximate => Source == AnchorSource;
}
