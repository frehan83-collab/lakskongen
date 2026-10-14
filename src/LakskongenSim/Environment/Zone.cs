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
        /// <summary>Effective mixed water volume (m³) for concentration math.
        /// Phase 1 wave 6: without a volume, fish-driven mass inputs were added
        /// straight to mg/L, collapsing dissolved oxygen to 0 under any real
        /// biomass. 500,000 m³ ≈ a flushed multi-pen zone.</summary>
        public double WaterVolumeM3 { get; set; } = 500_000.0;
        
        // Environmental parameters (added for currents, eutrophication, algal blooms)
        public double CurrentSpeed { get; set; } // m/s (ocean current velocity)
        public double NutrientLevel { get; set; } // mg/L (nitrogen + phosphorus compounds)
        public bool AlgaeBloomActive { get; set; } // true when harmful algal bloom is occurring
        /// <summary>Days an algal bloom persists even if nutrients fall (algae hold
        /// them in biomass). Saved so a reload does not end a running bloom.</summary>
        public int AlgalBloomDaysRemaining { get; set; }
        public double JellyfishDensity { get; set; } // count per cubic meter
        public double ExternalRiskMultiplier { get; set; } = 1.0; // production-area / locality pressure from public data
        public double ExternalNutrientMultiplier { get; set; } = 1.0; // regional environmental pressure multiplier
        public double ExternalDiseasePressure { get; set; } = 1.0;
        public double ExternalTemperatureStress { get; set; } = 1.0;
        public double ExternalTreatmentIntensity { get; set; } = 1.0;
        /// <summary>Cumulative simulated days in this zone; drives seasonality.</summary>
        /// <summary>Cumulative simulated days; public setter so saves restore it
        /// (System.Text.Json skips private setters — a load used to restart
        /// the seasonal cycle at day 0, Phase 1 wave 6).</summary>
        public long ElapsedDays { get; set; }

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
                WaterChemistry.UpdateOneDay(CurrentFishBiomassKg, FeedConversionRatio, WaterFlowRate, WaterVolumeM3);

                // Update ocean currents (seasonal/weather variation).
                // Uses cumulative ElapsedDays so the seasonal cycle advances
                // across ticks instead of restarting every call.
                UpdateCurrents(Core.SimCalendar.DayOfYear((int)ElapsedDays));
                
                // Calculate nutrient load from fish waste and feed
                double dailyNutrientLoad = CalculateDailyNutrientLoad();
                UpdateEutrophication(dailyNutrientLoad);
            }
        }
        
        /// <summary>
        /// Daily dissolved N+P input from the zone's fish, as a concentration
        /// increment (mg/L/day) over the mixed volume. The old load was 0.05 kg
        /// per kg fish per day divided by the flow index — not a concentration,
        /// about 300x the real release — and drove NutrientLevel to ~1,500 mg/L.
        /// </summary>
        private double CalculateDailyNutrientLoad()
        {
            double volumeL = Math.Max(1.0, WaterVolumeM3 * 1000.0);
            return CurrentFishBiomassKg * FishNutrientReleaseGPerKgDay * 1000.0 / volumeL * ExternalNutrientMultiplier;
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

        // Eutrophication tuning. Thresholds and bloom O2 drawdown unchanged; the
        // load and flushing are now a concentration balance.
        /// <summary>Dissolved N+P released by farmed salmon, g per kg standing
        /// biomass per day (~50 kg N + ~8 kg P per tonne produced a year).</summary>
        private const double FishNutrientReleaseGPerKgDay = 0.15;
        private const double BackgroundNutrientMgPerL = 0.5; // regional background (constructor baseline)
        private const double NutrientUptakeRatePerDay = 0.1; // biological uptake
        /// <summary>How long a bloom persists once triggered (days).</summary>
        public const int DefaultAlgalBloomDays = 14;
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
            int dayOfYear = Core.SimCalendar.DayOfYear(day);
            double seasonalFactor = 1.0 + SeasonalCurrentAmplitude * Math.Cos(2 * Math.PI * (dayOfYear - WinterCurrentPeakDay) / Core.SimCalendar.DaysPerYear); // peak in January

            // Weather-driven variation from a process-stable hash of (zone, day):
            // HashCode.Combine is randomized per process, which made the same
            // seed diverge between launches and across save/load (F453).
            double weatherNoise = WeatherNoiseMin + WeatherNoiseRange * Core.StableHash.Unit01(Core.StableHash.Of(Id, day)); // 0.8-1.2x

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
            // Tidal exchange flushes the zone toward the regional background and
            // algae take nutrients up; integrated exactly like the water chemistry.
            // (The old 0.1 × current decay, 1.5%/day, against an un-normalized load
            // kept a bloom active on ~600 of 600 days.)
            double exchange = WaterChemistry.ExchangeRatePerDay(WaterFlowRate);
            double background = BackgroundNutrientMgPerL * ExternalNutrientMultiplier;
            NutrientLevel = WaterChemistry.RelaxOneDay(NutrientLevel,
                nutrientLoad + exchange * background, exchange + NutrientUptakeRatePerDay);
            NutrientLevel = Math.Max(0.0, NutrientLevel);

            // Algal bloom threshold (typical: >2.0 mg/L total N+P for coastal blooms);
            // a bloom runs its course before the hysteresis can clear it.
            if (AlgalBloomDaysRemaining > 0)
                AlgalBloomDaysRemaining--;
            if (!AlgaeBloomActive && NutrientLevel > AlgalBloomThresholdMgPerL)
            {
                TriggerAlgalBloom();
            }
            else if (AlgaeBloomActive && AlgalBloomDaysRemaining <= 0 && NutrientLevel < AlgalBloomClearMgPerL)
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
        /// Start (or extend) a harmful algal bloom in this zone for at least
        /// <paramref name="days"/> days.
        /// </summary>
        public void TriggerAlgalBloom(int days = DefaultAlgalBloomDays)
        {
            AlgaeBloomActive = true;
            AlgalBloomDaysRemaining = Math.Max(AlgalBloomDaysRemaining, days);
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
        /// True while the zone's water takes its production area's sea
        /// temperature every day (<see cref="TakeSeaTemperature"/>, S1-01/S2-02).
        /// A zone given its own temperature through <see cref="SetTemperature"/>
        /// (a scenario, a data layer or a test) keeps it. Saved.
        /// </summary>
        public bool FollowsSeaTemperature { get; set; } = true;

        /// <summary>
        /// Production area (1-13, PO1-PO13) the zone lies in (S2-02). 0 until
        /// assigned: a scenario sets it for its sites; otherwise the engine
        /// resolves it on the first day from an id such as "PO04" or else the
        /// game's home area, and keeps it. Saved; legacy saves load as 0 and are
        /// resolved the same way.
        /// </summary>
        public int ProductionArea { get; set; }

        /// <summary>
        /// Give the zone its own water temperature (affects water chemistry rates
        /// and oxygen solubility). The zone stops following the sea temperature.
        /// </summary>
        public void SetTemperature(double temperature)
        {
            FollowsSeaTemperature = false;
            WaterChemistry.SetTemperature(temperature);
        }

        /// <summary>
        /// Today's sea temperature reaches the zone's water unless the zone has a
        /// temperature of its own. Called by the engine before the chemistry runs.
        /// </summary>
        public void TakeSeaTemperature(double seaTemperatureC)
        {
            if (FollowsSeaTemperature)
                WaterChemistry.SetTemperature(seaTemperatureC);
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