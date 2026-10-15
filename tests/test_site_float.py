"""Multi-pen sites and their floating gear (roadmap G2-04): static checks.

The hoses, buoys, lights and mooring lines ride the drawn water in their
vertex shaders (godot/shaders/site_float.gdshaderinc). These tests pin that
include to the water shader it mirrors and to WaveField.gd (the CPU mirror
the pens and boats ride), so a change to the water's waves cannot leave the
site gear floating on a different sea; and they hold the site kit to its
contracts: every floating material is synced, every pixel-size formula
survives the renderer's y-flipped projection, rival farms are the player's
assets with no one-off materials, the pen's hose port is where the site's
hoses start. The UI-flow tests (godot/tests/SiteFlowChecks.gd) check the
same things numerically in the running game.
"""

import json
import re
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
SHADERS = GODOT / "shaders"
INCLUDE = (SHADERS / "site_float.gdshaderinc").read_text()
WATER = (SHADERS / "water_shader.gdshader").read_text()
WAVEFIELD = (GODOT / "scripts" / "environment" / "WaveField.gd").read_text()
SITEFLOAT = (GODOT / "scripts" / "environment" / "SiteFloat.gd").read_text()


def _function(source: str, name: str) -> str:
    """The body of a GLSL function, whitespace collapsed."""
    m = re.search(r"\b%s\s*\([^)]*\)\s*\{" % re.escape(name), source)
    assert m, f"{name} not found"
    depth, i = 1, m.end()
    while depth:
        depth += {"{": 1, "}": -1}.get(source[i], 0)
        i += 1
    return " ".join(source[m.end():i - 1].split())


def _gd_const(name: str) -> float:
    m = re.search(r"const %s := ([0-9.e-]+)" % name, WAVEFIELD)
    assert m, name
    return float(m.group(1))


def test_component_amplitude_is_the_water_shaders():
    assert _function(INCLUDE, "float_component_amplitude") == _function(WATER, "component_amplitude")


def test_bake_lookup_is_the_water_shaders():
    water = _function(WATER, "bake_at")
    mine = _function(INCLUDE, "float_bake_at")
    for a, b in (("float_bake_origin", "bake_origin"), ("float_bake_spacing", "bake_spacing"),
                 ("float_bake_texels", "bake_texels"), ("float_has_bake", "has_bake"),
                 ("float_bake_tex", "water_bake")):
        mine = mine.replace(a, b)
    # The water's comment line is the only other difference.
    water = water.replace("// Beyond the bake: the open ocean. ", "")
    assert mine == water


def test_displacement_loop_is_the_water_shaders():
    water = _function(WATER, "displacement")
    mine = _function(INCLUDE, "float_displacement")
    # The component's drawn amplitude: band-limited at 3 and 6 grid spacings.
    for text in (water, mine):
        assert "smoothstep(3.0 * " in text and "6.0 * " in text
        assert "if (a < 1e-4)" in text
        assert "d.y += a * sin(ph);" in text
        assert "d.xz += wk.w * a * (wk.xy / wk.z) * cos(ph);" in text
    assert "fract((dot(wk.xy, p) - wa.z) * INV_TAU) * TAU_" in _function(WATER, "wave_phase")
    assert "fract((dot(wk.xy, p) - wa.z) * FLOAT_INV_TAU) * FLOAT_TAU" in mine


def test_grid_reach_and_far_fade_match_the_water_and_wavefield():
    vertex = _function(WATER, "vertex")
    assert "grid_base_spacing * max(1.0, 2.0 * d_grid / (grid_half_units * grid_base_spacing))" in vertex
    assert "1.0 - smoothstep(0.75 * displaced_extent, displaced_extent, d_grid)" in vertex
    reach = _function(INCLUDE, "float_grid_reach")
    assert "float_grid_base_spacing * max(1.0, 2.0 * d / (float_grid_half_units * float_grid_base_spacing))" in reach
    assert "1.0 - smoothstep(0.75 * float_displaced_extent, float_displaced_extent, d)" in reach
    assert _gd_const("WAVE_MIN_SPACINGS") == 3.0 and _gd_const("WAVE_FULL_SPACINGS") == 6.0
    assert _gd_const("FAR_FADE_FROM") == 0.75 and _gd_const("BAKE_EDGE_UV") == 0.015
    assert _gd_const("SHALLOW_DEPTH_M") == 40.0 and _gd_const("SHALLOW_PER_WAVELENGTH") == 0.08
    assert _gd_const("SHALLOW_OFFSET_M") == 0.5


def test_the_cpu_twin_runs_the_shaders_inversion():
    # Same array size, same fixed-point steps.
    assert re.search(r"#define FLOAT_WAVES 16", INCLUDE)
    assert re.search(r"const WAVES := 16", SITEFLOAT)
    assert re.search(r"const INVERT_STEPS := (\d+)", SITEFLOAT).group(1) == "4"
    assert 'set_shader_parameter("float_invert_steps", INVERT_STEPS)' in SITEFLOAT
    surface = _function(INCLUDE, "float_surface")
    assert "for (int s = 0; s < float_invert_steps; s++)" in surface
    assert "p = xz - float_displacement(p).xz;" in surface
    # Uniform loop bounds: the compiler must not unroll the loops into every
    # call site (unrolled, a site's gear took minutes to compile on Metal).
    assert "i < float_wave_count" in _function(INCLUDE, "float_displacement")


def _shader_of(tres: Path) -> str | None:
    m = re.search(r'path="res://(shaders/[^"]+)"', tres.read_text())
    return m.group(1) if m else None


