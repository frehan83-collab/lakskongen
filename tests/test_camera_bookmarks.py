"""F2-21 camera targets, bookmarks and the offshore sea (farm-types spec §4
"Camera" and "Wave driver", §5.5 rules 2 and 6, §7.2 acceptance 1-6).

The UI-input half (G pressed as a player does, the framings measured on the
real camera, the drawn spectrum at Hs 4 m, the chip against the engine's
forecast) is godot/tests/CameraSeaChecks.gd in the UI flow suite; the
engine's split of the sea and the swell bands' spectrum are xUnit
(OpenSeaSwellTests). These static checks hold the pieces together:

- G is a rebindable input action (D15) and nothing else answers to G;
- the camera cycles Fjord -> Land -> Hav from that action, and the hint
  names it in both languages;
- the presenter makes every pad and slot a target and falls back to the
  hero plots;
- the water shader's swell include draws as many components as the
  director builds, with WaveField's own band limit and shallow fade, and its
  wavelengths lie at 150-300 m; the director alone writes its uniforms and
  hands the CPU field the very arrays;
- the chip's status colours stand at least 20 delta E off the hull's safety
  yellow (spec §5.5 rule 6) and its glyph has a shape per status.
"""

import math
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
PROJECT = (GODOT / "project.godot").read_text(encoding="utf-8")
CAMERA = (GODOT / "scripts" / "environment" / "FjordCamera.gd").read_text(encoding="utf-8")
PRESENTER = (GODOT / "scripts" / "visual" / "FarmFacilityVisualManager.gd").read_text(encoding="utf-8")
UNIT = (GODOT / "scripts" / "visual" / "OffshoreUnitVisual.gd").read_text(encoding="utf-8")
DIRECTOR = (GODOT / "scripts" / "environment" / "EnvironmentDirector.gd").read_text(encoding="utf-8")
OPEN_SEA = (GODOT / "scripts" / "environment" / "OpenSea.gd").read_text(encoding="utf-8")
WAVE_FIELD = (GODOT / "scripts" / "environment" / "WaveField.gd").read_text(encoding="utf-8")
WATER = (GODOT / "shaders" / "water_shader.gdshader").read_text(encoding="utf-8")
SWELL = (GODOT / "shaders" / "water" / "water_swell.gdshaderinc").read_text(encoding="utf-8")
CHIP_SHADER = (GODOT / "shaders" / "world_chip.gdshader").read_text(encoding="utf-8")
SEA_MODEL = (REPO / "src" / "LakskongenSim" / "Environment" / "SeaStateModel.cs").read_text(encoding="utf-8")
TRANSLATIONS = (GODOT / "translations" / "translations.csv").read_text(encoding="utf-8").splitlines()
HULL = "#E6BE2E"


def _actions():
    """project.godot input actions -> their physical keycodes."""
    out = {}
    for m in re.finditer(r"^([a-z_0-9]+)=\{\n\"deadzone\".*?\n\"events\": \[(.*?)\]\n\}", PROJECT, re.S | re.M):
        out[m.group(1)] = [int(k) for k in re.findall(r'"physical_keycode":(\d+)', m.group(2))]
    return out


def test_g_is_the_rebindable_bookmark_action_and_nothing_else_answers_to_g():
    actions = _actions()
    assert actions.get("camera_bookmark") == [71], actions.get("camera_bookmark")
    others = [name for name, keys in actions.items() if 71 in keys and name != "camera_bookmark"]
    assert not others, others
    # No script reads the G key itself: the action is the one way in (rebindable, D15).
    for path in (GODOT / "scripts").rglob("*.gd"):
        assert "KEY_G" not in path.read_text(encoding="utf-8"), path


