# F2-03 site-kind audit

Roadmap item F2-03 (docs/FARM_TYPES_SPEC.md §3.1 decision 4 and §7.1 acceptance 1), branch `wave3/sim-core-2`, 2026-10-16.

F2-03 makes everything in the water a `SeaSite` and everything on land a `RASFacility`:

| Kind | Where | What it is |
|---|---|---|
| `SeaSite.Kind = Fjord` | a fjord zone of the scenario (`po3_nord`…) | a fjord locality with its pen frame (S2-01b) |
| `SeaSite.Kind = Offshore` | an offshore slot (`off_1`, `off_2`, `hav_1`, overflow slots) | an offshore unit: `SeaSite.Offshore` (`Farm/Offshore/OffshoreUnit.cs`) and its zones as `Pen`s (3 for OF1-class, 6 for Havfarm-class) |
| `RASFacility.Kind = GrowOut` | a land pad (`pad_a`, `pad_b`, overflow pads) | a grow-out land site |
| `RASFacility.Kind = PostSmolt` | `pad_delta` (or an overflow pad) | a post-smolt land site; its batches are populations in its tanks |

Every unit on a plot sits in its plot's own water area: its `ZoneId` is the plot id, in the scenario's production area. No fjord zone holds it, so the systems that work per fjord zone (water chemistry and biomass load, the traffic light, the bloom deck, the red-zone orders, auctions by zone, cohort transfers) never see an offshore unit or a land site without a single line of their own changing. The remaining consumers of the sea-site list are decided below.

**Decisions.** `include`: an offshore unit takes part as the sea site it is. `exclude`: it does not, by its kind (`Kind == Fjord`, `FjordSites()`), by its zone (its plot is no fjord zone) or by state that a verb refusing it keeps (no skirt, no barrier, open net). `hook`: an F2-02 hook or an F2-03 predicate decides per kind, so a lane can change it in its own file (`Farm/Offshore/OffshoreHooks.cs`) without touching the consumer.

