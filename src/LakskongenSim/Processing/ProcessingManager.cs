using System;
using System.Collections.Generic;
using LakskongenSim.Market;

namespace LakskongenSim.Processing
{
    public enum ProcessingTier
    {
        Small,
        Medium,
        Large
    }

    public record ProcessingResult(int InputKg, int OutputKg, decimal Revenue, decimal RawMaterialCost, decimal Opex, decimal Depreciation, decimal Profit);

    /// <summary>
    /// Processing manager implements simple economics: CAPEX tiers, OPEX scaling, depreciation, ROI calculations and capacity utilization curves.
    /// Deterministic calculation methods are provided for unit tests and game integration.
    /// </summary>
    public class ProcessingManager
    {
        public ProcessingTier Tier { get; }
        public int NominalCapacityKgPerDay { get; }
        public decimal Capex { get; }
        public decimal AssetLifeYears { get; } = 7m; // used for straight-line depreciation
        public decimal VariableOpexPerTonne { get; } // NOK per tonne
        public decimal FixedOpexPerDay { get; }

        // Product-specific yields (kg output per kg input) and price multipliers
        public Dictionary<ProductType, decimal> Yield = new()
        {
            { ProductType.FreshWhole, 0.95m },
            { ProductType.Fillet, 0.40m },
            { ProductType.Frozen, 0.90m }
        };

        public Dictionary<ProductType, decimal> PricePerKg = new()
        {
            { ProductType.FreshWhole, 60m },
            { ProductType.Fillet, 150m },
            { ProductType.Frozen, 55m }
        };

        public ProcessingManager(ProcessingTier tier)
        {
            Tier = tier;
            (NominalCapacityKgPerDay, Capex, FixedOpexPerDay, VariableOpexPerTonne) = tier switch
            {
                ProcessingTier.Small => (10000, 1000000m, 800m, 200m),
                ProcessingTier.Medium => (75000, 5000000m, 3000m, 150m),
                ProcessingTier.Large => (250000, 15000000m, 8000m, 120m),
                _ => (10000, 1000000m, 800m, 200m)
            };
        }

        /// <summary>
        /// Utilization curve: returns an efficiency multiplier (0-1) based on utilization ratio (throughput/nominal).
        /// We model inefficiency at low utilization and marginal gains near high utilization.
        /// </summary>
        public static decimal UtilizationMultiplier(decimal utilizationRatio)
        {
            if (utilizationRatio <= 0m) return 0m;
            if (utilizationRatio < 0.3m) return 0.6m * utilizationRatio / 0.3m; // steep loss when very low
            if (utilizationRatio < 0.6m) return 0.6m + 0.3m * (utilizationRatio - 0.3m) / 0.3m; // ramp 0.6→0.9
            if (utilizationRatio <= 1.0m) return 0.9m + 0.1m * (utilizationRatio - 0.6m) / 0.4m; // plateau
            // overcapacity: efficiency degrades linearly beyond 1.0
            return Math.Max(0.0m, 1.0m - 0.3m * (utilizationRatio - 1.0m));
        }

        /// <summary>
        /// Calculate OPEX (fixed + variable) for a given daily throughput kg.
        /// Variable OPEX stored in per-tonne units (per 1000 kg). Returns total OPEX.
        /// </summary>
        public decimal CalculateDailyOpex(int throughputKg)
        {
            decimal tonnes = throughputKg / 1000m;
            decimal variable = VariableOpexPerTonne * tonnes;
            return FixedOpexPerDay + variable;
        }

        /// <summary>
        /// Annual depreciation (straight-line)
        /// </summary>
        public decimal AnnualDepreciation()
        {
            return Capex / AssetLifeYears;
        }

        /// <summary>
        /// Simple ROI years estimate: capex / annualProfit (using utilization fraction)
        /// </summary>
        public decimal EstimatePaybackYears(decimal annualThroughputKg, decimal averageMarginPerKg)
        {
            if (annualThroughputKg <= 0 || averageMarginPerKg <= 0) return decimal.MaxValue;
            decimal annualProfit = annualThroughputKg * averageMarginPerKg;
            return Capex / annualProfit;
        }

        /// <summary>
        /// Process a given input weight (kg) into a product type. Uses yields, prices and economics to compute P&amp;L.
        /// rawMaterialCostPerKg is the price paid for whole fish input.
        /// </summary>
        public ProcessingResult Process(int inputKg, ProductType product, decimal rawMaterialCostPerKg, decimal qualityMultiplier = 1.0m)
        {
            if (!Yield.TryGetValue(product, out var yield)) throw new ArgumentException("Unknown product type", nameof(product));
            if (!PricePerKg.TryGetValue(product, out var priceKg)) throw new ArgumentException("Unknown product type", nameof(product));

            int outputKg = (int)Math.Round(inputKg * (double)yield);
            decimal revenue = outputKg * priceKg * qualityMultiplier;
            decimal rawCost = inputKg * rawMaterialCostPerKg;

            decimal opex = CalculateDailyOpex(inputKg);
            decimal depreciationDaily = AnnualDepreciation() / LakskongenSim.Core.SimCalendar.DaysPerYear;

            decimal profit = revenue - rawCost - opex - depreciationDaily;

            return new ProcessingResult(inputKg, outputKg, revenue, rawCost, opex, depreciationDaily, profit);
        }
    }
}
