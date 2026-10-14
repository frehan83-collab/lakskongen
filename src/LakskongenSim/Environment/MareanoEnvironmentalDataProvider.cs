using System;
using System.Collections.Generic;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Provider for Mareano/Geonorge environmental data integration.
    /// Supplies zone-specific environmental parameters to the simulation based on
    /// real Norwegian coastal oceanographic data.
    /// 
    /// This service:
    /// 1. Loads environmental profiles for all 13 Norwegian production zones
    /// 2. Provides time-varying environmental data (daily resolution)
    /// 3. Calculates environmental stress indices for fish growth and health
    /// 4. Interfaces with Zone.cs to set external multipliers
    /// </summary>
    public class MareanoEnvironmentalDataProvider
    {
        private readonly Dictionary<string, EnvironmentalDataPoint> _currentZoneData;
        private readonly Dictionary<string, Queue<EnvironmentalDataPoint>> _forecastCache;
        private int _currentDayOfYear;
        private DateTime _currentDate;
        
        /// <summary>
        /// Event fired when environmental data is updated for any zone.
        /// </summary>
        public event Action<string, EnvironmentalDataPoint>? OnZoneEnvironmentUpdated;
        
        /// <summary>
        /// Initialize the environmental data provider.
        /// </summary>
        /// <param name="startDate">Simulation start date.</param>
        public MareanoEnvironmentalDataProvider(DateTime? startDate = null)
        {
            _currentDate = startDate ?? DateTime.UtcNow;
            _currentDayOfYear = _currentDate.DayOfYear;
            _currentZoneData = new Dictionary<string, EnvironmentalDataPoint>();
            _forecastCache = new Dictionary<string, Queue<EnvironmentalDataPoint>>();
            
            // Initialize data for all 13 production zones
            InitializeZoneData();
        }
        
        /// <summary>
        /// Initialize environmental data for all Norwegian production zones.
        /// </summary>
        private void InitializeZoneData()
        {
            string[] zones = { "PO01", "PO02", "PO03", "PO04", "PO05", "PO06", 
                              "PO07", "PO08", "PO09", "PO10", "PO11", "PO12", "PO13" };
            
            foreach (var zoneId in zones)
            {
                var profile = NorwegianZoneEnvironmentalProfile.GetProfileForDay(zoneId, _currentDayOfYear);
                profile.Timestamp = _currentDate;
                _currentZoneData[zoneId] = profile;
                _forecastCache[zoneId] = new Queue<EnvironmentalDataPoint>();
                
                // Pre-cache 7-day forecast
                CacheForecast(zoneId, 7);
            }
        }
        
        /// <summary>
        /// Advance the simulation by one day and update all zone environmental data.
        /// </summary>
        public void AdvanceOneDay()
        {
            _currentDate = _currentDate.AddDays(1);
            _currentDayOfYear = _currentDate.DayOfYear;
            
            foreach (var zoneId in _currentZoneData.Keys)
            {
                // Check if we have cached forecast data
                if (_forecastCache[zoneId].Count > 0)
                {
                    _currentZoneData[zoneId] = _forecastCache[zoneId].Dequeue();
                }
                else
                {
                    // Generate new data point
                    var profile = NorwegianZoneEnvironmentalProfile.GetProfileForDay(zoneId, _currentDayOfYear);
                    profile.Timestamp = _currentDate;
                    _currentZoneData[zoneId] = profile;
                }
                
                // Fire update event
                OnZoneEnvironmentUpdated?.Invoke(zoneId, _currentZoneData[zoneId]);
            }
            
            // Replenish forecast cache
            foreach (var zoneId in _currentZoneData.Keys)
            {
                if (_forecastCache[zoneId].Count < 3)
                {
                    CacheForecast(zoneId, 7);
                }
            }
        }
        
        /// <summary>
        /// Advance multiple days.
        /// </summary>
        public void AdvanceDays(int days)
        {
            for (int i = 0; i < days; i++)
            {
                AdvanceOneDay();
            }
        }
        
        /// <summary>
        /// Get current environmental data for a specific zone.
        /// </summary>
        public EnvironmentalDataPoint GetCurrentData(string zoneId)
        {
            if (_currentZoneData.TryGetValue(zoneId, out var data))
            {
                return data;
            }
            
            // Return default data for unknown zones
            return new EnvironmentalDataPoint
            {
                ZoneId = zoneId,
                Timestamp = _currentDate,
                SurfaceTemperature = 10.0,
                OxygenAtCageDepth = 8.0,
                CurrentSpeedAtCageDepth = 0.15,
                EnvironmentalQualityScore = 0.85
            };
        }
        
        /// <summary>
        /// Get environmental data for all zones.
        /// </summary>
        public IReadOnlyDictionary<string, EnvironmentalDataPoint> GetAllCurrentData()
        {
            return _currentZoneData;
        }
        
        /// <summary>
        /// Get forecast for a zone (up to 7 days).
        /// </summary>
        public List<EnvironmentalDataPoint> GetForecast(string zoneId, int days)
        {
            var forecast = new List<EnvironmentalDataPoint>();
            
            if (!_forecastCache.ContainsKey(zoneId))
                return forecast;
            
            var cacheArray = _forecastCache[zoneId].ToArray();
            for (int i = 0; i < Math.Min(days, cacheArray.Length); i++)
            {
                forecast.Add(cacheArray[i]);
            }
            
            return forecast;
        }
        
        /// <summary>
        /// Apply environmental data to a Zone object, setting all external multipliers.
        /// This is the main integration point with the simulation.
        /// </summary>
        public void ApplyToZone(string zoneId, Zone zone)
        {
            if (!_currentZoneData.TryGetValue(zoneId, out var data))
                return;
            
            var riskProfile = NorwegianZoneEnvironmentalProfile.GetRiskProfile(zoneId);
            
            // Set Zone's water chemistry from environmental data
            // The data layer's temperature is the zone's own (it no longer
            // follows the shared sea temperature, S1-01).
            zone.SetTemperature(data.TemperatureAtCageDepth);
            zone.WaterChemistry.OxygenO2 = data.OxygenAtCageDepth;
            zone.WaterChemistry.pH = data.pH;
            
            // Set current speed (affects water flow and nutrient dilution)
            zone.CurrentSpeed = data.CurrentSpeedAtCageDepth;
            zone.WaterFlowRate = 500.0 + data.CurrentSpeedAtCageDepth * 3000.0;
            
            // Apply environmental risk multipliers to zone
            // These are used by MortalityModel, DiseaseSystem, and growth calculations
            zone.ExternalRiskMultiplier = CalculateExternalRiskMultiplier(data, riskProfile);
            zone.ExternalNutrientMultiplier = riskProfile.AlgalBloomRisk;
            zone.ExternalDiseasePressure = data.OxygenStressIndex > 0.3 ? 1.2 : riskProfile.DiseasePressureMultiplier;
            zone.ExternalTemperatureStress = 1.0 + data.TemperatureStressIndex * 0.3;
            zone.ExternalTreatmentIntensity = 1.0; // Baseline, modified by player actions
        }
        
        /// <summary>
        /// Calculate overall external risk multiplier for a zone based on environmental conditions.
        /// </summary>
        private double CalculateExternalRiskMultiplier(EnvironmentalDataPoint data, ZoneEnvironmentalRiskProfile riskProfile)
        {
            double baseRisk = 1.0;
            
            // Temperature stress contribution
            baseRisk *= 1.0 + data.TemperatureStressIndex * 0.2 * riskProfile.TemperatureStressMultiplier;
            
            // Oxygen stress contribution
            baseRisk *= 1.0 + data.OxygenStressIndex * 0.3;
            
            // Current stress contribution
            baseRisk *= 1.0 + data.CurrentStressIndex * 0.15;
            
            // Apply zone-specific risk profile
            baseRisk *= riskProfile.DiseasePressureMultiplier;
            baseRisk *= riskProfile.JellyfishRisk;
            
            // Environmental quality benefit (good conditions reduce risk)
            baseRisk /= Math.Max(0.5, data.EnvironmentalQualityScore);
            
            return Math.Clamp(baseRisk, 0.5, 2.0);
        }
        
        /// <summary>
        /// Get growth multiplier for a zone based on environmental conditions.
        /// Applied to fish growth calculations.
        /// </summary>
        public double GetGrowthMultiplier(string zoneId)
        {
            if (!_currentZoneData.TryGetValue(zoneId, out var data))
                return 1.0;
            
            var riskProfile = NorwegianZoneEnvironmentalProfile.GetRiskProfile(zoneId);
            
            // Start with environmental quality score
            double multiplier = data.EnvironmentalQualityScore;
            
            // Apply cold water penalty for northern zones
            multiplier /= riskProfile.ColdWaterGrowthPenalty;
            
            // Apply current advantage (better water exchange = better growth)
            multiplier *= riskProfile.CurrentAdvantage;
            
            return Math.Clamp(multiplier, 0.5, 1.3);
        }
        
        /// <summary>
        /// Get mortality multiplier for a zone based on environmental conditions.
        /// Applied to baseline mortality calculations.
        /// </summary>
        public double GetMortalityMultiplier(string zoneId)
        {
            if (!_currentZoneData.TryGetValue(zoneId, out var data))
                return 1.0;
            
            var riskProfile = NorwegianZoneEnvironmentalProfile.GetRiskProfile(zoneId);
            
            double multiplier = 1.0;
            
            // Oxygen stress increases mortality
            multiplier *= 1.0 + data.OxygenStressIndex * 0.5;
            
            // Temperature stress increases mortality
            multiplier *= 1.0 + data.TemperatureStressIndex * 0.3;
            
            // Apply zone-specific disease pressure
            multiplier *= riskProfile.DiseasePressureMultiplier;
            
            return Math.Clamp(multiplier, 0.7, 2.0);
        }
        
        /// <summary>
        /// Get feed conversion efficiency multiplier for a zone.
        /// Poor environmental conditions reduce feed efficiency.
        /// </summary>
        public double GetFeedConversionMultiplier(string zoneId)
        {
            if (!_currentZoneData.TryGetValue(zoneId, out var data))
                return 1.0;
            
            // FCR increases (worse) with environmental stress
            // Return multiplier for FCR (1.0 = baseline, >1 = worse FCR)
            double stressFactor = (data.TemperatureStressIndex + data.OxygenStressIndex) / 2.0;
            return 1.0 + stressFactor * 0.2;
        }
        
        /// <summary>
        /// Pre-cache forecast data for a zone.
        /// </summary>
        private void CacheForecast(string zoneId, int days)
        {
            var forecastStartDay = _currentDayOfYear;
            
            for (int i = 1; i <= days; i++)
            {
                int forecastDay = ((forecastStartDay + i - 1) % 365) + 1;
                var profile = NorwegianZoneEnvironmentalProfile.GetProfileForDay(zoneId, forecastDay);
                profile.Timestamp = _currentDate.AddDays(i);
                _forecastCache[zoneId].Enqueue(profile);
            }
        }
        
        /// <summary>
        /// Get current simulation date.
        /// </summary>
        public DateTime CurrentDate => _currentDate;
        
        /// <summary>
        /// Get current day of year (1-365).
        /// </summary>
        public int CurrentDayOfYear => _currentDayOfYear;
        
        /// <summary>
        /// Get summary statistics for all zones.
        /// </summary>
        public ZoneEnvironmentSummary GetSummary()
        {
            double avgQuality = 0;
            double avgTemp = 0;
            double avgOxygen = 0;
            int count = 0;
            
            foreach (var data in _currentZoneData.Values)
            {
                avgQuality += data.EnvironmentalQualityScore;
                avgTemp += data.TemperatureAtCageDepth;
                avgOxygen += data.OxygenAtCageDepth;
                count++;
            }
            
            if (count > 0)
            {
                avgQuality /= count;
                avgTemp /= count;
                avgOxygen /= count;
            }
            
            return new ZoneEnvironmentSummary
            {
                AverageEnvironmentalQuality = avgQuality,
                AverageTemperature = avgTemp,
                AverageOxygen = avgOxygen,
                ZoneCount = count,
                Date = _currentDate
            };
        }
    }
    
    /// <summary>
    /// Summary of environmental conditions across all zones.
    /// </summary>
    public struct ZoneEnvironmentSummary
    {
        public double AverageEnvironmentalQuality;
        public double AverageTemperature;
        public double AverageOxygen;
        public int ZoneCount;
        public DateTime Date;
    }
}
