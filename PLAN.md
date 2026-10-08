# Lakskongen — Road to Elite

**Goal:** an elite, realistic salmon-farming simulator with a signature UI.
**Status date:** 2026-10-08 · **Build:** 0 errors, 0 warnings · **Tests:** 727 passed, 1 skipped (deterministic)

---

## Where we are

- ~240 C# files / 37k LOC, ~120 GDScript files, 40 scenes.
- Phases 1–7 feature-complete; deep biology, market O-U, regulation (traffic lights/MAB), empire layer.
- Real Norwegian data calibration with a quarterly review checklist (`docs/REALISM_CHECKLIST.md`).

### Just landed (this program)
| Item | Deliverable | Status |
|------|-------------|--------|
| **A** Realism spine | `CalibrationRegistry.cs` + `godot/data/calibration.json` + `CalibrationGuardTests` (16 guards). Feed now modelled as price×FCR (~25.6, was a flat 12.5). | ✅ |
| **B1** Token pipeline | Theme derives at runtime from `godot/theme/tokens.json`; `tools/sync_tokens.py [--check]`. | ✅ |
| **B2** Data-viz | New `Sparkline.gd`; `LineChart.gd` polished (area fill, gridlines, empty_text). | ✅ |
| **B0** Theme/a11y | Global theme applied to root; focus rings; semantic variations; `UIPolish.gd`; toast palette. | ✅ |
| **Stability** | Deterministic engine seeding (`Initialize(enableEvents, seed)`); fixed 2 advisor bugs; de-flaked integration tests (8/8). | ✅ |

---

## Phase A — Realism spine (DONE)
Single source of truth for balance, enforced by tests. Next: wire `MarketSimulation`/`FeedSystem` to read the registry live (today they keep their own defaults) and re-run the quarterly checklist.

## Phase B — UI: consistent → signature
- **B1 Token pipeline** ✅
- **B2 Data-viz components** ✅ — *next: drop Sparklines into Dashboard/Finance KPI cards.*
- **B3 Motion language** — centralize (tab enter, KPI count-up, value flashes, alert pulse). `JuiceEffects`/`UIPolish` started.
- **B4 Variations + states** — apply `Secondary/Ghost/Danger` buttons across panels; add empty/loading/error states everywhere.
- **B5 Onboarding** — see spec below.
- **B6 A11y** — full keyboard/gamepad nav, focus order, colorblind-safe status, scaling.

## Phase C — Feel & feedback
Audio + consequence theater + notification intelligence. See spec below.

## Phase D — Depth players feel
Authored campaign scenarios; mod pipeline → Workshop; photo mode polish. See spec below.

## Phase E — Ship engineering
Perf budget (<2ms/day @100 sites — 3-yr run currently ~0.1s), save fuzz round-trips, multi-platform export, weekly playtest + eval harness.

```
A ✅ ─┐
      ├─▶ C ─▶ E
B1,B2 ┘   (B3–B6 ongoing)
D ────────────────────────┘
```

---

## Integration spec (from code audit)

### Audio — current state & fixes
Assets live in `godot/assets/audio/{music,ambient,sfx,ui}` but `MusicLibrary.gd`/`AudioController.gd` reference non-existent `res://audio/...` paths and wrong filenames → **almost nothing plays**. Music/ambient `.import` files have `loop=false`. Duplicate `AudioController` (autoload + `GameScene.tscn`). Seasonal music is dead code (reads a nonexistent `get_current_season`; the value already exists at `SimBridge.audio_seasonal_track`).

**Next 5:**
1. Fix `MusicLibrary.gd` paths/names → `res://assets/audio/...`; fix `_start_ocean_ambient` candidates; set `loop=true` on music/ambient imports.
2. Remove duplicate `AudioController`; fix `/root/GameAudio` → `/root/AudioController` (`MorningBriefing.gd`, `DecisionPanel.gd`).
3. Seasonal transitions via `SimBridge.audio_seasonal_track` + `_update_bgm_direct`.
4. Location/weather ambience via `_update_ambient_base_direct` / `_update_ambient_weather`.
5. UI micro-sounds on press/hover/panel open-close; event stingers for harvest/crisis/achievement/lice.

### Onboarding — two conflicting systems
- GDScript `TutorialManager` loader is **broken** (`tutorial_steps.json` is a bare array but the loader requires a `steps` key) → 23-step track never advances; `TutorialOverlay` has signal-arity + missing-method bugs.
- C# `TutorialSystem` is robust (12 built-in steps) and drives the working `TutorialUI`.

