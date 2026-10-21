"""Geo stages 2-3, track B: the open-pen candidates' contract.

The candidate reason keys (Core/SeaCandidateViews.cs) all have Norwegian and
English text; GameBridge.SeaCandidates.cs projects the candidates, the plan,
the build and the site locations with the keys the UI and the map read; the
facade forwards them from one sim·candidates lane block and SimActions owns
the verb; every score term, binding and planning stage the data can name has
its text; the expansion UI never hard-codes a candidate reason and never
reads a message; ExpansionReasons keeps its 11 keys. Pure static analysis.
"""

from __future__ import annotations

import csv
import json
import re
from pathlib import Path

REPO = Path(__file__).parent.parent
VIEWS = REPO / "src" / "LakskongenSim" / "Core" / "SeaCandidateViews.cs"
ENGINE = REPO / "src" / "LakskongenSim" / "Core" / "SimulationEngine.SeaCandidates.cs"
SEA_SITE = REPO / "src" / "LakskongenSim" / "Farm" / "SeaSite.cs"
EXPANSION_VIEWS = REPO / "src" / "LakskongenSim" / "Core" / "ExpansionViews.cs"
SCANNER = REPO / "src" / "LakskongenSim.Tests" / "Core" / "DeterminismScanner.cs"
PARTIAL = REPO / "godot" / "scripts" / "autoload" / "GameBridge.SeaCandidates.cs"
PROXIES = REPO / "godot" / "scripts" / "bridge" / "SimProxies.gd"
ACTIONS = REPO / "godot" / "scripts" / "bridge" / "SimActions.gd"
FACADE = REPO / "godot" / "scripts" / "SimBridge.gd"
TRANSLATIONS = REPO / "godot" / "translations" / "translations.csv"
UI = REPO / "godot" / "scripts" / "ui" / "expansion"
NEW_UI = [UI / "SeaCandidateList.gd", UI / "CandidateWhyHere.gd"]
PLOT_LENS = REPO / "godot" / "scripts" / "ui" / "facilities" / "PlotLens.gd"
GEO = REPO / "godot" / "data" / "geo"
EMOJI = re.compile("[\U0001F000-\U0001FAFF☀-➿️]")
ANCHOR = "CAPACITY_REASON_SITE_NOT_FOUND"


def _rows() -> dict[str, list[str]]:
    with TRANSLATIONS.open(encoding="utf-8", newline="") as f:
        return {r[0]: r for r in csv.reader(f) if len(r) == 3}


def _keys_in_order() -> list[str]:
    with TRANSLATIONS.open(encoding="utf-8", newline="") as f:
        return [r[0] for r in csv.reader(f) if r][1:]


def _consts(path: Path, cls: str) -> list[str]:
    block = path.read_text(encoding="utf-8").split(f"public static class {cls}", 1)[1].split("\n}\n", 1)[0]
    return [v for v in re.findall(r'public const string \w+ = "([a-z_]*)";', block) if v]


def _has_text(rows: dict[str, list[str]], key: str) -> bool:
    return key in rows and rows[key][1].strip() != "" and rows[key][2].strip() != ""


def test_every_candidate_reason_has_english_and_norwegian_text() -> None:
    reasons = _consts(VIEWS, "SeaCandidateReasons")
    assert reasons == ["engine_not_ready", "candidate_not_found", "candidate_wrong_area", "candidate_taken",
                       "candidate_not_suitable"], reasons
    rows = _rows()
    missing = [r for r in reasons if not _has_text(rows, "SEA_CANDIDATE_REASON_" + r.upper())]
    assert not missing, f"candidate reasons without en/no text: {missing}"


def test_the_expansion_reasons_stay_eleven() -> None:
    assert len(_consts(EXPANSION_VIEWS, "ExpansionReasons")) == 11


