"""Offshore kit (farm-types spec §5.2, item F2-26): the OF1-class unit meets the spec.

blender/build_offshore.py writes a JSON sidecar next to every .glb (triangles
per node, part and build group; materials and surfaces; bbox; measured
dimensions; swatches; markers and pivots). These tests hold the unit to the
owner's polygon ruling and the spec row: a 12-gon of circumradius 55 m
(± 2.75), 68 m high (± 3.4) at the 43 m draft, columns Ø3.56 and Ø2.8
(± 5%), the rings, diagonals and gantry at their [O:R6] diameters, budgets
of 60 k / 12 k / 1.5 k triangles, the fixed node names with the Beacon, the
part groups for the yard picture-in-picture and the timelapse, the
swatches, the Godot wiring, the preview evidence (the ring fills 25-45% of
the frame at the OF1 framing; the unit and its service vessel inside the
§5.5 home-view draw budget), and, when Blender is installed, a rebuild that
reproduces the committed files byte for byte and a build that stops over
its budget.
"""

import hashlib
import json
import math
import os
import re
import shutil
import struct
import subprocess
import sys
import zlib
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
OFFSHORE = GODOT / "assets" / "models" / "offshore"
FACILITY = GODOT / "assets" / "models" / "facility_kit"
ATLAS = FACILITY / "textures" / "offshore_atlas.png"
BUILD = (REPO / "blender" / "build_offshore.py").read_text()
DESIGNS = (REPO / "blender" / "offshore_designs.py").read_text()
SPEC = (REPO / "docs" / "FARM_TYPES_SPEC.md").read_text()
RUN_LOG = (REPO / "docs" / "RUN_LOG.md").read_text()
MANIFEST = json.loads((REPO / "blender" / "asset_manifest.json").read_text())
ASSET = next(a for a in MANIFEST["assets"] if a["id"] == "of1_class")
LODS = ["LOD0", "LOD1", "LOD2"]
NODES_LOD0 = ["Hull", "Columns", "Bracing", "Topsides", "Net", "Bulkhead", "Cranes", "CraneA", "CraneB", "NavLights",
              "Beacon"]
GROUPS = ["Pontoons", "Columns", "Rings", "Bracing", "Topsides", "Net"]
METRICS = REPO / "docs" / "run" / "evidence" / "f2_26_of1_metrics.json"
EVIDENCE = REPO / "docs" / "run" / "evidence" / "f2_26_of1.md"
SHOTS = json.loads((GODOT / "qa" / "shots.json").read_text())
SCENE = "res://qa/scenes/OffshorePreview.tscn"

sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "blender-asset-pipeline"))
import blender_asset_pipeline as pipeline  # noqa: E402


def _glb(lod):
    return REPO / ASSET["export_path"].format(lod=lod, variant="")


def _sidecar(lod):
    return json.loads(_glb(lod).with_suffix(".json").read_text())


def _glb_load(path):
    data = path.read_bytes()
    assert data[:4] == b"glTF"
    jlen = struct.unpack("<I", data[12:16])[0]
    j = json.loads(data[20:20 + jlen])
    off = 20 + jlen
    blen = struct.unpack("<I", data[off:off + 4])[0]
    return j, data[off + 8:off + 8 + blen]


def _accessor(j, binc, idx):
    """An accessor's values as rows of floats (FLOAT or normalised ints)."""
    a = j["accessors"][idx]
    bv = j["bufferViews"][a["bufferView"]]
    fmt = {5126: "f", 5121: "B", 5123: "H", 5125: "I"}[a["componentType"]]
    n = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
    size = struct.calcsize("<" + fmt)
    start = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
    stride = bv.get("byteStride", size * n)
    rows = []
    for i in range(a["count"]):
        vals = struct.unpack_from("<" + fmt * n, binc, start + i * stride)
        if a.get("normalized"):
            vals = tuple(v / {"B": 255.0, "H": 65535.0, "I": 4294967295.0}[fmt] for v in vals)
        rows.append(vals)
    return rows


def _spec_row():
    """Spec §5.2 `of1_class` row: (cells, (LOD0, LOD1, LOD2) budget)."""
    line = next(l for l in SPEC.splitlines() if l.startswith("| `of1_class` |"))
    cells = [c.strip() for c in line.strip("|").split("|")]
    budget = tuple(int(n.replace(",", "")) for n in re.split(r"\s*/\s*", cells[-1]))
    return cells, budget


