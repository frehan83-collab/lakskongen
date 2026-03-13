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
        
        // Hatchery economic and biological parameters
        public decimal RearingCostPerFishPerDay { get; set; } = 0.15m; // NOK per fish per day (feed, labor, facilities)
        public int MaturationDays { get; set; } = 365; // days from egg to smolt-ready (12 months typical)
        public double SmoltReleaseWeight { get; set; } = 0.08; // kg (80g typical smolt weight)

        /// <summary>Create a batch of eggs.</summary>
        public void SpawnEggs(int count)
        {
            var cohort = new Biology.FishPopulation.Cohort(0, count, 0.0001, LifeStage.Egg);
            OnNewCohort?.Invoke(cohort);
        }

        /// <summary>
        /// Create smolt batch (for stocking sea pens).
        /// Smolts are reared to proper size and age before release.
        /// </summary>
        public void ReleaseSmolt(int count)
        {
            // Smolts are typically ~12-18 months old (365 days) and 60-100g when released
            var cohort = new Biology.FishPopulation.Cohort(MaturationDays, count, SmoltReleaseWeight, LifeStage.Smolt);
            OnNewCohort?.Invoke(cohort);
        }
        
        /// <summary>
        /// Calculate total rearing cost for raising fish in the hatchery.
        /// </summary>
        /// <param name="fishCount">Number of fish being reared</param>
        /// <param name="days">Number of days in hatchery</param>
        /// <returns>Total cost in NOK</returns>
        public decimal CalculateRearingCost(int fishCount, int days)
        {
            return RearingCostPerFishPerDay * fishCount * days;
        }
        
        /// <summary>
        /// Check if fish have reached maturation age and are ready for release to sea pens.
        /// </summary>
        /// <param name="daysInHatchery">Number of days fish have been in hatchery</param>
        /// <returns>True if fish are ready for release as smolts</returns>
        public bool IsReadyForRelease(int daysInHatchery)
        {
            return daysInHatchery >= MaturationDays;
        }
    }
}