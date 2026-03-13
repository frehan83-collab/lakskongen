using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

        /// <summary>Unique identifier for this population.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>List of cohorts in this population.</summary>
        public List<Cohort> Cohorts { get; set; } = new();

        /// <summary>Total count of all fish across all cohorts.</summary>
        public int TotalCount => Cohorts.Sum(c => c.Count);

        /// <summary>Zone this population resides in.</summary>
        public string ZoneId { get; set; } = "";

        /// <summary>Whether this population had a recent treatment (resets after 30 days).</summary>
        public bool HasRecentTreatment { get; set; }

        /// <summary>Treatment intensity (0.0-1.0) from last treatment.</summary>
        public double TreatmentIntensity { get; set; }

        /// <summary>Sea lice load (average lice per fish) for this population.</summary>
        public double SeaLiceLoad { get; set; } = 0.3;

        /// <summary>Growth penalty multiplier from disease resistance fitness cost. Serialized for save/load.</summary>
        public double GrowthPenalty { get => _growthPenalty; set => _growthPenalty = value; }
        private double _growthPenalty = 0.0;

        /// <summary>Raised when cohorts change (spawn/growth/mortality). Not serialized (events are excluded by default).</summary>
        public event Action? OnPopulationChanged;

        private GrowthModel _growth = null!;
        private MortalityModel _mortality = null!;

        /// <summary>Parameterless constructor for JSON deserialization. GrowthModel/MortalityModel must be injected post-load by the simulation engine.</summary>
        [JsonConstructor]
        public FishPopulation() { }

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

        /// <summary>Triggers the OnPopulationChanged event. Used by external systems that modify cohorts directly.</summary>
        public void TriggerOnPopulationChanged()
        {
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

        /// <summary>
        /// Apply a growth penalty from disease resistance fitness cost.
        /// Penalty is subtracted from effective growth rate (0.0 = no penalty).
        /// </summary>
        public void ApplyGrowthPenalty(double penalty)
        {
            _growthPenalty = Math.Clamp(penalty, 0.0, 0.5); // Cap at 50% reduction
        }

        /// <summary>Get current growth penalty.</summary>
        public double GetGrowthPenalty() => GrowthPenalty;

        /// <summary>
        /// Get individual fish representations for agent-based disease simulation.
        /// Creates lightweight proxies from cohort data (not persistent — regenerated each call).
        /// </summary>
        public List<LakskongenSim.Health.FishIndividual> GetIndividuals()
        {
            var individuals = new List<LakskongenSim.Health.FishIndividual>();
            int id = 0;
            foreach (var cohort in Cohorts)
            {
                // Create representative individuals (sample up to 100 per cohort for performance)
                int sampleSize = Math.Min(cohort.Count, 100);
                for (int i = 0; i < sampleSize; i++)
                {
                    individuals.Add(new LakskongenSim.Health.FishIndividual
                    {
                        Id = $"{ZoneId}-{id++}",
                        ZoneId = ZoneId,
                        Weight = cohort.MeanWeightKg,
                        GrowthRate = 0.01,
                        HasResistanceGene = false
                    });
                }
            }
            return individuals;
        }
    }
}