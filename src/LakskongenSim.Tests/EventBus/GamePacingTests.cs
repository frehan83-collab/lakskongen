using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.EventBus;

/// <summary>
/// Roadmap U2-07: the clock's speeds and the rules by which it slows down and
/// stops by itself. The deadline rule is the farm-types one (FARM_TYPES_SPEC
/// §3.5/§6.6, F2-34 acceptance 1): a counterable warning with a deadline
/// slows the game to 1× at seven days left and stops it at one day left with
/// no response. "Til neste hendelse" runs until the next warning or critical.
/// </summary>
public class GamePacingTests
{
    private static GameEvent Event(GameEventType type, int day, int deadline, string site = "site-1",
        GameEventSeverity? severity = null)
    {
        var bus = new GameEventBus();
        return bus.Publish(new GameEventRequest(type, day)
        {
            SiteId = site,
            Severity = severity,
            DeadlineDay = deadline
        }).Event;
    }

    /// <summary>A counterable warning with a deadline (the lice-treatment order).</summary>
    private static GameEvent Order(int day, int deadline, string site = "site-1") =>
        Event(GameEventType.LiceTreatmentOrder, day, deadline, site);

    // ── Speeds ─────────────────────────────────────────────────────────────

    [Fact]
    public void Speeds_AreOneThreeTenThirty_AndOldMultipliersSnapToTheNearest()
    {
        Assert.Equal(new[] { 1, 3, 10, 30 }, GameSpeed.Steps);
        Assert.Equal(1, GameSpeed.Normal);
        Assert.Equal(30, GameSpeed.Fastest);
        // Saved defaults from the old 0.5-5× slider and the old 2×/5× buttons.
        Assert.Equal(1, GameSpeed.Snap(0.5));
        Assert.Equal(1, GameSpeed.Snap(1.5));
        Assert.Equal(3, GameSpeed.Snap(2.0));
        Assert.Equal(3, GameSpeed.Snap(5.0));
        Assert.Equal(10, GameSpeed.Snap(7.0));
        Assert.Equal(30, GameSpeed.Snap(20.0));
        Assert.Equal(30, GameSpeed.Snap(100.0));
        Assert.Equal(0, GameSpeed.StepIndex(double.NaN));
        Assert.Equal(1.0, GameSpeed.SecondsPerDay(1), 9);
        Assert.Equal(1.0 / 30.0, GameSpeed.SecondsPerDay(30), 9);
    }

    // ── The deadline rule ─────────────────────────────────────────────────

    [Fact]
    public void ACounterableDeadline_SlowsAtSevenDaysLeft_AndStopsAtOne()
    {
        var pacing = new PacingGovernor();
        var registered = pacing.Observe(Order(day: 10, deadline: 20));
        Assert.NotNull(registered);
        Assert.Equal(20, registered!.DeadlineDay);
        Assert.Equal(GameEventCatalog.VerbDelouse, registered.CounterVerb);

        var actions = new Dictionary<int, PacingDecision>();
        for (int day = 10; day <= 21; day++)
            actions[day] = pacing.Evaluate(day, speed: 30, clockRunning: true);

        Assert.All(new[] { 10, 11, 12 }, d => Assert.Equal(PacingAction.None, actions[d].Action));
        Assert.Equal(PacingAction.SlowToNormal, actions[13].Action);
        Assert.Equal(PacingReason.DeadlineApproaching, actions[13].Reason);
        Assert.Equal(7, actions[13].DaysLeft);
        Assert.All(new[] { 14, 15, 16, 17, 18 }, d => Assert.Equal(PacingAction.None, actions[d].Action));
        Assert.Equal(PacingAction.Pause, actions[19].Action);
        Assert.Equal(PacingReason.DeadlineImminent, actions[19].Reason);
        Assert.Equal(1, actions[19].DaysLeft);
        Assert.Equal(PacingAction.None, actions[20].Action);
        Assert.Empty(pacing.Active);   // past its day: no longer watched
    }

    [Fact]
    public void SpeedingUpAfterTheSlowDown_IsThePlayersChoice_ButTheStopStillComes()
    {
        var pacing = new PacingGovernor();
        pacing.Observe(Order(0, 10));
        Assert.Equal(PacingAction.SlowToNormal, pacing.Evaluate(3, 30, true).Action);
        // The player picks 30× again: no second slow-down for this deadline.
        for (int day = 4; day < 9; day++)
            Assert.Equal(PacingAction.None, pacing.Evaluate(day, 30, true).Action);
        Assert.Equal(PacingAction.Pause, pacing.Evaluate(9, 30, true).Action);
        // Started again on the last day: it does not stop twice.
        Assert.Equal(PacingAction.None, pacing.Evaluate(9, 30, true).Action);
    }

