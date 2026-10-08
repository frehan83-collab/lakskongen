using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LakskongenSim.Health.Tests")]

namespace LakskongenSim.Health
{
    /// <summary>
    /// Advanced sea lice population model with life-stage structure, temperature dependence,
    /// treatment resistance, and environmental drivers.
    /// Based on: Samsing et al. 2017, Aldrin et al. 2017, Norwegian lice management framework.
    /// </summary>
    public class SeaLiceModel
    {
        #region Life Stage Counts (per fish)
        /// <summary>Chalimus (attached) stages I-II per fish.</summary>
        public double ChalimusPerFish { get; private set; } = 0.0;
        
        /// <summary>Pre-adult (mobile) stages per fish.</summary>
        public double PreAdultPerFish { get; private set; } = 0.0;
        
        /// <summary>Adult female lice per fish (egg-producing).</summary>
        public double AdultFemalePerFish { get; private set; } = 0.0;
        
        /// <summary>Adult male lice per fish.</summary>
        public double AdultMalePerFish { get; private set; } = 0.0;
        
        /// <summary>Total lice per fish (all stages).</summary>
        public double AverageLicePerFish => ChalimusPerFish + PreAdultPerFish + AdultFemalePerFish + AdultMalePerFish;
        #endregion

        #region Environmental Drivers
        /// <summary>Current water temperature (°C).</summary>
        public double TemperatureC { get; private set; } = 10.0;
        
        /// <summary>Current salinity (PSU).</summary>
        public double SalinityPSU { get; private set; } = 34.0;
        
        /// <summary>External lice pressure from neighboring farms/wild salmon (0.5-2.0).</summary>
        public double ExternalPressureMultiplier { get; private set; } = 1.0;
        
        /// <summary>Current speed at cage depth (m/s) - affects infection rate.</summary>
        public double CurrentSpeedMs { get; private set; } = 0.15;
        
        /// <summary>Stocking density in kg/m³ - affects transmission.</summary>
        public double StockingDensityKgM3 { get; private set; } = 20.0;
        #endregion

        #region Treatment & Resistance
        /// <summary>Resistance level to pyrethroids (0.0 = susceptible, 1.0 = fully resistant).</summary>
        public double PyrethroidResistance { get; private set; } = 0.0;
        
        /// <summary>Resistance level to azamethiphos/organophosphates.</summary>
        public double OrganophosphateResistance { get; private set; } = 0.0;
        
        /// <summary>Resistance level to hydrogen peroxide.</summary>
        public double HydrogenPeroxideResistance { get; private set; } = 0.0;
        
        /// <summary>Resistance level to mechanical treatment (e.g., freshwater, thermal).</summary>
        public double MechanicalResistance { get; private set; } = 0.0;
        
        /// <summary>Cleaner fish efficiency multiplier (0.0-1.0).</summary>
        public double CleanerFishEfficiency { get; private set; } = 0.0;
        #endregion

        #region Events
        public event Action<double, double, double, double>? OnLiceStageChanged; // chalimus, preadult, adultF, adultM
        public event Action<double>? OnTotalLiceChanged;
        #endregion

        #region Constants (calibrated to Norwegian data)
        private const double CHALIMUS_TO_PREADULT_RATE_BASE = 0.05;   // per day at 10°C
        private const double PREADULT_TO_ADULT_RATE_BASE = 0.03;      // per day at 10°C
        private const double ADULT_FEMALE_EGG_RATE_BASE = 120.0;      // ↓ from 150 (calibrated to ~0.5 steady state)
        private const double EGG_HATCH_RATE_BASE = 0.15;              // per day at 10°C
        private const double NATURAL_MORTALITY_BASE = 0.025;          // ↑ from 0.02 (slightly higher natural loss)
        private const double TEMPERATURE_Q10 = 2.0;                   // Rate doubles per 10°C
        private const double OPTIMAL_TEMP_C = 12.0;
        private const double SALINITY_OPTIMAL = 34.0;
        private const double SALINITY_MIN = 20.0;
        
        // Infection parameters
        private const double INFECTION_RATE_BASE = 0.0006; // ↓ from 0.0008 (calibrated)
        private const double MAX_LICE_PER_FISH = 50.0;     // Crowding limit
        
        // Treatment efficacy (before resistance)
        private const double PYRETHROID_EFFICACY_BASE = 0.92; // ↓ from 0.95
        private const double ORGANOPHOSPHATE_EFFICACY_BASE = 0.88; // ↓ from 0.90
        private const double H2O2_EFFICACY_BASE = 0.65; // ↓ from 0.70
        private const double MECHANICAL_EFFICACY_BASE = 0.82; // ↓ from 0.85
        