**Next 5:**
1. Pick **C# `TutorialSystem` as source of truth**; write `godot/data/tutorial_steps.json` in its schema (12–15 steps) with `phase`/`unlocks`.
2. Deprecate/fix the GDScript `TutorialManager` + `TutorialOverlay`; keep one UI (`TutorialUI`).
3. Add UI-tracking completion conditions (`feed_panel_opened`, `health_checked`) via a `GameState` event-tag queue.
4. Progressive disclosure: gate panels by tutorial phase (`TutorialState` autoload).
5. Wire `MorningBriefing.tscn` into `GameScene` (fix its audio node name).

### Scenarios + Mods — implemented but unplugged
- Three unreconciled scenario representations: GDScript `scenarios.json` (5), C# `ScenarioSystem` (5, different IDs), hardcoded UI panel (5). `GameBridge` scenario API is stubbed (returns empty/false); `ApplyScenario` doesn't exist; `ScenarioSystem` is never constructed.
- `ModLoader.cs` is fully functional (manifest, deps, prefix dispatch, enable/disable) but never instantiated; `SteamWorkshopManager` is a simulation; `ModManagerPanel.tscn` is never instanced. Docs (`Modding_API.md`, `SCENARIOS.md`) diverge from the code.

**Next 5:**
1. Unify scenario schema on C# `ScenarioDefinition`; convert `godot/data/scenarios.json`; pick canonical IDs.
2. Construct `ScenarioSystem` in `SimulationEngine`; call `CheckWinConditions` daily; replace `GameBridge` stubs + emit `ScenarioLoaded`.
3. Instantiate `ModLoader` in `GameBridge`; implement `GetModList/ReloadMods/ApplyModSettings`; instantiate `ModManagerPanel.tscn`.
4. Implement/replace `ui/ScenarioGoalHUD.gd` (missing) + connect goal signals.
5. Real Steam Workshop backend behind `ISteamWorkshop` (build-gated).

---

## How we work
- **Godot MCP Toolkit** drives UI/script work: `scene_query` audits, `node_set_property` (batch) edits, `script_check`/`lsp_project_diagnostics` gates, `runtime_screenshot` verification. Never hand-edit `.tscn` when the editor is live.
- **One editor at a time** for this project — a second instance clobbers the shared MCP token and breaks auth for the running one.
- Verify centrally: `dotnet build` → all test projects → Godot headless → token sync check.

## Immediate next actions
1. Wire **Sparklines** into Dashboard/Finance KPI cards (B2 follow-through).
2. **Audio A1–A3** (make sound play at all) — highest visible impact.
3. **Scenario C2+C3** (construct `ScenarioSystem`, replace stubs) — unlocks campaign play.

---

## ⚠️ Critical build note (learned the hard way)
The Godot C# assembly is **`godot/LakskongenGame.csproj`**, separate from `src/LakskongenSim`.
Building only the sim library hides errors in `GameBridge.cs`/`SaveLoadManager.cs`, and headless
Godot will silently run a **stale DLL**. **Always** build the whole solution (`dotnet build LakskongenSim.sln`)
and, after adding `class_name` scripts, run a headless editor scan (`--headless --editor --quit`)
before trusting a headless boot.

---

## Wave 1 — landed (this session)
| Workstream | Deliverable | Verified |
|---|---|---|
| **Audio** | Catalogues remapped to real `assets/audio/*`; `loop=true` on all music/ambient `.import`; duplicate `AudioController` removed from `GameScene.tscn`; seasonal/weather ambience wired to `SimBridge.audio_*`; `/root/GameAudio`→`/root/AudioController`; `validate_catalog()` guard | ✅ |
| **UI data-viz** | Sparklines under all 4 Dashboard KPIs + Finance balance, fed from analytics; value-change flashes (green/red, direction-aware) | ✅ |
| **Scenarios** | `ScenarioSystem` constructed in `SimulationEngine`; daily `CheckWinConditions`; `GameBridge` scenario API implemented (+`ApplyScenario`, `ScenarioLoaded`); `scenarios.json` ids unified; start flow end-to-end | ✅ |
| **Mods** | `ModLoader` instantiated in `GameBridge` (`user://mods`); `GetModList/ReloadMods/ApplyModSettings`; `ModManagerPanel` singleton-check fixed | ✅ |
| **Build repair** | Fixed `SaveLoadManager.cs` brace bug (misplaced `ValidateAndFixGameState`) + missing `Farm/Events/Processing/Logistics` aliases + `CrewEffects` ns + carbon-array typing → **Godot assembly compiles** | ✅ |
| **Tests** | 409 sim + 20 save/load + 46 health green; deterministic (seeded engine) | ✅ |

