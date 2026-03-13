using System;
using System.IO;
using System.Linq;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Events;
using LakskongenSim.Processing;
using LakskongenSim.Data;

namespace LakskongenSim.SaveLoad.Tests
{
    public class SaveLoadTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly SaveLoadManager _manager;

        public SaveLoadTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "salmon-sim-saveload-tests-" + Guid.NewGuid());
            _manager = new SaveLoadManager(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ─── GameState top-level fields ─────────────────────────────────────────

        [Fact]
        public void RoundTrip_TopLevelFields_Preserved()
        {
            var state = new GameState { Day = 42, Balance = 1_234_567.89m };
            _manager.SaveGame(state);
            var loaded = _manager.LoadGame();

            Assert.Equal(42, loaded.Day);
            Assert.Equal(1_234_567.89m, loaded.Balance);
        }

        // ─── Zone / WaterChemistry ───────────────────────────────────────────────

        [Fact]
        public void RoundTrip_Zone_WithWaterChemistry_Preserved()
        {
            var zone = new Zone("fjord-1", temperature: 12.5);
            zone.CarryingCapacityTonnes = 500.0;
            zone.CurrentFishBiomassKg = 12_000.0;
            zone.WaterFlowRate = 2500.0;
            zone.FeedConversionRatio = 1.15;
            // Simulate a few days so chemistry drifts from baseline
            zone.AdvanceDays(5);

            var state = new GameState();
            state.Zones.Add(zone);
            _manager.SaveGame(state);
            var loaded = _manager.LoadGame();

            Assert.Single(loaded.Zones);
            var z = loaded.Zones[0];
            Assert.Equal("fjord-1", z.Id);
            Assert.Equal(500.0, z.CarryingCapacityTonnes);
            Assert.Equal(12_000.0, z.CurrentFishBiomassKg);
            Assert.Equal(2500.0, z.WaterFlowRate);
            Assert.Equal(1.15, z.FeedConversionRatio);

            // WaterChemistry state
            Assert.Equal(zone.WaterChemistry.AmmoniaNH4,   z.WaterChemistry.AmmoniaNH4,   precision: 6);
            Assert.Equal(zone.WaterChemistry.NitriteNO2,   z.WaterChemistry.NitriteNO2,   precision: 6);
            Assert.Equal(zone.WaterChemistry.NitrateNO3,   z.WaterChemistry.NitrateNO3,   precision: 6);
            Assert.Equal(zone.WaterChemistry.PhosphorusPO4, z.WaterChemistry.PhosphorusPO4, precision: 6);
            Assert.Equal(zone.WaterChemistry.OxygenO2,     z.WaterChemistry.OxygenO2,     precision: 6);
            Assert.Equal(zone.WaterChemistry.pH,           z.WaterChemistry.pH,           precision: 6);
            Assert.Equal(zone.WaterChemistry.Temperature,  z.WaterChemistry.Temperature,  precision: 6);
        }

        // ─── FishPopulation ──────────────────────────────────────────────────────

        [Fact]
        public void RoundTrip_FishPopulation_CohortsAndNewFields_Preserved()
        {
            var pop = new FishPopulation(new GrowthModel(), new MortalityModel());
            pop.ZoneId = "fjord-1";
            pop.HasRecentTreatment = true;
            pop.TreatmentIntensity = 0.75;
            pop.ApplyGrowthPenalty(0.2);
            pop.AddCohort(new FishPopulation.Cohort(400, 10_000, 2.5, LifeStage.Seawater));
            pop.AddCohort(new FishPopulation.Cohort(200, 5_000,  0.8, LifeStage.Parr));

            var state = new GameState();
            state.Populations.Add(pop);
            _manager.SaveGame(state);
            var loaded = _manager.LoadGame();

            Assert.Single(loaded.Populations);
            var p = loaded.Populations[0];
            Assert.Equal("fjord-1", p.ZoneId);
            Assert.True(p.HasRecentTreatment);
            Assert.Equal(0.75, p.TreatmentIntensity, precision: 6);
            Assert.Equal(0.2,  p.GetGrowthPenalty(),  precision: 6);
            Assert.Equal(2,    p.Cohorts.Count);

            var c0 = p.Cohorts[0];
            Assert.Equal(400,             c0.AgeDays);
            Assert.Equal(10_000,          c0.Count);
            Assert.Equal(2.5,             c0.MeanWeightKg, precision: 6);
            Assert.Equal(LifeStage.Seawater, c0.Stage);

            var c1 = p.Cohorts[1];
            Assert.Equal(200,          c1.AgeDays);
            Assert.Equal(5_000,        c1.Count);
            Assert.Equal(LifeStage.Parr, c1.Stage);
        }

        // ─── SeaSite ────────────────────────────────────────────────────────────

        [Fact]
        public void RoundTrip_SeaSite_AllFields_Preserved()
        {
            var site = new SeaSite("site-alpha", "fjord-1");
            site.MABTonnes = 900.0;
            site.IsClosedContainment = true;
            site.MaxAllowedDensity = 20.0f;
            site.PenVolume = 60_000f;
            site.NumberOfPens = 10;
            site.RecordEscape(150);

            var pop = new FishPopulation(new GrowthModel(), new MortalityModel());
            pop.AddCohort(new FishPopulation.Cohort(500, 20_000, 4.0, LifeStage.Harvest));
            site.Stock(pop);

            var state = new GameState();
            state.SeaSites.Add(site);
            _manager.SaveGame(state);
            var loaded = _manager.LoadGame();

            Assert.Single(loaded.SeaSites);
            var s = loaded.SeaSites[0];
            Assert.Equal("site-alpha",  s.Id);
            Assert.Equal("fjord-1",     s.ZoneId);
            Assert.Equal(900.0,         s.MABTonnes);
            Assert.True(s.IsClosedContainment);
            Assert.Equal(20.0f,         s.MaxAllowedDensity);
            Assert.Equal(60_000f,       s.PenVolume);
            Assert.Equal(10,            s.NumberOfPens);
            Assert.Equal(150,           s.EscapesThisYear);
            Assert.Single(s.Pens);
            Assert.Single(s.Pens[0].Cohorts);
        }

        // ─── EventLog ───────────────────────────────────────────────────────────

        [Fact]
        public void RoundTrip_EventLog_Preserved()
        {
            var state = new GameState { Day = 10 };
            state.EventLog.Add(new EventRecord
            {
                Day = 10,
                Id = "STORM-001",
                Name = "Nordavind storm",
                Category = EventCategory.Weather,
                Description = "Severe storm in Hardangerfjord.",
                BalanceChange = -250_000m
            });

            _manager.SaveGame(state);
            var loaded = _manager.LoadGame();

            Assert.Single(loaded.EventLog);
            var e = loaded.EventLog[0];
            Assert.Equal("STORM-001",            e.Id);
            Assert.Equal(EventCategory.Weather,  e.Category);
            Assert.Equal(-250_000m,              e.BalanceChange);
        }

        // ─── ProcessingSnapshots ─────────────────────────────────────────────────

        [Fact]
        public void RoundTrip_ProcessingSnapshots_Preserved()
        {
            var state = new GameState();
            state.ProcessingSnapshots.Add(new ProcessingSnapshot
            {
                PlantId = "plant-bergen",
                Tier = ProcessingTier.Large,
                LastInputKg = 50_000,
                LastOutputKg = 44_000,
                LastProfit = 3_200_000m,
                CumulativeProfit = 48_000_000m
            });

            _manager.SaveGame(state);
            var loaded = _manager.LoadGame();

            Assert.Single(loaded.ProcessingSnapshots);
            var snap = loaded.ProcessingSnapshots[0];
            Assert.Equal("plant-bergen",         snap.PlantId);
            Assert.Equal(ProcessingTier.Large,   snap.Tier);
            Assert.Equal(50_000,                 snap.LastInputKg);
            Assert.Equal(3_200_000m,             snap.LastProfit);
            Assert.Equal(48_000_000m,            snap.CumulativeProfit);
        }

        // ─── Backup rotation ────────────────────────────────────────────────────

        [Fact]
        public void BackupRotation_CreatesUpToThreeBackups()
        {
            // Save 4 times — should keep backups .bak1, .bak2, .bak3 (no .bak4)
            for (int i = 0; i < 4; i++)
            {
                _manager.SaveGame(new GameState { Day = i * 10 });
            }

            string base_ = Path.Combine(_tempDir, "save.json");
            Assert.True(File.Exists(base_),           "Primary save should exist.");
            Assert.True(File.Exists(base_ + ".bak1"), ".bak1 should exist.");
            Assert.True(File.Exists(base_ + ".bak2"), ".bak2 should exist.");
            Assert.True(File.Exists(base_ + ".bak3"), ".bak3 should exist.");
            Assert.False(File.Exists(base_ + ".bak4"), ".bak4 should NOT exist.");
        }

        [Fact]
        public void BackupRotation_Bak1IsSecondMostRecentSave()
        {
            _manager.SaveGame(new GameState { Day = 100 }); // save #1
            _manager.SaveGame(new GameState { Day = 200 }); // save #2 → #1 becomes bak1

            var bak1Json = File.ReadAllText(Path.Combine(_tempDir, "save.json.bak1"));
            var bak1State = System.Text.Json.JsonSerializer.Deserialize<GameState>(bak1Json,
                new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });

            Assert.NotNull(bak1State);
            Assert.Equal(100, bak1State!.Day);
        }

        [Fact]
        public void TryLoadWithFallback_ReturnsNull_WhenNoFilesExist()
        {
            var result = _manager.TryLoadWithFallback("nonexistent");
            Assert.Null(result);
        }

        // ─── Full round-trip smoke test ──────────────────────────────────────────

        [Fact]
        public void FullRoundTrip_ComplexState_AllSubsystemsIntact()
        {
            var state = new GameState { Day = 365, Balance = 10_000_000m };

            var zone = new Zone("zone-alpha", 8.0);
            zone.AdvanceDays(30);
            state.Zones.Add(zone);

            var pop = new FishPopulation(new GrowthModel(), new MortalityModel());
            pop.ZoneId = "zone-alpha";
            pop.HasRecentTreatment = false;
            pop.TreatmentIntensity = 0.0;
            pop.AddCohort(new FishPopulation.Cohort(600, 25_000, 5.2, LifeStage.Harvest));
            state.Populations.Add(pop);

            var site = new SeaSite("site-bergen", "zone-alpha")
            {
                MABTonnes = 780,
                IsClosedContainment = false
            };
            state.SeaSites.Add(site);

            state.EventLog.Add(new EventRecord
            {
                Day = 100, Id = "ISA-001", Name = "ISA outbreak",
                Category = EventCategory.Disease, Description = "Confirmed ISA at site-bergen.",
                BalanceChange = -5_000_000m
            });

            state.ProcessingSnapshots.Add(new ProcessingSnapshot
            {
                PlantId = "plant-stavanger", Tier = ProcessingTier.Medium,
                LastInputKg = 30_000, LastOutputKg = 26_500,
                LastProfit = 1_500_000m, CumulativeProfit = 22_000_000m
            });

            _manager.SaveGame(state, "autosave");
            var loaded = _manager.LoadGame("autosave");

            Assert.Equal(365,            loaded.Day);
            Assert.Equal(10_000_000m,    loaded.Balance);
            Assert.Single(loaded.Zones);
            Assert.Single(loaded.Populations);
            Assert.Single(loaded.SeaSites);
            Assert.Single(loaded.EventLog);
            Assert.Single(loaded.ProcessingSnapshots);

            Assert.Equal("zone-alpha",   loaded.Populations[0].ZoneId);
            Assert.Equal(5.2,            loaded.Populations[0].Cohorts[0].MeanWeightKg, precision: 6);
            Assert.Equal(EventCategory.Disease, loaded.EventLog[0].Category);
            Assert.Equal(ProcessingTier.Medium, loaded.ProcessingSnapshots[0].Tier);
        }
    }
}
