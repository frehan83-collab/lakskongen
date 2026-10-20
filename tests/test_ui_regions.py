"""Static checks behind roadmap U1-04 (screen regions and controls).

The runtime half (overlap lint at three UI scales, the toast stack, the
overflow menu, real Cmd/Ctrl+digit presses) lives in
godot/tests/UiFlowTests.gd; these pin what can be read from the files.
"""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"

# Godot 4 Key enum: KEY_F1 = 4194332 … KEY_F35 = 4194366.
F_KEY_CODES = range(4194332, 4194367)
PLAYER_CODE_DIRS = ("scripts", "ui", "scenes", "theme")


def _player_scripts() -> list[Path]:
    files: list[Path] = []
    for folder in PLAYER_CODE_DIRS:
        files += [p for p in (GODOT / folder).rglob("*.gd") if "/tools/" not in p.as_posix()]
    return files


def test_no_function_key_bindings_in_player_code() -> None:
    """D15: F1–F12 clash with macOS (brightness, Mission Control, volume)."""
    offenders = []
    for path in _player_scripts():
        for n, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            code = line.split("#", 1)[0]
            if re.search(r"\bKEY_F\d+\b", code):
                offenders.append(f"{path.relative_to(REPO)}:{n}: {line.strip()}")
    assert offenders == []


def test_no_function_key_in_the_input_map() -> None:
    project = (GODOT / "project.godot").read_text(encoding="utf-8")
    codes = [int(c) for c in re.findall(r'"(?:physical_)?keycode":(\d+)', project)]
    assert [c for c in codes if c in F_KEY_CODES] == []


def test_core_tabs_open_with_command_digits() -> None:
    source = (GODOT / "scripts" / "ui" / "GameUI.gd").read_text(encoding="utf-8")
    digits = re.findall(r'TabDefinition\.new\("(\w+)".*?(KEY_\d)\)\)', source)
    assert [d for _, d in digits] == [f"KEY_{n}" for n in (1, 2, 3, 4, 5, 6, 7, 8, 9, 0)]
    assert "is_command_or_control_pressed()" in source


def test_the_goal_chip_lives_in_the_hud_bar() -> None:
    """The goal tracker floated over the rail and the speed buttons."""
    hud = (GODOT / "scenes" / "ui" / "HUD.tscn").read_text(encoding="utf-8")
    game = (GODOT / "scenes" / "GameScene.tscn").read_text(encoding="utf-8")
    assert 'name="GoalChip" parent="MainHBox"' in hud
    assert "ScenarioGoalHUD" not in game


def test_toasts_stack_in_the_bottom_right_corner() -> None:
    source = (GODOT / "scripts" / "ui" / "ToastNotification.gd").read_text(encoding="utf-8")
    assert "PRESET_BOTTOM_RIGHT" in source
    assert "MAX_VISIBLE_TOASTS    := 3" in source
    # The container lays out slots; tweens only move a toast inside its slot.
    assert "ToastSlot" in source and "TOAST_TOP" not in source
