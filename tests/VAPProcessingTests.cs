using System;
using System.Linq;
using Xunit;
using LakskongenSim.Processing;
using LakskongenSim.Market;

namespace LakskongenSim.Tests
{
    public class VAPProcessingTests
    {
        [Fact]
        public void ProductCatalog_ContainsAllExpectedProducts()
        {
            // Should have all 17 products
            Assert.Equal(17, ProductCatalog.Products.Count);
            
            // Check primary products exist
            Assert.True(ProductCatalog.Products.ContainsKey(ProductType.FreshWhole));
            Assert.True(ProductCatalog.Products.ContainsKey(ProductType.HOG));
            Assert.True(ProductCatalog.Products.ContainsKey(ProductType.Fillet));
            
            // Check value-added
            Assert.True(ProductCatalog.Products.ContainsKey(ProductType.ColdSmoked));
            Assert.True(ProductCatalog.Products.ContainsKey(ProductType.Sashimi));
            
            // Check byproducts
            Assert.True(ProductCatalog.Products.ContainsKey(ProductType.FishOil));
            Assert.True(ProductCatalog.Products.ContainsKey(ProductType.Roe));
            
            // Check boutique
            Assert.True(ProductCatalog.Products.ContainsKey(ProductType.ReadyMeal));
        }
        
        [Fact]
        public void ProductCatalog_YieldsAreRealistic()
        {
            // Fillet yield should be 60-65%
            var fillet = ProductCatalog.Products[ProductType.Fillet];
            Assert.InRange(fillet.BaseYieldPercent, 0.60m, 0.65m);
            
            // HOG should be ~85%
            var hog = ProductCatalog.Products[ProductType.HOG];
            Assert.InRange(hog.BaseYieldPercent, 0.80m, 0.90m);
            
            // Smoked should lose weight (50%)
            var smoked = ProductCatalog.Products[ProductType.ColdSmoked];
            Assert.InRange(smoked.BaseYieldPercent, 0.45m, 0.55m);
            
            // Fish oil is byproduct (low yield)
            var oil = ProductCatalog.Products[ProductType.FishOil];
            Assert.InRange(oil.BaseYieldPercent, 0.05m, 0.10m);
        }
        
        [Fact]
        public void ProductCatalog_MarginsReflectMarketReality()
        {
            // Commodity products have low margins
            var whole = ProductCatalog.Products[ProductType.FreshWhole];
            Assert.InRange(whole.MarginPercent, 0.08m, 0.15m);
            
            // Premium products have higher margins
            var sashimi = ProductCatalog.Products[ProductType.Sashimi];
            Assert.InRange(sashimi.MarginPercent, 0.40m, 0.50m);
            
            // Byproducts have excellent margins
            var oil = ProductCatalog.Products[ProductType.FishOil];
            Assert.InRange(oil.MarginPercent, 0.60m, 0.70m);
        }
        
        [Fact]
        public void YieldCalculator_QualityImpactsYield()
        {
            // Perfect quality
            var perfectYield = YieldCalculator.Calculate(ProductType.Fillet, inputQuality: 1.0m);
            
            // Poor quality
            var poorYield = YieldCalculator.Calculate(ProductType.Fillet, inputQuality: 0.5m);
            
            // Poor quality should reduce yield
            Assert.True(poorYield < perfectYield);
            
            // Difference should be meaningful (at least 5%)
            Assert.True(perfectYield - poorYield >= 0.05m);
        }
        
        [Fact]
        public void YieldCalculator_SkillLevelMatters()
        {
            decimal inputQuality = 0.9m;
            
            // Untrained vs Master
            var untrainedYield = YieldCalculator.Calculate(
                ProductType.Fillet, inputQuality, SkillLevel.Untrained);
            
            var masterYield = YieldCalculator.Calculate(
                ProductType.Fillet, inputQuality, SkillLevel.Master);
            
            // Master should achieve significantly higher yield
            Assert.True(masterYield > untrainedYield);
            
            // Gap should be at least 15% (master +12%, untrained -15% = 27% difference potential)
            Assert.True(masterYield - untrainedYield >= 0.15m);
        }
        
