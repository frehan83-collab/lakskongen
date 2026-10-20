"""Roadmap U2-03: the campaign graph (godot/data/campaign.json) is sound.

CampaignProgress.gd reads it to decide which scenarios are open: two are open
from the start and wins open the next tier. A typo, a scenario left out or a
scenario nothing opens would lock players out silently, so the graph is
checked against the shipped roster (scenarios.json) here.
"""

import json
from pathlib import Path

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
CAMPAIGN = json.loads((GODOT / "data" / "campaign.json").read_text(encoding="utf-8"))
SCENARIOS = [s["id"] for s in json.loads((GODOT / "data" / "scenarios.json").read_text(encoding="utf-8"))]
PROGRESS = (GODOT / "scripts" / "campaign" / "CampaignProgress.gd").read_text(encoding="utf-8")


def _open_after(wins: set[str]) -> set[str]:
    """The rule CampaignProgress.is_unlocked implements."""
    unlocks = CAMPAIGN["unlocks"]
    opened = set(CAMPAIGN["open_from_start"]) | wins
    for source in wins:
        opened |= set(unlocks.get(source, []))
    return opened


def test_the_campaign_orders_every_shipped_scenario_once() -> None:
    order = CAMPAIGN["order"]
    assert sorted(order) == sorted(SCENARIOS), "campaign order and scenarios.json disagree"
    assert len(order) == len(set(order))


def test_the_graph_names_only_shipped_scenarios() -> None:
    ids = set(SCENARIOS)
    assert set(CAMPAIGN["open_from_start"]) <= ids
    for source, targets in CAMPAIGN["unlocks"].items():
        assert source in ids, source
        assert set(targets) <= ids, (source, targets)
        assert source not in targets, f"{source} opens itself"


def test_two_beginner_scenarios_are_open_from_the_start() -> None:
    # The tutorial (Første sesong) and the sandbox-like Bærekraftsmesteren.
    assert CAMPAIGN["open_from_start"] == ["forste_sesong", "barekraft_mester"]


def test_winning_through_the_campaign_opens_every_scenario() -> None:
    wins: set[str] = set()
    for _ in range(len(SCENARIOS)):
        opened = _open_after(wins)
        if opened == set(SCENARIOS):
            break
        playable = opened - wins
        assert playable, f"stuck: {sorted(set(SCENARIOS) - opened)} can never open"
        wins |= playable
    assert _open_after(wins) == set(SCENARIOS)


def test_a_scenario_only_opens_from_an_earlier_one() -> None:
    order = CAMPAIGN["order"]
    for source, targets in CAMPAIGN["unlocks"].items():
        for target in targets:
            assert order.index(target) > order.index(source), f"{source} opens the earlier {target}"


def test_the_progress_file_is_the_players_own() -> None:
    assert 'const DATA_PATH := "res://data/campaign.json"' in PROGRESS
    assert 'const DEFAULT_PATH := "user://campaign.json"' in PROGRESS
    # Writes are atomic and a broken file is kept, never overwritten blind.
    assert 'rename_absolute(tmp, target)' in PROGRESS
    assert '".bad"' in PROGRESS
