### ab14 — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-14T20:59:34

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| coast | high | 16.68 | 17.48 | 18.51 | 18.75 | 14.30 / 15.22 | +0.00 | +0.00 | 266 | 93 | 0.93 | 30 |
| coast | high,scale=0.75 | 15.49 | 16.34 | 17.29 | 17.74 | 14.49 / 15.35 | -1.19 | -1.14 | 266 | 93 | 0.92 | 6 |
| coast | high,shadow_splits=2 | 16.19 | 16.95 | 18.05 | 18.50 | 14.31 / 15.45 | -0.49 | -0.53 | 223 | 51 | 0.92 | 6 |
| coast | high,shadow_distance=0.75 | 16.98 | 17.71 | 18.70 | 19.27 | 17.37 / 18.23 | +0.30 | +0.23 | 247 | 76 | 1.02 | 6 |
| coast | high,dof=false | 16.32 | 16.97 | 17.76 | 18.16 | 14.68 / 15.35 | -0.36 | -0.51 | 266 | 93 | 0.91 | 6 |
| coast | high,cloud_steps=4,cloud_octaves=5 | 16.59 | 17.30 | 18.11 | 18.36 | 14.41 / 15.80 | -0.09 | -0.18 | 266 | 93 | 1.02 | 6 |
| farm | high | 19.26 | 19.77 | 20.62 | 20.92 | 16.14 / 16.95 | +0.00 | +0.00 | 186 | 48 | 1.13 | 30 |
| farm | high,scale=0.75 | 17.80 | 18.26 | 18.86 | 19.08 | 15.38 / 16.02 | -1.46 | -1.51 | 186 | 48 | 1.17 | 6 |
| farm | high,shadow_splits=2 | 19.44 | 19.84 | 20.55 | 21.05 | 16.28 / 17.06 | +0.18 | +0.07 | 162 | 24 | 1.14 | 6 |
| farm | high,shadow_distance=0.75 | 19.38 | 19.80 | 20.48 | 20.61 | 16.88 / 17.64 | +0.12 | +0.03 | 184 | 46 | 1.17 | 6 |
| farm | high,dof=false | 18.21 | 18.68 | 19.66 | 20.23 | 15.61 / 16.43 | -1.05 | -1.09 | 186 | 48 | 1.06 | 6 |
| farm | high,cloud_steps=4,cloud_octaves=5 | 19.12 | 19.84 | 20.87 | 21.12 | 18.99 / 19.56 | -0.14 | +0.07 | 186 | 48 | 1.06 | 6 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| coast | high,scale=0.75 | 0.904 | 0.907 | 6 |
| coast | high,shadow_splits=2 | 0.982 | 0.984 | 6 |
| coast | high,shadow_distance=0.75 | 1.024 | 1.026 | 6 |
| coast | high,dof=false | 0.966 | 0.975 | 6 |
| coast | high,cloud_steps=4,cloud_octaves=5 | 0.997 | 0.999 | 6 |
| farm | high,scale=0.75 | 0.923 | 0.928 | 6 |
| farm | high,shadow_splits=2 | 1.001 | 1.004 | 6 |
| farm | high,shadow_distance=0.75 | 1.002 | 1.009 | 6 |
| farm | high,dof=false | 0.929 | 0.929 | 6 |
| farm | high,cloud_steps=4,cloud_octaves=5 | 1.000 | 1.004 | 6 |

Boot: terrain ready at frame 7, max frame after the first 10: 27.4 ms, frames over 50 ms: 4 (111.8, 88.3, 168.7, 82.1)
