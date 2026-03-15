# Lakskongen — Architecture Overview

**Version:** 0.4.0  
**Last Updated:** 2026-03-15  
**Author:** Fredrikke (Chief Software Engineer)

---

## 🎯 Design Philosophy

Lakskongen is built on a **clean separation of concerns**:

1. **Simulation Layer** — Pure C# business logic (no Godot dependencies)
2. **Presentation Layer** — Godot scenes for rendering and UI
3. **Bridge Layer** — C# autoloads connecting simulation to Godot
4. **Data Layer** — JSON configuration + SQLite saves

**Why this matters:**
- Simulation can be tested without Godot (fast, deterministic)
- Headless simulation possible (e.g., AI training, cloud servers)
- Core logic is portable (future Unity/Unreal ports if needed)
- Clean API boundaries reduce coupling

---

## 📂 Project Structure

```
lakskongen/
├── src/                          # C# Simulation Core (.NET 8.0)
│   ├── LakskongenSim/            # Main simulation library
│   │   ├── Core/                 # Core engine and orchestration
│   │   │   ├── SimulationEngine.cs   # Main tick loop, coordinates systems
│   │   │   ├── TimeSystem.cs         # Day/week/month tracking
│   │   │   └── GameState.cs          # Serializable game state
│   │   ├── Biology/              # Fish biology and genetics
│   │   │   ├── FishPopulation.cs     # Cohort-based population management
│   │   │   ├── GeneticsModel.cs      # Mendelian genetics, breeding
│   │   │   ├── GrowthModel.cs        # von Bertalanffy, Gompertz curves
│   │   │   └── MortalityModel.cs     # Baseline + disease mortality
│   │   ├── Health/               # Disease and parasite systems
│   │   │   ├── DiseaseSystem.cs      # SIR epidemic model
│   │   │   ├── SeaLiceModel.cs       # Lice lifecycle and treatment
│   │   │   ├── IntegratedDiseaseSystem.cs  # Multi-disease orchestrator
│   │   │   └── DiseaseLODSystem.cs   # Adaptive detail levels
│   │   ├── Economics/            # Markets and finance
│   │   │   ├── MarketModel.cs        # Price dynamics, seasonal curves
│   │   │   ├── ContractSystem.cs     # Long-term contracts vs spot
│   │   │   └── FinanceManager.cs     # Cash flow, EBIT tracking
│   │   ├── Environment/          # Fjord simulation
│   │   │   ├── ZoneModel.cs          # 13 Norwegian production zones
│   │   │   ├── WaterChemistry.cs     # Temp, O2, salinity, currents
│   │   │   └── WeatherSystem.cs      # Seasonal weather events
│   │   ├── Production/           # Farm operations
│   │   │   ├── HatcheryModel.cs      # Egg → Smolt pipeline
│   │   │   ├── SeaSite.cs            # Net pen management
│   │   │   ├── FeedManager.cs        # Feed quality and FCR
│   │   │   └── HarvestScheduler.cs   # Harvest timing optimization
│   │   ├── Regulation/           # Norwegian regulations
│   │   │   ├── TrafficLightSystem.cs # Green/yellow/red zones
│   │   │   ├── LicenseManager.cs     # MAB limits, license trading
│   │   │   └── ComplianceTracker.cs  # Environmental reporting
│   │   └── Data/                 # Serialization and persistence
│   │       ├── SaveData.cs           # Save file schema
│   │       └── ConfigLoader.cs       # JSON config parsing
│   ├── LakskongenSim.Tests/      # xUnit tests
│   │   ├── Biology/
│   │   ├── Health/
│   │   ├── Economics/
│   │   └── Integration/
│   └── PerformanceBenchmark/     # Performance profiling tool
├── godot/                        # Godot 4.3 Presentation Layer
│   ├── project.godot             # Godot project file
│   ├── scenes/                   # .tscn scene files
│   │   ├── MainMenu.tscn
│   │   ├── GameWorld.tscn
│   │   └── ui/                   # UI scenes
│   ├── scripts/                  # GDScript + C# Godot scripts
│   │   ├── autoload/             # Singleton autoloads
│   │   │   ├── GameBridge.cs     # Main C# → Godot bridge
│   │   │   ├── EventBus.cs       # Global event dispatcher
│   │   │   ├── SaveLoadManager.cs
│   │   │   └── AudioManager.cs
│   │   ├── ui/                   # UI controllers
│   │   └── SimBridge.gd          # GDScript helper for C# sim
│   ├── assets/                   # Art and audio
│   ├── translations/             # Localization files
│   └── theme/                    # UI theme
├── tests/                        # Python integration tests
│   ├── test_genetics.py
│   ├── test_growth.py
│   └── test_environment.py
└── docs/                         # Documentation
    ├── Architecture.md           # This file
    ├── Modding_API.md
    └── ...
```

