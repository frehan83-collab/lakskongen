# Wave 4b integration: `wave4b/gfx-farm-2` merged into `wave4b/int`

The farm-types presentation stage (F2-21 camera targets, the G bookmarks,
the open sea's swell and the Hs chip; F2-23 the first sight, the teaser, the
night lights and the showcase shots) merged into `wave4b/int` on 2026-10-21
(`--no-ff`, `940f0f9`), on top of `77e4d1d` (gfx-polish-1 merged). The
stage's own evidence is `f2_21_camera_sea.md`, `f2_23_first_sight.md`,
`f2_21/`, `f2_23/` and `f2_21_f2_23_golden_diff.md`. This file is the merge:
the conflicts, what the gates found, the review's findings and how each was
handled, the gates on the merged tree, and the frames looked at. Frames are
`wave4b_int_gfx_farm_2/` (JPEG, at most 1280 px); the golden traces
`wave4b_int_gfx_farm_2_golden_diff.md`; the swell's A/B
`perf/perf_wave4b_int_swell_gate_{nounits,units}.md`; the showcase numbers
`f2_23_showcase_metrics.json` (re-recorded on the merged tree, 22 shots).

Commits: `940f0f9` the merge, `4f9bf06` the chip the gates found, `f64c9de`
findings 1, 2 and 9, `0ef94c0` finding 3, `b1951e8` findings 4-8, then this
document.

## Merge

| File | Conflict | Resolution |
|---|---|---|
| `godot/scripts/environment/EnvironmentDirector.gd` | int's `stage_sky_time`/`get_sky_clock` (gfx-polish-1) against the stage's `set_mood_hook` at one place; in `_apply_water` int's `_apply_pen_calm` and `ripple_strength_for(wind)` against the stage's `open_sea.exposed_hs()` for `sea_height` | Both: the three functions side by side; int's pen calm and ripple curve with the stage's open-sea Hs (and `c["sea_hs_exposed"]`). A wrong pick here (int's `sea_state["exposed_hs"]`) passed the stage's checks: finding 4 |
| `docs/RUN_LOG.md` | §7 and §8 rows on both sides | Both sides' rows kept (int's first); int's three §8 rows edited in place by gfx-polish-1 kept as int has them |
| `tools/run_ui_flow_tests.sh` | MIN_CHECKS 1794 (int) against 1863 (stage) | Measured on the merged tree: 1905 = 1794 + the stage's 85 + the merge's 26 |
| `UiFlowTests.gd`, `water_shader.gdshader`, `VesselChip.gd`, `AGENTS.md`, `FJORD_RENDERER.md` | Auto-merged | Read: `_run()` adds the three checks files after `_test_facilities_render`; int's pen calm and the swell include sit side by side in the water shader; int's chip slide and the stage's dot shape in `VesselChip` |

`translations.csv`, `shots.json`, `shots.schema.json` and `project.godot`
changed on the stage only.

**Found by the gates:** the three new checks files passed 85 of 85 on the
merged tree but logged 4 SCRIPT ERRORs, which fail the UI flow runner:
`VesselChip.get_size_px` returned Nil from its typed `Vector2`.
gfx-polish-1's `keep_on_screen` reads the pill's `size_px` every frame, and
F2-21's sea chip is made hidden before its unit's first forecast, so nothing
had set it. It now reads zero until `set_chip` sets it (`4f9bf06`;
CameraSeaChecks +1).

## Review findings

All nine confirmed on the merged tree (the review's probes re-run with an
untracked runner, or by code read); none rejected.