def test_the_camera_cycles_fjord_land_hav_from_the_action():
    assert 'const BOOKMARK_ACTION := "camera_bookmark"' in CAMERA
    assert 'const BOOKMARK_SLOTS := ["fjord", "land", "hav"]' in CAMERA
    assert "event.is_action_pressed(BOOKMARK_ACTION, false, true)" in CAMERA
    # Letters typed into a text field stay text: the action sits behind the letter-key rule.
    handle = CAMERA[CAMERA.index("func _handle_key("):]
    handle = handle[: handle.index("\nfunc ", 10)]
    assert handle.index("_letter_keys_allowed()") < handle.index("BOOKMARK_ACTION")
    hint = next(r for r in TRANSLATIONS if r.startswith("UI_FJORD_HINT,"))
    assert "G: Fjord/Land/Offshore" in hint and "G: Fjord/Land/Hav" in hint


def test_every_pad_and_slot_is_a_target_and_the_bookmarks_fall_back_to_the_hero_plots():
    assert 'const PLOT_TARGET := "plot:%s"' in PRESENTER
    body = PRESENTER[PRESENTER.index("func _register_plot_targets()"):]
    body = body[: body.index("\nfunc ", 10)]
    assert 'land_pad_slots' in body and 'offshore_slots' in body
    assert 'const HERO_PAD := "pad_a"' in PRESENTER and 'const HERO_SLOT := "off_1"' in PRESENTER
    assert 'camera.call("set_bookmark", "land", land if land != "" else PLOT_TARGET % HERO_PAD)' in PRESENTER
    assert 'camera.call("set_bookmark", "hav", hav if hav != "" else PLOT_TARGET % HERO_SLOT)' in PRESENTER
    # The spec's OF1 framing (F2-18) and the Havfarm-class one inside §4's horizon rule.
    assert "const OF1_FRAMING := Vector2(220.0, -18.0)" in PRESENTER
    distance, pitch = map(float, re.search(r"const HAVFARM_FRAMING := Vector2\(([\d.]+), (-[\d.]+)\)", PRESENTER).groups())
    # Horizon row = 0.5 - tan(pitch) / (2 tan(fov/2)); the camera's table gives 45.6 deg at 700 m.
    row = 0.5 - math.tan(math.radians(-pitch)) / (2.0 * math.tan(math.radians(45.62) / 2.0))
    assert distance == 700.0 and 0.10 <= row <= 0.25, row


def _gd_const(text: str, name: str) -> float:
    return float(re.search(rf"const {name} := ([-\d.e]+)", text).group(1))


def test_the_swell_is_drawn_as_built_and_ridden_as_drawn():
    bands = [float(v) for v in re.search(r"SwellBandWavelengthsM\s*=\s*\{([^}]*)\}", SEA_MODEL).group(1).split(",")]
    per_band = int(_gd_const(OPEN_SEA, "COMPONENTS_PER_BAND"))
    assert int(re.search(r"#define SWELL_WAVES (\d+)", SWELL).group(1)) == len(bands) * per_band
    detune = [float(v) for v in re.search(r"const DETUNE := \[([^\]]*)\]", OPEN_SEA).group(1).split(",")]
    lengths = [b / d for b in bands for d in detune]
    assert all(150.0 <= l <= 300.0 for l in lengths), lengths
    assert min(bands) > max(float(v) for v in re.search(r"BandWavelengthsM\s*=\s*\{([^}]*)\}", SEA_MODEL).group(1).split(","))
    # The include runs the water's own band limit, shallow fade and Gerstner terms.
    assert "smoothstep(3.0 * grid_spacing, 6.0 * grid_spacing, wa.w)" in SWELL
    assert _gd_const(WAVE_FIELD, "WAVE_MIN_SPACINGS") == 3.0 and _gd_const(WAVE_FIELD, "WAVE_FULL_SPACINGS") == 6.0
    assert "component_amplitude(wa, exposure, depth)" in SWELL
    assert "d.xz += wk.w * a * (wk.xy / wk.z) * cos(ph);" in SWELL
    assert '#include "res://shaders/water/water_swell.gdshaderinc"' in WATER
    assert "return d + swell_displacement(p, exposure, depth, grid_spacing);" in WATER
    # The fjord gets none of it: the sheltered amplitude row is 0.
    assert "out.append(Vector4(0.0, shown_swell[_band[i]] * _weight[i], _phase[i], TAU / swell_k[i].z))" in OPEN_SEA
    # The director writes the uniforms and hands the CPU field the very arrays, in the same frame.
    assert re.search(r'set_shader_parameter\("swell_k", open_sea\.swell_k\)\n\s+mat\.set_shader_parameter\("swell_amp", swell\)\n\s+wave_field\.set_swell\(open_sea\.swell_k, swell\)', DIRECTOR)
    for path in GODOT.rglob("*.gd"):
        if "/addons/" in path.as_posix() or path.name == "EnvironmentDirector.gd":
            continue
        code = path.read_text(encoding="utf-8")
        assert not re.search(r'set_shader_parameter\(\s*"(swell_k|swell_amp)"', code), path


