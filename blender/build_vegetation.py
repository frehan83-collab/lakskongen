"""Build the fjord trees (roadmap G2-02) and export glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_vegetation.py -- [--asset spruce|birch]
      [--lod LOD0|LOD1] [--out DIR]

Trees for a camera 100-3,000 m away, drawn by the thousand through
MultiMesh (godot/scripts/environment/Vegetation.gd), so they are built for
silhouette and soft volumetric light, not for close-ups:

  spruce   Norway spruce of a planted coastal stand, 16 m at scale 1: a
           narrow cone of six drooping, star-rimmed branch tiers on a
           tapered trunk. LOD1 is one six-sided cone.
  birch    Downy birch, 11 m at scale 1: a leaning, white-barked trunk
           under a crown of five lumpy leaf masses. LOD1 is one mass on a
           three-sided stem.

Shading data the vegetation shader reads:
  UV.x  part: 0 trunk, 1 foliage (crown or needles);
  UV.y  ambient occlusion 0-1 (inner and lower foliage darker); Blender's
        V is written as 1 - AO, which glTF/Godot flip back.
Normals: foliage normals point out of the crown (a blend of the leaf mass's
and the crown's centre), so the low-poly masses shade as soft volumes rather
than facets.

Each export writes <name>_<LOD>.glb plus a <name>_<LOD>.json sidecar
(tri_count, material_count, bbox_size), checked against
blender/asset_manifest.json by tests/test_vegetation_assets.py.

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
OUT_DIR = os.path.join(REPO, "godot", "assets", "models", "vegetation")
PREFIX = "LK_Tree"

# One material; Godot draws the trees with vegetation_shader.gdshader.
MATERIALS = {"LK_Tree": ("#3A5641", 0.9, 0.0)}

SPRUCE_H = 16.0
SPRUCE_CROWN_BASE = 1.8
SPRUCE_R = 2.5
SPRUCE_TIERS = 6
BIRCH_H = 11.0

LODS = ("LOD0", "LOD1")


def _hash01(i: int, salt: int) -> float:
    """Deterministic value in [0, 1) (no RNG state)."""
    h = (i * 0x9E3779B1 ^ salt * 0x85EBCA77) & 0xFFFFFFFF
    h ^= h >> 15
    h = (h * 0x2C1B3C6D) & 0xFFFFFFFF
    h ^= h >> 12
    return (h & 0xFFFFFF) / float(0x1000000)


class Tree:
    """Vertices, triangles, per-corner UVs and per-vertex normals."""

    def __init__(self) -> None:
        self.mb = kit.MeshBuilder()
        self.normals: list[tuple] = []
        self.uv: list[tuple] = []

    def vert(self, co, normal, part: float, ao: float) -> int:
        self.normals.append(kit.v_norm(normal))
        self.uv.append((part, 1.0 - max(0.0, min(1.0, ao))))
        return self.mb.vert(co)

    def tri(self, a: int, b: int, c: int) -> None:
        # Blender faces are counter-clockwise seen from the outside.
        self.mb.face((a, b, c), "LK_Tree", smooth=True, uv=(self.uv[a], self.uv[b], self.uv[c]))

    def build(self, name: str, mats: dict):
        obj = self.mb.build(name, mats)
        mesh = obj.data
        mesh.normals_split_custom_set_from_vertices(self.normals)
        return obj


def trunk(t: Tree, path, radii, sides: int, ao_base: float = 0.55) -> None:
    """A tapered tube along `path` (Blender coords, Z up), open at both ends."""
    rings = []
    for k, (p, r) in enumerate(zip(path, radii)):
        ring = []
        for s in range(sides):
            a = TAU * s / sides
            d = (math.cos(a), math.sin(a), 0.0)
            ring.append(t.vert((p[0] + r * d[0], p[1] + r * d[1], p[2]), d, 0.0, ao_base + 0.4 * k / max(len(path) - 1, 1)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        lo, hi = rings[k], rings[k + 1]
        for s in range(sides):
            a, b = lo[s], lo[(s + 1) % sides]
            c, d = hi[(s + 1) % sides], hi[s]
            t.tri(a, b, c)
            t.tri(a, c, d)


def spruce(lod: str) -> Tree:
    t = Tree()
    if lod == "LOD1":
        sides = 6
        apex = t.vert((0.0, 0.0, SPRUCE_H), (0.0, 0.0, 1.0), 1.0, 1.0)
        base_z = SPRUCE_CROWN_BASE - 0.4
        rim = []
        for s in range(sides):
            a = TAU * s / sides
            d = (math.cos(a), math.sin(a), 0.0)
            rim.append(t.vert((SPRUCE_R * d[0], SPRUCE_R * d[1], base_z), (d[0] * 0.8, d[1] * 0.8, 0.6), 1.0, 0.75))
        under = t.vert((0.0, 0.0, base_z + 1.2), (0.0, 0.0, -1.0), 1.0, 0.3)
        for s in range(sides):
            t.tri(rim[s], rim[(s + 1) % sides], apex)
            t.tri(rim[(s + 1) % sides], rim[s], under)
        return t
    trunk(t, [(0.0, 0.0, -0.4), (0.0, 0.0, SPRUCE_CROWN_BASE + 2.0), (0.0, 0.0, SPRUCE_H * 0.9)], [0.26, 0.2, 0.05], 5)
    rim_n = 8
    span = SPRUCE_H - SPRUCE_CROWN_BASE
    for i in range(SPRUCE_TIERS):
        f0 = i / SPRUCE_TIERS
        zb = SPRUCE_CROWN_BASE + span * f0 * 0.92
        tier_h = span / SPRUCE_TIERS * 1.55
        za = min(zb + tier_h, SPRUCE_H) if i < SPRUCE_TIERS - 1 else SPRUCE_H
        r = SPRUCE_R * (1.0 - f0) ** 0.95 + 0.3
        ao_rim = 0.62 + 0.38 * f0
        apex = t.vert((0.0, 0.0, za), (0.0, 0.0, 1.0), 1.0, min(1.0, ao_rim + 0.15) if i == SPRUCE_TIERS - 1 else 0.45)
        rim = []
        turn = 0.37 * i
        for s in range(rim_n):
            a = TAU * s / rim_n + turn
            tip = s % 2 == 0
            rr = r * (1.0 if tip else 0.7) * (0.92 + 0.16 * _hash01(i * 16 + s, 3))
            d = (math.cos(a), math.sin(a), 0.0)
            droop = 0.22 * rr if tip else 0.05 * rr
            n = (d[0] * 0.78, d[1] * 0.78, 0.62)
            rim.append(t.vert((rr * d[0], rr * d[1], zb - droop), n, 1.0, ao_rim * (1.0 if tip else 0.85)))
        under = t.vert((0.0, 0.0, zb + tier_h * 0.3), (0.0, 0.0, -1.0), 1.0, 0.3)
        for s in range(rim_n):
            t.tri(rim[s], rim[(s + 1) % rim_n], apex)
            t.tri(rim[(s + 1) % rim_n], rim[s], under)
    return t


# Unit icosahedron.
_PHI = (1.0 + math.sqrt(5.0)) / 2.0
_ICO_V = [(-1, _PHI, 0), (1, _PHI, 0), (-1, -_PHI, 0), (1, -_PHI, 0), (0, -1, _PHI), (0, 1, _PHI),
          (0, -1, -_PHI), (0, 1, -_PHI), (_PHI, 0, -1), (_PHI, 0, 1), (-_PHI, 0, -1), (-_PHI, 0, 1)]
_ICO_F = [(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2),
          (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5),
          (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)]


def leaf_mass(t: Tree, centre, radius, squash: float, crown_centre, crown_lo: float, crown_hi: float, salt: int) -> None:
    verts = []
    for k, v in enumerate(_ICO_V):
        u = kit.v_norm(v)
        jitter = 0.84 + 0.3 * _hash01(k, salt)
        p = (centre[0] + u[0] * radius * jitter, centre[1] + u[1] * radius * jitter,
             centre[2] + u[2] * radius * jitter * squash)
        out_mass = kit.v_norm(kit.v_sub(p, centre))
        out_crown = kit.v_norm(kit.v_sub(p, crown_centre))
        n = kit.v_add(kit.v_mul(out_mass, 0.45), kit.v_mul(out_crown, 0.55))
        height = (p[2] - crown_lo) / max(crown_hi - crown_lo, 1e-3)
        inner = max(0.0, kit.v_dot(out_mass, kit.v_norm(kit.v_sub(crown_centre, centre))))
        ao = (0.5 + 0.5 * max(0.0, min(1.0, height))) * (1.0 - 0.35 * inner)
        verts.append(t.vert(p, n, 1.0, ao))
    for f in _ICO_F:
        t.tri(verts[f[0]], verts[f[1]], verts[f[2]])


BIRCH_MASSES = [  # (x, y, z, radius) in metres, Blender Z up
    (0.35, 0.10, 8.7, 2.2),
    (-1.05, 0.55, 7.2, 1.85),
    (1.45, -0.45, 7.0, 1.9),
    (0.15, 1.45, 6.5, 1.6),
    (0.65, -1.35, 6.0, 1.55),
]


def birch(lod: str) -> Tree:
    t = Tree()
    crown_centre = (0.3, 0.0, 7.3)
    if lod == "LOD1":
        trunk(t, [(0.0, 0.0, -0.3), (0.3, 0.1, 6.0)], [0.17, 0.1], 3)
        leaf_mass(t, crown_centre, 2.9, 0.95, (0.3, 0.0, 6.5), 4.6, 10.4, 91)
        return t
    trunk(t, [(0.0, 0.0, -0.4), (0.18, 0.06, 3.6), (0.32, 0.12, 6.4), (0.4, 0.15, 8.6)], [0.19, 0.14, 0.09, 0.04], 5)
    for i, (x, y, z, r) in enumerate(BIRCH_MASSES):
        leaf_mass(t, (x, y, z), r, 0.82, crown_centre, 4.4, 10.8, 17 + 13 * i)
    return t


ASSETS = {
    "spruce": ("Spruce", spruce),
    "birch": ("Birch", birch),
}


def export(asset: str, lod: str, mats: dict, out_dir: str) -> dict:
    kit.clear_prefixed(PREFIX)
    node, fn = ASSETS[asset]
    tree = fn(lod)
    obj = tree.build(node, mats)
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
