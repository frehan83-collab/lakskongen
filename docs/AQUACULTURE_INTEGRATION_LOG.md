# Aquaculture Integration Log

## 2026-03-13

### Scope completed
- Replaced the placeholder snapshot generator with a proper offline-first ingestion pipeline in `scripts/update_aquaculture_snapshot.py`.
- Added provider client scaffolding for:
  - BarentsWatch Fishhealth (OAuth client credentials + weekly aggregate/locality endpoints)
  - Fiskeridirektoratet public APIs
  - SSB market anchors
- Expanded the compact runtime snapshot schema to include:
  - disease pressure
  - escape risk
  - environmental compliance
  - temperature anomaly
  - treatment intensity
  - mortality stress
  - locality crowding
  - market price support
  - active source flags
  - regional profiles
- Added `AquacultureDataIntegration.cs` for regional mapping and analytics summary generation.
- Extended runtime calibration in `AquacultureCalibrationEngine.cs` to affect:
  - growth
  - mortality
  - delousing cost
  - welfare pressure
  - escape fine pressure
  - market support
  - disease pressure
  - temperature stress
- Wired new signals into runtime consumers:
  - `SimulationEngine`
  - `IntegratedDiseaseSystem`
  - `MarketSimulation`
  - `Zone`
  - `AnalyticsTracker`
  - `GameState`

### Calibration changes
- Disease pressure now scales disease transmission and disease-linked mortality.
- Environmental compliance now influences nutrient loading and zone stress.
- Temperature anomaly now directly penalizes growth conditions in zones.
- Treatment intensity now increases welfare/cost pressure rather than acting only as flavor.
- Market support index now nudges supply/demand expectations instead of hard-overwriting prices.

### Output files
- `game/data/aquaculture_snapshot.json`
- `game/data/aquaculture_regions.json`

### Remaining work / known gaps
- BarentsWatch and some Fiskeridirektoratet endpoints may require endpoint-specific payload tuning once real credentials/sample payloads are available.
- SSB table ID currently acts as a market-anchor placeholder and should be swapped to the final chosen salmon export series.
- No compile/test pass was completed yet after these code changes.
- No UI panel wiring was added yet for exposing the new external-data analytics summary.

### Recommended next step
1. Run the Python ingestion script with real fixtures or credentials.
2. Build the .NET solution and fix any integration compile errors.
3. Surface `ExternalDataAnalytics` in Godot analytics/dashboard UI.
4. Add a small dev command to refresh snapshots on demand.
