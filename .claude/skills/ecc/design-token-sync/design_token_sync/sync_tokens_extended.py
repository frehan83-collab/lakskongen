#!/usr/bin/env python3
"""Extended sync: design/tokens.json → godot/theme/tokens.json → regenerated Kyst & Stål theme.

tools/sync_tokens.py --write copies the tokens and runs headless Godot
(res://scripts/tools/build_theme.gd) to rebuild godot/theme/lakskongen_theme.tres
(the project theme) and lakskongen_chart_theme.tres; --check then verifies
the copies and the generated themes' SHA-256 stamps."""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

try:
    from .config import REPO_ROOT
except ImportError:
    # Running as standalone script
    sys.path.insert(0, str(Path(__file__).parent))
    from config import REPO_ROOT


def run_sync_tokens(write: bool = True) -> int:
    """Run the existing tools/sync_tokens.py script."""
    sync_script = REPO_ROOT / "tools" / "sync_tokens.py"
    
    cmd = [sys.executable, str(sync_script)]
    if write:
        cmd.append("--write")
    else:
        cmd.append("--check")
    
    print(f"[design-token-sync] Running: {' '.join(cmd)}")
    result = subprocess.run(cmd, capture_output=True, text=True)
    
    if result.stdout:
        print(result.stdout.strip())
    if result.stderr:
        print(result.stderr.strip(), file=sys.stderr)
    
    return result.returncode


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Sync design tokens and reload Godot theme")
    parser.add_argument("--write", action="store_true", help="Write sync (default)")
    parser.add_argument("--check", action="store_true", help="Check only (no write, no reload)")
    args = parser.parse_args(argv)
    
    if args.check:
        return run_sync_tokens(write=False)
    
    # Full sync: copy + rebuild the theme (headless Godot), then verify the stamps.
    rc = run_sync_tokens(write=True)
    if rc != 0:
        return rc

    rc = run_sync_tokens(write=False)
    if rc != 0:
        print("[design-token-sync] ERROR: generated theme does not match the tokens", file=sys.stderr)
        return 1

    print("[design-token-sync] Sync complete ✓")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())