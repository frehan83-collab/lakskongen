"""S2-04 harvest pipeline: the shipped calibration matches its sources.

The engine reads godot/data/calibration.json (embedded at build time); these
checks pin the harvest section to the sources named in CalibrationRegistry.cs
(region "S2-04 harvest pipeline") and docs/BALANCE.md §1h.
"""

import json
from pathlib import Path

REPO = Path(__file__).parent.parent
CALIBRATION = REPO / "godot" / "data" / "calibration.json"

LADDER = [
    "priceFactorUnder1Kg",
    "priceFactor1To2Kg",
    "priceFactor2To3Kg",
    "priceFactor3To4Kg",
    "priceFactor4To5Kg",
    "priceFactor5To6Kg",
    "priceFactor6To7Kg",
    "priceFactorOver7Kg",
]


def _harvest() -> dict:
    data = json.loads(CALIBRATION.read_text(encoding="utf-8"))
    assert "harvest" in data, "calibration.json has no harvest section (S2-04)"
    return data["harvest"]


def test_hog_yield_and_harvest_cost_match_their_sources() -> None:
    h = _harvest()
    # Mowi Salmon Farming Industry Handbook 2025, p. 114: live 100% -> HOG ~84%.
    assert h["hogYieldFraction"] == 0.84
    # Fiskeridirektoratet 2023 "slakting og transport" 4.45 NOK/kg round weight,
    # x 1.125 gutting loss (Fiskehelserapporten 2024, table 3.2.1) = 5.0 NOK/kg HOG.
    per_kg_hog = h["wellboatCostPerKgHogNok"] + h["slaughterCostPerKgHogNok"]
    assert abs(per_kg_hog - 4.45 * 1.125) / (4.45 * 1.125) < 0.01


def test_the_size_ladder_rises_with_gutted_weight_and_is_anchored_at_4_to_5_kg() -> None:
    h = _harvest()
    factors = [h[name] for name in LADDER]
    assert factors == sorted(factors), f"ladder not rising: {factors}"
    assert len(set(factors[:-1])) == len(factors) - 1
    assert h["priceFactor4To5Kg"] == 1.0
    # Kontali relative prices 2015-2024 (Mowi Handbook 2025, §6.7).
    assert 0.84 <= h["priceFactor2To3Kg"] <= 0.88
    assert 1.06 <= h["priceFactor6To7Kg"] <= 1.10
    # Roadmap S2-04 acceptance 2: a 2 kg live fish (1.68 kg HOG, the 1-2 kg
    # class) earns at least 25% less per kilo than a 5 kg fish (4.2 kg HOG).
    assert h["priceFactor1To2Kg"] <= 0.75 * h["priceFactor4To5Kg"]


def test_grades_follow_norway_2024() -> None:
    h = _harvest()
    # Fiskehelserapporten 2024, table 5.8.1: ordinary 20,976 of 1,408,650 t.
    assert abs(h["ordinaryShare"] - 20976 / 1408650) < 0.001
    # Mowi Handbook 2025, p. 116: downgraded fish ~30% below superior.
    assert h["productionPriceFactor"] == 0.70
    assert h["productionPriceFactor"] < h["ordinaryPriceFactor"] < 1.0
