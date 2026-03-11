# 🐟 LAKSKONGEN — Salmon Farming Simulator
## Game Design Document v1.0

---

## 1. GAME OVERVIEW

**Title:** Lakskongen (The Salmon King)
**Genre:** Business Simulation / Tycoon / Strategy
**Platform:** PC (Steam), later console ports
**Engine:** TBD (Godot 4 or Unity recommended)
**Target Audience:** Strategy/tycoon gamers, ages 16+, fans of Factorio, Farming Simulator, Anno series
**Unique Selling Point:** The first deep, realistic salmon aquaculture simulator based on Norway's $15B+ industry. Real genetics, real biology, real market dynamics. Think "Factorio meets the Norwegian fjords."

**Tagline:** *From Egg to Export. Build your salmon empire in the fjords of Norway.*

---

## 2. CORE GAME LOOP

```
GENETICS → HATCHERY → SMOLT → SEA PHASE → HARVEST → PROCESSING → SALES → REINVEST
    ↑                                                                          |
    └──────────────────────────────────────────────────────────────────────────┘
```

### Phase Progression:
1. **Broodstock & Genetics** — Select/breed parent fish for traits
2. **Egg & Alevin** — Incubation, hatch rate management
3. **Fry & Parr** — Freshwater rearing, vaccination
4. **Smoltification** — Critical transition phase
5. **Sea Phase** — 12-18 months in net pens (the main gameplay)
6. **Harvest** — Timing, size optimization, quality grading
7. **Processing** — HOG, fillets, smoked, value-added
8. **Sales & Export** — Market selection, price negotiation, contracts
9. **Reinvestment** — Expand, upgrade, research

---

## 3. GENETICS SYSTEM

### 3.1 Breeding Traits (based on real AquaGen/SalmoBreed programs)

Each fish has a genome with traits scored 1-100:

| Trait | Effect | Trade-offs |
|-------|--------|------------|
| **Growth Rate** | Faster time to harvest weight | Higher feed consumption |
| **Disease Resistance** | Lower mortality from ISA, PD, IHN | Slower growth potential |
| **Sea Lice Resistance** | Fewer lice per fish, less treatment needed | Genetic bottleneck risk |
| **Fillet Quality** | Higher superior grade %, better color | May reduce growth rate |
| **Feed Conversion (FCR)** | Less feed per kg gained | Hard to breed alongside growth |
| **Stress Tolerance** | Survives handling, delousing better | — |
| **Late Maturation** | Prevents early sexual maturation (grilse) | Longer generation time |
| **Cold Tolerance** | Better survival in northern zones | Slower in warmer water |

### 3.2 Breeding Mechanics
- **Broodstock selection** — Pick males/females based on trait profiles
- **Family-based breeding** — Track lineages, avoid inbreeding (inbreeding coefficient)
- **Genomic selection** — Unlock via R&D; allows marker-assisted selection
- **Ova suppliers** — Buy eggs from AquaGen-type companies (different strains)
- **Generation time** — 4 years per breeding generation (accelerated in-game)
- **Triploid fish** — Sterile fish option (no genetic escape risk, but welfare concerns)

### 3.3 Genetic Events
- Escaped fish → wild salmon hybridization → regulatory penalties
- Random mutations (positive or negative)
- New disease strains require new resistance breeding

---

## 4. PRODUCTION LIFECYCLE

### 4.1 Freshwater Phase (Hatchery)

**Duration:** 10-16 months (in-game accelerated)

| Stage | Duration | Key Mechanics |
|-------|----------|---------------|
| **Eggs** | 60 days (500 degree-days) | Temperature control, water quality, fungal infection risk |
| **Alevin** | 4-6 weeks | Yolk sac absorption, first feeding timing critical |
| **Fry** | 3-4 months | Feeding regime, density management, vaccination |
| **Parr** | 4-6 months | Growth monitoring, photoperiod manipulation |
| **Smolt** | Smoltification trigger | Light program + size threshold → seawater readiness |

**Hatchery Buildings:**
- Egg incubation trays
- Start-feeding tanks
- On-growing tanks
- Vaccination station (injectable vaccines)
- RAS (Recirculating Aquaculture Systems) — premium upgrade
- Photoperiod control rooms

