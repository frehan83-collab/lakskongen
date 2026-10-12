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

Additionally, any headless-editor boot that writes to the SAME per-project
registry + token (e.g. `godot --headless --export-release`, historically run by
the CI export-verify step) overwrites the live editor's entry file and session
token, then deletes the entry on exit — leaving the project key absent and the
on-disk token not matching the editor's in-memory token.

This script repairs the discovery layer end-to-end:
  1. derives the live editor facts (WS port + pid via lsof, token file, LSP port),
  2. writes the derived entry under the repo-root key the bridge looks up,
  3. runs a real WS auth handshake against the editor to prove the entry works.

It only edits the derived registry file (projects.json). It never writes the
plugin's entries/ directory, and it never restarts the editor.

Durable fix (needs an opencode restart, apply once): give the bridge the right
key at spawn time — see docs/research/MCP_CAPABILITIES.md §3
(`GODOT_MCP_PROJECT_PATH=<repo>/godot`). Until then, run this after any CI run
(the sandboxed export-verify in tools/ci.sh preserves the editor token; the
entry-file churn is what this repairs).

Usage:  python3 tools/mcp_bridge_alias.py [--print]
Exit 0 = alias present and auth verified (or websocket lib missing → verified=skipped).
Exit 2 = repaired entry written but auth probe FAILED (token out of sync —
         restart the Godot editor, then re-run).
Exit 1 = could not determine live editor facts (is the editor running?).
"""
from __future__ import annotations

import asyncio
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
GODOT_DIR = REPO_ROOT / "godot"
WS_PORT_RANGE = range(6550, 6561)
LSP_HOST = "127.0.0.1"
LSP_PORT = 6005


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


def find_editor_ws() -> tuple[int, int] | None:
    """Return (pid, port) of the live Godot editor MCP WS server, or None."""
    lsof = shutil.which("lsof")
    if not lsof:
        return None
    try:
        out = subprocess.run([lsof, "-nP", "-iTCP", "-sTCP:LISTEN"],
                             capture_output=True, text=True, timeout=15).stdout
    except Exception:
        return None
    best: tuple[int, int] | None = None
    for line in out.splitlines():
        parts = line.split()
        if len(parts) < 9 or parts[0] != "Godot":
            continue
        m = re.search(r"TCP\s+127\.0\.0\.1:(\d+)\s+\(LISTEN\)", line)
        if not m:
            continue
        port = int(m.group(1))
        pid = int(parts[1])
        if port in WS_PORT_RANGE and (best is None or port < best[1]):
            best = (pid, port)
    return best


def find_token_file() -> Path | None:
    """Newest editor-published mcp_token under the Lakskongen user data dirs."""
    roots = []
    if sys.platform == "darwin":
        roots.append(Path.home() / "Library/Application Support/Godot/app_userdata")
    elif os.name == "nt":
        roots.append(Path(os.environ.get("APPDATA", "")) / "Godot/app_userdata")
    else:
        roots.append(Path.home() / ".local/share/godot/app_userdata")
    candidates: list[Path] = []
    for root in roots:
        if not root or not root.exists():
            continue
        for token in root.glob("Lakskongen/addons/godot_mcp_toolkit/project_instance_*/mcp_token"):
            candidates.append(token)
    if not candidates:
        return None
    return max(candidates, key=lambda p: p.stat().st_mtime)


def derive_entry(pid: int, port: int, token_path: Path) -> dict:
    import time
    return {
        "godot_version": "4.7",
        "lsp_host": LSP_HOST,
        "lsp_port": float(LSP_PORT),
        "pid": float(pid),
        "port": float(port),
        "runtime_pid": None,
        "runtime_port": None,
        "started_at": float(int(time.time())),
        "token_path": str(token_path),
    }


async def probe_auth(port: int, token: str) -> tuple[bool, str]:
    """Real WS handshake: send {auth: token}, expect an authed ack."""
    try:
        import websockets  # type: ignore
    except ImportError:
        return True, "skipped (websockets lib not installed)"
    import json as _json
    try:
        ws = await websockets.connect(f"ws://127.0.0.1:{port}", open_timeout=5)
    except Exception as e:  # noqa: BLE001
        return False, f"connect failed: {e!r}"
    try:
        await ws.send(_json.dumps({"auth": token}))
        frame = await asyncio.wait_for(ws.recv(), timeout=5)
        data = _json.loads(frame)
        if data.get("authed") is True:
            return True, "authed"
        return False, f"unexpected ack: {str(data)[:120]}"
    except Exception as e:  # noqa: BLE001
        return False, f"handshake failed: {e!r}"
    finally:
        try:
            await ws.close()
        except Exception:  # noqa: BLE001
            pass


def main() -> int:
    show = "--print" in sys.argv

    live = find_editor_ws()
    token_path = find_token_file()
    if live is None or token_path is None:
        editor = "running" if live is None else "missing token"
        print(f"FAIL: could not determine live editor facts ({editor}). "
              f"Is the Godot editor running with the MCP toolkit?", file=sys.stderr)
        return 1
    pid, port = live
    token = token_path.read_text(encoding="utf-8").strip()
    entry = derive_entry(pid, port, token_path)

    reg = registry_path()
    if reg.exists():
        try:
            data = json.loads(reg.read_text(encoding="utf-8"))
        except Exception:  # noqa: BLE001
            data = {}
    else:
        data = {}
    by_path: dict = data.setdefault("by_path", {})
    alias_key = norm(str(REPO_ROOT))
    by_path[alias_key] = entry

    fd, tmp = tempfile.mkstemp(dir=str(reg.parent), prefix="projects.", suffix=".tmp")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(data, f, indent="\t")
    os.replace(tmp, reg)  # atomic on POSIX and Windows
    print(f"OK: alias written ({alias_key} -> port {port}, pid {pid}, token {token_path})")

    ok, detail = asyncio.run(probe_auth(port, token))
    if ok:
        print(f"OK: auth probe {detail}")
        if show:
            print(json.dumps({alias_key: entry}, indent=2))
        return 0
    print(f"FAIL: auth probe failed ({detail}).\n"
          f"      The on-disk token does not match the editor's in-memory token.\n"
          f"      Restart the Godot editor (fresh token) and re-run this script.",
          file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
