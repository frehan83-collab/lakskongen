using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StateHashProbe;

/// <summary>One difference between an expected and an actual golden trace.</summary>
/// <param name="Day">Game day (-1 for the header).</param>
/// <param name="Group">The group, or "rng", "ev", "cmd", "st", "header", "length", "save".</param>
/// <param name="Field">The entity (and at a snapshot the field) that differs.</param>
public sealed record TraceDifference(string Scenario, int Seed, int Day, string Group, string Field, string Expected, string Actual)
{
    public override string ToString()
        => $"scenario {Scenario}, seed {Seed}, day {Day.ToString(CultureInfo.InvariantCulture)}, field {Group} › {Field}: expected {Expected}, actual {Actual}";
}

/// <summary>The comparison of one scenario × seed trace.</summary>
public sealed class TraceComparison
{
    public string Scenario { get; init; } = "";
    public int Seed { get; init; }

    /// <summary>The first difference in the day lines (or the header): the divergence's day and field.</summary>
    public TraceDifference? First { get; set; }

    /// <summary>The first snapshot difference in the traced values, with the exact field and both values.</summary>
    public TraceDifference? FirstAtSnapshot { get; set; }

    /// <summary>
    /// The first snapshot whose whole save differs (its digest tree's changed
    /// leaves; added and removed nodes are listed apart). Reported, never part
    /// of <see cref="Identical"/>: an item that only adds save fields (F2-03's
    /// plots and kinds) drifts the save without changing the game.
    /// </summary>
    public TraceDifference? FirstSaveDrift { get; set; }

    /// <summary>Snapshots whose save hash differs.</summary>
    public int SaveDriftSnapshots { get; set; }

    /// <summary>The game is the same: every day line and every snapshot's traced values match.</summary>
    public bool Identical => First == null && FirstAtSnapshot == null;
    public int DaysCompared { get; set; }
    public int DivergentDays { get; set; }

    /// <summary>Divergent days per group (a day counts in every group that differs on it).</summary>
    public SortedDictionary<string, int> DivergentDaysByGroup { get; } = new(StringComparer.Ordinal);

    /// <summary>Last snapshot's cash and live biomass, expected and actual ("" when absent).</summary>
    public (string Expected, string Actual) FinalCash { get; set; } = ("", "");
    public (string Expected, string Actual) FinalBiomassKg { get; set; } = ("", "");

    /// <summary>A failure message: what, where, how much.</summary>
    public string Summary()
    {
        string drift = FirstSaveDrift == null ? ""
            : $"\n  save drift in {SaveDriftSnapshots} snapshots, first {FirstSaveDrift}";
        if (Identical) return $"{Scenario} seed {Seed}: identical over {DaysCompared} days" + drift;
        var sb = new StringBuilder();
        sb.Append("golden trace mismatch: ");
        if (First != null)
        {
            sb.Append(First);
            sb.Append($"\n  {DivergentDays} of {DaysCompared} days differ");
            if (DivergentDaysByGroup.Count > 0)
                sb.Append(" (").Append(string.Join(", ", DivergentDaysByGroup.Select(kv => $"{kv.Key} {kv.Value}"))).Append(')');
        }
        if (FirstAtSnapshot != null)
            sb.Append(First == null ? "" : "\n  first snapshot difference: ").Append(FirstAtSnapshot);
        if (FinalCash.Expected != FinalCash.Actual)
            sb.Append($"\n  final cash {FinalCash.Expected} → {FinalCash.Actual} NOK");
        if (FinalBiomassKg.Expected != FinalBiomassKg.Actual)
            sb.Append($"\n  final live biomass {FinalBiomassKg.Expected} → {FinalBiomassKg.Actual} kg");
        sb.Append(drift);
        return sb.ToString();
    }
}

/// <summary>
/// Compares two golden traces line by line (F2-01): the first day whose group
/// hashes, entity tags, stream draws, events, commands or status differ names
/// the scenario, seed, day and field; the snapshots then give the exact field
/// and both values, and the digest tree the deepest differing save node.
/// </summary>
public static class GoldenTraceComparer
{
    /// <summary>Header keys that must agree (recordedOn is metadata).</summary>
    private static readonly string[] HeaderKeys = { "format", "scenario", "seed", "days", "snapshotEveryDays", "player", "events" };

