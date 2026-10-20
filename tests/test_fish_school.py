"""Salmon schools on the GPU (roadmap G3-01) and the farm's lights after
dark: static checks.

The school's per-fish data is written into the MultiMesh custom data once
and the swim runs in the vertex shaders (godot/shaders/fish_school
.gdshaderinc); the school seen through the opaque water is a decal drawn
after it (fish_shoal.gdshader) that uses the water shader's own optics and
never reads the screen or the depth buffer. The night lights (SiteLights,
VesselLights) keep their screen brightness at the night exposure. The UI
flow tests (godot/tests/FishFlowChecks.gd) check the same contracts in the
running game; the screenshot harness measures the fish's share of 08b.
"""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
SHADERS = GODOT / "shaders"
SCRIPTS = GODOT / "scripts"
SCHOOL = (SCRIPTS / "environment" / "FishSchool.gd").read_text()
MANAGER = (SCRIPTS / "environment" / "FishPenManager.gd").read_text()
SITE_LIGHTS = (SCRIPTS / "environment" / "SiteLights.gd").read_text()
VESSEL_LIGHTS = (SCRIPTS / "visual" / "VesselLights.gd").read_text()
INCLUDE = (SHADERS / "fish_school.gdshaderinc").read_text()
SOLID = (SHADERS / "salmon_school.gdshader").read_text()
SHOAL = (SHADERS / "fish_shoal.gdshader").read_text()
WATER = (SHADERS / "water_shader.gdshader").read_text()


def _code(text: str) -> str:
    """GDScript or shader source without comments."""
    text = re.sub(r"//[^\n]*", "", text)
    return re.sub(r"(?m)^\s*#[^\n]*|\s##[^\n]*", "", text)


def _function(source: str, name: str) -> str:
    m = re.search(r"\b%s\s*\([^)]*\)\s*(->\s*\w+\s*)?[:{]" % re.escape(name), source)
    assert m, f"{name} not found"
    if source[m.end() - 1] == "{":
        depth, i = 1, m.end()
        while depth:
            depth += {"{": 1, "}": -1}.get(source[i], 0)
            i += 1
        return source[m.end():i - 1]
    # GDScript: until the next top-level declaration.
    rest = source[m.end():]
    end = re.search(r"\n(?=\S)", rest)
    return rest[:end.start()] if end else rest


def _const(source: str, name: str) -> str:
    m = re.search(r"const %s\s*:?=\s*([^\n]+)" % re.escape(name), source)
    assert m, name
    return m.group(1).strip()


def _vec3_default(source: str, name: str) -> tuple:
    m = re.search(r"uniform vec3 %s(?:\s*:[^=]+)?\s*=\s*vec3\(([^)]*)\)" % re.escape(name), source)
    assert m, name
    return tuple(round(float(v), 6) for v in m.group(1).split(","))


def test_per_fish_data_is_written_once_and_no_transform_ever():
    code = _code(SCHOOL)
    assert "set_instance_transform" not in code
    assert "set_instance_custom_data" not in code
    # The buffer (identity transforms + custom data) is uploaded where a
    # school's MultiMesh is made, nowhere else.
    assert code.count(".buffer = ") == 1
    assert ".buffer = " in _function(code, "_multimesh")
    assert "use_custom_data = true" in _function(code, "_multimesh")
    # The per-frame path hands the shaders the pen's uniforms only.
    for name in ("animate", "_write_motion", "_ease"):
        body = _function(code, name)
        assert "buffer" not in body and "set_instance_transform" not in body, name
    assert "FishSchool.animate(" in MANAGER and "set_instance_transform" not in _function(_code(MANAGER), "_process")


