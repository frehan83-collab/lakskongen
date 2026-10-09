using System;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// THE plan exit gate: save at day N, load, then advance M days — the loaded
/// run must serialize to the EXACT same state as the uninterrupted run.
/// Enabled by Phase 1 wave 6 (SimRandom positions + SimRng stream positions +
/// runtime cadence + stateful sub-models all persisted).
/// </summary>
public class ExactSaveHashTests
{
    private static SimulationEngine BuildEngine(int seed, bool events)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: events, seed: seed);
        var scenario = engine.Scenario.GetScenario("po3_red_sone");
        scenario!.ApplyStartingState(engine.State, engine);
        // Mirror GameBridge.StartNewGame: the live game always stores its
        // seed. Without this, Resume() falls back to an unseeded engine and
        // the formerly-unseeded subsystems diverge by construction.
        engine.State.RandomSeed = seed;
        return engine;
    }

    private static string Serialized(SimulationEngine engine)
    {
        engine.PrepareForSave();
        return JsonSerializer.Serialize(engine.State, SaveJson.Options);
    }

    private static string FirstDifference(string a, string b)
    {
        if (a == b) return "identical";
        using var da = JsonDocument.Parse(a);
        using var db = JsonDocument.Parse(b);
        string Diff(JsonElement x, JsonElement y, string path)
        {
            if (x.ValueKind != y.ValueKind)
                return $"{path}: kind {x.ValueKind} vs {y.ValueKind} (a={x.GetRawText()} b={y.GetRawText()})";
            switch (x.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in x.EnumerateObject())
                    {
                        if (path == "$" && p.Name is "Balance" or "Runtime")
                            continue; // instrument: report the NEXT divergence
                        if (!y.TryGetProperty(p.Name, out var other))
                            return $"{path}/{p.Name}: missing in b";
                        var d = Diff(p.Value, other, path + "/" + p.Name);
                        if (d != "identical") return d;
                    }
                    foreach (var p in y.EnumerateObject())
                        if (!x.TryGetProperty(p.Name, out _))
                            return $"{path}/{p.Name}: missing in a";
                    return "identical";
                case JsonValueKind.Array:
                    if (x.GetArrayLength() != y.GetArrayLength())
                        return $"{path}: length {x.GetArrayLength()} vs {y.GetArrayLength()}";
                    var xa = x.EnumerateArray();
                    var yb = y.EnumerateArray();
                    int i = 0;
                    while (xa.MoveNext() && yb.MoveNext())
                    {
                        var d = Diff(xa.Current, yb.Current, path + $"[{i}]");
                        if (d != "identical") return d;
                        i++;
                    }
                    return "identical";
                default:
                    return x.GetRawText() == y.GetRawText() ? "identical"
                        : $"{path}: {x.GetRawText()} vs {y.GetRawText()}";
            }
        }
        return Diff(da.RootElement, db.RootElement, "$");
    }

    private static SimulationEngine Resume(SimulationEngine source, bool events)
    {
        string json = Serialized(source);
        var snapshot = JsonSerializer.Deserialize<GameState>(json, SaveJson.Options)
            ?? throw new InvalidOperationException("deserialize failed");
        int? seed = snapshot.RandomSeed != 0 ? snapshot.RandomSeed : null;
        var resumed = new SimulationEngine();
        resumed.FinanceManager = new LakskongenSim.Finance.FinanceManager(snapshot.Balance);
        resumed.Initialize(enableEvents: events, seed: seed);
        resumed.AdoptSavedState(snapshot);
        return resumed;
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(13, false)]
    public void SaveLoadMidRun_ProducesBitIdenticalContinuingRun(int seed, bool events)
    {
        var uninterrupted = BuildEngine(seed, events);
        uninterrupted.Advance(120);

        // Detach via the serializer, resume on a fresh engine — the exact
        // game path (AdoptSavedState), nothing else.
        var resumed = Resume(uninterrupted, events);

        // Both advance the remaining window.
        uninterrupted.Advance(60);
        resumed.Advance(60);

        var a = Serialized(uninterrupted);
        var b = Serialized(resumed);
        Assert.True(a == b, $"first diff: {FirstDifference(a, b)}");
    }

    [Fact]
    public void SaveHashHolds_WithEventsOn_WhenStorytellerStatePersists()
    {
        // The strongest claim: events ON — Storyteller draws, RNG positions,
        // currency noise, price council history and clocks must ALL continue
        // bit-identically across the load.
        var uninterrupted = BuildEngine(7, events: true);
        uninterrupted.Advance(90);
        var resumed = Resume(uninterrupted, events: true);
        uninterrupted.Advance(45);
        resumed.Advance(45);
        var a = Serialized(uninterrupted);
        var b = Serialized(resumed);
        Assert.True(a == b, $"first diff: {FirstDifference(a, b)}");
    }
}