using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using LakskongenSim.Events;

// GameBridge.Clock.cs — partial class slice (see project docs).
// <summary>
// The game clock (roadmap U2-07): the four speeds, the player's pause, the
// holds that stop the clock while something waits for the player (the
// briefing card, a critical event's card, the pause menu), "Til neste
// hendelse", and the pacing rules (engine.Pacing) that slow it to 1× and stop
// it before a deadline. One owner for "is the clock running": the auto-tick
// timer runs exactly when a game runs, the player has not paused it and
// nothing holds it. Every change is announced as ClockChanged.
// </summary>
public partial class GameBridge
{
    /// <summary>The clock changed: speed, pause, holds, a run, or a pacing decision. Keys: see <see cref="GetClockState"/>.</summary>
    [Signal] public delegate void ClockChangedEventHandler(Godot.Collections.Dictionary clock);

    /// <summary>Hold reason: the briefing card of a new game.</summary>
    public const string HoldBriefing = "briefing";
    /// <summary>Hold reason: a critical event's card (U2-06).</summary>
    public const string HoldCriticalEvent = "critical_event";
    /// <summary>Hold reason: the pause menu (U1-06).</summary>
    public const string HoldPauseMenu = "pause_menu";

    private readonly SortedSet<string> _clockHolds = new(StringComparer.Ordinal);
    private PacingDecision _lastPacing = PacingDecision.None;
    private int _lastPacingDay = -1;
    private int _speedBeforeRun = GameSpeed.Normal;
    private bool _inTick;

    /// <summary>True from the start of a player's new game until its briefing card is answered.</summary>
    public bool BriefingPending { get; private set; }

    /// <summary>
    /// "Bremse ned før frister" (on by default): the deadline rule may slow and
    /// stop the clock. Off, deadlines are still shown but never touch the clock;
    /// "Til neste hendelse" still stops at events.
    /// </summary>
    public bool AutoSlowDeadlines
    {
        get => SimulationEngine?.Pacing?.DeadlinesSlowTheClock ?? true;
        set
        {
            if (SimulationEngine?.Pacing == null || SimulationEngine.Pacing.DeadlinesSlowTheClock == value) return;
            SimulationEngine.Pacing.DeadlinesSlowTheClock = value;
            EmitClock();
        }
    }

    /// <summary>The clock's speed (1, 3, 10 or 30 × normal).</summary>
    public float TimeSpeed => _timeSpeed;

    /// <summary>Real time the last auto-tick took, all of it: the engine's day, its events and every listener (ms).</summary>
    public double LastTickMs { get; private set; }

    /// <summary>Real time the engine's own day took in the last auto-tick (ms); the rest is the presentation side.</summary>
    public double LastTickEngineMs { get; private set; }

    /// <summary>Auto-ticks since launch (QA: which frames carried a day).</summary>
    public int TickCount { get; private set; }

    // ── The player's controls ─────────────────────────────────────────────

    /// <summary>
    /// Set the speed: snapped to 1×, 3×, 10× or 30× (1× = one sim day per
    /// second). The player taking the speed ends "Til neste hendelse" and
    /// clears a slow-down notice.
    /// </summary>
    public void SetTimeSpeed(float speed)
    {
        SimulationEngine?.Pacing?.CancelRunToNextEvent();
        _lastPacing = PacingDecision.None;
        ApplySpeed(GameSpeed.Snap(speed));
        EmitClock();
    }

    /// <summary>True while the clock stands: the player paused it or something holds it.</summary>
    public bool IsPaused() => _isPaused || _clockHolds.Count > 0;

    /// <summary>True while the player's own pause is on (holds aside).</summary>
    public bool IsPlayerPaused() => _isPaused;

