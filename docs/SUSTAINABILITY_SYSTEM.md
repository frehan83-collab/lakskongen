# 🌱 Sustainability Score & Green Certification System

**Author:** Camilla Johansen, Gameplay Designer  
**Version:** 1.0.0 (Phase 1: Foundation)  
**Date:** March 12, 2026  
**Status:** IMPLEMENTED ✅

---

## Overview

The Sustainability Score & Green Certification system is a **core differentiator** for Lakskongen vs. Farming Simulator 25's shallow aquaculture mechanics. It transforms environmental compliance from a penalty-only system into a **positive engagement loop** that unlocks premium markets (+12-29% price margins) and ESG investment bonuses.

### Key Features

- **0-100 Score:** Composite sustainability rating across 6 categories
- **5 Certification Tiers:** From Non-Compliant to Sustainability Leader
- **Premium Market Access:** ASC/Organic certification unlocks high-value buyers
- **ESG Mechanics:** Grants, subsidies, and reputation bonuses for green leaders
- **Visual Feedback:** Color-coded UI, category breakdown, real-time scoring

---

## Scoring Formula

### Total Score (0-100 points)

The sustainability score is calculated from **6 weighted categories**:

| Category | Weight | Description |
|----------|--------|-------------|
| **Sea Lice Management** | 25 pts | Lice count per fish, delousing methods |
| **Escape Prevention** | 20 pts | Annual escape events, containment systems |
| **Feed Sustainability** | 20 pts | Sustainable feed sources, advanced ingredients |
| **Carbon Footprint** | 15 pts | Renewable energy, transport efficiency, vessel fuel |
| **Welfare Standards** | 10 pts | Stocking density, handling practices, humane slaughter |
| **Ecosystem Impact** | 10 pts | Benthic health, nutrient discharge, wild fish impact |

**Total:** 100 points

---

## Category Breakdown

### 1. Sea Lice Management (25 points)

**Target:** <0.2 female lice per fish = maximum points  
**Calculation:**
- **≤0.2 lice/fish:** 25 points (Excellent)
- **0.2-0.5 lice/fish:** 15 points (Acceptable)
- **>0.5 lice/fish:** 0 points (Poor)

**Bonus:** +5 points for using **mechanical delousing** (no chemicals)

**Integration:** 
- Reads from `Health/SeaLiceModel.cs` → `AverageLicePerFish` property
- Tracks delousing method (chemical vs mechanical) via `DelousingSystem.cs`

---

### 2. Escape Prevention (20 points)

**Target:** Zero escapes per year = maximum points  
**Calculation:**
- **0 escapes:** 20 points (Perfect)
- **1-10 escapes:** 10 points (Acceptable)
- **>10 escapes:** 0 points (Poor)

**Bonus:** +5 points for **closed containment systems** (RAS, semi-closed pens)

**Integration:**
- Reads from `Farm/SeaSite.cs` → Add `EscapesThisYear` property
- Tracks containment type (open pen vs closed system)

---

### 3. Feed Sustainability (20 points)

**Target:** 100% sustainable feed sources = maximum points  
**Calculation:**
- Based on **sustainability rating (0.0-1.0)** of feed mix
- Points = `sustainabilityRating × 20`
- Example: 70% sustainable feed = 14 points

**Bonus:** +5 points for **advanced feed types** (insect meal, algae, single-cell proteins)

**Integration:**
- Reads from `Farm/FeedSystem.cs` → Add `SustainabilityRating` property
- Tracks feed composition (fishmeal % vs sustainable alternatives)

**Sustainability Ratings:**
- **Fishmeal (wild-caught):** 0.4 (40% sustainable)
- **Fishmeal (byproduct):** 0.7 (70% sustainable)
- **Soy/plant proteins:** 0.8 (80% sustainable)
- **Insect meal:** 0.95 (95% sustainable)
- **Algae/microalgae:** 1.0 (100% sustainable)

---

### 4. Carbon Footprint (15 points)

**Target:** 50%+ renewable energy, electric vessels = maximum points  
**Calculation:**
- **Renewable energy %:** 0-50% coverage = 0-10 points
- **Transport efficiency:** Local processing vs long-haul = 0-3 points
- **Vessel fuel type:** Electric = +2 points

**Integration:**
- Reads from `Logistics/LogisticsManager.cs` → Add `CarbonFootprintScore()` method
- Tracks energy sources (grid vs solar/wind/tidal)
- Tracks vessel fuel consumption and type

---

### 5. Welfare Standards (10 points)

