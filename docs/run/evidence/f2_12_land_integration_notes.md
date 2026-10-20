# F2-12 land physics: notes for the wiring stage

_Branch `wave3/sim-lanes-1`, 2026-10-15. Stage 1 of 2 of F2-12 (docs/FARM_TYPES_SPEC.md §3.3, §7.1).
Stage 1 shipped the land unit's physics as pure, standalone models; stage 2 wires them into the
engine once F2-02 (hooks), F2-03 (plots, `UnitResolver`, the `RASFacility` fields) and F2-04
(facilities calibration and its include loader) have merged. Measurements: docs/BALANCE.md §1p.
Decisions: docs/RUN_LOG.md §7 (2026-10-15 F2-12 rows); open findings: §8._

## 1. What exists (all new files, no engine edits)

| File | What it is |
|---|---|
| `src/LakskongenSim/Calibration/LandCalibration.cs` | `LandCalibration` and its records: four `LandTemplateCalibration`s (`Hfs`, `HfsPilot`, `Ras`, `PostSmolt`), `Intake`, `Oxygen`, `Co2`, `Tan`, `Density`, `Energy`, `Production`. Every leaf documents unit, `Range:` and exactly one `Source:`/`Game balance:` (held by `LandCalibrationTests` with Roslyn). `LandCalibration.Parse(json)` reads the draft; `For(LandTemplate)` picks a template. |
| `godot/data/calibration/facilities_land.json` | The draft include file, generated from the C# defaults, keys sorted; `LandCalibrationTests` hold file = code with unmapped keys disallowed. |
| `Farm/Land/LandTank.cs` | `LandTank` (wraps an S2-01b `Pen`: id `"<site>/tNN"`, number, volume π(D/2)²·depth, `PopulationId`; plus role, diameter, depth, bio-zone, and the day's water: `TemperatureC`, `DoMgL`, `Co2MgL`, `TanMgL`, `No3NMgL`, `AppetiteFactor`), `TankRole`, `BiofilterType`. |
| `Farm/Land/LandLot.cs` | `LandLot` (id `"<site>/lotN"`, template, tanks, `HeatPumpMWe`, `HeatRecovery`, `Biofilter`, `BiofilterMaturity`), `LandTemplate`. |
| `Farm/Land/LandSiteTemplates.cs` | `BuildLot(siteId, lotIndex, template, calibration, firstTankNumber)`, `DefaultStrategy`, `DefaultBiofilter`, `TanksAreSeparateZones`, `BiofilterMaturity(days, template, tan)`. |
| `Farm/Land/LandWaterModel.cs` | `LandWaterStrategy` (the Vannstrategi: `Reuse`, `DeepIntakeShare`, `SetpointC`, `BlendTowardSetpoint`), `LandIntakeSite` (the plot's intake side), `LandFlows`, `TankLoad`, `TankFlows`, `TankWater`; pure functions `Flows`, `ShareOf`, `DeepShare`, `IntakeTemperatureC`, `SolveTank`, `Co2AppetiteFactor`, `TanAppetiteFactor`, `DensityGrowthMultiplier`. |
| `Farm/Land/LandEnergyModel.cs` | `LandEnergyDay` (intake, loop, treatment, UV, heating, cooling, base, O2 generator; `LoxUpstreamKWh` off the meter), `LandHeatBalance`; `PumpingKWh`, `LineFrictionM`, `IntakePumpingKWh`, `LoopPumpingKWh`, `TreatmentKWh`, `UvKWh`, `HeatBalance`, `MetabolicHeatKWh`, `OxygenProductionKWh`. |
| `Farm/Land/LandLotDaySolver.cs` | `Solve(LandLotDayInput, LandCalibration) → LandLotDayResult` (pure: flows, blend, heat balance, every stocked tank's water, oxygen need and injection, LOX bought, energy) and `Apply(lot, result)` (writes the tanks' water). |
| `Farm/Land/LandStockingPlanner.cs` | `LandStockingPlan`, `LandStockingForecast`, `LandStockingVerdict`; `Project(law, plan)`, `Check(law, plan, volume, smolts, maxDensity)` → `density_exceeded` / `out_of_range`. |
| `Farm/Land/LandLotProjector.cs` | `LandLotProjector.Run(settings, calibration[, law])`: a pure year (or any window) of one lot, biology then land tick, as the engine orders them. The anchors, the sweep and later the Vannstrategi preview run on it. |

Nothing draws a random number. The spec's 10 uniforms per facility per day (`ras_facility` + id)
belong to the incident layer (F2-16); the physics here must stay draw-free so previews never move a
stream (spec §3.7).

## 2. Where each model plugs in (F2-02's `LandHooks` and the land tick)

| Engine seam (spec §3.3 hook table, current call site) | What the land wiring returns or does |
|---|---|
| `UnitWaterTemperatureC(pop)` and `SimulationEngine.WaterOf` (`Core/SimulationEngine.Holdings.cs`, `case HoldingKind.LandTank`) | The tank's `HoldingWater`: `TemperatureC = tank.TemperatureC`, `OxygenMgPerL = tank.DoMgL`, salinity = the intake's (35 PSU; F2-13's `SalinityPpt` for post-smolt), `ProductionArea` = the plot's area (F2-03), not `HomeProductionArea`. Today's body reads `RASFacility.CurrentWaterTemp`/`OxygenLevel` (one value per facility, from the legacy `TickDay` noise). |
| `AppetiteFactorFor(pop)` (Biology feeding, `InDayTickSystems.cs` ~l.160-185, the `waterPenalty` → `HoldingHusbandry.AppetiteModifier`) | Multiply by `tank.AppetiteFactor` (CO2 × TAN, written by yesterday's land tick). Oxygen stays on the engine's own DO-saturation path through the tank's `DoMgL`, so a LOX shortage bites through the water, not twice. |
| `FeedAvailableFor(pop)` | `true` on land (no silo model); the LOX store is an oxygen limit, handled by `OxygenAvailableKg`. |
| `ResolveCohortEnvironment` / `EnvironmentOf` (`Holdings.cs`: `co2 = holding.LandFacility?.CO2Level`) | `tank.Co2MgL`, so F2-15/F2-16's hazards read the tank, not the facility. Wave height 0. |
| Density stress (`InDayTickSystems.cs` ~l.144-170: `WaterQuality.CalculateStockingDensityModifier`, the **sea** curve from 25 kg/m³; `DensityVolumeM3` ~ `holding.Pen`) | **Needs a hook F2-02's table does not list.** A land tank at 60 kg/m³ would take the sea penalty. Proposal: `UnitDensityGrowthMultiplier(pop, densityKgM3)` with today's sea curve as the default and `LandWaterModel.DensityGrowthMultiplier` (1 to 50 kg/m³, 0.85 at 85) on land; `HoldingOf` must carry the tank's `Pen` (`PopulationHolding.Pen = tank.Pen`) so the density is the tank's. The FCR's density term (`FeedSystem.SatiationFcr(..., stockingDensityKgM3, ...)`) is sea-calibrated too: pass the land FCR (`LandProductionCalibration.BiologicalFcrAt2Kg` × the law's size factor) through the same hook or F2-15's. |
| `DayLengthHoursOf` (`SimulationEngine.Growth.cs`) | Already 24 h for `LandTank` (F2-16's short-day photoperiod changes it). |
| `UnitMortalityTerms(pop)` | Nothing from F2-12 (F2-15 growth shortfall, F2-16 O2/H2S/power). |
| `IsResourceRentLiable` | Land never (F2-04's base). |
| `QualityMortalityMultiplier`, `SmoltTierCostPerFish` | F2-13. |
| **`LandSiteTickSystem`** (FarmOpex phase; today the `RASFacility.TickDay` loop in `InDayTickSystems.cs` ~l.229-235) | Per facility, per lot: build `TankLoad`s in tank order (biomass from the tank's population's cohorts, feed = what Biology booked for that population **today**: expose the `FeedResult.FeedUsedKg` of `GrowAndFeedOneDay` per population, e.g. a per-step map, since Biology runs first); `LandLotDaySolver.Solve(new LandLotDayInput(lot, strategy, site, shallowC, loads, salinity, biofilterHealth, loxOnHand, onSiteGenerator), calibration)`; `Apply`; book `Energy.TotalKWh × area power price` and `LoxPurchasedKg × LoxPriceNokPerKg` as `CostBooking.Opex`; Scope 2 = `TotalKWh` × the CarbonLedger grid factor, Scope 3 = `LoxUpstreamKWh` (spec §3.6); mirror lot means into the legacy `RASFacility.CurrentWaterTemp`, `OxygenLevel`, `CO2Level`, `DailyEnergyKWh` for the old panels; skip the legacy `TickDay` energy/oxygen/CO2 formulas for F2 units (they assume 50,000 kWh/day and 4 g O2/kg/h). |
| `StockLandTank` (spec §3.4, `.LandSites` partial) | `LandStockingPlanner.Check(Growth.Law, LandStockingPlan.For(template, production) with { TemperatureC = strategy.SetpointC, GrowthMultiplier = strain × smolt tier }, tank.VolumeM3, count, template.MaxDensityKgM3)`; refuse with `verdict.Reason` (`density_exceeded`); `verdict.MaxSmolts` is the preview's room. Then S2-01b's pen bookkeeping on `tank.Pen`. |
| `SetVannstrategi` | Maps to `RASFacility.RecirculationRate` (r), a new `DeepIntakeShare` (w), `TargetWaterTemp` (setpoint) and a new blend-toward-setpoint flag; clamp r to `Intake.MaxReuse`. Offer blending toward the setpoint as a preset: on pad_a it beats every fixed blend (BALANCE §1p). |
| `InstallHeatRecovery(lotId)` | `lot.HeatRecovery = true` (heating then runs at `CopWithHeatRecovery`). |

**Inputs the wiring must supply per day:** the shallow intake temperature (the zone's sea, S2-02,
anomaly included: `SeaTemperatureOf(zone)`), the deep water (calibration, 10 °C), the plot
(`LandIntakeSite`: `StaticLiftM` and `PumpCapacityM3sPerLot` from `site_plots.json`,
`DeepLineKm = IntakeReachM / 1000` (F2-19), `ShallowLineKm` = distance to 25 m water (0 where the
quay stands in it; pad_a measures 1 m), `Disinfection = true` for every plant built in the game
([L:L2]: new plants must disinfect from 2025)), biofilter health (`RASFacility.BiofilterEfficiency`),
biofilter maturity (`LandSiteTemplates.BiofilterMaturity(days since commissioning)`), LOX on hand
(F2-16's store; `+∞` until then) and the power price.

## 3. State to save (F2-03 adds the fields; the relinker restores them)

`RASFacility.Lots` (`List<LandLot>`), per lot `HeatPumpMWe`, `HeatRecovery`, `Biofilter`,
`BiofilterMaturity`; per tank `Pen` (id, number, volume, `PopulationId`), role, diameter, depth,
bio-zone and the water fields (`TemperatureC`, `DoMgL`, `Co2MgL`, `TanMgL`, `No3NMgL`,
`AppetiteFactor`: the appetite is read the next day, so it must survive a save to keep save/load
bit-identical). The strategy fields on `RASFacility` (r, w, setpoint, blend flag). Nitrate relaxes
day to day (the only state with memory in the water model); everything else is a steady state of
the day's inputs.

## 4. Calibration (F2-04)

`FacilitiesCalibration` gains `Land` (`LandCalibration`), read from
`calibration/facilities_land.json` by the include loader; drop `LandCalibration.Parse` then or
keep it as the loader's leaf. Convert each leaf's doc provenance to `[Range]` and one of
`[Source]`/`[GameBalance]` (the text is already there), and fold `LandCalibrationTests`' doc guard
into `CalibrationGuardTests`' reflection guard. Bump the registry minor version at merge.
`LandEnergyCalibration.PowerPriceNokPerKWh` is a stand-in for the area's power price (S2-02).

## 5. Determinism and goldens

The models are pure, so the wiring changes the game only where land facilities exist. No shipped
scenario starts with one (`scenarios.json` has none), so the shipped-scenario goldens should stay
identical (spec §7.1 F2-12 #8); `save_v8_facilities.json` (one RAS facility with 2 stocked tanks)
changes once F2-03 migrates it onto lots. Keep the land tick's draws in their own stream and
fixed in number (spec §3.7).

## 6. Re-measure when these land

- **F2-15** (cohort plans, competence, FCR): the production index (115 kg/m³/yr now, single cohort
  per tank) moves kWh/kg (energy scales with stocked volume); re-run
  `LandEnergyModelTests.F2_12_Acceptance1_*` and the sweep.
- **F2-13** (post-smolt salinity): `SolveTank` takes salinity already (O2 saturation); the module's
  energy is 3.61 kWh/kg now (band 3.4-5.4).
- **F2-16** (incidents): LOX store and deliveries feed `OxygenAvailableKg`; a short supply already
  lowers `DoMgL` by the closed-form balance (`LandWaterModelTests.Oxygen_WhenLiquidOxygenRunsShort_*`).
- A **live-state projection** for the UI previews (F2-32 "equal to the engine projection ± 3 %")
  needs a `LandLotProjector` overload that starts from a facility's tanks instead of staggered empty
  tanks.
