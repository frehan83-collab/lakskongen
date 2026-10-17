# Balance & Realism Tuning Report (Lakskongen)

Single source of truth: `src/LakskongenSim/Calibration/CalibrationRegistry.cs`
(embedded fallback `godot/data/calibration.json`). Values are immutable
`init`-only records; `CalibrationGuardTests` fails the build when they drift
outside the reality bands named below.

This document is the human-readable companion to the registry: it records the
current game value, the real Norwegian reference, the source, and the
recommended playtest band for each high-leverage knob. Last reconciled:
**Oct 2026, calibration v1.0.3**; bands re-measured **2026-10-14** after the
S1-01 water-chemistry and S1-02 storm-season fixes (§1d), again after S2-02
(production-area sea temperatures and one calendar, calibration v1.0.6, §1f),
after S2-03 (TGC growth, §1g) and S2-04 (harvest pipeline, §1h), after wave 2
sim-a-1 (low oxygen at the fish, §1i) and on the integrated wave-2 sim tree
(`wave2/sim-int-1`, calibration v1.0.9, §1j).

## 1. Feed-cost path audit (end-to-end)

Feed is the dominant production cost (~50-65% of OPEX), so it is the most
sensitive economic knob. The path is:

```
godot/data/calibration.json
   └─ feed.feedPricePerKgFeedNOK / feed.baseFCR
        └─ CalibrationRegistry.Feed  (FeedCalibration)
             ├─ FeedCostPerKgProducedNOK = price × FCR       (derived)
             └─ BalancingConfig.FromRegistry (private, SimulationEngine)
                  └─ ApplyBalancingConfig()
                       ├─ FeedSystem.FeedCostPerKg = price
                       └─ FeedSystem.SetBaseFCR(FCR)
```

| Stage | Field / property | Game value | Real reference | Source | Status |
|---|---|---|---|---|---|
| `calibration.json` | `feed.feedPricePerKgFeedNOK` | 22.3 NOK/kg feed | ~20-24 delivered feed | Fiskeridir Key Figures; Mowi/SalMar annual | OK |
| `calibration.json` | `feed.baseFCR` | 1.15 | biological 1.14-1.15 | SalMar sustainability reports | OK |
| `CalibrationRegistry` | `Feed.FeedPricePerKgFeedNOK` | 22.3 | as above | registry = SSOT | OK |
| `CalibrationRegistry` | `Feed.BaseFCR` | 1.15 | as above | registry = SSOT | OK |
| `CalibrationRegistry` | `FeedCostPerKgProducedNOK` (derived) | **25.645** | **~25.7 NOK/kg produced** (2024) | Fiskeridir Tab.38 / Mowi annual | OK (was flat 12.5) |
| `SimulationEngine.BalancingConfig` | `FeedCostPerKgNOK` / `BaseFCR` | 22.3 / 1.15 | mirrors registry | `FromRegistry` | OK |
| `FeedSystem` (after engine load) | `FeedCostPerKg` / `BaseFCR` | 22.3 / 1.15 | mirrors registry | `ApplyBalancingConfig` | OK |
| `FeedSystem` (standalone default) | `FeedCostPerKg` / `BaseFCR` | **22.3 / 1.15** | mirrors registry | `CalibrationRegistry.Default` | **fixed this pass** (was 13.5 / 1.12 → 15.12 effective) |
| `EconomicEngine` | `ZoneFeedCosts` (delivered, per kg feed) | 18.5-23.5 | freight gradient around base | game design | OK (level fixed earlier from 37-45) |
| `MultiFarmManager.FarmSite` | `FeedCostPerKg` | 15 NOK/kg feed | abstract multi-site estimate | legacy | **out of scope / deferred** (see §5) |

Before this consolidation the registry and engine agreed (25.645), but a
bare `new FeedSystem()` used 13.5 NOK/kg feed × 1.12 FCR = **15.12 NOK/kg
produced** — a 41% understatement that any test, tool or subsystem bypassing
`ApplyBalancingConfig` would inherit. The default is now sourced from
`CalibrationRegistry.Default`, so the standalone path is identical to the
engine path.

## 1b. Measured distributions (SimProbe, seeds 1-20)

`src/LakskongenSim.Tests/Balance/SimProbe.cs` runs 20 seeded 3-year (1095-day)
grow-out sims per scenario, harvesting and re-stocking each time the stock
reaches the calibrated 5.2 kg target. `BalanceReportTests` asserts the medians
below against the reference bands in this document. Re-measured
**2026-10-14** after S1-01 and S1-02 (the before/after is §1d; fully
deterministic: the engine seeds growth, weather, market O-U and market
intelligence RNG from the same seed).

Scenario `Baseline`: one 50k-smolt sea site, storyteller events off, no zone.
This is the deterministic no-disease/no-storm path; its numbers are unchanged
by v1.0.3 (the calibrated all-cause anchor still drives the whole total). The
S1-02 weather chain moves them slightly (the sea temperature follows the weather
state).

| Metric | Measured min / **median** / p90 | Reference band | Verdict |
|---|---|---|---|
| Mean economic FCR | 1.20 / **1.22** / 1.25 | 1.10-1.30 (bFCR 1.15) | IN BAND |
| Sea mortality %/cycle | 14.25 / **15.67** / 20.18 | 14-16 (NVI 2024) | IN BAND |
| Mean harvest weight kg | 5.20 / **5.20** / 5.20 | 4.5-5.5 | IN BAND |
| Feed cost NOK/kg produced | 26.85 / **27.23** / 27.98 | 22-30 (~25.7) | IN BAND |
| Realized price NOK/kg | 81.21 / **88.00** / 88.00 | 74-90 | IN BAND |
| Peak adult-female lice/fish | 0.38 / **0.54** / 0.88 | ~0.5 action limit | IN BAND (median at limit) |
| Escapes/year | 0.00 / **0.00** / 0.00 | low (<=2 guard) | IN BAND |

Scenario `HighStocking` (100k smolt) is statistically identical on these
per-fish/per-cycle metrics — FCR 1.22, mortality 15.68%/cycle, harvest 5.20 kg,
feed cost 27.23 NOK/kg, realized price 88.0, lice 0.54 — confirming the bands
hold as farm size changes.

Note on the tails: the realized-price p90 sits at the normal-play band ceiling
(88 NOK/kg) rather than the hard ceiling; the lice p90 (0.88) exceeds the 0.5
action limit, which is expected for an *untreated* long/warm cycle and is the
whole reason delousing earns its cost.

### Tuning applied (calibration v1.0.2)

This pass closed the three measured gaps deferred in v1.0.1 by wiring the
calibration registry into the live models:

- **Sea mortality.** `MortalityModel` now consumes
  `CalibrationRegistry.Health.SeaMortalityPerCycle` (0.15) and maps it to a
  daily sea hazard over the calibrated grow-out:
  `1 - (1 - perCycle)^(1 / GrowthToHarvestDays)`, substituting for
  `GameConstants.BaselineMortalityPerDay` on the Seawater/Harvest stages only.
  Disease/event mortality stays additive, so `enableEvents:false` remains
  deterministic and the measured median moved 10.8% -> **15.8%**.
- **Market.** `MarketSimulation` sources all O-U parameters from
  `CalibrationRegistry.Market` (mean 82, weekly theta/sigma, 42-150 band),
  mean-reverts the supply/demand indices toward neutral every day, applies a
  bounded *decaying* supply/demand price offset (no compounding drift), and
  clamps spot and realized prices into a normal-play band (mean ±8) outside
  material shock windows. `MarketIntelligenceSystem`'s RNG is now seeded from
  the engine seed. Realized median moved ~153 -> **88.0 NOK/kg** and is
  reproducible run-to-run.
- **Lice.** `SimulationEngine` seeds external lice pressure on sea stocking
  (`SeaLice.SeedExternalInfection`) and modulates
  `SeaLice.SetExternalPressure` seasonally (late-summer peak, winter trough).
  Peak adult-female lice moved 0.00 -> **0.49/fish** (median), i.e. a clean
  grow-out reaches the regulatory action limit without treatment.

### Tuning applied (calibration v1.0.3)

This pass wired the two dormant depth systems into the live engine:

- **Disease is now an explicit slice of sea mortality.** `CalibrationRegistry.Health`
  gains `SeaMortalityDiseaseShare` (0.30). When `enableEvents:true`,
  `MortalityModel` uses the husbandry baseline `SeaMortalityPerCycle × (1 − share)`
  and `IntegratedDiseaseSystem` drives the base SIR, injecting the remaining
  slice through `MortalityModel.SetDiseaseMortalityRate`. The SIR is sized so
  its endemic daily hazard integrates to `SeaMortalityPerCycle × share` over the
  calibrated grow-out, and populations start at that endemic equilibrium, so the
  realized slice is deterministic and tunable. With `enableEvents:false` the full
  anchor stays in the baseline (no disease injection), so `Baseline` is unchanged
  and remains in the 14-16% band. Measured `DiseaseStress`: disease
  **4.39%/cycle**, total **14.88%/cycle** (seeds 1-20).
- **Storm escapes are engine-driven.** `SimulationEngine.ApplyStormHazards` (in
  the weather phase) owns net-integrity wear, repair, and escape accounting:
  storm days decay `SeaSite.NetIntegrityPct`, calm days repair it, a severe
  storm can cause a containment failure, and any site below the documented 40%
  risk line leaks on a storm day. Escapes flow through `SeaSite.RecordEscape`,
  charge the regulatory fine and write the event log. The duplicate escape /
  net-decay block was removed from `GameBridge.CheckWeatherImpacts`, leaving one
  source of truth. Deterministic for a given seed (storm timing is seeded).

### Tuning applied (calibration v1.0.1)

- `FeedSystem.FCRTempSensitivity` **0.018 -> 0.008 °C⁻¹** (and the reconciled
  `Feed.FCRTempSensitivity` in `CalibrationRegistry`/`calibration.json`).
  At 0.018 the seeded economic-FCR median was ~1.24 and feed cost ~27.7; at
  0.008 it centres on **1.20** and **26.8 NOK/kg produced**, versus the
  bFCR 1.15 / 25.7 NOK/kg references. Runtime authority is the `FeedSystem`
  constant; the registry field is the reconciled record.

## 1c. Extended probe — disease, escapes, multi-site economics (seeds 1-20)

`SimProbe` now also sweeps four extension scenarios, all pure in-process and
deterministic for a given seed. Re-measured **2026-10-14** after S1-01 and
S1-02 (before/after in §1d).

### Disease-cycle mortality (`SimScenario.DiseaseStress`)

`RunDisease` initialises with `enableEvents:true` (the disease subsystem live),
silences the random storyteller shocks so the only mortality difference versus
the deterministic baseline is the disease term, and measures disease-attributable
deaths exactly from the injected per-population rate
(`MortalityModel.GetSaveSnapshot`): for a one-day step
`survivors(without) - survivors(with) = count x diseaseRate`.

| Metric | Measured min / **median** / p90 | Reference / guard | Verdict |
|---|---|---|---|
| Disease mortality %/cycle | 4.29 / **4.37** / 8.53 | 0.5-10 (calibrated slice) | IN BAND |
| Total sea mortality %/cycle | 14.67 / **16.11** / 21.00 | 14-16 (NVI 2024) | IN BAND |

**v1.0.3 (wired):** the disease subsystem is no longer dormant. The base SIR is
ticked deterministically every day for every population and injects its mortality
through `MortalityModel.SetDiseaseMortalityRate`. When events are on the sea
anchor is split: the husbandry baseline becomes
`SeaMortalityPerCycle × (1 − SeaMortalityDiseaseShare)` and the SIR contributes
`SeaMortalityPerCycle × SeaMortalityDiseaseShare` (0.30 → ~4.4%/cycle measured),
so the two sum back to the calibrated 14-16% total. `SeaMortalityDiseaseShare` is
the tunable knob: raise it to shift more of the 15% onto disease, lower it to
return the burden to the baseline. With `enableEvents:false` the full anchor
stays in the baseline and disease never injects (deterministic path).

### Storm escapes (`SimScenario.Storm`)

`RunStorm` runs the seeded weather; the **engine itself** now owns storm
containment: `SimulationEngine.ApplyStormHazards` decays
`SeaSite.NetIntegrityPct` on storm days (0.3/day storm, 1.0/day severe, scaled by
containment/crew multipliers), repairs it 0.5/day on non-storm days (re-fit in
S1-02 from 0.2, which matched the old chain's Severe-free winters), and records
escapes through `SeaSite.RecordEscape`. A severe storm can cause a containment
failure (expected 1% per severe day releasing up to 5% of the pen at full wear,
scaled by containment `EscapeRiskMult`, `RemoteOpsCentre` watch-mode response and
`CrewEffects.StormLossMultiplier`); any site below the documented 40% risk line
also leaks on a storm day. Escapes charge the regulatory fine and write the event
log. The probe only reads the engine counters — there is no test-side model and
`GameBridge.CheckWeatherImpacts` no longer duplicates it. Deterministic for a
given seed (storm timing is seeded). Storm mortality, repair bills and
insurance claims (`ApplyStormAndTemperatureImpacts`) are charged once per storm
episode, plus once when it escalates from Storm to Severe (S1-02); they used to
fire on every Storm/Severe flip.

| Metric | Measured min / **median** / p90 | Reference / guard | Verdict |
|---|---|---|---|
| Escapes / site / year (fish) | 21.7 / **49.0** / 84.0 | low | IN BAND |
| Escape events / site / year | 0.32 / **0.42** / 0.46 | <=1 guard | IN BAND |
| Escapes % of stock / site / year | 0.13 / **0.36** / 0.50 | <=0.5% guard | IN BAND |
| Storm days / year | 136.0 / **145.0** / 155.3 | scenario exposure proof | OK |
| Min net integrity % | 68.8 / **82.9** / 85.9 | >=40 (SeaSite risk line) | IN BAND |

Real mapping: Norwegian escape events are rare and per-site annual escape rates
sit far below 1% of stock; the 0.5% guard is a conservative cap. This also keeps
the site inside the sustainability scoring band (0 escapes = full 20 pts,
1-10 = half, >10 = 0; `docs/SUSTAINABILITY_SYSTEM.md`).

### Multi-site economics (`SimScenario.ThreeSite`)

`RunThreeSite` operates three 50k-smolt sea sites together for the full horizon,
harvesting/re-stocking each as it reaches 5.2 kg, and reports pooled, farm-gate
P&L. OPEX uses the registry-consistent feed cost (price x economic FCR) plus the
calibrated smolt, labour, maintenance and harvest constants; downstream
processing is excluded so the EBIT/kg is comparable to how SalMar/Mowi report
farming-segment EBIT/kg.

| Metric | Measured min / **median** / p90 | Reference / guard | Verdict |
|---|---|---|---|
| Gross revenue NOK (3 sites) | 54.1M / **110.0M** / 113.1M | design | OK |
| Total OPEX NOK (3 sites) | 61.3M / **83.2M** / 83.6M | design | OK |
| **EBIT NOK/kg** | -11.20 / **20.11** / 22.48 | cyclical 8-25 | IN BAND |
| Cost NOK/kg produced | 62.63 / **63.21** / 79.39 | farm-gate, feed-dominant | IN BAND |
| Profit NOK / site | -2.38M / **8.87M** / 9.89M | positive | OK |
| Harvested kg / site | 212.6k / **439.0k** / 443.9k | ~50k x 5.2 kg x cycles | OK |

Cost stack per harvested kg: feed 25.645 (price x FCR), smolt 12/fish, sea labour
(7 workers/site x 48k/month), site maintenance (0.01%/day of build cost) and
wellboat harvest 4.0 — matching `docs/BALANCE.md` §2. Real mapping: the measured
median EBIT/kg **20.1** sits inside the SalMar/Mowi cyclical **8-25** band
(SalMar 23.4 FY24, 13.6 FY25); feed remains the dominant cost at ~25.7 NOK/kg
produced (Fiskeridir Tab.38).

### Lice control — treatments & resistance (`SimScenario.LiceControl`)

`RunLiceControl` applies a proactive delousing policy (a pyrethroid + a
mechanical pass at the calibrated trigger `Health.SeaLiceTreatmentThreshold`
= 0.15 adult females/fish) and reports treatments per cycle and the resistance
reached within a cycle (the engine resets the lice model at each sea stocking,
so resistance is a per-cycle quantity).

| Metric | Measured min / **median** / p90 | Reference / guard | Verdict |
|---|---|---|---|
| Treatments / cycle | 1.00 / **1.50** / 3.00 | proactive, low | OK |
| Max resistance % (per cycle) | 0.07 / **0.07** / 0.15 | low (<=5%) | IN BAND |
| Peak adult-female lice/fish | 0.15 / **0.15** / 0.15 | <=0.5 action limit | IN BAND |

Real mapping: treatment at the 0.15 trigger keeps the treated peak well under
the Mattilsynet 0.5 action limit (untreated cycles reach 0.49; §1b), and
per-cycle resistance development stays low because the model resets lice state
at sea stocking and resistance builds only from treatments applied within the
cycle.

## 1d. Re-measurement after S1-01 and S1-02 (2026-10-14, branch `wave1/sim-1`)

**S1-01** merged the F1024 water-chemistry fix (each species relaxes exactly over
the day toward its inflow baseline, oxygen toward saturation; the fish nutrient
load is a concentration over the zone volume; a bloom runs at least 14 days),
corrected the seawater oxygen solubility (Benson & Krause 1984 / APHA 4500-O:
9.02 mg/L at 10 °C and 35 PSU, was 10.9), raised the pH baseline to coastal
seawater (8.05, was 7.8) and lets every zone take the sea temperature each day.
**S1-02** renormalized the storm-season weather chain through one transition
helper shared with the 3-day forecast, charges storm damage once per storm
episode, re-fits routine net repair (0.2 to 0.5 integrity points per non-storm
day) and limits storm cooling to the stratified half of the year (Apr-Sep).

Columns: **base** = `3cfd4b2` (before S1-01), **S1-01** = `932d759`,
**S1-02** = the branch head. Measured with a temporary probe that calls the
harnesses the tests use (`SimProbe`, `PlayerScriptProbe`, the
`ScenarioCampaignTests` tutorial player); the S1-01 acceptance numbers are also
asserted by `WaterChemistryStabilityTests` and the S1-02 numbers by
`StormSeasonTests`.

### Water chemistry: the 600-day po3 probe

The po3 career script (events on, never harvested, so the load only grows),
seeds 7 and 13, both zones.

| Metric | base | S1-02 | S1-01 acceptance |
|---|---|---|---|
| Days with a species on a clamp | 599 of 600 | **0** | 0 |
| Days DO below 70% saturation | 306-307 (51%) | **4-5 (0.7-0.8%)**, Apr-Sep only | <= 10%, summer only |
| Days with an algal bloom | 593-597 (99%) | **0-14 (<= 2.3%)** | <= 15% |
| pH | 7.78-8.50 (on the 8.5 clamp) | **8.049-8.050** | 7.8-8.2 |
| Zone O2 min / mean / max, mg/L | 0.00 / 6.1 / 12.5-14.0 | 2.3-3.1 / 9.2-9.4 / 11.0 | |
| Nutrient level max, mg/L | 379-1,586 | 0.51-3.0 | |

Across 20 seeds (600 days, 24,000 zone-days, measured at S1-01) DO fell below
70% saturation on 0.69% of zone-days: 157 of 165 in June-September, 6 in late
April (spring-bloom crashes) and 2 in December/January. Those two come from a
plankton bloom that started in autumn and crashed in winter: `PlanktonBloom`
has no loss term while a bloom is active (follow-up for the mortality items).

### Algae and oxygen alerts: a 365-day default run

`forste_sesong` played passively, events on, seeds 4/7/9/13.

| Signal | base | S1-02 |
|---|---|---|
| Days with a zone algal bloom | 345-346 (near-permanent) | **0-14** (one storyteller bloom of 14 days, or none) |
| Days zone O2 = 0 mg/L | 184-185 | 0 |
| HUD oxygen alerts (`SimState`: 0 < O2 < 5 critical, 5-6.5 warning) | 6-8 critical days (the 184 zero days slipped past the `> 0` guard) | 2 critical (plankton-crash days), 0 warning |
| Days DO below 70% saturation | 186 | 2 |

### Weather (WeatherSystem alone, 10 seeds x 3 years)

| Metric | base | S1-02 |
|---|---|---|
| Oct-Mar Calm / Moderate / Storm / Severe | 32.1 / 37.2 / 30.7 / **0.0%** | 22.9 / 30.3 / 33.6 / **13.2%** |
| Apr-Sep Calm / Moderate / Storm / Severe | 31.5 / 37.5 / 22.7 / 8.3% | 31.4 / 37.5 / 22.7 / 8.4% |
| Exact stationary Storm+Severe, Oct-Mar vs Apr-Sep | 0.313 (Severe 0) vs 0.337 | **0.480 (Severe 0.138)** vs 0.337 |
| Storm episodes per year | 44.6 | 46.3 |
| Storm/Severe flips per year (the old charge rule) | 56.3 | 79.4 (now charged as 46.3 episodes plus escalations) |
| 3-day forecast vs realized, 400 seeds | 39.5% vs 32.5% (7.1 points high) | 40.95% vs 40.91% |

`StormSeasonTests` asserts the forecast within 2 points on 10 seeds (40.7% vs
39.2%), within 0.5 points on 200 seeds (40.95% vs 40.84%), and within 3 points
in each season.

### Careers (`PlayerScriptProbe`, po3_red_sone, events on, mid-run save/load)

| Seed | Stage | Days | Harvested kg | Realized NOK/kg | Econ FCR | Sea mortality | Ending balance |
|---|---|---|---|---|---|---|---|
| 7 | base | 480 | 444,117 | 78.12 | 1.531 | 26.7% | +10.68M |
| 7 | S1-01 | 311 | 515,767 | 75.52 (spot that day; career mean 80.46) | 1.178 | 18.4% | +23.83M |
| 7 | S1-02 | 311 | 520,293 | 75.52 (career mean 79.62) | 1.178 | 18.2% | +24.68M |
| 13 | base | 495 | 395,780 | 88.00 | 1.540 | 34.8% | +7.71M |
| 13 | S1-01 | 300 | 537,029 | 85.47 | 1.163 | 12.8% | +33.83M |
| 13 | S1-02 | 285 | 531,843 | 80.97 (career mean 83.13) | 1.163 | 12.5% | +29.97M |

The base column used the old probe (sell at 4.5 kg, 15-day steps, save on day
300); S1-01 and S1-02 use the D2 harvest rule (from 4.5 kg on a day spot is at
or above its 30-day mean, forced at 6 kg) and save on day 210, while both sites
still hold fish. Seed 7's bands: realized >= 0.95 x spot on the harvest days
(ratio 1.00), spot on the harvest days >= 0.9 x the career's mean spot (0.95),
FCR 1.0-1.6, mortality <= 65%, balance > -1M. Seed 13 keeps 70-96 NOK/kg,
FCR 0.9-2.6 and the D4 guard (balance > -15M); its old 61% mortality record is
now 12.5% (S2-05 still attributes the mortality).

### Tutorial (`forste_sesong`, the minimal player, seeds 4/7/9/21/13/42)

| Stage | Won | Finish day | First harvest day |
|---|---|---|---|
| base | 4 of 6 (seeds 13 and 42 stalled at step 16 by day 1500) | 428-1212 | 381-462 |
| S1-01 = S1-02 | **6 of 6** | 273-279 | 271-277 |

Removing the hidden hypoxia brake (growth x0.2 on half the days) exposes how
fast the growth model is: a January release of 100 g S1 smolts reaches 4.5 kg
in about 275 days, where real Norwegian grow-out takes 14-18 months. S2-03
(TGC growth) owns that; the tutorial should not be tuned around it.

### Engine-path production cycle (`FullCycleEconomicTests`)

PO04, 10,000 standard S1 smolts, events off, seeds 1/7/11/13, harvested at 5 kg.

| Metric | base | S1-01 | S1-02 |
|---|---|---|---|
| Days to 5 kg | 554-562 | 457-479 | 414-461 |
| Biological FCR (feed / biomass built incl. fish that died) | | 1.231-1.258 | **1.212-1.248** |
| Economic FCR (feed / net harvested gain) | 1.80-2.63 | 1.466-1.981 | 1.460-1.947 |
| Survival | 41-72% | 46-74% | 47-74% |

The ledger books smolts plus feed kg x 22.3 NOK exactly (the test asserts it).
Survival on any zone path is far below the NVI band (85%) because the seasonal
bloom deck (jellyfish, algae, hypoxia) kills 25-55% of a cycle; without a zone
the same cycle keeps 85% and an economic FCR of 1.34-1.37. That mortality is
S2-05/S4-03's to attribute and fix.

### SimProbe medians (seeds 1-20), base to S1-02

S1-01 alone moves none of these (the probes run without a zone).

| Scenario | Metric | base | S1-02 | Band |
|---|---|---|---|---|
| Baseline | Mean FCR | 1.22 | 1.22 | 1.10-1.30 |
| Baseline | Sea mortality %/cycle | 15.86 | 15.67 | 14-16 |
| Baseline | Feed cost NOK/kg produced | 27.15 | 27.23 | 22-30 |
| Baseline | Realized price NOK/kg | 84.07 | 88.00 | 74-90 |
| Baseline | Peak adult-female lice | 0.62 | 0.54 | ~0.5 |
| DiseaseStress | Disease mortality %/cycle | 4.36 | 4.37 | 0.5-10 |
| DiseaseStress | Total sea mortality %/cycle | 16.48 | 16.11 | 14-16 (+-0.5) |
| Storm | Escapes % of stock/site/year | 0.19 | 0.36 (p90 0.50) | <= 0.5 (p90 <= 1.0) |
| Storm | Escape events/site/year | 0.16 | 0.42 | <= 1 |
| Storm | Storm days/year | 116.3 | 145.0 | exposure |
| Storm | Min net integrity % | 84.1 | 82.9 | >= 40 |
| ThreeSite | EBIT NOK/kg | 14.80 | 20.11 | 8-25 |
| ThreeSite | Cost NOK/kg produced | 69.52 | 63.21 | |
| LiceControl | Treatments/cycle, resistance, peak lice | 1.50, 0.07%, 0.15 | unchanged | |

No band was loosened. The honest weather chain alone broke two of them, and the
S1-02 model fixes restore them: with net repair left at 0.2 the nets fall below
the 40% line within two years (storm escapes 173% of stock/site/year, DiseaseStress
total 55.4%); with repair re-fit but the old per-flip storm charging, the
DiseaseStress total is 16.89%; with storm episodes but winter storm cooling it is
16.60%.

## 1e. S1-03: honest economy (2026-10-14, branch `wave1/sim-2`)

Owner decision D16: achievements and tutorial steps pay no cash, each scenario
has its own bankruptcy floor with a warning ladder before it, and the
resource-rent tax is on by default. Calibration v1.0.5.

### Reward cash that left the income statement

Measured on `wave1/sim-1` (before), cumulative over the run:

| Run | Tutorial rewards | Achievement rewards | Largest other income line |
|---|---|---|---|
| Tutorial (`forste_sesong`, minimal player, 6 seeds, to completion) | 1.45M | 0.38-1.65M | events 0.2-0.68M |
| po3 career (`EconomyLedgerTests` script, 730 d, seeds 7/13/21) | 1.45M | 1.66-1.78M | events 1.78-2.67M |
| ressursrente harvest-and-restock (1095 d, seeds 7/13/21) | 1.45M | 7.28-10.28M | events 1.46-3.29M |

The tutorial ran (and paid) in every scenario, not only `forste_sesong`
(U2-02 restricts it). Achievements now award a medal (bronze, silver, gold,
platinum; mapped from the old amounts: up to 100k bronze, up to 500k silver,
up to 2M gold, above platinum); tutorial steps award nothing but the step.

| Measure (after S1-03) | Before | After |
|---|---|---|
| PlayerScriptProbe ending balance, seed 7 / 13 | +24.68M / +29.97M | +24.37M / +29.66M |
| Tutorial won (6 seeds, horizon 1500 d) | 6 of 6 | 6 of 6 |
| Tutorial finish day | 273-279 | 276-278 (seeds 9, 42); 608, 861, 1103, 1233 (seeds 7, 4, 13, 21) |
| Tutorial minimum cash | 0.93-1.35M | 0.55-0.93M |

The slower tutorials wait for the 6.5M NOK the minimal player wants before
building the second site (step 16): the family farm earns about 1.5-2.4M NOK a
year, and the 1.45M of step rewards used to cover most of the gap. The
minimal player never borrows; teaching the loan verb at that step is U2-02's.

### Bankruptcy floor per scenario

The bridge ended every game below -1M NOK and the scenario check used -2M,
with no warning. A harvest-only player (starting stock only, sell at 4.5 kg,
events on) measured on ten seeds per scenario, without reward cash:

| Scenario | Starting cash | Deepest trough (median) | Crosses -1M on day | First harvest day | Floor |
|---|---|---|---|---|---|
| algekatastrofe | 25M | -21.14M (-17.65M) | 184-199 | 266-284 | **-26.5M** |
| isa_utbrudd | 18M | -18.86M (-17.21M) | 175-184 | 272-292 | **-24.0M** |
| po3_red_sone | 12M | -11.66M (-10.76M) | 183-197 | 273-295 | **-15.0M** |
| krise_manager | -0.5M | -3.38M (-2.92M) | 77-130 | 273-298 | **-4.5M** |
| lofoten_eventyr | 2M | -1.36M (-0.97M) | 259-287 (5 of 10 seeds) | 271-302 | **-2.0M** |
| laksebaronene | 5M | -0.47M (0.00M) | never | 273-296 | default -1M |
| forste_sesong | 3M | +0.33M (+0.67M) | never | 271-284 | default -1M |
| barekraft_mester | 50M | +42.52M | never | 270-282 | default -1M |
| ressursrente | 60M | +7.38M | never | 249-262 | default -1M |

Rule: floor = 1.25 x the deepest trough, rounded out to the next 0.5M NOK;
scenarios whose trough stays above -1M keep the default. The floor is the
scenario's working-capital facility with its bank: real farmers finance
biomass through revolving credit whose borrowing base is the insured biomass
(e.g. [Ice Fish Farm's 2022 bank package](https://www.fishfarmermagazine.com/?p=145908)).
At po3's trough the farm holds about 500 t of fish (~41M NOK at the norm
price), so the 15M facility is about 37% of the collateral. Under the old -1M
rule the harvest-only player went bankrupt 70-110 days before its first sale
in po3, isa_utbrudd and algekatastrofe, and 150-200 days before it in
krise_manager.

Warning ladder (fractions of the floor, `SolvencyWarningFractions`): overdraft
below 0, bank warning below 50%, final warning below 80%, bankruptcy below the
floor. Each rung warns once on the way down and re-arms when cash is back at
zero. `krise_manager` passive play, days 1-30, 20 seeds: lowest cash -0.61M to
-0.77M (floor -4.5M); it crosses -1M on days 77-130 and, played passively,
goes bankrupt on days 446-675 (12 seeds). A player who delouses above 0.4 lice,
harvests at 4.5 kg and restocks stays solvent through day 730 on all 12 seeds
(trough -1.9M to -3.1M).

The PlayerScriptProbe applies the po3 starting state without activating the
scenario, so it runs on the default floor: the engine records a bankruptcy on
days 181-187 of every probe career (no effect on the headless run). With the
scenario active its trough (-13.35M to -14.92M over seeds 7/13/21/29/42; it
stocks 22,000 extra smolts and takes a 4M loan) stays above po3's -15M floor,
seed 7 by 78k NOK.

### Resource-rent tax on by default

| Career (1095 d, events on) | Rent base per closed year | Rent tax paid |
|---|---|---|
| Family farm (`forste_sesong`, 1 pen, harvest and restock 20,000) | 0.2-2.4M | 0 |
| ressursrente starting farm (3 sites, 22 pens), harvest and restock | -25.2M to +26.7M | 0 |
| barekraft_mester + 5 sites (6 x 780 t MAB, stocked to 90%, borrows 20M when cash < 5M), seeds 7/13/21/4/9 | 41.8-126.0M | 11.3-29.9M |

The 70M NOK deduction shields any farm below roughly 4,000 t a year, as it does
in Norway. Note: the ressursrente scenario's own farm never clears the
deduction, so the tax it is about only bites once the player expands.

SimProbe medians (seeds 1-20) are unchanged by S1-03 (Baseline FCR 1.22,
mortality 15.67%, price 88.0; DiseaseStress 16.11%; Storm 0.36% / 0.42 events /
82.9% integrity; ThreeSite EBIT 20.11 NOK/kg, cost 63.21): the probe books no
reward lines and its sites stay below the deduction.

## 1f. S2-02: production areas, sea temperature, one calendar (2026-10-14, branch `wave2/sim-b-1`)

Roadmap S2-02 (with D18). Calibration v1.0.6 (new section `seaTemperature`).
Base column = `4e761a3` (wave 1 integration); S2-02 = the branch head. Measured
with a temporary probe that calls the harnesses the tests use (`SimProbe`,
`PlayerScriptProbe`, the tutorial player of `ScenarioCampaignTests`, the
`FullCycleEconomicTests` cycle) on both commits.

### Sea temperature per production area (3 m, °C)

Derived by `tools/derive_po_sea_temperature.py` and embedded in
`Environment/ProductionAreas.cs` (a pytest keeps the two equal). Level:
Havforskningsinstituttet's normal winter and summer temperature per area
(NorKyst v3, 2012-2024, area mean inside the outermost islands; Albretsen et al.,
*Fysisk oseanografiske forhold i produksjonsområdene for akvakultur*, Rapport fra
havforskningen 2025-69, published 2025-12-02, section 4; range midpoints). Shape:
the monthly cycle of one or two coastal stations per area (10-year means,
satellite plus in situ, seatemperature.info, read 2026-10-14: Kristiansand,
Stavanger, Haugesund+Bergen, Florø, Ålesund, Kristiansund, Rørvik,
Sandnessjøen+Bodø, Svolvær, Harstad, Tromsø, Hammerfest, Vadsø+Kirkenes),
rescaled so its coldest month is HI's winter normal and its warmest HI's summer
normal. Depth: 3 m, where farms measure (luseforskriften § 6). Monthly means sit
at mid-month; days are interpolated.

| PO | Jan | Feb | Mar | Apr | May | Jun | Jul | Aug | Sep | Oct | Nov | Dec | Mean |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 Svenskegrensen–Jæren | 4.93 | 3.60 | 3.50 | 5.65 | 9.64 | 13.93 | 16.49 | 17.00 | 15.26 | 12.09 | 9.33 | 6.67 | 9.84 |
| 2 Ryfylke | 5.83 | 4.69 | 4.50 | 6.02 | 8.67 | 11.80 | 14.27 | 15.50 | 14.36 | 11.90 | 9.72 | 7.34 | 9.55 |
| 3 Karmøy–Sotra | 6.78 | 5.73 | 5.50 | 6.73 | 9.15 | 11.98 | 14.50 | 15.50 | 14.45 | 12.26 | 10.16 | 8.19 | 10.08 |
| 4 Nordhordland–Stadt | 6.76 | 5.60 | 5.50 | 6.76 | 9.38 | 12.49 | 15.31 | 15.50 | 13.95 | 11.62 | 9.58 | 7.93 | 10.03 |
| 5 Stadt–Hustadvika | 6.75 | 5.79 | 5.50 | 6.56 | 8.86 | 11.64 | 14.04 | 15.00 | 13.85 | 11.64 | 9.63 | 7.99 | 9.77 |
| 6 Nordmøre–Sør-Trøndelag | 7.05 | 6.18 | 6.00 | 6.79 | 9.07 | 11.70 | 13.71 | 14.50 | 13.27 | 11.35 | 9.51 | 7.93 | 9.76 |
| 7 Nord-Trøndelag m/Bindal | 6.40 | 5.66 | 5.50 | 6.32 | 8.44 | 11.13 | 13.09 | 13.50 | 12.11 | 10.23 | 8.52 | 7.21 | 9.01 |
| 8 Helgeland–Bodø | 5.85 | 5.05 | 5.00 | 5.52 | 7.47 | 10.41 | 12.98 | 13.50 | 12.08 | 10.32 | 8.56 | 7.04 | 8.65 |
| 9 Vestfjorden–Vesterålen | 6.17 | 5.29 | 5.00 | 5.36 | 7.04 | 9.73 | 11.99 | 12.50 | 11.19 | 9.73 | 8.42 | 7.26 | 8.31 |
| 10 Andøya–Senja | 6.15 | 4.76 | 4.50 | 5.28 | 6.93 | 9.88 | 12.22 | 13.00 | 11.18 | 9.62 | 8.40 | 7.19 | 8.26 |
| 11 Kvaløya–Loppa | 5.36 | 4.36 | 4.00 | 4.36 | 5.90 | 8.43 | 10.69 | 11.50 | 10.33 | 8.79 | 7.61 | 6.53 | 7.32 |
| 12 Vest-Finnmark | 5.50 | 4.46 | 4.00 | 4.00 | 5.04 | 8.04 | 10.58 | 11.50 | 10.58 | 9.19 | 8.04 | 6.54 | 7.29 |
| 13 Øst-Finnmark | 5.09 | 4.26 | 4.00 | 4.11 | 5.65 | 8.12 | 10.63 | 11.00 | 9.50 | 8.08 | 6.99 | 5.87 | 6.94 |
| National reference (no area) | 4.5 | 3.8 | 4.2 | 5.5 | 8.0 | 10.5 | 12.5 | 13.5 | 12.0 | 9.5 | 7.0 | 5.5 | 8.04 |

The national reference is the single curve every zone used before S2-02 (no
source); only a game with no scenario (tests, tools) reads it now. Every
scenario site names its area (`production_area` in `scenarios.json`):
forste_sesong PO3 (Hardanger), lofoten_eventyr PO9, laksebaronene PO6,
krise_manager PO12, barekraft_mester PO4, po3_red_sone PO3, isa_utbrudd PO6,
algekatastrofe PO9, ressursrente PO4.

### The shared anomaly

All areas move with one anomaly (`WeatherSystem`): a slow AR(1) process (SD
1.0 °C, e-folding 90 days; Sandvik et al. 2021 give SD 1.2 °C for PO3's yearly
3 m extremes, HI's 2017 note shows months-long same-signed monthly anomalies), a
storyteller anomaly that fades with an e-folding of 21 days (the July 2025 marine
heatwave lasted about three weeks), the day's fluctuation (SD 0.3 °C, x2 on Storm
and x3 on Severe days; a game estimate) and the April-September storm cooling
(S1-02). It reuses the two uniforms the old daily noise drew, so the weather chain
sees the same draws (`TheWeatherChain_DrawsFourNumbersADay`: storm days/year
145.0 before and after).

| Anomaly, 40 seeds x 3 years (first 180 days skipped) | Measured | Target |
|---|---|---|
| Mean | -0.011 °C | 0 |
| SD | 1.060 °C | 1.0 |
| Autocorrelation at 30 / 90 days | 0.741 / 0.448 | 0.717 / 0.368 |
| Largest excursion | 4.60 °C | |

Realized zone water, 10 seeds x 3 years:

| PO | Climatology mean | Realized mean | p5 | p95 | Min / max | SD of annual means |
|---|---|---|---|---|---|---|
| 1 | 9.88 | 9.73 | 3.16 | 16.80 | -0.15 / 18.96 | 0.65 |
| 3 | 10.10 | 9.96 | 4.98 | 15.31 | 1.74 / 17.38 | 0.65 |
| 4 | 10.06 | 9.91 | 4.94 | 15.46 | 1.75 / 17.51 | 0.65 |
| 6 | 9.78 | 9.63 | 5.30 | 14.32 | 2.02 / 16.42 | 0.65 |
| 8 | 8.67 | 8.52 | 4.16 | 13.35 | 0.89 / 15.46 | 0.65 |
| 9 | 8.33 | 8.18 | 4.16 | 12.39 | 0.81 / 14.46 | 0.65 |
| 12 | 7.31 | 7.16 | 2.94 | 11.39 | -0.38 / 13.40 | 0.65 |
| 13 | 6.96 | 6.81 | 3.02 | 10.90 | -0.31 / 12.98 | 0.65 |

The realized means sit 0.15 °C under the climatology: the summer storm cooling
plus these seeds' slightly negative anomaly. The only floor is the freezing point
of seawater (-1.9 °C); the old 1.5-19 °C clamp is gone.

### What the area's water drives

Growth, appetite and feeding take the population's water (`HoldingWaterOf`:
sea pen = its zone, offshore pen = its area's sea, RAS tank = the facility's
controlled water); so do the site lice models, the zone oxygen solubility, the
treatment temperature window and the temperature-stress deaths. The disease tick
receives the water temperature, but the generic SIR has no temperature term yet
(S4-02 brings pathogen-specific responses: PD warm, winter ulcer cold). The lice
limit is 0.2 adult females in ISO weeks 16-21 for PO1-PO7 and 21-26 for PO8-PO13
(luseforskriften § 8), 0.5 otherwise.

### Growth in real water: the S2-03 input (current growth law, untouched)

Days for 100 g standard S1 smolts to reach 5 kg, 10 seeds, events off, each site
explicitly in its area:

| PO | 1 Jan release: median [min-max] | Mid-April release: median [min-max] |
|---|---|---|
| 1 | 518 [413-645] | 444 [348-545] |
| 2 | 338 [307-428] | 307 [242-437] |
| 3 | 316 [291-350] | 263 [241-416] |
| 4 | 319 [292-352] | 278 [241-424] |
| 5 | 310 [283-337] | 256 [227-396] |
| 6 | 293 [267-320] | 245 [218-304] |
| 7 | 296 [272-314] | 243 [211-313] |
| 8 | 315 [287-341] | 267 [216-410] |
| 9 | 306 [282-365] | 258 [213-388] |
| 10 | 318 [289-385] | 298 [217-407] |
| 11 | 452 [314-573] | 445 [236-482] |
| 12 | 483 [317-577] | 451 [239-489] |
| 13 | 565 [341-610] | 477 [280-519] |

Real Norwegian grow-out takes 14-18 months (roadmap S2-03). The Gaussian growth
law (optimum 11 °C, width 3 °C, asymptote 5.5 kg) makes real southern water far
too fast (PO4 from 1 January: 292-352 days, below the 360-600 band of
`FullProductionCycle_OnTheEnginePath_FeedPerKgGainInBand`), PO1's 15-17 °C
summers slow, the far north 200 days slower than PO4 (the pre-registered
`Po4ReachesFiveKilos_TwoToFourMonthsBeforePo13` wants 60-120), and lets PO13 keep
pace with PO4 through a summer. Re-fitting the growth rate alone cannot fix it:
the outcome is bimodal (harvest before or after the second winter); at alpha_max
16-18 PO4 ranged 311-541 days over 13 seeds. That is S2-03's TGC work (RUN_LOG
§8). Until then the D2 tests whose zones are only *named* PO04/PO12/PO13 keep
running on the national reference: S2-02 places sites in areas by data, never by
parsing an id.

