# Godot Integration — SimBridge + Dashboard + GameController
**Date:** 2026-03-13
**Engineer:** Ingrid Lunde (UI/UX & Tools)
**Sprint:** Godot Integration Sprint

---

## Summary

Wired the C# simulation engine to the Godot UI layer via a clean two-tier bridge:

```
SimulationEngine (C#)
    ↓  Initialize() / Advance(days)
GameBridge.cs   ← C# autoload (existing, now fixed)
    ↓  signals: game_state_updated, simulation_ticked, game_started, game_ended
    ↓  properties: CurrentDay, Balance, TotalFishCount, TotalBiomassKg,
       CurrentTemperatureC, MarketPricePerKg, SeaLicePerFish,
       SustainabilityScore, SustainabilityLevel, ZoneCount, SiteCount
    ↓  method: GetRecentEvents(n) → Array[Dictionary]
SimBridge.gd    ← GDScript autoload (new)
    ↓  typed properties, formatting helpers, alert logic, signals
Dashboard.gd    ← UI node (updated — reads real data)
GameController.gd ← Input handler (new — advance buttons, keyboard shortcuts)
```

---

## Files Changed / Created

| File | Action | Description |
|------|--------|-------------|
| `godot/scripts/autoload/GameBridge.cs` | Fixed | Correct C# API, added GDScript-accessible properties |
| `godot/scripts/SimBridge.gd` | New | GDScript autoload wrapping GameBridge |
| `godot/scripts/ui/Dashboard.gd` | Rewritten | Reads real data from SimBridge, all KPI sections live |
| `godot/scripts/GameController.gd` | New | Advance buttons + keyboard shortcuts |
| `godot/project.godot` | Updated | SimBridge registered as autoload |

---

## GameBridge.cs — Fixes

The original GameBridge.cs had several API mismatches vs SimulationEngine:

| Bug | Old (broken) | Fixed |
|-----|-------------|-------|
| Initialize engine | `SimulationEngine.StartNewGame()` | `SimulationEngine.Initialize()` |
| Tick engine | `SimulationEngine.ProcessDay()` | `SimulationEngine.Advance(1)` |
| Read current day | `GameState.CurrentDay` | `SimulationEngine.State.Day` |
| Read balance | `GameState.Cash` | `SimulationEngine.FinanceManager.Balance` |
| Load saved state | `SimulationEngine.LoadGame(state)` | Reconstruct from snapshot |

### New properties exposed to GDScript

```csharp
int    CurrentDay          // State.Day
double Balance             // FinanceManager.Balance (NOK)
int    TotalFishCount      // Σ cohort.Count across all populations
double TotalBiomassKg      // Σ cohort.Count × cohort.MeanWeightKg
double CurrentTemperatureC // Weather.CurrentTemperatureC
double MarketPricePerKg    // MarketSimulation.PricePerKg (with cert multiplier)
double WeeklyAvgPricePerKg // MarketSimulation.WeeklyAvgPrice
int    SustainabilityScore // SustainabilityManager.CurrentScore (0–100)
string SustainabilityLevel // CertificationLevel enum as string
int    ZoneCount           // State.Zones.Count
int    SiteCount           // State.SeaSites.Count
double SeaLicePerFish      // SeaLice.AverageLicePerFish
```

### New methods

```csharp
void   AdvanceDays(int days)                         // manual skip (week/month)
Array<Dictionary> GetRecentEvents(int count = 10)    // GDScript-friendly event log
```

---

## SimBridge.gd — Architecture

**Type:** GDScript `extends Node`, registered as autoload `SimBridge`.

Responsibilities:
- Connects to GameBridge signals on `_ready()`
- Caches a typed snapshot of all key KPIs each tick
- Runs alert logic (sea lice > 2/5, negative balance, temp > 18°C, sustain < 50)
- Emits `state_updated`, `day_advanced(day, season)`, `alert_raised(level, msg)`
- Exposes control API: `advance_day()`, `advance_week()`, `advance_month()`, `set_paused()`, `set_time_speed()`
- Provides formatting helpers: `format_nok()`, `format_biomass()`, `format_fish()`, `lice_color()`, `balance_color()`, `sustainability_color()`

---

## Dashboard.gd — Real Data Sections

