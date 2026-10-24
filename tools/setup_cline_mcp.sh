#!/usr/bin/env bash
# Register the project's two MCP servers (Godot and Blender) with the Cline CLI.
#
# Usage: bash tools/setup_cline_mcp.sh
#
# Writes ~/.cline/data/settings/cline_mcp_settings.json through Cline's own installer
# (`cline mcp add --yes`), so re-running it is safe: it replaces the two entries.
# The commands match .mcp.json and opencode.json:
#   godot   -> npx -y @npgamedev/godot-mcp-server, with GODOT_MCP_PROJECT_PATH=<main checkout>/godot
#              (override the checkout with LAKSKONGEN_REPO=/path)
#              (the registry key the Godot editor registers under; see
#              docs/research/MCP_CAPABILITIES.md §3)
#   blender -> uv --directory ~/blender_mcp/mcp run blender-mcp
# Check afterwards with: python3 tools/mcp_probe.py --config cline
set -euo pipefail

SCRIPT_REPO="$(cd "$(dirname "$0")/.." && pwd)"
# The Godot editor registers the MAIN checkout's godot/ dir, so resolve the main
# checkout even when this runs from a worktree (its common .git dir's parent).
COMMON_GIT_DIR="$(git -C "$SCRIPT_REPO" rev-parse --path-format=absolute --git-common-dir)"
REPO_ROOT="${LAKSKONGEN_REPO:-$(dirname "$COMMON_GIT_DIR")}"
BLENDER_MCP_DIR="${BLENDER_MCP_DIR:-$HOME/blender_mcp/mcp}"

for tool in cline npx uv; do
	command -v "$tool" >/dev/null 2>&1 || { echo "FAIL: '$tool' is not on PATH" >&2; exit 1; }
done
[ -d "$REPO_ROOT/godot" ] || { echo "FAIL: no Godot project at $REPO_ROOT/godot" >&2; exit 1; }
[ -d "$BLENDER_MCP_DIR" ] || { echo "FAIL: no Blender MCP server at $BLENDER_MCP_DIR" >&2; exit 1; }

cline mcp add godot --yes --json -- \
	env "GODOT_MCP_PROJECT_PATH=$REPO_ROOT/godot" npx -y @npgamedev/godot-mcp-server
echo
cline mcp add blender --yes --json -- \
	uv --directory "$BLENDER_MCP_DIR" run blender-mcp
echo
echo "OK: godot and blender registered with Cline. Check: python3 tools/mcp_probe.py --config cline"
