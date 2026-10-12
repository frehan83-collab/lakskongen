"""
baseline-update — Promote current screenshots to baseline with git commit.
"""
import argparse
import subprocess
import sys
from pathlib import Path

from .config import ROOT
from .phash import build_manifest, copy_current_to_baseline, save_manifest
from .capture import run_harness


def git_commit(message: str) -> int:
    """Commit baseline changes. Returns exit code."""
    baseline_dir = ROOT / "godot" / "screenshots" / "baseline"
    result = subprocess.run(["git", "add", str(baseline_dir)], cwd=ROOT)
    if result.returncode != 0:
        return result.returncode
    result = subprocess.run(["git", "commit", "-m", message], cwd=ROOT)
    return result.returncode


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Promote current screenshots to baseline")
    parser.add_argument("-m", "--message", default="chore: update visual baseline", help="Commit message")
    parser.add_argument("--skip-harness", action="store_true", help="Skip running capture_screenshots.sh")
    parser.add_argument("--threshold", type=int, help="Phash threshold for manifest")
    parser.add_argument("--no-commit", action="store_true", help="Update baseline but don't commit")
    args = parser.parse_args(argv)

    if not args.skip_harness:
        print("[baseline-update] Running screenshot harness...")
        rc = run_harness()
        if rc != 0:
            print(f"[baseline-update] Harness exited with code {rc}", file=sys.stderr)
            return rc

    print("[baseline-update] Copying current screenshots to baseline...")
    count = copy_current_to_baseline()
    if count == 0:
        print("[baseline-update] ERROR: No screenshots found in current dir", file=sys.stderr)
        return 1
    print(f"[baseline-update] Copied {count} screenshots")

    print("[baseline-update] Building manifest...")
    manifest = build_manifest(threshold=args.threshold)
    save_manifest(manifest)
    print(f"[baseline-update] Manifest saved with {len(manifest['shots'])} shots")

    if not args.no_commit:
        print("[baseline-update] Committing baseline changes...")
        rc = git_commit(args.message)
        if rc != 0:
            print(f"[baseline-update] Git commit failed (exit {rc})", file=sys.stderr)
            return rc
        print("[baseline-update] Baseline committed and pushed")
    else:
        print("[baseline-update] Baseline updated (no commit)")

    return 0


if __name__ == "__main__":
    sys.exit(main())