        // Resistance development
        private const double RESISTANCE_DEVELOPMENT_RATE = 0.0008; // ↓ from 0.001 (slower resistance)
        private const double RESISTANCE_FITNESS_COST = 0.08;       // ↑ from 0.05 (higher fitness cost)
        
        // Cleaner fish
        private const double CLEANER_FISH_MAX_EFFICIENCY = 0.65;   // ↑ from 0.6
        private const double CLEANER_FISH_STOCKING_RATIO = 0.02;  // 2% cleaner fish per salmon
        #endregion

        #region State
        private double _copepodidDensity = 0.0; // Free-swimming infective stages per m³
        #endregion

        #region Events
        public event Action<double>? OnLiceChanged;
        public event Action<string, double>? OnTreatmentApplied; // treatment type, efficacy
        public event Action<string>? OnResistanceChanged;
        #endregion

        /// <summary>
        /// Update environmental conditions.
        /// </summary>
        public void SetEnvironment(double temperatureC, double salinityPSU, double currentSpeedMs, double stockingDensityKgM3)
        {
            TemperatureC = Math.Clamp(temperatureC, 0.0, 20.0);
            SalinityPSU = Math.Clamp(salinityPSU, 20.0, 35.0);
            CurrentSpeedMs = Math.Max(0.0, currentSpeedMs);
            StockingDensityKgM3 = Math.Max(0.0, stockingDensityKgM3);
        }

        /// <summary>
        /// Set external lice pressure from neighboring farms/wild salmon.
        /// </summary>
        public void SetExternalPressure(double multiplier)
        {
            ExternalPressureMultiplier = Math.Clamp(multiplier, 0.2, 3.0);
        }

        /// <summary>
        /// Fraction of stock (0-1) protected by lice skirts / snorkels (Phase 1.5).
        /// Reduces copepodid settlement up to 70% at full coverage.
        /// </summary>
        public double PreventiveCoverage01 { get; set; } = 0.0;

        /// <summary>
        /// Set cleaner fish stocking ratio (0.0-0.1).
        /// </summary>
        public void SetCleanerFishStocking(double ratio)
        {
            CleanerFishEfficiency = Math.Clamp(ratio / CLEANER_FISH_STOCKING_RATIO * CLEANER_FISH_MAX_EFFICIENCY, 0.0, CLEANER_FISH_MAX_EFFICIENCY);
        }

        /// <summary>
        /// Advance simulation by one day.
        /// </summary>
        public void AdvanceDay()
        {
            if (AverageLicePerFish <= 0.001) return; // Extinct
            
            // Temperature scaling (Q10 ≈ 2.0)
            double tempFactor = Math.Pow(2.0, (TemperatureC - 10.0) / 10.0);
            double tempFactorOptimal = Math.Exp(-Math.Pow(TemperatureC - 12.0, 2) / 18.0); // Optimal ~12°C
            double salinityFactor = Math.Clamp(1.0 - Math.Abs(SalinityPSU - SALINITY_OPTIMAL) * 0.1, 0.3, 1.0);
            double densityFactor = Math.Min(1.0, StockingDensityKgM3 / 25.0); // Higher density = more transmission
            
            double envFactor = tempFactor * tempFactorOptimal * salinityFactor * (0.5 + 0.5 * densityFactor);
            
            // Salinity mortality (lethal below 20 PSU)
            if (SalinityPSU < SALINITY_MIN)
            {
                ApplyMortality(0.1 * (SALINITY_MIN - SalinityPSU) / 5.0);
            }

            // Development rates (temperature-corrected)
            double chalimusToPreAdult = CHALIMUS_TO_PREADULT_RATE_BASE * envFactor;
            double preAdultToAdult = PREADULT_TO_ADULT_RATE_BASE * envFactor;
            double eggRate = ADULT_FEMALE_EGG_RATE_BASE * envFactor;
            double hatchRate = EGG_HATCH_RATE_BASE * envFactor;
            double naturalMortality = NATURAL_MORTALITY_BASE * envFactor;

            // Infection pressure from copepodids
            double infectionPressure = _copepodidDensity * INFECTION_RATE_BASE * (1.0 + CurrentSpeedMs * 2.0) * (1.0 + StockingDensityKgM3 / 50.0);
            infectionPressure *= ExternalPressureMultiplier;

            // New infections -> chalimus (skirts block settlement pre-attachment)
            double newChalimus = infectionPressure * (1.0 - AverageLicePerFish / MAX_LICE_PER_FISH);
            newChalimus *= 1.0 - 0.70 * Math.Clamp(PreventiveCoverage01, 0.0, 1.0);
            ChalimusPerFish += newChalimus;

            // Stage transitions
            double toPreAdult = ChalimusPerFish * chalimusToPreAdult;
            ChalimusPerFish -= toPreAdult;
            PreAdultPerFish += toPreAdult;

            double toAdult = PreAdultPerFish * preAdultToAdult;
            PreAdultPerFish -= toAdult;
            AdultFemalePerFish += toAdult * 0.5; // 50% female
            AdultMalePerFish += toAdult * 0.5;

            // Egg production -> copepodids
            double eggs = AdultFemalePerFish * eggRate;
            _copepodidDensity += eggs * hatchRate; // Simplified: eggs -> copepodids directly

            // Natural mortality
            ApplyMortality(naturalMortality);

            // Cleaner fish predation
            if (CleanerFishEfficiency > 0)
            {
                double cleanerMortality = CleanerFishEfficiency * 0.05; // Up to 5%/day
                ApplyMortality(cleanerMortality);
            }

            // Copepodid decay (mortality + settlement)
            _copepodidDensity *= 0.95; // 5%/day loss

            // Notify
            OnLiceStageChanged?.Invoke(ChalimusPerFish, PreAdultPerFish, AdultFemalePerFish, AdultMalePerFish);
            OnTotalLiceChanged?.Invoke(AverageLicePerFish);
            OnLiceChanged?.Invoke(AverageLicePerFish);
        }