    /// <summary>
    /// The player's pause. Resuming clears a pacing stop's notice; pausing
    /// ends "Til neste hendelse". Holds keep the clock stopped either way.
    /// </summary>
    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        if (paused)
            SimulationEngine?.Pacing?.CancelRunToNextEvent();
        else
            _lastPacing = PacingDecision.None;
        UpdateClockTimer();
        EmitClock();
        GD.Print($"GameBridge: {(IsPaused() ? "paused" : "resumed")}");
    }

    /// <summary>Stop the clock while <paramref name="reason"/> waits for the player (a card, a menu).</summary>
    public void HoldClock(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || !_clockHolds.Add(reason)) return;
        UpdateClockTimer();
        EmitClock();
    }

    /// <summary>Let go of a hold; the clock runs again when nothing else stops it.</summary>
    public void ReleaseClock(string reason)
    {
        if (reason == null || !_clockHolds.Remove(reason)) return;
        UpdateClockTimer();
        EmitClock();
    }

    /// <summary>True while <paramref name="reason"/> holds the clock ("" = anything).</summary>
    public bool IsClockHeld(string reason) =>
        string.IsNullOrEmpty(reason) ? _clockHolds.Count > 0 : _clockHolds.Contains(reason);

    /// <summary>
    /// "Til neste hendelse" (N): run at 30× until the next warning or
    /// critical event, a deadline's slow-down, or 60 quiet days, then stop
    /// at the speed the player had. False when no game runs or something
    /// holds the clock.
    /// </summary>
    public bool RunToNextEvent()
    {
        var engine = SimulationEngine;
        if (!_gameRunning || engine?.State == null || _clockHolds.Count > 0) return false;
        if (!engine.Pacing.IsRunningToNextEvent)
            _speedBeforeRun = GameSpeed.Snap(_timeSpeed);
        engine.Pacing.StartRunToNextEvent(engine.State.Day);
        _lastPacing = PacingDecision.None;
        _isPaused = false;
        ApplySpeed(GameSpeed.Fastest);
        EmitClock();
        return true;
    }

    /// <summary>End "Til neste hendelse" without stopping the clock (back to the speed before it).</summary>
    public void StopRunToNextEvent()
    {
        var pacing = SimulationEngine?.Pacing;
        if (pacing == null || !pacing.IsRunningToNextEvent) return;
        pacing.CancelRunToNextEvent();
        ApplySpeed(_speedBeforeRun);
        EmitClock();
    }

    /// <summary>True while "Til neste hendelse" runs.</summary>
    public bool IsRunningToNextEvent => SimulationEngine?.Pacing?.IsRunningToNextEvent ?? false;

    /// <summary>The briefing's "Start": the clock runs at <paramref name="speed"/>.</summary>
    public void BeginPlay(float speed)
    {
        BriefingPending = false;
        _clockHolds.Remove(HoldBriefing);
        _isPaused = false;
        _lastPacing = PacingDecision.None;
        ApplySpeed(GameSpeed.Snap(speed));
        EmitClock();
    }

    /// <summary>The briefing's "Se deg rundt først": the card goes, the game stays paused.</summary>
    public void DismissBriefing()
    {
        BriefingPending = false;
        _isPaused = true;
        _clockHolds.Remove(HoldBriefing);
        UpdateClockTimer();
        EmitClock();
    }

    // ── The deadline registration API (F2-34 and any other caller) ─────────

    /// <summary>
    /// Watch a deadline: the clock slows to 1× at <paramref name="slowAtDaysLeft"/>
    /// days left and stops at <paramref name="pauseAtDaysLeft"/> unless
    /// <see cref="ResolvePacingDeadline"/> came first. Events the bus delivers
    /// with a counter verb and a deadline are watched without this call.
    /// Returns false when the deadline is not valid.
    /// </summary>
    public bool RegisterPacingDeadline(string key, int deadlineDay, string headline, string siteId,
        string counterVerb, int slowAtDaysLeft, int pauseAtDaysLeft)
    {
        var pacing = SimulationEngine?.Pacing;
        if (pacing == null) return false;
        var registered = pacing.Register(new PacingDeadline
        {
            Key = key ?? "",
            DeadlineDay = deadlineDay,
            Headline = headline ?? "",
            SiteId = siteId ?? "",
            CounterVerb = string.IsNullOrEmpty(counterVerb) ? GameEventCatalog.NoCounterVerb : counterVerb,
            SlowAtDaysLeft = slowAtDaysLeft,
            PauseAtDaysLeft = pauseAtDaysLeft
        });
        if (registered == null) return false;
        if (!_inTick) ApplyPacing();
        EmitClock();
        return true;
    }

    /// <summary>The deadline was answered or its cause is gone.</summary>
    public bool ResolvePacingDeadline(string key)
    {
        bool removed = SimulationEngine?.Pacing?.Resolve(key) ?? false;
        if (removed)
        {
            if (_lastPacing.Deadline?.Key == key)
                _lastPacing = PacingDecision.None;
            EmitClock();
        }
        return removed;
    }

    /// <summary>The watched deadlines, soonest first ({key, deadline_day, days_left, …}).</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetPacingDeadlines()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var engine = SimulationEngine;
        if (engine?.State == null) return list;
        foreach (var d in engine.Pacing.Active)
            list.Add(DeadlineToDictionary(d, engine.State.Day));
        return list;
    }

    /// <summary>
    /// The clock as the HUD draws it. Keys: speed (1|3|10|30), step (0-3),
    /// paused, player_paused, holds (Array), running, next_event (bool),
    /// briefing_pending, auto_slow, day, deadlines (count), next_deadline
    /// (Dictionary, {} when none), pacing ({} or action "slow"|"pause",
    /// reason "deadline_approaching"|"deadline_imminent"|"next_event_reached"|
    /// "next_event_quiet", day, and the deadline's or the event's fields).
    /// </summary>
    public Godot.Collections.Dictionary GetClockState()
    {
        var engine = SimulationEngine;
        int today = engine?.State?.Day ?? 0;
        var holds = new Godot.Collections.Array<string>();
        foreach (var h in _clockHolds) holds.Add(h);
        var active = engine?.Pacing?.Active ?? (IReadOnlyList<PacingDeadline>)Array.Empty<PacingDeadline>();
        return new Godot.Collections.Dictionary
        {
            ["speed"] = GameSpeed.Snap(_timeSpeed),
            ["step"] = GameSpeed.StepIndex(_timeSpeed),
            ["paused"] = IsPaused(),
            ["player_paused"] = _isPaused,
            ["holds"] = holds,
            ["running"] = _gameRunning && !IsPaused(),
            ["next_event"] = IsRunningToNextEvent,
            ["briefing_pending"] = BriefingPending,
            ["auto_slow"] = AutoSlowDeadlines,
            ["day"] = today,
            ["deadlines"] = active.Count,
            ["next_deadline"] = active.Count > 0 ? DeadlineToDictionary(active[0], today) : new Godot.Collections.Dictionary(),
            ["pacing"] = PacingToDictionary(_lastPacing, _lastPacingDay, today)
        };
    }

    // ── Internals ─────────────────────────────────────────────────────────

    /// <summary>
    /// A new game starts at normal speed with no holds; a player's starts
    /// paused on its briefing (its "Start" picks the speed), a QA game keeps
    /// the pause as it was.
    /// </summary>
    private void BeginClockForNewGame(bool briefing)
    {
        _clockHolds.Clear();
        _lastPacing = PacingDecision.None;
        BriefingPending = briefing;
        if (briefing)
        {
            _isPaused = true;
            _clockHolds.Add(HoldBriefing);
        }
        ApplySpeed(GameSpeed.Normal);
        EmitClock();
    }

    /// <summary>A loaded game waits for the player: it opens paused, at normal speed.</summary>
    private void BeginClockForLoadedGame()
    {
        _clockHolds.Clear();
        _lastPacing = PacingDecision.None;
        BriefingPending = false;
        _isPaused = true;
        ApplySpeed(GameSpeed.Normal);
        EmitClock();
    }

    private void ApplySpeed(int speed)
    {
        _timeSpeed = speed;
        _simulationTimer.WaitTime = GameSpeed.SecondsPerDay(speed);
        // A new speed starts a fresh day: a slow-down from 30× must not wait
        // out a 1-second countdown, and a speed-up must not finish one.
        if (!_simulationTimer.IsStopped())
            _simulationTimer.Start();
        UpdateClockTimer();
    }

    private void UpdateClockTimer()
    {
        bool run = _gameRunning && !IsPaused();
        if (run && _simulationTimer.IsStopped())
            _simulationTimer.Start();
        else if (!run && !_simulationTimer.IsStopped())
            _simulationTimer.Stop();
    }

    /// <summary>
    /// Ask the pacing rules what to do today and do it. Called after every
    /// tick (the day's events are in by then) and after events or deadlines
    /// that arrive between ticks.
    /// </summary>
    private void ApplyPacing()
    {
        var engine = SimulationEngine;
        if (engine?.State == null || !_gameRunning) return;
        bool wasRunning = engine.Pacing.IsRunningToNextEvent;
        var decision = engine.Pacing.Evaluate(engine.State.Day, _timeSpeed, !IsPaused());
        if (decision.Action == PacingAction.None) return;

        _lastPacing = decision;
        _lastPacingDay = engine.State.Day;
        if (decision.Action == PacingAction.Pause)
        {
            // A run that ended stops at the speed the player had before it.
            if (wasRunning && !engine.Pacing.IsRunningToNextEvent)
                ApplySpeed(_speedBeforeRun);
            _isPaused = true;
            UpdateClockTimer();
        }
        else
        {
            ApplySpeed(GameSpeed.Normal);
        }
        EmitClock();
    }

    private void EmitClock() => EmitSignal(SignalName.ClockChanged, GetClockState());

    private static Godot.Collections.Dictionary DeadlineToDictionary(PacingDeadline d, int today) => new()
    {
        ["key"] = d.Key,
        ["deadline_day"] = d.DeadlineDay,
        ["days_left"] = d.DeadlineDay - today,
        ["type"] = d.EventType is { } t ? GameEventCatalog.Describe(t).Id : "",
        ["event_id"] = d.EventId,
        ["headline"] = d.Headline,
        ["site_id"] = d.SiteId,
        ["unit_id"] = d.UnitId,
        ["counter_verb"] = d.CounterVerb,
        ["slow_at_days_left"] = d.SlowAtDaysLeft,
        ["pause_at_days_left"] = d.PauseAtDaysLeft
    };

    private static Godot.Collections.Dictionary PacingToDictionary(PacingDecision p, int day, int today)
    {
        var dict = new Godot.Collections.Dictionary();
        if (p.Action == PacingAction.None) return dict;
        dict["action"] = p.Action == PacingAction.Pause ? "pause" : "slow";
        dict["reason"] = p.Reason switch
        {
            PacingReason.DeadlineApproaching => "deadline_approaching",
            PacingReason.DeadlineImminent => "deadline_imminent",
            PacingReason.NextEventReached => "next_event_reached",
            PacingReason.NextEventQuiet => "next_event_quiet",
            _ => ""
        };
        dict["day"] = day;
        if (p.Deadline != null)
        {
            foreach (var kv in DeadlineToDictionary(p.Deadline, today))
                dict[kv.Key] = kv.Value;
        }
        if (p.Event != null)
        {
            dict["type"] = GameEventCatalog.Describe(p.Event.Type).Id;
            dict["event_id"] = p.Event.Id;
            dict["headline"] = p.Event.Headline;
            dict["site_id"] = p.Event.SiteId;
            dict["severity"] = GameEventCatalog.SeverityId(p.Event.Severity);
        }
        return dict;
    }
}
