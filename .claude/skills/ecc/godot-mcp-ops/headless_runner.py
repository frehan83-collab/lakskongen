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
import signal
from pathlib import Path
from typing import Any, Dict, List, Optional


class HeadlessError(Exception):
    """Raised when headless Godot execution fails."""
    pass


KILL_GRACE_S = 5


def _kill_tree(proc: subprocess.Popen) -> None:
    """End `proc` and everything it started: SIGTERM to its process group
    (xvfb-run, its Xvfb and Godot), SIGKILL after a grace period."""
    if os.name != "posix":
        proc.kill()
        return
    for sig, grace in ((signal.SIGTERM, KILL_GRACE_S), (signal.SIGKILL, None)):
        try:
            os.killpg(proc.pid, sig)
        except ProcessLookupError:
            return
        if grace is None:
            return
        try:
            proc.wait(timeout=grace)
            return
        except subprocess.TimeoutExpired:
            continue


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
        return self._execute(cmd, capture_output, timeout)

    def run_with_display(self, args: List[str], prefix: List[str], timeout: float = 300) -> subprocess.CompletedProcess:
        """Run Godot with a real renderer (no --headless). `prefix` supplies a
        display where there is none, e.g. ["xvfb-run", "-a"].

        stdout and stderr come back as ONE stream in `result.stdout` (stderr
        is None), in the order Godot wrote them. Ubuntu's xvfb-run runs its
        command as `"$@" 2>&1`, so on a Linux runner everything arrives on
        stdout anyway; reading only stderr there saw nothing at all.

        On a timeout the whole process tree (xvfb-run, Xvfb, Godot) is killed
        and the result has returncode None and the output written so far, so
        the caller can still tell where the run stopped.
        """
        cmd = prefix + [self.godot_bin, "--path", str(self.project_path)] + args
        env = os.environ.copy()
        env["TERM"] = "dumb"  # Avoid ANSI codes in output
        try:
            proc = subprocess.Popen(
                cmd,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                encoding="utf-8",
                errors="replace",
                env=env,
                # Its own process group, so a timeout reaches Godot and Xvfb
                # too and not only the xvfb-run shell.
                start_new_session=os.name == "posix",
            )
        except FileNotFoundError:
            raise HeadlessError(f"Godot binary not found: {cmd[0]}")
        except OSError as e:
            raise HeadlessError(f"Failed to run Godot: {e}")

        try:
            output, _ = proc.communicate(timeout=timeout)
            return subprocess.CompletedProcess(cmd, proc.returncode, output, None)
        except subprocess.TimeoutExpired:
            _kill_tree(proc)
            output, _ = proc.communicate()
            return subprocess.CompletedProcess(cmd, None, output, None)

    def _execute(self, cmd: List[str], capture_output: bool, timeout: int) -> subprocess.CompletedProcess:
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
            raise HeadlessError(f"Godot timed out after {timeout}s: {e}")
        except FileNotFoundError:
            raise HeadlessError(f"Godot binary not found: {self.godot_bin}")
        except Exception as e:
            raise HeadlessError(f"Failed to run Godot: {e}")

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