def _strips():
    block = BUILD[BUILD.index("STRIPS = ["):BUILD.index("STRIP_INDEX")]
    return [(m.group(1), m.group(2), None if m.group(3) == "None" else float(m.group(3)))
            for m in re.finditer(r'\("(\w+)", "(#[0-9A-F]{6})", (None|[\d.]+),', block)]


def _lab(hexc):
    def lin(c):
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(int(hexc[i:i + 2], 16)) for i in (1, 3, 5))
    f = [(t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116) for t in
         ((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047, 0.2126 * r + 0.7152 * g + 0.0722 * b,
          (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883)]
    return 116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])


def _de(a, b):
    return math.dist(_lab(a), _lab(b))


def _read_png(path):
    data = path.read_bytes()
    pos, idat, w, h = 8, b"", 0, 0
    while pos < len(data):
        n = struct.unpack(">I", data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + n]
        if tag == b"IHDR":
            w, h = struct.unpack(">II", body[:8])
        elif tag == b"IDAT":
            idat += body
        pos += 12 + n
    raw = zlib.decompress(idat)
    stride = 4 * w + 1
    return w, h, [raw[y * stride + 1:(y + 1) * stride] for y in range(h)]


# ── The owner's ruling and the spec row ───────────────────────────────────────

def test_the_polygon_ruling_is_recorded_with_the_side_by_side():
    # F2-26 accept 1: the ruling in RUN_LOG §7, the 12-gon vs 8-gon against
    # O2/O3 in the evidence, and the geometry follows it.
    section = RUN_LOG[RUN_LOG.index("## 7. Decision log"):RUN_LOG.index("## 8.")]
    assert re.search(r"OF1-class polygon ruling.*12-gon", section)
    assert re.search(r"F2-26.*12-gon.*8-gon.*side.by.side", section, re.S)
    doc = EVIDENCE.read_text()
    for needle in ("O2", "O3", "12-gon", "8-gon"):
        assert needle in doc, needle
    assert "](f2_26/of1_12gon_vs_8gon.jpg)" in doc
    assert (EVIDENCE.parent / "f2_26" / "of1_12gon_vs_8gon.jpg").is_file()
    assert re.search(r"^SIDES = 12\s", DESIGNS, re.M)


def test_the_manifest_holds_the_spec_row():
    cells, budget = _spec_row()
    assert "Circumradius 55" in cells[1] and "Overall height 68; draft 43" in cells[1]
    assert ASSET["tri_max"] == dict(zip(LODS, budget)) == {"LOD0": 60000, "LOD1": 12000, "LOD2": 1500}
    limits = re.search(r'"of1_class": \((\d+), (\d+), (\d+)\)', BUILD)
    assert tuple(int(v) for v in limits.groups()) == budget
    assert ASSET["materials"] == ["LK_OffshoreKit", "LK_OffshoreNet"]
    assert ASSET["build_script"] == "blender/build_offshore.py" and ASSET["build_args"] == ["--asset", "of1_class"]
    assert ASSET["spec_tolerance"] <= 0.05
    for part, hexc in ASSET["spec_swatches"].items():
        assert hexc in cells[3], (part, hexc)


# ── Geometry ──────────────────────────────────────────────────────────────────

@pytest.mark.parametrize("lod", LODS)
def test_a_twelve_gon_of_circumradius_55(lod):
    dims = _sidecar(lod)["dimensions"]
    assert dims["sides"] == 12 and dims["columns"] == 12, dims
    assert abs(dims["circumradius_m"] - 55.0) <= 2.75, dims


@pytest.mark.parametrize("lod", ["LOD0", "LOD1"])
def test_68_m_high_at_the_43_m_draft(lod):
    # F2-26 accept 2: height 68 ± 3.4 m, the waterline (model y = 0) at the
    # 43 m draft; everything above the waterline stays within 30 m.
    dims = _sidecar(lod)["dimensions"]
    assert abs(dims["height_m"] - 68.0) <= 3.4, dims
    assert abs(dims["draft_m"] - 43.0) < 1e-6, dims
    assert dims["height_m"] - dims["draft_m"] == pytest.approx(25.4, abs=0.5)
    assert dims["air_draught_m"] < 30.0