def test_the_swim_runs_in_the_vertex_shaders():
    for shader in (SOLID, SHOAL):
        assert '#include "res://shaders/fish_school.gdshaderinc"' in shader
        vertex = _function(_code(shader), "vertex")
        assert "fish_pose(INSTANCE_CUSTOM, id" in vertex
        assert "fish_body_vertex(VERTEX, INSTANCE_CUSTOM.w" in vertex
    inc = _code(INCLUDE)
    for uniform in ("school_clock", "school_state", "school_body"):
        assert re.search(r"instance uniform vec4 %s\b" % uniform, inc), uniform
        assert '"%s"' % uniform in SCHOOL, uniform
    # The tail beat bends the body; the whole school turns by the pen's angle.
    assert "sin(w)" in _function(inc, "fish_body_vertex")
    assert "school_clock.x * pace" in _function(inc, "fish_pose")
    # Feeding (state.w) pulls the ring in; the depth comes from the state.
    assert "fish_ring_radius(custom.y, feed, outer)" in _function(inc, "fish_pose")
    assert "school_state.x + school_state.y" in _function(inc, "fish_pose")


def test_fish_counts_and_range_are_the_roadmaps():
    assert _const(SCHOOL, "MIN_FISH") == "1500"
    assert _const(SCHOOL, "MAX_FISH") == "4000"
    assert float(_const(SCHOOL, "FISH_RANGE_M")) == 400.0
    jumpers = int(_const(SCHOOL, "JUMPERS"))
    assert re.search(r"uniform int jumper_count = %d;" % jumpers, INCLUDE)
    # Feeding lifts the school: the fed band lies above the calm one.
    calm = float(_const(SCHOOL, "DEPTH_CALM_M"))
    fed = float(_const(SCHOOL, "DEPTH_FED_M"))
    assert fed + float(_const(SCHOOL, "SPREAD_FED_M")) < calm + float(_const(SCHOOL, "SPREAD_CALM_M"))
    assert fed < calm - 2.0
    assert 0.0 < float(_const(SCHOOL, "LOW_DO_SPEED")) < 0.5


def test_the_shoal_sees_the_waters_own_optics():
    # The shader defaults are the water's, and FishSchool.water_optics
    # composes them as water_shader.gdshader does.
    for name in ("absorption", "deep_scatter"):
        assert _vec3_default(SHOAL, name) == _vec3_default(WATER, name), name
    optics = _function(_code(SCHOOL), "water_optics")
    water = _function(_code(WATER), "fragment")
    for triple in ("0.9, 1.15, 0.85", "0.25, 0.04, 0.45", "0.15, 0.2, 0.3", "0.006, 0.024, 0.010",
                   "0.016, 0.017, 0.011"):
        assert triple in water.replace("vec3(", "("), triple
        assert triple in optics, triple
    for factor in ("temperature_factor * 0.5", "algae_density * 0.85", "turbidity * 0.7"):
        assert factor in water, factor
    assert "warm * 0.5" in optics and "algae * 0.85" in optics and "turbidity * 0.7" in optics


def test_the_shoal_never_makes_the_water_see_through():
    shoal = _code(SHOAL)
    assert "hint_screen_texture" not in shoal and "hint_depth_texture" not in shoal
    assert "depth_test_disabled" not in shoal and "depth_draw_never" in shoal
    # A decal on the water: every vertex on the plane decal_lift over it,
    # where the camera sees it through the surface (Snell's law).
    vertex = _function(shoal, "vertex")
    assert "refraction_index" in vertex and "decal_lift" in vertex
    # The water stays opaque (G1-05).
    water = _code(WATER)
    assert "ALPHA" not in water and "hint_screen_texture" not in water
    # Fish stay in their pen.
    assert "FISH_NET_R - 1.5, FISH_NET_R - 0.3" in vertex


