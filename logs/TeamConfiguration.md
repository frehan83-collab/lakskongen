# 🐟 LAKSKONGEN — Subagent Team Configuration (Advanced Mechanics Focus)

**Effective:** 2026‑03‑12 21:55 CET  
**Lead Architect:** AI Project Manager & Master Architect  
**Reference:** Advanced Mechanics Blueprint (salmon‑sim/AdvancedMechanicsBlueprint.md)

---

## TEAM ROLES & RESPONSIBILITIES

### **Mechanics Architect**
**Primary Model:** Claude Sonnet 4.5 (when available) → GPT‑4o (fallback)  
**Secondary/Local:** GLM‑4.7‑flash (complex math), Qwen3:8b (simpler tasks)

**Owns:**
- All simulation formulas, physics engines, emergent behavior logic
- Stochastic differential equations (growth, metabolism, disease progression)
- Partial differential equations (water column physics, advection‑diffusion)
- Genetic algorithms, optimization routines, numerical stability

**Deliverables:**
- SDE‑based growth model with individual fish tracking
- 3D water physics (temperature, oxygen, current simplified)
- Agent‑based disease transmission kernels
- Monte Carlo price forecasting engine

**Validation Criteria:**
- Mathematical correctness (peer‑review style verification)
- Numerical stability (no NaN/inf, bounded errors)
- Computational efficiency (O(n) or O(n log n) algorithms)
- Integration with existing C#/GDScript bridges

**Tools:**
- Julia/Python for prototyping (rapid iteration)
- C# for production (Unity/Godot integration)
- Latex for documentation (equation derivation)
- Unit tests with edge‑case coverage

---

### **Biology & Environment Specialist**
**Primary Model:** Claude Sonnet 4.5 → GPT‑4o  
**Secondary/Local:** GLM‑4.7‑flash (research comprehension)

**Owns:**
- Research‑grade salmon aquaculture models
- Metabolic budgets, disease progression, water chemistry cycles
- Validation against published Norwegian data

**Deliverables:**
- Energy budget model (intake − maintenance − activity − waste)
- Disease progression curves (ISA, PD, CMS with realistic timelines)
- Water chemistry (nitrogen, phosphorus, carbonate cycles)
- Environmental impact modeling (benthic loading, eutrophication)

**Validation Criteria:**
- Match Institute of Marine Research (IMR) growth data (±5%)
- Reproduce historical disease outbreak patterns (±10% probability)
- Align with Nofima feed conversion ratios (FCR 1.1‑1.3)
- Calibrate against real Norwegian fjord water quality data

**Tools:**
- Scientific literature review (PubMed, Google Scholar)
- Data calibration (parameter estimation from time‑series)
- Statistical validation (R², RMSE, confidence intervals)
- Sensitivity analysis (which parameters matter most)

---

### **Economic Strategist**
**Primary Model:** Claude Sonnet 4.5 → GPT‑4o  
**Secondary/Local:** Qwen3:8b (data processing)

**Owns:**
- Real‑time market engines with predictive analytics
- Supply‑demand curves, price formation, futures trading
- Multi‑tier supply chain optimization

**Deliverables:**
- Global commodity market engine (Norway, Chile, Scotland, Canada)
- Futures & options pricing models (Black‑Scholes adaptation)
- Contract optimization (cost‑plus, fixed‑price, revenue‑sharing)
- Logistics route planning (trucks, wellboats, container ships)

**Validation Criteria:**
- Historical price correlation R² > 0.8 vs. Nasdaq Salmon Index
- Production costs within 10% of Norwegian financial reports
- Market share dynamics reproduce industry consolidation
- Risk‑return profile matches salmon farming investment benchmarks

**Tools:**
- Econometric modeling (ARIMA, GARCH for volatility)
- Monte Carlo simulation (10,000+ price path scenarios)
- Optimization algorithms (linear programming for logistics)
- Backtesting framework (3‑year historical validation)

---

### **Emergence & Balance Engineer**
**Primary Model:** Claude Sonnet 4.5 → GPT‑4o  
**Secondary/Local:** GLM‑4.7‑flash (testing automation)

**Owns:**
- Testing for unintended but desirable interactions
- Ensuring mechanical depth without overwhelming complexity
- Difficulty curve tuning, exploit prevention

**Deliverables:**
- Emergent complexity metrics (Shannon entropy of game states)
- Difficulty curves (challenging but fair progression)
- Exploit prevention (patch dominant strategies)
- Scenario generators (stress‑test edge cases)

**Validation Criteria:**
- ≥3 unexpected but logical outcomes per 10‑hour playthrough
- Early choices still affect game state at 50‑hour mark
- ≥5 distinct viable strategies (cost‑leader, premium, sustainable, etc.)
- Learning curve: competent in 2 hours, mastering in 20 hours

