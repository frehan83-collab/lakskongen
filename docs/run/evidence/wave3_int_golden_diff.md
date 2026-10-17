# Golden-trace diff report

## Why the traces changed (wave-3 integration `wave3/int`, `27b0af6` against `c054300`)

Recorded with `tools/record_golden_traces.sh --commit 27b0af6 --base c054300 --report docs/run/evidence/wave3_int_golden_diff.md`
(both commits on this machine, math fingerprints equal); the fixtures were re-recorded once on the combined tree with
`tools/record_golden_traces.sh --update` (HEAD `27b0af6`, the last merge). Between the two commits lie five merged branches;
three change no game: `ci/perf-gate-calibration` (a test), `wave3/sim-lanes-2` (standalone models, no wiring) and
`wave3/ui-a-2` (U2-04/U2-05: 27 of 27 identical games on its branch, save drift only); inside the two that do
(`wave3/sim-core-4`, `wave3/ui-b-2`) F2-02, F2-03, F2-05 and U2-01 were 27 of 27 identical on their branches, so
only F2-04 and U2-03 change games. Every difference below has one of three causes:

1. **Save drift in all 27 traces** (additive save fields, reported, never a failure). The full list at day 0
   (`save_drift`, the comparer's rule): changed `/Runtime` (F2-04's lifetime `Depreciation` in the saved ledger, F2-05's
   `MktDailyPrices`); added `/SitePlots` and per sea site `Kind`, `PlotId`, `Offshore`, `AppliedContainmentCredit`
   (F2-03), `/FacilityAssets`, `/ConstructionProjects`, `/PermitCases`, `/NextConstructionProjectNumber`,
   `/NextPermitCaseNumber`, `/RentRingFencedCostsThisTaxYear` (F2-04), `/Driftsplan` (F2-05), per sea site `FeedPlan`
   (U2-04), `/Liquidity`, `/PnlStory` (U2-03). Nothing removed.
2. **F2-04's research narrowing (po3_red_sone x3, from day 9, the lice group).** The scenario starts with
   `deep-sea-farming` researched; its lice and disease effects now reach offshore units only, and po3_red_sone has
   none, so the fjord sites' settlement blocking falls on day 9 and the day-30 snapshot names `site/po3_nord`'s stages
   slightly higher; treatments, deaths, harvests, cash and the market follow (722 of 731 days). This is F2-04's own
   diff (`docs/run/evidence/f2_04_golden_diff.md`).
3. **U2-03's overdraft interest and the cash-bound golden player (20 traces).** Negative closing cash pays 12.25 % a
   year, booked as `cost/interest` (the first snapshot difference of every such trace); the golden player's restock
   ("within cash for smolts and feed") reacts to the lower cash in isa_utbrudd x3 and po3_red_sone x3 (population,
   lice, mortality, MTB and environment follow; isa_utbrudd seed 13 ends with 672 t instead of 1,048 t and -31.4
   instead of -40.6 MNOK); the bank's ladder adds `liquidity_warning`, `liquidity_bank_call` and
   `liquidity_rescue_expired` events (laksebaronene seed 13 and ressursrente seeds 7 and 13 differ by these events
   alone; the golden player never takes a rescue loan, so the ladder moves no cash). For the 24 traces outside
   po3_red_sone, every row of the table below (verdict, first divergence, days differing, final cash and biomass) is
   exactly the row of U2-03's own report (`git show wave3/ui-b-2:docs/run/evidence/U2-03_golden_diff.md`).

**The two causes in po3_red_sone, separated.** Recorded against `771c04e` (the `wave3/sim-core-4` tip, F2-04 already
in) the combined tree's po3_red_sone traces first differ on days 190, 204 and 193 in the ledger with a `cost/interest`
line on the day-210 snapshot: U2-03's days for the same seeds on its own base. All other 24 traces are again U2-03's
rows exactly (7 of 27 identical). So the po3 rows below are F2-04 (from day 9) and U2-03 (from day ~190) compounded:
final cash -12.11 → -12.39, -15.25 → -15.79, -13.77 → -14.41 MNOK for seeds 1, 7, 13 (F2-04 alone -11.91, -15.11,
-13.81; U2-03 alone -12.44, -15.74, -14.35). No difference is left unexplained.

