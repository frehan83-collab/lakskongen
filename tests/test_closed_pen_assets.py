"""F2-30 closed-pen kit: spec geometry, real GLB volume and presenter contract.

The volume acceptance is measured independently from the committed GLB shell
triangles (isolated by their atlas V row), not inferred from a nominal radius
or trusted from a sidecar. Blender rebuild tests prove deterministic exports.
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
FACILITY = GODOT / "assets" / "models" / "facility_kit"
ATLAS = FACILITY / "textures" / "facility_atlas.png"
BUILD = (REPO / "blender" / "build_closed_pens.py").read_text()
LAND_BUILD = (REPO / "blender" / "build_land_base.py").read_text()
SPEC = (REPO / "docs" / "FARM_TYPES_SPEC.md").read_text()
MANIFEST = json.loads((REPO / "blender" / "asset_manifest.json").read_text())
GENERATED = json.loads((GODOT / "assets" / "asset_manifest.json").read_text())
ASSETS = {a["id"]: a for a in MANIFEST["assets"] if a.get("type") == "closed_pen"}
KIT_IDS = ["closed_donut", "closed_globe", "semi_closed_ring", "pump_module"]
EVIDENCE = REPO / "docs" / "run" / "evidence" / "f2_30_closed_pens.md"

sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "blender-asset-pipeline"))
import blender_asset_pipeline as pipeline  # noqa: E402


def _glb(asset, lod):
    return REPO / ASSETS[asset]["export_path"].format(lod=lod, variant="")


def _sidecar(asset, lod):
    return json.loads(_glb(asset, lod).with_suffix(".json").read_text())


def _glb_chunks(path):
    data = path.read_bytes()
    assert data[:4] == b"glTF"
    length = struct.unpack("<I", data[12:16])[0]
    j = json.loads(data[20:20 + length])
    return j, data[28 + length:]


def _glb_json(path):
    return _glb_chunks(path)[0]


def _accessor(j, blob, index):
    acc = j["accessors"][index]
    assert acc["componentType"] == 5126, acc
    width = {"VEC2": 2, "VEC3": 3}[acc["type"]]
    view = j["bufferViews"][acc["bufferView"]]
    stride = view.get("byteStride", 4 * width)
    base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    return [struct.unpack_from("<%df" % width, blob, base + i * stride)
            for i in range(acc["count"])]


def _indices(j, blob, index):
    acc = j["accessors"][index]
    fmt = {5123: "H", 5125: "I"}[acc["componentType"]]
    assert acc["type"] == "SCALAR"
    view = j["bufferViews"][acc["bufferView"]]
    size = struct.calcsize(fmt)
    stride = view.get("byteStride", size)
    base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    return [struct.unpack_from("<" + fmt, blob, base + i * stride)[0]
            for i in range(acc["count"])]


def _shell_volume(asset, lod):
    """Absolute signed volume of the shell triangles in the actual GLB."""
    j, blob = _glb_chunks(_glb(asset, lod))
    prim = j["meshes"][0]["primitives"][0]
    pos = _accessor(j, blob, prim["attributes"]["POSITION"])
    uv = _accessor(j, blob, prim["attributes"]["TEXCOORD_0"])
    indices = _indices(j, blob, prim["indices"])
    strip_row = {"closed_donut": 28, "closed_globe": 19}[asset]
    strip_v = (strip_row + 0.5) / 32.0
    volume, triangles = 0.0, 0
    for k in range(0, len(indices), 3):
        ids = indices[k:k + 3]
        if not all(abs(uv[i][1] - strip_v) < 1e-5 for i in ids):
            continue
        p0, p1, p2 = (pos[i] for i in ids)
        volume += (p0[0] * (p1[1] * p2[2] - p1[2] * p2[1])
                   - p0[1] * (p1[0] * p2[2] - p1[2] * p2[0])
                   + p0[2] * (p1[0] * p2[1] - p1[1] * p2[0])) / 6.0
        triangles += 1
    assert triangles == _sidecar(asset, lod)["parts"]["Shell"], (asset, lod, triangles)
    return abs(volume)


def _strips():
    block = LAND_BUILD[LAND_BUILD.index("STRIPS = ["):LAND_BUILD.index("STRIP_INDEX")]
    return [(m.group(1), m.group(2), None if m.group(3) == "None" else float(m.group(3))) for m in
            re.finditer(r'\("(\w+)", "(#[0-9A-F]{6})", (None|[\d.]+),', block)]


def _spec_rows():
    section = SPEC[SPEC.index("### 5.3 Closed-pen kit"):SPEC.index("**Yard picture-in-picture")]
    rows = {}
    for line in section.splitlines():
        m = re.match(r"\| (`\w+`(?:, `\w+`)*) \|", line)
        if not m:
            continue
        cells = [c.strip() for c in line.strip("|").split("|")]
        nums = [int(n.replace(",", "")) for n in re.split(r"\s*/\s*", cells[3])]
        budget = tuple(nums + [None] * (3 - len(nums)))
        hexes = [h.upper() for h in re.findall(r"#[0-9A-Fa-f]{6}", cells[2])]
        for aid in re.findall(r"`(\w+)`", cells[0]):
            if aid in KIT_IDS:
                rows[aid] = (hexes, budget)
    return rows


def _lab(hexc):
    def lin(c):
        c /= 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    r, g, b = (lin(int(hexc[i:i + 2], 16)) for i in (1, 3, 5))
    f = [(t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116) for t in
         ((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047,
          0.2126 * r + 0.7152 * g + 0.0722 * b,
          (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883)]
    return 116 * f[1] - 16, 500 * (f[0] - f[1]), 200 * (f[1] - f[2])


def _de(a, b):
    return math.dist(_lab(a), _lab(b))


def _read_png(path):
    data = path.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    pos, idat, w, h = 8, b"", 0, 0
    while pos < len(data):
        n = struct.unpack(">I", data[pos:pos + 4])[0]
        tag, body = data[pos + 4:pos + 8], data[pos + 8:pos + 8 + n]
        if tag == b"IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", body[:10])
            assert (depth, ctype) == (8, 6)
        elif tag == b"IDAT":
            idat += body
        pos += 12 + n
    raw, rows, stride = zlib.decompress(idat), [], 4 * w + 1
    for y in range(h):
        assert raw[y * stride] == 0, "filter 0 only"
        rows.append(raw[y * stride + 1:(y + 1) * stride])
    return w, h, rows


def test_manifest_and_spec_cover_exactly_the_kit():
    assert sorted(ASSETS) == sorted(KIT_IDS)
    assert sorted(_spec_rows()) == sorted(KIT_IDS)
    limits = dict(re.findall(r'"(\w+)": \((\d+, \d+, (?:\d+|None))\)', BUILD))
    for aid, asset in ASSETS.items():
        assert asset["build_script"] == "blender/build_closed_pens.py"
        assert asset["build_args"] == ["--asset", aid]
        assert asset["materials"] == ["LK_FacilityKit"]
        assert asset["godot_material"] == "godot/assets/models/facility_kit/LK_FacilityKit.tres"
        assert asset["atlas"] == "godot/assets/models/facility_kit/textures/facility_atlas.png"
        assert asset["spec_tolerance"] == 0.05
        budget = _spec_rows()[aid][1]
        built = tuple(None if v.strip() == "None" else int(v) for v in limits[aid].split(","))
        assert built == budget
        wanted = {lod: n for lod, n in zip(("LOD0", "LOD1", "LOD2"), budget) if n is not None}
        assert asset["tri_max"] == wanted and asset["lod_levels"] == list(wanted)


def test_manifest_dimensions_match_the_spec():
    assert ASSETS["closed_donut"]["spec"] == {
        "volume_m3": 22000.0, "outer_diameter_m": 50.0, "freeboard_m": 3.0}
    assert ASSETS["closed_globe"]["spec"] == {
        "volume_m3": 3500.0, "diameter_m": 18.8, "freeboard_m": 5.0}
    assert ASSETS["semi_closed_ring"]["spec"] == {
        "collar_outer_diameter_m": 40.0, "skirt_depth_m": 13.0}
    assert ASSETS["pump_module"]["spec"] == {"length_m": 6.0, "width_m": 3.0, "height_m": 3.0}


@pytest.mark.parametrize("aid", KIT_IDS)
def test_each_lod_is_one_mesh_and_one_kit_surface(aid):
    for lod in ASSETS[aid]["lod_levels"]:
        j = _glb_json(_glb(aid, lod))
        assert len(j["meshes"]) == 1 and len(j["meshes"][0]["primitives"]) == 1
        assert [m["name"] for m in j["materials"]] == ["LK_FacilityKit"]
        assert [n["name"] for n in j["nodes"]] == [aid]
        attrs = j["meshes"][0]["primitives"][0]["attributes"]
        assert {"POSITION", "NORMAL", "TEXCOORD_0", "COLOR_0"} <= set(attrs)
        side = _sidecar(aid, lod)
        assert side["material_count"] == 1 and side["surfaces"] == 1
        assert side["asset"] == aid and side["lod"] == lod


@pytest.mark.parametrize("aid", KIT_IDS)
def test_triangle_budgets_hold_and_lods_step_down(aid):
    tris = [_sidecar(aid, lod)["tri_count"] for lod in ASSETS[aid]["lod_levels"]]
    for lod, n in zip(ASSETS[aid]["lod_levels"], tris):
        assert n <= ASSETS[aid]["tri_max"][lod]
        assert n == ASSETS[aid]["tri_budget"][lod], (aid, lod, "stale tri_budget")
    assert all(a > b for a, b in zip(tris, tris[1:])), (aid, tris)


def test_verifier_passes_every_closed_pen_lod():
    for aid, asset in ASSETS.items():
        spec = pipeline.AssetSpec.from_dict(asset)
        for lod in spec.lod_levels:
            result = pipeline.validate_asset(spec, lod, None, _glb(aid, lod))
            assert result.passed, (aid, lod, result.errors)


def _near(got, want, tolerance=0.05):
    return abs(got - want) <= tolerance * want + 1e-6


@pytest.mark.parametrize("aid", KIT_IDS)
def test_dimensions_hold_at_every_lod(aid):
    for lod in ASSETS[aid]["lod_levels"]:
        d = _sidecar(aid, lod)["dimensions"]
        if aid == "closed_donut":
            assert _near(d["major_radius_m"], 17.0) and _near(d["tube_radius_m"], 8.1)
            assert _near(d["outer_diameter_m"], 50.0) and _near(d["freeboard_m"], 3.0)
        elif aid == "closed_globe":
            assert _near(d["diameter_m"], 18.8) and _near(d["freeboard_m"], 5.0)
        elif aid == "semi_closed_ring":
            assert _near(d["collar_outer_diameter_m"], 40.0)
            assert _near(d["skirt_depth_m"], 13.0)
        else:
            assert _near(d["length_m"], 6.0) and _near(d["width_m"], 3.0)
            assert _near(d["height_m"], 3.0) and 15.0 <= d["intake_depth_m"] <= 40.0


@pytest.mark.parametrize("aid,target", [("closed_donut", 22000.0), ("closed_globe", 3500.0)])
def test_real_glb_shell_volume_is_within_five_percent(aid, target):
    for lod in ASSETS[aid]["lod_levels"]:
        measured = _shell_volume(aid, lod)
        sidecar = _sidecar(aid, lod)["dimensions"]["mesh_volume_m3"]
        assert math.isclose(measured, sidecar, rel_tol=0.0, abs_tol=0.25), (aid, lod, measured, sidecar)
        assert _near(measured, target), (aid, lod, measured, target)


def test_generated_manifest_hashes_and_measurements_are_fresh():
    for aid in KIT_IDS:
        for lod in ASSETS[aid]["lod_levels"]:
            path = _glb(aid, lod)
            key = f"{aid}_{lod}"
            generated = GENERATED["assets"][key]
            side = _sidecar(aid, lod)
            assert generated["sha256"] == hashlib.sha256(path.read_bytes()).hexdigest(), key
            assert generated["size"] == path.stat().st_size
            assert generated["tri_count"] == side["tri_count"]
            assert generated["bbox_size"] == pytest.approx(side["bbox_size"], abs=1e-4)


def test_spec_swatches_and_the_two_atlas_rows_are_exact():
    strips = {name: hexc for name, hexc, _rough in _strips()}
    names = [name for name, _hexc, _rough in _strips()]
    assert names[26:30] == ["skylight", "crane_yellow", "donut_shell", "skirt_tarp"]
    assert strips["donut_shell"] == "#8E979C" and strips["skirt_tarp"] == "#C9CCC6"
    for aid in KIT_IDS:
        side = _sidecar(aid, "LOD0")
        wanted = ASSETS[aid]["spec_swatches"]
        assert wanted and set(wanted) <= set(side["swatches"])
        for part, spec_hex in wanted.items():
            sw = side["swatches"][part]
            assert spec_hex in _spec_rows()[aid][0]
            assert sw["spec"] == spec_hex and strips[sw["strip"]] == spec_hex
            assert _de(sw["hex"], spec_hex) <= 3.0
            assert side["part_strips"][part] == sw["strip"]
    w, _h, rows = _read_png(ATLAS)
    for row, hexc, rough in ((28, "#8E979C", 0.55), (29, "#C9CCC6", 0.70)):
        line = rows[row * 32 + 16]
        px = sorted((tuple(line[4 * x:4 * x + 3]) for x in range(w)), key=sum)
        median = "#%02X%02X%02X" % px[w // 2]
        assert _de(median, hexc) <= 3.0, (row, median)
        assert {line[4 * x + 3] for x in range(w)} == {round(rough * 255)}


@pytest.mark.parametrize("aid", KIT_IDS)
def test_godot_import_uses_the_kit_material_and_hand_made_lods(aid):
    for lod in ASSETS[aid]["lod_levels"]:
        text = Path(str(_glb(aid, lod)) + ".import").read_text()
        assert "meshes/generate_lods=false" in text
        block = re.search(r'"LK_FacilityKit": \{(.*?)\}', text, re.S)
        assert block and '"use_external/enabled": true' in block.group(1)
        assert '"use_external/path": "res://assets/models/facility_kit/LK_FacilityKit.tres"' in block.group(1)


def test_all_six_containment_levels_have_the_handover_mapping():
    cage = (GODOT / "scripts" / "environment" / "PenCage.gd").read_text()
    farm = (GODOT / "scripts" / "environment" / "FarmSite.gd").read_text()
    sim = (REPO / "src" / "LakskongenSim" / "Farm" / "ContainmentTech.cs").read_text()
    enum = re.search(r"public enum ContainmentLevel\s*\{(.*?)\}", sim, re.S).group(1)
    levels = re.findall(r"^\s*(\w+)\s*=\s*\d+,?", enum, re.M)
    assert levels == ["OpenNet", "Skirt", "SemiClosed", "ClosedFloat", "Submersible", "OffshoreClosed"]
    assert all(f'"{level}"' in cage for level in levels)
    for mapping in (
        '"SemiClosed": ["SemiClosedRing", "PumpModule"]',
        '"ClosedFloat": ["ClosedDonut", "PumpModule"]',
        '"Submersible": ["ClosedGlobe", "PumpModule"]',
        '"OffshoreClosed": ["ClosedGlobe", "PumpModule"]',
    ):
        assert mapping in cage
    assert 'set_containment(str(site.get("containment", "OpenNet")))' in farm
    assert "_rebuild_lod_multimeshes()" in farm


def test_evidence_records_acceptance_frames_and_the_ui_flow_gate():
    doc = EVIDENCE.read_text()
    for needle in ("## Acceptance", "byte-identical", "mesh volume", "LOD2",
                   "OpenNet", "OffshoreClosed", "UI-flow", "MIN_CHECKS"):
        assert needle in doc, needle
    images = re.findall(r"\]\((f2_30/[^)]+)\)", doc)
    assert len(images) >= 6
    for image in images:
        assert (EVIDENCE.parent / image).is_file(), image


def _blender():
    for candidate in (os.environ.get("BLENDER_BIN"), "/Applications/Blender.app/Contents/MacOS/Blender",
                      shutil.which("blender")):
        if candidate and Path(candidate).is_file():
            return candidate
    return None


def _build(tmp_path, *args):
    return subprocess.run(
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / "build_closed_pens.py"), "--",
         "--out", str(tmp_path), *args], capture_output=True, text=True, timeout=900)


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
@pytest.mark.parametrize("aid,lod", [("closed_donut", "LOD2"), ("closed_globe", "LOD2"),
                                     ("semi_closed_ring", "LOD0"), ("pump_module", "LOD1")])
def test_build_reproduces_committed_files_and_atlas(tmp_path, aid, lod):
    out = _build(tmp_path, "--asset", aid, "--lod", lod,
                 "--textures", "--atlas-out", str(tmp_path / "atlas.png"))
    assert out.returncode == 0, out.stderr[-2000:]
    assert (tmp_path / "atlas.png").read_bytes() == ATLAS.read_bytes()
    committed = _glb(aid, lod)
    rebuilt = tmp_path / committed.name
    new_side = json.loads(rebuilt.with_suffix(".json").read_text())
    old_side = _sidecar(aid, lod)
    ignore = {"blender_version"}
    assert {k: v for k, v in new_side.items() if k not in ignore} == {
        k: v for k, v in old_side.items() if k not in ignore}
    if new_side["blender_version"] == old_side["blender_version"]:
        assert hashlib.sha256(rebuilt.read_bytes()).digest() == hashlib.sha256(committed.read_bytes()).digest()


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
def test_build_stops_over_its_triangle_budget(tmp_path):
    out = _build(tmp_path, "--asset", "pump_module", "--lod", "LOD0",
                 "--no-bake", "--tri-limit-scale", "0.5")
    assert out.returncode != 0
    assert "pump_module LOD0 has 1080 triangles, over its 600 budget" in out.stdout + out.stderr


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
def test_build_refuses_the_pumps_unspecified_lod2(tmp_path):
    out = _build(tmp_path, "--asset", "pump_module", "--lod", "LOD2", "--no-bake")
    assert out.returncode != 0
    assert "pump_module has no LOD2" in out.stdout + out.stderr
