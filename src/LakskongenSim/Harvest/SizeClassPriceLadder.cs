using System;
using LakskongenSim.Calibration;

namespace LakskongenSim.Harvest
{
    /// <summary>
    /// The size-class price ladder (roadmap S2-04): salmon is sold in size
    /// classes by gutted (HOG) weight, 1-2 kg, 2-3 kg ... 7+ kg, and small fish
    /// sell at a discount to the 4-5 kg reference class while large fish earn a
    /// premium (<see cref="HarvestCalibration"/>, Kontali via Mowi's Handbook).
    /// Pure: no state, no random numbers.
    /// </summary>
    public static class SizeClassPriceLadder
    {
        /// <summary>Size classes: under 1 kg, 1-2 ... 6-7 kg, 7 kg and more (HOG).</summary>
        public const int ClassCount = 8;

        /// <summary>Index of the reference class (4-5 kg HOG).</summary>
        public const int ReferenceClass = 4;

        /// <summary>Lower bound of size class <paramref name="sizeClass"/> (kg HOG).</summary>
        public static double LowerBoundKg(int sizeClass) => Math.Clamp(sizeClass, 0, ClassCount - 1);

        /// <summary>Upper bound of size class <paramref name="sizeClass"/> (kg HOG); infinity for the top class.</summary>
        public static double UpperBoundKg(int sizeClass)
            => sizeClass >= ClassCount - 1 ? double.PositiveInfinity : Math.Clamp(sizeClass, 0, ClassCount - 1) + 1.0;

        /// <summary>The industry's label of a size class ("&lt;1 kg", "4-5 kg", "7+ kg").</summary>
        public static string Label(int sizeClass) => sizeClass switch
        {
            <= 0 => "<1 kg",
            >= ClassCount - 1 => "7+ kg",
            _ => $"{sizeClass}-{sizeClass + 1} kg"
        };

        /// <summary>The size class of a fish of <paramref name="hogKg"/> gutted weight.</summary>
        public static int ClassOf(double hogKg)
            => double.IsFinite(hogKg) ? (int)Math.Clamp(Math.Floor(Math.Max(0.0, hogKg)), 0, ClassCount - 1) : 0;

        /// <summary>Price of size class <paramref name="sizeClass"/> relative to the 4-5 kg reference.</summary>
        public static double Factor(HarvestCalibration calibration, int sizeClass)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            return Math.Clamp(sizeClass, 0, ClassCount - 1) switch
            {
                0 => calibration.PriceFactorUnder1Kg,
                1 => calibration.PriceFactor1To2Kg,
                2 => calibration.PriceFactor2To3Kg,
                3 => calibration.PriceFactor3To4Kg,
                4 => calibration.PriceFactor4To5Kg,
                5 => calibration.PriceFactor5To6Kg,
                6 => calibration.PriceFactor6To7Kg,
                _ => calibration.PriceFactorOver7Kg
            };
        }

        /// <summary>Price factor of one fish of <paramref name="hogKg"/> gutted weight.</summary>
        public static double FactorForHogWeight(HarvestCalibration calibration, double hogKg)
            => Factor(calibration, ClassOf(hogKg));

        /// <summary>
        /// Add the gutted kilos of a cohort to <paramref name="hogKgByClass"/>, by
        /// size class: <paramref name="count"/> fish of mean live weight
        /// <paramref name="meanLiveKg"/> and weight CV <paramref name="weightCv"/>
        /// (individual weights lognormal), gutted at <paramref name="hogYield"/>.
        /// Returns the gutted kilos added. A CV of 0 puts the whole cohort in the
        /// class of its mean.
        /// </summary>
        public static double AddCohort(Span<double> hogKgByClass, int count, double meanLiveKg, double weightCv, double hogYield)
        {
            if (hogKgByClass.Length < ClassCount)
                throw new ArgumentException($"needs {ClassCount} classes", nameof(hogKgByClass));
            if (count <= 0 || !(meanLiveKg > 0.0) || !(hogYield > 0.0)) return 0.0;

            double meanHog = meanLiveKg * hogYield;
            double totalHog = count * meanHog;
            if (!(weightCv > 0.0) || !double.IsFinite(weightCv))
            {
                hogKgByClass[ClassOf(meanHog)] += totalHog;
                return totalHog;
            }

            // Lognormal individual weights: sigma^2 = ln(1 + CV^2), mu = ln(mean) - sigma^2/2.
            // The biomass in [a, b) is mean x [Phi((ln b - mu - s^2)/s) - Phi((ln a - mu - s^2)/s)].
            double s2 = Math.Log(1.0 + weightCv * weightCv);
            double s = Math.Sqrt(s2);
            double mu = Math.Log(meanHog) - 0.5 * s2;
            double previous = 0.0; // biomass share below the lower bound of class 0
            double assigned = 0.0;
            for (int c = 0; c < ClassCount; c++)
            {
                double upper = UpperBoundKg(c);
                double below = double.IsPositiveInfinity(upper)
                    ? 1.0
                    : NormalCdf((Math.Log(upper) - mu - s2) / s);
                double share = Math.Max(0.0, below - previous);
                double kg = c == ClassCount - 1 ? totalHog - assigned : totalHog * share;
                hogKgByClass[c] += kg;
                assigned += kg;
                previous = below;
            }
            return totalHog;
        }

        /// <summary>
        /// Mean price factor of a lot split by size class: the gutted kilos in
        /// each class times its factor, over all the gutted kilos (1 when empty).
        /// </summary>
        public static double MeanFactor(HarvestCalibration calibration, ReadOnlySpan<double> hogKgByClass)
        {
            double kg = 0.0, weighted = 0.0;
            for (int c = 0; c < ClassCount && c < hogKgByClass.Length; c++)
            {
                kg += hogKgByClass[c];
                weighted += hogKgByClass[c] * Factor(calibration, c);
            }
            return kg > 0.0 ? weighted / kg : 1.0;
        }

        /// <summary>Standard normal cumulative distribution.</summary>
        public static double NormalCdf(double x)
        {
            if (double.IsNegativeInfinity(x)) return 0.0;
            if (double.IsPositiveInfinity(x)) return 1.0;
            return 0.5 * Erfc(-x / Math.Sqrt(2.0));
        }

        /// <summary>
        /// Complementary error function, fractional error below 1.2e-7 everywhere
        /// (Numerical Recipes, Chebyshev fit). Deterministic, no state.
        /// </summary>
        private static double Erfc(double x)
        {
            double z = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.5 * z);
            double ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 +
                t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 +
                t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0.0 ? ans : 2.0 - ans;
        }
    }
}
