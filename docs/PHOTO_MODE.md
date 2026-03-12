# 📸 PHOTO MODE - Lakskongen

**Author:** Ingrid Lunde, UI/UX Designer  
**Date:** March 12, 2026  
**Status:** Phase 1 Complete ✅

---

## Overview

Photo Mode transforms Lakskongen's stunning Norwegian fjord visuals into shareable moments. Players can freeze time, explore with a free camera, apply cinematic filters, and capture screenshots worth sharing. This system directly addresses streamer appeal and social media marketing while giving players creative expression and session satisfaction.

**Key Features:**
- 🎥 Free camera movement with smooth controls
- 🎨 Cinematic filters (Golden Hour, Storm, Underwater, Northern Lights)
- ⏸️ Time control (pause, rewind)
- 🖼️ One-click screenshot capture
- 🎬 Automatic milestone cinematics
- 📹 Camera path recording (optional)

---

## Controls Reference

### Entering Photo Mode
- **Toggle Photo Mode:** Press `P` (or configured key)
- Game pauses automatically when entering photo mode
- Mouse is captured for camera control

### Camera Movement
| Control | Action |
|---------|--------|
| `W` | Move forward |
| `S` | Move backward |
| `A` | Strafe left |
| `D` | Strafe right |
| `Q` | Move down |
| `E` | Move up |
| `Right Mouse + Drag` | Look around (camera rotation) |
| `Scroll Wheel Up` | Increase camera speed |
| `Scroll Wheel Down` | Decrease camera speed |
| `Shift (hold)` | Fast movement (3x speed) |
| `Ctrl (hold)` | Slow movement (0.3x speed) |

### Photo Mode Actions
| Control | Action |
|---------|--------|
| `F12` | Capture screenshot |
| `F` | Cycle through filters |
| `H` | Toggle HUD visibility |
| `Space` | Pause/resume time |
| `R` | Rewind 30 seconds |
| `ESC` / `P` | Exit photo mode |

### UI Controls
- **Filter Dropdown:** Select specific filter
- **Time Controls:** Pause/play and rewind buttons
- **Screenshot Button:** Alternative to F12
- **HUD Toggle:** Show/hide all UI elements
- **Exit Button:** Return to normal gameplay

---

## Filters

### 1. **None**
Default rendering - clean and natural

### 2. **Golden Hour ☀️**
Warm, saturated colors perfect for sunset shots
- +10% brightness
- +15% contrast
- +25% saturation
- Ideal for: Harvest scenes, landscape overviews

### 3. **Storm Drama ⛈️**
Dark, moody, desaturated for dramatic effect
- -30% brightness
- +30% contrast
- -40% saturation
- Ideal for: Crisis moments, weather events

### 4. **Underwater 🌊**
Blue tint with depth fog for submersion feeling
- -10% brightness
- Blue fog overlay
- +10% saturation
- Ideal for: Fish close-ups, cage interior shots

### 5. **Northern Lights ✨**
Enhanced green/blue with glow effects
- +20% brightness
- +20% contrast
- +40% saturation
- Glow effects enabled
- Ideal for: Night scenes, aurora visibility

---

## Technical Implementation

### Architecture

```
PhotoMode System
├── PhotoMode.gd (Core Manager)
│   ├── Camera control
│   ├── Filter application
│   ├── Screenshot capture
│   └── Time manipulation
│
├── PhotoModeUI.gd (UI Controller)
│   ├── Overlay panel
│   ├── Filter dropdown
│   ├── Time controls
│   └── Notifications
│
├── PhotoMode.tscn (UI Scene)
│   ├── Overlay panel (top-right)
│   ├── Tips overlay (bottom-center)
│   ├── Flash effect
│   └── Notification popup
│
├── VisualMilestones.cs (C# Achievements)
│   ├── Milestone tracking
│   ├── Automatic cinematics
│   └── Screenshot triggers
│
└── CameraPath.gd (Recording System)
    ├── Path recording
    ├── Smooth playback
    └── Export capabilities
```

### Core Systems

#### 1. **PhotoMode.gd** (Main Controller)
**Location:** `game/scripts/PhotoMode.gd`

**Responsibilities:**
- Free camera management
- Input handling (WASD, mouse, keyboard shortcuts)
- Filter rendering via Environment adjustments
- Screenshot capture to `user://screenshots/`
- Time scale manipulation
- Smooth camera interpolation

**Key Methods:**
```gdscript
toggle_photo_mode()           # Enter/exit photo mode
capture_screenshot()          # Save screenshot to file
apply_filter(filter: Filter)  # Apply visual filter
toggle_hud()                  # Show/hide HUD
toggle_time_pause()           # Pause/resume time
```

**Signals:**
```gdscript
signal photo_mode_toggled(active: bool)
signal screenshot_captured(path: String)
signal filter_changed(filter_name: String)
```

#### 2. **PhotoModeUI.gd** (UI Controller)
**Location:** `game/scripts/PhotoModeUI.gd`

**Responsibilities:**
- UI visibility management
- Button/dropdown event handling
- Visual feedback (flash, notifications)
- Tips overlay timing
- Design token application

