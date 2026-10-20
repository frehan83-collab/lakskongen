#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using LakskongenSim.Environment.Geo;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// The candidate sites (STAGE23_PLAN track A, A1): 12 open pens per area in
/// rank order, the offshore spread, every score recomputed from its components
/// and the published weights within 0.002, the binding equal to the term with
/// the largest weighted cost, readable place labels, and parse errors that
/// name the candidate and the field.
/// </summary>
public class GeoCandidatesTests
{
    private static GeoCandidates Candidates => GeoCandidates.Default;
    private static GeoSuitabilityRules Rules => GeoSuitabilityRules.Default;

    [Fact]
    public void TheCounts_Are12OpenPensPerArea_AndTheOffshoreSpread()
    {
        Assert.Equal(189, Candidates.All.Count);
        for (int po = 1; po <= 13; po++)
        {
            var pens = Candidates.OpenPensOf(po);
            Assert.Equal(Enumerable.Range(1, 12), pens.Select(c => c.Rank));
            Assert.All(pens, c => Assert.True(c.IsOpenPen && c.Po == po && c.Id.StartsWith($"PO{po:00}-", StringComparison.Ordinal), c.Id));
            Assert.Equal(pens.Select(c => c.Score).OrderByDescending(s => s), pens.Select(c => c.Score));
        }
        var offshore = Candidates.Offshore.GroupBy(c => c.Po).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<int, int> { [2] = 4, [3] = 2, [6] = 9, [8] = 6, [11] = 6, [12] = 3, [13] = 3 }, offshore);
        Assert.All(Candidates.Offshore, c => Assert.True(c.IsOffshore && c.Id.StartsWith("HTH", StringComparison.Ordinal), c.Id));
        Assert.Empty(Candidates.OpenPensOf(0));
        Assert.Empty(Candidates.OpenPensOf(14));
    }

    [Fact]
    public void Find_ReturnsTheRecord_OrNull()
    {
        var c = Candidates.Find("PO03-01")!;
        Assert.Equal(("PO03-01", 3, 1, "open_pen"), (c.Id, c.Po, c.Rank, c.Type));
        Assert.Same(c, Candidates.OpenPensOf(3)[0]);
        Assert.Equal("Norskerenna sør", Candidates.Find("HTH02-01")!.Area);
        Assert.Null(Candidates.Find("PO14-01"));
        Assert.Null(Candidates.Find(null!));
    }

    [Fact]
    public void OpenPensCarryOceanValues_OffshoreCarryAreaStageAndFactor()
    {
        foreach (var c in Candidates.All)
        {
            if (c.IsOpenPen)
            {
                Assert.NotNull(c.Ocean);
                Assert.True(c.Ocean!.CurrentSpeedMs > 0 && c.Ocean.HsM > 0 && c.Ocean.O2MmolM3 > 0, c.Id);
                Assert.Null(c.Area);
                Assert.Null(c.Stage);
                Assert.Null(c.StageFactor);
            }
            else
            {
                Assert.Null(c.Ocean);
                Assert.False(string.IsNullOrEmpty(c.Area));
                Assert.Equal(Rules.OffshoreStageFactors[c.Stage!], c.StageFactor);
            }
        }
    }

    [Fact]
    public void Components_AreOrdinalSorted_AndWithinZeroToOne()
    {
        foreach (var c in Candidates.All)
        {
            var names = c.Components.Select(p => p.Key).ToArray();
            Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
            Assert.All(c.Components, p => Assert.InRange(p.Value, 0.0, 1.0));
            if (c.IsOpenPen)
                Assert.Equal(Rules.EffectiveWeights.Keys.Append("river").OrderBy(n => n, StringComparer.Ordinal), names);
            else
                Assert.Equal(new[] { "depth", "river" }, names);
        }
    }

    [Fact]
    public void EveryScore_RecomputesFromItsComponentsAndThePublishedWeights_Within0002()
    {
        double worst = 0;
        foreach (var c in Candidates.All)
        {
            double error = Math.Abs(Rules.Score(c) - c.Score);
            worst = Math.Max(worst, error);
            Assert.True(error <= 0.002, $"{c.Id}: recomputed {Rules.Score(c):F4} vs {c.Score}");
        }
        Assert.True(worst > 0, "the recomputation must use the rounded components, so some error is expected");
    }

    [Fact]
    public void TheRiverFactor_IsTheRiverComponentOnThePublishedRamp()
    {
        foreach (var c in Candidates.All)
        {
            Assert.True(c.TryGetComponent("river", out double river), c.Id);
            double expected = Rules.RiverMinFactor + (1 - Rules.RiverMinFactor) * river;
            Assert.True(Math.Abs(expected - c.RiverFactor) <= 0.0011, $"{c.Id}: {expected:F4} vs {c.RiverFactor}");
        }
    }

    [Fact]
    public void EveryOpenPensBinding_IsTheTermWithTheLargestWeightedCost()
    {
        var terms = Rules.ComponentOrder.Append(GeoSuitabilityRules.RiverTerm).ToArray();
        foreach (var c in Candidates.All.Where(c => c.IsOpenPen))
        {
            var costs = terms.ToDictionary(t => t, t => Rules.ComponentCost(c, t));
            double max = costs.Values.Max();
            if (max <= 1e-9)
            {
                Assert.True(c.Binding == GeoSuitabilityRules.NoBinding, $"{c.Id}: no term costs anything but the binding is {c.Binding}");
                continue;
            }
            Assert.True(costs.TryGetValue(c.Binding, out double cost), $"{c.Id}: binding '{c.Binding}' is not a score term");
            Assert.True(cost == max, $"{c.Id}: '{c.Binding}' costs {cost:F5}, the largest cost is {max:F5}");
            Assert.Equal(c.Binding, Rules.BindingOf(c));
        }
    }

    [Fact]
    public void EveryOffshoreBinding_FollowsTheDepthThenStageRule()
    {
        foreach (var c in Candidates.Offshore)
        {
            Assert.Equal(c.Binding, Rules.BindingOf(c));
            string largest = new[] { "depth", GeoSuitabilityRules.StageTerm, GeoSuitabilityRules.RiverTerm }
                .OrderByDescending(t => Rules.ComponentCost(c, t)).First();
            if (c.Binding != GeoSuitabilityRules.NoBinding)
                Assert.Equal(c.Binding, largest);
        }
        Assert.Equal(24, Candidates.Offshore.Count(c => c.Binding == "stage"));
    }

    [Fact]
    public void TheBindingTally_IsGeodatasPublishedOne()
    {
        // GEODATA §5.3: temperature 108, current 24, stage 24, waves 18, none 9, exchange 3, farm distance 3.
        var tally = Candidates.All.GroupBy(c => c.Binding).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int>
        {
            ["temperature"] = 108, ["current"] = 24, ["stage"] = 24, ["waves"] = 18,
            ["none"] = 9, ["exchange"] = 3, ["farm_distance"] = 3,
        }, tally);
    }

    // ── Place labels ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bømlafjord-", "Bømlafjord")]
    [InlineData("hammar", "Hammar")]
    [InlineData("Haugesund lufthavn", "Haugesund")]
    [InlineData("Stokmarknes lufthavn", "Stokmarknes")]
    [InlineData("Vardøtunnelen", "Vardø")]
    [InlineData("Valder-", "Valderøya")]
    [InlineData("Fugløy-", "Fugløybanken")]
    [InlineData("Stiiko-", "Stiiko")]
    [InlineData("  holmane ", "Holmane")]
    [InlineData("Øvre Golta", "Øvre Golta")]
    [InlineData("Hille", "Hille")]
    public void Clean_TrimsCapitalisesAndOverrides(string near, string label)
    {
        Assert.Equal(label, PlaceLabels.Clean(near));
    }

    [Fact]
    public void ThePlansThreePo3Examples_AreCleaned()
    {
        Assert.Equal("Haugesund", Candidates.Find("PO03-02")!.Label);
        Assert.Equal("Bømlafjord", Candidates.Find("PO03-03")!.Label);
        Assert.Equal("Hammar", Candidates.Find("PO03-04")!.Label);
        Assert.Equal("Haugesund lufthavn", Candidates.Find("PO03-02")!.Near);
    }

    [Fact]
    public void EveryLabel_IsReadable_AndFreeOfBrands()
    {
        string[] featureWords = { "lufthavn", "tunnelen", "nasjonalpark", " fyr", " kloster" };
        foreach (var c in Candidates.All)
        {
            Assert.Equal(PlaceLabels.Clean(c.Near), c.Label);
            Assert.False(string.IsNullOrWhiteSpace(c.Label), c.Id);
            Assert.True(char.IsUpper(c.Label[0]), $"{c.Id}: '{c.Label}'");
            Assert.False(c.Label.EndsWith('-'), $"{c.Id}: '{c.Label}'");
            Assert.Equal(c.Label.Trim(), c.Label);
            Assert.DoesNotContain(featureWords, w => c.Label.Contains(w, StringComparison.Ordinal));
            Assert.False(GeoTestData.HasBrand(c.Label), $"{c.Id}: '{c.Label}' names a brand (D17)");
        }
    }

    // ── Parse errors name the candidate and the field ─────────────────────

    private static JsonObject File() => JsonNode.Parse(GeoTestData.ReadGeoText("candidates.json"))!.AsObject();

    private static JsonObject Site(JsonObject file, string id) =>
        file["sites"]!.AsArray().Select(s => s!.AsObject()).First(s => (string?)s["id"] == id);

    [Fact]
    public void TheEmbeddedFile_IsTheGamesDataFile()
    {
        var fromFile = GeoCandidates.Parse(File().ToJsonString());
        Assert.Equal(fromFile.All.Select(c => (c.Id, c.Score, c.X, c.Y, c.Label)), Candidates.All.Select(c => (c.Id, c.Score, c.X, c.Y, c.Label)));
    }

    [Fact]
    public void Parse_RefusesADuplicateId()
    {
        var file = File();
        Site(file, "PO03-02")["id"] = "PO03-01";
        var e = Assert.Throws<InvalidDataException>(() => GeoCandidates.Parse(file.ToJsonString()));
        Assert.Contains("PO03-01", e.Message);
        Assert.Contains("field 'id'", e.Message);
    }

    [Fact]
    public void Parse_RefusesAComponentOutsideZeroToOne()
    {
        var file = File();
        Site(file, "PO05-07")["components"]!["waves"] = 1.2;
        var e = Assert.Throws<InvalidDataException>(() => GeoCandidates.Parse(file.ToJsonString()));
        Assert.Contains("PO05-07", e.Message);
        Assert.Contains("field 'components.waves'", e.Message);
    }

    [Theory]
    [InlineData("score", "PO07-03", "1.5", "field 'score'")]
    [InlineData("type", "PO07-03", "\"raft\"", "field 'type'")]
    [InlineData("po", "PO07-03", "14", "field 'po'")]
    [InlineData("x", "PO07-03", "12.5", "field 'x'")]
    [InlineData("near", "PO07-03", "\"\"", "field 'near'")]
    [InlineData("stage_factor", "HTH13-01", "0", "field 'stage_factor'")]
    [InlineData("river_factor", "HTH13-01", "1.5", "field 'river_factor'")]
    public void Parse_RefusesABadField_NamingTheCandidate(string field, string id, string value, string expected)
    {
        var file = File();
        Site(file, id)[field] = JsonNode.Parse(value);
        var e = Assert.Throws<InvalidDataException>(() => GeoCandidates.Parse(file.ToJsonString()));
        Assert.Contains($"candidate {id}", e.Message);
        Assert.Contains(expected, e.Message);
    }

    [Fact]
    public void Parse_RefusesAMissingField_NamingIt()
    {
        var file = File();
        Site(file, "PO11-04").Remove("depth_m");
        var e = Assert.Throws<InvalidDataException>(() => GeoCandidates.Parse(file.ToJsonString()));
        Assert.Contains("candidate PO11-04", e.Message);
        Assert.Contains("field 'depth_m' is missing", e.Message);
    }

    [Fact]
    public void Parse_RefusesARankGap_NamingTheAreaAndTheField()
    {
        var file = File();
        Site(file, "PO09-12")["rank"] = 13;
        var e = Assert.Throws<InvalidDataException>(() => GeoCandidates.Parse(file.ToJsonString()));
        Assert.Contains("PO9", e.Message);
        Assert.Contains("field 'rank'", e.Message);

        var offshore = File();
        Site(offshore, "HTH21-02")["rank"] = 5;
        var o = Assert.Throws<InvalidDataException>(() => GeoCandidates.Parse(offshore.ToJsonString()));
        Assert.Contains("Fugløybanken", o.Message);
        Assert.Contains("field 'rank'", o.Message);
    }
}
