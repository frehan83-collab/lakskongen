#!/usr/bin/env python3
"""Extended sync: design/tokens.json → godot/theme/tokens.json → LakskongenTheme.reload_tokens()"""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

try:
    from .config import DESIGN_TOKENS, GODOT_TOKENS, GODOT_BIN, THEME_SCRIPT, REPO_ROOT
except ImportError:
    # Running as standalone script
    sys.path.insert(0, str(Path(__file__).parent))
    from config import DESIGN_TOKENS, GODOT_TOKENS, GODOT_BIN, THEME_SCRIPT, REPO_ROOT


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


def reload_theme_headless() -> int:
    """Invoke LakskongenTheme.reload_tokens() via headless Godot."""
    if not Path(GODOT_BIN).exists():
        print(f"[design-token-sync] ERROR: Godot not found at {GODOT_BIN}", file=sys.stderr)
        print("[design-token-sync] Set GODOT_BIN environment variable", file=sys.stderr)
        return 1
    
    project_path = REPO_ROOT / "godot"
    
    cmd = [
        GODOT_BIN,
        "--headless",
        "--path", str(project_path),
        "-s", "res://theme/default_theme.gd",
        "--reload-tokens",
        "--quit"
    ]
    
    print(f"[design-token-sync] Invoking LakskongenTheme.reload_tokens() via headless Godot...")
    print(f"[design-token-sync] Command: {' '.join(cmd)}")
    
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
    
    if result.stdout:
        for line in result.stdout.strip().split('\n'):
            print(f"[design-token-sync] {line}")
    if result.stderr:
        for line in result.stderr.strip().split('\n'):
            print(f"[design-token-sync] {line}", file=sys.stderr)
    
    if result.returncode != 0:
        print(f"[design-token-sync] ERROR: Godot exited with code {result.returncode}", file=sys.stderr)
        return 1
    
    print("[design-token-sync] Theme reloaded successfully ✓")
    return 0


def verify_reload() -> int:
    """Verify tokens were loaded by checking theme state via headless Godot."""
    if not Path(GODOT_BIN).exists():
        return 0  # Skip if no Godot
    
    project_path = REPO_ROOT / "godot"
    
    cmd = [
        GODOT_BIN,
        "--headless",
        "--path", str(project_path),
        "-s", "res://theme/default_theme.gd",
        "--check-tokens",
        "--quit"
    ]
    
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
    return result.returncode


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Sync design tokens and reload Godot theme")
    parser.add_argument("--write", action="store_true", help="Write sync (default)")
    parser.add_argument("--check", action="store_true", help="Check only (no write, no reload)")
    args = parser.parse_args(argv)
    
    if args.check:
        return run_sync_tokens(write=False)
    
    # Full sync: write + reload + verify
    rc = run_sync_tokens(write=True)
    if rc != 0:
        return rc
    
    rc = reload_theme_headless()
    if rc != 0:
        return rc
    
    rc = verify_reload()
    if rc != 0:
        print("[design-token-sync] WARNING: Token verification failed", file=sys.stderr)
        return 1
    
    print("[design-token-sync] Sync complete ✓")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())