#!/usr/bin/env bash
#
# perf_smoke.sh — P0-04 performance smoke test (roadmap §4).
#
# Runs the PerfHarness scene (godot/scripts/tools/PerfHarness.gd) NON-headless
# at 2560x1440: repeatable camera paths at FARM, FJORD and COAST plus one run
# behind an opaque full-screen panel, 10 s each, vsync off. Writes
# perf_<label>.json and perf_<label>.md, then judges the frame budget
# (p95 <= 16.7 ms at FARM, FJORD and COAST) with tools/perf_report.py.
#
# The gate is warn-only until the Phase 1 exit: an over-budget segment prints
# PERF_GATE WARN and the script exits 0. PERF_GATE_STRICT=1 makes it fail.
# Measure on a quiet machine: other GPU work (another Godot, Blender) slows
# every frame. Compare variants inside one run (--variants, --paired), not
# across runs.
#
# Usage:
#   tools/perf_smoke.sh                     # High (the default tier), 10 s per segment
#   tools/perf_smoke.sh --variants="ultra;high" --repeats=3 --paired
#   PERF_OUT_DIR=docs/run/evidence/perf PERF_LABEL=baseline tools/perf_smoke.sh
#
# Environment:
#   GODOT_BIN          Godot 4 mono binary (auto-detected otherwise)
#   PERF_OUT_DIR       output directory (default: a temporary directory)
#   PERF_LABEL         file label (default: smoke)
#   PERF_RESOLUTION    window size (default: 2560x1440)
#   PERF_SECONDS       recorded seconds per segment (default: 10)
#   PERF_GATE_STRICT   1 = an over-budget segment fails (exit 1)
#
# Exit: 0 measured (warn-only gate), 1 strict gate failed, 3 could not measure
# (no display, no Godot, the harness failed).
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/godot"
LABEL="${PERF_LABEL:-smoke}"
RESOLUTION="${PERF_RESOLUTION:-2560x1440}"
SECONDS_PER_SEGMENT="${PERF_SECONDS:-10}"
OUT_DIR="${PERF_OUT_DIR:-}"
if [[ -z "$OUT_DIR" ]]; then
    OUT_DIR="$(mktemp -d -t lakskongen-perf.XXXXXX)"
fi
mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

PYTHON=""
for candidate in python3.12 python3.11 python3; do
    if command -v "$candidate" >/dev/null 2>&1; then
        PYTHON="$(command -v "$candidate")"; break
    fi
done

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
        "$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot"; do
        if [[ -x "$candidate" ]]; then
            printf '%s\n' "$candidate"; return 0
        fi
    done
    return 1
}

GODOT="$(find_godot)" || { echo "PERF_SMOKE SKIP: Godot binary not found (set GODOT_BIN)."; exit 3; }
if [[ -z "$PYTHON" ]]; then
    echo "PERF_SMOKE SKIP: no python3 for the report."; exit 3
fi
if [[ "$(uname -s)" == "Linux" && -z "${DISPLAY:-}" && -z "${WAYLAND_DISPLAY:-}" ]]; then
    echo "PERF_SMOKE SKIP: no display (the harness renders; headless cannot)."; exit 3
fi

LOG="$OUT_DIR/perf_$LABEL.log"
mcp_token_sandbox="$(mktemp -d -t lakskongen-mcp-token.XXXXXX)/mcp_token"
echo "Perf smoke: $GODOT at $RESOLUTION, $SECONDS_PER_SEGMENT s per segment -> $OUT_DIR"
perl -e 'alarm shift; exec @ARGV' 900 \
    env GODOT_MCP_TOKEN_PATH="$mcp_token_sandbox" \
    "$GODOT" --path "$PROJECT" --windowed --resolution "$RESOLUTION" \
    res://scenes/tools/PerfHarness.tscn -- \
    --out="$OUT_DIR" --label="$LABEL" --seconds="$SECONDS_PER_SEGMENT" "$@" >"$LOG" 2>&1
rc=$?
grep -E "^PERF_" "$LOG"
JSON="$OUT_DIR/perf_$LABEL.json"
if (( rc != 0 )) || [[ ! -f "$JSON" ]]; then
    echo "PERF_SMOKE SKIP: the harness did not measure (exit $rc); log: $LOG"
    tail -20 "$LOG"
    exit 3
fi

strict=()
if [[ "${PERF_GATE_STRICT:-0}" == "1" ]]; then
    strict=(--strict)
fi
"$PYTHON" "$REPO_ROOT/tools/perf_report.py" "$JSON" --markdown "$OUT_DIR/perf_$LABEL.md" ${strict[@]+"${strict[@]}"}
