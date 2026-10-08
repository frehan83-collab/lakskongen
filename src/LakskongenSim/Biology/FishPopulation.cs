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
        public class Cohort
        {
            public int AgeDays { get; set; }
            public int Count { get; set; }
            public double MeanWeightKg { get; set; }
            public LifeStage Stage { get; set; }
            public Genome? Genetics { get; set; }

            /// <summary>
            /// Smolt/post-smolt quality carryover tier, stamped at sea transfer.
            /// Additive field; defaults to <see cref="Farm.SmoltQualityTier.Standard"/>
            /// so existing saves and new cohorts keep baseline behaviour.
            /// </summary>
            public Farm.SmoltQualityTier Quality { get; set; } = Farm.SmoltQualityTier.Standard;

            // Constructor for easy initialization, matching the original record's constructor
            public Cohort(int ageDays, int count, double meanWeightKg, LifeStage stage, Genome? genetics = null)
            {
                AgeDays = ageDays;
                Count = count;
                MeanWeightKg = meanWeightKg;
                Stage = stage;
                Genetics = genetics;
            }
        }

        /// <summary>Unique identifier for this population.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>List of cohorts in this population.</summary>
        public List<Cohort> Cohorts { get; set; } = new();

        /// <summary>Total count of all fish across all cohorts.</summary>
        public int TotalCount => Cohorts.Sum(c => c.Count);

        /// <summary>Total biomass of all fish across all cohorts in kilograms.</summary>
        public double GetTotalBiomassKg()
        {
            double totalBiomass = 0.0;
            foreach (var cohort in Cohorts)
            {
                totalBiomass += cohort.Count * cohort.MeanWeightKg;
            }
            return totalBiomass;
        }

        /// <summary>Zone this population resides in.</summary>
        public string ZoneId { get; set; } = "";

        /// <summary>Whether this population had a recent treatment (resets after 30 days).</summary>
        public bool HasRecentTreatment { get; set; }

        /// <summary>Treatment intensity (0.0-1.0) from last treatment.</summary>
        public double TreatmentIntensity { get; set; }

        /// <summary>Accumulated delousing injury/welfare load (P4). Compounds
        /// on repeat treatments, decays daily in the tick. Initialized with a
        /// long-ago last treatment so the first real treatment is unamplified.</summary>
        public Health.DelousingInjuryState TreatmentHarm { get; set; } =
            new Health.DelousingInjuryState { DaysSinceLastTreatment = 999 };

        /// <summary>Sea lice load (average lice per fish) for this population.</summary>
        public double SeaLiceLoad { get; set; } = 0.3;

        /// <summary>Vaccination day index (Phase 5.1, ulcer/CMS, 12-mo protection). -1 = never.</summary>
        public int VaccinatedDay { get; set; } = -1;

        /// <summary>Site ID this population belongs to (for O(1) lookup instead of O(n) search).</summary>
        public string SiteId { get; set; } = "";

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

        /// <summary>
        /// Inject dependencies after deserialization from save file.
        /// MUST be called after loading a saved game before advancing simulation.
        /// </summary>
        public void InjectDependencies(GrowthModel growth, MortalityModel mortality)
        {
            _growth = growth ?? throw new ArgumentNullException(nameof(growth));
            _mortality = mortality ?? throw new ArgumentNullException(nameof(mortality));
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

        /// <summary>Advance the population by days: age, apply growth and mortality.
        /// Optimized: in-place updates with no list allocations (T56).</summary>
        public void AdvanceDays(int days, bool skipGrowth = false)
        {
            if (days <= 0) return;
            var populationId = this.Id; // Cache to avoid repeated property access

            for (int i = 0; i < Cohorts.Count; i++)
            {
                var cohort = Cohorts[i];

                cohort.AgeDays += days;
                if (!skipGrowth)
                {
                    cohort.MeanWeightKg = _growth.ApplyGrowth(cohort.MeanWeightKg, days, cohort.Stage);
                }
                cohort.Count = _mortality.ApplyMortality(cohort.Count, days, cohort.Stage, populationId);
                cohort.Stage = DetermineStage(cohort.AgeDays);
            }

            // Remove dead cohorts in-place — no new list allocation
            Cohorts.RemoveAll(c => c.Count <= 0);

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
                        GrowthRate = cohort.Genetics?.GetGrowthMultiplier() ?? 1.0,
                        HasResistanceGene = (cohort.Genetics?.TraitValues.GetValueOrDefault(GeneticTrait.DiseaseResistance) ?? 0.0) > 0.5
                    });
                }
            }
            return individuals;
        }
    }
}