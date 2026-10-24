"""C1 (graphics handover §2 Task C): the storm's whitecaps no longer tile into
rectangles.

The wave-group field that gathers the caps into patches was one layer of the
foam texture's smooth noise on a 260 m tile; every storm frame printed that
tile as a lattice of rectangular patches (wave4b frame review,
finding_whitecap_patches.jpg). It is now two layers on incommensurate tiles,
the second turned, with their sum mapped back to a uniform 0-1 so the
director's threshold calibration (WHITECAP_Z_SCALE / OFFSET) still holds.

The field is rebuilt here from the shader's own constants and the shipped
texture, then measured: its strongest repeat beyond 150 m (autocorrelation)
and its distribution.
"""

from __future__ import annotations

import math
import re
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[1]
SHADER = REPO / "godot/shaders/water_shader.gdshader"
FOAM = REPO / "godot/assets/textures/water_foam.png"
N = 512
PX_M = 4.0  # a 2,048 m square of sea


def _const(src: str, name: str) -> float:
    return float(re.search(rf"const float {name} = ([\d.]+);", src).group(1))


def _sample(channel: np.ndarray, u: np.ndarray, v: np.ndarray) -> np.ndarray:
    """Bilinear, wrapping lookup (the sampler's repeat_enable, filter_linear)."""
    size = channel.shape[0]
    x = u * size - 0.5
    y = v * size - 0.5
    x0 = np.floor(x).astype(int)
    y0 = np.floor(y).astype(int)
    fx = x - x0
    fy = y - y0
    x0 %= size
    y0 %= size
    x1 = (x0 + 1) % size
    y1 = (y0 + 1) % size
    top = channel[y0, x0] * (1 - fx) + channel[y0, x1] * fx
    bottom = channel[y1, x0] * (1 - fx) + channel[y1, x1] * fx
    return top * (1 - fy) + bottom * fy


def _uniform_of_pair(s: np.ndarray) -> np.ndarray:
    return np.where(s < 1.0, 0.5 * s * s, 1.0 - 0.5 * (2.0 - s) ** 2)


def _strongest_repeat(field: np.ndarray, min_m: float) -> float:
    f = field - field.mean()
    spectrum = np.fft.fft2(f)
    ac = np.real(np.fft.ifft2(spectrum * np.conj(spectrum)))
    ac /= ac[0, 0]
    lag = np.fft.fftfreq(field.shape[0]) * field.shape[0] * PX_M
    lx, ly = np.meshgrid(lag, lag)
    ac[np.hypot(lx, ly) < min_m] = 0.0
    return float(ac.max())


def _fields() -> tuple[np.ndarray, np.ndarray]:
    src = SHADER.read_text(encoding="utf-8")
    smooth = np.asarray(Image.open(FOAM)).astype(np.float64)[..., 3] / 255.0
    x = np.arange(N) * PX_M
    X, Y = np.meshgrid(x, x)
    single = _sample(smooth, X / 260.0 + 0.21, Y / 260.0 + 0.21)
    tile_a = _const(src, "GROUP_TILE_A_M")
    tile_b = _const(src, "GROUP_TILE_B_M")
    c = _const(src, "GROUP_TURN_COS")
    s = _const(src, "GROUP_TURN_SIN")
    # GLSL mat2(col0, col1) * v: col0 = (c, s), col1 = (-s, c).
    tx = c * X - s * Y
    ty = s * X + c * Y
    layered = _uniform_of_pair(_sample(smooth, X / tile_a + 0.21, Y / tile_a + 0.21)
                               + _sample(smooth, tx / tile_b + 0.63, ty / tile_b + 0.37))
    return single, layered


def test_the_shader_draws_the_group_field_from_two_turned_layers():
    src = SHADER.read_text(encoding="utf-8")
    assert "bq / 260.0" not in src, "the single 260 m group layer is gone"
    assert "float group = uniform_of_pair(group_a + group_b);" in src
    assert "GROUP_SPREAD * (group - 0.5)" in src
    c = _const(src, "GROUP_TURN_COS")
    s = _const(src, "GROUP_TURN_SIN")
    assert abs(c * c + s * s - 1.0) < 1e-5, "the turn is a rotation"
    ratio = _const(src, "GROUP_TILE_B_M") / _const(src, "GROUP_TILE_A_M")
    # Incommensurate: no small whole-number ratio within 2 % (the pair repeats far out).
    for p in range(1, 6):
        for q in range(1, 6):
            assert abs(ratio - p / q) > 0.02 * ratio, f"tiles {p}:{q} repeat together"


def test_the_group_field_no_longer_repeats_as_a_lattice():
    single, layered = _fields()
    before = _strongest_repeat(single, 150.0)
    after = _strongest_repeat(layered, 150.0)
    # Measured: 0.92 for the single tile (a near-copy every 260 m), 0.44 for the pair.
    assert before > 0.85, before
    assert after < 0.55, after


def test_the_group_field_stays_uniform_so_the_cover_calibration_holds():
    _, layered = _fields()
    assert abs(layered.mean() - 0.5) < 0.01
    assert abs(layered.std() - 1.0 / math.sqrt(12.0)) < 0.006
    # Deciles of a uniform field.
    deciles = np.quantile(layered, np.linspace(0.1, 0.9, 9))
    assert np.max(np.abs(deciles - np.linspace(0.1, 0.9, 9))) < 0.02
