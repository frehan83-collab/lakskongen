#!/usr/bin/env python3
"""Derive the monthly sea-temperature climatology of the 13 production areas.

Roadmap S2-02 (D18: offline data only). The engine embeds the table this
script prints (src/LakskongenSim/Environment/ProductionAreas.cs); the pytest
tests/test_po_sea_temperature.py fails when the two drift apart.

Method, per production area (PO):

1. Level: Havforskningsinstituttet's normal winter and summer temperature of
   the area's upper water masses (NorKyst v3, mean 2012-2024, area mean inside
   the outermost islands): Albretsen et al., "Fysisk oseanografiske forhold i
   produksjonsomraadene for akvakultur", Rapport fra havforskningen 2025-69
   (published 2025-12-02), section 4,
   https://www.hi.no/hi/nettrapporter/rapport-fra-havforskningen-2025-69 .
   The report gives ranges ("15-16 C om sommeren"); the midpoint is used. The
   winter normal becomes the coldest month, the summer normal the warmest.
2. Shape: the month-by-month seasonal cycle of one or two representative
   coastal stations in the area (10-year means, satellite plus in-situ,
   https://www.seatemperature.info/<station>-water-temperature.html, read
   2026-10-14). The station curve is rescaled linearly so its coldest month
   equals the HI winter normal and its warmest month the HI summer normal, so
   the timing comes from the station and the level from HI.

The depth is the cage depth farms report at: luseforskriften (FOR-2012-12-05-
1140) section 6 orders the sea temperature measured at 3 m every week.

Run: python3 tools/derive_po_sea_temperature.py            (prints the table)
     python3 tools/derive_po_sea_temperature.py --csharp   (C# initializer rows)
"""

from __future__ import annotations

import sys

# HI 2025-69 section 4, normal (2012-2024) winter and summer range per PO, degC.
HI_NORMALS = {
    1: ((3.0, 4.0), (16.0, 18.0)),   # Svenskegrensen til Jaeren
    2: ((4.0, 5.0), (15.0, 16.0)),   # Ryfylke
    3: ((5.0, 6.0), (15.0, 16.0)),   # Karmoey til Sotra
    4: ((5.0, 6.0), (15.0, 16.0)),   # Nordhordland til Stadt
    5: ((5.0, 6.0), (15.0, 15.0)),   # Stadt til Hustadvika ("rundt 15")
    6: ((6.0, 6.0), (14.0, 15.0)),   # Nordmoere og Soer-Troendelag ("rundt 6")
    7: ((5.0, 6.0), (13.0, 14.0)),   # Nord-Troendelag med Bindal
    8: ((5.0, 5.0), (13.0, 14.0)),   # Helgeland til Bodoe
    9: ((5.0, 5.0), (12.0, 13.0)),   # Vestfjorden og Vesteraalen
    10: ((4.0, 5.0), (12.0, 14.0)),  # Andoeya til Senja
    11: ((4.0, 4.0), (11.0, 12.0)),  # Kvaloeya til Loppa
    12: ((4.0, 4.0), (11.0, 12.0)),  # Vest-Finnmark
    13: ((4.0, 4.0), (10.0, 12.0)),  # Oest-Finnmark
}

