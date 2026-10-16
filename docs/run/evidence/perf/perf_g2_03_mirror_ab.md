### g2_03_mirror_ab — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-15T00:22:49

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| farm | high,mirror=false | 14.16 | 15.10 | 16.64 | 18.39 | 15.04 / 16.74 | +0.00 | +0.00 | 185 | 48 | 1.08 | 3 |
| farm | high | 14.16 | 15.14 | 16.75 | 18.08 | 15.14 / 16.75 | +0.00 | +0.04 | 185 | 48 | 1.10 | 3 |
| fjord | high,mirror=false | 12.93 | 14.02 | 15.73 | 16.72 | 14.00 / 15.73 | +0.00 | +0.00 | 212 | 52 | 1.02 | 3 |
| fjord | high | 14.67 | 15.28 | 16.48 | 17.84 | 15.24 / 16.65 | +1.74 | +1.26 | 264 | 52 | 0.99 | 3 |
| coast | high,mirror=false | 12.37 | 13.39 | 14.51 | 15.19 | 13.35 / 14.61 | +0.00 | +0.00 | 265 | 92 | 1.01 | 3 |
| coast | high | 12.66 | 14.11 | 15.88 | 16.63 | 14.06 / 15.88 | +0.29 | +0.72 | 291 | 92 | 0.98 | 3 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| farm | high | 1.006 | 1.004 | 3 |
| fjord | high | 1.127 | 1.089 | 3 |
| coast | high | 1.023 | 1.053 | 3 |

Boot: terrain ready at frame 4, max frame after the first 10: 17.9 ms, frames over 50 ms: 2 (71.5, 64.2)
