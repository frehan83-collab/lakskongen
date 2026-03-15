using System;
using System.Collections.Generic;
using LakskongenSim.Market;

namespace LakskongenSim.Processing
{
    /// <summary>
    /// Product category determines processing tier required and market positioning
    /// </summary>
    public enum ProductCategory
    {
        Primary,      // Core products: whole, HOG, fillets
        ValueAdded,   // Processed: smoked, gravlax, portions, sashimi
        Byproduct,    // Secondary revenue: oil, meal, skin, roe
        Boutique      // Premium niche: pâté, jerky, canned, ready meals
    }

    /// <summary>
    /// Market positioning affects pricing, demand volatility, and branding opportunities
    /// </summary>
    public enum MarketPosition
    {
        Commodity,      // Compete on price, high volume, low margin
        Differentiated, // Organic/sustainable/local, moderate premium
        Premium,        // Quality focus, higher margin, lower volume
        Luxury          // Ultra-premium (sashimi, boutique), very high margin
    }

    /// <summary>
    /// Defines a specific value-added product with all economic and processing parameters
    /// </summary>
    public class ProductDefinition
    {
        public ProductType Type { get; init; }
        public string Name { get; init; } = string.Empty;
        public ProductCategory Category { get; init; }
        public MarketPosition Position { get; init; }
        
        // Processing economics
        public decimal BaseYieldPercent { get; init; }        // % of input that becomes output
        public decimal QualityImpactOnYield { get; init; }     // Modifier: poor quality reduces yield
        public decimal BasePricePerKg { get; init; }           // NOK/kg market price
        public decimal MarginPercent { get; init; }            // Target gross margin %
        
        // Processing requirements
        public int MinimumPlantTier { get; init; }             // 1=basic gutting, 2=fillet, 3=value-add, 4=premium, 5=full integration
        public decimal ThroughputKgPerHour { get; init; }      // Processing speed
        public decimal VariableOpexPerKg { get; init; }        // Direct processing cost per kg output
        
        // Labor & skill
        public int SkilledWorkersRequired { get; init; }       // Per line
        public int UnskilledWorkersRequired { get; init; }
        public decimal LaborCostPerKg { get; init; }           // Combined labor cost
        
        // Quality control
        public decimal RejectRatePercent { get; init; }        // % lost to spoilage/quality
        public bool RequiresCertification { get; init; }       // ASC, MSC, organic, etc.
        
        // Market dynamics
        public decimal DemandVolatility { get; init; }         // 0-1, higher = more price swings
        public bool SeasonalDemand { get; init; }              // Affected by season?
        public string[] TargetMarkets { get; init; } = Array.Empty<string>(); // Geographic markets
        
        // Automation potential
        public decimal AutomationCapexMultiplier { get; init; } // Cost to automate this line
        public decimal AutomationLaborReduction { get; init; }  // % labor saved when automated
    }

    /// <summary>
    /// Central catalog of all available products with realistic Norwegian industry data
    /// </summary>
    public static class ProductCatalog
    {
        public static readonly Dictionary<ProductType, ProductDefinition> Products = new()
        {
            // ===== PRIMARY PRODUCTS =====
            
            [ProductType.FreshWhole] = new()
            {
                Type = ProductType.FreshWhole,
                Name = "Fresh Whole Salmon",
                Category = ProductCategory.Primary,
                Position = MarketPosition.Commodity,
                BaseYieldPercent = 0.95m,  // Minimal processing loss
                QualityImpactOnYield = 0.05m,
                BasePricePerKg = 60m,
                MarginPercent = 0.10m,  // 10% margin - commodity
                MinimumPlantTier = 1,
                ThroughputKgPerHour = 5000m,
                VariableOpexPerKg = 0.5m,
                SkilledWorkersRequired = 1,
                UnskilledWorkersRequired = 3,
                LaborCostPerKg = 1.2m,
                RejectRatePercent = 0.02m,
                RequiresCertification = false,
                DemandVolatility = 0.15m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "Norway", "EU", "Asia" },
                AutomationCapexMultiplier = 0.5m,
                AutomationLaborReduction = 0.30m
            },
            
