using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LakskongenSim.Core;

namespace StateHashProbe;

/// <summary>
/// Golden-trace recorder (FARM_TYPES_SPEC F2-01). Plays every shipped scenario
/// for <see cref="Days"/> days with events on and the <see cref="GoldenPlayer"/>,
/// and writes one JSONL line per day (a hash per group, a tag per entity, each
/// stream's draws, the day's events and commands) plus a snapshot every
/// <see cref="SnapshotEveryDays"/> days (exact values of the groups and a digest
/// tree of the whole GUID-normalized save). Later sim items prove they change
/// nothing by recording their merge base and their head and diffing
/// (tools/record_golden_traces.sh, docs/GOLDEN_TRACES.md).
/// </summary>
public static class GoldenTraceRecorder
{
    /// <summary>Trace format; bump it when a line's shape or meaning changes.</summary>
    public const string Format = "lakskongen-golden-trace/1";

    /// <summary>Days every trace covers (F2-01 acceptance 3).</summary>
    public const int Days = 730;

    /// <summary>A snapshot line every this many days, plus one on the last day.</summary>
    public const int SnapshotEveryDays = 30;

    /// <summary>The seeds every shipped scenario is recorded on.</summary>
    public static readonly int[] Seeds = { 1, 7, 13 };

    /// <summary>Snapshot digest tree: deepest level below the root.</summary>
    private const int TreeDepth = 3;

    /// <summary>Snapshot digest tree: objects and arrays with more children stay one node.</summary>
    private const int TreeMaxChildren = 64;

    /// <summary>Snapshot digest tree: a level-2 node is split into its children only above this size (bytes of JSON).</summary>
    private const int TreeSplitBytes = 2048;

    private static readonly Regex GuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    /// <summary>What no trace line hashes (written to every header, F2-01 acceptance 3).</summary>
    public static readonly string[] Excluded =
    {
        "Guid-valued ids (FishPopulation.Id and every other Guid default) are process-random by design: the save digest replaces them with 'GUID', and populations are keyed by their pen, tank or holding, never by Id",
        "the day line hashes only the groups listed under 'groups'; everything else (weather and zone water, market and price council, competitors, storyteller state, contracts, loans, insurance, research, disease prevalence, harvest orders and reports, licences' ids, analytics and histories) is covered by the snapshot's digest of the whole save every 30 days",
        "event text: a day line lists event ids (or names when an event has no id), not their descriptions; the save digest covers the descriptions",
        "-0.0 is written as 0 and decimals without trailing zeros in the day groups (equal values); the save digest keeps the serializer's exact text",
        "engine state outside GameState: none is needed, a save resumes bit-identically (SaveLoadMatrixTests)",
        "the header's recordedOn block (commit, platform, runtime, math fingerprint) is metadata and never compared",
    };

    /// <summary>Options for one recording.</summary>
    public sealed class Options
    {
        /// <summary>Days to simulate (default <see cref="Days"/>).</summary>
        public int Days { get; init; } = GoldenTraceRecorder.Days;

        /// <summary>The commit recorded (header metadata; "" in process).</summary>
        public string Commit { get; init; } = "";

        /// <summary>Test hook: runs right after the engine reaches day d (d ≥ 1), before the player acts and the day is recorded.</summary>
        public Action<SimulationEngine, int>? AfterAdvance { get; init; }

        /// <summary>
        /// Diagnosis mode (never committed): every day line also carries the
        /// groups' exact values ("v"), so a diff of two detailed recordings
        /// names the entity and field on the very day they part.
        /// </summary>
        public bool Detail { get; init; }
    }

    /// <summary>The scenarios a fresh engine ships (ordinal order).</summary>
    public static IReadOnlyList<string> ShippedScenarioIds()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        return engine.Scenario.All.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    /// <summary>File name of one trace.</summary>
    public static string FileName(string scenarioId, int seed)
        => $"{scenarioId}.seed{seed.ToString("D2", CultureInfo.InvariantCulture)}.jsonl.gz";

