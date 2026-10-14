# Farm types: codebase map (2026-10-14, integration/wave1 @ 90a0cf0)

What the sim and the Godot frontend already had for land-based and offshore farming when the F2 spec was written.

## code-sim

# Lakskongen sim: farm types beyond open net pens

Repo root for every reference below: `/Users/fredrik.j.hansen/lakskongen` (integration/wave1, read-only). C# sim is `src/LakskongenSim`.

## 0. Verdict

The sim already has three engine-owned facility kinds next to `SeaSite`: `RASFacility` (land-based), `OffshoreFarm` and `PostSmoltFacility`. It also has a six-rung containment ladder on sea sites. All of them are built, ticked, saved and covered by tests. Most of their own physics is decoration, though:

- Offshore storm effects (escapes, feeding stop, structural damage) are computed and then thrown away.
- RAS water chemistry has no effect on the fish.
- RAS scale only changes CAPEX.
- The post-smolt UI verbs are fake bridge stubs.
- Facility fish carry `ZoneId ""` and no `SiteId` in `SeaSites`. That puts them outside lice, delousing, wellboats, traffic light, ISA, sludge and escape accounting.

There are two parallel production-unit models: `SeaSite` and the facility classes.

## 1. Core data model

**Site.** `SeaSite` (`Farm/SeaSite.cs`):
- `Pens: List<FishPopulation>` (:26). One entry is added per stocking call. It is not a physical pen. `NumberOfPens=8` and `PenVolume=50,000 m³` (:112-113) are separate scalars used only for density and volume.
- `MABTonnes=780` (:29). Live `CurrentBiomassTonnes` (:32).
- Containment, skirt, barrier, gill, net integrity, sludge, `CarbonTracker` and a per-site `SeaLiceModel Lice` (:122), which is serialized.
- `Stock()` sets the population's `ZoneId` and `SiteId` (:155-167).

**Pen and cohorts.** `FishPopulation` (`Biology/FishPopulation.cs`):
- `Id = Guid.NewGuid()` (:45). This Guid is allowlisted in the determinism ban.
- `Cohorts`, `ZoneId` (:65), `SeaLiceLoad` (:80), `SiteId` (:86).
- `Cohort` (:18-31) holds `AgeDays`, `Count`, `MeanWeightKg`, `Stage`, `Genetics` and `Quality` (`SmoltQualityTier`).
- Stage is derived from age (:161-171): Smolt below 365 days, Seawater below 730 days, Harvest after that.

**Canonical list.** `GameState.Populations` is the one list the biology ticks. `SeaSite.Pens` and the facility `Tanks`/`Pens` hold the same instances. After a load they are re-linked by Id (`Core/GameStateRelinker.cs:22-95`, with facilities at :66-70).

**Verbs.** They return `CommandResult` (`Core/SimulationEngine.Commands.cs:21-57`):
- `StockSmolts` (:97-173): MAB hard gate at :116. Cost is `SmoltCostPerFish(12) × strain.CostMultiplier` (:122). The quality tier is free (exploit, see 3.7).
- `HarvestSite` → `HarvestPopulations` (:207-269) → `SellHarvest` (:277-328). Contracts are filled first, then spot, then VAP. `seaFarmed` feeds the resource-rent base (:321).
- `DelouseSite` (:514) and `RequestWellboat` (:577; harvest, delouse, rov, freshwater, with 2-5 day delays) work on sea sites only.
- Facilities: `Core/SimulationEngine.Facilities.cs` (Build ×3) and `Core/SimulationEngine.FacilityProduction.cs` (Stock, Harvest, PostSmolt verbs).

**Buying a new sea site** (`Core/SimulationEngine.Licence.cs`):
- `CanBuildSeaSite` (:128-146): the first site is free; any zone you already operate in is free; a new zone needs a won growth licence.
- `ApplyForGrowthLicence` (:46) and `BidOnLicenseAuction` (:91) use `Finance/LicenseAuctionSystem.cs`: 7-day auctions, 150M NOK reserve, rivals bid from cash.
- `BuildSeaSite` (:153-193) costs 6M NOK and creates 780 t MAB with 8 × 50,000 m³ pens.
- `AcquireDistressedAsset` (:403) transfers a rival's site and MAB.
- **Facilities bypass all licence gating.**

**Tick order.** `_inDayPhases` (`Core/SimulationEngine.cs:505-530`) runs in this order: Weather → HealthPrep (site lice :2543-2581) → Biology → FarmOpex (facility TickDay) → CarbonLedger → … → SludgeValorization → TrafficLight → … Post-tick phases are storm impacts, wellboat execution, licence auctions, balance mirror and solvency (`Core/SimulationEngine.PostTick.cs`).

## 2. Determinism, save format, tests

**Determinism**
- `Core/SimRng.cs` uses SplitMix64 with FNV-1a names. Scopes are frozen constants: `offshore_farm` (:152), `ras_facility` (:155) and others.
- Per-identity draws must use suffix overloads. Facilities are bound each tick with `Rng.Stream(scope, facility.Id)` (`Core/InDayTickSystems.cs:281, 289`).
- Stream positions are saved as `runtime.SimStreamPositions` (`Core/SimulationEngine.cs:3390`, import :3891). The seed is in `GameState.RandomSeed` (:282).
- The Roslyn ban `src/LakskongenSim.Tests/Core/DeterminismScanner.cs:35-55` blocks `DateTime.Now/UtcNow/Today`, `DateTimeOffset`, `Random.Shared`, `new Random()`, `Guid.NewGuid`, `Environment` and `RandomNumberGenerator`. Exceptions live in `src/LakskongenSim.Tests/DeterminismAllowlist.txt`, which includes `FishPopulation.Id`.
- `SmoltBatch.StartDay` was moved off `DateTime.Now`. Facility ids are deterministic: `prefix_N` (`Core/SimulationEngine.Facilities.cs:125-131`).
- `PostSmoltFacility` has no RNG. `CohortTransferSystem` receives a `Random` seeded from a stream but never uses it; losses are deterministic.

