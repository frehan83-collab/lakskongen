# BarentsWatch Fishhealth Integration

## What was added

A new BarentsWatch Fishhealth integration layer now exists in `src/LakskongenSim/Core/BarentsWatchFishHealthIntegration.cs`.

It provides:
- secure client options for API or offline-file ingestion
- raw Fishhealth transport models for disease / lice oriented data
- normalization into `ExternalAquacultureSnapshot`
- warning generation for high lice, disease escalation, and environmental stress
- dynamic crisis scenario generation for gameplay modifiers
- snapshot persistence for reproducible simulation runs
- compact health analytics reports for dashboards and in-game messaging

## Runtime flow

`SimulationEngine.Initialize()` now:
1. initializes the BarentsWatch pipeline
2. attempts an ingest from either:
   - `BARENTSWATCH_TOKEN` + configured API endpoint, or
   - `game/data/barentswatch_fishhealth_raw.json`
3. stores normalized snapshots under runtime `Data/barentswatch-snapshots`
4. loads snapshot values into:
   - `GameState.ExternalAquacultureSnapshot`
   - `GameState.ExternalDataAnalytics`
   - `GameState.ExternalHealthWarnings`
   - `GameState.ExternalHealthCrisis`
   - `GameState.ExternalHealthAnalytics`
5. applies crisis-weighted modifiers to sea lice, disease, environment, and market systems

## Gameplay effects

The integration now affects:
- sea lice pressure multipliers
- integrated disease pressure
- zone environmental stress and nutrient pressure
- market shocks via disease / mortality / market support signals
- analytics snapshots with warning count and composite external risk

## Offline sample data

An example raw snapshot is included at:
- `game/data/barentswatch_fishhealth_raw.json`

This keeps the feature testable without live API credentials.

## Testing

New xUnit coverage is in:
- `tests/BarentsWatchIntegrationTests.cs`

The tests cover:
- normalization of raw fishhealth payloads
- pipeline ingest + persisted snapshot reload
- crisis / warning generation for high-risk conditions

## Production hookup notes

To switch from offline mode to live mode:
1. provide `BARENTSWATCH_TOKEN`
2. update `BarentsWatchClientOptions.FishHealthPath` if BarentsWatch exposes a different endpoint path
3. ensure the live payload maps to `BarentsWatchFishHealthRawSnapshot`

If the actual API schema differs, adapt only the raw transport model / deserialization layer. The downstream pipeline, normalization, analytics, and gameplay wiring can remain stable.
