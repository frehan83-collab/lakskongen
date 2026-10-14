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
/// Roadmap U2-06: the typed event bus. One typed record per event, one
/// channel per severity, dedupe by (category, subject) with escalation, and
/// presentation state only (no RNG, no game state, not saved).
/// </summary>
public class GameEventBusTests
{
    private static GameEventRequest Req(GameEventType type, int day, string site = "",
        GameEventSeverity? severity = null, string variant = "", int deadline = -1) =>
        new(type, day) { SiteId = site, Severity = severity, Variant = variant, DeadlineDay = deadline };

    // ── The catalog (F2-05 builds on it) ──────────────────────────────────

    [Fact]
    public void EveryType_DeclaresSeverityCategoryVerbAndDeadlineRule()
    {
        var types = Enum.GetValues<GameEventType>();
        Assert.Equal(types.Length, GameEventCatalog.All.Count);
        foreach (var type in types)
        {
            var info = GameEventCatalog.Describe(type);
            Assert.Equal(type, info.Type);
            Assert.Matches("^[a-z][a-z0-9_]*$", info.Id);
            Assert.False(string.IsNullOrWhiteSpace(info.Category), $"{type} has no category");
            Assert.True(Enum.IsDefined(info.Severity), $"{type} has no severity");
            Assert.True(Enum.IsDefined(info.Deadline), $"{type} has no deadline rule");
            Assert.Contains(info.CounterVerb, GameEventCatalog.CounterVerbs);
            Assert.True(GameEventCatalog.TryParse(info.Id, out var parsed) && parsed == type);
        }
        Assert.Equal(types.Length, GameEventCatalog.All.Select(i => i.Id).Distinct().Count());
        Assert.False(GameEventCatalog.TryParse("Lus KRITISK", out _));
    }

    [Fact]
    public void Records_CarryTheFarmTypeFields_UnitIdDeadlineDayAndCounterVerb()
    {
        // F2-05 adds values, not shape: the fields exist on the record and the request.
        foreach (var name in new[] { "UnitId", "DeadlineDay", "CounterVerb", "SiteId", "ZoneId", "Severity", "Day", "Type" })
            Assert.NotNull(typeof(GameEvent).GetProperty(name));
        foreach (var name in new[] { "UnitId", "DeadlineDay", "SiteId", "ZoneId", "Severity", "Variant" })
            Assert.NotNull(typeof(GameEventRequest).GetProperty(name));

        var bus = new GameEventBus();
        var ev = bus.Publish(new GameEventRequest(GameEventType.NetsWorn, 4) { UnitId = "land_1" }).Event;
        Assert.Equal("land_1", ev.UnitId);
        Assert.Equal("land_1", ev.Subject);
        Assert.Equal(GameEventCatalog.VerbInspectNets, ev.CounterVerb);
        Assert.Equal(-1, ev.DeadlineDay);
    }

    [Fact]
    public void Deadline_CountsOnlyForTypesWhosePublisherSetsIt()
    {
        var bus = new GameEventBus();
        var window = bus.Publish(Req(GameEventType.VaccineWindowOpen, 280, deadline: 304)).Event;
        var lice = bus.Publish(Req(GameEventType.LiceAboveLimit, 280, deadline: 290)).Event;
        Assert.Equal(304, window.DeadlineDay);
        Assert.Equal(-1, lice.DeadlineDay);
    }

    // ── Dedupe and escalation (acceptance 3) ──────────────────────────────

    [Fact]
    public void ARepeatedCondition_CreatesNoDuplicateWithinThreeDays()
    {
        var bus = new GameEventBus();
        Assert.Equal(GameEventOutcome.Delivered, bus.Publish(Req(GameEventType.LiceRising, 10)).Outcome);
        // Reported again on every refresh and every day for three days.
        for (int day = 10; day <= 13; day++)
        {
            Assert.Equal(GameEventOutcome.Suppressed, bus.Publish(Req(GameEventType.LiceRising, day)).Outcome);
            Assert.Equal(GameEventOutcome.Suppressed, bus.Publish(Req(GameEventType.LiceRising, day)).Outcome);
        }
        Assert.Single(bus.DrainDelivered());
        Assert.Equal(4, bus.Occurrences(GameEventBus.KeyOf("lice", "", "")));
    }

