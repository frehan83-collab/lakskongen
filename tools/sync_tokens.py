#!/usr/bin/env python3
"""Sync design tokens into the Godot project and regenerate the UI theme.

The design system's source of truth is ``design/tokens.json``. The Godot project
root is ``godot/`` (``res://``):

1. ``design/tokens.json`` is copied to ``godot/theme/tokens.json``.
2. Headless Godot runs ``res://scripts/tools/build_theme.gd``, which builds the
   "Kyst & Stål" themes from the tokens and saves
   ``godot/theme/lakskongen_theme.tres`` (Ink, the project theme set in
   ``project.godot`` ``[gui] theme/custom``), ``lakskongen_chart_theme.tres``
   (Chart paper, document panels) and the fonts under ``godot/theme/fonts/``.

Each generated theme carries the SHA-256 of ``tokens.json`` and of the builder
sources, so ``--check`` detects a stale theme without starting Godot.

Usage:
    python3 tools/sync_tokens.py            # write/sync (same as --write)
    python3 tools/sync_tokens.py --write    # copy tokens + rebuild the theme (needs Godot)
    python3 tools/sync_tokens.py --check    # CI guard: non-zero exit on any drift

Environment:
    GODOT_BIN   Godot 4 mono binary (auto-detected otherwise).

Only the Python standard library is used.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
GODOT_DIR = REPO_ROOT / "godot"
SOURCE = REPO_ROOT / "design" / "tokens.json"
TARGET = GODOT_DIR / "theme" / "tokens.json"
PROJECT_FILE = GODOT_DIR / "project.godot"
PROJECT_THEME_RES = "res://theme/lakskongen_theme.tres"
THEMES = {
    "ink": GODOT_DIR / "theme" / "lakskongen_theme.tres",
    "chart": GODOT_DIR / "theme" / "lakskongen_chart_theme.tres",
}
BUILD_SCRIPT_RES = "res://scripts/tools/build_theme.gd"
# Mirrors build_theme.gd BUILDER_SOURCES (order matters: the hash is over the
# concatenated bytes).
BUILDER_SOURCES = [
    GODOT_DIR / "theme" / "theme_tokens.gd",
    GODOT_DIR / "theme" / "theme_style.gd",
    GODOT_DIR / "theme" / "theme_controls.gd",
    GODOT_DIR / "theme" / "theme_builder.gd",
    GODOT_DIR / "scripts" / "tools" / "build_theme.gd",
]
BUILD_TIMEOUT_S = 600


def _rel(path: Path) -> str:
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def tokens_sha256() -> str:
    return hashlib.sha256(TARGET.read_bytes()).hexdigest()


def builder_sha256() -> str:
    digest = hashlib.sha256()
    for path in BUILDER_SOURCES:
        digest.update(path.read_bytes())
    return digest.hexdigest()


def theme_stamp(path: Path) -> dict[str, str]:
    """The metadata a generated theme was stamped with (empty when missing)."""
    if not path.exists():
        return {}
    text = path.read_text(encoding="utf-8")
    return dict(re.findall(r'^metadata/(\w+) = "([^"]*)"$', text, flags=re.M))


def project_theme_setting() -> str:
    """The res:// path in project.godot [gui] theme/custom ("" when unset)."""
    if not PROJECT_FILE.exists():
        return ""
    section = ""
    for line in PROJECT_FILE.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if stripped.startswith("[") and stripped.endswith("]"):
            section = stripped
        elif section == "[gui]" and stripped.startswith("theme/custom="):
            return stripped.split("=", 1)[1].strip().strip('"')
    return ""


