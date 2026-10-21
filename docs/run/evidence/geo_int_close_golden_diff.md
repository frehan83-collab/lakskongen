# Golden-trace diff report

- expected: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.pLtwgj/out/base` (commit 497eec5041a6, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.pLtwgj/out/head` (commit ecdbe86127c8, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

## Verdict (geo stages 2-3, integration close: the per-merge check)

Recorded by the merge captain with `tools/record_golden_traces.sh --base 497eec5
--report docs/run/evidence/geo_int_close_golden_diff.md` on `ecdbe86`, against
the geo/int head before the close (`497eec5`, after track D's merge). No
`--update` was run here; the close's single re-record is in
`geo_stage23_golden_diff.md`. **27 of 27 traces identical on every day, no save
drift.** The math fingerprints are equal. Between the two heads lie:
- the GEO-VIS fixes (GDScript and the bridge's additive `home_po` key);
- the D17 comment sweep (comments only);
- the re-recorded fixtures;
- the GEO-VIS evidence and its capture scene.

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
