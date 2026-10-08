# 🎯 Quick Start: Capture Steam Screenshots (5 Minutes)

**Goal:** Capture 10 professional screenshots for Lakskongen Steam store page.

---

## Prerequisites

1. **Godot 4.3+** installed
   - Download: https://godotengine.org/download/windows/
   - Extract anywhere, no installation needed

---

## One-Command Launch

```powershell
# From salmon-sim folder:
.\capture_screenshots.ps1
```

If Godot is in a custom location:
```powershell
.\capture_screenshots.ps1 -GodotPath "C:\Path\To\Godot.exe"
```

---

## Capture Process (15-30 min)

### In Godot:
1. **Game launches** → Click "Play" (F5)
2. **Game starts** → Press `P` for Photo Mode
3. **Photo Mode active** → You're in free camera!

### For Each Screenshot (10 total):
1. **Navigate:** WASD + Mouse to position camera
2. **Compose:** Find the perfect angle
3. **Polish:** Press `F` for filters (optional)
4. **Capture:** Press `F12`
5. **Confirm:** See flash + notification

### When Done:
1. Exit Photo Mode (ESC)
2. Return to PowerShell
3. Press ENTER
4. Screenshots auto-copied! ✨

---

## 10 Required Shots

✅ Use this as your checklist:

- [ ] **01** - Dashboard overview (all UI panels)
- [ ] **02** - Fish pens (close-up, health bars)
- [ ] **03** - Market trading (price charts)
- [ ] **04** - Processing plant
- [ ] **05** - Map (Norwegian coast, 8 zones)
- [ ] **06** - Tutorial (overlay visible)
- [ ] **07** - Disease management panel
- [ ] **08** - Harvest operation
- [ ] **09** - Technology tree
- [ ] **10** - Seasonal weather (winter)

---

## Photo Mode Cheat Sheet

### Movement
- `WASD` - Move camera
- `Mouse` - Look around
- `Q` - Move down
- `E` - Move up
- `Shift` - Move fast
- `Ctrl` - Move slow

### Actions
- `F12` - **TAKE SCREENSHOT** 📸
- `F` - Cycle filters
- `H` - Toggle HUD
- `ESC` - Exit Photo Mode

### Filters (Press F to cycle)
1. None (realistic)
2. Golden Hour ☀️ (warm sunset)
3. Storm Drama ⛈️ (moody)
4. Underwater 🌊 (blue tint)
5. Northern Lights ✨ (vibrant)

---

## Pro Tips

🎨 **Composition:**
- Use Rule of Thirds
- Show depth (foreground + background)
- Vary angles (high, low, side)

⏰ **Timing:**
- Take 2-3 shots per scene
- Pick best later
- Wait for good animations

🎬 **Filters:**
- Use sparingly (2-3 total across all shots)
- Golden Hour for warm/inviting
- Storm Drama for epic moments
- None for UI-focused shots

---

## After Capture

1. **Find screenshots:**
   - Script auto-copies to: `salmon-sim/screenshots/steam_store/`

2. **Rename files:**
   - `01_dashboard_overview.png`
   - `02_fish_pens.png`
   - etc.

3. **Quality check:**
   - 1920x1080? ✅
   - Sharp and clear? ✅
   - Shows gameplay? ✅

4. **Upload to Steam:**
   - Steamworks dashboard → Store page → Screenshots
   - Add captions for each
   - Arrange in story order

5. **Done!** 🎉

---

## Troubleshooting

**Q: Photo Mode won't activate?**  
A: Check input settings, or use pause menu button

**Q: Screenshots not saving?**  
A: Check Godot console for errors

**Q: Wrong resolution?**  
A: Already set to 1920x1080 in project

**Q: Where are my screenshots?**  
A: `%APPDATA%\Godot\app_userdata\Lakskongen\screenshots\`

---

## Need More Help?

Read the full guides:
- `T73_COMPLETION_SUMMARY.md` - Overview
- `T73_SCREENSHOT_AUTOMATION_REPORT.md` - Full research
- `screenshots/steam_store/CAPTURE_CHECKLIST.md` - Detailed tracker

---

**Ready? Let's go!** 🚀

```powershell
.\capture_screenshots.ps1
```
