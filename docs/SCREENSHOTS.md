# Screenshots (Visual QA Harness)

Automated in-engine screenshots of Lakskongen's graphics/UI, captured from a real
Godot render (not mocked). Output is intended for **human visual QA** — review the
PNGs by eye; the harness makes no pass/fail judgement about how they look.

## Why a display is required

Godot can only render to an image when it has a graphics context. In `--headless`
mode there is no renderer, so `get_viewport().get_texture().get_image()` yields no
usable pixels. The harness detects headless mode and exits with an error rather
than writing blank files. Run it from a normal desktop session.

## How to run

```bash
# Default macOS Godot path, or auto-detected `godot`/`godot4`:
tools/capture_screenshots.sh

# Explicit binary:
GODOT_BIN="/path/to/Godot" tools/capture_screenshots.sh
```

The script launches the harness scene non-headless, waits for it to finish (the
harness quits itself), then prints every captured PNG path.

You can also run the harness directly:

```bash
/Users/fredrik.j.hansen/Applications/Godot_mono.app/Contents/MacOS/Godot \
  --path godot res://scenes/tools/ScreenshotHarness.tscn
```

## Output layout

Every shot is written twice:

| Location | Purpose |
| --- | --- |
| `screenshots/game/<name>.png` (repo) | Review/commit-friendly project copy |
| `user://screenshots/<name>.png` | Godot user-data copy |

On macOS the `user://` folder resolves to:

```
~/Library/Application Support/Godot/app_userdata/Lakskongen/screenshots/
```

## Shot list

| # | File | Setup |
| --- | --- | --- |
| 1 | `01_main_menu.png` | `MainMenu.tscn` after its fade-in |
| 2 | `02_scenario_selection.png` | Scenario-select panel (5 scenario cards) |
| 3 | `03_dashboard.png` | Game scene + GameUI Dashboard tab, after 12 simulated days |
| 4 | `04_telemetry.png` | Telemetry tab (tutorial gating forced open for QA) |
| 5 | `05_map_overlay.png` | Map tab with the **Oxygen** data overlay active |
| 6 | `06_finance.png` | Finance tab |
| 7 | `07_genetics.png` | Genetics & Breeding panel overlay |
| 8 | `08_fjord_day.png` | 3D fjord, summer noon, clear weather |
| 9 | `09_fjord_night_aurora.png` | Winter night, aurora intensity 1.0 |
| 10 | `10_fjord_storm.png` | Summer noon, storm (transition forced short for QA) |
| 11 | `11_fjord_underwater.png` | Camera below the water line (underwater post-process) |

UI panels are hidden for the 3D fjord shots so the environment is unobstructed.

## Per-shot mechanism

The harness (`godot/scripts/tools/ScreenshotHarness.gd`,
`godot/scenes/tools/ScreenshotHarness.tscn`) is a normal scene, so project
autoloads (`SimBridge`, `GameBridge`, `TutorialState`, …) are live.

- **Menu / scenario / genetics** are instantiated directly from their scenes/scripts.
- **Game boot**: instantiates `GameScene.tscn`, starts a scenario via
  `SimBridge.start_new_game(...)`, unlocks advanced panels via `TutorialState`,
  then calls `SimBridge.advance_days(12)`.
- **Tabs** are switched through `GameUI.show_tab(id)`.
- **Map overlay** is set on the embedded `SiteOverlayLegend` node.
- **Fjord state** uses the `FjordEnvironment` API: `set_season`, `set_time`,
  `set_weather`; the aurora is set on the sky `ShaderMaterial`; underwater moves
  the `FjordCamera` below the water line.
- Capture uses `get_viewport().get_texture().get_image().save_png(...)`, waiting on
  `RenderingServer.frame_post_draw` so a fully drawn frame is grabbed.

All node/property lookups are guarded: if a scene changes shape, the affected shot
is skipped with a warning instead of crashing the run.

## Limitations

- Images are captured at the project viewport size (1920×1080 per `project.godot`).
- This verifies only that a shot can be **produced and reviewed**; it does not
  assert visual correctness. Human review is the point.
- The harness cannot be validated in CI/headless — it only runs where a display is
  available.
