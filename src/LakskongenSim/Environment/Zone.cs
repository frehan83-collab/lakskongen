using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Represents a management zone in the simulation with water chemistry integration.
    /// </summary>
    public class Zone
    {
        public string Id { get; init; }
        public string Name { get; set; } = "";
        public double CarryingCapacityTonnes { get; set; }
        public WaterChemistry WaterChemistry { get; set; }
        public double CurrentFishBiomassKg { get; set; }
        
        /// <summary>Current stocking density in kg/m³ (calculated from biomass and pen volume).</summary>
        public double CurrentStockingDensity { get; set; }
        
        // Zone-specific parameters
        public double WaterFlowRate { get; set; } // m³/day (current speed affects dilution)
        public double FeedConversionRatio { get; set; } // FCR for fish in this zone
        
        // Environmental parameters (added for currents, eutrophication, algal blooms)
        public double CurrentSpeed { get; set; } // m/s (ocean current velocity)
        public double NutrientLevel { get; set; } // mg/L (nitrogen + phosphorus compounds)
        public bool AlgaeBloomActive { get; set; } // true when harmful algal bloom is occurring
        public double JellyfishDensity { get; set; } // count per cubic meter
        public double ExternalRiskMultiplier { get; set; } = 1.0; // production-area / locality pressure from public data
        public double ExternalNutrientMultiplier { get; set; } = 1.0; // regional environmental pressure multiplier
        public double ExternalDiseasePressure { get; set; } = 1.0;
        public double ExternalTemperatureStress { get; set; } = 1.0;
        public double ExternalTreatmentIntensity { get; set; } = 1.0;
        /// <summary>Cumulative simulated days in this zone; drives seasonality.</summary>
        public long ElapsedDays { get; private set; }

        /// <summary>Parameterless constructor for JSON deserialization.</summary>
        public Zone() : this("") { }

        public Zone(string id, double temperature = 10.0)
        {
            Id = id;
            CarryingCapacityTonnes = 1000.0;
            WaterChemistry = new WaterChemistry(temperature);
            CurrentFishBiomassKg = 0.0;
            WaterFlowRate = 1000.0; // Moderate flow
            FeedConversionRatio = 1.2; // Typical Norwegian FCR
            
            // Initialize environmental parameters
            CurrentSpeed = 0.15; // typical moderate current (m/s)
            NutrientLevel = 0.5; // low baseline nutrient level (mg/L)
            AlgaeBloomActive = false;
            JellyfishDensity = 0.0;
        }

        public void AdvanceDays(int days)
        {
            for (int day = 0; day < days; day++)
            {
                ElapsedDays++;
                // Update water chemistry based on current fish biomass
                WaterChemistry.UpdateOneDay(CurrentFishBiomassKg, FeedConversionRatio, WaterFlowRate);

                // Update ocean currents (seasonal/weather variation).
                // Uses cumulative ElapsedDays so the seasonal cycle advances
                // across ticks instead of restarting every call.
                UpdateCurrents((int)(ElapsedDays % 365));
                
                // Calculate nutrient load from fish waste and feed
                double dailyNutrientLoad = CalculateDailyNutrientLoad();
                UpdateEutrophication(dailyNutrientLoad);
            }
        }
        
        /// <summary>
        /// Calculate daily nutrient load from fish biomass and feeding.
        /// </summary>
        private double CalculateDailyNutrientLoad()
        {
            // Estimate: 1 kg fish produces ~0.05 kg N+P per day (depends on FCR and feed composition)
            double baseNutrientPerKg = 0.05 * ExternalNutrientMultiplier;
            return CurrentFishBiomassKg * baseNutrientPerKg / WaterFlowRate; // diluted by water flow
        }
        
        // Instance random for this zone (seeded once)
        private Random? _zoneRandom;

        /// <summary>
        /// Simulate ocean current changes based on season and weather patterns.
        /// Currents affect nutrient dilution and oxygen replenishment.
        /// </summary>
        public void UpdateCurrents(int day)
        {
            // Seasonal variation: stronger currents in winter (Norwegian coast pattern)
            int dayOfYear = day % 365;
            double seasonalFactor = 1.0 + 0.3 * Math.Cos(2 * Math.PI * (dayOfYear - 15) / 365.0); // peak in January
            
            // Initialize zone-specific random once (deterministic seed per zone ID)
            _zoneRandom ??= new Random(Id.GetHashCode());
            
            // Add some random weather-driven variation
            // Use a deterministic hash per day to avoid creating new Random every call
            int dayHash = HashCode.Combine(day, Id);
            double weatherNoise = 0.8 + 0.4 * ((dayHash & 0x7FFFFFFF) % 1000) / 1000.0; // 0.8-1.2x deterministic
            
            // Base current: 0.05-0.35 m/s typical for Norwegian coastal waters
            double baseSpeed = 0.15;
            CurrentSpeed = baseSpeed * seasonalFactor * weatherNoise;
            CurrentSpeed = Math.Clamp(CurrentSpeed, 0.05, 0.5);
            
            // Strong currents improve water quality (more dilution, oxygen replenishment)
            WaterFlowRate = 500.0 + CurrentSpeed * 3000.0; // scale flow rate with current speed
        }
        
        /// <summary>
        /// Model nutrient buildup and trigger algal blooms when thresholds are exceeded.
        /// </summary>
        /// <param name="nutrientLoad">Daily nutrient input (mg/L equivalent)</param>
        public void UpdateEutrophication(double nutrientLoad)
        {
            // Nutrient accumulation
            NutrientLevel += nutrientLoad;
            
            // Natural dissipation via currents and biological uptake
            double dissipationRate = 0.1 * CurrentSpeed; // stronger currents flush nutrients
            NutrientLevel -= NutrientLevel * dissipationRate;
            NutrientLevel = Math.Max(0.0, NutrientLevel);
            
            // Algal bloom threshold (typical: >2.0 mg/L total N+P for coastal blooms)
            const double bloomThreshold = 2.0;
            const double bloomClearThreshold = 1.2; // hysteresis to prevent flickering
            
            if (!AlgaeBloomActive && NutrientLevel > bloomThreshold)
            {
                AlgaeBloomActive = true;
            }
            else if (AlgaeBloomActive && NutrientLevel < bloomClearThreshold)
            {
                AlgaeBloomActive = false;
            }
            
            // Active blooms deplete oxygen (via decomposition)
            if (AlgaeBloomActive)
            {
                // Reduce oxygen by 1-2 mg/L per day during bloom
                WaterChemistry.OxygenO2 = Math.Max(0.0, WaterChemistry.OxygenO2 - 1.5);
            }
        }
        
        /// <summary>
        /// Calculate growth penalty multiplier (0.0-1.0) based on environmental stressors.
        /// 1.0 = optimal conditions, &lt;1.0 = reduced growth.
        /// </summary>
        public double CalculateGrowthPenalty()
        {
            double penalty = 1.0;
            
            // Low oxygen penalty (critical below 5 mg/L)
            if (WaterChemistry.OxygenO2 < 7.0)
            {
                double oxygenPenalty = WaterChemistry.OxygenO2 / 7.0;
                penalty *= Math.Max(0.3, oxygenPenalty); // minimum 30% growth if very low O2
            }
            
            // High temperature penalty (salmon optimal: 8-14°C)
            double temp = WaterChemistry.Temperature;
            if (temp > 14.0)
            {
                double tempExcess = temp - 14.0;
                double tempPenalty = Math.Max(0.4, 1.0 - tempExcess * 0.08); // -8% per degree above 14°C
                penalty *= tempPenalty;
            }
            else if (temp < 8.0)
            {
                double tempDeficit = 8.0 - temp;
                double tempPenalty = Math.Max(0.6, 1.0 - tempDeficit * 0.05); // -5% per degree below 8°C
                penalty *= tempPenalty;
            }

            penalty /= Math.Clamp(ExternalTemperatureStress, 0.85, 1.30);
            
            // Algal bloom penalty (toxins, reduced light, oxygen depletion)
            if (AlgaeBloomActive)
            {
                penalty *= 0.6; // 40% growth reduction during harmful algal bloom
            }
            
            // Jellyfish density penalty (compete for food, sting fish)
            if (JellyfishDensity > 0.5)
            {
                double jellyfishPenalty = Math.Max(0.7, 1.0 - JellyfishDensity * 0.1);
                penalty *= jellyfishPenalty;
            }

            penalty /= Math.Max(0.75, ExternalRiskMultiplier);
            penalty /= Math.Clamp(ExternalDiseasePressure, 0.85, 1.40);
            penalty /= Math.Clamp(1.0 + ((ExternalTreatmentIntensity - 1.0) * 0.10), 0.90, 1.15);
            return Math.Clamp(penalty, 0.0, 1.0);
        }
        
        /// <summary>
        /// Get current zone status including water chemistry and carrying capacity.
        /// </summary>
        public ZoneStatus GetStatus()
        {
            var chemStatus = WaterChemistry.GetStatus();
            return new ZoneStatus
            {
                Id = Id,
                CarryingCapacityTonnes = CarryingCapacityTonnes,
                CurrentFishBiomassKg = CurrentFishBiomassKg,
                UtilizationPercent = CurrentFishBiomassKg / (CarryingCapacityTonnes * 1000.0) * 100.0,
                WaterChemistry = chemStatus,
                WaterFlowRate = WaterFlowRate,
                FeedConversionRatio = FeedConversionRatio
            };
        }
        
        /// <summary>
        /// Set zone temperature (affects water chemistry rates).
        /// </summary>
        public void SetTemperature(double temperature)
        {
            WaterChemistry.SetTemperature(temperature);
        }
    }
    
    /// <summary>
    /// Zone status snapshot for UI display and player decisions.
    /// </summary>
    public struct ZoneStatus
    {
        public string Id;
        public double CarryingCapacityTonnes;
        public double CurrentFishBiomassKg;
        public double UtilizationPercent;
        public WaterChemistryStatus WaterChemistry;
        public double WaterFlowRate;
        public double FeedConversionRatio;
    }
}