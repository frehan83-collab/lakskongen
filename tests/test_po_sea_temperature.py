"""S2-02: the embedded production-area sea temperatures match their derivation.

src/LakskongenSim/Environment/ProductionAreas.cs embeds the monthly 3 m
climatology of the 13 production areas; tools/derive_po_sea_temperature.py
derives it from Havforskningsinstituttet's normals (HI 2025-69) and station
shapes. A hand edit of either side fails here.
"""

from __future__ import annotations

import importlib.util
import json
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
CS = REPO / "src" / "LakskongenSim" / "Environment" / "ProductionAreas.cs"
SCRIPT = REPO / "tools" / "derive_po_sea_temperature.py"
SCENARIOS = REPO / "godot" / "data" / "scenarios.json"


def _derivation():
    spec = importlib.util.spec_from_file_location("derive_po_sea_temperature", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def _embedded_table() -> dict[int, list[float]]:
    src = CS.read_text(encoding="utf-8")
    block = src.split("// BEGIN derive_po_sea_temperature.py", 1)[1].split("// END derive_po_sea_temperature.py", 1)[0]
    rows = {}
    for po, values in re.findall(r"\[(\d+)\]\s*=\s*new\[\]\s*\{([^}]*)\}", block):
        rows[int(po)] = [float(v) for v in values.split(",")]
    return rows


def test_embedded_climatology_matches_the_derivation():
    derived = _derivation().table()
    embedded = _embedded_table()
    assert sorted(embedded) == list(range(1, 14))
    for po in range(1, 14):
        assert embedded[po] == derived[po], f"PO{po}: embedded {embedded[po]} vs derived {derived[po]}"


def test_each_area_spans_hi_normal_winter_to_summer():
    module = _derivation()
    for po, row in module.table().items():
        (w_lo, w_hi), (s_lo, s_hi) = module.HI_NORMALS[po]
        assert min(row) == round((w_lo + w_hi) / 2.0, 2)
        assert max(row) == round((s_lo + s_hi) / 2.0, 2)
        assert len(row) == 12


def test_every_scenario_site_names_a_production_area():
    scenarios = json.loads(SCENARIOS.read_text(encoding="utf-8"))
    sites = [site for sc in scenarios for site in sc.get("starting_sites", [])]
    assert len(sites) >= 16
    for site in sites:
        assert 1 <= site.get("production_area", 0) <= 13, site["id"]
