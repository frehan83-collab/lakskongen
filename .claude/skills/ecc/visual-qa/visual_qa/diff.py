"""
diff — Compare current screenshots vs baseline using perceptual hash.
"""
import argparse
import sys
from pathlib import Path

from .config import SHOT_NAMES, THRESHOLD
from .phash import (
    compute_phash,
    get_shot_paths,
    load_manifest,
    phash_distance,
)
from .verdict import problems, resolve_threshold, shot_status


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Diff current screenshots against baseline")
    parser.add_argument("--threshold", type=int,
                        help=f"Phash distance threshold (default: the manifest's, else {THRESHOLD})")
    parser.add_argument("--fail-on-diff", action="store_true",
                        help="Exit 1 if any shot exceeds the threshold or is missing")
    parser.add_argument("--json", action="store_true", help="Output JSON summary")
    args = parser.parse_args(argv)

    manifest = load_manifest()
    if not manifest:
        print("[diff] ERROR: No baseline manifest found. Run capture first.", file=sys.stderr)
        return 1

    threshold = resolve_threshold(args.threshold, manifest, THRESHOLD)
    shots = manifest.get("shots", {})

    print(f"[diff] Comparing {len(SHOT_NAMES)} shots against baseline (threshold={threshold})")

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
            status = shot_status(dist, threshold)

        mark = "✗" if status == "FAIL" else "✓" if status == "PASS" else "?"
        print(f"  {shot:30s} dist={dist:3d}  {mark}  {status}")
        results.append({"shot": shot, "distance": dist, "status": status})

    failures = problems(results)
    if failures:
        print(f"\n[diff] {len(failures)}/{len(SHOT_NAMES)} shots failed (over the threshold or missing):")
        for r in failures:
            print(f"  {r['shot']}: {r['status']} (distance {r['distance']})")
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