    [Fact]
    public void AConditionThatClearsAndReturnsWithinTheWindow_IsNotNews_ButAfterItIs()
    {
        var bus = new GameEventBus();
        bus.Publish(Req(GameEventType.NetsWorn, 20));
        // Gone on days 21-22, back on day 23: still the same situation.
        Assert.Equal(GameEventOutcome.Suppressed, bus.Publish(Req(GameEventType.NetsWorn, 23)).Outcome);
        // Gone for longer than the window: news again.
        Assert.Equal(GameEventOutcome.Delivered, bus.Publish(Req(GameEventType.NetsWorn, 27)).Outcome);
        Assert.Equal(2, bus.DrainDelivered().Count);
    }

    [Fact]
    public void AStormEpisodeReportedEveryDay_IsOneNotification()
    {
        var bus = new GameEventBus();
        int delivered = 0;
        for (int day = 1; day <= 8; day++)
            if (bus.Publish(Req(GameEventType.StormDamage, day)).Outcome != GameEventOutcome.Suppressed)
                delivered++;
        Assert.Equal(1, delivered);
        Assert.Equal(7, bus.SuppressedCount);
    }

    [Fact]
    public void AMoreSevereRepeat_Escalates_ALessSevereOneDoesNot()
    {
        var bus = new GameEventBus();
        var warn = bus.Publish(Req(GameEventType.LiceRising, 5));
        var crit = bus.Publish(Req(GameEventType.LiceAboveLimit, 6));
        Assert.Equal(GameEventOutcome.Escalated, crit.Outcome);
        Assert.True(crit.Event.IsEscalation);
        Assert.Equal(GameEventSeverity.Critical, crit.Event.Severity);
        Assert.Equal(warn.Event.Key, crit.Event.Key);
        Assert.NotEqual(warn.Event.Id, crit.Event.Id);
        // Back to a warning, then critical again inside the window: told once.
        Assert.Equal(GameEventOutcome.Suppressed, bus.Publish(Req(GameEventType.LiceRising, 7)).Outcome);
        Assert.Equal(GameEventOutcome.Suppressed, bus.Publish(Req(GameEventType.LiceAboveLimit, 8)).Outcome);
        Assert.Equal(2, bus.DrainDelivered().Count);
    }

    [Fact]
    public void ASeverityOverride_EscalatesWithinOneType()
    {
        var bus = new GameEventBus();
        bus.Publish(Req(GameEventType.AlgalBloom, 3, "s1", variant: "algae"));
        var severe = bus.Publish(Req(GameEventType.AlgalBloom, 4, "s1", GameEventSeverity.Critical, "algae"));
        Assert.Equal(GameEventOutcome.Escalated, severe.Outcome);
    }

    [Fact]
    public void DedupeIsPerCategorySubjectAndVariant()
    {
        var bus = new GameEventBus();
        Assert.Equal(GameEventOutcome.Delivered, bus.Publish(Req(GameEventType.AlgalBloom, 3, "s1", variant: "algae")).Outcome);
        Assert.Equal(GameEventOutcome.Delivered, bus.Publish(Req(GameEventType.AlgalBloom, 3, "s2", variant: "algae")).Outcome);
        Assert.Equal(GameEventOutcome.Delivered, bus.Publish(Req(GameEventType.AlgalBloom, 3, "s1", variant: "jellyfish")).Outcome);
        Assert.Equal(GameEventOutcome.Delivered, bus.Publish(Req(GameEventType.OxygenLow, 3, "s1")).Outcome);
        Assert.Equal(GameEventOutcome.Suppressed, bus.Publish(Req(GameEventType.AlgalBloom, 4, "s1", variant: "algae")).Outcome);
        // Two types in one category share a key (the weekly lice reminder and the lice condition).
        Assert.Equal(GameEventOutcome.Delivered, bus.Publish(Req(GameEventType.LiceAboveLimit, 3)).Outcome);
        Assert.Equal(GameEventOutcome.Suppressed, bus.Publish(Req(GameEventType.LiceTreatmentDue, 5)).Outcome);
    }

    // ── Delivery ──────────────────────────────────────────────────────────

