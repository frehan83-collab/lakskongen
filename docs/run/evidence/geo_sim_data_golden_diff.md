# Golden-trace diff report

- expected: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.0dMwMG/out/base` (commit f15fc8a47c24, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.0dMwMG/out/head` (commit 645c850074cf, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

## Verdict (geo stages 2-3, track A: A2 and A3; `docs/geo/STAGE23_PLAN.md`)

Recorded with `tools/record_golden_traces.sh --base $(git merge-base HEAD geo/int)`
(`f15fc8a`, geo/int after A1) on `645c850` (`geo/sim-data-2`), no `--update`:
**27 of 27 games identical on every day** (every day hash, traced group and
RNG stream over 731 days). The only save drift is the D17 rename (ruling OD8),
on day 0 and every 30-day snapshot after it, in all 27 runs, on exactly three
nodes, each a display name and nothing else:

| Drifted node | What it holds | Why it changed |
|---|---|---|
| `/CompetitorAI/Competitors[5]` | the CompetitorAI roster snapshot; index 5 is `nordlaks_nord` | its `Name` is now "Kongsemne Havbruk AS" (was "NordLaks Nord AS"); the id and every number are unchanged |
| `/CompetitorCompanies[5]` | the legacy roster DTO GameState keeps beside the snapshot | the same company's name |
| `/Runtime` | `EngineRuntimeSnapshot`; its `IntelCompetitors` lists MarketIntelligence's producers | the producer "NordLaks" is now "Kongsemne"; its tonnes and draws are unchanged |

Why nothing else moves:
- The golden player never builds a site or a facility, so `State.SitePlots`
  stays empty; hav_1's new display position and depth (the per-game geo
  catalogue) are never saved and feed no tick.
- The rival layout, the parody names, the zone anchors and the plot binding
  create no SimRng stream, write no GameState field and run on no tick
  (GeoNoFileSystemTests, GeoReadPurityTests); no traced CompetitorCompany field
  (cash, revenue, EBITDA, debt, licences, MAB, sites, weekly harvest,
  distress) changes, so the `rivals` group is identical.
- No EventLog entry interpolates a rival's name (STAGE23_PLAN §8), so the `ev`
  lines are identical; CompetitorAI headlines go to OnEventTriggered only.

The captain's single `--update` at integration close takes this drift into
the fixtures (plan §4 step 4).

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

