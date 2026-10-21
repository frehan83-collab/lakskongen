### f2_21_23_presenter_ab — 1920x1080, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-21T01:20:29

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fjord | high | 13.63 | 14.39 | 15.90 | 16.52 | 14.37 / 15.80 | +0.00 | +0.00 | 514 | 63 | 2.83 | 3 |
| fjord | high,hide.FarmFacilityVisualManager=true | 13.50 | 14.27 | 15.83 | 16.58 | 14.23 / 15.77 | -0.13 | -0.12 | 481 | 63 | 2.83 | 3 |
| land | high | 11.70 | 12.12 | 13.53 | 14.06 | 12.12 / 13.48 | +0.00 | +0.00 | 191 | 31 | 2.46 | 3 |
| land | high,hide.FarmFacilityVisualManager=true | 11.27 | 11.72 | 13.09 | 13.47 | 11.71 / 13.03 | -0.43 | -0.40 | 162 | 27 | 2.44 | 3 |
| hav | high | 11.63 | 12.15 | 13.39 | 13.79 | 12.10 / 13.37 | +0.00 | +0.00 | 178 | 37 | 2.37 | 3 |
| hav | high,hide.FarmFacilityVisualManager=true | 11.24 | 11.72 | 13.03 | 13.42 | 11.71 / 13.05 | -0.39 | -0.43 | 126 | 6 | 2.37 | 3 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| fjord | high,hide.FarmFacilityVisualManager=true | 0.990 | 0.990 | 3 |
| land | high,hide.FarmFacilityVisualManager=true | 0.966 | 0.966 | 3 |
| hav | high,hide.FarmFacilityVisualManager=true | 0.966 | 0.965 | 3 |

Boot: terrain ready at frame 10, max frame after the first 10: 60.5 ms, frames over 50 ms: 5 (183.1, 282.7, 88.2, 132.6, 60.5)
