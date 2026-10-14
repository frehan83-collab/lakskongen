"""Vessels (roadmap A3-01): the exported fleet meets the spec.

blender/build_vessels.py writes a JSON sidecar next to every .glb with the
triangle count, materials, bounding box and the measured hull dimensions;
these tests hold them to blender/asset_manifest.json: feed barges 37 x 11 m
and 64.8 x 13 m, wellboats 77 x 17.8 m and 116 x 23 m, the workboat
15 x 10 m (all within 5%), every LOD inside its triangle ceiling, the
wellboat longer than a pen is wide, and job state shown as a UI chip, never
as hull colour. When Blender is installed, the build script must rebuild the
committed files. None of them needs Godot.
"""

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
MANIFEST = json.loads((REPO / "blender" / "asset_manifest.json").read_text())
ASSETS = {a["id"]: a for a in MANIFEST["assets"]}
VESSELS = ["workboat", "feed_barge", "feed_barge_large", "wellboat", "wellboat_large", "rib",
           # Farm-types spec §5.2, item F2-27: the offshore fleet's surface vessels.
           "offshore_service", "crew_boat"]
# F2-27 subsea props (two LODs, no deck): checked on their own below.
SUBSEA = ["rov", "auv"]
MANAGERS = [GODOT / "scripts" / "visual" / "WellboatVisualManager.gd",
            GODOT / "scripts" / "visual" / "FeedBargeVisualManager.gd"]

sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "blender-asset-pipeline"))
import blender_asset_pipeline as pipeline  # noqa: E402


def _glb(asset: dict, lod: str) -> Path:
    return REPO / asset["export_path"].format(lod=lod, variant="")


def _sidecar(asset: dict, lod: str) -> dict:
    return json.loads(_glb(asset, lod).with_suffix(".json").read_text())


def _within(value: float, spec: float, tol: float) -> bool:
    return spec * (1 - tol) <= value <= spec * (1 + tol)


def test_the_manifest_lists_the_whole_fleet():
    for vid in VESSELS:
        assert vid in ASSETS, vid
        assert ASSETS[vid]["type"] == "vessel"
        assert ASSETS[vid]["build_script"] == "blender/build_vessels.py"


@pytest.mark.parametrize("vid", VESSELS)
def test_every_lod_is_exported_with_its_manifest_materials(vid):
    asset = ASSETS[vid]
    spec = pipeline.AssetSpec.from_dict(asset)
    for lod in asset["lod_levels"]:
        assert _glb(asset, lod).is_file(), f"{vid} {lod} not exported"
        side = _sidecar(asset, lod)
        assert sorted(side["materials"]) == sorted(spec.materials_for(lod)), (vid, lod)
        assert side["material_count"] == len(spec.materials_for(lod))
    # Every LOD keeps the hull, deck, superstructure, glass and crane.
    for lod in asset["lod_levels"]:
        parts = set(_sidecar(asset, lod)["parts"])
        need = {"Hull", "Deck", "Superstructure"} | ({"Windows", "Crane"} if vid != "rib" else set())
        assert need <= parts, (vid, lod, parts)


@pytest.mark.parametrize("vid", VESSELS)
def test_hull_dimensions_match_the_spec(vid):
    # A3-01 accept 1: barge 37 x 11 and 64.8 x 13, wellboat 77 x 17.8 and
    # 116 x 23, workboat 15 x 10 (moulded hull, every LOD).
    asset = ASSETS[vid]
    tol = asset["spec_tolerance"]
    assert tol <= 0.05
    for lod in asset["lod_levels"]:
        dims = _sidecar(asset, lod)["dimensions"]
        assert _within(dims["length_m"], asset["spec"]["length_m"], tol), (vid, lod, dims)
        assert _within(dims["beam_m"], asset["spec"]["beam_m"], tol), (vid, lod, dims)


def test_roadmap_dimensions_are_the_spec():
    want = {"feed_barge": (37.0, 11.0), "feed_barge_large": (64.8, 13.0), "wellboat": (77.0, 17.8),
            "wellboat_large": (116.0, 23.0), "workboat": (15.0, 10.0)}
    for vid, (length, beam) in want.items():
        assert (ASSETS[vid]["spec"]["length_m"], ASSETS[vid]["spec"]["beam_m"]) == (length, beam)


