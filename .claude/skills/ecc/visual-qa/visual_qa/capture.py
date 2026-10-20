"""
capture — Run screenshot harness, copy outputs to baseline with manifest.
"""
import argparse
import subprocess
import sys
from pathlib import Path

from .config import BASELINE_DIR, CURRENT_DIR, ROOT
from .phash import build_manifest, copy_current_to_baseline, save_manifest


def run_harness() -> int:
    """Run tools/capture_screenshots.sh. Returns exit code."""
    script = ROOT / "tools" / "capture_screenshots.sh"
    if not script.exists():
        print(f"[capture] ERROR: {script} not found", file=sys.stderr)
        return 1
    result = subprocess.run([str(script)], cwd=ROOT)
    return result.returncode


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Capture screenshots and update baseline")
    parser.add_argument("--skip-harness", action="store_true", help="Skip running capture_screenshots.sh")
    parser.add_argument("--threshold", type=int, help="Phash threshold for manifest")
    args = parser.parse_args(argv)

    if not args.skip_harness:
        print("[capture] Running screenshot harness...")
        rc = run_harness()
        if rc != 0:
            print(f"[capture] Harness exited with code {rc}", file=sys.stderr)
            return rc

    print("[capture] Copying current screenshots to baseline...")
    count = copy_current_to_baseline()
    if count == 0:
        print("[capture] ERROR: No screenshots found in current dir", file=sys.stderr)
        return 1
    print(f"[capture] Copied {count} screenshots")

    print("[capture] Building manifest...")
    manifest = build_manifest(threshold=args.threshold)
    save_manifest(manifest)
    print(f"[capture] Manifest saved to {BASELINE_DIR / 'manifest.json'}")
    print(f"[capture] Baseline ready with {len(manifest['shots'])} shots")
    return 0


if __name__ == "__main__":
    sys.exit(main())