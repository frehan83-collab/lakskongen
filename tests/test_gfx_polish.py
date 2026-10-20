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
MIRROR_INC = GODOT / "shaders/water/water_mirror.gdshaderinc"
PERF = GODOT / "scripts/tools/PerfHarness.gd"


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


def _function_gd(text: str, signature: str) -> str:
    """A GDScript function's text, up to the next top-level declaration."""
    start = text.index(signature)
    nxt = re.search(r"\n(?:func |static func |const |var |## )", text[start + len(signature):])
    return text[start:start + len(signature) + (nxt.start() if nxt else len(text))]


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
    assert "vec2 lod_in = cloud_lod(edge, dir.y, pixel_angle, octaves);" in march, "the steps into it at the edge, which moves smoothly"
    # No whole-octave step along the screen any more (it drew horizontal seams).
    assert "int(clamp(log2(" not in march
    assert "fbm_lod(q + warp * 1.4, octaves)" in _function(sky, "float low_noise(")
    blurred = _function(sky, "float low_density_blurred(")
    assert "threshold + rise - soft * 0.2 - blur" in blurred and "threshold + rise + soft + blur" in blurred


def test_a_fading_octave_hands_its_share_to_the_coarser_ones():
    # Weighted in the sum and in the normalisation (as fbm() drops a whole
    # octave), so the field keeps its spread and the clouds their cover; a
    # faded octave pulled toward the mean (the wip) shrank the spread and
    # turned far broken cloud into one deck.
    fbm_lod = _function(_code(SKY), "float fbm_lod(")
    assert "float w = clamp(octaves - float(i), 0.0, 1.0);" in fbm_lod
    assert "sum += amp * w * vnoise(p);" in fbm_lod
    assert "norm += amp * w;" in fbm_lod


def test_a_thin_cloud_does_not_hide_the_one_behind_it():
    # The march goes on into a cloud until it is opaque (not three steps into
    # its fringe) and looks for a second cloud behind a thin one; no threshold
    # on the light let through decides how far it goes (contour lines).
    sky = _code(SKY)
    assert re.search(r"const int INNER_MAX_STEPS = (\d+);", sky) and int(re.search(r"const int INNER_MAX_STEPS = (\d+);", sky).group(1)) > 3
    assert int(re.search(r"const int CLOUD_HITS = (\d+);", sky).group(1)) >= 2
    march = _function(sky, "vec4 cloud_layer_low(")
    assert "for (int hit_i = 0; hit_i < CLOUD_HITS; hit_i++)" in march
    assert "find_cloud_edge(dir, t_from, t1, grid_origin, dc, coarse, lod, edge)" in march
    assert "for (int i = 0; i < INNER_MAX_STEPS; i++)" in march
    assert "if (transmittance < 0.02) {" in march
    assert "THIN_TRANSMITTANCE" not in sky
    # The search grid is anchored to the eye (rows test the same points).
    search = _function(sky, "bool find_cloud_edge(")
    assert "float t = grid_origin + (floor((t_from - grid_origin) / dc) + 0.5) * dc;" in search
    assert "float grid_origin = 0.0;" in march and "grid_origin = t0;" in march


def _cloud_lod(t_km: float, sin_e: float, angle: float, octaves: int, sky: str) -> tuple[float, float]:
    """Replica of sky_shader.gdshader cloud_lod()."""
    wavelength = 1.0 / _float_const(sky, "LOW_FREQ_PER_KM")
    min_sin = _float_const(sky, "LOD_MIN_SIN")
    footprint = t_km * angle / math.sqrt(max(sin_e, min_sin))
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
    # Overhead the full detail, near the horizon the coarse octaves, more of
    # them than the stretch along the view alone would leave (its geometric
    # mean with the width across the view).
    assert rows[-1][0] == 6.0
    assert rows[0][0] < 3.0
    e = math.radians(0.05)
    stretch_only = math.log2((1.0 / _float_const(sky, "LOW_FREQ_PER_KM")) / (2.0 * (1.1 / math.sin(e)) * angle / max(math.sin(e), _float_const(sky, "LOD_MIN_SIN")))) / math.log2(2.03)
    assert rows[0][0] > max(stretch_only, 1.0)
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

