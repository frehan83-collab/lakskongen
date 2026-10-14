"""Build the fjord shore buildings (roadmap G2-02) and export glTF.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_shore_props.py -- [--asset naust|quay] [--out DIR]

  naust  A western Norwegian boathouse: a dry-stone base (sunk 2.5 m so it
         meets sloping ground), board walls, a gabled roof of dark slate
         with a 0.4 m overhang, a wide boat door in the gable that faces
         the water. 5 x 9 m, ridge 4.6 m over the base top. The walls use
         LK_NaustWall, which Godot paints red ochre or tar black per instance.
  quay   A small solid quay: a stone crib from 4 m under the sea to a
         concrete deck 1.6 m above it, 6 m wide, running 18 m out from the
         waterline plus 4 m into the land, with timber fenders along both
         sides and four bollards.

Axes: Blender +Y is the water side (glTF/Godot −Z): ShorePropPlacer turns
the model's −Z to the water. The origin is the base point (naust: the base
top at its uphill corner height; quay: the landward end at the waterline).

Each export writes <name>_LOD0.glb plus a <name>_LOD0.json sidecar
(tri_count, material_count, bbox_size), checked against
blender/asset_manifest.json by tests/test_terrain_material.py.

Licence: in-house geometry, no third-party assets.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lk_meshkit as kit  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(REPO, "godot", "assets", "models", "shore")
PREFIX = "LK_Shore"

MATERIALS = {
    "LK_DryStone": ("#77766F", 0.92, 0.0),
    "LK_NaustWall": ("#8E3B2A", 0.85, 0.0),
    "LK_Slate": ("#3B3F42", 0.7, 0.0),
    "LK_DoorDark": ("#2A211C", 0.8, 0.0),
    "LK_Concrete": ("#7C7E7A", 0.9, 0.0),
    "LK_Fender": ("#3F3328", 0.9, 0.0),
    "LK_Bollard": ("#2A2C2E", 0.5, 0.6),
}

NAUST_W = 5.0
NAUST_L = 9.0
BASE_TOP = 0.0
BASE_BOTTOM = -2.5
EAVES = 2.4
RIDGE = 4.6
OVERHANG = 0.4
GABLE_OVERHANG = 0.3


def quad(mb, a, b, c, d, mat):
    mb.face((mb.vert(a), mb.vert(b), mb.vert(c), mb.vert(d)), mat, smooth=False)


def tri(mb, a, b, c, mat):
    mb.face((mb.vert(a), mb.vert(b), mb.vert(c)), mat, smooth=False)


def build_naust():
    mb = kit.MeshBuilder()
    hw, hl = NAUST_W * 0.5, NAUST_L * 0.5
    # Dry-stone base: a box from deep in the ground to the floor level.
    kit.box(mb, (0, 0, (BASE_TOP + BASE_BOTTOM) * 0.5), (1, 0, 0), (0, 1, 0), (0, 0, 1),
            hw + 0.15, hl + 0.15, (BASE_TOP - BASE_BOTTOM) * 0.5, "LK_DryStone", skip_bottom=True)
    # Board walls up to the eaves.
    kit.box(mb, (0, 0, (BASE_TOP + EAVES) * 0.5), (1, 0, 0), (0, 1, 0), (0, 0, 1),
            hw, hl, (EAVES - BASE_TOP) * 0.5, "LK_NaustWall", skip_bottom=True)
    # Gable triangles at both ends.
    for y, flip in ((hl, False), (-hl, True)):
        a, b, c = (-hw, y, EAVES), (hw, y, EAVES), (0.0, y, RIDGE)
        if flip:
            tri(mb, b, a, c, "LK_NaustWall")
        else:
            tri(mb, a, b, c, "LK_NaustWall")
    # Roof: two slabs with an overhang (top and underside faces).
    ry0, ry1 = -hl - GABLE_OVERHANG, hl + GABLE_OVERHANG
    drop = (RIDGE - EAVES) / hw
    ex = hw + OVERHANG
    ez = RIDGE - drop * ex
    t = 0.12
    for side in (-1.0, 1.0):
        top = [(0.0, ry0, RIDGE + t), (0.0, ry1, RIDGE + t), (side * ex, ry1, ez + t), (side * ex, ry0, ez + t)]
        bot = [(0.0, ry0, RIDGE), (side * ex, ry0, ez), (side * ex, ry1, ez), (0.0, ry1, RIDGE)]
        if side > 0:
            quad(mb, *top, "LK_Slate")
            quad(mb, *bot, "LK_Slate")
        else:
            quad(mb, top[0], top[3], top[2], top[1], "LK_Slate")
            quad(mb, bot[0], bot[3], bot[2], bot[1], "LK_Slate")
        # Eave edge.
        e0, e1 = (side * ex, ry0, ez), (side * ex, ry1, ez)
        e2, e3 = (side * ex, ry1, ez + t), (side * ex, ry0, ez + t)
        if side > 0:
            quad(mb, e0, e1, e2, e3, "LK_Slate")
        else:
            quad(mb, e0, e3, e2, e1, "LK_Slate")
    # The boat door on the water gable (+Y), a little proud of the wall.
    dw, dh = 1.4, 2.1
    y = hl + 0.03
    quad(mb, (-dw, y, BASE_TOP + 0.05), (dw, y, BASE_TOP + 0.05), (dw, y, BASE_TOP + dh), (-dw, y, BASE_TOP + dh), "LK_DoorDark")
    # A small window high in the back gable.
    y = -hl - 0.03
    quad(mb, (0.45, y, 2.9), (-0.45, y, 2.9), (-0.45, y, 3.5), (0.45, y, 3.5), "LK_DoorDark")
    return mb


QUAY_W = 6.0
QUAY_OUT = 18.0
QUAY_IN = 4.0
DECK = 1.6
DECK_T = 0.45
CRIB_BOTTOM = -4.0


def build_quay():
    mb = kit.MeshBuilder()
    hw = QUAY_W * 0.5
    y0, y1 = -QUAY_IN, QUAY_OUT
    cy, hl = (y0 + y1) * 0.5, (y1 - y0) * 0.5
    # Stone crib to just under the deck, then the concrete deck slab.
    crib_top = DECK - DECK_T
    kit.box(mb, (0, cy, (crib_top + CRIB_BOTTOM) * 0.5), (1, 0, 0), (0, 1, 0), (0, 0, 1),
            hw - 0.1, hl - 0.1, (crib_top - CRIB_BOTTOM) * 0.5, "LK_DryStone", skip_bottom=True)
    kit.box(mb, (0, cy, DECK - DECK_T * 0.5), (1, 0, 0), (0, 1, 0), (0, 0, 1),
            hw, hl, DECK_T * 0.5, "LK_Concrete", skip_bottom=True)
    # Timber fenders along both sides and the head, from the deck to the water.
    for x in (-hw - 0.12, hw + 0.12):
        for k in range(7):
            y = 1.0 + k * (QUAY_OUT - 2.0) / 6.0
            kit.box(mb, (x, y, DECK * 0.5 - 0.6), (1, 0, 0), (0, 1, 0), (0, 0, 1),
                    0.12, 0.15, DECK * 0.5 + 0.6, "LK_Fender", skip_bottom=True)
    for k in range(3):
        x = -hw + 1.0 + k * (QUAY_W - 2.0) / 2.0
        kit.box(mb, (x, y1 + 0.12, DECK * 0.5 - 0.6), (1, 0, 0), (0, 1, 0), (0, 0, 1),
                0.15, 0.12, DECK * 0.5 + 0.6, "LK_Fender", skip_bottom=True)
    # Bollards.
    for (x, y) in ((-hw + 0.5, 3.0), (hw - 0.5, 3.0), (-hw + 0.5, QUAY_OUT - 1.5), (hw - 0.5, QUAY_OUT - 1.5)):
        kit.cylinder(mb, (x, y, DECK), (x, y, DECK + 0.55), 0.18, 8, "LK_Bollard", caps=True, smooth=True)
    return mb


ASSETS = {
    "naust": ("Naust", build_naust),
    "quay": ("Quay", build_quay),
}


def export(asset: str, mats: dict, out_dir: str) -> dict:
    kit.clear_prefixed(PREFIX)
    node, fn = ASSETS[asset]
    mb = fn()
    obj = mb.build(node, mats)
    obj["lk_build"] = PREFIX
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    path = os.path.join(out_dir, f"{asset}_LOD0.glb")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True, export_yup=True,
        export_texcoords=False, export_normals=True, export_materials="EXPORT",
        export_vertex_color="NONE", export_cameras=False, export_lights=False,
        export_extras=False,
    )
    used = sorted({s.material.name for s in obj.material_slots if s.material})
    lo, hi = kit.world_bbox([obj])
    bbox = [round(hi[0] - lo[0], 4), round(hi[2] - lo[2], 4), round(hi[1] - lo[1], 4)]
    data = {
        "asset": asset, "lod": "LOD0", "tri_count": kit.mesh_tris(obj), "material_count": len(used),
        "materials": used, "bbox_size": bbox, "file": os.path.basename(path),
        "blender_version": bpy.app.version_string,
    }
    with open(os.path.join(out_dir, f"{asset}_LOD0.json"), "w") as f:
        json.dump(data, f, indent=2, sort_keys=True)
        f.write("\n")
    print(f"TRIS: {data['tri_count']}")
    print(f"BBOX: {' '.join(str(v) for v in bbox)}")
    print(f"KIT_EXPORT {asset} LOD0 {path} tris={data['tri_count']}")
    kit.clear_prefixed(PREFIX)
    return data


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset", choices=list(ASSETS), default=None)
    ap.add_argument("--lod", default="LOD0")  # accepted for the pipeline CLI
    ap.add_argument("--out", default=OUT_DIR)
    ap.add_argument("--variant", default=None)  # accepted for the pipeline CLI
    return ap.parse_args(argv)


def main():
    args = parse_args()
    os.makedirs(args.out, exist_ok=True)
    mats = {n: kit.make_material(n, kit.hex_rgba(h), r, m) for n, (h, r, m) in MATERIALS.items()}
    for asset in ([args.asset] if args.asset else list(ASSETS)):
        export(asset, mats, args.out)


if __name__ == "__main__":
    main()
