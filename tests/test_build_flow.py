"""F2-31: the family-first build flow's text contract.

Every translation key the build dialog and its parts read exists in English
and Norwegian (the keys the scripts build from engine ids included: an axis,
a gate kind, a constraint, a sea phase, a refusal, an exposure class), the
F2-31 rows sit in translations.csv as one block sorted by key, and no new UI
text or script carries an emoji (D14: CI bans emoji).
"""

from __future__ import annotations

import csv
import io
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
CSV = REPO / "godot" / "translations" / "translations.csv"
FACILITIES = REPO / "godot" / "scripts" / "ui" / "facilities"
F231_SCRIPTS = [
    REPO / "godot" / "scripts" / "ui" / "expansion" / "BuildSiteDialog.gd",
    FACILITIES / "BuildFlowLink.gd",
    FACILITIES / "CashRunwayChart.gd",
    FACILITIES / "ComparisonBars.gd",
    FACILITIES / "DiagnosisStrip.gd",
    FACILITIES / "FacilityBuildPreview.gd",
    FACILITIES / "FarmFamilyCard.gd",
    FACILITIES / "FarmGhostLayout.gd",
    FACILITIES / "GateList.gd",
    FACILITIES / "PermitTimeline.gd",
    FACILITIES / "PlotLens.gd",
    REPO / "godot" / "scripts" / "environment" / "FarmGhost.gd",
    REPO / "godot" / "scripts" / "ui" / "RASPanel.gd",
    REPO / "godot" / "scripts" / "ui" / "OffshorePanel.gd",
]
F231_SCENES = [REPO / "godot" / "scenes" / "ui" / "FarmFamilyCard.tscn"]
## The files F2-31 wrote (the panels it only touched carry older emoji, U3-02's).
F231_NEW = [p for p in F231_SCRIPTS if p.name not in ("RASPanel.gd", "OffshorePanel.gd")] + F231_SCENES
EMOJI = re.compile("[\U0001F000-\U0001FAFF☀-➿️]")
LITERAL_KEY = re.compile(r'tr\("(BUILD_[A-Z0-9_]+)"\)')

CS = REPO / "src" / "LakskongenSim"


def _rows() -> list[list[str]]:
    return [r for r in csv.reader(io.StringIO(CSV.read_text(encoding="utf-8"))) if r]


def _texts() -> dict[str, tuple[str, str]]:
    rows = _rows()
    return {r[0]: (r[1], r[2]) for r in rows[1:] if len(r) >= 3}


def _cs_strings(path: Path, pattern: str) -> list[str]:
    return re.findall(pattern, path.read_text(encoding="utf-8"))


def _enum_names(path: Path, enum: str) -> list[str]:
    text = path.read_text(encoding="utf-8")
    body = re.search(r"enum\s+" + enum + r"\s*\{(.*?)\}", text, re.S)
    assert body, f"{enum} not found in {path}"
    cleaned = re.sub(r"///.*", "", body.group(1))
    return [m for m in re.findall(r"\b([A-Z]\w*)\b(?:\s*=\s*\d+)?\s*,?", cleaned)]


