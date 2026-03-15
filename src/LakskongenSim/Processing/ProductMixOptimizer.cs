using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Market;

namespace LakskongenSim.Processing
{
    /// <summary>
    /// Optimization strategy for product mix decisions
    /// </summary>
    public enum OptimizationStrategy
    {
        MaximizeProfit,      // Pure profit maximization
        MaximizeVolume,      // Maximize throughput (kg produced)
        MaximizeMargin,      // Highest margin % products
        Balanced,            // Mix of margin and volume
        MinimizeRisk,        // Low-volatility products
        PremiumFocus         // Luxury/premium positioning
    }
    
    /// <summary>
    /// Market demand scenario for a product
    /// </summary>
    public class DemandScenario
    {
        public ProductType Product { get; set; }
        public decimal DemandMultiplier { get; set; } = 1.0m;  // 1.0 = normal, >1 = high demand, <1 = low
        public decimal PriceMultiplier { get; set; } = 1.0m;   // Market price adjustment
        public bool MarketOpen { get; set; } = true;           // Export restrictions, etc.
    }
    
    /// <summary>
    /// Recommendation for what to produce
    /// </summary>
    public class ProductMixRecommendation
    {
        public ProductType Product { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int RecommendedKgPerDay { get; set; }
        public decimal ExpectedRevenue { get; set; }
        public decimal ExpectedProfit { get; set; }
        public decimal ExpectedMargin { get; set; }
        public decimal RiskScore { get; set; }  // 0-1, higher = more risky
        public string Reasoning { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Complete product mix recommendation for a plant
    /// </summary>
    public class MixAnalysis
    {
        public OptimizationStrategy Strategy { get; set; }
        public List<ProductMixRecommendation> Recommendations { get; set; } = new();
        public decimal TotalExpectedProfit { get; set; }
        public decimal WeightedAverageMargin { get; set; }
        public decimal PortfolioRiskScore { get; set; }
        public string Summary { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// Helps players optimize their product mix based on market conditions and strategy
    /// </summary>
    public static class ProductMixOptimizer
    {
        /// <summary>
        /// Analyze and recommend optimal product mix for a processing plant
        /// </summary>
        public static MixAnalysis OptimizeProductMix(
            ProcessingPlant plant,
            int availableInputKg,
            decimal rawMaterialCostPerKg,
            OptimizationStrategy strategy,
            Dictionary<ProductType, DemandScenario>? marketConditions = null)
        {
            marketConditions ??= new Dictionary<ProductType, DemandScenario>();
            
            var analysis = new MixAnalysis { Strategy = strategy };
            var recommendations = new List<ProductMixRecommendation>();
            
            // Get all available products for this plant tier
            var availableProducts = plant.GetAvailableProducts();
            
            // Score each product
            foreach (var product in availableProducts)
            {
                if (!ProductCatalog.Products.TryGetValue(product, out var def))
                    continue;
                
                // Get market conditions
                if (!marketConditions.TryGetValue(product, out var demand))
                {
                    demand = new DemandScenario { Product = product };
                }
                
                if (!demand.MarketOpen)
                    continue;  // Skip restricted products
                
                // Calculate expected economics
                var rec = CalculateProductScore(product, def, rawMaterialCostPerKg, demand, strategy);
                recommendations.Add(rec);
            }
            
            // Sort by strategy
            recommendations = SortByStrategy(recommendations, strategy).ToList();
            
            // Allocate capacity
            int remainingCapacity = availableInputKg;
            var selectedProducts = new List<ProductMixRecommendation>();
            
            foreach (var rec in recommendations)
            {
                if (remainingCapacity <= 0) break;
                
                // Allocate capacity based on strategy
                int allocation = CalculateAllocation(rec, remainingCapacity, strategy);
                
                if (allocation > 0)
                {
                    rec.RecommendedKgPerDay = allocation;
                    selectedProducts.Add(rec);
                    remainingCapacity -= allocation;
                }
            }
            
            analysis.Recommendations = selectedProducts;
            analysis.TotalExpectedProfit = selectedProducts.Sum(r => r.ExpectedProfit);
            analysis.WeightedAverageMargin = CalculateWeightedMargin(selectedProducts);
            analysis.PortfolioRiskScore = CalculatePortfolioRisk(selectedProducts);
            analysis.Summary = GenerateSummary(analysis, strategy);
            
            return analysis;
        }
        
        /// <summary>
        /// Calculate score and metrics for a product
        /// </summary>
        private static ProductMixRecommendation CalculateProductScore(
            ProductType product,
            ProductDefinition def,
            decimal rawCostPerKg,
            DemandScenario demand,
            OptimizationStrategy strategy)
        {
            // Simplified economics (use average quality/skill)
            decimal yieldPercent = def.BaseYieldPercent * 0.9m; // Conservative estimate
            decimal outputPerKgInput = yieldPercent;
            
            decimal adjustedPrice = def.BasePricePerKg * demand.PriceMultiplier;
            decimal revenue = outputPerKgInput * adjustedPrice;
            
            decimal costs = rawCostPerKg + def.VariableOpexPerKg + def.LaborCostPerKg;
            decimal profit = revenue - costs;
            decimal margin = revenue > 0 ? profit / revenue : 0m;
            
            // Risk scoring
            decimal riskScore = CalculateRiskScore(def, demand);
            
            // Strategy-specific reasoning
            string reasoning = GenerateReasoning(product, def, profit, margin, riskScore, strategy);
            
            return new ProductMixRecommendation
            {
                Product = product,
                ProductName = def.Name,
                RecommendedKgPerDay = 0,  // Will be set during allocation
                ExpectedRevenue = revenue,
                ExpectedProfit = profit,
                ExpectedMargin = margin,
                RiskScore = riskScore,
                Reasoning = reasoning
            };
        }
        
        /// <summary>
        /// Calculate risk score for a product
        /// </summary>
        private static decimal CalculateRiskScore(ProductDefinition def, DemandScenario demand)
        {
            decimal risk = 0m;
            
            // Demand volatility
            risk += def.DemandVolatility * 0.4m;
            
            // Market position (luxury = higher risk)
            risk += def.Position switch
            {
                MarketPosition.Luxury => 0.3m,
                MarketPosition.Premium => 0.2m,
                MarketPosition.Differentiated => 0.1m,
                MarketPosition.Commodity => 0.05m,
                _ => 0.1m
            };
            
            // Certification requirement (regulatory risk)
            if (def.RequiresCertification)
                risk += 0.1m;
            
            // Seasonal products = higher risk
            if (def.SeasonalDemand)
                risk += 0.15m;
            
            // Quality sensitivity
            risk += def.QualityImpactOnYield * 0.3m;
            
            return Math.Min(1.0m, risk);
        }
        
        /// <summary>
        /// Sort recommendations by strategy
        /// </summary>
        private static IEnumerable<ProductMixRecommendation> SortByStrategy(
            List<ProductMixRecommendation> recommendations,
            OptimizationStrategy strategy)
        {
            return strategy switch
            {
                OptimizationStrategy.MaximizeProfit => recommendations.OrderByDescending(r => r.ExpectedProfit),
                OptimizationStrategy.MaximizeVolume => recommendations.OrderByDescending(r => r.ExpectedRevenue),
                OptimizationStrategy.MaximizeMargin => recommendations.OrderByDescending(r => r.ExpectedMargin),
                OptimizationStrategy.MinimizeRisk => recommendations.OrderBy(r => r.RiskScore),
                OptimizationStrategy.PremiumFocus => recommendations.OrderByDescending(r => r.ExpectedMargin).ThenByDescending(r => r.RiskScore),
                OptimizationStrategy.Balanced => recommendations.OrderByDescending(r => r.ExpectedProfit * (1.0m - r.RiskScore * 0.5m)),
                _ => recommendations.OrderByDescending(r => r.ExpectedProfit)
            };
        }
        
        /// <summary>
        /// Calculate allocation for a product based on strategy
        /// </summary>
        private static int CalculateAllocation(
            ProductMixRecommendation rec,
            int availableCapacity,
            OptimizationStrategy strategy)
        {
            // Base allocation: use portion based on score
            decimal allocationFactor = strategy switch
            {
                OptimizationStrategy.MaximizeProfit or OptimizationStrategy.MaximizeVolume => 1.0m,  // Concentrate
                OptimizationStrategy.Balanced => 0.6m,  // Diversify moderately
                OptimizationStrategy.MinimizeRisk => 0.4m,  // High diversification
                OptimizationStrategy.PremiumFocus => 0.8m,
                OptimizationStrategy.MaximizeMargin => 0.9m,
                _ => 0.7m
            };
            
            int allocation = (int)(availableCapacity * (double)allocationFactor);
            return Math.Min(allocation, availableCapacity);
        }
        
        /// <summary>
        /// Calculate weighted average margin across portfolio
        /// </summary>
        private static decimal CalculateWeightedMargin(List<ProductMixRecommendation> products)
        {
            if (!products.Any()) return 0m;
            
            decimal totalRevenue = products.Sum(p => p.ExpectedRevenue * p.RecommendedKgPerDay);
            if (totalRevenue == 0) return 0m;
            
            decimal weightedMargin = products.Sum(p => 
                p.ExpectedMargin * (p.ExpectedRevenue * p.RecommendedKgPerDay / totalRevenue));
            
            return weightedMargin;
        }
        
        /// <summary>
        /// Calculate portfolio-level risk
        /// </summary>
        private static decimal CalculatePortfolioRisk(List<ProductMixRecommendation> products)
        {
            if (!products.Any()) return 0m;
            
            // Diversification reduces risk
            decimal diversificationFactor = 1.0m / (decimal)Math.Sqrt(products.Count);
            
            decimal avgRisk = products.Average(p => p.RiskScore);
            
            return avgRisk * diversificationFactor;
        }
        
        /// <summary>
        /// Generate human-readable reasoning for product recommendation
        /// </summary>
        private static string GenerateReasoning(
            ProductType product,
            ProductDefinition def,
            decimal profit,
            decimal margin,
            decimal risk,
            OptimizationStrategy strategy)
        {
            var reasons = new List<string>();
            
            if (profit > 50m)
                reasons.Add($"High profit margin ({margin:P0})");
            else if (profit < 10m)
                reasons.Add($"Low profitability ({margin:P0})");
            
            if (risk > 0.5m)
                reasons.Add($"High risk ({risk:P0})");
            else if (risk < 0.2m)
                reasons.Add("Stable demand");
            
            if (def.Position == MarketPosition.Luxury)
                reasons.Add("Premium positioning");
            
            if (def.SeasonalDemand)
                reasons.Add("Seasonal product");
            
            return string.Join(", ", reasons);
        }
        
        /// <summary>
        /// Generate summary of the mix analysis
        /// </summary>
        private static string GenerateSummary(MixAnalysis analysis, OptimizationStrategy strategy)
        {
            var lines = new List<string>
            {
                $"Strategy: {strategy}",
                $"Products selected: {analysis.Recommendations.Count}",
                $"Expected profit: {analysis.TotalExpectedProfit:N0} NOK/day",
                $"Avg margin: {analysis.WeightedAverageMargin:P1}",
                $"Portfolio risk: {analysis.PortfolioRiskScore:P0}"
            };
            
            if (analysis.PortfolioRiskScore > 0.5m)
                lines.Add("⚠️ High-risk portfolio - consider diversification");
            
            if (analysis.WeightedAverageMargin < 0.15m)
                lines.Add("⚠️ Low margins - consider premium products");
            
            return string.Join("\n", lines);
        }
    }
}