# seatemperature.info monthly means (Jan..Dec, degC), read 2026-10-14.
STATIONS = {
    "kristiansand": [5.6, 4.3, 4.2, 6.3, 10.2, 14.4, 16.9, 17.4, 15.7, 12.6, 9.9, 7.3],
    "stavanger": [6.1, 4.9, 4.7, 6.3, 9.1, 12.4, 15.0, 16.3, 15.1, 12.5, 10.2, 7.7],
    "haugesund": [6.0, 4.8, 4.6, 6.2, 9.0, 12.1, 14.8, 16.0, 14.8, 12.3, 9.9, 7.6],
    "bergen": [6.6, 5.5, 5.2, 6.3, 8.8, 11.9, 14.7, 15.7, 14.6, 12.3, 10.1, 8.1],
    "floro": [6.2, 5.0, 4.9, 6.2, 8.9, 12.1, 15.0, 15.2, 13.6, 11.2, 9.1, 7.4],
    "alesund": [6.3, 5.3, 5.0, 6.1, 8.5, 11.4, 13.9, 14.9, 13.7, 11.4, 9.3, 7.6],
    "kristiansund": [6.3, 5.3, 5.1, 6.0, 8.6, 11.6, 13.9, 14.8, 13.4, 11.2, 9.1, 7.3],
    "rorvik": [5.5, 4.6, 4.4, 5.4, 8.0, 11.3, 13.7, 14.2, 12.5, 10.2, 8.1, 6.5],
    "sandnessjoen": [4.9, 4.2, 4.3, 4.8, 7.0, 10.3, 13.0, 13.3, 11.7, 9.7, 7.8, 6.2],
    "bodo": [5.3, 4.3, 4.1, 4.7, 6.6, 9.5, 12.2, 13.0, 11.6, 9.9, 8.1, 6.5],
    "svolvaer": [4.8, 3.6, 3.2, 3.7, 6.0, 9.7, 12.8, 13.5, 11.7, 9.7, 7.9, 6.3],
    "harstad": [4.9, 3.3, 3.0, 3.9, 5.8, 9.2, 11.9, 12.8, 10.7, 8.9, 7.5, 6.1],
    "tromso": [5.1, 4.0, 3.6, 4.0, 5.7, 8.5, 11.0, 11.9, 10.6, 8.9, 7.6, 6.4],
    "hammerfest": [4.8, 3.9, 3.5, 3.5, 4.4, 7.0, 9.2, 10.0, 9.2, 8.0, 7.0, 5.7],
    "vadso": [3.6, 2.4, 2.1, 2.4, 4.4, 7.5, 10.9, 11.4, 9.4, 7.5, 6.1, 4.6],
    "kirkenes": [3.4, 2.4, 2.0, 2.0, 4.1, 7.6, 10.9, 11.4, 9.4, 7.5, 6.0, 4.5],
}

# Representative station(s) per PO (shape only; averaged month by month).
SHAPE_STATIONS = {
    1: ["kristiansand"],
    2: ["stavanger"],
    3: ["haugesund", "bergen"],
    4: ["floro"],
    5: ["alesund"],
    6: ["kristiansund"],
    7: ["rorvik"],
    8: ["sandnessjoen", "bodo"],
    9: ["svolvaer"],
    10: ["harstad"],
    11: ["tromso"],
    12: ["hammerfest"],
    13: ["vadso", "kirkenes"],
}

# The national curve the engine used before S2-02 (WeatherSystem.MonthlyMeanTemp).
LEGACY_NATIONAL = [4.5, 3.8, 4.2, 5.5, 8.0, 10.5, 12.5, 13.5, 12.0, 9.5, 7.0, 5.5]


def midpoint(rng: tuple[float, float]) -> float:
    return (rng[0] + rng[1]) / 2.0


def shape(po: int) -> list[float]:
    rows = [STATIONS[name] for name in SHAPE_STATIONS[po]]
    return [sum(r[m] for r in rows) / len(rows) for m in range(12)]


def climatology(po: int) -> list[float]:
    winter, summer = (midpoint(r) for r in HI_NORMALS[po])
    s = shape(po)
    lo, hi = min(s), max(s)
    return [round(winter + (summer - winter) * (v - lo) / (hi - lo), 2) for v in s]


def table() -> dict[int, list[float]]:
    return {po: climatology(po) for po in range(1, 14)}


def rms(a: list[float], b: list[float]) -> float:
    return (sum((x - y) ** 2 for x, y in zip(a, b)) / len(a)) ** 0.5


def main(argv: list[str]) -> int:
    t = table()
    if "--csharp" in argv:
        for po, row in t.items():
            vals = ", ".join(f"{v:.2f}" for v in row)
            print(f"            [{po}] = new[] {{ {vals} }},")
        return 0
    print("PO   Jan   Feb   Mar   Apr   May   Jun   Jul   Aug   Sep   Oct   Nov   Dec  mean  rms(legacy)")
    for po, row in t.items():
        mean = sum(row) / 12.0
        print(f"{po:>2} " + " ".join(f"{v:5.2f}" for v in row) + f" {mean:5.2f} {rms(row, LEGACY_NATIONAL):6.2f}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
