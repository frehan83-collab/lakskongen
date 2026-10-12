#!/usr/bin/env python3
"""
godot_mcp_ops.py — Godot MCP Operator Workflows

CLI for running operator workflows against a Godot project via:
- MCP bridge (ws://localhost:6550) when Godot editor is running
- Headless Godot fallback for compile/export checks
"""

import argparse
import asyncio
import json
import os
import sys
import subprocess
from pathlib import Path
from typing import Any, Dict, List, Optional

# Add skill directory to path for imports
SKILL_DIR = Path(__file__).parent
sys.path.insert(0, str(SKILL_DIR))

from mcp_client import MCPClient, MCPConnectionError
from headless_runner import HeadlessRunner, HeadlessError
from scene_diff import SceneDiff
from autoload_audit import AutoloadAudit
from shader_check import ShaderCheck
from export_verify import ExportVerify


class GodotMCPOps:
    def __init__(self, project_path: Path, godot_bin: Optional[str] = None):
        self.project_path = project_path
        self.godot_bin = godot_bin or self._find_godot()
        self.mcp_client: Optional[MCPClient] = None
        self.headless = HeadlessRunner(self.godot_bin, project_path)
        self._use_mcp = False

    def _find_godot(self) -> str:
        """Find Godot binary in PATH or common locations."""
        for candidate in ["godot-mono", "godot", "Godot"]:
            if path := subprocess.run(["which", candidate], capture_output=True, text=True).stdout.strip():
                return path
        # Common macOS locations
        for candidate in [
            "/Applications/Godot.app/Contents/MacOS/Godot",
            "/Applications/Godot_mono.app/Contents/MacOS/Godot",
            os.path.expanduser("~/Applications/Godot_mono.app/Contents/MacOS/Godot"),
            os.path.expanduser("~/Applications/Godot.app/Contents/MacOS/Godot"),
        ]:
            if os.path.isfile(candidate) and os.access(candidate, os.X_OK):
                return candidate
        # Homebrew cask
        import glob
        for candidate in glob.glob("/opt/homebrew/Caskroom/godot-mono/*/.homebrew-command-wrappers/godot-mono"):
            if os.access(candidate, os.X_OK):
                return candidate
        return "godot-mono"  # Will fail later with clear error

    async def connect_mcp(self) -> bool:
        """Try to connect to MCP bridge on ws://localhost:6550."""
        self.mcp_client = MCPClient("ws://localhost:6550")
        try:
            await self.mcp_client.connect()
            self._use_mcp = True
            return True
        except MCPConnectionError:
            self._use_mcp = False
            return False

    async def disconnect_mcp(self):
        if self.mcp_client:
            await self.mcp_client.disconnect()
            self.mcp_client = None
            self._use_mcp = False

    def run_scene_diff(self, base_scene: str, pr_scene: str) -> Dict[str, Any]:
        """Compare two scene files."""
        diff = SceneDiff(self)
        return diff.run(base_scene, pr_scene)

    def run_autoload_audit(self) -> Dict[str, Any]:
        """Audit project.godot autoloads."""
        audit = AutoloadAudit(self)
        return audit.run()

    def run_shader_check(self) -> Dict[str, Any]:
        """Compile all .gdshader files and report errors."""
        check = ShaderCheck(self)
        return check.run()

    def run_export_verify(self) -> Dict[str, Any]:
        """Run headless export for all presets."""
        verify = ExportVerify(self)
        return verify.run()


def output_json(result: Dict[str, Any]):
    """Output result as JSON to stdout."""
    json.dump(result, sys.stdout, indent=2)
    print()  # newline


def main():
    parser = argparse.ArgumentParser(description="Godot MCP Operator Workflows")
    parser.add_argument("--project", default="godot", help="Path to Godot project (default: godot)")
    parser.add_argument("--godot-bin", help="Path to Godot binary")
    parser.add_argument("--no-mcp", action="store_true", help="Skip MCP bridge, use headless only")

    subparsers = parser.add_subparsers(dest="command", required=True)

    # scene-diff
    diff_parser = subparsers.add_parser("scene-diff", help="Compare two scene files")
    diff_parser.add_argument("--base", required=True, help="Base scene path (res://...)")
    diff_parser.add_argument("--pr", required=True, help="PR scene path (res://...)")

    # autoload-audit
    subparsers.add_parser("autoload-audit", help="Audit project.godot autoloads")

    # shader-check
    subparsers.add_parser("shader-check", help="Compile all .gdshader files")

    # export-verify
    subparsers.add_parser("export-verify", help="Run headless export for all presets")

    args = parser.parse_args()

    # Find repo root by looking for .git or known structure
    script_path = Path(__file__).resolve()
    repo_root = script_path
    while repo_root != repo_root.parent:
        if (repo_root / ".git").exists() or (repo_root / "godot" / "project.godot").exists():
            break
        repo_root = repo_root.parent
    else:
        repo_root = script_path.parent.parent.parent.parent  # fallback

    project_path = repo_root / args.project

    if not (project_path / "project.godot").exists():
        print(json.dumps({
            "success": False,
            "command": args.command,
            "errors": [f"Godot project not found at {project_path}"],
            "warnings": []
        }), file=sys.stderr)
        sys.exit(1)

    ops = GodotMCPOps(project_path, args.godot_bin)

    mode_info = {"mode": "mcp"}
    try:
        if not args.no_mcp:
            # Try MCP bridge first
            connected = asyncio.run(ops.connect_mcp())
            if not connected:
                mode_info = {"mode": "headless", "reason": "MCP bridge not available"}
        else:
            mode_info = {"mode": "headless", "reason": "MCP bridge disabled by flag"}

        # Run the command
        if args.command == "scene-diff":
            result = ops.run_scene_diff(args.base, args.pr)
        elif args.command == "autoload-audit":
            result = ops.run_autoload_audit()
        elif args.command == "shader-check":
            result = ops.run_shader_check()
        elif args.command == "export-verify":
            result = ops.run_export_verify()
        else:
            result = {
                "success": False,
                "command": args.command,
                "errors": [f"Unknown command: {args.command}"],
                "warnings": []
            }

        # Merge mode info into result data
        if "data" not in result:
            result["data"] = {}
        result["data"].update(mode_info)
        result["command"] = args.command
        output_json(result)

        sys.exit(0 if result.get("success") else 1)

    except Exception as e:
        output_json({
            "success": False,
            "command": args.command,
            "errors": [f"Unhandled error: {str(e)}"],
            "warnings": []
        })
        sys.exit(1)
    finally:
        asyncio.run(ops.disconnect_mcp())


if __name__ == "__main__":
    main()