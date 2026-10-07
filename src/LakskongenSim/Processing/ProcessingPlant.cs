using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Market;

namespace LakskongenSim.Processing
{
    /// <summary>
    /// Processing plant tier determines which products can be produced
    /// </summary>
    public enum PlantTier
    {
        BasicGutting = 1,      // HOG, whole fish processing
        FilletLine = 2,        // Fillets, skin-on/skinless
        ValueAdded = 3,        // Smoked, gravlax, portions, canned
        Premium = 4,           // Sashimi, boutique products, roe
        FullIntegration = 5    // Byproduct utilization (oil, meal, skin)
    }
    
    /// <summary>
    /// Upgrade path for a processing line
    /// </summary>
    public class UpgradeOption
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal CapexCost { get; set; }
        public int DowntimeDays { get; set; }
        public Dictionary<string, object> Benefits { get; set; } = new();
    }
    
    /// <summary>
    /// Represents a complete processing plant with multiple production lines
    /// Handles upgrades, capacity planning, and investment decisions
    /// </summary>
    public class ProcessingPlant
    {
        public string PlantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public PlantTier Tier { get; set; }
        
        // Production lines
        public List<ProductionLine> Lines { get; set; } = new();
        
        // Plant-level characteristics
        public int TotalCapacityKgPerDay { get; set; }
        public decimal FixedOpexPerMonth { get; set; }
        public decimal TotalCapex { get; set; }
        
        // Make vs Buy strategy
        public bool UsesTollProcessing { get; set; } = false;
        public decimal TollProcessingCostPerKg { get; set; }
        
        // Economic tracking
        public decimal CumulativeRevenue { get; set; }
        public decimal CumulativeProfit { get; set; }
        public decimal CumulativeCapex { get; set; }

        // Phase 5.4: Carbon ledger
        /// <summary>Carbon emissions/credits tracker for this processing plant.</summary>
        public Farm.CarbonTracker CarbonTracker { get; set; } = new();

        /// <summary>
        /// Get available upgrade options for this plant
        /// </summary>
        public List<UpgradeOption> GetAvailableUpgrades()
        {
            var upgrades = new List<UpgradeOption>();
            
            // Tier upgrades
            if (Tier < PlantTier.FullIntegration)
            {
                var nextTier = (PlantTier)((int)Tier + 1);
                upgrades.Add(new UpgradeOption
                {
                    Name = $"Upgrade to {nextTier}",
                    Description = GetTierDescription(nextTier),
                    CapexCost = GetTierUpgradeCost(Tier, nextTier),
                    DowntimeDays = 30,
                    Benefits = new()
                    {
                        { "NewProductsUnlocked", GetUnlockedProducts(nextTier).Count },
                        { "Tier", nextTier }
                    }
                });
            }
            
            // Capacity expansion
            upgrades.Add(new UpgradeOption
            {
                Name = "Capacity Expansion (+50%)",
                Description = "Add production capacity to existing lines",
                CapexCost = TotalCapex * 0.35m,  // 35% of original investment
                DowntimeDays = 14,
                Benefits = new()
                {
                    { "CapacityIncrease", 0.5m },
                    { "ThroughputKgPerDay", (int)(TotalCapacityKgPerDay * 0.5m) }
                }
            });
            
            // Automation upgrade for each line
            foreach (var line in Lines)
            {
                if (line.TechnologyLevel < TechnologyTier.AIOptimized)
                {
                    var nextTech = (TechnologyTier)((int)line.TechnologyLevel + 1);
                    upgrades.Add(new UpgradeOption
                    {
                        Name = $"Automate {line.Name} → {nextTech}",
                        Description = $"Upgrade automation level, reduce labor, increase yield",
                        CapexCost = GetAutomationCost(line, nextTech),
                        DowntimeDays = 7,
                        Benefits = new()
                        {
                            { "LineId", line.LineId },
                            { "TechnologyTier", nextTech },
                            { "YieldIncrease", YieldCalculator.GetTechnologyModifier(nextTech) },
                            { "LaborReduction", YieldCalculator.GetLaborReduction(nextTech) }
                        }
                    });
                }
            }
            
            // Quality control system
            foreach (var line in Lines.Where(l => !l.HasQualityControlSystem))
            {
                upgrades.Add(new UpgradeOption
                {
                    Name = $"Install QC System on {line.Name}",
                    Description = "5% price premium, lower reject rate",
                    CapexCost = 500000m,
                    DowntimeDays = 3,
                    Benefits = new()
                    {
                        { "LineId", line.LineId },
                        { "QualitySystem", true },
                        { "PricePremium", 0.05m }
                    }
                });
            }
            
            // Certification
            foreach (var line in Lines.Where(l => !l.HasCertification))
            {
                if (ProductCatalog.Products.TryGetValue(line.ConfiguredProduct, out var def) && def.RequiresCertification)
                {
                    upgrades.Add(new UpgradeOption
                    {
                        Name = $"Obtain Certification for {line.Name}",
                        Description = "ASC/MSC/Organic certification - 8% price premium",
                        CapexCost = 300000m,
                        DowntimeDays = 0,  // No downtime, just audit
                        Benefits = new()
                        {
                            { "LineId", line.LineId },
                            { "Certification", true },
                            { "PricePremium", 0.08m }
                        }
                    });
                }
            }
            
            return upgrades;
        }
        
        /// <summary>
        /// Apply an upgrade to the plant
        /// </summary>
        public bool ApplyUpgrade(UpgradeOption upgrade)
        {
            CumulativeCapex += upgrade.CapexCost;
            
            // Handle tier upgrade
            if (upgrade.Benefits.ContainsKey("Tier"))
            {
                Tier = (PlantTier)upgrade.Benefits["Tier"];
                return true;
            }
            
            // Handle capacity expansion
            if (upgrade.Benefits.ContainsKey("CapacityIncrease"))
            {
                decimal increase = (decimal)upgrade.Benefits["CapacityIncrease"];
                TotalCapacityKgPerDay = (int)(TotalCapacityKgPerDay * (1.0m + increase));
                foreach (var line in Lines)
                {
                    line.CapacityKgPerHour = (int)(line.CapacityKgPerHour * (1.0m + increase));
                    line.TotalCapex += upgrade.CapexCost / Lines.Count;
                }
                return true;
            }
            
            // Handle line-specific upgrades
            if (upgrade.Benefits.ContainsKey("LineId"))
            {
                var lineId = (string)upgrade.Benefits["LineId"];
                var line = Lines.FirstOrDefault(l => l.LineId == lineId);
                if (line == null) return false;
                
                line.TotalCapex += upgrade.CapexCost;
                
                // Technology upgrade
                if (upgrade.Benefits.ContainsKey("TechnologyTier"))
                {
                    line.TechnologyLevel = (TechnologyTier)upgrade.Benefits["TechnologyTier"];
                }
                
                // Quality control
                if (upgrade.Benefits.ContainsKey("QualitySystem"))
                {
                    line.HasQualityControlSystem = true;
                }
                
                // Certification
                if (upgrade.Benefits.ContainsKey("Certification"))
                {
                    line.HasCertification = true;
                }
                
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Add a new production line to the plant
        /// </summary>
        public ProductionLine AddProductionLine(ProductType product, string lineName)
        {
            if (!ProductCatalog.Products.TryGetValue(product, out var def))
                throw new ArgumentException($"Unknown product: {product}");
            
            if (def.MinimumPlantTier > (int)Tier)
                throw new InvalidOperationException($"Plant tier {Tier} cannot produce {product}. Requires tier {def.MinimumPlantTier}+");
            
            var line = new ProductionLine
            {
                LineId = Guid.NewGuid().ToString(),
                Name = lineName,
                ConfiguredProduct = product,
                CapacityKgPerHour = (int)def.ThroughputKgPerHour,
                SkilledWorkers = def.SkilledWorkersRequired,
                UnskilledWorkers = def.UnskilledWorkersRequired,
                HourlyWageCostNOK = def.LaborCostPerKg * (int)def.ThroughputKgPerHour,
                MaintenanceCostPerMonth = GetLineMaintenanceCost(product),
                TotalCapex = GetLineCapex(product)
            };
            
            Lines.Add(line);
            CumulativeCapex += line.TotalCapex;
            
            return line;
        }
        
        /// <summary>
        /// Calculate total plant ROI
        /// </summary>
        public decimal CalculateROI()
        {
            if (CumulativeCapex <= 0) return 0m;
            return CumulativeProfit / CumulativeCapex;
        }
        
        /// <summary>
        /// Get all products this plant can currently produce
        /// </summary>
        public List<ProductType> GetAvailableProducts()
        {
            return ProductCatalog.GetByPlantTier((int)Tier).Select(p => p.Type).ToList();
        }
        
        /// <summary>
        /// Calculate cost to upgrade between tiers
        /// </summary>
        private decimal GetTierUpgradeCost(PlantTier from, PlantTier to)
        {
            return to switch
            {
                PlantTier.FilletLine => 3_000_000m,
                PlantTier.ValueAdded => 8_000_000m,
                PlantTier.Premium => 15_000_000m,
                PlantTier.FullIntegration => 25_000_000m,
                _ => 1_000_000m
            };
        }
        
        /// <summary>
        /// Get description of tier capabilities
        /// </summary>
        private string GetTierDescription(PlantTier tier)
        {
            return tier switch
            {
                PlantTier.BasicGutting => "Gutting and basic processing (HOG, whole fish)",
                PlantTier.FilletLine => "Filleting capability (skin-on, skinless, portions)",
                PlantTier.ValueAdded => "Value-added processing (smoked, gravlax, canned)",
                PlantTier.Premium => "Premium products (sashimi, boutique, roe)",
                PlantTier.FullIntegration => "Full byproduct utilization (oil, meal, skin)",
                _ => "Unknown tier"
            };
        }
        
        /// <summary>
        /// Get products unlocked at a specific tier
        /// </summary>
        private List<ProductType> GetUnlockedProducts(PlantTier tier)
        {
            return ProductCatalog.GetByPlantTier((int)tier)
                .Where(p => p.MinimumPlantTier == (int)tier)
                .Select(p => p.Type)
                .ToList();
        }
        
        /// <summary>
        /// Calculate automation upgrade cost for a line
        /// </summary>
        private decimal GetAutomationCost(ProductionLine line, TechnologyTier targetTier)
        {
            if (!ProductCatalog.Products.TryGetValue(line.ConfiguredProduct, out var def))
                return 1_000_000m;
            
            decimal baseAutoCost = 2_000_000m; // Base automation cost
            return baseAutoCost * def.AutomationCapexMultiplier;
        }
        
        /// <summary>
        /// Get monthly maintenance cost for a product line
        /// </summary>
        private decimal GetLineMaintenanceCost(ProductType product)
        {
            if (!ProductCatalog.Products.TryGetValue(product, out var def))
                return 50000m;
            
            // More complex products = higher maintenance
            return def.Category switch
            {
                ProductCategory.Primary => 30000m,
                ProductCategory.ValueAdded => 50000m,
                ProductCategory.Byproduct => 40000m,
                ProductCategory.Boutique => 60000m,
                _ => 50000m
            };
        }
        
        /// <summary>
        /// Get initial CAPEX for a production line
        /// </summary>
        private decimal GetLineCapex(ProductType product)
        {
            if (!ProductCatalog.Products.TryGetValue(product, out var def))
                return 2_000_000m;
            
            // CAPEX varies by product complexity
            return def.Category switch
            {
                ProductCategory.Primary => 1_500_000m,
                ProductCategory.ValueAdded => 3_500_000m,
                ProductCategory.Byproduct => 5_000_000m,
                ProductCategory.Boutique => 4_000_000m,
                _ => 2_000_000m
            };
        }
    }
}
