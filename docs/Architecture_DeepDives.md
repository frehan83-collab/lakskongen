# Architecture Deep Dives

**Companion to:** [Architecture.md](Architecture.md)  
**Version:** 0.4.0

---

## Table of Contents

- [Cohort-Based Population Model](#cohort-based-population-model)
- [Genetics System](#genetics-system)
- [Save/Load Format](#saveload-format)
- [Performance Optimization](#performance-optimization)
- [Localization Workflow](#localization-workflow)

---

## Cohort-Based Population Model

### Why Cohorts?

Individual fish simulation is too expensive at scale (100k+ fish). Instead, we group fish into **cohorts** — statistically similar groups that share genetics, age, and conditions.

```
Individual Model:     100,000 fish × 10 operations = 1,000,000 ops/tick ❌
Cohort Model:         50 cohorts × 10 operations = 500 ops/tick ✅
```

### Cohort Structure

Each cohort tracks:

```csharp
public class Cohort
{
    public int Count;              // Living fish in this group
    public double MeanWeightKg;    // Average weight
    public double WeightStdDev;    // Weight variance within cohort
    public Genome Genetics;        // Shared genetic profile (8 traits)
    public LifeStage Stage;        // Egg/Alevin/Fry/Parr/Smolt/PostSmolt/Adult
    public int AgeDays;            // Days since hatch
    public bool IsVaccinated;      // Vaccination status
}
```

### Population Operations

| Operation | Implementation | Complexity |
|-----------|---------------|-----------|
| **Growth** | `MeanWeightKg += growth_rate × days` (von Bertalanffy) | O(cohorts) |
| **Mortality** | `Count -= Count × mortality_rate × days` | O(cohorts) |
| **Harvest** | Remove fish from largest cohorts first | O(cohorts) |
| **Stocking** | Add new cohort with hatchery genetics | O(1) |
| **Disease** | Modify mortality rate (injected into MortalityModel) | O(cohorts) |

### Cohort Lifecycle

```
Hatchery.OnNewCohort
    → FishPopulation.AddCohort(cohort)
    → Each tick:
        GrowthModel.ApplyGrowth(cohort, days, temp)
        MortalityModel.ApplyMortality(cohort, days)
        → cohort.Count decreases (deaths)
        → cohort.MeanWeightKg increases (growth)
    → When MeanWeightKg ≥ target:
        HarvestScheduler triggers harvest
        cohort.Count → 0 (or partial harvest)
```

### Splitting and Merging

- **Splitting**: When weight variance (StdDev) exceeds threshold, split into two cohorts (fast/slow growers)
- **Merging**: When two cohorts have similar weight/genetics, merge to reduce iteration count
- **Size grading**: Player action that splits population by weight class

---

## Genetics System

### Overview

8 heritable traits modeled using quantitative genetics (BLUP-inspired). Based on real Norwegian breeding programs (AquaGen, SalmoBreed, Mowi Genetics).

### Genome Data Structure

```csharp
public class Genome
{
    // Breeding Values (BV) — 0 = population average, ±1σ = one std dev
    public Dictionary<GeneticTrait, double> TraitValues;
    public int Generation;          // Breeding generation
    public bool HasMutation;        // Rare mutation flag
    public List<GeneticTrait> MutatedTraits;  // Which traits mutated
}
```

### Trait Heritabilities

| Trait | h² | Meaning |
|-------|-----|---------|
| Growth Rate | 0.30 | 30% of phenotypic variation is genetic |
| Disease Resistance | 0.15 | Low — mostly environmental |
| Sea Lice Resistance | 0.20 | Moderate |
| Fillet Quality | 0.25 | Moderate |
| Feed Conversion | 0.20 | Moderate |
| Stress Tolerance | 0.10 | Very low — mostly environment |
| Late Maturation | 0.35 | High — strongly genetic |
| Cold Tolerance | 0.15 | Low-moderate |

### Breeding Algorithm

```
Breed(parent1, parent2) → offspring:

1. Mid-parent value:
   midParent = (parent1.BV + parent2.BV) / 2

2. Mendelian segregation:
   segregation = Normal(0, √(0.5 × h²))

3. Environmental variance:
   envEffect = Normal(0, √(1 - h²) × 0.3)

4. Genetic correlations:
   correlationEffect = Σ(correlated_trait_effects) × 0.1

5. Offspring BV = midParent + segregation + envEffect + correlationEffect

6. Rare mutation (0.1% per trait):
   If random < 0.001: BV += Normal(0, 0.5)
```

### Genetic Correlations (Trade-offs)

Key correlations that create strategic depth:

```
Growth ←(+0.6)→ Feed Efficiency    Fast growers also convert feed well
Growth ←(-0.2)→ Disease Resist.    Fast growers have weaker immunity
Growth ←(-0.1)→ Fillet Quality     Slight quality penalty for speed
Disease ←(+0.3)→ Lice Resist.      Immune system co-benefits
Stress ←(+0.2)→ Disease Resist.    Calm fish are healthier
Late Mat ←(-0.15)→ Growth           Delayed maturity slows growth
```

### Breeding Value Estimation

Simplified BLUP (Best Linear Unbiased Prediction):

```
accuracy = √(h² × n / (1 + (n-1) × h²))

Where n = number of phenotypic observations

Example: Growth Rate (h² = 0.30), 1 observation
accuracy = √(0.30 × 1 / (1 + 0 × 0.30)) = 0.548 (55% accuracy)

With 10 observations:
accuracy = √(0.30 × 10 / (1 + 9 × 0.30)) = 0.894 (89% accuracy)
```

### Expected Genetic Gain

```
ΔG = i × h² × σ_p

Where:
  i   = selection intensity (top 10% → i ≈ 1.76)
  h²  = heritability
  σ_p = phenotypic standard deviation

Example: Selecting top 10% for growth rate
ΔG = 1.76 × 0.30 × 1.0 = 0.528 σ per generation
```

### Inbreeding

When breeding related individuals:
- Small population → increased inbreeding coefficient
- Inbreeding depression reduces fitness traits
- Player must balance selection intensity vs. genetic diversity
- Future: Pedigree tracking and inbreeding coefficient calculation

---

## Save/Load Format

### JSON Schema

```json
{
  // ─── Core State ─────────────────────────
  "Day": 905,
  "Balance": 15000000.0,

  // ─── Farm Infrastructure ────────────────
  "Zones": [
    {
      "Id": "zone_3",
      "Name": "Nordland",
      "TrafficLightColor": "Green",
      "MABLimit": 780000
    }
  ],
  "SeaSites": [
    {
      "Id": "site_001",
      "ZoneId": "zone_3",
      "MaxBiomassKg": 200000,
      "CurrentBiomassKg": 185000
    }
  ],

  // ─── Biology ────────────────────────────
  "Populations": [
    {
      "SiteId": "site_001",
      "Cohorts": [
        {
          "Count": 45000,
          "MeanWeightKg": 4.2,
          "WeightStdDev": 0.8,
          "AgeDays": 540,
          "Stage": "Adult",
          "IsVaccinated": true,
          "Genetics": {
            "Generation": 3,
            "TraitValues": {
              "GrowthRate": 0.45,
              "DiseaseResistance": 0.12,
              "SeaLiceResistance": -0.08,
              "FilletQuality": 0.33,
              "FeedConversion": 0.28,
              "StressTolerance": 0.05,
              "LateMaturation": 0.18,
              "ColdTolerance": 0.22
            },
            "HasMutation": false,
            "MutatedTraits": []
          }
        }
      ]
    }
  ],
  "Broodstock": [ /* Genome objects for breeding program */ ],

  // ─── Analytics & Events ─────────────────
  "DailySnapshots": [ /* One per simulated day */ ],
  "EventLog": [
    {
      "Day": 456,
      "Name": "Storm Warning",
      "Category": "Weather",
      "Description": "Severe storm damages net pens",
      "BalanceChange": -250000
    }
  ],
  "ProcessingSnapshots": [ /* Processing plant P&L */ ],

  // ─── Research & Tech ────────────────────
  "UnlockedResearchIds": ["basic_feed", "vaccination", "led_lighting"],
  "ActiveResearchId": "laser_delousing",
  "ResearchProgress": 12,

  // ─── Market & Expansion ─────────────────
  "ExportMarkets": ["EU", "USA", "Japan"],
  "ContractActive": true,
  "AcquisitionCount": 2,
  "OwnsFeedMill": false,
  "OwnsHatchery": true,
  "OwnsProcessingPlant": false,

  // ─── Achievements ───────────────────────
  "UnlockedAchievementIds": ["first_harvest", "millionaire", "zero_mortality_month"],
  "BestFCR": 1.12,

  // ─── Sustainability ─────────────────────
  "SustainabilityScore": 75,
  "SustainabilityCertification": "ASC",
  "FeedSustainabilityRating": 0.82,

  // ─── Scenarios ──────────────────────────
  "ActiveScenarioId": null,
  "ScenarioIsWon": false,
  "ScenarioIsLost": false,
  "ScenarioLossReason": null,
  "ScenarioStartDay": 0,

  // ─── Tutorial ───────────────────────────
  "TutorialCurrentStepIndex": 5,
  "CompletedTutorialStepIds": ["welcome", "stock_fish", "first_feed", "check_health", "first_harvest"]
}
```

### Serialization Details

| Feature | Implementation |
|---------|---------------|
| Format | JSON via `System.Text.Json` |
| Enums | Serialized as strings (`JsonStringEnumConverter`) |
| Indentation | Pretty-printed for human readability |
| Compression | gzip for saves > 1 MB (future) |
| Backward compat | Missing properties use `GameState` defaults |
| Corruption recovery | Checksum validation + 3-generation backup rotation |

### Migration Strategy

When the schema changes between versions:
1. New properties added with default values → automatic backward compat
2. Renamed properties → deserialize with `[JsonPropertyName]` attribute
3. Removed properties → `JsonIgnoreCondition.Never` handles gracefully
4. Type changes → manual migration in `Deserialize()` method

---

## Performance Optimization

### Current Profile (v0.4.0)

| System | Time @ 10k fish | Time @ 100k fish | Target |
|--------|-----------------|-------------------|--------|
| Biology (Growth) | ~3ms | ~30ms | < 5ms |
| Disease (SIR) | ~1ms | ~10ms | < 3ms |
| Feed Calculation | ~0.8ms | ~8ms | < 2ms |
| Market Update | ~0.2ms | ~0.2ms | OK |
| Total Tick | ~5ms | ~50ms | < 16.67ms |

**Target:** 60 FPS @ 100k fish (tick budget = 16.67ms)

### Optimization Strategies

#### 1. In-Place Cohort Updates (Implemented)
```csharp
// Before (creates new objects):
cohort = GrowthModel.Apply(cohort, days, temp); // allocation

// After (mutates in place):
GrowthModel.ApplyInPlace(ref cohort, days, temp); // no allocation
```
**Expected gain:** 40-60% reduction in biology tick time.

#### 2. Adaptive LOD for Disease (Implemented)
Three detail tiers that switch dynamically:
- **Low** (< 0.1% prevalence): Statistical aggregate — ~0.1ms
- **Medium** (0.1-1% prevalence): Zone-level SIR — ~2ms
- **High** (> 1%, ≤ 1000 fish): Agent-based — ~10ms

#### 3. SIMD Vectorization (Future)
```csharp
// AVX2 vectorized growth calculation (4 doubles per instruction)
Vector256<double> weights = Avx2.LoadVector256(cohortWeights);
Vector256<double> growth = Avx2.Multiply(weights, growthRates);
```
**Expected gain:** 4x throughput for growth calculations.

#### 4. ECS Migration (Future)
For 500k+ fish, migrate to Arch ECS:
```csharp
// Instead of cohort objects:
world.Query<Weight, Genetics, Health>(
    (ref Weight w, ref Genetics g, ref Health h) =>
    {
        w.Value += GrowthRate(g, temperature);
    });
```
**Expected gain:** 10-20x with cache-friendly memory layout.

#### 5. Parallelization (Future)
Independent systems can run in parallel:
```csharp
Parallel.Invoke(
    () => UpdateDisease(zone1),
    () => UpdateDisease(zone2),
    () => UpdateMarket(),
    () => UpdateWeather()
);
```

### Memory Optimization

| Object | Current | Optimized |
|--------|---------|-----------|
| Cohort | ~200 bytes (class) | ~80 bytes (struct) |
| Genome | ~300 bytes (Dictionary) | ~64 bytes (fixed array) |
| Population | ~2KB | ~500 bytes |

---

## Localization Workflow

### Overview

6 supported languages: Norwegian (default), English, German, French, Spanish, Japanese.

### Translation Pipeline

```
1. Developers add tr("key") calls in GDScript
2. extract_strings.py scans all .gd files
3. New keys added to translations/strings.csv
4. Translators fill in columns for each language
5. Godot auto-loads CSV at runtime
6. LocalizationManager.gd handles language switching
```

### File Format

`translations/strings.csv`:
```csv
keys,no,en,de,fr,es,ja
ui.main_menu.title,"LAKSKONGEN","SALMON KING","LACHSKÖNIG","ROI DU SAUMON","REY DEL SALMÓN","サーモンキング"
ui.main_menu.new_game,"Nytt Spill","New Game","Neues Spiel","Nouveau Jeu","Nuevo Juego","新しいゲーム"
ui.dashboard.day,"Dag","Day","Tag","Jour","Día","日"
```

### Key Naming Convention

```
ui.<screen>.<element>         → UI text
ui.tooltip.<element>          → Tooltips
ui.common.<element>           → Shared labels (OK, Cancel)
game.event.<event_id>         → Event descriptions
game.achievement.<id>         → Achievement text
game.tutorial.<step>          → Tutorial content
```

### Adding a New Language

1. Add column to `translations/strings.csv`
2. Register in `LocalizationManager.gd`:
   ```gdscript
   var supported_languages = ["no", "en", "de", "fr", "es", "ja", "NEW"]
   ```
3. Add language option in Settings UI
4. Test all screens for text overflow/truncation

### Runtime Language Switching

```gdscript
# Via LocalizationManager autoload:
LocalizationManager.set_language("en")

# All tr() calls automatically update
# Signals emitted for UI refresh
```

### For Contributors

See the full guide: [Localization Guide](../godot/LOCALIZATION_GUIDE.md)

---

## Cross-Reference

| Document | Content |
|----------|---------|
| [Architecture.md](Architecture.md) | Core architecture overview |
| [API Reference](API_Reference.md) | Public APIs |
| [Diagrams](diagrams/) | Visual system diagrams |
| [Modding API](Modding_API.md) | Mod development |
| [Tutorials](tutorials/) | Getting started guides |

---

**Last Updated:** 2026-03-15  
**Maintained By:** Lakskongen Development Team
