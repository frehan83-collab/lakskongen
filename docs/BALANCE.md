# Balance & Realism Tuning Report (Lakskongen)

Single source of truth: `src/LakskongenSim/Calibration/CalibrationRegistry.cs`
(embedded fallback `godot/data/calibration.json`). Values are immutable
`init`-only records; `CalibrationGuardTests` fails the build when they drift
outside the reality bands named below.

This document is the human-readable companion to the registry: it records the
current game value, the real Norwegian reference, the source, and the
recommended playtest band for each high-leverage knob. Last reconciled:
**Oct 2026**.

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
below against the reference bands in this document. Measured **Oct 2026,
calibration v1.0.2** (fully deterministic: the engine seeds growth, weather,
market O-U and market intelligence RNG from the same seed).

Scenario `Baseline`: one 50k-smolt sea site, storyteller events off, no zone.

| Metric | Measured min / **median** / p90 | Reference band | Verdict |
|---|---|---|---|
| Mean economic FCR | 1.20 / **1.20** / 1.21 | 1.10-1.30 (bFCR 1.15) | IN BAND |
| Sea mortality %/cycle | 14.19 / **15.76** / 17.82 | 14-16 (NVI 2024) | IN BAND |
| Mean harvest weight kg | 5.20 / **5.20** / 5.20 | 4.5-5.5 | IN BAND |
| Feed cost NOK/kg produced | 26.65 / **26.76** / 26.96 | 22-30 (~25.7) | IN BAND |
| Realized price NOK/kg | 80.89 / **88.00** / 88.00 | 74-90 | IN BAND |
| Peak adult-female lice/fish | 0.36 / **0.49** / 0.76 | ~0.5 action limit | IN BAND (median at limit) |
| Escapes/year | 0.00 / **0.00** / 0.00 | low (<=2 guard) | IN BAND |

Scenario `HighStocking` (100k smolt) is statistically identical on these
per-fish/per-cycle metrics — FCR 1.20, mortality 15.75%/cycle, harvest 5.20 kg,
feed cost 26.76 NOK/kg, realized price 88.0, lice 0.49 — confirming the bands
hold as farm size changes.

Note on the tails: the realized-price p90 sits at the normal-play band ceiling
(88 NOK/kg) rather than the hard ceiling; the lice p90 (0.76) exceeds the 0.5
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

### Tuning applied (calibration v1.0.1)

- `FeedSystem.FCRTempSensitivity` **0.018 -> 0.008 °C⁻¹** (and the reconciled
  `Feed.FCRTempSensitivity` in `CalibrationRegistry`/`calibration.json`).
  At 0.018 the seeded economic-FCR median was ~1.24 and feed cost ~27.7; at
  0.008 it centres on **1.20** and **26.8 NOK/kg produced**, versus the
  bFCR 1.15 / 25.7 NOK/kg references. Runtime authority is the `FeedSystem`
  constant; the registry field is the reconciled record.

## 1c. Extended probe — disease, escapes, multi-site economics (seeds 1-20)

`SimProbe` now also sweeps four extension scenarios, all pure in-process and
deterministic for a given seed. Measured **Oct 2026, calibration v1.0.2**.

### Disease-cycle mortality (`SimScenario.DiseaseStress`)

`RunDisease` initialises with `enableEvents:true` (the disease subsystem live),
silences the random storyteller shocks so the only mortality difference versus
the deterministic baseline is the disease term, forces the disease LOD high, and
measures disease-attributable deaths exactly from the injected per-population
rate (`MortalityModel.GetSaveSnapshot`): for a one-day step
`survivors(without) - survivors(with) = count x diseaseRate`.

| Metric | Measured min / **median** / p90 | Reference / guard | Verdict |
|---|---|---|---|
| Disease mortality %/cycle | 0.00 / **0.00** / 0.00 | <=2 (additive guard) | DORMANT (see note) |
| Total sea mortality %/cycle | 14.19 / **15.76** / 17.82 | 14-16 (NVI 2024) | IN BAND |

**Note (engine deferral, see §5):** the additive disease subsystem is
effectively dormant. The stochastic engine's base SIR is seeded at 1e-6
prevalence and the high-fidelity path does not inject its mortality into
`MortalityModel`, so the measured *additive* term is 0. The real disease burden
is **not** missing from the game: NVI's all-cause 15.4% sea mortality (14-16%
band) is already anchored in `Health.SeaMortalityPerCycle` and reproduced by the
baseline total (15.76%). The guard exists to catch an accidental outbreak
pushing the additive term beyond the calibrated anchor.

### Storm escapes (`SimScenario.Storm`)

`RunStorm` runs the seeded weather and applies a documented **test-side
containment-risk model** through the engine's own `SeaSite.NetIntegrityPct` and
`SeaSite.RecordEscape` (the live engine does not yet drive sea-site storm
escapes; only `OffshoreFarm` has a live escape path — the same deferral pattern
as disease). Each cycle starts with fresh nets (integrity 100). Ordinary storm
days cost 0.3 integrity, severe days 1.0, with 0.2/day repair; on severe days a
containment failure occurs with expectation 1% and releases, at full wear, 5% of
the pen — so escapes stay rare and low.

