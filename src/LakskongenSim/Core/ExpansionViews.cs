using System.Collections.Generic;

namespace LakskongenSim.Core;

/// <summary>
/// Stable reason keys of the expansion verbs (roadmap U2-01): why a site
/// cannot be built, a licence cannot be applied for or a bid cannot be placed.
/// The UI maps each to the translation key <c>EXPANSION_REASON_*</c> and
/// renders it as text; nothing decides by reading a message.
/// </summary>
public static class ExpansionReasons
{
    /// <summary>The check passed.</summary>
    public const string None = "";

    /// <summary>The game has not started.</summary>
    public const string EngineNotReady = "engine_not_ready";

    /// <summary>No zone (and no catalogue zone) has this id.</summary>
    public const string ZoneNotFound = "zone_not_found";

    /// <summary>A new zone needs a won growth licence before a site can be built there.</summary>
    public const string GrowthLicenceRequired = "growth_licence_required";

    /// <summary>The company cannot pay (a build, or a bid above its cash).</summary>
    public const string InsufficientFunds = "insufficient_funds";

    /// <summary>The zone is red: no growth licences are auctioned there.</summary>
    public const string ZoneRed = "zone_red";

    /// <summary>The company already farms the zone: it builds there directly.</summary>
    public const string AlreadyOperating = "already_operating";

    /// <summary>The company already holds an unused growth licence for the zone.</summary>
    public const string LicenceHeld = "licence_held";

    /// <summary>An auction for the zone is already running.</summary>
    public const string AuctionOpen = "auction_open";

    /// <summary>The bid is under the minimum (the reserve, then 5 % over the leading bid).</summary>
    public const string BidTooLow = "bid_too_low";

    /// <summary>The auction has closed.</summary>
    public const string AuctionClosed = "auction_closed";

    /// <summary>No auction runs or ran for the zone.</summary>
    public const string AuctionNotFound = "auction_not_found";
}

/// <summary>A verb's verdict: allowed, or the reason key it is refused for.</summary>
/// <param name="Allowed">True when the verb would pass.</param>
/// <param name="ReasonKey">An <see cref="ExpansionReasons"/> key; empty when allowed.</param>
public sealed record ExpansionVerdict(bool Allowed, string ReasonKey)
{
    /// <summary>The verb passes.</summary>
    public static ExpansionVerdict Yes { get; } = new(true, ExpansionReasons.None);

    /// <summary>The verb is refused for <paramref name="reasonKey"/>.</summary>
    public static ExpansionVerdict No(string reasonKey) => new(false, reasonKey);
}

/// <summary>One zone on the expansion map.</summary>
/// <param name="ZoneId">Zone id.</param>
/// <param name="Label">The name the player reads (the catalogue place, the site's name, or the zone id made readable).</param>
/// <param name="ProductionArea">The production area it lies in.</param>
/// <param name="InCatalogue">True for one of the map's growth zones (<see cref="Environment.ExpansionZones"/>).</param>
/// <param name="InGame">True when the zone is part of the game (a scenario zone, or a catalogue zone with a site).</param>
/// <param name="PlayerSites">The company's sea sites in the zone.</param>
/// <param name="TrafficLight">"green" | "yellow" | "red".</param>
/// <param name="Build">May a site be built here now (the build gate and the cost).</param>
/// <param name="Apply">May a growth licence be applied for here now.</param>
/// <param name="HoldsLicence">True while the company holds an unused growth licence for the zone.</param>
/// <param name="OpenAuctionId">The id of the auction running for the zone ("" when none).</param>
public sealed record ExpansionZoneView(string ZoneId, string Label, int ProductionArea, bool InCatalogue, bool InGame,
    int PlayerSites, string TrafficLight, ExpansionVerdict Build, ExpansionVerdict Apply, bool HoldsLicence,
    string OpenAuctionId);

