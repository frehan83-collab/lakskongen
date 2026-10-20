# Lakskongen QC Report - Wave 4 Final Review

**Reviewer:** Lars Pettersen, Code Reviewer & QC Lead  
**Date:** 2026-03-12  
**Review Scope:** Wave 4 bug fixes (5 commits), expansion systems (7 new systems), full architecture review

---

## Executive Summary

**Overall Quality Score: 7.5/10** ⭐⭐⭐⭐⭐⭐⭐⚫⚫⚫

The Wave 4 bug fixes demonstrate **solid defensive programming practices**. Aksel Berg's commits show consistent patterns for null safety, error handling, and graceful degradation. The new expansion systems are architecturally ambitious and well-documented, but have some integration gaps that need attention before release.

### Finding Summary

| Severity | Count | Status |
|----------|-------|--------|
| 🔴 Critical | 1 | Requires immediate fix |
| 🟠 High | 3 | Should fix before release |
| 🟡 Medium | 6 | Fix in next sprint |
| 🟢 Low | 8 | Nice to have |

---

## PHASE 1: Wave 4 Bug Fixes Review

### ✅ Commit: LogisticsManager null safety (cb7df4b)
**File:** `src/LakskongenSim/Logistics/LogisticsManager.cs`

**Verdict: EXCELLENT** 👏

The fix is robust and well-thought-out:
- Null checks before accessing vehicle properties (line 21)
- Division-by-zero protection for `SpeedKmPerH` (line 27)
- Returns `(null, double.MaxValue)` instead of throwing - allows callers to handle gracefully

```csharp
// Good pattern - defensive check with continue
if (v == null) continue;
if (v.SpeedKmPerH <= 0) continue;
```

**Minor suggestion:** Consider logging when no vehicle is available for debugging.

---

### ✅ Commit: SaveLoadManager error handling (636f365)
**File:** `godot/scripts/autoload/SaveLoadManager.cs`

**Verdict: EXCELLENT** 👏👏

This is the gold standard for error handling in this codebase:

1. **Empty/null validation** (lines 91-96) - catches corrupted files early
2. **Specific JsonException handling** (lines 102-107) - distinguishes JSON errors from other failures
3. **User-friendly messages** - "Save file is corrupted (invalid JSON)" instead of stack traces
4. **TryLoadBackup() helper** (lines 112-119) - notifies users of recovery options
5. **Null-coalescing for properties** (line 191) - `saveData.SaveName ?? "Unknown"`

**Edge case fixed:** Empty save files no longer crash the game.

---

### ✅ Commit: Market.gd dictionary validation (f252f63)
**File:** `game/scripts/Market.gd`

**Verdict: GOOD** 👍

Safe dictionary access patterns implemented:
```gdscript
# Before: dict["key"] - crashes if missing
# After: dict.get("key", default) - safe
```

Key validations added:
- Size multiplier bands (min/max/mult keys)
- Contract dictionaries (volume_kg, locked_price_per_kg, weeks_remaining)
- Market entries (name, volume_share, price_multiplier)

**Note:** One edge case remains - `apply_size_multiplier()` returns `price_per_kg` unchanged if no band matches. Consider logging when fish size falls outside all bands.

---

### ✅ Commit: AudioManager constants refactor (5f96ee3)
**File:** `godot/scripts/autoload/AudioManager.cs`

**Verdict: EXCELLENT** 👏

Clean refactoring that improves maintainability:
- **Path constants extracted** (lines 26-30)
- **Centralized dictionaries** for ambient sounds and season music (lines 33-47)
- **GetValueOrDefault()** for safe lookup with fallbacks (lines 251, 259)

The documentation header is exemplary - explains the audio resource structure clearly.

---

### ✅ Commit: Dashboard safe node access (5142657)
**File:** `godot/scripts/ui/Dashboard.gd`

**Verdict: GOOD** 👍

Comprehensive graceful degradation implemented:
- `_safe_get_node()` returns null instead of crashing
- `_safe_set_text()` and `_safe_set_modulate()` helper methods
- Validation for autoload references (GameBridge, EventBus)

**Praise:** The approach of failing silently with warnings is perfect for UI code where partial functionality is better than crashes.

**Minor issue:** `_update_farm_stats()` uses `.has()` method inconsistently:
```gdscript
# Line 131: Good pattern
farm.Capacity if farm.has("Capacity") else 0
```
But the pattern could be simplified to `.get("Capacity", 0)` for consistency with Market.gd.

---

## PHASE 2: Architecture Review

### 🔴 CRITICAL: Duplicate Enum Definitions

**Files:**
- `src/LakskongenSim/Core/Enums.cs` (lines 11-17)
- `src/LakskongenSim/Data/Enums.cs` (lines 43-52)

The `LifeStage` enum is defined **twice** with **different values**:

```csharp
// Core/Enums.cs
public enum LifeStage {
    EyedEgg, Smolt, Juvenile, Adult, Harvestable
}

// Data/Enums.cs  
public enum LifeStage {
    Egg, Alevin, Fry, Parr, Smolt, Seawater, Harvest
}
```

