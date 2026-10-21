# G3-04 evidence: the underwater view

Branch `wave4b/gfx-polish-2`. Frames from `bash tools/capture_screenshots.sh
-- --only=underwater` (1920x1080, Forward+ on Metal, Apple M1 Pro), JPEG at
1280 px. The harness's report is `11_underwater.json` (copied here); its
content checks passed (`"failures": []`).

| Frame | What it shows |
|---|---|
| `g3_04_sheet.jpg` | The old 11 (a blue overlay over a net) against the five new frames |
| `11_fjord_underwater.jpg` | 5.0 m down, 17.96 m from the pen's centre (net 24.95 m): Snell's window at the top, shafts, snow, the school (3.3 % of the frame), the net's ropes |
| `11c_underwater_window.jpg` | Tilted 45° up: the window 2.42× as bright as the surface outside it (TIR ring rgb 0.054, 0.299, 0.275: the water's own tint), the sun and its glow, a cloud through the window |
| `11b_underwater_bloom.jpg` | A QA bloom of 0.8 at the site: 2.35 m water (fog 0.839 /m against the clear 11.53 m, 0.269 /m), olive-green |
| `11d_underwater_net.jpg` | 2.6 m from the net at 4 m, the sun behind the camera: the net, the shafts converging below |
| `11e_underwater_net_fouled.jpg` | 11d with the site's nets fully fouled (41 % of the frame changes) |
| `11e_fouling_crop.jpg` | The same 640 x 360 crop of 11d and 11e at 1:1: ragged olive growth closing the mesh |
| `11d_caustics_motion.jpg` | Time stopped, the fish, shafts and snow held off: the net; |dL| × 10 for 0.5 s on the caustics' clock on the clean net (mean 0.0039 over the net's pixels), on the fouled net (0.0086) and with the caustics held off (0) |
| `11_before.jpg`, `11_wip_first_light.jpg` | The base's 11; the first light of the new composition (earlier commit) |

## The checks (UnderwaterShots.gd)

| Check | Bar | Measured |
|---|---|---|
| 11: the camera 3-8 m down inside the net | depth 3-8, < 24.95 m from the centre | 5.0 m, 17.96 m |
| 11c: Snell's window over the TIR ring, tinted | ≥ 1.8×, green over red | 2.42× (2.38 in earlier runs), g 0.299 > r 0.054 |
| 11b: the bloom's fog over the clear water's | shorter visibility, denser fog | 2.35 m / 0.839 against 11.53 m / 0.269 |
| 11: fish | ≥ 1 % of the frame | 3.31 % (2.97-3.29 in earlier runs) |
| 11d: the caustics animate on the net (time stopped, 0.5 s on their clock) | clean ≥ 0.002, fouled ≥ 0.005, held off ≤ 0.0005 | clean 0.0039 (p99 4.2, max 12 levels on 18 % of the frame), fouled 0.0086 (p99 8.2, max 30 on 32 %), held off 0.0000; the same in three runs |
| 11e: the fouling uniform reads | ≥ 10 % of the frame | 40.9 % |

The first version measured two real-time frames 0.5 s apart against a twin
taken later: 0.020 against 0.005. That was an artifact: the pair came right
after the frozen mask frames and caught the scene catching up (a real-time
pair later in the same run read 0.006); in another run, with the farm shot
before it, it failed (0.009 against 0.009). Measured frozen, the caustics'
own motion on the clean, dark net is the faint shimmer above.

## Cost (`perf_uw.md`)

`tools/perf_smoke.sh --segments=farm,underwater
"--variants=high;high,hide.UnderwaterView=true" --repeats=3 --paired`,
2560x1440 High, 8 s a segment, load 9-14 (other agents' runs): the
underwater view p50 14.4 ms, p95 15.2 ms against FARM's 20.3 / 23.3 ms in the
same run (266 draws against 310, 75 shadow draws against 107); the shafts
and snow cost 0.66 ms p50 (paired ratio 0.952 with the view hidden).

The UI side of a 30× day (UiFlowTests' check run alone, A/B in one time
window, load 13-21): with the fouling written every day it cost 1.0 ms p50
more (4.13-4.16 against 3.06-3.19 ms with the per-day additions off); stepped
(0.05) and with the nets found once it costs 44 µs a day (3.02 ms p50; 38
writes in 74 days of the po3 game).

## Tests

- `godot/tests/UnderwaterChecks.gd`: 31 UI-flow checks (`MIN_CHECKS` 1794 →
  1825). Mutation runs, each caught: the fouling's 60 % moved to 50 %, the
  camera's ring outside the net (1.05), the probe left on under water, the
  staging ignored (one run, 4 failures); the fog from the fallback water,
  the fish's reach fixed at 400 m, the site kit without the fouling, the
  site's chlorophyll dropped (one run, 5 failures); the fouling's step 0
  (40 writes in 40 days).
- `tests/test_underwater.py`: 6 checks (the underside's vertex stage is the
  water's, the old post gone and the lens reads no screen, the globals and
  their one writer, the caustics' loop rebuilt byte for byte, the caustics
  without a second 1/π, a fully fouled tile's coverage against the shader's
  constant). Seven mutations, each caught by its test.
- The harness's 11d check against a net shader without its caustics'
  emission: it fails (0.0000 clean, fouled and held off).
- `tests/test_environment_director.py`: the single-writer lists take the
  underwater sky's and the underside's uniforms.
- `src/LakskongenSim.Tests/Environment/WaterClarityTests.cs`: 10 xUnit tests.