    [Fact]
    public void Delivered_EventsDrainInOrder_WithDeterministicIds()
    {
        static List<string> Run()
        {
            var bus = new GameEventBus();
            bus.Publish(Req(GameEventType.StormDamage, 2));
            bus.Publish(Req(GameEventType.OxygenLow, 2, "s1"));
            bus.Publish(Req(GameEventType.OxygenLow, 3, "s1"));
            bus.Publish(Req(GameEventType.WellboatCompleted, 3, "s1", variant: "delouse"));
            return bus.DrainDelivered().Select(e => e.Id).ToList();
        }
        var a = Run();
        Assert.Equal(new[] { "ev1-2-storm_damage", "ev2-2-oxygen_low", "ev3-3-wellboat_completed" }, a);
        Assert.Equal(a, Run());
    }

    [Fact]
    public void Drain_Empties_HistoryKeepsTheLog_AndClearForgetsEverything()
    {
        var bus = new GameEventBus();
        var heard = new List<GameEvent>();
        bus.Delivered += heard.Add;
        bus.Publish(Req(GameEventType.StormDamage, 1));
        Assert.Single(bus.DrainDelivered());
        Assert.Empty(bus.DrainDelivered());
        Assert.Single(bus.History);
        Assert.Single(heard);
        int generation = bus.Generation;
        bus.Clear();
        Assert.Empty(bus.History);
        Assert.Equal(0, bus.SuppressedCount);
        // Listeners tell the old game's events from the new one's by the generation.
        Assert.Equal(generation + 1, bus.Generation);
        Assert.Equal(GameEventOutcome.Delivered, bus.Publish(Req(GameEventType.StormDamage, 1)).Outcome);
    }

    [Fact]
    public void HistoryAndPending_AreBounded()
    {
        var bus = new GameEventBus();
        for (int i = 0; i < GameEventBus.PendingCapacity + 50; i++)
            bus.Publish(Req(GameEventType.StoryEvent, i * 10, variant: $"e{i}"));
        Assert.Equal(GameEventBus.HistoryCapacity, bus.History.Count);
        Assert.Equal(GameEventBus.PendingCapacity, bus.DrainDelivered().Count);
    }

    // ── The engine's side ─────────────────────────────────────────────────

