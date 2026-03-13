# Scenario System — Implementation Summary
**Date:** 2026-03-13

## What Was Built

### `src/LakskongenSim/Data/Enums.cs` — `WinCondition` enum added

| Value | Description |
|-------|-------------|
| `Tutorial` | Complete all tutorial milestones |
| `Profit` | Reach a target NOK balance |
| `Sustainability` | Achieve sustainability score ≥ threshold |
| `Survival` | Stay solvent for N days |
| `Competition` | Outpace AI competitors to a shared profit target |

---

### `src/LakskongenSim/Core/ScenarioSystem.cs` — Full rewrite

**`ScenarioDefinition` class** (renamed from `Scenario`):
- `Id` — snake_case identifier
- `Name` — Norwegian display name
- `Description` — flavour text
- `StartingCapitalNOK` — decimal; negative for debt scenarios
- `StartingRegion` — `ProductionRegion` enum
- `Difficulty` — int 1–5
- `WinCondition` — typed enum (replaces string)
- `SpecialRules` — `Dictionary<string,string>` with named keys
- `TimeLimitDays` — nullable int
- `IsUnlocked` — bool

**`ScenarioSystem` class**:
- Constructor takes `GameState` (matching `AchievementSystem` pattern)
- `StartScenario(id)` — validates unlock, seeds starting capital and AI state
- `CheckWinConditions(sustainabilityScore, tutorialComplete)` — call once/day
- `OnScenarioWon(ScenarioDefinition)` — fires on win
- `OnScenarioLost(ScenarioDefinition, reason)` — fires on loss
- `Unlock(id)` — programmatic unlock after prerequisites
- `AiProgress` — read-only AI competitor accumulator (Competition scenario)

---

### Five Scenarios

| # | ID | Name | Capital | Region | Difficulty | Win Condition |
|---|-----|------|---------|--------|-----------|---------------|
| 1 | `forste_sesong` | Første Sesong | 500 000 NOK | Vestland | ⭐ | Tutorial |
| 2 | `lofoten_eventyr` | Lofotens Eventyr | 2 000 000 NOK | Nordland | ⭐⭐⭐ | Profit (5M NOK) |
| 3 | `laksebaronene` | Laksebaronene | 5 000 000 NOK | Trøndelag | ⭐⭐⭐⭐ | Competition (10M vs 3 AI) |
| 4 | `barekraft_mester` | Bærekraftsmester | 3 000 000 NOK | Vestland | ⭐⭐⭐ | Sustainability (score ≥ 95) |
| 5 | `krise_manager` | Krise Manager | −500 000 NOK | Finnmark | ⭐⭐⭐⭐⭐ | Survival (730 days) |

Scenarios 1 and 2 are unlocked by default; 3–5 must be unlocked via `ScenarioSystem.Unlock()`.

---

### `godot/scripts/ui/ScenarioSelectPanel.gd` — Full rewrite

- Procedural build (no scene file required), matching `TechTreePanel.gd` pattern
- Deep-ocean Norwegian colour palette (`C_BG`, `C_CARD_BASE`, etc.)
- Five cards arranged horizontally, each with:
  - Icon (lock icon for locked cards) + name + subtitle
  - Difficulty stars (★ filled = active, greyed = inactive)
  - Scenario description (auto-wrap)
  - Special rules bullet list
  - Region + starting capital (colour-coded positive/negative)
  - Win objective line
  - Start button (active) or Locked button (disabled, greyed)
- Signals: `scenario_selected(id)` and `panel_close_requested`
- Fade-out tween on scenario selection

---

## Integration Notes

To wire into the engine:

1. **Add to `SimulationEngine`**:
   ```csharp
   public ScenarioSystem Scenarios { get; private set; } = null!;
   // In Initialize():
   Scenarios = new ScenarioSystem(State);
   // In Advance():
   Scenarios.CheckWinConditions(SustainabilityManager.CurrentScore, Tutorial.IsComplete);
   ```

2. **Add to `GameBridge`**:
   ```csharp
   [Signal] public delegate void ScenarioWonEventHandler(string scenarioId, string scenarioName);
   [Signal] public delegate void ScenarioLostEventHandler(string scenarioId, string reason);
   // In StartNewGame(), after Initialize():
   SimulationEngine.Scenarios.OnScenarioWon += s =>
       EmitSignal(SignalName.ScenarioWon, s.Id, s.Name);
   SimulationEngine.Scenarios.OnScenarioLost += (s, r) =>
       EmitSignal(SignalName.ScenarioLost, s.Id, r);
   ```

3. **Modify `StartNewGame`** to accept an optional `scenarioId` and call
   `SimulationEngine.Scenarios.StartScenario(scenarioId)` when provided.