    public static TraceComparison Compare(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count == 0 || actual.Count == 0)
            throw new ArgumentException("a trace has no header line");
        using var expectedHeader = JsonDocument.Parse(expected[0]);
        using var actualHeader = JsonDocument.Parse(actual[0]);
        var e0 = expectedHeader.RootElement;
        var comparison = new TraceComparison
        {
            Scenario = e0.GetProperty("scenario").GetString() ?? "",
            Seed = e0.GetProperty("seed").GetInt32(),
        };
        foreach (var key in HeaderKeys)
        {
            string a = Raw(e0, key), b = Raw(actualHeader.RootElement, key);
            if (a != b)
            {
                comparison.First = new TraceDifference(comparison.Scenario, comparison.Seed, -1, "header", key, a, b);
                return comparison;
            }
        }

        var expectedDays = Split(expected, "d", out var expectedSnaps);
        var actualDays = Split(actual, "d", out var actualSnaps);
        int days = Math.Min(expectedDays.Count, actualDays.Count);
        comparison.DaysCompared = days;
        for (int i = 0; i < days; i++)
            CompareDay(comparison, expectedDays[i], actualDays[i]);
        if (expectedDays.Count != actualDays.Count && comparison.First == null)
            comparison.First = new TraceDifference(comparison.Scenario, comparison.Seed, days, "length", "day lines",
                expectedDays.Count.ToString(CultureInfo.InvariantCulture), actualDays.Count.ToString(CultureInfo.InvariantCulture));
        CompareSnapshots(comparison, expectedSnaps, actualSnaps);
        return comparison;
    }

    // ── Day lines ──────────────────────────────────────────────────────────

    private static void CompareDay(TraceComparison comparison, string expected, string actual)
    {
        if (expected == actual) return;
        using var e = JsonDocument.Parse(expected);
        using var a = JsonDocument.Parse(actual);
        int day = e.RootElement.GetProperty("d").GetInt32();
        var differences = new List<TraceDifference>();
        var eg = e.RootElement.GetProperty("g");
        var ag = a.RootElement.GetProperty("g");
        bool detailed = e.RootElement.TryGetProperty("v", out var ev) & a.RootElement.TryGetProperty("v", out var av);
        foreach (var group in GoldenTraceState.GroupOrder)
        {
            if (Raw(eg, group) == Raw(ag, group)) continue;
            differences.Add(detailed && FirstValueDifference(comparison, day, ev, av, group) is { } exact
                ? exact
                : new TraceDifference(comparison.Scenario, comparison.Seed, day, group, "(group hash; the next snapshot names the entity)",
                    Raw(eg, group), Raw(ag, group)));
        }
        string eDraws = Raw(e.RootElement, "rng"), aDraws = Raw(a.RootElement, "rng");
        if (eDraws != aDraws || Raw(eg, "rng") != Raw(ag, "rng"))
        {
            var key = FirstDifferentKey(Map(e.RootElement, "rng"), Map(a.RootElement, "rng"));
            differences.Add(key == null
                ? new TraceDifference(comparison.Scenario, comparison.Seed, day, "rng", "stream positions", Raw(eg, "rng"), Raw(ag, "rng"))
                : new TraceDifference(comparison.Scenario, comparison.Seed, day, "rng", "draws of stream " + key,
                    Map(e.RootElement, "rng").GetValueOrDefault(key, "0"), Map(a.RootElement, "rng").GetValueOrDefault(key, "0")));
        }
        foreach (var field in new[] { "ev", "cmd", "st" })
        {
            string ef = Raw(e.RootElement, field), af = Raw(a.RootElement, field);
            if (ef != af)
                differences.Add(new TraceDifference(comparison.Scenario, comparison.Seed, day, field,
                    field switch { "ev" => "events", "cmd" => "player commands", _ => "scenario status" }, ef, af));
        }
        if (differences.Count == 0 && Raw(e.RootElement, "h") != Raw(a.RootElement, "h"))
            differences.Add((detailed ? FirstValueDifference(comparison, day, ev, av) : null)
                ?? new TraceDifference(comparison.Scenario, comparison.Seed, day, "state", "(day hash only: the 8-bit group hashes collided; the next snapshot names it)",
                    Raw(e.RootElement, "h"), Raw(a.RootElement, "h")));
        if (differences.Count == 0) return;
        comparison.DivergentDays++;
        foreach (var group in differences.Select(d => d.Group).Distinct())
            comparison.DivergentDaysByGroup[group] = comparison.DivergentDaysByGroup.GetValueOrDefault(group) + 1;
        comparison.First ??= differences[0];
    }

    // ── Snapshots ──────────────────────────────────────────────────────────

    private static void CompareSnapshots(TraceComparison comparison, List<string> expected, List<string> actual)
    {
        var expectedTree = new Dictionary<string, string>(StringComparer.Ordinal);
        var actualTree = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < Math.Min(expected.Count, actual.Count); i++)
        {
            using var e = JsonDocument.Parse(expected[i]);
            using var a = JsonDocument.Parse(actual[i]);
            Accumulate(expectedTree, e.RootElement);
            Accumulate(actualTree, a.RootElement);
            if (i == Math.Min(expected.Count, actual.Count) - 1)
                RecordFinals(comparison, e.RootElement, a.RootElement);
            if (expected[i] == actual[i]) continue;
            int day = e.RootElement.GetProperty("snap").GetInt32();
            comparison.FirstAtSnapshot ??= FirstValueDifference(comparison, day, e.RootElement.GetProperty("v"), a.RootElement.GetProperty("v"));
            string expectedSave = Raw(e.RootElement, "save"), actualSave = Raw(a.RootElement, "save");
            if (expectedSave == actualSave) continue;
            comparison.SaveDriftSnapshots++;
            comparison.FirstSaveDrift ??= SaveDrift(comparison, day, expectedTree, actualTree, expectedSave, actualSave);
        }
        if (expected.Count != actual.Count && comparison.FirstAtSnapshot == null)
            comparison.FirstAtSnapshot = new TraceDifference(comparison.Scenario, comparison.Seed, -1, "length", "snapshots",
                expected.Count.ToString(CultureInfo.InvariantCulture), actual.Count.ToString(CultureInfo.InvariantCulture));
    }

    private static TraceDifference? FirstValueDifference(TraceComparison comparison, int day, JsonElement expected, JsonElement actual,
        string? onlyGroup = null)
    {
        foreach (var group in onlyGroup == null ? GoldenTraceState.GroupOrder : new[] { onlyGroup })
        {
            var e = Map(expected, group);
            var a = Map(actual, group);
            var key = FirstDifferentKey(e, a);
            if (key == null) continue;
            if (!e.TryGetValue(key, out var ev) || !a.TryGetValue(key, out var av))
                return new TraceDifference(comparison.Scenario, comparison.Seed, day, group, key, e.GetValueOrDefault(key, "absent"), a.GetValueOrDefault(key, "absent"));
            var fields = DifferentFields(ev, av);
            return fields.Count == 0
                ? new TraceDifference(comparison.Scenario, comparison.Seed, day, group, key, ev, av)
                : new TraceDifference(comparison.Scenario, comparison.Seed, day, group, $"{key} › {string.Join(", ", fields.Select(f => f.Name))}",
                    string.Join(", ", fields.Select(f => $"{f.Name}={f.Expected}")), string.Join(", ", fields.Select(f => $"{f.Name}={f.Actual}")));
        }
        return null;
    }

    /// <summary>The fields of two canonical texts that differ, in the order the text lists them.</summary>
    private static List<(string Name, string Expected, string Actual)> DifferentFields(string expected, string actual)
    {
        var e = Fields(expected);
        var a = Fields(actual);
        var names = new List<string>();
        foreach (var part in (expected + ";" + actual).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            string name = eq > 0 ? part[..eq] : part;
            if (!names.Contains(name)) names.Add(name);
        }
        return names
            .Where(n => e.GetValueOrDefault(n, "absent") != a.GetValueOrDefault(n, "absent"))
            .Select(n => (n, e.GetValueOrDefault(n, "absent"), a.GetValueOrDefault(n, "absent")))
            .ToList();
    }

    /// <summary>The save's changed leaves (the deepest digests that differ), then its added and removed nodes.</summary>
    private static TraceDifference SaveDrift(TraceComparison comparison, int day,
        Dictionary<string, string> expected, Dictionary<string, string> actual, string expectedSave, string actualSave)
    {
        var parents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in expected.Keys.Concat(actual.Keys))
            for (int i = 1; i < path.Length; i++)
                if (path[i] == '/' || path[i] == '[') parents.Add(path[..i]);
        var changed = expected.Keys
            .Where(p => actual.TryGetValue(p, out var h) && h != expected[p] && !parents.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        var added = actual.Keys.Where(p => !expected.ContainsKey(p) && !HasNewAncestor(p, actual, expected))
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        var removed = expected.Keys.Where(p => !actual.ContainsKey(p) && !HasNewAncestor(p, expected, actual))
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        var parts = new List<string>();
        if (changed.Count > 0) parts.Add("changed " + Paths(changed));
        if (added.Count > 0) parts.Add("added " + Paths(added));
        if (removed.Count > 0) parts.Add("removed " + Paths(removed));
        if (parts.Count == 0) parts.Add("(save hash only)");
        return new TraceDifference(comparison.Scenario, comparison.Seed, day, "save", string.Join("; ", parts), expectedSave, actualSave);
    }

    /// <summary>True when an ancestor of <paramref name="path"/> is also only in <paramref name="tree"/> (the topmost new node is reported).</summary>
    private static bool HasNewAncestor(string path, Dictionary<string, string> tree, Dictionary<string, string> other)
    {
        for (int i = 1; i < path.Length; i++)
            if ((path[i] == '/' || path[i] == '[') && tree.ContainsKey(path[..i]) && !other.ContainsKey(path[..i]))
                return true;
        return false;
    }

    private static string Paths(List<string> paths)
        => string.Join(", ", paths.Take(6)) + (paths.Count > 6 ? $" (+{paths.Count - 6} more)" : "");

    private static void Accumulate(Dictionary<string, string> tree, JsonElement snapshot)
    {
        if (snapshot.TryGetProperty("gone", out var gone))
            foreach (var path in gone.EnumerateArray()) tree.Remove(path.GetString() ?? "");
        foreach (var node in snapshot.GetProperty("tree").EnumerateObject())
            tree[node.Name] = node.Value.GetString() ?? "";
    }

    private static void RecordFinals(TraceComparison comparison, JsonElement expected, JsonElement actual)
    {
        comparison.FinalCash = (CashOf(expected), CashOf(actual));
        comparison.FinalBiomassKg = (BiomassOf(expected), BiomassOf(actual));
    }

    /// <summary>The balance in a snapshot ("" when absent).</summary>
    public static string CashOf(JsonElement snapshot)
        => Fields(Map(snapshot.GetProperty("v"), "cash").GetValueOrDefault("balance", "")).GetValueOrDefault("nok", "");

    /// <summary>Live biomass summed over a snapshot's populations (kg, 0 decimals).</summary>
    public static string BiomassOf(JsonElement snapshot)
    {
        double kg = 0.0;
        foreach (var value in Map(snapshot.GetProperty("v"), "pop").Values)
            if (Fields(value).TryGetValue("kg", out var text))
                kg += double.Parse(text, CultureInfo.InvariantCulture);
        return kg.ToString("F0", CultureInfo.InvariantCulture);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Day lines (those with <paramref name="dayKey"/>) and snapshot lines of a trace, header skipped.</summary>
    private static List<string> Split(IReadOnlyList<string> lines, string dayKey, out List<string> snapshots)
    {
        var days = new List<string>();
        snapshots = new List<string>();
        foreach (var line in lines.Skip(1))
            (line.StartsWith("{\"" + dayKey + "\":", StringComparison.Ordinal) ? days : snapshots).Add(line);
        return days;
    }

    private static string Raw(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) ? value.GetRawText() : "";

    /// <summary>The string (or number) members of an object member, by name.</summary>
    private static SortedDictionary<string, string> Map(JsonElement element, string property)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object) return map;
        foreach (var member in value.EnumerateObject())
            map[member.Name] = member.Value.ValueKind == JsonValueKind.String ? member.Value.GetString() ?? "" : member.Value.GetRawText();
        return map;
    }

    /// <summary>"a=1;b=2" → {a: 1, b: 2}.</summary>
    private static SortedDictionary<string, string> Fields(string canonical)
    {
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in canonical.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) fields[part[..eq]] = part[(eq + 1)..];
        }
        return fields;
    }

    private static string? FirstDifferentKey(SortedDictionary<string, string> expected, SortedDictionary<string, string> actual)
        => expected.Keys.Union(actual.Keys).OrderBy(k => k, StringComparer.Ordinal)
            .FirstOrDefault(k => !expected.TryGetValue(k, out var e) || !actual.TryGetValue(k, out var a) || e != a);
}
