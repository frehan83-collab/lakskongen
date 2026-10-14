"""Trim atlas shared by the farm-types kits (F2-25 land kit, F2-26 offshore kit).

A kit's colour lives in one square RGBA8 PNG of horizontal strips: each
strip is a swatch modulated along U only (seams, panels, corrugation, a
faint wrap-around tone drift) and constant down its rows, so every mip level
down to strip-height keeps a strip on its own texels and the shader can
clamp the mip there. The alpha is the strip's roughness, or 1.0 where the
strip glows at night (windows, lamps, nav lights). Geometry points a face at
a strip with lk_meshkit.strip_uv: U is metres along the surface / tile_m,
V the strip's centre row.

A strip row is (name, sRGB hex, roughness or None, pattern, period m,
pattern depth). Patterns: "flat", "panel" (seams plus a per-panel tone),
"seam", "grating" (dark joints), "corrugated" (a sine). Periods divide the
tile so strips tile seamlessly.

The PNG writer is plain zlib level 9 with filter 0 on every row: the same
input writes the same bytes on every machine.

Licence: in-house code (proprietary, part of the Lakskongen repository).
"""

from __future__ import annotations

import math
import os
import struct
import zlib


def srgb(code: str):
    code = code.lstrip("#")
    return [int(code[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]


def strip_v(index: int, rows: int) -> float:
    """Blender UV V of a strip's centre row (glTF flips V: row r -> v_gltf = (r + 0.5) / rows)."""
    return 1.0 - (index + 0.5) / rows


def atlas_rows(strips, tile_m: float, atlas_px: int, strip_rows: int):
    """The atlas as atlas_px rows (top first) of RGBA bytes."""
    strip_px = atlas_px // strip_rows
    rows = []
    px_per_m = atlas_px / tile_m
    for idx in range(strip_rows):
        if idx < len(strips):
            _name, hexc, rough, pattern, period, depth = strips[idx]
        else:
            _name, hexc, rough, pattern, period, depth = ("unused", "#808080", 0.8, "flat", 0.0, 0.0)
        base = srgb(hexc)
        line = bytearray()
        for x in range(atlas_px):
            u = (x + 0.5) / px_per_m
            lum = 1.0
            # Low tone drift: integer harmonics of the tile, fixed phases.
            for k, (amp, ph) in enumerate(((0.012, 0.3 + idx), (0.008, 1.7 + 2 * idx), (0.006, 4.1 + 3 * idx))):
                lum += amp * math.sin(math.tau * (k + 1) * u / tile_m + ph)
            if pattern in ("panel", "seam", "grating") and period > 0.0:
                m = u % period
                d_px = min(m, period - m) * px_per_m
                lum -= depth * math.exp(-(d_px / 1.1) ** 2)
            if pattern == "panel" and period > 0.0:
                panel = int(u // period)
                lum += 0.018 * math.sin(12.9898 * (panel + 1) * (idx + 3))
            if pattern == "corrugated" and period > 0.0:
                lum += depth * math.sin(math.tau * u / period)
            alpha = 1.0 if rough is None else rough
            for c in base:
                line.append(max(0, min(255, int(round(c * lum * 255.0)))))
            line.append(int(round(alpha * 255.0)))
        rows.extend([bytes(line)] * strip_px)
    return rows


def write_png(path: str, rows, width: int) -> None:
    """A plain RGBA8 PNG (filter 0, zlib level 9): byte-identical per input."""
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, len(rows), 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(png)


def strip_colours(strips, rows, atlas_px: int, strip_rows: int) -> dict:
    """Measured colour of every strip: the median texel of its centre row."""
    strip_px = atlas_px // strip_rows
    out = {}
    for idx, s in enumerate(strips):
        line = rows[idx * strip_px + strip_px // 2]
        px = sorted((line[4 * x:4 * x + 3] for x in range(atlas_px)), key=lambda c: sum(c))
        r, g, b = px[atlas_px // 2]
        out[s[0]] = "#%02X%02X%02X" % (r, g, b)
    return out
