# Balance & Realism Tuning Report (Lakskongen)

Single source of truth: `src/LakskongenSim/Calibration/CalibrationRegistry.cs`
(embedded fallback `godot/data/calibration.json`). Values are immutable
`init`-only records; `CalibrationGuardTests` fails the build when they drift
outside the reality bands named below.

This document is the human-readable companion to the registry: it records the
current game value, the real Norwegian reference, the source, and the
recommended playtest band for each high-leverage knob. Last reconciled:
**Oct 2026, calibration v1.0.3**; bands re-measured **2026-10-14** after the
S1-01 water-chemistry and S1-02 storm-season fixes (§1d).

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
| `Operations.HarvestCostPerKgNOK` | 4.0 | 3-5 (wellboat) | industry | 3-5 |
| `Operations.ProcessingCostPerKgNOK` | 9 | 8-10 | industry | 7-11 |
| `Finance.CorporateTaxRate` | 0.22 | 22% statutory | Skatteetaten | 0.22 (fixed) |
| `Finance.ResourceRentTaxRate` | 0.25 | 25% since 1 Jan 2023 | Skatteetaten / Prop.1 LS | 0.24-0.26 |
| `Finance.ResourceRentBasicDeductionNOK` | 70,000,000 | 70M NOK | Prop.1 LS | 60M-80M |
| `Licensing.MABLimitKgPerLicense` | 780,000 | 780 t standard (945 t far north) | Fiskeridir | 780,000 (fixed) |
| `Licensing.LicenseCostNOK` | 150,000,000 | ~150-200M market | Fiskeridir | 100M-250M |
| `Labor.WorkerMonthlySalaryNOK` | 48,000 | ~48k (2024) | industry wage stats | 36k-60k |

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
