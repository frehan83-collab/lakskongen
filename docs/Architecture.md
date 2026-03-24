# Lakskongen — System Architecture

**Document Version:** 2.0  
**Last Updated:** 2026-03-24  
**Status:** Current (Phase 5+6 Complete)

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture Principles](#architecture-principles)
3. [System Layers](#system-layers)
4. [Core Subsystems](#core-subsystems)
5. [Data Flow](#data-flow)
6. [Module Dependencies](#module-dependencies)
7. [Key Classes](#key-classes)
8. [Performance Architecture](#performance-architecture)
9. [Integration Points](#integration-points)

---

## Overview

Lakskongen is a deep simulation game about salmon farming built on **Godot 4.3** (game engine) and **.NET 8.0** (simulation core). The architecture follows a strict **separation of concerns** between game presentation and simulation logic, enabling:

- **Full testability** of business logic (203 xUnit tests, 0 Godot dependencies in simulation)
- **High performance** through C# AOT compilation and object pooling
- **Modular design** allowing independent development of subsystems
- **Cross-platform support** (Windows, Linux, macOS)

### Design Philosophy

> **"Simulation Layer = Pure C#. Presentation Layer = Godot. Bridge Layer = Minimal Interop."**

- **Simulation Core (C#):** All game logic, calculations, state management — no Godot dependencies
- **Presentation (Godot):** Rendering, UI, input, audio — minimal business logic
- **Bridge (SimBridge/GameBridge):** Thin adapter layer for C#/GDScript communication

---

## Architecture Principles

### 1. **Separation of Simulation and Presentation**
- **Simulation (src/LakskongenSim/):** Pure C# classes with no `Godot.*` imports
- **Presentation (godot/):** GDScript scenes and scripts calling into C# via bridge
- **Why:** Enables unit testing, faster iteration, engine independence

### 2. **Single Source of Truth**
- **GameState.cs** holds all authoritative game data
- UI panels read from C# via SimBridge, never maintain their own state
- SaveLoadManager serializes GameState to JSON

### 3. **Event-Driven Communication**
- Subsystems emit C# events (`Action<T>`, `EventHandler<T>`)
- GDScript listeners subscribe via bridge layer
- Decouples systems, prevents tight coupling

### 4. **Dependency Injection**
- SimulationEngine instantiates all subsystems
- Dependencies passed via constructors (e.g., `MarketSimulation` needs `CurrencySystem`)
- Enables mocking for tests

### 5. **Performance First**
- Object pooling for frequently allocated objects
- Lazy evaluation for expensive calculations
- Caching layers for environmental data and economics
- Adaptive LOD for disease simulation

---

## System Layers

```
┌─────────────────────────────────────────────────────────────┐
│                    PRESENTATION LAYER                        │
│  (Godot 4.3 — GDScript + Scenes + Shaders)                  │
├─────────────────────────────────────────────────────────────┤
│  • 3D Fjord Renderer (terrain, water, sky, weather)         │
│  • UI Panels (Dashboard, PenManagement, SaveLoad, Tutorial) │
│  • Camera System (pan, zoom, rotate)                        │
│  • Audio Manager (music, SFX, ambience)                     │
│  • Input Handling (keyboard, mouse, gamepad)                │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│                     BRIDGE LAYER                             │
│  (C# ↔ GDScript Interop)                                    │
├─────────────────────────────────────────────────────────────┤
│  • SimBridge.gd — Autoload singleton (GDScript → C#)        │
│  • GameBridge.cs — C# API gateway (C# → GDScript)           │
│  • EventBus.gd — GDScript event relay                       │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│                   SIMULATION LAYER                           │
│  (Pure C# — .NET 8.0)                                       │
├─────────────────────────────────────────────────────────────┤
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Core:                                                  │  │
│  │  • SimulationEngine (orchestrator, update loop)       │  │
│  │  • GameState (authoritative state, save/load)         │  │
│  │  • TimeManager (day counter, speed control)           │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Biology:                                               │  │
│  │  • GrowthModel (temperature-based growth curves)      │  │
│  │  • Genetics (8-trait Mendelian system)                │  │
│  │  • MortalityModel (stress, disease, age)              │  │
│  │  • Hatchery (egg → smolt pipeline)                    │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Environment (Phase 5):                                 │  │
│  │  • MareanoEnvironmentalDataProvider (13 zones)        │  │
│  │  • NorwegianZoneEnvironmentalProfile (Mareano data)   │  │
│  │  • EnvironmentModel (water chemistry, stress)         │  │
│  │  • WeatherSystem (storms, seasonal patterns)          │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Finance (Phase 6):                                     │  │
│  │  • FinanceManager (cash flow, balance)                │  │
│  │  • FinancialStatements (P&L, balance sheet)           │  │
│  │  • TaxSystem (Norwegian tax rates)                    │  │
│  │  • LoanSystem (debt management)                       │  │
│  │  • InvestorSystem (equity financing)                  │  │
│  │  • StockMarketSystem (IPO, valuation, dividends)      │  │
│  │  • LicenseSystem (MAB limits, traffic light)          │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Market (Phase 6):                                      │  │
│  │  • MarketSimulation (O-U price model, Fish Pool FPI)  │  │
│  │  • ContractSystem (spot, forward, tolling, exclusive) │  │
│  │  • InternationalMarketSystem (18 export destinations) │  │
│  │  • MarketIntelligenceSystem (forecasting, news)       │  │
│  │  • CurrencySystem (NOK/USD/EUR/GBP/JPY/CNY)           │  │
│  │  • ExportMarket (quality grading, tariffs)            │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Optimization (Phase 6):                                │  │
│  │  • FeedingOptimizer (temp/oxygen-based feeding)       │  │
│  │  • HarvestOptimizer (timing, size distribution)       │  │
│  │  • PenManagementOptimizer (stocking density)          │  │
│  │  • TreatmentCostBenefitAnalyzer (disease treatment)   │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Processing:                                            │  │
│  │  • ProcessingManager (17-product VAP chain)           │  │
│  │  • YieldCalculator (fillet yield, waste)              │  │
│  │  • ProductMixOptimizer (SKU optimization)             │  │
│  │  • BrandReputation (quality score, certifications)    │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Performance:                                           │  │
│  │  • EconomicCalculationCache (price/revenue cache)     │  │
│  │  • EnvironmentalDataCache (7-day forecast cache)      │  │
│  │  • ObjectPoolManager (allocation optimization)        │  │
│  │  • SimulationProfiler (performance tracking)          │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

---

## Core Subsystems

### 1. **Core** — `src/LakskongenSim/Core/`

**Purpose:** Orchestration, state management, game loop.

**Key Classes:**
- **SimulationEngine.cs** — Central orchestrator, daily update loop, subsystem wiring
- **GameState.cs** — Authoritative game state (serializable)
- **TimeManager.cs** — Day counter, speed control (1x/5x/10x/30x)
- **SaveLoadManager.cs** — JSON serialization, save slot management
- **TutorialSystem.cs** — 23-step interactive tutorial
- **AnalyticsTracker.cs** — Player behavior tracking
- **AchievementSystem.cs** — Milestone tracking

**Update Order:**
```
SimulationEngine.AdvanceOneDay():
  1. TimeManager.AdvanceOneDay()
  2. WeatherSystem.UpdateWeather()
  3. Environment.UpdateEnvironment()
  4. Biology (Growth, Mortality, Feed, Disease)
  5. Health (SeaLice, FishWelfare)
  6. Events.Storyteller.EvaluateEvents()
  7. Market.MarketSimulation.AdvanceOneDay()
  8. Finance.FinanceManager (balance updates)
  9. Tutorial.CheckCompletionCriteria()
  10. Analytics.RecordDailySnapshot()
```

---

### 2. **Biology** — `src/LakskongenSim/Biology/`

**Purpose:** Fish lifecycle, genetics, growth, mortality.

**Key Classes:**
- **GrowthModel.cs** — Temperature-based TGC (Thermal Growth Coefficient) model
- **Genetics.cs** — 8-trait Mendelian system (growth, disease resistance, fillet quality, maturation, feed efficiency, color, stress tolerance, fillet yield)
- **MortalityModel.cs** — Background mortality, disease, stress, age
- **Hatchery.cs** — Egg → Alevin → Fry → Parr → Smolt pipeline
- **FeedSystem.cs** — Feed quality, FCR (Feed Conversion Ratio)

**Trait System:**
```csharp
public class FishGenetics
{
    public Trait GrowthRate { get; set; }        // 0-100
    public Trait DiseaseResistance { get; set; } // 0-100
    public Trait FilletQuality { get; set; }     // 0-100
    public Trait Maturation { get; set; }        // 0-100 (lower = later)
    public Trait FeedEfficiency { get; set; }    // 0-100
    public Trait FilletColor { get; set; }       // 0-100 (23-26 ideal)
    public Trait StressTolerance { get; set; }   // 0-100
    public Trait FilletYield { get; set; }       // 0-100 (% of body weight)
}
```

---

### 3. **Environment** (Phase 5) — `src/LakskongenSim/Environment/`

**Purpose:** Real Norwegian coastal oceanographic data integration.

**Key Classes:**
- **MareanoEnvironmentalDataProvider.cs** — 13 Norwegian production zones (PO01-PO13), daily updates, 7-day forecasting
- **NorwegianZoneEnvironmentalProfile.cs** — Zone-specific profiles from Mareano/Geonorge/IMR
- **EnvironmentModel.cs** — Water chemistry, stress multipliers
- **WeatherSystem.cs** — Storms, seasonal patterns, midnight sun/polar night

**Data Sources:**
- **Mareano:** https://www.mareano.no (bathymetry, sediment, oceanography)
- **Geonorge:** https://www.geonorge.no (marine habitat maps, current data)
- **Institute of Marine Research (IMR):** https://www.hi.no (temperature, oxygen)
- **Norwegian Meteorological Institute (MET):** https://www.met.no (current models)

**Zone Coverage:**
```
PO01: Østfold          (59.0°N, 11.0°E) — 20-100m depth
PO02: Aust-Agder       (58.3°N, 8.6°E)  — 30-150m depth
PO03: Rogaland         (58.9°N, 5.7°E)  — 50-300m depth
PO04: Hordaland        (60.2°N, 5.3°E)  — 100-500m depth
PO05: Sogn og Fjordane (61.5°N, 5.0°E)  — 100-800m depth
PO06: Møre og Romsdal  (62.8°N, 6.5°E)  — 150-1000m depth
PO07: Trøndelag Sør    (64.0°N, 9.5°E)  — 100-600m depth
PO08: Trøndelag Nord   (65.0°N, 11.0°E) — 100-500m depth
PO09: Nordland Sør     (66.0°N, 12.5°E) — 100-400m depth
PO10: Nordland Nord    (67.5°N, 14.0°E) — 80-300m depth
PO11: Troms            (69.5°N, 19.0°E) — 50-250m depth
PO12: Finnmark         (70.5°N, 23.0°E) — 30-150m depth
PO13: Vest-Finnmark    (71.0°N, 26.0°E) — 20-100m depth
```

**Environmental Parameters:**
- Surface/Bottom Temperature (°C)
- Oxygen Saturation (mg/L)
- Current Speed (m/s)
- Salinity (PSU)
- Chlorophyll-a (µg/L)
- Turbidity (NTU)

---

### 4. **Finance** (Phase 6) — `src/LakskongenSim/Finance/`

**Purpose:** Cash flow, accounting, financing, taxation.

**Key Classes:**
- **FinanceManager.cs** — Single source of truth for balance, revenue/expense tracking
- **FinancialStatements.cs** — P&L, balance sheet, cash flow statement
- **TaxSystem.cs** — Norwegian corporate tax (22%), resource rent tax (40% on profits > NOK 70M)
- **LoanSystem.cs** — Debt management, interest rates
- **InvestorSystem.cs** — Equity financing rounds (seed, Series A/B, IPO)
- **StockMarketSystem.cs** — IPO mechanics, stock price, dividends, market cap
- **LicenseSystem.cs** — MAB (Maximum Allowed Biomass) limits, traffic light system (green/yellow/red zones)

**Cash Flow:**
```csharp
public class FinanceManager
{
    public decimal Balance { get; private set; } // Single source of truth
    public void AddRevenue(decimal amount) { Balance += amount; }
    public bool Spend(decimal amount) { /* Check balance, deduct */ }
    public bool CanAfford(decimal amount) { return Balance >= amount; }
}
```

**Sustainability Integration:**
```csharp
// ESG score >= 80 → 0-30% CAPEX discount on green tech
public decimal ApplyESGDiscount(decimal baseCost) 
{
    return baseCost * (1m - ESGInvestmentDiscount);
}

// High ESG score → 1.0-1.5x government grant multiplier
public decimal ApplyGovernmentGrant(decimal baseGrant)
{
    return baseGrant * GovernmentGrantMultiplier;
}
```

---

### 5. **Market** (Phase 6) — `src/LakskongenSim/Market/`

**Purpose:** Price simulation, contracts, international trade.

**Key Classes:**
- **MarketSimulation.cs** — Ornstein-Uhlenbeck mean reversion model calibrated to Fish Pool FPI (2020-2025)
- **ContractSystem.cs** — Spot, forward, tolling, exclusive contracts
- **InternationalMarketSystem.cs** — 18 export destinations (USA, EU, Japan, China, Southeast Asia)
- **MarketIntelligenceSystem.cs** — Price forecasting, competitor tracking, news events
- **CurrencySystem.cs** — Real-time FX rates (NOK/USD/EUR/GBP/JPY/CNY)
- **ExportMarket.cs** — Quality grading (Superior/Standard/Economy), tariffs

**Ornstein-Uhlenbeck Price Model:**
```
dP_t = κ(μ - P_t)dt + σdW_t

Where:
  P_t = current price
  μ = long-term mean (~85 NOK/kg, Fish Pool 2020-2025 avg)
  κ = mean reversion speed (~0.62 annualized)
  σ = volatility (~9 NOK/kg weekly)
  W_t = Wiener process (random walk)
```

**Calibration to Fish Pool FPI:**
- Price range: 40-160 NOK/kg (historical 2020-2025)
- Seasonal peaks: Easter (~+12 NOK/kg), BBQ season (~+12 NOK/kg), Christmas (~+6 NOK/kg)
- Supply shocks: COVID-19 trough (~45 NOK/kg), 2022 peak (~140 NOK/kg)

---

### 6. **Optimization** (Phase 6) — `src/LakskongenSim/Optimization/`

**Purpose:** Strategic decision support tools.

**Key Classes:**
- **FeedingOptimizer.cs** — Temperature/oxygen-based feeding schedules, economic margin optimization
- **HarvestOptimizer.cs** — Optimal harvest timing based on size distribution, market price, contract deadlines
- **PenManagementOptimizer.cs** — Stocking density optimization (welfare vs growth)
- **TreatmentCostBenefitAnalyzer.cs** — Disease treatment ROI (medication vs mortality cost)

**Example: Feeding Optimization**
```csharp
public double CalculateOptimalDailyRation(
    double fishWeightKg,
    double tempC,
    double oxygenMgL,
    double feedCostPerKg,
    double marketPricePerKg)
{
    double baseRate = 1.2; // % body weight
    double tempMultiplier = TempResponse(tempC); // Gaussian centered on 11°C
    double oxygenMultiplier = OxygenResponse(oxygenMgL); // 1.0 at >7mg/L
    double economicMultiplier = MarginResponse(marketPricePerKg - feedCostPerKg);
    return fishWeightKg * baseRate * tempMultiplier * oxygenMultiplier * economicMultiplier / 100.0;
}
```

---

### 7. **Processing** — `src/LakskongenSim/Processing/`

**Purpose:** Value-added processing, product mix optimization.

**Key Classes:**
- **ProcessingManager.cs** — 17-product VAP chain (fresh, frozen, smoked, sashimi-grade)
- **YieldCalculator.cs** — Fillet yield (55-65%), waste streams
- **ProductMixOptimizer.cs** — SKU optimization based on demand and margin
- **BrandReputation.cs** — Quality score, certifications (ASC, MSC, organic)

**Product Catalog:**
```
Whole fish (fresh/frozen)
Head-on gutted (HOG)
Fillets (fresh/frozen)
Portions (skinless/skin-on)
Smoked (cold/hot)
Gravlax
Sashimi-grade
Burgers/patties
Canned
Roe
Pet food
Organic waste → compost
```

---

### 8. **Performance** — `src/LakskongenSim/Performance/`

**Purpose:** Caching, profiling, object pooling.

**Key Classes:**
- **EconomicCalculationCache.cs** — Cache price calculations, revenue projections
- **EnvironmentalDataCache.cs** — 7-day environmental forecast cache
- **ObjectPoolManager.cs** — Pool frequently allocated objects (FishBatch, EventData)
- **SimulationProfiler.cs** — Per-subsystem performance tracking

**Caching Strategy:**
```csharp
// Environmental data cached for 7 days ahead
Queue<EnvironmentalDataPoint> _forecastCache;

// Economic calculations cached until price changes
Dictionary<(double weight, QualityGrade), decimal> _revenueCache;
```

---

## Data Flow

### Daily Update Cycle

```
User Input (Play/Pause/Speed)
         ↓
┌────────────────────────────────────────────────┐
│  SimulationEngine.AdvanceOneDay()              │
├────────────────────────────────────────────────┤
│  1. TimeManager.AdvanceOneDay()                │
│     • currentDay++                             │
│     • Fire OnDayChanged event                  │
│                                                │
│  2. WeatherSystem.UpdateWeather()              │
│     • Generate storms, fog, seasonal patterns  │
│     • Update temperature/wind/precipitation    │
│                                                │
│  3. MareanoEnvironmentalDataProvider.AdvanceOneDay() │
│     • Fetch day N+1 data for all 13 zones     │
│     • Update temperature, oxygen, currents     │
│     • Fire OnZoneEnvironmentUpdated events     │
│                                                │
│  4. EnvironmentModel.UpdateEnvironment()       │
│     • Calculate stress multipliers             │
│     • Update water chemistry                   │
│                                                │
│  5. Biology Updates (per pen)                  │
│     • GrowthModel.CalculateDailyGrowth()       │
│     • MortalityModel.CalculateDailyMortality() │
│     • FeedSystem.ConsumeFeed()                 │
│     • SeaLice.UpdateInfestation()              │
│     • Disease.CheckOutbreaks()                 │
│                                                │
│  6. Health.FishWelfare.UpdateScore()           │
│     • Aggregate welfare indicators             │
│                                                │
│  7. Storyteller.EvaluateEvents()               │
│     • Check event triggers                     │
│     • Generate news, crises, opportunities     │
│                                                │
│  8. MarketSimulation.AdvanceOneDay()           │
│     • Update O-U price model                   │
│     • Apply seasonal offsets                   │
│     • Generate market news                     │
│                                                │
│  9. ContractSystem.CheckExpirations()          │
│     • Expire unfulfilled contracts             │
│     • Apply penalties                          │
│                                                │
│ 10. FinanceManager (passive — triggered by sales) │
│     • Balance updated on AddRevenue/Spend      │
│                                                │
│ 11. Tutorial.CheckCompletionCriteria()         │
│     • Advance tutorial step if criteria met    │
│                                                │
│ 12. Analytics.RecordDailySnapshot()            │
│     • Log KPIs for analytics                   │
└────────────────────────────────────────────────┘
         ↓
  Fire OnDayAdvanced event
         ↓
  GDScript listeners update UI
```

### Environmental Data → Growth → Revenue Flow

```
Mareano/Geonorge Data
         ↓
NorwegianZoneEnvironmentalProfile
         ↓
MareanoEnvironmentalDataProvider
         ↓
EnvironmentModel (stress multipliers)
         ↓
GrowthModel (temperature-based TGC)
         ↓
FishBatch.AverageWeight increases
         ↓
HarvestOptimizer suggests harvest timing
         ↓
Player harvests fish
         ↓
ProcessingManager converts to products
         ↓
MarketSimulation provides current price
         ↓
ContractSystem matches to contracts
         ↓
FinanceManager.AddRevenue(revenue)
         ↓
Dashboard displays updated balance
```

---

## Module Dependencies

```
Core (SimulationEngine, GameState, TimeManager)
  ↓
  ├─→ Biology (Growth, Genetics, Mortality, Hatchery, Feed)
  │    ↓
  │    └─→ Environment (EnvironmentModel, Weather)
  │
  ├─→ Environment (MareanoDataProvider, ZoneProfiles, Weather)
  │    ↓
  │    └─→ Performance (EnvironmentalDataCache)
  │
  ├─→ Health (SeaLice, Disease, FishWelfare)
  │    ↓
  │    └─→ Biology (Mortality, Genetics)
  │
  ├─→ Market (MarketSimulation, Contracts, InternationalMarkets, CurrencySystem)
  │    ↓
  │    ├─→ Performance (EconomicCalculationCache)
  │    └─→ Processing (ProductCatalog, BrandReputation)
  │
  ├─→ Finance (FinanceManager, FinancialStatements, TaxSystem, Loans, Investors, StockMarket, Licenses)
  │    ↓
  │    └─→ Sustainability (SustainabilityManager)
  │
  ├─→ Optimization (FeedingOptimizer, HarvestOptimizer, PenManagementOptimizer, TreatmentCostBenefitAnalyzer)
  │    ↓
  │    ├─→ Biology (GrowthModel, FeedSystem)
  │    ├─→ Environment (EnvironmentModel)
  │    ├─→ Market (MarketSimulation)
  │    └─→ Finance (FinanceManager)
  │
  ├─→ Processing (ProcessingManager, YieldCalculator, ProductMixOptimizer, BrandReputation)
  │    ↓
  │    └─→ Market (MarketSimulation, ExportMarket)
  │
  ├─→ Events (Storyteller, EventSystem)
  │    ↓
  │    └─→ Core (GameState, TimeManager)
  │
  └─→ Performance (Caching, Profiling, ObjectPooling)
```

**Dependency Rules:**
1. **No circular dependencies** — enforced via namespace structure
2. **Core never depends on submodules** — only submodules depend on Core
3. **Simulation never depends on Godot** — pure C# for testability
4. **Presentation depends on Simulation** — one-way dependency via bridge

---

## Key Classes

### SimulationEngine.cs

**Purpose:** Central orchestrator connecting all subsystems.

**Responsibilities:**
- Instantiate all subsystems with correct dependency injection
- Execute daily update loop in correct order
- Provide public API for game actions (HarvestPen, SellFish, BuyFeed, etc.)
- Serialize/deserialize GameState

**Key Methods:**
```csharp
public void AdvanceOneDay() // Main update loop
public void HarvestPen(Guid penId, int quantityKg) // Player action
public void SellFish(int quantityKg, ContractType contractType) // Player action
public void BuyFeed(FeedType type, int quantityKg) // Player action
public void SaveGame(string filePath) // Persistence
public void LoadGame(string filePath) // Persistence
```

---

### GameState.cs

**Purpose:** Authoritative game state (serializable to JSON).

**Structure:**
```csharp
public class GameState
{
    // Meta
    public string SaveName { get; set; }
    public DateTime SaveTimestamp { get; set; }
    public int CurrentDay { get; set; }
    
    // Biology
    public List<PenState> Pens { get; set; }
    public List<FishBatchState> FishBatches { get; set; }
    public HatcheryState Hatchery { get; set; }
    
    // Finance
    public decimal Balance { get; set; }
    public List<TransactionRecord> Transactions { get; set; }
    
    // Market
    public decimal CurrentPricePerKg { get; set; }
    public List<Contract> ActiveContracts { get; set; }
    
    // Environment
    public Dictionary<string, EnvironmentalDataPoint> CurrentZoneData { get; set; }
    
    // Progression
    public int TutorialStep { get; set; }
    public List<string> UnlockedFeatures { get; set; }
    public List<string> CompletedAchievements { get; set; }
}
```

---

### MareanoEnvironmentalDataProvider.cs

**Purpose:** Real Norwegian coastal oceanographic data integration.

**Key Methods:**
```csharp
public void AdvanceOneDay() // Update all 13 zones
public EnvironmentalDataPoint GetCurrentData(string zoneId) // Current day
public EnvironmentalDataPoint GetForecast(string zoneId, int daysAhead) // 1-7 day forecast
public double CalculateGrowthStressIndex(string zoneId) // 0-1 stress multiplier
public double CalculateHealthStressIndex(string zoneId) // 0-1 stress multiplier
```

**Data Structure:**
```csharp
public class EnvironmentalDataPoint
{
    public string ZoneId { get; set; }
    public DateTime Timestamp { get; set; }
    
    // Temperature
    public double SurfaceTemperature { get; set; } // °C
    public double BottomTemperature { get; set; }
    public double TemperatureAt10m { get; set; }
    public double TemperatureAt30m { get; set; }
    
    // Oxygen
    public double SurfaceOxygen { get; set; } // mg/L
    public double OxygenAt10m { get; set; }
    public double OxygenAtCageDepth { get; set; }
    public double OxygenSaturation { get; set; } // 0-1
    
    // Currents
    public double SurfaceCurrentSpeed { get; set; } // m/s
    public double CurrentDirection { get; set; } // degrees
    public double TidalAmplitude { get; set; } // m
    
    // Water Quality
    public double Salinity { get; set; } // PSU
    public double Chlorophyll { get; set; } // µg/L
    public double Turbidity { get; set; } // NTU
}
```

---

### MarketSimulation.cs

**Purpose:** Realistic salmon market price simulation.

**Key Methods:**
```csharp
public void AdvanceOneDay() // Update O-U model
public decimal GetCurrentPrice(QualityGrade grade = QualityGrade.Standard) // Base price + quality premium
public decimal GetForecastPrice(int daysAhead) // Forecast (used by contracts)
public void ApplySupplyShock(double magnitude, int durationDays) // Manual events
public void ApplyDemandShock(double magnitude, int durationDays) // Manual events
```

**Calibration:**
```csharp
// Ornstein-Uhlenbeck parameters (calibrated to Fish Pool FPI 2020-2025)
private const decimal DefaultLongTermMean = 85m; // NOK/kg
private const decimal DefaultWeeklyTheta = 0.012m; // Mean reversion speed
private const decimal DefaultWeeklySigma = 9m; // Volatility
private const decimal PriceFloor = 40m; // Historical low
private const decimal PriceCeiling = 160m; // Historical high

// Seasonal offsets (NOK/kg)
private static readonly decimal[] MonthlySeasonalOffsets =
{
     5m, -2m, -5m,  // Jan (CNY), Feb (post-CNY), Mar (low)
    -3m,  2m,  8m,  // Apr (Easter), May (rising), Jun (BBQ)
    12m, 10m,  3m,  // Jul (peak BBQ), Aug (high), Sep (harvest drop)
    -4m, -2m,  6m   // Oct (oversupply), Nov (stable), Dec (Christmas)
};
```

---

### ContractSystem.cs

**Purpose:** Contract management (spot, forward, tolling, exclusive).

**Key Methods:**
```csharp
public ContractOffer ProposeContract(ContractType type, int quantityKg, int durationWeeks) // Generate offer
public Contract SignContract(ContractOffer offer) // Accept offer
public decimal FulfillContract(Contract contract, int deliveredKg, QualityGrade actualGrade) // Deliver fish
public void ApplyPenalty(Contract contract) // Unfulfilled contract
public List<ContractOffer> GetCurrentOffers() // Available contracts
```

**Contract Types:**
```csharp
public enum ContractType
{
    Spot,       // Immediate sale at current market price
    Forward,    // Fixed price for future delivery (4-12 weeks)
    Tolling,    // Processing fee-based (plant provides processing)
    Exclusive   // Long-term exclusive buyer (premium price, volume commitment)
}
```

---

### FeedingOptimizer.cs

**Purpose:** Strategic feeding schedule optimization.

**Key Methods:**
```csharp
public double CalculateOptimalDailyRation(double fishWeightKg, double tempC, double oxygenMgL, double feedCostPerKg, double marketPricePerKg)
public List<FeedingPlan> GetFeedingSchedule(int fishCount, double avgWeightKg, double[] dailyTempC, double[] dailyOxygenMgL, double feedCostPerKg, double marketPricePerKg, int daysAhead = 30)
public double CalculateFCR(double feedConsumedKg, double growthKg) // Feed Conversion Ratio
```

**Optimization Strategy:**
```csharp
// Base rate: 1.2% body weight/day (optimal)
// Temp multiplier: Gaussian response centered on 11°C
// Oxygen multiplier: 1.0 at >7mg/L, 0.6 at 5mg/L, 0.0 at <4mg/L
// Economic multiplier: Feed more when margin is high (price - feed cost)
// Size multiplier: Smaller fish eat more relative to body weight
```

---

## Performance Architecture

### Caching Strategy

**1. Environmental Data Cache** (`EnvironmentalDataCache.cs`)
- 7-day forecast pre-cached for all 13 zones
- Replenished daily (cache ahead as forecast is consumed)
- Reduces Mareano/Geonorge API calls from 13/day to 13/week

**2. Economic Calculation Cache** (`EconomicCalculationCache.cs`)
- Revenue projections cached by (weight, quality grade, contract type)
- Invalidated on price change events
- Reduces repeated calculations in UI panels

**3. Object Pooling** (`ObjectPoolManager.cs`)
- Pools for frequently allocated objects: `FishBatch`, `EventData`, `TransactionRecord`
- Reduces GC pressure in tight loops (daily update cycle)

### Profiling

**SimulationProfiler.cs** tracks per-subsystem execution time:
```csharp
// T60 Performance Report (100k fish simulation):
// Total daily update: 45ms (target: <50ms for 60 FPS)
// - Biology: 28ms (62% of total) ← primary bottleneck
// - Environment: 6ms (13%)
// - Market: 4ms (9%)
// - Finance: 3ms (7%)
// - Events: 2ms (4%)
// - Other: 2ms (4%)
```

**Optimization Targets:**
- Daily update <50ms for 60 FPS (16.7ms frame budget, 3 frames for update loop)
- Save/Load <500ms for smooth UX
- UI panel refresh <16ms (no frame drops)

---

## Integration Points

### C# ↔ GDScript Bridge

**SimBridge.gd** (GDScript Autoload Singleton)
```gdscript
# Example: Call C# from GDScript
var engine = SimBridge.get_simulation_engine()
engine.HarvestPen(pen_id, quantity_kg)

# Example: Subscribe to C# events
SimBridge.connect("day_advanced", self, "_on_day_advanced")
```

**GameBridge.cs** (C# API Gateway)
```csharp
// Example: Call GDScript from C#
public void UpdateDashboard(decimal balance, int totalBiomass)
{
    GD.Call("EventBus", "emit_signal", "dashboard_update", balance, totalBiomass);
}
```

### External Data Sources

**Mareano/Geonorge Integration:**
- **Offline mode:** Pre-loaded CSV/JSON datasets in `data/mareano/`
- **Online mode (future):** REST API calls to Mareano/Geonorge WMS/WFS services
- **Caching:** 7-day forecast cached locally

**Fish Pool FPI Data:**
- Historical price data in `research/real-salmon-prices.md`
- Calibration parameters in `MarketSimulation.cs`
- Manual updates quarterly

---

## Conclusion

Lakskongen's architecture prioritizes:
1. **Testability** — Pure C# simulation with 203 passing tests
2. **Performance** — Object pooling, caching, profiling, adaptive LOD
3. **Modularity** — Clear subsystem boundaries, dependency injection
4. **Realism** — Real Norwegian data (Mareano/Geonorge), calibrated models (Fish Pool FPI)
5. **Extensibility** — Event-driven design, mod support framework

For deep dives into specific subsystems:
- **Economic System:** See [ECONOMIC_SYSTEM.md](ECONOMIC_SYSTEM.md)
- **Environmental Data:** See [ENVIRONMENTAL_DATA.md](ENVIRONMENTAL_DATA.md)
- **API Reference:** See [API_Reference.md](API_Reference.md)

---

**Document Status:** ✅ Complete  
**Last Verified:** 2026-03-24  
**Next Review:** Phase 7 completion
