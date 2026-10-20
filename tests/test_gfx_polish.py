"""gfx-polish-1 guards (wave 4): the graphics findings of the water and
wave-3 merges (docs/RUN_LOG.md section 8), fixed at their source.

- Clouds: the low layer is band-limited to the pass's own pixel (no octave
  finer than two footprints, edges spread over a pixel), so the half- and
  quarter-resolution passes have nothing to stair-step or slice; the menu's
  hero view draws them one pass finer.
- Walls: the terrain's masks read the slope and cavity with the wall ledges
  averaged out (UV2) where a ledge is small on screen, and the cliff bands
  fade by their size on screen (the venetian blinds of harness 18).
- Water: a Beaufort-0 calm is a mirror; the water draws no whitecap inside a
  pen's collar.
- The moods were tuned toward the owner brief, not the goldens: the specs
  and the bars are unchanged.

Pure static analysis plus a numeric replica of the clouds' level of detail;
the UI flow test (GfxPolishChecks.gd) drives the same pieces live and the
screenshot harness judges the pixels.
"""

import math
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
SKY = GODOT / "shaders/sky_shader.gdshader"
TERRAIN = GODOT / "shaders/terrain_shader.gdshader"
WATER = GODOT / "shaders/water_shader.gdshader"
DIRECTOR = GODOT / "scripts/environment/EnvironmentDirector.gd"
SPRAY = GODOT / "scripts/environment/PenSpray.gd"
MOOD = GODOT / "scripts/environment/EnvironmentMood.gd"
GOLDENS = GODOT / "scripts/tools/MoodGoldens.gd"
HARNESS = GODOT / "scripts/tools/ScreenshotHarness.gd"
MENU = GODOT / "scripts/ui/MenuBackdrop.gd"
CHIP = GODOT / "scripts/visual/VesselChip.gd"
PREWARM = GODOT / "scripts/environment/ShaderPrewarm.gd"
FJORD_TERRAIN = GODOT / "scripts/environment/FjordTerrain.gd"
BRIDGE = GODOT / "scripts/environment/TerrainGeneratorBridge.cs"
GENERATOR = REPO / "src/LakskongenSim/Environment/TerrainGenerator.cs"
MESH = REPO / "src/LakskongenSim/Environment/TerrainMesh.cs"


def _code(path: Path) -> str:
    """Source without // (shaders, C#) or # (GDScript) comments."""
    lines = path.read_text(encoding="utf-8").splitlines()
    if path.suffix == ".gd":
        return "\n".join(line.split("#", 1)[0] for line in lines)
    return "\n".join(re.sub(r"(?<!:)//.*$", "", line) for line in lines)


def _float_const(text: str, name: str) -> float:
    m = re.search(rf"const (?:float )?{name}\s*(?::=|=)\s*([-\d.]+)", text)
    assert m, f"{name} not found"
    return float(m.group(1))


