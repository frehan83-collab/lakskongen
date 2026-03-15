# Steam Store Screenshots

This folder contains the 10 required screenshots for the Lakskongen Steam store page.

## Required Screenshots (1920x1080 PNG)

1. **01_dashboard_overview.png** - Full game view with all UI panels
2. **02_fish_pens.png** - Zoomed view of fish pens with health indicators
3. **03_market_trading.png** - Market panel showing price charts
4. **04_processing_plant.png** - Processing chain view
5. **05_map_norwegian_coast.png** - Full map with 8 zones
6. **06_tutorial.png** - Tutorial overlay visible
7. **07_disease_management.png** - Disease management panel
8. **08_harvest.png** - Harvest operation in progress
9. **09_tech_tree.png** - Technology tree panel
10. **10_seasonal_weather.png** - Seasonal variation (winter/summer)

## Capture Instructions

### Quick Start
```powershell
cd ..\..\
.\capture_screenshots.ps1
```

### Manual Capture
1. Launch Godot and open `godot/project.godot`
2. Press F5 to play the game
3. Press `P` to enter Photo Mode
4. Navigate and capture (F12)
5. Screenshots save to: `%APPDATA%\Godot\app_userdata\Lakskongen\screenshots\`
6. Copy files to this folder
7. Rename according to convention above

## Photo Mode Controls

- **WASD** - Move camera
- **Mouse** - Look around
- **Q/E** - Move up/down
- **Shift** - Fast movement
- **Ctrl** - Slow movement
- **F12** - Take screenshot
- **F** - Cycle filters
- **H** - Toggle HUD
- **ESC** - Exit Photo Mode

## Filters Available

1. **None** - Default realistic look
2. **Golden Hour** ☀️ - Warm, saturated, sunset vibes
3. **Storm Drama** ⛈️ - Dark, moody, high contrast
4. **Underwater** 🌊 - Blue tint, atmospheric
5. **Northern Lights** ✨ - Enhanced colors, dramatic

## Quality Checklist

Before uploading to Steam, verify:
- ✅ 1920x1080 resolution
- ✅ PNG format
- ✅ Sharp and clear
- ✅ Good composition
- ✅ Shows gameplay feature
- ✅ No UI bugs
- ✅ Descriptive filename

## Steam Upload

1. Go to Steamworks Partner dashboard
2. Navigate to your game's store page
3. Upload screenshots section
4. Add captions for each image
5. Arrange in story order
6. Save and preview

---

**Ready to capture?** Run `capture_screenshots.ps1` from the salmon-sim folder!
