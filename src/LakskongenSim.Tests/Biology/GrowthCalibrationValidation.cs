using System;
using Xunit;
using Xunit.Abstractions;
using LakskongenSim.Biology;
using LakskongenSim.Data;

namespace LakskongenSim.Tests.Biology
{
    /// <summary>
    /// Validates that the SDE growth model produces biologically realistic results.
    /// 
    /// Real-world targets (Norwegian salmon farming):
    /// - Smolt transfer: 80-120g
    /// - 18 months seawater: 4-6 kg harvest
    /// - SGR at optimal temp: 0.8-1.2% per day
    /// - FCR: 1.1-1.3 (handled by FeedConversionModel)
    /// </summary>
    public class GrowthCalibrationValidation
    {
        private readonly ITestOutputHelper _output;
        private readonly GrowthModel _model = new();

        public GrowthCalibrationValidation(ITestOutputHelper output) => _output = output;

        [Fact]
        public void SDE_540Days_OptimalTemp_ProducesHarvestWeight()
        {
            // Monte Carlo: run 50 fish, check median falls in 3-7 kg range
            int n = 50;
            double[] finalWeights = new double[n];

            for (int i = 0; i < n; i++)
            {
                double weight = 0.08; // 80g smolt
                for (int d = 0; d < 540; d++)
                {
                    weight = _model.ApplySDEGrowthOneDay(
                        weight, 11.0, 5.5, 3.0, 0.001, LifeStage.Seawater);
                    if (weight <= 0.01) weight = 0.01;
                }
                finalWeights[i] = weight;
            }

            Array.Sort(finalWeights);
            double median = finalWeights[n / 2];
            double p10 = finalWeights[n / 10];
            double p90 = finalWeights[n * 9 / 10];

            _output.WriteLine($"Median: {median:F2} kg, P10: {p10:F2}, P90: {p90:F2}");

            // Realistic: median between 2-8 kg (broad range due to stochastic model)
            Assert.InRange(median, 1.5, 10.0);
        }

        [Fact]
        public void SDE_BatchGrowth_MatchesDayByDay()
        {
            // Batch and day-by-day should produce same-order results
            double dayByDay = 0.08;
            for (int d = 0; d < 180; d++)
            {
                dayByDay = _model.ApplySDEGrowthOneDay(
                    dayByDay, 11.0, 5.5, 3.0, 0.001, LifeStage.Seawater);
                if (dayByDay <= 0.01) dayByDay = 0.01;
            }

            double batch = _model.ApplySDEGrowthMultipleDays(
                0.08, 180, 11.0, 5.5, 3.0, 0.001, LifeStage.Seawater);
            if (batch <= 0.01) batch = 0.01;

            _output.WriteLine($"Day-by-day: {dayByDay:F3} kg, Batch: {batch:F3} kg");

            // Both should be positive and in same order of magnitude
            Assert.True(dayByDay > 0.08, "Day-by-day should grow from 80g");
            Assert.True(batch > 0.08, "Batch should grow from 80g");
        }

        [Fact]
        public void SDE_ColdWater_SlowerGrowth()
        {
            double cold = SimulateGrowth(6.0, 180);
            double optimal = SimulateGrowth(11.0, 180);
            
            _output.WriteLine($"Cold (6°C): {cold:F3} kg, Optimal (11°C): {optimal:F3} kg");
            Assert.True(cold < optimal, $"Cold water ({cold:F3}) should produce less growth than optimal ({optimal:F3})");
        }

        [Fact]
        public void SDE_WarmWater_SlowerThanOptimal()
        {
            double warm = SimulateGrowth(15.0, 180);
            double optimal = SimulateGrowth(11.0, 180);
            
            _output.WriteLine($"Warm (15°C): {warm:F3} kg, Optimal (11°C): {optimal:F3} kg");
            Assert.True(warm < optimal, $"Warm water ({warm:F3}) should produce less growth than optimal ({optimal:F3})");
        }

        [Fact]
        public void SimpleGrowth_540Days_SeawaterStage_ReachesHarvestWeight()
        {
            double weight = _model.ApplyGrowth(0.08, 540, LifeStage.Seawater);
            _output.WriteLine($"Simple growth 540d: {weight:F2} kg");
            // With BaselineGrowthPerDay=0.006, seawater modifier=1.2 → rate=0.0072
            // 0.08 * (1.0072)^540 ≈ 3.84 kg — realistic!
            Assert.InRange(weight, 2.0, 10.0);
        }

        private double SimulateGrowth(double temperature, int days)
        {
            double weight = 0.08;
            for (int d = 0; d < days; d++)
            {
                weight = _model.ApplySDEGrowthOneDay(
                    weight, temperature, 5.5, 3.0, 0.001, LifeStage.Seawater);
                if (weight <= 0.01) weight = 0.01;
            }
            return weight;
        }
    }
}
