#!/usr/bin/env python3.12
"""Deterministic, tileable detail textures for the fjord water (G1-05).

Generates (512 x 512, seeded, seamless by construction):

  godot/assets/textures/water/ocean_normal_1.png  ripple normal map, long tile
  godot/assets/textures/water/ocean_normal_2.png  ripple normal map, short tile
  godot/assets/textures/water_foam.png            foam data (RGBA, linear):
      R  foam lace: Voronoi cell walls (the bubbly web of breaking foam)
      G  foam patches: low-frequency FBM (where a whitecap's foam lies thick)
      B  fine bubbles: a smaller Voronoi web
      A  smooth noise: FBM equalised to a uniform 0-1 distribution (which
         stretch of a crest breaks, foam fields)

The ripple maps are short-wave spectra, not value noise: a sum of plane waves
on the tile's integer wavevectors (so they tile exactly) with a k^-3.5
(saturation-range) amplitude spectrum and a cos^4 directional spread around
+X (the shader turns +X into the sea direction). Written for the project;
no third-party code.

Needs numpy and Pillow (python3.12 on the dev machines). Run:
    python3.12 tools/gen_water_textures.py
"""

from __future__ import annotations

import os

import numpy as np
from PIL import Image

SIZE = 512
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "godot", "assets", "textures")
WATER_OUT = os.path.join(OUT, "water")


def ripple_height(seed: int, k_min: int, k_max: int, spread_power: float) -> np.ndarray:
    """Height field (zero mean, unit RMS) from plane waves on integer wavevectors."""
    rng = np.random.default_rng(seed)
    coords = np.arange(SIZE) / SIZE
    u, v = np.meshgrid(coords, coords, indexing="xy")
    height = np.zeros((SIZE, SIZE))
    for kx in range(-k_max, k_max + 1):
        for ky in range(-k_max, k_max + 1):
            k = np.hypot(kx, ky)
            if k < k_min or k > k_max:
                continue
            # Waves travel within +-90 deg of +X (cos^p spreading).
            cos_t = kx / k
            if cos_t <= 0.0:
                continue
            amp = k ** -3.5 * cos_t ** spread_power * rng.rayleigh(1.0)
            phase = rng.uniform(0.0, 2.0 * np.pi)
            height += amp * np.cos(2.0 * np.pi * (kx * u + ky * v) + phase)
    height -= height.mean()
    return height / height.std()


def height_to_normal(height: np.ndarray, strength: float) -> Image.Image:
    """Tangent-space normal map (X right, Y down the image = +Z) from a periodic height field."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    nx, ny, nz = -dx * strength, -dy * strength, np.ones_like(height)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([nx / length, ny / length, nz / length], axis=-1) * 0.5 + 0.5
    return Image.fromarray(np.clip(rgb * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGB")


def voronoi_walls(seed: int, cells: int) -> np.ndarray:
    """Tileable Worley F2 - F1 (0 on cell walls, growing inside), normalised to 0-1."""
    rng = np.random.default_rng(seed)
    # One feature point per cell, jittered; pixels compare the 5x5 cells round theirs.
    jitter = rng.uniform(0.0, 1.0, (cells, cells, 2))
    coords = (np.arange(SIZE) + 0.5) / SIZE * cells
    px, py = np.meshgrid(coords, coords, indexing="xy")
    cx, cy = np.floor(px).astype(int), np.floor(py).astype(int)
    f1 = np.full((SIZE, SIZE), 9.0)
    f2 = np.full((SIZE, SIZE), 9.0)
    for ox in range(-2, 3):
        for oy in range(-2, 3):
            nx, ny = cx + ox, cy + oy
            jx = jitter[ny % cells, nx % cells, 0]
            jy = jitter[ny % cells, nx % cells, 1]
            d = np.hypot(px - (nx + jx), py - (ny + jy))
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            f1 = np.where(closer, d, f1)
    walls = f2 - f1
    return np.clip(walls / np.percentile(walls, 99.0), 0.0, 1.0)


def fbm(seed: int, base: int, octaves: int) -> np.ndarray:
    """Tileable FBM from random-phase plane waves (0-1)."""
    total = np.zeros((SIZE, SIZE))
    amp = 1.0
    for octave in range(octaves):
        total += amp * ripple_height(seed + octave * 31, base * 2 ** octave, base * 2 ** octave * 2, 0.0)
        amp *= 0.5
    total -= total.min()
    return total / total.max()


def main() -> None:
    os.makedirs(WATER_OUT, exist_ok=True)
    # Ripples: the long tile carries 2-6 cycles of gentle wind waves, the
    # short tile the fine capillary-gravity chop.
    height_to_normal(ripple_height(4242, 4, 30, 4.0), 4.5).save(os.path.join(WATER_OUT, "ocean_normal_1.png"), optimize=True)
    height_to_normal(ripple_height(7777, 8, 56, 3.0), 4.0).save(os.path.join(WATER_OUT, "ocean_normal_2.png"), optimize=True)
    print("  water   ocean_normal_1/2 -> ripple spectra")

    lace = 1.0 - voronoi_walls(9001, 14) ** 0.45
    bubbles = 1.0 - voronoi_walls(9002, 38) ** 0.6
    patches = fbm(9003, 2, 3)
    patches = np.clip((patches - 0.35) * 2.2, 0.0, 1.0)
    smooth = fbm(9004, 2, 4)
    # Histogram-equalise: rank / count gives an exactly uniform distribution.
    order = np.argsort(smooth, axis=None)
    uniform = np.empty(smooth.size)
    uniform[order] = (np.arange(smooth.size) + 0.5) / smooth.size
    smooth = uniform.reshape(smooth.shape)
    rgba = np.stack([lace, patches, bubbles, smooth], axis=-1)
    Image.fromarray(np.clip(rgba * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGBA").save(
        os.path.join(OUT, "water_foam.png"), optimize=True)
    print("  water   water_foam       -> RGBA lace / patches / bubbles / uniform noise")


if __name__ == "__main__":
    main()
