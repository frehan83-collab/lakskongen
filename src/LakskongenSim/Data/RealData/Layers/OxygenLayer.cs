using System;
using System.Collections.Generic;

namespace LakskongenSim.Data.RealData.Layers
{
    /// <summary>
    /// Dissolved oxygen data layer for Norwegian coastal waters.
    /// Surface: 8-11 mg/L, depth: 6-9 mg/L, seasonal variation.
    /// </summary>
    public class OxygenLayer
    {
        public string LayerName => "Dissolved Oxygen";
        public string Unit => "mg/L";

        // Base oxygen by zone (mg/L) — higher in colder, well-mixed northern waters
        private static readonly Dictionary<string, double> ZoneBaseOxygen = new()
        {
            { "PO01", 8.5 }, { "PO02", 8.7 }, { "PO03", 8.9 },
            { "PO04", 9.0 }, { "PO05", 9.2 }, { "PO06", 9.3 },
            { "PO07", 9.5 }, { "PO08", 9.7 }, { "PO09", 9.8 },
            { "PO10", 10.0 }, { "PO11", 10.2 }, { "PO12", 10.5 },
            { "PO13", 10.8 },
        };

        /// <summary>
        /// Get dissolved oxygen at a specific location, time, and depth.
        /// </summary>
        public double GetOxygen(string zoneId, DateTime date, double depthM = 10.0)
        {
            double baseOxygen = ZoneBaseOxygen.TryGetValue(zoneId, out var o) ? o : 9.0;

            // Seasonal variation: lower in summer (warmer water holds less O2)
            double dayOfYear = date.DayOfYear;
            double seasonalEffect = -1.5 * Math.Exp(-Math.Pow((dayOfYear - 200) / 50.0, 2)); // Minimum in July-August

            // Depth effect: oxygen decreases with depth
            double depthEffect = depthM < 5 ? 0.5 :
                                 depthM < 15 ? 0.0 :
                                 depthM < 30 ? -0.5 :
                                 depthM < 50 ? -1.0 : -1.5;

            return Math.Clamp(baseOxygen + seasonalEffect + depthEffect, 2.0, 14.0);
        }

        /// <summary>
        /// Assess oxygen stress level for salmon.
        /// </summary>
        public string GetStressLevel(double oxygenMgL)
        {
            if (oxygenMgL >= 8.0) return "Optimal";
            if (oxygenMgL >= 6.0) return "Suboptimal";
            if (oxygenMgL >= 4.0) return "Stressed";
            if (oxygenMgL >= 2.0) return "Critical";
            return "Lethal";
        }
    }
}