@pytest.mark.parametrize("vid", VESSELS)
def test_triangle_budgets_hold(vid):
    # A3-01 accept 4.
    asset = ASSETS[vid]
    tris = {lod: _sidecar(asset, lod)["tri_count"] for lod in asset["lod_levels"]}
    assert tris["LOD0"] > tris["LOD1"] > tris["LOD2"] > 0
    assert set(asset["tri_max"]) == set(asset["lod_levels"])
    for lod, ceiling in asset["tri_max"].items():
        assert tris[lod] <= ceiling, (vid, lod, tris[lod], ceiling)
    # Far LODs stay cheap: a whole fleet at COAST zoom costs a few thousand.
    assert tris["LOD2"] <= 2500


def test_the_wellboat_is_longer_than_a_pen_is_wide():
    # A3-01 accept 3.
    pen = json.loads((GODOT / "assets/models/pen_cage/pen_cage_LOD0.json").read_text())
    collar = pen["dimensions"]["collar_outer_diameter_m"]
    for vid in ("wellboat", "wellboat_large"):
        assert _sidecar(ASSETS[vid], "LOD0")["dimensions"]["length_m"] > 1.3 * collar


@pytest.mark.parametrize("vid", VESSELS)
def test_bow_points_along_godot_minus_z(vid):
    # Blender +Y is the bow and becomes Godot -Z: the way a PathFollow3D faces.
    dims = _sidecar(ASSETS[vid], "LOD0")["dimensions"]
    assert dims["bow_y_m"] > 0 > dims["stern_y_m"]
    assert abs(dims["bow_y_m"] - dims["stern_y_m"] - dims["length_m"]) < 0.01
    assert dims["draught_m"] > 0 and dims["air_draught_m"] > dims["draught_m"]


@pytest.mark.parametrize("vid", VESSELS)
def test_godot_import_maps_every_material_to_the_vessel_kit(vid):
    asset = ASSETS[vid]
    spec = pipeline.AssetSpec.from_dict(asset)
    for lod in asset["lod_levels"]:
        text = Path(str(_glb(asset, lod)) + ".import").read_text()
        assert "meshes/generate_lods=false" in text, "hand-made LODs: no auto LOD"
        for name in spec.materials_for(lod):
            block = re.search(r'"%s": \{(.*?)\}' % re.escape(name), text, re.S)
            assert block, f"{vid} {lod}: {name} has no external material"
            assert '"use_external/enabled": true' in block.group(1)
            path = re.search(r'"use_external/path": "res://([^"]+)"', block.group(1)).group(1)
            assert path.startswith("assets/models/vessel_kit/"), path
            tres = (GODOT / path).read_text()
            assert 'path="res://shaders/vessel_paint.gdshader"' in tres, path


@pytest.mark.parametrize("vid", VESSELS)
def test_scene_inherits_the_lod_glbs_and_knows_its_size(vid):
    asset = ASSETS[vid]
    scene = (REPO / asset["godot_scene"]).read_text()
    for lod in ("LOD0", "LOD1", "LOD2"):
        assert f"{vid}_{lod}.glb" in scene
    assert "scripts/visual/VesselModel.gd" in scene
    assert f'asset_id = "{vid}"' in scene
    dims = _sidecar(asset, "LOD0")["dimensions"]
    length = float(re.search(r"length_m = ([\d.]+)", scene).group(1))
    beam = float(re.search(r"beam_m = ([\d.]+)", scene).group(1))
    assert (length, beam) == (dims["length_m"], dims["beam_m"])


def test_hull_paint_has_the_waterline_and_no_job_colour():
    for stem in ("red", "orange", "slate"):
        tres = (GODOT / f"assets/models/vessel_kit/vessel_hull_{stem}_material.tres").read_text()
        assert "shader_parameter/waterline_paint = true" in tres
    shader = (GODOT / "shaders/vessel_paint.gdshader").read_text()
    assert "instance uniform" not in shader, "per-boat paint would let job state onto a hull"


@pytest.mark.parametrize("manager", MANAGERS, ids=lambda p: p.stem)
def test_job_state_is_a_chip_never_hull_colour(manager):
    # A3-01 accept 2 (grep): the managers never tint a boat; they hang a
    # VesselChip over it.
    code = re.sub(r"#.*", "", manager.read_text())
    assert "tint_deck(" not in code
    assert "albedo_color" not in code
    assert "material_override" not in code
    assert "set_instance_shader_parameter" not in code
    assert "VesselChip.new(" in code


def test_the_service_boat_is_retired():
    assert not (GODOT / "assets/models/boat/service_boat.tscn").exists()
    hits = []
    for path in list(GODOT.rglob("*.gd")) + list(GODOT.rglob("*.tscn")):
        if ".godot" in path.parts:
            continue
        if "assets/models/boat/" in path.read_text(errors="ignore"):
            hits.append(str(path.relative_to(REPO)))
    assert not hits, hits


