#nullable enable
using System;
using Xunit;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.Core
{
    public class DifficultyTests
    {
        [Fact]
        public void CreatePreset_Easy_HasCorrectMultipliers()
        {
            // Arrange & Act
            var settings = DifficultySettings.CreatePreset(DifficultyLevel.Easy);

            // Assert
            Assert.Equal(DifficultyLevel.Easy, settings.Level);
            Assert.Equal(1.3, settings.FishGrowthRateMultiplier);
            Assert.Equal(0.5, settings.DiseaseFrequencyMultiplier);
            Assert.Equal(0.5, settings.MarketVolatilityMultiplier);
            Assert.Equal(1.5, settings.StartingCapitalMultiplier);
            Assert.Equal(0.5, settings.LiceGrowthRateMultiplier);
            Assert.Equal(0.5, settings.RegulatoryStrictnessMultiplier);
            Assert.Equal(0.8, settings.FeedCostMultiplier);
            Assert.Contains("Lett", settings.Description);
        }

        [Fact]
        public void CreatePreset_Normal_HasCorrectMultipliers()
        {
            // Arrange & Act
            var settings = DifficultySettings.CreatePreset(DifficultyLevel.Normal);

            // Assert
            Assert.Equal(DifficultyLevel.Normal, settings.Level);
            Assert.Equal(1.0, settings.FishGrowthRateMultiplier);
            Assert.Equal(1.0, settings.DiseaseFrequencyMultiplier);
            Assert.Equal(1.0, settings.MarketVolatilityMultiplier);
            Assert.Equal(1.0, settings.StartingCapitalMultiplier);
            Assert.Equal(1.0, settings.LiceGrowthRateMultiplier);
            Assert.Equal(1.0, settings.RegulatoryStrictnessMultiplier);
            Assert.Equal(1.0, settings.FeedCostMultiplier);
            Assert.Contains("Normal", settings.Description);
        }

        [Fact]
        public void CreatePreset_Hard_HasCorrectMultipliers()
        {
            // Arrange & Act
            var settings = DifficultySettings.CreatePreset(DifficultyLevel.Hard);

            // Assert
            Assert.Equal(DifficultyLevel.Hard, settings.Level);
            Assert.Equal(0.8, settings.FishGrowthRateMultiplier);
            Assert.Equal(1.5, settings.DiseaseFrequencyMultiplier);
            Assert.Equal(1.5, settings.MarketVolatilityMultiplier);
            Assert.Equal(0.7, settings.StartingCapitalMultiplier);
            Assert.Equal(1.5, settings.LiceGrowthRateMultiplier);
            Assert.Equal(1.2, settings.RegulatoryStrictnessMultiplier);
            Assert.Equal(1.2, settings.FeedCostMultiplier);
            Assert.Contains("Vanskelig", settings.Description);
        }

        [Fact]
        public void CreatePreset_Realistic_HasCorrectMultipliers()
        {
            // Arrange & Act
            var settings = DifficultySettings.CreatePreset(DifficultyLevel.Realistic);

            // Assert
            Assert.Equal(DifficultyLevel.Realistic, settings.Level);
            Assert.Equal(0.9, settings.FishGrowthRateMultiplier);
            Assert.Equal(1.2, settings.DiseaseFrequencyMultiplier);
            Assert.Equal(1.0, settings.MarketVolatilityMultiplier);
            Assert.Equal(0.8, settings.StartingCapitalMultiplier);
            Assert.Equal(1.3, settings.LiceGrowthRateMultiplier);
            Assert.Equal(1.0, settings.RegulatoryStrictnessMultiplier);
            Assert.Equal(1.0, settings.FeedCostMultiplier);
            Assert.Contains("Realistisk", settings.Description);
        }

        [Fact]
        public void DifficultyManager_DefaultsToNormal()
        {
            // Arrange & Act
            var manager = new DifficultyManager();

            // Assert
            Assert.NotNull(manager.CurrentSettings);
            Assert.Equal(DifficultyLevel.Normal, manager.CurrentSettings.Level);
            Assert.Equal(1.0, manager.CurrentSettings.FishGrowthRateMultiplier);
        }

        [Fact]
        public void DifficultyManager_SetDifficulty_ChangesSettings()
        {
            // Arrange
            var manager = new DifficultyManager();
            bool eventRaised = false;
            DifficultySettings? capturedSettings = null;
            manager.OnDifficultyChanged += (settings) =>
            {
                eventRaised = true;
                capturedSettings = settings;
            };

            // Act
            manager.SetDifficulty(DifficultyLevel.Hard);

            // Assert
            Assert.True(eventRaised);
            Assert.NotNull(capturedSettings);
            Assert.Equal(DifficultyLevel.Hard, manager.CurrentSettings.Level);
            Assert.Equal(0.8, manager.CurrentSettings.FishGrowthRateMultiplier);
        }

        [Fact]
        public void DifficultyManager_GetAdjustedGrowthRate_AppliesMultiplier()
        {
            // Arrange
            var manager = new DifficultyManager();
            manager.SetDifficulty(DifficultyLevel.Easy);
            double baseRate = 0.0025;

            // Act
            double adjustedRate = manager.GetAdjustedGrowthRate(baseRate);

            // Assert
            Assert.Equal(0.0025 * 1.3, adjustedRate, 6);
        }

        [Fact]
        public void DifficultyManager_GetAdjustedStartingCapital_AppliesMultiplier()
        {
            // Arrange
            var manager = new DifficultyManager();
            manager.SetDifficulty(DifficultyLevel.Easy);
            decimal baseCapital = 1_000_000m;

            // Act
            decimal adjustedCapital = manager.GetAdjustedStartingCapital(baseCapital);

            // Assert
            Assert.Equal(1_500_000m, adjustedCapital);
        }

        [Fact]
        public void DifficultyManager_GetAdjustedFeedCost_AppliesMultiplier()
        {
            // Arrange
            var manager = new DifficultyManager();
            manager.SetDifficulty(DifficultyLevel.Hard);
            decimal baseCost = 100m;

            // Act
            decimal adjustedCost = manager.GetAdjustedFeedCost(baseCost);

            // Assert
            Assert.Equal(120m, adjustedCost);
        }

        [Fact]
        public void CustomDifficultyBuilder_BuildsCorrectSettings()
        {
            // Arrange
            var builder = new DifficultyManager.CustomDifficultyBuilder();

            // Act
            var settings = builder
                .WithFishGrowthRate(1.5)
                .WithDiseaseFrequency(0.3)
                .WithMarketVolatility(2.0)
                .WithDescription("Test Custom")
                .Build();

            // Assert
            Assert.Equal(DifficultyLevel.Custom, settings.Level);
            Assert.Equal(1.5, settings.FishGrowthRateMultiplier);
            Assert.Equal(0.3, settings.DiseaseFrequencyMultiplier);
            Assert.Equal(2.0, settings.MarketVolatilityMultiplier);
            Assert.Equal("Test Custom", settings.Description);
        }

        [Fact]
        public void DifficultyManager_SetCustomDifficulty_SetsLevelToCustom()
        {
            // Arrange
            var manager = new DifficultyManager();
            var customSettings = new DifficultyManager.CustomDifficultyBuilder()
                .WithFishGrowthRate(2.0)
                .Build();

            // Act
            manager.SetCustomDifficulty(customSettings);

            // Assert
            Assert.Equal(DifficultyLevel.Custom, manager.CurrentSettings.Level);
            Assert.Equal(2.0, manager.CurrentSettings.FishGrowthRateMultiplier);
        }

        [Fact]
        public void DifficultySettings_Clone_CreatesIndependentCopy()
        {
            // Arrange
            var original = DifficultySettings.CreatePreset(DifficultyLevel.Easy);

            // Act
            var clone = original.Clone();
            clone.FishGrowthRateMultiplier = 2.5;

            // Assert
            Assert.Equal(1.3, original.FishGrowthRateMultiplier);
            Assert.Equal(2.5, clone.FishGrowthRateMultiplier);
        }

        [Fact]
        public void DifficultyManager_GetAdjustedDiseaseFrequency_AppliesMultiplier()
        {
            // Arrange
            var manager = new DifficultyManager();
            manager.SetDifficulty(DifficultyLevel.Realistic);
            double baseFrequency = 0.1;

            // Act
            double adjustedFrequency = manager.GetAdjustedDiseaseFrequency(baseFrequency);

            // Assert
            Assert.Equal(0.12, adjustedFrequency, 6);
        }

        [Fact]
        public void DifficultyManager_GetAdjustedLiceGrowthRate_AppliesMultiplier()
        {
            // Arrange
            var manager = new DifficultyManager();
            manager.SetDifficulty(DifficultyLevel.Hard);
            double baseRate = 0.05;

            // Act
            double adjustedRate = manager.GetAdjustedLiceGrowthRate(baseRate);

            // Assert
            Assert.Equal(0.075, adjustedRate, 6);
        }

        [Fact]
        public void DifficultyManager_GetAdjustedMarketVolatility_AppliesMultiplier()
        {
            // Arrange
            var manager = new DifficultyManager();
            manager.SetDifficulty(DifficultyLevel.Easy);
            double baseVolatility = 10.0;

            // Act
            double adjustedVolatility = manager.GetAdjustedMarketVolatility(baseVolatility);

            // Assert
            Assert.Equal(5.0, adjustedVolatility, 6);
        }

        [Fact]
        public void DifficultyManager_GetAdjustedRegulatoryStrictness_AppliesMultiplier()
        {
            // Arrange
            var manager = new DifficultyManager();
            manager.SetDifficulty(DifficultyLevel.Hard);
            double baseStrictness = 1.0;

            // Act
            double adjustedStrictness = manager.GetAdjustedRegulatoryStrictness(baseStrictness);

            // Assert
            Assert.Equal(1.2, adjustedStrictness, 6);
        }

        [Fact]
        public void CustomDifficultyBuilder_AllParametersCanBeSet()
        {
            // Arrange
            var builder = new DifficultyManager.CustomDifficultyBuilder();

            // Act
            var settings = builder
                .WithFishGrowthRate(1.1)
                .WithDiseaseFrequency(1.2)
                .WithMarketVolatility(1.3)
                .WithStartingCapital(1.4)
                .WithLiceGrowthRate(1.5)
                .WithRegulatoryStrictness(1.6)
                .WithFeedCost(1.7)
                .WithDescription("All params")
                .Build();

            // Assert
            Assert.Equal(1.1, settings.FishGrowthRateMultiplier);
            Assert.Equal(1.2, settings.DiseaseFrequencyMultiplier);
            Assert.Equal(1.3, settings.MarketVolatilityMultiplier);
            Assert.Equal(1.4, settings.StartingCapitalMultiplier);
            Assert.Equal(1.5, settings.LiceGrowthRateMultiplier);
            Assert.Equal(1.6, settings.RegulatoryStrictnessMultiplier);
            Assert.Equal(1.7, settings.FeedCostMultiplier);
            Assert.Equal("All params", settings.Description);
        }
    }
}
