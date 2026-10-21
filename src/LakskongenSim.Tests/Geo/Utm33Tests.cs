#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Environment.Geo;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// The C# UTM 33 projection (STAGE23_PLAN track A, A1): a port of
/// tests/test_geodata.py::utm33 that matches the six pyproj reference points in
/// manifest.json projection_check_25833 to 1 cm and puts every shipped point's
/// lon/lat within 2 m of the x/y the pipeline wrote.
/// </summary>
public class Utm33Tests
{
    [Fact]
    public void Forward_MatchesTheManifestsPyprojPoints_ToOneCentimetre()
    {
        var points = GeoTestData.Manifest.GetProperty("projection_check_25833").EnumerateArray().ToArray();
        Assert.Equal(6, points.Length);
        foreach (var p in points)
        {
            var (x, y) = Utm33.Forward(p.GetProperty("lon").GetDouble(), p.GetProperty("lat").GetDouble());
            Assert.True(Math.Abs(x - p.GetProperty("x").GetDouble()) < 0.01, $"{p}: x {x:F4}");
            Assert.True(Math.Abs(y - p.GetProperty("y").GetDouble()) < 0.01, $"{p}: y {y:F4}");
        }
    }

    /// <summary>
    /// tests/test_geodata.py::utm33 evaluated at the six manifest points and
    /// four far ones (Vardø, the Norskerenna, west of Shetland, the Barents Sea),
    /// printed to the micrometre. The n⁴ term of the series moves these points
    /// by up to 25 µm, so the port must carry all four Krüger terms.
    /// </summary>
    [Theory]
    [InlineData(5.3221, 60.3913, -32117.390649, 6734201.591538)]
    [InlineData(10.3951, 63.4305, 270340.022842, 7041816.279689)]
    [InlineData(14.4049, 67.2804, 474351.683822, 7462761.730269)]
    [InlineData(18.9553, 69.6492, 653421.187635, 7731721.082897)]
    [InlineData(29.7417, 70.0742, 1055939.316443, 7841845.489960)]
    [InlineData(8.0, 58.15, 88422.938937, 6466808.117850)]
    [InlineData(31.1, 70.4, 1096588.060199, 7890085.812393)]
    [InlineData(4.3, 58.6, -120186.993568, 6545165.323074)]
    [InlineData(-5.0, 60.0, -603890.105032, 6820843.170575)]
    [InlineData(40.0, 72.0, 1339957.886739, 8166566.298084)]
    public void Forward_IsThePythonReferencePort_ToTheMicrometre(double lon, double lat, double x, double y)
    {
        var (px, py) = Utm33.Forward(lon, lat);
        Assert.True(Math.Abs(px - x) < 2e-6, $"x {px:F7} vs {x:F6}");
        Assert.True(Math.Abs(py - y) < 2e-6, $"y {py:F7} vs {y:F6}");
    }

    [Fact]
    public void Forward_PutsTheCentralMeridianAtTheFalseEasting_AndTheEquatorAtZero()
    {
        var (x, y) = Utm33.Forward(15.0, 0.0);
        Assert.Equal(500000.0, x, 6);
        Assert.Equal(0.0, y, 6);
        // East of the meridian is east of 500 km, west is west; north grows north.
        Assert.True(Utm33.Forward(16.0, 65.0).X > 500000.0);
        Assert.True(Utm33.Forward(14.0, 65.0).X < 500000.0);
        Assert.True(Utm33.Forward(15.0, 66.0).Y > Utm33.Forward(15.0, 65.0).Y);
    }

    [Fact]
    public void EveryCandidatesLonLat_LandsWithinTwoMetresOfItsXY()
    {
        var all = GeoCandidates.Default.All;
        Assert.Equal(189, all.Count);
        foreach (var c in all)
        {
            var (x, y) = Utm33.Forward(c.Lon, c.Lat);
            Assert.True(Math.Abs(x - c.X) < 2 && Math.Abs(y - c.Y) < 2, $"{c.Id}: ({x:F1}, {y:F1}) vs ({c.X}, {c.Y})");
        }
    }

    [Fact]
    public void EveryLocalitysLonLat_LandsWithinTwoMetresOfItsXY()
    {
        using var doc = JsonDocument.Parse(GeoResources.ReadText(GeoResources.LocalitiesGeoJson));
        int n = 0;
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var coordinates = f.GetProperty("geometry").GetProperty("coordinates");
            var p = f.GetProperty("properties");
            var (x, y) = Utm33.Forward(coordinates[0].GetDouble(), coordinates[1].GetDouble());
            Assert.True(Math.Abs(x - p.GetProperty("x").GetInt32()) < 2 && Math.Abs(y - p.GetProperty("y").GetInt32()) < 2,
                $"locality {p.GetProperty("id").GetInt32()}");
            n++;
        }
        Assert.Equal(1781, n);
    }

    [Fact]
    public void EveryAreasLabel_ProjectsOntoItsLabel25833_WithinTheSnapCell()
    {
        // The pipeline snaps label_25833 onto a 250 m water cell's centre, then
        // writes the lon/lat of that point to 4 decimals (≈ 11 m).
        foreach (var area in GeoProductionAreas.Default.All)
        {
            var (x, y) = Utm33.Forward(area.Label.Lon, area.Label.Lat);
            Assert.True(Math.Abs(x - area.Label25833.X) < 15 && Math.Abs(y - area.Label25833.Y) < 15,
                $"{area.Code}: ({x:F1}, {y:F1}) vs {area.Label25833}");
        }
    }
}
