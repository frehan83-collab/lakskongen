"""F2-18 gray-box read gate (farm-types spec §5.5, §7.2).

The gate's shot list (godot/qa/shots.json) must match its schema and cover
the six home-view weathers plus the land and OF1 framings of spec §4; the
gray-box layout must stand on the spec §4 plots with the §5.1 lot geometry;
and the committed measurement (docs/run/evidence/f2_18_read_gate_metrics.json,
written by godot/scripts/qa/FarmTypeGrayBox.gd) must meet the acceptance:
every target recorded in every weather, a lid ellipse of at least 40 px at
the land framing, the OF1 ring at 25-45% of the frame width, and the
evidence doc must fix the read rule, the chip rule and the budgets. None of
these needs Godot; UiFlowTests._test_farm_type_graybox runs the layout.
"""

import json
import re
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
SHOTS = json.loads((GODOT / "qa" / "shots.json").read_text())
SCHEMA = json.loads((GODOT / "qa" / "shots.schema.json").read_text())
SPEC = (REPO / "docs" / "FARM_TYPES_SPEC.md").read_text()
LAYOUT = (GODOT / "scripts" / "qa" / "LandCampusLayout.gd").read_text()
METRICS_PATH = REPO / "docs" / "run" / "evidence" / "f2_18_read_gate_metrics.json"
EVIDENCE = REPO / "docs" / "run" / "evidence" / "f2_18_read_gate.md"
SCENE = "res://qa/scenes/FarmTypeGrayBox.tscn"
WEATHERS = ["clear", "overcast", "rain", "snow", "storm", "fog"]
REQUIRED = [f"36_graybox_home_{w}" for w in WEATHERS] + ["36_graybox_land_focus", "36_graybox_of1_focus"]


# ── A small JSON-Schema validator (the subset shots.schema.json uses) ─────────

def _validate(value, schema, root, path="$"):
    if "$ref" in schema:
        ref = schema["$ref"]
        assert ref.startswith("#/$defs/"), ref
        return _validate(value, root["$defs"][ref.split("/")[-1]], root, path)
    errors = []
    t = schema.get("type")
    types = {"object": dict, "array": list, "string": str, "boolean": bool,
             "integer": int, "number": (int, float)}
    if t and (not isinstance(value, types[t]) or (t in ("integer", "number") and isinstance(value, bool))):
        return [f"{path}: expected {t}, got {type(value).__name__}"]
    if "const" in schema and value != schema["const"]:
        errors.append(f"{path}: {value!r} != const {schema['const']!r}")
    if "enum" in schema and value not in schema["enum"]:
        errors.append(f"{path}: {value!r} not in {schema['enum']}")
    if "pattern" in schema and not re.search(schema["pattern"], value):
        errors.append(f"{path}: {value!r} does not match {schema['pattern']}")
    if "minLength" in schema and len(value) < schema["minLength"]:
        errors.append(f"{path}: shorter than {schema['minLength']}")
    for key, op in (("minimum", lambda v, b: v >= b), ("maximum", lambda v, b: v <= b),
                    ("exclusiveMinimum", lambda v, b: v > b)):
        if key in schema and not op(value, schema[key]):
            errors.append(f"{path}: {value} violates {key} {schema[key]}")
    if isinstance(value, dict):
        for req in schema.get("required", []):
            if req not in value:
                errors.append(f"{path}: missing {req}")
        props = schema.get("properties", {})
        extra = schema.get("additionalProperties", True)
        for k, v in value.items():
            if k in props:
                errors += _validate(v, props[k], root, f"{path}.{k}")
            elif extra is False:
                errors.append(f"{path}: unexpected key {k}")
            elif isinstance(extra, dict):
                errors += _validate(v, extra, root, f"{path}.{k}")
    if isinstance(value, list):
        if "minItems" in schema and len(value) < schema["minItems"]:
            errors.append(f"{path}: fewer than {schema['minItems']} items")
        if "maxItems" in schema and len(value) > schema["maxItems"]:
            errors.append(f"{path}: more than {schema['maxItems']} items")
        if "items" in schema:
            for i, v in enumerate(value):
                errors += _validate(v, schema["items"], root, f"{path}[{i}]")
    return errors


def test_shots_file_matches_its_schema():
    assert _validate(SHOTS, SCHEMA, SCHEMA) == []


def test_the_validator_rejects_a_broken_shot():
    broken = json.loads(json.dumps(SHOTS))
    broken["shots"][0]["weather"] = "drizzle"
    del broken["shots"][1]["camera"]
    broken["shots"][2]["colour"] = "red"
    errors = _validate(broken, SCHEMA, SCHEMA)
    assert any("drizzle" in e for e in errors)
    assert any("missing camera" in e for e in errors)
    assert any("unexpected key colour" in e for e in errors)


def test_shot_ids_are_unique_and_variants_name_a_shot():
    ids = [s["id"] for s in SHOTS["shots"]]
    assert len(ids) == len(set(ids))
    for s in SHOTS["shots"]:
        if "variant_of" in s:
            assert s["variant_of"] in ids, s["id"]


# ── The gate's shots ──────────────────────────────────────────────────────────

