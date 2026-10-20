using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Balance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// Roadmap S2-03 acceptance over 10 seeds, on the engine path (zones,
    /// blooms, the shared sea-temperature anomaly; storyteller events off).
    /// The measured numbers are recorded in docs/BALANCE.md §1g.
    /// </summary>
    public class TgcGrowthAcceptanceTests
    {
        private const int Seeds = 10;
        /// <summary>Months on a 365-day year, in days.</summary>
        private const double Month = GrowthCycleProbe.DaysPerMonth;
        private static readonly double FourteenMonths = 14 * Month;
        private static readonly double EighteenMonths = 18 * Month;

        private readonly ITestOutputHelper _out;
        public TgcGrowthAcceptanceTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Po4AprilS1At100Grams_ReachesHarvestSizeIn14To18Months_OnEverySeed()
        {
            var days5 = new List<double>();
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var c = GrowthCycleProbe.Run(seed, area: 4, stopKg: 5.5);
                _out.WriteLine($"seed {seed}: 4.5 kg day {c.DaysTo4_5Kg}, 5.0 kg day {c.DaysTo5Kg}, 5.5 kg day {c.DaysTo5_5Kg}, CV {c.FinalWeightCv:F3}");
                Assert.NotNull(c.DaysTo5_5Kg);
                // In the harvest window (4.5-5.5 kg) some time between month 14 and 18.
                Assert.True(c.DaysTo4_5Kg <= EighteenMonths, $"seed {seed}: 4.5 kg only on day {c.DaysTo4_5Kg}");
                Assert.True(c.DaysTo5_5Kg >= FourteenMonths, $"seed {seed}: past 5.5 kg on day {c.DaysTo5_5Kg}");
                days5.Add(c.DaysTo5Kg!.Value);
            }
            double median = GrowthCycleProbe.Median(days5);
            _out.WriteLine($"median day to 5 kg: {median} ({median / Month:F1} months)");
            Assert.InRange(median, FourteenMonths, EighteenMonths);
        }

        /// <summary>
        /// Economic FCR (delivered feed per kg of net harvested gain: dead fish
        /// and wasted pellets count against it) on a commercial pen of 50,000
        /// standard S1 smolts released mid-April in PO4 water, with the
        /// husbandry mortality of a calibrated cycle. The zone path's bloom deck
        /// (25-55% cycle mortality, S2-05/S4-03) is measured in BALANCE.md §1g.
        /// </summary>
        [Fact]
        public void EconomicFcr_IsOnePointTwoToOnePointThirtyFive_OnEverySeed()
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var engine = new SimulationEngine { FinanceManager = new FinanceManager(100_000_000m) };
                engine.Initialize(enableEvents: false, seed: seed);
                engine.State.HomeProductionArea = 4;
                var site = new SeaSite("commercial") { MABTonnes = 5_000 };
                engine.State.SeaSites.Add(site);
                engine.Advance(GrowthCycleProbe.SpringReleaseDay);
                Assert.True(engine.StockSmolts(site.Id, 50_000, "standard").Ok);
                double stockedKg = site.Pens.Sum(p => p.GetTotalBiomassKg());
                double feed0 = engine.Feed.TotalFeedUsedKg, grown0 = engine.Feed.TotalGrowthKg;
                int stocked = site.Pens.Sum(p => p.TotalCount);
                int day = 0;
                while (site.Pens.Sum(p => p.GetTotalBiomassKg()) / Math.Max(1, site.Pens.Sum(p => p.TotalCount)) < 5.0 && day++ < 800)
                    engine.Advance(1);
                double harvestKg = site.Pens.Sum(p => p.GetTotalBiomassKg());
                double feed = engine.Feed.TotalFeedUsedKg - feed0;
                double economic = feed / (harvestKg - stockedKg);
                double biological = feed / (engine.Feed.TotalGrowthKg - grown0);
                double mortality = 1.0 - (double)site.Pens.Sum(p => p.TotalCount) / stocked;
                _out.WriteLine($"seed {seed}: {day} d, mortality {mortality:P1}, eFCR {economic:F3}, FCR on growth {biological:F3}");
                Assert.InRange(economic, 1.20, 1.35);
                Assert.True(biological < economic, "losses raise the economic FCR above the FCR on growth");
            }
        }

        [Fact]
        public void FishGrownOn_PassSixKilos_OnTheEnginePath()
        {
            for (int seed = 1; seed <= 3; seed++)
            {
                var c = GrowthCycleProbe.Run(seed, area: 4, stopKg: 6.5, horizonDays: 900);
                _out.WriteLine($"seed {seed}: 6.5 kg reached? final {c.FinalMeanKg:F2} kg after {c.MeanKgByDay.Count - 1} days");
                Assert.True(c.FinalMeanKg >= 6.5, $"seed {seed}: grown-on fish stalled at {c.FinalMeanKg:F2} kg");
            }
        }

        [Fact]
        public void EveryCohort_ReportsItsWeightCv_OnEverySeed()
        {
            var law = TgcGrowthLaw.Default;
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var c = GrowthCycleProbe.Run(seed, area: 4, stopKg: 5.0);
                Assert.InRange(c.FinalWeightCv, law.Calibration.SmoltWeightCv, 0.35);
                Assert.True(c.FinalWeightCv > law.Calibration.SmoltWeightCv + 0.01, $"seed {seed}: CV {c.FinalWeightCv:F3} never moved");
            }
        }

        /// <summary>
        /// The climate half of the pre-registered D2 claim: with the shared
        /// year-to-year sea-temperature anomaly switched off (normal years),
        /// the same spring S1 release reaches 5 kg in PO4 2-4 months before
        /// PO13 on every seed. Across weather years the per-seed gap runs
        /// 63-168 days (RUN_LOG §8 dispute): a cold spring keeps PO13 at sea
        /// through a second winter.
        /// </summary>
        [Fact]
        public void Po4ReachesFiveKilos_TwoToFourMonthsBeforePo13_InNormalYears()
        {
            var normal = CalibrationRegistry.Default.SeaTemperature with { AnomalyStdDevC = 0.0 };
            for (int seed = 1; seed <= 5; seed++)
            {
                int po4 = GrowthCycleProbe.Run(seed, 4, stopKg: 5.0,
                    configure: (e, _) => e.Weather.ApplySeaTemperatureCalibration(normal)).DaysTo5Kg!.Value;
                int po13 = GrowthCycleProbe.Run(seed, 13, stopKg: 5.0,
                    configure: (e, _) => e.Weather.ApplySeaTemperatureCalibration(normal)).DaysTo5Kg!.Value;
                _out.WriteLine($"seed {seed}: PO4 {po4} d, PO13 {po13} d, gap {po13 - po4}");
                Assert.InRange(po13 - po4, 60, 120);
            }
        }
    }
}
