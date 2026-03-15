# VAP Processing System Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    PROCESSING PLANT                             │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  Tier 1: Basic Gutting (1M NOK)                           │ │
│  │    Products: Whole, HOG, Frozen                           │ │
│  └───────────────────────────────────────────────────────────┘ │
│                            ↓ UPGRADE                            │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  Tier 2: Fillet Line (3M NOK)                             │ │
│  │    + Fillets (skin-on, skinless), Portions                │ │
│  └───────────────────────────────────────────────────────────┘ │
│                            ↓ UPGRADE                            │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  Tier 3: Value-Added (8M NOK)                             │ │
│  │    + Smoked, Gravlax, Canned                              │ │
│  └───────────────────────────────────────────────────────────┘ │
│                            ↓ UPGRADE                            │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  Tier 4: Premium (15M NOK)                                │ │
│  │    + Sashimi, Pâté, Jerky, Ready Meals, Roe              │ │
│  └───────────────────────────────────────────────────────────┘ │
│                            ↓ UPGRADE                            │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  Tier 5: Full Integration (25M NOK)                       │ │
│  │    + Fish Oil, Fish Meal, Fish Skin                       │ │
│  └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│  Lines: [Line 1] [Line 2] [Line 3] ... [Line N]               │
└─────────────────────────────────────────────────────────────────┘
```

---

## Production Line Flow

```
INPUT                    PROCESSING                OUTPUT
                                                   
10,000 kg    ┌──────────────────────────┐     
Fresh Fish   │   PRODUCTION LINE        │     6,200 kg
Quality:0.9  │                          │     Fillets
Fresh: 8h    │  • Product: Fillet       │     
Temp: 95%    │  • Technology: Semi-Auto │     Revenue: 930,000 NOK
             │  • Skill: Skilled        │     Costs:   630,000 NOK
Raw Cost:    │  • Workers: 4 skilled    │     Profit:  300,000 NOK
600,000 NOK  │             2 unskilled  │     Margin:  32%
             │                          │     
             │  YIELD CALCULATION       │     Rejects: 200 kg
             │   Base:        62%       │     (Quality issues)
             │   Quality:     +2%       │     
             │   Skill:       +3%       │     Net: 6,000 kg
             │   Tech:        +5%       │     saleable product
             │   Freshness:   -2%       │     
             │   = 70% gross yield      │     
             │   - 3% rejects           │     
             │   = 67% net yield        │     
             └──────────────────────────┘     
