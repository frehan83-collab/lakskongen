# S10 — Career-divergence probe (session 4)

**Gate:** pre-registered failure criterion, registered BEFORE running (file header
`src/LakskongenSim.Tests/Integration/CareerDivergenceProbe.cs` + this report).
**Verdict: PASS** — 2 of 3 thresholds crossed; the red-light career path
meaningfully diverges from the compliant path on an identical seed.

## Pre-registered criterion (unchanged since first draft)

A (red-light) vs B (compliant) scripted careers on the SAME SEED must diverge by
at least ONE of:
- ≥ 200,000 kg difference in forced-harvest biomass, **or**
- ≥ 5,000,000 NOK difference in penalty revenue, **or**
- ≥ 50,000,000 NOK difference in ending balance.

AND A must actually exercise the mechanic (≥1 forced harvest, penalty revenue > 0) —
a vacuous comparison of zeros fails loudly as an inconclusive probe.

## Rig

- `dotnet test src/LakskongenSim.Tests --filter "FullyQualifiedName~CareerDivergenceProbe"`
  → **Passed: 1** (33 evidence lines).
- Seed `20261012`; 30 simulated days; identical builds; only the daily lice
  policy differs.
- A: site-level lice model re-seeded to red level daily (operator never
  delouses) → traffic light logs **Red**, Mattilsynet over-MAB order issued
  (deadline day + 14), deadline missed → forced harvest at 70 % of market price.
- B: lice kept suppressed → zone stays green → no orders, no penalties.
- Mechanic provenance: same state machine shipped in `MattilsynetOrderTests`
  (order issue on RED entry → deadline → forced harvest / fulfillment).

## Raw results

```
[S10] A red-light: forcedHarvests=1, harvested=299500 kg, penalty=18,449,200 NOK, balance=123,881,729 NOK
[S10] B compliant: forcedHarvests=0, harvested=0 kg, penalty=0 NOK, balance=99,719,322 NOK
[S10] divergence: harvested=299500 kg (need ≥ 200000), penalty=18,449,200 (need ≥ 5,000,000), balance=24,162,407 (need ≥ 50,000,000)
```

Timeline excerpt (A):

```
[S10][A] day=1  zone=Red order=True deadline=14 lice=2.00
...
[S10][A] day=14 zone=Red order=True deadline=14 lice=2.00   ← deadline day (forced harvest executes)
```

## Threshold scorecard

| Metric | Measured divergence | Threshold | Crossed |
|---|---|---|---|
| Forced-harvest biomass | +299,500 kg | ≥ 200,000 kg | **YES** |
| Penalty revenue | +18,449,200 NOK | ≥ 5,000,000 NOK | **YES** |
| Ending balance | +24,162,407 NOK | ≥ 50,000,000 NOK | no (not required — "at least one") |

## Notes / follow-ups

- The ending-balance threshold (50M) is deliberately high for a 30-day / 300 t
  rig; the harvest + penalty thresholds carry the criterion. Observed balance
  delta (~24M) is consistent with penalty revenue minus daily operating costs
  plus penalty-harvest proceeds (300 t @ ~61.6 NOK/kg penalty price).
- After the forced harvest the site is back at MAB and the order is fulfilled —
  exactly one forced harvest per career window, as designed.
- The weekly lice-census mirror (site model → pens) was the key rig insight:
  pen-level seeding is overwritten on census days; the probe seeds the
  site-level model (the true state) instead.