### Before and after, measured on both commits

| Measure | Base `4e761a3` | S2-02 | Band |
|---|---|---|---|
| SimProbe Baseline FCR (median) | 1.22 | 1.23 | 1.10-1.30 |
| Baseline sea mortality %/cycle (min / median / p90) | 14.25 / 15.67 / 20.18 | 12.05 / 15.48 / 19.00 | 14-16 median |
| Baseline feed cost NOK/kg produced | 27.23 | 27.40 | 22-30 |
| Baseline peak adult-female lice (median) | 0.54 | 0.52 | ~0.5 |
| DiseaseStress disease / total mortality % | 4.37 / 16.11 | 4.38 / 15.84 | 0.5-10 / 14-16 (+-0.5) |
| Storm escapes % / events / storm days / min integrity | 0.36 / 0.42 / 145.0 / 82.9 | 0.36 / 0.41 / 145.0 / 83.2 | <=0.5 / <=1 / exposure / >=40 |
| ThreeSite EBIT NOK/kg / cost NOK/kg | 20.11 / 63.21 | 19.63 / 63.25 | 8-25 |
| LiceControl treatments, resistance, peak lice | 1.50, 0.07%, 0.15 | unchanged | |
| Career po3 seed 7 (now PO3 water): days, kg, FCR, mortality, balance | 311, 520,293, 1.178, 18.2%, +24.37M | 315, 460,912, 1.179, 23.9%, +21.24M | FCR 1.0-1.6, mort <=65%, > -1M |
| Career po3 seed 13 | 285, 531,843, 1.163, 12.5%, +29.66M | 331, 493,072, 1.176, 20.6%, +22.94M | > -15M (D4) |
| Tutorial forste_sesong (PO3): won, finish day, first harvest | 6/6, 276-1233, 271-277 | 6/6, 285-1058, 258-325 | won |
| FullCycle zone named PO04 (national reference), days to 5 kg, seeds 1/7/11/13 | 461 / 414 / 422 / 440 | 503 / 368 / 453 / 483 | 360-600 |

The po3 careers now run in PO3 water: warmer summers (15.5 °C normal, up to 17 °C
with the anomaly) bring temperature-stress deaths, lower oxygen saturation and the
algae/hypoxia bloom rolls (> 10 °C / > 14 °C) more often, so sea mortality rises
from 12.5-18.2% to 20.6-23.9% (Vestland reports 15-20%; S2-05/S4-03 own the cause
ledger). The FullCycle spread widens because the anomaly persists for months; the
old daily white noise averaged out within weeks.

### One calendar

`Core/SimCalendar.cs`: day 0 is Friday 1 January 2027, years have 365 days,
months their real lengths, weeks are ISO-8601 (sim year 1 is the real 2027 day
for day). The weather (storm season October-March), the market's seasonal months,
the lice weeks, the provisional tax, monthly OPEX and the corporate-finance
quarters read it. Books close on the period's last day: quarters on 31 March,
30 June, 30 September and 31 December (they closed every 90 days, a 360-day
financial year), months on their last day. Wages and night-shift premiums are
monthly x 12 / 365 per day (/30 paid 12.17 months a year, 1.4% too much); loan
terms run whole calendar months; a crisis lasts calendar months.
## 1f. S2-05: mortality by cause (2026-10-14, branch `wave2/sim-c-1`)

Roadmap S2-05 / D4. Every fish that leaves a cohort is booked on the
mortality ledger (`Biology/MortalityLedger.cs`): a death under one cause, or a
live removal (escape, harvest, forced harvest, transfer). Escapes are not
deaths. Categories are the six level-1 classes of the industry cause-of-death
database the Veterinary Institute reports, plus the roadmap's predator row.

| Category | NVI | Causes in the game | NVI 2024 share of dead salmon |
|---|---|---|---|
| Infectious | A | disease (SIR hazard), disease cull (ISA order) | 32.9 % |
| Environment | B | temperature, low oxygen, harmful algae, jellyfish, storm | 8.8 % (75 % of it jellyfish) |
| Injury and treatment | C | delousing, handling (transfer, vaccination) | 26.6 % |
| Physiological | D | (none yet) | 5.7 % |
| Predators | — | (none yet: the seal event kills no fish) | not a level-1 class |
| Other | E | (none yet) | 4.8 % |
| Unexplained | F | baseline anchor (the calibrated whole-cycle hazard; post-smolt batch losses) | 21.2 % |

Source: Fiskehelserapporten 2024 (Veterinærinstituttet rapportserie 1a/2025),
table 2.4.2 p. 35, from "Fiskehelsedatabasen" (Sjømat Norge / AquaCloud, 387
salmon sites, about half the standing biomass);
https://www.vetinst.no/rapporter-og-publikasjoner/rapporter/2025/fiskehelserapporten-2024.
The shares are registrations by participating companies, not national prevalence.

### Seed 13 (input to S4-03)

`PlayerScriptProbe.RunCareer(13)` (po3_red_sone start plus 22,000 smolts,
events on, save/load at day 210), printed by
`MortalityLedgerEngineTests.CareerProbe_AccountsForEveryFish_AndPrintsTheBreakdownInNviCategories`:

```
po3_red_sone seed 13: 134 000 fish at the start, 285 days, sea loss 12,48 % (deaths + escapes + forced harvest)
  category / cause                    NVI         fish    share             kg        value NOK
  infectious                            A        3,378   20.3 %          5,210          382,191
    disease                                      3,378   20.3 %          5,210          382,191
  environment                           B          859    5.2 %            915           67,594
    temperature                                    107    0.6 %             18            1,682
    storm                                          752    4.5 %            897           65,912
  injury_treatment                      C        2,290   13.7 %            247           19,729
    delousing                                    2,290   13.7 %            247           19,729
  physiological                         D            0    0.0 %              0                0
  predators                                          0    0.0 %              0                0
  other                                 E            0    0.0 %              0                0
  unexplained                           F       10,141   60.8 %         12,020          888,560
    baseline_anchor                             10,141   60.8 %         12,020          888,560
  all deaths                                    16,668  100.0 %         18,392        1,358,074
  left alive: escape                                49                      23            1,870
  left alive: harvest                          117,283                 531,843       42,231,724
```

134,000 = 16,668 dead + 49 escaped + 117,283 harvested, exactly. Value is
live kg at the day's spot price. The 2,290 delousing deaths are the probe's
day-3 hydrolicer treatment of freshly stocked 0.1 kg smolts.

### D4: what the 61 % disaster was

The ledger cherry-picked onto the pre-wave-1 base (`4a5290d`) and the same
probe, 20 seeds:

| Base | Sea loss median (min-max) | Worst seed | Low-oxygen deaths |
|---|---|---|---|
| pre-wave-1 `4a5290d` | 28.5 % (19.1-62.7) | 16: 62.7 %, 63,814 of 83,949 deaths low oxygen | seeds 1, 13, 16: 37,335 / 21,374 / 63,814 (plankton crash at zone O2 < 3 mg/L) |
| wave 1 + S2-05 | 17.9 % (12.3-29.2) | 2: 29.2 % (algae 13,013, jellyfish 6,659, hypoxia 5,964) | 0-5,964, from the bloom deck's hypoxia events only |

Seed 13 on the old base: 34.8 % lost, 21,374 of 46,545 deaths low oxygen,
ending +7.71M. The disaster careers were the plankton-crash event firing on
the O2 square wave (0 mg/L on 293 of 600 days) that S1-01 removed; the 61 %
and -10.6M of the §8 dispute were measured on an earlier review commit. On the
current base seeds 7 and 13 lose 18.2 % and 12.5 % and end +24.37M and
+29.66M, so D4's band (at most 35 % cycle mortality) and the -1M bound hold;
`D4_Po3Career_LosesAtMost35Percent_AndEndsAboveTheDefaultBankruptcyFloor`
guards both.

### Twenty seeds (wave 1 + S2-05)

Probe careers, seeds 1-20 (285-311 days each; the fish reach 4.5 kg fast, S2-03):

| Measure | Median | Min | Max |
|---|---|---|---|
| Sea loss (deaths + escapes), % of stock | 17.91 | 12.33 | 29.20 |
| Infectious, % of stock | 2.35 | 1.93 | 2.69 |
| Environment, % of stock | 6.52 | 0.49 | 19.64 |
| Injury and treatment, % of stock | 1.71 | 0 | 1.71 |
| Unexplained (anchor), % of stock | 7.69 | 7.43 | 8.06 |
| Infectious, share of deaths | 12.7 % | 7.3 % | 20.5 % |
| Environment, share of deaths | 35.7 % | 4.0 % | 67.3 % |
| Injury and treatment, share of deaths | 8.9 % | 0 % | 13.9 % |
| Unexplained, share of deaths | 44.1 % | 25.5 % | 61.7 % |

Pooled causes (489,619 deaths): anchor 42.3 %, harmful algae 19.9 %, disease
12.7 %, jellyfish 11.7 %, delousing 7.5 %, low oxygen 2.8 %, storm 2.7 %,
temperature 0.4 %.

Inputs for S4-03 (not tuned here; S2-05 attributes, S4-03 changes hazards):
- The mix is far from NVI's: unexplained 44 % (NVI F 21 %), environment 36 %
  (NVI B 9 %), infectious 13 % (NVI A 33 %), injury and treatment 9 % (NVI C 27 %).
- The bloom deck is the environment surplus: one harmful-algae event kills
  5-10 % of a career's stock (seed 2: 13,013 of 134,000), and blooms hit
  18 of 20 careers. Real catastrophic blooms are rare events (Calado et al.
  2021 cite one northern-Norway algal bloom that killed several million
  salmon), and NVI books all environmental causes at 8.8 % of deaths.
- The anchor alone kills 7.4-8.1 % of the stock in under ten months; it is a
  top-down hazard with no mechanism and should give way to hazards per cause.
- No predator or physiological deaths exist; the seal event says seals kill
  fish but removes none.
- Temperature stress is small, as calibrated: 0.4 % of deaths.

### Temperature stress constant

