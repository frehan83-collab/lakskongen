# Lakskongen — macOS (Apple Silicon) setup

Validated 2026-10-05 on an M-series Mac. No sudo needed except where noted.

## Toolchain

```zsh
# .NET 10 SDK (formula; SDK 10 builds net8.0 targets — see notes)
brew install dotnet
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"

# Godot 4.7.2 with C# support, installed user-local (no sudo).
# --skip-cask-deps avoids the sudo-requiring dotnet-sdk cask;
# the brew dotnet formula above satisfies it.
mkdir -p ~/Applications
brew install --cask --appdir="$HOME/Applications" --skip-cask-deps godot-mono
```

`godot-mono` CLI is linked to `/opt/homebrew/bin/godot-mono`.

## Build & test

```zsh
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_ROLL_FORWARD=LatestMajor   # tests target net8.0; runs on the 10 runtime

dotnet build LakskongenSim.sln          # 0 errors (2449 warnings, mostly XML-doc)
dotnet test LakskongenSim.sln           # all green (main suite 290 incl. 6 new
                                        # cash/save round-trip tests; satellites 20+46+46+46)
python3 -m pytest tests/ -q            # 21 passed, 1 skipped (design + bridge + save guards)
```

Individual suites: `tests/*.csproj` and `src/*/*.Tests/*.csproj` build and
pass the same way (ProcessingTests 46, LogisticsTests is an Exe, not tests).

Proper fix for CI/dev machines: install the .NET 8 runtime side-by-side
instead of roll-forward, or retarget test projects to net10.0.

## Godot project

- Current project: `godot/` (Godot **4.7**, C#, Forward Plus — migrated from
  4.3 on 2026-10-05; `project.godot` features tag + `Godot.NET.Sdk/4.7.2`
  pin this. All verification runs on 4.7.2.)
- Legacy project: `game/` (Godot 3 era, `config_version=4`) — not maintained.
- Headless import check: `godot-mono --headless --path godot --import`
- Headless boot smoke: `godot-mono --headless --path godot --quit-after 600`
- `.uid` files (130, Godot 4.4+ resource IDs) are TRACKED in git — commit them
  with their scripts. Never `git clean` them away.
- SAFE post-import cleanup (import touches only generated files):
  `git checkout -- godot/LakskongenGame.csproj` then
  `git status --porcelain godot/` and revert generated files individually.
  NEVER run broad `git clean -fdq godot/` — it once deleted an untracked
  source file (ProductionGoal.cs).

## Status (2026-10-05, post repair passes)

- Both `.sln` build with 0 errors; full test suites green (see above).
- GDScript parse failures: 0/118. Boot smoke: clean.
- Remaining work: empire-restore edge cases (competitor roster, phase
  unlocks), onboarding slice, legacy `game/` archival decision.
