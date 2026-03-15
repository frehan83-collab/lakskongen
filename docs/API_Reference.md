# Lakskongen — API Reference

**Version:** 0.4.0  
**Last Updated:** 2026-03-15

---

## Table of Contents

- [SimBridge (GDScript API)](#simbridge-gdscript-api)
- [GameBridge (C# Autoload)](#gamebridge-c-autoload)
- [EventBus (Signal System)](#eventbus-signal-system)
- [SimulationEngine (Core API)](#simulationengine-core-api)
- [Save/Load System](#saveload-system)
- [Event System](#event-system)

---

## SimBridge (GDScript API)

**File:** `godot/scripts/SimBridge.gd`  
**Type:** GDScript Autoload Singleton  
**Purpose:** Exposes all simulation state as typed, GDScript-friendly properties. UI nodes should read from SimBridge rather than calling the C# layer directly.

### Signals

| Signal | Parameters | Description |
|--------|-----------|-------------|
| `state_updated` | — | Emitted every tick after snapshot refresh |
| `day_advanced` | `day: int, season: String` | Emitted each simulated day |
| `alert_raised` | `level: String, message: String` | KPI crosses alert threshold |
| `tutorial_step_completed` | `step_number: int, title: String, reward_nok: float` | Tutorial progress |
| `audio_alert_triggered` | `alert_key: String` | One-shot SFX trigger |
| `achievement_unlocked` | `id: String, name: String, desc: String, reward: float, tier: String` | Achievement earned |

### Properties (Read-Only Snapshot)

| Property | Type | Description |
|----------|------|-------------|
| `current_day` | `int` | Simulation day counter |
| `balance` | `float` | Cash balance (NOK) |
| `fish_count` | `int` | Total live fish across all sites |
| `fish_biomass_kg` | `float` | Total biomass (kg) |
| `temperature_c` | `float` | Average water temperature |
| `market_price_nok` | `float` | Current spot price (NOK/kg) |
| `weekly_avg_price_nok` | `float` | 7-day average price |
| `sea_lice_per_fish` | `float` | Average lice count per fish |
| `sustainability_score` | `int` | 0-100 sustainability rating |
| `sustainability_level` | `String` | NonCompliant / Bronze / Silver / Gold / ASC |
| `zone_count` | `int` | Number of active zones |
| `site_count` | `int` | Number of active sea sites |
| `recent_events` | `Array[Dictionary]` | Last 10 events |
| `active_alerts` | `Array[Dictionary]` | Current alert messages |

### Audio Properties

| Property | Type | Values |
|----------|------|--------|
| `audio_base_track` | `String` | `calm_sea`, `storm`, `wind`, `harbor`, `processing_plant` |
| `audio_weather_layer` | `String` | `""`, `wind`, `rain`, `storm` |
| `audio_alert_layer` | `String` | `""`, `disease_outbreak`, `market_crash`, `achievement`, `harvest`, `lice_warning` |
| `audio_volume` | `float` | 0.0 – 1.0 |

### External Data Properties

| Property | Type | Description |
|----------|------|-------------|
| `external_warning_count` | `int` | BarentsWatch warning count |
| `external_composite_risk` | `float` | Risk index from external data |
| `external_crisis_active` | `bool` | Whether crisis event is active |
| `external_crisis_label` | `String` | Crisis severity label |
| `external_snapshot_source` | `String` | Data source name |
| `external_snapshot_freshness_days` | `int` | Data age (-1 = offline) |

### Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `get_farm_data(site_index: int)` | `Dictionary` | Farm details for specific site |
| `get_population_summary()` | `Array[Dictionary]` | All population summaries |
| `get_cohort_details(pop_index: int)` | `Array[Dictionary]` | Cohort breakdown |
| `get_genetics_summary(pop_index: int)` | `Dictionary` | Genetic trait averages |
| `get_research_tree()` | `Array[Dictionary]` | All research nodes + status |
| `get_achievements()` | `Array[Dictionary]` | All achievements + unlock status |

### Usage Example

```gdscript
# In any UI script:
func _ready():
    SimBridge.state_updated.connect(_on_state_updated)
    SimBridge.achievement_unlocked.connect(_on_achievement)

func _on_state_updated():
    balance_label.text = "NOK %s" % format_number(SimBridge.balance)
    fish_label.text = "%d fish" % SimBridge.fish_count
    price_label.text = "%.1f NOK/kg" % SimBridge.market_price_nok

func _on_achievement(id, name, desc, reward, tier):
    show_popup("🏆 %s — %s" % [name, desc])
```

---

## GameBridge (C# Autoload)

**File:** `godot/scripts/autoload/GameBridge.cs`  
**Type:** C# Godot Node (autoload)  
**Purpose:** Bridges SimulationEngine to Godot. Manages engine lifecycle, tick pumping, and signal emission.

### Signals

| Signal | Parameters | Description |
|--------|-----------|-------------|
| `GameStateUpdated` | — | Fired after each tick |
| `SimulationTicked` | `int day` | Day number after tick |
| `GameStarted` | — | New game initialized |
| `GameEnded` | `string reason` | Game over |
| `TutorialStepCompleted` | `int step, string title, double reward` | Tutorial progress |
| `AudioAlertTriggered` | `string alertKey` | SFX cue |
| `AchievementUnlocked` | `string id, name, desc, double reward, string tier` | Achievement |
| `ResearchCompleted` | `string nodeId, string nodeName` | Research done |
| `SustainabilityChanged` | `int score, string level` | Rating changed |
| `FinanceTransaction` | `string type, double amount, string desc` | Money moved |

### Key Methods

| Method | Description |
|--------|-------------|
| `StartNewGame(string name, int capital = 10_000_000)` | Initialize and start |
| `LoadGame(GameState snapshot)` | Restore from save |
| `SetTimeSpeed(float speed)` | Change simulation speed (0.5x–4x) |
| `TogglePause()` | Pause/resume simulation |
| `StockFish(int siteIndex, int count)` | Release smolts to sea |
| `HarvestFish(int siteIndex, int count)` | Harvest fish from site |
| `AcceptContract(string contractId)` | Accept market contract |
| `StartResearch(string nodeId)` | Begin researching technology |
| `LoadScenario(string scenarioId)` | Load a campaign scenario |
| `ExportGameData(string filePath)` | Export analytics to CSV |

---

## EventBus (Signal System)

**File:** `godot/scripts/autoload/EventBus.cs`  
**Type:** C# Godot Node (autoload)  
**Purpose:** Global signal bus for decoupled UI ↔ game communication.

### Signal Categories

#### Time & Progress
```csharp
signal DayAdvanced(int day, string season)
signal TimeSpeedChanged(float speed)
signal GamePaused(bool isPaused)
```

#### Financial
```csharp
signal FinanceUpdated(long cash, long netWorth)
signal MarketPriceChanged(float pricePerKg, string trend)
signal RevenueEarned(long amount, string source)
signal ExpenseIncurred(long amount, string source)
```

#### Production
```csharp
signal HarvestCompleted(int farmId, int fishCount, float avgWeight, long revenue)
signal FishStocked(int farmId, int fishCount, float avgWeight)
signal GrowthReport(int farmId, float avgGrowthRate, int totalFish)
```

#### Health & Disease
```csharp
signal DiseaseOutbreak(int farmId, string diseaseName, int affectedFish)
signal LiceWarning(int farmId, float liceCount, string severity)
signal MortalityReport(int farmId, int deaths, string cause)
signal TreatmentApplied(int farmId, string treatmentType, long cost)
```

#### Environment
```csharp
signal WeatherChanged(string weatherType, float temperature, string impact)
signal WaterQualityChanged(int farmId, float oxygenLevel, float temperature)
signal SeasonChanged(string newSeason, string effects)
```

#### Farm Management
```csharp
signal FarmBuilt(int farmId, string location, long cost)
signal FarmUpgraded(int farmId, string upgradeType, long cost)
signal FarmSold(int farmId, long salePrice)
signal CapacityChanged(int farmId, int newCapacity)
```

### Connecting to Signals (GDScript)

```gdscript
func _ready():
    EventBus.DayAdvanced.connect(_on_day_advanced)
    EventBus.DiseaseOutbreak.connect(_on_disease)
    EventBus.MarketPriceChanged.connect(_on_price_changed)

func _on_day_advanced(day: int, season: String):
    day_label.text = "Day %d — %s" % [day, season]

func _on_disease(farm_id: int, disease: String, affected: int):
    show_alert("⚠️ %s outbreak at Farm %d: %d fish affected!" % [disease, farm_id, affected])
```

---

## SimulationEngine (Core API)

**File:** `src/LakskongenSim/Core/SimulationEngine.cs`  
**Type:** Pure C# (no Godot dependency)  
**Purpose:** Central orchestrator connecting all subsystems.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Time` | `TimeManager` | Day/week/month tracking |
| `State` | `GameState` | Serializable game state |
| `Weather` | `WeatherSystem` | Weather conditions |
| `Environment` | `EnvironmentModel` | Zone + water chemistry |
| `Growth` | `GrowthModel` | Fish growth calculations |
| `Mortality` | `MortalityModel` | Death rate management |
| `Feed` | `FeedSystem` | Feed and FCR |
| `Disease` | `DiseaseSystem` | SIR epidemic model |
| `IntegratedDisease` | `IntegratedDiseaseSystem` | Multi-disease orchestrator |
| `Hatchery` | `Hatchery` | Egg → smolt pipeline |
| `Genetics` | `Genetics` | Breeding and trait system |
| `MarketSimulation` | `MarketSimulation` | O-U price model |
| `ContractSystem` | `ContractSystem` | Market contracts |
| `FinanceManager` | `FinanceManager` | Cash flow tracking |
| `SustainabilityManager` | `SustainabilityManager` | ESG rating |
| `Storyteller` | `Storyteller` | Event director |
| `Tutorial` | `TutorialSystem` | Onboarding system |
| `Analytics` | `AnalyticsTracker` | Daily snapshot recording |
| `Achievements` | `AchievementSystem` | Achievement tracking |
| `Research` | `ResearchSystem` | Tech tree progression |

### Events

| Event | Parameters | Description |
|-------|-----------|-------------|
| `OnDayAdvanced` | `int day` | Fired after each tick |
| `OnResearchCompleted` | `ResearchNode` | Tech unlocked |
| `OnEventTriggered` | `string message` | Narrative event |
| `OnEventFired` | `EventRecord` | Detailed event data |
| `OnAchievementUnlocked` | `Achievement` | Achievement earned |

### Key Methods

| Method | Description |
|--------|-------------|
| `Initialize(bool enableEvents = true)` | Wire all subsystems |
| `AdvanceOneDay()` | Run one simulation tick |
| `AdvanceDays(int days)` | Run multiple ticks |

---

## Save/Load System

**File:** `src/LakskongenSim/Core/SaveLoadManager.cs`  
**Type:** Pure C# — JSON serialization with backup rotation

### Methods

| Method | Description |
|--------|-------------|
| `SaveGame(GameState state, string saveName = "save")` | Serialize to JSON with backup rotation |
| `LoadGame(string saveName = "save")` | Deserialize from JSON |
| `LoadWithFallback(string saveName = "save")` | Try primary, fall back to backups |
| `ListSaves()` | List available save files |
| `DeleteSave(string saveName)` | Remove save + backups |

### Backup Rotation

On each save, the system rotates:
```
save.json       → save.json.bak1  (most recent backup)
save.json.bak1  → save.json.bak2
save.json.bak2  → save.json.bak3
save.json.bak3  → deleted
```

### JSON Options
- `WriteIndented = true` (human-readable)
- `JsonStringEnumConverter` (enums as strings)
- All public properties serialized
- Missing properties on load use GameState defaults (backward compatible)

---

## Event System

**File:** `src/LakskongenSim/Events/Storyteller.cs`  
**Type:** Pure C# event director

### Event Types

| Category | Examples |
|----------|---------|
| **Weather** | Storm, algal bloom, cold snap |
| **Market** | Price crash, demand surge, trade sanctions |
| **Disease** | IPN outbreak, ISA detection |
| **Regulatory** | Inspection, traffic light change |
| **Political** | Environmental protest, subsidy announcement |
| **Norwegian** | Midnight sun festival, Christmas market |

### Registering Custom Events

```csharp
// Register a new event with the Storyteller
storyteller.RegisterEvent(new EventDefinition
{
    Id = "my_custom_event",
    Name = "Custom Event",
    Category = EventCategory.Market,
    Description = "Something interesting happens",
    MinDay = 100,        // Earliest trigger day
    MaxDay = 900,        // Latest trigger day
    Probability = 0.05,  // 5% chance per eligible tick
    BalanceChange = -50000m,  // Cost to player
    Cooldown = 90        // Minimum days between occurrences
});
```

### Event Record Structure

```csharp
public record EventRecord(
    int Day,
    string Name,
    string Category,
    string Description,
    decimal BalanceChange,
    Dictionary<string, object> Metadata
);
```

---

## Cross-Reference

| Document | Content |
|----------|---------|
| [Architecture Overview](Architecture.md) | Full system design |
| [Modding API](Modding_API.md) | Mod development guide |
| [Diagrams](diagrams/) | Visual system diagrams |
| [Tutorials](tutorials/) | Step-by-step guides |

---

**Last Updated:** 2026-03-15  
**Maintained By:** Lakskongen Development Team
