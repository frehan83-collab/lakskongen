# Lakskongen: roadmap to a top-tier release

_Product-lead synthesis of six audits (visual, UX, 3D tech, simulation, render research, art research). Written 2026-10-14 against branch `review-remediation` at `8c4b90b`. This was a read-only audit: no repo file was changed. Severity tags: [C] critical, [H] high, [M] medium, [L] low._

---

## 1. Where the product stands

The engine is release-grade in its foundations. It is deterministic, round-trips save/load exactly, and has an honest ledger: 914 .NET tests, 224 UI-flow checks and 17/17 CI steps pass (`docs/CODE_REVIEW_RECHECK.md`). The product on top of it is not yet a game, and it is far from top tier:

- **Every 3D frame is broken.** The terrain shows as needle shards and the camera stares straight down, so no sky is visible (`08_fjord_day.png`, which I opened and confirmed). Water reads as tinted glass. Pens are a brown torus with a green debug sphere floating above them.
- **The UI looks unfinished.** The project theme never reaches anything under the CanvasLayer, so headers render white on white. The dashboard title literally reads `%s — Oversikt`. English and Norwegian are mixed, charts are static mock-ups, the goal HUD and toasts overlap the controls, and 731 emoji serve as icons.
- **The core loop cannot be completed.** No UI lets the player expand, so tutorial step 16 never completes and `forste_sesong` (the demo scenario) can only be "won" by skipping the tutorial. "Fôr" fails by design. There is no pause menu, no way to quit to the menu, and no end screen.
- **The simulation's signature mechanics are thin.** Capacity is almost free and overstocking dominates. The traffic light changes a field nothing reads. Price is clamped to 74–90 NOK/kg. Harvest sells live kilos at the gutted-weight price. Every production area has the same temperature, and growth runs 25–45% too fast.
- **The visual gate cannot catch any of this.** A pHash threshold of 25/64 accepted all of it into the baseline.

Most fixes are well understood. Four of them unblock everything else: fixing the terrain (winding and noise), letting the camera show the horizon, making the theme project-wide, and giving the environment a single owner.

### 1.1 Where the audits disagreed, and what I checked myself