/// <summary>One production area on the expansion map: the company's capacity there and its zones.</summary>
/// <param name="ProductionArea">Area number (1-13).</param>
/// <param name="Code">"PO3".</param>
/// <param name="Name">The area's official name.</param>
/// <param name="LicenceSizeTonnes">MTB of one standard licence in the area (780 t; 945 t in Troms and Finnmark).</param>
/// <param name="Licences">The company's licences pooled in the area.</param>
/// <param name="LicenceMtbTonnes">The company's licence MTB in the area (t).</param>
/// <param name="StandingTonnes">Biomass standing in the company's sites there today (t).</param>
/// <param name="HeadroomTonnes">Licence MTB minus standing biomass (t).</param>
/// <param name="TrafficLight">The area's colour as the game knows it: the worst colour among its zones in the game, "green" for water nobody farms.</param>
/// <param name="SeaTemperatureMinC">Coldest monthly mean sea temperature at 3 m (°C, the area's climatology).</param>
/// <param name="SeaTemperatureMaxC">Warmest monthly mean sea temperature at 3 m (°C).</param>
/// <param name="InPdZone">True inside the national PD zone.</param>
/// <param name="Zones">The company's zones in the area, then the catalogue zone.</param>
public sealed record ExpansionAreaView(int ProductionArea, string Code, string Name, int LicenceSizeTonnes,
    int Licences, double LicenceMtbTonnes, double StandingTonnes, double HeadroomTonnes, string TrafficLight,
    double SeaTemperatureMinC, double SeaTemperatureMaxC, bool InPdZone, IReadOnlyList<ExpansionZoneView> Zones);

/// <summary>The expansion map (U2-01): every production area with its zones and verdicts.</summary>
public sealed class ExpansionOverview
{
    /// <summary>The 13 production areas, by number.</summary>
    public IReadOnlyList<ExpansionAreaView> Areas { get; init; } = new List<ExpansionAreaView>();

    /// <summary>What a site build costs (NOK).</summary>
    public decimal BuildCostNok { get; init; }

    /// <summary>The reserve of a growth-licence auction (NOK).</summary>
    public decimal LicenceReserveNok { get; init; }

    /// <summary>The company's cash today (NOK).</summary>
    public decimal BalanceNok { get; init; }

    /// <summary>How many days an auction runs.</summary>
    public int AuctionLengthDays { get; init; }
}

/// <summary>
/// What building a site in a zone costs, holds and adds (the BuildSiteDialog's
/// preview, before the player commits).
/// </summary>
/// <param name="Verdict">The build gate and the cost check.</param>
/// <param name="ZoneId">Zone id.</param>
/// <param name="Label">The zone's name.</param>
/// <param name="ProductionArea">The zone's area.</param>
/// <param name="NewZone">True when the build enters a zone the company does not farm yet (and uses its growth licence).</param>
/// <param name="CostNok">Construction cost.</param>
/// <param name="BalanceNok">Cash today.</param>
/// <param name="LocalityMtbTonnes">The new locality's own cap (t).</param>
/// <param name="LicenceMtbAddedTonnes">Licence MTB the build adds: always 0.</param>
/// <param name="AreaLicenceMtbTonnes">The area's licence MTB before and after the build (t).</param>
/// <param name="Pens">Pens in the locality's mooring frame.</param>
/// <param name="PenCircumferenceM">Collar circumference of each pen (m).</param>
/// <param name="PenNetDepthM">Net depth of each pen, wall plus cone (m).</param>
/// <param name="PenVolumeM3">Volume of each pen (m³).</param>
/// <param name="MaxFishPerPen">Most fish a pen may hold (§ 47a).</param>
/// <param name="Workers">The crew the site adds.</param>
/// <param name="RunningCostPerMonthNok">What the site adds to the operating costs per average month (crew and upkeep).</param>
/// <param name="PlayerSitesInZone">The company's sites in the zone before the build (0 for a first site or a new zone).</param>
public sealed record SiteBuildPlan(ExpansionVerdict Verdict, string ZoneId, string Label, int ProductionArea,
    bool NewZone, decimal CostNok, decimal BalanceNok, double LocalityMtbTonnes, double LicenceMtbAddedTonnes,
    double AreaLicenceMtbTonnes, int Pens, double PenCircumferenceM, double PenNetDepthM, double PenVolumeM3,
    int MaxFishPerPen, decimal Workers, decimal RunningCostPerMonthNok, int PlayerSitesInZone);

