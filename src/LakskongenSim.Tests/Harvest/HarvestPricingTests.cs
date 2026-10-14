using System;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Harvest;
using Xunit;

namespace LakskongenSim.Tests.Harvest
{
    /// <summary>
    /// Roadmap S2-04, the pure parts of the harvest pipeline: the HOG yield, the
    /// size-class price ladder on gutted weight and the quality grades.
    /// </summary>
    public class HarvestPricingTests
    {
        private static readonly HarvestCalibration Cal = CalibrationRegistry.Default.Harvest;

        /// <summary>Gross price per LIVE kilo of one fish of <paramref name="liveKg"/>, as a share of the reference price.</summary>
        private static double PricePerLiveKg(double liveKg)
            => Cal.HogYieldFraction * SizeClassPriceLadder.FactorForHogWeight(Cal, liveKg * Cal.HogYieldFraction);

        [Fact]
        public void ATwoKiloFish_EarnsAtLeast25PercentLessPerKilo_ThanAFiveKiloFish()
        {
            // Acceptance 2. A 2 kg fish guts to 1.68 kg (the 1-2 kg class), a 5 kg
            // fish to 4.2 kg (the 4-5 kg reference class).
            double two = PricePerLiveKg(2.0), five = PricePerLiveKg(5.0);
            Assert.True(two <= 0.75 * five, $"2 kg earns {two / five:P1} of 5 kg per kilo");
            // Net of the harvest cost (the same per kilo) the gap only widens.
            double spot = 80.0;
            double cost = (double)(Cal.WellboatCostPerKgHogNok + Cal.SlaughterCostPerKgHogNok) * Cal.HogYieldFraction;
            Assert.True(two * spot - cost <= 0.75 * (five * spot - cost));
        }

        [Fact]
        public void SizeClasses_AreGuttedWeight_AndTheLadderRisesWithSize()
        {
            // 4.5 kg live = 3.78 kg HOG: the 3-4 kg class, not 4-5.
            Assert.Equal(3, SizeClassPriceLadder.ClassOf(4.5 * Cal.HogYieldFraction));
            Assert.Equal(4, SizeClassPriceLadder.ClassOf(5.0 * Cal.HogYieldFraction));
            Assert.Equal(0, SizeClassPriceLadder.ClassOf(0.5));
            Assert.Equal(7, SizeClassPriceLadder.ClassOf(12.0));
            Assert.Equal("<1 kg", SizeClassPriceLadder.Label(0));
            Assert.Equal("4-5 kg", SizeClassPriceLadder.Label(4));
            Assert.Equal("7+ kg", SizeClassPriceLadder.Label(7));
            for (int c = 1; c < SizeClassPriceLadder.ClassCount; c++)
                Assert.True(SizeClassPriceLadder.Factor(Cal, c) > SizeClassPriceLadder.Factor(Cal, c - 1),
                    $"class {c} does not earn more than class {c - 1}");
            Assert.Equal(1.0, SizeClassPriceLadder.Factor(Cal, SizeClassPriceLadder.ReferenceClass));
        }

        [Fact]
        public void ACohortSplitsIntoSizeClasses_AsALognormal_WithEveryGuttedKiloAccountedFor()
        {
            Span<double> classes = stackalloc double[SizeClassPriceLadder.ClassCount];
            double hog = SizeClassPriceLadder.AddCohort(classes, 10_000, 5.0, 0.20, Cal.HogYieldFraction);
            Assert.Equal(10_000 * 5.0 * Cal.HogYieldFraction, hog, 6);
            double sum = 0.0;
            foreach (double kg in classes) sum += kg;
            Assert.Equal(hog, sum, 6);

            // Independent check by numerical integration of the lognormal biomass density.
            double s2 = Math.Log(1.0 + 0.04), s = Math.Sqrt(s2), mean = 5.0 * Cal.HogYieldFraction;
            double mu = Math.Log(mean) - s2 / 2.0;
            double Integrate(double a, double b)
            {
                double total = 0.0;
                int n = 20_000;
                double lo = Math.Max(a, 1e-6), hi = Math.Min(b, 30.0);
                double h = (hi - lo) / n;
                for (int i = 0; i < n; i++)
                {
                    double w = lo + (i + 0.5) * h;
                    double pdf = Math.Exp(-Math.Pow(Math.Log(w) - mu, 2) / (2 * s2)) / (w * s * Math.Sqrt(2 * Math.PI));
                    total += w * pdf * h;
                }
                return total / mean;
            }
            for (int c = 1; c < SizeClassPriceLadder.ClassCount - 1; c++)
                Assert.Equal(Integrate(c, c + 1), classes[c] / hog, 4);

            // A cohort with no spread sits in the class of its mean.
            Span<double> sharp = stackalloc double[SizeClassPriceLadder.ClassCount];
            SizeClassPriceLadder.AddCohort(sharp, 100, 5.0, 0.0, Cal.HogYieldFraction);
            Assert.Equal(100 * 5.0 * Cal.HogYieldFraction, sharp[4], 9);
        }

