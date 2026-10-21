"""G3-04 guards: the underwater view.

The camera dives into a pen and the water is drawn in 3D: the surface's
underside (a second material on the water grid, its own vertex stage the
water's), the sim's visibility as the fog, the light filtered by depth on the
net, the collar and the fish (global uniforms the director writes), caustics
from a generated loop of frames, light shafts, marine snow and a lens that
reads no screen. These checks pin what keeps it right: the underside moves
with the water, the old full-screen post is gone, only the director writes
the water's globals, the caustics texture is the generator's (one frame
rebuilt byte for byte) and is read at the light's own scale, and the net's
fouling grows the documented coverage. Pure static analysis plus numpy: no
engine (UnderwaterChecks.gd drives the same pieces live, UnderwaterShots the
pixels).
"""

import importlib.util
import re
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
WATER = GODOT / "shaders/water_shader.gdshader"
UNDERSIDE = GODOT / "shaders/water/water_underside.gdshader"
LENS = GODOT / "shaders/underwater_lens.gdshader"
LIGHT_INC = GODOT / "shaders/include/underwater_light.gdshaderinc"
NET = GODOT / "shaders/pen_net_shader.gdshader"
NET_TEXTURE = GODOT / "assets/models/pen_cage/textures/net_mesh.png"
CAUSTICS = GODOT / "assets/textures/water/caustics_frames.png"
PROJECT = GODOT / "project.godot"
SCENE = GODOT / "scenes/environment/FjordEnvironment.tscn"
DIRECTOR = "godot/scripts/environment/EnvironmentDirector.gd"
UW_GLOBALS = ("lk_uw", "lk_uw_sun", "lk_uw_light", "lk_uw_sigma", "lk_uw_caustics")
VERTEX_FUNCTIONS = ("bake_at", "component_amplitude", "wave_phase", "displacement")


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def _function(text: str, name: str) -> str:
    """The body of a top-level shader function, whitespace collapsed."""
    match = re.search(r"^[a-z0-9]+ %s\([^)]*\) \{\n(.*?)^\}" % name, text, re.S | re.M)
    assert match, f"{name} not found"
    return " ".join(match.group(1).split())


def _consts(text: str) -> dict[str, list[float]]:
    """A shader's `const float/vec2/vec3 NAME = ...;` values."""
    out = {}
    for kind, name, value in re.findall(r"^const (float|vec2|vec3) (\w+) = ([^;]+);", text, re.M):
        out[name] = [float(v) for v in re.findall(r"-?\d+\.?\d*", value if kind == "float" else value.split("(", 1)[1])]
    return out


def test_the_underside_moves_with_the_water():
    water, under = _read(WATER), _read(UNDERSIDE)
    for name in VERTEX_FUNCTIONS:
        assert _function(water, name) == _function(under, name), f"{name}: the underside's copy drifted from the water's"
    water_vertex = _function(water, "vertex")
    under_vertex = _function(under, "vertex")
    shared = water_vertex.split("v_world = VERTEX;", 1)[0]
    assert under_vertex.startswith(shared), "the underside's vertex stage must displace the grid as the water's does"
    assert "cull_front" in under and "fog_disabled" in under, "seen from below, fogged by its own veil"


def test_the_old_post_is_gone_and_the_lens_reads_no_screen():
    assert not (GODOT / "shaders/underwater_post.gdshader").exists()
    for path in list(GODOT.rglob("*.gd")) + list(GODOT.rglob("*.tscn")) + list(GODOT.rglob("*.gdshader")):
        if "/.godot/" in path.as_posix() or "/addons/" in path.as_posix():
            continue
        code = [line.split("#", 1)[0].split("//", 1)[0] for line in path.read_text(encoding="utf-8").splitlines()]
        assert not any("underwater_post" in line or "underwater_rect" in line for line in code), path
    lens = _read(LENS)
    assert "hint_screen_texture" not in lens and "SCREEN_TEXTURE" not in lens
    assert 'path="res://shaders/underwater_lens.gdshader"' in _read(SCENE)


def test_the_water_globals_are_declared_once_and_written_by_the_director():
    project = _read(PROJECT)
    for name in UW_GLOBALS:
        assert re.search(r"^%s=\{" % name, project, re.M), f"{name} missing from [shader_globals]"
    declared = re.findall(r"^global uniform \w+ (\w+);", _read(LIGHT_INC), re.M)
    assert tuple(declared) == UW_GLOBALS
    writers = set()
    for path in GODOT.rglob("*.gd"):
        rel = path.relative_to(REPO).as_posix()
        if "/addons/" in rel or "/.godot/" in rel:
            continue
        if "global_shader_parameter_set" in path.read_text(encoding="utf-8"):
            writers.add(rel)
    assert writers == {DIRECTOR}
    texture = re.search(r'^lk_uw_caustics=\{\n"type": "sampler2DArray",\n"value": "res://([^"]+)"', project, re.M)
    assert texture and (GODOT / texture.group(1)) == CAUSTICS


