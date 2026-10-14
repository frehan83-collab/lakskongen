using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Simplified 3D water column physics model for Norwegian fjord simulation.
    /// Models temperature stratification with seasonal thermocline, dissolved oxygen
    /// variation with depth and biological oxygen demand, and current speed profiles.
    /// </summary>
    public class WaterColumnPhysics
    {
        // Seasonal temperature parameters (Norwegian fjord)
        private const double SurfaceTempAmplitude = 7.0;  // ±7°C from mean (range 4-18°C)
        private const double SurfaceTempMean = 11.0;      // Mean annual surface temperature
        private const double DeepWaterTempMin = 6.0;      // Deep water minimum temperature
        private const double DeepWaterTempMax = 8.0;      // Deep water maximum temperature
        
        // Thermocline parameters
        private const double ThermoclineDepthSummer = 5.0;   // Summer thermocline depth (m)
        private const double ThermoclineDepthWinter = 20.0;  // Winter thermocline depth (m)
        private const double ThermoclineTransitionDays = 30.0; // Days for transition between seasons
        
        // Oxygen parameters
        private const double SurfaceOxygenSaturation = 10.0; // mg/L at surface (saturated)
        private const double OxygenAttenuationDepth = 50.0;  // Depth where oxygen halves (m)
        private const double BodCoefficient = 0.0001;        // BOD per kg fish biomass per day
        
        // Current speed parameters (m/s)
        private const double SurfaceCurrentCalm = 0.1;       // Calm weather surface current
        private const double SurfaceCurrentModerate = 0.5;   // Moderate weather surface current
        private const double SurfaceCurrentStorm = 1.0;      // Storm weather surface current
        private const double CurrentAttenuationDepth = 30.0; // Depth where current halves (m)
        
        // State variables
        private int _currentDayOfYear;
        private double _totalFishBiomassKg;
        private double _currentThermoclineDepth;
        
        /// <summary>
        /// Initialize water column physics with default values.
        /// </summary>
        public WaterColumnPhysics()
        {
            _currentDayOfYear = 1;  // Start at day 1 (January 1)
            _totalFishBiomassKg = 0.0;
            UpdateThermoclineDepth();
        }
        
        /// <summary>
        /// Initialize water column physics with specific day and biomass.
        /// </summary>
        /// <param name="dayOfYear">Day of year (1-365)</param>
        /// <param name="totalFishBiomassKg">Total fish biomass in kg</param>
        public WaterColumnPhysics(int dayOfYear, double totalFishBiomassKg)
        {
            _currentDayOfYear = Math.Clamp(dayOfYear, 1, 365);
            _totalFishBiomassKg = Math.Max(0.0, totalFishBiomassKg);
            UpdateThermoclineDepth();
        }
        
        /// <summary>
        /// Get water temperature at specified depth and day of year.
        /// Models seasonal thermocline with three layers: surface, mid, deep.
        /// </summary>
        /// <param name="depthM">Depth in meters (0 = surface)</param>
        /// <param name="dayOfYear">Day of year (1-365)</param>
        /// <returns>Water temperature in °C</returns>
        public double GetTemperatureAtDepth(double depthM, int dayOfYear)
        {
            // Calculate seasonal surface temperature (peaks in August, day ~213)
            double surfaceTemp = CalculateSeasonalSurfaceTemperature(dayOfYear);
            
            // Calculate deep water temperature (stable year-round with slight seasonal variation)
            double deepTemp = CalculateDeepWaterTemperature(dayOfYear);
            
            // Get current thermocline depth for this day
            double thermoclineDepth = CalculateThermoclineDepth(dayOfYear);
            
            // Calculate temperature profile with depth
            return CalculateTemperatureProfile(depthM, surfaceTemp, deepTemp, thermoclineDepth);
        }
        
        /// <summary>
        /// Get dissolved oxygen concentration at specified depth, day, and fish biomass.
        /// Models oxygen depletion with depth and biological oxygen demand from fish.
        /// </summary>
        /// <param name="depthM">Depth in meters (0 = surface)</param>
        /// <param name="dayOfYear">Day of year (1-365)</param>
        /// <param name="fishBiomassKg">Fish biomass in kg at this depth/location</param>
        /// <returns>Dissolved oxygen concentration in mg/L</returns>
        public double GetOxygenAtDepth(double depthM, int dayOfYear, double fishBiomassKg)
        {
            // Base oxygen at surface (slightly seasonal due to temperature solubility)
            double surfaceTemp = CalculateSeasonalSurfaceTemperature(dayOfYear);
            double surfaceOxygen = CalculateSurfaceOxygen(surfaceTemp);
            
            // Oxygen depletion with depth (exponential decay)
            double oxygenDepthFactor = Math.Exp(-depthM / OxygenAttenuationDepth);
            double depthOxygen = surfaceOxygen * oxygenDepthFactor;
            
            // Biological oxygen demand from fish biomass
            double bodConsumption = BodCoefficient * fishBiomassKg;
            double oxygenAfterBod = Math.Max(0.0, depthOxygen - bodConsumption);
            
            return oxygenAfterBod;
        }
        
        /// <summary>
        /// Get current speed at specified depth for given weather condition.
        /// </summary>
        /// <param name="depthM">Depth in meters (0 = surface)</param>
        /// <param name="weatherCondition">Weather condition: "calm", "moderate", "storm"</param>
        /// <returns>Current speed in meters per second</returns>
        public double GetCurrentSpeedAtDepth(double depthM, string weatherCondition)
        {
            // Get surface current based on weather condition
            double surfaceCurrent = GetSurfaceCurrentForWeather(weatherCondition);
            
            // Current speed decreases exponentially with depth
            double currentSpeed = surfaceCurrent * Math.Exp(-depthM / CurrentAttenuationDepth);
            
            return currentSpeed;
        }
        
        /// <summary>
        /// Update daily state with new day and total fish biomass.
        /// Call this once per simulation day to update internal state.
        /// </summary>
        /// <param name="dayOfYear">Day of year (1-365)</param>
        /// <param name="totalFishBiomassKg">Total fish biomass in kg</param>
        public void UpdateDaily(int dayOfYear, double totalFishBiomassKg)
        {
            _currentDayOfYear = Math.Clamp(dayOfYear, 1, 365);
            _totalFishBiomassKg = Math.Max(0.0, totalFishBiomassKg);
            UpdateThermoclineDepth();
        }
        
        #region Private Helper Methods
        
        /// <summary>
        /// Calculate seasonal surface temperature using sine wave (peak in August).
        /// </summary>
        private double CalculateSeasonalSurfaceTemperature(int dayOfYear)
        {
            // Convert day of year to radians (365 days in year)
            // Peak temperature at day ~213 (August 1)
            double phaseShift = (213.0 / LakskongenSim.Core.SimCalendar.DaysPerYear) * 2.0 * Math.PI;
            double radians = (2.0 * Math.PI * dayOfYear / LakskongenSim.Core.SimCalendar.DaysPerYear) - phaseShift;
            
            // Sine wave for seasonal variation
            double seasonalVariation = SurfaceTempAmplitude * Math.Sin(radians);
            double surfaceTemp = SurfaceTempMean + seasonalVariation;
            
            // Clamp to Norwegian fjord range (4-18°C)
            return Math.Clamp(surfaceTemp, 4.0, 18.0);
        }
        
        /// <summary>
        /// Calculate deep water temperature (stable with slight seasonal variation).
        /// </summary>
        private double CalculateDeepWaterTemperature(int dayOfYear)
        {
            // Deep water has much smaller seasonal variation
            double phaseShift = (213.0 / LakskongenSim.Core.SimCalendar.DaysPerYear) * 2.0 * Math.PI;
            double radians = (2.0 * Math.PI * dayOfYear / LakskongenSim.Core.SimCalendar.DaysPerYear) - phaseShift;
            
            // Small seasonal variation (±1°C)
            double seasonalVariation = Math.Sin(radians);
            double deepTemp = 7.0 + seasonalVariation; // Mean 7°C
            
            return Math.Clamp(deepTemp, DeepWaterTempMin, DeepWaterTempMax);
        }
        
        /// <summary>
        /// Calculate thermocline depth based on day of year.
        /// Shallow in summer (June-August), deep in winter.
        /// </summary>
        private double CalculateThermoclineDepth(int dayOfYear)
        {
            // Calculate seasonal progression (0=winter, 1=summer)
            // Summer centered around day 213 (August 1)
            double daysFromSummerPeak = Math.Abs(dayOfYear - 213.0);
            if (daysFromSummerPeak > 182.5) // Wrap around year
                daysFromSummerPeak = 365.0 - daysFromSummerPeak;
            
            // Linear transition between winter and summer depths
            double transitionFactor = Math.Clamp(1.0 - (daysFromSummerPeak / ThermoclineTransitionDays), 0.0, 1.0);
            
            // Summer: shallow thermocline (5m), Winter: deep thermocline (20m)
            double thermoclineDepth = ThermoclineDepthWinter + 
                                     (ThermoclineDepthSummer - ThermoclineDepthWinter) * transitionFactor;
            
            return thermoclineDepth;
        }
        
        /// <summary>
        /// Calculate temperature profile with depth using three-layer model.
        /// </summary>
        private double CalculateTemperatureProfile(double depthM, double surfaceTemp, double deepTemp, double thermoclineDepth)
        {
            // Surface layer (0 to thermoclineDepth): well-mixed, uniform temperature
            if (depthM <= thermoclineDepth)
                return surfaceTemp;
            
            // Mid layer (thermoclineDepth to thermoclineDepth+20m): transition layer
            if (depthM <= thermoclineDepth + 20.0)
            {
                double midLayerFraction = (depthM - thermoclineDepth) / 20.0;
                // Exponential transition for realism
                double transitionFactor = 1.0 - Math.Exp(-midLayerFraction * 2.0);
                return surfaceTemp + (deepTemp - surfaceTemp) * transitionFactor;
            }
            
            // Deep layer (>thermoclineDepth+20m): stable deep water temperature
            return deepTemp;
        }
        
        /// <summary>
        /// Calculate surface oxygen concentration based on temperature.
        /// Oxygen solubility decreases with increasing temperature.
        /// </summary>
        private double CalculateSurfaceOxygen(double temperature)
        {
            // Simplified oxygen solubility model (mg/L at 100% saturation)
            // Reference: 14.6 mg/L at 0°C, 7.6 mg/L at 30°C
            double oxygenSaturation = 14.6 - (temperature * 0.233);
            
            // Clamp to realistic range and apply slight randomness for realism
            return Math.Clamp(oxygenSaturation, 7.0, 15.0);
        }
        
        /// <summary>
        /// Get surface current speed based on weather condition.
        /// </summary>
        private double GetSurfaceCurrentForWeather(string weatherCondition)
        {
            return weatherCondition.ToLowerInvariant() switch
            {
                "storm" => SurfaceCurrentStorm,
                "moderate" => SurfaceCurrentModerate,
                "calm" or _ => SurfaceCurrentCalm,
            };
        }
        
        /// <summary>
        /// Update internal thermocline depth based on current day of year.
        /// </summary>
        private void UpdateThermoclineDepth()
        {
            _currentThermoclineDepth = CalculateThermoclineDepth(_currentDayOfYear);
        }
        
        #endregion
        
        #region Public Properties for State Access
        
        /// <summary>
        /// Get current day of year.
        /// </summary>
        public int CurrentDayOfYear => _currentDayOfYear;
        
        /// <summary>
        /// Get total fish biomass in kg.
        /// </summary>
        public double TotalFishBiomassKg => _totalFishBiomassKg;
        
        /// <summary>
        /// Get current thermocline depth in meters.
        /// </summary>
        public double CurrentThermoclineDepth => _currentThermoclineDepth;
        
        #endregion
    }
}