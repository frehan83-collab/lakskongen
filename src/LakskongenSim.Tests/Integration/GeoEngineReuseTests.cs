#nullable enable
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Geo;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// GameBridge keeps one engine for the whole session: New Game, a scenario
/// start and a load each re-Initialize it. Every geo read must follow the game
/// that is running now, never the one before (captain's review of geo track A,
/// findings 2 and 3; STAGE23_PLAN §2 track A: "an engine with no scenario keeps
/// Default" and "save, load and GetBuildPlots('hav') give the same result").
/// The helpers replay GameBridge's own call order on one engine.
/// </summary>
public class GeoEngineReuseTests
{
    /// <summary>A name no roster company uses: what a save made before a rename still holds (ruling OD8).</summary>
    private const string StoredName = "Tidligere Navn AS";

    /// <summary>GameBridge.StartNewGameInternal (New Game, start_qa_new_game): a game with no scenario.</summary>
    private static void NewGame(SimulationEngine engine, int seed)
    {
        engine.FinanceManager = new FinanceManager(10_000_000m);
        engine.Initialize(enableEvents: false, seed: seed);
    }

    /// <summary>GameBridge.StartScenarioCore: a new game, then the scenario's starting state.</summary>
    private static void NewScenario(SimulationEngine engine, string scenarioId, int seed)
    {
        NewGame(engine, seed);
        engine.Scenario.Unlock(scenarioId);
        Assert.True(engine.StartScenario(scenarioId).Ok);
    }

    /// <summary>GameBridge.LoadGame: Initialize with the save's seed, then adopt the save.</summary>
    private static void Load(SimulationEngine engine, string json)
    {
        var snapshot = JsonSerializer.Deserialize<GameState>(json, SaveJson.Options)!;
#pragma warning disable CS0618 // GameState.Balance is the save-compat mirror GameBridge.LoadGame reads
        engine.FinanceManager = new FinanceManager(snapshot.Balance);
#pragma warning restore CS0618
        engine.Initialize(enableEvents: false, seed: snapshot.RandomSeed != 0 ? snapshot.RandomSeed : null);
        engine.AdoptSavedState(snapshot);
    }

    private static BuildPlotView[] HavLens(SimulationEngine engine) => engine.GetBuildPlots(FarmTypeIds.Hav).ToArray();

    [Theory]
    [MemberData(nameof(GeoPlotEngineTests.Scenarios), MemberType = typeof(GeoPlotEngineTests))]
    public void ANewGameAfterAScenario_IsBackOnTheDefaultCatalogue(string scenarioId, int home)
    {
        var fresh = new SimulationEngine();
        NewGame(fresh, seed: 6);
        var engine = new SimulationEngine();
        NewScenario(engine, scenarioId, seed: 5);
        Assert.Same(GeoPlotCatalogs.ForHomeArea(home), engine.PlotCatalog);

        NewGame(engine, seed: 6);

        Assert.Equal(0, engine.State.HomeProductionArea);
        Assert.Same(SitePlotCatalog.Default, engine.PlotCatalog);
        Assert.Null(engine.GeoBindingOf("hav_1"));
        Assert.Equal(HavLens(fresh), HavLens(engine));
        var hav = HavLens(engine).Single(p => p.Id == "hav_1");
        Assert.Equal((-5200.0, 300.0), (hav.WorldZ, hav.WaterDepthM));   // site_plots.json hav_1
    }

    [Fact]
    public void OnAReusedEngine_SaveAndLoadGiveTheSameHavLens_AndEachGameItsOwnCatalogue()
    {
        var engine = new SimulationEngine();
        NewScenario(engine, "po3_red_sone", seed: 5);
        NewGame(engine, seed: 6);
        var lens = HavLens(engine);
        string saved = SaveStateComparer.Serialize(engine);

        // On a fresh engine (a restarted game) and on the same engine after another scenario.
        Assert.Equal(lens, HavLens(SaveStateComparer.Resume(saved, events: false)));
        NewScenario(engine, "po3_red_sone", seed: 5);
        Load(engine, saved);
        Assert.Same(SitePlotCatalog.Default, engine.PlotCatalog);
        Assert.Equal(lens, HavLens(engine));

        // A scenario after a scenario binds its own area, and a load of the first game binds that one again.
        NewScenario(engine, "lofoten_eventyr", seed: 5);
        string lofoten = SaveStateComparer.Serialize(engine);
        var lofotenLens = HavLens(engine);
        NewScenario(engine, "laksebaronene", seed: 5);
        Assert.Same(GeoPlotCatalogs.ForHomeArea(6), engine.PlotCatalog);
        Load(engine, lofoten);
        Assert.Same(GeoPlotCatalogs.ForHomeArea(9), engine.PlotCatalog);
        Assert.Equal(lofotenLens, HavLens(engine));
    }