def _generator():
    spec = importlib.util.spec_from_file_location("gen_caustics", REPO / "tools" / "gen_caustics.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_the_caustics_are_the_generators_loop():
    gen = _generator()
    image = np.asarray(Image.open(CAUSTICS))
    assert image.shape == (gen.SIZE * gen.FRAMES, gen.SIZE) and image.dtype == np.uint8
    stored = image.astype(np.float64) / 255.0 * gen.I_MAX
    assert abs(stored.mean() - 1.0) < 0.03, "a texel of 1/I_MAX is the mean sunlight"
    importer = _read(Path(str(CAUSTICS) + ".import"))
    assert 'importer="2d_array_texture"' in importer and f"slices/vertical={gen.FRAMES}" in importer
    assert "mipmaps/generate=true" in importer
    inc = _consts(_read(LIGHT_INC))
    assert inc["UW_CAUSTIC_FRAMES"] == [gen.FRAMES] and inc["UW_CAUSTIC_I_MAX"] == [gen.I_MAX]
    # Frame 0 rebuilt: the stored loop is the generator's (seeded, whole cycles a loop).
    rng = np.random.default_rng(gen.SEED)
    comps = gen.spectrum(rng)
    assert all(isinstance(c[4], int) and c[4] >= 1 for c in comps)
    var = sum(0.5 * (2.0 * np.pi * np.hypot(kx, ky) * amp) ** 2 for kx, ky, amp, _, _ in comps)
    first = gen.frame(comps, 0.0, gen.SLOPE_RMS / np.sqrt(var))
    rebuilt = np.clip(first / gen.I_MAX * 255.0 + 0.5, 0, 255).astype(np.uint8)
    assert np.array_equal(rebuilt, image[: gen.SIZE])


def test_the_caustics_add_the_sunlight_without_a_second_pi():
    # Godot folds the diffuse BRDF's 1/pi into a light's energy (RUN_LOG §7,
    # G3-04: a light of energy 0.25 lit a white Lambert quad to 0.2502), so
    # the caustics' added light is the sunlight times (i - 1).
    inc = _read(LIGHT_INC)
    body = _function(inc, "uw_caustic_light")
    assert "0.318" not in body and "/ PI" not in body and "(i - 1.0)" in body


def _bilinear(mip: np.ndarray, n: int) -> np.ndarray:
    size = mip.shape[0]
    u = (np.arange(n) + 0.5) / n * size - 0.5
    i0 = np.floor(u).astype(int)
    f = u - i0
    i0 %= size
    i1 = (i0 + 1) % size
    return (mip[i0][:, i0] * (1 - f)[:, None] * (1 - f)[None, :] + mip[i1][:, i0] * f[:, None] * (1 - f)[None, :]
            + mip[i0][:, i1] * (1 - f)[:, None] * f[None, :] + mip[i1][:, i1] * f[:, None] * f[None, :])


def _smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def _tuft_noise(x: np.ndarray, y: np.ndarray) -> np.ndarray:
    """pen_net_shader tuft_noise: value noise on the shader's hash."""
    def hash2(ix, iy):
        p3 = np.stack([ix, iy, ix], -1) * 0.1031
        p3 = p3 - np.floor(p3)
        p3 = p3 + (p3 * (p3[..., [1, 2, 0]] + 33.33)).sum(-1)[..., None]
        v = (p3[..., 0] + p3[..., 1]) * p3[..., 2]
        return v - np.floor(v)

    ix, iy = np.floor(x), np.floor(y)
    fx, fy = x - ix, y - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = hash2(ix, iy), hash2(ix + 1, iy)
    c, d = hash2(ix, iy + 1), hash2(ix + 1, iy + 1)
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def test_a_fully_fouled_net_covers_the_documented_share():
    k = _consts(_read(NET))
    alpha = np.asarray(Image.open(NET_TEXTURE).convert("RGBA"))[..., 3].astype(np.float64) / 255.0
    mips = [alpha]
    while mips[-1].shape[0] > 1:
        m = mips[-1]
        mips.append(0.25 * (m[0::2, 0::2] + m[1::2, 0::2] + m[0::2, 1::2] + m[1::2, 1::2]))
    tiles = 4
    size = alpha.shape[0]
    base = np.tile(alpha, (tiles, tiles))
    spread = np.tile(_bilinear(mips[int(k["FOULED_SPREAD_LOD"][0])], size), (tiles, tiles))
    tile_m = 0.25
    coords = (np.arange(size * tiles) + 0.5) / size * tile_m
    ym, xm = np.meshgrid(coords, coords, indexing="ij")
    per_m, weight = k["FOULED_TUFT_PER_M"], k["FOULED_TUFT_WEIGHT"]
    t = sum(w * _tuft_noise(xm * s + o, ym * s + o) for s, w, o in zip(per_m, weight, (0.0, 17.0, 34.0)))
    clumps = _smoothstep(*k["FOULED_TUFT_STRETCH"], t)
    reach = spread * 2.0 * clumps
    th = k["FOULED_THRESHOLD"][1]
    grown = np.maximum(base, _smoothstep(th, th + k["FOULED_EDGE"][0], reach) * k["FOULED_DENSITY"][0])
    gain = grown.mean() / base.mean()
    assert abs(base.mean() - 0.15) < 0.005, "the clean tile covers 0.15"
    assert abs(gain - k["FOULED_COVER_GAIN"][0]) < 0.08 * k["FOULED_COVER_GAIN"][0], (
        f"a fully fouled tile covers {gain:.2f}x the clean one; FOULED_COVER_GAIN says {k['FOULED_COVER_GAIN'][0]}")
    assert 2.0 <= gain <= 3.0, "Gansel et al. 2015: a 0.20 net reached 0.50 in ten weeks (2.5x)"
