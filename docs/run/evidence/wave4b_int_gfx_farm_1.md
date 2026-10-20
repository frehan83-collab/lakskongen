# Wave 4b integration: `wave4b/gfx-farm-1` merged into `wave4b/int`

F2-19 (carved shore pads) and F2-20 (the facility presenter) merged into
`wave4b/int` on 2026-10-20 (`--no-ff`, `5d7f18a`), on top of `5fa1f2a`
(ui-core-2 merged). The stage's own evidence is `f2_19_pads.md` and
`f2_20_presenter.md` (read the latter with its merge addendum). This file is
the merge: the conflicts, the review's findings and how each was handled,
the gates on the merged tree, and the frames looked at. Frames are
`wave4b_int_gfx_farm_1/` (1280 px JPEG); the capture's numbers are
`f2_20_facilities_metrics.json` (re-recorded at the merge, 36 shots); the
build cost is `perf/perf_wave4b_int_gfx_farm_1_create.{json,md}`; the golden
traces `wave4b_int_gfx_farm_1_golden_diff.md`.

## Merge

| File | Conflict | Resolution |
|---|---|---|
| `docs/RUN_LOG.md` | §7 and §8 rows on both sides | Both sides' rows kept (int's first) |
| `godot/qa/shots.json` | Both sides appended shots | Both kept: 82 + int's 7 U2-08 shots, ids unique |
| `tools/run_ui_flow_tests.sh` | MIN_CHECKS 1731 (int) vs 1670 (stage) | Measured on the merged tree: 1778 = 1731 + the stage's 33 + the review's 14 |
| `godot/tests/UiFlowTests.gd`, `translations.csv`, `AGENTS.md` | Auto-merged | Checked: `_run()` calls `_test_facilities_render` and `SettingsAudioChecks`; 2755 keys, none twice |

## Review findings

| # | Severity | Finding | Handling | Proof |
|---|---|---|---|---|
| 1 | high | On pad_b the pipe bridge ran through the harvest hall (the hall at a fixed v = -30, the bridge and basin following the pad's depth) | Confirmed by geometry (bridge at v -67.5 inside the hall's v -92.5..32.5) and by `CampusLayoutCheck`. Fixed: the quay side is laid out from the bridge's line (`LandCampusBuilder.bridge_line_v`): basin beside the first lot row, hall 10 m off the bridge and 3 m off the cross road, quay from the bridge on, all from the kit's real LOD0 boxes (`turned_box`; the hall reaches 44 m to one side of its origin, 30 m to the other; the symmetric `FOOTPRINT_M` table is gone). Roads reach the apron only through the bridge's corridor. A plot with no carved pad gets a frame its campus fits (`frame_size_for`) | `tests/FacilityLayoutChecks.gd`: every pad and lot count `get_land_plots` allows (pad_a 1-4, pad_b 1-2, overflow 1-4; HFS, RAS, pilot first; post-smolt 1-4 with and without the river intake: 54 layouts, 30 of them grow-out) clear, the bridge between basin and hall everywhere, the review's clash found. Mutation (the hall back at v -30): pad_b, overflow and the bridge check fail. Frames `46_facilities_campus_pad_b.jpg`, `_quay.jpg` |
| 2 | medium | Esc with the card open left the Fjord tab (GameUI took `ui_cancel` first) | Confirmed (tree order: the world before the UI). Fixed: cards over the world join `FacilityWorldCard.WORLD_CARD_GROUP`; `GameUI._input` closes an open one before leaving the tab | UI check made stronger: the first Esc closes the card and the tab stays `fjord`; +1: the next Esc leaves the tab. Mutation (no world-card close): fails |
| 3 | medium | Building a campus stalled the main thread 0.2-0.56 s | Confirmed by a windowed probe (load 47): `land_pad_slots` 21-31 ms a call (twice per land unit), a 4-lot campus's groups 84 ms warm, 236 ms cold (`SurfaceTool.append_from` reads every piece back from the GPU, ~0.6 ms a call). Fixed: pads computed once per generation; `KitMeshMerge` merges on the CPU from arrays read back once per mesh; the land kit loads in the background; `PerfHarness --facilities` reports `create_ms`, `rebuild_ms`, `create_spike` | Probe after: groups 6.5-7.4 ms warm, the pads 0.01 ms. Perf harness (load 47): the whole set's first sync 34.3 ms, a rebuild from nothing 29.2 ms (review: 560 ms). UI check: a second campus reads nothing back, the pads computed once. Mutations (no cache): fail |
| 4 | medium | Behind the camera the card stayed (alpha 0) and ate clicks | Confirmed by code. Fixed: hidden (`visible` false) while the anchor is behind, open (`is_open()`), shown again when the anchor is back | +2 UI checks; mutation (alpha 0 again): the hidden check fails |
| 5 | low | The "presenter only reads" source rule caught only a local named `sim` | Confirmed: the review's two mutations pass the old rule. New rule (`engine_rule_breaches`): every local bound to `/root/SimBridge` by any name, and the parameters of the file's functions it is handed to; the autoload by name, `GameBridge`, aliases, `call()` of a non-read and emitting `state_updated` refused | The review's two mutations and six more are negative fixtures, three allowed patterns positive ones |
| 6 | low | The pHash gate sat about 1 bit above the whole frame's noise | Confirmed (the clouds move between shots). Fixed: the clouds' drift staged before every grab; the campus's own screen box hashed (`phash_roi`); the tank-rings state stood twice (`_twin`) | p2 vs p4 ROI 14 bits (whole frame 16), the twin 0 (whole frame 2). The whole-frame test replaced by the stronger one (signal >= 9, noise <= 3, margin >= 6; RUN_LOG §7). Metric mutations killed |
| 7 | low | A waiting land phase never said why | Confirmed (`BuildOf` passed ""). Fixed: `closed_season` (its month is closed to it) or `funds`; `FACILITY_CARD_WAITING_CLOSED_SEASON` | xUnit `AWaitingLandPhase_SaysWhy_TheClosedSeasonOrTheMoney` (mutation: fails); UI check of the card's line |
| 8 | low | F2-19's computed intake reach never reaches the running engine | Confirmed (`SitePlotCatalog.WithGeometry` is called only from tests; the engine's pads keep 0 m). Not wired here: it changes the land physics of every land game, a sim change for the land lane with its own balance and golden review. BALANCE's note now says its figures are the harness's | RUN_LOG §8 "Finding (F2-19)" with the remedy and the test to add |
| 9 | low | The shadow half of the budget cannot fail at the home view; criterion 5 drew one of two units | Confirmed. The spec says its §5 budget is the P0-04 home view's; the framings are measured (`46_facilities_campus_full` +11/+6, new `46_facilities_budget_of1_framing` +19/+25) with regression ceilings (14/8, 24/32). Criterion 5 marked partial until F2-29 | `test_the_units_own_framings_stay_under_their_measured_ceilings` (mutations killed) |
| 10 | low | Conflicts with the current int head; the shared `44_` prefix | Merged as above; the presenter's shots are `46_facilities_*` | `tests/test_facility_presenter.py` reads the new ids |

