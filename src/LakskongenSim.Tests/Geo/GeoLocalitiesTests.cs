#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using LakskongenSim.Environment.Geo;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// The aquaculture localities (STAGE23_PLAN track A, A1): 1,781 records sorted
/// by id, the salmonid view, the 994 producing sea grow-out sites holding
/// 3,722,295 t, the offshore site 45196, and parse errors naming the locality.
/// </summary>
public class GeoLocalitiesTests
{
    private static GeoLocalities Localities => GeoLocalities.Default;

    [Fact]
    public void All_Is1781Records_SortedById_WithUniqueIds()
    {
        Assert.Equal(1781, Localities.All.Count);
        var ids = Localities.All.Select(l => l.Id).ToArray();
        Assert.Equal(ids.OrderBy(i => i), ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(Localities.All, l =>
        {
            Assert.InRange(l.Po, 0, 13);
            Assert.False(string.IsNullOrWhiteSpace(l.Name));
            Assert.False(string.IsNullOrWhiteSpace(l.Municipality));
            Assert.True(l.MtbT == null || l.MtbT >= 0, l.Id.ToString());
        });
        Assert.Equal(21, Localities.All.Count(l => l.Po == 0));
    }

    [Fact]
    public void TheViews_AreSalmonid_AndProducingSeaGrowout()
    {
        Assert.Equal(1380, Localities.Salmonid.Count);
        Assert.All(Localities.Salmonid, l => Assert.EndsWith("_salmonid", l.Kind));
        Assert.Equal((1070, 309, 1), (
            Localities.Salmonid.Count(l => l.Kind == "sea_salmonid"),
            Localities.Salmonid.Count(l => l.Kind == "land_salmonid"),
            Localities.Salmonid.Count(l => l.Kind == "offshore_salmonid")));

        var producing = Localities.SeaGrowoutProducing;
        Assert.Equal(994, producing.Count);
        Assert.All(producing, l => Assert.True(l.Kind == "sea_salmonid" && l.Stage == "growout" && !l.HarvestPen && l.MtbT > 0, l.Id.ToString()));
        Assert.Equal(3_722_295.0, producing.Sum(l => l.MtbT!.Value), 3);
        Assert.Equal(producing.Select(l => l.Id).OrderBy(i => i), producing.Select(l => l.Id));
        Assert.Equal(44, Localities.All.Count(l => l.HarvestPen));
    }

    [Fact]
    public void TryGet_FindsTheOffshoreSite45196()
    {
        Assert.True(Localities.TryGet(45196, out var offshore));
        Assert.Equal(("offshore_salmonid", "growout", 0, 19000.0), (offshore!.Kind, offshore.Stage, offshore.Po, offshore.MtbT));
        Assert.True(offshore.IsSalmonid);
        Assert.False(offshore.IsSeaGrowoutProducing);
        Assert.False(Localities.TryGet(1, out _));
        Assert.True(Localities.TryGet(10029, out var first));
        Assert.Equal(("sea_salmonid", 3, -54724, 6622295), (first!.Kind, first.Po, first.X, first.Y));
    }

    [Fact]
    public void ANonTonneCapacity_HasNoMtb()
    {
        Assert.Equal(1348, Localities.All.Count(l => l.MtbT != null));
        Assert.Contains(Localities.All, l => l.MtbT == null && l.IsSalmonid);
    }

    private static JsonObject File() => JsonNode.Parse(GeoTestData.ReadGeoText("localities.geojson"))!.AsObject();

    private static JsonObject Props(JsonObject file, int id) =>
        file["features"]!.AsArray().Select(f => f!["properties"]!.AsObject()).First(p => (int)p["id"]! == id);

    [Theory]
    [InlineData("kind", "\"pond_salmonid\"", "field 'kind'")]
    [InlineData("po", "14", "field 'po'")]
    [InlineData("mtb_t", "-5.0", "field 'mtb_t'")]
    [InlineData("x", "\"east\"", "field 'x'")]
    [InlineData("municipality", "\"\"", "field 'municipality'")]
    public void Parse_RefusesADefect_NamingTheLocalityAndTheField(string field, string value, string expected)
    {
        var file = File();
        Props(file, 10029)[field] = JsonNode.Parse(value);
        var e = Assert.Throws<InvalidDataException>(() => GeoLocalities.Parse(file.ToJsonString()));
        Assert.Contains("locality 10029", e.Message);
        Assert.Contains(expected, e.Message);
    }

    [Fact]
    public void Parse_RefusesADuplicateId()
    {
        var file = File();
        Props(file, 45196)["id"] = 10029;
        var e = Assert.Throws<InvalidDataException>(() => GeoLocalities.Parse(file.ToJsonString()));
        Assert.Contains("locality 10029: field 'id' appears twice", e.Message);
    }
}
