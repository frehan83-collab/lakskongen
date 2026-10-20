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
# A hang guard, not a speed gate: the wave-3 integration's suite (1425 checks)
# runs 4:51 on a loaded M1 Pro (load 6), against 300 s before; U2-02 (1469
# checks, the first season played by input) ran 5:39 at load 10-25 beside
# another worktree's suite.
TIMEOUT_SECS=720
# The exact number of checks the suite makes: fewer means tests stopped
# running (an early return, a call dropped from _run()). Raise it with every
# check you add; lowering it weakens the gate and needs a dispute entry in
# docs/RUN_LOG.md §8.
MIN_CHECKS=1791  # wave4b/int after gfx-polish-1: + 13 (GfxPolishChecks.gd: harness arguments 2, Beaufort-0 ripples, the menu hero sky, the scenario, the terrain CUSTOM0, pen calm 2, the shoal LOD, chips 3, the sun keeps its light). wave4b/int after gfx-farm-1: + 47 (F2-20 _test_facilities_render: the stage's 33, the presenter drawing the engine's lots, tanks, construction phases and offshore units, the anchored card, the camera framings; + 14 from the review: FacilityLayoutChecks.gd 8 (every pad and lot count clear, the bridge between basin and hall, the clash found), the kit read back once and the pads computed once, every drawn kit mesh live on the rendering server, a second Esc leaves the tab, the land card's waiting line, the card hidden behind the camera and back). wave4b/int after ui-core-2: + 21 (SettingsAudioChecks.gd, the U2-08/Au2-01 review: the HC HUD glass, primary text and restore; the sea state and the underwater bed; a silent storm warning with notifications off; the slot's time_local; save-and-leave and save-and-quit wait for the picture, a picture of a world that left is given up; a fresh autosave interval per game; the end screen offers only this game's autosave; a volume change leaves the window alone; by-path runs off the player's settings). wave4b/ui-core-2: + 73 (tests/SettingsAudioChecks.gd: U2-08 every persisted setting through its consumer, the menu, a language change redrawing the menus, save slots with picture, scenario and ISO time; Au2-01 every button clicks, the verb cues once, the ambience bed). wave4/int: ui-farm-1's 1549 + sim-land-2's 18 (the land boards' bridge verbs) + sim-sea-2's 9 (OffshoreBridgeChecks.gd: F2-08 handling and incidents, F2-09 round and licences) + 17 (BuildFlowChecks.gd: the Hav confirm files the development-licence application, its lint, and the award) + ui-core-1's 44 (U2-02 FirstSeasonFlowChecks: the card, the guide off, D19 in three scenarios, the skip, the season by input with the loan). wave4/ui-farm-1: F2-31 + 87 (tests/BuildFlowChecks.gd 86, U2-01 expansion test +1: its two placeholder checks replaced by three stronger ones). Merge of sim-land-1 (+10, the land lane's bridge verbs) and sim-sea-1 (+27, tests/OffshoreBridgeChecks.gd) on wave 3's 1425. wave-3 integration: wave 2's 1167 + sim-core-4's 37 + ui-a-2's 72 + ui-b-2's 148 (1065 -> 1213) + 1 (the end screen gives back the toasts and the clock); sim-lanes-2 adds none

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
