# Golden-trace diff report

## Why the traces changed (U2-03, commit 05a5c26 against the branch point d1e7361)

Recorded with `tools/record_golden_traces.sh --commit 05a5c26 --base d1e7361 --report docs/run/evidence/U2-03_golden_diff.md`;
the fixtures were re-recorded with `--commit 05a5c26 --update`. Every difference below has one of four causes, all deliberate:

1. **Save drift in all 27 traces** (`added /Liquidity, /PnlStory`): the save gains the liquidity ladder's state (closing-cash
   history, the episode's warning and bank-call days, the rescue offer, overdraft interest paid) and the P&L story (months,
   lifetime totals per ledger line, one-off runs). Additive fields; drift is reported, never a failure.
2. **Overdraft interest** (RUN_LOG section 8 "overdraft is free"): every trace whose cash closes a day below zero now pays
   12.25 % p.a. on it (act/365, the statutory late-payment rate, H2 2025 and H2 2026), booked as interest. The first snapshot
   difference of each such trace is a new `ledger > cost/interest` line on the first snapshot after cash went negative
   (algekatastrofe x3, isa_utbrudd x3, krise_manager x3 from day 1, lofoten_eventyr x3, po3_red_sone x3, laksebaronene seed 7,
   ressursrente seed 1), and its final cash is lower by the interest paid (algekatastrofe seed 1: -0.77 MNOK over 730 days;
   lofoten_eventyr seed 13: -34 NOK).
3. **The golden player's restock rule reacts to the lower cash** ("restock within cash for smolts and feed"): in
   isa_utbrudd x3 and po3_red_sone x3 the interest delays or skips a restock, so the population, lice, mortality, MTB,
   environment and (for isa_utbrudd seed 13 and po3_red_sone seed 1) market, rivals and RNG groups follow a different path
   from the first skipped restock on. isa_utbrudd seed 13 ends with 672 t instead of 1,048 t live and -31.4 instead of
   -40.6 MNOK: fewer smolts and less feed bought. No rule other than the player's cash check is involved.
4. **The bank's liquidity ladder** (events only): the event log gains `liquidity_warning` (runway to the bankruptcy floor
   <= 60 days at the running burn), `liquidity_bank_call` (<= 28 days, with or without a rescue offer) and
   `liquidity_rescue_expired` (the golden player never answers the bank). laksebaronene seed 13 and ressursrente seeds 7 and
   13 differ by these events alone (a pre-harvest dip toward the floor without negative cash); the golden player never takes
   a rescue loan, so the ladder changes no cash in any trace.


**Re-recorded at eb44b8e (save shape only).** After this report the P&L story's
runs became one ledger line's income and costs together (a harvest nets its
harvest cost) and gained revenue/cost parts. `tools/record_golden_traces.sh
--commit eb44b8e --against src/LakskongenSim.Tests/Fixtures/golden_traces`:
27 of 27 traces identical, every one an identical game with save drift in
`/PnlStory/Closed`, `/PnlStory/Open` only; the fixtures were re-recorded at
eb44b8e (`--update`) so they carry the final save shape. The game differences
above (base d1e7361 to 05a5c26) are the whole behaviour change of U2-03.

The 7 identical games (barekraft_mester x3, forste_sesong x3, laksebaronene seed 1) never bring cash toward the floor.
Liquidity events per new trace: algekatastrofe 1-2 warnings + 1 call + offer expired + bankrupt; isa_utbrudd 2 warnings +
1 call + bankrupt (no offer expires in them); po3_red_sone seed 7 2 warnings + 1 call +
bankrupt; ressursrente seed 1 warning + call + expired offer + bankrupt.

