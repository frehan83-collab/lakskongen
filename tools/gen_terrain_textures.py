#!/usr/bin/env python3
"""Terrain material layers for the fjord (roadmap G2-02), generated, not downloaded.

Writes two layered images that Godot imports as Texture2DArrays:

  godot/assets/textures/terrain/terrain_albedo_height.png
      one 1024x1024 slice per layer stacked vertically; RGB albedo (sRGB),
      A height (0 low, 1 high) for height-blended layer transitions.
  godot/assets/textures/terrain/terrain_normal_rough_ao.png
      the same layers; RG tangent-space normal in the texture's own u/v
      directions (u along +column, v along +row: n = normalize(-dh/du,
      -dh/dv, 1) * 0.5 + 0.5), B roughness, A ambient occlusion; then two
      noise slices the shader samples instead of computing value noise per
      pixel (NOISE_SLICES): four independent smooth fields, and one field
      with its u and v derivatives (for bump mapping).

Layers (LAYERS below, in order) are drawn for the game's camera, which sits
25-3,000 m from the ground: one slice spans NEAR_TILE_M metres of ground at
the near sample scale (the shader samples it again at a far scale), so the
features are cliff blocks, ledges, crowns of trees and heath patches, not
pebbles. Every slice tiles seamlessly (periodic spectral noise, a periodic
Voronoi diagram, wrapped filters and warps).

Deterministic: fixed seeds, numpy + scipy + Pillow only, no network, no
third-party images. The output is original work of this repository.

Run:
    python3 tools/gen_terrain_textures.py            # writes both images
    python3 tools/gen_terrain_textures.py --preview  # also writes per-layer previews to /tmp
"""

from __future__ import annotations

import argparse
import os
import sys
from concurrent.futures import ProcessPoolExecutor

import numpy as np
from PIL import Image
from scipy import ndimage
from scipy.spatial import cKDTree

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "godot", "assets", "textures", "terrain")
ALBEDO_FILE = "terrain_albedo_height.png"
NORMAL_FILE = "terrain_normal_rough_ao.png"

SIZE = 1024
# Ground covered by one slice at the shader's near scale (terrain_shader.gdshader NEAR_TILE_M).
NEAR_TILE_M = 20.0
PX_PER_M = SIZE / NEAR_TILE_M

# Layer order = array index in the shader (terrain_shader.gdshader LAYER_*).
LAYERS = ("rock", "grass", "heath", "snow", "shingle", "spruce_canopy", "birch_canopy")
# Extra slices at the end of the normal array (terrain_shader.gdshader L_NOISE, L_BUMP).
NOISE_SLICES = ("noise4", "bump")
# The bump slice stores dB/du and dB/dv (per slice length) as 0.5 + d / (2 BUMP_GRADIENT_RANGE).
BUMP_GRADIENT_RANGE = 48.0


# --------------------------------------------------------------------------- #
# Tileable building blocks                                                    #
# --------------------------------------------------------------------------- #

def _freq_grid(size: int) -> np.ndarray:
    fy = np.fft.fftfreq(size)[:, None] * size
    fx = np.fft.fftfreq(size)[None, :] * size
    return np.sqrt(fx * fx + fy * fy)


def spectral(rng: np.random.Generator, beta: float, kmin: float, kmax: float,
             aniso: tuple[float, float] = (1.0, 1.0)) -> np.ndarray:
    """Periodic 1/f^beta noise with wavenumbers (cycles per slice) in [kmin, kmax], scaled to [-1, 1].

    aniso stretches the spectrum: (1, 4) makes features four times longer along u than v.
    """
    fy = np.fft.fftfreq(SIZE)[:, None] * SIZE * aniso[1]
    fx = np.fft.fftfreq(SIZE)[None, :] * SIZE * aniso[0]
    k = np.sqrt(fx * fx + fy * fy)
    amp = np.zeros_like(k)
    band = (k >= kmin) & (k <= kmax)
    amp[band] = k[band] ** (-beta / 2.0)
    # Soft band edges avoid ringing.
    amp *= np.clip((k - kmin * 0.7) / (kmin * 0.3 + 1e-9), 0, 1)
    phase = rng.uniform(0, 2 * np.pi, (SIZE, SIZE))
    field = np.real(np.fft.ifft2(amp * np.exp(1j * phase)))
    field -= field.mean()
    return field / (np.abs(field).max() + 1e-9)


