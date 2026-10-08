# 📸 Lakskongen — Screenshot Guide for Steam Store

## Objective
Capture 10+ high-quality in-game screenshots for Steam store page.

---

## Quick Start (5-minute session)

### 1. Launch Godot + Project
```bash
cd C:\Users\freha\.openclaw\workspace\salmon-sim\godot
# Launch Godot 4.x
# Open project.godot
```

### 2. Run Game
- Click "Play" (F5) in Godot editor
- Navigate through tutorial/gameplay
- Use F12 or in-game screenshot button (if implemented)

### 3. Required Screenshots (10 shots)

**Priority shots for Steam store:**

1. **Dashboard Overview** (main HUD)
   - Full game view with all UI panels visible
   - Show financial data, fish counts, map
   - Screenshot filename: `01_dashboard_overview.png`

2. **Fish Pen Management**
   - Zoomed view of pens with fish
   - Show health indicators, feeding system
   - Filename: `02_fish_pens.png`

3. **Market Trading Interface**
   - Market panel open showing price charts
   - Contracts visible
   - Filename: `03_market_trading.png`

4. **Processing Plant**
   - Processing chain view (HOG → fillets)
   - Show production lines
   - Filename: `04_processing_plant.png`

5. **Map View (Norwegian Coast)**
   - Full map with 8 zones visible
   - Your sites marked
   - Filename: `05_map_norwegian_coast.png`

6. **Tutorial Sequence**
   - Tutorial overlay visible
   - Clean, beginner-friendly view
   - Filename: `06_tutorial.png`

7. **Disease Management**
   - Disease alert/management panel
   - Lice count, delousing interface
   - Filename: `07_disease_management.png`

8. **Harvest Scene**
   - Harvest operation in progress
   - Wellboat, fish being harvested
   - Filename: `08_harvest.png`

9. **Technology Tree**
   - Tech tree panel open
   - Show unlocks and progression
   - Filename: `09_tech_tree.png`

10. **Seasonal Variation**
    - Same scene in different season (winter/summer)
    - Show weather/light variation
    - Filename: `10_seasonal_weather.png`

---

## Screenshot Settings

**Resolution:** 1920x1080 (minimum)  
**Format:** PNG (lossless)  
**Location:** `salmon-sim/screenshots/` (create folder)

---

## Godot Screenshot Methods

### Method 1: F12 (if implemented)
- Press F12 during gameplay
- Screenshots auto-saved to `user://screenshots/`

### Method 2: Godot Editor Screenshot
1. Run game in editor
2. Pause game
3. Click editor "Capture" button
4. Or use built-in screenshot tool

### Method 3: Windows Snipping Tool
- Launch game
- Alt+Tab to Snipping Tool
- Capture window
- Save to `salmon-sim/screenshots/`

---

## After Screenshots

1. **Review quality** — Sharp, good lighting, no UI bugs
2. **Rename files** — Follow naming convention (01_dashboard_overview.png)
3. **Move to folder** — `salmon-sim/screenshots/steam_store/`
4. **Update me** — Ping when done, I'll process them for Steam capsules

---

## Automation Option (Advanced)

If Godot has CLI screenshot support:
```bash
godot --headless --screenshot <scene_path> --output <filename>
```

I can research this and automate if needed.

---

**Ready to start?** Launch Godot and start capturing! 📸
