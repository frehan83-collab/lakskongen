#!/usr/bin/env bash
# Lakskongen commit-time verification: builds, tests, parse scan, boot smoke.
# Godot-dependent steps skip gracefully when godot-mono is absent (CI installs it).
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
FAIL=0

say() { printf '\n### %s\n' "$1"; }
fail() { echo "VERIFY FAILED: $1"; FAIL=1; }

export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_ROLL_FORWARD=LatestMajor

say "dotnet build (sim)"
dotnet build LakskongenSim.sln --nologo -v q 2>&1 | grep -E " error " && fail "sim build errors"

say "dotnet build (game)"
dotnet build godot/LakskongenGame.sln --nologo -v q 2>&1 | grep -E " error " && fail "game build errors"

say "dotnet test"
dotnet test LakskongenSim.sln --no-build --nologo -v q 2>&1 | grep -E "Failed: +[1-9]" && fail "dotnet test failures"

say "pytest (design + contract + compat)"
if command -v python3 >/dev/null; then
  python3 -m pytest tests/ -q 2>&1 | tail -2
  python3 -m pytest tests/ -q 2>&1 | grep -qE "[1-9]+ failed" && fail "pytest failures"
else
  echo "SKIP: no python3"
fi

if command -v godot-mono >/dev/null; then
  say "gdscript parse scan"
  BAD=0
  while IFS= read -r f; do
    rel="${f#godot/}"
    if godot-mono --headless --path godot --check-only --script "res://$rel" 2>&1 | grep -q "Parse Error"; then
      echo "PARSE FAIL: $rel"
      BAD=$((BAD + 1))
    fi
  done < <(find godot game -name "*.gd" -not -path "*/bin/*" 2>/dev/null)
  [ "$BAD" -gt 0 ] && fail "$BAD unparseable scripts"

  say "headless boot smoke (600 frames)"
  godot-mono --headless --path godot --quit-after 600 2>&1 | grep -E "SCRIPT ERROR|Failed to (load|instantiate)" && fail "boot errors"
  git checkout -- godot/LakskongenGame.csproj 2>/dev/null
  git clean -fdq godot/ 2>/dev/null
else
  echo "SKIP: no godot-mono (parse scan + boot smoke need it)"
fi

if [ "$FAIL" -eq 0 ]; then echo; echo "VERIFY OK"; else echo; echo "VERIFY FAILED"; fi
exit "$FAIL"
