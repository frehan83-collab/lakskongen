"""Build the slaughterhouse kit (GRAPHICS_HANDOVER §2 Task A) and export glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \\
      --python blender/build_slaughterhouse_kit.py -- [--asset ID]
      [--lod LOD0|LOD1|LOD2] [--out DIR] [--no-bake] [--tri-limit-scale S]

Assets (geometry in blender/slaughterhouse_designs.py): slaughter_hall_s,
slaughter_hall_m, reception_house, chilled_loading_bay, wellboat_intake.
The Large tier reuses the land kit's harvest_hall and every tier berths at the
land kit's quay; neither is rebuilt here.

The kit shares the land kit's one material (LK_FacilityKit) and trim atlas
(build_land_base.STRIPS rows 0-27; this kit adds no strip, so the atlas file is
untouched). Every LOD is ONE mesh with ONE surface; Cycles bakes ambient
occlusion against the pad into the vertex colour (COLOR_0), LOD2 gets white.
Each export writes godot/assets/models/slaughterhouse_kit/<asset>_<LOD>.glb and
the land kit's JSON sidecar (tests/test_slaughterhouse_kit_assets.py). A LOD
over its budget stops the build.

Licence: in-house geometry, no third-party assets.
"""

from __future__ import annotations

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_land_base as land  # noqa: E402
import lk_meshkit as kit  # noqa: E402
import slaughterhouse_designs as designs  # noqa: E402
from land_parts import Build  # noqa: E402

OUT_DIR = os.path.join(land.MODELS, "slaughterhouse_kit")
PREFIX = "LK_SlaughterhouseKit"
ASSETS = list(designs.DESIGNS)
LODS = land.LODS
# Budgets (LOD0 / LOD1 / LOD2); halls in line with harvest_hall's 3,000 / 450 / 24.
TRI_LIMITS = {
    "slaughter_hall_s": (1200, 200, 16),
    "slaughter_hall_m": (2200, 340, 20),
    "reception_house": (700, 120, 12),
    "chilled_loading_bay": (600, 140, 12),
    "wellboat_intake": (900, 160, 24),
}
SPEC_SWATCHES = {
    "slaughter_hall_s": {"Walls": "#3A4046", "Fascia": "#D5D9DB", "Logo": "#D5D9DB"},
    "slaughter_hall_m": {"Walls": "#3A4046", "Fascia": "#D5D9DB", "Logo": "#D5D9DB"},
    "reception_house": {"Walls": "#3A4046", "Pipes": "#9AA0A6"},
    "chilled_loading_bay": {"Walls": "#D5D9DB", "Shelters": "#1C1E20"},
    "wellboat_intake": {"Crane": "#D9622B", "Hose": "#1C1E20", "Platform": "#5C6369"},
}
AO_DISTANCE = {"slaughter_hall_s": 6.0, "slaughter_hall_m": 7.0, "reception_house": 4.0,
               "chilled_loading_bay": 4.0, "wellboat_intake": 3.0}


def _verts(b: Build, *names):
    return [v for n in names if n in b.parts for v in b.parts[n][0].verts]


def _ext(vs, k):
    return max(v[k] for v in vs) - min(v[k] for v in vs)


def measure(asset: str, b: Build) -> dict:
    """The asset's dimensions, measured on its parts."""
    if asset == "wellboat_intake":
        p = _verts(b, "Platform") or _verts(b, "Frame")
        everything = [v for mb, _s in b.parts.values() for v in mb.verts]
        return {"platform_length_m": _ext(p, 0), "platform_width_m": _ext(p, 1),
                "platform_height_m": max(v[2] for v in p), "height_m": max(v[2] for v in everything),
                "hose_reach_m": -min(v[1] for v in _verts(b, "Crane"))}
    walls = _verts(b, "Walls", "Plinth", "Doors", "Windows")
    roof = _verts(b, "Roof", "HallRoof", "Walls", "Trim")
    return {"length_m": _ext(walls, 0), "width_m": _ext(walls, 1), "height_m": max(v[2] for v in roof)}


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


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset", choices=ASSETS, default=None)
    ap.add_argument("--lod", choices=LODS, default=None)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--out", default=None, help="output directory (default godot/assets/models/slaughterhouse_kit)")
    ap.add_argument("--tri-limit-scale", type=float, default=1.0, help="scale the budgets (tests use it)")
    ap.add_argument("--variant", default=None)  # accepted for the pipeline CLI
    return ap.parse_args(argv)


def main():
    args = parse_args()
    # The land kit's export writes the sidecar from its own tables: register this kit's rows there.
    land.TRI_LIMITS.update(TRI_LIMITS)
    land.SPEC_SWATCHES.update(SPEC_SWATCHES)
    land.AO_DISTANCE.update(AO_DISTANCE)
    mat = land.material()
    out_dir = args.out or OUT_DIR
    os.makedirs(out_dir, exist_ok=True)
    for asset in ([args.asset] if args.asset else ASSETS):
        for lod in ([args.lod] if args.lod else LODS):
            kit.clear_prefixed(PREFIX)
            objs, tris, strips, dims = build_objects(asset, LODS.index(lod), mat)
            if lod != "LOD2" and not args.no_bake:
                land.bake_ao(objs, asset)
            else:
                land.white_vertex_colour(objs)
            obj = land.join(objs, asset)
            land.export_lod(asset, lod, obj, tris, strips, dims, out_dir, args.tri_limit_scale)
    kit.clear_prefixed(PREFIX)


if __name__ == "__main__":
    main()