**The 7 identical games** (barekraft_mester x3, forste_sesong x3, laksebaronene seed 1) never bring cash below zero and
never research a narrowed node: save drift only.

- expected: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.MtNK1g/out/base` (commit c054300a61cb, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.MtNK1g/out/head` (commit 27b0af604a52, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |
|---|---|---|---|---|---|---|
| algekatastrofe | 1 | **differs** | 184: ledger › (group hash; the next snapshot names the entity) | 547/731 | -26147391 → -26922217 | 1073733 → 1073733 |
| algekatastrofe | 7 | **differs** | 198: ledger › (group hash; the next snapshot names the entity) | 533/731 | -42296263 → -42785628 | 1635643 → 1635643 |
| algekatastrofe | 13 | **differs** | 189: ledger › (group hash; the next snapshot names the entity) | 542/731 | -46351695 → -46892364 | 1617876 → 1617876 |
| barekraft_mester | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more) | 0 | — | — |
| barekraft_mester | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more) | 0 | — | — |
| barekraft_mester | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more) | 0 | — | — |
| forste_sesong | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more) | 0 | — | — |
| forste_sesong | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more) | 0 | — | — |
| forste_sesong | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more) | 0 | — | — |
| isa_utbrudd | 1 | **differs** | 184: ledger › (group hash; the next snapshot names the entity) | 547/731 | -27984579 → -28580489 | 519523 → 506959 |
| isa_utbrudd | 7 | **differs** | 195: ledger › (group hash; the next snapshot names the entity) | 536/731 | -29853656 → -30371737 | 370875 → 349286 |
| isa_utbrudd | 13 | **differs** | 185: ledger › (group hash; the next snapshot names the entity) | 546/731 | -40591445 → -31406304 | 1048113 → 672204 |
| krise_manager | 1 | **differs** | 1: ledger › (group hash; the next snapshot names the entity) | 730/731 | -1459126 → -1688180 | 0 → 0 |
| krise_manager | 7 | **differs** | 1: ledger › (group hash; the next snapshot names the entity) | 730/731 | -2192903 → -2671560 | 0 → 0 |
| krise_manager | 13 | **differs** | 1: ledger › (group hash; the next snapshot names the entity) | 730/731 | -1367027 → -1584096 | 0 → 0 |
| laksebaronene | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more) | 0 | — | — |
| laksebaronene | 7 | **differs** | 290: ev › events | 423/731 | -678747 → -684617 | 90604 → 90604 |
| laksebaronene | 13 | **differs** | 298: ev › events | 1/731 | 404433 → 404433 | 0 → 0 |
| lofoten_eventyr | 1 | **differs** | 202: ledger › (group hash; the next snapshot names the entity) | 529/731 | 1249395 → 1245986 | 0 → 0 |
| lofoten_eventyr | 7 | **differs** | 211: ledger › (group hash; the next snapshot names the entity) | 520/731 | 1208258 → 1205234 | 23408 → 23408 |
| lofoten_eventyr | 13 | **differs** | 235: ledger › (group hash; the next snapshot names the entity) | 496/731 | 778934 → 778900 | 0 → 0 |
| po3_red_sone | 1 | **differs** | 9: lice › (group hash; the next snapshot names the entity) | 722/731 | -12108791 → -12385068 | 216291 → 211150 |
| po3_red_sone | 7 | **differs** | 9: lice › (group hash; the next snapshot names the entity) | 722/731 | -15246624 → -15786032 | 118538 → 105367 |
| po3_red_sone | 13 | **differs** | 9: lice › (group hash; the next snapshot names the entity) | 722/731 | -13769652 → -14407545 | 243668 → 240658 |
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
  save drift in 26 snapshots, first scenario algekatastrofe, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+19 more): expected "628daaef4237ada8", actual "d7535ed5210fb0c6"
```

### algekatastrofe seed 7

```
golden trace mismatch: scenario algekatastrofe, seed 7, day 198, field ledger › (group hash; the next snapshot names the entity): expected "37", actual "81"
  533 of 731 days differ (cash 532, ev 10, ledger 528)
  first snapshot difference: scenario algekatastrofe, seed 7, day 210, field ledger › cost/interest: expected absent, actual nok=6450.16
  final cash -42296263 → -42785628 NOK
  save drift in 26 snapshots, first scenario algekatastrofe, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+19 more): expected "a19b0fb2ffdf4b45", actual "244aeef48b2264e0"
