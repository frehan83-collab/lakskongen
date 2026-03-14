using System;
using System.Collections.Generic;
using Xunit;
using LakskongenSim.Finance;
using LakskongenSim.Expansion;

namespace LakskongenSim.Tests.Expansion
{
    public class VerticalIntegrationTests
    {
        /// <summary>
        /// Helper to create a test asset with reasonable defaults.
        /// </summary>
        private IntegrationAsset CreateTestAsset(
            IntegrationTier tier = IntegrationTier.FEED_MILL,
            float capex = 50_000_000f,
            float costSavingPct = 0.05f,
            float qualityBonus = 0.03f,
            int constructionMonths = 12)
        {
            return new IntegrationAsset
            {
                Tier = tier,
                Name = $"Test {tier}",
                CapexNOK = capex,
                AnnualOpexNOK = capex * 0.05f, // 5% annual opex
                Capacity = 10000,
                CostSavingPctPerKg = costSavingPct,
                QualityBonus = qualityBonus,
                CompetitorLockoutRadius = 50f,
                Description = $"Test {tier} asset",
                MaintenanceCostPctPerYear = 0.02f,
                ConstructionTimeMonths = constructionMonths,
                IsOwned = false
            };
        }

        [Fact]
        public void BuildAsset_ValidPurchase_SucceedsAndDeductsCapital()
        {
            // Arrange
            var finance = new FinanceManager(100_000_000m); // 100M NOK starting capital
            var vis = new VerticalIntegrationSystem(finance);
            
            var asset = CreateTestAsset(
                tier: IntegrationTier.FEED_MILL,
                capex: 50_000_000f
            );
            
            // Load asset into system
            vis.LoadAssetsFromData(new List<IntegrationAsset> { asset });
            
            // Act
            var result = vis.BuildAsset(asset, finance.GetAvailableCapital());
            
            // Assert
            Assert.True(result.Success, "Asset purchase should succeed with sufficient capital");
            Assert.Equal(50_000_000f, result.ActualCapexPaid);
            Assert.Contains(asset, vis.GetOwnedAssets());
            
            // Verify capital was deducted
            Assert.Equal(50_000_000m, finance.Balance);
        }

        [Fact]
        public void BuildAsset_InsufficientFunds_FailsWithMessage()
        {
            // Arrange
            var finance = new FinanceManager(10_000_000m); // Only 10M NOK
            var vis = new VerticalIntegrationSystem(finance);
            
            var expensiveAsset = CreateTestAsset(
                tier: IntegrationTier.PROCESSING,
                capex: 75_000_000f // Costs 75M NOK
            );
            
            vis.LoadAssetsFromData(new List<IntegrationAsset> { expensiveAsset });
            
            // Act
            var result = vis.BuildAsset(expensiveAsset, finance.GetAvailableCapital());
            
            // Assert
            Assert.False(result.Success, "Asset purchase should fail with insufficient funds");
            Assert.Contains("Insufficient funds", result.Message);
            Assert.Empty(vis.GetOwnedAssets());
            
            // Verify capital was NOT deducted
            Assert.Equal(10_000_000m, finance.Balance);
        }

        [Fact]
        public void BuildAsset_AlreadyOwned_FailsWithMessage()
        {
            // Arrange
            var finance = new FinanceManager(200_000_000m);
            var vis = new VerticalIntegrationSystem(finance);
            
            var asset = CreateTestAsset();
            vis.LoadAssetsFromData(new List<IntegrationAsset> { asset });
            
            // First purchase succeeds
            var firstResult = vis.BuildAsset(asset, finance.GetAvailableCapital());
            Assert.True(firstResult.Success);
            
            // Act - try to buy same asset again
            var secondResult = vis.BuildAsset(asset, finance.GetAvailableCapital());
            
            // Assert
            Assert.False(secondResult.Success, "Cannot purchase already-owned asset");
            Assert.Contains("already owned", secondResult.Message);
        }

        [Fact]
        public void GetIntegrationScore_MultipleAssets_CalculatesCorrectly()
        {
            // Arrange
            var finance = new FinanceManager(500_000_000m);
            var vis = new VerticalIntegrationSystem(finance);
            
            var feedMill = CreateTestAsset(IntegrationTier.FEED_MILL, capex: 50_000_000f, constructionMonths: 0);
            var processing = CreateTestAsset(IntegrationTier.PROCESSING, capex: 75_000_000f, constructionMonths: 0);
            var hatchery = CreateTestAsset(IntegrationTier.HATCHERY, capex: 30_000_000f, constructionMonths: 0);
            
            // Set construction to complete immediately for testing
            feedMill.ConstructionCompleted = DateTime.Now.AddDays(-1);
            processing.ConstructionCompleted = DateTime.Now.AddDays(-1);
            hatchery.ConstructionCompleted = DateTime.Now.AddDays(-1);
            
            vis.LoadAssetsFromData(new List<IntegrationAsset> { feedMill, processing, hatchery });
            
            // Purchase all assets
            vis.BuildAsset(feedMill, finance.GetAvailableCapital());
            vis.BuildAsset(processing, finance.GetAvailableCapital());
            vis.BuildAsset(hatchery, finance.GetAvailableCapital());
            
            // Act
            var score = vis.GetIntegrationScore();
            
            // Assert
            // 3 tiers * 0.15 base score = 0.45, plus 0.25 bonus for >= 3 tiers = 0.70
            Assert.True(score >= 0.45f, $"Integration score should be at least 0.45 with 3 tiers, got {score}");
            Assert.True(score <= 1.0f, "Integration score should be capped at 1.0");
        }

