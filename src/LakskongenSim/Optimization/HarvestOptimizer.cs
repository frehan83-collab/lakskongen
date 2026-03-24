using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Optimization
{
    public class HarvestScenario
    {
        public DateTime HarvestDate { get; set; }
        public double EstimatedWeightKg { get; set; }
        public double EstimatedPricePerKg { get; set; }
        public double EstimatedCostPerKg { get; set; }
        public double GrossRevenueNOK { get; set; }
        public double TotalCostNOK { get; set; }
        public double ProfitNOK { get; set; }
        public double MarginPct { get; set; }
        public string Recommendation { get; set; } = "";
    }

    public class HarvestOptimizer
    {
        // Target harvest weights
        private const double MinHarvestWeightKg = 3.5;
        private const double OptimalHarvestMinKg = 4.0;
        private const double OptimalHarvestMaxKg = 6.0;
        private const double MaxHarvestWeightKg = 7.0;

        // Seasonal price multipliers (Fish Pool historical patterns)
        private static readonly Dictionary<int, double> SeasonalPriceMultiplier = new()
        {
            { 1, 0.90 },  // January — post-holiday dip
            { 2, 0.92 },  // February
            { 3, 0.95 },  // March — Lent demand
            { 4, 1.05 },  // April — Easter
            { 5, 1.10 },  // May — peak spring
            { 6, 1.08 },  // June
            { 7, 0.95 },  // July — summer slowdown
            { 8, 0.93 },  // August
            { 9, 0.98 },  // September — recovery
            { 10, 1.05 }, // October — autumn demand
            { 11, 1.12 }, // November — pre-Christmas
            { 12, 1.15 }, // December — Christmas peak
        };

        /// <summary>
        /// Calculate optimal harvest date given current fish state and market outlook.
        /// </summary>
        public DateTime CalculateOptimalHarvestDate(
            double currentWeightKg,
            double dailyGrowthKg,
            double basePricePerKg,
            double costPerKgAccumulated,
            double dailyHoldingCostPerFish,
            int maxDaysToConsider = 180)
        {
            DateTime bestDate = DateTime.Today;
            double bestProfit = double.MinValue;

            for (int d = 0; d <= maxDaysToConsider; d++)
            {
                DateTime date = DateTime.Today.AddDays(d);
                double weight = currentWeightKg + (dailyGrowthKg * d);

                if (weight < MinHarvestWeightKg) continue;
                if (weight > MaxHarvestWeightKg) break;

                double seasonalMultiplier = SeasonalPriceMultiplier[date.Month];
                double pricePerKg = basePricePerKg * seasonalMultiplier;

                // Weight premium: optimal 4-6kg gets best price
                double weightPremium = GetWeightPremium(weight);
                pricePerKg *= weightPremium;

                double revenue = weight * pricePerKg;
                double cost = (costPerKgAccumulated * weight) + (dailyHoldingCostPerFish * d);
                double profit = revenue - cost;

                if (profit > bestProfit)
                {
                    bestProfit = profit;
                    bestDate = date;
                }
            }

            return bestDate;
        }

        /// <summary>
        /// Compare multiple harvest date scenarios.
        /// </summary>
        public List<HarvestScenario> CompareHarvestScenarios(
            double currentWeightKg,
            double dailyGrowthKg,
            double basePricePerKg,
            double costPerKgAccumulated,
            double dailyHoldingCostPerFish,
            int fishCount,
            params DateTime[] dates)
        {
            var scenarios = new List<HarvestScenario>();

            foreach (var date in dates)
            {
                int daysFromNow = (date - DateTime.Today).Days;
                if (daysFromNow < 0) continue;

                double weight = currentWeightKg + (dailyGrowthKg * daysFromNow);
                double seasonalMultiplier = SeasonalPriceMultiplier[date.Month];
                double pricePerKg = basePricePerKg * seasonalMultiplier * GetWeightPremium(weight);
                double costPerKg = costPerKgAccumulated + (dailyHoldingCostPerFish * daysFromNow / Math.Max(weight, 0.01));

                double grossRevenue = weight * pricePerKg * fishCount;
                double totalCost = costPerKg * weight * fishCount;
                double profit = grossRevenue - totalCost;
                double margin = grossRevenue > 0 ? (profit / grossRevenue) * 100.0 : 0;

                string recommendation = weight < MinHarvestWeightKg ? "Too early — fish underweight" :
                                        weight > MaxHarvestWeightKg ? "Late — diminishing returns" :
                                        margin > 30 ? "★ Excellent margin" :
                                        margin > 15 ? "Good margin" :
                                        margin > 0 ? "Marginal — consider waiting" : "Unprofitable";

                scenarios.Add(new HarvestScenario
                {
                    HarvestDate = date,
                    EstimatedWeightKg = weight,
                    EstimatedPricePerKg = pricePerKg,
                    EstimatedCostPerKg = costPerKg,
                    GrossRevenueNOK = grossRevenue,
                    TotalCostNOK = totalCost,
                    ProfitNOK = profit,
                    MarginPct = margin,
                    Recommendation = recommendation
                });
            }

            return scenarios.OrderByDescending(s => s.ProfitNOK).ToList();
        }

        /// <summary>
        /// Get profit margin for a given weight and market price.
        /// </summary>
        public double GetMarginByWeight(double weightKg, double marketPricePerKg, double costPerKg)
        {
            double priceWithPremium = marketPricePerKg * GetWeightPremium(weightKg);
            double revenue = weightKg * priceWithPremium;
            double cost = weightKg * costPerKg;
            return revenue - cost;
        }

        private double GetWeightPremium(double weightKg)
        {
            if (weightKg >= OptimalHarvestMinKg && weightKg <= OptimalHarvestMaxKg) return 1.05;
            if (weightKg >= 3.0 && weightKg < OptimalHarvestMinKg) return 0.95;
            if (weightKg > OptimalHarvestMaxKg && weightKg <= MaxHarvestWeightKg) return 1.00;
            return 0.85; // Under/overweight penalty
        }
    }
}