@pytest.mark.parametrize("lod", LODS)
def test_column_diameters(lod):
    # F2-26 accept 3: Ø3.56 (with the cone-ended feet) and Ø2.8 ± 5%.
    dims = _sidecar(lod)["dimensions"]
    assert abs(dims["column_large_d_m"] - 3.56) <= 0.05 * 3.56, dims
    assert abs(dims["column_slim_d_m"] - 2.8) <= 0.05 * 2.8, dims


def test_lod0_holds_every_spec_dimension_within_5_percent():
    dims = _sidecar("LOD0")["dimensions"]
    for key, want in ASSET["spec"].items():
        got = dims[key]
        if isinstance(want, int):
            assert got == want, (key, got, want)
        else:
            tol = 3.4 if key == "height_m" else (2.75 if key == "circumradius_m" else 0.05 * want)
            assert abs(got - want) <= tol + 1e-9, (key, got, want)
    # Control block 14 x 14 x 10 (spec, artist est.), feet on the six large columns.
    for got, want in zip(dims["deckhouse_m"], (14.0, 14.0, 10.0)):
        assert abs(got - want) <= 0.05 * want, dims["deckhouse_m"]
    assert dims["feet"] == 6 and dims["foot_d_m"] > dims["column_large_d_m"] * 2


def test_the_frame_sits_above_and_the_net_hangs_below_the_waterline():
    dims = _sidecar("LOD0")["dimensions"]
    assert dims["net_top_m"] > 10.0 and dims["net_bottom_m"] < -30.0
    assert dims["deck_z_m"] == pytest.approx(15.4)
    lod2 = _sidecar("LOD2")["dimensions"]
    # The far LOD keeps only what stands above the opaque water.
    assert lod2["net_bottom_m"] >= -3.0 and lod2["draft_m"] < 4.0


# ── Budgets, nodes, groups ────────────────────────────────────────────────────

def test_triangle_budgets_hold_and_lods_step_down():
    # F2-26 accept 4.
    tris = [_sidecar(lod)["tri_count"] for lod in LODS]
    for lod, n in zip(LODS, tris):
        assert n <= ASSET["tri_max"][lod], (lod, n)
        assert n == ASSET["tri_budget"][lod], (lod, "manifest tri_budget is stale")
    assert tris[0] > tris[1] > tris[2]


def test_verifier_passes_every_lod():
    spec = pipeline.AssetSpec.from_dict(ASSET)
    for lod in spec.lod_levels:
        result = pipeline.validate_asset(spec, lod, None, _glb(lod))
        assert result.passed, (lod, result.errors)


def test_fixed_node_names_and_the_beacon():
    # F2-26 accept 5.
    j, _ = _glb_load(_glb("LOD0"))
    names = [n["name"] for n in j["nodes"]]
    assert sorted(names) == sorted(NODES_LOD0 + ["W2WLanding"]), names
    nodes = {n["name"]: n for n in j["nodes"]}
    cranes = [j["nodes"][i]["name"] for i in nodes["Cranes"]["children"]]
    assert sorted(cranes) == ["CraneA", "CraneB"]
    assert "mesh" not in nodes["Beacon"] and "mesh" not in nodes["Cranes"]
    beacon = nodes["Beacon"]["translation"]
    side = _sidecar("LOD0")
    assert beacon[1] == pytest.approx(side["markers"]["Beacon"][1]) and beacon[1] > 27.0
    assert math.hypot(beacon[0], beacon[2]) < 3.0, "the beacon sits on the mast at the unit's axis"
    for lod in ("LOD1", "LOD2"):
        j, _ = _glb_load(_glb(lod))
        assert sorted(n["name"] for n in j["nodes"]) == ["Hull", "Net"], lod


