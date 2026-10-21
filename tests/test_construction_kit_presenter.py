"""The construction kit on a land campus (F2-28's assets drawn by ConstructionKitVisual;
farm-types spec §5.3, §6.3).

godot/scripts/visual/ConstructionKitVisual.gd draws the kit from the engine's phase
keys alone. What it assumes of the kit is read here from the kit's own files, so
neither side moves without the other: the assets and LODs it loads are the ones
the manifest builds, each asset stands in the phase the manifest's
`construction_phase_sets` gives it, the ring is revealed through the tank's shell
height, the crane's base slab is the half-width it keeps off the pad's edge. The
presentation itself (frames per phase, the headless draw checks) is the evidence in
docs/run/evidence/construction_kit_presenter.md; that the new script only reads the
engine is held by tests/test_facility_presenter.py, which scans it with the rest of
the presenter.
"""

import json
import re
import struct
from pathlib import Path

import pytest

REPO = Path(__file__).parent.parent
GODOT = REPO / "godot"
VISUAL = GODOT / "scripts" / "visual"
KIT_DIR = GODOT / "assets" / "models" / "construction_kit"
KIT = (VISUAL / "ConstructionKitVisual.gd").read_text()
CAMPUS = (VISUAL / "LandCampusBuilder.gd").read_text()
MANAGER = (VISUAL / "FarmFacilityVisualManager.gd").read_text()
EVIDENCE = REPO / "docs" / "run" / "evidence" / "construction_kit_presenter.md"
MANIFEST = json.loads((REPO / "blender" / "asset_manifest.json").read_text())
ASSETS = {a["id"]: a for a in MANIFEST["assets"] if a.get("type") == "construction_kit"}
LAND_SETS = MANIFEST["construction_phase_sets"]["land"]


def _gd_list(name: str) -> list[str]:
    m = re.search(rf"^const {name} := \[(.*?)\]", KIT, re.M | re.S)
    assert m, name
    return re.findall(r'"([^"]+)"', m.group(1))


def _gd_dict_ints(name: str) -> dict[str, int]:
    m = re.search(rf"^const {name} := \{{(.*?)\}}", KIT, re.M | re.S)
    assert m, name
    return {k: int(v) for k, v in re.findall(r'"([^"]+)":\s*(\d+)', m.group(1))}


def _gd_const(text: str, name: str) -> float:
    m = re.search(rf"^const {name} := ([0-9.]+)", text, re.M)
    assert m, name
    return float(m.group(1))


def _function(text: str, name: str) -> str:
    """The body of a top-level function: from its `func` line to the next top-level line."""
    m = re.search(rf"^(?:static )?func {name}\(.*?(?=^\S)", text + "\nend", re.M | re.S)
    assert m, name
    return m.group(0)


def test_the_presenter_loads_the_kit_the_manifest_builds():
    assert set(_gd_list("ASSETS")) == set(ASSETS)
    counts = _gd_dict_ints("LOD_COUNT")
    assert set(counts) == set(ASSETS)
    for asset, n in counts.items():
        assert n == len(ASSETS[asset]["lod_levels"]), (asset, n, ASSETS[asset]["lod_levels"])
        for lod in range(n):
            assert (KIT_DIR / f"{asset}_LOD{lod}.glb").is_file(), (asset, lod)
    assert 'const DIR := "res://assets/models/construction_kit/"' in KIT


def test_each_asset_is_drawn_in_the_phase_the_manifest_gives_it():
    # The presenter's three predicates, each naming exactly one engine phase key.
    drawn_in = {"blasting": "site_works", "ring_up": "tank_rings", "crane_up": "treatment_hall"}
    for predicate, key in drawn_in.items():
        keys = set(re.findall(r'"([a-z_]+)"', _function(KIT, predicate))) - {"status", "building", "phase_key", "phase_progress"}
        assert keys == {key}, (predicate, keys)
    phases_of: dict[str, list[list[str]]] = {}
    for row in LAND_SETS.values():
        for asset in row["assets"]:
            phases_of.setdefault(asset, []).append(row["engine_phases"])
    assert phases_of["blast_mats"] == phases_of["excavator"] == [["site_works"]]
    assert phases_of["scaffold_ring"] == [["tank_rings"]]
    assert phases_of["tower_crane"] == [["treatment_hall"]]


