#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using LakskongenSim.Environment.Geo;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// The embedded geo files (STAGE23_PLAN track A, A1): exactly the five data
/// files and 13 grids the readers name are embedded, each byte-identical to
/// its godot/data/geo/manifest.json entry (size and SHA-256), and a missing
/// resource throws naming its logical name, as SitePlotCatalog.LoadEmbedded does.
/// </summary>
public class GeoResourceParityTests
{
    [Fact]
    public void EveryEmbeddedGeoFile_HasItsManifestSizeAndSha256()
    {
        var files = GeoTestData.Manifest.GetProperty("files");
        Assert.Equal(18, GeoResources.All.Count);
        foreach (var (logicalName, manifestPath) in GeoResources.All)
        {
            byte[] bytes = GeoResources.ReadBytes(logicalName);
            var entry = files.GetProperty(manifestPath);
            Assert.Equal(entry.GetProperty("bytes").GetInt64(), bytes.LongLength);
            Assert.Equal(entry.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }

    [Fact]
    public void TheAssemblyEmbedsExactlyTheGeoFilesTheReadersName_AndNoImportSidecar()
    {
        var embedded = typeof(GeoResources).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(GeoResources.Prefix, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        var named = GeoResources.All.Select(r => r.LogicalName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(named, embedded);
        Assert.DoesNotContain(embedded, n => n.EndsWith(".import", StringComparison.Ordinal));
        Assert.Equal(13, embedded.Count(n => n.StartsWith(GeoResources.Prefix + "Suitability.po", StringComparison.Ordinal)));
    }

    [Fact]
    public void AMissingResource_ThrowsNamingItsLogicalName()
    {
        const string missing = "LakskongenSim.Geo.no_such_layer.geojson";
        var bytes = Assert.Throws<InvalidOperationException>(() => GeoResources.ReadBytes(missing));
        Assert.Contains(missing, bytes.Message);
        var text = Assert.Throws<InvalidOperationException>(() => GeoResources.ReadText(missing));
        Assert.Contains(missing, text.Message);
    }

    [Fact]
    public void ReadText_IsTheFilesUtf8Text()
    {
        string embedded = GeoResources.ReadText(GeoResources.SuitabilityRulesJson);
        Assert.Equal(GeoTestData.ReadGeoText("suitability_rules.json"), embedded);
        Assert.StartsWith("{", embedded);
    }
}