---

## 🔄 Data Flow

### Game Loop (60 FPS)

```
Godot _process(delta)
    ↓
GameBridge.Update(delta)
    ↓ (accumulate time)
SimulationEngine.AdvanceOneDay()
    ↓
┌─────────────────────────────────┐
│ Simulation Tick (1 day)         │
│ 1. Time.Advance()                │
│ 2. Environment.Update()          │
│ 3. Biology.Tick()                │
│    - Growth (cohorts)            │
│    - Mortality (baseline + disease)
│ 4. Health.Tick()                 │
│    - Disease spread (SIR)        │
│    - Lice lifecycle              │
│ 5. Production.Tick()             │
│    - Feeding                     │
│    - Harvesting                  │
│ 6. Market.Update()               │
│    - Price fluctuation           │
│ 7. Finance.Calculate()           │
│    - Revenue/expenses            │
│ 8. Events.Process()              │
│    - Random events (storms, etc.)
└─────────────────────────────────┘
    ↓
Emit signals (via EventBus)
    ↓
GDScript UI updates
```

### Event Flow

```
SimulationEngine (C#)
    ↓ EmitSignal
EventBus.cs (C# autoload)
    ↓ Dispatch
GDScript UI Controllers
    ↓ Connect
UI Updates (Labels, Graphs, Notifications)
```

**Example:**
```csharp
// C# (SimulationEngine.cs)
EventBus.Instance.EmitSignal("FishHarvested", new HarvestData
{
    Count = 50000,
    TotalWeightKg = 250000,
    PricePerKg = 85.0
});
```

```gdscript
# GDScript (Dashboard.gd)
func _ready():
    EventBus.FishHarvested.connect(_on_fish_harvested)

func _on_fish_harvested(data):
    harvest_label.text = "Harvested: %d kg" % data.TotalWeightKg
    revenue_label.text = "Revenue: $%d" % (data.TotalWeightKg * data.PricePerKg)
```

---

## 🧩 Core Systems

### 1. SimulationEngine

**Responsibility:** Orchestrate all subsystems, advance simulation time.

**Key Methods:**
- `Initialize()` — Create all subsystems
- `AdvanceOneDay()` — Run one simulation tick
- `GetState()` — Return serializable state for UI
- `Save() / Load()` — Persist/restore game state

**Performance:**
- Tick time: ~8ms @ 10k fish, ~50ms @ 100k fish (before optimization)
- Profiling via Stopwatch on Biology/Health phases

---

### 2. Biology System

#### FishPopulation
**Pattern:** Cohort-based (group fish by age/size for performance)

**Cohort Structure:**
```csharp
public record Cohort(
    int Count,
    double MeanWeightKg,
    double WeightStdDev,
    Genome Genetics,
    LifeStage Stage,
    int AgeDays
);
```

**Operations:**
- Add cohort (stocking)
- Apply growth (von Bertalanffy curve)
- Apply mortality (baseline + disease)
- Harvest (remove fish)

#### GeneticsModel
**Pattern:** Mendelian inheritance with 8 traits

