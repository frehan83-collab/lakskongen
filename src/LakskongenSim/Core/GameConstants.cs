using System;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Global game constants and default configuration values.
    /// </summary>
    public static class GameConstants
    {
        /// <summary>Currency symbol used in the simulation (NOK).</summary>
        public const string CurrencySymbol = "NOK";

        /// <summary>Default number of days per simulation tick.</summary>
        public const int DefaultTickDays = 1;

        /// <summary>Baseline mortality rate per day (approx 0.15 per year).</summary>
        public const double BaselineMortalityPerDay = 0.0004; // calibrated default (approx 0.146 / year)

        /// <summary>Baseline growth rate per day (percentage of weight).</summary>
        public const double BaselineGrowthPerDay = 0.0025; // calibrated default (approx 0.25%/day)

        // von Bertalanffy growth parameters (weight-based, grams/days)
        /// <summary>Asymptotic/maximum expected weight (grams).</summary>
        public const double VB_Linf_g = 4000.0; // default asymptotic weight (g) for farmed Atlantic salmon

        /// <summary>Growth coefficient (per day).</summary>
        public const double VB_k_per_day = 0.0025; // typical value used in literature (approx 0.9 / year)

        /// <summary>von Bertalanffy t0 (days).</summary>
        public const double VB_t0_days = -30.0;

        /// <summary>Typical feed conversion ratio (kg feed / kg growth).</summary>
        public const double TypicalFCR = 1.15; // industry average (Seafish / GSI)
    }
}