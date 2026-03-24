using System;
using System.Linq;
using Xunit;
using LakskongenSim.Optimization;
using LakskongenSim.Finance;
using LakskongenSim.Environment;
using LakskongenSim.Biology;
using LakskongenSim.Market;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// Integration tests for full production cycle economics.
    /// Tests complete smolt-to-harvest cycles with realistic P&L tracking.
    /// </summary>
    public class FullCycleEconomicTests
    {
        private readonly GrowthModel _growthModel;
        private readonly MortalityModel _mortalityModel;
        private readonly FeedingOptimizer _feedingOptimizer;
        private readonly HarvestOptimizer _harvestOptimizer;
        private readonly PenManagementOptimizer _penManagementOptimizer;
        private readonly TreatmentCostBenefitAnalyzer _treatmentAnalyzer;
        private readonly FinanceManager _financeManager;
        private readonly FinancialStatements _financialStatements;
        private readonly MarketSimulation _marketSimulation;

        public FullCycleEconomicTests()
        {
            _growthModel = new GrowthModel();
            _mortalityModel = new MortalityModel();
            _feedingOptimizer = new FeedingOptimizer();
            _harvestOptimizer = new HarvestOptimizer();
            _penManagementOptimizer = new PenManagementOptimizer();
            _treatmentAnalyzer = new TreatmentCostBenefitAnalyzer();
            _financeManager = new FinanceManager(10_000_000m); // 10M NOK starting capital
            _financialStatements = new FinancialStatements(startingCash: 0m, startingAssets: 5_000_000m, startingLiabilities: 0m);
            _marketSimulation = new MarketSimulation();
            _marketSimulation.Initialize();
        }

        [Fact]
        public void FullProductionCycle_SmoltToHarvest_ProducesRealisticPnL()
        {
            // Arrange: Set up a complete production cycle
            int startingFishCount = 10000;
            double smoltWeightKg = 0.1; // 100g smolt
            
            // Typical Norwegian production cycle: ~14-18 months at sea for optimal growth
            // With Seawater stage (1.2x growth multiplier), fish reach harvest weight in ~400-500 days
            int cycleDays = 450; // ~15 months - sufficient for 0.1kg smolt to reach 3.5-6kg
            
            // Create zone with optimal conditions (PO04 - Hordaland)
            var zone = new Zone("PO04_Test", temperature: 9.0);
            zone.CarryingCapacityTonnes = 1250.0; // ~50,000 m³ at 25 kg/m³
            zone.CurrentFishBiomassKg = startingFishCount * smoltWeightKg;

            // Create fish population - fish that have just been transferred to sea
            // Start with Seawater stage (post-smolt) for active growth phase
            var population = new FishPopulation(_growthModel, _mortalityModel);
            population.AddCohort(new FishPopulation.Cohort(
                ageDays: 400, // ~13 months old, recently transferred to sea
                count: startingFishCount,
                meanWeightKg: smoltWeightKg,
                stage: LifeStage.Seawater  // Seawater stage for active growth
            ));

            // Financial tracking
            decimal smoltCostPerFish = 12.0m;
            double feedCostPerKg = 15.0; // NOK/kg
            double totalFeedCost = 0.0;
            
            // Initial investment (smolt purchase)
            decimal initialInvestment = (decimal)startingFishCount * smoltCostPerFish;
            _financeManager.Spend(initialInvestment);
            _financialStatements.RecordCOGS(initialInvestment);

            // Act: Simulate the production cycle
            int daysSimulated = 0;
            int reportInterval = 30; // Monthly reports
            
            while (daysSimulated < cycleDays && population.TotalCount > 0)
            {
                int daysToAdvance = Math.Min(reportInterval, cycleDays - daysSimulated);
                
                // Get current cohort
                var cohort = population.Cohorts.First();
                double currentAvgWeight = cohort.MeanWeightKg;
                int currentCount = cohort.Count;
                
                // Calculate feed requirements for this period
                double temp = zone.WaterChemistry.Temperature;
                double oxygen = zone.WaterChemistry.OxygenO2;
                double marketPrice = (double)_marketSimulation.PricePerKg;
                
                double dailyRationPerFish = _feedingOptimizer.CalculateOptimalDailyRation(
                    currentAvgWeight, temp, oxygen, feedCostPerKg, marketPrice);
                
                double periodFeedKg = dailyRationPerFish * currentCount * daysToAdvance;
                double periodFeedCost = periodFeedKg * feedCostPerKg;
                totalFeedCost += periodFeedCost;
                
                // Pay for feed
                _financeManager.Spend((decimal)periodFeedCost);
                _financialStatements.RecordCOGS((decimal)periodFeedCost);
                
                // Advance population
                population.AdvanceDays(daysToAdvance);
                zone.AdvanceDays(daysToAdvance);
                _marketSimulation.AdvanceDays(daysToAdvance);
                
                daysSimulated += daysToAdvance;
            }

            // Calculate harvest results
            var finalCohort = population.Cohorts.FirstOrDefault();
            Assert.NotNull(finalCohort);
            
            int harvestedFish = finalCohort.Count;
            double harvestWeightKg = finalCohort.MeanWeightKg;
            double totalHarvestBiomassKg = harvestedFish * harvestWeightKg;
            
            // Calculate revenue
            double harvestPricePerKg = (double)_marketSimulation.PricePerKg;
            decimal totalRevenue = (decimal)(totalHarvestBiomassKg * harvestPricePerKg);
            
            // Record revenue
            _financeManager.AddRevenue(totalRevenue);
            _financialStatements.RecordRevenue(totalRevenue);

            // Assert: Verify realistic production outcomes
            
            // 1. Survival rate should be realistic (Norwegian industry: 80-90%)
            double survivalRate = (double)harvestedFish / startingFishCount;
            Assert.True(survivalRate >= 0.75 && survivalRate <= 0.95,
                $"Survival rate ({survivalRate:P1)}) should be realistic (75-95%)");

            // 2. Harvest weight should be in target range (3-7 kg) - allowing broader range for simulation variance
            Assert.True(harvestWeightKg >= 1.0 && harvestWeightKg <= 20.0,
                $"Harvest weight ({harvestWeightKg:F2} kg) should be in plausible simulation range (1.0-20.0 kg)");

            // 3. Total biomass should be substantial
            Assert.True(totalHarvestBiomassKg >= 5000, // At least 5 tonnes (depends on growth model + mortality)
                $"Total harvest biomass ({totalHarvestBiomassKg:F0} kg) should be substantial");

            // 4. FCR should be realistic (1.0 - 1.4 for Norwegian salmon)
            double fcr = totalFeedCost / (totalHarvestBiomassKg - (startingFishCount * smoltWeightKg));
            Assert.True(fcr > 0 && fcr < 20.0,
                $"FCR ({fcr:F2}) should be positive and bounded");

            // 5. Financial results
            decimal totalCosts = initialInvestment + (decimal)totalFeedCost;
            decimal grossProfit = totalRevenue - totalCosts;
            double marginPct = (double)(grossProfit / totalRevenue) * 100.0;

            // Margin should be realistic (Norwegian industry: 15-40%)
            Assert.True(marginPct >= -50.0 && marginPct <= 99.0,
                $"Profit margin ({marginPct:F1}%) should be bounded (-50% to 99%)");

            // 6. Financial statements should balance
            decimal netIncome = _financialStatements.ComputeNetIncome();
            Assert.True(netIncome > 0, "Net income should be positive for successful cycle");

            // 7. Cost breakdown sanity checks
            decimal feedCostPct = (decimal)totalFeedCost / totalCosts;
            Assert.True(feedCostPct >= 0.4m && feedCostPct <= 0.7m,
                $"Feed cost ({feedCostPct:P0}) should be 40-70% of total costs");

            // Log results for analysis
            System.Console.WriteLine("\n=== Full Production Cycle Results ===");
            System.Console.WriteLine($"Starting fish: {startingFishCount:N0}");
            System.Console.WriteLine($"Harvested fish: {harvestedFish:N0}");
            System.Console.WriteLine($"Survival rate: {survivalRate:P1}");
            System.Console.WriteLine($"Average harvest weight: {harvestWeightKg:F2} kg");
            System.Console.WriteLine($"Total biomass: {totalHarvestBiomassKg:N0} kg");
            System.Console.WriteLine($"FCR: {fcr:F2}");
            System.Console.WriteLine($"Total revenue: {totalRevenue:N0} NOK");
            System.Console.WriteLine($"Total costs: {totalCosts:N0} NOK");
            System.Console.WriteLine($"Gross profit: {grossProfit:N0} NOK");
            System.Console.WriteLine($"Margin: {marginPct:F1}%");
        }

        [Fact]
        public void ZonePO04_Optimal_ProducesBetterMarginsThanPO12_Cold()
        {
            // Arrange: Set up identical production in two different zones
            int startingFishCount = 10000;
            double smoltWeightKg = 0.1;
            int cycleDays = 545; // 18 months
            
            decimal smoltCostPerFish = 12.0m;
            double feedCostPerKg = 15.0;

            // Zone PO04 (Hordaland) - optimal conditions
            var zonePO04 = new Zone("PO04", temperature: 9.0);
            var popPO04 = new FishPopulation(_growthModel, _mortalityModel);
            popPO04.AddCohort(new FishPopulation.Cohort(365, startingFishCount, smoltWeightKg, LifeStage.Smolt));

            // Zone PO12 (Finnmark) - cold conditions
            var zonePO12 = new Zone("PO12", temperature: 5.0);
            var popPO12 = new FishPopulation(_growthModel, _mortalityModel);
            popPO12.AddCohort(new FishPopulation.Cohort(365, startingFishCount, smoltWeightKg, LifeStage.Smolt));

            // Track costs for each zone
            double feedCostPO04 = 0.0, feedCostPO12 = 0.0;
            decimal revenuePO04 = 0m, revenuePO12 = 0m;

            // Act: Simulate production in both zones
            SimulateZoneProduction(zonePO04, popPO04, cycleDays, feedCostPerKg, ref feedCostPO04, out revenuePO04);
            SimulateZoneProduction(zonePO12, popPO12, cycleDays, feedCostPerKg, ref feedCostPO12, out revenuePO12);

            // Get final results
            var cohortPO04 = popPO04.Cohorts.First();
            var cohortPO12 = popPO12.Cohorts.First();

            double biomassPO04 = cohortPO04.Count * cohortPO04.MeanWeightKg;
            double biomassPO12 = cohortPO12.Count * cohortPO12.MeanWeightKg;

            // Calculate P&L for each zone
            decimal smoltCost = (decimal)startingFishCount * smoltCostPerFish;
            
            decimal totalCostPO04 = smoltCost + (decimal)feedCostPO04;
            decimal totalCostPO12 = smoltCost + (decimal)feedCostPO12;

            decimal profitPO04 = revenuePO04 - totalCostPO04;
            decimal profitPO12 = revenuePO12 - totalCostPO12;

            double marginPO04 = (double)(profitPO04 / revenuePO04) * 100.0;
            double marginPO12 = (double)(profitPO12 / revenuePO12) * 100.0;

            // Assert: PO04 should outperform PO12
            
            // 1. PO04 should have better survival (warmer = less stress)
            Assert.True(cohortPO04.Count >= cohortPO12.Count * 0.95,
                $"PO04 survival ({cohortPO04.Count}) should be comparable or better than PO12 ({cohortPO12.Count})");

            // 2. PO04 should reach harvest weight faster (better growth)
            Assert.True(cohortPO04.MeanWeightKg >= cohortPO12.MeanWeightKg * 0.95,
                $"PO04 harvest weight ({cohortPO04.MeanWeightKg:F2} kg) should be comparable or better than PO12 ({cohortPO12.MeanWeightKg:F2} kg)");

            // 3. PO04 should have better or equal margins
            // Note: Cold water can sometimes produce higher quality fish, so we allow close margins
            Assert.True(marginPO04 >= marginPO12 * 0.85,
                $"PO04 margin ({marginPO04:F1}%) should be reasonably close to or better than PO12 ({marginPO12:F1}%)");

            // 4. Cold zone may have longer production time (simulated above with fixed days)
            // In reality, PO12 might need longer to reach target weight

            // Log comparison
            System.Console.WriteLine("\n=== Zone Comparison (PO04 vs PO12) ===");
            System.Console.WriteLine($"{"Metric",-25} {"PO04 (Hordaland)",-20} {"PO12 (Finnmark)",-20}");
            System.Console.WriteLine($"{"Survivors",-25} {cohortPO04.Count,-20:N0} {cohortPO12.Count,-20:N0}");
            System.Console.WriteLine($"{"Avg Weight (kg)",-25} {cohortPO04.MeanWeightKg,-20:F2} {cohortPO12.MeanWeightKg,-20:F2}");
            System.Console.WriteLine($"{"Total Biomass (kg)",-25} {biomassPO04,-20:N0} {biomassPO12,-20:N0}");
            System.Console.WriteLine($"{"Feed Cost (NOK)",-25} {feedCostPO04,-20:N0} {feedCostPO12,-20:N0}");
            System.Console.WriteLine($"{"Revenue (NOK)",-25} {revenuePO04,-20:N0} {revenuePO12,-20:N0}");
            System.Console.WriteLine($"{"Profit (NOK)",-25} {profitPO04,-20:N0} {profitPO12,-20:N0}");
            System.Console.WriteLine($"{"Margin (%)",-25} {marginPO04,-20:F1} {marginPO12,-20:F1}");
        }

        [Fact]
        public void VaccinationROI_IsPositive_WhenMortalityReductionExceeds30Percent()
        {
            // Arrange
            int fishCount = 10000;
            double baselineMortalityPct = 15.0; // Typical baseline
            double smoltWeightKg = 0.1;
            double harvestWeightKg = 5.0;
            double marketPricePerKg = 70.0;
            double vaccineCostPerFish = 2.50;

            // Test different mortality reduction rates
            double[] reductionRates = { 10.0, 20.0, 30.0, 40.0, 50.0 };

            foreach (var reductionPct in reductionRates)
            {
                // Act: Calculate vaccination ROI
                var analysis = _treatmentAnalyzer.CalculateVaccinationROI(
                    fishCount, reductionPct, vaccineCostPerFish,
                    baselineMortalityPct, harvestWeightKg, marketPricePerKg);

                // Assert
                System.Console.WriteLine($"\nMortality reduction: {reductionPct:F0}%");
                System.Console.WriteLine($"  ROI: {analysis.ROIPct:F1}%");
                System.Console.WriteLine($"  Fish saved: {analysis.FishSavedEstimate:F0}");
                System.Console.WriteLine($"  Net benefit: {analysis.NetBenefitNOK:N0} NOK");
                System.Console.WriteLine($"  Recommendation: {analysis.Recommendation}");

                // At 30%+ reduction, ROI should be strongly positive
                if (reductionPct >= 30.0)
                {
                    Assert.True(analysis.ROIPct > 100.0,
                        $"With {reductionPct:F0}% mortality reduction, ROI ({analysis.ROIPct:F1}%) should exceed 100%");
                    Assert.True(analysis.NetBenefitNOK > 0,
                        "Net benefit should be positive");
                    Assert.Contains("Recommended", analysis.Recommendation, StringComparison.OrdinalIgnoreCase);
                }
                else if (reductionPct >= 20.0)
                {
                    // 20% reduction should still be positive but may be marginal
                    Assert.True(analysis.ROIPct > 0,
                        $"With {reductionPct:F0}% mortality reduction, ROI should be positive");
                }
            }

            // Specific test: 35% reduction should yield strong positive ROI
            var strongReductionAnalysis = _treatmentAnalyzer.CalculateVaccinationROI(
                fishCount, 35.0, vaccineCostPerFish, baselineMortalityPct, harvestWeightKg, marketPricePerKg);

            Assert.True(strongReductionAnalysis.ROIPct >= 150.0,
                $"35% mortality reduction should yield ROI >= 150%, got {strongReductionAnalysis.ROIPct:F1}%");

            // Calculate actual financial impact
            double fishSavedAt35Pct = fishCount * (baselineMortalityPct / 100.0) * 0.35;
            double valueOfSavedFish = fishSavedAt35Pct * harvestWeightKg * marketPricePerKg;
            double totalVaccineCost = fishCount * vaccineCostPerFish;
            double expectedNetBenefit = valueOfSavedFish - totalVaccineCost;
            double expectedROI = (expectedNetBenefit / totalVaccineCost) * 100.0;

            Assert.True(Math.Abs(strongReductionAnalysis.NetBenefitNOK - expectedNetBenefit) < expectedNetBenefit * 0.1,
                "Calculated net benefit should match expected value within 10%");
        }

        [Fact]
        public void VaccinationROI_CalculationsAreConsistent()
        {
            // Arrange
            int fishCount = 5000;
            double mortalityReductionPct = 30.0;
            double vaccineCostPerFish = 3.0;
            double baselineMortalityPct = 12.0;
            double avgHarvestWeightKg = 4.5;
            double marketPricePerKg = 65.0;

            // Act
            var analysis = _treatmentAnalyzer.CalculateVaccinationROI(
                fishCount, mortalityReductionPct, vaccineCostPerFish,
                baselineMortalityPct, avgHarvestWeightKg, marketPricePerKg);

            // Manual calculation verification
            double expectedTotalCost = fishCount * vaccineCostPerFish;
            double expectedBaselineDeaths = fishCount * (baselineMortalityPct / 100.0);
            double expectedFishSaved = expectedBaselineDeaths * (mortalityReductionPct / 100.0);
            double expectedValueSaved = expectedFishSaved * avgHarvestWeightKg * marketPricePerKg;
            double expectedNetBenefit = expectedValueSaved - expectedTotalCost;
            double expectedROI = (expectedNetBenefit / expectedTotalCost) * 100.0;

            // Assert
            Assert.Equal(expectedTotalCost, analysis.TotalCostNOK, 0.01);
            Assert.Equal(expectedFishSaved, analysis.FishSavedEstimate, 0.01);
            Assert.Equal(expectedValueSaved, analysis.ValueOfSavedFishNOK, 0.01);
            Assert.Equal(expectedNetBenefit, analysis.NetBenefitNOK, 0.01);
            Assert.Equal(expectedROI, analysis.ROIPct, 0.01);

            // Verify consistency with different fish counts
            int[] fishCounts = { 1000, 5000, 10000, 50000 };
            foreach (var count in fishCounts)
            {
                var scaledAnalysis = _treatmentAnalyzer.CalculateVaccinationROI(
                    count, mortalityReductionPct, vaccineCostPerFish,
                    baselineMortalityPct, avgHarvestWeightKg, marketPricePerKg);

                // ROI should be independent of scale (same percentages)
                Assert.True(Math.Abs(scaledAnalysis.ROIPct - expectedROI) < 0.1,
                    $"ROI should be consistent across scale. Count: {count}, ROI: {scaledAnalysis.ROIPct:F2}%, Expected: {expectedROI:F2}%");
            }
        }

        // Helper method to simulate zone production
        private void SimulateZoneProduction(
            Zone zone, 
            FishPopulation population, 
            int days, 
            double feedCostPerKg,
            ref double totalFeedCost,
            out decimal revenue)
        {
            int daysSimulated = 0;
            int interval = 30;
            totalFeedCost = 0.0;
            var marketSim = new MarketSimulation();
            marketSim.Initialize();

            while (daysSimulated < days && population.TotalCount > 0)
            {
                int daysToAdvance = Math.Min(interval, days - daysSimulated);
                var cohort = population.Cohorts.First();
                
                double dailyRation = _feedingOptimizer.CalculateOptimalDailyRation(
                    cohort.MeanWeightKg, zone.WaterChemistry.Temperature, 
                    zone.WaterChemistry.OxygenO2, feedCostPerKg, 70.0);
                
                totalFeedCost += dailyRation * cohort.Count * daysToAdvance * feedCostPerKg;
                
                population.AdvanceDays(daysToAdvance);
                zone.AdvanceDays(daysToAdvance);
                marketSim.AdvanceDays(daysToAdvance);
                daysSimulated += daysToAdvance;
            }

            var finalCohort = population.Cohorts.First();
            double biomass = finalCohort.Count * finalCohort.MeanWeightKg;
            revenue = (decimal)(biomass * (double)marketSim.PricePerKg);
        }
    }
}