def _function(text: str, signature: str) -> str:
    start = text.index(signature)
    depth = 0
    for i in range(text.index("{", start), len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    raise AssertionError(f"{signature}: no body")


# ── Clouds ─────────────────────────────────────────────────────────────────────

def test_the_low_clouds_are_band_limited_to_the_pass_pixel():
    sky = _code(SKY)
    body = _function(sky, "void sky()")
    # The pass's pixel, taken before any branch (derivatives need uniform flow).
    first_branch = body.index("if (")
    angle = body.index("float pixel_angle = max(max(length(dFdx(dir)), length(dFdy(dir))), 1e-5);")
    assert angle < first_branch
    # The screen's passes march band-limited, the radiance map and the
    # mirror's sky the plain march.
    assert body.count("clouds(dir, sun_dir, sky_top, moon_light, cloud_octaves, cloud_steps, 4, pixel_angle)") == 2
    assert "clouds(dir, sun_dir, sky_top, moon_light, 3, 3, 2, 0.0)" in body
    assert "clouds(dir, sun_dir, sky_top, moon_light, cloud_octaves, cloud_steps, 4, 0.0)" in body
    march = _function(sky, "vec4 cloud_layer_low(")
    assert "cloud_lod(t0, dir.y, pixel_angle, octaves)" in march, "the search reads the noise at the slab's entry"
    assert "lod = cloud_lod(edge, dir.y, pixel_angle, octaves);" in march, "the steps into it at the edge, which moves smoothly"
    # No whole-octave step along the screen any more (it drew horizontal seams).
    assert "int(clamp(log2(" not in march
    assert "fbm_lod(q + warp * 1.4, octaves)" in _function(sky, "float low_noise(")
    blurred = _function(sky, "float low_density_blurred(")
    assert "threshold + rise - soft * 0.2 - blur" in blurred and "threshold + rise + soft + blur" in blurred


def test_a_faded_octave_keeps_the_noise_mean():
    fbm_lod = _function(_code(SKY), "float fbm_lod(")
    assert "float w = clamp(octaves - float(i), 0.0, 1.0);" in fbm_lod
    assert "sum += amp * mix(0.5, vnoise(p), w);" in fbm_lod
    assert "norm += amp;" in fbm_lod


def _cloud_lod(t_km: float, sin_e: float, angle: float, octaves: int, sky: str) -> tuple[float, float]:
    """Replica of sky_shader.gdshader cloud_lod()."""
    wavelength = 1.0 / _float_const(sky, "LOW_FREQ_PER_KM")
    min_sin = _float_const(sky, "LOD_MIN_SIN")
    footprint = t_km * angle / max(sin_e, min_sin)
    n = math.log2(wavelength / (2.0 * footprint)) / math.log2(2.03)
    n = min(max(n, 1.0), float(octaves))
    return n, _float_const(sky, "LOD_EDGE_BLUR") * _float_const(sky, "LOW_GRADIENT_PER_KM") * footprint


def test_the_cloud_level_of_detail_is_continuous_and_follows_the_pixel():
    sky = _code(SKY)
    # High's half pass at 1440p: 0.75 x 1440 / 2 rows over a 46 degree view.
    angle = math.radians(46.0) / (1440 * 0.75 / 2)
    rows = []
    for i in range(1, 600):
        e = math.radians(i * 0.05)
        t0 = 1.1 / math.sin(e) if math.sin(e) > 0.05 else 1.1 / 0.05 + (0.05 - math.sin(e)) * 400.0
        rows.append(_cloud_lod(t0, math.sin(e), angle, 6, sky))
    # Row to row the octave count moves smoothly (it stepped by whole octaves).
    assert max(abs(a[0] - b[0]) for a, b in zip(rows, rows[1:])) < 0.25
    # Overhead the full detail, near the horizon the base octaves.
    assert rows[-1][0] == 6.0
    assert rows[0][0] < 2.0
    # The quarter pass's pixels are twice the half pass's: never more detail.
    for t, s in ((3.0, 0.4), (10.0, 0.11), (25.0, 0.04)):
        half = _cloud_lod(t, s, angle, 6, sky)
        quarter = _cloud_lod(t, s, angle * 2.0, 6, sky)
        assert quarter[0] <= half[0] and quarter[1] >= half[1]


def test_the_menu_hero_view_draws_its_clouds_one_pass_finer():
    director = _code(DIRECTOR)
    assert "return maxi(tier_pass - 1, 0) if hero_sky else tier_pass" in director
    assert 'mat.set_shader_parameter("cloud_pass", cloud_pass_now(underwater))' in director
    assert "environment.director.set_hero_sky(true)" in _code(MENU)


# ── Walls ──────────────────────────────────────────────────────────────────────

def test_the_wall_masks_read_the_averaged_slope_where_a_ledge_is_small():
    terrain = _code(TERRAIN)
    assert "macro_terrain = UV2;" in terrain
    frag = terrain[terrain.index("void fragment()"):]
    assert "slope_tan = mix(macro_terrain.x, slope_tan, terrace_near);" in frag
    assert "cavity = mix(macro_terrain.y, cavity, terrace_near);" in frag
    # Before any mask reads them.
    blend = frag.index("slope_tan = mix(macro_terrain.x")
    for use in ("float cliff = smoothstep(0.9, 1.7, slope_tan);", "float bare = smoothstep(", "ground_snow(h, slope_eff"):
        assert frag.index(use) > blend, use
    # The shader's ledge step is the generator's.
    ledge = float(re.search(r"private const float LedgeStepM = ([\d.]+)f;", GENERATOR.read_text()).group(1))
    assert _float_const(terrain, "LEDGE_STEP_M") == ledge
    assert _float_const(terrain, "TERRACE_GONE_PX") < _float_const(terrain, "TERRACE_FULL_PX")
    # The cliff bands fade by their size on screen as well as by distance.
    assert "float band_px = BAND_M / max(footprint, 1e-3);" in frag
    assert "smoothstep(BAND_FADE_GONE_PX, BAND_FADE_FULL_PX, band_px)" in frag
    # The band swells never fold a band: their slopes sum to under 1.
    a = _float_const(terrain, "BAND_SWELL_A_M") / _float_const(terrain, "BAND_SWELL_A_LEN_M")
    b = _float_const(terrain, "BAND_SWELL_B_M") / _float_const(terrain, "BAND_SWELL_B_LEN_M")
    assert a + b < 1.0


def test_every_terrain_mesh_carries_the_averaged_slope():
    mesh = _code(MESH)
    assert "public const int MacroStride = 4;" in mesh
    assert "macro[v * 2] = MathF.Sqrt(gx * gx + gz * gz);" in mesh
    assert "macro[v * 2 + 1] = Cavity(heights, n, spacing, col, row, MacroStride);" in mesh
    assert '["uv2"] = macro,' in _code(BRIDGE)
    assert "arrays[Mesh.ARRAY_TEX_UV2] = chunk[\"uv2\"]" in _code(FJORD_TERRAIN)
    assert "Mesh.ARRAY_TEX_UV2" in _code(PREWARM), "the warm-up triangle has the chunks' vertex layout"


def test_snow_season_rock_is_damp():
    terrain = _code(TERRAIN)
    assert "rock *= 1.0 - ROCK_WINTER_DAMP * rock_damp;" in terrain
    assert 0.2 <= _float_const(terrain, "ROCK_WINTER_DAMP") <= 0.45, "a meltwater film darkens less than rain (0.45 on rock)"


# ── Water ──────────────────────────────────────────────────────────────────────

def test_a_beaufort_0_calm_is_a_mirror():
    director = _code(DIRECTOR)
    assert _float_const(director, "GLASS_FROM_MS") == 0.5, "WMO Beaufort 0: under 0.5 m/s"
    assert _float_const(director, "RIPPLE_GLASS") <= 0.05
    assert 'mat.set_shader_parameter("ripple_strength", ripple_strength_for(wind))' in director


def test_the_water_breaks_no_whitecap_inside_a_pen():
    water = _code(WATER)
    assert "caps *= 1.0 - pen_calm_at(p0);" in water
    max_calm = int(re.search(r"#define MAX_PEN_CALM (\d+)", water).group(1))
    spray = _code(SPRAY)
    assert int(re.search(r"const MAX_CALM_PENS := (\d+)", spray).group(1)) == max_calm
    director = _code(DIRECTOR)
    assert 'mat.set_shader_parameter("pen_calm", padded)' in director
    # The director is the only writer of the water's calm.
    for path in (GODOT / "scripts").rglob("*.gd"):
        if path == DIRECTOR:
            continue
        assert 'set_shader_parameter("pen_calm' not in path.read_text(encoding="utf-8"), path


def test_the_calm_dusk_is_staged_as_a_beaufort_0_mirror_calm():
    harness = _code(HARNESS)
    m = re.search(r"const MIRROR_SKY := \{[^}]*\"wind_ms\": ([\d.]+)", harness)
    assert m, "18 stages its own calm"
    assert float(m.group(1)) <= 0.5, "owner reference 2: wind <= 0.5 m/s"
    assert "ws.call(\"apply_sky_context\", MIRROR_SKY)" in harness
    assert re.search(r"const MIN_MIRROR_SSIM := 0\.5\b", harness)
    assert re.search(r"const MIN_MIRROR_SSIM_GAIN := 0\.1\b", harness)


# ── Moods ──────────────────────────────────────────────────────────────────────

def test_the_mood_goldens_kept_their_targets_and_bars():
    mood = MOOD.read_text(encoding="utf-8")
    assert 'const SPEC_DELTA_E := 10.0' in mood
    assert '"midnattssol": {"sky": ["#F3D4A4"], "water": ["#1E3340", "#2A4655"],' in mood
    assert '"hoest": {"sky": ["#5E8DB8", "#D5E0E6"], "water": ["#0D2B3B", "#1F4D5E"],' in mood
    assert 'const MIN_PHASH_DELTA := 9' in GOLDENS.read_text(encoding="utf-8")


# ── Chips ──────────────────────────────────────────────────────────────────────

def test_vessel_chips_keep_on_screen():
    chip = _code(CHIP)
    assert "keep_on_screen(cam, get_viewport().get_visible_rect().size)" in chip
    assert "static func edge_shift_for(" in chip
    assert '_material.set_shader_parameter("offset_px", Vector2(0.0, LIFT_PX) + edge_shift)' in chip
