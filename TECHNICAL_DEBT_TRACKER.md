# Technical Debt Tracker — Lakskongen Simulation Layer

**Created:** 2026-03-15  
**Source:** T74 Code Review  
**Status:** Active

---

## Debt Inventory

### 🔴 Critical Debt (Fix before/during EA)

| ID | Description | Location | Effort | Risk | Status |
|----|-------------|----------|--------|------|--------|
| TD-01 | Triple cash tracking (GameState / FinanceManager / FinancialStatements) | `Core/`, `Finance/` | 1d | **HIGH** — Money desync causes invisible bugs | Open |
| TD-02 | `Spend()` ignores failure → no bankruptcy logic | `Finance/FinanceManager.cs` | 0.5d | **HIGH** — Infinite money exploit | Open |
| TD-03 | Storyteller directly mutates `GameState.Balance` | `Events/Storyteller.cs` | 0.5d | **HIGH** — Bypasses financial system | Open |
| TD-04 | Save/Load doesn't restore `GrowthModel`/`MortalityModel` refs | `Core/SaveLoadManager.cs` | 0.5d | **HIGH** — NRE after loading save | Open |
| TD-05 | Double mortality from Storyteller events + disease system | `Events/Storyteller.cs` | 0.5d | **MEDIUM** — Excessive fish death | Open |
| TD-06 | `static Random` not thread-safe in `GrowthModel` | `Biology/GrowthModel.cs` | 1h | **MEDIUM** — Corrupted RNG if threaded | Open |
| TD-07 | `EstimateZoneDistance` returns random values per call | `Health/IntegratedDiseaseSystem.cs` | 1h | **MEDIUM** — Incoherent disease spread | Open |

### 🟡 Moderate Debt (Fix during EA)

| ID | Description | Location | Effort | Risk | Status |
|----|-------------|----------|--------|------|--------|
| TD-08 | `_individualStates` dictionary grows unboundedly | `Health/StochasticDiseaseEngine.cs` | 2h | **MEDIUM** — Memory leak over time | Open |
| TD-09 | `FishPopulation.GetIndividuals()` allocates heavily per call | `Biology/FishPopulation.cs` | 4h | **MEDIUM** — GC pressure in disease tick | Open |
| TD-10 | `FinancialStatements.Events` list grows unboundedly | `Finance/FinancialStatements.cs` | 1h | **LOW** — Memory growth | Open |
| TD-11 | `GameState.DailySnapshots` grows unboundedly | `Core/GameState.cs` | 1h | **LOW** — Memory growth, save file bloat | Open |
| TD-12 | `Zone.UpdateCurrents()` allocates `new Random` every call | `Environment/Zone.cs` | 1h | **LOW** — GC pressure + weak seeds | Open |
| TD-13 | `WaterChemistry` ODE can overshoot (negative concentrations) | `Environment/WaterChemistry.cs` | 3h | **LOW** — Numerical instability at edges | Open |
| TD-14 | `MarketSimulation` hardcoded file path with relative `../..` | `Market/MarketSimulation.cs` | 1h | **MEDIUM** — Breaks in deployed builds | Open |
| TD-15 | `float` precision loss in `GetAvailableCapital()` | `Finance/FinanceManager.cs` | 30m | **LOW** — Rounding errors for large balances | Open |
| TD-16 | LongTermMean clamp (70-80) inconsistent with default price (100) | `Market/MarketSimulation.cs` | 1h | **MEDIUM** — Price behavior depends on data file presence | Open |

### 🟢 Low-Priority Debt (Post-EA)

| ID | Description | Location | Effort | Risk | Status |
|----|-------------|----------|--------|------|--------|
| TD-17 | SimulationEngine God class (~500 LOC, 20+ dependencies) | `Core/SimulationEngine.cs` | 3d | **LOW** — Hard to maintain/test | Open |
| TD-18 | 6 duplicate LifeStage switch blocks | Multiple files | 4h | **LOW** — DRY violation | Open |
| TD-19 | ~40 magic numbers scattered through codebase | Multiple files | 2d | **LOW** — Hard to tune/balance | Open |
| TD-20 | No interfaces → untestable subsystems in isolation | All subsystems | 3d | **LOW** — Test coverage ceiling | Open |
| TD-21 | `Console.WriteLine` as logging (~40 instances) | Multiple files | 1d | **LOW** — No log filtering/levels | Open |
| TD-22 | Empty `Core/Enums.cs` file | `Core/Enums.cs` | 5m | **NONE** — Dead code | Open |
| TD-23 | Duplicate `GameConstants` in Core and Data namespaces | `Core/`, `Data/` | 30m | **NONE** — Confusing | Open |
| TD-24 | `BalancingConfig` is private nested class in SimulationEngine | `Core/SimulationEngine.cs` | 1h | **LOW** — Not reusable | Open |
| TD-25 | `TaxSystem.CorporateTaxRate` is a field, not property | `Finance/TaxSystem.cs` | 5m | **NONE** — Convention | Open |
| TD-26 | `DiseaseLODSystem` high-detail tier methods return empty lists | `Health/DiseaseLODSystem.cs` | 2d | **MEDIUM** — Feature incomplete | Open |
| TD-27 | `UpdateBaseSystemPrevalence()` is a no-op | `Health/IntegratedDiseaseSystem.cs` | 2h | **MEDIUM** — Stale prevalence data | Open |
| TD-28 | No event unsubscription / IDisposable | `Core/SimulationEngine.cs` | 4h | **LOW** — Lifetime coupling | Open |
| TD-29 | `DateTime.Now` for performance timing (15ms resolution) | `Health/IntegratedDiseaseSystem.cs` | 30m | **NONE** — Inaccurate metrics | Open |
| TD-30 | No `<Nullable>enable</Nullable>` | Project-wide | 1d | **LOW** — NRE risks | Open |

---

## Risk Assessment Matrix

```
            LOW EFFORT          HIGH EFFORT
          ┌──────────────────┬──────────────────┐
HIGH RISK │ TD-02, TD-03,    │ TD-01, TD-04     │
          │ TD-06, TD-07     │                  │
          │ → FIX NOW        │ → FIX THIS WEEK  │
          ├──────────────────┼──────────────────┤
MED RISK  │ TD-14, TD-15,    │ TD-05, TD-08,    │
          │ TD-16            │ TD-09, TD-26,    │
          │ → FIX THIS MONTH │ TD-27            │
          │                  │ → PLAN FOR EA    │
          ├──────────────────┼──────────────────┤
LOW RISK  │ TD-10, TD-11,    │ TD-17, TD-18,    │
          │ TD-12, TD-22,    │ TD-19, TD-20,    │
          │ TD-23, TD-25,    │ TD-21, TD-28,    │
          │ TD-29            │ TD-30            │
          │ → WHEN CONVENIENT│ → POST-EA SPRINTS│
          └──────────────────┴──────────────────┘
```

---

## Estimated Total Effort

| Category | Items | Estimated Effort |
|----------|-------|-----------------|
| Critical (🔴) | 7 | **~3.5 days** |
| Moderate (🟡) | 9 | **~5 days** |
| Low-Priority (🟢) | 14 | **~12 days** |
| **Total** | **30** | **~20.5 days** |

---

## Tracking Notes

- **Created:** T74 Code Review, 2026-03-15
- **Review Scope:** `src/LakskongenSim/` (core simulation), tests, benchmark
- **Methodology:** Manual code reading, pattern analysis, no automated static analysis tools run
- **False Positive Rate:** Conservative — all items verified in source code
- **Next Review:** Recommended after Phase 1 fixes are merged
