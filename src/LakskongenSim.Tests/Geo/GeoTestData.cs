#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// Shared, lazily read test inputs for the geo tests: the repository's
/// <c>godot/data/geo</c> files the sim does not embed (manifest.json,
/// ocean_climatology.json) and the canonical D17 brand list. Each is read
/// once per test process so the geo suite stays fast (STAGE23_PLAN §5, OD15).
/// </summary>
internal static class GeoTestData
{
    private static readonly Lazy<string> LazyRepoRoot = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    });

    private static readonly Lazy<JsonDocument> LazyManifest = new(() => JsonDocument.Parse(ReadGeoText("manifest.json")));

    private static readonly Lazy<JsonDocument> LazyClimatology = new(() => JsonDocument.Parse(ReadGeoText("ocean_climatology.json")));

    private static readonly Lazy<IReadOnlyList<string>> LazyBrands = new(ReadBrands);

    public static string RepoRoot => LazyRepoRoot.Value;

    public static string GeoDir => Path.Combine(RepoRoot, "godot", "data", "geo");

    public static string ReadGeoText(string relative) => File.ReadAllText(Path.Combine(GeoDir, relative));

    /// <summary>godot/data/geo/manifest.json.</summary>
    public static JsonElement Manifest => LazyManifest.Value.RootElement;

    /// <summary>godot/data/geo/ocean_climatology.json.</summary>
    public static JsonElement Climatology => LazyClimatology.Value.RootElement;

    /// <summary>
    /// tests/test_text_correctness.py BRANDS (the canonical D17 list), read
    /// from the Python source so the two can never drift.
    /// </summary>
    public static IReadOnlyList<string> Brands => LazyBrands.Value;

    /// <summary>True when the text holds a brand as a whole word (test_text_correctness.py BRAND_RE).</summary>
    public static bool HasBrand(string text) =>
        Brands.Any(b => Regex.IsMatch(text, @"(?<![\w-])" + Regex.Escape(b) + @"(?![\w-])"));

    private static IReadOnlyList<string> ReadBrands()
    {
        string py = File.ReadAllText(Path.Combine(RepoRoot, "tests", "test_text_correctness.py"));
        int start = py.IndexOf("BRANDS = [", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("BRANDS not found in test_text_correctness.py");
        int end = py.IndexOf(']', start);
        var brands = Regex.Matches(py[start..end], "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        if (brands.Length < 10) throw new InvalidOperationException("BRANDS parsed too short");
        return brands;
    }
}
