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

The compile reads Godot's stdout and stderr as one stream: Ubuntu's xvfb-run
runs its command as `"$@" 2>&1`, so on the CI runner the probe's markers and
Godot's errors all arrive on stdout. A run that ends before the last shader
(crash, signal, timeout) is reported with its exit status, the shader on
screen and the last lines of output, and the shaders after that one go to a
fresh run, so one bad shader cannot hide the others.
"""

import os
import re
import shutil
import signal
import sys
import time
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from autoload_audit import is_git_ignored
from headless_runner import HeadlessError
from shader_hints import check_project_hints, find_shader_files, res_path

PROBE = Path(__file__).resolve().parent / "shader_compile_probe.gd"
# Budget for the whole compile, every run included. One run over the 18
# project shaders takes ~5 s on Metal and took ~60 s under Xvfb with Mesa's
# software renderer on GitHub's runner.
COMPILE_TIMEOUT_S = 600
# The audio driver is irrelevant to shaders and a CI runner has no sound card.
WINDOW_ARGS = ["--windowed", "--resolution", "320x180", "--audio-driver", "Dummy"]
OUTPUT_TAIL_LINES = 40

STOPPED_EARLY = "the compile run stopped before it reached every shader"

_BEGIN = "SHADER_CHECK_BEGIN "
_END = "SHADER_CHECK_END "
_LOAD_FAILED = "SHADER_CHECK_LOAD_FAILED "
_NOT_DRAWN = "SHADER_CHECK_NOT_DRAWN "
_DONE = re.compile(r"SHADER_CHECK_DONE count=(\d+)")
_SHADER_ERROR = re.compile(r"SHADER ERROR: (.*)")
_ANY_ERROR = re.compile(r"^\s*(?:SCRIPT )?ERROR: ")
# Godot prints the location under the error: "at: (null) (res://x.gdshader:13)".
_LOCATION = re.compile(r"\((res://[^()]+?):(\d+)\)")
_OUTSIDE = "(outside any shader)"
# "Vulkan 1.4.318 - Forward+ - Using Device #0: Mesa - llvmpipe (...)",
# "OpenGL API 4.5 (Core Profile) Mesa 25.2.8 - Compatibility - Using Device: ..."
_RENDERER = re.compile(r"^(?:Vulkan|Metal|D3D12|OpenGL)\b.* - (Forward\+|Mobile|Compatibility) - Using Device")
_AUTOLOAD_FAILED = re.compile(r"Failed to instantiate an autoload, can't load from path: (res://\S+?)\.?$",
                              re.MULTILINE)
# project.godot's rendering/renderer/rendering_method -> the name Godot prints.
_METHOD_NAMES = {"forward_plus": "Forward+", "mobile": "Mobile", "gl_compatibility": "Compatibility"}


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
    """Errors in the probe's output: every SHADER ERROR, attributed to its
    shader, any other error while a shader is being drawn, a shader that was
    never drawn, and a run that did not get through every shader."""
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
        elif line.startswith(_NOT_DRAWN):
            path, _, detail = line[len(_NOT_DRAWN):].strip().partition(" ")
            errors.append(f"{path}: never drawn ({detail})")
            flagged.add(path)
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
        errors.append(STOPPED_EARLY)
    elif done != expected:
        errors.append(f"the compile run covered {done} of {expected} shaders")
    return errors


def compile_progress(log: str) -> Tuple[List[str], Optional[str], bool]:
    """How far a run got: the shaders it began, the one still on screen when
    the output ends (None between shaders), and whether it finished."""
    begun: List[str] = []
    on_screen: Optional[str] = None
    finished = False
    for line in log.splitlines():
        if line.startswith(_BEGIN):
            on_screen = line[len(_BEGIN):].strip()
            begun.append(on_screen)
        elif line.startswith(_END):
            on_screen = None
        elif _DONE.search(line):
            finished = True
    return begun, on_screen, finished


def renderer_line(log: str) -> Optional[str]:
    """The line where Godot names the renderer it drew with, if any."""
    return next((line.strip() for line in log.splitlines() if _RENDERER.match(line.strip())), None)


def project_rendering_method(project: Path) -> str:
    """The renderer the game runs with (rendering/renderer/rendering_method,
    Godot's default forward_plus when project.godot does not set it)."""
    try:
        text = (project / "project.godot").read_text(encoding="utf-8")
    except OSError:
        return "forward_plus"
    section = None
    for raw in text.splitlines():
        line = raw.strip()
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1]
        elif section == "rendering" and line.startswith("renderer/rendering_method="):
            return line.split("=", 1)[1].strip().strip('"')
    return "forward_plus"


def describe_exit(code: Optional[int], timeout_s: float) -> str:
    """A run's end in words: exit code, signal, or timeout."""
    if code is None:
        return f"timed out after {timeout_s:.0f} s and was killed"
    if code < 0:
        return f"killed by {_signal_name(-code) or f'signal {-code}'}"
    # xvfb-run is a shell script: a child killed by signal N exits 128+N.
    name = _signal_name(code - 128) if code > 128 else None
    if name:
        return f"exit code {code}, i.e. killed by {name} (as a shell reports it)"
    return f"exit code {code}"


