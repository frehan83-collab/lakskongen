# 🌙 LAKSKONGEN — NIGHT SPRINT PLAN
## 2026-03-13 00:00–08:00 CET

---

## ✅ QC PASS COMPLETED (00:00–00:30)

**10+ compile-breaking and runtime bugs fixed:**

| # | File | Bug | Severity |
|---|------|-----|----------|
| 1 | `GrowthModel.cs` | Case mismatch: `optimalTemperature` → `OptimalTemperature` | **COMPILE** |
| 2 | `DiseaseSystem.cs` | `_liceModel` was private, accessed from StochasticDiseaseEngine | **COMPILE** |
| 3 | `StochasticDiseaseEngine.cs` | Missing `using System.Linq` — uses `.Where()`, `.Count()` | **COMPILE** |
| 4 | `FishPopulation.cs` | Missing `ZoneId` property — used by disease engine | **COMPILE** |
| 5 | `FishPopulation.cs` | Missing `HasRecentTreatment` property | **COMPILE** |
| 6 | `FishPopulation.cs` | Missing `TreatmentIntensity` property | **COMPILE** |
| 7 | `FishPopulation.cs` | Missing `GetIndividuals()` method | **COMPILE** |
| 8 | `FishPopulation.cs` | Missing `ApplyGrowthPenalty()` method | **COMPILE** |
| 9 | `StochasticDiseaseEngine.cs` | Dictionary modification during iteration → `InvalidOperationException` | **RUNTIME** |
| 10 | `StochasticDiseaseEngine.cs` | `KeyNotFoundException` in zone infection pressure lookups | **RUNTIME** |
| 11 | `FishPopulation.cs` | Wrong namespace for `FishIndividual` reference | **COMPILE** |

**Git commit:** `8f9e68d` — "QC Pass: Fix 10+ compile-breaking and runtime bugs"

---

## 🎯 NIGHT SPRINT PRIORITIES (00:30–08:00)

### Phase 1: Integration & Wiring (00:30–02:00)
1. **Connect WaterChemistry → GrowthModel**
   - Add `waterChemistryPenalty` parameter to SDE growth methods
   - Zone.AdvanceDays should pass water penalty to fish growth
   - FCR integration: FeedSystem feeds WaterChemistry waste inputs

2. **Connect DiseaseEngine → FishPopulation**
   - IntegratedDiseaseSystem wired into SimulationEngine.Advance()
   - Disease mortality flows through existing FishPopulation cohort system
   - Treatment events update HasRecentTreatment/TreatmentIntensity

3. **Connect WeatherSystem → All systems**
   - Temperature flows: Weather → Zone → WaterChemistry → GrowthModel
   - Temperature flows: Weather → DiseaseSystem (treatment efficacy)
   - Seasonal patterns affect all biological rates

### Phase 2: Missing Core Systems (02:00–04:00)
4. **Feed Conversion Ratio (FCR) engine**
   - Currently FeedSystem uses fixed 2% of bodyweight — needs dynamic FCR
   - FCR 1.1–1.3 (industry range), affected by: temperature, feed quality, fish size, stage
   - Connect to GrowthModel: growth = f(feed, FCR, temperature, genetics)

5. **Economic integration**
   - MarketSimulation needs real salmon price dynamics (Nasdaq Salmon Index correlation)
   - Seasonal pricing patterns: Q1 low, Q2-Q3 high, Q4 Christmas peak
   - Contract system needs to reference actual zone/population data

6. **Pen/Cage system**
   - SeaSite needs connection to Zone and FishPopulation
   - Stocking density → water chemistry → disease susceptibility chain
   - MAB (Maximum Allowed Biomass) enforcement per license

### Phase 3: Gameplay Systems (04:00–06:00)
7. **SimulationEngine orchestration**
   - Wire all subsystems into the central Advance() loop
   - Correct update order: Weather → Environment → Biology → Health → Market → Finance
   - Event system triggers (disease outbreaks, weather events, market crashes)

8. **Save/Load verification**
   - GameState needs all new properties serializable
   - Verify JSON round-trip for Zone (now has WaterChemistry), FishPopulation (new fields)

### Phase 4: Documentation & Planning (06:00–08:00)
9. **Update all logs and reports**
   - Update AdvancedMechanicsCycle1Report with QC findings
   - Create Cycle 2 plan with specific tasks and assignments
   - Update MEMORY.md with tonight's progress

10. **Morning briefing preparation**
    - Summary of all changes made overnight
    - List of remaining issues and priorities
    - Recommended next steps for Fredrik

---

## 📊 CODEBASE STATUS (Post-QC)

### **Systems Inventory (60 .cs files)**

| Category | Files | Status | Integration |
|----------|-------|--------|-------------|
| **Biology** | 4 | ✅ Fixed | GrowthModel needs FCR/water input |
| **Core** | 5 | ✅ Clean | SimulationEngine needs full wiring |
| **Data** | 2 | ✅ Clean | Comprehensive enum coverage |
| **Environment** | 4 | ✅ New | WaterChemistry created, Zone upgraded |
| **Events** | 5 | ⚠️ Not reviewed | Need QC pass |
| **Expansion** | 2 | ⚠️ Not reviewed | Need QC pass |
| **Farm** | 4 | ✅ Clean | FeedSystem needs FCR upgrade |
| **Finance** | 8 | ✅ Clean | Sustainability integration done |
| **Health** | 6 | ✅ Fixed | LOD + Integrated system created |
| **Logistics** | 6 | ⚠️ Not reviewed | Need QC pass |
| **Market** | 4 | ✅ Clean | Needs real price dynamics |
| **Markets** | 1 | ⚠️ Not reviewed | Duplicate namespace? |
| **Politics** | 1 | ⚠️ Not reviewed | Need QC pass |
| **Processing** | 2 | ⚠️ Not reviewed | Need QC pass |
| **Sustainability** | 1 | ✅ Clean | Well-implemented |
| **Tests** | 3 | ⚠️ Not reviewed | Need execution |

---

## 🔧 CRITICAL PATH

```
Weather → Zone.Temperature → WaterChemistry.Update() → GrowthPenalty
                                                      → DiseaseSusceptibility
Feed → FeedSystem.Feed() → WaterChemistry.Waste() → Ammonia/O2 cycles
                         → GrowthModel.SDE(FCR) → FishPopulation.Weight
Disease → IntegratedDiseaseSystem.Update() → FishPopulation.Mortality
                                           → ResistanceEvolution
Market → MarketSimulation.Price() → Finance.Revenue()
```

**The simulator doesn't run as a coherent loop yet.** SimulationEngine.Advance() only calls TimeManager and SustainabilityManager. All other systems are disconnected. This is the #1 priority.
