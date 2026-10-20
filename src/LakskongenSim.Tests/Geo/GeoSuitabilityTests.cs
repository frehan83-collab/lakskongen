#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LakskongenSim.Environment.Geo;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// The suitability grids and the published rules (STAGE23_PLAN track A, A1):
/// all 13 grids decode to suitability.json's size, every open-pen candidate's
/// cell is legal and scored, the sampler's row and column follow
/// test_geodata.py, each grid decodes once per process, and the rules carry
/// the published weights and cost formulas.
/// </summary>
public class GeoSuitabilityTests
{
    private static GeoSuitability Suitability => GeoSuitability.Default;
    private static GeoSuitabilityRules Rules => GeoSuitabilityRules.Default;

    [Fact]
    public void AllThirteenGrids_DecodeToTheirPublishedSize_AsRgb()
    {
        using var doc = JsonDocument.Parse(GeoTestData.ReadGeoText("suitability.json"));
        var codes = Suitability.ReasonCodes.Keys.ToHashSet();
        for (int po = 1; po <= 13; po++)
        {
            var published = doc.RootElement.GetProperty("grids").GetProperty(po.ToString());
            var grid = Suitability.GridOf(po);
            var image = Suitability.Image(po);
            Assert.Equal((published.GetProperty("width").GetInt32(), published.GetProperty("height").GetInt32()), (grid.Width, grid.Height));
            Assert.Equal((grid.Width, grid.Height, 3, (byte)2), (image.Width, image.Height, image.Channels, image.ColourType));
            Assert.Equal(500, grid.ResM);
            Assert.Equal(published.GetProperty("origin_25833")[0].GetInt64(), grid.OriginX);
            Assert.Equal(published.GetProperty("origin_25833")[1].GetInt64(), grid.OriginY);
            // Every reason code in the grid is a published one (every 7th cell, as test_geodata does).
            for (int r = 0; r < image.Height; r += 7)
                for (int c = 0; c < image.Width; c += 7)
                    Assert.Contains((int)image.Sample(c, r, 1), codes);
        }
    }

    [Fact]
    public void EveryOpenPenCandidatesCell_IsLegalScoredAndDeep()
    {
        foreach (var c in GeoCandidates.Default.All.Where(c => c.IsOpenPen))
        {
            var cell = Suitability.CellAt(c.Po, c.X, c.Y);
            Assert.True(cell.InGrid && cell.Legal && cell.ScoreByte > 0, $"{c.Id}: {cell}");
            Assert.InRange(cell.Reason, GeoSuitability.FirstScoredReason, (byte)24);
            Assert.True(cell.DepthM > 0, $"{c.Id}: depth {cell.DepthM}");
            // A 500 m cell shows its best 250 m sub-cell: never below the candidate by more than a byte step.
            Assert.True(cell.Score >= c.Score - 1.0 / 254, $"{c.Id}: cell {cell.Score:F4} < candidate {c.Score}");
        }
    }

    [Fact]
    public void OffshoreCandidates_SampleOutsideTheirAreasGrid_OrOutsideThePo()
    {
        foreach (var c in GeoCandidates.Default.Offshore)
        {
            var cell = Suitability.CellAt(c.Po, c.X, c.Y);
            Assert.True(!cell.InGrid || (!cell.Legal && cell.Reason == 2), $"{c.Id}: {cell}");
        }
    }

