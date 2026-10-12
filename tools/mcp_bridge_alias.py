#!/usr/bin/env python3
"""Bridge alias repair for the Godot MCP Toolkit registry (macOS: ~/Library/Application Support/godot-mcp-toolkit/projects.json).

Why this exists
---------------
The `@npgamedev/godot-mcp-server` bridge (used by opencode's `godot` MCP server)
resolves its registry key as `GODOT_MCP_PROJECT_PATH ?? process.cwd()`. opencode
spawns it with cwd = the *repo root* (`lakskongen/`), but the Godot editor
registers its entry under the *Godot project dir* (`lakskongen/godot`). The keys
never match, so every tool call dies with:

    AUTH_FAILED: no registry entry for /Users/<user>/lakskongen

This script copies the live editor entry (port/token/pid/lsp) under the repo-root
key so the running bridge can find it. It only edits the derived registry file
(projects.json), never the plugin's entries/ directory, so the editor's own
registration is untouched.

Caveats
-------
* `projects.json` is a projection rebuilt from `entries/` whenever the editor or a
  headless Godot instance registers/deregisters. Every `./tools/ci.sh` run boots a
  headless instance, so after CI (or any editor restart) re-run this script.
* The durable fix is to give the bridge the right key at spawn time, e.g. wrap
  the command: `env GODOT_MCP_PROJECT_PATH=<repo>/godot npx -y @npgamedev/godot-mcp-server`
  in `opencode.json` + restart the opencode service. Until that is done, use this.

Usage:  python3 tools/mcp_bridge_alias.py [--print]
Exit 0 = alias present (written or already current). Exit 1 = no live editor entry found.
"""
from __future__ import annotations

import json
import os
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
GODOT_DIR = REPO_ROOT / "godot"


def registry_path() -> Path:
    if sys.platform == "darwin":
        return Path.home() / "Library/Application Support/godot-mcp-toolkit/projects.json"
    if os.name == "nt":
        base = Path(os.environ.get("APPDATA", Path.home() / "AppData/Roaming"))
        return base / "godot-mcp-toolkit/projects.json"
    base = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local/share"))
    return base / "godot-mcp-toolkit/projects.json"


def norm(p: str) -> str:
    n = p.replace("\\", "/").rstrip("/")
    if sys.platform in ("darwin", "win32"):
        n = n.lower()
    return n


def main() -> int:
    show = "--print" in sys.argv
    reg = registry_path()
    if not reg.exists():
        print(f"FAIL: registry not found: {reg}", file=sys.stderr)
        return 1
    data = json.loads(reg.read_text(encoding="utf-8"))
    by_path: dict = data.setdefault("by_path", {})

    editor_key = norm(str(GODOT_DIR))
    alias_key = norm(str(REPO_ROOT))

    entry = by_path.get(editor_key)
    if entry is None:
        # Fallback: any entry whose key ends in /godot with a live-looking port.
        for k, v in by_path.items():
            if k.endswith("/godot") and int(v.get("port") or 0) > 0:
                entry = v
                editor_key = k
                break
    if entry is None or int(entry.get("port") or 0) <= 0:
        print(f"FAIL: no editor entry for {editor_key} in {reg}", file=sys.stderr)
        print(f"      known keys: {sorted(by_path)}", file=sys.stderr)
        return 1

    if by_path.get(alias_key) == entry:
        print(f"OK: alias already current ({alias_key} -> port {entry['port']}, pid {entry.get('pid')})")
        if show:
            print(json.dumps({alias_key: entry}, indent=2))
        return 0

    by_path[alias_key] = entry
    fd, tmp = tempfile.mkstemp(dir=str(reg.parent), prefix="projects.", suffix=".tmp")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(data, f, indent="\t")
    os.replace(tmp, reg)  # atomic on POSIX and Windows
    print(f"OK: alias written ({alias_key} -> port {entry['port']}, pid {entry.get('pid')}, token {entry.get('token_path')})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
