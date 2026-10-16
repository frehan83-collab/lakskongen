### ab6 — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-14T19:08:41

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fjord | ultra | 30.99 | 33.28 | 35.71 | 37.73 | 29.87 / 31.59 | +0.00 | +0.00 | 212 | 52 | 0.82 | 63 |
| fjord | ultra,hide.WaterGrid=true | 26.31 | 27.36 | 28.98 | 31.25 | 26.68 / 51.79 | -4.68 | -5.92 | 207 | 52 | 0.84 | 7 |
| fjord | ultra,hide.Terrain=true | 29.55 | 33.73 | 54.96 | 55.63 | 30.06 / 57.78 | -1.44 | +0.45 | 159 | 15 | 0.87 | 7 |
| fjord | ultra,env.background_mode=1 | 28.82 | 34.28 | 47.04 | 54.40 | 27.66 / 28.26 | -2.17 | +1.00 | 212 | 52 | 0.90 | 7 |
| fjord | ultra,sun.shadow_enabled=false | 25.80 | 28.41 | 41.11 | 42.35 | 25.86 / 26.82 | -5.19 | -4.87 | 161 | 0 | 0.76 | 7 |
| fjord | ultra,ssao=false | 25.87 | 27.73 | 28.80 | 28.89 | 25.30 / 26.94 | -5.12 | -5.55 | 212 | 52 | 0.90 | 7 |
| fjord | ultra,msaa=0 | 26.14 | 27.13 | 38.09 | 44.29 | 22.47 / 25.46 | -4.85 | -6.15 | 212 | 52 | 0.91 | 7 |
| fjord | ultra,scale=0.5 | 12.85 | 13.69 | 14.61 | 16.56 | 12.68 / 13.56 | -18.14 | -19.59 | 212 | 52 | 0.84 | 7 |
| fjord | ultra,hide.FishPenManager=true | 29.12 | 30.10 | 32.73 | 33.46 | 29.59 / 32.73 | -1.87 | -3.18 | 152 | 37 | 0.84 | 7 |
| fjord | ultra,glow=false,vp.screen_space_aa=0 | 27.49 | 28.77 | 31.64 | 34.49 | 28.57 / 30.61 | -3.50 | -4.51 | 212 | 52 | 0.82 | 7 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| fjord | ultra,hide.WaterGrid=true | 0.909 | 0.905 | 7 |
| fjord | ultra,hide.Terrain=true | 1.018 | 0.990 | 7 |
| fjord | ultra,env.background_mode=1 | 0.915 | 0.910 | 7 |
| fjord | ultra,sun.shadow_enabled=false | 0.860 | 0.862 | 7 |
| fjord | ultra,ssao=false | 0.838 | 0.832 | 7 |
| fjord | ultra,msaa=0 | 0.762 | 0.770 | 7 |
| fjord | ultra,scale=0.5 | 0.419 | 0.425 | 7 |
| fjord | ultra,hide.FishPenManager=true | 0.981 | 1.004 | 7 |
| fjord | ultra,glow=false,vp.screen_space_aa=0 | 0.938 | 0.946 | 7 |

Boot: terrain ready at frame 6, max frame after the first 10: 16.2 ms, frames over 50 ms: 4 (95.1, 303.7, 93.9, 82.5)
