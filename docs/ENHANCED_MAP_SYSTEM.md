# Lakskongen Enhanced Map System
## Environmental Data Layer Implementation

> **Superseded (geo stages 2-3, Track C, 2026-10-20).** Nothing described
> here ships: `MapPanelEnhanced.gd` and its scene were never in the game, and
> the map tab is now the national map built from the real geo data
> (`godot/scripts/ui/map/`, `docs/geo/GEODATA.md` §7.3,
> `docs/geo/STAGE23_PLAN.md` Track C). Kept as history only.

---

## Overview

The enhanced map system brings Mareano-inspired environmental data visualization to the Lakskongen salmon farming simulator. Players can now explore Norwegian coastal regions with rich, color-coded environmental overlays that help inform strategic decisions about farm placement and operations.

---

## Features

### 1. Layer-Based Data Visualization

The map supports 7 distinct data layers, accessible via toolbar buttons or keyboard shortcuts (1-7):

| Key | Layer | Visualization | Use Case |
|-----|-------|--------------|----------|
| 1 | **Regions** | Disease risk colors (green/yellow/red) | Quick risk assessment |
| 2 | **Temperature** | Blue→Green→Yellow→Red gradient | Thermal stress planning |
| 3 | **Oxygen** | Red→Yellow→Green gradient | Water quality evaluation |
| 4 | **Currents** | Arrow direction + intensity color | Water exchange assessment |
| 5 | **Quality** | 5-tier score coloring | Overall site suitability |
| 6 | **Disease Risk** | High/med/low zone highlighting | Biosecurity planning |
| 7 | **Seasonal** | Variation intensity | Long-term planning |

### 2. Temperature Layer (🌡️)

