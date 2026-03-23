using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Monte Carlo price forecasting engine for salmon spot markets.
    /// Uses Geometric Brownian Motion with Poisson jump-diffusion (Merton model):
    /// dP = μPdt + σPdW + JdN
    /// Calibrated to Nasdaq Salmon Index historical behavior.
    /// </summary>
    public class MonteCarloPriceEngine
    {
        // --- Moddable constants ---
        /// <summary>Annual drift rate (baseline price trend).</summary>
        public static readonly double AnnualDrift = 0.02;
        /// <summary>Annual volatility (Norwegian salmon market ~25%).</summary>
        public static readonly double AnnualVolatility = 0.25;
        /// <summary>Mean jump size (log-normal, negative = price drop).</summary>
        public static readonly double JumpMean = -0.10;
        /// <summary>Jump size standard deviation.</summary>
        public static readonly double JumpStd = 0.15;
        /// <summary>Poisson intensity: expected jumps per year.</summary>
        public static readonly double JumpIntensity = 2.0;

        private readonly Random _rng;

        /// <summary>Create engine with optional seed for reproducibility.</summary>
        public MonteCarloPriceEngine(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        }

        /// <summary>
        /// Get seasonal price multiplier for a given day of year.
        /// Based on Norwegian salmon market patterns.
        /// </summary>
        /// <param name="dayOfYear">1-365</param>
        /// <returns>Multiplier around 1.0 (e.g. 1.15 for Christmas peak)</returns>
        public static double GetSeasonalMultiplier(int dayOfYear)
        {
            // Christmas peak (Dec 15-31): +15%
            if (dayOfYear >= 349) return 1.15;
            // Pre-Christmas ramp (Dec 1-14): +10%
            if (dayOfYear >= 335) return 1.10;
            // Easter window (~day 90-105): +10%
            if (dayOfYear >= 90 && dayOfYear <= 105) return 1.10;
            // Chinese New Year (~day 25-40): +5%
            if (dayOfYear >= 25 && dayOfYear <= 40) return 1.05;
            // Summer lull (Jun 15 - Aug 15): -8%
            if (dayOfYear >= 166 && dayOfYear <= 227) return 0.92;
            // September recovery: +3%
            if (dayOfYear >= 244 && dayOfYear <= 273) return 1.03;
            return 1.0;
        }

        /// <summary>
        /// Generate a single price path over N days using GBM + jumps.
        /// </summary>
        /// <param name="startPrice">Starting spot price (NOK/kg)</param>
        /// <param name="days">Number of days to simulate</param>
        /// <returns>Array of daily prices (length = days + 1, index 0 = start)</returns>
        public double[] GeneratePricePath(double startPrice, int days)
        {
            double[] path = new double[days + 1];
            path[0] = startPrice;

            double dt = 1.0 / 365.0;
            double dailyDrift = AnnualDrift * dt;
            double dailyVol = AnnualVolatility * Math.Sqrt(dt);
            double jumpProb = JumpIntensity * dt;

            for (int d = 0; d < days; d++)
            {
                // Standard GBM diffusion
                double z = NormalSample();
                double diffusion = (dailyDrift - 0.5 * AnnualVolatility * AnnualVolatility * dt) + dailyVol * z;

                // Poisson jump
                double jump = 0.0;
                if (_rng.NextDouble() < jumpProb)
                {
                    jump = JumpMean + JumpStd * NormalSample();
                }

                // Seasonal overlay
                int dayOfYear = ((d + 1) % 365) + 1;
                double seasonal = GetSeasonalMultiplier(dayOfYear);

                double newPrice = path[d] * Math.Exp(diffusion + jump) * seasonal / GetSeasonalMultiplier(dayOfYear > 1 ? dayOfYear - 1 : 365);
                path[d + 1] = Math.Max(newPrice, 1.0); // Floor at 1 NOK/kg
            }

            return path;
        }

        /// <summary>
        /// Run Monte Carlo simulation and return statistics.
        /// </summary>
        /// <param name="currentPrice">Current spot price (NOK/kg)</param>
        /// <param name="daysForward">Forecast horizon in days</param>
        /// <param name="numSimulations">Number of MC paths (default 1000)</param>
        /// <returns>(meanPrice, lowerBound5pct, upperBound95pct) at horizon</returns>
        public (double MeanPrice, double Lower5Pct, double Upper95Pct) SimulatePrice(
            double currentPrice, int daysForward, int numSimulations = 1000)
        {
            double[] terminalPrices = new double[numSimulations];

            for (int i = 0; i < numSimulations; i++)
            {
                double[] path = GeneratePricePath(currentPrice, daysForward);
                terminalPrices[i] = path[daysForward];
            }

            Array.Sort(terminalPrices);
            double mean = terminalPrices.Average();
            double lower5 = terminalPrices[(int)(numSimulations * 0.05)];
            double upper95 = terminalPrices[(int)(numSimulations * 0.95)];

            return (mean, lower5, upper95);
        }

        /// <summary>
        /// Apply a supply shock to current price.
        /// Supply reduction increases price via inverse elasticity.
        /// Price elasticity of demand for salmon ≈ -0.3 (inelastic).
        /// </summary>
        /// <param name="currentPrice">Current spot price</param>
        /// <param name="supplyReductionPercent">Supply reduction 0-100%</param>
        /// <returns>New price after shock</returns>
        public static double ApplySupplyShock(double currentPrice, double supplyReductionPercent)
        {
            double elasticity = 0.3; // Absolute value of price elasticity of demand
            // Supply reduction → price increase: ΔP/P = (1/elasticity) × (ΔQ/Q)
            double priceChangeRatio = (1.0 / elasticity) * (supplyReductionPercent / 100.0);
            return currentPrice * (1.0 + priceChangeRatio);
        }

        /// <summary>Box-Muller normal sample N(0,1).</summary>
        private double NormalSample()
        {
            double u1 = 1.0 - _rng.NextDouble(); // Avoid log(0)
            double u2 = _rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
