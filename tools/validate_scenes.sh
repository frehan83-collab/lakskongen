#!/usr/bin/env bash
#
# Lakskongen headless scene-load validator.
#
# Walks every .tscn under the given project roots (default: the whole project,
# skipping res://addons), loads each PackedScene, and fails non-zero when a
# scene cannot load or references a missing script/resource.
#
# Environment:
#   GODOT_BIN   Path to the Godot 4 editor/mono binary. Auto-detected from
#               PATH (godot-mono, godot, Godot) or /Applications.
#
# Usage:
#   tools/validate_scenes.sh                 # validate the whole project
#   tools/validate_scenes.sh res://scenes    # validate one subtree
#
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/godot"

find_godot() {
    if [[ -n "${GODOT_BIN:-}" && -x "$GODOT_BIN" ]]; then
        printf '%s\n' "$GODOT_BIN"; return 0
    fi
    local candidate
    for candidate in godot-mono godot Godot; do
        if command -v "$candidate" >/dev/null 2>&1; then
            command -v "$candidate"; return 0
        fi
    done
    for candidate in \
        "/Applications/Godot_mono.app/Contents/MacOS/Godot" \
        "/Applications/Godot.app/Contents/MacOS/Godot" \
        "/Applications/Godot-mono.app/Contents/MacOS/Godot"; do
        if [[ -x "$candidate" ]]; then
            printf '%s\n' "$candidate"; return 0
        fi
    done
    return 1
}

GODOT_BIN="$(find_godot)" || {
    echo "ERROR: Godot binary not found. Set GODOT_BIN=/path/to/Godot." >&2
    exit 2
}

if [[ ! -f "$PROJECT/project.godot" ]]; then
    echo "ERROR: Godot project not found at $PROJECT" >&2
    exit 2
fi

LOG="$(mktemp -t lakskongen-scenes.XXXXXX)"
trap 'rm -f "$LOG"' EXIT

echo "Scene validator using: $GODOT_BIN"
echo "Project: $PROJECT"
echo ""

"$GODOT_BIN" --headless --path "$PROJECT" \
    --script res://scripts/tools/SceneValidator.gd -- "$@" 2>&1 | tee "$LOG"
godot_rc="${PIPESTATUS[0]}"

# Engine-level errors that GDScript cannot observe (printed to stderr by the
# C++ resource loader) are treated as validation failures too.
engine_fail=0
if grep -qiE "Parse Error|non-existent resource|failed to load (script|resource)|_create_mesh_array must return" "$LOG"; then
    engine_fail=1
fi

if grep -q "SCENE_VALIDATION_RESULT failures=" "$LOG"; then
    declared="$(grep -o "SCENE_VALIDATION_RESULT failures=[0-9]*" "$LOG" | tail -1)"
    if [[ "$declared" != "SCENE_VALIDATION_RESULT failures=0" ]]; then
        engine_fail=1
    fi
fi

echo ""
if (( engine_fail != 0 || godot_rc != 0 )); then
    echo "SCENE VALIDATION: FAILED"
    exit 1
fi
echo "SCENE VALIDATION: PASSED"
exit 0