**Save format**
- One serializer, `Core/SaveJson.cs:25-36`: System.Text.Json, enum names, case-insensitive, `IncludeFields`.
- The whole `GameState` is serialized. Facilities live in `GameState.RASFacilities/OffshoreFarms/PostSmoltFacilities` (`Core/GameState.cs:37-43`), so they persist automatically.
- `GameState.Runtime` (`EngineRuntimeSnapshot`, :339) carries tick clocks and RNG positions.
- Library loader: `Core/SaveLoadManager.cs:132-196`, which null-guards, clamps undefined `Cohort.Quality` and re-links.
- Game wrapper: `godot/scripts/autoload/SaveLoadManager.cs`, `CurrentSaveVersion=3` (:41). `MigrateSaveData` (:555-584) only bumps the number; every change so far has been additive with defaults.
- Load path: `AdoptSavedState` (`Core/SimulationEngine.Commands.cs:641`, facilities :722-774) → `RestoreFromLoad` (`Core/SimulationEngine.cs:3770`), with the containment migration at :3808-3819.
- New farm kinds stay save-safe if they are additive lists or fields with defaults. Avoid polymorphic lists unless you add `[JsonDerivedType]`.

**Tests**
- The solution `LakskongenSim.sln` uses xUnit on net10.0. Projects:
  - `src/LakskongenSim.Tests`: about 105 files, foldered by domain (Core, Environment, Farm, Finance, Integration, …).
  - `src/LakskongenSim.SaveLoadTests`
  - `src/LakskongenSim.LogisticsTests`
  - `tests/Health`, `tests/Persistence`, `tests/Processing`
  - `src/StateHashProbe`
- Determinism suites: `Integration/EngineTwinRunDeterminismTests`, `CrossProcessDeterminismTests`, `ExactSaveHashTests`, and `SaveLoadMatrixTests` (:47; builds all three facility kinds at :129-138, then requires bit-identical continuation).
- Python static checks live in `tests/*.py`. `test_bridge_contract.py` keeps an `ORPHANED_PANELS` list (:244). Godot UI flow tests are in `godot/tests/UiFlowTests.gd`.

## 3. Component map

### 3.1 ContainmentLevel / ContainmentTech
`Farm/ContainmentTech.cs` defines the levels OpenNet, Skirt, SemiClosed, ClosedFloat, Submersible, OffshoreClosed (:12-20). The modifier table (:35-44):

| Level | Lice excluded | O2 demand | Storm | Escape | Growth | CAPEX | Weekly OPEX |
|---|---|---|---|---|---|---|---|
| OpenNet | 0 | 1.0 | 1.0 | 1.0 | 1.0 | 0 | 0 |
| Skirt | 0.35 | 1.05 | 1.1 | 0.6 | 1.0 | 0.5M | 14k |
| SemiClosed | 0.85 | 1.4 | 0.7 | 0.2 | 0.98 | 12M | 120k |
| ClosedFloat | 0.95 | 1.6 | 0.5 | 0.05 | 0.95 | 28M | 420k |
| Submersible | 0.90 | 1.6 | 0.3 | 0.3 | 0.97 | 55M | 540k |
| OffshoreClosed | 0.90 | 1.1 | 0.5 | 0.75 | 1.05 | 120M | 800k |

`MabCreditFor` in red zones (:50-58): ×1.03 / 1.06 / 1.06 / 1.10. `IsClosedContainment` means SemiClosed or above (:66).

**LIVE.** Where each modifier is used:
- O2 demand and growth: `Core/InDayTickSystems.cs:149-152, 204`
- Storm wear and escape: `Core/SimulationEngine.cs:1717-1741`
- Per-site lice blocking: `SimulationEngine.cs:3104-3113`, fed into site lice at :2578
- Weekly OPEX: :3090-3091
- Red-zone credit: :2889-2907
- Bloom: `Environment/BloomEventDeck.cs:40`
- Carbon electricity multipliers: `Farm/CarbonLedger.cs:53-63`
- Sludge collection: `Farm/SludgeValorization.cs:40-48`
- Scenario start: `Core/ScenarioSystem.cs:542-545`
- Tests: `Environment/ContainmentTests.cs`

**Problems**
- The upgrade verb is bridge-owned, not an engine command: `godot/scripts/autoload/GameBridge.Farm.cs:1183-1220`. It is UI-wired through `OffshorePanel.gd:267`.
- Upgrading after a red credit was applied leaves the flag set. On zone exit the engine then divides by the new level's credit (`SimulationEngine.cs:2903-2904`), so MAB drifts down.
- Red status only shrinks `zone.CarryingCapacityTonnes` (`Finance/TrafficLightSystem.cs:123`), which is display-only. So closed sites gain MAB in a red zone while open sites lose nothing.
- "OffshoreClosed" is a sea-site rung only. It has no link to `OffshoreFarm`.

### 3.2 EnvironmentalFlexibility (`Farm/EnvironmentalFlexibility.cs`)
- Pure static. Red-zone eligibility requires closed containment. `MabMultiplier = min(1, 0.94 + (credit − 1))` (:50-60).
- **Dead on the tick path.** It is used only by the bridge's `QualifiesForEnvironmentalFlexibility` (`GameBridge.Depth.cs:1489`) and by `Finance/FinanceDepthTests.cs:138-167`. `BarrierTech.IsEligibleForEnvironmentalFlexibility` always returns false (:214).
- Its formula disagrees with what the engine does inline (multiply site MAB by the credit, uncapped).

### 3.3 SeaSite.IsClosedContainment (`SeaSite.cs:43`)
- A legacy bool. The bridge sets it at ClosedFloat or above (`GameBridge.Farm.cs:1197`), and the load migration maps it to ClosedFloat.
- Read by the sustainability escape bonus (`Sustainability/SustainabilityManager.cs:249`; RAS does not count despite the comment) and an achievement (`Core/AchievementSystem.cs:355`).
- It disagrees with `ContainmentTech.IsClosedContainment`, which uses a SemiClosed threshold.

### 3.4 RASFacility, land-based (`Farm/RASFacility.cs`)

**Model**
- Scale capacity: Small 5,000, Medium 8,000, Large 20,000 t/yr (:236-242). The comments say Small is 3,000.
- CAPEX: 500M / 1.2B / 2B (:245-251). Construction takes 730 days.
- 12 tanks × 3,000 m³ (:32-35). Target water 12 °C ±0.15 noise from RNG (:146).
- O2 = `max(5, 8.5 − …)` and CO2 = `6 + biomass/volume × 0.5` with an alert above 15 (:150-159).
- Biofilter loses 0.001/day after 90 days, down to a floor of 0.70 (:162-170).
- Energy: 50,000 kWh/day × (1 + 0.05·|12 − ambient|) × 1.05 (UV) × 1.03 (ozone) × 0.80 NOK (:144-177).
- Staff 35 × 650k/yr. Consumables 0.02 NOK/kg/day. Water: 3% of 36,000 m³ × 5 NOK. Lice risk 0, escape risk 0.

