# Refactoring Roadmap — Lakskongen Simulation Layer

**Created:** 2026-03-15  
**Source:** T74 Code Review

---

## Phase 1: Pre-Launch Fixes (1-2 weeks)

### 1.1 Financial System Consolidation (3 days)
**Risk: HIGH — Money bugs are player-visible**

| Task | Effort | Impact |
|------|--------|--------|
| Designate `FinanceManager.Balance` as single source of truth | 2h | Critical |
| Remove `GameState.Balance` setter; make it read from `FinanceManager` | 2h | Critical |
| Remove `FinancialStatements.Cash` independent tracking | 2h | High |
| Route all Storyteller event balance changes through `FinanceManager` | 3h | Critical |
| Add bankruptcy/debt logic when `Spend()` returns false | 4h | High |
| Add tests for negative balance scenarios | 2h | High |

### 1.2 RNG Safety (1 day)

| Task | Effort | Impact |
|------|--------|--------|
| Replace `static Random` in `GrowthModel` with `Random.Shared` | 30m | High |
| Fix `Zone.UpdateCurrents()` — use per-zone cached `Random` | 1h | Medium |
| Fix `EstimateZoneDistance` — deterministic or cached | 1h | Medium |

### 1.3 Save/Load Integrity (1 day)

| Task | Effort | Impact |
|------|--------|--------|
| Add `SimulationEngine.RestoreFromSave(GameState)` method | 3h | Critical |
| Re-inject `GrowthModel`/`MortalityModel` into loaded `FishPopulation` objects | 2h | Critical |
| Write save/load round-trip integration test | 2h | High |

### 1.4 Storyteller Double-Mortality Fix (0.5 days)

| Task | Effort | Impact |
|------|--------|--------|
| Remove direct cohort count manipulation from `ApplyBiologicalImpacts` | 2h | High |
| Route all mortality through `MortalityModel` rate injection | 2h | High |

---

## Phase 2: EA Stability (Months 1-3)

### 2.1 Memory Leak Prevention (2 days)

| Task | Effort | Impact |
|------|--------|--------|
| Add periodic clearing of `StochasticDiseaseEngine._individualStates` | 2h | High |
| Cap `FinancialStatements.Events` list (ring buffer or periodic trim) | 1h | Medium |
| Cap `GameState.DailySnapshots` (keep last 365*3 days) | 1h | Medium |
| Pool `FishIndividual` objects or cache per-tick | 4h | Medium |

### 2.2 Magic Number Extraction (2 days)

| Task | Effort | Impact |
|------|--------|--------|
| Move hardcoded growth parameters from `SimulationEngine.AdvanceOneDay()` to `BalancingConfig` | 3h | Medium |
| Move waste coefficients from `WaterChemistry` to constants | 1h | Low |
| Move disease parameters from `StochasticDiseaseEngine` to config | 2h | Medium |
| Move feed quality bonuses from `FeedSystem` to config | 1h | Low |
| Create `LifeStageProfile` class for stage multipliers | 4h | Medium |

### 2.3 Test Coverage Expansion (3 days)

| Task | Effort | Impact |
|------|--------|--------|
| Disease system SIR model unit tests | 4h | High |
| Water chemistry ODE stability tests | 3h | High |
| Genetics breeding/mutation tests | 2h | Medium |
| Feed system FCR calculation tests | 2h | Medium |
| Sustainability scoring tests | 2h | Medium |
| Storyteller event triggering tests | 3h | Medium |

### 2.4 Logging Upgrade (1 day)

| Task | Effort | Impact |
|------|--------|--------|
| Create `ISimLogger` interface | 1h | Medium |
| Replace `Console.WriteLine` with logger calls (40+ instances) | 3h | Medium |
| Add log level filtering (Debug/Info/Warning/Error) | 2h | Medium |

---

## Phase 3: Post-EA Architecture (Months 3-6)

### 3.1 SimulationEngine Decomposition (5 days)

**Goal:** Break the ~500-line God class into focused components.

```
SimulationEngine (orchestrator only)
├── ConfigurationLoader        — JSON loading, path resolution
├── SubsystemFactory           — Creates and wires subsystems
├── DailyTickProcessor         — AdvanceOneDay() logic
├── EventRouter                — Storyteller → subsystem routing
└── ExternalDataCalibrator     — BarentsWatch, aquaculture data
```

