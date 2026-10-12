# godot-mcp-ops Skill

Operator workflows for Godot projects using the MCP bridge (port 6550) and headless Godot fallback.

## Purpose

Provides CI-friendly CLI commands that inspect and validate Godot projects through:
- **MCP bridge** (ws://localhost:6550) when Godot editor is running
- **Headless Godot** (`godot-mono --headless`) for compile/export checks when editor is not available

All commands output JSON for CI consumption.

## Commands

| Command | Description |
|---------|-------------|
| `scene-diff` | Compare two scene files (base vs PR), report node added/removed, property changes, resource refs |
| `autoload-audit` | Scan project.godot autoloads for: missing node_paths, circular refs, init order issues, duplicate names |
| `shader-check` | Compile all `.gdshader` files, report errors/warnings (use Godot headless) |
| `export-verify` | Run headless export for all presets in `export_presets.cfg`, verify output size > 0 |

## Installation

The skill is installed at `.claude/skills/ecc/godot-mcp-ops/` in the lakskongen repo.

The CLI tool is at `godot_mcp_ops.py` and should be run from the repo root.

## Usage

```bash
# Scene diff between two files
python .claude/skills/ecc/godot-mcp-ops/godot_mcp_ops.py scene-diff --base res://scenes/MainMenu.tscn --pr res://scenes/MainMenu.tscn

# Autoload audit
python .claude/skills/ecc/godot-mcp-ops/godot_mcp_ops.py autoload-audit

# Shader compilation check
python .claude/skills/ecc/godot-mcp-ops/godot_mcp_ops.py shader-check

# Export verification
python .claude/skills/ecc/godot-mcp-ops/godot_mcp_ops.py export-verify
```

## CI Integration

Commands are added to `tools/ci.sh`:
- `godot:mcp:scene-diff`
- `godot:mcp:autoload-audit`
- `godot:mcp:shader-check`
- `godot:mcp:export-verify`

## Architecture

### MCP Bridge Mode (Preferred)
When Godot editor is running with the `godot_mcp_toolkit` plugin enabled, the CLI connects to `ws://localhost:6550` and uses the existing command registry:
- `scene.diff` for scene comparison
- `project.get_settings` / `autoload.manage` (action=list) for autoload audit
- `project.get_settings` with shader-related prefixes for shader check

### Headless Fallback Mode
When editor is not running, falls back to `godot-mono --headless`:
- Scene diff: loads both scenes as PackedScene and compares node trees
- Autoload audit: parses project.godot directly and validates paths
- Shader check: uses `--check-only` flag to compile shaders
- Export verify: uses `--export` flag for each preset

## Output Format

All commands output JSON to stdout:
```json
{
  "success": true,
  "command": "scene-diff",
  "data": { ... },
  "errors": [],
  "warnings": []
}
```

Non-zero exit code on failure.

## Requirements

- Godot 4.x (mono or standard)
- `godot_mcp_toolkit` plugin enabled in project (for MCP bridge mode)
- Python 3.10+
- websockets library (`pip install websockets`)

## Files

- `godot_mcp_ops.py` — Main CLI entry point
- `mcp_client.py` — MCP WebSocket client
- `headless_runner.py` — Headless Godot fallback
- `scene_diff.py` — Scene diff logic
- `autoload_audit.py` — Autoload audit logic
- `shader_check.py` — Shader compilation check
- `export_verify.py` — Export verification