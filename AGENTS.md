# AGENTS.md — LakskongenSim Agent Instructions

## Project Overview
LakskongenSim is a realistic Norwegian salmon-farming simulation with Godot 4.7.2 (Mono) frontend and C# .NET 10 backend. The engine is deterministic, save/load round-trips exactly, and all subsystems implement `ITickSystem` + `ISnapshotable`.

**Key Architectural Principles:**
- **Determinism first**: `SimRng` SplitMix64 with frozen streams; `DeterminismBanTests` enforces no unseeded `Random`, `DateTime.Now/Today`, `DateTimeOffset.Now`, `Environment.TickCount`, `RandomNumberGenerator`, `Guid.NewGuid()` or process-random hashing in src/LakskongenSim (Roslyn scan; the allowlist is keyed by `path :: pattern :: Type.Member :: count`)
- **Calibration SSOT**: All numeric constants in `CalibrationRegistry` / `BalancingConfig`; grep test fails on literals in tick code
- **No clamps to hit realism**: If a band is missed, fix the model
- **Visual claims need screenshot diffs**: Headless "green" ≠ evidence
- **Player-path tests required**: Feature not "done" until `PlayerScriptProbe` shows outcome change

## Repository Structure
```
lakskongen/
├── src/LakskongenSim/           # Core simulation engine (.NET 10)
│   ├── Core/                    # SimulationEngine, GameState, SimRng, Tick systems
│   ├── Finance/                 # CompetitorAI, CrisisCapitalism, LicenseAuctionSystem
│   ├── Events/                  # CrisisCapitalismSystem, Event types
│   ├── Farm/                    # SeaSite, FishPopulation, Biology
│   ├── Market/                  # MarketSimulation, PriceCouncil
│   └── Tests/                   # xUnit tests (914 passing / 1 skipped across the solution)
├── godot/                       # Godot 4.7.2 Mono project
│   ├── scripts/
│   │   ├── autoload/            # GameBridge partials (Core, Farm, Market, Depth, Economy, Licence, Content)
│   │   ├── bridge/              # SimProxies.gd, SimActions.gd, SimFormatters.gd, SimState.gd
│   │   └── SimBridge.gd         # Facade forwarding to proxies
│   └── data/                    # calibration.json, scenarios.json, tutorial_steps.json
├── tools/ci.sh                  # 18-step CI pipeline (tools/ci_clean_checkout.sh runs it on a fresh clone; step 18, the P0-04 perf smoke test, is warn-only)
├── docs/NEXT_LEVEL_PLAN.md      # Phase 4+ roadmap
└── docs/REALISM_CHECKLIST.md    # Calibration bands
```

## Development Commands
```bash
# Full CI (run before any commit). A missing tool makes a step SKIP;
# CI_REQUIRE_ALL=1 (set in GitHub CI) turns every SKIP into a failure.
# The shader step opens a small window: headless Godot parses no shaders.
./tools/ci.sh
CI_REQUIRE_ALL=1 ./tools/ci.sh

# Build only
dotnet build LakskongenSim.sln

# Run all tests
dotnet test src/LakskongenSim.Tests

# Run specific test filter
dotnet test src/LakskongenSim.Tests --filter "FullyQualifiedName~LicenseAuctionTests"

# Capture screenshots (visual regression)
./tools/capture_screenshots.sh

# Godot headless (for bridge contract tests)
cd godot && dotnet run -- --headless --quit
```

## Phase Status (from NEXT_LEVEL_PLAN.md)

### ✅ Phase 4.1 — Licence Economy (COMPLETE)
- Green-zone growth-licence auctions with 150M NOK reserve, 7-day window
- AI rivals bid from `CashReserves` with appetite/lobbying-scaled ceilings
- Player bid gate: ≥5% over current, rounded to whole millions
- Settlement: highest solvent bidder wins (player pays from balance, rival from CashReserves)
- Rival-initiated openings (p=0.02/day when no auction open)
- Build gate: first site free, new zone requires unconsumed won licence
- Build cost: 6M NOK (was 30M hardcoded)
- Save/load round-trip via single eager `license_auction` RNG stream
- **Tests**: 17 new tests in `LicenseAuctionTests.cs` (calibration, bidding, settlement, determinism, save/load, 400-day rival opener)
- **Bridge**: `GameBridge.Licence.cs` partial with `GetLicenseCost`, `ApplyForLicense`, `PlaceLicenseBid`, `GetLicenseAuctions`, `HasGrowthLicense`, `CanBuildSeaSite`

### ✅ Phase 4.2 — Competitors That Compete (COMPLETE)
- **CrisisCapitalismSystem determinism fixes**: 
  - `DateTime.Now` → `StartDay` (simulation day)
  - `Guid.NewGuid()` → deterministic IDs (`crisis_{id}_{type}_{day}`, `asset_{crisisId}_{type}_{idx}`)
  - `IsActive(int currentDay)` instead of property
- **Distress wiring**: `CompetitorAI.OnCompetitorDistressed` event → `CrisisCapitalism.TriggerCrisis` with competitor roster (subscribed by the engine since review commit `1467d03`; before that the event had no subscriber and this line was not true, review F499)
- **Distressed assets linked to actual competitor state**: 
  - `SourceCompetitorId`, `SiteId`, `LicenseCount`, `MABTonnes`
  - Values derived from competitor's actual `SiteCount`, `LicenseCount`, `TotalMABTonnes`
- **Player acquisition transfers real assets**: `AcquireDistressedAsset` → adds `SeaSite`, `LicenseRecord`, reduces competitor holdings
- **Save/load**: `CrisisEventState` + `DistressedAssetState` in `GameState`, restored via reflection on private `_availableAssets`
- **Bridge**: `GetDistressedAssets()`, `AcquireDistressedAsset()` in `GameBridge.Licence.cs`, proxied via `SimProxies.gd`/`SimBridge.gd`; `GetAcquisitionOpportunities()`/`AttemptAcquisition()` return the live listings. No player UI reaches them yet: `EmpirePanel` is orphaned (no scene or tab loads it) and the bridge returns stubs for empire phase, share price and IPO

### ✅ Phase 4.3 — Corporate Finance (COMPLETE)
- **Annual closing & periods**: Quarterly AdvanceQuarter() with year-end AnnualClose() resetting P&L accounts
- **22% corporate tax + 25% resource rent tax**: TaxSystem.ComputeTotalTax() combines both on net income and resource rent base (norm price × volume - 65% standard deduction)
- **Loss carry-forward**: 80% annual limitation, tracked in TaxSystem.LossCarryForward, auto-utilized against future profits
- **Covenants**: Leverage ≤4.0x, Interest coverage ≥3.0x, Equity ratio ≥30%, Liquidity ≥50M NOK; tested quarterly + annually; breaches tracked with resolution state
- **IPO system**: Private → Preparing → Roadshow → Listed stages; quarterly guidance with share price impact on miss/beat; market drift applied daily
- **Biomass inventory**: FinancialStatements.UpdateBiomassInventory(biomassKg, normPricePerKg) tracks live fish value as asset
- **Save/load**: All new state (TaxSystem loss carry-forward, CovenantSystem breaches, IPOSession stage/price/guidance history, FinancialStatements biomass/period) persisted via Core.CorporateFinanceSystemState
- **Bridge**: TaxSystem, CovenantSystem, IPOSession accessible via SimulationEngine properties for future UI integration
- **Tests**: All 667 existing tests pass; determinism gates (ExactSaveHashTests, EngineTwinRunDeterminismTests, PlayerScriptProbe) pass

