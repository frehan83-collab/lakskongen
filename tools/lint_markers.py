#!/usr/bin/env python3
"""Marker colour lint (roadmap F2-24; farm-types spec §5.5 rule 6).

The offshore unit's hull is safety yellow (#E6BE2E) and the sea is grey and
blue, so a yellow marker over a unit would read as the hull, or as nothing.
Status markers over offshore units are therefore Ink chips with glyphs, and
this lint requires that

  - no world-anchored marker over a unit uses a yellow fill, and
  - every colour such a marker carries stands at least 20 delta E (CIE76)
    off the hull swatch.

The markers are the unit's sea chip (OffshoreUnitVisual.STATUS_COLORS and
the chip's pill and text, VesselChip, world_chip.gdshader) and the card that
hangs from a unit (FacilityWorldCard's ring). Feed barge chips and the
slot's survey buoys are not over a unit.

    python3 tools/lint_markers.py        # exit 1 and the offenders on a failure
"""

from __future__ import annotations

import colorsys
import math
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GODOT = REPO / "godot"
UNIT = GODOT / "scripts" / "visual" / "OffshoreUnitVisual.gd"
CHIP = GODOT / "scripts" / "visual" / "VesselChip.gd"
CHIP_SHADER = GODOT / "shaders" / "world_chip.gdshader"
CARD = GODOT / "scripts" / "ui" / "facilities" / "FacilityWorldCard.gd"

MIN_DELTA_E = 20.0
# What reads as a yellow fill: hue 35-70 degrees, clearly saturated, not dark.
YELLOW_HUE = (35.0, 70.0)
YELLOW_MIN_SV = (0.4, 0.5)

_FLOAT_COLOR = re.compile(r"Color\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)(?:\s*,\s*[\d.]+)?\s*\)")
_HEX_COLOR = re.compile(r'Color\(\s*"(#[0-9A-Fa-f]{6})"\s*\)')
_SHADER_COLOR = re.compile(r"vec4\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*[\d.]+\s*\)")


def srgb(hex_colour: str) -> tuple[float, float, float]:
    h = hex_colour.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))  # type: ignore[return-value]


def lab(rgb: tuple[float, float, float]) -> tuple[float, float, float]:
    """sRGB (0-1) to CIE L*a*b*, D65."""
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]
    x = (0.4124 * lin[0] + 0.3576 * lin[1] + 0.1805 * lin[2]) / 0.95047
    y = 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2]
    z = (0.0193 * lin[0] + 0.1192 * lin[1] + 0.9505 * lin[2]) / 1.08883
    f = [v ** (1 / 3) if v > 0.008856 else 7.787 * v + 16 / 116 for v in (x, y, z)]
    return (116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2]))


def delta_e(a: tuple[float, float, float], b: tuple[float, float, float]) -> float:
    return math.dist(lab(a), lab(b))


def is_yellow(rgb: tuple[float, float, float]) -> bool:
    h, s, v = colorsys.rgb_to_hsv(*rgb)
    return YELLOW_HUE[0] <= h * 360.0 <= YELLOW_HUE[1] and s >= YELLOW_MIN_SV[0] and v >= YELLOW_MIN_SV[1]


def _gd_const(text: str, name: str) -> str:
    m = re.search(rf"^const {name}\b[^\n]*?:?=\s*(.*)$", text, re.M)
    if not m:
        raise LookupError(f"const {name} not found")
    return m.group(1)


def _colours_in(expression: str) -> list[tuple[float, float, float]]:
    out = [srgb(h) for h in _HEX_COLOR.findall(expression)]
    out += [tuple(float(v) for v in m) for m in _FLOAT_COLOR.findall(expression)]  # type: ignore[misc]
    return out


def hull_yellow() -> tuple[float, float, float]:
    found = _colours_in(_gd_const(UNIT.read_text(encoding="utf-8"), "HULL_YELLOW"))
    if len(found) != 1:
        raise LookupError("OffshoreUnitVisual.HULL_YELLOW is not one colour")
    return found[0]


def markers() -> list[tuple[str, tuple[float, float, float]]]:
    """Every colour a world-anchored marker over a unit carries: (where, rgb)."""
    out: list[tuple[str, tuple[float, float, float]]] = []
    unit = UNIT.read_text(encoding="utf-8")
    block = re.search(r"^const STATUS_COLORS\b.*?\]", unit, re.M | re.S)
    if not block:
        raise LookupError("OffshoreUnitVisual.STATUS_COLORS not found")
    for k, rgb in enumerate(_colours_in(block.group(0))):
        out.append((f"OffshoreUnitVisual.STATUS_COLORS[{k}]", rgb))
    chip = CHIP.read_text(encoding="utf-8")
    for name in ("FILL", "TEXT_COLOR"):
        for rgb in _colours_in(_gd_const(chip, name)):
            out.append((f"VesselChip.{name}", rgb))
    shader = CHIP_SHADER.read_text(encoding="utf-8")
    for name in ("fill", "accent"):
        m = re.search(rf"uniform vec4 {name}\b[^=]*=\s*(vec4\([^;]*\));", shader)
        if not m:
            raise LookupError(f"world_chip.gdshader uniform {name} not found")
        for rgb in _SHADER_COLOR.findall(m.group(1)):
            out.append((f"world_chip.gdshader {name}", tuple(float(v) for v in rgb)))  # type: ignore[arg-type]
    card = CARD.read_text(encoding="utf-8")
    for name in ("RING_TRACK", "RING_FILL"):
        for rgb in _colours_in(_gd_const(card, name)):
            out.append((f"FacilityWorldCard.{name}", rgb))
    return out


def lint() -> list[str]:
    """What is wrong, one line each; empty for a clean set of markers."""
    hull = hull_yellow()
    found = markers()
    problems: list[str] = []
    if len(found) < 8:
        problems.append(f"only {len(found)} marker colours found: the lint's sources moved")
    for where, rgb in found:
        de = delta_e(rgb, hull)
        if de < MIN_DELTA_E:
            problems.append(f"{where} is {de:.1f} delta E from the hull yellow (needs {MIN_DELTA_E:.0f})")
        if is_yellow(rgb):
            problems.append(f"{where} is a yellow fill")
    return problems


def main() -> int:
    hull = hull_yellow()
    for where, rgb in markers():
        print(f"  {where:46s} delta E {delta_e(rgb, hull):6.1f}{'  YELLOW' if is_yellow(rgb) else ''}")
    problems = lint()
    for p in problems:
        print(f"FAIL {p}")
    print("MARKER LINT: " + ("FAILED" if problems else "PASSED"))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
