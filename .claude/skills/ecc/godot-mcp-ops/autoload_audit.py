#!/usr/bin/env python3
"""
autoload_audit.py — Autoload Audit Logic

Scans project.godot autoloads for:
- Missing node_paths (script file doesn't exist)
- Circular references
- Init order issues
- Duplicate names
"""

import asyncio
import re
import subprocess
from typing import Any, Dict, List
from pathlib import Path


def is_git_ignored(project_path: Path, script_path: str) -> bool:
    """True when git ignores a res:// path of the project (False without
    git). Local tooling such as the Godot MCP toolkit lives under ignored
    paths, so a clean checkout lacks it by design (F177)."""
    rel = script_path[6:] if script_path.startswith("res://") else script_path
    try:
        result = subprocess.run(
            ["git", "check-ignore", "-q", rel],
            cwd=project_path, capture_output=True, timeout=10)
    except (OSError, subprocess.SubprocessError):
        return False
    return result.returncode == 0


class AutoloadAudit:
    def __init__(self, ops):
        self.ops = ops
        self.project_path = ops.project_path

    def run(self) -> Dict[str, Any]:
        """Run autoload audit."""
        try:
            if self.ops._use_mcp and self.ops.mcp_client:
                try:
                    return asyncio.run(self._audit_via_mcp())
                except Exception as mcp_error:
                    # MCP failed (timeout, connection error, etc.) — fall back to headless
                    return self._audit_via_headless()
            else:
                return self._audit_via_headless()
        except Exception as e:
            return {
                "success": False,
                "data": {},
                "errors": [f"Autoload audit failed: {str(e)}"],
                "warnings": []
            }

    async def _audit_via_mcp(self) -> Dict[str, Any]:
        """Use MCP bridge to get autoload list."""
        # Use a longer timeout for autoload.manage as it may need to scan all scripts
        original_timeout = self.ops.mcp_client.timeout
        self.ops.mcp_client.timeout = 30.0
        try:
            result = await self.ops.mcp_client.call("autoload.manage", {"action": "list"})
        finally:
            self.ops.mcp_client.timeout = original_timeout
        autoloads = result.get("autoloads", [])
        return self._analyze_autoloads(autoloads, "mcp")

    def _audit_via_headless(self) -> Dict[str, Any]:
        """Use headless Godot to get autoload list."""
        autoloads = self.ops.headless.get_autoloads()
        return self._analyze_autoloads(autoloads, "headless")

    def _analyze_autoloads(self, autoloads: List[Dict], mode: str) -> Dict[str, Any]:
        """Analyze autoloads for issues."""
        errors = []
        warnings = []

        # Track names for duplicates
        name_counts = {}
        for autoload in autoloads:
            name = autoload.get("name", "")
            name_counts[name] = name_counts.get(name, 0) + 1

        # Check each autoload
        for autoload in autoloads:
            name = autoload.get("name", "")
            script_path = autoload.get("script_path", "")
            enabled = autoload.get("enabled", True)

            # Duplicate names
            if name_counts.get(name, 0) > 1:
                errors.append(f"Duplicate autoload name: '{name}'")

            # Missing script file. A script under a git-ignored path is local
            # tooling each developer installs (the Godot MCP toolkit plugin
            # re-registers its runtime autoload in project.godot whenever the
            # editor opens), so a clean checkout lacks it by design (F177).
            if script_path and not self._script_exists(script_path):
                if self._is_git_ignored(script_path):
                    warnings.append(
                        f"Autoload '{name}': optional local tooling not installed "
                        f"('{script_path}' is git-ignored)")
                else:
                    errors.append(f"Autoload '{name}': missing script at '{script_path}'")

            # Check for circular references (autoloads referencing each other)
            if script_path and self._has_circular_ref(script_path, autoloads):
                warnings.append(f"Autoload '{name}': potential circular reference detected")

            # Check init order (autoloads that depend on other autoloads)
            deps = self._get_autoload_dependencies(script_path, autoloads)
            if deps:
                warnings.append(f"Autoload '{name}' depends on: {', '.join(deps)} (verify init order)")

        return {
            "success": len(errors) == 0,
            "data": {
                "mode": mode,
                "autoloads": autoloads,
                "total": len(autoloads),
                "duplicates": [name for name, count in name_counts.items() if count > 1],
                "issues": {
                    "missing_scripts": [e for e in errors if "missing script" in e],
                    "circular_refs": [w for w in warnings if "circular" in w],
                    "init_order": [w for w in warnings if "depends on" in w]
                }
            },
            "errors": errors,
            "warnings": warnings
        }

    def _is_git_ignored(self, script_path: str) -> bool:
        """True when git ignores the script's path (False without git)."""
        return is_git_ignored(self.project_path, script_path)

    def _script_exists(self, script_path: str) -> bool:
        """Check if script file exists."""
        if script_path.startswith("res://"):
            # Convert to filesystem path
            rel_path = script_path[6:]  # Remove "res://"
            return (self.project_path / rel_path).exists()
        return False

    def _has_circular_ref(self, script_path: str, all_autoloads: List[Dict]) -> bool:
        """Check if script references other autoloads that might cause circular deps."""
        if not self._script_exists(script_path):
            return False

        try:
            content = (self.project_path / script_path[6:]).read_text(encoding='utf-8')
            # Look for get_node('/root/OtherAutoload') patterns
            autoload_names = {a["name"] for a in all_autoloads}
            for name in autoload_names:
                if name != Path(script_path).stem:
                    if f"/root/{name}" in content or f'get_node("/root/{name}")' in content:
                        # Check if the other autoload also references this one
                        other_path = next((a["script_path"] for a in all_autoloads if a["name"] == name), None)
                        if other_path and self._script_exists(other_path):
                            other_content = (self.project_path / other_path[6:]).read_text(encoding='utf-8')
                            if f"/root/{Path(script_path).stem}" in other_content:
                                return True
        except Exception:
            pass
        return False

    def _get_autoload_dependencies(self, script_path: str, all_autoloads: List[Dict]) -> List[str]:
        """Get list of other autoloads this script depends on."""
        if not self._script_exists(script_path):
            return []

        deps = []
        try:
            content = (self.project_path / script_path[6:]).read_text(encoding='utf-8')
            autoload_names = {a["name"] for a in all_autoloads}
            for name in autoload_names:
                if name != Path(script_path).stem:
                    if f"/root/{name}" in content:
                        deps.append(name)
        except Exception:
            pass
        return deps