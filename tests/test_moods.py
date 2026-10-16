"""G3-05 guards: weather drama and the Nordic night.

Eight lighting moods (EnvironmentMood.gd) are weighed every frame from the
sim's weather and calendar and blended by the EnvironmentDirector; their
colours come from the art audit's palette and the owner's three water moods
(OWNER_REFERENCE_BRIEF), and the mood goldens are measured against them
(MoodGoldens.gd, MoodMetrics.gd, ScreenshotHarness 20_mood_*). Frost smoke
comes from the sim (AtmosphereModel.FrostSmoke); cloud shadows are one decal
along the sunlight (CloudShadows.gd) the water also reads; the pens' lamps
(PenLamps.gd) glow through the water at night; the camera sways in storms
(StormSway.gd) and honours reduced motion; the rain slants with the wind.

These checks pin the sources of the colours, the acceptance thresholds, the
single writer of the look, the shader hooks and the recorded evidence. Pure
static analysis plus the evidence file: needs no engine (the UI flow tests,
WeatherDramaChecks.gd, drive the same pieces live).
"""

import json
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
MOOD = GODOT / "scripts/environment/EnvironmentMood.gd"
DIRECTOR = GODOT / "scripts/environment/EnvironmentDirector.gd"
SHADOWS = GODOT / "scripts/environment/CloudShadows.gd"
LAMPS = GODOT / "scripts/environment/PenLamps.gd"
SWAY = GODOT / "scripts/environment/StormSway.gd"
WEATHER = GODOT / "scripts/environment/WeatherController.gd"
SKY = GODOT / "shaders/sky_shader.gdshader"
WATER = GODOT / "shaders/water_shader.gdshader"
WATER_MOOD = GODOT / "shaders/water/water_mood.gdshaderinc"
RAIN = GODOT / "shaders/rain_streak.gdshader"
GOLDENS = GODOT / "scripts/tools/MoodGoldens.gd"
METRICS = GODOT / "scripts/tools/MoodMetrics.gd"
HARNESS = GODOT / "scripts/tools/ScreenshotHarness.gd"
SCENE = GODOT / "scenes/environment/FjordEnvironment.tscn"
UI_FLOW = GODOT / "tests/UiFlowTests.gd"
ATMOSPHERE = REPO / "src/LakskongenSim/Environment/AtmosphereModel.cs"
SKY_BRIDGE = GODOT / "scripts/autoload/GameBridge.Sky.cs"
EVIDENCE = REPO / "docs/run/evidence/g3_05/moods.json"
MIST = GODOT / "scripts/environment/MistBanks.gd"
MIST_SHADER = GODOT / "shaders/mist_bank.gdshader"
CHIP = GODOT / "scripts/visual/VesselChip.gd"
CHIP_SHADER = GODOT / "shaders/world_chip.gdshader"
QUALITY = GODOT / "scripts/environment/RenderQuality.gd"

NAMES = ["sommerdag", "midnattssol", "graavaer", "storm", "blaatime", "nordlys", "frostroyk", "hoest"]

# The palettes the moods are specified by (roadmap G3-05: "the art audit's
# hexes"; the task: "the owner brief's water moods"), and the owner's
# calm-winter reference photo for the one sky neither gives.
ART_AUDIT = {
    "#FFF1DD", "#5E8DB8", "#D5E0E6", "#A9BCC8", "#FFC27A", "#F3D4A4", "#A7B3BA", "#2E4852", "#9AA7AE",
    "#4C5861", "#6E7A82", "#E7ECEC", "#3C5A8A", "#7F96BC", "#D99AA8", "#FFB35C", "#08121F", "#78F2A0",
    "#B8334A", "#7A4CD6", "#DDE4E8", "#D2A23A", "#FFD9A0", "#0D2B3B", "#1F4D5E",
}
OWNER_WATER = {"#0B3F7A", "#12407A", "#1E3340", "#2A4655", "#5E666C"}
OWNER_REF2_SKY = {"#81A0B0", "#C5D2DA"}


def _code(path: Path) -> str:
    lines = path.read_text(encoding="utf-8").splitlines()
    if path.suffix in (".gd",):
        return "\n".join(line.split("#", 1)[0] if not line.lstrip().startswith('"') else line for line in lines)
    return "\n".join(re.sub(r"(?<!:)//.*$", "", line) for line in lines)


