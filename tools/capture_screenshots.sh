#!/usr/bin/env bash
#
# capture_screenshots.sh — launch the Godot screenshot harness NON-headless and
# report the captured PNG paths.
#
# A real display/session is REQUIRED. Godot's headless renderer produces no
# image data, so the harness refuses to run in headless mode.
#
# Usage:
#   tools/capture_screenshots.sh
#   GODOT_BIN=/path/to/Godot tools/capture_screenshots.sh
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
GODOT_DIR="$ROOT_DIR/godot"
HARNESS_SCENE="res://scenes/tools/ScreenshotHarness.tscn"
OUT_DIR="$ROOT_DIR/screenshots/game"
USER_OUT_DIR="$HOME/Library/Application Support/Godot/app_userdata/Lakskongen/screenshots"

DEFAULT_GODOT_MACOS="/Users/fredrik.j.hansen/Applications/Godot_mono.app/Contents/MacOS/Godot"

# ── Locate the Godot binary (GODOT_BIN override wins) ────────────────────────────
GODOT_BIN="${GODOT_BIN:-}"
if [[ -z "$GODOT_BIN" ]]; then
  if [[ -x "$DEFAULT_GODOT_MACOS" ]]; then
    GODOT_BIN="$DEFAULT_GODOT_MACOS"
  elif command -v godot >/dev/null 2>&1; then
    GODOT_BIN="$(command -v godot)"
  elif command -v godot4 >/dev/null 2>&1; then
    GODOT_BIN="$(command -v godot4)"
  else
    echo "ERROR: Godot binary not found." >&2
    echo "Set GODOT_BIN=/path/to/Godot to override." >&2
    exit 1
  fi
fi

if [[ ! -x "$GODOT_BIN" ]]; then
  echo "ERROR: GODOT_BIN is not executable: $GODOT_BIN" >&2
  exit 1
fi

# ── Display / session check ──────────────────────────────────────────────────────
# macOS uses WindowServer (no $DISPLAY); only Linux exposes DISPLAY/Wayland.
if [[ "$(uname -s)" == "Linux" ]]; then
  if [[ -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
    echo "ERROR: no DISPLAY/WAYLAND_DISPLAY — a rendering session is required." >&2
    exit 1
  fi
fi

mkdir -p "$OUT_DIR"

echo "Godot:   $GODOT_BIN"
echo "Project: $GODOT_DIR"
echo "Output:  $OUT_DIR"
echo "Scene:   $HARNESS_SCENE"
echo
echo "Launching non-headless screenshot harness (a display is required)..."
echo

# Do not let a non-zero Godot exit abort the report step.
set +e
"$GODOT_BIN" --path "$GODOT_DIR" "$HARNESS_SCENE"
status=$?
set -e

echo
if [[ $status -ne 0 ]]; then
  echo "WARNING: harness exited with status $status" >&2
fi

# ── Report ───────────────────────────────────────────────────────────────────────
shopt -s nullglob
shots=("$OUT_DIR"/*.png)
shopt -u nullglob

if [[ ${#shots[@]} -eq 0 ]]; then
  echo "ERROR: no screenshots found in $OUT_DIR" >&2
  echo "A display/session is required; headless cannot render." >&2
  exit 1
fi

echo "Captured ${#shots[@]} screenshot(s):"
for f in "${shots[@]}"; do
  echo "  $f"
done

echo
echo "user:// copy: $USER_OUT_DIR"
