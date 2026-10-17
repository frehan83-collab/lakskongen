using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Tutorial;

/// <summary>
/// Roadmap U2-02 (learn-by-doing first season) and owner decision D19: the
/// steps advance on real UI or engine events, never on a day count or a
/// placeholder; the tutorial runs only in Første sesong; a skip is no win;
/// the funding step teaches the loan where the minimal player waits for cash.
/// </summary>
public class FirstSeasonTutorialTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    // ── Acceptance 1: no trigger is a day count or a placeholder ───────────

    /// <summary>
    /// Static: every trigger and completion in tutorial_steps.json is a name
    /// from the closed vocabulary (a UI mark or an engine state), none is
    /// empty, none mentions a day; the vocabulary itself reads no day and has
    /// no predicate that is true by itself; the engine loads the same file it
    /// embeds.
    /// </summary>
    [Fact]
    public void NoStepWaitsForADayCountOrAPlaceholder()
    {
        string root = RepoRoot();
        string json = File.ReadAllText(Path.Combine(root, "godot", "data", "tutorial_steps.json"));
        var vocabulary = TutorialConditions.All.ToHashSet(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        int steps = 0;
        foreach (var step in doc.RootElement.EnumerateArray())
        {
            steps++;
            string id = step.GetProperty("id").GetString()!;
            string completion = step.GetProperty("completionCondition").GetString() ?? "";
            Assert.False(string.IsNullOrWhiteSpace(completion), $"{id}: no completion condition (it would complete by itself)");
            Assert.Contains(completion, vocabulary);
            if (step.TryGetProperty("triggerCondition", out var trigger) && !string.IsNullOrEmpty(trigger.GetString()))
                Assert.Contains(trigger.GetString()!, vocabulary);
            foreach (var condition in new[] { completion, step.TryGetProperty("triggerCondition", out var t) ? t.GetString() ?? "" : "" })
            {
                Assert.DoesNotMatch(new Regex(@"\bday\b|day_|_day|>=|<=|[<>]|\d", RegexOptions.IgnoreCase), condition);
            }
        }
        Assert.Equal(18, steps);
        Assert.Equal(steps, TutorialSystem.Template().Count);
        Assert.Equal(steps, TutorialSystem.ParseSteps(json).Count);

        // The vocabulary's source reads no day and holds no constant predicate.
        string conditions = File.ReadAllText(Path.Combine(root, "src", "LakskongenSim", "Core", "TutorialConditions.cs"));
        string system = File.ReadAllText(Path.Combine(root, "src", "LakskongenSim", "Core", "TutorialSystem.cs"));
        foreach (var (name, source) in new[] { ("TutorialConditions.cs", conditions), ("TutorialSystem.cs", system) })
        {
            string code = Regex.Replace(source, @"///.*|//.*", "");
            Assert.DoesNotMatch(new Regex(@"State\.Day\s*[<>=]|\.Day\s*>=|Day\s*-\s*\w+\s*>="), code);
            Assert.DoesNotMatch(new Regex(@"=>\s*true\b|_\s*=>\s*true"), code);
        }
        Assert.DoesNotMatch(new Regex(@"Placeholder|placeholder|LiceStepFallbackDay"), system + conditions);
    }

    /// <summary>An unknown condition, an empty completion or a day count fails the load instead of waiting forever.</summary>
    [Theory]
    [InlineData("day >= 7")]
    [InlineData("game_started")]
    [InlineData("ui:tab:nowhere")]
    [InlineData("")]
    public void AStepWithAnUnknownOrEmptyCondition_FailsToLoad(string condition)
    {
        string json = "[{\"id\":\"x\",\"stepNumber\":1,\"kind\":\"do\",\"pace\":\"hold\",\"completionCondition\":\""
                      + condition + "\",\"highlightTarget\":\"tab:pens\"}]";
        Assert.Throws<InvalidDataException>(() => TutorialSystem.ParseSteps(json));
    }

    /// <summary>
    /// Dynamic: a player who does nothing (events on, a whole year, the clock
    /// running) completes no step: nothing advances by itself.
    /// </summary>
    [Fact]
    public void AnIdlePlayer_CompletesNoStepInAYear()
    {
        var engine = TutorialPlayer.Start("forste_sesong", 4);
        engine.Advance(365);
        Assert.Equal(0, engine.Tutorial.CompletedCount);
        Assert.Equal("open_pens", engine.Tutorial.CurrentStep?.Id);
        Assert.False(engine.Scenario.IsWon);
    }

    /// <summary>A UI event completes only the step that waits for it, and only while it is shown.</summary>
    [Fact]
    public void AMark_CompletesOnlyTheStepThatWaitsForIt()
    {
        var engine = TutorialPlayer.Start("forste_sesong", 4, events: false);
        Assert.False(engine.Tutorial.Mark(TutorialMarks.Inbox));         // step 4's event, shown at step 1
        Assert.False(engine.Tutorial.Mark(TutorialMarks.Tab("map")));
        Assert.Equal(1, engine.Tutorial.CurrentStepNumber);
        Assert.True(engine.Tutorial.Mark(TutorialMarks.Tab("pens")));
        Assert.Equal(2, engine.Tutorial.CurrentStepNumber);
        Assert.False(engine.Tutorial.Mark(TutorialMarks.Tab("pens")));  // a repeat is not step 2's verb
        Assert.Equal(2, engine.Tutorial.CurrentStepNumber);
        // The inbox opened at step 1 was not remembered: step 4 waits for it again.
        new TutorialPlayer().DoToday(engine);
        Assert.Equal(TutorialSystem.Template().Single(s => s.Id == "grow").StepNumber, engine.Tutorial.CurrentStepNumber);
    }

    // ── Acceptance 2 (engine half): the minimal player wins Første sesong ──

    /// <summary>
    /// The minimal player (every step by its verb or mark, the loan where the
    /// cash is short) wins Første sesong on every seed, solvent all the way;
    /// the seeds that waited 600-1233 days for the second site's cash
    /// (docs/BALANCE.md §1e) finish in the first season now.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(13)]
    [InlineData(21)]
    [InlineData(42)]
    public void TheMinimalPlayer_WinsFirstSeason(int seed)
    {
        var engine = TutorialPlayer.Start("forste_sesong", seed);
        var player = new TutorialPlayer();
        player.Play(engine, horizonDays: 730);
        Assert.True(engine.Tutorial.IsEarned, $"seed {seed}: stalled at step {engine.Tutorial.CurrentStepNumber} on day {engine.State.Day}");
        Assert.True(engine.Scenario.IsWon, $"seed {seed}: complete but not won");
        Assert.False(engine.Scenario.IsLost);
        Assert.True(engine.State.Day <= 365, $"seed {seed}: finished on day {engine.State.Day}");
        Assert.True(player.MinCash > 0m, $"seed {seed}: cash fell to {player.MinCash:N0}");
        Assert.Equal(engine.Tutorial.TotalSteps, engine.Tutorial.EarnedCount);
    }

    /// <summary>
    /// The win lands when the last step is done, not at the next day's tick:
    /// the player finishes the season while the clock stands.
    /// </summary>
    [Fact]
    public void TheLastStep_WinsAtOnce()
    {
        var engine = TutorialPlayer.Start("forste_sesong", 9);
        var player = new TutorialPlayer();
        player.Play(engine, horizonDays: 730);
        int day = player.StepDays[engine.Tutorial.TotalSteps];
        Assert.True(engine.Scenario.IsWon);
        Assert.Equal(day, engine.State.Day);   // Play stops on completion: no tick ran after the last step
    }

    // ── The loan, taught where the minimal player waits for cash ───────────

    /// <summary>
    /// The funding step waits for the second site's price plus three months
    /// of its running cost. Short of it, taking the finance panel's loan
    /// completes it at once; with the cash in the bank it completes without
    /// one (the step never forces a loan).
    /// </summary>
    [Fact]
    public void TheFundingStep_TeachesTheLoan_WhereTheCashIsShort()
    {
        var engine = PlayTo("funding", seed: 7);
        var home = engine.FjordSites().First();
        var plan = engine.PlanSiteBuild(home.ZoneId);
        Assert.Equal(plan.CostNok + plan.RunningCostPerMonthNok * TutorialSystem.FundingReserveMonths,
            engine.Tutorial.FundingTargetNok());
        var progress = engine.Tutorial.CurrentProgress()!;
        Assert.Equal("nok", progress.Unit);
        Assert.Equal((double)engine.Tutorial.FundingTargetNok(), progress.Goal, 3);

        if (engine.FinanceManager.Balance >= engine.Tutorial.FundingTargetNok())
        {
            Assert.True(engine.Tutorial.CheckProgress());
            return;
        }
        engine.Tutorial.CheckProgress();
        Assert.Equal("funding", engine.Tutorial.CurrentStep?.Id);
        Assert.True(engine.TakeLoan(TutorialPlayer.LoanNok, TutorialPlayer.LoanTermMonths, 0.0).Ok);
        Assert.True(engine.Tutorial.CheckProgress());
        Assert.Equal("open_map", engine.Tutorial.CurrentStep?.Id);
    }

    [Fact]
    public void WithTheCashInTheBank_TheFundingStepNeedsNoLoan()
    {
        var engine = PlayTo("funding", seed: 7);
        decimal missing = engine.Tutorial.FundingTargetNok() - engine.FinanceManager.Balance;
        if (missing > 0m)
            engine.ReceiveIncome(missing, "test");
        Assert.True(engine.Tutorial.CheckProgress());
        Assert.Empty(engine.State.ActiveLoans);
    }

    // ── Acceptance 3: after a skip the Tutorial win condition is false ─────

    /// <summary>
    /// Skipped on day 0, then every step's verb played to the end (the second
    /// site built, both lice orders set): the tutorial is complete, skipped,
    /// not earned, and Første sesong is not won; the skip survives a save.
    /// </summary>
    [Fact]
    public void AfterASkip_TheTutorialWinConditionIsFalse()
    {
        var engine = TutorialPlayer.Start("forste_sesong", 9);
        engine.Tutorial.Skip();
        Assert.True(engine.Tutorial.IsComplete);
        Assert.True(engine.Tutorial.IsSkipped);
        Assert.False(engine.Tutorial.IsEarned);
        Assert.False(engine.Tutorial.IsActive);

        // The full season by the same verbs as the minimal player, step by step.
        var reference = TutorialPlayer.Start("forste_sesong", 9);
        var player = new TutorialPlayer();
        player.Play(reference, horizonDays: 730);
        Assert.True(reference.Scenario.IsWon, "setup: the same season played wins");
        var site = engine.FjordSites().First();
        Assert.True(engine.SetDriftsplan(site.Id, FjordPolicies.DelouseOnCount).Ok);
        for (int day = 0; day < reference.State.Day + 30; day++)
        {
            if (TutorialPlayer.MeanWeightKg(site) >= TutorialPlayer.OrderAtKg && engine.State.HarvestOrders.Count == 0
                && engine.State.TotalHarvestRevenueNok == 0m)
                Assert.True(engine.OrderHarvest(site.Id).Ok);
            engine.Advance(1);
        }
        Assert.True(engine.TakeLoan(TutorialPlayer.LoanNok, TutorialPlayer.LoanTermMonths, 0.0).Ok);
        Assert.True(engine.BuildSeaSite(site.ZoneId, "andre_anlegg").Ok);
        var second = engine.FjordSites().Skip(1).First();
        Assert.True(engine.StockSmoltsInPen(second.Id, second.PhysicalPens[0].Id, TutorialPlayer.StockBatch).Ok);
        Assert.True(engine.SetDriftsplan(second.Id, FjordPolicies.DelouseOnCount).Ok);
        Assert.False(engine.Tutorial.Mark(TutorialMarks.Tab("pens")));
        Assert.False(engine.Tutorial.CheckProgress());
        engine.Advance(5);
        Assert.False(engine.Scenario.IsWon);
        Assert.Equal(0, engine.Tutorial.EarnedCount);
        Assert.Equal(0.0, engine.GetScenarioGoalStatus()!.Progress01, 9);

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        Assert.True(resumed.Tutorial.IsSkipped);
        Assert.True(resumed.Tutorial.IsComplete);
        Assert.False(resumed.Tutorial.IsEarned);
        resumed.Advance(2);
        Assert.False(resumed.Scenario.IsWon);
    }

    // ── Acceptance 4 / D19: no tutorial and no gate outside Første sesong ──

    /// <summary>
    /// po3_red_sone, ISA and the algae scenario (and every other non-tutorial
    /// scenario) have every tab open on day 0; the tutorial neither runs nor
    /// takes marks there, and a season of the minimal player's verbs
    /// completes no step.
    /// </summary>
    [Theory]
    [InlineData("po3_red_sone")]
    [InlineData("isa_utbrudd")]
    [InlineData("algekatastrofe")]
    [InlineData("lofoten_eventyr")]
    [InlineData("ressursrente")]
    public void OutsideFirstSeason_EveryTabIsOpen_AndTheTutorialDoesNotRun(string scenarioId)
    {
        var probe = TutorialPlayer.NewEngine(3, 10_000_000m, events: false);
        var def = probe.Scenario.GetScenario(scenarioId)!;
        var engine = TutorialPlayer.NewEngine(3, def.StartingCapitalNOK, events: true);
        engine.Scenario.Unlock(scenarioId);
        Assert.True(engine.StartScenario(scenarioId).Ok);

        Assert.False(engine.Tutorial.InTutorialScenario);
        Assert.False(engine.Tutorial.IsActive);
        var gated = TutorialSystem.Template().SelectMany(s => s.Unlocks).Distinct().ToList();
        Assert.NotEmpty(gated);
        Assert.All(gated, tab => Assert.True(engine.Tutorial.IsTabUnlocked(tab), $"{scenarioId}: '{tab}' locked on day 0"));
        Assert.False(engine.Tutorial.Mark(TutorialMarks.Tab("pens")));
        var site = engine.FjordSites().First();
        Assert.True(engine.SetFeedPlan(site.Id, 100.0, FeedRecipeKind.Standard, 30).Ok);
        Assert.False(engine.Tutorial.CheckProgress());
        engine.Advance(120);
        Assert.Equal(0, engine.Tutorial.CompletedCount);
        Assert.Empty(engine.State.CompletedTutorialStepIds);
    }

    /// <summary>In Første sesong a gated tab opens at the step that teaches it, and every tab is open once the season is done.</summary>
    [Fact]
    public void InFirstSeason_ATabOpensAtTheStepThatTeachesIt()
    {
        var engine = TutorialPlayer.Start("forste_sesong", 9);
        var steps = TutorialSystem.Template();
        int researchStep = steps.Single(s => s.Unlocks.Contains("research")).StepNumber;
        Assert.False(engine.Tutorial.IsTabUnlocked("research"));
        Assert.True(engine.Tutorial.IsTabUnlocked("pens"));    // never gated
        Assert.True(engine.Tutorial.IsTabUnlocked("map"));
        var atResearch = PlayTo("research", seed: 9);
        Assert.Equal(researchStep, atResearch.Tutorial.CurrentStepNumber);
        Assert.True(atResearch.Tutorial.IsTabUnlocked("research"));
        Assert.False(atResearch.Tutorial.IsTabUnlocked("sustainability"));
        new TutorialPlayer().Play(engine, 730);
        Assert.True(engine.Tutorial.IsEarned);
        Assert.All(steps.SelectMany(s => s.Unlocks), tab => Assert.True(engine.Tutorial.IsTabUnlocked(tab)));
    }

    // ── Saves ───────────────────────────────────────────────────────────────

    /// <summary>A save mid-season resumes on the same step with the same steps done, and plays on to the win.</summary>
    [Fact]
    public void ASaveMidSeason_ResumesTheStep()
    {
        var engine = TutorialPlayer.Start("forste_sesong", 4);
        var player = new TutorialPlayer();
        player.DoToday(engine);
        engine.Advance(30);
        Assert.Equal("grow", engine.Tutorial.CurrentStep?.Id);
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        Assert.Equal("grow", resumed.Tutorial.CurrentStep?.Id);
        Assert.Equal(5, resumed.Tutorial.EarnedCount);
        Assert.True(resumed.Tutorial.IsActive);
        new TutorialPlayer().Play(resumed, 730);
        Assert.True(resumed.Scenario.IsWon);
    }

    /// <summary>A save from the old eighteen-step track keeps its count of finished steps on this track.</summary>
    [Fact]
    public void ASaveFromTheOldTrack_KeepsItsCountOfFinishedSteps()
    {
        var engine = TutorialPlayer.Start("forste_sesong", 4);
        var json = SaveStateComparer.Serialize(engine);
        var snapshot = JsonSerializer.Deserialize<GameState>(json, SaveJson.Options)!;
        snapshot.TutorialCurrentStepIndex = 4;
        snapshot.CompletedTutorialStepIds.Clear();
        foreach (var old in new[] { "welcome", "setup-sea-site", "stock-smolts", "manage-feed" })
            snapshot.CompletedTutorialStepIds.Add(old);
        var resumed = SaveStateComparer.Resume(JsonSerializer.Serialize(snapshot, SaveJson.Options), events: false);
        Assert.Equal(5, resumed.Tutorial.CurrentStepNumber);
        Assert.Equal(4, resumed.Tutorial.EarnedCount);
        Assert.Equal(TutorialSystem.Template().Take(4).Select(s => s.Id).OrderBy(i => i),
            resumed.State.CompletedTutorialStepIds.OrderBy(i => i));
    }

    // ── What the card reads ─────────────────────────────────────────────────

    /// <summary>
    /// Every step names a translation key for its title, text and action, a
    /// kind, a pace and (for a "do" step) a control to highlight; the growth
    /// step reports the mean weight against 4.0 kg.
    /// </summary>
    [Fact]
    public void EveryStep_HasItsTextKeysPaceAndTarget_AndTheGrowthStepShowsProgress()
    {
        foreach (var step in TutorialSystem.Template())
        {
            Assert.Equal("TUTORIAL_" + step.Id.ToUpperInvariant() + "_TITLE", step.Title);
            Assert.Contains(step.Kind, TutorialStepKind.All);
            Assert.Contains(step.Pace, TutorialPace.All);
            Assert.False(string.IsNullOrEmpty(step.HighlightTarget), $"{step.Id}: no highlight target");
            if (step.Kind == TutorialStepKind.Wait)
                Assert.NotEqual(TutorialPace.Hold, step.Pace);   // a wait step needs the clock
        }
        var engine = TutorialPlayer.Start("forste_sesong", 4, events: false);
        new TutorialPlayer().DoToday(engine);
        Assert.Equal("grow", engine.Tutorial.CurrentStep?.Id);
        var progress = engine.Tutorial.CurrentProgress()!;
        Assert.Equal("kg", progress.Unit);
        Assert.Equal(TutorialSystem.HarvestTeachingWeightKg, progress.Goal);
        Assert.InRange(progress.Value, 0.3, TutorialSystem.HarvestTeachingWeightKg);
    }

    private static SimulationEngine PlayTo(string stepId, int seed)
    {
        var engine = TutorialPlayer.Start("forste_sesong", seed);
        var player = new TutorialPlayer();
        for (int day = 0; day < 730; day++)
        {
            for (int guard = 0; guard < 32 && engine.Tutorial.CurrentStep?.Id != stepId && engine.Tutorial.IsActive; guard++)
            {
                int before = engine.Tutorial.CurrentStepNumber;
                // One step at a time so the loop stops on the step asked for.
                var single = new TutorialPlayer();
                single.DoOne(engine);
                if (engine.Tutorial.CurrentStepNumber == before) break;
            }
            if (engine.Tutorial.CurrentStep?.Id == stepId) return engine;
            engine.Advance(1);
        }
        throw new InvalidOperationException($"seed {seed}: never reached '{stepId}'");
    }
}