```

---

## Yield Calculation Engine

```
┌───────────────────────────────────────────────────────────────┐
│                  YIELD CALCULATOR                             │
│                                                               │
│  INPUT FACTORS:                                               │
│    • Input Quality Score (0-1)          [0.9] ───┐          │
│    • Skill Level (Untrained...Master)   [Skilled] ──┤        │
│    • Technology Tier (Manual...AI)      [SemiAuto] ─┤        │
│    • Temperature Control (0-1)          [0.95] ─────┤        │
│    • Freshness Hours Since Harvest      [8h] ───────┤        │
│                                                      │        │
│  CALCULATION:                                        ↓        │
│    1. Get base yield from ProductCatalog       [62.0%]       │
│    2. Apply quality penalty                    [-1.0%]       │
│    3. Add skill modifier                       [+3.0%]       │
│    4. Add technology modifier                  [+5.0%]       │
│    5. Subtract temperature penalty             [-0.5%]       │
│    6. Subtract freshness penalty               [-0.5%]       │
│                                                ─────────      │
│  GROSS YIELD:                                   68.0%        │
│                                                               │
│  REJECT CALCULATION:                                          │
│    Base reject rate: 5%                                       │
│    Quality penalty: +1%                                       │
│    Skill reduction: -1%                                       │
│                                                ─────────      │
│  REJECT RATE:                                   5.0%         │
│                                                               │
│  NET YIELD = Gross × (1 - Reject)              63.96%        │
│                                                               │
│  OUTPUT: 10,000 kg × 63.96% = 6,396 kg net output           │
└───────────────────────────────────────────────────────────────┘
```

---

## Product Mix Optimizer

```
┌────────────────────────────────────────────────────────────────┐
│              PRODUCT MIX OPTIMIZER                             │
│                                                                │
│  INPUTS:                                                       │
│    • Plant capacity:        50,000 kg/day                      │
│    • Raw material cost:     60 NOK/kg                          │
│    • Strategy:              MaximizeProfit                     │
│    • Market conditions:     [Sashimi ban, Smoked seasonal]     │
│                                                                │
│  AVAILABLE PRODUCTS (Tier 4 plant):                            │
│                                                                │
│   Product       Profit/kg  Margin  Risk  Demand  Market Open  │
│   ─────────────────────────────────────────────────────────── │
│   Sashimi       120 NOK    45%     0.40   0.5×      ✓         │
│   Smoked        80 NOK     38%     0.25   1.8×      ✓         │
│   Gravlax       90 NOK     42%     0.30   1.0×      ✓         │
│   Fillets       45 NOK     25%     0.18   1.0×      ✓         │
│   Portions      55 NOK     32%     0.22   1.0×      ✓         │
│   Roe           150 NOK    70%     0.45   0.8×      ✓         │
│                                                                │
│  STRATEGY: MaximizeProfit                                      │
│    Sort by: Profit/kg × Demand × (1 - Risk×0.5)              │
│                                                                │
│  RECOMMENDATIONS:                                              │
│                                                                │
│   1. Smoked Salmon      20,000 kg  (40% allocation)           │
│      Expected profit:   1,600,000 NOK                          │
│      Reasoning:         High profit, seasonal demand peak      │
│                                                                │
│   2. Gravlax           15,000 kg  (30% allocation)            │
│      Expected profit:   1,350,000 NOK                          │
│      Reasoning:         Premium margin, stable demand          │
│                                                                │
│   3. Portions          10,000 kg  (20% allocation)            │
│      Expected profit:   550,000 NOK                            │
│      Reasoning:         Moderate margin, low risk              │
│                                                                │
│   4. Fillets            5,000 kg  (10% allocation)            │
│      Expected profit:   225,000 NOK                            │
│      Reasoning:         Stable baseline, fills capacity        │
│                                                                │
│  PORTFOLIO METRICS:                                            │
│    Total expected profit:    3,725,000 NOK/day                │
│    Weighted avg margin:      36.2%                             │
│    Portfolio risk score:     0.28 (Low-Medium)                │
│    Diversification:          4 products                        │
│                                                                │
│  ⚠️ NOTE: Sashimi skipped due to low demand (Japan ban)       │
│  ✓ TIP: Smoked allocation high (Christmas season demand)      │
└────────────────────────────────────────────────────────────────┘
```

---

## Brand Reputation System

```
┌──────────────────────────────────────────────────────────────┐
│                   BRAND REPUTATION                           │
│                                                              │
│  Brand: "Arctic Premium Salmon"                             │
│  Strategy: Premium                                           │
│                                                              │
│  REPUTATION SCORES (0-100):                                  │
│    Quality:         ████████████░░░░░░░  75                 │
│    Sustainability:  ███████████░░░░░░░░  70                 │
│    Reliability:     ████████████████░░░  80                 │
│    Innovation:      ██████████░░░░░░░░░  60                 │
│                                                              │
│  OVERALL SCORE (weighted by strategy):                       │
│    = 0.5×Quality + 0.2×Sustainability + 0.2×Reliability     │
│      + 0.1×Innovation                                        │
│    = 0.5×75 + 0.2×70 + 0.2×80 + 0.1×60                      │
│    = 73.5 (Strong)                                           │
│                                                              │
│  CERTIFICATIONS:                                             │
│    ✓ ASC (Aquaculture Stewardship Council)                  │
│    ✓ Organic Norway                                          │
│                                                              │
│  AWARDS:                                                     │
│    🏆 Best Norwegian Salmon 2026                            │
│    🏆 Sustainability Award 2025                             │
│                                                              │
│  PRICING POWER CALCULATION:                                  │
│    Base multiplier:           1.00                           │
│    + Brand strength bonus:    +0.18  (from 73.5 score)     │
│    + Strategy alignment:      +0.10  (Premium product)      │
│    + Certification bonus:     +0.06  (2 certs × 3%)         │
│    + Award bonus:             +0.04  (2 awards × 2%)        │
│                              ───────                         │
│    TOTAL MULTIPLIER:          1.38×                          │
│                                                              │
│  IMPACT ON REVENUE:                                          │
│    Base revenue (sashimi):    450 NOK/kg                     │
│    × Brand multiplier:        1.38×                          │
│    = Branded price:           621 NOK/kg                     │
│    Extra revenue:             +171 NOK/kg (+38%)             │
│                                                              │
│  RECENT EVENTS:                                              │
│    ✓ 2026-01-15: Award Won (Best Norwegian Salmon)          │
│    ✓ 2025-11-03: Certification Earned (ASC)                 │
│    ⚠ 2025-08-22: Quality Issue (Minor recall)               │
└──────────────────────────────────────────────────────────────┘
```

---

## Technology Progression Path

```
Manual Processing
     │
     │  CAPEX: +1.5M NOK
     │  Benefits: +5% yield, -20% labor
     ↓
