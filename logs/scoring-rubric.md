# Scoring Rubric — Iterative Development Cycle

## Scoring Scale (0‑10)

**0‑3:** Deficient — Major flaws, unrealistic, inefficient, unoriginal  
**4‑6:** Adequate — Functional but with notable issues, basic implementation  
**7‑8:** Good — Solid implementation, minor issues, some innovative elements  
**9‑10:** Excellent — Production‑ready, highly realistic, efficient, innovative

---

## 1. Realism (0‑10)

**Alignment with Norwegian salmon‑aquaculture data and real‑world dynamics.**

### 9‑10 (Excellent)
- Parameters match published Norwegian industry data (e.g., FCR 1.1‑1.3, mortality rates 10‑20%)
- Disease models reflect actual outbreak patterns (ISA, PD, CMS probabilities and spread)
- Economic figures align with real NOK values (license costs, feed prices, market premiums)
- Biological growth curves match von Bertalanffy/Gompertz with accurate temperature dependence
- All systems validated against reference case studies (e.g., ISA Chile 2007)

### 7‑8 (Good)  
- Generally realistic but minor deviations from industry benchmarks
- Disease probabilities in right ballpark but simplified spread mechanics
- Economic values plausible but not precisely calibrated
- Growth model biologically sound but may lack fine‑tuned environmental factors

### 4‑6 (Adequate)
- Basic realism: fish grow, diseases occur, money flows
- Parameters loosely based on reality but not rigorously validated
- Missing key real‑world constraints or dynamics

### 0‑3 (Deficient)
- Unrealistic or broken mechanics (e.g., fish grow 10kg/day, negative costs)
- Contradicts established aquaculture science
- No reference to actual Norwegian industry

---

## 2. Efficiency (0‑10)

**Computational performance, memory usage, code quality, and scalability.**

### 9‑10 (Excellent)
- O(n) or better algorithms for core simulations (growth, disease spread)
- Memory usage stable even with 100k+ fish entities
- No redundant calculations or duplicated data
- Clean, modular code with appropriate abstractions
- Handles edge cases gracefully (zero‑fish, max‑capacity, etc.)

### 7‑8 (Good)
- Generally efficient but minor optimizations possible
- Memory usage scales linearly with entity count
- Code is readable and reasonably organized
- Some duplication or suboptimal data structures

### 4‑6 (Adequate)
- Works but inefficient for large‑scale simulations
- Quadratic or worse algorithms in critical paths
- Memory leaks or unbounded growth possible
- Messy code but functional

### 0‑3 (Deficient)
- Crashes on moderate loads (>10k fish)
- Severe performance issues (seconds per iteration)
- Memory exhaustion with normal use
- Spaghetti code, global state abuse, no error handling

---

## 3. Innovation (0‑10)

**Novel mechanics, creative systems, and unique gameplay beyond existing sims.**

### 9‑10 (Excellent)
- Introduces genuinely new gameplay mechanics not seen in other tycoon/agriculture sims
- Creative systems that leverage salmon‑aquaculture specifics (e.g., smoltification as critical phase)
- Emergent complexity from simple rules (e.g., genetics + market = unique breeding strategies)
- Surprising depth that rewards player experimentation

### 7‑8 (Good)
- Fresh take on established genre mechanics
- Some unique elements tied to salmon farming theme
- Encourages non‑obvious strategies
- More than just a reskin of existing sim formulas

### 4‑6 (Adequate)
- Standard tycoon mechanics with salmon theme
- Follows well‑worn genre conventions
- Few surprises but competently executed

### 0‑3 (Deficient)
- Cloned mechanics from other games with minimal adaptation
- No original thought or creative elements
- Generic implementation that could be any commodity

---

## Scoring Process

1. **Generate** → Produce code/feature
2. **Test** → Run simulations, validate against reference data
3. **Self‑Critique** → Score on above rubric, identify deficiencies
4. **Refine** → Apply targeted fixes based on low scores
5. **Convergence Check** → Continue if scores <9 or material enhancements possible

**Convergence Threshold:** All three scores ≥9.0  
**Cycle Limit:** 10 per session (expandable across sessions)

---

## Reference Data (Norwegian Salmon Aquaculture)

### Biological
- FCR (Feed Conversion Ratio): 1.1‑1.3 (kg feed per kg fish growth)
- Sea‑phase duration: 12‑18 months
- Typical harvest weight: 4‑6 kg
- Mortality: 10‑20% sea phase, 2‑5% freshwater
- Optimal temperature: 8‑14°C (growth slows outside range)

### Economic
- MAB license: ~150M NOK (170,300 NOK/tonne × 880T)
- Feed cost: 40‑50% of production cost (~15‑20 NOK/kg fish)
- Market price: 60‑90 NOK/kg (fresh, head‑on gutted)
- Processing CAPEX: 150M‑3B NOK depending on scale

### Disease (Annual Probabilities)
- ISA: 0.5% (highly contagious, spreads 5‑15km via water)
- PD: 2‑4% in warmer months (≥12°C water)
- CMS: 1‑3% after 12+ months at sea
- Sea lice: Ubiquitous (requires regular treatment)

### Vaccination Effectiveness
- ISA: 90%
- PD: 80%  
- CMS: 80%
- Winter ulcers: 70%

---

*Rubric used by Lead Orchestrator for consistent scoring across iterative cycles.*