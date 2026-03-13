using System;
using System.IO;
using Xunit;
using LakskongenSim.Core;

namespace LakskongenSim.Health.Tests
{
    public class BarentsWatchIntegrationTests
    {
        [Fact]
        public void Normalizer_ProducesStableExternalSnapshot()
        {
            var raw = new BarentsWatchFishHealthRawSnapshot
            {
                RetrievedAtUtc = new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc),
                Year = 2026,
                Week = 11,
                ProductionAreas =
                {
                    new BarentsWatchProductionAreaRecord
                    {
                        RegionId = "NO-VL",
                        RegionName = "Vestland",
                        BiomassUtilization = 0.91,
                        LicePerFish = 0.48,
                        DiseaseCasesPer100Sites = 6.5,
                        MortalityPercent = 4.1,
                        TemperatureDeviationC = 1.4,
                        TreatmentCountPerMonth = 2.6,
                        LocalityCount = 28,
                        EscapesReported = 3,
                        EnvironmentalAlerts = 2
                    }
                }
            };

            var normalized = BarentsWatchFishHealthNormalizer.Normalize(raw);

            Assert.Equal(2026, normalized.SnapshotYear);
            Assert.Equal(11, normalized.SnapshotWeek);
            Assert.True(normalized.IndustryLicePressure > 0.40);
            Assert.True(normalized.DiseasePressureIndex > 1.0);
            Assert.Single(normalized.RegionalProfiles);
        }

        [Fact]
        public void Pipeline_IngestsAndPersistsOfflineSnapshot()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "bw-fishhealth-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            try
            {
                var rawPath = Path.Combine(tempDir, "raw.json");
                File.WriteAllText(rawPath, @"{
  ""retrievedAtUtc"": ""2026-03-13T12:00:00Z"",
  ""year"": 2026,
  ""week"": 11,
  ""sourceName"": ""BarentsWatch Fishhealth"",
  ""productionAreas"": [
    {
      ""regionId"": ""NO-VL"",
      ""regionName"": ""Vestland"",
      ""biomassUtilization"": 0.91,
      ""licePerFish"": 0.48,
      ""diseaseCasesPer100Sites"": 6.5,
      ""mortalityPercent"": 4.1,
      ""temperatureDeviationC"": 1.4,
      ""treatmentCountPerMonth"": 2.6,
      ""localityCount"": 28,
      ""escapesReported"": 3,
      ""environmentalAlerts"": 2
    }
  ]
}");

                var client = new BarentsWatchFishHealthClient(new BarentsWatchClientOptions
                {
                    LocalSnapshotPath = rawPath
                });
                var repo = new BarentsWatchSnapshotRepository(Path.Combine(tempDir, "snapshots"));
                var pipeline = new BarentsWatchFishHealthPipeline(client, repo);

                var stored = pipeline.IngestLatest();
                var loaded = pipeline.LoadLatest();

                Assert.NotNull(loaded);
                Assert.Equal(stored.Normalized.SnapshotWeek, loaded!.Normalized.SnapshotWeek);
                Assert.NotEmpty(stored.Warnings);
                Assert.True(stored.Analytics.DataConfidence > 0.0);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void CrisisEngine_FlagsHighRiskConditions()
        {
            var snapshot = new ExternalAquacultureSnapshot
            {
                SnapshotYear = 2026,
                SnapshotWeek = 11,
                IndustryLicePressure = 0.62,
                DiseasePressureIndex = 1.52,
                MortalityStressIndex = 1.48,
                TemperatureAnomalyIndex = 1.18,
                TreatmentIntensityIndex = 1.20,
                RegionalProfiles = new()
                {
                    new RegionalAquacultureProfile
                    {
                        RegionId = "NO-VL",
                        RegionName = "Vestland",
                        LicePressure = 0.70,
                        DiseasePressure = 1.60
                    }
                }
            };

            var warnings = BarentsWatchHealthCrisisEngine.BuildWarnings(snapshot);
            var scenario = BarentsWatchHealthCrisisEngine.BuildScenario(snapshot);
            var report = BarentsWatchHealthCrisisEngine.BuildReport(snapshot);

            Assert.NotEmpty(warnings);
            Assert.Equal("disease-wave", scenario.CrisisType);
            Assert.True(report.CompositeRiskScore > 1.0);
        }
    }
}