    [Fact]
    public void CellAt_UsesTheNorthWestOrigin_FloorDivision_AndRowZeroNorth()
    {
        var g = Suitability.GridOf(3);
        var image = Suitability.Image(3);
        // The origin corner is cell (0, 0); one metre west or north of it is off the grid.
        Assert.Equal(image.Sample(0, 0, 1), Suitability.CellAt(3, g.OriginX, g.OriginY).Reason);
        Assert.True(Suitability.CellAt(3, g.OriginX, g.OriginY).InGrid);
        Assert.False(Suitability.CellAt(3, g.OriginX - 1, g.OriginY).InGrid);
        Assert.False(Suitability.CellAt(3, g.OriginX, g.OriginY + 1).InGrid);
        // 499 m east and 499 m south is still cell (0, 0); 500 m is the next one.
        Assert.Equal(Suitability.CellAt(3, g.OriginX, g.OriginY), Suitability.CellAt(3, g.OriginX + 499, g.OriginY - 499));
        // The south-east corner cell, and one cell past it.
        long eastEdge = g.OriginX + (long)g.Width * g.ResM, southEdge = g.OriginY - (long)g.Height * g.ResM;
        Assert.True(Suitability.CellAt(3, eastEdge - 1, southEdge + 1).InGrid);
        Assert.False(Suitability.CellAt(3, eastEdge, southEdge + 1).InGrid);
        Assert.False(Suitability.CellAt(3, eastEdge - 1, southEdge).InGrid);
        // A candidate's cell is (floor((x − ox) / 500), floor((oy − y) / 500)).
        var c = GeoCandidates.Default.Find("PO03-01")!;
        int col = (int)Math.Floor((c.X - g.OriginX) / 500.0), row = (int)Math.Floor((g.OriginY - c.Y) / 500.0);
        var cell = Suitability.CellAt(3, c.X, c.Y);
        Assert.Equal((image.Sample(col, row, 0), image.Sample(col, row, 1), image.Sample(col, row, 2) * 4.0),
            (cell.ScoreByte, cell.Reason, cell.DepthM));
        Assert.Same(GeoSuitability.Outside, Suitability.CellAt(3, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Suitability.CellAt(0, c.X, c.Y));
    }

    [Fact]
    public void TheScoreByte_EncodesTheScore()
    {
        Assert.Equal(0.0, new SuitabilityCell(true, 0, 1, 0, false).Score);
        Assert.Equal(0.0, new SuitabilityCell(true, 1, 16, 40, true).Score);
        Assert.Equal(1.0, new SuitabilityCell(true, 255, 24, 40, true).Score);
        Assert.Equal(127.0 / 254, new SuitabilityCell(true, 128, 19, 40, true).Score, 12);
    }

    [Fact]
    public void EachGrid_DecodesOncePerProcess()
    {
        // A private instance over the embedded files, counting its own decodes.
        int reads = 0;
        var own = GeoSuitability.Parse(GeoResources.ReadText(GeoResources.SuitabilityJson), name =>
        {
            System.Threading.Interlocked.Increment(ref reads);
            return GeoResources.ReadBytes(name);
        });
        Assert.Equal(0, own.DecodedGridCount);
        var c = GeoCandidates.Default.Find("PO06-02")!;
        for (int i = 0; i < 50; i++) Assert.True(own.CellAt(6, c.X, c.Y).Legal);
        Assert.Equal((1, 1), (own.DecodedGridCount, reads));
        own.CellAt(6, 0, 0);       // off the grid: no further decode
        own.CellAt(7, 0, 0);       // off PO7's grid: PO7 is never decoded
        Assert.Equal((1, 1), (own.DecodedGridCount, reads));
        Assert.InRange(Suitability.DecodedGridCount, 0, 13);
    }

    [Fact]
    public void Parse_RefusesADefect_NamingTheField()
    {
        var file = JsonNode.Parse(GeoTestData.ReadGeoText("suitability.json"))!.AsObject();
        file["grids"]!["4"]!["file"] = "suitability/po05.png";
        var e = Assert.Throws<InvalidDataException>(() => GeoSuitability.Parse(file.ToJsonString(), GeoResources.ReadBytes));
        Assert.Contains("grid 4", e.Message);
        Assert.Contains("field 'file'", e.Message);

        var north = JsonNode.Parse(GeoTestData.ReadGeoText("suitability.json"))!.AsObject();
        north["row0"] = "south";
        Assert.Contains("field 'row0'", Assert.Throws<InvalidDataException>(() => GeoSuitability.Parse(north.ToJsonString(), GeoResources.ReadBytes)).Message);
    }

    /// <summary>
    /// A mistyped origin or reason-code value is a data defect naming its field
    /// (InvalidDataException), never a JsonElement InvalidOperationException.
    /// </summary>
    [Theory]
    [InlineData("[\"-120000\", 6650000]")]
    [InlineData("[-120000.5, 6650000]")]
    [InlineData("[-120000, true]")]
    public void Parse_RefusesAMistypedOrigin_NamingTheGridAndTheField(string origin)
    {
        var file = JsonNode.Parse(GeoTestData.ReadGeoText("suitability.json"))!.AsObject();
        file["grids"]!["7"]!["origin_25833"] = JsonNode.Parse(origin);
        var e = Assert.Throws<InvalidDataException>(() => GeoSuitability.Parse(file.ToJsonString(), GeoResources.ReadBytes));
        Assert.Contains("grid 7", e.Message);
        Assert.Contains("field 'origin_25833'", e.Message);
    }

    [Fact]
    public void Parse_RefusesAReasonCodeThatIsNotAName_NamingIt()
    {
        var file = JsonNode.Parse(GeoTestData.ReadGeoText("suitability.json"))!.AsObject();
        file["codes"]!["4"] = 4;
        var e = Assert.Throws<InvalidDataException>(() => GeoSuitability.Parse(file.ToJsonString(), GeoResources.ReadBytes));
        Assert.Contains("field 'codes.4'", e.Message);
    }

    [Fact]
    public void AGridOfTheWrongSize_IsRefusedOnItsFirstRead_NamingIt()
    {
        var file = JsonNode.Parse(GeoTestData.ReadGeoText("suitability.json"))!.AsObject();
        file["grids"]!["2"]!["width"] = 359;
        var own = GeoSuitability.Parse(file.ToJsonString(), GeoResources.ReadBytes);
        var c = GeoCandidates.Default.Find("PO02-03")!;
        var e = Assert.Throws<InvalidDataException>(() => own.CellAt(2, c.X, c.Y));
        Assert.Contains("po02.png", e.Message);
    }

    // ── The published rules ───────────────────────────────────────────────

    [Fact]
    public void EffectiveWeights_AreThePublishedOnes()
    {
        Assert.Equal(new Dictionary<string, double>
        {
            ["current"] = 0.15, ["depth"] = 0.3, ["exchange"] = 0.25, ["farm_distance"] = 0.25,
            ["oxygen"] = 0.1, ["temperature"] = 0.2, ["waves"] = 0.2,
        }, Rules.EffectiveWeights.ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal(1.45, Rules.WeightSum, 12);
        Assert.Equal(new[] { "depth", "exchange", "farm_distance", "temperature", "current", "waves", "oxygen" }, Rules.ComponentOrder);
        Assert.Equal((0.6, 5000.0), (Rules.RiverMinFactor, Rules.RiverRadiusM));
        Assert.Equal((2.0, 8.0, 16.0, 20.0), Rules.TemperatureRampC);
        Assert.Equal(1.0, Rules.OffshoreStageFactors["kan_utlyses_2026"]);
        Assert.Equal(0.85, Rules.OffshoreStageFactors["tilradning_2019"]);
    }

    [Fact]
    public void ComponentCost_IsTheWeightedShareOfTheMissingValue()
    {
        var c = GeoCandidates.Default.Find("PO01-01")!;   // temperature 0.752, the rest 1, river factor 1
        Assert.Equal(0.2 / 1.45 * (1 - 0.752) * 1.0, Rules.ComponentCost(c, "temperature"), 12);
        Assert.Equal(0.0, Rules.ComponentCost(c, "depth"), 12);
        Assert.Equal(0.0, Rules.ComponentCost(c, GeoSuitabilityRules.RiverTerm), 12);
        Assert.Equal(0.0, Rules.ComponentCost(c, GeoSuitabilityRules.StageTerm), 12);
        Assert.Equal("temperature", Rules.BindingOf(c));
        var e = Assert.Throws<ArgumentException>(() => Rules.ComponentCost(c, "salinity"));
        Assert.Contains("salinity", e.Message);

        // A river factor below 1 costs base × (1 − factor) and scales every component's cost.
        var nearRiver = GeoCandidates.Default.All.Where(x => x.IsOpenPen).OrderBy(x => x.RiverFactor).First();
        Assert.True(nearRiver.RiverFactor < 1, nearRiver.Id);
        Assert.Equal(Rules.BaseScore(nearRiver) * (1 - nearRiver.RiverFactor), Rules.ComponentCost(nearRiver, "river"), 12);
        Assert.Equal(Rules.BaseScore(nearRiver) * nearRiver.RiverFactor, Rules.Score(nearRiver), 12);

        var hth = GeoCandidates.Default.Find("HTH13-01")!;   // stage 0.85
        Assert.Equal(0.15, Rules.ComponentCost(hth, GeoSuitabilityRules.StageTerm), 12);
        Assert.Equal(0.0, Rules.ComponentCost(hth, "temperature"), 12);
        Assert.Equal(0.85, Rules.Score(hth), 12);
    }

    /// <summary>
    /// Every shipped offshore candidate has depth 1 and river factor 1, so the
    /// data alone cannot tell the offshore cost formula from a wrong one. A
    /// synthetic HTH13-01 (depth 0.8, river factor 0.9, stage 0.85) pins each
    /// factor of depth × stage × river costing (1 − its value) times the other two.
    /// </summary>
    [Fact]
    public void OffshoreCosts_AreEachFactorsShortfallTimesTheOtherTwo()
    {
        var file = JsonNode.Parse(GeoTestData.ReadGeoText("candidates.json"))!.AsObject();
        var site = file["sites"]!.AsArray().Select(s => s!.AsObject()).First(s => (string?)s["id"] == "HTH13-01");
        site["components"]!["depth"] = 0.8;
        site["components"]!["river"] = 0.75;
        site["river_factor"] = 0.9;
        var hth = GeoCandidates.Parse(file.ToJsonString()).Find("HTH13-01")!;
        Assert.Equal(0.85, hth.StageFactor);

        Assert.Equal(0.2 * 0.85 * 0.9, Rules.ComponentCost(hth, "depth"), 12);
        Assert.Equal(0.8 * 0.15 * 0.9, Rules.ComponentCost(hth, GeoSuitabilityRules.StageTerm), 12);
        Assert.Equal(0.8 * 0.85 * 0.1, Rules.ComponentCost(hth, GeoSuitabilityRules.RiverTerm), 12);
        Assert.Equal(0.0, Rules.ComponentCost(hth, "waves"), 12);
        Assert.Equal(0.8 * 0.85 * 0.9, Rules.Score(hth), 12);
        Assert.Equal("depth", Rules.BindingOf(hth));
    }

    [Fact]
    public void Trapezoid_IsGeoRulesShape()
    {
        Assert.Equal(0.0, GeoSuitabilityRules.Trapezoid(2.0, 2, 8, 16, 20));
        Assert.Equal(0.5, GeoSuitabilityRules.Trapezoid(5.0, 2, 8, 16, 20), 12);
        Assert.Equal(1.0, GeoSuitabilityRules.Trapezoid(8.0, 2, 8, 16, 20));
        Assert.Equal(1.0, GeoSuitabilityRules.Trapezoid(16.0, 2, 8, 16, 20));
        Assert.Equal(0.75, GeoSuitabilityRules.Trapezoid(17.0, 2, 8, 16, 20), 12);
        Assert.Equal(0.0, GeoSuitabilityRules.Trapezoid(25.0, 2, 8, 16, 20));
        // A step when the ramp is vertical (waves: 1 up to 1.2 m from −1 m).
        Assert.Equal(1.0, GeoSuitabilityRules.Trapezoid(-1.0, -1, -1, 1.2, 2.8));
    }

    [Fact]
    public void Rules_ParseRefusesANegativeWeight_NamingIt()
    {
        var file = JsonNode.Parse(GeoTestData.ReadGeoText("suitability_rules.json"))!.AsObject();
        file["effective_weights"]!["waves"] = -0.2;
        var e = Assert.Throws<InvalidDataException>(() => GeoSuitabilityRules.Parse(file.ToJsonString()));
        Assert.Contains("effective_weights.waves", e.Message);
    }
}