- expected: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.8lEfDi/out/base` (commit d1e7361c6755, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.8lEfDi/out/head` (commit 05a5c268ef4a, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |
|---|---|---|---|---|---|---|
| algekatastrofe | 1 | **differs** | 184: ledger › (group hash; the next snapshot names the entity) | 547/731 | -26147391 → -26922217 | 1073733 → 1073733 |
| algekatastrofe | 7 | **differs** | 198: ledger › (group hash; the next snapshot names the entity) | 533/731 | -42296263 → -42785628 | 1635643 → 1635643 |
| algekatastrofe | 13 | **differs** | 189: ledger › (group hash; the next snapshot names the entity) | 542/731 | -46351695 → -46892364 | 1617876 → 1617876 |
| barekraft_mester | 1 | identical game, save drift in 26 snapshots | 0: added /Liquidity, /PnlStory | 0 | — | — |
| barekraft_mester | 7 | identical game, save drift in 26 snapshots | 0: added /Liquidity, /PnlStory | 0 | — | — |
| barekraft_mester | 13 | identical game, save drift in 26 snapshots | 0: added /Liquidity, /PnlStory | 0 | — | — |
| forste_sesong | 1 | identical game, save drift in 26 snapshots | 0: added /Liquidity, /PnlStory | 0 | — | — |
| forste_sesong | 7 | identical game, save drift in 26 snapshots | 0: added /Liquidity, /PnlStory | 0 | — | — |
| forste_sesong | 13 | identical game, save drift in 26 snapshots | 0: added /Liquidity, /PnlStory | 0 | — | — |
| isa_utbrudd | 1 | **differs** | 184: ledger › (group hash; the next snapshot names the entity) | 547/731 | -27984579 → -28580489 | 519523 → 506959 |
| isa_utbrudd | 7 | **differs** | 195: ledger › (group hash; the next snapshot names the entity) | 536/731 | -29853656 → -30371737 | 370875 → 349286 |
| isa_utbrudd | 13 | **differs** | 185: ledger › (group hash; the next snapshot names the entity) | 546/731 | -40591445 → -31406304 | 1048113 → 672204 |
| krise_manager | 1 | **differs** | 1: ledger › (group hash; the next snapshot names the entity) | 730/731 | -1459126 → -1688180 | 0 → 0 |
| krise_manager | 7 | **differs** | 1: ledger › (group hash; the next snapshot names the entity) | 730/731 | -2192903 → -2671560 | 0 → 0 |
| krise_manager | 13 | **differs** | 1: ledger › (group hash; the next snapshot names the entity) | 730/731 | -1367027 → -1584096 | 0 → 0 |
| laksebaronene | 1 | identical game, save drift in 26 snapshots | 0: added /Liquidity, /PnlStory | 0 | — | — |
| laksebaronene | 7 | **differs** | 290: ev › events | 423/731 | -678747 → -684617 | 90604 → 90604 |
| laksebaronene | 13 | **differs** | 298: ev › events | 1/731 | 404433 → 404433 | 0 → 0 |
| lofoten_eventyr | 1 | **differs** | 202: ledger › (group hash; the next snapshot names the entity) | 529/731 | 1249395 → 1245986 | 0 → 0 |
| lofoten_eventyr | 7 | **differs** | 211: ledger › (group hash; the next snapshot names the entity) | 520/731 | 1208258 → 1205234 | 23408 → 23408 |
| lofoten_eventyr | 13 | **differs** | 235: ledger › (group hash; the next snapshot names the entity) | 496/731 | 778934 → 778900 | 0 → 0 |
| po3_red_sone | 1 | **differs** | 190: ledger › (group hash; the next snapshot names the entity) | 541/731 | -12108791 → -12435875 | 216291 → 213935 |
| po3_red_sone | 7 | **differs** | 204: ledger › (group hash; the next snapshot names the entity) | 527/731 | -15246624 → -15735734 | 118538 → 105533 |
| po3_red_sone | 13 | **differs** | 193: ledger › (group hash; the next snapshot names the entity) | 538/731 | -13769652 → -14345273 | 243668 → 241221 |
| ressursrente | 1 | **differs** | 656: ev › events | 18/731 | -2567810 → -2569313 | 1207292 → 1207292 |
| ressursrente | 7 | **differs** | 247: ev › events | 3/731 | 75948395 → 75948395 | 512813 → 512813 |
| ressursrente | 13 | **differs** | 249: ev › events | 1/731 | 14657919 → 14657919 | 846531 → 846531 |

7 of 27 traces identical.

## Details

### algekatastrofe seed 1

```
golden trace mismatch: scenario algekatastrofe, seed 1, day 184, field ledger › (group hash; the next snapshot names the entity): expected "6e", actual "73"
  547 of 731 days differ (cash 546, ev 5, ledger 543)
  first snapshot difference: scenario algekatastrofe, seed 1, day 210, field ledger › cost/interest: expected absent, actual nok=27873.88
  final cash -26147391 → -26922217 NOK
  save drift in 26 snapshots, first scenario algekatastrofe, seed 1, day 0, field save › added /Liquidity, /PnlStory: expected "628daaef4237ada8", actual "5f981a270fffa26f"
```

### algekatastrofe seed 7

```
golden trace mismatch: scenario algekatastrofe, seed 7, day 198, field ledger › (group hash; the next snapshot names the entity): expected "37", actual "81"
  533 of 731 days differ (cash 532, ev 10, ledger 528)
  first snapshot difference: scenario algekatastrofe, seed 7, day 210, field ledger › cost/interest: expected absent, actual nok=6450.16
  final cash -42296263 → -42785628 NOK
  save drift in 26 snapshots, first scenario algekatastrofe, seed 7, day 0, field save › added /Liquidity, /PnlStory: expected "a19b0fb2ffdf4b45", actual "8e49ad81eab97349"
```

### algekatastrofe seed 13

```
golden trace mismatch: scenario algekatastrofe, seed 13, day 189, field ledger › (group hash; the next snapshot names the entity): expected "46", actual "62"
  542 of 731 days differ (cash 542, ev 12, ledger 540)
  first snapshot difference: scenario algekatastrofe, seed 13, day 210, field ledger › cost/interest: expected absent, actual nok=24380.49
  final cash -46351695 → -46892364 NOK
  save drift in 26 snapshots, first scenario algekatastrofe, seed 13, day 0, field save › added /Liquidity, /PnlStory: expected "48d1ec46ab8e22c5", actual "d53f62155d95fd55"
```

### barekraft_mester seed 1

```
barekraft_mester seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 1, day 0, field save › added /Liquidity, /PnlStory: expected "644c96859861c047", actual "20aec0de9aeb16a8"
```

### barekraft_mester seed 7

```
barekraft_mester seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 7, day 0, field save › added /Liquidity, /PnlStory: expected "d71f2168bf3edb4f", actual "8daa8f9504d0c4e0"
```

### barekraft_mester seed 13

```
barekraft_mester seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 13, day 0, field save › added /Liquidity, /PnlStory: expected "41c29555e2c48e04", actual "c839089ba5a8cfd1"
```

### forste_sesong seed 1

```
forste_sesong seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 1, day 0, field save › added /Liquidity, /PnlStory: expected "73897993e53a5184", actual "823ef1bb72f0bd84"
```

### forste_sesong seed 7

```
forste_sesong seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 7, day 0, field save › added /Liquidity, /PnlStory: expected "50fa20b1f53dbf22", actual "db3b930246689b41"
```

### forste_sesong seed 13

```
forste_sesong seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 13, day 0, field save › added /Liquidity, /PnlStory: expected "c822c92ed7992bab", actual "9e6286ab9df3ec7e"
```

### isa_utbrudd seed 1

```
golden trace mismatch: scenario isa_utbrudd, seed 1, day 184, field ledger › (group hash; the next snapshot names the entity): expected "b8", actual "4e"
  547 of 731 days differ (cash 545, cmd 3, env 400, ev 28, ledger 544, lice 401, mort 400, mtb 399, ops 359, pop 400)
  first snapshot difference: scenario isa_utbrudd, seed 1, day 210, field ledger › cost/interest: expected absent, actual nok=16719.61
  final cash -27984579 → -28580489 NOK
  final live biomass 519523 → 506959 kg
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 1, day 0, field save › added /Liquidity, /PnlStory: expected "08d512f4dd5472ae", actual "9979edb5785d8085"
```

### isa_utbrudd seed 7

```
golden trace mismatch: scenario isa_utbrudd, seed 7, day 195, field ledger › (group hash; the next snapshot names the entity): expected "df", actual "d6"
  536 of 731 days differ (cash 532, cmd 2, env 392, ev 29, ledger 535, lice 392, mort 392, mtb 393, ops 366, pop 393)
  first snapshot difference: scenario isa_utbrudd, seed 7, day 210, field ledger › cost/interest: expected absent, actual nok=6648
  final cash -29853656 → -30371737 NOK
  final live biomass 370875 → 349286 kg
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 7, day 0, field save › added /Liquidity, /PnlStory: expected "de2ca6311aae02a1", actual "56a0e0dfd8c0dffb"
```

### isa_utbrudd seed 13

```
golden trace mismatch: scenario isa_utbrudd, seed 13, day 185, field ledger › (group hash; the next snapshot names the entity): expected "d2", actual "79"
  546 of 731 days differ (cash 543, cmd 2, env 391, ev 67, ledger 544, lice 393, market 196, mort 393, mtb 392, ops 315, pop 395, rivals 93, rng 197)
  first snapshot difference: scenario isa_utbrudd, seed 13, day 210, field ledger › cost/interest: expected absent, actual nok=16211.6
  final cash -40591445 → -31406304 NOK
  final live biomass 1048113 → 672204 kg
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 13, day 0, field save › added /Liquidity, /PnlStory: expected "b4eead42f2239927", actual "f0d1419c0c0a56fc"
```

### krise_manager seed 1

```
golden trace mismatch: scenario krise_manager, seed 1, day 1, field ledger › (group hash; the next snapshot names the entity): expected "e5", actual "80"
  730 of 731 days differ (cash 726, ev 2, ledger 726)
  first snapshot difference: scenario krise_manager, seed 1, day 30, field ledger › cost/interest: expected absent, actual nok=5810.31
  final cash -1459126 → -1688180 NOK
  save drift in 26 snapshots, first scenario krise_manager, seed 1, day 0, field save › added /Liquidity, /PnlStory: expected "447e6fd6e0a6d845", actual "b23ae0cea754402f"
```

### krise_manager seed 7

```
golden trace mismatch: scenario krise_manager, seed 7, day 1, field ledger › (group hash; the next snapshot names the entity): expected "ff", actual "28"
  730 of 731 days differ (cash 726, ev 2, ledger 729)
  first snapshot difference: scenario krise_manager, seed 7, day 30, field ledger › cost/interest: expected absent, actual nok=6223.52
  final cash -2192903 → -2671560 NOK
  save drift in 26 snapshots, first scenario krise_manager, seed 7, day 0, field save › added /Liquidity, /PnlStory: expected "c80cbc2478bb2642", actual "b3ebfc464b3f7ea8"
```

### krise_manager seed 13

```
golden trace mismatch: scenario krise_manager, seed 13, day 1, field ledger › (group hash; the next snapshot names the entity): expected "2e", actual "fb"
  730 of 731 days differ (cash 728, ev 4, ledger 728, ops 314)
  first snapshot difference: scenario krise_manager, seed 13, day 30, field ledger › cost/interest: expected absent, actual nok=6041.4
  final cash -1367027 → -1584096 NOK
  save drift in 26 snapshots, first scenario krise_manager, seed 13, day 0, field save › added /Liquidity, /PnlStory: expected "169105d3c49be06a", actual "f34adf4cb19d4bcd"
```

### laksebaronene seed 1

```
laksebaronene seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 1, day 0, field save › added /Liquidity, /PnlStory: expected "4d495f5c7a13349c", actual "6897dfc348904c64"
```

### laksebaronene seed 7

```
golden trace mismatch: scenario laksebaronene, seed 7, day 290, field ev › events: expected , actual ["liquidity_warning"]
  423 of 731 days differ (cash 419, ev 4, ledger 418)
  first snapshot difference: scenario laksebaronene, seed 7, day 330, field ledger › cost/interest: expected absent, actual nok=31.53
  final cash -678747 → -684617 NOK
  save drift in 26 snapshots, first scenario laksebaronene, seed 7, day 0, field save › added /Liquidity, /PnlStory: expected "2500f5deadf4f349", actual "41a78daef0e5ade0"
```

### laksebaronene seed 13

```
golden trace mismatch: scenario laksebaronene, seed 13, day 298, field ev › events: expected ["Storm Damage"], actual ["Storm Damage","liquidity_warning"]
  1 of 731 days differ (ev 1)
  save drift in 26 snapshots, first scenario laksebaronene, seed 13, day 0, field save › added /Liquidity, /PnlStory: expected "e9ffa0a465806517", actual "8e62f348fca125a0"
```

### lofoten_eventyr seed 1

```
golden trace mismatch: scenario lofoten_eventyr, seed 1, day 202, field ledger › (group hash; the next snapshot names the entity): expected "0e", actual "82"
  529 of 731 days differ (cash 529, ledger 527)
  first snapshot difference: scenario lofoten_eventyr, seed 1, day 210, field ledger › cost/interest: expected absent, actual nok=263.16
  final cash 1249395 → 1245986 NOK
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 1, day 0, field save › added /Liquidity, /PnlStory: expected "8a957f004e0f28f0", actual "ebbf1046a8c3fbde"
```

### lofoten_eventyr seed 7

```
golden trace mismatch: scenario lofoten_eventyr, seed 7, day 211, field ledger › (group hash; the next snapshot names the entity): expected "c1", actual "59"
  520 of 731 days differ (cash 518, ledger 518)
  first snapshot difference: scenario lofoten_eventyr, seed 7, day 240, field ledger › cost/interest: expected absent, actual nok=2974.68
  final cash 1208258 → 1205234 NOK
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 7, day 0, field save › added /Liquidity, /PnlStory: expected "d9bae32bc7d7f6e8", actual "0c6ae3ffc83d099b"
```

### lofoten_eventyr seed 13

```
golden trace mismatch: scenario lofoten_eventyr, seed 13, day 235, field ledger › (group hash; the next snapshot names the entity): expected "45", actual "87"
  496 of 731 days differ (cash 494, ledger 492)
  first snapshot difference: scenario lofoten_eventyr, seed 13, day 240, field ledger › cost/interest: expected absent, actual nok=42.99
  final cash 778934 → 778900 NOK
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 13, day 0, field save › added /Liquidity, /PnlStory: expected "e4225f3afbdfe48e", actual "08b1c3ba2009297e"
```

### po3_red_sone seed 1

```
golden trace mismatch: scenario po3_red_sone, seed 1, day 190, field ledger › (group hash; the next snapshot names the entity): expected "b9", actual "e5"
  541 of 731 days differ (cash 540, cmd 2, env 400, ev 20, ledger 539, lice 401, market 187, mort 393, mtb 401, ops 302, pop 400, rivals 185)
  first snapshot difference: scenario po3_red_sone, seed 1, day 210, field ledger › cost/interest: expected absent, actual nok=8508.74
  final cash -12108791 → -12435875 NOK
  final live biomass 216291 → 213935 kg
  save drift in 26 snapshots, first scenario po3_red_sone, seed 1, day 0, field save › added /Liquidity, /PnlStory: expected "1ef8f306ced9ee10", actual "b339f5e83077aa65"
```

### po3_red_sone seed 7

```
golden trace mismatch: scenario po3_red_sone, seed 7, day 204, field ledger › (group hash; the next snapshot names the entity): expected "4b", actual "6b"
  527 of 731 days differ (cash 526, cmd 2, env 384, ev 20, ledger 526, lice 387, market 2, mort 387, mtb 386, ops 357, pop 388, rivals 1, st 2)
  first snapshot difference: scenario po3_red_sone, seed 7, day 210, field ledger › cost/interest: expected absent, actual nok=857.05
  final cash -15246624 → -15735734 NOK
  final live biomass 118538 → 105533 kg
  save drift in 26 snapshots, first scenario po3_red_sone, seed 7, day 0, field save › added /Liquidity, /PnlStory: expected "ff772d7d0f44fd7c", actual "5ed77da587ad074f"
```

### po3_red_sone seed 13

```
golden trace mismatch: scenario po3_red_sone, seed 13, day 193, field ledger › (group hash; the next snapshot names the entity): expected "3c", actual "1f"
  538 of 731 days differ (cash 536, cmd 1, env 384, ev 21, ledger 537, lice 384, mort 384, mtb 386, pop 386)
  first snapshot difference: scenario po3_red_sone, seed 13, day 210, field ledger › cost/interest: expected absent, actual nok=5239.26
  final cash -13769652 → -14345273 NOK
  final live biomass 243668 → 241221 kg
  save drift in 26 snapshots, first scenario po3_red_sone, seed 13, day 0, field save › added /Liquidity, /PnlStory: expected "525c7606c2b50bf9", actual "5cbee0ee000ec525"
```

### ressursrente seed 1

```
golden trace mismatch: scenario ressursrente, seed 1, day 656, field ev › events: expected , actual ["liquidity_warning"]
  18 of 731 days differ (cash 15, ev 3, ledger 15)
  first snapshot difference: scenario ressursrente, seed 1, day 720, field ledger › cost/interest: expected absent, actual nok=537.22
  final cash -2567810 → -2569313 NOK
  save drift in 26 snapshots, first scenario ressursrente, seed 1, day 0, field save › added /Liquidity, /PnlStory: expected "59b826f9f22fdfed", actual "da70ef3bac678863"
```

### ressursrente seed 7

```
golden trace mismatch: scenario ressursrente, seed 7, day 247, field ev › events: expected ["R\u00F8mming","R\u00F8mming","R\u00F8mming","Slakting bestilt","Slakting bestilt"], actual ["R\u00F8mming","R\u00F8mming","R\u00F8mming","liquidity_warning","Slakting bestilt","Slakting bestilt"]
  3 of 731 days differ (ev 3)
  save drift in 26 snapshots, first scenario ressursrente, seed 7, day 0, field save › added /Liquidity, /PnlStory: expected "42a33fb8b3e1d82a", actual "bd61b537407067e1"
```

### ressursrente seed 13

```
golden trace mismatch: scenario ressursrente, seed 13, day 249, field ev › events: expected , actual ["liquidity_warning"]
  1 of 731 days differ (ev 1)
  save drift in 26 snapshots, first scenario ressursrente, seed 13, day 0, field save › added /Liquidity, /PnlStory: expected "3cb92d7739630852", actual "067d4548f3dab720"
```

