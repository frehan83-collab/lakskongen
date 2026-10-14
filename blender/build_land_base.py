"""Build the land-based farm kit (farm-types spec §5.1, item F2-25) and
export glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_land_base.py -- [--asset ID] [--lod LOD0|LOD1|LOD2]
      [--out DIR] [--no-bake] [--textures] [--tri-limit-scale S]

Assets (geometry in blender/land_designs.py, parts in blender/land_parts.py):
tank_growout, tank_pregrow, tank_feeder, process_hall_bay, harvest_hall,
pipe_bridge_span, quay, intake_basin, river_intake, lox_tank,
genset_container, substation, feed_silo_land, sludge_station.

One material for the whole kit (spec §5 "one material per kit"): every
LOD is ONE mesh with ONE surface using LK_FacilityKit, so F2-20 can draw a
whole campus's tanks with one MultiMesh per tank type and LOD band. Colour
and surface come from a 1024 px trim atlas of horizontal strips
(--textures writes godot/assets/models/facility_kit/textures/
facility_atlas.png): each part's UVs sit on its strip's centre row (V) and
run along the surface in metres / TILE_M (U, repeating). The atlas alpha is
the strip's roughness, or 1.0 where the strip emits at night (windows,
work-lights). Cycles bakes ambient occlusion (against a ground plane for
pieces that stand on the pad) into the vertex colour (COLOR_0); LOD2 gets
white. The spec's node names (Wall, Band, Lid, Walkway, Feeder...) are the
parts: one surface cannot keep nodes, so each part keeps its own strip (the
shader can address it: lid swatch, leg stretch, rotor) and the sidecar
lists its triangles and swatch.

Each export writes godot/assets/models/land_kit/<asset>_<LOD>.glb and a
JSON sidecar (tri_count, materials, bbox, per-part triangles, measured
dimensions, per-part swatches) for blender_asset_pipeline.py and
tests/test_land_kit_assets.py. A LOD over its spec budget stops the build.

Licence: in-house geometry and generated textures, no third-party assets.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import struct
import sys
import zlib

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import land_designs as designs  # noqa: E402
import lk_meshkit as kit  # noqa: E402
from land_parts import M, Build  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(REPO, "godot", "assets", "models")
OUT_DIR = os.path.join(MODELS, "land_kit")
ATLAS_PATH = os.path.join(MODELS, "facility_kit", "textures", "facility_atlas.png")
PREFIX = "LK_LandKit"

# Trim atlas: 32 strips of 32 px across 1024 px; U repeats every TILE_M.
TILE_M = 16.0
ATLAS_PX = 1024
STRIP_ROWS = 32
STRIP_PX = ATLAS_PX // STRIP_ROWS

# Strip rows, top of the image first: name, sRGB swatch, roughness (atlas
# alpha; None = emits at night, alpha 1.0), surface pattern, period (m),
# pattern depth. Periods divide TILE_M so every strip tiles seamlessly.
STRIPS = [
    ("tank_wall", "#C9CED1", 0.62, "panel", 2.0, 0.07),
    ("tank_band", "#5E7A6A", 0.66, "panel", 2.0, 0.07),
    ("tank_lid", "#2A3036", 0.90, "seam", 1.6, 0.10),
    ("tank_lid_pale", "#EDEFEF", 0.72, "seam", 1.6, 0.06),
    ("feeder", "#8A9096", 0.50, "flat", 0.0, 0.0),
    ("rotor", "#8A9096", 0.50, "flat", 0.0, 0.0),
    ("steel", "#8A9096", 0.45, "flat", 0.0, 0.0),
    ("grating", "#5C6369", 0.85, "grating", 0.25, 0.14),
    ("hall_clad", "#D5D9DB", 0.60, "seam", 1.0, 0.06),
    ("hall_roof", "#4A5056", 0.72, "seam", 0.5, 0.08),
    ("window", "#F2D8A0", None, "flat", 0.0, 0.0),
    ("harvest_clad", "#3A4046", 0.62, "seam", 1.0, 0.09),
    ("panel_blank", "#D5D9DB", 0.55, "flat", 0.0, 0.0),
    ("orange", "#D9622B", 0.52, "flat", 0.0, 0.0),
    ("bridge_leg", "#D9622B", 0.52, "flat", 0.0, 0.0),
    ("pipe", "#9AA0A6", 0.40, "flat", 0.0, 0.0),
    ("conveyor", "#2E3236", 0.85, "seam", 0.5, 0.08),
    ("concrete", "#9EA3A6", 0.92, "panel", 3.2, 0.05),
    ("fender", "#1C1E20", 0.88, "flat", 0.0, 0.0),
    ("lox", "#E9ECEA", 0.45, "flat", 0.0, 0.0),
    ("genset", "#4A5650", 0.60, "corrugated", 0.32, 0.07),
    ("silo", "#8A8F96", 0.50, "seam", 1.6, 0.05),
    ("sludge", "#6E7B73", 0.66, "corrugated", 0.32, 0.07),
    ("door", "#6B7177", 0.60, "seam", 0.25, 0.08),
    ("lamp", "#FFE7B0", None, "flat", 0.0, 0.0),
    ("safety", "#E8B92A", 0.55, "flat", 0.0, 0.0),
]
STRIP_INDEX = {s[0]: i for i, s in enumerate(STRIPS)}

ASSETS = list(designs.DESIGNS)
LODS = ["LOD0", "LOD1", "LOD2"]
# Spec §5.1 budgets (LOD0 / LOD1 / LOD2); the feeder rides in the tank's
# LOD2 and has none of its own.
TRI_LIMITS = {
    "tank_growout": (2400, 600, 96),
    "tank_pregrow": (1600, 400, 64),
    "tank_feeder": (220, 60, None),
    "process_hall_bay": (900, 160, 12),
    "harvest_hall": (3000, 450, 24),
    "pipe_bridge_span": (1800, 320, 24),
    "quay": (2500, 400, 30),
    "intake_basin": (2200, 380, 40),
    "river_intake": (600, 120, 12),
    "lox_tank": (640, 120, 12),
    "genset_container": (320, 60, 12),
    "substation": (1500, 220, 12),
    "feed_silo_land": (900, 160, 12),
    "sludge_station": (800, 150, 12),
}
# Spec §5.1 swatch per part (hex): the sidecar reports the strip each part
# uses and that strip's colour in the atlas.
SPEC_SWATCHES = {
    "tank_growout": {"Wall": "#C9CED1", "Band": "#5E7A6A", "Lid": "#2A3036"},
    "tank_pregrow": {"Wall": "#C9CED1", "Band": "#5E7A6A", "Lid": "#2A3036"},
    "tank_feeder": {"Feeder": "#8A9096"},
    "process_hall_bay": {"Walls": "#D5D9DB", "Roof": "#4A5056", "Windows": "#F2D8A0"},
    "harvest_hall": {"Walls": "#3A4046", "Fascia": "#D5D9DB"},
    "pipe_bridge_span": {"Truss": "#D9622B", "Pipes": "#9AA0A6"},
    "quay": {"Deck": "#9EA3A6", "Fenders": "#1C1E20"},
    "intake_basin": {"Wall": "#9EA3A6", "Walls": "#D5D9DB"},
    "river_intake": {"Weir": "#9EA3A6"},
    "lox_tank": {"Vessel": "#E9ECEA"},
    "genset_container": {"Box": "#4A5650"},
    "substation": {"Transformers": "#D9622B", "Fence": "#8A9096"},
    "feed_silo_land": {"Silos": "#8A8F96"},
    "sludge_station": {"House": "#D5D9DB", "Skips": "#6E7B73"},
}
# Ground plane height for the AO bake (None: the piece stands in water),
# and the AO ray distance (m).
GROUND = {"quay": None, "intake_basin": None, "river_intake": None, "pipe_bridge_span": -10.0, "tank_feeder": None}
AO_DISTANCE = {"harvest_hall": 8.0, "process_hall_bay": 5.0, "quay": 4.0, "intake_basin": 4.0,
               "tank_growout": 4.0, "tank_pregrow": 3.0}


def strip_v(name: str) -> float:
    """Blender UV V of a strip's centre row (glTF flips V: row r -> v_gltf = (r+0.5)/32)."""
    return 1.0 - (STRIP_INDEX[name] + 0.5) / STRIP_ROWS


