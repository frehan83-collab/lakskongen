# Graphics and Blender handover — 2026-10-24, revision 4

Compact entry point for graphics/Blender agents. Read `docs/HANDOFF.md` for
repo-wide rules, then start at §1 below. Do not re-run finished items.

## 0. Scope & git rules (owner direction 2026-10-24)

- **Focus:** player-placeable buildings, slaughterhouses, land-based farms, pens, ocean, fjords and offshore units.
- **Out of scope (spend 0 tokens):** trees, shore props (`naust`, `shore_quay`), terrain textures, snow, `08a` land contrast, species models, and `src/` C# sim edits.
- **Git hygiene:**
  - Always run `git fetch --all --prune && gh pr list` first.
  - Never edit `/Users/fredrik.j.hansen/lakskongen` (stale `review-remediation` checkout; owner's Godot editor is open).
  - Create worktrees from `origin/main` (or from `art/f2-30` if extending the `LK_FacilityKit` trim atlas before PR #22 merges so rows 28–29 stay intact).
  - Do not push to PRs #16, #17 or #20 (owned by other workers).
  - Never run `python -m visual_qa.capture` or `baseline_update`. Ask before any windowed run (`game_start`, `capture_screenshots.sh`, `perf_smoke.sh`).

## 1. Completed in this cycle (do NOT redo)

- **F2-28 Construction kit (`art/construction-kit` + `gfx/construction-kit-draw`):** merged on `main` (`89bfc92`). Uses `LK_FacilityKit` atlas row 27 (`crane_yellow` `#E6BE2E`).
- **F2-29 Havfarm-class & mooring kit (`art/f2-29`, PR #20, `e469fbc`):** `havfarm_class` (`385 × 59.5 m`, hull `#34424B`) + `mooring_line_kit` verified and open in PR #20.
- **F2-30 Closed-pen kit & presenter (`art/f2-30`, PR #22, `685ec1d`):** **DONE, all gates green.**
  - Built `closed_donut` (`8,784 / 1,884 / 396` tris; shell volume `21,859.7 / 21,393.8 / 21,103.5 m³`), `closed_globe` (`4,874 / 1,122 / 192` tris; shell volume `3,465.2 / 3,423.6 / 3,391.5 m³`), `semi_closed_ring` (`6,312 / 1,288 / 192` tris; Ø`40 m` collar, `13 m` skirt) and `pump_module` (`1,080 / 232` tris, no `LOD2`) on `LK_FacilityKit` with atlas rows 28 (`donut_shell` `#8E979C`) and 29 (`skirt_tarp` `#C9CCC6`) appended.
  - Wired all 6 `ContainmentLevel` values into `PenCage.gd` and `FarmSite.gd`; preloaded all 11 GLBs in `PenCage.CLOSED_SCENES` to prevent headless Dummy-renderer `MeshStorage` RID races against `FarmFacilityVisualManager._ready()`'s threaded loads.
  - Verified: `blender_asset_pipeline.py --verify-only` `113/113`, `pytest tests/test_closed_pen_assets.py` `31/31`, `validate_scenes.sh` `89/89`, `run_ui_flow_tests.sh` `UI_FLOW_RESULT checks=2340 failures=0` (`MIN_CHECKS=2340`).

## 2. Next work queue — start here, in order

### Task A: Slaughterhouse model specification PR (numbers first, owner gate before building)
- **Goal:** self-contained player-placement-ready slaughterhouse kit on `LK_FacilityKit`, reusing `harvest_hall` and `quay`, adding wellboat intake/landing, reception/pump house, and chilled loading bays, in three tiers (`Small`, `Medium`, `Large`) matching `src/LakskongenSim/Processing/ProcessingPlant.cs` (`ProcessingTier`).
- **Step 1 (now):** do **not** build GLBs yet. Inspect `ProcessingPlant.cs` and `blender/build_land_base.py` (`harvest_hall`, `quay`, `STRIPS`), then open a small docs/spec PR proposing exact dimensions (m), real-world Norwegian slaughterhouse references (code names only in-game, D17), atlas strip swatches (reusing existing rows 0–29 or appending ≥30), and LOD0/LOD1/LOD2 triangle budgets per tier. Wait for owner sign-off before building geometry. Do not wire into the game.

### Task B: Player placement architecture proposal (`docs/proposals/player_placement.md`, docs only)
- **Goal:** design how pens, land-based farms and slaughterhouses become player-placed without breaking determinism or saves.
- **Cover:**
  1. Pen coordinates/verbs, mooring frame & `FarmGhost` preview.
  2. Whether a slaughterhouse `LandTemplate` reuses `SitePlot`, `FarmGhost`, `ConstructionProject` and `PermitCase`.
  3. Replacing bridge-only `BuyProcessingPlant` with an engine command and migrating `OwnsProcessingPlant` saves.
  4. Save version bump, golden-trace impact (`27 × 730d`), `CalibrationRegistry` keys, bridge contract (`GameBridge.*.cs` → `SimProxies.gd` → `SimBridge.gd`), and ordered implementation slices with owner gates marked.
- **Constraint:** docs only (`docs/proposals/player_placement.md`). Do **not** edit `src/`.

### Task C: Ocean & offshore visual polish (one PR per item)
1. Rectangular whitecaps in water shader.
2. Fish shoals showing through heavy sea fog.
3. Emerald algal bloom tint tuning.
4. Missing vessel wakes.
5. Underwater caustics reading as moving light.
6. World chip pill outliving its text at `700–770 m`.
7. `F2-22` vessel logistics beat (`6–15 s` per job).
- Diagnose via code/shader inspection + headless checks + read-only Godot MCP. Ask before any windowed capture.

## 3. Tooling & pitfall cheat-sheet (saves debugging tokens)

- **Headless Dummy renderer thread-safety:** `FarmFacilityVisualManager._ready()` calls `ResourceLoader.load_threaded_request` for 41 land/construction `.glb` files when `GameScene.tscn` boots. Never call synchronous `load()` on `.glb` files on the main thread during `GameScene` startup—it races `DummyStorage::MeshStorage` (`RID_Owner`) and corrupts mesh surfaces (`mesh_storage.h` null `m` / `dead [...]`). Always `preload` or queue via `load_threaded_request`.
- **Scenario `po3_red_sone` starting sites:** `po3_nord` (site 0) starts with `"containment": "semi_closed"` (`SemiClosed`), while `po3_sor` (site 1) starts with `"OpenNet"`, and starting cash is `12M NOK`. Tests needing an open Polarcirkel pen on day 0 must select `po3_sor` (or filter for `containment == "OpenNet"`).
- **Blender 5.2.2:** `/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python blender/<script>.py -- ...`. Never touch/reset/save the owner's open `farm_diorama.blend` in Blender MCP; use a temporary scene if needed and delete it.
- **Godot headless in a worktree:**
  ```bash
  env GODOT_MCP_TOKEN_PATH=$(mktemp -d)/t godot-mono --headless --audio-driver Dummy --path godot --import
  ```
  Run `--import` once alone in a new worktree before running scene validation or UI flow tests.
- **Timeouts:** single tool commands time out at 30 s; run `pytest` (~95 s) and `bash tools/run_ui_flow_tests.sh` (~5–7 min) in the background (`nohup ... > /tmp/run.log 2>&1 &`) and poll.
