using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// P0-07 (review RECHECK §2 low): <c>Initialize(seed)</c> never recorded
/// <see cref="GameState.RandomSeed"/>. Every caller had to remember to set it,
/// and a game started without a seed saved 0, so its load fell back to a
/// fresh unseeded engine and diverged from the run it was saved from.
/// </summary>
public class InitializeSeedTests
{
    [Fact]
    public void Initialize_RecordsTheSeed_OnTheState()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 4711);
        Assert.Equal(4711, engine.State.RandomSeed);
        Assert.Equal(4711, engine.Rng.Seed);

        // A second Initialize (New Game on the same engine) records its own seed.
        engine.Initialize(enableEvents: false, seed: -12);
        Assert.Equal(-12, engine.State.RandomSeed);
    }

    [Fact]
    public void UnseededGame_RecordsTheSeedItRolled_AndASaveResumesExactly()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true);
        Assert.True(engine.StartScenario("po3_red_sone").Ok);
        Assert.NotEqual(0, engine.State.RandomSeed);
        Assert.Equal(engine.Rng.Seed, engine.State.RandomSeed);
        engine.Advance(40);

        // Nobody set RandomSeed by hand: the save alone must carry the game on.
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        engine.Advance(40);
        resumed.Advance(40);

        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        Assert.True(a == b, "resumed unseeded game diverged at " + SaveStateComparer.FirstDifference(a, b));
    }

    [Fact]
    public void LegacySaveWithoutASeed_IsResavedWithTheSeedItNowRunsOn()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 5);
        engine.Advance(5);
        var legacy = JsonSerializer.Deserialize<GameState>(SaveStateComparer.Serialize(engine), SaveJson.Options)!;
        legacy.RandomSeed = 0; // saves written before the seed was persisted

        var loaded = new SimulationEngine();
        loaded.Initialize(enableEvents: false, seed: null);
        loaded.AdoptSavedState(legacy);

        Assert.NotEqual(0, loaded.State.RandomSeed);
        Assert.Equal(loaded.Rng.Seed, loaded.State.RandomSeed);
    }
}