def test_cranes_slew_on_their_own_axes():
    j, binc = _glb_load(_glb("LOD0"))
    nodes = {n["name"]: n for n in j["nodes"]}
    dims = _sidecar("LOD0")["dimensions"]
    assert abs(dims["crane_reach_m"] - 18.0) <= 0.05 * 18.0
    for name in ("CraneA", "CraneB"):
        t = nodes[name]["translation"]
        assert abs(math.hypot(t[0], t[2]) - 55.0) < 0.5 and t[1] == pytest.approx(15.4), (name, t)
        pos = _accessor(j, binc, j["meshes"][nodes[name]["mesh"]]["primitives"][0]["attributes"]["POSITION"])
        # The pedestal stands on the node origin.
        assert min(p[1] for p in pos) == pytest.approx(0.0, abs=1e-3)


@pytest.mark.parametrize("lod", LODS)
def test_every_vertex_carries_its_build_group(lod):
    # F2-26 accept 6: the part groups Pontoons -> Columns -> Rings -> Bracing
    # -> Topsides -> Net exist for the yard view and the timelapse.
    j, binc = _glb_load(_glb(lod))
    found = set()
    for mesh in j["meshes"]:
        for prim in mesh["primitives"]:
            assert "TEXCOORD_1" in prim["attributes"], (lod, mesh["name"])
            groups = {round(v[0], 3) for v in _accessor(j, binc, prim["attributes"]["TEXCOORD_1"])}
            assert all(g == int(g) and 0 <= g <= 5 for g in groups), (lod, mesh["name"], groups)
            if j["materials"][prim["material"]]["name"] == "LK_OffshoreNet":
                assert groups == {5.0}, (lod, mesh["name"])
            found |= {int(g) for g in groups}
    side = _sidecar(lod)
    assert sorted(side["groups"]) == sorted(GROUPS)
    want = set(range(6)) if lod != "LOD2" else {1, 2, 3, 4, 5}
    assert found == want, (lod, found)
    assert all(side["groups"][GROUPS[g]] > 0 for g in want)


def test_build_groups_are_wired_through_shader_and_model():
    shader = (GODOT / "shaders" / "offshore_kit.gdshader").read_text()
    assert "instance uniform float build_stage = 6.0;" in shader
    assert "part_group = floor(UV2.x + 0.5);" in shader
    lo = re.search(r"const float GROUP_LO\[6\] = \{([^}]*)\};", shader).group(1).split(",")
    hi = re.search(r"const float GROUP_HI\[6\] = \{([^}]*)\};", shader).group(1).split(",")
    assert len(lo) == len(hi) == 6 and all(float(a) < float(b) for a, b in zip(lo, hi))
    # Each band covers its group above the water: the topsides on the deck,
    # the pontoons at the keel.
    assert float(lo[4]) > 10.0 and float(hi[0]) < -30.0 and float(lo[0]) <= -43.0
    model = (GODOT / "scripts" / "visual" / "OffshoreUnitModel.gd").read_text()
    assert re.search(r'const PART_GROUPS := \["Pontoons", "Columns", "Rings", "Bracing", "Topsides", "Net"\]', model)
    assert re.search(r'GROUP_NAMES = \["Pontoons", "Columns", "Rings", "Bracing", "Topsides", "Net"\]', DESIGNS)
    assert "func set_build_stage(stage: float)" in model


# ── Swatches, atlas, shader rows ──────────────────────────────────────────────

def test_swatch_report_matches_the_spec():
    strips = {name: hexc for name, hexc, _ in _strips()}
    side = _sidecar("LOD0")
    assert set(side["swatches"]) == set(ASSET["spec_swatches"])
    for part, sw in side["swatches"].items():
        assert sw["spec"] == ASSET["spec_swatches"][part]
        assert strips[sw["strip"]] == sw["spec"], (part, sw)
        assert _de(sw["hex"], sw["spec"]) <= 3.0, (part, sw)
    assert side["net_colour"] == "#A8ADB0"
    net = (FACILITY / "LK_OffshoreNet.tres").read_text()
    assert "shader_parameter/albedo_color = Color(0.658824, 0.678431, 0.690196, 1)" in net