# --------------------------------------------------------------------------
# Measurements (spec dimensions, from the geometry)
# --------------------------------------------------------------------------

def _verts(b: Build, *names):
    return [v for n in names if n in b.parts for v in b.parts[n][0].verts]


def _ext(vs, k):
    return max(v[k] for v in vs) - min(v[k] for v in vs)


def _top(vs):
    return max(v[2] for v in vs)


def _radius(vs):
    return max(math.hypot(v[0], v[1]) for v in vs)


def _arc(vs):
    rs = [math.hypot(v[0], v[1]) for v in vs]
    ang = [math.degrees(math.atan2(v[1], v[0])) for v in vs]
    return 0.5 * (max(rs) + min(rs)), max(ang) - min(ang), max(rs) - min(rs)


def measure(asset: str, b: Build) -> dict:
    """The spec dimensions of an asset, measured on its parts."""
    walls = ("Walls", "Windows", "Plinth", "Doors", "Fascia")
    if asset in ("tank_growout", "tank_pregrow"):
        return {"shell_diameter_m": 2 * _radius(_verts(b, "Band")), "shell_height_m": _top(_verts(b, "Wall"))}
    if asset == "tank_feeder":
        body = [v for v in _verts(b, "Feeder") if math.hypot(v[0], v[1]) <= 1.0]
        return {"diameter_m": 2 * _radius([v for v in body if 0.3 <= v[2] <= 0.75]), "height_m": _top(body)}
    if asset == "process_hall_bay":
        vs = _verts(b, *walls)
        return {"length_m": _ext(vs, 0), "width_m": _ext(vs, 1), "height_m": _top(_verts(b, "Roof"))}
    if asset == "harvest_hall":
        vs = _verts(b, "Walls", "Fascia")
        return {"length_m": _ext(vs, 0), "width_m": _ext(vs, 1), "height_m": _top(vs)}
    if asset == "pipe_bridge_span":
        vs = _verts(b, "Truss")
        return {"span_m": _ext(vs, 0), "section_width_m": _ext(vs, 1), "section_height_m": _ext(vs, 2),
                "leg_height_m": -min(v[2] for v in _verts(b, "Legs"))}
    if asset == "quay":
        vs = _verts(b, "Deck")
        return {"length_m": _ext(vs, 0), "width_m": _ext(vs, 1), "deck_height_m": 3.0 + _top(vs)}
    if asset == "intake_basin":
        r, deg, t = _arc(_verts(b, "Wall"))
        house = _verts(b, *walls)
        return {"arc_radius_m": r, "arc_degrees": deg, "wall_thickness_m": t, "wall_top_m": 3.6 + _top(_verts(b, "Wall")),
                "pump_house_length_m": _ext(house, 0), "pump_house_width_m": _ext(house, 1),
                "pump_house_height_m": _top(_verts(b, "Roof")) if "Roof" in b.parts else _top(house)}
    if asset == "river_intake":
        vs = _verts(b, "Weir", "House", *walls)
        return {"length_m": _ext(vs, 0), "width_m": _ext(_verts(b, "Weir"), 1), "height_m": _top(_verts(b, "House", "Roof", *walls))}
    if asset == "lox_tank":
        vs = _verts(b, "Vessel")
        return {"diameter_m": 2 * _radius(vs), "height_m": _top(vs)}
    if asset == "genset_container":
        vs = _verts(b, "Box")
        return {"length_m": _ext(vs, 0), "width_m": _ext(vs, 1), "height_m": _top(vs)}
    if asset == "substation":
        everything = [v for mb, _s in b.parts.values() for v in mb.verts]
        return {"length_m": _ext(everything, 0), "width_m": _ext(everything, 1), "height_m": _top(everything)}
    if asset == "feed_silo_land":
        vs = _verts(b, "Silos")
        one = [v for v in vs if math.hypot(v[0] + 2.5, v[1] + 2.5) < 2.4]
        return {"silo_diameter_m": 2 * max(math.hypot(v[0] + 2.5, v[1] + 2.5) for v in one) if one else 0.0,
                "silo_height_m": _top(vs), "silos": 4}
    if asset == "sludge_station":
        vs = _verts(b, "House", "Skips", "Canopy", "CanopyRoof", *walls)
        return {"length_m": _ext(vs, 0), "width_m": _ext(_verts(b, "House", "CanopyRoof", *walls), 1),
                "height_m": _top(_verts(b, "Roof", "House"))}
    raise KeyError(asset)


