using System;
using System.Collections.Generic;
using System.Linq;
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
        public decimal FeedCostPerKg { get; set; } = 14m;  // NOK/kg (industry avg ~12-16 NOK/kg)

        // ─── FCR parameters ───
        public double BaseFCR { get; private set; } = 1.15; // Industry average (GSI/Seafish data)
        public double CurrentFCR { get; private set; } = 1.15;

        // Temperature-FCR relationship (Norwegian data: optimal 11-13°C)
        private const double OptimalFCRTemp = 12.0;
        private const double FCRTempSensitivity = 0.02; // FCR increases 0.02 per °C away from optimal

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

            // Clamp to realistic range
            CurrentFCR = Math.Clamp(fcr, 0.9, 2.5);
            OnFCRChanged?.Invoke(CurrentFCR);

            return CurrentFCR;
        }

        /// <summary>
        /// Apply feed to population. Returns total feed used (kg).
        /// Also calculates waste output for water chemistry integration.
        /// </summary>
        public FeedResult Feed(Biology.FishPopulation pop, int days,
            double temperatureC = 12.0, double stockingDensityKgM3 = 20.0,
            double waterQualityPenalty = 1.0)
        {
            double biomassKg = pop.Cohorts.Sum(c => c.Count * c.MeanWeightKg);
            if (biomassKg <= 0) return FeedResult.Empty;

            // Get dominant stage for FCR calculation
            var dominantStage = pop.Cohorts
                .OrderByDescending(c => c.Count)
                .Select(c => c.Stage)
                .FirstOrDefault();

            // Calculate dynamic FCR
            double fcr = CalculateFCR(temperatureC, dominantStage, stockingDensityKgM3, waterQualityPenalty);

            // Daily feed amount (% of bodyweight, adjustable by player)
            double dailyFeedKg = biomassKg * (FeedingRatePercent / 100.0);
            double totalFeedKg = dailyFeedKg * days;

            // Expected growth from this feed
            double expectedGrowthKg = totalFeedKg / fcr;

            // Waste outputs (for water chemistry)
            // ~30% of feed N is retained, 70% excreted (Norwegian aquaculture data)
            double nitrogenWasteKg = totalFeedKg * 0.07 * 0.70; // 7% N in feed, 70% excreted
            double phosphorusWasteKg = totalFeedKg * 0.01 * 0.70; // 1% P in feed, 70% excreted

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
                BiomassKg = biomassKg
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

        public static FeedResult Empty => new FeedResult();
    }
}