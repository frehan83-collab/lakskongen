# Lakskongen C# Code Review Report — T74

**Reviewer:** Automated Production Audit  
**Date:** 2026-03-15  
**Codebase:** `src/LakskongenSim/` (core simulation layer)  
**Build Status:** 0 errors, 45/45 tests pass  
**Target:** Early Access launch readiness

---

## Executive Summary

The Lakskongen simulation layer is a **competent, well-structured** codebase with strong domain modeling and realistic calibration against Norwegian aquaculture data. For an Early Access indie simulation game, it is **ship-ready** — no P0 show-stoppers were found.

That said, there are several **P1 issues** that could cause subtle gameplay bugs, data corruption, or performance degradation at scale, and numerous **P2 items** that constitute technical debt for post-EA improvement.

### Issue Counts

| Priority | Count | Description |
|----------|-------|-------------|
| **P0 (Ship-blocker)** | 0 | None found |
| **P1 (Fix before/during EA)** | 12 | Correctness, data integrity, perf risks |
| **P2 (Post-EA)** | 18 | Code quality, architecture, maintainability |
| **P3 (Nice-to-have)** | 8 | Modern C# idioms, polish |

---

## P1 Issues — Fix Before/During Early Access

### P1-01: `static Random` is not thread-safe (GrowthModel)

**File:** `Biology/GrowthModel.cs:29`  
**Severity:** P1 — Can cause corrupted random sequences if ever called from multiple threads.

```csharp
private static Random _random = new Random(); // ← shared mutable state
```

`System.Random` is **not thread-safe**. If parallelization is ever introduced (or Godot calls from multiple threads), this silently corrupts the RNG state, producing duplicated or degenerate values.

**Fix:** Use `[ThreadStatic]` or `Random.Shared` (.NET 6+):
```csharp
// .NET 6+ solution:
private static readonly Random _random = Random.Shared; // thread-safe
// Or for reproducibility:
private readonly Random _random;
```

**Same issue in:** `StochasticDiseaseEngine.cs`, `IntegratedDiseaseSystem.cs`, `DiseaseLODSystem.cs`, `Zone.cs`

---

### P1-02: Non-deterministic zone distance estimation (random-based)

**File:** `IntegratedDiseaseSystem.cs:218`, `StochasticDiseaseEngine.cs:177`

```csharp
private double EstimateZoneDistance(string zoneId1, string zoneId2)
{
    return _rng.NextDouble() * 30.0; // ← random distance every call!
}
```

Zone distances return **different values every time they're called**, even for the same zone pair within the same tick. This means disease spread is non-deterministic in a way that's not just stochastic but **structurally incoherent** — the same two zones can be 2km apart in one calculation and 28km apart in the next within the same tick.

**Fix:** Cache distances or compute from actual coordinates. At minimum, use a deterministic hash:
```csharp
private double EstimateZoneDistance(string zoneId1, string zoneId2)
{
    int hash = HashCode.Combine(zoneId1, zoneId2);
    return (Math.Abs(hash) % 3000) / 100.0; // 0-30km, deterministic per pair
}
```

---

### P1-03: `new Random(day + Id.GetHashCode())` creates correlated seeds

**File:** `Environment/Zone.cs:72`

```csharp
var rng = new Random(day + Id.GetHashCode()); // deterministic per zone+day
```

Creating a new `Random` per call with a weak seed is problematic: (1) allocates a new object every day per zone (GC pressure in hot path), and (2) `GetHashCode()` for strings is randomized per-process in .NET 6+, so "deterministic" is only within a single process run, not across save/load.

**Fix:** Use a per-zone `Random` instance initialized once, or seed from a master RNG.

---

### P1-04: `FinanceManager.Spend()` silently fails on insufficient funds

**File:** `Finance/FinanceManager.cs:31`

```csharp
public bool Spend(decimal amount)
{
    if (amount <= 0) return true;
    if (Balance < amount) return false; // ← silent failure
    Balance -= amount;
    ...
}
```

Multiple callers (daily wages, feed costs, taxes) **ignore the return value**:

```csharp
// SimulationEngine.cs:387
FinanceManager.Spend(totalOpEx); // return value ignored!
// SimulationEngine.cs:396
FinanceManager.Spend(tax);       // return value ignored!
```

If the player runs out of money, operational costs silently stop being deducted — the simulation continues with no consequences for bankruptcy. This breaks the economic simulation loop.

**Fix:** Either (a) allow negative balance (debt), (b) fire a bankruptcy event, or (c) at minimum log a warning. The return value should be checked:
```csharp
if (!FinanceManager.Spend(totalOpEx))
{
    // Trigger bankruptcy/debt logic
    OnEventTriggered?.Invoke("⚠️ Insufficient funds for operations!");
}
```

