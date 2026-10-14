using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Events
{
    /// <summary>
    /// The game clock's speeds (roadmap D15 / U2-07): 1×, 3×, 10× and 30× on
    /// the keys 1–4. At 1× a sim day lasts one second, so 30× runs a month in
    /// a second and a year in about twelve.
    /// </summary>
    public static class GameSpeed
    {
        /// <summary>The four speeds, slowest first.</summary>
        public static IReadOnlyList<int> Steps { get; } = new[] { 1, 3, 10, 30 };

        /// <summary>Normal speed: one sim day per second.</summary>
        public const int Normal = 1;

        /// <summary>The fastest speed ("Til neste hendelse" runs at it).</summary>
        public const int Fastest = 30;

        /// <summary>Real seconds a sim day lasts at <see cref="Normal"/>.</summary>
        public const double SecondsPerDayAtNormal = 1.0;

        /// <summary>
        /// The step (0-3) nearest to a multiplier, judged by ratio: a 2× from
        /// an old setting lands on 3×, a 5× on 3× and a 20× on 30×.
        /// </summary>
        public static int StepIndex(double multiplier)
        {
            if (double.IsNaN(multiplier) || multiplier <= 0.0)
                return 0;
            int best = 0;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < Steps.Count; i++)
            {
                double distance = Math.Abs(Math.Log(multiplier / Steps[i]));
                if (distance < bestDistance - 1e-9)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>The speed step nearest to a multiplier.</summary>
        public static int Snap(double multiplier) => Steps[StepIndex(multiplier)];

        /// <summary>Real seconds a sim day lasts at a multiplier.</summary>
        public static double SecondsPerDay(double multiplier) =>
            SecondsPerDayAtNormal / Math.Max(multiplier, Normal / 10.0);
    }

    /// <summary>What the clock should do now.</summary>
    public enum PacingAction
    {
        None = 0,
        /// <summary>Drop to 1× (only decided while the clock runs faster).</summary>
        SlowToNormal = 1,
        /// <summary>Stop the clock until the player starts it again.</summary>
        Pause = 2
    }

    /// <summary>Why the clock slowed or stopped (the HUD says it).</summary>
    public enum PacingReason
    {
        None = 0,
        /// <summary>A deadline that can be answered is a week or less away.</summary>
        DeadlineApproaching,
        /// <summary>A deadline is a day or less away and nobody has answered it.</summary>
        DeadlineImminent,
        /// <summary>"Til neste hendelse" reached a warning or a critical event.</summary>
        NextEventReached,
        /// <summary>"Til neste hendelse" ran its longest stretch without one.</summary>
        NextEventQuiet
    }

    /// <summary>
    /// A deadline the clock watches. <see cref="Key"/> identifies what it is
    /// about (an event's dedupe key, or a registrant's own key): registering
    /// the same key again updates it, <see cref="PacingGovernor.Resolve"/>
    /// ends it.
    /// </summary>
    public sealed record PacingDeadline
    {
        public required string Key { get; init; }
        /// <summary>The last sim day to act by.</summary>
        public required int DeadlineDay { get; init; }
        /// <summary>The clock drops to 1× once this many days (or fewer) are left.</summary>
        public int SlowAtDaysLeft { get; init; } = PacingRules.SlowAtDaysLeft;
        /// <summary>The clock stops once this many days (or fewer) are left.</summary>
        public int PauseAtDaysLeft { get; init; } = PacingRules.PauseAtDaysLeft;
        /// <summary>The event that registered it ("" for a direct registration).</summary>
        public string EventId { get; init; } = "";
        /// <summary>The event's type, when an event registered it.</summary>
        public GameEventType? EventType { get; init; }
        /// <summary>A headline of its own ("" = the event type's title).</summary>
        public string Headline { get; init; } = "";
        public string SiteId { get; init; } = "";
        public string UnitId { get; init; } = "";
        /// <summary>What answers it (a catalog counter verb).</summary>
        public string CounterVerb { get; init; } = GameEventCatalog.NoCounterVerb;
    }

    /// <summary>
    /// A pacing decision: the deadline behind it and the days left to it, or
    /// the event a "Til neste hendelse" run stopped at.
    /// </summary>
    public sealed record PacingDecision(PacingAction Action, PacingReason Reason, PacingDeadline? Deadline = null,
        int DaysLeft = -1, GameEvent? Event = null)
    {
        public static PacingDecision None { get; } = new(PacingAction.None, PacingReason.None);
    }

    /// <summary>
    /// The pacing rules. The deadline rule is the farm-types one
    /// (docs/FARM_TYPES_SPEC.md §3.5 and §6.6, consumed by F2-34): a warning
    /// or critical event that names a counter verb and a deadline slows the
    /// clock to 1× at seven days left and stops it at one day left when
    /// nobody has answered it. At 30× with no input that leaves six seconds
    /// of 1× time and then a stop before every such deadline.
    /// </summary>
    public static class PacingRules
    {
        /// <summary>Days left at which a counterable deadline slows the clock to 1×.</summary>
        public const int SlowAtDaysLeft = 7;

        /// <summary>Days left at which an unanswered deadline stops the clock.</summary>
        public const int PauseAtDaysLeft = 1;

        /// <summary>The longest "Til neste hendelse" runs without an event before it stops.</summary>
        public const int NextEventMaxDays = 60;

        /// <summary>True for an event the deadline rule watches.</summary>
        public static bool IsCounterableDeadline(GameEvent ev) =>
            ev.Severity >= GameEventSeverity.Warning
            && ev.DeadlineDay >= 0
            && ev.CounterVerb != GameEventCatalog.NoCounterVerb;

        /// <summary>True for an event that ends "Til neste hendelse": anything the player gets a toast or a card for.</summary>
        public static bool EndsNextEventRun(GameEvent ev) => ev.Severity >= GameEventSeverity.Warning;

        /// <summary>The deadline an event registers (its dedupe key is the key).</summary>
        public static PacingDeadline FromEvent(GameEvent ev) => new()
        {
            Key = ev.Key,
            DeadlineDay = ev.DeadlineDay,
            EventId = ev.Id,
            EventType = ev.Type,
            Headline = ev.Headline,
            SiteId = ev.SiteId,
            UnitId = ev.UnitId,
            CounterVerb = ev.CounterVerb
        };
    }

    /// <summary>
    /// When the clock slows down and stops by itself (roadmap U2-07). It holds
    /// the deadlines registered with it and the "Til neste hendelse" run, and
    /// the clock asks it after every sim day what to do:
    /// <list type="bullet">
    /// <item><b>Deadlines.</b> Every counterable warning or critical event the
    /// bus delivers registers its deadline (<see cref="Observe"/>); anything
    /// else with a deadline (a tutorial step, an auction, a farm-types
    /// decision) registers through <see cref="Register"/>. Each deadline slows
    /// the clock once (at <see cref="PacingDeadline.SlowAtDaysLeft"/>) and
    /// stops it once (at <see cref="PacingDeadline.PauseAtDaysLeft"/>) unless
    /// it was answered first (<see cref="Resolve"/>). Speeding up again after
    /// a slow-down is the player's choice and is not undone; the stop still
    /// comes.</item>
    /// <item><b>"Til neste hendelse".</b> A run (<see cref="StartRunToNextEvent"/>)
    /// ends paused at the first warning or critical event, at a deadline's
    /// slow-down, or after <see cref="PacingRules.NextEventMaxDays"/> quiet
    /// days.</item>
    /// </list>
    /// Nothing is decided for a clock that stands still: a deadline that comes
    /// due while the game is paused acts on the first day the clock runs.
    /// Presentation state only, like the bus: no RNG, no game state, not saved;
    /// a new or loaded game starts it empty.
    /// </summary>
    public sealed class PacingGovernor
    {
        private sealed class Entry
        {
            public PacingDeadline Deadline = null!;
            public bool Slowed;
            public bool Paused;
        }

        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private PacingDecision? _runEnd;

        /// <summary>True while "Til neste hendelse" runs.</summary>
        public bool IsRunningToNextEvent { get; private set; }

        /// <summary>The sim day the current run started on (-1 when none runs).</summary>
        public int RunStartDay { get; private set; } = -1;

        /// <summary>The deadlines being watched, soonest first.</summary>
        public IReadOnlyList<PacingDeadline> Active =>
            Ordered().Select(e => e.Deadline).ToList();

        /// <summary>
        /// Watch a deadline. Registering a key again updates it; a new deadline
        /// day for the key is news, so it may slow and stop the clock again.
        /// Returns the deadline watched, or null when it is not valid (no key,
        /// a negative day, or a stop window wider than the slow window).
        /// </summary>
        public PacingDeadline? Register(PacingDeadline deadline)
        {
            ArgumentNullException.ThrowIfNull(deadline);
            if (string.IsNullOrWhiteSpace(deadline.Key) || deadline.DeadlineDay < 0
                || deadline.PauseAtDaysLeft < 0 || deadline.SlowAtDaysLeft < deadline.PauseAtDaysLeft)
                return null;
            if (_entries.TryGetValue(deadline.Key, out var entry))
            {
                if (entry.Deadline.DeadlineDay != deadline.DeadlineDay)
                {
                    entry.Slowed = false;
                    entry.Paused = false;
                }
                entry.Deadline = deadline;
            }
            else
            {
                _entries[deadline.Key] = new Entry { Deadline = deadline };
            }
            return deadline;
        }

        /// <summary>
        /// An event the bus delivered: a counterable deadline is watched, and a
        /// warning or critical ends a running "Til neste hendelse". Returns the
        /// deadline registered, or null.
        /// </summary>
        public PacingDeadline? Observe(GameEvent ev)
        {
            ArgumentNullException.ThrowIfNull(ev);
            if (IsRunningToNextEvent && _runEnd == null && PacingRules.EndsNextEventRun(ev))
                _runEnd = new PacingDecision(PacingAction.Pause, PacingReason.NextEventReached, Event: ev);
            return PacingRules.IsCounterableDeadline(ev) ? Register(PacingRules.FromEvent(ev)) : null;
        }

        /// <summary>The deadline was answered or its cause is gone: stop watching it.</summary>
        public bool Resolve(string key) => key != null && _entries.Remove(key);

        /// <summary>True while a deadline with this key is watched.</summary>
        public bool IsWatching(string key) => key != null && _entries.ContainsKey(key);

        /// <summary>Start "Til neste hendelse" on <paramref name="today"/>.</summary>
        public void StartRunToNextEvent(int today)
        {
            IsRunningToNextEvent = true;
            RunStartDay = today;
            _runEnd = null;
        }

        /// <summary>The player took the clock back (a speed, a pause): the run ends quietly.</summary>
        public void CancelRunToNextEvent()
        {
            IsRunningToNextEvent = false;
            RunStartDay = -1;
            _runEnd = null;
        }

        /// <summary>
        /// What the clock should do on <paramref name="today"/>, running at
        /// <paramref name="speed"/> (× normal). Each deadline acts once per
        /// threshold; deadlines past their day are dropped. A run that reached
        /// an event ends even while the clock is held (a critical card), so the
        /// game stays paused behind the card.
        /// </summary>
        public PacingDecision Evaluate(int today, double speed, bool clockRunning)
        {
            foreach (var stale in _entries.Where(kv => today > kv.Value.Deadline.DeadlineDay)
                         .Select(kv => kv.Key).ToList())
                _entries.Remove(stale);

            var decision = PacingDecision.None;
            if (_runEnd != null)
            {
                decision = _runEnd;
                CancelRunToNextEvent();
            }
            if (!clockRunning)
                return decision;

            foreach (var entry in Ordered())
            {
                int left = entry.Deadline.DeadlineDay - today;
                var candidate = PacingDecision.None;
                if (!entry.Paused && left <= entry.Deadline.PauseAtDaysLeft)
                {
                    entry.Paused = true;
                    entry.Slowed = true;
                    candidate = new PacingDecision(PacingAction.Pause, PacingReason.DeadlineImminent, entry.Deadline, left);
                }
                else if (!entry.Slowed && left <= entry.Deadline.SlowAtDaysLeft)
                {
                    entry.Slowed = true;
                    if (speed > GameSpeed.Normal || IsRunningToNextEvent)
                        candidate = new PacingDecision(PacingAction.SlowToNormal, PacingReason.DeadlineApproaching, entry.Deadline, left);
                }
                if (candidate.Action > decision.Action)
                    decision = candidate;
            }

            if (IsRunningToNextEvent)
            {
                if (decision.Action != PacingAction.None)
                {
                    decision = decision with { Action = PacingAction.Pause };
                    CancelRunToNextEvent();
                }
                else if (today - RunStartDay >= PacingRules.NextEventMaxDays)
                {
                    decision = new PacingDecision(PacingAction.Pause, PacingReason.NextEventQuiet);
                    CancelRunToNextEvent();
                }
            }
            return decision;
        }

        /// <summary>Forget every deadline and any run (a new or loaded game).</summary>
        public void Clear()
        {
            _entries.Clear();
            CancelRunToNextEvent();
        }

        private IEnumerable<Entry> Ordered() =>
            _entries.Values
                .OrderBy(e => e.Deadline.DeadlineDay)
                .ThenBy(e => e.Deadline.Key, StringComparer.Ordinal);
    }
}