**LIVE**
- Build `Core/SimulationEngine.Facilities.cs:20-45`; Stock `Core/SimulationEngine.FacilityProduction.cs:38-57` (density cap 60 kg/m³, :23 and :52); Harvest :60-69 (`seaFarmed:false`, so no resource rent).
- Tanks grow at facility water temperature (`InDayTickSystems.cs:114-130`). Exempt from sea-temperature stress (`SimulationEngine.cs:2153-2169`). OPEX booked (`InDayTickSystems.cs:279-285`, then `SimulationEngine.cs:2086`). Carbon Scope 2 (:2273-2278).
- Saved and re-linked. Tests: `Integration/FacilityProductionTests.cs` (RAS :41-160, save continuity :236), `PersistenceGapTests.cs:50-98`. UI: `godot/scripts/ui/RASPanel.gd`, which always builds "medium" (:175-178).

**Dead or broken**
- O2, CO2, pH and the biofilter never affect fish.
- `FCR=1.30` is unused; the generic feed model runs instead.
- `CapacityTonnesPerYear` is never enforced.
- Scale changes only CAPEX: tank count, volume, energy and staff are identical, so standing capacity is 2,160 t at every scale.
- `ZoneId` is never set, so zone is null: no environmental multiplier and no zone biomass.
- `PerformMaintenance()` and `EstimatedAnnualOPEX` are never called. The bridge `PerformRASMaintenance` (`GameBridge.Farm.cs:1674-1724`) charges 0.5-2M and resets chemistry, but not `DaysSinceLastMaintenance` or the biofilter. `NeedsMaintenance` therefore stays true forever.
- The SIR disease tick still runs on tanks at a fixed 10 °C (`Health/IntegratedDiseaseSystem.cs:146-150`).

### 3.5 OffshoreFarm (`Farm/OffshoreFarm.cs`)

**Model**
- Types: SemiSubmersible, ShipShaped, Submersible. CAPEX 700M / 900M / 1.2B (:228-242). Construction takes 1,095 days.
- 8,000 t capacity, 6 pens × 40,000 m³.
- `SeaLicePressure 0.3`, `WaterFlowRate 0.8`, `OxygenBoost 1.5`, `FCR 1.12`, `MaxWaveHeightM 12`.
- Storm lockdown when Storm or Hs > 6 m. Damage is 5% per metre above 12 m. Escape chance is 2% per metre above 8 m, up to 5% of fish, drawn from RNG (:128-161).
- Crew rotates every 14 days at 150k (:164-175).
- OPEX: 25 × 900k/yr staff, 80k/day energy, 2 NOK × capacity t/day maintenance, 50k/day logistics (:179-192). That totals about 208k/day, roughly 76M/yr.

**LIVE**
- Build (`Facilities.cs:48-72`), Stock (`FacilityProduction.cs:78-98`: `SeaLiceLoad = strain lice × 0.3`), Harvest (:101-110, `seaFarmed:true`, so rent applies).
- OPEX tick (`InDayTickSystems.cs:287-293`). Carbon: Scope 1 at 200 + 1 L/t, Scope 2 at 500 + 2 kWh/t, helicopter on rotation days, supply vessel (`SimulationEngine.cs:2281-2306`).
- Saved and re-linked. Tests: `FacilityProductionTests.cs:184-202`. UI: `godot/scripts/ui/OffshorePanel.gd` (build semi_submersible :209-211; stock 200,000 per press :159-165).

**Dead or broken**
- Of `OffshoreTickResult`, only `TotalDailyOPEX` is used (`InDayTickSystems.cs:291-292`). `FeedingPossible`, `FishEscaped`, `StructuralDamage` and `CrewMoralePenalty` are dropped. Lockdown does not stop feeding, and escapes are never removed or recorded.
- `RepairStructure()` is never called. The bridge `RepairOffshore` (`GameBridge.Farm.cs:1799-1837`) charges a flat 50M and restores nothing; it is not wired to the UI.
- Lice load is frozen at stocking, with no lice model and no delousing. ZoneId is empty, so the farm is outside the traffic light. The ISA event can never reach it (`Events/NorwegianEvents.cs:54-55`).
- Wave input is the single fjord Hs (`Environment/WeatherSystem.cs:357-382`: Storm at most 7 m, Severe at most 15 m). The 12 m rating is practically never exceeded.
- Pen count, pen volume, flow, O2 boost and FCR are unused. `Submersible` and `ShipShaped` differ only in CAPEX.

### 3.6 PostSmoltFacility (`Farm/PostSmoltFacility.cs`)

**Model**
- 10M smolts held at once. Target transfer weight 300 g. 13 °C.
- Growth: 1.5% body weight/day × 13/12, giving 80→300 g in about 82 days.
- Survival 0.97 spread over 180 days. Feed = biomass × 0.015 × FCR 0.95 at 12 NOK/kg.
- Vaccine 3.5 NOK/fish. Staff 20 × 600k/yr, energy 30k/day. CAPEX 250M per 10M smolts. Construction takes 548 days (:108-151, :197-201).
- Batches are `SmoltBatch` objects, not populations.

**LIVE in the engine**
- `BuildPostSmoltFacility` (`Facilities.cs:75-96`), `StartPostSmoltBatch` (`FacilityProduction.cs:120-147`), `VaccinatePostSmoltBatch` (:150-166).
- `TransferPostSmoltToSea` (:173-217): MAB-checked. Quality tier is SuperSmolt at 400 g or more and Large at 200 g or more (:26-29, :196-198). The cohort is aged 365 + days in the facility.
- OPEX tick: `InDayTickSystems.cs:295-299`. Tests: `FacilityProductionTests.cs:204-234`.

**Not reachable from the game**
- The bridge verbs `StartSmoltBatch`, `VaccinateBatch` and `TransferBatchToSea` (`GameBridge.Farm.cs:1987, 2037, 2075`) charge money and log events but never call the engine. They use 15 NOK/smolt, a hard-coded 100,000 fish (:2046), a 2M transfer fee and send to the first sea site (:2104).
- There is no bridge build verb and no UI. Roadmap S4-11 already tracks this.
- Photoperiod, short-day, `VaccinationRecoveryDays` and `TankSections` are unused. `TargetTransferWeightG` cannot be set, so SuperSmolt is unreachable.
- Transfer has no wellboat cost or mortality, and resets the genome to the "standard" strain.
- No disease, carbon or sludge tracking.

