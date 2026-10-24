"""Slaughterhouse kit (GRAPHICS_HANDOVER §2 Task A): the exported kit meets its manifest.

blender/build_slaughterhouse_kit.py writes a JSON sidecar next to every .glb.
These tests hold every piece to blender/asset_manifest.json: dimensions within
5%, every LOD inside its triangle ceiling, one LK_FacilityKit surface per LOD,
the swatches equal to the spec, the Godot import wired to the kit material,
the halls carrying their blank logo panel (D17), and the land kit's trim atlas
untouched (the kit adds no strip; harvest_hall and quay are reused).
"""

import json
import re
import struct
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
KIT = REPO / "godot" / "assets" / "models" / "slaughterhouse_kit"
MANIFEST = json.loads((REPO / "blender" / "asset_manifest.json").read_text())
ASSETS = {a["id"]: a for a in MANIFEST["assets"] if a.get("type") == "slaughterhouse_kit"}
KIT_IDS = ["slaughter_hall_s", "slaughter_hall_m", "reception_house", "chilled_loading_bay", "wellboat_intake"]
LODS = ["LOD0", "LOD1", "LOD2"]
LAND_BUILD = (REPO / "blender" / "build_land_base.py").read_text()
KIT_BUILD = (REPO / "blender" / "build_slaughterhouse_kit.py").read_text()


def _sidecar(asset, lod):
    return json.loads((KIT / f"{asset}_{lod}.json").read_text())


def _glb_json(path):
    data = path.read_bytes()
    assert data[:4] == b"glTF"
    length = struct.unpack("<I", data[12:16])[0]
    return json.loads(data[20:20 + length])


def test_manifest_lists_the_kit():
    assert sorted(ASSETS) == sorted(KIT_IDS)
    for aid, a in ASSETS.items():
        assert a["materials"] == ["LK_FacilityKit"] and a["spec_tolerance"] <= 0.05, aid
        assert a["build_script"] == "blender/build_slaughterhouse_kit.py", aid


@pytest.mark.parametrize("aid", KIT_IDS)
@pytest.mark.parametrize("lod", LODS)
def test_lod_meets_budget_material_and_dimensions(aid, lod):
    a, s = ASSETS[aid], _sidecar(aid, lod)
    assert s["tri_count"] <= a["tri_max"][lod], (aid, lod, s["tri_count"])
    assert s["tri_count"] == a["tri_budget"][lod], "manifest tri_budget is stale: rebuild and update"
    assert s["materials"] == ["LK_FacilityKit"] and s["surfaces"] == 1 and s["material_count"] == 1
    for key, want in a["spec"].items():
        got = s["dimensions"][key]
        assert abs(got - want) <= a["spec_tolerance"] * want + 1e-9, (aid, lod, key, got, want)
    j = _glb_json(KIT / f"{aid}_{lod}.glb")
    assert len(j["meshes"]) == 1 and len(j["meshes"][0]["primitives"]) == 1
    assert [m["name"] for m in j["materials"]] == ["LK_FacilityKit"]


@pytest.mark.parametrize("aid", KIT_IDS)
def test_swatches_match_spec(aid):
    s = _sidecar(aid, "LOD0")
    for part, want in ASSETS[aid]["spec_swatches"].items():
        sw = s["swatches"][part]
        assert sw["spec"] == want
        got = tuple(int(sw["hex"][k:k + 2], 16) for k in (1, 3, 5))
        exp = tuple(int(want[k:k + 2], 16) for k in (1, 3, 5))
        assert max(abs(g - e) for g, e in zip(got, exp)) <= 6, (aid, part, sw["hex"], want)


@pytest.mark.parametrize("aid", ["slaughter_hall_s", "slaughter_hall_m"])
@pytest.mark.parametrize("lod", ["LOD0", "LOD1"])
def test_halls_carry_a_blank_logo_panel(aid, lod):
    """No names on the models (D17): the halls' logo panel is the blank strip."""
    assert _sidecar(aid, lod)["part_strips"]["Logo"] == "panel_blank"


@pytest.mark.parametrize("aid", KIT_IDS)
@pytest.mark.parametrize("lod", LODS)
def test_godot_import_uses_the_kit_material(aid, lod):
    imp = (KIT / f"{aid}_{lod}.glb.import").read_text()
    assert f'source_file="res://assets/models/slaughterhouse_kit/{aid}_{lod}.glb"' in imp
    assert 'use_external/path": "res://assets/models/facility_kit/LK_FacilityKit.tres"' in imp
    assert "meshes/generate_lods=false" in imp


def test_kit_adds_no_atlas_strip_and_reuses_hall_and_quay():
    """Rows 0-27 of the shared atlas stay as they are; the Large tier and the
    berth are the land kit's harvest_hall and quay, not rebuilt here."""
    block = LAND_BUILD[LAND_BUILD.index("STRIPS = ["):LAND_BUILD.index("STRIP_INDEX")]
    names = re.findall(r'\(\"(\w+)\", \"#', block)
    used = {s for aid in KIT_IDS for lod in LODS for s in _sidecar(aid, lod)["part_strips"].values()}
    assert used <= set(names[:28]), used - set(names[:28])
    assert not re.search(r"^STRIPS\s*=|STRIPS\.(append|extend|insert)", KIT_BUILD, re.M)
    assert '"harvest_hall"' not in KIT_BUILD and '"quay"' not in KIT_BUILD