    [Fact]
    public void AnAnsweredDeadline_NeitherSlowsNorStops()
    {
        var pacing = new PacingGovernor();
        var deadline = pacing.Observe(Order(0, 10))!;
        Assert.True(pacing.IsWatching(deadline.Key));
        Assert.True(pacing.Resolve(deadline.Key));
        Assert.False(pacing.IsWatching(deadline.Key));
        for (int day = 0; day <= 10; day++)
            Assert.Equal(PacingAction.None, pacing.Evaluate(day, 30, true).Action);
    }

    [Fact]
    public void OnlyCounterableWarningsAndCriticalsWithADeadline_AreWatched()
    {
        var pacing = new PacingGovernor();
        // Info severity: the log only.
        Assert.Null(pacing.Observe(Event(GameEventType.LiceTreatmentOrder, 0, 5, severity: GameEventSeverity.Info)));
        // A deadline nothing can answer (the vaccine window names no verb).
        Assert.Null(pacing.Observe(Event(GameEventType.VaccineWindowOpen, 0, 5)));
        // A counter verb without a deadline.
        Assert.Null(pacing.Observe(Event(GameEventType.LiceRising, 0, 5)));
        Assert.Empty(pacing.Active);
        // A critical with both is watched.
        Assert.NotNull(pacing.Observe(Event(GameEventType.BiomassOrder, 0, 5, severity: GameEventSeverity.Critical)));
        Assert.Single(pacing.Active);
    }

    [Fact]
    public void AStandingClock_DecidesNothing_AndTheFirstRunningDayActs()
    {
        var pacing = new PacingGovernor();
        pacing.Observe(Order(0, 6));   // already inside the slow window
        Assert.Equal(PacingAction.None, pacing.Evaluate(0, 30, clockRunning: false).Action);
        Assert.Equal(PacingAction.None, pacing.Evaluate(1, 30, clockRunning: false).Action);
        Assert.Equal(PacingAction.SlowToNormal, pacing.Evaluate(2, 30, clockRunning: true).Action);
    }

    [Fact]
    public void AtNormalSpeed_ThereIsNothingToSlow_ButTheStopComes()
    {
        var pacing = new PacingGovernor();
        pacing.Observe(Order(0, 8));
        for (int day = 0; day < 7; day++)
            Assert.Equal(PacingAction.None, pacing.Evaluate(day, 1, true).Action);
        Assert.Equal(PacingAction.Pause, pacing.Evaluate(7, 1, true).Action);
    }

    [Fact]
    public void ANewDeadlineForTheSameKey_IsNews_AndSlowsAndStopsAgain()
    {
        var pacing = new PacingGovernor();
        pacing.Observe(Order(0, 8));
        Assert.Equal(PacingAction.SlowToNormal, pacing.Evaluate(1, 30, true).Action);
        Assert.Equal(PacingAction.Pause, pacing.Evaluate(7, 30, true).Action);
        // The same order reported again with the same day changes nothing ...
        pacing.Observe(Order(7, 8));
        Assert.Equal(PacingAction.None, pacing.Evaluate(7, 30, true).Action);
        // ... a new order with a new deadline acts again.
        pacing.Observe(Order(9, 30));
        Assert.Equal(PacingAction.SlowToNormal, pacing.Evaluate(23, 30, true).Action);
        Assert.Equal(PacingAction.Pause, pacing.Evaluate(29, 30, true).Action);
    }

    [Fact]
    public void TheSoonestDeadline_IsTheOneTheDecisionNames_AndAStopBeatsASlowDown()
    {
        var pacing = new PacingGovernor();
        pacing.Observe(Order(0, 5, "site-a"));    // in the slow window
        pacing.Observe(Order(0, 1, "site-b"));    // in the stop window
        var decision = pacing.Evaluate(0, 30, true);
        Assert.Equal(PacingAction.Pause, decision.Action);
        Assert.Equal("site-b", decision.Deadline!.SiteId);
        Assert.Equal(new[] { "site-b", "site-a" }, pacing.Active.Select(d => d.SiteId));
    }

