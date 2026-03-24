using System;
using System.Collections.Generic;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Environmental data point from Mareano/Geonorge geospatial layers.
    /// Represents real Norwegian coastal environmental conditions for a specific zone and time.
    /// </summary>
    public class EnvironmentalDataPoint
    {
        /// <summary>Zone identifier (PO 1-13 production zone).</summary>
        public string ZoneId { get; set; } = "";
        
        /// <summary>Timestamp of the data point.</summary>
        public DateTime Timestamp { get; set; }
        
        // Temperature parameters (from Mareano/Institute of Marine Research)
        /// <summary>Sea surface temperature (°C).</summary>
        public double SurfaceTemperature { get; set; }
        
        /// <summary>Temperature at 10m depth (°C).</summary>
        public double TemperatureAt10m { get; set; }
        
        /// <summary>Temperature at 30m depth (°C).</summary>
        public double TemperatureAt30m { get; set; }
        
        /// <summary>Temperature at typical cage depth (0-30m) (°C).</summary>
        public double TemperatureAtCageDepth => (TemperatureAt10m + TemperatureAt30m) / 2.0;
        
        /// <summary>Sea bottom temperature (°C).</summary>
        public double BottomTemperature { get; set; }
        
        /// <summary>Temperature variability coefficient (std dev).</summary>
        public double TemperatureVariability { get; set; }
        
        // Oxygen parameters (from Mareano/Geonorge oceanographic data)
        /// <summary>Dissolved oxygen at surface (mg/L).</summary>
        public double SurfaceOxygen { get; set; }
        
        /// <summary>Dissolved oxygen at 10m depth (mg/L).</summary>
        public double OxygenAt10m { get; set; }
        
        /// <summary>Dissolved oxygen at cage depth (mg/L).</summary>
        public double OxygenAtCageDepth { get; set; }
        
        /// <summary>Oxygen saturation percentage (0.0-1.0).</summary>
        public double OxygenSaturation { get; set; }
        
        // Current parameters (from Norwegian Meteorological Institute/Mareano)
        /// <summary>Average current speed at surface (m/s).</summary>
        public double SurfaceCurrentSpeed { get; set; }
        
        /// <summary>Average current speed at cage depth (m/s).</summary>
        public double CurrentSpeedAtCageDepth { get; set; }
        
        /// <summary>Current direction in degrees (0-360, meteorological convention).</summary>
        public double CurrentDirection { get; set; }
        
        /// <summary>Tidal current amplitude (m/s).</summary>
        public double TidalCurrentAmplitude { get; set; }
        
        // Water quality (from Geonorge/NERA water quality monitoring)
        /// <summary>Salinity (PSU - practical salinity units).</summary>
        public double Salinity { get; set; }
        
        /// <summary>pH level.</summary>
        public double pH { get; set; }
        
        /// <summary>Turbidity (NTU - nephelometric turbidity units).</summary>
        public double Turbidity { get; set; }
        
        /// <summary>Chlorophyll-a concentration (µg/L) - proxy for phytoplankton.</summary>
        public double ChlorophyllA { get; set; }
        
        // Derived environmental stress indices
        /// <summary>Temperature stress index (0.0-1.0, 0 = optimal).</summary>
        public double TemperatureStressIndex { get; set; }
        
        /// <summary>Oxygen stress index (0.0-1.0, 0 = optimal).</summary>
        public double OxygenStressIndex { get; set; }
        
        /// <summary>Current stress index (0.0-1.0, too weak or too strong).</summary>
        public double CurrentStressIndex { get; set; }
        
        /// <summary>Overall environmental quality score (0.0-1.0, 1.0 = optimal).</summary>
        public double EnvironmentalQualityScore { get; set; }
        
        /// <summary>
        /// Calculate derived stress indices from raw environmental parameters.
        /// Uses scientifically-validated thresholds for Atlantic salmon.
        /// </summary>
        public void CalculateDerivedIndices()
        {
            // Temperature stress: optimal 8-14°C, Gaussian penalty outside
            const double optimalTemp = 11.0;
            const double tempTolerance = 3.0;
            double tempDiff = Math.Abs(TemperatureAtCageDepth - optimalTemp);
            TemperatureStressIndex = Math.Min(1.0, tempDiff / (2.0 * tempTolerance));
            
            // Oxygen stress: optimal >8 mg/L, critical <5 mg/L
            const double optimalOxygen = 8.0;
            const double criticalOxygen = 5.0;
            if (OxygenAtCageDepth >= optimalOxygen)
                OxygenStressIndex = 0.0;
            else if (OxygenAtCageDepth <= criticalOxygen)
                OxygenStressIndex = 1.0;
            else
                OxygenStressIndex = (optimalOxygen - OxygenAtCageDepth) / (optimalOxygen - criticalOxygen);
            
            // Current stress: optimal 0.1-0.3 m/s, too weak (<0.05) or too strong (>0.5)
            const double optimalCurrentMin = 0.1;
            const double optimalCurrentMax = 0.3;
            const double weakCurrent = 0.05;
            const double strongCurrent = 0.5;
            
            if (CurrentSpeedAtCageDepth < weakCurrent)
                CurrentStressIndex = 0.5 + 0.5 * (weakCurrent - CurrentSpeedAtCageDepth) / weakCurrent;
            else if (CurrentSpeedAtCageDepth < optimalCurrentMin)
                CurrentStressIndex = (optimalCurrentMin - CurrentSpeedAtCageDepth) / (optimalCurrentMin - weakCurrent) * 0.5;
            else if (CurrentSpeedAtCageDepth <= optimalCurrentMax)
                CurrentStressIndex = 0.0;
            else if (CurrentSpeedAtCageDepth <= strongCurrent)
                CurrentStressIndex = (CurrentSpeedAtCageDepth - optimalCurrentMax) / (strongCurrent - optimalCurrentMax) * 0.5;
            else
                CurrentStressIndex = 0.5 + 0.5 * Math.Min(1.0, (CurrentSpeedAtCageDepth - strongCurrent) / 0.3);
            
            CurrentStressIndex = Math.Clamp(CurrentStressIndex, 0.0, 1.0);
            
            // Overall environmental quality (weighted combination)
            // Temperature: 40%, Oxygen: 40%, Current: 20%
            double stressScore = TemperatureStressIndex * 0.4 + OxygenStressIndex * 0.4 + CurrentStressIndex * 0.2;
            EnvironmentalQualityScore = Math.Max(0.0, 1.0 - stressScore);
        }
        
        /// <summary>
        /// Interpolate between two data points (for smooth transitions between seasons/locations).
        /// </summary>
        public static EnvironmentalDataPoint Interpolate(
            EnvironmentalDataPoint a, 
            EnvironmentalDataPoint b, 
            double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            
            return new EnvironmentalDataPoint
            {
                ZoneId = a.ZoneId,
                Timestamp = a.Timestamp.AddTicks((long)((b.Timestamp - a.Timestamp).Ticks * t)),
                SurfaceTemperature = Lerp(a.SurfaceTemperature, b.SurfaceTemperature, t),
                TemperatureAt10m = Lerp(a.TemperatureAt10m, b.TemperatureAt10m, t),
                TemperatureAt30m = Lerp(a.TemperatureAt30m, b.TemperatureAt30m, t),
                BottomTemperature = Lerp(a.BottomTemperature, b.BottomTemperature, t),
                TemperatureVariability = Lerp(a.TemperatureVariability, b.TemperatureVariability, t),
                SurfaceOxygen = Lerp(a.SurfaceOxygen, b.SurfaceOxygen, t),
                OxygenAt10m = Lerp(a.OxygenAt10m, b.OxygenAt10m, t),
                OxygenAtCageDepth = Lerp(a.OxygenAtCageDepth, b.OxygenAtCageDepth, t),
                OxygenSaturation = Lerp(a.OxygenSaturation, b.OxygenSaturation, t),
                SurfaceCurrentSpeed = Lerp(a.SurfaceCurrentSpeed, b.SurfaceCurrentSpeed, t),
                CurrentSpeedAtCageDepth = Lerp(a.CurrentSpeedAtCageDepth, b.CurrentSpeedAtCageDepth, t),
                CurrentDirection = LerpAngle(a.CurrentDirection, b.CurrentDirection, t),
                TidalCurrentAmplitude = Lerp(a.TidalCurrentAmplitude, b.TidalCurrentAmplitude, t),
                Salinity = Lerp(a.Salinity, b.Salinity, t),
                pH = Lerp(a.pH, b.pH, t),
                Turbidity = Lerp(a.Turbidity, b.Turbidity, t),
                ChlorophyllA = Lerp(a.ChlorophyllA, b.ChlorophyllA, t)
            };
        }
        
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        
        private static double LerpAngle(double a, double b, double t)
        {
            double diff = b - a;
            while (diff > 180) diff -= 360;
            while (diff < -180) diff += 360;
            return a + diff * t;
        }
    }
}
