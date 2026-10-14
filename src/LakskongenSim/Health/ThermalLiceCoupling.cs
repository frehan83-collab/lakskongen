using System;

namespace LakskongenSim.Health
{
    /// <summary>
    /// Couples water temperature and degree-day accumulation to sea-lice development.
    /// Warmer water shortens development time, so a given warming trend accelerates
    /// lice population growth (NVI Fish Health Report 2025).
    /// All functions are pure and deterministic.
    /// </summary>
    public static class ThermalLiceCoupling
    {
        /// <summary>Lower thermal threshold for lice development (°C).</summary>
        public const double BaseDevelopmentTempC = 5.0;

        /// <summary>Reference temperature at which the multiplier equals 1.0 (°C).</summary>
        public const double ReferenceTempC = 10.0;

        /// <summary>Upper clamp on temperature used for rate calculations (°C).</summary>
        public const double MaxTempC = 25.0;

        /// <summary>
        /// Q10-style development multiplier relative to the reference temperature.
        /// Doubles per +10 °C above reference, halves per -10 °C.
        /// </summary>
        public static double DevelopmentMultiplier(double temperatureC, double referenceTempC = ReferenceTempC)
        {
            double t = Math.Clamp(temperatureC, 0.0, MaxTempC);
            return Math.Pow(2.0, (t - referenceTempC) / 10.0);
        }

        /// <summary>
        /// Effective degree-day accumulation rate above the base development threshold.
        /// Zero at or below the threshold; linear above it.
        /// </summary>
        public static double DegreeDayRate(double temperatureC, double baseTempC = BaseDevelopmentTempC)
        {
            return Math.Max(0.0, temperatureC - baseTempC);
        }

        /// <summary>
        /// Accumulated degree-days over a number of days at a constant temperature.
        /// </summary>
        public static double DegreeDays(double temperatureC, double days, double baseTempC = BaseDevelopmentTempC)
        {
            return DegreeDayRate(temperatureC, baseTempC) * Math.Max(0.0, days);
        }

        /// <summary>
        /// Days required to accumulate <paramref name="requiredDegreeDays"/> at a constant temperature.
        /// Returns <see cref="double.PositiveInfinity"/> below the development threshold.
        /// </summary>
        public static double DaysToAccumulate(double requiredDegreeDays, double temperatureC, double baseTempC = BaseDevelopmentTempC)
        {
            double rate = DegreeDayRate(temperatureC, baseTempC);
            if (rate <= 1e-9)
                return double.PositiveInfinity;
            return Math.Max(0.0, requiredDegreeDays) / rate;
        }

        /// <summary>
        /// Baseline temperature adjusted by a linear warming trend of
        /// <paramref name="warmingPerYearC"/> °C/year at a given simulation day.
        /// </summary>
        public static double EffectiveTemperature(double baselineTempC, double warmingPerYearC, int day)
        {
            double years = LakskongenSim.Core.SimCalendar.YearFraction(Math.Max(0, day));
            return baselineTempC + warmingPerYearC * years;
        }

        /// <summary>
        /// Development multiplier including an applied warming trend at a given day.
        /// </summary>
        public static double DevelopmentMultiplierWithWarming(
            double baselineTempC,
            double warmingPerYearC,
            int day,
            double referenceTempC = ReferenceTempC)
        {
            return DevelopmentMultiplier(EffectiveTemperature(baselineTempC, warmingPerYearC, day), referenceTempC);
        }

        /// <summary>
        /// Additive, non-mutating convenience: push the effective (warming-adjusted)
        /// temperature into an existing <see cref="SeaLiceModel"/> using its stable
        /// public API. Salinity, currents and density are preserved.
        /// </summary>
        public static void SyncSeaLiceTemperature(
            SeaLiceModel model,
            double baselineTempC,
            double warmingPerYearC,
            int day)
        {
            if (model is null)
                throw new ArgumentNullException(nameof(model));

            double effective = EffectiveTemperature(baselineTempC, warmingPerYearC, day);
            model.SetEnvironment(effective, model.SalinityPSU, model.CurrentSpeedMs, model.StockingDensityKgM3);
        }
    }
}