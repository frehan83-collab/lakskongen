# Disease Propagation System

## SIR Model + LOD Tiers

```mermaid
stateDiagram-v2
    direction LR
    [*] --> Susceptible
    Susceptible --> Infected: β × S × I / N<br/>(density-dependent)
    Infected --> Recovered: γ (recovery rate)
    Infected --> Dead: μ (disease mortality)
    Recovered --> Susceptible: Waning immunity<br/>(future feature)

    note right of Susceptible
        All healthy fish
        Modified by:
        - Sea lice load (+β)
        - Genetics (-β)
        - Vaccination (-β)
    end note

    note right of Infected
        Prevalence tracked per population
        Extra mortality: 0.2% per day
        at 100% prevalence
    end note
```

## Disease System Architecture

```mermaid
flowchart TB
    subgraph Orchestrator["IntegratedDiseaseSystem"]
        IDS["Coordinates all disease subsystems"]
    end

    subgraph SIR["DiseaseSystem (SIR)"]
        Beta["β = 0.20 (transmission)"]
        Gamma["γ = 0.05 (recovery)"]
        Mu["μ = 0.002 (extra mortality/day)"]
    end

    subgraph Lice["SeaLiceModel"]
        Nauplii["Nauplii (planktonic)"]
        Copepodid["Copepodid (infectious)"]
        Chalimus["Chalimus (attached)"]
        PreAdult["Pre-adult"]
        AdultLice["Adult (reproducing)"]
        Nauplii --> Copepodid --> Chalimus --> PreAdult --> AdultLice
        AdultLice -->|"eggs"| Nauplii
    end

    subgraph LOD["DiseaseLODSystem"]
        High["🔴 High Detail<br/>Individual agent-based<br/>≤1000 fish"]
        Medium["🟡 Medium Detail<br/>Zone-level SIR<br/>prevalence > 0.1%"]
        Low["🟢 Low Detail<br/>Population-level stats<br/>prevalence < 0.1%"]
    end

    subgraph Stochastic["StochasticDiseaseEngine"]
        SDE["Monte Carlo sampling<br/>Event-driven outbreaks"]
    end

    IDS --> SIR
    IDS --> Lice
    IDS --> LOD
    LOD --> Stochastic
    Lice -.->|"lice load increases β"| SIR

    style LOD fill:#f39c12,color:#fff
    style SIR fill:#e74c3c,color:#fff
    style Lice fill:#3498db,color:#fff
```

## LOD Tier Switching

```mermaid
flowchart LR
    A["New population<br/>0 infections"] -->|"default"| Low
    Low -->|"prevalence > 0.1%"| Medium
    Medium -->|"prevalence > 1%<br/>AND fish ≤ 1000"| High
    High -->|"prevalence < 1%<br/>OR fish > 1000"| Medium
    Medium -->|"prevalence < 0.1%"| Low

    Low["🟢 LOW<br/>Stats only<br/>~0.1ms/tick"]
    Medium["🟡 MEDIUM<br/>Zone SIR<br/>~2ms/tick"]
    High["🔴 HIGH<br/>Agent-based<br/>~10ms/tick"]

    style Low fill:#2ecc71,color:#fff
    style Medium fill:#f39c12,color:#fff
    style High fill:#e74c3c,color:#fff
```

## SIR Parameters

| Parameter | Symbol | Default | Description |
|-----------|--------|---------|-------------|
| Transmission rate | β | 0.20 | Base daily transmission coefficient |
| Recovery rate | γ | 0.05 | Daily recovery probability |
| Disease mortality | μ | 0.002 | Extra daily mortality at 100% prevalence |
| Lice susceptibility | - | 0.02 | β multiplier per lice/fish |
| Treatment temp sensitivity | - | -0.01 | Efficacy drop per °C above 10°C |

## Diseases Modeled

| Disease | Type | Transmission | Severity |
|---------|------|-------------|----------|
| **IPN** | Viral | Vertical + horizontal | High mortality in fry |
| **ISA** | Viral | Waterborne | Catastrophic (mandatory cull) |
| **Furunculosis** | Bacterial | Direct contact | Treatable with antibiotics |
| **Sea Lice** | Parasite | Environmental drift | Chronic welfare + regulatory |

## Interaction: Sea Lice → Disease

```
High lice load (>0.5 lice/fish)
    → Skin lesions → Open wounds
    → β_effective = β × (1 + lice_count × 0.02)
    → Increased disease susceptibility
    → Treatment decision:
        Chemical (delousing bath)
        Mechanical (warm water flush)
        Biological (cleaner fish)
        Each has cost, efficacy, resistance risk
```

## Mortality Prevention: Rate Injection Pattern

To prevent double-counting disease mortality:

```
DiseaseSystem.Tick()
    ├── Calculates extra mortality rate from infection
    ├── Injects rate into MortalityModel (NOT direct fish removal)
    └── MortalityModel.ApplyMortality() applies total mortality once

This ensures:
  Total mortality = baseline_rate + disease_rate + stress_rate
  Applied ONCE per tick, not per disease subsystem
```
