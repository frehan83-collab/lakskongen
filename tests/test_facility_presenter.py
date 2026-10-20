"""F2-19 land pads and F2-20 facility presenter (farm-types spec §4, §5, §6.3, §7.2).

The committed measurement docs/run/evidence/f2_20_facilities_metrics.json is
what godot/scripts/qa/FacilityPresenterCapture.gd wrote for the shots of
godot/qa/shots.json that run in res://qa/scenes/FacilityPresenterCapture.tscn
(tools/capture_screenshots.sh --scene ...). It must hold every such shot and
meet the acceptance the frames can show:

- F2-19 accept 6: an unbuilt pad (heath) and a built pad (blasted granite)
  differ by ΔE76 >= 10 in their mean colour, at every framing measured;
- F2-20 accept 1: in the QA set (an HFS lot, a post-smolt site, an OF1-class
  unit, built through the engine) the presenter draws the engine's tanks and
  units, and a tank stands whole exactly when the engine has handed it over;
- F2-20 accept 2: each construction shot stands in the engine's phase it
  names, and the tank-rings and sea-works shots differ by pHash Δ >= 9;
- F2-20 accept 4: at the home view (the P0-04 view) a full 4-lot campus adds
  <= 20 opaque + <= 16 shadow draws and one OF1-class unit with its vessel
  <= 8 + 6; a full campus is <= 250 k LOD0 triangles; LOD2 casts no shadow
  and of LOD1 only the tanks do.

The UI-input half of the acceptance (the click, the card, the camera,
cancelling a build) is UiFlowTests._test_facilities_render; the frame-time
half is the P0-04 run in docs/run/evidence/f2_20_presenter.md. The source
checks below hold the presenter to reading the engine, and keep the QA
staging verbs out of the player's UI.
"""

import json
import re
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
SHOTS = json.loads((GODOT / "qa" / "shots.json").read_text())["shots"]
SCENE = "res://qa/scenes/FacilityPresenterCapture.tscn"
METRICS_PATH = REPO / "docs" / "run" / "evidence" / "f2_20_facilities_metrics.json"
OWN = [s for s in SHOTS if s["scene"] == SCENE]
LAND_PHASES = ["site_works", "tank_rings", "treatment_hall", "sea_works", "first_tanks", "completion"]
SEA_PHASES = ["yard_contract", "hull", "voyage", "tow", "mooring", "commissioning"]
PRESENTER = [
    GODOT / "scripts" / "visual" / "FarmFacilityVisualManager.gd",
    GODOT / "scripts" / "visual" / "LandCampusBuilder.gd",
    GODOT / "scripts" / "visual" / "OffshoreUnitVisual.gd",
    GODOT / "scripts" / "ui" / "facilities" / "FacilityWorldCard.gd",
]
QA_VERBS = ["QaPlaceFarmTypeSet", "QaStartLandBuild", "QaExpandLandSite", "QaStageConstruction", "QaContractOffshore"]


@pytest.fixture(scope="module")
def metrics():
    return json.loads(METRICS_PATH.read_text())


def _land(units):
    return next(u for u in units.values() if u["variant"] == "hfs")


def _of1(units):
    return next(u for u in units.values() if u["variant"] == "of1")


def test_every_presenter_shot_was_measured_at_1080p(metrics):
    assert metrics["items"] == ["F2-19", "F2-20"] and metrics["resolution"] == [1920, 1080]
    assert OWN, "no FacilityPresenterCapture shots in shots.json"
    for s in OWN:
        assert s["id"] in metrics["shots"], f"{s['id']} is in shots.json but was not captured"


def test_a_built_pad_differs_from_heath_by_delta_e_ten(metrics):
    pairs = [s["id"] for s in OWN if s["id"].endswith("_granite")]
    assert len(pairs) == 4
    for sid in pairs:
        delta = metrics["shots"][sid]["delta_e_vs_heath"]
        assert delta, sid
        for pad, de in delta.items():
            assert de >= 10.0, (sid, pad, de)