## Found by the gates

The first full UI-flow run on `dd6f3b7` counted 1777 checks and 0 failures
but failed on 6 SCRIPT ERRORs in `KitMeshMerge.merge` (a part without
vertices). An instrumented run caught it: in the first campus build of the
suite `lox_tank_LOD1` read back while the headless server had no mesh for it
("mesh_get_surface: Parameter m is null"), the glb having come from the
background loader in that frame. `KitMeshMerge.arrays_of` now never keeps a
read without vertices (it syncs the server and reads again, then reads a
copy loaded on the main thread, and reports it), the presenter no longer
re-requests a glb the kit holds, and a UI check holds every kit mesh the
campus drew live on the rendering server (`7bef1e1`).

## Gates on the merged tree

On `7bef1e1` (the commits after it are documents):

- `dotnet build LakskongenSim.sln` and `godot/LakskongenGame.csproj`: 0 errors.
- `dotnet test LakskongenSim.sln`: 2060 passed, 3 skipped, 0 failed
  (LakskongenSim.Tests 1985 = the stage's 1984 + the waiting-reason test;
  31, 4, 5, 10 and 25 in the other projects; load 24-45).
- `python3.12 -m pytest tests/ -q`: 855 passed, 1 skipped (836 on int + the
  stage's 14 + the review's 5; one test replaced by a stronger one).
- `tools/validate_scenes.sh`: 84 scenes, failures=0 (83 + the capture scene).
- `tools/run_ui_flow_tests.sh`: checks=1778, failures=0, PASSED, MIN_CHECKS
  1778 = 1731 + the stage's 33 + the review's 14, load falling 19 to 5. The
  run before it on the same commit counted 1778 and failed one check, U2-07's
  wall-clock "30x: p95 of the UI side of a day" (19.8 ms against 18.2 ms)
  while another agent's dotnet process held eight cores (load 36-45); the
  stage measured that check with the presenter shown and removed (no
  effect), and this merge adds no per-day work to the presenter (two clock
  reads per sync). Earlier runs: `dd6f3b7` 1777/0 with the read-back race's
  SCRIPT ERRORs; two instrumented runs 1777/0, one of them with the race's
  warning and the fallback taken.
- `tools/record_golden_traces.sh --base 5fa1f2a`: 27 of 27 traces identical,
  math fingerprints equal (`wave4b_int_gfx_farm_1_golden_diff.md`). The sim
  changes of the merge (the stage's `ContractOffshoreUnit` split and QA seam,
  the review's waiting reason) leave every game identical.
- Capture: `tools/capture_screenshots.sh --scene
  res://qa/scenes/FacilityPresenterCapture.tscn`, 36 shots, exit 0;
  `tests/test_facility_presenter.py` 18 of 18 on its metrics.

## Frames looked at (windowed, 1920 x 1080, load 47-48)

| Frame | What it shows |
|---|---|
| `46_facilities_campus_pad_b.jpg` | pad_b's 2-lot campus (28 tanks): the orange bridge runs from the quay between the intake basin's pump house and the dark harvest hall, clear of both; the corridor road under it reaches the apron |
| `46_facilities_campus_pad_b_quay.jpg` | Closer, afternoon: the spans and their legs on the corridor road, the hall's wall 10 m beyond, the basin's arc at the water |
| `46_facilities_campus_full.jpg` | pad_a's full campus (56 tanks): unchanged in its look; the hall 6 m nearer the quay, the service road ending at the cross road |
| `46_facilities_campus_close.jpg` | LOD0 at the quay side: the bridge over the corridor road between the pump house and the hall |
| `46_facilities_campus_dusk_hero.jpg` | L2's dusk still holds: pink-violet sky, mirror sea, lit hall windows, the bridge in silhouette |
| `32_land_construction_p4_sea_works.jpg` | The sea works: the bridge's legs rising, the basin's walls, the hall and apron standing |

The OF1 frames are unchanged from the stage's (the presenter's offshore half
was not touched).