---

### P1-05: Double mortality application from Storyteller events

**File:** `Events/Storyteller.cs:213-226` (ApplyBiologicalImpacts), `Events/Storyteller.cs:241-265` (ProcessDiseaseEvents)

The Storyteller events directly modify cohort counts:
```csharp
pop.Cohorts[i].Count = newCount; // Direct mutation!
```

Meanwhile, the `IntegratedDiseaseSystem` injects mortality rates into `MortalityModel`, which are applied during `FishPopulation.AdvanceDays()`. Storyteller events that trigger `ApplyDiseaseConsequence` run `IntegratedDisease.Update()` with 3 extra days:

```csharp
private void ApplyDiseaseConsequence(EventRecord record)
{
    IntegratedDisease?.Update(State.Zones.ToList(), State.Populations.ToList(), 3);
}
```

This creates **double mortality**: direct cohort killing + injected rates from the extra disease ticks.

**Fix:** Storyteller disease events should only inject rates, not directly kill fish. Or clearly document that the direct kills represent *acute* mortality and the injected rates represent *ongoing* disease pressure.

---

### P1-06: `GameState.Balance` dual source of truth

**File:** `Core/GameState.cs:12`, `Finance/FinanceManager.cs:7`

Both `GameState.Balance` and `FinanceManager.Balance` track the player's money. They're synced via event:
```csharp
FinanceManager.OnBalanceChanged += balance => State.Balance = balance;
```

But Storyteller events modify `GameState.Balance` directly:
```csharp
gs.Balance += rec.BalanceChange; // ← bypasses FinanceManager!
```

This means `FinanceManager.Balance` and `GameState.Balance` can **diverge** after any Storyteller event. Serialization saves `GameState.Balance`, but runtime financial logic uses `FinanceManager.Balance`.

**Fix:** All balance changes must go through `FinanceManager`. Events should call `FinanceManager.AddRevenue()` or `FinanceManager.Spend()` instead of modifying `GameState.Balance` directly.

---

### P1-07: `FinancialStatements.Cash` diverges from `FinanceManager.Balance`

**File:** `Finance/FinancialStatements.cs`

`FinancialStatements` tracks its own `Cash` property independently:
```csharp
public void RecordOperatingExpense(decimal amount)
{
    OperatingExpenses += amount;
    Cash -= amount; // ← separate tracking from FinanceManager
}
```

But daily finance in `SimulationEngine` calls both:
```csharp
FinanceManager.Spend(totalOpEx);                    // Deducts from FinanceManager
FinancialStatements.RecordOperatingExpense(totalOpEx); // Also deducts from FinancialStatements.Cash
```

This means there are **three independent cash tracking systems** (GameState.Balance, FinanceManager.Balance, FinancialStatements.Cash) that can diverge.

**Fix:** Consolidate to one source of truth. `FinancialStatements` should observe `FinanceManager`, not maintain its own cash.

---

### P1-08: Save/Load does not restore runtime dependencies

**File:** `Core/SaveLoadManager.cs`

The `SaveLoadManager` correctly serializes `GameState`, but after deserialization, `FishPopulation` objects lack their `_growth` and `_mortality` references:

```csharp
[JsonConstructor]
public FishPopulation() { } // ← _growth and _mortality are null!
```

If the simulation engine doesn't re-inject these after loading (via some post-load wiring), `AdvanceDays()` will throw `NullReferenceException` on the first tick.

**Investigation:** The `SimulationEngine` doesn't have a `LoadGame()` method that re-wires dependencies. This is a latent bug if save/load is used through the C# layer.

**Fix:** Add a `RestoreFromSave(GameState loaded)` method to `SimulationEngine` that re-injects `GrowthModel` and `MortalityModel` into all loaded populations.

---

### P1-09: `FishPopulation.GetIndividuals()` allocates heavily in hot path

**File:** `Biology/FishPopulation.cs:105-121`

```csharp
public List<FishIndividual> GetIndividuals()
{
    var individuals = new List<FishIndividual>();
    // Creates up to 100 objects per cohort, per call
    for (int i = 0; i < sampleSize; i++)
    {
        individuals.Add(new FishIndividual { ... });
    }
    return individuals;
}
```

This is called by `StochasticDiseaseEngine.ApplyDelayedInfection()` which is called on **every infection event** in the high-fidelity disease simulation. Each call allocates up to 100 `FishIndividual` objects × number of cohorts.

**Fix:** Pool `FishIndividual` objects or cache the list per tick.

