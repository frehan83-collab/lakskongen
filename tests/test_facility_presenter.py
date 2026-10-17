"""F2-19 land pads and F2-20 facility presenter (farm-types spec §4, §5, §6.3, §7.2).

The committed measurement docs/run/evidence/f2_20_facilities_metrics.json is
what godot/scripts/qa/FacilityPresenterCapture.gd wrote for the shots of
godot/qa/shots.json that run in res://qa/scenes/FacilityPresenterCapture.tscn
(tools/capture_screenshots.sh --scene ...). It must hold every such shot and
meet the acceptance the frames can show:

- F2-19 accept 6: an unbuilt pad (heath) and a built pad (blasted granite)
  differ by ΔE76 >= 10 in their mean colour, at every framing measured;
- F2-20 accept 1: in the QA set (one HFS lot, one post-smolt site, one
  OF1-class unit) the presenter draws the engine's tanks and units;
- F2-20 accept 2: shot 32_land_construction differs by pHash Δ >= 9 between
  phases 2 and 4, and each 32_ shot stands in its own phase;
- F2-20 accept 4: against the home view (the P0-04 view) a full 4-lot campus
  adds <= 20 opaque + <= 16 shadow draws and one OF1-class unit with its
  vessel <= 8 + 6; a full campus is <= 250 k LOD0 triangles; LOD2 casts no
  shadow and of LOD1 only the tanks do.

The UI-input half of the acceptance (the click, the card, the camera,
dropping a unit) is UiFlowTests._test_facilities_render; the frame-time
half is the P0-04 run in docs/run/evidence/f2_20_presenter.md.
"""

import json
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
SHOTS = json.loads((REPO / "godot" / "qa" / "shots.json").read_text())["shots"]
SCENE = "res://qa/scenes/FacilityPresenterCapture.tscn"
METRICS_PATH = REPO / "docs" / "run" / "evidence" / "f2_20_facilities_metrics.json"
OWN = [s for s in SHOTS if s["scene"] == SCENE]
LAND_PHASES = ["blast", "foundations", "tanks", "hall", "intake", "commissioning"]


@pytest.fixture(scope="module")
def metrics():
    return json.loads(METRICS_PATH.read_text())


def test_every_presenter_shot_was_measured_at_1080p(metrics):
    assert metrics["items"] == ["F2-19", "F2-20"] and metrics["resolution"] == [1920, 1080]
    assert OWN, "no FacilityPresenterCapture shots in shots.json"
    for s in OWN:
        assert s["id"] in metrics["shots"], f"{s['id']} is in shots.json but was not captured"


def test_a_built_pad_differs_from_heath_by_delta_e_ten(metrics):
    pairs = [s["id"] for s in OWN if s["id"].endswith("_granite")]
    assert len(pairs) == 4
    for sid in pairs:
        delta = metrics["shots"][sid]["delta_e_vs_heath"]
        assert delta, sid
        for pad, de in delta.items():
            assert de >= 10.0, (sid, pad, de)


def test_the_presenter_draws_the_engines_tanks_and_units(metrics):
    built = [s for s in OWN if s.get("facilities") and all(v == 1.0 for v in s["construction"].values())]
    assert built
    for s in built:
        units = metrics["shots"][s["id"]]["units"]
        variants = sorted(u["variant"] for u in units.values())
        assert variants == ["hfs", "of1", "post_smolt"], (s["id"], variants)
        for uid, u in units.items():
            if u["variant"] == "of1":
                assert u["unit_visible"] and "offshore_service" in u["fleet"], (s["id"], uid)
            else:
                assert u["engine_tanks"] > 0 and u["drawn_tanks"] == u["engine_tanks"], (s["id"], uid, u)
        lots = next(u for u in units.values() if u["variant"] == "hfs")["lots"]
        assert lots == s.get("land_lots", 1), s["id"]


def test_each_construction_shot_stands_in_its_own_phase(metrics):
    for k, phase in enumerate(LAND_PHASES):
        units = metrics["shots"][f"32_land_construction_p{k}"]["units"]
        land = next(u for u in units.values() if u["variant"] == "hfs")
        assert land["phase_key"] == phase and land["phase_index"] == k, (k, land)


def test_phases_two_and_four_differ_by_phash_nine(metrics):
    p4 = metrics["shots"]["32_land_construction_p4"]
    assert p4["phash_distance_vs_32_land_construction_p2"] >= 9


def test_the_draw_call_budgets_at_the_home_view(metrics):
    campus = metrics["shots"]["44_facilities_budget_campus_home"]["perf"]
    assert campus["units"] == ["land"]
    assert campus["added_opaque_draws"] <= 20 and campus["added_shadow_draws"] <= 16, campus
    of1 = metrics["shots"]["44_facilities_budget_of1_home"]["perf"]
    assert of1["units"] == ["offshore"]
    assert of1["added_opaque_draws"] <= 8 and of1["added_shadow_draws"] <= 6, of1


def test_a_full_campus_stays_under_250k_lod0_triangles(metrics):
    units = metrics["shots"]["44_facilities_campus_full"]["units"]
    land = next(u for u in units.values() if u["variant"] == "hfs")
    assert land["lots"] == 4 and land["drawn_tanks"] == 4 * 14
    assert 0 < land["lod0_triangles"] <= 250_000, land["lod0_triangles"]


def test_the_shadow_rule_holds_for_every_unit(metrics):
    for sid, rec in metrics["shots"].items():
        for uid, u in rec.get("units", {}).items():
            if "shadow_rule_ok" in u:
                assert u["shadow_rule_ok"], (sid, uid)
