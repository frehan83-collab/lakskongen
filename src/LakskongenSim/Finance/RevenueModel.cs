using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Revenue calculation model for salmon products.
    /// Handles fresh/frozen/processed margins, contract vs spot, export premiums.
    /// </summary>
    public class RevenueModel
    {
        // Seasonal price multipliers by month
        private static readonly double[] SeasonalMultipliers =
        {
            0, 0.90, 0.92, 0.95, 1.05, 1.10, 1.08,
            0.95, 0.93, 0.98, 1.05, 1.12, 1.15
        };

        // Product type margins (multiplier on base price)
        private static readonly Dictionary<string, double> ProductMargins = new()
        {
            { "fresh_whole", 1.00 },
            { "fresh_fillet", 1.35 },
            { "frozen_whole", 0.85 },
            { "frozen_fillet", 1.15 },
            { "smoked", 1.80 },
            { "portions", 1.50 },
            { "sushi_grade", 2.00 },
        };

        // Export market premiums
        private static readonly Dictionary<string, double> ExportPremiums = new()
        {
            { "EU", 1.00 },
            { "Japan", 1.25 },
            { "US", 1.10 },
            { "China", 1.15 },
            { "Korea", 1.20 },
            { "UK", 1.05 },
            { "domestic", 0.95 },
        };

        /// <summary>
        /// Get seasonal spot price for a given base price and month.
        /// </summary>
        public double GetSeasonalSpotPrice(double basePrice, int month)
        {
            month = Math.Clamp(month, 1, 12);
            return basePrice * SeasonalMultipliers[month];
        }

        /// <summary>
        /// Calculate revenue per kg for fresh salmon.
        /// </summary>
        public double CalculateFreshSalmonRevenue(double quantityKg, double spotPricePerKg)
        {
            return quantityKg * spotPricePerKg * ProductMargins["fresh_whole"];
        }

        /// <summary>
        /// Calculate revenue for a specific product type and market.
        /// </summary>
        public double CalculateProductRevenue(
            double quantityKg, double spotPricePerKg,
            string productType = "fresh_whole", string exportMarket = "EU")
        {
            double productMultiplier = ProductMargins.TryGetValue(productType, out var pm) ? pm : 1.0;
            double marketMultiplier = ExportPremiums.TryGetValue(exportMarket, out var em) ? em : 1.0;
            return quantityKg * spotPricePerKg * productMultiplier * marketMultiplier;
        }

        /// <summary>
        /// Calculate contract price (typically 5-15% below spot but guaranteed).
        /// </summary>
        public double CalculateContractPrice(double spotPrice, double discountPct = 10.0)
        {
            return spotPrice * (1.0 - discountPct / 100.0);
        }
    }
}
