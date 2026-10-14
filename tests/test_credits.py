"""The in-game credits list every third-party work the game ships (U1-06, D24).

THIRD_PARTY.md is the inventory; godot/data/credits.json is what the credits
screen shows. A work added to one and not the other, or with a different
licence, fails here. The runtime half (the screen draws every entry and is
reachable from the main menu and the pause menu) is in
godot/tests/UiFlowTests.gd.
"""

import csv
import json
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"


def _inventory() -> dict[str, str]:
    """Component -> licence, from the THIRD_PARTY.md table."""
    rows: dict[str, str] = {}
    header: list[str] = []
    for line in (REPO / "THIRD_PARTY.md").read_text(encoding="utf-8").splitlines():
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if not header:
            header = cells
            continue
        if set(cells[0]) <= {"-", ":"}:
            continue
        rows[cells[header.index("Component")]] = cells[header.index("Licence")]
    return rows


def _credits() -> dict:
    return json.loads((GODOT / "data" / "credits.json").read_text(encoding="utf-8"))


def _translations() -> dict[str, list[str]]:
    with (GODOT / "translations" / "translations.csv").open(encoding="utf-8") as f:
        return {row[0]: row[1:] for row in csv.reader(f)}


def test_the_inventory_is_not_empty() -> None:
    assert len(_inventory()) >= 7


def test_every_inventory_row_is_in_the_credits_with_its_licence() -> None:
    listed = {e["name"]: e["licence"] for e in _credits()["third_party"]}
    assert listed == _inventory()


def test_attribution_licences_carry_a_notice() -> None:
    """CC BY asks for credit, the licence and a link; the entry says so."""
    for entry in _credits()["third_party"]:
        if entry["licence"].startswith("CC BY"):
            assert entry.get("notice_key"), entry["name"]


def test_every_credits_key_is_translated() -> None:
    table = _translations()
    keys = [m.get("role_key", "") for m in _credits()["team"]]
    for entry in _credits()["third_party"]:
        keys += [entry.get(k, "") for k in ("use_key", "display_key", "notice_key", "licence_key")]
    source = (GODOT / "scripts" / "ui" / "CreditsScreen.gd").read_text(encoding="utf-8")
    keys += re.findall(r'_section\("(\w+)"\)', source)
    missing = [k for k in keys if k and (k not in table or not all(v.strip() for v in table[k]))]
    assert missing == []


def test_credits_name_no_invented_people() -> None:
    """The old screen credited a team that never worked on the game."""
    source = (GODOT / "scripts" / "ui" / "CreditsScreen.gd").read_text(encoding="utf-8")
    for invented in ("Aksel Berg", "Camilla Johansen", "Ingrid Lunde", "Jonas Karlsen", "OpenClaw"):
        assert invented not in source