def test_atlas_strips_hold_their_swatch_and_survive_mipmapping():
    w, h, rows = _read_png(ATLAS)
    assert (w, h) == (1024, 1024)
    for idx, (name, hexc, rough) in enumerate(_strips()):
        block = rows[idx * 32:(idx + 1) * 32]
        assert all(r == block[0] for r in block), name
        line = block[16]
        px = sorted((tuple(line[4 * x:4 * x + 3]) for x in range(w)), key=sum)
        assert _de("#%02X%02X%02X" % px[w // 2], hexc) <= 3.0, name
        alphas = {line[4 * x + 3] for x in range(w)}
        assert alphas == ({255} if rough is None else {round(rough * 255)}), (name, alphas)


def test_shader_rows_follow_the_strip_order():
    names = [s[0] for s in _strips()]
    shader = (GODOT / "shaders" / "offshore_kit.gdshader").read_text()
    rows = dict(re.findall(r"const float ROW_(\w+) = ([\d.]+);", shader))
    for const, strip in (("HULL", "hull_yellow"), ("SPLASH", "hull_splash"), ("WHITE", "white"), ("GREY", "grey"),
                         ("SILO", "silo"), ("CRANE", "crane"), ("NAV", "nav_light")):
        assert float(rows[const]) == names.index(strip), const


def test_the_splash_zone_is_painted_round_the_waterline():
    # Spec: splash zone 0-4 m #B8902A (plus 2.5 m below for heave).
    assert re.search(r"^SPLASH = \(-2\.5, 4\.0\)", DESIGNS, re.M)
    side = _sidecar("LOD0")
    assert side["swatches"]["SplashZone"]["strip"] == "hull_splash"


# ── Godot wiring ──────────────────────────────────────────────────────────────

@pytest.mark.parametrize("lod", LODS)
def test_godot_import_uses_the_kit_materials_and_hand_made_lods(lod):
    text = Path(str(_glb(lod)) + ".import").read_text()
    assert "meshes/generate_lods=false" in text
    for name in ("LK_OffshoreKit", "LK_OffshoreNet"):
        block = re.search(r'"%s": \{(.*?)\}' % name, text, re.S)
        assert block and '"use_external/enabled": true' in block.group(1), (lod, name)
        assert f'"use_external/path": "res://assets/models/facility_kit/{name}.tres"' in block.group(1)


def test_kit_materials_read_the_atlas_and_the_net_shader():
    kit = (FACILITY / "LK_OffshoreKit.tres").read_text()
    assert 'path="res://shaders/offshore_kit.gdshader"' in kit
    assert 'path="res://assets/models/facility_kit/textures/offshore_atlas.png"' in kit
    assert 'resource_name = "LK_OffshoreKit"' in kit
    net = (FACILITY / "LK_OffshoreNet.tres").read_text()
    assert 'path="res://shaders/pen_net_shader.gdshader"' in net and 'resource_name = "LK_OffshoreNet"' in net
    imp = (FACILITY / "textures" / "offshore_atlas.png.import").read_text()
    assert "mipmaps/generate=true" in imp and "compress/mode=0" in imp


def test_scene_inherits_the_lods_with_the_spec_lod_distances():
    scene = (REPO / ASSET["godot_scene"]).read_text()
    for lod in LODS:
        assert f"of1_class_{lod}.glb" in scene
    assert "scripts/visual/OffshoreUnitModel.gd" in scene and 'asset_id = "of1_class"' in scene
    # Spec §5.5 rule 4: units LOD0 below 500 m and LOD1 below 2,000 m.
    assert "lod_switch = Vector2(500, 2000)" in scene
    dims = _sidecar("LOD0")["dimensions"]
    assert float(re.search(r"height_m = ([\d.]+)", scene).group(1)) == dims["height_m"]
    assert float(re.search(r"air_draught_m = ([\d.]+)", scene).group(1)) == dims["air_draught_m"]
    model = (GODOT / "scripts" / "visual" / "OffshoreUnitModel.gd").read_text()
    assert 'const NO_SHADOW := ["Net", "NavLights"]' in model
    assert "var casts := level == 0" in model, "buildings and units cast no shadow from LOD1 on (spec §5)"


def test_nav_lanterns_flash_in_sync():
    shader = (GODOT / "shaders" / "offshore_kit.gdshader").read_text()
    assert "uniform float nav_flash_hz = 1.0;" in shader
    assert "fract(TIME * nav_flash_hz)" in shader, "one global clock: every unit flashes together"
    assert "instance uniform float nav" not in shader


def test_the_8_gon_is_evidence_only():
    assert "--sides" in BUILD and "choices=(8, 12)" in BUILD
    assert not list(GODOT.rglob("*8gon*")), "the 8-gon read of O2 is never committed as an asset"


# ── Preview evidence ──────────────────────────────────────────────────────────

@pytest.fixture(scope="module")
def metrics():
    return json.loads(METRICS.read_text())


def test_every_offshore_shot_was_captured(metrics):
    assert metrics["item"] == "F2-26/F2-27" and metrics["resolution"] == [1920, 1080]
    shots = [s for s in SHOTS["shots"] if s["scene"] == SCENE]
    assert len(shots) >= 8
    for s in shots:
        assert s["id"] in metrics["shots"], s["id"]


def test_the_ring_fills_its_share_of_the_of1_framing(metrics):
    # Spec §4 (as the F2-18 read gate set it): 220 m at -18 deg, the ring
    # fills 25-45% of the frame width.
    shot = metrics["shots"]["40_offshore_framing"]
    assert shot["camera"]["spec"]["distance_m"] == 220.0 and shot["camera"]["spec"]["pitch_deg"] == -18.0
    assert 0.25 <= shot["ring"]["width_frac"] <= 0.45, shot["ring"]
    assert shot["unit"]["lod"] == 0


def test_home_view_draws_inside_the_unit_budget(metrics):
    # Spec §5.5 rule 5: one OF1-class unit plus its vessel adds <= 8 opaque
    # and <= 6 shadow draw calls at the home view.
    shot = metrics["shots"]["40_offshore_home"]
    assert shot["camera"]["spec"] == {"mode": "home"} and shot["unit"]["lod"] == 2
    pair = shot["perf"]["added_unit_service"]
    assert pair["opaque_draws"] <= 8 and pair["shadow_draws"] <= 6, pair
    assert shot["perf"]["added_unit"]["shadow_draws"] == 0


def test_hero_mood_is_low_winter_sun(metrics):
    hero = metrics["shots"]["40_offshore_hero"]
    assert 0.0 < hero["environment"]["sun_elevation_deg"] < 12.0
    dusk = metrics["shots"]["40_offshore_dusk"]
    assert dusk["environment"]["nav_lights"] == 1.0 and dusk["environment"]["sun_elevation_deg"] < 0.0
    yard = metrics["shots"]["40_offshore_yard"]
    assert 0.0 < yard["unit"]["build_stage"] < 6.0


# ── Rebuild (needs Blender) ───────────────────────────────────────────────────

def _blender():
    for candidate in (os.environ.get("BLENDER_BIN"), "/Applications/Blender.app/Contents/MacOS/Blender",
                      shutil.which("blender")):
        if candidate and Path(candidate).is_file():
            return candidate
    return None


def _build(tmp_path, *args):
    return subprocess.run(
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / "build_offshore.py"), "--",
         "--out", str(tmp_path), *args], capture_output=True, text=True, timeout=900)


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
@pytest.mark.parametrize("lod", ["LOD1", "LOD2"])
def test_build_script_reproduces_the_committed_files(tmp_path, lod):
    out = _build(tmp_path, "--lod", lod, "--textures", "--atlas-out", str(tmp_path / "atlas.png"))
    assert out.returncode == 0, out.stderr[-2000:]
    assert (tmp_path / "atlas.png").read_bytes() == ATLAS.read_bytes()
    rebuilt = tmp_path / _glb(lod).name
    new_side = json.loads(rebuilt.with_suffix(".json").read_text())
    old_side = _sidecar(lod)
    assert {k: v for k, v in new_side.items() if k != "blender_version"} == \
        {k: v for k, v in old_side.items() if k != "blender_version"}
    if new_side["blender_version"] == old_side["blender_version"]:
        assert hashlib.sha256(rebuilt.read_bytes()).hexdigest() == hashlib.sha256(_glb(lod).read_bytes()).hexdigest()


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
def test_build_stops_over_its_triangle_budget(tmp_path):
    out = _build(tmp_path, "--lod", "LOD2", "--tri-limit-scale", "0.5")
    assert out.returncode != 0
    n = _sidecar("LOD2")["tri_count"]
    assert f"of1_class LOD2 has {n} triangles, over its 750 budget" in out.stdout + out.stderr
