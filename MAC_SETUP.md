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
dotnet test LakskongenSim.sln           # 351 passed, 0 failed (via roll-forward)
```

Individual suites: `tests/*.csproj` and `src/*/*.Tests/*.csproj` build and
pass the same way (ProcessingTests 46, LogisticsTests is an Exe, not tests).

Proper fix for CI/dev machines: install the .NET 8 runtime side-by-side
instead of roll-forward, or retarget test projects to net10.0.

## Godot project

- Current project: `godot/` (Godot 4.3, C#, Forward Plus).
- Legacy project: `game/` (Godot 3 era, `config_version=4`) — not maintained.
- Headless import check: `godot-mono --headless --path godot --import`
- Headless boot smoke: `godot-mono --headless --path godot --quit-after 600`
- NOTE: `--import` on 4.7 writes `.uid` files and touches the `.csproj`;
  run `git checkout -- godot/LakskongenGame.csproj && git clean -fdq godot/`
  afterwards to keep the tree clean.

## Known environment-independent breakage (2026-10-05)

- ~81 `.gd` files fail to parse (stray column-0 `tooltip_text` lines + other
  defects) — the game cannot boot. See review findings, not setup.
- `godot/LakskongenGame.sln` does not compile (7× CS0266 in GameBridge.cs).