| Metric | Measured min / **median** / p90 | Reference / guard | Verdict |
|---|---|---|---|
| Escapes / site / year (fish) | 10.6 / **25.0** / 39.1 | low | IN BAND |
| Escape events / site / year | 0.12 / **0.16** / 0.21 | <=1 guard | IN BAND |
| Escapes % of stock / site / year | 0.06 / **0.15** / 0.30 | <=0.5% guard | IN BAND |
| Storm days / year | 97 / **118.7** / 128.7 | scenario exposure proof | OK |
| Min net integrity % | 72.1 / **83.4** / 89.5 | >=40 (SeaSite risk line) | IN BAND |

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
| Gross revenue NOK (3 sites) | 70.0M / **94.8M** / 112.8M | design | OK |
| Total OPEX NOK (3 sites) | 67.3M / **76.1M** / 83.4M | design | OK |
| **EBIT NOK/kg** | 2.31 / **16.43** / 22.13 | cyclical 8-25 | IN BAND |
| Cost NOK/kg produced | 62.96 / **69.58** / 79.58 | farm-gate, feed-dominant | IN BAND |
| Profit NOK / site | 0.65M / **6.09M** / 9.78M | positive | OK |
| Harvested kg / site | 273.7k / **366.2k** / 441.5k | ~50k x 5.2 kg x cycles | OK |

Cost stack per harvested kg: feed 25.645 (price x FCR), smolt 12/fish, sea labour
(7 workers/site x 48k/month), site maintenance (0.01%/day of build cost) and
wellboat harvest 4.0 — matching `docs/BALANCE.md` §2. Real mapping: the measured
median EBIT/kg **16.4** sits inside the SalMar/Mowi cyclical **8-25** band
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

## 5. Resolved engine coupling (v1.0.2)

### Measured-band gaps resolved (from §1b, Oct 2026)

The three metrics previously deferred to the engine owner are now wired into
`SimulationEngine` / the biology and market models it owns:

- **Sea mortality 10.8% -> 15.8%/cycle.** `MortalityModel` now reads
  `CalibrationRegistry.Health.SeaMortalityPerCycle` and maps the whole-cycle
  fraction to a per-day sea hazard
  (`1 - (1 - perCycle)^(1 / GrowthToHarvestDays)`) for the Seawater/Harvest
  stages; freshwater stages keep `GameConstants.BaselineMortalityPerDay`.
  `SimulationEngine.ApplyBalancingConfig` pushes the registry anchor in, and
  disease/event mortality remains additive, so `enableEvents:false` stays
  deterministic and on-band.
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
- **Disease-attributable mortality is dormant.** `IntegratedDiseaseSystem`
  builds its own `DiseaseSystem` base SIR but `SetMortalityModel` wires the
  `MortalityModel` only into the LOD system, not the base system; and the base
  SIR starts from a 1e-6 prevalence seed. The high-fidelity path therefore never
  injects disease mortality, so the measured additive term is 0 (§1c). The
  all-cause disease burden is still represented via the calibrated
  `Health.SeaMortalityPerCycle` anchor. Recommended (engine): call
  `_baseSystem.SetMortalityModel(...)` and drive a realistic exogenous infection
  ramp, then re-tune `DiseaseSystem.ExtraMortalityRatePerDay`.
- **Sea-site storm escapes are not engine-driven.** `SeaSite.NetIntegrityPct`
  and `SeaSite.RecordEscape` exist and are documented ("Storms degrade; below 40
  during storms risks escapes") but no engine code degrades integrity or records
  a sea-site escape; only `OffshoreFarm` has a live escape path. §1c measures a
  documented **test-side** containment-risk model through the same public
  counters. Recommended (engine): decrement `NetIntegrityPct` on storm days and
  call `RecordEscape`, gated at the 40% threshold.

## 6. Guard coverage

`CalibrationGuardTests` enforces the bands above. Feed-path specific guards:

- `Feed_MatchesRealReferences` — FCR band + feed price band + FCR anchor
  equality with `Biology.OptimalFCR`.
- `Feed_CostModel_PriceTimesFcrMatchesRealFeedCostPerKgProduced` — registry
  derived cost within 25.7 ± 4 and > 20 (anti-regression to flat 12.5).
- `FeedSystem_Defaults_MatchRegistry_NoDrift` — a bare `new FeedSystem()` must
  match registry price/FCR and the derived cost (guards the 13.5 / 1.12
  default regression).

## Review log

| Date | Reviewer | Changes |
|---|---|---|
| 2026-10-07 | Balance consolidation | FeedSystem defaults now sourced from CalibrationRegistry; added standalone-drift guard; report created; feed row in REALISM_CHECKLIST reconciled. |
| 2026-10-08 | Balance harness | Added `SimProbe` + `BalanceReportTests` (seeds 1-20, 3-year grow-out cycles); measured-band table (§1b); tuned `FCRTempSensitivity` 0.018 -> 0.008 (v1.0.1); recorded mortality/price/lice deferrals (§5). |
| 2026-10-08 | Calibration v1.0.2 | Wired `Health.SeaMortalityPerCycle` into `MortalityModel` (per-cycle -> per-day; 10.8 -> 15.8%); sourced `MarketSimulation` O-U from `CalibrationRegistry.Market`, added supply/demand mean-reversion + normal-play band, seeded market intelligence RNG (153 -> 88.0 NOK/kg); seeded seasonal external lice pressure (0.00 -> 0.49/fish). Re-ran `SimProbe` seeds 1-20 both scenarios; full suite green (707 passed). Updated `BalanceReportTests` bands from deferred to the real references; bumped `calibration.json` to 1.0.2. |
| 2026-10-08 | Extended probe | Added disease-cycle, storm-escape, multi-site economics and lice-control scenarios to `SimProbe` + `BalanceReportTests` (§1c). Measured disease term 0.00%/cycle (dormant; engine deferral recorded in §5), storm escapes 0.15%/site/yr vs 0.5 guard, multi-site EBIT/kg 16.43 (in the 8-25 cyclical band), cost 69.6 NOK/kg produced, 1.5 treatments/cycle with 0.07% per-cycle resistance. Documented the test-side containment-risk model and the farm-gate cost stack. |
