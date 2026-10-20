"""Static checks behind roadmap U2-07 (pacing: briefing, calendar HUD, speeds).

The rules are tested in src/LakskongenSim.Tests/EventBus/GamePacingTests.cs
and the running game in godot/tests/UiFlowTests.gd. These pin what can be read
from the files: the keys of D15 (Space, 1-4, N), one owner of the clock (the
cards and the pause menu hold it, nobody else stops the timer), the values
the GDScript side mirrors from the C# rules, and the farm-types deadline rule
(FARM_TYPES_SPEC section 3.5) the C# constants implement.
"""

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
SCRIPTS = GODOT / "scripts"
PACING = REPO / "src" / "LakskongenSim" / "Events" / "GamePacing.cs"
CLOCK = SCRIPTS / "autoload" / "GameBridge.Clock.cs"
SPEC = REPO / "docs" / "FARM_TYPES_SPEC.md"

KEYCODES = {"space": 32, "1": 49, "2": 50, "3": 51, "4": 52, "n": 78, "v": 86, "i": 73}


def _input_map() -> dict[str, list[tuple[int, bool, bool, bool, bool]]]:
    """action -> [(physical_keycode, shift, ctrl, alt, meta)] from project.godot."""
    text = (GODOT / "project.godot").read_text(encoding="utf-8")
    section = text.split("[input]", 1)[1].split("\n[", 1)[0]
    actions = {}
    for name, body in re.findall(r"(?m)^(\w+)=\{(.*?)\n\}", section, re.S):
        keys = []
        for ev in re.findall(r"Object\(InputEventKey,(.*?)\)", body, re.S):
            def flag(f: str) -> bool:
                return f'"{f}":true' in ev
            phys = int(re.search(r'"physical_keycode":(\d+)', ev).group(1))
            keys.append((phys, flag("shift_pressed"), flag("ctrl_pressed"), flag("alt_pressed"), flag("meta_pressed")))
        actions[name] = keys
    return actions


def _cs_int(text: str, name: str) -> int:
    return int(re.search(rf"public const int {name} = (\d+);", text).group(1))


def test_the_clock_keys_are_d15s() -> None:
    actions = _input_map()
    assert actions["toggle_pause"] == [(KEYCODES["space"], False, False, False, False)]
    for i in range(1, 5):
        assert actions[f"speed_{i}"] == [(KEYCODES[str(i)], False, False, False, False)], i
    assert actions["next_event"] == [(KEYCODES["n"], False, False, False, False)]
    # The notification drawer moved off N (D15 gives N to the next event).
    assert actions["toggle_notifications"] == [(KEYCODES["v"], False, False, False, False)]
    assert actions["toggle_attention_inbox"][0][0] == KEYCODES["i"]


def test_no_two_actions_share_a_key() -> None:
    seen: dict[tuple, str] = {}
    for action, keys in _input_map().items():
        for key in keys:
            assert key not in seen, f"{action} and {seen[key]} share {key}"
            seen[key] = action


def test_the_speed_keys_need_an_exact_match() -> None:
    """Cmd/Ctrl+digit opens a tab and Shift+digit is a lens (D15): a digit
    with a modifier must not change the speed."""
    hud = (SCRIPTS / "ui" / "HUD.gd").read_text(encoding="utf-8")
    assert "event.is_action_pressed(SPEED_ACTIONS[i], false, true)" in hud
    assert 'event.is_action_pressed("next_event", false, true)' in hud


def test_cards_and_menus_hold_the_clock_keys() -> None:
    clock_keys = {"toggle_pause", "next_event", "speed_1", "speed_2", "speed_3", "speed_4"}
    for path in (SCRIPTS / "ui" / "CriticalEventCard.gd", SCRIPTS / "ui" / "PauseMenu.gd",
                 SCRIPTS / "ui" / "BriefingCard.gd"):
        text = path.read_text(encoding="utf-8")
        block = re.search(r"const BLOCKED_ACTIONS := \[(.*?)\]", text, re.S).group(1)
        blocked = set(re.findall(r'&"(\w+)"', block))
        assert clock_keys <= blocked, (path.name, sorted(clock_keys - blocked))