`MortalityCalibration.TemperatureStressDailyFractionPerDegreeC` = 0.00005
(0.005 % of the fish per °C outside 4-16 °C per day), the value the engine
always applied (0.01 x 0.005); the method's documentation said 0.5 %, a
hundred times more. Held against Calado et al. 2021 (Animals 11(6):1800,
https://pmc.ncbi.nlm.nih.gov/articles/PMC8234874/): weeks above 18 °C cost
appetite and growth, not stock; at this rate a Tasmania-2015/16-like summer
(117 days above 18 °C) kills about 1-3 % directly and a 2 °C northern winter of
90 days about 0.9 %. `TemperatureStressDocumentationTests` reads the compiled
XML documentation of the engine method and the registry entry and holds both to
the applied value.

### The cohort-environment seam

Hazards read `CohortEnvironment` (holding kind and id, water temperature, O2,
CO2, wave height), resolved per population by
`SimulationEngine.ResolveCohortEnvironment`. Measured effects of reading the
holding's own water instead of the zone's: a RAS tank in a zone whose fjord
oxygen crashes to 1.5 mg/L loses nothing (it lost 25 % before); a RAS tank whose
chiller fails (0 °C water) loses 0.02 % a day to temperature while the sea pen
at 10 °C loses none.

## 1g. S2-03: TGC growth, appetite, ration policy (2026-10-14, branch `wave2/sim-b-2`)

Roadmap S2-03. Calibration v1.0.7 (new section `growth`), save format v5.
Measured with `GrowthCycleProbe` (engine path: a zone in its production area by
data, blooms and the shared anomaly on, storyteller events off), the S2-03
acceptance tests (`TgcGrowthAcceptanceTests`, `TgcGrowthEngineTests`,
`TgcGrowthLawTests`) and a temporary probe calling the same harnesses.

### The growth law (`Biology/TgcGrowthLaw.cs`)

Each day a cohort's satiation gain adds `TGC/1000 × E(T) × size × light × multipliers`
to the cube root of its weight in grams:

| Term | Value | Source |
|---|---|---|
| TGC | 3.0 | Reid et al. 2013a average 3.0 (remedial 2.5, elite 3.5) via Dempsey et al. 2023, Front. Mar. Sci. 10:1094247; Karlsen et al. 2018, Sci. Rep. 8:9510: 2.80 open net pen, 3.04 semi-closed (first 4 months); Arctic Fish 3.38 (Benchmark Genetics) |
| E(T) up to the optimum | T (degree-days) | the TGC definition; valid 4-14 °C (Alanärä et al. 2001); growth at 4/4.5/5 °C = 0.31/0.35/0.38 of the optimum |
| Optimum | 13 °C | Kleiven et al. 2026, Front. Aquac.: "around 13 to 14 °C"; Calado et al. 2021, Animals 11:1800: 13-16 °C |
| Above the optimum | T × (22 − T)/9, 0 at 22 °C | Calado et al. 2021: ITmax ~22 °C; 2 kg fish at 19 °C ate and grew ≥ 50% less than at 14 °C (law: 51%); Thyholdt 2014: warm periods slow growth in the south |
| Size | 1 to 3 kg, then linear in cube-root weight to 0 at 20 kg | TGC valid to 3 kg (Alanärä et al. 2001); 20 kg game estimate (0.79 at 5 kg) |
| Light | 1 + 0.2 × (day length − 12)/12; land tanks 24 h | Aunsmo et al. 2014 (TGC tied to day length on 827 Norwegian groups); slope a game estimate (sea-cage light trials, Endal et al. 2000); CtrlAQUA 2023 (24 h light in RAS) |
| Multipliers | genome, smolt quality, containment, research, density stress (Oppedal et al. 2011) | unchanged inputs |

Appetite (satiation intake) = satiation gain × the biological FCR at satiation
(temperature, size factor (W/2 kg)^0.07 at sea, feed, density, water, research):
a Cho (1992) feeding standard, so appetite follows temperature and size. The
holding's ration policy (`FishPopulation.Ration`, fraction of appetite) times the
appetite left by water and gills is the ration eaten r; growth = satiation gain ×
g(r) with maintenance at 10% and the best conversion near 70% (Brett 1979). The
law draws no random numbers; the daily tick, the forecast twin and the scenario
starting stock all run it.

### Acceptance over 10 seeds

| Criterion | Band | Measured |
|---|---|---|
| PO4 mid-April S1 100 g: harvest window | 4.5-5.5 kg within months 14-18 | 4.5 kg on days 400-480, 5.5 kg on days 435-516 on every seed; 5 kg median day 448 (14.7 months), 418-497 |
| Economic FCR (feed / net harvested gain), commercial pen, PO4 | 1.20-1.35 | 1.230-1.246 (mortality 13.3-15.1%); FCR on growth 1.162-1.170 |
| Relative growth at 4-5 °C | ≥ 0.3 of the optimum | 0.31 / 0.35 / 0.38 at 4 / 4.5 / 5 °C |
| Fish grown on | pass 6 kg | 6.5 kg on days 474-536 (PO4 April release) |
| Per-cohort weight CV | reported | 0.211-0.216 at 5 kg (watch mode); bridge `weight_cv` |
| Pre-registered D2: PO4 2-4 months before PO13 | 60-120 days, seeds 1-5 | normal years: 92-115 on every seed; weather years: 157 / 117 / 68 / 168 / 120 (fails seeds 1 and 4, RUN_LOG §8 dispute) |

Realized cycle TGC on the zone path (PO4, gills and blooms included): 2.61-2.97.

### Days for 100 g S1 smolts to reach 5 kg, per area (10 seeds, engine path)

| PO | 1 Jan release: median [min-max] | Mid-April release: median [min-max] | S2-02 law, mid-April (§1f) |
|---|---|---|---|
| 1 | 547 [499-581] | 477 [445-517] | 444 [348-545] |
| 2 | 526 [462-551] | 462 [418-498] | 307 [242-437] |
| 3 | 498 [432-545] | 448 [417-483] | 263 [241-416] |
| 4 | 500 [453-527] | 448 [418-497] | 278 [241-424] |
| 5 | 501 [432-529] | 451 [404-483] | 256 [227-396] |
| 6 | 488 [427-517] | 446 [405-485] | 245 [218-304] |
| 7 | 520 [464-551] | 466 [419-491] | 243 [211-313] |
| 8 | 537 [486-561] | 480 [430-508] | 267 [216-410] |
| 9 | 551 [490-580] | 494 [441-526] | 258 [213-388] |
| 10 | 554 [501-578] | 493 [439-526] | 298 [217-407] |
| 11 | 602 [535-634] | 540 [474-626] | 445 [236-482] |
| 12 | 605 [544-640] | 548 [478-605] | 451 [239-489] |
| 13 | 616 [549-659] | 568 [486-665] | 477 [280-519] |

A spring release reaches 5 kg in 14.7-15.7 months in PO1-PO7, 15.8-16.2 in
PO8-PO10 and 17.8-18.7 in PO11-PO13: real Norwegian grow-out is 14-18 months,
longer in Finnmark. PO1's 16-17 °C summers are now merely slower than PO3-PO6
(the Gaussian stalled them).

### PO4 against PO13: why the pre-registered test fails on two seeds

The same mid-April release in weather years 1-10: gaps 157, 117, 68, 168, 120,
156, 106, 95, 122, 63 days (median 118.5). Seeds 1 and 4 have springs 2-3 °C below
normal for 3-6 months (the shared anomaly, SD 1.0 °C, e-folding 90 days, S2-02):
PO13's fish then miss the second autumn and spend a second winter at sea. With
the anomaly off the gap is 92-115 on every seed. A 300-year Monte Carlo of the
climatologies gives P(gap in 60-120) of 0.40-0.78 per weather year for every
plausible TGC (2.8-3.2), optimum (12.5-13 °C) and light slope (0-0.3), so no growth
law makes all five seeds pass; the criterion is the owner's (RUN_LOG §8).

### Ration policy (PO4, from day 165, 120 days, 5 seeds)

| Fraction of appetite | Gain per fish | FCR on growth | Feed cost per day (10,000 fish) | Weight CV |
|---|---|---|---|---|
| 100% | 1.113 kg | 1.103 | 2 141 NOK | 0.207 |
| 85% | 0.891 kg | 1.077 | 1 673 NOK | 0.215 |
| 70% | 0.678 kg | 1.061 | 1 255 NOK | 0.222 |
| 50% | 0.417 kg | 1.068 | 777 NOK | 0.226 |

U2-04's acceptance 1 holds on the engine path (70% vs full: less growth, lower
biological FCR, lower cost per day; `TgcGrowthEngineTests`).

### The re-measure (roadmap R2: scenario data, not mechanics)

| Change | Why | Measured |
|---|---|---|
| Scenario sites `days_at_sea`: S0 mid-September (110 d) in PO1-PO7, S1 mid-June (200 d) in PO8-PO13 | a farm on 1 January holds its last release window's fish; 1 January smolts could not be sold before day ~460 | day-0 weights 0.48-1.33 kg (CV 0.226-0.296); passive first 4.5 kg day: algekatastrofe 210, barekraft 294, forste 300, isa 287, krise 252, laksebaronene 288, lofoten 217, po3 286, ressursrente 234 |
| `forste_sesong` 22,000 fish (was 20,000) | step 16's 6M second site from the first harvest | minimal player: 5 of 6 seeds win (days 281-737), seed 13 solvent at step 16 (20,000: 4 of 6) |
| Hatchery batches: standard strain genome | one random individual's genome (growth SD 15%) on a whole cohort | SimProbe cycle spread 464-921 days → one climate's |
| SimProbe in PO4 water, first release mid-April | the national reference with a 1 January release is a 19-20 month cycle | see below |

| Measure | S2-02 (`wave2/sim-b-1`) | S2-03 | Band |
|---|---|---|---|
| SimProbe Baseline FCR (median) | 1.23 | 1.17 | 1.10-1.30 |
| Baseline sea mortality %/cycle (min / median / p90) | 12.05 / 15.48 / 19.00 | 14.05 / 14.54 / 14.74 | 14-16 median |
| Baseline peak adult-female lice (min / median / p90) | 0.52 (median) | 0.52 / 0.61 / 0.75 | 0.30-0.65 (+0.05), p90 ≤ 1.0 |
| Baseline feed cost NOK/kg produced | 27.40 | 26.04 | 22-30 |
| DiseaseStress disease / total mortality % | 4.38 / 15.84 | 4.39 / 14.76 | 0.5-10 / 14-16 (±0.5) |
| ThreeSite EBIT NOK/kg / cost NOK/kg | 19.63 / 63.25 | 21.74 / 62.67 | 8-25 (±2) |
| Storm escapes % / events / storm days / min integrity | 0.36 / 0.41 / 145.0 / 83.2 | 0.31 / 0.42 / 146.3 / 83.4 | ≤ 0.5 / ≤ 1 / exposure / ≥ 40 |
| LiceControl treatments, resistance, peak | 1.50, 0.07%, 0.15 | unchanged | |
| Career po3 seed 7: days, kg, FCR, mortality, balance | 315, 460,912, 1.179, 23.9%, +21.24M | 348, 507,436, 1.182, 20.1%, +23.74M | FCR 1.0-1.6, mort ≤ 65%, > -1M |
| Career po3 seed 13 | 331, 493,072, 1.176, 20.6%, +22.94M | 345, 495,497, 1.183, 20.6%, +27.84M | > -15M (D4), 70-96 NOK/kg (88.00) |
| FullCycle PO4 by data (was the national reference): days, bio FCR, eFCR, survival | 503 / 368 / 453 / 483 d | 490-522 d, 1.177-1.187, 1.326-1.509, 56-69% | 360-600 d, 1.0-1.6 |

The zone path's economic FCR (1.33-1.51) is the bloom deck's 31-44% cycle
mortality, not the feeding (RUN_LOG §8, S2-05/S4-03).

### The seam for farm types

`GrowthConditions` (water temperature, ration, appetite modifier, growth and
density multipliers, spreading, day length) is everything the law reads;
`SimulationEngine.GrowAndFeedOneDay(population, HoldingHusbandry)` fills it from
`HoldingWaterOf(population)` (S2-02) and `DayLengthHoursOf(water)`. A land tank
(RAS, flow-through) supplies its controlled temperature and 24 h light, an
offshore hull its area's sea and latitude; `FishPopulation.Ration` is the
holding's feeding plan. `TgcGrowthEngineTests.ALandTankAtTwelveDegrees_GrowsOnTheSameLaw_InItsOwnWaterAndLight`
proves a 12 °C tank grows exactly as the law predicts from its own water.

## 1h. S2-04: harvest pipeline (2026-10-14, branch `wave2/sim-b-3`)

Roadmap S2-04, review F270. Calibration v1.0.8 (new section `harvest`), save
format v6. Before S2-04 a harvest sold the LIVE kilos at the spot price, which
is quoted for superior gutted (HOG) fish of 4-5 kg, and cost nothing: revenue
per live kilo was about 27% too high. Measured with the S2-04 tests
(`HarvestPricingTests`, `HarvestPipelineTests`, `HarvestAcceptanceTests`), the
po3 career (`PlayerScriptProbe`), the new player-path farm cycle
(`PlayerScriptFarmCycle`) and a temporary probe calling the same harnesses.

### What a harvest is now

