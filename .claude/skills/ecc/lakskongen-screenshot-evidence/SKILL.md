---
name: lakskongen-screenshot-evidence
description: Capture → label → distinctness/diff workflow for Lakskongen visual claims on top of the visual-qa skill. Use for any UI/3D change, S1/S2/S3/S7 gates, store screenshots, and before/after claims in RUN_LOG.
origin: project
---

# lakskongen-screenshot-evidence — visual claims need pixels

Use this skill whenever a task claims a visual outcome (living fjord states,
time-series UI, accessibility pass, visual upgrades) or when screenshots feed
store art / demo polish. The rule from `AGENTS.md`: **headless "green" is not
evidence; visual claims need screenshot diffs.** This skill is the end-to-end
recipe; deep workflow lives in the `visual-qa` skill (`.claude/skills/ecc/visual-qa/SKILL.md`).

## 0. One-time setup

```bash
pip install pillow imagehash        # visual-qa diff deps (imagehash not installed by default)
```

## 1. Capture (non-headless only)

```bash
tools/capture_screenshots.sh        # launches the harness on a desktop session; quit = auto
```

- Harness scene: `res://scenes/tools/ScreenshotHarness.tscn`
  (`godot/scripts/tools/ScreenshotHarness.gd`).
- Outputs: `godot/screenshots/game/*.png` (gitignored) + `harness.log` next to them;
  user:// copy at `…/app_userdata/Lakskongen/screenshots/`.
- Headless Godot renders **no pixels** — the script refuses explicitly. Do not
  "capture" headless; do not fake it.
- The harness is a deterministic sequence (fixed camera framing,
  `auto_orbit_enabled=false`) — that's what makes frames comparable run-to-run.

## 2. Force states (how the harness stages conditions)

Fjord shots are staged via `_configure_env(season, hour, weather, aurora, zoom)`
on `FjordEnvironment` (`set_season`, `set_time`, `set_weather` with
`transition_duration=0.15`, aurora shader param, `set_zoom_level`):

| Shot(s) | Forced state |
|---|---|
| `08a_fjord_live_sync` | live sim→3D path (sim weather NOT overridden — the only "real" frame) |
| `08_fjord_day` | season 0.5, hour 12, clear, fjord zoom |
| `08b_fjord_farm` | farm zoom (asset closeup: fish, pens, boats, barges) |
| `09_fjord_night_aurora` | hour 23, aurora 1.0 |
| `10_fjord_storm` | weather = storm |
| `11_fjord_underwater` | camera under water (`global_position.y=-60`) |
| `14/15/16_diag_*` | diagnostics: tilt/underwater overlay/farm hidden |

When a new state must be forced (e.g. **lice-hit collars, bloom tint, red zone**
for S1), extend the harness with an explicit `_configure_*` call — never tweak
sim internals from the harness; stage via public methods only.

## 3. Distinctness evidence (the actual proof)

For every "frames differ per state" claim, record **raw numbers**:

```bash
# md5 per frame — fast, exact-identity check (same state twice ≈ same md5,
# different states must differ):
md5 -q godot/screenshots/game/09_fjord_night_aurora.png \
       godot/screenshots/game/10_fjord_storm.png

# perceptual diff vs baseline (threshold 8, from visual-qa config):
python -m visual_qa.diff
python -m visual_qa.diff --threshold 8 --fail-on-diff   # CI-style gate
```

- Same-state repeatability: capture twice; md5 equal (or phash distance 0) is the
  control. Different forced states: phash distance must exceed threshold (≥9 is
  "noticeable"; ≥16 "major"). Report both.
- Labeled gallery for PRs: `python -m visual_qa.gallery` (see visual-qa skill).
- New shots must be registered: add to `SHOT_NAMES` in `visual_qa/config.py` and
  promote with `python -m visual_qa.baseline_update -m "<why>"` (baseline lives
  in `godot/screenshots/baseline/` + `manifest.json`).

## 4. Evidence record format (RUN_LOG / session report)

```
Shots: godot/screenshots/game/<NN>_<name>.png (forced state: <what>)
Distinctness: md5 <stateA>=<hash> vs <stateB>=<hash>; phash distance <N> (threshold 8)
Command: tools/capture_screenshots.sh → "Captured <N> screenshot(s)"
```

A gate claim without these numbers is not done (RUN_LOG §1 auto-fail list).

## 5. Anti-patterns

| Anti-pattern | Why it fails | Fix |
|---|---|---|
| Headless capture | No pixels; blank/skip | Desktop session + this script |
| Claiming "looks different" | Not evidence | md5 + phash numbers |
| Committing `screenshots/game/` | Gitignored churn | Commit only `baseline/` promotions |
| Re-baselining silently | Hides regressions | `baseline_update` with a message + RUN_LOG note |
| Forcing states by editing sim code | Proxy for real behavior | Stage via harness methods (`_configure_env`) |
| One frame per state, no control | Can't separate noise from change | Repeat-capture control + diff |

## 6. Relation to other skills / gates

- `visual-qa` (repo): capture/diff/gallery/baseline internals — this skill wraps it.
- `.claude/skills/ecc/godot-mcp-ops`: `export-verify`, `shader-check` for CI-side.
- Evidence gates served: G-SHOT (S1/S2/S3/S7, store art), part of G-TEST for
  visual features. D6 (≥24 labeled frames) is the cumulative release bar.
