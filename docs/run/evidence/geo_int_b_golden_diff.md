# Golden-trace diff report

- expected: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.WCj0aq/out/base` (commit 67ae9e6aacb4, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.WCj0aq/out/head` (commit 4cb8ca9f2f03, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

## Verdict (geo/int after track B's full merge; `docs/geo/STAGE23_PLAN.md` §4 step 3)

Recorded by the merge captain with `tools/record_golden_traces.sh --base 67ae9e6
--report docs/run/evidence/geo_int_b_golden_diff.md` on `4cb8ca9` (the merge
`b4cc97f` of `geo/sea-candidates` `811a5eb` with the track-A stubs swapped for
track A's code, the review fixes `944d2bd`, `681ef6e` and `4cb8ca9`, and the
dialog layout fix `338269f`), against the previous geo/int head `67ae9e6`, no
`--update`: **27 of 27 games identical on every day** (731 days each), math
fingerprints equal, and **no save drift**: no golden game builds at a
candidate, and `SeaSite.CandidateId` is written only when set, so every
snapshot is byte-identical. Track B's own check against its base `f15fc8a`
(`geo_sea_candidates_golden_diff.md`) said the same. The later commits
(`716b80c` MIN_CHECKS, the docs commit) change no sim code.

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
