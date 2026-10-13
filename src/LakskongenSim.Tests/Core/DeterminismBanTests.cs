using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// S10 determinism ban: no NEW unseeded randomness / wall-clock / unstable
/// hashes in the simulation library. Pre-existing violations are grandfathered
/// in DeterminismAllowlist.txt — fix one by threading the engine seed through,
/// then delete its line from the allowlist.
/// </summary>
public class DeterminismBanTests
{
    private static readonly Regex Banned = new(
        @"new\s+(?:System\.)?Random\s*\(\)|Random\.Shared|(?:System\.)?DateTime\.(?:Now|UtcNow)|(?:System\.)?Guid\.NewGuid\s*\(\)|HashCode\.Combine\s*\(|\.GetHashCode\s*\(\s*\)",
        RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        if (dir == null)
            throw new InvalidOperationException("Repo root (LakskongenSim.sln) not found.");
        return dir.FullName;
    }

    [Fact]
    public void NoNewNondeterminism_InSimLibrary()
    {
        string root = RepoRoot();
        var allowlist = new HashSet<string>(
            File.ReadAllLines(Path.Combine(root, "src", "LakskongenSim.Tests", "DeterminismAllowlist.txt"))
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#")));

        var current = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "LakskongenSim"), "*.cs", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file))
            {
                string s = line.Trim();
                if (s.Length == 0 || s.StartsWith("//"))
                    continue;
                foreach (Match m in Banned.Matches(line))
                {
                    string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    current.Add($"{rel} :: {m.Value} :: {s.Substring(0, Math.Min(140, s.Length))}");
                }
            }
        }

        var added = current.Except(allowlist).OrderBy(x => x).ToList();
        var removed = allowlist.Except(current).OrderBy(x => x).ToList();
        Assert.True(added.Count == 0,
            "New non-determinism in src/LakskongenSim (thread the engine seed instead):\n  "
            + string.Join("\n  ", added)
            + (removed.Count > 0
                ? "\n\nThese allowlist entries no longer match — thank you for fixing them; delete these lines from DeterminismAllowlist.txt:\n  " + string.Join("\n  ", removed)
                : ""));
    }
}