Semi-Automated
     │
     │  CAPEX: +2.0M NOK
     │  Benefits: +10% yield, -40% labor
     ↓
Automated
     │
     │  CAPEX: +2.5M NOK
     │  Benefits: +15% yield, -60% labor
     ↓
Fully Automated
     │
     │  CAPEX: +3.0M NOK
     │  Benefits: +20% yield, -70% labor
     ↓
AI-Optimized
     
     Total yield improvement: +20%
     Total labor reduction: -70%
     Total CAPEX investment: ~10M NOK
     Payback period: 2-3 years
```

---

## Upgrade Decision Tree

```
Processing Plant (Current: Tier 2, Fillet Line)
│
├─ TIER UPGRADE → Tier 3 (Value-Added)
│   Cost: 8M NOK
│   Downtime: 30 days
│   Unlocks: Smoked, Gravlax, Canned
│   Expected ROI: 2.5 years
│
├─ CAPACITY EXPANSION → +50% throughput
│   Cost: 1.75M NOK (35% of original)
│   Downtime: 14 days
│   Benefit: 25,000 → 37,500 kg/day
│   Expected ROI: 1.8 years
│
├─ AUTOMATION → Upgrade Line 1 to Semi-Auto
│   Cost: 2.0M NOK
│   Downtime: 7 days
│   Benefits: +5% yield, -20% labor
│   Annual labor savings: 400k NOK
│   Expected ROI: 2.2 years
│
├─ QUALITY CONTROL → Install QC system
│   Cost: 500k NOK
│   Downtime: 3 days
│   Benefit: +5% price premium
│   Expected ROI: 1.5 years
│
└─ CERTIFICATION → Obtain ASC certification
    Cost: 300k NOK
    Downtime: 0 days (audit only)
    Benefit: +8% price premium (certified products)
    Expected ROI: 1.2 years

OPTIMIZER RECOMMENDATION:
  1. Get Certification first (fastest ROI, no downtime)
  2. Install QC system (quick win)
  3. Expand capacity (leverage existing efficiency)
  4. Automate high-volume lines
  5. Upgrade tier when ready for premium products
```

---

## Complete Product Value Chain

```
                    SALMON FARMING VALUE CHAIN
                                
