using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using StateHashProbe;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// The committed golden traces (FARM_TYPES_SPEC F2-01): every shipped scenario
/// × seeds 1, 7, 13 × 730 days, recorded by tools/record_golden_traces.sh on the
/// pre-F2 commit (src/LakskongenSim.Tests/Fixtures/golden_traces). A sim item
/// that must not change the game (the F2 standing gate) keeps them; one that
/// does re-records them with a diff report (docs/GOLDEN_TRACES.md).
/// </summary>
internal static class GoldenTraceFixtures
{
    private static readonly Lazy<string> DirectoryPath = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "LakskongenSim.Tests", "Fixtures", "golden_traces");
    });

    private static readonly Lazy<GoldenTraceSet.Manifest> LazyManifest = new(() => GoldenTraceSet.ReadManifest(Directory));
    private static readonly Lazy<string> LazyMath = new(GoldenTraceRecorder.MathFingerprint);

    public static string Directory => DirectoryPath.Value;
    public static GoldenTraceSet.Manifest Manifest => LazyManifest.Value;

    /// <summary>
    /// True when this platform's C math library agrees with the recording's
    /// (<see cref="GoldenTraceRecorder.MathFingerprint"/>): only then are
    /// bit-exact traces comparable. Elsewhere the gate is a base-vs-head
    /// recording on one machine (tools/record_golden_traces.sh --base).
    /// </summary>
    public static bool MathMatchesRecording => Manifest.RecordedOn.GetValueOrDefault("math") == LazyMath.Value;

    public static List<string> Expected(string scenario, int seed)
        => GoldenTraceRecorder.ReadLines(Path.Combine(Directory, GoldenTraceRecorder.FileName(scenario, seed)));

    public static IEnumerable<object[]> ShippedScenarios()
        => GoldenTraceRecorder.ShippedScenarioIds().Select(id => new object[] { id });

    /// <summary>Record the run now and require it to equal its golden trace day by day.</summary>
    public static void AssertMatches(string scenario, int seed, ITestOutputHelper output)
    {
        var comparison = GoldenTraceComparer.Compare(Expected(scenario, seed), GoldenTraceRecorder.Record(scenario, seed));
        output.WriteLine(comparison.Summary());
        if (!MathMatchesRecording)
        {
            output.WriteLine($"not enforced here: the traces were recorded on {Manifest.RecordedOn.GetValueOrDefault("rid")} " +
                $"(math {Manifest.RecordedOn.GetValueOrDefault("math")}), this platform's math library differs " +
                $"({LazyMath.Value}); CI gates with tools/record_golden_traces.sh --base on one runner");
            return;
        }
        Assert.True(comparison.Identical, comparison.Summary() +
            "\nIf the change is intended, re-record with tools/record_golden_traces.sh --update and commit the diff report (docs/GOLDEN_TRACES.md).");
    }
}

[Trait("Category", "GoldenTrace")]
public class GoldenTraceSeed1Tests
{
    private readonly ITestOutputHelper _output;
    public GoldenTraceSeed1Tests(ITestOutputHelper output) => _output = output;

    [Theory]
    [MemberData(nameof(GoldenTraceFixtures.ShippedScenarios), MemberType = typeof(GoldenTraceFixtures))]
    public void ShippedScenario_OnSeed1_MatchesItsGoldenTraceDayByDay(string scenario)
        => GoldenTraceFixtures.AssertMatches(scenario, 1, _output);
}

[Trait("Category", "GoldenTrace")]
public class GoldenTraceSeed7Tests
{
    private readonly ITestOutputHelper _output;
    public GoldenTraceSeed7Tests(ITestOutputHelper output) => _output = output;

    [Theory]
    [MemberData(nameof(GoldenTraceFixtures.ShippedScenarios), MemberType = typeof(GoldenTraceFixtures))]
    public void ShippedScenario_OnSeed7_MatchesItsGoldenTraceDayByDay(string scenario)
        => GoldenTraceFixtures.AssertMatches(scenario, 7, _output);
}

[Trait("Category", "GoldenTrace")]
public class GoldenTraceSeed13Tests
{
    private readonly ITestOutputHelper _output;
    public GoldenTraceSeed13Tests(ITestOutputHelper output) => _output = output;

