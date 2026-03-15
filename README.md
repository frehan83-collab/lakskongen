# 🐟 Lakskongen — From Egg to Export

**A deep simulation game about salmon farming in the fjords of Norway.**

[![Built with Godot](https://img.shields.io/badge/Godot-4.3-blue.svg)](https://godotengine.org/)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: TBD](https://img.shields.io/badge/License-TBD-yellow.svg)]()

---

## 🎯 Vision

While **Farming Simulator 25** recently added salmon aquaculture as a surface-level feature, **Lakskongen** goes **100x deeper** — we are to salmon farming what iRacing is to Need for Speed.

Build your empire from a single net pen to a vertically integrated corporation. Manage real genetics, navigate Norwegian regulations, master global markets, and discover why Norway is the world's #1 salmon producer.

### Key Differentiators
- **Deep Biology:** Mendelian genetics with 8+ traits, inbreeding depression, genomic selection
- **Living Fjords:** Dynamic environment with temperature layers, currents, algal blooms, seasonal light cycles
- **Market Mastery:** Global salmon markets with contracts, quality grading, currency fluctuations
- **Regulatory Chess:** Real Norwegian traffic light system, MAB limits, sustainability ratings
- **Technology Evolution:** From 1970s wooden pens to AI-driven offshore Ocean Farms
- **Empire Building:** Vertical integration from hatchery to export markets

---

## ✨ Features

### 🧬 **Biology & Genetics**
- **Breeding System:** Select broodstock, track pedigrees, manage 8 heritable traits (growth rate, disease resistance, fillet quality, maturation timing, feed efficiency, color, stress tolerance, fillet yield)
- **Life Cycle Simulation:** Egg → Alevin → Fry → Parr → Smolt → Post-smolt → Adult → Broodstock
- **Population Dynamics:** Cohort-based simulation with statistical variance and mortality modeling
- **Disease & Parasites:** Sea lice, IPN, ISA, furunculosis with infection spread and treatment

### 🌊 **Environment & Operations**
- **Dynamic Zones:** 13 Norwegian production zones with unique characteristics
- **Water Chemistry:** Temperature, oxygen, salinity, currents affecting fish performance
- **Seasonal Cycles:** Midnight sun to polar night, spawning seasons, market peaks
- **Sea Site Management:** Feed scheduling, delousing, harvesting, biomass monitoring
- **Hatchery Operations:** Egg incubation, smolt production, vaccination programs

### 📊 **Economics & Markets**
- **Dynamic Pricing:** Seasonal peaks (Easter, Christmas), size premiums, quality bonuses
- **Export Markets:** Fresh whole fish, fillets, smoked products, sashimi-grade to Japan
- **Contract System:** Spot market vs. long-term contracts, price risk management
- **Financial Management:** Cash flow, EBIT/kg tracking, investment decisions
- **Vertical Integration:** Own feed mills, processing plants, logistics fleet

### ⚖️ **Regulations & Compliance**
- **Traffic Light System:** Green/yellow/red zones based on sea lice levels
- **MAB Limits:** Maximum Allowed Biomass per license
- **Environmental Monitoring:** Impact assessments, sustainability certifications
- **License Management:** Acquire, trade, and optimize production licenses

### 🔧 **Technology & Progression**
- **Tech Tree:** Unlock upgrades from basic nets to closed-containment systems
- **Equipment:** Feed barges, workboats, ROVs, laser delousers, processing lines
- **Automation:** AI feeding systems, automated counting, predictive analytics
- **Innovation:** Research and development for competitive advantage

### 🎮 **Gameplay Modes**
- **Campaign:** Historical scenarios from 1970s pioneers to modern challenges
- **Sandbox:** Full creative freedom with customizable difficulty
- **Multiplayer:** Co-op empire building and competitive markets *(planned)*

---

## 🛠️ Tech Stack

| Component | Technology |
|-----------|-----------|
| **Game Engine** | Godot 4.3+ |
| **Simulation Core** | C# (.NET 8.0) |
| **UI & Scripting** | GDScript + C# |
| **Data Storage** | JSON + SQLite |
| **Version Control** | Git |
| **Testing** | xUnit + Python pytest |

### Architecture Philosophy
- **Simulation Layer:** Pure C# — no Godot dependencies. Fully testable, high performance.
- **Presentation Layer:** Godot scenes for rendering, UI, and input handling.
- **Data Layer:** JSON for configuration, SQLite for save files and market history.
- **Mod Support:** Resource-based system for custom content *(planned)*.

---

## 🚀 Getting Started

### Prerequisites
- **.NET 8.0 SDK** — [Download](https://dotnet.microsoft.com/download)
- **Godot 4.3+ with .NET support** — [Download](https://godotengine.org/)
- **Git** — [Download](https://git-scm.com/)

### Build Instructions

#### Windows
```powershell
# Clone the repository
git clone https://github.com/yourusername/lakskongen.git
cd lakskongen

# Build the C# simulation core
dotnet build src/LakskongenSim.sln

# Open Godot project
cd godot
# Open project.godot in Godot Editor (4.3+)

# Run from editor or build export template
```

#### macOS / Linux
```bash
# Clone the repository
git clone https://github.com/yourusername/lakskongen.git
cd lakskongen

# Build the C# simulation core
dotnet build src/LakskongenSim.sln

# Open Godot project
cd godot
# Open project.godot in Godot Editor (4.3+)
```

### Running Tests

#### C# Unit Tests (xUnit)
```bash
cd src/LakskongenSim.Tests
dotnet test
```

#### Python Integration Tests (pytest)
```bash
cd tests
pip install -r requirements.txt  # If using Python tests
pytest -v
```

**Current Test Stats:**
- **108 C# source files**
- **21 test files**
- **14/14 core unit tests passing** (1 skipped — requires calibration data)
- **Performance benchmarks:** 100k fish simulation tested

---

## 📖 Documentation

### 📐 Architecture & Design
- **[Architecture Overview](docs/Architecture.md)** — Layered design, data flow, core systems
- **[Architecture Deep Dives](docs/Architecture_DeepDives.md)** — Cohort model, genetics, save format, performance, localization
- **[API Reference](docs/API_Reference.md)** — SimBridge, GameBridge, EventBus, SimulationEngine APIs
- **[Modding API](docs/Modding_API.md)** — Create custom content (species, maps, scenarios)

### 📊 System Diagrams
- **[Architecture Diagram](docs/diagrams/architecture-overview.md)** — Simulation ↔ Bridge ↔ Presentation layers
- **[Data Flow](docs/diagrams/data-flow.md)** — GameState → SimulationEngine → UI pipeline
- **[Biology Lifecycle](docs/diagrams/biology-lifecycle.md)** — Egg → Fry → Smolt → Adult → Harvest
- **[Disease Propagation](docs/diagrams/disease-propagation.md)** — SIR model + LOD tiers
- **[Market Pricing](docs/diagrams/market-pricing.md)** — Ornstein-Uhlenbeck process + seasonal curves

### 🎓 Tutorials
- **[Getting Started](docs/tutorials/getting-started.md)** — 5-minute quickstart
- **[Building from Source](docs/tutorials/building-from-source.md)** — Detailed build guide
- **[Running Tests](docs/tutorials/running-tests.md)** — Unit, integration, stress tests
- **[Creating Your First Mod](docs/tutorials/creating-your-first-mod.md)** — Step-by-step mod guide
- **[Contributing Code](docs/tutorials/contributing-code.md)** — Fork → PR workflow

### 📋 Project
- **[VISION.md](VISION.md)** — Design philosophy and market analysis
- **[GAMEPLAY_ROADMAP.md](GAMEPLAY_ROADMAP.md)** — Feature roadmap and industry research
- **[STATUS.md](STATUS.md)** — Current development status
- **[CONTRIBUTING.md](CONTRIBUTING.md)** — How to contribute
- **[CHANGELOG.md](CHANGELOG.md)** — Version history

---

## 🎨 Visual Style

**Stylized realism** — inspired by *Anno 1800* and *Northgard*. Clean, atmospheric 3D with:
- Volumetric fog over Norwegian fjords
- Tilt-shift miniature aesthetic
- Dynamic weather and seasonal color palettes
- Northern lights and midnight sun
- Satisfying fish-jumping and processing animations

---

## 🔊 Audio

- **Ambient soundscapes:** Ocean waves, seagulls, wind, rain on metal roofs
- **Soundtrack:** Norwegian folk-inspired (Wardruna meets Sigur Rós)
- **Dynamic layers:** Peaceful exploration → tense crisis → triumphant harvest
- **ASMR elements:** Feeding splashes, processing machinery, ice cracking

---

## 🌍 Localization

Currently supported languages:
- **Norwegian** (Bokmål) — Primary
- **English** — Primary

Planned:
- German, French, Spanish, Japanese

---

## 🛣️ Development Roadmap

### ✅ Completed
- **Phase 1:** Core prototype with hatchery → sea → harvest loop
- **Sprint 9:** Advanced biology models, disease systems, sea lice mechanics
- **Sprint 10:** Performance optimization, stress testing, mortality debugging

### 🔄 In Progress
- **Wave 4 QC:** Final bug fixes and polish
- **Documentation:** README, CHANGELOG, CONTRIBUTING

### 📋 Next Up
- **Technology research tree** (4-6 weeks)
- **Sustainability certification system** (2-3 weeks)
- **Photo mode & visual milestones** (1-2 weeks)
- **Active competitor AI** (3-4 weeks)
- **Campaign scenarios** (5-6 weeks)

**Estimated Early Access Launch:** 5-6 months

See **[GAMEPLAY_ROADMAP.md](GAMEPLAY_ROADMAP.md)** for full feature breakdown.

---

## 🤝 Contributing

We welcome contributions! Whether you're a developer, fish farmer, artist, or salmon enthusiast, there's a place for you.

See **[CONTRIBUTING.md](CONTRIBUTING.md)** for:
- Code style guidelines (C# and GDScript)
- Commit message format
- Pull request process
- Testing requirements
- How to report bugs and request features

---

## 📜 License

**TBD** — License to be determined before public release.

---

## 🙏 Acknowledgments

- Norwegian Directorate of Fisheries (Fiskeridir) for aquaculture data
- Barentswatch for fish health monitoring integration
- Real-world salmon farmers for domain expertise validation
- Godot community for engine support
- OpenClaw AI team for development acceleration

---

## 📞 Contact

- **Website:** *TBD*
- **Discord:** *TBD*
- **Email:** *TBD*

---

**Built with passion for simulation, respect for the ocean, and Norwegian precision.**

*Lakskongen — From Egg to Export.* 🐟🇳🇴
