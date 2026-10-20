# F2-08/F2-09 golden traces (`wave4/sim-sea-2`)

Recorded with `tools/record_golden_traces.sh --base 93c79c5` (the branch point, F2-07's last commit) at `7790a0a` (the branch's last code change; later commits change docs and test labels only). Every game is identical on every day (every traced group and every stream): 27 of 27. The only drift is the save's shape: the new `GameState` lists and counters (`DevelopmentRounds`, `DevelopmentAwards`, `OffshoreIncidents`, `IncidentReports`, `SiteWithdrawals`, their counters, `RivalOffshore`) and the new `LicenceHolding` fields (`Kind`, `BoundUnitId`, `ExpiresDay`, `PlacedZoneId`, `AwaitingPlacement`, `AwardId`) at their defaults. No shipped scenario has an offshore unit, and a development round's field is drawn from a private stream, so no game stream moves. The fixtures are not re-recorded.

# Golden-trace diff report

- expected: `<temporary recording of 93c79c5>` (commit 93c79c538655, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- actual: `<temporary recording of 7790a0a>` (commit 7790a0a3775d, rid osx-arm64, runtime .NET 10.0.12, math 8481617aed3cdd1d)
- math fingerprints are equal: the traces are comparable bit for bit

| scenario | seed | verdict | first divergence (day: group › field) | days differing | final cash NOK (expected → actual) | final live biomass kg (expected → actual) |
|---|---|---|---|---|---|---|
| algekatastrofe | 1 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| algekatastrofe | 7 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| algekatastrofe | 13 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| barekraft_mester | 1 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2], /LicenceHoldings[3], /LicenceHoldings[4], /LicenceHoldings[5] (+2 more); added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| barekraft_mester | 7 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2], /LicenceHoldings[3], /LicenceHoldings[4], /LicenceHoldings[5] (+2 more); added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| barekraft_mester | 13 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2], /LicenceHoldings[3], /LicenceHoldings[4], /LicenceHoldings[5] (+2 more); added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| forste_sesong | 1 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| forste_sesong | 7 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| forste_sesong | 13 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| isa_utbrudd | 1 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| isa_utbrudd | 7 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| isa_utbrudd | 13 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| krise_manager | 1 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| krise_manager | 7 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| krise_manager | 13 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| laksebaronene | 1 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| laksebaronene | 7 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| laksebaronene | 13 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| lofoten_eventyr | 1 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| lofoten_eventyr | 7 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| lofoten_eventyr | 13 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| po3_red_sone | 1 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| po3_red_sone | 7 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| po3_red_sone | 13 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| ressursrente | 1 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| ressursrente | 7 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |
| ressursrente | 13 | identical game, save drift in 26 snapshots | 0: changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more) | 0 | — | — |

27 of 27 traces identical.

## Details

### algekatastrofe seed 1

```
algekatastrofe seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 1, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "444a491484f869fc", actual "08d14c0d4717bfbb"
```

### algekatastrofe seed 7

```
algekatastrofe seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 7, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "2fc3cbb663f636d2", actual "d5ed0ae4d76d63aa"
```

### algekatastrofe seed 13

```
algekatastrofe seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario algekatastrofe, seed 13, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "9fc50a4d83a31367", actual "44c81b75e378eb9e"
```

### barekraft_mester seed 1

```
barekraft_mester seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 1, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2], /LicenceHoldings[3], /LicenceHoldings[4], /LicenceHoldings[5] (+2 more); added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "4860057e7d1b5bb2", actual "3bdd51a2e9bc8bc0"
```

### barekraft_mester seed 7

```
barekraft_mester seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 7, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2], /LicenceHoldings[3], /LicenceHoldings[4], /LicenceHoldings[5] (+2 more); added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "dbb6e32dc11afc00", actual "7c02102fa2198d60"
```

### barekraft_mester seed 13