    [Theory]
    [MemberData(nameof(GoldenTraceFixtures.ShippedScenarios), MemberType = typeof(GoldenTraceFixtures))]
    public void ShippedScenario_OnSeed13_MatchesItsGoldenTraceDayByDay(string scenario)
        => GoldenTraceFixtures.AssertMatches(scenario, 13, _output);
}

/// <summary>The harness itself: coverage, byte identity, and that a 1-ulp change is caught and named.</summary>
[Trait("Category", "GoldenTrace")]
public class GoldenTraceHarnessTests
{
    private const string MutationScenario = "po3_red_sone";
    private const int MutationSeed = 7;
    private const int MutationDay = 200;

    private readonly ITestOutputHelper _output;
    public GoldenTraceHarnessTests(ITestOutputHelper output) => _output = output;

    /// <summary>F2-01 acceptance 3: every shipped scenario × {1, 7, 13} × 730 days, a line a day, a snapshot every 30 days.</summary>
    [Fact]
    public void Fixtures_CoverEveryShippedScenario_OnSeeds1_7_13_For730Days_WithASnapshotEvery30Days()
    {
        var manifest = GoldenTraceFixtures.Manifest;
        var shipped = GoldenTraceRecorder.ShippedScenarioIds();
        Assert.Equal(GoldenTraceRecorder.Format, manifest.Format);
        Assert.Equal(GoldenPlayer.Version, manifest.Player);
        Assert.Equal(730, manifest.Days);
        Assert.Equal(30, manifest.SnapshotEveryDays);
        Assert.Equal(new[] { 1, 7, 13 }, manifest.Seeds);
        Assert.Equal(shipped, manifest.Scenarios.OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(shipped.Count * 3, manifest.Files.Count);
        foreach (var scenario in shipped)
            foreach (int seed in new[] { 1, 7, 13 })
            {
                var entry = Assert.Single(manifest.Files, f => f.Scenario == scenario && f.Seed == seed);
                var lines = GoldenTraceFixtures.Expected(scenario, seed);
                Assert.Equal(entry.Lines, lines.Count);
                Assert.Equal(entry.Sha256, GoldenTraceRecorder.ContentHash(lines));
                var days = lines.Where(l => l.StartsWith("{\"d\":", StringComparison.Ordinal)).ToList();
                var snapshots = lines.Where(l => l.StartsWith("{\"snap\":", StringComparison.Ordinal)).ToList();
                Assert.Equal(731, days.Count); // days 0..730
                Assert.Equal(26, snapshots.Count); // days 0, 30, …, 720 and 730
                using var last = JsonDocument.Parse(snapshots[^1]);
                Assert.Equal(730, last.RootElement.GetProperty("snap").GetInt32());
                Assert.Equal(entry.FinalSave, last.RootElement.GetProperty("save").GetString());
            }
    }

    /// <summary>F2-01 acceptance 3: the header lists the groups a day hashes and every field left out.</summary>
    [Fact]
    public void EveryTraceHeader_ListsItsHashedGroupsAndTheExcludedFields()
    {
        foreach (var entry in GoldenTraceFixtures.Manifest.Files)
        {
            using var header = JsonDocument.Parse(GoldenTraceFixtures.Expected(entry.Scenario, entry.Seed)[0]);
            var root = header.RootElement;
            var groups = root.GetProperty("groups").EnumerateObject().Select(p => p.Name).ToList();
            foreach (var group in new[] { "pop", "cash", "ledger", "mtb", "lice", "rng", "mort" })
                Assert.Contains(group, groups);
            var excluded = root.GetProperty("excluded").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            Assert.Equal(GoldenTraceRecorder.Excluded, excluded);
            Assert.Contains(excluded, e => e.Contains("Guid", StringComparison.Ordinal));
            Assert.Equal(entry.Scenario, root.GetProperty("scenario").GetString());
            Assert.Equal(entry.Seed, root.GetProperty("seed").GetInt32());
            Assert.False(root.GetProperty("detail").GetBoolean());
        }
    }

    /// <summary>F2-01 acceptance 1, in one process: two recordings of one run give the same bytes.</summary>
    [Fact]
    public void Recording_TheSameRunTwice_GivesByteIdenticalFiles()
    {
        var first = GoldenTraceRecorder.Compress(GoldenTraceRecorder.Record("ressursrente", 13));
        var second = GoldenTraceRecorder.Compress(GoldenTraceRecorder.Record("ressursrente", 13));
        Assert.Equal(first, second);
    }

    /// <summary>
    /// F2-01 acceptance 1, across processes: the probe recording a run in its own
    /// process (as tools/record_golden_traces.sh does) writes the bytes this
    /// process records (process-random hashing would break it, review F453).
    /// </summary>
    [Fact]
    public void Recording_InAnotherProcess_GivesTheSameBytes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "golden_trace_test_" + Path.GetRandomFileName());
        try
        {
            var (exitCode, stdout, stderr) = CrossProcessDeterminismTests.RunProcess(DotnetHost(),
                $"\"{ProbeDll()}\" golden record --out \"{dir}\" --scenarios forste_sesong --seeds 13 --jobs 1",
                TimeSpan.FromSeconds(300));
            Assert.True(exitCode == 0, $"probe failed ({exitCode}): {stdout}\n{stderr}");
            var inProcess = GoldenTraceRecorder.Compress(GoldenTraceRecorder.Record("forste_sesong", 13));
            Assert.Equal(inProcess, File.ReadAllBytes(Path.Combine(dir, GoldenTraceRecorder.FileName("forste_sesong", 13))));
        }
        finally
        {
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// F2-01 acceptance 2: one cohort's weight raised by one ulp on day 200 is a
    /// failure that names the scenario, the seed, day 200 and the field: on the
    /// day with a detailed recording (pop › pen › kg and the cohort's weight),
    /// and against the committed trace (day 200, group pop; the day-210
    /// snapshot names the pen and its fields with both values).
    /// </summary>
    [Fact]
    public void OneUlpOnOneCohortWeight_OnDay200_FailsAndNamesScenarioSeedDayAndField()
    {
        string? perturbedPen = null;
        void Perturb(SimulationEngine engine, int day)
        {
            if (day != MutationDay) return;
            var (pen, population) = engine.State.SeaSites
                .SelectMany(s => s.Pens.Select(p => (Pen: s.PenOf(p)?.Id ?? "", Population: p)))
                .Where(x => x.Population.TotalCount > 0 && x.Pen.Length > 0)
                .OrderBy(x => x.Pen, StringComparer.Ordinal)
                .First();
            var cohort = population.Cohorts.First(c => c.Count > 0);
            double before = cohort.MeanWeightKg;
            cohort.MeanWeightKg = Math.BitIncrement(before);
            Assert.NotEqual(before, cohort.MeanWeightKg);
            perturbedPen = pen;
        }

        // On the day, with exact values (a detailed recording, as the diff tool makes).
        var reference = GoldenTraceRecorder.Record(MutationScenario, MutationSeed, new GoldenTraceRecorder.Options { Detail = true });
        var mutated = GoldenTraceRecorder.Record(MutationScenario, MutationSeed,
            new GoldenTraceRecorder.Options { Detail = true, AfterAdvance = Perturb });
        Assert.NotNull(perturbedPen);
        var detailed = GoldenTraceComparer.Compare(reference, mutated);
        _output.WriteLine(detailed.Summary());
        Assert.False(detailed.Identical);
        var first = detailed.First!;
        Assert.Equal((MutationScenario, MutationSeed, MutationDay, "pop"), (first.Scenario, first.Seed, first.Day, first.Group));
        Assert.StartsWith(perturbedPen + " › ", first.Field);
        Assert.Contains("c0.w", first.Field);
        Assert.Contains($"scenario {MutationScenario}, seed {MutationSeed}, day {MutationDay}, field pop › {perturbedPen}", detailed.Summary());
        // The whole save drifts from the next snapshot on, and its digest tree names changed leaves.
        Assert.Equal(210, detailed.FirstSaveDrift!.Day);
        Assert.StartsWith("changed /", detailed.FirstSaveDrift.Field);

        // Against the committed trace: the same day and group; the snapshot names the pen.
        var compact = GoldenTraceComparer.Compare(GoldenTraceFixtures.Expected(MutationScenario, MutationSeed),
            GoldenTraceRecorder.Record(MutationScenario, MutationSeed, new GoldenTraceRecorder.Options { AfterAdvance = Perturb }));
        _output.WriteLine(compact.Summary());
        if (!GoldenTraceFixtures.MathMatchesRecording) return; // the committed trace is another platform's (see AssertMatches)
        Assert.Equal((MutationDay, "pop"), (compact.First!.Day, compact.First.Group));
        Assert.Equal(210, compact.FirstAtSnapshot!.Day);
        Assert.StartsWith(perturbedPen + " › ", compact.FirstAtSnapshot.Field);
        Assert.Contains($"day {MutationDay}, field pop", compact.Summary());
    }

    /// <summary>
    /// The comparer names a cash difference as cash (a second mutation: one
    /// krone booked outside the ledger on day 300 shows in cash first).
    /// </summary>
    [Fact]
    public void OneKroneOutsideTheLedger_OnDay300_IsNamedAsCash()
    {
        void Perturb(SimulationEngine engine, int day)
        {
            if (day == 300) engine.FinanceManager.AddRevenue(1m);
        }
        var reference = GoldenTraceRecorder.Record("forste_sesong", 1, new GoldenTraceRecorder.Options { Days = 330, Detail = true });
        var mutated = GoldenTraceRecorder.Record("forste_sesong", 1, new GoldenTraceRecorder.Options { Days = 330, Detail = true, AfterAdvance = Perturb });
        var comparison = GoldenTraceComparer.Compare(reference, mutated);
        _output.WriteLine(comparison.Summary());
        Assert.Equal((300, "cash"), (comparison.First!.Day, comparison.First.Group));
        Assert.StartsWith("balance › nok", comparison.First.Field);
    }

    /// <summary>
    /// The traces exercise the player verbs, not only the passive biology: every
    /// trace orders a harvest through the S2-04 pipeline, and most restock under
    /// the capacity and pen checks and start research.
    /// </summary>
    [Fact]
    public void TheGoldenPlayer_HarvestsRestocksAndResearches_InTheRecordedTraces()
    {
        int traces = 0, stocked = 0, researched = 0;
        foreach (var entry in GoldenTraceFixtures.Manifest.Files)
        {
            var commands = GoldenTraceFixtures.Expected(entry.Scenario, entry.Seed)
                .Where(l => l.StartsWith("{\"d\":", StringComparison.Ordinal))
                .SelectMany(l =>
                {
                    using var doc = JsonDocument.Parse(l);
                    return doc.RootElement.TryGetProperty("cmd", out var cmd)
                        ? cmd.EnumerateArray().Select(c => c.GetString() ?? "").ToList()
                        : new List<string>();
                })
                .ToList();
            traces++;
            Assert.Contains(commands, c => c.StartsWith("order_harvest ", StringComparison.Ordinal) && c.EndsWith(": ok", StringComparison.Ordinal));
            if (commands.Any(c => c.StartsWith("stock ", StringComparison.Ordinal) && c.EndsWith(": ok", StringComparison.Ordinal))) stocked++;
            if (commands.Any(c => c.StartsWith("research ", StringComparison.Ordinal) && c.EndsWith(": ok", StringComparison.Ordinal))) researched++;
        }
        _output.WriteLine($"{traces} traces: {stocked} restock, {researched} start research");
        Assert.True(stocked * 3 >= traces * 2, $"only {stocked} of {traces} traces restock");
        Assert.True(researched * 3 >= traces * 2, $"only {researched} of {traces} traces start research");
    }

    [Fact]
    public void AComparisonOfATraceWithItself_IsIdentical()
    {
        var lines = GoldenTraceFixtures.Expected("forste_sesong", 1);
        var comparison = GoldenTraceComparer.Compare(lines, lines);
        Assert.True(comparison.Identical, comparison.Summary());
        Assert.Equal(731, comparison.DaysCompared);
        Assert.Equal(0, comparison.DivergentDays);
    }

    private static string DotnetHost()
        => System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : "dotnet";

    /// <summary>The probe in its own output folder (same configuration as this test assembly), as CrossProcessDeterminismTests runs it.</summary>
    private static string ProbeDll()
    {
        var match = System.Text.RegularExpressions.Regex.Match(AppContext.BaseDirectory.Replace('\\', '/'), @"/bin/([^/]+)/([^/]+)/?$");
        Assert.True(match.Success, $"unexpected test output path: {AppContext.BaseDirectory}");
        var root = new DirectoryInfo(GoldenTraceFixtures.Directory).Parent!.Parent!.Parent!.Parent!.FullName;
        string dll = Path.Combine(root, "src", "StateHashProbe", "bin", match.Groups[1].Value, match.Groups[2].Value, "StateHashProbe.dll");
        Assert.True(File.Exists(dll), $"probe not built: {dll}");
        return dll;
    }
}
