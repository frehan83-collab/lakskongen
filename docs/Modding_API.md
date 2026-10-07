# Lakskongen — Modding API

**Status:** Data mods implemented (JSON). Script/asset/map mods are **planned**.
**Source of truth:** `src/LakskongenSim/Core/ModLoader.cs`.

This document describes the contract the game actually enforces today. Anything
not backed by `ModLoader.cs` is explicitly marked **[PLANNED]**.

---

## 1. What a mod is

A mod is a **folder** containing a `manifest.json` plus any number of JSON data
files listed in that manifest. The loader scans every subdirectory of the mods
directory, so the folder name itself is not significant.

```
example_norwegian_pack/
├── manifest.json
├── research_arctic_breeding.json     # → research_* prefix
├── events_winter_storm.json          # → events_*  prefix
└── scenario_fjord_pioneer.json       # → scenario* prefix
```

There is **no** `.lkmod`, zip, `.pck`, Lua, or `entryPoint` support. Those are
**[PLANNED]** (see §8).

### Mods directory

`ModLoader` is constructed with an explicit directory. In-game,
`GameBridge.ResolveModsDirectory()` resolves:

1. `user://mods` (preferred — writable, survives updates), else
2. a `mods/` folder beside the executable.

In the repo the example pack lives at `godot/mods/example_norwegian_pack/`.
Enable/disable state is persisted to `mods_config.json` inside the mods directory.

---

## 2. Manifest — `manifest.json` → `ModManifest`

```json
{
  "id": "example_norwegian_pack",
  "name": "Example Norwegian Pack",
  "author": "Lakskongen Team",
  "version": "1.0.0",
  "description": "Reference data mod.",
  "files": [
    "research_arctic_breeding.json",
    "events_winter_storm.json",
    "scenario_fjord_pioneer.json"
  ],
  "dependencies": []
}
```

| JSON field | C# property | Type | Required | Notes |
|---|---|---|---|---|
| `id` | `Id` | string | **yes** | Must be unique; duplicates are rejected. |
| `name` | `Name` | string | **yes** | Display name. |
| `author` | `Author` | string | **yes** | Creator. |
| `version` | `Version` | string | no | Defaults to `"1.0.0"`. |
| `description` | `Description` | string | no | Shown in the Mod Manager. |
| `files` | `Files` | string[] | no | Relative paths from the mod folder. Missing files become load errors. |
| `dependencies` | `Dependencies` | string[] | no | Mod IDs loaded first (topological sort). Missing dep → error; cycle → error. |

Discovery validation (`DiscoverManifests`) rejects a mod when `id`, `name`, or
`author` is blank. Invalid manifests are collected in `ModLoader.LoadErrors` and
skipped. Deserialization is case-insensitive and tolerates comments and trailing
commas.

`Enabled` and `FolderPath` are runtime-only; `FolderPath` is `[JsonIgnore]` and
`Enabled` is applied from `mods_config.json` (not the manifest).

### Dependency ordering

`SortByDependencies` performs a depth-first topological sort. Dependencies that
are not installed produce `"Mod '<id>' depends on missing mod '<dep>'"`;
circular dependencies produce `"Circular dependency detected involving mod '<id>'"`.

---

## 3. File dispatch by filename prefix

`ModLoader.LoadMod()` lowercases the filename and dispatches on the first path
segment. Unknown prefixes are rejected with
`"Unknown file type (unrecognised prefix)"`.

| Prefix | Target type | Runtime collection | Payload shape |
|---|---|---|---|
| `events...` | `ModEventDefinition` | `LoadedMod.Events` | **JSON array** |
| `research...` | `ModResearchNode` | `LoadedMod.ResearchNodes` | **JSON array** |
| `fish...` | `ModFishSpecies` | `LoadedMod.FishSpecies` | **JSON array** |
| `scenario...` | `ModScenario` | `LoadedMod.Scenarios` | **JSON array** |

Matching uses `StartsWith`, so both `scenario_*.json` and `scenarios_*.json`
load as scenarios. Each listed file **must** deserialize to an array of the
matching type or the mod records a parse error.

Accessors on `ModLoader` aggregate across all **enabled** mods:
`BuildEventDefinitions(finance)`, `GetAllResearchNodes()`, `GetAllFishSpecies()`,
`GetAllScenarios()`.

---

## 4. Event schema — `ModEventDefinition`

```json
[
  {
    "id": "evt_winter_storm",
    "name": "Winter Storm",
    "category": "Weather",
    "weight": 1.5,
    "cooldown_days": 90,
    "min_day": 30,
    "max_day": 2147483647,
    "description": "A fierce storm batters the pens.",
    "effects": {
      "balance_change": -350000.0,
      "mortality_change": 0.8
    },
    "condition_min_balance": 500000.0,
    "condition_min_day": 30
  }
]
```

| JSON field | Type | Default | Meaning |
|---|---|---|---|
| `id` | string | new GUID | Unique event id. |
| `name` | string | `""` | Display name. |
| `category` | string | `"General"` | Parsed against `EventCategory`; unknown → `Other`. |
| `weight` | double | `1.0` | Relative selection weight. |
| `cooldown_days` | int | `30` | Minimum days between fires. |
| `min_day` / `max_day` | int | `0` / `int.MaxValue` | Day window. |
| `description` | string | `""` | Shown in the event record. |
| `effects` | map<string,double> | `{}` | Numeric deltas (see below). |
| `condition_min_balance` | double? | null | Optional: requires `state.Balance >= value`. |
| `condition_min_day` | int? | null | Optional: requires `state.Day >= value`. |

