# Changelog

All notable changes to Lakskongen will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.6.0-alpha] - 2026-10-07

### 🎯 Focus: Waves 1–8 — Depth, Signature UI, Content & Ship Engineering

A single long session that took the game from "feature-complete systems" to
"systems the player can actually feel": real audio, a token-driven theme, deep
biology/economics/genetics, legible overlays and briefings, authored
scenarios/mods, save persistence, and a performance gate.

### Added — Wave 1 (Foundations)
- **Audio truth pass** — catalogues remapped to real `assets/audio/*` paths,
  `loop=true` on all music/ambient imports, duplicate `AudioController` removed,
  seasonal/weather ambience wired to `SimBridge.audio_*`, `/root/GameAudio` →
  `/root/AudioController`, `validate_catalog()` guard.
- **Theme/token pipeline** — theme derived at runtime from
  `godot/theme/tokens.json`; `tools/sync_tokens.py [--check]`.
- **Data-viz** — `Sparkline.gd` under all four Dashboard KPIs + Finance balance,
  `LineChart.gd` area fill/gridlines/empty state, direction-aware value flashes.
- **Scenarios + mods** — `ScenarioSystem` constructed in `SimulationEngine` with
  daily win checks; `GameBridge` scenario API implemented (`ApplyScenario`,
  `ScenarioLoaded`); `ModLoader` instantiated (`user://mods`), Mod Manager wired.
- **Build repair** — `SaveLoadManager` brace bug, missing namespace aliases and
  typing fixed; the Godot C# assembly compiles again.

### Added — Wave 2 (Onboarding + consequence)
- **Unified onboarding** — C# `TutorialSystem` is the single source of truth;
  `tutorial_steps.json` rewritten to a valid 14-step schema; GDScript
  `TutorialManager`/`TutorialOverlay` deprecated; `TutorialState` autoload gates
  advanced tabs (progressive disclosure).
- **Consequence director** — new `ConsequenceDirector` autoload maps bridge
  signals → screen-shake, toasts, and audio; `ToastNotification` gained dedup,
  severity-ranked queue and click actions; `NotificationCenter` grouped/sorted.
- **Scenario goal HUD + mod panel** — missing `ui/ScenarioGoalHUD.gd` created and
  instanced; `MainMenu` opens the Mod Manager on demand.

### Added — Wave 3 (Depth systems)
- **Health** — `DelousingHarmModel`, `ThermalLiceCoupling`, `GillHealthWarning`
  (0–1 early warning) with warming coupled into lice development.
- **Biology** — `SmoltQuality` (Standard/Large/SuperSmolt carryover, grounded in
  SuperSmolt field data) stamped onto cohorts; precision-feeding spread → FCR,
  waste and size variance.
- **Finance/Market** — `ResourceRentTax` (25% cash-flow, 70M deduction, opt-in),
  `PriceCouncil` (binding monthly pen price), `EnvironmentalFlexibility`
  (red-zone recovery via closed containment).
- **Campaigns** — 4 real-event scenarios (PO3 red-zone cut, ISA outbreak, algal
  catastrophe, resource-rent transition); **9 scenarios load** in-game.

### Added — Wave 4 (Depth UI + modding)
- Finance binding pen price + resource-rent row; per-site gill-risk chips;
  smolt-quality selector with carryover tooltips.
- Sample mod `godot/mods/example_norwegian_pack/`; `docs/Modding_API.md`
  reconciled to the real contract; new `docs/CALIBRATION.md`.
- Cohort quality validated on both save paths; 3-year resource-rent soak test.

### Added — Wave 5 (Insurance, mitigation, overlays)
- **Insurance (P11)** — `InsuranceSystem`: Mortality/Bloom/Storm/ISA policies
  with premium/deductible/coverage and stacked payouts capped at loss.
- **Bloom mitigation** — `BloomMitigation` (bubble curtain / deep feeding /
  pumping / O2 diffuser) with severity- and target-aware effectiveness.