def test_the_wall_masks_read_the_ground_without_its_terraces_where_a_ledge_is_small():
    terrain = _code(TERRAIN)
    assert "varying vec4 macro_terrain;" in terrain and "macro_terrain = CUSTOM0;" in terrain
    frag = terrain[terrain.index("void fragment()"):]
    assert "slope_tan = mix(macro_terrain.x, slope_tan, terrace_near);" in frag
    assert "cavity = mix(macro_terrain.y, cavity, terrace_near);" in frag
    # Before any mask reads them.
    blend = frag.index("slope_tan = mix(macro_terrain.x")
    for use in ("float cliff = smoothstep(0.9, 1.7, slope_tan);", "float bare = smoothstep(", "ground_snow(h, slope_eff"):
        assert frag.index(use) > blend, use
    # Where the terraces do not draw, the snow takes the mean of what the
    # flats (slope x (1 - k)) and the risers (x (1 + k)) hold, the bare
    # rock half of each.
    assert "float terrace_spread = macro_terrain.z * (1.0 - terrace_near);" in frag
    assert "snow_amount = 0.5 * (ground_snow(h, snow_slope(slope_tan * (1.0 - terrace_spread), ledge, gully), north, hollow, macro)" in frag
    assert "+ ground_snow(h, snow_slope(slope_tan * (1.0 + terrace_spread), ledge, gully), north, hollow, macro));" in frag
    assert "smoothstep(soil_limit, soil_limit + bare_span, slope_tan * (1.0 - terrace_spread))" in frag
    assert "smoothstep(soil_limit, soil_limit + bare_span, slope_tan * (1.0 + terrace_spread))" in frag
    # The strength the shader reads is the generator's (at most).
    strength = float(re.search(r"private const float LedgeStrength = ([\d.]+)f;", GENERATOR.read_text()).group(1))
    assert strength == 0.28
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


def test_every_terrain_mesh_carries_the_ground_without_its_terraces():
    mesh = _code(MESH)
    assert "public const int MacroStride = 4;" in mesh
    assert "ground[i] = heights[i] - wall.Offsets[i];" in mesh
    assert "var (mx, my, mz) = GridNormal(ground, n, worldSize, col, row, outsideHeight);" in mesh
    assert "macro[m + 1] = Cavity(ground, n, spacing, col, row);" in mesh
    assert "macro[m + 2] = ledgeStrength != null ? ledgeStrength[g] : 0f;" in mesh
    generator = _code(GENERATOR)
    assert "public float[] GenerateHeightmap(out WallLedges ledges)" in generator
    assert "ledgeStrength = LedgeStrength * mask;" in generator
    bridge = _code(BRIDGE)
    assert "_generator.GenerateHeightmap(out WallLedges ledges)" in bridge and '["custom0"] = macro,' in bridge
    assert "_generator.FarFieldHeightAt, ledges)" in bridge
    terrain_gd = _code(FJORD_TERRAIN)
    assert "const MACRO_FORMAT_FLAGS := Mesh.ARRAY_CUSTOM_RGBA_FLOAT << Mesh.ARRAY_FORMAT_CUSTOM0_SHIFT" in terrain_gd
    assert 'arrays[Mesh.ARRAY_CUSTOM0] = chunk["custom0"]' in terrain_gd
    assert "mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays, [], {}, flags)" in terrain_gd
    prewarm = _code(PREWARM)
    assert "Mesh.ARRAY_CUSTOM0" in prewarm and "FjordTerrain.MACRO_FORMAT_FLAGS" in prewarm, "the warm-up triangle has the chunks' vertex layout"


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
    # The chip stays (slid in) while any of its boat shows: the anchor may
    # be half the boat's length (projected) past an edge.
    assert "var reach := OFFSCREEN_PX + boat_reach_px(cam, view_size, anchor, vessel_half_length_m())" in chip
    assert 'var length: Variant = boat.get("length_m")' in chip
    assert "return half_length_m * focal_px / depth" in chip
    # The text is culled no sooner than the pill (an empty pill showed in 08b).
    assert "_label.extra_cull_margin = _quad.extra_cull_margin" in chip