**Key Decisions:**
- When to start light manipulation for smoltification
- Vaccination program (which diseases to vaccinate against)
- Smolt size at transfer (bigger = better sea survival, but more expensive)
- RAS vs flow-through systems

### 4.2 Sea Phase (Main Gameplay)

**Duration:** 12-18 months
**Target:** 4-6 kg harvest weight (HOG)

**Net Pen Management:**
- Pen capacity: 200,000 fish standard, up to 1M in large operations
- Stocking density: max 25 kg/m³ (regulatory limit)
- Pen types: Standard circular, semi-closed, offshore (Ocean Farm), land-based (RAS)
- Mooring systems, net integrity, predator nets

**Environmental Parameters (continuous simulation):**

| Parameter | Optimal Range | Effect |
|-----------|---------------|--------|
| **Sea Temperature** | 8-14°C | Growth rate, disease risk, lice reproduction |
| **Oxygen Saturation** | >7 mg/L | Below 6 = stress, below 4 = mortality |
| **Salinity** | 32-35‰ | Freshwater events can stress fish |
| **Current Speed** | 5-50 cm/s | Good flow = O2 renewal; too high = stress |
| **Light/Photoperiod** | Seasonal | Affects appetite, maturation, lice behavior |
| **Algal Blooms** | Random events | Can cause mass mortality |
| **Jellyfish** | Seasonal/random | Gill damage, mortality |

### 4.3 Feed Management

**Feed = 50-65% of total production cost.** This is the biggest controllable expense.

**Feed Composition (player can customize):**

| Ingredient | Cost | Effect |
|------------|------|--------|
| Fishmeal | $$$ | High protein, best FCR, limited supply |
| Fish oil | $$$ | Omega-3 content, flesh color |
| Soy protein | $ | Cheap but can cause gut inflammation |
| Rapeseed oil | $ | Cheap, reduces omega-3 |
| Insect meal | $$ | Sustainable, good protein, limited supply |
| Microalgae | $$$ | Premium omega-3 source, future-tech |
| Astaxanthin | $$ | Pink flesh color (critical for market) |
| Single-cell protein | $$ | Novel, sustainability bonus |

**Feed Mechanics:**
- **FCR (Feed Conversion Ratio):** Industry average 1.15:1 — lower is better
- **Feed waste:** Overfeeding = pollution = regulatory issues + cost waste
- **Feeding systems:** Manual, semi-auto, AI-driven (upgrades)
- **Feed supplier contracts** — lock in prices or go spot market
- **Seasonal feeding curves** — fish eat more in summer, almost nothing in winter

### 4.4 Fish Health & Disease

**Major Diseases (all real):**

| Disease | Type | Mortality | Prevention | Treatment |
|---------|------|-----------|------------|-----------|
| **ISA** (Infectious Salmon Anaemia) | Virus | Up to 90% | Vaccination, biosecurity | Mandatory cull |
| **PD** (Pancreas Disease) | Virus (SAV) | 10-20% | Vaccination | No treatment, manage mortality |
| **IHN** | Virus | 5-70% | Vaccination | None |
| **AGD** (Amoebic Gill Disease) | Parasite | 5-15% | Freshwater bath | Freshwater treatment |
| **CMS** (Cardiomyopathy) | Virus | 5-20% | Genetics | None |
| **Winter Ulcers** | Bacteria | 5-15% | Vaccination | Antibiotics (reputation cost) |
| **Sea Lice** | Parasite | Indirect | See below | See below |

### 4.5 Sea Lice System (Critical mechanic)

Sea lice (Lepeophtheirus salmonis) are THE central challenge in Norwegian salmon farming.

**Regulatory limit:** Max 0.5 adult female lice per fish (averaged across site)

**Lice Reproduction Model:**
- Temperature-dependent: faster in warm water (>12°C)
- Seasonal peaks: spring/summer
- Cross-infection between nearby farms
- Wild salmon migration = lice dispersal

**Delousing Methods (all have trade-offs):**

