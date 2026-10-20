using System;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Runtime cadence persistence (Phase 1 wave 5): a loaded game CONTINUES —
/// interval clocks must not double-fire weekly/quarterly beats, and a
/// mid-storm load must not re-settle the running episode's insurance claim.
/// RNG positions remain a documented gap until the seeded subsystems migrate
/// onto SimRng streams.
/// </summary>
public class RuntimePersistenceTests
{
    private static GameState CloneThroughJson(GameState state)
    {
        var json = JsonSerializer.Serialize(state, SaveJson.Options);
        return JsonSerializer.Deserialize<GameState>(json, SaveJson.Options)
               ?? throw new InvalidOperationException("clone failed");
    }

    private static SimulationEngine ResumeFrom(SimulationEngine source)
    {
        var snapshot = CloneThroughJson(source.State);
        var resumed = new SimulationEngine();
        resumed.FinanceManager = new FinanceManager(snapshot.Balance);
        int? seedArg = snapshot.RandomSeed != 0 ? snapshot.RandomSeed : 1;
        resumed.Initialize(enableEvents: true, seed: seedArg);
        resumed.AdoptSavedState(snapshot);
        return resumed;
    }

    private static int ClockIndexOf(SimulationEngine engine, System.Type systemType)
    {
        for (int i = 0; i < engine.InDayPhasesForTests.Length; i++)
            if (engine.InDayPhasesForTests[i] is ITickClock &&
                engine.InDayPhasesForTests[i].GetType() == systemType)
                return i;
        return -1;
    }

    [Fact]
    public void IntervalClocks_SurviveSaveAndLoad_CadenceContinues()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        engine.Advance(8);
        engine.PrepareForSave();

        Assert.NotNull(engine.State.Runtime);
        Assert.Equal(engine.InDayPhasesForTests.Length, engine.State.Runtime.InDayClocks.Length);

        int susIdx = ClockIndexOf(engine, typeof(SustainabilityTickSystem));
        Assert.True(susIdx >= 0, "SustainabilityTickSystem should own a clock");
        int susBefore = engine.State.Runtime.InDayClocks[susIdx];
        Assert.True(susBefore > 0, "weekly clock should have fired by day 8");

        var resumed = ResumeFrom(engine);

        // Direct check: the restored system clock matches the snapshot —
        // no fresh-0 clock, so the weekly block cannot double-fire.
        int susAfter = ClockIndexOf(resumed, typeof(SustainabilityTickSystem));
        Assert.Equal(susBefore, ((ITickClock)resumed.InDayPhasesForTests[susAfter]).ClockValue);

        // One more day must NOT re-fire the weekly block on an off-cadence
        // day: the clock continues from the saved day, and cadence resumes
        // only at the next multiple-of-7 boundary.
        resumed.Advance(1);
        Assert.Equal(susBefore, ((ITickClock)resumed.InDayPhasesForTests[susAfter]).ClockValue);
    }

    [Fact]
    public void MidStormLoad_DoesNotResettleTheRunningEpisode()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: seed);
            var zone = new LakskongenSim.Environment.Zone("zone-1");
            engine.State.Zones.Add(zone);
            engine.State.SeaSites.Add(new LakskongenSim.Farm.SeaSite("site-1", zone.Id));
            Assert.True(engine.StockSmolts("site-1", 5_000, "standard").Ok);
            Assert.True(engine.SetInsurancePolicy("storm", true, 1.0, 0).Ok);

            bool stormed = false;
            for (int day = 0; day < 120 && !stormed; day++)
            {
                engine.Advance(1);
                stormed = engine.DrainDayNotes().Any(n => n.Kind == "storm_impact");
            }
            if (!stormed) continue;

            int settlementsAtSave = engine.State.EventLog.Count(e => e.Name == "Forsikringsoppgjør");
            engine.PrepareForSave();

            var resumed = ResumeFrom(engine);

            // The edge state was persisted — the next day (still stormy, or
            // tailing off) must not fire a fresh storm_impact note...
            resumed.Advance(1);
            Assert.DoesNotContain(resumed.DrainDayNotes(), n => n.Kind == "storm_impact");

            // ...and must not settle the same episode's insurance again.
            Assert.Equal(settlementsAtSave,
                resumed.State.EventLog.Count(e => e.Name == "Forsikringsoppgjør"));
            return;
        }

        Assert.Fail("No storm episode observed across seeds 1-40 within 120 days");
    }
}