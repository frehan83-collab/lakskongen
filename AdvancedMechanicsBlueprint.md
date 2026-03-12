# 🐟 LAKSKONGEN — Advanced Mechanics Blueprint
## Version 1.0 — Master Architecture for Commercial-Grade Simulation

**Date:** 2026‑03‑12  
**Lead Architect:** AI Project Manager & Master Architect  
**Mandate:** Uncompromising emphasis on advanced game mechanics as cornerstone of excellence. Deliver unprecedented depth, realism, emergent complexity, and long‑term replayability.

---

## 1. EXECUTIVE SUMMARY

Lakskongen will be the first salmon‑farming simulator built not as a tycoon‑reskin but as a **research‑grade simulation engine** wrapped in accessible gameplay. Every system must be:

- **Biologically accurate** — validated against Norwegian aquaculture data
- **Computationally sophisticated** — using stochastic differential equations, agent‑based modeling, Monte Carlo forecasting
- **Emergently complex** — simple rules producing unpredictable but logical outcomes
- **Modular & extensible** — ready for DLC, community modding, academic use
- **Performant** — 60+ FPS with 10,000+ fish entities, scalable to 100,000+

This blueprint defines the **seven core mechanical pillars** that distinguish Lakskongen from all existing farming simulators.

---

## 2. CORE MECHANICAL PILLARS

### 2.1 Biologically Accurate Salmon Lifecycle Simulation

**Current state:** Simple stage‑based growth multipliers.  
**Target state:** **Stochastic differential equation (SDE) driven lifecycle** with genetic variance, epigenetic effects, and individual fish tracking.

#### 2.1.1 Growth Model
- **Von Bertalanffy‑Gompertz hybrid** with temperature‑dependent metabolic scaling
- **Individual variance:** Each fish has unique growth trajectory ±15% from genetic baseline
- **Epigenetic memory:** Early‑life stress (temperature shocks, handling) reduces asymptotic size
- **Real‑time metabolism:** Energy budget = intake (feed) − maintenance − activity − waste
- **Feed conversion efficiency:** Dynamic FCR based on protein/fat ratios, pellet size, feeding frequency

#### 2.1.2 Reproduction & Genetics
- **Genomic simulation:** 50+ trait loci with pleiotropic effects, linkage disequilibrium
- **Epistatic interactions:** Trait combinations produce non‑linear effects
- **Inbreeding depression:** Coefficient >0.125 reduces survival, growth, disease resistance
- **Marker‑assisted selection:** Unlock via R&D; predict breeding value with 85% accuracy
- **Triploid induction:** Chemical/ pressure treatments with trade‑offs (welfare vs. escape risk)

#### 2.1.3 Mortality Risks
- **Age‑specific hazard functions:** U‑shaped curve (high early/late, low mid‑life)
- **Competitive mortality:** Density‑dependent survival via contest vs. scramble competition
- **Size‑selective predation:** Birds/seals preferentially take smaller fish
- **Stochastic events:** Storm‑induced cage damage, algal blooms, equipment failure

### 2.2 Dynamic Environmental Systems

**Current state:** Basic temperature/oxygen zones.  
**Target state:** **Real‑time 3D hydro‑biogeochemical model** with particle‑based advection‑diffusion.

#### 2.2.1 Water Column Physics
- **Temperature stratification:** Seasonal thermocline formation/dissolution
- **Oxygen diffusion:** Fick's law with biological oxygen demand (BOD) from waste
- **Current flow:** Navier‑Stokes simplified for fjord bathymetry
- **Particle tracking:** Waste solids settle, create benthic impact zones
- **Light penetration:** Photosynthetically active radiation (PAR) affects algal growth

#### 2.2.2 Water Chemistry
- **Nitrogen cycle:** NH₄⁺ → NO₂⁻ → NO₃⁻ conversion via nitrifying bacteria
- **Phosphorus dynamics:** PO₄³⁻ adsorption/desorption from sediments
- **Carbonate system:** pH, alkalinity, pCO₂ affected by respiration/photosynthesis
- **Toxic metabolites:** NH₃, H₂S accumulation during low flow/high density

