# Contract Warfare System - Design Document

*Strategic contract mechanics for Lakskongen salmon farming simulator*

## Overview

The Contract Warfare System enables players to secure competitive advantages through exclusive deals, long-term buyer relationships, joint ventures, and strategic partnerships. Inspired by Anno 1800's Docklands DLC and Capitalism II's contract mechanics, it adds depth to empire building beyond pure production optimization.

## Core Mechanics

### Contract Types

#### 1. **Exclusive Supplier Contracts**
Lock down critical inputs (feed, smolt) from major suppliers.

**Strategic Value:**
- Cost reduction: 6-8% typical
- Supply security: Guaranteed availability
- Competitor lockout: Exclusivity radius prevents competitor access
- Quality consistency: Single-source reduces variance

**Risk:**
- Lock-in: Cannot switch suppliers during contract period
- Geographic constraint: May limit expansion outside radius
- Price volatility: Fixed price may become unfavorable

**Example:**
- Mowi Feed AS: 36 months, 150K kg/month, 8% cost reduction, 150km exclusivity
- BioMar Norway AS: 24 months, 100K kg/month, 6% cost reduction, 100km exclusivity

#### 2. **Long-term Buyer Contracts**
Guaranteed buyers at fixed (premium) prices.

**Strategic Value:**
- Revenue stability: Eliminates spot market volatility
- Price premium: 5-15% above baseline
- Planning certainty: Known revenue for expansion decisions
- Market access: Entry to premium retail channels

**Risk:**
- Volume commitment: Must deliver or face penalties
- Missed upside: Can't capitalize on price spikes
- Quality requirements: Strict standards or breach

**Example:**
- LIDL International: 24 months, 80K kg/month, 7% premium, quality certification required
- Tesco UK: 18 months, 65K kg/month, 5% premium, UK market access

#### 3. **Joint Ventures**
Strategic partnerships for shared capabilities.

**Strategic Value:**
- Technology access: Genetics, processing, R&D
- Shared costs/risks: CAPEX distribution
- Synergy bonuses: Combined operational advantages
- Long-term alignment: Strategic rather than transactional

**Risk:**
- Long commitment: Typically 4-5 years
- Partner dependency: Relies on partner performance
- Governance complexity: Shared decision-making

**Example:**
- Trondheim Genetics Lab: 60 months, 12% quality premium, superior genetics access
- Tromsø Processing Group: 48 months, 15% processing cost reduction, ASC certification

#### 4. **License Deals**
MAB license leasing or partnerships.

**Strategic Value:**
- Expansion without CAPEX: Lease rather than buy licenses
- Competitor blocking: Deny licenses to rivals
- Distressed opportunities: Acquire from struggling companies
- Geographic control: Exclusivity zones

**Risk:**
- Temporary control: Lease expires
- Partner recovery: Distressed companies may regain strength
- Regulatory scrutiny: Lease deals may face review

**Example:**
- Hardanger Seafarms (distressed): 24 months, 2,400 tonnes MAB, 50km exclusivity
- Møre Ekspansjon (partnership): 12 months, shared MAB, intelligence gathering

#### 5. **Export Agreements**
International premium market access.

**Strategic Value:**
- Premium pricing: 15-35% above Norwegian baseline
- Market diversification: Reduce domestic dependency
- Brand building: Association with premium retailers
- Currency advantages: NOK/foreign exchange plays

**Risk:**
- Quality thresholds: ASC/organic certification required
- Volume consistency: High standards enforcement
- Logistics complexity: International cold chain
- Reputation dependency: Must maintain 0.85+ reputation

**Example:**
- Tokyo Premium Seafood: 36 months, 50K kg/month, 29% premium, ASC required
- Whole Foods USA: 30 months, 35K kg/month, 24% premium, organic only

## Economic Modeling

### Contract Evaluation Formula

