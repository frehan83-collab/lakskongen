# Lakskongen — Full Codebase Report
**Generated:** 2026-03-23 14:30 CET  
**Repository:** https://github.com/frehan83-collab/lakskongen  
**Commits:** 123 | **Latest:** 96b1055

---

## Summary

| Metric | Count |
|--------|-------|
| GDScript files | 97 |
| GDScript lines | 25,749 |
| C# source files | 105 |
| C# lines | 25,131 |
| C# autoload scripts | 5 |
| Scene files (.tscn) | 19 |
| Shaders | 5 |
| UI Panels | 64 |
| Audio files | 69 |
| Font files | 13 |
| Texture files | 6 |
| Autoloads | 14 |
| Design HTML prototypes | 9 |
| **Total source lines** | **~51,000** |

---

## Architecture

```
salmon-sim/
├── godot/              # Godot 4.3 + C# project (main game)
│   ├── scripts/
│   │   ├── ui/         # 64 UI panels (GDScript)
│   │   ├── autoload/   # 5 C# autoloads + GDScript autoloads
│   │   ├── environment/ # Fjord, weather, camera
│   │   ├── tutorial/    # Tutorial system
│   │   └── *.gd        # Core scripts (SimBridge, GameController, etc)
│   ├── scenes/         # 19 .tscn scene files
│   ├── shaders/        # 5 shaders (water, terrain, sky, tilt-shift)
│   ├── assets/         # Audio (69), fonts (13), textures (6)
│   ├── theme/          # LakskongenTheme v2.0 (Nordic Minimalist)
│   ├── translations/   # 7 languages (no, en, de, fr, es, ja)
│   ├── addons/         # gd-retroterrain
│   └── data/           # achievements.json, scenarios.json, tutorial_steps.json
├── src/LakskongenSim/  # C# simulation engine (105 files)
│   ├── Biology/        # Fish population, genetics, growth, mortality
│   ├── Core/           # SimulationEngine, GameState, Research, Achievements
│   ├── Environment/    # Weather, water chemistry, zones
│   ├── Events/         # Storyteller, Norwegian events, crises, milestones
│   ├── Expansion/      # Acquisitions, vertical integration
│   ├── Farm/           # AI feeding, delousing, RAS, offshore, hatchery
│   ├── Finance/        # P&L, investors, stock market, empire, competitors
│   ├── Health/         # Disease (SIR model), sea lice, stochastic engine
│   ├── Logistics/      # Trucks, wellboats, inventory, routes
│   ├── Market/         # Contracts, pricing, currency, quality grades
│   ├── Processing/     # Plants, product catalog, brand reputation
│   └── Sustainability/ # ESG tracking, certifications
└── design/             # HTML prototypes (9), tokens.json, Figma links
```

---

## Simulation Engine (C# — 25,131 lines)

### Biology (4 files, 678 lines)
- `FishPopulation.cs` — Population tracking per cohort
- `Genetics.cs` — Breeding traits, lice resistance, growth genes
- `GrowthModel.cs` — VB + Gompertz growth curves, temperature-dependent
- `MortalityModel.cs` — Natural, disease, handling mortality rates

### Core (21 files, 8,484 lines)
- `SimulationEngine.cs` (1,704 lines) — Master orchestrator, day tick, all subsystems
- `GameState.cs` (341 lines) — Serializable game state
- `ResearchSystem.cs` (588 lines) — 25-node tech tree with prerequisites
- `ScenarioSystem.cs` (456 lines) — 5 Norwegian scenarios
- `AchievementSystem.cs` (284 lines) — 38 achievements with conditions
- `TutorialSystem.cs` (579 lines) — Data-driven onboarding
- `ModLoader.cs` (584 lines) — Mod support + CSV export
- `MultiplayerManager.cs` (469 lines) — Foundation for multiplayer
- `BarentsWatchFishHealthIntegration.cs` (478 lines) — Real Norwegian fish health API
- `AquacultureCalibrationEngine.cs` (217 lines) — Real-world data calibration

### Environment (4 files, 745 lines)
- `WeatherSystem.cs` — Markov chain Norwegian weather, seasonal patterns
- `WaterChemistry.cs` — Temperature, oxygen, salinity, pH modeling
- `Zone.cs` — 8 Norwegian coastal zones with realistic parameters

### Events (6 files, 2,157 lines)
- `Storyteller.cs` — AI event generation based on game state
- `NorwegianEvents.cs` (969 lines) — 40+ realistic Norwegian events
- `CrisisCapitalismSystem.cs` — Distressed asset opportunities
- `VisualMilestones.cs` — Cinematic milestone moments

### Farm (8 files, 1,610 lines)
- `AIFeedingSystem.cs` — Camera-based appetite detection simulation
- `DelousingSystem.cs` — Mechanical/thermal/cleaner fish treatments
- `RASFacility.cs` — Recirculating aquaculture system
- `OffshoreFarm.cs` — Offshore cage farming
- `PostSmoltFacility.cs` — Post-smolt production

### Finance (13 files, 3,571 lines)
- `CompetitorAI.cs` (895 lines) — 8 AI competitors with strategies
- `EmpireProgression.cs` (669 lines) — SmallFarmer → GlobalTitan phases
- `StockMarket.cs` (693 lines) — IPO, share price, market cap
- `InvestmentManager.cs` (800 lines) — Investor relations, PE backing
- `FinanceManager.cs` — P&L, balance sheet, cash flow

### Health (6 files, 1,354 lines)
- `IntegratedDiseaseSystem.cs` — SIR epidemiological model
- `StochasticDiseaseEngine.cs` — Probabilistic disease outbreaks
- `DiseaseLODSystem.cs` — Level-of-detail for performance