## Research-driven roadmap (web, Oct 2026)
Sources: Steam store pages & reviews, HowToMarketAGame benchmarks, Fiskeridir/NVI/VetInst, FFE, LandbasedAQ, SalmonBusiness.
Full report saved in the session log. Top priorities:

**P1 Unify onboarding** (RimWorld-style AI tutor; one TutorialSystem) · **P2 Real mod pipeline → Steam Workshop** · **P3 Authored scenarios from real 2024-26 events** (PO3 red-zone cut, SalMar ISA Q3-26, 1,000 t algal-bloom mortality, resource-rent transition) · **P4 Mortality attribution + delousing-harm & welfare budget** (NVI 2025: delousing injuries + infection = top causes) · **P5 Post-smolt quality carryover** (SuperSmolt: 1.02% vs 1.53% 4-mo mortality; 5.69% vs 8.6% to harvest) · **P6 Precision/AI feeding as a control problem** (feed spreading → FCR/size variance) · **P7 Dynamic traffic-light + closed-containment "environmental flexibility"** (2026: 3 green/9 yellow/1 red PO3) · **P8 Lice resistance + barrier tech** (Smoltscreen permeable liner, flow-dependent) · **P9 Resource-rent tax + binding Price Council** · **P10 Climate coupling** (warming → faster lice; gill-health early warning) · **P11 Catastrophic events + insurance** · **P12 Genetics: genomic selection, inbreeding, GE branch** · **P13 Data-dense dashboard UX** (overlays, alert triage, "explain this number") · **P14 Audio + consequence theater** · **P15 Launch engineering** (wishlist targets Bronze 5k / Silver 8k / Gold 50k / Diamond 90k; early Steam page + demo + Next Fest; perf budget; no exploitative DLC).

**Signature "wow" (no competitor has):** regulation as first-class strategy; generational fish-level biology; living fjord hydro-climate coupled to biology; full egg→sashimi→byproducts chain; player supply that moves the global price.

**Anti-patterns to avoid:** bolting on shallow minigames (FS25 Highlands Fishing was panned); shipping bugs in marquee systems; broken/duplicated onboarding; depth without legible consequence ("boring game"); late/weak Steam page; performance/DLC backlash (Cities: Skylines II); opaque numbers; over-promising scope.

## Wave 2 — landed (this session)
- **Onboarding unified**: C# `TutorialSystem` is now the single source of truth; `godot/data/tutorial_steps.json` rewritten (14 steps, valid schema); GDScript `TutorialManager`/`TutorialOverlay` deprecated/fixed; `TutorialOverlay` removed from `Main.tscn`. New `TutorialState` autoload gates advanced tabs (`GameUI`) with progressive disclosure.
- **Notifications + consequence theater**: `ToastNotification` gained dedup, severity-ranked queue, and click actions; new `ConsequenceDirector` autoload maps SimBridge/GameBridge signals → screen-shake + toasts + audio; `NotificationCenter` grouped/sorted.
- **Scenario goal HUD + mod panel**: created missing `godot/ui/ScenarioGoalHUD.gd`, instanced in `GameScene`; `ScenarioManager` drives goal progress from sim state; `MainMenu` opens `ModManagerPanel` on demand.
- **GDScript parse repairs**: fixed `OffshorePanel.gd` (OptionButton ids must be int — moved site ids to item metadata; Variant inference) and `SustainabilityPanel.gd` (missing `_update_categories()` header). Editor scan now reports **0 parse errors**.