# ── The mirror's edges ─────────────────────────────────────────────────────────

def test_the_mirror_holds_to_the_screen_edges():
    # The mirror's view is the camera's: the fade runs only beyond its edges
    # (a ripple pushing the lookup out), the lookup clamped to the edge
    # texels; it ran inside them and drew a strip of sky reflection down both
    # sides of a calm screen.
    inc = _code(MIRROR_INC)
    sample = _function(inc, "vec4 planar_sample(")
    assert "w *= 1.0 - smoothstep(0.0, PLANAR_EDGE, -min(edge.x, edge.y));" in sample
    assert "uv = clamp(uv, vec2(0.0), vec2(1.0));" in sample
    assert sample.index("uv = clamp(uv") < sample.index("texture(planar_reflection, uv)")
    assert "w *= smoothstep(0.0, PLANAR_EDGE, min(edge.x, edge.y));" not in sample


# ── The harness's stills ───────────────────────────────────────────────────────

def test_the_menu_and_aurora_stills_pin_their_clouds_and_curtains():
    # Clouds drift and curtains move with the time a run has played: 01 and
    # 09 are shot at fixed points (09's reflection check passed or failed
    # with where they happened to be).
    harness = _code(HARNESS)
    assert re.search(r"const MENU_CLOUD_DRIFT_KM := Vector2\(", harness)
    assert re.search(r"const AURORA_CLOUD_DRIFT_KM := Vector2\(", harness)
    assert re.search(r"const AURORA_SKY_TIME_S := [\d.]+", harness)
    menu = _function_gd(harness, "func _capture_main_menu()")
    assert 'menu_director.call("stage_cloud_drift", MENU_CLOUD_DRIFT_KM)' in menu
    aurora = _function_gd(harness, "func _capture_fjord_night_aurora()")
    staged = aurora.index('director.call("stage_sky_time", AURORA_SKY_TIME_S)')
    assert aurora.index('director.call("stage_cloud_drift", AURORA_CLOUD_DRIFT_KM)') < staged
    assert staged < aurora.index('_shoot("09_fjord_night_aurora", true)')
    director = _code(DIRECTOR)
    assert "func stage_sky_time(seconds: float) -> void:" in director and "_sky_time = seconds" in director


def test_the_perf_harness_splits_the_schools_by_part():
    perf = _code(PERF)
    assert '"shoal":' in perf and "return [FishSchool.SHOAL_MATERIAL, prop, true]" in perf


# ── The sun's light ────────────────────────────────────────────────────────────

def test_the_sun_light_keeps_its_luminance_and_encodes_its_tint():
    # Light3D colours are sRGB-encoded: the transmittance's linear tint is
    # encoded before it meets the mood's (sRGB) tint, and the light's energy
    # is divided by its colour's luminance so the light carries the
    # director's energy (a 3-degree sun kept about a quarter of it).
    director = _code(DIRECTOR)
    apply_sun = _function_gd(director, "func _apply_sun(")
    assert "Color(balanced.x / peak, balanced.y / peak, balanced.z / peak).linear_to_srgb()" in apply_sun
    assert "sun_light.light_energy = energy / sun_color_luminance(sun_light.light_color)" in apply_sun
    assert apply_sun.index("linear_to_srgb()") < apply_sun.index("_mood_sun_tint()")
    lum = _function_gd(director, "static func sun_color_luminance(")
    assert "var lin := srgb.srgb_to_linear()" in lum and "SUN_COLOR_MIN_LUMINANCE" in lum
    assert 'c["sun_energy"] = energy' in apply_sun, "the composed energy stays the luminance"