### Logistics (7 files, 301 lines)
- Route planning, truck/wellboat fleet, inventory, suppliers

### Market (9 files, 2,053 lines)
- `MarketSimulation.cs` — Dynamic salmon pricing with seasonal cycles
- `ContractSystem.cs` — Buyer contracts with fulfillment tracking
- `QualityGrade.cs` — Superior/Ordinary grading system
- `MarketIntelligence.cs` — Market trend analysis
- `CurrencySystem.cs` — Multi-currency (NOK, EUR, GBP, JPY, USD)
- `MarketChannel.cs` — Export routes to different regions

### Processing (8 files, 2,090 lines)
- `ProductCatalog.cs` (573 lines) — All product types with margins
- `ProductMixOptimizer.cs` — Optimal product mix calculation
- `BrandReputation.cs` — Brand building, certifications, pricing power
- `ProcessingPlant.cs` — CAPEX/OPEX, production lines, yield

---

## Godot UI (GDScript — 25,749 lines)

### Core Systems
| Script | Lines | Purpose |
|--------|-------|---------|
| SimBridge.gd | ~1,250 | C#↔GDScript bridge, all game data access |
| GameController.gd | ~200 | Game loop, scene management |
| Main.gd | ~120 | Scene entry point |
| LakskongenTheme v2.0 | ~350 | Nordic Minimalist palette + font loading |

### Major UI Panels (programmatic, no scene deps)
| Panel | Lines | Features |
|-------|-------|----------|
| Dashboard.gd | 799 | KPI strip, 6 pens, water, weather, finance, alerts, actions |
| MapPanel.gd | 1,032 | 13 Norwegian production zones, region selection |
| EncyclopediaPanel.gd | 905 | 51 entries, search, categories |
| EmpirePanel.gd | 895 | Empire phases, competitors, stock market, acquisitions |
| FinanceDeepPanel.gd | 862 | Loans, tax, investors, financial health |
| StakeholderPanel.gd | 659 | 6 stakeholder factions, demands, reputation |
| ModManagerPanel.gd | 580 | Mod loading, CSV export |
| PenManagement.gd | 552 | Pen list + detail (water, health, genetics, feed) |
| FinancePanel.gd | 542 | KPIs, chart, P&L breakdown, loans |
| ContractsPanel.gd | 525 | Market prices, buyers, contracts, regions |
| DataExportPanel.gd | 518 | Data export functionality |
| TechTreePanel.gd | 496 | 25-node visual tech tree, 5 branches |
| WeatherDiseasePanel.gd | 392 | Weather + disease monitoring |
| CrewPanel.gd | 391 | Employee cards, skills, morale |
| StockMarketPanel.gd | 378 | IPO, share price, dividends |
| ScenarioSelectPanel.gd | 368 | 5 scenarios with preview |
| SustainabilityPanel.gd | 332 | Certifications, ESG tracking |
| SettingsMenu.gd | 322 | Audio/Graphics/Gameplay/Accessibility tabs |
| ProductionChainPanel.gd | 321 | Processing chain visualization |
| HUD.gd | 311 | Top bar (day, season, balance, fish, temp, price, speed) |
| MainMenu.gd | 271 | Teal branding, wave animation, 4 buttons |
| GameUI.gd | 291 | Tab-based navigation, 14 tabs |

### Supporting Systems
- AudioController.gd — Weather-reactive ambient audio, crossfade
- MusicLibrary.gd — Seasonal music tracks
- ToastNotification.gd — Toast popup system
- JuiceEffects.gd — UI animations
- TutorialManager.gd — Data-driven tutorial
- AchievementManager.gd — 38 achievements
- ScenarioManager.gd — 5 scenarios

---

## Assets

### Audio (69 files)
- 12 ambient tracks (ocean, rain, wind, seagulls, storm, underwater, harbor, ice)
- 6 music tracks (seasonal: spring, summer, autumn, winter + main theme + calm sea)
- 6 SFX (achievement, disease alert, financial crisis, harvest, lice alert)
- 6 UI sounds (button click, hover, error, notification, panel open/close)

### Fonts (13 files)
- Inter (Regular, Medium, SemiBold, Bold) — headings
- JetBrains Mono (Regular, Medium) — numbers/data

### Textures (6 files, Gemini-generated)
- salmon-icon.png — Fish icon for UI
- lakskongen-logo.png — Game logo (2K)
- fjord-background.png — Aerial fjord with salmon pens (2K)
- pen-icon.png — Isometric salmon pen
- menu-background.png — Norwegian coastal village (2K)
- .gitkeep

### Shaders (5)
- water_shader.gdshader — Ocean water with waves
- water_wave_shader.gdshader — Wave displacement
- terrain_shader.gdshader — Height-based terrain coloring
- sky_shader.gdshader — Dynamic sky
- tilt_shift.gdshader — Camera tilt-shift effect

---

## Translations
7 languages: Norwegian (primary), English, German, French, Spanish, Japanese

---

## Design System
- **Theme:** Nordic Minimalist / Clean-Tech v2.0
- **Primary:** #0A4D68 (deep fjord teal)
- **Secondary:** #88C0D0 (Nordic ice blue)
- **Background:** #ECEFF4 (light gray-blue)
- **Surface:** #FFFFFF
- **Status:** Success #A3BE8C, Warning #EBCB8B, Danger #BF616A
- **Fonts:** Inter (headings), JetBrains Mono (data)
- **9 HTML prototypes** for all major screens

---

## Build Status
- **C# build:** ✅ 0 errors, 3 warnings
- **GDScript:** ✅ 0 parse errors
- **Godot version:** 4.3 stable mono
- **Export templates:** ⚠️ Need manual install
- **Git:** Clean, pushed to main