def _specs() -> dict:
    text = MOOD.read_text(encoding="utf-8")
    block = text[text.index("const SPECS := {"):text.index("\n}\n", text.index("const SPECS := {"))]
    specs = {}
    for name, body in re.findall(r'"(\w+)": \{(.*?)"source"', block, re.S):
        specs[name] = {key: re.findall(r'"(#[0-9A-Fa-f]{6})"', vals) for key, vals in re.findall(r'"(sky|water|smoke)": \[([^\]]*)\]', body)}
    return specs


def _profiles() -> list:
    text = MOOD.read_text(encoding="utf-8")
    block = text[text.index("const PROFILES := ["):text.index("\n]\n", text.index("const PROFILES := ["))]
    return [dict(re.findall(r'"(\w+)": ("#[0-9A-Fa-f]{6}"|Color\([^)]*\)|-?[\d.]+)', p)) for p in re.findall(r"\{(.*?)\}", block, re.S)]


def _const(text: str, name: str) -> float:
    match = re.search(rf"const {name}\s*(?::\s*\w+)?\s*:=\s*(-?[\d.]+)", text)
    assert match, name
    return float(match.group(1))


def test_eight_moods_with_one_profile_shape():
    text = MOOD.read_text(encoding="utf-8")
    assert re.search(r"enum Id \{ SOMMERDAG, MIDNATTSSOL, GRAAVAER, STORM, BLAATIME, NORDLYS, FROSTROYK, HOEST \}", text)
    assert 'const NAMES: Array[String] = ["sommerdag", "midnattssol", "graavaer", "storm", "blaatime", "nordlys", "frostroyk", "hoest"]' in text
    profiles = _profiles()
    assert len(profiles) == 8
    keys = {frozenset(p) for p in profiles}
    assert len(keys) == 1, "every mood profile has the same keys"
    assert {"zenith", "horizon", "horizon_sun", "sky_grade", "deck", "fog", "fog_value", "sun", "water", "exposure_ev",
            "cloud_base_m", "sky_haze", "deck_value"} <= set(next(iter(keys)))


def test_the_specs_come_from_the_art_audit_the_owner_and_the_owner_reference():
    specs = _specs()
    assert sorted(specs) == sorted(NAMES)
    allowed = ART_AUDIT | OWNER_WATER | OWNER_REF2_SKY
    for name, spec in specs.items():
        assert spec["sky"] and spec["water"], name
        for hexes in spec.values():
            for h in hexes:
                assert h.upper() in allowed, f"{name}: {h} is in no source palette"
    # The owner's three water moods are the targets where they apply.
    assert specs["sommerdag"]["water"] == ["#0B3F7A", "#12407A"]
    assert specs["graavaer"]["water"] == ["#5E666C"]
    for calm in ("blaatime", "frostroyk", "midnattssol"):
        assert specs[calm]["water"] == ["#1E3340", "#2A4655"], calm
    # The art audit's skies where it gives them.
    assert specs["graavaer"]["sky"] == ["#A7B3BA"]
    assert specs["storm"]["sky"][0] == "#4C5861"
    assert specs["nordlys"]["sky"] == ["#08121F"]
    assert specs["blaatime"]["sky"] == ["#3C5A8A", "#7F96BC"]
    assert specs["frostroyk"]["smoke"] == ["#DDE4E8"]
    assert _const(MOOD.read_text(encoding="utf-8"), "SPEC_DELTA_E") == 10.0


def test_the_profiles_carry_the_art_audits_palette():
    profiles = dict(zip(NAMES, _profiles()))
    assert profiles["sommerdag"]["sun"] == '"#FFF1DD"' and profiles["sommerdag"]["zenith"] == '"#5E8DB8"'
    assert profiles["sommerdag"]["fog"] == '"#A9BCC8"'
    assert profiles["midnattssol"]["sun"] == '"#FFC27A"'
    assert profiles["graavaer"]["deck"] == '"#A7B3BA"' and profiles["graavaer"]["fog"] == '"#9AA7AE"'
    assert profiles["storm"]["deck"] == '"#4C5861"' and profiles["storm"]["fog"] == '"#6E7A82"'
    assert profiles["blaatime"]["zenith"] == '"#3C5A8A"' and profiles["blaatime"]["horizon"] == '"#7F96BC"'
    assert profiles["blaatime"]["horizon_sun"] == '"#D99AA8"'
    assert profiles["nordlys"]["zenith"] == '"#08121F"'
    assert profiles["hoest"]["sun"] == '"#FFD9A0"'
    # Mist on the mountains (height fog and MistBanks) only under a deck; a
    # storm's deck hangs dark over a lighter far horizon, its rain veil on the
    # land a shade under that horizon, so the walls stand dark against it.
    for name in NAMES:
        base = float(profiles[name]["cloud_base_m"])
        assert (base > 0.0) == (name in ("graavaer", "storm")), name
        assert (float(profiles[name]["mist"]) > 0.0) == (base > 0.0), name
    assert float(profiles["graavaer"]["mist"]) > float(profiles["storm"]["mist"])
    assert float(profiles["storm"]["deck_value"]) < 0.6 and float(profiles["storm"]["horizon_lift"]) >= 1.0
    assert float(profiles["storm"]["fog_value"]) < 1.0