**Key Methods:**
```gdscript
show_tips()                   # Display control tips
show_notification()           # Show screenshot confirmation
apply_design_tokens()         # Apply Lakskongen theme
```

#### 3. **VisualMilestones.cs** (Achievement System)
**Location:** `src/LakskongenSim/Events/VisualMilestones.cs`

**Milestones Tracked:**
1. **First Harvest** - Initial fish harvest
2. **1M NOK Revenue** - First million
3. **10M/100M Revenue** - Growth milestones
4. **Perfect Sustainability Quarter** - Environmental achievement
5. **100K/1M Fish Harvested** - Production milestones
6. **First Acquisition** - Business expansion
7. **Vertical Integration** - Supply chain mastery
8. **International Expansion** - Global reach
9. **Crisis Survival** - Resilience achievement

**Cinematic Types:**
- Slow-motion harvest sequences
- Fireworks celebrations
- Drone overview shots
- Production montages
- Recovery narratives

**Usage:**
```csharp
visualMilestones.CheckMilestone(
    MilestoneType.FirstHarvest,
    context
);
```

#### 4. **CameraPath.gd** (Recording System)
**Location:** `game/scripts/CameraPath.gd`

**Capabilities:**
- Record camera movement at 60fps
- Smooth playback with interpolation
- Save/load paths as JSON
- Loop playback option
- Export preparation (video frames)

**Usage:**
```gdscript
cameraPath.start_recording(camera)
# ... move camera around ...
cameraPath.stop_recording()
cameraPath.save_path("epic_harvest")

# Later:
cameraPath.load_path("epic_harvest")
cameraPath.start_playback(camera, speed=1.0, loop=false)
```

---

## Screenshot System

### File Naming
Screenshots are automatically named with timestamps:
```
lakskongen_YYYYMMDD_HHMMSS.png
Example: lakskongen_20260312_153045.png
```

### Storage Location
- **Development:** `user://screenshots/`
- **Windows:** `%APPDATA%\Godot\app_userdata\Lakskongen\screenshots\`
- **macOS:** `~/Library/Application Support/Godot/app_userdata/Lakskongen/screenshots/`
- **Linux:** `~/.local/share/godot/app_userdata/Lakskongen/screenshots/`

### Format
- **Format:** PNG (lossless)
- **Resolution:** Native game resolution
- **Size:** Typically 2-8 MB per screenshot

---

## Integration with Game Systems

### 1. **GameManager Integration**
Photo mode should be initialized as autoload or early in GameManager:

```gdscript
# In GameManager._ready()
var photo_mode = PhotoMode.new()
photo_mode.name = "PhotoMode"
add_child(photo_mode)
```

### 2. **Input Map Configuration**
Add these actions to `project.godot` input map:
```
toggle_photo_mode: P
capture_screenshot: F12
toggle_filters: F
toggle_hud: H
pause_time: Space
rewind_time: R
```

### 3. **HUD Integration**
Ensure HUD node is accessible at `/root/Main/HUD` or update path in PhotoMode.gd

### 4. **Milestone Triggers**
Game systems should call VisualMilestones.CheckMilestone() when events occur:

```csharp
// In harvest system
if (isFirstHarvest) {
    visualMilestones.CheckMilestone(
        MilestoneType.FirstHarvest
    );
}

// In finance system
if (totalRevenue >= 1_000_000) {
    visualMilestones.CheckMilestone(
        MilestoneType.Revenue1M
    );
}
```

---

## Design Tokens Applied

From `design/tokens.json`:

```json
{
  "colors": {
    "bg": "#0F1720",
    "panel": "#121826",
    "text": "#E6EEF3",
    "accent": "#2AB7B5"
  },
  "spacing": { "unit": 8 },
  "radii": { "md": 10 }
}
```

**UI Styling:**
- Panel backgrounds: `#121826`
- Accent borders: `#2AB7B5` (2px)
- Text color: `#E6EEF3`
- Border radius: 10px
- Padding: 16px (2 * unit)

---

## Performance Considerations

### Smooth Camera Movement
- Uses `lerp()` for position interpolation
- Movement smoothing factor: 0.5
- Base camera speed: 10 units/sec
- Speed multipliers: 0.3x (Ctrl), 1x (normal), 3x (Shift)

### Filter Performance
- Filters use Godot's Environment adjustments
- No custom shaders (Phase 1)
- Minimal performance impact
- Can be disabled for low-end systems

### Screenshot Capture
- Captures viewport texture (no extra render)
- Saves asynchronously
- Brief flash feedback (~300ms)
- No frame drops during capture

---

## Future Enhancements (Phase 2+)

### 🎯 High Priority
- [ ] **Depth of Field:** Tilt-shift miniature effect
- [ ] **Focal Length Control:** Wide-angle vs telephoto
- [ ] **Weather Override:** Force specific weather for shots
- [ ] **Pose Controls:** Pause and position objects/units
- [ ] **Grid Overlay:** Composition guides (rule of thirds)