    private static SimulationEngine StockedEngine(int seed)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: seed);
        var zone = new Zone("zone-1");
        engine.State.Zones.Add(zone);
        engine.State.SeaSites.Add(new SeaSite("site-1", zone.Id));
        Assert.True(engine.StockSmolts("site-1", 5_000, "standard").Ok);
        return engine;
    }

    /// <summary>
    /// Storm damage is reported once per run of storm notes (notes within the
    /// dedupe window of each other), dated to its day, to the log: a northern
    /// winter brings a storm episode every week or two, the forecast is the
    /// warning, and what needs an answer after a storm is its own event.
    /// Severe storms must be among them, or the rule is only half tested.
    /// </summary>
    [Fact]
    public void Engine_ReportsStormDamageOncePerRun_ToTheLog()
    {
        int severeChecked = 0, ordinaryChecked = 0;
        for (int seed = 1; seed <= 40 && (severeChecked == 0 || ordinaryChecked == 0); seed++)
        {
            var engine = StockedEngine(seed);
            var notes = new List<DayNote>();
            var events = new List<GameEvent>();
            for (int day = 0; day < 150; day++)
            {
                engine.Advance(1);
                notes.AddRange(engine.DrainDayNotes().Where(n => n.Kind == "storm_impact"));
                events.AddRange(engine.GameEvents.DrainDelivered().Where(e => e.Type == GameEventType.StormDamage));
            }
            if (notes.Count < 2) continue;

            // One event per run of notes (plus an escalation when a run turns Severe).
            int runs = 1;
            for (int i = 1; i < notes.Count; i++)
                if (notes[i].Day - notes[i - 1].Day > GameEventBus.DedupeWindowDays) runs++;
            Assert.Equal(runs, events.Count(e => !e.IsEscalation));
            foreach (var ev in events)
            {
                var note = notes.Last(n => n.Day == ev.Day);
                bool severe = note.EventType == nameof(WeatherState.Severe);
                Assert.Equal(GameEventSeverity.Info, ev.Severity);
                Assert.Equal(note.Message, ev.Message);
                if (severe) severeChecked++; else ordinaryChecked++;
            }
        }
        Assert.True(severeChecked > 0 && ordinaryChecked > 0,
            $"need both kinds of storm (severe {severeChecked}, ordinary {ordinaryChecked})");
    }

    /// <summary>
    /// Fish escaping through storm-torn nets is the classic critical event:
    /// one per escape, about its site, with the event log's text.
    /// </summary>
    [Fact]
    public void Engine_ReportsAnEscapeAsCritical_ForItsSite()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var engine = StockedEngine(seed);
            engine.State.SeaSites[0].NetIntegrityPct = 5.0;
            var escapes = new List<GameEvent>();
            for (int day = 0; day < 150 && escapes.Count == 0; day++)
            {
                engine.Advance(1);
                engine.State.SeaSites[0].NetIntegrityPct = 5.0;
                escapes.AddRange(engine.GameEvents.DrainDelivered().Where(e => e.Type == GameEventType.Escape));
            }
            if (escapes.Count == 0) continue;
            var ev = escapes[0];
            Assert.Equal(GameEventSeverity.Critical, ev.Severity);
            Assert.Equal("site-1", ev.SiteId);
            Assert.Equal(GameEventCatalog.VerbInspectNets, ev.CounterVerb);
            Assert.True(ev.Value > 0, "the event carries the number of fish");
            Assert.Contains(engine.State.EventLog, r => r.Name == "Rømming" && r.Day == ev.Day && r.Description == ev.Message);
            return;
        }
        Assert.Fail("no escape through 5 % nets in 150 days on seeds 1-40");
    }

    [Fact]
    public void Engine_StorytellerEvent_IsTypedByCategoryAndCash_NotByText()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: 5);
        var costly = new EventDefinition
        {
            Id = "ci-cost", Name = "CI-kostnad", Category = EventCategory.Equipment, Weight = 1,
            Apply = (_, rec) => { rec.Description = "Storm og sykdom og rømming"; rec.BalanceChange = -5000m; }
        };
        var benign = new EventDefinition
        {
            Id = "ci-gain", Name = "CI-gevinst", Category = EventCategory.Market, Weight = 1,
            Apply = (_, rec) => { rec.Description = "Storm og sykdom og rømming"; rec.BalanceChange = 1000m; }
        };
        engine.Storyteller.Fire(costly, 3);
        engine.Storyteller.Fire(benign, 3);
        var events = engine.GameEvents.DrainDelivered();
        var a = Assert.Single(events, e => e.Variant == "ci-cost");
        var b = Assert.Single(events, e => e.Variant == "ci-gain");
        Assert.Equal(GameEventType.StoryEvent, a.Type);
        Assert.Equal(GameEventSeverity.Warning, a.Severity);
        Assert.Equal(GameEventSeverity.Info, b.Severity);   // same scary words, no cost: information
        Assert.Equal("CI-kostnad", a.Headline);
    }

    [Fact]
    public void Engine_Reset_EmptiesTheBus()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 3);
        engine.PublishGameEvent(Req(GameEventType.StormDamage, 0));
        Assert.Single(engine.GameEvents.History);
        engine.Initialize(enableEvents: false, seed: 3);
        Assert.Empty(engine.GameEvents.History);
        Assert.Empty(engine.GameEvents.DrainDelivered());
    }

    [Fact]
    public void TheBus_IsNotSaved_AndPublishingNeverMovesAStreamOrTheState()
    {
        Assert.DoesNotContain(typeof(GameState).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.PropertyType == typeof(GameEventBus));

        var quiet = StockedEngine(11);
        var loud = StockedEngine(11);
        for (int day = 0; day < 60; day++)
        {
            quiet.Advance(1);
            loud.Advance(1);
            // The snapshot layer reports conditions on every refresh.
            foreach (var type in Enum.GetValues<GameEventType>())
                loud.PublishGameEvent(Req(type, loud.State.Day, "site-1"));
            loud.GameEvents.DrainDelivered();
        }
        var qa = quiet.Rng.CaptureStates();
        var la = loud.Rng.CaptureStates();
        Assert.Equal(qa.Count, la.Count);
        foreach (var kv in qa)
            Assert.Equal(kv.Value, la[kv.Key]);
        Assert.Equal(Serialized(quiet), Serialized(loud));
    }

    private static string Serialized(SimulationEngine engine)
    {
        engine.PrepareForSave();
        var json = JsonSerializer.Serialize(engine.State, SaveJson.Options);
        return Regex.Replace(json, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "GUID");
    }
}
