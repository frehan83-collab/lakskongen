"""G2-03 guards: water v2 (planar mirror, foam, wakes, spray).

The calm fjord mirrors the mountains, the boats, the sky and the aurora
through a planar reflection (PlanarReflection.gd): the scene rendered from
the camera mirrored in the water plane, at a share of the window's
resolution, blended in by roughness and fresnel. These checks pin the
pieces that make it right and affordable: what the mirror camera must not
see (the water, everything under it, labels, the scene's shadowed lights),
the terrain's hook that keeps the seabed out of the mirror, the tiers (Low
falls back to the sky's radiance and the probe; High skips FARM distances
and updates at 30 Hz at COAST), the mirror's own quarter-resolution sky, the
night fix (no specular occlusion on the water), the wakes' shader constants
mirrored in GDScript, the shore foam fringe and the storm spray. Pure static
analysis: needs no engine (the UI flow test drives the same pieces live).
"""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
WATER = GODOT / "shaders/water_shader.gdshader"
MIRROR_INC = GODOT / "shaders/water/water_mirror.gdshaderinc"
MIRROR_PASS = GODOT / "shaders/include/mirror_pass.gdshaderinc"
TERRAIN_SHADER = GODOT / "shaders/terrain_shader.gdshader"
SKY = GODOT / "shaders/sky_shader.gdshader"
WAKE_SHADER = GODOT / "shaders/water/wake_shader.gdshader"
PLANAR = GODOT / "scripts/environment/PlanarReflection.gd"
WAKES = GODOT / "scripts/environment/VesselWakes.gd"
SPRAY = GODOT / "scripts/environment/PenSpray.gd"
DIRECTOR = GODOT / "scripts/environment/EnvironmentDirector.gd"
QUALITY = GODOT / "scripts/environment/RenderQuality.gd"
GRID = GODOT / "scripts/environment/WaterGrid.gd"
SCENE = GODOT / "scenes/environment/FjordEnvironment.tscn"
PEN = GODOT / "scripts/environment/PenCage.gd"
PEN_MANAGER = GODOT / "scripts/environment/FishPenManager.gd"
MOORING = GODOT / "scripts/environment/MooringGrid.gd"


def _code(path: Path) -> str:
    """Source without // (shaders) or # (GDScript) comments; a res:// path is not a comment."""
    lines = path.read_text(encoding="utf-8").splitlines()
    if path.suffix == ".gd":
        return "\n".join(line.split("#", 1)[0] for line in lines)
    return "\n".join(re.sub(r"(?<!:)//.*$", "", line) for line in lines)


def _gd_int_const(text: str, name: str) -> int:
    match = re.search(rf"const {name} := 1 << (\d+)", text)
    assert match, f"{name} must be a render layer bit (1 << n)"
    return 1 << int(match.group(1))


def _node(scene: str, name: str) -> str:
    block = scene[scene.index(f'[node name="{name}"'):]
    return block[: block.index("\n\n")] if "\n\n" in block else block


def _presets() -> dict:
    text = QUALITY.read_text(encoding="utf-8")
    tiers = {}
    for tier in ("low", "medium", "high", "ultra"):
        block = text[text.index(f'\t"{tier}": {{'):]
        block = block[: block.index("\t},")]
        tiers[tier] = dict(re.findall(r'"(\w+)": ([^,\n]+)', block))
    return tiers


def test_the_mirror_camera_leaves_out_the_water_and_what_lies_under_it():
    planar = _code(PLANAR)
    no_mirror = _gd_int_const(planar, "NO_MIRROR_LAYER")
    mirror_only = _gd_int_const(planar, "MIRROR_ONLY_LAYER")
    water = _gd_int_const(_code(GRID), "RENDER_LAYER")
    assert len({no_mirror, mirror_only, water}) == 3
    assert "0xFFFFF & ~(WaterGrid.RENDER_LAYER | NO_MIRROR_LAYER)" in planar
    # The mirror renders at a share of the window, into linear HDR, with its own camera.
    assert "use_hdr_2d = true" in planar and "SubViewport.new()" in planar and "Camera3D.new()" in planar
    # The mirror's lights cast no shadows and only the mirror sees them.
    assert planar.count("light.shadow_enabled = false") == 1 and "light.layers = MIRROR_ONLY_LAYER" in planar


