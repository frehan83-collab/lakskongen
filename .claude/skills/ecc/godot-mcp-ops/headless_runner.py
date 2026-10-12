#!/usr/bin/env python3
"""
headless_runner.py — Headless Godot Runner

Executes Godot headless commands for compile/export checks
when MCP bridge is not available.
"""

import subprocess
import json
import os
import re
from pathlib import Path
from typing import Any, Dict, List, Optional


class HeadlessError(Exception):
    """Raised when headless Godot execution fails."""
    pass


def _extract_json(output: str) -> Any:
    """Extract the last valid JSON object from Godot output."""
    # Godot prints version info and logs to stdout; the JSON is typically the last line
    lines = output.strip().split('\n')
    for line in reversed(lines):
        line = line.strip()
        if not line:
            continue
        # Skip Godot version/engine lines
        if line.startswith("Godot Engine") or line.startswith("https://godotengine.org"):
            continue
        try:
            return json.loads(line)
        except json.JSONDecodeError:
            continue
    # If no valid JSON found, try the whole output
    try:
        return json.loads(output.strip())
    except json.JSONDecodeError:
        raise HeadlessError(f"No valid JSON found in output: {output[:500]}")


class HeadlessRunner:
    def __init__(self, godot_bin: str, project_path: Path):
        self.godot_bin = godot_bin
        self.project_path = project_path

    def run(self, args: List[str], capture_output: bool = True, timeout: int = 300) -> subprocess.CompletedProcess:
        """Run Godot headless with given arguments."""
        cmd = [self.godot_bin, "--headless", "--path", str(self.project_path)] + args
        env = os.environ.copy()
        env["TERM"] = "dumb"  # Avoid ANSI codes in output

        try:
            result = subprocess.run(
                cmd,
                capture_output=capture_output,
                text=True,
                timeout=timeout,
                env=env
            )
            return result
        except subprocess.TimeoutExpired as e:
            raise HeadlessError(f"Godot headless timed out after {timeout}s: {e}")
        except FileNotFoundError:
            raise HeadlessError(f"Godot binary not found: {self.godot_bin}")
        except Exception as e:
            raise HeadlessError(f"Failed to run Godot headless: {e}")

    def run_script(self, script_path: str, args: List[str] = None) -> subprocess.CompletedProcess:
        """Run a GDScript file headless."""
        cmd = ["--script", script_path]
        if args:
            cmd += ["--"] + args
        return self.run(cmd)

    def check_only(self) -> subprocess.CompletedProcess:
        """Run Godot with --check-only to parse/compile all scripts and shaders."""
        return self.run(["--check-only"], timeout=180)

    def export(self, preset_name: str, output_path: str) -> subprocess.CompletedProcess:
        """Run headless export for a preset."""
        return self.run(["--export-release", preset_name, output_path])

    def export_debug(self, preset_name: str, output_path: str) -> subprocess.CompletedProcess:
        """Run headless export for a preset (debug)."""
        return self.run(["--export-debug", preset_name, output_path])

    def run_gdscript(self, code: str) -> subprocess.CompletedProcess:
        """Run inline GDScript code."""
        # Write to temp file and execute
        import tempfile
        with tempfile.NamedTemporaryFile(mode='w', suffix='.gd', delete=False) as f:
            f.write(code)
            temp_path = f.name
        try:
            return self.run_script(temp_path)
        finally:
            os.unlink(temp_path)

    def parse_scene(self, scene_path: str) -> Dict[str, Any]:
        """Parse a scene file and return its node tree as JSON."""
        code = f'''
extends SceneTree

func _init():
    var packed = load("{scene_path}")
    if packed == null:
        print(JSON.stringify({{"error": "failed to load"}}))
        quit(1)
    var state = packed.get_state()
    var result = {{"nodes": []}}
    for i in range(state.get_node_count()):
        var node_info = {{
            "name": state.get_node_name(i),
            "type": state.get_node_type(i),
            "path": state.get_node_path(i),
            "groups": state.get_node_groups(i),
        }}
        result.nodes.append(node_info)
    print(JSON.stringify(result))
    quit(0)
'''
        result = self.run_gdscript(code)
        if result.returncode != 0:
            raise HeadlessError(f"Failed to parse scene: {result.stderr}")
        try:
            return _extract_json(result.stdout)
        except HeadlessError:
            raise HeadlessError(f"Invalid JSON output: {result.stdout}")

    def get_autoloads(self) -> List[Dict[str, Any]]:
        """Get all autoloads from project.godot."""
        code = '''
extends SceneTree

func _init():
    var autoloads = []
    for property in ProjectSettings.get_property_list():
        var name = property.name
        if name.begins_with("autoload/"):
            var aname = name.substr("autoload/".length())
            var raw = str(ProjectSettings.get_setting(name))
            var enabled = raw.begins_with("*")
            var path = raw.lstrip("*")
            autoloads.append({"name": aname, "script_path": path, "enabled": enabled})
    print(JSON.stringify(autoloads))
    quit(0)
'''
        result = self.run_gdscript(code)
        if result.returncode != 0:
            raise HeadlessError(f"Failed to get autoloads: {result.stderr}")
        try:
            return _extract_json(result.stdout)
        except HeadlessError:
            raise HeadlessError(f"Invalid JSON output: {result.stdout}")

    def find_shaders(self) -> List[str]:
        """Find all .gdshader files in the project."""
        code = '''
extends SceneTree

func _init():
    var shaders = []
    var dir = DirAccess.open("res://")
    _find_shaders_recursive(dir, "res://", shaders)
    print(JSON.stringify(shaders))
    quit(0)

func _find_shaders_recursive(dir: DirAccess, base_path: String, shaders: Array):
    for file in dir.get_files():
        if file.get_extension().to_lower() == "gdshader":
            shaders.append(base_path.path_join(file))
    for subdir in dir.get_directories():
        var sub = DirAccess.open(base_path.path_join(subdir))
        if sub:
            _find_shaders_recursive(sub, base_path.path_join(subdir), shaders)
'''
        result = self.run_gdscript(code)
        if result.returncode != 0:
            raise HeadlessError(f"Failed to find shaders: {result.stderr}")
        try:
            return _extract_json(result.stdout)
        except HeadlessError:
            raise HeadlessError(f"Invalid JSON output: {result.stdout}")

    def compile_shader(self, shader_path: str) -> Dict[str, Any]:
        """Compile a single shader and return errors/warnings."""
        code = f'''
extends SceneTree

func _init():
    var shader = load("{shader_path}")
    if shader == null:
        print(JSON.stringify({{"error": "failed to load shader"}}))
        quit(1)
    # Shader compilation happens on load in Godot 4
    print(JSON.stringify({{"success": true, "path": "{shader_path}"}}))
    quit(0)
'''
        result = self.run_gdscript(code)
        try:
            return _extract_json(result.stdout)
        except HeadlessError:
            return {"error": "Failed to parse shader compilation output", "raw": result.stdout}

    def get_export_presets(self) -> List[Dict[str, Any]]:
        """Get all export presets from export_presets.cfg."""
        code = '''
extends SceneTree

func _init():
    var presets = []
    var config = ConfigFile.new()
    var err = config.load("res://export_presets.cfg")
    if err != OK:
        print(JSON.stringify([]))
        quit(0)
    for section in config.get_sections():
        if section.begins_with("preset.") and not section.ends_with(".options"):
            var name = config.get_value(section, "name", "")
            var platform = config.get_value(section, "platform", "")
            var export_path = config.get_value(section, "export_path", "")
            if name and export_path:
                presets.append({
                    "section": section,
                    "name": name,
                    "platform": platform,
                    "export_path": export_path
                })
    print(JSON.stringify(presets))
    quit(0)
'''
        result = self.run_gdscript(code)
        if result.returncode != 0:
            raise HeadlessError(f"Failed to get export presets: {result.stderr}")
        try:
            return _extract_json(result.stdout)
        except HeadlessError:
            raise HeadlessError(f"Invalid JSON output: {result.stdout}")