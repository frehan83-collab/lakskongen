# 🐟 Lakskongen — From Egg to Export

**A deep simulation game about salmon farming in the fjords of Norway.**

[![Built with Godot](https://img.shields.io/badge/Godot-4.3-blue.svg)](https://godotengine.org/)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Status: Playable Alpha](https://img.shields.io/badge/Status-Playable%20Alpha-brightgreen.svg)]()
[![Tests: 203/203](https://img.shields.io/badge/Tests-203%2F203%20passing-success.svg)]()
[![License: TBD](https://img.shields.io/badge/License-TBD-yellow.svg)]()

---

## 🎮 Project Status: PLAYABLE ALPHA (v0.5.0-alpha)

Lakskongen is now **playable** with a full game loop, 3D fjord visuals, tutorial system, and deep simulation mechanics. The game runs from egg to export with environmental challenges, financial strategy, and Norwegian coastal aesthetics.

**Current milestone:** QA testing → Playtesting → Early Access prep

---

## 🌊 Vision

While **Farming Simulator 25** recently added salmon aquaculture as a surface-level feature, **Lakskongen** goes **100x deeper** — we are to salmon farming what iRacing is to Need for Speed.

Build your empire from a single net pen to a vertically integrated corporation. Manage real genetics, navigate Norwegian regulations, master global markets, and discover why Norway is the world's #1 salmon producer.

---

## 🕹️ Quick Start

### Prerequisites
- **.NET 8.0 SDK** — [Download](https://dotnet.microsoft.com/download)
- **Godot 4.3+ with .NET support** — [Download](https://godotengine.org/)
- **Git** — [Download](https://git-scm.com/)

### Run the Game
```powershell
# Clone
git clone https://github.com/yourusername/lakskongen.git
cd lakskongen

# Build C# simulation core
dotnet build src/LakskongenSim.sln

# Open in Godot 4.3+ (.NET edition)
# Open godot/project.godot → Press F5 to play
```

### Controls
| Key | Action |
|-----|--------|
| **WASD / Arrow Keys** | Pan camera |
| **Mouse Wheel** | Zoom in/out |
| **Space / P** | Pause/Resume |
| **1-4** | Time speed (1x, 5x, 10x, 30x) |
| **F5** | Quick Save |
| **F9** | Quick Load |
| **Tab** | Toggle Dashboard |
| **Esc** | Menu |

---

## 🐟 Features

### 🧬 Biology & Genetics
- 8-trait Mendelian genetics (growth rate, disease resistance, fillet quality, maturation, feed efficiency, color, stress tolerance, fillet yield)
- Full life cycle: Egg → Alevin → Fry → Parr → Smolt → Post-smolt → Adult → Broodstock
- Cohort-based population dynamics with statistical variance
- Inbreeding depression and genomic selection

### 🏔️ 3D Fjord Renderer *(NEW — Wave 10)*
- Norwegian coastal terrain with fjord geometry
- 11 custom shaders (water, sky, terrain, tilt-shift, weather effects)
- Dynamic weather visualization (storms, fog, northern lights)
- Anno 1800-tier visual aesthetic at 60 FPS target
- Camera system with smooth pan, zoom, and rotation

### 🖥️ Complete UI System *(NEW — Wave 10)*
- **Dashboard:** Real-time KPIs, biomass charts, financial overview
- **Pen Management:** Stock, feed, harvest, monitor individual pens
- **Save/Load:** Multiple save slots with metadata preview
- **Game Loop:** Full pause, speed control, day/week/month progression

### 📚 Tutorial System *(NEW — Wave 10)*
- 23-step interactive tutorial with progress tracking
- Contextual hints and guidance
- Progressive feature unlocking for new players

### 🌦️ Weather & Environment *(NEW — Wave 11)*
- Weather impacts on fish health and operations
- Storm events affecting feeding and mortality
- Temperature stress modeling
- Seasonal light cycles (midnight sun, polar night)

### 🦠 Disease System *(NEW — Wave 11)*
- Disease outbreak events with spread mechanics
- Treatment options and costs
- Sea lice, IPN, ISA, furunculosis models
- Adaptive LOD for performance scaling

### 📊 Financial Strategy *(NEW — Wave 11)*
- Contract system (forward contracts vs spot market)
- IPO/Stock market mechanics (company valuation, dividends)
- Dynamic seasonal pricing with global market simulation
- EBIT/kg tracking, cash flow management

### 🎯 Scenarios *(NEW — Wave 12)*
- **Small Farm Start:** Classic beginner experience
- **Industrial Giant:** Large-scale operations challenge
- **Organic Pioneer:** Sustainability-focused gameplay
- **Research Station:** Science and breeding focus
- **Family Inheritance:** Story-driven management

### 🏗️ Operations & Production
- 17-product processing chain (fresh, frozen, smoked, sashimi-grade)
- Hatchery → Sea site → Processing → Export pipeline
- Feed management with FCR optimization
- Vertical integration opportunities

### 📜 Regulations
- Norwegian traffic light system (green/yellow/red zones)
- MAB (Maximum Allowed Biomass) limits
- Environmental monitoring and sustainability ratings
- License management

---

## 🧪 Running Tests

```bash
# All tests (203 tests)
dotnet test src/LakskongenSim.sln --configuration Release

# Specific test projects
dotnet test src/LakskongenSim.Tests/
dotnet test src/LakskongenSim.LogisticsTests/
dotnet test src/LakskongenSim.SaveLoadTests/
dotnet test tests/
```

**Current test stats:** 203/203 passing, 0 failures, 0 errors

---

## 📦 Building & Exporting

### Build from Source
```powershell
# Debug build
dotnet build src/LakskongenSim.sln

# Release build
dotnet build src/LakskongenSim.sln --configuration Release
```

### Export Windows EXE
1. Open `godot/project.godot` in Godot 4.3+ (.NET edition)
2. Project → Export → Windows Desktop
3. Export presets configured in `godot/export_presets.cfg`
4. Output: `godot/export/Lakskongen.exe`

---

## 🏗️ Architecture

| Component | Technology |
|-----------|-----------|
| **Game Engine** | Godot 4.3+ |
| **Simulation Core** | C# (.NET 8.0) |
| **UI & Scripting** | GDScript + C# |
| **Shaders** | Godot Shader Language (11 custom) |
| **Data Storage** | JSON + SQLite |
| **Testing** | xUnit (203 tests) |

### Design Philosophy
- **Simulation Layer:** Pure C# — no Godot dependencies, fully testable, high performance
- **Presentation Layer:** Godot scenes + GDScript for rendering, UI, input
- **Bridge Layer:** SimBridge.gd ↔ GameBridge.cs for C#/GDScript interop
- **Mod Support:** Resource-based system for custom content *(planned)*

---

## 📁 Project Structure

```
lakskongen/
├── godot/                    # Godot project
│   ├── scenes/               # .tscn scene files
│   │   ├── environment/      # 3D fjord renderer scenes
│   │   └── ui/               # UI scenes
│   ├── scripts/              # GDScript files
│   │   ├── autoload/         # Autoload singletons (GameBridge.cs)
│   │   ├── environment/      # Camera, weather, terrain scripts
│   │   └── ui/               # UI panel scripts
│   ├── shaders/              # Custom shader files (11)
│   └── ui/                   # Additional UI resources
├── src/                      # C# simulation core
│   ├── LakskongenSim/        # Main simulation library
│   │   ├── Biology/          # Fish genetics, growth, lifecycle
│   │   ├── Core/             # SimulationEngine, GameState
│   │   ├── Disease/          # Disease models, LOD system
│   │   ├── Environment/      # Zones, water chemistry
│   │   ├── Events/           # Storyteller, event system
│   │   ├── Finance/          # Accounting, cash flow
│   │   ├── Market/           # Pricing, contracts, intelligence
│   │   └── Processing/       # VAP chain, yield calculation
│   ├── LakskongenSim.Tests/  # Core unit tests
│   ├── LakskongenSim.LogisticsTests/
│   └── LakskongenSim.SaveLoadTests/
├── tests/                    # Additional test projects
├── docs/                     # Documentation
├── screenshots/              # Store assets, capsules
├── CHANGELOG.md
├── README.md
└── VISION.md
```

---

## 🎨 Visual Style

**Stylized realism** — inspired by *Anno 1800* and *Northgard*:
- Norwegian fjord terrain with volumetric fog
- Tilt-shift miniature aesthetic
- Dynamic weather (storms, fog, northern lights, midnight sun)
- Custom water shader with reflections and foam
- Satisfying fish-jumping and processing animations

---

## 📖 Documentation

- **[Architecture Overview](docs/Architecture.md)** — System design and data flow
- **[API Reference](docs/API_Reference.md)** — SimBridge, GameBridge, EventBus APIs
- **[VISION.md](VISION.md)** — Design philosophy and market analysis
- **[GAMEPLAY_ROADMAP.md](GAMEPLAY_ROADMAP.md)** — Feature roadmap
- **[CHANGELOG.md](CHANGELOG.md)** — Version history
- **[CONTRIBUTING.md](CONTRIBUTING.md)** — How to contribute

---

## 🗺️ Development Roadmap

### ✅ Completed
- **Phase 1:** Core simulation (biology, genetics, disease, market, processing)
- **Sprint 9:** Advanced biology, disease LOD, market dynamics
- **Sprint 10:** Performance optimization, localization, stress testing
- **Wave 9:** Market intelligence, VAP processing, commercial gameplay
- **Wave 10:** Complete UI, 3D fjord renderer, tutorial system
- **Wave 11:** Weather impacts, disease events, contracts, IPO/stock market
- **Wave 12:** 5 scenarios, Windows export configuration

### 🔜 Next Up
- QA testing and playtesting
- Steam store page preparation
- Technology research tree
- Active competitor AI
- Campaign mode
- Multiplayer co-op *(planned)*

**Estimated Early Access Launch:** Q3 2026

---

## 🐛 Known Issues

- Some UI panels use placeholder data pending full SimBridge integration
- Weather system visual effects are shader-based approximations
- Stock market prices use simplified model (full integration pending)
- Tutorial may skip steps if game is advanced manually during guided sections
- Export templates need Godot .NET export templates installed separately

---

## 🤝 Contributing

We welcome contributions! See **[CONTRIBUTING.md](CONTRIBUTING.md)** for guidelines.

---

## 📜 License

**TBD** — License to be determined before public release.

---

## 🙏 Acknowledgments

- Norwegian Directorate of Fisheries (Fiskeridir) for aquaculture data
- Barentswatch for fish health monitoring integration
- Real-world salmon farmers for domain expertise validation
- Godot community for engine support

---

**Built with passion for simulation, respect for the ocean, and Norwegian precision.**

*Lakskongen — From Egg to Export.* 🐟🌊🇳🇴
