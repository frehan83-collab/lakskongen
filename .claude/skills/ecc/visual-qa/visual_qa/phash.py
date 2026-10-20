"""
Perceptual hash utilities using PIL + imagehash.
"""
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from PIL import Image
import imagehash

from .config import BASELINE_DIR, CURRENT_DIR, MANIFEST, SHOT_NAMES, THRESHOLD


def compute_phash(image_path: Path) -> str:
    """Compute perceptual hash (pHash) of an image as hex string."""
    with Image.open(image_path) as img:
        phash = imagehash.phash(img)
    return str(phash)


def phash_distance(hash1: str, hash2: str) -> int:
    """Hamming distance between two hex phash strings."""
    h1 = int(hash1, 16)
    h2 = int(hash2, 16)
    return bin(h1 ^ h2).count("1")


def get_git_sha() -> str:
    """Get current git commit SHA (short)."""
    try:
        result = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            capture_output=True,
            text=True,
            check=True,
        )
        return result.stdout.strip()
    except (subprocess.CalledProcessError, FileNotFoundError):
        return "unknown"


def load_manifest() -> dict[str, Any]:
    """Load baseline manifest.json."""
    if not MANIFEST.exists():
        return {}
    with MANIFEST.open() as f:
        return json.load(f)


def save_manifest(manifest: dict[str, Any]) -> None:
    """Save baseline manifest.json."""
    BASELINE_DIR.mkdir(parents=True, exist_ok=True)
    with MANIFEST.open("w") as f:
        json.dump(manifest, f, indent=2)


def build_manifest(threshold: int | None = None) -> dict[str, Any]:
    """Build manifest from current baseline images."""
    # The calibrated default (config.THRESHOLD, 25), not the old 8 (F1110).
    threshold = threshold if threshold is not None else THRESHOLD
    manifest = {
        "version": 1,
        "git_sha": get_git_sha(),
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "threshold": threshold,
        "shots": {},
    }

    for shot in SHOT_NAMES:
        baseline_path = BASELINE_DIR / f"{shot}.png"
        if not baseline_path.exists():
            continue
        phash = compute_phash(baseline_path)
        with Image.open(baseline_path) as img:
            size = list(img.size)
        manifest["shots"][shot] = {"phash": phash, "size": size}

    return manifest


def copy_current_to_baseline() -> int:
    """Copy all current screenshots to baseline dir. Returns count copied."""
    BASELINE_DIR.mkdir(parents=True, exist_ok=True)
    count = 0
    for shot in SHOT_NAMES:
        src = CURRENT_DIR / f"{shot}.png"
        dst = BASELINE_DIR / f"{shot}.png"
        if src.exists():
            import shutil
            shutil.copy2(src, dst)
            count += 1
    return count


def get_shot_paths(shot: str) -> tuple[Path, Path]:
    """Return (baseline_path, current_path) for a shot."""
    return BASELINE_DIR / f"{shot}.png", CURRENT_DIR / f"{shot}.png"