### 🔄 Phase 4.4 — Research & Vertical Integration (PENDING)

### ✅ Phase 4.4 — Research & Vertical Integration (COMPLETE)
- **Research effects now consumed at simulation points:**
  - `growth_rate_bonus` → GrowthModel SDE growth rate multiplier
  - `fcr_reduction` → FeedSystem FCR multiplier  
  - `feed_waste_reduction` → FeedSystem waste fraction multiplier
  - `mortality_reduction` → MortalityModel additive reduction to total daily rate
  - `disease_resistance_bonus` → MortalityModel disease rate multiplier
  - `market_price_bonus` → MarketSimulation price multiplier (combined with VerticalIntegration quality premium)
  - `sustainability_score_bonus` → SustainabilityManager score additive
- **Vertical Integration made mechanical:**
  - Monthly OPEX from owned assets charged to FinanceManager
  - Cost savings per kg from completed tiers → reduces effective FCR
  - Quality premium multiplier → increases market price
  - Construction timer framework (30-day interval)
- **Determinism:** All new parameters flow through engine properties, no new RNG
- **Tests:** All 667 pass; determinism gates (ExactSaveHashTests, EngineTwinRunDeterminismTests, PlayerScriptProbe) pass

### ✅ Phase 4.5 — UI/UX Pass (COMPLETE)
- **AttentionInbox panel** — unified "what needs my attention" inbox driven by C# aggregator (`GetAttentionItems`) + market-drop item computed in GDScript; event-driven via SimBridge signals (`day_advanced`, `state_updated`, `alert_raised`); priority-based sorting; auto-refresh toggle
- **AttentionInbox scene** — `scenes/ui/AttentionInbox.tscn` (CanvasLayer layer=100, white DialogPanel 780×540, unique node names) + `scripts/ui/AttentionInbox.gd` with category grouping, priority badges (emoji+text CVD-safe), action buttons, auto-refresh toggle
- **HUD integration** — 🔔 InboxBtn in `scenes/ui/HUD.tscn` + `scripts/ui/HUD.gd`; `set_paused_state(paused)` public method; tooltips on balance/fish/temp/price/alerts/weather
- **Auto-pause on alerts** — `scripts/Main.gd` auto-pauses on NEW critical alerts (deduped via `_last_auto_pause_msg`); setting `gameplay.auto_pause_critical` (default true) in SettingsMenu
- **Input** — `toggle_attention_inbox` action (physical keycode 73 = 'I') in `project.godot`; `_input` guard for LineEdit/TextEdit focus + ESC
- **Bridge & signals** — `AttentionInboxRequested` signal in `GameBridge.Farm.cs`; `OpenAttentionInbox()` → `SimulationEngine?.OpenAttentionInbox()`; direct connect (no `has_signal` — bridge-contract rule); `_tr_or` helper using `TranslationServer.Translate` compare-then-fallback
- **Settings** — `accessibility.ui_scale` (80/90/100/110/125%) in SettingsMenu; applied via `content_scale_factor`; persisted to `user://settings.json`; `_apply_saved_ui_scale()` in `default_theme.gd` at boot
- **Known limitations** — `high_contrast` setting collected+persisted but NOT applied anywhere (documented decision); color-blind safety via emoji/text priority badges (not color-only)
- **Scene validation & CI** — all 54 scenes load cleanly; CI 15/15 passes; screenshots captured

### ✅ Phase 4.6 — Modding Parity (COMPLETE — species frozen as non-goal)
- **Mod events/research/scenarios flow into the engine** (species frozen — see below):
  - `SimulationEngine.LoadMods(dir)` configures the mods directory; every `Initialize()` re-applies mods onto the fresh systems (idempotent per engine state — events never double-register). The bridge resolves `user://mods` and hands it over; `SimulationEngine.ResolveModsDirectory()` is the headless fallback (`<exe>/mods`)
  - Mod events → `ModLoader.BuildEventDefinitions(FinanceManager)` → `Storyteller.RegisterEvent`
  - Mod research → `ModLoader.GetAllResearchNodes()` → `ResearchSystem.AddModNodes` (additive, core nodes win on ID collision)
  - Mod scenarios → `ModLoader.GetAllScenarios()` → `ScenarioSystem.RegisterModScenarios` (converts `ModScenario` → `ScenarioDefinition`, never overwrites core)
- **Single naming scheme** — `tools/validate_mods.py` enforces the ModLoader prefix-dispatch convention (`events_*`/`research_*`/`fish_*`/`scenario_*`) and mirrors the real `EventCategory`/`ResearchBranch`/`ProductionRegion`/`WinCondition` enums; fixed the example pack's free-text `win_condition` → enum value
- **JSON Schemas** — `schemas/mods/{mod,events,research,fish,scenarios}.schema.json`
- **CI cross-reference checks** — `step_validate_mods` (15th CI step) validates manifests, file references, dependency closure, duplicate IDs, scenario `initial_events` → known event IDs, enum values, and temperature-range sanity
- **Tests** — `ModWiringTests.cs` (8 tests: event registration, research add/no-overwrite, scenario register/no-overwrite, species load, dependency ordering)
- **`.gitignore`** — runtime-generated `mods_config.json` ignored
- **Species frozen (documented non-goal):** `ModFishSpecies` is parsed (`GetAllFishSpecies`) but NOT consumed by any simulation point — the engine hardcodes Atlantic salmon and there is no player-selectable species path. Decision (council, 2026-10-12): species wiring feeds neither the Phase 4 exit gates (blind playtest, no-dominant-strategy sweep) nor Phase 5, so it stays "parsed, not consumed" rather than faked green. Reference mod: `godot/mods/example_norwegian_pack/`.

### 🔄 Phase 5 — Ship (PENDING)
- Windows/Linux/macOS export presets + CI export job + artefact upload (macOS export-verify already passes in CI)
- Save fuzzing; weekly playtest builds
- Decide licence; finish Steam page + demo build (`forste_sesong` + `po3_red_sone`) + Next Fest plan
- Update VISION/GDD to match reality (no Lua; multiplayer deferred)

### Remaining Phase 4 exit gates (human/empirical — NOT code-verifiable)
The Phase 4 "Exit" in `NEXT_LEVEL_PLAN.md` has two gates an agent cannot close autonomously. Treat them as explicit, separately-scheduled items, not as implicit "CI is green = done":
1. **Blind playtest** — 5 people × 2 hours: everyone completes `forste_sesong`, ≥3 say they understand why they made/lost money. *A calendar item only the owner can schedule.*
2. **No-dominant-strategy sweep** — `PlayerScriptProbe` strategy sweep. **Must define the failure criterion before trusting a pass** (a sweep that finds no dominant strategy can just be too shallow). Note the tension: "no clamps to hit realism bands" collides with "no dominant strategy" — a real dominant strategy needs a model fix, not a clamp.

