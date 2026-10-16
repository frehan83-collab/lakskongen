# G3-05 weather drama and the Nordic night: evidence

Branch `wave2/gfx-water-3` (gfx-water stage 3). Frames are 1920x1080 renders on an
M1 Pro (Metal, High tier), scaled to JPEG in `g3_05/`. The design is described in
`docs/FJORD_RENDERER.md` ("Moods and weather drama (G3-05)"); decisions and their numbers
are in `docs/RUN_LOG.md` section 7 (2026-10-16 rows). The raw report of the last run is
`g3_05/moods.json` (`godot/screenshots/game/20_moods.json`).

Reproduce: `bash tools/capture_screenshots.sh` (all shots, the goldens included), or the
goldens alone: `godot-mono --path godot res://scenes/tools/ScreenshotHarness.tscn -- --only=moods`.

## Acceptance (roadmap G3-05)

| # | Criterion | Result | How |
|---|---|---|---|
| 1a | Eight mood goldens pairwise distinct (pHash Δ ≥ 9) | min **12** (Blåtime/Høst, Sommerdag/Høst, Gråvær/Høst) | `MoodGoldens.gd`, pHash 64-bit DCT on 32x32 luma |
| 1b | Sky and water mean within ΔE 10 of spec | every mood, worst sky 8.5 (Midnattssol), worst water 9.6 (Midnattssol); the owner's overcast grey water #5E666C at **1.6**, sunny deep blue 8.1, slate mirror 6.8 (Blåtime) | CIEDE2000 (checked against Sharma et al. 2005 test pairs) on the sky and on the water within 2 km, pens excluded, against `EnvironmentMood.SPECS` |
| 2 | Night shot: emissive pen lights ≥ 0.3 % of the frame | **1.6 %** (Nordlys golden: the frame against itself with the lamps off, ≥ 4 L*) | `MoodGoldens._lamp_fraction` |
| 3 | Cloud shadows move (temporal variance on land) | **0.82** mean \|dL*\| on the land in 12 s with the shadows, 0.18 without (4.5x; min 0.5 and 3x) | `MoodGoldens._shadow_motion`, frames `20_mood_sommerdag_shadows_t0/t1.png` |
| 4 | Storm Hs ≥ 2 m at exposed sites | **2.58 m** at the golden's 16 m/s, **2.10 m** at the sim's weakest storm (13 m/s); the drawn sea (WaveField) 2.08 m | `MoodGoldens`, `WeatherDramaChecks._check_storm_sea` |
| + | The storm keeps the mountains' outline | skyline **15.4 dL*** off the sky (min 15); was a murk (mean luma 60, the coast lost) | `MoodGoldens.skyline_contrast`, `storm_before_after.jpg` |
| + | Every golden is the sim's own day | each reads as its mood with weight ≥ 0.9 from `GameBridge.SkyContextFor` (regime, wind, sea temperature, day, latitude) at an hour | `WeatherDramaChecks._check_moods_from_the_sim` |

The full harness (`tools/capture_screenshots.sh`, 38 shots) passes every content check,
including 09 (aurora 12.9 %, mirrored 4.0 %), 18 (mirror SSIM 0.648 against 0.524 for the
sky's reflection alone) and 19 (shore foam 18.7 % / 0.0 %).

## What changed in this stage, and why it shows

| Frame | What |
|---|---|
| `g3_05/moods_sheet.jpg` | the eight goldens |
| `g3_05/clouds_sommerdag_before_after.jpg`, `clouds_blaatime_before_after.jpg` | the low cloud slab: stacked horizontal slices (the 2D noise sampled at fixed distances) against the bisected march |
| `g3_05/storm_before_after.jpg` | the storm: a dark murk with the coast lost, against a readable dark sea under a lighter far deck, scud on the walls, slanting rain |
| `g3_05/graavaer_before_after.jpg` | the grey day (owner ref 3): low cloud on the walls, the far fjord fading into the haze, grey water at ΔE 1.6 of the owner's hex |
| `g3_05/frostroyk_before_after.jpg` | sea smoke: brush strokes against puffs with clear water between |
| `g3_05/mist_fjord.jpg`, `mist_coast.jpg` | the grey day's mist at FJORD, and clear at COAST (the camera above the deck: the farm stays in sight) |
| `g3_05/08_fjord_day.jpg`, `09_fjord_night_aurora.jpg`, `10_fjord_storm.jpg`, `18_fjord_calm_dusk.jpg` | the harness's own shots after the fog-colour fix |

The fog-colour fix (`EnvironmentDirector.encoded()`): the engine takes
`Environment.fog_light_color`, `ambient_light_color` and every `source_color` uniform as
sRGB (probe: a fog colour of 0.5 renders 0.498 sRGB). The director wrote scene-linear
values raw, so a linear 0.18 arrived as 0.027 and every overcast, storm, dusk and night fog
was 4-10x too dark; the measured fog light per golden (`fog_light` in `moods.json`) now
reaches the engine unchanged (UI flow check). With the fog at its true luminance the sim's
1.65 km storm visibility whited out the farm, so the drawn fog keeps a 2.5 km floor
(G1-04's storm veil, ≤ 35 % of the FJORD focus visible, still holds: 27 %).

## Tests

- `godot/tests/WeatherDramaChecks.gd` (from UiFlowTests): 39 checks. Mutation run
  (encoding off, readability floor off, mist never clears, banks 200 m off their contours,
  chip uncompensated, no horizon lift): 7 checks fail, the rest pass; restored 39/39.
- `tests/test_moods.py`: 19 static guards (palette sources, thresholds, single writer,
  shader hooks, encoded colours, the floor, the sky's march, mist banks, chips, settled QA
  moods, this evidence).
- `bash tools/run_ui_flow_tests.sh`: `UI_FLOW_RESULT checks=772 failures=0` (MIN_CHECKS
  733 -> 772).

## Performance (P0-04 harness, 2560x1440, High)

The machine was shared with other agents' Godot captures throughout (the panel segment,
which none of this touches, ran 9.7 ms against stage 2's 8.56 ms), so absolute numbers
are high; the costs below are paired ratios inside one run (`--paired`, 4 pairs x 3 s).

| Segment | cloud shadows on/off (p50) | 3x first-cloud search vs 1x (p50) |
|---|---:|---:|
| FARM | x1.04 | x1.00 |
| FJORD | x1.04 | x1.03 |
| COAST | x1.06 | x1.00 |
| panel (noise floor) | x1.04 | x1.04 |

High takes the search at 2x (`cloud_search` 2; clouds checked for slices), Low has neither
(`cloud_search` 1, `cloud_shadows` false). Mist banks, frost smoke and lamps cost nothing on
the gate's clear noon (hidden, one uniform test). Gate result on this machine: see the
final row of the stage report (`PERF_GATE` lines).
