using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Cohort-based fish population model. Each cohort is a record of age, count and mean weight.
    /// </summary>
    public class FishPopulation
    {
        /// <summary>
        /// Represents a cohort in the population.
        /// </summary>
        public record Cohort(int AgeDays, int Count, double MeanWeightKg, LakskongenSim.Core.LifeStage Stage);

        /// <summary>List of cohorts in this population.</summary>
        public List<Cohort> Cohorts { get; } = new();

        /// <summary>Raised when cohorts change (spawn/growth/mortality).</summary>
        public event Action? OnPopulationChanged;

        private readonly GrowthModel _growth;
        private readonly MortalityModel _mortality;

        public FishPopulation(GrowthModel growth, MortalityModel mortality)
        {
            _growth = growth;
            _mortality = mortality;
        }

        /// <summary>Add a new cohort to the population.</summary>
        public void AddCohort(Cohort cohort)
        {
            Cohorts.Add(cohort);
            OnPopulationChanged?.Invoke();
        }

        /// <summary>Advance the population by days: age, apply growth and mortality.</summary>
        public void AdvanceDays(int days)
        {
            if (days <= 0) return;
            var updated = new List<Cohort>();
            foreach (var c in Cohorts)
            {
                var newAge = c.AgeDays + days;
                var newWeight = _growth.ApplyGrowth(c.MeanWeightKg, newAge - c.AgeDays, c.Stage);
                var survivors = _mortality.ApplyMortality(c.Count, days, c.Stage);
                var newStage = DetermineStage(newAge);
                if (survivors > 0)
                {
                    updated.Add(new Cohort(newAge, survivors, newWeight, newStage));
                }
            }
            Cohorts.Clear();
            Cohorts.AddRange(updated);
            OnPopulationChanged?.Invoke();
        }

        private LakskongenSim.Core.LifeStage DetermineStage(int ageDays)
        {
            // TODO: Replace thresholds with tunable parameters
            if (ageDays < 60) return LakskongenSim.Core.LifeStage.EyedEgg;
            if (ageDays < 180) return LakskongenSim.Core.LifeStage.Smolt;
            if (ageDays < 365) return LakskongenSim.Core.LifeStage.Juvenile;
            if (ageDays < 730) return LakskongenSim.Core.LifeStage.Adult;
            return LakskongenSim.Core.LifeStage.Harvestable;
        }
    }
}