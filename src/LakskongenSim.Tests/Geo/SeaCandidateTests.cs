#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using StateHashProbe;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// Geo stages 2-3, track B (docs/geo/STAGE23_PLAN.md §2 B): the open-pen
/// candidates in the U2-01 build flow. The area's 12 candidates in rank order
/// with their weights, costs, holder, build zone and verdict; reads that change
/// nothing; a build plan at a candidate that equals the zone's plan; a build at
/// a candidate that is U2-01's build plus the candidate on the site; the
/// refusals in their order; the build zone in the map's zone order; the
/// candidate surviving a save while every older save loads without one; and
/// where the company's sites lie.
/// </summary>
public class SeaCandidateTests
{
    private static SimulationEngine Started(string scenarioId, int seed = 17, decimal? cash = null)
    {
        var probe = new SimulationEngine();
        probe.Initialize(enableEvents: false, seed: seed);
        decimal capital = probe.Scenario.GetScenario(scenarioId)!.StartingCapitalNOK;
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(capital) };
        engine.Initialize(enableEvents: false, seed: seed);
        engine.Scenario.Unlock(scenarioId);
        Assert.True(engine.StartScenario(scenarioId).Ok);
        if (cash.HasValue && cash.Value > engine.FinanceManager.Balance)
            engine.ReceiveIncome(cash.Value - engine.FinanceManager.Balance, "test_capital");
        return engine;
    }

    private static string Snapshot(SimulationEngine engine)
        => SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));

    private static readonly Regex CandidateLine = new(@"^\s*""CandidateId"": ""[^""]*"",\r?\n", RegexOptions.Multiline);

    private static IEnumerable<string> ScenarioIds()
        => new ScenarioSystem(new GameState()).All.Keys.OrderBy(k => k, StringComparer.Ordinal);

    // ── The area's candidates ────────────────────────────────────────────

    [Fact]
    public void EveryArea_ListsItsTwelveCandidates_InRankOrder_TheFirstFreeOneSuggested()
    {
        var engine = Started("forste_sesong");
        for (int po = 1; po <= ProductionAreas.Count; po++)
        {
            var views = engine.GetSeaCandidates(po);
            Assert.Equal(GeoCandidates.Default.OpenPensOf(po).Select(c => c.Id), views.Select(v => v.Id));
            Assert.Equal(Enumerable.Range(1, 12), views.Select(v => v.Rank));
            Assert.All(views, v => Assert.Equal(po, v.Po));
            Assert.All(views, v => Assert.Same(GeoCandidates.Default.Find(v.Id), v.Candidate));
            Assert.Equal(new[] { views[0].Id }, views.Where(v => v.Suggested).Select(v => v.Id));
            Assert.All(views, v => Assert.False(v.Taken));
        }
        Assert.Empty(engine.GetSeaCandidates(0));
        Assert.Empty(engine.GetSeaCandidates(14));
    }

    [Fact]
    public void AView_CarriesTheWeightsAndWhatEachTermCostsTheScore_TheBindingTermCostingMost()
    {
        var engine = Started("forste_sesong");
        var rules = GeoSuitabilityRules.Default;
        foreach (var v in Enumerable.Range(1, ProductionAreas.Count).SelectMany(po => engine.GetSeaCandidates(po)))
        {
            Assert.Same(rules.EffectiveWeights, v.Weights);
            Assert.Equal(rules.ComponentOrder.Append(GeoSuitabilityRules.RiverTerm), v.ComponentCosts.Select(c => c.Key));
            foreach (var (term, cost) in v.ComponentCosts)
                Assert.Equal(rules.ComponentCost(v.Candidate, term), cost);
            var top = v.ComponentCosts.OrderByDescending(c => c.Value).First();
            string binding = top.Value > 1e-9 ? top.Key : GeoSuitabilityRules.NoBinding;
            // The shipped binding is the term costing most (ties to the pipeline's first).
            Assert.True(binding == v.Candidate.Binding
                        || Math.Abs(v.ComponentCosts.Single(c => c.Key == v.Candidate.Binding).Value - top.Value) < 1e-12,
                $"{v.Id}: binds '{v.Candidate.Binding}', the largest cost is '{top.Key}'");
        }
    }

    [Fact]
    public void ReadingTheCandidatesThePlansAndTheLocations_ChangesNothingAndDrawsNothing()
    {
        var engine = Started("forste_sesong", cash: 20_000_000m);
        Assert.True(engine.BuildSeaSiteAt("fjord_west", "PO03-02").Ok);
        engine.Advance(2);
        string before = Snapshot(engine);
        var streams = engine.Rng.CaptureStates();
        string day = GoldenTraceState.Capture(engine).DayHash();

        for (int po = 0; po <= 14; po++)
            engine.GetSeaCandidates(po);
        foreach (var zone in ExpansionZones.All.Select(z => z.ZoneId).Append("fjord_west").Append("nowhere"))
        foreach (var id in new[] { "PO03-01", "PO03-02", "PO04-01", "HTH02-01", "nope" })
            engine.PlanSiteBuildAt(zone, id);
        engine.GetPlayerSiteLocations();

        Assert.Equal(streams, engine.Rng.CaptureStates());
        Assert.Equal(day, GoldenTraceState.Capture(engine).DayHash());
        Assert.Equal(before, Snapshot(engine));
    }

    // ── The plan and the build ───────────────────────────────────────────

    [Fact]
    public void ThePlanAtACandidate_IsTheZonesPlan_OnEveryFigure_WithTheCandidateAndItsChecks()
    {
        var engine = Started("forste_sesong", cash: 20_000_000m);
        foreach (var (zone, id) in new[] { ("fjord_west", "PO03-01"), ("po03_bomlo", "PO03-05"), ("po04_solund", "PO04-01"),
                                           ("fjord_west", "PO04-01"), ("fjord_west", "nope") })
        {
            var at = engine.PlanSiteBuildAt(zone, id);
            Assert.Equal(engine.PlanSiteBuild(zone), at.Plan);
            Assert.Equal(engine.CandidateCheck(zone, id), at.CandidateVerdict);
            Assert.Equal(at.CandidateVerdict.Allowed ? at.Plan.Verdict : at.CandidateVerdict, at.Verdict);
        }
        var home = engine.PlanSiteBuildAt("fjord_west", "PO03-01");
        Assert.True(home.Verdict.Allowed, home.Verdict.ReasonKey);
        Assert.Equal("PO03-01", home.Candidate!.Id);
        Assert.Equal("fjord_west", home.Candidate.BuildZoneId);
        Assert.Equal(SeaCandidateReasons.CandidateWrongArea, engine.PlanSiteBuildAt("fjord_west", "PO04-01").Verdict.ReasonKey);
        Assert.Equal(ExpansionReasons.GrowthLicenceRequired, engine.PlanSiteBuildAt("po03_bomlo", "PO03-05").Verdict.ReasonKey);
        Assert.Null(engine.PlanSiteBuildAt("fjord_west", "nope").Candidate);
    }

    /// <summary>
    /// Twin games: one builds with U2-01's verb, the other at a candidate. The
    /// cost, the licence, the admitted zone, the pens and every other saved
    /// value are equal; the only difference is the candidate on the new site.
    /// </summary>
    [Theory]
    [InlineData("fjord_west", "PO03-01")]   // organic growth in the home zone
    [InlineData("po04_solund", "PO04-03")]  // a catalogue zone on a won growth licence
    public void ABuildAtACandidate_IsU2_01sBuild_PlusTheCandidateOnTheSite(string zone, string candidate)
    {
        SimulationEngine Prepared()
        {
            var engine = Started("forste_sesong", seed: 29, cash: 300_000_000m);
            if (zone == "po04_solund")
            {
                foreach (var rival in engine.CompetitorAI.Competitors)
                {
                    rival.IsDistressed = true;
                    rival.CashReserves = 0m;
                }
                Assert.True(engine.ApplyForGrowthLicence(zone).Ok);
                Assert.True(engine.BidOnLicenseAuction(zone, engine.LicenseReservePriceNOK).Ok);
                engine.Advance(LicenseAuctionSystem.AuctionLengthDays + 1);
                Assert.True(engine.HasGrowthLicence(zone));
            }
            return engine;
        }

        var plain = Prepared();
        var at = Prepared();
        Assert.Equal(Snapshot(plain), Snapshot(at));
        decimal balance = at.FinanceManager.Balance;
        var built = plain.BuildSeaSite(zone, "");
        var builtAt = at.BuildSeaSiteAt(zone, candidate);
        Assert.True(builtAt.Ok, builtAt.Message);
        Assert.Equal(built.Id, builtAt.Id);
        Assert.Equal(built.Message, builtAt.Message);
        Assert.Equal(balance - at.SiteBuildCostNOK, at.FinanceManager.Balance);

        var site = at.State.SeaSites.Single(s => s.Id == builtAt.Id);
        Assert.Equal(candidate, site.CandidateId);
        Assert.Null(plain.State.SeaSites.Single(s => s.Id == built.Id).CandidateId);
        Assert.Contains(at.State.Zones, z => z.Id == zone);
        if (zone == "po04_solund")
            Assert.True(at.State.LicenseAuctions.Single(a => a.ZoneId == zone).ConsumedByBuild);

        string withCandidate = Snapshot(at);
        Assert.Single(CandidateLine.Matches(withCandidate));
        Assert.Equal(Snapshot(plain), CandidateLine.Replace(withCandidate, ""));
    }

    [Fact]
    public void ACandidateCarriesOneLocality_TheNextFreeOneIsSuggested()
    {
        var engine = Started("forste_sesong", cash: 40_000_000m);
        var first = engine.BuildSeaSiteAt("fjord_west", "PO03-01");
        Assert.True(first.Ok, first.Message);
        var site = engine.State.SeaSites.Single(s => s.Id == first.Id);

        var again = engine.BuildSeaSiteAt("fjord_west", "PO03-01");
        Assert.Equal(SeaCandidateReasons.CandidateTaken, again.ErrorKey);
        var views = engine.GetSeaCandidates(3);
        var taken = views.Single(v => v.Id == "PO03-01");
        Assert.Equal(site.Id, taken.TakenBySiteId);
        Assert.Equal(site.Label, taken.TakenBySiteLabel);
        Assert.Equal(SeaCandidateReasons.CandidateTaken, taken.Verdict.ReasonKey);
        Assert.Equal(new[] { "PO03-02" }, views.Where(v => v.Suggested).Select(v => v.Id));
        Assert.True(views.Single(v => v.Id == "PO03-02").Verdict.Allowed);
    }

    /// <summary>
    /// Tutorial step 16 on the dialog's route (the home zone's «Bygg lokalitet»,
    /// then the confirm): the dialog preselects the first free place, the
    /// build there completes the step as U2-01's build did, and the new site
    /// lies at PO03-01. The minimal first-season player gets there; the same
    /// route by input alone is FirstSeasonFlowChecks'.
    /// </summary>
    [Fact]
    public void TutorialStep16_BuildsTheSecondSiteAtThePreselectedFirstPlace()
    {
        var engine = Tutorial.TutorialPlayer.Start("forste_sesong", 4, events: false);
        var player = new Tutorial.TutorialPlayer();
        for (int day = 0; day < 1500 && engine.Tutorial.CurrentStep?.Id != "build_site"; day++)
        {
            for (int guard = 0; guard < 32 && engine.Tutorial.IsActive && engine.Tutorial.CurrentStep?.Id != "build_site"; guard++)
            {
                int before = engine.Tutorial.CurrentStepNumber;
                player.DoOne(engine);
                if (engine.Tutorial.CurrentStepNumber == before) break;
            }
            if (engine.Tutorial.CurrentStep?.Id != "build_site") engine.Advance(1);
        }
        Assert.Equal("build_site", engine.Tutorial.CurrentStep?.Id);
        var home = engine.State.SeaSites.Single();
        for (int day = 0; day < 365 && !engine.BuildVerdictOf(home.ZoneId).Allowed; day++)
            engine.Advance(1);   // as the player: the build waits for its cash

        var suggested = Assert.Single(engine.GetSeaCandidates(engine.ProductionAreaOfZone(home.ZoneId)), v => v.Suggested);
        Assert.Equal(("PO03-01", home.ZoneId), (suggested.Id, suggested.BuildZoneId));
        Assert.True(suggested.Verdict.Allowed, suggested.Verdict.ReasonKey);
        var built = engine.BuildSeaSiteAt(home.ZoneId, suggested.Id);
        Assert.True(built.Ok, built.Message);
        engine.Tutorial.CheckProgress();
        Assert.Equal("stock_new_site", engine.Tutorial.CurrentStep?.Id);
        var at = engine.GetPlayerSiteLocations().Last();
        Assert.Equal((built.Id, PlayerSiteLocation.CandidateSource, "PO03-01"), (at.SiteId, at.Source, at.CandidateId));
    }

    // ── The refusals, in order ───────────────────────────────────────────

    [Fact]
    public void EachRefusal_HasItsKey_InTheListedOrder_BeforeTheZonesOwnGate()
    {
        var idle = new SimulationEngine();
        Assert.Equal(SeaCandidateReasons.EngineNotReady, idle.CandidateCheck("fjord_west", "PO03-01").ReasonKey);
        Assert.Equal(SeaCandidateReasons.EngineNotReady, idle.BuildSeaSiteAt("fjord_west", "PO03-01").ErrorKey);

        var engine = Started("forste_sesong", cash: 40_000_000m);
        string before = Snapshot(engine);
        Assert.Equal(SeaCandidateReasons.CandidateNotFound, engine.BuildSeaSiteAt("fjord_west", "PO99-01").ErrorKey);
        Assert.Equal(SeaCandidateReasons.CandidateNotFound, engine.BuildSeaSiteAt("fjord_west", "HTH02-01").ErrorKey);   // offshore: no open pen
        Assert.Equal(SeaCandidateReasons.CandidateNotFound, engine.BuildSeaSiteAt("nowhere", "PO99-01").ErrorKey);
        Assert.Equal(SeaCandidateReasons.CandidateWrongArea, engine.BuildSeaSiteAt("fjord_west", "PO04-01").ErrorKey);
        Assert.Equal(SeaCandidateReasons.CandidateWrongArea, engine.BuildSeaSiteAt("po04_solund", "PO03-01").ErrorKey);
        // The candidate checks pass, the zone's own gate refuses with its key.
        Assert.Equal(ExpansionReasons.GrowthLicenceRequired, engine.BuildSeaSiteAt("po03_bomlo", "PO03-01").ErrorKey);
        Assert.Equal(before, Snapshot(engine));

        Assert.True(engine.BuildSeaSiteAt("fjord_west", "PO03-01").Ok);
        Assert.Equal(SeaCandidateReasons.CandidateTaken, engine.CandidateCheck("po03_bomlo", "PO03-01").ReasonKey);
        // A wrong area outranks a taken candidate; a taken one outranks an illegal cell.
        Assert.Equal(SeaCandidateReasons.CandidateWrongArea, engine.CandidateCheck("po04_solund", "PO03-01").ReasonKey);
        Assert.Equal(SeaCandidateReasons.CandidateTaken, engine.CandidateCheck("fjord_west", "PO03-01", _ => false).ReasonKey);
        Assert.Equal(SeaCandidateReasons.CandidateNotSuitable, engine.CandidateCheck("fjord_west", "PO03-02", _ => false).ReasonKey);
        Assert.True(engine.CandidateCheck("fjord_west", "PO03-02").Allowed);

        // A cost refusal is the zone's: nothing changes, no candidate is held.
        engine.FinanceManager.Spend(engine.FinanceManager.Balance - 1_000_000m);
        string poor = Snapshot(engine);
        Assert.Equal(ExpansionReasons.InsufficientFunds, engine.BuildSeaSiteAt("fjord_west", "PO03-02").ErrorKey);
        Assert.Equal(poor, Snapshot(engine));
    }

    [Fact]
    public void EveryShippedOpenPenCandidate_PassesTheSuitabilityGuard()
    {
        var engine = Started("forste_sesong");
        foreach (var c in Enumerable.Range(1, ProductionAreas.Count).SelectMany(GeoCandidates.Default.OpenPensOf))
            Assert.True(engine.CandidateCheck(engine.SeaBuildZoneOf(c.Po), c.Id).Allowed, c.Id);
    }

    // ── The build zone follows the map ───────────────────────────────────

    [Fact]
    public void TheBuildZone_IsTheMapsFirstZoneOfTheArea_InEveryScenario()
    {
        foreach (var id in ScenarioIds())
        {
            var engine = Started(id);
            var map = engine.GetExpansionOverview();
            for (int po = 1; po <= ProductionAreas.Count; po++)
            {
                string first = map.Areas[po - 1].Zones[0].ZoneId;
                Assert.Equal(first, engine.SeaBuildZoneOf(po));
                Assert.All(engine.GetSeaCandidates(po), v => Assert.Equal(first, v.BuildZoneId));
            }
        }
    }

    [Fact]
    public void TheBuildZone_IsTheZoneWithMostFjordSites_TiesByOrdinalId()
    {
        var engine = Started("po3_red_sone", cash: 100_000_000m);
        var po3 = engine.State.Zones.Where(z => engine.ProductionAreaOfZone(z.Id) == 3).Select(z => z.Id)
            .OrderBy(z => z, StringComparer.Ordinal).ToList();
        Assert.True(po3.Count >= 2, string.Join(",", po3));
        Assert.Equal(po3[0], engine.SeaBuildZoneOf(3));
        // A second site in the other zone makes it the build zone.
        Assert.True(engine.BuildSeaSite(po3[1], "").Ok);
        int sites0 = engine.State.SeaSites.Count(s => s.ZoneId == po3[0]);
        int sites1 = engine.State.SeaSites.Count(s => s.ZoneId == po3[1]);
        string expected = sites1 > sites0 ? po3[1] : po3[0];
        Assert.Equal(expected, engine.SeaBuildZoneOf(3));
        Assert.Equal(engine.GetExpansionOverview().Areas[2].Zones[0].ZoneId, engine.SeaBuildZoneOf(3));
        // An area the company does not farm builds through its catalogue zone.
        Assert.Equal("po12_hasvik", engine.SeaBuildZoneOf(12));
        Assert.Equal("", engine.SeaBuildZoneOf(14));
    }

    // ── Saves ────────────────────────────────────────────────────────────

    [Fact]
    public void TheCandidate_SurvivesASave_AndTheResumedGameRunsOnIdentically()
    {
        var engine = Started("forste_sesong", seed: 31, cash: 40_000_000m);
        var built = engine.BuildSeaSiteAt("fjord_west", "PO03-04");
        Assert.True(built.Ok, built.Message);
        string saved = SaveStateComparer.Serialize(engine);
        Assert.Contains("\"CandidateId\": \"PO03-04\"", saved);

        var resumed = SaveStateComparer.Resume(saved, events: false);
        Assert.Equal("PO03-04", resumed.State.SeaSites.Single(s => s.Id == built.Id).CandidateId);
        Assert.Equal(built.Id, resumed.GetSeaCandidates(3).Single(v => v.Id == "PO03-04").TakenBySiteId);
        Assert.Equal(SeaCandidateReasons.CandidateTaken, resumed.BuildSeaSiteAt("fjord_west", "PO03-04").ErrorKey);

        engine.Advance(5);
        resumed.Advance(5);
        string a = Snapshot(engine);
        string b = Snapshot(resumed);
        Assert.True(a == b, "resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
    }

    [Fact]
    public void NoScenarioStart_WritesACandidate_SoItsSaveIsTheOlderFormatByteForByte()
    {
        foreach (var id in ScenarioIds())
        {
            var engine = Started(id);
            string saved = SaveStateComparer.Serialize(engine);
            Assert.DoesNotContain("CandidateId", saved);
            // Read back as an older (v11) save that never had the property: no site holds a candidate.
            var resumed = SaveStateComparer.Resume(saved, events: false);
            Assert.All(resumed.State.SeaSites, s => Assert.Null(s.CandidateId));
            Assert.Equal(saved, SaveStateComparer.Serialize(resumed));
        }
    }

    [Fact]
    public void ThePreF2FacilitiesSave_AndAnExplicitNull_LoadWithoutACandidate()
    {
        var fixture = SaveStateComparer.Resume(FarmTypeMoveTests.FixtureGameState(), events: false);
        Assert.NotEmpty(fixture.State.SeaSites);
        Assert.All(fixture.State.SeaSites, s => Assert.Null(s.CandidateId));
        Assert.All(fixture.GetPlayerSiteLocations(), l => Assert.Equal(PlayerSiteLocation.AnchorSource, l.Source));

        var engine = Started("forste_sesong");
        string json = SaveStateComparer.Serialize(engine).Replace("\"PlotId\": \"\",", "\"PlotId\": \"\",\n      \"CandidateId\": null,");
        Assert.Contains("\"CandidateId\": null", json);
        var resumed = SaveStateComparer.Resume(json, events: false);
        Assert.All(resumed.State.SeaSites, s => Assert.Null(s.CandidateId));
        Assert.DoesNotContain("CandidateId", SaveStateComparer.Serialize(resumed));
    }

    // ── Where the company's sites lie ────────────────────────────────────

    [Fact]
    public void TheSitesLie_AtTheirCandidate_OrApproximatelyAtTheirZonesAnchor()
    {
        var engine = Started("forste_sesong", cash: 40_000_000m);
        var start = Assert.Single(engine.GetPlayerSiteLocations());
        var home = engine.State.SeaSites.Single();
        var anchor = ScenarioGeoAnchors.AnchorOf(home.ZoneId, 3);
        Assert.Equal((home.Id, home.Label, PlayerSiteLocation.AnchorSource, 3, ""), (start.SiteId, start.SiteLabel, start.Source, start.Po, start.CandidateId));
        Assert.Equal((anchor.Name, (long)anchor.X, (long)anchor.Y), (start.Label, start.X, start.Y));
        Assert.True(start.Approximate);

        var built = engine.BuildSeaSiteAt("fjord_west", "PO03-01");
        Assert.True(built.Ok, built.Message);
        var rows = engine.GetPlayerSiteLocations();
        Assert.Equal(new[] { home.Id, built.Id }, rows.Select(r => r.SiteId));
        var c = GeoCandidates.Default.Find("PO03-01")!;
        var at = rows[1];
        Assert.Equal((PlayerSiteLocation.CandidateSource, "PO03-01", c.Label, 3, c.X, c.Y),
            (at.Source, at.CandidateId, at.Label, at.Po, at.X, at.Y));
        Assert.False(at.Approximate);
        Assert.Equal(engine.State.SeaSites.Single(s => s.Id == built.Id).Label, at.SiteLabel);
        Assert.Equal(rows[0], start);
    }
}
