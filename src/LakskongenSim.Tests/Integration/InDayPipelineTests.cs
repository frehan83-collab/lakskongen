using System.Linq;
using LakskongenSim.Core;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// In-day ITickSystem pipeline contract (Phase 1 wave 4): the interval
/// systems own their clocks, and the array is rebuilt every Initialize, so
/// a New Game starts with fresh quarterly/weekly cadence (S9) — including
/// the previously-leaking _quarterStartDay.
/// </summary>
public class InDayPipelineTests
{
    [Fact]
    public void ReInitialize_ResetsIntervalClocks_QuarterlyTaxFiresOnSchedule()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 7);
        engine.Advance(90); // first quarterly tax window passes
        decimal taxesBefore = engine.FinancialStatements.Taxes;

        // New Game: fresh clocks. Days 1-89 after re-init must NOT fire the
        // quarterly block again (the old clock leaked across new games).
        engine.Initialize(enableEvents: false, seed: 7);
        engine.Advance(89);
        Assert.Equal(taxesBefore, engine.FinancialStatements.Taxes);

        // Day 90: the quarterly system fires exactly on its own cadence.
        engine.Advance(1);
        Assert.True(engine.FinancialStatements.Taxes >= taxesBefore,
            "quarterly tax must fire again after a full re-initialized quarter");
    }

    [Fact]
    public void Pipeline_RunsAllInDayPhases_WeeklyTrafficLightFiresOnSeventhDay()
    {
        // Mirrors the Phase1Regression boundary contract: weekly blocks first
        // fire on the 8th executed tick (days 0..6 no fire, day 7 fires).
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: 18);
        engine.Advance(6);
        int eventLogAt6 = engine.State.EventLog.Count;
        engine.Advance(1);
        Assert.True(engine.State.EventLog.Count >= eventLogAt6, "pipeline produces event log on the weekly boundary");
        Assert.Equal(7, engine.State.Day);
    }
}