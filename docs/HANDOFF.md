# Handoff: total context and next steps (updated 2026-10-20)

Read this file first, and only this file, until you know your task. It is a summary
of everything else. Then open only the sections the reading map (§8) names for your
task. The large docs are long: ROADMAP 957 lines, FARM_TYPES_SPEC 1605, RUN_LOG 710,
BALANCE 3085, FJORD_RENDERER 782, AGENTS 410. Do not read them whole.

## 0. TL;DR

- **Lakskongen** is a Norwegian salmon-farming management sim. The simulation is deterministic C# .NET 10 (`src/LakskongenSim`). The frontend is Godot 4.7.2 Mono (`godot/`).
- **Build on `main`.** PR #5 (review remediation + waves 1-3) was merged into `main` on 2026-10-20 (`1a944e2`). New work goes on a branch from `origin/main` and back through a PR, so the GitHub CI (`ci`, `verify`) runs on it.
- **Roadmap waves 1-3 are done**: graphics, simulation core, core loop UI, and farm-type foundations. **Wave 4's headless part is integrated** on `wave4/int` (land and offshore farm types in the engine, the build dialog, the tutorial; PR to `main`); the graphics stages wait for the owner's OK to open windows (§6).
- **Owner rules:**
  - wait for the owner's go and never autostart;
  - align with GitHub first, because other models may push;
  - use tokens efficiently.

## 1. Product and owner direction

- **Goal:** a top-tier product, with an elite simulation, elite graphics and the best game experience. The owner's words: "keep the graphics on a cool level with the user in mind"; "best ever user and gaming experience based on research on other simulation games that have success". Benchmarks: Planet Zoo, Anno 117, Farming Simulator 25, Frostpunk 2, Two Point.
- **Art direction:** `docs/art/OWNER_REFERENCE_BRIEF.md`, about 60 lines.
  - Cool Nordic palette; saturation only on equipment and signals.
  - A site is a grid of pens with white feed hoses, yellow buoys, bird nets and a feed barge.
  - The water has three moods: sunny deep blue, calm winter slate mirror, overcast grey.
  - Land-based farms are tank campuses on granite pads with orange pipe bridges.
  - Offshore farms are safety-yellow semi-submersibles, OF1-class: a 12-gon, Ø110 m.
- **Farm types are a full feature**, specified in `docs/FARM_TYPES_SPEC.md`. There are three families: Sjø (open and closed pens), Land (post-smolt module, hybrid flow-through, RAS) and Hav (OF1-class, Havfarm-class).
- **Scope** follows `docs/ROADMAP_TOP_TIER.md`, Phases 0-5, which ends with a Steam demo and Next Fest.

## 2. Architecture map

**Simulation** (`src/LakskongenSim`, deterministic):
- `Core/SimulationEngine*.cs`: a partial class per area (Capacity, Harvest, Mortality, SeaTemperature, Growth, Holdings, Pens, FarmTypeHooks, Containment, Driftsplan, FacilityPermits, …).
  - `SimulationEngine.cs` and `InDayTickSystems.cs` are hot and frozen for facility code; use the F2-02 hooks.
  - Tick systems are `ITickSystem`.
  - `HoldingIndex` resolves each population's holding once per step. The holding is a sea pen, a land tank or an offshore structure, through `HoldingWaterOf` and `CohortEnvironment.From`.
- `SimRng` uses seeded streams only; `DeterminismBanTests` (Roslyn) forbids the wall clock and unseeded RNG.
- **Calibration:** `Calibration/CalibrationRegistry.cs` plus `godot/data/calibration.json` (version 1.1.4), with farm-type include files under `godot/data/calibration/facilities_*.json`. Every constant records its value, unit, source and year. Facility leaves need `[Range]` plus `[Source]` or `[GameBalance]`.
- **Domains:**
  - `Biology/`: `TgcGrowthLaw` and `MortalityHazards`;
  - `Environment/`: production-area water, weather, `OffshoreMetocean`;
  - `Farm/`: `SeaSite`, `Pen`, `Land/*`, `Offshore/*`, `SitePlot`;
  - `Harvest/`: `HarvestRoute` per holding kind;
  - `Finance/`: `LicenceHolding`, `FacilityAssetRegister`, `PermitCase`;
  - `Market/`.
- **Save:** version 11. Migrators chain sequentially, and the frozen fixture `save_v8_facilities.json` must keep loading.

