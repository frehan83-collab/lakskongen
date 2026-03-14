# Changelog

All notable changes to Lakskongen will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### 🚀 Planned Features
- Technology research tree (T-Tree 2.0)
- Sustainability certification system
- Photo mode with visual milestones
- Active competitor AI
- Campaign scenarios
- Multiplayer co-op mode

---

## [0.4.0] - Sprint 10 - 2026-03-14

### 🎯 Focus: Tutorial Polish, Localization, Performance, Stress Testing

### Added
- **Localization System**
  - Norwegian (Bokmål) translations for all UI strings
  - English primary support
  - `LocalizationManager.gd` autoload for runtime language switching
  - Translation extraction script (`extract_strings.py`)
  - CSV-based translation workflow
- **Performance Profiling**
  - Stopwatch instrumentation in `SimulationEngine.AdvanceOneDay()`
  - Biology Phase and Health Phase timing metrics
  - Standalone benchmark tool (`PerformanceBenchmark/`) for 100k fish simulation
  - Performance report (`T52_PERFORMANCE_REPORT.md`)
- **Stress Testing**
  - Save/Load integrity tests over 905-day production cycles
  - Corruption detection and recovery mechanisms
  - Test report (`T53_SAVELOAD_STRESS_REPORT.md`)

### Improved
- **Performance Optimization**
  - Identified biology loop as #1 bottleneck
  - Designed in-place cohort update strategy (40-60% expected gain)
  - Disease system already optimized with adaptive LOD
  - Projected performance: 10k fish @ 125 FPS, 100k fish @ 20 FPS (before optimization)
- **Mortality Debugging**
  - Fixed triple-stacking disease mortality (DiseaseSystem + IntegratedDiseaseSystem + DiseaseLODSystem)
  - Added debug logging to all mortality paths
  - Adjusted baseline mortality rates (T47 adjustments)
  - Debug report (`T54_MORTALITY_DEBUG_REPORT.md`)
- **Tutorial System**
  - Interactive tutorial scenarios for new players
  - Tooltips and contextual help
  - Progressive feature unlocking

### Fixed
- **Save/Load System**
  - Robust error handling for corrupted save files
  - Null safety in vehicle capacity handling (`LogisticsManager.cs`)
- **UI Stability**
  - Safe node access with graceful degradation (`Dashboard.gd`)
  - Dictionary validation in market data (`Market.gd`)
- **Audio System**
  - Refactored hardcoded paths to constants (`AudioManager.cs`)

### Testing
- **14/14 core unit tests passing** (1 skipped — requires calibration data)
- **7 integration tests added** for VerticalIntegration system
- Performance benchmarks run (100k fish over 100 days)

---

## [0.3.0] - Sprint 9 - 2026-03-12

### 🎯 Focus: Multiplayer, Mods, Steam Workshop, Difficulty Settings

### Added
- **Multiplayer Foundation**
  - `MultiplayerBridge.cs` autoload for network sync
  - `NetworkManager.gd` for connection handling
  - Player session management framework
  - Co-op mode architecture (not yet playable)
- **Mod Support (Alpha)**
  - Steam Workshop integration planning
  - Resource-based mod loading system
  - Custom scenario support
  - Mod validation framework
- **Difficulty Settings**
  - Easy/Normal/Hard/Realistic modes
  - Customizable parameters (disease frequency, market volatility, regulation strictness)
  - Preset configurations for different play styles
- **Advanced Biology Models**
  - 8-trait genetic system (growth rate, disease resistance, fillet quality, maturation timing, feed efficiency, color, stress tolerance, fillet yield)
  - Inbreeding depression mechanics
  - Genomic selection framework
  - Mendelian inheritance with mutation
- **Disease & Parasite Systems**
  - Sea lice population dynamics with treatment resistance
  - IPN (Infectious Pancreatic Necrosis) model
  - ISA (Infectious Salmon Anemia) spread mechanics
  - Furunculosis bacterial infection
  - Adaptive LOD (Level of Detail) for disease simulation
  - `IntegratedDiseaseSystem.cs` with High/Medium/Low detail tiers
- **Environmental Simulation**
  - Zone-based temperature profiles
  - Seasonal light cycles (midnight sun, polar night)
  - Current patterns affecting lice drift
  - Algal bloom events
  - Temperature-driven growth modulation

### Improved
- **Market System**
  - Dynamic seasonal pricing (Easter, Christmas peaks)
  - Size-based premiums (4-6 kg optimal)
  - Quality grading with price multipliers
  - Contract vs. spot market differentiation