def test_the_blast_ends_where_the_campus_ends_its_blast():
    # One definition of the blast's share: the campus's (the terrain's granite spreads over it).
    body = _function(KIT, "blasting")
    assert "LandCampusBuilder.BLAST_SHARE" in body
    assert not re.search(r"^const BLAST_SHARE", KIT, re.M)


def test_the_ring_is_revealed_through_the_tank_shell_it_stands_as_tall_as():
    assert 'set_instance_shader_parameter("build_height_m", LandCampusBuilder.GROWOUT_H)' in KIT
    assert _gd_const(CAMPUS, "GROWOUT_H") == ASSETS["scaffold_ring"]["spec"]["height_m"] == 13.0
    # Only the grow-out tank has a ring: its Ø29.5 m would float 7 m off a pre-grow tank.
    assert 'str(t["asset"]) == "tank_growout"' in KIT


def _crane_base_half_width() -> float:
    """Half the width of the crane's concrete base slab, from its LOD0 vertices below the mast's foot."""
    data = (KIT_DIR / "tower_crane_LOD0.glb").read_bytes()
    json_len = struct.unpack_from("<I", data, 12)[0]
    gltf = json.loads(data[20:20 + json_len])
    blob = data[20 + json_len + 8:]
    accessor = gltf["accessors"][gltf["meshes"][0]["primitives"][0]["attributes"]["POSITION"]]
    view = gltf["bufferViews"][accessor["bufferView"]]
    base = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    stride = view.get("byteStride", 12)
    foot = [struct.unpack_from("<3f", blob, base + i * stride) for i in range(accessor["count"])]
    # glTF is Y-up: the slab is the vertices at or under 0.7 m (its top) in the x/z plane, ballast aside.
    slab = [p for p in foot if p[1] <= 0.7 + 1e-4]
    return max(max(abs(p[0]), abs(p[2])) for p in slab)


def test_the_crane_keeps_its_base_slab_on_the_pad():
    half = _crane_base_half_width()
    assert half == pytest.approx(3.5, abs=0.05)  # the 7 m slab was found, not an empty selection
    assert _gd_const(KIT, "CRANE_BASE_HALF_M") >= half - 1e-3
    # The crane stands in the layout's gap behind a lot: its offset, base and edge margin fit
    # the pad's margin behind the last lot (the 30 m between lots is wider still).
    margin_u = float(re.search(r"^const LOT_MARGIN := Vector2\(([0-9.]+),", CAMPUS, re.M).group(1))
    reach = _gd_const(KIT, "CRANE_INLAND_M") + _gd_const(KIT, "CRANE_BASE_HALF_M") + _gd_const(KIT, "CRANE_EDGE_M")
    assert reach <= margin_u, (reach, margin_u)


def test_the_campus_hooks_the_kit_in():
    flat = re.sub(r"\s+", " ", CAMPUS)
    assert "static func kit_mesh(asset: String, lod: String, dir: String = KIT_DIR) -> Mesh:" in flat
    assert "construction = ConstructionKitVisual.new()" in flat and "construction.build(self)" in flat
    # The state follows the phases after the LOD bands are set, and the rings follow their tanks' bands.
    assert re.search(r"refresh_lods\(_camera_position\(\), true\)\s+# F2-28[^\n]*\n\s+if construction != null:\s+construction\.apply_state\(\)", CAMPUS)
    assert re.search(r"if construction != null:\s+construction\.refresh_rings\(\)", CAMPUS)
    # A lot's centre is recorded by both layouts (the crane stands off it).
    assert len(re.findall(r"lot_uv\.append\(", CAMPUS)) == 2
    assert "ConstructionKitVisual.paths()" in MANAGER


def test_the_script_has_its_godot_uid():
    uid = (VISUAL / "ConstructionKitVisual.gd.uid").read_text().strip()
    assert re.fullmatch(r"uid://[a-z0-9]+", uid), uid


def test_the_script_never_reaches_the_engine():
    code = "\n".join(line.split("#")[0] for line in KIT.splitlines())
    for forbidden in ("SimBridge", "GameBridge", "/root/", "Qa", "get_node"):
        assert forbidden not in code, forbidden


def test_the_evidence_doc_has_its_frames():
    doc = EVIDENCE.read_text()
    images = re.findall(r"\]\((construction_kit_presenter/[^)]+)\)", doc)
    assert len(images) >= 8
    for image in images:
        assert (EVIDENCE.parent / image).is_file(), image
    for needle in ("ConstructionKitVisual", "site_works", "tank_rings", "treatment_hall", "UI_FLOW_RESULT"):
        assert needle in doc, needle
