#!/usr/bin/env python3
"""Sync design tokens into the Godot project.

The design system's source of truth is ``design/tokens.json``. The Godot project
root is ``godot/`` (``res://``), so the file is copied to
``godot/theme/tokens.json`` where ``LakskongenTheme`` loads it at runtime and
derives the UI theme. This keeps art and code from drifting.

Usage:
    python3 tools/sync_tokens.py            # write/sync (same as --write)
    python3 tools/sync_tokens.py --write    # write/sync
    python3 tools/sync_tokens.py --check    # CI guard: non-zero exit on drift

Only the Python standard library is used.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SOURCE = REPO_ROOT / "design" / "tokens.json"
TARGET = REPO_ROOT / "godot" / "theme" / "tokens.json"


def _rel(path: Path) -> str:
    try:
        return str(path.relative_to(REPO_ROOT))
    except ValueError:
        return str(path)


def in_sync() -> bool:
    if not SOURCE.exists() or not TARGET.exists():
        return False
    return SOURCE.read_bytes() == TARGET.read_bytes()


def write() -> int:
    if not SOURCE.exists():
        print(f"ERROR: token source not found: {_rel(SOURCE)}", file=sys.stderr)
        return 1
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_bytes(SOURCE.read_bytes())
    print(f"Synced {_rel(SOURCE)} -> {_rel(TARGET)}")
    return 0


def check() -> int:
    if in_sync():
        print(f"OK: {_rel(TARGET)} is in sync with {_rel(SOURCE)}")
        return 0
    if not TARGET.exists():
        print(f"DRIFT: {_rel(TARGET)} is missing", file=sys.stderr)
    else:
        print(f"DRIFT: {_rel(TARGET)} differs from {_rel(SOURCE)}", file=sys.stderr)
    print("Run: python3 tools/sync_tokens.py --write", file=sys.stderr)
    return 1


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Sync design/tokens.json into the Godot project.")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="exit non-zero if the synced copy differs (CI guard)")
    mode.add_argument("--write", action="store_true", help="copy design/tokens.json to godot/theme/tokens.json (default)")
    args = parser.parse_args(argv)
    if args.check:
        return check()
    return write()


if __name__ == "__main__":
    raise SystemExit(main())