    [Fact]
    public void ALoadedSavesStoredNames_StayWithThatSave_AndTheNextGameShowsItsOwnRoster()
    {
        var maker = new SimulationEngine();
        NewScenario(maker, "forste_sesong", seed: 11);
        var json = JsonNode.Parse(SaveStateComparer.Serialize(maker))!.AsObject();
        var stored = json["CompetitorAI"]!["Competitors"]!.AsArray().Single(c => (string?)c!["Id"] == "nordlaks_nord")!;
        stored["Name"] = StoredName;

        var engine = new SimulationEngine();
        Load(engine, json.ToJsonString());
        Assert.Equal(StoredName, engine.CompetitorAI.Competitors.Single(c => c.Id == "nordlaks_nord").Name);
        AssertTheLayoutNamesTheRoster(engine);

        // The same scenario again before any licence round: same ids, tonnes and anchors, other names.
        NewScenario(engine, "forste_sesong", seed: 12);
        Assert.NotEqual(StoredName, engine.CompetitorAI.Competitors.Single(c => c.Id == "nordlaks_nord").Name);
        AssertTheLayoutNamesTheRoster(engine);
        Assert.DoesNotContain(engine.GetRivalLayout().Owners, o => o.Name == StoredName);
        for (int po = 1; po <= ProductionAreas.Count; po++)
            Assert.DoesNotContain(engine.GetRivalSummary(po).TopOwners, o => o.Name == StoredName);

        // And back to the old save: its stored name is on the map again.
        Load(engine, json.ToJsonString());
        AssertTheLayoutNamesTheRoster(engine);
        Assert.Contains(engine.GetRivalLayout().Owners, o => o.Name == StoredName);
    }

    private static void AssertTheLayoutNamesTheRoster(SimulationEngine engine)
    {
        var roster = engine.CompetitorAI.Competitors;
        var layout = engine.GetRivalLayout();
        Assert.Equal(roster.Select(c => (c.Id, c.Name)), layout.Owners.Take(roster.Count).Select(o => (o.Id, o.Name)));
        Assert.Equal(layout.Signature, engine.RivalLayoutSignature());
    }

    [Fact]
    public void AGameWithNoHomeArea_CountsNoHomeSites_SoTheThreeDOrderIsTheRosterOrder()
    {
        var engine = new SimulationEngine();
        NewScenario(engine, "lofoten_eventyr", seed: 5);
        Assert.Equal("nordlaks_nord", engine.CompetitorsByHomePresence()[0].Company.Id);

        NewGame(engine, seed: 6);

        Assert.Equal(ProductionAreas.NationalReference, engine.HomeProductionArea);
        var ordered = engine.CompetitorsByHomePresence();
        Assert.Equal(engine.CompetitorAI.Competitors.Select(c => c.Id), ordered.Select(p => p.Company.Id));
        Assert.All(ordered, p => Assert.Equal(0, p.HomePoSites));

        // Area 0 is "no home area", not the localities with no area: one producing
        // sea grow-out site has none, and once the majors hold every sea site
        // (licence wins grow their shares) one of them holds it.
        var orphan = Assert.Single(GeoLocalities.Default.SeaGrowoutProducing, l => l.Po == 0);
        foreach (var c in engine.CompetitorAI.Competitors)
            c.TotalMABTonnes = (float)RivalLayout.NationalMtbTonnes;
        Assert.True(engine.GetRivalLayout().OwnerOf(orphan.Id)!.IsMajor);
        ordered = engine.CompetitorsByHomePresence();
        Assert.Equal(engine.CompetitorAI.Competitors.Select(c => c.Id), ordered.Select(p => p.Company.Id));
        Assert.All(ordered, p => Assert.Equal(0, p.HomePoSites));
    }
}
