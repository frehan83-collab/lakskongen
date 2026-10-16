using System;
using System.Linq;
using System.Reflection;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Tests.Integration;
using StateHashProbe;
using Xunit;

namespace LakskongenSim.Tests.EventBus;

/// <summary>
/// Roadmap F2-05 acceptance 4 (docs/FARM_TYPES_SPEC.md §6.6, measured in
/// F2-17): the notification meter counts the warnings and criticals the
/// player was told about, per unit and sim month, and is excluded from the
/// save (and so from the save hash).
/// </summary>
public class NotificationMeterTests
{
    private static SimulationEngine Engine()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        return engine;
    }

    private static GameEventPublishResult Publish(SimulationEngine engine, GameEventType type, int day,
        string unit = "", string site = "", string zone = "", GameEventSeverity? severity = null, string variant = "")
        => engine.PublishGameEvent(new GameEventRequest(type, day)
        {
            UnitId = unit, SiteId = site, ZoneId = zone, Severity = severity, Variant = variant
        });

    /// <summary>
    /// Warnings and criticals are counted by unit (the event's unit, else its
    /// site; company and zone events under "") and by sim month (31 January is
    /// day 30, 1 February day 31); information is not counted.
    /// </summary>
    [Fact]
    public void CountsWarningsAndCriticals_PerUnitPerMonth_AndNotInformation()
    {
        var engine = Engine();
        Publish(engine, GameEventType.SiloLow, 2, unit: "offshore_1");                    // warning
        Publish(engine, GameEventType.CrewOverdue, 30, unit: "offshore_1");               // warning, 31 Jan
        Publish(engine, GameEventType.TiltEvent, 31, unit: "offshore_1");                 // critical, 1 Feb
        Publish(engine, GameEventType.PermitDecision, 5, unit: "offshore_1");             // info
        Publish(engine, GameEventType.ConstructionPhaseComplete, 6, unit: "offshore_1");  // info
        Publish(engine, GameEventType.NetsWorn, 3, site: "po3_nord");                     // warning, a fjord site
        Publish(engine, GameEventType.LandO2Low, 4, unit: "ras_1", site: "po3_nord");     // the unit wins over the site
        Publish(engine, GameEventType.TrafficLightRed, 5, zone: "po3_sor");               // a zone: the company
        Publish(engine, GameEventType.SolvencyBankWarning, 6);                            // the company, critical

        var meter = engine.Notifications;
        Assert.Equal((2, 0), (meter.Warnings("offshore_1", 0), meter.Criticals("offshore_1", 0)));
        Assert.Equal((0, 1), (meter.Warnings("offshore_1", 1), meter.Criticals("offshore_1", 1)));
        Assert.Equal(1, meter.Warnings("po3_nord", 0));
        Assert.Equal(1, meter.Warnings("ras_1", 0));
        Assert.Equal((1, 1), (meter.Warnings(NotificationMeter.Company, 0), meter.Criticals(NotificationMeter.Company, 0)));
        Assert.Equal(7, meter.Total);
        Assert.Equal(new[] { ("", 0), ("offshore_1", 0), ("offshore_1", 1), ("po3_nord", 0), ("ras_1", 0) },
            meter.Months.Select(m => (m.UnitId, m.Month)));
        var february = meter.Months.Single(m => m.UnitId == "offshore_1" && m.Month == 1);
        Assert.Equal((2027, 2, 1), (february.Year, february.MonthOfYear, february.ByType["tilt_event"]));
        Assert.Equal(new[] { "crew_overdue", "silo_low" }, meter.Months.Single(m => m.UnitId == "offshore_1" && m.Month == 0).ByType.Keys);
        Assert.Equal(1, meter.CriticalsInYear("offshore_1", 0));
        Assert.Equal(0, meter.CriticalsInYear("offshore_1", 1));
        Assert.Equal(0, NotificationMeter.MonthOf(30));
        Assert.Equal(1, NotificationMeter.MonthOf(31));
        Assert.Equal(12, NotificationMeter.MonthOf(365));
    }

    /// <summary>
    /// What the player was told: a repeat the bus absorbs is not counted, an
    /// escalation is (the card the player sees again), and a report after the
    /// dedupe window is news again.
    /// </summary>
    [Fact]
    public void CountsWhatTheBusDelivered_EscalationsIn_RepeatsOut()
    {
        var engine = Engine();
        for (int day = 40; day < 50; day++)
            Publish(engine, GameEventType.H2sChronic, day, unit: "ras_1");                // one condition, held ten days
        Publish(engine, GameEventType.H2sAcute, 50, unit: "ras_1");                       // escalates it
        Publish(engine, GameEventType.H2sChronic, 60, unit: "ras_1");                     // ten quiet days later (1 March): news
        var meter = engine.Notifications;
        Assert.Equal((1, 1), (meter.Warnings("ras_1", 1), meter.Criticals("ras_1", 1)));
        Assert.Equal((1, 0), (meter.Warnings("ras_1", 2), meter.Criticals("ras_1", 2)));
        Assert.Equal(3, meter.Total);
        Assert.True(engine.GameEvents.SuppressedCount >= 9);
    }

    /// <summary>
    /// The §6.6 budget: more than 2 warnings for a unit in a month, or more
    /// than 1 critical in a year, is over it; company-wide events are no
    /// unit's; a year's criticals do not run into the next year.
    /// </summary>
    [Fact]
    public void TheBudget_FlagsAUnitOverTwoWarningsAMonth_OrOneCriticalAYear()
    {
        Assert.Equal(2, NotificationMeter.WarningBudgetPerUnitMonth);
        Assert.Equal(1, NotificationMeter.CriticalBudgetPerUnitYear);
        var engine = Engine();
        Publish(engine, GameEventType.SiloLow, 1, unit: "a");
        Publish(engine, GameEventType.CrewOverdue, 2, unit: "a");
        Assert.Empty(engine.Notifications.OverBudget());
        Publish(engine, GameEventType.LoxShortage, 3, unit: "a");
        var warning = Assert.Single(engine.Notifications.OverBudget());
        Assert.Equal(("a", GameEventSeverity.Warning, 0, 3, 2), (warning.UnitId, warning.Severity, warning.Period, warning.Count, warning.Budget));

        Publish(engine, GameEventType.NetTear, 100, unit: "b");
        Publish(engine, GameEventType.MooringFailure, 400, unit: "b");                  // the next year
        Assert.DoesNotContain(engine.Notifications.OverBudget(), b => b.UnitId == "b");
        Publish(engine, GameEventType.TiltEvent, 200, unit: "b");
        var critical = Assert.Single(engine.Notifications.OverBudget(), b => b.UnitId == "b");
        Assert.Equal((GameEventSeverity.Critical, 0, 2, 1), (critical.Severity, critical.Period, critical.Count, critical.Budget));

        for (int i = 0; i < 5; i++)
            Publish(engine, GameEventType.SolvencyBankWarning, 10 + 10 * i, variant: $"rung{i}");
        Assert.DoesNotContain(engine.Notifications.OverBudget(), b => b.UnitId == NotificationMeter.Company);
    }

    /// <summary>
    /// Excluded from the save hash: the meter lives on the engine beside the
    /// bus, no saved type holds it, and a game whose meter counted hundreds of
    /// notifications saves to the same bytes, and runs the same game, as one
    /// whose meter counted none.
    /// </summary>
    [Fact]
    public void TheMeter_IsNotSaved_AndCountingChangesNoStreamAndNoSave()
    {
        Assert.DoesNotContain(typeof(GameState).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.PropertyType == typeof(NotificationMeter));
        Assert.DoesNotContain(typeof(EngineRuntimeSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.PropertyType == typeof(NotificationMeter));

        var quiet = Game(5);
        var loud = Game(5);
        for (int day = 0; day < 90; day++)
        {
            foreach (var type in new[] { GameEventType.SiloLow, GameEventType.TiltEvent, GameEventType.LandO2Low, GameEventType.HandlingOverdue })
                foreach (var unit in new[] { "po3_nord", "po3_sor", "offshore_9" })
                    Publish(loud, type, loud.State.Day, unit: unit, variant: day.ToString(System.Globalization.CultureInfo.InvariantCulture));
            quiet.Advance(1);
            loud.Advance(1);
            Assert.Equal(GoldenTraceState.Capture(quiet).DayHash(), GoldenTraceState.Capture(loud).DayHash());
        }
        Assert.True(loud.Notifications.Total >= 1000, $"only {loud.Notifications.Total} counted");
        Assert.NotEmpty(loud.Notifications.OverBudget());
        Assert.Equal(quiet.Rng.CaptureStates(), loud.Rng.CaptureStates());
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(quiet)),
            SaveStateComparer.Normalize(SaveStateComparer.Serialize(loud)));
    }

    /// <summary>A new or loaded game empties the meter with the bus.</summary>
    [Fact]
    public void ANewOrLoadedGame_EmptiesTheMeter()
    {
        var engine = Game(6);
        Publish(engine, GameEventType.SiloLow, 0, unit: "offshore_1");
        Assert.Equal(1, engine.Notifications.Total);
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        Assert.Equal(0, resumed.Notifications.Total);
        engine.Initialize(enableEvents: true, seed: 6);
        Assert.Equal(0, engine.Notifications.Total);
        Assert.Empty(engine.Notifications.Months);
    }

    private static SimulationEngine Game(int seed)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: seed);
        engine.State.RandomSeed = seed;
        Assert.True(engine.StartScenario(PlayerScriptProbe.CareerScenarioId).Ok);
        return engine;
    }
}
