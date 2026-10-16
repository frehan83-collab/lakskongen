# G2-03 water v2: evidence

Branch `wave2/gfx-water-2` (gfx-water stage 2). Frames are 1920x1080 renders
on an M1 Pro (Metal, High tier unless named), cropped to JPEG in `g2_03/`.
The design is described in `docs/FJORD_RENDERER.md` ("Water v2"); decisions
and their numbers are in `docs/RUN_LOG.md` section 7 (2026-10-15 rows).

## Acceptance (roadmap G2-03)

| # | Criterion | Result | How |
|---|---|---|---|
| 1 | In `18_fjord_calm_dusk` the reflection band has SSIM >= 0.5 with the vertically flipped skyline | **0.624** (High); the same frame with the sky's reflection alone (Low): 0.441, so the mirror lifts it by 0.183 (harness also requires >= 0.1) | `tools/capture_screenshots.sh`, `ScreenshotHarness._capture_fjord_calm_dusk` / `mirror_ssim` |
| 2 | Reflection updates at 30 Hz at COAST; the Low tier falls back to the probe | High: 60 of 60 frames at FJORD, 30 of 60 at COAST; Low: no mirror, the water's `planar_mix` 0 | UiFlowTests `_test_water_mirror_wakes_and_spray` |
| 3 | \|object y - wave(x, z)\| <= 0.1 m for pens and boats | unchanged and green (pen 0.009 m, vessels 0.018 m in a 25 m/s storm) | UiFlowTests `_check_waves_carry_pens_and_boats` |
| 4 | Foam present where the shore SDF is < 5 m | 19: foam-white on 20.0 % of the water within 5 m of the shore, 0.0 % 15-40 m out | `ScreenshotHarness._capture_fjord_shore_foam` |
| 5 | p95 within the budget (16.7 ms at 2560x1440, High) | FARM 16.05, FJORD 16.14, COAST 15.31, panel 8.56 ms: PERF_GATE PASS | `PERF_LABEL=g2_03_high ./tools/perf_smoke.sh --repeats=3` -> `perf/perf_g2_03_high.md` |

The art review's "night water is a black void": 09 now mirrors the aurora on
10.9 % of the frame (harness check, min 1 %); the cause was the engine's
specular occlusion (ambient-scaled reflections), off for the water now.

## Mirror cost (paired A/B, 2560x1440, 3 repeats)

`perf/perf_g2_03_mirror_ab.md`: p50 FJORD +1.26 ms (x1.089), COAST +0.72 ms
(x1.053, 30 Hz), FARM 0 (High draws it from 300 m). Steps that got it there,
measured at FJORD in the same harness: sharing the main sky with shadows
4.7 ms; its own quarter-pass sky 2.8 ms; its own shadowless lights 1.9 ms;
0.4 of the window 1.4 ms.

The water shader's other changes cost +0.2-0.4 ms p50 at FARM/FJORD against
the stage-1 build (`git archive 2b37a43`, runs alternated three times) after
moving the peak-band weights to the director (they cost 0.45 ms per pixel
as log2/exp) and slimming the foam and mirror fetches.

Storm (16 m/s, 1440p, not a gate segment): stage-1 build p95 FARM 16.07,
FJORD 14.70, COAST 13.71; this branch 16.26, 14.81, 13.67 ms (the whitecaps'
spent foam, halo, gust field and wind streaks; the spray costs ~0.1 ms; no
mirror in a storm). Before cutting the large breaker's cross-fade and the
coast's extra field the storm read 17.13 / 15.91 / 14.56.

## Frames

| File | What |
|---|---|
| `g2_03/calm_dusk_low_vs_high.jpg` | calm winter dusk, Low (sky radiance only) against High (planar mirror: clouds, islands, walls) |
| `g2_03/night_aurora_low_vs_high.jpg` | calm aurora night, Low against High: the aurora in the water |
| `g2_03/18_fjord_calm_dusk.jpg` | the SSIM shot: up the fjord to its head, the walls mirrored |
| `g2_03/09_fjord_night_aurora.jpg` | the gameplay aurora night (Nordland, sim weather) |
| `g2_03/sunny_glints.jpg` | 6 m/s chop toward a low sun: the glitter path (ref 1) |
| `g2_03/08_fjord_day.jpg` | the default day: cloud reflections, the shoreline's foam line |
| `g2_03/10_fjord_storm.jpg` | storm at FJORD: whitecaps in groups, not bands of flecks |
| `g2_03/storm_farm_spray.jpg` | storm at FARM: caps with spent foam, spray off the collar |
| `g2_03/wake_fjord.jpg` | a workboat at 7 m/s: the wash and the Kelvin wedge |
| `g2_03/19_fjord_shore_foam.jpg` | the shore-foam shot, straight down |
