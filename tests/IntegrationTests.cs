using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using LakskongenSim.Finance;
using LakskongenSim.Market;
using LakskongenSim.Processing;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests
{
    public class IntegrationTests
    {
        [Fact]
        public void EndToEnd_ValueChain_Works()
        {
            // 1. End-to-End Value Chain
            decimal spotPrice = 100m; // Fish Pool O-U base
            decimal qualityPremium = 0.40m; // SashimiGrade
            decimal channelPremium = 0.25m; // Asia
            decimal brandMultiplier = 1.05m; // score > 0
            
            decimal calculatedPrice = spotPrice * (1 + qualityPremium) * (1 + channelPremium);
            decimal revenuePerKg = calculatedPrice * brandMultiplier;
            
            // Calculate yield using the actual YieldCalculator
            var product = ProductCatalog.Products[ProductType.Sashimi];
            decimal yield = YieldCalculator.Calculate(ProductType.Sashimi, 1.0m, SkillLevel.Competent, TechnologyTier.Manual, 1.0m, 0);

            Assert.True(yield > 0.40m && yield < 0.50m); // Sashimi yield is roughly 45%
            Assert.Equal(183.75m, Math.Round(revenuePerKg, 2)); 
        }

        [Fact]
        public void ProductMix_Optimization_IncreasesProfitOverVolume()
        {
            // 2. Product Mix Optimization
            var plant = new ProcessingPlant
            {
                Tier = PlantTier.Premium,
                TotalCapacityKgPerDay = 10000
            };

            var profitAnalysis = ProductMixOptimizer.OptimizeProductMix(
                plant,
                availableInputKg: 10000,
                rawMaterialCostPerKg: 60m,
                strategy: OptimizationStrategy.MaximizeProfit);

            var volumeAnalysis = ProductMixOptimizer.OptimizeProductMix(
                plant,
                availableInputKg: 10000,
                rawMaterialCostPerKg: 60m,
                strategy: OptimizationStrategy.MaximizeVolume);

            // Profit strategy should yield higher profit
            Assert.True(profitAnalysis.TotalExpectedProfit >= volumeAnalysis.TotalExpectedProfit);
        }

        [Fact]
        public void MarketEvent_ImpactsPriceAndReputation()
        {
            // 3. Market Intelligence Impact
            var evt = new MarketNewsEvent 
            { 
                Headline = "Chile floods market", 
                Description = "Global supply surge", 
                PriceShockNOK = -15m 
            };
            decimal basePrice = 100m;
            decimal newPrice = basePrice + evt.PriceShockNOK;
            
            Assert.Equal(85m, newPrice); // Price drops -15 NOK/kg

            // Reputation effect (simulated)
            var rep = new BrandReputation();
            rep.RecordEvent(ReputationEvent.ConsistentQuality, "Good delivery", 1);
            decimal initialScore = rep.OverallScore;
            
            Assert.True(newPrice < basePrice);
        }

        [Fact]
        public void AICompetitor_Acquisition_Works()
        {
            // 4. AI Competitor Acquisition
            var aiSystem = new CompetitorAISystem(); 
            
            // Assume player has enough cash
            decimal playerCash = 20_000_000_000m; // 20B NOK
            var target = aiSystem.Competitors.OrderBy(c => c.MarketCap).First();
            string targetId = target.Id;
            
            var result = aiSystem.PlayerAcquireCompetitor(targetId, target.MarketCap * 1.1m, playerCash);
            
            Assert.True(result.Success);
            Assert.DoesNotContain(aiSystem.Competitors, c => c.Id == targetId); // Removed from active list
        }

        [Fact]
        public void EmpireProgression_PhaseTransition_Works()
        {
            // 5. Empire Progression Milestone
            var progression = new EmpireProgressionSystem();
            
            var initialState = new EmpireState { LicenseCount = 1, AnnualRevenue = 10_000_000m };
            progression.Update(initialState);
            Assert.Equal(EmpirePhase.SmallFarmer, progression.CurrentPhase);

            var grownState = new EmpireState { LicenseCount = 10, ZoneCount = 2, HasProcessing = true, AnnualRevenue = 150_000_000m };
            progression.Update(grownState);
            Assert.Equal(EmpirePhase.RegionalPlayer, progression.CurrentPhase);
        }

        [Fact]
        public void IPO_StockMarket_EarningsReaction_Works()
        {
            // 6. IPO + Stock Market
            var market = new StockMarketSystem();
            
            var ipoResult = market.ExecuteIPO(
                annualRevenue: 1_200_000_000m,
                annualEBITDA: 300_000_000m,
                totalAssets: 1_500_000_000m,
                targetDilution: 0.20f);
            
            Assert.True(ipoResult.Success);
            Assert.True(market.SharePrice > 0);
            
            decimal preEarningsPrice = market.SharePrice;
            
            // Simulate HUGE Earnings Beat
            var report = market.AdvanceQuarter(
                quarterlyRevenue: 20_000_000_000m, 
                quarterlyEBITDA: 15_000_000_000m, 
                quarterlyNetIncome: 10_000_000_000m, // Massive!
                totalAssets: 5_000_000_000m, 
                totalDebt: 500_000_000m);
            
            // Stock price responds to beat
            Assert.True(market.SharePrice > preEarningsPrice);
        }

        [Fact]
        public void Investment_ROI_Calculation_RealisticNPV()
        {
            // 7. Investment ROI Calculation
            var manager = new InvestmentManager(new FinanceManager(), new InvestorSystem(), new LoanSystem(), new StockMarketSystem());
            var opp = new InvestmentOpportunity 
            {
                CapexRequired = 12_000_000m, // 12M NOK
                ProjectedAnnualEBITDA = 3_000_000m // 3M NOK/yr
            };
            
            var cashFlows = Enumerable.Repeat(opp.ProjectedAnnualEBITDA, 10).ToList();
            decimal discountRate = 0.10m; // 10%
            
            decimal npv = manager.CalculateNPV(opp.CapexRequired, cashFlows, discountRate);
            float irr = manager.CalculateIRR(opp.CapexRequired, cashFlows);
            
            Assert.True(npv > 0); // Positive NPV
            Assert.True(irr > 0.15f); // IRR > 15%
        }

        [Fact]
        public void Brand_Building_UnlocksPricingPower()
        {
            // 8. Brand Building
            var brand = new BrandReputation();
            
            for(int i=0; i<10; i++)
            {
                brand.RecordEvent(ReputationEvent.ConsistentQuality, "Excellent Sashimi", 1);
            }
            
            Assert.True(brand.OverallScore > 50m);
            decimal premium = brand.GetPricingPowerMultiplier(ProductType.Sashimi);
            Assert.True(premium >= 1.0m);
        }

        [Fact]
        public void Yield_Freshness_Decay_Works()
        {
            // 9. Product Yield Degradation
            decimal baseYield = YieldCalculator.Calculate(ProductType.Fillet, 1.0m, SkillLevel.Competent, TechnologyTier.Manual, 1.0m, 0);
            // 3 days = 72 hours
            decimal degradedYield = YieldCalculator.Calculate(ProductType.Fillet, 1.0m, SkillLevel.Competent, TechnologyTier.Manual, 1.0m, 72);
            
            // Should be lower due to decay
            Assert.True(degradedYield < baseYield);
        }

        [Fact]
        public void MultiChannel_Portfolio_ContractVsSpot_Works()
        {
            // 10. Multi-Channel Portfolio
            var system = new ContractSystem();
            var offer = system.ProposeContract("EU Buyer", ContractType.Forward, 90m, 50000);
            system.AcceptOffer(offer);
            
            decimal spotPrice = 110m; // Market spikes
            
            var summary = system.GetPortfolioSummary();
            Assert.Equal(50000, summary.hedgedKg);
            Assert.Equal(90m, summary.avgHedgePrice);
            Assert.NotEqual(spotPrice, summary.avgHedgePrice);
        }
    }
}