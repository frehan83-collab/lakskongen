using System;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Biology
{
    /// <summary>
    /// Roadmap S2-03: the sea-phase growth law (<see cref="TgcGrowthLaw"/>):
    /// growth by the thermal growth coefficient, appetite by temperature and
    /// size, the response to the ration fed, and the cohort weight CV.
    /// </summary>
    public class TgcGrowthLawTests
    {
        private static readonly TgcGrowthLaw Law = TgcGrowthLaw.Default;
        private static readonly GrowthCalibration C = CalibrationRegistry.Default.Growth;

        /// <summary>A cohort grown <paramref name="days"/> days at a constant temperature, fed to appetite.</summary>
        private static CohortGrowthDay Grow(double weightKg, double temperatureC, int days,
            double ration = 1.0, double dayLengthHours = TgcGrowthLaw.NeutralDayLengthHours,
            double spread = FeedSystem.DefaultSpreadQuality, double cv = 0.0)
        {
            var conditions = new GrowthConditions(temperatureC, ration, SpreadQuality: spread, DayLengthHours: dayLengthHours);
            CohortGrowthDay day = new(weightKg, cv, 0, 0, 0, 0, 0);
            double eaten = 0, gained = 0;
            for (int i = 0; i < days; i++)
            {
                day = Law.GrowOneDay(day.WeightKg, day.WeightCv, LifeStage.Seawater, conditions, satiationFcr: 1.1);
                eaten += day.EatenFeedKg;
                gained += day.GrowthKg;
            }
            return day with { EatenFeedKg = eaten, GrowthKg = gained };
        }

        private static double CubeRootGrams(double kg) => Math.Cbrt(kg * 1000.0);

        [Fact]
        public void BelowThreeKilos_TheCubeRootGrowsByTheThermalGrowthCoefficient()
        {
            // TGC = (W1^(1/3) - W0^(1/3)) / degree-days x 1000, W in grams.
            var grown = Grow(0.10, 10.0, 200);
            double tgc = (CubeRootGrams(grown.WeightKg) - CubeRootGrams(0.10)) / (10.0 * 200) * 1000.0;
            Assert.True(grown.WeightKg < C.TgcValidToKg, $"left the TGC range: {grown.WeightKg:F2} kg");
            Assert.Equal(C.ThermalGrowthCoefficient, tgc, 9);
            Assert.InRange(C.ThermalGrowthCoefficient, 2.8, 3.2); // roadmap S2-03 band, sourced in the registry
        }

        [Theory]
        [InlineData(4.0)]
        [InlineData(4.5)]
        [InlineData(5.0)]
        public void GrowthAtFourToFiveDegrees_IsAtLeastThreeTenthsOfTheOptimum(double temperatureC)
        {
            // Acceptance 3, measured on fish: the same 1 kg fish for one day.
            double atOptimum = Law.SatiationGrowthKg(1.0, Law.PeakGrowthTemperatureC);
            double cold = Law.SatiationGrowthKg(1.0, temperatureC);
            Assert.True(cold / atOptimum >= 0.3, $"{temperatureC} °C: {cold / atOptimum:F3} of the optimum");
            Assert.Equal(Law.RelativeGrowthAt(temperatureC), temperatureC / C.OptimalTemperatureC, 9);
        }

        [Fact]
        public void Growth_PeaksAtTheOptimum_HalvesAtNineteenDegrees_AndStopsAtTheLimit()
        {
            Assert.Equal(C.OptimalTemperatureC, Law.PeakGrowthTemperatureC, 9);
            for (double t = 0.5; t < 22.0; t += 0.5)
                Assert.True(Law.EffectiveDegrees(t) <= Law.EffectiveDegrees(C.OptimalTemperatureC) + 1e-12, $"{t} °C beats the optimum");
            // Calado et al. 2021: 2 kg salmon at 19 °C ate and grew at least 50% less than at 14 °C.
            Assert.InRange(Law.EffectiveDegrees(19.0) / Law.EffectiveDegrees(14.0), 0.45, 0.55);
            Assert.Equal(0.0, Law.EffectiveDegrees(C.UpperGrowthLimitC));
            Assert.Equal(0.0, Law.EffectiveDegrees(25.0));
            Assert.Equal(0.0, Law.EffectiveDegrees(0.0));
            Assert.Equal(0.0, Law.EffectiveDegrees(-1.0));
            Assert.Equal(0.0, Law.EffectiveDegrees(double.NaN));
            // Warmer than the optimum is slower: southern summers slow growth (Thyholdt 2014).
            Assert.True(Law.SatiationGrowthKg(2.0, 16.0) < Law.SatiationGrowthKg(2.0, 13.0));
        }

        [Fact]
        public void FishGrownOn_PassSixKilos_ButSlowAboveThree()
        {
            // Acceptance 4. The old law's asymptote (5.5 kg) held fish below 6 kg forever.
            var grown = Grow(5.0, 10.0, 365);
            Assert.True(grown.WeightKg > 6.0, $"a year on from 5 kg at 10 °C: {grown.WeightKg:F2} kg");
            Assert.True(grown.WeightKg < C.AsymptoticWeightKg);
            Assert.Equal(1.0, Law.SizeFactor(0.1));
            Assert.Equal(1.0, Law.SizeFactor(C.TgcValidToKg));
            Assert.InRange(Law.SizeFactor(5.0), 0.7, 0.9);
            Assert.True(Law.SizeFactor(10.0) < Law.SizeFactor(5.0));
            Assert.Equal(0.0, Law.SizeFactor(C.AsymptoticWeightKg));
        }

        [Fact]
        public void ARestrictedRation_GrowsLess_EatsLess_AndConvertsBetter()
        {
            var full = Grow(1.0, 11.0, 120, ration: 1.0);
            var restricted = Grow(1.0, 11.0, 120, ration: 0.7);
            Assert.True(restricted.GrowthKg < full.GrowthKg);
            Assert.True(restricted.EatenFeedKg < full.EatenFeedKg);
            double fcrFull = full.EatenFeedKg / full.GrowthKg;
            double fcrRestricted = restricted.EatenFeedKg / restricted.GrowthKg;
            Assert.True(fcrRestricted < fcrFull, $"FCR {fcrRestricted:F3} at 70% vs {fcrFull:F3} at appetite");
            // The optimum ration (best conversion) lies below satiation, near 70% (Brett 1979).
            double best = 1.0, bestFcr = double.MaxValue;
            for (double r = 0.2; r <= 1.0 + 1e-9; r += 0.05)
            {
                double f = Law.RelativeFcrAtRation(r);
                if (f < bestFcr) { bestFcr = f; best = r; }
            }
            Assert.InRange(best, 0.6, 0.8);
            Assert.Equal(1.0, Law.RationGrowthFraction(1.0), 12);
            Assert.Equal(0.0, Law.RationGrowthFraction(C.MaintenanceRationFraction), 12);
        }

        [Fact]
        public void StarvedFish_LoseWeight_AndEatNothing()
        {
            var day = Law.GrowOneDay(2.0, 0.2, LifeStage.Seawater, new GrowthConditions(10.0, RationFraction: 0.0), 1.1);
            Assert.True(day.GrowthKg < 0.0);
            Assert.Equal(0.0, day.EatenFeedKg);
            Assert.True(day.AppetiteFeedKg > 0.0, "a starved fish still has an appetite");
            Assert.Equal(0.2, day.WeightCv); // the spread does not move while the fish shrink
        }

        [Fact]
        public void Appetite_RisesWithTemperature_AndFallsWithFishSize_AsAShareOfBodyWeight()
        {
            static double PercentBodyWeight(double kg, double t)
                => Law.GrowOneDay(kg, 0, LifeStage.Seawater, new GrowthConditions(t), satiationFcr: 1.1).AppetiteFeedKg / kg * 100.0;

            Assert.True(PercentBodyWeight(1.0, 12.0) > PercentBodyWeight(1.0, 6.0));
            Assert.True(PercentBodyWeight(1.0, 12.0) > PercentBodyWeight(1.0, 18.0));
            Assert.True(PercentBodyWeight(0.5, 12.0) > PercentBodyWeight(4.0, 12.0));
            // Feeding-table magnitudes: ~1-2 %BW/day for small post-smolts at 12 °C, under 1 % for 4 kg fish.
            Assert.InRange(PercentBodyWeight(0.2, 12.0), 1.0, 3.0);
            Assert.InRange(PercentBodyWeight(4.0, 12.0), 0.3, 1.0);
            // Poor water or damaged gills shrink the appetite, not the ration policy.
            var sick = Law.GrowOneDay(1.0, 0, LifeStage.Seawater, new GrowthConditions(12.0, AppetiteModifier: 0.6), 1.1);
            var healthy = Law.GrowOneDay(1.0, 0, LifeStage.Seawater, new GrowthConditions(12.0), 1.1);
            Assert.Equal(0.6, sick.AppetiteFeedKg / healthy.AppetiteFeedKg, 9);
            Assert.True(sick.GrowthKg < healthy.GrowthKg);
        }

        [Fact]
        public void TheCohortWeightCv_MovesTowardTheSpreadTheFeedingSets()
        {
            double perfect = Grow(0.10, 11.0, 400, spread: 1.0, cv: C.SmoltWeightCv).WeightCv;
            double poor = Grow(0.10, 11.0, 400, spread: 0.3, cv: C.SmoltWeightCv).WeightCv;
            double restricted = Grow(0.10, 11.0, 400, ration: 0.7, spread: 1.0, cv: C.SmoltWeightCv).WeightCv;
            Assert.True(poor > perfect, $"poor spread {poor:F3} vs perfect {perfect:F3}");
            Assert.True(restricted > perfect, $"restricted {restricted:F3} vs full ration {perfect:F3}");
            Assert.InRange(perfect, C.SmoltWeightCv, Law.SettledWeightCv(1.0, 1.0));
            // A cohort with no CV yet starts at the CV a standard cohort of its weight has.
            Assert.Equal(C.SmoltWeightCv, Law.InitialWeightCv(0.10), 12);
            Assert.InRange(Law.InitialWeightCv(5.0), C.SmoltWeightCv, Law.SettledWeightCv(FeedSystem.DefaultSpreadQuality, 1.0));
            Assert.Equal(Law.InitialWeightCv(2.0), Law.GrowOneDay(2.0, 0.0, LifeStage.Seawater, new GrowthConditions(0.0), 1.1).WeightCv, 12);
        }

        [Fact]
        public void Light_RaisesGrowth_UnderLongDays_AndLowersItInPolarNight()
        {
            Assert.Equal(1.0, Law.PhotoperiodFactor(12.0), 12);
            Assert.Equal(1.0 + C.PhotoperiodGrowthSlope, Law.PhotoperiodFactor(24.0), 12);
            Assert.Equal(1.0 - C.PhotoperiodGrowthSlope, Law.PhotoperiodFactor(0.0), 12);
            Assert.True(Law.SatiationGrowthKg(1.0, 10.0, dayLengthHours: 20.0) > Law.SatiationGrowthKg(1.0, 10.0));
        }

        [Fact]
        public void DayLength_FollowsLatitudeAndSeason()
        {
            const int midsummer = 171, midwinter = 354, equinox = 79;
            // Øst-Finnmark (70.4°N): midnight sun and polar night.
            Assert.Equal(24.0, DayLength.Hours(70.4, midsummer));
            Assert.Equal(0.0, DayLength.Hours(70.4, midwinter));
            // Bergen area (60.4°N): ~19 h in June, ~6 h in December.
            Assert.InRange(DayLength.Hours(60.4, midsummer), 18.5, 19.5);
            Assert.InRange(DayLength.Hours(60.4, midwinter), 5.5, 6.5);
            Assert.InRange(DayLength.Hours(60.4, equinox), 11.9, 12.5);
        }

        [Fact]
        public void TheLaw_DrawsNoRandomNumbers_SoTheSameInputsGiveTheSameDay()
        {
            var conditions = new GrowthConditions(9.3, 0.85, 0.9, 1.04, 0.97, 0.8, 15.5);
            var a = Law.GrowOneDay(1.7, 0.21, LifeStage.Seawater, conditions, 1.12);
            var b = Law.GrowOneDay(1.7, 0.21, LifeStage.Seawater, conditions, 1.12);
            Assert.Equal(a, b);
        }
    }
}
