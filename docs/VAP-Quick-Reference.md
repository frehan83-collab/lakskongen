# VAP Processing - Quick Reference

## Basic Usage Examples

### 1. Create a Processing Plant

```csharp
using LakskongenSim.Processing;
using LakskongenSim.Market;

// Create a new processing plant
var plant = new ProcessingPlant
{
    PlantId = "plant-001",
    Name = "Bergen Processing AS",
    Tier = PlantTier.FilletLine,  // Start at tier 2
    TotalCapacityKgPerDay = 50000,
    TotalCapex = 5_000_000m
};

// Add a fillet production line
var filletLine = plant.AddProductionLine(
    ProductType.Fillet,
    "Fillet Line Alpha"
);
```

### 2. Process a Batch

```csharp
// Configure line
filletLine.TechnologyLevel = TechnologyTier.SemiAuto;
filletLine.AverageSkillLevel = SkillLevel.Skilled;

// Process 10 tonnes of fresh salmon
var result = filletLine.ProcessBatch(
    inputKg: 10000,
    inputQuality: 0.9m,          // 90% quality score
    rawMaterialCostPerKg: 60m,   // 60 NOK/kg
    temperatureControl: 0.95m,   // 95% temp stability
    freshnessHours: 8            // 8 hours since harvest
);

// Check results
if (result.Success)
{
    Console.WriteLine($"Produced: {result.NetOutputKg} kg fillets");
    Console.WriteLine($"Yield: {result.YieldPercent:P1}");
    Console.WriteLine($"Revenue: {result.Revenue:N0} NOK");
    Console.WriteLine($"Profit: {result.Profit:N0} NOK");
    Console.WriteLine($"Margin: {result.Margin:P1}");
}
```

### 3. Calculate Optimal Product Mix

```csharp
using LakskongenSim.Processing;

// Setup market conditions
var marketConditions = new Dictionary<ProductType, DemandScenario>
{
    [ProductType.Sashimi] = new()
    {
        Product = ProductType.Sashimi,
        DemandMultiplier = 0.5m,  // Low demand (Japan ban)
        PriceMultiplier = 0.8m,   // Price down 20%
        MarketOpen = true
    },
    [ProductType.ColdSmoked] = new()
    {
        Product = ProductType.ColdSmoked,
        DemandMultiplier = 1.8m,  // High demand (Christmas season)
        PriceMultiplier = 1.15m,  // Premium pricing
        MarketOpen = true
    }
};

// Optimize product mix
var analysis = ProductMixOptimizer.OptimizeProductMix(
    plant,
    availableInputKg: 50000,
    rawMaterialCostPerKg: 60m,
    strategy: OptimizationStrategy.MaximizeProfit,
    marketConditions: marketConditions
);

// Review recommendations
Console.WriteLine(analysis.Summary);
foreach (var rec in analysis.Recommendations)
{
    Console.WriteLine($"{rec.ProductName}: {rec.RecommendedKgPerDay} kg/day");
    Console.WriteLine($"  Expected profit: {rec.ExpectedProfit:N0} NOK");
    Console.WriteLine($"  Margin: {rec.ExpectedMargin:P1}");
    Console.WriteLine($"  Risk: {rec.RiskScore:P0}");
    Console.WriteLine($"  Reasoning: {rec.Reasoning}");
}
```

### 4. Upgrade a Plant

```csharp
// Get available upgrades
var upgrades = plant.GetAvailableUpgrades();

// Find tier upgrade
var tierUpgrade = upgrades.FirstOrDefault(u => u.Name.Contains("ValueAdded"));
if (tierUpgrade != null)
{
    Console.WriteLine($"Upgrade: {tierUpgrade.Name}");
    Console.WriteLine($"Cost: {tierUpgrade.CapexCost:N0} NOK");
    Console.WriteLine($"Downtime: {tierUpgrade.DowntimeDays} days");
    Console.WriteLine($"Benefits: {tierUpgrade.Description}");
    
    // Apply upgrade
    plant.ApplyUpgrade(tierUpgrade);
    Console.WriteLine($"New tier: {plant.Tier}");
    Console.WriteLine($"Available products: {plant.GetAvailableProducts().Count}");
}

// Automate a line
var autoUpgrade = upgrades.FirstOrDefault(u => u.Name.Contains("Automate"));
if (autoUpgrade != null)
{
    plant.ApplyUpgrade(autoUpgrade);
    // Line technology level is now upgraded
}
```

### 5. Build Brand Reputation

