### ab12 — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-14T20:22:24

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fjord | ultra | 45.08 | 47.23 | 57.36 | 61.50 | 29.26 / 29.94 | +0.00 | +0.00 | 212 | 52 | 0.81 | 30 |
| fjord | high | 21.38 | 29.80 | 37.20 | 39.39 | 16.24 / 22.09 | -23.70 | -17.43 | 212 | 52 | 0.79 | 6 |
| fjord | high,dof=false | 18.18 | 26.73 | 36.84 | 39.61 | 14.61 / 18.49 | -26.90 | -20.50 | 212 | 52 | 0.82 | 6 |
| fjord | high,shadow_splits=2 | 23.07 | 31.72 | 39.32 | 43.00 | 16.46 / 17.13 | -22.01 | -15.51 | 193 | 32 | 0.86 | 6 |
| fjord | high,scale=0.75 | 25.31 | 34.32 | 41.95 | 44.39 | 18.33 / 32.41 | -19.77 | -12.91 | 212 | 52 | 0.77 | 6 |
| fjord | high,sun.shadow_enabled=false | 21.27 | 28.93 | 38.35 | 47.28 | 27.45 / 38.42 | -23.81 | -18.30 | 160 | 0 | 0.81 | 6 |
| farm | ultra | 41.14 | 45.85 | 57.78 | 59.95 | 27.86 / 36.66 | +0.00 | +0.00 | 186 | 48 | 0.84 | 30 |
| farm | high | 24.32 | 31.51 | 39.86 | 47.00 | 16.76 / 32.97 | -16.82 | -14.34 | 186 | 48 | 0.84 | 6 |
| farm | high,dof=false | 19.98 | 27.35 | 37.47 | 42.81 | 14.57 / 15.54 | -21.16 | -18.50 | 186 | 48 | 0.86 | 6 |
| farm | high,shadow_splits=2 | 24.18 | 31.94 | 43.28 | 49.55 | 30.20 / 45.44 | -16.96 | -13.91 | 162 | 24 | 0.82 | 6 |
| farm | high,scale=0.75 | 22.88 | 29.96 | 40.53 | 45.86 | 18.82 / 33.72 | -18.26 | -15.89 | 186 | 48 | 0.89 | 6 |
| farm | high,sun.shadow_enabled=false | 20.75 | 29.20 | 38.42 | 44.86 | 15.02 / 15.75 | -20.39 | -16.65 | 137 | 0 | 0.81 | 6 |
| coast | ultra | 42.32 | 44.48 | 53.61 | 55.75 | 29.45 / 31.98 | +0.00 | +0.00 | 266 | 93 | 0.81 | 30 |
| coast | high | 22.26 | 30.94 | 38.19 | 41.75 | 16.86 / 17.60 | -20.06 | -13.54 | 266 | 93 | 0.78 | 6 |
| coast | high,dof=false | 20.02 | 27.04 | 34.48 | 36.13 | 15.57 / 16.32 | -22.30 | -17.44 | 266 | 93 | 0.76 | 6 |
| coast | high,shadow_splits=2 | 21.36 | 30.50 | 38.19 | 41.75 | 16.91 / 17.72 | -20.96 | -13.98 | 224 | 51 | 0.84 | 6 |
| coast | high,scale=0.75 | 20.39 | 30.22 | 39.42 | 43.03 | 17.33 / 18.12 | -21.93 | -14.26 | 266 | 93 | 0.74 | 6 |
| coast | high,sun.shadow_enabled=false | 19.34 | 27.12 | 34.48 | 43.81 | 14.01 / 14.78 | -22.98 | -17.36 | 172 | 0 | 0.81 | 6 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| fjord | high | 0.561 | 0.661 | 6 |
| fjord | high,dof=false | 0.501 | 0.612 | 6 |
| fjord | high,shadow_splits=2 | 0.592 | 0.679 | 6 |
| fjord | high,scale=0.75 | 0.591 | 0.698 | 6 |
| fjord | high,sun.shadow_enabled=false | 0.489 | 0.587 | 6 |
| farm | high | 0.618 | 0.685 | 6 |
| farm | high,dof=false | 0.523 | 0.561 | 6 |
| farm | high,shadow_splits=2 | 0.665 | 0.716 | 6 |
| farm | high,scale=0.75 | 0.550 | 0.647 | 6 |
| farm | high,sun.shadow_enabled=false | 0.604 | 0.633 | 6 |
| coast | high | 0.583 | 0.664 | 6 |
| coast | high,dof=false | 0.495 | 0.592 | 6 |
| coast | high,shadow_splits=2 | 0.549 | 0.654 | 6 |
| coast | high,scale=0.75 | 0.568 | 0.661 | 6 |
| coast | high,sun.shadow_enabled=false | 0.456 | 0.585 | 6 |

Boot: terrain ready at frame 11, max frame after the first 10: 42.0 ms, frames over 50 ms: 5 (149.1, 147.1, 123.4, 199.7, 104.3)
