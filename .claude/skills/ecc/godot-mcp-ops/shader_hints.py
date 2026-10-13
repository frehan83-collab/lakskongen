#!/usr/bin/env python3
"""
shader_hints.py — static check of the uniform hints in .gdshader files.

The --headless dummy renderer never parses shader code, so a shader with an
unknown hint (review F958: `hint_default_noise`) loads there without a word.
This check needs no Godot: it reads every uniform declaration and rejects any
hint the Godot 4.7 shading language does not define.
"""

import os
import re
from pathlib import Path
from typing import Iterator, List, Tuple

# The uniform hint keywords of the Godot 4.7.2 shading language (checked
# against the strings in the 4.7.2 binary; extend this when Godot adds one).
KNOWN_HINTS = frozenset({
    "source_color", "hint_range", "hint_enum", "instance_index",
    "hint_normal", "hint_anisotropy",
    "hint_default_white", "hint_default_black", "hint_default_transparent",
    "hint_roughness_r", "hint_roughness_g", "hint_roughness_b", "hint_roughness_a",
    "hint_roughness_normal", "hint_roughness_gray",
    "hint_screen_texture", "hint_depth_texture", "hint_normal_roughness_texture",
    "hint_blit_source0", "hint_blit_source1", "hint_blit_source2", "hint_blit_source3",
    "filter_nearest", "filter_linear", "filter_nearest_mipmap", "filter_linear_mipmap",
    "filter_nearest_mipmap_anisotropic", "filter_linear_mipmap_anisotropic",
    "repeat_enable", "repeat_disable",
})

SHADER_SUFFIXES = (".gdshader", ".gdshaderinc")

_UNIFORM = re.compile(r"\buniform\b([^;]*);")
_IDENTIFIER = re.compile(r"\s*([A-Za-z_]\w*)")


def find_shader_files(project: Path) -> List[Path]:
    """Every shader source under the project, skipping hidden dirs (.godot)."""
    found = []
    for root, dirs, files in os.walk(project):
        dirs[:] = [d for d in dirs if not d.startswith(".")]
        found.extend(Path(root) / f for f in files if f.endswith(SHADER_SUFFIXES))
    return sorted(found)


def res_path(project: Path, path: Path) -> str:
    return "res://" + path.relative_to(project).as_posix()


def _top_level(text: str, targets: str, start: int = 0) -> Iterator[int]:
    """Indexes of `targets` characters outside (...) and "..." from `start`."""
    depth, quoted = 0, False
    for i in range(start, len(text)):
        ch = text[i]
        if ch == '"':
            quoted = not quoted
        elif quoted:
            continue
        elif ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        elif depth == 0 and ch in targets:
            yield i


def _hints(declaration: str) -> List[Tuple[int, str]]:
    """(offset, text) of each hint after the ':' of a uniform declaration."""
    colon = next(_top_level(declaration, ":"), -1)
    if colon < 0:
        return []
    end = next(_top_level(declaration, "=", colon + 1), len(declaration))
    section = declaration[:end]
    hints, start = [], colon + 1
    for comma in [*_top_level(section, ",", colon + 1), end]:
        hints.append((start, section[start:comma]))
        start = comma + 1
    return hints


def strip_comments(text: str) -> str:
    """Drop // and /* */ comments outside string literals (hint_enum("a//b")
    is not a comment), keeping every newline so line numbers stay right."""
    out, i, n, quoted = [], 0, len(text), False
    while i < n:
        ch = text[i]
        if quoted or ch == '"':
            quoted = (ch != '"') if quoted else True
            out.append(ch)
            i += 1
        elif text.startswith("//", i):
            end = text.find("\n", i)
            i = n if end < 0 else end
        elif text.startswith("/*", i):
            end = text.find("*/", i + 2)
            end = n if end < 0 else end + 2
            out.append("\n" * text.count("\n", i, end))
            i = end
        else:
            out.append(ch)
            i += 1
    return "".join(out)


def check_shader_text(text: str, label: str) -> List[str]:
    """One 'label:line: message' per unknown or malformed uniform hint."""
    code = strip_comments(text)
    errors = []
    for uniform in _UNIFORM.finditer(code):
        for offset, hint in _hints(uniform.group(1)):
            name = _IDENTIFIER.match(hint)
            at = uniform.start(1) + offset + (name.start(1) if name else 0)
            line = code.count("\n", 0, at) + 1
            if name is None:
                errors.append(f"{label}:{line}: malformed uniform hint '{hint.strip()}'")
            elif name.group(1) not in KNOWN_HINTS:
                errors.append(f"{label}:{line}: unknown uniform hint '{name.group(1)}'")
    return errors


def check_project_hints(project: Path) -> List[str]:
    errors = []
    for path in find_shader_files(project):
        text = path.read_text(encoding="utf-8")
        errors.extend(check_shader_text(text, res_path(project, path)))
    return errors