```python
def evaluate_contract(contract):
    # Annual revenue/savings
    monthly_value = volume_kg * (price_premium_nok_per_kg + cost_reduction_nok_per_kg)
    annual_value = monthly_value * 12
    
    # Total benefit over contract duration
    total_benefit = annual_value * (duration_months / 12)
    
    # Risk adjustments
    risk_score = 0.0
    if exclusivity_radius > 0:
        risk_score += 0.3  # Lock-in risk
    if duration_months > 24:
        risk_score += 0.2  # Long commitment
    if price < market_baseline:
        risk_score += 0.4  # Below-market risk
    
    # Exclusivity value (market control)
    exclusivity_value = 0.0
    if exclusive:
        # Competitor lockout worth 25% of contract value
        exclusivity_value = annual_value * 0.25
    
    # ROI calculation
    roi_annual = (annual_value + exclusivity_value) / (duration_months / 12)
    
    return {
        "roi_annual": roi_annual,
        "total_benefit": total_benefit,
        "risk_score": clamp(risk_score, 0, 1),
        "exclusivity_value": exclusivity_value
    }
```

### Cost Reduction Mechanics

Contracts reduce operational costs:
- **Feed contracts**: 6-8% cost reduction (feed = 45% of production cost → 2.7-3.6% total cost reduction)
- **Processing JVs**: 15% processing cost reduction (processing = 15% of cost → 2.25% total)
- **Vertically integrated contracts**: Stack multiple reductions (capped at 50% total)

### Revenue Premium Calculation

```python
def calculate_revenue(base_production_kg, contracts):
    baseline_price = 71.44  # NOK/kg Q1 2026
    
    # Apply contract premiums
    contract_revenue = 0.0
    for contract in contracts:
        if contract.type == LONG_TERM_BUYER:
            contract_revenue += contract.volume_kg * contract.price_nok_per_kg
    
    # Remaining production sold at spot
    contracted_volume = sum(c.volume_kg for c in contracts if c.type == LONG_TERM_BUYER)
    spot_volume = base_production_kg - contracted_volume
    spot_revenue = spot_volume * baseline_price
    
    return contract_revenue + spot_revenue
```

## Gameplay Balance

### Unlock Conditions

Contracts require:
1. **Minimum reputation**: 0.50-0.88 depending on contract tier
2. **Minimum production capacity**: Must handle volume commitments
3. **Certifications**: ASC/MSC for premium export contracts
4. **Capital reserves**: Some contracts require upfront fees (future mechanic)

### Competitor Lockout Mechanics

**Exclusivity Radius:**
- Player signs exclusive feed contract with 150km radius
- Competitors within 150km cannot access that supplier
- Forces competitors to:
  - Pay higher prices from alternative suppliers
  - Reduce production due to supply constraints
  - Relocate operations outside radius (expensive)

**Strategic Implications:**
- Aggressive players can choke competitor supply chains
- Defensive players use exclusivity to protect home regions
- Creates geographic competition for contract access

### Contract Breach Consequences

Breaking a contract:
- **Reputation penalty**: -15% reputation
- **Financial penalty**: Forfeit remaining contract value
- **Lockout period**: Cannot sign new contracts with that counterparty for 24 months
- **Industry reputation**: Other potential partners increase risk premium

### Portfolio Optimization

Players balance:
- **Stability vs. Upside**: Long-term contracts reduce volatility but cap gains
- **Diversification**: Multiple buyers reduce dependency risk
- **Vertical integration**: Own operations vs. contract partnerships
- **Geographic spread**: Exclusivity zones vs. expansion flexibility

## Integration with Other Systems

### Financial System
- Contract revenue counted in monthly cash flow
- Cost reductions applied to production expenses
- Contract obligations tracked in financial dashboard

### Reputation System
- Contract performance affects reputation
- High-reputation unlocks premium contracts
- Breach penalties damage long-term opportunities

### Regulatory System
- Exclusive contracts may trigger antitrust review
- Export agreements require compliance with international standards
- Joint ventures subject to regulatory approval in some regions

### Market Dynamics
- Player contracts affect competitor behavior
- AI competitors pursue their own contract strategies
- Market power through contract dominance

## UI/UX Design

### Contract Panel Layout

