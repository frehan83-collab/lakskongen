# Crisis Capitalism & Distressed Asset System

The Crisis Capitalism system allows players to exploit industry crises to acquire competitor assets at significant discounts. This mechanic is inspired by real-world distressed asset acquisition and the actual vulnerabilities of the Norwegian salmon farming industry.

## Overview

When competitors face crises (disease outbreaks, bankruptcies, regulatory shutdowns, environmental scandals), their assets become available for acquisition at fire-sale prices. Players can build empires by strategically timing these acquisitions, but must manage the associated risks and reputation damage.

## Real-World Basis

This system is based on documented patterns from the Norwegian salmon industry:

### Historical Crisis Examples
- **Chilean ISA Crisis (2007-2010)**: $2B industry losses, 60% production drop, mass bankruptcies
- **ISA Outbreaks**: 40-70% asset value discounts during forced liquidations  
- **Disease-Triggered Distress**: 30-60% discounts on licenses and farm sites
- **Regulatory Shutdowns**: Emergency liquidation sales at massive discounts
- **Market Crashes + Overleveraging**: Forced asset sales by weak competitors

### Industry Vulnerabilities
- **Disease Susceptibility**: Dense farming creates epidemic risks
- **Regulatory Exposure**: Environmental violations trigger shutdowns
- **Capital Intensity**: High leverage makes companies vulnerable to price crashes
- **Environmental Sensitivity**: Pollution scandals destroy company values
- **Market Volatility**: Global price swings force distressed sales

## System Components

### CrisisEvent Class
Represents industry crises that create distressed asset opportunities:

```csharp
public class CrisisEvent
{
    public CrisisType Type { get; set; }           // DISEASE_OUTBREAK, BANKRUPTCY, etc.
    public string AffectedCompetitorId { get; set; } // Which company is affected
    public float SeverityScore { get; set; }        // 0-1 scale of crisis severity
    public float DiscountMultiplier { get; set; }   // How much prices are reduced
    public int DurationMonths { get; set; }         // How long crisis lasts
}
```

#### Crisis Types
1. **DISEASE_OUTBREAK**: ISA, sea lice, viral infections
2. **BANKRUPTCY**: Financial collapse from overleveraging
3. **REGULATORY_SHUTDOWN**: Environmental or safety violations
4. **MARKET_CRASH**: Global price collapses affecting weak players
5. **ENVIRONMENTAL_SCANDAL**: Chemical contamination, pollution incidents

### DistressedAsset Class
Represents individual assets available for acquisition:

```csharp
public class DistressedAsset
{
    public AssetType AssetType { get; set; }         // LICENSE, FARM_SITE, etc.
    public decimal OriginalValueNOK { get; set; }    // Pre-crisis market value
    public decimal DiscountedPriceNOK { get; set; }  // Current fire-sale price
    public int TimeRemainingMonths { get; set; }     // Time before opportunity expires
    public float RiskScore { get; set; }             // Integration/contamination risks
}
```

#### Asset Types
1. **LICENSE**: Aquaculture permits and production quotas (500K-5M NOK)
2. **FARM_SITE**: Sea cages, moorings, infrastructure (10M-80M NOK)  
3. **PROCESSING_PLANT**: Processing facilities, cold storage (25M-300M NOK)
4. **EQUIPMENT**: Feeding systems, monitoring equipment (200K-15M NOK)

### CrisisCapitalismSystem
Main system managing crisis detection and asset acquisition:

#### Key Methods
- `TriggerCrisis()`: Initiates a crisis affecting a competitor
- `GetActiveCrises()`: Returns all ongoing crisis events
- `GetAvailableDistressedAssets()`: Lists assets available for purchase
- `PurchaseDistressedAsset()`: Attempts to acquire a distressed asset
- `CalculateIntegrationRisk()`: Evaluates acquisition risks

## Acquisition Process

### 1. Crisis Detection
- System monitors competitor health, market conditions, regulatory issues
- Crisis triggers when thresholds exceeded (debt ratios, disease detection, violations)
- Crisis severity determines discount levels and duration

### 2. Asset Generation
- Crisis creates portfolio of distressed assets from affected competitor
- Asset types and values based on crisis type and company profile
- Risk scores reflect contamination, damage, regulatory issues

### 3. Acquisition Opportunity
- Assets available for limited time (2-24 months depending on crisis)
- Prices discounted 20-80% based on crisis severity
- First-come-first-served basis for acquisition

### 4. Purchase Decision
- Player evaluates price vs. risk vs. potential return
- Must have sufficient cash for purchase
- Acquisition adds to player portfolio but may damage reputation

### 5. Integration Risk
- Acquired assets may have ongoing problems (disease, contamination)
- Risk scores influence operational costs and success rates  
- Some assets may require significant remediation investment

## Discount Mechanics

### Crisis Type Discounts
- **Disease Outbreak**: 40-70% (based on real ISA crisis data)
- **Bankruptcy**: 60-80% (liquidation sale urgency)
- **Regulatory Shutdown**: 50-75% (compliance uncertainty)
- **Market Crash**: 30-60% (fundamental pressure)
- **Environmental Scandal**: 20-50% (reputation damage)

### Severity Multipliers
- Low Severity (0.0-0.3): Minimum discount range
- Medium Severity (0.3-0.6): Mid-range discounts
- High Severity (0.6-1.0): Maximum discount range