## Wave 3 — landed (this session)
- **Health depth**: `DelousingHarmModel` (compounding injury/handling mortality/welfare by method), `ThermalLiceCoupling` (degree-days + warming), `GillHealthWarning` (0–1 early warning). Wired: warming applied to lice development; gill risk exposed.
- **Biology depth**: `SmoltQuality` (Standard/Large/SuperSmolt carryover, grounded in SuperSmolt data) stamped onto cohorts and applied to sea-phase growth; precision feeding (`spreadQuality01` → FCR/waste/size-variance) wired from watch mode + AI feeding.
- **Finance/Market depth**: `ResourceRentTax` (25% cash-flow, 70M deduction, opt-in flag + `ResourceRentPaid`), `PriceCouncil` (binding monthly pen price), `EnvironmentalFlexibility` (red-zone recovery via closed containment). Wired + exposed via GameBridge.
- **Campaigns**: `ScenarioDefinition` extended (starting sites/research/buildings/story/restrictions/tags) + `ApplyStartingState`; 4 real-event campaigns (PO3 red cut, ISA outbreak, algal catastrophe, resource-rent transition). **Godot now loads 9 scenarios.**

## Wave 4 — landed (this session)
- **Depth UI**: Finance shows binding pen price + resource-rent; per-site gill-risk chips; smolt-quality selector with carryover tooltips; new SimProxies + facade pass-throughs + `StockSmolts` quality overload.
- **Modding**: sample mod `godot/mods/example_norwegian_pack/` (manifest + research/event/scenario JSON); `docs/Modding_API.md` reconciled to the real contract; new `docs/CALIBRATION.md`.
- **Robustness**: cohort `Quality` validated on load (both save paths); save round-trip + scenario-start tests; **3-year resource-rent soak** (~0.4s) with sanity assertions.

## Wave 5 — landed (this session)
- **Insurance + mitigation (P11)**: `InsuranceSystem` (policies Mortality/Bloom/Storm/ISA, premium/deductible/coverage, stacked payouts capped at loss) + `BloomMitigation` (bubble/deep-feed/pump/O2 effectiveness vs bloom kind, anchored to existing site flags).
- **Data overlays + explain (P13)**: new `SiteOverlayLegend.gd` (overlay modes lice/oxygen/biomass/gill/containment with green→amber→red ramp + legend) embedded in `MapPanel`; Dashboard KPI cards now have honest "explain this number" tooltips.

