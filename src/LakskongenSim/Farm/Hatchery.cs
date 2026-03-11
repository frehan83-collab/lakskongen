using System;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Simple hatchery that produces cohorts of eyed eggs or smolts.
    /// </summary>
    public class Hatchery
    {
        public event Action<Biology.FishPopulation.Cohort>? OnNewCohort;

        /// <summary>Create a batch of eyed eggs.</summary>
        public void SpawnEggs(int count)
        {
            var cohort = new Biology.FishPopulation.Cohort(0, count, 0.0001, LakskongenSim.Core.LifeStage.EyedEgg);
            OnNewCohort?.Invoke(cohort);
        }

        /// <summary>Create smolt batch (for stocking).
        /// TODO: Add rearing costs and time to mature in hatchery.</summary>
        public void ReleaseSmolt(int count)
        {
            var cohort = new Biology.FishPopulation.Cohort(90, count, 0.02, LakskongenSim.Core.LifeStage.Smolt);
            OnNewCohort?.Invoke(cohort);
        }
    }
}