---

### P1-10: `StochasticDiseaseEngine._individualStates` grows unboundedly

**File:** `Health/StochasticDiseaseEngine.cs:33`

```csharp
private Dictionary<FishIndividual, DiseaseState> _individualStates = new();
```

`FishIndividual` objects are regenerated every call to `GetIndividuals()` with new instances. Since `FishIndividual` doesn't override `Equals`/`GetHashCode`, the dictionary uses reference equality. Every call to `ApplyDelayedInfection` adds **new** entries without ever removing old ones, causing unbounded memory growth.

**Fix:** Either (a) implement value-based equality on `FishIndividual`, (b) clear `_individualStates` periodically, or (c) use string IDs as dictionary keys.

---

### P1-11: `WaterChemistry` ODE can produce negative concentrations

**File:** `Environment/WaterChemistry.cs:86-99`

The Euler integration of water chemistry can produce negative intermediate values before clamping:
```csharp
AmmoniaNH4 += ammoniaInput - (NitrificationRateNH4 * tempFactor * AmmoniaNH4) - (AmmoniaNH4 * waterFlowRate * 0.01);
```

With high `waterFlowRate` (e.g., 1500+ from strong currents), the term `AmmoniaNH4 * waterFlowRate * 0.01 = AmmoniaNH4 * 15` can massively overdrain, producing a negative value that gets clamped at line 105 but only after being used in downstream calculations (NitriteNO2 depends on AmmoniaNH4).

**Fix:** Clamp each variable immediately after update, or use a semi-implicit integration scheme.

---

### P1-12: `MarketSimulation.LongTermMean` is clamped to 70-80 but initial/default price is 100

**File:** `Market/MarketSimulation.cs:135`

```csharp
LongTermMean = ClampToBand(mean, 70m, 80m); // Calibrated clamp
```

But defaults are:
```csharp
private const decimal DefaultLongTermMean = 100m;
private const decimal DefaultInitialPrice = 100m;
```

If calibration runs and sets `LongTermMean` to 75, but `BasePricePerKg` was already 100, the O-U process will aggressively mean-revert downward. If calibration *doesn't* run (no data file), the long-term mean stays at 100 — inside the 50-150 bounds but above the calibration clamp. This inconsistency can cause jarring price behavior depending on whether the data file is present.

**Fix:** Align default price with the calibration band, or widen the calibration clamp.

---

## P2 Issues — Post-EA Technical Debt

### P2-01: SimulationEngine is a God Class (~500 lines)

`SimulationEngine.cs` at ~500 lines orchestrates 20+ subsystems, handles configuration loading, event routing, and daily simulation logic. It violates SRP heavily.

**Recommendation:** Extract `ConfigurationLoader`, `EventRouter`, and `DailyTickProcessor` classes.

### P2-02: Duplicate LifeStage switch statements (6 occurrences)

The same `LifeStage → multiplier` switch appears in:
- `GrowthModel.ApplyGrowth()`
- `GrowthModel.ApplySDEGrowthOneDay()`
- `GrowthModel.ApplySDEGrowthMultipleDays()`
- `MortalityModel.ApplyMortality()`
- `FeedSystem.CalculateFCR()`
- `FishPopulation.DetermineStage()`

**Fix:** Extract a `LifeStageProfile` record/class with multipliers for growth, mortality, FCR, etc.

### P2-03: Magic numbers throughout

Examples:
- `5.5` (target weight in SimulationEngine)
- `3.0` (genetic max growth rate α_max)
- `0.02` (individual variance)
- `10_000.0` (stocking density divisor)
- `0.05`, `0.02` (waste coefficients in WaterChemistry)
- `3.5` (harvest cost per kg in tests)
- `50_000m` (compliance fine)

These should be in `GameConstants` or `BalancingConfig`.

### P2-04: No dependency injection — all `new` in SimulationEngine

All subsystems are created with `new` in `SimulationEngine.Initialize()`. This makes testing individual subsystems with mocks impossible without the full engine.

### P2-05: Console.WriteLine used as logging

~40 instances of `Console.WriteLine` used for logging. Should use a proper logging abstraction (even a simple `ILogger` interface) to support log levels, filtering, and redirecting output.

### P2-06: Duplicate `Enums.cs` files in Core and Data namespaces

`Core/Enums.cs` is empty (comments only) but still exists alongside `Data/Enums.cs`. Delete `Core/Enums.cs`.

### P2-07: Duplicate `GameConstants.cs` in Core and Data

`Core/GameConstants.cs` has all the real constants. `Data/GameConstants.cs` only has a version string. Consolidate.

### P2-08: `BalancingConfig` is a private nested class

