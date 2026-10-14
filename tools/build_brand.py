#!/usr/bin/env python3
"""Build the Lakskongen brand lockup SVGs (roadmap U1-09).

The wordmark is the word LAKSKONGEN set in the display face, Barlow Condensed
SemiBold (SIL OFL 1.1, bundled in ``godot/assets/fonts``), with wide tracking.
Its glyph outlines are written as SVG paths, so the logo is the theme's own
display face at any size and needs no font at run time.

The mark is a crown of three fjord peaks over two lines of still water: the
salmon king's crown and a fjord's walls in one stroke. The crown is drawn in
the brand salmon (#F2876A), the water in Ink info (#8CC7DA); the wordmark in
Ink text (#E8EEF0), the colours of the Ink surface it sits on (D13).

Usage:
    python3 tools/build_brand.py           # write the SVGs
    python3 tools/build_brand.py --check   # non-zero exit when they are stale

Needs fontTools (pip install fonttools). Output is deterministic.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

REPO = Path(__file__).resolve().parent.parent
FONT = REPO / "godot" / "assets" / "fonts" / "BarlowCondensed-SemiBold.ttf"
BRAND_DIR = REPO / "godot" / "assets" / "brand"
WORDMARK = BRAND_DIR / "lakskongen_wordmark.svg"
MARK = BRAND_DIR / "lakskongen_mark.svg"

WORD = "LAKSKONGEN"
# Cap height of the wordmark in SVG units (the menu shows it at half size,
# so the import's 2x raster is pixel-exact at 1080p).
CAP_HEIGHT = 112.0
# Extra space after each letter, in em units (font units / 1000).
TRACKING_EM = 0.085
INK_TEXT = "#E8EEF0"
SALMON = "#F2876A"
ICE = "#8CC7DA"


def _num(value: float) -> str:
    text = f"{value:.2f}".rstrip("0").rstrip(".")
    return "0" if text in ("-0", "") else text


class _RoundingPen(SVGPathPen):
    """An SVG path pen that writes two-decimal coordinates."""

    def __init__(self, glyph_set):
        super().__init__(glyph_set, ntos=_num)


def wordmark_svg() -> str:
    font = TTFont(FONT)
    upem = font["head"].unitsPerEm
    cap = font["OS/2"].sCapHeight
    scale = CAP_HEIGHT / cap
    glyphs = font.getGlyphSet()
    cmap = font.getBestCmap()
    tracking = TRACKING_EM * upem
    pen = _RoundingPen(glyphs)
    x = 0.0
    for i, char in enumerate(WORD):
        name = cmap[ord(char)]
        glyph = glyphs[name]
        # Font units are y-up with the baseline at 0: flip into SVG's y-down
        # space with the cap line at y = 0.
        glyph.draw(TransformPen(pen, (scale, 0, 0, -scale, x * scale, CAP_HEIGHT)))
        x += glyph.width + (tracking if i < len(WORD) - 1 else 0.0)
    width = x * scale
    path = pen.getCommands()
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{_num(width)}" height="{_num(CAP_HEIGHT)}" '
        f'viewBox="0 0 {_num(width)} {_num(CAP_HEIGHT)}">\n'
        f"  <title>Lakskongen</title>\n"
        f'  <path fill="{INK_TEXT}" d="{path}"/>\n'
        f"</svg>\n"
    )


def mark_svg() -> str:
    # 128 x 96 units: the crown's three peaks (the middle one highest) and two
    # shorter strokes of water beneath, the lower one fainter (a reflection).
    crown = "M8 62 L34 26 L50 46 L64 10 L78 46 L94 26 L120 62"
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" width="128" height="96" viewBox="0 0 128 96">\n'
        "  <title>Lakskongen</title>\n"
        f'  <path d="{crown}" fill="none" stroke="{SALMON}" stroke-width="7" '
        'stroke-linecap="round" stroke-linejoin="round"/>\n'
        f'  <path d="M18 76 H110" stroke="{ICE}" stroke-width="5" stroke-linecap="round"/>\n'
        f'  <path d="M38 89 H90" stroke="{ICE}" stroke-width="5" stroke-linecap="round" stroke-opacity="0.5"/>\n'
        "</svg>\n"
    )


def outputs() -> dict[Path, str]:
    return {WORDMARK: wordmark_svg(), MARK: mark_svg()}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="fail when the SVGs are stale")
    args = parser.parse_args()
    stale = []
    for path, text in outputs().items():
        current = path.read_text(encoding="utf-8") if path.exists() else None
        if current == text:
            continue
        if args.check:
            stale.append(path.relative_to(REPO).as_posix())
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
            print(f"wrote {path.relative_to(REPO)}")
    if stale:
        print("stale brand assets (run python3 tools/build_brand.py): " + ", ".join(stale), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
