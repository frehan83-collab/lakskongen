# Scene Validation

Headless scene-load validation for the Godot project. It catches the class of
breakage that a GDScript parse scan cannot see: `.tscn` files that fail to load,
`[ext_resource]` entries that point at deleted/moved files, and primitive meshes
saved against the abstract `PrimitiveMesh` type (which silently produce empty
geometry).

## Tooling

| Path | Purpose |
| --- | --- |
| `godot/scripts/tools/SceneValidator.gd` | Godot `SceneTree` script that does the actual validation. |
| `tools/validate_scenes.sh` | Locates Godot, runs the validator, folds in engine-level errors, and sets the exit code. |

## Usage

```bash
# Validate the whole project (default; skips res://addons)
tools/validate_scenes.sh

# Validate a single subtree
tools/validate_scenes.sh res://scenes
tools/validate_scenes.sh res://assets/models
```

Or run the Godot script directly:

```bash
godot-mono --headless --path godot \
    --script res://scripts/tools/SceneValidator.gd -- res://scenes
```

### Godot binary discovery

`tools/validate_scenes.sh` uses, in order:

1. `$GODOT_BIN` if it is executable.
2. `godot-mono`, `godot`, or `Godot` on `PATH`.
3. `/Applications/Godot{,_mono,-mono}.app/Contents/MacOS/Godot`.

If none is found it exits `2` without pretending to pass.

## What it checks

For every `*.tscn` found under the requested root(s):

1. **Dependency existence** — every `[ext_resource] path="res://..."` must exist
   on disk.
2. **Mesh validity** — every `[ext_resource type="Mesh"]` must load and return at
   least one surface (catches `[gd_resource type="PrimitiveMesh"]` resources that
   set a bogus `type = "TorusMesh"` property instead of declaring the concrete
   mesh type).
3. **Scene load** — the file must load as a `PackedScene`.
4. **Instantiation** — `PackedScene.instantiate()` must return a node without a
   fatal error (catches invalid root nodes, etc.).

Validation runs on the first frame rather than in `_init` so project autoload
singletons are registered. Otherwise scripts that reference autoloads such as
`LakskongenTheme` or `SimBridge` report spurious compile errors.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Every scanned scene loaded cleanly. |
| `1` | One or more scenes failed validation (or the engine printed a scene/resource error), a root held no scenes, or the validator never printed its result line (crash, early exit, or the 600 s timeout). |
| `2` | Tooling error (Godot not found, project missing). |

The wrapper prints a final `SCENE VALIDATION: PASSED/FAILED` line and the
validator prints a machine-readable `SCENE_VALIDATION_RESULT failures=N`.

## Baseline failures (pre-existing, not yet fixed)

The following are real issues discovered by the tool. They sit outside the
current repair scope (panels / dead legacy scenes) and are left for their owners:

| Scene | Problem |
| --- | --- |
| `res://scenes/ui/CarbonLedgerPanel.tscn` | Parse error at line 207 (`_parse_node_tag`) — the scene does not load. |
| `res://ui/AchievementItem.tscn` | `[ext_resource]` points at missing `res://scripts/ui/AchievementItem.gd`. |
| `res://ui/AchievementsPanel.tscn` | `[ext_resource]` points at missing `res://scripts/ui/AchievementsPanel.gd`. |
| `res://ui/ScenarioSelection.tscn` | `[ext_resource]` points at missing `res://ui/ScenarioSelection.gd`. |

`res://scenes/Main.tscn` emits a non-fatal warning: its `SaveLoadMenu` extent
uses a stale `uid` and falls back to the text path.

## Repaired scripts (per-file findings)

| Script | Verdict | Detail |
| --- | --- | --- |
| `scripts/visual/PenLODManager.gd` | **Fixed** | `preload(".../pen_collar_mesh.res")` referenced a non-existent file; retargeted to `pen_collar_mesh.tres`. |
| `scripts/environment/PenCage.gd` | **Fixed** | `get_parent() as FishPenManager` created a circular GDScript dependency (`FishPenManager` preloads `pen_cage.tscn`, which references `PenCage.gd`), so `pen_cage.tscn` failed to resolve its script. Replaced with a dynamic signal check/emit, breaking the cycle without touching `FishPenManager`. |
| `scripts/multiplayer/MultiplayerHUD.gd` | **Fixed** | `var abs_val := abs(value)` inferred `Variant`, which is a warning-as-error in this project. Changed to `var abs_val: float = absf(value)`. |

All three pass `godot --headless --check-only --script <path>` cleanly after the
change. No Godot 3 APIs or signal-arity problems were found; the earlier
`PenCage` "non-existent resource" report was real (dependency cycle), while the
`MultiplayerHUD` report was a strict-warning parse error (also real).

## Fixed by this work

- `res://assets/models/pen_cage/lod/PenLODManager.tscn` — malformed
  `[gd_resource type="Script"]` file rewritten as a proper scene.
- `res://scripts/visual/particles/ParticleEffects.tscn` — same malformed
  pattern, rewritten as a proper scene.
- `res://scenes/SettingsMenu.tscn` — wrapper root incorrectly declared
  `parent="."`; removed.
- Ten mesh `.tres` files under `assets/models/{pen_cage,boat,fish}/` that
  declared `type="PrimitiveMesh"` instead of their concrete mesh type.
