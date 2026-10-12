#!/usr/bin/env python3
"""Visual diff for design token sync: compare before/after panel screenshots using visual-qa."""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

try:
    from .config import SCREENSHOT_AFTER, SCREENSHOT_BASE, SCREENSHOT_BEFORE, THRESHOLD
except ImportError:
    # Running as standalone script
    sys.path.insert(0, str(Path(__file__).parent))
    from config import SCREENSHOT_AFTER, SCREENSHOT_BASE, SCREENSHOT_BEFORE, THRESHOLD


def run_visual_qa_diff(before_dir: Path, after_dir: Path, threshold: int) -> tuple[int, str]:
    """Run visual_qa.diff comparing before vs after."""
    repo_root = Path(__file__).resolve().parent.parent.parent.parent
    visual_qa_dir = repo_root / ".claude" / "skills" / "ecc" / "visual-qa" / "visual_qa"
    
    # Temporarily symlink before as baseline, after as current
    baseline_link = repo_root / "godot" / "screenshots" / "baseline"
    current_link = repo_root / "godot" / "screenshots" / "game"
    
    # Backup existing
    baseline_backup = None
    current_backup = None
    
    if baseline_link.exists() or baseline_link.is_symlink():
        baseline_backup = baseline_link.rename(baseline_link.with_suffix(".bak"))
    if current_link.exists() or current_link.is_symlink():
        current_backup = current_link.rename(current_link.with_suffix(".bak"))
    
    try:
        baseline_link.symlink_to(before_dir)
        current_link.symlink_to(after_dir)
        
        cmd = [
            sys.executable, "-m", "visual_qa.diff",
            "--threshold", str(threshold),
            "--fail-on-diff"
        ]
        
        print(f"[design-token-sync] Running visual-qa diff (threshold={threshold})...")
        result = subprocess.run(cmd, capture_output=True, text=True, cwd=repo_root)
        
        output = result.stdout + result.stderr
        return result.returncode, output
    finally:
        # Restore
        baseline_link.unlink(missing_ok=True)
        current_link.unlink(missing_ok=True)
        if baseline_backup:
            baseline_backup.rename(baseline_link)
        if current_backup:
            current_backup.rename(current_link)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Visual diff for design token sync panels")
    parser.add_argument("--threshold", type=int, default=THRESHOLD, help=f"Phash distance threshold (default {THRESHOLD})")
    args = parser.parse_args(argv)
    
    if not SCREENSHOT_BEFORE.exists():
        print(f"[design-token-sync] ERROR: Before screenshots not found at {SCREENSHOT_BEFORE}", file=sys.stderr)
        print("[design-token-sync] Run: python -m design_token_sync.capture_panels --before", file=sys.stderr)
        return 1
    
    if not SCREENSHOT_AFTER.exists():
        print(f"[design-token-sync] ERROR: After screenshots not found at {SCREENSHOT_AFTER}", file=sys.stderr)
        print("[design-token-sync] Run: python -m design_token_sync.capture_panels --after", file=sys.stderr)
        return 1
    
    rc, output = run_visual_qa_diff(SCREENSHOT_BEFORE, SCREENSHOT_AFTER, args.threshold)
    
    print(output)
    
    if rc != 0:
        print(f"[design-token-sync] Visual regression detected (threshold={args.threshold})", file=sys.stderr)
        return 1
    
    print(f"[design-token-sync] Visual regression check passed ✓")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())