| Topic | Disagreement | Resolution (with evidence) |
|---|---|---|
| Terrain root cause | Visual and render audits blame the C# warp hash and say C# is the "preferred path". The tech3d audit blames the triangle winding and the GDScript warp. | **Three compounding causes, all real.** (1) Windowed sessions use the GDScript path (`FjordTerrain.gd:58-63, 88-99`); C# runs only in the headless/sync path (`:115-121`). (2) The indices `(i, i+size, i+1)` at `FjordTerrain.gd:315-322` are counter-clockwise seen from +Y. Godot's front faces are clockwise and `terrain_shader.gdshader:2` uses `cull_back`, so every upward-facing triangle is culled. `08_fjord_day.png` shows isolated triangles with gaps between them. (3) Both warps alias: GDScript uses amplitude 30 at frequency 1.0 over `nx*3` (`:153-164, :252`), and C# uses a per-0.001-unit hash (`FastNoise.cs:55-59`). Fix all three and keep one generator (G1-01). |
| MSAA level | The visual audit says "MSAA 2x only". | `project.godot:91` sets `msaa_3d=2`, which is **4x** (Viewport.MSAA_4X). Keep 4x and add SMAA. |
| Terrain technology | The visual audit recommends Terrain3D. Tech3d says to spike it first. The render audit says not to adopt it. | **In-house chunked/clipmap terrain.** Terrain3D v1.0.2 targets Godot 4.4–4.6, its 4.7 support exists only on main, its docs list Metal as untested, and its macOS binaries are unsigned (#227, #993). |
| Water model | Tech3d proposes alpha 1 with screen-texture refraction. The render audit proposes fully opaque water with baked bathymetry. | **Opaque water with baked bathymetry.** Reading `screen_texture` keeps the material in the transparent pass, which costs SSR, depth of field and correct sorting with nets and boats. |
| GI on Mac | The W1 decision lists SDFGI as the "High" tier. The render audit says SDFGI gets no indirect light on Metal in 4.7 (Godot #120496). | **No SDFGI on macOS.** Use SSAO+SSIL instead, and correct `docs/research/TECH_GRAPHICS.md` and `W1_DECISIONS.md`. |
| Fog numbers | Tech3d uses ad-hoc densities. The render audit derives them from visibility (Koschmieder). | **Koschmieder**, σ = 3.912/V, driven by the sim's visibility. Same order of magnitude, but tied to the sim. |
| Camera pitch | Tech3d steepens the pitch when zoomed out. The render audit flattens it when zoomed out, steepens it at FARM, and wants the horizon in frame. | **The horizon must be in frame at the FJORD default.** With a 45° FOV that needs a pitch of −18° or shallower. Defaults: FARM −32°, FJORD −18°, COAST −22°, plus a top-down toggle for strategy (D15). |
| UI palette | The visual audit proposes dark glass. The art audit proposes light "Kyst & Stål" chart paper. | **Hybrid** (D13): dark Ink glass for the HUD and world overlays, Chart paper for document panels. All pairs computed at ≥4.5:1 (§3, D13). |
| Fonts | The visual audit drops monospace. The art audit keeps JetBrains Mono for figures. | **Inter with tabular figures + Barlow Condensed.** The user rules cap fonts at two families. |
| Hotkeys | UX puts hubs on 1–6. Art puts speeds on 1–4. | Speeds on 1–4 (genre convention); hubs on Cmd/Ctrl+1–6 (D15). |
| Research tooling | Firecrawl returned 401 in every audit. | Rotating the key is an owner action (D26). The research here used WebSearch/WebFetch. |
| Tree state | The task said "clean tree". | **It is not clean.** `tools/capture_screenshots.sh` and `tools/validate_scenes.sh` carry an uncommitted MCP token-sandbox fix. Land it in P0-01. |

Also confirmed in code: `Dashboard.gd:244` (title), `ScenarioSelectPanel.gd:55` ("300 000 NOK" while `scenarios.json:8` holds 3,000,000) and `:230` (padlock on every card), `SimActions.gd:159-167` (feed always fails), `GameBridge.Core.cs:968` (hard-coded −1M bankruptcy), `FjordCamera.gd:232-243` (collapse onto the focus and the +10 m clamp), and `default_theme.gd` loaded as an autoload with no `gui/theme/custom` in `project.godot`.

---

## 2. Conventions

- **Phases are ship points.** Each phase ends in a build that passes CI plus that phase's exit gate, and the baseline is re-promoted with art review. Tracks inside a phase run in parallel worktrees, and one merge captain per phase owns the integration order (§10). Later-phase items may start early once their dependencies are met and they do not conflict.
- **Effort** is measured in agent-days for one track: S ≤ 2, M 3–5, L 6–10, XL 11–20.
- **Area** is one of graphics, assets, ux, audio, sim, tech or qa.
- **Every item** is one PR (or a short series) carrying RUN_LOG §1 evidence: the command, the raw output, screenshot paths and their metric values.
- **Scoreboard rule** (RUN_LOG §2): tests and thresholds are only ever strengthened. New QA gates start with an **expected-fail ledger** (`qa/expected_failures.json`) that lists today's known failures by item ID. The ledger may only shrink, and each item's acceptance includes deleting its own entries.
- **Calendar** dates are targets. Exit gates decide when a phase ends.

| Phase | Theme | Target window | Exit headline |
|---|---|---|---|
| 0 | Groundwork and gates | Oct 15 – Oct 23, 2026 | GitHub CI green; objective visual/UI gates live; nothing dev-only ships |
| 1 | **First Light**: the world and UI look intentional | Oct 26 – Nov 27 | A real fjord with sky and dark water; readable, honest, one-language UI; pause and quit work |
| 2 | **A Game You Can Finish** | Nov 30 – Jan 29, 2027 | `forste_sesong` won through UI input alone; every verb works with a preview and a payoff; real fjords from elevation data; hero pens. **Coming Soon page** |
| 3 | **The Fjord Is the Game** | Feb 1 – Mar 12 | World-first UI; lenses; living fish, vessels and event visuals; market and traffic light are real |
| 4 | **Elite Simulation and Decisions** | Mar 15 – May 7 | Pathogens/ISA, mechanistic mortality, capacity market, choice events; no-dominant-strategy sweep passes |
| 5 | **Signature and Ship** | May 10 – Jun 4 | D3–D8: performance tiers, Deck, localization, audio identity, save fuzz, soak, demo. **June 2027 Next Fest** |

---

## 3. Open owner decisions, resolved

The owner has authorised me to decide these. Each decision is reversible unless stated otherwise, and each gets logged in RUN_LOG §7 when it lands.

| # | Decision | Ruling | Why | Lands in |
|---|---|---|---|---|
| D1 | Push gate | **Push `review-remediation` to a PR branch on origin (never `main`) at the start of Phase 0.** Merge only on all-green ci/verify/visual-qa. Check the repo's visibility first, and record the push in RUN_LOG §9. | None of the four workflows has ever run, so every later gate assumes they work. | P0-01 |
| D2 | F1024 water chemistry | **Merge in Phase 1.** Rebase `8123ea1`; only `Events/Storyteller.cs` has drifted. Replace the three disputed tests with stronger ones: (1) FCR in **kg feed per kg gain**, band 1.0–1.6, on the engine path; (2) the "PO04 beats PO12" claim is false in the current model, because both zones grow identically and the result is decided by O2 noise, so it becomes an honest parity test now plus a **pre-registered** engine test, "PO4 reaches 5 kg 2–4 months before PO13", which S2-02/S2-03 must make pass; (3) realized price ≥ 0.95 × mean spot on harvest days, with a probe harvest rule (≥ 4.5 kg when spot ≥ its 30-day mean, forced at 6 kg). Also fix the seawater O2 solubility (`WaterQuality.cs:39-44`). | The old tests passed only on a numerical artifact (O2 at 0 mg/L on 293/600 days, bloom on 597/600). The replacements assert more, which fits the spirit of the add-only rule. | S1-01 |
| D3 | F456 storm season | **Renormalize the default chain now**, through one helper shared with `StormRiskNext3Days`. Re-measure the storm-dependent bands; do not loosen them. Monthly per-PO matrices follow in S2-02. | Today winter has zero severe-storm days and the forecast disagrees with what happens. This is a correction, not a regression. | S1-02 |
| D4 | Seed-13 −15M bound | **Keep it as a dated, temporary guard that expires at the Phase 4 exit.** S2-05 attributes the 61% mortality, and S4-02/S4-03 fix the cause. Then restore −1M and add a cycle-mortality band of ≤ 35% for seeds 7 and 13. | Loosening the bound hides a model defect. | S2-05, S4-03 |
| D5 | ISA scenario | **Build a per-site notifiable-disease state machine**: S→E→suspected→confirmed→cull order→fallow ≥ 90 d→surveillance, with control zones, a seaway spread kernel and wellboat contacts. Then strengthen the `isa_utbrudd` test. | A shipped scenario is inert today. | S4-02 |
| D6 | Orphaned panels | **Crisis**: wire it after F271, together with the capacity market (S4-01). **MarketChannel**: wire it after harvest grading (S2-04). **Genetics**: fold strain choice into stocking and delete the panel. **Stakeholder**: delete (its data is random). **Empire, IPO, CompetitorDashboard**: defer to after 1.0 and strip their stub data. **ProcessingMix**: wire it after the HOG/fillet yield model. **EventLog**: wire it into the end screen (U2-03). **DecisionPanel, MorningBriefing, NextActionWidget**: revive them with choice events (S4-08). **CreditsScreen**: wire it now, because attribution is a licence requirement (U1-06). **DailySummary, FjordTensionMeter, MiniMap**: delete. | Wiring a panel onto a stub ships fake depth. | S4-11 and as listed |
| D7 | F177 MCP addon | **Keep the dev exemption, but the MCP runtime and the QA hooks must never ship.** Guard them with a feature tag, add an export exclude filter, and make CI assert their absence in exported builds. | Shipping a remote-control autoload that publishes a session token would be a security defect. | P0-02 |
| D8 | WellboatJob ids | **Change `Job_Defaults` to assert a deterministic id** (day, site, kind, sequence) and drop the allowlist line. | The test gets stronger and the code gets more deterministic. | P0-07 |
| D9 | Next Fest | **Do not register for February 2027** (deadline 2027-01-10). **Target June 2027**, the W1 fallback. Publish the Coming Soon page at the Phase 2 exit using Phase 2 frames, and re-base D10's wishlist targets to the June date. | The February demo would ship before the loop is completable and before the hero assets exist, and the one shot must not be spent on a weak demo. | P5-01 |
| D10 | Strategy-sweep failure criterion | **FAIL if (a)** any rule-violating strategy (overstock, ignoring lice orders, skipping fallow) has a median 5-year equity ≥ the best compliant strategy's, after fines, over 20 seeds; **or (b)** one compliant strategy that pins a lever at its bound beats every other strategy by more than 25% in median equity on at least 80% of seeds. Pre-register this in RUN_LOG §6 before the run. | The sweep must be able to fail. | Q4-01 |
| D11 | Terrain source | **Phase 1:** a procedural U-trough. **Phase 2:** real elevation data for the demo regions (Vestland for `forste_sesong`, PO3 for `po3_red_sone`). Use Kartverket DTM10 under CC BY 4.0 "© Kartverket" after checking each tile's licence record, because some Geonorge records say CC BY-NC; fall back to Copernicus GLO-30 with its attribution. Seabed comes from Kartverket "Dybdedata terrengmodell" at 25/50 m. | Authenticity is the pitch, and real data beats any noise. | G1-01, G2-01 |
| D12 | Third-party render tech | **Adopt:** FastNoiseLite C# (MIT), Poly Haven/ambientCG CC0 textures, the Phosphor icon set (MIT). **Do not adopt now:** Terrain3D, GodotOceanWaves (broken on 4.7, issues #26/#27; FFT stays parked), SunshineClouds2 (Metal issue #34), PlanarReflector-CPP (an unsigned native binary), and any Shadertoy port (default licence CC BY-NC-SA). **Anti-aliasing:** MSAA 4x + SMAA, with TAA optional. | These tools are broken or unproven on Godot 4.7.2 with Metal, so they carry signing and licence risk. | G1-06, G5-01 |
| D13 | Art direction | **World: "Vestland Light"**, stylized-realistic. Sun 10–35° high, AgX tonemapping, fog that gives aerial perspective, matte painterly PBR materials, saturated hue only on equipment and gameplay signals. Landscape palette: granite #6B7378, wet rock #3E4448, spruce #2F4A3A, heath #8F9A5A, snow #F4F7FA, deep water #0E2A33, black HDPE #1C1E20, buoy yellow #E8B92A. **UI: "Kyst & Stål"** with two surfaces. **Ink** glass (#0B2A3A at 90%) for the HUD, the rail, world cards and toasts: text #E8EEF0 12.7:1, secondary #9FB4BB 6.9:1, ok #7FD69B 8.5:1, warn #F2B705 8.2:1, crit #FF8A80 6.5:1, info #8CC7DA 8.0:1, accent salmon #F2876A 6.0:1. **Chart** paper (#F4F1EA) for document panels: text #1C1E20 14.8:1, muted #5A6168 5.6:1, ok #2E6B45 5.6:1, warn #8A5A00 5.3:1, crit #B3172F 6.0:1, info #1F5E73 6.4:1, accent-as-text #B5452B 4.8:1; #D9583B (3.4:1) is for large or decorative use only. **Primary buttons use Ink text on salmon (6.0:1), never white on salmon (3.9:1).** All ratios computed in the audit scratch script contrast.py (not in the repo). | The current palette is Nord's code-editor palette and fails WCAG (amber 1.56:1). The hybrid suits a world-first game: overlays sit on Ink, documents sit on Chart. | U1-02 |
| D14 | Type and icons | **Inter** for the UI (with `tnum` tabular figures) and **Barlow Condensed** (OFL) for display. Remove JetBrains Mono from the player UI and IBM Plex from the tokens. Use Phosphor icons plus about 20 custom aquaculture glyphs; CI bans emoji. | Emoji render differently on each OS and show as tofu on SteamOS. | U1-02, U3-02 |
| D15 | Information architecture and controls | **The fjord is the home screen**, with six hubs: Drift, Helse, Marked, Økonomi, Vekst, and Regulering & bærekraft. Save/Load, Export, Settings and Leksikon move to the pause and help menus. **Keys:** Space = pause; 1–4 = 1×/3×/10×/30×; N = next event; Cmd/Ctrl+1–6 = hubs; Shift+1–6 = lenses; WASD or middle-drag = pan; Q/E = rotate; Home = recentre; T = top-down; Esc = pause menu; ? = controls overlay. No F-keys (they clash with macOS). Everything rebindable. | F1–F10 conflict with macOS. Nineteen flat tabs give the player no hierarchy. | U1-04, U3-01 |
| D16 | Economy rules | Achievements and tutorial steps pay **no cash**; they award medals and Steam achievements. The bankruptcy floor is set per scenario in BalancingConfig, with a warning ladder before it. **Resource-rent tax is on by default** (the 70M deduction already shields family farms). Once the price clamp is gone, premiums multiply the realized price. | Reward cash currently tops the income list (06). This supersedes the 2026-10-13 premium-cap decision. | S1-03, S3-01 |
| D17 | Real names | **Parody names only.** Rename Whole Foods and the Cermaq, Bakkafrost, Mowi Scotland and Multiexport entries (`MarketIntelligence.cs:289-295`). | Legal risk. Rival companies are already parodies. | U1-03 |
| D18 | Live data | **Offline snapshots only.** No network I/O in `Initialize`; remove the `BARENTSWATCH_TOKEN` fetch (`SimulationEngine.cs:780-819`). | That fetch breaks reproducibility between machines. | S2-02 |
| D19 | Tutorial scope | Only `forste_sesong` runs the tutorial; no panel gating anywhere else. Skipping the tutorial is not a win. | Expert scenarios currently hide the very tools they are about. | U2-02 |
| D20 | 3D clock | The **date, season and latitude** come from the sim calendar. The **hour of day** stays a presentation clock, because a 1-second sim day cannot drive a visible day cycle. Photo mode can pin the hour. | Night and the aurora still follow the real season and latitude. | G1-04 |
| D21 | `high_contrast` setting | **Implement it** as a theme variant rather than deleting it. | Accessibility. | U2-08 |
| D22 | Assets and spend | **Free first:** hero assets built through the Blender MCP from real dimensions, with CC0 textures and audio. **Optional spend (owner gate):** a freelance 3D-art review of the pen, barge and wellboat, triggered only if the Phase 2 art checklist fails twice. | Same quality bar, lowest cost. | A2-01, A3-01 |
| D23 | AI-content disclosure | File Steam's **pre-generated content** disclosure, covering AI-assisted code and AI-scripted 3D assets and textures. No generative AI runs in the game. The owner signs (legal gate). | Honest, and required. | P5-01 |
| D24 | Licence | The **game is proprietary.** Mod schemas, mod docs and the example pack are **MIT.** Add `THIRD_PARTY.md` and in-game credits for every MIT, OFL and CC BY item. | CC BY requires attribution, and an open mod format helps modders. | P0-08 |
| D25 | Price | **Recommend $24.99** with a 10–15% launch discount. Confirm after the Phase 4 playtest (owner gate). | Top of the W1 band, justified by depth. | P5-01 |
| D26 | Firecrawl | The **owner rotates the API key** (every audit got 401). Until then, research uses WebSearch/WebFetch. | Only the owner can change the credentials. | P0-08 |
| D27 | F618 Space | **Keep** Space as the global pause key; Enter activates the focused button. | Already a documented trade-off. | — |

---

## 4. Phase 0: Groundwork and gates (≈1.5 weeks)

**Goal:** make Phase 1 verifiable and parallel-safe. No player-visible change.

**P0-01: Land in-flight work and run GitHub CI for the first time** · tech · S–M · from RECHECK §5
- Files: `tools/capture_screenshots.sh`, `tools/validate_scenes.sh` (the uncommitted MCP token sandbox), `.github/workflows/{ci,verify,visual-qa,design-tokens}.yml`, `docs/RUN_LOG.md` §9.
- Approach: commit the sandbox fix. Push to a PR branch (D1). Fix whatever the first run exposes: the Linux Xvfb shader gate and the self-hosted Mac runner for visual-qa.
- Accept: all four workflows green on GitHub with `CI_REQUIRE_ALL=1`; run URLs recorded in RUN_LOG.
- Deps: none · Parallel: yes, no file conflicts with the rest of P0.

**P0-02: Keep dev-only code out of shipped builds** · tech/security · S · from F177, F708
- Files: `godot/project.godot` (autoloads), `godot/export_presets.cfg` (exclude filters), the MCP runtime autoload script, the bridge QA hooks (`StartQaScenario`, `SetQaPenLice`, `SpawnQaBloom`…), and the CI export-verify step.
- Approach: each autoload and QA hook frees itself or refuses unless `OS.has_feature("editor")` or a `qa` feature tag is set. Exports exclude the addon path.
- Accept: CI exports a build and asserts that the pack has no MCP autoload and that the QA hooks return "unavailable". A UI-flow test confirms the hooks still work under the `qa` tag.
- Deps: none · Parallel: yes.

**P0-03: Visual QA v2 (state reset, capture matrix, objective metrics, expected-fail ledger)** · qa · M · [M] visual "QA gate cannot catch"
- Files: `godot/scripts/tools/ScreenshotHarness.gd` (shots move into a data file, `godot/qa/shots.json`, so items add shots without touching code), `.claude/skills/ecc/visual-qa/visual_qa/*`, `tools/capture_screenshots.sh`, `tools/ci.sh`, new `tools/ui_lint.py`, new `qa/expected_failures.json`, `tests/test_visual_qa_diff.py`.
- Approach:
  - Reset the environment and sim before every shot. Today 14/15/16 inherit the bloom state.
  - Capture a matrix: 1280×720, 1920×1080 and 2560×1440; UI scale 100 and 125; day, night and storm for the fjord.
  - Compute metrics:
    - **sky fraction** and **horizon row**, from a sky-ID pass or the camera ray math;
    - **land fragmentation**: the count of connected land components smaller than 0.05% of the frame;
    - **per-region SSIM** against art-approved goldens;
    - **label dump lint**: format tokens (`%s/%d/%f`), CamelCase enums, snake_case ids, and English stop-words when the locale is nb;
    - **contrast lint**: each Label's resolved font colour against its nearest panel background, ≥ 4.5:1;
    - **overlap lint**: pairwise intersections between the HUD, tab bar, goal, toast stack and notification panel;
    - **theme-resolution lint**: no visible Control still styled by Godot's default theme.
- Accept: the gates run in CI. Every current failure is listed in `expected_failures.json` with its owning item ID. The ledger file has a pytest that rejects additions. The pHash threshold of 25 stays until Phase 1 promotes the SSIM goldens, after which SSIM ≥ 0.92 per region applies; that is a strengthening.
- Deps: P0-01 · Parallel: yes, and it owns the harness.

**P0-04: Performance smoke test** · qa · S · from tech3d "wasted GPU"
- Files: new `godot/scripts/tools/PerfHarness.gd`, `tools/ci.sh` (Mac runner).
- Approach: 10 s at each zoom (FARM, FJORD, COAST) at 1440p, plus one run with a full-screen panel open. Log p50/p95 frame time, `TIME_PROCESS`, draw calls and VRAM.
- Accept: baseline numbers recorded in `docs/run/evidence/`. The gate is warn-only until the Phase 1 exit, then fails above p95 16.7 ms on the M1 Pro.
- Deps: P0-01 · Parallel: yes.

**P0-05: Delete dead rendering stacks and duplicate sync layers** · tech · S · [L] render "dead stacks", F746 part, F753
- Files: `godot/shaders/fjord_water.gdshader`, `water_wave_shader.gdshader`, `fjord_sky.gdshader`; `godot/scenes/NordlandFjord.tscn` and `scripts/environment/NordlandFjordController.gd`; `FjordTerrainGenerator.gd`; `scenes/environment/FjordTerrain3D.gd`; `addons/gd-retroterrain`; `scripts/environment/Sim3DSync.gd` and its node in `FjordEnvironment.tscn`; the dead body of `scripts/visual/VisualAdapter3D.gd`; `scenes/environment/FJORD_RENDERER.md`.
- Accept: scene validation 54/54 clean; the bridge contract still passes; grep finds no references; renderer docs match the code that ships.
- Deps: none · Parallel: conflicts with P0-06 on `FjordEnvironment.tscn`, so land P0-05 first.

**P0-06: Split `FjordEnvironment.tscn` into separately owned resources** · tech · S
- Files: `godot/scenes/environment/FjordEnvironment.tscn` → `environment.tres`, `water_material.tres`, `sky_material.tres`, `camera_attributes.tres`. Also split `water_shader.gdshader` into `water_waves.gdshaderinc`, `water_optics.gdshaderinc` and `water_foam.gdshaderinc`.
- Accept: frames are pixel-identical before and after (md5 of 08–16 with the sim pinned); the shader gate passes.
- Deps: P0-05 · Parallel: blocks G1-02, G1-05 and G1-06 until merged.

**P0-07: Harden the determinism gate (open lows)** · tech · S · RECHECK §2 lows, D8
- Files: `DeterminismBanTests` (ban `DateTimeOffset.Now` and friends; key the allowlist by symbol rather than line text), `CrossProcessDeterminismTests` (enforce the timeout), `SimulationEngine.Initialize` (record `State.RandomSeed`), `src/LakskongenSim/Logistics/WellboatJob.cs` and `WellboatJobTests.Job_Defaults` (deterministic id).
- Accept: each change has a mutation check (the test fails with the fix reverted); the allowlist shrinks by one line.
- Deps: none · Parallel: yes, sim files only.

**P0-08: Research and documentation hygiene** · tech · S · [L] art "Firecrawl / Ships At Sea", render "SDFGI on Metal"
- Files: `docs/research/TECH_GRAPHICS.md` (SDFGI on Metal, Terrain3D and GodotOceanWaves status, the Ships At Sea claim), `docs/research/W1_DECISIONS.md` (R2 item 4), new `THIRD_PARTY.md` (D24). The Firecrawl key is the owner's (D26).
- Accept: the docs carry corrected claims with source URLs; `THIRD_PARTY.md` lists every bundled third-party file and its licence.

**Phase 0 exit:** P0-01 green on GitHub; the P0-03 gates run with the ledger committed; perf baseline recorded; the export contains no dev tooling.

---

## 5. Phase 1: First Light (≈5 weeks)

**Goal:** every frame a player or reviewer sees looks intentional. The world is a fjord with sky, horizon and dark water. The UI is readable, honest and in one language. The player can pause, quit and pick any scenario.

**Tracks:**
- G-A: G1-01 → G1-03 → G1-06
- G-B: G1-02 → G1-04; G1-05 starts after G1-01's bathymetry lands
- G-C: G1-07
- U-A: U1-01 → U1-02 → (U1-03 ∥ U1-05 ∥ U1-07)
- U-B: U1-04 → U1-06 → U1-08 → U1-09
- S: S1-01 → S1-02 → S1-03

### Graphics

**G1-01: Terrain with correct winding, one generator, and a real U-shaped fjord** · graphics · M · [C] visual#1, tech3d#1–2, render#1, art#1 (F750, F464)
- Files:
  - `godot/scripts/environment/FjordTerrain.gd`: indices `:315-322`; noise `:153-164`; heightmap `:226-263`; async path `:58-110`; normals `:328-335`; collider `:189-197`.
  - `src/LakskongenSim/Environment/FastNoise.cs`: replace with the vendored FastNoiseLite C# port (MIT).
  - `src/LakskongenSim/Environment/TerrainGenerator.cs:43, :147-148`; `godot/scripts/environment/TerrainGeneratorBridge.cs`.
  - New `src/LakskongenSim.Tests/Environment/TerrainGeneratorTests.cs` and a headless terrain check in `godot/tests/`.
- Approach:
  1. Wind the indices clockwise: `(i, i+1, i+size)` and `(i+1, i+size+1, i+size)`. Do not use `cull_disabled`.
  2. Work in world metres with one cross-section: h = shoulder(d) − D·max(0, 1 − (d/w)⁴), with half-width w 350–600 m, depth D 120–250 m, walls 30–60°, shoulders 350–700 m, a 30–60 m sill at the mouth and a skerry strandflat outside it.
  3. Add detail with OpenSimplex2 FBM: frequency 1/1200 m⁻¹, 6 octaves, lacunarity 2, gain 0.5. Blend in ridged noise only above 150 m. Warp with OpenSimplex2Reduced at amplitude 150–200 m and frequency 1/2500 m⁻¹, with no warp fractal. Mask the detail by elevation so the fjord floor and beaches stay smooth.
  4. Call the C# generator from the worker thread in windowed mode too, and delete `_generate_heightmap_gd`, so headless and windowed runs share one heightfield.
  5. Raise the grid to 512² (7.8 m spacing). Build the arrays on the worker thread and only add the surface on the main thread. Use central-difference normals and `HeightMapShape3D` instead of a 130k-face concave collider.
- Accept:
  1. Every triangle's `Plane(v0,v1,v2).normal.y > 0`, and its dot product with the stored normal is > 0 (headless test).
  2. For seeds 1–10, the 99th percentile of |Δh|/spacing is < 2.0 outside the wall-band mask.
  3. Cross-sections match the profile: floor −120 to −250 m, median wall slope 30–60°, shoulders 350–700 m.
  4. The heightfield hash is identical between headless and windowed runs and across processes.
  5. Shots 08/08b/09/10/12/13/14: land fragmentation = 0 small components, a continuous skyline, and pHash Δ ≥ 16 against the old baseline.
  6. Generation never takes a main-thread frame over 50 ms.
  7. Removes its own P0-03 ledger entries.
- Deps: P0-03, P0-06 · Parallel: yes; owns the terrain files.

**G1-02: EnvironmentDirector as the only writer of Environment, sky and weather state** · graphics · M · [C] render#2; [H] tech3d#4, visual "sky/weather overwrite"
- Files:
  - New `godot/scripts/environment/EnvironmentDirector.gd`.
  - `TimeOfDay.gd:69-87, 167-169, 186-195`; `WeatherController.gd:67-70, 114-127, 133-139`; `FjordEnvironment.gd:117-129`; `FjordVistaBridge.gd:113-131`.
  - `environment.tres` (from P0-06); `tilt_shift.gdshader:70-86` (grading removed).
- Approach:
  - Inputs come from TimeOfDay (sun), the sim (weather state, visibility, wind, Hs) and the camera (zoom, underwater). The director composes the result each frame and tweens it.
  - Fog follows Koschmieder, σ = 3.912/V, so clear 40 km gives 1.0e-4 and sea fog at 300 m gives 1.3e-2. Fog stays on; aerial perspective 0.6, sky affect 0.15, sun scatter 0.25; height fog only for sea-fog weather.
  - Tonemapping: AgX with `tonemap_agx_white` 6 and contrast 1.3 (check the property name on 4.7.2); adjustments off. Glow: Screen blend, intensity 0.25, threshold 1.0, levels 3–5.
  - Cloud coverage comes from the weather only. Clouds dim the sun: light energy × lerp(1, 0.15, c), with matching shadow opacity and angular size.
  - An underwater profile is applied on entry and restored on exit; this fixes the stuck fog colour.
- Accept:
  1. A static pytest finds exactly one writer of `fog_*`, `tonemap_*`, sky `cloud_coverage`, sun `light_energy`/`shadow_opacity` and the water wave uniforms.
  2. Harness-computed transmittance to the FJORD focus is ≥ 0.85 on a clear day and ≤ 0.35 in a storm.
  3. 08a is no longer a whiteout: land-region mean luminance ≤ 0.75 and RMS contrast ≥ 0.12.
  4. With the sun pinned, clear/overcast/rain/storm/fog frames differ pairwise by pHash Δ ≥ 9.
  5. Leaving underwater restores the day fog colour (test).
- Deps: P0-06 · Parallel: conflicts with G1-04 (TimeOfDay, WeatherController); same track, sequential.

**G1-03: Camera rig with horizon framing, continuous zoom, terrain avoidance and input gating** · graphics · M · [C] visual#3; [H] tech3d#5, render#6, UX "fjord is wallpaper" (camera parts)
- Files: `godot/scripts/environment/FjordCamera.gd:16-21, 97-136, 172-186, 219, 228-243`; `godot/scenes/GameScene.tscn:28-31` (auto-orbit); `godot/scripts/ui/GameUI.gd:179` (key gating); `FjordCameraHint.tscn`.
- Approach:
  - Continuous distance from 25 to 3000 m, ×1.15 per wheel notch, zooming toward the cursor.
  - Pitch comes from a per-distance table (FARM −32°, FJORD −18°, COAST −22°) so the horizon sits 10–25% from the top of the frame.
  - Frame-rate-independent damping on every axis: x += (t − x)(1 − e^(−12·dt)).
  - Terrain avoidance lifts the camera to clear the terrain by 25–40 m, sampling the height at the camera and at 6 points along the sight line. It never teleports over the focus.
  - Auto-orbit runs only in the menu or after 30 s idle. Q/E rotate, Home recentres, double-click focuses, T toggles top-down.
  - Camera keys act only when no Control has focus and the world is visible. The camera hint and "Fly til merd" hide while an opaque panel is open.
  - The UNDERWATER mode skips the +10 m clamp (it inherits the G1-02/G1-05 v1 underwater look).
  - Default yaw runs along the fjord axis toward the mouth.
  - Until G2-01 adds the far field, the camera is soft-clamped 300 m inside the world edge.
- Accept:
  1. Sky fraction ≥ 10% and horizon row in 10–25% in 08/09/10 at the default.
  2. For 200 seeded orbits × 3 zooms, the camera sits ≥ 20 m above the terrain and the focus is unoccluded in ≥ 99%.
  3. In 11, `camera.global_position.y < 0`.
  4. During a 10 s orbit and zoom sweep, the per-frame camera jump stays ≤ 2% of the distance.
  5. UI flow: arrow keys on the tab bar and WASD in a LineEdit leave the camera still.
- Deps: G1-01 · Parallel: `GameUI.gd` is shared with U1-04/U1-06; land G1-03's two-line key gate first.

**G1-04: Sky, sun and calendar agree** · graphics · M · [H] visual "sky/weather", render#6; [M] tech3d "sky disagrees", render "clouds"
- Files: `godot/shaders/sky_shader.gdshader:13, 25-26, 46, 80-91, 114-134`; `TimeOfDay.gd:39-45, 126-160`; `ScreenshotHarness.gd:588-596` (stop forcing the aurora).
- Approach:
  - The sun disc uses `LIGHT0_DIRECTION/COLOR/ENERGY`.
  - Real solar geometry: elevation from latitude φ, solar declination δ(N) and hour angle, with N and latitude taken from the sim (D20).
  - Day and night factors come from `LIGHT0_DIRECTION.y`. Two layers of FBM clouds take their coverage from the director. Skip work in `AT_CUBEMAP_PASS` and set `Sky.process_mode` to incremental.
  - Add a moon light (#C7D7E8, energy 0.08). Sun energy fades rather than shadows switching on and off.
  - In-house aurora curtains: green 557.7 nm (#78F2A0), with a red top fringe only in strong displays. Intensity is driven by latitude, season, darkness and clear sky.
- Accept:
  1. The sun disc is within 0.5° of the DirectionalLight.
  2. Unit test: Bodø (67.3°N) maximum elevation < 0° on Dec 21 and minimum > 0° on Jun 21; Bergen noon on Jun 21 is 53° ± 1°.
  3. 09 is captured in a real gameplay state (Nordland, winter, clear): aurora pixel fraction ≥ 3%; under overcast it is 0.
  4. No step in sun energy > 10% between frames.
  5. GPU frame time drop from the radiance fix is recorded by P0-04.
- Deps: G1-02, G1-03 · Parallel: same track as G1-02.

**G1-05: Water optics v1** · graphics · L · [H] visual "water", tech3d#3, render#3
- Files: `godot/shaders/water_shader.gdshader:2, 47, 95-116, 179-200` (now `.gdshaderinc` parts); the water mesh in `FjordEnvironment.tscn:66-69` → a new camera-snapped LOD grid script; `FjordVistaBridge.gd:117-126`; `WeatherController.gd:121-127`; the ReflectionProbe at `FjordEnvironment.tscn:250-257`; new `tools/bake_bathymetry` step inside FjordTerrain (RG16F depth + shore SDF via jump flood).
- Approach:
  - **Opaque** water: no ALPHA, no screen texture. Colour comes from Beer–Lambert absorption over the baked depth, with σ ≈ (0.40, 0.09, 0.14)/m and deep scatter ≈ (0.010, 0.045, 0.050); sim algae and turbidity modify σ (global in v1).
  - A camera-snapped grid with 2 m spacing out to 256 m, doubling per ring, plus a skirt.
  - 6–8 Gerstner waves taken from the JONSWAP peak for the sim's wind and the site fetch. Σa² = Hs²/8, with analytic normals. Schlick F0 0.02 and roughness 0.04–0.18.
  - Shore foam where the SDF is < 5 m; whitecaps follow Monahan's coverage formula.
  - The underside is drawn through a `!FRONT_FACING` branch with a TIR tint (v1).
  - The ReflectionProbe is re-captured after the terrain build and at each day phase.
- Accept:
  1. A static check confirms the shader writes no ALPHA and reads no `screen_texture`.
  2. In 08b, terrain edge density inside the water mask where depth > 15 m is ≤ the golden + 5%.
  3. Mesh test: vertex spacing ≤ 2 m within 256 m of the camera.
  4. Sea-state test: sheltered at 3 m/s gives Hs 0.2–0.5 m; exposed at 20 m/s gives 2–4 m. Wind no longer multiplies amplitude.
  5. The probe is captured at least once after the terrain build (counter).
  6. The surface stays below the collar's freeboard at storm Hs until G2-03 adds bobbing.
  7. p95 ≤ 16.7 ms at 1440p at FJORD.
- Deps: G1-01 (heightmap), G1-02 · Parallel: owns the water files; conflicts with G1-02 only on the wave-uniform writer, which goes through the director.

**G1-06: Image pipeline (depth of field, anti-aliasing, net, shadows, render cost)** · graphics · S–M · [M] visual "post", tech3d "lighting/AA", "tilt-shift", "wasted GPU", render "tilt-shift", "net moiré", "tiering"
- Files: `FjordEnvironment.tscn:139-148, 290-300` (remove TiltShiftCanvas); `camera_attributes.tres`; `FjordCamera.gd:249-260`; `godot/shaders/pen_net_shader.gdshader:13-27`; `project.godot` (rendering: SMAA, anisotropic 4, shadow atlas 4096); `EnvironmentDirector.gd` (per-zoom SSAO and shadow distance); a one-signal hook in `GameUI.gd` (`opaque_panel_changed`).
- Approach:
  - CameraAttributesPractical depth of field driven by the focus distance (amount COAST 0.14, FJORD 0.10, FARM 0.06, underwater 0).
  - MSAA 4x + SMAA.
  - The net uses alpha-to-coverage, fading to its mean coverage when `fwidth > 0.35`.
  - Shadow max distance by zoom: FARM 350, FJORD 1600, COAST 4500. SSAO radius by zoom: 1.5, 4 or 8.
  - While an opaque full-screen panel is shown, set `scaling_3d_scale` to 0.5 and pause sky updates.
- Accept:
  1. With and without tilt-shift, the mean colour differs by ΔE2000 < 2 (14 vs 08).
  2. A 2 s slow-orbit capture shows net-region frame-to-frame variance cut by ≥ 50%.
  3. The selected pen is sharper than the background (variance-of-Laplacian ratio ≥ 2).
  4. GPU time on 06 drops ≥ 40% (P0-04).
  5. No SDFGI in any macOS preset.
- Deps: G1-02, G1-03, G1-05 · Parallel: shares `FjordCamera.gd` with G1-03; same track.

**G1-07: Quick fixes to placeholder models** · assets · S · [H] visual "placeholders", tech3d "pens primitive", art "pen model" (Phase 1 subset), [L] selection
- Files:
  - `godot/assets/models/pen_cage/pen_net_mesh.tres` (`cap_top = false`); `pen_collar_mesh.tres` and `pen_handrail_mesh.tres` (use the real TorusMesh properties `rings`/`ring_segments`); `pen_collar_material.tres` (black HDPE #1C1E20, roughness 0.6); `pen_feedpipe_mesh.tres` with its transform (horizontal).
  - `FishPenManager.gd:95-109` (debug sphere → emissive selection ring on the water), `:224-229` (Label3D billboard), `:237-254` (`health_color_for` key mismatch); `pen_collar_shader.gdshader:11-22` (health moves to the ring and beacon, out of the albedo).
  - `assets/models/boat/service_boat.tscn` (hulls along Z); `CompetitorFarmVisualManager.gd:133-141` (no grey 60% override; a colour band instead).
- Accept:
  1. 11 shows no net lid.
  2. No unshaded sphere in any frame.
  3. Collar albedo luminance < 0.1.
  4. The health ring differs between 08b and 12 by ΔE ≥ 20.
  5. Rival pens' net alpha ≤ 0.55.
  6. Boat hulls are horizontal (AABB: x/z extent > y).
- Deps: none · Parallel: owns `FishPenManager.gd` in Phase 1. A2-01, G2-04 and G3-01 later serialize on it.

### UI

**U1-01: Project-wide theme** · ux · S · [C] visual#2
- Files: `godot/theme/default_theme.gd:155-156` (save the built theme as `res://theme/lakskongen_theme.tres`, regenerated by `tools/sync_tokens.py`); `godot/project.godot` (`[gui] theme/custom`); `godot/scenes/GameScene.tscn:52-53` (theme on the first Control under UILayer); `ScreenshotHarness.gd:210-237`; `TelemetryPanel.gd:706-708`; FinancePanel insurance titles; `MainMenu.gd:146-161`; the controls in 07c/07d/07e/07f.
- Accept:
  1. Theme-resolution lint: 0 visible Controls styled by `ThemeDB` defaults.
  2. Contrast lint ≥ 4.5:1 (3:1 for text ≥ 18 px bold) for every visible Label in 01–07g.
  3. The four telemetry headers and the panel title appear in 04's label dump.
  4. UI-flow tests unchanged and passing.
- Deps: P0-03 · Parallel: must merge first in U-A.

**U1-02: "Kyst & Stål" tokens and type** · ux · M · [H] visual "art direction", art "UI identity" (palette and type part); [M] UX "accessibility" (fonts)
- Files: `godot/theme/tokens.json`; `default_theme.gd:18-67, 195-203`; `tools/sync_tokens.py`; `.github/workflows/design-tokens.yml`; `godot/assets/fonts/` (add Barlow Condensed under OFL; drop JetBrains Mono from the player UI; drop IBM Plex from the tokens); the 106 per-node font-size overrides across about 70 `.tscn`/`.gd` files become theme type variations.
- Accept:
  1. The token contrast test passes every declared pair (D13 table).
  2. Font lint: no size < 13 px at the 1080p base, and no per-node override where a type variation exists.
  3. `HUD.gd:75` no longer uses a monospace font.
  4. The design-tokens workflow is green.
- Deps: U1-01 · Parallel: touches many scenes, so merge it before U1-03/04/05/07 branch.

**U1-03: Text correctness pass** · ux · M · [H] visual "text bugs", UX "mixed language"; [M] art "player-facing text"; D17
- Files:
  - `Dashboard.gd:244` with `translations.csv:9` (format the title with the company name).
  - About 50 missing keys: `AttentionInbox.gd:52, 189, 337`, `MapPanel.gd:409, 503, 521` and `MapPanel.tscn:148, 302, 315, 449`, the Genetics strings, the `UI_HUD_TIP_*` tooltips, and `ScenarioGoalHUD.gd:84-92`.
  - Enum display tables for every enum crossing the bridge: weather (`GameBridge.Core.cs:768`, `HUD.gd:135`), traffic light (`SimulationEngine.cs:2801`), containment, treatment, traits.
  - `GameBridge.Farm.cs:801` (`s.Name ?? s.Id`).
  - Typos: `scenarios.json:205-207`, `SiteOverlayLegend.gd:67, 130`, `FjordCameraHint.tscn:62`, "kulstoffneutral", "Sørg for å gjenopprett".
  - One FCR target from CalibrationRegistry through the bridge (today it is set separately in `FarmStateBanner.gd:28`, `TelemetryPanel.gd:14`, `Dashboard.gd:479-482` and `SimState.gd:313`).
  - nb-NO formatting in `godot/scripts/bridge/SimFormatters.gd`; `FinancePanel.gd:443-448` (P&L category labels); `MarketIntelligence.cs:289-295`, and Whole Foods in the contract buyers (D17).
- Accept:
  1. Label-dump lint finds zero format tokens, CamelCase enums, snake_case ids and English stop-words across all panels in nb.
  2. Every `tr`/`_tr_or` key exists for both en and no.
  3. No literal passed to `tr()`.
  4. Grep finds one FCR target constant.
  5. Formatter unit tests: nb gives `11,49 MNOK`, `71 472`, `12. jan. 2027`.
  6. The brand-name grep list is empty.
- Deps: U1-02 · Parallel: conflicts on `translations.csv` and `scenarios.json` (keys stay sorted; the merge captain resolves conflicts mechanically).

**U1-04: Screen regions and overlap** · ux · M · [H] visual "UI overlap", UX "feedback channels" (layout part); [M] UX "IA" (F-keys, overflow)
- Files: `GameScene.tscn:81-93, 121-132`; `godot/ui/ScenarioGoalHUD.gd` (a collapsible chip in the HUD, with the full objective in a tooltip); `ToastNotification.gd:19-20, 213-240` (a container-owned stack with wrapper tweens, bottom-right, at most 3 visible, deduped); `NotificationCenter.gd:158`; `Dashboard.gd:630-649` (dedupe by category and site); `GameUI.gd:123-143, 167` (F-keys removed; temporary Cmd/Ctrl+number shortcuts; the tab bar scrolls instead of clipping at 125%).
- Accept:
  1. Overlap lint: 0 intersections at 3 resolutions × UI scale 80/100/125.
  2. With 5 queued toasts, their rects are pairwise disjoint and none touches the speed, pause or inbox controls.
  3. 03 has no duplicate (category, site) alert rows.
  4. No F1–F12 bindings.
  5. No clipped tab label at 125%.
- Deps: U1-01 · Parallel: owns `GameScene.tscn` in Phase 1; U1-06 lands after it.

**U1-05: Truthful numbers (no fake charts or constants shown as state)** · ux · M · [H] visual "charts", UX "market made-up", "placeholder values", art "Hvorfor bars"; F952
- Files:
  - `FinancePanel.gd:142-150, 501-516` and `FinancePanel.tscn:281` (Resultatregnskap rows and the income-vs-costs chart bound to the P&L; share bars drawn in a plain Control).
  - `ContractsPanel.gd:76-78, 82-83, 122-134, 283-288` and `ContractsPanel.tscn:266-366` (price chart and low/average/high from `get_analytics_data('market_price', 84)`; trend computed; regional prices removed until S3-01; `BtnNewContract` wired to the orphaned `ContractDialog.gd`; `BtnSellSpot` hidden until U2-05).
  - `Dashboard.gd:305-310, 459, 540-570, 728-760` (correct series; "Fiskehelse" shows welfare; per-site lice; N site cards for N sites).
  - `CrewPanel.gd:132, 273-314`; `PenManagement.gd:275-307` (bind each value or show "Ikke målt").
- Accept:
  1. Chart-change test: in a scripted 60-day run, every chart node's data differs between day 1 and day 60 and equals the bridge series it claims to show.
  2. Share-bar width/track = share ± 2 px.
  3. A grep test finds no literal state values (`"A+"`, `"1.2%"`, `"8.5 m"`, `"Lav"`…) assigned to value labels in `scripts/ui`.
  4. Dashboard site-card count matches the site count for 1, 3 and 9 sites.
  5. 06/06b/07b/07c/07f re-captured.
- Deps: U1-02 · Parallel: owns FinancePanel, ContractsPanel and CrewPanel; shares `Dashboard.gd` with U1-04 (merge U1-04 first).

**U1-06: Pause menu, quit guard, Continue, credits** · ux · M · [C] UX "no pause menu"; D6 (CreditsScreen)
- Files: `GameScene.tscn` (instance `scenes/PauseMenu.tscn` on a high CanvasLayer); `PauseMenu.gd:51, 239, 281-291` (node paths, focus neighbours, embedded Settings and SaveLoad); `Main.gd:18-19, 145-152`; `GameUI.gd:384-393` and `Dashboard.gd:118-121` (ESC routing; the duplicate "Lagre/Laste" tab removed); `MainMenu.gd` ("Fortsett" = latest save); `NOTIFICATION_WM_CLOSE_REQUEST` handling; `CreditsScreen.gd` reachable from the pause and main menus.
- Accept (UI-flow tests with real input events):
  1. Esc on the dashboard opens the pause menu.
  2. Pause → Hovedmeny reaches the main menu.
  3. Pause → Innstillinger → back works.
  4. A window-close request shows "Lagre før du avslutter?".
  5. Fortsett restores the latest save's day and balance exactly.
  6. Credits list every `THIRD_PARTY.md` entry.
- Deps: U1-04 · Parallel: serialized after U1-04 on `GameScene.tscn`/`GameUI.gd`.

**U1-07: Scenario picker v1** · ux · M · [H] UX "scenario selection", visual "first impression" (picker part); F692
- Files: `ScenarioSelectPanel.gd:22-25, 47-123, 141-148, 191-197, 230, 328`; `godot/scripts/autoload/GameBridge.Depth.cs:44-65`; `MainMenu.gd:224-228`; `scenarios.json` texts (ressursrente says 25 MNOK while the target is 85; "Lakskongen" promises a free choice of region); the mod-scenario fresh-launch path (F692).
- Approach: cards built from `get_available_scenarios()` (all 9 plus mods) in a carousel that fits 1280–1920 px. Capital is formatted from the data. A lock icon appears only on locked cards; none are locked until U2-03. Esc emits `panel_close_requested`.
- Accept:
  1. 9+ cards are reachable.
  2. Displayed capital equals the formatted `starting_balance` for every card.
  3. Card rects lie inside the panel at 1280 and 1920.
  4. A real Esc key returns to a usable menu (test).
  5. A mod scenario starts on a fresh launch (test).
- Deps: U1-02 · Parallel: yes.

**U1-08: No dead verbs** · ux · S · [C] UX "Feed always fails" (Phase 1 part)
- Files: `PenManagement.gd:323, 359-364, 408-413, 520-527`; `Dashboard.gd:136-138`; `SimActions.gd:159-167`; `FarmStateBanner.gd:28-29, 174-181` (suppress the FCR factor below 0.5 kg mean weight or within 30 days of stocking; the call to action deep-links); `AttentionInbox.gd:381-382` (→ `ai_feeding`).
- Approach: remove the Feed / Fôr alle buttons until U2-04. Give every disabled control a tooltip stating why.
- Accept:
  1. A "click every enabled button" UI test on each panel in a fresh `forste_sesong` produces 0 failure toasts and 0 `push_warning`s.
  2. 03 on day 12 shows no "Kritisk tilstand".
  3. Every disabled Button has a non-empty tooltip.
- Deps: U1-05 · Parallel: shares `PenManagement.gd` with U1-05 (sequential).

**U1-09: Live main menu** · ux/graphics · M · [H] visual "first impression"; [M] UX "main menu"
- Files: `godot/scenes/MainMenu.tscn:70-118, 205`; `MainMenu.gd:99-104, 146-161`; new `godot/assets/brand/lakskongen_wordmark.svg`; delete `assets/textures/menu-background.png` (wrong brand, buttons baked in) and replace the RGB `lakskongen-logo.png`; `godot/scripts/AudioController.gd:206-223, 332-341` (menu music in `_ready`).
- Approach: FjordEnvironment at golden hour with a slow camera spline and a wellboat crossing, behind a left-aligned menu column on a vignette.
- Accept:
  1. 01 has a 3D backdrop: luminance std ≥ 0.08, and frames 2 s apart differ.
  2. Text lint finds no emoji glyph in 01.
  3. The music bus is active within 1 s.
  4. The version string no longer contains "Nordic Minimalist".
- Deps: G1-01, G1-02, G1-05, U1-02 · Parallel: yes, owns the MainMenu files.

### Simulation

**S1-01: Merge F1024 (water chemistry)** · sim · M · [H] sim "F1024"; D2
- Files: branch `review/f1024-water-chemistry` (`8123ea1`); `src/LakskongenSim/Biology/WaterQuality.cs:39-44`; `src/LakskongenSim.Tests/Integration/FullCycleEconomicTests.cs:46, 187`; `Integration/PlayerScriptCareerTests.cs:20`; `Integration/PlayerScriptProbe.cs` (harvest rule); `docs/BALANCE.md`; RUN_LOG §7/§8.
- Accept:
  1. On the 600-day po3 probe: 0 clamp days; DO < 70% saturation on ≤ 10% of days and only in summer; a bloom active on ≤ 15% of days; pH 7.8–8.2.
  2. The rewritten tests (D2) pass on the engine path.
  3. Every career band is re-measured and recorded.
  4. The save/load matrix and twin determinism are green.
  5. Algae alerts in a 365-day default run drop from near-permanent to event-driven (count recorded).
- Deps: none · Parallel: sim files only; touches no Godot file.

**S1-02: Renormalize the storm season (F456)** · sim · S · [M] sim "F456"; D3
- Files: `src/LakskongenSim/Environment/WeatherSystem.cs:196-215, 270-291`.
- Accept:
  1. Stationary-distribution test: October–March severe share > 0, and storm+severe share ≥ the off-season's.
  2. Realized frequencies match the `StormRiskNext3Days` forecast within ±2 percentage points over 10 seeds × 3 years.
  3. Storm-dependent bands re-measured, not loosened.
- Deps: S1-01 (both re-measure the bands) · Parallel: sim track.

**S1-03: Honest economy (rewards, bankruptcy floor, rent tax)** · sim · M · [C] UX "end and bankruptcy" (floor part), UX "harvest" (reward cash); [M] sim "rent tax off"; D16
- Files: `src/LakskongenSim/Core/AchievementSystem.cs:71-76`; `Core/TutorialSystem.cs:391-393`; `godot/scripts/autoload/GameBridge.Core.cs:966-978` (floor and reason key); BalancingConfig and `EnableResourceRentTax` (`SimulationEngine.cs:52`); scenario `special_rules` (the floor for `krise_manager`).
- Accept:
  1. Ledger test: no achievement or tutorial line in revenue.
  2. The floor is read from config per scenario; `krise_manager` survives days 1–30 of passive play.
  3. A family farm pays 0 rent tax and a multi-site career pays more than 0.
  4. Ledger reconciliation over 730 days × 3 seeds stays green.
- Deps: S1-02 · Parallel: sim track.

**Phase 1 exit gate:**
- Every Phase 1 entry in `expected_failures.json` is deleted.
- New SSIM goldens are promoted after an art-review checklist, with side-by-side reference photos, signed in `docs/run/evidence/phase1_art_review.md`. New shots: `17_fjord_shore`, `18_fjord_calm_dusk`, `19_menu_live`.
- P0-04 p95 ≤ 16.7 ms at 1440p at FARM, FJORD and COAST on the M1 Pro.
- UI-flow check count raised; .NET green with F1024 merged.
- GitHub CI green.

---

## 6. Phase 2: A Game You Can Finish (≈8 weeks, including the holidays)

**Goal:** the core loop is completable and honest, every verb has a preview and a payoff, and the setting becomes real Norway. **Phase exit:** the Coming Soon page assets (D9) and the first blind playtest (owner schedules: 5 × 2 h).

**Tracks:**
- U: U2-01 · U2-02 · U2-03 · U2-04 · U2-05 · U2-06 · U2-07 · U2-08
- Au: Au2-01
- S: S2-02 → S2-03 → S2-04; S2-01 runs in parallel in the Licence/Commands partials; S2-05 in parallel in MortalityModel
- G-A: G2-01 → G2-02
- G-B: G2-03
- A: A2-01 → G2-04

**U2-01: Expansion flow** · ux · L · [C] UX "expansion impossible"
- Files: `MapPanel.gd:553-558` (`region_selected`); `GameUI.gd` (wire it); `godot/scripts/SimBridge.gd:511-517`; `SimActions.gd:144-156`; `godot/scripts/autoload/GameBridge.Licence.cs`; new `scenes/ui/LicenceAuctionCard.tscn` and `scenes/ui/BuildSiteDialog.tscn`; AttentionInbox auction items; `godot/tests/UiFlowTests.gd`.
- Approach: the region view shows zones with the `can_build_sea_site` verdict and reason. "Søk vekstlisens" opens an auction card with live rival bids, the minimum bid and a countdown. "Bygg lokalitet" shows the cost, the MTB effect and the pen count, then asks for confirmation.
- Accept:
  1. A UI-input-only test plays new `forste_sesong` → map → build a second site, and tutorial step 16 completes.
  2. In a green zone, the test opens an auction and wins it against ≥ 1 rival bid.
  3. Every `can_build` false reason is rendered as text.
- Deps: S2-01 · Parallel: owns the MapPanel files.

**U2-02: Learn-by-doing first season** · ux · L · [C] UX "tutorial broken"; D19
- Files: `src/LakskongenSim/Core/TutorialSystem.cs:185-247, 298-303`; `Core/InDayTickSystems.cs:805`; `Core/ScenarioSystem.cs:720-722`; `SimulationEngine.cs:409`; `godot/data/tutorial_steps.json`; `godot/scripts/autoload/TutorialState.gd:24-35`; `GameUI.gd:42`; `TutorialUI.gd:93, 125, 175`; `godot/scripts/tutorial/TutorialHighlight.gd`.
- Approach: steps advance on real UI or engine events (`tutorial_mark(...)` and real actions). The target control is highlighted. Time slows or pauses while a step waits for input. Gating applies only in `forste_sesong`.
- Accept:
  1. A static test confirms no trigger is a day count or a placeholder.
  2. A seeded, UI-input-only test **wins `forste_sesong`**.
  3. After a skip, the Tutorial win condition is false.
  4. `po3_red_sone`, ISA and algae have every hub available on day 0.
  5. The progress dots equal the step count.
- Deps: U2-01, U2-04, U2-05 · Parallel: owns the tutorial files.

**U2-03: End screens, failure ladder, campaign progress** · ux · M · [C] UX "no end screen"
- Files: new `scenes/ui/ScenarioEndScreen.tscn` (CanvasLayer 50); `GameBridge.Core.cs:856-860`; `ScenarioManager.gd:259-262`; `ConsequenceDirector.gd:267-272`; `EventLog.gd` (wired); `src/LakskongenSim/Finance/CovenantSystem.cs` (breach events reach the UI); a liquidity-ladder engine rule (warning → bank call → rescue-loan offer); `user://campaign.json`; locks and medals in `ScenarioSelectPanel.gd`.
- Approach: the end screen shows the verdict, key statistics, the P&L story timeline and the top 3 turning points, with buttons for continue, load autosave, retry, next scenario and main menu.
- Accept:
  1. UI tests for win, loss and bankruptcy.
  2. After GameEnded, only the end screen accepts input.
  3. A scripted decline gets a liquidity warning ≥ 30 sim days and a bank call ≥ 10 days before bankruptcy.
  4. Unlocks persist across a restart.
  5. Turning-point amounts reconcile to the ledger.
- Deps: S1-03, S2-05 · Parallel: yes.

**U2-04: Feeding plan** · ux+sim · M · [C] UX "Feed verb"; [H] sim "feeding not a decision" (UI part)
- Files: new `SetFeedPlan(siteId, rationPct, recipe, window)` in `SimulationEngine.Commands.cs`; `Farm/FeedSystem.cs:304-313`; `GameBridge.Farm.cs`; `SimActions.gd` (`feed_pen` replaced); `PenManagement.gd` (Fôringsplan card); `Dashboard.gd:136-138`; `AIFeedingPanel.gd:95-97` (recipe merged in); `FarmStateBanner.gd`.
- Approach: a ration slider (restricted / standard / max appetite), recipe presets, and predicted FCR, growth and cost per day from a twin-lite run.
- Accept:
  1. Player-path test: 120 days at 70% ration vs max appetite → lower growth, lower biological FCR and lower cost per day.
  2. The 30-day projection is within ±10% of the realized result.
  3. "Fôr alle" applies to every site (UI test).
  4. Plans survive a save/load round trip.
- Deps: S2-03 · Parallel: shares `PenManagement.gd` with U2-05 (sequential).

**U2-05: Harvest planner and harvest report** · ux · M · [H] UX "harvest payoff"
- Files: `PenManagement.gd:323, 408-413, 520-521`; `ConsequenceDirector.gd:135-155`; new `HarvestPlanner.tscn` and `HarvestReport.tscn`; `WellboatVisualManager.gd` (the boat sails out); AudioController (a stinger replaces the 0.36 s `harvest_ready.wav`).
- Approach: the planner shows a weight histogram, HOG kg, spot vs contract, the wellboat slot and ETA, net revenue, and a projection for waiting N weeks.
- Accept:
  1. Report figures equal the ledger entries.
  2. Same-day projection within ±5% of realized revenue.
  3. "Wait N weeks" uses twin-lite and leaves the live RNG untouched (determinism test).
  4. Exactly one toast per harvest.
- Deps: S2-04 · Parallel: after U2-04 on `PenManagement.gd`.

**U2-06: Typed event bus, one channel per severity** · ux+tech · L · [H] UX "feedback overload"
- Files: new `src/LakskongenSim/Events/GameEventBus.cs` and its bridge signal (GameBridge partial + `SimProxies.gd` + `SimBridge.gd`); `ConsequenceDirector.gd:15-25, 68-89, 316-323`; `JuiceEffects.gd:18-29`; `NotificationCenter.gd`; `ToastNotification.gd`; `Main.gd:99-109`; `FjordCamera.gd` (shake offset).
- Approach: critical = pause + modal card with actions; warning = inbox item + badge + one toast; info = log only. Dedupe by (category, site), with escalation. Add a "Reduser bevegelse" setting.
- Accept:
  1. Grep finds no substring classification of alert text.
  2. Routing test: exactly one modal per critical event and one toast per warning.
  3. A repeated condition creates no duplicates within 3 days.
  4. Shake moves the 3D camera offset, and does nothing when reduced motion is on.
- Deps: U1-04 · Parallel: conflicts with G1-03's `FjordCamera.gd` (adds an offset only).

**U2-07: Pacing (briefing, calendar HUD, speeds)** · ux · M · [M] UX "pacing", art "time controls"
- Files: `GameBridge.Core.cs:93, 179-185`; `MainMenu.gd:197-204`; `HUD.gd:41-43, 83-85, 98-101, 163-167`; `SettingsMenu.gd:121`; `Dashboard.gd:363-373` (throttle and diff instead of rebuilding); new `BriefingCard.tscn`; the `project.godot` input map (D15).
- Accept:
  1. New games start paused on a briefing card.
  2. The HUD shows "Uke 14 · april · År 1".
  3. Speeds 1/3/10/30 and "next event" work, with auto-slow and auto-pause rules (tests).
  4. At 30×, p95 ≤ 16.7 ms and the dashboard rebuilds at most once per sim day.
- Deps: U1-04 · Parallel: owns HUD.

**U2-08: Settings that apply, and richer save slots** · ux · M · [M] UX "settings cosmetic"; D21
- Files: `SettingsMenu.gd:14, 112-121, 150-151`; `SaveLoadManager.cs:711-717`; `SaveLoadMenu.gd:122-160`; a high-contrast theme variant; the quality-preset hook in EnvironmentDirector; a language picker; `LocalizationManager.gd:9` (supported locales trimmed to en and no).
- Accept:
  1. A consumer test: every persisted setting has an observable effect (window mode and size, vsync, preset → environment flags, autosave interval, default speed, high contrast, language, reduced motion).
  2. Save slots show the scenario, an ISO timestamp and a thumbnail.
- Deps: U1-06 · Parallel: yes.

**Au2-01: Audio floor** · audio · M · [M] UX "audio thin"
- Files: `AudioController.gd:206-223, 276-317, 332-341`; `FinancePanel.gd:131` (UI sounds attached globally through a `node_added` hook); `WellboatVisualManager.gd`; `FeedBargeVisualManager.gd`; `godot/assets/audio/` (60–120 s CC0 loops; `boat_engine.ogg` and `feed_barge.ogg` supplied); `AUDIO_SOURCES.md`.
- Accept:
  1. A CI audio check: no ambience loop shorter than 30 s, no missing file, every file's licence recorded.
  2. A UI test: every Button press plays the click bus.
  3. Verb SFX fire on event-bus events: stock, feed plan, delouse, harvest, auction, storm.
- Deps: U2-06 for the verb hooks · Parallel: yes.

**S2-01: Capacity v1 (licences, MTB, real pens)** · sim · XL, split into a (L) and b (M) · [C] sim "capacity almost free"
- Files:
  - `src/LakskongenSim/Core/SimulationEngine.Licence.cs:142-172`; `SimulationEngine.Commands.cs:97-170`; `Farm/SeaSite.cs:19`; `InDayTickSystems.cs:133`; `SimulationEngine.cs:159` (`PenVolumeProxyM3`), `:2846`; `src/LakskongenSim/Calibration/CalibrationRegistry.cs:228`.
  - New `Farm/Pen.cs`, `Finance/LicenceHolding.cs` and a new partial `SimulationEngine.Capacity.cs`; `godot/data/scenarios.json`; `PlayerScriptProbe.cs`.
- Approach:
  - **a:** licences are 780 t (945 t in Troms and Finnmark); company MTB is pooled per PO; each site has its own MTB cap; building a site grants 0 t. Daily enforcement brings a fine, confiscation of the gain and a forced-harvest order. Stocking is checked against the projected peak.
  - **b:** sites have 6–12 pens; each pen holds at most 200,000 fish at ≤ 25 kg/m³. The stocking UI picks a pen and a count.
- Accept:
  1. The overstock probe strategy is refused or fined and ends below the compliant strategy's equity.
  2. Daily enforcement test.
  3. A build adds 0 t of MTB.
  4. Stocking rejects more than 200k fish per pen and a projected density above 25 kg/m³.
  5. Save/load and twin determinism green.
  6. `forste_sesong` still wins on ≥ 4/6 seeds, re-measured; if it does not, tune the scenario data, not clamps.
- Deps: none · Parallel: owns the Licence/Commands/Capacity partials; CalibrationRegistry additions go in their own region.

**S2-02: PO regions, zone temperature, one calendar, offline data** · sim · L · [H] sim "POs identical"; [L] "calendar governance"; D18
- Files: `Environment/WeatherSystem.cs:24-38, 115-120`; `InDayTickSystems.cs:98`; `SimulationEngine.cs:765-771, 780-819, 2516-2521, 2559-2560, 2583`; `ScenarioSystem.cs:316`; `Environment/Zone.cs:183, 239-241`; `Data/Enums.cs:272-282`; `Market/MarketSimulation.cs:420-424`; `BarentsWatchFishHealthIntegration.cs:142-147`; new `Core/SimCalendar.cs`; zone ids in `scenarios.json` mapped to POs.
- Approach: the 13 production areas get sea-temperature climatologies plus a shared anomaly. Zone temperature drives growth, lice, disease, O2 and treatments. PO rules: 945 t in the north, the PD zone, and regional spring windows (ISO weeks 16–21 south, 21–26 north).
- Accept:
  1. Grep finds no `% 360` and no 30-day months; one calendar helper serves market, weather, lice and finance.
  2. `Initialize` performs no network I/O even with the token set (test).
  3. The pre-registered PO4-vs-PO13 test (D2) passes after S2-03.
  4. Spring-window test per region.
- Deps: S1-01 · Parallel: sim track (sequenced before S2-03).

**S2-03: TGC growth, appetite, ration policy** · sim · L · [H] sim "growth not TGC"
- Files: `Biology/GrowthModel.cs:43-53, 176-182`; `SimulationEngine.cs:156` (asymptotic weight K); `Farm/FeedSystem.cs:304-313`; FishPopulation (weight CV per cohort).
- Accept, over 10 seeds:
  1. PO4 April S1 at 100 g reaches 4.5–5.5 kg in 14–18 months.
  2. Economic FCR 1.2–1.35.
  3. Relative growth at 4–5 °C ≥ 0.3 of the optimum.
  4. Fish can exceed 6 kg when grown on.
  5. Per-cohort weight CV reported.
- Deps: S2-02 · Parallel: sim track.

**S2-04: Harvest pipeline** · sim · L · [H] sim "harvest sells live kg"; F270
- Files: `SimulationEngine.Commands.cs:182-306`; `SimulationEngine.cs:1268-1269`; `Market/MarketSimulation.cs:667-675` (`SellFish`); `Market/QualityGrade.cs:179`; `SimActions.gd:171-182`; `docs/BALANCE.md:199-221`; the wellboat slot in Logistics.
- Approach: pre-harvest starvation, a wellboat slot, slaughterhouse capacity, HOG yield 0.84, harvest cost booked as COGS, a size-class price ladder, quality grades, and partial or per-pen harvest.
- Accept:
  1. Ledger: revenue = HOG kg × size-class price × grade factor − harvest cost.
  2. A 2 kg fish earns ≥ 25% less per kg than a 5 kg fish.
  3. F270 closed.
  4. The EBIT/kg band test runs on the PlayerScriptProbe path.
  5. Partial-harvest test.
- Deps: S2-03 · Parallel: sim track.

**S2-05: Mortality ledger by cause** · sim · M · [H] sim "mortality without causes"; D4
- Files: `Biology/MortalityModel.cs:44-110, 183`; `SimulationEngine.cs:2095-2120` (temperature-stress constant vs its documentation); FishPopulation; the P&L story bridge; probe output.
- Accept:
  1. Σ deaths by cause = total deaths, exactly, per cohort per day.
  2. The probe prints a breakdown in NVI categories.
  3. The seed-13 breakdown is recorded as input to S4-03.
  4. The temperature-stress constant matches its documentation (test).
- Deps: none · Parallel: owns MortalityModel.

**G2-01: Terrain v2: real fjords with seabed, LOD, collision and far field** · graphics · XL · [H] tech3d "terrain resolution/LOD", render "terrain mesh"; [M] tech3d "world ends at ±2 km"; D11
- Files: new `godot/assets/terrain/<region>/` (16-bit heightmap, bathymetry, splat map, `meta.json`, `LICENCE`); new `tools/terrain_bake.py` and `blender/terrain_<region>.py` (run through the Blender MCP); `FjordTerrain.gd` (16×16 chunk LOD or a GPU clipmap; `HeightMapShape3D`); `TerrainGeneratorBridge.cs` and `TerrainGenerator.cs` (load the bake for headless placement); `FishPenManager.gd:150-178` (site slots from the bake); `FjordCamera.gd:38-41` (bounds); a far-field ring (40 km ocean skirt plus a mountain silhouette 6–15 km out).
- Accept:
  1. A licence file per tile and a credits entry.
  2. Site positions within 50 m of the Fiskeridirektoratet locality coordinates used.
  3. Edge-of-world mask = 0 at COAST.
  4. LOD error ≤ 2 px on screen.
  5. Headless and windowed runs load the same heightfield hash.
  6. p95 ≤ 16.7 ms at COAST.
  7. The procedural G1-01 generator stays as the fallback for regions without a bake.
- Deps: G1-01, G1-05 · Parallel: owns the terrain files; `FishPenManager.gd` is serialized with G2-04.

**G2-02: Terrain material and vegetation** · graphics/assets · L · [H] tech3d "terrain shading", render "terrain material"; [C] art "terrain" (the look itself)
- Files: `godot/shaders/terrain_shader.gdshader:43-47, 121-124, 136-184`; `FjordTerrain.gd:324, 354`; textures from Poly Haven CC0 at 2K in a `Texture2DArray`, with `.import` `normal_map=1`; new `scenes/environment/Vegetation.tscn` (MultiMesh birch and pine with octahedral impostors beyond 600 m); props built in Blender (naust boathouses, a ferry quay, a lighthouse).
- Approach: biplanar or triplanar weighted sampling (iq or Golus) with whiteout normals; two sample scales plus macro variation; a wet band 0–1.5 m above the waterline; snow by slope and altitude; treeline 450–550 m.
- Accept:
  1. Static check: no UV lerp; normals use `hint_normal`; ≤ 2 sampler arrays.
  2. The 24 m tiling autocorrelation peak in 08 is below threshold.
  3. The wet band is ≥ 20% darker.
  4. Trees appear only inside the slope and treeline mask.
  5. Art checklist against the UNESCO West Norwegian Fjords references.
  6. Performance holds.
- Deps: G2-01 · Parallel: owns the terrain shader.

**G2-03: Water v2: planar reflection, foam, wakes, bobbing** · graphics · L · [H] render "no fjord mirror", visual "water", tech3d "water" (rest)
- Files: water shader includes; new `godot/scripts/environment/PlanarReflection.gd` (half-resolution SubViewport, mirrored camera, a global `lk_reflection_pass` uniform that makes terrain, pen and boat shaders discard below y = 0); `godot/scripts/environment/PenCage.gd` (heave); `WellboatVisualManager.gd` and `FeedBargeVisualManager.gd` (bob and wakes); a wake trail render target; the wave function mirrored in GDScript.
- Accept:
  1. In `18_fjord_calm_dusk`, the reflection band has SSIM ≥ 0.5 with the vertically flipped skyline.
  2. Reflection updates at 30 Hz at COAST; the Low tier falls back to the probe.
  3. |object y − wave(x,z)| ≤ 0.1 m for pens and boats.
  4. Foam present where the shore SDF is < 5 m.
  5. p95 ≤ 16.7 ms.
- Deps: G1-05 · Parallel: owns the water files.

**A2-01: Hero pen and site kit (Blender MCP)** · assets · L · [H] visual "placeholders", tech3d "pens/vessels", art "pen model"; [M] art "site infrastructure"
- Files: new `blender/build_pen.py` and `blender/build_site_kit.py`; `blender/asset_manifest.json` (dimensions, triangle budgets, licences); `godot/assets/models/pen_cage/*` (glTF LOD0–2 with baked AO and normal maps replace the primitive `.tres` files); `pen_cage.tscn`; `PenCage.gd`; `pen_net_shader.gdshader` (texture-based net).
- Spec (AKVA Polarcirkel 500-class):
  - Collar: twin HDPE pipes Ø450–500 mm at 1.1 m centres; 160 m circumference; brackets every 2.5–2.7 m (MultiMesh); Ø160 mm stanchions about 1.0 m tall with a Ø140 mm handrail; 1.2 m walkway.
  - Net: bird-net poles 6–8 m; sinker tube; 20–40 m deep with a cone bottom.
  - Site: mooring frame of 70–100 m cells; grey or orange buoys; synchronised yellow corner lights (#FFC23A); hoses; a lice-skirt variant 6–15 m deep in #C9CCC6.
- Accept:
  1. Each asset's bounding box is within 5% of spec.
  2. LOD0 ≤ 60k triangles per pen; LOD2 ≤ 2k.
  3. Side-by-side reference evidence doc.
  4. 08b pHash Δ ≥ 16.
  5. Draw calls per site within the P0-04 budget.
- Deps: G1-07 · Parallel: yes (Blender plus asset files); the scene swap is serialized on `FishPenManager.gd` with G2-04.

**G2-04: Multi-pen sites in a mooring grid** · graphics · M · [H] visual "one ring per site"
- Files: `FishPenManager.gd:150-196`; `CompetitorFarmVisualManager.gd`; `GameBridge.Farm.cs` (pens per site from S2-01b); `FeedBargeVisualManager.gd`.
- Accept:
  1. The rendered pen count equals the engine's pen count for every scenario site.
  2. Grid spacing per spec.
  3. One barge per site.
  4. Rival farms use the same assets with a colour band (grep finds no material override).
- Deps: A2-01, S2-01b, G2-01 · Parallel: owns `FishPenManager.gd` in Phase 2.

**Phase 2 exit gate:**
- The UI-input-only `forste_sesong` win test passes.
- The "click every button" test has 0 failures across all panels.
- Every Phase 2 ledger entry is deleted.
- New goldens for terrain v2, pens and water v2, with art review.
- `.NET` sim band tests pass for TGC, harvest, capacity and POs.
- **Owner-scheduled blind playtest** (5 × 2 h), recorded against D1's "understand money" ≥ 3/5.
- Store-candidate frames exported for the Coming Soon page (D9).

---

## 7. Phase 3: The Fjord Is the Game (≈6 weeks)

**Goal:** the player plays on the fjord. Farms look and behave like Norwegian farms, sim state is readable in 3D, and the market and traffic light are real. Exit: UX benchmark rows 1, 2, 7, 9 and 10 at MATCH (art audit checklist), with evidence in `docs/run/evidence/benchmark.md`.

**U3-01: World-first information architecture** · ux · XL · [H] visual "IA hides world", UX "fjord is wallpaper", art "world not home"; [M] UX "19 tabs"; D15
- Files: `GameUI.gd:88, 119-143, 167, 179, 384-393`; `GameUI.tscn:44`; `FjordViewPanel.gd`; `FjordVistaBridge.gd:54-60`; `HUD.gd`; `Dashboard.gd` (becomes a KPI strip plus an inbox drawer); every panel scene (into a dock container); `UiFlowTests.gd` (route-based); harness shots per hub.
- Approach: six hubs on a left rail, docked Ink panels ≤ 40% wide, modals over a dimmed world, and in-world anchored cards for pen, site, barge and boat with 3–4 verbs plus "Detaljer". Full-screen views only for Analyse and Eksport. A "?" overlay lists the controls.
- Accept:
  1. Exactly 6 top-level destinations.
  2. The world is unoccluded over ≥ 55% of the frame in every hub shot.
  3. Clicking a pen opens its anchored card, and the card's verbs execute (UI test).
  4. UI-flow checks migrated, with the count ≥ before.
  5. GPU time with a full-screen view open is ≥ 40% lower.
- Deps: U2-* merged · Parallel: no. It touches every panel, so it runs alone in the UI track; graphics and sim tracks continue.

**U3-02: Icon system; emoji removed** · ux · M · [M] visual "emoji", art "UI identity" (icons); D14
- Files: `default_theme.gd:196-203` (emoji SystemFont fallback removed); new `godot/assets/icons/` (Phosphor plus about 20 custom glyphs: pen, net, wellboat, barge, smolt, louse, delousing, harvest, MTB, traffic light, Mattilsynet, skirt); new autoload `IconRegistry.gd`; the 731 emoji across 86 files (including `scenarios.json` and `ToastNotification.gd:33-38`); an emoji lint in `tools/ci.sh`.
- Accept:
  1. Emoji lint finds 0 in `.gd`, `.tscn`, `.json` and `.csv`.
  2. Status uses shape as well as colour; the CVD simulation in `a11y_audit.py` passes.
  3. Icons tint through `modulate`.
- Deps: U3-01 · Parallel: after U3-01 (same files).

**U3-03: Panel component kit and rebuilds** · ux · L · [M] visual "panel defects"
- Files: new `godot/scripts/ui/components/{PanelShell,EmptyState,KeyValueRow,ActionBar,ChartKit}.gd`; pen panel (07f: hero stats, actions, tabs); offshore (07d); carbon (07e); `MapPanel.gd` cartography (Norway's land only, neighbours greyed, PO polygons shaded by traffic light, label collision avoidance); `AttentionInbox.gd` (duplicate refresh); `SiteOverlayLegend.gd`.
- Accept:
  1. No horizontal scrollbar at 1280×720.
  2. EmptyState wherever data is empty (test).
  3. 0 map label collisions.
  4. Grep finds no ad-hoc ColorRect bars outside ChartKit.
- Deps: U3-01, S3-02 (map shading) · Parallel: one panel per PR after U3-01.

**U3-04: Nested tooltips and objective help** · ux · M · [M] art "tooltips/onboarding" (benchmark row 3)
- Files: new `components/NestedTooltip.gd`; HUD; the KPI strip; the goal chip; Leksikon glossary data.
- Accept:
  1. Every KPI node has a tooltip naming this week's drivers (an enumeration test).
  2. Tooltips nest 3 levels deep and can be locked.
  3. "Vis meg hvordan" highlights its target (UI test).
- Deps: U3-01 · Parallel: yes.

**A3-01: Vessels (feed barge, wellboat, workboat, RIB)** · assets · L · [H] art "barges and wellboats", tech3d "vessels"
- Files: new `blender/build_vessels.py`; new `godot/assets/models/{feed_barge,wellboat,workboat,rib}/`; `WellboatVisualManager.gd:14-19`; `FeedBargeVisualManager.gd:9-11`; `service_boat.tscn` retired.
- Accept:
  1. Dimension tests: barge 37×11 m and 64.8×13 m; wellboat 77×17.8 m and 116×23 m; workboat 15×10 m.
  2. Job state appears as a UI chip, never as hull colour (grep).
  3. The wellboat is visibly longer than a pen's diameter.
  4. Triangle budgets met.
- Deps: none · Parallel: yes.

**G3-01: Salmon schools on the GPU** · graphics/assets · M · [H] tech3d "fish carousel"; [M] render "fish"
- Files: `FishPenManager.gd:22-24, 112-118, 424-475` (fish code moves to a new `FishSchool.gd`); `godot/shaders/fish_shader.gdshader:4-14`; `salmon_*_material.tres`; `blender/build_salmon.py` (UVs, LOD0 about 1.5k triangles, LOD1 about 300).
- Approach: per-fish data written into MultiMesh custom data once; the swim cycle animated in the vertex shader; the whole school rotated per pen; countershaded silver skin; depth, speed and feeding-rise uniforms come from the sim.
- Accept:
  1. 0 per-frame `set_instance_transform` calls; fish cost < 0.3 ms at 12 pens.
  2. 1,500–4,000 fish per pen within 400 m, scaled by biomass.
  3. Fish-mask fraction ≥ 2% in 08b.
  4. Feeding raises the mean-depth uniform; low DO lowers speed.
- Deps: G2-04 · Parallel: after G2-04 on `FishPenManager.gd` (the extraction removes the conflict afterwards).

**G3-02: One sim-to-3D presenter, event visuals, working particles** · graphics · L · [H] tech3d "F746/no 3D consequences", "particles draw nothing"; [M] art "in-world feedback"
- Files: `FjordVistaBridge.gd` → `FjordPresenter.gd`; `FjordEnvironment.gd:42-45`; `ParticleEffects.gd:39-104`; `FjordEnvironment.tscn:162-185`; `WeatherController.gd:133, 139`; a new bridge `get_fjord_visual_state()` and a `visual_event` signal (GameBridge partial + SimProxies + SimBridge, covered by the contract test).
- Accept:
  1. A visual-event contract test: every event type (lice, harvest, escape, bloom, storm damage, mortality, feeding, stocking, delousing) reaches a presenter method.
  2. Every GPUParticles3D has `draw_pass_1` (static check).
  3. In 10, no rain streak is wider than 4 px at 1080p.
  4. Feeding particles cover ≥ 0.5% of the frame at FARM.
  5. Particle systems do not restart during weather blends (`amount_ratio`).
- Deps: U2-06 (event bus) · Parallel: owns the presenter and particles.

**G3-03: Lenses and a per-site sim field** · graphics/ux · L · [H] art "no lenses"; [M] visual "3D readouts", tech3d "global sim state"
- Files: water shader (a `sim_field` sampler replaces the global uniforms at `water_shader.gdshader:63-67`); new `godot/scripts/environment/SimFieldTexture.gd`; a lens-bar UI; `FishPenManager.gd:224-254` (beacons, fixed-size markers at COAST); `PenLODManager.gd` deleted in favour of visibility ranges.
- Accept:
  1. A bloom at one site shifts water colour by ΔE ≥ 10 within 500 m and ≤ 2 at 2 km (13b vs 13).
  2. Lens values equal panel values (test).
  3. 12 shows the zone border and the traffic-light colour.
  4. COAST shows fixed-size status markers.
- Deps: U3-01, S3-02, G3-01 · Parallel: shares the water shader with G2-03 (sequential).

**G3-04: A real underwater view** · graphics · M · [H] visual "underwater never underwater", render "underwater"
- Files: `FjordCamera.gd:20, 243`; the water-shader underside (Snell's window, total internal reflection); `FjordEnvironment.gd:117-129`; `underwater_post.gdshader` (replaced); `FjordEnvironment.tscn:150-160, 302-313`; a FogVolume below y = 0; a caustics Decal (`water_caustics.png`); marine-snow particles; a net-fouling uniform.
- Accept:
  1. In 11, the camera sits 3–8 m below the surface inside the pen.
  2. The surface underside renders, with a TIR tint outside the 48.6° window.
  3. Fog distance follows sim visibility (bloom visibility < clear).
  4. Caustics animate on the net (temporal variance).
  5. Fish are visible.
- Deps: G3-01 · Parallel: yes, apart from the shared shader include.

**G3-05: Weather drama and the Nordic night** · graphics · M · [H] visual "storm/aurora never read"; [M] art "lighting moods", render "cloud shadows"
- Files: EnvironmentDirector mood profiles (Sommerdag, Midnattssol, Gråvær, Storm, Blåtime/mørketid, Nordlys, Frostrøyk, Høst, with the art audit's hexes); `WeatherController.gd:126, 156`; pen lights (OmniLight at −5 m with volumetric energy); barge mast lights; a global cloud-shadow uniform; camera sway in storms.
- Accept:
  1. Eight mood goldens are pairwise distinct (pHash Δ ≥ 9), with sky and water mean hue within ΔE 10 of spec.
  2. The night shot shows emissive pen lights covering ≥ 0.3% of the frame.
  3. Cloud shadows move (temporal variance on land).
  4. Storm Hs ≥ 2 m at exposed sites.
- Deps: G1-04, A2-01 · Parallel: yes.

**S3-01: Market model** · sim · L · [H] sim "price clamped"; D16
- Files: `Market/MarketSimulation.cs:333-345, 396-424`; `CalibrationRegistry.cs:499-503`; `Market/MarketIntelligence.cs`; CompetitorAI harvest output (a supply index); the ContractsPanel regional section (bound to the model); `docs/REALISM_CHECKLIST.md`.
- Approach: an Ornstein–Uhlenbeck process on log price, with weekly seasonality fitted to SISALMONI/NSC 2015–2026 data and a supply index built from rivals, the player and other origins.
- Accept (distribution tests, 50 seeds × 10 years):
  1. Annual mean 60–110 NOK/kg.
  2. Intra-year amplitude ≥ 15 NOK.
  3. The trough falls in ISO weeks 26–42 in ≥ 60% of years.
  4. Only the 30–200 guards remain (grep).
  5. A red-light cut or a disease wave raises the price (directional test).
- Deps: S2-02, S2-04 · Parallel: owns the Market files.

**S3-02: Traffic light rework** · sim · L · [H] sim "traffic light has no effect"; F358
- Files: `Finance/TrafficLightSystem.cs:119-125`; `SimulationEngine.cs:2781-2826`; `Zone.cs:239-241`; `ScenarioSystem.cs:1084`; `Events/NorwegianEvents.cs:552-590` (cards deleted); new `godot/data/traffic_light_2026.json` (real 2026 colours: PO1/12/13 green, PO3 red, the rest yellow; verify against regjeringen.no); CompetitorAI lice output per PO.
- Approach: a June reassessment every two years, based on larval output across the whole PO (player plus rivals) in weeks 16–26. Red means −6% MTB for every licence holder; green brings an offer of growth capacity; low-lice operators can be exempt.
- Accept:
  1. No weekly flips (test).
  2. Player-path test: a red PO forces a lower stocking plan.
  3. Green-offer flow and exemption tests.
  4. Grep finds the cards gone.
  5. Repeated flips do not ratchet capacity (F358).
  6. The `po3_red_sone` "−6% MTB" promise holds.
- Deps: S2-01, S2-02 · Parallel: sim track.

**Phase 3 exit gate:** the benchmark rows above at MATCH; emoji 0; event-visual contract green; goldens re-promoted; performance gate green with 12 sites of fish and vessels.

---

## 8. Phase 4: Elite Simulation and Decisions (≈8 weeks)

**Goal:** decisions with real-world consequences. Exit: realism checklist filled and validated; the strategy sweep passes (D10); seed-13 restored (D4); benchmark rows 3–6 at MATCH; second blind playtest.

| ID | Title | Area · Effort | Files (key) | Approach (short) | Accept (measurable) | Deps / parallel |
|---|---|---|---|---|---|---|
| S4-01 | Capacity market v2, distressed assets, CrisisPanel [C part b] | sim+ux · L | `SimulationEngine.Licence.cs`, `LicenseAuctionSystem`, `CrisisCapitalismSystem` (F271), `CrisisPanel.gd`, CompetitorAI | Green-growth offers; buying capacity from rivals; distressed listings transfer licences and MTB | Distressed purchase transfers MTB (test); F271 closed; auction prices emerge at 150–350k NOK/t (distribution); CrisisPanel UI test | S3-02 · sim track |
| S4-02 | Diseases by pathogen and the ISA state machine [H] (D5) | sim · XL | `Health/DiseaseSystem.cs:16-17, 73-140, 175-180`, `IntegratedDiseaseSystem.cs:91-100`, `NorwegianEvents.cs:63-185`, `VaccinationProgram.cs:9-16`, new `Health/Pathogens/*` | Per-site SEIR with temperature dependence: ISA, PD (SAV2/3, PD zone), CMS, AGD, winter ulcer, IPN. Spread by seaway kernel and wellboat contacts, rivals included. Vaccines bought with the smolts | Start prevalence ≈ 0; infection share of deaths 28–33%; delousing leaves viral states unchanged; ISA: cull order fires, fallow ≥ 90 d, control zone blocks stocking; `isa_utbrudd`: neighbour infection probability lower with mitigation than without (≥ 200 seeds) | S2-02, S2-05 · owns Health |
| S4-03 | Mechanistic mortality; seed-13 restored [H] (D4) | sim · L | `MortalityModel.cs`, `FishWelfare.cs` (per site), `PlayerScriptCareerTests.cs:48-62` | Hazards per cause; the top-down anchor removed | Median cycle mortality 14–16% (grep: anchor gone); post-transfer deaths front-loaded; seeds 7 and 13 back at −1M with ≤ 35% cycle mortality | S4-02 |
| S4-04 | Lice regulation details [M] | sim · M | `SmoltStrain.cs:26, 46-78`, `Commands.cs:154`, `SimulationEngine.cs:2533-2583`, F299 | Lice-free smolts; counting below 4 °C; rival farms shed lice; coordinated area treatment; fallow clears lice | Each behaviour has a test; coordinated treatment beats uncoordinated on the player path | S2-02 |
| S4-05 | Fallow, single year class, MOM-B [M] | sim · M | `CalibrationRegistry.cs:355`, `SimulationEngine.cs:1308`, `Commands.cs:97-170`, `FeedSystem.cs` (seabed load), `SustainabilityManager.cs` (F467) | One year class per site; enforced fallow; seabed condition | Mixed year classes rejected; early restock rejected; MOM-B 4 blocks stocking; recovers during fallow; F467 bonuses apply | S2-01 |
| S4-06 | Delousing logistics [L, high gameplay value] | sim · M | `Commands.cs:489-600`, `DelousingSystem.cs:36-100`, WellboatJob | Shared regional fleet including rivals; weather gate; withdrawal periods; fasting days | No wellboat operations in Storm/Severe; a seeded high-lice year produces August–October queue delays; withdrawal blocks harvest | S2-04 |
| S4-07 | Storyteller cards become state perturbations [M] | sim · M | `NorwegianEvents.cs` (52 cards), `Storyteller.cs:43, 112-117` | Market news moves supply; disease news seeds exposure; duplicate regulatory cards deleted; windfalls become conditional offers | One player-path test per card shows its stated effect | S3-01, S4-02 |
| S4-08 | Choice events and the weekly rhythm [H] UX "no choices", UX core-loop proposal | sim+ux · XL | `Events/EventDefinition.cs:9-30` (Options[]), a new eager `decisions` RNG stream, bridge `GetPendingDecisions`/`ResolveDecision`, `DecisionPanel.gd:135`, `MorningBriefing.gd` (weekly), NextActionWidget | Options carry costs, deterministic effects and delayed consequences; each option shows a twin-lite projection; time pauses while a decision is open | ≥ 25 authored events across the seasonal arc; twin and save/load determinism with a decision open; projection within tolerance; UI test resolves a decision; the briefing is weekly | S4-07 |
| S4-09 | Tax completeness [M] | sim · S | `SimulationEngine.Tax.cs:57-63` | A production fee per kg credited against rent tax (verify the rate in Prop. 1 LS); a rule-set switch between council price and actual sales price | Tests for both rule sets | — |
| S4-10 | Calibration governance [L] | sim · S | `docs/REALISM_CHECKLIST.md:25-30`, CalibrationRegistry | Each row gets a value, a reference and a source (2025 mortality 14.2%, 2026 colours, auction about 305k NOK/t, 200k fish per pen, 25 kg/m³, TGC 2.8–3.2) | A `Validate()` test per row | parallel |
| S4-11 | Post-smolt verbs (F383) and orphaned panels (D6) | sim+ux · M | bridge post-smolt stubs, panel scenes per D6 | Real post-smolt verbs; each orphaned panel wired or deleted per D6 | The contract test's orphan list shrinks to the deferred set; no wired panel uses invented data (grep) | S2-04 |
| Q4-01 | No-dominant-strategy sweep (D10) | qa · M | `PlayerScriptProbe.cs` (≥ 12 strategy families × 20 seeds × 5 years) | Run the pre-registered criterion | Passes the D10 criterion; report in evidence | after S4-01…06 |
| Q4-02 | Open lows sweep | tech · M | RECHECK §2 lows not covered elsewhere: AcceptContractOffer index, SuperSmolt, export-all (F600), the ScenarioCampaign ledger assertion, the profitable-quarter test, the fake exporter, the delete label, ApplyScenario, AttemptAcquisitionTarget units, RealData lows, F023, F672, F1013, and the UI-flow runner's error grep | Fix each with a test | RECHECK open lows = 0, or deferred with a stated reason | parallel |

---

## 9. Phase 5: Signature and Ship (≈4 weeks, then the June Next Fest)

| ID | Title | Area · Effort | Files / approach | Accept |
|---|---|---|---|---|
| G5-01 | Quality tiers and performance [M] tech3d "lighting/GI", render "tiering" | graphics · M | EnvironmentDirector presets Low/Med/High/Ultra: SSAO/SSIL per zoom, volumetric fog, shadow atlas 4096/8192, soft-shadow quality, MSAA 4x + SMAA, optional TAA, MetalFX/FSR2 scaling (check 4.7 option names), SDFGI only on non-macOS Ultra; fish and vegetation density per tier | p95 ≤ 16.7 ms at 1440p on High at every zoom with 12 sites (M1 Pro); Steam Deck p95 ≤ 33 ms at 1280×800 on Low/Med (D3) |
| G5-02 | Closed and semi-closed pens, late-game facilities | assets · M | Blender: FishGLOBE, egg pen, Havfarm-class and Ocean Farm 1-class hulls, bound to RAS and offshore facilities | Each facility type renders its own asset (test); dimensions within 5% of spec |
| U5-01 | Accessibility and Steam Deck [M] UX "accessibility" | ux · L | Joypad events for every action; analog camera; bumpers rotate; radial pen verbs; keybinding page; colour-blind palettes; minimum text size at 1280×800 | A controller-only UI test wins `forste_sesong`; `a11y_audit.py` passes; Valve Deck legibility checklist met |
| U5-02 | Localization complete [H] UX "mixed language" (engine part) | ux+sim · L | The engine emits `{key, args}` for every player-facing string; EN complete | Text lint clean in en and nb across the full capture matrix; 0 missing keys |
| Au5-01 | Audio identity (D7) | audio · M | Seasonal music plus crisis stems with crossfades; mix buses; ducking | D7 checklist; music integrated loudness −16 LUFS ± 1; peaks ≤ −1 dBTP |
| Q5-01 | Save fuzz and soak (D4, D5) | qa · M | 10k randomized save/load/continue cycles; 2 h soak | 0 divergences; 0 crashes; memory within ±5% |
| Q5-02 | Release engineering (W4) | tech · M | Exports for three OSes as CI artefacts; version stamping; macOS signing and notarization (owner credentials gate); depot script | W4 criteria |
| P5-01 | Demo and store (D2, D8; D9, D23, D25) | qa+ux · L | Demo of `forste_sesong` + `po3_red_sone` (separate App ID); a first-15-minutes script; capsule art and trailer captured in-game; EN and NB store page; AI disclosure; June registration | Human gates: Steam account, price, legal, playtests. The demo passes every Phase 1–4 gate |

---

## 10. File-conflict map and merge order

| Hot file | Items | Rule |
|---|---|---|
| `godot/scenes/environment/FjordEnvironment.tscn` | P0-05, G1-02, G1-05, G1-06, G3-02, G3-04 | Split in P0-06; afterwards each item owns its sub-resource |
| `godot/shaders/water_shader.gdshader` | G1-05, G2-03, G3-03, G3-04 | Split into includes (P0-06); serial order G1-05 → G2-03 → G3-03/G3-04 |
| `godot/scripts/environment/FjordCamera.gd` | G1-03, G1-06, G2-01 (bounds), G2-03, G3-04, U2-06 (offset) | G1-03 first; one camera owner per phase |
| `TimeOfDay.gd` / `WeatherController.gd` / `FjordEnvironment.gd` | G1-02, G1-04, G3-02, G3-05 | Owned by EnvironmentDirector after G1-02 |
| `godot/scripts/environment/FishPenManager.gd` | G1-07, G2-01, G2-04, A2-01, G3-01, G3-03 | Serial; G3-01 extracts `FishSchool.gd` |
| `godot/scripts/environment/FjordTerrain.gd`, terrain shader | G1-01, G2-01, G2-02 | One graphics track |
| `godot/scenes/GameScene.tscn`, `GameUI.gd` | G1-03 (2 lines), U1-04, U1-06, U2-03, U2-07, U3-01 | Serial; U3-01 runs alone |
| `godot/scripts/ui/Dashboard.gd` | U1-03, U1-04, U1-05, U1-08, U2-07, U3-01 | Serial in the U-B track |
| `godot/theme/default_theme.gd`, `tokens.json` | U1-01 → U1-02 → U2-08 → U3-02 | Serial |
| `translations.csv`, `scenarios.json` | U1-03, U1-07, S2-01, S2-02 + every UI item | Keys sorted; conflicts resolved mechanically by the merge captain |
| `ScreenshotHarness.gd` | every item that adds a shot | Shots live in `godot/qa/shots.json` (P0-03), so items only append data |
| `src/LakskongenSim/Core/SimulationEngine.cs` (4,046 lines), `CalibrationRegistry.cs` | every S item | New logic goes in new partials (`.Capacity.cs`, `.Harvest.cs`, `SimCalendar.cs`…); calibration constants go in per-item regions; one sim track at a time per file |
| GameBridge partials, `SimProxies.gd`, `SimBridge.gd` | U2-01, U2-04, U2-06, G3-02, S4-08 | One new partial per feature; the bridge contract test guards drift |
| `godot/tests/UiFlowTests.gd` (`MIN_CHECKS`) | most UI items | Append functions; the merge captain raises `MIN_CHECKS` |

---

## 11. Risks

| # | Risk | Likelihood / impact | Mitigation |
|---|---|---|---|
| R1 | Scope against the June 2027 Next Fest: the plan totals about 30 weeks of track time | High / high | Phases are gated ship points; Phase 4 items can slip after the demo (the demo needs P1–P3 plus S3-02); the Coming Soon page at the Phase 2 exit protects wishlists |
| R2 | The realism overhaul (capacity, TGC, HOG, rent tax, pathogens) breaks careers and bands, and someone reaches for a clamp | High / high | One sim change per PR; distribution tests over seeds; re-measure, never loosen; tune scenario data, not mechanics; D10 sweep |
| R3 | Save compatibility: new state (pens, licences, pathogens, decisions) invalidates old saves | Certain / medium | Bump the save version every phase; pre-1.0 saves explicitly unsupported; keep the save matrix and cross-process twins as gates |
| R4 | Godot 4.7.2 on Metal: volumetric fog, a SubViewport planar reflection and SMAA may cost more than the M1 Pro budget or misbehave (SDFGI #120496, MSAA 8x #123478) | Medium / high | P0-04 perf gate on every graphics PR; quality tiers; probe fallback; engine stays pinned (W1) |
| R5 | Baseline churn: Phase 1 invalidates every 3D golden, and bad frames could be promoted again, as happened with the pHash 25 baseline | High / high | Objective metrics plus an expected-fail ledger that only shrinks; SSIM goldens only after an art-review checklist with reference photos |
| R6 | Licences: DEM tiles (CC BY vs CC BY-NC), MPL erosion code, OFL fonts, real company names, Shadertoy code | Medium / high | `THIRD_PARTY.md`; a licence file per tile; parody names (D17); no Shadertoy ports; credits screen (U1-06) |
| R7 | Security: the MCP runtime or QA hooks ship in exports | Medium / critical | P0-02 export assertion in CI |
| R8 | Parallel tracks collide on hot files | High / medium | §10 rules; split resources and shaders in P0-06; one merge captain |
| R9 | Tooling: Godot MCP token clobbering, Blender MCP availability, Firecrawl 401, GitHub CI never run | High / medium | Land the sandbox fix (P0-01); `tools/mcp_bridge_alias.py` routine; WebSearch fallback; first CI run in P0-01 |
| R10 | Blender-scripted hero assets look procedural rather than crafted | Medium / high | Real-dimension specs, reference sheets and an art checklist; optional freelance review (D22) after two failed checklists |
| R11 | U3-01 rewrites the UI shell, invalidating 224+ UI-flow checks and the harness | Certain / medium | Route-based tests from U3-01 on; hubs map existing tab IDs to routes; U3-01 runs alone |
| R12 | Performance with more fish, vegetation and reflections plus a world-first UI that always renders 3D | Medium / high | GPU fish (G3-01); visibility ranges; tiering; render scaling behind full-screen views; the Deck target on Low |
| R13 | The uncommitted working-tree edits (`tools/*.sh`) show that another process is active in the repo | Medium / medium | P0-01 lands them; worktrees per track; nobody runs `tools/ci.sh` concurrently on the same tree |
| R14 | The human gates (playtests, Steam, legal, price, push) stall the schedule | Medium / high | Gates are scheduled at the Phase 2 and Phase 4 exits; owner actions are listed per phase |

---

## 12. Deliberately not doing (before 1.0)

FFT ocean (parked until GodotOceanWaves works on 4.7 and playtests ask for it); Terrain3D; SDFGI on macOS; volumetric-cloud plugins; Empire, IPO and the competitor dashboard; species modding (frozen); multiplayer; locales beyond EN and NB.

---

## Appendix: where every critical and high finding lands

- **Visual:**
  - terrain → G1-01, G2-01, G2-02
  - theme → U1-01
  - camera → G1-03
  - menu and picker → U1-07, U1-09
  - charts → U1-05
  - models → G1-07, A2-01, A3-01, G2-04
  - overlap → U1-04
  - text → U1-03
  - water → G1-05, G2-03
  - sky and weather → G1-02, G1-04, G3-05
  - underwater → G3-04
  - information architecture → U3-01
  - art direction → U1-02, U3-02
- **UX:**
  - expansion → U2-01 + S2-01
  - pause menu → U1-06
  - feed → U1-08 + U2-04 + S2-03
  - tutorial → U2-02
  - end screens and bankruptcy → U2-03 + S1-03
  - scenario selection → U1-07 + U2-03
  - market numbers → U1-05 + S3-01
  - placeholders → U1-05
  - harvest → U2-05 + S2-04
  - fjord as wallpaper → G1-03 + U3-01
  - feedback overload → U1-04 + U2-06
  - choice events → S4-08
  - language → U1-03 + U5-02
  - core-loop proposal → U2-*, S4-08
- **Tech3d:**
  - winding and noise → G1-01
  - water → G1-05, G2-03
  - environment writers → G1-02
  - camera → G1-03
  - F746 and particles → P0-05, G3-02
  - fish → G3-01
  - terrain resolution and shading → G1-01, G2-01, G2-02
  - pens and vessels → G1-07, A2-01, A3-01
- **Sim:**
  - capacity → S2-01, S4-01
  - traffic light → S3-02
  - price → S3-01
  - harvest → S2-04
  - production areas → S2-02
  - TGC → S2-03
  - disease → S4-02
  - mortality → S2-05, S4-03
  - F1024 → S1-01
- **Render:**
  - terrain → G1-01, G2-01
  - washed out → G1-02
  - water → G1-05
  - reflections → G2-03
  - terrain mesh and material → G2-01, G2-02
  - sky → G1-03, G1-04
  - underwater → G3-04
- **Art:**
  - terrain → G1-01, G2-01, G2-02
  - pen → G1-07, A2-01
  - barge and wellboat → A3-01
  - world not home → U3-01
  - "Hvorfor" bars → U1-05
  - UI identity → U1-02, U3-02
  - lenses → G3-03