**Categories.** Each category has one test in `src/LakskongenSim.Tests/Integration/SiteKindAuditTests.cs` (trait `SiteKind` = the category) on one game holding every kind (po3_red_sone's two fjord sites stocked, a stocked offshore unit on `off_1`, a stocked grow-out land site on `pad_a`, a post-smolt site on `pad_delta`):

| Category | What it covers | Test |
|---|---|---|
| units-and-lists | the list, lookups by id, the unit views, saves and the bridge's lists | `UnitsAndLists_FindEveryKindById_AndSplitTheKinds` |
| fjord-zone | per-zone systems: traffic light (a unit's larval output, never its fish, F2-08), zone water load, bloom deck, red-zone orders, auctions, transfers | `FjordZoneSystems_NeverSeeAnOffshoreUnitsFishOrALandSite` |
| site-limits | the remote operations centre's watch budget (`CountsTowardSiteLimits`) | `SiteLimits_CountTheFjordSites_NotTheOffshoreUnit` |
| fjord-economics | fjord crews, upkeep, per-site carbon, the night shift | `FjordEconomics_TheUnitPaysAndEmitsOnlyItsOwn` |
| storm-wear | fjord storm net wear, storm escapes, storm deaths and repairs | `StormWear_HitsTheFjord_NeverTheOffshoreUnit` |
| lice | the site lice model, the weekly census, Mattilsynet orders, the company average | `Lice_TheUnitIsCountedAndRegulated_AtItsPlotsPressure` |
| capacity | the area's pooled licence MTB, the stocking checks, enforcement | `Capacity_TheAreaPoolHoldsTheFjord_TheUnitHoldsItsOwnMtb` |
| harvest-wellboat | harvests, wellboat jobs, delousing, the advisors | `HarvestAndWellboats_ReachTheOffshoreUnit` |
| escapes-sustainability | escapes, sustainability and welfare scores, sludge, medals | `EscapesAndSustainability_CountTheUnit` |
| events | storyteller cards (fjord cards, ISA, company cards), scenario markers | `Events_FjordCardsSkipTheUnit_IsaReachesIt` |
| fjord-hardware | containment rungs and red-zone credit, skirts, deterrents, barriers, bloom countermeasures | `FjordHardware_NeverAppliesToTheUnit` |
| reports | day summary, dashboard, HUD, tutorial and rival approximations | `Reports_CountEveryUnitOnce` |

**Completeness.** `TheAudit_ListsEveryConsumer_WithADecision` scans `src/LakskongenSim` and `godot/scripts/autoload` (C#) and every GDScript file under `godot/scripts` (comments stripped) for `SeaSites` and `GetSeaSiteDetails`, keyed by file and enclosing member with its number of uses, and fails when a consumer appears, disappears or changes its number of uses without this table following; `TheAudit_ListsEveryFjordViewConsumer` does the same for the fjord view (`FjordSites`), the consumers F2-03 moved off the list. `TheScan_FindsTheKnownConsumers` guards the scanner itself.

**Totals.** 153 consumers (228 uses) of the list (F2-04 added `EscapeFreeDays`, F2-05 four Driftsplan readers, the wave-3 integration U2-04's feeding plans, ui-a's daily snapshot and U2-01's expansion verdicts, which took over `ApplyForGrowthLicence`'s and `CanBuildSeaSite`'s reads; F2-06/F2-07 added the open sea, the unit build, the legacy commissioning and the harvest-order sea gate; F2-13 four transfer readers, and the bridge's `TransferBatchToSea` left the list as a thin call into the engine, `wave4/sim-land-2`): 96 include, 45 exclude, 12 hook; 18 readers of the fjord view (16 exclude, the 2 views themselves include; F2-31 added the build diagnosis's farmed areas, F2-13 the post-smolt planner and the batch transfer, F2-09 the conversion checklist's area mortality and the dossier's lice compliance). The spec's estimate was about 99 in the sim and 58 in the bridge counted by use; a member that reads the list several times is one row here with its number of uses.

## Consumers

| File | Member | Uses | Category | Decision | How |
|---|---|---|---|---|---|
| `godot/scripts/autoload/GameBridge.Core.cs` | `CheckDiseaseOutbreaks` | 1 | lice | include | Null guard only; the weekly outbreak check reads the company lice model, which counts the unit's fish |
| `godot/scripts/autoload/GameBridge.Core.cs` | `DaysSinceStocking` | 2 | reports | include | The latest stocking of any stocked sea site: an offshore unit's stocking counts as one |
| `godot/scripts/autoload/GameBridge.Core.cs` | `MinNetIntegrity` | 2 | storm-wear | include | The lowest net of any sea site: the unit's net takes no fjord storm wear (hook `SkipsFjordStormWear`) and stays 100 until F2-08's net tears lower it |
| `godot/scripts/autoload/GameBridge.Core.cs` | `SiteCount` | 1 | units-and-lists | include | The HUD's count of the company's sea sites: an offshore unit is one |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `BuildSeaSite` | 1 | units-and-lists | include | Null guard before the engine's fjord build (`SimulationEngine.BuildSeaSite`, which builds a fjord site) |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `CleanBarrier` | 1 | fjord-hardware | exclude | By state: `InstallBarrier` refuses a non-fjord site, so a unit's `Barrier` stays None and the verb refuses it |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `DeployCountermeasure` | 1 | fjord-hardware | exclude | By kind: looks the site up with `Kind == Fjord` (the bloom deck spawns in fjord zones only) |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `GetBarrierStatus` | 1 | fjord-hardware | exclude | By kind: `if (site.Kind != Fjord) continue` |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `GetCarbonIntensity` | 1 | fjord-economics | include | Every sea site's tracker: the unit's own fuel, power and transport are on its sea site's one tracker (no `OffshoreFarms` term any more, no double count) |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `GetCarbonLedger` | 3 | fjord-economics | hook | The `sea_sites` rows skip the unit by kind and list it under `offshore` (`OffshoreUnits`); the processing plant reads the first sea site's tracker and `this_year_net_kg` sums every sea site's tracker once |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `GetGillRisk` | 1 | fjord-zone | include | By id: reads the unit's own gill score, which the fjord bloom deck never lowers (zone) |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `GetSludgeStatus` | 1 | escapes-sustainability | include | Every sea site's sludge: the unit collects sludge like any sea site |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `InstallBarrier` | 1 | fjord-hardware | exclude | By kind: `Kind == Fjord` in the lookup (a permeable barrier is fjord-pen hardware) |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `IsCarbonNeutralYear` | 1 | fjord-economics | include | As `GetCarbonIntensity`: every sea site's tracker once |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `QualifiesForEnvironmentalFlexibility` | 1 | fjord-hardware | exclude | By zone and state: the unit's zone (its plot) has no traffic-light colour and its containment stays open net |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `SetQaPenLice` | 1 | units-and-lists | include | QA-only verb by site id (screenshot harness); any sea site |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `SpawnQaBloom` | 1 | units-and-lists | include | QA-only verb by site id (screenshot harness); the game's bloom deck itself never reaches a unit |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `ToggleSludgeHub` | 2 | escapes-sustainability | include | By id, and whether any sea site already has the hub: a unit can connect to the shore hub |
| `godot/scripts/autoload/GameBridge.Depth.cs` | `ValorizeSludgeNow` | 1 | escapes-sustainability | include | By id: a unit's sludge is valorized like any sea site's |
| `godot/scripts/autoload/GameBridge.Economy.cs` | `GetActiveLicenses` | 1 | capacity | exclude | By kind: `if (site.Kind != Fjord) continue` (a unit holds no sea licence until F2-09 binds its development licences) |
| `godot/scripts/autoload/GameBridge.Expansion.cs` | `BuildExpansionSite` | 1 | units-and-lists | include | U2-01 (wave-3 integration): looks the site the build verb made up by id for its label; any sea site (the verb builds a fjord site) |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `FjordGillAverage` | 1 | fjord-zone | exclude | By kind: the intervention ranking's gill input is the fjord sites' mean (blooms never reach a unit) |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `GetAttentionItems` | 2 | reports | include | Every sea site with critical gills gets an inbox item: a unit's gills only fall if a later item makes them |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `GetBloomMitigations` | 1 | fjord-hardware | exclude | By kind: only fjord sites are listed as installed (`s.Kind == Fjord &&`) |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `GetSeaSiteDetails` | 1 | units-and-lists | exclude | The fjord sites only (`SeaSiteDetails(includeOffshore: false)`): the fjord's index-keyed consumers (FishPenManager's pen slots, the feed barges, the fjord lists) never see a unit; no argument, because Godot never fills a C# default (tests/test_bridge_contract.py) |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `SeaSiteDetails` | 1 | units-and-lists | hook | The builder of both lists: `includeOffshore` false for `GetSeaSiteDetails` (fjord), true for `GetAllSeaSiteDetails` (every sea site); every row carries `kind` and `plot_id`; units also have `GetOffshoreUnits` |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `GetSiteNames` | 1 | units-and-lists | include | Every sea site's id and label (label lookups for event text) |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `InstallBloomMitigation` | 1 | fjord-hardware | exclude | By kind: `Kind == Fjord` in the lookup |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `OrderDelousing` | 1 | harvest-wellboat | include | "all_sites" delouses every sea site through `DelouseSite`, the unit included |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `RepairNets` | 1 | storm-wear | include | By id: a unit's nets can be repaired (F2-08 net tears) |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `SetWatchMode` | 1 | site-limits | exclude | By kind: refuses a unit (crewed on board); the budget sums only the sites that count toward the limits (`RemoteOpsAttentionUsed`) |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `SiteLabel` | 1 | units-and-lists | include | Label lookup by id |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `ToggleMitigation` | 1 | fjord-hardware | exclude | By kind: a lice skirt and a seal deterrent are fjord-pen hardware (`Kind == Fjord` in the lookup) |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `UpgradeContainment` | 1 | fjord-hardware | exclude | By kind: the containment ladder is a fjord locality's (F2-10 moves the verb into the engine) |
| `godot/scripts/autoload/GameBridge.Farm.cs` | `VaccinateSite` | 1 | units-and-lists | include | By id: a unit's fish can be vaccinated like any sea site's |
| `godot/scripts/autoload/GameBridge.FeedPlans.cs` | `GetFeedPlan` | 1 | units-and-lists | include | U2-04 (wave-3 integration): by id, any sea site's plan, an offshore unit's too; an unknown id reports the standard plan with site_id "" |
| `godot/scripts/autoload/GameBridge.FeedPlans.cs` | `GetFeedPlans` | 1 | units-and-lists | include | U2-04 (wave-3 integration): every sea site's plan in site order, the unit's beside the fjord sites' |
| `godot/scripts/autoload/SaveLoadManager.cs` | `ValidateAndFixGameState` | 6 | units-and-lists | include | Load defaults for every sea site (population site ids, carbon tracker, sludge, gills); a unit's structure (`Offshore ??= new`), its stored credit (1.0) and its watch mode (`onboard`) |
| `godot/scripts/bridge/SimActions.gd` | `get_all_pens` | 1 | units-and-lists | exclude | Calls the fjord verb `GetSeaSiteDetails()`: every GDScript consumer of `get_all_pens` (FjordVistaBridge -> FishPenManager, Dashboard, SiteOverlayLegend, OffshorePanel's containment and transfer lists, BriefingCard, Telemetry, WeatherDisease, DataExport, GameEventActions, ScreenshotHarness) sees the fjord sites only |
| `godot/scripts/bridge/SimState.gd` | `_refresh_state` | 1 | units-and-lists | exclude | Wave-3 ui-a (one snapshot a day): calls the fjord verb `GetSeaSiteDetails()` for `sea_sites`, which FjordVistaBridge hands FishPenManager's index-keyed pen slots; the fjord sites only, as `get_all_pens` |
| `src/LakskongenSim/Advisor/AdvisorDemo.cs` | `CreateSampleGameState` | 1 | units-and-lists | include | Demo program's sample state (one fjord site); not engine code |
| `src/LakskongenSim/Advisor/AdvisorDemo.cs` | `RunDemo` | 2 | units-and-lists | include | Demo program's printout of its sample sites; not engine code |
| `src/LakskongenSim/Advisor/IntelligentAdvisor.cs` | `AnalyzeAndGenerateRecommendations` | 1 | harvest-wellboat | include | The advisors' risk view (spec §3.1 decision 4): every sea site's water, pens and management |
| `src/LakskongenSim/Advisor/InterventionAdvisor.cs` | `Rank` | 3 | harvest-wellboat | include | The advisors' risk view: the first stocked sea site (a unit when it is the only stocked one) and whether any site exists |
| `src/LakskongenSim/Core/AchievementSystem.cs` | `NetWorth` | 6 | escapes-sustainability | include | Medal conditions: site counts (MultiSiteOperator, VerticalIntegration, IndustryLeader) count a unit as a site; the containment-credit and bloom-countermeasure medals are fjord-only by state (the verbs refuse a unit) |
| `src/LakskongenSim/Core/DaySummary.cs` | `AddEmpireEntries` | 1 | reports | include | Sites = sea sites + land sites: each unit once (units are sea sites, post-smolt sites land sites) |
| `src/LakskongenSim/Core/DaySummary.cs` | `CalculateAverageFishWeight` | 1 | reports | include | Sea sites' pens + land sites' tanks: each fish once (no `OffshoreFarms` term after the migration) |
| `src/LakskongenSim/Core/DaySummary.cs` | `CountAllFish` | 1 | reports | include | As `CalculateAverageFishWeight` |
| `src/LakskongenSim/Core/DaySummary.cs` | `CountHarvestReadyFish` | 1 | reports | include | Sea sites (the unit) and grow-out land sites; post-smolt sites' smolts are never harvest-ready |
| `src/LakskongenSim/Core/GameState.cs` | `SeaSites` | 1 | units-and-lists | include | The list itself: fjord sites and offshore units (`SeaSite.Kind`) |
| `src/LakskongenSim/Core/GameStateRelinker.cs` | `RelinkIds` | 2 | units-and-lists | include | Relinks every sea site's pen populations to the game's list (a unit's zones too) |
| `src/LakskongenSim/Core/InDayTickSystems.cs` | `Tick` | 2 | reports | include | Hot file (F2-02 only): the rivals' quarterly inputs approximate the player's assets and licences by sea-site count; a unit counts as a site (it will hold development licences, F2-09) |
| `src/LakskongenSim/Core/OffshoreTickSystem.cs` | `AccountCarbon` | 1 | fjord-economics | include | Offshore units only (`site.Offshore`): their fuel, power, crew helicopter and supply vessel on the site's tracker |
| `src/LakskongenSim/Core/OffshoreTickSystem.cs` | `Tick` | 1 | units-and-lists | include | Offshore units only (`Kind == Offshore`): each unit's F2-07 day (construction and tow, bookings, silo, crew, structure and nets, teething, its own lines) |
| `src/LakskongenSim/Core/SaveLoadManager.cs` | `Deserialize` | 1 | units-and-lists | include | Null default of the list |
| `src/LakskongenSim/Core/ScenarioSystem.cs` | `ApplyDiseaseMarker` | 1 | events | include | By id from scenarios.json (scenario sites are fjord sites) |
| `src/LakskongenSim/Core/ScenarioSystem.cs` | `ApplyMitigations` | 1 | fjord-hardware | include | By id from scenarios.json: scenario mitigations name fjord sites |
| `src/LakskongenSim/Core/ScenarioSystem.cs` | `ApplyStartingState` | 2 | units-and-lists | include | Builds the scenario's fjord sites (idempotent by id) |
| `src/LakskongenSim/Core/SimulationEngine.Capacity.cs` | `EnforceCapacity` | 2 | capacity | hook | `CountsTowardAreaLicencePool` (OffshoreHooks): a unit's fish stay out of the area's pooled licence MTB until F2-09; its own MTB caps its stocking |
| `src/LakskongenSim/Core/SimulationEngine.Capacity.cs` | `GetCapacityOverview` | 2 | capacity | hook | `CountsTowardAreaLicencePool`: the overview lists the fjord sites; units are in `GetOffshoreUnits` (`mtb_tonnes`) |
| `src/LakskongenSim/Core/SimulationEngine.Capacity.cs` | `MigrateCapacityRegisterAfterLoad` | 1 | capacity | hook | `CountsTowardAreaLicencePool`: a pre-S2-01 save grants licences for its fjord sites only |
| `src/LakskongenSim/Core/SimulationEngine.Capacity.cs` | `PreviewStocking` | 1 | capacity | exclude | Refuses a unit (`offshore_unit`): its zones are stocked through `StockOffshoreFarm` |
| `src/LakskongenSim/Core/SimulationEngine.Capacity.cs` | `SeaSitesInArea` | 2 | capacity | hook | `CountsTowardAreaLicencePool`: the area's standing biomass is its fjord sites' |
| `src/LakskongenSim/Core/SimulationEngine.Commands.cs` | `AdoptSavedState` | 3 | units-and-lists | include | Restores every sea site of a save (then `MigrateFacilities` moves a pre-F2 save's facilities in) |
| `src/LakskongenSim/Core/SimulationEngine.Commands.cs` | `DelouseSite` | 2 | harvest-wellboat | include | By id: a unit is deloused like any sea site (its own lice model) |
| `src/LakskongenSim/Core/SimulationEngine.Commands.cs` | `HarvestSite` | 2 | harvest-wellboat | include | By id: express harvest through the S2-04 pipeline (`FindHarvestHolding`: offshore wellboat route) |
| `src/LakskongenSim/Core/SimulationEngine.Commands.cs` | `RequestWellboat` | 4 | harvest-wellboat | include | By id: wellboat jobs (ROV, freshwater, harvest) run at a unit; `OperationPossible` (F2-06) gates its sea state |
| `src/LakskongenSim/Core/SimulationEngine.Commands.cs` | `StockSmolts` | 2 | capacity | exclude | Delegates to the pen stocking, which refuses a unit (`offshore_unit`) |
| `src/LakskongenSim/Core/SimulationEngine.Containment.cs` | `ApplyRedZoneContainmentCredit` | 1 | fjord-hardware | exclude | By zone and state: a unit's zone never has a traffic-light colour, and its containment stays open net (the containment verb refuses it), so no credit; `AppliedContainmentCredit` stays 1.0 |
| `src/LakskongenSim/Core/SimulationEngine.Containment.cs` | `PreventiveMitigationDailyOpexNok` | 1 | fjord-hardware | exclude | By state: a unit never has a skirt or a seal deterrent (`ToggleMitigation` refuses it), so it pays no upkeep |
| `src/LakskongenSim/Core/SimulationEngine.Containment.cs` | `UpdatePreventiveCoverage` | 1 | fjord-hardware | include | The company rollup's coverage counts a unit's fish as unblocked (open net, no skirt or barrier), as it counts them in the company lice average; its open-net rung's weekly opex is 0 |
| `src/LakskongenSim/Core/SimulationEngine.Driftsplan.cs` | `DriftsplanUnitKindOf` | 1 | units-and-lists | include | F2-05: lookup by id: a fjord site is a `FjordSite` and an offshore unit an `OffshoreUnit` Driftsplan unit; each policy declares the kinds it applies to (`not_applicable` otherwise) |
| `src/LakskongenSim/Core/SimulationEngine.Driftsplan.cs` | `PublishDriftsplanFailure` | 1 | events | include | F2-05: a standing order that failed on a sea site (fjord or unit) names the site as well as the unit, so the counter verb opens it |
| `src/LakskongenSim/Core/SimulationEngine.Driftsplan.cs` | `UnitLabel` | 1 | units-and-lists | include | Label lookup by id (any sea site, then the land sites) |
| `src/LakskongenSim/Core/SimulationEngine.Expansion.cs` | `ApplyVerdictOf` | 1 | fjord-zone | exclude | U2-01 (moved here from `ApplyForGrowthLicence`): by zone, "already operating in the zone" never matches a unit's plot zone |
| `src/LakskongenSim/Core/SimulationEngine.Expansion.cs` | `AreaView` | 1 | fjord-zone | exclude | U2-01: orders an area's map zones by the company's sites in each, by zone; a unit's plot zone is no map zone |
| `src/LakskongenSim/Core/SimulationEngine.Expansion.cs` | `BuildGateKey` | 2 | fjord-zone | exclude | U2-01 (moved here from `CanBuildSeaSite`; the wave-3 integration keeps F2-03's rule): a null guard, then by zone (organic growth never matches a unit's plot zone); the first-site right reads `FjordSites()` (`SiteLimits_CountTheFjordSites_NotTheOffshoreUnit`) |
| `src/LakskongenSim/Core/SimulationEngine.Expansion.cs` | `PlanSiteBuild` | 3 | fjord-zone | exclude | U2-01: a null guard and, by zone, "no site in the zone yet" and the company's sites in the zone (a unit's plot zone is no map zone); "new zone" also needs a fjord site (`FjordSites()`, F2-03's development right) |
| `src/LakskongenSim/Core/SimulationEngine.Expansion.cs` | `ZoneView` | 1 | fjord-zone | exclude | U2-01: the company's sites in a map zone, by zone |
| `src/LakskongenSim/Core/SimulationEngine.Facilities.cs` | `BuildOffshoreFarm` | 3 | units-and-lists | include | Creates the unit as an offshore sea site on the first free slot its class is rated for (`no_free_plot` otherwise) |
| `src/LakskongenSim/Core/SimulationEngine.FacilityMigration.cs` | `MigrateFacilities` | 1 | units-and-lists | include | Adds each pre-F2 offshore farm as an offshore sea site (same id, zones merged pairwise) |
| `src/LakskongenSim/Core/SimulationEngine.FacilityProduction.cs` | `TransferPostSmoltToSea` | 1 | capacity | exclude | A post-smolt batch goes to a fjord site; refuses a unit (`offshore_unit`) until F2-07's offshore stocking |
| `src/LakskongenSim/Core/SimulationEngine.FarmTypes.cs` | `EscapeFreeDays` | 1 | escapes-sustainability | include | F2-04: the farm-type catalogue's escape-free gate (spec §2C, 12 escape-free months before the development-licence round) is the company's: an escape from an offshore unit breaks it as one from a fjord site does |
| `src/LakskongenSim/Core/SimulationEngine.FeedPlans.cs` | `ApplyFeedPlans` | 2 | units-and-lists | include | U2-04 (wave-3 integration): every sea site's plan reaches its pens each morning; an offshore unit's zones are its `Pens` and take the plan like a fjord site's (whether feed reaches them is the F2 hook `FeedAvailableFor`); `UnitsAndLists_FindEveryKindById_AndSplitTheKinds` |
| `src/LakskongenSim/Core/SimulationEngine.FeedPlans.cs` | `FeedPlanOf` | 1 | units-and-lists | include | U2-04 (wave-3 integration): by id, any sea site |
| `src/LakskongenSim/Core/SimulationEngine.FeedPlans.cs` | `ProjectFeedPlan` | 1 | units-and-lists | include | U2-04 (wave-3 integration): by id; the twin-lite grows the unit's pens in their own water (`HoldingOf` -> `WaterOf`) |
| `src/LakskongenSim/Core/SimulationEngine.FeedPlans.cs` | `SetFeedPlan` | 1 | units-and-lists | include | U2-04 (wave-3 integration): by id, any sea site; the unit's pens take the ration at once |
| `src/LakskongenSim/Core/SimulationEngine.FeedPlans.cs` | `SetFeedPlanForAllSites` | 4 | units-and-lists | include | U2-04 "Fôr alle" (wave-3 integration): one company feeding decision for every sea site, the offshore unit's too (`Value` counts it) |
| `src/LakskongenSim/Core/SimulationEngine.GameEvents.cs` | `PublishStandingOrders` | 2 | lice | include | Mattilsynet's standing treatment orders: a unit over its lice limit gets one (and its pacing deadline) |
| `src/LakskongenSim/Core/SimulationEngine.Harvest.cs` | `FindHarvestHolding` | 1 | harvest-wellboat | include | A unit is an `Offshore` holding: the offshore wellboat route and rent (`IsResourceRentLiable`) |
| `src/LakskongenSim/Core/SimulationEngine.Licence.cs` | `AcquireDistressedAsset` | 1 | units-and-lists | include | Adds the acquired fjord site (a rival's locality) |
| `src/LakskongenSim/Core/SimulationEngine.Licence.cs` | `BuildSeaSite` | 3 | units-and-lists | include | The fjord build: null guard, the next fjord name (`SiteNames.NextFor` skips names in use) and the add |
| `src/LakskongenSim/Core/SimulationEngine.Licence.cs` | `MaybeOpenRivalAuction` | 1 | fjord-zone | exclude | By zone: a fjord zone is "player-operated" only through a fjord site |
| `src/LakskongenSim/Core/SimulationEngine.Offshore.cs` | `BuildOffshoreUnit` | 3 | units-and-lists | include | F2-07: the next offshore id among the offshore units (and a load-only farm's), the add, and its undo should the project refuse |
| `src/LakskongenSim/Core/SimulationEngine.OffshoreOps.cs` | `CommissionReadyLegacyUnits` | 1 | units-and-lists | include | F2-07: offshore units only (`Kind == Offshore`): a legacy or migrated unit fills its silo before its fish eat |
| `src/LakskongenSim/Core/SimulationEngine.OffshoreOps.cs` | `SeaAllowsHarvestToday` | 1 | harvest-wellboat | hook | F2-07: an S2-04 harvest order's site through `OperationPossible` (a fjord site always; a unit by its sea) |
| `src/LakskongenSim/Core/SimulationEngine.OffshoreSea.cs` | `AdvanceOffshoreSea` | 2 | units-and-lists | include | F2-06: the open sea starts with the first offshore unit (`Kind == Offshore`) and each unit takes its plot's Hs; fjord sites are skipped |
| `src/LakskongenSim/Core/SimulationEngine.Pens.cs` | `DelousePens` | 2 | harvest-wellboat | include | By id and pen: a unit's zones are pens |
| `src/LakskongenSim/Core/SimulationEngine.Pens.cs` | `MigratePensAfterLoad` | 2 | units-and-lists | include | A v7 site gets its layout; a unit already has its zones (`PhysicalPens`), so it is skipped |
| `src/LakskongenSim/Core/SimulationEngine.Pens.cs` | `PenViewsOf` | 1 | units-and-lists | include | A unit's zones are listed as pens (`GetOffshoreUnits` `pens`) |
| `src/LakskongenSim/Core/SimulationEngine.Pens.cs` | `PreviewPenStocking` | 1 | capacity | exclude | Refuses a unit (`offshore_unit`) |
| `src/LakskongenSim/Core/SimulationEngine.Pens.cs` | `StockSmoltsInPen` | 2 | capacity | exclude | Refuses a unit (`offshore_unit`): `StockOffshoreFarm` stocks its first empty zone |
| `src/LakskongenSim/Core/SimulationEngine.PostSmolt.cs` | `PreviewTransferTankToSea` | 1 | capacity | exclude | F2-13: by id, a land tank's fish go to a fjord site; refuses a unit (`offshore_unit`), whose zones are stocked through its own verb |
| `src/LakskongenSim/Core/SimulationEngine.PostSmolt.cs` | `RunDueLandTransfers` | 1 | harvest-wellboat | include | F2-13: by id, the booked destination (a fjord site, checked at booking and again here); `OperationPossible` gates the handling as for any wellboat job |
| `src/LakskongenSim/Core/SimulationEngine.PostSmolt.cs` | `SeaDaysSavedBy` | 1 | reports | exclude | F2-13: by id, the planner's sea days saved at a fjord site; a unit reads 0 |
| `src/LakskongenSim/Core/SimulationEngine.PostSmolt.cs` | `TransferTankToSea` | 1 | capacity | exclude | F2-13: by id, the preview's destination (a fjord site; the preview refused a unit) |
| `src/LakskongenSim/Core/SimulationEngine.SeaTemperature.cs` | `HomeProductionArea` | 2 | fjord-zone | exclude | By zone: the fallback reads a sea site's zone, and a unit's plot zone is no fjord zone (skipped) |
| `src/LakskongenSim/Core/SimulationEngine.SiteKinds.cs` | `FindOffshoreUnit` | 1 | units-and-lists | include | Offshore units only (`Kind == Offshore`) |
| `src/LakskongenSim/Core/SimulationEngine.SiteKinds.cs` | `FjordSites` | 1 | units-and-lists | exclude | The fjord view (`Kind == Fjord`) the fjord consumers read |
| `src/LakskongenSim/Core/SimulationEngine.SiteKinds.cs` | `OffshoreUnits` | 2 | units-and-lists | include | The unit view (`Kind == Offshore`) |
| `src/LakskongenSim/Core/SimulationEngine.SiteKinds.cs` | `RemoteOpsAttentionUsed` | 2 | site-limits | hook | `CountsTowardSiteLimits` (F2-02's hook, wired here): the remote operations centre watches the fjord sites |
| `src/LakskongenSim/Core/SimulationEngine.SiteKinds.cs` | `SitesCountingTowardLimits` | 2 | site-limits | hook | `CountsTowardSiteLimits`: a unit is outside the site limits (spec: excluded from `MaxSitesPerZone`) |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `AdvanceSiteLiceModels` | 4 | lice | hook | Every sea site runs its lice model; a unit's external pressure is its slot's (`ExternalLicePressureFor` -> the plot's 0.12 at off_1); farm-to-farm spread couples a unit at the cross-zone 0.08, never the same-zone 0.4 |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ApplyStormAndTemperatureImpacts` | 2 | storm-wear | hook | `SkipsFjordStormWear`: no fjord storm deaths at a unit and no fjord repair bill for it (F2-07/F2-08 give it its own) |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ApplyStormHazards` | 4 | storm-wear | hook | `SkipsFjordStormWear`: no fjord net wear or storm escapes at a unit; the early-out reads its (untouched) net too |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `BuildEmpireState` | 3 | reports | include | Hot file: the empire snapshot counts sea sites (a unit is one) for its site, licence and staff approximations |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ClearMattilsynetOverMabOrders` | 1 | fjord-zone | exclude | By zone: the red-zone MTB order is per fjord zone; a unit's plot zone has no colour |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ExecuteFreshwaterBath` | 1 | harvest-wellboat | include | By id: a freshwater bath (wellboat job) treats a unit's fish |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ExecuteRovInspection` | 1 | harvest-wellboat | include | By id: an ROV inspects and cleans a unit's nets |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `GetCompanyLiceAverage` | 1 | lice | include | Fish-weighted over every sea site: the unit's fish and lice count |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `IssueMattilsynetOverMabOrders` | 1 | fjord-zone | exclude | By zone: as `ClearMattilsynetOverMabOrders` (and capacity v1 replaces it) |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ProcessCarbonLedger` | 2 | fjord-economics | exclude | The per-site diesel and power formula runs over `FjordSites()` (a unit books its own in `OffshoreTickSystem.AccountCarbon`); the company's feed and processing emissions go on the first sea site's tracker, whichever kind |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ProcessDueWellboatJobs` | 1 | harvest-wellboat | include | By id: due jobs at a unit run, gated by `OperationPossible` (F2-06) |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ProcessMattilsynetOverMabOrders` | 1 | fjord-zone | exclude | By state: only the red-zone order (per fjord zone) sets an over-MAB order, so a unit never has one |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ProcessSludgeValorization` | 1 | escapes-sustainability | include | A unit's sludge is valorized like any sea site's (with its biogas credit on its tracker) |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `RestoreFromLoad` | 1 | units-and-lists | include | Net-integrity and containment migration for every loaded sea site |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `RollBloomWeek` | 3 | fjord-zone | exclude | By zone: blooms spawn per fjord zone among its stocked sites; the gill recovery and countermeasure upkeep loop includes a unit (gills 100, no countermeasures: the verbs refuse it) |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `SiteLabelOf` | 1 | units-and-lists | include | Label lookup by id |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `UpdateSiteLiceCounts` | 1 | lice | include | The weekly census counts a unit against its area's limit and Mattilsynet orders it like any sea site |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ZoneLabel` | 1 | fjord-zone | exclude | By zone: a fjord zone's name is its first site's; a unit's plot zone is never asked for |
| `src/LakskongenSim/Core/UnitResolver.cs` | `ForEach` | 1 | units-and-lists | include | Every sea site's pens are units' populations (a unit's zones too) |
| `src/LakskongenSim/Core/UnitResolver.cs` | `Resolve` | 1 | units-and-lists | include | The precedence land tank -> pre-F2 offshore pen -> sea-site pen (fjord or unit) |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `FirstSeaPen` | 1 | events | include | ISA culls the first sea pen wherever it is (spec: offshore is excluded from the fjord cards except ISA) |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `FjordSites` | 1 | events | exclude | The fjord view the fjord site cards read: seals (`predator-attack`), escape fines, net damage, fjord-use disputes, the tourism view |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `RegisterCommunityEvents` | 2 | events | include | Company-level cards (wild-salmon protest, school visits) need any sea site: a unit is one |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `RegisterCompetitorEvents` | 1 | events | include | Company-level: an acquisition offer counts the company's sea sites |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `RegisterEquipmentEvents` | 1 | events | include | Company-level: the underwater-drone failure needs any sea site |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `RegisterInvestorEvents` | 1 | events | include | Company-level: the IPO window counts the company's sea sites |
| `src/LakskongenSim/Events/Storyteller.cs` | `BuildDefaultEvents` | 2 | events | include | Company-level: the equipment-failure card needs any sea site |
| `src/LakskongenSim/Farm/CohortTransferSystem.cs` | `ExecuteTransfer` | 2 | fjord-zone | exclude | By kind: refuses a plan from or to a unit (transfers run between the fjord sites of one zone; a fish moved into a unit would sit in none of its zones) |
| `src/LakskongenSim/Farm/CohortTransferSystem.cs` | `GetValidDestinations` | 2 | fjord-zone | exclude | By zone: same-zone destinations only, and a unit's plot zone holds only itself |
| `src/LakskongenSim/Farm/CohortTransferSystem.cs` | `LabelOf` | 1 | units-and-lists | include | Label lookup by id |
| `src/LakskongenSim/Farm/FjordPolicies.cs` | `FjordSite` | 1 | units-and-lists | exclude | F2-05: the fjord policies (net care, delousing on the count) act on fjord localities only (`Kind == Fjord`); an offshore unit's periodic tasks are the sea lane's policies (F2-07) |
| `src/LakskongenSim/Integration/DashboardDataProvider.cs` | `CalculateLaborCosts` | 1 | reports | include | Dashboard estimate per sea site: a unit is crewed (12 in rotation) |
| `src/LakskongenSim/Integration/DashboardDataProvider.cs` | `CalculateLicenseCapex` | 1 | reports | exclude | By kind: fjord licences only; a unit's capex is `capex_offshore` |
| `src/LakskongenSim/Integration/DashboardDataProvider.cs` | `CalculateLicenseFees` | 1 | reports | include | Dashboard estimate per sea site (a unit will hold development licences, F2-09) |
| `src/LakskongenSim/Integration/DashboardDataProvider.cs` | `CalculateMaintenanceCosts` | 1 | reports | include | Dashboard estimate per sea site: a unit's structure is maintained |
| `src/LakskongenSim/Integration/DashboardDataProvider.cs` | `CalculatePenCapex` | 1 | reports | exclude | By kind: fjord pen frames only; a unit's capex is `capex_offshore` |
| `src/LakskongenSim/Integration/DashboardDataProvider.cs` | `GetAdvisorRecommendations` | 1 | reports | include | "Underutilized capacity" below two sea sites: a unit is a site |
| `src/LakskongenSim/Sustainability/SustainabilityManager.cs` | `CalculateEscapeScore` | 2 | escapes-sustainability | include | Escapes at a unit count in the escape score (spec: offshore is included in escapes and `SustainabilityManager`) |
| `src/LakskongenSim/Sustainability/SustainabilityManager.cs` | `CalculateWelfareScore` | 2 | escapes-sustainability | include | Density per sea site: a unit is one body of water (its zones' volume, `PenVolume` x 1) |

## Fjord-view consumers

The consumers that read the list before F2-03 and now read the fjord view instead (`SimulationEngine.FjordSites()`, `NorwegianEvents.FjordSites(gs)`: the sea sites with `Kind == Fjord`, in list order). Each excludes an offshore unit by its kind; the two definitions are the views themselves. With no offshore unit in a game the view is the list, so a fjord-only game (every shipped scenario, every golden trace) runs exactly as before.

| File | Member | Uses | Category | Decision | How |
|---|---|---|---|---|---|
| `src/LakskongenSim/Core/SimulationEngine.BuildDiagnosis.cs` | `FarmedAreas` | 1 | fjord-zone | exclude | F2-31: the build dialog's diagnosis reads the production areas of the company's fjord sites (their traffic light, licence MTB and warmest month); an offshore unit is free of those limits, which is the diagnosis's point |
| `src/LakskongenSim/Core/SimulationEngine.Expansion.cs` | `BuildGateKey` | 1 | site-limits | exclude | U2-01 (moved here from `CanBuildSeaSite`, wave-3 integration): the free first locality (the development right) is for a company with no fjord site; an offshore unit does not use it up |
| `src/LakskongenSim/Core/SimulationEngine.Expansion.cs` | `PlanSiteBuild` | 1 | site-limits | exclude | U2-01 (wave-3 integration): the build plan's "new zone" follows the same development right as `BuildGateKey` |
| `src/LakskongenSim/Core/SimulationEngine.LandBoard.cs` | `PostSmoltPlannerOf` | 1 | capacity | exclude | F2-13: the post-smolt planner's destinations are the fjord sites with their MTB headroom; a unit takes no land fish (`offshore_unit`) |
| `src/LakskongenSim/Core/SimulationEngine.Licence.cs` | `BuildSeaSite` | 1 | units-and-lists | exclude | A fjord build without a name is numbered among the fjord sites (`site_N`); offshore units are numbered apart (`offshore_N`) |
| `src/LakskongenSim/Core/SimulationEngine.OffshoreLicences.cs` | `AreaLiveMortalityShare` | 1 | reports | exclude | F2-09: the conversion checklist compares a unit's mortality with its production area's live average over the company's fjord sites there (spec §2B "compared live with the game's own PO mortality"); a unit is never its own yardstick |
| `src/LakskongenSim/Core/SimulationEngine.OffshoreLicences.cs` | `LiceComplianceShare` | 1 | lice | exclude | F2-09: a development-licence dossier's lice compliance is the share of the company's stocked fjord sites under the limit at their last count |
| `src/LakskongenSim/Core/SimulationEngine.PostSmolt.cs` | `TransferSmoltBatchToSea` | 1 | capacity | exclude | F2-13: the post-smolt panel's "move to sea" picks the fjord site with the most MTB headroom that takes the fish; a unit takes no land fish |
| `src/LakskongenSim/Core/SimulationEngine.SiteKinds.cs` | `FjordSites` | 1 | units-and-lists | include | The engine's fjord view (definition) |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ProcessCarbonLedger` | 1 | fjord-economics | exclude | Hot-file one-liner: the fjord per-site diesel and power formula; a unit books its own fuel, power and transport (`OffshoreTickSystem.AccountCarbon`): one fuel entry a day, not two |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ProcessDailyFinance` | 1 | fjord-economics | exclude | Hot-file one-liner: fjord site crews' wages; a unit's crew of 12 is in its own opex (`OffshoreUnit.TickDay`) |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `ProcessNightShiftPolicy` | 1 | fjord-economics | exclude | Hot-file one-liner: the remote operations centre's night rolls (bloom detection, oxygen response) are for the fjord sites it watches; a unit is crewed on board |
| `src/LakskongenSim/Core/SimulationEngine.cs` | `TotalSiteScale` | 1 | fjord-economics | exclude | Hot-file one-liner: fjord site upkeep scales with the fjord sites' size; a unit's upkeep is its own |
| `src/LakskongenSim/Core/TutorialSystem.cs` | `FjordLocalities` | 1 | reports | exclude | U2-02: the first season teaches the fjord locality; its feeding plan, lice order, restock, second site and the new site's fish are read on the fjord sites (an offshore unit is no step of Første sesong; the old track's `BuildSteps`/`ParseCondition` rows went with the old track) |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `FjordSites` | 1 | events | include | The storyteller's fjord view (definition) |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `RegisterCommunityEvents` | 2 | events | exclude | Fjord-use dispute (two fjord sites) and the tourism view (a fjord site) |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `RegisterEquipmentEvents` | 1 | events | exclude | Net damage at a fjord pen |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `RegisterRegulatoryEvents` | 1 | events | exclude | The escape fine (a fjord net failure) |
| `src/LakskongenSim/Events/NorwegianEvents.cs` | `RegisterWeatherEvents` | 2 | events | exclude | Seals at the nets (`predator-attack`): the condition and the damage share read stocked fjord sites only |

## GDScript consumers

No GDScript file reads the C# list. The one GDScript caller of `GetSeaSiteDetails` is `SimActions.get_all_pens` (row above), which calls the fjord verb `GetSeaSiteDetails()`; `tests/test_bridge_contract.py::test_sea_site_details_list_the_fjord_unless_asked` fails if a call passes an argument, if `get_all_pens` stops calling the fjord verb or if a fjord consumer (FishPenManager, FjordVistaBridge, FeedBargeVisualManager, SimActions) reads every sea site (`GetAllSeaSiteDetails`), and `test_bridge_consumers_call_real_methods_with_valid_arity` fails on any GDScript call that leaves a C# default unfilled (Godot never fills one). Its readers therefore see the fjord sites exactly as before F2-03, in the same order:

| Reader of `get_all_pens` | Reads | Effect of an offshore unit |
|---|---|---|
| `environment/FjordVistaBridge.gd` -> `FishPenManager.sync_pens_from_sim` | the 3D pen sites, by index into the channel slots | none: the pen slots never shift (UiFlowTests `_test_offshore_unit_leaves_fjord_pens_in_place`) |
| `ui/Dashboard.gd`, `ui/SiteOverlayLegend.gd`, `ui/BriefingCard.gd` | site rows and overlays | none |
| `ui/OffshorePanel.gd` | the containment ladder and the transfer lists | none: containment and transfers are fjord-only (rows above) |
| `ui/TelemetryPanel.gd`, `ui/WeatherDiseasePanel.gd`, `ui/DataExportPanel.gd`, `ui/GameEventActions.gd`, `tools/ScreenshotHarness.gd` | per-site readouts | none |

The offshore units reach GDScript through the sea lane's slice (`GameBridge.Offshore.cs`: `GetOffshoreUnits`, `GetOffshorePlots`; `SimBridge.get_offshore_units()`, `get_offshore_plots()`), the land sites through the land lane's (`GameBridge.LandSites.cs`: `GetLandSites`, `GetLandPlots`). The pre-F2 panel verbs keep their keys: `GetOffshoreFarms` lists the units (`OffshoreUnitViews`), `GetRASFacilities` the grow-out land sites, `GetPostSmoltFacilities` the post-smolt land sites.

## The land-site list

Post-smolt sites join `GameState.RASFacilities` (`Kind = PostSmolt`). The readers of that list that must tell the kinds apart do so: `GameBridge.GetRASFacilities` (grow-out only), `GetPostSmoltFacilities` (post-smolt only), `DaySummary.CountHarvestReadyFish` (grow-out only), `DashboardDataProvider.CalculateRasCapex` (grow-out only), `AchievementSystem` "SuperSmoltStocked" (a post-smolt site), `LandSiteTickSystem` (grow-out tanks and post-smolt modules in FarmOpex's pre-F2 order). Every other reader (fish counts, land carbon, the holding seam, harvests by id) treats both kinds as land tanks.

## Hot files

F2-03 edits `Core/SimulationEngine.cs` in four lines only (the fjord-view rows marked "hot-file one-liner"), each `State.SeaSites` -> `FjordSites()`, with no other change: the F2-02 hooks are frozen and none of them covers crews, upkeep, per-site carbon or the night shift. `Core/InDayTickSystems.cs` is untouched (its two site counts are decided "include" above).