def test_the_candidate_checks_run_in_the_listed_order() -> None:
    src = ENGINE.read_text(encoding="utf-8")
    body = src.split("internal ExpansionVerdict CandidateCheck(string zoneId, string candidateId, Func<GeoCandidate, bool> isSuitable)", 1)[1]
    body = body.split("return ExpansionVerdict.Yes;", 1)[0]
    order = re.findall(r"SeaCandidateReasons\.(\w+)", body)
    assert order == ["EngineNotReady", "CandidateNotFound", "CandidateWrongArea", "CandidateTaken", "CandidateNotSuitable"], order
    # The build runs the checks, then U2-01's unchanged build, then records the candidate.
    build = src.split("public CommandResult BuildSeaSiteAt(", 1)[1].split("\n    }\n", 1)[0]
    assert build.index("CandidateCheck(") < build.index("BuildSeaSite(zoneId, name)") < build.index("site.CandidateId = candidateId")


def test_the_candidate_on_a_site_is_written_only_when_set() -> None:
    src = SEA_SITE.read_text(encoding="utf-8")
    assert re.search(r"\[JsonIgnore\(Condition = JsonIgnoreCondition\.WhenWritingNull\)\]\s*public string\? CandidateId \{ get; set; \}", src)


def test_the_bridge_projects_candidates_plan_build_and_locations() -> None:
    src = PARTIAL.read_text(encoding="utf-8")
    for sig in ("public Godot.Collections.Array<Godot.Collections.Dictionary> GetSeaCandidates(int productionArea)",
                "public Godot.Collections.Dictionary PlanSiteBuildAt(string zoneId, string candidateId)",
                "public Godot.Collections.Dictionary BuildExpansionSiteAt(string zoneId, string candidateId)",
                "public Godot.Collections.Array<Godot.Collections.Dictionary> GetPlayerSiteLocations()"):
        assert sig in src, sig
    for key in ("id", "po", "rank", "label", "near", "score", "components", "weights", "component_costs", "binding",
                "river_factor", "depth_m", "openness", "nearest_farm_km", "nearest_river_km", "ocean", "x", "y", "lon",
                "lat", "taken_by_site_id", "suggested", "build_zone_id", "can_build", "build_reason_key",
                "build_label_key", "candidate", "candidate_reason_key", "candidate_label_key", "candidate_id",
                "site_id", "site_label", "source", "current_speed_ms", "hs_m", "o2_mmol_m3"):
        assert f'["{key}"]' in src, f"candidates bridge lacks key {key}"
    # The build reports like BuildExpansionSite: the same signals on success.
    build = src.split("BuildExpansionSiteAt(string zoneId, string candidateId)", 1)[1].split("\n    }\n", 1)[0]
    assert 'EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, true)' in build
    assert "EmitSignal(SignalName.GameStateUpdated)" in build


def _block(path: Path, name: str) -> list[str]:
    text = path.read_text(encoding="utf-8")
    begins = [m.start() for m in re.finditer(rf"^# ═══ BEGIN {name} lane block", text, re.M)]
    assert len(begins) == 1, f"{path.name}: {len(begins)} '{name}' lane blocks"
    body = text[begins[0]:].split(f"# ═══ END {name} lane block", 1)[0]
    return body.splitlines()


def test_the_facade_forwards_the_candidates_lane_from_one_block() -> None:
    proxies = "\n".join(_block(PROXIES, "sim·candidates"))
    facade = "\n".join(_block(FACADE, "sim·candidates"))
    for name, csharp, args, ret in (("get_sea_candidates", "GetSeaCandidates", "area", r"Array\[Dictionary\]"),
                                    ("plan_site_build_at", "PlanSiteBuildAt", "zone_id, candidate_id", "Dictionary"),
                                    ("get_player_site_locations", "GetPlayerSiteLocations", "", r"Array\[Dictionary\]")):
        assert re.search(rf"func {name}\(.*\) -> {ret}: return (?:_arr\()?game_bridge\.{csharp}\({args}\)", proxies), name
        assert re.search(rf"func {name}\(.*\) -> {ret}: return _proxies\.{name}\({args}\)", facade), name
    assert re.search(r"func build_expansion_site_at\(zone_id: String, candidate_id: String\) -> Dictionary: "
                     r"return _actions\.build_expansion_site_at\(zone_id, candidate_id\)", facade)
    # The block comes after the sea lane's (and, once merged, after track A's sim·geo block).
    for path in (PROXIES, FACADE):
        text = path.read_text(encoding="utf-8")
        assert text.index("END sim·sea lane block") < text.index("BEGIN sim·candidates lane block")