**Bridge** (Godot side, one partial per feature):
- `godot/scripts/autoload/GameBridge.*.cs`: Core, Farm, Pens, Capacity, Harvest, HarvestPlanner, FeedPlans, Expansion, EndScreen, Events, Clock, Driftsplan, FarmTypes, LandSites, Offshore, Mortality, Sky, ….
- GDScript facade: `godot/scripts/bridge/SimProxies.gd` and `SimActions.gd` → `godot/scripts/SimBridge.gd`.
- `tests/test_bridge_contract.py` guards drift between the two sides.

**Godot frontend:**
- **World:** `godot/scripts/environment/`.
  - `EnvironmentDirector.gd` is the only writer of environment, fog, sky, water uniforms and the eight moods.
  - `RenderQuality.gd` holds the Low/Medium/High/Ultra tiers; High meets p95 16.7 ms on the M1 Pro.
  - `FjordTerrain.gd`, with the heightfield from C# `TerrainGenerator` and a far field to 16 km.
  - `WaterGrid.gd` and `PlanarReflection.gd`.
  - `WaveField.gd`, a CPU mirror of the shader waves that pens and boats ride.
  - `FjordCamera.gd`.
  - `FishPenManager.gd`, `FarmSite.gd` and `MooringGrid.gd` draw multi-pen sites, one pen per engine pen.
  - `FishSchool.gd` runs GPU salmon.
  - `SiteLights.gd` and `VesselLights.gd`.
- **Vessels:** `godot/scripts/visual/`.
- **UI:** `godot/scripts/ui/`.
  - `GameUI.gd` is the shell; there is one panel per tab.
  - The theme is Kyst & Stål, generated from `design/tokens.json` via `tools/sync_tokens.py` into `godot/theme/`. Fonts are Inter and Barlow Condensed. All text is Norwegian via `godot/translations/translations.csv`.
- **Assets:** built headless by Blender scripts in `blender/` (`build_pen.py`, `build_site_kit.py`, `build_vessels.py`, `build_land_base.py`, `build_offshore.py`, …), listed in `blender/asset_manifest.json`, and exported to `godot/assets/models/`.

**Tests and tools:**
- `tools/ci.sh` has 18 steps; `CI_REQUIRE_ALL=1` makes every step required.
- `godot/tests/UiFlowTests.gd` drives the real game headless. MIN_CHECKS in `tools/run_ui_flow_tests.sh` must equal the exact check count (1637 on `wave4/int`), and the suite takes about 5-6 minutes.
- Golden traces (F2-01): `tools/record_golden_traces.sh` records 27 games × 730 days.
- Screenshot harness: `bash tools/capture_screenshots.sh`; shots are listed in `godot/qa/shots.json`.
- Perf harness: `tools/perf_smoke.sh` (P0-04).
- GitHub workflows run only on PRs to `main`. They are `ci` (Quality gate), `verify` (dotnet, python, golden-traces) and `visual-qa`; the last needs a self-hosted Mac runner.

## 3. What is built (waves 1-3)

- **Graphics:**
  - a U-trough fjord with a granite and heath PBR material, season-driven snow, 15k birch and spruce, naust boathouses and a quay;
  - the far field;
  - opaque JONSWAP water with planar reflection, shore foam, wakes and storm spray;
  - eight weather moods, an aurora and the Nordic night;
  - quality tiers and a perf harness;
  - a camera rig that keeps the horizon in frame.
- **Art:** the hero pen and site kit (hoses, buoys, bird nets, lice skirt), feed barges, wellboats, a workboat, a RIB, the salmon, the land campus kit and the OF1-class unit with its offshore fleet. Land and offshore are not yet placed in the world; that is wave 4.
- **Simulation:**
  - S2-01: capacity, licences, MTB per production area, real pens (6-12 per site, at most 200k fish, at most 25 kg/m³);
  - S2-02: production-area sea temperatures and one calendar;
  - S2-03: TGC growth;
  - S2-04: the harvest pipeline (wellboat slots, HOG 0.84, size-class prices);
  - S2-05: mortality booked by cause;
  - the honest economy: medals instead of cash, per-scenario bankruptcy floors, a liquidity ladder with bank call and rescue loan, overdraft interest, resource-rent tax;
  - farm types F2-01 to F2-05 (golden traces, hooks, site kinds and plots, facility finance and catalogue, Driftsplan standing orders);
  - F2-12 land physics and F2-06 offshore metocean, both standalone; wave 4 wires them in.