**Target:** Stocking density <80% of max, humane slaughter practices  
**Calculation:**
- **Stocking density:** If <80% of max across all sites = 7 points
- **Handling practices:** Low-stress handling = 2 points
- **Slaughter method:** Electrical stunning = +1 point bonus

**Integration:**
- Reads from `Farm/SeaSite.cs` and `Biology/FishPopulation.cs`
- Calculate `CurrentDensity / MaxAllowedDensity` per site
- Track slaughter method (percussive vs electrical stunning)

---

### 6. Ecosystem Impact (10 points)

**Target:** Healthy seabed (70%+ benthic oxygen), low nutrient discharge  
**Calculation:**
- **Benthic health:** 0-100% health rating = 0-7 points
- **Nutrient discharge:** Below threshold = 3 points

**Integration:**
- NEW SYSTEM: `Environment/EcosystemMonitor.cs` (future implementation)
- Or add properties to `Environment/Zone.cs`:
  - `BenthicOxygenLevel` (percentage)
  - `NutrientDischargeRate` (kg/day)

---

## Certification Tiers

### Non-Compliant (0-49 points)
**Color:** 🔴 Red  
**Price Multiplier:** 0.85× (−15% penalty)  
**Market Access:** Domestic + Russia only (cannot export to EU, USA, Japan)  
**Penalties:** 50,000 NOK fine per quarter  
**Description:** Major buyers refuse contracts. Regulatory risk.

---

### Basic Compliance (50-69 points)
**Color:** 🟡 Gold  
**Price Multiplier:** 1.00× (standard pricing)  
**Market Access:** Domestic, EU, USA, Russia, China  
**Penalties:** None  
**Description:** Standard market access. No premium buyers.

---

### ASC Certified (70-84 points)
**Color:** 🔵 Cyan  
**Price Multiplier:** 1.08× (+8% bonus)  
**Market Access:** + Whole Foods, Waitrose, premium retail chains  
**Bonuses:** Access to premium buyers  
**Description:** Industry-standard certification. Unlocks mid-tier premium markets.

---

### Organic Certified (85-94 points)
**Color:** 🟢 Green  
**Price Multiplier:** 1.15× (+15% bonus)  
**Market Access:** + Japan Premium, Michelin restaurants, ultra-premium  
**Bonuses:** Full market access  
**Description:** Top-tier certification. Maximum price premiums.

---

### Sustainability Leader (95-100 points)
**Color:** 🟢 Bright Green  
**Price Multiplier:** 1.15× (+15% bonus)  
**Market Access:** All markets  
**Extra Bonuses:**
- **Government grants** (1.5× multiplier on innovation funding)
- **ESG investment bonus** (30% subsidy on green tech like RAS, solar)
- **Media awards** (positive press, reputation boost)
- **Lower loan interest rates** (ESG investors offer better terms)

**Description:** Industry leadership. Unlocks ESG investor benefits and government support.

---

## Integration Guide

### For Gameplay Programmers

#### 1. Add Properties to Existing Systems

**`Health/SeaLiceModel.cs`:**
```csharp
public double AverageLicePerFish { get; private set; }
```

**`Farm/SeaSite.cs`:**
```csharp
public int EscapesThisYear { get; set; }
public bool IsClosedContainment { get; set; }
public double CurrentDensity { get; private set; }
public double MaxAllowedDensity { get; set; }
```

**`Farm/FeedSystem.cs`:**
```csharp
public double SustainabilityRating { get; private set; }
public bool UsesInsectMeal { get; set; }
public bool UsesAlgae { get; set; }
```

**`Logistics/LogisticsManager.cs`:**
```csharp
public double CarbonFootprintScore()
{
    // Calculate based on:
    // - Renewable energy %
    // - Transport distance
    // - Vessel fuel type
    return 0.5; // 0.0-1.0
}
```

---

#### 2. Instantiate SustainabilityManager

In your main simulation engine (`Core/SimulationEngine.cs`):

```csharp
using LakskongenSim.Sustainability;
using System.IO;
using System.Text.Json;

// Load config
var configJson = File.ReadAllText("Data/SustainabilityConfig.json");
var config = JsonSerializer.Deserialize<SustainabilityConfig>(configJson);

// Create manager
var sustainabilityManager = new SustainabilityManager(config);

// Subscribe to events
sustainabilityManager.OnScoreChanged += (score, level) => 
{
    Console.WriteLine($"Score updated: {score}/100 ({level})");
    // Update UI
};

sustainabilityManager.OnCertificationChanged += (level) => 
{
    Console.WriteLine($"Certification changed: {level}");
    // Unlock new markets, apply bonuses
};
```