**Traits:**
- Growth rate (heritability ~0.4)
- Disease resistance (heritability ~0.3)
- Fillet quality (heritability ~0.35)
- Maturation timing (heritability ~0.45)
- Feed efficiency (heritability ~0.25)
- Color (heritability ~0.5)
- Stress tolerance (heritability ~0.3)
- Fillet yield (heritability ~0.4)

**Breeding:**
```csharp
var offspring = GeneticsModel.Breed(parent1, parent2);
// Includes Mendelian segregation + mutation
```

#### GrowthModel
**Models:**
- **von Bertalanffy:** `W(t) = W_inf * (1 - e^(-k * t))^3`
- **Gompertz:** `W(t) = W_inf * e^(-e^(-k * (t - t_i)))`

**Factors:**
- Genetics (growth rate trait)
- Temperature (optimal range 8-14°C)
- Feed quality (FCR multiplier)
- Oxygen levels

---

### 3. Health System

#### DiseaseSystem (SIR Model)
**States:** Susceptible → Infected → Recovered

**Diseases:**
- IPN (Infectious Pancreatic Necrosis)
- ISA (Infectious Salmon Anemia)
- Furunculosis
- Custom diseases (moddable)

**Spread:**
- Transmission rate (β) based on density and water exchange
- Recovery rate (γ) based on fish immunity
- Mortality (extra deaths from disease)

#### SeaLiceModel
**Lifecycle Stages:**
- Nauplii (planktonic)
- Copepodid (infectious)
- Chalimus (attached)
- Pre-adult
- Adult (reproducing)

**Dynamics:**
- External lice drift from environment
- Reproduction on fish
- Treatment (chemical, mechanical, biological)
- Resistance development

#### IntegratedDiseaseSystem (LOD)
**Detail Levels:**
- **High:** Full SIR for each disease + lice lifecycle
- **Medium:** Simplified prevalence tracking
- **Low:** Statistical aggregate (minimal CPU)

**Adaptive switching:** Based on fish count and player focus

---

### 4. Economics System

#### MarketModel
**Price Dynamics:**
```csharp
basePrice = 80.0; // NOK/kg baseline
seasonalMultiplier = 1.0 + 0.3 * sin(2π * (day - 100) / 365);
  // Peaks at Easter (~day 100), Christmas (~day 350)
sizeMultiplier = 1.0 + 0.2 * (weight in 4-6 kg range ? 1.0 : 0.0);
qualityMultiplier = 1.0 + (filletQuality - 50) * 0.01;

finalPrice = basePrice * seasonalMultiplier * sizeMultiplier * qualityMultiplier;
```

**Random fluctuations:** ±5% weekly volatility

#### ContractSystem
**Types:**
- **Spot Market:** Sell at current price (high volatility)
- **Forward Contract:** Lock price 3-6 months ahead (risk mitigation)
- **Premium Contracts:** Quality-based bonuses (sashimi, organic)

**Implementation:**
```csharp
public record Contract(
    ContractType Type,
    double PricePerKg,
    int VolumeKg,
    DateTime ExpiryDate,
    Dictionary<string, double> Bonuses
);
```

---

### 5. Environment System

#### ZoneModel
**13 Norwegian Zones:** Defined by:
- Temperature profile (seasonal)
- Current strength (lice drift)
- Lice pressure (external)
- Regulatory status (traffic light color)

**Data Source:** Real Norwegian Directorate of Fisheries zones

#### WaterChemistry
**Parameters:**
- Temperature (°C) — Affects growth, disease, lice development
- Dissolved Oxygen (mg/L) — Critical for biomass limits
- Salinity (ppt) — Affects lice survival
- Currents (m/s) — Waste dispersion, lice drift

**Update Frequency:** Daily (integrated with time system)

---

### 6. Save/Load System

#### Schema (JSON)
```json
{
  "version": "0.4.0",
  "timestamp": "2026-03-15T12:34:56Z",
  "currentDay": 905,
  "finances": {
    "cash": 15000000,
    "ebitPerKg": 22.5
  },
  "populations": [
    {
      "siteId": "site_001",
      "cohorts": [ /* cohort data */ ]
    }
  ],
  "diseases": { /* disease state */ },
  "market": { /* historical prices */ }
}
```