All TODO placeholders replaced with live data from SimBridge:

| Section | Data shown |
|---------|-----------|
| **Overview** | Day counter, NOK balance (color-coded), season, site/zone count |
| **Production** | Fish count, biomass (tonnes), sea temperature (color: green 8–16°C) |
| **Health** | Sea lice / fish (color-coded), overall health status (Kritisk/Dårlig/Middels/God) |
| **Market** | Spot price NOK/kg, 7-day average, delta color (green = above avg) |
| **Sustainability** | Score 0–100, certification level (Norwegian labels), color-coded |
| **Alerts** | Dynamic list from SimBridge.active_alerts (⚠ / 🔴 prefix) |
| **Recent Events** | Last 10 events, newest first, balance change shown in green/red |

Uses `@onready` with graceful `_get()` fallback — nodes not yet wired in the .tscn will log a warning and be skipped rather than crashing.

---

## GameController.gd — Player Input

**Type:** `extends Node`, attached to a scene node (not autoload).

Controls:
- `@export var player_name` / `starting_capital` / `auto_start` — configurable in Inspector
- Wires `NextDayButton`, `NextWeekButton`, `NextMonthButton`, `PauseButton` via `%UniqueNodeName`
- Keyboard shortcuts: `D` = next day, `W` = next week, `M` = next month, `Space` = pause
- `start_game()` / `set_speed()` public API for MainMenu / HUD integration
- Emits `pause_toggled(bool)` and `advance_completed(days)` signals for HUD sync

---

## Autoload Order (project.godot)

```ini
GameBridge    = "*res://scripts/autoload/GameBridge.cs"   ; C# — engine owner
EventBus      = "*res://scripts/autoload/EventBus.cs"     ; C# — signal hub
AudioManager  = "*res://scripts/autoload/AudioManager.cs" ; C# — audio
SaveLoadManager = "*res://scripts/autoload/SaveLoadManager.cs"
SimBridge     = "*res://scripts/SimBridge.gd"             ; GDScript — UI data layer ✨
```

SimBridge must load **after** GameBridge so the `/root/GameBridge` path exists when `_ready()` fires.

---

## Data Flow Diagram

```
Player presses "Next Week"
    → GameController._on_next_week()
    → SimBridge.advance_week()
    → GameBridge.AdvanceDays(7)
    → SimulationEngine.Advance(7)
        ├─ WeatherSystem.AdvanceDays(7)
        ├─ Zone.SetTemperature() × 7
        ├─ FishPopulation.AdvanceDays(7) × populations
        ├─ FeedSystem.Feed() × populations
        ├─ SeaLiceModel.AdvanceDays(7)
        ├─ IntegratedDiseaseSystem.Update()
        ├─ MarketSimulation.AdvanceDays(7)
        └─ SustainabilityManager.UpdateScore() (weekly)
    → GameBridge emits SimulationTicked(day), GameStateUpdated
    → SimBridge._refresh_state() — pulls all scalar props
    → SimBridge._check_alerts() — evaluates thresholds
    → SimBridge emits state_updated, day_advanced
    → Dashboard._refresh() — updates all label nodes
```

---

## Known Limitations / Next Steps

1. **Scene wiring:** Dashboard.gd and GameController.gd use `%UniqueNodeName` and path-based `@onready`. The corresponding `.tscn` scenes need the node unique-names set in the Godot editor to match (`NextDayButton`, `NextWeekButton`, etc.). Until then, nodes log a warning and are skipped gracefully.

2. **EventLog population:** `SimulationEngine.State.EventLog` is only populated by the `Storyteller` system, which is not yet wired into `SimulationEngine.Advance()`. Events will show an empty list until Storyteller is connected.

3. **SeaLice seeding:** `SeaLiceModel` starts at 0.0 and grows at 0.01/day + 1%/day. In a new game the lice widget will show a slowly rising value — realistic for a new stocked site.

4. **Save/Load restoration:** `LoadGame()` in GameBridge does a shallow copy of lists. Complex nested references (zone→population linkages) need deeper restoration; marked TODO in the method.

5. **HUD sync:** HUD.gd still reads from GameBridge directly. A follow-up task should migrate HUD to SimBridge so all UI uses one data path.