| Method | Cost | Mortality Risk | Efficacy | Notes |
|--------|------|----------------|----------|-------|
| **Hydrogen peroxide bath** | Low | Medium | Declining | Resistance developing |
| **Thermolicer** (warm water) | Medium | High (3-5%) | Good | Most controversial — fish welfare |
| **Hydrolicer** (pressure wash) | Medium | Medium | Good | Mechanical |
| **Cleaner fish** (wrasse/lumpfish) | Medium | Low | Variable | Biodiversity concerns, seasonal |
| **Laser delousing** | High | Very low | Good | Stingray system — premium tech |
| **Freshwater treatment** | Medium | Low | Good | Also treats AGD |
| **Medicated feed** | Medium | Low | Declining | Resistance, environmental cost |
| **Lice skirts** (prevention) | Low ongoing | None | Moderate | Barrier around top of pen |
| **Deep lights/feeding** | Low | None | Moderate | Keep fish below lice zone |
| **Semi-closed containment** | Very high | Very low | Excellent | Game-changer technology |

**Lice Consequences:**
- Exceed limit → mandatory treatment → fish stress → mortality
- Exceed limit repeatedly → production reduction order
- Affects wild salmon → environmental penalty → traffic light downgrade
- Treatment mortality: Norway lost 16.7% of farmed salmon in 2023 (62.8M fish!)

---

## 5. REGULATORY SYSTEM

### 5.1 Traffic Light System (Real Norwegian system)

Norway's coast is divided into **13 production zones**, each assigned a color:

| Color | Meaning | Effect on Player |
|-------|---------|------------------|
| 🟢 **Green** | Low lice impact on wild salmon | Can increase MAB by 6% |
| 🟡 **Yellow** | Moderate impact | Maintain current MAB |
| 🔴 **Red** | High impact | Must reduce MAB by 6% |

**Reassessment every 2 years** (in-game: every few months)

### 5.2 Licenses (MAB — Maximum Allowable Biomass)

- Each license = 780 tonnes MAB (standard)
- License cost: NOK 150-250 million at auction (!) 
- Green licenses (sustainability bonus): cheaper
- Development licenses: free but must innovate
- Offshore licenses: new category, larger MAB

**Player must manage:**
- Total biomass across all sites ≤ licensed MAB
- Site-specific biomass limits
- Fallow periods between cycles (mandatory)
- Distance requirements between sites

### 5.3 Other Regulations
- Environmental monitoring (benthic surveys)
- Escape reporting and prevention
- Fish welfare standards
- Antibiotic use reporting (reputation impact)
- Resource rent tax (introduced 2023: 25% on profits above threshold)

---

## 6. TECHNOLOGY TREE

### Tier 1 — Startup
- Basic hatchery (flow-through)
- Standard net pens
- Manual feeding
- Basic health monitoring
- Small processing (gutting only)

### Tier 2 — Established
- Semi-automatic feeding systems
- Underwater cameras
- Cleaner fish integration
- Lice skirts
- Vaccination programs
- HOG processing line

### Tier 3 — Industrial
- RAS hatchery (recirculating)
- AI-driven feed optimization
- Laser delousing
- Genomic selection breeding
- Fillet processing line
- Cold chain logistics
- Fish Pool futures trading

### Tier 4 — Innovation Leader
- Semi-closed containment systems
- Offshore farming (Ocean Farm concept)
- Full RAS sea-phase (land-based)
- Precision breeding / gene editing (controversial)
- Automated harvest systems
- Global distribution network
- Branding & retail partnerships

---

## 7. MARKET & SALES SYSTEM

### 7.1 Salmon Pricing

**Price is dynamic and based on real market mechanics:**

- **Spot market** (Nasdaq Salmon Index / Fish Pool style)
  - Weekly price fluctuations
  - Seasonal patterns: high around Easter/Christmas, low in summer
  - Price range: NOK 50-140/kg historically
  - Size-dependent pricing (3-6 kg premium)

- **Contract sales** — Lock in price for X months
  - Lower average but predictable
  - Processor contracts, retail chain deals

- **Futures trading** — Speculate or hedge
  - Buy/sell futures contracts
  - Risk management tool

### 7.2 Quality Grading

| Grade | Price Premium | Criteria |
|-------|---------------|----------|
| **Superior** | 100% (base) | Perfect condition, good color, no defects |
| **Ordinary** | -15 to -25% | Minor defects, lighter color |
| **Production** | -40 to -60% | Significant defects, deformed, wounds |

**Factors affecting grade:**
- Genetics (fillet quality trait)
- Handling during harvest
- Disease history
- Delousing treatment damage
- Feed composition (astaxanthin → color)
- Harvest timing and method

