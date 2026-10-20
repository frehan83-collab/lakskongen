# F2-03 replaced tests (PR checklist)

Roadmap F2-03 acceptance 7 (docs/FARM_TYPES_SPEC.md §3.10 "Replacements"): every test F2-03 replaced asserts at least what it replaced. Old assertions on the left (as on `235ccf2`, `wave3/sim-core-1`), the replacement's on the right (`wave3/sim-core-2`). RUN_LOG §7 (2026-10-16, F2-03) records the rulings.

## `FacilityProductionTests` offshore section -> `Farm/OffshoreUnitTests.Offshore_StocksAndHarvests_AndCountsForTheRentTax`

| Old (`FacilityProductionTests.Offshore_StocksAndHarvests_AndCountsForTheRentTax`) | New |
|---|---|
| `BuildOffshoreFarm("Havfarm", SemiSubmersible)` succeeds | same verb succeeds, and `Build_CreatesAnOffshoreSeaSite_WithItsClassZones_OnTheFirstRatedSlot`: one offshore sea site with the build's id, in `State.SeaSites`, `Kind == Offshore`, on `off_1` (zone `off_1`, no fjord zone), owner of the plot, OF1 class (design Hs 5.5 m, 7-day rota, 12 crew, MTB 6,240 t), 3 zones of 83,333 m³ (250,000 m³), capex paid, not operational until built (`not_operational`) |
| `StockOffshoreFarm(id, 50,000)` succeeds; one pen; the pen is in `State.Populations` | same, and the pen is zone 1 (`PenOf`), carries the unit's site and zone ids, and resolves as an `Offshore` holding |
| `Advance(200)` | same, and the fish grew more than 5x, the site lice census counted the unit, its lice model holds lice |
| `HarvestOffshoreFarm` succeeds, the farm's pens are empty, the rent base grew | same, and zone 1 is free again and the harvest report's route is the offshore wellboat |
| (none) | `Build_PlacesEachClassOnlyOnASlotItIsRatedFor_AndRefusesBeforeAnyMutation_WhenNoneIsFree`, `Stocking_RefusesBeforeAnyMutation`, `TheFjordStockingVerbs_RefuseAnOffshoreUnit`, the unit's day (rota, carbon, storm lockdown) |

## `FacilityProductionTests` post-smolt section -> `Farm/PostSmoltSiteTests.PostSmolt_BatchIsVaccinated_GrowsAndGoesToSeaAsALargeSmolt`

The spec names `PostSmoltBridgeTests` (F2-13: tiers live, salinity); F2-03 moves the section to the land site it creates and F2-13 adds its assertions there.

| Old | New |
|---|---|
| `BuildPostSmoltFacility` succeeds | same, and `Build_CreatesAPostSmoltLandSite_OfFourModules_OnThePostSmoltPad`: a post-smolt land site (`Kind == PostSmolt`) in `RASFacilities` on `pad_delta` (its zone), 4 modules, 8 tanks of 1,600 m³, 1,920 t a year, the legacy facility's staff, water, construction time and capex; a second one on `pad_a`, a third refused (`no_free_plot`) with no cash moved |
| `StartPostSmoltBatch(id, 50,000)` succeeds; the batch's start day is today | same, and the batch is a population in the site's tanks and in `State.Populations` with the site's ids, a land-tank holding, 50,000 fish at 80 g, smolt age |
| `TransferPostSmoltToSea` refuses an unready batch (`batch_not_ready`) | same |
| vaccination succeeds, then `already_vaccinated` | same, and the population's vaccination day is today |
| the batch reaches transfer weight within 400 days | same, within 20-200 days, in the site's own controlled water (setpoint +/- 0.15 °C) |
| the transfer empties the facility; the sea site's one cohort has the batch's count and weight (6 dp) and is a Large smolt | same, and the population leaves the game's list, the cohort keeps its age, genome and vaccination day |

## `SaveLoadMatrixTests` facility builds (:129-138) -> the same test through the new verbs

| Old | New |
|---|---|
| day 45 `BuildOffshoreFarm`, day 60 `BuildPostSmoltFacility`, every save/load point of the career equal to the uninterrupted run | same verbs (they build an offshore sea site and a post-smolt land site), and day 112 stocks the offshore unit's first zone and a post-smolt batch, day 118 vaccinates the batch, so every later save/load point carries fish in both new kinds |

## `PersistenceGapTests` RAS part (:50-98) -> kept, new fields added

| Old | New |
|---|---|
| a refused offshore build moves no cash and adds no farm | same, and no offshore unit and no plot state (`State.SitePlots` empty) |
| `BuiltFacilities_SurviveSaveLoad`: one RAS, one offshore farm, one post-smolt facility after a load; the RAS construction day | one grow-out land site, one offshore unit, one post-smolt land site (2 land sites in all), no legacy facilities; each one's construction day; their plots (`pad_a`, `off_1`, `pad_delta`) and owners; the unit's kind, class, design Hs and 3 zones; the catalogue's attributes rejoined (`off_1` Hs100 5.0 m) |

## Tests F2-03 moved to the new kinds (stronger, same intent)

| Test | Old | New |
|---|---|---|
| `HoldingSeamTests` offshore case | an offshore farm moved into the fjord zone takes the zone's oxygen + its boost; holding id and farm | the offshore unit is its own holding (site, zone pen `p01`, no zone, no farm, no land facility), its water is its area's open sea (PO9, saturation O2) + the unit's 1.5 mg/L boost in both views, its wave height the unit's |
| `PreF2SaveFixtureTests` | the fixture loads with one RAS, one offshore farm, one post-smolt facility; fish and kg kept; RAS fish grow | the load migrates them to the F2 kinds by the facilities' ids; the legacy lists are empty; every fish and kilo (batches included) is in a population; the post-smolt populations grow too |
| `FarmTypeMoveTests.PreF2FacilitiesSave_Runs365Days_ExactlyAsBeforeTheMove` | the pre-move pins (`66412d201cf6141c`, `e4cfbd3cf6eb0d09`, `6e7519ba98447711`) | the F2-03 pins (`fc222260db75d617`, `e06067233951965a`, `cc5ee2b0971b9fb1`), the pre-move pins asserted unequal (the migration changes the facilities' days on purpose), and each unit by id on its plot with its fish in its zones and its carbon booked |
| `HarvestLedgerReconciliationTests`, `MortalityLedgerEngineTests` | ledger reconciliation with an offshore farm and a RAS facility | the same with the offshore unit and the land site (fish lost = fish booked, harvest reports = removals) |
| `MortalityLedgerEngineTests` post-smolt case | a post-smolt batch (no cohorts) loses fish to the facility's top-down survival rate; every death is booked as `BaselineAnchor` under the facility; the company total holds them | the batch is a population with a cohort, so its deaths are booked by cause: every day the cohort's loss equals that day's booked deaths and removals, the 30 days' deaths equal the sum over every cause and the company total's growth, the anchor is among the causes, and the batch carries its site's id. The old "every death is the anchor" no longer holds by design (post-smolts now run the biology every population runs, RUN_LOG §7) and is replaced by the exact per-day, per-cause reconciliation |
| `FarmTypeHooksTests` | every hook neutral for every site | fjord sites neutral; the offshore unit's hooks (site limits, area pool, storm wear, plot lice pressure, zone lice share) as the audit decides |
