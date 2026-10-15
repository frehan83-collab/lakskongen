# Golden-trace diff report

U2-01 (`wave3/ui-b-1`) changes the auction rules (the activity rule) and adds
the catalogue zones, the expansion verdicts and the auction's bus events; no
golden trace opens an auction or builds a site, so every trace must stay
identical. Recorded the way `tools/record_golden_traces.sh --base 8c81754`
does, without its temporary git worktrees (the track's agent may not create
worktrees): the base tree from `git archive 8c81754` and the branch head each
built and recorded with their own `StateHashProbe golden record`, then
`StateHashProbe golden diff base head`. Result: 27 of 27 identical, no save
drift. (Against the committed fixtures, recorded on dc0b56a, both trees show
the same pre-existing save drift in `/DailySnapshots`, never a game change.)

- expected: `base (8c81754, recorded from git archive)` (commit 8c81754, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `head (29bd5fe, wave3/ui-b-1)` (commit 29bd5fea99de, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
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
