using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StateHashProbe;

/// <summary>
/// A directory of golden traces: one .jsonl.gz per shipped scenario × seed and
/// a manifest.json (format, player, platform, each trace's content hash).
/// Records a set, reads its manifest and writes the Markdown diff report of
/// two sets (tools/record_golden_traces.sh).
/// </summary>
public static class GoldenTraceSet
{
    public const string ManifestFileName = "manifest.json";

    /// <summary>One trace in a manifest.</summary>
    public sealed record Entry(string File, string Scenario, int Seed, int Lines, string Sha256, string FinalSave);

    /// <summary>A set's manifest.</summary>
    public sealed record Manifest(string Format, string Player, int Days, int SnapshotEveryDays,
        IReadOnlyList<int> Seeds, IReadOnlyList<string> Scenarios, IReadOnlyDictionary<string, string> RecordedOn,
        IReadOnlyList<Entry> Files);

    /// <summary>
    /// Record every <paramref name="scenarios"/> × <paramref name="seeds"/> trace
    /// into <paramref name="directory"/> (up to <paramref name="jobs"/> at a time;
    /// each run is its own engine, so the files do not depend on the order).
    /// </summary>
    public static Manifest Record(string directory, IReadOnlyList<string> scenarios, IReadOnlyList<int> seeds,
        int days, string commit, int jobs, bool detail = false, TextWriter? log = null)
    {
        Directory.CreateDirectory(directory);
        var runs = scenarios.SelectMany(s => seeds.Select(seed => (Scenario: s, Seed: seed))).ToList();
        var results = new List<string>[runs.Count];
        Parallel.For(0, runs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs) }, i =>
        {
            results[i] = GoldenTraceRecorder.Record(runs[i].Scenario, runs[i].Seed,
                new GoldenTraceRecorder.Options { Days = days, Commit = commit, Detail = detail });
            lock (runs) log?.WriteLine($"recorded {runs[i].Scenario} seed {runs[i].Seed}");
        });

        var entries = new List<Entry>();
        for (int i = 0; i < runs.Count; i++)
        {
            string file = GoldenTraceRecorder.FileName(runs[i].Scenario, runs[i].Seed);
            File.WriteAllBytes(Path.Combine(directory, file), GoldenTraceRecorder.Compress(results[i]));
            entries.Add(new Entry(file, runs[i].Scenario, runs[i].Seed, results[i].Count,
                GoldenTraceRecorder.ContentHash(results[i]), FinalSave(results[i])));
        }
        var manifest = new Manifest(GoldenTraceRecorder.Format, GoldenPlayer.Version, days, GoldenTraceRecorder.SnapshotEveryDays,
            seeds.ToList(), scenarios.ToList(), GoldenTraceRecorder.RecordedOn(commit),
            entries.OrderBy(e => e.File, StringComparer.Ordinal).ToList());
        WriteManifest(Path.Combine(directory, ManifestFileName), manifest);
        return manifest;
    }

    /// <summary>The last snapshot's save hash of a trace.</summary>
    private static string FinalSave(IReadOnlyList<string> lines)
    {
        using var doc = JsonDocument.Parse(lines.Last(l => l.StartsWith("{\"snap\":", StringComparison.Ordinal)));
        return doc.RootElement.GetProperty("save").GetString() ?? "";
    }

    public static void WriteManifest(string path, Manifest manifest)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", manifest.Format);
            w.WriteString("player", manifest.Player);
            w.WriteNumber("days", manifest.Days);
            w.WriteNumber("snapshotEveryDays", manifest.SnapshotEveryDays);
            w.WriteStartArray("seeds");
            foreach (int seed in manifest.Seeds) w.WriteNumberValue(seed);
            w.WriteEndArray();
            w.WriteStartArray("scenarios");
            foreach (var scenario in manifest.Scenarios) w.WriteStringValue(scenario);
            w.WriteEndArray();
            w.WriteStartObject("recordedOn");
            foreach (var kv in manifest.RecordedOn) w.WriteString(kv.Key, kv.Value);
            w.WriteEndObject();
            w.WriteStartArray("files");
            foreach (var e in manifest.Files)
            {
                w.WriteStartObject();
                w.WriteString("file", e.File);
                w.WriteString("scenario", e.Scenario);
                w.WriteNumber("seed", e.Seed);
                w.WriteNumber("lines", e.Lines);
                w.WriteString("sha256", e.Sha256);
                w.WriteString("finalSave", e.FinalSave);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        File.WriteAllBytes(path, buffer.ToArray().Append((byte)'\n').ToArray());
    }

    public static Manifest ReadManifest(string directory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, ManifestFileName)));
        var r = doc.RootElement;
        return new Manifest(
            r.GetProperty("format").GetString() ?? "",
            r.GetProperty("player").GetString() ?? "",
            r.GetProperty("days").GetInt32(),
            r.GetProperty("snapshotEveryDays").GetInt32(),
            r.GetProperty("seeds").EnumerateArray().Select(s => s.GetInt32()).ToList(),
            r.GetProperty("scenarios").EnumerateArray().Select(s => s.GetString() ?? "").ToList(),
            r.GetProperty("recordedOn").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal),
            r.GetProperty("files").EnumerateArray().Select(f => new Entry(
                f.GetProperty("file").GetString() ?? "", f.GetProperty("scenario").GetString() ?? "",
                f.GetProperty("seed").GetInt32(), f.GetProperty("lines").GetInt32(),
                f.GetProperty("sha256").GetString() ?? "", f.GetProperty("finalSave").GetString() ?? "")).ToList());
    }

    /// <summary>
    /// Compare two recorded sets trace by trace and write a Markdown report
    /// (verdict, first divergence, divergent days per group, final cash and
    /// biomass). Returns the number of traces that differ or are missing.
    /// </summary>
    public static int Diff(string expectedDirectory, string actualDirectory, TextWriter report)
    {
        var expected = ReadManifest(expectedDirectory);
        var actual = ReadManifest(actualDirectory);
        report.WriteLine("# Golden-trace diff report");
        report.WriteLine();
        report.WriteLine($"- expected: `{expectedDirectory}` ({Describe(expected.RecordedOn)})");
        report.WriteLine($"- actual: `{actualDirectory}` ({Describe(actual.RecordedOn)})");
        bool sameMath = expected.RecordedOn.GetValueOrDefault("math") == actual.RecordedOn.GetValueOrDefault("math");
        report.WriteLine(sameMath
            ? "- math fingerprints are equal: the traces are comparable bit for bit"
            : "- **math fingerprints differ**: the two sets come from platforms whose C math library differs, so bit-exact differences may be the platform's; record both on one machine");
        if (expected.Format != actual.Format || expected.Player != actual.Player)
            report.WriteLine($"- **format or player changed**: {expected.Format}/{expected.Player} → {actual.Format}/{actual.Player}");
        report.WriteLine();
        report.WriteLine("| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |");
        report.WriteLine("|---|---|---|---|---|---|---|");

        var details = new StringBuilder();
        int differing = 0;
        var files = expected.Files.Select(f => f.File).Union(actual.Files.Select(f => f.File)).OrderBy(f => f, StringComparer.Ordinal);
        foreach (var file in files)
        {
            var e = expected.Files.FirstOrDefault(f => f.File == file);
            var a = actual.Files.FirstOrDefault(f => f.File == file);
            if (e == null || a == null)
            {
                differing++;
                var only = e ?? a!;
                report.WriteLine($"| {only.Scenario} | {only.Seed} | {(e == null ? "new" : "missing")} | — | — | — | — |");
                continue;
            }
            var comparison = e.Sha256 == a.Sha256 ? null : GoldenTraceComparer.Compare(
                GoldenTraceRecorder.ReadLines(Path.Combine(expectedDirectory, file)),
                GoldenTraceRecorder.ReadLines(Path.Combine(actualDirectory, file)));
            if (comparison == null || comparison.Identical)
            {
                report.WriteLine($"| {e.Scenario} | {e.Seed} | identical | — | 0 | — | — |");
                continue;
            }
            differing++;
            string first = comparison.First is { } d ? $"{d.Day}: {d.Group} › {d.Field}" : comparison.FirstAtSnapshot?.ToString() ?? "";
            report.WriteLine($"| {e.Scenario} | {e.Seed} | **differs** | {first.Replace("|", "/")} | {comparison.DivergentDays}/{comparison.DaysCompared} | " +
                $"{comparison.FinalCash.Expected} → {comparison.FinalCash.Actual} | {comparison.FinalBiomassKg.Expected} → {comparison.FinalBiomassKg.Actual} |");
            details.AppendLine($"### {e.Scenario} seed {e.Seed}").AppendLine().AppendLine("```").AppendLine(comparison.Summary()).AppendLine("```").AppendLine();
        }
        report.WriteLine();
        report.WriteLine($"{files.Count() - differing} of {files.Count()} traces identical.");
        if (details.Length > 0)
        {
            report.WriteLine();
            report.WriteLine("## Details");
            report.WriteLine();
            report.Write(details.ToString());
        }
        return differing;
    }

    private static string Describe(IReadOnlyDictionary<string, string> recordedOn)
        => string.Join(", ", recordedOn.Select(kv => $"{kv.Key} {(kv.Key == "commit" && kv.Value.Length > 12 ? kv.Value[..12] : kv.Value)}"));

    /// <summary>"a,b" → [a, b].</summary>
    public static List<string> ParseList(string text)
        => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>"1,7,13" → [1, 7, 13].</summary>
    public static List<int> ParseSeeds(string text)
        => ParseList(text).Select(s => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture)).ToList();
}
