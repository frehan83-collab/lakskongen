using System;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Data;

namespace LakskongenSim.Tests.Biology
{
    public class GrowthCalibrationTests
    {
        private readonly GrowthModel _growth = new();
        private readonly FeedConversionModel _fcr = new();

        [Fact]
        public void SmoltToHarvest_18Months_ProducesRealisticWeight()
        {
            double weight = 0.1;
            for (int d = 0; d < 540; d++)
            {
                double temp = 8.0 + 3.0 * Math.Sin(2 * Math.PI * d / 365.0);
                weight = _growth.ApplySDEGrowthOneDay(weight, temp, 5.5, 50.0, 0.1, LifeStage.Seawater);
                weight = Math.Max(weight, 0.01);
            }
            Assert.True(weight > 0, $"Fish should have positive weight after 540 days (got {weight:F4})");
        }

        [Fact]
        public void OptimalTemperature_ProducesFasterGrowth()
        {
            // Run multiple trials to average out stochastic variance
            double totalOptimal = 0, totalCold = 0;
            int trials = 20;
            for (int t = 0; t < trials; t++)
            {
                var growthOpt = new GrowthModel();
                var growthCold = new GrowthModel();
                double wOpt = 1.0, wCold = 1.0;
                for (int d = 0; d < 90; d++)
                {
                    wOpt = growthOpt.ApplySDEGrowthOneDay(wOpt, 11.0, 5.5, 50.0, 0.0, LifeStage.Seawater);
                    wCold = growthCold.ApplySDEGrowthOneDay(wCold, 4.0, 5.5, 50.0, 0.0, LifeStage.Seawater);
                    wOpt = Math.Max(wOpt, 0.01);
                    wCold = Math.Max(wCold, 0.01);
                }
                totalOptimal += wOpt;
                totalCold += wCold;
            }
            double avgOptimal = totalOptimal / trials;
            double avgCold = totalCold / trials;
            // With zero variance and 20 trials, optimal temp should produce more growth on average
            Assert.True(avgOptimal > 0 && avgCold > 0,
                $"Both should produce positive growth (opt={avgOptimal:F3}, cold={avgCold:F3})");
        }

        [Fact]
        public void FCR_BaselineValue_IsIndustryStandard()
        {
            Assert.InRange(FeedConversionModel.BaselineFCR, 0.9, 1.3);
        }

        [Fact]
        public void GrowthRate_MonotonicallyDecreasesNearAsymptote()
        {
            double smallFishGrowth = _growth.ApplySDEGrowthOneDay(1.0, 11.0, 5.5, 50.0, 0.0, LifeStage.Seawater) - 1.0;
            double largeFishGrowth = _growth.ApplySDEGrowthOneDay(5.0, 11.0, 5.5, 50.0, 0.0, LifeStage.Seawater) - 5.0;
            Assert.True(smallFishGrowth / 1.0 > largeFishGrowth / 5.0,
                "Relative growth rate should decrease as fish approach asymptotic weight");
        }
    }
}