def _shot(sid):
    return next(s for s in SHOTS["shots"] if s["id"] == sid)


def _spec_framing(target):
    """Distance (m) and pitch (deg) of a spec §4 camera framing."""
    row = re.search(r"\|\s*%s\s*\|\s*([\d,]+) m\s*\|\s*[−-](\d+)°" % re.escape(target), SPEC)
    assert row, f"no §4 framing for {target}"
    return float(row.group(1).replace(",", "")), -float(row.group(2))


def test_the_gate_shoots_six_weathers_and_both_framings_at_1080p():
    for sid in REQUIRED:
        s = _shot(sid)
        assert s["item"] == "F2-18" and s["scene"] == SCENE and s["resolution"] == [1920, 1080], sid
    assert sorted(_shot(f"36_graybox_home_{w}")["weather"] for w in WEATHERS) == sorted(WEATHERS)
    for w in WEATHERS:
        assert _shot(f"36_graybox_home_{w}")["camera"] == {"mode": "home"}
    land = _shot("36_graybox_land_focus")["camera"]
    assert land["target"] == "pad_a"
    assert (land["distance_m"], land["pitch_deg"]) == _spec_framing("Full campus") == (520.0, -40.0)
    of1 = _shot("36_graybox_of1_focus")["camera"]
    assert of1["target"] == "off_1"
    assert (of1["distance_m"], of1["pitch_deg"]) == _spec_framing("OF1")


def test_the_scene_runs_the_gray_box_script():
    scene = (GODOT / "qa" / "scenes" / "FarmTypeGrayBox.tscn").read_text()
    assert 'path="res://scripts/qa/FarmTypeGrayBox.gd"' in scene
    script = (GODOT / "scripts" / "qa" / "FarmTypeGrayBox.gd").read_text()
    assert f'const SCENE_PATH := "{SCENE}"' in script
    assert script.startswith('extends "res://scripts/qa/QaWorldCapture.gd"')
    base = (GODOT / "scripts" / "qa" / "QaWorldCapture.gd").read_text()
    assert 'const SHOTS_PATH := "res://qa/shots.json"' in base


def test_capture_script_runs_a_qa_scene_and_keeps_the_harness_default():
    sh = (REPO / "tools" / "capture_screenshots.sh").read_text()
    assert 'HARNESS_SCENE="res://scenes/tools/ScreenshotHarness.tscn"' in sh
    assert "--scene) HARNESS_SCENE=" in sh


# ── Layout against the spec ───────────────────────────────────────────────────

def _layout_centres():
    return {m.group(1): (float(m.group(2)), float(m.group(3))) for m in re.finditer(
        r'"(\w+)": \{"kind": "\w+", "centre": Vector2\(([-\d.]+), ([-\d.]+)\)', LAYOUT)}


def _spec_centres():
    out = {}
    for m in re.finditer(r"\|\s*`(pad_\w+|off_\d|hav_\d)`[^|]*\|\s*(?:head delta, )?\(([−+\-]?[\d,]+), ([−+\-]?[\d,]+)\)", SPEC):
        num = lambda s: float(s.replace("−", "-").replace(",", "").replace("+", ""))
        out[m.group(1)] = (num(m.group(2)), num(m.group(3)))
    return out


def test_gray_box_plots_are_the_spec_plots():
    layout = _layout_centres()
    spec = _spec_centres()
    assert set(layout) == {"pad_a", "pad_b", "pad_delta", "off_1", "off_2"}
    for plot, centre in layout.items():
        assert spec[plot] == centre, (plot, spec.get(plot), centre)
    # The read gate moved pad_a; the old centre is gone from both.
    assert layout["pad_a"] == (-450.0, -1720.0)
    assert "Vector2(-700.0, -1650.0)" not in LAYOUT


def test_lot_geometry_is_the_spec_lot():
    const = {m.group(1): float(m.group(2)) for m in re.finditer(r"const (\w+) := ([\d.]+)\n", LAYOUT)}
    row = const["LOT_TANKS_PER_ROW"] * const["GROWOUT_D"] + (const["LOT_TANKS_PER_ROW"] - 1) * const["LOT_TANK_GAP"]
    assert row == 188.0  # §5.1: two rows of 6, 4 m apart, each row 188 m
    assert (const["GROWOUT_D"], const["GROWOUT_H"]) == (28.0, 13.0)
    assert (const["PREGROW_D"], const["PREGROW_H"]) == (16.0, 9.0)
    assert const["PAD_TOP_Y"] == 3.6 and const["OF1_RADIUS"] == 55.0
    assert "LOT_SIZE := Vector2(208.0, 90.0)" in LAYOUT


# ── The measurement ───────────────────────────────────────────────────────────

@pytest.fixture(scope="module")
def metrics():
    return json.loads(METRICS_PATH.read_text())


def test_every_gate_shot_was_measured_at_1080p(metrics):
    assert metrics["item"] == "F2-18" and metrics["resolution"] == [1920, 1080]
    for sid in REQUIRED:
        assert sid in metrics["shots"], sid
    for s in SHOTS["shots"]:
        if s["scene"] == SCENE:
            assert s["id"] in metrics["shots"], f"{s['id']} is in shots.json but was not captured"


