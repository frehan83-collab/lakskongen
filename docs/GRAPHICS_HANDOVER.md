# Graphics and Blender handover — 2026-10-24, revision 3

Read §1 first to resume the current work. This revision supersedes the stale
“ship F2-29 / F2-30 recon only” text. The project-wide entry point is
`docs/HANDOFF.md`; detailed architecture remains in `AGENTS.md`.

## 0. Binding direction and git safety

Owner, 2026-10-24: buildings, slaughterhouses, land-based farms and pens are to
be player-placed. Focus on ocean, fjords, offshore, the land-farm kit and land
slaughterhouses. Spend nothing on trees, shore props (`naust`, `shore_quay`),
terrain textures, snow, 08a land contrast or species models.

- Start with `git fetch --all --prune && gh pr list`; others push here.
- Work only in worktrees from `origin/main`; never edit the main checkout.
  Merge `origin/main` again before the first commit. The owner merges PRs.
- PRs #16, #17 and #20 belong to someone else; do not push to their branches.
- Do not edit `src/` for this graphics assignment. Player placement of pens
  and slaughterhouses is a sim feature, not a modelling workaround.
- Tests are add-only. Keep both manifest/RUN_LOG rows on merges; never lower
  `MIN_CHECKS`. No real brands on models; append atlas rows only.
- Commits: `run: <item> - <outcome>`, evidence in the body, no attribution or
  co-author lines. Visual claims need committed frames and one RUN_LOG §7 row.
- Never run `python -m visual_qa.capture` or `baseline_update`. Ask before any
  windowed run (`game_start`, `capture_screenshots.sh`, `perf_smoke.sh`).

## 1. Resume here: F2-30 is WIP, not ready for a PR

**Location:** `/Users/fredrik.j.hansen/lakskongen/.claude/worktrees/f2-30`,
branch `art/f2-30`, base `466120c` (`origin/main` at handoff). Everything is
uncommitted; no PR exists. Do not create a second branch/worktree for this item
or discard the inherited work. No commands are running.

The inherited draft was rebased from `89bfc92` to `466120c` before editing.
Its GLBs were reviewed and rebuilt. The Blender look-dev scene was deleted and
the owner's original scene restored (25 objects); nothing was saved in Blender.

### Current files

Modified: `blender/asset_manifest.json`, `blender/build_land_base.py`,
`godot/assets/asset_manifest.json`, `godot/assets/models/facility_kit/textures/facility_atlas.png`,
`godot/scripts/environment/FarmSite.gd`, `godot/scripts/environment/PenCage.gd`,
`godot/tests/SiteFlowChecks.gd`, `tools/run_ui_flow_tests.sh`.

New: `blender/{build_closed_pens,closed_pen_designs,render_closed_pen_preview}.py`,
`tests/test_closed_pen_assets.py`, 11 GLB/JSON/import sets under
`godot/assets/models/closed_pen/`, and six frames under
`docs/run/evidence/f2_30/`.

Temporary, retained deliberately for the next debug run:
`godot/tests/F2_30_Probe.{gd,tscn}`. `SiteFlowChecks.gd` also retains
`F2_30_DEBUG` prints. Remove both probe files and the debug prints before the
final commit. `docs/run/evidence/f2_30_closed_pens.md` has **not** been written.

### What is implemented

- Four assets on `LK_FacilityKit`, with atlas rows 28 `donut_shell` and 29
  `skirt_tarp` appended. Service sectors keep the pump clear of handrails;
  the pump has a mounting plate. Previews place a pump on all three shells.
- `PenCage` lazily loads only active closed meshes. `FarmSite.apply_site_kit`
  reads the existing bridge `containment` payload and rebuilds only pen LOD
  MultiMeshes on a real change. Layout indexing, clicks, health rings and wave
  motion are retained. Presenters do not write sim state.
- Six new UI checks were intended to cover all levels. `MIN_CHECKS=2340` is
  only the provisional old count 2334 + 6; the full suite has not measured it.

### Verified results

