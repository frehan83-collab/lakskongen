# F2-06/F2-07 golden traces (`wave4/sim-sea-1`)

Recorded with `tools/record_golden_traces.sh --commit HEAD --against src/LakskongenSim.Tests/Fixtures/golden_traces` at `110fe2e` (the code of this branch's final commit; later commits change documents only). Every game is identical on every day (every traced group and every stream); the only drift is the save's shape: `/Runtime` (the in-day phase clocks gain `OffshoreSeaTickSystem`'s entry) and `/Metocean` (null: no shipped scenario has an offshore unit, so the open sea is never created). The fixtures are not re-recorded.

# Golden-trace diff report

- expected: `/Users/fredrik.j.hansen/lakskongen/.claude/worktrees/wf_a570e149-b3a-2/src/LakskongenSim.Tests/Fixtures/golden_traces` (commit 27b0af604a52, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `/private/tmp/claude-501/-Users-fredrik-j-hansen-lakskongen/1a7df52a-cf33-40ac-aaf8-eaf13ebf76df/scratchpad/simsea/golden2/run` (commit 110fe2edcd1d, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |
|---|---|---|---|---|---|---|
| algekatastrofe | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| algekatastrofe | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| algekatastrofe | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| barekraft_mester | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| barekraft_mester | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| barekraft_mester | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| forste_sesong | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| forste_sesong | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| forste_sesong | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| isa_utbrudd | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| isa_utbrudd | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| isa_utbrudd | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| krise_manager | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| krise_manager | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| krise_manager | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| laksebaronene | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| laksebaronene | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| laksebaronene | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| lofoten_eventyr | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| lofoten_eventyr | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| lofoten_eventyr | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| po3_red_sone | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| po3_red_sone | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| po3_red_sone | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| ressursrente | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| ressursrente | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |
| ressursrente | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /Metocean | 0 | — | — |

27 of 27 traces identical.

## Details

### algekatastrofe seed 1

```
algekatastrofe seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 1, day 0, field save › changed /Runtime; added /Metocean: expected "d7535ed5210fb0c6", actual "444a491484f869fc"
```

### algekatastrofe seed 7

```
algekatastrofe seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 7, day 0, field save › changed /Runtime; added /Metocean: expected "244aeef48b2264e0", actual "2fc3cbb663f636d2"
```

### algekatastrofe seed 13

```
algekatastrofe seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 13, day 0, field save › changed /Runtime; added /Metocean: expected "dfbb585064dd63ce", actual "9fc50a4d83a31367"
```

### barekraft_mester seed 1

```
barekraft_mester seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 1, day 0, field save › changed /Runtime; added /Metocean: expected "3bff0fdf541cc1f1", actual "4860057e7d1b5bb2"
```

### barekraft_mester seed 7

```
barekraft_mester seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 7, day 0, field save › changed /Runtime; added /Metocean: expected "d7cc645878f78cb8", actual "dbb6e32dc11afc00"
```

### barekraft_mester seed 13

```
barekraft_mester seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 13, day 0, field save › changed /Runtime; added /Metocean: expected "e947539171daa974", actual "3cc8d0d7fc14d3bc"
```

### forste_sesong seed 1

```
forste_sesong seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 1, day 0, field save › changed /Runtime; added /Metocean: expected "8d7d982a9d0e213b", actual "1f7fe2a6863c2c10"
```

### forste_sesong seed 7

```
forste_sesong seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 7, day 0, field save › changed /Runtime; added /Metocean: expected "b7876d8ebbccd48f", actual "08e947b8280d4ae5"
```

### forste_sesong seed 13

```
forste_sesong seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 13, day 0, field save › changed /Runtime; added /Metocean: expected "2f597cc4a3f52967", actual "920008e82fff03bb"
```

### isa_utbrudd seed 1

```
isa_utbrudd seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 1, day 0, field save › changed /Runtime; added /Metocean: expected "cb2896dbc550d811", actual "e4c329b298f64bc1"
```

### isa_utbrudd seed 7

```
isa_utbrudd seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 7, day 0, field save › changed /Runtime; added /Metocean: expected "5e61c6ef3794b27d", actual "1dfab712445f5e2c"
```

### isa_utbrudd seed 13

```
isa_utbrudd seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 13, day 0, field save › changed /Runtime; added /Metocean: expected "2128f991b26ee1ea", actual "93704a382be93d5b"
```

### krise_manager seed 1

```
krise_manager seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 1, day 0, field save › changed /Runtime; added /Metocean: expected "8a175fad3c0c5e09", actual "dcaf6f83bf6e3c82"
```

### krise_manager seed 7

```
krise_manager seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 7, day 0, field save › changed /Runtime; added /Metocean: expected "3d586ae0b7cc9daf", actual "bc4713ea6c32c3ee"
```

### krise_manager seed 13

```
krise_manager seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 13, day 0, field save › changed /Runtime; added /Metocean: expected "7fdfddaa3b8293f7", actual "3fe82581fa083e83"
```

### laksebaronene seed 1

```
laksebaronene seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 1, day 0, field save › changed /Runtime; added /Metocean: expected "da1b451fa580c228", actual "c8643a82b8767f09"
```

### laksebaronene seed 7

```
laksebaronene seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 7, day 0, field save › changed /Runtime; added /Metocean: expected "af329bfc7942cc06", actual "073fdbed529c952e"
```

### laksebaronene seed 13

```
laksebaronene seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 13, day 0, field save › changed /Runtime; added /Metocean: expected "bc9e276618684060", actual "ab3e2cccf25bd86c"
```

### lofoten_eventyr seed 1

```
lofoten_eventyr seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 1, day 0, field save › changed /Runtime; added /Metocean: expected "01bd6a8fb747d6ce", actual "59f72e96fd4edf79"
```

### lofoten_eventyr seed 7

```
lofoten_eventyr seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 7, day 0, field save › changed /Runtime; added /Metocean: expected "bce9917dd6ba2cda", actual "711a0a15960ab860"
```

### lofoten_eventyr seed 13

```
lofoten_eventyr seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 13, day 0, field save › changed /Runtime; added /Metocean: expected "7d4d86df3f2ff858", actual "17401d935feddc93"
```

### po3_red_sone seed 1

```
po3_red_sone seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 1, day 0, field save › changed /Runtime; added /Metocean: expected "c913af6d3c21bcb5", actual "50a418595583c728"
```

### po3_red_sone seed 7

```
po3_red_sone seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 7, day 0, field save › changed /Runtime; added /Metocean: expected "d86c6a08bcb73080", actual "e123b75bc3c04a3d"
```

### po3_red_sone seed 13

```
po3_red_sone seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 13, day 0, field save › changed /Runtime; added /Metocean: expected "5da30f29ea846963", actual "d7e0bf8933649c42"
```

### ressursrente seed 1

```
ressursrente seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 1, day 0, field save › changed /Runtime; added /Metocean: expected "5fce6a4faa23c9a7", actual "2f285350eb086a62"
```

### ressursrente seed 7

```
ressursrente seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 7, day 0, field save › changed /Runtime; added /Metocean: expected "63317055fca124a4", actual "48f653c542f13911"
```

### ressursrente seed 13

```
ressursrente seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 13, day 0, field save › changed /Runtime; added /Metocean: expected "2d3da6bbbdf51f8a", actual "4822577fa8a7e61a"
```

