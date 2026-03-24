using System;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Optimization;
using LakskongenSim.Data;

namespace LakskongenSim.Tests.Playtest
{
    public class AutomatedPlaytestSuite
    {
        [Fact]
        public void Scenario_TutorialFarm_FishGrow()
        {
            var growth = new GrowthModel();
            double weight = 0.1;
            for (int day = 0; day < 365; day++)
            {
                weight = growth.ApplySDEGrowthOneDay(weight, 10.0, 5.0, 40.0, 0.1, LifeStage.Seawater);
                weight = Math.Max(weight, 0.01);
            }
            Assert.True(weight > 0.5, $"Fish should grow past 0.5kg (got {weight:F2})");
        }

        [Fact]
        public void Scenario_FeedingOptimizer_ProducesPositiveRation()
        {
            var optimizer = new FeedingOptimizer();
            double ration = optimizer.CalculateOptimalDailyRation(1.0, 11.0, 8.0, 40.0, 70.0);
            Assert.True(ration > 0, "Optimizer should produce positive ration");
            Assert.True(ration < 0.05, "Ration should be reasonable");
        }

        [Fact]
        public void Scenario_HarvestOptimizer_FindsOptimalDate()
        {
            var optimizer = new HarvestOptimizer();
            var date = optimizer.CalculateOptimalHarvestDate(3.0, 0.02, 70.0, 40.0, 0.15);
            Assert.True(date >= DateTime.Today, "Optimal harvest should be in the future");
        }

        [Fact]
        public void Scenario_PenManagement_EnforcesDensity()
        {
            var optimizer = new PenManagementOptimizer();
            var rec = optimizer.OptimizeStockingDensity(50000, 500000, 3.0);
            Assert.True(rec.CurrentDensityKgM3 > 0, "Should calculate current density");
            Assert.Contains("CRITICAL", rec.WelfareStatus); // 500k fish * 3kg = 1.5M kg / 50k m3 = 30 kg/m3 > 25 limit
        }

        [Fact]
        public void Scenario_Treatment_RanksOptions()
        {
            var analyzer = new TreatmentCostBenefitAnalyzer();
            var options = analyzer.AnalyzeDelousingOptions(2.5, 100000, 4.0);
            Assert.True(options.Count >= 4, "Should have at least 4 delousing options");
            Assert.True(options[0].CostPerLiceRemovedNOK < options[options.Count - 1].CostPerLiceRemovedNOK,
                "Options should be sorted by cost-effectiveness");
        }
    }
}