def test_vessel_chip_strings_are_translated():
    rows = {}
    with (GODOT / "translations/translations.csv").open(encoding="utf-8") as f:
        import csv
        for row in csv.reader(f):
            if row:
                rows[row[0]] = row
    keys = set()
    for path in MANAGERS:
        keys |= set(re.findall(r'"(UI_VESSEL_[A-Z_]+)"', path.read_text()))
    assert len(keys) >= 9, keys
    for key in keys:
        assert key in rows, key
        en, nb = rows[key][1], rows[key][2]
        assert en and nb and nb != key, key
        assert en.count("%") == nb.count("%"), key


def test_vessel_textures_import_with_mipmaps():
    tex = GODOT / "assets/models/vessel_kit/textures"
    for name in ("vessel_weathering.png", "vessel_plates_normal.png"):
        assert (tex / name).is_file()
        assert "mipmaps/generate=true" in (tex / (name + ".import")).read_text(), name
    assert "compress/normal_map=1" in (tex / "vessel_plates_normal.png.import").read_text()


def test_verifier_checks_each_lods_own_material_list(tmp_path, monkeypatch):
    monkeypatch.setattr(pipeline, "REPO_ROOT", tmp_path)
    spec = pipeline.AssetSpec.from_dict(ASSETS["wellboat"])
    lod2 = spec.materials_for("LOD2")
    assert len(lod2) < len(spec.materials)
    glb = tmp_path / "wellboat_LOD2.glb"
    glb.write_bytes(b"glTF")
    side = glb.with_suffix(".json")
    side.write_text(json.dumps({"tri_count": spec.tri_budget["LOD2"], "material_count": len(lod2),
                                "bbox_size": [1, 1, 1]}))
    assert pipeline.validate_asset(spec, "LOD2", None, glb).passed
    side.write_text(json.dumps({"tri_count": spec.tri_budget["LOD2"], "material_count": len(spec.materials),
                                "bbox_size": [1, 1, 1]}))
    assert not pipeline.validate_asset(spec, "LOD2", None, glb).passed
    side.write_text(json.dumps({"tri_count": 2600, "material_count": len(lod2), "bbox_size": [1, 1, 1]}))
    over = pipeline.AssetSpec.from_dict(dict(ASSETS["wellboat"], tri_budget={"LOD2": 2600}))
    result = pipeline.validate_asset(over, "LOD2", None, glb)
    assert any("ceiling 2000" in e for e in result.errors)


def _blender() -> str | None:
    for candidate in (os.environ.get("BLENDER_BIN"), "/Applications/Blender.app/Contents/MacOS/Blender",
                      shutil.which("blender")):
        if candidate and Path(candidate).is_file():
            return candidate
    return None


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
@pytest.mark.parametrize("vid,lod", [("workboat", "LOD2"), ("wellboat", "LOD1")])
def test_build_script_reproduces_the_committed_vessels(tmp_path, vid, lod):
    out = subprocess.run(
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / "build_vessels.py"), "--",
         "--asset", vid, "--lod", lod, "--out", str(tmp_path)],
        capture_output=True, text=True, timeout=600)
    assert out.returncode == 0, out.stderr[-2000:]
    committed = _glb(ASSETS[vid], lod)
    rebuilt = tmp_path / committed.name
    new_side = json.loads(rebuilt.with_suffix(".json").read_text())
    old_side = _sidecar(ASSETS[vid], lod)
    geometry = ("tri_count", "material_count", "materials", "bbox_size", "parts", "dimensions")
    assert {k: new_side.get(k) for k in geometry} == {k: old_side.get(k) for k in geometry}
    if new_side.get("blender_version") == old_side.get("blender_version"):
        assert hashlib.sha256(rebuilt.read_bytes()).hexdigest() == hashlib.sha256(committed.read_bytes()).hexdigest()


# ── Offshore fleet (farm-types spec §5.2, item F2-27) ─────────────────────────

SPEC_TEXT = (REPO / "docs" / "FARM_TYPES_SPEC.md").read_text()
BUILD_VESSELS = (REPO / "blender" / "build_vessels.py").read_text()


def _spec_row(asset_id: str) -> list[str]:
    line = next(l for l in SPEC_TEXT.splitlines() if l.startswith(f"| `{asset_id}`"))
    return [c.strip() for c in line.strip("|").split("|")]


def _budgets(cell: str) -> list[tuple[int, ...]]:
    """'30,000 / 6,000 / 800' -> [(30000, 6000, 800)]; 'a / b · c / d' -> two."""
    return [tuple(int(n.replace(",", "")) for n in re.split(r"\s*/\s*", part.strip())) for part in cell.split("·")]


