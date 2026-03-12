using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Cohort-based fish population model. Each cohort is a record of age, count and mean weight.
    /// Uses biologically-accurate 7-stage salmon lifecycle from Data.LifeStage.
    /// </summary>
    public class FishPopulation
    {
        /// <summary>
        /// Represents a cohort in the population.
        /// </summary>
        public record Cohort(int AgeDays, int Count, double MeanWeightKg, LifeStage Stage);

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

        /// <summary>
        /// Determine life stage based on age in days.
        /// Based on Norwegian salmon lifecycle: Egg→Alevin→Fry→Parr→Smolt→Seawater→Harvest
        /// </summary>
        private LifeStage DetermineStage(int ageDays)
        {
            // Biologically-accurate salmon lifecycle thresholds
            if (ageDays < 60) return LifeStage.Egg;           // 0-60 days, temperature-dependent
            if (ageDays < 90) return LifeStage.Alevin;        // Yolk sac stage, 4-6 weeks
            if (ageDays < 180) return LifeStage.Fry;          // First feeding, 3-4 months
            if (ageDays < 300) return LifeStage.Parr;         // Freshwater growth, 4-6 months
            if (ageDays < 365) return LifeStage.Smolt;        // Seawater-ready juveniles
            if (ageDays < 730) return LifeStage.Seawater;     // Growing in sea pens, 12-18 months
            return LifeStage.Harvest;                          // Ready for processing at 4-6kg
        }
    }
}