        private void ApplyMortality(double rate)
        {
            rate = Math.Clamp(rate, 0.0, 0.5); // Max 50%/day
            ChalimusPerFish *= (1.0 - rate);
            PreAdultPerFish *= (1.0 - rate);
            AdultFemalePerFish *= (1.0 - rate);
            AdultMalePerFish *= (1.0 - rate);
            _copepodidDensity *= (1.0 - rate);
        }

        /// <summary>
        /// Base (resistance-free) efficacy for a treatment type. Callers that
        /// already computed an operational efficacy should normalize their
        /// dose by this so the kill is efficacy x (1 - resistance), not the
        /// product of two efficacy estimates (S8 double-dip).
        /// </summary>
        public static double GetBaseEfficacy(string treatmentType)
        {
            return treatmentType.ToLower() switch
            {
                "pyrethroid" or "deltamethrin" or "cypermethrin" => PYRETHROID_EFFICACY_BASE,
                "organophosphate" or "azamethiphos" => ORGANOPHOSPHATE_EFFICACY_BASE,
                "hydrogen_peroxide" or "h2o2" => H2O2_EFFICACY_BASE,
                "mechanical" or "freshwater" or "thermal" => MECHANICAL_EFFICACY_BASE,
                _ => 0.5,
            };
        }

        /// <summary>
        /// Apply chemical treatment.
        /// </summary>
        /// <param name="treatmentType">"pyrethroid", "organophosphate", "hydrogen_peroxide"</param>
        /// <param name="doseFraction">Dose fraction 0-1 (default 1.0)</param>
        /// <returns>Fraction of lice killed (0-1)</returns>
        public double ApplyTreatment(string treatmentType, double doseFraction = 1.0)
        {
            double efficacy = 0.0;
            string resistanceField = "";
            
            switch (treatmentType.ToLower())
            {
                case "pyrethroid":
                case "deltamethrin":
                case "cypermethrin":
                    efficacy = PYRETHROID_EFFICACY_BASE * (1.0 - PyrethroidResistance);
                    resistanceField = nameof(PyrethroidResistance);
                    break;
                case "organophosphate":
                case "azamethiphos":
                    efficacy = ORGANOPHOSPHATE_EFFICACY_BASE * (1.0 - OrganophosphateResistance);
                    resistanceField = nameof(OrganophosphateResistance);
                    break;
                case "hydrogen_peroxide":
                case "h2o2":
                    efficacy = H2O2_EFFICACY_BASE * (1.0 - HydrogenPeroxideResistance);
                    resistanceField = nameof(HydrogenPeroxideResistance);
                    break;
                case "mechanical":
                case "freshwater":
                case "thermal":
                    efficacy = MECHANICAL_EFFICACY_BASE * (1.0 - MechanicalResistance);
                    resistanceField = nameof(MechanicalResistance);
                    break;
                default:
                    efficacy = 0.5;
                    resistanceField = "";
                    break;
            }

            efficacy *= doseFraction;
            efficacy = Math.Clamp(efficacy, 0.0, 0.99);

            // Stage-specific efficacy (Phase 2.1): attached chalimus shrug
            // off peroxide baths and thermal/mechanical stripping that clear
            // mobiles effectively — follow-up treatments stay necessary.
            double chalimusFactor = treatmentType.ToLower() switch
            {
                "hydrogen_peroxide" or "h2o2" => 0.35,
                "thermal" or "mechanical" or "freshwater" => 0.6,
                _ => 1.0,
            };
            double survival = 1.0 - efficacy;
            ChalimusPerFish *= 1.0 - efficacy * chalimusFactor;
            PreAdultPerFish *= survival;
            AdultFemalePerFish *= survival;
            AdultMalePerFish *= survival;

            // Develop resistance (simplified)
            if (!string.IsNullOrEmpty(resistanceField))
            {
                var prop = GetType().GetProperty(resistanceField);
                if (prop != null)
                {
                    double current = prop.GetValue(this) is double d ? d : 0.0;
                    double increase = RESISTANCE_DEVELOPMENT_RATE * efficacy; // Stronger selection = faster resistance
                    prop.SetValue(this, Math.Min(1.0, current + increase));
                    double newVal = prop.GetValue(this) is double nd ? nd : 0.0;
                    OnResistanceChanged?.Invoke($"{resistanceField}: {newVal:P1}");
                }
            }

            OnTreatmentApplied?.Invoke(treatmentType, efficacy);
            OnLiceStageChanged?.Invoke(ChalimusPerFish, PreAdultPerFish, AdultFemalePerFish, AdultMalePerFish);
            OnTotalLiceChanged?.Invoke(AverageLicePerFish);
            OnLiceChanged?.Invoke(AverageLicePerFish);

            return efficacy;
        }

