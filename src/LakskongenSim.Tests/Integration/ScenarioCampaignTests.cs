using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Review next-step 10, scenario and tutorial chain (F1005, F949, F1013, F1014)
/// plus the blockers found while playing them: the first five scenarios had no
/// farm (the tutorial's 300k NOK could not buy a 6M site), the tutorial's
/// harvest/sale steps waited for processing snapshots that play never creates,
/// a one-pen farm paid a full eight-pen crew and full-size event costs, and the
/// scenario rules (calm weather, easy market, harsh storms, premium prices) were
/// labels with no effect.
/// </summary>
public class ScenarioCampaignTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static SimulationEngine NewEngine(int seed, decimal cash = 10_000_000m, bool events = false)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(cash);
        engine.Initialize(enableEvents: events, seed: seed);
        return engine;
    }

    private static SimulationEngine Started(string scenarioId, int seed, bool events = false)
    {
        var probe = NewEngine(seed);
        var def = probe.Scenario.GetScenario(scenarioId)!;
        var engine = NewEngine(seed, def.StartingCapitalNOK, events);
        // Locked scenarios cannot be started from the menu; apply them directly.
        if (def.IsUnlocked)
            Assert.True(engine.StartScenario(scenarioId).Ok);
        else
            engine.Scenario.GetScenario(scenarioId)!.ApplyStartingState(engine.State, engine);
        return engine;
    }

    private static IEnumerable<ScenarioDefinition> Roster() => NewEngine(1).Scenario.All.Values;

    // ── Every scenario starts with a farm (F1013) ───────────────────────────

    [Fact]
    public void EveryScenario_StartsWithAStockedFarm()
    {
        foreach (var def in Roster())
        {
            var engine = Started(def.Id, 2);
            Assert.True(engine.State.SeaSites.Count >= 1, $"{def.Id}: no sea site");
            Assert.True(engine.State.Populations.Sum(p => p.TotalCount) > 0, $"{def.Id}: no fish");
        }
    }

    [Fact]
    public void ScenarioJson_MatchesTheCodeRoster()
    {
        var roster = Roster().ToDictionary(d => d.Id);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "scenarios.json")));
        int compared = 0;
        foreach (var json in doc.RootElement.EnumerateArray())
        {
            string id = json.GetProperty("id").GetString()!;
            if (!roster.TryGetValue(id, out var def)) continue;
            compared++;
            Assert.Equal((double)def.StartingCapitalNOK, json.GetProperty("starting_balance").GetDouble());
            Assert.Equal(def.WinCondition.ToString(), json.GetProperty("win_condition").GetString());

            var sites = json.GetProperty("starting_sites").EnumerateArray().ToList();
            Assert.Equal(def.StartingSites.Count, sites.Count);
            for (int i = 0; i < sites.Count; i++)
            {
                Assert.Equal(def.StartingSites[i].Id, sites[i].GetProperty("id").GetString());
                Assert.Equal(def.StartingSites[i].Zone, sites[i].GetProperty("zone").GetString());
                Assert.Equal(def.StartingSites[i].Pens, sites[i].GetProperty("pens").GetInt32());
                Assert.Equal(def.StartingSites[i].SmoltCount, sites[i].GetProperty("smolt_count").GetInt32());
            }

            def.SpecialRules.TryGetValue("profit_target", out var codeTarget);
            string? jsonTarget = json.TryGetProperty("special_rules", out var rules)
                && rules.TryGetProperty("profit_target", out var t) ? t.GetString() : null;
            Assert.Equal(codeTarget, jsonTarget);
            Assert.Equal(def.SpecialRules.ContainsKey("calm_weather"),
                json.TryGetProperty("special_rules", out var r2) && r2.TryGetProperty("calm_weather", out _));
        }
        Assert.Equal(roster.Count, compared);
    }

    // ── F1005: profit targets are above the starting capital ────────────────

    [Fact]
    public void ProfitAndCompetitionTargets_LieAboveTheStartingCapital()
    {
        foreach (var def in Roster().Where(d => d.WinCondition is WinCondition.Profit or WinCondition.Competition))
        {
            Assert.True(def.SpecialRules.TryGetValue("profit_target", out var raw), $"{def.Id}: no profit_target");
            Assert.True(decimal.Parse(raw!) > def.StartingCapitalNOK,
                $"{def.Id}: target {raw} is not above the starting capital {def.StartingCapitalNOK:N0}");
        }
    }

    [Fact]
    public void Ressursrente_IsNotWonOnDayOne()
    {
        var engine = Started("ressursrente", 3);
        engine.Advance(1);
        Assert.False(engine.Scenario.IsWon);
    }

    // ── F949: goal progress measured like the win check ─────────────────────

    [Fact]
    public void GoalStatus_SurvivalCountsTheTimeLimit()
    {
        var engine = Started("po3_red_sone", 4);
        engine.Advance(73);
        var goal = engine.GetScenarioGoalStatus()!;
        Assert.Equal("po3_red_sone", goal.ScenarioId);
        Assert.Equal(WinCondition.Survival, goal.Condition);
        Assert.Equal(73.0 / 1095.0, goal.Progress01, 3);
        Assert.Equal(1095 - 73, goal.DaysLeft);
        Assert.False(string.IsNullOrWhiteSpace(goal.Objective));
    }

    [Fact]
    public void GoalStatus_ProfitIsTheShareOfTheWayToTheTarget()
    {
        var engine = Started("ressursrente", 5);
        Assert.Equal(0.0, engine.GetScenarioGoalStatus()!.Progress01, 3);
        engine.ReceiveIncome(12_500_000m, "test");      // half of the 25M asked for
        Assert.Equal(0.5, engine.GetScenarioGoalStatus()!.Progress01, 3);
        Assert.Null(engine.GetScenarioGoalStatus()!.DaysLeft);
    }

    [Fact]
    public void GoalStatus_TutorialFollowsTheSteps_AndNoScenarioMeansNoGoal()
    {
        var engine = Started("forste_sesong", 6);
        engine.Advance(3);
        Assert.Equal(engine.Tutorial.Progress01, engine.GetScenarioGoalStatus()!.Progress01, 9);
        Assert.True(engine.Tutorial.Progress01 > 0.0);

        Assert.Null(NewEngine(6).GetScenarioGoalStatus());
    }

    // ── Scenario rules act on the live systems ──────────────────────────────

    private static int StormDays(SimulationEngine engine, int days)
    {
        int storms = 0;
        for (int d = 0; d < days; d++)
        {
            engine.Advance(1);
            if (engine.Weather.CurrentState is WeatherState.Storm or WeatherState.Severe) storms++;
        }
        return storms;
    }

    [Fact]
    public void CalmWeather_HasNoStorms_AndSurvivesALoad()
    {
        var engine = Started("forste_sesong", 7, events: true);
        Assert.Equal(0, StormDays(engine, 365));
        Assert.Equal(WeatherState.Moderate, engine.Weather.MaxState);
        Assert.True(engine.Storyteller.SuppressStormEvents);

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        Assert.Equal(WeatherState.Moderate, resumed.Weather.MaxState);
        Assert.True(resumed.Storyteller.SuppressStormEvents);
        Assert.Equal(0, StormDays(resumed, 365));
    }

    [Fact]
    public void HarshStorms_BringMoreStormDaysThanTheDefault()
    {
        int harsh = StormDays(Started("lofoten_eventyr", 8), 730);
        int plain = StormDays(NewEngine(8), 730);
        Assert.True(harsh > plain, $"harsh {harsh} vs default {plain} storm days");
    }

    [Fact]
    public void EasyMarket_HalvesPriceNoise()
    {
        static double DailyMoves(SimulationEngine engine)
        {
            double sum = 0.0, prev = (double)engine.MarketSimulation.BasePricePerKg;
            for (int d = 0; d < 365; d++)
            {
                engine.Advance(1);
                double now = (double)engine.MarketSimulation.BasePricePerKg;
                sum += Math.Abs(now - prev);
                prev = now;
            }
            return sum;
        }
        var easy = Started("forste_sesong", 9);
        Assert.Equal(0.5, easy.MarketSimulation.VolatilityScale);
        Assert.True(DailyMoves(easy) < DailyMoves(NewEngine(9)));
    }

    [Fact]
    public void PremiumPrices_AddTheExportPremium()
    {
        Assert.Equal(1.10, Started("lofoten_eventyr", 10).ScenarioPriceMultiplier);
        Assert.Equal(1.0, Started("po3_red_sone", 10).ScenarioPriceMultiplier);
    }

    // ── Small farms pay small-farm costs ────────────────────────────────────

    [Fact]
    public void SiteCrew_ScalesWithPens_AtLeastOneWorker()
    {
        var engine = NewEngine(11);
        Assert.Equal(1m, engine.SiteWorkers(new LakskongenSim.Farm.SeaSite("one") { NumberOfPens = 1 }));
        Assert.Equal(4m, engine.SiteWorkers(new LakskongenSim.Farm.SeaSite("four") { NumberOfPens = 4 }));
        Assert.Equal(7m, engine.SiteWorkers(new LakskongenSim.Farm.SeaSite("eight") { NumberOfPens = 8 }));
    }

    [Fact]
    public void EventCosts_ShrinkToTheFarm_GainsDoNot()
    {
        var engine = NewEngine(12, events: true);
        var cost = new EventDefinition
        {
            Id = "test-cost", Name = "Test cost", Category = EventCategory.Market, Weight = 1.0,
            Apply = (gs, rec) => { rec.BalanceChange = -1_000_000m; engine.FinanceManager.ForceSpend(1_000_000m); }
        };
        var gain = new EventDefinition
        {
            Id = "test-gain", Name = "Test gain", Category = EventCategory.Market, Weight = 1.0,
            Apply = (gs, rec) => { rec.BalanceChange = 400_000m; engine.FinanceManager.AddRevenue(400_000m); }
        };
        engine.Storyteller.CostScale = () => 0.25;

        decimal before = engine.FinanceManager.Balance;
        var costRecord = engine.Storyteller.Fire(cost, engine.State.Day);
        Assert.Equal(before - 250_000m, engine.FinanceManager.Balance);
        Assert.Equal(-250_000m, costRecord.BalanceChange);

        before = engine.FinanceManager.Balance;
        engine.Storyteller.Fire(gain, engine.State.Day);
        Assert.Equal(before + 400_000m, engine.FinanceManager.Balance);
        Assert.Equal(0m, engine.UnbookedCashTotal);
    }

    // ── Research verb, debt getter ──────────────────────────────────────────

    [Fact]
    public void StartResearchProject_PaysAndStarts_OrFailsWithAKey()
    {
        var engine = NewEngine(13);
        Assert.Equal("unknown_node", engine.StartResearchProject("no-such-node").ErrorKey);
        decimal before = engine.FinanceManager.Balance;
        var ok = engine.StartResearchProject("premium-products");
        Assert.True(ok.Ok, ok.Message);
        Assert.Equal(before - 500_000m, engine.FinanceManager.Balance);
        Assert.Equal("research_unavailable", engine.StartResearchProject("selective-breeding").ErrorKey);

        var broke = NewEngine(13, cash: 100_000m);
        Assert.Equal("insufficient_funds", broke.StartResearchProject("premium-products").ErrorKey);
    }

    [Fact]
    public void TotalDebt_IsTheOutstandingLoanPrincipal()
    {
        var engine = NewEngine(14);
        Assert.Equal(0m, engine.TotalDebtNok);
        Assert.True(engine.TakeLoan(2_000_000, 36, 0.07).Ok);
        Assert.Equal(2_000_000m, engine.TotalDebtNok);
    }

    // ── F1014: the tutorial can be played to the end ────────────────────────

    private sealed record TutorialRun(bool Complete, bool Won, bool Lost, int Day, int Step, decimal MinCash);

    /// <summary>
    /// A minimal player: delouse above 0.4 adult females, harvest at 4.5 kg,
    /// restock up to 20,000 smolts, start the cheapest research once a harvest
    /// paid, build a second site when 6.5M is in the bank.
    /// </summary>
    private static TutorialRun PlayTutorial(int seed, int horizonDays = 1500)
    {
        var engine = Started("forste_sesong", seed, events: true);
        bool harvested = false, research = false, secondSite = false;
        decimal minCash = decimal.MaxValue;
        for (int day = 0; day < horizonDays && !engine.Tutorial.IsComplete; day++)
        {
            engine.Advance(1);
            minCash = Math.Min(minCash, engine.FinanceManager.Balance);
            var site = engine.State.SeaSites[0];
            int fish = site.Pens.Sum(p => p.TotalCount);
            if (fish > 0)
            {
                if (site.Lice.AdultFemalePerFish > 0.4) engine.DelouseSite(site.Id, "hydrolicer");
                if (site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish >= 4.5)
                    harvested |= engine.HarvestSite(site.Id).Ok;
            }
            else if (harvested)
            {
                int smolts = (int)Math.Min(20_000, Math.Max(0m, engine.FinanceManager.Balance - 500_000m) / 12m);
                if (smolts >= 5_000) engine.StockSmolts(site.Id, smolts);
            }
            if (!research && harvested)
            {
                var node = engine.Research.Nodes.Values
                    .Where(n => engine.Research.CanResearch(n.Id)).OrderBy(n => n.CostNOK).First();
                if (engine.FinanceManager.Balance > node.CostNOK + 200_000m)
                    research = engine.StartResearchProject(node.Id).Ok;
            }
            if (!secondSite && engine.FinanceManager.Balance > 6_500_000m)
                secondSite = engine.BuildSeaSite(site.ZoneId, "andre_anlegg").Ok;
        }
        return new TutorialRun(engine.Tutorial.IsComplete, engine.Scenario.IsWon, engine.Scenario.IsLost,
            engine.State.Day, engine.Tutorial.CurrentStepNumber, minCash);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(21)]
    public void TutorialScenario_IsPlayableThroughAllEighteenSteps(int seed)
    {
        var run = PlayTutorial(seed);
        Assert.True(run.Complete, $"seed {seed}: stalled at step {run.Step} on day {run.Day}");
        Assert.True(run.Won, $"seed {seed}: tutorial complete but the scenario was not won");
        Assert.False(run.Lost);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(42)]
    public void TutorialScenario_SlowSeedsStaySolvent_AndReachTheExpansionStep(int seed)
    {
        var run = PlayTutorial(seed);
        Assert.False(run.Lost, $"seed {seed}: bankrupt (min cash {run.MinCash:N0})");
        Assert.True(run.Step >= 16, $"seed {seed}: stalled at step {run.Step} on day {run.Day}");
    }

    [Fact]
    public void TutorialLiceStep_HasATimeFallback()
    {
        // Calm tutorial, no events: lice stay low, so the lice step can only open
        // through the day-150 fallback (it used to wait for 0.8 lice forever).
        var engine = Started("forste_sesong", 9, events: false);
        engine.Advance(TutorialSystem.LiceStepFallbackDay - 10);
        Assert.Equal("apply-delousing", engine.Tutorial.CurrentStep?.Id);
        engine.Advance(15);
        Assert.True(engine.Tutorial.CurrentStepNumber > 6,
            $"still at step {engine.Tutorial.CurrentStepNumber} on day {engine.State.Day}");
    }
}
