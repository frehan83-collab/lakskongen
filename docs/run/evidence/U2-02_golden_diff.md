# U2-02 golden-trace diff (base 4f927a1 -> 2f41541)

Command: `tools/record_golden_traces.sh --base 4f927a1` (then `--update` on
2f41541 for the fixtures). **27 of 27 traces are the same game** (every day
line identical over 731 days); only the save drifts, in three nodes and no
other, in every trace (digest trees compared node by node, base vs head):

| Save node | Why it moved |
|---|---|
| `/TutorialSkipped` | Added (save v11): a skipped tutorial is saved, and a skip is no win (owner decision D19). |
| `/TutorialCurrentStepIndex` | The old track ran in every scenario and completed its day-count and placeholder steps (`day >= N`, `game_started`, "a second site exists"). The new track runs only in Første sesong (D19) and only on real UI or engine events: outside Første sesong it stays at 0; in Første sesong the golden player does no step's verb (no UI mark, no feeding plan, no standing order), so it stays at step 1. |
| `/CompletedTutorialStepIds[...]` | Same cause: no step completes in a golden run. |

Nothing the game simulates changed: no day group, no stream, no event, no
command, no scenario status (Første sesong was never won by the golden
player before either: it builds no second site).

# Golden-trace diff report

- expected: `<tmp>/base` (commit 4f927a1bc9e3, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `<tmp>/head` (commit 2f41541dbb6a, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |
|---|---|---|---|---|---|---|
| algekatastrofe | 1 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| algekatastrofe | 7 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| algekatastrofe | 13 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| barekraft_mester | 1 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| barekraft_mester | 7 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| barekraft_mester | 13 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| forste_sesong | 1 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| forste_sesong | 7 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| forste_sesong | 13 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| isa_utbrudd | 1 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| isa_utbrudd | 7 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| isa_utbrudd | 13 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| krise_manager | 1 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| krise_manager | 7 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| krise_manager | 13 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| laksebaronene | 1 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| laksebaronene | 7 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| laksebaronene | 13 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| lofoten_eventyr | 1 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| lofoten_eventyr | 7 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| lofoten_eventyr | 13 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| po3_red_sone | 1 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| po3_red_sone | 7 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| po3_red_sone | 13 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| ressursrente | 1 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| ressursrente | 7 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |
| ressursrente | 13 | identical game, save drift in 26 snapshots | 0: added /TutorialSkipped | 0 | — | — |

27 of 27 traces identical.

## Details

### algekatastrofe seed 1

```
algekatastrofe seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 1, day 0, field save › added /TutorialSkipped: expected "d7535ed5210fb0c6", actual "3cdf8875ca92472e"
```

### algekatastrofe seed 7

```
algekatastrofe seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 7, day 0, field save › added /TutorialSkipped: expected "244aeef48b2264e0", actual "3d9d71bf84e25627"
```

### algekatastrofe seed 13

```
algekatastrofe seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 13, day 0, field save › added /TutorialSkipped: expected "dfbb585064dd63ce", actual "b36c588a8078fcef"
```

### barekraft_mester seed 1

```
barekraft_mester seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 1, day 0, field save › added /TutorialSkipped: expected "3bff0fdf541cc1f1", actual "4e09bde03861969a"
```

### barekraft_mester seed 7

```
barekraft_mester seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 7, day 0, field save › added /TutorialSkipped: expected "d7cc645878f78cb8", actual "9428f8eda2c5d30e"
```

### barekraft_mester seed 13

```
barekraft_mester seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 13, day 0, field save › added /TutorialSkipped: expected "e947539171daa974", actual "0d87526f3c0df5c8"
```

### forste_sesong seed 1

```
forste_sesong seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 1, day 0, field save › added /TutorialSkipped: expected "8d7d982a9d0e213b", actual "601e9947b83d0bd7"
```

### forste_sesong seed 7

```
forste_sesong seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 7, day 0, field save › added /TutorialSkipped: expected "b7876d8ebbccd48f", actual "2fa9846deda77730"
```

### forste_sesong seed 13

```
forste_sesong seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 13, day 0, field save › added /TutorialSkipped: expected "2f597cc4a3f52967", actual "ab0e1818a2f55d95"
```

### isa_utbrudd seed 1

```
isa_utbrudd seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 1, day 0, field save › added /TutorialSkipped: expected "cb2896dbc550d811", actual "95cb6e2d1fdf5446"
```

### isa_utbrudd seed 7

```
isa_utbrudd seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 7, day 0, field save › added /TutorialSkipped: expected "5e61c6ef3794b27d", actual "8551c1f74430d5cc"
```

### isa_utbrudd seed 13

```
isa_utbrudd seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 13, day 0, field save › added /TutorialSkipped: expected "2128f991b26ee1ea", actual "51c51a47609fbacd"
```

### krise_manager seed 1

```
krise_manager seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 1, day 0, field save › added /TutorialSkipped: expected "8a175fad3c0c5e09", actual "544dab9155668ae2"
```

### krise_manager seed 7

```
krise_manager seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 7, day 0, field save › added /TutorialSkipped: expected "3d586ae0b7cc9daf", actual "6fabbb57f6fcb40b"
```

### krise_manager seed 13

```
krise_manager seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 13, day 0, field save › added /TutorialSkipped: expected "7fdfddaa3b8293f7", actual "a7766f82a0e756c0"
```

### laksebaronene seed 1

```
laksebaronene seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 1, day 0, field save › added /TutorialSkipped: expected "da1b451fa580c228", actual "a87a75999a012d09"
```

### laksebaronene seed 7

```
laksebaronene seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 7, day 0, field save › added /TutorialSkipped: expected "af329bfc7942cc06", actual "b3fdc3d66166cca3"
```

### laksebaronene seed 13

```
laksebaronene seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 13, day 0, field save › added /TutorialSkipped: expected "bc9e276618684060", actual "2bfc10b854274b5a"
```

### lofoten_eventyr seed 1

```
lofoten_eventyr seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 1, day 0, field save › added /TutorialSkipped: expected "01bd6a8fb747d6ce", actual "cfa25a08fedc381f"
```

### lofoten_eventyr seed 7

```
lofoten_eventyr seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 7, day 0, field save › added /TutorialSkipped: expected "bce9917dd6ba2cda", actual "9fe74954de04818f"
```

### lofoten_eventyr seed 13

```
lofoten_eventyr seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 13, day 0, field save › added /TutorialSkipped: expected "7d4d86df3f2ff858", actual "35e4ab41f707c3c3"
```

### po3_red_sone seed 1

```
po3_red_sone seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 1, day 0, field save › added /TutorialSkipped: expected "c913af6d3c21bcb5", actual "c471c3e1cc7c945b"
```

### po3_red_sone seed 7

```
po3_red_sone seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 7, day 0, field save › added /TutorialSkipped: expected "d86c6a08bcb73080", actual "6a30f66919523cc2"
```

### po3_red_sone seed 13

```
po3_red_sone seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 13, day 0, field save › added /TutorialSkipped: expected "5da30f29ea846963", actual "5d270e71c8e31e13"
```

### ressursrente seed 1

```
ressursrente seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 1, day 0, field save › added /TutorialSkipped: expected "5fce6a4faa23c9a7", actual "af44bcf93aba721a"
```

### ressursrente seed 7

```
ressursrente seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 7, day 0, field save › added /TutorialSkipped: expected "63317055fca124a4", actual "ca0d7fe908a60dcd"
```

### ressursrente seed 13

```
ressursrente seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 13, day 0, field save › added /TutorialSkipped: expected "2d3da6bbbdf51f8a", actual "edd2974d1a5d3234"
```