def test_every_floating_material_is_synced_and_every_synced_one_floats():
    listed = set(re.findall(r'preload\("res://(assets/models/site_kit/[^"]+\.tres)"\)', SITEFLOAT))
    floating = set()
    for tres in (GODOT / "assets" / "models").rglob("*.tres"):
        shader = _shader_of(tres)
        if shader and "site_float.gdshaderinc" in (GODOT / shader).read_text():
            floating.add(str(tres.relative_to(GODOT)))
    assert floating and listed == floating


def test_pixel_sizes_survive_the_flipped_projection():
    # The renderer's projection flips y: PROJECTION_MATRIX[1][1] < 0. A
    # minimum on-screen size computed from it without abs() silently does
    # nothing (it did, for the corner-light halos and the health rings).
    for shader in list(SHADERS.glob("*.gdshader")) + list(SHADERS.glob("*.gdshaderinc")):
        text = shader.read_text()
        for m in re.finditer(r"PROJECTION_MATRIX\[1\]\[1\]", text):
            before = text[max(0, m.start() - 90):m.start()]
            assert before.endswith("abs(") or "float_m_per_px(" in before, f"{shader.name}: {before!r}"
    assert "abs(proj11)" in _function(INCLUDE, "float_m_per_px")


def test_rival_farms_are_the_players_site_with_no_one_off_materials():
    rivals = (GODOT / "scripts" / "visual" / "CompetitorFarmVisualManager.gd").read_text()
    assert "material_override" not in rivals
    assert "FarmSite.new()" in rivals and '"band_color": band' in rivals
    farm = (GODOT / "scripts" / "environment" / "FarmSite.gd").read_text()
    # The only override a site sets is its shared ghost material.
    overrides = re.findall(r"material_override = ([A-Za-z_]+)", farm)
    assert set(overrides) <= {"mat"} and "_get_ghost_mat()" in farm


def test_the_pens_hose_port_is_where_the_site_hoses_start():
    layout = (GODOT / "scripts" / "environment" / "SiteLayout.gd").read_text()
    build = (REPO / "blender" / "build_pen.py").read_text()
    port = float(re.search(r"const PORT_R := ([0-9.]+)", layout).group(1))
    assert port == float(re.search(r"^PORT_R = ([0-9.]+)", build, re.M).group(1))
    side = json.loads((GODOT / "assets" / "models" / "pen_cage" / "pen_cage_LOD0.json").read_text())
    assert side["dimensions"]["hose_port_m"] == port
    # The port lies outside the collar (outer pipe edge 26.8 m) and inside
    # the pen's mooring cell.
    cell = float(re.search(r"const CELL_M := ([0-9.]+)", (GODOT / "scripts" / "environment" / "MooringGrid.gd").read_text()).group(1))
    assert 26.8 < port < cell / 2.0
    assert 70.0 <= cell <= 100.0


@pytest.mark.parametrize("lod", ["LOD0", "LOD1", "LOD2"])
def test_every_pen_lod_carries_both_bird_nets_and_the_spreader(lod):
    side = json.loads((GODOT / "assets" / "models" / "pen_cage" / f"pen_cage_{lod}.json").read_text())
    for part in ("BirdNet", "BirdNetPoles", "BirdNetRim", "BirdNetRimPoles", "FeedPipe"):
        assert side["parts"].get(part, 0) > 0, (lod, part)
    for mat in ("LK_BirdNetGreen", "LK_PoleBlue", "LK_Spreader", "LK_Hose"):
        assert mat in side["materials"], (lod, mat)


def test_reference_colours_reach_the_pen_materials():
    def colour(name: str) -> tuple:
        text = (GODOT / "assets" / "models" / "pen_cage" / name).read_text()
        m = re.search(r"albedo_color = Color\(([^)]*)\)", text)
        return tuple(round(float(v), 3) for v in m.group(1).split(",")[:3])
    # Owner palette: pole blue #2F6DB5, hose white #EDEFEF.
    assert colour("pen_pole_blue_material.tres") == (0.184, 0.427, 0.71)
    assert colour("pen_hose_material.tres") == (0.929, 0.937, 0.937)
    green = colour("pen_bird_net_green_material.tres")
    assert green[1] > green[0] and green[1] > green[2]
    hose = (GODOT / "assets" / "models" / "site_kit" / "feed_hose_material.tres").read_text()
    assert "hose_color = Color(0.929412, 0.937255, 0.937255" in hose


def test_hose_sway_is_held_at_the_barge_and_the_pen():
    # The floating hoses sway slowly across their line, but never where they
    # are fixed: the sway fades to zero within `sway_hold_m` of the barge
    # outlet (its riser to the deck included) and of the pen's hose port.
    shader = (SHADERS / "feed_hose.gdshader").read_text()
    hoses = (GODOT / "scripts" / "environment" / "FeedHoses.gd").read_text()

    def uniform(name: str) -> float:
        m = re.search(r"uniform float %s = ([0-9.]+);" % name, shader)
        assert m, name
        return float(m.group(1))

    vertex = _function(shader, "vertex")
    hold = "smoothstep(0.0, sway_hold_m, UV.y) * smoothstep(0.0, sway_hold_m, UV2.y - UV.y)"
    assert hold in vertex
    assert "float sway = sway_m * hold *" in vertex
    # The sway moves the centre point before it samples the water.
    assert vertex.index("c.xz += vec2(-t.z, t.x) * sway;") < vertex.index("float_height(c.xz)")
    # UV2.y is each hose's own length, so the far end is held too.
    assert "uv2s.append(Vector2(lift, total))" in hoses
    assert "total += line[i].distance_to(line[i - 1])" in hoses
    riser = float(re.search(r"const RISER_RUN_M := ([0-9.]+)", hoses).group(1))
    assert uniform("sway_hold_m") >= riser
    # A gentle slack: well under a metre, slower than the swell.
    assert 0.0 < uniform("sway_m") < 1.0 and uniform("sway_period_s") >= 20.0
