# Player placement: pens, land farms and slaughterhouses (architecture proposal)

Status: proposal, docs only (GRAPHICS_HANDOVER §2 Task B). No `src/` change. Written against
`origin/main` `466120c`: save v11 (`SaveLoadManager.CurrentSaveVersion`), calibration registry
1.1.4. No open PR (#16, #17, #20, #22, #23) bumps either.

## 1. Where placement stands today

| Unit | How it is placed now | What the player cannot choose |
|---|---|---|
| Fjord site | `BuildSeaSiteAt(zone, candidate)`: one of 12 ranked candidates per area (`GeoCandidates`); `SeaSite.CandidateId` gives the position | The pens: layout, heading, count. `PenLayout.Standard` builds 8 pens, and `SiteLayout.gd` grids them in 90 m `MooringGrid` cells (presentation only) |
| Land farm | `BuildLandSite(plotId, LandTemplate, contract)` on a `SitePlot` (`LandPad`), with `ConstructionProject` phases and `PermitCase`s; `LandCampusLayout.gd` places the lots | Where on the pad a lot stands |
| Offshore unit | `BuildOffshoreUnit(plotId, type, contract, equity)` on an `OffshoreSlot` | Heading |
| Slaughterhouse | Bridge-only `GameBridge.BuyProcessingPlant(tier)`: sets `GameState.OwnsProcessingPlant` and the string `ProcessingTier`. No position, no build time, no permit, one a game | Everything |
| Preview | `FarmGhost` + `FarmGhostLayout` draw the unit on its plot from the build dialog (presentation only) | Moving or turning the ghost |

## 2. Principles

1. **Positions are engine state.** What the player places is saved in `GameState` as integers
   (frame cells, decimetres, whole degrees) in the unit's own frame. With no floats, a position
   hashes the same on every platform (`MathFingerprint`).
2. **Placement is a command.** Every placement is a `CommandResult` verb with a pure `Preview…`
   twin that returns the same reason keys. The ghost asks the preview and keeps no rules of its own.
3. **Placement draws no RNG.** Random outcomes come later from the existing seams: the
   `facility_construction` and `facility_permits` streams, two uniforms per round.
4. **Additive saves.** A save from before placement loads with today's standard layouts and plays
   exactly as before. Golden traces stay identical until a slice changes behaviour on purpose.

## 3. Pens: coordinates, verbs, mooring frame, ghost

**Model.** A new `Farm/MooringFrame.cs`, saved as `SeaSite.Frame`:

```
MooringFrame { int HeadingDeg;           // 0-359, 0 = along the channel
               int CellDm;               // 900 = the 90 m MooringGrid cell
               int Cols, Rows;           // frame cells across x along
               List<FrameCell> Cells; }  // FrameCell { int Col, Row; string PenId ("" = spare) }
```

A pen's position is its frame cell, not free coordinates. Real sites moor their pens in a rope
grid, so cells are realistic, and they cannot drift numerically. `Pen` gets `Col` and `Row`.

**Verbs** (`Core/SimulationEngine.PenPlacement.cs`):

| Verb | Does | Reason keys |
|---|---|---|
| `PreviewMooringFrame` / `SetMooringFrame(siteId, cols, rows, headingDeg)` | Sizes and turns the frame, only while the site holds no fish | `site_stocked`, `frame_too_large` (frame area over the locality footprint), `frame_out_of_zone` |
| `PreviewPenPlacement` / `AddPen(siteId, col, row)` | Puts a standard net pen in a spare cell: capex `pens.penCapexNok` and a one-phase `ConstructionProject` (`pen_install`) | `cell_taken`, `cell_outside_frame`, `pen_limit`, `insufficient_funds` |
| `MovePen(siteId, penId, col, row)` | Moves an **empty** pen to a spare cell | `pen_occupied`, as `AddPen` |
| `RemovePen(siteId, penId)` | Frees an empty pen's cell | `pen_occupied` |

Stocking keeps its rules (`pen_density`, `pen_fish_limit`, `mtb_*`). `NumberOfPens` stays the
crew and upkeep scale (`SiteSizeScale`); tying it to the pen count is owner gate G3.

**Ghost.** `FarmGhost` gets a pen mode. `FarmSite` and `SiteLayout` read the cells from
`GetSeaSiteDetails()["frame"]` instead of computing `grid_dims(pen_count)`; `SiteLayout.pen_cells`
stays the fallback for a site without a frame. While placing, the ghost draws the frame plus one
translucent pen at the hovered cell, coloured by the preview's verdict, with the reason key's text.

## 4. Slaughterhouse as a placed land unit

**Decision: reuse all four seams; build no parallel system.**

| Seam | Reuse? | How |
|---|---|---|
| `SitePlot` | **Yes** | A slaughterhouse needs a quay. Add `PlotKind.ShorePlot = 2` (shore plots in `site_plots.json`, each with its quay frontage in metres); a `LandPad` with a quay also qualifies. A plot is its own water area (`ZoneId` = plot id), so per-zone systems never see the plant |
| `LandTemplate` | **Yes** | Append `SlaughterSmall = 4`, `SlaughterMedium = 5`, `SlaughterLarge = 6` and `LandSiteKind.Processing`. They hold no tanks and no fish: `LandHooks` reports no holding, so `UnitResolver` never maps a population there |
| `FarmGhost` | **Yes** | `FarmGhostLayout._land` gets `slaughter_*` cases, with footprints from the slaughterhouse kit (PR #23: hall, reception, loading bays, intake on the quay) |
| `ConstructionProject` | **Yes** | Phases `site_works` → `building` → `quay_works` (sea-works `ClosedMonths`) → `commissioning`. Add `FacilityAssetKind.Processing` (life `facilities.processing.lifeYears`) |
| `PermitCase` | **Yes** | Existing kinds only: `Zoning` (unless pre-zoned), `Discharge`, `SeaWorks` (the quay) |

**Harvest route.** A commissioned plant becomes its own capacity pool. `HarvestRoute.For` asks a
new hook, `OwnSlaughterhouseOf(holding)`, for the company's nearest commissioned plant: pool
`"plant:" + siteId`, capacity the tier's t/day, slaughter cost the plant's variable opex. A company
without a plant keeps the contracted `SlaughterhousePool`. The VAP premium then applies to fish
slaughtered at the company's own plant, not to all harvest revenue (slice S3).

## 5. `BuyProcessingPlant` → engine command; migrating `OwnsProcessingPlant` saves

**Verbs** (`Core/SimulationEngine.Processing.cs`, the engine-command pattern):

- `PreviewSlaughterhouse(plotId, ProcessingTier, ContractForm)`: pure. Returns budget, phases,
  missing permits, capacity, first day and verdict.
- `BuildSlaughterhouse(plotId, ProcessingTier, ContractForm, name = "")`. Reason keys: `not_found`,
  `plot_taken`, `no_quay`, `permits_missing`, `insufficient_funds`, `not_unlocked`.
- `UpgradeSlaughterhouse(siteId, ProcessingTier)`: a new `ConstructionProject` on the same plot;
  the old tier keeps running until it completes.

`GameBridge.BuyProcessingPlant` first forwards to `BuildSlaughterhouse` on the first free shore
plot. It is retired once `ProcessingFacilityPanel.gd` moves into the build dialog's Land family.

**State.** The processing land sites become the source of truth. `GameState.OwnsProcessingPlant`
becomes a computed read (any commissioned processing site), so the existing engine readers
(`SimulationEngine.cs`, `Harvest*.cs`, `AchievementSystem`, carbon) keep working unchanged. The
stored bool and string `ProcessingTier` are read **only** by the migrator.

**Migration v11 → v12** (`SaveLoadManager` case 11, then an idempotent
`SimulationEngine.MigrateProcessingPlant` after `MigrateFacilities`):

| Old save | Becomes |
|---|---|
| `OwnsProcessingPlant = false` | Nothing |
| `true`, tier parses as `ProcessingTier` | One commissioned processing site of that tier on an **overflow shore plot** (as `MigrateFacilities` does for pre-F2 RAS): no project, no permit, no new capex, no asset-register entry (bought capex never depreciated before) |
| `true`, tier `""` (scenario `ressursrente`'s starting building) | Tier `Small` |

`ScenarioSystem.ApplyStartingBuilding("processing_plant")` calls the same helper, so a scenario
start and a migrated save give the same state.

## 6. Saves, golden traces, calibration, bridge, determinism

**Save version.** v12: processing sites and shore plots (S2). v13: mooring frames (S4). Each is
additive with a migrator case. The frozen v8 fixture still loads through the chain
(`PreF2SaveFixtureTests`).

**Golden traces (27 = 9 scenarios × seeds 1/7/13 × 730 days).** `GoldenPlayer` builds nothing.

| Slice | Expected `tools/record_golden_traces.sh --base` |
|---|---|
| S1 verbs, no caller | 27 of 27 identical |
| S2 processing site + migration | 27 game-identical; save drift on the 3 `ressursrente` traces only (the plant becomes a site). Game-identical proves the migration |
| S3 own-plant route | `ressursrente` × 3 change on purpose: `--update` and a RUN_LOG §7 row |
| S4 mooring frames | 27 game-identical; save drift on all 27 (the frame is saved; cells equal today's grid) |

**Calibration keys** (`[Range]` plus one `[Source]` or `[GameBalance]` each, `CalibrationGuardTests`),
in a new `godot/data/calibration/facilities_processing.json` via `facilitiesIncludes`:
`processing.{small,medium,large}.{capacityTonnesHogPerDay, capexNok, fixedOpexNokPerDay,
variableOpexNokPerTonne, quayFrontageM}`, `processing.lifeYears`,
`processing.phases.<key>.{plannedDays, capexShare}`, `processing.permits.<kind>.{minDays, maxDays,
refusalProbability, feeNok}`, `processing.vapPremium` (today 0.12 in `FeedRecipe.VapMult`); and
`pens.penCapexNok`, `pens.maxPensPerSite`, `pens.installDays`. Registry 1.1.4 → 1.2.0. S1 moves
`ProcessingManager`'s literals into these keys at today's values, so behaviour is unchanged.

**Bridge contract** (`GameBridge.*.cs` → `SimProxies.gd` → `SimBridge.gd`, checked by
`tests/test_bridge_contract.py`; GDScript passes every argument):

| C# | GDScript |
|---|---|
| New `GameBridge.Processing.cs`: `PreviewSlaughterhouse(plotId, tier, contract)`, `BuildSlaughterhouse(plotId, tier, contract, name)`, `UpgradeSlaughterhouse(siteId, tier)`, `GetSlaughterhouses()`, `GetShorePlots()` | `preview_slaughterhouse`, `build_slaughterhouse`, `upgrade_slaughterhouse`, `get_slaughterhouses`, `get_shore_plots` |
| `GameBridge.Pens.cs`: `PreviewMooringFrame`, `SetMooringFrame`, `PreviewPenPlacement`, `AddPen`, `MovePen`, `RemovePen` | `preview_mooring_frame`, `set_mooring_frame`, `preview_pen_placement`, `add_pen`, `move_pen`, `remove_pen` |

`OwnsProcessingPlant` and `ProcessingTier` stay as read-through bridge properties until no UI uses them.

**Determinism.** Placement opens no RNG stream; only the existing construction and permit streams
draw. `ProcessingPlant.AddProductionLine`'s allowlisted `Guid.NewGuid()` is not on this path.
`EngineTwinRunDeterminismTests` and `ExactSaveHashTests` gain a placed plant and a moved pen.

## 7. Ordered slices (★ = owner gate)

| # | Slice | Scope | Merge gate |
|---|---|---|---|
| S0 | Slaughterhouse kit | art, PR #23 | done |
| S1 | Engine verbs + calibration section, no caller | `src/`, `facilities_processing.json` | 27/27 traces identical; a test per reason key; `CalibrationGuardTests` |
| ★ G1 | Shore plots per region; one plant or many; 90 m frame cells | — | owner sign-off |
| S2 | Processing land site, v12 migration, `BuyProcessingPlant` forwards | `src/`, `SaveLoadManager`, bridge | traces as §6; v11 save round-trip; `PlayerScriptProbe`: building a plant changes the outcome |
| ★ G2 | Realistic capex and capacity: today 1 / 5 / 15 M NOK, likely far below a real plant (unsourced; cite references in the G2 PR). Balance, no clamp | calibration | owner sign-off |
| S3 | Own-plant harvest route (pool, cost, VAP on own fish) | `HarvestRoute`, hooks | `--update` + RUN_LOG row; the no-dominant-strategy sweep includes "build a plant early" |
| S4 | Mooring frame + pen verbs, v13 | `src/`, bridge | traces as §6; `MortalityLedgerEngineTests`; density per cell |
| S5 | Placement UI: build dialog Land → slaughterhouse, pen editor on the Pens tab, `FarmGhost` modes, `LandCampusBuilder` draws the kit | `godot/` | `run_ui_flow_tests.sh` with new checks (MIN_CHECKS raised); screenshot diff; ask before windowed runs |
| ★ G3 | Does the pen count drive crew and upkeep (`NumberOfPens`)? | balance | owner sign-off |
