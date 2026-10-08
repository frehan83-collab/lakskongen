# 🐟 Lakskongen — From Egg to Export

**A deep salmon-farming simulation set in the fjords of Norway.**

[![Engine](https://img.shields.io/badge/Godot-4.7-blue.svg)](https://godotengine.org/)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![Status: Playable Alpha](https://img.shields.io/badge/Status-Playable%20Alpha-brightgreen.svg)]()
[![Tests: 663 passing](https://img.shields.io/badge/Tests-663%20passing-success.svg)]()

Lakskongen puts you in charge of a Norwegian salmon farm and scales you from a
single net pen to a vertically integrated corporation: egg → smolt → sea site →
processing → export. It simulates real biology (genetics, growth, disease, sea
lice), a calibrated Norwegian market, regulation (traffic lights, MAB), and the
economics of feed, tax and resource rent — aiming for depth a surface
aquaculture minigame can't reach. See [VISION.md](VISION.md) and
[PLAN.md](PLAN.md).

Current state: **[STATUS.md](STATUS.md)** · Roadmap: **[PLAN.md](PLAN.md)** ·
History: **[CHANGELOG.md](CHANGELOG.md)**.

---

## Requirements

- **Godot 4.7 with .NET support**
- **.NET 10 SDK**
- **Python 3.12** (tooling only: tokens, textures, store art, CI helpers)

## Quick start

```bash
# Build the whole solution (sim library + Godot C# assembly)
dotnet build LakskongenSim.sln

# Run the full test suite (663 passing, 1 skipped)
dotnet test LakskongenSim.sln

# Launch the game
godot --path godot
# …or open godot/project.godot in the Godot 4.7 .NET editor and press F5.

# Run the CI quality gate locally
tools/ci.sh
```

The CI gate runs build, tests, the performance benchmark, design-token sync,
JSON validation, scene-load validation and a headless GDScript parse scan
(7 steps). See [docs/CI.md](docs/CI.md).

## Regenerating generated assets

All generators are deterministic and safe to re-run:

```bash
python3 tools/sync_tokens.py --write   # design/tokens.json -> godot/theme/tokens.json
python3 tools/gen_textures.py          # 14 tileable fjord textures + normal maps
python3 tools/gen_store_art.py         # Steam "Nordic Aurora Fjord" capsules
```

The screenshot harness renders with a graphics context and therefore needs a
real display (it cannot run headless):

```bash
tools/capture_screenshots.sh           # writes screenshots/game/*.png
```

See [docs/SCREENSHOTS.md](docs/SCREENSHOTS.md) for the shot list.

## Documentation

- **[docs/INDEX.md](docs/INDEX.md)** — index of everything in `docs/`, plus the
  historical root-level reports.
- **[PLAN.md](PLAN.md)** — roadmap and the current program of work.
- **[STATUS.md](STATUS.md)** — build / test / perf snapshot.
- **[docs/BALANCE.md](docs/BALANCE.md)** — calibration and measured realism.
- **[docs/Architecture.md](docs/Architecture.md)** — system architecture.
- **[SCENARIOS.md](SCENARIOS.md)** — campaign and sandbox scenarios.
- **[docs/Modding_API.md](docs/Modding_API.md)** — mod contract + sample mod.
- **[design/ART_DIRECTION.md](design/ART_DIRECTION.md)** — art direction.
- **[CHANGELOG.md](CHANGELOG.md)** — version history.

## Project layout

```
lakskongen/
├── godot/             # Godot 4.7 project (scenes, scripts, shaders, assets, data)
├── src/               # C# simulation core + test projects (net10.0)
├── tests/             # Additional test projects (health, processing)
├── tools/             # ci.sh, token/texture/store-art generators, validators
├── docs/              # Documentation (see docs/INDEX.md)
├── design/            # Design tokens + art direction
└── LakskongenSim.sln  # Whole-solution build entry point
```

## License

**TBD** — to be determined before public release.
