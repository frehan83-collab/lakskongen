"""S2-01b real pens: scenario locality layouts, the pen facade and its text.

Static checks (no engine): every scenario site has a mooring frame of 6-12
pens of a positive net that its stocked pens fit in, the per-pen payload and
verbs travel GameBridge partial -> SimProxies/SimActions -> SimBridge facade,
and every pen reason the engine can emit is a translation key with English and
Norwegian text.
"""

import csv
import json
import math
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
SCENARIOS = REPO / "godot" / "data" / "scenarios.json"
CALIBRATION = REPO / "godot" / "data" / "calibration.json"
SCRIPTS = REPO / "godot" / "scripts"
BRIDGE_DIR = SCRIPTS / "autoload"

#: Akvakulturdriftsforskriften § 47a / § 25 (calibration section "pens" / "capacity").
MAX_FISH_PER_PEN = 200_000
MAX_DENSITY_KG_M3 = 25.0


def _volume_m3(circumference_m: float, wall_m: float, cone_m: float) -> float:
    area = circumference_m * circumference_m / (4.0 * math.pi)
    return area * (wall_m + cone_m / 3.0)


def _sites():
    for scenario in json.loads(SCENARIOS.read_text(encoding="utf-8")):
        for site in scenario.get("starting_sites", []):
            yield scenario["id"], site


def test_every_scenario_site_has_a_locality_layout_of_six_to_twelve_pens():
    sites = list(_sites())
    assert len(sites) == 16, f"harness broken: {len(sites)} scenario sites"
    for scenario_id, site in sites:
        layout = site.get("pen_layout")
        assert isinstance(layout, dict), f"{scenario_id}/{site['id']}: no pen_layout"
        assert 6 <= layout["count"] <= 12, f"{site['id']}: {layout['count']} pens"
        assert layout["count"] >= site["pens"], f"{site['id']}: {site['pens']} stocked pens in {layout['count']}"
        assert 90 <= layout["circumference_m"] <= 200, f"{site['id']}: collar {layout['circumference_m']} m"
        assert 15 <= layout["net_wall_depth_m"] + layout["cone_depth_m"] <= 50, f"{site['id']}: net depth"


def test_every_stocked_pen_starts_within_the_per_pen_limits():
    for scenario_id, site in _sites():
        layout = site["pen_layout"]
        volume = _volume_m3(layout["circumference_m"], layout["net_wall_depth_m"], layout["cone_depth_m"])
        assert site["smolt_count"] <= MAX_FISH_PER_PEN, f"{site['id']}: {site['smolt_count']} fish a pen"
        # Even at the plan's 5.2 kg harvest weight with no deaths the start fits § 25.
        assert site["smolt_count"] * 5.2 / volume <= MAX_DENSITY_KG_M3, f"{scenario_id}/{site['id']}"


def test_the_standard_pen_is_calibrated_with_its_sources():
    pens = json.loads(CALIBRATION.read_text(encoding="utf-8"))["pens"]
    assert pens["maxFishPerPen"] == MAX_FISH_PER_PEN
    assert pens["standardCircumferenceM"] == 160.0
    assert pens["standardNetWallDepthM"] + pens["standardConeDepthM"] == 40.0
    assert 6 <= pens["builtSitePenCount"] <= 12
    standard = _volume_m3(160.0, pens["standardNetWallDepthM"], pens["standardConeDepthM"])
    assert abs(standard - 61_115.5) < 1.0, standard


