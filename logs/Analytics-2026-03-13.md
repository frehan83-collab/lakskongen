# Analytics Tracker — Sprint Log
**Date:** 2026-03-13
**Author:** Jonas Karlsen (Senior Systems)
**Branch:** main

---

## What was built

### 1. `src/LakskongenSim/Core/AnalyticsTracker.cs`

New file containing three public types:

| Type | Purpose |
|------|---------|
| `AnalyticsMetric` | String constants for all 10 metric keys |
| `DailySnapshot` | JSON-serializable struct of one day's KPIs |
| `AnalyticsTracker` | Service: captures snapshots, exposes query API |

**`DailySnapshot` fields captured each day:**

| Field | Source |
|-------|--------|
| `FishCount` | Sum of all cohort counts across populations |
| `TotalBiomassKg` | `count × meanWeightKg` per cohort |
| `AvgWeightKg` | `biomass / fishCount` |
| `TemperatureC` | `WeatherSystem.CurrentTemperatureC` |
| `MarketPriceNok` | `MarketSimulation.PricePerKg` |
| `BalanceNok` | `FinanceManager.Balance` |
| `FCR` | `FeedSystem.CurrentFCR` |
| `LicePerFish` | `SeaLiceModel.AverageLicePerFish` |
| `SustainabilityScore` | `SustainabilityManager.CurrentScore` |
| `MortalityRate` | Daily fractional change in fish count |

**`AnalyticsTracker` public API:**

```csharp
void RecordSnapshot(SimulationEngine engine)
List<double> GetTimeSeries(string metric, int lastNDays)
List<double> GetMovingAverage(string metric, int lastNDays, int windowDays)
TrendDirection GetTrend(string metric, int lastNDays = 14)   // Up / Down / Stable
double GetAllTimeHigh(string metric)
double GetAllTimeLow(string metric)
int SnapshotCount { get; }
```

Trend detection uses OLS linear regression slope normalised by the series mean; the
threshold is ±0.5 % change per day (tunable via `TrendThreshold` constant).

---

### 2. `src/LakskongenSim/Core/GameState.cs`

Added:
```csharp
public List<DailySnapshot> DailySnapshots { get; set; } = new();
```
The list is passed by reference into `AnalyticsTracker` so both the tracker and the
serialiser share the same backing collection. No breaking changes to existing properties.

---

### 3. `src/LakskongenSim/Core/SimulationEngine.cs`

- Added `public AnalyticsTracker Analytics { get; private set; }` property.
- `Initialize()` creates `Analytics = new AnalyticsTracker(State.DailySnapshots)` after the
  tutorial system.
- `AdvanceOneDay()` calls `Analytics?.RecordSnapshot(this)` as **Phase 7** (after all other
  subsystem updates), ensuring every snapshot reflects the final state for that day.

---

### 4. `godot/scripts/autoload/GameBridge.cs`

Five new methods added to the public bridge API:

| Method | Returns | Description |
|--------|---------|-------------|
| `GetAnalyticsData(metric, days)` | `Array<double>` | Raw time series, oldest→newest |
| `GetAnalyticsMovingAverage(metric, days, windowDays)` | `Array<double>` | Rolling mean |
| `GetAnalyticsTrend(metric, days)` | `string` | `"up"`, `"down"`, or `"stable"` |
| `GetAnalyticsAllTimeHigh(metric)` | `double` | Maximum ever recorded |
| `GetAnalyticsAllTimeLow(metric)` | `double` | Minimum ever recorded |

All methods null-guard against an uninitialised engine and return safe defaults.

---

### 5. `godot/scripts/ui/AnalyticsPanel.gd`

New GDScript panel displaying a 2×2 grid of mini-charts for the four most player-relevant
metrics:

1. **Fish Count** — blue
2. **Biomass (kg)** — green
3. **Price (NOK/kg)** — amber
4. **Balance (NOK)** — red

**Features:**

- **30d / 90d view toggle** via toolbar buttons.
- **Chart drawing** entirely via `_ChartArea._draw()` — no external plugins needed.
  Draws 3 horizontal grid lines, the raw series (2 px), and the 7-day MA (1.5 px, semi-transparent).
- **Trend arrows** (▲ green / ▼ red / ◆ grey) updated on every refresh cycle.
- **Latest value** formatted with domain-aware units (tonnes, kNOK, MNOK, lice/fish, etc.).
- **Procedural layout** — the panel builds its own VBox/Grid/Label/Control tree in `_ready()`;
  no external `.tscn` scene file required.
- Refreshes every ~2 seconds (`Engine.get_process_frames() % 120`).
- Safe no-op when `GameBridge` singleton is absent (editor mode).

---

## Integration notes

- `AnalyticsTracker` has **no Godot dependency** and can be unit-tested independently.
- `DailySnapshot` uses only BCL types so `System.Text.Json` serialises it without attributes.
- The snapshot list is part of `GameState`, so it is automatically persisted by
  `SaveLoadManager` when the game is saved.
- The `AnalyticsPanel` accesses GameBridge via `Engine.get_singleton("GameBridge")` — the
  same pattern used by `Dashboard.gd` and `SimBridge.gd`.

---

## Files changed

| File | Change |
|------|--------|
| `src/LakskongenSim/Core/AnalyticsTracker.cs` | **New** |
| `src/LakskongenSim/Core/GameState.cs` | Added `DailySnapshots` property |
| `src/LakskongenSim/Core/SimulationEngine.cs` | Added `Analytics` property + Phase 7 hook |
| `godot/scripts/autoload/GameBridge.cs` | Added 5 analytics bridge methods |
| `godot/scripts/ui/AnalyticsPanel.gd` | **New** |
