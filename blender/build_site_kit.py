"""Build the salmon-site mooring kit and export glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_site_kit.py -- [--asset mooring_buoy|mooring_light]
      [--lod LOD0|LOD1|LOD2] [--out DIR]

Pieces (Godot assembles them into a frame mooring with MooringGrid.gd):
  mooring_buoy   grid buoy that floats over each frame coupling plate: a
                 moulded HDPE barrel, 1.25 m across and 1.6 m tall, riding
                 ~0.6 m out of the water, with a galvanised lifting eye on
                 top and a chain eye below. Grey or orange (instance colour).
  mooring_light  the synchronised yellow corner light (#FFC23A) that sits on
                 the corner buoys: a short mast, a lantern base and the lens.
  hose_float     the small float that carries the feed hoses (roadmap G2-04,
                 owner reference 1: yellow floats along the white hoses): a
                 0.7 m oval HDPE float riding half out of the water, with a
                 black strap and a saddle the hoses lie in. Buoy yellow
                 (instance colour).

Each export writes <name>_<LOD>.glb plus a <name>_<LOD>.json sidecar
(tri_count, material_count, bbox_size) for blender_asset_pipeline.py.
Node names: Buoy, CornerLight, HoseFloat. Materials map to Godot .tres files
through the .glb.import "use_external" settings: LK_Buoy, LK_Steel, LK_HDPE,
LK_Lamp.

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
from lk_meshkit import TAU  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(REPO, "godot", "assets", "models", "site_kit")
PREFIX = "LK_SiteKit"

MATERIALS = {
    "LK_Buoy": ("#8E979C", 0.55, 0.0),
    "LK_Steel": ("#8A8F93", 0.45, 0.8),
    "LK_HDPE": ("#1C1E20", 0.6, 0.0),
    "LK_Lamp": ("#FFC23A", 0.3, 0.0),
}

BUOY_R = 0.625
BUOY_TOP = 0.62      # height above the waterline
BUOY_BOTTOM = -1.0

LODS = {
    "LOD0": dict(around=24, eye_sides=8, eye_segs=12, rib=True),
    "LOD1": dict(around=10, eye_sides=4, eye_segs=6, rib=False),
    "LOD2": dict(around=5, eye_sides=3, eye_segs=3, rib=False),
}


def buoy_profile(rib: bool):
    """(r, z) from the top down: rounded shoulders, two moulded ribs."""
    pts = [(0.0, BUOY_TOP), (0.32, BUOY_TOP - 0.02), (0.52, BUOY_TOP - 0.10), (0.61, BUOY_TOP - 0.24)]
    if rib:
        pts += [(BUOY_R, 0.20), (BUOY_R + 0.025, 0.14), (BUOY_R + 0.025, 0.06), (BUOY_R, 0.0),
                (BUOY_R, -0.40), (BUOY_R + 0.025, -0.46), (BUOY_R + 0.025, -0.54), (BUOY_R, -0.60)]
    else:
        pts += [(BUOY_R, -0.6)]
    pts += [(0.55, BUOY_BOTTOM + 0.16), (0.30, BUOY_BOTTOM + 0.03), (0.0, BUOY_BOTTOM)]
    return pts


def eye(mb, z, r_ring, r_tube, sides, segs, mat, up=True):
    """A lifting eye: a ring standing in the XZ plane on a short stem."""
    pts = []
    for i in range(segs + 1):
        a = math.pi * i / segs if up else math.pi + math.pi * i / segs
        pts.append((r_ring * math.cos(a), 0.0, z + (r_ring * math.sin(a) if up else r_ring * math.sin(a))))
    kit.path_tube(mb, pts, r_tube, sides, mat, caps=True)


def build_buoy(p) -> kit.MeshBuilder:
    mb = kit.MeshBuilder()
    kit.surface_of_revolution(mb, buoy_profile(p["rib"]), p["around"], "LK_Buoy")
    eye(mb, BUOY_TOP - 0.01, 0.10, 0.022, p["eye_sides"], p["eye_segs"], "LK_Steel", up=True)
    eye(mb, BUOY_BOTTOM + 0.01, 0.08, 0.02, p["eye_sides"], p["eye_segs"], "LK_Steel", up=False)
    return mb


def build_light(p) -> kit.MeshBuilder:
    """Corner light: mast on the buoy top, lantern base, yellow lens."""
    mb = kit.MeshBuilder()
    sides = max(p["around"] // 2, 4)
    z0 = BUOY_TOP - 0.02
    kit.cylinder(mb, (0, 0, z0), (0, 0, z0 + 0.85), 0.04, max(sides // 2, 3), "LK_HDPE")
    base = [(0.0, z0 + 1.00), (0.11, z0 + 0.99), (0.11, z0 + 0.85), (0.0, z0 + 0.84)]
    kit.surface_of_revolution(mb, base, sides, "LK_HDPE")
    lens = [(0.0, z0 + 1.24), (0.06, z0 + 1.235), (0.085, z0 + 1.20), (0.085, z0 + 1.02), (0.0, z0 + 1.00)]
    kit.surface_of_revolution(mb, lens, sides, "LK_Lamp")
    # Stainless bracket strap where the mast meets the buoy.
    kit.cylinder(mb, (0, 0, z0 - 0.02), (0, 0, z0 + 0.06), 0.07, max(sides // 2, 3), "LK_Steel")
    return mb


FLOAT_R = 0.35
FLOAT_TOP = 0.26
FLOAT_BOTTOM = -0.26


def build_hose_float(p) -> kit.MeshBuilder:
    """Hose float: an oval yellow float half out of the water, a black strap
    round its waist and a shallow saddle on top for the hoses."""
    mb = kit.MeshBuilder()
    around = max(p["around"] // 2 + 2, 6)
    body = [(0.0, FLOAT_TOP), (0.20, FLOAT_TOP - 0.02), (0.31, FLOAT_TOP - 0.10), (FLOAT_R, 0.04),
            (FLOAT_R, -0.04), (0.31, FLOAT_BOTTOM + 0.10), (0.20, FLOAT_BOTTOM + 0.02), (0.0, FLOAT_BOTTOM)]
    if not p["rib"]:
        body = [(0.0, FLOAT_TOP), (0.27, FLOAT_TOP - 0.06), (FLOAT_R, 0.0), (0.27, FLOAT_BOTTOM + 0.06),
                (0.0, FLOAT_BOTTOM)]
    kit.surface_of_revolution(mb, body, around, "LK_Buoy")
    strap = [(FLOAT_R + 0.012, 0.035), (FLOAT_R + 0.012, -0.035)]
    kit.surface_of_revolution(mb, strap, around, "LK_HDPE")
    if p["rib"]:
        # Saddle: a short black cradle across the top.
        kit.box(mb, (0.0, 0.0, FLOAT_TOP + 0.02), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                0.22, 0.05, 0.025, "LK_HDPE")
    return mb


ASSETS = {
    "mooring_buoy": ("Buoy", build_buoy),
    "mooring_light": ("CornerLight", build_light),
    "hose_float": ("HoseFloat", build_hose_float),
}


def export(asset: str, lod: str, mats: dict, out_dir: str) -> dict:
    kit.clear_prefixed(PREFIX)
    node, fn = ASSETS[asset]
    mb = fn(LODS[lod])
    obj = mb.build(node, mats)
    obj["lk_build"] = PREFIX
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    path = os.path.join(out_dir, f"{asset}_{lod}.glb")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True, export_yup=True,
        export_texcoords=True, export_normals=True, export_materials="EXPORT",
        export_vertex_color="NONE", export_cameras=False, export_lights=False,
        export_extras=False,
    )
    used = sorted({s.material.name for s in obj.material_slots if s.material})
    lo, hi = kit.world_bbox([obj])
    bbox = [round(hi[0] - lo[0], 4), round(hi[2] - lo[2], 4), round(hi[1] - lo[1], 4)]
    data = {
        "asset": asset, "lod": lod, "tri_count": kit.mesh_tris(obj), "material_count": len(used),
        "materials": used, "bbox_size": bbox, "file": os.path.basename(path),
        "blender_version": bpy.app.version_string,
    }
    with open(os.path.join(out_dir, f"{asset}_{lod}.json"), "w") as f:
        json.dump(data, f, indent=2, sort_keys=True)
        f.write("\n")
    print(f"TRIS: {data['tri_count']}")
    print(f"BBOX: {' '.join(str(v) for v in bbox)}")
    print(f"MATERIALS: {len(used)}")
    print(f"KIT_EXPORT {asset} {lod} {path} tris={data['tri_count']}")
    kit.clear_prefixed(PREFIX)
    return data


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset", choices=list(ASSETS), default=None)
    ap.add_argument("--lod", choices=list(LODS), default=None)
    ap.add_argument("--out", default=OUT_DIR)
    ap.add_argument("--variant", default=None)  # accepted for the pipeline CLI
    return ap.parse_args(argv)


def main():
    args = parse_args()
    os.makedirs(args.out, exist_ok=True)
    mats = {n: kit.make_material(n, kit.hex_rgba(h), r, m) for n, (h, r, m) in MATERIALS.items()}
    assets = [args.asset] if args.asset else list(ASSETS)
    lods = [args.lod] if args.lod else list(LODS)
    for asset in assets:
        for lod in lods:
            export(asset, lod, mats, args.out)


if __name__ == "__main__":
    main()