def _lab(hex_color: str):
    h = hex_color.lstrip("#")
    rgb = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]
    x = (0.4124 * lin[0] + 0.3576 * lin[1] + 0.1805 * lin[2]) / 0.95047
    y = 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2]
    z = (0.0193 * lin[0] + 0.1192 * lin[1] + 0.9505 * lin[2]) / 1.08883
    f = [v ** (1 / 3) if v > 0.008856 else 7.787 * v + 16 / 116 for v in (x, y, z)]
    return (116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2]))


def test_the_chips_status_colours_stand_off_the_hull_yellow_and_carry_a_shape():
    """Spec §5.5 rule 6: a lint requires delta E >= 20 between any
    world-anchored marker and the hull swatch; status is colour plus shape."""
    assert f'const HULL_YELLOW := Color("{HULL}")' in UNIT
    colours = re.findall(r'Color\("(#[0-9A-Fa-f]{6})"\)', re.search(r"const STATUS_COLORS := \[(.*?)\]", UNIT).group(1))
    assert len(colours) == 3
    for c in colours:
        assert math.dist(_lab(c), _lab(HULL)) >= 20.0, c
    # The pill is Ink, never a yellow fill.
    fill = re.search(r"uniform vec4 fill : source_color = vec4\(([\d.]+), ([\d.]+), ([\d.]+)", CHIP_SHADER).groups()
    fill_hex = "#" + "".join(f"{round(float(v) * 255):02X}" for v in fill)
    assert math.dist(_lab(fill_hex), _lab(HULL)) >= 20.0
    # Three shapes, one a status: circle, triangle, square.
    assert "instance uniform float dot_shape" in CHIP_SHADER and "enum Shape { CIRCLE, TRIANGLE, SQUARE }" in (
        GODOT / "scripts" / "visual" / "VesselChip.gd").read_text(encoding="utf-8")
    assert "chip.set_chip(chip_text(short), STATUS_COLORS[status], status as VesselChip.Shape)" in UNIT


def test_the_chip_reads_the_engines_sea_and_shows_in_the_weather_that_hides_the_unit():
    """Spec §4 "Weather and exposure display" and §5.5 rule 2 (F2-18's sea-fog clause)."""
    assert 'var today: Dictionary = sea.get("today", {})' in UNIT
    assert 'SimFormatters.num(float(today.get("hs", 0.0)), 1)' in UNIT
    assert _gd_const(UNIT, "CHIP_FOG_TRANSMITTANCE") == 0.4 and _gd_const(UNIT, "CHIP_SEA_FOG") == 0.5
    wanted = UNIT[UNIT.index("func chip_wanted("):]
    wanted = wanted[: wanted.index("\nfunc ", 10)]
    assert '"transmittance"' in wanted and "get_sea_fog" in wanted and "get_visibility_m" in wanted
    for key in ("FACILITY_CHIP_SEA", "FACILITY_CHIP_SEA_SHORT", "FACILITY_CHIP_WINDOW_IN", "FACILITY_CHIP_WINDOW_NONE",
                "CAMERA_BOOKMARK_FJORD", "CAMERA_BOOKMARK_LAND", "CAMERA_BOOKMARK_HAV"):
        assert any(r.startswith(key + ",") for r in TRANSLATIONS), key
