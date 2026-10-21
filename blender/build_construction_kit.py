"""Build the construction kit (farm-types spec §5.3, item F2-28) and export
glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_construction_kit.py -- [--asset ID]
      [--lod LOD0|LOD1|LOD2] [--out DIR] [--no-bake] [--textures]
      [--atlas-out PATH] [--tri-limit-scale S]

Assets (geometry in blender/construction_designs.py): tower_crane,
scaffold_ring, excavator, blast_mats.

The kit shares the land kit's one material (LK_FacilityKit) and its trim atlas:
the strips live in build_land_base.STRIPS (this kit adds crane_yellow, row 27)
and the atlas file is generated from that list, so `--textures` here writes the
same bytes as `build_land_base.py --textures`. Every LOD is ONE mesh with ONE
surface, so the facility presenter (LandCampusBuilder, F2-20) can instance the
scaffold ring on rising tanks beside the tank MultiMesh with the same shader
(INSTANCE_CUSTOM build progress discards fragments above progress x
build_height_m: 13 m, the ring's height too). Cycles bakes ambient occlusion against a ground plane into the vertex colour
(COLOR_0); LOD2 gets white.

Each export writes godot/assets/models/construction_kit/<asset>_<LOD>.glb and a
JSON sidecar (tri_count, materials, bbox, per-part triangles, measured
dimensions, per-part swatches) for blender_asset_pipeline.py and
tests/test_construction_kit_assets.py. A LOD over its spec budget stops the
build.

Licence: in-house geometry and generated textures, no third-party assets.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_land_base as land  # noqa: E402
import construction_designs as designs  # noqa: E402
import lk_meshkit as kit  # noqa: E402
from land_parts import Build  # noqa: E402

REPO = land.REPO
OUT_DIR = os.path.join(land.MODELS, "construction_kit")
PREFIX = "LK_ConstructionKit"

ASSETS = list(designs.DESIGNS)
LODS = ["LOD0", "LOD1", "LOD2"]
# Spec §5.3 budgets (LOD0 / LOD1 / LOD2). The excavator and the blast mats
# share one row of the spec table (2,000 / 300): each asset gets that budget.
TRI_LIMITS = {
    "tower_crane": (2500, 300, 40),
    "scaffold_ring": (1500, 200, None),
    "excavator": (2000, 300, None),
    "blast_mats": (2000, 300, None),
}
# Spec §5.3 swatch per part (hex): the sidecar reports the strip each part
# uses and that strip's colour in the atlas.
SPEC_SWATCHES = {
    "tower_crane": {"Mast": "#E6BE2E", "Jib": "#E6BE2E"},
    "scaffold_ring": {"Standards": "#8A9096"},
    "excavator": {"House": "#E6BE2E", "Tracks": "#1C1E20"},
    "blast_mats": {"Mats": "#1C1E20"},
}


# --------------------------------------------------------------------------
# Measurements (spec dimensions, from the geometry)
# --------------------------------------------------------------------------

def _verts(b: Build, *names):
    return [v for n in names if n in b.parts for v in b.parts[n][0].verts]


def _all(b: Build, skip=()):
    return [v for n, (mb, _s) in b.parts.items() if n not in skip for v in mb.verts]


def _ext(vs, k):
    return max(v[k] for v in vs) - min(v[k] for v in vs)


def _top(vs):
    return max(v[2] for v in vs)


def measure(asset: str, b: Build) -> dict:
    """The spec dimensions of an asset, measured on its parts."""
    if asset == "tower_crane":
        mast, jib, counter = _verts(b, "Mast"), _verts(b, "Jib"), _verts(b, "CounterJib")
        return {"mast_height_m": _top(mast),
                "jib_length_m": max(v[0] for v in jib),
                "counter_jib_length_m": -min(v[0] for v in counter),
                "mast_width_m": _ext(mast, 0),
                "overall_height_m": _top(_all(b, skip=("Lights",)))}
    if asset == "scaffold_ring":
        vs = _all(b)
        radii = [math.hypot(v[0], v[1]) for v in vs]
        inner = min(radii)
        return {"outer_diameter_m": 2 * max(radii),
                "inner_clear_diameter_m": 2 * inner,
                # The platform ring is a polygon: its inner edges stand at
                # inner * cos(pi / sides) from the axis at mid-edge.
                "inner_inscribed_diameter_m": 2 * inner * math.cos(math.pi / b.dims["sides"]),
                "height_m": _top(vs),
                "lifts": float(b.dims["lifts"])}
    if asset == "excavator":
        vs = _all(b)
        return {"length_m": _ext(vs, 1), "width_m": _ext(vs, 0), "height_m": _top(vs)}
    if asset == "blast_mats":
        vs = _all(b)
        return {"length_m": _ext(vs, 0), "width_m": _ext(vs, 1), "stack_height_m": _top(vs),
                "mats": float(b.dims["mats"])}
    raise KeyError(asset)


# --------------------------------------------------------------------------
# Build, bake, join, export
# --------------------------------------------------------------------------

def build_objects(asset: str, lod: int, mat):
    b = Build()
    designs.DESIGNS[asset](b, lod)
    dims = measure(asset, b)
    objs, tris, strips = [], {}, {}
    for name in sorted(b.parts):
        mb, strip = b.parts[name]
        if not mb.faces:
            continue
        kit.strip_uv(mb, land.strip_v(strip), land.TILE_M)
        obj = mb.build(name, {land.M: mat})
        obj["lk_build"] = PREFIX
        objs.append(obj)
        tris[name] = mb.tri_count
        strips[name] = strip
    return objs, tris, strips, dims


def export_lod(asset: str, lod: str, obj, tris, strips, dims, out_dir: str, limit_scale: float) -> dict:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    path = os.path.join(out_dir, f"{asset}_{lod}.glb")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True, export_apply=False, export_yup=True,
        export_texcoords=True, export_normals=True, export_materials="EXPORT",
        export_vertex_color="ACTIVE", export_all_vertex_colors=False,
        export_cameras=False, export_lights=False, export_extras=False,
    )
    lo, hi = kit.world_bbox([obj])
    colours = land.strip_colours()
    tri_count = kit.mesh_tris(obj)
    data = {
        "asset": asset,
        "lod": lod,
        "tri_count": tri_count,
        "material_count": len({s.material.name for s in obj.material_slots if s.material}),
        "materials": sorted({s.material.name for s in obj.material_slots if s.material}),
        "surfaces": len(obj.material_slots),
        "bbox_size": [round(hi[0] - lo[0], 4), round(hi[2] - lo[2], 4), round(hi[1] - lo[1], 4)],
        "parts": tris,
        "part_strips": strips,
        "dimensions": {k: round(v, 3) for k, v in sorted(dims.items())},
        "swatches": {part: {"strip": strips[part], "hex": colours[strips[part]], "spec": spec}
                     for part, spec in sorted(SPEC_SWATCHES[asset].items()) if part in strips},
        "atlas": os.path.relpath(land.ATLAS_PATH, REPO).replace(os.sep, "/"),
        "file": os.path.basename(path),
        "blender_version": bpy.app.version_string,
    }
    with open(os.path.join(out_dir, f"{asset}_{lod}.json"), "w") as f:
        json.dump(data, f, indent=2, sort_keys=True)
        f.write("\n")
    print(f"TRIS: {tri_count}")
    print(f"BBOX: {' '.join(str(v) for v in data['bbox_size'])}")
    print(f"MATERIALS: {data['material_count']}")
    print(f"CONSTRUCTION_EXPORT {asset} {lod} {path} tris={tri_count} dims={data['dimensions']} parts={tris}")
    limit = TRI_LIMITS[asset][LODS.index(lod)]
    if limit is not None and tri_count > limit * limit_scale:
        raise SystemExit(f"{asset} {lod} has {tri_count} triangles, over its {int(limit * limit_scale)} budget")
    return data


def lods_for(asset: str):
    return [lod for lod, lim in zip(LODS, TRI_LIMITS[asset]) if lim is not None]


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset", choices=ASSETS, default=None)
    ap.add_argument("--lod", choices=LODS, default=None)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--textures", action="store_true", help="also write the shared atlas (the land kit's bytes)")
    ap.add_argument("--atlas-out", default=land.ATLAS_PATH, help="where --textures writes the atlas")
    ap.add_argument("--out", default=None, help="output directory (default godot/assets/models/construction_kit)")
    ap.add_argument("--tri-limit-scale", type=float, default=1.0, help="scale the budgets (tests use it)")
    ap.add_argument("--variant", default=None)  # accepted for the pipeline CLI
    return ap.parse_args(argv)


def main():
    args = parse_args()
    if args.textures:
        land.write_png(args.atlas_out, land.atlas_rows(), land.ATLAS_PX)
        print(f"TEXTURE {args.atlas_out}")
    mat = land.material()
    out_dir = args.out or OUT_DIR
    os.makedirs(out_dir, exist_ok=True)
    for asset in ([args.asset] if args.asset else ASSETS):
        for lod in ([args.lod] if args.lod else lods_for(asset)):
            if lod not in lods_for(asset):
                raise SystemExit(f"{asset} has no {lod} (spec budgets: {TRI_LIMITS[asset]})")
            kit.clear_prefixed(PREFIX)
            objs, tris, strips, dims = build_objects(asset, LODS.index(lod), mat)
            if lod != "LOD2" and not args.no_bake:
                land.bake_ao(objs, asset)
            else:
                land.white_vertex_colour(objs)
            obj = land.join(objs, asset)
            export_lod(asset, lod, obj, tris, strips, dims, out_dir, args.tri_limit_scale)
    kit.clear_prefixed(PREFIX)


if __name__ == "__main__":
    main()
