using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Data;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Dynamic feed system with realistic Feed Conversion Ratio (FCR) modeling.
    /// Norwegian industry FCR range: 1.1–1.3 (kg feed per kg fish growth).
    /// FCR affected by: temperature, fish size/stage, feed quality, feeding frequency,
    /// stocking density, and water quality.
    /// </summary>
    public class FeedSystem
    {
        // ─── Cost parameters ───
        /// <summary>
        /// Feed price per kg of feed (NOK/kg feed). Defaults to the calibrated
        /// <see cref="CalibrationRegistry"/> value so a standalone <see cref="FeedSystem"/>
        /// (tests, headless tools, code that bypasses <c>SimulationEngine</c>) cannot
        /// drift from the single source of truth. The engine also re-applies the
        /// registry value on load via <c>ApplyBalancingConfig</c>.
        /// </summary>
        public decimal FeedCostPerKg { get; set; } =
            CalibrationRegistry.Default.Feed.FeedPricePerKgFeedNOK;

        /// <summary>
        /// Effective feed cost per kg of fish produced (NOK/kg produced) = feed price
        /// × base FCR. Registry anchor is ~25.7 (22.3 × 1.15), per docs/REALISM_CHECKLIST.md.
        /// </summary>
        public decimal FeedCostPerKgProducedNOK => FeedCostPerKg * (decimal)BaseFCR;

        // ─── FCR parameters ───
        /// <summary>Base FCR, seeded from the calibrated registry (real biological ~1.15).</summary>
        public double BaseFCR { get; private set; } = CalibrationRegistry.Default.Feed.BaseFCR;
        /// <summary>Current dynamic FCR; reset to <see cref="BaseFCR"/> until conditions are applied.</summary>
        public double CurrentFCR { get; private set; } = CalibrationRegistry.Default.Feed.BaseFCR;

        /// <summary>
        /// Apply the calibrated base FCR from <see cref="Calibration.CalibrationRegistry"/>
        /// (Phase A). Keeps FeedSystem's FCR anchor from drifting from the single source
        /// of truth without exposing a public mutable property.
        /// </summary>
        public void SetBaseFCR(double baseFcr)
        {
            BaseFCR = Math.Clamp(baseFcr, 0.8, 2.5);
            CurrentFCR = BaseFCR;
        }

        // Temperature-FCR relationship (Norwegian data: optimal 11-13°C)
        private const double OptimalFCRTemp = 12.0;
        private const double FCRTempSensitivity = 0.018; // ↓ from 0.02 (less sensitive)

        // ─── Feeding strategy ───
        public double FeedingRatePercent { get; set; } = 1.5; // % of bodyweight per day (adjustable by player)
        public int MealsPerDay { get; set; } = 3; // More meals = better FCR but higher labour cost

        // ─── Sustainability ───
        public float SustainabilityRating { get; private set; } = 0.7f;
        public Dictionary<string, float> IngredientSources { get; private set; } = new();
        public bool UsesInsectMeal { get; set; } = false;
        public bool UsesAlgae { get; set; } = false;

        // ─── Tracking ───
        public double TotalFeedUsedKg { get; private set; }
        public double TotalGrowthKg { get; private set; }
        public double LifetimeFCR => TotalGrowthKg > 0 ? TotalFeedUsedKg / TotalGrowthKg : BaseFCR;

        /// <summary>Last computed appetite 0-1 (Phase 2.1). 1 = full ration.</summary>
        public double LastAppetite01 { get; private set; } = 1.0;

        /// <summary>Recipe FCR multiplier 0.90-1.0 (Phase 4.4, set by recipe).</summary>
        public double RecipeFCRMult { get; set; } = 1.0;

        // ─── Precision feeding (feed spreading quality) ───
        // Poor spreading means pellets land unevenly: dominant fish gorge while
        // subordinate fish miss out. Empirically this raises FCR, produces a more
        // variable size distribution, and increases pellet waste on the seabed.
        // Feed is 50-65% of production cost, so spreading quality is high-leverage.

        /// <summary>FCR penalty at zero spreading quality (poor = +20%).</summary>
        public const double MaxPrecisionFcrPenalty = 0.20;

        /// <summary>Pellet waste fraction at perfect spreading (5%).</summary>
        public const double BaseWasteFraction = 0.05;

        /// <summary>Additional pellet waste fraction at zero spreading quality (+15%).</summary>
        public const double MaxExtraWasteFraction = 0.15;

        /// <summary>Size variance (CV) at perfect spreading (0.10).</summary>
        public const double BaseSizeVarianceCv = 0.10;

        /// <summary>Additional size variance (CV) at zero spreading quality (+0.25).</summary>
        public const double MaxExtraSizeVarianceCv = 0.25;

        /// <summary>
        /// FCR multiplier from feed spreading quality. 1.0 at perfect spread,
        /// 1.20 at poorest spread. Pure and monotonic decreasing in quality.
        /// </summary>
        public static double PrecisionFCRMultiplier(double spreadQuality01)
        {
            double q = Math.Clamp(spreadQuality01, 0.0, 1.0);
            return 1.0 + MaxPrecisionFcrPenalty * (1.0 - q);
        }

        /// <summary>Feed-waste fraction (uneaten pellets) as a function of spreading quality. Monotonic.</summary>
        public static double CalculateFeedWasteFraction(double spreadQuality01)
        {
            double q = Math.Clamp(spreadQuality01, 0.0, 1.0);
            return BaseWasteFraction + MaxExtraWasteFraction * (1.0 - q);
        }

        /// <summary>Cohort size variance (coefficient of variation) vs spreading quality. Monotonic.</summary>
        public static double CalculateSizeVarianceCv(double spreadQuality01)
        {
            double q = Math.Clamp(spreadQuality01, 0.0, 1.0);
            return BaseSizeVarianceCv + MaxExtraSizeVarianceCv * (1.0 - q);
        }

        /// <summary>
        /// Appetite from temperature + oxygen (Phase 2.1): full ration at 8-14°C
        /// and good O2, tapering to 0.3 in cold/heat or hypoxia. Fish that won't
        /// eat shouldn't be fed — saves cost and seabed load.
        /// </summary>
        public static double CalculateAppetite(double temperatureC, double doModifier01)
        {
            double tempApp = temperatureC switch
            {
                < 2.0 => 0.3,
                < 6.0 => 0.6 + (temperatureC - 2.0) * 0.1,   // 0.6→1.0
                <= 14.0 => 1.0,
                <= 18.0 => 1.0 - (temperatureC - 14.0) * 0.1, // 1.0→0.6
                _ => 0.5
            };
            return Math.Clamp(tempApp * (0.3 + 0.7 * doModifier01), 0.3, 1.0);
        }

        // ─── Events ───
        public event Action<decimal>? OnFeedCostIncurred;
        public event Action<double>? OnFCRChanged;

        public FeedSystem()
        {
            IngredientSources = new Dictionary<string, float>
            {
                ["FishMeal"] = 0.30f,
                ["PlantProtein"] = 0.40f,
                ["Byproduct"] = 0.25f,
                ["Additives"] = 0.05f
            };
            UpdateSustainabilityRating();
        }

        /// <summary>
        /// Calculate dynamic FCR based on current conditions.
        /// Returns kg feed required per kg of fish growth.
        /// </summary>
        public double CalculateFCR(double temperatureC, LifeStage stage, double stockingDensityKgM3,
            double waterQualityPenalty = 1.0)
        {
            double fcr = BaseFCR;

            // Temperature effect: FCR worsens away from optimal (11-13°C)
            double tempDelta = Math.Abs(temperatureC - OptimalFCRTemp);
            fcr += tempDelta * FCRTempSensitivity;

            // Stage effect: smaller fish have higher FCR
            double stageModifier = stage switch
            {
                LifeStage.Fry => 1.4,      // Poor conversion in fry
                LifeStage.Parr => 1.2,     // Improving
                LifeStage.Smolt => 1.1,    // Smoltification stress
                LifeStage.Seawater => 1.0, // Optimal
                LifeStage.Harvest => 1.05, // Slightly higher near harvest
                _ => 1.0
            };
            fcr *= stageModifier;

            // Feed quality effect: better ingredients = lower FCR
            double qualityBonus = CalculateFeedQualityBonus();
            fcr *= (1.0 - qualityBonus);

            // Feeding frequency: more meals = better utilisation (diminishing returns)
            double mealBonus = MealsPerDay switch
            {
                1 => 1.10,   // Single meal = 10% worse
                2 => 1.03,   // Two meals = 3% worse
                3 => 1.00,   // Three meals = baseline
                >= 4 => 0.98, // 4+ meals = 2% better (but higher cost)
                _ => 1.0
            };
            fcr *= mealBonus;

            // Stocking density: overcrowding worsens FCR (stress, competition)
            if (stockingDensityKgM3 > 25.0) // Norwegian limit ~25 kg/m³
            {
                double overcrowdingPenalty = 1.0 + (stockingDensityKgM3 - 25.0) * 0.01;
                fcr *= overcrowdingPenalty;
            }

            // Water quality: poor water → worse FCR
            fcr *= (2.0 - waterQualityPenalty); // penalty=1.0 → no effect; penalty=0.5 → FCR×1.5

            // Recipe: better ingredients convert better (Phase 4.4).
            fcr *= RecipeFCRMult;

            // Clamp to realistic range
            CurrentFCR = Math.Clamp(fcr, 0.9, 2.5);
            OnFCRChanged?.Invoke(CurrentFCR);

            return CurrentFCR;
        }

        /// <summary>
        /// Apply feed to population. Returns total feed used (kg).
        /// Also calculates waste output for water chemistry integration.
        /// </summary>
        /// <param name="pop">Population to feed.</param>
        /// <param name="days">Number of days to feed over.</param>
        /// <param name="temperatureC">Water temperature in °C.</param>
        /// <param name="stockingDensityKgM3">Stocking density in kg/m³.</param>
        /// <param name="waterQualityPenalty">Water-quality multiplier (1.0 = no penalty).</param>
        /// <param name="spreadQuality01">Feed spreading precision 0 (poor) to 1 (perfect).
        /// Defaults to 1.0 so existing callers keep baseline behaviour.</param>
        public FeedResult Feed(Biology.FishPopulation pop, int days,
            double temperatureC = 12.0, double stockingDensityKgM3 = 20.0,
            double waterQualityPenalty = 1.0, double spreadQuality01 = 1.0)
        {
            double biomassKg = pop.Cohorts.Sum(c => c.Count * c.MeanWeightKg);
            if (biomassKg <= 0) return FeedResult.Empty;

            // Get dominant stage for FCR calculation
            var dominantStage = pop.Cohorts
                .OrderByDescending(c => c.Count)
                .Select(c => c.Stage)
                .FirstOrDefault();

            // Calculate dynamic FCR, then apply precision-feeding penalty.
            double fcr = CalculateFCR(temperatureC, dominantStage, stockingDensityKgM3, waterQualityPenalty);
            fcr = Math.Clamp(fcr * PrecisionFCRMultiplier(spreadQuality01), 0.9, 2.5);
            if (Math.Abs(fcr - CurrentFCR) > 1e-9)
            {
                CurrentFCR = fcr;
                OnFCRChanged?.Invoke(CurrentFCR);
            }

            // Precision-feeding side outputs: waste and size spread.
            double wasteFraction = CalculateFeedWasteFraction(spreadQuality01);
            double sizeVarianceCv = CalculateSizeVarianceCv(spreadQuality01);

            // Daily feed amount (% of bodyweight, adjustable by player),
            // scaled by appetite so uneaten feed doesn't wreck FCR/seabed.
            LastAppetite01 = CalculateAppetite(temperatureC, waterQualityPenalty);
            double dailyFeedKg = biomassKg * (FeedingRatePercent / 100.0) * LastAppetite01;
            double totalFeedKg = dailyFeedKg * days;

            // Expected growth from this feed
            double expectedGrowthKg = totalFeedKg / fcr;

            // Waste outputs (for water chemistry)
            // ~30% of feed N is retained, 70% excreted (Norwegian aquaculture data)
            double nitrogenWasteKg = totalFeedKg * 0.07 * 0.70; // 7% N in feed, 70% excreted
            double phosphorusWasteKg = totalFeedKg * 0.01 * 0.70; // 1% P in feed, 70% excreted

            // Uneaten pellets from imperfect spreading (seabed load).
            double feedWasteKg = totalFeedKg * wasteFraction;

            // Cost
            decimal totalCost = (decimal)totalFeedKg * FeedCostPerKg;

            // Track cumulative
            TotalFeedUsedKg += totalFeedKg;
            TotalGrowthKg += expectedGrowthKg;

            // Fire events
            OnFeedCostIncurred?.Invoke(totalCost);

            return new FeedResult
            {
                FeedUsedKg = totalFeedKg,
                ExpectedGrowthKg = expectedGrowthKg,
                ActualFCR = fcr,
                NitrogenWasteKg = nitrogenWasteKg,
                PhosphorusWasteKg = phosphorusWasteKg,
                CostNOK = totalCost,
                BiomassKg = biomassKg,
                FeedWasteFraction = wasteFraction,
                FeedWasteKg = feedWasteKg,
                SizeVarianceCv = sizeVarianceCv
            };
        }

        /// <summary>
        /// Calculate feed quality bonus from ingredient composition.
        /// Better ingredients → lower FCR (up to 5% improvement).
        /// </summary>
        private double CalculateFeedQualityBonus()
        {
            double bonus = 0.0;

            // High fishmeal content improves FCR
            if (IngredientSources.TryGetValue("FishMeal", out float fishMeal))
                bonus += fishMeal * 0.05; // Up to 5% at 100% fishmeal

            // Insect meal is nearly as good as fishmeal
            if (UsesInsectMeal)
                bonus += 0.02;

            // Algae provides omega-3 benefits
            if (UsesAlgae)
                bonus += 0.01;

            // Too much plant protein worsens FCR (gut inflammation in salmon)
            if (IngredientSources.TryGetValue("PlantProtein", out float plant) && plant > 0.5f)
                bonus -= (plant - 0.5f) * 0.04; // Penalty above 50%

            return Math.Clamp(bonus, -0.05, 0.05);
        }

        /// <summary>Update feed composition and recalculate sustainability rating.</summary>
        public void SetIngredientComposition(Dictionary<string, float> composition)
        {
            IngredientSources = composition;
            UpdateSustainabilityRating();
        }

        private void UpdateSustainabilityRating()
        {
            float rating = 0.0f;
            if (IngredientSources.TryGetValue("FishMeal", out float fishMeal))
                rating += fishMeal * 0.4f;
            if (IngredientSources.TryGetValue("Byproduct", out float byproduct))
                rating += byproduct * 0.7f;
            if (IngredientSources.TryGetValue("PlantProtein", out float plant))
                rating += plant * 0.8f;
            if (IngredientSources.TryGetValue("Additives", out float additives))
                rating += additives * 0.5f;
            if (UsesInsectMeal) rating += 0.05f;
            if (UsesAlgae) rating += 0.05f;
            SustainabilityRating = Math.Clamp(rating, 0.0f, 1.0f);
        }
    }

    /// <summary>
    /// Result of a feeding operation — provides data to water chemistry and finance.
    /// </summary>
    public struct FeedResult
    {
        public double FeedUsedKg;
        public double ExpectedGrowthKg;
        public double ActualFCR;
        public double NitrogenWasteKg;
        public double PhosphorusWasteKg;
        public decimal CostNOK;
        public double BiomassKg;

        /// <summary>Fraction of dispensed feed not consumed (0-1) from spreading quality.</summary>
        public double FeedWasteFraction;

        /// <summary>Absolute uneaten feed (kg) from imperfect spreading.</summary>
        public double FeedWasteKg;

        /// <summary>Cohort size variance (coefficient of variation) implied by spreading quality.</summary>
        public double SizeVarianceCv;

        public static FeedResult Empty => new FeedResult();
    }
}