#!/usr/bin/env python3
"""Deterministic procedural texture generator for the Lakskongen fjord renderer.

Uses only the Python standard library and Pillow (PIL). No numpy, no ImageMagick.

Generates:
  godot/assets/textures/terrain_grass.png        + terrain_grass_normal.png
  godot/assets/textures/terrain_rock.png         + terrain_rock_normal.png
  godot/assets/textures/terrain_snow.png         + terrain_snow_normal.png
  godot/assets/textures/terrain_sand.png         + terrain_sand_normal.png
  godot/assets/textures/terrain_moss.png         + terrain_moss_normal.png
  godot/assets/textures/water_foam.png
  godot/assets/textures/water_caustics.png
  godot/assets/textures/water/ocean_normal_1.png
  godot/assets/textures/water/ocean_normal_2.png

All textures are seamless/tileable. Everything is seeded and deterministic.

Run:
    python3 tools/gen_textures.py
"""

from __future__ import annotations

import math
import os

from PIL import Image

SIZE = 512
TAU = math.pi * 2.0

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "godot", "assets", "textures")
WATER_OUT = os.path.join(OUT, "water")

SEED = 1337


# --------------------------------------------------------------------------- #
# Deterministic, tileable periodic noise (sum of integer-frequency sinusoids)  #
# --------------------------------------------------------------------------- #
def _phase(*key) -> float:
    """Deterministic pseudo-random phase in [0, TAU) from integer key parts."""
    h = 2166136261
    for k in key:
        h ^= (int(k) + 0x9E3779B9) & 0xFFFFFFFF
        h = (h * 16777619) & 0xFFFFFFFF
    return (h / 0xFFFFFFFF) * TAU


def _hash2(ix: int, iy: int, seed: int) -> float:
    """Deterministic hash of an integer lattice point -> [0, 1)."""
    n = (ix * 374761393 + iy * 668265263 + seed * 1274126177) & 0xFFFFFFFF
    n = (n ^ (n >> 13)) & 0xFFFFFFFF
    n = (n * 1274126177) & 0xFFFFFFFF
    n = (n ^ (n >> 16)) & 0xFFFFFFFF
    return n / 0xFFFFFFFF


def value_noise(u: float, v: float, period_x: int, period_y: int,
                seed: int) -> float:
    """Seamless smooth value noise. (u, v) in [0, 1), integer lattice period."""
    x = u * period_x
    y = v * period_y
    x0 = int(math.floor(x))
    y0 = int(math.floor(y))
    tx = x - x0
    ty = y - y0
    sx = tx * tx * (3.0 - 2.0 * tx)
    sy = ty * ty * (3.0 - 2.0 * ty)
    x0m = x0 % period_x
    y0m = y0 % period_y
    x1m = (x0 + 1) % period_x
    y1m = (y0 + 1) % period_y
    n00 = _hash2(x0m, y0m, seed)
    n10 = _hash2(x1m, y0m, seed)
    n01 = _hash2(x0m, y1m, seed)
    n11 = _hash2(x1m, y1m, seed)
    nx0 = n00 + (n10 - n00) * sx
    nx1 = n01 + (n11 - n01) * sx
    return nx0 + (nx1 - nx0) * sy


def fbm(u: float, v: float, octaves: int, base_freq: int, seed: int,
        ridged: bool = False, stretch: float = 1.0) -> float:
    """Tileable fractal value noise sampled at normalized (u, v) in [0, 1).

    Returns roughly [0, 1]. Integer lattice periods guarantee seamlessness.
    """
    value = 0.0
    amp = 0.5
    norm = 0.0
    freq = float(base_freq)
    for o in range(octaves):
        px = max(1, int(round(freq)))
        py = max(1, int(round(freq * stretch)))
        n = value_noise(u, v, px, py, seed + o * 131)
        if ridged:
            n = 1.0 - abs(2.0 * n - 1.0)
        value += amp * n
        norm += amp
        amp *= 0.5
        freq *= 2.0
    return value / norm


def clamp01(x: float) -> float:
    return 0.0 if x < 0.0 else (1.0 if x > 1.0 else x)


def smoothstep(a: float, b: float, x: float) -> float:
    if b == a:
        return 0.0
    t = clamp01((x - a) / (b - a))
    return t * t * (3.0 - 2.0 * t)


