# Golden-trace diff report

- expected: `src/LakskongenSim.Tests/Fixtures/golden_traces` (commit dc0b56a56399, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `<scratch>/golden (HEAD 7c00b3b recorded by tools/record_golden_traces.sh --against the fixtures)` (commit 7c00b3b6dc5f, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |
|---|---|---|---|---|---|---|
| algekatastrofe | 1 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan | 0 | — | — |
| algekatastrofe | 7 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan | 0 | — | — |
| algekatastrofe | 13 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan | 0 | — | — |
| barekraft_mester | 1 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| barekraft_mester | 7 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| barekraft_mester | 13 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| forste_sesong | 1 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| forste_sesong | 7 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| forste_sesong | 13 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| isa_utbrudd | 1 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan | 0 | — | — |
| isa_utbrudd | 7 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan | 0 | — | — |
| isa_utbrudd | 13 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan | 0 | — | — |
| krise_manager | 1 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| krise_manager | 7 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| krise_manager | 13 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| laksebaronene | 1 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| laksebaronene | 7 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| laksebaronene | 13 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| lofoten_eventyr | 1 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| lofoten_eventyr | 7 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| lofoten_eventyr | 13 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan | 0 | — | — |
| po3_red_sone | 1 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan | 0 | — | — |
| po3_red_sone | 7 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan | 0 | — | — |
| po3_red_sone | 13 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan | 0 | — | — |
| ressursrente | 1 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan | 0 | — | — |
| ressursrente | 7 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan | 0 | — | — |
| ressursrente | 13 | identical game, save drift in 26 snapshots | 0: added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan | 0 | — | — |

27 of 27 traces identical.

## Details

### algekatastrofe seed 1

```
algekatastrofe seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 1, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan: expected "628daaef4237ada8", actual "4b1cbefb9e216869"
```

### algekatastrofe seed 7

```
algekatastrofe seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 7, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan: expected "a19b0fb2ffdf4b45", actual "2eaa215de488d163"
```

### algekatastrofe seed 13

```
algekatastrofe seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 13, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan: expected "48d1ec46ab8e22c5", actual "ab414f33dddd20e3"
```

### barekraft_mester seed 1

```
barekraft_mester seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 1, day 0, field save › added /SeaSites[0]/FeedPlan: expected "644c96859861c047", actual "b1ee757ea6b86dbe"
```

### barekraft_mester seed 7

```
barekraft_mester seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 7, day 0, field save › added /SeaSites[0]/FeedPlan: expected "d71f2168bf3edb4f", actual "f0576b006f752dc6"
```

### barekraft_mester seed 13

```
barekraft_mester seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 13, day 0, field save › added /SeaSites[0]/FeedPlan: expected "41c29555e2c48e04", actual "92a7a33872053062"
```

### forste_sesong seed 1

```
forste_sesong seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 1, day 0, field save › added /SeaSites[0]/FeedPlan: expected "73897993e53a5184", actual "cc9756163d0442e1"
```

### forste_sesong seed 7

```
forste_sesong seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 7, day 0, field save › added /SeaSites[0]/FeedPlan: expected "50fa20b1f53dbf22", actual "430439b0d5682ee7"
```

### forste_sesong seed 13

```
forste_sesong seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 13, day 0, field save › added /SeaSites[0]/FeedPlan: expected "c822c92ed7992bab", actual "b94a4dfd6eedf6c7"
```

### isa_utbrudd seed 1

```
isa_utbrudd seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 1, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan: expected "08d512f4dd5472ae", actual "f398b2476a5526f9"
```

### isa_utbrudd seed 7

```
isa_utbrudd seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 7, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan: expected "de2ca6311aae02a1", actual "9f6d8acfeea69ee6"
```

### isa_utbrudd seed 13

```
isa_utbrudd seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 13, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan: expected "b4eead42f2239927", actual "43c9ede5c1f31040"
```

### krise_manager seed 1

```
krise_manager seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 1, day 0, field save › added /SeaSites[0]/FeedPlan: expected "447e6fd6e0a6d845", actual "3778db3a10b8ff56"
```

### krise_manager seed 7

```
krise_manager seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 7, day 0, field save › added /SeaSites[0]/FeedPlan: expected "c80cbc2478bb2642", actual "5c40dcc4c9003f41"
```

### krise_manager seed 13

```
krise_manager seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 13, day 0, field save › added /SeaSites[0]/FeedPlan: expected "169105d3c49be06a", actual "55c5825081407cc7"
```

### laksebaronene seed 1

```
laksebaronene seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 1, day 0, field save › added /SeaSites[0]/FeedPlan: expected "4d495f5c7a13349c", actual "f4cdbc235feb17e5"
```

### laksebaronene seed 7

```
laksebaronene seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 7, day 0, field save › added /SeaSites[0]/FeedPlan: expected "2500f5deadf4f349", actual "3c197864ae2a87e9"
```

### laksebaronene seed 13

```
laksebaronene seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 13, day 0, field save › added /SeaSites[0]/FeedPlan: expected "e9ffa0a465806517", actual "a887990117f11209"
```

### lofoten_eventyr seed 1

```
lofoten_eventyr seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 1, day 0, field save › added /SeaSites[0]/FeedPlan: expected "8a957f004e0f28f0", actual "41681c3a8bf8e6b4"
```

### lofoten_eventyr seed 7

```
lofoten_eventyr seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 7, day 0, field save › added /SeaSites[0]/FeedPlan: expected "d9bae32bc7d7f6e8", actual "41df24c48547e944"
```

### lofoten_eventyr seed 13

```
lofoten_eventyr seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 13, day 0, field save › added /SeaSites[0]/FeedPlan: expected "e4225f3afbdfe48e", actual "a623aa68bc8751c6"
```

### po3_red_sone seed 1

```
po3_red_sone seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 1, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan: expected "1ef8f306ced9ee10", actual "63785fc684b015b2"
```

### po3_red_sone seed 7

```
po3_red_sone seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 7, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan: expected "ff772d7d0f44fd7c", actual "098e16f638536d6d"
```

### po3_red_sone seed 13

```
po3_red_sone seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 13, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan: expected "525c7606c2b50bf9", actual "cc0e6d74b4074675"
```

### ressursrente seed 1

```
ressursrente seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 1, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan: expected "59b826f9f22fdfed", actual "f988d2d647b434a4"
```

### ressursrente seed 7

```
ressursrente seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 7, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan: expected "42a33fb8b3e1d82a", actual "14ab209c08596d46"
```

### ressursrente seed 13

```
ressursrente seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 13, day 0, field save › added /SeaSites[0]/FeedPlan, /SeaSites[1]/FeedPlan, /SeaSites[2]/FeedPlan: expected "3cb92d7739630852", actual "b6d02dc5929ee3b4"
```

