using System;
using System.Collections.Generic;

namespace LakskongenSim.Data.RealData.Layers
{
    /// <summary>
    /// Ocean current data layer for Norwegian coastal waters.
    /// Models the Norwegian Coastal Current (NCC) and Norwegian Atlantic Current (NAC).
    /// Current speed affects oxygen exchange, waste dispersal, and lice pressure.
    /// </summary>
    public class CurrentLayer
    {
        public string LayerName => "Ocean Currents";
        public string Unit => "m/s";

        // Base current speed by zone (m/s) — Norwegian Coastal Current flows northward
        private static readonly Dictionary<string, double> ZoneBaseSpeed = new()
        {
            { "PO01", 0.15 }, { "PO02", 0.18 }, { "PO03", 0.20 },
            { "PO04", 0.22 }, { "PO05", 0.25 }, { "PO06", 0.28 },
            { "PO07", 0.30 }, { "PO08", 0.25 }, { "PO09", 0.22 },
            { "PO10", 0.20 }, { "PO11", 0.18 }, { "PO12", 0.15 },
            { "PO13", 0.12 },
        };

        // Dominant current direction by zone (degrees, 0=N, 90=E, 180=S, 270=W)
        private static readonly Dictionary<string, double> ZoneDirection = new()
        {
            { "PO01", 30 },  { "PO02", 25 },  { "PO03", 20 },
            { "PO04", 15 },  { "PO05", 10 },  { "PO06", 5 },
            { "PO07", 0 },   { "PO08", 355 }, { "PO09", 350 },
            { "PO10", 345 }, { "PO11", 340 }, { "PO12", 335 },
            { "PO13", 330 },
        };

        /// <summary>
        /// Get current speed at a specific zone and time.
        /// </summary>
        public double GetCurrentSpeed(string zoneId, DateTime date, double depthM = 10.0)
        {
            double baseSpeed = ZoneBaseSpeed.TryGetValue(zoneId, out var s) ? s : 0.20;

            // Seasonal variation: stronger in winter due to storm-driven circulation
            double dayOfYear = date.DayOfYear;
            double seasonalFactor = 1.0 + 0.3 * Math.Cos((dayOfYear - 15) * 2 * Math.PI / 365); // Peak in Jan

            // Depth effect: weaker with depth
            double depthFactor = depthM < 5 ? 1.2 :
                                 depthM < 15 ? 1.0 :
                                 depthM < 30 ? 0.7 :
                                 depthM < 50 ? 0.4 : 0.2;

            // Tidal component (semi-diurnal, ~12.4h period)
            double hourFraction = date.TimeOfDay.TotalHours;
            double tidalFactor = 1.0 + 0.15 * Math.Sin(hourFraction * 2 * Math.PI / 12.4);

            return Math.Clamp(baseSpeed * seasonalFactor * depthFactor * tidalFactor, 0.01, 2.0);
        }

        /// <summary>
        /// Get dominant current direction (degrees from north).
        /// </summary>
        public double GetCurrentDirection(string zoneId, DateTime date)
        {
            double baseDir = ZoneDirection.TryGetValue(zoneId, out var d) ? d : 0;

            // Tidal variation (±15 degrees)
            double hourFraction = date.TimeOfDay.TotalHours;
            double tidalVariation = 15 * Math.Sin(hourFraction * 2 * Math.PI / 12.4);

            return (baseDir + tidalVariation + 360) % 360;
        }

        /// <summary>
        /// Assess current impact on aquaculture operations.
        /// Strong currents: better O2 exchange, faster waste dispersal, but higher structural stress.
        /// </summary>
        public CurrentAssessment AssessForAquaculture(double speedMs)
        {
            if (speedMs < 0.05) return new CurrentAssessment("Stagnant", "Poor oxygen exchange, waste accumulation risk", -2);
            if (speedMs < 0.15) return new CurrentAssessment("Low", "Adequate but suboptimal flushing", -1);
            if (speedMs < 0.50) return new CurrentAssessment("Optimal", "Good oxygen exchange, effective waste dispersal", 0);
            if (speedMs < 1.00) return new CurrentAssessment("Strong", "Excellent flushing but increased equipment stress", 1);
            return new CurrentAssessment("Extreme", "Risk of cage/mooring damage, feeding difficulties", 2);
        }
    }

    public record CurrentAssessment(string Level, string Description, int StressRating);
}