#### 2.2.3 Seasonal & Climate Variability
- **Procedural weather:** Markov chain generates realistic Norwegian climate sequences
- **Climate change scenarios:** +2°C, +4°C warming with altered precipitation, storm frequency
- **Ocean acidification:** Reduced pH affects shell formation (calcareous fouling organisms)
- **Harmful algal blooms:** Alexandrium spp. produce saxitoxins (paralytic shellfish poisoning)

### 2.3 Emergent Disease & Biosecurity Mechanics

**Current state:** Simple SIR model with lice interaction.  
**Target state:** **Agent‑based multi‑vector outbreak simulation** with resistance evolution.

#### 2.3.1 Pathogen Propagation
- **Water‑borne transmission:** ISA spreads 5‑15km via currents (advection‑diffusion PDE)
- **Vector‑borne:** Sea lice (Lepeophtheirus salmonis) as mobile disease vectors
- **Fomite transmission:** Equipment, personnel movement between farms
- **Superspreader events:** 20% of infected fish cause 80% of transmission (Pareto distribution)
- **Latent periods:** Infection → symptoms → detection → reporting delays

#### 2.3.2 Antibiotic/Antiparasitic Resistance
- **Selection pressure:** Treatment eliminates susceptible strains, resistant strains proliferate
- **Resistance genes:** Plasmid‑borne resistance transfer between bacterial species
- **Fitness cost:** Resistant strains grow 5‑15% slower in absence of treatment
- **Resistance management:** Rotate drug classes, combination therapies, treatment holidays

#### 2.3.3 Biosecurity Layers
- **Farm‑level:** Fallowing periods, single‑year‑class stocking, disinfectant footbaths
- **Regional:** Synchronized production cycles, coordinated delousing, shared monitoring
- **National:** Movement restrictions, import bans, surveillance programs
- **Economic impact:** Outbreak → price depression → insurance claims → government compensation

### 2.4 Deep Economic & Market Simulation

**Current state:** Basic pricing with seasonality.  
**Target state:** **Global commodity market engine** with futures trading, supply‑chain disruption, player‑influenced price formation.

#### 2.4.1 Supply‑Demand Dynamics
- **Global production:** Norway (55%), Chile (25%), Scotland (10%), Canada (5%), others (5%)
- **Elasticities:** Price elasticity of demand = −0.3 (inelastic), income elasticity = +0.8
- **Substitution effects:** Salmon vs. chicken, pork, plant‑based proteins
- **Seasonal patterns:** Christmas/Easter peaks, summer lull, Chinese New Year demand spike

#### 2.4.2 Price Formation
- **Monte Carlo forecasting:** Geometric Brownian motion with jumps (extreme events)
- **Futures market:** 3‑12‑month forward contracts with basis risk
- **Options trading:** Call/put options for price hedging
- **Market sentiment:** News events (disease outbreaks, trade disputes, sustainability scandals)
- **Player influence:** Large producers (>10% market share) affect spot prices

#### 2.4.3 Multi‑Tier Supply Chains
- **Vertical integration ownership:** 7 tiers with adjacency synergies
- **Contract types:** Cost‑plus, fixed‑price, take‑or‑pay, revenue‑sharing
- **Logistics optimization:** Route planning for trucks, wellboats, container ships
- **Cold‑chain management:** Temperature monitoring, shelf‑life prediction, waste minimization
- **Certification premiums:** ASC, GLOBALG.A.P., organic, carbon‑neutral labels

### 2.5 Player Decision Systems with Cascading Consequences

**Current state:** Simple unlock progression.  
**Target state:** **Branching decision trees** where early choices constrain/expand late‑game possibilities.

#### 2.5.1 Strategic Farm Expansion
- **Location selection:** Fjord vs. open ocean, sheltered vs. exposed, depth, current speed
- **Technology adoption:** Traditional nets → semi‑closed → closed containment → offshore
- **Scale economics:** CAPEX per tonne decreases with size but regulatory scrutiny increases
- **Diversification:** Single‑species vs. multi‑trophic (IMTA: salmon + mussels + kelp)

