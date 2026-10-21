### swell_gate_units — 1920x1080, Apple M1 Pro (Apple7), 4.7.2-stable (official), 2026-10-21T03:00:57

| Segment | Variant | p10 ms | p50 ms | p95 ms | p99 ms | best p50 / p95 | Δp10 vs first | Δp50 vs first | draws | shadow draws | script ms | repeats |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| fjord | high | 14.91 | 15.75 | 18.32 | 19.14 | 15.68 / 18.34 | +0.00 | +0.00 | 515 | 63 | 2.85 | 6 |
| fjord | high,water.swell_peak=0.0 | 14.64 | 15.43 | 18.05 | 18.93 | 15.42 / 18.05 | -0.27 | -0.32 | 515 | 63 | 2.85 | 3 |
| fjord | high,water.swell_peak=1000000.0 | 14.86 | 15.76 | 18.26 | 19.10 | 15.69 / 18.26 | -0.05 | +0.01 | 514 | 63 | 2.87 | 3 |
| hav | high | 13.10 | 13.71 | 15.98 | 16.51 | 13.59 / 16.02 | +0.00 | +0.00 | 178 | 37 | 2.38 | 6 |
| hav | high,water.swell_peak=0.0 | 12.76 | 13.35 | 15.57 | 15.98 | 13.24 / 15.82 | -0.34 | -0.36 | 178 | 37 | 2.36 | 3 |
| hav | high,water.swell_peak=1000000.0 | 13.13 | 13.68 | 16.03 | 16.53 | 13.62 / 16.13 | +0.03 | -0.03 | 178 | 37 | 2.40 | 3 |

Paired against the reference block run just before each variant (median ratio over repeats):

| Segment | Variant | p10 ratio | p50 ratio | pairs |
|---|---|---:|---:|---:|
| fjord | high,water.swell_peak=0.0 | 0.984 | 0.982 | 3 |
| fjord | high,water.swell_peak=1000000.0 | 1.000 | 1.001 | 3 |
| hav | high,water.swell_peak=0.0 | 0.973 | 0.974 | 3 |
| hav | high,water.swell_peak=1000000.0 | 1.005 | 1.002 | 3 |

Boot: terrain ready at frame 5, max frame after the first 10: 25.8 ms, frames over 50 ms: 4 (158.9, 93.9, 55.4, 50.3)
