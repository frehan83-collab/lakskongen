using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Events;
using Xunit;

namespace LakskongenSim.Tests.EventBus;

/// <summary>
/// Roadmap F2-05 acceptance 3 (docs/FARM_TYPES_SPEC.md §3.5): every farm-type
/// event type declares its severity, its deadline rule and its counter verb
/// (or "none"), the set is the spec's table with the spec's severities, and
/// the fields the farm types build on (<see cref="GameEvent.UnitId"/>,
/// <see cref="GameEvent.DeadlineDay"/>, <see cref="GameEvent.CounterVerb"/>)
/// reach the bus key, the pacing rule and the notification meter.
/// </summary>
public class FarmTypeEventCatalogTests
{
    private static IReadOnlyList<GameEventType> MarkedTypes() =>
        typeof(GameEventType).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.GetCustomAttribute<FarmTypeEventAttribute>() != null)
            .Select(f => (GameEventType)f.GetValue(null)!)
            .ToList();

    /// <summary>
    /// Reflection over the enum: every type marked [FarmTypeEvent] has a
    /// catalog row declared through the farm-type row (no column defaulted),
    /// with a defined severity and deadline rule and a verb of the catalog's
    /// list; no unmarked type has a farm-type row.
    /// </summary>
    [Fact]
    public void EveryFarmTypeEvent_DeclaresItsSeverityDeadlineRuleAndCounterVerb()
    {
        var marked = MarkedTypes();
        Assert.Equal(32, marked.Count);
        foreach (var type in marked)
        {
            var info = GameEventCatalog.Describe(type);
            Assert.True(info.DeclaredForFarmTypes, $"{type} is not declared through a farm-type row");
            Assert.True(Enum.IsDefined(info.Severity), $"{type}: severity");
            Assert.True(Enum.IsDefined(info.Deadline), $"{type}: deadline rule");
            Assert.Contains(info.CounterVerb, GameEventCatalog.CounterVerbs);
            Assert.Matches("^[a-z][a-z0-9_]*$", info.Id);
            Assert.False(string.IsNullOrWhiteSpace(info.Category));
        }
        var declared = GameEventCatalog.All.Where(i => i.DeclaredForFarmTypes).Select(i => i.Type).ToHashSet();
        Assert.Equal(marked.ToHashSet(), declared);
    }

    /// <summary>
    /// The marked types are the spec's §3.5 table (read from the spec itself)
    /// plus F2-05's own DriftsplanCommandFailed, each with the severity the
    /// spec's row gives it.
    /// </summary>
    [Fact]
    public void TheFarmTypeEvents_AreTheSpecsTable_WithItsSeverities()
    {
        var spec = SpecTable();
        Assert.Equal(31, spec.Count);
        var expected = spec.ToDictionary(kv => Enum.Parse<GameEventType>(kv.Key), kv => kv.Value);
        expected[GameEventType.DriftsplanCommandFailed] = GameEventSeverity.Warning;
        Assert.Equal(expected.Keys.OrderBy(t => t), MarkedTypes().OrderBy(t => t));
        foreach (var (type, severity) in expected)
            Assert.True(GameEventCatalog.Describe(type).Severity == severity, $"{type}: catalog {GameEventCatalog.Describe(type).Severity}, spec {severity}");
    }

    /// <summary>The §3.5 table's rows: event name → severity (an annotation such as "(≥ 4 d ahead)" dropped).</summary>
    private static Dictionary<string, GameEventSeverity> SpecTable()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var lines = File.ReadAllLines(Path.Combine(dir!.FullName, "docs", "FARM_TYPES_SPEC.md"));
        int start = Array.FindIndex(lines, l => l.StartsWith("### 3.5 Events", StringComparison.Ordinal));
        Assert.True(start >= 0, "spec §3.5 not found");
        var table = new Dictionary<string, GameEventSeverity>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(start + 1).TakeWhile(l => !l.StartsWith("### ", StringComparison.Ordinal)))
        {
            var m = Regex.Match(line, @"^\| (Critical|Warning|Info) \| (.+) \|$");
            if (!m.Success) continue;
            var severity = Enum.Parse<GameEventSeverity>(m.Groups[1].Value);
            foreach (var cell in m.Groups[2].Value.Split(','))
                table.Add(Regex.Replace(cell, @"\(.*?\)", "").Trim(), severity);
        }
        return table;
    }

    /// <summary>
    /// A warning or critical whose deadline the publisher sets names a verb,
    /// so the deadline rule (U2-07: a counterable deadline slows the clock at
    /// 7 days and stops it at 1) watches it: each such type, published for a
    /// unit with a deadline, registers that deadline under the event's key,
    /// with the unit and the verb. A type without the rule never carries one.
    /// </summary>
    [Fact]
    public void AFarmTypeDeadline_IsWatchedByThePacingRule_WithItsUnitAndVerb()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        foreach (var type in MarkedTypes())
        {
            var info = GameEventCatalog.Describe(type);
            var ev = engine.PublishGameEvent(new GameEventRequest(type, 10) { UnitId = $"unit_{info.Id}", DeadlineDay = 40 }).Event;
            Assert.Equal(($"unit_{info.Id}", info.CounterVerb), (ev.UnitId, ev.CounterVerb));
            Assert.Equal($"unit_{info.Id}", ev.Subject);
            if (info.Deadline == GameEventDeadlineRule.SetByPublisher)
            {
                Assert.Equal(40, ev.DeadlineDay);
                if (info.Severity >= GameEventSeverity.Warning)
                {
                    Assert.NotEqual(GameEventCatalog.NoCounterVerb, info.CounterVerb);
                    var watched = Assert.Single(engine.Pacing.Active, d => d.Key == ev.Key);
                    Assert.Equal((40, $"unit_{info.Id}", info.CounterVerb), (watched.DeadlineDay, watched.UnitId, watched.CounterVerb));
                }
            }
            else
            {
                Assert.Equal(-1, ev.DeadlineDay);
                Assert.False(engine.Pacing.IsWatching(ev.Key));
            }
        }
        Assert.Contains(MarkedTypes(), t => GameEventCatalog.Describe(t).Deadline == GameEventDeadlineRule.SetByPublisher);
    }

    /// <summary>
    /// The handling ladder (spec §3.3, the AOF lesson) is one story on the bus:
    /// the 21-day warning and the 45-day Mattilsynet order share a category, so
    /// the order escalates the warning for the same unit (delivered again, now
    /// critical) and moves the watched deadline to the order's own; another
    /// unit's warning is its own news.
    /// </summary>
    [Fact]
    public void TheHandlingLadder_EscalatesOnTheBus_AndMovesTheDeadline()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        var warning = engine.PublishGameEvent(new GameEventRequest(GameEventType.HandlingOverdue, 21) { UnitId = "offshore_1", DeadlineDay = 45 });
        Assert.Equal(GameEventOutcome.Delivered, warning.Outcome);
        var repeat = engine.PublishGameEvent(new GameEventRequest(GameEventType.HandlingOverdue, 22) { UnitId = "offshore_1", DeadlineDay = 45 });
        Assert.Equal(GameEventOutcome.Suppressed, repeat.Outcome);
        var order = engine.PublishGameEvent(new GameEventRequest(GameEventType.MattilsynetOrder, 23) { UnitId = "offshore_1", DeadlineDay = 53 });
        Assert.Equal((GameEventOutcome.Escalated, GameEventSeverity.Critical), (order.Outcome, order.Event.Severity));
        Assert.Equal(warning.Event.Key, order.Event.Key);
        Assert.Equal(53, Assert.Single(engine.Pacing.Active, d => d.Key == order.Event.Key).DeadlineDay);
        var other = engine.PublishGameEvent(new GameEventRequest(GameEventType.HandlingOverdue, 23) { UnitId = "offshore_2", DeadlineDay = 47 });
        Assert.Equal(GameEventOutcome.Delivered, other.Outcome);
        Assert.Equal((1, 1), (engine.Notifications.Warnings("offshore_1", 0), engine.Notifications.Criticals("offshore_1", 0)));
        Assert.Equal(1, engine.Notifications.Warnings("offshore_2", 0));
    }

    /// <summary>The farm-type verbs are listed, unique and named like the others.</summary>
    [Fact]
    public void TheFarmTypeVerbs_AreListedOnce()
    {
        var verbs = GameEventCatalog.CounterVerbs;
        Assert.Equal(verbs.Count, verbs.Distinct().Count());
        Assert.All(verbs, v => Assert.Matches("^[a-z][a-z_]*$", v));
        foreach (var info in GameEventCatalog.All.Where(i => i.DeclaredForFarmTypes))
            Assert.Contains(info.CounterVerb, verbs);
        Assert.Contains(GameEventCatalog.VerbOpenDriftsplan, verbs);
    }
}