def test_the_moods_are_weighed_from_the_sims_weather_and_calendar():
    code = _code(MOOD)
    for name in ("sun_elevation", "sun_direction", "day_of_year", "cloud", "rain", "wind"):
        assert f'inputs.get("{name}"' in code, name
    director = _code(DIRECTOR)
    assert "EnvironmentMood.weights(inputs)" in director and "EnvironmentMood.blend(mood_weights)" in director
    assert "time_system.get_day_of_year()" in director and "weather_system.get_rain()" in director
    # The weights follow their targets over seconds; a snap takes them at once.
    assert re.search(r"const MOOD_RATE := 0\.\d+", director)
    assert "1.0 - exp(-MOOD_RATE * delta)" in director


def test_the_director_writes_the_mood_and_no_one_else_writes_the_look():
    director = _code(DIRECTOR)
    for uniform in ("grade_zenith", "grade_horizon", "grade_horizon_sun", "grade_amount", "grade_deck", "grade_deck_amount",
                    "grade_deck_value", "sky_haze_color", "deep_scatter", "frost_smoke", "frost_smoke_light",
                    "cloud_shadow_strength", "cloud_shadow_map", "lamp_glow", "lamp_count", "lamp_light"):
        assert f'"{uniform}"' in director, uniform
    for path in (SHADOWS, LAMPS, SWAY, MOOD):
        code = _code(path)
        assert not re.search(r"\.light_energy\s*=(?!=)|\.fog_\w+\s*=(?!=)|set_shader_parameter", code), path.name
    # The fog stays Koschmieder's: the mood touches its colour and the height
    # fog (mist under a low deck), never the density.
    assert "env.fog_density = KOSCHMIEDER / maxf(distance_vis, 1.0)" in director
    assert "env.fog_height_density = -base_fog" in director


def test_the_sky_grades_its_clear_sky_and_deck_keeping_the_light():
    sky = _code(SKY)
    for uniform in ("grade_zenith", "grade_horizon", "grade_horizon_sun", "grade_amount", "grade_deck",
                    "grade_deck_amount", "grade_deck_value", "sky_haze_color"):
        assert re.search(rf"uniform \w+ {uniform}\b", sky), uniform
    assert "target * (dot(rgb, LUMA) / lt)" in sky, "the grade keeps the luminance"
    assert "sky_rgb = grade_sky(sky_rgb, dir, sun_dir);" in sky
    assert "cl.rgb = grade_to(cl.rgb, grade_deck, grade_deck_amount) * grade_deck_value;" in sky
    assert "clear_h = grade_sky(clear_h, flat_dir, sun_dir);" in sky
    # The unfogging of the reduced passes still uses the fog's light colour.
    assert re.search(r"cl\.rgb - sky_fog_k \* haze_color - sky_fog_scatter \* scatter", sky)


def test_the_water_draws_frost_smoke_cloud_shadows_and_lamps():
    water = _code(WATER)
    assert '#include "res://shaders/water/water_mood.gdshaderinc"' in water
    # Lamps and smoke per vertex (smooth over tens of metres; in the fragment they cost ~1 ms).
    assert "v_mood = vec4(lamp_glow_at(p0), frost_smoke_cover(p0, normalize(CAMERA_POSITION_WORLD - VERTEX)));" in water
    assert "cloud_shadow_at(v_world)" in water and "float smoke = v_mood.a;" in water
    inc = _code(WATER_MOOD)
    assert "frost_smoke_depth / max(view_w.y" in inc, "the smoke's path through its layer thickens at a graze"
    assert "#define MAX_LAMPS 24" in inc
    assert "texture(" not in inc.split("float frost_smoke_cover", 1)[1].split("}", 1)[0], "the smoke's noise repeats no tile"
    lamps = LAMPS.read_text(encoding="utf-8")
    assert "const MAX_LAMPS := 24" in lamps and "const LAMP_DEPTH_M := 5.0" in lamps
    # The water stays opaque (G1-05).
    assert "ALPHA" not in water and "screen_texture" not in water.lower()


