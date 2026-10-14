"""U1-05 guards: no fake charts and no constants shown as state.

The dashboard, finance, market, crew and pen panels used to draw static
mock-ups (six hard-coded month bars, twelve week bars at full height, a
"6 months" chart on day 12), derive "data" from the spot price with fixed
factors (low = spot x 0.83, contract = weekly x 0.93, regional prices and
demand), show invented skill percentages, and placeholder cards for pens
that did not exist. The UI flow tests check the live values; these static
checks keep the fakes from creeping back. Pure text scans, no engine.
"""

from __future__ import annotations

import re
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
GODOT = REPO / "godot"
UI = GODOT / "scripts" / "ui"
SCENES = GODOT / "scenes" / "ui"

# The panels this item made truthful (roadmap U1-05 file list).
PANELS = ["Dashboard", "FinancePanel", "ContractsPanel", "CrewPanel", "PenManagement"]


def _code(path: Path) -> str:
    """The script without comments (a comment may quote an old fake)."""
    lines = []
    for line in path.read_text(encoding="utf-8").splitlines():
        lines.append(re.sub(r'#(?=(?:[^"]*"[^"]*")*[^"]*$).*', "", line))
    return "\n".join(lines)


def test_no_literal_numbers_assigned_to_value_labels():
    """A label's text is never a number typed into the script ("8.5 m", "A+")."""
    literal = re.compile(r'\.text\s*=\s*"([^"%]*\d[^"]*|A\+|Lav|Middels|Høy|Frisk)"')
    offenders = []
    for name in PANELS:
        for n, line in enumerate(_code(UI / f"{name}.gd").splitlines(), 1):
            if literal.search(line):
                offenders.append(f"{name}.gd:{n}: {line.strip()}")
    assert offenders == []


def test_no_values_derived_from_the_spot_price_with_fixed_factors():
    code = _code(UI / "ContractsPanel.gd")
    assert not re.search(r"\b(spot|weekly)\s*\*\s*\d", code), "market numbers invented from the spot price"
    assert not re.search(r"[€£¥$]%s", code), "regional prices in foreign currency are invented"
    assert "fish_biomass_kg / 1000.0 * 0.2" not in code


def test_crew_shows_the_sims_roles_not_invented_skills():
    code = _code(UI / "CrewPanel.gd")
    assert not re.search(r'"level"\s*:\s*\d', code), "hard-coded skill levels"
    assert "60 + 12" not in code, "the invented efficiency formula"
    assert "get_crew_role_effects" in code


def test_dashboard_revenue_card_draws_revenue_not_cash():
    code = _code(UI / "Dashboard.gd")
    spec = re.search(r'\["revenue", _kpi_revenue_value, "(\w+)"', code)
    assert spec and spec.group(1) == "revenue_nok", spec
    assert 'get_analytics_trend("balance_nok")' not in code
    assert '"revenue_nok"' in code and '"expenses_nok"' in code


def test_no_static_chart_mockups_in_the_scenes():
    dashboard = (SCENES / "Dashboard.tscn").read_text(encoding="utf-8")
    finance = (SCENES / "FinancePanel.tscn").read_text(encoding="utf-8")
    contracts = (SCENES / "ContractsPanel.tscn").read_text(encoding="utf-8")
    # Hand-placed bars with fixed heights and fixed month or week names.
    for text, scene in ((dashboard, "Dashboard"), (finance, "FinancePanel")):
        assert not re.search(r'name="(FinRev|FinCost|ChartRev|ChartCost)\d"', text), scene
        assert not re.search(r'text = "(Okt|Nov|Des|Jan|Feb|Mar)"', text), scene
    assert not re.search(r'name="PriceBar\d+"', contracts)
    assert not re.search(r'text = "U\d+"', contracts)
    assert "RegionsCard" not in contracts and "BtnSellSpot" not in contracts and "BtnNewContract" not in contracts
    # Six placeholder pen cards ("Merd 3".."Merd 6" read "Tom" on a two-site farm).
    assert not re.search(r'name="PenCard\d"', dashboard)
    assert 'text = "FISKEHELSE"' not in dashboard, "the card shows the sustainability score"


def test_charts_read_the_sims_series():
    period = _code(UI / "PeriodBarChart.gd")
    assert "static func periods(" in period and "static func daily_amounts(" in period
    finance = _code(UI / "FinancePanel.gd")
    assert 'get_analytics_data("revenue_nok"' in finance and 'get_analytics_data("expenses_nok"' in finance
    assert "get_profit_loss_story(99)" in finance, "the income statement lists the whole ledger"
    contracts = _code(UI / "ContractsPanel.gd")
    assert 'get_analytics_data("market_price_nok"' in contracts


def test_snapshots_record_revenue_and_costs():
    tracker = (REPO / "src" / "LakskongenSim" / "Core" / "AnalyticsTracker.cs").read_text(encoding="utf-8")
    assert 'Revenue = "revenue_nok"' in tracker and 'Expenses = "expenses_nok"' in tracker
    assert "RevenueNok = (double)(engine.FinancialStatements?.Revenue" in tracker
