# GameState Serialization Reference

## Overview
All public properties in `GameState` are automatically serialized to JSON by `SaveLoadManager` using `System.Text.Json`.

## Serialized Properties

### Core Simulation
- `Day` (int) - Current simulation day
- `Balance` (decimal) - Monetary balance in NOK
- `Zones` (List<Environment.Zone>) - Environment zones
- `SeaSites` (List<Farm.SeaSite>) - Active farms/sea sites
- `Populations` (List<Biology.FishPopulation>) - Fish populations
- `ProcessingSnapshots` (List<Processing.ProcessingSnapshot>) - Processing P&L snapshots
- `EventLog` (List<Events.EventRecord>) - Event history
- `DailySnapshots` (List<DailySnapshot>) - Daily analytics snapshots

### Research & Tech Tree
- `UnlockedResearchIds` (List<string>) - Completed research nodes
- `ActiveResearchId` (string?) - Currently active research (nullable)
- `ResearchProgress` (int) - Days of progress on active research

### Achievement Tracking
- `ExportMarkets` (HashSet<string>) - Markets player has sold to
- `AcquisitionCount` (int) - Number of competitor farms acquired
- `OwnsFeedMill` (bool) - Feed mill ownership
- `OwnsHatchery` (bool) - Hatchery ownership
- `OwnsProcessingPlant` (bool) - Processing plant ownership
- `HasASCCertification` (bool) - ASC certification status
- `BestFCR` (double) - Best (lowest) FCR achieved
- `BestGrowthBreedingValue` (double) - Highest growth breeding value
- `BestDiseaseResistanceBreedingValue` (double) - Highest disease resistance breeding value
- `MinimumBalanceEver` (decimal) - Historical minimum balance
- `UnlockedAchievementIds` (HashSet<string>) - Unlocked achievements

### Sustainability
- `FeedSustainabilityRating` (double) - Feed sustainability 0.0-1.0
- `SustainabilityScore` (int) - Overall sustainability score 0-100
- `SustainabilityCertification` (CertificationLevel enum) - Current certification level

### Scenarios
- `ActiveScenarioId` (string?) - Active scenario ID (nullable)
- `ScenarioIsWon` (bool) - Scenario victory status
- `ScenarioIsLost` (bool) - Scenario loss status
- `ScenarioLossReason` (string) - Reason for scenario loss
- `ScenarioStartDay` (int) - Day scenario started

### Tutorial
- `TutorialCurrentStepIndex` (int) - Current tutorial step
- `CompletedTutorialStepIds` (HashSet<string>) - Completed tutorial steps

### Excluded (JsonIgnore)
- `LogisticsManager` (Logistics.LogisticsManager?) - Runtime manager, not serialized

## Backward Compatibility

### Collections
Missing collections in older save files are initialized to empty:
- All List<T> properties → new List<T>()
- All HashSet<T> properties → new HashSet<T>()

### Special Default Values
- `BestFCR`: 0.0 → double.MaxValue (no record yet)
- `MinimumBalanceEver`: 0.0m → decimal.MaxValue (no low yet)
- `FeedSustainabilityRating`: 0.0 → 0.7 (70% sustainable default)
- `ScenarioLossReason`: null → "" (empty string)

### Nullable Properties
Nullable properties (string?, int?) default to null if missing from save file.

### New Properties
Adding new properties to GameState is safe - they will use default/initialized values when loading older saves.

## JSON Format

### Enum Serialization
Enums are serialized as strings (via JsonStringEnumConverter):
```json
"SustainabilityCertification": "ASCCertified"
```

### Collections
```json
"UnlockedResearchIds": ["tech-node-1", "tech-node-2"],
"ExportMarkets": ["EU", "USA", "China"]
```

### Nullable Values
```json
"ActiveResearchId": null,
"ActiveScenarioId": "crisis-2025"
```

## Testing
To verify all properties serialize correctly:
1. Create a GameState with all properties populated
2. Call SaveLoadManager.SaveGame()
3. Load the save file and verify all properties match
4. Test backward compatibility by removing properties from JSON and loading