def test_the_presenter_draws_the_engines_tanks_and_units(metrics):
    built = [s for s in OWN if s.get("facilities", {}).get("land", {}).get("phase") == "operational"]
    assert built
    for s in built:
        units = metrics["shots"][s["id"]]["units"]
        variants = sorted(u["variant"] for u in units.values())
        want = ["hfs", "post_smolt"] + (["of1"] if "offshore" in s["facilities"] else [])
        assert variants == sorted(want), (s["id"], variants)
        for uid, u in units.items():
            if u["variant"] == "of1":
                if u["phase_key"] == "operational":
                    assert u["unit_visible"] and "offshore_service" in u["fleet"], (s["id"], uid)
            else:
                assert u["engine_tanks"] > 0 and u["drawn_tanks"] == u["engine_tanks"], (s["id"], uid, u)
                assert u["whole_tanks"] == u["engine_ready_tanks"] == u["engine_tanks"], (s["id"], uid, u)
        assert _land(units)["lots"] == s["facilities"]["land"]["lots"], s["id"]


def test_each_construction_shot_stands_in_its_own_phase_and_hands_over_the_engines_tanks(metrics):
    shots = sorted(s["id"] for s in OWN if s["id"].startswith("32_land_construction_"))
    assert len(shots) == len(LAND_PHASES) + 1  # site works twice: the blast, then the foundations
    for sid in shots:
        spec = next(s for s in OWN if s["id"] == sid)["facilities"]["land"]
        land = _land(metrics["shots"][sid]["units"])
        assert land["phase_key"] == spec["phase"] and land["status"] == "building", (sid, land)
        assert land["phase_index"] == LAND_PHASES.index(spec["phase"]), (sid, land)
        # Whole tanks are the ones the engine has handed over; the rest stand open (or not yet).
        assert land["whole_tanks"] == land["engine_ready_tanks"], (sid, land)
    blast = _land(metrics["shots"]["32_land_construction_p0_site_works_blast"]["units"])
    assert blast["drawn_tanks"] == 0 and 0.0 < blast["blast_state"] < 1.0, blast
    rings = _land(metrics["shots"]["32_land_construction_p1_site_works_foundations"]["units"])
    assert rings["drawn_tanks"] == rings["engine_tanks"] and rings["whole_tanks"] == 0, rings
    done = _land(metrics["shots"]["32_land_construction_p6_completion"]["units"])
    assert 0 < done["whole_tanks"] < done["engine_tanks"], done


def test_the_tank_rings_and_the_sea_works_differ_by_phash_nine(metrics):
    p4 = metrics["shots"]["32_land_construction_p4_sea_works"]
    assert p4["phash_distance_vs_32_land_construction_p2_tank_rings"] >= 9


def test_each_offshore_phase_draws_its_own_state(metrics):
    seen = {}
    for s in OWN:
        o = s.get("facilities", {}).get("offshore")
        if not o or o["phase"] == "operational":
            continue
        unit = _of1(metrics["shots"][s["id"]]["units"])
        assert unit["phase_key"] == o["phase"] == unit["drawn_phase"], (s["id"], unit)
        seen[o["phase"]] = (unit["unit_visible"], unit["buoys"], tuple(sorted(unit["fleet"])))
    assert sorted(seen) == sorted(SEA_PHASES)
    assert len(set(seen.values())) == len(SEA_PHASES), seen


def test_the_draw_call_budgets_at_the_home_view(metrics):
    campus = metrics["shots"]["44_facilities_budget_campus_home"]
    assert campus["camera"]["spec"]["mode"] == "home" and campus["perf"]["units"] == ["land"]
    assert _land(campus["units"])["lots"] == 4
    assert campus["perf"]["added_opaque_draws"] <= 20 and campus["perf"]["added_shadow_draws"] <= 16, campus["perf"]
    of1 = metrics["shots"]["44_facilities_budget_of1_home"]
    assert of1["camera"]["spec"]["mode"] == "home" and of1["perf"]["units"] == ["offshore"]
    assert of1["perf"]["added_opaque_draws"] <= 8 and of1["perf"]["added_shadow_draws"] <= 6, of1["perf"]


