"""Read the game's own production-area table (names, monthly sea temperature).

The map data takes the names and the sea-temperature climatology straight
from src/LakskongenSim/Environment/ProductionAreas.cs, so the sim and the map
can never disagree about an area's water.
"""

from __future__ import annotations

import re

from geo_common import REPO

PRODUCTION_AREAS_CS = REPO / "src" / "LakskongenSim" / "Environment" / "ProductionAreas.cs"


def production_area_table() -> dict[int, dict]:
    """{po: {"name": str, "sea_temp_monthly_c": [12 floats]}} for PO1..PO13."""
    text = PRODUCTION_AREAS_CS.read_text(encoding="utf-8")
    block = text.split("// BEGIN derive_po_sea_temperature.py", 1)[1].split("// END", 1)[0]
    temps = {
        int(m.group(1)): [float(v) for v in m.group(2).split(",")]
        for m in re.finditer(r"\[(\d+)\]\s*=\s*new\[\]\s*\{([^}]*)\}", block)
    }
    names_block = text.split("private static readonly string[] Names =", 1)[1].split("};", 1)[0]
    names = re.findall(r'"([^"]+)"', names_block)
    if len(names) != 13 or sorted(temps) != list(range(1, 14)):
        raise SystemExit("ProductionAreas.cs: could not read 13 names and 13 temperature rows")
    return {po: {"name": names[po - 1], "sea_temp_monthly_c": temps[po]} for po in range(1, 14)}
