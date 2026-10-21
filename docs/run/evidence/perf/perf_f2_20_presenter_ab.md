### f2_20_presenter_ab — 1920x1147, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-20T18:16:16

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fjord | high | 13.03 | 13.75 | 15.26 | 15.92 | 13.72 / 15.17 | +0.00 | +0.00 | 510 | 63 | 2.56 | 3 |
| fjord | high,hide.FarmFacilityVisualManager=true | 12.96 | 13.70 | 15.26 | 16.03 | 13.65 / 15.26 | -0.07 | -0.05 | 477 | 63 | 2.58 | 3 |
| land | high | 11.95 | 17.25 | 25.36 | 28.05 | 12.04 / 13.26 | +0.00 | +0.00 | 198 | 30 | 2.07 | 3 |
| land | high,hide.FarmFacilityVisualManager=true | 11.29 | 11.75 | 20.44 | 26.78 | 11.73 / 12.97 | -0.66 | -5.50 | 173 | 28 | 2.08 | 3 |
| hav | high | 10.98 | 11.46 | 12.78 | 13.99 | 11.45 / 12.78 | +0.00 | +0.00 | 166 | 27 | 2.19 | 3 |
| hav | high,hide.FarmFacilityVisualManager=true | 10.46 | 10.91 | 12.36 | 12.82 | 10.90 / 12.36 | -0.52 | -0.55 | 126 | 6 | 2.20 | 3 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| fjord | high,hide.FarmFacilityVisualManager=true | 0.995 | 0.999 | 3 |
| land | high,hide.FarmFacilityVisualManager=true | 0.958 | 0.896 | 3 |
| hav | high,hide.FarmFacilityVisualManager=true | 0.952 | 0.953 | 3 |

Boot: terrain ready at frame 5, max frame after the first 10: 23.0 ms, frames over 50 ms: 4 (167.8, 121.1, 53.7, 79.0)
