using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Roadmap U2-01, the expansion flow's engine side: the map's catalogue zones,
/// the build and licence verdicts with a reason key for every refusal, the
/// build plan the dialog previews, the auction card's view, the auction's
/// news on the event bus and the clock, and the activity rule that keeps an
/// auction open for the rivals to answer a last-day bid.
/// </summary>
public class ExpansionFlowTests
{
    private readonly ITestOutputHelper _output;

    public ExpansionFlowTests(ITestOutputHelper output) => _output = output;

    private static SimulationEngine Started(string scenarioId, int seed = 17, decimal? cash = null, bool events = false)
    {
        var probe = new SimulationEngine();
        probe.Initialize(enableEvents: false, seed: seed);
        decimal capital = probe.Scenario.GetScenario(scenarioId)!.StartingCapitalNOK;
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(capital) };
        engine.Initialize(enableEvents: events, seed: seed);
        engine.Scenario.Unlock(scenarioId);
        Assert.True(engine.StartScenario(scenarioId).Ok);
        if (cash.HasValue && cash.Value > engine.FinanceManager.Balance)
            engine.ReceiveIncome(cash.Value - engine.FinanceManager.Balance, "test_capital");
        return engine;
    }

    private static void SilenceRivals(SimulationEngine engine)
    {
        foreach (var rival in engine.CompetitorAI.Competitors)
        {
            rival.IsDistressed = true;
            rival.CashReserves = 0m;
        }
    }

    private static string Snapshot(SimulationEngine engine)
        => SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));

    // ── The catalogue ────────────────────────────────────────────────────

    [Fact]
    public void TheCatalogue_HasOneGrowthZonePerProductionArea_AndNoScenarioZone()
    {
        Assert.Equal(ProductionAreas.Count, ExpansionZones.All.Count);
        Assert.Equal(Enumerable.Range(1, ProductionAreas.Count), ExpansionZones.All.Select(z => z.ProductionArea).OrderBy(a => a));
        Assert.Equal(ExpansionZones.All.Count, ExpansionZones.All.Select(z => z.ZoneId).Distinct().Count());
        Assert.All(ExpansionZones.All, z => Assert.False(string.IsNullOrWhiteSpace(z.Name)));

        var roster = new ScenarioSystem(new GameState());
        var scenarioZones = roster.All.Values.SelectMany(s => s.StartingSites).Select(s => s.Zone).ToHashSet();
        Assert.DoesNotContain(ExpansionZones.All, z => scenarioZones.Contains(z.ZoneId));
        for (int area = 1; area <= ProductionAreas.Count; area++)
            Assert.Equal(area, ExpansionZones.OfArea(area)!.ProductionArea);
    }

    [Fact]
    public void ACatalogueZone_LiesInItsArea_ForTheLicenceAndTheWater()
    {
        var engine = Started("forste_sesong");
        Assert.Equal(12, engine.ProductionAreaOfZone("po12_hasvik"));
        Assert.Equal(945, engine.StandardLicenceMtbTonnes("po12_hasvik"));
        Assert.Equal(780, engine.StandardLicenceMtbTonnes("po04_solund"));
        // An id outside the game and the catalogue still lies in the home area.
        Assert.Equal(3, engine.ProductionAreaOfZone("nowhere"));
        Assert.Equal("Hasvik", engine.ZoneLabelOf("po12_hasvik"));
    }

    // ── The map ──────────────────────────────────────────────────────────

    [Fact]
    public void TheMap_ListsEveryArea_WithTheCompanysZoneFirst_ThenTheCatalogueZone()
    {
        var engine = Started("forste_sesong");
        var map = engine.GetExpansionOverview();

        Assert.Equal(Enumerable.Range(1, 13), map.Areas.Select(a => a.ProductionArea));
        Assert.Equal(engine.SiteBuildCostNOK, map.BuildCostNok);
        Assert.Equal(engine.LicenseReservePriceNOK, map.LicenceReserveNok);
        Assert.Equal(LicenseAuctionSystem.AuctionLengthDays, map.AuctionLengthDays);

        var po3 = map.Areas.Single(a => a.ProductionArea == 3);
        Assert.Equal("PO3", po3.Code);
        Assert.Equal(1, po3.Licences);
        Assert.Equal(780.0, po3.LicenceMtbTonnes);
        Assert.Equal(780, po3.LicenceSizeTonnes);
        Assert.True(po3.SeaTemperatureMinC < po3.SeaTemperatureMaxC);
        Assert.True(po3.InPdZone);
        Assert.Equal(new[] { "fjord_west", "po03_bomlo" }, po3.Zones.Select(z => z.ZoneId));

        var home = po3.Zones[0];
        Assert.True(home.InGame && !home.InCatalogue);
        Assert.Equal(1, home.PlayerSites);
        Assert.Equal("Bestefars Anlegg", home.Label);
        // Organic growth needs no licence, but the family farm's 4 MNOK does
        // not cover a 6 MNOK build yet.
        Assert.Equal(ExpansionReasons.None, engine.BuildGateKey("fjord_west"));
        Assert.Equal(ExpansionReasons.InsufficientFunds, home.Build.ReasonKey);
        Assert.Equal(ExpansionReasons.AlreadyOperating, home.Apply.ReasonKey);

        var bomlo = po3.Zones[1];
        Assert.True(bomlo.InCatalogue && !bomlo.InGame);
        Assert.Equal("Bømlo", bomlo.Label);
        Assert.Equal(ExpansionReasons.GrowthLicenceRequired, bomlo.Build.ReasonKey);
        Assert.True(bomlo.Apply.Allowed, bomlo.Apply.ReasonKey);

        var po12 = map.Areas.Single(a => a.ProductionArea == 12);
        Assert.Equal(945, po12.LicenceSizeTonnes);
        Assert.Equal(0, po12.Licences);
        Assert.Equal("green", po12.TrafficLight);                        // water nobody farms
        Assert.Equal(new[] { "po12_hasvik" }, po12.Zones.Select(z => z.ZoneId));
    }

    [Fact]
    public void ReadingTheMapAndEveryPreview_ChangesNothing()
    {
        var engine = Started("forste_sesong", cash: 300_000_000m);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        engine.Advance(2);
        string before = Snapshot(engine);
        int events = engine.GameEvents.History.Count;

        engine.GetExpansionOverview();
        foreach (var zone in ExpansionZones.All.Select(z => z.ZoneId).Append("fjord_west").Append("nowhere"))
        {
            engine.PlanSiteBuild(zone);
            engine.PreviewLicenceApplication(zone);
            engine.GetLicenceAuctionView(zone);
            engine.PreviewBuildSeaSite(zone);
        }

        Assert.Equal(before, Snapshot(engine));
        Assert.Equal(events, engine.GameEvents.History.Count);
        Assert.DoesNotContain(engine.State.Zones, z => ExpansionZones.Contains(z.Id));
    }

    // ── Every refusal has its key ────────────────────────────────────────

    [Fact]
    public void EveryBuildRefusal_HasItsReasonKey_AndTheVerbRefusesWithIt()
    {
        var engine = Started("forste_sesong");
        Assert.Equal(ExpansionReasons.ZoneNotFound, engine.BuildVerdictOf("nowhere").ReasonKey);
        Assert.Equal(ExpansionReasons.ZoneNotFound, engine.BuildSeaSite("nowhere", "x").ErrorKey);
        Assert.Equal(ExpansionReasons.GrowthLicenceRequired, engine.BuildVerdictOf("po03_bomlo").ReasonKey);
        Assert.Equal(ExpansionReasons.GrowthLicenceRequired, engine.BuildSeaSite("po03_bomlo", "x").ErrorKey);
        Assert.Equal(ExpansionReasons.GrowthLicenceRequired, engine.PreviewBuildSeaSite("po03_bomlo").ReasonKey);

        engine.FinanceManager.Spend(engine.FinanceManager.Balance - 1_000_000m);
        Assert.Equal(ExpansionReasons.InsufficientFunds, engine.BuildVerdictOf("fjord_west").ReasonKey);
        Assert.Equal(ExpansionReasons.InsufficientFunds, engine.BuildSeaSite("fjord_west", "x").ErrorKey);
        Assert.Equal(ExpansionReasons.InsufficientFunds, engine.PlanSiteBuild("fjord_west").Verdict.ReasonKey);
    }

    [Fact]
    public void EveryLicenceRefusal_HasItsReasonKey_AndTheVerbRefusesWithIt()
    {
        var engine = Started("po3_red_sone", cash: 400_000_000m);
        Assert.Equal(ExpansionReasons.ZoneNotFound, engine.ApplyVerdictOf("nowhere").ReasonKey);
        Assert.Equal(ExpansionReasons.AlreadyOperating, engine.ApplyVerdictOf("po3_nord").ReasonKey);
        Assert.Equal(ExpansionReasons.AlreadyOperating, engine.ApplyForGrowthLicence("po3_nord").ErrorKey);

        // A red zone in the area makes the area's catalogue zone red too.
        engine.TrafficLightSystem.SetZoneColor("po3_nord", ZoneColor.Red);
        Assert.Equal("red", engine.GetExpansionOverview().Areas.Single(a => a.ProductionArea == 3).TrafficLight);
        Assert.Equal(ExpansionReasons.ZoneRed, engine.ApplyVerdictOf("po03_bomlo").ReasonKey);
        Assert.Equal(ExpansionReasons.ZoneRed, engine.ApplyForGrowthLicence("po03_bomlo").ErrorKey);
        engine.TrafficLightSystem.SetZoneColor("po3_nord", ZoneColor.Green);
        engine.TrafficLightSystem.SetZoneColor("po3_sor", ZoneColor.Green);

        Assert.True(engine.ApplyForGrowthLicence("po03_bomlo").Ok);
        Assert.Equal(ExpansionReasons.AuctionOpen, engine.ApplyVerdictOf("po03_bomlo").ReasonKey);
        Assert.Equal(ExpansionReasons.AuctionOpen, engine.ApplyForGrowthLicence("po03_bomlo").ErrorKey);

        SilenceRivals(engine);
        Assert.True(engine.BidOnLicenseAuction("po03_bomlo", engine.LicenseReservePriceNOK).Ok);
        var auction = engine.State.LicenseAuctions.Single(a => a.ZoneId == "po03_bomlo");
        engine.State.Day = auction.ClosesDay + 1;
        engine.TickLicenseAuctions();
        Assert.Equal(ExpansionReasons.LicenceHeld, engine.ApplyVerdictOf("po03_bomlo").ReasonKey);
        Assert.Equal(ExpansionReasons.LicenceHeld, engine.ApplyForGrowthLicence("po03_bomlo").ErrorKey);

        var idle = new SimulationEngine();
        Assert.Equal(ExpansionReasons.EngineNotReady, idle.ApplyVerdictOf("po03_bomlo").ReasonKey);
    }

    [Fact]
    public void EveryBidRefusal_HasItsReasonKey()
    {
        var engine = Started("forste_sesong", cash: 160_000_000m);
        Assert.Equal(ExpansionReasons.AuctionNotFound, engine.BidOnLicenseAuction("po04_solund", 1m).ErrorKey);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        Assert.Equal(ExpansionReasons.BidTooLow, engine.BidOnLicenseAuction("po04_solund", 1m).ErrorKey);
        Assert.Equal(ExpansionReasons.InsufficientFunds, engine.BidOnLicenseAuction("po04_solund", 900_000_000m).ErrorKey);
        engine.State.LicenseAuctions.Single().Status = "expired";
        Assert.Equal(ExpansionReasons.AuctionClosed, engine.BidOnLicenseAuction("po04_solund", 200_000_000m).ErrorKey);
    }

    // ── A catalogue zone joins the game with its first site ──────────────

    [Fact]
    public void ACatalogueZone_StaysOutOfTheGame_UntilItsFirstSiteIsBuilt()
    {
        var engine = Started("forste_sesong", cash: 300_000_000m);
        int zones = engine.State.Zones.Count;
        SilenceRivals(engine);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        Assert.True(engine.BidOnLicenseAuction("po04_solund", engine.LicenseReservePriceNOK).Ok);
        engine.Advance(LicenseAuctionSystem.AuctionLengthDays + 1);

        Assert.Equal("won_player", engine.State.LicenseAuctions.Single().Status);
        Assert.Equal(zones, engine.State.Zones.Count);                      // a licence is no zone
        Assert.Equal(780.0, engine.AreaLicenceMtbTonnes(4));
        Assert.Equal(780.0, engine.AreaLicenceMtbTonnes(3));
        Assert.True(engine.BuildVerdictOf("po04_solund").Allowed);

        var plan = engine.PlanSiteBuild("po04_solund");
        Assert.True(plan.NewZone);
        Assert.Equal(4, plan.ProductionArea);

        var built = engine.BuildSeaSite("po04_solund", "solund_1");
        Assert.True(built.Ok, built.Message);
        var zone = engine.State.Zones.Single(z => z.Id == "po04_solund");
        Assert.Equal(4, zone.ProductionArea);
        Assert.Equal("Solund", zone.Name);
        Assert.Equal(4, engine.ProductionAreaOfSite(engine.State.SeaSites.Single(s => s.Id == "solund_1")));
        Assert.True(engine.State.LicenseAuctions.Single().ConsumedByBuild);
        Assert.Equal(ZoneColor.Green, engine.TrafficLightSystem.GetZoneColor("po04_solund"));

        // The zone is now the company's: a second site there is organic growth.
        var map = engine.GetExpansionOverview().Areas.Single(a => a.ProductionArea == 4);
        var view = Assert.Single(map.Zones);
        Assert.True(view.InGame && view.InCatalogue);
        Assert.Equal(1, view.PlayerSites);
        Assert.True(view.Build.Allowed);
        Assert.Equal(ExpansionReasons.AlreadyOperating, view.Apply.ReasonKey);
    }

    [Fact]
    public void AnAdmittedZoneAndARunningAuction_SurviveASave()
    {
        var engine = Started("forste_sesong", seed: 23, cash: 600_000_000m);
        SilenceRivals(engine);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        Assert.True(engine.BidOnLicenseAuction("po04_solund", engine.LicenseReservePriceNOK).Ok);
        engine.Advance(LicenseAuctionSystem.AuctionLengthDays + 1);
        Assert.True(engine.BuildSeaSite("po04_solund", "solund_1").Ok);
        foreach (var rival in engine.CompetitorAI.Competitors) { rival.IsDistressed = false; rival.CashReserves = 3_000_000_000m; }
        Assert.True(engine.ApplyForGrowthLicence("po07_vikna").Ok);
        engine.Advance(3);

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        Assert.Equal("Solund", resumed.ZoneLabelOf("po04_solund"));
        Assert.Equal(4, resumed.ProductionAreaOfZone("po04_solund"));
        Assert.Equal(engine.GetLicenceAuctionView("po07_vikna")!.Bids.Count, resumed.GetLicenceAuctionView("po07_vikna")!.Bids.Count);

        engine.Advance(10);
        resumed.Advance(10);
        string a = Snapshot(engine);
        string b = Snapshot(resumed);
        Assert.True(a == b, "resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
    }

    // ── The build plan ───────────────────────────────────────────────────

    [Fact]
    public void TheBuildPlan_IsWhatTheBuildDoes_CostPensMtbAndRunningCost()
    {
        var engine = Started("forste_sesong", cash: 20_000_000m);
        engine.Advance(14);   // mid-month: no period close inside the measured days
        var plan = engine.PlanSiteBuild("fjord_west");
        Assert.True(plan.Verdict.Allowed);
        Assert.False(plan.NewZone);
        Assert.Equal(engine.SiteBuildCostNOK, plan.CostNok);
        Assert.Equal(0.0, plan.LicenceMtbAddedTonnes);
        Assert.Equal(engine.AreaLicenceMtbTonnes(3), plan.AreaLicenceMtbTonnes);

        decimal OperationsToday()
        {
            decimal before = engine.FinancialStatements.CostBreakdown.GetValueOrDefault("operations");
            engine.Advance(1);
            return engine.FinancialStatements.CostBreakdown.GetValueOrDefault("operations") - before;
        }

        decimal dayBefore = OperationsToday();
        Assert.Equal(dayBefore, OperationsToday());   // a quiet day costs the same
        decimal balance = engine.FinanceManager.Balance;
        double areaMtb = engine.AreaLicenceMtbTonnes(3);
        var built = engine.BuildSeaSite("fjord_west", "andre");
        Assert.True(built.Ok, built.Message);
        Assert.Equal(balance - plan.CostNok, engine.FinanceManager.Balance);
        Assert.Equal(areaMtb, engine.AreaLicenceMtbTonnes(3));                  // 0 t licence MTB

        var site = engine.State.SeaSites.Single(s => s.Id == "andre");
        Assert.Equal(plan.LocalityMtbTonnes, site.MABTonnes);
        Assert.Equal(plan.Pens, site.PhysicalPens.Count);
        Assert.All(site.PhysicalPens, p => Assert.Equal(plan.PenVolumeM3, p.VolumeM3, 6));
        Assert.All(site.PhysicalPens, p => Assert.Equal(plan.PenCircumferenceM, p.CircumferenceM, 6));
        Assert.Equal(engine.SiteWorkers(site), plan.Workers);

        decimal added = OperationsToday() - dayBefore;
        decimal perMonth = Math.Round(added * (decimal)SimCalendar.AverageDaysPerMonth);
        Assert.True(Math.Abs(perMonth - plan.RunningCostPerMonthNok) <= 1m,
            $"plan says {plan.RunningCostPerMonthNok:N0} NOK/month, the books {perMonth:N0}");
    }

    // ── The auction card ─────────────────────────────────────────────────

    [Fact]
    public void TheLicencePreview_ShowsTheReserveAndItsPricePerTonne()
    {
        var engine = Started("forste_sesong");
        var preview = engine.PreviewLicenceApplication("po12_hasvik");
        Assert.True(preview.Verdict.Allowed);
        Assert.Equal(945.0, preview.LicenceMtbTonnes);
        Assert.Equal(engine.LicenseReservePriceNOK, preview.ReservePriceNok);
        Assert.Equal(Math.Round(engine.LicenseReservePriceNOK / 945m), preview.ReservePerTonneNok);
        Assert.Equal("green", preview.TrafficLight);
        Assert.Equal(12, preview.ProductionArea);
    }

    [Fact]
    public void TheAuctionView_ListsRivalBidsByName_NewestFirst_WithTheMinimumBid()
    {
        var engine = Started("forste_sesong", seed: 5, cash: 400_000_000m);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        Assert.True(engine.BidOnLicenseAuction("po04_solund", engine.LicenseReservePriceNOK).Ok);
        for (int i = 0; i < 5 && engine.State.LicenseAuctions.Single().LeaderKind != "rival"; i++)
            engine.Advance(1);

        var view = engine.GetLicenceAuctionView("po04_solund")!;
        var auction = engine.State.LicenseAuctions.Single();
        Assert.Equal("rival", view.LeaderKind);
        Assert.False(view.PlayerLeads);
        Assert.Equal(engine.CompetitorAI.Competitors.Single(c => c.Id == auction.LeaderId).Name, view.LeaderName);
        Assert.True(view.RivalBids >= 1);
        Assert.Equal(engine.LicenseReservePriceNOK, view.PlayerBestBidNok);
        Assert.Equal(LicenseAuctionSystem.MinBid(auction), view.MinBidNok);
        Assert.Equal(auction.ClosesDay - engine.State.Day, view.DaysLeft);
        Assert.Equal(auction.Bids.Count, view.Bids.Count);
        Assert.Equal(view.CurrentBidNok, view.Bids[0].AmountNok);                // newest first
        Assert.Equal("player", view.Bids[^1].BidderKind);                        // the opening bid last
        Assert.All(view.Bids.Where(b => b.BidderKind == "rival"), b => Assert.False(string.IsNullOrEmpty(b.BidderName)));
        Assert.Equal(Math.Round(view.CurrentBidNok / 780m), view.CurrentPerTonneNok);
    }

    // ── The auction's news and the clock ─────────────────────────────────

    [Fact]
    public void OpeningAnAuction_SlowsTheClock_UntilTheCompanyLeads()
    {
        var engine = Started("forste_sesong", cash: 400_000_000m);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        var auction = engine.State.LicenseAuctions.Single();
        var watched = Assert.Single(engine.Pacing.Active);
        Assert.Equal(auction.ClosesDay, watched.DeadlineDay);
        Assert.Equal(GameEventCatalog.VerbBidLicence, watched.CounterVerb);
        var decision = engine.Pacing.Evaluate(engine.State.Day, GameSpeed.Fastest, clockRunning: true);
        Assert.Equal(PacingAction.SlowToNormal, decision.Action);

        Assert.True(engine.BidOnLicenseAuction("po04_solund", engine.LicenseReservePriceNOK).Ok);
        Assert.Empty(engine.Pacing.Active);
    }

    [Fact]
    public void BeingOutbid_IsAWarningWithTheClosingDay_ThatStopsTheClock_UntilTheCompanyBidsAgain()
    {
        var engine = Started("forste_sesong", seed: 5, cash: 400_000_000m);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        Assert.True(engine.BidOnLicenseAuction("po04_solund", engine.LicenseReservePriceNOK).Ok);
        var auction = engine.State.LicenseAuctions.Single();
        for (int i = 0; i < 5 && auction.LeaderKind != "rival"; i++)
            engine.Advance(1);
        Assert.Equal("rival", auction.LeaderKind);

        var outbid = engine.GameEvents.History.Last(e => e.Type == GameEventType.LicenceOutbid);
        Assert.Equal(GameEventSeverity.Warning, outbid.Severity);
        Assert.Equal("po04_solund", outbid.ZoneId);
        Assert.Equal(auction.ClosesDay, outbid.DeadlineDay);
        Assert.Equal(GameEventCatalog.VerbBidLicence, outbid.CounterVerb);
        Assert.Equal((double)auction.CurrentBidNOK, outbid.Value);
        Assert.Contains(SimulationEngine.NbDate(auction.ClosesDay), outbid.Message);   // a date, not a day number
        Assert.Equal("8. januar", SimulationEngine.NbDate(7));
        Assert.Contains(engine.Pacing.Active, d => d.Key == outbid.Key);

        // Unanswered, the clock stops on the auction's last day.
        var stop = engine.Pacing.Evaluate(auction.ClosesDay - 1, GameSpeed.Normal, clockRunning: true);
        Assert.Equal(PacingAction.Pause, stop.Action);

        Assert.True(engine.BidOnLicenseAuction("po04_solund", LicenseAuctionSystem.MinBid(auction)).Ok);
        Assert.DoesNotContain(engine.Pacing.Active, d => d.Key == outbid.Key);
    }

    [Fact]
    public void ASettledAuction_IsReported_WonOrLost_AndStopsBeingWatched()
    {
        var won = Started("forste_sesong", cash: 400_000_000m);
        SilenceRivals(won);
        Assert.True(won.ApplyForGrowthLicence("po04_solund").Ok);
        Assert.True(won.BidOnLicenseAuction("po04_solund", won.LicenseReservePriceNOK).Ok);
        won.Advance(LicenseAuctionSystem.AuctionLengthDays + 1);
        var report = Assert.Single(won.GameEvents.History, e => e.Type == GameEventType.LicenceAuctionWon);
        Assert.Equal((double)won.LicenseReservePriceNOK, report.Value);
        Assert.Empty(won.Pacing.Active);

        var lost = Started("forste_sesong", seed: 5, cash: 155_000_000m);
        Assert.True(lost.ApplyForGrowthLicence("po04_solund").Ok);
        Assert.True(lost.BidOnLicenseAuction("po04_solund", lost.LicenseReservePriceNOK).Ok);
        lost.Advance(LicenseAuctionSystem.AuctionLengthDays + 1);
        Assert.Equal("won_rival", lost.State.LicenseAuctions.Single().Status);
        Assert.Single(lost.GameEvents.History, e => e.Type == GameEventType.LicenceAuctionLost);
        Assert.Empty(lost.Pacing.Active);
    }

    [Fact]
    public void ABidOnTheLastDay_KeepsTheAuctionOpen_AndTheRivalsAnswerIt()
    {
        var engine = Started("forste_sesong", seed: 5, cash: 600_000_000m);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        var auction = engine.State.LicenseAuctions.Single();
        int closes = auction.ClosesDay;
        // A rival leads well under every rival's ceiling on the last day.
        var leader = engine.CompetitorAI.Competitors.First();
        auction.Bids.Add(new LicenseBid { Day = engine.State.Day, Kind = "rival", BidderId = leader.Id, AmountNOK = 160_000_000m });
        auction.CurrentBidNOK = 160_000_000m;
        auction.LeaderKind = "rival";
        auction.LeaderId = leader.Id;
        engine.State.Day = closes;

        decimal lastDayBid = LicenseAuctionSystem.MinBid(auction);
        Assert.True(engine.BidOnLicenseAuction("po04_solund", lastDayBid).Ok);
        Assert.Equal(closes + LicenseAuctionSystem.ClosingRoundExtensionDays, auction.ClosesDay);

        engine.State.Day = closes + 1;   // the rivals' answering round
        engine.TickLicenseAuctions();
        Assert.Equal("open", auction.Status);
        Assert.Contains(auction.Bids, b => b.Kind == "rival" && b.Day == closes + 1 && b.AmountNOK > lastDayBid);
        Assert.Equal("rival", auction.LeaderKind);
    }

    [Fact]
    public void WithoutAnotherBid_TheExtendedAuctionSettles_TheDayAfter()
    {
        var engine = Started("forste_sesong", cash: 600_000_000m);
        SilenceRivals(engine);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        var auction = engine.State.LicenseAuctions.Single();
        int closes = auction.ClosesDay;
        engine.State.Day = closes;
        Assert.True(engine.BidOnLicenseAuction("po04_solund", engine.LicenseReservePriceNOK).Ok);
        engine.State.Day = closes + 1;
        engine.TickLicenseAuctions();
        Assert.Equal("open", auction.Status);
        engine.State.Day = closes + 2;
        engine.TickLicenseAuctions();
        Assert.Equal("won_player", auction.Status);
    }

    [Fact]
    public void ABidBeforeTheLastDay_LeavesTheClosingDayAlone()
    {
        var engine = Started("forste_sesong", cash: 400_000_000m);
        Assert.True(engine.ApplyForGrowthLicence("po04_solund").Ok);
        var auction = engine.State.LicenseAuctions.Single();
        int closes = auction.ClosesDay;
        Assert.True(engine.BidOnLicenseAuction("po04_solund", engine.LicenseReservePriceNOK).Ok);
        Assert.Equal(closes, auction.ClosesDay);
    }

    // ── Prices against the state's auctions ──────────────────────────────

    /// <summary>
    /// The state's capacity auctions cleared at NOK 154,000 a tonne on average
    /// in 2022 and NOK 305,000 in 2024 (by area 170,000-348,400 in 2024);
    /// regjeringen.no, "Resultater fra auksjon av nye oppdrettstillatelser
    /// 2024" (25 June 2024: 17,330 t for NOK 5,288,602,600). A rival-won
    /// licence in the game, where the company opens the auction and does not
    /// bid, clears inside that band on every seed.
    /// </summary>
    [Fact]
    public void RivalWonLicences_ClearInsideTheStatesAuctionBand()
    {
        var perTonne = new List<decimal>();
        foreach (int seed in Enumerable.Range(1, 30))
        {
            foreach (var zone in new[] { "po04_solund", "po12_hasvik" })
            {
                var engine = Started("forste_sesong", seed: seed);
                Assert.True(engine.ApplyForGrowthLicence(zone).Ok);
                var auction = engine.State.LicenseAuctions.Single();
                for (int i = 0; i < 20 && auction.Status == "open"; i++)
                    engine.Advance(1);
                Assert.Equal("won_rival", auction.Status);
                perTonne.Add(Math.Round(auction.CurrentBidNOK / engine.StandardLicenceMtbTonnes(zone)));
            }
        }
        perTonne.Sort();
        decimal median = perTonne[perTonne.Count / 2];
        _output.WriteLine($"clearing NOK/t: min {perTonne[0]:N0} median {median:N0} max {perTonne[^1]:N0} (n={perTonne.Count})");
        Assert.All(perTonne, p => Assert.InRange(p, LicenseAuctionSystem.StateAuction2022AveragePerTonneNok,
            LicenseAuctionSystem.StateAuction2024HighestAreaPerTonneNok));
        Assert.Equal(154_000m, LicenseAuctionSystem.StateAuction2022AveragePerTonneNok);
        Assert.Equal(348_400m, LicenseAuctionSystem.StateAuction2024HighestAreaPerTonneNok);
    }
}
