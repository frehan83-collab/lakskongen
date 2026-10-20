"""Static checks behind roadmap U2-06 (typed event bus, one channel per severity).

The runtime half (routing: one card per critical, one toast per warning,
no duplicates within three days, the camera shake and reduced motion) lives
in godot/tests/UiFlowTests.gd and the bus itself in
src/LakskongenSim.Tests/EventBus/GameEventBusTests.cs. These pin what can be
read from the files: nothing classifies alert text by substring, and the
event types, verbs and texts agree between C#, GDScript and translations.
"""

import csv
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
SCRIPTS = GODOT / "scripts"
CATALOG = REPO / "src" / "LakskongenSim" / "Events" / "GameEventCatalog.cs"
TRANSLATIONS = GODOT / "translations" / "translations.csv"

# The files an event passes through on its way to the player.
EVENT_PATH_GD = [
    SCRIPTS / "ui" / "ConsequenceDirector.gd",
    SCRIPTS / "ui" / "NotificationCenter.gd",
    SCRIPTS / "ui" / "ToastNotification.gd",
    SCRIPTS / "ui" / "CriticalEventCard.gd",
    SCRIPTS / "ui" / "GameEventText.gd",
    SCRIPTS / "ui" / "GameEventActions.gd",
    SCRIPTS / "ui" / "CameraShake.gd",
    SCRIPTS / "ui" / "JuiceEffects.gd",
    SCRIPTS / "ui" / "HUD.gd",
    SCRIPTS / "Main.gd",
    SCRIPTS / "SimBridge.gd",
    SCRIPTS / "bridge" / "SimState.gd",
]
EVENT_PATH_CS = [
    REPO / "src" / "LakskongenSim" / "Events" / "GameEventBus.cs",
    CATALOG,
    REPO / "src" / "LakskongenSim" / "Core" / "SimulationEngine.GameEvents.cs",
    GODOT / "scripts" / "autoload" / "GameBridge.Events.cs",
]

SUBSTRING_CALL = re.compile(r"\.(contains|containsn|find|findn|rfind|rfindn|begins_with|ends_with|match|matchn)\(")
# A substring test on something that holds alert text, anywhere in player code.
TEXT_VAR = r"(?:msg|message|desc|description|text|title|headline|lname|alert_text|body)"
TEXT_SUBSTRING = re.compile(
    rf"\b{TEXT_VAR}\b(?:\.to_lower\(\)|\.to_upper\(\))?\.(?:contains|containsn|find|findn|begins_with|ends_with|match|matchn)\("
    rf"|\bin\s+{TEXT_VAR}\b(?:\.to_lower\(\))?\s*[:)]"
)
CS_TEXT_SUBSTRING = re.compile(
    r"\b(?:Message|Description|Headline|Name|Text|message|description|headline|text)\b\??"
    r"(?:\.ToLowerInvariant\(\)|\.ToLower\(\))?\.(?:Contains|StartsWith|EndsWith|IndexOf)\("
)


def _code(path: Path) -> list[tuple[int, str]]:
    """(line number, code without its comment) for a GDScript file."""
    out = []
    for n, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        in_str = None
        cut = len(line)
        for i, ch in enumerate(line):
            if in_str:
                if ch == in_str and line[i - 1] != "\\":
                    in_str = None
            elif ch in "\"'":
                in_str = ch
            elif ch == "#":
                cut = i
                break
        out.append((n, line[:cut]))
    return out


def _player_scripts() -> list[Path]:
    return [p for p in SCRIPTS.rglob("*.gd") if "/tools/" not in p.as_posix() and "/tutorial/TutorialTests" not in p.as_posix()]


def _catalog_rows() -> list[tuple[str, str, str, str, str]]:
    """(enum, id, category, severity, verb const) per catalog row."""
    text = CATALOG.read_text(encoding="utf-8")
    rows = re.findall(
        r'Row\(GameEventType\.(\w+), "([a-z_]+)", "([a-z_]+)", GameEventSeverity\.(\w+), (\w+)', text)
    assert rows, "catalog parse failed"
    return rows


def _verb_ids() -> dict[str, str]:
    text = CATALOG.read_text(encoding="utf-8")
    verbs = dict(re.findall(r'public const string (\w+) = "([a-z_]+)";', text))
    assert "NoCounterVerb" in verbs
    return verbs


def _translations() -> dict[str, tuple[str, str]]:
    with TRANSLATIONS.open(encoding="utf-8", newline="") as f:
        return {r[0]: (r[1], r[2]) for r in csv.reader(f) if len(r) == 3}


# ── Acceptance 1: no substring classification of alert text ───────────────────

def test_the_event_path_never_reads_text_to_decide_anything() -> None:
    offenders = []
    for path in EVENT_PATH_GD:
        for n, code in _code(path):
            if SUBSTRING_CALL.search(code) or "_has_any(" in code:
                offenders.append(f"{path.relative_to(REPO)}:{n}: {code.strip()}")
    assert offenders == [], "\n".join(offenders)


def test_no_player_script_classifies_alert_text_by_substring() -> None:
    offenders = []
    for path in _player_scripts():
        for n, code in _code(path):
            if TEXT_SUBSTRING.search(code):
                offenders.append(f"{path.relative_to(REPO)}:{n}: {code.strip()}")
    assert offenders == [], "\n".join(offenders)


