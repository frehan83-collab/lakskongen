using System;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Placeholder delousing system that reduces sea lice burden at a cost.
    /// </summary>
    public class DelousingSystem
    {
        public decimal TreatmentCostPerFish = 2m; // NOK per fish

        public event Action<decimal>? OnTreatmentCost;

        /// <summary>Apply treatment to a population, returns number treated.</summary>
        public int Treat(Biology.FishPopulation pop)
        {
            int total = 0;
            foreach (var c in pop.Cohorts) total += c.Count;
            decimal cost = total * TreatmentCostPerFish;
            OnTreatmentCost?.Invoke(cost);
            // TODO: modify health/sea lice model
            return total;
        }
    }
}