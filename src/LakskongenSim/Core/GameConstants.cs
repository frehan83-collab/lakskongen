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

        /// <summary>Baseline mortality rate per day (approx 6-10% per year for Norwegian farmed salmon).</summary>
        public const double BaselineMortalityPerDay = 0.00018; // Calibrated to achieve 70%+ survival over 905-day cycle with stage multipliers applied

        /// <summary>Baseline growth rate per day (percentage of weight).</summary>
                public const double BaselineGrowthPerDay = 0.006; // Calibrated to achieve 4-6kg in 18 months sea phase // calibrated default (approx 0.25%/day)

        // von Bertalanffy growth parameters (weight-based, grams/days)
        /// <summary>Asymptotic/maximum expected weight (grams). Norwegian harvest weight 4-6 kg, asymptote higher.</summary>
        public const double VB_Linf_g = 6500.0; // asymptotic weight (g) — Norwegian farmed salmon can exceed 6 kg

        /// <summary>Growth coefficient (per day). Calibrated to reach ~5 kg in 14-18 months at sea (8-14°C).</summary>
        public const double VB_k_per_day = 0.0030; // ~1.1/year — faster than wild due to selective breeding

        /// <summary>von Bertalanffy t0 (days). Offset for smolt stocking at ~80g.</summary>
        public const double VB_t0_days = -45.0; // accounts for hatchery growth phase

        /// <summary>Typical feed conversion ratio (kg feed / kg growth). Norwegian industry average.</summary>
        public const double TypicalFCR = 1.20; // industry average 1.1-1.3 (Kontali Analyse 2024)

        /// <summary>Production cost per kg (NOK). Norwegian industry average 2024.</summary>
        public const decimal ProductionCostPerKg = 42.0m; // includes feed, smolt, labor, delousing, mortality

        /// <summary>Feed cost as fraction of total production cost.</summary>
        public const double FeedCostFraction = 0.50; // feed represents ~50% of production costs

        /// <summary>Smolt cost per fish (NOK). Standard quality.</summary>
        public const decimal SmoltCostPerFish = 12.0m; // typical 10-15 NOK per smolt

        /// <summary>Delousing cost per treatment per pen (NOK).</summary>
        public const decimal DelousingCostPerTreatment = 250_000m; // mechanical treatment ~250K NOK

        /// <summary>Standard license MAB (tonnes).</summary>
        public const double StandardLicenseMAB = 780.0; // standard Norwegian license = 780 tonnes MAB

        /// <summary>License value (NOK). Recent auction prices.</summary>
        public const decimal LicenseValueNOK = 150_000_000m; // 150-200M NOK at recent auctions
    }
}