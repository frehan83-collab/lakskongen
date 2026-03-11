using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;

namespace LakskongenSim.Health
{
    /// <summary>
    /// High-level disease system that can infect populations and trigger mortality.
    /// Implements a simple deterministic SIR-like model at the population level and
    /// an interaction model with sea-lice and delousing treatments.
    /// </summary>
    public class DiseaseSystem
    {
        // Transmission and recovery parameters (per day)
        public double Beta { get; set; } = 0.20;   // transmission coefficient
        public double Gamma { get; set; } = 0.05;  // recovery rate

        // Additional daily mortality applied proportionally to infected fraction
        public double ExtraMortalityRatePerDay { get; set; } = 0.002; // 0.2% per day at 100% prevalence

        // How much sea-lice increases susceptibility (multiplier per lice/fish)
        public double LiceSusceptibilityFactor { get; set; } = 0.02; // per lice

        // Temperature sensitivity for treatment efficacy (linear modifier for simplicity)
        public double TreatmentTempSensitivityPerDegC { get; set; } = -0.01; // efficacy decreases 1% per degC above ref
        public double TreatmentRefTempC { get; set; } = 10.0;

        // Tracks infected fraction per population (0..1)
        private readonly Dictionary<FishPopulation, double> _infected = new();

        private readonly SeaLiceModel _liceModel;

        public event Action<FishPopulation, double>? OnInfection; // population, prevalence

        public DiseaseSystem(SeaLiceModel liceModel)
        {
            _liceModel = liceModel ?? throw new ArgumentNullException(nameof(liceModel));
        }

        /// <summary>
        /// Simulate disease spread for given population over days.
        /// Temperature (C) can be provided for temperature-dependent treatment effects.
        /// </summary>
        public void Tick(FishPopulation pop, int days, double temperatureC = 10.0)
        {
            if (pop == null) throw new ArgumentNullException(nameof(pop));
            if (days <= 0) return;

            if (!_infected.TryGetValue(pop, out var p)) p = 1e-6; // tiny seed

            // incorporate lice-driven increased transmission
            var liceEffect = 1.0 + LiceSusceptibilityFactor * _liceModel.AverageLicePerFish;

            // simple Euler integration of dI/dt = beta * liceEffect * I * (1-I) - gamma * I
            for (int d = 0; d < days; d++)
            {
                var dp = Beta * liceEffect * p * (1.0 - p) - Gamma * p;
                p += dp;
                // numerical safety
                if (p < 0) p = 0;
                if (p > 1) p = 1;
            }

            _infected[pop] = p;

            // Apply additional mortality proportional to infected fraction.
            // Approximate extra deaths = total * (1 - (1 - extraRate*p)^days)
            var total = pop.Cohorts.Sum(c => c.Count);
            if (total > 0)
            {
                var daily = ExtraMortalityRatePerDay * p;
                var survivorsFactor = Math.Pow(1.0 - daily, days);
                var survivors = (int)Math.Round(total * survivorsFactor);
                var deaths = Math.Max(0, (int)total - survivors);
                if (deaths > 0)
                {
                    // remove deaths proportionally from cohorts
                    var remainingToRemove = deaths;
                    var updated = new List<FishPopulation.Cohort>();
                    foreach (var c in pop.Cohorts)
                    {
                        if (remainingToRemove <= 0)
                        {
                            updated.Add(c);
                            continue;
                        }
                        var take = (int)Math.Round((double)c.Count / total * deaths);
                        take = Math.Min(take, c.Count);
                        remainingToRemove -= take;
                        var survivorsInC = c.Count - take;
                        if (survivorsInC > 0)
                        {
                            updated.Add(new FishPopulation.Cohort(c.AgeDays, survivorsInC, c.MeanWeightKg, c.Stage));
                        }
                    }

                    // If rounding left some, remove from last cohort
                    if (remainingToRemove > 0 && updated.Count > 0)
                    {
                        var last = updated[updated.Count - 1];
                        var newCount = Math.Max(0, last.Count - remainingToRemove);
                        updated[updated.Count - 1] = new FishPopulation.Cohort(last.AgeDays, newCount, last.MeanWeightKg, last.Stage);
                        updated.RemoveAll(c => c.Count == 0);
                    }

                    // Replace cohorts
                    pop.Cohorts.Clear();
                    pop.Cohorts.AddRange(updated);
                    pop.OnPopulationChanged?.Invoke();
                }
            }

            OnInfection?.Invoke(pop, p);
        }

        /// <summary>
        /// Apply a delousing treatment. reductionFraction is the expected lice reduction (0..1)
        /// treatmentEfficacy is modified by temperature (default ref at TreatmentRefTempC).
        /// The method reduces lice, and reduces infected fraction proportionally to lice reduction
        /// and a direct treatment efficacy (simulates medicinal treatment).
        /// </summary>
        public void ApplyTreatment(FishPopulation pop, double reductionFraction, double directTreatmentEfficacy, double temperatureC)
        {
            if (reductionFraction < 0) reductionFraction = 0;
            if (reductionFraction > 1) reductionFraction = 1;
            if (directTreatmentEfficacy < 0) directTreatmentEfficacy = 0;
            if (directTreatmentEfficacy > 1) directTreatmentEfficacy = 1;

            // temperature modifier (linear): effic decreases with temp if sensitivity negative
            var tempModifier = 1.0 + TreatmentTempSensitivityPerDegC * (temperatureC - TreatmentRefTempC);
            tempModifier = Math.Clamp(tempModifier, 0.0, 2.0);

            // apply to lice model
            var effectiveReduction = reductionFraction * tempModifier;
            _liceModel.ApplyTreatment(effectiveReduction);

            // reduce infected fraction: two effects - via lice (less susceptibility) and direct treatment
            if (!_infected.TryGetValue(pop, out var p)) p = 0.0;

            var liceContribution = effectiveReduction * 0.5; // assume half of benefit via lice
            var directContribution = directTreatmentEfficacy * tempModifier * 0.5;

            var totalReduction = Math.Clamp(liceContribution + directContribution, 0.0, 1.0);
            p *= (1.0 - totalReduction);
            _infected[pop] = p;

            OnInfection?.Invoke(pop, p);
        }

        /// <summary>Get current infected fraction for a population.</summary>
        public double GetPrevalence(FishPopulation pop)
        {
            if (pop == null) throw new ArgumentNullException(nameof(pop));
            return _infected.TryGetValue(pop, out var p) ? p : 0.0;
        }
    }
}