def test_cloud_shadows_are_a_decal_along_the_sunlight():
    code = _code(SHADOWS)
    assert "extends Decal" in code and "func _init()" in code, "set up before the director's first update"
    assert "Basis(x, y, z)" in code and "x.cross(y)" in code, "the decal's +Y points at the sun"
    assert "normal_fade = SUN_FACING" in code and "cull_mask = LAND_LAYERS" in code
    assert "threshold_for(cover)" in code, "the shadows cover the cloud's share of the ground"
    assert "PERIOD_M * roundf(" in code, "snapped by whole periods, the pattern never jumps"
    scene = SCENE.read_text(encoding="utf-8")
    assert '[node name="CloudShadows" type="Decal"' in scene and '[node name="PenLamps" type="Node3D"' in scene
    assert 'cloud_shadows = NodePath("../CloudShadows")' in scene and 'pen_lamps = NodePath("../PenLamps")' in scene


def test_storm_sway_is_additive_and_honours_reduced_motion():
    code = _code(SWAY)
    assert "cam.h_offset += offset.x - applied.x" in code and "cam.v_offset += offset.y - applied.y" in code
    assert "MotionPrefs.gd" in code and '"reduced_motion"' in code and "accessibility_should_reduce_animation" in code
    assert _const(SWAY.read_text(encoding="utf-8"), "MAX_SCREEN_FRACTION") <= 0.005
    assert "StormSway.ensure(camera)" in _code(DIRECTOR)


def test_rain_slants_with_the_wind():
    weather = _code(WEATHER)
    assert "Vector3(wind.x, -RAIN_FALL_MS, wind.y).normalized()" in weather
    assert "process.direction = aim" in weather
    scene = SCENE.read_text(encoding="utf-8")
    block = scene[scene.index('[sub_resource type="ParticleProcessMaterial" id="rain_process"]'):]
    assert "particle_flag_align_y = true" in block[:block.index("\n\n")]
    assert 'shader = ExtResource("23_rain_streak")' in scene
    rain = _code(RAIN)
    assert "MODEL_MATRIX[1].xyz" in rain, "a streak lies along its drop's velocity"


def test_frost_smoke_comes_from_the_sim():
    atmosphere = ATMOSPHERE.read_text(encoding="utf-8")
    assert "double FrostSmoke01 = 0.0" in atmosphere and "public static double FrostSmoke(" in atmosphere
    assert "Saunders (1964" in atmosphere
    bridge = SKY_BRIDGE.read_text(encoding="utf-8")
    assert '["frost_smoke"] = atmosphere.FrostSmoke01' in bridge and "public Godot.Collections.Dictionary SkyContextFor(" in bridge
    weather = _code(WEATHER)
    assert '"frost_smoke": float(ctx.get("frost_smoke", 0.0))' in weather


def test_the_goldens_hold_the_roadmap_thresholds():
    text = GOLDENS.read_text(encoding="utf-8")
    moods = re.findall(r'\{"mood": "(\w+)"', text)
    assert sorted(moods) == sorted(NAMES) and len(moods) == 8
    assert _const(text, "MIN_PHASH_DELTA") == 9
    assert _const(text, "MIN_LAMP_FRACTION") == 0.003
    assert _const(text, "MIN_STORM_HS") == 2.0
    assert "EnvironmentMood.SPEC_DELTA_E" in text
    assert "gb.SkyContextFor(" in text, "each golden is a real sim atmosphere"
    harness = _code(HARNESS)
    assert "await _capture_mood_goldens()" in harness and "MoodGoldens.new()" in harness
    assert "WeatherDramaChecks.new().run(self)" in _code(UI_FLOW)
    metrics = _code(METRICS)
    assert "static func delta_e2000(" in metrics and "static func phash(" in metrics


def test_the_mood_goldens_are_recorded():
    report = json.loads(EVIDENCE.read_text(encoding="utf-8"))
    goldens = report["goldens"]
    assert sorted(goldens) == sorted(NAMES)
    assert report["failures"] == []
    assert report["min_phash"] >= 9
    for name, g in goldens.items():
        assert g["composed_mood"] == name
        assert g["sky_de"] <= 10.0 and g["water_de"] <= 10.0, name
    assert goldens["nordlys"]["lamp_fraction"] >= 0.003
    assert goldens["storm"]["exposed_hs"] >= 2.0 and goldens["storm"]["exposed_hs_weakest_storm"] >= 2.0
    assert goldens["storm"]["skyline_dl"] >= 15.0
    # The grey day's low cloud banks were drawn in its golden.
    assert goldens["graavaer"]["mist"] >= 0.9 and goldens["graavaer"]["mist_banks"] > 20
    motion = [float(v) for v in re.findall(r"[\d.]+", str(goldens["sommerdag"]["shadow_motion"]))]
    assert motion[0] >= 0.5 and motion[0] >= 3.0 * motion[1]