def test_the_verb_lives_in_sim_actions_after_u2_01s() -> None:
    src = ACTIONS.read_text(encoding="utf-8")
    assert src.count("func build_expansion_site_at(") == 1
    assert "game_bridge.BuildExpansionSiteAt(zone_id, candidate_id)" in src
    assert src.index("func build_expansion_site(") < src.index("func build_expansion_site_at(") < src.index("func open_licence_auction(")


def test_the_determinism_scan_covers_the_partial() -> None:
    assert '"godot/scripts/autoload/GameBridge.SeaCandidates.cs"' in SCANNER.read_text(encoding="utf-8")


def test_every_score_term_binding_and_stage_has_its_text() -> None:
    rules = json.loads((GEO / "suitability_rules.json").read_text(encoding="utf-8"))
    candidates = json.loads((GEO / "candidates.json").read_text(encoding="utf-8"))["sites"]
    components = sorted(rules["effective_weights"]) + ["river"]
    assert len(components) == 8, components
    terms = components + ["stage", "none"]
    assert {c["binding"] for c in candidates} <= set(terms), {c["binding"] for c in candidates}
    stages = sorted(rules["offshore"]["stage_bonus"])
    assert {c["stage"] for c in candidates if c["type"] == "offshore"} <= set(stages)
    rows = _rows()
    wanted = ["SEA_CANDIDATE_COMPONENT_" + c.upper() for c in components]
    wanted += ["SEA_CANDIDATE_BINDING_" + t.upper() for t in terms]
    wanted += ["SEA_CANDIDATE_SHORT_" + t.upper() for t in terms]
    wanted += ["GEO_PLOT_STAGE_" + s.upper() for s in stages]
    missing = [k for k in wanted if not _has_text(rows, k)]
    assert not missing, missing


def test_every_literal_key_of_the_track_exists_and_sits_in_the_sorted_tail() -> None:
    used: set[str] = set()
    for path in NEW_UI + [UI / "BuildSiteDialog.gd", PLOT_LENS, REPO / "godot" / "tests" / "GeoExpansionChecks.gd"]:
        # Whole keys only: a prefix the script completes ("SEA_CANDIDATE_BINDING_" + term) is checked above.
        used |= set(re.findall(r'"((?:SEA_CANDIDATE|GEO_PLOT)_[A-Z0-9_]*[A-Z0-9])"', path.read_text(encoding="utf-8")))
    rows = _rows()
    assert len(used) >= 25, sorted(used)
    missing = sorted(k for k in used if not _has_text(rows, k))
    assert not missing, missing
    keys = _keys_in_order()
    tail = keys[keys.index(ANCHOR) + 1:]
    ours = [k for k in keys if k.startswith(("SEA_CANDIDATE_", "GEO_PLOT_"))]
    assert all(k in tail for k in ours), "track B rows must sit in the sorted tail"
    assert tail == sorted(tail)


def test_the_track_carries_no_emoji_and_formats_numbers_in_norwegian() -> None:
    for path in NEW_UI + [REPO / "godot" / "tests" / "GeoExpansionChecks.gd"]:
        assert not EMOJI.search(path.read_text(encoding="utf-8")), path.name
    for key, row in _rows().items():
        if key.startswith(("SEA_CANDIDATE_", "GEO_PLOT_")):
            assert not EMOJI.search(row[1] + row[2]), key
            # nb-NO writes a decimal comma (TextLint's 'decimal' rule).
            assert not re.search(r"(?<![0-9.,])[0-9]+\.[0-9]+(?![0-9.,])", row[2]), (key, row[2])


def test_the_expansion_ui_shows_candidate_reasons_through_their_keys() -> None:
    """No expansion script hard-codes a candidate reason or decides by a message."""
    reasons = [r for r in _consts(VIEWS, "SeaCandidateReasons") if r != "engine_not_ready"]
    for script in list(UI.glob("*.gd")) + [PLOT_LENS]:
        src = script.read_text(encoding="utf-8")
        for reason in reasons:
            assert f'"{reason}"' not in src, (script.name, reason)
        assert "message" not in re.findall(r'\.get\("(\w+)"', src), script.name
