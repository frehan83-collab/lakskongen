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
        
        // Ocean-current tuning (Norwegian coastal waters; values unchanged).
        private const double BaseCurrentSpeedMs = 0.15; // typical moderate current
        private const double SeasonalCurrentAmplitude = 0.3; // ±30% winter/summer swing
        private const int WinterCurrentPeakDay = 15; // mid-January peak
        private const double WeatherNoiseMin = 0.8; // deterministic 0.8-1.2x weather band
        private const double WeatherNoiseRange = 0.4;
        private const double MinCurrentSpeedMs = 0.05;
        private const double MaxCurrentSpeedMs = 0.5;
        private const double FlowRateBaseM3PerDay = 500.0;
        private const double FlowRatePerCurrentMs = 3000.0; // flow scales with current

        // Eutrophication tuning (values unchanged).
        private const double AlgalBloomThresholdMgPerL = 2.0; // bloom trigger (total N+P)
        private const double AlgalBloomClearMgPerL = 1.2; // hysteresis: clears below this
        private const double BloomOxygenDepletionMgPerL = 1.5; // daily O2 drawdown during bloom

        // Growth-penalty tuning (values unchanged).
        private const double OxygenReferenceMgPerL = 7.0; // penalty below this O2 level
        private const double MinOxygenGrowthFactor = 0.3; // floor: 30% growth at ~0 O2
        private const double OptimalTempHighC = 14.0;
        private const double OptimalTempLowC = 8.0;
        private const double HeatPenaltyPerDegree = 0.08; // −8% growth per °C above 14
        private const double MinHeatGrowthFactor = 0.4;
        private const double ColdPenaltyPerDegree = 0.05; // −5% growth per °C below 8
        private const double MinColdGrowthFactor = 0.6;
        private const double AlgalBloomGrowthFactor = 0.6; // 40% reduction during blooms
        private const double JellyfishDensityThreshold = 0.5;
        private const double JellyfishPenaltyPerUnit = 0.1;
        private const double MinJellyfishGrowthFactor = 0.7;

        /// <summary>
        /// Simulate ocean current changes based on season and weather patterns.
        /// Currents affect nutrient dilution and oxygen replenishment.
        /// Deterministic per (zone, day): no RNG instance is allocated or stored
        /// (TD-12) — a hash of the day and zone ID drives weather variation.
        /// </summary>
        public void UpdateCurrents(int day)
        {
            // Seasonal variation: stronger currents in winter (Norwegian coast pattern)
            int dayOfYear = day % 365;
            double seasonalFactor = 1.0 + SeasonalCurrentAmplitude * Math.Cos(2 * Math.PI * (dayOfYear - WinterCurrentPeakDay) / 365.0); // peak in January

            // Add some random weather-driven variation
            // Use a deterministic hash per day to avoid creating new Random every call
            int dayHash = HashCode.Combine(day, Id);
            double weatherNoise = WeatherNoiseMin + WeatherNoiseRange * ((dayHash & 0x7FFFFFFF) % 1000) / 1000.0; // 0.8-1.2x deterministic

            // Base current: 0.05-0.35 m/s typical for Norwegian coastal waters
            CurrentSpeed = BaseCurrentSpeedMs * seasonalFactor * weatherNoise;
            CurrentSpeed = Math.Clamp(CurrentSpeed, MinCurrentSpeedMs, MaxCurrentSpeedMs);

            // Strong currents improve water quality (more dilution, oxygen replenishment)
            WaterFlowRate = FlowRateBaseM3PerDay + CurrentSpeed * FlowRatePerCurrentMs; // scale flow rate with current speed
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
            if (!AlgaeBloomActive && NutrientLevel > AlgalBloomThresholdMgPerL)
            {
                AlgaeBloomActive = true;
            }
            else if (AlgaeBloomActive && NutrientLevel < AlgalBloomClearMgPerL)
            {
                AlgaeBloomActive = false;
            }

            // Active blooms deplete oxygen (via decomposition)
            if (AlgaeBloomActive)
            {
                // Reduce oxygen by 1-2 mg/L per day during bloom
                WaterChemistry.OxygenO2 = Math.Max(0.0, WaterChemistry.OxygenO2 - BloomOxygenDepletionMgPerL);
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
            if (WaterChemistry.OxygenO2 < OxygenReferenceMgPerL)
            {
                double oxygenPenalty = WaterChemistry.OxygenO2 / OxygenReferenceMgPerL;
                penalty *= Math.Max(MinOxygenGrowthFactor, oxygenPenalty); // minimum 30% growth if very low O2
            }
            
            // High temperature penalty (salmon optimal: 8-14°C)
            double temp = WaterChemistry.Temperature;
            if (temp > OptimalTempHighC)
            {
                double tempExcess = temp - OptimalTempHighC;
                double tempPenalty = Math.Max(MinHeatGrowthFactor, 1.0 - tempExcess * HeatPenaltyPerDegree); // -8% per degree above 14°C
                penalty *= tempPenalty;
            }
            else if (temp < OptimalTempLowC)
            {
                double tempDeficit = OptimalTempLowC - temp;
                double tempPenalty = Math.Max(MinColdGrowthFactor, 1.0 - tempDeficit * ColdPenaltyPerDegree); // -5% per degree below 8°C
                penalty *= tempPenalty;
            }

            penalty /= Math.Clamp(ExternalTemperatureStress, 0.85, 1.30);
            
            // Algal bloom penalty (toxins, reduced light, oxygen depletion)
            if (AlgaeBloomActive)
            {
                penalty *= AlgalBloomGrowthFactor; // 40% growth reduction during harmful algal bloom
            }
            
            // Jellyfish density penalty (compete for food, sting fish)
            if (JellyfishDensity > JellyfishDensityThreshold)
            {
                double jellyfishPenalty = Math.Max(MinJellyfishGrowthFactor, 1.0 - JellyfishDensity * JellyfishPenaltyPerUnit);
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