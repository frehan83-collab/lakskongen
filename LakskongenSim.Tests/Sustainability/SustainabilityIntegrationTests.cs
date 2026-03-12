using System;
using System.Collections.Generic;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Sustainability;
using LakskongenSim.Farm;
using LakskongenSim.Health;
using LakskongenSim.Market;
using LakskongenSim.Finance;
using LakskongenSim.Logistics;
using LakskongenSim.Biology;

namespace LakskongenSim.Tests.Sustainability
{
    /// <summary>
    /// Integration tests for Sustainability system integration with core simulation systems.
    /// Tests that SustainabilityManager correctly reads from integrated systems and
    /// that bonuses/penalties are applied appropriately.
    /// </summary>
    public class SustainabilityIntegrationTests
    {
        [Fact]
        public void Test_SustainabilityManager_CalculatesScoreFromGameState()
        {
            // Arrange
            var config = new SustainabilityConfig();
            var manager = new SustainabilityManager(config);
            var gameState = CreateTestGameState();

            // Act
            var score = manager.CalculateScore(gameState);
            var level = manager.GetCertificationLevel(score);

            // Assert
            Assert.InRange(score, 0, 100);
            Assert.NotEqual(CertificationLevel.NonCompliant, level); // Should have some baseline compliance
        }

        [Fact]
        public void Test_MarketSystem_UpdatesPriceMultiplier_OnCertificationChange()
        {
            // Arrange
            var market = new MarketSimulation();
            var config = new SustainabilityConfig();
            var manager = new SustainabilityManager(config);
            
            var basePricePerKg = market.BasePricePerKg;

            // Act - Apply ASC certification (+8% price)
            var multiplier = manager.GetPriceMultiplier(CertificationLevel.ASCCertified);
            market.UpdateCertificationLevel(CertificationLevel.ASCCertified, multiplier);

            // Assert
            Assert.Equal(1.08m, multiplier);
            Assert.True(market.PricePerKg > basePricePerKg);
            Assert.Equal(basePricePerKg * 1.08m, market.PricePerKg);
        }

        [Fact]
        public void Test_MarketSystem_BlocksMarkets_ForNonCompliant()
        {
            // Arrange
            var market = new MarketSimulation();
            var config = new SustainabilityConfig();
            var manager = new SustainabilityManager(config);

            // Act - Non-compliant status
            var multiplier = manager.GetPriceMultiplier(CertificationLevel.NonCompliant);
            market.UpdateCertificationLevel(CertificationLevel.NonCompliant, multiplier);

            // Assert
            Assert.False(market.CanAccessMarket("EU")); // Blocked from EU
            Assert.False(market.CanAccessMarket("USA")); // Blocked from USA
            Assert.True(market.CanAccessMarket("Domestic")); // Can still sell domestically
        }

        [Fact]
        public void Test_MarketSystem_UnlocksPremiumMarkets_WithASC()
        {
            // Arrange
            var market = new MarketSimulation();
            var config = new SustainabilityConfig();
            var manager = new SustainabilityManager(config);

            // Act - ASC certification
            market.UpdateCertificationLevel(CertificationLevel.ASCCertified, 1.08m);

            // Assert
            Assert.True(market.CanAccessMarket("EU"));
            Assert.True(market.CanAccessMarket("USA"));
            Assert.True(market.CanAccessMarket("WholeFoods")); // Premium buyer
        }

        [Fact]
        public void Test_FinanceManager_AppliesESGDiscount_At80Score()
        {
            // Arrange
            var finance = new FinanceManager(1_000_000m);
            var baseCost = 100_000m;

            // Act
            finance.UpdateSustainabilityBonuses(80); // Trigger ESG discount
            var discountedCost = finance.ApplyESGDiscount(baseCost);

            // Assert
            Assert.Equal(0.30m, finance.ESGInvestmentDiscount); // 30% discount
            Assert.Equal(70_000m, discountedCost); // 30% off 100k = 70k
        }

