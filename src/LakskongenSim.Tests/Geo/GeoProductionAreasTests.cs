#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// The production-area polygons (STAGE23_PLAN track A, A1): names equal to
/// ProductionAreas, the source's traffic lights, point-in-polygon for every
/// candidate and locality (exactly the 8 enumerated localities disagree with
/// the registry's area), the projected test equal to the lon/lat test, and the
/// even-odd rule on synthetic MultiPolygons with holes.
/// </summary>
public class GeoProductionAreasTests
{
    private static GeoProductionAreas Areas => GeoProductionAreas.Default;

    /// <summary>
    /// The localities whose registry area differs from the polygon test
    /// (measured 2026-10-20 with test_geodata.py's point_in_geom): all of them
    /// lie outside every regional polygon while the registry names an area.
    /// </summary>
    private static readonly int[] PolygonDisagreements = { 10388, 11063, 13131, 15316, 32517, 34237, 45152, 45153 };

    [Fact]
    public void TheThirteenAreas_CarryProductionAreasNamesAndCodes()
    {
        Assert.Equal(13, Areas.All.Count);
        for (int po = 1; po <= 13; po++)
        {
            var area = Areas.Get(po);
            Assert.Equal(po, area.Number);
            Assert.Equal("PO" + po, area.Code);
            Assert.Equal(ProductionAreas.Get(po).Name, area.Name);
            Assert.Equal(ProductionAreas.Get(po).Code, area.Code);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", area.Tint);
            Assert.True(area.SeaAreaKm2 > 0 && area.SeaAreaKm2 <= area.AreaKm2, area.Code);
            var bbox = area.Bbox25833;
            Assert.Equal(4, bbox.Length);
            Assert.True(bbox[0] < bbox[2] && bbox[1] < bbox[3], area.Code);
            Assert.InRange(area.Label25833.X, bbox[0], bbox[2]);
            Assert.InRange(area.Label25833.Y, bbox[1], bbox[3]);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => Areas.Get(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Areas.Get(14));
    }

    [Fact]
    public void TheOfficialLights_ArePo1Po12Po13Green_Po3Red_TheRestYellow()
    {
        var expected = Enumerable.Range(1, 13).Select(po => po switch
        {
            1 or 12 or 13 => "green",
            3 => "red",
            _ => "yellow",
        });
        Assert.Equal(expected, Areas.All.Select(a => a.OfficialTrafficLight));
    }

    [Fact]
    public void Bbox25833_IsACopy_SoTheSharedAreaCannotBeMutated()
    {
        var area = Areas.Get(3);
        var first = area.Bbox25833;
        first[0] = 0;
        Assert.NotEqual(0, area.Bbox25833[0]);
    }

    [Fact]
    public void EveryAreasLabel_LiesInItsOwnPolygon()
    {
        foreach (var area in Areas.All)
            Assert.Equal(area.Number, Areas.AreaOf(area.Label.Lon, area.Label.Lat));
    }

    [Fact]
    public void AreaOf_PutsEveryOpenPenInItsOwnArea_AndEveryOffshoreCandidateInNone()
    {
        var candidates = GeoCandidates.Default.All;
        Assert.Equal(156, candidates.Count(c => c.IsOpenPen));
        foreach (var c in candidates.Where(c => c.IsOpenPen))
            Assert.True(c.Po == Areas.AreaOf(c.Lon, c.Lat), $"{c.Id} in PO{Areas.AreaOf(c.Lon, c.Lat)}");
        Assert.Equal(33, candidates.Count(c => c.IsOffshore));
        foreach (var c in candidates.Where(c => c.IsOffshore))
            Assert.True(Areas.AreaOf(c.Lon, c.Lat) == 0, $"{c.Id} lies in PO{Areas.AreaOf(c.Lon, c.Lat)}");
    }

    [Fact]
    public void ExactlyTheEightEnumeratedLocalities_DisagreeWithTheirPolygon()
    {
        using var doc = JsonDocument.Parse(GeoResources.ReadText(GeoResources.LocalitiesGeoJson));
        var disagree = new List<int>();
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var coordinates = f.GetProperty("geometry").GetProperty("coordinates");
            var p = f.GetProperty("properties");
            int registry = p.GetProperty("po").ValueKind == JsonValueKind.Null ? 0 : p.GetProperty("po").GetInt32();
            if (Areas.AreaOf(coordinates[0].GetDouble(), coordinates[1].GetDouble()) != registry)
                disagree.Add(p.GetProperty("id").GetInt32());
        }
        Assert.Equal(PolygonDisagreements, disagree.OrderBy(id => id));
        // Each of them lies in no polygon while the registry names an area.
        foreach (int id in PolygonDisagreements)
        {
            Assert.True(GeoLocalities.Default.TryGet(id, out var l));
            Assert.NotEqual(0, l!.Po);
        }
    }

