### panel_ab — 2560x1440, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-14T21:30:31

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| panel | high,hidden_scale=1.0,hidden_pause=false | 18.20 | 18.73 | 20.06 | 22.79 | 18.51 / 19.83 | +0.00 | +0.00 | 300 | 51 | 0.90 | 6 |
| panel | high | 7.31 | 7.68 | 8.25 | 8.74 | 7.65 / 8.19 | -10.89 | -11.05 | 300 | 51 | 0.90 | 6 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| panel | high | 0.400 | 0.412 | 6 |

Boot: terrain ready at frame 4, max frame after the first 10: 16.9 ms, frames over 50 ms: 2 (77.9, 64.3)