def expected_built_keys() -> set[str]:
    """Keys the F2-31 scripts build from engine ids (ids read from the C# source)."""
    axes = _cs_strings(CS / "Farm" / "FarmTypeCatalog.cs", r'public const string \w+ = "([a-z_]+)";')
    axes = [a for a in axes if a.endswith(("_year", "_hog", "survival", "_factor", "_per_kg", "_days"))]
    gates = _enum_names(CS / "Farm" / "FarmTypeCatalog.cs", "FarmTypeGateKind")
    constraints = _cs_strings(CS / "Core" / "SimulationEngine.BuildDiagnosis.cs", r'public const string \w+ = "([a-z]+)";')
    phases = _cs_strings(CS / "Farm" / "Offshore" / "OffshoreProfiles.cs", r'public const string \w+Phase = "([a-z_]+)";')
    refusals = _cs_strings(CS / "Core" / "SimulationEngine.Offshore.cs", r'public const string (?:NotUnlocked|PlotNotFound|NotAnOffshoreSlot|PlotTaken|ExposureExceedsDesign|InsufficientFunds|InvalidType|OutOfRange) = "([a-z_]+)";')
    exposures = _enum_names(CS / "Farm" / "SitePlot.cs", "ExposureClass")
    keys = set()
    keys |= {"BUILD_AXIS_" + a.upper() for a in axes}
    keys |= {"BUILD_GATE_" + g.upper() for g in gates}
    keys |= {"BUILD_GATE_WAIVED_ZONEDPAD", "BUILD_GATE_WAIVED_DEVELOPMENTLICENCE", "BUILD_GATE_FUNDS_HAV"}
    keys |= {"BUILD_DIAG_" + c.upper() for c in constraints}
    keys |= {"BUILD_DIAG_LIGHT_0", "BUILD_DIAG_LIGHT_1", "BUILD_DIAG_LIGHT_2", "BUILD_DIAG_RELIEF_CAPITAL"}
    keys |= {"BUILD_SEA_PHASE_" + p.upper() for p in phases}
    keys |= {"BUILD_SEA_REASON_" + r.upper() for r in refusals} | {"BUILD_SEA_REASON_ENGINE_NOT_READY"}
    keys |= {"BUILD_PLOT_EXPOSURE_" + e.upper() for e in exposures}
    for fam in ("SJO", "LAND", "HAV"):
        keys |= {"BUILD_FAMILY_PAYS_" + fam, "BUILD_FAMILY_LIMITS_" + fam}
    for fam in ("LAND", "HAV"):
        keys |= {f"BUILD_{k}_{fam}" for k in ("WATCH", "HINT", "SUBTITLE", "DONE_VIEW", "KPI_PAYBACK_NO_MARGIN")}
    keys |= {"BUILD_OPTION_HFS_PILOT_LOT", "BUILD_OPTION_HFS_LOT", "BUILD_CONTRACT_TIP_MANAGED", "BUILD_CONTRACT_TIP_TURNKEY",
             "BUILD_NOTE_NO_PUBLISHED_FIGURE", "BUILD_SOURCE_GB", "BUILD_SOURCE_CODE"}
    return keys


def test_the_engine_ids_are_read():
    keys = expected_built_keys()
    for key in ("BUILD_AXIS_CAPEX_NOK_PER_T_YEAR", "BUILD_GATE_ESCAPEFREEDAYS", "BUILD_DIAG_CAPITAL",
                "BUILD_SEA_PHASE_YARD_CONTRACT", "BUILD_SEA_REASON_PLOT_TAKEN", "BUILD_PLOT_EXPOSURE_OUTERCOAST"):
        assert key in keys, key
    assert len([k for k in keys if k.startswith("BUILD_AXIS_")]) == 7
    assert len([k for k in keys if k.startswith("BUILD_GATE_")]) == 9 + 3


def test_every_build_flow_key_has_english_and_norwegian_text():
    texts = _texts()
    used = set()
    for path in F231_SCRIPTS:
        used |= set(LITERAL_KEY.findall(path.read_text(encoding="utf-8")))
    used |= expected_built_keys()
    assert len(used) > 150, len(used)
    missing = sorted(k for k in used if k not in texts or not texts[k][0].strip() or not texts[k][1].strip())
    assert not missing, missing


def test_the_build_flow_rows_are_one_sorted_block():
    keys = [r[0] for r in _rows()[1:]]
    build = [i for i, k in enumerate(keys) if k.startswith("BUILD_")]
    assert build, "no BUILD_ rows"
    assert build == list(range(build[0], build[0] + len(build))), "the BUILD_ rows are not one block"
    block = [keys[i] for i in build]
    assert block == sorted(block)
    assert len(block) == len(set(block))


def test_no_emoji_in_the_build_flow():
    for path in F231_NEW:
        text = path.read_text(encoding="utf-8")
        assert not EMOJI.search(text), (path, EMOJI.findall(text))
    for key, (en, nb) in _texts().items():
        if key.startswith("BUILD_"):
            assert not EMOJI.search(en + nb), (key, en, nb)


def test_the_check_finds_a_missing_key_and_an_emoji():
    assert EMOJI.search("Bygg ✅")
    assert not EMOJI.search("● ○ ■ ▲ → ≈ × –")
    assert LITERAL_KEY.findall('x.text = tr("BUILD_NOPE_KEY")') == ["BUILD_NOPE_KEY"]
