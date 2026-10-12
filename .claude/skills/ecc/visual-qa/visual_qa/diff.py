"""
diff — Compare current screenshots vs baseline using perceptual hash.
"""
import argparse
import sys
from pathlib import Path

from .config import SHOT_NAMES
from .phash import (
    compute_phash,
    get_shot_paths,
    load_manifest,
    phash_distance,
)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Diff current screenshots against baseline")
    parser.add_argument("--threshold", type=int, help="Phash distance threshold (default 8)")
    parser.add_argument("--fail-on-diff", action="store_true", help="Exit 1 if any shot exceeds threshold")
    parser.add_argument("--json", action="store_true", help="Output JSON summary")
    args = parser.parse_args(argv)

    manifest = load_manifest()
    if not manifest:
        print("[diff] ERROR: No baseline manifest found. Run capture first.", file=sys.stderr)
        return 1

    threshold = args.threshold or manifest.get("threshold", 8)
    shots = manifest.get("shots", {})

    print(f"[diff] Comparing {len(SHOT_NAMES)} shots against baseline (threshold={threshold})")

    failures = []
    results = []

    for shot in SHOT_NAMES:
        baseline_path, current_path = get_shot_paths(shot)

        if shot not in shots:
            status = "MISSING_BASELINE"
            dist = -1
        elif not current_path.exists():
            status = "MISSING_CURRENT"
            dist = -1
        elif not baseline_path.exists():
            status = "MISSING_BASELINE_FILE"
            dist = -1
        else:
            current_hash = compute_phash(current_path)
            baseline_hash = shots[shot]["phash"]
            dist = phash_distance(current_hash, baseline_hash)
            status = "FAIL" if dist > threshold else "PASS"
            if status == "FAIL":
                failures.append((shot, dist))

        mark = "✗" if status == "FAIL" else "✓" if status == "PASS" else "?"
        print(f"  {shot:30s} dist={dist:3d}  {mark}  {status}")
        results.append({"shot": shot, "distance": dist, "status": status})

    if failures:
        print(f"\n[diff] {len(failures)}/{len(SHOT_NAMES)} shots exceeded threshold:")
        for shot, dist in failures:
            print(f"  {shot}: distance {dist}")
    else:
        print(f"\n[diff] All {len(SHOT_NAMES)} shots within threshold ✓")

    if args.json:
        import json
        print(json.dumps({"threshold": threshold, "results": results, "failures": failures}, indent=2))

    if args.fail_on_diff and failures:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())