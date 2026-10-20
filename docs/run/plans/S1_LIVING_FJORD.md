# S1 — Living fjord readout (W2 first item) — execution plan

> **STATUS (2026-10-12, session 1): DONE — gate PASSED.** Shipped: QA forcing
> bridge methods (`StartQaScenario`/`SetQaPenLice`/`SpawnQaBloom`/`ClearQaBlooms`),
> collar-tint rendering fix (per-pen material; instance uniforms never rendered),
> harness hardening. Evidence: `docs/run/evidence/session_01_s1_frames.txt`
> (lice pair phash 16, bloom pair phash 16, run14 capture set). Baseline
> promotion + repeatability control deferred to W2 close.

*Draft written session 0 (2026-10-12). Revisit after W1 decision memos. Gate: G-SHOT
(forced-state frames distinct) + G-TEST (data test that visuals follow sim values).*

## Goal

The 3D fjord visibly reflects live sim state: lice → pen collar tint, biomass →
visual density, weather → waves/foam, bloom → water tint, wellboat/feed-barge
events → 3D activity. Exit criterion: harness frames show **distinct, state-driven
images per forced state (red zone/lice, bloom, storm, night)** with md5 + phash
numbers recorded in RUN_LOG.

## Current-state audit (session 0, read-only)

Already implemented (verify each at capture time, fix gaps):

| Readout | Where | Status |
|---|---|---|
| Lice → collar tint | `FishPenManager.gd` (~L240): `_site_float(site, ["sea_lice","lice_per_fish","lice"])`, thresholds 0.2 / 0.5, health tint palette | present — needs forced-state proof |
| Bloom → water tint | `FjordVistaBridge._sync_environment` → `GameBridge.GetMaxBloomSeverity` → water shader `algae_density`; also `Sim3DSync._sync_bloom` | present — needs forced-state proof |
| Weather → waves/foam/fog | `FjordVistaBridge` + `Sim3DSync._sync_weather` → `wave_amplitude`, `wind_strength`, `temperature_factor` | present; storm shot exists (`10_fjord_storm`) |
| Pens from sim | `FishPenManager.sync_pens_from_sim` via `get_all_pens` (SimActions.gd:291) | present |
| Wellboat activity | `WellboatVisualManager` (visual/) — travels per `get_all_wellboat_jobs` | present — needs a visible-event shot |
| Feed barge | `FeedBargeVisualManager` (visual/) | present |
| Biomass → density | fish instances per pen (check `FishPenManager`/shader path) | **verify** — confirm density actually scales with `biomass` key |

## Missing piece (the actual work)

1. **Forced-state hooks in the harness** (`godot/scripts/tools/ScreenshotHarness.gd`):
   add shots `12_fjord_lice_red_zone` and `13_fjord_bloom` that stage sim state via
   public methods only. Candidates for forcing (discover first, do not fake):
   - scenario boot: harness already selects a scenario and `_ensure_qa_site(zone_id, ...)`
     — check whether `po3_red_sone` (scenarios.json:203) or a zone id can be used to
     get a red-zone site;
   - lice: find a legal engine path (e.g., bridge method used by tests, or a
     dev-only command); if none exists, add a minimal `Debug*` bridge method behind
     the existing dev-gating pattern — determinism rules apply (no new RNG).
   - bloom: `GetMaxBloomSeverity` exists; find the sim setter path used by tests
     (`BloomSystem`?) or stage via the bloom scenario if one exists.
2. **Distinctness evidence**: capture, then record md5 pairs
   (`12_*` vs `08_*`, `13_*` vs `08_*`) + phash distances (threshold 8). Control:
   two captures same state → md5 equal.
3. **Baseline promotion**: `visual_qa.capture` + `baseline_update` (new shots
   registered in `visual_qa/config.py SHOT_NAMES`). First promote the current
   24-frame set as the official baseline (no `baseline/` dir exists yet).

## Evidence plan (RUN_LOG §1 compliant)

```
tools/capture_screenshots.sh        → "Captured N screenshot(s)"
md5 -q .../12_fjord_lice_red_zone.png .../08_fjord_day.png
python -m visual_qa.diff            → raw distances
```

## Risks / notes

- The harness boots a real game session (~1–2 min); do not run it concurrently
  with a CI run (both drive Godot/GPU).
- `.import` files appear next to captured PNGs (Godot scans res://) — both dir and
  files are gitignored; never commit `screenshots/game/`.
- If a forcing hook requires engine changes, it ships with its own tests
  (`lakskongen-feature` skill) before the screenshot claim.
- After C# changes: `./tools/ci.sh`, then re-run `tools/mcp_bridge_alias.py`
  (headless CI instances rebuild the MCP registry).