#### 2.5.2 Investment Choices
- **R&D allocation:** Genetics (long‑term), feed efficiency (medium), disease vaccines (short)
- **Capital structure:** Equity vs. debt, bond issuance, retained earnings, government grants
- **Risk management:** Insurance (storm, disease, price), futures hedging, option strategies
- **M&A strategy:** Horizontal (competitors), vertical (suppliers), conglomerate (unrelated)

#### 2.5.3 Regulatory Compliance & Sustainability
- **Traffic‑light system:** Green (expansion allowed), yellow (conditional), red (no expansion)
- **Environmental taxes:** Nitrogen (NOK 20/kg), phosphorus (NOK 250/kg), carbon (NOK 600/tonne)
- **Stakeholder reputation:** NGOs, local communities, politicians, consumers, investors
- **Sustainability scoring:** 0‑100 index affecting access to premium markets, financing costs

### 2.6 Procedural Event Engine

**Current state:** 40+ random events.  
**Target state:** **Hidden‑state narrative generator** where events emerge from simulation state.

#### 2.6.1 Randomized Crises
- **Biological:** Novel pathogen emergence, antibiotic resistance, algal bloom, jellyfish swarm
- **Environmental:** Storm damage, temperature shock, low oxygen, freshwater inflow
- **Economic:** Price collapse, trade embargo, currency fluctuation, competitor bankruptcy
- **Social:** Protest blockade, media exposé, regulatory investigation, employee strike
- **Technical:** Equipment failure, software hack, power outage, feed contamination

#### 2.6.2 Opportunities
- **Market:** New export market opens, premium buyer appears, competitor distress sale
- **Technological:** Breakthrough vaccine, feed additive, monitoring system, genetic line
- **Regulatory:** Tax incentive, simplified permitting, research grant, certification fast‑track
- **Partnership:** Joint venture, technology license, co‑marketing, research collaboration

#### 2.6.3 Narrative Branches
- **Character‑driven:** NGO activist, regulator, journalist, competitor CEO, scientist
- **Plot arcs:** Multi‑year storylines with escalating stakes, moral dilemmas, reputation effects
- **Hidden variables:** Player choices tracked but not revealed until critical moments
- **Multiple endings:** Industry leader, niche premium, sustainable pioneer, bankrupt, activist‑owned

### 2.7 Scalable Simulation Layers

**Current state:** Single‑scale cohort‑based simulation.  
**Target state:** **Multi‑resolution hierarchy** from individual fish to global markets.

#### 2.7.1 Micro‑Layer (Individual)
- **Agent‑based fish:** Unique ID, genetics, life history, health status, behavior
- **Real‑time metabolism:** Energy budget, feed intake, waste production, growth
- **Behavioral repertoire:** Feeding competition, social hierarchy, stress response
- **Pathogen interaction:** Susceptibility, infection progression, immune response

#### 2.7.2 Meso‑Layer (Farm/Regional)
- **Population dynamics:** Age‑structure, size distribution, genetic diversity
- **Environmental modeling:** Water quality, waste accumulation, benthic impact
- **Economic operations:** Feed scheduling, harvest planning, workforce management
- **Disease epidemiology:** SIR‑like compartment models with spatial spread

#### 2.7.3 Macro‑Layer (Global)
- **Market simulation:** Global supply‑demand, price formation, trade flows
- **Regulatory environment:** Policy changes, certification schemes, tax regimes
- **Climate modeling:** Temperature trends, precipitation patterns, extreme events
- **Industry evolution:** Competitor strategies, technological adoption, consolidation

---

## 3. MODULARITY & EXTENSIBILITY DESIGN

### 3.1 Plugin Architecture
- **Core engine:** Simulation mathematics, data structures, save/load
- **Module system:** Genetics, environment, disease, economics, events as pluggable DLLs
- **Modding API:** Lua/Python scripting, custom UI widgets, new mechanics
- **Data‑driven design:** JSON/YAML configuration for traits, prices, events, narratives

