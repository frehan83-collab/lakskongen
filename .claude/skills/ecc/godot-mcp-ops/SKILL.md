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
| `shader-check` | Check every uniform hint against the Godot 4.7 list, then compile every `.gdshader` under a real renderer (needs a display; skipped and exit 3 without one) |
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

`tools/ci.sh` runs `autoload-audit`, `shader-check` and `export-verify` with
`--no-mcp --godot-bin "$GODOT_BIN"`, so they check this checkout with the
gate's Godot rather than whatever project a running editor has open.
`scene-diff` needs explicit scenes and is run by hand.

## Architecture

### MCP Bridge Mode (Preferred)
When Godot editor is running with the `godot_mcp_toolkit` plugin enabled, the CLI connects to `ws://localhost:6550` and uses the existing command registry:
- `scene.diff` for scene comparison
- `project.get_settings` / `autoload.manage` (action=list) for autoload audit

### Headless Fallback Mode
When editor is not running, falls back to `godot-mono --headless`:
- Scene diff: loads both scenes as PackedScene and compares node trees
- Autoload audit: parses project.godot directly and validates paths
- Shader check: never headless. The `--headless` dummy renderer parses no
  shader code, so loading a broken shader there succeeds (review F958). The
  check reads every uniform hint statically (`shader_hints.py`), then runs
  `shader_compile_probe.gd` with a window (or `xvfb-run` on Linux): each shader
  is drawn for a few frames and every `SHADER ERROR` fails the check
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

Exit codes: 0 passed, 1 failed, 3 passed but part of the check was skipped
(`"skipped": true`, e.g. `shader-check` without a display).

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
- `shader_check.py` — Shader check: hint check plus real-renderer compile
- `shader_hints.py` — Static uniform-hint allow-list check (no Godot needed)
- `shader_compile_probe.gd` — SceneTree script that draws each shader
- `export_verify.py` — Export verification