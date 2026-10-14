"""Build the aquaculture vessels and export glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_vessels.py -- [--asset NAME] [--lod LOD0|LOD1|LOD2]
      [--no-bake] [--out DIR] [--textures]

Assets (blender/vessel_designs.py places the parts):
  workboat          15 x 10 m service catamaran: wheelhouse forward, push
                    knees on both bows, a knuckle-boom crane behind the
                    wheelhouse, capstans and a winch on the open aft deck.
  feed_barge        37 x 11.2 m square steel feed barge, 500 t (ScaleAQ
                    "SeaFarm Feeder 500" class): silo house over the deck,
                    crew quarters amidships, control room on the roof, feed
                    lines over the starboard side.
  feed_barge_large  64.8 x 13 m ship-shaped exposed-site barge (AKVA
                    AC800-class): raised bow with the control house, long
                    silo house, workboat ramp in the stern.
  wellboat          76.96 x 17.8 m live-fish carrier (Sølvtrans "Ronja"
                    class, Aas Mek AAS 3002 ST): red hull, white
                    superstructure forward with the wheelhouse and bridge
                    wings, pump house, two wells, four cranes, aft control
                    house.
  wellboat_large    116 x 23 m (Ronja Storm class): four wells, three tiers.
  rib               8.5 x 3.0 m work RIB, the site tender: deep-V hull in an
                    orange buoyancy tube, centre console, radar arch, twin
                    outboards.

References (retrieved 2026-10-14): Baird Maritime vessel reviews of Ronja
Queen and Ronja Princess (LOA 76.96 m, beam 17.8 m, draught 5.0 m, depth
5.9 m, four Palfinger cranes, shelter deck); Salmon Business / Fish Farming
Expert on Ronja Storm (116 m) and the ScaleAQ-Mowi Feeder 500 order
(37 x 11.2 m, 500 t); AKVA group feed-barge range and the AC800 order for
Arctic Offshore Farming (64.8 m, 13 m); roadmap A3-01 (workboat 15 x 10 m).

Each export writes godot/assets/models/<dir>/<asset>_<LOD>.glb and a JSON
sidecar (tri_count, materials, bbox, per-part tris and measured hull
dimensions) for blender_asset_pipeline.py and tests/test_vessel_assets.py.
Node names (one material each, one draw call per part): Hull, Deck,
Superstructure, Windows, Machinery, Rubber, Crane, Safety. Materials map to
Godot .tres files in godot/assets/models/vessel_kit through the
.glb.import "use_external" settings. Bow along Blender +Y = Godot -Z.

Licence: in-house geometry and generated textures, no third-party assets.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

import bpy
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lk_hull as hull  # noqa: E402
import lk_meshkit as kit  # noqa: E402
import vessel_designs as designs  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(REPO, "godot", "assets", "models")
KIT_DIR = os.path.join(MODELS, "vessel_kit")
TEX_DIR = os.path.join(KIT_DIR, "textures")
PREFIX = "LK_Vessel"

# glTF material name -> (base colour hex, roughness, metallic)
MATERIALS = {
    "LK_VHullRed": ("#8E1F24", 0.55, 0.0),
    "LK_VHullOrange": ("#C9531F", 0.55, 0.0),
    "LK_VHullSlate": ("#34424B", 0.6, 0.0),
    "LK_VDeck": ("#4C5E52", 0.8, 0.0),
    "LK_VWhite": ("#E6E9E6", 0.5, 0.0),
    "LK_VGlass": ("#16232B", 0.08, 0.3),
    "LK_VSteel": ("#7D858B", 0.45, 0.6),
    "LK_VDark": ("#1E2124", 0.75, 0.0),
    "LK_VCrane": ("#E8B92A", 0.45, 0.0),
    "LK_VSafety": ("#E0662A", 0.5, 0.0),
    # Offshore fleet (F2-27): the crew boat's white hull, the subsea props.
    "LK_VHullWhite": ("#E9ECEA", 0.5, 0.0),
    "LK_VSubseaYellow": ("#E6BE2E", 0.45, 0.0),
    "LK_VSubseaOrange": ("#C9531F", 0.5, 0.0),
}

PART_MATERIALS = {
    "Deck": "LK_VDeck",
    "Superstructure": "LK_VWhite",
    "Windows": "LK_VGlass",
    "Machinery": "LK_VSteel",
    "Rubber": "LK_VDark",
    "Crane": "LK_VCrane",
    "Safety": "LK_VSafety",
}

# asset -> (export directory, hull material, spec dimensions for the sidecar)
ASSETS = {
    "workboat": ("workboat", "LK_VHullOrange"),
    "feed_barge": ("feed_barge", "LK_VHullSlate"),
    "feed_barge_large": ("feed_barge", "LK_VHullSlate"),
    "wellboat": ("wellboat", "LK_VHullRed"),
    "wellboat_large": ("wellboat", "LK_VHullRed"),
    "rib": ("rib", "LK_VSafety"),
    "offshore_service": ("offshore_service", "LK_VHullOrange"),
    "crew_boat": ("crew_boat", "LK_VHullWhite"),
    "rov": ("subsea", "LK_VSubseaYellow"),
    "auv": ("subsea", "LK_VSubseaOrange"),
}

# Per-asset parts beyond PART_MATERIALS, or a part on another material:
# the service vessel's walk-to-work gangway is its own node (F2-27 accept
# 4) at LOD0; the crew boat's deck is dark slate (spec §5.2 #34424B).
ASSET_PARTS = {
    "offshore_service": {"Gangway": "LK_VWhite"},
    "crew_boat": {"Deck": "LK_VHullSlate"},
}
# Per-asset part folds by detail level (2 = LOD0, 1 = LOD1, 0 = LOD2).
ASSET_ALIASES = {
    "offshore_service": {1: {"Gangway": "Superstructure"}, 0: {"Gangway": "Superstructure"}},
}
# Subsea props: no third LOD, no plate densifying (they are 2-4 m long).
SUBSEA = {"rov", "auv"}

LODS = {"LOD0": 2, "LOD1": 1, "LOD2": 0}
# LOD2 folds the small parts into the big ones: five draw calls at most.
LOD2_ALIASES = {"Machinery": "Superstructure", "Rubber": "Hull", "Safety": "Hull"}

# Roadmap A3-01 "triangle budgets met": per-asset ceilings (LOD0/LOD1/LOD2).
TRI_LIMITS = {
    "workboat": (20000, 5000, 1000),
    "feed_barge": (30000, 8000, 1500),
    "feed_barge_large": (40000, 10000, 2000),
    "wellboat": (45000, 12000, 2000),
    "wellboat_large": (60000, 15000, 2500),
    "rib": (6000, 1500, 400),
    # Farm-types spec §5.2 (F2-27); the subsea props have two LODs.
    "offshore_service": (30000, 6000, 800),
    "crew_boat": (8000, 1500, 300),
    "rov": (1200, 300, None),
    "auv": (600, 150, None),
}

# Parts that get baked AO in the vertex colour.
AO_PARTS = {"Hull", "Deck", "Superstructure", "Machinery", "Rubber", "Crane", "Safety", "Gangway"}
UV_PER_M = 1.0


def materials() -> dict:
    out = {}
    for name, (hexcode, rough, metal) in MATERIALS.items():
        out[name] = kit.make_material(name, kit.hex_rgba(hexcode), rough, metal)
    return out


def build_asset(asset: str, lod: str, mats: dict):
    d = LODS[lod]
    part_mats = dict(PART_MATERIALS, Hull=ASSETS[asset][1], **ASSET_PARTS.get(asset, {}))
    aliases = dict(LOD2_ALIASES if d == 0 else {}, **ASSET_ALIASES.get(asset, {}).get(d, {}))
    parts = hull.Parts(part_mats, aliases or None)
    # A design may name a part's pivot (the gangway's slewing axis): that
    # part's node gets its origin there, so Godot turns it about the axis.
    parts.pivots = {}
    spec = designs.DESIGNS[asset](parts, d)
    objs = []
    for name, mb in parts.used():
        obj = mb.build(name, mats, auto_uv=UV_PER_M)
        obj["lk_build"] = PREFIX
        if name in DENSIFY_PARTS and DENSIFY_EDGE_M.get(lod) and asset not in SUBSEA:
            densify(obj, DENSIFY_EDGE_M[lod])
        if name in parts.pivots:
            px, py, pz = parts.pivots[name]
            obj.data.transform(mathutils.Matrix.Translation((-px, -py, -pz)))
            obj.location = (px, py, pz)
            obj["lk_pivot"] = [px, py, pz]
        objs.append(obj)
    return objs, spec


# Baked AO lives in the vertices: big flat faces (deck plates, deck-house
# walls, bulwarks) get split so the occlusion where they meet reads as a
# soft contact shadow instead of a smear across a whole wall.
DENSIFY_PARTS = {"Hull", "Deck", "Superstructure"}
DENSIFY_EDGE_M = {"LOD0": 1.6, "LOD1": 4.0}


def densify(obj, max_edge: float) -> None:
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for _ in range(6):
        long_edges = [e for e in bm.edges if e.calc_length() > max_edge]
        if not long_edges:
            break
        bmesh.ops.subdivide_edges(bm, edges=long_edges, cuts=1, use_grid_fill=True)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def bake_ao(objs) -> None:
    """Cycles AO bake into a per-vertex colour (COLOR_0 in the glTF)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    if scene.world is None:
        scene.world = bpy.data.worlds.new("LK_World")
    scene.world.light_settings.distance = 2.5
    for obj in objs:
        attr = obj.data.color_attributes.new("AO", "BYTE_COLOR", "POINT")
        obj.data.color_attributes.active_color = attr
        obj.data.color_attributes.render_color_index = obj.data.color_attributes.active_color_index
    targets = [o for o in objs if o.name.split(".")[0] in AO_PARTS]
    others = [o for o in scene.objects if o not in targets and not o.hide_render]
    for obj in others:
        obj.hide_render = True
    bpy.ops.object.select_all(action="DESELECT")
    for obj in targets:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    try:
        bpy.ops.object.bake(type="AO", target="VERTEX_COLORS", use_selected_to_active=False)
    finally:
        for obj in others:
            obj.hide_render = False
    for obj in objs:
        if obj not in targets:
            for c in obj.data.color_attributes["AO"].data:
                c.color = (1.0, 1.0, 1.0, 1.0)