- **Data overlays** — `SiteOverlayLegend.gd` (lice/oxygen/biomass/gill/
  containment, green→amber→red ramp) embedded in `MapPanel`.
- Dashboard KPI cards gained honest "explain this number" tooltips.

### Added — Wave 6 (Genetics + player-supply)
- **Genetics (P12)** — `GenomicSelection` (breeder's equation, marker accuracy,
  Wright inbreeding F + depression) and `GeneticEngineering` (per-region
  regulatory status, sterility containment, reputation/recall risk).
- **Player-supply market impact (P15)** — `PlayerSupplyImpact` +
  `MarketSimulation.ApplyPlayerHarvest`: large harvests press the spot price
  down (−8% cap), decaying ~15%/day.
- Engine wiring: insurance payouts on bloom/disease/storm/escape; mitigation
  cuts bloom mortality and charges OPEX.

### Added — Wave 7 (Briefings + content + save)
- **Depth briefing** — `MorningBriefing` daily depth line (gill warning, active
  insurance premium, Price-Council pen price, own supply pressure) and a
  night-shift digest.
- **Content** — encyclopedia/event/achievement/tutorial data refreshed;
  Norwegian event flavour and onboarding copy.
- **Save persistence** — empire/competitor state, cohort quality, ModLoader
  cash, and scenario carryover round-trip through save/load with versioning.

### Added — Wave 8 (Perf gate + localization + docs)
- **Performance gate** — deterministic day-advance benchmark (~0.27 ms/day on
  the reference 100-site run) added to the verification loop.
- **Localization finalize** — 108 new `key,en,no` rows covering insurance,
  bloom mitigation, genetics (gain/inbreeding/GE), overlay legends, depth
  briefing, smolt quality, price council, resource rent and gill risk; 5
  previously malformed CSV rows repaired; `translations.en/nb.translation`
  regenerated; workflow documented in `docs/LOCALIZATION.md`.

### Testing
- **674 → ~690 tests, 0 failures** across the assemblies (incl. 3-year soak).
- Godot editor scan: 0 GDScript parse errors; headless boot: 0 script errors.
- `tools/sync_tokens.py --check` → in sync.

### Notes
- Roadmap and remaining work (P8 barrier tech, P14 audio polish, Steam
  page/demo/Next Fest, CI perf budget) tracked in `PLAN.md`.

---

## [0.5.0-alpha] - 2026-03-16

### 🎯 Focus: Waves 10-12 — Playable Game with 3D Fjords, Tutorial, Advanced Features

### Added — Wave 10 (Playability + Visuals)
- **Complete UI System**
  - Dashboard with real-time KPIs, biomass charts, financial overview
  - Pen Management panel (stock, feed, harvest, monitor)
  - Save/Load menu with multiple slots and metadata preview
  - Full game loop integration (pause, speed control, day progression)
- **3D Fjord Renderer**
  - Norwegian coastal terrain with fjord geometry
  - Custom water shader with reflections, foam, and depth
  - Sky shader with dynamic time-of-day
  - Terrain shader with cliff faces and vegetation
  - Tilt-shift depth-of-field effect
  - Weather visualization shaders (rain, fog, snow, storm)
  - Camera system (smooth pan, zoom, rotation)
  - Dynamic lighting and atmosphere
  - 11 custom shaders total
- **Tutorial System**
  - 23-step interactive tutorial
  - Progress tracking and contextual hints
  - Progressive feature unlocking

### Added — Wave 11 (Depth + Integration)
- **Advanced Gameplay Features**
  - Weather impacts on fish health and operations
  - Storm events affecting feeding schedules and mortality
  - Temperature stress modeling with seasonal variation
  - Disease outbreak events with spread mechanics
  - Treatment options (medication, delousing) with cost tracking
  - Contract system UI (forward contracts vs spot market)
  - IPO/Stock market UI (company valuation, stock price, dividends)
- **Test Coverage**
  - 100% test pass rate achieved: 203/203 tests
  - Integration tests for all new Wave 10-11 features