        [Fact]
        public void YieldCalculator_TechnologyImprovesYield()
        {
            decimal inputQuality = 0.85m;
            
            var manualYield = YieldCalculator.Calculate(
                ProductType.Fillet, inputQuality, techTier: TechnologyTier.Manual);
            
            var aiYield = YieldCalculator.Calculate(
                ProductType.Fillet, inputQuality, techTier: TechnologyTier.AIOptimized);
            
            // AI should beat manual by ~20%
            Assert.True(aiYield > manualYield);
            Assert.InRange(aiYield - manualYield, 0.10m, 0.25m);
        }
        
        [Fact]
        public void YieldCalculator_FreshnessAffectsPremiumProducts()
        {
            // Sashimi is freshness-sensitive
            var freshSashimi = YieldCalculator.Calculate(
                ProductType.Sashimi, inputQuality: 1.0m, freshnessHours: 0);
            
            var oldSashimi = YieldCalculator.Calculate(
                ProductType.Sashimi, inputQuality: 1.0m, freshnessHours: 24);
            
            // Old fish should have lower yield for premium
            Assert.True(freshSashimi > oldSashimi);
            
            // Commodity products less affected
            var freshWhole = YieldCalculator.Calculate(
                ProductType.Frozen, inputQuality: 1.0m, freshnessHours: 0);
            
            var oldWhole = YieldCalculator.Calculate(
                ProductType.Frozen, inputQuality: 1.0m, freshnessHours: 24);
            
            decimal sashimiDelta = freshSashimi - oldSashimi;
            decimal wholeDelta = freshWhole - oldWhole;
            
            // Sashimi should be more affected
            Assert.True(sashimiDelta > wholeDelta);
        }
        
        [Fact]
        public void ProductionLine_ProcessesBatchCorrectly()
        {
            var line = new ProductionLine
            {
                LineId = "test-1",
                Name = "Fillet Line A",
                ConfiguredProduct = ProductType.Fillet,
                TechnologyLevel = TechnologyTier.Manual,
                AverageSkillLevel = SkillLevel.Competent,
                CapacityKgPerHour = 1500,
                UptimePercent = 1.0m  // 100% for test
            };
            
            var result = line.ProcessBatch(
                inputKg: 10000,
                inputQuality: 0.9m,
                rawMaterialCostPerKg: 60m);
            
            Assert.True(result.Success);
            Assert.True(result.NetOutputKg > 0);
            Assert.True(result.NetOutputKg < result.InputKg);  // Yield < 100%
            Assert.True(result.Revenue > 0);
            Assert.True(result.Profit != 0);  // Could be positive or negative
            
            // Fillet yield should be ~60% with good quality
            decimal yieldPercent = (decimal)result.NetOutputKg / result.InputKg;
            Assert.InRange(yieldPercent, 0.50m, 0.70m);
        }
        
        [Fact]
        public void ProductionLine_RejectsLowerOutput()
        {
            var line = new ProductionLine
            {
                ConfiguredProduct = ProductType.Sashimi,
                AverageSkillLevel = SkillLevel.Competent,
                UptimePercent = 1.0m
            };
            
            // High quality = fewer rejects
            var highQResult = line.ProcessBatch(10000, inputQuality: 0.95m, rawMaterialCostPerKg: 60m);
            
            // Low quality = more rejects
            var lowQResult = line.ProcessBatch(10000, inputQuality: 0.5m, rawMaterialCostPerKg: 60m);
            
            // Low quality should have more rejects
            Assert.True(lowQResult.RejectedKg > highQResult.RejectedKg);
        }
        
        [Fact]
        public void ProductionLine_MaintenanceImpactsUptime()
        {
            var line = new ProductionLine
            {
                ConfiguredProduct = ProductType.Fillet,
                UptimePercent = 0.95m,
                DaysSinceLastMaintenance = 0
            };
            
            // Fresh maintenance
            Assert.True(line.IsOperational());
            
            // Simulate aging
            line.DaysSinceLastMaintenance = 40;
            // Uptime should degrade (but test may still pass due to RNG)
            
            // Perform maintenance
            var maint = line.PerformMaintenance();
            Assert.True(maint.Cost > 0);
            Assert.Equal(0, line.DaysSinceLastMaintenance);
            Assert.Equal(0.98m, line.UptimePercent);
        }
        
