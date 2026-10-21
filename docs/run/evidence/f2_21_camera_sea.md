# F2-21 evidence: camera targets, bookmarks, the offshore sea

Farm-types spec item F2-21 (§4 "Camera", "Weather and exposure display",
"Wave driver"; §5.5 rules 2 and 6; §7.2), branch `wave4b/gfx-farm-2` from
`wave4b/int` `1bea464`. The camera frames every pad and slot and the units on
them at their own distance, pitch and yaw; G steps Fjord → Land → Hav; the
open coast draws the target plot's own sea, its wind sea and a long swell,
and the units ride it; each offshore unit carries an Hs chip read from the
engine. Numbers come from the UI flow suite (`godot/tests/CameraSeaChecks.gd`,
40 checks, headless), xUnit (`OpenSeaSwellTests`, 12) and pytest
(`tests/test_camera_bookmarks.py`, 6). The frames are the F2-23 showcase run
(`docs/run/evidence/f2_23/`, 1280 px; numbers in
`f2_23_showcase_metrics.json`): `30_land_campus_day` is the land framing,
`33_offshore_of1_winter_sun` the OF1 framing, `35_offshore_storm` the open sea
at Hs 4.8 m.

## What was built

| Piece | Where |
|---|---|
| Focus targets with their own yaw; both clamps take the targets' discs (F2-20); a sight-line clearance that tapers near the focus (`FOCUS_GROUND_M`), so the land framing is not lifted off its pitch by the ground it looks at; `_snap()` lands on the damped pitch (offset and top-down included); `cinematic_pose()` for F2-23 | `godot/scripts/environment/FjordCamera.gd` |
| Bookmarks: the input action `camera_bookmark` (G, physical keycode 71; rebindable, D15), `BOOKMARK_SLOTS` Fjord → Land → Hav, `cycle_bookmark()`, `bookmark_changed`; the hint's chip names the place | `godot/project.godot`, `FjordCamera.gd`, `godot/scenes/ui/FjordCameraHint.tscn`, `godot/scripts/ui/FjordCameraHint.gd`, keys `CAMERA_BOOKMARK_*`, `UI_FJORD_HINT` |
| Plot targets (`plot:<id>` for every pad and slot), the hero plots (pad_a, off_1) when the company has no unit, the company's own units first; framings: a campus at −40° from the side that keeps the rig in −35..−45° (up the fjord over the campus to the pens, else off the quay edge, else down the fjord), the OF1 at 220 m −18° with the service vessel on its far side, the Havfarm class at 700 m −16° | `godot/scripts/visual/FarmFacilityVisualManager.gd` (`land_framing`, `solved_pitch`, `_update_bookmarks`, `unit_heading`) |
| The engine's split of a plot's sea: wind sea `scale × base`, swell `scale × √(0.34² + storm swell²)` (the terms the model adds in energy), the swell's Tp | `src/LakskongenSim/Core/SimulationEngine.OffshoreSea.cs` (`OpenSeaPartsAt`, pure), bridge `GameBridge.Offshore.cs` (`today`: `wind_sea_hs`, `swell_total_hs`, `swell_tp`) |
| Swell bands 280 m and 160 m (beyond the wind sea's 128 m), JONSWAP-shared, Σa² = Hs²/8; the wind whose open-coast sea has a given Hs (bisection) | `src/LakskongenSim/Environment/SeaStateModel.cs` (`SwellBandWavelengthsM`, `SwellAmplitudes`, `WindForHeight`), `godot/scripts/environment/SeaStateBridge.cs` |
| The open sea at the target plot: wind bands scaled to the target's wind sea, two Gerstner components per swell band (detuned 0.95/1.055: 152-168 m and 265-295 m, 6° spread, from the WSW), eased toward a new target | `godot/scripts/environment/OpenSea.gd`; `EnvironmentDirector.gd` (`set_target_sea`, the only writer of `swell_k`/`swell_amp`) |
| The swell in the water: exposed sea only (sheltered amplitude 0), the water's own band limit and shallow fade; the CPU field carries the same term, so the units heave on it | `godot/shaders/water/water_swell.gdshaderinc`, `water_shader.gdshader`, `godot/scripts/environment/WaveField.gd` |
| The Hs chip: "Hs 2,4 m · Tp 10 s · i morgen 3,1 m · behandlingsvindu om 3 d" from the engine's forecast, within 1,000 m and wherever fog or sea fog hides the unit (transmittance < 0.4); status as colour and shape (circle, triangle, square), every colour ≥ 20 ΔE off the hull's #E6BE2E | `godot/scripts/visual/OffshoreUnitVisual.gd`, `VesselChip.gd`, `godot/shaders/world_chip.gdshader`, keys `FACILITY_CHIP_*` |
| Tests | `CameraSeaChecks.gd` (40), `OpenSeaSwellTests.cs` (12), `tests/test_camera_bookmarks.py` (6), `tests/test_facility_presenter.py` (the presenter's fourth read and its purity) |

## Acceptance

| # | Criterion | Measured | Result |
|---|---|---|---|
| 1 | Focus and camera position reach every slot and pad; the world clamp still holds elsewhere | All 6 plots (pad_a, pad_b, pad_delta, off_1, off_2, hav_1) reached by focus and camera, 4 of them beyond the world's focus clamp; away from the targets both clamps hold | Pass |
| 2 | The OF1 framing fills 25-45 % of frame width; the land framing at −35° to −45° shows the full campus | OF1 33.4 % of the width. The QA campus's framing −40.0° from 410 m, 8 of 8 corners in frame; every grow-out campus the pads allow (6) fits its framing with the rig at −35..−45°. Shot 30 (the full 4-lot campus): −40.0°, 588 m, 8 of 8 corners | Pass |
| 3 | The horizon row is 10-25 % from the top at the FJORD/COAST defaults and the offshore framings | FJORD 13.1 %, COAST 12.7 %, OF1 14.1 %, Havfarm class 15.9 % | Pass |
| 4 | At target Hs 4 m, Σa² = Hs²/8 ± 10 % across the wind and swell bands; swell energy at 150-300 m | Drawn: Σa² 2.000 m² (Hs²/8 = 2.000). The swell's 0.781 m² all in components at 152-295 m. xUnit at Tp 9-14 s: the swell bands' Σa² = Hs²/8 to 1e-9, both bands (280, 160 m) at 150-300 m and beyond the wind sea's 128 m; pytest: the detuned components at 152-295 m and the shader's component count = bands × 2 | Pass |
| 5 | Chip Hs = `GetSeaStateForecast(site).today.hs` ± 0.05 m | Chip "Hs 1,3 m" for the engine's 1.27 m (the open sea draws the same 1.27 m); the bridge's split adds up in energy to 1.271 m | Pass |
| 6 | G cycles Fjord → Land → Hav by UI input | The key pressed as a player does: Land (the company's campus, "Land · Storholmen"), Hav ("Hav · Ytre kyst"), Fjord ("Fjord · hjemmevisning", the home view at the FJORD default 900 m whatever framing G left); the caption stands above the camera hint at the bottom of the screen (windowed: `f2_21/hint_g*.jpg`); G typed into a text field stays text; G behind an opaque panel leaves the camera alone | Pass |
| — | Graphics leave the games identical | `qa_state_digest` unchanged by the framings, bookmarks, the open sea and the chip (UI check); `record_golden_traces.sh --base 1bea464` on `8c5e5ae`: 27 of 27 traces identical, math fingerprints equal (`f2_21_f2_23_golden_diff.md`) | Pass |

The units ride the swell: the CPU field's height adds the shader's swell term
(−0.109 m against −0.109 m expected at the probe); the sheltered fjord gets
none of it; with no target the open coast draws no swell.

## Mutation checks

| Mutation | Caught by |
|---|---|
| The chip shows the engine's Hs + 0.1 m | CameraSeaChecks: "the chip reads the engine's Hs (Hs 1,4 m … engine 1.27 m)" |
| The bookmark order Fjord → Hav → Land | CameraSeaChecks: G takes the camera to land / hav (2 failures); `test_camera_bookmarks.py` |
| The caption's box at the top of the screen (under the HUD's bars) | CameraSeaChecks: "the caption shows on screen above the camera hint … (883, −127)" |
| G's Fjord step keeps the zoom band (Home's recentre alone) | CameraSeaChecks: "G takes the camera to fjord" |
| The drawn wind sea scaled 1.2 | CameraSeaChecks: "Σa² 2.536 m² (Hs²/8 = 2.000, ±10 %)" |
| The swell spectrum fed Hs × 1.2 | `OpenSeaSwellTests`: 5 of 12 fail |
| The engine's split drops the background swell | `OpenSeaSwellTests.TodaysSea_SplitsIntoWindSeaAndSwell_AsAPureRead` |

## Decisions (RUN_LOG §7, 2026-10-21)

- The Havfarm-class framing is 700 m at **−16°**, not the table's −25°: at
  −25° and the 700 m view's 45.6° field of view the horizon sits above the
  frame (the §4 horizon rule's 10-25 % fails); −16° puts it at 15.9 % with the
  whole 385 m hull in frame. Spec §4's table carries the amendment.
- The land framing's yaw is chosen per campus: from the mouth side up the
  fjord over the campus (the pens behind it), else off the quay edge, else
  down the fjord, whichever keeps the rig's solved pitch in −35..−45° (from
  the fjord's axis the rig stood over the hills behind pad_a, and the terrain
  avoidance lifted it to −64..−71°).
- The status chip's colours are #7FD69B, #F29A3C and #FF8A80 (64, 28 and
  63 ΔE off the hull's #E6BE2E; spec §5.5 rule 6 asks ≥ 20), with a shape
  each (circle, triangle, square), on an Ink pill.
- The chip shows within 1,000 m of a unit (the OF1 framing is 220 m) and
  wherever the fog's transmittance at the unit is under 0.4 or sea fog lies.

## The swell's cost (wave4b int review, merge of gfx-farm-2)

The stage's perf A/B hid the whole presenter and drew the swell in both
variants, so the swell's own cost was not in it. The review measured it: with
every swell amplitude 0 (no offshore unit) the swell's loops still ran on
every water vertex, +0.33-0.50 ms p50 at 1080p High. Since the merge the
water reads `swell_peak`, the largest swell amplitude (`EnvironmentDirector`,
0 with no swell), and a vertex skips the loops when exposure × swell_peak is
under 0.1 mm (`water_swell.gdshaderinc` `swell_skips`): the same skip each
component already made, made before the loops, so nothing drawn changes.

In-run paired A/B (`tools/perf_smoke.sh`, windowed 1920x1080, High, 12 sea
sites, 8 s segments, 3 repeats, `--paired`; the raw override
`water.swell_peak` set after the director's own write):

| Game | Segment | Variant against the default | Δp50 | p50 ratio | Load |
|---|---|---|---:|---:|---|
| No offshore unit | FJORD | the loops forced on (`swell_peak` 1e6, as before the gate) | +0.23 ms | 1.016 | 4.2 |
| No offshore unit | FARM | the loops forced on | +0.46 ms | 1.032 | 4.2 |
| The farm-type set (OF1 and Havfarm class) | FJORD | no swell drawn (`swell_peak` 0) | −0.32 ms | 0.982 | 4.8-6.9 |
| The farm-type set | hav (220 m, −18°) | no swell drawn | −0.36 ms | 0.974 | 4.8-6.9 |
| The farm-type set | FJORD | the exposure skip off (`swell_peak` 1e6) | +0.01 ms | 1.001 | 4.8-6.9 |
| The farm-type set | hav | the exposure skip off | −0.03 ms | 1.002 | 4.8-6.9 |

So a game without an offshore unit no longer pays for the swell (the gate
saves the review's +0.2-0.5 ms); a game with one pays +0.32-0.36 ms p50 for
the swell it sees (the open coast heaves on it from the home view too), within
F2-20's 1.5 ms presenter budget. The exposure skip (where the bake's exposure
is near 0) saves nothing measurable at these two views; it costs nothing either.
A first run without units at load 13.6 → 7.3 gave the same sign (+0.82 /
+1.23 ms p50, ratios 1.025 / 1.045, p95 noisy). Reports:
`docs/run/evidence/perf/perf_wave4b_int_swell_gate_nounits.md` and
`perf_wave4b_int_swell_gate_units.md` (with their JSON).