def test_the_engine_gets_the_directors_light_scene_linear():
    """Environment fog/ambient colours and source_color uniforms are sRGB to the engine."""
    director = _code(DIRECTOR)
    assert re.search(r"static func encoded\(linear: Color\) -> Color:\n\treturn .*\.linear_to_srgb\(\)", director)
    assert 'env.fog_light_color = encoded(c.get("fog_light_color", c["horizon_color"]))' in director
    assert 'env.ambient_light_color = encoded(c["horizon_color"])' in director
    assert 'set_shader_parameter("haze_color", encoded(' in director
    assert 'set_shader_parameter("sky_haze_color", encoded(' in director
    sky = _code(SKY)
    assert re.search(r"uniform vec3 haze_color : source_color", sky) and re.search(r"uniform vec3 sky_haze_color : source_color", sky)


def test_a_storm_stays_readable_and_koschmieder_holds_above_the_floor():
    text = DIRECTOR.read_text(encoding="utf-8")
    assert _const(text, "READABLE_VIS_M") <= 3000.0 and _const(text, "READABLE_SOFT_M") > 0.0
    director = _code(DIRECTOR)
    assert "var distance_vis := drawn_visibility(lerpf(visibility, ABOVE_SEA_FOG_VISIBILITY_M, sea_fog))" in director
    # Exact above floor + soft (a clear or grey day is untouched), the G1-04 storm veil still checked.
    assert "lerpf(READABLE_VIS_M, visibility_m, h) + READABLE_SOFT_M * h * (1.0 - h)" in director
    assert "t_storm <= 0.35" in UI_FLOW.read_text(encoding="utf-8")


def test_a_closed_deck_shows_no_clear_sky_and_clouds_have_no_slices():
    sky = _code(SKY)
    assert "sky_rgb = mix(sky_rgb, sky_haze_color * lift, smoothstep(0.8, 0.97, cloud_coverage));" in sky
    assert "uniform float grade_horizon_lift" in sky and '"grade_horizon_lift"' in _code(DIRECTOR)
    # The first cloud on a ray: a fine search, a bisected edge, fixed steps into it.
    for const in ("EDGE_SEARCH", "INNER_STEPS", "INNER_STEP_KM", "COARSE_STEP_KM", "COARSE_MARGIN", "HORIZON_FADE"):
        assert re.search(rf"const \w+ {const} = ", sky), const
    assert "steps * cloud_search" in sky and '"cloud_search"' in _code(DIRECTOR)
    quality = QUALITY.read_text(encoding="utf-8")
    assert '"cloud_search": 1, "cloud_shadows": false' in quality, "Low pays for neither"


def test_low_cloud_banks_hang_on_the_walls_under_a_deck():
    mist = _code(MIST)
    assert "extends MultiMeshInstance3D" in mist and "layers = PlanarReflection.NO_MIRROR_LAYER" in mist
    assert "static func contour_anchors(terrain: FjordTerrain)" in mist and "get_height_at(" in mist
    shader = _code(MIST_SHADER)
    assert "hint_depth_texture" in shader and "soft_m" in shader, "soft against the slope"
    assert "smoothstep(cloud_base - 0.15 * base_soft, cloud_base + base_soft, v_world.y)" in shader, "cut at the cloud base"
    assert "fog_disabled" in shader and "unshaded" in shader
    director = _code(DIRECTOR)
    assert "MistBanks.ensure(get_parent())" in director
    assert 'float(mood.get("mist", 0.0)) * float(c.get("mist_view", 1.0))' in director
    assert '* mist_clear_for(base)' in director, "the height fog clears over the deck too"


def test_world_chips_keep_their_day_brightness_at_night():
    chip = _code(CHIP)
    assert "func set_exposure(exposure: float)" in chip and 'set_shader_parameter("exposure_comp", comp)' in chip
    assert "RenderQuality.DIRECTOR_GROUP" in chip
    assert "* exposure_comp;" in _code(CHIP_SHADER)


def test_qa_shots_take_the_settled_mood():
    harness = _code(HARNESS)
    i = harness.index("func _configure_env(")
    body = harness[i:harness.index("\nfunc ", i + 10)]
    assert 'director.call("compose", 0.0, true)' in body