/// <summary>What applying for a growth licence in a zone opens (the auction card before the auction).</summary>
/// <param name="Verdict">May the company apply.</param>
/// <param name="ZoneId">Zone id.</param>
/// <param name="Label">The zone's name.</param>
/// <param name="ProductionArea">The zone's area.</param>
/// <param name="LicenceMtbTonnes">MTB the licence holds (t).</param>
/// <param name="ReservePriceNok">The auction's reserve (NOK).</param>
/// <param name="ReservePerTonneNok">The reserve per tonne of MTB (NOK/t).</param>
/// <param name="AuctionLengthDays">Days the auction runs.</param>
/// <param name="BalanceNok">Cash today.</param>
/// <param name="TrafficLight">The zone's colour.</param>
public sealed record LicenceApplicationPreview(ExpansionVerdict Verdict, string ZoneId, string Label,
    int ProductionArea, double LicenceMtbTonnes, decimal ReservePriceNok, decimal ReservePerTonneNok,
    int AuctionLengthDays, decimal BalanceNok, string TrafficLight);

/// <summary>One bid in an auction, as the auction card lists it.</summary>
/// <param name="Day">Sim day of the bid.</param>
/// <param name="BidderKind">"player" | "rival".</param>
/// <param name="BidderId">"player" or the rival's id.</param>
/// <param name="BidderName">The rival's name ("" for the player).</param>
/// <param name="AmountNok">The bid (NOK).</param>
/// <param name="PerTonneNok">The bid per tonne of the licence's MTB (NOK/t).</param>
public sealed record LicenceAuctionBidView(int Day, string BidderKind, string BidderId, string BidderName,
    decimal AmountNok, decimal PerTonneNok);

/// <summary>A growth-licence auction as the auction card shows it.</summary>
/// <param name="Id">Auction id.</param>
/// <param name="ZoneId">The zone the licence is for.</param>
/// <param name="Label">The zone's name.</param>
/// <param name="ProductionArea">The area the licence's MTB pools in.</param>
/// <param name="LicenceMtbTonnes">MTB the licence holds (t).</param>
/// <param name="OpensDay">Sim day the auction opened.</param>
/// <param name="ClosesDay">The last day bids are taken (a bid on the last day keeps it open one more day).</param>
/// <param name="DaysLeft">Days until it closes (0 on the last day, and once settled).</param>
/// <param name="Status">"open" | "won_player" | "won_rival" | "expired" | "forfeited".</param>
/// <param name="ReserveNok">The reserve.</param>
/// <param name="CurrentBidNok">The leading bid (0 before the first).</param>
/// <param name="MinBidNok">The lowest bid that is taken now.</param>
/// <param name="CurrentPerTonneNok">The leading bid per tonne.</param>
/// <param name="MinBidPerTonneNok">The minimum bid per tonne.</param>
/// <param name="LeaderKind">"player" | "rival" | "none".</param>
/// <param name="LeaderId">"player", a rival's id or "".</param>
/// <param name="LeaderName">The leading rival's name ("" for the player or nobody).</param>
/// <param name="PlayerEntered">True once the company opened or bid in it.</param>
/// <param name="PlayerLeads">True while the company's bid leads.</param>
/// <param name="PlayerBestBidNok">The company's highest bid (0 when none).</param>
/// <param name="RivalBids">How many bids rivals have placed.</param>
/// <param name="Consumed">True when a won licence has been used for a site.</param>
/// <param name="Bids">Every bid, newest first.</param>
/// <param name="BalanceNok">Cash today.</param>
public sealed record LicenceAuctionView(string Id, string ZoneId, string Label, int ProductionArea,
    double LicenceMtbTonnes, int OpensDay, int ClosesDay, int DaysLeft, string Status, decimal ReserveNok,
    decimal CurrentBidNok, decimal MinBidNok, decimal CurrentPerTonneNok, decimal MinBidPerTonneNok,
    string LeaderKind, string LeaderId, string LeaderName, bool PlayerEntered, bool PlayerLeads,
    decimal PlayerBestBidNok, int RivalBids, bool Consumed, IReadOnlyList<LicenceAuctionBidView> Bids,
    decimal BalanceNok);