def _gltf_nodes(path: Path) -> list[str]:
    data = path.read_bytes()
    length = int.from_bytes(data[12:16], "little")
    return [n["name"] for n in json.loads(data[20:20 + length])["nodes"]]


def test_offshore_fleet_triangle_budgets_are_the_spec_rows():
    # F2-27 accept 3: the build's TRI_LIMITS and the manifest equal spec §5.2.
    rows = {"offshore_service": _budgets(_spec_row("offshore_service")[-1])[0],
            "crew_boat": _budgets(_spec_row("crew_boat")[-1])[0]}
    rov, auv = _budgets(_spec_row("rov")[-1])
    rows["rov"], rows["auv"] = rov, auv
    tri_limits = BUILD_VESSELS[BUILD_VESSELS.index("TRI_LIMITS = {"):]
    tri_limits = tri_limits[:tri_limits.index("\n}\n")]
    for vid, want in rows.items():
        limits = re.search(r'"%s": \(([^)]*)\)' % vid, tri_limits).group(1)
        built = tuple(int(v) for v in limits.split(",") if v.strip() != "None")
        assert built == want, (vid, built, want)
        asset = ASSETS[vid]
        assert tuple(asset["tri_max"][lod] for lod in asset["lod_levels"]) == want, vid
        for lod in asset["lod_levels"]:
            assert _sidecar(asset, lod)["tri_count"] <= asset["tri_max"][lod], (vid, lod)


@pytest.mark.parametrize("vid,length,beam", [("offshore_service", 75.0, 17.0), ("crew_boat", 20.0, 7.0)])
def test_offshore_fleet_dimensions_are_the_spec(vid, length, beam):
    # F2-27 accept 1: service vessel 75 x 17 and crew boat 20 x 7 m ± 5%.
    cells = _spec_row(vid)
    assert f"{int(length)} × {int(beam)}" in cells[2]
    assert (ASSETS[vid]["spec"]["length_m"], ASSETS[vid]["spec"]["beam_m"]) == (length, beam)
    for lod in ASSETS[vid]["lod_levels"]:
        dims = _sidecar(ASSETS[vid], lod)["dimensions"]
        assert _within(dims["length_m"], length, 0.05) and _within(dims["beam_m"], beam, 0.05), (vid, lod, dims)


def test_the_service_vessel_is_shorter_than_of1_and_has_its_gangway():
    # F2-27 accept 4: visibly shorter than the OF1-class ring's diameter, and
    # the walk-to-work gangway is its own node.
    of1 = json.loads((GODOT / "assets/models/offshore/of1_class_LOD0.json").read_text())
    diameter = 2.0 * of1["dimensions"]["circumradius_m"]
    length = _sidecar(ASSETS["offshore_service"], "LOD0")["dimensions"]["length_m"]
    assert length <= 0.75 * diameter, (length, diameter)
    assert "Gangway" in _gltf_nodes(_glb(ASSETS["offshore_service"], "LOD0"))
    side = _sidecar(ASSETS["offshore_service"], "LOD0")
    assert side["parts"]["Gangway"] > 0
    # The node turns about the tower axis (F2-22 slews it to the unit's
    # W2WLanding): its origin is the pivot, on the deck inside the hull.
    data = _glb(ASSETS["offshore_service"], "LOD0").read_bytes()
    nodes = json.loads(data[20:20 + int.from_bytes(data[12:16], "little")])["nodes"]
    gangway = next(n for n in nodes if n["name"] == "Gangway")
    pivot = side["pivots"]["Gangway"]
    assert [round(v, 3) for v in gangway["translation"]] == pivot
    assert abs(pivot[0]) < side["dimensions"]["beam_m"] / 2 and 0.0 < pivot[1] < 5.0
    assert abs(pivot[2]) < side["dimensions"]["length_m"] / 2
    scene = (GODOT / "assets/models/offshore_service/offshore_service.tscn").read_text()
    switch = float(re.search(r"lod_switch = Vector2\(([\d.]+),", scene).group(1))
    block = scene[scene.index('[node name="Gangway" parent="."]'):]
    assert float(re.search(r"visibility_range_end = ([\d.]+)", block).group(1)) == switch
    # LOD1/LOD2 fold the gangway into the superstructure.
    for lod in ("LOD1", "LOD2"):
        assert "Gangway" not in _gltf_nodes(_glb(ASSETS["offshore_service"], lod))