def _signal_name(number: int) -> Optional[str]:
    try:
        return signal.Signals(number).name
    except ValueError:
        return None


def _output_of(result) -> str:
    """The run's whole output: run_with_display merges stderr into stdout."""
    return (getattr(result, "stdout", None) or "") + (getattr(result, "stderr", None) or "")


def _tail(log: str) -> List[str]:
    return [line.rstrip() for line in log.splitlines()[-OUTPUT_TAIL_LINES:]]


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
        compile_data: Dict[str, Any] = {}
        if prefix is None:
            warnings.append(
                "No display (no DISPLAY or WAYLAND_DISPLAY, no xvfb-run): "
                "the shaders were not compiled, only their hints were checked."
            )
        elif shaders:
            compile_errors, compile_warnings, compile_data = self._compile(shaders, prefix)
            errors.extend(compile_errors)
            warnings.extend(compile_warnings)

        return {
            "success": not errors,
            "skipped": prefix is None,
            "data": {
                "shaders_checked": len(shaders),
                "shader_files": shaders,
                "render_compile": "ran" if prefix is not None and shaders else "skipped",
                **compile_data,
            },
            "errors": errors,
            "warnings": warnings
        }

    def _compile(self, shaders: List[str], prefix: List[str]) -> Tuple[List[str], List[str], Dict[str, Any]]:
        """Draw every shader, in as many runs as it takes: a run that ends
        early is reported and the shaders after the one on screen go to a
        new run, within one COMPILE_TIMEOUT_S budget."""
        project = self.ops.project_path
        errors: List[str] = []
        runs: List[Dict[str, Any]] = []
        logs: List[str] = []
        tail: List[str] = []
        deadline = time.monotonic() + COMPILE_TIMEOUT_S
        remaining = list(shaders)

        while remaining:
            budget = deadline - time.monotonic()
            if budget <= 0:
                errors.append(f"the {COMPILE_TIMEOUT_S} s compile budget ran out before these shaders were "
                              f"drawn: {', '.join(remaining)}")
                break
            started = time.monotonic()
            args = WINDOW_ARGS + ["--script", str(PROBE), "--"] + remaining
            result = self.ops.headless.run_with_display(args, prefix, timeout=budget)
            log = _output_of(result)
            logs.append(log)
            exit_text = describe_exit(result.returncode, budget)
            begun, on_screen, finished = compile_progress(log)
            runs.append({
                "shaders": len(remaining),
                "reached": begun[-1] if begun else None,
                "finished": finished,
                "exit": exit_text,
                "seconds": round(time.monotonic() - started, 1),
            })
            errors.extend(e for e in parse_compile_log(log, len(remaining)) if e != STOPPED_EARLY)
            if finished and result.returncode == 0:
                break
            tail = _tail(log)
            if finished:
                errors.append(f"Godot ended the compile run with {exit_text} after it reached every shader "
                              f"(last output in data.output_tail)")
                break
            last = on_screen or (begun[-1] if begun else None)
            if last is None or last not in remaining:
                errors.append(f"{STOPPED_EARLY}: it ended before the first shader with {exit_text} "
                              f"(last output in data.output_tail)")
                break
            where = "while it was on screen" if on_screen else "right after it was drawn"
            errors.append(f"{last}: the compile run ended {where}, with {exit_text} "
                          f"(last output in data.output_tail)")
            remaining = remaining[remaining.index(last) + 1:]

        whole_log = "\n".join(logs)
        renderer = renderer_line(whole_log)
        errors.extend(self._renderer_errors(renderer, project_rendering_method(project)))
        data: Dict[str, Any] = {"renderer": renderer, "compile_runs": runs}
        if tail:
            data["output_tail"] = tail
        return errors, self._autoload_warnings(whole_log), data

    @staticmethod
    def _renderer_errors(renderer: Optional[str], method: str) -> List[str]:
        """A fallback to another renderer (e.g. Vulkan missing, Godot drops to
        OpenGL) compiles the shaders with a different compiler than the game
        uses, so it fails the check instead of passing it quietly."""
        expected = _METHOD_NAMES.get(method)
        match = _RENDERER.match(renderer) if renderer else None
        if expected is None or match is None or match.group(1) == expected:
            return []
        return [f"Godot drew with the {match.group(1)} renderer ('{renderer}'), not the project's "
                f"{expected}: the shaders were not compiled the way the game compiles them"]

    def _autoload_warnings(self, log: str) -> List[str]:
        """Autoloads that failed to load during the compile run. The MCP
        runtime is git-ignored local tooling, absent from a clean checkout."""
        warnings: List[str] = []
        for path in dict.fromkeys(_AUTOLOAD_FAILED.findall(log)):
            if is_git_ignored(self.ops.project_path, path):
                warnings.append(f"Autoload '{path}' is optional local tooling that is not installed "
                                f"(git-ignored); the compile run went on without it")
            else:
                warnings.append(f"Autoload '{path}' failed to load during the compile run")
        return warnings
