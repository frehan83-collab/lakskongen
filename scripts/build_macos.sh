#!/usr/bin/env bash
# Build a testable macOS Lakskongen.app: export, sign, ship data files.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/build/macos/Lakskongen.app"
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_ROLL_FORWARD=LatestMajor

rm -rf "$APP"
godot-mono --headless --path "$ROOT/godot" --export-release "macOS" "$APP" 2>&1 | grep -iE "^ERROR" | head -5
[ -d "$APP" ] || { echo "EXPORT FAILED"; exit 1; }

# C# System.IO cannot read inside the .pck — ship JSON configs as bundle files.
mkdir -p "$APP/Contents/Resources/data"
cp "$ROOT/godot/data/"*.json "$APP/Contents/Resources/data/"

codesign --force --deep --sign - "$APP" 2>/dev/null
echo "BUILT: $APP ($(du -sh "$APP" | cut -f1))"
echo "Launch: open \"$APP\"   (right-click > Open on first run if Gatekeeper complains)"
