#!/usr/bin/env python3
"""Check that the project's MCP servers start and answer, the way an agent client would.

Usage:
  python3 tools/mcp_probe.py                 # both servers
  python3 tools/mcp_probe.py godot           # one server
  python3 tools/mcp_probe.py --config cline  # read Cline's settings instead of .mcp.json

For each server it spawns the configured command over stdio, sends `initialize` and
`tools/list`, then one read-only call that needs the live app:
  godot   -> editor_get_console {limit: 1}   (needs the Godot editor open on godot/)
  blender -> get_objects_summary {}          (needs Blender open with the MCP add-on started)

Exit 0 = every probed server started, listed its tools and answered the live call.
Exit 1 = a server failed to start or list tools (a config or install problem).
Exit 2 = the servers start, but a live app is not reachable (open Godot/Blender).
Read-only: it never edits a scene, a file or the registry.
"""
from __future__ import annotations

import json
import os
import select
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CLINE_SETTINGS = Path.home() / ".cline/data/settings/cline_mcp_settings.json"
LIVE_CALLS = {
    "godot": ("editor_get_console", {"limit": 1}),
    "blender": ("get_objects_summary", {}),
}
TIMEOUT_S = 90  # first `npx -y` run downloads the bridge


def load_servers(source: str) -> dict[str, tuple[str, list[str], dict[str, str]]]:
    """Return {name: (command, args, env)} from .mcp.json or Cline's settings."""
    path = CLINE_SETTINGS if source == "cline" else REPO_ROOT / ".mcp.json"
    raw = json.loads(path.read_text(encoding="utf-8"))["mcpServers"]
    out = {}
    for name, cfg in raw.items():
        cfg = cfg.get("transport", cfg)  # Cline nests stdio fields under "transport"
        env = {k: os.path.expandvars(v) for k, v in cfg.get("env", {}).items()}
        args = [os.path.expandvars(a) for a in cfg.get("args", [])]
        out[name] = (cfg["command"], args, env)
    return out


class Session:
    def __init__(self, command: str, args: list[str], env: dict[str, str]):
        self.proc = subprocess.Popen(
            [command, *args], cwd=REPO_ROOT, env={**os.environ, **env}, text=True,
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        self.next_id = 0

    def request(self, method: str, params: dict | None = None) -> dict | None:
        self.next_id += 1
        self._send({"jsonrpc": "2.0", "id": self.next_id, "method": method, "params": params or {}})
        deadline = time.time() + TIMEOUT_S
        while time.time() < deadline:
            ready, _, _ = select.select([self.proc.stdout], [], [], 1)
            if not ready:
                if self.proc.poll() is not None:
                    return None
                continue
            line = self.proc.stdout.readline()
            if not line:
                return None
            try:
                msg = json.loads(line)
            except json.JSONDecodeError:
                continue
            if msg.get("id") == self.next_id:
                return msg
        return None

    def notify(self, method: str) -> None:
        self._send({"jsonrpc": "2.0", "method": method})

    def _send(self, msg: dict) -> None:
        self.proc.stdin.write(json.dumps(msg) + "\n")
        self.proc.stdin.flush()

    def close(self) -> None:
        self.proc.kill()
        self.proc.wait()


def probe(name: str, command: str, args: list[str], env: dict[str, str]) -> int:
    s = Session(command, args, env)
    try:
        init = s.request("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                        "clientInfo": {"name": "lakskongen-mcp-probe", "version": "1"}})
        if not init or "result" not in init:
            print(f"FAIL {name}: server did not initialize ({command} {' '.join(args)})")
            return 1
        info = init["result"].get("serverInfo", {})
        s.notify("notifications/initialized")
        listed = s.request("tools/list")
        tools = [t["name"] for t in (listed or {}).get("result", {}).get("tools", [])]
        if not tools:
            print(f"FAIL {name}: no tools listed")
            return 1
        print(f"OK   {name}: {info.get('name')} {info.get('version')}, {len(tools)} tools")
        if name not in LIVE_CALLS:
            return 0
        tool, tool_args = LIVE_CALLS[name]
        reply = s.request("tools/call", {"name": tool, "arguments": tool_args})
        result = (reply or {}).get("result", {})
        text = " ".join(c.get("text", "") for c in result.get("content", []))
        if not reply or result.get("isError") or "AUTH_FAILED" in text:
            print(f"LIVE {name}: {tool} failed: {text[:200] or 'no reply'}")
            return 2
        print(f"OK   {name}: live call {tool} answered")
        return 0
    finally:
        s.close()


def main() -> int:
    argv = sys.argv[1:]
    source = "repo"
    if "--config" in argv:
        i = argv.index("--config")
        source = argv[i + 1]
        del argv[i:i + 2]
    servers = load_servers(source)
    names = argv or [n for n in ("godot", "blender") if n in servers]
    worst = 0
    for name in names:
        if name not in servers:
            print(f"FAIL {name}: not configured in {source} config")
            worst = 1
            continue
        code = probe(name, *servers[name])
        worst = code if worst == 0 or code == 1 else worst
    return worst


if __name__ == "__main__":
    sys.exit(main())
