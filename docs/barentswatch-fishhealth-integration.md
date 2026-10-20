# BarentsWatch Fishhealth Integration

## What was added

A new BarentsWatch Fishhealth integration layer now exists in `src/LakskongenSim/Core/BarentsWatchFishHealthIntegration.cs`.

It provides:
- offline-file ingestion (no network I/O; roadmap D18)
- raw Fishhealth transport models for disease / lice oriented data
- normalization into `ExternalAquacultureSnapshot`
- warning generation for high lice, disease escalation, and environmental stress
- dynamic crisis scenario generation for gameplay modifiers
- snapshot persistence for reproducible simulation runs
- compact health analytics reports for dashboards and in-game messaging

## Runtime flow (offline only, roadmap D18)

`SimulationEngine.Initialize()` reads data, it never fetches it. The live API
fetch (bearer token from `BARENTSWATCH_TOKEN`) and the snapshot cache it wrote
next to the binary were removed in S2-02: with a token one machine played on
live data and another on defaults, and the newest cached file decided the game.
`OfflineDataTests` listens to every `System.Net` event source with the token set.

1. looks for a bundled raw snapshot (`game/data/barentswatch_fishhealth_raw.json`
   or `Data/barentswatch_fishhealth_raw.json`); without one the external data stay
   at their defaults
2. normalizes it in memory (`BarentsWatchFishHealthPipeline.ReadOffline`; nothing
   is written) and keeps the file's own retrieval time
3. loads the values into:
   - `GameState.ExternalAquacultureSnapshot`
   - `GameState.ExternalDataAnalytics`
   - `GameState.ExternalHealthWarnings`
   - `GameState.ExternalHealthCrisis`
   - `GameState.ExternalHealthAnalytics`
4. applies crisis-weighted modifiers to sea lice, disease, environment, and market systems

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

## Refreshing the data

A fresh snapshot is exported outside the game (a tool with the BarentsWatch API
credentials writes `barentswatch_fishhealth_raw.json`) and bundled with the build;
`IngestLatest` can still persist a normalized copy for tools. The simulation
itself has no network client (D18).

If the actual API schema differs, adapt only the raw transport model / deserialization layer. The downstream pipeline, normalization, analytics, and gameplay wiring can remain stable.