The 60-field `BalancingConfig` class is nested inside `SimulationEngine`. It should be a standalone class in `Core/` or `Data/` for reuse and testability.

### P2-09: `TaxSystem` class has a public field instead of property

```csharp
public decimal CorporateTaxRate = 0.22m; // ← field, not property
```

### P2-10: `FishPopulation.Cohort` should be a record

`Cohort` is a mutable class with only data properties. In modern C# it's a natural fit for a record (or at least a record class for mutability).

### P2-11: `GetAvailableCapital()` returns `float`, losing precision

```csharp
public float GetAvailableCapital() => (float)Balance; // decimal → float!
```

For a 10M NOK balance, `float` loses precision at ~1 NOK granularity. Use `decimal` consistently.

### P2-12: No IDisposable implementations despite event subscriptions

`SimulationEngine` subscribes to numerous events (FinanceManager, Hatchery, Feed, Research, etc.) but never unsubscribes. This creates implicit object lifetime coupling and prevents GC of unused subsystems.

### P2-13: `FeedSystem.Feed()` uses LINQ in hot path

```csharp
double biomassKg = pop.Cohorts.Sum(c => c.Count * c.MeanWeightKg);
var dominantStage = pop.Cohorts
    .OrderByDescending(c => c.Count)
    .Select(c => c.Stage)
    .FirstOrDefault();
```

LINQ allocates delegates and enumerators. The main loop already optimized biology with indexed loops (T56) but feed still uses LINQ.

### P2-14: Events list grows unboundedly in `FinancialStatements`

```csharp
public List<string> Events { get; } = new List<string>();
```

Every financial transaction appends a string to this list forever. Over a multi-year simulation, this can accumulate thousands of entries consuming memory.

### P2-15: `DiseaseLODSystem` high-detail tier returns empty lists

Multiple methods return `new List<>()`:
```csharp
private List<FishIndividual> GetInfectedIndividuals(...) => new List<FishIndividual>();
private List<FishIndividual> GetAllIndividuals(...) => new List<FishIndividual>();
private List<Zone> GetZonesForFish(...) => new List<Zone>();
```

This means the high-detail tier effectively does nothing useful — it calls `SimulateMultiVectorTransmission` with empty lists.

### P2-16: `UpdateBaseSystemPrevalence` is a no-op

```csharp
private void UpdateBaseSystemPrevalence(FishPopulation population, double prevalence)
{
    // This would update the internal state of _baseSystem
    // For now, just a placeholder
}
```

The medium/low-fidelity disease tiers calculate new prevalence values but **never write them back** to the base system. Prevalence queries from `_baseSystem.GetPrevalence()` return stale data.

### P2-17: `MarketSimulation` file path uses `AppContext.BaseDirectory` with hardcoded relative path

```csharp
private static readonly string HistoricalPriceMarkdownPath = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "research", "real-salmon-prices.md"));
```

This assumes a specific build directory structure and will fail in deployed builds.

### P2-18: `IntegratedDiseaseSystem` uses `DateTime.Now` for performance timing

```csharp
var startTime = DateTime.Now;
// ...
var endTime = DateTime.Now;
double updateTimeMs = (endTime - startTime).TotalMilliseconds;
```

`DateTime.Now` has ~15ms resolution on Windows. Use `Stopwatch` (already used elsewhere in the codebase).

---

## P3 Issues — Nice-to-Have

### P3-01: No nullable reference type annotations
The project doesn't enable `<Nullable>enable</Nullable>`. Several `null!` suppressions suggest this was considered. Enabling NRT would catch ~10 potential null dereferences.

### P3-02: No use of `Span<T>` or `Memory<T>`
Some string operations (CSV parsing in historical prices, JSON path resolution) could benefit from span-based parsing.

### P3-03: `ValueTask` not used for synchronous methods
All operations are synchronous — no async/await usage. This is fine for a game simulation but limits future async I/O integration.

### P3-04: No C# records used
`Genome`, `EventRecord`, `DailySnapshot`, `MarketStatus`, etc. are natural record candidates.

### P3-05: Pattern matching underutilized
Multiple `switch` blocks on `LifeStage` could use switch expressions for conciseness.

### P3-06: No XML doc on many public APIs
Some classes have good XML docs (Genetics, SimulationEngine), others have none (Zone constructors, many properties).

### P3-07: `struct` used for status types but with mutable fields
`MarketStatus`, `WaterChemistryStatus`, etc. are mutable structs. These should be `readonly struct` to prevent accidental mutation.

### P3-08: No `sealed` on leaf classes
Performance improvement opportunity — sealing classes allows devirtualization.

