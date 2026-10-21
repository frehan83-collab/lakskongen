### uw — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-21T02:13:50

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| farm | high | 19.59 | 20.33 | 23.25 | 23.72 | 20.26 / 23.25 | +0.00 | +0.00 | 310 | 107 | 1.42 | 3 |
| farm | high,hide.UnderwaterView=true | 19.34 | 20.18 | 22.82 | 23.45 | 20.15 / 22.49 | -0.25 | -0.15 | 307 | 107 | 1.42 | 3 |
| underwater | high | 13.62 | 14.42 | 15.23 | 15.77 | 14.31 / 15.02 | +0.00 | +0.00 | 266 | 75 | 1.36 | 3 |
| underwater | high,hide.UnderwaterView=true | 13.17 | 13.76 | 14.55 | 14.86 | 13.73 / 14.47 | -0.45 | -0.66 | 264 | 75 | 1.45 | 3 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| farm | high,hide.UnderwaterView=true | 0.995 | 0.993 | 3 |
| underwater | high,hide.UnderwaterView=true | 0.958 | 0.952 | 3 |

Boot: terrain ready at frame 7, max frame after the first 10: 45.2 ms, frames over 50 ms: 4 (238.7, 151.3, 151.0, 163.5)
