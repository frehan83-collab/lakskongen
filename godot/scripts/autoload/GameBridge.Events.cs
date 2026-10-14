using Godot;
using LakskongenSim.Events;

// GameBridge.Events.cs — partial class slice (see project docs).
// <summary>
// The typed event bus (roadmap U2-06): the GameEventRaised signal that carries
// every event the engine's bus delivers to the presentation layer, the entry
// the snapshot layer (SimState) reports conditions through, and read access
// to the delivered log and the type catalog. The bus decides what is news
// (dedupe by category and subject, escalation); the UI routes by severity.
// </summary>
public partial class GameBridge
{
    /// <summary>
    /// An event the player is told about (delivered or escalated by the bus).
    /// Keys: id, type, severity ("info" | "warning" | "critical"), category,
    /// key, day, site_id, zone_id, unit_id, variant, headline, message,
    /// deadline_day (-1 none), counter_verb ("none" when nothing answers it),
    /// value, escalation, generation (changes with every new or loaded game).
    /// </summary>
    [Signal] public delegate void GameEventRaisedEventHandler(Godot.Collections.Dictionary gameEvent);

    /// <summary>Emit every event the bus delivered since the last call, in order.</summary>
    private void ProjectGameEvents()
    {
        var bus = SimulationEngine?.GameEvents;
        if (bus == null) return;
        foreach (var ev in bus.DrainDelivered())
            EmitSignal(SignalName.GameEventRaised, ToDictionary(ev, bus.Generation));
    }

    /// <summary>The bus's generation: it changes when a new or loaded game empties it.</summary>
    public int GameEventGeneration => SimulationEngine?.GameEvents?.Generation ?? 0;

    /// <summary>
    /// A condition the snapshot layer sees (SimState reports each one while it
    /// holds). <paramref name="typeId"/> is a catalog id ("lice_above_limit"),
    /// <paramref name="severity"/> "" (the type's own), "info", "warning" or
    /// "critical". Returns {outcome: "delivered" | "escalated" | "suppressed" |
    /// "rejected", id, reason}; a delivered event is emitted before this returns.
    /// </summary>
    public Godot.Collections.Dictionary PublishConditionEvent(string typeId, string severity, string siteId,
        string message, string variant, int deadlineDay)
    {
        var result = new Godot.Collections.Dictionary { ["outcome"] = "rejected", ["id"] = "", ["reason"] = "" };
        if (SimulationEngine?.State == null)
        {
            result["reason"] = "engine_not_ready";
            return result;
        }
        if (!GameEventCatalog.TryParse(typeId, out var type))
        {
            result["reason"] = "unknown_type";
            return result;
        }
        GameEventSeverity? level = null;
        if (!string.IsNullOrEmpty(severity))
        {
            if (!GameEventCatalog.TryParseSeverity(severity, out var parsed))
            {
                result["reason"] = "unknown_severity";
                return result;
            }
            level = parsed;
        }
        var outcome = SimulationEngine.PublishGameEvent(new GameEventRequest(type, SimulationEngine.State.Day)
        {
            Severity = level,
            SiteId = siteId ?? "",
            Variant = variant ?? "",
            Message = message ?? "",
            DeadlineDay = deadlineDay
        });
        result["outcome"] = outcome.Outcome switch
        {
            GameEventOutcome.Delivered => "delivered",
            GameEventOutcome.Escalated => "escalated",
            _ => "suppressed"
        };
        result["id"] = outcome.Event.Id;
        ProjectGameEvents();
        return result;
    }

    /// <summary>Delivered events, newest first (at most <paramref name="maxCount"/>).</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGameEventLog(int maxCount)
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var bus = SimulationEngine?.GameEvents;
        if (bus == null) return list;
        for (int i = bus.History.Count - 1; i >= 0 && list.Count < maxCount; i--)
            list.Add(ToDictionary(bus.History[i], bus.Generation));
        return list;
    }

    /// <summary>Repeats the bus absorbed since the game started (QA and tests).</summary>
    public int GameEventsSuppressed => SimulationEngine?.GameEvents?.SuppressedCount ?? 0;

    /// <summary>The type catalog: {id, category, severity, counter_verb, deadline} per type.</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGameEventCatalog()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var info in GameEventCatalog.All)
        {
            list.Add(new Godot.Collections.Dictionary
            {
                ["id"] = info.Id,
                ["category"] = info.Category,
                ["severity"] = GameEventCatalog.SeverityId(info.Severity),
                ["counter_verb"] = info.CounterVerb,
                ["deadline"] = info.Deadline == GameEventDeadlineRule.SetByPublisher ? "set_by_publisher" : "none"
            });
        }
        return list;
    }

    private static Godot.Collections.Dictionary ToDictionary(GameEvent ev, int generation) => new()
    {
        ["generation"] = generation,
        ["id"] = ev.Id,
        ["type"] = GameEventCatalog.Describe(ev.Type).Id,
        ["severity"] = GameEventCatalog.SeverityId(ev.Severity),
        ["category"] = ev.Category,
        ["key"] = ev.Key,
        ["day"] = ev.Day,
        ["site_id"] = ev.SiteId,
        ["zone_id"] = ev.ZoneId,
        ["unit_id"] = ev.UnitId,
        ["variant"] = ev.Variant,
        ["headline"] = ev.Headline,
        ["message"] = ev.Message,
        ["deadline_day"] = ev.DeadlineDay,
        ["counter_verb"] = ev.CounterVerb,
        ["value"] = ev.Value,
        ["escalation"] = ev.IsEscalation
    };
}