    [Fact]
    public void DirectRegistrations_AreValidated_AndUseTheirOwnWindows()
    {
        var pacing = new PacingGovernor();
        Assert.Null(pacing.Register(new PacingDeadline { Key = "", DeadlineDay = 5 }));
        Assert.Null(pacing.Register(new PacingDeadline { Key = "a", DeadlineDay = -1 }));
        Assert.Null(pacing.Register(new PacingDeadline { Key = "a", DeadlineDay = 5, SlowAtDaysLeft = 1, PauseAtDaysLeft = 2 }));
        // A tutorial step that waits for the player: stop on the day itself, slow two days before.
        Assert.NotNull(pacing.Register(new PacingDeadline { Key = "tutorial:feed", DeadlineDay = 10, SlowAtDaysLeft = 2, PauseAtDaysLeft = 0 }));
        Assert.Equal(PacingAction.None, pacing.Evaluate(7, 10, true).Action);
        Assert.Equal(PacingAction.SlowToNormal, pacing.Evaluate(8, 10, true).Action);
        Assert.Equal(PacingAction.None, pacing.Evaluate(9, 10, true).Action);
        Assert.Equal(PacingAction.Pause, pacing.Evaluate(10, 10, true).Action);
    }

    // ── F2-34 acceptance 1, measured on the rule ──────────────────────────

    /// <summary>
    /// At 30× with no input, every counterable warning gives at least five
    /// seconds of 1× time or a stop before its deadline, for every lead time
    /// from one day to two months and every arrival day of the week. The
    /// clock is simulated as the bridge drives it: a day passes, the day's
    /// events arrive, then the clock asks the rules.
    /// </summary>
    [Fact]
    public void AtThirtyTimesWithNoInput_EveryCounterableWarning_GetsFiveSecondsAtOneTimesOrAStop()
    {
        for (int lead = 1; lead <= 60; lead++)
        {
            for (int arrival = 1; arrival <= 7; arrival++)
            {
                var pacing = new PacingGovernor();
                double speed = GameSpeed.Fastest;
                double normalSeconds = 0;
                bool stopped = false;
                int deadline = arrival + lead;
                for (int day = 1; day < deadline && !stopped; day++)
                {
                    if (day > 1)
                        normalSeconds += speed == GameSpeed.Normal ? GameSpeed.SecondsPerDay(speed) : 0.0;
                    if (day == arrival)
                        pacing.Observe(Order(day, deadline));
                    var decision = pacing.Evaluate(day, speed, clockRunning: true);
                    if (decision.Action == PacingAction.SlowToNormal)
                        speed = GameSpeed.Normal;
                    else if (decision.Action == PacingAction.Pause)
                        stopped = true;
                }
                // The acceptance: five seconds at 1× or a stop. The rule gives
                // more: it always stops before the deadline, and a warning
                // that arrives before the slow window also gets its seconds.
                Assert.True(stopped || normalSeconds >= 5.0,
                    $"lead {lead} d, arrival day {arrival}: {normalSeconds:F1} s at 1×, stopped {stopped}");
                Assert.True(stopped, $"lead {lead} d, arrival day {arrival}: the clock stops before the deadline");
                if (lead > PacingRules.SlowAtDaysLeft)
                    Assert.True(normalSeconds >= 5.0,
                        $"lead {lead} d, arrival day {arrival}: only {normalSeconds:F1} s at 1× before the stop");
            }
        }
    }

    // ── "Til neste hendelse" ───────────────────────────────────────────────

    [Fact]
    public void NextEvent_RunsPastInformation_AndStopsAtTheNextWarning()
    {
        var pacing = new PacingGovernor();
        pacing.StartRunToNextEvent(5);
        Assert.True(pacing.IsRunningToNextEvent);
        Assert.Equal(5, pacing.RunStartDay);
        pacing.Observe(Event(GameEventType.StormDamage, 6, -1));            // info: the log
        Assert.Equal(PacingAction.None, pacing.Evaluate(6, 30, true).Action);
        var warning = Event(GameEventType.OxygenLow, 9, -1);
        pacing.Observe(warning);
        var stop = pacing.Evaluate(9, 30, true);
        Assert.Equal(PacingAction.Pause, stop.Action);
        Assert.Equal(PacingReason.NextEventReached, stop.Reason);
        Assert.Equal(warning, stop.Event);
        Assert.False(pacing.IsRunningToNextEvent);
        Assert.Equal(PacingAction.None, pacing.Evaluate(10, 30, true).Action);
    }

    [Fact]
    public void NextEvent_StopsAtACriticalEvenBehindItsCard()
    {
        var pacing = new PacingGovernor();
        pacing.StartRunToNextEvent(0);
        pacing.Observe(Event(GameEventType.Escape, 3, -1));
        // The critical card already holds the clock: the run still ends, paused.
        var stop = pacing.Evaluate(3, 30, clockRunning: false);
        Assert.Equal(PacingAction.Pause, stop.Action);
        Assert.Equal(PacingReason.NextEventReached, stop.Reason);
        Assert.False(pacing.IsRunningToNextEvent);
    }