**Tools:**
- Automated testing frameworks (unit + integration + system)
- Agent‑based testing (AI players exploring strategy space)
- Balance spreadsheets (parameter tuning with visual feedback)
- Playtesting coordination (human feedback integration)

---

### **Quality & Performance Guardian**
**Primary Model:** Claude Sonnet 4.5 → GPT‑4o  
**Secondary/Local:** Qwen3:8b (profiling automation)

**Owns:**
- 60+ FPS simulation, memory efficiency, load‑time optimization
- Stress‑test reliability, long‑term stability, mod compatibility

**Deliverables:**
- Performance benchmarks (FPS vs. entity count)
- Memory profiles (leak detection, optimization)
- Load‑time optimization (asset bundling, lazy loading)
- Compatibility testing (hardware tiers, driver versions)

**Validation Criteria:**
- 60 FPS with 10,000 fish (GTX 1060 equivalent)
- <4 GB RAM with 100,000 fish entities
- Save/load <10 seconds for 100‑hour game state
- Zero crashes in 1000‑hour stress test

**Tools:**
- Profilers (dotTrace, PerfView, Unity Profiler)
- Memory analyzers (dotMemory, heap dumps)
- GPU optimization (compute shaders, batching)
- Automated stress‑test rigs (24/7 testing)

---

## INTER‑AGENT COMMUNICATION PROTOCOL

### Task Handoff Requirements
Every task handoff must include:

1. **Blueprint reference** — Which section of Advanced Mechanics Blueprint is being implemented
2. **Validation criteria** — Specific metrics for success/failure
3. **Performance targets** — FPS, memory, simulation speed requirements
4. **Modularity hooks** — DLC extension points, modding API considerations
5. **Integration tests** — How this integrates with existing systems

### Daily Stand‑Up Format
**Each agent reports:**
- What I built yesterday (with blueprint section citation)
- What I'm building today (with validation plan)
- What's blocking me (technical, resource, knowledge gaps)

**Stand‑up time:** 09:00 CET daily (virtual via OpenClaw session‑history review)

### Version Control Discipline
- **Feature branches:** `mechanics/<pillar>/<feature>` (e.g., `mechanics/biology/sde‑growth`)
- **Pull requests:** Require blueprint citation + performance metrics
- **Merge criteria:** Automated tests pass + manual validation + performance benchmarks
- **Code review:** At least one other mechanics‑focused agent must approve

### Fallback Chain (Per‑Agent)
1. **Primary:** Claude Sonnet 4.5 (when rate limits allow)
2. **Secondary:** GPT‑4o (tightly scoped tasks)
3. **Local:** GLM‑4.7‑flash (complex math), Qwen3:8b (data processing)
4. **Manual:** Lead Architect intervention if automation fails

### File Path Standardization
**Absolute paths only:** `C:\Users\freha\.openclaw\workspace\salmon‑sim\...`
- No relative paths (`godot/scripts/...`)
- No ambiguous references (`Fish.gd` → full path)
- Working directory explicitly set in agent spawn

---

## IMMEDIATE NEXT ACTIONS (Phase 1 — Month 1‑2)

### Week 1‑2: Foundation
**Mechanics Architect:**
- Implement stochastic differential equation growth model (individual fish)
- Create 3D water column physics (simplified temperature, oxygen, current)

**Biology & Environment Specialist:**
- Calibrate growth model against Norwegian production data
- Implement water chemistry (nitrogen cycle simplified)

**Economic Strategist:**
- Build Monte Carlo price forecasting (basic supply‑demand)
- Create historical price correlation validation

**Emergence & Balance Engineer:**
- Design automated testing framework for new systems
- Create difficulty curve for initial gameplay

**Quality & Performance Guardian:**
- Establish performance benchmarking suite
- Profile existing codebase for optimization targets

### Success Metrics (Week 2 Review)
- SDE growth model running at 60 FPS with 1,000 fish
- Water physics affecting growth rates (observable effect)
- Price forecasting R² > 0.7 vs. historical data
- Automated tests covering 80% of new mechanics
- Memory usage <2 GB with 10,000 fish

---

## BLUEPRINT COMPLIANCE ENFORCEMENT

**Lead Architect will:**
- Review all agent outputs against blueprint criteria
- Reject deliverables not meeting validation metrics
- Reassign tasks if agent consistently misses targets
- Adjust team composition based on performance

**Agents must:**
- Reference blueprint in every communication
- Report deviations from blueprint immediately
- Suggest blueprint improvements when inconsistencies found
- Maintain version control of blueprint (fork + PR for changes)

---

## ESCALATION PATHS

**Technical Blockers →** Lead Architect + Team consultation  
**Design Conflicts →** Blueprint review + majority vote  
**Performance Issues →** Quality Guardian + Mechanics Architect collaboration  
**Rate Limits/Failures →** Fallback chain + manual implementation

**Critical path items:** If automation fails >2 times, manual implementation is authorized.

---

*Team configuration is living document. Revisions require Lead Architect approval.*