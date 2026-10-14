#!/usr/bin/env python3
"""Visual diff for design token sync: before/ vs after/ UI shots by perceptual hash.

Compares the two capture directories directly. It never touches
godot/screenshots/baseline (the project's reviewed goldens).
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

try:
    from .config import SCREENSHOT_AFTER, SCREENSHOT_BASE, SCREENSHOT_BEFORE, THRESHOLD, UI_SHOTS
except ImportError:
    # Running as standalone script
    sys.path.insert(0, str(Path(__file__).parent))
    from config import SCREENSHOT_AFTER, SCREENSHOT_BASE, SCREENSHOT_BEFORE, THRESHOLD, UI_SHOTS


def phash_distance(a: Path, b: Path) -> int:
    from PIL import Image
    import imagehash

    with Image.open(a) as ia, Image.open(b) as ib:
        return int(imagehash.phash(ia) - imagehash.phash(ib))  # numpy int64 -> int (JSON)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Visual diff for design token sync panels")
    parser.add_argument("--threshold", type=int, default=THRESHOLD, help=f"pHash distance threshold (default {THRESHOLD})")
    args = parser.parse_args(argv)

    for name, path in (("before", SCREENSHOT_BEFORE), ("after", SCREENSHOT_AFTER)):
        if not path.exists():
            print(f"[design-token-sync] ERROR: {name} screenshots not found at {path}", file=sys.stderr)
            print(f"[design-token-sync] Run: python -m design_token_sync.capture_panels --{name}", file=sys.stderr)
            return 1

    results = []
    for shot in UI_SHOTS:
        before, after = SCREENSHOT_BEFORE / f"{shot}.png", SCREENSHOT_AFTER / f"{shot}.png"
        if not before.exists() or not after.exists():
            results.append({"shot": shot, "distance": -1, "status": "MISSING"})
            continue
        dist = phash_distance(before, after)
        results.append({"shot": shot, "distance": dist, "status": "PASS" if dist <= args.threshold else "FAIL"})
    for r in results:
        print(f"  {r['shot']:28s} dist={r['distance']:3d}  {r['status']}")
    (SCREENSHOT_BASE / "visual_diff.json").write_text(json.dumps({"threshold": args.threshold, "results": results}, indent=2))
    failures = [r for r in results if r["status"] != "PASS"]
    if failures:
        print(f"[design-token-sync] {len(failures)}/{len(results)} UI shots changed beyond {args.threshold}", file=sys.stderr)
        return 1
    print(f"[design-token-sync] All {len(results)} UI shots within threshold ✓")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
