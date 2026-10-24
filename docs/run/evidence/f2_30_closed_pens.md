# F2-30 evidence: the closed-pen kit

Farm-types spec item F2-30 (`docs/FARM_TYPES_SPEC.md` §5.3 and §7.3), branch `art/f2-30`.
Four closed-containment assets (`closed_donut`, `closed_globe`, `semi_closed_ring`, and `pump_module`), 11 LOD GLBs, on the shared `LK_FacilityKit` material and trim atlas, built headless and byte-identical from code, and wired into the fjord site presenter (`PenCage.gd`, `FarmSite.gd`) so each containment level renders its own asset across near/mid/far LOD bands.

## What was built

| Piece | Where |
|---|---|
| Build script (CLI, atlas, AO bake, join, export, sidecars, budgets) | `blender/build_closed_pens.py` |
| Asset geometry, one function per asset and LOD | `blender/closed_pen_designs.py` |
| Two atlas strips appended: `donut_shell` `#8E979C` (row 28) and `skirt_tarp` `#C9CCC6` (row 29) | `blender/build_land_base.py` `STRIPS`; `godot/assets/models/facility_kit/textures/facility_atlas.png` (rows 0–27 keep their indices) |
| Review renders (EEVEE, AgX, trim atlas and baked AO) | `blender/render_closed_pen_preview.py`; frames in `docs/run/evidence/f2_30/` |
| 11 LOD GLBs + JSON sidecars + Godot `.import` files | `godot/assets/models/closed_pen/` |
| Manifest: 4 entries (`type: "closed_pen"`) and verified hashes | `blender/asset_manifest.json`; `godot/assets/asset_manifest.json` |
| Presenter wiring (per-pen LOD0 + site-wide LOD1/LOD2 MultiMeshes, preserving `PumpModule` collar/deck offset) | `godot/scripts/environment/PenCage.gd`, `godot/scripts/environment/FarmSite.gd` |
| Tests | `tests/test_closed_pen_assets.py` (31), `godot/tests/SiteFlowChecks.gd` (`_closed_containment_levels`) |

## Acceptance

| # | Spec §7.3 F2-30 criterion | Measured / verified |
|---|---|---|
| 1 | Donut volume 22,000 m³ ± 5%, Globe 3,500 m³ ± 5%, skirt 13 m | Independent GLB shell-triangle mesh volume (`_shell_volume` in `tests/test_closed_pen_assets.py`, isolated by atlas V row): `closed_donut` LOD0 `21,859.712 m³`, LOD1 `21,393.779 m³`, LOD2 `21,103.459 m³` (within `20,900–23,100 m³`; nominal torus `22,016.521 m³`, major radius `17.0 m`, tube radius `8.1 m`, outer Ø`50.2 m`, freeboard `3.0 m`). `closed_globe` LOD0 `3,465.193 m³`, LOD1 `3,423.619 m³`, LOD2 `3,391.501 m³` (within `3,325–3,675 m³`; nominal sphere `3,479.142 m³`, Ø`18.8 m`, freeboard `5.0 m`). `semi_closed_ring` steel collar outer Ø`40.0 m` (`+1.4 m` top), tarpaulin skirt depth `13.0 m` (`z = 0` to `-13.0 m`), net cone to `-28.054 m`. `pump_module` housing `6.0 × 3.0 × 3.0 m` on a mounting plate (`z = 1.28 m` underside) with intake strainer at `-25.0 m` (spec band `15–40 m`). |
| 2 | Triangle budgets met | `closed_donut`: `8,784 / 1,884 / 396` ≤ `12,000 / 3,000 / 400`. `closed_globe`: `4,874 / 1,122 / 192` ≤ `6,000 / 1,500 / 200`. `semi_closed_ring`: `6,312 / 1,288 / 192` ≤ `15,000 / 4,000 / 600`. `pump_module`: `1,080 / 232` ≤ `1,200 / 300` (no LOD2 per spec; requesting `LOD2` or exceeding budget exits non-zero). |
| 3 | Each containment level renders its own asset (UI-flow test on a QA site per level) | `PenCage` and `FarmSite` map all six `ContainmentLevel` values (`OpenNet`, `Skirt`, `SemiClosed`, `ClosedFloat`, `Submersible`, `OffshoreClosed`): `OpenNet` → open `PenCage`; `Skirt` → open `PenCage` + `LiceSkirt`; `SemiClosed` → `semi_closed_ring` + `pump_module`; `ClosedFloat` → `closed_donut` + `pump_module`; `Submersible` and `OffshoreClosed` → `closed_globe` + `pump_module`. Verified in `SiteFlowChecks._closed_containment_levels` across `NEAR` (LOD0), `MID` (LOD1 MultiMesh, including per-pen `PumpModule` collar/deck offset), and `FAR` (LOD2 MultiMesh, where `PumpModule` is omitted per spec), plus round-trip restoration to `OpenNet`, raising `MIN_CHECKS` in `tools/run_ui_flow_tests.sh`. |

## Swatches and trim atlas

Rows 28 (`donut_shell`, `#8E979C`, roughness `0.55`) and 29 (`skirt_tarp`, `#C9CCC6`, roughness `0.70`) are appended at the end of `build_land_base.STRIPS` so rows 0–27 remain unchanged. A rebuild of either `build_closed_pens.py --textures` or `build_land_base.py --textures` produces byte-identical `facility_atlas.png` and GLB/JSON files (`test_build_reproduces_committed_files_and_atlas`).

| Asset | Part | Strip | Spec hex | Sampled atlas hex |
|---|---|---|---|---|
| `closed_donut` | `Shell` | `donut_shell` (row 28) | `#8E979C` | `#8F989D` |
| `closed_globe` | `Shell` | `lox` (row 19) | `#E9ECEA` | `#EAEDEB` |
| `semi_closed_ring` | `Collar` | `steel` (row 4) | `#8A9096` | `#8B9197` |
| `semi_closed_ring` | `Skirt` | `skirt_tarp` (row 29) | `#C9CCC6` | `#CACDC7` |
| `pump_module` | `Housing` | `steel` (row 4) | `#8A9096` | `#8B9197` |
| `pump_module` | `Pumps` | `orange` (row 8) | `#D9622B` | `#DA622B` |

## Review frames

- [Kit overview (Donut, Globe, and Semi-closed ring with pump modules)](f2_30/kit_overview_LOD0.png)
- [Closed donut (`closed_donut` + `pump_module` on crown service gap)](f2_30/donut_close_LOD0.png)
- [Closed globe (`closed_globe` + `pump_module` on waist service gap)](f2_30/globe_close_LOD0.png)
- [Semi-closed ring above water (`semi_closed_ring` + `pump_module`)](f2_30/ring_close_LOD0.png)
- [Semi-closed ring underwater (`13 m` skirt, ballast chain, net cone, intake)](f2_30/ring_under_LOD0.png)
- [Pump module and deepwater intake hose (`6 × 3 × 3 m` skid, strainer at `-25 m`)](f2_30/pump_under_LOD0.png)

## Reproduce

```bash
/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
    --python blender/build_closed_pens.py -- --textures
BLENDER_BIN=/Applications/Blender.app/Contents/MacOS/Blender \
    python3.12 .claude/skills/ecc/blender-asset-pipeline/blender_asset_pipeline.py --verify-only
env GODOT_MCP_TOKEN_PATH=$(mktemp -d)/t godot-mono --headless --audio-driver Dummy --path godot --import
python3.12 -m pytest tests/test_closed_pen_assets.py -q
bash tools/validate_scenes.sh
dotnet build godot/LakskongenGame.csproj
bash tools/run_ui_flow_tests.sh
```