```csharp
using LakskongenSim.Processing;

var brand = new BrandReputation
{
    BrandName = "Arctic Premium Salmon",
    Strategy = BrandStrategy.Premium,
    QualityReputation = 60m,
    SustainabilityReputation = 60m,
    ReliabilityReputation = 60m,
    InnovationReputation = 50m
};

// Earn certification
brand.Certifications.Add("ASC");
brand.Certifications.Add("Organic");
brand.RecordEvent(
    ReputationEvent.CertificationEarned,
    "Achieved ASC and Organic certification",
    severity: 2
);

// Win award
brand.RecordEvent(
    ReputationEvent.AwardWon,
    "Best Norwegian Salmon 2026",
    severity: 3
);

// Check pricing power
var multiplier = brand.GetPricingPowerMultiplier(ProductType.Sashimi);
Console.WriteLine($"Brand pricing power: +{(multiplier - 1.0m):P1}");

// Apply to revenue
decimal baseRevenue = result.Revenue;
decimal brandedRevenue = baseRevenue * multiplier;
decimal extraProfit = brandedRevenue - baseRevenue;
Console.WriteLine($"Extra profit from brand: {extraProfit:N0} NOK");
```

### 6. Yield Calculation Deep Dive

```csharp
using LakskongenSim.Processing;

// Calculate yield for different scenarios
var scenarios = new[]
{
    ("Manual, Untrained", TechnologyTier.Manual, SkillLevel.Untrained),
    ("Manual, Skilled", TechnologyTier.Manual, SkillLevel.Skilled),
    ("Semi-Auto, Skilled", TechnologyTier.SemiAuto, SkillLevel.Skilled),
    ("AI-Optimized, Master", TechnologyTier.AIOptimized, SkillLevel.Master)
};

foreach (var (name, tech, skill) in scenarios)
{
    var yieldPercent = YieldCalculator.Calculate(
        ProductType.Fillet,
        inputQuality: 0.85m,
        skillLevel: skill,
        techTier: tech,
        temperatureControl: 0.9m,
        freshnessHours: 12
    );
    
    Console.WriteLine($"{name}: {yieldPercent:P1} yield");
}

// Output:
// Manual, Untrained: 48.5% yield
// Manual, Skilled: 56.2% yield
// Semi-Auto, Skilled: 61.7% yield
// AI-Optimized, Master: 72.1% yield
```

---

## Product Categories Cheat Sheet

### Primary (Tier 1-2)
- **FreshWhole** (95%, 10%) - Minimal processing
- **HOG** (85%, 15%) - Head-off gutted
- **Fillet** (62%, 25%) - Standard fillets
- **FilletSkinless** (58%, 28%) - Skinless
- **Portions** (55%, 32%) - Pre-portioned
- **Frozen** (90%, 12%) - Frozen product

### Value-Added (Tier 3)
- **ColdSmoked** (50%, 38%) - Premium smoked
- **HotSmoked** (45%, 40%) - Hot-smoked
- **Gravlax** (52%, 42%) - Cured salmon
- **Canned** (55%, 35%) - Canned retail

### Premium (Tier 4)
- **Sashimi** (48%, 45%) - Ultra-premium
- **SmokedPate** (35%, 50%) - Boutique
- **SalmonJerky** (30%, 48%) - Specialty
- **ReadyMeal** (40%, 52%) - Branded retail
- **Roe** (4%, 70%) - Caviar (seasonal)

### Byproducts (Tier 5)
- **FishOil** (8%, 65%) - Omega-3 supplements
- **FishMeal** (20%, 40%) - Feed industry
- **FishSkin** (6%, 55%) - Leather/collagen

*(Format: Yield%, Margin%)*

---

## Technology Impact Table

| Tech Level | Yield Boost | Labor Reduction | CAPEX Multiplier |
|------------|-------------|-----------------|------------------|
| Manual | 0% | 0% | 1.0x |
| Semi-Auto | +5% | -20% | 1.5x |
| Automated | +10% | -40% | 2.0x |
| Fully-Auto | +15% | -60% | 2.5x |
| AI-Optimized | +20% | -70% | 3.0x |

---

## Skill Level Impact

| Skill | Yield Modifier | Training Cost | Time to Achieve |
|-------|----------------|---------------|-----------------|
| Untrained | -15% | 0 | Day 1 |
| Basic | -5% | Low | 1 month |
| Competent | 0% (baseline) | Moderate | 6 months |
| Skilled | +3% | Moderate | 1 year |
| Expert | +7% | High | 3 years |
| Master | +12% | Very High | 5+ years |

---

## Plant Tier Progression

