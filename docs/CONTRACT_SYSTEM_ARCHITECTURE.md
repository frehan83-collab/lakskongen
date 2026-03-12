# Contract System Architecture

## Overview

The Lakskongen contract system uses a **dual-layer architecture** separating simulation logic from game/UI concerns. This enables clean testing of business logic while maintaining rich gameplay features.

## System Layers

### Layer 1: C# Simulation Core (`Market/ContractSystem.cs`)

**Purpose:** Authoritative business logic for contract economics

**Responsibilities:**
- Contract workflow (propose → accept/decline → fulfill)
- Payment calculations based on quantity and price
- Penalty calculations for unfulfilled contracts
- Quality grade handling (Standard, ASC, Premium)
- Contract lifecycle management

**Contract Types (Economics):**
```csharp
public enum ContractType
{
    Spot,       // Immediate sale at market price
    Tolling,    // Processing agreement
    Exclusive,  // Locked buyer relationship
    Forward     // Future delivery at fixed price
}
```

**Key Methods:**
- `ProposeContract()` - Generate offer for negotiation
- `AcceptOffer()` - Convert offer to binding contract
- `FulfillContract()` - Process delivery and calculate payment/penalty
- `RemoveContract()` - Clean up completed/cancelled contracts

### Layer 2: GDScript Game Wrapper (`game/scripts/ContractSystem.gd`)

**Purpose:** Gameplay features, UI integration, template system

**Responsibilities:**
- Loading contract templates from JSON data
- Evaluating contracts for player guidance (ROI, risk scoring)
- Managing reputation effects
- Competitor lockout mechanics
- Exclusivity radius enforcement
- Contract expiration and tick updates

**Contract Types (Gameplay):**
```gdscript
enum ContractType {
    EXCLUSIVE_SUPPLIER,  # Lock down feed/smolt supplier
    LONG_TERM_BUYER,     # Guaranteed buyer at fixed price
    JOINT_VENTURE,       # Partnership for shared operations
    LICENSE_DEAL,        # MAB license lease/partnership
    EXPORT_AGREEMENT     # International market access
}
```

**Key Methods:**
- `generate_available_contracts()` - Filter templates by player state
- `evaluate_contract()` - Calculate ROI, risk, exclusivity value
- `sign_contract()` - Create contract from template
- `tick()` - Process contract expirations
- `breach_contract()` - Handle contract violations and reputation

## Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                        GAME LAYER (GDScript)                     │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │   Template   │───▶│   Contract   │───▶│     UI       │       │
│  │    Loader    │    │   Evaluator  │    │    Panels    │       │
│  └──────────────┘    └──────────────┘    └──────────────┘       │
│          │                                      │                │
│          ▼                                      ▼                │
│  ┌──────────────────────────────────────────────────────┐       │
│  │              ContractSystem.gd                        │       │
│  │  - Reputation management                              │       │
│  │  - Exclusivity mechanics                              │       │
│  │  - Tick processing                                    │       │
│  └──────────────────────────────────────────────────────┘       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Godot C# Interop
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    SIMULATION LAYER (C#)                         │
│  ┌──────────────────────────────────────────────────────┐       │
│  │              ContractSystem.cs                        │       │
│  │  - Payment calculations                               │       │
│  │  - Penalty logic                                      │       │
│  │  - Quality grade adjustments                          │       │
│  │  - Contract state management                          │       │
│  └──────────────────────────────────────────────────────┘       │
│          │                                                       │
│          ▼                                                       │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │   Finance    │◀──▶│    Market    │◀──▶│   Player     │       │
│  │   Manager    │    │    Prices    │    │  Inventory   │       │
│  └──────────────┘    └──────────────┘    └──────────────┘       │
└─────────────────────────────────────────────────────────────────┘
```

## Type Mapping

| GDScript Type (Gameplay) | C# Type (Economics) | Notes |
|--------------------------|---------------------|-------|
| LONG_TERM_BUYER          | Forward             | Fixed-price future delivery |
| EXCLUSIVE_SUPPLIER       | Exclusive           | Locked supplier relationship |
| EXPORT_AGREEMENT         | Spot/Forward        | Market-dependent |
| JOINT_VENTURE            | (Custom handling)   | Partnership mechanics |
| LICENSE_DEAL             | (Custom handling)   | MAB-specific |

## Integration Points

### 1. Contract Signing
When player signs a contract in GDScript:
1. GDScript validates production capacity
2. GDScript calls C# `AcceptOffer()` with economic terms
3. C# creates contract record with unique ID
4. GDScript stores additional gameplay metadata (reputation, lockout)

### 2. Contract Fulfillment
When player delivers salmon:
1. GDScript triggers delivery event
2. C# `FulfillContract()` calculates payment and penalties
3. C# updates contract state
4. GDScript updates UI and triggers gameplay effects

### 3. Contract Evaluation
When player views available contracts:
1. GDScript loads templates and filters by conditions
2. GDScript calculates gameplay metrics (ROI, risk)
3. C# can be queried for price projections if needed
4. GDScript renders contract cards in UI

## Testing Strategy

| Layer | Test Type | Location |
|-------|-----------|----------|
| C# ContractSystem | Unit tests | `tests/ContractTests.cs` |
| GDScript ContractSystem | Integration tests | Godot test runner |
| Full flow | End-to-end | Game scenario tests |

## Future Considerations

1. **Real-time price feeds:** C# layer could integrate Nasdaq Salmon Index
2. **AI contract negotiation:** C# handles negotiation logic, GDScript renders
3. **Multiplayer contracts:** C# validates, GDScript syncs state
4. **Contract templates v2:** JSON schema expansion for new contract types
