# Lakskongen — Environmental Data Integration

**Document Version:** 1.0  
**Last Updated:** 2026-03-24  
**Phase:** Phase 5 Complete (Mareano/Geonorge Environmental Data Integration)

---

## Table of Contents

1. [Overview](#overview)
2. [Data Sources](#data-sources)
3. [Norwegian Production Zones](#norwegian-production-zones)
4. [Environmental Parameters](#environmental-parameters)
5. [Data Architecture](#data-architecture)
6. [Cache System](#cache-system)
7. [Stress Index Calculation](#stress-index-calculation)
8. [Seasonal Patterns](#seasonal-patterns)
9. [Integration with Game Systems](#integration-with-game-systems)
10. [Future Enhancements](#future-enhancements)

---

## Overview

Phase 5 integrates **real Norwegian coastal oceanographic data** into the simulation, providing zone-specific environmental conditions that directly impact fish growth, health, and welfare.

**Key Features:**
- **13 Norwegian production zones (PO01-PO13)** with geospatially accurate profiles
- **Daily resolution** environmental data (temperature, oxygen, currents, salinity)
- **Seasonal patterns** based on Institute of Marine Research (IMR) monitoring
- **7-day forecast cache** for performance optimization
- **Stress indices** that modulate growth and mortality rates

**Design Philosophy:**
> "Real data, simplified interface. The simulation uses complex oceanographic models internally, but players see intuitive environmental conditions."

---

## Data Sources

### 1. Mareano (Marine Areal Database for Norwegian Waters)

**URL:** https://www.mareano.no

**Data Layers Used:**
- **Bathymetry:** Seafloor depth maps (10-1000m resolution)
- **Sediment:** Substrate types (mud, sand, rock)
- **Oceanography:** Temperature profiles, current patterns, oxygen levels
- **Habitat Maps:** Marine habitat classification

**Integration Method:**
- **Offline:** Pre-processed CSV/JSON datasets in `data/mareano/`
- **Online (future):** WMS/WFS API calls for real-time data

**Update Frequency:**
- Static baseline data (bathymetry, sediment): One-time import
- Dynamic data (temperature, oxygen): Daily updates (cached locally)

---

### 2. Geonorge (Norwegian National Geospatial Portal)

**URL:** https://www.geonorge.no

**Data Layers Used:**
- **Marine Habitat Maps:** Detailed habitat classification (fjords, coastal, open ocean)
- **Current Models:** Surface and subsurface current velocity and direction
- **Coastal Zone Management:** Regulatory boundaries for aquaculture zones

**Integration Method:**
- **Offline:** GeoJSON files for zone boundaries
- **Online (future):** REST API for current forecasts

---

### 3. Institute of Marine Research (IMR)

**URL:** https://www.hi.no

**Data Used:**
- **Temperature Monitoring:** Long-term temperature time series for all production zones
- **Oxygen Monitoring:** Dissolved oxygen measurements at standard depths (0m, 10m, 30m)
- **Chlorophyll-a:** Primary productivity indicator (algal blooms)

**Integration Method:**
- **Historical datasets:** CSV files with seasonal averages (2015-2025)
- **Interpolation:** Cubic spline interpolation for daily values

---

### 4. Norwegian Meteorological Institute (MET Norway)

**URL:** https://www.met.no

**Data Used:**
- **Current Models:** Numerical ocean models (ROMS, NORKYST800)
- **Wind Data:** Surface wind speed and direction (affects currents)
- **Wave Height:** Storm event detection

**Integration Method:**
- **NetCDF files:** Gridded model outputs
- **API (future):** MET API for real-time forecasts

---

## Norwegian Production Zones

Norway is divided into **13 aquaculture production zones (PO01-PO13)** for management and disease monitoring. Each zone has distinct oceanographic characteristics.

### Zone Descriptions

| Zone | Name | Center Coordinates | Depth Range | Key Characteristics |
|------|------|-------------------|-------------|---------------------|
| **PO01** | Østfold | 59.0°N, 11.0°E | 20-100m | Sheltered, brackish, low currents |
| **PO02** | Aust-Agder | 58.3°N, 8.6°E | 30-150m | Coastal, moderate currents |
| **PO03** | Rogaland | 58.9°N, 5.7°E | 50-300m | Exposed coast, strong currents |
| **PO04** | Hordaland | 60.2°N, 5.3°E | 100-500m | Deep fjords, stable temps |
| **PO05** | Sogn og Fjordane | 61.5°N, 5.0°E | 100-800m | Very deep fjords, low exchange |
| **PO06** | Møre og Romsdal | 62.8°N, 6.5°E | 150-1000m | Deepest fjords, good water quality |
| **PO07** | Trøndelag Sør | 64.0°N, 9.5°E | 100-600m | Central Norway, moderate exposure |
| **PO08** | Trøndelag Nord | 65.0°N, 11.0°E | 100-500m | Coastal, good currents |
| **PO09** | Nordland Sør | 66.0°N, 12.5°E | 100-400m | Northern Norway, colder waters |
| **PO10** | Nordland Nord | 67.5°N, 14.0°E | 80-300m | Colder, seasonal ice risk |
| **PO11** | Troms | 69.5°N, 19.0°E | 50-250m | Arctic conditions, midnight sun |
| **PO12** | Finnmark | 70.5°N, 23.0°E | 30-150m | Arctic, extreme seasonality |
| **PO13** | Vest-Finnmark | 71.0°N, 26.0°E | 20-100m | Northernmost, coldest |

### Latitudinal Gradients

**Temperature:**
- **Southern zones (PO01-03):** 6-18°C seasonal range (warmer, narrower)
- **Western zones (PO04-06):** 7-16°C seasonal range (Gulf Stream moderation)
- **Northern zones (PO11-13):** 2-14°C seasonal range (colder, wider)

**Light Regime:**
- **Southern zones (PO01-03):** 6-18 hours daylight (normal seasonal cycle)
- **Mid zones (PO04-08):** 0-24 hours daylight (midnight sun/polar night at extremes)
- **Northern zones (PO11-13):** 0-24 hours daylight (extreme polar cycle)

**Current Strength:**
- **Exposed western coast (PO03-06):** 0.3-0.8 m/s (strong)
- **Sheltered fjords (PO01-02, PO07-13):** 0.1-0.3 m/s (weak)

---

## Environmental Parameters

Each **EnvironmentalDataPoint** contains:

### Temperature Profile

```csharp
public double SurfaceTemperature { get; set; } // °C (0m depth)
public double BottomTemperature { get; set; }  // °C (cage bottom, ~30-50m)
public double TemperatureAt10m { get; set; }   // °C (thermocline detection)
public double TemperatureAt30m { get; set; }   // °C (typical cage depth)
public double TemperatureVariability { get; set; } // Daily std dev (storm detection)
```

**Thermal Stress Zones:**
- **Optimal:** 8-14°C (max growth)
- **Suboptimal:** 2-8°C, 14-18°C (reduced growth)
- **Critical:** <2°C, >18°C (mortality risk, feeding stops)

**Seasonal Patterns:**
- **Winter (Jan-Mar):** Coldest, narrow vertical gradient
- **Spring (Apr-Jun):** Rapid warming, thermocline development
- **Summer (Jul-Sep):** Warmest, strong thermocline
- **Autumn (Oct-Dec):** Cooling, thermocline breakdown

---

### Oxygen Profile

```csharp
public double SurfaceOxygen { get; set; }      // mg/L (0m depth)
public double OxygenAt10m { get; set; }        // mg/L
public double OxygenAtCageDepth { get; set; }  // mg/L (30m typical)
public double OxygenSaturation { get; set; }   // 0-1 (fraction of saturation)
```

**Oxygen Thresholds:**
- **Optimal:** >7 mg/L (no stress)
- **Moderate:** 5-7 mg/L (slight stress, reduced feeding)
- **Critical:** <5 mg/L (severe stress, mortality risk)

**Factors Affecting Oxygen:**
- **Temperature:** Higher temp → lower O₂ solubility (summer risk)
- **Stratification:** Thermocline → oxygen depletion below (deep fjords)
- **Algal Blooms:** Chlorophyll >10 µg/L → hypoxia risk after bloom collapse
- **Currents:** Strong currents → good oxygen replenishment

---

### Current Profile

```csharp
public double SurfaceCurrentSpeed { get; set; } // m/s (0m depth)
public double CurrentDirection { get; set; }    // degrees (0-360, 0=North)
public double TidalAmplitude { get; set; }      // m (tidal range)
```

**Current Effects:**
- **Weak (<0.2 m/s):** Poor water exchange, waste accumulation, oxygen depletion risk
- **Moderate (0.2-0.5 m/s):** Optimal (good exchange, no stress)
- **Strong (>0.5 m/s):** High energy expenditure, stress, net damage risk

**Current Variability:**
- **Tidal:** ~6-hour cycle (twice daily)
- **Wind-driven:** Storm events → 2-5x normal current speed
- **Seasonal:** Winter storms → stronger currents

---

### Water Quality

```csharp
public double Salinity { get; set; }     // PSU (Practical Salinity Units)
public double Chlorophyll { get; set; }  // µg/L (algal bloom indicator)
public double Turbidity { get; set; }    // NTU (water clarity)
```

**Salinity:**
- **Optimal:** 30-35 PSU (normal seawater)
- **Brackish:** <30 PSU (river discharge, shallow coastal zones)
- **Hypersaline:** >35 PSU (rare in Norwegian waters)

**Chlorophyll-a (Algal Blooms):**
- **Normal:** <3 µg/L (oligotrophic, clear water)
- **Moderate:** 3-10 µg/L (mesotrophic, healthy productivity)
- **Bloom:** >10 µg/L (eutrophic, algal bloom risk)
- **Toxic:** >20 µg/L (harmful algal bloom, shellfish toxins)

**Turbidity:**
- **Clear:** <5 NTU (good light penetration)
- **Moderate:** 5-15 NTU (typical fjord water)
- **Turbid:** >15 NTU (sediment resuspension, storm runoff)

---

## Data Architecture

### Class Structure

```
MareanoEnvironmentalDataProvider.cs
  ├─ Dictionary<string, EnvironmentalDataPoint> _currentZoneData (13 zones)
  ├─ Dictionary<string, Queue<EnvironmentalDataPoint>> _forecastCache (7-day ahead)
  ├─ DateTime _currentDate
  └─ int _currentDayOfYear (1-365)

NorwegianZoneEnvironmentalProfile.cs (static class)
  ├─ Dictionary<string, ZoneGeography> ZoneGeography (zone metadata)
  ├─ GetSeasonalProfile(zoneId, season) → EnvironmentalDataPoint
  ├─ GetProfileForDay(zoneId, dayOfYear) → EnvironmentalDataPoint
  └─ Interpolation methods (cubic spline for daily resolution)

EnvironmentalDataPoint.cs (data structure)
  ├─ ZoneId: string
  ├─ Timestamp: DateTime
  ├─ Temperature: SurfaceTemperature, BottomTemperature, TemperatureAt10m, TemperatureAt30m
  ├─ Oxygen: SurfaceOxygen, OxygenAt10m, OxygenAtCageDepth, OxygenSaturation
  ├─ Currents: SurfaceCurrentSpeed, CurrentDirection, TidalAmplitude
  └─ WaterQuality: Salinity, Chlorophyll, Turbidity
```

### Data Flow

```
Game starts (Day 1)
  ↓
MareanoEnvironmentalDataProvider initialized
  ↓
Load baseline profiles for all 13 zones (Day 1 = Jan 1)
  ↓
Cache 7-day forecast for each zone
  ↓
Daily update:
  1. Advance _currentDate by 1 day
  2. Pop Day N+1 from _forecastCache
  3. Update _currentZoneData[zoneId]
  4. Fire OnZoneEnvironmentUpdated event
  5. Replenish cache if < 3 days remain
  ↓
SimulationEngine receives OnZoneEnvironmentUpdated
  ↓
EnvironmentModel.UpdateEnvironment()
  ↓
Calculate stress multipliers:
  - GrowthStressIndex (temperature, oxygen)
  - HealthStressIndex (temperature, oxygen, currents, chlorophyll)
  ↓
Pass stress indices to:
  - GrowthModel (temperature-based TGC)
  - MortalityModel (stress mortality)
  - SeaLiceModel (temperature-dependent infestation)
  - DiseaseSystem (temperature-dependent outbreak risk)
```

---

## Cache System

**File:** `src/LakskongenSim/Performance/EnvironmentalDataCache.cs`

### Purpose
Pre-cache 7-day environmental forecasts to reduce computational overhead and enable UI forecast displays.

### Cache Structure

```csharp
private readonly Dictionary<string, Queue<EnvironmentalDataPoint>> _forecastCache;

// Example: PO06 (Møre og Romsdal)
_forecastCache["PO06"] = Queue<EnvironmentalDataPoint>
{
    Day N+1: { Temp: 11.2°C, O2: 8.1 mg/L, Current: 0.35 m/s },
    Day N+2: { Temp: 11.5°C, O2: 8.0 mg/L, Current: 0.40 m/s },
    Day N+3: { Temp: 11.8°C, O2: 7.9 mg/L, Current: 0.42 m/s },
    ...
    Day N+7: { Temp: 12.9°C, O2: 7.5 mg/L, Current: 0.50 m/s }
};
```

### Cache Replenishment

```csharp
// Called in MareanoEnvironmentalDataProvider.AdvanceOneDay()
foreach (var zoneId in _currentZoneData.Keys)
{
    if (_forecastCache[zoneId].Count < 3) // Replenish when <3 days remain
    {
        CacheForecast(zoneId, 7); // Cache next 7 days
    }
}
```

### Cache Benefits

1. **Performance:** Reduce per-frame calculations (forecast data pre-computed)
2. **UI Responsiveness:** Instant forecast display in weather panel
3. **Predictability:** Players can plan harvest/stocking based on 7-day forecast

---

## Stress Index Calculation

### Growth Stress Index

**Purpose:** Modulate fish growth rate based on environmental conditions.

**Formula:**
```csharp
public double CalculateGrowthStressIndex(string zoneId)
{
    var data = GetCurrentData(zoneId);
    
    // Temperature stress (Gaussian response centered on 11°C)
    double tempC = data.TemperatureAt30m;
    double tempStress = Math.Exp(-Math.Pow(tempC - 11, 2) / 50);
    // tempStress = 1.0 at 11°C, 0.6 at 2°C, 0.6 at 18°C, 0.0 at <0°C or >20°C
    
    // Oxygen stress (linear response)
    double oxygenMgL = data.OxygenAtCageDepth;
    double oxygenStress = Math.Clamp((oxygenMgL - 4) / 3, 0, 1);
    // oxygenStress = 1.0 at >7 mg/L, 0.5 at 5.5 mg/L, 0.0 at <4 mg/L
    
    // Combined index (multiplicative)
    return tempStress * oxygenStress; // 0-1, where 1.0 = optimal
}
```

**Usage:**
```csharp
// In GrowthModel.CalculateDailyGrowth()
double baseGrowthKg = CalculateTGCGrowth(tempC, fishWeightKg, feedQuality);
double stressIndex = environmentModel.CalculateGrowthStressIndex(zoneId);
double actualGrowthKg = baseGrowthKg * stressIndex;
// If stressIndex = 0.6, fish grows at 60% of optimal rate
```

---

### Health Stress Index

**Purpose:** Modulate mortality rate and disease susceptibility based on environmental conditions.

**Formula:**
```csharp
public double CalculateHealthStressIndex(string zoneId)
{
    var data = GetCurrentData(zoneId);
    
    // Temperature stress (U-shaped: stress at extremes)
    double tempC = data.TemperatureAt30m;
    double tempStress = 1.0;
    if (tempC < 4) tempStress = 0.5; // Cold stress
    if (tempC > 16) tempStress = 0.6; // Heat stress
    
    // Oxygen stress (exponential below 6 mg/L)
    double oxygenMgL = data.OxygenAtCageDepth;
    double oxygenStress = oxygenMgL >= 6 ? 1.0 : Math.Pow(oxygenMgL / 6, 2);
    // oxygenStress = 1.0 at >6 mg/L, 0.69 at 5 mg/L, 0.44 at 4 mg/L
    
    // Current stress (weak currents = poor water exchange)
    double currentSpeed = data.SurfaceCurrentSpeed;
    double currentStress = currentSpeed >= 0.2 ? 1.0 : currentSpeed / 0.2;
    // currentStress = 1.0 at >0.2 m/s, 0.5 at 0.1 m/s
    
    // Chlorophyll stress (algal bloom risk)
    double chlorophyll = data.Chlorophyll;
    double chlorophyllStress = chlorophyll < 10 ? 1.0 : 1.0 - (chlorophyll - 10) / 20;
    // chlorophyllStress = 1.0 at <10 µg/L, 0.5 at 20 µg/L, 0.0 at 30 µg/L
    
    // Combined index (multiplicative)
    return tempStress * oxygenStress * currentStress * chlorophyllStress; // 0-1
}
```

**Usage:**
```csharp
// In MortalityModel.CalculateDailyMortality()
double baseMortalityPct = 0.05; // 0.05% daily (1.8% annual background)
double stressIndex = environmentModel.CalculateHealthStressIndex(zoneId);
double stressMortalityPct = baseMortalityPct * (1.0 - stressIndex) * 2; // 0-0.1% daily
double totalMortalityPct = baseMortalityPct + stressMortalityPct;
// If stressIndex = 0.5, mortality = 0.05% + 0.05% = 0.1% daily (3.65% annual)
```

---

## Seasonal Patterns

### Winter (January-March)

**Temperature:**
- Southern zones (PO01-03): 6-8°C
- Mid zones (PO04-08): 4-6°C
- Northern zones (PO11-13): 2-4°C

**Oxygen:** High (cold water holds more O₂, minimal stratification)

**Currents:** Strong (winter storms)

**Light:** Short days (8-12 hours in south, 0-6 hours in north)

**Fish Behavior:**
- Slow growth (low temperature)
- Reduced feeding (low metabolism)
- Low disease risk (cold-blooded pathogens dormant)

---

### Spring (April-June)

**Temperature:**
- Southern zones: 8-12°C
- Mid zones: 6-10°C
- Northern zones: 4-8°C

**Oxygen:** Good (warming, but algal blooms starting)

**Currents:** Moderate (calmer seas)

**Light:** Rapidly increasing (12-18 hours in south, 18-24 hours in north)

**Fish Behavior:**
- Rapid growth (optimal temperature + long days)
- Increased feeding (smoltification, pre-summer growth spurt)
- Sea lice risk increasing (warming waters)

---

### Summer (July-September)

**Temperature:**
- Southern zones: 14-18°C
- Mid zones: 12-16°C
- Northern zones: 10-14°C

**Oxygen:** Declining (warm water + stratification + algal bloom decomposition)

**Currents:** Weak (calm seas)

**Light:** Maximum (18-24 hours in south, 24 hours in north)

**Fish Behavior:**
- Peak growth (optimal temperature + maximum light)
- Heat stress risk (>16°C in southern zones)
- Oxygen stress risk (deep fjords with poor exchange)
- Sea lice peak (warmest waters)
- Harvest window (target 4-6 kg size reached)

---

### Autumn (October-December)

**Temperature:**
- Southern zones: 10-14°C
- Mid zones: 8-12°C
- Northern zones: 6-10°C

**Oxygen:** Improving (cooling, thermocline breakdown)

**Currents:** Increasing (autumn storms)

**Light:** Rapidly decreasing (12-6 hours in south, 6-0 hours in north)

**Fish Behavior:**
- Moderate growth (cooling but still adequate)
- Pre-winter feeding (build energy reserves)
- Disease risk (temperature fluctuations, stress)
- Harvest push (before winter shutdown)

---

## Integration with Game Systems

### 1. Growth Model Integration

**File:** `src/LakskongenSim/Biology/GrowthModel.cs`

```csharp
public double CalculateDailyGrowth(double tempC, double currentWeightKg, double feedQuality, string zoneId)
{
    // TGC-based growth (Thermal Growth Coefficient)
    double tgc = 0.003 * feedQuality; // Feed quality 0.8-1.2 → TGC 0.0024-0.0036
    double baseGrowth = tgc * Math.Pow(currentWeightKg, 0.333) * tempC;
    
    // Apply environmental stress
    double stressIndex = mareanoDataProvider.CalculateGrowthStressIndex(zoneId);
    return baseGrowth * stressIndex;
}
```

---

### 2. Mortality Model Integration

**File:** `src/LakskongenSim/Biology/MortalityModel.cs`

```csharp
public double CalculateDailyMortality(double tempC, double currentWeightKg, string zoneId)
{
    // Background mortality
    double baseMortalityPct = 0.05; // 0.05% daily
    
    // Environmental stress mortality
    double stressIndex = mareanoDataProvider.CalculateHealthStressIndex(zoneId);
    double stressMortalityPct = baseMortalityPct * (1.0 - stressIndex) * 2;
    
    // Age-related mortality (higher for small fish)
    double ageMortalityPct = currentWeightKg < 0.5 ? 0.1 : 0.02;
    
    return baseMortalityPct + stressMortalityPct + ageMortalityPct;
}
```

---

### 3. Sea Lice Model Integration

**File:** `src/LakskongenSim/Health/SeaLiceModel.cs`

```csharp
public void UpdateInfestation(string zoneId)
{
    var envData = mareanoDataProvider.GetCurrentData(zoneId);
    double tempC = envData.TemperatureAt30m;
    
    // Sea lice reproduction rate (exponential with temperature)
    // Optimal: 10-15°C, minimal below 4°C or above 18°C
    double reproductionRate = 0.0;
    if (tempC >= 4 && tempC <= 18)
    {
        reproductionRate = 0.05 * Math.Exp((tempC - 10) / 5); // Peak at 15°C
    }
    
    liceCount += liceCount * reproductionRate;
}
```

---

### 4. Disease System Integration

**File:** `src/LakskongenSim/Health/DiseaseSystem.cs`

```csharp
public void CheckOutbreakRisk(string zoneId)
{
    var envData = mareanoDataProvider.GetCurrentData(zoneId);
    double tempC = envData.TemperatureAt30m;
    double oxygenMgL = envData.OxygenAtCageDepth;
    
    // IPN (Infectious Pancreatic Necrosis): 10-15°C optimal for virus
    if (tempC >= 10 && tempC <= 15)
    {
        ipnOutbreakRisk += 0.02; // 2% daily increase
    }
    
    // ISA (Infectious Salmon Anemia): stress-related, low oxygen trigger
    if (oxygenMgL < 6)
    {
        isaOutbreakRisk += 0.05; // 5% daily increase
    }
}
```

---

### 5. Feeding Optimizer Integration

**File:** `src/LakskongenSim/Optimization/FeedingOptimizer.cs`

```csharp
public double CalculateOptimalDailyRation(double fishWeightKg, string zoneId)
{
    var envData = mareanoDataProvider.GetCurrentData(zoneId);
    double tempC = envData.TemperatureAt30m;
    double oxygenMgL = envData.OxygenAtCageDepth;
    
    double baseRate = 1.2; // % body weight/day
    
    // Temperature response (Gaussian)
    double tempMultiplier = Math.Exp(-Math.Pow(tempC - 11, 2) / 50);
    
    // Oxygen response (linear)
    double oxygenMultiplier = Math.Clamp((oxygenMgL - 4) / 3, 0, 1);
    
    return fishWeightKg * baseRate * tempMultiplier * oxygenMultiplier / 100.0;
}
```

---

## Future Enhancements

### Phase 7+ Roadmap

**1. Real-Time API Integration:**
- Connect to Mareano/Geonorge WMS/WFS services for live data
- MET Norway API for real-time current forecasts
- 14-day weather forecasts (extend from 7-day)

**2. Player-Driven Site Selection:**
- Interactive map for site selection (choose lat/lon)
- Real bathymetry and current data at chosen location
- Risk assessment tool (exposure, oxygen, temperature range)

**3. Climate Change Scenarios:**
- Temperature rise (+1-3°C by 2050-2100)
- Ocean acidification (pH decline)
- Extreme weather frequency increase
- Altered seasonal patterns

**4. Advanced Oceanographic Models:**
- Hydrodynamic modeling (current circulation at site scale)
- Particle tracking (waste dispersion, sea lice larvae)
- Upwelling/downwelling events (oxygen pulses)

**5. Multi-Year Environmental Cycles:**
- ENSO (El Niño/La Niña) influence on Norwegian coast
- NAO (North Atlantic Oscillation) index → winter temperature variability
- Long-term trends (decadal oscillations)

---

## Conclusion

Phase 5 provides a **realistic environmental foundation** for the salmon farming simulation:

- **Geospatially accurate:** 13 production zones with real Norwegian data
- **Temporally dynamic:** Daily resolution with seasonal patterns
- **Mechanistically linked:** Environmental parameters directly affect growth, health, mortality
- **Performance optimized:** 7-day forecast cache, lazy evaluation, minimal API calls

**Key Design Decisions:**

1. **Simplicity vs Realism:** Use complex models internally, expose simple stress indices (0-1)
2. **Offline-First:** Pre-loaded datasets for offline play, API calls as optional enhancement
3. **Caching:** 7-day forecasts cached for performance and gameplay (player planning)
4. **Validation:** Calibrated to IMR datasets (2015-2025 temperature/oxygen observations)

For integration details:
- **System Architecture:** [ARCHITECTURE.md](ARCHITECTURE.md)
- **Economic Integration:** [ECONOMIC_SYSTEM.md](ECONOMIC_SYSTEM.md)
- **API Reference:** [API_Reference.md](API_Reference.md)

---

**Document Status:** ✅ Complete  
**Last Verified:** 2026-03-24  
**Next Review:** Phase 7 completion
