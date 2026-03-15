# Architecture Overview Diagram

## Layered Architecture (Simulation ↔ Presentation ↔ Bridge)

```mermaid
graph TB
    subgraph Presentation["🎮 Presentation Layer (Godot 4.3)"]
        UI["UI Controllers<br/>(GDScript)"]
        Scenes["Scenes (.tscn)<br/>MainMenu · GameWorld · HUD"]
        Assets["Assets<br/>Sprites · Audio · Theme"]
        SimBridgeGD["SimBridge.gd<br/>(GDScript wrapper)"]
    end

    subgraph Bridge["🔗 Bridge Layer (C# Autoloads)"]
        GB["GameBridge.cs<br/>Lifecycle + Tick Pump"]
        EB["EventBus.cs<br/>Signal Dispatcher"]
        SLM["SaveLoadManager.cs<br/>Godot ↔ Disk"]
        AM["AudioManager.cs<br/>SFX + Music"]
    end

    subgraph Simulation["⚙️ Simulation Layer (Pure C# · .NET 8)"]
        SE["SimulationEngine<br/>Central Orchestrator"]
        Bio["Biology<br/>FishPopulation · Genetics<br/>GrowthModel · MortalityModel"]
        Health["Health<br/>DiseaseSystem (SIR)<br/>SeaLiceModel · LOD"]
        Env["Environment<br/>Zones · WaterChemistry<br/>WeatherSystem"]
        Econ["Economics<br/>MarketSimulation (O-U)<br/>ContractSystem · Finance"]
        Farm["Farm<br/>Hatchery · SeaSite<br/>FeedSystem · Delousing"]
        Events["Events<br/>Storyteller · NorwegianEvents<br/>CrisisCapitalism"]
        Sust["Sustainability<br/>SustainabilityManager"]
    end

    subgraph Data["💾 Data Layer"]
        JSON["JSON Config<br/>species · zones · tech tree"]
        Saves["Save Files (.json)<br/>+ Backup Rotation"]
        ExtData["External Data<br/>BarentsWatch · Aquaculture API"]
    end

    UI --> SimBridgeGD
    SimBridgeGD --> GB
    GB --> SE
    SE --> Bio
    SE --> Health
    SE --> Env
    SE --> Econ
    SE --> Farm
    SE --> Events
    SE --> Sust
    SE -.->|"C# events"| GB
    GB -.->|"Godot signals"| EB
    EB -.->|"signal dispatch"| UI
    SLM --> Saves
    SE --> Data
    ExtData -.->|"calibration"| SE

    style Presentation fill:#4a9eff,color:#fff
    style Bridge fill:#ff9f43,color:#fff
    style Simulation fill:#2ecc71,color:#fff
    style Data fill:#9b59b6,color:#fff
```

## Key Design Principles

| Principle | Implementation |
|-----------|---------------|
| **No Godot in Simulation** | `LakskongenSim` has zero Godot references — fully testable |
| **Signal-Based Decoupling** | SimulationEngine → C# events → Godot signals → UI |
| **Cohort Performance** | Fish grouped by age/genetics, not individual agents |
| **Adaptive LOD** | Disease detail scales with fish count and infection status |
| **Pure C# Testing** | xUnit tests run without Godot — fast CI pipeline |

## Communication Flow

```
GDScript UI ──read──→ SimBridge.gd ──read──→ GameBridge.cs ──read──→ SimulationEngine
                                                                          │
                                                                     AdvanceOneDay()
                                                                          │
SimulationEngine ──C# event──→ GameBridge ──Godot signal──→ EventBus ──signal──→ UI
```