```

### algekatastrofe seed 13

```
golden trace mismatch: scenario algekatastrofe, seed 13, day 189, field ledger › (group hash; the next snapshot names the entity): expected "46", actual "62"
  542 of 731 days differ (cash 542, ev 12, ledger 540)
  first snapshot difference: scenario algekatastrofe, seed 13, day 210, field ledger › cost/interest: expected absent, actual nok=24380.49
  final cash -46351695 → -46892364 NOK
  save drift in 26 snapshots, first scenario algekatastrofe, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+19 more): expected "48d1ec46ab8e22c5", actual "dfbb585064dd63ce"
```

### barekraft_mester seed 1

```
barekraft_mester seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "644c96859861c047", actual "3bff0fdf541cc1f1"
```

### barekraft_mester seed 7

```
barekraft_mester seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "d71f2168bf3edb4f", actual "d7cc645878f78cb8"
```

### barekraft_mester seed 13

```
barekraft_mester seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "41c29555e2c48e04", actual "e947539171daa974"
```

### forste_sesong seed 1

```
forste_sesong seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "73897993e53a5184", actual "8d7d982a9d0e213b"
```

### forste_sesong seed 7

```
forste_sesong seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "50fa20b1f53dbf22", actual "b7876d8ebbccd48f"
```

### forste_sesong seed 13

```
forste_sesong seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "c822c92ed7992bab", actual "2f597cc4a3f52967"
```

### isa_utbrudd seed 1

```
golden trace mismatch: scenario isa_utbrudd, seed 1, day 184, field ledger › (group hash; the next snapshot names the entity): expected "b8", actual "4e"
  547 of 731 days differ (cash 545, cmd 3, env 400, ev 28, ledger 544, lice 401, mort 400, mtb 399, ops 359, pop 400)
  first snapshot difference: scenario isa_utbrudd, seed 1, day 210, field ledger › cost/interest: expected absent, actual nok=16719.61
  final cash -27984579 → -28580489 NOK
  final live biomass 519523 → 506959 kg
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+19 more): expected "08d512f4dd5472ae", actual "cb2896dbc550d811"
```

### isa_utbrudd seed 7

```
golden trace mismatch: scenario isa_utbrudd, seed 7, day 195, field ledger › (group hash; the next snapshot names the entity): expected "df", actual "d6"
  536 of 731 days differ (cash 532, cmd 2, env 392, ev 29, ledger 535, lice 392, mort 392, mtb 393, ops 366, pop 393)
  first snapshot difference: scenario isa_utbrudd, seed 7, day 210, field ledger › cost/interest: expected absent, actual nok=6648
  final cash -29853656 → -30371737 NOK
  final live biomass 370875 → 349286 kg
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+19 more): expected "de2ca6311aae02a1", actual "5e61c6ef3794b27d"
```

### isa_utbrudd seed 13

```
golden trace mismatch: scenario isa_utbrudd, seed 13, day 185, field ledger › (group hash; the next snapshot names the entity): expected "d2", actual "79"
  546 of 731 days differ (cash 543, cmd 2, env 391, ev 67, ledger 544, lice 393, market 196, mort 393, mtb 392, ops 315, pop 395, rivals 93, rng 197)
  first snapshot difference: scenario isa_utbrudd, seed 13, day 210, field ledger › cost/interest: expected absent, actual nok=16211.6
  final cash -40591445 → -31406304 NOK
  final live biomass 1048113 → 672204 kg
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+19 more): expected "b4eead42f2239927", actual "2128f991b26ee1ea"
```

### krise_manager seed 1

```
golden trace mismatch: scenario krise_manager, seed 1, day 1, field ledger › (group hash; the next snapshot names the entity): expected "e5", actual "80"
  730 of 731 days differ (cash 726, ev 2, ledger 726)
  first snapshot difference: scenario krise_manager, seed 1, day 30, field ledger › cost/interest: expected absent, actual nok=5810.31
  final cash -1459126 → -1688180 NOK
  save drift in 26 snapshots, first scenario krise_manager, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "447e6fd6e0a6d845", actual "8a175fad3c0c5e09"