```
barekraft_mester seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario barekraft_mester, seed 13, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2], /LicenceHoldings[3], /LicenceHoldings[4], /LicenceHoldings[5] (+2 more); added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "3cc8d0d7fc14d3bc", actual "259f54982398ee3a"
```

### forste_sesong seed 1

```
forste_sesong seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 1, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "1f7fe2a6863c2c10", actual "e7815a224a1fe977"
```

### forste_sesong seed 7

```
forste_sesong seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 7, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "08e947b8280d4ae5", actual "e7cef1e9aea1b24d"
```

### forste_sesong seed 13

```
forste_sesong seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario forste_sesong, seed 13, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "920008e82fff03bb", actual "de7b35af7de9a916"
```

### isa_utbrudd seed 1

```
isa_utbrudd seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 1, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "e4c329b298f64bc1", actual "455746ac606ed554"
```

### isa_utbrudd seed 7

```
isa_utbrudd seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 7, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "1dfab712445f5e2c", actual "194f2b5fa10b82c5"
```

### isa_utbrudd seed 13

```
isa_utbrudd seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario isa_utbrudd, seed 13, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "93704a382be93d5b", actual "4df45b262c4f02f3"
```

### krise_manager seed 1

```
krise_manager seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 1, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "dcaf6f83bf6e3c82", actual "c2cf233898da8b25"
```

### krise_manager seed 7

```
krise_manager seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 7, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "bc4713ea6c32c3ee", actual "296fc3c167936fac"
```

### krise_manager seed 13

```
krise_manager seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario krise_manager, seed 13, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "3fe82581fa083e83", actual "cb19cc5711c0f842"
```

### laksebaronene seed 1

```
laksebaronene seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 1, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "c8643a82b8767f09", actual "2ba9ac0a169911f1"
```

### laksebaronene seed 7

```
laksebaronene seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 7, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "073fdbed529c952e", actual "b77dfdaeae87a378"
```

### laksebaronene seed 13

```
laksebaronene seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario laksebaronene, seed 13, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "ab3e2cccf25bd86c", actual "ac0b0fdc73a6cf50"
```

### lofoten_eventyr seed 1

```
lofoten_eventyr seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 1, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "59f72e96fd4edf79", actual "330c4082a3b21ba9"
```

### lofoten_eventyr seed 7

```
lofoten_eventyr seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 7, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "711a0a15960ab860", actual "dfa301f05f23f6d5"
```

### lofoten_eventyr seed 13

```
lofoten_eventyr seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario lofoten_eventyr, seed 13, day 0, field save › changed /LicenceHoldings[0]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "17401d935feddc93", actual "2317dd8f796071b2"
```

### po3_red_sone seed 1

```
po3_red_sone seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 1, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "50a418595583c728", actual "66c931d692917332"
```

### po3_red_sone seed 7

```
po3_red_sone seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 7, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "e123b75bc3c04a3d", actual "730e7a95121bd93f"
```

### po3_red_sone seed 13

```
po3_red_sone seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario po3_red_sone, seed 13, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "d7e0bf8933649c42", actual "25b4fa4574b52342"
```

### ressursrente seed 1

```
ressursrente seed 1: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 1, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "2f285350eb086a62", actual "fc04f3fe9faf1c98"
```

### ressursrente seed 7

```
ressursrente seed 7: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 7, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "48f653c542f13911", actual "c8e0ab38a7b8a4e9"
```

### ressursrente seed 13

```
ressursrente seed 13: identical over 731 days
  save drift in 26 snapshots, first scenario ressursrente, seed 13, day 0, field save › changed /LicenceHoldings[0], /LicenceHoldings[1], /LicenceHoldings[2]; added /DevelopmentAwards, /DevelopmentRounds, /IncidentReports, /NextIncidentReportNumber, /NextSiteWithdrawalNumber, /OffshoreIncidents (+2 more): expected "4822577fa8a7e61a", actual "299b6a2682e22e30"
```