### 3.7 SmoltQuality / SmoltStrain / Hatchery
- `Farm/SmoltQuality.cs:86-111` tier multipliers:

| Tier | Mortality | Growth | Time to harvest |
|---|---|---|---|
| Standard | 1.0 | 1.0 | 1.0 |
| Large | 0.82 | 1.06 | 0.88 |
| SuperSmolt | 0.67 | 1.13 | 0.70 |

- Only the growth multiplier is live (`InDayTickSystems.cs:201-204`). Mortality and time-to-harvest are used only in tests (`Environment/BiologyDepthTests.cs`).
- The tier is persisted (`SaveLoadTests/CohortQualitySaveTests.cs`) and kept through transfers (`CohortTransferSystem.cs:180-198`).
- **Exploit:** the PenManagement tier dropdown (`godot/scripts/ui/PenManagement.gd:441-479`) passes a tier into `StockSmolts`, which charges no premium for it.
- `Farm/SmoltStrain.cs:43-80`: five strains. "postsmolt" is 250 g at ×2.5 cost. Live through stocking.
- `Farm/Hatchery.cs`: `ReleaseSmolt` is called only from tests. `OwnsHatchery` adds wages only (`SimulationEngine.cs:2068`) and gives no smolt discount.

### 3.8 CohortTransferSystem (`Farm/CohortTransferSystem.cs`)
- Cost is 200k plus 50 NOK/fish. Mortality = base 0.15-0.5% × weight factor (2.0 / 1.5 / 1.0 / 0.8) × 0.7 with a vet (:59-81).
- Destination MAB is checked. Merges keep stage, quality and genome.
- Called only from the bridge (`GameBridge.Depth.cs:1208-1247`), which executes immediately; the advertised 3-day lead time is fake. UI is `OffshorePanel.gd:461-503`. Tests: `Integration/EngineCorrectnessTests.cs:44-130`.
- Works only between `SeaSite`s in the same zone (:242-250). The `OffshoreStocking` reason is cosmetic.

### 3.9 SludgeValorization (`Farm/SludgeValorization.cs`)
- 20 kg dry solids per tonne of feed × containment collection factor (0.30 to 0.85) (:54-58). Each kg dry solids gives 0.4 Nm³ biogas at 0.35 NOK and 0.8 kg fertilizer at 3 NOK. OPEX is 500 NOK per tonne dry solids.
- LIVE for sea sites only:
  - Accrual: `InDayTickSystems.cs:250-255`
  - Weekly valorization at ≥500 kg plus biogas carbon credit: `SimulationEngine.cs:2176-2218`
  - Manual valorization: `GameBridge.Depth.cs:1118`
- `SludgeTracker` and `HubConfig` are dead. `ToggleSludgeHub` charges 40M/5M (`GameBridge.Depth.cs:1072-1109`), but `HasSludgeHubConnection` is never read by the engine, so the money buys nothing.
- RAS sludge, which in reality is the main sludge source, is not modelled.

### 3.10 Licences and research
- `Finance/LicenseAuctionSystem.cs` (360 lines) is live and tested (`Finance/LicenseAuctionTests.cs`). It covers growth licences per zone only.
- `Finance/LicenseSystem.cs` (`ChargeAnnual`) is wired (`SimulationEngine.cs:579`) but never called: dead. `GameState.Licenses` is filled only by distressed acquisitions (`Licence.cs:444`).
- There are no development licences, offshore licences, land-based permits or discharge permits.
- Research nodes `deep-sea-farming` (`Core/ResearchSystem.cs:536-545`: −10% lice, +6% disease resistance) and `land-based-ras` (:549-558) are flat company-wide bonuses. They do not gate the build verbs.
- `Farm/MultiFarmManager.cs` is dead (tests only).
- Facility parameters are hard-coded in the classes, not in `Calibration/CalibrationRegistry.cs`.

### 3.11 Front end
- RAS and offshore appear only as tabs (`godot/scripts/ui/GameUI.gd:145-146`).
- Nothing in `godot/scripts/environment` renders facilities. Roadmap G5-02 plans the hulls.
- Scenarios cannot start with facilities (`Core/ScenarioSystem.cs:300-360`).

## 4. How facility fish plug into each system today

| System | Sea site | RAS | Offshore | Post-smolt |
|---|---|---|---|---|
| Growth (SDE) | zone water, containment | facility temp, no zone | sea temp, no zone | own simple model |
| Mortality / SIR disease | yes | SIR at 10 °C | yes | own survival curve |
| ISA / storyteller events | yes | excluded | excluded (bug) | n/a |
| Lice model and delousing | per-site model | 0 (correct) | frozen scalar | none |
| Storm wear and escapes | yes | n/a | computed, dropped | n/a |
| Harvest | instant or wellboat | instant | instant | n/a |
| Resource rent | yes | no (correct) | yes | n/a |
| MAB / licence gate | yes | capacity only | capacity only | headcount only |
| Traffic light | yes | no | no | no |
| Carbon | Scope 1/2, feed | Scope 2 | Scope 1/2/3 | none |
| Sludge | yes | no | no | no |
| Save / determinism | yes | yes | yes | yes |

## 5. Gap list

**Land-based grow-out farm (the two LAND references)**
1. No location: no plot, coast, intake or outfall, grid connection, map presence or 3D (ZoneId is empty).
2. No first-class tanks. Needed: smolt, post-smolt and grow-out tank sizes with per-tank cohorts and density limits (kg/m³), grading or splitting between tanks, and staggered batches for year-round harvest. Scale must actually change tanks, energy and staff, and nameplate t/yr should be enforced.
3. Water system type is missing: RAS, flow-through or hybrid. Intake depth sets temperature; pumping energy should scale with head × flow.
4. Water chemistry needs a mass balance driven by feed: O2, CO2, ammonia/nitrite, solids, biofilter capacity. It should feed back into appetite, growth and mortality.
5. Land-specific failure events are missing: power outage leading to O2 crash within hours (needs backup generator or oxygen supply), biofilter crash, H₂S mass mortality, nephrocalcinosis, geosmin off-flavour requiring a purge before harvest.
6. Energy is a fixed 0.80 NOK/kWh. Needed: area spot price, liquid-oxygen or on-site oxygen supply, heat recovery.
7. Biosecurity and fallow are missing: disinfection between batches, disease that enters through intake water, early maturation controlled by photoperiod.
8. On-site harvest and processing are missing: harvest hall, purge, slaughter capacity, truck or quay logistics, and a lice-free price premium.
9. Regulation is missing: a land permit with no MTB or auction, a discharge (N/P) permit, a construction and commissioning phase (biofilter maturation before stocking), CAPEX overrun risk, a production fee, and gating behind `land-based-ras` research.
10. RAS sludge collection and valorization, real maintenance verbs and the facility FCR are all absent.

