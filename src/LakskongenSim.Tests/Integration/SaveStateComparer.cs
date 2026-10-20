using System.Text.Json;
using System.Text.RegularExpressions;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Shared helpers for save/load determinism tests: serialize an engine the way
/// a save does, normalize process-random entity GUIDs, and report the first
/// differing JSON path.
/// </summary>
internal static class SaveStateComparer
{
    private static readonly Regex GuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    /// <summary>Serialize exactly what a save writes (PrepareForSave + SaveJson).</summary>
    public static string Serialize(SimulationEngine engine)
    {
        engine.PrepareForSave();
        return JsonSerializer.Serialize(engine.State, SaveJson.Options);
    }

    /// <summary>
    /// Entity identities that are still process-random (FishPopulation.Id and
    /// similar Guid defaults) are replaced by a placeholder; every simulated
    /// value and RNG stream position must still match exactly.
    /// </summary>
    public static string Normalize(string json) => GuidPattern.Replace(json, "GUID");

    /// <summary>Resume a save on a fresh engine through the game's adoption path.</summary>
    public static SimulationEngine Resume(string savedJson, bool events)
    {
        var snapshot = JsonSerializer.Deserialize<GameState>(savedJson, SaveJson.Options)
            ?? throw new System.InvalidOperationException("deserialize failed");
        int? seed = snapshot.RandomSeed != 0 ? snapshot.RandomSeed : null;
        var resumed = new SimulationEngine();
#pragma warning disable CS0618 // GameState.Balance is the save-compat mirror
        resumed.FinanceManager = new Finance.FinanceManager(snapshot.Balance);
#pragma warning restore CS0618
        resumed.Initialize(enableEvents: events, seed: seed);
        resumed.AdoptSavedState(snapshot);
        return resumed;
    }

    /// <summary>First differing JSON path between two serialized states.</summary>
    public static string FirstDifference(string a, string b)
    {
        if (a == b) return "identical";
        using var da = JsonDocument.Parse(a);
        using var db = JsonDocument.Parse(b);
        return Diff(da.RootElement, db.RootElement, "$");
    }

    private static string Diff(JsonElement x, JsonElement y, string path)
    {
        if (x.ValueKind != y.ValueKind)
            return $"{path}: kind {x.ValueKind} vs {y.ValueKind} (a={Clip(x.GetRawText())} b={Clip(y.GetRawText())})";
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
                int i = 0;
                using (var xa = x.EnumerateArray())
                using (var yb = y.EnumerateArray())
                {
                    while (xa.MoveNext() && yb.MoveNext())
                    {
                        var d = Diff(xa.Current, yb.Current, path + $"[{i}]");
                        if (d != "identical") return d;
                        i++;
                    }
                }
                return "identical";
            default:
                return x.GetRawText() == y.GetRawText() ? "identical"
                    : $"{path}: {Clip(x.GetRawText())} vs {Clip(y.GetRawText())}";
        }
    }

    private static string Clip(string s) => s.Length <= 160 ? s : s[..160] + "…";
}