Raw Fish (10,000 kg @ 60 NOK/kg = 600k NOK raw cost)
│
├─ PRIMARY PROCESSING (Tier 1-2)
│  │
│  ├─ Fresh Whole (9,500 kg, 60 NOK/kg)
│  │  Revenue: 570k, Margin: 10%, Profit: 57k
│  │
│  ├─ HOG (8,500 kg, 70 NOK/kg)
│  │  Revenue: 595k, Margin: 15%, Profit: 89k
│  │
│  ├─ Fillets (6,200 kg, 150 NOK/kg)
│  │  Revenue: 930k, Margin: 25%, Profit: 233k ← VOLUME PLAY
│  │
│  ├─ Fillets Skinless (5,800 kg, 165 NOK/kg)
│  │  Revenue: 957k, Margin: 28%, Profit: 268k
│  │
│  ├─ Portions (5,500 kg, 180 NOK/kg)
│  │  Revenue: 990k, Margin: 32%, Profit: 317k
│  │
│  └─ Frozen (9,000 kg, 55 NOK/kg)
│     Revenue: 495k, Margin: 12%, Profit: 59k
│
├─ VALUE-ADDED PROCESSING (Tier 3)
│  │
│  ├─ Cold Smoked (5,000 kg, 280 NOK/kg)
│  │  Revenue: 1,400k, Margin: 38%, Profit: 532k
│  │
│  ├─ Hot Smoked (4,500 kg, 320 NOK/kg)
│  │  Revenue: 1,440k, Margin: 40%, Profit: 576k
│  │
│  ├─ Gravlax (5,200 kg, 350 NOK/kg)
│  │  Revenue: 1,820k, Margin: 42%, Profit: 764k ← PREMIUM PLAY
│  │
│  └─ Canned (5,500 kg, 95 NOK/kg)
│     Revenue: 523k, Margin: 35%, Profit: 183k
│
├─ PREMIUM PRODUCTS (Tier 4)
│  │
│  ├─ Sashimi (4,800 kg, 450 NOK/kg)
│  │  Revenue: 2,160k, Margin: 45%, Profit: 972k ← LUXURY PLAY
│  │
│  ├─ Smoked Pâté (3,500 kg, 220 NOK/kg)
│  │  Revenue: 770k, Margin: 50%, Profit: 385k
│  │
│  ├─ Salmon Jerky (3,000 kg, 380 NOK/kg)
│  │  Revenue: 1,140k, Margin: 48%, Profit: 547k
│  │
│  ├─ Ready Meals (4,000 kg, 260 NOK/kg)
│  │  Revenue: 1,040k, Margin: 52%, Profit: 541k
│  │
│  └─ Roe (400 kg, 400 NOK/kg)
│     Revenue: 160k, Margin: 70%, Profit: 112k (SEASONAL)
│
└─ BYPRODUCTS (Tier 5)
   │
   ├─ Fish Oil (800 kg, 120 NOK/kg)
   │  Revenue: 96k, Margin: 65%, Profit: 62k ← HIGH MARGIN
   │
   ├─ Fish Meal (2,000 kg, 15 NOK/kg)
   │  Revenue: 30k, Margin: 40%, Profit: 12k
   │
   └─ Fish Skin (600 kg, 80 NOK/kg)
      Revenue: 48k, Margin: 55%, Profit: 26k

STRATEGIES:
  • Volume Play:    Process as fillets (6,200 kg, 233k profit)
  • Premium Play:   Sashimi (4,800 kg, 972k profit) ← 4× profit!
  • Balanced:       Mix fillets + smoked + byproducts
  • Risk-Averse:    HOG + frozen (stable, low margin)
  • Byproduct:      Extract oil from trim (65% margin boost)
