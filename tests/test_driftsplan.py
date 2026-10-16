"""Static checks behind roadmap F2-05 (the Driftsplan, docs/FARM_TYPES_SPEC.md §1 pillar 5).

The runtime half lives in src/LakskongenSim.Tests/Core/DriftsplanTests.cs (the
engine) and godot/tests/UiFlowTests.gd (_test_driftsplan_card_sets_and_runs_standing_orders,
the card by real clicks). These pin what can be read from the files: every
policy, setting, option and waiting trigger the sim declares has its Norwegian
text, the card shows no number of its own, and a farm-type event names its
unit in the UI.
"""

import csv
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
SIM = REPO / "src" / "LakskongenSim"
CARD = REPO / "godot" / "scripts" / "ui" / "facilities" / "DriftsplanCard.gd"
TRANSLATIONS = REPO / "godot" / "translations" / "translations.csv"


def _translations() -> dict[str, tuple[str, str]]:
    with TRANSLATIONS.open(encoding="utf-8", newline="") as f:
        return {r[0]: (r[1], r[2]) for r in csv.reader(f) if len(r) == 3}


def _policy_files() -> list[Path]:
    """Every file that declares Driftsplan policies (sim-core's and the lanes')."""
    files = [p for p in SIM.rglob("*.cs") if "/bin/" not in p.as_posix() and "/obj/" not in p.as_posix()]
    return [p for p in files if "IDriftsplanPolicy" in p.read_text(encoding="utf-8") and p.name != "Driftsplan.cs"]


def _declared() -> tuple[set[str], set[str], set[str], set[str]]:
    """(policy kinds, setting keys, choice ids, trigger keys) declared in the sim."""
    kinds, keys, choices, triggers = set(), set(), set(), set()
    for path in _policy_files():
        text = path.read_text(encoding="utf-8")
        consts = dict(re.findall(r'public const string (\w+) = "([a-z_]+)";', text))
        for ref in re.findall(r"public string Kind => (?:\w+\.)?(\w+);", text):
            kinds.add(consts[ref])
        keys.update(re.findall(r'Key = "([a-z_]+)"', text))
        for block in re.findall(r"Choices = new\[\] \{([^}]*)\}", text):
            choices.update(re.findall(r'"([a-z_]+)"', block))
        triggers.update(v for k, v in consts.items() if k.startswith("Trigger"))
    return kinds, keys, choices, triggers


def test_the_scan_sees_the_fjord_policies() -> None:
    kinds, keys, choices, triggers = _declared()
    assert {"net_cleaning", "delouse_on_count"} <= kinds
    assert {"interval_days", "share_of_limit", "min_interval_days", "method"} <= keys
    assert {"hydrolicer", "thermolicer", "freshwater", "peroxide"} <= choices
    assert {"site_stocked", "lice_count_above"} <= triggers


def test_every_policy_setting_option_and_trigger_has_norwegian_text() -> None:
    texts = _translations()
    kinds, keys, choices, triggers = _declared()
    wanted = (
        [f"DRIFTSPLAN_POLICY_{k.upper()}" for k in kinds]
        + [f"DRIFTSPLAN_TIP_{k.upper()}" for k in kinds]
        + [f"DRIFTSPLAN_SETTING_{k.upper()}" for k in keys]
        + [f"DRIFTSPLAN_CHOICE_{c.upper()}" for c in choices]
        + [f"DRIFTSPLAN_WAIT_{t.upper()}" for t in triggers]
    )
    missing = [key for key in sorted(wanted) if key not in texts]
    assert missing == []
    untranslated = [key for key in wanted if texts[key][0] == texts[key][1] and key != "DRIFTSPLAN_SETTING_METHOD"]
    assert untranslated == [], "the Norwegian column repeats the English one"


def test_the_card_holds_no_number_of_its_own() -> None:
    """Ranges, defaults and levels come from the sim's catalogue and orders."""
    code = "\n".join(line.split("#", 1)[0] for line in CARD.read_text(encoding="utf-8").splitlines())
    literals = re.findall(r"(?<![\w.])(\d+(?:\.\d+)?)(?![\w.])", code)
    # Layout (margins, separations, a width), 100 for a share shown in percent, its 5 % step, and indices.
    allowed = {"0", "1", "2", "4", "6", "12", "16", "110", "0.0", "1.0", "0.1", "100.0", "5.0"}
    assert sorted(set(literals) - allowed) == []
    for key in ("min", "max", "default", "choices", "default_choice", "source", "trigger_value", "days_left"):
        assert f'"{key}"' in code, key


def test_a_farm_type_event_names_its_unit() -> None:
    text = (REPO / "godot" / "scripts" / "ui" / "GameEventText.gd").read_text(encoding="utf-8")
    subject = text[text.index("static func subject("):]
    subject = subject[: subject.index("\nstatic func ", 1)]
    assert subject.index('"unit_id"') < subject.index('"site_id"'), "the unit wins over the site"
    assert '"unit_name"' in subject
    bridge = (REPO / "godot" / "scripts" / "autoload" / "GameBridge.Events.cs").read_text(encoding="utf-8")
    assert '["unit_name"] = SimulationEngine?.UnitLabel(ev.UnitId)' in bridge


def test_the_pen_view_mounts_the_card_for_its_site() -> None:
    pens = (REPO / "godot" / "scripts" / "ui" / "PenManagement.gd").read_text(encoding="utf-8")
    assert "DriftsplanCard.new()" in pens
    assert '_driftsplan_card.set_unit(pen_id, "fjord_site")' in pens


def test_a_failed_order_opens_the_pen_view_on_its_site() -> None:
    """open_driftsplan opens the pens tab and selects the order's unit there (the card's home)."""
    actions = (REPO / "godot" / "scripts" / "ui" / "GameEventActions.gd").read_text(encoding="utf-8")
    assert '"open_driftsplan": "pens"' in actions
    route = actions[actions.index('if verb == "open_driftsplan":'):]
    route = route[: route.index("return true")]
    assert 'find_child("Panel_pens", true, false)' in route
    assert 'pens.select_pen(unit)' in route and 'event.get("unit_id", "")' in route
