"""
gallery — Generate side-by-side diff gallery for PR comments.
"""
import base64
import io
import json
import sys
from pathlib import Path
from typing import Any

from PIL import Image, ImageChops, ImageDraw, ImageFont

from .config import SHOT_NAMES
from .phash import (
    compute_phash,
    get_shot_paths,
    load_manifest,
    phash_distance,
)


def create_diff_image(baseline: Image.Image, current: Image.Image) -> Image.Image:
    """Create a heatmap diff image (red = different)."""
    if baseline.size != current.size:
        current = current.resize(baseline.size, Image.Resampling.LANCZOS)

    diff = ImageChops.difference(baseline.convert("RGB"), current.convert("RGB"))
    diff = diff.convert("L").point(lambda x: x * 8)  # amplify
    heatmap = Image.new("RGB", baseline.size, (0, 0, 0))
    for y in range(baseline.height):
        for x in range(baseline.width):
            v = diff.getpixel((x, y))
            if v > 0:
                heatmap.putpixel((x, y), (min(255, v), 0, 0))
    return heatmap


def create_side_by_side(baseline: Image.Image, current: Image.Image, diff: Image.Image, max_width: int = 1200) -> Image.Image:
    """Create horizontal triplet: baseline | current | diff."""
    images = [baseline, current, diff]
    # Resize all to same height, proportional
    target_height = 400
    resized = []
    for img in images:
        ratio = target_height / img.height
        new_w = int(img.width * ratio)
        resized.append(img.resize((new_w, target_height), Image.Resampling.LANCZOS))

    total_width = sum(img.width for img in resized) + 20 * 2
    if total_width > max_width:
        scale = max_width / total_width
        resized = [img.resize((int(img.width * scale), int(img.height * scale)), Image.Resampling.LANCZOS) for img in resized]

    combined = Image.new("RGB", (sum(img.width for img in resized) + 40, max(img.height for img in resized) + 60), (240, 240, 240))
    x = 20
    labels = ["Baseline", "Current", "Diff"]
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Helvetica.ttc", 14)
    except OSError:
        font = ImageFont.load_default()
    draw = ImageDraw.Draw(combined)
    for img, label in zip(resized, labels):
        combined.paste(img, (x, 40))
        draw.text((x + 5, 10), label, fill=(60, 60, 60), font=font)
        x += img.width + 20
    return combined


def image_to_data_uri(img: Image.Image) -> str:
    """Convert PIL image to base64 data URI."""
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    b64 = base64.b64encode(buf.getvalue()).decode()
    return f"data:image/png;base64,{b64}"


def generate_gallery(output_md: Path = Path("visual_qa_gallery.md"), threshold: int | None = None) -> int:
    """Generate markdown gallery file for PR comment."""
    manifest = load_manifest()
    if not manifest:
        print("[gallery] ERROR: No baseline manifest", file=sys.stderr)
        return 1

    threshold = threshold or manifest.get("threshold", 8)
    shots = manifest.get("shots", {})

    lines = [
        "## Visual QA Report",
        "",
        f"Threshold: {threshold}  |  Baseline SHA: {manifest.get('git_sha', 'unknown')}  |  Baseline Time: {manifest.get('timestamp', 'unknown')}",
        "",
        "| Shot | Baseline | Current | Diff | Distance | Status |",
        "|------|----------|---------|------|----------|--------|",
    ]

    details = ["<details><summary>All shots detail</summary>", "", "| Shot | Baseline | Current | Diff | Distance | Status |", "|------|----------|---------|------|----------|--------|"]

    for shot in SHOT_NAMES:
        baseline_path, current_path = get_shot_paths(shot)

        if shot not in shots or not current_path.exists() or not baseline_path.exists():
            status = "MISSING"
            dist = "N/A"
            baseline_uri = current_uri = diff_uri = ""
        else:
            baseline_img = Image.open(baseline_path)
            current_img = Image.open(current_path)
            baseline_hash = shots[shot]["phash"]
            current_hash = compute_phash(current_path)
            dist = phash_distance(current_hash, baseline_hash)
            status = "❌ FAIL" if dist > threshold else "✅ PASS"

            diff_img = create_diff_image(baseline_img, current_img)
            combined = create_side_by_side(baseline_img, current_img, diff_img)
            baseline_uri = image_to_data_uri(baseline_img)
            current_uri = image_to_data_uri(current_img)
            diff_uri = image_to_data_uri(diff_img)

        # Table row with small thumbnails
        def thumb(uri: str) -> str:
            return f'<img src="{uri}" width="120" />' if uri else "—"

        lines.append(f"| {shot} | {thumb(baseline_uri)} | {thumb(current_uri)} | {thumb(diff_uri)} | {dist} | {status} |")
        details.append(f"| {shot} | {thumb(baseline_uri)} | {thumb(current_uri)} | {thumb(diff_uri)} | {dist} | {status} |")

    details.extend(["", "</details>"])
    lines.extend(details)

    output_md.write_text("\n".join(lines))
    print(f"[gallery] Generated {output_md}")
    return 0


def main(argv: list[str] | None = None) -> int:
    import argparse
    parser = argparse.ArgumentParser(description="Generate visual diff gallery markdown")
    parser.add_argument("-o", "--output", default="visual_qa_gallery.md", help="Output markdown file")
    parser.add_argument("--threshold", type=int, help="Phash threshold")
    args = parser.parse_args(argv)
    return generate_gallery(Path(args.output), args.threshold)


if __name__ == "__main__":
    sys.exit(main())