# --------------------------------------------------------------------------
# Trim atlas
# --------------------------------------------------------------------------

def _srgb(code: str):
    code = code.lstrip("#")
    return [int(code[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]


def atlas_rows():
    """The atlas as ATLAS_PX rows (top first) of RGBA bytes: each strip is
    its swatch, modulated along U only (seams, panels, corrugation, a faint
    wrap-around tone drift), constant down its 32 rows, so every mip level
    down to 32 x 32 keeps a strip on its own rows."""
    rows = []
    px_per_m = ATLAS_PX / TILE_M
    for idx in range(STRIP_ROWS):
        if idx < len(STRIPS):
            _name, hexc, rough, pattern, period, depth = STRIPS[idx]
        else:
            _name, hexc, rough, pattern, period, depth = ("unused", "#808080", 0.8, "flat", 0.0, 0.0)
        base = _srgb(hexc)
        line = bytearray()
        for x in range(ATLAS_PX):
            u = (x + 0.5) / px_per_m
            lum = 1.0
            # Low tone drift: integer harmonics of the tile, fixed phases.
            for k, (amp, ph) in enumerate(((0.012, 0.3 + idx), (0.008, 1.7 + 2 * idx), (0.006, 4.1 + 3 * idx))):
                lum += amp * math.sin(math.tau * (k + 1) * u / TILE_M + ph)
            if pattern in ("panel", "seam", "grating") and period > 0.0:
                m = u % period
                d_px = min(m, period - m) * px_per_m
                lum -= depth * math.exp(-(d_px / 1.1) ** 2)
            if pattern == "panel" and period > 0.0:
                panel = int(u // period)
                lum += 0.018 * math.sin(12.9898 * (panel + 1) * (idx + 3))
            if pattern == "corrugated" and period > 0.0:
                lum += depth * math.sin(math.tau * u / period)
            alpha = 1.0 if rough is None else rough
            for c in base:
                line.append(max(0, min(255, int(round(c * lum * 255.0)))))
            line.append(int(round(alpha * 255.0)))
        rows.extend([bytes(line)] * STRIP_PX)
    return rows


def write_png(path: str, rows, width: int) -> None:
    """A plain RGBA8 PNG (filter 0, zlib level 9): byte-identical per input."""
    raw = b"".join(b"\x00" + r for r in rows)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, len(rows), 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(png)


_STRIP_COLOURS: dict = {}


def strip_colours():
    """Measured colour of every strip: the median texel of its centre row."""
    if _STRIP_COLOURS:
        return _STRIP_COLOURS
    rows = atlas_rows()
    out = _STRIP_COLOURS
    for idx, s in enumerate(STRIPS):
        line = rows[idx * STRIP_PX + STRIP_PX // 2]
        px = sorted((line[4 * x:4 * x + 3] for x in range(ATLAS_PX)), key=lambda c: sum(c))
        r, g, b = px[ATLAS_PX // 2]
        out[s[0]] = "#%02X%02X%02X" % (r, g, b)
    return out


# --------------------------------------------------------------------------
# Build, bake, join, export
# --------------------------------------------------------------------------

def material():
    mat = bpy.data.materials.get(M) or bpy.data.materials.new(M)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.7
    return mat


def build_objects(asset: str, lod: int, mat):
    b = Build()
    designs.DESIGNS[asset](b, lod)
    dims = measure(asset, b)
    objs, tris, strips = [], {}, {}
    for name in sorted(b.parts):
        mb, strip = b.parts[name]
        if not mb.faces:
            continue
        kit.strip_uv(mb, strip_v(strip), TILE_M)
        obj = mb.build(name, {M: mat})
        obj["lk_build"] = PREFIX
        objs.append(obj)
        tris[name] = mb.tri_count
        strips[name] = strip
    return objs, tris, strips, dims


def bake_ao(objs, asset: str) -> None:
    """Cycles AO into a per-vertex colour (COLOR_0), against a ground plane
    for pieces standing on the pad."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    if scene.world is None:
        scene.world = bpy.data.worlds.new("LK_World")
    scene.world.light_settings.distance = AO_DISTANCE.get(asset, 3.0)
    ground = None
    gz = GROUND.get(asset, 0.0)
    if gz is not None:
        mesh = bpy.data.meshes.new("LK_LandKitGround")
        s = 400.0
        mesh.from_pydata([(-s, -s, gz), (s, -s, gz), (s, s, gz), (-s, s, gz)], [], [(0, 1, 2, 3)])
        ground = bpy.data.objects.new("LK_LandKitGround", mesh)
        ground["lk_build"] = PREFIX
        scene.collection.objects.link(ground)
    for obj in objs:
        attr = obj.data.color_attributes.new("AO", "BYTE_COLOR", "POINT")
        obj.data.color_attributes.active_color = attr
        obj.data.color_attributes.render_color_index = obj.data.color_attributes.active_color_index
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.bake(type="AO", target="VERTEX_COLORS", use_selected_to_active=False)
    if ground is not None:
        bpy.data.objects.remove(ground, do_unlink=True)


def white_vertex_colour(objs) -> None:
    for obj in objs:
        attr = obj.data.color_attributes.new("AO", "BYTE_COLOR", "POINT")
        obj.data.color_attributes.active_color = attr
        for c in attr.data:
            c.color = (1.0, 1.0, 1.0, 1.0)


def join(objs, name: str):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objs:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    obj.data.name = name
    return obj


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
    colours = strip_colours()
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
        "atlas": os.path.relpath(ATLAS_PATH, REPO).replace(os.sep, "/"),
        "file": os.path.basename(path),
        "blender_version": bpy.app.version_string,
    }
    with open(os.path.join(out_dir, f"{asset}_{lod}.json"), "w") as f:
        json.dump(data, f, indent=2, sort_keys=True)
        f.write("\n")
    print(f"TRIS: {tri_count}")
    print(f"BBOX: {' '.join(str(v) for v in data['bbox_size'])}")
    print(f"MATERIALS: {data['material_count']}")
    print(f"LAND_EXPORT {asset} {lod} {path} tris={tri_count} dims={data['dimensions']} parts={tris}")
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
    ap.add_argument("--textures", action="store_true")
    ap.add_argument("--atlas-out", default=ATLAS_PATH, help="where --textures writes the atlas")
    ap.add_argument("--out", default=None, help="output directory (default godot/assets/models/land_kit)")
    ap.add_argument("--tri-limit-scale", type=float, default=1.0, help="scale the budgets (tests use it)")
    ap.add_argument("--variant", default=None)  # accepted for the pipeline CLI
    return ap.parse_args(argv)


def main():
    args = parse_args()
    if args.textures:
        write_png(args.atlas_out, atlas_rows(), ATLAS_PX)
        print(f"TEXTURE {args.atlas_out}")
    mat = material()
    out_dir = args.out or OUT_DIR
    os.makedirs(out_dir, exist_ok=True)
    for asset in ([args.asset] if args.asset else ASSETS):
        for lod in ([args.lod] if args.lod else lods_for(asset)):
            kit.clear_prefixed(PREFIX)
            objs, tris, strips, dims = build_objects(asset, LODS.index(lod), mat)
            if lod != "LOD2" and not args.no_bake:
                bake_ao(objs, asset)
            else:
                white_vertex_colour(objs)
            obj = join(objs, asset)
            export_lod(asset, lod, obj, tris, strips, dims, out_dir, args.tri_limit_scale)
    kit.clear_prefixed(PREFIX)


if __name__ == "__main__":
    main()
