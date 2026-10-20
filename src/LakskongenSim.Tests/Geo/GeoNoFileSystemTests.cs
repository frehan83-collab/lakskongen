#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// Source scans that keep the geo readers pure (STAGE23_PLAN track A; §5
/// "golden traces turning red by accident"): no file-system, network or RNG
/// API in the geo readers or the rival layout (the engine never touches the
/// file system; data comes from embedded resources), and no tick system, nor
/// the engine's day loop files, names a geo type, so geo data is never read on
/// a tick. Syntax-aware (Roslyn): comments and strings are not code.
/// </summary>
public class GeoNoFileSystemTests
{
    /// <summary>Identifiers that reach the file system, the network or randomness.</summary>
    private static readonly HashSet<string> BannedInReaders = new(StringComparer.Ordinal)
    {
        "File", "Directory", "FileStream", "FileInfo", "DirectoryInfo", "Path", "DriveInfo",
        "StreamReader", "StreamWriter", "FileSystemWatcher", "FileAccess", "FileMode",
        "HttpClient", "WebClient", "Socket", "TcpClient", "Dns",
        "Random", "SimRng", "SimRandom", "RandomNumberGenerator",
        "GameState", "DateTime", "DateTimeOffset", "Stopwatch",
    };

    /// <summary>Whole files of the day loop the geo tracks never touch (STAGE23_PLAN track A "Never touched").</summary>
    private static readonly string[] DayLoopFiles =
    {
        "src/LakskongenSim/Core/SimulationEngine.cs",
        "src/LakskongenSim/Core/InDayTickSystems.cs",
        "src/LakskongenSim/Core/SimulationEngine.PostTick.cs",
    };

    private static string Root => GeoTestData.RepoRoot;

    private static string Rel(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    /// <summary>The geo readers and, once they exist, the rival layout and its name generator.</summary>
    private static IReadOnlyList<string> GeoSourceFiles()
    {
        var files = Directory.EnumerateFiles(Path.Combine(Root, "src", "LakskongenSim", "Environment", "Geo"), "*.cs").ToList();
        var finance = Path.Combine(Root, "src", "LakskongenSim", "Finance");
        files.AddRange(Directory.EnumerateFiles(finance, "Rival*.cs"));
        files.AddRange(Directory.EnumerateFiles(finance, "ParodyNames*.cs"));
        return files.OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    private static SyntaxNode Parse(string path) => CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

    private static IEnumerable<string> IdentifiersIn(SyntaxNode node) =>
        node.DescendantNodes().OfType<IdentifierNameSyntax>().Select(i => i.Identifier.Text);

    [Fact]
    public void TheGeoReaders_UseNoFileNetworkClockOrRandomApi()
    {
        var files = GeoSourceFiles();
        Assert.True(files.Count >= 10, $"expected the geo readers, found {files.Count} files");
        var found = new List<string>();
        foreach (var file in files)
        {
            var root = Parse(file);
            found.AddRange(IdentifiersIn(root).Where(BannedInReaders.Contains).Distinct().Select(id => $"{Rel(file)}: {id}"));
            found.AddRange(root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Where(u => u.Name != null && (u.Name.ToString() is "System.Net" or "System.Net.Http" || u.Name.ToString().StartsWith("Godot", StringComparison.Ordinal)))
                .Select(u => $"{Rel(file)}: using {u.Name}"));
        }
        Assert.True(found.Count == 0, "geo readers must read embedded resources only:\n" + string.Join("\n", found));
    }

    /// <summary>The type names the geo sources declare (GeoCandidates, PngGrid, Utm33, ...).</summary>
    private static HashSet<string> GeoTypeNames() =>
        GeoSourceFiles().SelectMany(f => Parse(f).DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            .Select(t => t.Identifier.Text)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Members a later geo partial of the engine declares (SimulationEngine.Geo.cs), when it exists.</summary>
    private static IEnumerable<string> GeoEngineMembers()
    {
        string path = Path.Combine(Root, "src", "LakskongenSim", "Core", "SimulationEngine.Geo.cs");
        if (!File.Exists(path)) return Array.Empty<string>();
        return Parse(path).DescendantNodes().OfType<MemberDeclarationSyntax>().Select(m => m switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            PropertyDeclarationSyntax property => property.Identifier.Text,
            FieldDeclarationSyntax field => field.Declaration.Variables.First().Identifier.Text,
            _ => "",
        }).Where(n => n.Length > 0);
    }

    /// <summary>Every class that is a tick system: implements ITickSystem, or is named *TickSystem.</summary>
    private static IEnumerable<(string File, ClassDeclarationSyntax Class)> TickSystems()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src", "LakskongenSim"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Rel(file);
            if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal)) continue;
            foreach (var cls in Parse(file).DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                bool implementsTick = cls.BaseList?.Types.Any(t => t.Type.ToString() == "ITickSystem") == true;
                if (implementsTick || cls.Identifier.Text.EndsWith("TickSystem", StringComparison.Ordinal))
                    yield return (rel, cls);
            }
        }
    }

    [Fact]
    public void NoTickSystem_NorTheDayLoop_NamesAGeoType()
    {
        var banned = GeoTypeNames();
        foreach (var name in new[] { "GeoCandidates", "GeoProductionAreas", "GeoSuitability", "GeoLocalities", "PngGrid", "Utm33", "GeoResources" })
            Assert.Contains(name, banned);
        banned.UnionWith(GeoEngineMembers());
        banned.UnionWith(new[] { "RivalLayout", "ParodyNames", "ScenarioGeoAnchors", "GeoPlotCatalogs" });

        var ticks = TickSystems().ToList();
        Assert.True(ticks.Count >= 20, $"expected the engine's tick systems, found {ticks.Count}");
        var found = new List<string>();
        foreach (var (file, cls) in ticks)
            found.AddRange(IdentifiersIn(cls).Where(banned.Contains).Distinct().Select(id => $"{file} {cls.Identifier.Text}: {id}"));
        foreach (var rel in DayLoopFiles)
            found.AddRange(IdentifiersIn(Parse(Path.Combine(Root, rel))).Where(banned.Contains).Distinct().Select(id => $"{rel}: {id}"));
        Assert.True(found.Count == 0, "geo data must never be read on a tick:\n" + string.Join("\n", found));
    }
}
