# WAVE 4 QC REPORT - Lakskongen Salmon-Sim

**Author:** Lars Pettersen, Code Reviewer / QC Lead
**Date:** March 11, 2026

## 1. Critical Issues (Must fix before playtest)
- **Null Reference Vulnerabilities:** 
  - `LogisticsManager.cs` (Line 43): `ChooseVehicleForLoad` can return null if no vehicle meets criteria, causing an unhandled `InvalidOperationException` that crashes the transport loop.
  - `Market.gd` (Line 87/100): Dictionary accesses using `prod.has("margin_multiplier")` but misses fallback validation if `product_type` itself is missing in `MarketData`.
  - `SaveManager.cs` (Line 148): Missing try-catch for `JsonSerializer.Deserialize<List<CohortSnapshot>>`. Corrupted save files will crash the application rather than returning a clear error or attempting recovery.

## 2. High Priority (Fix soon)
- **Hardcoded Paths:** `AudioManager.cs` references absolute or hardcoded resource paths (`res://godot/ui/icons/warning.png`). Missing assets will throw exceptions on load.
- **Incomplete Implementations (TODOs):**
  - `Environment.gd` / `Zone.cs`: "implement water quality dynamics, currents, eutrophication" - core environmental factors are currently static placeholders.
  - `Disease.gd`: "integrate with DiseaseSystem to increase prevalence, spawn choices, timed decisions" - disease system is not yet interacting with storyteller events correctly.
  - `FeedSystem.cs` (Line 17): Missing Stage-specific FCRs. The feed formula only considers bulk biomass without stage variance.
- **Godot UI Binding Warnings:** In `Dashboard.gd`, calculations rely on placeholders (`# TODO: Replace with actual farm data from game_state when available`) and fake strings. 

## 3. Medium Priority (Polish)
- **Inconsistent Naming Conventions:** C# and GDScript overlap in some data model variable namings (e.g. `weight_kg` vs `MeanWeightKg`). Unify data terminology to prevent serialization mismatches across the Bridge.
- **Storyteller Event Logic:** `Storyteller.cs` lacks cooldowns for some critical events, potentially spamming negative events like "Algae Bloom" too closely together.
- **Hatchery Timing:** `Hatchery.cs` (Line 20) TODO: "Add rearing costs and time to mature in hatchery." Smolts spawn instantly without realistic incubation duration or cost tracking.

## 4. Low Priority (Nice to have)
- **Quantitative Genetics:** `Genetics.cs` (Line 13) TODO: "implement quantitative genetics." The current Mendelian model works for basic gameplay but should be deepened for the breeding minigame.
- **Extensible UI:** The `MarketPanel` UI is tightly bound to 52-week curves and doesn't handle multi-year historical charting yet.

## 5. Test Coverage Gaps
- **Market & Contracts:** `MarketSimulation.cs` and `ContractSystem.cs` have no dedicated unit tests. We must test contract fulfillment penalties and market shock generation.
- **Storyteller Events:** No coverage for `Storyteller.cs` weighted random picking and conditional checks.
- **C# to Godot Bridge:** Missing integration tests verifying that `GameBridge.cs` correctly marshals `GameState` updates into Godot UI nodes.

## 6. Architecture Recommendations
- **Avoid Tight Coupling via Singletons:** Godot scripts (`TutorialManager.gd`, `GameManager.gd`) use `Engine.get_singleton(...)` heavily. We should use dependency injection or the `EventBus` consistently to allow mocked testing.
- **Data Model Duplication:** `data/Fish.gd` and `Biology/FishPopulation.cs` maintain parallel logic for growth and mortality. We should keep biological truth entirely in C# and only send presentation data to GDScript, or risk desyncs.