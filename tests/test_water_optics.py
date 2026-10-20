"""G1-05 / G1-06 guards: opaque fjord water and an anti-aliased pen net.

The old water wrote ALPHA (0.85) and declared the screen and depth textures,
so it sat in the transparent pass, showed the terrain through deep water as
tinted glass, and missed SSR, depth of field and correct sorting with nets
and boats. The roadmap's ruling: opaque water, colour from Beer-Lambert over a
baked bathymetry. These checks keep it that way, keep the wave bands the C#
sea-state model hands over in step with the shader, and pin the net's
alpha-to-coverage fade. Pure static analysis: needs no engine.
"""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
WATER = GODOT / "shaders/water_shader.gdshader"
NET = GODOT / "shaders/pen_net_shader.gdshader"
SCENE = GODOT / "scenes/environment/FjordEnvironment.tscn"
DIRECTOR = GODOT / "scripts/environment/EnvironmentDirector.gd"
GRID = GODOT / "scripts/environment/WaterGrid.gd"
SEA_MODEL = REPO / "src/LakskongenSim/Environment/SeaStateModel.cs"
WAVE_FIELD = GODOT / "scripts/environment/WaveField.gd"
FLOATERS = (GODOT / "scripts/environment/PenCage.gd", GODOT / "scripts/visual/VesselModel.gd")


def _code(path: Path) -> str:
    """Shader source without // comments."""
    return "\n".join(line.split("//", 1)[0] for line in path.read_text(encoding="utf-8").splitlines())


def test_water_writes_no_alpha_and_reads_no_screen_or_depth_texture():
    code = _code(WATER)
    assert not re.search(r"\bALPHA\w*\s*=", code), "the water must stay opaque (no ALPHA writes)"
    for banned in ("hint_screen_texture", "hint_depth_texture", "SCREEN_TEXTURE", "DEPTH_TEXTURE", "screen_texture"):
        assert banned not in code, f"the water must not read {banned} (it would force the transparent pass)"
    render_mode = re.search(r"render_mode\s+([^;]+);", code).group(1)
    for mode in ("blend_add", "blend_sub", "blend_mul", "depth_draw_never", "unshaded"):
        assert mode not in render_mode, f"render_mode {mode} breaks the opaque water"


def test_water_colour_comes_from_the_baked_bathymetry():
    code = _code(WATER)
    assert "uniform sampler2D water_bake" in code
    # Beer-Lambert: exp(-sigma * path) over the baked depth.
    assert re.search(r"exp\(\s*-\s*sigma\s*\*\s*water_depth", code), "no Beer-Lambert transmittance over the bake"
    # The seabed shows only through shallow water: no terrain through deep water.
    assert "seabed * transmittance" in code


def test_water_has_an_underside_and_shore_foam_and_whitecaps():
    code = _code(WATER)
    assert "cull_disabled" in code and "FRONT_FACING" in code, "the underside needs cull_disabled and a !FRONT_FACING branch"
    assert "SNELL_COS" in code
    assert "shore" in code and "surf_width" in code
    assert "whitecap_threshold" in code


def test_the_shader_draws_every_component_the_director_builds():
    shader_waves = int(re.search(r"#define\s+WAVES\s+(\d+)", _code(WATER)).group(1))
    bands = re.search(r"BandWavelengthsM\s*=\s*\{([^}]*)\}", SEA_MODEL.read_text(encoding="utf-8")).group(1)
    band_count = len([b for b in bands.split(",") if b.strip()])
    per_band = int(re.search(r"const COMPONENTS_PER_BAND := (\d+)", DIRECTOR.read_text(encoding="utf-8")).group(1))
    assert shader_waves == band_count * per_band, (
        f"shader WAVES {shader_waves} != {band_count} bands x {per_band} components"
    )
    # The roadmap's 6-8 bands.
    assert 6 <= band_count <= 8


def test_wind_no_longer_multiplies_a_fixed_wave():
    water = _code(WATER)
    director = DIRECTOR.read_text(encoding="utf-8")
    assert "wind_strength" not in water
    assert "wave_amplitude" not in water
    assert "WAVE_AMPLITUDE_PER_HS" not in director


def test_the_water_material_is_not_transparent_in_the_scene():
    scene = SCENE.read_text(encoding="utf-8")
    mat = scene[scene.index('id="water_mat"'):]
    mat = mat[: mat.index("\n\n")]
    assert "transparency" not in mat
    assert "screen_texture" not in mat


def test_the_reflection_probe_leaves_the_water_layer_out():
    scene = SCENE.read_text(encoding="utf-8")
    probe = scene[scene.index('[node name="ReflectionProbe"'):]
    probe = probe[: probe.index("\n\n")]
    mask = int(re.search(r"reflection_mask = (\d+)", probe).group(1))
    layer = re.search(r"const RENDER_LAYER := 1 << (\d+)", GRID.read_text(encoding="utf-8"))
    assert layer, "WaterGrid.gd must put the water on its own render layer"
    assert mask & (1 << int(layer.group(1))) == 0, "the probe must not mirror the water (it captures from one point)"


