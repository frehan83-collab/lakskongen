"""F2-04 acceptance 5: the UI holds no farm-type number of its own.

The farm-type catalogue (GameBridge.GetFarmTypeCatalog, Farm/FarmTypeCatalog.cs)
computes every card value from the registry's reference inputs in
godot/data/calibration/facilities_common.json. A number from that file, or a
card value derived from it, written as a literal into a UI script would drift
from the registry the day a value is re-sourced, so this test greps
godot/scripts/ui for them.

A number counts as a farm-type number when it is distinctive: an integer of
500 or more with at least two significant digits, or a non-integer with at
least three (small integers, round magnitudes such as 1e9 and two-digit
fractions such as 16, 0.5 or 1.2 are layout, formatting and tuning constants
everywhere).
"""

from __future__ import annotations

import json
import re
from decimal import Decimal
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
COMMON = REPO / "godot" / "data" / "calibration" / "facilities_common.json"
UI_DIR = REPO / "godot" / "scripts" / "ui"

NUMBER = re.compile(r"(?<![\w.#])(\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?)(?![\w.])")


def _leaves(node, out: list[float]) -> list[float]:
    if isinstance(node, dict):
        for value in node.values():
            _leaves(value, out)
    elif isinstance(node, list):
        for value in node:
            _leaves(value, out)
    elif isinstance(node, (int, float)) and not isinstance(node, bool):
        out.append(float(node))
    return out


def _derived(catalog: dict) -> list[float]:
    """The card values the catalogue computes (Farm/FarmTypeCatalog.cs), rounded as a UI would print them."""
    hog = catalog["hogTonnesPerMtbTonneYear"]
    licence = catalog["licenceSizeTonnesMtb"]
    fee = catalog["conversionFeeNokPerLicence"]
    open_pen, of1, havfarm = catalog["openPen"], catalog["of1"], catalog["havfarm"]
    ras, post, hfs = catalog["ras"], catalog["postSmolt"], catalog["hfs"]
    open_capacity = open_pen["localityMtbTonnes"] * hog
    of1_mtb = of1["licences"] * licence
    values = [
        open_capacity,
        (open_pen["siteCapexNok"] + open_pen["localityMtbTonnes"] * open_pen["licencePriceNokPerTonneMtb"]) / open_capacity,
        of1_mtb,
        of1_mtb * hog,
        (of1["unitCapexNok"] + of1["licences"] * fee) / (of1_mtb * hog),
        havfarm["mtbTonnes"] * hog,
        (havfarm["unitCapexNok"] + havfarm["licences"] * fee) / (havfarm["mtbTonnes"] * hog),
        post["moduleVolumeM3"] * post["productionIndexKgPerM3Year"] / 1000.0,
        post["moduleCapexNok"] / (post["moduleVolumeM3"] * post["productionIndexKgPerM3Year"] / 1000.0),
        post["constructionDays"] + post["biofilterMaturationDays"],
        ras["capexUsdPerTonne"] * ras["nokPerUsd"],
        ras["constructionDays"] + ras["biofilterMaturationDays"],
        of1["yardDays"] + of1["voyageDays"] + of1["mooringAndCertificationDays"],
        hfs["capexNokPerTonneHog"] * hfs["lotCapacityTonnesHog"],
        of1["unitCapexNok"] * of1["consortiumEquityShare"],
    ]
    return [round(v) for v in values] + values


def _distinctive(value: float) -> bool:
    digits = Decimal(repr(value)).normalize().as_tuple().digits
    if value == int(value):
        # 500 or more, and not a round power-of-ten multiple such as 1e9 or 50 000 000.
        return abs(value) >= 500 and len(digits) >= 2
    return len(digits) >= 3


def farm_type_numbers() -> set[Decimal]:
    catalog = json.loads(COMMON.read_text(encoding="utf-8"))["catalog"]
    numbers = _leaves(json.loads(COMMON.read_text(encoding="utf-8")), []) + _derived(catalog)
    return {Decimal(repr(float(v))).normalize() for v in numbers if _distinctive(float(v))}


def _strip_comments(text: str) -> str:
    out = []
    for line in text.splitlines():
        quote = None
        cut = len(line)
        for i, ch in enumerate(line):
            if quote:
                if ch == quote and line[i - 1] != "\\":
                    quote = None
            elif ch in "\"'":
                quote = ch
            elif ch == "#":
                cut = i
                break
        out.append(line[:cut])
    return "\n".join(out)


def literals_in(text: str) -> list[tuple[int, Decimal]]:
    """Every numeric literal of a GDScript text (comments dropped), with its line."""
    found = []
    for lineno, line in enumerate(_strip_comments(text).splitlines(), 1):
        for m in NUMBER.finditer(line):
            try:
                found.append((lineno, Decimal(m.group(1).replace("_", "")).normalize()))
            except ArithmeticError:
                continue
    return found


def offenders(text: str, numbers: set[Decimal]) -> list[str]:
    return [f"line {n}: {v}" for n, v in literals_in(text) if v in numbers]


def test_the_catalogue_numbers_are_many_and_distinctive():
    numbers = farm_type_numbers()
    assert len(numbers) >= 40, sorted(numbers)
    for expected in ("6240", "7488", "13804348", "877000000", "262000", "64.68"):
        assert Decimal(expected).normalize() in numbers, expected


def test_no_ui_script_writes_a_farm_type_number():
    numbers = farm_type_numbers()
    problems = []
    for path in sorted(UI_DIR.rglob("*.gd")):
        for problem in offenders(path.read_text(encoding="utf-8"), numbers):
            problems.append(f"{path.relative_to(REPO)} {problem}")
    assert not problems, "farm-type numbers written into UI scripts (read them from get_farm_type_catalog()):\n  " + "\n  ".join(problems)


def test_the_grep_catches_a_planted_number_and_ignores_comments_and_layout():
    numbers = farm_type_numbers()
    planted = 'var mtb := 6_240.0\nlabel.text = "%d" % 877000000\nvar pad := 16  # 7488 in a comment is fine\nvar s := "#7488"\n'
    found = offenders(planted, numbers)
    assert [f.split(":")[0] for f in found] == ["line 1", "line 2"], found
    assert offenders("var x := 0.5\nvar y := 1.2\nvar z := 16\n", numbers) == []
