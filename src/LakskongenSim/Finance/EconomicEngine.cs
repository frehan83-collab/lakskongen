using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Main economic calculation engine for the salmon farming simulation.
    /// Provides Fish Pool pricing, zone costs, taxes, and currency conversion.
    /// </summary>
    public class EconomicEngine
    {
        // Fish Pool Index seasonal base prices (NOK/kg)
        private static readonly Dictionary<int, (double min, double max)> SeasonalPriceRanges = new()
        {
            { 1, (55, 65) },   // Q1: Post-holiday
            { 2, (58, 68) },
            { 3, (60, 72) },   // Lent demand
            { 4, (70, 85) },   // Q2: Easter + spring peak
            { 5, (72, 88) },
            { 6, (68, 82) },
            { 7, (60, 75) },   // Q3: Summer slowdown
            { 8, (58, 72) },
            { 9, (62, 78) },
            { 10, (75, 90) },  // Q4: Autumn + Christmas
            { 11, (78, 92) },
            { 12, (80, 95) },  // Christmas peak
        };

        // Zone-specific feed costs (NOK/kg feed) — varies by distance from feed mills
        private static readonly Dictionary<string, double> ZoneFeedCosts = new()
        {
            { "PO01", 38.0 }, { "PO02", 37.5 }, { "PO03", 37.0 },
            { "PO04", 36.5 }, { "PO05", 37.0 }, { "PO06", 38.0 },
            { "PO07", 39.0 }, { "PO08", 40.0 }, { "PO09", 41.0 },
            { "PO10", 42.0 }, { "PO11", 43.0 }, { "PO12", 44.0 },
            { "PO13", 45.0 },
        };

        // Regional electricity tariffs (NOK/kWh)
        private static readonly Dictionary<string, double> ZoneElectricityCosts = new()
        {
            { "PO01", 0.75 }, { "PO02", 0.70 }, { "PO03", 0.65 },
            { "PO04", 0.60 }, { "PO05", 0.55 }, { "PO06", 0.50 },
            { "PO07", 0.45 }, { "PO08", 0.42 }, { "PO09", 0.40 },
            { "PO10", 0.38 }, { "PO11", 0.35 }, { "PO12", 0.32 },
            { "PO13", 0.30 },
        };

        private readonly Random _random = new();

        /// <summary>
        /// Get current spot price based on month and random market variation.
        /// </summary>
        public double GetSpotPrice(int month, double volatility = 0.05)
        {
            month = Math.Clamp(month, 1, 12);
            var (min, max) = SeasonalPriceRanges[month];
            double basePrice = (min + max) / 2.0;
            double variation = basePrice * volatility * (_random.NextDouble() * 2 - 1);
            return Math.Max(30.0, basePrice + variation);
        }

        /// <summary>
        /// Get feed cost per kg for a specific production zone.
        /// </summary>
        public double GetFeedCostPerZone(string zoneId)
        {
            return ZoneFeedCosts.TryGetValue(zoneId, out var cost) ? cost : 40.0;
        }

        /// <summary>
        /// Get electricity cost per kWh for a specific zone.
        /// </summary>
        public double GetElectricityCostPerZone(string zoneId)
        {
            return ZoneElectricityCosts.TryGetValue(zoneId, out var cost) ? cost : 0.50;
        }

        /// <summary>
        /// Calculate production tax for harvested salmon.
        /// </summary>
        public double CalculateProductionTax(double harvestedKg)
        {
            const double taxPerKg = 0.90; // NOK per kg
            return harvestedKg * taxPerKg;
        }

        /// <summary>
        /// Calculate resource rent tax on profits.
        /// </summary>
        public double CalculateResourceRentTax(double profitNOK)
        {
            const double rate = 0.25; // 25%
            return Math.Max(0, profitNOK * rate);
        }

        /// <summary>
        /// Convert NOK to another currency.
        /// </summary>
        public double ConvertCurrency(double amountNOK, string targetCurrency)
        {
            return targetCurrency.ToUpperInvariant() switch
            {
                "EUR" => amountNOK / 11.50,
                "USD" => amountNOK / 10.80,
                "GBP" => amountNOK / 13.20,
                "NOK" => amountNOK,
                _ => amountNOK
            };
        }
    }
}