### 7.3 Export Markets

| Market | Volume Share | Price Sensitivity | Preferences |
|--------|-------------|-------------------|-------------|
| 🇪🇺 **EU (France, Spain, Italy)** | ~65% | Medium | Fresh whole, portion size |
| 🇵🇱 **Poland** | ~15% | High | Processing hub, re-export |
| 🇺🇸 **USA** | ~8% | Low | Fillets, smoked, premium |
| 🇯🇵 **Japan** | ~5% | Low | Sashimi grade, ultra-fresh |
| 🇨🇳 **China** | ~3% | Medium | Growing, price-sensitive |
| 🇰🇷 **South Korea** | ~2% | Medium | Fresh, growing market |
| 🇧🇷 **Brazil** | ~2% | High | Volume market |

**Market Events:**
- Trade disputes / tariffs
- Currency fluctuations (NOK/EUR/USD)
- Competitor supply shocks (Chile disease, Scottish issues)
- Consumer trends (sustainability labels, organic)
- Seasonal demand spikes

### 7.4 Value-Added Products

| Product | Margin | Requirements |
|---------|--------|--------------|
| Whole fresh (HOG) | Base | Gutting line |
| Fresh fillets | +20-30% | Fillet line, skilled labor |
| Smoked salmon | +40-60% | Smoking facility |
| Sashimi grade | +50-80% | Ultra-cold chain, Japan certification |
| Portions/retail | +30-50% | Packaging line, retail contracts |
| Fish oil/meal (byproducts) | Revenue stream | Rendering plant |

---

## 8. FINANCIAL SYSTEM

### 8.1 Cost Structure (based on real Norwegian data)

**Production cost per kg (2024 baseline): ~NOK 55-60/kg**

| Cost Component | % of Total | Player Can Optimize |
|----------------|------------|---------------------|
| **Feed** | 50-55% | Recipe, FCR, supplier deals |
| **Labor** | 10-12% | Automation, efficiency |
| **Sea lice treatment** | 8-12% | Prevention, technology |
| **Smolt cost** | 8-10% | Own hatchery vs. buy |
| **Depreciation** | 5-8% | Equipment choices |
| **Insurance** | 2-3% | Risk management |
| **Other (transport, admin)** | 8-12% | Logistics optimization |

### 8.2 Revenue Streams
- Fish sales (primary)
- License value appreciation
- Byproduct sales (fish oil, meal, skin)
- Carbon credits / sustainability premiums
- Processing services for other farms

### 8.3 Financial Instruments
- Bank loans (for expansion)
- License purchase/sale
- Fish Pool futures & options
- Insurance products (mortality, weather)
- Government grants (innovation, green tech)
- Resource rent tax management

### 8.4 Key KPIs
- EBIT per kg
- Production cost per kg
- Survival rate (%)
- FCR (Feed Conversion Ratio)
- Harvest weight distribution
- Superior grade %
- MAB utilization %
- Lice counts

---

## 9. WORLD & MAP

### 9.1 Norwegian Coast

The map covers Norway's coastline from Rogaland (south) to Finnmark (north), divided into the real **13 production zones.**

**Each zone has unique characteristics:**

| Zone | Region | Temperature | Lice Pressure | Current Status |
|------|--------|-------------|---------------|----------------|
| 1-2 | South (Rogaland) | Warmer | High | Often Red/Yellow |
| 3-5 | West (Hordaland/Sogn) | Moderate | High | Mixed |
| 6-7 | Mid (Møre/Trøndelag) | Moderate | Moderate | Often Green |
| 8-10 | Nordland | Cool | Lower | Mostly Green |
| 11-12 | Troms | Cold | Low | Green |
| 13 | Finnmark | Very Cold | Very Low | Green |

**Site selection factors:**
- Water temperature profile (seasonal curves)
- Current speed and direction
- Depth and seabed type
- Distance to other farms (lice spread)
- Distance to wild salmon rivers (regulatory sensitivity)
- Logistics (distance to processing, airports)
- Weather exposure (storms, waves)

### 9.2 Seasons & Weather

Full seasonal cycle with real Norwegian weather patterns:
- **Spring:** Smolt transfer season, lice increasing, wild smolt migration
- **Summer:** Peak growth, peak lice, algal bloom risk, midnight sun
- **Autumn:** Harvest season, declining temperatures, storm risk
- **Winter:** Minimal growth, low lice, winter ulcers, polar night in north

