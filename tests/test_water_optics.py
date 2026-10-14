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