### 🎬 Content Features
- [ ] **Video Export:** 10-second clips for social media
- [ ] **Timelapse Mode:** Speed up recorded paths
- [ ] **Screenshot Challenges:** Weekly themes with voting
- [ ] **In-game Gallery:** Browse and manage screenshots
- [ ] **Share Integration:** Direct upload to Steam Workshop

### 🎨 Advanced Filters
- [ ] **Black & White:** Classic monochrome
- [ ] **Vintage Film:** Film grain and vignette
- [ ] **Cyberpunk:** Neon glow and chromatic aberration
- [ ] **Custom LUTs:** Load external color grading

### 📹 Camera Tools
- [ ] **Spline Paths:** Bezier curve camera movement
- [ ] **Orbit Mode:** Circle around a point
- [ ] **Follow Target:** Track specific fish/boat
- [ ] **Shake Effects:** Cinematic camera shake

### 🚀 Creator Tools
- [ ] **Text Overlays:** Add farm name, stats
- [ ] **Watermarks:** "Built in Lakskongen" branding
- [ ] **Frame Export:** Individual frames for editing
- [ ] **360° Panorama:** Spherical screenshots

### 🌐 Social Features
- [ ] **Steam Workshop:** Share camera paths
- [ ] **Screenshot Voting:** Community favorites
- [ ] **Monthly Contests:** Best photos win prizes
- [ ] **Gallery Website:** lakskongen.no/gallery

---

## Testing Checklist

### ✅ Core Functionality
- [x] Photo mode toggles on/off with P key
- [x] Camera moves smoothly with WASD
- [x] Mouse look rotates camera
- [x] Speed modifiers work (Shift/Ctrl)
- [x] Scroll wheel adjusts speed
- [x] Time pauses when entering photo mode
- [x] HUD toggles on/off
- [x] All 5 filters apply correctly
- [x] Screenshots save to correct directory
- [x] Screenshot naming is correct
- [x] Flash effect shows on capture
- [x] Notification displays properly
- [x] Exit restores normal gameplay

### ✅ UI Testing
- [x] Overlay panel displays in top-right
- [x] Filter dropdown lists all filters
- [x] Time control buttons respond
- [x] Screenshot button works
- [x] Exit button closes photo mode
- [x] Tips overlay shows and fades
- [x] Design tokens applied correctly

### 📋 Integration Testing
- [ ] Photo mode works with HUD system
- [ ] GameManager integration functional
- [ ] Input actions mapped correctly
- [ ] VisualMilestones triggers work
- [ ] Screenshots capture game correctly
- [ ] Performance acceptable (60+ fps)

### 📋 Milestone Testing
- [ ] First Harvest cinematic plays
- [ ] Revenue milestone celebrations work
- [ ] Sustainability achievement shows
- [ ] Auto-screenshots capture correctly
- [ ] Achievement popups display
- [ ] Multiple milestones don't conflict

---

## Known Limitations (Phase 1)

1. **Rewind functionality** not yet implemented (placeholder)
2. **Camera path video export** not functional (use OBS for now)
3. **Color LUTs** not created (using basic adjustments)
4. **Cinematic camera movements** are placeholders
5. **No depth of field** or focal length control
6. **Weather override** not implemented
7. **Social sharing** requires manual upload

---

## Developer Notes

### Adding New Filters
1. Add enum entry to `PhotoMode.gd::Filter`
2. Add case to `apply_filter()` method
3. Update filter names in `get_current_filter_name()`
4. Add UI entry in PhotoModeUI dropdown

### Creating Custom Cinematics
1. Add milestone to `VisualMilestones.cs`
2. Implement cinematic function (e.g., `PlayNewMilestoneCinematic()`)
3. Define camera path or effects
4. Set duration and cleanup callback

### Camera Path Presets
Save common paths (e.g., "harvest_overview", "fjord_panorama") in `user://camera_paths/` for reuse in cinematics.

---

## Credits

**Design & Implementation:** Ingrid Lunde, UI/UX Designer  
**Inspired By:**
- Farming Simulator's photo modes
- Anno 1800's screenshot tools
- Ghost of Tsushima's legendary photo mode

**Norwegian Fjords:** Nature's own photography studio 🏔️

---

## Questions & Support

For technical questions or feature requests:
- Contact: Ingrid Lunde (UI/UX)
- Technical implementation: See code comments
- Design tokens: `design/tokens.json`

---

**"Every harvest is a masterpiece. Make it yours." 📸✨**

---

## Appendix: File Locations

```
salmon-sim/
├── game/scripts/
│   ├── PhotoMode.gd              # Core photo mode manager
│   ├── PhotoModeUI.gd            # UI controller script
│   └── CameraPath.gd             # Camera recording system
│
├── godot/scenes/ui/
│   └── PhotoMode.tscn            # Photo mode UI scene
│
├── src/LakskongenSim/Events/
│   └── VisualMilestones.cs       # Milestone & cinematic system
│
├── docs/
│   └── PHOTO_MODE.md             # This document
│
└── design/
    └── tokens.json               # Design system tokens
```

---

**Status:** Phase 1 deliverables complete. Ready for integration testing and polish! 🎊
