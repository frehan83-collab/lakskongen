using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using LakskongenSim.Market;

namespace LakskongenSim.Tests.Market
{
    /// <summary>
    /// Goodness-of-fit harness for the Fish Pool calibration series.
    /// Characterization only — asserts determinism, math correctness, and
    /// queryability. Never rebalances prices or O-U parameters.
    /// </summary>
    public class MarketCalibrationFitTests
    {
        private readonly ITestOutputHelper _output;

        public MarketCalibrationFitTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void RSquared_PerfectPrediction_ReturnsOne()
        {
            var series = new List<double> { 80.0, 85.5, 90.25, 77.0, 95.0 };
            Assert.Equal(1.0, MarketSimulation.ComputeRSquared(series, new List<double>(series)), precision: 10);
        }

        [Fact]
        public void RSquared_MeanPrediction_ReturnsZero()
        {
            var observed = new List<double> { 80.0, 90.0, 100.0 };
            var mean = new List<double> { 90.0, 90.0, 90.0 };
            Assert.Equal(0.0, MarketSimulation.ComputeRSquared(observed, mean), precision: 10);
        }

        [Fact]
        public void RSquared_KnownSeries_MatchesHandComputation()
        {
            // obs mean = 2.0; SStot = 2.0; SSres = 0.5 → R² = 0.75
            var observed = new List<double> { 1.0, 2.0, 3.0 };
            var simulated = new List<double> { 1.5, 2.0, 2.5 };
            Assert.Equal(0.75, MarketSimulation.ComputeRSquared(observed, simulated), precision: 10);
        }

        [Fact]
        public void RSquared_ConstantObserved_PerfectMatchReturnsOneOtherwiseZero()
        {
            var flat = new List<double> { 85.0, 85.0, 85.0 };
            Assert.Equal(1.0, MarketSimulation.ComputeRSquared(flat, new List<double>(flat)), precision: 10);
            Assert.Equal(0.0, MarketSimulation.ComputeRSquared(flat, new List<double> { 85.0, 86.0, 85.0 }), precision: 10);
        }

        [Fact]
        public void RSquared_EmptyOrMismatched_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                MarketSimulation.ComputeRSquared(new List<double>(), new List<double>()));
            Assert.Throws<ArgumentException>(() =>
                MarketSimulation.ComputeRSquared(
                    new List<double> { 1.0, 2.0 }, new List<double> { 1.0 }));
        }

        [Fact]
        public void CalibrationFit_IsDeterministicAndQueryable()
        {
            var market = new MarketSimulation();

            // Fixed Fish Pool reference slice: Norwegian yearly average export
            // prices 2020-2025 from research/real-salmon-prices.md (IndexMundi
            // monthly data via FishFarmingExpert). Inline so the harness test
            // never depends on markdown file resolution.
            var fishPoolYearlyAverages = new List<HistoricalPrice>
            {
                new("2020 avg", 62.68m, 2020),
                new("2021 avg", 60.81m, 2021),
                new("2022 avg", 82.26m, 2022),
                new("2023 avg", 95.76m, 2023),
                new("2024 avg", 94.35m, 2024),
                new("2025 avg", 83.84m, 2025),
            };

            var first = market.ComputeCalibrationFit(fishPoolYearlyAverages, seed: 42);
            var second = market.ComputeCalibrationFit(fishPoolYearlyAverages, seed: 42);

            Assert.Equal(first.RSquared, second.RSquared, precision: 12);
            Assert.Equal(fishPoolYearlyAverages.Count, first.ObservationCount);
            Assert.True(double.IsFinite(first.RSquared),
                $"R² must be finite (got {first.RSquared}).");
            Assert.True(first.ObservedMean > 0 && first.SimulatedMean > 0);

            // Queryable metric mirrors the latest report.
            Assert.Equal(first.RSquared, market.LastCalibrationR2, precision: 12);
            Assert.Equal(first.ObservationCount, market.LastCalibrationObservationCount);

            _output.WriteLine(
                $"Fish Pool fit: R²={first.RSquared:F4} over {first.ObservationCount} obs " +
                $"(observed mean {first.ObservedMean:F2} NOK/kg, simulated mean {first.SimulatedMean:F2} NOK/kg).");

            // Harness is side-effect-free: live price state untouched.
            var priceBefore = market.BasePricePerKg;
            market.ComputeCalibrationFit(fishPoolYearlyAverages, seed: 7);
            Assert.Equal(priceBefore, market.BasePricePerKg);
        }

        [Fact]
        public void CalibrationFit_EmptySeries_YieldsNaNReport()
        {
            var market = new MarketSimulation();
            var report = market.ComputeCalibrationFit(
                new List<HistoricalPrice>(), seed: 42);
            Assert.Equal(0, report.ObservationCount);
            Assert.True(double.IsNaN(report.RSquared));
            Assert.True(double.IsNaN(market.LastCalibrationR2));
        }

        [Fact]
        public void CalibrationFit_DoesNotRebalancePrices()
        {
            var market = new MarketSimulation();
            var meanBefore = market.LongTermMean;
            var thetaBefore = market.MeanReversionSpeedWeekly;
            var sigmaBefore = market.VolatilityWeekly;
            var priceBefore = market.BasePricePerKg;

            market.ComputeCalibrationFit();

            Assert.Equal(meanBefore, market.LongTermMean);
            Assert.Equal(thetaBefore, market.MeanReversionSpeedWeekly);
            Assert.Equal(sigmaBefore, market.VolatilityWeekly);
            Assert.Equal(priceBefore, market.BasePricePerKg);
        }
    }
}
