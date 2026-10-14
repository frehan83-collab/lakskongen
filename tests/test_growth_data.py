"""S2-03 data guards: the growth calibration and the scenario starting stock.

The engine reads godot/data/calibration.json (embedded) and its built-in
scenario roster mirrors godot/data/scenarios.json; these checks keep the
shipped data on the sourced bands when the files are merged or edited by hand.
"""
import json
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "godot" / "data"


def _load(name: str) -> Any:
    return json.loads((DATA / name).read_text(encoding="utf-8"))


def test_growth_calibration_is_on_its_sourced_bands() -> None:
    growth = _load("calibration.json")["growth"]
    # Sea-phase TGC 2.8-3.2 (Reid et al. 2013a average 3.0; Karlsen et al. 2018 2.80-3.04).
    assert 2.8 <= growth["thermalGrowthCoefficient"] <= 3.2
    # Growth optimum 13-14 C (Kleiven et al. 2026); degree-days below it, so
    # growth at 4 C is at least 0.3 of the optimum (roadmap S2-03 acceptance 3).
    assert 12.5 <= growth["optimalTemperatureC"] <= 14.5
    assert 4.0 / growth["optimalTemperatureC"] >= 0.3
    # Growth stops near the incremental thermal maximum, ~22 C (Calado et al. 2021).
    assert 20.0 <= growth["upperGrowthLimitC"] <= 23.0
    # Fish grown on pass 6 kg: the size tail ends far above harvest size.
    assert growth["asymptoticWeightKg"] > 10.0


def test_every_scenario_farm_starts_with_its_last_release_window_at_sea() -> None:
    for scenario in _load("scenarios.json"):
        for site in scenario.get("starting_sites", []):
            area = site["production_area"]
            expected = 200 if area >= 8 else 110  # S1 mid-June north, S0 mid-September south
            assert site.get("days_at_sea") == expected, (scenario["id"], site["id"], site.get("days_at_sea"))
