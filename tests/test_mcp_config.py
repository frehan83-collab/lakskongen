"""The checked-in MCP configs agree, and tools/mcp_probe.py reads both config formats.

No live Godot or Blender is needed: these tests only parse configs.
"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
GODOT_BRIDGE = "@npgamedev/godot-mcp-server"


def _load_probe():
    spec = importlib.util.spec_from_file_location("mcp_probe", REPO_ROOT / "tools/mcp_probe.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_mcp_json_registers_godot_and_blender_with_the_godot_project_key():
    servers = _load_probe().load_servers("repo")
    assert set(servers) >= {"godot", "blender"}
    command, args, env = servers["godot"]
    assert command == "npx" and GODOT_BRIDGE in args
    # The editor registers <repo>/godot; without this key every call is AUTH_FAILED
    # (docs/research/MCP_CAPABILITIES.md §3).
    assert env["GODOT_MCP_PROJECT_PATH"].endswith("/lakskongen/godot")
    command, args, _ = servers["blender"]
    assert command == "uv" and args[-2:] == ["run", "blender-mcp"]


def test_opencode_json_gives_the_godot_bridge_the_same_project_key():
    cfg = json.loads((REPO_ROOT / "opencode.json").read_text(encoding="utf-8"))["mcp"]
    godot = cfg["godot"]["command"]
    assert godot[0] == "env" and GODOT_BRIDGE in godot
    assert any(a.startswith("GODOT_MCP_PROJECT_PATH=") and a.endswith("/lakskongen/godot") for a in godot)
    assert cfg["blender"]["command"][-2:] == ["run", "blender-mcp"]


def test_the_probe_reads_clines_nested_transport_format(tmp_path, monkeypatch):
    probe = _load_probe()
    settings = tmp_path / "cline_mcp_settings.json"
    settings.write_text(json.dumps({"mcpServers": {
        "godot": {"transport": {"type": "stdio", "command": "env",
                                "args": ["GODOT_MCP_PROJECT_PATH=/x/lakskongen/godot", "npx", "-y", GODOT_BRIDGE]}},
        "blender": {"transport": {"type": "stdio", "command": "uv",
                                  "args": ["--directory", "/x/blender_mcp/mcp", "run", "blender-mcp"]}},
    }}), encoding="utf-8")
    monkeypatch.setattr(probe, "CLINE_SETTINGS", settings)
    servers = probe.load_servers("cline")
    assert servers["godot"] == ("env", ["GODOT_MCP_PROJECT_PATH=/x/lakskongen/godot", "npx", "-y", GODOT_BRIDGE], {})
    assert servers["blender"][0] == "uv"


def test_the_probe_fails_on_an_unconfigured_server(capsys, monkeypatch):
    probe = _load_probe()
    monkeypatch.setattr("sys.argv", ["mcp_probe.py", "no_such_server"])
    assert probe.main() == 1
    assert "not configured" in capsys.readouterr().out
