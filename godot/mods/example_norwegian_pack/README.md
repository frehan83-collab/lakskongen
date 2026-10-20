# Example Norwegian Pack

A reference **data mod** for Lakskongen. It demonstrates the exact contract the
in-game `ModLoader` (`src/LakskongenSim/Core/ModLoader.cs`) deserializes: a
`manifest.json` plus one JSON file per content type, dispatched by filename prefix.

This mod is safe to enable — it only adds content and never mutates base-game data.

## What's in the pack

| File | Type | Loaded as |
|---|---|---|
| `research_arctic_breeding.json` | Research nodes | `ModResearchNode[]` |
| `events_winter_storm.json` | Events | `ModEventDefinition[]` |
| `scenario_fjord_pioneer.json` | Scenario | `ModScenario[]` |

## Install

1. Copy the whole `example_norwegian_pack/` folder into your mods directory:
   - **Godot editor / dev builds:** `godot/mods/` or the per-user dir the engine
     resolves, which is normally `user://mods/`
     (`GameBridge.ResolveModsDirectory()` prefers `user://mods`, then falls back
     to a `mods/` folder beside the executable).
2. Launch the game. `ModLoader.DiscoverAndLoad()` scans every subdirectory of the
   mods dir for a `manifest.json`.
3. Enable/disable the mod in-game (Mod Manager). That state is persisted to
   `mods_config.json` inside the mods directory; `DiscoverAndLoad()` reads it on
   the next reload and only loads **enabled** mods.

The mods directory is configurable because `ModLoader` is constructed with an
explicit directory — there is no fixed global path baked into the loader.

## Manifest schema

`manifest.json` is deserialized into `ModManifest`. Required fields (`id`, `name`,
`author`) are validated at discovery time; a missing one is reported in
`ModLoader.LoadErrors` and the mod is skipped.

```json
{
  "id": "example_norwegian_pack",
  "name": "Example Norwegian Pack",
  "author": "Lakskongen Team",
  "version": "1.0.0",
  "description": "Short description of the pack.",
  "files": ["research_arctic_breeding.json", "events_winter_storm.json"],
  "dependencies": []
}
```

| Field | JSON name | Type | Required | Notes |
|---|---|---|---|---|
| `Id` | `id` | string | yes | Unique across installed mods; duplicate IDs are rejected. Lowercase, no spaces recommended. |
| `Name` | `name` | string | yes | Display name. |
| `Author` | `author` | string | yes | Creator name/handle. |
| `Version` | `version` | string | no | Defaults to `"1.0.0"`. |
| `Description` | `description` | string | no | Shown in the Mod Manager. |
| `Files` | `files` | string[] | no | Relative paths (from the mod folder) of JSON data files to load. |
| `Dependencies` | `dependencies` | string[] | no | Other mod IDs that must load first. Missing deps produce a load error; cycles are detected. |

`Enabled` and `FolderPath` are runtime-only and are **not** part of the JSON
(`FolderPath` is `[JsonIgnore]`; `Enabled` is applied from `mods_config.json`).

## Prefix dispatch (how files are typed)

`ModLoader.LoadMod()` inspects the **lowercased filename** and dispatches on the
first path segment prefix. The prefix must match or the file is rejected with
`Unknown file type (unrecognised prefix)`:

| Filename prefix | Deserialized type | Runtime collection |
|---|---|---|
| `events...` | `ModEventDefinition[]` | `LoadedMod.Events` |
| `research...` | `ModResearchNode[]` | `LoadedMod.ResearchNodes` |
| `fish...` | `ModFishSpecies[]` | `LoadedMod.FishSpecies` |
| `scenario...` | `ModScenario[]` | `LoadedMod.Scenarios` |

Because matching uses `StartsWith`, `scenario_fjord.json` and
`scenarios_fjord.json` both load as scenarios. Every listed file must parse as a
**JSON array** of the matching type.

### Event schema — `ModEventDefinition`

```json
{
  "id": "evt_winter_storm",
  "name": "Winter Storm",
  "category": "Weather",
  "weight": 1.5,
  "cooldown_days": 90,
  "min_day": 30,
  "max_day": 2147483647,
  "description": "A fierce storm batters the pens.",
  "effects": { "balance_change": -350000.0, "mortality_change": 0.8 },
  "condition_min_balance": 500000.0,
  "condition_min_day": 30
}
```

- `category` is parsed against `LakskongenSim.Events.EventCategory`
  (`Weather`, `Disease`, `Market`, `Regulation`, `Equipment`, `Community`,
  `Investor`, `Competitor`, `PlanktonBloom`, `PlanktonCrash`, `Other`);
  unknown values fall back to `Other`.
- `effects` keys: `balance_change`, `lice_change`, `sustainability_change`,
  `mortality_change`. Only `balance_change` is currently settled by
  `ModLoader.BuildEventDefinitions()` (positive = revenue, negative = spend).
- `condition_min_balance` / `condition_min_day` are optional gates.

### Research node schema — `ModResearchNode`

```json
{
  "id": "arctic_breeding_program",
  "name": "Arctic Breeding Program",
  "description": "Selective breeding for cold-water tolerance.",
  "branch": "Biology",
  "tier": 2,
  "cost_nok": 900000,
  "research_days": 120,
  "prerequisites": [],
  "effects": { "mortality_reduction": 0.02 }
}
```

### Scenario schema — `ModScenario`

```json
{
  "id": "scenario_fjord_pioneer",
  "name": "Fjord Pioneer",
  "description": "Start a single-licence operation in a remote fjord.",
  "starting_capital_nok": 5000000,
  "starting_region": "Vestland",
  "difficulty": 2,
  "win_condition": "Profit",
  "initial_balance_nok": 5000000,
  "starting_fish_count": 50000,
  "initial_events": ["evt_winter_storm"]
}
```

`win_condition` is one of the `WinCondition` enum values — `Tutorial`, `Profit`,
`Sustainability`, `Survival`, `Competition`. Unknown values fall back to
`Profit`. `initial_events` references event `id`s (they may come from this mod
or another loaded mod).

## Fish species (not used by this pack)

For completeness, `fish*.json` files load as `ModFishSpecies[]` with fields:
`id`, `name`, `scientific_name`, `description`, `growth_rate_multiplier`,
`optimal_temp_min`, `optimal_temp_max`, `base_fcr`, `price_multiplier`,
`max_harvest_weight_kg`.

## Validation

All JSON files in this pack are plain JSON arrays/objects and are validated in CI
with:

```bash
python3 -m json.tool manifest.json
python3 -m json.tool research_arctic_breeding.json
python3 -m json.tool events_winter_storm.json
python3 -m json.tool scenario_fjord_pioneer.json
```

See `docs/Modding_API.md` for the full contract and `docs/CALIBRATION.md` for how
base-game balance values (which mods complement) are governed.
