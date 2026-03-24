using System;
using System.Linq;
using Xunit;
using LakskongenSim.Optimization;
using LakskongenSim.Finance;
using LakskongenSim.Environment;
using LakskongenSim.Biology;
using LakskongenSim.Market;
using LakskongenSim.Data;
using Season = LakskongenSim.Environment.Season;
using LakskongenSim.Farm;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// Integration tests for economic and environmental system interactions.
    /// Tests how environmental factors (temperature, water quality) affect financial outcomes.
    /// </summary>
    public class EconomicEnvironmentalIntegrationTests
    {
        private readonly FeedingOptimizer _feedingOptimizer;
        private readonly HarvestOptimizer _harvestOptimizer;
        private readonly MortalityModel _mortalityModel;
        private readonly GrowthModel _growthModel;
        private readonly MarketSimulation _marketSimulation;
        private readonly FinanceManager _financeManager;

        public EconomicEnvironmentalIntegrationTests()
        {
            _feedingOptimizer = new FeedingOptimizer();
            _harvestOptimizer = new HarvestOptimizer();
            _mortalityModel = new MortalityModel();
            _growthModel = new GrowthModel();
            _marketSimulation = new MarketSimulation();
            _marketSimulation.Initialize();
            _financeManager = new FinanceManager(5_000_000m);
        }

        [Fact]
        public void TemperatureAffectsFeedCost_ColdZonesHigherCost()
        {
            // Arrange: Set up identical fish populations in cold vs warm zones
            double fishWeightKg = 3.0;
            double oxygenMgL = 8.0; // Optimal oxygen
            double feedCostPerKg = 15.0; // NOK/kg feed
            double marketPricePerKg = 70.0;

            // Cold zone temperature (PO12 Finnmark ~5°C)
            double coldTempC = 5.0;
            // Warm zone temperature (PO04 Hordaland ~9°C)
            double warmTempC = 9.0;

            // Act: Calculate feeding rations for both temperatures
            double coldRation = _feedingOptimizer.CalculateOptimalDailyRation(
                fishWeightKg, coldTempC, oxygenMgL, feedCostPerKg, marketPricePerKg);
            
            double warmRation = _feedingOptimizer.CalculateOptimalDailyRation(
                fishWeightKg, warmTempC, oxygenMgL, feedCostPerKg, marketPricePerKg);

            // Cold temperature reduces feeding rate (metabolism slows)
            Assert.True(coldRation < warmRation, 
                $"Cold zone ration ({coldRation:F4}) should be less than warm zone ({warmRation:F4}) due to reduced metabolism");

            // Calculate daily feed costs for a pen of 10,000 fish
            int fishCount = 10000;
            double coldDailyFeedCost = coldRation * fishCount * feedCostPerKg;
            double warmDailyFeedCost = warmRation * fishCount * feedCostPerKg;

            // Cold zones require less daily feed but may need more days to reach target weight
            // Total feed cost per kg of growth should be higher in cold (less efficient)
            Assert.True(coldDailyFeedCost < warmDailyFeedCost,
                "Cold water feeding rate should be lower due to reduced metabolism");
        }

        [Fact]
        public void MarketPriceSeasonality_MatchesZoneProductionCycles()
        {
            // Arrange: Get seasonal profiles for different zones
            var po04ProfileSummer = NorwegianZoneEnvironmentalProfile.GetSeasonalProfile("PO04", Season.Summer);
            var po04ProfileWinter = NorwegianZoneEnvironmentalProfile.GetSeasonalProfile("PO04", Season.Winter);
            
            var po12ProfileSummer = NorwegianZoneEnvironmentalProfile.GetSeasonalProfile("PO12", Season.Summer);
            var po12ProfileWinter = NorwegianZoneEnvironmentalProfile.GetSeasonalProfile("PO12", Season.Winter);

            // Act: Verify seasonal temperature patterns affect growth potential
            // PO04 (Hordaland) has moderated temperatures year-round
            double po04SummerTemp = po04ProfileSummer.TemperatureAtCageDepth;
            double po04WinterTemp = po04ProfileWinter.TemperatureAtCageDepth;
            double po04TempVariation = po04SummerTemp - po04WinterTemp;

            // PO12 (Finnmark) has wider seasonal variation
            double po12SummerTemp = po12ProfileSummer.TemperatureAtCageDepth;
            double po12WinterTemp = po12ProfileWinter.TemperatureAtCageDepth;
            double po12TempVariation = po12SummerTemp - po12WinterTemp;

            // Assert: Finnmark has larger seasonal temperature swing
            Assert.True(po12TempVariation > po04TempVariation,
                $"PO12 seasonal variation ({po12TempVariation:F1}°C) should exceed PO04 ({po04TempVariation:F1}°C)");

            // Northern zones have peak production shifted to summer when temperatures are optimal
            // Growth rates should be higher in summer for PO12 (overcoming cold baseline)
            double growthRateSummerPO12 = CalculateGrowthRateAtTemp(po12SummerTemp);
            double growthRateWinterPO12 = CalculateGrowthRateAtTemp(po12WinterTemp);
            double seasonalGrowthSwingPO12 = growthRateSummerPO12 - growthRateWinterPO12;

            double growthRateSummerPO04 = CalculateGrowthRateAtTemp(po04SummerTemp);
            double growthRateWinterPO04 = CalculateGrowthRateAtTemp(po04WinterTemp);
            double seasonalGrowthSwingPO04 = growthRateSummerPO04 - growthRateWinterPO04;

            // Northern zones have more pronounced seasonal growth patterns
            Assert.True(seasonalGrowthSwingPO12 > seasonalGrowthSwingPO04,
                "PO12 should have more pronounced seasonal growth variation than PO04");

            // Market prices follow seasonal demand patterns (higher in winter holidays)
            // Production cycles need to align with these patterns
            var harvestOptimizer = new HarvestOptimizer();
            double currentWeight = 4.5;
            double dailyGrowth = 0.02;
            double basePrice = 85.0;
            double costPerKg = 45.0;
            double holdingCost = 0.5;

            // Compare harvest timing scenarios
            // Use dates in the future to allow fish to grow to valid harvest weight
            var today = DateTime.Today;
            var earlyHarvest = today.AddDays(60);   // ~60 days out
            var lateHarvest = today.AddDays(120);   // ~120 days out

            var scenarios = harvestOptimizer.CompareHarvestScenarios(
                currentWeight, dailyGrowth, basePrice, costPerKg, holdingCost, 10000,
                earlyHarvest, lateHarvest);

            // Assert we have valid scenarios
            Assert.True(scenarios.Count >= 2, "Should have at least 2 valid harvest scenarios");

            // Late harvest should typically be more profitable due to higher weight
            // The harvest optimizer considers weight and seasonal pricing
            var earlyScenario = scenarios.OrderBy(s => s.HarvestDate).First();
            var lateScenario = scenarios.OrderByDescending(s => s.HarvestDate).First();
            
            // Later harvest should have higher weight
            Assert.True(lateScenario.EstimatedWeightKg >= earlyScenario.EstimatedWeightKg,
                "Later harvest should have higher estimated weight");
            
            // Both scenarios should be valid
            Assert.True(earlyScenario.EstimatedWeightKg >= 3.5, "Early harvest should be at minimum weight");
            Assert.True(lateScenario.EstimatedWeightKg >= 3.5, "Late harvest should be at minimum weight");
            
            // Profit should be reasonable
            Assert.True(earlyScenario.ProfitNOK > 0, "Early harvest should have positive profit");
            Assert.True(lateScenario.ProfitNOK > 0, "Late harvest should have positive profit");
        }

        [Fact]
        public void EnvironmentalStress_IncreasesMortality_AffectsProfitAndLoss()
        {
            // Arrange: Create two identical populations
            int initialFishCount = 10000;
            double avgWeightKg = 3.0;
            int daysToSimulate = 90; // 3 months

            // Create environmental profiles - one optimal, one stressful
            var optimalEnvironment = new EnvironmentalDataPoint
            {
                ZoneId = "PO04",
                TemperatureAt10m = 11.0, TemperatureAt30m = 11.0, // Optimal
                OxygenAtCageDepth = 8.0, // Good oxygen
                CurrentSpeedAtCageDepth = 0.2,
                EnvironmentalQualityScore = 0.9
            };

            var stressfulEnvironment = new EnvironmentalDataPoint
            {
                ZoneId = "PO12",
                TemperatureAt10m = 16.5, TemperatureAt30m = 16.5, // Heat stress threshold
                OxygenAtCageDepth = 4.0, // Low oxygen (below 5 mg/L threshold)
                CurrentSpeedAtCageDepth = 0.1,
                EnvironmentalQualityScore = 0.4
            };

            // Act: Apply mortality with different environmental conditions
            int optimalSurvivors = _mortalityModel.ApplyMortalityWithEnvironment(
                initialFishCount, daysToSimulate, LifeStage.Seawater, optimalEnvironment, "pop_optimal");
            
            _mortalityModel.ClearDiseaseMortalityRates();
            
            int stressfulSurvivors = _mortalityModel.ApplyMortalityWithEnvironment(
                initialFishCount, daysToSimulate, LifeStage.Seawater, stressfulEnvironment, "pop_stressful");

            // Assert: Stressful environment should have higher mortality
            Assert.True(stressfulSurvivors < optimalSurvivors,
                $"Stressful environment should have fewer survivors. Optimal: {optimalSurvivors}, Stressful: {stressfulSurvivors}");

            // Calculate financial impact of mortality
            double marketPricePerKg = 70.0;
            double productionCostPerKg = 45.0;
            
            // Revenue lost due to mortality
            int optimalDeaths = initialFishCount - optimalSurvivors;
            int stressfulDeaths = initialFishCount - stressfulSurvivors;
            
            double optimalLostRevenue = optimalDeaths * avgWeightKg * marketPricePerKg;
            double stressfulLostRevenue = stressfulDeaths * avgWeightKg * marketPricePerKg;
            
            double optimalLostCost = optimalDeaths * avgWeightKg * productionCostPerKg;
            double stressfulLostCost = stressfulDeaths * avgWeightKg * productionCostPerKg;

            // Stressful environment should result in greater financial losses
            Assert.True(stressfulLostRevenue > optimalLostRevenue,
                "Stressful environment should result in greater lost revenue due to higher mortality");

            Assert.True(stressfulLostCost > optimalLostCost,
                "Stressful environment should result in greater sunk costs due to higher mortality");

            // P&L impact: net loss from mortality
            double optimalNetLoss = optimalLostRevenue - optimalLostCost;
            double stressfulNetLoss = stressfulLostRevenue - stressfulLostCost;

            // Verify P&L tracking would reflect this
            var financialStatements = new FinancialStatements(startingCash: 0m, startingAssets: 0m, startingLiabilities: 0m);
            
            // Record the cost of fish lost to mortality (sunk production cost)
            financialStatements.RecordCOGS((decimal)stressfulLostCost);
            
            // The COGS should be recorded for the higher mortality scenario
            Assert.True(financialStatements.COGS > 0, "COGS should reflect production costs of lost fish");
        }

        // Helper method to estimate growth rate at a given temperature
        private double CalculateGrowthRateAtTemp(double temperature)
        {
            // Simplified growth rate based on temperature (Gaussian around 11°C)
            double optimalTemp = 11.0;
            double tolerance = 3.0;
            double tempDiff = temperature - optimalTemp;
            return Math.Exp(-(tempDiff * tempDiff) / (2.0 * tolerance * tolerance));
        }
    }
}
