using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

    /// <summary>Wall-clock bound for one probe process (a 400-day career takes ~2 s).</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(120);

    private static string RunProbe(int seed, int days, bool events)
    {
        var (exitCode, stdout, stderr) = RunProcess(DotnetHost(),
            $"\"{ProbePath()}\" {seed} {days} {(events ? "true" : "false")}", ProbeTimeout);
        Assert.True(exitCode == 0, $"probe failed ({exitCode}): {stderr}");
        string hash = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault() ?? "";
        Assert.Matches("^[0-9A-F]{64}$", hash);
        return hash;
    }

    /// <summary>
    /// Run a process and collect its output, killing it (and its children) once
    /// <paramref name="timeout"/> has passed. P0-07: the old runner read stdout
    /// to the end BEFORE waiting with its timeout, so a hung probe blocked the
    /// read forever and the timeout never applied; reading stdout then stderr in
    /// sequence could also deadlock on a full stderr pipe.
    /// </summary>
    internal static (int ExitCode, string Stdout, string Stderr) RunProcess(string fileName, string arguments, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"could not start {fileName}");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(timeout);
        TimeSpan left = timeout - clock.Elapsed;
        if (!exited || !Task.WaitAll(new Task[] { stdout, stderr }, left > TimeSpan.Zero ? left : TimeSpan.Zero))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* exited between the check and the kill */ }
            process.WaitForExit();
            throw new TimeoutException(
                $"'{Path.GetFileName(fileName)} {arguments}' did not finish within {timeout.TotalSeconds:F0} s; killed");
        }
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>
    /// P0-07: the timeout is enforced. A probe that hangs for a minute is
    /// killed at a 3-second timeout and reported, not waited out.
    /// </summary>
    [Fact]
    public void ProbeRunner_KillsAHungProbe_AtItsTimeout()
    {
        var clock = Stopwatch.StartNew();
        var error = Assert.Throws<TimeoutException>(() =>
            RunProcess(DotnetHost(), $"\"{ProbePath()}\" --sleep-ms 60000", TimeSpan.FromSeconds(3)));
        clock.Stop();

        Assert.Contains("did not finish within 3 s", error.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30),
            $"the runner waited {clock.Elapsed.TotalSeconds:F1} s for a probe it should have killed at 3 s");
    }

    internal static string DotnetHost()
        => System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)
            ? host
            : "dotnet";

    internal static string ProbePath()
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