- `BLENDER_BIN=/Applications/Blender.app/Contents/MacOS/Blender python3.12
  .claude/skills/ecc/blender-asset-pipeline/blender_asset_pipeline.py
  --verify-only`: **113 passed, 0 failed**.
- `python3.12 -m pytest tests/test_closed_pen_assets.py -q -k 'not evidence'`:
  **30 passed, 1 deselected**. This includes four byte-identical rebuild/atlas
  checks. It is not the complete test file: the evidence-doc gate is excluded.
- `bash tools/validate_scenes.sh`: **89/89 passed**. The clean worktree reports
  the ignored MCP runtime autoload missing and a dummy-RID leak on exit.
- `dotnet build godot/LakskongenGame.csproj`: **0 errors, 16 existing warnings**.
- Six revised Blender evidence frames exist and were visually reviewed.
- Full UI flow, full Python suite and Godot presenter captures are still open.

| Asset | LOD triangles | Actual shell volume, LOD0/1/2 (m³) |
|---|---|---|
| `closed_donut` | 8784 / 1884 / 396 | 21859.712 / 21393.779 / 21103.459 |
| `closed_globe` | 4874 / 1122 / 192 | 3465.193 / 3423.619 / 3391.501 |
| `semi_closed_ring` | 6312 / 1288 / 192 | n/a; collar Ø40 m, skirt 13 m |
| `pump_module` | 1080 / 232, no LOD2 | n/a; 6 × 3 × 3 m, intake 25 m |

The Python test independently isolates shell triangles by atlas V row and
recomputes volume from committed GLBs; it does not trust nominal arithmetic or
sidecars. LOD2 envelope compensation stays within the dimension and ±5% real
mesh-volume bands. The earlier draft's stale globe manifest hashes were fixed
by the full verify run.

### The failing check and the next action

The targeted probe currently reports **6 checks, 6 failures**. It also emits
dummy-renderer RID errors and inconsistent debug output, so neither the
production mapping nor the probe has been exonerated. Do not delete the checks
or mark them green. Visible facts: OpenNet fails before its LOD stage;
OpenNet's LOD stage passes; SemiClosed reaches expected LOD states but fails
part/transform validation.

More importantly, the probe design is invalid as an all-level purchase path:
`start_qa_scenario` starts with **12M NOK**, while the sequential upgrades cost
**215.5M NOK**. Replace it with per-level visual fixtures on QA sites
(`apply_site_kit` with the bridge payload) and keep a separate affordable
real-verb/player-path check. This preserves acceptance 3 without sim edits or
a money cheat. Diagnose direct-mesh and LOD failures with per-part/per-instance
assertion details, then run the standard UI suite. Do not trust the isolated
probe until its dummy-renderer errors are understood.

Finish in this order:

1. Fix the fixture and/or presenter from evidence; remove debug and probe files.
2. Capture headless Godot evidence for the actual pen mapping; write the
   evidence doc with acceptance, volumes, budgets, reproducibility and frames.
3. Run complete `tests/test_closed_pen_assets.py` and full UI flow. Set
   `MIN_CHECKS` to the exact printed count after merging current `origin/main`.
4. Run relevant checks, add a RUN_LOG §7 row, review the diff, merge
   `origin/main` again before the first commit, then commit and PR.

Probe command (from the F2-30 worktree):

```bash
env GODOT_MCP_TOKEN_PATH=$(mktemp -d)/t godot-mono --headless \
  --audio-driver Dummy --path godot res://tests/F2_30_Probe.tscn
```

## 2. Settled F2-30 decisions; do not re-derive

Spec: `docs/FARM_TYPES_SPEC.md` §5.3 and §7.3 row F2-30. Containment is a
site-wide upgrade on existing pens (`BuildSiteDialog.gd`'s `closed_pen` is not
a new unit). Mapping, confirmed against `ContainmentTech.cs`:

