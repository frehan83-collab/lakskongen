using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// The numerics the offshore metocean model and its forecast share (F2-06):
    /// the standard normal CDF and quantile in double precision, the
    /// transform the sea states are drawn in, and an 8-point Gauss-Legendre rule.
    /// Pure and deterministic: no state, no RNG.
    /// </summary>
    public static class MetoceanMath
    {
        private const double Sqrt2Pi = 2.5066282746310002;
        private const double HalfUlpOfUnitInterval = 1.0 / 18014398509481984.0; // 2^-54

        /// <summary>
        /// Standard normal CDF, absolute error below 1e-14 (Hart 1968, algorithm
        /// 5666, in the double-precision form of G. West, "Better approximations
        /// to cumulative normal functions", Wilmott 2005).
        /// </summary>
        public static double NormalCdf(double x)
        {
            if (double.IsNaN(x)) return double.NaN;
            double a = Math.Abs(x);
            double tail;
            if (a > 37.0)
            {
                tail = 0.0;
            }
            else
            {
                double e = Math.Exp(-a * a / 2.0);
                if (a < 7.07106781186547)
                {
                    double num = 3.52624965998911E-02 * a + 0.700383064443688;
                    num = num * a + 6.37396220353165;
                    num = num * a + 33.912866078383;
                    num = num * a + 112.079291497871;
                    num = num * a + 221.213596169931;
                    num = num * a + 220.206867912376;
                    double den = 8.83883476483184E-02 * a + 1.75566716318264;
                    den = den * a + 16.064177579207;
                    den = den * a + 86.7807322029461;
                    den = den * a + 296.564248779674;
                    den = den * a + 637.333633378831;
                    den = den * a + 793.826512519948;
                    den = den * a + 440.413735824752;
                    tail = e * num / den;
                }
                else
                {
                    double cf = a + 0.65;
                    cf = a + 4.0 / cf;
                    cf = a + 3.0 / cf;
                    cf = a + 2.0 / cf;
                    cf = a + 1.0 / cf;
                    tail = e / cf / Sqrt2Pi;
                }
            }
            return x > 0.0 ? 1.0 - tail : tail;
        }

        /// <summary>Standard normal density.</summary>
        public static double NormalPdf(double x) => Math.Exp(-0.5 * x * x) / Sqrt2Pi;

        /// <summary>
        /// Standard normal quantile, relative error about 1e-16 (Wichura 1988,
        /// algorithm AS 241 PPND16, Applied Statistics 37:477-484).
        /// </summary>
        public static double NormalQuantile(double p)
        {
            if (double.IsNaN(p) || p < 0.0 || p > 1.0)
                throw new ArgumentOutOfRangeException(nameof(p), p, "Probability must lie in [0, 1]");
            if (p == 0.0) return double.NegativeInfinity;
            if (p == 1.0) return double.PositiveInfinity;
            double q = p - 0.5;
            if (Math.Abs(q) <= 0.425)
            {
                double r = 0.180625 - q * q;
                return q * (((((((2509.0809287301226727 * r + 33430.575583588128105) * r + 67265.770927008700853) * r
                        + 45921.953931549871457) * r + 13731.693765509461125) * r + 1971.5909503065514427) * r
                        + 133.14166789178437745) * r + 3.387132872796366608)
                    / (((((((5226.495278852545925 * r + 28729.085735721942674) * r + 39307.89580009271061) * r
                        + 21213.794301586595867) * r + 5394.1960214247511077) * r + 687.1870074920579083) * r
                        + 42.313330701600911252) * r + 1.0);
            }
            double rr = q < 0.0 ? p : 1.0 - p;
            rr = Math.Sqrt(-Math.Log(rr));
            double value;
            if (rr <= 5.0)
            {
                rr -= 1.6;
                value = (((((((7.7454501427834140764e-4 * rr + 0.0227238449892691845833) * rr + 0.24178072517745061177) * rr
                        + 1.27045825245236838258) * rr + 3.64784832476320460504) * rr + 5.7694972214606914055) * rr
                        + 4.6303378461565452959) * rr + 1.42343711074968357734)
                    / (((((((1.05075007164441684324e-9 * rr + 5.475938084995344946e-4) * rr + 0.0151986665636164571966) * rr
                        + 0.14810397642748007459) * rr + 0.68976733498510000455) * rr + 1.6763848301838038494) * rr
                        + 2.05319162663775882187) * rr + 1.0);
            }
            else
            {
                rr -= 5.0;
                value = (((((((2.01033439929228813265e-7 * rr + 2.71155556874348757815e-5) * rr + 0.0012426609473880784386) * rr
                        + 0.026532189526576123093) * rr + 0.29656057182850489123) * rr + 1.7848265399172913358) * rr
                        + 5.4637849111641143699) * rr + 6.6579046435011037772)
                    / (((((((2.04426310338993978564e-15 * rr + 1.4215117583164458887e-7) * rr + 1.8463183175100546818e-5) * rr
                        + 7.868691311456132591e-4) * rr + 0.0148753612908506148525) * rr + 0.13692988092273580531) * rr
                        + 0.59983220655588793769) * rr + 1.0);
            }
            return q < 0.0 ? -value : value;
        }

        /// <summary>
        /// A uniform draw in [0, 1) as a standard normal by inverse CDF. The draw
        /// is moved half a grid step up (the stream's 2^-53 grid) so it lies in
        /// the open interval and 0 never maps to minus infinity.
        /// </summary>
        public static double UniformToNormal(double u) => NormalQuantile(u + HalfUlpOfUnitInterval);

        /// <summary>
        /// The transform a sea state is drawn in (F2-06): ln h up to 1 m and the
        /// Box-Cox power (h^λ − 1)/λ above it. The two pieces meet at 1 m with
        /// equal value (0) and slope (1), so a normal in this space is a
        /// lognormal sea below 1 m (positive, with no floor and no pile-up at
        /// zero) and above 1 m a sea whose upper tail thins as λ rises from 0
        /// (lognormal) toward 1 (normal). Monotone; −∞ at h = 0.
        /// </summary>
        public static double UpperBoxCox(double h, double lambda)
        {
            if (h <= 0.0) return double.NegativeInfinity;
            if (h <= 1.0 || lambda == 0.0) return Math.Log(h);
            return (Math.Pow(h, lambda) - 1.0) / lambda;
        }

        /// <summary>
        /// Slope of <see cref="UpperBoxCox"/> per ln unit at <paramref name="h"/>
        /// (dT/d ln h): 1 up to 1 m, h^λ above. Converts a spread in ln units
        /// at a median into the transform's units.
        /// </summary>
        public static double UpperBoxCoxSlopePerLn(double h, double lambda)
            => h <= 1.0 || lambda == 0.0 ? 1.0 : Math.Pow(h, lambda);

        /// <summary>Inverse of <see cref="UpperBoxCox"/>: e^y at and below 0, (1 + λy)^(1/λ) above.</summary>
        public static double UpperBoxCoxInverse(double y, double lambda)
        {
            if (y <= 0.0 || lambda == 0.0) return Math.Exp(y);
            return Math.Pow(1.0 + lambda * y, 1.0 / lambda);
        }

        /// <summary>
        /// Gauss-Hermite rules for a standard normal weight (∫ f(ζ) φ(ζ) dζ ≈
        /// Σ wᵢ f(ζᵢ)), by the number of other swells they serve: 12 nodes for
        /// one dimension of a pair, 12 for two, 7 for three. Computed once by
        /// Newton's method on the Hermite recurrence (Press et al., Numerical
        /// Recipes, 3rd ed. 2007, §4.6 "gauher"), rescaled from the e^(−x²)
        /// weight.
        /// </summary>
        internal static readonly (double[] Nodes, double[] Weights)[] GaussHermiteRules =
        {
            GaussHermite(12), GaussHermite(12), GaussHermite(7),
        };

        /// <summary>The n-point Gauss-Hermite rule for the standard normal density.</summary>
        internal static (double[] Nodes, double[] Weights) GaussHermite(int n)
        {
            var x = new double[n];
            var w = new double[n];
            const double PiM4 = 0.7511255444649425; // π^(−1/4)
            int m = (n + 1) / 2;
            double z = 0.0;
            for (int i = 0; i < m; i++)
            {
                z = i switch
                {
                    0 => Math.Sqrt(2.0 * n + 1.0) - 1.85575 * Math.Pow(2.0 * n + 1.0, -0.16667),
                    1 => z - 1.14 * Math.Pow(n, 0.426) / z,
                    2 => 1.86 * z - 0.86 * x[0],
                    3 => 1.91 * z - 0.91 * x[1],
                    _ => 2.0 * z - x[i - 2],
                };
                double pp = 0.0;
                for (int it = 0; it < 100; it++)
                {
                    double p1 = PiM4, p2 = 0.0;
                    for (int j = 0; j < n; j++)
                    {
                        double p3 = p2;
                        p2 = p1;
                        p1 = z * Math.Sqrt(2.0 / (j + 1)) * p2 - Math.Sqrt((double)j / (j + 1)) * p3;
                    }
                    pp = Math.Sqrt(2.0 * n) * p2;
                    double z1 = z;
                    z = z1 - p1 / pp;
                    if (Math.Abs(z - z1) <= 1e-15) break;
                }
                x[i] = z;
                x[n - 1 - i] = -z;
                w[i] = w[n - 1 - i] = 2.0 / (pp * pp);
            }
            // e^(−x²) weight → standard normal: ζ = √2 x, weight / √π.
            var nodes = new double[n];
            var weights = new double[n];
            for (int i = 0; i < n; i++)
            {
                nodes[i] = Math.Sqrt(2.0) * x[n - 1 - i];
                weights[i] = w[n - 1 - i] / Math.Sqrt(Math.PI);
            }
            return (nodes, weights);
        }

        /// <summary>Nodes of the 8-point Gauss-Legendre rule on [−1, 1] (exact to degree 15).</summary>
        internal static readonly double[] GaussLegendreNodes =
        {
            -0.9602898564975363, -0.7966664774136267, -0.5255324099163290, -0.1834346424956498,
            0.1834346424956498, 0.5255324099163290, 0.7966664774136267, 0.9602898564975363,
        };

        /// <summary>Weights of the 8-point Gauss-Legendre rule on [−1, 1].</summary>
        internal static readonly double[] GaussLegendreWeights =
        {
            0.1012285362903763, 0.2223810344533745, 0.3137066458778873, 0.3626837833783620,
            0.3626837833783620, 0.3137066458778873, 0.2223810344533745, 0.1012285362903763,
        };
    }
}