    [Fact]
    public void NextEvent_StopsWhereADeadlineWouldSlowTheClock()
    {
        var pacing = new PacingGovernor();
        pacing.Observe(Order(0, 20));
        pacing.StartRunToNextEvent(1);
        for (int day = 1; day < 13; day++)
            Assert.Equal(PacingAction.None, pacing.Evaluate(day, 30, true).Action);
        var stop = pacing.Evaluate(13, 30, true);
        Assert.Equal(PacingAction.Pause, stop.Action);
        Assert.Equal(PacingReason.DeadlineApproaching, stop.Reason);
        Assert.False(pacing.IsRunningToNextEvent);
    }

    [Fact]
    public void NextEvent_StopsAfterTwoQuietMonths()
    {
        var pacing = new PacingGovernor();
        pacing.StartRunToNextEvent(100);
        for (int day = 100; day < 100 + PacingRules.NextEventMaxDays; day++)
            Assert.Equal(PacingAction.None, pacing.Evaluate(day, 30, true).Action);
        var stop = pacing.Evaluate(100 + PacingRules.NextEventMaxDays, 30, true);
        Assert.Equal(PacingAction.Pause, stop.Action);
        Assert.Equal(PacingReason.NextEventQuiet, stop.Reason);
    }

    [Fact]
    public void NextEvent_EndsQuietlyWhenThePlayerTakesTheClockBack()
    {
        var pacing = new PacingGovernor();
        pacing.StartRunToNextEvent(0);
        pacing.CancelRunToNextEvent();
        pacing.Observe(Event(GameEventType.OxygenLow, 1, -1));
        Assert.Equal(PacingAction.None, pacing.Evaluate(1, 3, true).Action);
        Assert.Equal(-1, pacing.RunStartDay);
    }

    // ── The engine: Mattilsynet's orders reach the clock ──────────────────

