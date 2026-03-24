using System;
using System.Collections.Generic;

namespace LakskongenSim.Data.RealData.Layers
{
    /// <summary>
    /// Salinity data layer for Norwegian coastal waters.
    /// Norwegian coastal: 30-35 PSU, fjords: 25-33 PSU with seasonal freshwater influence.
    /// </summary>
    public class SalinityLayer
    {
        public string LayerName => "Salinity";
        public string Unit => "PSU";

        // Base salinity by zone (PSU) — decreasing toward fjord heads
        private static readonly Dictionary<string, double> ZoneBaseSalinity = new()
        {
            { "PO01", 33.5 }, { "PO02", 33.0 }, { "PO03", 32.5 },
            { "PO04", 32.0 }, { "PO05", 31.5 }, { "PO06", 31.0 },
            { "PO07", 31.5 }, { "PO08", 32.0 }, { "PO09", 32.5 },
            { "PO10", 33.0 }, { "PO11", 33.5 }, { "PO12", 34.0 },
            { "PO13", 34.5 },
        };

        /// <summary>
        /// Get salinity at a specific location, time, and depth.
        /// </summary>
        public double GetSalinity(string zoneId, DateTime date, double depthM = 10.0)
        {
            double baseSalinity = ZoneBaseSalinity.TryGetValue(zoneId, out var s) ? s : 33.0;

            // Seasonal variation: spring snowmelt reduces coastal salinity
            double dayOfYear = date.DayOfYear;
            double seasonalEffect = -1.5 * Math.Exp(-Math.Pow((dayOfYear - 150) / 40.0, 2)); // Peak freshwater in May-June

            // Depth effect: salinity increases with depth (halocline)
            double depthEffect = depthM < 5 ? -1.0 :
                                 depthM < 15 ? -0.5 :
                                 depthM < 30 ? 0.0 : 0.5;

            return Math.Clamp(baseSalinity + seasonalEffect + depthEffect, 20.0, 36.0);
        }

        /// <summary>
        /// Check if salinity is in optimal range for Atlantic salmon (25-35 PSU).
        /// </summary>
        public bool IsOptimalForSalmon(double salinityPSU)
        {
            return salinityPSU >= 25.0 && salinityPSU <= 35.0;
        }
    }
}