| Task | Effort | Impact |
|------|--------|--------|
| Extract `ConfigurationLoader` class | 4h | Medium |
| Extract `DailyTickProcessor` class | 6h | Medium |
| Extract `EventRouter` class | 4h | Medium |
| Extract `ExternalDataCalibrator` class | 4h | Low |
| Extract `BalancingConfig` to standalone class | 1h | Low |

### 3.2 Interface Introduction (3 days)

**Goal:** Enable unit testing with mocks and future flexibility.

| Interface | Implementations | Purpose |
|-----------|----------------|---------|
| `IWeatherProvider` | `WeatherSystem` | Decouple weather from engine |
| `IGrowthModel` | `GrowthModel` | Test biology independently |
| `IMortalityModel` | `MortalityModel` | Test mortality independently |
| `IMarketSimulation` | `MarketSimulation` | Test market independently |
| `IFinanceManager` | `FinanceManager` | Test finance independently |
| `IDiseaseSystem` | `DiseaseSystem`, `IntegratedDiseaseSystem` | Test disease independently |

### 3.3 Disease LOD System Completion (4 days)

**Goal:** The LOD system has placeholder methods that return empty lists. Complete the implementation.

| Task | Effort | Impact |
|------|--------|--------|
| Implement `GetInfectedIndividuals()` with actual disease engine queries | 4h | High |
| Implement `GetAllIndividuals()` with cohort-to-individual conversion | 4h | High |
| Implement `GetZonesForFish()` and `GetPopulationsForFish()` | 3h | Medium |
| Implement `UpdateBaseSystemPrevalence()` to write back calculated prevalence | 2h | High |
| Add integration tests for LOD tier transitions | 4h | Medium |

### 3.4 Water Chemistry Numerical Stability (2 days)

| Task | Effort | Impact |
|------|--------|--------|
| Switch from Euler to semi-implicit integration | 4h | Medium |
| Add per-step clamping (clamp immediately after each variable update) | 2h | Medium |
| Add numerical stability tests with extreme inputs | 3h | Medium |

---

## Phase 4: Long-Term Evolution (6-12 months)

### 4.1 Modern C# Migration

| Task | Effort | Impact |
|------|--------|--------|
| Enable `<Nullable>enable</Nullable>` and fix warnings | 8h | Medium |
| Convert data classes to records (`Genome`, `EventRecord`, etc.) | 4h | Low |
| Use switch expressions for LifeStage logic | 2h | Low |
| Seal leaf classes for devirtualization | 1h | Low |
| Make status structs `readonly struct` | 1h | Low |

### 4.2 Performance Optimization V2

| Task | Effort | Impact |
|------|--------|--------|
| Replace LINQ in `FeedSystem.Feed()` with indexed loops | 2h | Medium |
| Implement object pooling for `FishIndividual` | 4h | Medium |
| Profile GC allocation in full 10-year simulation | 4h | Medium |
| Investigate `Span<T>` for configuration parsing | 4h | Low |
| Benchmark and optimize `WaterChemistry.UpdateOneDay()` | 3h | Low |

### 4.3 Multiplayer / Networking Preparation

| Task | Effort | Impact |
|------|--------|--------|
| Make all simulation state deterministic (fixed seeds, no `DateTime.Now`) | 8h | High |
| Add simulation checksum for desync detection | 4h | High |
| Separate input handling from simulation state mutation | 8h | High |

---

## Dependency Graph

```
Phase 1 (Pre-launch)
  ├── 1.1 Financial Consolidation ──→ Phase 2 test coverage
  ├── 1.2 RNG Safety ──→ Phase 4 deterministic sim
  ├── 1.3 Save/Load Integrity ──→ Phase 3 interfaces
  └── 1.4 Double-Mortality Fix ──→ Phase 3 LOD completion

Phase 2 (EA Stability)
  ├── 2.1 Memory Leaks ──→ Phase 4 perf v2
  ├── 2.2 Magic Numbers ──→ Phase 3 config extraction
  ├── 2.3 Tests ──→ Phase 3 interfaces
  └── 2.4 Logging ──→ independent

Phase 3 (Architecture)
  ├── 3.1 Engine Decomposition ──→ Phase 4 multiplayer
  ├── 3.2 Interfaces ──→ Phase 4 multiplayer
  ├── 3.3 LOD Completion ──→ independent
  └── 3.4 Water Chemistry ──→ independent

Phase 4 (Evolution)
  └── All items are independent improvements
```
