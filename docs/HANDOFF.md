# Handoff: total context and next steps (updated 2026-10-17)

Read this file first, and only this file, until you know your task. It is a summary
of everything else. Then open only the sections the reading map (§8) names for your
task. The large docs are long: ROADMAP 957 lines, FARM_TYPES_SPEC 1595, RUN_LOG 630,
BALANCE 2619, FJORD_RENDERER 782, AGENTS 368. Do not read them whole.

## 0. TL;DR

- **Lakskongen** is a Norwegian salmon-farming management sim. The simulation is deterministic C# .NET 10 (`src/LakskongenSim`). The frontend is Godot 4.7.2 Mono (`godot/`).
- **Build on `review-remediation`**, which equals `integration/wave2`, head `04735a8`; it is draft PR #5 against `main`. `main` is older, so do not start from it.
- **Roadmap waves 1-3 are done**: graphics, simulation core, core loop UI, and farm-type foundations. **Wave 4 is paused**: work in progress sits on `wave4/*` branches (§6).
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
- **Calibration:** `Calibration/CalibrationRegistry.cs` plus `godot/data/calibration.json` (version 1.1.1), with farm-type include files under `godot/data/calibration/facilities_*.json`. Every constant records its value, unit, source and year. Facility leaves need `[Range]` plus `[Source]` or `[GameBalance]`.
- **Domains:**
  - `Biology/`: `TgcGrowthLaw` and `MortalityHazards`;
  - `Environment/`: production-area water, weather, `OffshoreMetocean`;
  - `Farm/`: `SeaSite`, `Pen`, `Land/*`, `Offshore/*`, `SitePlot`;
  - `Harvest/`: `HarvestRoute` per holding kind;
  - `Finance/`: `LicenceHolding`, `FacilityAssetRegister`, `PermitCase`;
  - `Market/`.
- **Save:** version 10. Migrators chain sequentially, and the frozen fixture `save_v8_facilities.json` must keep loading.

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
- `godot/tests/UiFlowTests.gd` drives the real game headless. MIN_CHECKS in `tools/run_ui_flow_tests.sh` must equal the exact check count (1425 now), and the suite takes about 5 minutes.
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

1. **GitHub Quality gate:** one UI flow check fails on the Linux runner. It is U2-07's "next event: N during a run ends it and the clock keeps its 3×", a timing check on a slow runner. Grep that text in `godot/tests/UiFlowTests.gd`. Make it deterministic without weakening it.
2. **Visual QA** needs a self-hosted Mac runner, which is the owner's call.
3. **Harness content findings**, being handled in `wave4/gfx-polish-1`; RUN_LOG §8 has the details:
   - 08a land contrast is 0.106 against a bar of 0.12;
   - the Midnattssol and Høst moods are 8 pHash bits apart against a minimum of 9;
   - the shot-18 mirror gain bar;
   - stair-stepped cloud edges in the half-resolution sky pass;
   - snow banding on the walls at 1-2 km.

## 6. Wave 4 (paused 2026-10-17): branch state and next steps

Goal: land-based and offshore farms that are buildable, visible and operable end to end, plus the tutorial and polish. All branches below are built from `4f927a1` and pushed.

| Branch | Items (spec rows) | State |
|---|---|---|
| `wave4/sim-land-1` | F2-12 engine wiring, F2-14 land permits and phased construction | **Done** (`46a4a3f`, gates green, goldens 27/27 identical) |
| `wave4/sim-land-2` | F2-13 post-smolt bridge, F2-15 grow-out, competence, harvest planning | WIP, unverified |
| `wave4/sim-sea-1` | F2-06 wiring, F2-07 offshore unit build, operations, logistics | WIP, unverified |
| `wave4/gfx-farm-1` | F2-19 carved shore pads, F2-20 facility presenter | WIP, unverified |
| `wave4/gfx-polish-1` | Polish list in §5.3, plus idle-machine perf, chip clipping, whitecaps inside pens | WIP, unverified |
| `wave4/ui-core-1` | U2-02 learn-by-doing tutorial | WIP, unverified |

**Next, in order:**
1. Finish the WIP stages. Review each WIP commit, finish the item to its acceptance list, and run the gates (§7).
2. **sim-sea-2:** F2-08 offshore incidents and F2-09 development licences.
3. **ui-farm-1:** F2-31 family-first build dialog, after merging sim-land-1 and sim-sea-1.
4. **gfx-farm-2:** F2-21 camera bookmarks and offshore swell, then the F2-23 first-sight sequence and showcase shots.
5. **Integrate:** merge into `integration/wave2` in a separate worktree, re-record the goldens once with a diff report, run the full CI, push to `review-remediation`.
6. **Deferred:** G3-04 underwater view, U2-08 settings that apply, Au2-01 audio floor.
7. **Then:**
   - the rest of the farm types: F2-08…F2-11, F2-16/17, F2-22/24, F2-27-30, F2-32-35;
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