def white_vertex_colour(objs) -> None:
    """LOD2 is not baked; give it a white COLOR_0 so every LOD has the same
    vertex layout (white reads as 'no occlusion' in the shader)."""
    for obj in objs:
        attr = obj.data.color_attributes.new("AO", "BYTE_COLOR", "POINT")
        obj.data.color_attributes.active_color = attr
        for c in attr.data:
            c.color = (1.0, 1.0, 1.0, 1.0)


def export_lod(asset: str, lod: str, objs, spec, out_dir: str) -> dict:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objs:
        obj.select_set(True)
    path = os.path.join(out_dir, f"{asset}_{lod}.glb")
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=False,
        export_yup=True,
        export_texcoords=True,
        export_normals=True,
        export_materials="EXPORT",
        export_vertex_color="ACTIVE",
        export_all_vertex_colors=False,
        export_cameras=False,
        export_lights=False,
        export_extras=False,
    )
    tris = {o.name.split(".")[0]: kit.mesh_tris(o) for o in objs}
    used = sorted({slot.material.name for o in objs for slot in o.material_slots if slot.material})
    lo, hi = kit.world_bbox(objs)
    hlo, hhi = kit.world_bbox([o for o in objs if o.name.split(".")[0] == "Hull"])
    bbox_godot = [round(hi[0] - lo[0], 4), round(hi[2] - lo[2], 4), round(hi[1] - lo[1], 4)]
    data = {
        "asset": asset,
        "lod": lod,
        "tri_count": sum(tris.values()),
        "material_count": len(used),
        "materials": used,
        "bbox_size": bbox_godot,
        "parts": tris,
        "dimensions": {
            "length_m": round(hhi[1] - hlo[1], 3),
            "beam_m": round(hhi[0] - hlo[0], 3),
            "draught_m": round(-lo[2], 3),
            "air_draught_m": round(hi[2], 3),
            "bow_y_m": round(hhi[1], 3),
            "stern_y_m": round(hlo[1], 3),
        },
        "file": os.path.basename(path),
        **({"pivots": {o.name.split(".")[0]: [round(o["lk_pivot"][0], 3), round(o["lk_pivot"][2], 3),
                                              round(-o["lk_pivot"][1], 3)] for o in objs if "lk_pivot" in o}}
           if any("lk_pivot" in o for o in objs) else {}),
        "blender_version": bpy.app.version_string,
    }
    with open(os.path.join(out_dir, f"{asset}_{lod}.json"), "w") as f:
        json.dump(data, f, indent=2, sort_keys=True)
        f.write("\n")
    print(f"TRIS: {data['tri_count']}")
    print(f"BBOX: {' '.join(str(v) for v in bbox_godot)}")
    print(f"MATERIALS: {len(used)}")
    print(f"VESSEL_EXPORT {asset} {lod} {path} tris={data['tri_count']} dims={data['dimensions']} parts={tris}")
    limit = TRI_LIMITS[asset][list(LODS).index(lod)]
    if limit is not None and data["tri_count"] > limit:
        raise SystemExit(f"{asset} {lod} has {data['tri_count']} triangles, over its {limit} budget")
    return data