def test_sea_site_details_carry_the_pen_count_and_each_pen():
    farm = (BRIDGE_DIR / "GameBridge.Farm.cs").read_text(encoding="utf-8")
    assert re.search(r'\["pen_count"\]\s*=\s*site\.PhysicalPens\.Count', farm)
    assert re.search(r'\["pens"\]\s*=\s*PenArrayOf\(site\.Id\)', farm)
    pens = (BRIDGE_DIR / "GameBridge.Pens.cs").read_text(encoding="utf-8")
    for key in ("id", "number", "population_id", "occupied", "fish_count", "biomass_kg", "mean_weight_kg",
                "weight_cv", "lice_per_fish", "density_kg_m3", "volume_m3", "circumference_m", "net_depth_m",
                "ration", "fasting"):
        assert f'["{key}"] = pen.' in pens, f"pen dictionary lacks {key}"
    for key in ("allowed", "reason_key", "label_key", "pen_id", "pen_number", "max_fish_per_pen",
                "density_limit_kg_m3", "projected_peak_density_kg_m3", "projected_peak_day",
                "projected_peak_weight_kg", "max_count"):
        assert f'["{key}"] =' in pens, f"pen stocking preview lacks {key}"


def test_pen_verbs_reach_the_facade():
    pens = (BRIDGE_DIR / "GameBridge.Pens.cs").read_text(encoding="utf-8")
    assert "public Godot.Collections.Array<Godot.Collections.Dictionary> GetSitePens(string siteId)" in pens
    assert re.search(r"public Godot\.Collections\.Dictionary PreviewPenStocking\(string siteId, string penId, int count,", pens)
    assert re.search(r"public Godot\.Collections\.Dictionary StockSmoltsInPen\(string siteId, string penId, int count,", pens)
    assert "public bool DelousePens(string siteId, string method, Godot.Collections.Array<string> penIds)" in pens
    proxies = (SCRIPTS / "bridge" / "SimProxies.gd").read_text(encoding="utf-8")
    actions = (SCRIPTS / "bridge" / "SimActions.gd").read_text(encoding="utf-8")
    facade = (SCRIPTS / "SimBridge.gd").read_text(encoding="utf-8")
    assert "return _arr(game_bridge.GetSitePens(site_id))" in proxies
    assert "return game_bridge.PreviewPenStocking(site_id, pen_id, count, strain_id, quality_tier)" in proxies
    assert "game_bridge.StockSmoltsInPen(site_id, pen_id, count, strain_id, quality_tier)" in actions
    assert "game_bridge.DelousePens(site_id, method, pen_ids)" in actions
    assert "return _proxies.get_site_pens(site_id)" in facade
    assert "return _proxies.preview_pen_stocking(site_id, pen_id, count, strain_id, quality_tier)" in facade
    assert "return _actions.stock_smolts_in_pen(site_id, pen_id, count, strain_id, quality_tier)" in facade
    assert "return _actions.delouse_pens(site_id, method, pen_ids)" in facade


def test_every_pen_reason_and_pen_label_has_english_and_norwegian_text():
    views = (REPO / "src" / "LakskongenSim" / "Core" / "PenViews.cs").read_text(encoding="utf-8")
    reasons = re.findall(r'public const string \w+ = "([a-z_]+)";', views)
    assert len(reasons) == 5, f"harness broken: {reasons}"
    # The per-pen verb can also refuse with these engine keys (before the pen checks or for money).
    generic = ["insufficient_funds", "mab_exceeded", "invalid_count", "site_not_found"]
    keys = ["CAPACITY_REASON_" + r.upper() for r in reasons + generic]
    panel = (SCRIPTS / "ui" / "PenManagement.gd").read_text(encoding="utf-8")
    keys += sorted(set(re.findall(r'tr\("(PENS_[A-Z_]+)"\)', panel)))
    assert len(keys) >= 9 + 7, f"harness broken: {keys}"
    with (REPO / "godot" / "translations" / "translations.csv").open(encoding="utf-8", newline="") as f:
        rows = {r[0]: r for r in csv.reader(f) if len(r) == 3}
    missing = [k for k in keys if k not in rows or not rows[k][1].strip() or not rows[k][2].strip()]
    assert not missing, f"pen labels without en/no text: {missing}"
    for key in keys:
        en, nb = rows[key][1], rows[key][2]
        assert re.findall(r"%[sd]", en) == re.findall(r"%[sd]", nb), f"{key}: placeholders differ"