---

#### 3. Update Score Periodically

Call `UpdateScore()` weekly or monthly in your simulation loop:

```csharp
// In SimulationEngine.Tick()
if (currentDay % 7 == 0) // Weekly update
{
    sustainabilityManager.UpdateScore(gameState);
    
    // Apply price multiplier to market sales
    var multiplier = sustainabilityManager.GetPriceMultiplier(
        sustainabilityManager.CurrentCertification
    );
    marketSystem.SetPriceMultiplier(multiplier);
}
```

---

#### 4. Display in UI (Godot)

Connect the C# manager to the GDScript UI panel:

```csharp
// In your Godot bridge/controller script
var panel = GetNode<SustainabilityPanel>("UI/SustainabilityPanel");

sustainabilityManager.OnScoreChanged += (score, level) => 
{
    var categories = sustainabilityManager.GetComponentScores(gameState);
    panel.Call("update_display", score, level.ToString(), categories);
};
```

---

### For UI Designers

#### GDScript Panel Usage

```gdscript
# Get reference to panel
var sustainability_panel = $UI/SustainabilityPanel

# Update display
sustainability_panel.update_display(
    75,  # score (0-100)
    "ASCCertified",  # certification level
    {
        "SeaLiceManagement": 20,
        "EscapePrevention": 18,
        "FeedSustainability": 15,
        "CarbonFootprint": 10,
        "WelfareStandards": 7,
        "EcosystemImpact": 5
    }
)
```

#### Styling

Panel uses design tokens from `design/tokens.json`:
- **Background:** `#121826` (panel)
- **Text:** `#E6EEF3` (primary)
- **Accent:** `#2AB7B5` (cyan for scores)
- **Danger:** `#E06666` (red for low scores)

---

## Gameplay Impact

### Market Access

Certification level **gates access** to premium markets:

| Market | Min. Certification | Price Premium |
|--------|-------------------|---------------|
| Domestic | None | Standard |
| EU | Basic Compliance | Standard |
| USA | Basic Compliance | Standard |
| Whole Foods | ASC Certified | +8% |
| Waitrose | ASC Certified | +8% |
| Japan Premium | Organic Certified | +15% |
| Michelin Restaurants | Organic Certified | +15% |

### Financial Bonuses

**Sustainability Leader (95-100):**
- **Government Grants:** 1.5× multiplier (e.g., 300K grant becomes 450K)
- **ESG Investment Subsidy:** 30% discount on green tech (RAS system 10M → 7M)
- **Loan Interest Reduction:** −1.5% APR from ESG-focused banks

### Penalties

**Non-Compliant (<50):**
- **Quarterly Fine:** 50,000 NOK automatically deducted
- **Market Lockout:** Cannot export to EU, USA, Japan (60% of premium markets)
- **Reputation Damage:** News headlines, stakeholder faction anger

---

## Balancing Notes

### Difficulty Curve

**Early Game (Year 1-2):**
- Expect 40-60 score (Basic Compliance)
- Focus: Lice management, zero escapes
- Goal: Avoid Non-Compliant fines

**Mid Game (Year 3-5):**
- Target 70-85 score (ASC Certified)
- Investments: Mechanical delousing, sustainable feed (insect meal)
- Unlock: Premium retail buyers (+8%)

**Late Game (Year 6+):**
- Aim for 95-100 (Sustainability Leader)
- Investments: RAS systems, solar panels, electric vessels
- Payoff: ESG grants offset high costs, maximum margins

### Trade-Offs

**High Sustainability = High Costs (initially):**
- RAS system: 10M NOK investment
- Solar panels: 2M NOK
- Insect meal feed: +15% feed costs

**BUT rewards compound:**
- +15% price premium = +3M revenue on 20M sales
- 30% ESG subsidy = −3M on 10M RAS investment
- 1.5× government grants = +150K on 300K innovation grant
- Lower loan rates = −500K interest on 10M loan

**Net result:** Break-even in Year 2-3, then massive profitability.

---

## Future Improvements (Phase 2+)

### Planned Features

1. **Dynamic Audits:**
   - Random quarterly inspections (15% chance)
   - Failure penalties: 25K-75K fines, risk of license suspension
   - Player can prepare for audits (temporary score boosts)

2. **Certification Decay:**
   - Score slowly drifts toward 70 if player neglects investments
   - Must actively maintain standards to keep Leader status

3. **Stakeholder Faction Integration:**
   - Environmental NGOs demand 80+ score (approval = grants)
   - Corporate buyers require ASC certification for contracts
   - Government offers subsidies for 95+ score

