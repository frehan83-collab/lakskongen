### swell_gate_nounits2 — 1920x1080, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-21T03:03:21

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fjord | high | 14.51 | 15.38 | 17.96 | 18.85 | 15.35 / 17.96 | +0.00 | +0.00 | 482 | 63 | 2.66 | 3 |
| fjord | high,water.swell_peak=1000000.0 | 14.71 | 15.61 | 18.48 | 19.25 | 15.59 / 18.48 | +0.20 | +0.23 | 481 | 63 | 2.65 | 3 |
| farm | high | 13.58 | 14.14 | 16.80 | 17.34 | 14.10 / 16.58 | +0.00 | +0.00 | 344 | 131 | 2.63 | 3 |
| farm | high,water.swell_peak=1000000.0 | 14.05 | 14.60 | 17.19 | 17.77 | 14.59 / 17.19 | +0.47 | +0.46 | 343 | 131 | 2.64 | 3 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| fjord | high,water.swell_peak=1000000.0 | 1.020 | 1.016 | 3 |
| farm | high,water.swell_peak=1000000.0 | 1.029 | 1.032 | 3 |

Boot: terrain ready at frame 6, max frame after the first 10: 25.7 ms, frames over 50 ms: 2 (129.0, 95.8)
