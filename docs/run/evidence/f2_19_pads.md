# F2-19 evidence: land pads and offshore slots in the terrain

Farm-types spec item F2-19 (§4, §7.2), branch `wave4b/gfx-farm-1`. The pads
were carved on the paused `wave4/gfx-farm-1` (`1656cd0`, `bb707ce`) and are
checked here on the wave-4 base (`e98944d`) after the merge, with the windowed
frames that stage could not render. Frames are 960 px downscales in
`f2_19/`; the numbers are `f2_20_facilities_metrics.json` (the capture's
`godot/screenshots/game/f2_20_facilities_metrics.json`, shots `45_pads_*`).

## What is built

| Piece | Where |
|---|---|
| Pads carved into the heightfield (flat +3.6 m top, cut face into higher ground, a rock shelf to the sea, a quay face on the channel side) | `src/LakskongenSim/Environment/TerrainGenerator.Plots.cs` (`CarvePads`, `QuaySides`), called from `TerrainGenerator.HeightAt` |
| Pad and slot geometry for the plot catalogue (`ISitePlotGeometry`: `LandPadSlots`, `OffshoreSlots`, computed `IntakeReachM`) | `TerrainGenerator.Plots.cs`, `SitePlotGeometry.cs` |
| Nothing grows or stands on a pad or its rim | `Environment/FjordGroundCover.cs`, `PadClearings()` in `Vegetation.cs`, `ShoreProps.cs` |
| Baked regions name the same pads and slots | `schemas/terrain/meta.schema.json` (`land_pads[]`, `offshore_slots[]`; an authored intake reach is refused) |
| Heath or blasted granite per pad | `godot/shaders/terrain_shader.gdshader` (`pad_rects`, `pad_state`, `pad_blast`; F2-20 adds the graded yard: `yard_contrast` 0.8, `yard_flatten` 0.7), `FjordTerrain.set_pad_state(id, state, blast)` |
| Tests | `TerrainPadTests.cs` (7), `TerrainMetaSchemaTests.cs` (5), `TerrainGeneratorTests.GoldenHashSeed42`, `UiFlowTests.TERRAIN_GOLDEN_HASH`, `ShorePropTests`, `FarFieldTests`, `SitePlotCatalogTests` |

## Acceptance

| # | Criterion | Measured | Result |
|---|---|---|---|
| 1 | Seeds 1-10: pad height 3.6 ± 0.3 m over ≥ 95 % of each pad mask | `TerrainPadTests.EveryPad_IsFlatAtThePlanLevel_OnSeedsOneToTen`: 100 % of every mask's vertices at 3.6 m on all ten seeds; mutation (no carve) fails it and the golden hash | Pass |
| 2a | The headless hash equals the windowed hash | `a7b58a2c48dd4a8f` in `TerrainGeneratorTests` (.NET) and `UiFlowTests` (headless Godot); the windowed capture logs `[FjordTerrain] 512x512 fjord, 7.8 m grid, hash a7b58a2c48dd4a8f, 15432 trees` | Pass |
| 2b | The terrain golden and the water bake re-promoted with an art and a bake check | Golden `5a84dfe1202f180d` → `a7b58a2c48dd4a8f` (`1656cd0`). Bake check below; art check: the frames below | Pass |
| 3 | Pad and slot coordinates equal `site_plots.json` (procedural) or `meta.json` (baked) within 1 m; intake reach computed | `PadsAndSlots_StandAtTheCataloguePositions_WithAComputedIntake` (every plot within 1 m, no authored reach); `TheProceduralSlots_WrittenAsAMetaJson_Validate` (seeds 42, 1, 7) | Pass |
| 4 | Zero vegetation instances inside pad masks | `NothingGrowsOrStands_OnAPadOrItsRim` (trees, props, wood and meadow; mutation: no clearings fails it and `ShorePropTests`) | Pass |
| 5 | Every offshore slot outside the bake with exposure 1 | `EveryOffshoreSlot_LiesOutsideTheBake_WithExposureOne` | Pass |
| 6 | An unbuilt pad renders as heath: its mean colour differs from a built pad by ΔE ≥ 10 | ΔE76 heath vs blasted, mean CIE Lab over a 24 x 16 grid on each pad's top: pad_a 22.37 (land framing 520 m, −40°), pad_b 19.35 (land framing), pad_delta 22.00 (380 m, −40°), home view pad_a 13.21, pad_b 10.56 | Pass (pad_b at the home view by 0.56) |

## Bake check (seed 42, the carved fjord against the same fjord uncarved)

A scratch probe built `WaterBake` on both heightfields (the uncarved one from
the generator's sampler without `CarvePads`; nothing of it ships):

| Channel | Within 300 m of a pad's rim | Beyond |
|---|---|---|
| Depth | changed on the 10,786 carved vertices (max 114 m: the delta's and the headlands' fill) | identical |
| Seabed slope | changed on 11,462 texels | identical |
| Shore distance | changed on 24,522 texels | 1,345 texels (max 70 m: the new shore moves the distance field's far values) |
| Exposure | changed on 26,681 texels | 27,713 texels, at most 0.084 |

- Sheltered fetch 1,080 → 1,015 m (−6 %), inside `WaterBakeTests`' band (800-4,500 m).
- The 12 channel pen slots stay sheltered (exposure under 0.25): the two
  nearest the mouth drop 0.179 → 0.147 and 0.145 → 0.116 (the pads shade the
  mouth), the rest change by at most 0.011.
- The golden traces do not read the terrain: 27 of 27 games identical to the
  base (`f2_20_golden_diff.md`).

## Frames (shots `45_pads_*`, noon, clear, summer)

| Shot | What it shows |
|---|---|
| `f2_19/45_pads_land_heath.jpg` / `_granite.jpg` | pad_a (Storholmen) at the land framing: unbuilt, heath and strandflat slabs on the flat +3.6 m top, its rim a rock shelf to the sea and a cut face inland, no tree or naust on it; built, the whole pad blasted granite (ΔE 22.4) |
| `f2_19/45_pads_b_heath.jpg` / `_granite.jpg` | pad_b (Skjærvika): the cut face into the hillside, the rock fill to the fairway (ΔE 19.4) |
| `f2_19/45_pads_delta_heath.jpg` / `_granite.jpg` | pad_delta (Elveøyra) raised from the delta's +2.5 m and filled out over the delta front (ΔE 22.0) |
| `f2_19/45_pads_home_heath.jpg` / `_granite.jpg` | the shipped home view: unbuilt, the pads read as low heath-covered strandflat, not as flat rectangles; built, two grey yards at the mouth (ΔE 13.2 / 10.6) |

Judged against the owner brief (the land references L1/L2 exist only as
descriptions, `docs/art/OWNER_REFERENCE_BRIEF.md`): L2's "blasted granite
pad" reads as a grey crushed-rock yard on a rock shelf; in the F2-20 frames
the graded yard keeps the campus on one even surface. An unbuilt pad stays
part of the heath landscape at the home view (the spec's "before" state).