def voronoi(rng: np.random.Generator, cells: int, jitter: float = 1.0):
    """Periodic Voronoi on a jittered grid of cells x cells: (F1, F2, cell index) in pixels."""
    gy, gx = np.mgrid[0:cells, 0:cells]
    pts = np.stack([(gx + 0.5 + jitter * (rng.random((cells, cells)) - 0.5)).ravel(),
                    (gy + 0.5 + jitter * (rng.random((cells, cells)) - 0.5)).ravel()], axis=1)
    pts = np.mod(pts * (SIZE / cells), SIZE)
    tree = cKDTree(pts, boxsize=SIZE)
    yy, xx = np.mgrid[0:SIZE, 0:SIZE]
    q = np.stack([xx.ravel() + 0.5, yy.ravel() + 0.5], axis=1)
    d, idx = tree.query(q, k=2)
    f1 = d[:, 0].reshape(SIZE, SIZE)
    f2 = d[:, 1].reshape(SIZE, SIZE)
    return f1, f2, idx[:, 0].reshape(SIZE, SIZE)


def scatter_points(rng: np.random.Generator, count: int, min_dist_px: float) -> np.ndarray:
    """Periodic dart-throwing points (approximate Poisson disc)."""
    pts = []
    tries = 0
    while len(pts) < count and tries < count * 60:
        tries += 1
        p = rng.random(2) * SIZE
        if pts:
            arr = np.array(pts)
            d = np.abs(arr - p)
            d = np.minimum(d, SIZE - d)
            if np.min(np.hypot(d[:, 0], d[:, 1])) < min_dist_px:
                continue
        pts.append(p)
    return np.array(pts)


def warp(field: np.ndarray, dx: np.ndarray, dy: np.ndarray) -> np.ndarray:
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float64)
    return ndimage.map_coordinates(field, [yy + dy, xx + dx], order=1, mode="grid-wrap")


def blur(field: np.ndarray, sigma: float) -> np.ndarray:
    return ndimage.gaussian_filter(field, sigma, mode="wrap")


def norm01(field: np.ndarray) -> np.ndarray:
    lo, hi = np.percentile(field, 0.5), np.percentile(field, 99.5)
    return np.clip((field - lo) / (hi - lo + 1e-9), 0.0, 1.0)