| Level | Presenter asset |
|---|---|
| OpenNet | existing `PenCage` |
| Skirt | existing `PenCage` + `LiceSkirt` |
| SemiClosed | `semi_closed_ring` + `pump_module` |
| ClosedFloat | `closed_donut` + `pump_module` |
| Submersible | `closed_globe` + `pump_module` |
| OffshoreClosed | `closed_globe` + `pump_module` |

Levels 4/5 therefore need no extra model in task 1. Acceptance 3's UI check
raises `MIN_CHECKS`; say so in the eventual PR.

## 3. Remaining owner-approved queue

1. **Slaughterhouse model:** self-contained player-placement-ready kit on
   `LK_FacilityKit`; reuse `harvest_hall` and `quay`, add wellboat intake /
   landing, reception/pump house and chilled loading bays; Small/Medium/Large
   matching `ProcessingTier`. **Numbers first:** put dimensions, real-world
   references, swatches and tri budgets in a PR and obtain owner OK before
   building. Model only; do not wire it into the game.
2. **Placement proposal, docs only:** write
   `docs/proposals/player_placement.md`: pen coordinates/verbs, mooring/ghost,
   save version/goldens/calibration/bridge; verify whether a slaughterhouse
   `LandTemplate` reuses plots, ghost, `ConstructionProject` and `PermitCase`.
   Replace bridge-only `BuyProcessingPlant`; migrate `OwnsProcessingPlant`
   saves. Order/size steps and mark owner gates. Do not edit `src/`.
3. **Ocean/offshore polish, one PR per item:** rectangular whitecaps; shoals
   through sea fog; emerald bloom; missing wakes; caustics as moving light;
   chip pill outliving text at 700–770 m; F2-22 vessel beat (6–15 s/job).
   Diagnose headless plus read-only MCP. Ask before windowed runs.
4. **Ask first:** actual pen/slaughterhouse placement, mood-golden pHash,
   unnamed models or extra containment models.

## 4. Tool safety on this Mac

- Blender 5.2.2: `/Applications/Blender.app/Contents/MacOS/Blender`. Headless:
  `-b --factory-startup --python blender/<script> -- ...`. Read
  `.claude/skills/ecc/blender-asset-pipeline/SKILL.md` before model work.
- Blender MCP is look-dev only. The owner's unsaved `farm_diorama.blend` holds
  the Havfarm: never open/reset/save it. Use a new `lookdev_<item>` scene and
  delete it afterwards. Committed assets come only from headless build scripts.
- Godot MCP is bound to the owner's stale main-checkout editor: read-only
  queries only; no edits, saves, game start or input. It cannot see worktrees.
  For connection issues use `python3 tools/mcp_bridge_alias.py`.
- Worktree Godot: `env GODOT_MCP_TOKEN_PATH=$(mktemp -d)/t godot-mono
  --headless --audio-driver Dummy --path godot ...`. Run `--import` once alone
  in a fresh worktree; never overlap another headless import (exit 139 risk).
- macOS has no `timeout`; use `perl -e 'alarm shift; exec @ARGV' N cmd`.
  Commands are capped at 30 s: background long runs and poll their logs.

## 5. Verification and current repository state

From the item worktree, after the one-time import:

```bash
python3.12 -m pytest tests/test_closed_pen_assets.py -q
BLENDER_BIN=/Applications/Blender.app/Contents/MacOS/Blender \
  python3.12 .claude/skills/ecc/blender-asset-pipeline/blender_asset_pipeline.py --verify-only
bash tools/validate_scenes.sh
dotnet build godot/LakskongenGame.csproj
bash tools/run_ui_flow_tests.sh   # 5–8 min; exact MIN_CHECKS required
```

`origin/main` is `466120c`; PRs #16, #17 and #20 remain open and are not ours.
PR #20 is the finished F2-29 Havfarm/mooring work (`e469fbc`), already verified
by its owner; do not redo it. The main checkout is dirty and has the owner's
editor open: leave it and its untracked directories alone. The docs worktree
for this handover is `.claude/worktrees/handover-2026-10-24`, branch
`docs/graphics-handover-2026-10-24`.