def test_a_full_campus_stays_under_250k_lod0_triangles(metrics):
    land = _land(metrics["shots"]["44_facilities_campus_full"]["units"])
    assert land["lots"] == 4 and land["drawn_tanks"] == 4 * 14
    assert 0 < land["lod0_triangles"] <= 250_000, land["lod0_triangles"]


def test_the_shadow_rule_holds_for_every_unit(metrics):
    for sid, rec in metrics["shots"].items():
        for uid, u in rec.get("units", {}).items():
            if "shadow_rule_ok" in u:
                assert u["shadow_rule_ok"], (sid, uid)


def test_the_of1_unit_reads_as_hull_yellow_where_it_stands(metrics):
    for sid in ["44_facilities_of1_winter_sun", "44_facilities_of1_hero"]:
        unit = _of1(metrics["shots"][sid]["units"])
        assert unit["hull_yellow"]["share"] >= 0.05, (sid, unit["hull_yellow"])


# ── Source rules ──────────────────────────────────────────────────────────────

def test_the_presenter_only_reads_the_engine():
    """The presenter calls no SimBridge or GameBridge verb but its reads."""
    reads = {"get_facility_visuals", "get_land_plots", "get_offshore_plots"}
    for path in PRESENTER:
        text = path.read_text()
        for verb in QA_VERBS:
            assert verb not in text, (path.name, verb)
        called = set(re.findall(r'sim\.(?:call\(")?([a-z_]+)', text))
        for getters in re.findall(r"for getter in \[([^\]]*)\]", text):
            called |= set(re.findall(r'"([a-z_]+)"', getters))
        assert called <= reads | {"call", "has_method", "has_signal", "state_updated"}, (path.name, called - reads)
        assert "/root/GameBridge" not in text, path.name


# The presenter's only way into the engine: a local bound to the SimBridge
# autoload by its node path. Everything done with such a local must be one of
# the three reads (or has_method / has_signal, or connecting to state_updated).
SIM_BINDING = re.compile(r'(?:var\s+)?([A-Za-z_]\w*)\s*(?::\s*[A-Za-z_]\w*\s*)?:?=\s*get_node(?:_or_null)?\(\s*"/root/SimBridge"\s*\)')
SIM_SAFE_MEMBERS = {"has_method", "has_signal"}
SIM_READS = {"get_facility_visuals", "get_land_plots", "get_offshore_plots"}


def _code_only(text: str) -> str:
    """The script without its comments (a '#' outside a string starts one)."""
    out_lines = []
    for line in text.splitlines():
        out, quote, i = [], None, 0
        while i < len(line):
            ch = line[i]
            if quote:
                out.append(ch)
                if ch == "\\" and i + 1 < len(line):
                    out.append(line[i + 1])
                    i += 1
                elif ch == quote:
                    quote = None
            elif ch in "\"'":
                quote = ch
                out.append(ch)
            elif ch == "#":
                break
            else:
                out.append(ch)
            i += 1
        out_lines.append("".join(out))
    return "\n".join(out_lines)


def _strings_masked(code: str) -> str:
    """`code` with every string literal's content blanked (same length)."""
    return re.sub(r'"(?:[^"\\\n]|\\.)*"|\'(?:[^\'\\\n]|\\.)*\'', lambda m: m.group(0)[0] + "_" * (len(m.group(0)) - 2) + m.group(0)[-1], code)