### 3.2 DLC Roadmap
- **Season 1:** Offshore aquaculture (semi‑submersible platforms, wave energy)
- **Season 2:** Land‑based RAS (recirculating systems, urban farming)
- **Season 3:** Multi‑trophic IMTA (salmon + mussels + kelp + sea cucumbers)
- **Season 4:** Genetic engineering (CRISPR‑Cas9, transgenics, gene drives)
- **Season 5:** Climate adaptation (ocean acidification, warming, sea‑level rise)

### 3.3 Academic & Research Use
- **Export formats:** NetCDF for environmental data, CSV for economic, FASTA for genetics
- **Scenario testing:** "What‑if" analysis for policy, climate, disease interventions
- **Parameter calibration:** Bayesian optimization against real Norwegian data
- **Open‑source components:** Core math libraries released under MIT license

---

## 4. TECHNICAL IMPLEMENTATION ROADMAP

### Phase 1 (Month 1‑2): Foundation
- **Stochastic differential equation growth model** (individual fish tracking)
- **3D water column physics** (temperature, oxygen, current simplified)
- **Agent‑based disease transmission** (ISA water‑borne spread)
- **Monte Carlo price forecasting** (basic supply‑demand)

### Phase 2 (Month 3‑4): Depth
- **Genetic simulation** (50+ loci, epistasis, inbreeding)
- **Water chemistry** (nitrogen, phosphorus, carbonate cycles)
- **Resistance evolution** (antibiotic, antiparasitic)
- **Futures market** (forward contracts, hedging)

### Phase 3 (Month 5‑6): Emergence
- **Multi‑vector outbreak simulation** (water, lice, fomites, personnel)
- **Global market engine** (Norway, Chile, Scotland, Canada production)
- **Procedural event generator** (hidden‑state narrative)
- **Multi‑resolution simulation** (micro‑meso‑macro layers)

### Phase 4 (Month 7‑8): Polish
- **Performance optimization** (60 FPS @ 10,000 fish, 30 FPS @ 100,000)
- **UI/UX refinement** (complex systems made accessible)
- **Balance tuning** (challenging but fair progression)
- **Modding tools** (plugin editor, scenario builder)

---

## 5. SUBAGENT TEAM RECONFIGURATION

### 5.1 Role Definitions

#### **Mechanics Architect**
- **Owns:** All simulation formulas, physics engines, emergent behavior logic
- **Deliverables:** SDE growth model, water physics PDEs, genetic algorithms
- **Validation:** Mathematical correctness, numerical stability, computational efficiency
- **Tools:** Julia/Python for prototyping, C# for production

#### **Biology & Environment Specialist**
- **Owns:** Research‑grade salmon aquaculture models
- **Deliverables:** Metabolic budgets, disease progression, water chemistry
- **Validation:** Against published Norwegian data (Institute of Marine Research, Nofima)
- **Tools:** Scientific literature review, data calibration, parameter estimation

#### **Economic Strategist**
- **Owns:** Real‑time market engines with predictive analytics
- **Deliverables:** Supply‑demand curves, price formation, futures trading
- **Validation:** Historical price correlation (Nasdaq Salmon Index)
- **Tools:** Econometric modeling, Monte Carlo simulation, option pricing

#### **Emergence & Balance Engineer**
- **Owns:** Testing for unintended but desirable interactions
- **Deliverables:** Emergent complexity metrics, difficulty curves, exploit prevention
- **Validation:** Playtesting, agent‑based testing, sensitivity analysis
- **Tools:** Automated testing frameworks, scenario generators, balance spreadsheets

#### **Quality & Performance Guardian**
- **Owns:** 60+ FPS simulation, memory efficiency, load‑time optimization
- **Deliverables:** Performance benchmarks, memory profiles, optimization patches
- **Validation:** Stress tests (100,000 fish), long‑term stability (1000+ hours)
- **Tools:** Profilers (dotTrace, PerfView), memory analyzers, GPU optimization

