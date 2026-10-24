"""Build the closed-pen kit (farm-types spec §5.3, item F2-30) and export glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_closed_pens.py -- [--asset ID]
      [--lod LOD0|LOD1|LOD2] [--out DIR] [--no-bake] [--textures]
      [--atlas-out PATH] [--tri-limit-scale S]

Assets (geometry in blender/closed_pen_designs.py): closed_donut,
closed_globe, semi_closed_ring, pump_module.

The kit shares the land kit's one material (LK_FacilityKit) and its trim atlas:
the strips live in build_land_base.STRIPS (this kit appends donut_shell and
skirt_tarp, rows 28 and 29) and the atlas file is generated from that list, so
`--textures` here writes the same bytes as `build_land_base.py --textures`.
Every LOD is ONE mesh with ONE surface. Cycles bakes ambient occlusion into the
vertex colour (COLOR_0) with no ground plane (these pens float on the
waterline); LOD2 gets white.

Each export writes godot/assets/models/closed_pen/<asset>_<LOD>.glb and a JSON
sidecar (tri_count, materials, bbox, per-part triangles and strips, measured
dimensions, per-part swatches) for blender_asset_pipeline.py and
tests/test_closed_pen_assets.py. A LOD over its spec budget stops the build.

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
import closed_pen_designs as designs  # noqa: E402
import lk_meshkit as kit  # noqa: E402
from land_parts import Build  # noqa: E402

REPO = land.REPO
OUT_DIR = os.path.join(land.MODELS, "closed_pen")
PREFIX = "LK_ClosedPen"

ASSETS = list(designs.DESIGNS)
LODS = ["LOD0", "LOD1", "LOD2"]
# Spec §5.3 budgets (LOD0 / LOD1 / LOD2); the pump module has no LOD2.
TRI_LIMITS = {
    "closed_donut": (12000, 3000, 400),
    "closed_globe": (6000, 1500, 200),
    "semi_closed_ring": (15000, 4000, 600),
    "pump_module": (1200, 300, None),
}
# Spec §5.3 swatch per part (hex): the sidecar reports the strip each part
# uses and that strip's colour in the atlas.
SPEC_SWATCHES = {
    "closed_donut": {"Shell": "#8E979C"},
    "closed_globe": {"Shell": "#E9ECEA"},
    "semi_closed_ring": {"Collar": "#8A9096", "Skirt": "#C9CCC6"},
    "pump_module": {"Housing": "#8A9096", "Pumps": "#D9622B"},
}
# The pens float: no ground plane under the AO bake.
land.GROUND.update({a: None for a in ASSETS})
land.AO_DISTANCE.update({"closed_donut": 6.0, "closed_globe": 6.0, "semi_closed_ring": 5.0, "pump_module": 3.0})


# --------------------------------------------------------------------------
# Measurements (spec dimensions, from the geometry)
# --------------------------------------------------------------------------

def _verts(b: Build, *names):
    return [v for n in names if n in b.parts for v in b.parts[n][0].verts]


def _all(b: Build):
    return [v for _n, (mb, _s) in b.parts.items() for v in mb.verts]


def _ext(vs, k):
    return max(v[k] for v in vs) - min(v[k] for v in vs)


def _mesh_volume(b: Build, part: str) -> float:
    """Volume of a closed part by the divergence theorem (outward faces)."""
    mb = b.parts[part][0]
    vol = 0.0
    for f in mb.faces:
        p0 = mb.verts[f[0]]
        for k in range(1, len(f) - 1):
            p1, p2 = mb.verts[f[k]], mb.verts[f[k + 1]]
            vol += (p0[0] * (p1[1] * p2[2] - p1[2] * p2[1]) - p0[1] * (p1[0] * p2[2] - p1[2] * p2[0])
                    + p0[2] * (p1[0] * p2[1] - p1[1] * p2[0])) / 6.0
    return vol


def measure(asset: str, b: Build) -> dict:
    """The spec dimensions of an asset, measured on its parts."""
    if asset == "closed_donut":
        shell = _verts(b, "Shell")
        radii = [math.hypot(v[0], v[1]) for v in shell]
        big_r, small_r = (max(radii) + min(radii)) / 2, (max(radii) - min(radii)) / 2
        return {"major_radius_m": big_r, "tube_radius_m": small_r, "outer_diameter_m": 2 * max(radii),
                "freeboard_m": max(v[2] for v in shell),
                "volume_m3": 2 * math.pi ** 2 * big_r * small_r ** 2,
                "mesh_volume_m3": _mesh_volume(b, "Shell")}
    if asset == "closed_globe":
        shell = _verts(b, "Shell")
        r = _ext(shell, 2) / 2
        return {"diameter_m": 2 * r, "freeboard_m": max(v[2] for v in shell),
                "volume_m3": 4.0 / 3.0 * math.pi * r ** 3, "mesh_volume_m3": _mesh_volume(b, "Shell")}
    if asset == "semi_closed_ring":
        collar, skirt, net = _verts(b, "Collar"), _verts(b, "Skirt"), _verts(b, "Net")
        return {"collar_outer_diameter_m": 2 * max(math.hypot(v[0], v[1]) for v in collar),
                "skirt_depth_m": -min(v[2] for v in skirt),
                "net_depth_m": -min(v[2] for v in net) if net else -min(v[2] for v in skirt),
                "collar_top_m": max(v[2] for v in collar)}
    if asset == "pump_module":
        house, strainer = _verts(b, "Housing"), _verts(b, "Strainer")
        return {"length_m": _ext(house, 0), "width_m": _ext(house, 1), "height_m": _ext(house, 2),
                "intake_depth_m": -min(v[2] for v in strainer), "base_z_m": min(v[2] for v in house)}
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
    print(f"CLOSED_PEN_EXPORT {asset} {lod} {path} tris={tri_count} dims={data['dimensions']} parts={tris}")
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
    ap.add_argument("--out", default=None, help="output directory (default godot/assets/models/closed_pen)")
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
