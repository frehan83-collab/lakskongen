# F2-13 / F2-15 replaced and re-set tests (wave4/sim-land-2)

The rule (RUN_LOG §2, spec §3.10): tests are add-only; a test is replaced only by a stronger one where a resolved
decision changes the behaviour it pinned. Spec §3.1 decision 0 names F2-13 as the item that fixes the smolt-tier
exploit; F2-02 installed both hooks with "today's" defaults for F2-13 to fill.

## Replaced (FarmTypeHooksTests)

| Old test (F2-02) | Old assertions | New test (F2-13) | Assertions kept | Added |
|---|---|---|---|---|
| `QualityMortalityMultiplier_IsOne_WhateverTheSmoltTier_AndLeavesTheAnchorDeathsUnchanged` | multiplier 1.0 for every tier; every fixture population's terms × quality = 1.0; the anchor run with the multiplier 1.0 kills exactly the pre-move run's fish (cohorts, ledger total, BaselineAnchor deaths) | `QualityMortalityMultiplier_IsOneForStandardSmolts_AndTheTiersCarryoverIsLiveAtSea` | 1.0 exactly for standard smolts; every fixture population (all standard, asserted) × terms = 1.0; the same anchor comparison, unchanged | each tier's multiplier bit for bit (0.82, 0.67); the fish-weighted mean over mixed cohorts; 1.0 in a land tank whatever the tier; a 0.67 run kills 0.6-0.7 of the anchor's fish |
| `SmoltTierCostPerFish_TimesCount_IsThePreMoveStockingCost_ForEveryStrainTierAndSource` | count × price × strain multiplier, bit for bit, for every strain, tier and source and 8 counts | `SmoltTierCostPerFish_IsThePreMoveCost_ForStandardAndOwnSmolts_AndChargesTheTierPremiumOnBoughtOnes` | bit for bit for every standard smolt and every smolt from the own source, all strains and counts | the bought premiums (× 1.6, × 2.5) for every strain and count; the registry's values; the sea verb `StockSmolts` pays exactly the hook's price for each tier |

## Re-set (setup changes, every assertion unchanged)

| Test | Change | Why |
|---|---|---|
| `LandSiteEngineTests.Acceptance6_TheNameplate_CapsTheRollingYearsPlannedProduction` | the pilot lot runs at competence 1.0 | at 0.4 F2-15's utilisation cap (55 %) refuses first; the nameplate check is unchanged and `LandCycleTests.Acceptance4_*` pins the cap |
| `LandSiteEngineTests.TheEngineLot_HoldsTheStandaloneEnergyAndOxygenAnchors` | competence 1.0; the harness holds the fallow days; its plans take `PlanGrowthMultiplier`; the standalone runs on the engine's pad lines and year of shallow water with the engine's plan | the projection is the plan; like with like the engine is −1 % of the projection (it was −4 % on the dossier's pad lines and climatology); bands (6.0-7.5, 0.45-0.65) and ±6 % unchanged |
| `LandSiteEngineTests` stocking check | the plan takes `PlanGrowthMultiplier` × the genome | the engine's check does |
| `LandFacilitiesSectionTests.EveryLandLeaf_IsWalkedByTheGuard_WithItsRangeAndOneProvenance` | exact count 228 → 300 with the new sections named and nine new leaves' provenance asserted | new leaves; the count stays exact |
| `FarmTypeMoveTests.PreF2FacilitiesSave_Runs365Days_ExactlyAsBeforeTheMove` | a new level: `F2_15Recording` is the whole save; `AsF2_12Wrote` strips the new land-site fields (each at its default, asserted) and the hash is `F2_12Recording` exactly | save drift only: no pre-F2 facility day changes |
