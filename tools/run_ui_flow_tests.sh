#!/usr/bin/env bash
#
# Lakskongen headless UI flow tests (godot/tests/UiFlowTests.tscn).
#
# Boots the real project headless (autoloads included), drives menus and the
# save/load flow through button signals, and fails non-zero when:
#   - the runner reports any failed check, or fewer than MIN_CHECKS checks;
#   - GDScript raises a script/parse error while the flows run;
#   - Godot exits non-zero or does not finish within the timeout.
#
# Requires the Godot C# assembly to be built first
# (dotnet build godot/LakskongenGame.csproj — CI step 2).
#
# Environment:
#   GODOT_BIN   Path to the Godot 4 mono binary (auto-detected otherwise).
#
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/godot"
TIMEOUT_SECS=300
# The exact number of checks the suite makes: fewer means tests stopped
# running (an early return, a call dropped from _run()). Raise it with every
# check you add; lowering it weakens the gate and needs a dispute entry in
# docs/RUN_LOG.md §8.
MIN_CHECKS=269

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
        "$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot"; do
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

LOG="$(mktemp -t lakskongen-uiflow.XXXXXX)"
trap 'rm -f "$LOG"' EXIT

echo "UI flow tests using: $GODOT_BIN"
# perl alarm = portable timeout (macOS ships without coreutils `timeout`).
# The MCP token is sandboxed like the other headless steps in tools/ci.sh.
mcp_token_sandbox="$(mktemp -d -t lakskongen-mcp-token.XXXXXX)/mcp_token"
perl -e 'alarm shift; exec @ARGV' "$TIMEOUT_SECS" \
    env GODOT_MCP_TOKEN_PATH="$mcp_token_sandbox" \
    "$GODOT_BIN" --headless --path "$PROJECT" res://tests/UiFlowTests.tscn >"$LOG" 2>&1
rc=$?

grep -E "^  (ok|FAIL) " "$LOG"
result="$(grep -o "UI_FLOW_RESULT checks=[0-9]* failures=[0-9]*" "$LOG" | tail -1)"
echo "${result:-UI_FLOW_RESULT missing}"

checks="${result#UI_FLOW_RESULT checks=}"
checks="${checks%% *}"

fail=0
if [[ -z "$result" ]]; then
    echo "FAIL: runner did not report a result (crash or timeout, rc=$rc)."
    fail=1
elif (( checks < MIN_CHECKS )); then
    echo "FAIL: only $checks checks ran, expected at least $MIN_CHECKS (did tests stop running?)."
    fail=1
elif [[ "$result" != *" failures=0" ]]; then
    fail=1
fi
if grep -qE "SCRIPT ERROR|Parse Error" "$LOG"; then
    echo "FAIL: GDScript errors while the flows ran:"
    grep -E -A3 "SCRIPT ERROR|Parse Error" "$LOG" | head -40
    fail=1
fi
if (( rc != 0 )); then
    echo "FAIL: Godot exited with code $rc."
    fail=1
fi

if (( fail )); then
    tail -30 "$LOG"
    echo "UI FLOW TESTS: FAILED"
    exit 1
fi
echo "UI FLOW TESTS: PASSED"