**Random Events:**
- Algal blooms (mass mortality)
- Storm damage to pens (escapes)
- Jellyfish invasions
- Predator attacks (seals, sea eagles)
- Equipment failure

---

## 10. CAMPAIGN & PROGRESSION

### 10.1 Starting Scenarios

**Scenario 1: "The Dreamer"** (Easy)
- Start with 1 license, small hatchery, 1 sea site
- NOK 50M starting capital
- Location: Zone 7 (Trøndelag) — Green zone, moderate conditions

**Scenario 2: "The Inheritor"** (Medium)
- Inherit a struggling family farm with 3 licenses
- Debt, aging equipment, disease issues
- Location: Zone 3 (Hordaland) — Yellow zone

**Scenario 3: "The Investor"** (Hard)
- Start with capital only, no licenses or sites
- Must acquire everything from scratch
- Any location, premium prices for licenses

**Scenario 4: "The Pioneer"** (Expert)
- Offshore/land-based focus
- Development licenses only
- Must innovate to survive

### 10.2 Milestones & Goals

1. **First Harvest** — Survive your first production cycle
2. **Profitable Year** — EBIT > 0 for a full year
3. **Regional Player** — 5+ licenses, own hatchery
4. **Innovation Award** — Develop/deploy new technology
5. **Green Champion** — All sites in green zones, sustainability score A
6. **Market Maker** — Active in 5+ export markets, own processing
7. **Industry Titan** — 20+ licenses, vertically integrated
8. **Salmon King** — Top ranking, highest cumulative profit

### 10.3 Sandbox Mode
- Unlimited starting options
- Customizable market conditions
- Speed controls
- No fail state — pure optimization playground

---

## 11. COMPETITOR AI

**AI-controlled companies based on real industry structure:**

| Type | Examples | Behavior |
|------|----------|----------|
| **Mega-corp** | Mowi-type, SalMar-type | Aggressive expansion, price-setters |
| **Regional** | Mid-size companies | Moderate, zone-focused |
| **Family farms** | Small operators | Conservative, quality-focused |
| **Land-based** | Innovators | High-tech, high-cost |
| **Offshore** | Pioneers | Risk-taking, large scale |

Competitors affect:
- License auction prices
- Local lice pressure
- Labor market
- Market prices (supply)
- Innovation pace

---

## 12. REPUTATION & SUSTAINABILITY

### 12.1 Reputation Score (0-100)

Affected by:
- Fish welfare (mortality rate, handling)
- Environmental impact (escapes, lice on wild fish)
- Antibiotic use
- Sustainability certifications (ASC, organic)
- Community relations
- Media events (positive/negative)

### 12.2 Impact of Reputation
- Premium pricing access
- Easier license acquisition
- Consumer brand value
- Regulatory leniency
- Investor confidence
- Employee recruitment

---

## 13. MULTIPLAYER (Phase 2)

- **Competitive:** Compete in same market for licenses, sites, market share
- **Cooperative:** Share processing, logistics, breeding programs
- **Market PvP:** Fish Pool trading against other players

---

## 14. UI/UX CONCEPT

### Main Views:
1. **Map View** — Norwegian coast, zoom to zones, sites, pens
2. **Farm View** — 3D/isometric view of individual farm site
3. **Hatchery View** — Indoor facility management
4. **Dashboard** — KPIs, financials, alerts
5. **Market View** — Price charts, contracts, export map
6. **Genetics Lab** — Breeding interface, family trees
7. **Research Hub** — Technology tree, R&D projects

### Visual Style:
- Clean, modern Scandinavian aesthetic
- Beautiful fjord landscapes
- Underwater views of fish in pens
- Weather effects (rain, snow, northern lights)
- Day/night and seasonal light changes

---

## 15. AUDIO

- Ambient: Ocean waves, seagulls, wind, rain
- Regional Norwegian folk-inspired soundtrack
- Dynamic music: calm during planning, tense during crises
- Fish feeding sounds, boat engines, processing plant ambience

---

## 16. MONETIZATION STRATEGY (Commercial Game)

**Premium game:** NOK 299-399 / $29-39 on Steam

