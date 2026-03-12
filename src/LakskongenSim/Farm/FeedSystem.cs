using System;
using System.Collections.Generic;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Simplified feed system impacting growth and cost.
    /// </summary>
    public class FeedSystem
    {
        public decimal DailyFeedCostPerKg = 20m; // NOK per kg feed (placeholder)

        // Sustainability integration properties
        public float SustainabilityRating { get; private set; } = 0.7f; // 0.0-1.0 (default: 70% sustainable)
        public Dictionary<string, float> IngredientSources { get; private set; } = new();
        public bool UsesInsectMeal { get; set; } = false;
        public bool UsesAlgae { get; set; } = false;

        public event Action<decimal>? OnFeedCostIncurred;

        public FeedSystem()
        {
            // Default feed composition (% by weight)
            IngredientSources = new Dictionary<string, float>
            {
                ["FishMeal"] = 0.30f,      // Wild-caught fishmeal
                ["PlantProtein"] = 0.40f,  // Soy, wheat, pea
                ["Byproduct"] = 0.25f,     // Fish processing waste
                ["Additives"] = 0.05f      // Vitamins, minerals
            };
            UpdateSustainabilityRating();
        }

        /// <summary>Apply feed to population; returns feed used (kg).</summary>
        public double Feed(Biology.FishPopulation pop, int days)
        {
            // Very simple: feed proportional to biomass. TODO: add FCR and stage-specific consumption
            double biomassKg = 0.0;
            foreach (var c in pop.Cohorts)
            {
                biomassKg += c.Count * c.MeanWeightKg;
            }
            double dailyFeed = biomassKg * 0.02; // 2% of bodyweight
            double totalFeed = dailyFeed * days;
            OnFeedCostIncurred?.Invoke((decimal)totalFeed * DailyFeedCostPerKg);
            return totalFeed;
        }

        /// <summary>Update feed composition and recalculate sustainability rating.</summary>
        public void SetIngredientComposition(Dictionary<string, float> composition)
        {
            IngredientSources = composition;
            UpdateSustainabilityRating();
        }

        /// <summary>Calculate sustainability rating based on feed ingredients.</summary>
        private void UpdateSustainabilityRating()
        {
            float rating = 0.0f;

            // Weighted sustainability scores per ingredient type
            if (IngredientSources.TryGetValue("FishMeal", out float fishMeal))
                rating += fishMeal * 0.4f; // Wild-caught = 40% sustainable

            if (IngredientSources.TryGetValue("Byproduct", out float byproduct))
                rating += byproduct * 0.7f; // Processing waste = 70% sustainable

            if (IngredientSources.TryGetValue("PlantProtein", out float plant))
                rating += plant * 0.8f; // Plant-based = 80% sustainable

            if (IngredientSources.TryGetValue("Additives", out float additives))
                rating += additives * 0.5f; // Additives = neutral 50%

            // Bonus for advanced ingredients
            if (UsesInsectMeal)
                rating += 0.05f; // +5% for insect meal

            if (UsesAlgae)
                rating += 0.05f; // +5% for algae

            SustainabilityRating = Math.Clamp(rating, 0.0f, 1.0f);
        }
    }
}