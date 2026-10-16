# Golden-trace diff report

## F2-04: what changed, and why (wave3/sim-core-3)

Recorded with `tools/record_golden_traces.sh --base d3e536c` (the branch point,
`wave3/sim-core-2`) against `191bb13` (F2-04's research gates and narrowed
effects); the fixtures in `src/LakskongenSim.Tests/Fixtures/golden_traces` were
re-recorded on `191bb13` with `--update`. Spec §3.1 decision 0 and §7.1
acceptance 4: F2-04 narrows the research effects, so the goldens change, and
only the runs that research the two narrowed nodes may change.

- **24 of 27 runs: the same game.** Every day line of every scenario that
  never researches `land-based-ras` or `deep-sea-farming` is identical. The
  golden player researches the cheapest node it can afford once, after its
  first harvest: `premium-products` (0.5 M) in 24 runs, none in krise_manager's
  three (read from the traces' commands), never one of the two.
- **Save drift in every run (expected, never a failure).** The save gains
  F2-04's additive fields, empty or zero in a fjord-only game:
  `/FacilityAssets`, `/ConstructionProjects`, `/PermitCases`,
  `/NextConstructionProjectNumber`, `/NextPermitCaseNumber`,
  `/RentRingFencedCostsThisTaxYear`, and `/Runtime` changes because the saved
  lifetime ledger (`FinLifetime`) gains `Depreciation` (0). No traced value moves:
  the period depreciation is 0 and adds no cost line, the ring-fenced costs are 0
  and leave the rent base's decimal as it was, and the post-tick
  `ConstructionAndPermits` phase draws and books nothing without a project, a
  case or an asset.
- **po3_red_sone, seeds 1, 7, 13: differs from day 9, in the lice.** The
  scenario starts with `deep-sea-farming` researched. Before F2-04 its lice
  reduction (0.10 of new settlement) and disease resistance (0.06) acted on the
  two fjord sites; now they act on offshore units only, and the scenario has
  none. The first group to move is the lice (day 9: the sites' settlement
  blocking falls from 1 − (1 − c)(1 − 0.10) to the containment and skirt's c);
  the day-30 snapshot names `site/po3_nord`'s chalimus, pre-adults and adults
  slightly higher. From there the treatments, deaths, harvests, cash and the
  market follow (722 of 731 days). Final cash: seed 1 −12.11 M → −11.91 M,
  seed 7 −15.25 M → −15.11 M, seed 13 −13.77 M → −13.81 M; final live biomass
  216 → 213 t, 119 → 112 t, 244 → 243 t. This is the intended change: a fjord
  company no longer gets a lice and disease upgrade from offshore research.
- **No other F2-04 change reaches a shipped scenario.** The rent ring-fence,
  the facility depreciation, construction projects and permit cases act only
  on F2 units, and the golden player never builds one (their own pins:
  `FarmTypeMoveTests`, `FacilityAssetRegisterTests`, `RentRingFenceTests`).

- expected: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.xHeBds/out/base` (commit d3e536cfcb49, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `/var/folders/q4/52l4rkg548l_9pvgj9fxbj000000gn/T/golden_traces.xHeBds/out/head` (commit 191bb136df80, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |
|---|---|---|---|---|---|---|
| algekatastrofe | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| algekatastrofe | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| algekatastrofe | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| barekraft_mester | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| barekraft_mester | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| barekraft_mester | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| forste_sesong | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| forste_sesong | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| forste_sesong | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| isa_utbrudd | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| isa_utbrudd | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| isa_utbrudd | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| krise_manager | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| krise_manager | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| krise_manager | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| laksebaronene | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| laksebaronene | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| laksebaronene | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| lofoten_eventyr | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| lofoten_eventyr | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| lofoten_eventyr | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| po3_red_sone | 1 | **differs** | 9: lice › (group hash; the next snapshot names the entity) | 722/731 | -12108791 → -11910854 | 216291 → 213499 |
| po3_red_sone | 7 | **differs** | 9: lice › (group hash; the next snapshot names the entity) | 722/731 | -15246624 → -15112946 | 118538 → 112101 |
| po3_red_sone | 13 | **differs** | 9: lice › (group hash; the next snapshot names the entity) | 722/731 | -13769652 → -13805066 | 243668 → 243310 |
| ressursrente | 1 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| ressursrente | 7 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |
| ressursrente | 13 | identical game, save drift in 26 snapshots | 0: changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear | 0 | — | — |

24 of 27 traces identical.

## Details

### algekatastrofe seed 1

```
algekatastrofe seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "db3664b379792d98", actual "a945c8419b7a79e2"
```

### algekatastrofe seed 7

```
algekatastrofe seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "79850be10d446d56", actual "03217f6cc7438bb1"
```

### algekatastrofe seed 13

```
algekatastrofe seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "336a881f5c80a698", actual "6684e0535ec09f76"
```

### barekraft_mester seed 1

```
barekraft_mester seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "9c6fc4d3f68eb44f", actual "e823cad50bd6bd92"
```

### barekraft_mester seed 7

```
barekraft_mester seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "632f897db4b3d4bf", actual "5790374f489bb50b"
```

### barekraft_mester seed 13

```
barekraft_mester seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "4588a30e6c9dd645", actual "d0baa7b41d16f9d5"
```

### forste_sesong seed 1

```
forste_sesong seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "636bc79140add433", actual "18124a8c0c932619"
```

### forste_sesong seed 7

```
forste_sesong seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "21c9acf0ce5f89a9", actual "1c81bf9a68707261"
```

### forste_sesong seed 13

```
forste_sesong seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "30a6a73dd813a39b", actual "23331326f2fdb8d8"
```

### isa_utbrudd seed 1

```
isa_utbrudd seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "5b0cbf68cbc105d3", actual "a73e67f165f08f86"
```

### isa_utbrudd seed 7

```
isa_utbrudd seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "21acfd6168fa1a31", actual "552f96664d414b7a"
```

### isa_utbrudd seed 13

```
isa_utbrudd seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "0f652cee022b6202", actual "27a65a57ea974856"
```

### krise_manager seed 1

```
krise_manager seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "1dd95534faaa9d4a", actual "8aaced31338fc2e1"
```

### krise_manager seed 7

```
krise_manager seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "1f554fb3da48b773", actual "b67bfeb78c570de6"
```

### krise_manager seed 13

```
krise_manager seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "538f508d348f573d", actual "699946bf501ea0af"
```

### laksebaronene seed 1

```
laksebaronene seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "f322f92cea2a4bfb", actual "d23136bee3533052"
```

### laksebaronene seed 7

```
laksebaronene seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "65e9d7c458a5f224", actual "6bc80c93d2fb6e45"
```

### laksebaronene seed 13

```
laksebaronene seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "aae1707423b9e662", actual "d9089fd77567a7e7"
```

### lofoten_eventyr seed 1

```
lofoten_eventyr seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "090c627f29a25c5a", actual "aa0d347da16083ad"
```

### lofoten_eventyr seed 7

```
lofoten_eventyr seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "5cff665085119978", actual "34575f870e71cbfe"
```

### lofoten_eventyr seed 13

```
lofoten_eventyr seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "2cdb2866693d6d1f", actual "5bfbbf055ad4ac51"
```

### po3_red_sone seed 1

```
golden trace mismatch: scenario po3_red_sone, seed 1, day 9, field lice › (group hash; the next snapshot names the entity): expected "f0", actual "a3"
  722 of 731 days differ (cash 459, cmd 1, env 431, ev 15, ledger 460, lice 717, market 315, mort 461, mtb 428, ops 432, pop 429, rivals 185, state 1)
  first snapshot difference: scenario po3_red_sone, seed 1, day 30, field lice › site/po3_nord › ch, pa, af, am, cop: expected ch=0.09373416098120464, pa=0.008871113495944839, af=0.00013517254017527658, am=0.00013517254017527658, cop=0.6169412064977837, actual ch=0.09392349943661087, pa=0.008882379606485592, af=0.00013530393511427166, am=0.00013530393511427166, cop=0.6169412928043899
  final cash -12108791 → -11910854 NOK
  final live biomass 216291 → 213499 kg
  save drift in 26 snapshots, first scenario po3_red_sone, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "ec573d802c1086ba", actual "4e3d645b909c1490"
```

### po3_red_sone seed 7

```
golden trace mismatch: scenario po3_red_sone, seed 7, day 9, field lice › (group hash; the next snapshot names the entity): expected "b9", actual "e4"
  722 of 731 days differ (cash 522, cmd 2, env 496, ev 10, ledger 521, lice 719, market 202, mort 524, mtb 495, ops 436, pop 496, rivals 1, st 2, state 1)
  first snapshot difference: scenario po3_red_sone, seed 7, day 30, field lice › site/po3_nord › ch, pa, af, am, cop: expected ch=0.09401898423244832, pa=0.008703762576170018, af=0.00012962341256746214, am=0.00012962341256746214, cop=0.61757298181705, actual ch=0.09420873245066766, pa=0.008714837958113238, af=0.00012974911118687413, am=0.00012974911118687413, cop=0.6175730627240784
  final cash -15246624 → -15112946 NOK
  final live biomass 118538 → 112101 kg
  save drift in 26 snapshots, first scenario po3_red_sone, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "58d7dd843986068d", actual "1d10be8f55b637ac"
```

### po3_red_sone seed 13

```
golden trace mismatch: scenario po3_red_sone, seed 13, day 9, field lice › (group hash; the next snapshot names the entity): expected "f7", actual "f1"
  722 of 731 days differ (cash 464, cmd 1, env 436, ev 6, ledger 463, lice 715, market 141, mort 465, mtb 436, ops 417, pop 436, state 3)
  first snapshot difference: scenario po3_red_sone, seed 13, day 30, field lice › site/po3_nord › ch, pa, af, am, cop: expected ch=0.09365120028336284, pa=0.008916118067897258, af=0.00013687526752124697, am=0.00013687526752124697, cop=0.6167336230471537, actual ch=0.09384066710228889, pa=0.008927198772501083, af=0.0001370027214660622, am=0.0001370027214660622, cop=0.6167337050563557
  final cash -13769652 → -13805066 NOK
  final live biomass 243668 → 243310 kg
  save drift in 26 snapshots, first scenario po3_red_sone, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "cf79327c5d65f34b", actual "b9e15373f61f3b1a"
```

### ressursrente seed 1

```
ressursrente seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 1, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "708a0bedc4383dbb", actual "3cd9f34a17f9de0e"
```

### ressursrente seed 7

```
ressursrente seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 7, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "60a150b3bd7afa23", actual "417cb1c62cf32263"
```

### ressursrente seed 13

```
ressursrente seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 13, day 0, field save › changed /Runtime; added /ConstructionProjects, /FacilityAssets, /NextConstructionProjectNumber, /NextPermitCaseNumber, /PermitCases, /RentRingFencedCostsThisTaxYear: expected "3b056d294259580d", actual "f14f57dee3aafecd"
```

