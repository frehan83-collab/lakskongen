# Encyclopedia Browser & Norwegian Coast Map Panel
**Date:** 2026-03-13
**Author:** Ingrid Lunde (UI/UX)
**Sprint:** Evening session — Knowledge & Navigation systems

---

## Summary

Two new overlay panels delivered: an in-game encyclopedia browser covering all major salmon farming knowledge domains, and an interactive Norwegian coastline map for selecting sea site regions.

---

## EncyclopediaPanel.gd

**Location:** `godot/scripts/ui/EncyclopediaPanel.gd`

### Architecture
- Full-screen overlay (`Control`, anchored FULL_RECT) with animated fade in/out
- Three-column layout: category sidebar → entry list → content area
- `HSplitContainer` with fixed 520px left split; content scrolls independently
- Emits `close_requested` signal; hides on Escape key

### Categories & Entry Count
| Category | Entries |
|---|---|
| Fish Species | 4 (Atlantic Salmon, Lifecycle, Smoltification, Parr) |
| Diseases | 5 (ISA, PD, IHN, BKD, Sea Lice) |
| Equipment | 4 (Sea Pens, Feeding Systems, Cameras, Freshwater RAS) |
| Regulations | 3 (MAB/MTB, Traffic Light System, Escape Reporting) |
| Feed | 3 (Feed Types, Ingredients & Sustainability, FCR) |
| Markets | 3 (Nasdaq Salmon Index, Contract Types, Seasonal Patterns) |
| **Total** | **22 entries** |

### Content per Entry
- Multi-paragraph description (2–7 paragraphs, factually detailed)
- Emoji icon placeholder
- "See Also" related entry links (cross-category navigation supported)
- Category colour badge on sidebar (distinct colour per category)

### Search
- Live `LineEdit` filter — searches entry titles, updates entry list in real time
- Auto-selects first matching entry on each keystroke

### UI Style
- Norwegian ocean theme: deep navy backgrounds, cyan text accents
- Per-category accent colours: green (Fish), red (Diseases), blue (Equipment), gold (Regulations), lime (Feed), purple (Markets)
- Selected entry highlighted with left border + background tint
- Hover states on all interactive elements

---

## MapPanel.gd

**Location:** `godot/scripts/ui/MapPanel.gd`

### Architecture
- Full-screen overlay with animated fade
- Two-pane layout: interactive map (left, expandable) + detail panel (right, 340px fixed)
- Emits `region_selected(region_id: String)` on confirm and `close_requested` on close/escape
- Inner classes: `_CoastlineDrawer` (custom draw), `_RegionMarker` (interactive markers), `_TempBar` (temperature range visualisation)

### Regions (North → South)
| Region | Zone | Licences | Temp Range | Disease Risk |
|---|---|---|---|---|
| Finnmark | PO13 | 18 | 3–10 °C | Low |
| Troms | PO12 | 24 | 4–12 °C | Low |
| Nordland | PO11 | 52 | 5–14 °C | Low-Medium |
| Trøndelag | PO9-10 | 38 | 6–16 °C | Medium |
| Møre og Romsdal | PO8 | 31 | 7–17 °C | Medium-High |
| Vestland | PO3-5 | 44 | 8–18 °C | High |
| Rogaland | PO2 | 22 | 8–19 °C | High |
| Agder | PO1 | 8 | 9–20 °C | High |

### Map Drawing (`_CoastlineDrawer`)
- Custom `_draw()` paints a simplified Norway west-coast outline as coloured polygon
- Fjord indentations rendered as short blue lines from the coastline
- Latitude grid lines at 12.5% intervals
- Region markers positioned using normalised `Vector2` coordinates mapped to container size at runtime (via `call_deferred("_build_region_markers")`)

### Region Markers (`_RegionMarker`)
- Risk-colour-coded dot (green / amber / red) matching disease risk level
- Normal / Hover / Selected states with distinct background highlight intensities
- Name label as child Label node for crisp text rendering
- `mouse_entered` / `mouse_exited` / `gui_input` signals connected to parent for state management

### Detail Panel
- Shows: region name, production zone, description, available licences, distance to processing, processing hub, annual harvest volume, key operators
- Temperature range bar (`_TempBar`) with optimal zone (12–16 °C) overlaid and colour shift for warm regions (blue → amber → red)
- Disease risk indicator: colour dot + text + details paragraph
- Confirm button enables only when a region is selected; triggers `region_selected` emission

---

## Technical Notes

- Both panels use `show_panel()` / `hide_panel()` API (fade tween) — compatible with MainMenu overlay pattern
- No scene files required; all UI is built programmatically in GDScript 4
- `_CoastlineDrawer` and `_TempBar` use `queue_redraw()` pattern, not `_process()`
- Region marker positions use `call_deferred` to ensure layout size is available before pixel-coordinate calculation
- Both panels respond to Escape key via `_unhandled_key_input`

---

## Integration Notes

To wire these panels into the game, add to any parent scene:

```gdscript
var encyclopedia := preload("res://scripts/ui/EncyclopediaPanel.gd").new()
var map_panel := preload("res://scripts/ui/MapPanel.gd").new()
add_child(encyclopedia)
add_child(map_panel)

# Show
encyclopedia.show_panel()
map_panel.show_panel()

# Listen for region selection
map_panel.region_selected.connect(_on_region_selected)
```

---

## Files Changed
- `godot/scripts/ui/EncyclopediaPanel.gd` — **NEW** (~520 lines)
- `godot/scripts/ui/MapPanel.gd` — **NEW** (~700 lines)
- `logs/EncyclopediaMap-2026-03-13.md` — this file