def test_what_hangs_under_the_water_and_world_labels_stay_out_of_the_mirror():
    pen = _code(PEN)
    parts = re.search(r"const NO_MIRROR_PARTS := \[([^\]]*)\]", pen).group(1)
    for part in ("Net", "SinkerTube", "LiceSkirt"):
        assert f'"{part}"' in parts, f"{part} hangs under the water: it must stay out of the mirror"
    assert "mesh.layers = PlanarReflection.NO_MIRROR_LAYER" in pen
    assert "label.layers = PlanarReflection.NO_MIRROR_LAYER" in pen, "pen labels are UI, not world: no reflection"
    assert "school.layers = PlanarReflection.NO_MIRROR_LAYER" in _code(PEN_MANAGER)
    assert "mmi.layers = PlanarReflection.NO_MIRROR_LAYER" in _code(MOORING)
    assert "PlanarReflection.NO_MIRROR_LAYER" in _code(WAKES) and "PlanarReflection.NO_MIRROR_LAYER" in _code(SPRAY)


def test_the_scene_lights_cameras_and_weather_respect_the_mirror_layers():
    scene = SCENE.read_text(encoding="utf-8")
    no_mirror = 1 << 18
    mirror_only = 1 << 17
    for light in ("Sun", "Moon"):
        assert f"layers = {no_mirror}" in _node(scene, light), f"{light}: the mirror has its own light without shadows"
    for viewer in ("FjordCamera", "ReflectionProbe"):
        mask = int(re.search(r"cull_mask = (\d+)", _node(scene, viewer)).group(1))
        assert mask & mirror_only == 0, f"{viewer} must not see the mirror's lights (the world would be lit twice)"
        assert mask & no_mirror, f"{viewer} must see the scene's Sun and Moon"
    for particles in ("RainParticles", "SnowParticles"):
        assert f"layers = {no_mirror}" in _node(scene, particles), f"{particles} fall round the camera: not in the mirror"
    for node in ("PlanarReflection", "VesselWakes", "PenSpray"):
        assert f'[node name="{node}"' in scene
    assert 'planar_reflection = NodePath("../PlanarReflection")' in scene


def test_the_terrain_keeps_its_seabed_out_of_the_mirror():
    terrain = _code(TERRAIN_SHADER)
    assert '#include "res://shaders/include/mirror_pass.gdshaderinc"' in terrain
    vertex = terrain[terrain.index("void vertex()"):]
    vertex = vertex[: vertex.index("\n}")]
    assert "lk_mirror_vertex(VERTEX, MODEL_MATRIX, CAMERA_VISIBLE_LAYERS, CAMERA_POSITION_WORLD)" in vertex
    inc = _code(MIRROR_PASS)
    # Recognised per camera (one global value would hold for every viewport in a frame).
    assert "LK_NO_MIRROR_LAYER = 262144u" in inc and "camera_world.y < LK_WATER_LEVEL" in inc
    assert "global uniform" not in inc


def test_the_water_blends_the_mirror_by_roughness_and_fresnel_and_lifts_the_night():
    water = _code(WATER)
    assert '#include "res://shaders/water/water_mirror.gdshaderinc"' in water
    render_mode = re.search(r"render_mode\s+([^;]+);", water).group(1)
    # The engine's specular occlusion darkens reflections by the ambient light:
    # at night it turned the water into a black void.
    assert "specular_occlusion_disabled" in render_mode
    assert "EMISSION = mirror.rgb * fresnel * mirror.a;" in water
    assert re.search(r"SPECULAR = mix\(WATER_SPECULAR, 0\.1, foam\) \* sqrt\(1\.0 - mirror\.a\);", water)
    assert re.search(r"const float WATER_F0 = 0\.02;", water)
    inc = _code(MIRROR_INC)
    rough_from = float(re.search(r"PLANAR_ROUGH_FROM = ([\d.]+)", inc).group(1))
    rough_to = float(re.search(r"PLANAR_ROUGH_TO = ([\d.]+)", inc).group(1))
    assert 0.0 < rough_from < rough_to <= 0.3, "a calm sea mirrors, a rough one breaks the mirror up"
    assert "planar_view_proj * vec4(p, 1.0)" in inc, "the lookup uses the very matrix that rendered the texture"


