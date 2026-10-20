### cloud_pass — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-14T21:53:00

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fjord | high,cloud_pass=2 | 12.79 | 13.73 | 15.00 | 16.96 | 13.64 / 14.69 | +0.00 | +0.00 | 212 | 52 | 0.89 | 6 |
| fjord | high | 13.09 | 13.90 | 14.96 | 16.67 | 13.77 / 23.58 | +0.30 | +0.17 | 212 | 52 | 0.90 | 6 |
| coast | high,cloud_pass=2 | 12.46 | 13.22 | 14.25 | 16.29 | 13.09 / 14.06 | +0.00 | +0.00 | 266 | 93 | 0.88 | 6 |
| coast | high | 12.59 | 13.31 | 14.66 | 16.70 | 13.21 / 14.42 | +0.13 | +0.09 | 266 | 93 | 0.87 | 6 |
| farm | high,cloud_pass=2 | 14.29 | 14.97 | 20.84 | 24.92 | 14.80 / 15.42 | +0.00 | +0.00 | 186 | 48 | 0.95 | 6 |
| farm | high | 14.25 | 15.23 | 22.78 | 26.15 | 14.78 / 16.18 | -0.04 | +0.26 | 186 | 48 | 0.94 | 6 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| fjord | high | 1.032 | 1.015 | 6 |
| coast | high | 1.012 | 1.010 | 6 |
| farm | high | 0.998 | 1.003 | 6 |

Boot: terrain ready at frame 4, max frame after the first 10: 17.7 ms, frames over 50 ms: 2 (62.9, 67.2)
