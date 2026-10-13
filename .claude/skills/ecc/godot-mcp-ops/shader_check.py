#!/usr/bin/env python3
"""
shader_check.py — Shader Compilation Check

The old check only load()ed each shader under --headless, where the dummy
renderer parses nothing, so it could not fail (review F958). It has two parts:

1. A static check of every uniform hint against the Godot 4.7 list
   (shader_hints.py). It needs no Godot, so it always runs.
2. A compile under a real renderer (shader_compile_probe.gd): Godot opens a
   small window, draws each shader for a few frames, and every SHADER ERROR it
   prints fails the check. Without a display (Linux with neither DISPLAY nor
   xvfb-run) this part is skipped and the result says so ("skipped": true)
   instead of passing.
"""

import os
import re
import shutil
import sys
from pathlib import Path
from typing import Any, Dict, List, Optional

from headless_runner import HeadlessError
from shader_hints import check_project_hints, find_shader_files, res_path

PROBE = Path(__file__).resolve().parent / "shader_compile_probe.gd"
COMPILE_TIMEOUT_S = 300
WINDOW_ARGS = ["--windowed", "--resolution", "320x180"]

_BEGIN = "SHADER_CHECK_BEGIN "
_END = "SHADER_CHECK_END "
_LOAD_FAILED = "SHADER_CHECK_LOAD_FAILED "
_DONE = re.compile(r"SHADER_CHECK_DONE count=(\d+)")
_SHADER_ERROR = re.compile(r"SHADER ERROR: (.*)")
_ANY_ERROR = re.compile(r"^\s*(?:SCRIPT )?ERROR: ")
# Godot prints the location under the error: "at: (null) (res://x.gdshader:13)".
_LOCATION = re.compile(r"\((res://[^()]+?):(\d+)\)")
_OUTSIDE = "(outside any shader)"


def display_prefix() -> Optional[List[str]]:
    """Command prefix that gives Godot a display: [] when there is one,
    ["xvfb-run", "-a"] on a Linux box without one, None when neither works."""
    if sys.platform in ("darwin", "win32"):
        return []
    if os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY"):
        return []
    if shutil.which("xvfb-run"):
        return ["xvfb-run", "-a"]
    return None


def parse_compile_log(log: str, expected: int) -> List[str]:
    """Errors in the probe's stderr: every SHADER ERROR, attributed to its
    shader, any other error while a shader is being drawn, and a run that did
    not get through every shader."""
    errors: List[str] = []
    current: Optional[str] = None
    flagged = set()
    done = None
    lines = log.splitlines()
    for i, line in enumerate(lines):
        if line.startswith(_BEGIN):
            current = line[len(_BEGIN):].strip()
        elif line.startswith(_END):
            current = None
        elif line.startswith(_LOAD_FAILED):
            errors.append(f"{line[len(_LOAD_FAILED):].strip()}: failed to load")
        elif (match := _DONE.search(line)):
            done = int(match.group(1))
        elif (match := _SHADER_ERROR.search(line)):
            following = lines[i + 1] if i + 1 < len(lines) else ""
            location = _LOCATION.search(following)
            where = f"{location.group(1)}:{location.group(2)}" if location else current or _OUTSIDE
            errors.append(f"{where}: {match.group(1).strip()}")
            flagged.add(current)
        elif current is not None and current not in flagged and _ANY_ERROR.match(line):
            # Deny by default while a shader is on screen: the RD backend
            # reports a GLSL failure as "ERROR: Error compiling <Stage>
            # shader.", and a probe script error leaves the shader undrawn.
            errors.append(f"{current}: {line.strip()}")
            flagged.add(current)
    if done is None:
        errors.append("the compile run stopped before it reached every shader")
    elif done != expected:
        errors.append(f"the compile run covered {done} of {expected} shaders")
    return errors


class ShaderCheck:
    def __init__(self, ops):
        self.ops = ops

    def run(self) -> Dict[str, Any]:
        """Run the hint check and, when a display exists, the compile."""
        try:
            return self._check()
        except (HeadlessError, OSError) as e:
            return {
                "success": False,
                "data": {},
                "errors": [f"Shader check failed: {e}"],
                "warnings": []
            }

    def _check(self) -> Dict[str, Any]:
        project = self.ops.project_path
        shaders = [res_path(project, p) for p in find_shader_files(project) if p.suffix == ".gdshader"]
        errors = check_project_hints(project)
        warnings = []
        if not shaders:
            # A moved shader folder or a wrong --project must not pass.
            errors.append(f"No .gdshader files found under {project}")

        prefix = display_prefix()
        if prefix is None:
            warnings.append(
                "No display (no DISPLAY or WAYLAND_DISPLAY, no xvfb-run): "
                "the shaders were not compiled, only their hints were checked."
            )
        else:
            errors.extend(self._compile(shaders, prefix))

        return {
            "success": not errors,
            "skipped": prefix is None,
            "data": {
                "shaders_checked": len(shaders),
                "shader_files": shaders,
                "render_compile": "ran" if prefix is not None and shaders else "skipped",
            },
            "errors": errors,
            "warnings": warnings
        }

    def _compile(self, shaders: List[str], prefix: List[str]) -> List[str]:
        if not shaders:
            return []
        args = WINDOW_ARGS + ["--script", str(PROBE), "--"] + shaders
        result = self.ops.headless.run_with_display(args, prefix, timeout=COMPILE_TIMEOUT_S)
        errors = parse_compile_log(result.stderr or "", len(shaders))
        if result.returncode != 0:
            errors.append(f"Godot exited with code {result.returncode} during the compile run")
        return errors