    [Fact]
    public void AreaAtUtm_EqualsAreaOf_AtEveryCandidateAndLocality()
    {
        foreach (var c in GeoCandidates.Default.All)
            Assert.True(Areas.AreaOf(c.Lon, c.Lat) == Areas.AreaAtUtm(c.X, c.Y), c.Id);
        using var doc = JsonDocument.Parse(GeoResources.ReadText(GeoResources.LocalitiesGeoJson));
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var coordinates = f.GetProperty("geometry").GetProperty("coordinates");
            var p = f.GetProperty("properties");
            Assert.True(
                Areas.AreaOf(coordinates[0].GetDouble(), coordinates[1].GetDouble())
                    == Areas.AreaAtUtm(p.GetProperty("x").GetInt32(), p.GetProperty("y").GetInt32()),
                $"locality {p.GetProperty("id").GetInt32()}");
        }
    }

    [Fact]
    public void AreaOf_IsZero_FarFromNorway()
    {
        Assert.Equal(0, Areas.AreaOf(0.0, 0.0));
        Assert.Equal(0, Areas.AreaOf(-30.0, 65.0));
        Assert.Equal(0, Areas.AreaAtUtm(0, 0));
    }

    // ── The even-odd rule on synthetic shapes ─────────────────────────────

    private static GeoMultiPolygon Shape(string geometryJson)
    {
        using var doc = JsonDocument.Parse(geometryJson);
        return GeoMultiPolygon.FromGeoJson(doc.RootElement, "synthetic");
    }

    private const string Square = "[[0,0],[10,0],[10,10],[0,10],[0,0]]";
    private const string Hole = "[[3,3],[7,3],[7,7],[3,7],[3,3]]";
    private const string FarSquare = "[[20,0],[30,0],[30,10],[20,10],[20,0]]";
    private const string Island = "[[4,4],[6,4],[6,6],[4,6],[4,4]]";

    [Fact]
    public void APolygonWithAHole_ExcludesTheHole()
    {
        var shape = Shape($"{{\"type\":\"Polygon\",\"coordinates\":[{Square},{Hole}]}}");
        Assert.Equal((1, 1), (shape.PolygonCount, shape.HoleCount));
        Assert.True(shape.Contains(1, 1));
        Assert.True(shape.Contains(9, 5));
        Assert.False(shape.Contains(5, 5));
        Assert.False(shape.Contains(11, 5));
        Assert.False(shape.Contains(-0.5, 5));
    }

    [Fact]
    public void AMultiPolygon_HoldsEachPart_AndAnIslandInsideAHole()
    {
        // Part 1: a square with a hole; part 2: a far square; part 3: an island in the hole.
        var shape = Shape($"{{\"type\":\"MultiPolygon\",\"coordinates\":[[{Square},{Hole}],[{FarSquare}],[{Island}]]}}");
        Assert.Equal((3, 1), (shape.PolygonCount, shape.HoleCount));
        Assert.True(shape.Contains(1, 1));
        Assert.True(shape.Contains(25, 5));
        Assert.False(shape.Contains(3.5, 3.5));   // in the hole
        Assert.True(shape.Contains(5, 5));        // on the island inside the hole
        Assert.False(shape.Contains(15, 5));      // between the parts
    }

    [Fact]
    public void AConcaveRing_FollowsTheEvenOddRule()
    {
        // A "U": the notch between the arms is outside.
        var shape = Shape("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[9,0],[9,9],[6,9],[6,3],[3,3],[3,9],[0,9],[0,0]]]}");
        Assert.True(shape.Contains(1, 8));
        Assert.True(shape.Contains(8, 8));
        Assert.False(shape.Contains(4.5, 6));
        Assert.True(shape.Contains(4.5, 1));
    }

    [Fact]
    public void Map_ProjectsEveryVertex()
    {
        var shape = Shape($"{{\"type\":\"Polygon\",\"coordinates\":[{Square},{Hole}]}}");
        var moved = shape.Map((x, y) => (x + 100, y * 2));
        Assert.True(moved.Contains(101, 2));
        Assert.False(moved.Contains(105, 10));
        Assert.Equal((100.0, 0.0, 110.0, 20.0), (moved.MinX, moved.MinY, moved.MaxX, moved.MaxY));
    }

    [Fact]
    public void ABadGeometry_ThrowsNamingTheProblem()
    {
        var point = Assert.Throws<InvalidDataException>(() => Shape("{\"type\":\"Point\",\"coordinates\":[1,2]}"));
        Assert.Contains("Point", point.Message);
        var tiny = Assert.Throws<InvalidDataException>(() => Shape("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[0,0]]]}"));
        Assert.Contains("fewer than 4", tiny.Message);
    }

    // ── Parse over a synthetic 13-area file ───────────────────────────────

    /// <summary>
    /// A valid 13-area GeoJSON: area n is the square (n·10 .. n·10+8, 0 .. 8)
    /// in lon/lat; area 5 is a MultiPolygon with a hole and a second part.
    /// </summary>
    private static JsonObject SyntheticFile()
    {
        var features = new JsonArray();
        for (int po = 1; po <= 13; po++)
        {
            double x0 = po * 10;
            string ring(double ox, double oy, double size) => string.Create(CultureInfo.InvariantCulture,
                $"[[{ox},{oy}],[{ox + size},{oy}],[{ox + size},{oy + size}],[{ox},{oy + size}],[{ox},{oy}]]");
            string geometry = po == 5
                ? $"{{\"type\":\"MultiPolygon\",\"coordinates\":[[{ring(x0, 0, 8)},{ring(x0 + 2, 2, 4)}],[{ring(x0, 20, 2)}]]}}"
                : $"{{\"type\":\"Polygon\",\"coordinates\":[{ring(x0, 0, 8)}]}}";
            features.Add(new JsonObject
            {
                ["type"] = "Feature",
                ["geometry"] = JsonNode.Parse(geometry),
                ["properties"] = new JsonObject
                {
                    ["id"] = po,
                    ["code"] = "PO" + po,
                    ["name"] = ProductionAreas.Get(po).Name,
                    ["traffic_light"] = "yellow",
                    ["tint"] = "#CFE3EC",
                    ["bbox_25833"] = new JsonArray(0, 0, 10, 10),
                    ["label"] = new JsonArray(x0 + 1, 1.0),
                    ["label_25833"] = new JsonArray(5, 5),
                    ["area_km2"] = 100.0,
                    ["sea_area_km2"] = 40.0,
                },
            });
        }
        return new JsonObject { ["type"] = "FeatureCollection", ["features"] = features };
    }

    [Fact]
    public void Parse_ReadsMultiPolygonsWithHoles_LowestAreaFirst()
    {
        var areas = GeoProductionAreas.Parse(SyntheticFile().ToJsonString());
        Assert.Equal(1, areas.AreaOf(11, 1));
        Assert.Equal(5, areas.AreaOf(51, 1));
        Assert.Equal(0, areas.AreaOf(54, 4));      // area 5's hole
        Assert.Equal(5, areas.AreaOf(51, 21));     // area 5's second part
        Assert.Equal(13, areas.AreaOf(137, 7));
        Assert.Equal(0, areas.AreaOf(9, 1));
    }

    [Theory]
    [InlineData("name", "\"Ryfylket\"", "field 'name'")]
    [InlineData("code", "\"PO02\"", "field 'code'")]
    [InlineData("traffic_light", "\"amber\"", "field 'traffic_light'")]
    [InlineData("tint", "\"blue\"", "field 'tint'")]
    [InlineData("bbox_25833", "[1, 2, 3]", "field 'bbox_25833'")]
    [InlineData("sea_area_km2", "500.0", "field 'sea_area_km2'")]
    [InlineData("id", "7", "field 'id'")]
    // A mistyped integer array is a data defect naming its field, not a JsonElement InvalidOperationException.
    [InlineData("bbox_25833", "[\"0\", 0, 10, 10]", "field 'bbox_25833'")]
    [InlineData("bbox_25833", "[0, 0, 10.5, 10]", "field 'bbox_25833'")]
    [InlineData("label_25833", "[\"5\", 5]", "field 'label_25833'")]
    [InlineData("label_25833", "[5, null]", "field 'label_25833'")]
    public void Parse_RefusesADefect_NamingTheAreaAndTheField(string field, string value, string expected)
    {
        var file = SyntheticFile();
        file["features"]![1]!["properties"]![field] = JsonNode.Parse(value);
        var e = Assert.Throws<InvalidDataException>(() => GeoProductionAreas.Parse(file.ToJsonString()));
        Assert.Contains("area 2", e.Message);
        Assert.Contains(expected, e.Message);
    }

    [Fact]
    public void Parse_RefusesAMissingArea()
    {
        var file = SyntheticFile();
        file["features"]!.AsArray().RemoveAt(12);
        var e = Assert.Throws<InvalidDataException>(() => GeoProductionAreas.Parse(file.ToJsonString()));
        Assert.Contains("expected 13 areas", e.Message);
    }

    [Fact]
    public void TheEmbeddedFile_IsTheGamesDataFile()
    {
        var fromFile = GeoProductionAreas.Parse(GeoTestData.ReadGeoText("production_areas.regional.geojson"));
        for (int po = 1; po <= 13; po++)
        {
            var a = fromFile.Get(po);
            var b = Areas.Get(po);
            Assert.Equal((a.Name, a.OfficialTrafficLight, a.Tint, a.Label, a.Label25833, a.AreaKm2, a.SeaAreaKm2),
                (b.Name, b.OfficialTrafficLight, b.Tint, b.Label, b.Label25833, b.AreaKm2, b.SeaAreaKm2));
            Assert.Equal(a.Bbox25833, b.Bbox25833);
        }
        Assert.True(Encoding.UTF8.GetByteCount(GeoTestData.ReadGeoText("production_areas.regional.geojson")) > 1000);
    }
}
