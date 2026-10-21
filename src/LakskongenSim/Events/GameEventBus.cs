using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Events
{
    /// <summary>
    /// One event as the player is told about it (roadmap U2-06). Every field
    /// is typed: nothing downstream reads <see cref="Message"/> to decide what
    /// the event is. <see cref="UnitId"/>, <see cref="DeadlineDay"/> and
    /// <see cref="CounterVerb"/> are the fields the farm-types work (F2-05)
    /// builds on, present from the start so later items add values, not shape.
    /// </summary>
    public sealed record GameEvent
    {
        /// <summary>Deterministic id: sequence, day and type ("ev12-40-lice_above_limit").</summary>
        public required string Id { get; init; }
        public required GameEventType Type { get; init; }
        public required GameEventSeverity Severity { get; init; }
        /// <summary>Dedupe category from the catalog ("lice", "storm", …).</summary>
        public required string Category { get; init; }
        /// <summary>The sim day the event happened on.</summary>
        public required int Day { get; init; }
        /// <summary>Sea site the event is about ("" = the company).</summary>
        public string SiteId { get; init; } = "";
        /// <summary>Zone / production area the event is about ("" = none).</summary>
        public string ZoneId { get; init; } = "";
        /// <summary>F2 unit (land facility, offshore structure) the event is about ("" = none).</summary>
        public string UnitId { get; init; } = "";
        /// <summary>What kind within the type (a bloom's kind, a storyteller event's id); part of the dedupe key.</summary>
        public string Variant { get; init; } = "";
        /// <summary>A headline of its own (a storyteller event's name); "" = the type's title.</summary>
        public string Headline { get; init; } = "";
        /// <summary>The text shown to the player (already in the player's language).</summary>
        public string Message { get; init; } = "";
        /// <summary>Last day to answer the event by, -1 when it has none.</summary>
        public int DeadlineDay { get; init; } = -1;
        /// <summary>What answers the event (<see cref="GameEventCatalog.NoCounterVerb"/> when nothing).</summary>
        public string CounterVerb { get; init; } = GameEventCatalog.NoCounterVerb;
        /// <summary>A number the event carries (a severity, an amount, days left).</summary>
        public double Value { get; init; }
        /// <summary>True when this event raised the severity of one already reported.</summary>
        public bool IsEscalation { get; init; }

        /// <summary>What the event is about: the unit, else the site, else the zone.</summary>
        public string Subject => UnitId.Length > 0 ? UnitId : SiteId.Length > 0 ? SiteId : ZoneId;

        /// <summary>The dedupe key: category, subject and variant.</summary>
        public string Key => GameEventBus.KeyOf(Category, Subject, Variant);
    }

    /// <summary>A report to the bus. Severity defaults to the type's; the deadline only counts for types whose rule lets the publisher set it.</summary>
    public sealed record GameEventRequest(GameEventType Type, int Day)
    {
        public GameEventSeverity? Severity { get; init; }
        public string SiteId { get; init; } = "";
        public string ZoneId { get; init; } = "";
        public string UnitId { get; init; } = "";
        public string Variant { get; init; } = "";
        public string Headline { get; init; } = "";
        public string Message { get; init; } = "";
        public int DeadlineDay { get; init; } = -1;
        public double Value { get; init; }
    }

    /// <summary>What the bus did with a report.</summary>
    public enum GameEventOutcome
    {
        /// <summary>New: the player is told.</summary>
        Delivered,
        /// <summary>Already known, but now more severe: the player is told again.</summary>
        Escalated,
        /// <summary>A repeat inside the dedupe window: counted, not told.</summary>
        Suppressed
    }

    /// <summary>The outcome and the event (for a suppressed report, the one it repeated).</summary>
    public sealed record GameEventPublishResult(GameEventOutcome Outcome, GameEvent Event);

    /// <summary>
    /// The typed event bus (roadmap U2-06). Publishers report what happened or
    /// what holds; the bus decides whether the player hears about it:
    /// <list type="bullet">
    /// <item>Reports are deduplicated by (category, subject, variant). A report
    /// within <see cref="DedupeWindowDays"/> days of the last one with the same
    /// key is a repeat: it is counted and dropped, and it keeps the key alive,
    /// so a condition reported every day stays one notification for as long
    /// as it holds, and one that clears and returns within the window is not
    /// news either.</item>
    /// <item>A repeat that is more severe than anything delivered for its key is
    /// an escalation and is delivered (a lice warning turning critical).</item>
    /// </list>
    /// Presentation state only: the bus never draws from an RNG stream, never
    /// changes the game state and is not saved; a new or loaded game starts it
    /// empty, so the conditions that hold are reported afresh.
    /// </summary>
    public sealed class GameEventBus
    {
        /// <summary>A repeat this many days (or fewer) after the last report of its key is not delivered.</summary>
        public const int DedupeWindowDays = 3;

        /// <summary>Delivered events kept for the log (oldest dropped first).</summary>
        public const int HistoryCapacity = 200;

        /// <summary>Delivered events waiting to be drained (oldest dropped first when nobody drains).</summary>
        public const int PendingCapacity = 256;

        /// <summary>Keys tracked before stale ones (outside the window) are pruned.</summary>
        private const int KeyPruneThreshold = 512;

        private sealed class KeyState
        {
            public int LastSeenDay;
            public int Occurrences;
            public GameEventSeverity HighestDelivered;
            public GameEvent LastDelivered = null!;
        }

        private readonly Dictionary<string, KeyState> _keys = new(StringComparer.Ordinal);
        private readonly List<GameEvent> _pending = new();
        private readonly List<GameEvent> _history = new();
        private int _sequence;

        /// <summary>Raised for every delivered or escalated event, as it happens.</summary>
        public event Action<GameEvent>? Delivered;

        /// <summary>Raised when the bus is emptied (a new or loaded game), so
        /// state kept beside it (the pacing deadlines, U2-07) goes with it.</summary>
        public event Action? Cleared;

        /// <summary>Reports absorbed as repeats since the bus was last cleared.</summary>
        public int SuppressedCount { get; private set; }

        /// <summary>Counts the clears (a new or loaded game): a listener that sees
        /// it change knows the events it holds belong to the previous game.</summary>
        public int Generation { get; private set; }

        /// <summary>Delivered events, oldest first (at most <see cref="HistoryCapacity"/>).</summary>
        public IReadOnlyList<GameEvent> History => _history;

        /// <summary>The dedupe key of a category, subject and variant.</summary>
        public static string KeyOf(string category, string subject, string variant) =>
            $"{category}|{subject}|{variant}";

        /// <summary>Report an event. See the class summary for the rules.</summary>
        public GameEventPublishResult Publish(GameEventRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var info = GameEventCatalog.Describe(request.Type);
            var severity = request.Severity ?? info.Severity;
            var candidate = new GameEvent
            {
                Id = "",
                Type = request.Type,
                Severity = severity,
                Category = info.Category,
                Day = request.Day,
                SiteId = request.SiteId ?? "",
                ZoneId = request.ZoneId ?? "",
                UnitId = request.UnitId ?? "",
                Variant = request.Variant ?? "",
                Headline = request.Headline ?? "",
                Message = request.Message ?? "",
                DeadlineDay = info.Deadline == GameEventDeadlineRule.SetByPublisher ? request.DeadlineDay : -1,
                CounterVerb = info.CounterVerb,
                Value = request.Value
            };

            string key = candidate.Key;
            if (_keys.TryGetValue(key, out var state) && Math.Abs(request.Day - state.LastSeenDay) <= DedupeWindowDays)
            {
                if (request.Day > state.LastSeenDay)
                {
                    state.LastSeenDay = request.Day;
                    state.Occurrences++;
                }
                if (severity <= state.HighestDelivered)
                {
                    SuppressedCount++;
                    return new GameEventPublishResult(GameEventOutcome.Suppressed, state.LastDelivered);
                }
                var escalated = Deliver(candidate with { IsEscalation = true });
                state.HighestDelivered = severity;
                state.LastDelivered = escalated;
                return new GameEventPublishResult(GameEventOutcome.Escalated, escalated);
            }

            PruneStaleKeys(request.Day);
            var delivered = Deliver(candidate);
            _keys[key] = new KeyState
            {
                LastSeenDay = request.Day,
                Occurrences = 1,
                HighestDelivered = severity,
                LastDelivered = delivered
            };
            return new GameEventPublishResult(GameEventOutcome.Delivered, delivered);
        }

        /// <summary>
        /// The last day each live key was reported (read-only; keys pruned
        /// out of the dedupe window are gone). A condition the game keeps
        /// reporting (a bloom, lice over the limit) stays current; a one-off
        /// event ages. The inbox reads it to tell what needs the player now
        /// from history (SHIP_PLAN cycle 2: alarm fatigue).
        /// </summary>
        public IReadOnlyDictionary<string, int> LastSeenDays()
            => _keys.ToDictionary(kv => kv.Key, kv => kv.Value.LastSeenDay, StringComparer.Ordinal);

        /// <summary>Days with a report of this key in its current run (0 when unknown).</summary>
        public int Occurrences(string key) => _keys.TryGetValue(key, out var s) ? s.Occurrences : 0;

        /// <summary>Delivered events since the last drain, in the order they were delivered.</summary>
        public IReadOnlyList<GameEvent> DrainDelivered()
        {
            var drained = _pending.ToList();
            _pending.Clear();
            return drained;
        }

        /// <summary>Forget everything (a new or loaded game).</summary>
        public void Clear()
        {
            _keys.Clear();
            _pending.Clear();
            _history.Clear();
            _sequence = 0;
            SuppressedCount = 0;
            Generation++;
            Cleared?.Invoke();
        }

        private GameEvent Deliver(GameEvent candidate)
        {
            _sequence++;
            var ev = candidate with
            {
                Id = $"ev{_sequence}-{candidate.Day}-{GameEventCatalog.Describe(candidate.Type).Id}"
            };
            Append(_pending, ev, PendingCapacity);
            Append(_history, ev, HistoryCapacity);
            Delivered?.Invoke(ev);
            return ev;
        }

        private static void Append(List<GameEvent> list, GameEvent ev, int capacity)
        {
            list.Add(ev);
            if (list.Count > capacity)
                list.RemoveRange(0, list.Count - capacity);
        }

        private void PruneStaleKeys(int day)
        {
            if (_keys.Count < KeyPruneThreshold) return;
            foreach (var stale in _keys.Where(kv => Math.Abs(day - kv.Value.LastSeenDay) > DedupeWindowDays)
                         .Select(kv => kv.Key).ToList())
                _keys.Remove(stale);
        }
    }
}