4. **Visual Fjord Health:**
   - Real-time water clarity based on ecosystem score
   - Algae blooms visible if nutrient discharge high
   - Healthy seabed = fish population natural bonuses

5. **Technology Research Tree:**
   - Unlock advanced sustainability tech (closed containment, algae feed)
   - Research grants available at 85+ score

6. **Competitor Pressure:**
   - AI rivals invest in sustainability → market expects YOU to match
   - "SalMar achieves Organic Certification" → pressure to keep up

7. **Photo Mode Integration:**
   - Screenshot achievement: "Pristine Fjord" unlocks at 95 score
   - Media award cutscenes for Sustainability Leader tier

---

## Technical Architecture

### Class Diagram

```
SustainabilityManager
├── CalculateScore(GameState) → int (0-100)
├── GetComponentScores(GameState) → Dictionary<string, int>
├── GetCertificationLevel(int score) → CertificationLevel
├── GetPriceMultiplier(CertificationLevel) → decimal
└── UpdateScore(GameState) → void (triggers events)

SustainabilityConfig (JSON)
├── Thresholds (SeaLice, Escapes, Feed, Carbon, Welfare, Ecosystem)
├── CertificationMinScores
├── PriceMultipliers
└── Bonuses (ESG, Grants, Fines)

SustainabilityPanel (GDScript)
├── update_display(score, cert, categories)
├── _create_category_bar(label, score, max)
└── on_score_changed(score, cert)
```

### Data Flow

1. **Simulation Tick** (weekly/monthly)
2. **SustainabilityManager.UpdateScore(gameState)** reads from:
   - SeaLiceModel.AverageLicePerFish
   - SeaSite.EscapesThisYear
   - FeedSystem.SustainabilityRating
   - LogisticsManager.CarbonFootprintScore()
   - FishPopulation.StockingDensity
   - (Future) EcosystemMonitor.BenthicHealth
3. **Calculate component scores** (6 categories)
4. **Sum to total score** (0-100)
5. **Determine certification level** (enum)
6. **Trigger events** → UI update, market unlock, bonuses

---

## Testing Checklist

### Unit Tests (C#)

- [ ] `CalculateScore()` returns 0-100 for valid GameState
- [ ] `GetComponentScores()` sums to total score
- [ ] `GetCertificationLevel()` maps scores correctly:
  - 49 → NonCompliant
  - 50 → BasicCompliance
  - 70 → ASCCertified
  - 85 → OrganicCertified
  - 95 → SustainabilityLeader
- [ ] `GetPriceMultiplier()` returns correct decimals
- [ ] Edge cases: Empty GameState, zero fish, zero sites

### Integration Tests

- [ ] Score updates when sea lice count changes
- [ ] Score updates when escape event occurs
- [ ] Feed system integration: Insect meal increases score
- [ ] Logistics integration: Electric vessel increases score
- [ ] Price multiplier applies to market sales

### UI Tests (Godot)

- [ ] Panel displays score (0-100)
- [ ] Certification label updates with color
- [ ] Category bars show correct values
- [ ] Price multiplier displays +% or −%
- [ ] Events trigger UI refresh

---

## FAQ

### Q: Why is Sea Lice Management worth 25 points?
**A:** It's the #1 operational challenge in Norwegian salmon farming. High weight reflects industry reality (2026 lice quota system) and gameplay importance.

### Q: Can I achieve 100/100 score?
**A:** Yes, but requires maximum investment: RAS systems (zero lice/escapes), 100% algae feed, electric vessels, solar power. Target for late-game perfectionists.

### Q: Do competitors affect my score?
**A:** Not directly (Phase 1). In Phase 2, competitor sustainability investments will create market pressure ("Mowi is Organic Certified, why aren't you?").

### Q: What happens if I drop below 50 score?
**A:** Non-Compliant status triggers:
- Quarterly fines (50K NOK)
- Loss of EU/USA export access
- Negative news headlines
- Stakeholder faction anger (Environmental NGOs)

**Recovery:** Invest in improvements (mechanical delousing, sustainable feed). Score updates weekly, so 4-8 weeks to regain Basic Compliance.

---

## Credits

**Design:** Camilla Johansen (Gameplay Designer)  
**Research:** 2026 Norwegian aquaculture sustainability standards  
**Inspiration:** Anno 1800's production chain depth, Frostpunk 2's political pressure systems  
**Goal:** Differentiate Lakskongen from Farming Simulator 25 aquaculture with 100× deeper sustainability mechanics  

---

**End of Documentation**  
🌱 Build sustainably, farm profitably, lead the industry! 🐟
