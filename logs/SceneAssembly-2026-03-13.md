# Scene Assembly & Project Configuration — 2026-03-13

**Author:** Erik Dahl (DevOps)
**Commit:** feat: Godot scene assembly and project configuration

---

## Summary

Assembled the full Godot 4 project structure for Lakskongen. All scene files have been created and wired to their corresponding GDScript controllers. The project is now launchable with a proper main menu entry point.

---

## Files Created

### Scenes (`godot/scenes/`)

| File | Root Node | Scripts Wired |
|------|-----------|---------------|
| `MainMenu.tscn` | Control | MainMenu.gd, SettingsMenu.gd, SaveLoadMenu.gd |
| `GameScene.tscn` | Node | GameController.gd, AudioController.gd, HUD.gd, Dashboard.gd, TutorialUI.gd, NotificationCenter.gd, AnalyticsPanel.gd |
| `PauseMenu.tscn` | Control | PauseMenu.gd (new) |
| `SettingsMenu.tscn` | Control | SettingsMenu.gd |

#### MainMenu.tscn — Layout
```
MainMenu (Control, full-rect)
├── Background (ColorRect, ocean dark blue)
└── CenterContainer
    └── VBoxContainer (440px min-width)
        ├── TitleLabel ("Lakskongen", 72px)
        ├── SubtitleLabel ("Lakseoppdrett Simulator", 22px)
        ├── ButtonContainer (VBox, 14px gap)
        │   ├── %NewGameButton (360×60)
        │   ├── %ContinueButton
        │   ├── %LoadGameButton
        │   ├── %SettingsButton
        │   └── %QuitButton
        └── VersionLabel ("v0.1.0-alpha")
        %SettingsMenu (hidden)
        %SaveLoadMenu (hidden)
```

#### GameScene.tscn — Layout
```
GameScene (Node)
├── %GameController (Node) — scripts/GameController.gd
│   └── auto_start=true, starting_capital=10000000 NOK
├── %AudioController (Node) — scripts/AudioController.gd
└── UILayer (CanvasLayer, layer=1)
    ├── %HUD (Control, top bar: height=56px)
    ├── %Dashboard (Control, below HUD, full-width)
    ├── %TutorialUI (Control, bottom-left, 364×264px)
    ├── %NotificationCenter (Control, right rail, 360px wide)
    └── %AnalyticsPanel (Control, full-screen overlay, hidden)
```
All UI nodes use `unique_name_in_owner = true` for `%NodeName` access from scripts.

#### PauseMenu.tscn — Layout
```
PauseMenu (Control, full-rect, process_mode=Always)
├── Overlay (ColorRect, 65% black)
└── PanelContainer (centred, 400×480)
    └── VBoxContainer
        ├── PausedLabel ("Pause", 40px)
        ├── HSeparator
        ├── %ResumeButton
        ├── %SaveButton
        ├── %SettingsButton
        └── %QuitButton → changes scene to MainMenu.tscn
```

#### SettingsMenu.tscn — Layout
```
SettingsMenu (Control, full-rect)
├── Overlay (ColorRect, 70% black)
└── PanelContainer (760×640 centred)
    └── VBoxContainer
        ├── HeaderRow → TitleLabel + %CloseButton
        ├── %TabContainer
        │   ├── Tab "Lyd"     → %MasterSlider, %BGMSlider, %SFXSlider, %AmbientSlider
        │   ├── Tab "Grafikk" → %FullscreenCheck, %VSyncCheck, %ResolutionOption
        │   └── Tab "Spill"   → %SpeedSlider, %AutosaveSlider, %TutorialCheck
        └── ButtonRow → %ResetButton, %ApplyButton
```

### New Script (`godot/scripts/ui/`)

| File | Purpose |
|------|---------|
| `PauseMenu.gd` | Pause overlay: resume/save/settings/quit-to-menu. Hooks `ui_cancel` for ESC key. Calls `get_tree().paused` to freeze simulation. |

