using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Review F453/F1073: every other determinism gate runs both engines in ONE
/// process, where .NET's per-process hash seed is shared, so process-random
/// hashing (HashCode.Combine, string.GetHashCode) passes unnoticed while a save
/// loaded after a relaunch diverges. These tests run the same seeded career in
/// separate OS processes (src/StateHashProbe) and require identical saves.
/// </summary>
public class CrossProcessDeterminismTests
{
    private const int Seed = 29;
    private const int Days = 400; // crosses competitor quarters and the year-end close

    [Fact]
    public void SameSeed_InSeparateProcesses_WritesIdenticalSaves()
    {
        string inProcess = StateHashProbe.StateHash.Compute(Seed, Days, events: true);
        string first = RunProbe(Seed, Days, events: true);
        string second = RunProbe(Seed, Days, events: true);

        Assert.Equal(inProcess, first);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// Pins the zone current noise to exact values: a process-random hash
    /// (the old HashCode.Combine(day, Id)) cannot reproduce them.
    /// </summary>
    [Fact]
    public void ZoneCurrentNoise_IsPinnedAcrossProcesses()
    {
        var zone = new Zone("zone-1");
        zone.UpdateCurrents(100);
        Assert.Equal(PinnedZone1Day100CurrentSpeed, zone.CurrentSpeed, precision: 12);
        Assert.Equal(PinnedStableHashZone1, StableHash.Of("zone-1"));
    }

    private const double PinnedZone1Day100CurrentSpeed = 0.1694483165605167;
    private const ulong PinnedStableHashZone1 = 3552879084099539837UL; // FNV-1a 64 of "zone-1"

    private static string RunProbe(int seed, int days, bool events)
    {
        string dll = ProbePath();
        var psi = new ProcessStartInfo(DotnetHost(), $"\"{dll}\" {seed} {days} {(events ? "true" : "false")}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("could not start probe");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "probe timed out");
        Assert.True(process.ExitCode == 0, $"probe failed ({process.ExitCode}): {stderr}");
        string hash = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault() ?? "";
        Assert.Matches("^[0-9A-F]{64}$", hash);
        return hash;
    }

    private static string DotnetHost()
        => System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)
            ? host
            : "dotnet";

    private static string ProbePath()
    {
        // Same configuration as this test assembly (…/bin/<Configuration>/<tfm>/).
        var match = Regex.Match(AppContext.BaseDirectory.Replace('\\', '/'), @"/bin/([^/]+)/([^/]+)/?$");
        Assert.True(match.Success, $"unexpected test output path: {AppContext.BaseDirectory}");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string dll = Path.Combine(dir!.FullName, "src", "StateHashProbe", "bin",
            match.Groups[1].Value, match.Groups[2].Value, "StateHashProbe.dll");
        Assert.True(File.Exists(dll), $"probe not built: {dll}");
        return dll;
    }
}