def engine_rule_breaches(text: str) -> list:
    """What a presenter script does with the engine beyond its reads.

    Wave4b review: the first source rule caught only calls through a local
    named `sim`. This one tracks every local bound to /root/SimBridge (by any
    name, and the parameters of this file's functions it is passed to), and
    refuses any other way in: the SimBridge or GameBridge autoload by name,
    another path to them, a QA verb, an alias, call()/callv() of anything but
    a read, emitting state_updated.
    """
    code = _code_only(text)
    breaches = [f"QA verb {verb}" for verb in QA_VERBS + ["QaContractOffshoreUnit"] if verb in code]
    names = set(SIM_BINDING.findall(code))
    rest = SIM_BINDING.sub(lambda m: "", code)
    for word in ("SimBridge", "GameBridge"):
        if word in rest:
            breaches.append(f"{word} reached other than by a local bound to /root/SimBridge")
    funcs = {m.group(1): [p.split(":")[0].strip() for p in m.group(2).split(",") if p.strip()]
             for m in re.finditer(r"func\s+([A-Za-z_]\w*)\s*\(([^)]*)\)", rest)}
    loops = {m.group(1): re.findall(r'"([^"]*)"', m.group(2))
             for m in re.finditer(r"for\s+([A-Za-z_]\w*)\s+in\s+\[([^\]]*)\]", rest)}
    # A bound local passed to this file's own function binds that parameter too.
    changed = True
    while changed:
        changed = False
        for fname, params in funcs.items():
            for m in re.finditer(rf"\b{fname}\s*\(([^()]*)\)", rest):
                args = [a.strip() for a in m.group(1).split(",")]
                for k, arg in enumerate(args):
                    if arg in names and k < len(params) and params[k] not in names:
                        names.add(params[k])
                        changed = True
    masked = _strings_masked(rest)
    for name in sorted(names):
        for m in re.finditer(rf"\b{name}\b", masked):
            after = rest[m.end():]
            before = rest[:m.start()]
            member = re.match(r"\s*\.\s*([A-Za-z_]\w*)", after)
            if member:
                what = member.group(1)
                tail = after[member.end():]
                if what in SIM_READS or what in SIM_SAFE_MEMBERS:
                    continue
                if what == "state_updated" and re.match(r"\s*\.\s*connect\s*\(", tail):
                    continue
                if what == "call":
                    arg = re.match(r'\s*\(\s*(?:"([^"]*)"|([A-Za-z_]\w*))\s*[,)]', tail)
                    if arg and arg.group(1) in SIM_READS:
                        continue
                    if arg and arg.group(2) and loops.get(arg.group(2)) and set(loops[arg.group(2)]) <= SIM_READS:
                        continue
                breaches.append(f"{name}.{what}")
                continue
            if re.match(r"\s*[!=]=\s*null\b", after) or re.search(r"(?:\bnot|\bif|\band|\bor)\s*$", before) and not re.match(r"\s*=", after):
                continue
            # A parameter declaration of this file's function, or an argument to one that binds it.
            if re.search(r"func\s+[A-Za-z_]\w*\s*\([^)]*$", before):
                continue
            call = re.search(r"\b([A-Za-z_]\w*)\s*\(([^()]*)$", before)
            if call and call.group(1) in funcs:
                args = (call.group(2) + name).split(",")
                k = len(args) - 1
                params = funcs[call.group(1)]
                if k < len(params) and params[k] in names:
                    continue
            breaches.append(f"{name} used as a value (an alias the rule cannot follow)")
    return breaches


def test_no_presenter_script_reaches_the_engine_but_through_its_reads():
    for path in PRESENTER:
        assert engine_rule_breaches(path.read_text()) == [], path.name


def _with_line_after(path, anchor: str, line: str) -> str:
    text = path.read_text()
    assert anchor in text, (path.name, anchor)
    return text.replace(anchor, anchor + "\n" + line, 1)


