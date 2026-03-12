# Competitor AI Design for Lakskongen

*Design specification for AI-controlled salmon farming competitors*

## Overview

Lakskongen features 8 AI-controlled Norwegian salmon farming companies that compete with the player for market share, licenses, and resources. Each competitor has distinct strategies, financial positions, and vulnerabilities that create dynamic market conditions and acquisition opportunities.

## Competitor Archetypes

### 1. **Conservative Farmers** (Nordfjord Akva, Hardanger Seafarms)
**Behavior:**
- Focus on steady, sustainable growth
- Avoid high-risk expansion
- Prioritize biosecurity and compliance
- Slow to adopt new technologies
- React defensively to market changes

**Triggers:**
- Distress: Major disease outbreak (ISA/PD) or regulatory violation
- Acquisition: Offer 15-25% premium above fair value
- Partnership: Open to joint ventures for risk-sharing

### 2. **Aggressive Expansionists** (Lofoten Laksekongen, Møre Ekspansjon, Finnmark Giants)
**Behavior:**
- Pursue maximum MAB growth
- Leverage debt for acquisitions
- Push environmental/regulatory limits
- Compete aggressively for licenses
- Quick to exploit market opportunities

**Triggers:**
- Distress: Overleveraged + price crash, or major regulatory penalty
- Acquisition: Require 30-50% premium (won't sell cheap)
- Competition: Will undercut player pricing to gain market share

### 3. **Innovation Specialists** (Trondheim Genetics, Bergen BioTech)
**Behavior:**
- Develop proprietary technologies
- License innovations to competitors
- Small production volumes, high margins
- Strategic partnerships over expansion
- Premium market positioning

**Triggers:**
- Distress: R&D failure or patent challenges
- Acquisition: Very high premiums (40-60%) due to IP value
- Partnership: Licensing deals, joint R&D ventures

### 4. **Environmentalists** (Tromsø Processing)
**Behavior:**
- ASC/MSC certifications
- Circular economy practices
- Premium pricing strategy
- Transparent operations
- Stakeholder engagement

**Triggers:**
- Distress: Environmental scandal or certification loss
- Acquisition: Require cultural fit assurances
- Partnership: Sustainability-focused collaborations

### 5. **Vertically Integrated Giants** (Lofoten Laksekongen, Bergen BioTech, Finnmark Giants)
**Behavior:**
- Control full value chain
- Strategic asset acquisitions
- Market power through integration
- Economies of scale
- Political influence

**Triggers:**
- Distress: Supply chain disruption or antitrust action
- Acquisition: Only if player has comparable integration
- Competition: Will engage in contract warfare (exclusive deals)

## AI Decision Framework

### Expansion Decisions
```python
def should_expand(competitor):
    if competitor.financial_health < 0.6:
        return False  # Too risky
    
    if competitor.expansion_appetite > random(0, 1):
        # Check affordability
        available_licenses = get_available_mab_licenses()
        affordable = [l for l in available_licenses if l.price < competitor.cash * 0.4]
        if affordable:
            return True
    
    return False
```

### Disease Response
```python
def handle_disease_outbreak(competitor, disease):
    if disease in competitor.distress_trigger_diseases:
        competitor.financial_health -= 0.3  # Major impact
        
        if competitor.strategy_type == "aggressive":
            # Try to harvest quickly before losses mount
            emergency_harvest()
        elif competitor.strategy_type == "conservative":
            # Cull infected stock, improve biosecurity
            cull_infected()
            increase_biosecurity_spend()
        elif competitor.strategy_type == "environmental":
            # Transparent disclosure, maintain certifications
            public_disclosure()
            regulatory_cooperation()
```

### Pricing Strategy
```python
def set_market_price(competitor):
    base_price = global_salmon_price_nok_per_kg
    
    if competitor.strategy_type == "aggressive":
        # Undercut to gain market share
        return base_price * 0.95
    elif competitor.specialization == "environmental":
        # Premium for sustainability
        return base_price * 1.15
    elif competitor.financial_health < 0.4:
        # Distressed fire sale
        return base_price * 0.85
    else:
        # Follow market
        return base_price
```

### Acquisition Triggers
Competitors become acquirable when:
- **Financial health < 0.3** (distressed)
- **Major disease outbreak** in distress_trigger_diseases
- **Regulatory shutdown** or major compliance violation
- **Overleveraged** (debt > 80% assets) during price crash
- **Strategic fit** for vertical integration plays

## Player Interactions

### 1. **Acquisitions**
- **Friendly buyout**: Offer premium, negotiate terms
- **Hostile takeover**: Stock accumulation (if public), shareholder pressure
- **Distressed purchase**: Fire-sale pricing during crisis
- **Asset stripping**: Buy for licenses, shut down inefficient operations

### 2. **Partnerships**
- **Joint ventures**: Share costs/risks for new market entry
- **Licensing agreements**: Access competitor genetics/technology
- **Processing contracts**: Toll processing arrangements
- **Supply chain integration**: Exclusive feed/smolt agreements

### 3. **Competition**
- **Price wars**: Undercut competitor pricing
- **Contract lockout**: Secure exclusive deals to block competitors
- **License bidding**: Outbid for prime MAB allocations
- **Talent poaching**: Hire key personnel from competitors

### 4. **Sabotage** (Future Mechanic)
- **Industrial espionage**: Steal R&D, production data
- **Regulatory complaints**: Trigger inspections of competitors
- **Market manipulation**: Short competitor stocks, spread negative press
- **Supply chain disruption**: Interfere with feed/smolt delivery

## Event Triggers

### Distress Events
1. **ISA Outbreak** → 40-70% discount for affected companies
2. **Regulatory Shutdown** → Emergency asset liquidation
3. **Market Price Crash** → Overleveraged companies seek buyers
4. **Environmental Scandal** → Reputation-focused companies vulnerable
5. **Technology Failure** → Innovation specialists devalued

### Opportunity Events
1. **License Auction** → Competitors bid against player
2. **Processing Plant Sale** → Vertically integrated companies compete
3. **Bankruptcy Auction** → Multiple bidders for distressed assets
4. **Technology Breakthrough** → Innovation specialists gain value
5. **Export Market Opening** → Expansion race for new markets

## AI Personality Parameters

Each competitor has dynamic attributes that evolve:

```json
{
  "financial_health": 0.0-1.0,  // Affects all decisions
  "expansion_appetite": 0.0-1.0,  // Willingness to grow
  "risk_tolerance": 0.0-1.0,  // Debt usage, new markets
  "innovation_focus": 0.0-1.0,  // R&D investment
  "environmental_commitment": 0.0-1.0,  // Sustainability priority
  "political_influence": 0.0-1.0,  // Regulatory advantages
  "reputation_score": 0.0-1.0  // Public perception
}
```

### Dynamic Changes
- **Financial health**: Disease, prices, costs, acquisitions
- **Reputation**: Environmental incidents, quality issues, certifications
- **Political influence**: Lobbying spend, stakeholder relations
- **Risk tolerance**: Success/failure of past expansions

## Strategic Depth

### Early Game (Years 1-3)
- Competitors are relatively stable
- Focus on organic growth
- Few acquisition opportunities
- Competitive but not cutthroat

### Mid Game (Years 4-7)
- Market consolidation begins
- Disease outbreaks create opportunities
- Some competitors expand aggressively
- First distressed asset sales

### Late Game (Years 8+)
- Industry dominated by 3-4 major players
- High-stakes M&A activity
- Regulatory scrutiny on consolidation
- Vertical integration arms race
- International market competition

## Implementation Priorities

### Phase 1 (MVP)
- Basic competitor profiles loaded from JSON
- Simple AI pricing decisions
- Distress triggers (disease, financial)
- Acquisition availability based on financial_health
- Static market share tracking

### Phase 2 (Enhanced AI)
- Dynamic expansion decisions
- Disease response strategies
- Competitive bidding for licenses
- Partnership/JV mechanics
- Event-driven behavior changes

### Phase 3 (Full Intelligence)
- Machine learning opponent adaptation
- Multi-turn strategic planning
- Coalition formation against player
- Emergent market dynamics
- Player pattern recognition

## Balance Considerations

**Too Easy:**
- Competitors always distressed
- Acquisitions too cheap
- No competitive pressure on licenses
- AI doesn't respond to player dominance

**Too Hard:**
- No acquisition opportunities
- AI always outbids player
- Impossible to gain market share
- Competitors collude against player

**Sweet Spot:**
- 1-2 distressed competitors available at any time
- Competitive but winnable license auctions
- AI adapts to player strategy (price matching, etc.)
- Opportunities created by player skill (triggering crises)

## Future Enhancements

1. **Emergent Narratives**: Generated news stories about competitor activities
2. **CEO Personalities**: Named executives with traits affecting decisions
3. **Merger Simulations**: AI competitors merge with each other
4. **International Competition**: Scottish, Chilean, Canadian rivals
5. **Activist Investors**: External pressure on competitor strategies
6. **Technology Arms Race**: Competing R&D programs
7. **Labor Market**: Poaching key personnel affects competitor performance

---

*This design creates a living, breathing competitive market where player decisions have ripple effects and strategic opportunities emerge from dynamic market conditions.*
