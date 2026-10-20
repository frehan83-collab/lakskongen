# F2-13 / F2-15 golden-trace diff (wave4/sim-land-2)

Recorded with `tools/record_golden_traces.sh --against src/LakskongenSim.Tests/Fixtures/golden_traces` at `4384675`
(F2-13 and F2-15 in), on the M1 Pro with the fixtures' math library: 27 of 27 traces identical, no save drift. No
shipped scenario stocks a tiered smolt (the tier's live carryover and its premium never run there) or holds a land
site (the land plan, competence, costs and boards never run there), so the fixtures are not re-recorded (RUN_LOG §7,
2026-10-20).

- expected: `src/LakskongenSim.Tests/Fixtures/golden_traces` (commit 27b0af604a52, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: a temporary recording of `4384675` (commit 438467593d8e, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |
|---|---|---|---|---|---|---|
| algekatastrofe | 1 | identical | — | 0 | — | — |
| algekatastrofe | 7 | identical | — | 0 | — | — |
| algekatastrofe | 13 | identical | — | 0 | — | — |
| barekraft_mester | 1 | identical | — | 0 | — | — |
| barekraft_mester | 7 | identical | — | 0 | — | — |
| barekraft_mester | 13 | identical | — | 0 | — | — |
| forste_sesong | 1 | identical | — | 0 | — | — |
| forste_sesong | 7 | identical | — | 0 | — | — |
| forste_sesong | 13 | identical | — | 0 | — | — |
| isa_utbrudd | 1 | identical | — | 0 | — | — |
| isa_utbrudd | 7 | identical | — | 0 | — | — |
| isa_utbrudd | 13 | identical | — | 0 | — | — |
| krise_manager | 1 | identical | — | 0 | — | — |
| krise_manager | 7 | identical | — | 0 | — | — |
| krise_manager | 13 | identical | — | 0 | — | — |
| laksebaronene | 1 | identical | — | 0 | — | — |
| laksebaronene | 7 | identical | — | 0 | — | — |
| laksebaronene | 13 | identical | — | 0 | — | — |
| lofoten_eventyr | 1 | identical | — | 0 | — | — |
| lofoten_eventyr | 7 | identical | — | 0 | — | — |
| lofoten_eventyr | 13 | identical | — | 0 | — | — |
| po3_red_sone | 1 | identical | — | 0 | — | — |
| po3_red_sone | 7 | identical | — | 0 | — | — |
| po3_red_sone | 13 | identical | — | 0 | — | — |
| ressursrente | 1 | identical | — | 0 | — | — |
| ressursrente | 7 | identical | — | 0 | — | — |
| ressursrente | 13 | identical | — | 0 | — | — |

27 of 27 traces identical.
