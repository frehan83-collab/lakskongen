### high_vs_ultra — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-14T21:26:56

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| farm | ultra | 36.40 | 40.15 | 43.52 | 52.14 | 28.04 / 28.73 | +0.00 | +0.00 | 186 | 48 | 0.87 | 10 |
| farm | high | 27.01 | 28.43 | 30.20 | 31.41 | 14.73 / 16.66 | -9.39 | -11.72 | 186 | 48 | 0.93 | 10 |
| fjord | ultra | 28.85 | 29.62 | 30.51 | 31.78 | 29.47 / 30.18 | +0.00 | +0.00 | 212 | 52 | 0.81 | 10 |
| fjord | high | 12.86 | 13.70 | 15.31 | 17.54 | 13.60 / 14.99 | -15.99 | -15.92 | 212 | 52 | 0.91 | 10 |
| coast | ultra | 27.00 | 27.84 | 29.44 | 31.89 | 27.57 / 28.81 | +0.00 | +0.00 | 266 | 93 | 0.81 | 10 |
| coast | high | 12.38 | 13.19 | 24.05 | 31.62 | 12.75 / 33.63 | -14.62 | -14.65 | 266 | 93 | 0.84 | 10 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| farm | high | 0.630 | 0.627 | 10 |
| fjord | high | 0.443 | 0.462 | 10 |
| coast | high | 0.458 | 0.477 | 10 |

Boot: terrain ready at frame 4, max frame after the first 10: 17.8 ms, frames over 50 ms: 3 (93.9, 76.2, 62.8)