def test_the_engine_rule_catches_the_known_ways_round_it():
    manager, campus = PRESENTER[0], PRESENTER[1]
    anchor_m = "func _on_state_updated() -> void:"
    anchor_c = "func _process(_delta: float) -> void:"
    bypasses = [
        # The review's two mutations (they passed the first rule).
        _with_line_after(manager, anchor_m, '\tvar bridge := get_node_or_null("/root/SimBridge"); bridge.stock_land_tank("ras_1", "t01", 50000, "standard")'),
        _with_line_after(campus, anchor_c, '\tvar sb := get_node("/root/SimBridge"); sb.set_paused(false)'),
        # The autoload by name, the other bridge, a QA verb, an alias, a write through call().
        _with_line_after(campus, anchor_c, "\tSimBridge.set_paused(false)"),
        _with_line_after(manager, anchor_m, '\tget_node("/root/GameBridge").call("SetPaused", true)'),
        _with_line_after(manager, anchor_m, '\tvar s := get_node("/root/SimBridge")\n\tvar other = s\n\tother.set_paused(false)'),
        _with_line_after(manager, anchor_m, '\tvar s := get_node("/root/SimBridge")\n\ts.call("set_paused", false)'),
        _with_line_after(manager, anchor_m, '\tvar s := get_node("/root/SimBridge")\n\ts.state_updated.emit()'),
        _with_line_after(campus, anchor_c, '\tget_tree().root.get_node("SimBridge").set_paused(false)'),
    ]
    for text in bypasses:
        assert engine_rule_breaches(text), text[-400:]
    # What the rule lets through: a null check and has_method, a read, the
    # bound local handed to this file's own function (its parameter is held
    # to the same rule).
    allowed = [
        _with_line_after(manager, anchor_m, '\tvar s := get_node("/root/SimBridge")\n\tif s != null and not s.has_method("get_land_plots"):\n\t\treturn'),
        _with_line_after(manager, anchor_m, '\tvar s := get_node("/root/SimBridge")\n\t_refresh_plot_names(s)'),
        _with_line_after(manager, anchor_m, '\tvar s := get_node("/root/SimBridge")\n\tvar rows_again: Array = s.call("get_facility_visuals")'),
    ]
    for text in allowed:
        assert engine_rule_breaches(text) == [], text[-400:]


def test_the_qa_staging_verbs_are_never_called_from_the_players_ui():
    allowed = ("godot/tests/", "godot/scripts/qa/", "godot/scripts/tools/PerfHarness.gd",
               "godot/scripts/autoload/GameBridge.FacilityVisuals.cs")
    for path in list((GODOT / "scripts").rglob("*.gd")) + list((GODOT / "scripts").rglob("*.cs")) + list(GODOT.glob("tests/*.gd")):
        rel = path.relative_to(REPO).as_posix()
        text = path.read_text()
        for verb in QA_VERBS + ["QaContractOffshoreUnit"]:
            if verb in text:
                assert rel.startswith(allowed), (rel, verb)
    engine_callers = [p.relative_to(REPO).as_posix() for p in (REPO / "src" / "LakskongenSim").rglob("*.cs")
                      if "QaContractOffshoreUnit(" in p.read_text() and "/bin/" not in p.as_posix() and "/obj/" not in p.as_posix()]
    assert engine_callers == ["src/LakskongenSim/Core/SimulationEngine.QaStaging.cs"], engine_callers


def test_the_cards_text_is_one_sorted_block_of_keys_that_exist():
    rows = (GODOT / "translations" / "translations.csv").read_text(encoding="utf-8").splitlines()
    block = [r.split(",", 1)[0] for r in rows if r.startswith("FACILITY_CARD_")]
    assert block == sorted(block) and len(block) == len(set(block))
    keys = {r.split(",", 1)[0] for r in rows}
    card = (GODOT / "scripts" / "ui" / "facilities" / "FacilityWorldCard.gd").read_text()
    for key in re.findall(r'tr\("([A-Z0-9_]+)"\)', card):
        assert key in keys, key
    # The phase names are the engine's own keys' (the land lane's and the sea lane's).
    for phase in LAND_PHASES:
        assert f"LAND_PHASE_{phase.upper()}" in keys, phase
    for phase in SEA_PHASES:
        assert f"BUILD_SEA_PHASE_{phase.upper()}" in keys, phase
    for reason in ["tow", "sheltering", "funds", "closed_season"]:
        assert f"FACILITY_CARD_WAITING_{reason.upper()}" in keys, reason
