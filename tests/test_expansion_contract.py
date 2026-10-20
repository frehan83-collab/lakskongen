"""U2-01: the expansion flow's contract.

The engine's reason keys (Core/ExpansionViews.cs) all have Norwegian and
English text; the bridge partial projects the map, the build plan and the
auction with the keys the UI reads, and the facade forwards every verb with
the same arguments. Pure static analysis, no engine.
"""

from __future__ import annotations

import csv
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
VIEWS = REPO / "src" / "LakskongenSim" / "Core" / "ExpansionViews.cs"
PARTIAL = REPO / "godot" / "scripts" / "autoload" / "GameBridge.Expansion.cs"
PROXIES = REPO / "godot" / "scripts" / "bridge" / "SimProxies.gd"
ACTIONS = REPO / "godot" / "scripts" / "bridge" / "SimActions.gd"
FACADE = REPO / "godot" / "scripts" / "SimBridge.gd"
TRANSLATIONS = REPO / "godot" / "translations" / "translations.csv"
CATALOGUE = REPO / "src" / "LakskongenSim" / "Environment" / "ExpansionZones.cs"
UI = REPO / "godot" / "scripts" / "ui" / "expansion"


def _rows() -> dict[str, list[str]]:
    with TRANSLATIONS.open(encoding="utf-8", newline="") as f:
        return {r[0]: r for r in csv.reader(f) if len(r) == 3}


def _reasons() -> list[str]:
    block = VIEWS.read_text(encoding="utf-8").split("public static class ExpansionReasons", 1)[1].split("\n}\n", 1)[0]
    return [v for v in re.findall(r'public const string \w+ = "([a-z_]*)";', block) if v]


def test_every_expansion_reason_has_english_and_norwegian_text() -> None:
    reasons = _reasons()
    assert len(reasons) == 11, f"contract harness broken: {reasons}"
    rows = _rows()
    keys = ["EXPANSION_REASON_" + r.upper() for r in reasons]
    missing = [k for k in keys if k not in rows or not rows[k][1].strip() or not rows[k][2].strip()]
    assert not missing, f"expansion reasons without en/no text: {missing}"


def test_every_catalogue_zone_has_a_place_name() -> None:
    ids = re.findall(r'new\("([a-z0-9_]+)", (\d+), "([^"]+)"\)', CATALOGUE.read_text(encoding="utf-8"))
    assert len(ids) == 13 and sorted(int(a) for _, a, _ in ids) == list(range(1, 14))
    rows = _rows()
    for zone_id, _, name in ids:
        row = rows.get("ZONE_" + zone_id.upper())
        assert row is not None and row[1] == name and row[2] == name, zone_id


def test_the_bridge_projects_the_map_the_plan_and_the_auction() -> None:
    src = PARTIAL.read_text(encoding="utf-8")
    for sig in ("public Godot.Collections.Dictionary GetExpansionOverview()",
                "public Godot.Collections.Dictionary PlanSiteBuild(string zoneId)",
                "public Godot.Collections.Dictionary BuildExpansionSite(string zoneId)",
                "public Godot.Collections.Dictionary PreviewLicenceApplication(string zoneId)",
                "public Godot.Collections.Dictionary OpenLicenceAuction(string zoneId)",
                "public Godot.Collections.Dictionary BidOnLicenceAuction(string zoneId, double amountNok)",
                "public Godot.Collections.Dictionary GetLicenceAuction(string zoneId)"):
        assert sig in src, sig
    for key in ("areas", "zones", "can_build", "build_reason_key", "build_label_key", "can_apply",
                "apply_reason_key", "apply_label_key", "traffic_light", "licence_mtb_t", "headroom_t",
                "cost_nok", "locality_mtb_t", "licence_mtb_added_t", "pens", "running_cost_per_month_nok",
                "reserve_nok", "reserve_per_tonne_nok", "min_bid_nok", "days_left", "leader_name", "bids",
                "rival_bids", "player_leads", "reference_2022_per_tonne_nok", "reference_2024_per_tonne_nok"):
        assert f'["{key}"]' in src, f"expansion bridge lacks key {key}"


def test_the_facade_forwards_every_expansion_verb() -> None:
    proxies = PROXIES.read_text(encoding="utf-8")
    actions = ACTIONS.read_text(encoding="utf-8")
    facade = FACADE.read_text(encoding="utf-8")
    for name, csharp, args in (("get_expansion_overview", "GetExpansionOverview", ""),
                               ("plan_site_build", "PlanSiteBuild", "zone_id"),
                               ("preview_licence_application", "PreviewLicenceApplication", "zone_id"),
                               ("get_licence_auction", "GetLicenceAuction", "zone_id")):
        assert re.search(rf"func {name}\(.*\) -> Dictionary: return game_bridge\.{csharp}\({args}\)", proxies), name
        assert re.search(rf"func {name}\(.*\) -> Dictionary: return _proxies\.{name}\({args}\)", facade), name
    for name, csharp in (("build_expansion_site", "BuildExpansionSite"),
                         ("open_licence_auction", "OpenLicenceAuction"),
                         ("bid_on_licence_auction", "BidOnLicenceAuction")):
        assert f"game_bridge.{csharp}(" in actions, name
        assert re.search(rf"func {name}\(.*\) -> Dictionary: return _actions\.{name}\(", facade), name


def test_the_expansion_ui_shows_reasons_through_their_keys() -> None:
    """No expansion script decides by reading text, and none hard-codes a reason."""
    for script in UI.glob("*.gd"):
        src = script.read_text(encoding="utf-8")
        for reason in _reasons():
            assert f'"{reason}"' not in src or reason in ("insufficient_funds", "licence_held"), (script.name, reason)
        assert "message" not in re.findall(r'\.get\("(\w+)"', src), script.name