## Key Systems Reference

### RNG & Determinism
- `SimRng` (SplitMix64) + `SimRngStreams` (frozen enum)
- Streams created EAGERLY in `Initialize` so `ImportStates` finds them on load
- `DeterminismBanTests` scans for banned patterns
- Twin-engine test: `EngineTwinRunDeterminismTests` advances 2 engines same seed → identical state

### Calendar, production areas, sea temperature (S2-02)
- `Core/SimCalendar.cs` is the one calendar: day 0 = Friday 1 January 2027, 365-day years, real months, ISO weeks. No `% 360`, `% 365` or 30-day months anywhere else (`CalendarGovernanceTests` greps for them); books close on the last day of the month/quarter
- `Environment/ProductionAreas.cs`: 13 production areas (3 m monthly climatology from `tools/derive_po_sea_temperature.py`, lice spring weeks, PD zone, licence size); the national reference (area 0) only serves games with no scenario
- A zone lies in an area only by data (`Zone.ProductionArea`, `production_area` on scenario sites; ids are never parsed); no area = the home area (`GameState.HomeProductionArea`)
- `WeatherSystem` owns the shared anomaly; `SimulationEngine.HoldingWaterOf(population)` (`Environment/HoldingWater.cs`) is the water growth, feeding, stress deaths, the disease tick and the mortality hazards take — land-based and offshore farm types plug in there
- One holding seam (wave 2 sim-a-1, `Core/SimulationEngine.Holdings.cs`): `HoldingOf(population)` resolves sea pen / offshore pen / land tank once per simulated step (`Core/HoldingIndex.cs`; between steps it scans afresh); `HoldingKind` is the one enum (`Biology.FishHolding` = S2-05's names for its values); never add a parallel lookup of facilities or zones in a hazard or model (docs/FARM_TYPES_SPEC.md §3.1a)
- D18: no network I/O in the sim; BarentsWatch reads offline files only (`OfflineDataTests`)

### Growth, appetite, ration (S2-03)
- `Biology/TgcGrowthLaw.cs` is the growth law every holding runs (calibration section `growth`): TGC 3.0 on degree-days up to a 13 °C optimum, falling to 0 at 22 °C, a size tail above 3 kg toward 20 kg, a day-length factor, appetite = satiation gain × satiation FCR, growth along a growth-ration curve, and a cohort weight CV. Pure (no RNG): the tick, `ForecastTwin` and scenario starting stock share it. The old SDE in `GrowthModel.cs` has no engine caller
- Seam for farm types: `GrowthConditions` is all the law reads; `SimulationEngine.GrowAndFeedOneDay(population, HoldingHusbandry)` (`SimulationEngine.Growth.cs`) fills it from the holding the step resolved once (`HoldingOf` → `WaterOf`, the same water as `HoldingWaterOf`) and `DayLengthHoursOf` (land tanks 24 h light). `FishPopulation.Ration` (`Farm/RationPolicy.cs`) is the feeding-plan input (U2-04); `Cohort.WeightCv` (0 = unknown) and `SizeSpreadOf(pens)` feed grading (S2-04)
- Scenario starting sites carry `days_at_sea` (S0 mid-September in PO1-PO7, S1 mid-June in PO8-PO13), grown by `GrowOnClimatology`; `SimProbe` sites lie in PO4 water with a mid-April first release; save format v5

### Feeding plans (U2-04)
- A sea site's plan (`SeaSite.FeedPlan`, `Farm/SiteFeedPlan.cs`; null = full appetite on the standard feed): ration (share of appetite, ≥ 50 %), recipe (`FeedRecipeKind` standard / high energy / economy, `FeedRecipeEffect`: FCR and price factors priced on energy, astaxanthin for the fillet grade) and window (days, 0 = until changed). Calibration section `feedPlan`; save format v10 (F2-03 took v9; the wave-3 integration chains U2-04 after it)
- Verbs (`SimulationEngine.FeedPlans.cs`): `SetFeedPlan(siteId, rationPct, recipe, windowDays)`, `SetFeedPlanForAllSites` ("Fôr alle"), `FeedPlanOf`, `ProjectFeedPlan` (twin-lite, pure: the site's cohorts on `Growth.Law` in the expected water, docs/BALANCE.md §1r). The biology tick calls `ApplyFeedPlans` first (rations to every pen, expiry); `HoldingHusbandry.Recipe` carries the recipe into `GrowAndFeedOneDay` and `FeedSystem.FeedRation`'s price factor. A plan-less game is unchanged (golden traces)
- Bridge `GameBridge.FeedPlans.cs` (`set_feed_plan`, `set_feed_plan_all`, `get_feed_plan(s)`, `preview_feed_plan`, `get_feed_plan_options`; recipes travel as ids `standard`/`high_energy`/`economy`); UI: the Fôringsplan card (`scenes/ui/FeedPlanCard.tscn`) on the Pens tab

### Harvest pipeline (S2-04)
- A harvest sells gutted kilos: live kg × 0.84 (HOG) split into size classes on the gutted weight (`Harvest/SizeClassPriceLadder.cs`, lognormal from each cohort's CV) and grades (`Harvest/HarvestGrading.cs`: superior/ordinary/production by cause), at the price × class factor × grade factor; the harvest cost (wellboat + slaughter, 5.0 NOK/kg HOG) is COGS `harvest` (F270). Calibration section `harvest` (v1.0.8); every batch files a `HarvestReport` equal to its ledger entries
- Verbs (`SimulationEngine.Harvest.cs`, `.HarvestOrders.cs`): `OrderHarvest(holding, pens, fraction)` (planned: wellboat slot, slaughterhouse capacity per pool, pens fast just ahead of their turn by degree-days, partial and per-pen), `QuoteHarvest` (pure), `CancelHarvestOrder`, `HarvestNow`/`HarvestSite` (express: today, waiting pen, surcharge); post-tick `HarvestOrdersTickSystem`. Bridge: `GameBridge.Harvest.cs` (`quote_harvest`, `order_harvest`, `cancel_harvest_order`, `get_harvest_orders`, `get_harvest_reports`); `harvest_pen` stays express
- Seam for farm types: `HarvestRoute.For(HoldingKind, …, hallCapacityTonnesHog)` (sea wellboat / offshore wellboat / a land farm's own hall) and `HarvestCondition` (cold-water memory and fast from `HoldingWaterOf`; `ExtraProductionShare` for a holding's own downgrade). Resource rent: HOG kg × norm price, the harvest cost is no sea-phase deduction. Save format v6
- Every fish a harvest takes leaves its cohort through the S2-05 ledger (`AddPenToLot`/`TakeCohortIntoLot` → `RemoveAlive`, `FishRemoval.Harvest`; forced harvests `FishRemoval.ForcedHarvest`), so a report's fish equal the ledger's removals (`HarvestLedgerReconciliationTests`)

### Capacity v1 (S2-01a)
- `Finance/LicenceHolding.cs`: a sea licence holds its area's size (780 t, 945 t in Troms and Finnmark) and licences pool per production area; `SeaSite.MABTonnes` is the locality MTB; a build clears a 3,120 t locality and adds 0 t of licence MTB. Land units hold no licence
- Stocking and post-smolt transfer are checked against the farmer's plan (`Finance/CapacityPlanner.cs`, pure: TGC 2.8 on the area's climatology) for the area's licence MTB and the site's locality MTB (`PreviewStocking`, `MaxStockableSmolts`, keys `mtb_*`)
- Daily post-tick `CapacityEnforcementTickSystem` (after `HarvestOrdersTickSystem`): an order, the fee (≤ 15 G) and the confiscation on each new peak of illegal kilos, a 14-day deadline, then a heaviest-first forced harvest sold through the S2-04 pipeline; regime `GameState.CapacityRules` (scenarios open it; a pre-S2-01 save migrates on load; bare engines stay unregulated). Calibration section `capacity`; bridge `GameBridge.Capacity.cs`; save format v7
- Density: a stocking fills as many 10,000 m³ proxy pens as it needs at 25 kg/m³ and 5 kg (`StockingVolumeM3`); S2-01b replaces the proxy with real pens (below); the proxy is left for fish in no pen (offshore pens, land tanks, fixtures)

### Real pens (S2-01b)
- `Farm/Pen.cs`: a sea site is a frame mooring of pens (`SeaSite.PhysicalPens`, numbered from 1, id `"<site>/p03"`); a pen has a net (collar circumference, wall, cone), `VolumeM3` = C²/(4π) × (wall + cone/3) (the volume the fish can move in, akvakulturdriftsforskriften § 25) and holds at most one population (`Pen.PopulationId`, "" = empty). `SeaSite.Pens` is still the list of populations (a legacy name); `PenOf`/`PopulationIn`/`FindPen` (id or number)/`IsPenFree`/`ReleasePen` map them. Calibration section `pens` (v1.0.10): 200,000 fish a pen (§ 47a), the standard 160 m pen (25 m wall + 15 m cone = 61,116 m³), 8 pens on a built site
- Layouts: scenario sites take `pen_layout` from `scenarios.json` and the code roster (`StartingSiteDefinition.ResolvePenLayout`, 6-10 pens; the stocked pens are the first ones); the `SeaSite(id, zone)` constructor and `BuildSeaSite` give the standard layout. `NumberOfPens` is NOT the pen count: it stays the size crew and upkeep scale with (`SiteSizeScale`)
- Stocking (`SimulationEngine.Pens.cs`): `StockSmoltsInPen(site, pen, count, strain, tier)` refuses `pen_not_found`, `pen_occupied`, `pen_fish_limit` (> 200,000, under capacity v1 only), `pen_density` (the plan's peak over 25 kg/m³, projected by `Farm/PenDensityPlanner.cs` on the S2-03 growth law to the plan's harvest weight with the plan's survival), then S2-01a's `mtb_*`; `PreviewPenStocking` is the same check with the room. `StockSmolts(site, count)` spreads the smolts over the fewest empty pens that hold them (`pens_full`); `PreviewStocking`/`MaxStockableSmolts` include the empty pens' room. Reason keys: `Core/PenViews.cs` (`PenReasons`), text `CAPACITY_REASON_PEN_*`/`PENS_FULL`
- Populations map to pens: `PopulationHolding.Pen` (resolved with the holding once per step); the biology tick reads density in the pen (`DensityVolumeM3`); harvests take pens by pen id or number as well as population id and free them (`HarvestOrderPen.PenId`); `DelousePens` treats chosen pens (handling on their fish; the site lice model loses the kill on the treated share); the advisor reads `SeaSite.DensestPenKgPerM3`
- Save format v8; a v7 save gets its locality's layout and one pen per population on load (`MigratePensAfterLoad`; a population too large for a pen keeps the proxy volume it was simulated in, a grandfathered unit as § 70 exempts units stocked before § 47a)
- Bridge `GameBridge.Pens.cs` (`get_site_pens`, `preview_pen_stocking`, `stock_smolts_in_pen`, `delouse_pens`); `GetSeaSiteDetails` adds `pen_count` and `pens` (pen k at index k-1: `id`, `number`, `population_id`, `occupied`, `fish_count`, `biomass_kg`, `mean_weight_kg`, `weight_cv`, `lice_per_fish`, `density_kg_m3`, `volume_m3`, `circumference_m`, `net_depth_m`, `ration`, `fasting`) for the fjord's pen layout (G2-04); the Pens tab's "Merder og utsett" card stocks a chosen count in a chosen empty pen

### Expansion flow (U2-01)
- `Core/SimulationEngine.Expansion.cs` + `Core/ExpansionViews.cs`: the map's verdicts as reason keys (`ExpansionReasons`, text `EXPANSION_REASON_*`): `BuildGateKey`/`BuildVerdictOf` (the build gate, then the cost; the free first locality is F2-03's development right: it counts fjord sites, `FjordSites()`, so an offshore unit neither uses it up nor opens a fjord zone) and `ApplyVerdictOf` (the licence application); `ApplyForGrowthLicence`, `CanBuildSeaSite`, `BuildSeaSite` and `PreviewBuildSeaSite` run them. Pure reads: `GetExpansionOverview` (13 areas: licences, MTB, headroom, traffic light, climatology, zones), `PlanSiteBuild` (cost, cash after, running cost per month, 3,120 t locality, +0 t licence MTB, the pen layout), `PreviewLicenceApplication`, `GetLicenceAuctionView` (bids newest first with rival names and NOK/t)
- Catalogue zones (`Environment/ExpansionZones.cs`, one growth zone per area) are addresses, not zones: they join `GameState.Zones` only when a site is built (`AdmitCatalogueZone`); outside the game they take the area's traffic light (worst colour of its zones, else green). Never add them to the state at start: that changes the rival opener's draws and every golden trace
- Auctions the company is in report `licence_outbid` (warning, verb `bid_licence`, deadline = close) and `licence_auction_won`/`_lost` on the bus; opening one registers a pacing deadline; a leading bid resolves them. Activity rule: a bid on the closing day extends the close by `ClosingRoundExtensionDays` (1)
- UI: `MapPanel.gd` (area picker, traffic-light dots, ring on the company's areas) hosts `RegionExpansionSection` (`scripts/ui/expansion/`), `LicenceAuctionCard.tscn` and `BuildSiteDialog.tscn` (first step "Anleggstype": Sjø works; Land and Hav point to their tabs until F2-04's catalogue, F2-31). Bridge `GameBridge.Expansion.cs` (`get_expansion_overview`, `plan_site_build`, `build_expansion_site`, `preview_licence_application`, `open_licence_auction`, `bid_on_licence_auction`, `get_licence_auction`); the inbox lists auctions under "Vekst". Frames: `tools/capture_screenshots.sh --scene res://qa/scenes/ExpansionFlowCapture.tscn` (shots `41_expansion_*`)

### End screens, liquidity ladder, campaign (U2-03)
- Negative cash pays 12.25 % a year (forsinkelsesrente), booked as interest (`OverdraftInterestTickSystem`). The bank's ladder (`Core/SimulationEngine.Liquidity.cs`, `Finance/LiquidityLadder.cs`, calibration section `liquidity`) reads the burn (median 7-day change over 35 days) and the runway to the scenario's floor: `liquidity_warning` (warning, <= 60 days), `bank_call` (critical, <= 28 days) with a rescue offer against half the biomass (14 %, 2 % fee, 36 months, 14 days to answer, one a game); verbs `AcceptRescueLoan`/`DeclineRescueLoan`; state `GameState.Liquidity` (additive). S1-03's depth ladder is unchanged beneath it
- `Finance/PnlStory.cs` hears every P&L booking (`SimulationEngine.Story.cs`, `PnlBooked` for tests); a turning point is one ledger line's income and costs over <= 14 days; `GetScenarioEndReport()` (verdict, figures, months, 3 turning points, 5 largest lines, mortality from the S2-05 ledger, distinctions and medal, the rescue loan's day). Never decide anything from it
- UI: `scenes/ui/ScenarioEndScreen.tscn` (CanvasLayer 50, last child of GameScene) opens on `SimBridge.scenario_ended` and alone takes input while open (its `_input` takes every key; tests replace the scene change with `scene_change_override`); `scripts/ui/endscreen/` (EndScreenReport, PnlStoryChart, MedalBadge); `BankCard.gd` tops the Finance tab. Bridge `GameBridge.EndScreen.cs` (`get_scenario_end_report`, `get_current_verdict`, `get_liquidity_status`, `accept_rescue_loan`, `decline_rescue_loan`)
- Campaign: `CampaignProgress.gd` over `data/campaign.json` (two scenarios open, wins open the next tier, best medal each) in `user://campaign.json`. Tests, captures and QA set `CampaignProgress.path_override`; never write the player's file from a test. Frames: `tools/capture_screenshots.sh --scene res://qa/scenes/EndScreenCapture.tscn` (shots `42_*`)

### First season (U2-02)
- `godot/data/tutorial_steps.json` (embedded in the sim, the one source): 18 steps, one verb each; a step has `kind` (do/wait), `pace` (hold/slow/run), `completionCondition`, `highlightTarget` and `unlocks` (tabs that open at that step); its text is `TUTORIAL_<ID>_TITLE/_TEXT/_DO`. `Core/TutorialConditions.cs` is the closed vocabulary: `ui:<mark>` (a screen sends `SimBridge.tutorial_mark(mark)`: `tab:<id>`, `inbox`, `clock`, `harvest_report`, `map_area_home`) or an engine state only the player's verb or the world produces. No day count, no condition true by itself (`FirstSeasonTutorialTests` scans the json and the source); an unknown or empty condition fails the load
- `Core/TutorialSystem.cs` runs only where the scenario's win is the tutorial (Første sesong, D19): elsewhere no step advances and no tab is gated. A mark completes only the step that waits for it; `CheckProgress` runs at day end and after the player's commands (bridge `TutorialCheck`; the card asks every 0.25 s); `Skip()` saves `GameState.TutorialSkipped` (save v11) and the win (`IsEarned`) is then false; steps pay nothing (D16). The funding step (13) waits for the second site's price + 3 months of its running cost in the bank and points at the bank's loan: it teaches the loan where the cash is short (a loan that leaves it short does not complete it)
- UI: `TutorialUI.gd` (the card; its exports are wired with `node_paths`), `scripts/tutorial/TutorialTargets.gd` (a target id → the control on screen now, or the way there) and `TutorialHighlight.gd` (ring and chevron, last child of `EventLayer`, never takes a click). Pace: hold = `hold_clock("tutorial")`, slow = 1× once per step, run = the player's clock. `TutorialState.gd` gates the tabs in `unlocks` until their step, only in Første sesong and only while "Vis opplæring" is on. A screen a step points at exposes the control through a getter (`PenManagement.stock_button()`, `FinancePanel.loan_button()`), never a node path in the resolver
- Tests: `Tutorial/FirstSeasonTutorialTests.cs`, the minimal player `Tutorial/TutorialPlayer.cs`, and `godot/tests/FirstSeasonFlowChecks.gd` (UiFlowTests: the season won by clicks and keys only, the loan taken at the funding step). Map: `godot/scripts/tutorial/README.md`

### Golden traces (F2-01)
- `src/StateHashProbe/GoldenTraceRecorder.cs` records every shipped scenario × seeds 1/7/13 × 730 days (events on, `GoldenPlayer`: planned harvest at 4.5 kg, delouse above 0.4, restock within cash for smolts and feed, one research; no builds) as `src/LakskongenSim.Tests/Fixtures/golden_traces/*.jsonl.gz` + `manifest.json`: a day line with a 64-bit hash over every traced group (env, market, pop by pen, lice, mtb, mort, ops, ledger, cash, rivals) and every SimRng stream, 8-bit group hashes, stream draws, events and commands; a snapshot every 30 days with exact values and a digest tree of the GUID-normalized save. Format, exclusions and workflow: `docs/GOLDEN_TRACES.md`
- `GoldenTraceTests` (trait `Category=GoldenTrace`) re-record in process and compare day by day; a 1-ulp cohort-weight change on day 200 is named by scenario, seed, day and field. Save drift (the whole save differs while the game does not, e.g. new save fields) is reported, never a failure. Bit-exact comparison needs the recording platform's C math library (`MathFingerprint`); elsewhere CI's `verify.yml` / `golden-traces` job records the PR's merge base and head on one runner
- A sim change that must not change the game: `tools/record_golden_traces.sh --base "$(git merge-base HEAD main)"` → "27 of 27 traces identical". A change that does: commit, `tools/record_golden_traces.sh --update`, commit the fixtures with the `--base` diff report and a RUN_LOG §7 row. `--detail` diffs name entity and field on the day. The script records commits (temporary git worktree), never the working tree
- `src/LakskongenSim.Tests/Fixtures/save_v8_facilities.json` is the frozen pre-F2 facilities save (RAS with 2 stocked tanks, SemiSubmersible offshore farm with 2 stocked pens, post-smolt facility with 2 batches, a hatchery cohort; save v8 in the game's wrapper) that F2-03 migrates; `PreF2SaveFixture` generated it, `PreF2SaveFixtureTests` pin it. Never regenerate it

### Farm-type hooks (F2-02)
- The 12 hooks of `docs/FARM_TYPES_SPEC.md` §3.3 are `internal` methods of `SimulationEngine` in `Core/SimulationEngine.FarmTypeHooks.cs` (frozen; the spec's "Landed (F2-02)" note lists every call site). Each dispatches to a lane's file: `Farm/Offshore/OffshoreHooks.cs` (every sea site; offshore holdings) or `Farm/Land/LandHooks.cs` (land-tank holdings; the smolt hooks). A lane fills its own hooks and never edits a hot file or the other lane's file; every default is the pre-F2 behaviour (`FarmTypeHooksTests`)
- The facility ticks live in `Core/LandSiteTickSystem.cs` (RAS tanks, post-smolt modules, land carbon) and `Core/OffshoreTickSystem.cs` (offshore farms, offshore carbon), called by FarmOpex in the order tanks → offshore → post-smolt; containment and preventive hardware live in `Core/SimulationEngine.Containment.cs`. `FarmTypeMoveTests` greps `SimulationEngine.cs` and `InDayTickSystems.cs` for facility or containment tick code and pins the pre-F2 facilities save's 365-day continuation (the goldens never build a facility): an item that changes facility behaviour on purpose replaces a pin with a RUN_LOG §7 row
- A land tank's and an offshore pen's water come from `LandHooks.TankWater` / `OffshoreHooks.PenWater` (through `WaterOf`), so `UnitWaterTemperatureC` is the one water's temperature

### Site kinds and plots (F2-03)
- Everything in the water is a `SeaSite`: `Kind = Fjord` (a fjord locality) or `Kind = Offshore` (an offshore unit: `SeaSite.Offshore` is its `Farm/Offshore/OffshoreUnit.cs`, its zones are `Pen`s, 3 for OF1-class, 6 for Havfarm-class). Everything on land is a `RASFacility`: `Kind = GrowOut` or `PostSmolt` (`Farm/LandSiteKind.cs`; a post-smolt batch is a population in its tanks). `OffshoreFarm` and `PostSmoltFacility` are load-only records: `SimulationEngine.MigrateFacilities` (`Core/SimulationEngine.FacilityMigration.cs`) moves a pre-F2 save's facilities into the F2 kinds after a load (idempotent; save format v9)
- Views: `FjordSites()`, `OffshoreUnits`, `LandSites`, `PostSmoltSites`, `FindOffshoreUnit`, `FindLandSite` (`Core/SimulationEngine.SiteKinds.cs`); list views for the bridge in `Core/FarmUnitViews.cs`. A consumer of `State.SeaSites` decides include, exclude or hook per kind, and the decision goes in `docs/run/evidence/f2_03_sitekind_audit.md`: `SiteKindAuditTests` scans every `SeaSites`/`GetSeaSiteDetails`/`FjordSites(` use and fails on a consumer the audit does not list
- Every unit stands on a plot (`Farm/SitePlot.cs`, `Farm/SitePlotCatalog.cs`; attributes in `godot/data/site_plots.json`, embedded; positions through `ISitePlotGeometry`). A plot is its own water area (`ZoneId` = plot id) in the scenario's production area, so per-fjord-zone systems never see a unit. `GameState.SitePlots` holds plot state only once a unit takes a plot; overflow plots are for migrated saves
- `UnitResolver.Resolve(state, population)` (`Core/UnitResolver.cs`) is the one precedence (land tank, pre-F2 offshore pen, sea-site pen); `UnitResolver.Violations` checks every non-hatchery population sits in exactly one unit with its site and zone ids (hatchery cohorts are exempt)
- Bridge: `GetSeaSiteDetails()` lists the fjord sites (FishPenManager's pen slots and the feed barges are index-keyed), `GetAllSeaSiteDetails()` every sea site (two verbs: a GDScript call never fills a C# default parameter, so GDScript passes every argument; `tests/test_bridge_contract.py` checks every call); units and plots come from the lanes' partials, `GameBridge.Offshore.cs` (`GetOffshoreUnits`, `GetOffshorePlots`) and `GameBridge.LandSites.cs` (`GetLandSites`, `GetLandPlots`). Each lane appends only inside its marked block there and in `SimProxies.gd`/`SimBridge.gd`

### Facility finance, the farm-type catalogue, research gates (F2-04)
- Calibration section `facilities` (registry 1.1.0): `calibration.json` lists `facilitiesIncludes`; each `godot/data/calibration/facilities_<name>.json` fills `facilities.<name>` (`Calibration/CalibrationIncludes.cs`; beside the main file first, then the embedded copies). `FacilitiesCalibration` is a partial record: sim·core owns `Common` (`facilities_common.json`: depreciation lives, construction risk, the catalogue's reference inputs); a lane adds its own section property from its own file. Every leaf carries `[Range]` and exactly one `[Source("L:R1", …)]` or `[GameBalance("why")]` (`Calibration/CalibrationProvenance.cs`); `CalibrationGuardTests` walks the whole section by reflection
- Depreciation: an F2 unit built in the game enters `GameState.FacilityAssets` (`Finance/FacilityAssetRegister.cs`; `RegisterFacilityAsset`) from its commissioning day; the post-tick `ConstructionAndPermitsTickSystem` books `capex × days / (life × 365)` as `FinancialStatements.Depreciation` (non-cash: corporate tax base, not cash, not the rent base; cost line `facility_depreciation`). Fjord, processing and migrated pre-F2 capex never depreciate
- Rent ring-fence: a cost spent on a unit goes through `AttributeUnitCost(unitId, HoldingKind, nok)` (or `AttributePopulationCost`) after it is booked; a unit `IsResourceRentLiable` says no to (land always) is left out of `ResourceRentBaseThisTaxYear()`. A lane that adds a land or offshore cost must attribute it
- `Core/ConstructionProject.cs` (`StartConstructionProject`, `StartConstructionPhase`: phases paid as capex, 2 uniforms per phase start from `facility_construction/<project>/<phase>`, managed overrun at the end, the asset registered at completion) and `Finance/PermitCase.cs` (`SubmitPermitCase`, `AppealPermitCase`, `WithdrawPermitCase`: 2 uniforms per round from `facility_permits/<case>/<round>`, decided post-tick). The lanes own the phase lists and the `PermitTerms`
- Research: `ResearchNode.Unlocks` (catalogue variant ids) and `AppliesTo` (`ResearchScopes`: sea, offshore, land). `land-based-ras` reaches land tanks and opens post_smolt and hfs; `deep-sea-farming` reaches offshore units and opens of1. Read a unit's effect with `ResearchEffectFor(key, HoldingKind)`; `GetResearchEffect(key)` is a fjord pen's
- `Farm/FarmTypeCatalog.cs`: 3 families (sjo, land, hav), 7 variants, the same 7 axes on every card with source keys, every unlock gate; `SimulationEngine.GetFarmTypeCatalog()` evaluates the gates (pure read), `IsFarmTypeUnlocked(variant)` is the build verbs' `not_unlocked` check. Bridge `GameBridge.FarmTypes.cs` → `SimBridge.get_farm_type_catalog()`; no UI script writes a farm-type number (`tests/test_farm_type_catalog.py`)
- `DeterminismScanner.BridgeScope` scans `GameBridge.LandSites.cs`, `GameBridge.Offshore.cs` and `GameBridge.FarmTypes.cs` with the sim library

### Driftsplan, the F2 event catalogue, the notification meter (F2-05)
- A standing order is a `DriftsplanEntry` (saved, `GameState.Driftsplan`, sorted by unit then policy) running an `IDriftsplanPolicy` (`Core/Driftsplan.cs`): `Plan(context)` returns the next `DriftsplanJob` (a dated job, issued on that day's start through `Issue`, the same public verb the player calls; or a waiting one with a `Trigger`). Policies are stateless and pure: every evaluation is checked against every SimRng position and a policy that draws or opens a stream throws `DriftsplanPurityException`
- The phase (`SimulationEngine.RunDriftsplan`, `Core/SimulationEngine.Driftsplan.cs`) runs at the start of every step in `Advance`, before `BeginMortalityDay`/`BeginHoldingDay` and the weather: where a player's commands between days land, so a plan plays exactly as a scripted player (`DriftsplanTests`, day hash every day); inside the step it would book a treatment's deaths on the next ledger day. A failed command slips day by day (the entry keeps `LastOutcome`) and publishes `DriftsplanCommandFailed` (deduped by the bus)
- Verbs: `SetDriftsplan(unitId, policy, numbers, choices)` (unknown_policy, not_found, not_applicable, out_of_range; a change keeps the order's history), `ClearDriftsplan`, `ApplyDriftsplanDefaults(unitId)` (a lane calls it when it builds a unit), reads `GetDriftsplan`, `GetPlannedJobs`, `GetNextPlanned` ("Neste planlagt"), `DriftsplanUnitKindOf`, `UnitLabel`. Bridge `GameBridge.Driftsplan.cs` → `SimBridge.get_driftsplan_catalog/get_driftsplan/set_driftsplan/clear_driftsplan/get_next_planned/get_notification_meter/get_notification_budget`
- A lane adds its policies from its own file by implementing `static partial void AddOffshorePolicies(List<IDriftsplanPolicy>)` / `AddLandPolicies(...)` in `partial class DriftsplanPolicies` (namespace `LakskongenSim.Core`); every setting declares range, default, unit and `Source`; translation keys `DRIFTSPLAN_POLICY_<KIND>`, `DRIFTSPLAN_TIP_<KIND>`, `DRIFTSPLAN_SETTING_<KEY>`, `DRIFTSPLAN_CHOICE_<ID>`, `DRIFTSPLAN_WAIT_<TRIGGER>` (`tests/test_driftsplan.py` checks them). sim·core's fjord policies (`Farm/FjordPolicies.cs`: `net_cleaning`, `delouse_on_count`) are off by default
- UI: `godot/scripts/ui/facilities/DriftsplanCard.gd` (built in code, catalogue-driven, any unit kind) leads with "Neste planlagt"; the pen view mounts it under a fjord site's actions. F2-32/F2-33 mount it on their boards
- Events: the 31 types of spec §3.5 and `DriftsplanCommandFailed` are `[FarmTypeEvent]` rows declared through `F2Row` (severity, deadline rule and counter verb, none defaulted; `FarmTypeEventCatalogTests` reads the spec's table). A lane publishes them with `UnitId` (and `DeadlineDay` where the rule is `SetByPublisher`); the 12 farm-type verbs open the `offshore` or `ras` tab until the boards exist. sim·core publishes `ConstructionPhaseComplete`, `CapexOverrun` and `PermitDecision`
- `engine.Notifications` (`Core/NotificationMeter.cs`) counts the warnings and criticals the bus delivered per unit (`UnitId`, else `SiteId`, else the company) and sim month, against the §6.6 budget (≤ 2 warnings a unit a month, ≤ 1 critical a unit a year: `OverBudget()`); presentation state, not saved, emptied with the bus
- The runtime snapshot saves the market's daily price queue (`MktDailyPrices`): a loaded game's weekly mean and price chart continue

### CompetitorAI
- 7 parody competitors (Mowier, SalMax, Herøy, Greed Gregarious, Offshore Optimists, NordLaks Nord)
- Quarterly `AdvanceQuarter` (every 90 days) with financial updates, distress check, strategic actions
- Actions: Harvest, Acquisition, LicenseBid (legacy, now separate from auction), PriceWar, Lobbying, International
- Distress triggers: `FinancialHealth < 0.30` || `LeverageRatio > 5.0` || `CashReserves <= 0`
- AI-vs-AI acquisitions in `SimulateAIAcquisitions` (5%/quarter)

### CrisisCapitalismSystem
- Crisis types: `DISEASE_OUTBREAK`, `BANKRUPTCY`, `REGULATORY_SHUTDOWN`, `MARKET_CRASH`, `ENVIRONMENTAL_SCANDAL`
- Asset types: `LICENSE`, `FARM_SITE`, `PROCESSING_PLANT`, `EQUIPMENT`
- Discounts: 20-80% based on crisis type + severity
- Assets expire on `ExpiresOnDay` (simulation day; `TimeRemainingMonths` is derived from it)
- Player purchase: `PurchaseDistressedAsset` → `AcquisitionResult` with integration risk + reputation impact

### Licence Auctions (Phase 4.1)
- `LicenseAuctionSystem` owns `license_auction` RNG stream
- `ApplyForGrowthLicence` → opens auction (playerEntered=true)
- `BidOnLicenseAuction` → validates min bid, balance
- `TickLicenseAuctions` (post-tick, before balance mirror):
  1. Rival raises (one per auction per day, p=0.3+0.5×appetite, bid ≤ maxPrice)
  2. Settle closed auctions (highest solvent bidder)
  3. Rival opener if no open auction (p=0.02, eligible = not Red + no player site + no player licence)
  4. Prune settled >90 days old (keep unconsumed won_player)

### Save/Load
- `GameState` serialized via `SaveJson.Options` (System.Text.Json)
- `AdoptSavedState` clears & restores all collections including `LicenseAuctions`, `ActiveCrises`, `DistressedAssets`
- `RestoreFromLoad` imports RNG stream positions (critical for determinism)
- `ExactSaveHashTests` + `EngineTwinRunDeterminismTests` + `PlayerScriptProbe` are the determinism gates

### Mortality ledger (S2-05)
- Every fish leaving a cohort goes through `FishPopulation.RemoveDead(cohort, n, MortalityCause)` or `RemoveAlive(cohort, n, FishRemoval)`; never decrement `Cohort.Count` directly (`MortalityLedgerEngineTests` checks lost == booked for every cohort every day)
- Causes map to the NVI level-1 classes (`MortalityCauses.CategoryOf`); escapes, harvests and transfers are removals, not deaths; holdings without cohorts book with `MortalityLedger.BookHoldingDeaths`
- Hazards read a `CohortEnvironment` from `SimulationEngine.ResolveCohortEnvironment` (RAS tank / offshore pen / sea pen), never a global zone value; it is built from `HoldingWaterOf` plus the holding id, CO2 and wave height (`CohortEnvironment.From`); a new farm type extends `HoldingOf`/`WaterOf` and adds a cause
- Low oxygen (wave 2 sim-a-1): one hazard, `MortalityHazards.LowOxygenFraction` at the fish's own water (none at or above 3 mg/L); the bloom deck's `hypoxia` card is an in-cage oxygen sag carried by the pens' `HoldingWater`, and a plankton crash takes only the oxygen its carbon needs (`waterOxygen` registry section, docs/BALANCE.md §1i)
- Saved in `MortalityModelSnapshot.Ledger`; bridge: `GameBridge.GetMortalityBreakdown` → `SimBridge.get_mortality_breakdown`

## Adding New Features Checklist
1. **Determinism**: Use `SimRng` streams, no `DateTime.Now`/`Guid`/`Random`
2. **Calibration**: All numbers in `BalancingConfig` / `CalibrationRegistry`
3. **Tick System**: Implement `ITickSystem` + register in `_inDayPhases` or `_postTickPhases`
4. **Snapshotable**: Implement `ISnapshotable` for save/load
5. **Player-Path Test**: Add to `PlayerScriptProbe` or dedicated test showing outcome change
6. **Bridge**: Add C# partial in `GameBridge.*.cs` + GDScript proxy in `SimProxies.gd` + forwarder in `SimBridge.gd`
7. **Tests**: Unit + integration + determinism (twin engine, save round-trip)
8. **Screenshots**: `./tools/capture_screenshots.sh` must produce non-identical frames

## Common Patterns

### Engine Command
```csharp
public CommandResult MyCommand(string arg)
{
    if (State == null || FinanceManager == null) 
        return CommandResult.Fail("engine_not_ready", "Engine not initialized");
    // ... logic ...
    return CommandResult.Success(message, value: 123.0, id: "optional-id");
}
```

### Bridge Partial
```csharp
public Godot.Collections.Dictionary MyBridgeMethod(string arg)
{
    var result = new Godot.Collections.Dictionary();
    if (SimulationEngine == null) { result["success"] = false; result["message"] = "Not initialized"; return result; }
    var outcome = SimulationEngine.MyCommand(arg);
    result["success"] = outcome.Ok;
    result["message"] = outcome.Message;
    if (outcome.Ok) EmitSignal(SignalName.GameStateUpdated);
    return result;
}
```

### SimProxies Proxy
```gdscript
func my_bridge_method(arg: String) -> Dictionary:
    return game_bridge.MyBridgeMethod(arg) if game_bridge else {"success": false, "message": "Not initialized"}
```

### SimBridge Forwarder
```gdscript
func my_bridge_method(arg: String) -> Dictionary: return _proxies.my_bridge_method(arg)
```

### Telling the player (typed event bus, roadmap U2-06)
- Add a `GameEventType` with its catalog row (`Events/GameEventCatalog.cs`: id, dedupe category, severity, counter verb or `none`, deadline rule), a `GAME_EVENT_<ID>` title in `translations.csv`, and publish it: `engine.PublishGameEvent(new GameEventRequest(type, day) { SiteId = …, Message = … })` for something that happens, or a `_raise(..., _ev("type_id"))` condition in `SimState.gd` for something that holds.
- Severity picks the channel (`ConsequenceDirector.route_for`): critical = a pausing card with actions, warning = one toast + an unread inbox entry + the bell's badge, info = the inbox log. The bus drops repeats of (category, subject, variant) within 3 days and delivers escalations.
- Never decide anything by reading message text (`tests/test_event_bus.py` greps for it). New counter verbs go in `GameEventCatalog`, `GameEventText.VERBS` and `GameEventActions`.

### Pacing the clock (roadmap U2-07)
- The game clock has one owner, `GameBridge.Clock.cs`: speeds 1×/3×/10×/30× (keys 1–4), Space pause, N "Til neste hendelse", and holds. UI that waits for the player holds the clock (`SimBridge.hold_clock(reason)` / `release_clock(reason)`; the critical card, the pause menu and the briefing do) instead of calling `set_paused`, which is the player's own pause. Draw the clock from `SimBridge.clock_changed` / `get_clock_state()`, never from a flag of your own.
- Deadlines slow and stop the clock by the farm-types rule (`PacingRules`: 1× at ≤ 7 days left, stop at ≤ 1 day left without a response). An event with a counter verb and a `DeadlineDay` (catalog `GameEventDeadlineRule.SetByPublisher`) is watched as soon as the bus delivers it; anything else registers with `engine.Pacing.Register(new PacingDeadline { Key, DeadlineDay, … })` or `SimBridge.register_pacing_deadline(...)`. The publisher resolves the deadline when its cause is met (`engine.Pacing.Resolve(key)`; the event's key is its dedupe key), as `PublishStandingOrders` does for Mattilsynet's orders.
- A player's new game starts paused on its `BriefingCard`; QA starts (seeded) and loads do not show one. The calendar the player reads is `GameCalendar.gd` (ISO weeks; it mirrors `SimCalendar`).

## Godot / C# Interop Notes
- Godot resolves autoload by filename: `GameBridge.cs` must exist as anchor
- Partials: `GameBridge.Core.cs`, `GameBridge.Farm.cs`, `GameBridge.Market.cs`, `GameBridge.Depth.cs`, `GameBridge.Economy.cs`, `GameBridge.Licence.cs`, `GameBridge.Content.cs`, `GameBridge.Events.cs` (the `GameEventRaised` signal of the typed event bus), `GameBridge.Clock.cs` (the game clock: speeds, holds, pacing, `ClockChanged`), `GameBridge.LandSites.cs` / `GameBridge.Offshore.cs` (F2-03: the land and sea lanes' slices, one marked append block each)
- `InternalsVisibleTo("LakskongenSim.Tests")` in `SaveJson.cs` allows tests to call `internal` methods (`TickLicenseAuctions`, `DrainDayNotes`, etc.)
- `Nullable enable`, `ImplicitUsings`, `TreatWarningsAsErrors` in csproj

## Debugging Tips
- **Determinism failure**: Run `EngineTwinRunDeterminismTests` or `ExactSaveHashTests` — they print divergent stream positions
- **RNG divergence**: Check `SimRng.ImportStates` only imports keys already in `_streams` — streams MUST be created eagerly in `Initialize`
- **Bridge contract test fails**: `tests/test_bridge_contract.py` parses all `GameBridge*.cs` — new methods must be in partials
- **Save hash changes**: Any new serializable field in `GameState` changes hash — update golden files or accept comparative test
- **godot MCP broken (esp. after CI)**: run `python3 tools/mcp_bridge_alias.py` — expect `OK: alias written …` + `OK: auth probe authed` (exit 0). Exit 2 = on-disk token ≠ editor memory → restart the Godot editor, then re-run. Root cause + full procedure: `docs/research/MCP_CAPABILITIES.md` §3

## Performance Targets
- 60 FPS at 1440p on M1 Pro (Metal 3.2 Forward+)
- Headless `Advance(3650)` < 2s
- `dotnet test` < 10s for 667 tests

## References
- `docs/NEXT_LEVEL_PLAN.md` — full phase roadmap
- `docs/AUTONOMOUS_TOP_TIER_RUN.md` — master plan for the top-tier commercial run (waves, research briefs, gates)
- `docs/RUN_LOG.md` — normative run protocol + status ledger (read every autonomous session)
- `docs/REALISM_CHECKLIST.md` — calibration bands
- `docs/VISION.md` — product vision
- `CLAUDE.md` (repo root) — environment, main project pointer