def drift_messages() -> list[str]:
    """Every reason the synced tokens or the generated theme are stale."""
    problems: list[str] = []
    if not SOURCE.exists():
        return [f"{_rel(SOURCE)} is missing"]
    if not TARGET.exists():
        problems.append(f"{_rel(TARGET)} is missing")
    elif SOURCE.read_bytes() != TARGET.read_bytes():
        problems.append(f"{_rel(TARGET)} differs from {_rel(SOURCE)}")
    if project_theme_setting() != PROJECT_THEME_RES:
        problems.append(f"{_rel(PROJECT_FILE)} [gui] theme/custom is not {PROJECT_THEME_RES}")
    if problems:
        return problems
    want_tokens, want_builder = tokens_sha256(), builder_sha256()
    for surface, path in THEMES.items():
        stamp = theme_stamp(path)
        if not stamp:
            problems.append(f"{_rel(path)} is missing")
            continue
        if stamp.get("surface") != surface:
            problems.append(f"{_rel(path)} is stamped surface={stamp.get('surface')!r}, expected {surface!r}")
        if stamp.get("tokens_sha256") != want_tokens:
            problems.append(f"{_rel(path)} was built from different tokens")
        if stamp.get("builder_sha256") != want_builder:
            problems.append(f"{_rel(path)} was built by a different theme builder")
    return problems


def find_godot() -> str | None:
    env = os.environ.get("GODOT_BIN", "")
    if env and os.access(env, os.X_OK):
        return env
    for name in ("godot-mono", "godot", "godot4", "Godot"):
        found = shutil.which(name)
        if found:
            return found
    for candidate in (
        "/Applications/Godot_mono.app/Contents/MacOS/Godot",
        "/Applications/Godot.app/Contents/MacOS/Godot",
        str(Path.home() / "Applications/Godot_mono.app/Contents/MacOS/Godot"),
    ):
        if os.access(candidate, os.X_OK):
            return candidate
    return None


def build_theme() -> int:
    godot = find_godot()
    if godot is None:
        print("ERROR: Godot not found; set GODOT_BIN to rebuild the theme.", file=sys.stderr)
        return 1
    # The game's MCP runtime would publish a session token at the live
    # editor's path; sandbox it like tools/ci.sh does.
    env = dict(os.environ)
    env["GODOT_MCP_TOKEN_PATH"] = str(Path(tempfile.mkdtemp(prefix="lakskongen-mcp-token.")) / "mcp_token")
    cmd = [godot, "--headless", "--path", str(GODOT_DIR), "--script", BUILD_SCRIPT_RES]
    print(f"Building theme: {' '.join(cmd)}")
    try:
        result = subprocess.run(cmd, env=env, capture_output=True, text=True, timeout=BUILD_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        print(f"ERROR: theme build timed out after {BUILD_TIMEOUT_S}s", file=sys.stderr)
        return 1
    for line in result.stdout.splitlines():
        if line.startswith("build_theme:"):
            print(line)
    if result.returncode != 0:
        print(result.stdout[-4000:], file=sys.stderr)
        print(result.stderr[-4000:], file=sys.stderr)
        print(f"ERROR: theme build failed (exit {result.returncode})", file=sys.stderr)
        return 1
    return 0


def write() -> int:
    if not SOURCE.exists():
        print(f"ERROR: token source not found: {_rel(SOURCE)}", file=sys.stderr)
        return 1
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_bytes(SOURCE.read_bytes())
    print(f"Synced {_rel(SOURCE)} -> {_rel(TARGET)}")
    rc = build_theme()
    if rc != 0:
        return rc
    return check()


def check() -> int:
    problems = drift_messages()
    if not problems:
        print(f"OK: {_rel(TARGET)} is in sync with {_rel(SOURCE)}; generated themes are current")
        return 0
    for problem in problems:
        print(f"DRIFT: {problem}", file=sys.stderr)
    print("Run: python3 tools/sync_tokens.py --write", file=sys.stderr)
    return 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Sync design/tokens.json into the Godot project and rebuild the theme.")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="exit non-zero if the tokens or the generated theme drifted (CI guard)")
    mode.add_argument("--write", action="store_true", help="copy design/tokens.json and rebuild the theme (default)")
    args = parser.parse_args(argv)
    if args.check:
        return check()
    return write()


if __name__ == "__main__":
    raise SystemExit(main())