---

## Architecture & Design Assessment

### Strengths
- **Clear domain modeling**: Biology, Health, Finance, Market are well-separated
- **Event-driven architecture**: OnPriceChanged, OnBalanceChanged, etc. enable loose coupling
- **Realistic calibration**: O-U process, SIR model, Norwegian industry data
- **Performance awareness**: T56 optimization (indexed loops, manual sums) in hot paths
- **LOD system for disease**: Smart tiered simulation approach

### Weaknesses
- **God class**: SimulationEngine orchestrates everything
- **No DI container**: All construction via `new` makes testing harder
- **Three financial tracking systems**: GameState.Balance, FinanceManager.Balance, FinancialStatements.Cash
- **Placeholder implementations**: Several LOD system methods return empty lists

### SOLID Compliance
- **S** (SRP): SimulationEngine violates; most domain classes are good
- **O** (Open/Closed): Events system is extensible; growth model is not
- **L** (LSP): N/A — limited inheritance hierarchy
- **I** (ISP): No interfaces used at all
- **D** (DIP): Everything depends on concrete classes, no abstractions

---

## Security Assessment

**Risk Level: LOW** — This is a single-player game simulation, not a networked service.

- **Serialization**: Uses `System.Text.Json` (safe, no type-name handling vulnerabilities)
- **File I/O**: Save files are read from user-controlled paths — acceptable for a game
- **No SQL, no network, no user input parsing**: Minimal attack surface
- **RNG quality**: Uses `System.Random` (fine for game simulation, not crypto)
- **One concern**: `BarentsWatchClientOptions.BearerToken` read from environment variable — ensure this is never serialized to save files (it isn't, since it's not in GameState)

---

## Performance Assessment

### Hot Path Analysis (AdvanceOneDay)

The main simulation loop is well-optimized after T56:
- Indexed loops instead of LINQ for population processing ✅
- Manual biomass summation ✅
- In-place cohort updates ✅

### Remaining Performance Concerns

1. **FeedSystem** still uses LINQ delegates (P2-13)
2. **`GetIndividuals()`** allocates heavily per call (P1-09)
3. **`_individualStates`** dictionary grows unboundedly (P1-10)
4. **`Zone.UpdateCurrents()`** creates new `Random` per call per zone per day (P1-03)
5. **`_priceHistory`** Queue in MarketSimulation — capped at 365, acceptable
6. **`DiseaseLODSystem.LogPerformanceMetrics()`** writes to Console every tick — allocates formatted strings

### Memory Profile (Estimated)

- GameState: ~O(populations × cohorts) — reasonable
- StochasticDiseaseEngine._individualStates: **unbounded growth** (P1-10)
- FinancialStatements.Events: **unbounded growth** (P2-14)
- Price history: bounded (365 entries) ✅
- DailySnapshots: **unbounded growth** — one per simulated day forever

---

## Test Coverage Assessment

### Covered
- Financial statements basic flow ✅
- Loan interest calculation ✅
- Investment depreciation ✅
- Insurance premium ✅
- Investor equity ✅
- Full production cycle (smolt → harvest → revenue) ✅
- Fish growth realistic bounds ✅
- Market price bounds ✅
- Finance P&L tracking ✅
- Multi-zone independence ✅

### Gaps
- **No disease system tests** in the main test suite (disease tests exist in separate `tests/` folder but may not be in test project)
- **No save/load round-trip test** — critical for data integrity
- **No water chemistry tests** — ODE behavior untested
- **No genetics breeding test** — mutation, heritability untested
- **No negative balance / bankruptcy test**
- **No event system test** — Storyteller event triggering untested
- **No LOD system performance test**
- **No feed system FCR calculation test**
- **No sustainability score calculation test**

---

## Summary Recommendations

### Before EA Launch (P1, 1-2 weeks)
1. Fix the triple-cash-tracking system (P1-06, P1-07) — pick one source of truth
2. Fix `Spend()` silent failure (P1-04) — add bankruptcy logic
3. Fix Storyteller direct balance modification (P1-06)
4. Add save/load round-trip test
5. Switch to `Random.Shared` or proper per-instance RNG (P1-01)

### During EA (P1/P2, ongoing)
6. Fix `_individualStates` memory leak (P1-10)
7. Fix zone distance randomness (P1-02)
8. Add disease system and water chemistry tests
9. Extract magic numbers to config (P2-03)

### Post-EA (P2/P3, quarterly)
10. Refactor SimulationEngine into smaller classes
11. Introduce interfaces for testability
12. Enable nullable reference types
13. Implement `DiseaseLODSystem` high-detail tier properly