- **Order** (`OrderHarvest(holding, pens, fraction)`): every pen with fish, or the
  chosen pens; a share of each pen's fish (partial harvest). The wellboat slot is
  5 days away (or the end of the fast if longer); the player's slaughterhouse
  takes 150 t HOG a day (a land farm's own hall: its own pool), shared by every
  order of the pool, oldest slot first. Pens go in turn, each fasting just ahead
  of its turn behind the orders queued ahead (30 degree-days of its own water, at least 2 days), so a site that
  takes two weeks to slaughter is not starved for two weeks. Each batch sells at
  that day's price. `CancelHarvestOrder` feeds the fish again.
- **Express** (`HarvestSite`, `HarvestNow`, the bridge's `harvest_pen`): today, on
  a spot wellboat to the slaughterhouse's waiting pen, where the fish fast; the
  same pricing plus a 1.0 NOK/kg HOG surcharge (none in a land farm's own hall). A **hired harvest wellboat**
  (`RequestWellboat("harvest")`) starts the fast when booked and its flat hire is
  the transport. A **forced harvest** (Mattilsynet) sells through the pipeline at
  70% of the price with the express cost.
- **Sale**: live kg × 0.84 = HOG kg; each cohort's HOG kilos split into size
  classes (lognormal individual weights from the cohort CV, S2-03); grade shares
  by cause; gross = Σ HOG kg × size-class factor × grade factor × price (spot,
  or a contract's locked price for the kilos it takes, oldest first) × the
  processing plant's VAP premium; the harvest cost is cost of goods sold
  (category `harvest`). Every batch files a `HarvestReport` (fish, live and HOG
  kg, size classes, grades, price, gross, cost, net) that equals its ledger entries.

### Sources

| Number | Value | Source |
|---|---|---|
| Live → HOG yield | 0.84 | Mowi Salmon Farming Industry Handbook 2025, p. 114 (live 100%, blood/starving 6%, offal 10%, HOG ~84%); 2018 edition 84%; capital model p. 75 "0.84" |
| Harvest + transport | 5.0 NOK/kg HOG (wellboat 1.55, slaughter/packing 3.45) | Fiskeridir profitability survey 2023: "slakting og transport" 4.45 NOK/kg round weight (Veterinærinstituttet, Fiskehelserapporten 2024, table 3.2.1) × 1.125 gutting loss; Mowi Handbook 2025 p. 75: EUR 0.40/kg GWT; split by Marine Harvest Norway 2017 (Handbook 2018 p. 48: well boat 0.15, primary processing 0.33 EUR/kg) |
| Size ladder (HOG classes) | <1: 0.60, 1-2: 0.72, 2-3: 0.86, 3-4: 0.93, 4-5: 1.00, 5-6: 1.04, 6-7: 1.08, 7+: 1.10 | Kontali relative prices, Norway weekly 2015-2024, Mowi Handbook 2025 §6.7 p. 49 (2-3 kg ~86% of 4-5 kg, range 70-97%; 6-7 kg ~108%, 95-150% with summer peaks); 3-4/5-6 interpolated, <2 and 7+ extrapolated (game estimates) |
| Grades | superior / ordinary 0.90 / production 0.70 | Mowi Handbook 2025 p. 116: "reduced price of downgraded fish (~30%)"; ordinary 0.90 game estimate |
| Grade shares | base production 5%, ordinary 1.5%, winter wounds up to +30% | Fiskehelserapporten 2024 §5.8: Norway 2024 80.6% superior, 1.5% ordinary, 17.1% production by weight; ~65% superior in H1, ~90% in H2; wounds the main cause, then maturation, defects, disease |
| Fasting | 30 degree-days, ≥ 2 days | kvalitetsforskriften § 16 (no feed residue in gut at harvest), akvakulturdriftsforskriften § 27 (period without feed as short as possible); duration a game estimate |
| Slaughter capacity | 150 t HOG/day | game estimate; Norway 2024: 1,408,650 t HOG at ~60 slaughterhouses + ~40 bleeding vessels (Fiskehelserapporten 2024 §5.8) |
| Wellboat | 300 t live/day, 5-day lead | game estimates (3,000 m³ Ronja Queen class, ~100 kg/m³; the game's existing booking delay) |
| Rent base | HOG kg × norm price; harvest cost not deducted | skatteloven § 19-5 (market value at the pen edge × harvest volume), § 19-6 (deductions list no slaughter or transport) |

### Acceptance

| Criterion | Measured | Test |
|---|---|---|
| 1. Revenue = HOG kg × size-class price × grade factor − harvest cost | exact to 0.01 NOK, express and planned; every report = its ledger entries; cash Δ = gross − cost | `TheLedger_BooksHogKilosTimesClassPriceTimesGrade_LessTheHarvestCost`, `APlannedHarvest_…`, `EconomyLedgerTests.Harvest_WithoutContracts…`, `FullProductionCycle_OnTheEnginePath…` |
| 2. A 2 kg fish earns ≥ 25% less per kg than a 5 kg fish | one fish: 0.72 (1.68 kg HOG in 1-2 kg vs 4.2 kg in 4-5 kg); engine pens at CV 1%: 72.0% gross, 69.7% net; at a real farm's CV 20%: 76.4% | `ATwoKiloFish…`, `TwoKiloFish…OnTheEnginePath` |
| 3. F270 closed | harvest cost booked as COGS `harvest`; the cash moves by revenue − cost (it moved by the whole revenue) | `PnLBookkeepingTests.HarvestSite_BooksRevenueExactlyMatchingTheCashFlow` |
| 4. EBIT/kg band on the PlayerScriptProbe path | median 18.19 NOK/kg HOG (11.07-23.23), 10 seeds | `AFarmCycle_EarnsAnEbitPerKiloInTheCyclicalBand_OnThePlayerPath` (band 8-25) |
| 5. Partial harvest | 40% of one pen: 4,000 of 10,000 fish, the rest feeds and grows again; half of a site through the bridge: 16,844 went, 16,747 stayed | `APartialHarvest_…`, `AnExpressPartialHarvest_…`, `APerPenHarvest_…`, UiFlowTests `harvest:` |

### EBIT/kg on the player path (`PlayerScriptFarmCycle`, 10 seeds)

Two sites of 8 pens × 75,000 standard S1 smolts in PO4 water, stocked mid-April,
events on, deloused at 0.4 adult females, each site's harvest ordered by the D2
rule, EBIT from the ledger (revenue − COGS − OPEX) per kg HOG sold.

| Seed | Days | t HOG | Revenue/kg | Cost/kg | EBIT/kg | Price achievement | Superior | Mortality |
|---|---|---|---|---|---|---|---|---|
| 1 | 470 | 2,986 | 68.59 | 57.52 | 11.07 | 0.927 | 82.3% | 37.8% |
| 2 | 471 | 2,660 | 68.47 | 53.47 | 15.00 | 0.925 | 83.5% | 43.2% |
| 3 | 444 | 3,487 | 69.23 | 48.61 | 20.63 | 0.962 | 88.8% | 32.7% |
| 4 | 479 | 2,680 | 68.48 | 55.90 | 12.57 | 0.925 | 82.3% | 43.5% |
| 5 | 467 | 3,436 | 68.83 | 49.24 | 19.59 | 0.930 | 84.2% | 28.7% |
| 6 | 458 | 3,448 | 69.05 | 47.22 | 21.83 | 0.933 | 84.4% | 29.4% |
| 7 | 457 | 3,340 | 70.40 | 49.00 | 21.40 | 0.951 | 85.2% | 35.6% |
| 8 | 444 | 3,197 | 63.62 | 49.18 | 14.44 | 0.919 | 81.7% | 32.4% |
| 9 | 455 | 4,054 | 68.80 | 45.57 | 23.23 | 0.930 | 82.5% | 17.3% |
| 10 | 447 | 3,292 | 68.43 | 51.65 | 16.79 | 0.925 | 82.5% | 31.4% |

Median 18.19 NOK/kg HOG inside SalMar's cyclical 8-25 (23.4 FY24, 13.6 FY25).
Real mapping: the game's cost per kg HOG (45.6-57.5) sits below Norway's ~66
(Fiskeridir 2023: 58.86 NOK/kg round weight before capital cost, ÷ 0.889): the
game books no insurance, health, administration or capital costs beyond its
wages, maintenance, storms and events, and the zone path's bloom deck kills
17-44% (S2-05/S4-03), which raises feed per kg sold (RUN_LOG §8). Harvests fall
in July-August, after the winter at sea: 82-89% superior.

`SimProbe.ThreeSite` (§1c) is unchanged (EBIT 21.74, cost 62.67 NOK/kg): it
prices live kilos at spot in its own synthetic P&L and never calls the harvest
verbs; the band test that counts is the player-path one above (RUN_LOG §8).

### Grades by season (engine, PO4 sea pen vs a 12 °C land tank, 5 kg at slaughter)

| Harvest | Sea pen superior | Land tank superior |
|---|---|---|
| April (after January-April below 8 °C) | 76.1% | 93.6% |
| September (after a summer) | 90.8% | 93.6% |

### Careers (`PlayerScriptProbe`, po3_red_sone, events on): S2-03 → S2-04

The career now orders its harvests (D2 rule) through the pipeline; the realized
price is per reference-equivalent kilo (HOG kg × size × grade), the unit the
spot is quoted in.

| Seed | Days | Live kg | HOG kg | Realized / spot of the slaughter days | Price achievement | Superior | Harvest cost | Ending balance (S2-03 → S2-04) |
|---|---|---|---|---|---|---|---|---|
| 1 | 333 → 341 | 507,123 → 508,912 | 427,486 | 88.00 / 88.00 | 0.951 | 92.5% | 2.14M | +29.43M → +17.98M |
| 2 | 330 → 341 | 422,632 → 411,628 | 345,767 | 93.49 / 93.49 | 0.941 | 90.2% | 1.73M | +23.49M → +15.47M |
| 3 | 306 → 313 | 515,228 → 517,887 | 435,025 | 87.14 / 87.14 | 0.960 | 92.8% | 2.18M | +31.76M → +19.90M |
| 7 | 348 → 340 | 507,436 → 502,032 | 421,707 | 77.75 / 77.75 | 0.954 | 92.7% | 2.11M | +23.74M → +14.75M |
| 13 | 345 → 339 | 495,497 → 489,836 | 411,462 | 87.61 / 87.61 | 0.950 | 92.8% | 2.06M | +27.84M → +17.17M |

Every band holds unchanged (seed 7 > -1M, seed 13 > -15M and realized 70-96,
FCR, mortality, the exact save/load continuation).

### The re-measure (roadmap R2: scenario data, not mechanics)

| Change | Why | Measured |
|---|---|---|
| `forste_sesong` 36,000 fish and 4,000,000 NOK (was 22,000 and 3,000,000) | step 16's 6M second site from the first harvest: a live kilo now earns ~27% less (HOG, size, grade, harvest cost) | minimal player, seeds 4/7/9/21/13/42: 22,000 + 3M won 3/6 (4 and 21 stalled at step 16 with 4.9-5.0M); 28,000: 2/6; 32,000 + 3M: 2/6 (6.7M after the first sale, the research leaves < 6.5M); 40,000 + 4.5M: 6/6 on the first harvest; **36,000 + 4M: 6/6, days 281-957** (21 and 13 on the second cycle), lowest cash +0.19M to +0.83M |
| Resource-rent base: the harvest cost is not deducted | skatteloven § 19-5 values the fish at the pen edge; § 19-6 lists no slaughter or transport | six-site career (`MultiSiteCareer_PaysResourceRentTax`), year-2 rent base, seeds 7/13/21: harvest value 184.1/172.7/195.2M on 2.06/2.14/2.22 kt HOG, costs 116.1/110.8/118.4M; rent paid with the harvest cost deducted 0/0/1.63M, without it 2.53/2.65/4.96M (S2-03: 11-30M over three years) |

### The seam for farm types

- `HarvestRoute.For(HoldingKind, calibration, holdingId, hallCapacity)`: a sea
  pen goes by wellboat to the player's slaughterhouse; an offshore pen by an
  offshore wellboat (×1.5 transport); a land tank by pipe or truck to its own
  hall (no wellboat, no lead time, its own capacity pool and capacity). A land
  farm supplies `hallCapacityTonnesHog`; its fish never count for resource rent.
- `HarvestCondition` (mean weight, cold-water memory, delousing injury load,
  lice, `ExtraProductionShare`) is all the grading reads: the holding fills it.
  A land tank at a controlled temperature never builds winter wounds; the
  farm-types maturation index and off-flavour plug into `ExtraProductionShare`.
- `FishPopulation.ColdExposure` and the fast's degree-days are tracked from
  `HoldingWaterOf(population)` in the growth step, so they follow whatever holds
  the fish.
## 1i. Wave 2 sim-a-1: low oxygen at the fish (2026-10-14, branch `wave2/sim-a-1`)

S2-05's `D4_Po3Career_LosesAtMost35Percent_AndEndsAboveTheDefaultBankruptcyFloor`
was red on the wave-2 base `68cce8b`. Once S2-02 put the po3 careers in real
PO3 water, low-oxygen deaths were 19.5 % (seed 7) and 18.7 % (seed 13) of all
deaths, and the test allows under 10 %. Calibration v1.0.7 (new keys in
`mortality`, new section `waterOxygen`).

### Where the low-oxygen deaths came from

There are two places in the engine that book `MortalityCause.LowOxygen`. One is
the bloom deck's `hypoxia` card (`RollBloomWeek`). The other is the storyteller's
`plankton-crash` event, which fires when a zone's oxygen is below 3 mg/L. The
diagnosis ran the po3 career probe (save/load at day 210) over seeds 1-20 with a
log sink that books each death to its source. A day-by-day replica of the same
script supplied the timing and the water.

| Source | Low-oxygen deaths, 20 careers | Careers hit | Notes |
|---|---|---|---|
| Bloom-deck `hypoxia` card | **65,225 (100 %)** | 14 | 18 sags, July (8), August (4), September (5), October (1); zone water 14.1-16.4 °C and **7.6-8.2 mg/L (saturated) at every sag** |
| Storyteller `plankton-crash` event | 0 | 0 | never fired: its trigger day exists only on a crash day |
| Plankton-bloom crashes (the zone water itself) | (no direct deaths) | — | 83 crashes in 20 careers, about 4 per career, in every zone each summer (May-October); 45 of them took the zone below 3 mg/L, the lowest to 2.41 mg/L |

Pooled over the 20 careers, low oxygen was 65,225 of 525,255 deaths (12.4 %).
The card killed `0.03 x severity x countermeasures` of the site's fish at each
weekly tick, whatever oxygen the water held. A sag of severity 0.4-1.0 lasting
7-21 days (one to three ticks) therefore took 1-9 % of the site. In PO3, the
14 °C gate is crossed from mid-July to late September, so about one sag fell
in each career summer. The national reference curve that the careers used
before S2-02 peaks at 13.5 °C and almost never crossed it.

### The real numbers the model is held to

- **Frequency.** NVI's environment class (B) was 1 % of all dead-fish
  registrations in PO1-PO4 in 2024. Nationally it was 0.2-2.7 % in 2021-2023 and
  8.8 % in 2024, three quarters of it jellyfish. Sources: Fiskehelserapporten
  2024, table 2.4.2 and pp. 36-38,
  https://www.vetinst.no/rapporter-og-publikasjoner/rapporter/2025/fiskehelserapporten-2024.
  Low oxygen is a small part of that 1 %.
- **Severity: in-cage hypoxia is common and mostly sub-lethal.** In a
  commercial Norwegian cage, oxygen readings ranged over 26-90 % saturation and
  about one reading in four was growth-limiting. The reference site stayed at
  levels for maximum growth throughout (Solstorm et al. 2018, Aquaculture
  486:122-129). Oldham's PhD thesis (University of Tasmania, 2018) concluded
  that DO conditions in cages rarely threaten the survival of farmed salmon,
  while hypoxia that hurts production and welfare is common.
- **Lethal limit.** US EPA (1986), EPA 440/5-86-003, gives 3 mg/L as the
  "Limit to Avoid Acute Mortality" for salmonid waters, other life stages.
  Severe, moderate and slight production impairment sit at 4, 5 and 6 mg/L.
  Atlantic salmon's critical oxygen level is 3.46 mg/L at 14 °C and 3.39 mg/L at
  18 °C (Barnes, King & Carter 2011, Aquaculture 318:397-401). The limiting
  saturation rises from 30 % at 6 °C to 55 % at 18 °C (Remen et al. 2013,
  Aquaculture 416:41-47).
- **Solubility.** Saturated seawater (35 PSU) holds 9.0 mg/L at 10 °C, 8.0 at
  16 °C and 7.7 at 18 °C (Benson & Krause 1984, in the engine since S1-01). The
  same fractional sag therefore leaves less oxygen in warm water.
- **A bloom's oxygen budget.** A seasonal coastal bloom reaches 1-6.5 µg/L
  chlorophyll a (NIVA FerryBox, Indre Oslofjord 2017), and below 0.3 to 10 µg/L
  in 2018. At about 50 g C per g chlorophyll (inside the range Cloern et al.
  1995 compiled) and Redfield's 138 O2 per 106 C (3.47 g O2 per g C), the whole
  of a 10 µg/L bloom holds 1.7 mg/L of oxygen demand.

### What changed (the model, not the band)

1. **One low-oxygen hazard at the fish's own water.**
   `MortalityHazards.LowOxygenFraction(environment, calibration)` kills nothing
   at or above 3 mg/L, a tenth below it, a quarter below 2 mg/L and half below
   1 mg/L (the plankton-crash event's steps, now registry keys). Both the
   plankton-crash event and the hypoxia card use it, and it reads
   `ResolveCohortEnvironment`. A land tank or an offshore structure therefore
   answers with its own water.
2. **The `hypoxia` card is an in-cage oxygen sag.** While it runs, the site's
   pens' water carries `O2 x (1 - severity x 0.42 x diffuser x containment)`,
   where `waterOxygen.hypoxiaSagMaxDepth` is 0.42. The worst open-net sag holds
   the cage's daily mean at 58 % of ambient, the middle of Solstorm's 26-90 %
   range. An O2 diffuser scales the depth by 0.3; semi-closed and closed pens
   scale it by their oxygen demand (1.4 and 1.6). The water reaches the fish
   through the seam (`HoldingWaterOf`), so appetite reads it every day and the
   card's weekly tick kills only the fish whose water is below 3 mg/L. Gill
   damage is unchanged.
3. **A plankton crash takes the oxygen its carbon needs.** That is the dying
   biomass's share of capacity times
   `waterOxygen.bloomChlorophyllAtCapacityUgL x phytoplanktonCarbonPerChlorophyll x oxygenPerOrganicCarbon`
   (10 x 50 x 3.47 / 1000 = 1.735 mg/L). The old crash cut 75 % x the peak from
   the zone's oxygen, which took every zone from about 8 mg/L to about 3 mg/L
   twice a summer. Bloom growth, frequency and respiration are unchanged.

In saturated 16 °C water, the worst open-net sag leaves 4.6 mg/L, which costs
appetite but kills no fish. A sag kills only when the cage's water is already
low or a closed pen's demand deepens it. The worst sag in a closed floating pen
leaves 33 % of ambient: 3.2 mg/L in saturated 7 °C water (no deaths) and 2.5
mg/L at 18 °C (a tenth die). `LowOxygenModelTests` pins each of these cases.

### Measured after the fix

The po3 career probe was run on seeds 1-20, with the base and the fix on the
same script and seeds.

| Measure | Base `68cce8b` | sim-a-1 |
|---|---|---|
| Low-oxygen deaths, pooled | 65,225 of 525,255 (12.4 %) | **0 of 462,613** |
| Careers with any low-oxygen death | 14 of 20 | **0 of 20** |
| Low-oxygen share of deaths, median / max | 9.0 % / 31.4 % | 0 / 0 |
| Sea loss, median (min-max) | 18.20 % (13.21-29.98) | **16.72 % (11.74-24.05)** |
| Environment share of deaths, median (min-max) | 37.9 % (4.3-61.1) | 30.7 % (3.2-48.5) |
| Environment, % of stock, median | 6.93 | 5.28 |
| Ending balance, median / min | +26.10M / +21.24M | +26.95M / +22.90M |
| Economic FCR, median | 1.179 | 1.177 |
| Pooled causes | anchor 39.7, jellyfish 14.8, **low oxygen 12.4**, disease 12.3, algae 9.4, delousing 8.7, storm 2.6 % | anchor 45.5, jellyfish 17.0, disease 14.1, algae 10.7, delousing 9.9, storm 2.9 % |

The 200-seed sweep (probe without save/load, seeds 1-200) gives the tail:

| Measure | Base | Sag model only (crash unchanged) | sim-a-1 (sag + crash) |
|---|---|---|---|
| Careers with low-oxygen deaths | 123 | 13 | **1** |
| Low-oxygen share of pooled deaths | 11.9 % | 3.06 % | **0.157 %** |
| Sea loss median / p95 / max | 20.59 / 28.93 / 31.69 % | 18.05 / 25.17 / 38.52 % | **17.88 / 24.05 / 26.99 %** |
| Ending balance, min | +4.64M | +5.00M | +5.04M |

The middle column explains why step 3 is part of the fix. With the sag reading
the water but the crash still taking 75 % of the oxygen, a sag tick that fell on
a crash day killed 10-25 % of a site in 13 careers. The worst career, seed 37,
lost 38.5 %. The one career left in the sim-a-1 column is a compound event:
seed 121's semi-closed `po3_nord` had a severity-0.98 sag on the day of a
plankton crash in late June. The zone held 6.95 mg/L, the cage 2.95 mg/L, and
7,761 fish died.

The 600-day po3 water probe (`WaterChemistryStabilityTests`' script) was run on
seeds 7 and 13:

| Measure | Base | sim-a-1 |
|---|---|---|
| Zone oxygen minimum | 2.88-2.96 mg/L | **6.51-6.95 mg/L** |
| Days below 70 % saturation, per zone | 4 | **0** |
| Days below 5 mg/L, per zone | 4 | 0 |

### Careers (`PlayerScriptProbe`, po3_red_sone, events on, save/load at day 210)

| Seed | Stage | Days | Harvested kg | Realized NOK/kg | Econ FCR | Sea loss | Low-oxygen deaths | Ending balance |
|---|---|---|---|---|---|---|---|---|
| 7 | base `68cce8b` | 315 | 460,912 | 75.52 | 1.179 | 23.86 % | 6,210 of 31,927 (19.5 %) | +21.24M |
| 7 | sim-a-1 | 315 | 487,858 | 75.52 | 1.178 | **19.45 %** | **0 of 26,019** | **+22.90M** |
| 13 | base `68cce8b` | 331 | 493,072 | 78.40 | 1.176 | 20.59 % | 5,144 of 27,546 (18.7 %) | +22.94M |
| 13 | sim-a-1 | 331 | 516,432 | 78.40 | 1.175 | **16.87 %** | **0 of 22,551** | **+24.56M** |

D4 holds as written for both seeds: at most 35 % lost, above the -1M floor,
and low oxygen under 10 % of deaths. The other probe bands also hold:
`PlayerScriptCareerTests` and `FullCycleEconomicTests` pass unchanged, as do
the tutorial (`ScenarioCampaignTests`) and the water bands
(`WaterChemistryStabilityTests`).

Seed 13's breakdown (input to S4-03): infectious 16.6 %, environment 23.3 %
(jellyfish 9.8, algae 9.5, storm 4.0), injury and treatment 10.2 %,
unexplained 50.0 %. The environment class is still far above NVI's 1 % in
PO1-PO4, now from jellyfish and algae alone (RUN_LOG §8).

## 1j. Wave-2 sim integration: S2-01a capacity, the harvest ledger, the D2 ruling (2026-10-15, branch `wave2/sim-int-1`)

The integrated tree is `integration/wave2` (`68cce8b`: wave 1, S2-02, S2-05) plus
`wave2/sim-b-3` (S2-03, S2-04), `wave2/sim-a-1` (the holding seam and the
low-oxygen model) and `wave2/sim-a-2` (S2-01a capacity). It runs calibration
v1.0.9 and save format v7. What the merges changed in the model:

| Change | Why | Effect |
|---|---|---|
| Every harvest path takes its fish through `RemoveAlive` (express, planned, hired wellboat, RAS hall, offshore, Mattilsynet's and S2-01a's forced harvests) | S2-04 decremented `Cohort.Count` directly; S2-05's ledger must reconcile per cohort per day | none in numbers; a report's fish = the ledger's removals (`HarvestLedgerReconciliationTests`) |
| Forced harvests sell the fish they remove (Mattilsynet: proportional per pen; S2-01a: heaviest first) through the S2-04 pipeline | the Mattilsynet path sold one set and removed another; S2-01a sold live kilos at 70 % of the gutted price, with no harvest cost | an S2-01a deadline harvest of ~190 t live sells ~160 t HOG for 8.8-9.2M gross with 0.94-0.98M harvest cost (seeds 31-33) |
| The biology tick reads the holding the step resolved (`HoldingOf` → `WaterOf`) | one resolution per step for S2-03's growth and feeding | byte-identical saves (careers 1/2/7/13, two mixed farms) |
| A stocking fills as many 10,000 m³ density proxy pens as it needs at 25 kg/m³ and 5 kg (`capacity.maxStockingDensityKgPerM3`, akvakulturdriftsforskriften § 25; `capacity.densityReferenceWeightKg`) | S2-01a stocks a site's plan room at once (173,555 smolts per po3 site, 561,600 at a barekraft_mester site), and one proxy pen put those fish at 90-280 kg/m³ | populations of ≤ 50,000 fish unchanged; the six-site rent career no longer goes bankrupt |

### S2-01a acceptance on the integrated tree

| Acceptance | Test | Result |
|---|---|---|
| 1. Overstock refused or fined, below the compliant player | `CapacityStrategyTests.Overstock_IsRefused_AndEndsBelowTheCompliantPlayer` (seeds 1-6) | refused at both sites (`mtb_area_projected_peak`), no cash moved; below compliant on seeds 1-6 |
| 2. Daily enforcement | `CapacityEnforcementTests` (9) | order, fee and confiscation on the same day; fee stops at 15 G; deadline harvest to 95 % of each cap |
| 3. A build adds 0 t | `LicenceCapacityTests` | a 3,120 t locality and 0 t of licence MTB in po3, forste_sesong and krise_manager |
| 5. Save/load and twins | `CapacityEnforcementTests.AnOpenOrder_SurvivesASave...`, the determinism suites, `ASaveFromBeforeTheRegister_GetsOneLicencePerSite_InItsArea` | green |
| 6. `forste_sesong` wins ≥ 4/6 seeds | `ScenarioCampaignTests` (minimal player) | **6 of 6** (table below) |

Capacity careers, po3_red_sone, events on (`CapacityStrategyProbe`), ending
equity in M NOK, 20 seeds:

| Seed | Compliant | Overstock (v1, refused) | Overstock (old rules) | Grow-on fined | Grow-on unpoliced |
|---|---|---|---|---|---|
| 1 | 24.66 | 15.33 | 69.24 | 4.31 | 16.00 |
| 2 | 16.69 | 13.98 | 52.99 | -8.57 | -20.66 |
| 3 | 33.12 | 16.77 | 116.96 | 26.66 | 40.89 |
| 4 | 19.18 | 13.19 | 35.29 | 7.90 | 14.42 |
| 5 | 41.09 | 14.84 | 106.31 | 16.18 | 34.65 |
| 6 | 18.09 | 13.85 | 105.58 | 17.41 | 50.17 |
| 7 | 44.47 | 9.60 | 127.12 | 31.52 | 44.38 |
| 8 | 28.25 | 10.61 | 113.16 | 99.51 | 91.99 |
| 9 | 43.62 | 22.06 | 174.69 | 13.30 | 62.69 |
| 10 | 9.71 | 13.00 | -29.68 | 8.11 | 30.23 |
| 11 | 23.32 | 10.70 | 49.30 | -3.46 | -8.60 |
| 12 | 26.26 | 12.49 | 115.29 | 26.97 | 38.25 |
| 13 | 21.77 | 10.89 | 102.53 | 34.96 | 47.26 |
| 14 | 14.72 | 15.22 | 96.41 | 20.13 | 45.22 |
| 15 | 31.08 | 14.78 | 105.82 | 19.92 | 43.84 |
| 16 | 39.18 | 16.07 | 129.81 | 14.40 | 5.40 |
| 17 | 29.20 | 12.18 | 54.59 | -1.72 | 21.92 |
| 18 | 37.90 | 11.97 | 125.15 | 36.20 | 83.77 |
| 19 | 30.01 | 14.64 | 70.39 | -6.32 | 1.50 |
| 20 | 24.73 | 15.50 | 130.23 | 21.31 | 59.00 |
| **Median** | **27.25** | **13.92** | **105.70** | **16.80** | **39.57** |

The compliant player harvests 1,181-1,485 t at a 1,107-1,358 t peak against
1,560 t, in 356-469 days, with no breach. The refused overstocker keeps only
the starting fish (~430 t) and ends below the compliant player on 18 of 20
seeds (10 and 14 are poor compliant years); under the old rules the same
script (3x the plan's fish, 1,041,330 smolts, borrowed when only money refuses
it) harvests 3,612-4,312 t and dominates on 19 of 20 seeds (median 105.7M vs
27.3M). The grow-on player breaks a cap 5-6 times on every seed and pays
11.8-17.8M of fees and confiscations; its median ends below both the unpoliced
script and the compliant player (roadmap D10 (a)). Per seed the fined career
ends above the unpoliced one on 4 of 20 seeds (2, 8, 11, 16): the storyteller's
price shocks pin the spot at 43 NOK/kg for 2-4 weeks, and the unpoliced
career's fixed sale day (60 days after 4.5 kg) falls in one while the fined
career, its heaviest fish taken at a deadline, sells later (seed 2: po3_nord
1,096 t at 42.7 NOK/kg on day 510 unpoliced, 800 t at 75.2 on day 537 fined).
Before the density fix both careers crawled at the Oppedal floor and the
fined one lost on 20 of 20.

Six-site rent career (`HonestEconomyTests.MultiSiteCareer_PaysResourceRentTax`,
barekraft_mester, 8 licences): resource rent paid 81.56M / 29.45M / 90.28M on
seeds 7/13/21 over three years, no breach (the plan refuses what the pool
cannot hold). Before the density fix: bankrupt on days 871/535/609.

`forste_sesong`, the minimal player (`ScenarioCampaignTests`, events on):

| Seed | Won | Day | First harvest | Lowest cash |
|---|---|---|---|---|
| 4 | yes | 289 | 287 | 0.55M |
| 7 | yes | 315 | 313 | 0.19M |
| 9 | yes | 280 | 278 | 0.38M |
| 21 | yes | 718 | 312 | 0.89M |
| 13 | yes | 1,196 | 313 | 0.88M |
| 42 | yes | 302 | 300 | 0.76M |

po3 careers (`PlayerScriptProbe`, events on, save/load at day 210):

| Seed | Days | Harvested kg | Realized NOK/kg | Econ FCR | Sea loss | Low-oxygen deaths | Superior | Ending balance |
|---|---|---|---|---|---|---|---|---|
| 1 | 341 | 517,806 | 88.00 | 1.182 | 17.24 % | 0 of 23,086 | 92.5 % | +18.49M |
| 2 | 341 | 463,331 | 92.69 | 1.204 | 24.66 % | 0 of 33,016 | 90.3 % | +18.09M |
| 7 | 339 | 503,137 | 77.78 | 1.178 | 20.18 % | 0 of 26,998 | 92.7 % | +14.85M |
| 13 | 338 | 510,963 | 87.62 | 1.184 | 17.13 % | 0 of 22,906 | 92.8 % | +18.35M |
| 21 | 313 | 533,077 | 75.46 | 1.176 | 17.26 % | 0 of 23,104 | 92.9 % | +16.64M |

D4 holds for seeds 7 and 13 (≤ 35 % lost, above -1M, low oxygen < 10 % of deaths).

### The D2 ruling: PO4 against PO13 on the climatology

Owner ruling (merge captain, autonomy mandate): the pre-registered 60-120 day
gap is judged on the climatology, not on one weather year. The pre-registered
harness (`FullCycleEconomicTests`: one site per area by data, 10,000 standard
S1 smolts released mid-April, events off, days to a 5 kg mean):

Normal year (shared anomaly off, daily fluctuation kept):

| Seed | PO4 | PO13 | Gap |
|---|---|---|---|
| 1 | 436 | 545 | 109 |
| 2 | 455 | 546 | 91 |
| 3 | 441 | 545 | 104 |
| 4 | 450 | 544 | 94 |
| 5 | 437 | 551 | 114 |

Ten weather years per seed (a release every mid-April of years 1-10 of the
seed's weather, each year-class in its own zone), gap in days per release year:

| Seed | Y1 | Y2 | Y3 | Y4 | Y5 | Y6 | Y7 | Y8 | Y9 | Y10 | Median |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 155 | 114 | 92 | 84 | 91 | 70 | 85 | 223 | 174 | 108 | 100 |
| 2 | 109 | 104 | 121 | 174 | 187 | 107 | 95 | 65 | 124 | 98 | 108 |
| 3 | 70 | 85 | 67 | 78 | 75 | 72 | 114 | 95 | 54 | 118 | 76.5 |
| 4 | 168 | 237 | 120 | 124 | 90 | 48 | 144 | 146 | 145 | 80 | **134** |
| 5 | 121 | 120 | 117 | 97 | 47 | 73 | 114 | 115 | 130 | 104 | 114.5 |

Days to 5 kg, seeds 1-4 (seed 5 from the 20-seed run): PO4 418-498, PO13
488-711. Seed 4's first two PO13 cycles ran 1.01 and 1.10 °C below normal
(mean shared anomaly over the cycle), which keeps PO13 at sea through a
second winter. Over seeds 1-20 x 10 years (200 year-classes) the gap's median
is 96 days (p10 66, p90 145), 143 of 200 years lie in 60-120, and the
10-year median lies in the band on 19 of 20 seeds (seed 4 the exception).
Year 1 reproduces S2-03's single-year measurement (seed 1: 469/624 vs
470/627; seed 4: 498/666 vs 497/665). The normal-year test passes on every
seed; the ten-year test fails on seed 4 and is skipped with the band
unchanged (RUN_LOG §8).

**Closed (2026-10-15, `wave3/sim-core-1`).** The captain's ruling under the
owner's autonomy mandate judges part 2 on the pooled climatology: over seeds
1-20 x 10 weather years the median gap must lie in 60-120 days and at least
90 % of the seeds' own ten-year medians in 60-120.
`Po4ReachesFiveKilos_TwoToFourMonthsBeforePo13_PooledOverTwentySeedsAndTenWeatherYears`
replaces the skipped per-seed test (same harness, band unchanged) and passes,
re-measured on the wave-2 base `8c81754` (3 s, the 40 engines in parallel):

| Seed | Gaps, years 1-10 | Median |
|---|---|---|
| 6 | 137, 85, 66, 72, 85, 118, 66, 68, 51, 106 | 78.5 |
| 7 | 107, 92, 81, 77, 70, 69, 67, 105, 115, 98 | 86.5 |
| 8 | 95, 102, 93, 138, 123, 89, 93, 117, 246, 129 | 109.5 |
| 9 | 122, 134, 167, 88, 74, 127, 105, 66, 42, 110 | 107.5 |
| 10 | 64, 86, 144, 98, 62, 62, 79, 74, 85, 71 | 76.5 |
| 11 | 74, 88, 153, 110, 115, 100, 105, 102, 113, 157 | 107.5 |
| 12 | 97, 98, 93, 84, 59, 118, 95, 97, 147, 147 | 97 |
| 13 | 109, 172, 114, 81, 63, 72, 93, 109, 117, 108 | 108.5 |
| 14 | 153, 117, 79, 106, 75, 87, 97, 95, 123, 138 | 101.5 |
| 15 | 111, 113, 96, 78, 87, 159, 90, 68, 60, 51 | 88.5 |
| 16 | 74, 78, 70, 84, 124, 127, 95, 71, 59, 45 | 76 |
| 17 | 91, 94, 135, 139, 186, 171, 131, 89, 39, 47 | 112.5 |
| 18 | 101, 109, 74, 121, 77, 41, 103, 97, 102, 96 | 99 |
| 19 | 92, 29, 64, 98, 79, 72, 79, 85, 80, 90 | 79.5 |
| 20 | 88, 93, 109, 96, 126, 127, 102, 106, 85, 88 | 99 |

Seeds 1-5 as in the table above. Pooled over 200 year-classes: median 96
days (p10 66, p90 145), 143 of 200 in 60-120; 19 of 20 seed medians in 60-120
(seed 4, 134, the one outside; the ruling allows two). Mutations: requiring
every seed's median in the band fails (19 of 20); a PO1 release against PO13
(summer stall) fails (pooled median 65, 12 of 20 seeds).

## 1k. S2-01b: real pens (2026-10-15, branch `wave2/sim-int-2`)

`wave2/sim-int-1` plus real pens: calibration v1.0.10 (section `pens`), save
format v8. A sea site is a frame mooring of pens (`SeaSite.PhysicalPens`); each
pen holds one population, at most 200,000 fish (akvakulturdriftsforskriften
§ 47a) and at most 25 kg/m³ (§ 25) in the volume its net encloses. Stocking
picks a pen and a count; the density is checked at the plan's projected peak,
grown on the S2-03 growth law. Decisions in RUN_LOG §7 (2026-10-15 S2-01b rows).

### The pen

| Net | Collar | Wall + cone | Volume | Used at |
|---|---|---|---|---|
| Standard (calibration) | 160 m (2,037 m²) | 25 m + 15 m | 61,116 m³ | built sites; laksebaronene, barekraft_mester, po3_red_sone, isa_utbrudd, ressursrente |
| Family farm / northern | 120 m (1,146 m²) | 20 m + 10 m | 26,738 m³ | forste_sesong, lofoten_eventyr (PO9), krise_manager (PO12), algekatastrofe (PO9) |

Volume = C²/(4π) × (wall + cone/3). Sources: § 25 ("the volume the fish can
move in"); iLaks 9 March 2020 (160 m standard, 200 m the largest, smaller in
the north and on rough sites, a 40 m net); Bergheim 2012 (Lat. Am. J. Aquat.
Res. 40(3): 800-807: nets 20-50 m deep, a third of Norway's ~4,000 cages over
20,000 m³). The wall/cone split and the scenario layouts are game estimates.

Scenario localities (`pen_layout`; the stocked pens are the first ones):

| Scenario | Site | Pens | Net | Stocked at start |
|---|---|---|---|---|
| forste_sesong | bestefars_anlegg | 6 | 120 m | 1 × 36,000 |
| lofoten_eventyr | nordland_premium | 6 | 120 m | 2 × 8,000 |
| laksebaronene | trondelag_base | 8 | 160 m | 3 × 10,000 |
| krise_manager | finnmark_turnaround | 6 | 120 m | 2 × 6,000 |
| barekraft_mester | lakskongen_hovedanlegg | 10 | 160 m | 4 × 12,000 |
| po3_red_sone | po3_nord / po3_sor | 8 / 6 | 160 m | 6 × 12,000 / 4 × 10,000 |
| isa_utbrudd | isa_kjerne / isa_nord / isa_sor | 6 / 8 / 8 | 160 m | 4 × 8,000 / 6 × 12,000 / 6 × 12,000 |
| algekatastrofe | alge_sor / alge_midt / alge_indre | 10 / 10 / 6 | 120 m | 8 × 14,000 / 8 × 14,000 / 5 × 9,000 |
| ressursrente | vest_1 / vest_2 / vest_3 | 10 / 10 / 8 | 160 m | 8 × 15,000 / 8 × 15,000 / 6 × 12,000 |

### A pen's room (the plan on the growth law)

One 100 g standard smolt grown on the S2-03 law in its area's climatology and
day length, fed to appetite, to the plan's 5.2 kg with the plan's survival
(15 % over 540 days); the pen takes 25 kg/m³ × volume / the peak kilos per
smolt, and § 47a caps it at 200,000 (`PenDensityPlanner`, scratch measurement
on this branch):

| Area | Release | Peak after | Survival | Peak kg/smolt | 120 m pen | 160 m pen (density only) | 200,000 in a 160 m pen peak at |
|---|---|---|---|---|---|---|---|
| PO3 | 1 Jan | 500 d | 86.0 % | 4.480 | 149,198 | 200,000 (341,024) | 14.7 kg/m³ |
| PO3 | mid-Apr | 445 d | 87.5 % | 4.563 | 146,486 | 200,000 (334,825) | 14.9 kg/m³ |
| PO3 | mid-Sep | 446 d | 87.4 % | 4.556 | 146,731 | 200,000 (335,385) | 14.9 kg/m³ |
| PO4 | 1 Jan | 503 d | 86.0 % | 4.474 | 149,403 | 200,000 (341,494) | 14.6 kg/m³ |
| PO4 | mid-Apr | 445 d | 87.5 % | 4.551 | 146,866 | 200,000 (335,694) | 14.9 kg/m³ |
| PO6 | mid-Apr | 442 d | 87.5 % | 4.578 | 146,011 | 200,000 (333,741) | 15.0 kg/m³ |
| PO9 | 1 Jan | 547 d | 84.8 % | 4.421 | 151,199 | 200,000 (345,598) | 14.5 kg/m³ |
| PO9 | mid-Sep | 550 d | 84.7 % | 4.408 | 151,646 | 200,000 (346,621) | 14.4 kg/m³ |
| PO12 | 1 Jan | 598 d | 83.5 % | 4.356 | 153,457 | 200,000 (350,758) | 14.3 kg/m³ |
| PO12 | mid-Sep | 640 d | 82.5 % | 4.299 | 155,480 | 200,000 (355,382) | 14.1 kg/m³ |

§ 47a binds in a standard pen and § 25 in a 120 m pen. The colder the water,
the longer the fish stand and the more of them die on the way, so a northern
pen takes ~4 % more smolts. Engine checks (`PenStockingTests`, barekraft_mester,
PO4, 1 January): 200,000 smolts in a standard pen peak at 14.69 kg/m³ on day
497 and are accepted, 200,001 are refused (`pen_fish_limit`); a 90 m pen
(11,817 m³) takes 65,825 smolts (peak 25.000 kg/m³ at 5.21 kg), one more peaks
at 25.0003 and is refused (`pen_density`) although the smolts stand at
0.56 kg/m³ that day. In the forste_sesong UI test a 120 m pen offers 149,026
smolts; 190,000 would peak at 31.9 kg/m³.

### What changed in the careers (sim-int-1 → this branch, same harness)

Fish now stand in their pen's volume instead of the wave-2 proxy (10,000 m³
per 50,000 fish at 5 kg). Below 25 kg/m³ density does nothing to growth or
FCR, so a career changes only where a population stood above 25 kg/m³ in the
proxy (fish grown past 5 kg) or a stocking now spans pens:

| Harness | sim-int-1 | S2-01b |
|---|---|---|
| `forste_sesong`, minimal player, seeds 4/7/9/21/13/42 | 6 of 6 won, days 289/315/280/718/1,196/302 | **6 of 6 won, identical days**, lowest cash 0.19-0.89M |
| po3 careers (`PlayerScriptProbe`), seeds 1/2/7/13/21 | +18.49/+18.09/+14.85/+18.35/+16.64M | identical (the saves of seeds 1/7/13 are byte-identical once the pen keys are dropped) |
| S2-03 acceptance (10 seeds): days to 5 kg, eFCR on the commercial pen | median 448 d; eFCR 1.230-1.246 | identical |
| Farm cycle EBIT/kg HOG (`PlayerScriptFarmCycle`, 10 seeds) | median 16.46, min 4.42, max 23.52 | identical but seeds 7 (+1 t HOG, 21.97 → 21.99) and 9 (+1 t, 23.52) |
| Capacity strategies, 20 seeds, median equity: compliant / v1 overstock / old-rules overstock / grow-on fined / grow-on unpoliced | 27.26 / 13.91 / 105.70 / 16.80 / 39.57M | 27.26 / 13.91 / **103.72** / **18.29** / **39.95**M |
| Six-site rent career (barekraft_mester, `HonestEconomyTests`), seeds 7/13/21: resource rent paid | 81.56 / 29.45 / 90.28M | **101.49 / 48.64 / 105.88M** |

The compliant and refused-overstock careers are identical on all 20 seeds.
The old-rules overstock (1,041,330 smolts: two free pens a site at up to
260,333 smolts each) and the grow-on careers (173,555 smolts in one standard
pen, grown past 5 kg) no longer meet the proxy's density stress: their fish
stand at 13-22 kg/m³ in 61,116 m³ pens where the proxy held them at 23-28
kg/m³ once they grew past 5 kg.
D10 (a) still holds for the grow-on family (fined median 18.29M below the
compliant 27.26M and the unpoliced 39.95M; per seed the fined career ends
above the unpoliced on the same 4 of 20 seeds, 2/8/11/16), and the old-rules
overstock still dominates on 19 of 20.

The rent career stocks 561,600 smolts a site in one call, now three pens of
187,200. Its gain is the storyteller's ILA event, which culls "the first sea
pen": it took a whole site's stocking before and takes one pen now
(disease-cull deaths 428,364 / 433,224 / 397,230 → 142,789 / 278,769 /
132,428 fish on seeds 7/13/21; harvested 13,964 / 10,463 / 14,226 →
15,409 / 11,749 / 15,398 t). Real ILA control empties the whole locality; the
event is left as it is for S4-02 (D5) and RUN_LOG §8 carries the finding.

## 1l. F2-01: the golden traces' baseline (2026-10-15, branch `wave2/sim-int-3`)

The golden traces (docs/GOLDEN_TRACES.md) record every shipped scenario x seeds
1/7/13 x 730 days with events on and the golden player (`golden-player/1`:
planned harvest at 4.5 kg, hydrolicer above 0.4 adult females, restock after a
30-day fallow within the cash for smolts and their feed to 4.5 kg, one
research). They are the before-state every later sim item is diffed against;
no tuning was done here. Recorded on `dc0b56a` (calibration and save format
as `wave2/sim-int-2`: v1.0.10, save v8), osx-arm64, .NET 10.0.12.

| scenario | seed | harvest orders | stockings | research | delousings | batches sold | HOG t sold | cash day 730 (M NOK) | live t day 730 | peak adult female lice | status |
|---|---|---|---|---|---|---|---|---|---|---|---|
| algekatastrofe | 1 | 3 | 3 | 1 | 0 | 8 | 844.8 | -26.15 | 1073.7 | 0.033 | won d391 |
| algekatastrofe | 7 | 3 | 3 | 1 | 0 | 9 | 839.7 | -42.30 | 1635.6 | 0.033 | won d391 |
| algekatastrofe | 13 | 3 | 3 | 1 | 0 | 8 | 897.2 | -46.35 | 1617.9 | 0.034 | won d391 |
| barekraft_mester | 1 | 1 | 1 | 1 | 0 | 2 | 137.2 | 22.29 | 619.9 | 0.031 | playing |
| barekraft_mester | 7 | 1 | 1 | 1 | 0 | 1 | 115.1 | 14.25 | 1010.0 | 0.029 | playing |
| barekraft_mester | 13 | 1 | 1 | 1 | 0 | 2 | 153.2 | 22.97 | 631.1 | 0.029 | playing |
| forste_sesong | 1 | 1 | 2 | 1 | 0 | 1 | 102.7 | 4.45 | 51.4 | 0.047 | playing |
| forste_sesong | 7 | 1 | 1 | 1 | 0 | 1 | 100.0 | 2.33 | 108.4 | 0.030 | playing |
| forste_sesong | 13 | 1 | 2 | 1 | 0 | 1 | 105.9 | 4.27 | 31.0 | 0.046 | playing |
| isa_utbrudd | 1 | 3 | 3 | 1 | 0 | 5 | 483.4 | -27.98 | 519.5 | 0.031 | lost d571 |
| isa_utbrudd | 7 | 3 | 3 | 1 | 0 | 6 | 516.6 | -29.85 | 370.9 | 0.031 | lost d571 |
| isa_utbrudd | 13 | 3 | 3 | 1 | 0 | 5 | 535.0 | -40.59 | 1048.1 | 0.031 | lost d571 |
| krise_manager | 1 | 1 | 0 | 0 | 0 | 1 | 41.0 | -1.46 | 0.0 | 0.017 | playing |
| krise_manager | 7 | 1 | 0 | 0 | 0 | 1 | 39.5 | -2.19 | 0.0 | 0.017 | playing |
| krise_manager | 13 | 1 | 0 | 0 | 0 | 1 | 37.3 | -1.37 | 0.0 | 0.017 | playing |
| laksebaronene | 1 | 1 | 2 | 1 | 0 | 1 | 78.4 | 1.10 | 19.3 | 0.047 | playing |
| laksebaronene | 7 | 1 | 1 | 1 | 0 | 1 | 83.3 | -0.68 | 90.6 | 0.030 | playing |
| laksebaronene | 13 | 1 | 1 | 1 | 0 | 1 | 87.3 | 0.40 | 0.0 | 0.029 | playing |
| lofoten_eventyr | 1 | 1 | 1 | 1 | 0 | 1 | 55.9 | 1.25 | 0.0 | 0.030 | playing |
| lofoten_eventyr | 7 | 1 | 1 | 1 | 0 | 2 | 51.6 | 1.21 | 23.4 | 0.033 | playing |
| lofoten_eventyr | 13 | 1 | 1 | 1 | 0 | 1 | 51.6 | 0.78 | 0.0 | 0.033 | playing |
| po3_red_sone | 1 | 2 | 2 | 1 | 0 | 3 | 363.7 | -12.11 | 216.3 | 0.030 | playing |
| po3_red_sone | 7 | 2 | 2 | 1 | 0 | 3 | 355.7 | -15.25 | 118.5 | 0.029 | lost d728 |
| po3_red_sone | 13 | 2 | 2 | 1 | 0 | 4 | 369.3 | -13.77 | 243.7 | 0.029 | playing |
| ressursrente | 1 | 3 | 4 | 1 | 0 | 8 | 853.3 | -2.57 | 1207.3 | 0.039 | lost d723 |
| ressursrente | 7 | 5 | 3 | 1 | 0 | 14 | 1829.7 | 75.95 | 512.8 | 0.026 | playing |
| ressursrente | 13 | 3 | 4 | 1 | 0 | 9 | 940.0 | 14.66 | 846.5 | 0.039 | won d331 |

Status is the scenario's verdict when it changed (the run continues). Every run
sells its starting fish through a planned harvest order; 24 of 27 restock and
start research (krise_manager's single small harvest leaves no cash above the
reserve). A first version of the player that spent all cash above 1 M on smolts
lost 16 of 27 runs (feed paid on credit until the bankruptcy floor); budgeting
each smolt's feed to 4.5 kg loses 5 (isa_utbrudd on every seed, po3_red_sone
seed 7 on day 728, ressursrente seed 1 on day 723).

**Lice (finding, RUN_LOG §8):** the sites' adult females per fish never pass
0.047 in any run (po3_red_sone, a red zone: 0.029-0.030), so the 0.4 delousing
rule never fires. The site model is seeded (po3_red_sone seed 7: 0.10 and 0.17
lice of all stages per fish on day 30) but the adult stage does not develop;
`SimProbe`'s synthetic sites reach a median peak of 0.52 (§1d).

**Size and time:** 27 traces, 1.80 MB (52-82 KB each; 758 lines: header, 731
days, 26 snapshots). Recording all 27 takes 6 s with 4 jobs on the M1 Pro; the
script 10 s a commit (temporary worktree, build, record) and 20-25 s for base
and head; the 27 comparisons in `dotnet test` 7 s (the suite 20 -> 26 s).
Recording one commit twice gives byte-identical files (28 of 28, `diff -r`).

**Sensitivity (mutation checks):** feed price 22.30 -> 22.31 NOK/kg: all 27
traces differ from day 1 (`ledger`, then `cash`; a `--detail` diff names
`ledger > cost/feed > nok` 3,494.18 -> 3,495.75 NOK on day 1). One cohort's
weight raised by one ulp on day 200 in po3_red_sone seed 7: named on day 200
(`pop > po3_nord/p01 > kg, c0.w`), 531 of 731 days then differ (cash 527,
ledger 526, mort 527, pop 102, mtb 35, env 3) and the final cash moves by
0.00000000016 NOK.

## 1m. F2-03: site kinds, plots and the facility migration (2026-10-16, branch `wave3/sim-core-2`)

No tuning. F2-03 gives the farm types their places and their unit kinds; the
numbers below are the catalogue and the unit classes it ships with, sourced in
`godot/data/site_plots.json` (`sources` per plot) and `Farm/Offshore/OffshoreUnit.cs`
from docs/FARM_TYPES_SPEC.md (§2A, §3.2, §4: [O:Rn], [L:Rn] are its offshore
and land references, [GB] a game-balance value).

**The shipped scenarios are unchanged:** `tools/record_golden_traces.sh --base 235ccf2`
gives 27 of 27 traces identical (every day line and every snapshot value);
the only save drift is the additive fields (`SeaSite.Kind`, `PlotId`,
`Offshore`, `AppliedContainmentCredit`, `GameState.SitePlots`),
docs/run/evidence/f2_03_golden_diff.md.

### Plots (`site_plots.json` v1)

| plot | name | kind | centre (x, z) m | real distance | gameplay attributes |
|---|---|---|---|---|---|
| `pad_a` | Storholmen | land pad 640 x 420 m | (-450, -1720) (F2-18 read gate) | 1.8 km | 4 grow-out lots, 4 post-smolt modules, grid 40 MW (1 feed), sensitivity 0.2, not pre-zoned, blasting 120 M NOK [GB] |
| `pad_b` | Skjærvika | land pad 480 x 360 m | (750, -1640) | 1.8 km | 2 lots, grid 25 MW, sensitivity 0.6 [GB, after Verholmen L:G4], blasting 77 M NOK |
| `pad_delta` | Elveøyra | land pad 260 x 180 m | (0, 1450) | 1.5 km | 4 post-smolt modules, river intake (12-35 ppt), pre-zoned, grid 10 MW, blasting 40 M NOK |
| `off_1` | Ytre kyst | offshore slot, OuterCoast | (-450, -2750) | 5 km | Hs100 5.0 m [O:R6], depth 150 m, external lice x 0.12 [GB: 0.01-0.06 adult females, 0 treatments in 2 OF1 cycles, O:R7, O:R8, O:R11] |
| `off_2` | Havkanten | offshore slot, OuterCoastExposed | (850, -3400) | 12 km | Hs100 6.5 m [GB; AOF approved 6.7 m, O:R19], depth 220 m, lice x 0.10 |
| `hav_1` | Havområde | offshore slot, OpenOcean (zone `hav`) | (0, -5200) | 83 km (45 nm, O:R58) | Hs100 13 m [GB, from the SFF design, O:R12, O:R29], depth 300 m, lice x 0.05-0.30 drawn per game |

Static lift on every pad 13.0 m (pad +3.6 m [L:R13] + 8 m water + 1.5 m
freeboard, derived); pumps 5 m³/s a lot [GB]; the deep-intake reach is
computed from the heightfield (`ISitePlotGeometry.IntakeReachM`), never authored.
Overflow plots for migrated saves: 4 reserved positions a kind (land pads
on the mouth's shore, offshore slots in the open far field), neutral attributes
(4 lots and 4 modules; off_1's sea), more generated past them.

### Unit classes

| class | zones | volume | MTB | design Hs | fits | crew | rota | capex (legacy verb) |
|---|---|---|---|---|---|---|---|---|
| OF1-class (SemiSubmersible) | 3 [O:R1, O:R6] | 250,000 m³ [O:R1, O:R6] | 6,240 t (8 x 780 t, O:R4) | 5.5 m [O:R6, O:R15] | off_1 (off_2 with +1.0 m) | 12 [O:R13] | 7 days (9-on/12-off in 3 shifts, O:R3) | 700 M NOK (pre-F2) |
| Havfarm-class (ShipShaped) | 6 [O:R23, O:R25] | 6 x 69,000 m³ | 10,000 t [O:R22, O:R23] | 8.0 m | off_1, off_2 | 12 | 7 days | 900 M NOK (pre-F2) |
| Submersible (legacy) | 3 | as OF1 [GB] | as OF1 [GB] | as OF1 [GB] | off_1 | 12 | 7 days | 1.2 bn NOK (pre-F2) |
| post-smolt module (land) | 2 tanks of 1,600 m³ | 3,200 m³ | 480 t a year, 1.1 M fish at once [GB] | — | pads with post-smolt room, pad_delta first | — | — | pre-F2 facility |
| grow-out lot (land) | — | — | 7,900 t a year [L:R8] | — | pads with lots | — | — | pre-F2 RAS facility |

An offshore unit's water is its area's open sea (climatology, saturation
oxygen) plus its current-fed 1.5 mg/L; its own lice model runs at its slot's
external pressure (x 0.12 at off_1, held at the model's 0.2 floor after the
seasonal factor); the fjord storm wear never reaches it.

### The pre-F2 facilities save, migrated (`save_v8_facilities.json`)

The RAS facility (Small, 5,000 t a year: 1 lot) takes pad_a, the semi-submersible offshore farm becomes
`offshore_1` on off_1 (its 2 stocked pens in zones 1 and 2, its 8,000 t
capacity kept as the unit's MTB, its 6 x 40,000 m³ of pens as 3 zones of
80,000 m³, its 14-day rota 7 days), the post-smolt facility (10 M smolts a year)
becomes `postsmolt_1` on pad_delta with 4 modules (8 tanks), its 2 batches
populations. Fish and kg equal the save's to 1e-9; the 30-day continuation is
bit-identical across processes. The 365-day continuations
(`FarmTypeMoveTests`, osx-arm64):

| variant | engine | cash day 365 (NOK) | fish | RAS kg | post-smolt kg | offshore kg |
|---|---|---|---|---|---|---|
| as-saved/events | pre-F2 (`235ccf2`) | 1,418,552,985 | 340,407 + 281,896 in batches | 267,921 | 9,514,553 (33.8 kg a fish) | 762,750 |
| as-saved/events | F2-03 | 1,447,483,294 | 340,407 + 266,352 in tanks | 267,921 | 2,142,024 (8.0 kg a fish, 167 kg/m³) | 762,750 |
| as-saved/no-events | pre-F2 (`235ccf2`) | 1,430,404,915 | 202,214 + 281,896 in batches | 158,051 | 9,514,553 (33.8 kg a fish) | 450,769 |
| as-saved/no-events | F2-03 | 1,469,614,476 | 202,214 + 179,962 in tanks | 158,051 | 1,447,269 (8.0 kg a fish, 113 kg/m³) | 450,769 |

The sea sites, the RAS tanks and the offshore unit hold the same fish and
kilos to the kilo before and after: the fixture's offshore farm had no zone,
so it already took its area's sea, and its fish now sit in zones of the same
water. What moved is the post-smolt batches (nobody transfers them in this
continuation): they now grow by the TGC law in their tanks' controlled 13 °C
and die by the mortality hazards, where the pre-F2 facility compounded
1.5 % of body weight a day x temperature / 12 with no end (33.8 kg a fish
after a year) and lost 3 % over 180 days as anchor deaths, and paid feed for
it. Cash is 28.9 M NOK (events) and 39.2 M NOK (no events) higher, net of the
unit's 7-day rota (26 more crew changes at 150,000 NOK). A post-smolt left in
its tanks still reaches 8 kg at 113-167 kg/m³: land tanks carry no density
stress until F2-12 (RUN_LOG §8).

## 1n. F2-04: facility finance, the farm-type catalogue, research gates (2026-10-16, branch `wave3/sim-core-3`)

The calibration registry is **v1.1.0** (the F2-04 minor bump). Its new section
`facilities` is assembled from include files (`calibration.json` →
`facilitiesIncludes`, one file per lane); sim·core's is
`godot/data/calibration/facilities_common.json`. Every leaf carries a `[Range]`
and one `[Source]` (dossier keys of docs/research/FARM_TYPES_RESEARCH.md: `L:x`
land, `O:x` offshore, `V:n` the realism review's, `code` the game's own) or
`[GameBalance]` (a reason); `CalibrationGuardTests` checks all 76.

### Depreciation and construction risk

| value | number | provenance |
|---|---|---|
| land site life | 20 years, straight line from commissioning | [GB] spec §3.6; stand-in for skatteloven § 14-43's declining groups |
| offshore unit life | 25 years | [GB] spec §3.6 |
| managed cost overrun (lognormal, truncated at its p99) | median +7 %, p90 +25 % (p10 +2 %, p99 +71 %) | [GB] after O:R4 (the first OF1-class unit: 720 M + 157 M = +22 %) and L:C1 |
| schedule slip (lognormal, truncated at its p99) | median +5 %, p90 +35 % (p10 +0.7 %, p99 +171 %) | [GB]; L:A10 slipped, O:R1 on plan |
| turnkey premium | +12 % up front, no overrun | [GB] spec §3.9 |

Depreciation is booked only for F2 units built in the game (the legacy build
verbs register them at build day + construction days; construction projects
at completion). Migrated pre-F2 units and every fjord capex stay off the
register (spec §8). It is a non-cash P&L line: the corporate tax base falls by
it (−22 % × depreciation of corporate tax), the cash reconciliation does not
move (730 days × seeds 1/7/13 with a post-smolt site, a RAS and an OF1-class
unit: depreciation = Σ capex / life per day to 1e-9 NOK, cash = booked flows to
1e-6 NOK), and the resource-rent base never sees it.

### The rent ring-fence (spec §3.6)

The rent base is the liable harvest at the norm price less the sea-phase costs;
costs of units the rent does not reach (every land site; an offshore unit on
licences outside it, F2-09) are booked like any cost and left out of it. Twin
games (`ressursrente`, events off, 300 days; one adds a commissioned Medium RAS
stocked with 200,000 smolts): the land site costs 65.3 / 65.6 / 65.7 M NOK
(seeds 1 / 7 / 13: opex, feed, smolts), every krone ring-fenced, and the rent
bases are equal to 1e-20 NOK (32.28 / 18.61 / 20.03 M) while the mixed company's
pre-tax income is that much lower. The real tax is a cash-flow tax that expenses
sea-phase investments directly (Prop. 78 LS (2022–2023)); the game's rent base
deducts no capex at all yet, sea or land (left for F2-09 / S4-09).

### The farm-type catalogue's reference cards (spec §2A-2C)

Computed by `Farm/FarmTypeCatalog.cs` from the registry; capacity cost per
tonne of yearly capacity including the licence at 1.2 t HOG per t MTB a year
[GB]; the bad case is the unit's capex at the overrun's p90, the build at the
slip's p90, the survival in a bad cycle. After F2-17 the probe's medians replace them.

| variant | capex NOK/t/yr (bad) | opex NOK/kg HOG | survival (bad) | ext. lice | kWh/kg | build days (bad) | capacity t/yr | sources (capex) |
|---|---|---|---|---|---|---|---|---|
| Åpen merd | 255,769 | 77.9 | 0.815 (0.741) | 1.0 | 0.07 | 0 | 3,744 | code, V:1, GB |
| Semi-lukket / lukket merd | 281,437 (288,255) | 73.3 | 0.979 (0.92) | 0.15 | 3.87 | 60 (81) | 1,833 | O:R44, V:1, code, GB |
| Postsmoltmodul | 229,167 (286,458) | — (no published figure) | 0.97 (0.90) | 0 | 4.4 | 593 (801) | 480 (live) | GB |
| Landanlegg (hybrid) | 262,000 (327,500) | 70.7 | 0.97 (0.90) | 0 | 6.55 | 910 (1,229) | 7,900 | L:R1, GB |
| Landanlegg (RAS), stage 3 | 321,000 (401,250) | 77.8 | 0.98 (0.88) | 0 | 7.0 | 973 (1,314) | 4,316 | L:C1, GB |
| Havmerd (OF1-class) | 131,869 (161,149) | 101.3 | 0.93 (0.81) | 0.12 | — | 670 (905) | 7,488 | O:R4, code, V:2, GB |
| Skipsmerd (Havfarm-class), stage 3 | 98,288 (119,121) | 101.3 | 0.972 (0.81) | 0.30 | — | 880 (1,188) | 12,000 | O:R26, O:R22, O:R23, V:2, GB |

Gates (FR3: every gate, money included): Åpen merd at the start (6 M); the
closed pen after the first harvest (50 M a pen); the post-smolt module after
`land-based-ras` and the first harvest (110 M); the hybrid land farm after
`land-based-ras`, an operating post-smolt module and a zoned pad (the pilot
lot, 0.9 bn [GB]); OF1-class after `deep-sea-farming`, the first harvest, 365
escape-free days and a development licence (F2-09), 35 % of 877 M as a
consortium; RAS and Havfarm-class after one cycle of the company's own HFS /
OF1-class unit.

### Research narrowed to its unit kinds (spec §3.1 decision 0)

| node | effects | before F2-04 | from F2-04 |
|---|---|---|---|
| `land-based-ras` | mortality −5 %, disease −14 % | every fish (fjord pens included) | land tanks only; opens the post-smolt module and the hybrid land farm |
| `deep-sea-farming` | lice settlement −10 %, disease −6 % | every fish and every sea site | offshore units only; opens the OF1-class unit |

A fjord-only game that researches neither is bit-identical (24 of 27 golden
traces). po3_red_sone starts with `deep-sea-farming`: its fjord sites lose the
10 % lice blocking and 6 % disease resistance (final cash −12.11 / −15.25 /
−13.77 M → −11.91 / −15.11 / −13.81 M on seeds 1 / 7 / 13, live biomass 216 /
119 / 244 t → 213 / 112 / 243 t; docs/run/evidence/f2_04_golden_diff.md).

### The pre-F2 facilities save, F2-03 → F2-04 (`FarmTypeMoveTests`, 365 days)

| variant | cash day 365 (NOK) | fish | RAS kg | post-smolt kg | offshore kg |
|---|---|---|---|---|---|
| as-saved/events F2-03 | 1,447,483,294 | 606,759 | 267,921 | 2,142,024 | 762,750 |
| as-saved/events F2-04 | 1,447,567,426 | 605,878 | 267,815 | 2,137,987 | 762,750 |
| as-saved/no-events F2-03 | 1,469,614,476 | 382,176 | 158,051 | 1,447,269 | 450,769 |
| as-saved/no-events F2-04 | 1,470,441,983 | 374,745 | 152,966 | 1,412,511 | 450,769 |

The save holds `deep-sea-farming`: its disease resistance no longer reaches
the land and post-smolt tanks or the fjord pens (fewer fish there), the
offshore unit keeps it (unchanged to the kilo). The year end's rent base now
leaves the land sites' costs out, but stays under the 70 M NOK basic deduction
either way, so no cash moves for it: with the ring-fence alone (before the
narrowing) cash, fish and kilos were F2-03's to the krone and the kilo, and only
the saved fields changed the pin.

## 1o. F2-05: the Driftsplan and the notification budget (2026-10-16, branch `wave3/sim-core-4`)

A standing order issues the player's own verb at the player's price: it adds
no cost, no discount and no new effect, so its balance is the verb's. The
defaults are the settings a new order starts with; each carries its source on
the setting (`DriftsplanSetting.Source`, shown in the card's tooltip).
Neither fjord policy is on by default: a fjord game plays as before until the
player sets one (the farm-type lanes add their units' defaults, spec §1
pillar 5).

| policy · setting | default (range) | unit | provenance |
|---|---|---|---|
| net care (`net_cleaning`) · interval | 14 (7–60) | days | Elsheshtawy et al. 2023, *Frontiers in Aquaculture* 2:1125595: commercial farms in western Norway cleaned their nets every second week, May–November 2019; Bloecher and Floerl 2021 "every two weeks or more" at high-fouling sites. The job is the ROV wellboat (`RequestWellboat` "rov": 75,000 NOK, nets +15 %) |
| delouse on the count (`delouse_on_count`) · share of the limit | 0.8 (0.5–1.0) | share | [GB] acts on the first weekly count over 0.8 × the lakselusforskriften § 8 limit (0.5 adult females, 0.2 in the area's spring weeks), a week before a second count over the limit brings Mattilsynet's order; 0.8 × 0.5 = the golden player's 0.4 (F2-01). Counts: § 6, weekly at ≥ 4 °C |
| · fewest days between treatments | 14 (7–60) | days | [GB] the golden player's interval; each handling stresses the fish |
| · method | hydrolicer (hydrolicer, thermolicer, freshwater, peroxide) | — | [GB] the golden player's; mechanical delousing works at any sea temperature |

What a plan changes in a year of po3_red_sone (events on, 40 M NOK loan in
every game, a lice wave of +0.5 adult females on both sites every 60 days;
`DriftsplanTests.APlan_ChangesThePlayersGame`):

| seed | net integrity mean / worst, no plan | with net care | ROV jobs (hire by day 300) | adult females, no plan | with the standing order | treatments | Mattilsynet treatment orders |
|---|---|---|---|---|---|---|---|
| 3 | 99.32 % / 95.6 % | 99.62 % / 95.8 % | 27 (1,650,000 NOK) | 0.441 | 0.123 | 12 | 9 → 0 |
| 11 | 98.04 % / 91.8 % | 99.43 % / 94.3 % | 27 (1,650,000 NOK) | 0.453 | 0.124 | 12 | 9 → 0 |

The nets' routine upkeep keeps them near 100 % on calm days, so net care pays
mostly after storms (seed 11's winter); its price is the wellboat's (≈ 2 M NOK
a year a site at 14 days), a balance for the player to judge against the
storm escapes it guards against. The standing order keeps the lice at a third
of the unplanned level and never lets an order come.

**The notification budget** (spec §6.6, measured in F2-17 with
`NotificationMeter`): a policy-competent player sees at most **2 warnings per
unit per in-game month** and at most **1 critical per unit per in-game year**
(`NotificationMeter.WarningBudgetPerUnitMonth`, `CriticalBudgetPerUnitYear`;
owner brief "alarms are rare and mean something"). The meter counts what the
bus delivered (new and escalated), never a repeat it absorbed, and never
information.
## 1p. F2-12: land water, energy and tanks, standalone (2026-10-15, branch `wave3/sim-lanes-1`)

F2-12 stage 1 builds the land unit's physics as pure models in new files
(`Farm/Land/*`, `Calibration/LandCalibration.cs`, draft
`godot/data/calibration/facilities_land.json`); nothing is wired into the
engine tick, the bridge, `GameState` or the registry yet (F2-02..F2-04 are in
flight), so the shipped game and the 27 golden traces are unchanged (27 of 27
identical over 731 days). Every number below comes from `LandLotProjector`, a
pure day-by-day projection of one lot: the biology grows each tank on the S2-03
growth law at the tank water the previous day's land tick left (continuous
light, appetite from CO2 and TAN, the land density brake), then
`LandLotDaySolver` solves the lot's flows, heat balance, water and energy on the
day's feed, in the engine's order.

**Harness.** pad_a of the procedural fjord (default terrain, seed 42): 25 m
water at the quay (1 m), 95 m water 140 m out (`IntakeReachM`, F2-19); 13 m
static lift, 5.0 m³/s of intake pumps per lot, UV on the intake. The 25 m intake
draws Salmon Evolution's area's water (PO5 climatology, S2-02), the 95 m intake
10 °C [V:10]. One HFS lot: 12 × Ø28 m × 8 m (4,926 m³) + 2 × Ø16 m (1,608 m³) =
62,329 m³. Every tank runs Salmon Evolution's new plan ([L:R1]: 400 g → 5.2 kg LW),
stocked to a projected peak of 80 kg/m³ (82,736 smolts would fill a grow-out tank
to 85), 97 % survival a cycle ([L:R2]), 7 days fallow, first stockings staggered
over the rotation; 420 days of warm-up, then one measured year (2028). The plan's
cycle at 12 °C and 24 h light is 240 days (8 months; [L:R1] 8-9). Energy and
oxygen are per kg of live-weight biomass growth (harvest + Δ standing − smolts
stocked), the basis of [L:R2]'s "kWh/kg biomass (LW)" and "oxygen consumption
(biomass growth (LW))".

### Acceptance (spec §7.1 F2-12, 1-5)

| # | Criterion | Band | Measured | Test |
|---|---|---|---|---|
| 1 | HFS r 0.65, heat recovery, pad_a, annual [L:R2] | 6.0-7.5 kWh/kg LW | **6.40** | `LandEnergyModelTests.F2_12_Acceptance1_HfsR065WithHeatRecovery_Uses6To75KWhPerKgLw` |
| 1 | r 0, intake at the setpoint, lift ≤ 2 m [L:A1, L:A3] | 0.6-1.5 | **1.08** (2 m lift, no UV, pumps sized for the loop) | `..._FlowThroughAtTheSetpoint_LiftUnder2M_Uses06To15KWhPerKgLw` |
| 1 | RAS hall r 0.97 [L:R7, L:S3, L:S4] | 4-10 | **6.33** (6.70 without heat recovery) | `..._RasHallAtR097_Uses4To10KWhPerKgLw` |
| 2 | LOX at the Salmon Evolution-like setup [L:R2] 0.55 | 0.45-0.65 kg O2/kg LW | **0.479** | `..._LoxAtTheSalmonEvolutionSetup_Is045To065KgPerKgLw` |
| 2 | LOX from r 0 to r 0.9 at equal density | rises < 15 % | **−2.4 %** (14.84 → 14.48 t/day; held to ±15 %) | `LandWaterModelTests.F2_12_Acceptance2_LoxMovesUnder15Percent_FromR0ToR09_AtEqualDensity` |
| 3 | CO2 at 60 kg/m³, 12 °C, r 0.65, default pumps | 10-15 mg/L | **13.41** | `..._Co2At60KgM3_12C_R065_DefaultPumps_Is10To15` |
| 3 | CO2 above 70-80 kg/m³ | > 15 | **15.60** at 70, **17.80** at 80 | `..._Co2Exceeds15_At70And80KgM3` |
| 3 | CO2 above r 0.8 | > 15 | **15.81** at r 0.8, 16.89 at 0.85 | `..._Co2Exceeds15_AboveR08_AndRisesWithReusePastTheStrippers` |
| 4 | interior profit-optimal r (0.3-0.8) in ≥ 2 of 4 seasons | ≥ 2 | **4 of 4** (r 0.70, 0.70, 0.65, 0.70) | `LandVannstrategiSweepTests.F2_12_Acceptance4_AnInteriorReuseWins_InAtLeastTwoOfFourSeasons` |
| 4 | the best probe pins neither r nor w at a bound | — | r **0.70**, intakes blended toward the setpoint (w interior on 116 of 365 days, mean 0.69) | `..._TheBestProbe_PinsNeitherReuseNorBlendAtABound` |
| 5 | stocking with a projected peak above 85 kg/m³ is refused | `density_exceeded` | room + 1 smolt refused, room allowed; a tank stocked to the room peaks at ≤ 85 on every day of a year | `LandStockingPlannerTests` |

Criterion 3 is measured on every tank of a full lot at the density, holding the
plan's fish of the weight at which the plan stands at 60 kg/m³ (3.92 kg, eating
0.771 % of their weight a day at 12 °C), i.e. denser stocking of the same fish.

### Where the energy goes (kWh per kg LW growth, pad_a, PO5, one year)

| Setup | total | intake | loop | treatment | UV | heating | cooling | base | LOX kg/kg | growth t | index kg/m³/yr | eFCR | CO2 mean / max | tank °C |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| HFS r 0.65, heat recovery (anchor) | 6.40 | 1.13 | 0.64 | 0.86 | 0.60 | 2.82 | 0.00 | 0.33 | 0.479 | 6,640 | 115 | 1.026 | 10.2 / 12.6 | 12.00 |
| HFS r 0.65, no heat recovery | 10.52 | 1.16 | 0.66 | 0.88 | 0.62 | 6.86 | 0.00 | 0.34 | 0.475 | 6,478 | 112 | 1.027 | 10.0 / 12.6 | 11.71 |
| HFS r 0.65, heat recovery, setpoint 13 °C | 7.48 | 1.05 | 0.59 | 0.80 | 0.56 | 4.19 | 0.00 | 0.30 | 0.494 | 7,185 | 124 | 1.026 | 10.9 / 13.3 | 13.00 |
| HFS pilot lot (4 + 1 tanks) | 6.35 | 1.12 | 0.64 | 0.86 | 0.60 | 2.81 | 0.00 | 0.32 | 0.480 | 2,297 | 116 | 1.028 | 10.2 / 12.5 | 12.00 |
| flow-through r 0, 12 °C intake, 2 m lift, no UV (anchor) | 1.08 | 0.75 | 0 | 0 | 0 | 0 | 0 | 0.33 | 0.482 | 6,697 | 116 | 1.027 | 9.7 / 12.0 | 12.02 |
| the same with UV (a new plant must disinfect, [L:L2]) | 2.79 | 0.75 | 0 | 0 | 1.71 | 0 | 0 | 0.33 | 0.482 | 6,712 | 116 | 1.028 | 9.7 / 12.0 | 12.04 |
| HFS r 0 on pad_a's pumps | 8.05 | 1.85 | 0 | 0 | 0.98 | 4.70 | 0 | 0.52 | 0.551 | 4,172 | 72 | 1.117 | 24.6 / 33.1 | 12.00 |
| RAS hall r 0.97, heat recovery (anchor) | 6.33 | 0.12 | 1.21 | 4.05 | 0.07 | 0.17 | 0 | 0.71 | 0.481 | 4,106 | 106 | 1.008 | 8.7 / 11.6 | 12.00 |
| RAS hall r 0.97, no heat recovery | 6.70 | 0.12 | 1.21 | 4.05 | 0.07 | 0.54 | 0 | 0.71 | 0.481 | 4,106 | 106 | 1.008 | 8.7 / 11.6 | 12.00 |
| post-smolt module r 0.97 (80 g → 450 g) | 3.61 | 0.07 | 0.69 | 2.30 | 0.04 | 0.07 | 0 | 0.44 | 0.440 | 822 | 310 | 0.877 | 11.2 / 14.2 | 12.00 |

Heating is the HFS's largest part (44 %) and the lever heat recovery pulls; the
RAS hall's is treatment of its reused water (64 %). The post-smolt module lands
in the 3.4-5.4 kWh/kg of [L:T1, L:S3] without tuning (F2-13 owns it).

**By production area** (HFS anchor setup; the 25 m intake draws each area's water):

| Area | kWh/kg, heat recovery | of which heating | kWh/kg, none |
|---|---|---|---|
| PO1 | 6.20 | 2.63 | 9.97 |
| PO2 | 6.35 | 2.78 | 10.39 |
| PO3 | 6.24 | 2.67 | 10.14 |
| PO4 | 6.31 | 2.73 | 10.29 |
| PO5 | 6.40 | 2.82 | 10.52 |
| PO6 | 6.43 | 2.86 | 10.61 |
| PO7 | 6.75 | 3.18 | 11.41 |
| PO8 | 6.84 | 3.27 | 11.63 |
| PO9 | 7.16 | 3.58 | 12.44 |
| PO10 | 7.12 | 3.55 | 12.34 |
| PO11 | 7.73 | 4.15 | 13.97 |
| PO12 | 7.70 | 4.13 | 13.92 |
| PO13 | 7.91 | 4.34 | 14.41 |

The 95 m water is 10 °C everywhere [V:10] (no northern deep-water series); a
northern pad pays for colder shallow water only through the blend.

**Reuse on the HFS anchor (annual).** Below r 0.66 the intake pumps limit the
new water (5.0 m³/s), so the circulation falls and CO2 rises; above the
strippers' design (0.65) reused water returns unstripped, and with no biofilter
the ammonia leaves only with the new water ([L:R7]).

| r | kWh/kg | LOX kg/kg | growth t | CO2 mean / max mg/L | mean appetite | pump-limited days |
|---|---|---|---|---|---|---|
| 0.00 | 8.05 | 0.551 | 4,172 | 24.6 / 33.1 | 0.582 | 365 |
| 0.30 | 6.50 | 0.498 | 5,509 | 16.1 / 24.4 | 0.775 | 365 |
| 0.50 | 6.23 | 0.487 | 6,228 | 12.6 / 15.9 | 0.899 | 365 |
| 0.60 | 6.35 | 0.482 | 6,532 | 11.0 / 14.0 | 0.960 | 365 |
| 0.65 | 6.40 | 0.479 | 6,640 | 10.2 / 12.6 | 0.987 | 225 |
| 0.70 | 5.95 | 0.481 | 6,558 | 10.5 / 13.5 | 0.966 | 0 |
| 0.75 | 5.58 | 0.482 | 6,344 | 10.6 / 14.3 | 0.920 | 0 |
| 0.80 | 5.34 | 0.483 | 5,941 | 10.6 / 15.2 | 0.850 | 0 |
| 0.85 | 5.21 | 0.485 | 5,379 | 10.5 / 16.2 | 0.748 | 0 |
| 0.90 | 5.89 | 0.525 | 4,162 | 12.0 / 17.4 | 0.576 | 0 |

**A full lot at equal density** (60 kg/m³ of the plan's 3.92 kg fish, 12 °C, pad_a's pumps):

| r | CO2 mg/L | TAN mg/L | LOX t/day | pumps limit |
|---|---|---|---|---|
| 0.00 | 34.58 | 2.00 | 14.84 | yes |
| 0.30 | 25.34 | 2.00 | 14.69 | yes |
| 0.50 | 18.69 | 2.00 | 14.50 | yes |
| 0.60 | 15.20 | 2.00 | 14.33 | yes |
| 0.65 | 13.41 | 2.00 | 14.20 | yes |
| 0.70 | 14.02 | 2.31 | 14.25 | no |
| 0.75 | 14.86 | 2.78 | 14.31 | no |
| 0.80 | 15.81 | 3.47 | 14.37 | no |
| 0.85 | 16.89 | 4.63 | 14.43 | no |
| 0.90 | 18.14 | 6.94 | 14.48 | no |

CO2 at r 0.65 by density: 11.21 (50), 13.41 (60), 14.51 (65), 15.60 (70), 16.70
(75), 17.80 (80), 18.90 (85). At pad_a's pumps the HFS runs at exactly the 2 mg/L
TAN limit with 60 kg/m³ of 3.9 kg fish, the "reuse until TAN" rule of [L:R7].

**Profit sweep (criterion 4).** Profit = LW growth × 70 NOK/kg HOG × 0.83 − feed
× 22.30 NOK/kg − kWh × 0.80 NOK − LOX × 4.0 NOK, over r 0-0.95 (step 0.05) ×
w 0-1 (step 0.1), each season (91 days from 1 Jan, 1 Apr, 1 Jul, 1 Oct 2028)
after a warm-up on the default strategy:

| Season | best r | best fixed w | profit (M NOK) |
|---|---|---|---|
| winter | 0.70 | 1.0 | 45.0 |
| spring | 0.70 | 1.0 | 45.0 |
| summer | 0.65 | 0.5 | 51.2 |
| autumn | 0.70 | 1.0 | 45.0 |
| year, blended toward the setpoint | 0.70 | (daily) | 187.3 |
| year, best fixed blend | 0.70 | 1.0 | 179.4 |

The tanks sit at the 12 °C setpoint all year (the heat pumps never run out of
capacity with heat recovery), so the fish grow alike in every season and only
the heating differs; with the 95 m water warmer than the 25 m water from
November to May, a fixed blend takes all deep water except in summer. Blending
toward the setpoint every day beats every fixed blend by 7.9 M NOK a year.

### Calibration chosen here (against the spec's draft, §3.9)

| Key | Spec draft | Now | Why |
|---|---|---|---|
| loop flow | 20 vol/day [GB] | 20 | [L:R5] DESMI: 18-22 vol/day at peak biomass (derived) |
| pump capacity | 5.0 m³/s [GB] | 5.0 | [L:R5] 16,000 L/s over 48 tanks: 5.1 per lot (derived) |
| heat-pump size | 8 MW [GB] | 8 MW **electrical input** | [V:11] six 5-6 MW units + exchangers, > 100 MW heat for phase 2; 8 MW of heat could lift 5 m³/s by only 0.4 K |
| HFS biofilter | maturing in 30 d | none | [L:R7], [L:R2]: HFS strips CO2 and oxygenates; reuse stops at the TAN limit |
| stripper design reuse | — | 0.65 HFS, 0.97 RAS/post-smolt | [L:R2]: higher reuse needs more treatment; the strippers are sized for the sweet spot |
| outlet DO | 7.0 mg/L | 8.0 (93 % sat.) | at 7.0 the new water and strippers supply 27 % of the fish's O2 and LOX reads 0.37 (fails 0.45); Remen et al. 2016: full feed intake needs 42-76 % sat. |
| injection efficiency | 0.75 | 0.70 | 0.75 gives 0.447 (fails 0.45) |
| heat per m³·K | 1.11 kWh | 1.14 | seawater ρc_p = 4.10 MJ/(m³·K) (TEOS-10) |
| base load | 2,000 kWh/day | 6,000 (HFS), 8,000 (RAS), 1,000 (post-smolt) | an itemised 250 kW for a 12-tank lot (feeding, LED light, sludge, buildings) |
| UV | — | 0.026 kWh/m³ | US DOE/LBNL Wastewater Tipsheet #6 (2021): low-pressure UV 100-250 kWh/MG |
| cooling band | — | 0.5 K | no chiller runs to take 0.02 K of fish heat out of a flow-through loop |
| fish O2 | 0.37 kg/kg feed | 0.37 | Grøttum & Sigholt 1998 respirometry (a 2 kg salmon at 12 °C: ~2.2 g O2/kg/day; a 2026 *Scientific Reports* refit raises the baseline 29 %) over a 0.8-0.9 %/day ration: ~0.3-0.4 kg O2/kg feed (derived) |

### Mutation checks (key tests fail when the model is broken)

| Mutation | Tests that fail |
|---|---|
| strippers take all reused water (no design cap) | `Flows_WithRoomInThePumps...`, acceptance 3 above r 0.8 |
| heat recovery ignored (plain COP) | `HeatBalance_WithinCapacity...`, acceptance 1 HFS (6.40 → 12+), `HeatRecovery_CutsTheHfsEnergy` |
| stocking allowed up to 1.1 × the limit | acceptance 5, `PostSmoltTanks_AreHeldTo80KgM3` |
| fish O2 0.37 → 0.30 kg/kg feed | acceptance 2 (LOX), acceptance 3 (70-80 kg/m³, r 0.8), JSON parity |
| water quality never touches appetite | `TankAppetite...`, acceptance 4 (the sweep then pins r at 0.95 in every season) |

### Findings (RUN_LOG §8)

- **Production index.** The plan stocks each tank once per cycle, so the lot's mean
  density is 35 kg/m³ and it grows 115 kg/m³/yr against [L:R6]'s 131-140 (revised)
  and the 150 of the 7,900 t nameplate; 6,640 t LW growth ≈ 5,850 t HOG, close to
  Salmon Evolution's 2026 guidance (≈ 6,000 t, [V:17]). Energy scales with the
  stocked tanks' volume (spec §3.3: the loop flow is tank volumes a day), so a
  cohort plan with pre-grow-out transfers (F2-15) that raises the index lowers
  kWh/kg: re-measure criterion 1 then.
- **Fixed blend.** On pad_a a fixed annual w pins at 1.0 (all deep water): the 95 m
  water is warmer than the 25 m water eight months a year and its line is 140 m.
  Daily blending toward the setpoint is the best strategy; the Vannstrategi
  presets (F2-32) should offer it.
- **LOX and reuse.** LOX falls 2.4 % from r 0 to r 0.9 instead of rising: the
  strippers bring the reused water back toward air saturation.

## 1q. F2-06: the offshore sea, its honest forecast and the operability windows, standalone (2026-10-16, branch `wave3/sim-lanes-2`)

F2-06 stage 2 builds the offshore sea as standalone components in new files
(`Environment/{OffshoreMetocean,MetoceanForecast,MetoceanMath,OperabilityWindows}.cs`,
`Calibration/OffshoreCalibration.cs`, draft `godot/data/calibration/facilities_offshore.json`)
plus one pure addition to `WeatherSystem` (`StateDistribution(k)`, which
`StormRiskNext3Days` now delegates to, and `WindSpeedBelowProbability`). Nothing is
wired into the tick, the bridge or `GameState` (F2-02/F2-03 own `OffshoreTickSystem`,
`OffshoreHooks.OperationPossible` and the bridge's `GetSeaStateForecast`), so the
shipped game and the golden traces are unchanged. Every number below is measured on
the standalone component the way the engine will run it: one `SimRng` per seed, the
fjord weather advancing first on its own stream, then the sea ticking the same day on
`offshore_metocean`. The acceptance sample is seeds 1-20 × 50 years (365,000 days);
the fit runs on the disjoint seeds 1001-1100 × 50 years.

**The model** (`OffshoreMetocean.TickDay`, 8 uniforms a day, always, inverse CDF):
- u0: the base sea, an AR(1) in a sea transform T: T(B) = μ_month + Δ_weather + X,
  X_t = 0.75 X_{t−1} + 0.30 Φ⁻¹(u0). T is ln h up to 1 m and (h^0.4 − 1)/0.4 above
  (`MetoceanMath.UpperBoxCox`): a lognormal sea below 1 m with no floor, a thinner tail above.
- u1-u6: a storm-swell event is created at the month's rate (0.02-0.10 a day, 22.1 a
  year), arrives 4-7 days later, holds its peak 1-4 days between a half-height ramp day
  on each side; peak from the month's median (2.2 m in January, shaped by NORA10's
  monthly 1-year extremes: 0.96 m in July) with a 0.20 ln spread; Tp 9-14 s; u6 is the
  standard normal error of the forecast's estimate of the peak.
- u7: visibility (low-visibility days 3-6 % a month).
- A plot's Hs = its scale × √(B² + 0.34² + Σ swell²): the day's wind sea, the background
  swell and the storm swell add in energy. A day's Hs is the day's highest 3-hour sea
  state, so the annual maxima give the design 100-year level and an operation booked for
  the day must clear it.

### Sources

| Key | What it gives | Used for |
|---|---|---|
| NORCE 2020 (Ø. Johnsen, *MariCulture Smart Fishfarm, Metocean Design Basis, Haltenbanken II*, report 103211, 12.11.2020; NORA10 1958-2019 at 63.86 °N 6.46 °E, published with Fiskeridirektoratet's hearing on the Norskehavet site: https://fiskeridir.no/hoeringer/horing-om-klarering-av-lokalitet-for-akvakultur-i-norskehavet) | Table 3.2 monthly mean Hs 3.7 3.6 3.2 2.4 1.8 1.7 1.5 1.6 2.3 2.9 3.2 3.7 m (year 2.6), non-exceedance per 0.5 m (0.18 % below 0.5 m, 7.15 % below 1.0 m, 32.6 % above 3 m); Table 3.4 monthly 1-year extremes 10.3 … 4.5 (July) … 10.2 m, annual 1/10/100-year 11.6/13.9/16.2 m, 3-p Weibull locations 0.68-1.05 m; Table 3.7 Tp of 1-2 m seas 8.4-9.5 s | the monthly shape of the base sea, the swell's seasonal medians, the 100-year/annual-maximum ratio, the calm end (background swell), hav_1's check |
| [O:R17] AOF summary report 1, Fiskeridirektoratet 2024 | site mean Hs 0.9 m, maximum 4.15 m (generation 1); barge inaccessible 92 days in ~25 months, 62 % in January-March | off_1's annual mean, acceptance 2, the winter window check |
| [O:R6] NTNU thesis on OF1 (2018) | OF1's 100-year design Hs 5 m at Frohavet | off_1 Hs100 |
| [O:R19] FFE 2026 | AOF approved for Hs 6.7 m | off_2 Hs100 6.5 m [GB] |
| [O:R37] Menon 131/2023 | crowding only below Hs 2 m; wellboats operate offshore to about 5 m | the handling and alongside limits |
| ICOADS 1950-2007, "Marine fog over northern Europe", AMS 97th Annual Meeting 2017 (https://ams.confex.com/ams/97Annual/webprogram/Paper307556.html) | north of 62 °N fog in < 3 % of ship observations in June-August, < 2 % the rest of the year | the low-visibility day shares (a day counts any low hour) |

### Acceptance (spec §7 F2-06, 1-9)

| # | Criterion | Band | Measured | Test |
|---|---|---|---|---|
| 1 | off_1 annual mean Hs | 0.8-1.1 m | **1.057** (the day's peak; [O:R17]'s 0.9 m is a time mean, ~10 % below) | `OffshoreMetoceanTests.Acceptance1_…` |
| 1 | off_1 median annual maximum | 3.5-4.8 m | **3.776** | same |
| 2 | off_1 days above 2.0 m, year / January-March | 8-14 % / 20-35 % | **11.8 % / 22.9 %** | `Acceptance2_…` |
| 3 | empirical 100-year level (0.99 quantile of 1,000 annual maxima) | Hs100 ± 10 % | off_1 **4.954** (5.0), off_2 **6.443** (6.5), hav_1 **12.886** (13): −0.9 % | `Acceptance3_…` |
| 4 | every swell event peaking ≥ 3 m at off_1 is in the forecast ≥ 4 days before onset | all | **707 of 707** (timing exact; its p90 on the first plateau day is above the forecast without it) | `Acceptance4_…` |
| 5 | realized frequency within ± 0.05 of the forecast in every decile at leads 1, 3, 5, 7, 10 (limits 2.0, 2.5, 3.0 m at off_1) | ± 0.05 | worst **0.021** (lead 10, decile 2: 0.146 forecast, 0.125 realized, 1,147 forecasts); PIT of the whole distribution 9.91-10.11 % per tenth at every lead (364,800 days) | `MetoceanForecastReliabilityTests` |
| 6 | 1,000 forecast polls leave every stream position unchanged | 0 moves | **0** (and the sea's saved state unchanged) | `Acceptance6_…` |
| 7 | exactly 8 uniforms a day | 8 | **8** on each of 365,000 days, none from the weather stream | `Acceptance7_…` |
| 8 | the fjord weather series for seeds 1-10 is unchanged | bit-identical | FNV-1a fingerprints of 10 years of state, temperature, wind, the three offsets and the 3-day storm risk, × default/harsh_storms/calm_weather: **30 of 30** equal to the pre-F2-06 recording (re-run with the pre-F2-06 algorithms in place: 3/3 pass) | `WeatherSeriesFingerprintTests` |
| 9 | a save mid-event is bit-identical | bit-identical | saved on a plateau day with an event pending: 500 more days, the forecast and the stream position bit for bit | `Acceptance9_…` |

### The climate the plots get (seeds 1-20 × 50 years, the day's peak Hs)

| Plot | Scale | Mean | p10 / p50 / p90 / p99 | Annual max p10 / p50 / p90 | > 2 m | > 2.5 m | > 3 m | > 5 m |
|---|---|---|---|---|---|---|---|---|
| off_1 "Ytre kyst" | 1.058 | 1.057 | 0.44 / 0.79 / 2.13 / 3.33 | 3.29 / 3.78 / 4.36 | 11.8 % | 5.8 % | 2.2 % | 0.0 % |
| off_2 "Havkanten" | 1.376 | 1.375 | 0.57 / 1.03 / 2.77 / 4.33 | 4.28 / 4.91 / 5.68 | 20.4 % | 13.0 % | 7.9 % | 0.24 % |
| hav_1 "Havområde" | 2.752 | 2.750 | 1.13 / 2.05 / 5.54 / 8.66 | 8.56 / 9.82 / 11.35 | 51.4 % | 40.4 % | 32.3 % | 13.0 % |

off_1 by month (mean, days above 2 m, above 3 m): Jan 1.49 / 26.1 % / 6.8 %, Feb 1.44 /
24.3 % / 5.5 %, Mar 1.29 / 18.5 % / 2.6 %, Apr 0.97 / 7.3 % / 0.4 %, May 0.73 / 1.5 %,
Jun 0.68 / 0.5 %, Jul 0.60 / 0.1 %, Aug 0.65 / 0.2 %, Sep 0.92 / 5.2 % / 0.2 %, Oct
1.17 / 13.0 % / 0.9 %, Nov 1.29 / 19.4 % / 3.6 %, Dec 1.49 / 26.0 % / 6.6 %. The monthly
means follow NORA10's shape within 1 % (`Off1_MonthlyMeans_FollowTheNora10Shape`).
Lag autocorrelation of ln Hs: 0.80 (1 day), 0.59 (2), 0.45 (3), 0.30 (5).

hav_1 is scaled only to the spec's 100-year sea of 13 m, yet it reproduces the open
Norwegian Sea at Haltenbanken: mean 2.75 m against 2.6 m, 32.3 % of days above 3 m
against 32.6 % of 3-hour states (`Hav1_MatchesTheOpenSeaAtHaltenbanken`). Its 100-year
sea stays the spec's 13 m (NORA10 gives 16.2 m at Haltenbanken; the spec infers 13 m
from the open-sea design, [O:R12], [O:R29]).

The calm end (`CalmSea_FollowsNora10_WithNoPileUp`): no day below 0.192 × the annual
mean (NORA10, 3-hour: 0.18 %), 4.9 % below 0.385 × (7.15 %), July 14.3 % below 0.667 ×
its mean (19.8 %); a day's peak is never below its 3-hour values, so these are upper
bounds, and the lowest 0.1 % of days spread over 0.341-0.349 m (no pile-up).

Off_1 winters (October-March, 980 winters; `Off1Winters_HandlingWindowClimatology`):
median 38 days above the 2.0 m handling limit (p10 27, p90 51; AOF's barge: ~37-40 a
winter), 28 of them in spells of 3+ days; the longest spell without a handling day lasts
median 7 days, p90 10; 21 days or more in 0.1 % of winters; above the offshore
wellboat's 2.5 m: median 20 days.

### Calibration chosen here (against the spec's draft, §3.9)

| Key | Spec draft | Now | Why |
|---|---|---|---|
| base distribution | lognormal (ln Hs) | ln below 1 m, Box-Cox λ = 0.4 above | the lognormal's 100-year level was 1.85 × the median annual maximum and failed acceptance 1 and 3; NORA10's ratio is ~1.35 (16.2 m against an annual maximum of ~12); measured now 4.954 / 3.776 = 1.31 |
| floor | — | none; background swell 0.34 m in energy | a Box-Cox power over the whole range reaches 0 m and needed a 0.1 m floor that bound on 3.8 % of days (a clamp); the lognormal lower piece put 2.4 % of days below 0.19 × the mean and 18.8 % below 0.385 × (NORA10: 0.18 % and 7.15 %); the background swell is NORA10's 3-p Weibull location (0.68-1.05 m in every month, 0.26-0.40 × the mean) |
| μ_month (off_1) | 0.20 … −0.65 … 0.20 (ln, scale 1.0) | −0.524 … −1.281 … −0.492 (transform units, reference sea) | fitted on seeds 1001-1100 so each month's mean is 1.0 m × NORA10's monthly shape; `OffshoreMetoceanFitTests` re-runs the fit and fails if the shipped values are not its output |
| swell median | 2.2 m all year | 2.2 m in January × NORA10's monthly 1-year extremes (0.96 m in July) | a constant median put summer storms at winter heights; NORA10's July 1-year extreme is 0.44 × January's |
| swell spread | σ_ln 0.30 | 0.20, Box-Cox λ 0.8 above 1 m | 0.30 with seasonal medians pushed the median annual maximum and the days above 2 m to the bands' edges |
| plot scales | off_1 1.0, off_2 1.3, hav_1 2.4 | 1.058, 1.376, 2.752 | fitted: Hs100 / the reference sea's empirical 100-year level (4.724 m on the fit seeds) |
| reference annual mean | 0.9 m (off_1) | 1.0 m (the day's peak) | [O:R17]'s 0.9 m is a time mean; a day's peak lies ~10 % above it |
| AR(1) φ, σ_ε, Δ_weather, rates, leads, durations, Tp, limits | spec | spec | unchanged |

### The forecast (`MetoceanForecast`, pure, never draws)

For a target day T = t + k, P(Hs ≤ L) is the model's conditional probability given
today's saved state: the base's AR(1) Gaussian at lead k mixed over the weather chain's
state distribution on T (`WeatherSystem.StateDistribution(k)`, the sampler's own
transition rows), the known events' timing exactly and each peak's posterior given its
estimate (prior N(0, 1) in the peak's latent units, estimate error τ = min(1, days to
onset / 4), conjugate update: mean y/(1+τ²), sd τ/√(1+τ²); exact from the onset day),
and every event not yet created (creation day, onset and plateau enumerated at the
month's rates). Numerics: the base in closed form, one uncertain swell on Gauss-Legendre
panels refined toward where it alone reaches the limit, likely pairs (≥ 2 % of the
probability) on nested panels, likely triples with the nearest swell on panels and the
others by tensor Gauss-Hermite, all rarer combinations by one antithetic Hammersley rule
(1,024 points), combinations under 1e-9 counted as above every limit (reported).
- Accuracy against a high-resolution integration of the same terms (1,600 / 320 × 480
  nodes, 2^15 Halton points): worst **1.6e-4** over 1,550 probabilities (62 snapshots ×
  leads 1/3/5/7/10 × 5 levels; 150 days with two uncertain swells, 132 with a tail); on
  every third snapshot (2,075 probabilities) the worst was 2.1e-4.
- Against 60,000 futures simulated from the generative model (24 states, leads 1/3/6/10):
  worst **3.1** standard errors.
- The known events' posterior: PIT deciles 9.6-10.4 % at leads 1-4 before onset, the
  10-90 % band holds 79.8-80.1 %, and the posterior mean's squared error is τ²/(1+τ²)
  (0.0590 / 0.2001 / 0.3595 / 0.4986 against 0.0588 / 0.2000 / 0.3600 / 0.5000), below
  the raw estimate's τ².
- Operability odds are honest per vessel (leads 2 and 6, seeds 1-10 × 20 years, ±0.01):
  delousing by a standard vessel 0.880 forecast / 0.880 realized, by an offshore wellboat
  0.940 / 0.941, DP resupply 0.977 / 0.977, helicopter crew change 0.902 / 0.901 (its
  wind from the weather chain: Severe days 0.499 below 25 m/s against 0.500).
- Cost (M1 Pro, load average 24-34 during the measurement): one P(Hs ≤ 2 m) at lead 1-4
  3-7 µs, lead 5 ~56 µs, leads 6-10 ~230-260 µs; a full `Issue` for 4 limits median
  3.3 ms (p95 8.9), with the 10/50/90 % quantiles median 15.8 ms (p95 74); the day's tick
  0.28 µs. The Driftsplan should ask lead by lead from tomorrow and stop at the first
  window, not issue the full forecast every day (F2-05/F2-07).

### Mutation checks (key tests fail when the model is broken)

| Mutation | Tests that fail |
|---|---|
| a 9th uniform a day | acceptance 7 |
| `Issue` touches the saved state | acceptance 6 |
| the wind's spread 4.0 → 4.0000001 m/s | the weather fingerprints (acceptance 8) |
| events created 3 days ahead | acceptance 4 |
| the AR residual not saved | acceptance 9 |
| the base forecast 20 % too sharp | the whole-distribution PIT (acceptance 5's ± 0.05 deciles alone pass) |
| one refined panel instead of four | the quadrature check |
| half the not-yet-created events' rate | the simulated-futures check |
| no background swell | the calm-sea check |
| no posterior shrinkage (estimate taken as the peak) | the posterior's squared error (its PIT alone passes) |
| the helicopter's visibility gate dropped | the per-vessel honesty check |

### Findings (RUN_LOG §8)

- **Weather alone almost never shuts off_1 for 21 days** (0.1 % of winters; the longest
  spell is 7 days in the median winter). F2-08's pre-registered 10-30 % of winters with a
  21-day warning must come from booking, vessel and crew delays on top of the sea.
- **One estimate error per event.** The spec draws the peak estimate's error once (u6)
  and scales it by the lead, so the posterior given today's estimate is calibrated at
  every lead, but the estimates on two days with different τ (leads 4 and 3) together
  reveal the peak. A player would have to invert the displayed p50 to use it.
- **The forecast is not free.** Long leads cost ~0.25 ms a probability; the wiring
  should cache a day's forecast per plot and query short leads first.
## 1r. U2-04: feeding plans (2026-10-15, branch `wave3/ui-a-1`)

A site's feeding plan is its ration (a share of its fish's appetite, S2-03's
`RationPolicy` on every pen's population), the recipe its barge carries and a
window (0 = until changed). Calibration section `feedPlan` (v1.0.11 on its branch; registry 1.1.1 on the wave-3 integration); a site
with no plan feeds the standard feed to full appetite, as every site did, so
the golden traces are unchanged (`GoldenTrace` 35 of 35, and
`StandardPlans_LeaveTheGameUnchanged`: a game that sets only standard plans
serializes byte-identical to one that never touches the verb after 200 days,
events on).

| knob | value | source |
|---|---|---|
| "Begrenset" stop | 70 % of appetite | the growth law's optimum ration (Brett 1979, `growth.rationCurvature`) |
| "Moderat" stop | 85 % | game estimate after Johansen and Jobling 1998 (Aquaculture International 6: 1-17): satiation grows faster, predetermined rations convert better |
| lowest ration | 50 % | Stefansson, Imsland and Handeland 2009 (Aquaculture 290: 243 ff.): post-smolts on 50-100 % rations grew, on 0-25 % they did not |
| high-energy recipe | FCR x 0.95, price x 1.05 | Hillestad et al. 1998 (Aquaculture Nutrition 4: 89-97): FCR falls with dietary energy at equal energy intake; +5 % energy and the price are game estimates |
| economy recipe | FCR x 1.06, price x 0.94, astaxanthin 30 mg/kg | same rule for ~6 % less energy; pale fillets (FeedRecipe grade C, VAP premium halved; Torrissen 1989) |
| longest window | 365 days | one sea year |

A recipe is priced on its energy: the feed cost per kg of growth stays within
1 % of the standard feed's (0.9975 high energy, 0.9964 economy); the recipes
differ in feed tonnage (sludge, N and P under the pens) and fillet colour.

### Acceptance 1: 120 days at 70 % against full appetite (player path)

`FeedPlanTests.SeventyPercent_For120Days_*`: the family farm (forste_sesong,
36,000 fish, 0.63 kg on day 30), the verb on day 30, 120 days.

| seed | events | ration | growth (kg) | mean weight day 150 | biological FCR | economic FCR | feed cost per day | feed cost per kg growth |
|---|---|---|---|---|---|---|---|---|
| 1 | off | 100 % | 26,477 | 1.39 kg | 1.112 | 1.123 | 5,527 NOK | 25.05 |
| 1 | off | 85 % | 22,015 | 1.27 kg | 1.091 | 1.102 | 4,509 NOK | 24.58 |
| 1 | off | 70 % | 17,520 | 1.14 kg | 1.080 | 1.090 | 3,550 NOK | 24.31 |
| 7 | off | 100 % | 22,267 | 1.27 kg | 1.111 | 1.122 | 4,644 NOK | 25.03 |
| 7 | off | 70 % | 14,871 | 1.06 kg | 1.080 | 1.090 | 3,013 NOK | 24.32 |
| 13 | on | 100 % | 23,885 | 1.31 kg | 1.111 | 1.122 | 4,982 NOK | 25.03 |
| 13 | on | 70 % | 15,894 | 1.08 kg | 1.079 | 1.090 | 3,220 NOK | 24.31 |

At 70 % the fish grow 66-67 % as much at a 2.9 % lower biological FCR and
35-36 % less feed cost a day; the feed cost per kg of growth is 3 % lower,
paid for in time (the fish reach harvest weight weeks later). That is the
trade the plan offers: hold biomass under MTB or wait for a price, not cheaper
salmon.

### Acceptance 2: the 30-day projection (twin-lite, `SimulationEngine.ProjectFeedPlan`)

The projection grows the site's cohorts on the tick's law in the water the
area can expect (its climatology plus today's anomaly fading at its e-folding
time), with today's oxygen appetite, the gills healing while no bloom stands,
the density the growing fish reach in their pens, the site's spreading and the
day length; fish are held constant. Measured against the game's next 30 days
at 70 % on the standard feed, 20 seeds x 6 start days:

| setup | n | growth: median / p90 / max error, share within ±10 % | cost per day | FCR |
|---|---|---|---|---|
| family farm (zone water, bloom deck), events off | 120 | 4.2 % / 14.0 % / 40.6 %, 81 % (mean signed +1.9 %) | 3.5 % / 10.8 % / 24.4 %, 89 % | 0.4 % / 5.1 % / 11.5 %, 99 % |
| zone-less PO4 site (only the sea's temperature random) | 120 | 3.7 % / 9.0 % / 20.6 %, 93 % (mean +1.5 %) | 3.5 % / 8.6 % / 19.8 %, 93 % | 0.2 % / 0.4 % / 0.8 %, 100 % |
| family farm seed 1, first month (nothing random happens) | 1 | 0.0 % | 0.0 % | 0.0 % |

Single cases miss by more than 10 % when the window holds a warm or cold
spell (the AR(1) anomaly moved 1.2 °C in 15 days on seed 7) or a bloom (seed
2, day 300: the gills fell from 79 to 60 and growth with them); the projection
is their expectation, unbiased (mean error +1.5 to +1.9 %). The tests hold the
median at 5 %, the ±10 % share at 80 % (family farm) and 90 % (zone-less),
the mean within ±3 %, and the FCR at ±10 % in 95 % of cases (±1 % where only
the weather is random).

## 1s. U2-05: the harvest planner's projection (2026-10-16, branch `wave3/ui-a-2`)

`SimulationEngine.ProjectHarvest(holding, pens, fraction, waitDays)`
(`Core/SimulationEngine.HarvestPlanner.cs`, twin-lite, pure: no draw, no
write) runs S2-04's planned pipeline forward on copies of the chosen pens:
the wait on the tick's growth law at the site's feeding plan
(`PopulationTwin`, shared with U2-04's projection), the order, each pen
fasting ahead of its turn (30 degree-days in the expected water, at least the
calibrated minimum), the wellboat slot and the slaughterhouse's daily capacity
behind the orders already queued, then every batch priced as `SellLot` prices
it: contracts first (oldest first, skipping one the weekly tick will have
expired), then the market's expected price that day
(`MarketSimulation.ExpectedPrices`: the daily step without its noise and
shocks, the player's own batches pressing the price after their day), the
processing plant's premium. Expected deaths are the median of the ledger's
last 30 daily death rates (a normal day; an outbreak is not projected
forward). No new calibration: every number is S2-04's `harvest` section, the
market's and the growth law's.

The planner compares ordering now with waiting 14, 28 and 56 days. "Net after
feed" is the figure that compares them: the harvest's net less the feed the
fish eat until their fast (a wait grows the fish and the gutted kilos, and
costs feed and a normal day's deaths).

### Acceptance 2: the same-day projection against the realized sale

`HarvestPlannerTests` (the planned order placed the same day as the
projection, then the game run until the order has sold):

| setup | n | gross | gutted kilos (HOG) |
|---|---|---|---|
| player path: forste_sesong seeds 1-20 + po3_red_sone seeds 1-10, events, market noise and shocks on, ordered at 4.5 kg | 30 | within ±5 % in 28 of 30 (93 %), median 1.09 %, mean -0.11 % | within ±3 % in 28 of 30 |
| zone-less site, market noise off | 12 | within ±2 % in 11 of 12 (seed 1: -4.46 %, the certification level changed during the harvest and the engine repriced) | within ±0.15 %; the kilos the price is paid on within ±0.45 %; last day within a day |
| wait 14 / 28 / 56 days, then order | 3 x 10 | within ±3.41 %, all 30 within ±5 % | within ±1.70 % |
| express, same day (the quote is what the sale books) | 2 | exact (gross, net, contract kilos and sales, VAP) | exact |
| expected price path, bare market, volatility 0, 60 days | 1 | exact until the first weekly tick, within ±1 % after (mean ratio 0.997-1.003) | — |

The two player-path misses on gross: forste_sesong seed 2 (+5.02 % gross,
+4.86 % HOG: fish died during the fast, events a normal day's death rate does
not foresee) and po3_red_sone seed 3 (-7.76 % gross at -0.02 % HOG: the
market moved). The two HOG misses are forste_sesong seeds 2 and 4 (+3.08 %). The projection is the
order's expectation, unbiased (mean -0.11 %); the tests hold ±5 % in at least
80 % of player-path cases, the median within 3 % and the mean within ±2 %.

In the game (UI flow, po3_red_sone seed 91, day 294, 6 pens at 4.58 kg): the
planner's "Nå" column said 17,873,202 NOK gross; the two slaughter days
booked 18,591,607 NOK (-3.9 %), and the report's gross, cost and net were the
ledger's harvest entries to the krone.

### Acceptance 3: waiting leaves the live game untouched

po3_red_sone seed 17, 60 days, every site projected at waits 0/14/28/56 every
day (more than 100 projections): every SimRng stream and the save are
unchanged after each day's projections, and the probed game equals its
never-probed twin after 60 days (`TheProjection_LeavesTheLiveGameAndItsRngUntouched`;
mutation: one draw from the live `market` stream inside `ProjectHarvest`
fails it).
## 1t. U2-01: the expansion flow's numbers (2026-10-15, branch `wave3/ui-b-1`)

The expansion flow (map, BuildSiteDialog, LicenceAuctionCard) shows the
engine's numbers; nothing in it is a new price. What it quotes, and where the
numbers come from:

### Growth-licence auctions against the state's auctions

The state sells capacity in simultaneous clock auctions (2018, 2020, 2022,
2024). Averages per tonne of MTB:

| auction | sold | revenue | NOK per t | source |
|---|---|---|---|---|
| 2020 | 27,189 t | ≈ 6.0 bn | ≈ 220,000 | regjeringen.no / iLaks (2020) |
| 2022 | 24,644 t of 32,887 t | ≈ 3.8 bn | ≈ 154,000 | regjeringen.no / iLaks (Oct 2022) |
| 2024 | 17,330 t (all) | 5,288,602,600 | ≈ 305,170 (170,000 PO1 to 348,400 PO9 by area); minimum price 170,000 | regjeringen.no, "Resultater fra auksjon av nye oppdrettstillatelser 2024" (25 June 2024), https://www.regjeringen.no/no/aktuelt/resultater-fra-auksjon-av-nye-oppdrettstillatelser-2024/id3046941/ |

The game's reserve is `Licensing.LicenseCostNOK` = 150 M a licence (unchanged):
192,308 NOK/t for a 780 t licence, 158,730 NOK/t for 945 t in Troms and
Finnmark. A rival's ceiling is reserve × (1.08 + 0.55 appetite + 0.15 lobbying)
(`LicenseAuctionSystem.MaxRivalPrice`, unchanged): 201-254 M, 258-326 k/t at
780 t. Measured clearing prices of rival-won auctions (the company opens the
auction and does not bid; `ExpansionFlowTests.RivalWonLicences_ClearInsideTheStatesAuctionBand`,
30 seeds × PO4 and PO12, n = 60):

| min | median | max | band asserted |
|---|---|---|---|
| 251,852 NOK/t | 305,128 NOK/t | 325,962 NOK/t | 154,000 (2022 average) to 348,400 (2024 highest area average) |

The median matches the 2024 auction's average (305,170 NOK/t). The card quotes
the 2022 and 2024 averages (`LicenseAuctionSystem.StateAuction2022AveragePerTonneNok`,
`StateAuction2024AveragePerTonneNok`).

**Activity rule** (`LicenseAuctionSystem.ClosingRoundExtensionDays` = 1): a bid
on the closing day keeps the auction open one more day, so the rivals answer
it. The state's clock auctions close only after a round with no new demand
(regjeringen.no, "Auksjon av oppdrettstillatelser holdes 24. juni", 2024).
Before, a bid on the last day won at 5 % over the leading bid with no answer.
No golden trace holds an auction (no scenario has a zone the company does not
farm), so the traces are unchanged.

### What a build costs and adds (`SimulationEngine.PlanSiteBuild`)

| line | value | source |
|---|---|---|
| investment | 6,000,000 NOK | `Infrastructure.SeaSiteBuildCostNOK` |
| locality MTB | 3,120 t | `Capacity.BuiltSiteLocalityMtbTonnes` (NFD 20.09.2017) |
| licence MTB added | 0 t | S2-01a |
| pens | 8 × 160 m, 40 m net (25 m wall + 15 m cone), 61,116 m³ | `pens` section (S2-01b) |
| running cost | +354 kNOK a month: 7 crew × 48,000 NOK + 0.01 % of the build cost a day | `Labor.WorkersPerSite`, `Labor.WorkerMonthlySalaryNOK`, `ProcessDailyFinance` |

`TheBuildPlan_IsWhatTheBuildDoes_CostPensMtbAndRunningCost` holds the preview
to the books: the day's operations cost rises by the plan's running cost per
day after the build (± 1 NOK a month). In Første sesong the second site adds
4.25 M a year of crew and upkeep before it holds a fish: the dialog says so
before the player commits.

### Catalogue zones

One growth zone per production area (`Environment/ExpansionZones.cs`, named
after a coastal municipality with sea farming in the area, none in a national
salmon fjord). A catalogue zone joins the game only with its first site, so a
game that never expands runs as before (golden traces identical). Its traffic
light is its area's as the game knows it (the worst colour of the area's zones;
water nobody farms assesses green, `TrafficLightSystem.GreenLiceThreshold`).

## 1u. U2-03: the liquidity ladder, overdraft interest and the rescue loan (2026-10-16, branch `wave3/ui-b-2`)

S1-03 warns by depth (overdraft, half-way, final warning) and ends the game
below the scenario's floor; it has no sense of time, and negative cash was free
(RUN_LOG §8 "overdraft is free"). U2-03 adds the bank's reading of the cash and
a price on the overdraft. Calibration section `liquidity` (v1.0.11,
`Calibration/LiquidityCalibration.cs`); engine `Core/SimulationEngine.Liquidity.cs`,
rules `Finance/LiquidityLadder.cs`.

### What negative cash costs now

| knob | value | source |
|---|---|---|
| `OverdraftInterestRate` | 12.25 % a year, daily on the closing cash (act/365) | Norway's late-payment interest (forsinkelsesrente) for H2 2025 and again from 1 July 2026: Norges Bank's policy rate + 8 pp (forsinkelsesrenteloven § 3; Finanstilsynet, https://www.finanstilsynet.no/nyhetsarkiv/nyheter/2025/forsinkelsesrente-og-standardkompensasjon-for-inndrivelseskostnader-fra-1.-juli-2025 and .../2026/...-fra-1.-juli-2026) |
| term loan (unchanged) | 5.5 % a year | `Finance.LoanInterestRate` |

Negative cash is unpaid bills and an overdrawn account, so it pays what an
unpaid bill costs in Norway, well above the term loan: borrowing now beats
riding the overdraft (`BorrowingBeatsRidingTheOverdraft`: two identical
po3_red_sone companies 3 MNOK under, one borrows 5 MNOK at 5.5 %; 60 days later
the borrower has more equity). Paid by the golden traces' players over 730
days (docs/run/evidence/U2-03_golden_diff.md): algekatastrofe 0.49-0.77 MNOK,
isa_utbrudd 0.52-0.60 MNOK (before the restock cascade), krise_manager
0.22-0.48 MNOK, po3_red_sone 0.33-0.58 MNOK, lofoten_eventyr 34 NOK-3.4 kNOK;
the 7 traces whose cash never goes below zero pay nothing. The career band
tests (`PlayerScriptCareerTests`, `FullCycleEconomicTests`) pass unchanged.

### The bank's reading: burn, runway, warning, call

| knob | value | why |
|---|---|---|
| `BurnWindowDays` / `BurnStepDays` | 35 / 7 | the burn is the median of the rolling 7-day changes over 35 days, per day: a week holds the running lumps (a storm repair, a monthly bill) that a single-day median skips, and the median of the weeks ignores a one-off harvest payment or purchase (`Burn_IsTheMedianWeek_SoAOneOffNeitherHidesNorFakesIt`) |
| `MinBurnHistoryDays` | 14 | no forecast in a game's (or a loaded save's) first two weeks |
| `WarningRunwayDays` | 60 | liquidity warning (typed warning, verb raise_capital, deadline = the projected floor day): twice the roadmap's 30 days, because the burn grows with the fish (feed) |
| `BankCallRunwayDays` | 28 | bank call (critical card) with the rescue offer: four weeks, so a 14-day offer is answered with two to spare and a last lump still leaves the roadmap's 10 days |
| `ReArmRunwayDays` | 120 | the episode ends (both rungs re-arm) when the runway is back above this or the cash stops falling |

Measured on the engine (passive companies: no harvest, the bank never answered;
9 scenarios × seeds 1-6; barekraft_mester never declines, so 48 declines;
probe in the run's scratch, the acceptance test is
`LiquidityLadderTests.ScriptedDecline_IsWarned30DaysAndCalled10DaysBeforeBankruptcy`
over 13 scenario/seed pairs):

| lead before bankruptcy | min | median | max | roadmap |
|---|---|---|---|---|
| liquidity warning | 40 days (lofoten_eventyr s2) | 69 days | 176 days | ≥ 30 |
| bank call | 14 days (krise_manager s3, laksebaronene s3) | 30 days | 113 days | ≥ 10 |

The shortest call leads come from a last lump before the floor (a storm repair
on a company already at its limit); the warning never came later than 40 days
out. A straight decline (a fixed cost every day) is warned 60 and called 28
days out to the day (`LinearDecline_IsWarnedAtTheRunwayThresholds`).

### The rescue loan

| knob | value | source |
|---|---|---|
| `RescueLoanInterestRate` | 14 % a year | three-month NIBOR (~4.5 % in 2025) + the 950 bps margin of a senior secured three-year bond a land-based salmon farmer placed in June 2025 (company release 2025-06-11, https://www.placera.se/pressmeddelanden/andfjord-salmon-group-andfjord-salmon-group-as--successful-bond-issue-20250611) |
| `RescueLoanArrangementFee` | 2 % up front | that bond's 98 % issue price |
| `RescueLoanTermMonths` | 36 | that bond's tenor; repaid daily like every loan |
| `RescueLoanAdvanceRate` | half the standing biomass at the norm price, less the drawn facility and the loans | biomass facilities are sized on the insured fish (a 2023 Icelandic sea farmer's EUR 70 M revolver on insured biomass and receivables, Fish Farmer, https://www.fishfarmermagazine.com/?p=145908); no public advance rate exists, so a conservative half (design value) |
| `RescueLoanCoverDays` / `RescueLoanMinCoverDays` | 120 / 30 | the offer covers 120 days of burn, rounded up to 0.5 MNOK (`RescueLoanRoundingNok`), and is not made when the collateral carries less than 30 days |
| `RescueOfferValidDays` / `MaxRescueLoans` | 14 / 1 | the offer's deadline (the clock slows and stops before it, U2-07); one rescue a game |

Offers at the bank call in the 48 declines: 40 offered (0.5-33 MNOK: forste_sesong
1.0-2.5, po3_red_sone 6.0-8.5, algekatastrofe 18.5-29.5, ressursrente
28.0-33.0); none for krise_manager (its small stock cannot carry its drawn
facility) or for isa_utbrudd seeds 4 and 6 (an infected stock). Taking the
offer and still playing passively buys 50-350 days (po3_red_sone: bankrupt on
days 323-360 without it, 393-451 with it; forste_sesong 366-604 → 534-954);
algekatastrofe ends lost instead of bankrupt (the survival goal counts the
loan against equity). `TakingTheRescueLoan_KeepsTheCompanyAlive_DecliningDoesNot`
is the player-path test; the loan is liquidity and a liability, never revenue,
and its fee is interest.

### The P&L story and the end report

`Finance/PnlStory.cs` sees every P&L booking under the statements' own
category (no RNG, no decision reads it). A turning point is one run of one
ledger line: its income and costs on days at most 3 apart and at most 14 days
long (a harvest's sales less its wellboat and slaughter cost); a longer run is
a running cost (feed, wages, interest) and never a turning point. The end
screen shows the three largest runs and the five largest ledger lines; both
reconcile to the raw booking stream exactly (`ScenarioReportTests`). A won
game earns a medal for the campaign: bronze, silver with two of three
distinctions, gold with all three: no bank call or rescue loan, mortality at
or under `Health.SeaMortalityPerCycle` (15 %, the calibrated cycle mortality), and no fine or infringement
fee (D16: medals, never cash). The win the UI tests play:
ressursrente seed 5 harvesting at 4.5 kg, won after 308 days with bronze (16.3 %
mortality and 7 kNOK of fines missed the other two).

## 1v. F2-12 wiring and F2-14: land sites in the engine, permits, grid and phased construction (2026-10-17, branch `wave4/sim-land-1`)

_The standalone land physics of §1p now run in the engine (`LandSiteTickSystem` → `TickLandSiteDay`,
`LandHooks`), and a land site is applied for, permitted and built through the engine's verbs.
Calibration `facilities.land` (registry 1.1.2): `facilities_land.json` gains `build`, `permits` and
`operations`. Decisions: RUN_LOG §7 (2026-10-17 F2-12/F2-14 rows); findings: §8._

### The engine reproduces the standalone anchors

`LandSiteEngineTests.TheEngineLot_HoldsTheStandaloneEnergyAndOxygenAnchors`: one full HFS lot on
pad_a in PO5 water, Salmon Evolution's strategy (r 0.65, the intakes blended toward 12 °C, heat
recovery), every tank stocked for its 80 kg/m³ plan peak in rotation (one tank every 26 days) and
harvested at 5.2 kg, measured over the second year:

| Measure | Engine (seed 54) | Standalone (§1p) | Band |
|---|---|---|---|
| Live-weight growth | 6,617 t | 6,640 t | — |
| Electricity | 40.8 GWh, **6.16 kWh/kg** | 6.40 kWh/kg | 6.0-7.5 [L:R2] |
| Liquid oxygen | **0.494 kg O2/kg** | 0.479 | 0.45-0.65 [L:R2] |

Two wiring choices close the gap between the two paths (before them the engine read 5.54 kWh/kg and
0.684 kg O2/kg): land tanks feed at the land plan's biological FCR (1.0 at 2 kg × the law's size
factor, [L:A3]/[L:R2]) instead of the sea's FCR model, and the land tick runs on the feed eaten (the
oxygen coefficient was back-calculated on it); land-stocked cohorts carry no sea-phase smolt-tier
growth bonus (the 400 g fish's tier prices it through the F2-13 hook). A pre-F2 land site keeps the
sea FCR and the pre-F2 tick (FarmTypeMoveTests).

### Opex lines of a land site

| Line | Daily amount | Source |
|---|---|---|
| `land_energy` | metered kWh × 0.80 NOK/kWh | [L:E1] midpoint (`energy.powerPriceNokPerKWh`) |
| `land_oxygen` | LOX bought × 4.0 NOK/kg | [L:S6] (`oxygen.loxPriceNokPerKg`) |
| `land_staff` | template FTE (HFS lot 33, pilot 15, module 6, RAS hall 35) × 650 k NOK/yr | [L:R2] staff; [code] salary |

Each line is attributed to the site and stays out of the resource-rent base (F2-04). Scope 2 =
metered kWh at the grid factor; the purchased LOX's upstream energy (0.75 kWh/kg) is booked as
`Scope3_Oxygen`. Maintenance, insurance, smolt and feed purchase, and the farming-cost band
(60-75 NOK/kg HOG) are F2-15's.

### Permits: refusal odds per pad (the quoted probability is the decided one)

Refusal = (base + per-sensitivity × the pad's nature sensitivity) × (production / 7,900 t)^0.5 for
zoning, the land permit and discharge, × 0.7 with an environmental survey (zoning, discharge, sea
works), × 0.5 with a denitrification commitment (discharge). Bases: zoning 0.01 + 0.35 s, land
0.005 + 0.10 s, discharge 0.01 + 0.35 s, sea works 0.005 + 0.10 s, biosecurity 0.01. Probability
that at least one case of the package is refused:

| Pad (sensitivity) | Full lot | Pilot lot | Post-smolt module | Full lot, survey + denitrification |
|---|---|---|---|---|
| a plot of 0.1 | 0.124 | 0.083 | 0.048 | 0.080 |
| pad_a Storholmen (0.2) | 0.203 | 0.134 | 0.074 | 0.130 |
| pad_delta Elveøyra (0.4, pre-zoned: no zoning case) | 0.233 | 0.158 | 0.096 | 0.132 |
| pad_b Skjærvika (0.6) | 0.473 | 0.320 | 0.176 | 0.310 |

Sensitivity 0.6 refuses 3.8× as often as 0.1 (package), 4.9× per zoning or discharge case; over 400
engine cases (200 zoning + 200 discharge per plot) the drawn refusal rates keep the ≥ 3× ratio
(`LandPermitTests.NatureSensitivity06_RefusesAtLeastThreeTimesAsOftenAs01`). Windows: zoning
365-730 d, land 135-225 d, discharge 180-300 d, sea works 90-150 d, biosecurity 68-113 d (± 25 %
around 180/240/120/90); appeals 180 d at half the refused case's odds; grid projects 365-1,095 d,
never refused, NOK 40 M per 10 MW connected (a second feed connects the pad's whole capacity on a
new line: 160 M NOK at pad_a).

### Construction: budgets, schedule and risk

| Unit | Budget | Phases (planned days) | First tanks | Complete |
|---|---|---|---|---|
| HFS lot | 2.07 bn + pad blasting (pad_a 120 M) | site works 180, tank rings 240, treatment hall 150, sea works 60, first tanks 40, completion 243 | 670 d (22 months) | 913 d (30 months) |
| HFS pilot (4 + 1 tanks) | 0.90 bn + blasting | 180, 150, 120, 60, 40 | 550 d (every tank) | 550 d |
| Pilot → full lot | 1.25 bn (pilot + expansion 2.15 bn) | tank rings 210, hall 90, first tanks 40, completion 60 | — | 400 d |
| Post-smolt module | 110 M + blasting | 90, 270, 90, 60, 38 | 548 d [code] | 548 d |
| RAS hall | 1.376 bn | 180, 270, 270, 60, 133 | 913 d | 913 d |

A full lot's first commissioning hands over half its grow-out tanks (6 of 14, [L:R1]). Started on
1 June (Salmon Evolution's phase-2 decision month) the planned lot reaches 22 and 30 whole months
exactly. The sea works neither start nor run in April-June ([L:R12]); a phase overtaken by the
season stands still in it (2,639 closed days stood still over 159 sampled starts × slips, never a
sea-works day worked in the season).

Over the engine's own draws (`facility_construction/cp_1/<phase>`, seeds 1-200, a June start):

| Measure | p50 | p90 | Acceptance |
|---|---|---|---|
| Managed overrun (budget share) | **+9.7 %** | **+17.5 %** | median 5-10 %, p90 ≤ 30 % (F2-14 #4) |
| Turnkey | +12 % up front | no overrun | +12 %, zero overrun |
| First tanks | 26 months | 29 months | plan 22 (finding §8) |
| Complete | 35 months | 39 months | plan 30 (finding §8) |

The preview's p50/p90 come from the same inverse CDFs on a 4,096-point Halton grid: overrun p50
within ±1 pp and p90 within ±3 pp of the 200-seed draws; first tanks and completion within a month.

One engine build (seed 82, pad_a, managed, started day 321): planned first tanks day 991, built
1,035 (+44 d); planned completion 1,234, built 1,336 (+102 d); paid 2.41 bn of a 2.19 bn budget
(preview p50 overrun 201 M, p90 392 M).

### Grid

Design load of a lot (heat pumps at full input + the intake pumps at the design new-water flow +
loop pumping, treatment and UV at the design loop flow + base load): HFS lot **10.76 MW**. pad_a's
40 MW carries 3 full lots; the 4th needs a 10 MW capacity project (`LandCommissioningTests.TheGrid_...`).

## 2. Key economic knobs

| Knob | Current | Real reference | Source | Recommended playtest band |
|---|---|---|---|---|
| `Economy.StartingCapitalNOK` | 10,000,000 | n/a (design) | game design | 5M-25M |
| `Economy.SmoltCostPerFishNOK` | 12 | ~12 NOK/fish | Fiskeridir smolt stats | 8-20 |
| `Economy.BaseSalmonPricePerKgNOK` | 70 | 2024 avg 92.7, 2025 avg 77.2, Q3-26 65-73 | Fish Pool / Euronext SISALMONI | 62-78 |
| `Market.LongTermMeanNOKPerKg` | 82 | 2020-2025 avg ~82-85 | Fish Pool | 74-90 |
| `Economy.PriceVolatilityPercent` | 12 | 10-15% annualised | Fish Pool | 8-18 |
| `Economy.SeasonalPriceSwingPercent` | 18 | ~15-20% peak/trough | Fish Pool seasonality | 12-25 |
| `Feed.FeedPricePerKgFeedNOK` | 22.3 | ~20-24 delivered | Fiskeridir / Mowi | 18-27 |
| `Feed.BaseFCR` | 1.15 | 1.14-1.15 biological; 1.2-1.5 economic | SalMar | 1.10-1.25 |
| `FeedCostPerKgProducedNOK` (derived) | 25.645 | ~25.7 | Fiskeridir Tab.38 | 22-30 |
| `Operations.HarvestCostPerKgNOK` | 4.0 | 3-5 (wellboat) | industry | 3-5 (read only by SimProbe's synthetic P&L; the engine uses `Harvest.*` since S2-04) |
| `Harvest.HogYieldFraction` | 0.84 | live → HOG 84% | Mowi Handbook 2025 p. 114 | 0.82-0.86 |
| `Harvest.WellboatCostPerKgHogNok` + `SlaughterCostPerKgHogNok` | 1.55 + 3.45 | 5.0 NOK/kg HOG | Fiskeridir 2023 (4.45/kg round × 1.125); Mowi EUR 0.40/kg GWT | 4.5-5.5 |
| `Harvest.ExpressSurchargePerKgHogNok` | 1.0 | spot wellboat + waiting pen | game estimate | 0.5-2.0 |
| `Harvest.PriceFactor*` (size ladder, HOG classes) | 0.60 / 0.72 / 0.86 / 0.93 / 1.00 / 1.04 / 1.08 / 1.10 | 2-3 kg ~86%, 6-7 kg ~108% of 4-5 kg | Kontali via Mowi Handbook 2025 §6.7 | ±0.03 |
| `Harvest.OrdinaryPriceFactor` / `ProductionPriceFactor` | 0.90 / 0.70 | downgraded ~30% off | Mowi Handbook 2025 p. 116 (production); game estimate (ordinary) | 0.85-0.95 / 0.65-0.75 |
| `Harvest.SlaughterCapacityTonnesHogPerDay` | 150 | ~56 t/facility/day average | Fiskehelserapporten 2024 §5.8 (game estimate) | 80-300 |
| `Harvest.FastingDegreeDays` | 30 | gut empty (kvalitetsforskriften § 16) | game estimate | 20-50 |
| `Operations.ProcessingCostPerKgNOK` | 9 | 8-10 | industry | 7-11 |
| `Finance.CorporateTaxRate` | 0.22 | 22% statutory | Skatteetaten | 0.22 (fixed) |
| `Finance.ResourceRentTaxRate` | 0.25 | 25% since 1 Jan 2023 | Skatteetaten / Prop.1 LS | 0.24-0.26 |
| `Finance.ResourceRentBasicDeductionNOK` | 70,000,000 | 70M NOK | Prop.1 LS | 60M-80M |
| `Licensing.MABLimitKgPerLicense` | 780,000 | 780 t standard (945 t far north) | Fiskeridir | 780,000 (fixed) |
| `Licensing.LicenseCostNOK` | 150,000,000 | ~150-200M market | Fiskeridir | 100M-250M |
| `Capacity.BuiltSiteLocalityMtbTonnes` | 3,120 | locality MTB cleared for a new site | NFD letter 20.09.2017, point 5 | data |
| `Capacity.PlanningThermalGrowthCoefficient` | 2.8 | sea cages 2.8 ± 0.3; realized cycle TGC 2.61-2.97 in the game (S2-03) | Nofima/SalMar 2012 (Hatchery International) | 2.6-3.0 |
| `Capacity.OrderDeadlineDays` | 14 | (game estimate) | the Phase 5.5 red-zone order | 7-30 |
| `Capacity.InfringementFeeCeilingG` × `GrunnbelopNok` | 15 G = 2,048,235 NOK | overtredelsesgebyr ceiling | FOR-2013-12-20-1675 § 8; NAV G 2026 | 15 G (law) |
| `Capacity.LiveToGuttedFactor` / `EconomicFeedFactor` / `OtherVariableCostPerKgNok` | 0.83 / 1.30 / 4.93 | the § 6 confiscation formula | FOR-2013-12-20-1675 § 6; Fiskeridir lønnsomhet 2024 | law / survey |
| `Capacity.ForcedHarvestPriceFactor` / `ForcedHarvestHeadroomFraction` | 0.70 / 0.05 | (game estimates) | akvakulturloven § 29 (at the company's cost) | 0.5-0.9 / 0-0.15 |
| `Capacity.MaxStockingDensityKgPerM3` | 25 | 25 kg/m³ per production unit | akvakulturdriftsforskriften § 25 | 25 (law) |
| `Capacity.DensityReferenceWeightKg` | 5.0 | proxy pen holds 50,000 fish at 25 kg/m³ (S2-03's commercial pen) | game estimate | 4.5-5.5 |
| `Pens.MaxFishPerPen` | 200,000 | at most 200,000 fish per production unit in the sea | akvakulturdriftsforskriften § 47a (2013) | 200,000 (law) |
| `Pens.StandardCircumferenceM` / `StandardNetWallDepthM` / `StandardConeDepthM` | 160 / 25 / 15 m (61,116 m³) | 160 m standard, 40 m net; nets 20-50 m | iLaks 2020; Bergheim 2012 | 120-200 m / 20-50 m |
| `Pens.BuiltSitePenCount` | 8 | 6-12 pens a locality | game estimate (roadmap S2-01b) | 6-12 |
| `Labor.WorkerMonthlySalaryNOK` | 48,000 | ~48k (2024) | industry wage stats | 36k-60k |
| `Liquidity.OverdraftInterestRate` | 0.1225 | 12.25 % forsinkelsesrente (H2 2025, H2 2026) | Finanstilsynet; forsinkelsesrenteloven § 3 | above `Finance.LoanInterestRate` (fixed by law) |
| `Liquidity.WarningRunwayDays` / `BankCallRunwayDays` | 60 / 28 | warning ≥ 30, call ≥ 10 days before bankruptcy (roadmap U2-03) | measured leads §1n | 45-90 / 21-35 |
| `Liquidity.RescueLoanInterestRate` / `ArrangementFee` / `TermMonths` | 0.14 / 0.02 / 36 | NIBOR + 950 bps, 98 % issue, 3 years | a June 2025 salmon-farm bond (§1n) | 0.10-0.18 / 0.01-0.03 / 24-60 |
| `Liquidity.RescueLoanAdvanceRate` | 0.5 | borrowing base on insured biomass | design value (no public advance rate) | 0.3-0.7 |

## 3. Key biological / environmental knobs

| Knob | Current | Real reference | Source | Recommended playtest band |
|---|---|---|---|---|
| `Biology.TargetHarvestWeightKg` | 5.2 | 4.5-5.5 kg live weight | Mowi Handbook / SalMar | 4.5-5.5 |
| `Biology.MinHarvestWeightKg` | 3.8 | ~3.5-4.0 kg min | industry | 3.5-4.2 |
| `Biology.MaxHarvestWeightKg` | 7.5 | >7 kg downgrade risk | industry | 6.5-8.0 |
| `Biology.GrowthToHarvestDays` | 540 | ~15-18 months at sea | Mowi / NVI | 450-600 |
| `Biology.SmoltMaturationDays` | 365 | ~12 months (smolt 100-250g) | Fiskeridir smolt stats | 305-425 |
| `Biology.OptimalFCR` | 1.15 | 1.14-1.15 biological | SalMar | 1.10-1.25 |
| `Biology.PoorFCR` | 1.35 | 1.2-1.5 economic | SalMar | 1.25-1.50 |
| `Health.SeaMortalityPerCycle` | 0.15 | 2024: 15.4% (14-16% band) | NVI Fish Health Report | 0.13-0.17 |
| `Health.SeaLiceThresholdPerFish` | 0.5 | 0.5 adult females/fish | Mattilsynet / BarentsWatch | 0.49-0.51 (regulatory) |
| `Health.MortalityRateBasePercent` | 0.025 | daily husbandry baseline | NVI | 0.01-0.05 |
| `Environment.OptimalTemperatureMinC` | 8 | 8-14°C optimal growth | Norwegian fjord data | 6-10 |
| `Environment.OptimalTemperatureMaxC` | 14 | 8-14°C optimal growth | Norwegian fjord data | 12-16 |
| `Environment.LethalTemperatureMaxC` | 22 | ~22°C lethal ceiling | Norwegian fjord data | 20-24 |
| `WaterQuality.StockingDensityThresholdKgM3` | 25.0 | ~25 kg/m³ regulatory | Mattilsynet | 20-30 |
| `SeaTemperature.AnomalyStdDevC` | 1.0 | SD 1.2 °C of PO3's yearly 3 m extremes | Sandvik et al. 2021 (ICES JMS 78:1849) | 0.6-1.2 |
| `SeaTemperature.AnomalyEFoldingDays` | 90 | months-long same-signed anomalies | HI note to the lice expert group, 2017 | 45-180 |
| `SeaTemperature.DailyFluctuationStdDevC` | 0.3 | (game estimate) | none | 0-0.6 |
| `SeaTemperature.EventAnomalyEFoldingDays` | 21 | July 2025 heatwave ~3 weeks | HI Rapport fra havforskningen 2025-69 | 7-45 |
| Production-area climatology (13 x 12, `ProductionAreas`) | §1f | HI normals 2012-2024 (NorKyst v3) | HI 2025-69 + station shapes | data, not a knob |
| `Growth.ThermalGrowthCoefficient` | 3.0 | 2.5-3.5 commercial sea TGC | Reid et al. 2013a / Dempsey et al. 2023; Karlsen et al. 2018 | 2.8-3.2 |
| `Growth.OptimalTemperatureC` | 13 | 13-14 °C (post-smolt to adult) | Kleiven et al. 2026; Calado et al. 2021 | 12.5-14 |
| `Growth.UpperGrowthLimitC` | 22 | ITmax ~22 °C | Calado et al. 2021 | 20-23 |
| `Growth.TgcValidToKg` / `AsymptoticWeightKg` | 3 / 20 | TGC valid 100-3000 g | Alanärä et al. 2001 / game estimate | 2.5-3.5 / 15-25 |
| `Growth.PhotoperiodGrowthSlope` | 0.2 | growth tied to day length | Aunsmo et al. 2014 (effect); game estimate (size) | 0-0.3 |
| `Growth.MaintenanceRationFraction` / `RationCurvature` | 0.10 / 0.5 | optimum ration below satiation | Brett 1979 (shape); game estimate | 0.05-0.2 / 0.3-1.0 |
| `Growth.SmoltWeightCv` / `BaseWeightCv` | 0.18 / 0.20 | CV 17-21% a month after transfer | Karlsen et al. 2018 / game estimate | 0.15-0.22 / 0.15-0.30 |
| `Mortality.AcuteLowOxygenMgL` | 3.0 | salmonid acute-mortality limit 3 mg/L; Pcrit 3.4-3.5 mg/L at 14-18 °C | US EPA 1986 (EPA 440/5-86-003); Barnes et al. 2011 | 2.5-3.5 |
| `Mortality.LowOxygenDeathFraction` (/Severe/Anoxic) | 0.10 / 0.25 / 0.50 below 3 / 2 / 1 mg/L | (game estimate: the limit is sourced, the kill above it is not) | plankton-crash steps, kept | monotone, 0-1 |
| `WaterOxygen.HypoxiaSagMaxDepth` | 0.42 | in-cage 26-90 % saturation, 1 reading in 4 growth-limiting | Solstorm et al. 2018 (Aquaculture 486:122-129) | 0.25-0.6 |
| `WaterOxygen.BloomChlorophyllAtCapacityUgL` | 10 | seasonal bloom 1-6.5 µg/L (2017), <0.3-10 (2018) | NIVA FerryBox Indre Oslofjord 2017/2018 | 5-20 |
| `WaterOxygen.PhytoplanktonCarbonPerChlorophyll` | 50 g/g | ~10-330 g/g by light, temperature, nutrients | Cloern et al. 1995 (game estimate inside the range) | 30-100 |
| `WaterOxygen.OxygenPerOrganicCarbon` | 3.47 g/g | 138 O2 : 106 C (mol) | Redfield et al. 1963 | 3.4-3.8 |

## 4. Playtest tuning priorities

Highest-leverage first:

1. **Feed cost/kg produced (price × FCR).** Watch EBIT/kg after a full cycle.
   Real feed cost is ~25.7 NOK/kg produced; if playtests show feed at <20 the
   model has regressed to the old flat 12.5 NOK/kg fish. Keep within 22-30.
2. **Salmon spot mean & volatility.** Real long-run ~82-85 NOK/kg. If EBIT/kg
   sits far outside the cyclical 8-25 band (SalMar 23.4 FY24, 13.6 FY25),
   tune `Market.LongTermMeanNOKPerKg` and `VolatilityWeekly` (5.5-12.5).
3. **Sea mortality / cycle (0.15).** Directly scales harvestable biomass;
   real 2024 was 15.4%. Keep in 14-16%.
4. **FCR band (base 1.15, poor 1.35).** Drives both feed volume and cost.
   Economic FCR (1.2-1.5) is what players actually experience once stage,
   temperature and precision-feeding penalties apply.
5. **Resource rent (25%, 70M deduction).** Dominant late-game tax; foreign to
   most players — verify the deduction is legible in the finance UI.

## 5. Resolved engine coupling (v1.0.2 / v1.0.3)

### Measured-band gaps resolved (from §1b, Oct 2026)

The three metrics previously deferred to the engine owner are now wired into
`SimulationEngine` / the biology and market models it owns:

- **Sea mortality 10.8% -> 15.8%/cycle.** `MortalityModel` now reads
  `CalibrationRegistry.Health.SeaMortalityPerCycle` and maps the whole-cycle
  fraction to a per-day sea hazard
  (`1 - (1 - perCycle)^(1 / GrowthToHarvestDays)`) for the Seawater/Harvest
  stages; freshwater stages keep `GameConstants.BaselineMortalityPerDay`.
  `SimulationEngine.ApplyBalancingConfig` pushes the registry anchor in. From
  v1.0.3 that anchor is split into the husbandry baseline plus an explicit
  disease slice when events are on (see below); with events off the full anchor
  stays in the baseline, so `enableEvents:false` remains deterministic and
  on-band.
- **Realized price ~153 -> 88.0 NOK/kg.** `MarketSimulation` sources all O-U
  parameters from `CalibrationRegistry.Market` (mean 82, weekly theta/sigma,
  42-150 band), mean-reverts the supply/demand indices daily, applies a bounded
  *decaying* supply/demand price offset, and keeps spot/realized inside a
  normal-play band (mean ±8) outside material shock windows. `MarketIntelligence`
  RNG is seeded from the engine seed, so price is reproducible.
- **Peak adult-female lice 0.00 -> 0.49/fish.** `SimulationEngine` seeds
  external lice pressure at sea stocking and drives `SeaLice.SetExternalPressure`
  seasonally (late-summer peak, winter trough), so a clean grow-out reaches the
  0.5 regulatory action limit without treatment. No RNG is involved.

### Depth-system wiring resolved (v1.0.3)

The two dormant depth systems are now wired into the live engine in-band:

- **Disease-attributable mortality is live and tunable.** `IntegratedDiseaseSystem`
  now calls `_baseSystem.SetMortalityModel(...)` and `Update` deterministically
  ticks the base SIR for every population, injecting the resulting mortality
  through `MortalityModel.SetDiseaseMortalityRate`. `MortalityModel` splits the
  calibrated anchor into `SeaMortalityPerCycle × (1 − SeaMortalityDiseaseShare)`
  (husbandry baseline) and the disease slice, and `ConfigureDiseaseShare` sizes
  `DiseaseSystem.ExtraMortalityRatePerDay` so the endemic daily hazard integrates
  to the slice over the grow-out. `enableEvents:false` never injects. Measured
  `DiseaseStress`: disease **4.39%** of the **14.88%** total (§1c).
- **Sea-site storm escapes are engine-driven.** `SimulationEngine.ApplyStormHazards`
  (weather phase) decrements `SeaSite.NetIntegrityPct` on storm days, repairs it
  in calm weather, records escapes via `SeaSite.RecordEscape` (gated at the
  documented 40% risk line and a rare severe-day containment failure), charges
  the regulatory fine and writes the event log. The duplicate escape/net-decay
  block was removed from `GameBridge.CheckWeatherImpacts`. Deterministic for a
  given seed.

### Remaining engine coupling

- **Stale `SimulationEngine.BalancingConfig.FeedCostPerKgNOK` default** now
  mirrors the registry (22.3); `FromRegistry` still overwrites it every load.
  Kept only as the local engine view.
- **`FeedSystem` daily-ration coupling.** The daily ration
  (`FeedSystem.FeedingRatePercent` 1.5% bodyweight) is still decoupled from the
  SDE growth response, so *accounting* feed cost per harvested kg (~4x the
  registry-derived 25.7) is far above the price × FCR figure the bands use.
  `SimProbe` therefore reports the documented `price × economic FCR` definition
  (§1b). Coupling the ration to the SDE growth response remains an engine
  change.
- **`MultiFarmManager.FarmSite.FeedCostPerKg = 15m`** (default, asserted in
  `MultiFarmManagerTests.cs:584`) is a legacy per-site estimate that does not
  source from `CalibrationRegistry`. It is *not* the live feed-cost path (the
  live authority is `FeedSystem.FeedCostPerKg`), so it does not affect
  simulation cash. Recommended: derive it from the registry
  (`FeedPricePerKgFeedNOK`) and update the test, or document it explicitly as
  an abstract estimate.

## 6. Guard coverage

`CalibrationGuardTests` enforces the bands above. Feed-path specific guards:

- `Feed_MatchesRealReferences` — FCR band + feed price band + FCR anchor
  equality with `Biology.OptimalFCR`.
- `Feed_CostModel_PriceTimesFcrMatchesRealFeedCostPerKgProduced` — registry
  derived cost within 25.7 ± 4 and > 20 (anti-regression to flat 12.5).
- `FeedSystem_Defaults_MatchRegistry_NoDrift` — a bare `new FeedSystem()` must
  match registry price/FCR and the derived cost (guards the 13.5 / 1.12
  default regression).

v1.0.3 depth-system guards in `BalanceReportTests`:

- `DiseaseMortality_IsVisibleAndBounded` — the SIR disease slice must be visible
  within events-on runs (median > 0) and bounded (median 0.5-10%/cycle, p90
  within the runaway guard, and less than the total).
- `DiseaseStress_TotalMortality_WithinReferenceBand` — disease-stress total sea
  mortality stays in the 14-16% band.
- `DiseaseShare_IsCalibratedAndSplitsTheSeaAnchor` — `SeaMortalityDiseaseShare`
  is a real slice (not 0 or 1) and the husbandry + disease split reconstructs
  `SeaMortalityPerCycle` exactly.
- `StormEscapes_StayWithinRegulatoryGuard` — engine-driven storm escapes stay
  within the 0.5%/site/year share guard and the 1 event/site/year guard, storms
  actually occur, and maintained nets stay above the 40% risk line.
- `SimProbe_IsDeterministic_SameSeedSameMetrics` /
  `ExtendedProbes_AreDeterministic_SameSeedSameMetrics` — two identical seeds
  produce byte-identical metrics for the baseline, disease and storm scenarios.

S1-01 / S1-02 guards (§1d):

- `WaterChemistryStabilityTests.Po3Probe600Days_StaysOffTheClamps_WithRareSummerOnlyOxygenDips`
  — 600 days, seeds 7 and 13: no clamp, pH 7.8-8.2, DO below 70% saturation on
  at most 10% of days and only in Apr-Sep, a bloom on at most 15% of days.
- `WaterChemistryStabilityTests.ZoneWater_FollowsTheSeaTemperature_AndItsOxygenTheSeasonalSolubility`.
- `SDEGrowthModelTests.CalculateOxygenSaturation_*` — 9.02 +-0.05 mg/L at 10 °C
  and 35 PSU, Garcia & Gordon (1992) to 0.01 mg/L, freshwater reference values.
- `FullCycleEconomicTests.FullProductionCycle_OnTheEnginePath_FeedPerKgGainInBand`
  — kg feed per kg gain 1.0-1.6 on the engine path, reconciled with the ledger.
- `FullCycleEconomicTests.ZonePO04_AndAColdZone_GrowAndEarnAlike_UntilZonesGetTheirOwnTemperature`
  and the pre-registered, skipped `Po4ReachesFiveKilos_TwoToFourMonthsBeforePo13`
  (S2-02/S2-03 remove the Skip).
- `PlayerScriptCareerTests.Po3RedZone_CommandDrivenCareer_CompletesInBand` —
  realized >= 0.95 x spot on the harvest days under the D2 harvest rule.
- `StormSeasonTests` — every transition row sums to 1; winter has severe storms
  and at least the summer storm share (stationary and sampled); the forecast
  matches realized storms within 2 points (10 seeds) and 0.5 points (200 seeds);
  storm damage once per episode, across a save; winter storms do not cool.

## Review log

| Date | Reviewer | Changes |
|---|---|---|
| 2026-10-07 | Balance consolidation | FeedSystem defaults now sourced from CalibrationRegistry; added standalone-drift guard; report created; feed row in REALISM_CHECKLIST reconciled. |
| 2026-10-08 | Balance harness | Added `SimProbe` + `BalanceReportTests` (seeds 1-20, 3-year grow-out cycles); measured-band table (§1b); tuned `FCRTempSensitivity` 0.018 -> 0.008 (v1.0.1); recorded mortality/price/lice deferrals (§5). |
| 2026-10-08 | Calibration v1.0.2 | Wired `Health.SeaMortalityPerCycle` into `MortalityModel` (per-cycle -> per-day; 10.8 -> 15.8%); sourced `MarketSimulation` O-U from `CalibrationRegistry.Market`, added supply/demand mean-reversion + normal-play band, seeded market intelligence RNG (153 -> 88.0 NOK/kg); seeded seasonal external lice pressure (0.00 -> 0.49/fish). Re-ran `SimProbe` seeds 1-20 both scenarios; full suite green (707 passed). Updated `BalanceReportTests` bands from deferred to the real references; bumped `calibration.json` to 1.0.2. |
| 2026-10-08 | Extended probe | Added disease-cycle, storm-escape, multi-site economics and lice-control scenarios to `SimProbe` + `BalanceReportTests` (§1c). Measured disease term 0.00%/cycle (dormant; engine deferral recorded in §5), storm escapes 0.15%/site/yr vs 0.5 guard, multi-site EBIT/kg 16.43 (in the 8-25 cyclical band), cost 69.6 NOK/kg produced, 1.5 treatments/cycle with 0.07% per-cycle resistance. Documented the test-side containment-risk model and the farm-gate cost stack. |
| 2026-10-08 | Calibration v1.0.3 | Wired the two dormant depth systems into the live engine in-band. Disease: `IntegratedDiseaseSystem` injects via `MortalityModel.SetDiseaseMortalityRate`, anchor split by new `Health.SeaMortalityDiseaseShare` (0.30), measured disease 4.39%/cycle of a 14.88% total (was 0.00 / 15.76). Storms: `SimulationEngine.ApplyStormHazards` owns `SeaSite.NetIntegrityPct` wear/repair + `RecordEscape` (fine + event log); removed the duplicate block from `GameBridge.CheckWeatherImpacts`. Measured storm escapes 0.22%/site/yr, 0.16 events, min integrity 80.7% (was test-side 0.15% / 0.16 / 83.4). Re-ran `SimProbe` seeds 1-20; full solution suite green; `enableEvents:false` path unchanged and deterministic; bumped `calibration.json` to 1.0.3. |
| 2026-10-14 | wave1/sim-1 (S1-01, S1-02) | F1024 water chemistry merged; seawater O2 solubility (Benson & Krause, 9.02 mg/L at 10 °C/35 PSU, was 10.9); pH baseline 8.05; zones take the sea temperature; F456 storm season renormalized through one helper shared with the forecast; storm damage once per episode; net repair 0.2 -> 0.5; storm cooling Apr-Sep only. Re-measured every band (§1b, §1c updated, before/after in §1d); no band loosened. |
| 2026-10-14 | wave1/sim-2 (S1-03, D16) | Achievement and tutorial reward cash removed (medals); per-scenario bankruptcy floors in calibration v1.0.5 from measured harvest-only troughs (po3 -15M, isa -24M, algae -26.5M, krise -4.5M, lofoten -2M, default -1M) with a three-rung warning ladder; resource-rent tax on by default (family farm pays 0, a six-site career 11-30M over three years). SimProbe medians unchanged; tutorial still 6 of 6, four seeds finish later (§1e). |
| 2026-10-14 | wave2/sim-b-3 (S2-04, F270) | Harvest pipeline (calibration v1.0.8 `harvest`, save v6): fish sold as gutted kilos (yield 0.84) at the reference price × size class (HOG ladder, Kontali) × grade (superior/ordinary/production by cause, Fiskehelserapporten 2024) − the harvest cost (5.0 NOK/kg HOG, COGS, F270 closed); fasting in degree-days, wellboat slot, slaughterhouse capacity 150 t HOG/day, partial and per-pen orders, land hall / offshore routes. Player-path EBIT/kg HOG median 18.19 (11.07-23.23, 10 seeds); po3 careers +14.75M to +19.90M (were +23.49M to +31.76M); forste_sesong 36,000 fish and 4M (6/6 seeds win); rent base excludes the harvest cost (§1h). |
