using System.Collections.Generic;

namespace LakskongenSim.Core;

/// <summary>
/// The real open-sea area a plot is bound to (STAGE23_PLAN track A, A2;
/// <see cref="SimulationEngine.GeoBindingOf"/>): the Havbruk til havs
/// candidate behind hav_1's display position and depth. Display only: the
/// plot's id, Hs calibration and lice band are unchanged.
/// </summary>
/// <param name="PlotId">The bound plot ("hav_1").</param>
/// <param name="CandidateId">The candidate ("HTH13-02").</param>
/// <param name="Area">The Havbruk til havs area's name.</param>
/// <param name="Stage">The area's latest planning stage (candidates.json stage).</param>
/// <param name="StageFactor">The stage's score factor (0.85-1).</param>
/// <param name="Po">The production area the candidate is nearest.</param>
/// <param name="DepthM">Water depth at the candidate, m.</param>
/// <param name="Score">The candidate's suitability score, 0-1.</param>
/// <param name="Binding">The term that costs the score the most ("depth", "stage" or "none").</param>
/// <param name="DistanceKm">Straight-line distance from the home production area's label point, km.</param>
/// <param name="X">EPSG:25833 easting, m.</param>
/// <param name="Y">EPSG:25833 northing, m.</param>
/// <param name="Lon">Longitude, degrees.</param>
/// <param name="Lat">Latitude, degrees.</param>
/// <param name="Near">The nearest place label, cleaned (PlaceLabels).</param>
public sealed record GeoPlotBinding(
    string PlotId,
    string CandidateId,
    string Area,
    string Stage,
    double StageFactor,
    int Po,
    double DepthM,
    double Score,
    string Binding,
    double DistanceKm,
    long X,
    long Y,
    double Lon,
    double Lat,
    string Near);

/// <summary>
/// Where a zone's farm is drawn on the real coast (<see cref="SimulationEngine.GetZoneAnchors"/>):
/// a zone of the game or of the expansion catalogue with its anchor locality.
/// </summary>
/// <param name="ZoneId">The zone id.</param>
/// <param name="ProductionArea">The zone's production area.</param>
/// <param name="LocalityId">The anchor locality's registry number.</param>
/// <param name="Name">The locality's place name.</param>
/// <param name="X">EPSG:25833 easting, m.</param>
/// <param name="Y">EPSG:25833 northing, m.</param>
/// <param name="Source">"authored", "municipality" or "fallback" (ScenarioGeoAnchors).</param>
/// <param name="InGame">True for a zone of the game (State.Zones), false for a catalogue zone the game does not hold.</param>
public sealed record GeoZoneAnchorView(
    string ZoneId,
    int ProductionArea,
    int LocalityId,
    string Name,
    int X,
    int Y,
    string Source,
    bool InGame);

/// <summary>One owner's presence in a production area (<see cref="RivalAreaSummary.TopOwners"/>).</summary>
/// <param name="Id">The owner id (a roster id or ind_&lt;po2&gt;_&lt;nnn&gt;).</param>
/// <param name="Name">The owner's display name.</param>
/// <param name="IsMajor">True for a roster company.</param>
/// <param name="Sites">Salmonid localities it holds in the area.</param>
/// <param name="MtbT">Their summed MTB, t (sites registered in tonnes).</param>
public sealed record RivalOwnerPresence(string Id, string Name, bool IsMajor, int Sites, double MtbT);

/// <summary>
/// A production area's rival picture (<see cref="SimulationEngine.GetRivalSummary"/>):
/// its localities by kind, the producing sea grow-out MTB and the top owners.
/// </summary>
/// <param name="Po">The production area (1-13).</param>
/// <param name="SitesByKind">Every locality in the area by kind ("sea_salmonid", "land_shellfish" …), ordinal by kind.</param>
/// <param name="SeaGrowoutMtbT">Summed MTB of the area's producing sea grow-out localities, t.</param>
/// <param name="TopOwners">The five owners with the most sites there (then MTB, then id).</param>
public sealed record RivalAreaSummary(
    int Po,
    IReadOnlyList<KeyValuePair<string, int>> SitesByKind,
    double SeaGrowoutMtbT,
    IReadOnlyList<RivalOwnerPresence> TopOwners);

/// <summary>
/// A roster company with its sea grow-out sites in the home production area
/// (<see cref="SimulationEngine.CompetitorsByHomePresence"/>): the 3D fjord
/// shows the rivals that farm the player's own waters first.
/// </summary>
/// <param name="Company">The roster company.</param>
/// <param name="HomePoSites">Its producing sea grow-out localities in the home area.</param>
public sealed record RivalHomePresence(Finance.CompetitorCompany Company, int HomePoSites);