```
┌─────────────────────────────────────────┐
│  AVAILABLE CONTRACTS                    │
│  ┌─────────────────────────────────┐   │
│  │ Filter: [All Types ▼] Sort: [▼] │   │
│  ├─────────────────────────────────┤   │
│  │ LIDL - Long-term Buyer          │   │
│  │   Value: 18.4M NOK | Risk: 15%  │   │
│  ├─────────────────────────────────┤   │
│  │ Mowi Feed - Exclusive Supplier  │   │
│  │   Value: 23.8M NOK | Risk: 45%  │   │
│  └─────────────────────────────────┘   │
│                                         │
│  ACTIVE CONTRACTS                       │
│  ┌─────────────────────────────────┐   │
│  │ Tesco UK - Long-term Buyer      │   │
│  │   12 months left | 65K kg/month │   │
│  └─────────────────────────────────┘   │
│                                         │
│  DETAILS                                │
│  ┌─────────────────────────────────┐   │
│  │ LIDL International              │   │
│  │ Duration: 24 months             │   │
│  │ Volume: 80,000 kg/month         │   │
│  │ Price: 76.50 NOK/kg (7% prem.)  │   │
│  │                                 │   │
│  │ Annual ROI: 18.4M NOK           │   │
│  │ Risk Score: 15%                 │   │
│  │                                 │   │
│  │ [ Sign Contract ]               │   │
│  └─────────────────────────────────┘   │
└─────────────────────────────────────────┘
```

### Visual Indicators
- **Green**: Low risk, high ROI
- **Yellow**: Exclusivity warnings
- **Red**: High risk, capacity constraints
- **Lock icon**: Exclusive contracts
- **Trophy icon**: Premium/export contracts

## Strategic Scenarios

### Scenario 1: Feed Monopoly Play
**Setup:** Player controls 60% of regional production
**Strategy:** Sign exclusive feed contracts with all major suppliers in 200km radius
**Outcome:** Competitors forced to import feed at 20% markup, player gains cost advantage
**Counter:** Antitrust review triggered at 70% market share, regulators may void contracts

### Scenario 2: Premium Export Focus
**Setup:** Player has ASC certification and strong reputation
**Strategy:** Stack Japan + USA export contracts for 50% of production
**Outcome:** 25%+ revenue premium vs. competitors selling domestically
**Counter:** Volume commitments create risk if disease outbreak reduces production

### Scenario 3: Distressed Acquisition via Contracts
**Setup:** Competitor hit by ISA outbreak, desperate for cash
**Strategy:** Offer short-term license lease at favorable terms, then acquire company
**Outcome:** Gain control of licenses at discount, eliminate competitor
**Counter:** Competitor may recover if disease controlled quickly

### Scenario 4: Vertical Integration Through JVs
**Setup:** Player lacks processing capacity
**Strategy:** Joint venture with Tromsø Processing instead of CAPEX
**Outcome:** 15% cost reduction without 500M NOK processing plant investment
**Counter:** Dependent on JV partner, less control than ownership

## Future Enhancements

1. **Dynamic Contract Generation**: AI-generated contracts based on market conditions
2. **Renegotiation Mechanics**: Modify terms mid-contract with penalties
3. **Contract Trading**: Sell contracts to other players/AI
4. **Performance Bonuses**: Exceed volume targets for premium increases
5. **Multi-party Contracts**: 3-way deals (player + 2 competitors)
6. **Contract Derivatives**: Hedge against price risk with financial instruments
7. **Reputation-Based Pricing**: Dynamic premiums based on past performance
8. **Political Lobbying**: Influence contract regulations through government relations

## Data Sources & Realism

- **Feed costs**: 40-50% of production (industry standard)
- **Premium markets**: Japan pays 20-30% premium for Norwegian salmon
- **Contract durations**: 18-60 months typical for major buyers
- **Exclusive deals**: Common in Norwegian aquaculture for feed/smolt
- **Processing margins**: 15% cost typical, integration saves 10-15%

---

*This system transforms Lakskongen from a production simulator into a strategic business game where contract warfare creates competitive moats and empire-building opportunities beyond simple farm expansion.*
