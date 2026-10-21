#!/usr/bin/env bash
# One PlaytestHarness session (docs/SHIP_PLAN.md §4) in an ISOLATED Godot user
# dir, so a playtest never reads or writes the player's saves, settings,
# language or campaign progress.
#
#   bash tools/playtest.sh <acts> <evidence_dir> [harness args...]
#   bash tools/playtest.sh res://qa/playtests/smoke.json /abs/out --w=1280 --h=720
#
# How: a temporary godot/override.cfg points Godot at a custom user dir named
# LakskongenPlaytest (wiped first, so every session starts like a fresh
# install), and is removed on any exit. The harness itself refuses to run in
# any other user dir. Exit code: the harness's (0 = ran, findings in the
# summary; 1 = the harness could not run).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ACTS="${1:?usage: playtest.sh <acts res://...json> <abs evidence dir> [args]}"
OUT="${2:?usage: playtest.sh <acts res://...json> <abs evidence dir> [args]}"
shift 2
USER_DIR_NAME="LakskongenPlaytest"
OVR="$ROOT/godot/override.cfg"

if [[ -e "$OVR" ]]; then
    echo "playtest: refusing to run: $OVR already exists and is not this script's" >&2
    exit 1
fi
cleanup() { rm -f "$OVR"; }
trap cleanup EXIT INT TERM

case "$(uname -s)" in
    Darwin) DATA_DIR="$HOME/Library/Application Support/$USER_DIR_NAME" ;;
    *)      DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/$USER_DIR_NAME" ;;
esac
# Fresh install every session. Guarded: only ever this exact folder name.
if [[ "$(basename "$DATA_DIR")" == "$USER_DIR_NAME" && -d "$DATA_DIR" ]]; then
    rm -rf "$DATA_DIR"
fi

# Silent: no game audio from a playtest (owner rule, 2026-10-21: "do not play
# sound when you debug"). Belt and braces: the override and the CLI flag.
printf '[application]\n\nconfig/use_custom_user_dir=true\nconfig/custom_user_dir_name="%s"\n\n[audio]\n\ndriver/driver="Dummy"\n' \
    "$USER_DIR_NAME" > "$OVR"
mkdir -p "$OUT"

env GODOT_MCP_TOKEN_PATH="$(mktemp -d)/t" perl -e 'alarm shift; exec @ARGV' 2400 \
    godot-mono --audio-driver Dummy --path "$ROOT/godot" res://qa/scenes/PlaytestSession.tscn -- \
    --acts="$ACTS" --out="$OUT" "$@"