**DLC roadmap:**
1. **"Chile Expansion"** — Different climate, regulations, market access
2. **"Scotland & Faroes"** — New maps, UK/EU market focus
3. **"Future Tech"** — Gene editing, AI farming, vertical ocean farms
4. **"The Trading Floor"** — Deep market/trading mechanics
5. **"Multiplayer Update"** — Competitive/co-op modes

---

## 17. COMPETITIVE ANALYSIS

| Game | What They Do | What We Do Better |
|------|--------------|-------------------|
| Aquaculture Land | Basic freshwater fish farm, pixel art | Deep, realistic, salmon-specific, Norwegian |
| Farming Simulator | Agriculture focus | Specialized aquaculture depth |
| Anno series | City building + trade | Biological simulation realism |
| Factorio | Production chains | Living organisms, environmental dynamics |
| Fish Tycoon | Casual breeding game | Industry realism, business simulation |

**Our edge:** No game has EVER done realistic salmon aquaculture. Norway's biggest export after oil, $15B+ industry, zero games about it. Massive untapped market — especially in Norway, where salmon farming is a cultural institution.

---

## 18. DEVELOPMENT PHASES

### Phase 1: Prototype (3-4 months)
- Core loop: hatchery → sea phase → harvest → sell
- Basic genetics (3 traits)
- Single zone, single site
- Simple market (fixed seasonal prices)
- 2D/isometric

### Phase 2: Alpha (3-4 months)
- Full genetics system
- Disease & lice mechanics
- Multiple sites and zones
- Dynamic market with AI competitors
- Feed management
- Basic UI polish

### Phase 3: Beta (3-4 months)
- Full 13-zone map
- Technology tree
- Campaign scenarios
- Processing & value-added
- Regulatory system (traffic lights, MAB)
- Financial instruments
- Balance & polish

### Phase 4: Launch (2-3 months)
- Steam page, marketing
- Achievement system
- Tutorial & onboarding
- Localization (Norwegian, English minimum)
- Community beta testing
- Launch!

**Total estimated timeline:** 12-15 months for v1.0

---

## 19. TECHNICAL REQUIREMENTS

### Minimum Specs (Target):
- OS: Windows 10/11, macOS, Linux
- CPU: Intel i5 / AMD Ryzen 5
- RAM: 8 GB
- GPU: GTX 1060 / RX 580
- Storage: 5 GB

### Key Technical Challenges:
- Biological simulation (thousands of fish, growth models)
- Environmental model (temperature, currents, seasonal)
- Market simulation (price dynamics, AI competitors)
- Save/load system for complex state
- Performance optimization (large fish populations)

---

## 20. REAL DATA REFERENCE SHEET

### Key Industry Numbers (for balancing):

| Metric | Real Value | Source |
|--------|-----------|--------|
| Norway total production | ~1.6M tonnes (2024) | SSB |
| Production cost/kg | NOK 55-60 | EY 2024 |
| Spot price range | NOK 50-140/kg | Nasdaq Salmon Index |
| Average spot 2024 | NOK ~93/kg | Fish Farming Expert |
| FCR industry average | 1.15:1 | GSI |
| Sea phase mortality | 15-17% (2023) | NVI |
| Sea phase duration | 12-18 months | Industry standard |
| Egg to harvest | 2-3 years | Lerøy |
| License cost (auction) | NOK 150-250M | Government auctions |
| MAB per license | 780 tonnes | Regulations |
| Lice limit | 0.5 adult female/fish | Regulations |
| Feed % of cost | 50-55% | EY 2024 |
| Superior grade target | 90-95% | Industry |
| Export to EU | ~65% of volume | Nasdaq/Fish Pool |
| Resource rent tax | 25% (from 2023) | Government |
| # production zones | 13 | Traffic light system |
| # active licenses | ~1,000 | Government |

---

## NEXT STEPS

1. ✅ Research complete — GDD written
2. 🔲 Choose tech stack (engine, language)
3. 🔲 Define data models (fish, genetics, environment, market)
4. 🔲 Build prototype: core loop first
5. 🔲 Create art direction / mood board
6. 🔲 Start coding Phase 1

---

*Document created: 2026-03-10*
*Author: Fredrikke (Chief Software Engineer) & Fredrik J. Hansen (Domain Expert)*
*Version: 1.0*
