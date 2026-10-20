using System;
using System.IO;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// TD-04: GrowthModel/MortalityModel save re-injection. Loaded games must
    /// resume disease + chronic stress pressure exactly; growth must resume
    /// with identical deterministic output. Economy/biology numbers, save
    /// format version, and bridge API are untouched (additive snapshot slots).
    /// </summary>
    public class ModelSaveRoundTripTests
    {
        private static LakskongenSim.Environment.EnvironmentalDataPoint StressfulConditions() =>
            new()
            {
                TemperatureAt10m = 16.0,
                TemperatureAt30m = 16.0,
                OxygenAtCageDepth = 4.0,
                CurrentSpeedAtCageDepth = 0.2,
                EnvironmentalQualityScore = 0.4,
            };

        [Fact]
        public void Mortality_RoundTrip_PreservesDiseaseRates()
        {
            var live = new MortalityModel();
            live.SetDiseaseMortalityRate("pop-a", 0.012);
            live.SetDiseaseMortalityRate("pop-b", 0.003);
            int expectedA = live.ApplyMortality(10000, 30, LifeStage.Seawater, "pop-a");

            var restored = new MortalityModel();
            restored.RestoreFromSave(live.GetSaveSnapshot());

            Assert.Equal(expectedA, restored.ApplyMortality(10000, 30, LifeStage.Seawater, "pop-a"));
            Assert.Equal(
                live.ApplyMortality(10000, 30, LifeStage.Seawater, "pop-b"),
                restored.ApplyMortality(10000, 30, LifeStage.Seawater, "pop-b"));
        }

        [Fact]
        public void Mortality_RoundTrip_PreservesAccumulatedStress()
        {
            var live = new MortalityModel();
            var conditions = StressfulConditions();
            for (int i = 0; i < 5; i++)
                live.SetEnvironmentalStress("pop-a", conditions);
            double expectedMultiplier = live.GetStressMortalityMultiplier("pop-a");
            Assert.True(expectedMultiplier > 1.0, "Fixture must actually accumulate stress.");

            var restored = new MortalityModel();
            restored.RestoreFromSave(live.GetSaveSnapshot());

            Assert.Equal(expectedMultiplier, restored.GetStressMortalityMultiplier("pop-a"), precision: 10);
            // Unknown populations stay neutral on both sides.
            Assert.Equal(1.0, restored.GetStressMortalityMultiplier("pop-unknown"));
        }

        [Fact]
        public void Mortality_RestoreFromNull_ClearsStateForLegacySaves()
        {
            var model = new MortalityModel();
            model.SetDiseaseMortalityRate("pop-a", 0.012);
            model.SetEnvironmentalStress("pop-a", StressfulConditions());

            model.RestoreFromSave(null);

            Assert.Equal(
                new MortalityModel().ApplyMortality(10000, 30, LifeStage.Seawater, "pop-a"),
                model.ApplyMortality(10000, 30, LifeStage.Seawater, "pop-a"));
            Assert.Equal(1.0, model.GetStressMortalityMultiplier("pop-a"));
        }

        [Fact]
        public void Growth_RoundTrip_ResumesIdenticalDeterministicOutput()
        {
            var live = new GrowthModel(7);
            double expected = live.ApplyGrowth(0.5, 90, LifeStage.Seawater);

            var snapshot = live.GetSaveSnapshot();
            Assert.NotNull(snapshot);

            var restored = new GrowthModel(7);
            restored.RestoreFromSave(snapshot);
            restored.RestoreFromSave(null); // legacy-save path must also be safe

            Assert.Equal(expected, restored.ApplyGrowth(0.5, 90, LifeStage.Seawater), precision: 12);
        }

        [Fact]
        public void Engine_RoundTrip_PreservesModelStateThroughSaveLoad()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false);
            engine.Mortality.SetDiseaseMortalityRate("test-pop", 0.008);
            for (int i = 0; i < 3; i++)
                engine.Mortality.SetEnvironmentalStress("test-pop", StressfulConditions());
            int expectedSurvivors = engine.Mortality.ApplyMortality(20000, 30, LifeStage.Seawater, "test-pop");
            double expectedStress = engine.Mortality.GetStressMortalityMultiplier("test-pop");

            engine.PrepareForSave();
            Assert.NotNull(engine.State.MortalityState);
            Assert.NotNull(engine.State.GrowthState);

            // Full JSON file round-trip proves the new slots serialize.
            string dir = Path.Combine(Path.GetTempPath(), "lakskongen-td04-" + Guid.NewGuid().ToString("N"));
            try
            {
                var saves = new SaveLoadManager(dir);
                saves.SaveGame(engine.State, "td04");
                var reloadedState = saves.LoadGame("td04");

                var loaded = new SimulationEngine();
                loaded.Initialize(enableEvents: false);
                loaded.RestoreFromLoad(reloadedState);

                Assert.Equal(expectedSurvivors,
                    loaded.Mortality.ApplyMortality(20000, 30, LifeStage.Seawater, "test-pop"));
                Assert.Equal(expectedStress,
                    loaded.Mortality.GetStressMortalityMultiplier("test-pop"), precision: 10);

                // Populations resume advancing with re-injected models.
                var pop = new FishPopulation(loaded.Growth, loaded.Mortality) { ZoneId = "PO04" };
                pop.AddCohort(new FishPopulation.Cohort(400, 1000, 1.5, LifeStage.Seawater));
                pop.AdvanceDays(7);
                Assert.Single(pop.Cohorts);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }
    }
}
