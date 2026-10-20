#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LakskongenSim.Core;
using LakskongenSim.Environment.Geo;
using LakskongenSim.Farm;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// The engine's per-game plot catalogue (STAGE23_PLAN track A, A2):
/// StartScenario binds the home area's geo catalogue, a load rebuilds it from
/// the saved home area before the plots rejoin their templates, nothing new is
/// saved, and a game with no scenario keeps the default catalogue.
/// </summary>
public class GeoPlotEngineTests
{
    /// <summary>The 9 shipped scenarios and their home areas (godot/data/scenarios.json).</summary>
    public static IEnumerable<object[]> Scenarios() => new[]
    {
        new object[] { "forste_sesong", 3 }, new object[] { "lofoten_eventyr", 9 }, new object[] { "laksebaronene", 6 },
        new object[] { "krise_manager", 12 }, new object[] { "barekraft_mester", 4 }, new object[] { "po3_red_sone", 3 },
        new object[] { "isa_utbrudd", 6 }, new object[] { "algekatastrofe", 9 }, new object[] { "ressursrente", 4 },
    };

    internal static SimulationEngine Started(string scenarioId, int seed = 17)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        engine.Scenario.Unlock(scenarioId);
        Assert.True(engine.StartScenario(scenarioId).Ok);
        return engine;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void StartScenario_BindsTheHomeAreasCatalogue(string scenarioId, int home)
    {
        var engine = Started(scenarioId);
        Assert.Equal(home, engine.State.HomeProductionArea);
        Assert.Same(GeoPlotCatalogs.ForHomeArea(home), engine.PlotCatalog);
        var binding = engine.GeoBindingOf("hav_1");
        Assert.NotNull(binding);
        Assert.Equal(GeoSitePlotGeometryTests.ExpectedBinding[home], binding!.CandidateId);
        var geometry = GeoPlotCatalogs.GeometryFor(home)!;
        var c = geometry.Candidate;
        Assert.Equal(new GeoPlotBinding("hav_1", c.Id, c.Area!, c.Stage!, c.StageFactor!.Value, c.Po, c.DepthM, c.Score, c.Binding,
            geometry.DistanceKm, c.X, c.Y, c.Lon, c.Lat, c.Label), binding);
        foreach (var other in new[] { "off_1", "off_2", "pad_a", "pad_b", "pad_delta", "overflow_offshore_1", "nowhere", "" })
            Assert.Null(engine.GeoBindingOf(other));
        // The Hav lens shows the bound position and depth; the gameplay rows stay the default's.
        var hav = engine.GetBuildPlots(FarmTypeIds.Hav).Single(p => p.Id == "hav_1");
        Assert.Equal(((double)(float)geometry.Z, (double)(float)c.DepthM), (hav.WorldZ, hav.WaterDepthM));
        Assert.Equal(13.0, hav.Hs100M);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void SaveAndLoad_RebuildTheSameCatalogue_AndTheSameHavLens(string scenarioId, int home)
    {
        var engine = Started(scenarioId);
        engine.Advance(2);
        var lens = engine.GetBuildPlots(FarmTypeIds.Hav).ToArray();
        string saved = SaveStateComparer.Serialize(engine);
        var resumed = SaveStateComparer.Resume(saved, events: false);
        Assert.Same(GeoPlotCatalogs.ForHomeArea(home), resumed.PlotCatalog);
        Assert.Equal(engine.GeoBindingOf("hav_1"), resumed.GeoBindingOf("hav_1"));
        Assert.Equal(lens, resumed.GetBuildPlots(FarmTypeIds.Hav).ToArray());
        Assert.Equal(SaveStateComparer.Normalize(saved), SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed)));
    }

    /// <summary>
    /// Nothing is saved: a game on the geo catalogue writes the same save as
    /// the same game on the default catalogue, before and after a unit claims
    /// hav_1 (the plots' saved fields hold no position).
    /// </summary>
    [Fact]
    public void TheCatalogue_IsNotSaved_TheSaveIsTheDefaultCataloguesByteForByte()
    {
        var geo = Started("po3_red_sone");
        var plain = Started("po3_red_sone");
        plain.PlotCatalog = SitePlotCatalog.Default;
        Assert.NotEqual(plain.SitePlots.Single(p => p.Id == "hav_1").WorldZ, geo.SitePlots.Single(p => p.Id == "hav_1").WorldZ);
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(plain)), SaveStateComparer.Normalize(SaveStateComparer.Serialize(geo)));

        foreach (var engine in new[] { geo, plain })
            engine.ClaimPlot(engine.SitePlots.Single(p => p.Id == "hav_1"), "offshore_test");
        string geoSave = SaveStateComparer.Serialize(geo);
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(plain)), SaveStateComparer.Normalize(geoSave));
        Assert.DoesNotContain("HTH", geoSave, StringComparison.Ordinal);
        // The saved plot is still the 7 fields of F2-03.
        var plot = JsonNode.Parse(geoSave)!["SitePlots"]!.AsArray().First(p => (string?)p!["Id"] == "hav_1")!.AsObject();
        Assert.Equal(new[] { "GridFeeds", "Id", "LiceExternalRevealed", "OwnerUnitId", "PreZoned", "WithdrawnFromDay", "ZoneId" },
            plot.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal));
        // And a load of the claimed game rejoins hav_1 with the geo position.
        var resumed = SaveStateComparer.Resume(geoSave, events: false);
        Assert.Equal(geo.SitePlots.Single(p => p.Id == "hav_1").WorldZ, resumed.SitePlots.Single(p => p.Id == "hav_1").WorldZ);
        Assert.Equal("offshore_test", resumed.SitePlots.Single(p => p.Id == "hav_1").OwnerUnitId);
    }

    [Fact]
    public void AnEngineWithNoScenario_KeepsTheDefaultCatalogue_AndBindsNothing()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 5);
        Assert.Same(SitePlotCatalog.Default, engine.PlotCatalog);
        Assert.Null(engine.GeoBindingOf("hav_1"));
        // A load of a scenario-less game keeps it too.
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        Assert.Same(SitePlotCatalog.Default, resumed.PlotCatalog);
        Assert.Null(resumed.GeoBindingOf("hav_1"));
        // A catalogue the engine did not select (a test's terrain join) is not a geo binding.
        var started = Started("lofoten_eventyr");
        started.PlotCatalog = SitePlotCatalog.Default.WithGeometry(new LakskongenSim.Environment.TerrainGenerator(42));
        Assert.Null(started.GeoBindingOf("hav_1"));
    }

    /// <summary>
    /// A pre-S2-02 save holds no home area: the load assigns it from the
    /// scenario's sites (RestoreScenarioFromState) before the catalogue is
    /// chosen, so the old save plays on its area's catalogue.
    /// </summary>
    [Fact]
    public void APreS2_02SaveWithNoHomeArea_LoadsOntoItsScenariosAreaCatalogue()
    {
        var engine = Started("lofoten_eventyr");
        var json = JsonNode.Parse(SaveStateComparer.Serialize(engine))!.AsObject();
        Assert.Equal(9, (int)json["HomeProductionArea"]!);
        json["HomeProductionArea"] = 0;
        foreach (var zone in json["Zones"]!.AsArray())
            zone!["ProductionArea"] = 0;
        var resumed = SaveStateComparer.Resume(json.ToJsonString(), events: false);
        Assert.Equal(9, resumed.State.HomeProductionArea);
        Assert.Same(GeoPlotCatalogs.ForHomeArea(9), resumed.PlotCatalog);
        Assert.Equal("HTH05-03", resumed.GeoBindingOf("hav_1")!.CandidateId);
    }

    [Fact]
    public void ReadingTheBinding_ChangesNothing()
    {
        var engine = Started("krise_manager");
        string before = SaveStateComparer.Serialize(engine);
        var streams = engine.Rng.CaptureStates();
        for (int i = 0; i < 20; i++)
        {
            engine.GeoBindingOf("hav_1");
            engine.GetBuildPlots(FarmTypeIds.Hav);
        }
        Assert.Equal(streams, engine.Rng.CaptureStates());
        Assert.Equal(before, SaveStateComparer.Serialize(engine));
    }
}