**Offshore farm (Ocean Farm 1 / Havfarm references)**
1. No exposure class or offshore wave, current and wind climatology. Wave period and operational weather windows are missing.
2. The dropped tick results need wiring: lockdown should stop feeding, escapes should remove fish and record fines and sustainability, integrity should raise escape risk. Repair, inspection and ROV need to work.
3. Needs a site lice model with lower external pressure, delousing restricted to weather windows, and inclusion in the traffic light.
4. Licensing is missing: development licences or offshore-area licences with MTB, and research gating via `deep-sea-farming`.
5. Pen structure is unused: per-section cohorts, and real behaviour for each type (submersible sinking in storms, ship-shaped weathervaning).
6. The systems in the reference diagram are missing: feed silo autonomy in days, supply runs scheduled against weather, DP service vessels, offshore wellboat harvest, shore control centre, sensors and cameras that improve information quality (could hook into `WatchMode` / `RemoteOpsCentre`), structural fatigue, mooring failure, collisions.
7. The flow, O2 and FCR advantages are not applied, and disease events and zone accounting exclude offshore fish.
8. The economics lack structure insurance, depreciation and transport distance to shore.

**Cross-cutting**
1. Unify production units: one `FarmKind` per site or unit, so that lice, wellboats, delousing, escapes, the traffic light, events and sludge apply uniformly instead of through per-class copies.
2. Move bridge-owned verbs into engine partials, following the §10 rule of one new partial per feature:
   - `UpgradeContainment`, `PerformRASMaintenance`, `RepairOffshore`
   - `ToggleSludgeHub`, `ValorizeSludgeNow`, `ExecuteCohortTransfer`
   - the three fake post-smolt stubs
3. Fix the money and capacity bugs: the free smolt-tier exploit, the 40M sludge hub that does nothing, the red-credit drift, and the `EnvironmentalFlexibility` vs engine MAB mismatch.
4. Make the smolt-quality mortality and time-to-harvest multipliers live, and make `TargetTransferWeightG` settable.
5. Move facility constants into `CalibrationRegistry` (S4-10).
6. Allow facilities in scenario starting states.
7. Add any new facility types as additive `GameState` lists with defaults, give each its own named RNG stream with an id suffix, and add them to `SaveLoadMatrixTests`.

## code-front

Lakskongen frontend and asset-pipeline map for adding land-based and offshore farm types (read-only; nothing edited, built or run)

All paths below are under `/Users/fredrik.j.hansen/lakskongen`, the main checkout on `integration/wave1` at commit `5e5a5db`.

**Read this first.** The branches `wave1/gfx-1`, `wave1/gfx-2` and `wave1/gfx-3` are **not merged** into `integration/wave1`. gfx-3 is at `92a8eec` and contains gfx-1 and gfx-2: 85 files, +7415/−3782 lines. They rewrite `FjordTerrain.gd`, `FjordCamera.gd`, `FjordEnvironment.tscn`/`.gd`, `FjordVistaBridge.gd`, `water_shader.gdshader` and `TerrainGenerator.cs`. They add `EnvironmentDirector.gd`, `WaterGrid.gd`, `SeaStateBridge.cs` and `GameBridge.Sky.cs`, and they delete `WaterSurface`, `Sim3DSync`, `VisualAdapter3D`, `NordlandFjordController` and `tilt_shift`. Line numbers marked "gfx-3" come from that branch. I saved copies of two of its files to `/private/tmp/claude-501/-Users-fredrik-j-hansen-lakskongen/1a7df52a-cf33-40ac-aaf8-eaf13ebf76df/scratchpad/FjordTerrain_gfx3.gd` and `.../FjordCamera_gfx3.gd`. Any farm-type 3D work should branch after gfx-3 merges.

## 1. 3D world: scene graph and where sites sit

- **Scene tree.** `godot/scenes/GameScene.tscn:26` instances `FjordEnvironment.tscn`. Lines `:39-40` add `FishPenManager` as a child of FjordEnvironment, and `:42-47` add `FjordVistaBridge` (pen manager, camera and GameUI paths).
- **FjordEnvironment.tscn nodes.**
  - Integration: Terrain `:216`, WaterPlane `:227`, FjordCamera `:232`, Sun, WorldEnvironment, ReflectionProbe `:250`, TimeOfDay, WeatherSystem, TiltShift, Underwater, VisualAdapter3D `:315`, ParticleEffects `:318`, then `WellboatVisualManager` / `FeedBargeVisualManager` / `CompetitorFarmVisualManager` / `Sim3DSync` at `:320-329`.
  - gfx-3: WaterGrid `:209`, Moon `:230`, EnvironmentDirector `:262`, and the same three vessel/rival managers at `:309-315`.
  - The P0-06 split into `.tres` files has **not** happened on either branch, so the scene is still one hot file.
- **Sync path.**
  - Sea sites: `SimBridge.state_updated` → `FjordVistaBridge._on_state_updated` (`FjordVistaBridge.gd:87-94`) → `FishPenManager.sync_pens_from_sim(SimBridge.get_all_pens())`.
  - `get_all_pens` is `SimActions.gd:344`, which calls `GameBridge.GetSeaSiteDetails` (`GameBridge.Farm.cs:838-893`). That covers **SeaSites only**.
  - The vessel and rival managers each bind `state_updated` themselves.
- **Site positions are chosen by index, not by zone.**
  - `FishPenManager._generate_recommended_locations` (`FishPenManager.gd:148-193`) lays `max_pens=12` (`:12`) slots along the channel centreline and probes x offsets for 15–350 m depth.
  - gfx-3 replaces the z span and centreline with `terrain.pen_slot_range()` and `channel_center_x()`.
  - A site takes slot `i` = its index in the `get_all_pens()` array (`:209-225`). Player sites fill from the mouth (−Z) end; rivals fill from the far end (`CompetitorFarmVisualManager.farm_slot`, `:78-80`).
  - The site's `zone` (PO / `fjord_west` …) is **never read by 3D**. Every scenario uses the same 4×4 km procedural fjord. Real per-region terrain is roadmap G2-01.
  - The C# twin for headless placement is `TerrainGenerator.GetRecommendedLocations` (gfx-3 `TerrainGenerator.cs:401`) via `TerrainGeneratorBridge.cs`.
