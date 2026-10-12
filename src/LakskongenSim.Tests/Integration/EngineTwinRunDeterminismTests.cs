using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Golden determinism gate for the SimRng migration: two freshly-initialized
/// engines with the same seed must reach BIT-IDENTICAL state after the same
/// number of days — including RNG stream positions. Any leftover unseeded
/// draw (System.Random, wall-clock, Guid-into-state) in the tick path fails
/// this test. Extends ExactSaveHashTests (which covers save/load continuity)
/// with fresh-run reproducibility.
/// </summary>
public class EngineTwinRunDeterminismTests
{
    private static SimulationEngine BuildEngine(int seed, bool events)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: events, seed: seed);
        var scenario = engine.Scenario.GetScenario("po3_red_sone");
        scenario!.ApplyStartingState(engine.State, engine);
        engine.State.RandomSeed = seed;
        return engine;
    }

    private static string Serialized(SimulationEngine engine)
    {
        engine.PrepareForSave();
        var json = JsonSerializer.Serialize(engine.State, SaveJson.Options);
        // Entity identities (FishPopulation.Id, job/contract Guids) are
        // process-random by design and excluded from determinism: what must
        // be identical is every simulated VALUE plus stream positions
        // (asserted separately above).
        return System.Text.RegularExpressions.Regex.Replace(
            json,
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            "GUID");
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(13, true)]
    [InlineData(7, false)]
    public void TwinRuns_SameSeed_ProduceIdenticalStateAndStreams(int seed, bool events)
    {
        var a = BuildEngine(seed, events);
        var b = BuildEngine(seed, events);
        a.Advance(90);
        b.Advance(90);

        var statesA = a.Rng.CaptureStates();
        var statesB = b.Rng.CaptureStates();
        Assert.Equal(statesA.Count, statesB.Count);
        foreach (var kvp in statesA)
        {
            Assert.True(statesB.TryGetValue(kvp.Key, out var posB),
                $"stream '{kvp.Key}' missing in twin run");
            Assert.Equal(kvp.Value, posB);
        }

        var sa = Serialized(a);
        var sb = Serialized(b);
        Assert.True(sa == sb, $"twin runs diverged at: {FirstDifference(sa, sb)}");
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

    [Fact]
    public void EngineInitialize_BindsAllNamedStreams()
    {
        // Guards against silently dropping a subsystem binding: every
        // engine-owned named stream must exist right after Initialize.
        // Key format is scope + "\u0001" + suffixes (see SimRng.Stream).
        var engine = BuildEngine(7, events: true);
        var states = engine.Rng.CaptureStates();
        string[] required =
        {
            "weather\u0001",
            "market\u0001",
            "intelligence\u0001",
            "intelligence\u0001market_inner",
            "market\u0001contracts",
            "growth\u0001",
            "genetics\u0001",
            "storyteller\u0001",
            "storyteller\u0001engine",
            "competitor_ai\u0001",
            "stock_market\u0001",
            "currency\u0001",
            "crisis_capitalism\u0001",
            "factions\u0001",
            "plankton_bloom\u0001",
            "stochastic_disease\u0001",
            "stochastic_disease\u0001lod",
            "acquisition\u0001",
            "ai_feeding\u0001",
        };
        foreach (var key in required)
            Assert.True(states.ContainsKey(key), $"named stream missing: '{key}'");
    }
}