### 5.2 Inter‑Agent Communication Protocol

**Every task handoff must reference:**
1. **Blueprint section** being implemented
2. **Validation criteria** from this document
3. **Performance targets** (FPS, memory, simulation speed)
4. **Modularity requirements** (DLC hooks, modding API points)
5. **Integration tests** with existing systems

**Daily stand‑up format:**
- What I built yesterday (with blueprint reference)
- What I'm building today (with validation plan)
- What's blocking me (technical, resource, knowledge)

**Version control:**
- Feature branches named `mechanics/<pillar>/<feature>`
- Pull requests require blueprint citation + performance metrics
- Merge only after automated tests + manual validation

---

## 6. VALIDATION METRICS & SUCCESS CRITERIA

### 6.1 Biological Realism
- **Growth curves:** Match Norwegian production data (±5% error)
- **Disease outbreaks:** Reproduce historical ISA/PD patterns (±10% probability)
- **Genetic gain:** 10‑15% per generation (industry standard)
- **Feed conversion:** FCR 1.1‑1.3 (Norwegian best practice)

### 6.2 Economic Accuracy
- **Price correlation:** R² > 0.8 vs. Nasdaq Salmon Index (3‑year backtest)
- **Production costs:** Within 10% of Norwegian producer financial reports
- **Market share dynamics:** Reproduce industry consolidation patterns
- **Risk‑return profile:** Match salmon farming investment benchmarks

### 6.3 Gameplay Depth
- **Emergent events:** ≥3 unexpected but logical outcomes per 10‑hour playthrough
- **Decision consequence:** Early choices still affecting game state at 50‑hour mark
- **Replayability:** ≥5 distinct viable strategies (cost‑leader, premium, sustainable, etc.)
- **Learning curve:** New players competent in 2 hours, mastering systems in 20 hours

### 6.4 Technical Performance
- **Frame rate:** 60 FPS with 10,000 fish (GTX 1060 equivalent)
- **Memory:** <4 GB RAM with 100,000 fish entities
- **Save/load:** <10 seconds for 100‑hour game state
- **Modularity:** Third‑party mods can add new mechanics without engine modification

---

## 7. RISK MITIGATION

### 7.1 Technical Risks
- **Over‑engineering:** Start with simplified SDE, add complexity only when needed
- **Performance bottlenecks:** Profile early, optimize algorithmic complexity first
- **Integration complexity:** Define clear APIs between simulation layers
- **Numerical instability:** Use robust ODE solvers (Runge‑Kutta 4/5, adaptive step)

### 7.2 Design Risks
- **Complexity overwhelm:** Progressive disclosure of mechanics, contextual tutorials
- **Balance issues:** Continuous playtesting, automated balance testing
- **Emergence bugs:** Agent‑based testing for unintended consequences
- **Modding conflicts:** Versioned APIs, dependency management, conflict resolution

### 7.3 Development Risks
- **Subagent coordination:** Daily stand‑ups, clear handoff protocols, shared documentation
- **Scope creep:** Feature freeze after blueprint approval, change requests require re‑estimation
- **Quality assurance:** Automated testing coverage >80%, manual testing for emergent behavior
- **Knowledge silos:** Cross‑training, paired programming, shared code ownership

---

## 8. IMMEDIATE NEXT ACTIONS

1. **Distribute blueprint** to all subagents (existing + newly configured)
2. **Reconfigure teams** per Section 5 (mechanics‑focused roles)
3. **Begin Phase 1 implementation** (SDE growth + 3D water physics)
4. **Establish validation pipeline** (automated tests against Norwegian data)
5. **Schedule first blueprint review** (7 days from today)

**Blueprint version control:** This document is living. Major revisions require lead architect approval. Minor clarifications can be made via pull request.

---

*"The difference between a good game and a great simulation is that the latter doesn't just entertain—it teaches you how the world actually works."*  
— Lead Architect, 2026‑03‑12