| Tier | CAPEX | Products Unlocked | Strategy |
|------|-------|-------------------|----------|
| 1: Basic Gutting | 1M | Whole, HOG, Frozen | Volume play |
| 2: Fillet Line | 3M | Fillets, Portions | Differentiation |
| 3: Value-Added | 8M | Smoked, Gravlax, Canned | Premium focus |
| 4: Premium | 15M | Sashimi, Boutique, Roe | Luxury play |
| 5: Full Integration | 25M | Byproducts (oil, meal, skin) | Margin maximization |

---

## Optimization Strategies

| Strategy | Focus | Risk | Use Case |
|----------|-------|------|----------|
| MaximizeProfit | Highest profit products | Medium | Default for most players |
| MaximizeVolume | Throughput (kg/day) | Low | Commodity focus |
| MaximizeMargin | Highest margin % | High | Cash-rich, risk-tolerant |
| Balanced | Mix of margin + volume | Medium | Diversified portfolio |
| MinimizeRisk | Stable, low-volatility | Very Low | Conservative play |
| PremiumFocus | Luxury positioning | High | Brand-building strategy |

---

## Common Patterns

### Pattern 1: Volume Commodity Play
```csharp
// Start tier 1, automate heavily, maximize throughput
plant.Tier = PlantTier.BasicGutting;
line.ConfiguredProduct = ProductType.HOG;
line.TechnologyLevel = TechnologyTier.FullyAuto;  // Reduce labor
// High volume, low margin, stable demand
```

### Pattern 2: Premium Sashimi Focus
```csharp
// Tier 4, manual precision, brand building
plant.Tier = PlantTier.Premium;
line.ConfiguredProduct = ProductType.Sashimi;
line.AverageSkillLevel = SkillLevel.Master;  // Quality critical
line.TechnologyLevel = TechnologyTier.Manual;  // Human precision
brand.Strategy = BrandStrategy.Luxury;
// Low volume, very high margin, risky (export bans)
```

### Pattern 3: Byproduct Arbitrage
```csharp
// Tier 5, extract maximum value from waste
plant.Tier = PlantTier.FullIntegration;
// Add oil + meal lines
var oilLine = plant.AddProductionLine(ProductType.FishOil, "Oil Extraction");
var mealLine = plant.AddProductionLine(ProductType.FishMeal, "Meal Processing");
// Use trim/offcuts from fillet lines
// 65-70% margins on byproducts
```

---

## Integration Checklist

### ✅ Before Using VAP System
1. Product type enum updated to 17 products
2. Market system supports new product types
3. Finance system ready for CAPEX/revenue tracking
4. Save/load includes Processing namespace

### ✅ Typical Flow
1. Create `ProcessingPlant` at chosen tier
2. Add `ProductionLine` for desired product
3. Configure technology + skill levels
4. Process batches via `ProcessBatch()`
5. Track cumulative profit/ROI
6. Use `ProductMixOptimizer` to guide decisions
7. Apply upgrades when profitable
8. Build `BrandReputation` for pricing power

---

## Performance Notes

- All calculations are deterministic (no RNG in core logic)
- Yield calculations are lightweight (simple arithmetic)
- Product mix optimizer: O(n) where n = available products
- Brand reputation: Incremental updates (event-driven)
- Suitable for real-time simulation (60 FPS game loop)

---

## Troubleshooting

### "Cannot produce product X"
→ Check `plant.Tier` vs `ProductCatalog.Products[X].MinimumPlantTier`

### "Negative profit on every batch"
→ Check raw material cost vs product price, adjust product mix or upgrade to premium

### "Yield lower than expected"
→ Check quality score, skill level, technology tier, freshness hours

### "Brand not affecting price"
→ Check product requires certification (`RequiresCertification = true`) and brand has certs

---

## API Summary

### Key Classes
- `ProductCatalog` - Static product definitions
- `YieldCalculator` - Yield calculation engine
- `ProductionLine` - Individual line simulation
- `ProcessingPlant` - Plant management + upgrades
- `ProductMixOptimizer` - AI recommendations
- `BrandReputation` - Brand building + pricing power

### Key Methods
- `YieldCalculator.Calculate()` - Calculate actual yield
- `ProductionLine.ProcessBatch()` - Run production
- `ProcessingPlant.AddProductionLine()` - Add new line
- `ProcessingPlant.GetAvailableUpgrades()` - List upgrades
- `ProcessingPlant.ApplyUpgrade()` - Execute upgrade
- `ProductMixOptimizer.OptimizeProductMix()` - Get recommendations
- `BrandReputation.GetPricingPowerMultiplier()` - Brand premium
- `BrandReputation.RecordEvent()` - Update reputation

---

**Ready to start processing salmon like a Norwegian tycoon!** 🐟💰