- **Fjord geometry (gfx-3 `TerrainGenerator.cs`).**
  - World ±2000 m (`:16`), mouth sill at z=−1300 (`:30`), head at z=+1450 (`:32`), shoulders 620 m (`:24`), fairway half-width 140 m (`:34`).
  - Skerry strandflat outside the mouth: `BlendStrandflat` `:344-352`, heights about −7 to +15 m. The strandflat drops only 50 m in the last 260 m before the world edge (`:102-103`).
  - Head delta at +2.5 m (`DeltaAtHead` `:332`).
  - **There is no flat land pad and no deep open sea inside the world.**
- **Camera.**
  - Integration: hard bounds ±2000 m (`FjordCamera.gd:38-41`).
  - gfx-3: the focus is clamped to `terrain_size/2 − EDGE_MARGIN(300)` = ±1700 m (`:69`, `_world_half` `:377`, `_clamp_to_world` `:385`). Zoom presets are COAST 2400 / FJORD 900 / FARM 140 / UNDERWATER 40 m (`:26-31`), distance range 25–3000 m (`:43-44`), and `fly_to_position(pos, zoom)` is at `:151`.
- **Water (gfx-3).**
  - `WaterGrid.gd` is a camera-snapped LOD grid with a flat skirt to ±32.8 km.
  - `water_shader.gdshader:107-114` `bake_at()` returns "open ocean" outside the 4 km bake (depth 200, exposure 1).
  - Wave amplitude blends the sheltered and exposed seas by the baked exposure (`:119-120`, `:197`, `:207`). `EnvironmentDirector._derive_sea` (`:637-663`) computes the sheltered sea from the fjord fetch and the exposed sea from `ExposedFetchM`.
  - So an offshore structure placed outside the bake already sits on exposed water and gets exposed waves with no shader work.

## 2. Sea-site rendering

- **`FishPenManager.gd` (734 lines).**
  - Model scenes: pen_cage, workboat and salmon at `:18-21`.
  - `_add_pen_internal` (`:295-355`) builds **one pen per site**: a PenCage, a workboat moored at +33.6 m (`:311-318`), a 1×1 `MooringGrid` (`:320-326`) and a per-pen fish MultiMesh (`:328-333`, at most 240 fish, `:527`).
  - Unstocked sites get a ghost style (`:375-416`).
  - Health ring from lice (`health_state_for` `:257-274`); click handling `:588-595`; `pen_clicked` signal `:46`.
- **`PenCage.gd`.**
  - The `pen_cage.tscn` scene inherits `pen_cage_LOD0.glb` and adds LOD1/LOD2 children, a `HealthRing` 78 m plane and a `ClickArea` sphere of radius 18 m at y −10.
  - LOD switch distances 260/850 m (`:67`), applied with visibility ranges (`:124-135`).
  - Per-pen state goes through instance shader parameters: band, net opacity, skirt (`:172-218`).
  - `style_label` (`:233-248`) is the shared world-label style.
- **`MooringGrid.gd`.** 70 m cells (`:18`). `build(nx, nz, pen_cells, buoy_color, anchor_floor)` (`:82`) draws everything as MultiMeshes with three LODs.
- **Rival farms.** `CompetitorFarmVisualManager.gd:82-133` places 2–4 PenCages in a row with an orange-buoy MooringGrid and a colour band (`:139-167`).
- **Vessels (A3-01).**
  - `VesselModel.gd` takes `asset_id`, length, beam, air draught and `lod_switch` (`:18-28`).
  - `FeedBargeVisualManager.gd`: one barge per stocked site (`:71-104`). Sites with at least 6,000 t get the large barge (`:31`, `:107-110`). Barges are towed along `terrain.channel_route` from `depot_along=-0.45` (`:35`, `:123`), carry a RIB tender, and show a VesselChip.
  - `WellboatVisualManager.gd`: driven by `get_wellboat_jobs`; harbour at `harbour_along=-0.47` (`:37`), lane −50 m (`:41`).
  - Shared helpers: `ChannelBoat.launch` (`ChannelBoat.gd:9`) and `VesselChip.set_chip` (`VesselChip.gd:73`).

## 3. RAS, offshore and post-smolt already exist in sim, bridge and UI, but not in 3D

- **Sim models.**
  - `src/LakskongenSim/Farm/RASFacility.cs`: scale enum Small/Medium/Large (`:260-265`), 12 tanks of 3000 m³ (`:33-37`), CAPEX 0.5/1.2/2 billion NOK (`:245-251`).
  - `OffshoreFarm.cs`: type enum SemiSubmersible (Ocean Farm 1, 110 m), ShipShaped (Havfarm, 394 m), Submersible (`:237-242`); 6 pens of 40,000 m³ (`:26-35`); storm lockdown and structural integrity.
  - `PostSmoltFacility.cs`, and the containment ladder `ContainmentTech.cs:12-20` (OpenNet … OffshoreClosed).
  - Engine verbs: `SimulationEngine.Facilities.cs:20` BuildRASFacility, `:48` BuildOffshoreFarm, `:75` BuildPostSmoltFacility.
  - **Builds set no `ZoneId` or location.** Facility fish join `State.Populations` (`SimulationEngine.FacilityProduction.cs:209`, `:243`).
- **Bridge.**
  - `GameBridge.Farm.cs`: `GetRASFacilities` `:1551`, Stock/Harvest RAS and offshore `:1583-1597`, `GetBuildCosts` `:1637` (medium RAS and semi-sub only), `BuildRASFacility` `:1644`, `GetOffshoreFarms` `:1733` (has `farm_type`, `pen_count`, `construction_progress`, `storm_lockdown`, fish), `BuildOffshoreFarm` `:1768`, `RepairOffshore` `:1799`, `GetPostSmoltFacilities` `:1959`, `StartSmoltBatch` `:1987`.
  - GDScript side: `SimProxies.gd:271-366` and `SimBridge.gd:480-495`.
  - There is **no post-smolt or hatchery build verb** in the bridge.
