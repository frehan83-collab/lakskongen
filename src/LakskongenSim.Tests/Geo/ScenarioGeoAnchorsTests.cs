#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// Where the game's zones are drawn on the real coast (STAGE23_PLAN track A,
/// A3; ruling OD9): the 16 shipped starting sites at authored producing
/// grow-out localities, the 13 catalogue zones in their municipality and own
/// area, and a deterministic fallback for everything else.
/// </summary>
public class ScenarioGeoAnchorsTests
{
    private readonly ITestOutputHelper _output;

    public ScenarioGeoAnchorsTests(ITestOutputHelper output) => _output = output;

    private static GeoLocality Locality(int id)
    {
        Assert.True(GeoLocalities.Default.TryGet(id, out var l), $"locality {id}");
        return l!;
    }

    /// <summary>(scenario, zone, area) of every starting site in godot/data/scenarios.json.</summary>
    private static IReadOnlyList<(string Scenario, string Zone, int Po)> ShippedStartingSites()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(GeoTestData.RepoRoot, "godot", "data", "scenarios.json")));
        var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("scenarios");
        var sites = new List<(string, string, int)>();
        foreach (var scenario in root.EnumerateArray())
            foreach (var site in scenario.GetProperty("starting_sites").EnumerateArray())
                sites.Add((scenario.GetProperty("id").GetString()!, site.GetProperty("zone").GetString()!, site.GetProperty("production_area").GetInt32()));
        return sites;
    }

    [Fact]
    public void TheAuthoredTable_IsExactlyTheShippedScenariosStartingSites()
    {
        var shipped = ShippedStartingSites();
        Assert.Equal(16, shipped.Count);
        Assert.Equal(9, shipped.Select(s => s.Scenario).Distinct().Count());
        Assert.Equal(shipped.OrderBy(s => s.Scenario).ThenBy(s => s.Zone),
            ScenarioGeoAnchors.AuthoredKeys.Select(k => (k.Scenario, k.ZoneId, k.ProductionArea)).OrderBy(s => s.Scenario).ThenBy(s => s.ZoneId));
        // fjord_west in PO3, PO4 and PO6, fjord_north in PO9 and PO12: keyed by (zone, area).
        Assert.Equal(new[] { 3, 4, 6 }, shipped.Where(s => s.Zone == "fjord_west").Select(s => s.Po).OrderBy(p => p));
        Assert.Equal(new[] { 9, 12 }, shipped.Where(s => s.Zone == "fjord_north").Select(s => s.Po).OrderBy(p => p));
    }

    [Fact]
    public void EveryAuthoredAnchor_IsAProducingGrowoutSiteInItsArea_DistinctWithinItsScenario()
    {
        foreach (var group in ScenarioGeoAnchors.AuthoredKeys.GroupBy(k => k.Scenario))
        {
            var anchors = group.Select(k => ScenarioGeoAnchors.AnchorOf(k.ZoneId, k.ProductionArea)).ToArray();
            Assert.Equal(anchors.Length, anchors.Select(a => a.LocalityId).Distinct().Count());
            foreach (var (key, anchor) in group.Zip(anchors))
            {
                var l = Locality(anchor.LocalityId);
                Assert.True(l.IsSeaGrowoutProducing, $"{key}: {l.Id} is not producing sea grow-out");
                Assert.Equal(key.ProductionArea, l.Po);
                Assert.Equal((l.X, l.Y, l.Name, ScenarioGeoAnchors.AuthoredSource), (anchor.X, anchor.Y, anchor.Name, anchor.Source));
                _output.WriteLine($"{key.Scenario}/{key.ZoneId}/PO{key.ProductionArea}: {l.Id} {l.Name} ({l.Municipality}, {l.MtbT} t)");
            }
        }
    }

    [Fact]
    public void TheAnchors_FitTheirScenarios()
    {
        GeoLocality At(string zone, int po) => Locality(ScenarioGeoAnchors.AnchorOf(zone, po).LocalityId);
        // OD9: the family farm is a one-licence site in Kvinnherad (Hardangerfjorden).
        var family = At("fjord_west", 3);
        Assert.Equal(("Kvinnherad", 780.0), (family.Municipality, family.MtbT!.Value));
        Assert.Equal("Flakstad", At("fjord_north", 9).Municipality);           // Lofoten
        Assert.Equal("Frøya", At("fjord_west", 6).Municipality);               // Trøndelag
        Assert.Equal("Nordkapp", At("fjord_north", 12).Municipality);          // Finnmark
        Assert.Equal("Gulen", At("fjord_west", 4).Municipality);               // Vestland
        Assert.True(At("po3_nord", 3).Y > At("po3_sor", 3).Y);                 // north of south
        // ISA: neighbours north and south of the core, within 5 km.
        var core = At("isa_kjerne", 6);
        var north = At("isa_nord", 6);
        var south = At("isa_sor", 6);
        Assert.True(north.Y > core.Y && south.Y < core.Y);
        foreach (var n in new[] { north, south })
            Assert.True(Math.Sqrt(Math.Pow(n.X - core.X, 2) + Math.Pow(n.Y - core.Y, 2)) < 5000.0);
        // Algae: south of middle, the sheltered site inland (east) of both, one fjord system.
        var algeSor = At("alge_sor", 9);
        var algeMidt = At("alge_midt", 9);
        var algeIndre = At("alge_indre", 9);
        Assert.True(algeSor.Y < algeMidt.Y && algeIndre.X > algeMidt.X && algeIndre.X > algeSor.X);
        Assert.Single(new[] { algeSor, algeMidt, algeIndre }.Select(l => l.Municipality).Distinct());
        // Resource rent: growth site north of the main site, expansion south of it.
        var main = At("vest_1", 4);
        Assert.True(At("vest_2", 4).Y > main.Y && At("vest_3", 4).Y < main.Y);
    }

    [Fact]
    public void EveryCatalogueZone_IsAnchoredInItsOwnArea_InItsMunicipalityWhereTheRegistryHasOne()
    {
        var expectedMunicipality = new Dictionary<string, string>
        {
            ["po01_flekkefjord"] = "Flekkefjord", ["po02_finnoy"] = "Stavanger", ["po03_bomlo"] = "Bømlo",
            ["po04_solund"] = "Solund", ["po05_sande"] = "Sande I Møre Og Romsdal", ["po06_hitra"] = "Hitra",
            ["po07_vikna"] = "Nærøysund", ["po08_luroy"] = "Lurøy", ["po09_steigen"] = "Steigen",
            ["po10_senja"] = "Senja", ["po11_skjervoy"] = "Skjervøy", ["po12_hasvik"] = "Hasvik",
        };
        Assert.Equal(13, ExpansionZones.All.Count);
        foreach (var zone in ExpansionZones.All)
        {
            var anchor = ScenarioGeoAnchors.AnchorOf(zone.ZoneId, zone.ProductionArea);
            var l = Locality(anchor.LocalityId);
            Assert.True(l.IsSeaGrowoutProducing);
            Assert.Equal(zone.ProductionArea, l.Po);
            if (expectedMunicipality.TryGetValue(zone.ZoneId, out var municipality))
            {
                Assert.Equal((municipality, ScenarioGeoAnchors.MunicipalitySource), (l.Municipality, anchor.Source));
                // The first producing grow-out locality by id there.
                Assert.Equal(GeoLocalities.Default.SeaGrowoutProducing.First(x => x.Po == zone.ProductionArea && x.Municipality == municipality).Id, l.Id);
            }
            else
            {
                // po13_lebesby: every Lebesby locality lies in PO12, so the PO13 fallback.
                Assert.Equal("po13_lebesby", zone.ZoneId);
                Assert.Equal(ScenarioGeoAnchors.FallbackSource, anchor.Source);
            }
            _output.WriteLine($"{zone.ZoneId}: {l.Id} {l.Name} ({l.Municipality}) {anchor.Source}");
        }
        Assert.All(GeoLocalities.Default.All.Where(l => l.Municipality == "Lebesby"), l => Assert.Equal(12, l.Po));
    }

    [Theory]
    [InlineData("Flekkefjord", "Flekkefjord", true)]
    [InlineData("Sande", "Sande I Møre Og Romsdal", true)]
    [InlineData("Herøy", "Herøy I Nordland", true)]
    [InlineData("Nesseby", "Unjárga - Nesseby", true)]
    [InlineData("Kåfjord", "Gáivuotna - Kåfjord - Kaivuono", true)]
    [InlineData("Finnøy", "Stavanger", true)]
    [InlineData("Vikna", "Nærøysund", true)]
    [InlineData("Finnøy", "Finnøy", false)]
    [InlineData("Sande", "Sandnes", false)]
    [InlineData("Bø", "Bømlo", false)]
    [InlineData("", "Bømlo", false)]
    public void MunicipalityMatching_TakesTheBaseNameATwoLanguagePartOrTheSuccessor(string zoneName, string municipality, bool matches)
        => Assert.Equal(matches, ScenarioGeoAnchors.MunicipalityMatches(zoneName, municipality));

    [Fact]
    public void TheFallback_IsADeterministicProducingSiteInTheArea()
    {
        var picks = new HashSet<int>();
        foreach (var zone in new[] { "mod_zone", "Troms fjord", "a", "b", "c", "d", "e", "f", "g", "h" })
        {
            var a = ScenarioGeoAnchors.AnchorOf(zone, 5);
            Assert.Equal(a, ScenarioGeoAnchors.AnchorOf(zone, 5));
            Assert.Equal(ScenarioGeoAnchors.FallbackSource, a.Source);
            var l = Locality(a.LocalityId);
            Assert.True(l.IsSeaGrowoutProducing);
            Assert.Equal(5, l.Po);
            picks.Add(a.LocalityId);
        }
        Assert.True(picks.Count >= 3, $"{picks.Count} distinct fallback picks over 10 zone ids");
        // A known zone in a different area than the table's key falls back too.
        Assert.Equal(ScenarioGeoAnchors.FallbackSource, ScenarioGeoAnchors.AnchorOf("fjord_west", 5).Source);
        Assert.Equal(ScenarioGeoAnchors.FallbackSource, ScenarioGeoAnchors.AnchorOf("po06_hitra", 5).Source);
        // An area outside 1-13 picks from every producing site; a null zone id is "".
        var none = ScenarioGeoAnchors.AnchorOf("x", 0);
        Assert.True(Locality(none.LocalityId).IsSeaGrowoutProducing);
        Assert.Equal(ScenarioGeoAnchors.AnchorOf("", 7), ScenarioGeoAnchors.AnchorOf(null!, 7));
    }
}
