# T66: EmpirePanel.gd — Empire Overview UI Panel

**Status:** ✅ Complete  
**File Created:** `godot/scripts/ui/EmpirePanel.gd`  
**Lines:** ~900 lines  
**Date:** 2026-03-19

## Summary

Created a comprehensive Empire Overview UI panel that displays the player's empire status, combining data from multiple game systems:

- **CompetitorAI** — Competitor leaderboard with market share
- **EmpireProgression** — Phase tracking and growth strategy
- **StockMarket** — IPO eligibility and stock performance
- **BrandReputation** — Brand score and certifications
- **VerticalIntegration** — Ownership chain visualization
- **CrisisCapitalism** — Active crises and acquisition opportunities

## Features Implemented

### 1. Empire Phase Banner
- Current phase display (SmallFarmer → IndustryMonopolist)
- Progress bar to next phase
- Growth strategy indicator
- Phase-based color coding

### 2. Competitor Leaderboard
- Top 10 competitors with player rank highlighted
- Columns: Rank, Name, Revenue, Market Share, Strategy
- "Market Leader" crown badge for #1
- Auto-sorted by revenue
- Player row highlighted in gold

### 3. Brand & Reputation
- Overall brand score (0-100) with color coding
- Breakdown bars: Quality, Sustainability, Reliability, Innovation
- Active certifications list (ASC, GlobalGAP, etc.)
- Pricing power multiplier display

### 4. Stock Market Status
- **Private company:** IPO eligibility check + launch button
- **Public company:**
  - Share price with daily change (▲/▼)
  - Market capitalization
  - Ownership percentage with risk warnings
  - Status badge (OSE listing)

### 5. Vertical Integration Tracker
- Visual chain: 🧬 → 🥚 → 🌾 → 🐟 → 🏭 → ❄️ → 🚚
- Each tier shows ✓ (owned) or ✗ (not owned)
- Integration score percentage
- Detailed list of owned tiers

### 6. Crisis & Opportunities
- Active crises list (⚠️ with red warnings)
- Acquisition opportunities with "Kjøp" buttons
- Price display and descriptions
- Empty state messages

## Design Patterns Followed

✅ **Extends PanelContainer** (standard panel pattern)  
✅ **@onready var** for UI node references  
✅ **func _ready()** with SimBridge connection  
✅ **signal state_updated** subscription  
✅ **_refresh_from_bridge()** pattern  
✅ **_input(event)** for ESC key handling  
✅ **_create_ui()** dynamic UI generation  
✅ **Norwegian tooltips** for accessibility  
✅ **RichTextLabel** and formatted sections  
✅ **Color scheme matching** existing panels  
✅ **ScrollContainer** for vertical scrolling  

## Safe Fallback System

All SimBridge calls use **safe getter methods** that provide placeholder data if the C# methods don't exist yet:

- `_get_empire_phase_safe()` → Default: SmallFarmer phase
- `_get_revenue_safe()` → Default: 10M NOK
- `_get_competitors_safe()` → Default: 3 placeholder competitors
- `_get_brand_data_safe()` → Default: 55/100 brand score
- `_get_stock_data_safe()` → Default: Private company
- `_get_integration_data_safe()` → Default: Only sea_farm owned
- `_get_crises_safe()` → Default: Empty array
- `_get_opportunities_safe()` → Default: 2 placeholder opportunities

This allows the UI to display **realistic mock data** until the C# backend methods are implemented.

## Signals Emitted

```gdscript
signal ipo_launched()
signal acquisition_purchased(opportunity_id: String)
```

These can be connected by GameController or other systems to trigger IPO flows or acquisition logic.

## Norwegian Localization

All UI text uses Norwegian labels:

- "Empire Oversikt" (Empire Overview)
- "Konkurrent Leaderboard" (Competitor Leaderboard)
- "Merkevare & Omdømme" (Brand & Reputation)
- "Aksjemarked" (Stock Market)
- "Vertikal Integrasjon" (Vertical Integration)
- "Kriser & Muligheter" (Crises & Opportunities)

Tooltips provide additional context in Norwegian.

## Integration Notes

The panel is **ready to use** once the following C# SimBridge methods are implemented:

1. `get_empire_phase()` → Returns `{phase: String, strategy: String}`
2. `get_revenue()` → Returns `float` (annual revenue)
3. `get_competitors()` → Returns `Array<Dictionary>` with competitor data
4. `get_brand_reputation()` → Returns `Dictionary` with brand metrics
5. `get_stock_market_status()` → Returns `Dictionary` with stock data
6. `get_vertical_integration()` → Returns `Dictionary` with owned tiers
7. `get_active_crises()` → Returns `Array<Dictionary>` with crisis events
8. `get_acquisition_opportunities()` → Returns `Array<Dictionary>` with buyable assets

Until then, the panel displays **placeholder data** that looks realistic for testing.

## UI Layout Structure

```
EmpirePanel (PanelContainer)
└── MarginContainer
    └── ScrollContainer
        └── VBoxContainer
            ├── Header ("👑 Empire Oversikt")
            ├── HSeparator
            ├── Empire Phase Banner
            │   ├── phase_name_label
            │   ├── phase_progress_bar
            │   └── growth_strategy_label
            ├── HSeparator
            ├── Competitor Leaderboard
            │   └── competitor_leaderboard_container (VBoxContainer)
            ├── HSeparator
            ├── Brand & Reputation
            │   └── brand_reputation_container (VBoxContainer)
            ├── HSeparator
            ├── Stock Market
            │   └── stock_market_container (VBoxContainer)
            ├── HSeparator
            ├── Vertical Integration
            │   └── vertical_integration_container (VBoxContainer)
            ├── HSeparator
            └── Crisis & Opportunities
                └── crisis_opportunities_container (VBoxContainer)
```

## Next Steps (for other agents)

1. **Add SimBridge C# methods** to provide real data
2. **Wire IPO flow** — connect `ipo_launched` signal to StockMarket system
3. **Wire acquisition flow** — connect `acquisition_purchased` signal to CrisisCapitalism/EmpireProgression
4. **Add scene file** — Create `EmpirePanel.tscn` in Godot editor with proper layout
5. **Link to Dashboard** — Add button in Dashboard.gd to open EmpirePanel

## Testing Recommendations

1. Open the panel in Godot editor
2. Run the game and check placeholder data renders correctly
3. Test ESC key to close panel
4. Verify scroll behavior with different window sizes
5. Test IPO button and acquisition buttons (should print to console)
6. Verify all tooltips display correctly
7. Check color-coded elements (brand score, stock changes, etc.)

## File Size & Performance

- **Lines of code:** ~900
- **File size:** ~39 KB
- **UI complexity:** High (6 major sections, dynamic lists)
- **Performance:** Lightweight — only refreshes on `state_updated` signal
- **Memory:** Efficient — clears and rebuilds containers on refresh

---

**Created by:** T66 Subagent  
**Project:** Lakskongen Salmon Farming Simulator  
**Engine:** Godot 4 + C#  
**Language:** GDScript 2.0