    private static SimulationEngine EngineWithSite()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 7);
        var zone = new Zone("zone-1");
        engine.State.Zones.Add(zone);
        engine.State.SeaSites.Add(new SeaSite("site-1", zone.Id));
        return engine;
    }

    [Fact]
    public void Engine_ALiceTreatmentOrder_IsAWarningWithItsDeadline_AndWatchedUntilItIsMet()
    {
        var engine = EngineWithSite();
        var site = engine.State.SeaSites[0];
        site.TreatmentOrderDueDay = engine.State.Day + 14;
        site.LastLiceCount = 0.74;
        engine.DrainDayNotes();
        var ev = Assert.Single(engine.GameEvents.DrainDelivered(), e => e.Type == GameEventType.LiceTreatmentOrder);
        Assert.Equal(GameEventSeverity.Warning, ev.Severity);
        Assert.Equal("site-1", ev.SiteId);
        Assert.Equal(site.TreatmentOrderDueDay, ev.DeadlineDay);
        Assert.Equal(GameEventCatalog.VerbDelouse, ev.CounterVerb);
        Assert.Contains("Avlus før fristen", ev.Message);
        string key = SimulationEngine.StandingOrderKey(GameEventType.LiceTreatmentOrder, "site-1");
        Assert.Equal(ev.Key, key);
        Assert.True(engine.Pacing.IsWatching(key));

        // Reported again tomorrow: one notification, still watched.
        engine.DrainDayNotes();
        Assert.DoesNotContain(engine.GameEvents.DrainDelivered(), e => e.Type == GameEventType.LiceTreatmentOrder);
        Assert.True(engine.Pacing.IsWatching(key));

        // The count is back under the limit: the order is closed and no longer stops the clock.
        site.TreatmentOrderDueDay = -1;
        engine.DrainDayNotes();
        Assert.False(engine.Pacing.IsWatching(key));
    }

    [Fact]
    public void Engine_AnOverMtbOrder_IsWatchedUntilFulfilled()
    {
        var engine = EngineWithSite();
        var site = engine.State.SeaSites[0];
        site.MattilsynetDeadlineDay = engine.State.Day + 14;
        engine.DrainDayNotes();
        var ev = Assert.Single(engine.GameEvents.DrainDelivered(), e => e.Type == GameEventType.BiomassOrder);
        Assert.Equal(site.MattilsynetDeadlineDay, ev.DeadlineDay);
        Assert.Equal(GameEventCatalog.VerbInspectSite, ev.CounterVerb);
        string key = SimulationEngine.StandingOrderKey(GameEventType.BiomassOrder, "site-1");
        Assert.True(engine.Pacing.IsWatching(key));
        site.MattilsynetOrderFulfilled = true;
        engine.DrainDayNotes();
        Assert.False(engine.Pacing.IsWatching(key));
    }

    /// <summary>
    /// The real path: a site over the lice limit on two weekly counts gets
    /// Mattilsynet's order from the engine, the bus delivers it with its
    /// deadline and the clock, driven day by day at 30×, slows and then stops
    /// before the order is enforced.
    /// </summary>
    [Fact]
    public void Engine_ARealLiceOrder_SlowsAndStopsTheClockBeforeItIsEnforced()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: seed);
            var zone = new Zone("zone-1");
            engine.State.Zones.Add(zone);
            engine.State.SeaSites.Add(new SeaSite("site-1", zone.Id));
            Assert.True(engine.StockSmolts("site-1", 20_000, "standard").Ok);
            var site = engine.State.SeaSites[0];
            double speed = GameSpeed.Fastest;
            var decisions = new List<(int Day, PacingDecision Decision)>();
            int orderDeadline = -1;
            for (int day = 0; day < 400 && decisions.All(d => d.Decision.Action != PacingAction.Pause); day++)
            {
                // Keep the lice high so the counts breach the limit.
                site.Lice.AdultFemalePerFish = 2.0;
                engine.Advance(1);
                engine.DrainDayNotes();
                if (site.TreatmentOrderDueDay >= 0)
                    orderDeadline = site.TreatmentOrderDueDay;
                var decision = engine.Pacing.Evaluate(engine.State.Day, speed, clockRunning: true);
                if (decision.Action == PacingAction.SlowToNormal)
                    speed = GameSpeed.Normal;
                if (decision.Action != PacingAction.None)
                    decisions.Add((engine.State.Day, decision));
            }
            if (orderDeadline < 0) continue;
            var slow = decisions.First(d => d.Decision.Action == PacingAction.SlowToNormal);
            var stop = decisions.First(d => d.Decision.Action == PacingAction.Pause);
            Assert.Equal(GameEventType.LiceTreatmentOrder, stop.Decision.Deadline!.EventType);
            Assert.True(orderDeadline - slow.Day <= PacingRules.SlowAtDaysLeft);
            Assert.Equal(orderDeadline - PacingRules.PauseAtDaysLeft, stop.Day);
            Assert.Equal(orderDeadline, site.TreatmentOrderDueDay);   // still standing: not yet enforced
            return;
        }
        Assert.Fail("no Mattilsynet lice order in 400 days on seeds 1-20");
    }

    [Fact]
    public void Engine_ANewGame_EmptiesThePacingRules()
    {
        var engine = EngineWithSite();
        engine.Pacing.Register(new PacingDeadline { Key = "ci", DeadlineDay = 9 });
        engine.Pacing.StartRunToNextEvent(0);
        engine.Initialize(enableEvents: false, seed: 7);
        Assert.Empty(engine.Pacing.Active);
        Assert.False(engine.Pacing.IsRunningToNextEvent);
    }

    [Fact]
    public void Pacing_IsNotSaved_AndNeverMovesAStreamOrTheState()
    {
        Assert.DoesNotContain(typeof(GameState).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.PropertyType == typeof(PacingGovernor));
        foreach (var field in typeof(PacingGovernor).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            Assert.False(field.FieldType == typeof(SimRng) || field.FieldType == typeof(Random), field.Name);

        var quiet = EngineWithSite();
        var paced = EngineWithSite();
        paced.State.SeaSites[0].TreatmentOrderDueDay = 40;
        quiet.State.SeaSites[0].TreatmentOrderDueDay = 40;
        for (int day = 0; day < 60; day++)
        {
            quiet.Advance(1);
            paced.Advance(1);
            quiet.DrainDayNotes();
            paced.DrainDayNotes();
            paced.Pacing.Evaluate(paced.State.Day, 30, true);
            if (day == 5) paced.Pacing.StartRunToNextEvent(paced.State.Day);
        }
        var qa = quiet.Rng.CaptureStates();
        var pa = paced.Rng.CaptureStates();
        foreach (var kv in qa)
            Assert.Equal(kv.Value, pa[kv.Key]);
        Assert.Equal(Serialized(quiet), Serialized(paced));
    }

    private static string Serialized(SimulationEngine engine)
    {
        engine.PrepareForSave();
        var json = JsonSerializer.Serialize(engine.State, SaveJson.Options);
        return Regex.Replace(json, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "GUID");
    }
}
