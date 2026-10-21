# Golden-trace diff report

- expected: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.48tZk5/out/base` (commit e98944df1dc4, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.48tZk5/out/head` (commit b06fd70a77c4, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

## Verdict (geo stages 2-3, integration close; `docs/geo/STAGE23_PLAN.md` §4 step 4)

Recorded by the merge captain after the plan's single re-record:
- `tools/record_golden_traces.sh --update` on geo/int `b06fd70` wrote the
  fixtures (`src/LakskongenSim.Tests/Fixtures/golden_traces`);
- then `tools/record_golden_traces.sh --base origin/main --report
  docs/run/evidence/geo_stage23_golden_diff.md` ran on the same commit.
origin/main is `e98944d`, which is also geo/int's merge base.

Result: **27 of 27 games identical on every day** (731 days each), and the
math fingerprints are equal. The only save drift is the D17 rename (ruling
OD8). It starts on day 0 and lasts every snapshot after, in all 27 runs, on
three nodes:

| Drifted node | What it holds | Why it changed |
|---|---|---|
| `/CompetitorAI/Competitors[5]` | the CompetitorAI roster snapshot; index 5 is `nordlaks_nord` | its `Name` is now "Kongsemne Havbruk AS"; the id and every number are unchanged |
| `/CompetitorCompanies[5]` | the legacy roster DTO beside the snapshot | the same company's name |
| `/Runtime` | `EngineRuntimeSnapshot`; `IntelCompetitors` lists MarketIntelligence's producers | the producer "NordLaks" is now "Kongsemne"; its tonnes and draws are unchanged |

Every run's day-0 drift hashes equal those in track A's own report
(`geo_int_a23_golden_diff.md`). That holds for both sides: the expected
(base) hashes and the actual (head) hashes, 27 of 27 each. So nothing merged
after track A changed a save:
- track B's `SeaSite.CandidateId` is written only when set, and the golden
  player never builds;
- tracks C and D are GDScript;
- the captain's GEO-VIS fixes are GDScript plus the bridge key `home_po`;
- the D17 sweep is comments only.
Nothing in this change moves a traced field, an RNG stream or an event line.
No `GameState` field was added except the optional `SeaSite.CandidateId`.
The save version stays 11.

The fixtures committed with this report carry the rename. From now on,
`GoldenTraceTests` compare against them, and the CI `golden-traces` job diffs
any PR's merge base against its head.

| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |
|---|---|---|---|---|---|---|
| algekatastrofe | 1 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| algekatastrofe | 7 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| algekatastrofe | 13 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| barekraft_mester | 1 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| barekraft_mester | 7 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| barekraft_mester | 13 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| forste_sesong | 1 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| forste_sesong | 7 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| forste_sesong | 13 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| isa_utbrudd | 1 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| isa_utbrudd | 7 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| isa_utbrudd | 13 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| krise_manager | 1 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| krise_manager | 7 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| krise_manager | 13 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| laksebaronene | 1 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| laksebaronene | 7 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| laksebaronene | 13 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| lofoten_eventyr | 1 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| lofoten_eventyr | 7 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| lofoten_eventyr | 13 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| po3_red_sone | 1 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| po3_red_sone | 7 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| po3_red_sone | 13 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| ressursrente | 1 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| ressursrente | 7 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |
| ressursrente | 13 | identical game, save drift in 26 snapshots | 0: changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime | 0 | — | — |

27 of 27 traces identical.

## Details

### algekatastrofe seed 1

```
algekatastrofe seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 1, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "70c2cbbfbb4a4ad3", actual "8e949b98aa185eb5"
```

### algekatastrofe seed 7

```
algekatastrofe seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 7, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "91785458d77f6a27", actual "e747825fa4ace7bf"
```

### algekatastrofe seed 13

```
algekatastrofe seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 13, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "aac9f9fa341cf498", actual "0fad510e06fb48de"
```

### barekraft_mester seed 1

```
barekraft_mester seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 1, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "39ee2cbacdb6d661", actual "aa5616629484111e"
```

### barekraft_mester seed 7

```
barekraft_mester seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 7, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "9c9b399e92ce16ca", actual "6989c1d72ab43263"
```

### barekraft_mester seed 13

```
barekraft_mester seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 13, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "9d7ac615eaeb85e2", actual "4f59706c71c30f03"
```

### forste_sesong seed 1

```
forste_sesong seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 1, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "909c412601c61581", actual "1ba6331ec6a0db5b"
```

### forste_sesong seed 7

```
forste_sesong seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 7, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "3d9a4176e8905112", actual "1ced714109810e1d"
```

### forste_sesong seed 13

```
forste_sesong seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 13, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "e0f9f83b77a54b74", actual "174e3c797ecceef5"
```

### isa_utbrudd seed 1

```
isa_utbrudd seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 1, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "20b18a954ebe06f5", actual "833322ae9d1bd16e"
```

### isa_utbrudd seed 7

```
isa_utbrudd seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 7, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "a8e9cce7a4f4836c", actual "02df5e1922611309"
```

### isa_utbrudd seed 13

```
isa_utbrudd seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 13, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "7c3b22939af656c6", actual "7860e646027512a8"
```

### krise_manager seed 1

```
krise_manager seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 1, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "da4bcba0c9b491de", actual "6f1cb3e8e17dc9ac"
```

### krise_manager seed 7

```
krise_manager seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 7, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "3cdd42a8d33d7ca7", actual "ebcf45a833c95edc"
```

### krise_manager seed 13

```
krise_manager seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 13, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "dc477f42ead8698d", actual "253cecbc8fb1fac2"
```

### laksebaronene seed 1

```
laksebaronene seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 1, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "009a710c26ed834b", actual "8ab176c95265bf8d"
```

### laksebaronene seed 7

```
laksebaronene seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 7, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "99aee2b5b617f6d6", actual "2ca7606b16651b65"
```

### laksebaronene seed 13

```
laksebaronene seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 13, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "18bce2ef9e685855", actual "49dd95cb0a5091ae"
```

### lofoten_eventyr seed 1

```
lofoten_eventyr seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 1, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "f513708f12266a22", actual "3c2becbf11cc7597"
```

### lofoten_eventyr seed 7

```
lofoten_eventyr seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 7, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "2fe09e66808f28fd", actual "fcedb3752d5a9e11"
```

### lofoten_eventyr seed 13

```
lofoten_eventyr seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 13, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "498d28d1704581fc", actual "b6fab3f79885bb2b"
```

### po3_red_sone seed 1

```
po3_red_sone seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 1, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "66d9ae47d9f3a25d", actual "07fa6a57e6a7dbfd"
```

### po3_red_sone seed 7

```
po3_red_sone seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 7, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "86bad6ae4c285801", actual "f0648e7392cd2e4a"
```

### po3_red_sone seed 13

```
po3_red_sone seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 13, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "fb1eaaf132b705ee", actual "02ebf61de4b7859b"
```

### ressursrente seed 1

```
ressursrente seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 1, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "90a02ab624b77214", actual "482b7ef26c63a165"
```

### ressursrente seed 7

```
ressursrente seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 7, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "7eb5dbc5fc89a900", actual "c1ddc2268272206d"
```

### ressursrente seed 13

```
ressursrente seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 13, day 0, field save › changed /CompetitorAI/Competitors[5], /CompetitorCompanies[5], /Runtime: expected "6ffc9fd3121d400c", actual "0e3e6759e8b3714a"
```

