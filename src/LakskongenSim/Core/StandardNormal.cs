using System;

namespace LakskongenSim.Core;

/// <summary>
/// The standard normal quantile for inverse-CDF draws (F2-04: a construction
/// phase's lognormal cost and duration factors). Pure; it draws nothing.
/// </summary>
public static class StandardNormal
{
    /// <summary>z of the 90th percentile.</summary>
    public const double Z90 = 1.2815515655446004;

    // Acklam's rational approximation (relative error below 1.15e-9 over (0, 1)).
    private static readonly double[] A = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
    private static readonly double[] B = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
    private static readonly double[] C = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
    private static readonly double[] D = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
    private const double Low = 0.02425;

    /// <summary>The z with Φ(z) = <paramref name="p"/>; p must lie strictly between 0 and 1.</summary>
    public static double Quantile(double p)
    {
        if (!(p > 0.0 && p < 1.0))
            throw new ArgumentOutOfRangeException(nameof(p), p, "A normal quantile needs 0 < p < 1");
        if (p < Low)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(p));
            return (((((C[0] * q + C[1]) * q + C[2]) * q + C[3]) * q + C[4]) * q + C[5])
                   / ((((D[0] * q + D[1]) * q + D[2]) * q + D[3]) * q + 1.0);
        }
        if (p > 1.0 - Low)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
            return -(((((C[0] * q + C[1]) * q + C[2]) * q + C[3]) * q + C[4]) * q + C[5])
                   / ((((D[0] * q + D[1]) * q + D[2]) * q + D[3]) * q + 1.0);
        }
        double r = p - 0.5;
        double s = r * r;
        return (((((A[0] * s + A[1]) * s + A[2]) * s + A[3]) * s + A[4]) * s + A[5]) * r
               / (((((B[0] * s + B[1]) * s + B[2]) * s + B[3]) * s + B[4]) * s + 1.0);
    }

    /// <summary>
    /// A lognormal set by its median and 90th percentile (both positive, p90
    /// above the median), read at quantile <paramref name="p"/>.
    /// </summary>
    public static double LognormalQuantile(double median, double p90, double p)
    {
        if (!(median > 0.0) || !(p90 > median))
            throw new ArgumentOutOfRangeException(nameof(p90), p90, "A lognormal needs 0 < median < p90");
        double sigma = (Math.Log(p90) - Math.Log(median)) / Z90;
        return median * Math.Exp(sigma * Quantile(p));
    }
}
