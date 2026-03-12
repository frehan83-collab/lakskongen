# Acquisition System - Lakskongen

## Overview

The Acquisition System allows players to expand their salmon farming empire by purchasing competitors, MAB licenses, processing plants, and distressed assets. This system models realistic Norwegian salmon industry M&A mechanics with authentic pricing and regulatory constraints.

## Core Mechanics

### Asset Types

1. **Fish Farms** - Include MAB licenses and production infrastructure
2. **Processing Plants** - Vertical integration opportunities
3. **Hatcheries** - Smolt production facilities
4. **Distressed Assets** - Post-disease outbreak or bankruptcy situations

### Valuation Model

#### Base Value Calculation
```
Total Value = Asset Value + License Value
```

Where:
- **Asset Value**: Physical infrastructure, equipment, real estate
- **License Value**: MAB (Maximum Allowed Biomass) capacity × NOK 170,300/tonne

#### Market Premium/Discount
```
Final Price = Base Value × Health Multiplier × Premium/Discount × Competition Multiplier
```

**Health Score Impact:**
- Health Multiplier = 0.7 + (Health Score × 0.6)
- Range: 0.7x (very poor health) to 1.3x (excellent health)

**Market Conditions:**
- **Healthy Companies**: +10% to +50% premium (random)
- **Distressed Companies**: -40% to -70% discount (random)

**Competition Factor:**
- Higher player market share = higher acquisition costs
- Competition Multiplier = 1.1 + (Player Market Share × 0.5)

### Offer Acceptance

Targets will accept offers based on:
- **Healthy Companies**: ≥85% of asking price
- **Distressed Companies**: ≥70% of asking price
- Random negotiation variance

### Regulatory Framework

#### Antitrust Review
- **Trigger**: Combined regional market share >30%
- **Review Time**: 6-18 months (random)
- **Risk**: Potential deal blocking or divestiture requirements

#### Standard Approval
- **Time**: 1-3 months for non-controversial deals
- **Requirements**: Basic due diligence and regulatory filing

## Implementation Details

### C# Classes

#### AcquisitionTarget
Represents a company or asset available for purchase.

**Key Properties:**
- `Id`: Unique identifier
- `Name`: Company name
- `AssetValue`: Infrastructure value (NOK millions)
- `LicenseValue`: MAB license value (NOK millions)
- `HealthScore`: 0-1 scale (disease history, financial health, infrastructure condition)
- `IsDistressed`: Boolean flag for special pricing
- `RegionalMarketShare`: 0-1 decimal percentage

#### AcquisitionSystem
Main business logic for M&A operations.

**Key Methods:**
- `GetAvailableTargets()`: Returns list of acquisition candidates
- `CalculateAcquisitionPrice()`: Computes realistic offer price
- `RequiresAntitrustReview()`: Checks if deal triggers regulatory review
- `MakeOffer()`: Processes acquisition attempt
- `CalculateIntegrationSynergies()`: Computes post-acquisition value

#### AcquisitionResult
Contains outcome of acquisition attempt.

**Properties:**
- `Success`: Boolean result
- `ActualPrice`: Final negotiated price
- `RegulatoryDelayMonths`: Time to complete deal
- `SynergiesNOKPerYear`: Annual value creation potential

### Integration Synergies

Synergies are calculated as:
```
Base Revenue = (Asset Value + License Value) × 0.15
Synergy % = 5% + (Player Integration Level × 15%)
Annual Synergies = Base Revenue × Synergy % × Health Score Modifier
```

**Processing Plant Bonus**: 1.5× synergy multiplier for vertical integration

## Game Balance

### Economic Constants

- **Standard MAB License**: 780 tonnes = NOK 132.8M
- **Small Processing Plant**: NOK 150-350M CAPEX
- **Distressed Asset Range**: 40-70% below fair value
- **Acquisition Premium Range**: 10-50% above fair value

### Player Progression

Early game players should focus on:
1. **Small distressed assets** - Lower entry cost, higher risk/reward
2. **Regional consolidation** - Build market position before expanding

Mid-game opportunities:
1. **Healthy mid-size competitors** - Steady cash flow, market share
2. **Processing plant integration** - Vertical synergies

Late-game strategies:
1. **Premium assets** - Market-leading positions
2. **Cross-regional expansion** - National presence

### Risk Factors

1. **Market Concentration**: High market share attracts regulatory scrutiny
2. **Integration Risk**: Poor health score targets may underperform
3. **Cash Flow**: Large acquisitions require significant capital
4. **Timing**: Regulatory delays can impact other opportunities

## Sample Targets

### Nordfjord Salmon AS
- **Type**: Healthy Fish Farm
- **MAB Capacity**: 780 tonnes
- **Health Score**: 85%
- **Asking Price**: NOK 250M
- **Strategy**: Solid acquisition for stable growth

### Sunnmøre Akva (Distressed)
- **Type**: Post-ISA Outbreak
- **MAB Capacity**: 585 tonnes  
- **Health Score**: 25%
- **Asking Price**: NOK 65M (55% discount)
- **Strategy**: Turnaround opportunity, high risk/reward

### Arctic Fish Processing AS
- **Type**: Processing Plant
- **Health Score**: 92%
- **Asking Price**: NOK 220M
- **Strategy**: Vertical integration, premium synergies

## Technical Integration

### Godot Integration
The `AcquisitionPanel.gd` script provides:
- Visual target listing with health/distress indicators
- Offer submission interface
- Antitrust warning system
- Real-time cash flow validation

### Event System
```gdscript
# Signals emitted by the acquisition panel
signal acquisition_offer_made(target_id: String, offer_amount: float)
signal acquisition_completed(target_id: String, final_price: float, synergies: float)
```

### Data Management
- Target data stored in `acquisition_targets.json`
- Dynamic loading and refresh capabilities
- Persistent state management for ongoing deals

## Future Enhancements

### Planned Features
1. **Due Diligence Mini-Game**: Risk discovery mechanics
2. **Integration Challenges**: Post-acquisition operational events
3. **Market Dynamics**: Dynamic pricing based on industry cycles
4. **Regulatory Politics**: Lobbying and political influence systems
5. **International Expansion**: Cross-border acquisition mechanics

### Balancing Considerations
- Monitor player acquisition velocity vs. game progression
- Adjust distressed asset spawn rates based on difficulty
- Scale antitrust thresholds for different game modes
- Consider seasonal pricing variations for realism

## Conclusion

The Acquisition System provides a realistic foundation for M&A gameplay in Lakskongen, balancing authentic Norwegian salmon industry economics with engaging strategic decisions. Players must carefully consider timing, financing, regulatory risk, and integration capabilities to build a successful salmon farming empire.