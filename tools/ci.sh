#!/usr/bin/env bash
#
# Lakskongen CI quality gate.
#
# Fails fast: the first failing step prints the summary and exits non-zero.
# Every step is recorded so the summary always shows what ran.
#
# Environment:
#   GODOT_BIN   Path to the Godot 4 editor binary. Defaults to the standard
#               macOS install. If the binary is missing the GDScript parse
#               scan is skipped gracefully (never a hard failure).
#   PYTHON      Python interpreter to use. Defaults to python3.
#
# Usage:
#   bash tools/ci.sh
#
set -euo pipefail
set -E  # errtrace: propagate the ERR trap into shell functions

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Prefer an interpreter that actually has pytest (GitHub uses 3.12; local
# default python3 may be a newer brew install without it).
resolve_python() {
    if [[ -n "${PYTHON:-}" ]]; then printf '%s' "$PYTHON"; return 0; fi
    local c
    for c in python3.12 python3.11 python3; do
        if command -v "$c" >/dev/null 2>&1 && "$c" -m pytest --version >/dev/null 2>&1; then
            command -v "$c"; return 0
        fi
    done
    printf '%s' "python3"
}
PYTHON="$(resolve_python)"

# Resolve the Godot binary: explicit GODOT_BIN wins, else PATH, else common
# macOS install locations (incl. ~/Applications and the Homebrew cask wrapper).
resolve_godot_bin() {
    if [[ -n "${GODOT_BIN:-}" && -x "$GODOT_BIN" ]]; then printf '%s' "$GODOT_BIN"; return 0; fi
    local c
    for c in godot-mono godot Godot; do
        if command -v "$c" >/dev/null 2>&1; then command -v "$c"; return 0; fi
    done
    for c in \
        "/Applications/Godot.app/Contents/MacOS/Godot" \
        "/Applications/Godot_mono.app/Contents/MacOS/Godot" \
        "$HOME/Applications/Godot_mono.app/Contents/MacOS/Godot" \
        "$HOME/Applications/Godot.app/Contents/MacOS/Godot"; do
        [[ -x "$c" ]] && { printf '%s' "$c"; return 0; }
    done
    # Homebrew cask wrapper (version-agnostic glob).
    for c in /opt/homebrew/Caskroom/godot-mono/*/.homebrew-command-wrappers/godot-mono; do
        [[ -x "$c" ]] && { printf '%s' "$c"; return 0; }
    done
    printf '%s' "/Applications/Godot.app/Contents/MacOS/Godot"
}
GODOT_BIN="$(resolve_godot_bin)"

STEP_NAMES=()
STEP_RESULTS=()
CURRENT_STEP=""
STEP_STATUS=""

print_summary() {
    local i
    echo ""
    echo "=================== CI SUMMARY ==================="
    for i in "${!STEP_NAMES[@]}"; do
        printf '  %-4s  %s\n' "${STEP_RESULTS[$i]}" "${STEP_NAMES[$i]}"
    done
    if [[ ${#STEP_NAMES[@]} -eq 0 ]]; then
        echo "  (no steps completed)"
    fi
    echo "=================================================="
}

on_err() {
    local code=$?
    if [[ -n "$CURRENT_STEP" ]]; then
        STEP_RESULTS+=("FAIL")
    fi
    print_summary
    echo ""
    echo "CI FAILED at step: ${CURRENT_STEP:-<unknown>} (exit $code)"
    exit "$code"
}

trap on_err ERR

run_step() {
    local name="$1"
    shift
    CURRENT_STEP="$name"
    STEP_NAMES+=("$name")
    STEP_STATUS="PASS"
    echo "▶ $name"
    "$@"
    STEP_RESULTS+=("$STEP_STATUS")
    CURRENT_STEP=""
}

step_build() {
    dotnet build LakskongenSim.sln --nologo
}

step_build_godot() {
    # The Godot C# assembly (GameBridge, SaveLoadManager, ...) is NOT in
    # LakskongenSim.sln — build it explicitly so bridge breakage fails CI.
    # Godot.NET.Sdk comes from NuGet; no editor install required.
    dotnet build godot/LakskongenGame.csproj --nologo
}

step_test() {
    dotnet test LakskongenSim.sln --nologo
}

step_pytest() {
    if ! "$PYTHON" -m pytest --version >/dev/null 2>&1; then
        echo "   No Python with pytest found (tried python3.12, python3.11, python3)."
        echo "   Install the gate's Python packages: python3 -m pip install -r requirements-dev.txt"
        STEP_STATUS="FAIL"
        return 1
    fi
    "$PYTHON" -m pytest tests/ -q
}

step_benchmark() {
    # Program.cs returns non-zero when the perf gate exceeds its hard budget.
    dotnet run --project src/PerformanceBenchmark -c Release
}

step_tokens() {
    "$PYTHON" tools/sync_tokens.py --check
}

step_design_token_drift() {
    local script="$REPO_ROOT/.claude/skills/ecc/design-token-sync/design_token_sync/drift_check.py"
    if [[ ! -f "$script" ]]; then
        echo "   Design token drift check script not found at $script — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    local python_bin
    python_bin="$(command -v python3.12 || command -v python3.11 || command -v python3)"
    if [[ -z "$python_bin" ]]; then
        echo "   Python 3 not found — skipping design token drift check."
        STEP_STATUS="SKIP"
        return 0
    fi

    echo "   Running design token drift check..."
    if ! "$python_bin" "$script"; then
        echo "   Design token drift check failed."
        STEP_STATUS="FAIL"
        return 1
    fi

    echo "   Design token drift check passed."
}

step_validate_json() {
    local fail=0 count=0 f
    while IFS= read -r -d '' f; do
        count=$((count + 1))
        if ! "$PYTHON" -m json.tool "$f" >/dev/null 2>&1; then
            echo "   INVALID JSON: ${f#"$REPO_ROOT"/}"
            "$PYTHON" -m json.tool "$f" 2>&1 | head -5 || true
            fail=1
        fi
    done < <(find "$REPO_ROOT/godot/data" -maxdepth 1 -name '*.json' -print0; \
             find "$REPO_ROOT/godot/mods" -name 'manifest.json' -print0)
    echo "   Validated $count JSON file(s)."
    (( fail == 0 ))
}

# A clean checkout has no godot/.godot cache: class_name globals are unknown
# (parse errors on JuiceEffects & co.) and imported resources such as the
# translation files do not exist, so every later Godot step failed on a fresh
# clone (review F177). Importing first builds the cache; on an already
# imported working tree it is a quick no-op.
step_godot_import() {
    if [[ ! -x "$GODOT_BIN" ]]; then
        echo "   Godot not found at '$GODOT_BIN' — skipping import."
        STEP_STATUS="SKIP"
        return 0
    fi

    local project="$REPO_ROOT/godot"
    local log rc=0
    log="$(mktemp -t lakskongen-import.XXXXXX)"

    local -a timeout_cmd=()
    if command -v timeout >/dev/null 2>&1; then
        timeout_cmd=(timeout 900)
    elif command -v gtimeout >/dev/null 2>&1; then
        timeout_cmd=(gtimeout 900)
    fi

    # Same MCP token sandbox as the parse scan: the import boots the editor.
    local mcp_token_sandbox
    mcp_token_sandbox="$(mktemp -d -t lakskongen-mcp-token.XXXXXX)/mcp_token"
    "${timeout_cmd[@]}" env GODOT_MCP_TOKEN_PATH="$mcp_token_sandbox" "$GODOT_BIN" --headless --path "$project" \
        --import >"$log" 2>&1 || rc=$?

    if (( rc != 0 )); then
        echo "   Godot import failed (exit $rc):"
        tail -20 "$log"
        echo "   Full log: $log"
        STEP_STATUS="FAIL"
        return 1
    fi

    echo "   Project imported (class cache and imported resources ready)."
    rm -f "$log"
}

step_scene_validation() {
    if [[ ! -x "$GODOT_BIN" ]]; then
        echo "   Godot not found at '$GODOT_BIN' — skipping scene validation."
        STEP_STATUS="SKIP"
        return 0
    fi
    if [[ ! -f "$REPO_ROOT/tools/validate_scenes.sh" ]]; then
        echo "   tools/validate_scenes.sh missing — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi
    GODOT_BIN="$GODOT_BIN" bash "$REPO_ROOT/tools/validate_scenes.sh"
}

step_gdscript_scan() {
    if [[ ! -x "$GODOT_BIN" ]]; then
        echo "   Godot not found at '$GODOT_BIN' — skipping GDScript parse scan."
        echo "   Set GODOT_BIN=/path/to/Godot to enable it."
        STEP_STATUS="SKIP"
        return 0
    fi

    local project="$REPO_ROOT/godot"
    local log rc=0
    log="$(mktemp -t lakskongen-godot.XXXXXX)"

    local -a timeout_cmd=()
    if command -v timeout >/dev/null 2>&1; then
        timeout_cmd=(timeout 300)
    elif command -v gtimeout >/dev/null 2>&1; then
        timeout_cmd=(gtimeout 300)
    fi

    echo "   Using Godot: $GODOT_BIN"
    # Sandbox the MCP session token: --editor boots the toolkit plugin, which
    # would otherwise rotate the live editor's shared token (see
    # docs/research/MCP_CAPABILITIES.md §3). The scan itself needs no token.
    local mcp_token_sandbox
    mcp_token_sandbox="$(mktemp -d -t lakskongen-mcp-token.XXXXXX)/mcp_token"
    "${timeout_cmd[@]}" env GODOT_MCP_TOKEN_PATH="$mcp_token_sandbox" "$GODOT_BIN" --headless --path "$project" \
        --editor --quit-after 3 >"$log" 2>&1 || rc=$?

    if grep -qiE "parse error|script error" "$log"; then
        echo "   GDScript parse errors detected:"
        grep -iE "parse error|script error" "$log" | head -40
        echo "   Full log: $log"
        STEP_STATUS="FAIL"
        return 1
    fi

    if (( rc != 0 )); then
        echo "   Godot exited with code $rc; parse scan did not complete cleanly."
        tail -20 "$log"
        echo "   Full log: $log"
        STEP_STATUS="FAIL"
        return 1
    fi

    echo "   GDScript parse scan clean."
    rm -f "$log"
}

step_ui_flow_tests() {
    if [[ ! -x "$GODOT_BIN" ]]; then
        echo "   Godot not found at '$GODOT_BIN' — skipping UI flow tests."
        STEP_STATUS="SKIP"
        return 0
    fi
    GODOT_BIN="$GODOT_BIN" bash "$REPO_ROOT/tools/run_ui_flow_tests.sh"
}

step_verify_assets() {
    local script="$REPO_ROOT/.claude/skills/ecc/blender-asset-pipeline/blender_asset_pipeline.py"
    if [[ ! -f "$script" ]]; then
        echo "   Blender asset pipeline script not found at $script — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    local python_bin
    python_bin="$(command -v python3.12 || command -v python3.11 || command -v python3)"
    if [[ -z "$python_bin" ]]; then
        echo "   Python 3 not found — skipping asset verification."
        STEP_STATUS="SKIP"
        return 0
    fi

    echo "   Verifying exported assets against manifest..."
    if ! "$python_bin" "$script" --verify-only; then
        echo "   Asset verification failed."
        STEP_STATUS="FAIL"
        return 1
    fi

    echo "   Asset verification passed."
}

step_godot_mcp_scene_diff() {
    local script="$REPO_ROOT/.claude/skills/ecc/godot-mcp-ops/godot_mcp_ops.py"
    if [[ ! -f "$script" ]]; then
        echo "   Godot MCP ops script not found at $script — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    local python_bin
    python_bin="$(command -v python3.12 || command -v python3.11 || command -v python3)"
    if [[ -z "$python_bin" ]]; then
        echo "   Python 3 not found — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    # Scene diff requires a base and PR scene; in CI we compare against HEAD
    # For now, skip if no obvious comparison target
    echo "   Scene diff: requires explicit base/PR scenes (run manually with --base/--pr)"
    STEP_STATUS="SKIP"
    return 0
}

# The three godot-mcp-ops steps run with --no-mcp: by default the tool asks a
# running editor's MCP bridge (ws://localhost:6550) instead, so the result
# depended on whichever project that editor had open, not on this checkout.
step_godot_mcp_autoload_audit() {
    local script="$REPO_ROOT/.claude/skills/ecc/godot-mcp-ops/godot_mcp_ops.py"
    if [[ ! -f "$script" ]]; then
        echo "   Godot MCP ops script not found at $script — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    local python_bin
    python_bin="$(command -v python3.12 || command -v python3.11 || command -v python3)"
    if [[ -z "$python_bin" ]]; then
        echo "   Python 3 not found — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    echo "   Running autoload audit..."
    if ! GODOT_BIN="$GODOT_BIN" "$python_bin" "$script" --no-mcp autoload-audit; then
        echo "   Autoload audit failed."
        STEP_STATUS="FAIL"
        return 1
    fi

    echo "   Autoload audit passed."
}

step_godot_mcp_shader_check() {
    local script="$REPO_ROOT/.claude/skills/ecc/godot-mcp-ops/godot_mcp_ops.py"
    if [[ ! -f "$script" ]]; then
        echo "   Godot MCP ops script not found at $script — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    local python_bin
    python_bin="$(command -v python3.12 || command -v python3.11 || command -v python3)"
    if [[ -z "$python_bin" ]]; then
        echo "   Python 3 not found — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    echo "   Running shader compilation check..."
    if ! GODOT_BIN="$GODOT_BIN" "$python_bin" "$script" --no-mcp shader-check; then
        echo "   Shader check failed."
        STEP_STATUS="FAIL"
        return 1
    fi

    echo "   Shader check passed."
}

step_godot_mcp_export_verify() {
    local script="$REPO_ROOT/.claude/skills/ecc/godot-mcp-ops/godot_mcp_ops.py"
    if [[ ! -f "$script" ]]; then
        echo "   Godot MCP ops script not found at $script — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    local python_bin
    python_bin="$(command -v python3.12 || command -v python3.11 || command -v python3)"
    if [[ -z "$python_bin" ]]; then
        echo "   Python 3 not found — skipping."
        STEP_STATUS="SKIP"
        return 0
    fi

    if [[ ! -x "$GODOT_BIN" ]]; then
        echo "   Godot not found at '$GODOT_BIN' — skipping export verify."
        STEP_STATUS="SKIP"
        return 0
    fi

    echo "   Running export verification..."
    # Sandbox the MCP session token for this headless-editor export: an export
    # boots a headless editor which would otherwise overwrite the live editor's
    # published token (same per-project user:// path) and break its MCP bridge
    # until relaunch. The exported game never needs the live token, so point it
    # at a throwaway file. (Registry entry churn is handled post-CI by
    # tools/mcp_bridge_alias.py --repair.)
    local mcp_token_sandbox
    mcp_token_sandbox="$(mktemp -d -t lakskongen-mcp-token.XXXXXX)/mcp_token"
    if ! GODOT_BIN="$GODOT_BIN" GODOT_MCP_TOKEN_PATH="$mcp_token_sandbox" "$python_bin" "$script" --no-mcp export-verify; then
        echo "   Export verification failed."
        STEP_STATUS="FAIL"
        return 1
    fi

    echo "   Export verification passed."
}

step_validate_mods() {
    "$PYTHON" "$REPO_ROOT/tools/validate_mods.py"
}

echo "Lakskongen CI quality gate — repo: $REPO_ROOT"
echo ""

run_step "dotnet build LakskongenSim.sln"              step_build
run_step "dotnet build godot/LakskongenGame.csproj"    step_build_godot
run_step "dotnet test LakskongenSim.sln"               step_test
run_step "pytest tests/ (bridge contract)"             step_pytest
run_step "Performance benchmark (perf gate)"           step_benchmark
run_step "Design token sync check"                     step_tokens
run_step "Design token drift check"                    step_design_token_drift
run_step "Validate JSON data + mod manifests"          step_validate_json
run_step "Godot import (class cache + resources)"      step_godot_import
run_step "Scene load validation"                       step_scene_validation
run_step "Headless GDScript parse scan"                step_gdscript_scan
run_step "Godot UI flow tests (headless)"              step_ui_flow_tests
run_step "Blender asset pipeline verification"         step_verify_assets
run_step "Godot MCP: Autoload audit"                   step_godot_mcp_autoload_audit
run_step "Godot MCP: Shader check"                     step_godot_mcp_shader_check
run_step "Godot MCP: Export verify"                    step_godot_mcp_export_verify
run_step "Mod validation + cross-reference checks"     step_validate_mods

CURRENT_STEP=""
print_summary
echo ""
echo "CI PASSED"
