#!/usr/bin/env bash
# showroom.sh — open the showroom (godot/qa/scenes/Showroom.tscn) windowed: the
# real game world with every farm type and the new models standing in it,
# to look around in. Not a test: it stays open until you close the window.
#
# Like tools/playtest.sh it runs in an ISOLATED Godot user dir (a temporary
# godot/override.cfg), so the player's saves and settings are never touched.
#
# Usage (from the repo root or anywhere):
#   tools/showroom.sh                 # the showroom
#   tools/showroom.sh --game          # the game itself, from the main menu
#   GODOT_BIN=/path/to/Godot tools/showroom.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SCENE="res://qa/scenes/Showroom.tscn"
if [[ "${1:-}" == "--game" ]]; then
    SCENE="res://scenes/MainMenu.tscn"
fi

GODOT_BIN="${GODOT_BIN:-}"
if [[ -z "$GODOT_BIN" ]]; then
    for candidate in godot-mono godot godot4; do
        if command -v "$candidate" >/dev/null 2>&1; then GODOT_BIN="$(command -v "$candidate")"; break; fi
    done
fi
if [[ -z "$GODOT_BIN" ]]; then
    for candidate in "$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot" "/Applications/Godot_mono.app/Contents/MacOS/Godot"; do
        if [[ -x "$candidate" ]]; then GODOT_BIN="$candidate"; break; fi
    done
fi
if [[ -z "$GODOT_BIN" ]]; then
    echo "showroom: Godot not found (set GODOT_BIN)" >&2
    exit 1
fi

USER_DIR_NAME="LakskongenShowroom"
OVR="$ROOT/godot/override.cfg"
# A run killed before its trap leaves its own override behind: drop that one
# (it names this script's user dir); refuse any other.
if [[ -e "$OVR" ]] && grep -q "custom_user_dir_name=\"$USER_DIR_NAME\"" "$OVR"; then
    rm -f "$OVR"
fi
if [[ -e "$OVR" ]]; then
    echo "showroom: refusing to run: $OVR already exists and is not this script's" >&2
    exit 1
fi
cleanup() { rm -f "$OVR"; }
trap cleanup EXIT INT TERM
printf '[application]\n\nconfig/use_custom_user_dir=true\nconfig/custom_user_dir_name="%s"\n' "$USER_DIR_NAME" > "$OVR"

# The C# assemblies must be current (a fresh checkout has none).
(cd "$ROOT/godot" && dotnet build -v q -nologo >/dev/null)
# A fresh checkout has no imported assets: import once, headless.
if [[ ! -d "$ROOT/godot/.godot/imported" ]]; then
    echo "showroom: importing assets (first run only) ..."
    GODOT_MCP_TOKEN_PATH="$(mktemp -d)/t" "$GODOT_BIN" --headless --path "$ROOT/godot" --import >/dev/null 2>&1 || true
fi

"$GODOT_BIN" --path "$ROOT/godot" "$SCENE" ${SHOWROOM_ARGS:+-- $SHOWROOM_ARGS}
