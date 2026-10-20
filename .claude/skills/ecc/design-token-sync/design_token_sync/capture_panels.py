#!/usr/bin/env python3
"""Capture the UI panels for the design-token visual regression.

Runs the real screenshot harness (tools/capture_screenshots.sh: non-headless,
the only way Godot renders) and copies the UI shots (01-07g: menus, every
document panel, the inbox) into the before/ or after/ directory. The 3D shots
are not affected by tokens and are skipped.

Usage:
    python -m design_token_sync.capture_panels --before   # before the token write
    python -m design_token_sync.capture_panels --after    # after sync_tokens --write
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path

try:
    from .config import GODOT_BIN, REPO_ROOT, SCREENSHOT_AFTER, SCREENSHOT_BEFORE, UI_SHOTS
except ImportError:
    # Running as standalone script
    sys.path.insert(0, str(Path(__file__).parent))
    from config import GODOT_BIN, REPO_ROOT, SCREENSHOT_AFTER, SCREENSHOT_BEFORE, UI_SHOTS

HARNESS = REPO_ROOT / "tools" / "capture_screenshots.sh"
HARNESS_OUT = REPO_ROOT / "godot" / "screenshots" / "game"
TIMEOUT_S = 1200


def capture(output_dir: Path) -> int:
    env = dict(os.environ)
    if Path(GODOT_BIN).exists():
        env["GODOT_BIN"] = GODOT_BIN
    print(f"[design-token-sync] Running {HARNESS.relative_to(REPO_ROOT)} ...")
    result = subprocess.run(["bash", str(HARNESS)], env=env, capture_output=True, text=True, timeout=TIMEOUT_S)
    if result.returncode != 0:
        print(result.stdout[-3000:], file=sys.stderr)
        print(result.stderr[-3000:], file=sys.stderr)
        print(f"[design-token-sync] ERROR: harness exited {result.returncode}", file=sys.stderr)
        return 1
    output_dir.mkdir(parents=True, exist_ok=True)
    missing = []
    for shot in UI_SHOTS:
        src = HARNESS_OUT / f"{shot}.png"
        if not src.exists():
            missing.append(shot)
            continue
        shutil.copy2(src, output_dir / src.name)
    (output_dir / "manifest.json").write_text(json.dumps({"version": 2, "shots": UI_SHOTS}, indent=2))
    if missing:
        print(f"[design-token-sync] ERROR: harness did not write {missing}", file=sys.stderr)
        return 1
    print(f"[design-token-sync] {len(UI_SHOTS)} UI shots -> {output_dir}")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Capture UI panels for visual regression")
    phase = parser.add_mutually_exclusive_group(required=True)
    phase.add_argument("--before", action="store_true", help="Capture before the token write (baseline)")
    phase.add_argument("--after", action="store_true", help="Capture after the token sync (test)")
    args = parser.parse_args(argv)
    return capture(SCREENSHOT_BEFORE if args.before else SCREENSHOT_AFTER)


if __name__ == "__main__":
    raise SystemExit(main())
