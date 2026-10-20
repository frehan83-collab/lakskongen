# P0-04 performance smoke test, frame budget and quality tiers

Wave 2, gfx-water track stage 1 (branch `wave2/gfx-water-1` from `cf007e3`), 2026-10-14.
Items: P0-04 (performance smoke test), G5-01 groundwork (quality tiers through the
EnvironmentDirector), the G1-06 remainder (render scale and a paused sky behind opaque
panels, the ≥ 40 % cut on shot 06), the sky raymarch tiering and the terrain pipeline
warm-up.

## Method

- Machine: MacBook Pro M1 Pro (16-core GPU, 16 GB), Metal 3.2 Forward+, Godot
  4.7.2-stable mono. Window 2560×1440 pixels (`--windowed --resolution 2560x1440`; the
  3D renders at the window's pixel size), vsync off, no frame cap.
- Harness: `godot/scripts/tools/PerfHarness.gd` (scene `res://scenes/tools/PerfHarness.tscn`),
  wrapper `tools/perf_smoke.sh`, report and gate `tools/perf_report.py`. It boots the
  seeded QA scenario (`po3_red_sone`, seed 91), stages a fixed summer noon with clear
  weather (sim and clock paused), focuses the first pen and records four segments of
  10 s: FARM, FJORD and COAST, each a 50° yaw sweep round the home view with the HUD on
  the Fjord tab, and the finance panel open over the world (shot 06).
- Metrics per segment: p10/p50/p95/p99 frame time, script time (process → pre-draw),
  render submission (pre-draw → post-draw), draw calls, shadow draw calls, primitives,
  video memory, and the boot timeline (every frame until the terrain is up, with the
  terrain upload step each frame carried). Metal returns no GPU timestamps in Godot 4.7
  (`viewport_get_measured_render_time_gpu` is 0), so the frame time is the measure; the
  frame is GPU-bound (script ≈ 0.8-1.0 ms of a 14-30 ms frame).
- The machine is shared with other agents (other Godot instances, Blender): their GPU work
  slows every frame of ours. So variants run interleaved in one process, or `--paired`
  (each variant right after a reference block, the ratio taken per pair); reports show
  the median over repeats and the least disturbed repeat ("best"). A quiet Ultra/baseline
  frame is very stable (28.0-30.3 ms in every quiet block of the session), which makes
  disturbed blocks easy to spot.

Reproduce:

```bash
tools/perf_smoke.sh --repeats=3                               # High (default tier), the gate
PERF_LABEL=tiers tools/perf_smoke.sh --repeats=3 --variants="ultra;high;medium;low"
PERF_LABEL=ab tools/perf_smoke.sh --paired --repeats=6 --seconds=2 --variants="high;high,scale=0.7"
```

## Baseline (cf007e3, before this branch)

`perf/perf_baseline.json` / `.md`: the base tree exported with `git archive cf007e3 godot`
plus this harness (which runs on pre-tier builds), 3 × 10 s per segment.

| Segment | p50 ms | p95 ms | best p50 / p95 | draws | shadow draws |
|---|---:|---:|---:|---:|---:|
| FARM | 28.21 | 29.32 | 28.09 / 28.72 | 185 | 48 |
| FJORD | 30.30 | 32.60 | 29.35 / 31.55 | 212 | 52 |
| COAST | 29.36 | 30.86 | 28.05 / 29.49 | 265 | 92 |
| Finance panel open (06) | 31.09 | 41.96 | 29.61 / 30.13 | 300 | 51 |

The world behind an opaque panel cost as much as the world on screen.

## Where the 30 ms went

`perf/perf_attribution.json` (paired, 7 pairs, FJORD, the Wave-1 image = Ultra): ratio of the
frame with one thing removed to the frame with everything.

| Removed | Frame ratio | Note |
|---|---:|---|
| MSAA 4× | 0.77 | multisampled colour, depth and normal buffers and their resolves |
| SSAO | 0.83 | on Metal it forces a depth + normal-roughness prepass of all geometry |
| Sun shadows (4 cascades) | 0.86 | |
| Water | 0.91 | |
| Sky background (clouds marched per pixel) | 0.91 | |
| Glow + SMAA | 0.94 | |
| Pens, fish and boats | 0.98-1.0 | |
| Terrain | 1.0 | the sky and water it hides cost as much |
| 3D at half scale | 0.42 | ≈ 23 % of the frame does not scale with pixels (geometry, shadow passes) |
| Depth of field (`perf_dof.json`: High at 0.8 with DOF at every zoom; `perf_high_options.json` for FARM) | 0.89 (FJORD), 0.85 (COAST), 0.84 (FARM) | at FJORD/COAST indistinguishable on or off (`p0_04_cmp_dof.jpg`) |

On the High candidate, paired against it (`perf/perf_high_savings.json`): 2 cascades −2 %,
0.75× shadow distance 0 %, cheaper clouds 0 %, terrain detail textures 0 %
(`terrain.use_textures=0`), anisotropy 1× 0 %, 3D scale 0.8 → 0.75 −7 to −9 %, no depth of
field at FARM −7 %.

## Quality tiers (this branch)

`RenderQuality.gd`; table in `docs/FJORD_RENDERER.md` § Quality tiers. High is the default.

`perf/perf_high.json` / `.md` — the gate run (`tools/perf_smoke.sh --repeats=3`, High):

| Segment | p50 ms | p95 ms | p99 ms | vs baseline p95 |
|---|---:|---:|---:|---:|
| FARM | 14.91 | **15.86** | 16.82 | −46 % |
| FJORD | 13.84 | **15.00** | 15.88 | −54 % |
| COAST | 13.32 | **14.78** | 16.16 | −52 % |
| Finance panel open (06) | 7.93 | 8.55 | 8.97 | −74 % p50 (31.09 → 7.93) |

`PERF_GATE PASS: p95 <= 16.7 ms on farm,fjord,coast` (final High, clouds in the half pass;
the same gate run with the quarter pass measured 16.15 / 14.98 / 14.33 ms). FARM has the least margin (the pen,
its net and the work boat fill the frame and depth of field stays on there): the Wave-2
site grid (G2-04/A2-01: several pens, hoses, bird nets) must be measured against this
budget when it lands.

`perf/perf_high_vs_ultra.json` (paired, 10 pairs of 2 s): High/Ultra 0.46 at FJORD (13.70
vs 29.62 ms median), 0.48 at COAST, 0.52-0.63 at FARM (FARM blocks were disturbed in that run;
the quiet `perf_series` run had 14.79/16.41 and 15.61/16.57 ms p50/p95 against Ultra's 28.14).

`perf/perf_tiers.json` / `.md` (all tiers, 3 × 5 s, interleaved, High with the clouds still in
the quarter pass; some blocks disturbed, see the best column): best p50 / p95 —

| Segment | Ultra | High | Medium | Low |
|---|---|---|---|---|
| FARM | 28.75 / 29.50 | 15.05 / 15.85 | 11.52 / — | 7.02 / 7.50 |
| FJORD | 29.43 / 30.26 | 13.59 / 14.43 | 10.48 / 11.04 | 6.79 / 7.30 |
| COAST | 28.00 / 30.08 | 13.16 / 14.23 | 10.06 / 10.88 | 6.81 / 7.28 |
| Panel | 11.26 / 11.57 | 7.68 / 8.16 | 6.88 / 7.34 | 5.80 / — |

(— : that tier's least disturbed block still had a disturbed tail.) A storm at FJORD: Ultra
30.13 ms, High 14.03 ms p50 / 15.77 ms p95 (`p0_04_final_storm.jpg`).

### The look at High

Frames from the harness at 2560×1440, crops at 1:1 (Ultra top left, High top right, Medium
bottom left, Low bottom right): `perf/p0_04_final_farm.jpg`, `perf/p0_04_final_fjord.jpg`,
`perf/p0_04_final_coast.jpg`; Ultra and High in a storm: `perf/p0_04_final_storm.jpg`.

- FJORD and COAST: High and Ultra are indistinguishable at 1:1 (islands, shoreline foam,
  labels, pen rings, the work boats). Ultra's depth of field softens the far COAST
  mountains a hair more; the haze does nearly all of it.
- FARM: the boat, the rails and stanchions and the buoys read the same. The net inside the
  pen shows a finer dark mesh on High than on Ultra (alpha-to-coverage resolved at 0.75
  scale and upscaled by MetalFX); over a 16-frame slow orbit (`--shots --burst=16`) the net
  region's mean frame-to-frame change, normalised by open water's, is 0.81 on High against
  0.60 on Ultra (+35 %; +26 % at 0.8 scale). It reads as the net hanging in the pen (owner
  ref 4), with a little more shimmer in motion; the net shader can take that back (follow-up:
  widen its fade to mean coverage by the 3D scale).
- Clouds: by day the half and quarter passes keep the clouds' shape and soft edges
  (`perf/p0_04_cmp_cloud2.jpg`: broken cumulus per pixel, half and quarter pass, and an
  overcast deck); mean sky luminance within 0.4 / 1.5 / 0.1 (of 255) of the per-pixel
  march for overcast / broken / clear, after the engine's double sky fog is taken out
  (before that correction the reduced pass read +4 to +14 brighter). At night the dark
  cumulus silhouettes against an aurora showed the quarter pass's blocks in shot 09, so High
  uses the half pass: `perf/p0_04_night_clouds.jpg` (Nordland winter night, 22:00, Ultra per
  pixel top left, High half pass top right, quarter pass bottom) — High matches Ultra,
  including the horizontal streaks the per-pixel march already draws at the horizon. The
  half pass costs +1.0 to +1.5 % over the quarter pass (`perf/perf_cloud_pass.json`, paired).
- Medium shows the net as a coarse dot grid (MSAA 2×) and Low drops it (no MSAA): those
  tiers are for smaller GPUs.

## Behind an opaque panel (G1-06 remainder)

With any tab but Fjord open and the UI layer drawn, the 3D renders at half scale
(bilinear) and the sky material holds still, so its radiance map is not re-rendered.
`perf/perf_panel_ab.json` (paired, 6 pairs, High): 18.73 → 7.68 ms p50 (−59 %) against the
same tier at native scale without the pause; against the Wave-1 build 31.09 → 7.93 ms (−74 %,
the final gate run). The
roadmap's acceptance (GPU time on 06 drops ≥ 40 %) is met. The thin margins of world that
show round the panel are blurred by the scale, which nobody can see at 8-16 px.

## Boot and the pipeline warm-up

`ShaderPrewarm` draws a millimetre triangle with the terrain shader and the terrain chunks'
own vertex arrays in front of the camera on the first two frames. Boot A/B, 4 runs each
(`--boot-only`, `--no-prewarm`), warm shader cache: the frames around the first terrain
chunk peaked at a median 57 ms with the warm-up and 98 ms without, but single runs ranged
39-543 ms on the shared machine, so the difference is not significant at n = 4. With a warm
shader cache the boot is ~4 frames of 50-130 ms (scene load, behind the dashboard), then
steady. The worst frame after the first ten: 16.7-18.3 ms in the final runs (it was 39-75 ms
in the baseline runs). The first frame of a cold shader cache (first launch after an
install or a shader change) is still a long compile; that needs a loading screen or the
main menu to warm the pipelines (follow-up).