# --------------------------------------------------------------------------
# Textures (tiling, generated in code)
# --------------------------------------------------------------------------

def _save_image(name, path, rgba_rows, size_x, size_y, non_color=False):
    import numpy as np
    img = bpy.data.images.new(name, size_x, size_y, alpha=True, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(np.asarray(rgba_rows, dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def build_textures() -> list[str]:
    """Paint weathering for every vessel material, one 4 m x 4 m tile:
    R grime (multiplies albedo), G roughness offset, B run-down streaks,
    plus a normal map of the plate seams and weld beads."""
    import numpy as np
    os.makedirs(TEX_DIR, exist_ok=True)
    n = 512
    tile_m = 4.0
    u = (np.arange(n) + 0.5) / n
    uu, vv = np.meshgrid(u, u)          # uu across (metres / 4), vv up

    def tiled_noise(cells_x, cells_y, seed):
        r = np.random.default_rng(seed).random((cells_y, cells_x))
        ix = np.arange(n) * cells_x / n
        iy = np.arange(n) * cells_y / n
        x0 = np.floor(ix).astype(int) % cells_x
        y0 = np.floor(iy).astype(int) % cells_y
        fx = ix - np.floor(ix)
        fy = iy - np.floor(iy)
        fx = fx * fx * (3 - 2 * fx)
        fy = fy * fy * (3 - 2 * fy)
        x1 = (x0 + 1) % cells_x
        y1 = (y0 + 1) % cells_y
        a = r[y0][:, x0] * (1 - fx)[None, :] + r[y0][:, x1] * fx[None, :]
        b = r[y1][:, x0] * (1 - fx)[None, :] + r[y1][:, x1] * fx[None, :]
        return a * (1 - fy)[:, None] + b * fy[:, None]

    # Plate seams: horizontal strakes every 2 m, butts every 4 m (staggered).
    px = 1.0 / n
    seam_h = np.exp(-((np.minimum(np.mod(vv * tile_m, 2.0), 2.0 - np.mod(vv * tile_m, 2.0))) / (1.4 * px * tile_m)) ** 2)
    stagger = np.where(np.mod(vv * tile_m, 4.0) < 2.0, 0.0, 0.5)
    bu = np.mod(uu + stagger, 1.0)
    seam_v = np.exp(-((np.minimum(bu, 1.0 - bu) * tile_m) / (1.4 * px * tile_m)) ** 2)
    seams = np.clip(seam_h + seam_v, 0.0, 1.0)
    grime = 0.55 * tiled_noise(4, 4, 21) + 0.3 * tiled_noise(16, 16, 22) + 0.15 * tiled_noise(64, 64, 23)
    # Streaks: thin vertical runs that start at random heights and fade down.
    cols = tiled_noise(96, 1, 31) ** 3.0
    starts = tiled_noise(96, 2, 32)
    fade = np.clip(1.0 - np.mod(vv - starts, 1.0) * 2.2, 0.0, 1.0)
    streaks = np.clip(cols * fade * 2.5 + 0.35 * seam_h * cols, 0.0, 1.0)
    rgba = np.stack([
        np.clip(0.84 + 0.22 * grime - 0.25 * seams, 0.0, 1.0),
        np.clip(0.5 + 0.35 * (grime - 0.5) + 0.2 * streaks, 0.0, 1.0),
        streaks,
        np.ones_like(grime),
    ], axis=-1)
    written = []
    path = os.path.join(TEX_DIR, "vessel_weathering.png")
    _save_image("LK_vessel_weathering", path, rgba[::-1], n, n, non_color=True)
    written.append(path)
    height = 0.8 * seam_h + 0.6 * seam_v + 0.15 * tiled_noise(32, 32, 41)
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    strength = 2.2
    nx, ny, nz = -dx * strength, -dy * strength, np.ones_like(dx)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nx / ln * 0.5 + 0.5, ny / ln * 0.5 + 0.5, nz / ln * 0.5 + 0.5, np.ones_like(dx)], axis=-1)
    path = os.path.join(TEX_DIR, "vessel_plates_normal.png")
    _save_image("LK_vessel_plates_normal", path, normal[::-1], n, n, non_color=True)
    written.append(path)
    return written


def lods_for(asset: str) -> list[str]:
    """The LODs an asset has (the subsea props stop at LOD1)."""
    return [lod for lod, lim in zip(LODS, TRI_LIMITS[asset]) if lim is not None]


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset", choices=list(ASSETS), default=None)
    ap.add_argument("--lod", choices=list(LODS), default=None)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--textures", action="store_true")
    ap.add_argument("--out", default=None, help="output directory (default: the asset's models dir)")
    ap.add_argument("--variant", default=None)  # accepted for the pipeline CLI
    return ap.parse_args(argv)


def main():
    args = parse_args()
    if args.textures:
        for path in build_textures():
            print(f"TEXTURE {path}")
    mats = materials()
    assets = [args.asset] if args.asset else list(ASSETS)
    for asset in assets:
        out_dir = args.out or os.path.join(MODELS, ASSETS[asset][0])
        os.makedirs(out_dir, exist_ok=True)
        for lod in ([args.lod] if args.lod else lods_for(asset)):
            kit.clear_prefixed(PREFIX)
            objs, spec = build_asset(asset, lod, mats)
            if lod != "LOD2" and not args.no_bake:
                bake_ao(objs)
            else:
                white_vertex_colour(objs)
            export_lod(asset, lod, objs, spec, out_dir)
    kit.clear_prefixed(PREFIX)


if __name__ == "__main__":
    main()
