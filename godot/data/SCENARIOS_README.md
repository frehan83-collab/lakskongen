# Lakskongen Scenario System

## Overview
The scenario system provides 5 distinct starting conditions and goals to give players different challenges and replayability.

## Scenarios

### 1. 🟢 Small Family Farm (Easy)
**Best for:** First-time players, learning the game
- **Starting:** 1 site, 2 pens, 10k smolt, 5M NOK
- **Goal:** Reach 50M NOK revenue in 5 years
- **Challenge:** Limited capital, must grow carefully without overextending
- **Focus:** Basic farm management, sustainable growth
- **Tutorial:** Recommended

### 2. 🟡 Industrial Giant (Medium)
**Best for:** Players who want to manage large-scale operations
- **Starting:** 3 sites, 10 pens, 100k smolt, 50M NOK
- **Goal:** Go public (IPO) within 3 years
- **Challenge:** Scale management, disease control across multiple sites
- **Focus:** Financial strategy, market timing, corporate growth
- **Skills tested:** Multi-site coordination, disease prevention, financial planning

### 3. 🔴 Organic Specialist (Hard)
**Best for:** Players who enjoy sustainability challenges
- **Starting:** 1 site, 3 pens, 5k smolt, 10M NOK
- **Goal:** Build brand reputation to 90+ while profitable
- **Challenge:** No chemical treatments, no antibiotics allowed
- **Focus:** Sustainability, premium pricing, natural methods
- **Restrictions:** Must use only organic/natural solutions
- **Skills tested:** Disease prevention without drugs, market positioning, cost management

### 4. 🔴 Research Station (Hard)
**Best for:** Tech-focused players who love unlocking new abilities
- **Starting:** Hatchery, 2 sites, 20k smolt, 20M NOK
- **Goal:** Unlock 20 tech tree nodes in 4 years
- **Challenge:** R&D costs, experimental methods, balancing research and profit
- **Focus:** Technology advancement, selective breeding, genetics
- **Buildings:** Starts with hatchery
- **Skills tested:** Research prioritization, experimental farming, genetics

### 5. 🟡 The Inheritance (Medium)
**Best for:** Players who want a dramatic survival challenge
- **Starting:** 2 sites, 5 pens, 50k smolt, 2M NOK, **30M NOK debt**
- **Goal:** Pay off debt AND reach profitability
- **Challenge:** Massive debt service, negative cash flow, aging infrastructure
- **Focus:** Turnaround management, cash flow optimization
- **Story:** Save your grandfather's struggling farm before the banks take it
- **Skills tested:** Crisis management, debt strategy, operational efficiency

## Goal Types

### Revenue Goal
- Track cumulative revenue
- Achievement: `total_revenue >= target`
- Example: Small Family Farm

### IPO Goal
- Go public on stock market
- Achievement: Complete IPO process
- Example: Industrial Giant

### Reputation + Profit Goal
- Build brand reputation score
- Achievement: `reputation >= target AND balance > starting_balance`
- Example: Organic Specialist

### Research Goal
- Unlock technology tree nodes
- Achievement: `UnlockedResearchIds.length >= target`
- Example: Research Station

### Debt + Profit Goal
- Pay off starting debt and become profitable
- Achievement: `debt <= 0 AND balance > 0`
- Example: The Inheritance

## File Structure

```
godot/
├── data/
│   ├── scenarios.json          # 5 scenario definitions
│   └── SCENARIOS_README.md     # This file
├── scripts/
│   ├── ScenarioManager.gd      # Core scenario logic (autoloaded)
│   └── SimBridge.gd            # GDScript ↔ C# bridge
└── ui/
    ├── ScenarioSelection.gd    # Scenario selection UI logic
    ├── ScenarioSelection.tscn  # Scenario selection scene
    ├── ScenarioGoalHUD.gd      # In-game goal progress display
    └── ScenarioGoalHUD.tscn    # HUD scene
```

## Adding New Scenarios

To add a new scenario, edit `scenarios.json`:

```json
{
  "id": "unique_scenario_id",
  "name": "Display Name",
  "difficulty": "Easy|Medium|Hard",
  "description": "One-line hook",
  "story": "Full backstory paragraph",
  "starting_balance": 5000000,
  "starting_day": 0,
  "starting_sites": [
    {
      "name": "Site Name",
      "zone_id": "fjord_west",
      "pen_count": 2,
      "smolt_per_pen": 5000
    }
  ],
  "starting_debt": 0,
  "starting_research": ["research_node_id"],
  "starting_buildings": ["hatchery", "processing_plant"],
  "goal": {
    "type": "revenue|ipo|reputation_and_profit|research|debt_and_profit",
    "target": 50000000,
    "time_limit_years": 5,
    "description": "Human-readable goal"
  },
  "restrictions": ["no_chemicals", "no_antibiotics"],
  "tutorial_recommended": false,
  "tags": ["tag1", "tag2"]
}
```

## Integration with C# GameState

See `C#_INTEGRATION_REQUIRED.md` for implementation details.

The C# `GameBridge` must implement:
- `ApplyScenario(string scenarioJson)` - Initialize game state
- `GetGameStateForGoalTracking()` - Return state snapshot for progress tracking

## Testing Checklist

- [ ] All 5 scenarios appear in selection UI
- [ ] Selecting a scenario shows correct details (name, difficulty, story, goal, conditions)
- [ ] "Start Scenario" button launches game with correct starting state
- [ ] Tutorial auto-starts for "Small Family Farm" on first launch
- [ ] Goal progress HUD displays correctly in-game
- [ ] Goal progress updates as player advances
- [ ] Victory/failure conditions trigger correctly
- [ ] Time limits work (fail when exceeded)
- [ ] Each goal type (revenue, IPO, reputation, research, debt) tracks correctly

## UI/UX Flow

1. **Game Start** → Scenario Selection screen appears
2. **Player clicks scenario** → Detail panel shows on right
3. **Player clicks "Start Scenario"** → ScenarioManager applies scenario via C#
4. **Game loads** → Goal HUD appears in top-right
5. **Player plays** → Progress bar updates periodically
6. **Goal achieved/failed** → Victory/failure popup appears

## Future Enhancements

- [ ] Add scenario difficulty modifiers (e.g., "1.5x disease risk")
- [ ] Add scenario achievements/badges
- [ ] Add scenario leaderboards (fastest completion times)
- [ ] Add scenario randomization (procedural starting conditions)
- [ ] Add scenario custom challenges (community-created)
- [ ] Add mid-game scenario switching (New Game+)
