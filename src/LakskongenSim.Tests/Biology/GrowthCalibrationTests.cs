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
            Assert.InRange(weight, 0.5, 15.0);
        }

        [Fact]
        public void OptimalTemperature_ProducesFasterGrowth()
        {
            double weightOptimal = 1.0;
            double weightCold = 1.0;
            for (int d = 0; d < 90; d++)
            {
                weightOptimal = _growth.ApplySDEGrowthOneDay(weightOptimal, 11.0, 5.5, 50.0, 0.05, LifeStage.Seawater);
                weightCold = _growth.ApplySDEGrowthOneDay(weightCold, 4.0, 5.5, 50.0, 0.05, LifeStage.Seawater);
                weightOptimal = Math.Max(weightOptimal, 0.01);
                weightCold = Math.Max(weightCold, 0.01);
            }
            Assert.True(weightOptimal > weightCold,
                $"Optimal temp (11C) weight {weightOptimal:F3} should exceed cold (4C) weight {weightCold:F3}");
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