**Effect keys:** `balance_change`, `lice_change`, `sustainability_change`,
`mortality_change`. `ModLoader.BuildEventDefinitions()` currently settles only
`balance_change` through `FinanceManager` (positive = revenue including tax
settlement, negative = forced spend into debt); the other keys are carried for
future systems.

**Valid `category` values** (`LakskongenSim.Events.EventCategory`): `Weather`,
`Disease`, `Market`, `Regulation`, `Equipment`, `Community`, `Investor`,
`Competitor`, `PlanktonBloom`, `PlanktonCrash`, `Other`.

---

## 5. Research schema — `ModResearchNode`

```json
[
  {
    "id": "arctic_breeding_program",
    "name": "Arctic Breeding Program",
    "description": "Selective breeding for cold-water tolerance.",
    "branch": "Biology",
    "tier": 2,
    "cost_nok": 900000,
    "research_days": 120,
    "prerequisites": [],
    "effects": { "mortality_reduction": 0.02, "growth_rate_bonus": 0.05 }
  }
]
```

| JSON field | Type | Default |
|---|---|---|
| `id` | string | `""` |
| `name` | string | `""` |
| `description` | string | `""` |
| `branch` | string | `"Biology"` |
| `tier` | int | `1` |
| `cost_nok` | decimal | `0` |
| `research_days` | int | `30` |
| `prerequisites` | string[] | `[]` |
| `effects` | map<string,double> | `{}` |

Research costs should sit inside the calibrated envelope (base ~450,000 NOK, max
~4,500,000 NOK) — see `docs/CALIBRATION.md`.

---

## 6. Fish species schema — `ModFishSpecies`

```json
[
  {
    "id": "rainbow_trout",
    "name": "Rainbow Trout",
    "scientific_name": "Oncorhynchus mykiss",
    "description": "A cold-water species with a lower market price than salmon.",
    "growth_rate_multiplier": 0.95,
    "optimal_temp_min": 10.0,
    "optimal_temp_max": 16.0,
    "base_fcr": 1.1,
    "price_multiplier": 0.85,
    "max_harvest_weight_kg": 3.5
  }
]
```

All fields map 1:1 to `ModFishSpecies` with sensible defaults (e.g.
`growth_rate_multiplier = 1.0`, `optimal_temp_min/max = 8/14`, `base_fcr = 1.15`,
`price_multiplier = 1.0`, `max_harvest_weight_kg = 5.0`).

---

## 7. Scenario schema — `ModScenario`

```json
[
  {
    "id": "scenario_fjord_pioneer",
    "name": "Fjord Pioneer",
    "description": "Start a single-licence operation in a remote fjord.",
    "starting_capital_nok": 5000000,
    "starting_region": "Vestland",
    "difficulty": 2,
    "win_condition": "Reach 500000 kg harvested.",
    "initial_balance_nok": 5000000,
    "starting_fish_count": 50000,
    "initial_events": ["evt_winter_storm"]
  }
]
```

| JSON field | Type | Default |
|---|---|---|
| `id` | string | `""` |
| `name` | string | `""` |
| `description` | string | `""` |
| `starting_capital_nok` | decimal | `5000000` |
| `starting_region` | string | `"Vestland"` |
| `difficulty` | int | `2` |
| `win_condition` | string | `""` |
| `initial_balance_nok` | decimal | `5000000` |
| `starting_fish_count` | int | `50000` |
| `initial_events` | string[] | `[]` — event ids seeded at scenario start |

---

## 8. [PLANNED] Not yet implemented

These features appear in older docs/roadmaps but are **not** backed by
`ModLoader.cs` and must be treated as design intent only:

- **Script mods** — GDScript/C# entry points (`entryPoint`, `scripts/*.gd`),
  a `ModAPI`, and event-handler registration.
- **Asset/map/campaign mods** — texture/model/audio overrides, custom
  `BaseFjordMap` scenes, campaign objective graphs.
- **Packaging** — `.lkmod`/zip/`.pck` archives, Steam Workshop publishing and a
  mod validator/packaging CLI.
- **Lua scripting** and a visual mod editor.
- **Sandboxing policy** — no allow/deny list for filesystem/network is enforced
  by the loader; only JSON data is read.

Until these land, a mod can only add **data**: events, research nodes, fish
species and scenarios.

---

## 9. Writing & testing a mod

1. Copy a folder (e.g. `godot/mods/example_norwegian_pack/`) into the mods
   directory.
2. Ensure `manifest.json` lists every data file and that each file's prefix
   matches its intended type.
3. Launch the game and enable the mod in the Mod Manager (persisted to
   `mods_config.json`).
4. Watch the console — `ModLoader` logs
   `✅ Loaded mod: <name> v<version> by <author>` or
   `⚠️ Mod <name> loaded with N error(s)`. Per-file errors land on
   `LoadedMod.Errors`; discovery errors land on `ModLoader.LoadErrors`.

Validate JSON syntax before shipping:

```bash
python3 -m json.tool godot/mods/example_norwegian_pack/manifest.json
```

---

## 10. Best practices

- Use lowercase, underscore-separated `id`s (`example_norwegian_pack`).
- Namespace your content ids (`evt_`, `research_`, `scenario_`) to avoid clashes.
- Declare `dependencies` instead of assuming another mod is installed.
- Keep balance values near the calibrated real-world bands documented in
  `docs/CALIBRATION.md` and `docs/REALISM_CHECKLIST.md`.
- Include a `README.md` in every mod folder.