def test_every_night_light_is_synced_and_every_synced_one_lights():
    listed = set(re.findall(r'preload\("res://(assets/models/[^"]+\.tres)"\)', SITE_LIGHTS))
    compensated = set()
    lit = set()
    for tres in (GODOT / "assets" / "models").rglob("*.tres"):
        text = tres.read_text()
        m = re.search(r'path="res://(shaders/[^"]+\.gdshader)"', text)
        if not m:
            continue
        # The vessel paint lights only the glass (its window_light).
        if m.group(1).endswith("vessel_paint.gdshader") and "shader_parameter/window_light" not in text:
            continue
        shader = (GODOT / m.group(1)).read_text()
        if re.search(r"uniform float exposure_comp\b", shader):
            compensated.add(str(tres.relative_to(GODOT)))
            if re.search(r"uniform float night\b", shader):
                lit.add(str(tres.relative_to(GODOT)))
    assert compensated and listed == compensated
    block = SITE_LIGHTS.split("const LIGHT_MATERIALS")[1].split("const SIGNAL_MATERIALS")[0]
    lights = set(re.findall(r'preload\("res://(assets/models/[^"]+\.tres)"\)', block))
    assert lights == lit and len(lights) == 4


def test_lights_keep_their_screen_brightness_at_night():
    glow = _code((SHADERS / "mooring_light_glow.gdshader").read_text())
    vessel = _code((SHADERS / "vessel_light_glow.gdshader").read_text())
    for shader in (glow, vessel):
        frag = _function(shader, "fragment")
        assert "exposure_comp" in frag.split("ALBEDO")[1].split(";")[0]
        assert "night" in frag.split("ALPHA")[1].split(";")[0]
        assert "fog_disabled" in shader and "blend_add" in shader
    lamp = _code((SHADERS / "mooring_lamp_shader.gdshader").read_text())
    assert re.search(r"EMISSION = [^;]*\* night \* exposure_comp;", lamp)
    paint = _code((SHADERS / "vessel_paint.gdshader").read_text())
    assert re.search(r"EMISSION = window_light[^;]*night \* exposure_comp;", paint)
    ring = _code((SHADERS / "pen_signal_ring.gdshader").read_text())
    assert "ALBEDO = ring_color.rgb * exposure_comp;" in ring
    # World text is re-tinted in linear light, not in sRGB.
    assert "srgb_to_linear()" in _function(SITE_LIGHTS, "scaled_colour")
    assert "SiteLights.keep_text_colour(label)" in (SCRIPTS / "environment" / "PenCage.gd").read_text()


def test_the_corner_lights_and_the_barge_marker_are_one_ffc23a_flash():
    lamp = (GODOT / "assets/models/site_kit/mooring_lamp_material.tres").read_text()
    glow = (GODOT / "assets/models/site_kit/mooring_light_glow_material.tres").read_text()
    for tres, key in ((lamp, "lamp_color"), (glow, "glow_color")):
        m = re.search(r"shader_parameter/%s = Color\(([^)]*)\)" % key, tres)
        rgb = [round(float(v) * 255) for v in m.group(1).split(",")[:3]]
        assert rgb == [0xFF, 0xC2, 0x3A], (key, rgb)
        assert "shader_parameter/period_s = 4.0" in tres
    assert _const(VESSEL_LIGHTS, "YELLOW") == 'Color("#FFC23A")'
    assert float(_const(VESSEL_LIGHTS, "MARKER_PERIOD_S")) == 4.0
    # Every flashing light reads the one global clock.
    for name in ("mooring_lamp_shader", "mooring_light_glow", "vessel_light_glow"):
        assert "mod(TIME," in (SHADERS / f"{name}.gdshader").read_text(), name


def test_navigation_light_arcs_are_colregs():
    import math
    assert abs(float(_const(VESSEL_LIGHTS, "MASTHEAD_HALF")) - math.radians(112.5)) < 1e-4
    assert abs(float(_const(VESSEL_LIGHTS, "SIDE_HALF")) - math.radians(56.25)) < 1e-4
    assert abs(float(_const(VESSEL_LIGHTS, "STERN_HALF")) - math.radians(67.5)) < 1e-4
    vessel = _code((SHADERS / "vessel_light_glow.gdshader").read_text())
    # Bearing from dead ahead (-Z), + to starboard (+X), against the arc.
    assert "atan(local.x, -local.z)" in vessel and "INSTANCE_CUSTOM.x" in vessel
    model = (SCRIPTS / "visual" / "VesselModel.gd").read_text()
    assert "VesselLights.attach(self)" in _function(model, "_ready")