```

---

## System Integration Map

```
┌─────────────────────────────────────────────────────────────┐
│                    LAKSKONGEN SIM                           │
│                                                             │
│  ┌──────────────┐       ┌──────────────┐                  │
│  │   BIOLOGY    │       │   MARKET     │                  │
│  │  (Farming)   │──────→│  (Pricing)   │                  │
│  └──────────────┘       └──────┬───────┘                  │
│        │                       │                           │
│        │  Fish harvest         │  Product prices           │
│        │  Quality score        │  Demand conditions        │
│        ↓                       ↓                           │
│  ┌─────────────────────────────────────────────┐          │
│  │           PROCESSING (T78)                  │          │
│  │                                             │          │
│  │  ┌──────────────────┐  ┌─────────────────┐│          │
│  │  │ ProductCatalog   │  │ YieldCalculator ││          │
│  │  │ (17 products)    │  │ (multi-factor)  ││          │
│  │  └──────────────────┘  └─────────────────┘│          │
│  │                                             │          │
│  │  ┌──────────────────────────────────────┐ │          │
│  │  │      ProcessingPlant                 │ │          │
│  │  │   ┌─────────────────────────────┐   │ │          │
│  │  │   │ ProductionLine (fillet)     │   │ │          │
│  │  │   │ ProductionLine (smoked)     │   │ │          │
│  │  │   │ ProductionLine (oil)        │   │ │          │
│  │  │   └─────────────────────────────┘   │ │          │
│  │  └──────────────────────────────────────┘ │          │
│  │                                             │          │
│  │  ┌──────────────────┐  ┌─────────────────┐│          │
│  │  │ ProductMix       │  │ BrandReputation ││          │
│  │  │ Optimizer        │  │ (pricing power) ││          │
│  │  └──────────────────┘  └─────────────────┘│          │
│  └─────────────┬─────────────────────────────┘          │
│                │                                          │
│                │  Revenue, Profit, CAPEX                  │
│                ↓                                          │
│  ┌──────────────────────────────────────────┐            │
│  │           FINANCE                        │            │
│  │  (P&L, Balance Sheet, Cash Flow)         │            │
│  └──────────────────────────────────────────┘            │
│                │                                          │
│                │  Financial reports                       │
│                ↓                                          │
│  ┌──────────────────────────────────────────┐            │
│  │              UI / PLAYER                 │            │
│  │  (Dashboards, Decisions, Investments)    │            │
│  └──────────────────────────────────────────┘            │
└─────────────────────────────────────────────────────────────┘

DATA FLOW:
  Harvest → Quality Score → Yield Calculation → Production Batch
  Market → Demand/Price → Product Mix Optimizer → Recommendations
  Brand → Reputation Events → Pricing Power → Revenue Multiplier
  Revenue/Costs → Finance → P&L → Player Dashboard
```

---

## Quick Decision Guide

### "I want HIGH PROFIT NOW"
→ Upgrade to Tier 4 (Premium)  
→ Add Sashimi line (45% margin)  
→ Hire Master workers  
→ Build Premium brand  
→ Target Japan/USA markets  
⚠️ Risk: Export restrictions, quality sensitivity

### "I want STABLE CASH FLOW"
→ Stay Tier 1-2 (Basic/Fillet)  
→ Process HOG + Fillets (15-25% margin)  
→ Invest in automation (reduce labor)  
→ Commodity brand (low marketing cost)  
→ EU markets (stable demand)  
✓ Safe: Low risk, consistent returns

### "I want MAXIMUM EFFICIENCY"
→ Tier 5 (Full Integration)  
→ Extract byproducts (oil 65%, meal 40%)  
→ Automate everything (AI-Optimized)  
→ Minimal labor (70% reduction)  
→ Zero waste (use trim/heads/frames)  
✓ Best: Highest margins long-term

### "I want to BUILD A BRAND"
→ Tier 3-4 (Value-Added/Premium)  
→ Focus on Gravlax/Smoked (premium products)  
→ Get certifications (ASC, Organic)  
→ Premium/Luxury brand strategy  
→ Win awards (enter competitions)  
✓ Goal: 30-40% pricing power

---

**Systems thinking made visual.** 🎯