def smoothstep(e0: float, e1: float, x: np.ndarray) -> np.ndarray:
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def srgb(hex_code: str) -> np.ndarray:
    hex_code = hex_code.lstrip("#")
    return np.array([int(hex_code[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def mix(a: np.ndarray, b: np.ndarray, t: np.ndarray) -> np.ndarray:
    t = t[..., None] if t.ndim == 2 else t
    return a * (1.0 - t) + b * t


def normal_from_height(h_m: np.ndarray) -> np.ndarray:
    """Tangent-space normal from a height field in metres (u right, v down the rows)."""
    px_m = 1.0 / PX_PER_M
    dhdu = (np.roll(h_m, -1, axis=1) - np.roll(h_m, 1, axis=1)) / (2.0 * px_m)
    dhdv = (np.roll(h_m, -1, axis=0) - np.roll(h_m, 1, axis=0)) / (2.0 * px_m)
    n = np.stack([-dhdu, -dhdv, np.ones_like(h_m)], axis=-1)
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


def occlusion(h_m: np.ndarray, radii_m=(0.15, 0.6, 2.0), strength: float = 1.0) -> np.ndarray:
    """Cavity occlusion: how far a texel sits below its neighbourhood, over a few radii."""
    ao = np.zeros_like(h_m)
    for r in radii_m:
        sigma = r * PX_PER_M
        below = np.clip(blur(h_m, sigma) - h_m, 0.0, None)
        ao += below / (r * 0.6)
    return np.clip(1.0 - strength * ao / len(radii_m), 0.25, 1.0)


# --------------------------------------------------------------------------- #
# Layers: each returns (albedo sRGB HxWx3, height 0-1, height metres, roughness) #
# --------------------------------------------------------------------------- #

def joint_set(rng: np.random.Generator, direction: tuple[int, int], warp_px: float,
              crack_px: float) -> tuple[np.ndarray, np.ndarray]:
    """A set of parallel rock joints, periodic over the slice.

    direction is an integer (a, b): the joints run across it, |(a, b)| of
    them per slice, so the set tiles. Every band between two joints is a
    slab whose height varies slowly along the band and steps at the joints.
    Returns (slab height -1..1, crack 0-1).
    """
    a, b = direction
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float64)
    length = np.hypot(a, b)
    spacing_px = SIZE / length
    t = (a * xx + b * yy) / SIZE
    t = t + spectral(rng, 2.4, 1, 8) * warp_px / spacing_px
    # Irregular spacing: squeeze some bands, stretch others.
    t = t + 0.22 * np.sin(2 * np.pi * t * 0.5 + rng.uniform(0, 6.28)) * (1 if a % 2 == 0 and b % 2 == 0 else 0)
    f = t - np.floor(t)
    dist = np.minimum(f, 1.0 - f) * spacing_px
    # The slab's height is read at the start of its band, so it is constant
    # across the band and steps at each joint.
    nx, ny = a / length, b / length
    back = f * spacing_px
    field = spectral(rng, 2.0, 1, 10)
    slab = ndimage.map_coordinates(field, [yy - back * ny, xx - back * nx], order=1, mode="grid-wrap")
    crack = 1.0 - smoothstep(0.0, crack_px, dist)
    return slab, crack


def layer_rock(seed: int):
    """Vestland gneiss/granite cliff: joint-bounded slabs, weathering patches, lichen, moss in the joints."""
    rng = np.random.default_rng(seed)
    # Sheeting joints (near horizontal on a cliff) and cross joints (near
    # vertical), each open only along stretches of its length.
    slab_a, crack_a = joint_set(rng, (1, 7), 9.0, 4.0)
    slab_b, crack_b = joint_set(rng, (6, -1), 12.0, 3.0)
    crack_a *= smoothstep(-0.15, 0.35, spectral(rng, 2.0, 2, 14))
    gate_b = smoothstep(0.0, 0.45, spectral(rng, 2.0, 2, 14))
    crack_b *= gate_b
    lumps = spectral(rng, 2.4, 2, 40)
    mid = spectral(rng, 1.9, 12, 120)
    grain = spectral(rng, 1.6, 100, 300)
    foliation = spectral(rng, 2.2, 3, 90, aniso=(0.15, 1.0))
    h_m = (0.32 * slab_a + 0.16 * slab_b * gate_b + 0.42 * lumps + 0.10 * mid
           + 0.06 * foliation + 0.012 * grain - 0.30 * crack_a - 0.22 * crack_b)
    height = norm01(h_m)
    crack = np.clip(crack_a + crack_b, 0.0, 1.0)
    crack_dark = blur(crack, 1.2) * (0.25 + 0.3 * norm01(spectral(rng, 2.0, 3, 30)))

    base = srgb("#6B7276")
    warm = srgb("#7F786F")      # feldspar-rich, weathered
    cool = srgb("#535D63")      # fresh, mica-rich bands
    pale = srgb("#8F928E")      # weathered faces, crustose lichen
    stain = srgb("#4A4F50")     # dark lichen and seepage stains
    dark = srgb("#272C30")      # joint shadow
    lichen_grey = srgb("#A6AB9F")
    lichen_yellow = srgb("#9F9A5C")
    moss = srgb("#3D4A32")
    rust = srgb("#7A5E4B")
    col = np.broadcast_to(base, (SIZE, SIZE, 3)).copy()
    col = mix(col, warm, smoothstep(-0.1, 0.7, spectral(rng, 2.2, 2, 20)) * 0.6)
    col = mix(col, cool, smoothstep(0.0, 0.7, foliation) * 0.45)
    col = mix(col, pale, smoothstep(0.1, 0.8, spectral(rng, 2.3, 2, 12)) * 0.5)
    col = mix(col, stain, smoothstep(0.25, 0.85, spectral(rng, 2.0, 4, 40)) * 0.45)
    # Each slab weathers to its own tone.
    tone = 0.14 * np.tanh(1.2 * slab_a + 0.8 * slab_b)
    col *= (1.0 + tone)[..., None]
    col *= (0.88 + 0.18 * norm01(mid) + 0.05 * norm01(grain))[..., None]
    top = smoothstep(0.4, 0.85, height)
    col = mix(col, lichen_grey, smoothstep(0.35, 0.7, spectral(rng, 1.8, 14, 160)) * top * 0.55)
    col = mix(col, lichen_yellow, smoothstep(0.6, 0.85, spectral(rng, 1.8, 16, 140)) * top * 0.45)
    col = mix(col, rust, smoothstep(0.78, 0.95, spectral(rng, 2.0, 5, 40)) * 0.3)
    # Soil, moss and shadow in the joints.
    col = mix(col, moss, crack * smoothstep(-0.2, 0.5, spectral(rng, 2.0, 4, 30)) * 0.5)
    col = mix(col, dark, crack_dark)
    rough = 0.80 + 0.1 * norm01(mid) - 0.05 * top
    return col, height, h_m, rough


def layer_grass(seed: int):
    """Hay meadow / pasture seen from above: clumps, tussocks, a few bare spots."""
    rng = np.random.default_rng(seed)
    clumps = spectral(rng, 1.6, 40, 260)
    tufts = spectral(rng, 1.2, 120, 300)
    patches = spectral(rng, 2.2, 2, 20)
    h_m = 0.08 * clumps + 0.03 * tufts + 0.12 * patches
    height = norm01(h_m)
    green = srgb("#58703F")
    yellow = srgb("#7A834A")
    deep = srgb("#3D5530")
    soil = srgb("#5E5141")
    col = mix(np.broadcast_to(green, (SIZE, SIZE, 3)), yellow, smoothstep(0.1, 0.9, patches) * 0.6)
    col = mix(col, deep, smoothstep(0.55, 0.0, norm01(clumps)) * 0.55)
    col *= (0.86 + 0.24 * norm01(tufts))[..., None]
    col = mix(col, soil, smoothstep(0.82, 0.95, spectral(rng, 1.8, 8, 60)) * 0.7)
    rough = 0.9 + 0.06 * norm01(tufts)
    return col, height, h_m, rough


def layer_heath(seed: int):
    """Coastal mountain heath: crowberry, heather, reindeer lichen, dwarf birch, boulders."""
    rng = np.random.default_rng(seed)
    a = spectral(rng, 2.0, 4, 80)
    b = spectral(rng, 2.0, 4, 80)
    fine = spectral(rng, 1.3, 80, 300)
    crowberry = srgb("#4A4C30")
    heather = srgb("#6B5849")
    lichen = srgb("#B3B39A")
    dwarf_birch = srgb("#7A6F42")
    peat = srgb("#463C31")
    stone = srgb("#787D7F")
    col = np.broadcast_to(crowberry, (SIZE, SIZE, 3)).copy()
    col = mix(col, heather, smoothstep(0.0, 0.5, a) * 0.8)
    col = mix(col, dwarf_birch, smoothstep(0.1, 0.6, b) * 0.6)
    lich = smoothstep(0.35, 0.75, spectral(rng, 1.9, 10, 120))
    col = mix(col, lichen, lich * 0.85)
    col = mix(col, peat, smoothstep(0.75, 0.95, spectral(rng, 2.0, 6, 60)) * 0.6)
    # Outcrops and boulders: irregular patches of grey rock.
    rock = spectral(rng, 2.1, 6, 60)
    rock = warp(rock, spectral(rng, 2, 4, 30) * 14, spectral(rng, 2, 4, 30) * 14)
    boulder = smoothstep(0.42, 0.62, rock)
    stone_tone = (0.82 + 0.3 * norm01(spectral(rng, 1.8, 30, 200)))[..., None]
    col = mix(col, stone * stone_tone, boulder * 0.9)
    col *= (0.85 + 0.25 * norm01(fine))[..., None]
    h_m = 0.10 * a + 0.05 * fine + 0.35 * boulder + 0.06 * lich
    height = norm01(h_m)
    rough = 0.92 - 0.1 * boulder
    return col, height, h_m, rough


def layer_snow(seed: int):
    """Wind-packed snow: drifts and sastrugi ripples, faint blue in the hollows."""
    rng = np.random.default_rng(seed)
    drifts = spectral(rng, 2.6, 2, 18)
    sastrugi = spectral(rng, 1.8, 20, 140, aniso=(0.35, 1.0))
    sparkle = spectral(rng, 0.8, 150, 300)
    h_m = 0.35 * drifts + 0.05 * sastrugi + 0.004 * sparkle
    height = norm01(h_m)
    white = srgb("#F2F5F8")
    blue = srgb("#D3DEE9")
    col = mix(np.broadcast_to(white, (SIZE, SIZE, 3)), blue, smoothstep(0.55, 0.0, height) * 0.7)
    col *= (0.97 + 0.04 * norm01(sparkle))[..., None]
    rough = 0.62 + 0.12 * norm01(sastrugi)
    return col, height, h_m, rough


def layer_shingle(seed: int):
    """Shingle and boulder beach: rounded cobbles, sand between, wrack lines."""
    rng = np.random.default_rng(seed)
    f1, f2, cid = voronoi(rng, 48, 1.0)
    dome = np.clip(1.0 - (f1 / (0.55 * f2 + 1e-6)) ** 2, 0, 1)
    rng_cells = np.random.default_rng(seed + 3)
    tone = rng_cells.uniform(0.75, 1.15, 48 * 48)[cid]
    hue = rng_cells.integers(0, 3, 48 * 48)[cid]
    stones = np.stack([srgb("#7A766E"), srgb("#8A8579"), srgb("#5D5B57")])[hue]
    sand = srgb("#8C8475")
    col = mix(np.broadcast_to(sand, (SIZE, SIZE, 3)), stones * tone[..., None], smoothstep(0.05, 0.25, dome))
    wrack = smoothstep(0.7, 0.9, spectral(rng, 2.0, 3, 25, aniso=(0.2, 1.0)))
    col = mix(col, srgb("#3B3426"), wrack * 0.6)
    h_m = 0.12 * dome + 0.05 * spectral(rng, 2.0, 2, 20)
    height = norm01(h_m)
    rough = 0.82 - 0.15 * dome
    return col, height, h_m, rough


def _canopy(seed: int, crowns: int, min_dist: float, radius: tuple[float, float],
            colours: tuple[str, str, str], gap: str, pointed: bool):
    rng = np.random.default_rng(seed)
    pts = scatter_points(rng, crowns, min_dist)
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float64)
    h = np.zeros((SIZE, SIZE))
    shade = np.zeros((SIZE, SIZE))
    tint = np.zeros((SIZE, SIZE))
    for (px, py) in pts:
        r0 = rng.uniform(*radius)
        top = rng.uniform(0.7, 1.0)
        tone = rng.uniform(0, 1)
        if pointed:
            blobs = [(px, py, r0)]
        else:
            # A birch crown is a few rounded masses around the stem.
            blobs = [(px + rng.normal(0, r0 * 0.25), py + rng.normal(0, r0 * 0.25), r0 * rng.uniform(0.5, 0.75))
                     for _ in range(4)]
        for (bx, by, r) in blobs:
            dx = (xx - bx + SIZE / 2) % SIZE - SIZE / 2
            dy = (yy - by + SIZE / 2) % SIZE - SIZE / 2
            d = np.sqrt(dx * dx + dy * dy)
            if pointed:
                ang = np.arctan2(dy, dx)
                d = d / (r * (1.0 + 0.16 * np.sin(9 * ang + px) + 0.08 * np.sin(5 * ang + py)))
                crown = np.clip(1.0 - d, 0, 1) ** 1.3
            else:
                d = d / r
                crown = np.sqrt(np.clip(1.0 - d * d, 0, 1))
            val = crown * top
            better = val > h
            h = np.where(better, val, h)
            tint = np.where(better, tone, tint)
            shade = np.where(better, crown, shade)
    clusters = spectral(rng, 1.3, 60, 300)
    h_m = 3.0 * h + 0.3 * clusters * (h > 0.02)
    height = norm01(h_m)
    c_dark, c_mid, c_light = (srgb(c) for c in colours)
    col = mix(np.broadcast_to(c_mid, (SIZE, SIZE, 3)), c_light, smoothstep(0.3, 1.0, shade) * (0.4 + 0.6 * tint))
    col = mix(col, c_dark, smoothstep(0.55, 0.0, shade) * 0.75)
    col *= (0.86 + 0.28 * norm01(clusters))[..., None]
    col = mix(col, srgb(gap), smoothstep(0.06, 0.0, h))
    rough = 0.9 * np.ones((SIZE, SIZE))
    return col, height, h_m, rough


def layer_spruce_canopy(seed: int):
    """Spruce stand from above: dense, dark, pointed crowns 3-4 m across."""
    return _canopy(seed, 80, 66.0, (62.0, 95.0), ("#1B2A20", "#26392B", "#3A5641"), "#1A2118", pointed=True)


def layer_birch_canopy(seed: int):
    """Birch wood from above: rounded, lighter crowns 4-6 m across (summer green; the shader turns them)."""
    return _canopy(seed, 40, 95.0, (100.0, 150.0), ("#2C4022", "#43602F", "#5E7A3E"), "#252D1E", pointed=False)


BUILDERS = {
    "rock": layer_rock,
    "grass": layer_grass,
    "heath": layer_heath,
    "snow": layer_snow,
    "shingle": layer_shingle,
    "spruce_canopy": layer_spruce_canopy,
    "birch_canopy": layer_birch_canopy,
}


def build_layer(index: int, name: str):
    col, height, h_m, rough = BUILDERS[name](1000 + 17 * index)
    col = np.clip(col, 0.0, 1.0)
    n = normal_from_height(h_m)
    ao = occlusion(h_m)
    albedo = np.dstack([col, height])
    nra = np.dstack([n[..., 0] * 0.5 + 0.5, n[..., 1] * 0.5 + 0.5, np.clip(rough, 0, 1), ao])
    return albedo, nra


def noise_slice(name: str) -> np.ndarray:
    """RGBA 0-1. noise4: four independent smooth fields, features about a
    sixth of the slice (A twice as fine). bump: a smooth field and its u, v
    derivatives (per slice length, encoded around 0.5)."""
    rng = np.random.default_rng(4242 if name == "noise4" else 4343)
    if name == "noise4":
        chans = [norm01(spectral(rng, 2.0, 2, 40)) for _ in range(3)]
        chans.append(norm01(spectral(rng, 2.0, 6, 80)))
        return np.dstack(chans)
    b = norm01(spectral(rng, 2.4, 2, 32))
    du = (np.roll(b, -1, axis=1) - np.roll(b, 1, axis=1)) * 0.5 * SIZE
    dv = (np.roll(b, -1, axis=0) - np.roll(b, 1, axis=0)) * 0.5 * SIZE
    peak = max(np.abs(du).max(), np.abs(dv).max())
    assert peak <= BUMP_GRADIENT_RANGE, f"bump gradient {peak:.1f} exceeds the encoded range"
    enc = lambda d: 0.5 + d / (2.0 * BUMP_GRADIENT_RANGE)  # noqa: E731
    return np.dstack([b, enc(du), enc(dv), np.ones_like(b)])


def to_u8(img: np.ndarray) -> np.ndarray:
    return (np.clip(img, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n", 1)[0])
    parser.add_argument("--preview", metavar="DIR", help="also write one RGB preview per layer into DIR")
    parser.add_argument("--out", default=OUT_DIR)
    args = parser.parse_args(argv)
    os.makedirs(args.out, exist_ok=True)
    albedo_stack, nra_stack = [], []
    with ProcessPoolExecutor(max_workers=min(4, os.cpu_count() or 1)) as pool:
        built = list(pool.map(build_layer, range(len(LAYERS)), LAYERS))
    for i, name in enumerate(LAYERS):
        albedo, nra = built[i]
        albedo_stack.append(to_u8(albedo))
        nra_stack.append(to_u8(nra))
        if args.preview:
            os.makedirs(args.preview, exist_ok=True)
            Image.fromarray(to_u8(albedo[..., :3])).save(os.path.join(args.preview, f"{i}_{name}_albedo.png"))
            Image.fromarray(to_u8(nra[..., :3])).save(os.path.join(args.preview, f"{i}_{name}_nr.png"))
        print(f"layer {i} {name}: albedo mean {albedo[..., :3].mean(axis=(0, 1)).round(3)}")
    for name in NOISE_SLICES:
        nra_stack.append(to_u8(noise_slice(name)))
    Image.fromarray(np.vstack(albedo_stack), "RGBA").save(os.path.join(args.out, ALBEDO_FILE), optimize=True)
    Image.fromarray(np.vstack(nra_stack), "RGBA").save(os.path.join(args.out, NORMAL_FILE), optimize=True)
    print(f"wrote {len(LAYERS)} layers (+{len(NOISE_SLICES)} noise slices) of {SIZE}x{SIZE} to {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
