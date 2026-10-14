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
VESSELS = ["workboat", "feed_barge", "feed_barge_large", "wellboat", "wellboat_large", "rib"]
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
