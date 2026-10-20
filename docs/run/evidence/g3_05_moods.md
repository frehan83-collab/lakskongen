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
| 1a | Eight mood goldens pairwise distinct (pHash Δ ≥ 9) | min **12** (Blåtime/Høst, Gråvær/Frostrøyk) | `MoodGoldens.gd`, pHash 64-bit DCT on 32x32 luma |
| 1b | Sky and water mean within ΔE 10 of spec | every mood, worst sky 8.4 (Midnattssol, Høst), worst water 9.6 (Midnattssol); the owner's overcast grey water #5E666C at **3.6** (1.6-3.7 across runs), sunny deep blue 8.1, slate mirror 6.8 (Blåtime) | CIEDE2000 (checked against Sharma et al. 2005 test pairs) on the sky and on the water within 2 km, pens excluded, against `EnvironmentMood.SPECS` |
| 2 | Night shot: emissive pen lights ≥ 0.3 % of the frame | **1.5 %** (Nordlys golden: the frame against itself with the lamps off, ≥ 4 L*) | `MoodGoldens._lamp_fraction` |
| 3 | Cloud shadows move (temporal variance on land) | **0.79** mean \|dL*\| on the land in 12 s with the shadows, 0.20 without (4.0x; min 0.5 and 3x) | `MoodGoldens._shadow_motion`, frames `20_mood_sommerdag_shadows_t0/t1.png` |
| 4 | Storm Hs ≥ 2 m at exposed sites | **2.58 m** at the golden's 16 m/s, **2.10 m** at the sim's weakest storm (13 m/s); the drawn sea (WaveField) 2.08 m | `MoodGoldens`, `WeatherDramaChecks._check_storm_sea` |
| + | The storm keeps the mountains' outline | skyline **16.0 dL*** off the sky (min 15; sky 58.4 over land 42.8 L*); was a murk (mean luma 60, the coast lost) | `MoodGoldens.skyline_contrast`, `storm_before_after.jpg` |
| + | Every golden is the sim's own day | each reads as its mood with weight ≥ 0.9 from `GameBridge.SkyContextFor` (regime, wind, sea temperature, day, latitude) at an hour | `WeatherDramaChecks._check_moods_from_the_sim` |

The full harness (`tools/capture_screenshots.sh`, 38 shots) passes every content check,
including 09 (aurora 12.9 %, mirrored 4.0 %), 18 (mirror SSIM 0.648 against 0.515 for the
sky's reflection alone) and 19 (shore foam 18.9 % / 0.0 %).

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

The machine was shared with other agents' Godot captures throughout, so the stage is
measured against the stage-2 build itself (`git archive 5004f7b`, built and imported in a
scratch directory), the two alternated in one session, 6 s per segment:

| Segment | stage 2 p50 / p95 ms | this branch p50 / p95 ms | Δ p50 |
|---|---:|---:|---:|
| FARM | 15.05 / 15.87 | 15.73 / 17.23 | +0.68 |
| FJORD | 15.27 / 16.06 | 16.63 / 17.76 | +1.36 |
| COAST | 13.99 / 15.45 | 15.53 / 17.23 | +1.54 |
| panel (world hidden) | 8.18 / 8.63 | 8.89 / 9.44 | +0.71 |

**PERF_GATE WARN** on this machine (p95 FARM 17.2, FJORD 17.8, COAST 17.2 > 16.7 ms; the
gate is warn-only until the Phase 1 exit). Where the time goes (alternated or paired runs,
p50 at FJORD):

| Item | Cost | Done |
|---|---:|---|
| the water's lamp glow and frost smoke in the fragment stage (register pressure; off on most days) | 0.8-1.0 ms | moved to the vertex stage: **0** |
| the fine cloud march in the half pass, the radiance map and the mirror's sky | 0.8 ms | 4 bisections, 3 steps in, one sun look; the radiance map and the mirror's sky on the plain march; High searches at 2x: **0.26 ms** |
| cloud shadows (the Decal over the view; the water's own fetch costs nothing) | 0.6-0.8 ms (COAST 1.0-1.4) | knob `cloud_shadows` (off on Low); the decal's normal fade is free |
| the rest (director, moods, chips) | ~0.3 ms | — |

The open item is the cloud shadows' decal: Godot evaluates a decal per land fragment in
every cluster it covers, and this one covers the view. The water already reads the same
map through `cloud_shadow_at()` for nothing measurable; the terrain shader reading it the
same way (the gfx-terrain track's file) would retire the decal and close the gap to the
budget.
