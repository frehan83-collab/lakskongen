using System.Globalization;
using LakskongenSim.Core;

namespace StateHashProbe;

/// <summary>
/// <c>StateHashProbe golden …</c>: the command line of the golden traces
/// (F2-01), driven by tools/record_golden_traces.sh.
/// <list type="bullet">
/// <item><c>record --out DIR [--commit SHA] [--scenarios a,b] [--seeds 1,7,13] [--days 730] [--jobs N] [--detail true]</c></item>
/// <item><c>diff EXPECTED_DIR ACTUAL_DIR [--report FILE]</c>: exit 0 identical, 1 different</item>
/// <item><c>save-fixture --out FILE</c>: the pre-F2 facilities save (<see cref="PreF2SaveFixture"/>)</item>
/// </list>
/// </summary>
public static class GoldenCli
{
    public const string Usage =
        "usage: StateHashProbe golden record --out DIR [--commit SHA] [--scenarios a,b] [--seeds 1,7,13] [--days 730] [--jobs N] [--detail true]\n" +
        "       StateHashProbe golden diff EXPECTED_DIR ACTUAL_DIR [--report FILE]\n" +
        "       StateHashProbe golden save-fixture --out FILE";

    public static int Run(string[] args)
    {
        if (args.Length == 0) return Fail();
        var options = ParseOptions(args.Skip(1), out var positional);
        if (options == null) return Fail();
        SimLog.MinLevel = SimLogLevel.Error;
        switch (args[0])
        {
            case "record":
                return Record(options);
            case "diff" when positional.Count == 2:
                return Diff(positional[0], positional[1], options.GetValueOrDefault("report"));
            case "save-fixture" when options.TryGetValue("out", out var file):
                PreF2SaveFixture.Write(file);
                Console.WriteLine($"wrote {file}");
                return 0;
            default:
                return Fail();
        }
    }

    private static int Record(Dictionary<string, string> options)
    {
        if (!options.TryGetValue("out", out var directory)) return Fail();
        var scenarios = options.TryGetValue("scenarios", out var s) ? GoldenTraceSet.ParseList(s) : GoldenTraceRecorder.ShippedScenarioIds().ToList();
        var seeds = options.TryGetValue("seeds", out var seedText) ? GoldenTraceSet.ParseSeeds(seedText) : GoldenTraceRecorder.Seeds.ToList();
        int days = options.TryGetValue("days", out var d) ? int.Parse(d, CultureInfo.InvariantCulture) : GoldenTraceRecorder.Days;
        int jobs = options.TryGetValue("jobs", out var j) ? int.Parse(j, CultureInfo.InvariantCulture) : Math.Min(4, Environment.ProcessorCount);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool detail = options.GetValueOrDefault("detail") == "true";
        var manifest = GoldenTraceSet.Record(directory, scenarios, seeds, days, options.GetValueOrDefault("commit", ""), jobs, detail, Console.Out);
        Console.WriteLine($"recorded {manifest.Files.Count} traces ({scenarios.Count} scenarios x {seeds.Count} seeds x {days} days) " +
            $"in {clock.Elapsed.TotalSeconds:F1} s into {directory}");
        return 0;
    }

    private static int Diff(string expected, string actual, string? reportPath)
    {
        using var report = new StringWriter(CultureInfo.InvariantCulture);
        int differing = GoldenTraceSet.Diff(expected, actual, report);
        if (reportPath != null) File.WriteAllText(reportPath, report.ToString());
        Console.Write(report.ToString());
        return differing == 0 ? 0 : 1;
    }

    /// <summary>"--key value" pairs; anything else is positional. Null when a key has no value.</summary>
    private static Dictionary<string, string>? ParseOptions(IEnumerable<string> args, out List<string> positional)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        positional = new List<string>();
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (!list[i].StartsWith("--", StringComparison.Ordinal)) { positional.Add(list[i]); continue; }
            if (i + 1 >= list.Count) return null;
            options[list[i][2..]] = list[++i];
        }
        return options;
    }

    private static int Fail()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