    /// <summary>Record one scenario on one seed: the trace's lines (header first).</summary>
    public static List<string> Record(string scenarioId, int seed, Options? options = null)
    {
        options ??= new Options();
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: seed);
        engine.State.RandomSeed = seed;
        var started = engine.StartScenario(scenarioId);
        if (!started.Ok)
            throw new InvalidOperationException($"{scenarioId}: {started.ErrorKey}: {started.Message}");

        var lines = new List<string> { HeaderLine(scenarioId, seed, options) };
        var cursor = new Cursor();
        var player = new GoldenPlayer();
        for (int day = 0; day <= options.Days; day++)
        {
            if (day > 0)
            {
                engine.Advance(1);
                if (engine.State.Day != day)
                    throw new InvalidOperationException($"{scenarioId} seed {seed}: engine at day {engine.State.Day}, expected {day}");
                options.AfterAdvance?.Invoke(engine, day);
            }
            var commands = day < options.Days ? player.Act(engine, day) : new List<string>();
            var state = GoldenTraceState.Capture(engine);
            lines.Add(DayLine(engine, day, state, commands, cursor, options.Detail));
            if (day % SnapshotEveryDays == 0 || day == options.Days)
                lines.Add(SnapshotLine(engine, day, state, cursor));
        }
        return lines;
    }

    /// <summary>The trace as the bytes of its .jsonl.gz file (deterministic: fixed header, no file name or time).</summary>
    public static byte[] Compress(IReadOnlyList<string> lines)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(Join(lines)));
        return buffer.ToArray();
    }

    /// <summary>The lines of a .jsonl.gz trace.</summary>
    public static List<string> ReadLines(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            if (line.Length > 0) lines.Add(line);
        return lines;
    }

    /// <summary>SHA-256 (hex) of a trace's uncompressed text: the manifest's identity of a trace.</summary>
    public static string ContentHash(IReadOnlyList<string> lines)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Join(lines)))).ToLowerInvariant();

    private static string Join(IReadOnlyList<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines) sb.Append(line).Append('\n');
        return sb.ToString();
    }

    // ── Platform ───────────────────────────────────────────────────────────

    /// <summary>
    /// Hash of the transcendental functions the sim calls (Math.Sin … Math.Cbrt,
    /// MathF.Exp) over 4,096 inputs per function. .NET takes them from the
    /// platform's C library, so bit-exact traces are comparable only where this
    /// matches the recording's (GoldenTraceTests); elsewhere the base-vs-head
    /// recording in tools/record_golden_traces.sh is the gate.
    /// </summary>
    public static string MathFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(double v) => hash.AppendData(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(v)));
        ulong s = 0x2545F4914F6CDD1DUL;
        double Next()
        {
            s = unchecked(s + 0x9E3779B97F4A7C15UL);
            ulong z = s;
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return ((z ^ (z >> 31)) >> 11) * (1.0 / 9007199254740992.0);
        }
        for (int i = 0; i < 4096; i++)
        {
            double u = Next(), v = Next();
            double x = (u - 0.5) * 40.0;
            Add(Math.Sin(x)); Add(Math.Cos(x)); Add(Math.Tan(x));
            Add(Math.Asin(2.0 * u - 1.0)); Add(Math.Acos(2.0 * v - 1.0)); Add(Math.Atan2(x, v - 0.5));
            Add(Math.Exp(x)); Add(Math.Exp(-u * 3.0));
            Add(Math.Log(u * 1000.0 + 1e-9)); Add(Math.Log10(v * 100.0 + 1e-9));
            Add(Math.Pow(u * 30.0, (v - 0.5) * 6.0)); Add(Math.Pow(u * 8.0, 1.0 / 3.0)); Add(Math.Pow(v, 0.75));
            Add(Math.Cbrt(x)); Add(Math.Sqrt(u)); Add(MathF.Exp((float)x * 0.5f));
        }
        return Convert.ToHexString(hash.GetHashAndReset(), 0, 8).ToLowerInvariant();
    }

    /// <summary>The platform a trace is recorded on (header and manifest metadata).</summary>
    public static Dictionary<string, string> RecordedOn(string commit) => new(StringComparer.Ordinal)
    {
        ["commit"] = commit,
        ["rid"] = RuntimeInformation.RuntimeIdentifier,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["math"] = MathFingerprint(),
    };

    // ── Lines ──────────────────────────────────────────────────────────────

    /// <summary>Per-recording memory: yesterday's streams, the last event seen, the scenario status, the last snapshot tree.</summary>
    private sealed class Cursor
    {
        public SortedDictionary<string, ulong>? Streams;
        public object? LastEvent;
        public string Status = "";
        public SortedDictionary<string, string> Tree = new(StringComparer.Ordinal);
    }

    private static string HeaderLine(string scenarioId, int seed, Options options) => Line(w =>
    {
        w.WriteString("format", Format);
        w.WriteString("scenario", scenarioId);
        w.WriteNumber("seed", seed);
        w.WriteNumber("days", options.Days);
        w.WriteNumber("snapshotEveryDays", SnapshotEveryDays);
        w.WriteString("player", GoldenPlayer.Version);
        w.WriteBoolean("events", true);
        w.WriteBoolean("detail", options.Detail);
        w.WriteStartObject("groups");
        foreach (var kv in GoldenTraceState.GroupDescriptions) w.WriteString(kv.Key, kv.Value);
        w.WriteEndObject();
        w.WriteString("dayLine", "the state at day 'd' after the player's commands of that day: 'h' SHA-256 (64 bit) of every group's " +
            "canonical text and every stream state; 'g' a 16-bit hash per group; 'rng' each stream's draws that day (new streams: 'new:' and a tag " +
            "of their state); 'ev' event-log entries added since the previous line; 'cmd' the golden player's commands and outcomes; 'st' the " +
            "scenario status when it changes; 'v' (detail recordings only) every group's exact values");
        w.WriteString("snapshot", $"day {SnapshotEveryDays}k and the last day: 'v' holds every group's exact values (entity → field=value;…); " +
            "'save' is SHA-256 (64 bit) of the GUID-normalized save (PrepareForSave + SaveJson.Options); 'tree' holds 16-bit digests of its " +
            $"nodes to depth {TreeDepth} (every top-level member; below it objects and arrays of at most {TreeMaxChildren} children, level-2 nodes split above {TreeSplitBytes} bytes), " +
            "listing only nodes that changed since the previous snapshot, 'gone' the nodes that disappeared");
        w.WriteStartArray("excluded");
        foreach (var item in Excluded) w.WriteStringValue(item);
        w.WriteEndArray();
        w.WriteStartObject("recordedOn");
        foreach (var kv in RecordedOn(options.Commit)) w.WriteString(kv.Key, kv.Value);
        w.WriteEndObject();
    });

    private static string DayLine(SimulationEngine engine, int day, GoldenTraceState state, List<string> commands, Cursor cursor, bool detail) => Line(w =>
    {
        w.WriteNumber("d", day);
        w.WriteString("h", state.DayHash());
        w.WriteStartObject("g");
        foreach (var group in GoldenTraceState.GroupOrder) w.WriteString(group, state.GroupHash(group));
        w.WriteString("rng", state.StreamHash());
        w.WriteEndObject();
        WriteDraws(w, state.Streams, cursor);
        WriteEvents(w, engine, cursor);
        if (commands.Count > 0)
        {
            w.WriteStartArray("cmd");
            foreach (var command in commands) w.WriteStringValue(command);
            w.WriteEndArray();
        }
        string status = engine.State.ScenarioIsWon ? "won" : engine.State.ScenarioIsLost ? "lost" : "playing";
        if (status != cursor.Status)
        {
            w.WriteString("st", status);
            cursor.Status = status;
        }
        if (detail) WriteValues(w, state);
    });

    /// <summary>"v": every group's exact values (entity → "field=value;…").</summary>
    private static void WriteValues(Utf8JsonWriter w, GoldenTraceState state)
    {
        w.WriteStartObject("v");
        foreach (var group in GoldenTraceState.GroupOrder)
        {
            w.WriteStartObject(group);
            foreach (var kv in state.Groups[group]) w.WriteString(kv.Key, kv.Value);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    /// <summary>Each stream's draws since yesterday (streams that drew nothing are left out; a new stream shows its state's tag).</summary>
    private static void WriteDraws(Utf8JsonWriter w, SortedDictionary<string, ulong> streams, Cursor cursor)
    {
        w.WriteStartObject("rng");
        foreach (var kv in streams)
        {
            if (cursor.Streams != null && cursor.Streams.TryGetValue(kv.Key, out ulong before))
            {
                ulong draws = GoldenTraceState.DrawsBetween(before, kv.Value);
                if (draws != 0) w.WriteNumber(kv.Key, draws);
            }
            else
                w.WriteString(kv.Key, "new:" + GoldenTraceState.Tag(kv.Key, GoldenTraceState.StateHex(kv.Value)));
        }
        w.WriteEndObject();
        cursor.Streams = new SortedDictionary<string, ulong>(streams, StringComparer.Ordinal);
    }

    /// <summary>Event-log entries added since the previous line (their ids; the log is trimmed from the front, so the last entry seen marks the place).</summary>
    private static void WriteEvents(Utf8JsonWriter w, SimulationEngine engine, Cursor cursor)
    {
        var log = engine.State.EventLog;
        int start = 0;
        if (cursor.LastEvent != null)
        {
            int seen = log.FindLastIndex(r => ReferenceEquals(r, cursor.LastEvent));
            start = seen + 1;
        }
        if (start < log.Count)
        {
            w.WriteStartArray("ev");
            for (int i = start; i < log.Count; i++)
                w.WriteStringValue(GuidPattern.Replace(string.IsNullOrEmpty(log[i].Id) ? log[i].Name : log[i].Id, "GUID"));
            w.WriteEndArray();
        }
        cursor.LastEvent = log.Count > 0 ? log[^1] : null;
    }

    private static string SnapshotLine(SimulationEngine engine, int day, GoldenTraceState state, Cursor cursor)
    {
        engine.PrepareForSave();
        string save = GuidPattern.Replace(JsonSerializer.Serialize(engine.State, SaveJson.Options), "GUID");
        var tree = DigestTree(save);
        return Line(w =>
        {
            w.WriteNumber("snap", day);
            w.WriteString("save", GoldenTraceState.Hex(save, 8));
            w.WriteStartObject("tree");
            foreach (var kv in tree)
                if (!cursor.Tree.TryGetValue(kv.Key, out var before) || before != kv.Value)
                    w.WriteString(kv.Key, kv.Value);
            w.WriteEndObject();
            var gone = cursor.Tree.Keys.Where(k => !tree.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (gone.Count > 0)
            {
                w.WriteStartArray("gone");
                foreach (var path in gone) w.WriteStringValue(path);
                w.WriteEndArray();
            }
            WriteValues(w, state);
            cursor.Tree = tree;
        });
    }

    /// <summary>32-bit digests of the save's JSON nodes to <see cref="TreeDepth"/> levels ("/SeaSites[0]/Lice").</summary>
    public static SortedDictionary<string, string> DigestTree(string json)
    {
        var tree = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        Walk(doc.RootElement, "", 0, tree);
        return tree;
    }

    private static void Walk(JsonElement node, string path, int depth, SortedDictionary<string, string> tree)
    {
        if (depth > 0)
        {
            string raw = node.GetRawText();
            tree[path] = GoldenTraceState.Hex(raw, 2);
            if (depth >= TreeDepth || (depth >= 2 && raw.Length <= TreeSplitBytes)) return;
        }
        if (node.ValueKind == JsonValueKind.Object)
        {
            // The save's root is always split into its members; deeper objects only when small enough to list.
            if (depth > 0 && node.EnumerateObject().Count() > TreeMaxChildren) return;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                seen.TryGetValue(property.Name, out int n);
                seen[property.Name] = n + 1;
                string name = n == 0 ? property.Name : $"{property.Name}#{n + 1}";
                Walk(property.Value, path + "/" + name, depth + 1, tree);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() <= TreeMaxChildren)
        {
            int i = 0;
            foreach (var item in node.EnumerateArray())
                Walk(item, $"{path}[{i++}]", depth + 1, tree);
        }
    }

    private static string Line(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            write(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