        [Fact]
        public void ProcessingPlant_TierRestrictsProducts()
        {
            var plant = new ProcessingPlant
            {
                PlantId = "plant-1",
                Tier = PlantTier.BasicGutting
            };
            
            // Should be able to add HOG (tier 1)
            var hogLine = plant.AddProductionLine(ProductType.HOG, "HOG Line 1");
            Assert.NotNull(hogLine);
            
            // Should NOT be able to add sashimi (tier 4)
            Assert.Throws<InvalidOperationException>(() =>
                plant.AddProductionLine(ProductType.Sashimi, "Sashimi Line"));
        }
        
        [Fact]
        public void ProcessingPlant_UpgradeUnlocksProducts()
        {
            var plant = new ProcessingPlant
            {
                Tier = PlantTier.BasicGutting,
                TotalCapacityKgPerDay = 10000,
                TotalCapex = 2_000_000m
            };
            
            var availableBefore = plant.GetAvailableProducts();
            
            // Upgrade to tier 2
            var upgrades = plant.GetAvailableUpgrades();
            var tierUpgrade = upgrades.FirstOrDefault(u => u.Name.Contains("FilletLine"));
            
            Assert.NotNull(tierUpgrade);
            plant.ApplyUpgrade(tierUpgrade);
            
            Assert.Equal(PlantTier.FilletLine, plant.Tier);
            
            var availableAfter = plant.GetAvailableProducts();
            Assert.True(availableAfter.Count > availableBefore.Count);
            Assert.Contains(ProductType.Fillet, availableAfter);
        }
        
        [Fact]
        public void ProcessingPlant_CapacityExpansionWorks()
        {
            var plant = new ProcessingPlant
            {
                Tier = PlantTier.FilletLine,
                TotalCapacityKgPerDay = 10000,
                TotalCapex = 5_000_000m
            };
            
            var line = plant.AddProductionLine(ProductType.Fillet, "Line 1");
            int originalCapacity = line.CapacityKgPerHour;
            
            var upgrades = plant.GetAvailableUpgrades();
            var capacityUpgrade = upgrades.FirstOrDefault(u => u.Name.Contains("Capacity Expansion"));
            
            Assert.NotNull(capacityUpgrade);
            plant.ApplyUpgrade(capacityUpgrade);
            
            // Line capacity should increase
            Assert.True(line.CapacityKgPerHour > originalCapacity);
        }
        
        [Fact]
        public void ProductMixOptimizer_MaximizeProfitStrategy()
        {
            var plant = new ProcessingPlant
            {
                Tier = PlantTier.Premium,  // Access to all products
                TotalCapacityKgPerDay = 50000
            };
            
            var analysis = ProductMixOptimizer.OptimizeProductMix(
                plant,
                availableInputKg: 50000,
                rawMaterialCostPerKg: 60m,
                strategy: OptimizationStrategy.MaximizeProfit);
            
            Assert.NotNull(analysis);
            Assert.NotEmpty(analysis.Recommendations);
            Assert.True(analysis.TotalExpectedProfit > 0);
            
            // Should recommend high-margin products
            var topRec = analysis.Recommendations.First();
            Assert.True(topRec.ExpectedMargin > 0.2m);  // At least 20% margin
        }
        
        [Fact]
        public void ProductMixOptimizer_MinimizeRiskStrategy()
        {
            var plant = new ProcessingPlant
            {
                Tier = PlantTier.Premium,
                TotalCapacityKgPerDay = 50000
            };
            
            var analysis = ProductMixOptimizer.OptimizeProductMix(
                plant,
                availableInputKg: 50000,
                rawMaterialCostPerKg: 60m,
                strategy: OptimizationStrategy.MinimizeRisk);
            
            Assert.NotNull(analysis);
            
            // Portfolio risk should be low
            Assert.True(analysis.PortfolioRiskScore < 0.4m);
            
            // Should favor commodity/stable products
            Assert.Contains(analysis.Recommendations, r => 
                ProductCatalog.Products[r.Product].Position == MarketPosition.Commodity);
        }
        
