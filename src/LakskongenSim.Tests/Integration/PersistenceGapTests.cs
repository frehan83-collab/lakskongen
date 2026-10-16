using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Health;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Targeted regressions for the review's persistence and lifecycle findings.
/// The end-to-end guard is <see cref="SaveLoadMatrixTests"/>; these pin the
/// individual contracts so a failure names the broken piece.
/// </summary>
public class PersistenceGapTests
{
    private static SimulationEngine NewEngine(int seed = 7, bool events = false)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: events, seed: seed);
        engine.State.RandomSeed = seed;
        return engine;
    }

    // ── F292: Initialize must not inherit the previous game ──────────────────

    [Fact]
    public void ReInitialize_OnSameEngine_MatchesFreshEngine()
    {
        var reused = NewEngine(seed: 1);
        Assert.True(reused.StartScenario("po3_red_sone").Ok);
        reused.Advance(40);

        reused.Initialize(enableEvents: false, seed: 2);
        reused.State.RandomSeed = 2;
        var fresh = NewEngine(seed: 2);
        Assert.True(reused.StartScenario("po3_red_sone").Ok);
        Assert.True(fresh.StartScenario("po3_red_sone").Ok);
        reused.Advance(40);
        fresh.Advance(40);

        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(fresh));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(reused));
        Assert.True(a == b, "re-initialized engine leaked game #1 state: " + SaveStateComparer.FirstDifference(a, b));
    }

    // ── F1069 / F383: facility verbs ───────────────────────────────────────────

    [Fact]
    public void BuildRASFacility_ChargesModelCapex_StartsConstruction_AndLivesInGameState()
    {
        var engine = NewEngine();
        engine.FinanceManager.AddRevenue(1_000_000_000m);
        decimal before = engine.FinanceManager.Balance;

        var result = engine.BuildRASFacility("Test RAS", RASScale.Small);

        Assert.True(result.Ok, result.Message);
        var facility = Assert.Single(engine.State.RASFacilities);
        Assert.Same(facility, engine.RASFacilities[0]);
        Assert.Equal(facility.GetCAPEX(), before - engine.FinanceManager.Balance);
        Assert.Equal(engine.State.Day, facility.ConstructionStartDay);

        facility.ConstructionDays = 5;
        engine.Advance(6);
        Assert.True(facility.IsOperational, "construction never completed");
    }

    [Fact]
    public void BuildFacility_WithoutFunds_FailsAndSpendsNothing()
    {
        var engine = NewEngine();
        decimal before = engine.FinanceManager.Balance;

        var result = engine.BuildOffshoreFarm("Too expensive", OffshoreType.Submersible);

        Assert.False(result.Ok);
        Assert.Equal("insufficient_funds", result.ErrorKey);
        Assert.Equal(before, engine.FinanceManager.Balance);
        Assert.Empty(engine.OffshoreFarms);
        // F2-03: no offshore unit, and no plot was taken (a refused build changes nothing).
        Assert.Empty(engine.OffshoreUnits);
        Assert.Empty(engine.State.SitePlots);
    }

    /// <summary>
    /// The three facility verbs' units survive a save and a load (F2-03: a
    /// grow-out land site, an offshore unit and a post-smolt land site, each on
    /// its plot): one of each, by id, kind and plot, with their construction
    /// clocks, the offshore unit's structure and zones, and the plots' owners.
    /// </summary>
    [Fact]
    public void BuiltFacilities_SurviveSaveLoad()
    {
        var engine = NewEngine();
        engine.FinanceManager.AddRevenue(5_000_000_000m);
        Assert.True(engine.BuildRASFacility("R", RASScale.Medium).Ok);
        Assert.True(engine.BuildOffshoreFarm("O", OffshoreType.SemiSubmersible).Ok);
        Assert.True(engine.BuildPostSmoltFacility("P").Ok);
        engine.Advance(3);

        var loaded = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);

        var ras = Assert.Single(loaded.RASFacilities, f => f.Kind == LandSiteKind.GrowOut);
        var unit = Assert.Single(loaded.OffshoreUnits);
        var postSmolt = Assert.Single(loaded.PostSmoltSites);
        Assert.Equal(2, loaded.RASFacilities.Count);
        Assert.Empty(loaded.OffshoreFarms);
        Assert.Empty(loaded.PostSmoltFacilities);
        Assert.Equal(engine.RASFacilities[0].ConstructionStartDay, ras.ConstructionStartDay);
        Assert.Equal(engine.OffshoreUnits[0].Offshore!.ConstructionStartDay, unit.Offshore!.ConstructionStartDay);
        Assert.Equal(engine.PostSmoltSites[0].ConstructionStartDay, postSmolt.ConstructionStartDay);
        Assert.Equal(("pad_a", "off_1", "pad_delta"), (ras.PlotId, unit.PlotId, postSmolt.PlotId));
        Assert.Equal((SiteKind.Offshore, OffshoreType.SemiSubmersible, 5.5, 3),
            (unit.Kind, unit.Offshore.FarmType, unit.Offshore.DesignHsM, unit.PhysicalPens.Count));
        Assert.Equal(ras.Id, loaded.FindPlot("pad_a")!.OwnerUnitId);
        Assert.Equal(unit.Id, loaded.FindPlot("off_1")!.OwnerUnitId);
        Assert.Equal(postSmolt.Id, loaded.FindPlot("pad_delta")!.OwnerUnitId);
        Assert.Equal(5.0, loaded.FindPlot("off_1")!.Hs100M); // the catalogue's attributes join the saved state
    }

    // ── F409: contracts ────────────────────────────────────────────────────────

    [Fact]
    public void ContractOffers_AreStablePerDay_AndReadingThemNeverAdvancesSimRng()
    {
        var engine = NewEngine();
        var before = engine.Rng.CaptureStates();

        var first = engine.GetContractOffers().ToList();
        var second = engine.GetContractOffers().ToList();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        var after = engine.Rng.CaptureStates();
        var moved = after.Where(kv => !before.TryGetValue(kv.Key, out var v) || v != kv.Value)
            .Select(kv => kv.Key.Replace('\u0001', '/'))
            .Concat(before.Keys.Where(k => !after.ContainsKey(k)).Select(k => "missing:" + k))
            .ToList();
        Assert.True(moved.Count == 0, "reading offers moved streams: " + string.Join(", ", moved));
    }

    [Fact]
    public void AcceptedContract_HasDeterministicId_AndSurvivesSaveLoad()
    {
        var engine = NewEngine();
        int offered = engine.GetContractOffers().Count;
        var accepted = engine.AcceptContractOffer(0);

        Assert.True(accepted.Ok, accepted.Message);
        Assert.Equal(offered - 1, engine.GetContractOffers().Count);
        Assert.Equal("00000001-0000-0000-0000-000000000000", accepted.Id);

        var loaded = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        var contract = Assert.Single(loaded.ContractSystem.ActiveContracts);
        Assert.Equal(accepted.Id, contract.Id.ToString());
        Assert.Equal(offered - 1, loaded.GetContractOffers().Count);
    }

    // ── F243 / F244: scenarios ─────────────────────────────────────────────────

    [Fact]
    public void StartScenario_AppliesInsuranceAndResourceRentToTheLiveEngine()
    {
        var isa = NewEngine();
        Assert.True(isa.StartScenario("isa_utbrudd").Ok);
        Assert.Equal(2, isa.Insurance.Policies.Count);

        var rent = NewEngine();
        Assert.True(rent.StartScenario("ressursrente").Ok);
        Assert.True(rent.EnableResourceRentTax);
    }

    [Fact]
    public void ActiveScenario_SurvivesSaveLoad()
    {
        var engine = NewEngine();
        Assert.True(engine.StartScenario("po3_red_sone").Ok);
        engine.Advance(10);

        var loaded = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);

        Assert.Equal("po3_red_sone", loaded.Scenario.ActiveScenario?.Id);
        Assert.Equal(engine.Scenario.ScenarioStartDay, loaded.Scenario.ScenarioStartDay);
        Assert.True(loaded.Scenario.IsScenarioActive);
    }

    [Fact]
    public void UnknownScenario_FailsWithStableErrorKey()
    {
        var result = NewEngine().StartScenario("no_such_scenario");
        Assert.False(result.Ok);
        Assert.Equal("unknown_scenario", result.ErrorKey);
    }

    // ── F1071: competitor roster ───────────────────────────────────────────────

    [Fact]
    public void CompetitorSnapshot_RoundTrip_DropsAcquiredRivalsAndKeepsClock()
    {
        var ai = new CompetitorAISystem(seed: 5);
        for (int q = 0; q < 3; q++) ai.AdvanceQuarter(0.01f, 50_000_000m, 80f, 2);
        var snapshot = ai.ExportState();
        snapshot.Competitors.RemoveAt(0); // as if acquired before the save

        var restored = new CompetitorAISystem(seed: 5);
        restored.ImportState(snapshot);

        Assert.Equal(snapshot.Competitors.Count, restored.Competitors.Count);
        Assert.Equal(snapshot.Competitors.Select(c => c.Id), restored.Competitors.Select(c => c.Id));
        Assert.Equal(snapshot.Competitors[0].FinancialHealth, restored.Competitors[0].FinancialHealth);
        Assert.Equal(snapshot.CurrentQuarter, restored.ExportState().CurrentQuarter);
    }

    // ── F478: disease prevalence + company lice model ──────────────────────────

    [Fact]
    public void EngineDisease_IsTheSameSirTheDailyTickUses()
    {
        var engine = NewEngine();
        Assert.Same(engine.IntegratedDisease.BaseSystem, engine.Disease);
    }

    [Fact]
    public void CompanyLiceModel_StateRoundTrips()
    {
        var model = new SeaLiceModel();
        model.SeedExternalInfection(3.0);
        model.AdvanceDay();
        model.AdvanceDay();

        var copy = new SeaLiceModel();
        copy.ImportState(model.ExportState());

        Assert.Equal(model.AverageLicePerFish, copy.AverageLicePerFish);
        Assert.Equal(model.CopepodidDensity, copy.CopepodidDensity);
        copy.AdvanceDay();
        model.AdvanceDay();
        Assert.Equal(model.AverageLicePerFish, copy.AverageLicePerFish);
    }
}
