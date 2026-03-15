# Data Flow Diagram

## Game Loop: GameState → SimulationEngine → UI

```mermaid
sequenceDiagram
    participant Godot as Godot _process()
    participant GB as GameBridge.cs
    participant SE as SimulationEngine
    participant Sub as Subsystems
    participant EB as EventBus
    participant SB as SimBridge.gd
    participant UI as UI (GDScript)

    Godot->>GB: Timer timeout (1s at 1x speed)
    GB->>SE: AdvanceOneDay()

    rect rgb(46, 204, 113, 0.1)
        Note over SE,Sub: Simulation Tick (1 day)
        SE->>Sub: 1. Weather.Update(day)
        SE->>Sub: 2. Environment.Update(zones, weather)
        SE->>Sub: 3. Biology.Tick(populations, days=1)
        Note right of Sub: Growth (von Bertalanffy)<br/>Mortality (baseline + disease)
        SE->>Sub: 4. Health.Tick(populations, temperature)
        Note right of Sub: SIR disease spread<br/>Sea lice lifecycle<br/>LOD tier switching
        SE->>Sub: 5. Storyteller.Tick(day)
        Note right of Sub: Random events<br/>Norwegian events
        SE->>Sub: 6. Market.UpdateDay(day)
        Note right of Sub: O-U price process<br/>Seasonal adjustments
        SE->>Sub: 7. Finance.Calculate()
        SE->>Sub: 8. Sustainability.Update()
        SE->>Sub: 9. Analytics.RecordSnapshot()
        SE->>Sub: 10. Tutorial.CheckProgress()
    end

    SE-->>GB: C# events (OnDayAdvanced, OnEventFired, etc.)
    GB-->>EB: EmitSignal("DayAdvanced", day, season)
    GB->>GB: Refresh scalar properties
    GB-->>EB: EmitSignal("GameStateUpdated")
    EB-->>SB: Godot signal propagation
    SB->>SB: _refresh_snapshot()
    SB-->>UI: signal state_updated()
    UI->>SB: Read properties (balance, fish_count, etc.)
    UI->>UI: Update labels, graphs, notifications
```

## Save/Load Data Flow

```mermaid
flowchart LR
    subgraph Save
        GS[GameState] -->|"JSON serialize"| SLM[SaveLoadManager]
        SLM -->|"rotate backups"| Disk["save.json<br/>save.json.bak1<br/>save.json.bak2<br/>save.json.bak3"]
    end

    subgraph Load
        Disk2["save.json"] -->|"JSON deserialize"| SLM2[SaveLoadManager]
        SLM2 -->|"GameState object"| SE2[SimulationEngine]
        SE2 -->|"re-wire subsystems"| GB2[GameBridge]
    end
```

## GameState Serialization Schema

```json
{
  "Day": 905,
  "Balance": 15000000.0,
  "Zones": [ /* Zone objects */ ],
  "SeaSites": [ /* SeaSite objects */ ],
  "Populations": [ /* FishPopulation with Cohort[] */ ],
  "Broodstock": [ /* Genome objects */ ],
  "EventLog": [ /* EventRecord[] */ ],
  "DailySnapshots": [ /* analytics per day */ ],
  "UnlockedResearchIds": ["basic_feed", "vaccination"],
  "ActiveResearchId": "laser_delousing",
  "ResearchProgress": 12,
  "ExportMarkets": ["EU", "USA", "Japan"],
  "SustainabilityScore": 75,
  "SustainabilityCertification": "ASC",
  "UnlockedAchievementIds": ["first_harvest", "millionaire"]
}
```

## Subsystem Update Order

The update order in `SimulationEngine.AdvanceOneDay()` is critical for correctness:

| Order | System | Why This Order |
|-------|--------|---------------|
| 1 | Weather | Drives temperature/conditions for all other systems |
| 2 | Environment | Water chemistry depends on weather |
| 3 | Biology (Growth) | Growth depends on temperature, oxygen, feed |
| 4 | Biology (Mortality) | Must run after growth to avoid dead fish growing |
| 5 | Health (Disease) | SIR model uses current population counts |
| 6 | Health (Sea Lice) | Temperature-dependent lifecycle |
| 7 | Events/Storyteller | Can modify any system (storms, outbreaks) |
| 8 | Market | Price updates independent of biology |
| 9 | Finance | Revenue/expense calculation after all state changes |
| 10 | Sustainability | Weekly aggregate (every 7 days) |
| 11 | Analytics | Snapshot of final daily state |
| 12 | Tutorial | Check completion conditions |
