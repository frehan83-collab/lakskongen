using System;
using System.IO;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.SaveLoadTests;

/// <summary>
/// Hardening tests for the additive <see cref="SmoltQualityTier"/> cohort field
/// and the scenario starting-state hand-off through a JSON save round-trip.
/// </summary>
public class CohortQualitySaveTests : IDisposable
{
    private readonly string _testDir;
    private readonly SaveLoadManager _manager;

    public CohortQualitySaveTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "CohortQualityTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _manager = new SaveLoadManager(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void RoundTrip_PreservesSuperSmoltQuality()
    {
        var state = new GameState { Day = 300, Balance = 5_000_000m };
        var pop = new FishPopulation { ZoneId = "vest_1", SiteId = "site_1" };
        var cohort = new FishPopulation.Cohort(300, 15_000, 0.4, LifeStage.Smolt)
        {
            Quality = SmoltQualityTier.SuperSmolt
        };
        pop.AddCohort(cohort);
        state.Populations.Add(pop);

        _manager.SaveGame(state, "quality_roundtrip");
        var loaded = _manager.LoadGame("quality_roundtrip");

        var loadedCohort = loaded.Populations.Single().Cohorts.Single();
        Assert.Equal(SmoltQualityTier.SuperSmolt, loadedCohort.Quality);
        Assert.Equal(15_000, loadedCohort.Count);
    }

    [Fact]
    public void RoundTrip_UndefinedQuality_ClampsToStandard()
    {
        // Simulates a hand-edited/future save carrying an out-of-range enum
        // value. The deserializer must not let it reach the quality registry.
        const string json = """
        {
          "Day": 42,
          "Balance": 1000000,
          "Populations": [
            {
              "Id": "pop-bad",
              "ZoneId": "vest_1",
              "Cohorts": [
                { "AgeDays": 300, "Count": 1000, "MeanWeightKg": 0.4, "Stage": "Smolt", "Quality": 99 }
              ]
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(_testDir, "undefined_quality.json"), json);

        var loaded = _manager.LoadGame("undefined_quality");

        var loadedCohort = loaded.Populations.Single().Cohorts.Single();
        Assert.Equal(SmoltQualityTier.Standard, loadedCohort.Quality);
    }

    [Fact]
    public void RoundTrip_ScenarioStartingState_AppliesAndSurvives()
    {
        var state = new GameState();
        var scenario = new ScenarioSystem(new GameState()).GetScenario("ressursrente");
        Assert.NotNull(scenario);

        scenario!.ApplyStartingState(state);
        Assert.Equal(scenario.StartingSites.Count, state.SeaSites.Count);
        Assert.True(state.Populations.Count > 0, "scenario should stock populations");
        Assert.True(state.Populations.Sum(p => p.TotalCount) > 0, "scenario fish should be live");

        _manager.SaveGame(state, "scenario_start");
        var loaded = _manager.LoadGame("scenario_start");

        Assert.Equal(scenario.StartingSites.Count, loaded.SeaSites.Count);
        Assert.Equal(state.Populations.Count, loaded.Populations.Count);
        Assert.All(loaded.Populations, p =>
            Assert.All(p.Cohorts, c => Assert.Equal(SmoltQualityTier.Standard, c.Quality)));
    }
}
