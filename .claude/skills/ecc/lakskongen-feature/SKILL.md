---
name: lakskongen-feature
description: Repo-native checklist for adding or changing a LakskongenSim subsystem — determinism, calibration SSOT, ITickSystem/ISnapshotable, bridge trio, player-path test, screenshot/CI evidence gates. Load before any engine/feature work in src/ or godot/.
origin: project
---

# lakskongen-feature — add a subsystem the repo way

Use this skill whenever a change touches the simulation engine (`src/LakskongenSim/`),
the bridge (`godot/scripts/autoload/GameBridge*.cs` + `godot/scripts/bridge/`), or
ships a feature that claims player value. It encodes the rules from `AGENTS.md`
(engineering SSOT) and `docs/RUN_LOG.md` (evidence contract).

**Golden rule:** a feature is "done" only with (1) `./tools/ci.sh` green on the
committed tree, (2) a player-path test showing an outcome change, (3) for visual
work a labeled screenshot diff. Chat is not evidence.

## Pre-flight (5 minutes, saves whole sessions)

1. Read `AGENTS.md` (repo root) — determinism, calibration, bridge sections.
2. Read `docs/RUN_LOG.md` §1 (evidence contract) and §2 (add-only scoreboard).
3. `git log --oneline -5` + `git status --short` — know your base SHA.
4. Run `./tools/ci.sh` once before you start (baseline; do not touch a red tree).

## The checklist (from AGENTS.md "Adding New Features Checklist")

1. **Determinism first**
   - Randomness: `SimRng` SplitMix64 streams from `SimRngStreams` (frozen enum).
     Create streams **eagerly in `Initialize()`** — `ImportStates` only imports
     keys that already exist (classic divergence bug).
   - Banned anywhere in `src/`: `Random`, `DateTime.Now`, `Guid.NewGuid()`
     (enforced by `DeterminismBanTests`).
   - If you add a stream: append to the enum only — never reorder (frozen order).
2. **Calibration SSOT**
   - All numeric constants go in `CalibrationRegistry` / `BalancingConfig`
     (`godot/data/calibration.json` mirrors it). A grep test fails on literals in
     tick code. If a realism band is missed, fix the model — never clamp to pass
     (`docs/REALISM_CHECKLIST.md` is the band ledger).
3. **Tick system**: implement `ITickSystem`; register in `_inDayPhases` or
   `_postTickPhases` in `SimulationEngine` — pick deliberately (ordering matters).
4. **Snapshotable**: implement `ISnapshotable`; add state to `GameState`; restore in
   `AdoptSavedState`. Remember the save-hash tests below.
5. **Player-path test**: extend `PlayerScriptProbe` or add a dedicated test that
   shows a *different outcome* for the player when the feature is on. "It
   compiles" is not a player path.
6. **Bridge trio** (only if UI needs it):
   - C# partial: `GameBridge.<Area>.cs` (methods live in partials, never in
     `GameBridge.cs` itself). Return `Godot.Collections.Dictionary` with
     `success`/`message` keys; `EmitSignal(SignalName.GameStateUpdated)` on change.
   - GDScript proxy: `godot/scripts/bridge/SimProxies.gd`.
   - Forwarder: `godot/scripts/SimBridge.gd` (one-line delegation).
   - Static contract test parses `GameBridge*.cs` (`tests/test_bridge_contract.py`
     via pytest) — new bridge methods must be in a partial or it fails.
7. **Tests**: unit + integration + determinism. The three gates that catch
   nearly everything:
   - `dotnet test src/LakskongenSim.Tests --filter "FullyQualifiedName~<YourTests>"` (fast loop)
   - `dotnet test src/LakskongenSim.Tests --filter "FullyQualifiedName~EngineTwinRunDeterminismTests"` (twin engines, same seed)
   - `dotnet test src/LakskongenSim.Tests --filter "FullyQualifiedName~ExactSaveHashTests"` (mid-run save → load → continue is bit-identical)
8. **Visual claims**: if the UI changed, capture via the
   `lakskongen-screenshot-evidence` skill — headless "green" is not evidence.

## Full CI (required before every commit)

```bash
./tools/ci.sh            # 15 steps; must end "CI PASSED"
```

## Common failure modes (seen in this repo)

| Symptom | Real cause | Fix |
|---|---|---|
| Twin-run diverges after load | New RNG stream not created eagerly in `Initialize` | Create in `Initialize`, import in `ImportStates` |
| Save hash changes unexpectedly | New `GameState` field with a default that serializes | Give it a deterministic default; check the comparative hash test's first-diff output |
| Bridge contract test fails | Method added to `GameBridge.cs` (anchor file) instead of a partial | Move to `GameBridge.<Area>.cs` |
| Calibration grep test fails | Literal number in tick code | Hoist into `CalibrationRegistry`/`BalancingConfig` |
| Screenshot harness "passes" but UI broken | Ran headless (no pixel data) | Use `tools/capture_screenshots.sh` on a desktop session |
| `git add` sweeps generated junk | `godot/screenshots/game/`, `artifacts/`, `mods_config.json` | They're gitignored; verify `git status` before commit |

## Evidence record (for RUN_LOG session reports)

```
Shipped: <files + paths>
Evidence: <exact command> → <raw result tail>; commit <SHA>
CI: ./tools/ci.sh → CI PASSED (15/15)
```

Never weaken tests/CI/thresholds to get green — if a test looks wrong, log a
`Dispute` row in `docs/RUN_LOG.md` §8 and stop that item (human gate).
