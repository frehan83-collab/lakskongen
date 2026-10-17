using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The expansion flow (roadmap U2-01) on top of S2-01a's capacity and the
/// Phase 4.1 licence auctions: the map's verdicts, the build and auction
/// previews, the catalogue zones and the auction's news on the event bus.
/// <list type="bullet">
/// <item><b>Verdicts.</b> <see cref="BuildVerdictOf"/> and
/// <see cref="ApplyVerdictOf"/> are the build gate and the licence
/// application's checks, in the order the verbs run them, as reason keys
/// (<see cref="ExpansionReasons"/>); the verbs refuse with the same keys.</item>
/// <item><b>Catalogue zones.</b> Every production area has one growth zone
/// (<see cref="ExpansionZones"/>). It is an address the auction and the build
/// gate understand; it joins <see cref="GameState.Zones"/> only when a site is
/// built there, so a game that never expands runs as before.</item>
/// <item><b>Traffic light.</b> A catalogue zone outside the game takes its
/// area's colour as the game knows it (the worst colour of the area's zones;
/// water nobody farms assesses green), so a red area auctions no growth.</item>
/// <item><b>News.</b> An auction the company is in reports to the event bus:
/// outbid (a warning with the closing day as its deadline, so the clock slows
/// and stops before the auction closes), won and lost. Presentation only, like
/// the bus: no RNG, no game state.</item>
/// </list>
/// Every read here is pure; the save hash does not move when the map is open.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Category of the auction events on the bus (and the prefix of their pacing keys).</summary>
    private const string AuctionEventCategory = "licence_auction";

    // ─── Zones ─────────────────────────────────────────────────────────────

    /// <summary>True for a zone of the game or a catalogue zone.</summary>
    public bool IsKnownZone(string? zoneId) => FindZone(zoneId) != null || ExpansionZones.Contains(zoneId);

    /// <summary>The name the player reads for a zone: its catalogue place, else its first site's name.</summary>
    public string ZoneLabelOf(string zoneId)
        => ExpansionZones.TryGet(zoneId, out var definition) ? definition.Name : ZoneLabel(zoneId);

    /// <summary>
    /// A zone's traffic-light colour: the game's for a zone in the game; for a
    /// catalogue zone outside it, its area's (<see cref="AreaTrafficLightColor"/>).
    /// </summary>
    public ZoneColor ZoneTrafficLightColor(string? zoneId)
    {
        if (TrafficLightSystem == null || string.IsNullOrEmpty(zoneId)) return ZoneColor.Yellow;
        if (FindZone(zoneId) == null && ExpansionZones.TryGet(zoneId, out var definition))
            return AreaTrafficLightColor(definition.ProductionArea);
        return TrafficLightSystem.GetZoneColor(zoneId);
    }

    /// <summary>
    /// A production area's colour as the game knows it: the worst colour of its
    /// zones in the game; water nobody farms holds no lice and assesses green
    /// (<see cref="TrafficLightSystem.GreenLiceThreshold"/>).
    /// </summary>
    public ZoneColor AreaTrafficLightColor(int productionArea)
    {
        if (TrafficLightSystem == null || State?.Zones == null) return ZoneColor.Yellow;
        ZoneColor? worst = null;
        foreach (var zone in State.Zones)
        {
            if (ProductionAreaOf(zone) != productionArea) continue;
            var color = TrafficLightSystem.GetZoneColor(zone.Id);
            if (worst == null || Severity(color) > Severity(worst.Value)) worst = color;
        }
        return worst ?? EmptyWaterColor;
    }

    /// <summary>The colour an empty zone assesses to (no fish, no lice).</summary>
    private static ZoneColor EmptyWaterColor =>
        0.0 <= TrafficLightSystem.GreenLiceThreshold ? ZoneColor.Green : ZoneColor.Yellow;

    private static int Severity(ZoneColor color) => color switch
    {
        ZoneColor.Red => 2,
        ZoneColor.Yellow => 1,
        _ => 0
    };

    /// <summary>"green" | "yellow" | "red".</summary>
    public static string TrafficLightId(ZoneColor color) => color switch
    {
        ZoneColor.Red => "red",
        ZoneColor.Yellow => "yellow",
        _ => "green"
    };

    /// <summary>
    /// A catalogue zone joins the game (its first site is being built): the
    /// zone carries its area and the colour the map showed for it.
    /// </summary>
    private void AdmitCatalogueZone(string zoneId)
    {
        if (FindZone(zoneId) != null || !ExpansionZones.TryGet(zoneId, out var definition)) return;
        var color = AreaTrafficLightColor(definition.ProductionArea);
        State.Zones.Add(new Zone(zoneId)
        {
            Name = definition.Name,
            ProductionArea = definition.ProductionArea
        });
        TrafficLightSystem?.SetZoneColor(zoneId, color);
    }

    // ─── Verdicts ──────────────────────────────────────────────────────────

    /// <summary>
    /// The build gate as a reason key, without the cost: the first fjord site
    /// and a site in a zone the company farms are a development right; a new
    /// zone needs an unused growth licence won for it. An offshore unit is no
    /// fjord locality (F2-03): it neither uses up the development right nor
    /// opens a fjord zone (its zone is its plot).
    /// </summary>
    internal string BuildGateKey(string zoneId)
    {
        if (State?.Zones == null || State.SeaSites == null) return ExpansionReasons.EngineNotReady;
        if (!IsKnownZone(zoneId)) return ExpansionReasons.ZoneNotFound;
        if (!FjordSites().Any()) return ExpansionReasons.None;                    // development right (F2-03: fjord localities)
        if (State.SeaSites.Any(s => s.ZoneId == zoneId)) return ExpansionReasons.None; // organic growth
        if (HasGrowthLicence(zoneId)) return ExpansionReasons.None;
        return ExpansionReasons.GrowthLicenceRequired;
    }

    /// <summary>May a site be built in the zone now: the build gate, then the cost.</summary>
    public ExpansionVerdict BuildVerdictOf(string zoneId)
    {
        string gate = BuildGateKey(zoneId);
        if (gate.Length > 0) return ExpansionVerdict.No(gate);
        if (FinanceManager == null || FinanceManager.Balance < SiteBuildCostNOK)
            return ExpansionVerdict.No(ExpansionReasons.InsufficientFunds);
        return ExpansionVerdict.Yes;
    }

    /// <summary>
    /// May the company apply for a growth licence in the zone now (the checks
    /// <see cref="ApplyForGrowthLicence"/> runs, in its order).
    /// </summary>
    public ExpansionVerdict ApplyVerdictOf(string zoneId)
    {
        if (State?.Zones == null || LicenseAuctionSystem == null || TrafficLightSystem == null)
            return ExpansionVerdict.No(ExpansionReasons.EngineNotReady);
        if (!IsKnownZone(zoneId)) return ExpansionVerdict.No(ExpansionReasons.ZoneNotFound);
        if (ZoneTrafficLightColor(zoneId) == ZoneColor.Red) return ExpansionVerdict.No(ExpansionReasons.ZoneRed);
        if (State.SeaSites.Any(s => s.ZoneId == zoneId)) return ExpansionVerdict.No(ExpansionReasons.AlreadyOperating);
        if (HasGrowthLicence(zoneId)) return ExpansionVerdict.No(ExpansionReasons.LicenceHeld);
        if (State.LicenseAuctions.Any(a => a.ZoneId == zoneId && a.Status == "open"))
            return ExpansionVerdict.No(ExpansionReasons.AuctionOpen);
        return ExpansionVerdict.Yes;
    }

    // ─── The map ───────────────────────────────────────────────────────────

    /// <summary>Every production area with the company's capacity there, its zones and their verdicts (pure).</summary>
    public ExpansionOverview GetExpansionOverview()
    {
        var areas = new List<ExpansionAreaView>();
        for (int area = 1; area <= ProductionAreas.Count; area++)
            areas.Add(AreaView(area));
        return new ExpansionOverview
        {
            Areas = areas,
            BuildCostNok = SiteBuildCostNOK,
            LicenceReserveNok = LicenseReservePriceNOK,
            BalanceNok = FinanceManager?.Balance ?? 0m,
            AuctionLengthDays = Finance.LicenseAuctionSystem.AuctionLengthDays
        };
    }

    private ExpansionAreaView AreaView(int area)
    {
        var info = ProductionAreas.Get(area);
        var zones = new List<ExpansionZoneView>();
        if (State?.Zones != null)
        {
            foreach (var zone in State.Zones
                         .Where(z => ProductionAreaOf(z) == area)
                         .OrderByDescending(z => State.SeaSites.Count(s => s.ZoneId == z.Id))
                         .ThenBy(z => z.Id, StringComparer.Ordinal))
                zones.Add(ZoneView(zone.Id, area));
        }
        var catalogue = ExpansionZones.OfArea(area);
        if (catalogue != null && zones.All(z => z.ZoneId != catalogue.ZoneId))
            zones.Add(ZoneView(catalogue.ZoneId, area));

        double mtb = AreaLicenceMtbTonnes(area);
        double standing = State == null ? 0.0 : AreaStandingBiomassTonnes(area);
        var months = info.SeaTemperatureMonthlyC;
        return new ExpansionAreaView(area, info.Code, info.Name, info.LicenceMtbTonnes,
            State == null ? 0 : LicenceRegister.CountIn(State.LicenceHoldings, area), mtb, standing, mtb - standing,
            TrafficLightId(AreaTrafficLightColor(area)), months.Min(), months.Max(), info.InPdZone, zones);
    }

    private ExpansionZoneView ZoneView(string zoneId, int area)
    {
        string auctionId = State?.LicenseAuctions?
            .FirstOrDefault(a => a.ZoneId == zoneId && a.Status == "open")?.Id ?? "";
        return new ExpansionZoneView(zoneId, ZoneLabelOf(zoneId), area, ExpansionZones.Contains(zoneId),
            FindZone(zoneId) != null, State?.SeaSites?.Count(s => s.ZoneId == zoneId) ?? 0,
            TrafficLightId(ZoneTrafficLightColor(zoneId)), BuildVerdictOf(zoneId), ApplyVerdictOf(zoneId),
            HasGrowthLicence(zoneId), auctionId);
    }

    // ─── Previews ──────────────────────────────────────────────────────────

    /// <summary>
    /// What a site build in the zone costs, holds and adds (pure): the verdict,
    /// the cost against the cash, the locality's MTB, the 0 t of licence MTB, the
    /// pens of the standard mooring frame and the crew and upkeep it adds per month.
    /// </summary>
    public SiteBuildPlan PlanSiteBuild(string zoneId)
    {
        int area = ProductionAreaOfZone(zoneId);
        var layout = BuiltSitePenLayout;
        decimal workers = Math.Max(1m, Math.Round((decimal)_balancing.WorkersPerSite));
        // ProcessDailyFinance: the crew's wages plus the upkeep share of a
        // standard-size site (a built site is a standard 8-pen site).
        decimal dailyCost = workers * SimCalendar.MonthlyToDaily(_balancing.WorkerMonthlySalaryNOK)
                            + _balancing.SeaSiteBuildCostNOK * SiteUpkeepShareOfBuildCostPerDay;
        // A new zone needs a licence: not the first fjord site (F2-03's
        // development right, as BuildGateKey) and no site in the zone yet.
        bool newZone = State?.SeaSites != null && FjordSites().Any()
                       && State.SeaSites.All(s => s.ZoneId != zoneId);
        return new SiteBuildPlan(BuildVerdictOf(zoneId), zoneId, ZoneLabelOf(zoneId), area, newZone,
            SiteBuildCostNOK, FinanceManager?.Balance ?? 0m, BuiltSiteLocalityMtbTonnes, 0.0,
            AreaLicenceMtbTonnes(area), layout.Count, layout.Geometry.CircumferenceM,
            layout.Geometry.NetWallDepthM + layout.Geometry.ConeDepthM, layout.Geometry.VolumeM3,
            _calibration.Pens.MaxFishPerPen, workers,
            Math.Round(dailyCost * (decimal)SimCalendar.AverageDaysPerMonth),
            State?.SeaSites?.Count(s => s.ZoneId == zoneId) ?? 0);
    }

    /// <summary>
    /// The daily upkeep of a standard site as a share of its build cost
    /// (<c>ProcessDailyFinance</c>: 0.01 % of the build cost a day per standard site).
    /// </summary>
    internal const decimal SiteUpkeepShareOfBuildCostPerDay = 0.0001m;

    /// <summary>What applying for a growth licence in the zone opens (pure).</summary>
    public LicenceApplicationPreview PreviewLicenceApplication(string zoneId)
    {
        double mtb = StandardLicenceMtbTonnes(zoneId);
        decimal reserve = LicenseReservePriceNOK;
        return new LicenceApplicationPreview(ApplyVerdictOf(zoneId), zoneId, ZoneLabelOf(zoneId),
            ProductionAreaOfZone(zoneId), mtb, reserve, PerTonne(reserve, mtb),
            Finance.LicenseAuctionSystem.AuctionLengthDays, FinanceManager?.Balance ?? 0m,
            TrafficLightId(ZoneTrafficLightColor(zoneId)));
    }

    /// <summary>
    /// The zone's auction as the auction card shows it: the running one, else
    /// the latest; null when the zone never had one.
    /// </summary>
    public LicenceAuctionView? GetLicenceAuctionView(string zoneId)
    {
        var auction = State?.LicenseAuctions?
            .Where(a => a.ZoneId == zoneId)
            .OrderByDescending(a => a.Status == "open")
            .ThenByDescending(a => a.OpensDay)
            .ThenByDescending(a => a.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return auction == null ? null : AuctionView(auction);
    }

    private LicenceAuctionView AuctionView(LicenseAuctionState auction)
    {
        double mtb = StandardLicenceMtbTonnes(auction.ZoneId);
        decimal min = Finance.LicenseAuctionSystem.MinBid(auction);
        var bids = auction.Bids
            .Select((b, i) => (b, i))
            .OrderByDescending(x => x.b.Day)
            .ThenByDescending(x => x.i)
            .Select(x => new LicenceAuctionBidView(x.b.Day, x.b.Kind, x.b.BidderId,
                x.b.Kind == "rival" ? RivalName(x.b.BidderId) : "", x.b.AmountNOK, PerTonne(x.b.AmountNOK, mtb)))
            .ToList();
        decimal playerBest = auction.Bids.Where(b => b.Kind == "player").Select(b => b.AmountNOK)
            .DefaultIfEmpty(0m).Max();
        int today = State?.Day ?? 0;
        return new LicenceAuctionView(auction.Id, auction.ZoneId, ZoneLabelOf(auction.ZoneId),
            ProductionAreaOfZone(auction.ZoneId), mtb, auction.OpensDay, auction.ClosesDay,
            auction.Status == "open" ? Math.Max(0, auction.ClosesDay - today) : 0, auction.Status,
            auction.ReservePriceNOK, auction.CurrentBidNOK, min, PerTonne(auction.CurrentBidNOK, mtb),
            PerTonne(min, mtb), string.IsNullOrEmpty(auction.LeaderKind) ? "none" : auction.LeaderKind,
            auction.LeaderId, auction.LeaderKind == "rival" ? RivalName(auction.LeaderId) : "",
            auction.PlayerEntered, auction.LeaderKind == "player", playerBest,
            auction.Bids.Count(b => b.Kind == "rival"), auction.ConsumedByBuild, bids,
            FinanceManager?.Balance ?? 0m);
    }

    private string RivalName(string rivalId)
        => CompetitorAI?.Competitors.FirstOrDefault(c => c.Id == rivalId)?.Name ?? rivalId;

    private static readonly string[] NbMonthNames =
    {
        "januar", "februar", "mars", "april", "mai", "juni",
        "juli", "august", "september", "oktober", "november", "desember"
    };

    /// <summary>"8. januar": a sim day as the player's calendar shows it (GameCalendar.short_date).</summary>
    internal static string NbDate(int day)
        => $"{SimCalendar.DayOfMonth(day)}. {NbMonthNames[SimCalendar.MonthIndex(day)]}";

    private static decimal PerTonne(decimal amountNok, double tonnes)
        => tonnes > 0.0 ? Math.Round(amountNok / (decimal)tonnes) : 0m;

    // ─── The auction's news (event bus + pacing) ───────────────────────────

    /// <summary>Who leads each auction the company is in, before a day's rival round.</summary>
    internal Dictionary<string, string> AuctionLeadersBeforeRivalRound()
    {
        var leaders = new Dictionary<string, string>(StringComparer.Ordinal);
        if (State?.LicenseAuctions == null) return leaders;
        foreach (var auction in State.LicenseAuctions)
            if (auction.Status == "open" && auction.PlayerEntered)
                leaders[auction.Id] = auction.LeaderKind;
        return leaders;
    }

    /// <summary>
    /// After a rival round: every auction where a rival took the lead from the
    /// company reports "overbudt" with the closing day as its deadline, so the
    /// clock slows to 1× and stops before the auction closes unless the company
    /// bids again (<see cref="ResolveAuctionDeadlines"/>).
    /// </summary>
    internal void ReportAuctionLeadChanges(Dictionary<string, string> leadersBefore)
    {
        foreach (var auction in State.LicenseAuctions)
        {
            if (auction.Status != "open" || !leadersBefore.TryGetValue(auction.Id, out var before)) continue;
            if (before != "player" || auction.LeaderKind != "rival") continue;
            ResolveAuctionDeadlines(auction);
            PublishGameEvent(new GameEventRequest(GameEventType.LicenceOutbid, State.Day)
            {
                ZoneId = auction.ZoneId,
                Variant = $"{auction.Id}:{auction.Bids.Count}",
                DeadlineDay = auction.ClosesDay,
                Value = (double)auction.CurrentBidNOK,
                Message = SimText.Nb($"{RivalName(auction.LeaderId)} bød {auction.CurrentBidNOK / 1_000_000m:N0} MNOK for vekstlisensen i {ZoneLabelOf(auction.ZoneId)}. Auksjonen stenger {NbDate(auction.ClosesDay)}.")
            });
        }
    }

    /// <summary>
    /// The company opened an auction: the clock slows to 1× while it runs and
    /// stops the day before it closes unless the company leads by then.
    /// </summary>
    private void WatchOpenedAuction(LicenseAuctionState auction)
    {
        Pacing.Register(new PacingDeadline
        {
            Key = AuctionDeadlinePrefix(auction) + "opened",
            DeadlineDay = auction.ClosesDay,
            Headline = SimText.Nb($"Lisensauksjonen i {ZoneLabelOf(auction.ZoneId)} stenger"),
            CounterVerb = GameEventCatalog.VerbBidLicence
        });
    }

    /// <summary>The company leads, or the auction ended: stop watching its deadlines.</summary>
    private void ResolveAuctionDeadlines(LicenseAuctionState auction)
    {
        string prefix = AuctionDeadlinePrefix(auction);
        foreach (var deadline in Pacing.Active.Where(d => d.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            Pacing.Resolve(deadline.Key);
    }

    /// <summary>The bus key prefix of an auction's events (category | zone | auction id).</summary>
    private static string AuctionDeadlinePrefix(LicenseAuctionState auction)
        => GameEventBus.KeyOf(AuctionEventCategory, auction.ZoneId, auction.Id + ":");

    /// <summary>A settled auction the company was in: won or lost, on the bus (the log).</summary>
    private void ReportAuctionSettlement(LicenseAuctionState auction, AuctionSettlement settlement)
    {
        ResolveAuctionDeadlines(auction);
        if (!auction.PlayerEntered) return;
        if (settlement.Status == "won_player")
        {
            PublishGameEvent(new GameEventRequest(GameEventType.LicenceAuctionWon, State.Day)
            {
                ZoneId = auction.ZoneId,
                Variant = auction.Id,
                Value = (double)settlement.AmountNOK,
                Message = SimText.Nb($"Du vant vekstlisensen i {ZoneLabelOf(auction.ZoneId)}: {StandardLicenceMtbTonnes(auction.ZoneId):N0} t MTB for {settlement.AmountNOK / 1_000_000m:N0} MNOK.")
            });
        }
        else if (settlement.Status == "won_rival")
        {
            PublishGameEvent(new GameEventRequest(GameEventType.LicenceAuctionLost, State.Day)
            {
                ZoneId = auction.ZoneId,
                Variant = auction.Id,
                Value = (double)settlement.AmountNOK,
                Message = SimText.Nb($"{RivalName(settlement.WinnerId)} vant vekstlisensen i {ZoneLabelOf(auction.ZoneId)} for {settlement.AmountNOK / 1_000_000m:N0} MNOK.")
            });
        }
    }
}
