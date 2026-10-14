"""G1-02 / G1-04 / G1-06 guards: one writer for the environment's look.

TimeOfDay, WeatherController, FjordEnvironment, FjordVistaBridge and the
screenshot harness all used to write fog density, cloud cover, sun energy
and the water's wave uniforms every frame, so the last writer won and
leaving the water left its fog behind. EnvironmentDirector.gd is now the
only writer; these checks keep it that way. They also pin the image
pipeline decisions: no SDFGI (no indirect light on Metal in 4.7), MSAA 4x
with SMAA, no tilt-shift pass, and a sky whose sun is the DirectionalLight.
Pure static analysis: needs no engine.
"""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
DIRECTOR = "godot/scripts/environment/EnvironmentDirector.gd"

SKY_UNIFORMS = (
    "sky_time", "cloud_coverage", "cloud_density", "cloud_offset", "aurora_intensity",
    "aurora_offset_km", "aurora_kp", "moon_direction", "moon_illumination", "haze_color",
    "haze_amount", "sky_energy",
)
WATER_WAVE_UNIFORMS = (
    "wave_amplitude", "wave_speed", "wave_frequency", "wave_steepness", "wind_strength",
    "wind_direction", "temperature_factor", "algae_density",
)

# Each category: patterns that write it. "= " but not "==".
CATEGORIES = {
    "fog": [r"\.fog_[a-z_]+\s*=(?!=)", r"""\bset\(\s*["']fog_"""],
    "tonemap": [r"\.tonemap_[a-z_]+\s*=(?!=)", r"""\bset\(\s*["']tonemap_"""],
    "sky uniforms": [r"""set_shader_parameter\(\s*["'](%s)["']""" % "|".join(SKY_UNIFORMS)],
    "light energy / shadow opacity": [
        r"\.light_energy\s*=(?!=)", r"\.shadow_opacity\s*=(?!=)",
        r"""\bset\(\s*["'](light_energy|shadow_opacity)["']""",
    ],
    "water wave uniforms": [r"""set_shader_parameter\(\s*["'](%s)["']""" % "|".join(WATER_WAVE_UNIFORMS)],
}


def _sources():
    for path in sorted(GODOT.rglob("*")):
        rel = path.relative_to(REPO).as_posix()
        if path.suffix not in (".gd", ".cs") or "/addons/" in rel or "/.godot/" in rel:
            continue
        yield rel, path.read_text(encoding="utf-8")


def _writers(patterns):
    compiled = [re.compile(p) for p in patterns]
    found = {}
    for rel, text in _sources():
        for lineno, line in enumerate(text.splitlines(), 1):
            code = line.split("#", 1)[0] if rel.endswith(".gd") else line.split("//", 1)[0]
            if any(c.search(code) for c in compiled):
                found.setdefault(rel, []).append(lineno)
    return found


def test_the_director_is_the_only_writer_of_each_category():
    for category, patterns in CATEGORIES.items():
        writers = _writers(patterns)
        assert set(writers) == {DIRECTOR}, (
            f"{category}: written by {writers} — only {DIRECTOR} may write it "
            "(hand the director an input instead)"
        )


def test_the_harness_forces_no_aurora():
    harness = (GODOT / "scripts/tools/ScreenshotHarness.gd").read_text(encoding="utf-8")
    assert "aurora_intensity" not in harness
    assert "_set_aurora" not in harness


def test_no_sdfgi_anywhere():
    # Godot 4.7 SDFGI gets no indirect light on Metal (#120496): macOS ships SSAO.
    pattern = re.compile(r"sdfgi_enabled\s*=\s*true")
    hits = []
    for path in GODOT.rglob("*"):
        if path.suffix in (".tscn", ".tres", ".godot", ".cfg", ".gd", ".cs") and "/.godot/" not in path.as_posix():
            if pattern.search(path.read_text(encoding="utf-8", errors="ignore")):
                hits.append(path.relative_to(REPO).as_posix())
    assert not hits, f"SDFGI enabled in {hits}"


def test_msaa_4x_and_smaa():
    project = (GODOT / "project.godot").read_text(encoding="utf-8")
    assert "anti_aliasing/quality/msaa_3d=2" in project  # 2 = 4x
    assert "anti_aliasing/quality/screen_space_aa=2" in project  # 2 = SMAA


def test_the_tilt_shift_pass_is_gone():
    assert not (GODOT / "shaders/tilt_shift.gdshader").exists()
    for scene in GODOT.rglob("*.tscn"):
        assert "TiltShift" not in scene.read_text(encoding="utf-8"), scene


def test_the_sky_draws_the_directional_light_as_its_sun():
    sky = (GODOT / "shaders/sky_shader.gdshader").read_text(encoding="utf-8")
    code = "\n".join(line.split("//", 1)[0] for line in sky.splitlines())
    for name in ("LIGHT0_DIRECTION", "LIGHT0_COLOR", "LIGHT0_ENERGY"):
        assert name in code, f"the sun disc must use {name}"
    # The old shader placed its own sun from a time_of_day uniform.
    assert "time_of_day" not in code
    # TIME forces a real-time radiance update every frame; sky_time animates.
    assert not re.search(r"\bTIME\b", code)


def test_the_moon_light_stays_out_of_the_sky():
    scene = (GODOT / "scenes/environment/FjordEnvironment.tscn").read_text(encoding="utf-8")
    moon = scene[scene.index('[node name="Moon"'):]
    moon = moon[: moon.index("\n\n")]
    assert "sky_mode = 1" in moon, "the Moon must be Light Only so LIGHT0 is the sun"