### Time Pressure
- Assets near expiration get additional discounts
- Fire sales in final month may offer extra 10-20% off

## Risk Assessment

### Integration Risk Factors
1. **Disease Contamination**: Farm sites may require decontamination
2. **Regulatory Compliance**: Licenses may need costly approval processes
3. **Equipment Condition**: Machinery may be damaged or obsolete
4. **Market Position**: Assets may be in declining regions
5. **Reputation Transfer**: May inherit negative associations

### Risk Mitigation Strategies
- **Due Diligence**: Research crisis details before purchase
- **Diversification**: Spread acquisitions across crisis types and regions
- **Cash Reserves**: Maintain funds for post-acquisition remediation
- **Timing**: Avoid acquisitions during reputation-sensitive periods

## Reputation System

### Vulture Capitalism Score
- Scale: 0-100 (higher = more negative reputation)
- Increases with each distressed acquisition (+5 base)
- Crisis type multipliers affect reputation damage
- Industry perception affects partnerships, financing, regulatory treatment

### Reputation Thresholds
- **0-20**: Clean reputation, no concerns
- **20-40**: Opportunistic, some ethical questions  
- **40-60**: Questionable, known for exploiting crises
- **60-80**: Predatory, widely criticized tactics
- **80-100**: Industry pariah, extreme vulture capitalist

### Reputation Recovery
- Natural decay: -0.5 points per month
- Positive actions: Environmental investments, community support
- Crisis resolution: Helping industry during crises can improve reputation

## Strategic Considerations

### Timing Strategies
- **Crisis Anticipation**: Monitor competitor health indicators
- **Market Cycles**: Acquire during downturns for maximum advantage
- **Seasonal Patterns**: Disease outbreaks more likely in spring/summer
- **Regulatory Calendar**: Track compliance deadlines and enforcement cycles

### Portfolio Optimization
- **Asset Mix**: Balance licenses, sites, plants, equipment
- **Geographic Spread**: Diversify across Norwegian regions
- **Risk Balance**: Mix high-discount/high-risk with safer acquisitions
- **Integration Capacity**: Don't acquire more than can be effectively integrated

### Competitive Advantages
- **Information Networks**: Early crisis detection provides acquisition edge
- **Capital Availability**: Maintain cash reserves for rapid deployment
- **Integration Expertise**: Build capabilities to handle distressed assets
- **Reputation Management**: Balance acquisition opportunities with public image

## Crisis Scenarios

### Disease Outbreak Response
1. **Immediate Assessment**: Evaluate contamination risk and quarantine scope
2. **Asset Triage**: Prioritize licenses and clean sites over contaminated assets
3. **Decontamination Planning**: Budget for cleanup and biosecurity upgrades
4. **Timeline Management**: Balance acquisition speed with due diligence

### Bankruptcy Liquidation
1. **Asset Catalog Review**: Identify highest-value, lowest-risk opportunities
2. **Competitive Bidding**: Prepare for auction scenarios with multiple bidders
3. **Integration Planning**: Prepare for rapid asset absorption
4. **Workforce Transition**: Plan for potential employee transfers

### Regulatory Shutdown
1. **Compliance Assessment**: Evaluate regulatory risk and remediation costs
2. **Legal Review**: Understand liability transfer and ongoing obligations
3. **Stakeholder Management**: Engage with regulators early in process
4. **Timeline Uncertainty**: Plan for extended approval processes

## Implementation Notes

### Technical Integration
- Crisis monitoring runs as background service
- Asset generation triggered by crisis events
- Player notifications for new acquisition opportunities
- Integration with cash management and portfolio tracking systems

### Balancing Considerations
- Crisis frequency balanced to create regular opportunities without overwhelming players
- Discount levels significant enough to incentivize risk-taking
- Reputation penalties meaningful but not game-ending
- Risk/reward ratios calibrated for interesting strategic decisions

### Historical Accuracy
- Crisis frequencies based on Norwegian salmon industry data
- Asset value ranges reflect real Norwegian farm and plant valuations
- Discount percentages based on documented crisis liquidations
- Regulatory responses mirror actual Norwegian aquaculture oversight

## Future Enhancements

### Advanced Crisis Modeling
- **Regional Spread**: Disease outbreaks spreading between adjacent farms
- **Market Contagion**: Financial crises cascading between connected companies
- **Regulatory Cycles**: Predictable enforcement patterns and policy changes
- **Insurance Integration**: Coverage affecting asset availability and pricing

### Player Agency Expansion
- **Crisis Mitigation**: Investments to reduce crisis probability
- **Industry Influence**: Lobbying and political activities affecting regulations
- **Intelligence Networks**: Information gathering for crisis prediction
- **Reputation Rehabilitation**: Active PR campaigns to improve public image

### Multiplayer Considerations
- **Competitive Bidding**: Multiple players competing for same distressed assets
- **Information Asymmetry**: Different players having different crisis intelligence
- **Cooperative Opportunities**: Joint ventures for large acquisitions
- **Market Manipulation**: Players potentially triggering artificial crises

This system creates compelling risk/reward decisions where players must balance the allure of cheap assets against integration challenges and reputation damage, mirroring the complex dynamics of real-world distressed asset acquisition in volatile industries.