| # | Severity | Finding | Handling | Proof |
|---|---|---|---|---|
| 1 | medium | A first sight or the teaser starts under an open critical event card; the card covers it and its Esc and buttons skip the cinematic | Confirmed ("first sight playing=true; critical card open=true"). `CinematicStage.modal()` names the card or menu that is up (critical card, briefing, pause menu, end screen, attention inbox), `blocker()` adds the other cinematic. A completion is queued and starts on the first frame nothing blocks; a due teaser waits and starts if still due. A card that comes up while one plays holds it still: the overlay steps aside, the clock stays held, the card takes the input, the sequence resumes after. The overlay holds back the game's own keys as a card does (the hidden HUD heard them: 4 set 30x; I opened the inbox over the sequence) | FirstSightChecks +5, TeaserChecks +4: under an open card it waits (queued, the UI as it was), Esc answers the card, it plays once the card has closed, a card coming up holds it still with the overlay aside; the game's keys held. Mutations: no blocker, no stand-still (each file), no held keys: each caught. Windowed: `first_sight_waits_for_the_card.jpg`, `first_sight_plays_after_the_card.jpg` |
| 2 | medium | Loading a save in the running game replays "Første i drift" for a unit long in operation | Confirmed ("after load s1: playing=true unit=ras_1"). The presenter reads the game's generation (`SimBridge.get_game_generation`, the event bus's counter that every new and loaded game moves); the first sync of a new one is a baseline (`new_game_seen` also drops the first sight's queue). The teaser takes it as a new timeline too: a save of another Første sesong game loaded on day 160 over one armed on day 100 played it | FirstSightChecks +3 (the review's case: building in one save, running in the other, loaded in turn: nothing plays); TeaserChecks +3 (that load: it never plays). Mutations: no baseline, no generation in the teaser: each caught. `test_the_game_generation_read_is_the_event_bus_counter` pins the read's purity |
| 3 | medium | The swell's loops run on every water vertex with every amplitude 0 (+0.33-0.50 ms p50), missing from the perf evidence | Confirmed by the shader. `swell_peak` (the largest exposed amplitude, 0 with no swell) gates both loops where exposure × `swell_peak` < 1e-4: the components' own skip, made early, so nothing drawn changes | Paired A/B, 1080p High: no unit, the loops forced on +0.23 / +0.46 ms p50 (FJORD / FARM, ratios 1.016 / 1.032); with units the swell itself 0.32 / 0.36 ms (FJORD / hav). CameraSeaChecks +2 (the uniform with and without a swell; both mutations caught), pytest +1 (the equivalence). `f2_21_camera_sea.md` "The swell's cost" |
| 4 | medium | The spectrum check read the director's own list, never the uniforms | Confirmed: the review's mutations A and B passed 40 of 40 | CameraSeaChecks +2: Σa² from `wave_amp` and `swell_amp` (2.000 m²), the wave field's rows the very array, `sea_height.y` = the target's Hs (4.000 m). Mutation A fails at 0.801 m², B (int's side of the conflict) at 0.397 m |
| 5 | medium | Nothing checked how a cinematic gives the world back | Confirmed (the behaviour was right) | FirstSightChecks and TeaserChecks compare `world_state` (hour and its pause, weather state, rain, visibility, cloud, sim weather, mood hook) before and right after Esc; FirstSightChecks the camera with the sequence's last frame (0.000 m, 0.000°). Mutation 1 fails both files (15.48 h, blaatime); mutation 2: 185.6 m / 26.4° |
| 6 | low | The weather came back over 10 s | Confirmed (rain 1.08 mm/h and 8.3 km before, 0 and 60 km right after). `WeatherController.snapshot`/`restore_snapshot`; `leave()` restores it, then the blend's duration, then re-syncs the sim's weather | The world_state checks: without the restore, rain 0.0 and 60 km in both files |
| 7 | low | A right-drag held into a cinematic kept orbiting after it | Confirmed (-18 rad on a bare move). `FjordCamera.input_enabled` turned off ends a drag, wherever it is turned off | FirstSightChecks +1 (the drag held through a first sight: a bare move turns nothing); without the reset the yaw moves 18 rad |
| 8 | low | "Bilde lagret" went to the hidden toast layer | Confirmed by code. It shows on the card under the buttons (`FirstSightOverlay.saved_line`) | FirstSightChecks +1 (the line on the end card, visible); the toast instead fails it. Windowed: `first_sight_picture_saved_on_the_card.jpg` |
| 9 | low | Unused `focus_target_ids()` and `unit_completed` | Confirmed by grep. `focus_target_ids()` removed; `unit_completed` drives the first sight (connected deferred, as the direct call was) | FirstSightChecks plays every first sight through the signal |

## Gates on the merged tree

On `b1951e8` (the commit after it adds documents, frames and the
re-recorded showcase metrics only):

- `dotnet build LakskongenSim.sln` and `godot/LakskongenGame.csproj`: 0 errors.
- `dotnet test LakskongenSim.sln`: LakskongenSim.Tests 2015 passed, 3
  skipped, 1 failed of 2019 (int's 2004 + the stage's 12 `OpenSeaSwellTests`),
  in two runs; the failure both times
  `SoakLongRunTests.ThreeYearResourceRentSoak_StaysSane_AndFast`, 24 s and
  18 s against its 10 s budget, while other agents' test suites held the
  load at 109-122; alone it takes 0.84 s (load 24). 31, 4, 5, 10 and 25 in
  the other projects.
- `python3.12 -m pytest tests/ -q`: 889 passed, 1 skipped (872 + the stage's
  15 + the merge's 2; two assertions replaced by stronger ones).
- `tools/validate_scenes.sh`: 86 scenes (85 tracked and the captain's
  untracked runner), failures=0.
- `tools/run_ui_flow_tests.sh`: checks=1905 failures=0, PASSED, load 16 -> 7.
- `tools/record_golden_traces.sh --base 77e4d1d`: 27 of 27 traces
  identical, math fingerprints equal. The merge changes no game.
- `godot_mcp_ops.py shader-check` (windowed): every shader compiled, no
  errors.

## Windowed captures on the merged tree (1920 x 1080)

`ShowcaseCapture` (22 shots): the spec's bars hold on the merged tree's
numbers (`tests/test_showcase_shots.py` 8 of 8 on them).

| Shot | Merged | Stage head (its report) | Bar |
|---|---|---|---|
| 33 hull share / water saturation | 1.551 % / 0.254 | 1.583 % / 0.252 | >= 1.5 % / <= 0.35 |
| 36 overcast OF1 hull-hue px / campus ΔL | 346 / 18.47 | 299 / 17.38 | >= 200 / >= 15 |
| 30 corners in frame at the land framing | 8 of 8 at -40.0° | 8 of 8 at -40.0° | all, -35..-45° |
| Teaser hull-hue px at 2 / 6 / 10 s | 8,186 / 6,762 / 4,372 | 8,242 / 6,804 / 4,369 | on screen |

The offshore frames read a little differently from the stage's: the OF1
ring in low winter sun averages (169, 139, 60) against (163, 128, 51), and
33's hull share is 0.03 points lower. gfx-polish-1's sun light keeps its
luminance at low elevations (merged first on int), which lights the ring
and the sea differently; the merge's own changes draw the same (the swell
gate is the components' own skip). The thin margin on 33 is in RUN_LOG §8.

The default harness (45 frames): one content failure, 08a 0.107, as on
`77e4d1d`; 18 SSIM 0.646, structure 0.399 against 0.073 without the mirror
(gain 0.326); 09 the water mirrors the aurora on 2.6 %; 19 foam 26.7 %
within 5 m of the shore, 0.0 % out at 15-40 m; 11 underwater 0 % black.

`FacilityPresenterCapture` (36 shots): `tests/test_facility_presenter.py`
20 of 20 on its numbers; the home-view draws equal int's (campus +10 / +0,
OF1 +7 / +0); pads against heath ΔE 12.01 / 11.01 (home), 22.37 (land). The
committed `f2_20_facilities_metrics.json` (int's) is kept.

## Frames looked at

| Frame | What it shows |
|---|---|
| `merged_showcase_frames.jpg` | 30, 31, 33, 35, 36 overcast and storm on the merged tree: the campus with its orange bridge and dark lids; the lit hall at Blåtime; the yellow ring with the orange service vessel and its chip; the storm with the chip (the flat whitecap patches are the stage's known G2-03 leftover); the yellow sliver and the campus in overcast; the chip carrying the read in the storm |
| `merged_cinematic_frames.jpg` | The land first sight (the quay lamps and the bridge; the end card at -40°), the offshore first sight (the ring from the vessel's side; the card with the wellboat in), the teaser (the tow beyond the mouth; the reduced still with its news card) |
| `first_sight_waits_for_the_card.jpg` | Windowed probe: a critical event and the OF1's completion in one refresh. The card stands over the world with its own hint ("Spillet står på pause …"); no letterbox, the HUD as it was; the first sight is queued |
| `first_sight_plays_after_the_card.jpg` | The same run after Esc answered the card: the first sight plays (letterbox, the ring framed from the vessel's side, the skip hint) |
| `first_sight_picture_saved_on_the_card.jpg` | The land first sight's end card after "Lagre bilde": "Bilde lagret: first_sight_ras_1.png" under the buttons |
| `merged_harness_water_frames.jpg` | 08, 10, 18, 09 from the default harness: the water with no offshore unit (the swell's loops skipped) as on int: sunny deep blue, the storm with no caps inside the pens, the calm-dusk mirror, the aurora in the water |

## Remainder

RUN_LOG §8, "Remainder (wave4b int, gfx-farm-2 merge)": the swell's 0.3-0.4
ms with a unit (one loop for the displacement and the slope could take part
of it back); 33's thin margin over 1.5 %; a card that comes up under a
cinematic reads "Spillet går videre" while the cinematic holds the clock;
the soak budget under other agents' load; the quiet "Bilde lagret" line;
the stage's own remainder (the teaser's Leksikon and locked card F2-35,
offshore wakes, the storm's whitecap patches, telephoto cloud steps, the
Havfarm-class first sight F2-29).
