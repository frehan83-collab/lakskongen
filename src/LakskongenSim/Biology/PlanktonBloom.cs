using System;
using System.Collections.Generic;
using LakskongenSim.Environment;
using LakskongenSim.Core;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Models phytoplankton bloom dynamics with logistic growth and crash events.
    /// Driven by light (daylength), temperature, nutrient load, and stratification.
    /// When biomass exceeds threshold, crash occurs -> respiration spike -> DO crash -> mortality.
    /// </summary>
    public class PlanktonBloom
    {
        // Bloom state per zone
        private readonly Dictionary<string, BloomState> _zoneBlooms = new();

        // Configuration (loaded from JSON)
        private PlanktonBloomConfig _config = new();

        // Deterministic RNG per zone per day (named stream; per-zone draws
        // should migrate to the suffix overload as stochastic triggers land).
        private readonly SimRngStream _globalRng;
        internal SimRngStream RngForSnapshot => _globalRng;

        public PlanktonBloom(SimRngStream rng)
        {
            _globalRng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public PlanktonBloom(int? seed = null)
            : this((seed.HasValue ? new SimRng(seed.Value) : SimRng.CreateUnseeded()).Stream(SimRngStreams.PlanktonBloom))
        {
        }

        /// <summary>
        /// Load configuration from JSON file.
        /// </summary>
        public void LoadConfig(PlanktonBloomConfig config)
        {
            _config = config ?? new PlanktonBloomConfig();
        }

        /// <summary>
        /// Advance bloom simulation by one day for all zones.
        /// </summary>
        public void AdvanceDay(GameState state, WeatherSystem weather)
        {
            if (!_config.Enabled) return;

            foreach (var zone in state.Zones)
            {
                AdvanceZoneBloom(zone, state.Day, weather);
            }
        }

        /// <summary>
        /// Advance bloom for a single zone using deterministic calculations.
        /// </summary>
        private void AdvanceZoneBloom(Zone zone, int day, WeatherSystem weather)
        {
            var bloom = _zoneBlooms.GetValueOrDefault(zone.Id, new BloomState());
            _zoneBlooms[zone.Id] = bloom;

            // Calculate bloom risk factors
            double lightFactor = CalculateLightFactor(day);
            double tempFactor = CalculateTemperatureFactor(zone.WaterChemistry.Temperature);
            double nutrientFactor = CalculateNutrientFactor(zone);
            double stratificationFactor = CalculateStratificationFactor(zone);

            // Logistic growth rate (0-1)
            double growthRate = _config.BaseGrowthRate * lightFactor * tempFactor * nutrientFactor * stratificationFactor;
            growthRate = Math.Clamp(growthRate, 0.0, _config.MaxGrowthRate);

            // Bloom intensity (0-1) represents chlorophyll-a concentration relative to max
            bloom.Intensity += growthRate * (1.0 - bloom.Intensity / _config.CarryingCapacity);
            bloom.Intensity = Math.Clamp(bloom.Intensity, 0.0, _config.CarryingCapacity);

            // Track days since bloom started
            if (bloom.Intensity > _config.BloomThreshold && !bloom.IsActive)
            {
                bloom.IsActive = true;
                bloom.StartDay = day;
                bloom.PeakIntensity = bloom.Intensity;
            }
            else if (bloom.IsActive)
            {
                bloom.PeakIntensity = Math.Max(bloom.PeakIntensity, bloom.Intensity);
            }

            // Check for crash condition
            if (bloom.IsActive && bloom.Intensity > _config.CrashThreshold)
            {
                TriggerCrash(zone, bloom, day);
            }

            // Natural decay when not blooming
            if (!bloom.IsActive || bloom.Intensity < _config.BloomThreshold)
            {
                bloom.Intensity *= _config.NaturalDecayRate;
                if (bloom.Intensity < 0.01)
                {
                    bloom.Intensity = 0.0;
                    bloom.IsActive = false;
                }
            }

            // Apply oxygen impact from active bloom
            if (bloom.IsActive)
            {
                ApplyOxygenImpact(zone, bloom);
            }
        }

        /// <summary>
        /// Light factor based on daylength (seasonal).
        /// Peak at summer solstice (day 172), minimum at winter solstice (day 355).
        /// </summary>
        private double CalculateLightFactor(int day)
        {
            int dayOfYear = day % 365;
            // Cosine curve: max at day 172 (June 21), min at day 355 (Dec 21)
            double angle = 2 * Math.PI * (dayOfYear - 172) / 365.0;
            double light = 0.5 + 0.5 * Math.Cos(angle); // 0 to 1
            return Math.Clamp(light, _config.MinLightFactor, 1.0);
        }

        /// <summary>
        /// Temperature factor - optimal range for phytoplankton growth.
        /// </summary>
        private double CalculateTemperatureFactor(double temperature)
        {
            // Optimal: 10-18°C for Norwegian coastal phytoplankton
            if (temperature >= _config.OptimalTempMin && temperature <= _config.OptimalTempMax)
                return 1.0;

            if (temperature < _config.OptimalTempMin)
            {
                double deficit = _config.OptimalTempMin - temperature;
                return Math.Max(_config.MinTempFactor, 1.0 - deficit * _config.ColdPenaltyPerDegree);
            }

            double excess = temperature - _config.OptimalTempMax;
            return Math.Max(_config.MinTempFactor, 1.0 - excess * _config.HeatPenaltyPerDegree);
        }

        /// <summary>
        /// Nutrient factor from feed waste, runoff, and zone nutrient level.
        /// </summary>
        private double CalculateNutrientFactor(Zone zone)
        {
            // Base nutrients from zone
            double zoneNutrients = zone.NutrientLevel;

            // Additional from fish biomass (feed waste)
            double fishNutrients = zone.CurrentFishBiomassKg * _config.NutrientPerKgFish / Math.Max(1000.0, zone.WaterFlowRate);

            // External multipliers
            double totalNutrients = (zoneNutrients + fishNutrients) * zone.ExternalNutrientMultiplier;

            // Michaelis-Menten saturation
            return totalNutrients / (totalNutrients + _config.HalfSaturationNutrient);
        }

        /// <summary>
        /// Stratification factor from temperature gradient.
        /// Stronger stratification = less vertical mixing = bloom retention.
        /// </summary>
        private double CalculateStratificationFactor(Zone zone)
        {
            // Simplified: stronger currents = more mixing = less stratification
            double mixing = zone.CurrentSpeed;
            double stratification = 1.0 / (1.0 + mixing * _config.StratificationMixingFactor);
            return Math.Clamp(stratification, _config.MinStratification, 1.0);
        }

        /// <summary>
        /// Trigger bloom crash event - respiration spike depletes DO.
        /// </summary>
        private void TriggerCrash(Zone zone, BloomState bloom, int day)
        {
            bloom.IsCrashing = true;
            bloom.CrashDay = day;

            // Crash intensity determines DO depletion severity
            double crashSeverity = bloom.PeakIntensity / _config.CarryingCapacity;
            
            // DO drops to <30% of saturation (typically 8 mg/L -> <2.4 mg/L)
            double targetOxygen = zone.WaterChemistry.OxygenO2 * (1.0 - _config.MaxOxygenDepletion * crashSeverity);
            zone.WaterChemistry.OxygenO2 = Math.Max(0.0, targetOxygen);

            // Log crash event
            SimLog.Info($"Plankton bloom crash in zone {zone.Id}: intensity={bloom.PeakIntensity:F2}, DO={zone.WaterChemistry.OxygenO2:F2} mg/L");

            // Reset bloom after crash
            bloom.Intensity *= _config.PostCrashRetention;
            bloom.IsActive = false;
            bloom.IsCrashing = false;
        }

        /// <summary>
        /// Apply oxygen depletion from active bloom respiration.
        /// </summary>
        private void ApplyOxygenImpact(Zone zone, BloomState bloom)
        {
            // Daytime: photosynthesis adds O2, nighttime: respiration consumes O2
            // Net effect: active bloom consumes more O2 than it produces at high biomass
            double respirationRate = bloom.Intensity * _config.RespirationOxygenDrawdown;
            zone.WaterChemistry.OxygenO2 = Math.Max(0.0, zone.WaterChemistry.OxygenO2 - respirationRate);
        }

        /// <summary>
        /// Check if a zone currently has an active bloom.
        /// </summary>
        public bool IsBloomActive(string zoneId)
        {
            return _zoneBlooms.TryGetValue(zoneId, out var bloom) && bloom.IsActive;
        }

        /// <summary>
        /// Check if a zone is experiencing a crash.
        /// </summary>
        public bool IsCrashing(string zoneId)
        {
            return _zoneBlooms.TryGetValue(zoneId, out var bloom) && bloom.IsCrashing;
        }

        /// <summary>
        /// Get current bloom intensity (0-1) for a zone.
        /// </summary>
        public double GetBloomIntensity(string zoneId)
        {
            return _zoneBlooms.TryGetValue(zoneId, out var bloom) ? bloom.Intensity : 0.0;
        }

        /// <summary>
        /// Get bloom state for serialization.
        /// </summary>
        public Dictionary<string, BloomState> GetAllBloomStates()
        {
            return new Dictionary<string, BloomState>(_zoneBlooms);
        }

        /// <summary>
        /// Restore bloom state from serialization.
        /// </summary>
        public void RestoreBloomStates(Dictionary<string, BloomState> states)
        {
            _zoneBlooms.Clear();
            if (states != null)
            {
                foreach (var kvp in states)
                {
                    _zoneBlooms[kvp.Key] = kvp.Value;
                }
            }
        }

        /// <summary>
        /// Internal bloom state per zone.
        /// </summary>
        public class BloomState
        {
            public double Intensity { get; set; } = 0.0;
            public bool IsActive { get; set; } = false;
            public bool IsCrashing { get; set; } = false;
            public int StartDay { get; set; } = -1;
            public int CrashDay { get; set; } = -1;
            public double PeakIntensity { get; set; } = 0.0;
        }

        /// <summary>
        /// Configuration for plankton bloom dynamics.
        /// </summary>
        public class PlanktonBloomConfig
        {
            public bool Enabled { get; set; } = true;
            public double BaseGrowthRate { get; set; } = 0.15; // per day
            public double MaxGrowthRate { get; set; } = 0.5;
            public double CarryingCapacity { get; set; } = 1.0; // normalized 0-1
            public double BloomThreshold { get; set; } = 0.3; // intensity to trigger "bloom"
            public double CrashThreshold { get; set; } = 0.85; // intensity to trigger crash
            public double NaturalDecayRate { get; set; } = 0.95; // per day when not blooming
            public double PostCrashRetention { get; set; } = 0.1; // fraction remaining after crash

            // Light parameters
            public double MinLightFactor { get; set; } = 0.1;

            // Temperature parameters
            public double OptimalTempMin { get; set; } = 10.0;
            public double OptimalTempMax { get; set; } = 18.0;
            public double ColdPenaltyPerDegree { get; set; } = 0.08;
            public double HeatPenaltyPerDegree { get; set; } = 0.1;
            public double MinTempFactor { get; set; } = 0.1;

            // Nutrient parameters
            public double NutrientPerKgFish { get; set; } = 0.001; // mg/L per kg fish/day
            public double HalfSaturationNutrient { get; set; } = 1.0; // mg/L

            // Stratification
            public double StratificationMixingFactor { get; set; } = 5.0;
            public double MinStratification { get; set; } = 0.2;

            // Oxygen impact
            public double RespirationOxygenDrawdown { get; set; } = 0.5; // mg/L per day per intensity unit
            public double MaxOxygenDepletion { get; set; } = 0.75; // max 75% DO depletion during crash
        }
    }
}