def mix3(c1, c2, t: float):
    t = clamp01(t)
    return (c1[0] + (c2[0] - c1[0]) * t,
            c1[1] + (c2[1] - c1[1]) * t,
            c1[2] + (c2[2] - c1[2]) * t)


# --------------------------------------------------------------------------- #
# Image helpers                                                               #
# --------------------------------------------------------------------------- #
def _rgb_bytes(px):
    """Pack a flat list of (r,g,b) floats [0,1] into a bytes buffer."""
    buf = bytearray(len(px) * 3)
    i = 0
    for r, g, b in px:
        buf[i] = int(clamp01(r) * 255.0 + 0.5)
        buf[i + 1] = int(clamp01(g) * 255.0 + 0.5)
        buf[i + 2] = int(clamp01(b) * 255.0 + 0.5)
        i += 3
    return bytes(buf)


def save_rgb(path: str, px) -> None:
    Image.frombytes("RGB", (SIZE, SIZE), _rgb_bytes(px)).save(path, optimize=True)


def save_rgba(path: str, px, alpha) -> None:
    buf = bytearray(len(px) * 4)
    i = 0
    for (r, g, b), a in zip(px, alpha):
        buf[i] = int(clamp01(r) * 255.0 + 0.5)
        buf[i + 1] = int(clamp01(g) * 255.0 + 0.5)
        buf[i + 2] = int(clamp01(b) * 255.0 + 0.5)
        buf[i + 3] = int(clamp01(a) * 255.0 + 0.5)
        i += 4
    Image.frombytes("RGBA", (SIZE, SIZE), bytes(buf)).save(path, optimize=True)


def height_to_normal(height, strength: float) -> Image.Image:
    """Convert a size*size height field into a tangent-space normal map (RGB)."""
    n = SIZE
    out = bytearray(n * n * 3)
    for y in range(n):
        ym = (y - 1) % n
        yp = (y + 1) % n
        row = y * n
        for x in range(n):
            xm = (x - 1) % n
            xp = (x + 1) % n
            dx = height[row + xp] - height[row + xm]
            dy = height[yp * n + x] - height[ym * n + x]
            nx = -dx * strength
            ny = -dy * strength
            nz = 1.0
            inv = 1.0 / math.sqrt(nx * nx + ny * ny + nz * nz)
            i = (row + x) * 3
            out[i] = int((nx * inv * 0.5 + 0.5) * 255.0 + 0.5)
            out[i + 1] = int((ny * inv * 0.5 + 0.5) * 255.0 + 0.5)
            out[i + 2] = int((nz * inv * 0.5 + 0.5) * 255.0 + 0.5)
    return Image.frombytes("RGB", (n, n), bytes(out))


# --------------------------------------------------------------------------- #
# Terrain textures                                                            #
# --------------------------------------------------------------------------- #
def gen_terrain(name, seed, dark, light, height_octaves, base_freq,
               normal_strength, ridged=False, grain=0.03, detail_stretch=1.0):
    """Generate a seamless albedo + matching normal map for a terrain layer."""
    px = []
    height = [0.0] * (SIZE * SIZE)
    inv = 1.0 / SIZE
    for y in range(SIZE):
        v = y * inv
        for x in range(SIZE):
            u = x * inv
            h = fbm(u, v, height_octaves, base_freq, seed,
                    ridged=ridged, stretch=detail_stretch)
            height[y * SIZE + x] = h
            t = smoothstep(0.15, 0.85, h)
            r, g, b = mix3(dark, light, t)
            # Fine grain to break up flat areas.
            gr = fbm(u, v, 3, base_freq * 6, seed + 7)
            gv = (gr - 0.5) * grain
            px.append((r + gv, g + gv, b + gv))
    save_rgb(os.path.join(OUT, f"{name}.png"), px)
    height_to_normal(height, normal_strength).save(
        os.path.join(OUT, f"{name}_normal.png"), optimize=True)
    print(f"  terrain {name:14s} -> albedo + normal map")


def gen_grass():
    gen_terrain("terrain_grass", seed=11,
                dark=(0.310, 0.451, 0.290), light=(0.635, 0.741, 0.545),
                height_octaves=5, base_freq=4, normal_strength=2.2,
                grain=0.045, detail_stretch=1.0)


