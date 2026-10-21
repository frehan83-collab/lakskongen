"""U1-09 guards for the live main menu (static; the UI flow tests drive it).

The main menu shows the game's own fjord behind an Ink-glass column with the
brand lockup. These checks keep the pieces honest without starting Godot:
the brand SVGs are generated from the bundled display face and are current,
the menu scene carries no emoji and no baked image backdrop, its glass is the
D13 Ink at 90 %, and the version string comes from the project version.
"""

from __future__ import annotations

import importlib.util
import re
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GODOT = REPO / "godot"
MENU_SCENE = GODOT / "scenes" / "MainMenu.tscn"
MENU_SCRIPT = GODOT / "scripts" / "ui" / "MainMenu.gd"
BACKDROP = GODOT / "scripts" / "ui" / "MenuBackdrop.gd"

EMOJI = re.compile("[\U0001F000-\U0001FAFF☀-➿️]")


def _build_brand():
    spec = importlib.util.spec_from_file_location("build_brand", REPO / "tools" / "build_brand.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_brand_svgs_are_generated_and_current():
    brand = _build_brand()
    for path, text in brand.outputs().items():
        assert path.exists(), f"{path.relative_to(REPO)} missing: run python3 tools/build_brand.py"
        assert path.read_text(encoding="utf-8") == text, f"{path.relative_to(REPO)} is stale"


def test_wordmark_is_the_display_face_outlined():
    brand = _build_brand()
    assert brand.FONT.name == "BarlowCondensed-SemiBold.ttf"
    svg = brand.wordmark_svg()
    # One outlined path, no live text and no embedded raster.
    assert svg.count("<path") == 1 and "<text" not in svg and "<image" not in svg
    assert "<title>Lakskongen</title>" in svg


def test_menu_scene_uses_the_brand_lockup_and_no_emoji():
    scene = MENU_SCENE.read_text(encoding="utf-8")
    assert 'path="res://assets/brand/lakskongen_wordmark.svg"' in scene
    assert 'path="res://assets/brand/lakskongen_mark.svg"' in scene
    assert not EMOJI.search(scene), EMOJI.findall(scene)
    assert not EMOJI.search(MENU_SCRIPT.read_text(encoding="utf-8"))
    # The old flat backdrop and the baked menu image are gone.
    assert "menu-background" not in scene and "WaveEffect" not in scene
    assert not (GODOT / "assets" / "textures" / "menu-background.png").exists()


def test_menu_world_is_the_live_fjord_environment():
    scene = MENU_SCENE.read_text(encoding="utf-8")
    assert 'path="res://scripts/ui/MenuBackdrop.gd"' in scene
    backdrop = BACKDROP.read_text(encoding="utf-8")
    assert 'preload("res://scenes/environment/FjordEnvironment.tscn")' in backdrop
    # No video or picture stands in for the world.
    assert not re.search(r"VideoStream|\.ogv|TextureRect", backdrop)


def test_menu_glass_is_d13_ink_at_ninety_percent():
    scene = MENU_SCENE.read_text(encoding="utf-8")
    glass = scene[scene.index('[node name="Glass"'):]
    glass = glass[: glass.index("\n\n")]
    assert 'material = SubResource("ShaderMaterial_glass")' in glass
    # #0B2A3A at 0.9 (tokens: surfaces.ink.base, base-alpha).
    assert "color = Color(0.043137, 0.164706, 0.227451, 0.9)" in glass
    shader = (GODOT / "shaders" / "menu_glass.gdshader").read_text(encoding="utf-8")
    assert "mix(behind, COLOR.rgb, COLOR.a)" in shader


def test_version_string_comes_from_the_project_version():
    project = (GODOT / "project.godot").read_text(encoding="utf-8")
    version = re.search(r'^config/version="([^"]+)"$', project, re.M)
    assert version, "application/config/version is not set"
    presets = (GODOT / "export_presets.cfg").read_text(encoding="utf-8")
    assert f'application/version="{version.group(1)}"' in presets, "export preset and project versions differ"
    translations = (GODOT / "translations" / "translations.csv").read_text(encoding="utf-8")
    row = next(line for line in translations.splitlines() if line.startswith("UI_VERSION_TEXT,"))
    assert "Nordic" not in row and "%s" in row, row
    assert "Nordic Minimalist" not in MENU_SCRIPT.read_text(encoding="utf-8")
    assert 'ProjectSettings.get_setting("application/config/version"' in MENU_SCRIPT.read_text(encoding="utf-8")


def test_reduced_motion_is_a_saved_accessibility_setting():
    settings = (GODOT / "scripts" / "ui" / "SettingsMenu.gd").read_text(encoding="utf-8")
    # U2-08: the schema and its defaults live in SettingsStore, which the menu uses.
    store = (GODOT / "scripts" / "ui" / "SettingsStore.gd").read_text(encoding="utf-8")
    assert '"reduced_motion": false' in store
    assert "const DEFAULT_SETTINGS := SettingsStore.DEFAULTS" in settings
    assert '"reduced_motion": _motion_check.button_pressed' in settings
    assert "SettingsStore.load_raw()" in (GODOT / "scripts" / "ui" / "MotionPrefs.gd").read_text(encoding="utf-8")
    prefs = (GODOT / "scripts" / "ui" / "MotionPrefs.gd").read_text(encoding="utf-8")
    assert "accessibility_should_reduce_animation()" in prefs
    assert "MotionPrefs.reduced_motion()" in BACKDROP.read_text(encoding="utf-8")
