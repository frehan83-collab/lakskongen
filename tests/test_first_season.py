"""The first season's data against its screens (roadmap U2-02), statically.

* Every step of godot/data/tutorial_steps.json has its three texts (title,
  why, what to do) in both languages.
* A step's text never contradicts its pace: a step that holds the clock
  does not promise a slow or a running clock, and a step that lets it run
  does not say it stands (the card shows the pace on its own line).
* Every UI event a step waits for is sent by a screen: a mark no screen
  sends would hold the season forever. A tab mark needs the tab in the
  rail (GameUI sends "tab:" + the id of the tab the player opened).
* Every tab a step opens is one the rail can lock (GameUI.ADVANCED_TABS),
  so the gate and the step agree.
The C# half (no day count, no placeholder, the vocabulary) is
src/LakskongenSim.Tests/Tutorial/FirstSeasonTutorialTests.cs; the season
played by input is godot/tests/FirstSeasonFlowChecks.gd.
"""

from __future__ import annotations

import csv
import json
import re
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parent.parent
GODOT = REPO / "godot"
STEPS = json.loads((GODOT / "data" / "tutorial_steps.json").read_text(encoding="utf-8"))
SCRIPTS = GODOT / "scripts"


def _translations() -> dict[str, dict[str, str]]:
    with (GODOT / "translations" / "translations.csv").open(encoding="utf-8", newline="") as f:
        return {row["key"]: row for row in csv.DictReader(f)}


TEXT = _translations()

# What a text may not say for a pace (Norwegian and English).
CONTRADICTS = {
    "hold": [r"går sakte", r"runs slowly", r"høyeste fart", r"top speed"],
    "slow": [r"[Kk]lokka står", r"clock stands", r"clock waits"],
    "run": [r"[Kk]lokka står", r"clock stands", r"clock waits", r"går sakte", r"runs slowly"],
}


def _ids() -> list[str]:
    return [step["id"] for step in STEPS]


def test_the_track_has_eighteen_steps_numbered_in_order():
    assert [step["stepNumber"] for step in STEPS] == list(range(1, len(STEPS) + 1))
    assert len(STEPS) == 18


@pytest.mark.parametrize("step_id", _ids())
def test_every_step_has_its_three_texts_in_both_languages(step_id: str):
    for part in ("TITLE", "TEXT", "DO"):
        key = f"TUTORIAL_{step_id.upper()}_{part}"
        assert key in TEXT, f"{key} missing from translations.csv"
        assert TEXT[key]["en"].strip() and TEXT[key]["no"].strip(), f"{key} has an empty language"


@pytest.mark.parametrize("step", STEPS, ids=_ids())
def test_a_steps_text_never_contradicts_its_pace(step: dict):
    for part in ("TEXT", "DO"):
        key = f"TUTORIAL_{step['id'].upper()}_{part}"
        for language in ("en", "no"):
            text = TEXT[key][language]
            for pattern in CONTRADICTS[step["pace"]]:
                assert not re.search(pattern, text), (
                    f"{key} ({language}) says '{pattern}' but the step's pace is '{step['pace']}': {text}")


def _sent_marks() -> tuple[set[str], bool]:
    """Literal marks the screens send, and whether GameUI sends "tab:" + the opened tab."""
    literal: set[str] = set()
    dynamic_tab = False
    for path in SCRIPTS.rglob("*.gd"):
        if path.name in {"SimBridge.gd", "SimActions.gd"}:
            continue
        source = path.read_text(encoding="utf-8")
        literal.update(re.findall(r'tutorial_mark\("([a-z_:]+)"\)', source))
        if path.name == "GameUI.gd" and re.search(r'tutorial_mark\("tab:" \+ tab_id\)', source):
            dynamic_tab = True
    return literal, dynamic_tab


def _rail_tabs() -> set[str]:
    source = (SCRIPTS / "ui" / "GameUI.gd").read_text(encoding="utf-8")
    return set(re.findall(r'TabDefinition\.new\("([a-z_]+)"', source))


def test_every_ui_event_a_step_waits_for_is_sent_by_a_screen():
    literal, dynamic_tab = _sent_marks()
    tabs = _rail_tabs()
    waited = [step["completionCondition"][3:] for step in STEPS if step["completionCondition"].startswith("ui:")]
    assert waited, "no step waits for a UI event"
    for mark in waited:
        if mark.startswith("tab:"):
            assert dynamic_tab, "GameUI no longer sends the tab it opened"
            assert mark[4:] in tabs, f"a step waits for the tab '{mark[4:]}', which the rail does not have"
        else:
            assert mark in literal, f"a step waits for '{mark}', which no screen sends"


def test_every_tab_a_step_opens_is_one_the_rail_can_lock():
    source = (SCRIPTS / "ui" / "GameUI.gd").read_text(encoding="utf-8")
    match = re.search(r"const ADVANCED_TABS: Array\[String\] = \[([^\]]*)\]", source)
    assert match, "GameUI.ADVANCED_TABS not found"
    lockable = set(re.findall(r'"([a-z_]+)"', match.group(1)))
    opened = {tab for step in STEPS for tab in step.get("unlocks", [])}
    assert opened == lockable, f"steps open {sorted(opened)}, the rail locks {sorted(lockable)}"