        [Fact]
        public void TheNormalCdf_IsAccurate()
        {
            Assert.Equal(0.5, SizeClassPriceLadder.NormalCdf(0.0), 7);
            Assert.Equal(0.8413447461, SizeClassPriceLadder.NormalCdf(1.0), 7);
            Assert.Equal(0.0249978952, SizeClassPriceLadder.NormalCdf(-1.96), 7);
            Assert.Equal(0.9986501020, SizeClassPriceLadder.NormalCdf(3.0), 7);
        }

        [Fact]
        public void Grades_FollowNorway2024_AfterSummerAndAfterWinter()
        {
            // Fiskehelserapporten 2024 §5.8: superior ~90% in the second half year,
            // ~65% in the first (after a winter at sea, winter wounds); ordinary ~1.5%.
            var summer = HarvestGrading.Grade(Cal, new HarvestCondition(5.0, ColdExposure: 0.1));
            var spring = HarvestGrading.Grade(Cal, new HarvestCondition(5.0, ColdExposure: 1.0));
            Assert.InRange(summer.Superior, 0.89, 0.93);
            Assert.InRange(spring.Superior, 0.62, 0.68);
            Assert.InRange(summer.Ordinary, 0.010, 0.020);
            foreach (var g in new[] { summer, spring })
                Assert.Equal(1.0, g.Superior + g.Ordinary + g.Production, 12);
            // Price factor of a lot: superior 1, ordinary 0.90, production 0.70 (Mowi: ~30% off).
            Assert.Equal(summer.Superior + summer.Ordinary * 0.90 + summer.Production * 0.70, summer.PriceFactor(Cal), 12);
        }

        [Fact]
        public void Wounds_Lice_Maturation_AndAHoldingsOwnCause_EachDowngrade()
        {
            var clean = HarvestGrading.Grade(Cal, new HarvestCondition(5.0));
            var treated = HarvestGrading.Grade(Cal, new HarvestCondition(5.0, TreatmentInjuryLoad: 0.7));
            var lousy = HarvestGrading.Grade(Cal, new HarvestCondition(5.0, AdultFemaleLicePerFish: 2.0));
            var grownOn = HarvestGrading.Grade(Cal, new HarvestCondition(8.0));
            var offFlavour = HarvestGrading.Grade(Cal, new HarvestCondition(5.0, ExtraProductionShare: 0.3));
            Assert.True(treated.Production > clean.Production + 0.10, $"{treated.Production:P1} vs {clean.Production:P1}");
            Assert.True(lousy.Production > clean.Production + 0.05);
            Assert.True(grownOn.Production > clean.Production + 0.10);
            Assert.True(offFlavour.Production > clean.Production + 0.25);
            // Below 6 kg maturation does nothing.
            Assert.Equal(clean.Production, HarvestGrading.Grade(Cal, new HarvestCondition(5.9)).Production, 12);
        }

        [Fact]
        public void ColdWaterMemory_BuildsBelowTheWinterWoundTemperature_AndFadesAbove()
        {
            double exposure = 0.0;
            for (int d = 0; d < 60; d++) exposure = HarvestGrading.NextColdExposure(Cal, exposure, 5.0);
            Assert.Equal(1.0 - Math.Exp(-1.0), exposure, 9);
            for (int d = 0; d < 60; d++) exposure = HarvestGrading.NextColdExposure(Cal, exposure, 12.0);
            Assert.Equal((1.0 - Math.Exp(-1.0)) * Math.Exp(-1.0), exposure, 9);
            // A land tank held at 12 °C never builds any.
            double tank = 0.0;
            for (int d = 0; d < 365; d++) tank = HarvestGrading.NextColdExposure(Cal, tank, 12.0);
            Assert.Equal(0.0, tank);
        }

        [Fact]
        public void ALot_IsPricedAsItsClassesTimesItsGrades()
        {
            var lot = new HarvestLot(Cal);
            var condition = new HarvestCondition(4.6, ColdExposure: 0.4);
            lot.AddFish(8_000, 4.6, 0.21, condition);
            Assert.Equal(8_000 * 4.6, lot.LiveKg, 6);
            Assert.Equal(8_000 * 4.6 * Cal.HogYieldFraction, lot.HogKg, 6);
            double sized = lot.HogKgBySizeClassArray().Select((kg, c) => kg * SizeClassPriceLadder.Factor(Cal, c)).Sum();
            double gradeFactor = HarvestGrading.Grade(Cal, condition).PriceFactor(Cal);
            Assert.Equal(sized * gradeFactor, lot.ValueKg, 6);
            Assert.Equal(lot.SizeFactor * lot.GradeFactor, lot.ValueFactor, 9);
            Assert.InRange(lot.WeightCv, 0.209, 0.211);
        }
    }
}