## Session verification (final)
- `dotnet build LakskongenSim.sln` → **succeeded** (sim + Godot C# assemblies).
- Tests: **647 passed, 1 skipped, 0 failed** across 5 assemblies (incl. 3-year resource-rent soak in ~0.4s).
- Godot editor scan: **0 GDScript parse errors**; headless boot: **0 script errors**; 9 scenarios; ModLoader ready; theme derived from tokens.
- `tools/sync_tokens.py --check` → in sync.

## Remaining roadmap (Wave 6+)
- P12 genetics: genomic selection, inbreeding depression, GE branch with regulatory/reputation trade-offs.
- P15: player-supply price impact (your harvest moves the market), Steam page/demo/Next Fest, perf budget CI gate.
- P8 remaining barrier tech (Smoltscreen flow-dependent); P14 audio polish pass.
- Wire insurance/mitigation + overlays into the daily engine loop and UI actions.

## Wave 6 — landed (this session)
- **Genetics depth (P12)**: `GenomicSelection` (breeder's equation, marker/genomic accuracy, Wright inbreeding F + depression) and `GeneticEngineering` (GE traits, per-region regulatory status, sterility containment, reputation/recall risk).
- **Player-supply market impact (P15)**: `PlayerSupplyImpact` (+`MarketSimulation.ApplyPlayerHarvest`) — large harvests press the price down (−8% cap), decaying 15%/day.
- **Engine wiring**: `Insurance` instantiated; bloom mitigation reduces bloom mortality + charges OPEX; insurance payouts on bloom/disease/storm/escape; `HarvestSite` applies player-supply pressure; `GetInsuranceStatus`/`SetInsurancePolicy` exposed.

## Wave 7 — landed (this session)
- **Genetics wired + UI**: inbreeding depression applied to growth; `GetGenomicSelectionPreview`/`GetInbreedingStatus`/`GetGeTraits` exposed; `GeneticsPanel` shows gain/inbreeding/GE availability.
- **Insurance + mitigation UI**: Finance insurance section (enable/coverage/deductible/premium); WeatherDiseasePanel offers all bloom mitigations; new proxies.
- **Content**: +16 encyclopedia entries (81 total), +8 achievements (34 config), Morning Briefing "Dybde" line.
- **Tests/perf/copy**: per-scenario 365-day campaign smoke tests; perf gate; store copy refreshed.

## Wave 8 — landed (this session)
- **Save persistence**: insurance policies, bloom-mitigation registry, resource-rent flag/paid, pen-price council now round-trip through save/load (backward-compatible; legacy saves default safely). Round-trip tests added.
- **Localization + docs**: +108 `en/no` translation rows (552 total, CSV valid), new `docs/LOCALIZATION.md`, `docs/SESSION_SUMMARY.md`, `CHANGELOG.md` `[0.6.0-alpha]`, refreshed `STATUS.md`.

## Wave 9 — landed (this session)
- **Smoltscreen barrier tech (P8)**: `BarrierTech` (permeable 150/200 µm liner) — flow-dependent lice/jellyfish blocking, cleaning decay, capex ~half of closed retrofit, and *not* eligible for environmental flexibility (regulatory ambiguity). Wired into weekly lice settlement + jellyfish blooms; install/clean/status in GameBridge + OffshorePanel.
- **Onboarding + audio**: tutorial track 14→18 steps (genetics, insurance, mitigation, overlays) with `TutorialState` unlock mapping; event stingers for insurance payout / GE deploy / red-zone change / mitigation install via `ConsequenceDirector`; UI sounds on the new panels.
- **Balance/realism**: standalone `FeedSystem` defaults reconciled to the registry (22.3 / 1.15 → 25.65/kg produced); new `docs/BALANCE.md`; `CalibrationGuardTests` extended; `REALISM_CHECKLIST` feed row corrected.

## Critical review verdict (external research + metrics)
Scored against a research rubric (Dwarf Fortress/RimWorld/Factorio/Anno/Cities:Skylines/Mini Motorways/iRacing):
- **Realism/depth: 3.5/5** — genuinely interlocking systems + calibration guards; but never played, so "emergent/legible" is unproven.
- **Code/engineering: 3.5→4/5** — deterministic, save-safe, moddable, 698 tests; was held back by a 5,868-LOC monolith + 9 stubs (now fixed).
- **Graphics/presentation: 2/5** — **the blocker**: no 3D model assets, procedural primitives, placeholder capsules.
- **Category context:** no top-tier aquaculture incumbent exists (Aquaculture Land 75%, FS25 Highlands Fishing 56% mixed); realistic 3D is *not* required — coherent stylized art is.

## Wave 10 — landed (this session) — attacking the graphics blocker + engineering
- **Visual identity (A)**: `tools/gen_textures.py` (PIL, deterministic) → 14 tileable textures + normal maps (grass/rock/snow/sand/moss, foam, caustics); `terrain_shader` now textured (biplanar + slope/height blend + normals); `water_shader` foam+caustics; `sky_shader` aurora; new `underwater_post.gdshader`; `FjordEnvironment.tscn` gains ReflectionProbe, ACES/glow/SSAO/fog tuning, particle process materials.
- **Engineering (B)**: `GameBridge.cs` 5,868 LOC → anchor + 5 partials (`Core`/`Farm`/`Market`/`Depth`/`Economy`), behaviour-identical; **0 `NotImplementedException`** (safe-fail + log).
- **CI (C)**: `tools/ci.sh` + `.github/workflows/ci.yml` — build, test, perf gate, token sync, JSON validation, headless parse scan. **Passes locally (all 6 steps).** Fixed stale `verify.yml` (.NET 8→10).
- **Store art (D)**: `tools/gen_store_art.py` → illustrated "Nordic Aurora Fjord" capsule set (7 sizes) replacing text-on-gradient placeholders.

**Caveat:** graphics/store-art changes are code-generated and **cannot be visually verified headlessly** — they need human eyeballs on a display before shipping.

## Final session verification
- `dotnet build LakskongenSim.sln` → **succeeded**.
- Tests: **698 passed, 1 skipped, 0 failed**.
- `tools/ci.sh` (with Godot) → **PASSED (6/6)**; perf ~0.21 ms/day.
- Godot editor scan: **0 parse/shader errors**; headless boot: **0 script errors**; GameBridge autoload alive after split; 14 textures imported.

## Next (post-session)
- Visual QA on a real display (all verification so far is headless + tests).
- P8 Smoltscreen barrier tech; P14 audio polish pass; wire genetics/insurance into onboarding.
- Launch engineering: Steam page + demo + Next Fest; perf CI gate in the pipeline; balance playtest pass.
- Commit the milestone (nothing has been committed this session).