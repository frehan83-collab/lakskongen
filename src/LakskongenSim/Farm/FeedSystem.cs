using System;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Simplified feed system impacting growth and cost.
    /// </summary>
    public class FeedSystem
    {
        public decimal DailyFeedCostPerKg = 20m; // NOK per kg feed (placeholder)

        public event Action<decimal>? OnFeedCostIncurred;

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
    }
}