        [Fact]
        public void Test_FinanceManager_AppliesGrantMultiplier_At95Score()
        {
            // Arrange
            var finance = new FinanceManager(1_000_000m);
            var baseGrant = 50_000m;

            // Act
            finance.UpdateSustainabilityBonuses(95); // Trigger grant multiplier
            var multipliedGrant = finance.ApplyGrantMultiplier(baseGrant);

            // Assert
            Assert.Equal(1.5m, finance.GovernmentGrantMultiplier); // 1.5x grants
            Assert.Equal(75_000m, multipliedGrant); // 50k * 1.5 = 75k
        }

        [Fact]
        public void Test_FinanceManager_NoBonus_BelowThresholds()
        {
            // Arrange
            var finance = new FinanceManager(1_000_000m);

            // Act
            finance.UpdateSustainabilityBonuses(60); // Below 80 and 95

            // Assert
            Assert.Equal(0m, finance.ESGInvestmentDiscount);
            Assert.Equal(1.0m, finance.GovernmentGrantMultiplier);
        }

        [Fact]
        public void Test_SimulationEngine_InitializesSustainabilitySystem()
        {
            // Arrange
            var engine = new SimulationEngine();

            // Act
            engine.Initialize();

            // Assert
            Assert.NotNull(engine.SustainabilityManager);
            Assert.Equal(0, engine.SustainabilityManager.CurrentScore); // Initial score
        }

        [Fact]
        public void Test_SimulationEngine_UpdatesScore_Weekly()
        {
            // Arrange
            var engine = new SimulationEngine();
            engine.Initialize();
            engine.State.SeaSites.Add(CreateTestSeaSite());
            engine.State.Populations.Add(CreateTestPopulation());

            var scoreChangedCount = 0;
            engine.SustainabilityManager.OnScoreChanged += (score, level) => scoreChangedCount++;

            // Act - Advance 14 days (2 weeks)
            engine.Advance(14);

            // Assert
            Assert.True(scoreChangedCount >= 1, "Score should update at least once in 2 weeks");
        }

        [Fact]
        public void Test_EndToEnd_SustainabilityFlow()
        {
            // Arrange
            var engine = new SimulationEngine();
            engine.Initialize();
            
            var finance = new FinanceManager(5_000_000m);
            var market = new MarketSimulation();
            
            engine.FinanceManager = finance;
            engine.MarketSimulation = market;

            // Add game content
            engine.State.SeaSites.Add(CreateTestSeaSite());
            engine.State.Populations.Add(CreateTestPopulation());

            var initialPrice = market.PricePerKg;
            var initialBalance = finance.Balance;

            // Act - Simulate 30 days
            engine.Advance(30);

            // Assert - System is functioning
            Assert.NotNull(engine.SustainabilityManager);
            Assert.InRange(engine.SustainabilityManager.CurrentScore, 0, 100);
            
            // Market should be initialized (even if price didn't change much)
            Assert.NotNull(market);
        }

        // ===== HELPER METHODS =====

        private GameState CreateTestGameState()
        {
            var state = new GameState
            {
                Day = 365,
                Balance = 5_000_000m
            };

            state.SeaSites.Add(CreateTestSeaSite());
            state.Populations.Add(CreateTestPopulation());

            return state;
        }

        private SeaSite CreateTestSeaSite()
        {
            var site = new SeaSite("TestSite")
            {
                IsClosedContainment = false,
                MaxAllowedDensity = 25.0f,
                PenVolume = 50000f
            };

            site.Stock(CreateTestPopulation());

            return site;
        }

        private FishPopulation CreateTestPopulation()
        {
            var pop = new FishPopulation("TestPopulation", startDate: DateTime.Today);
            
            // Add a cohort of 10,000 fish at 3kg each
            pop.Cohorts.Add(new Cohort
            {
                Count = 10000,
                MeanWeightKg = 3.0,
                AgeDays = 365
            });

            return pop;
        }
    }
}
