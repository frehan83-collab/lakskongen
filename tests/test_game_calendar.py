"""Roadmap U2-07: the HUD's calendar ("Uke 14 · april · År 1").

GameCalendar.gd draws the sim calendar the engine counts in (SimCalendar,
roadmap S2-02): day 0 is Friday 1 January 2027, years have 365 days, months
their real lengths, the weekday moves one day a year, and weeks are ISO-8601
weeks (what Norwegian regulation and farmers count in). These tests re-run the
script's own week formula, from its own constants, against Python's ISO
calendar for six sim years, and pin that the script still holds that formula.
The runtime half (the HUD shows it, nb and en) is in godot/tests/UiFlowTests.gd.
"""

import csv
import datetime
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
CALENDAR = REPO / "godot" / "scripts" / "bridge" / "GameCalendar.gd"
FORMATTERS = REPO / "godot" / "scripts" / "bridge" / "SimFormatters.gd"
TRANSLATIONS = REPO / "godot" / "translations" / "translations.csv"

SIM_YEARS = 6


def _int_const(text: str, name: str) -> int:
    m = re.search(rf"^const {name} := (\d+)$", text, re.M)
    assert m, f"{name} not found"
    return int(m.group(1))


def _constants() -> tuple[int, int, int, int]:
    gd = CALENDAR.read_text(encoding="utf-8")
    fmt = FORMATTERS.read_text(encoding="utf-8")
    return (
        _int_const(gd, "DAY0_ISO_WEEKDAY"),
        _int_const(gd, "DAYS_PER_YEAR"),
        _int_const(gd, "DAYS_PER_WEEK"),
        _int_const(fmt, "CALENDAR_START_YEAR"),
    )


def _iso_weekday(day: int, day0: int, per_week: int) -> int:
    return (day0 - 1 + day) % per_week + 1


def _iso_week(day: int, day0: int, per_year: int, per_week: int) -> int:
    """GameCalendar.iso_week, line for line (Python's % and // floor like posmod/floori)."""
    def year_start(d: int) -> int:
        return (d // per_year) * per_year

    def weeks_in_year(d: int) -> int:
        return 53 if _iso_weekday(year_start(d), day0, per_week) == 4 else 52

    week = (day % per_year + 1 - _iso_weekday(day, day0, per_week) + 10) // per_week
    if week < 1:
        return weeks_in_year(year_start(day) - 1)
    if week > weeks_in_year(day):
        return 1
    return week


def _reference_year(jan1_iso_weekday: int) -> int:
    """A real year that starts on that weekday and, like every sim year, has
    365 days and follows a 365-day year (so its week 53 rule is the sim's)."""
    def leap(y: int) -> bool:
        return y % 4 == 0 and (y % 100 != 0 or y % 400 == 0)

    for y in range(1901, 2400):
        if not leap(y) and not leap(y - 1) and datetime.date(y, 1, 1).isoweekday() == jan1_iso_weekday:
            return y
    raise AssertionError(f"no reference year for weekday {jan1_iso_weekday}")


def test_the_calendar_starts_on_friday_1_january_2027_like_the_engine() -> None:
    day0, per_year, per_week, start_year = _constants()
    assert start_year == 2027
    assert per_year == 365 and per_week == 7
    assert datetime.date(start_year, 1, 1).isoweekday() == day0 == 5


def test_sim_year_one_is_2027_day_for_day() -> None:
    day0, per_year, per_week, start_year = _constants()
    for doy in range(per_year):
        real = datetime.date(start_year, 1, 1) + datetime.timedelta(days=doy)
        assert _iso_weekday(doy, day0, per_week) == real.isoweekday(), doy
        assert _iso_week(doy, day0, per_year, per_week) == real.isocalendar()[1], doy


def test_iso_weeks_match_pythons_calendar_for_six_sim_years() -> None:
    day0, per_year, per_week, _ = _constants()
    for year in range(SIM_YEARS):
        first = year * per_year
        ref = _reference_year(_iso_weekday(first, day0, per_week))
        for doy in range(per_year):
            real = datetime.date(ref, 1, 1) + datetime.timedelta(days=doy)
            assert _iso_week(first + doy, day0, per_year, per_week) == real.isocalendar()[1], (year, doy)


def test_anchor_days_the_hud_test_uses() -> None:
    day0, per_year, per_week, _ = _constants()
    weeks = {d: _iso_week(d, day0, per_year, per_week) for d in (0, 2, 3, 94, 364, 365, 367, 400)}
    # 1-3 Jan 2027 are week 53 (of 2026); Monday 5 April 2027 is week 14.
    assert weeks == {0: 53, 2: 53, 3: 1, 94: 14, 364: 52, 365: 52, 367: 1, 400: 5}


def test_the_script_holds_the_formula_these_tests_run() -> None:
    gd = CALENDAR.read_text(encoding="utf-8")
    assert "posmod(DAY0_ISO_WEEKDAY - 1 + day, DAYS_PER_WEEK) + 1" in gd
    assert "(day_of_year(day) + 1 - iso_weekday(day) + 10) / DAYS_PER_WEEK" in gd
    assert "return 53 if iso_weekday(year_start(day)) == THURSDAY else 52" in gd
    assert "return iso_weeks_in_year(year_start(day) - 1)" in gd


def test_the_hud_label_reads_uke_month_year() -> None:
    with TRANSLATIONS.open(encoding="utf-8", newline="") as f:
        rows = {r[0]: (r[1], r[2]) for r in csv.reader(f) if len(r) == 3}
    assert rows["UI_HUD_CALENDAR"] == ("Week %d · %s · Year %d", "Uke %d · %s · År %d")
    gd = CALENDAR.read_text(encoding="utf-8")
    assert '"januar", "februar", "mars", "april"' in gd