- **UX:**
  - a live 3D main menu, the theme, a pause menu and a scenario picker;
  - the typed event bus: critical events pause the game, warnings go to the inbox;
  - pacing: a briefing card, the calendar in the HUD, auto-slow and auto-pause;
  - U2-04 feeding plan and U2-05 harvest planner and report;
  - U2-01 expansion (licence auctions, build dialog);
  - U2-03 end screens and campaign progress.

## 4. Key decisions (details: `docs/RUN_LOG.md` §7, grep the item id)

- **Owner decisions D1-D27** are in ROADMAP §3; the key ones:
  - D13/D14: the art direction, the Kyst & Stål theme and fonts;
  - D16: an honest economy, with medals and no reward cash;
  - D17: no real company names in the game;
  - D2/D4: realism over legacy test bands, with stronger tests.
- **Merge-captain rulings:**
  - the OF1 hull is a 12-gon;
  - the sheltered sea band is Hs 0.05-0.15 m at 3 m/s;
  - the PO4 vs PO13 growth gap is judged on the pooled climatology;
  - the shot-18 mirror is judged at the ripple scale. Its gain bar is still open, and wave 4 re-stages the shot as mirror-calm.
- **Process:**
  - parallel worktree tracks, one merge captain;
  - semantic merges, never picking a side wholesale;
  - golden traces are re-recorded once per integration, with a diff report.

## 5. Open issues (fix first)