def test_one_owner_of_the_clock() -> None:
    """The cards and the pause menu hold the clock through the bridge (one
    owner, U2-07); the hold names match the bridge's; only the bridge starts
    and stops the auto-tick timer."""
    clock = CLOCK.read_text(encoding="utf-8")
    holds = dict(re.findall(r'public const string (Hold\w+) = "(\w+)";', clock))
    card = (SCRIPTS / "ui" / "CriticalEventCard.gd").read_text(encoding="utf-8")
    menu = (SCRIPTS / "ui" / "PauseMenu.gd").read_text(encoding="utf-8")
    assert f'const CLOCK_HOLD := "{holds["HoldCriticalEvent"]}"' in card
    assert f'const CLOCK_HOLD := "{holds["HoldPauseMenu"]}"' in menu
    assert "_sim.hold_clock(CLOCK_HOLD)" in card and "_sim.release_clock(CLOCK_HOLD)" in card
    assert "_sim.hold_clock(CLOCK_HOLD)" in menu and "_sim.release_clock(CLOCK_HOLD)" in menu
    assert "_was_paused" not in menu
    for path in sorted((SCRIPTS / "autoload").glob("GameBridge*.cs")):
        text = path.read_text(encoding="utf-8")
        starts = len(re.findall(r"_simulationTimer\.Start\(\)", text))
        if path.name != "GameBridge.Clock.cs":
            assert starts == 0, f"{path.name} starts the timer outside the clock"
    # Main.gd no longer drives the clock from the HUD's signal (the HUD does).
    main = (SCRIPTS / "Main.gd").read_text(encoding="utf-8")
    assert "_on_speed_changed" not in main
    assert "_sim.set_paused(false)" not in main


def test_the_gdscript_side_mirrors_the_rules() -> None:
    pacing = PACING.read_text(encoding="utf-8")
    steps = [int(s) for s in re.search(r"Steps \{ get; \} = new\[\] \{ ([\d, ]+) \}", pacing).group(1).split(",")]
    assert steps == [1, 3, 10, 30]
    hud = (SCRIPTS / "ui" / "HUD.gd").read_text(encoding="utf-8")
    prefs = (SCRIPTS / "ui" / "GameplayPrefs.gd").read_text(encoding="utf-8")
    assert "const SPEEDS: Array[int] = [1, 3, 10, 30]" in hud
    assert "const SPEEDS: Array[int] = [1, 3, 10, 30]" in prefs
    quiet = int(re.search(r"const PACING_QUIET_DAYS := (\d+)", hud).group(1))
    assert quiet == _cs_int(pacing, "NextEventMaxDays")


def test_the_deadline_rule_is_the_farm_types_one() -> None:
    """FARM_TYPES_SPEC section 3.5: slow to 1x at <= 7 days left, pause at <= 1."""
    spec = SPEC.read_text(encoding="utf-8")
    assert "`DeadlineDay − today ≤ 7` slows the game to 1×" in spec
    assert "At ≤ 1 sim-day remaining with no response, the game pauses." in spec
    pacing = PACING.read_text(encoding="utf-8")
    assert _cs_int(pacing, "SlowAtDaysLeft") == 7
    assert _cs_int(pacing, "PauseAtDaysLeft") == 1


def test_the_bridge_announces_the_clock() -> None:
    clock = CLOCK.read_text(encoding="utf-8")
    assert "[Signal] public delegate void ClockChangedEventHandler(" in clock
    bridge = (SCRIPTS / "SimBridge.gd").read_text(encoding="utf-8")
    assert "signal clock_changed(clock: Dictionary)" in bridge
    assert "_game_bridge.ClockChanged.connect(" in bridge
    # A new game of the player's starts paused on its briefing; QA starts do not.
    core = (SCRIPTS / "autoload" / "GameBridge.Core.cs").read_text(encoding="utf-8")
    assert "StartNewGameInternal(playerName, startingCapital, seed: null, briefing: true)" in core
    assert "StartNewGameInternal(playerName, startingCapital, seed, briefing: false)" in core
    depth = (SCRIPTS / "autoload" / "GameBridge.Depth.cs").read_text(encoding="utf-8")
    assert "briefing: seed == null" in depth
    scene = (GODOT / "scenes" / "GameScene.tscn").read_text(encoding="utf-8")
    assert 'name="BriefingCard" parent="EventLayer"' in scene