- **UI.**
  - RAS and offshore are tabs in `GameUI._register_tabs` (`GameUI.gd:145-146`, Cmd+8/9).
  - Both are gated by the tutorial: `TutorialState.gd:33-34` unlocks ras at step 16 and offshore at step 17. Step 16 (expansion) cannot be completed today.
  - The build buttons are hard-coded: `RASPanel.gd:175-177` → `build_ras_facility(name, "medium")`; `OffshorePanel.gd:209-211` → `build_offshore_farm(name, "semi_submersible")`. There is no type or scale choice, no siting and no preview.
  - Nothing outside these two panels reads `get_ras_facilities` or `get_offshore_farms`.

## 4. Blender pipeline

- **Build scripts.** `blender/build_pen.py`, `build_site_kit.py`, `build_vessels.py`, `build_salmon.py`, sharing `lk_meshkit.py` (MeshBuilder `:69`, ring_tube, path_tube, cylinder, box, surface_of_revolution `:310`, make_material `:365`) and `lk_hull.py` / `vessel_designs.py` (designs dict `:553`).
  - Run headless: `Blender -b --factory-startup --python blender/<script> -- [--asset][--lod][--out][--textures]`.
  - Builds are deterministic (no modifiers, no randomness) and byte-reproducible.
- **Export pattern** (`build_vessels.py:205-263`).
  - Writes `<asset>_<LOD>.glb` plus a JSON sidecar (tri count, materials, bounding box, per-part triangles, measured dimensions, Blender version) and raises if over `TRI_LIMITS` (`:106-114`).
  - Fixed node names are the Godot API: Hull/Deck/…; for pens Collar/Net/….
  - Materials are named `LK_*` and mapped to shared `.tres` files through `.glb.import` `use_external` entries, with `meshes/generate_lods=false` (hand-made LODs).
  - The `.tscn` inherits LOD0.glb, adds LOD1/LOD2 children and a script that sets visibility ranges (`feed_barge.tscn` is the template).
- **Manifest and checks.**
  - `blender/asset_manifest.json` (10 entries: id, type, scale, lod_levels, materials, tri_budget, tri_max, export_path, build_script, spec, spec_tolerance, licence, reference).
  - `.claude/skills/ecc/blender-asset-pipeline/blender_asset_pipeline.py --verify-only` (CI step `tools/ci.sh:294-309`) generates `godot/assets/asset_manifest.json` with sha256 per file.
  - pytest: `tests/test_pen_asset.py` (spec ±5%, budgets, import mapping, a rebuild-reproduces test when Blender exists), `tests/test_vessel_assets.py` (vessel list `:28`), `tests/test_salmon_asset.py`.
  - Evidence docs: `docs/run/evidence/a2_01_pen_reference.md`, `a3_01_vessels.md`. Review renders: `blender/render_asset_preview.py`, `render_vessel_preview.py`.
- **Asset folders.** `godot/assets/models/{pen_cage,site_kit,feed_barge,wellboat,workboat,rib,fish,vessel_kit}`.
- **Stale doc.** The SKILL.md LOD and triangle table is out of date.

## 5. How a player expands today

- **Sea sites: there is no UI path.**
  - `MapPanel.gd` is a PO region viewer with hard-coded `REGIONS` (`:38-60`). It emits `region_selected` (`:522-527`), which nothing connects.
  - `SimActions.place_pen` → `BuildSeaSite` (`SimActions.gd:151-165`, `GameBridge.Depth.cs:755`) has no caller except `ScreenshotHarness.gd:314-318`.
  - Licence verbs (`ApplyForLicense`/`PlaceLicenseBid`/`GetLicenseAuctions`/`CanBuildSeaSite`, `GameBridge.Licence.cs:20/59/93/135`; `SimBridge.gd:545-551`) have no UI callers.
  - Roadmap U2-01 owns this flow (BuildSiteDialog, LicenceAuctionCard).
- **Orphaned panels.** `EmpirePanel` and `DecisionPanel` are not in the tabs (D6 defers or revives them).
- **Research.** `godot/data/research_nodes.json:315` `deep-sea-farming` and `:329` `land-based-ras` carry effects only and do not gate builds. They are mirrored in `ResearchSystem.cs:536/549` and kept equal by a contract test.
- **Scenarios.** `scenarios.json` has `starting_buildings` (hatchery, ai_feeding, processing_plant …), applied at `ScenarioSystem.cs:380`. No scenario starts with RAS or offshore.
- **Text and theme.**
  - `godot/translations/translations.csv` (key,en,no; 9 `UI_RAS_*` keys, 29 `UI_OFFSHORE_*` keys, e.g. `:417-424`).
  - Encyclopedia: hatchery entry `encyclopedia_entries.json:139`, closed-containment entry `:425`.
  - Theme: autoload `LakskongenTheme` = `godot/theme/default_theme.gd` (Ink and Chart tokens `:25-55`); `project.godot:58` sets `theme/custom`; tokens live in `tokens.json` and sync via `tools/sync_tokens.py`.

## 6. Tests, CI and visual QA (none were run)

- **`godot/tests/UiFlowTests.gd`.**
  - `_run()` awaits the tests serially (`:29-102`); helpers `_check` `:2692`, `_finish` `:2700` (prints `UI_FLOW_RESULT`).
  - The asset tests use `start_qa_scenario("po3_red_sone", 91)` and then instance GameScene: `_test_pen_models_read_as_equipment` `:2235`, `_test_hero_pen_and_site_kit` `:2307`, `_test_vessel_fleet` `:2393`.
  - Facility UI tests: RAS `:322`, offshore `:757`/`:778`, facade `:1029`.
- **MIN_CHECKS.** `tools/run_ui_flow_tests.sh:26` is `MIN_CHECKS=0  # set after the integration run` on integration, and 278 on gfx-3.
- **CI.** `tools/ci.sh:409-425` runs 17 steps: dotnet build/test, pytest (including `tests/test_bridge_contract.py`; facade parity `:219`, consumer arity `:511`), JSON validation, Godot import, scene validation (walks every `.tscn`), GDScript scan, UI flow tests, asset verify, MCP audits, and mods.
- **Visual QA.**
  - Baselines live in `godot/screenshots/baseline` (28 PNG frames plus `.import` files).
  - `ScreenshotHarness.gd` shoots `07d_offshore` (`:383-386`) and `08b_fjord_farm` (`:435-440`); there is no RAS or land shot.
  - P0-03's `godot/qa/shots.json` does not exist yet, so shots are still added in code.

## 7. Defects found along the way