def test_the_csharp_event_path_never_reads_text() -> None:
    offenders = []
    for path in EVENT_PATH_CS:
        for n, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            code = line.split("//", 1)[0]
            if CS_TEXT_SUBSTRING.search(code):
                offenders.append(f"{path.relative_to(REPO)}:{n}: {line.strip()}")
    assert offenders == [], "\n".join(offenders)


def test_the_text_grep_catches_a_planted_classifier() -> None:
    """The guards above must be able to fail."""
    assert TEXT_SUBSTRING.search('\tif msg.to_lower().contains("storm"):')
    assert TEXT_SUBSTRING.search('\telif message.begins_with("Lus"):')
    assert TEXT_SUBSTRING.search('\tif "rømming" in desc:')
    assert SUBSTRING_CALL.search('if level.contains("crit"):')
    assert CS_TEXT_SUBSTRING.search('if (note.Message.Contains("storm"))')
    assert not TEXT_SUBSTRING.search('\tif fileName.begins_with("autosave"):')


# ── One typed vocabulary across C#, GDScript and the texts ────────────────────

def test_every_event_type_has_a_title_in_both_languages() -> None:
    keys = _translations()
    missing = [f"GAME_EVENT_{row[1].upper()}" for row in _catalog_rows() if f"GAME_EVENT_{row[1].upper()}" not in keys]
    assert missing == []


def test_every_counter_verb_is_performed_and_labelled() -> None:
    verbs = {v for k, v in _verb_ids().items() if k.startswith("Verb")}
    used = {_verb_ids()[row[4]] for row in _catalog_rows()}
    assert used <= verbs | {"none"}
    text = (SCRIPTS / "ui" / "GameEventText.gd").read_text(encoding="utf-8")
    ui_verbs = set(re.findall(r'"([a-z_]+)"', re.search(r"const VERBS := \[(.*?)\]", text, re.S).group(1)))
    assert ui_verbs == verbs
    actions = (SCRIPTS / "ui" / "GameEventActions.gd").read_text(encoding="utf-8")
    tab_verbs = set(re.findall(r'"([a-z_]+)": "', re.search(r"const TAB_VERBS := \{(.*?)\}", actions, re.S).group(1)))
    site_verbs = set(re.findall(r'"([a-z_]+)"', re.search(r"const SITE_VERBS := \[(.*?)\]", actions).group(1)))
    assert tab_verbs | site_verbs | {"inspect_site"} == verbs
    keys = _translations()
    assert [v for v in sorted(verbs) if f"UI_EVENT_VERB_{v.upper()}" not in keys] == []


def test_the_snapshot_layer_reports_catalog_types_only() -> None:
    ids = {row[1] for row in _catalog_rows()}
    source = (SCRIPTS / "bridge" / "SimState.gd").read_text(encoding="utf-8")
    reported = set(re.findall(r'_ev\("([a-z_]+)"', source))
    assert reported, "SimState reports no typed events"
    assert reported <= ids, sorted(reported - ids)


def test_the_bridge_signal_reaches_gdscript() -> None:
    cs = (GODOT / "scripts" / "autoload" / "GameBridge.Events.cs").read_text(encoding="utf-8")
    assert "[Signal] public delegate void GameEventRaisedEventHandler(" in cs
    bridge = (SCRIPTS / "SimBridge.gd").read_text(encoding="utf-8")
    assert "signal game_event_raised(event: Dictionary)" in bridge
    assert "_game_bridge.GameEventRaised.connect(" in bridge
    proxies = (SCRIPTS / "bridge" / "SimProxies.gd").read_text(encoding="utf-8")
    for member in ("GetGameEventLog", "GetGameEventCatalog", "PublishConditionEvent", "GameEventsSuppressed"):
        assert f"game_bridge.{member}" in proxies, member


def test_one_pause_channel_for_criticals() -> None:
    """Main.gd paused on any 'critical' alert text; the card owns the pause now."""
    main = (SCRIPTS / "Main.gd").read_text(encoding="utf-8")
    assert "alert_raised" not in main
    assert "pause_changed.connect" in main
    game = (GODOT / "scenes" / "GameScene.tscn").read_text(encoding="utf-8")
    assert 'name="CriticalEventCard" parent="EventLayer"' in game


# ── Acceptance 4 (static half): the shake stays out of the camera rig ─────────

def test_the_shake_uses_the_view_offset_and_leaves_the_camera_rig_alone() -> None:
    camera = (SCRIPTS / "environment" / "FjordCamera.gd").read_text(encoding="utf-8")
    assert "shake" not in camera.lower()
    assert not re.search(r"\b[hv]_offset\b", camera)
    shake = (SCRIPTS / "ui" / "CameraShake.gd").read_text(encoding="utf-8")
    assert "cam.h_offset +=" in shake and "cam.v_offset +=" in shake
    assert "MotionPrefs.reduced_motion()" in shake
    assert "global_transform" not in shake and ".rotation" not in shake


def test_toasts_keep_clear_of_hurtigvalg_and_the_drawer_header() -> None:
    dashboard = (GODOT / "scenes" / "ui" / "Dashboard.tscn").read_text(encoding="utf-8")
    assert re.search(r'\[node name="ActionsCard"[^\]]*groups=\["toast_keep_clear"\]', dashboard)
    center = (SCRIPTS / "ui" / "NotificationCenter.gd").read_text(encoding="utf-8")
    assert 'header.add_to_group(&"toast_keep_clear")' in center
    toasts = (SCRIPTS / "ui" / "ToastNotification.gd").read_text(encoding="utf-8")
    assert 'KEEP_CLEAR_GROUP      := &"toast_keep_clear"' in toasts
