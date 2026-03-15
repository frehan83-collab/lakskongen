// =============================================================================
// QualityGrade.cs - Fish Quality Grading System
// =============================================================================
// Maps fish attributes (weight, lice history, harvest method, freshness)
// to quality grades with corresponding price multipliers.
// Based on real Norwegian salmon grading: Superior, Ordinary, Inferior (NOS 10.01)
// =============================================================================

using System;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Extended quality grading aligned with Norwegian Standard (NS 9401/9417).
    /// </summary>
    public enum FishQualityGrade
    {
        /// <summary>Sashimi-grade: >5kg, zero lice, perfect handling. +40% premium.</summary>
        SashimiGrade,
        /// <summary>Superior: >4kg, low lice count, good condition. +15% premium.</summary>
        Superior,
        /// <summary>Standard/Ordinary: meets baseline specs. No premium.</summary>
        Standard,
        /// <summary>Production grade: minor defects, smaller fish. -15% discount.</summary>
        Production,
        /// <summary>Inferior: damaged, high lice scarring, undersized. -30% discount.</summary>
        Inferior
    }

    /// <summary>
    /// Product form affects base price. Processing adds value but reduces yield.
    /// </summary>
    public enum ProductForm
    {
        /// <summary>Whole round fish. Baseline price.</summary>
        WholeRound,
        /// <summary>Head-on gutted. +5% vs whole round.</summary>
        HOG,
        /// <summary>Fresh fillets. +60% vs whole round (40% yield).</summary>
        FreshFillet,
        /// <summary>Smoked salmon. +120% vs whole round (35% yield).</summary>
        Smoked,
        /// <summary>Frozen whole/portions. -5% vs fresh whole round.</summary>
        Frozen
    }

    /// <summary>
    /// Input parameters for grading a fish batch.
    /// </summary>
    public record FishBatchAttributes
    {
        /// <summary>Average weight in kg.</summary>
        public double AverageWeightKg { get; init; } = 4.5;
        /// <summary>Average lice count per fish over grow-out cycle.</summary>
        public double AverageLiceCount { get; init; } = 0.1;
        /// <summary>Days since harvest (freshness decay).</summary>
        public int DaysSinceHarvest { get; init; } = 0;
        /// <summary>Whether fish was mechanically or manually harvested.</summary>
        public bool ManualHarvest { get; init; } = false;
        /// <summary>Percentage of fish with visible wounds/scarring (0-1).</summary>
        public double WoundRate { get; init; } = 0.02;
        /// <summary>Whether the farm has organic certification.</summary>
        public bool OrganicCertified { get; init; } = false;
        /// <summary>Whether the farm has ASC certification.</summary>
        public bool ASCCertified { get; init; } = false;
        /// <summary>Feed conversion ratio (lower = better quality feed = better flesh).</summary>
        public double FCR { get; init; } = 1.15;
    }

    /// <summary>
    /// Result of grading a fish batch, including all applicable price multipliers.
    /// </summary>
    public record GradingResult
    {
        public FishQualityGrade Grade { get; init; }
        public ProductForm Form { get; init; } = ProductForm.WholeRound;
        /// <summary>Quality grade multiplier (e.g., 1.40 for sashimi).</summary>
        public decimal GradeMultiplier { get; init; } = 1.0m;
        /// <summary>Product form multiplier (e.g., 1.60 for fillets).</summary>
        public decimal FormMultiplier { get; init; } = 1.0m;
        /// <summary>Freshness decay multiplier (1.0 at harvest, decays over days).</summary>
        public decimal FreshnessMultiplier { get; init; } = 1.0m;
        /// <summary>Certification bonus multiplier (organic +15%, ASC +10%).</summary>
        public decimal CertificationMultiplier { get; init; } = 1.0m;
        /// <summary>Combined effective multiplier applied to base spot price.</summary>
        public decimal EffectiveMultiplier => GradeMultiplier * FormMultiplier * FreshnessMultiplier * CertificationMultiplier;
        /// <summary>Yield factor for product form (1.0 = no loss, 0.40 = 60% lost in filleting).</summary>
        public decimal YieldFactor { get; init; } = 1.0m;
    }

    /// <summary>
    /// Grades fish batches and calculates price multipliers based on Norwegian salmon industry standards.
    /// </summary>
    public static class QualityGrader
    {
        // ─── Grade multipliers (applied to base NOK/kg spot price) ───
        private static readonly decimal[] GradeMultipliers =
        {
            1.40m,  // SashimiGrade: premium Japanese/Korean market
            1.15m,  // Superior: EU premium retailers (Waitrose, etc.)
            1.00m,  // Standard: baseline
            0.85m,  // Production: processing/value-added only
            0.70m   // Inferior: fishmeal/pet food
        };

        // ─── Product form multipliers (relative to whole round) ───
        private static readonly decimal[] FormMultipliers =
        {
            1.00m,  // WholeRound
            1.05m,  // HOG (head-on gutted)
            1.60m,  // FreshFillet
            2.20m,  // Smoked
            0.95m   // Frozen
        };

        // ─── Product form yield factors ───
        private static readonly decimal[] FormYields =
        {
            0.98m,  // WholeRound (minimal loss)
            0.88m,  // HOG (head + guts removed)
            0.40m,  // FreshFillet
            0.35m,  // Smoked
            0.95m   // Frozen
        };

        /// <summary>Freshness decay: -5% per day after harvest, floor at 50% of original value.</summary>
        private const decimal FreshnessDecayPerDay = 0.05m;
        private const decimal MinFreshnessFactor = 0.50m;

        /// <summary>
        /// Grade a fish batch based on its attributes.
        /// </summary>
        public static FishQualityGrade GradeBatch(FishBatchAttributes batch)
        {
            // Sashimi grade: strict requirements
            if (batch.AverageWeightKg >= 5.0 &&
                batch.AverageLiceCount < 0.05 &&
                batch.WoundRate < 0.01 &&
                batch.ManualHarvest &&
                batch.FCR <= 1.10 &&
                batch.DaysSinceHarvest <= 1)
            {
                return FishQualityGrade.SashimiGrade;
            }

            // Superior: good quality
            if (batch.AverageWeightKg >= 4.0 &&
                batch.AverageLiceCount < 0.2 &&
                batch.WoundRate < 0.03 &&
                batch.DaysSinceHarvest <= 3)
            {
                return FishQualityGrade.Superior;
            }

            // Inferior: poor quality
            if (batch.AverageWeightKg < 2.5 ||
                batch.AverageLiceCount > 1.0 ||
                batch.WoundRate > 0.15 ||
                batch.DaysSinceHarvest > 10)
            {
                return FishQualityGrade.Inferior;
            }

            // Production: below standard but not terrible
            if (batch.AverageWeightKg < 3.5 ||
                batch.AverageLiceCount > 0.5 ||
                batch.WoundRate > 0.08 ||
                batch.DaysSinceHarvest > 7)
            {
                return FishQualityGrade.Production;
            }

            return FishQualityGrade.Standard;
        }

        /// <summary>
        /// Full grading with all multipliers computed.
        /// </summary>
        public static GradingResult GradeFull(FishBatchAttributes batch, ProductForm form = ProductForm.WholeRound)
        {
            var grade = GradeBatch(batch);
            var gradeMultiplier = GradeMultipliers[(int)grade];
            var formMultiplier = FormMultipliers[(int)form];
            var yieldFactor = FormYields[(int)form];

            // Freshness decay
            var freshnessDecay = Math.Max(
                MinFreshnessFactor,
                1.0m - (batch.DaysSinceHarvest * FreshnessDecayPerDay));

            // Certification bonuses (additive, then combined)
            var certBonus = 1.0m;
            if (batch.OrganicCertified) certBonus += 0.15m;
            if (batch.ASCCertified) certBonus += 0.10m;

            return new GradingResult
            {
                Grade = grade,
                Form = form,
                GradeMultiplier = gradeMultiplier,
                FormMultiplier = formMultiplier,
                FreshnessMultiplier = freshnessDecay,
                CertificationMultiplier = certBonus,
                YieldFactor = yieldFactor
            };
        }

        /// <summary>
        /// Calculate effective price per kg for a graded batch.
        /// </summary>
        public static decimal CalculateEffectivePrice(decimal baseSpotPrice, GradingResult grading)
        {
            return baseSpotPrice * grading.EffectiveMultiplier;
        }

        /// <summary>
        /// Calculate revenue for selling a batch, accounting for yield loss.
        /// </summary>
        public static decimal CalculateBatchRevenue(decimal baseSpotPrice, GradingResult grading, int inputKg)
        {
            var effectivePrice = CalculateEffectivePrice(baseSpotPrice, grading);
            var outputKg = (int)(inputKg * grading.YieldFactor);
            return effectivePrice * outputKg;
        }
    }
}
