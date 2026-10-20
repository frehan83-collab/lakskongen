# CI Quality Gate

The quality gate is implemented once in [`tools/ci.sh`](../tools/ci.sh) and
invoked both locally and from [`.github/workflows/ci.yml`](../.github/workflows/ci.yml).
It fails fast: the first failing step prints a summary and exits non-zero.

## Steps

| # | Step | Command | Protects against |
|---|------|---------|------------------|
| 1 | Build | `dotnet build LakskongenSim.sln` | Compile errors, broken project/solution references, missing types |
| 2 | Tests | `dotnet test LakskongenSim.sln` | Regressions in simulation, save/load, health, processing and logistics |
| 3 | Performance | `dotnet run --project src/PerformanceBenchmark -c Release` | Simulation slowdowns; the benchmark returns non-zero when the perf gate exceeds its hard budget (5 ms/day) |
| 4 | Token sync | `python3 tools/sync_tokens.py --check` | UI theme drift between `design/tokens.json` and `godot/theme/tokens.json` |
| 5 | JSON validation | `python3 -m json.tool` over `godot/data/*.json` and `godot/mods/**/manifest.json` | Malformed data or mod manifests that would crash the game/loader |
| 6 | Scene load validation | `tools/validate_scenes.sh` (only if Godot is present) | `.tscn` files that fail to load, dangling `[ext_resource]` paths, invalid meshes |
| 7 | GDScript parse scan | `godot --headless --editor --quit-after 3` (only if Godot is present) | GDScript parse/syntax errors across the project |

## Running locally

```bash
bash tools/ci.sh
```

The script changes into the repository root itself, so it can be run from
anywhere. It always prints a per-step summary and exits `0` only when every
step passed.

### Environment variables

| Variable | Default | Purpose |
|----------|---------|---------|
| `GODOT_BIN` | `/Applications/Godot.app/Contents/MacOS/Godot` | Path to the Godot 4 editor binary used for steps 6 and 7. |
| `PYTHON` | `python3` | Python interpreter used for steps 4 and 5. |

If `GODOT_BIN` does not point at an executable, steps 6 and 7 are reported as
`SKIP` and the gate still passes — Godot is optional. To run the scene/parse
checks locally against a non-default install:

```bash
GODOT_BIN="/Applications/Godot_mono.app/Contents/MacOS/Godot" bash tools/ci.sh
```

## Continuous integration

`.github/workflows/ci.yml` runs the same script on GitHub Actions (Ubuntu) for
every push and pull request:

1. Checkout
2. `actions/setup-dotnet` with .NET 10
3. `actions/setup-python` with Python 3.12
4. `bash tools/ci.sh`

Godot is not installed on the runner, so steps 6 and 7 are skipped there. To
enable them in CI, download a Godot build and export `GODOT_BIN` before the gate
step.

## Fixing failures

- **Build/test failures** — reproduce with `dotnet build` / `dotnet test` and
  fix the reported project.
- **Performance gate** — inspect the printed per-day timings. The gate passes
  above the soft target (2 ms/day) with a warning and only fails above the hard
  limit (5 ms/day) to avoid false failures on shared runners.
- **Token drift** — run `python3 tools/sync_tokens.py --write` and commit the
  updated `godot/theme/tokens.json`.
- **Invalid JSON** — the offending file and a parser message are printed; fix
  the syntax (trailing commas and comments are not valid JSON).
- **Scene validation failures** — the validator prints the failing `.tscn` and
  the reason (missing `[ext_resource]`, invalid mesh, load/instantiate error);
  fix the scene or restore the referenced file. See
  [SCENE_VALIDATION.md](SCENE_VALIDATION.md).
- **GDScript parse errors** — run Godot locally with `GODOT_BIN` set and fix
  the scripts reported in the log.