1. ~~GitHub Quality gate (U2-07 next-event timing check on Linux)~~ — fixed in `4f93d68`; PR #5 was green on `ci` and `verify` before it merged.
2. **Visual QA** needs a self-hosted Mac runner, which is the owner's call.
3. **Harness content findings**, handled by `wave4b/gfx-polish-1`, merged into `wave4b/int` (`480ae51`, 2026-10-21; RUN_LOG §7/§8, `docs/run/evidence/wave4b_int_gfx_polish_1.md`):
   - the moods pass (closest pair 10 pHash bits, Midnattssol water ΔE 8.39);
   - shot 18 judges the mirror by its structure over a mirror-off twin (the captain's ruling; gain 0.336, bar 0.2);
   - the clouds have no slices or stair steps, and the main menu keeps the tier's half pass;
   - the snow banding on the walls at 1-2 km is gone;
   - still open: 08a land contrast is 0.107 against a bar of 0.12 (the polish adds 0.006; F2-19's flat pads take 0.005).

## 6. Wave 4: state (2026-10-20) and next steps

Goal: land-based and offshore farms that are buildable, visible and operable end to end, plus the tutorial and polish.

**The headless part is integrated on `wave4/int`** (branched from `origin/main` `e297f16`, PR to `main` "Wave 4 (headless part)"; RUN_LOG §7 has one row per merge). Merged with `--no-ff`, the gates run after each merge:

| Merge | Stage branch (local head) | Items | Gates after the merge (headless) |
|---|---|---|---|
| 1 | `wave4/ui-farm-1` (`08770bc`; carries `wave4/sim-land-1` `46a4a3f` and `wave4/sim-sea-1` `93c79c5`) | F2-31 family-first build dialog; F2-12 land wiring, F2-14 land permits and phased construction; F2-06 open-sea wiring, F2-07 offshore unit build, operations and logistics | dotnet 1945/0, UI flow 1549/0 |
| 2 | `wave4/sim-land-2` (`83be749`) | F2-13 post-smolt bridge, F2-15 grow-out biology, competence, harvest planning | dotnet 1973/0, UI flow 1567/0 |
| 3 | `wave4/sim-sea-2` (`c523acb`) | F2-08 offshore incidents, handling, lice coupling; F2-09 development licences | the build dialog could not build a Hav unit any more (F2-09's award rule): fixed in `605040e`, then dotnet 2011/0, UI flow 1593/0 |
| 4 | `wave4/ui-core-1` (`d3afa9a`) | U2-02 learn-by-doing first season | dotnet 2037/0 (3 skipped), pytest 719/0, scenes 0 failures, UI flow 1637/0 |

The stage branches' finished heads above exist only in this checkout's local branches and in `wave4/int`'s history: `origin/wave4/sim-land-2`, `sim-sea-1` and `ui-core-1` still point at older WIP commits, and `wave4/ui-farm-1` and `wave4/sim-sea-2` were never pushed. Build on `main` once the PR merges, not on the stage branches.

**Integration facts:** calibration registry 1.1.4 (one above every branch's), save version 11 (U2-02's migrator), MIN_CHECKS 1637. BALANCE sections: §1v F2-12/F2-14, §1w F2-06/F2-07, §1x F2-31, §1y F2-13/F2-15, §1z F2-08/F2-09, §1aa U2-02. `FarmTypeMoveTests` pins the facilities save on the combined engine and strips each step's fields back to the previous merge's pin and to sim-sea-2's own pin. Golden traces re-recorded once: 27 of 27 games identical to `origin/main` on every day, save drift only, every node explained in `docs/run/evidence/wave4_int_golden_diff.md`.

**Found and fixed at integration:** F2-09 builds an offshore unit only on a development-licence award, which F2-31's dialog predated. The Hav confirm now files the application while a round is open (as Land files its permits), then contracts the unit once the award comes (RUN_LOG §7, "F2-31 meets F2-09").

**Still deferred (they need windows, so they wait for the owner's OK):**
- `wave4/gfx-farm-1` (F2-19 carved shore pads, F2-20 facility presenter): finished on `wave4b/gfx-farm-1` and merged into `wave4b/int` (`5d7f18a`, 2026-10-21) with its review handled there (pad_b's layout, Esc and the hidden card, the build stall, the land card's waiting line; `docs/run/evidence/wave4b_int_gfx_farm_1.md`, `f2_19_pads.md`, `f2_20_presenter.md`; remainder in RUN_LOG §8).
- `wave4/gfx-polish-1` (the §5.3 polish list, idle-machine perf, chip clipping, whitecaps inside pens): finished on `wave4b/gfx-polish-1` and merged into `wave4b/int` (`480ae51`, 2026-10-21) with its review handled there (the merged grid lost F2-19's pads: one carved ground for the grid and `HeightAt`; 18's gain is the mirror's own structure; the menu keeps the tier's clouds, -2.3 ms; the ledge, sun and `--only` checks; `docs/run/evidence/wave4b_int_gfx_polish_1.md`; remainder in RUN_LOG §8: 08a, per-pixel cloud contours at Ultra, P0-04 at 1440p).
- `wave4/gfx-farm-2` (F2-21 camera targets, G bookmarks, the open sea's swell and Hs chip; F2-23 first sight, teaser, night lights, showcase shots): finished on `wave4b/gfx-farm-2` and merged into `wave4b/int` (`940f0f9`, 2026-10-21) with its review handled there (cinematics wait for an open card or menu and stand still under one; a save loaded in the game is no completion; the swell's loops skipped without a swell; the weather, a held drag and the saved picture come back right; the drawn uniforms and the hand-back checked; `docs/run/evidence/wave4b_int_gfx_farm_2.md`, `f2_21_camera_sea.md`, `f2_23_first_sight.md`; remainder in RUN_LOG §8).
- Deferred visual checks of the merged stages (no frame was rendered or looked at on any of them):
  - F2-31: the build dialog in each state (family cards, catalogue bars, locked gates, Land preview with permits and runway, Hav preview with licence line and consortium, the new Hav licence step and its filed-case result), the ghost in 3D on `pad_a` and `off_1`, and the runway chart, at 1280x720 at 100 % and 125 % against the owner refs (RUN_LOG §8, "Remainder (F2-31)").
  - U2-02: the ten `43_first_season_*` shots in `godot/qa/shots.json`, played by `qa/scenes/FirstSeasonCapture.tscn` (input season seed 7, skip seed 23).
  - F2-07/F2-08/F2-14: the land campus and the OF1 unit are placed and drawn phase by phase since gfx-farm-1 (`46_facilities_*`, `32_land_construction_*`); the build dialog's ghost does not yet use the presenter's layout.
- The full `CI_REQUIRE_ALL=1 tools/ci.sh` was not run locally (its shader check and perf steps open windows); GitHub CI runs the full suite on the PR.

**Owner and economy decisions waiting:**
1. **F2-07 acceptance 7, the offshore opex basis.** The bar is opex/kg HOG 1.15-1.45 × an open pen's in the same PO. Measured medians are 1.885 (ex feed, spill and smolts, the pre-registered basis), 1.557 (ex feed and spill, smolts kept, acceptance 7's own words) and 1.145 (all in). No basis lands inside the band. Choose the basis, or the band (BALANCE §1w, RUN_LOG §8 "Finding (F2-07): acceptance 7").
2. **F2-08 acceptance 5, the warning band's lower bound.** "Handle when required" with standard vessels at `off_1` should warn in 10-30 % of winters. The measured share is 0 of 200 winters (20 seeds x 10 winters): the sea alone never keeps a requirement overdue for 21 days. The upper bounds hold. Choose between keeping the band and adding friction later (wellboat availability and lead time, a fleet shared with the fjord), or lowering the band to what the sea gives. The lower-band probe test is skipped until then (BALANCE §1z, RUN_LOG §8 "Finding (F2-08): acceptance 5").

**Next, in order:**
1. Merge the wave-4 PR once GitHub CI is green (the owner merges).
2. With the owner's OK to open windows (given 2026-10-20): gfx-farm-1, gfx-polish-1 and gfx-farm-2 are merged on `wave4b/int`; run the deferred visual checks above.
3. **Deferred:** G3-04 underwater view, U2-08 settings that apply, Au2-01 audio floor.
4. **Then:**
   - the rest of the farm types: F2-10/F2-11, F2-16/17, F2-22/24, F2-27-30, F2-32-35 (F2-32/F2-33 are the land and offshore boards on the bridge reads F2-13/F2-15 and F2-08/F2-09 already expose, and the development-round panel);
   - Phase 3: U3-01 world-first IA (runs alone in the UI track), U3-02 icons replacing emoji, U3-03 panel kit, G3-02 presenter, G3-03 lenses, S3-01 market, S3-02 traffic light;
   - Phases 4-5.

## 7. Rules and how to verify

- **Wait for the owner's go.** Before changing anything: `git fetch --all --prune`, compare branches with origin, check open PRs, and build on what others pushed.
- **Tests are add-only.** Never weaken a check. Replace one only with a stronger one, logged as a row in RUN_LOG §7. Never loosen a band; fix the model, with sources.
- **Golden traces must stay byte-identical** unless the behaviour change is deliberate. In that case run `tools/record_golden_traces.sh --update` plus `--base <point> --report docs/run/evidence/<item>_golden_diff.md`.
- **Godot:**
  - always run `env GODOT_MCP_TOKEN_PATH=$(mktemp -d)/t godot-mono …`;
  - run `godot-mono --headless --path godot --import` after adding a `class_name`;
  - macOS has no `timeout`, so use `perl -e 'alarm shift; exec @ARGV' 900 <cmd>`.
- **Never run** `python -m visual_qa.capture` or `baseline_update`; they overwrite the baseline.
- **The owner's Godot editor is open on the main checkout.** Work and test in a separate git worktree.
- **Commits:**
  - format: `run: <wave> <item> — <outcome>` plus evidence in the body;
  - no attribution or co-author lines;
  - no emoji in new UI text;
  - Norwegian text via translation keys.
- **Gates per branch:**
  - `dotnet build LakskongenSim.sln && dotnet build godot/LakskongenGame.csproj`;
  - `dotnet test LakskongenSim.sln`;
  - `python3 -m pytest tests/ -q`, with packages from `requirements-dev.txt`;
  - `bash tools/validate_scenes.sh`;
  - `bash tools/run_ui_flow_tests.sh`;
  - full: `CI_REQUIRE_ALL=1 /bin/bash tools/ci.sh`, about 25 min, at integration only.

## 8. Reading map: open only what your task needs

| Task | Read (grep the heading) |
|---|---|
| Any roadmap item | ROADMAP: grep `**<ID>:` (e.g. `**U2-02:`) for files, approach and acceptance. §10 is the conflict map. |
| Farm-type item F2-xx | FARM_TYPES_SPEC §7 (`### 7.1 sim` … `### 7.4 ui`): the item's one-line row holds files and acceptance. Then only the section that row cites: §3.2 data, §3.3 per-tick, §3.4 commands, §4 world placement, §5 assets, §6 UX. |
| Sim numbers and sources | BALANCE: grep the item id (sections §1a…§1u). `docs/research/FARM_TYPES_RESEARCH.md` for land and offshore sources. |
| Why something is the way it is | RUN_LOG §7: grep the item id or keyword. §8 for open findings. |
| Rendering, water, terrain, perf | FJORD_RENDERER: grep the system name. `docs/run/evidence/p0_04_perf.md` for the perf baseline. |
| Art and the look | `docs/art/OWNER_REFERENCE_BRIEF.md` and `docs/art/WAVE1_ART_REVIEW.md` (short). |
| Codebase conventions | AGENTS.md: grep the topic (e.g. "Real pens", "Driftsplan", "golden"). |