            [ProductType.HOG] = new()
            {
                Type = ProductType.HOG,
                Name = "Head-Off Gutted (HOG)",
                Category = ProductCategory.Primary,
                Position = MarketPosition.Commodity,
                BaseYieldPercent = 0.85m,  // Lose head, guts, blood
                QualityImpactOnYield = 0.08m,
                BasePricePerKg = 70m,
                MarginPercent = 0.15m,
                MinimumPlantTier = 1,
                ThroughputKgPerHour = 3000m,
                VariableOpexPerKg = 1.5m,
                SkilledWorkersRequired = 2,
                UnskilledWorkersRequired = 4,
                LaborCostPerKg = 2.0m,
                RejectRatePercent = 0.03m,
                RequiresCertification = false,
                DemandVolatility = 0.12m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "EU", "Russia", "China" },
                AutomationCapexMultiplier = 1.0m,
                AutomationLaborReduction = 0.40m
            },
            
            [ProductType.Fillet] = new()
            {
                Type = ProductType.Fillet,
                Name = "Fresh Fillets",
                Category = ProductCategory.Primary,
                Position = MarketPosition.Differentiated,
                BaseYieldPercent = 0.62m,  // Industry standard 60-65%
                QualityImpactOnYield = 0.10m,
                BasePricePerKg = 150m,
                MarginPercent = 0.25m,
                MinimumPlantTier = 2,
                ThroughputKgPerHour = 1500m,
                VariableOpexPerKg = 4.0m,
                SkilledWorkersRequired = 4,
                UnskilledWorkersRequired = 2,
                LaborCostPerKg = 5.5m,
                RejectRatePercent = 0.05m,
                RequiresCertification = false,
                DemandVolatility = 0.18m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "Norway", "EU", "USA" },
                AutomationCapexMultiplier = 1.5m,
                AutomationLaborReduction = 0.35m
            },
            
            [ProductType.FilletSkinless] = new()
            {
                Type = ProductType.FilletSkinless,
                Name = "Skinless Fillets",
                Category = ProductCategory.Primary,
                Position = MarketPosition.Differentiated,
                BaseYieldPercent = 0.58m,  // Skin removed
                QualityImpactOnYield = 0.12m,
                BasePricePerKg = 165m,
                MarginPercent = 0.28m,
                MinimumPlantTier = 2,
                ThroughputKgPerHour = 1200m,
                VariableOpexPerKg = 5.0m,
                SkilledWorkersRequired = 5,
                UnskilledWorkersRequired = 2,
                LaborCostPerKg = 6.5m,
                RejectRatePercent = 0.06m,
                RequiresCertification = false,
                DemandVolatility = 0.20m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "EU", "USA", "Japan" },
                AutomationCapexMultiplier = 1.8m,
                AutomationLaborReduction = 0.40m
            },
            
            [ProductType.Portions] = new()
            {
                Type = ProductType.Portions,
                Name = "Pre-Portioned Fillets",
                Category = ProductCategory.ValueAdded,
                Position = MarketPosition.Premium,
                BaseYieldPercent = 0.55m,  // Trimming + portioning waste
                QualityImpactOnYield = 0.15m,
                BasePricePerKg = 180m,
                MarginPercent = 0.32m,
                MinimumPlantTier = 3,
                ThroughputKgPerHour = 800m,
                VariableOpexPerKg = 8.0m,
                SkilledWorkersRequired = 3,
                UnskilledWorkersRequired = 3,
                LaborCostPerKg = 8.0m,
                RejectRatePercent = 0.08m,
                RequiresCertification = false,
                DemandVolatility = 0.22m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "USA", "UK", "Germany" },
                AutomationCapexMultiplier = 2.0m,
                AutomationLaborReduction = 0.50m
            },
            
            // ===== VALUE-ADDED PRODUCTS =====
            
            [ProductType.ColdSmoked] = new()
            {
                Type = ProductType.ColdSmoked,
                Name = "Cold-Smoked Salmon",
                Category = ProductCategory.ValueAdded,
                Position = MarketPosition.Premium,
                BaseYieldPercent = 0.50m,  // Smoking reduces weight
                QualityImpactOnYield = 0.18m,
                BasePricePerKg = 280m,
                MarginPercent = 0.38m,
                MinimumPlantTier = 3,
                ThroughputKgPerHour = 400m,
                VariableOpexPerKg = 15.0m,
                SkilledWorkersRequired = 6,
                UnskilledWorkersRequired = 2,
                LaborCostPerKg = 12.0m,
                RejectRatePercent = 0.10m,
                RequiresCertification = true,
                DemandVolatility = 0.25m,
                SeasonalDemand = true,  // Christmas/holidays peak
                TargetMarkets = new[] { "Norway", "UK", "Germany", "France" },
                AutomationCapexMultiplier = 1.2m,
                AutomationLaborReduction = 0.25m
            },
            
            [ProductType.HotSmoked] = new()
            {
                Type = ProductType.HotSmoked,
                Name = "Hot-Smoked Salmon",
                Category = ProductCategory.ValueAdded,
                Position = MarketPosition.Premium,
                BaseYieldPercent = 0.45m,  // More weight loss than cold
                QualityImpactOnYield = 0.20m,
                BasePricePerKg = 320m,
                MarginPercent = 0.40m,
                MinimumPlantTier = 3,
                ThroughputKgPerHour = 350m,
                VariableOpexPerKg = 18.0m,
                SkilledWorkersRequired = 6,
                UnskilledWorkersRequired = 2,
                LaborCostPerKg = 14.0m,
                RejectRatePercent = 0.12m,
                RequiresCertification = true,
                DemandVolatility = 0.28m,
                SeasonalDemand = true,
                TargetMarkets = new[] { "USA", "UK", "France" },
                AutomationCapexMultiplier = 1.3m,
                AutomationLaborReduction = 0.25m
            },
            
            [ProductType.Gravlax] = new()
            {
                Type = ProductType.Gravlax,
                Name = "Gravlax (Cured Salmon)",
                Category = ProductCategory.ValueAdded,
                Position = MarketPosition.Luxury,
                BaseYieldPercent = 0.52m,  // Salt cure, some moisture loss
                QualityImpactOnYield = 0.22m,
                BasePricePerKg = 350m,
                MarginPercent = 0.42m,
                MinimumPlantTier = 3,
                ThroughputKgPerHour = 300m,
                VariableOpexPerKg = 20.0m,
                SkilledWorkersRequired = 8,
                UnskilledWorkersRequired = 1,
                LaborCostPerKg = 16.0m,
                RejectRatePercent = 0.08m,
                RequiresCertification = true,
                DemandVolatility = 0.30m,
                SeasonalDemand = true,  // Nordic Christmas tradition
                TargetMarkets = new[] { "Norway", "Sweden", "Denmark", "USA" },
                AutomationCapexMultiplier = 1.0m,
                AutomationLaborReduction = 0.20m
            },
            
            [ProductType.Sashimi] = new()
            {
                Type = ProductType.Sashimi,
                Name = "Sashimi-Grade Salmon",
                Category = ProductCategory.ValueAdded,
                Position = MarketPosition.Luxury,
                BaseYieldPercent = 0.48m,  // Ultra-precise cutting, high trim waste
                QualityImpactOnYield = 0.30m,  // Quality is CRITICAL
                BasePricePerKg = 450m,
                MarginPercent = 0.45m,
                MinimumPlantTier = 4,
                ThroughputKgPerHour = 200m,
                VariableOpexPerKg = 25.0m,
                SkilledWorkersRequired = 10,
                UnskilledWorkersRequired = 0,
                LaborCostPerKg = 22.0m,
                RejectRatePercent = 0.15m,
                RequiresCertification = true,  // Strict food safety
                DemandVolatility = 0.40m,  // Export restrictions = volatility
                SeasonalDemand = false,
                TargetMarkets = new[] { "Japan", "USA", "Hong Kong", "Singapore" },
                AutomationCapexMultiplier = 0.5m,  // Mostly manual precision work
                AutomationLaborReduction = 0.10m
            },
            
            // ===== BYPRODUCTS =====
            
            [ProductType.FishOil] = new()
            {
                Type = ProductType.FishOil,
                Name = "Omega-3 Fish Oil",
                Category = ProductCategory.Byproduct,
                Position = MarketPosition.Premium,
                BaseYieldPercent = 0.08m,  // Extract from trim/offcuts
                QualityImpactOnYield = 0.05m,
                BasePricePerKg = 120m,
                MarginPercent = 0.65m,  // HIGH margin byproduct
                MinimumPlantTier = 5,
                ThroughputKgPerHour = 500m,
                VariableOpexPerKg = 10.0m,
                SkilledWorkersRequired = 2,
                UnskilledWorkersRequired = 1,
                LaborCostPerKg = 4.0m,
                RejectRatePercent = 0.05m,
                RequiresCertification = true,
                DemandVolatility = 0.15m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "Global (supplements)" },
                AutomationCapexMultiplier = 2.5m,
                AutomationLaborReduction = 0.60m
            },
            
            [ProductType.FishMeal] = new()
            {
                Type = ProductType.FishMeal,
                Name = "Fish Meal (Feed)",
                Category = ProductCategory.Byproduct,
                Position = MarketPosition.Commodity,
                BaseYieldPercent = 0.20m,  // Heads, frames, trim
                QualityImpactOnYield = 0.02m,
                BasePricePerKg = 15m,
                MarginPercent = 0.40m,
                MinimumPlantTier = 5,
                ThroughputKgPerHour = 1000m,
                VariableOpexPerKg = 2.0m,
                SkilledWorkersRequired = 1,
                UnskilledWorkersRequired = 2,
                LaborCostPerKg = 1.5m,
                RejectRatePercent = 0.02m,
                RequiresCertification = false,
                DemandVolatility = 0.10m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "Norway", "Chile", "Global (aquaculture)" },
                AutomationCapexMultiplier = 3.0m,
                AutomationLaborReduction = 0.70m
            },
            
            [ProductType.FishSkin] = new()
            {
                Type = ProductType.FishSkin,
                Name = "Fish Skin (Leather/Collagen)",
                Category = ProductCategory.Byproduct,
                Position = MarketPosition.Premium,
                BaseYieldPercent = 0.06m,
                QualityImpactOnYield = 0.10m,
                BasePricePerKg = 80m,
                MarginPercent = 0.55m,
                MinimumPlantTier = 5,
                ThroughputKgPerHour = 300m,
                VariableOpexPerKg = 8.0m,
                SkilledWorkersRequired = 3,
                UnskilledWorkersRequired = 1,
                LaborCostPerKg = 6.0m,
                RejectRatePercent = 0.08m,
                RequiresCertification = false,
                DemandVolatility = 0.35m,  // Niche market
                SeasonalDemand = false,
                TargetMarkets = new[] { "EU", "USA", "Fashion industry" },
                AutomationCapexMultiplier = 1.5m,
                AutomationLaborReduction = 0.40m
            },
            
            [ProductType.Roe] = new()
            {
                Type = ProductType.Roe,
                Name = "Salmon Roe (Caviar)",
                Category = ProductCategory.Byproduct,
                Position = MarketPosition.Luxury,
                BaseYieldPercent = 0.04m,  // Seasonal, female only
                QualityImpactOnYield = 0.25m,
                BasePricePerKg = 400m,
                MarginPercent = 0.70m,  // Very high margin
                MinimumPlantTier = 4,
                ThroughputKgPerHour = 100m,
                VariableOpexPerKg = 30.0m,
                SkilledWorkersRequired = 4,
                UnskilledWorkersRequired = 0,
                LaborCostPerKg = 18.0m,
                RejectRatePercent = 0.20m,
                RequiresCertification = true,
                DemandVolatility = 0.45m,
                SeasonalDemand = true,  // Spawning season dependent
                TargetMarkets = new[] { "Japan", "Russia", "USA", "EU" },
                AutomationCapexMultiplier = 0.3m,  // Manual harvest
                AutomationLaborReduction = 0.05m
            },
            
            // ===== BOUTIQUE PRODUCTS =====
            
            [ProductType.SmokedPate] = new()
            {
                Type = ProductType.SmokedPate,
                Name = "Smoked Salmon Pâté",
                Category = ProductCategory.Boutique,
                Position = MarketPosition.Premium,
                BaseYieldPercent = 0.35m,  // Use trim + mix ingredients
                QualityImpactOnYield = 0.15m,
                BasePricePerKg = 220m,
                MarginPercent = 0.50m,
                MinimumPlantTier = 4,
                ThroughputKgPerHour = 250m,
                VariableOpexPerKg = 22.0m,
                SkilledWorkersRequired = 4,
                UnskilledWorkersRequired = 2,
                LaborCostPerKg = 12.0m,
                RejectRatePercent = 0.10m,
                RequiresCertification = true,
                DemandVolatility = 0.30m,
                SeasonalDemand = true,
                TargetMarkets = new[] { "France", "UK", "Germany", "USA" },
                AutomationCapexMultiplier = 1.8m,
                AutomationLaborReduction = 0.45m
            },
            
            [ProductType.SalmonJerky] = new()
            {
                Type = ProductType.SalmonJerky,
                Name = "Salmon Jerky",
                Category = ProductCategory.Boutique,
                Position = MarketPosition.Premium,
                BaseYieldPercent = 0.30m,  // Heavy dehydration
                QualityImpactOnYield = 0.18m,
                BasePricePerKg = 380m,
                MarginPercent = 0.48m,
                MinimumPlantTier = 4,
                ThroughputKgPerHour = 150m,
                VariableOpexPerKg = 28.0m,
                SkilledWorkersRequired = 3,
                UnskilledWorkersRequired = 2,
                LaborCostPerKg = 14.0m,
                RejectRatePercent = 0.12m,
                RequiresCertification = true,
                DemandVolatility = 0.35m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "USA", "Norway", "Outdoor/Sports retail" },
                AutomationCapexMultiplier = 1.5m,
                AutomationLaborReduction = 0.35m
            },
            
            [ProductType.Canned] = new()
            {
                Type = ProductType.Canned,
                Name = "Canned Salmon",
                Category = ProductCategory.Boutique,
                Position = MarketPosition.Differentiated,
                BaseYieldPercent = 0.55m,  // Can use lower-grade fish
                QualityImpactOnYield = 0.08m,
                BasePricePerKg = 95m,
                MarginPercent = 0.35m,
                MinimumPlantTier = 3,
                ThroughputKgPerHour = 600m,
                VariableOpexPerKg = 12.0m,
                SkilledWorkersRequired = 2,
                UnskilledWorkersRequired = 4,
                LaborCostPerKg = 8.0m,
                RejectRatePercent = 0.05m,
                RequiresCertification = true,
                DemandVolatility = 0.12m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "USA", "UK", "Global retail" },
                AutomationCapexMultiplier = 2.5m,
                AutomationLaborReduction = 0.60m
            },
            
            [ProductType.ReadyMeal] = new()
            {
                Type = ProductType.ReadyMeal,
                Name = "Ready Meals (Branded Retail)",
                Category = ProductCategory.Boutique,
                Position = MarketPosition.Premium,
                BaseYieldPercent = 0.40m,  // Mixed with vegetables, sauces
                QualityImpactOnYield = 0.20m,
                BasePricePerKg = 260m,
                MarginPercent = 0.52m,
                MinimumPlantTier = 4,
                ThroughputKgPerHour = 400m,
                VariableOpexPerKg = 35.0m,  // Ingredients + packaging
                SkilledWorkersRequired = 6,
                UnskilledWorkersRequired = 4,
                LaborCostPerKg = 18.0m,
                RejectRatePercent = 0.08m,
                RequiresCertification = true,
                DemandVolatility = 0.25m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "UK", "Norway", "Sweden", "USA retail" },
                AutomationCapexMultiplier = 2.2m,
                AutomationLaborReduction = 0.50m
            },
            
            [ProductType.Frozen] = new()
            {
                Type = ProductType.Frozen,
                Name = "Frozen Whole/Fillets",
                Category = ProductCategory.Primary,
                Position = MarketPosition.Commodity,
                BaseYieldPercent = 0.90m,  // Minimal processing
                QualityImpactOnYield = 0.05m,
                BasePricePerKg = 55m,
                MarginPercent = 0.12m,
                MinimumPlantTier = 1,
                ThroughputKgPerHour = 2000m,
                VariableOpexPerKg = 2.0m,
                SkilledWorkersRequired = 1,
                UnskilledWorkersRequired = 3,
                LaborCostPerKg = 2.5m,
                RejectRatePercent = 0.03m,
                RequiresCertification = false,
                DemandVolatility = 0.10m,
                SeasonalDemand = false,
                TargetMarkets = new[] { "Global", "Bulk export" },
                AutomationCapexMultiplier = 1.5m,
                AutomationLaborReduction = 0.50m
            }
        };
        
        /// <summary>
        /// Get all products for a specific category
        /// </summary>
        public static IEnumerable<ProductDefinition> GetByCategory(ProductCategory category)
        {
            foreach (var product in Products.Values)
            {
                if (product.Category == category)
                    yield return product;
            }
        }
        
        /// <summary>
        /// Get all products available at a given plant tier
        /// </summary>
        public static IEnumerable<ProductDefinition> GetByPlantTier(int tier)
        {
            foreach (var product in Products.Values)
            {
                if (product.MinimumPlantTier <= tier)
                    yield return product;
            }
        }
        
        /// <summary>
        /// Calculate actual yield accounting for quality
        /// </summary>
        public static decimal CalculateActualYield(ProductType product, decimal qualityScore)
        {
            if (!Products.TryGetValue(product, out var def))
                throw new ArgumentException($"Unknown product: {product}");
                
            // Quality score 0-1, where 1.0 = perfect
            // Poor quality reduces yield by QualityImpactOnYield percentage
            decimal qualityPenalty = (1.0m - qualityScore) * def.QualityImpactOnYield;
            return Math.Max(0m, def.BaseYieldPercent - qualityPenalty);
        }
    }
}