        [Fact]
        public void BrandReputation_PricingPowerIncreasesWithScore()
        {
            var brand = new BrandReputation
            {
                BrandName = "Premium Salmon Co",
                Strategy = BrandStrategy.Premium,
                QualityReputation = 80m,
                SustainabilityReputation = 70m,
                ReliabilityReputation = 75m,
                InnovationReputation = 60m
            };
            
            brand.Certifications.Add("ASC");
            brand.Certifications.Add("Organic");
            brand.AwardsCount = 2;
            
            var multiplier = brand.GetPricingPowerMultiplier(ProductType.Sashimi);
            
            // Strong brand should command premium (>10%)
            Assert.True(multiplier >= 1.10m);
            
            // Weak brand
            var weakBrand = new BrandReputation
            {
                Strategy = BrandStrategy.Commodity,
                QualityReputation = 40m,
                SustainabilityReputation = 35m,
                ReliabilityReputation = 45m
            };
            
            var weakMultiplier = weakBrand.GetPricingPowerMultiplier(ProductType.FreshWhole);
            Assert.True(weakMultiplier < multiplier);
        }
        
        [Fact]
        public void BrandReputation_EventsImpactScores()
        {
            var brand = new BrandReputation
            {
                Strategy = BrandStrategy.Premium,
                QualityReputation = 60m,
                SustainabilityReputation = 60m
            };
            
            decimal initialQuality = brand.QualityReputation;
            
            // Positive event
            brand.RecordEvent(ReputationEvent.AwardWon, "Best Salmon 2026", severity: 2);
            
            Assert.True(brand.QualityReputation > initialQuality);
            Assert.Equal(1, brand.AwardsCount);
            
            // Negative event
            decimal beforeIncident = brand.QualityReputation;
            brand.RecordEvent(ReputationEvent.QualityIssue, "Recall batch X", severity: 3);
            
            Assert.True(brand.QualityReputation < beforeIncident);
        }
        
        [Fact]
        public void Integration_CompleteProductionCycle()
        {
            // Setup plant
            var plant = new ProcessingPlant
            {
                PlantId = "integration-test",
                Tier = PlantTier.ValueAdded,
                TotalCapacityKgPerDay = 20000,
                TotalCapex = 8_000_000m
            };
            
            // Add production line
            var line = plant.AddProductionLine(ProductType.ColdSmoked, "Smokehouse A");
            line.TechnologyLevel = TechnologyTier.SemiAuto;
            line.AverageSkillLevel = SkillLevel.Skilled;
            
            // Brand
            var brand = new BrandReputation
            {
                BrandName = "Arctic Premium",
                Strategy = BrandStrategy.Premium,
                QualityReputation = 75m,
                SustainabilityReputation = 70m,
                ReliabilityReputation = 80m
            };
            brand.Certifications.Add("ASC");
            
            // Process batch
            var result = line.ProcessBatch(
                inputKg: 5000,
                inputQuality: 0.9m,
                rawMaterialCostPerKg: 60m,
                temperatureControl: 0.95m,
                freshnessHours: 6);
            
            Assert.True(result.Success);
            Assert.True(result.Profit > 0, "Premium smoked salmon should be profitable");
            
            // Apply brand pricing power
            decimal brandMultiplier = brand.GetPricingPowerMultiplier(ProductType.ColdSmoked);
            decimal brandedRevenue = result.Revenue * brandMultiplier;
            decimal brandedProfit = brandedRevenue - result.TotalCost;
            
            Assert.True(brandedProfit > result.Profit, "Brand should increase profit");
            
            // Update plant tracking
            plant.CumulativeRevenue += brandedRevenue;
            plant.CumulativeProfit += brandedProfit;
            
            // Check ROI
            decimal roi = plant.CalculateROI();
            Assert.True(roi > 0);
        }
    }
}
