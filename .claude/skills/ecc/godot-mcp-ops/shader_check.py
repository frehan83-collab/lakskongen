#!/usr/bin/env python3
"""
shader_check.py — Shader Compilation Check

Compiles all .gdshader files and reports errors/warnings.
"""

from typing import Any, Dict, List


class ShaderCheck:
    def __init__(self, ops):
        self.ops = ops

    def run(self) -> Dict[str, Any]:
        """Run shader compilation check."""
        try:
            if self.ops._use_mcp and self.ops.mcp_client:
                return self._check_via_mcp()
            else:
                return self._check_via_headless()
        except Exception as e:
            return {
                "success": False,
                "data": {},
                "errors": [f"Shader check failed: {str(e)}"],
                "warnings": []
            }

    def _check_via_mcp(self) -> Dict[str, Any]:
        """Use MCP bridge to find and check shaders."""
        # Get project settings for shader paths
        result = self.ops.mcp_client.call("project.get_settings", {"prefix": "rendering/shaders"})
        # For now, fall back to headless for actual compilation
        return self._check_via_headless()

    def _check_via_headless(self) -> Dict[str, Any]:
        """Use headless Godot to find and validate shaders."""
        # Find all shader files
        shader_files = self.ops.headless.find_shaders()

        errors = []
        warnings = []

        # For each shader, try to load it (which compiles it)
        for shader_path in shader_files:
            result = self.ops.headless.compile_shader(shader_path)
            if "error" in result:
                errors.append(f"{shader_path}: {result['error']}")

        return {
            "success": len(errors) == 0,
            "data": {
                "mode": "headless",
                "shaders_checked": len(shader_files),
                "shader_files": shader_files,
            },
            "errors": errors,
            "warnings": warnings
        }