### Theme (`godot/theme/`)

| File | Type |
|------|------|
| `default_theme.gd` | Node autoload — programmatically builds a `Theme` resource |

#### Norwegian Palette

| Name | Hex | Usage |
|------|-----|-------|
| `OCEAN_BLUE` | `#104478` | Primary UI colour, buttons |
| `OCEAN_LIGHT` | `#589CD0` | Accent, hover state |
| `SALMON_PINK` | `#E57257` | Highlight, pressed state |
| `SALMON_DARK` | `#B13F25` | Salmon accent dark |
| `FJORD_GREEN` | `#2F644B` | Success, health bars |
| `FJORD_LIGHT` | `#5BA077` | Hover success |
| `SNOW_WHITE` | `#F1F4F9` | Primary text |
| `CLOUD_GREY` | `#B1BAC6` | Dim text, disabled |
| `DEEP_SEA` | `#07142E` | Background |
| `AMBER_WARN` | `#EDB22B` | Warnings |
| `DANGER_RED` | `#D62E2E` | Alerts, danger |
| `SUCCESS_TEAL` | `#21B58F` | Healthy fish, success |

Styled nodes: Panel, PanelContainer, Button (normal/hover/pressed/disabled), Label, ProgressBar, LineEdit, TabContainer.

### Asset Directories (`godot/assets/`)

```
godot/assets/
├── audio/      (.gitkeep — for AudioController StreamAudioPlayer resources)
├── textures/   (.gitkeep — for UI textures, icons, atlases)
└── fonts/      (.gitkeep — for custom font .ttf / .otf imports)
```
MusicLibrary.gd references `res://audio/{music,ambient,sfx,ui}/` — populate those paths with actual `.ogg`/`.wav` files to wire audio.

---

## project.godot Changes

### Main Scene
| Before | After |
|--------|-------|
| `res://scenes/Main.tscn` | `res://scenes/MainMenu.tscn` |

### Project Name
| Before | After |
|--------|-------|
| `"Lakskongen - Salmon Farming Simulator"` | `"Lakskongen"` |

### Autoloads Added

| Name | Script |
|------|--------|
| `AudioController` | `res://scripts/AudioController.gd` |
| `MusicLibrary` | `res://scripts/MusicLibrary.gd` |
| `ToastNotification` | `res://scripts/ui/ToastNotification.gd` |
| `LakskongenTheme` | `res://theme/default_theme.gd` |

Full autoload list (in load order):
1. `GameBridge` — C# GameBridge singleton
2. `EventBus` — C# event bus
3. `AudioManager` — C# audio manager
4. `SaveLoadManager` — C# save/load
5. `SimBridge` — GDScript simulation bridge
6. `AudioController` — Godot audio layer (BGM + ambient + SFX)
7. `MusicLibrary` — static audio path lookup
8. `ToastNotification` — CanvasLayer toast queue (layer 10)
9. `LakskongenTheme` — Norwegian palette + Theme resource

---

## Notes for Next Steps

- **Dashboard.gd** resides at `res://ui/Dashboard.gd` (not `scripts/ui/`). Either move it to `scripts/ui/` or keep the divergent path; GameScene.tscn references the current location.
- **AudioController double-use**: The script is both an autoload and a node in GameScene.tscn. Remove the `AudioController` child node from GameScene once it's confirmed the autoload covers all use cases, or remove the autoload entry and rely solely on the scene node.
- **Audio files**: `MusicLibrary.gd` expects `.ogg` files under `res://audio/`. Populate `godot/assets/audio/` and configure import paths accordingly.
- **PauseMenu integration**: Add `%PauseMenu` as a child of `UILayer` in GameScene (or load it dynamically) and connect `GameController`'s `pause_toggled` signal to `PauseMenu.show_menu()` / `hide_menu()`.
- **Theme activation**: Call `LakskongenTheme.apply_to_root()` from GameScene or MainMenu `_ready()` to push the theme to all nodes.
