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

        /// <summary>
        /// Prevalence a population starts from when first seen. The engine sets
        /// this to the calibrated endemic equilibrium so the disease slice tracks
        /// its calibrated share of the sea-mortality anchor deterministically
        /// (instead of the old 1e-6 seed that left the subsystem dormant).
        /// </summary>
        public double InitialPrevalence { get; set; } = 1e-6;

        // How much sea-lice increases susceptibility (multiplier per lice/fish)
        public double LiceSusceptibilityFactor { get; set; } = 0.02; // per lice

        // Temperature sensitivity for treatment efficacy (linear modifier for simplicity)
        public double TreatmentTempSensitivityPerDegC { get; set; } = -0.01; // efficacy decreases 1% per degC above ref
        public double TreatmentRefTempC { get; set; } = 10.0;

        // Tracks infected fraction per population (0..1)
        private readonly Dictionary<FishPopulation, double> _infected = new();

        private readonly SeaLiceModel _liceModel;
        
        /// <summary>Expose lice model for integration with stochastic disease engine.</summary>
        public SeaLiceModel LiceModel => _liceModel;

        public event Action<FishPopulation, double>? OnInfection; // population, prevalence

        // MortalityModel reference for rate injection (prevents double mortality)
        private Biology.MortalityModel? _mortalityModel;

        public DiseaseSystem(SeaLiceModel liceModel)
        {
            _liceModel = liceModel ?? throw new ArgumentNullException(nameof(liceModel));
        }

        /// <summary>
        /// Inject MortalityModel reference to enable disease rate injection.
        /// Called by SimulationEngine during initialization.
        /// </summary>
        public void SetMortalityModel(Biology.MortalityModel mortalityModel)
        {
            _mortalityModel = mortalityModel;
        }

        /// <summary>
        /// Simulate disease spread for given population over days.
        /// Temperature (C) can be provided for temperature-dependent treatment effects.
        /// </summary>
        public void Tick(FishPopulation pop, int days, double temperatureC = 10.0)
        {
            if (pop == null) throw new ArgumentNullException(nameof(pop));
            if (days <= 0) return;

            if (!_infected.TryGetValue(pop, out var p)) p = Math.Clamp(InitialPrevalence, 0.0, 1.0);

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

            // Inject disease mortality rate instead of directly modifying cohorts
            var dailyRate = ExtraMortalityRatePerDay * p;
            System.Diagnostics.Debug.WriteLine($"[DiseaseSystem] Tick: PopId={pop.Id}, Prevalence={p:F4}, DailyMortRate={dailyRate:F6}");
            
            if (_mortalityModel != null && dailyRate > 0)
            {
                _mortalityModel.SetDiseaseMortalityRate(pop.Id, dailyRate);
            }

            OnInfection?.Invoke(pop, p);
        }

        /// <summary>
        /// Apply a delousing treatment. reductionFraction is the expected lice reduction (0..1)
        /// treatmentEfficacy is modified by temperature (default ref at TreatmentRefTempC).
        /// The method reduces lice, and reduces infected fraction proportionally to lice reduction
        /// and a direct treatment efficacy (simulates medicinal treatment).
        /// </summary>
        public void ApplyTreatment(FishPopulation pop, string treatmentType, double reductionFraction, double directTreatmentEfficacy, double temperatureC)
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
            _liceModel.ApplyTreatment(treatmentType, effectiveReduction);

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

        /// <summary>
        /// Stable endemic prevalence of the population-level SIR
        /// (<c>1 - gamma / (beta * liceEffect)</c>). Used to size the disease
        /// mortality rate so its calibrated per-cycle slice is reproducible.
        /// </summary>
        public double EquilibriumPrevalence()
        {
            double liceEffect = 1.0 + LiceSusceptibilityFactor * (_liceModel?.AverageLicePerFish ?? 0.0);
            double transmission = Beta * liceEffect;
            return transmission > Gamma ? Math.Clamp(1.0 - Gamma / transmission, 0.0, 1.0) : 0.0;
        }
    }
}