- **Feed Management**
  - Feed quality tiers (Economy/Standard/Premium)
  - FCR (Feed Conversion Ratio) calculation with temperature correction
  - Daily feed cost tracking per cohort
- **Finance Tracking**
  - EBIT/kg metrics
  - Revenue/expense categorization
  - Cash flow projections

### Fixed
- **Genetics System**
  - Fixed `preload()` usage in `Genetics.gd` (converted to static functions)
  - Trait heritability calculations corrected
- **Growth Model**
  - Consolidated duplicate growth logic (Fish.gd vs SeaSite.gd)
  - Temperature tolerance range validation
- **Market Pricing**
  - Seasonal curve peak timing adjusted (Easter ~day 100)
  - Price volatility smoothing

### Quality Control
- **Wave 4 QC Review** completed (Lars Pettersen - Claude Opus)
- Quality score improved: **7.5/10 → 8.5/10**
- Critical issues resolved: 1 duplicate enum, 3 high-priority bugs
- Medium/low issues: 14 documentation and cleanup tasks
- Report: `QC_REPORT_WAVE4_FINAL.md`

### Documentation
- **GAMEPLAY_ROADMAP.md** — 31KB research document on industry trends and feature prioritization
- **CONTRACT_SYSTEM_ARCHITECTURE.md** — C# vs GDScript integration guide
- **SYSTEM_OPTIMIZATION_LOG.md** — Model configuration lessons learned

### Testing
- 550+ lines of defensive code added
- 9 commits across bug fixes and QC improvements
- Zero regressions introduced

---

## [0.2.0] - Phase 1 Prototype - 2026-03-10

### 🎯 Focus: Core Gameplay Loop

### Added
- **Hatchery System**
  - Egg incubation with temperature-driven development
  - Smolt production pipeline
  - Vaccination scheduling
- **Sea Site Management**
  - Net pen simulation (stocking, growth, monitoring)
  - Biomass tracking and harvest triggering
  - Mortality modeling (baseline + disease)
- **Basic Genetics**
  - 3-trait system (growth, disease resistance, fillet quality)
  - Random broodstock generation
  - Simple breeding mechanics
- **Market Integration**
  - Seasonal price curves
  - Batch selling with transaction records
  - Revenue tracking
- **Time System**
  - Day counter with speed control (1x/5x/10x)
  - Signals for day/week/month transitions
  - Pause/resume functionality
- **Game Manager**
  - Orchestrates all subsystems
  - Tick-based simulation loop
  - State management
- **Placeholder UI**
  - Dashboard with KPI display (print statements)
  - Main scene structure (`Main.tscn`)

### Technical Foundation
- **Godot 4.3 + C# architecture**
- Pure C# simulation layer (no Godot dependencies)
- GDScript presentation layer
- JSON-based configuration
- Modular system design

### Known Issues
- Dashboard UI is print-only (needs Control nodes)
- Growth model temperature response needs tuning
- No save/load system yet
- No multiple sites/zones

---

## [0.1.0] - Initial Setup - 2026-03-08

### Added
- Project structure created
- .NET 8.0 solution setup
- Godot project initialization
- Git repository configured
- VISION.md and design documents

---

## Version Numbering

**Format:** `MAJOR.MINOR.PATCH`

- **MAJOR:** Incompatible API changes (e.g., save format breaks)
- **MINOR:** New features in a backwards-compatible manner (sprints)
- **PATCH:** Backwards-compatible bug fixes (hotfixes)

**Pre-release tags:**
- `alpha` — Early development, incomplete features
- `beta` — Feature-complete, testing phase
- `rc` — Release candidate, final testing

**Examples:**
- `0.3.0-alpha` — Sprint 9 (multiplayer alpha)
- `0.4.0-beta` — Sprint 10 (localization beta)
- `1.0.0-rc1` — Early Access release candidate
- `1.0.0` — Public Early Access launch

---

## Release Schedule

- **Sprints:** 1-2 weeks each
- **Early Access Launch:** Q3 2026 (estimated)
- **Full Release:** Q1 2027 (estimated)

---

## Links

- [VISION.md](VISION.md) — Design philosophy
- [GAMEPLAY_ROADMAP.md](GAMEPLAY_ROADMAP.md) — Feature roadmap
- [STATUS.md](STATUS.md) — Current development status
- [CONTRIBUTING.md](CONTRIBUTING.md) — How to contribute

---

**Stay tuned for updates!** 🐟