```

### krise_manager seed 7

```
golden trace mismatch: scenario krise_manager, seed 7, day 1, field ledger › (group hash; the next snapshot names the entity): expected "ff", actual "28"
  730 of 731 days differ (cash 726, ev 2, ledger 729)
  first snapshot difference: scenario krise_manager, seed 7, day 30, field ledger › cost/interest: expected absent, actual nok=6223.52
  final cash -2192903 → -2671560 NOK
  save drift in 26 snapshots, first scenario krise_manager, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "c80cbc2478bb2642", actual "3d586ae0b7cc9daf"
```

### krise_manager seed 13

```
golden trace mismatch: scenario krise_manager, seed 13, day 1, field ledger › (group hash; the next snapshot names the entity): expected "2e", actual "fb"
  730 of 731 days differ (cash 728, ev 4, ledger 728, ops 314)
  first snapshot difference: scenario krise_manager, seed 13, day 30, field ledger › cost/interest: expected absent, actual nok=6041.4
  final cash -1367027 → -1584096 NOK
  save drift in 26 snapshots, first scenario krise_manager, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "169105d3c49be06a", actual "7fdfddaa3b8293f7"
```

### laksebaronene seed 1

```
laksebaronene seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "4d495f5c7a13349c", actual "da1b451fa580c228"
```

### laksebaronene seed 7

```
golden trace mismatch: scenario laksebaronene, seed 7, day 290, field ev › events: expected , actual ["liquidity_warning"]
  423 of 731 days differ (cash 419, ev 4, ledger 418)
  first snapshot difference: scenario laksebaronene, seed 7, day 330, field ledger › cost/interest: expected absent, actual nok=31.53
  final cash -678747 → -684617 NOK
  save drift in 26 snapshots, first scenario laksebaronene, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "2500f5deadf4f349", actual "af329bfc7942cc06"
```

### laksebaronene seed 13

```
golden trace mismatch: scenario laksebaronene, seed 13, day 298, field ev › events: expected ["Storm Damage"], actual ["Storm Damage","liquidity_warning"]
  1 of 731 days differ (ev 1)
  save drift in 26 snapshots, first scenario laksebaronene, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "e9ffa0a465806517", actual "bc9e276618684060"
```

### lofoten_eventyr seed 1

```
golden trace mismatch: scenario lofoten_eventyr, seed 1, day 202, field ledger › (group hash; the next snapshot names the entity): expected "0e", actual "82"
  529 of 731 days differ (cash 529, ledger 527)
  first snapshot difference: scenario lofoten_eventyr, seed 1, day 210, field ledger › cost/interest: expected absent, actual nok=263.16
  final cash 1249395 → 1245986 NOK
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "8a957f004e0f28f0", actual "01bd6a8fb747d6ce"
```

### lofoten_eventyr seed 7

```
golden trace mismatch: scenario lofoten_eventyr, seed 7, day 211, field ledger › (group hash; the next snapshot names the entity): expected "c1", actual "59"
  520 of 731 days differ (cash 518, ledger 518)
  first snapshot difference: scenario lofoten_eventyr, seed 7, day 240, field ledger › cost/interest: expected absent, actual nok=2974.68
  final cash 1208258 → 1205234 NOK
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "d9bae32bc7d7f6e8", actual "bce9917dd6ba2cda"
```

### lofoten_eventyr seed 13

```
golden trace mismatch: scenario lofoten_eventyr, seed 13, day 235, field ledger › (group hash; the next snapshot names the entity): expected "45", actual "87"
  496 of 731 days differ (cash 494, ledger 492)
  first snapshot difference: scenario lofoten_eventyr, seed 13, day 240, field ledger › cost/interest: expected absent, actual nok=42.99
  final cash 778934 → 778900 NOK
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+9 more): expected "e4225f3afbdfe48e", actual "7d4d86df3f2ff858"
```

### po3_red_sone seed 1

```
golden trace mismatch: scenario po3_red_sone, seed 1, day 9, field lice › (group hash; the next snapshot names the entity): expected "f0", actual "a3"
  722 of 731 days differ (cash 538, cmd 2, env 432, ev 20, ledger 536, lice 716, market 315, mort 459, mtb 431, ops 432, pop 429, rivals 185)
  first snapshot difference: scenario po3_red_sone, seed 1, day 30, field lice › site/po3_nord › ch, pa, af, am, cop: expected ch=0.09373416098120464, pa=0.008871113495944839, af=0.00013517254017527658, am=0.00013517254017527658, cop=0.6169412064977837, actual ch=0.09392349943661087, pa=0.008882379606485592, af=0.00013530393511427166, am=0.00013530393511427166, cop=0.6169412928043899
  final cash -12108791 → -12385068 NOK
  final live biomass 216291 → 211150 kg
  save drift in 26 snapshots, first scenario po3_red_sone, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+14 more): expected "1ef8f306ced9ee10", actual "c913af6d3c21bcb5"
