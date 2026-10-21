### uw_sky_ab — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-21T05:39:55

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| underwater | high | 13.96 | 14.66 | 15.34 | 15.65 | 14.61 / 15.34 | +0.00 | +0.00 | 264 | 74 | 1.50 | 3 |
| underwater | high,director.sky_mode_staged=2 | 13.69 | 14.32 | 15.07 | 15.38 | 14.30 / 14.99 | -0.27 | -0.34 | 264 | 75 | 1.52 | 3 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| underwater | high,director.sky_mode_staged=2 | 0.973 | 0.977 | 3 |

Boot: terrain ready at frame 6, max frame after the first 10: 28.0 ms, frames over 50 ms: 3 (155.4, 89.9, 53.4)