- `GameBridge.Farm.cs:1625` lists RAS scale "Mega", which the enum does not have.
- `GameBridge.Farm.cs:1765` lists offshore types "Ship" and "Submerged"; the enum names are ShipShaped and Submersible, so "Ship" fails the parse.
- Facilities have no location, so a presenter must assign 3D slots itself, or the sim must start storing them.
- Slots are by array index, so removing a site shifts the indices that later sites map to.

## 8. Extension points for land-based and offshore farms

1. **New presenter, not FishPenManager.** Add `godot/scripts/visual/FarmFacilityVisualManager.gd`, modelled on CompetitorFarmVisualManager / FeedBargeVisualManager.
   - It reconciles `get_ras_facilities()`, `get_post_smolt_facilities()` and `get_offshore_farms()` by id.
   - It shows `construction_progress` as a staged or ghost build (the pattern in `FishPenManager.gd:398-436`), `storm_lockdown` and CO2 warnings as a VesselChip, and the health signal through a ring or instance parameters as PenCage does.
   - Mount it with two lines in `GameScene.tscn`, the way FishPenManager is mounted at `:39-40`, rather than editing `FjordEnvironment.tscn`.
2. **World slots from the terrain generator.** Add `LandPadSlots()` (strandflat or head-delta pads, blasted-granite shelves baked into the heightfield per seed so the headless and windowed hashes stay equal) and `OffshoreSlots()` (beyond the sill, in the exposure=1 water past the bake) to `TerrainGenerator.cs` and `TerrainGeneratorBridge.cs`, with a matching `FjordTerrain.gd` wrapper. The pen-slot helper `pen_slot_range` (gfx-3 FjordTerrain.gd:359) is the precedent. G2-02 vegetation must exclude the pads.
3. **Camera.** Offshore slots sit outside the ±1700 m clamp, so `FjordCamera._world_half` needs a focus-target bounds extension (coordinate with G2-01's far-field ring). A per-target framing distance is also needed: about 400 m for a land campus, about 250 m for a 110 m ring, versus FARM's 140 m.
4. **Clicks.** Mirror the PenCage `ClickArea` → manager signal → `FjordVistaBridge` route, so a click opens `show_tab("ras" / "offshore")` and selects the row (`FjordVistaBridge.gd:54-62`, `:134-138`).
5. **Logistics.**
   - A land quay or wellboat berth near the mouth fits the existing wellboat harbour (`harbour_along=-0.47`), using `channel_route` plus a final leg.
   - Offshore needs an exposed-sea route beyond the mouth, a DP service vessel (orange hull, `LK_VHullOrange` exists) and a crew boat.
   - Offshore and land sites get no feed barge: exclude them in `FeedBargeVisualManager._sync_barges`.
6. **Assets.**
   - New scripts such as `blender/build_land_base.py` (tank kit in two sizes with banded walls and dark or white lids, service halls, harvest hall, orange truss conveyor bridge, quay, intake/outfall basin, substation) and `blender/build_offshore.py` (Ocean Farm 1-class octagonal semi-sub with columns, pontoons, bracing, gantry, accommodation block, silos and cranes; a Havfarm-class hull via `lk_hull`).
   - Repeated tanks and buildings should be drawn as MultiMesh kits, like MooringGrid.
   - Use a new shared material folder such as `godot/assets/models/facility_kit/`, manifest entries with specs, pytest files like `test_vessel_assets.py`, and a UI-flow `_test_facilities_render`.
   - This pulls roadmap G5-02 forward; the roadmap has no land-base asset item at all.
7. **UI and verbs.**
   - Type and scale choice with cost and preview, replacing the hard-coded calls in `RASPanel.gd:175-177` and `OffshorePanel.gd:209-211`.
   - Add `build_post_smolt_facility` to the bridge.
   - Optionally gate builds on research by giving `land-based-ras` and `deep-sea-farming` unlock effects.
   - New keys in `translations.csv`, encyclopedia entries, a harness shot for each type, and a scenario with `starting_buildings` including RAS or offshore.
   - Put new bridge members in one new partial (`GameBridge.Facilities.cs`) plus SimProxies and SimBridge lines; the contract test will enforce parity.

## 9. Hot files (cross-checked against roadmap §10)

| File | §10 owners / overlapping work | Advice |
|---|---|---|
| `FishPenManager.gd` | G1-07, G2-01, G2-04, A2-01, G3-01, G3-03 | Avoid; use the new presenter |
| `FjordEnvironment.tscn` | P0-05, G1-02, G1-05, G1-06, G3-02, G3-04; unsplit | Do not add nodes here |
| `FjordCamera.gd` | G1-03, G1-06, G2-01 (bounds), G2-03, G3-04, U2-06 | Bounds hook must be coordinated |
| `FjordTerrain.gd` + `TerrainGenerator.cs` | G1-01, G2-01, G2-02 | Pads and slots belong to the terrain track |
| `water_shader.gdshader` | G1-05, G2-03, G3-03, G3-04 | Avoid |
| `GameScene.tscn` / `GameUI.gd` | G1-03, U1-04, U1-06, U2-03, U2-07, U3-01 | Two-line mount only; U3-01 rewrites the tabs into hubs |
| GameBridge partials / `SimProxies.gd` / `SimBridge.gd` | U2-01, U2-04, U2-06, G3-02, S4-08 | One new partial |
| `translations.csv`, `scenarios.json` | Every item | Sorted keys; merge captain resolves |
| `UiFlowTests.gd` (MIN_CHECKS) | Most items | Append tests; merge captain raises MIN_CHECKS |
| `ScreenshotHarness.gd` | Every item adding a shot | Shots are still code until P0-03 |
| `SimulationEngine*.cs` / `CalibrationRegistry` | Every S item | Sim side |

Hot in practice but not in §10:
- `blender/asset_manifest.json` and the generated `godot/assets/asset_manifest.json`: every asset item edits them.
- `RASPanel.gd` / `OffshorePanel.gd`: U3-01 docks them, U3-03 rebuilds 07d.
- `TutorialState.gd`: U2-02.
- `research_nodes.json` + `ResearchSystem.cs`: contract-paired.
- `GameBridge.Farm.cs` (2121 lines): G2-04.
- `WellboatVisualManager.gd` / `FeedBargeVisualManager.gd` / `PenCage.gd`: G2-03 bobbing and wakes.
- `CompetitorFarmVisualManager.gd`: G2-04.
- `FjordVistaBridge.gd`: renamed to FjordPresenter in G3-02; also touched by U3-01 and gfx-2/3.
- `MapPanel.gd`: U2-01 and U3-03.