```

### po3_red_sone seed 7

```
golden trace mismatch: scenario po3_red_sone, seed 7, day 9, field lice › (group hash; the next snapshot names the entity): expected "b9", actual "e4"
  722 of 731 days differ (cash 525, cmd 2, env 495, ev 21, ledger 525, lice 719, market 202, mort 524, mtb 495, ops 436, pop 497, rivals 1, st 2, state 1)
  first snapshot difference: scenario po3_red_sone, seed 7, day 30, field lice › site/po3_nord › ch, pa, af, am, cop: expected ch=0.09401898423244832, pa=0.008703762576170018, af=0.00012962341256746214, am=0.00012962341256746214, cop=0.61757298181705, actual ch=0.09420873245066766, pa=0.008714837958113238, af=0.00012974911118687413, am=0.00012974911118687413, cop=0.6175730627240784
  final cash -15246624 → -15786032 NOK
  final live biomass 118538 → 105367 kg
  save drift in 26 snapshots, first scenario po3_red_sone, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+14 more): expected "ff772d7d0f44fd7c", actual "d86c6a08bcb73080"
```

### po3_red_sone seed 13

```
golden trace mismatch: scenario po3_red_sone, seed 13, day 9, field lice › (group hash; the next snapshot names the entity): expected "f7", actual "f1"
  722 of 731 days differ (cash 535, cmd 1, env 437, ev 21, ledger 535, lice 716, market 141, mort 466, mtb 433, ops 417, pop 436, state 2)
  first snapshot difference: scenario po3_red_sone, seed 13, day 30, field lice › site/po3_nord › ch, pa, af, am, cop: expected ch=0.09365120028336284, pa=0.008916118067897258, af=0.00013687526752124697, am=0.00013687526752124697, cop=0.6167336230471537, actual ch=0.09384066710228889, pa=0.008927198772501083, af=0.0001370027214660622, am=0.0001370027214660622, cop=0.6167337050563557
  final cash -13769652 → -14407545 NOK
  final live biomass 243668 → 240658 kg
  save drift in 26 snapshots, first scenario po3_red_sone, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+14 more): expected "525c7606c2b50bf9", actual "5da30f29ea846963"
```

### ressursrente seed 1

```
golden trace mismatch: scenario ressursrente, seed 1, day 656, field ev › events: expected , actual ["liquidity_warning"]
  18 of 731 days differ (cash 15, ev 3, ledger 15)
  first snapshot difference: scenario ressursrente, seed 1, day 720, field ledger › cost/interest: expected absent, actual nok=537.22
  final cash -2567810 → -2569313 NOK
  save drift in 26 snapshots, first scenario ressursrente, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+19 more): expected "59b826f9f22fdfed", actual "5fce6a4faa23c9a7"
```

### ressursrente seed 7

```
golden trace mismatch: scenario ressursrente, seed 7, day 247, field ev › events: expected ["R\u00F8mming","R\u00F8mming","R\u00F8mming","Slakting bestilt","Slakting bestilt"], actual ["R\u00F8mming","R\u00F8mming","R\u00F8mming","liquidity_warning","Slakting bestilt","Slakting bestilt"]
  3 of 731 days differ (ev 3)
  save drift in 26 snapshots, first scenario ressursrente, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+19 more): expected "42a33fb8b3e1d82a", actual "63317055fca124a4"
```

### ressursrente seed 13

```
golden trace mismatch: scenario ressursrente, seed 13, day 249, field ev › events: expected , actual ["liquidity_warning"]
  1 of 731 days differ (ev 1)
  save drift in 26 snapshots, first scenario ressursrente, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /Driftsplan, /FacilityAssets, /Liquidity, /NextConstructionProjectNumber, /NextPermitCaseNumber (+19 more): expected "3cb92d7739630852", actual "2d3da6bbbdf51f8a"
```