        /// <summary>
        /// Advance resistance decay (fitness cost) when no selection pressure.
        /// </summary>
        public void AdvanceResistanceDecay(int daysWithoutTreatment)
        {
            double decay = RESISTANCE_FITNESS_COST * daysWithoutTreatment / 365.0;
            PyrethroidResistance = Math.Max(0.0, PyrethroidResistance - decay);
            OrganophosphateResistance = Math.Max(0.0, OrganophosphateResistance - decay);
            HydrogenPeroxideResistance = Math.Max(0.0, HydrogenPeroxideResistance - decay);
            MechanicalResistance = Math.Max(0.0, MechanicalResistance - decay);
            
            if (daysWithoutTreatment > 0)
                OnResistanceChanged?.Invoke($"Decay: {daysWithoutTreatment} days");
        }

        /// <summary>
        /// Throwaway copy for read-only projections (Phase 4.5). No events
        /// fire on the clone (fresh invocation list); live state untouched.
        /// </summary>
        public SeaLiceModel CloneForProjection()
        {
            var c = new SeaLiceModel
            {
                ChalimusPerFish = ChalimusPerFish,
                PreAdultPerFish = PreAdultPerFish,
                AdultFemalePerFish = AdultFemalePerFish,
                AdultMalePerFish = AdultMalePerFish,
                PreventiveCoverage01 = PreventiveCoverage01,
                PyrethroidResistance = PyrethroidResistance,
                OrganophosphateResistance = OrganophosphateResistance,
                HydrogenPeroxideResistance = HydrogenPeroxideResistance,
                MechanicalResistance = MechanicalResistance,
                CleanerFishEfficiency = CleanerFishEfficiency
            };
            c.SetEnvironment(TemperatureC, SalinityPSU, CurrentSpeedMs, StockingDensityKgM3);
            c.SetExternalPressure(ExternalPressureMultiplier);
            c._copepodidDensity = _copepodidDensity;
            return c;
        }

        /// <summary>
        /// Introduce new lice from external source (wild salmon, neighboring farms).
        /// </summary>
        public void SeedExternalInfection(double intensity = 0.1)
        {
            ChalimusPerFish += intensity * 0.5;
            _copepodidDensity += intensity * 10.0;
        }

        /// <summary>
        /// Get current lice stage distribution for UI.
        /// </summary>
        public (double chalimus, double preAdult, double adultFemale, double adultMale, double total, double copepodidDensity) GetStageDistribution()
        {
            return (ChalimusPerFish, PreAdultPerFish, AdultFemalePerFish, AdultMalePerFish, AverageLicePerFish, _copepodidDensity);
        }

        /// <summary>
        /// Reset to initial state.
        /// </summary>
        public void Reset(double initialLice = 0.1)
        {
            ChalimusPerFish = initialLice * 0.5;
            PreAdultPerFish = initialLice * 0.3;
            AdultFemalePerFish = initialLice * 0.1;
            AdultMalePerFish = initialLice * 0.1;
            _copepodidDensity = initialLice * 10.0;
            PyrethroidResistance = 0.0;
            OrganophosphateResistance = 0.0;
            HydrogenPeroxideResistance = 0.0;
            MechanicalResistance = 0.0;
            CleanerFishEfficiency = 0.0;
            ExternalPressureMultiplier = 1.0;
            TemperatureC = 10.0;
            SalinityPSU = 34.0;
            CurrentSpeedMs = 0.15;
            StockingDensityKgM3 = 20.0;
        }
    }
}