def test_the_tiers_place_the_mirror():
    tiers = _presets()
    assert tiers["low"]["mirror"] == "false", "Low falls back to the sky's radiance and the probe"
    for tier in ("medium", "high", "ultra"):
        assert tiers[tier]["mirror"] == "true"
    scales = [float(tiers[t]["mirror_scale"]) for t in ("medium", "high", "ultra")]
    assert scales == sorted(scales) and scales[-1] <= 0.5, "half resolution at most"
    assert float(tiers["high"]["mirror_far_hz"]) == 30.0, "the roadmap's 30 Hz at COAST"
    assert float(tiers["ultra"]["mirror_from_m"]) == 0.0, "Ultra mirrors at every zoom"
    assert 0.0 < float(tiers["high"]["mirror_from_m"]) < 400.0, "High skips FARM distances only (FARM band ends at 400 m)"
    assert "RenderQuality.apply_mirror_viewport(_viewport)" in _code(PLANAR)


def test_the_mirror_has_its_own_quarter_resolution_sky_and_a_share_of_the_fog():
    director = _code(DIRECTOR)
    assert "const MIRROR_SKY_PASS := 2" in director
    assert re.search(r"const MIRROR_FOG_SHARE := 0\.5", director)
    assert "m.tonemap_mode = Environment.TONE_MAPPER_LINEAR" in director and "m.tonemap_exposure = 1.0" in director
    assert "m.sky = _ensure_mirror_sky(env)" in director
    sky = _code(SKY)
    assert "uniform int sky_pass" in sky and "uniform bool sky_pass_aurora" in sky
    assert "sky_pass == 1 ? HALF_RES_COLOR : QUARTER_RES_COLOR" in sky


def test_the_wake_shader_and_its_script_agree():
    shader = _code(WAKE_SHADER)
    script = _code(WAKES)
    for name in ("KELVIN_TAN", "FOAM_LIFE_S", "FOAM_SPREAD", "KELVIN_FADE_M", "KELVIN_ALPHA"):
        a = float(re.search(rf"const float {name} = ([\d.]+);", shader).group(1))
        b = float(re.search(rf"const {name} := ([\d.]+)", script).group(1))
        assert a == b, f"{name}: shader {a} != script {b}"
    # The Kelvin wedge: 19.47 degrees either side of the track.
    import math
    assert abs(float(re.search(r"const KELVIN_TAN := ([\d.]+)", script).group(1)) - math.tan(math.radians(19.47))) < 1e-4
    render_mode = re.search(r"render_mode\s+([^;]+);", shader).group(1)
    assert "depth_draw_never" in render_mode and "blend_mix" in render_mode


def test_shore_foam_reaches_five_metres_and_the_storm_sprays():
    water = _code(WATER)
    fringe = float(re.search(r"const float SHORE_FRINGE_M = ([\d.]+);", water).group(1))
    assert 4.0 <= fringe < 5.0, "foam along the shore wherever the shore distance is under 5 m, in any weather"
    assert "float surf_width = SHORE_FRINGE_M +" in water
    spray = _code(SPRAY)
    assert float(re.search(r"const WIND_FROM_MS := ([\d.]+)", spray).group(1)) >= 8.0, "no spray in a breeze"
    assert "EMISSION_SHAPE_RING" in spray and "amount_ratio" in spray


def test_whitecaps_break_on_the_peak_band_and_the_mapping_is_recalibrated():
    water = _code(WATER)
    director = _code(DIRECTOR)
    # The crests that break are the spectral peak's: per-component weights
    # round the basin's and the coast's peak (computed once per sea, not per
    # pixel: log2 and exp per component cost 0.45 ms at 1440p FJORD).
    assert "uniform vec2 wave_peak[WAVES];" in water and "float peak_a = a * mix(wp.x, wp.y, exposure);" in water
    assert 'mat.set_shader_parameter("wave_peak", sea_state["wave_peak"])' in director
    assert "static func peak_weight(wavelength: float, peak_wavelength: float) -> float:" in director
    assert "GROUP_SPREAD * (group - 0.5)" in water, "caps gather in wave groups and gusts"
    scale = float(re.search(r"const WHITECAP_Z_SCALE := ([\d.]+)", director).group(1))
    offset = float(re.search(r"const WHITECAP_Z_OFFSET := ([\d.]+)", director).group(1))
    # The calibration points (top-down cover = Monahan's at 9 / 14 / 20 m/s):
    # z(1 - W) = 2.46, 1.866, 1.253 -> thresholds 3.46, 2.72, 1.96.
    for z, th in ((2.46, 3.46), (1.866, 2.72), (1.253, 1.96)):
        assert abs(scale * z + offset - th) < 0.03