**Impact:** Depending on which namespace is imported, code may behave differently. The `DiseaseTests.cs` uses `LakskongenSim.Core.LifeStage.Juvenile` which doesn't exist in Data namespace.

**Fix:** Consolidate to `Data/Enums.cs` (more complete/accurate) and delete `Core/Enums.cs` definition. Update all references.

---

### 🟠 HIGH: Missing FinanceManager Methods

**File:** `src/LakskongenSim/Finance/FinanceManager.cs`

`VerticalIntegrationSystem.cs` calls two methods that don't exist:
- `_financeManager.GetAvailableCapital()` (line 102)
- `_financeManager.DeductCapital()` (line 147)

Current FinanceManager only has:
- `Balance` property
- `AddRevenue()`
- `Spend()`

**Fix:** Add missing methods or refactor VerticalIntegrationSystem to use existing methods:
```csharp
// Add to FinanceManager.cs
public decimal GetAvailableCapital() => Balance;
public void DeductCapital(decimal amount) => Spend(amount);
```

---

### 🟠 HIGH: Duplicate ContractSystem Implementations

**Files:**
- `src/LakskongenSim/Market/ContractSystem.cs` (C#)
- `game/scripts/ContractSystem.gd` (GDScript)

Two completely different contract systems exist with different data models:

| Feature | C# Version | GDScript Version |
|---------|------------|------------------|
| Contract Types | Spot, Tolling, Exclusive, Forward | EXCLUSIVE_SUPPLIER, LONG_TERM_BUYER, etc. |
| Quality Grades | Standard, ASC, Premium | N/A |
| Penalty Clauses | Yes | Yes (breach events) |
| Exclusivity | No | Yes (radius_km, competitor_lockout) |

**Impact:** Unclear which system is authoritative. Risk of desync between UI and simulation.

**Recommendation:** Choose one as source of truth. If C# is the simulation and GDScript is UI, they should share data models via GameBridge.

---

### 🟡 MEDIUM: Namespace Organization Issues

The `Market` vs `Markets` namespace split is confusing:
- `src/LakskongenSim/Market/` - ContractSystem, ExportMarket, MarketSimulation
- `src/LakskongenSim/Markets/` - InternationalMarketSystem

**Recommendation:** Consolidate into single `Markets` namespace.

---

## PHASE 3: Code Quality Scan

### TODO/FIXME Comments (14 found)

| Priority | File | Line | Comment |
|----------|------|------|---------|
| 🟡 | FishPopulation.cs | 62 | Replace thresholds with tunable parameters |
| 🟡 | Genetics.cs | 13 | Implement quantitative genetics |
| 🟡 | Zone.cs | 21 | Implement water quality dynamics |
| 🟡 | Storyteller.cs | 141 | Hook into Environment/Health systems |
| 🟡 | Storyteller.cs | 159 | Integrate with DiseaseSystem |
| 🟢 | DelousingSystem.cs | 21 | Modify health/sea lice model |
| 🟢 | FeedSystem.cs | 17 | Add FCR and stage-specific consumption |
| 🟢 | Hatchery.cs | 20 | Add rearing costs and maturation time |
| 🟢 | Dashboard.gd | 127 | Replace with actual farm data |
| 🟢 | Dashboard.gd | 146 | Calculate actual production stats |
| 🟢 | Dashboard.gd | 159 | Calculate actual health stats |
| 🟢 | Dashboard.gd | 204 | Get actual market data |
| 🟢 | HUD.gd | 75 | Add farm values, equipment values |

**Observation:** The Dashboard TODOs are blocking full UI functionality. Consider creating a GameState bridge for KPI data.

---

### Magic Numbers Found

| File | Line | Value | Recommendation |
|------|------|-------|----------------|
| AcquisitionSystem.cs | 25 | `170300f` | ✅ Already has constant `MAB_VALUE_PER_TONNE` |
| VerticalIntegrationSystem.cs | 28 | `0.20f` | ✅ Has constant `FULLY_INTEGRATED_BONUS` |
| InternationalMarketSystem.cs | 46 | `71.44f` | Extract to `BASELINE_SALMON_PRICE_NOK_PER_KG` |
| CrisisCapitalismSystem.cs | 285 | `500_000, 2_000_000` | Consider extracting to config |
| StakeholderFactionSystem.cs | 84 | `0.9f, 0.95f` | ✅ Reasonable inline for influence scores |

---

### Documentation Quality

**Exemplary Documentation:**
- `AudioManager.cs` - Excellent header explaining resource structure
- `SaveLoadManager.cs` - Good XML doc comments
- `InternationalMarketSystem.cs` - Clear data model documentation
- `StakeholderFactionSystem.cs` - Comprehensive enum documentation

**Missing Documentation:**
- `VerticalIntegrationSystem.cs` - No class-level summary
- Most GDScript files - Minimal docstrings

---

## PHASE 4: Integration Review

### New Expansion Systems Assessment

| System | Code Quality | Integration | Test Coverage |
|--------|--------------|-------------|---------------|
| AcquisitionSystem | ⭐⭐⭐⭐ | 🟡 Needs events | ❌ None |
| VerticalIntegrationSystem | ⭐⭐⭐⭐ | 🔴 Missing FinanceManager methods | ❌ None |
| CrisisCapitalismSystem | ⭐⭐⭐⭐⭐ | ✅ Self-contained | ❌ None |
| InternationalMarketSystem | ⭐⭐⭐⭐ | ✅ Good events | ❌ None |
| StakeholderFactionSystem | ⭐⭐⭐⭐⭐ | ✅ Good events | ❌ None |
| ContractSystem (GD) | ⭐⭐⭐⭐ | 🟡 No bridge to C# | ❌ None |
| Competitor AI | ❓ Not found | N/A | N/A |

**🟠 HIGH: Missing Test Coverage**

All 7 expansion systems have **zero unit tests**. Compare to existing test coverage:
- FinanceTests.cs - 5 tests
- DiseaseTests.cs - 2 tests  
- ProcessingTests.cs - 3 tests

**Priority tests needed:**
1. AcquisitionSystem.MakeOffer() - contract acceptance logic
2. InternationalMarketSystem.EnterMarket() - certification validation
3. StakeholderFactionSystem reputation calculation

---

### Data Model Conflicts

| Conflict | Files | Issue |
|----------|-------|-------|
| LifeStage enum | Core vs Data | Duplicate definition (CRITICAL) |
| QualityGrade | ContractSystem.cs vs Market.gd | Different naming (Standard vs SUPERIOR) |
| Contract model | C# vs GDScript | Completely different structures |

---

## PHASE 5: Performance & Security Review

### ✅ Good Practices Observed

1. **Exception Handling** - SaveLoadManager wraps JSON parsing in try/catch
2. **Resource Cleanup** - AudioManager properly stops players in `_ExitTree()`
3. **Collection Iteration Safety** - CrisisCapitalismSystem iterates backwards when removing items (line 365)

### 🟡 MEDIUM: Potential Performance Issues

| File | Issue | Impact |
|------|-------|--------|
| StakeholderFactionSystem.cs:223 | `_factions.Values.Sum()` called twice in `GetOverallReputation()` | Minor - 6 factions max |
| Dashboard.gd:215 | `_update_all_kpis()` every 7 days but updates 4 sections | Consider throttling per-section |
| CrisisCapitalismSystem.cs:358 | `GenerateDistressedAssets()` creates up to 5 assets per crisis | Could lag if many crises trigger |

### ✅ Security Review: Save/Load System

The save/load security improvements in Wave 4 are solid:
- **JSON injection:** Mitigated - uses System.Text.Json with proper deserialization
- **Path traversal:** Mitigated - `SanitizeFileName()` removes invalid chars
- **File permissions:** Standard Godot `user://` directory isolation
- **Backup integrity:** Could add checksum validation in future

---

## Praise & Positive Feedback 🌟

### Things Done Well

1. **Defensive Programming Culture** - The Wave 4 commits show a consistent pattern of null checks, fallbacks, and graceful degradation. This is exactly what a production codebase needs.

2. **Industry-Accurate Domain Model** - The expansion systems demonstrate deep understanding of Norwegian salmon farming:
   - Accurate MAB pricing (170,300 NOK/tonne)
   - Realistic stakeholder factions (Mattilsynet, Fiskeridirektoratet)
   - Proper crisis types (ISA outbreaks, regulatory shutdowns)

3. **Event-Driven Architecture** - Systems like `StakeholderFactionSystem` and `InternationalMarketSystem` use C# events properly for loose coupling:
   ```csharp
   public event Action<FactionType, float, string> RelationshipChanged;
   ```

4. **Self-Documenting Code** - `AudioManager.cs` header is a model for how to document resource dependencies.

5. **Consistent Error Messaging** - SaveLoadManager provides user-friendly error messages while logging technical details separately.

---

## Recommended Fix Priority

### Sprint 1 (Immediate)
1. 🔴 Fix duplicate `LifeStage` enum definitions
2. 🟠 Add missing `GetAvailableCapital()` and `DeductCapital()` to FinanceManager
3. 🟠 Create baseline tests for expansion systems (at least 1 per system)

### Sprint 2
1. 🟡 Consolidate `Market`/`Markets` namespaces
2. 🟡 Resolve ContractSystem C#/GDScript duplication
3. 🟡 Extract magic number `71.44f` to constant

### Sprint 3
1. 🟢 Address TODO comments in Dashboard.gd
2. 🟢 Add XML documentation to expansion systems
3. 🟢 Implement missing Competitor AI system

---

## Conclusion

Wave 4 demonstrates strong progress in code quality and defensive programming. The bug fixes are well-crafted and the new expansion systems are architecturally sound. The main risks are the **duplicate enum definitions** (which could cause subtle bugs) and **missing test coverage** for the new systems.

With the recommended fixes, this codebase is on track for a solid beta release.

**Sign-off:** Lars Pettersen, QC Lead  
**Recommendation:** ✅ Approve for integration testing after Sprint 1 fixes

---

*Report generated: 2026-03-12 13:49 CET*
