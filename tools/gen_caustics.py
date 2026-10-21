#!/usr/bin/env python3.12
"""Animated caustics for the underwater view (roadmap G3-04).

Generates godot/assets/textures/water/caustics_frames.png: FRAMES square
frames of SIZE x SIZE stacked vertically (imported as a Texture2DArray,
slices/vertical = FRAMES), one loop of the light a wavy surface gathers on
a plane under it. Tileable in space by construction and seamless in time:
the frame after the last is the first.

The model (written for the project; no third-party code):
- the surface is a periodic height field of short waves: plane waves on the
  tile's integer wavevectors with |k| in K_MIN..K_MAX cycles a tile, an
  amplitude spectrum falling as |k|^-SPECTRUM, uniform directions, random
  phases (seeded), each turning a whole number of times a loop (its rate
  follows deep-water dispersion, omega ~ sqrt(k), rounded to whole cycles),
  scaled so the surface's RMS slope is SLOPE_RMS;
- light falls straight down through it and refracts (Snell, n = 1.333); a
  ray from (x, y) reaches the plane DEPTH below (tile units) displaced by
  DEPTH * the refracted ray's horizontal over its vertical, wrapped round the
  tile; SAMPLES x SAMPLES rays a frame are binned bilinearly;
- the caustic is the ray density over its mean (1 where light is spread
  evenly, bright on the folds, darker between), blurred by BLUR_PX against
  the binning noise, stored as min(I / I_MAX, 1) in 8 bits (linear):
  the shader reads I = texel * I_MAX.

The shader (shaders/include/underwater_light.gdshaderinc) tiles a frame
every few metres of world space projected along the sunlight, blends the two
nearest frames by its clock and fades the pattern with depth.

Needs numpy and Pillow (python3.12 on the dev machines). Run:
    python3.12 tools/gen_caustics.py
"""

from __future__ import annotations

import os

import numpy as np
from PIL import Image

SIZE = 256
FRAMES = 32
SAMPLES = 768
K_MIN = 2
K_MAX = 11
SPECTRUM = 2.4
SLOPE_RMS = 0.075
DEPTH = 0.9
IOR = 1.333
I_MAX = 4.0
BLUR_PX = 0.7
SEED = 20261021
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "godot", "assets", "textures", "water", "caustics_frames.png")


def spectrum(rng: np.random.Generator) -> list[tuple[int, int, float, float, int]]:
    """(kx, ky, amplitude, phase, cycles a loop) for every component."""
    comps = []
    for kx in range(-K_MAX, K_MAX + 1):
        for ky in range(-K_MAX, K_MAX + 1):
            k = float(np.hypot(kx, ky))
            if k < K_MIN or k > K_MAX:
                continue
            amp = k ** -SPECTRUM * rng.rayleigh(1.0)
            phase = float(rng.uniform(0.0, 2.0 * np.pi))
            # Deep-water dispersion, omega ~ sqrt(k): whole cycles a loop.
            cycles = max(1, int(round(2.0 * np.sqrt(k))))
            comps.append((kx, ky, float(amp), phase, cycles))
    return comps


def slopes(comps, t: float, u: np.ndarray, v: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """The surface's slope (dh/dx, dh/dy) at time t (0..1 of the loop)."""
    hx = np.zeros_like(u)
    hy = np.zeros_like(u)
    for kx, ky, amp, phase, cycles in comps:
        arg = 2.0 * np.pi * (kx * u + ky * v - cycles * t) + phase
        s = -np.sin(arg) * amp * 2.0 * np.pi
        hx += s * kx
        hy += s * ky
    return hx, hy


def frame(comps, t: float, scale: float) -> np.ndarray:
    coords = (np.arange(SAMPLES) + 0.5) / SAMPLES
    u, v = np.meshgrid(coords, coords, indexing="xy")
    hx, hy = slopes(comps, t, u, v)
    hx *= scale
    hy *= scale
    # The surface normal (up) and the vertical ray refracted through it.
    n = np.stack([-hx, np.ones_like(hx), -hy], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    d = np.array([0.0, -1.0, 0.0])
    eta = 1.0 / IOR
    cos_i = -(n @ d)
    k = 1.0 - eta * eta * (1.0 - cos_i * cos_i)
    r = eta * d[None, None, :] + (eta * cos_i - np.sqrt(np.maximum(k, 0.0)))[..., None] * n
    land_u = (u + DEPTH * r[..., 0] / -r[..., 1]) % 1.0
    land_v = (v + DEPTH * r[..., 2] / -r[..., 1]) % 1.0
    # Bilinear binning on the output grid, wrapped round the tile.
    fx = land_u * SIZE - 0.5
    fy = land_v * SIZE - 0.5
    x0 = np.floor(fx).astype(int)
    y0 = np.floor(fy).astype(int)
    wx = fx - x0
    wy = fy - y0
    hist = np.zeros((SIZE, SIZE))
    for ox, oy, w in ((0, 0, (1 - wx) * (1 - wy)), (1, 0, wx * (1 - wy)), (0, 1, (1 - wx) * wy), (1, 1, wx * wy)):
        np.add.at(hist, ((y0 + oy) % SIZE, (x0 + ox) % SIZE), w)
    hist /= hist.mean()
    return blur(hist, BLUR_PX)


def blur(img: np.ndarray, sigma: float) -> np.ndarray:
    """Periodic Gaussian blur through the FFT."""
    f = np.fft.fftfreq(SIZE)
    gx, gy = np.meshgrid(f, f, indexing="xy")
    kernel = np.exp(-2.0 * (np.pi * sigma) ** 2 * (gx * gx + gy * gy))
    return np.real(np.fft.ifft2(np.fft.fft2(img) * kernel))


def main() -> None:
    rng = np.random.default_rng(SEED)
    comps = spectrum(rng)
    # Scale the surface to the RMS slope (each component's slope variance is
    # (2 pi k a)^2 / 2 per axis pair).
    var = sum(0.5 * (2.0 * np.pi * np.hypot(kx, ky) * amp) ** 2 for kx, ky, amp, _, _ in comps)
    scale = SLOPE_RMS / np.sqrt(var)
    frames = [frame(comps, i / FRAMES, scale) for i in range(FRAMES)]
    stack = np.concatenate(frames, axis=0)
    data = np.clip(stack / I_MAX * 255.0 + 0.5, 0, 255).astype(np.uint8)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    Image.fromarray(data, "L").save(OUT, optimize=True)
    mean = float(np.mean(stack))
    p99 = float(np.percentile(stack, 99))
    print(f"{OUT}: {SIZE}x{SIZE * FRAMES}, {FRAMES} frames, {len(comps)} waves, mean {mean:.3f}, p99 {p99:.2f}, "
          f"clipped {float(np.mean(stack > I_MAX)) * 100:.2f} %")


if __name__ == "__main__":
    main()
