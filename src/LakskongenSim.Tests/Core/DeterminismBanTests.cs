using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// S10 determinism ban: no NEW unseeded randomness / wall-clock / unstable
/// hashes in the simulation library. Pre-existing violations are grandfathered
/// in DeterminismAllowlist.txt — fix one by threading the engine seed through,
/// then delete its line from the allowlist.
/// <para>
/// P0-07: the allowlist is keyed by the symbol that holds the construct
/// (<c>path :: pattern :: Type.Member :: count</c>), not by the line's text,
/// so reformatting a line neither breaks the gate nor lets a second copy in
/// under an existing entry (the count is checked); a stale entry fails too,
/// because a dead entry would silently admit the construct again later. The
/// ban covers DateTimeOffset, DateTime.Today, Environment.TickCount and
/// RandomNumberGenerator as well (see <see cref="DeterminismScanner"/>).
/// </para>
/// </summary>
public class DeterminismBanTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        if (dir == null)
            throw new InvalidOperationException("Repo root (LakskongenSim.sln) not found.");
        return dir.FullName;
    }

    private static string AllowlistPath(string root)
        => Path.Combine(root, "src", "LakskongenSim.Tests", "DeterminismAllowlist.txt");

    /// <summary>Allowlist entries: key (path :: pattern :: symbol) → allowed count.</summary>
    internal static Dictionary<string, int> ReadAllowlist(IEnumerable<string> lines)
    {
        var allowed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            int cut = line.LastIndexOf(" :: ", StringComparison.Ordinal);
            int count = 0;
            bool parsed = cut > 0
                && int.TryParse(line[(cut + 4)..], NumberStyles.None, CultureInfo.InvariantCulture, out count);
            Assert.True(parsed && count > 0, $"allowlist line must end in ' :: <count>': {line}");
            string key = line[..cut];
            Assert.False(allowed.ContainsKey(key), $"duplicate allowlist key: {key}");
            allowed[key] = count;
        }
        return allowed;
    }

    /// <summary>Every banned construct in src/LakskongenSim, counted per key.</summary>
    private static Dictionary<string, int> ScanSimLibrary(string root)
    {
        var current = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "LakskongenSim"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/"))
                continue;
            foreach (var finding in DeterminismScanner.Scan(rel, File.ReadAllText(file)))
                current[finding.Key] = current.GetValueOrDefault(finding.Key) + 1;
        }
        // F2-04: the farm-type bridge partials are in scope too.
        foreach (var rel in DeterminismScanner.BridgeScope)
            foreach (var finding in DeterminismScanner.Scan(rel, File.ReadAllText(Path.Combine(root, rel))))
                current[finding.Key] = current.GetValueOrDefault(finding.Key) + 1;
        return current;
    }

    [Fact]
    public void TheScan_CoversTheFarmTypeBridgePartials_WithNoAllowlistEntry()
    {
        // F2-04 acceptance 6: both lanes' bridge partials (and the catalogue's)
        // are scanned, and the allowlist did not grow to let anything in.
        string root = RepoRoot();
        Assert.Contains("godot/scripts/autoload/GameBridge.LandSites.cs", DeterminismScanner.BridgeScope);
        Assert.Contains("godot/scripts/autoload/GameBridge.Offshore.cs", DeterminismScanner.BridgeScope);
        foreach (var rel in DeterminismScanner.BridgeScope)
        {
            Assert.True(File.Exists(Path.Combine(root, rel)), rel);
            Assert.Empty(DeterminismScanner.Scan(rel, File.ReadAllText(Path.Combine(root, rel))));
        }
        var allowed = ReadAllowlist(File.ReadAllLines(AllowlistPath(root)));
        Assert.DoesNotContain(allowed.Keys, k => k.StartsWith("godot/", StringComparison.Ordinal));
        Assert.Equal(73, allowed.Count); // the allowlist's size before F2-04
        // A wall clock planted in a lane's partial would be found and keyed by it.
        string planted = File.ReadAllText(Path.Combine(root, DeterminismScanner.BridgeScope[1]))
            .Replace("public partial class GameBridge : Node\n{", "public partial class GameBridge : Node\n{\n    private static long Stamp() => System.DateTime.Now.Ticks;");
        var found = Assert.Single(DeterminismScanner.Scan(DeterminismScanner.BridgeScope[1], planted));
        Assert.StartsWith("godot/scripts/autoload/GameBridge.Offshore.cs :: DateTime.Now", found.Key);
    }

    [Fact]
    public void NoNewNondeterminism_InSimLibrary()
    {
        string root = RepoRoot();
        var allowed = ReadAllowlist(File.ReadAllLines(AllowlistPath(root)));
        var current = ScanSimLibrary(root);

        var added = current
            .Where(kv => kv.Value > allowed.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key} :: {kv.Value} (allowlisted: {allowed.GetValueOrDefault(kv.Key)})")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        Assert.True(added.Count == 0,
            "New non-determinism in src/LakskongenSim (thread the engine seed instead):\n  "
            + string.Join("\n  ", added));
    }

    [Fact]
    public void Allowlist_HasNoStaleEntries()
    {
        string root = RepoRoot();
        var allowed = ReadAllowlist(File.ReadAllLines(AllowlistPath(root)));
        var current = ScanSimLibrary(root);

        var stale = allowed
            .Where(kv => current.GetValueOrDefault(kv.Key) < kv.Value)
            .Select(kv => $"{kv.Key} :: {kv.Value} (found: {current.GetValueOrDefault(kv.Key)})")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        Assert.True(stale.Count == 0,
            "These allowlist entries allow more than the code holds (thank you for fixing them); " +
            "lower the count or delete the line in DeterminismAllowlist.txt:\n  " + string.Join("\n  ", stale));
    }

    // ── The scanner itself ──────────────────────────────────────────────────

    private const string Sample = """
        using System;
        using System.Security.Cryptography;
        using static System.DateTime;

        namespace Demo
        {
            // DateTime.Now in a comment is not code; neither is "Guid.NewGuid()".
            public class Clock
            {
                private Random _shared = new();
                private readonly Random _seeded = new Random(42);
                public string Stamp { get; } = $"{DateTimeOffset.UtcNow:O}";
                public Clock() { var r = new System.Random(); }
                public long Ticks() => Environment.TickCount64 + Environment.TickCount;
                public int Roll() => RandomNumberGenerator.GetInt32(6) + global::System.Random.Shared.Next();
                public DateTime When() { Func<DateTime> f = () => DateTime.Today; return f().AddTicks(DateTimeOffset.Now.Ticks); }
                public string Id() => Guid.NewGuid().ToString() + Guid.CreateVersion7();
                public override int GetHashCode() => HashCode.Combine(1, 2) ^ "x".GetHashCode();

                public class Inner
                {
                    public static DateTime Utc = System.DateTime.UtcNow;
                }
            }
        }
        """;

    [Fact]
    public void Scanner_FindsEveryBannedConstruct_KeyedByItsSymbol_AndSkipsCommentsAndStrings()
    {
        var found = DeterminismScanner.Scan("Demo.cs", Sample)
            .Select(f => $"{f.Pattern} @ {f.Symbol}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var expected = new[]
        {
            ".GetHashCode() @ Clock.GetHashCode",
            "DateTime.Today @ Clock.When",
            "DateTime.UtcNow @ Clock.Inner.Utc",
            "DateTimeOffset.Now @ Clock.When",
            "DateTimeOffset.UtcNow @ Clock.Stamp",
            "Environment.TickCount @ Clock.Ticks",
            "Environment.TickCount64 @ Clock.Ticks",
            "Guid.CreateVersion7() @ Clock.Id",
            "Guid.NewGuid() @ Clock.Id",
            "HashCode.Combine() @ Clock.GetHashCode",
            "Random.Shared @ Clock.Roll",
            "RandomNumberGenerator @ Clock.Roll",
            "new Random() @ Clock..ctor",
            "new Random() @ Clock._shared",
            "using static DateTime @ <file>",
        }.OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, found);
    }

    [Fact]
    public void Scanner_Key_SurvivesReformatting_ButFollowsTheSymbol()
    {
        const string before = "class A { void M() { var t = DateTime.Now; } }";
        const string reformatted = "class A {\n  void M()\n  {\n    var stamp =\n      System.DateTime  .  Now;\n  }\n}";
        const string moved = "class A { void M() { } void N() { var t = DateTime.Now; } }";

        string Key(string src) => DeterminismScanner.Scan("A.cs", src).Single().Key;

        Assert.Equal("A.cs :: DateTime.Now :: A.M", Key(before));
        Assert.Equal(Key(before), Key(reformatted));
        Assert.Equal("A.cs :: DateTime.Now :: A.N", Key(moved));
    }

    [Fact]
    public void Allowlist_CountsAreChecked_NotJustKeys()
    {
        var allowed = ReadAllowlist(new[] { "# comment", "", "A.cs :: DateTime.Now :: A.M :: 1" });
        Assert.Equal(1, allowed["A.cs :: DateTime.Now :: A.M"]);

        // A second copy in an allowlisted member is still new non-determinism.
        const string twice = "class A { void M() { var a = DateTime.Now; var b = DateTime.Now; } }";
        int count = DeterminismScanner.Scan("A.cs", twice).Count(f => f.Key == "A.cs :: DateTime.Now :: A.M");
        Assert.Equal(2, count);
        Assert.True(count > allowed["A.cs :: DateTime.Now :: A.M"]);
    }
}
