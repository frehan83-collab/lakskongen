using System;
using System.Collections.Generic;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Environmental profiles for all 13 Norwegian salmon production zones (PO 1-13).
    /// Based on Mareano/Geonorge geospatial data and Institute of Marine Research monitoring.
    /// 
    /// Data sources:
    /// - Mareano: https://www.mareano.no (bathymetry, sediment, oceanography)
    /// - Geonorge: https://www.geonorge.no (marine habitat maps, current data)
    /// - Institute of Marine Research: https://www.hi.no (temperature, oxygen monitoring)
    /// - Norwegian Meteorological Institute: https://www.met.no (current models)
    /// </summary>
    public static class NorwegianZoneEnvironmentalProfile
    {
        /// <summary>
        /// Production Zone definitions with approximate geographic centers.
        /// </summary>
        public static readonly Dictionary<string, ZoneGeography> ZoneGeography = new()
        {
            ["PO01"] = new ZoneGeography { Name = "Østfold", CenterLat = 59.0, CenterLon = 11.0, DepthRange = "20-100m", AvgDepth = 40 },
            ["PO02"] = new ZoneGeography { Name = "Aust-Agder", CenterLat = 58.3, CenterLon = 8.6, DepthRange = "30-150m", AvgDepth = 60 },
            ["PO03"] = new ZoneGeography { Name = "Rogaland", CenterLat = 58.9, CenterLon = 5.7, DepthRange = "50-300m", AvgDepth = 100 },
            ["PO04"] = new ZoneGeography { Name = "Hordaland", CenterLat = 60.2, CenterLon = 5.3, DepthRange = "100-500m", AvgDepth = 200 },
            ["PO05"] = new ZoneGeography { Name = "Sogn og Fjordane", CenterLat = 61.5, CenterLon = 5.0, DepthRange = "100-800m", AvgDepth = 300 },
            ["PO06"] = new ZoneGeography { Name = "Møre og Romsdal", CenterLat = 62.8, CenterLon = 6.5, DepthRange = "150-1000m", AvgDepth = 400 },
            ["PO07"] = new ZoneGeography { Name = "Trøndelag Sør", CenterLat = 64.0, CenterLon = 9.5, DepthRange = "100-600m", AvgDepth = 250 },
            ["PO08"] = new ZoneGeography { Name = "Trøndelag Nord", CenterLat = 65.0, CenterLon = 11.0, DepthRange = "100-500m", AvgDepth = 200 },
            ["PO09"] = new ZoneGeography { Name = "Nordland Sør", CenterLat = 66.0, CenterLon = 12.5, DepthRange = "100-400m", AvgDepth = 180 },
            ["PO10"] = new ZoneGeography { Name = "Nordland Nord", CenterLat = 67.5, CenterLon = 14.0, DepthRange = "80-300m", AvgDepth = 150 },
            ["PO11"] = new ZoneGeography { Name = "Troms", CenterLat = 69.5, CenterLon = 19.0, DepthRange = "50-250m", AvgDepth = 120 },
            ["PO12"] = new ZoneGeography { Name = "Finnmark", CenterLat = 70.5, CenterLon = 23.0, DepthRange = "30-150m", AvgDepth = 80 },
            ["PO13"] = new ZoneGeography { Name = "Vest-Finnmark", CenterLat = 71.0, CenterLon = 26.0, DepthRange = "20-100m", AvgDepth = 50 }
        };
        
        /// <summary>
        /// Get seasonal environmental profile for a specific zone and season.
        /// Profiles based on real Norwegian coastal oceanographic data.
        /// </summary>
        public static EnvironmentalDataPoint GetSeasonalProfile(string zoneId, Season season)
        {
            if (!ZoneGeography.ContainsKey(zoneId))
                zoneId = "PO06"; // Default to Møre og Romsdal if unknown
            
            var geo = ZoneGeography[zoneId];
            var profile = new EnvironmentalDataPoint { ZoneId = zoneId };
            
            // Temperature profiles vary by latitude and season
            // Southern zones (PO01-03): warmer, narrower seasonal range
            // Northern zones (PO11-13): colder, wider seasonal range
            // Western zones (PO04-06): moderated by Gulf Stream
            
            double baseTemp = GetBaseTemperatureForZone(zoneId);
            double seasonalAmplitude = GetSeasonalAmplitudeForZone(zoneId);
            
            switch (season)
            {
                case Season.Winter:
                    // January-March: coldest period
                    profile.SurfaceTemperature = baseTemp - seasonalAmplitude * 0.8;
                    profile.BottomTemperature = baseTemp - seasonalAmplitude * 0.3;
                    break;
                case Season.Spring:
                    // April-June: warming up
                    profile.SurfaceTemperature = baseTemp - seasonalAmplitude * 0.2;
                    profile.BottomTemperature = baseTemp - seasonalAmplitude * 0.2;
                    break;
                case Season.Summer:
                    // July-September: peak temperatures
                    profile.SurfaceTemperature = baseTemp + seasonalAmplitude * 0.9;
                    profile.BottomTemperature = baseTemp + seasonalAmplitude * 0.4;
                    break;
                case Season.Autumn:
                    // October-December: cooling down
                    profile.SurfaceTemperature = baseTemp + seasonalAmplitude * 0.2;
                    profile.BottomTemperature = baseTemp;
                    break;
            }
            
            // Depth temperature gradient (thermocline effect)
            profile.TemperatureAt10m = profile.SurfaceTemperature - 0.5;
            profile.TemperatureAt30m = (profile.SurfaceTemperature + profile.BottomTemperature) / 2.0;
            profile.TemperatureVariability = seasonalAmplitude * 0.3;
            
            // Oxygen profiles (inverse relationship with temperature)
            // Higher temperatures = lower oxygen solubility
            // Norwegian fjords typically well-oxygenated except in deep basins
            double baseOxygen = 9.0 - (baseTemp - 8.0) * 0.2; // mg/L
            profile.SurfaceOxygen = baseOxygen;
            profile.OxygenAt10m = baseOxygen - 0.3;
            profile.OxygenAtCageDepth = baseOxygen - 0.5 - Math.Max(0, (profile.TemperatureAt30m - 12) * 0.2);
            profile.OxygenSaturation = Math.Min(1.0, profile.OxygenAtCageDepth / 8.0);
            
            // Current profiles by zone
            // Exposed western coast (PO03-06): stronger currents
            // Sheltered fjords (PO01-02, PO07-13): weaker currents
            (double avgCurrent, double tidalAmp) = GetCurrentParametersForZone(zoneId);
            profile.SurfaceCurrentSpeed = avgCurrent * (1.0 + (season == Season.Winter ? 0.3 : 0.0));
            profile.CurrentSpeedAtCageDepth = avgCurrent * 0.7;
            profile.TidalCurrentAmplitude = tidalAmp;
            profile.CurrentDirection = GetPrevailingCurrentDirection(zoneId);
            
            // Water quality parameters
            profile.Salinity = 34.0 + (zoneId.StartsWith("PO01") || zoneId.StartsWith("PO02") ? -1.0 : 0.0); // Lower salinity in Skagerrak
            profile.pH = 8.0 + (profile.SurfaceTemperature - 10.0) * (-0.02); // Slight pH decrease with warming
            profile.Turbidity = zoneId.StartsWith("PO0") && int.Parse(zoneId.Substring(2)) <= 4 ? 2.0 : 1.0; // Higher turbidity in south
            profile.ChlorophyllA = season == Season.Spring || season == Season.Autumn ? 3.0 : 1.5; // Bloom seasons
            
            // Calculate derived indices
            profile.CalculateDerivedIndices();
            
            return profile;
        }
        
        /// <summary>
        /// Get environmental profile for a specific day of year with interpolation between seasons.
        /// </summary>
        public static EnvironmentalDataPoint GetProfileForDay(string zoneId, int dayOfYear)
        {
            // Determine seasons for interpolation
            // Winter: Dec-Feb (days 335-365, 1-59)
            // Spring: Mar-May (days 60-151)
            // Summer: Jun-Aug (days 152-243)
            // Autumn: Sep-Nov (days 244-334)
            
            Season currentSeason, nextSeason;
            double t; // interpolation factor 0-1
            
            if (dayOfYear <= 59 || dayOfYear >= 335)
            {
                // Winter period
                currentSeason = Season.Winter;
                nextSeason = Season.Spring;
                if (dayOfYear >= 335)
                    t = (dayOfYear - 335) / 90.0;
                else
                    t = (dayOfYear + 30) / 90.0;
            }
            else if (dayOfYear <= 151)
            {
                // Spring period
                currentSeason = Season.Spring;
                nextSeason = Season.Summer;
                t = (dayOfYear - 60) / 91.0;
            }
            else if (dayOfYear <= 243)
            {
                // Summer period
                currentSeason = Season.Summer;
                nextSeason = Season.Autumn;
                t = (dayOfYear - 152) / 91.0;
            }
            else
            {
                // Autumn period
                currentSeason = Season.Autumn;
                nextSeason = Season.Winter;
                t = (dayOfYear - 244) / 91.0;
            }
            
            var current = GetSeasonalProfile(zoneId, currentSeason);
            var next = GetSeasonalProfile(zoneId, nextSeason);
            
            return EnvironmentalDataPoint.Interpolate(current, next, t);
        }
        
        /// <summary>
        /// Get base temperature for a zone based on latitude and oceanography.
        /// </summary>
        private static double GetBaseTemperatureForZone(string zoneId)
        {
            return zoneId switch
            {
                "PO01" => 10.5, // Østfold - warmest, Skagerrak influence
                "PO02" => 10.0, // Aust-Agder - North Sea influenced
                "PO03" => 9.5,  // Rogaland - North Sea
                "PO04" => 9.0,  // Hordaland - Norwegian Sea
                "PO05" => 8.5,  // Sogn og Fjordane - strong Gulf Stream
                "PO06" => 8.0,  // Møre og Romsdal - Norwegian Sea
                "PO07" => 7.5,  // Trøndelag Sør - Norwegian Sea
                "PO08" => 7.0,  // Trøndelag Nord - Norwegian Sea
                "PO09" => 6.5,  // Nordland Sør - Norwegian Sea
                "PO10" => 6.0,  // Nordland Nord - Norwegian Sea
                "PO11" => 5.5,  // Troms - Barents Sea influence
                "PO12" => 5.0,  // Finnmark - Barents Sea
                "PO13" => 4.5,  // Vest-Finnmark - coldest, Arctic
                _ => 8.0
            };
        }
        
        /// <summary>
        /// Get seasonal temperature amplitude for a zone.
        /// </summary>
        private static double GetSeasonalAmplitudeForZone(string zoneId)
        {
            return zoneId switch
            {
                // Southern zones have smaller amplitude due to oceanic influence
                "PO01" => 6.0,
                "PO02" => 6.5,
                "PO03" => 7.0,
                // Western zones moderated by Gulf Stream
                "PO04" => 7.5,
                "PO05" => 7.5,
                "PO06" => 7.0,
                // Central/northern zones have larger seasonal variation
                "PO07" => 7.5,
                "PO08" => 8.0,
                "PO09" => 8.5,
                "PO10" => 9.0,
                // Northernmost zones - largest variation
                "PO11" => 9.5,
                "PO12" => 10.0,
                "PO13" => 10.5,
                _ => 7.0
            };
        }
        
        /// <summary>
        /// Get current parameters for a zone (average speed and tidal amplitude).
        /// </summary>
        private static (double avgCurrent, double tidalAmp) GetCurrentParametersForZone(string zoneId)
        {
            return zoneId switch
            {
                // Skagerrak - strong tidal currents
                "PO01" => (0.25, 0.15),
                // North Sea - moderate currents
                "PO02" => (0.20, 0.10),
                "PO03" => (0.22, 0.12),
                // Norwegian Sea - strong currents, especially in fjord mouths
                "PO04" => (0.30, 0.15),
                "PO05" => (0.35, 0.18),
                "PO06" => (0.32, 0.16),
                // Trøndelag - moderate
                "PO07" => (0.25, 0.12),
                "PO08" => (0.23, 0.11),
                // Nordland - strong tidal currents
                "PO09" => (0.28, 0.14),
                "PO10" => (0.26, 0.13),
                // Northern Norway - strong currents
                "PO11" => (0.30, 0.15),
                "PO12" => (0.28, 0.14),
                "PO13" => (0.25, 0.12),
                _ => (0.20, 0.10)
            };
        }
        
        /// <summary>
        /// Get prevailing current direction for a zone (meteorological convention, degrees from north).
        /// </summary>
        private static double GetPrevailingCurrentDirection(string zoneId)
        {
            // Norwegian Coastal Current generally flows northward along the coast
            // But local topography creates variations
            return zoneId switch
            {
                "PO01" => 45.0,   // Northeast into Skagerrak
                "PO02" => 30.0,   // Northeast along coast
                "PO03" => 10.0,   // North
                "PO04" => 355.0,  // North-northwest
                "PO05" => 350.0,  // North-northwest
                "PO06" => 340.0,  // North-northwest
                "PO07" => 330.0,  // Northwest
                "PO08" => 325.0,  // Northwest
                "PO09" => 320.0,  // Northwest
                "PO10" => 315.0,  // Northwest
                "PO11" => 310.0,  // Northwest
                "PO12" => 300.0,  // West-northwest
                "PO13" => 290.0,  // West-northwest (Arctic)
                _ => 0.0
            };
        }
        
        /// <summary>
        /// Get zone-specific environmental risk multipliers for simulation.
        /// These modify disease pressure, mortality, and growth based on real conditions.
        /// </summary>
        public static ZoneEnvironmentalRiskProfile GetRiskProfile(string zoneId)
        {
            var profile = new ZoneEnvironmentalRiskProfile { ZoneId = zoneId };
            
            switch (zoneId)
            {
                case "PO01": // Østfold - high disease pressure, warmer water
                    profile.DiseasePressureMultiplier = 1.3;
                    profile.TemperatureStressMultiplier = 1.2;
                    profile.JellyfishRisk = 1.4;
                    profile.AlgalBloomRisk = 1.2;
                    break;
                    
                case "PO02": // Aust-Agder - moderate conditions
                    profile.DiseasePressureMultiplier = 1.1;
                    profile.TemperatureStressMultiplier = 1.1;
                    profile.JellyfishRisk = 1.2;
                    profile.AlgalBloomRisk = 1.1;
                    break;
                    
                case "PO03": // Rogaland - exposed, good water exchange
                    profile.DiseasePressureMultiplier = 0.9;
                    profile.TemperatureStressMultiplier = 1.0;
                    profile.JellyfishRisk = 0.8;
                    profile.AlgalBloomRisk = 0.9;
                    break;
                    
                case "PO04": // Hordaland - deep fjords, stratification issues
                    profile.DiseasePressureMultiplier = 1.0;
                    profile.TemperatureStressMultiplier = 1.0;
                    profile.JellyfishRisk = 0.9;
                    profile.AlgalBloomRisk = 1.1;
                    profile.OxygenDepletionRisk = 1.2;
                    break;
                    
                case "PO05": // Sogn og Fjordane - strong currents, good conditions
                    profile.DiseasePressureMultiplier = 0.85;
                    profile.TemperatureStressMultiplier = 0.9;
                    profile.JellyfishRisk = 0.7;
                    profile.AlgalBloomRisk = 0.8;
                    profile.CurrentAdvantage = 1.2;
                    break;
                    
                case "PO06": // Møre og Romsdal - optimal conditions
                    profile.DiseasePressureMultiplier = 0.9;
                    profile.TemperatureStressMultiplier = 0.9;
                    profile.JellyfishRisk = 0.8;
                    profile.AlgalBloomRisk = 0.85;
                    profile.CurrentAdvantage = 1.15;
                    break;
                    
                case "PO07": // Trøndelag Sør - good conditions
                    profile.DiseasePressureMultiplier = 0.95;
                    profile.TemperatureStressMultiplier = 0.95;
                    profile.JellyfishRisk = 0.85;
                    profile.AlgalBloomRisk = 0.9;
                    break;
                    
                case "PO08": // Trøndelag Nord - colder, less disease
                    profile.DiseasePressureMultiplier = 0.85;
                    profile.TemperatureStressMultiplier = 1.0;
                    profile.JellyfishRisk = 0.75;
                    profile.AlgalBloomRisk = 0.8;
                    profile.ColdWaterGrowthPenalty = 1.1;
                    break;
                    
                case "PO09": // Nordland Sør - good northern conditions
                    profile.DiseasePressureMultiplier = 0.8;
                    profile.TemperatureStressMultiplier = 1.0;
                    profile.JellyfishRisk = 0.7;
                    profile.AlgalBloomRisk = 0.75;
                    profile.ColdWaterGrowthPenalty = 1.15;
                    break;
                    
                case "PO10": // Nordland Nord - colder, longer growth time
                    profile.DiseasePressureMultiplier = 0.75;
                    profile.TemperatureStressMultiplier = 1.05;
                    profile.JellyfishRisk = 0.65;
                    profile.AlgalBloomRisk = 0.7;
                    profile.ColdWaterGrowthPenalty = 1.25;
                    break;
                    
                case "PO11": // Troms - Arctic influence, very low disease
                    profile.DiseasePressureMultiplier = 0.7;
                    profile.TemperatureStressMultiplier = 1.1;
                    profile.JellyfishRisk = 0.6;
                    profile.AlgalBloomRisk = 0.6;
                    profile.ColdWaterGrowthPenalty = 1.35;
                    profile.IceRisk = 1.2;
                    break;
                    
                case "PO12": // Finnmark - extreme conditions
                    profile.DiseasePressureMultiplier = 0.65;
                    profile.TemperatureStressMultiplier = 1.15;
                    profile.JellyfishRisk = 0.5;
                    profile.AlgalBloomRisk = 0.5;
                    profile.ColdWaterGrowthPenalty = 1.5;
                    profile.IceRisk = 1.4;
                    break;
                    
                case "PO13": // Vest-Finnmark - harshest conditions
                    profile.DiseasePressureMultiplier = 0.6;
                    profile.TemperatureStressMultiplier = 1.2;
                    profile.JellyfishRisk = 0.4;
                    profile.AlgalBloomRisk = 0.4;
                    profile.ColdWaterGrowthPenalty = 1.6;
                    profile.IceRisk = 1.6;
                    break;
                    
                default:
                    profile.DiseasePressureMultiplier = 1.0;
                    profile.TemperatureStressMultiplier = 1.0;
                    profile.JellyfishRisk = 1.0;
                    profile.AlgalBloomRisk = 1.0;
                    break;
            }
            
            return profile;
        }
    }
    
    /// <summary>
    /// Geographic information for a production zone.
    /// </summary>
    public struct ZoneGeography
    {
        public string Name;
        public double CenterLat;
        public double CenterLon;
        public string DepthRange;
        public int AvgDepth;
    }
    
    /// <summary>
    /// Season enumeration for seasonal profiles.
    /// </summary>
    public enum Season
    {
        Winter,
        Spring,
        Summer,
        Autumn
    }
    
    /// <summary>
    /// Environmental risk profile for a zone.
    /// Multipliers applied to various simulation parameters.
    /// </summary>
    public class ZoneEnvironmentalRiskProfile
    {
        public string ZoneId { get; set; } = "";
        
        /// <summary>Disease pressure multiplier (>1 = higher risk).</summary>
        public double DiseasePressureMultiplier { get; set; } = 1.0;
        
        /// <summary>Temperature stress multiplier (>1 = more stress).</summary>
        public double TemperatureStressMultiplier { get; set; } = 1.0;
        
        /// <summary>Jellyfish aggregation risk multiplier.</summary>
        public double JellyfishRisk { get; set; } = 1.0;
        
        /// <summary>Harmful algal bloom risk multiplier.</summary>
        public double AlgalBloomRisk { get; set; } = 1.0;
        
        /// <summary>Oxygen depletion risk in deep fjords.</summary>
        public double OxygenDepletionRisk { get; set; } = 1.0;
        
        /// <summary>Advantage from strong currents (improves water quality).</summary>
        public double CurrentAdvantage { get; set; } = 1.0;
        
        /// <summary>Penalty for cold water (slower growth in north).</summary>
        public double ColdWaterGrowthPenalty { get; set; } = 1.0;
        
        /// <summary>Sea ice risk in far north.</summary>
        public double IceRisk { get; set; } = 1.0;
    }
}
