# Event Integration — Storyteller wired into SimulationEngine
**Date:** 2026-03-13
**Author:** Camilla Johansen (Gameplay Programmer)

---

## Summary

The Storyteller event system was fully wired into `SimulationEngine` so that Norwegian
salmon-farming events now fire during gameplay with real consequences across all subsystems.

---

## Files changed

### `src/LakskongenSim/Core/SimulationEngine.cs`
- Added `using LakskongenSim.Events;`
- Added `public Storyteller Storyteller { get; private set; }` property
- Added `public event Action<EventRecord>? OnEventFired;` for UI notification
- In `Initialize()`: created `Storyteller(Time, State)`, registered all 40+ `NorwegianEvents`,
  wired `OnEventRaised → HandleStorytellerEvent`
- In `AdvanceOneDay()` **Phase 4.5**: calls `Time.AdvanceDays(1)`, which pulses the Storyteller's
  `OnAdvanceDays` subscription. Storyteller evaluates pacing, cooldowns, and weighted selection
  against the current `State.Day`.
- Added `HandleStorytellerEvent(EventRecord)`: fires `OnEventFired` / `OnEventTriggered` for UI,
  logs to console, then dispatches to the four consequence-chain methods below.
- Added `ApplyDiseaseConsequence`: runs 3 extra `IntegratedDisease.Update()` days when a disease
  event fires, escalating the stochastic/LOD disease simulation.
- Added `ApplyWeatherConsequence`: maps event IDs to `Weather.ForceWeatherState()` or
  `Weather.ApplyTemperatureAnomaly()` — e.g. storms → `WeatherState.Storm`, heatwaves → +4 °C.
- Added `ApplyMarketConsequence`: maps event IDs to `MarketSimulation.ApplyExternalShock()` with
  calibrated supply/demand multipliers (e.g. oversupply crash → supply ×1.15 / demand ×0.90).
- Added `ApplyRegulatoryConsequence`: heavy regulatory events (escape penalties, traffic-light red,
  welfare inspections) call `SustainabilityManager.ApplyRegulatoryPenalty(10)`; positive events
  (green zone, development license) trigger `UpdateScore()`.

### `src/LakskongenSim/Market/MarketSimulation.cs`
- Added `public void ApplyExternalShock(double supplyFactor, double demandFactor)`: allows the
  engine to inject supply/demand pressure from narrative events into the Ornstein-Uhlenbeck price
  model. Values are clamped to [0.5, 2.0] to prevent runaway prices.

### `src/LakskongenSim/Environment/WeatherSystem.cs`
- Added `public void ForceWeatherState(WeatherState state)`: overrides the Markov chain state for
  the current day; natural mean-reversion resumes on the next tick.
- Added `public void ApplyTemperatureAnomaly(double deltaC)`: offsets sea surface temperature
  (clamped to realistic 1.5–19 °C range); used for heatwave (+4 °C) and cold-snap (−3.5 °C) events.

### `src/LakskongenSim/Sustainability/SustainabilityManager.cs`
- Added `public void ApplyRegulatoryPenalty(int penaltyPoints)`: directly reduces `CurrentScore`
  and fires `OnScoreChanged` / `OnCertificationChanged` if the certification tier drops. Regulatory
  events (escape fines, red-zone decisions, welfare inspections) now immediately degrade the
  sustainability standing rather than waiting for the weekly recalculation.

---

## Architecture: Phase 4.5 EVENTS

```
AdvanceOneDay():
  Phase 1  — Weather & Environment
  Phase 2  — Biology (Growth & Mortality)
  Phase 3  — Health (Disease & Sea Lice)
  Phase 4  — Market & Finance
  Phase 4.5— EVENTS  ← NEW: Time.AdvanceDays(1) → Storyteller.HandleAdvance()
                          → WeightedPick from 40+ NorwegianEvents
                          → Apply(GameState, EventRecord)   [balance change + description]
                          → HandleStorytellerEvent()        [consequence chains]
                          → EventLog.Add(record)            [persisted in GameState]
                          → OnEventFired?.Invoke(record)    [UI notification]
  Phase 5  — Sustainability (weekly)
```

---

## Consequence chains

| Event category | Subsystem hooked              | Mechanism                                        |
|----------------|-------------------------------|--------------------------------------------------|
| Disease        | `IntegratedDiseaseSystem`     | Extra 3-day LOD disease update pass              |
| Weather        | `WeatherSystem`               | `ForceWeatherState` or `ApplyTemperatureAnomaly` |
| Market         | `MarketSimulation`            | `ApplyExternalShock(supplyFactor, demandFactor)` |
| Regulation     | `SustainabilityManager`       | `ApplyRegulatoryPenalty` or `UpdateScore`        |

---

## Event pool (after `NorwegianEvents.RegisterAll`)

40+ events across 8 categories:
- **Disease** (10): ISA, PD, CMS, AGD, IHN, winter ulcers, sea lice infestation,
  disease-resistance breakthrough, new vaccine, cleaner fish success
- **Weather** (8): Storm Berit, toxic algal bloom, jellyfish swarm, heatwave, cold snap,
  perfect growing conditions, oxygen depletion, seal predation
- **Market** (8): Chinese demand surge, Chilean production crash, Brexit tariffs, US sushi boom,
  NOK weakens, oversupply crash, ASC premium grows, long-term contract
- **Regulatory** (6): Traffic-light green/red, escape incident fine, resource rent tax,
  development license, Mattilsynet welfare inspection
- **Equipment/Community/Competitor/Investor**: stubs registered for future content

---

## Known gaps / future work

- Disease consequence currently runs extra update days; a proper "inject outbreak at zone X" API
  would allow targeting specific populations and triggering player decisions.
- `NorwegianEvents` entries marked `// TODO: Set zone to fallow` / `// TODO: integrate with
  DiseaseSystem` still need full gameplay hooks (culling, zone fallow, player choice dialogs).
- `TimeManager.CurrentDay` and `State.Day` now diverge by 1 within each tick (Time advances at
  Phase 4.5, State.Day increments after AdvanceOneDay returns). Both remain internally consistent
  for their respective consumers; Storyteller uses `State.Day` as authoritative.