def test_the_anchor_tug_is_a_swatch_variant_with_no_new_meshes():
    # F2-27 accept 2.
    tug = (GODOT / "assets/models/offshore_service/anchor_tug.tscn").read_text()
    exts = re.findall(r'\[ext_resource type="(\w+)" path="([^"]+)"', tug)
    assert sorted(exts) == [("Material", "res://assets/models/vessel_kit/vessel_hull_red_material.tres"),
                            ("PackedScene", "res://assets/models/offshore_service/offshore_service.tscn")]
    assert ".glb" not in tug and "sub_resource" not in tug and "Mesh" not in tug
    for parent in (".", "LOD1", "LOD2"):
        assert f'[node name="Hull" parent="{parent}"]\nsurface_material_override/0 = ExtResource(' in tug, parent
    assert not list((GODOT / "assets/models").rglob("anchor_tug*.glb"))
    assert "no new geometry" in _spec_row("anchor_tug")[3]


@pytest.mark.parametrize("vid", SUBSEA)
def test_subsea_props_meet_the_spec(vid):
    asset = ASSETS[vid]
    assert asset["type"] == "subsea" and asset["build_script"] == "blender/build_vessels.py"
    assert asset["lod_levels"] == ["LOD0", "LOD1"] and asset["spec_tolerance"] <= 0.05
    spec = pipeline.AssetSpec.from_dict(asset)
    tris = []
    for lod in asset["lod_levels"]:
        side = _sidecar(asset, lod)
        assert sorted(side["materials"]) == sorted(spec.materials_for(lod)), (vid, lod)
        dims = side["dimensions"]
        assert _within(dims["length_m"], asset["spec"]["length_m"], 0.05), (vid, lod, dims)
        assert _within(dims["beam_m"], asset["spec"]["beam_m"], 0.05), (vid, lod, dims)
        if "height_m" in asset["spec"]:
            assert _within(side["bbox_size"][1], asset["spec"]["height_m"], 0.05), (vid, lod, side["bbox_size"])
        tris.append(side["tri_count"])
        assert pipeline.validate_asset(spec, lod, None, _glb(asset, lod)).passed, (vid, lod)
    assert tris[0] > tris[1] > 0
    swatch = {"rov": ("LK_VSubseaYellow", "#E6BE2E"), "auv": ("LK_VSubseaOrange", "#C9531F")}[vid]
    assert swatch[0] in _sidecar(asset, "LOD0")["materials"] and swatch[1] in _spec_row("rov")[3]
    hexc = re.search(r'"%s": \("(#[0-9A-F]{6})"' % swatch[0], BUILD_VESSELS).group(1)
    assert hexc == swatch[1]


def test_subsea_paint_has_no_waterline_and_the_white_hull_has_one():
    for stem in ("subsea_yellow", "subsea_orange"):
        tres = (GODOT / f"assets/models/vessel_kit/vessel_{stem}_material.tres").read_text()
        assert "waterline_paint = true" not in tres and 'path="res://shaders/vessel_paint.gdshader"' in tres
    white = (GODOT / "assets/models/vessel_kit/vessel_hull_white_material.tres").read_text()
    assert "shader_parameter/waterline_paint = true" in white
    for vid in SUBSEA:
        for lod in ASSETS[vid]["lod_levels"]:
            text = Path(str(_glb(ASSETS[vid], lod)) + ".import").read_text()
            assert "meshes/generate_lods=false" in text
            for name in pipeline.AssetSpec.from_dict(ASSETS[vid]).materials_for(lod):
                assert f'"{name}": {{' in text, (vid, lod, name)


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
@pytest.mark.parametrize("vid,lod", [("crew_boat", "LOD2"), ("rov", "LOD1")])
def test_build_script_reproduces_the_committed_offshore_fleet(tmp_path, vid, lod):
    out = subprocess.run(
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / "build_vessels.py"), "--",
         "--asset", vid, "--lod", lod, "--out", str(tmp_path)],
        capture_output=True, text=True, timeout=600)
    assert out.returncode == 0, out.stderr[-2000:]
    committed = _glb(ASSETS[vid], lod)
    rebuilt = tmp_path / committed.name
    new_side = json.loads(rebuilt.with_suffix(".json").read_text())
    old_side = _sidecar(ASSETS[vid], lod)
    assert {k: v for k, v in new_side.items() if k != "blender_version"} == \
        {k: v for k, v in old_side.items() if k != "blender_version"}
    if new_side["blender_version"] == old_side["blender_version"]:
        assert hashlib.sha256(rebuilt.read_bytes()).hexdigest() == hashlib.sha256(committed.read_bytes()).hexdigest()
