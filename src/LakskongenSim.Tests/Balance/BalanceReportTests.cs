using System;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Balance
{
    /// <summary>
    /// Realism proof for the calibrated balance bands.
    ///
    /// <see cref="SimProbe"/> runs 20 seeded 3-year grow-out sims per scenario
    /// and reduces each metric to a min/median/p90 distribution. Each test below
    /// asserts the measured median against the reference band recorded in
    /// docs/BALANCE.md, with an explicit tolerance. On failure the measured
    /// distribution is included in the assertion message.
    ///
    /// Calibration v1.0.2 wired the three previously-deferred metrics into the
    /// live simulation (sea mortality anchor, registry-sourced market, seeded
    /// lice pressure), so those tests now assert the real reference bands.
    /// </summary>
    public class BalanceReportTests
    {
        private const int SeedFrom = 1;
        private const int SeedTo = 20;

        private static readonly Lazy<ScenarioDistribution> Baseline =
            new(() => SimProbe.Measure(SimScenario.Baseline, SeedFrom, SeedTo));

        private static readonly Lazy<ScenarioDistribution> HighStocking =
            new(() => SimProbe.Measure(SimScenario.HighStocking, SeedFrom, SeedTo));

        private readonly ITestOutputHelper _output;
        public BalanceReportTests(ITestOutputHelper output) => _output = output;

        // ── Reference bands (docs/BALANCE.md) ───────────────────────────────
        // Industry salmon FCR 1.1-1.3; bFCR anchor 1.15.
        private const double FcrMin = 1.10, FcrMax = 1.30;
        // Sea mortality per cycle 14-16% (NVI 2024: 15.4%).
        private const double MortalityMin = 14.0, MortalityMax = 16.0;
        // Harvest live weight 4.5-5.5 kg (Mowi/SalMar).
        private const double WeightMin = 4.5, WeightMax = 5.5;
        // Feed cost per kg produced ~25.7 (Fiskeridir Tab.38); play band 22-30.
        private const double FeedCostMin = 22.0, FeedCostMax = 30.0;
        // Realized salmon price band ~62-90 NOK/kg (Fish Pool).
        private const double PriceMin = 74.0, PriceMax = 90.0;
        // Sea-lice action limit 0.5 adult females/fish (Mattilsynet).
        private const double LiceMax = 0.5;
        // Escapes: real events are rare; guard the upper bound per year.
        private const double EscapesMax = 2.0;

        [Fact]
        public void SimProbe_IsDeterministic_SameSeedSameMetrics()
        {
            // v1.0.2: all seeded subsystems (growth, weather, feed, mortality,
            // lice) plus the market and its intelligence RNG are seeded by the
            // engine, so realized price is now deterministic too.
            var a = SimProbe.Run(7, SimScenario.Baseline);
            var b = SimProbe.Run(7, SimScenario.Baseline);
            Assert.Equal(a.MeanFcr, b.MeanFcr, 9);
            Assert.Equal(a.SeaMortalityPercentPerCycle, b.SeaMortalityPercentPerCycle, 9);
            Assert.Equal(a.MeanHarvestWeightKg, b.MeanHarvestWeightKg, 9);
            Assert.Equal(a.FeedCostPerKgProducedNok, b.FeedCostPerKgProducedNok, 9);
            Assert.Equal(a.PeakLicePerFish, b.PeakLicePerFish, 9);
            Assert.Equal(a.PeakAdultFemaleLicePerFish, b.PeakAdultFemaleLicePerFish, 9);
            Assert.Equal(a.RealizedPriceNokPerKg, b.RealizedPriceNokPerKg, 9);
        }

        [Fact]
        public void MeanFcr_WithinIndustryBand()
        {
            var d = Baseline.Value.MeanFcr;
            AssertWithin(d.Median, FcrMin, FcrMax, 0.05, "mean economic FCR", d);
        }

        [Fact]
        public void SeaMortalityPerCycle_WithinReferenceBand()
        {
            var d = Baseline.Value.SeaMortalityPercentPerCycle;
            // v1.0.2: MortalityModel maps CalibrationRegistry.Health.SeaMortalityPerCycle
            // (0.15) to a daily sea hazard over the calibrated grow-out, so the
            // measured median now sits in the NVI 14-16% band. Disease/event
            // mortality remains additive on top when storyteller events are on.
            AssertWithin(d.Median, MortalityMin, MortalityMax, 0.5,
                "sea mortality %/cycle", d);
        }

        [Fact]
        public void MeanHarvestWeight_WithinReferenceBand()
        {
            var d = Baseline.Value.MeanHarvestWeightKg;
            AssertWithin(d.Median, WeightMin, WeightMax, 0.25, "mean harvest weight kg", d);
        }

        [Fact]
        public void FeedCostPerKgProduced_WithinReferenceBand()
        {
            var d = Baseline.Value.FeedCostPerKgProducedNok;
            AssertWithin(d.Median, FeedCostMin, FeedCostMax, 2.0, "feed cost NOK/kg produced", d);
        }

        [Fact]
        public void RealizedPrice_WithinReferenceBand()
        {
            var d = Baseline.Value.RealizedPriceNokPerKg;
            // v1.0.2: MarketSimulation sources CalibrationRegistry.Market (long-
            // term mean 82, weekly theta/sigma, 42-150 band), mean-reverts its
            // supply/demand indices, applies a normal-play band around the mean,
            // and seeds its intelligence RNG. Realized median now sits in the
            // Fish Pool 74-90 NOK/kg band; shocks may still breach transiently.
            AssertWithin(d.Median, PriceMin, PriceMax, 2.0,
                "realized price NOK/kg", d);
        }

        [Fact]
        public void PeakLice_ReachesRegulatoryLimitWithoutTreatment()
        {
            var d = Baseline.Value.PeakLicePerFish;
            // v1.0.2: the engine seeds external lice pressure at sea stocking and
            // seasonally, so a clean (no-treatment) grow-out builds to the
            // Mattilsynet action limit of ~0.5 adult females/fish. The untreated
            // peak hovers at the limit and can breach it in a long, warm cycle
            // (which is exactly why treatment matters); guard against runaway
            // infestation (no treatment at all must not explode toward the 50/fish
            // crowding cap).
            AssertWithin(d.Median, 0.30, 0.60, 0.05,
                "peak adult-female lice/fish (no treatment; regulatory limit 0.5)", d);
            Assert.True(d.P90 <= 1.0,
                $"untreated lice p90 {d.P90:F3} exceeded the runaway guard 1.0 {d.Triple()}");
        }

        [Fact]
        public void EscapesPerYear_WithinBound()
        {
            var d = Baseline.Value.EscapesPerYear;
            AssertWithin(d.P90, 0.0, EscapesMax, 0.0, "escapes/year", d);
        }

        [Fact]
        public void MeasuredReport_MatchesReference_Printed()
        {
            _output.WriteLine("=== SimProbe measured distributions vs docs/BALANCE.md bands ===");
            _output.WriteLine(SimProbe.Format(Baseline.Value));
            _output.WriteLine(SimProbe.Format(HighStocking.Value));
            _output.WriteLine("reference bands: FCR 1.10-1.30 | sea mortality 14-16%/cycle | " +
                "harvest 4.5-5.5 kg | feed cost 22-30 NOK/kg | price 74-90 NOK/kg | lice <=0.5 | escapes <=2/yr");

            // Sanity: both scenarios must produce finite, positive distributions.
            Assert.True(Baseline.Value.MeanFcr.Median > 0);
            Assert.True(HighStocking.Value.MeanFcr.Median > 0);
        }

        private static void AssertWithin(double measured, double min, double max,
            double tolerance, string label, Distribution d)
        {
            bool ok = measured >= min - tolerance && measured <= max + tolerance;
            Assert.True(ok,
                $"{label}: median {measured:F3} outside [{min - tolerance:F3}, {max + tolerance:F3}]. " +
                $"min/median/p90 = {d.Triple()}");
        }
    }
}
