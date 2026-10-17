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

    /// <summary>
    /// U2-02 (stronger than "progress after 3 days", RUN_LOG §7): the goal
    /// follows the steps the player completes by play. Three idle days move it
    /// not at all (no step waits for a day count); the steps the player does
    /// move it, step by step; a skip leaves it where play left it.
    /// </summary>
    [Fact]
    public void GoalStatus_TutorialFollowsTheSteps_AndNoScenarioMeansNoGoal()
    {
        var engine = Started("forste_sesong", 6);
        engine.Advance(3);
        Assert.Equal(0.0, engine.GetScenarioGoalStatus()!.Progress01, 9);

        var player = new Tutorial.TutorialPlayer();
        player.DoToday(engine);   // the five day-0 steps: pens, feeding plan, lice order, inbox, clock
        Assert.Equal(5, engine.Tutorial.EarnedCount);
        Assert.Equal(5.0 / engine.Tutorial.TotalSteps, engine.GetScenarioGoalStatus()!.Progress01, 9);
        Assert.Equal(engine.Tutorial.Progress01, engine.GetScenarioGoalStatus()!.Progress01, 9);

        engine.Tutorial.Skip();
        engine.Advance(1);
        Assert.Equal(5.0 / engine.Tutorial.TotalSteps, engine.GetScenarioGoalStatus()!.Progress01, 9);
        Assert.False(engine.GetScenarioGoalStatus()!.Won);

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
    /// A minimal player: it does what each step asks through the step's own
    /// verbs and UI marks (U2-02: steps advance on real events), so it sets a
    /// feeding plan and the lice standing order (delouse above 0.4 adult
    /// females, as this player always did by hand), orders the harvest at
    /// 4.5 kg, restocks, starts the cheapest research once a harvest paid and
    /// borrows the finance panel's 5 MNOK when the second site's cash is short
    /// (<see cref="Tutorial.TutorialPlayer"/>).
    /// </summary>
    private static TutorialRun PlayTutorial(int seed, int horizonDays = 1500)
    {
        var engine = Started("forste_sesong", seed, events: true);
        var player = new Tutorial.TutorialPlayer();
        player.Play(engine, horizonDays);
        return new TutorialRun(engine.Tutorial.IsComplete, engine.Scenario.IsWon, engine.Scenario.IsLost,
            engine.State.Day, engine.Tutorial.CurrentStepNumber, player.MinCash);
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

    /// <summary>
    /// U2-02 (stronger than the day-150 time fallback it replaces, RUN_LOG §7):
    /// the lice step is the "Avlus ved telling" standing order, so it never
    /// waits for lice or for a date. A calm tutorial with no events and no
    /// order stays on the step for 200 days (no fallback opens it); setting the
    /// order completes it on the day it is set, with the lice low.
    /// </summary>
    [Fact]
    public void TutorialLiceStep_IsAStandingOrder_WithNoTimeFallback()
    {
        var engine = Started("forste_sesong", 9, events: false);
        var site = engine.State.SeaSites[0];
        engine.Tutorial.Mark(TutorialMarks.Tab("pens"));
        Assert.True(engine.SetFeedPlan(site.Id, 100.0, LakskongenSim.Farm.FeedRecipeKind.Standard, 30).Ok);
        engine.Tutorial.CheckProgress();
        Assert.Equal("lice_order", engine.Tutorial.CurrentStep?.Id);
        engine.Advance(200);
        Assert.Equal("lice_order", engine.Tutorial.CurrentStep?.Id);

        Assert.True(engine.SetDriftsplan(site.Id, LakskongenSim.Farm.FjordPolicies.DelouseOnCount).Ok);
        engine.Tutorial.CheckProgress();
        Assert.Equal("inbox", engine.Tutorial.CurrentStep?.Id);
        Assert.Equal(engine.State.Day, engine.Tutorial.Steps[2].CompletedOnDay);
    }
}
