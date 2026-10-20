### ab13 — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-14T20:29:47

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| farm | ultra | 30.59 | 33.22 | 34.91 | 36.80 | 28.08 / 48.50 | +0.00 | +0.00 | 186 | 48 | 0.87 | 36 |
| farm | high | 19.12 | 19.68 | 29.91 | 30.92 | 17.89 / 18.75 | -11.47 | -13.54 | 186 | 48 | 0.95 | 6 |
| farm | high,dof_quality=0 | 18.11 | 24.32 | 37.64 | 42.18 | 16.43 / 17.49 | -12.48 | -8.90 | 186 | 48 | 0.89 | 6 |
| farm | high,dof=false | 14.48 | 15.77 | 17.29 | 22.27 | 15.02 / 15.71 | -16.11 | -17.45 | 186 | 48 | 0.93 | 6 |
| farm | high,screen_aa=0 | 16.66 | 18.24 | 19.11 | 23.98 | 16.00 / 16.93 | -13.93 | -14.98 | 186 | 48 | 0.91 | 6 |
| farm | high,msaa=1 | 17.02 | 19.61 | 20.52 | 20.90 | 16.07 / 16.77 | -13.57 | -13.61 | 186 | 48 | 0.91 | 6 |
| farm | high,soft_shadows=0 | 17.34 | 18.18 | 19.12 | 19.74 | 16.80 / 17.73 | -13.25 | -15.04 | 186 | 48 | 0.91 | 6 |
| fjord | ultra | 31.40 | 33.40 | 34.80 | 35.02 | 29.50 / 30.24 | +0.00 | +0.00 | 212 | 52 | 0.82 | 36 |
| fjord | high | 14.34 | 16.45 | 17.44 | 18.02 | 14.84 / 16.20 | -17.06 | -16.95 | 212 | 52 | 0.90 | 6 |
| fjord | high,dof_quality=0 | 15.39 | 16.02 | 18.20 | 18.70 | 14.80 / 15.72 | -16.01 | -17.38 | 212 | 52 | 0.93 | 6 |
| fjord | high,dof=false | 15.34 | 16.44 | 17.84 | 18.43 | 14.78 / 15.54 | -16.06 | -16.96 | 212 | 52 | 0.87 | 6 |
| fjord | high,screen_aa=0 | 13.82 | 15.04 | 15.96 | 16.61 | 14.51 / 15.53 | -17.58 | -18.36 | 212 | 52 | 0.87 | 6 |
| fjord | high,msaa=1 | 15.07 | 15.95 | 16.79 | 17.10 | 13.44 / 14.23 | -16.33 | -17.45 | 212 | 52 | 0.85 | 6 |
| fjord | high,soft_shadows=0 | 15.09 | 16.07 | 17.20 | 20.25 | 14.78 / 15.65 | -16.31 | -17.33 | 212 | 52 | 0.85 | 6 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| farm | high | 0.605 | 0.623 | 6 |
| farm | high,dof_quality=0 | 0.528 | 0.570 | 6 |
| farm | high,dof=false | 0.508 | 0.483 | 6 |
| farm | high,screen_aa=0 | 0.570 | 0.566 | 6 |
| farm | high,msaa=1 | 0.547 | 0.558 | 6 |
| farm | high,soft_shadows=0 | 0.569 | 0.572 | 6 |
| fjord | high | 0.495 | 0.505 | 6 |
| fjord | high,dof_quality=0 | 0.488 | 0.498 | 6 |
| fjord | high,dof=false | 0.487 | 0.498 | 6 |
| fjord | high,screen_aa=0 | 0.457 | 0.482 | 6 |
| fjord | high,msaa=1 | 0.446 | 0.460 | 6 |
| fjord | high,soft_shadows=0 | 0.485 | 0.492 | 6 |

Boot: terrain ready at frame 8, max frame after the first 10: 53.2 ms, frames over 50 ms: 4 (193.5, 828.1, 260.9, 53.2)