**Color Scale:**
- **< 5°C** Deep Blue (#1a5276) - Arctic conditions, slow growth
- **5-8°C** Blue (#2874a6) - Cold water, reduced metabolism
- **8-12°C** Light Blue (#5dade2) - Cool, good conditions
- **12-16°C** Green (#58d68d) - OPTIMAL range for salmon
- **16-18°C** Yellow (#f4d03f) - Warm, stress threshold
- **> 18°C** Red (#e74c3c) - DANGER, thermal stress risk

**Gameplay Impact:**
- Northern zones (PO10-13): Cold water = slower growth but less disease
- Southern zones (PO01-03): Warmer water = faster growth but higher stress risk
- Summer peaks can exceed 20°C in shallow southern areas

### 3. Oxygen Layer (💨)

**Color Scale:**
- **< 5 mg/L** Red (#922b21) - CRITICAL, immediate action required
- **5-7 mg/L** Yellow (#f39c12) - Warning, suboptimal conditions
- **> 7 mg/L** Green (#27ae60) - Good, healthy levels

**Gameplay Impact:**
- Deep fjords may have oxygen depletion events
- High biomass + warm water = oxygen stress
- Northern zones typically have excellent oxygen due to cold water

### 4. Currents Layer (🌊)

**Visualization:**
- Arrow direction indicates flow direction
- Arrow size/color indicates speed
- **0.05-0.15 m/s** Weak - Poor exchange, risk of nutrient buildup
- **0.15-0.30 m/s** Moderate - Good exchange rate
- **0.30-0.50 m/s** Strong - Excellent exchange, may affect cage stability

**Gameplay Impact:**
- Strong currents improve water quality but increase equipment stress
- Weak currents may lead to algal blooms and oxygen depletion
- Tidal currents create periodic variation

### 5. Environmental Quality Layer (⭐)

**5-Tier Score System:**
- **> 0.8** Dark Green (#27ae60) - Excellent conditions
- **0.6-0.8** Light Green (#58d68d) - Good conditions
- **0.4-0.6** Yellow (#f4d03f) - Moderate, some challenges
- **0.2-0.4** Orange (#e67e22) - Poor, significant issues
- **< 0.2** Red (#e74c3c) - Critical, avoid if possible

**Calculation:**
Quality Score = 1.0 - (TempStress×0.4 + OxygenStress×0.4 + CurrentStress×0.2)

### 6. Interactive Region Selection

Click any region marker to view detailed information:

**Displayed Data:**
- Current temperature (surface and cage depth)
- Dissolved oxygen levels
- Current speed and direction
- Salinity, pH, turbidity
- Chlorophyll-a (phytoplankton proxy)
- Environmental quality score
- Disease risk assessment
- Available licenses
- Distance to processing facilities

### 7. Legend System

Dynamic legend updates based on active layer:
- Shows color scale for current visualization
- Explains what each color represents
- Includes unit information (°C, mg/L, m/s, etc.)

---

## UI Layout

```
┌─────────────────────────────────────────────────────────────┐
│ 🗺️ SELECT REGION — NORWEGIAN COASTLINE              [✕]    │
├─────────────────────────────────────────────────────────────┤
│ Data Layers: [Regions] [🌡️ Temp] [💨 O₂] [🌊 Currents] [⭐ Quality] │
├──────────────────────────┬──────────────────────────────────┤
│                          │                                  │
│    ┌─────────────────┐   │   PO 6: Nordmøre og             │
│    │                 │   │   Sør-Trøndelag                  │
│    │   NORWAY MAP    │   │                                  │
│    │   with color    │   │   🌡️ Temperature: 8.0°C         │
│    │   overlays      │   │   💨 Oxygen: 8.7 mg/L           │
│    │                 │   │   🌊 Current: 0.32 m/s          │
│    │  [●] [●] [●]    │   │   ⭐ Quality: 0.90 (Excellent)  │
│    │   region        │   │                                  │
│    │   markers       │   │   Disease Risk: Low             │
│    │                 │   │   Available Licenses: 20        │
│    │                 │   │                                  │
│    │  ┌─────────┐    │   │   [SELECT THIS REGION]          │
│    │  │ Legend  │    │   │                                  │
│    │  └─────────┘    │   │                                  │
│    └─────────────────┘   │                                  │
│                          │                                  │
└──────────────────────────┴──────────────────────────────────┘
```

---

## Region Data Summary

| Zone | Name | Temp (°C) | O₂ (mg/L) | Current (m/s) | Quality |
|------|------|-----------|-----------|---------------|---------|
| PO01 | Svenskegrensen-Jæren | 10.5 | 7.8 | 0.25 | 0.72 |
| PO02 | Ryfylke | 10.0 | 8.0 | 0.20 | 0.78 |
| PO03 | Karmøy-Sotra | 9.5 | 8.2 | 0.22 | 0.82 |
| PO04 | Nordhordland-Stadt | 9.0 | 7.9 | 0.30 | 0.75 |
| PO05 | Stadt-Hustadvika | 8.5 | 8.5 | 0.35 | 0.88 |
| PO06 | Nordmøre-S.Trøndelag | 8.0 | 8.7 | 0.32 | 0.90 |
| PO07 | N.Trøndelag-Bindal | 7.5 | 8.8 | 0.25 | 0.85 |
| PO08 | Helgeland-Bodø | 7.0 | 9.0 | 0.28 | 0.87 |
| PO09 | Vestfjorden-Vesterålen | 6.5 | 9.2 | 0.26 | 0.86 |
| PO10 | Andøya-Senja | 6.0 | 9.3 | 0.26 | 0.88 |
| PO11 | Kvaløya-Loppa | 5.5 | 9.4 | 0.30 | 0.82 |
| PO12 | Vest-Finnmark | 5.0 | 9.5 | 0.28 | 0.80 |
| PO13 | Øst-Finnmark | 4.5 | 9.6 | 0.25 | 0.78 |

---

## Integration with Game Systems

### Connection to C# Backend

The map reads environmental data from:

```csharp
// Access via GameBridge singleton
var bridge = Engine.get_singleton("GameBridge")
var env_data = bridge.GetEnvironmentalDataForZone("PO06")
```

Data sources:
- `MareanoEnvironmentalDataProvider` - Real-time zone data
- `NorwegianZoneEnvironmentalProfile` - Seasonal profiles
- `EnvironmentalDataPoint` - Detailed parameters

### Signals Emitted

```gdscript
# When player switches layers
environmental_layer_changed(layer_id: int)

# When region is selected
region_selected(region_id: String, environmental_data: Dictionary)
```

---

## Strategic Gameplay Usage

### Optimal Farm Placement

**For Growth Speed:**
- Target zones with 12-16°C temperatures (PO03-PO06)
- Balance with disease risk - southern zones grow faster but risk more disease

**For Disease Minimization:**
- Northern zones (PO09-13) have lowest disease pressure
- Cold water naturally suppresses sea lice and pathogens

**For Water Quality:**
- Look for zones with >0.85 quality score (PO05-10)
- Strong currents (0.25-0.35 m/s) provide best exchange

**For Cost Efficiency:**
- Consider distance to processing facilities
- PO02, PO05, PO11 have shortest distances

### Seasonal Planning

The map can help plan:
- **Spring stocking** - Choose zones with stable spring temperatures
- **Summer feeding** - Avoid zones that exceed 18°C
- **Fall harvesting** - Plan logistics from remote northern zones
- **Winter maintenance** - Consider ice risk in far north (PO12-13)

---

## Files

| File | Description |
|------|-------------|
| `godot/scripts/ui/MapPanelEnhanced.gd` | Main enhanced map implementation |
| `godot/scenes/ui/MapPanelEnhanced.tscn` | Scene file with UI layout |
| `godot/scripts/ui/MapPanel.gd` | Original map (retained for compatibility) |

---

## Future Enhancements

1. **Time Animation** - Play button to show seasonal changes
2. **Depth Profiles** - Click for water column visualization
3. **Comparison Mode** - Select multiple zones side-by-side
4. **Historical Data** - Compare current vs. 5-year averages
5. **Weather Overlay** - Real-time weather from met.no API
6. **Route Planning** - Visualize wellboat/supplier routes
7. **Export Function** - Save environmental data as CSV

---

## Credits

- **Environmental Data:** Based on Mareano/Geonorge datasets
- **Temperature Data:** Institute of Marine Research (HI)
- **Current Data:** Norwegian Meteorological Institute (MET)
- **Design Inspiration:** Mareano Portal (mareano.no)

---

*Part of the Lakskongen Salmon Farming Simulator*