def gen_rock():
    # Ridged, horizontally-stretched noise gives cracked strata on cliff faces.
    gen_terrain("terrain_rock", seed=23,
                dark=(0.255, 0.286, 0.333), light=(0.620, 0.651, 0.698),
                height_octaves=6, base_freq=5, normal_strength=3.4,
                ridged=True, grain=0.06, detail_stretch=2.0)


def gen_snow():
    gen_terrain("terrain_snow", seed=37,
                dark=(0.741, 0.816, 0.878), light=(0.969, 0.984, 0.996),
                height_octaves=5, base_freq=3, normal_strength=1.6,
                grain=0.02)


def gen_sand():
    gen_terrain("terrain_sand", seed=53,
                dark=(0.573, 0.529, 0.435), light=(0.839, 0.800, 0.690),
                height_octaves=6, base_freq=6, normal_strength=1.8,
                grain=0.05)


def gen_moss():
    gen_terrain("terrain_moss", seed=71,
                dark=(0.169, 0.318, 0.216), light=(0.361, 0.549, 0.376),
                height_octaves=6, base_freq=5, normal_strength=2.6,
                ridged=True, grain=0.05)


# --------------------------------------------------------------------------- #
# Water textures                                                              #
# --------------------------------------------------------------------------- #
def gen_foam():
    """Tileable white foam / wisp mask. RGB is near-white, A is the wisp mask."""
    px = []
    alpha = []
    inv = 1.0 / SIZE
    for y in range(SIZE):
        v = y * inv
        for x in range(SIZE):
            u = x * inv
            # Anisotropic (wind-stretched) wisps plus a broader base field.
            wisps = fbm(u, v, 5, 3, 9001, ridged=True, stretch=0.18)
            base = fbm(u, v, 4, 2, 9002)
            a = clamp01((wisps * 0.7 + base * 0.3 - 0.42) * 2.6)
            a = a * a * (3.0 - 2.0 * a)
            r = 0.94 + base * 0.06
            px.append((r, r, 1.0))
            alpha.append(a)
    save_rgba(os.path.join(OUT, "water_foam.png"), px, alpha)
    print("  water   water_foam         -> RGBA wisp mask")


def gen_caustics():
    """Subtle tileable caustic web: dark base with thin bright light lines."""
    px = []
    inv = 1.0 / SIZE
    for y in range(SIZE):
        v = y * inv
        for x in range(SIZE):
            u = x * inv
            a = math.sin(TAU * (3.0 * u + 2.0 * v) + _phase(1))
            b = math.sin(TAU * (2.0 * u - 3.0 * v) + _phase(2))
            c = math.sin(TAU * (5.0 * u + 1.0 * v) + _phase(3))
            # Focused caustic lines appear where wavefronts intersect.
            web = abs(a * b * c)
            val = clamp01(1.0 - web)
            val = val * val * val * val * val * val  # sparse bright filaments
            # Slight blue tint; kept subtle so it reads as refracted light.
            px.append((val * 0.80, val * 0.92, val))
    save_rgb(os.path.join(OUT, "water_caustics.png"), px)
    print("  water   water_caustics     -> RGB")


def gen_ocean_normal(seed, path):
    """Tileable water surface normal map from wind-stretched value-noise ripples."""
    n = SIZE
    height = [0.0] * (n * n)
    inv = 1.0 / n
    for y in range(n):
        v = y * inv
        for x in range(n):
            u = x * inv
            # Broad swell + fine chop, mildly stretched by wind.
            h = fbm(u, v, 3, 3, seed, stretch=1.6)
            h += 0.45 * fbm(u, v, 4, 9, seed + 555, stretch=1.4)
            height[y * n + x] = h
    height_to_normal(height, 1.1).save(path, optimize=True)
    print(f"  water   {os.path.basename(path):16s} -> RGB normal")


# --------------------------------------------------------------------------- #
def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(WATER_OUT, exist_ok=True)
    print(f"[gen_textures] generating {SIZE}x{SIZE} tiles (seed={SEED})")
    gen_grass()
    gen_rock()
    gen_snow()
    gen_sand()
    gen_moss()
    gen_foam()
    gen_caustics()
    gen_ocean_normal(seed=4242, path=os.path.join(WATER_OUT, "ocean_normal_1.png"))
    gen_ocean_normal(seed=7777, path=os.path.join(WATER_OUT, "ocean_normal_2.png"))
    print("[gen_textures] done")


if __name__ == "__main__":
    main()