def test_home_view_records_size_depression_and_transmittance_per_plot(metrics):
    for w in WEATHERS:
        shot = metrics["shots"][f"36_graybox_home_{w}"]
        assert shot["weather"] == w and shot["camera"]["spec"] == {"mode": "home"}
        assert abs(shot["camera"]["pitch_deg"] + 17.0) < 0.1 and abs(shot["camera"]["fov_deg"] - 45.0) < 0.1
        for plot in ("pad_a", "pad_b", "off_1", "off_2"):
            t = shot["targets"][plot]
            assert t["in_frame"] and t["mask"]["visible"], (w, plot)
            assert t["mask"]["width_px"] > 0 and t["mask"]["height_px"] > 0
            assert 0.0 < t["depression_deg"] < 20.0 and 0.0 <= t["transmittance"] <= 1.0
        tank = shot["targets"]["pad_b"]
        assert tank["mask_tank_wall"]["visible"] and tank["mask_tank_wall"]["height_px"] >= 8, w
        assert shot["targets"]["off_1"]["mask"]["pixels"] >= 200, w  # spec F2-23 #5 needs >= 200 hull pixels


def test_land_framing_lid_ellipse_is_at_least_40_px(metrics):
    t = metrics["shots"]["36_graybox_land_focus"]["targets"]["pad_a"]
    assert abs(t["depression_deg"] - 40.0) < 0.1 and abs(t["distance_m"] - 520.0) < 1.0
    assert t["mask_tank_lid"]["height_px"] >= 40
    assert t["sample_tank"]["lid_projected"]["height_px"] >= 40


def test_of1_framing_ring_fills_25_to_45_percent_and_keeps_the_horizon(metrics):
    shot = metrics["shots"]["36_graybox_of1_focus"]
    width = shot["targets"]["off_1"]["mask"]["width_px"] / metrics["resolution"][0]
    assert 0.25 <= width <= 0.45, width
    pitch = -shot["camera"]["pitch_deg"]
    half = shot["camera"]["fov_deg"] / 2.0
    import math
    horizon_from_top = 0.5 - 0.5 * math.tan(math.radians(pitch)) / math.tan(math.radians(half))
    assert 0.10 <= horizon_from_top <= 0.25, horizon_from_top
    # The revision 2 framing it replaces fails both.
    old = metrics["shots"]["36_graybox_of1_focus_spec_r2"]
    assert old["targets"]["off_1"]["mask"]["width_px"] / metrics["resolution"][0] < 0.25


def test_pad_a_move_is_backed_by_the_measurement(metrics):
    new = metrics["shots"]["36_graybox_home_clear"]["targets"]["pad_a"]["mask"]
    old = metrics["shots"]["36_graybox_home_clear_pad_a_spec_r2"]["targets"]["pad_a"]["mask"]
    assert old["visible_fraction"] < 0.35 <= 0.5 <= new["visible_fraction"]
    assert new["pixels"] > 2 * old["pixels"]


def test_chip_rule_covers_rain_snow_storm_and_the_sea_fog_gap(metrics):
    for w in ("rain", "snow", "storm"):
        assert metrics["shots"][f"36_graybox_home_{w}"]["targets"]["off_1"]["needs_chip"], w
    for w in ("clear", "overcast"):
        assert not metrics["shots"][f"36_graybox_home_{w}"]["targets"]["off_1"]["needs_chip"], w
    fog = metrics["shots"]["36_graybox_home_fog"]
    assert fog["environment"]["sea_fog"] >= 0.5
    for plot in ("pad_a", "pad_b", "off_1"):
        t = fog["targets"][plot]
        # The distance fog alone keeps it above the chip threshold, yet most
        # of the contrast is gone: the reason rule 2 now names the layer.
        assert t["transmittance"] >= 0.4 and t["contrast_vs_clear"] < 0.4, plot
    assert "sea_fog ≥ 0.5" in SPEC


def test_instanced_gray_box_stays_inside_the_draw_budgets(metrics):
    for sid in ("36_graybox_home_clear", "36_graybox_land_focus"):
        perf = metrics["shots"][sid]["perf"]
        assert perf["land"]["added_opaque_draws"] <= 20 and perf["land"]["added_shadow_draws"] <= 16, sid
        assert perf["offshore"]["added_opaque_draws"] <= 8 and perf["offshore"]["added_shadow_draws"] <= 6, sid
    land = metrics["shots"]["36_graybox_land_focus"]["targets"]["pad_a"]["triangles"]
    assert land["lod0_tris_inside_600m"] <= 250_000


def test_evidence_doc_fixes_read_rule_chip_rule_and_budgets():
    doc = EVIDENCE.read_text()
    for needle in ("## Read rule per distance band", "## §5 budgets", "## Acceptance", "LOD0", "LOD1", "LOD2",
                   "Chip", "sea_fog", "pad_a moves", "220 m at −18°", "cast shadows"):
        assert needle in doc, needle
    for img in re.findall(r"\]\((f2_18/[^)]+)\)", doc):
        assert (EVIDENCE.parent / img).is_file(), img
