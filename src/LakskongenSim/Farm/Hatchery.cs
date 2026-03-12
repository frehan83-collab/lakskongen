using System;
using LakskongenSim.Data;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Simple hatchery that produces cohorts of eggs or smolts.
    /// Uses biologically-accurate 7-stage salmon lifecycle.
    /// </summary>
    public class Hatchery
    {
        public event Action<Biology.FishPopulation.Cohort>? OnNewCohort;

        /// <summary>Create a batch of eggs.</summary>
        public void SpawnEggs(int count)
        {
            var cohort = new Biology.FishPopulation.Cohort(0, count, 0.0001, LifeStage.Egg);
            OnNewCohort?.Invoke(cohort);
        }

        /// <summary>Create smolt batch (for stocking sea pens).
        /// TODO: Add rearing costs and time to mature in hatchery.</summary>
        public void ReleaseSmolt(int count)
        {
            // Smolts are typically ~12-18 months old (365 days) and 60-100g when released
            var cohort = new Biology.FishPopulation.Cohort(365, count, 0.08, LifeStage.Smolt);
            OnNewCohort?.Invoke(cohort);
        }
    }
}