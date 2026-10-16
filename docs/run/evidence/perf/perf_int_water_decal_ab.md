### int_water_decal_ab — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-16T16:45:50

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| farm | high,cloud_shadow_decal=true | 19.90 | 20.98 | 22.52 | 22.98 | 20.96 / 22.49 | +0.00 | +0.00 | 301 | 99 | 1.50 | 2 |
| farm | high | 19.11 | 19.95 | 21.31 | 21.88 | 19.94 / 21.37 | -0.79 | -1.03 | 298 | 99 | 1.49 | 2 |
| fjord | high,cloud_shadow_decal=true | 18.30 | 19.49 | 21.01 | 21.79 | 19.41 / 21.13 | +0.00 | +0.00 | 422 | 55 | 1.31 | 2 |
| fjord | high | 17.95 | 18.65 | 20.11 | 20.80 | 18.62 / 20.14 | -0.35 | -0.84 | 422 | 55 | 1.30 | 2 |
| coast | high,cloud_shadow_decal=true | 18.20 | 20.23 | 22.65 | 22.95 | 19.99 / 22.92 | +0.00 | +0.00 | 484 | 114 | 1.17 | 2 |
| coast | high | 16.73 | 19.11 | 21.35 | 21.88 | 19.02 / 21.62 | -1.47 | -1.12 | 484 | 115 | 1.16 | 2 |
| panel | high,cloud_shadow_decal=true | 9.85 | 10.30 | 10.87 | 11.34 | 10.28 / 10.84 | +0.00 | +0.00 | 386 | 51 | 1.33 | 2 |
| panel | high | 9.45 | 9.91 | 10.46 | 10.86 | 9.88 / 10.45 | -0.40 | -0.39 | 386 | 51 | 1.33 | 2 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| farm | high | 0.960 | 0.951 | 2 |
| fjord | high | 0.981 | 0.957 | 2 |
| coast | high | 0.920 | 0.945 | 2 |
| panel | high | 0.959 | 0.961 | 2 |

Boot: terrain ready at frame 10, max frame after the first 10: 271.8 ms, frames over 50 ms: 7 (233.2, 249.0, 513.9, 71.5, 137.7, 144.7, 271.7)
