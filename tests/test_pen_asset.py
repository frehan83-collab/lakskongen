"""Hero pen and site kit (roadmap A2-01): the exported assets meet the spec.

The Blender build scripts write a JSON sidecar next to every .glb with the
triangle count, materials, bounding box and measured key dimensions; these
tests hold them to blender/asset_manifest.json (AKVA Polarcirkel 500-class
dimensions within 5%, LOD0 <= 60k and LOD2 <= 2k triangles), check that the
Godot import maps every glTF material to a project material, and, when
Blender is installed, that the scripts rebuild the committed files byte for
byte. None of them needs Godot.
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
MANIFEST = json.loads((REPO / "blender" / "asset_manifest.json").read_text())
ASSETS = {a["id"]: a for a in MANIFEST["assets"]}
KIT_IDS = ["pen_cage", "mooring_buoy", "mooring_light", "hose_float"]

sys.path.insert(0, str(REPO / ".claude" / "skills" / "ecc" / "blender-asset-pipeline"))
import blender_asset_pipeline as pipeline  # noqa: E402


def _glb(asset: dict, lod: str) -> Path:
    return REPO / asset["export_path"].format(lod=lod, variant="")


def _sidecar(asset: dict, lod: str) -> dict:
    return json.loads(_glb(asset, lod).with_suffix(".json").read_text())


def _within(value: float, spec, tol: float) -> bool:
    lo, hi = (spec, spec) if isinstance(spec, (int, float)) else spec
    return lo * (1 - tol) <= value <= hi * (1 + tol)


@pytest.mark.parametrize("asset_id", KIT_IDS)
def test_every_lod_is_exported_with_the_manifest_materials(asset_id):
    asset = ASSETS[asset_id]
    for lod in asset["lod_levels"]:
        assert _glb(asset, lod).is_file(), f"{asset_id} {lod} not exported"
        side = _sidecar(asset, lod)
        assert sorted(side["materials"]) == sorted(asset["materials"]), (asset_id, lod)
        assert side["material_count"] == len(asset["materials"])


def test_pen_triangle_budgets_hold():
    pen = ASSETS["pen_cage"]
    tris = {lod: _sidecar(pen, lod)["tri_count"] for lod in pen["lod_levels"]}
    # Roadmap A2-01: LOD0 <= 60k triangles per pen, LOD2 <= 2k.
    assert tris["LOD0"] <= 60000
    assert tris["LOD2"] <= 2000
    assert tris["LOD0"] > tris["LOD1"] > tris["LOD2"]
    for lod, ceiling in pen["tri_max"].items():
        assert tris[lod] <= ceiling


def test_pen_dimensions_match_the_polarcirkel_spec():
    pen = ASSETS["pen_cage"]
    dims = _sidecar(pen, "LOD0")["dimensions"]
    tol = pen["spec_tolerance"]
    assert tol <= 0.05
    for key, spec in pen["spec"].items():
        assert key in dims, f"sidecar lacks {key}"
        assert _within(dims[key], spec, tol), f"{key} = {dims[key]} outside {spec} +/- {tol:.0%}"
    # The whole pen's footprint is the collar (the feed hose runs out past it).
    side = _sidecar(pen, "LOD0")
    assert _within(dims["collar_outer_diameter_m"], 53.63, 0.05)
    assert side["bbox_size"][1] >= dims["net_depth_m"]


@pytest.mark.parametrize("asset_id", ["mooring_buoy", "mooring_light", "hose_float"])
def test_site_kit_bounding_boxes_match_the_spec(asset_id):
    asset = ASSETS[asset_id]
    bbox = _sidecar(asset, "LOD0")["bbox_size"]
    for got, want in zip(bbox, asset["spec"]["bbox_m"]):
        assert _within(got, want, asset["spec_tolerance"]), (asset_id, bbox)


@pytest.mark.parametrize("asset_id", KIT_IDS)
def test_godot_import_maps_every_material_to_a_project_material(asset_id):
    asset = ASSETS[asset_id]
    for lod in asset["lod_levels"]:
        text = Path(str(_glb(asset, lod)) + ".import").read_text()
        assert "meshes/generate_lods=false" in text, "hand-made LODs: no auto LOD"
        for name in asset["materials"]:
            block = re.search(r'"%s": \{(.*?)\}' % re.escape(name), text, re.S)
            assert block, f"{asset_id} {lod}: {name} has no external material"
            assert '"use_external/enabled": true' in block.group(1)
            path = re.search(r'"use_external/path": "res://([^"]+)"', block.group(1)).group(1)
            assert (REPO / "godot" / path).is_file(), path


def test_pen_textures_import_with_mipmaps():
    tex = REPO / "godot" / "assets" / "models" / "pen_cage" / "textures"
    for name in ("net_mesh.png", "walkway_albedo.png", "walkway_normal.png"):
        text = (tex / (name + ".import")).read_text()
        # The net's far-field veil is the mip chain's mean coverage.
        assert "mipmaps/generate=true" in text, name
    assert "compress/normal_map=1" in (tex / "walkway_normal.png.import").read_text()


def test_pen_scene_inherits_the_lod_glbs():
    scene = (REPO / "godot" / "assets" / "models" / "pen_cage" / "pen_cage.tscn").read_text()
    for lod in ("LOD0", "LOD1", "LOD2"):
        assert f"pen_cage_{lod}.glb" in scene
    assert '[node name="PenCage" instance=' in scene
    # The primitive placeholder meshes are gone for good.
    assert 'type="Mesh"' not in scene


def test_verifier_rejects_a_pen_over_its_triangle_ceiling(tmp_path, monkeypatch):
    monkeypatch.setattr(pipeline, "REPO_ROOT", tmp_path)
    spec = pipeline.AssetSpec.from_dict(dict(ASSETS["pen_cage"], tri_budget={"LOD0": 70000}))
    glb = tmp_path / "pen_cage_LOD0.glb"
    glb.write_bytes(b"glTF")
    glb.with_suffix(".json").write_text(json.dumps(
        {"tri_count": 70000, "material_count": len(spec.materials), "bbox_size": [1, 1, 1]}))
    result = pipeline.validate_asset(spec, "LOD0", None, glb)
    assert not result.passed
    assert any("ceiling 60000" in e for e in result.errors)


def _blender() -> str | None:
    for candidate in (os.environ.get("BLENDER_BIN"), "/Applications/Blender.app/Contents/MacOS/Blender",
                      shutil.which("blender")):
        if candidate and Path(candidate).is_file():
            return candidate
    return None


@pytest.mark.skipif(_blender() is None, reason="Blender is not installed")
@pytest.mark.parametrize("script,asset_id,args", [
    ("build_pen.py", "pen_cage", ["--lod", "LOD2"]),
    ("build_site_kit.py", "mooring_light", ["--asset", "mooring_light", "--lod", "LOD0"]),
    ("build_site_kit.py", "hose_float", ["--asset", "hose_float", "--lod", "LOD0"]),
])
def test_build_scripts_reproduce_the_committed_assets(tmp_path, script, asset_id, args):
    out = subprocess.run(
        [_blender(), "-b", "--factory-startup", "--python", str(REPO / "blender" / script), "--",
         *args, "--out", str(tmp_path)],
        capture_output=True, text=True, timeout=600)
    assert out.returncode == 0, out.stderr[-2000:]
    lod = args[args.index("--lod") + 1]
    committed = _glb(ASSETS[asset_id], lod)
    rebuilt = tmp_path / committed.name
    new_side = json.loads(rebuilt.with_suffix(".json").read_text())
    old_side = _sidecar(ASSETS[asset_id], lod)
    # The geometry must match whatever Blender rebuilds it ...
    geometry = ("tri_count", "material_count", "materials", "bbox_size", "parts", "dimensions")
    assert {k: new_side.get(k) for k in geometry} == {k: old_side.get(k) for k in geometry}
    # ... and the bytes too when it is the Blender that built the committed file
    # (the exporter writes its own version into the glTF).
    if new_side.get("blender_version") == old_side.get("blender_version"):
        assert hashlib.sha256(rebuilt.read_bytes()).hexdigest() == hashlib.sha256(committed.read_bytes()).hexdigest()