### Added — Wave 12 (Content + Export)
- **5 Playable Scenarios**
  - Small Farm Start — Classic beginner experience
  - Industrial Giant — Large-scale operations challenge
  - Organic Pioneer — Sustainability-focused gameplay
  - Research Station — Science and breeding focus
  - Family Inheritance — Story-driven management
- **Windows Export Configuration**
  - Export presets for Windows Desktop
  - Production-ready build pipeline
  - Steam store capsule artwork and copy

### Technical
- ~3,000 lines of GDScript (UI, camera, weather, tutorial)
- ~1,600 lines of C# (stubs, gameplay features, tests)
- 11 custom shaders (water, sky, terrain, tilt-shift, weather)
- 26 new files across godot/ and src/
- Full C#/GDScript interop via SimBridge/GameBridge
- .gitignore added (bin/obj/IDE artifacts excluded)

### Game Status
- **PLAYABLE** — Full game loop functional (egg to export)
- **VISUAL** — Anno 1800-tier fjord aesthetic at 60 FPS target
- **DEEP** — Production cycle + environmental challenges + financial strategy
- **TESTED** — 203/203 tests passing, 0 errors

---

## [0.4.0] - Sprint 10 - 2026-03-14

### 🎯 Focus: Tutorial Polish, Localization, Performance, Stress Testing

### Added
- **Localization System**
  - Norwegian (Bokmål) translations for all UI strings
  - English primary support
  - `LocalizationManager.gd` autoload for runtime language switching
  - CSV-based translation workflow
- **Performance Profiling**
  - Stopwatch instrumentation in `SimulationEngine.AdvanceOneDay()`
  - Standalone benchmark tool for 100k fish simulation
- **Stress Testing**
  - Save/Load integrity tests over 905-day production cycles
  - Corruption detection and recovery mechanisms

### Improved
- Performance optimization (biology loop bottleneck identified)
- Mortality debugging (fixed triple-stacking disease mortality)
- Tutorial system with interactive scenarios

### Fixed
- Save/Load error handling for corrupted files
- UI stability with safe node access
- Audio system path management

### Testing
- 14/14 core unit tests passing

---

## [0.3.0] - Sprint 9 - 2026-03-12

### 🎯 Focus: Advanced Biology, Markets, VAP Processing

### Added
- 8-trait genetic system with Mendelian inheritance
- Disease & parasite systems (sea lice, IPN, ISA, furunculosis)
- Adaptive LOD for disease simulation
- Environmental simulation (zones, temperature, currents, algal blooms)
- Market intelligence and VAP processing chain (17 products)
- Multiplayer foundation (architecture only)
- Mod support framework (alpha)
- Difficulty settings (Easy/Normal/Hard/Realistic)

### Improved
- Dynamic seasonal pricing (Easter, Christmas peaks)
- Feed management with FCR calculation
- EBIT/kg financial tracking

### Quality Control
- Wave 4 QC review completed (7.5/10 → 8.5/10)
- 550+ lines of defensive code added

---

## [0.2.0] - Phase 1 Prototype - 2026-03-10

### Added
- Core gameplay loop (hatchery → sea site → harvest → market)
- Basic genetics (3-trait system)
- Seasonal price curves and batch selling
- Time system with speed control
- Game Manager orchestrating all subsystems
- Placeholder dashboard UI

---

## [0.1.0] - Initial Setup - 2026-03-08

### Added
- Project structure and .NET 8.0 solution
- Godot project initialization
- Git repository configured
- VISION.md and design documents

---

## Version Numbering

**Format:** `MAJOR.MINOR.PATCH[-prerelease]`

- **0.x.x** — Pre-release development
- **1.0.0** — Early Access launch
- Tags: `alpha` (incomplete), `beta` (feature-complete), `rc` (release candidate)

---

## Links

- [README.md](README.md) — Project overview and quick start
- [VISION.md](VISION.md) — Design philosophy
- [GAMEPLAY_ROADMAP.md](GAMEPLAY_ROADMAP.md) — Feature roadmap
- [CONTRIBUTING.md](CONTRIBUTING.md) — How to contribute
