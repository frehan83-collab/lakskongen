"""S2-02: the UI's calendar mirror agrees with the engine's one calendar.

godot/scripts/bridge/SimFormatters.gd formats sim days as dates; the engine's
calendar is src/LakskongenSim/Core/SimCalendar.cs. Both must start in the same
year and use the same month lengths and year length.
"""

from __future__ import annotations

import re
from pathlib import Path

REPO = Path(__file__).parent.parent
CS = (REPO / "src" / "LakskongenSim" / "Core" / "SimCalendar.cs").read_text(encoding="utf-8")
GD = (REPO / "godot" / "scripts" / "bridge" / "SimFormatters.gd").read_text(encoding="utf-8")


def _cs_const(name: str) -> int:
    return int(re.search(rf"public const int {name}\s*=\s*(\d+);", CS).group(1))


def _gd_const(name: str) -> int:
    return int(re.search(rf"const {name}\s*:=\s*(\d+)", GD).group(1))


def test_start_year_and_year_length_match():
    assert _cs_const("StartYear") == _gd_const("CALENDAR_START_YEAR") == 2027
    assert _cs_const("DaysPerYear") == _gd_const("DAYS_PER_YEAR") == 365
    assert _cs_const("MonthsPerYear") == _gd_const("MONTHS_PER_YEAR") == 12


def test_month_lengths_match():
    cs_months = [int(v) for v in re.search(r"MonthLengths\s*=\s*\{([^}]*)\}", CS).group(1).split(",")]
    gd_months = [int(v) for v in re.search(r"const _MONTH_DAYS\s*:=\s*\[([^\]]*)\]", GD).group(1).split(",")]
    assert cs_months == gd_months == [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