def test_the_grid_is_two_metres_out_to_256_m():
    grid = GRID.read_text(encoding="utf-8")
    spacing = float(re.search(r"@export var base_spacing: float = ([\d.]+)", grid).group(1))
    quads = int(re.search(r"@export var grid_quads: int = (\d+)", grid).group(1))
    assert spacing <= 2.0
    assert 0.5 * quads * spacing >= 256.0


def test_the_net_uses_alpha_to_coverage_and_fades_to_its_mean():
    code = _code(NET)
    render_mode = re.search(r"render_mode\s+([^;]+);", code).group(1)
    assert "alpha_to_coverage" in render_mode
    assert re.search(r"FADE_FWIDTH\s*=\s*0\.35", code), "the net fades to its mean coverage past fwidth 0.35"
    assert "mean_coverage" in code
    # The hard 0.5 cutout that aliased into moire is gone.
    assert "discard" not in code


def test_water_reflects_with_f0_two_percent():
    # Godot's F0 is 0.16 * SPECULAR^2, and it treats F0 below 0.02 as specular
    # occlusion (the grazing reflection scales by 50 * F0): SPECULAR 0.25 gave
    # F0 0.01 and halved the water's horizon reflection.
    code = _code(WATER)
    spec = float(re.search(r"const float WATER_SPECULAR = ([\d.]+);", code).group(1))
    assert 0.0195 <= 0.16 * spec * spec <= 0.022
    assert re.search(r"SPECULAR = mix\(WATER_SPECULAR,", code)


def _gd_const(text: str, name: str) -> float:
    return float(re.search(rf"const {name} := ([-\d.e]+)", text).group(1))


def test_the_cpu_wave_mirrors_the_shader_displacement():
    """G2-03: pens and boats ride WaveField, the CPU copy of the water's
    vertex displacement. Its band limit, shallow-water fade, open-ocean bake,
    far fade and amplitude floor are the shader's own numbers, so a change
    to the shader that the CPU copy misses fails here (the UI flow test
    compares the two numerically)."""
    shader = _code(WATER)
    field = WAVE_FIELD.read_text(encoding="utf-8")
    assert float(re.search(r"if \(wa\.w < ([\d.]+) \* grid_spacing\)", shader).group(1)) == _gd_const(field, "WAVE_MIN_SPACINGS")
    band = re.search(r"smoothstep\(([\d.]+) \* grid_spacing, ([\d.]+) \* grid_spacing, wa\.w\)", shader)
    assert (float(band.group(1)), float(band.group(2))) == (
        _gd_const(field, "WAVE_MIN_SPACINGS"), _gd_const(field, "WAVE_FULL_SPACINGS"))
    assert float(re.search(r"if \(depth < ([\d.]+)\)", shader).group(1)) == _gd_const(field, "SHALLOW_DEPTH_M")
    shallow = re.search(r"a \*= d / \(d \+ ([\d.]+) \* wa\.w \+ ([\d.]+)\);", shader)
    assert (float(shallow.group(1)), float(shallow.group(2))) == (
        _gd_const(field, "SHALLOW_PER_WAVELENGTH"), _gd_const(field, "SHALLOW_OFFSET_M"))
    assert float(re.search(r"if \(a < ([\d.e-]+)\)", shader).group(1)) == _gd_const(field, "MIN_AMPLITUDE_M")
    ocean = re.search(r"mix\(vec4\(([\d.]+), ([\d.]+), ([\d.]+), ([\d.]+)\), b, inside\)", shader).groups()
    mirrored = re.search(r"const OPEN_OCEAN := Color\(([\d.]+), ([\d.]+), ([\d.]+), ([\d.]+)\)", field).groups()
    assert [float(v) for v in ocean] == [float(v) for v in mirrored]
    assert float(re.search(r"smoothstep\(0\.0, ([\d.]+), min\(edge\.x, edge\.y\)\)", shader).group(1)) == _gd_const(field, "BAKE_EDGE_UV")
    assert float(re.search(r"smoothstep\(([\d.]+) \* displaced_extent, displaced_extent, d_grid\)", shader).group(1)) == _gd_const(field, "FAR_FADE_FROM")
    # The grid's spacing law and the Gerstner terms, as the shader writes them.
    assert "grid_base_spacing * max(1.0, 2.0 * d_grid / (grid_half_units * grid_base_spacing))" in shader
    assert "grid_base_spacing * maxf(1.0, 2.0 * d / (grid_half_units * grid_base_spacing))" in field
    assert "d.xz += wk.w * a * (wk.xy / wk.z) * cos(ph);" in shader


def test_the_director_hands_the_cpu_wave_the_shaders_arrays_and_bodies_ride_it():
    director = DIRECTOR.read_text(encoding="utf-8")
    assert re.search(r'set_shader_parameter\("wave_amp", amp\)\n\s+wave_field\.set_waves\(sea_state\["wave_k"\], amp\)', director), (
        "the CPU wave must get the very arrays the shader's wave uniforms get, in the same frame"
    )
    assert "wave_field.set_bake(" in director and "WaveField.active = wave_field" in director
    for path in FLOATERS:
        code = path.read_text(encoding="utf-8")
        assert "func ride_waves()" in code and "WaveField.active" in code, f"{path.name} must ride the drawn water"