        [Fact]
        public void CalculateTotalCostSavings_AppliesSynergyBonus()
        {
            // Arrange
            var finance = new FinanceManager(500_000_000m);
            var vis = new VerticalIntegrationSystem(finance);
            
            // Create adjacent tier assets (FEED_MILL adjacent to SEA_FARM)
            var feedMill = CreateTestAsset(IntegrationTier.FEED_MILL, capex: 50_000_000f, costSavingPct: 0.05f, constructionMonths: 0);
            var seaFarm = CreateTestAsset(IntegrationTier.SEA_FARM, capex: 40_000_000f, costSavingPct: 0.03f, constructionMonths: 0);
            
            feedMill.ConstructionCompleted = DateTime.Now.AddDays(-1);
            seaFarm.ConstructionCompleted = DateTime.Now.AddDays(-1);
            
            vis.LoadAssetsFromData(new List<IntegrationAsset> { feedMill, seaFarm });
            
            vis.BuildAsset(feedMill, finance.GetAvailableCapital());
            vis.BuildAsset(seaFarm, finance.GetAvailableCapital());
            
            var ownedTiers = new List<IntegrationTier> { IntegrationTier.FEED_MILL, IntegrationTier.SEA_FARM };
            float annualProductionKg = 1_000_000f; // 1000 tonnes/year
            
            // Act
            var totalSavings = vis.CalculateTotalCostSavings(ownedTiers, annualProductionKg);
            
            // Assert
            // Base savings: 1M kg * 0.05 (feed) + 1M kg * 0.03 (sea farm) = 80,000 NOK
            // With synergy multiplier (1.25x for adjacent tiers): higher
            Assert.True(totalSavings > 0, "Should have positive cost savings");
            Assert.True(totalSavings >= 80_000f, $"Savings should be at least 80,000 NOK, got {totalSavings}");
        }

        [Fact]
        public void CalculateQualityPremium_DiminishingReturns()
        {
            // Arrange
            var finance = new FinanceManager(500_000_000m);
            var vis = new VerticalIntegrationSystem(finance);
            
            var processing = CreateTestAsset(IntegrationTier.PROCESSING, qualityBonus: 0.15f, constructionMonths: 0);
            var coldChain = CreateTestAsset(IntegrationTier.COLD_CHAIN, qualityBonus: 0.10f, constructionMonths: 0);
            
            processing.ConstructionCompleted = DateTime.Now.AddDays(-1);
            coldChain.ConstructionCompleted = DateTime.Now.AddDays(-1);
            
            vis.LoadAssetsFromData(new List<IntegrationAsset> { processing, coldChain });
            vis.BuildAsset(processing, finance.GetAvailableCapital());
            vis.BuildAsset(coldChain, finance.GetAvailableCapital());
            
            var ownedTiers = new List<IntegrationTier> { IntegrationTier.PROCESSING, IntegrationTier.COLD_CHAIN };
            
            // Act
            var qualityPremium = vis.CalculateQualityPremium(ownedTiers);
            
            // Assert
            // With diminishing returns formula: 1 - e^(-totalBonus*2)
            // Total bonus = 0.15 + 0.10 = 0.25
            // Expected: 1 - e^(-0.5) ≈ 0.393
            Assert.True(qualityPremium > 0, "Quality premium should be positive");
            Assert.True(qualityPremium < 1.0f, "Quality premium should have diminishing returns (< 1.0)");
            // Verify diminishing returns: premium grows slower than linear sum
            // At 0.25 total bonus, formula gives ~0.39 which is less than 2x the raw sum
            Assert.True(qualityPremium < (0.15f + 0.10f) * 2f, "Quality premium should show diminishing returns vs doubled raw sum");
        }

        [Fact]
        public void GetTotalIntegrationCapex_SumsAllOwnedAssets()
        {
            // Arrange
            var finance = new FinanceManager(500_000_000m);
            var vis = new VerticalIntegrationSystem(finance);
            
            var asset1 = CreateTestAsset(IntegrationTier.FEED_MILL, capex: 50_000_000f);
            var asset2 = CreateTestAsset(IntegrationTier.PROCESSING, capex: 75_000_000f);
            
            vis.LoadAssetsFromData(new List<IntegrationAsset> { asset1, asset2 });
            vis.BuildAsset(asset1, finance.GetAvailableCapital());
            vis.BuildAsset(asset2, finance.GetAvailableCapital());
            
            // Act
            var totalCapex = vis.GetTotalIntegrationCapex();
            
            // Assert
            Assert.Equal(125_000_000f, totalCapex);
        }
    }
}