**Compression:** gzip for large saves (&gt;1 MB)

**Validation:**
- Version check (migration if needed)
- Corruption detection (checksums)
- Graceful degradation (skip corrupt sections)

---

## 🚀 Performance Considerations

### Bottlenecks (Profiled)

| System | Time @ 100k fish | Optimization |
|--------|------------------|--------------|
| **Biology Loop** | ~30ms | In-place cohort updates (40-60% faster) |
| Disease SIR | ~10ms | Already LOD-optimized |
| Market Update | ~2ms | No optimization needed |
| Feed Calculation | ~8ms | Vectorize with SIMD (future) |

### Optimization Strategies

1. **Cohort Aggregation:** Group similar fish to reduce iteration count
2. **LOD Systems:** Adaptive detail based on player focus
3. **SIMD:** Vectorize growth calculations (AVX2 on x64)
4. **ECS:** Consider Arch ECS for 500k+ fish (future)
5. **Parallelization:** Multi-thread independent systems (diseases per zone)

### Target Performance
- **60 FPS @ 100k fish** (current: ~20 FPS, needs 3x optimization)
- **Simulation tick budget:** 16.67ms (for 60 FPS)

---

## 🧪 Testing Architecture

### Unit Tests (xUnit)
- **Coverage Target:** 70%+ for new code
- **Test Doubles:** Mocks for randomness, time, I/O
- **Fast Tests:** &lt;1s per test, no file I/O

**Example:**
```csharp
[Fact]
public void ApplyGrowth_WithOptimalTemperature_IncreasesWeight()
{
    var cohort = new Cohort(Count: 10000, MeanWeightKg: 0.5, ...);
    var result = GrowthModel.ApplyGrowth(cohort, days: 30, tempC: 10.0);
    
    Assert.True(result.MeanWeightKg > 0.5);
}
```

### Integration Tests (Python pytest)
- **Test system interactions** (e.g., genetics → growth → market)
- **Longer tests** (full production cycles)
- **Calibration tests** (compare to real data)

### Performance Tests
- **Benchmark tool:** `PerformanceBenchmark/` project
- **Scenarios:** 10k, 50k, 100k fish over 100 days
- **Metrics:** ms/tick, memory usage

---

## 🔮 Future Architecture Plans

### Planned Improvements
1. **ECS Migration:** For 500k+ fish simulation
2. **Cloud Save/Sync:** Steam Cloud + multiplayer sync
3. **Headless Server:** For multiplayer host
4. **Modding API:** Lua/C# scripting for custom content
5. **SIMD Vectorization:** AVX2 for growth calculations

### Scalability Goals
- **1 million fish simulation** @ 30 FPS (with ECS + SIMD)
- **Multiplayer:** 8-16 players on same server
- **Mod ecosystem:** Steam Workshop integration

---

## 📖 Further Reading

- **[Architecture Deep Dives](Architecture_DeepDives.md)** — Cohort model, genetics, save format, performance, localization
- **[API Reference](API_Reference.md)** — SimBridge, GameBridge, EventBus, SimulationEngine APIs
- **[System Diagrams](diagrams/)** — Visual architecture, data flow, biology lifecycle, disease, market
  - [Architecture Overview](diagrams/architecture-overview.md)
  - [Data Flow](diagrams/data-flow.md)
  - [Biology Lifecycle](diagrams/biology-lifecycle.md)
  - [Disease Propagation](diagrams/disease-propagation.md)
  - [Market Pricing](diagrams/market-pricing.md)
- **[Tutorials](tutorials/)** — Getting started, building, testing, modding, contributing
- **[Modding API](Modding_API.md)** — Mod development guide

## 📚 References

- **Godot C# Docs:** https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/
- **Arch ECS:** https://github.com/genaray/Arch
- **.NET Performance:** https://learn.microsoft.com/en-us/dotnet/core/diagnostics/

---

**Last Updated:** 2026-03-15  
**Maintained By:** Fredrikke (Chief Software Engineer)
