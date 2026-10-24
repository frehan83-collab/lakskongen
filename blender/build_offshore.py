"""Build the offshore kit (farm-types spec §5.2): the OF1-class
semi-submersible unit (item F2-26), and export glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_offshore.py -- [--asset of1_class] [--lod LOD0|LOD1|LOD2]
      [--out DIR] [--no-bake] [--textures] [--atlas-out PATH] [--sides 12|8]
      [--tri-limit-scale S]

Geometry is blender/offshore_designs.py (see its docstring for the layout
and sources). One kit material (spec §5 "one material per kit"): every
solid face uses LK_OffshoreKit, a trim atlas of horizontal strips (--textures
writes godot/assets/models/facility_kit/textures/offshore_atlas.png, with
the land kit's strip code in lk_trim_atlas.py); the net uses LK_OffshoreNet
(pen_net_shader, alpha-to-coverage). The vertex colour (COLOR_0.r) is
ambient occlusion baked by Cycles; the second UV set (TEXCOORD_1.x) is the
build part group 0..5 (Pontoons, Columns, Rings, Bracing, Topsides, Net) for
the yard picture-in-picture and the timelapse (offshore_kit.gdshader
discards groups at or past `build_stage`).

Nodes (the Godot API, spec §5.2): LOD0 has Hull, Columns, Bracing,
Topsides, Net, Bulkhead (the sliding bulkhead, frame and net, pivoting on
the unit's axis), Cranes (an empty) with CraneA and CraneB (origins on
their slew axes), NavLights and Beacon (an empty at the mast top). LOD1 and
LOD2 merge every solid into Hull beside Net; LOD2 keeps only what stands
above z = -3 m (the water is opaque).

--sides 8 builds the 8-gon read of ref O2 for the evidence side-by-side
only; the committed asset is the owner's 12-gon.

Each export writes godot/assets/models/offshore/<asset>_<LOD>.glb and a JSON
sidecar (triangles per node, part and group; materials and surfaces;
bbox; measured dimensions; swatches; markers and pivots) for
blender_asset_pipeline.py and tests/test_offshore_assets.py. A LOD over its
spec budget stops the build.

Licence: in-house geometry and generated textures, no third-party assets.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys

import bpy
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lk_meshkit as kit  # noqa: E402
import lk_trim_atlas as trim  # noqa: E402
import offshore_designs as designs  # noqa: E402
from offshore_designs import GROUP_NAMES, M, N  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(REPO, "godot", "assets", "models")
OUT_DIR = os.path.join(MODELS, "offshore")
ATLAS_PATH = os.path.join(MODELS, "facility_kit", "textures", "offshore_atlas.png")
PREFIX = "LK_Offshore"

TILE_M = 16.0
ATLAS_PX = 1024
STRIP_ROWS = 32

# Strip rows, top of the image first: name, sRGB swatch, roughness (atlas
# alpha; None = glows, alpha 1.0), pattern, period (m), depth. Periods divide
# TILE_M. Spec §5.2 swatches: hull #E6BE2E, splash zone #B8902A, topsides
# #E9ECEA / #80838D, silos #777887, nav lights #FFC23A.
STRIPS = [
    ("hull_yellow", "#E6BE2E", 0.50, "panel", 4.0, 0.035),
    ("hull_splash", "#B8902A", 0.62, "panel", 4.0, 0.045),
    ("grating", "#7F868B", 0.82, "grating", 0.25, 0.12),
    ("deck_plate", "#6C7176", 0.80, "seam", 1.6, 0.06),
    ("white", "#E9ECEA", 0.55, "seam", 1.0, 0.05),
    ("grey", "#80838D", 0.55, "seam", 1.0, 0.06),
    ("silo", "#777887", 0.50, "seam", 1.6, 0.05),
    ("roof", "#4A5056", 0.72, "seam", 0.5, 0.08),
    ("window", "#F2D8A0", None, "flat", 0.0, 0.0),
    ("steel", "#9AA0A6", 0.45, "flat", 0.0, 0.0),
    ("crane", "#80838D", 0.50, "flat", 0.0, 0.0),
    ("nav_light", "#FFC23A", None, "flat", 0.0, 0.0),
    ("dark", "#1C1E20", 0.88, "flat", 0.0, 0.0),
    ("safety", "#D9622B", 0.52, "flat", 0.0, 0.0),
    ("door", "#6B7177", 0.60, "seam", 0.25, 0.08),
    ("hull_slate", "#34424B", 0.52, "panel", 4.0, 0.035),
    ("chain", "#3A3F44", 0.78, "seam", 0.5, 0.10),
    ("fibre", "#C8C2B0", 0.70, "corrugated", 0.25, 0.06),
]
STRIP_INDEX = {s[0]: i for i, s in enumerate(STRIPS)}
# The net is not on the atlas: LK_OffshoreNet's base colour (spec #A8ADB0).
NET_COLOUR = "#A8ADB0"

ASSETS = list(designs.DESIGNS)
LODS = ["LOD0", "LOD1", "LOD2"]
ASSET_LODS = {
    "of1_class": ["LOD0", "LOD1", "LOD2"],
    "havfarm_class": ["LOD0", "LOD1", "LOD2"],
    "mooring_line_kit": ["LOD0", "LOD1"],
}
# Spec §5.2 budgets (LOD0 / LOD1 / LOD2), confirmed by the F2-18 read gate.
TRI_LIMITS = {
    "of1_class": (60000, 12000, 1500),
    "havfarm_class": (60000, 12000, 1500),
    "mooring_line_kit": (400, 60, None),
}
# Spec §5.2 swatch per part: (strip, spec hex).
SPEC_SWATCHES = {
    "of1_class": {
        "Hull": ("hull_yellow", "#E6BE2E"),
        "SplashZone": ("hull_splash", "#B8902A"),
        "Deckhouse": ("white", "#E9ECEA"),
        "PowerHouse": ("grey", "#80838D"),
        "Silos": ("silo", "#777887"),
        "NavLights": ("nav_light", "#FFC23A"),
    },
    "havfarm_class": {
        "Hull": ("hull_slate", "#34424B"),
        "Deckhouse": ("white", "#E9ECEA"),
        "Rails": ("hull_yellow", "#E6BE2E"),
        "Silos": ("silo", "#777887"),
        "NavLights": ("nav_light", "#FFC23A"),
    },
    "mooring_line_kit": {
        "Chain": ("chain", "#3A3F44"),
        "Fibre": ("fibre", "#C8C2B0"),
        "Anchor": ("chain", "#3A3F44"),
    },
}
# Spec §5.2 node names, LOD0.
NODES_LOD0 = ["Hull", "Columns", "Bracing", "Topsides", "Net", "Bulkhead", "Cranes", "CraneA", "CraneB",
              "NavLights", "Beacon", "W2WLanding"]
AO_DISTANCE = 4.0


def strip_v(name: str) -> float:
    return trim.strip_v(STRIP_INDEX[name], STRIP_ROWS)


def atlas_rows():
    return trim.atlas_rows(STRIPS, TILE_M, ATLAS_PX, STRIP_ROWS)


_STRIP_COLOURS: dict = {}


def strip_colours():
    if not _STRIP_COLOURS:
        _STRIP_COLOURS.update(trim.strip_colours(STRIPS, atlas_rows(), ATLAS_PX, STRIP_ROWS))
    return _STRIP_COLOURS


# --------------------------------------------------------------------------
# Measurements (spec dimensions, from the geometry)
# --------------------------------------------------------------------------

def _columns(vs, sides, z_lo, z_hi):
    """Per column (nearest polygon vertex): centroid and radius of the
    vertices between z_lo and z_hi."""
    groups: dict[int, list] = {}
    for v in vs:
        if z_lo <= v[2] <= z_hi:
            k = round(math.atan2(v[1], v[0]) / (math.tau / sides)) % sides
            groups.setdefault(k, []).append(v)
    out = {}
    for k, pts in groups.items():
        cx = sum(p[0] for p in pts) / len(pts)
        cy = sum(p[1] for p in pts) / len(pts)
        out[k] = (cx, cy, max(math.hypot(p[0] - cx, p[1] - cy) for p in pts))
    return out


def _clusters(vs, gap):
    """Single-linkage groups of points closer than gap in plan:
    [(cx, cy, points)] with the centroid of each."""
    parent = list(range(len(vs)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for i in range(len(vs)):
        for j in range(i + 1, len(vs)):
            if math.hypot(vs[i][0] - vs[j][0], vs[i][1] - vs[j][1]) < gap:
                parent[find(i)] = find(j)
    comps: dict[int, list] = {}
    for i, v in enumerate(vs):
        comps.setdefault(find(i), []).append(v)
    out = []
    for pts in comps.values():
        cx = sum(p[0] for p in pts) / len(pts)
        cy = sum(p[1] for p in pts) / len(pts)
        out.append((cx, cy, pts))
    return out


def _diag_d(u, sides):
    """Diameter of the first bay's diagonal, at its middle third."""
    vs = u.part_verts("Diagonals")
    top, bot = designs.vertex(0, designs.Z_TR - 1.2, sides=sides), designs.vertex(1, designs.Z_BR + 1.1, sides=sides)
    mid = [v for v in vs if designs.Z_BR + 15.0 < v[2] < designs.Z_TR - 15.0 and
           abs(math.atan2(v[1], v[0]) - math.tau / (2 * sides)) < math.tau / (4 * sides)]
    return _tube_d(mid, (top, bot))


def _tube_d(vs, axis_pts):
    """Diameter of a straight tube from its vertices: twice the largest
    distance to the axis through axis_pts."""
    (ax, ay, az), (bx, by, bz) = axis_pts
    d = (bx - ax, by - ay, bz - az)
    ln = math.sqrt(sum(c * c for c in d))
    d = tuple(c / ln for c in d)
    best = 0.0
    for v in vs:
        w = (v[0] - ax, v[1] - ay, v[2] - az)
        t = sum(w[i] * d[i] for i in range(3))
        best = max(best, math.sqrt(max(sum(c * c for c in w) - t * t, 0.0)))
    return 2.0 * best


def _measure_havfarm(u: designs.Unit, lod: int) -> dict:
    every = [v for b in u.builders.values() for v in b.verts]
    no_mast = [v for key, b in u.builders.items() if key[4] not in ("Mast", "Lanterns", "Cranes")
               for v in b.verts]
    hull_vs = u.part_verts("Hull") + u.part_verts("Pontoons") + u.part_verts("WellBeams") + u.part_verts("Turret")
    house = u.part_verts("Deckhouse") + u.part_verts("DeckhouseTrim")
    turret = u.part_verts("Turret")
    # Group transverse well-dividing beams by X coordinate (1D clusters, gap 3 m).
    wb_vs = sorted([v for v in u.part_verts("WellBeams") if v[2] > 0.0], key=lambda p: p[0])
    beams: list[list] = []
    for v in wb_vs:
        if not beams or v[0] - beams[-1][-1][0] > 3.0:
            beams.append([v])
        else:
            beams[-1].append(v)
    well_lengths = []
    well_widths = []
    for a, b in zip(beams[:-1], beams[1:]):
        x0 = max(p[0] for p in a)
        x1 = min(p[0] for p in b)
        well_lengths.append(x1 - x0)
        well_widths.append(max(p[1] for p in a) - min(p[1] for p in a))
    length_m = max(v[0] for v in hull_vs) - min(v[0] for v in hull_vs)
    beam_m = max(v[1] for v in hull_vs) - min(v[1] for v in hull_vs)
    draft_m = -min(v[2] for v in hull_vs)
    deck_z = designs.HF_DECK_Z
    super_m = [
        max(v[0] for v in house) - min(v[0] for v in house),
        max(v[1] for v in house) - min(v[1] for v in house),
        max(v[2] for v in house) - deck_z,
    ]
    dims = {
        "length_m": length_m,
        "beam_m": beam_m,
        "circumradius_m": 0.5 * length_m,
        "freeboard_m": deck_z,
        "deck_z_m": deck_z,
        "draft_m": draft_m,
        "moulded_depth_m": deck_z + draft_m,
        "height_m": max(v[2] for v in house) - min(v[2] for v in hull_vs),
        "topsides_top_m": max(v[2] for v in no_mast),
        "air_draught_m": max(v[2] for v in every),
        "wells": len(well_lengths),
        "well_length_m": sum(well_lengths) / max(len(well_lengths), 1),
        "well_width_m": sum(well_widths) / max(len(well_widths), 1),
        "turret_d_m": max(v[1] for v in turret) - min(v[1] for v in turret),
        "superstructure_m": super_m,
        "deckhouse_m": super_m,
        "mooring_lines": designs.MOORING_SPREADS["havfarm_class"]["lines"],
        "mooring_anchor_radius_m": designs.MOORING_SPREADS["havfarm_class"]["anchor_radius_m"],
    }
    silos = u.part_verts("Silos")
    dims["silo_height_m"] = max(v[2] for v in silos) - deck_z
    clusters = _clusters([v for v in silos if deck_z + 1.5 <= v[2] <= deck_z + 8.5], 1.75)
    if clusters:
        dims["silos"] = len(clusters)
        dims["silo_d_m"] = max(2 * max(math.hypot(p[0] - cx, p[1] - cy) for p in pts) for cx, cy, pts in clusters)
    reach = []
    for node in ("CraneA", "CraneB"):
        if node in u.pivots:
            px, py, _pz = u.pivots[node]
            vs = [v for key, b in u.builders.items() if key[0] == node for v in b.verts]
            reach.append(max(math.hypot(v[0] - px, v[1] - py) for v in vs))
    if reach:
        dims["crane_reach_m"] = min(reach)
    net = u.part_verts("NetSides") + u.part_verts("NetBottom")
    dims["net_top_m"] = max(v[2] for v in net)
    dims["net_bottom_m"] = min(v[2] for v in net)
    dims["net_depth_m"] = -min(v[2] for v in net)
    return {k: (round(v, 3) if isinstance(v, float) else ([round(x, 3) for x in v] if isinstance(v, list) else v))
            for k, v in sorted(dims.items())}


def _measure_mooring_line(u: designs.Unit) -> dict:
    every = [v for b in u.builders.values() for v in b.verts]
    chain_vs = u.part_verts("Chain")
    anchor_vs = u.part_verts("Anchor")
    fairlead_ring = [v[2] for v in chain_vs if abs(v[0]) < 1e-4]
    of1_sp = designs.MOORING_SPREADS["of1_class"]
    hf_sp = designs.MOORING_SPREADS["havfarm_class"]
    dims = {
        "length_m": max(v[0] for v in every) - min(v[0] for v in every),
        "fairlead_z_m": sum(fairlead_ring) / len(fairlead_ring),
        "seabed_z_m": min(v[2] for v in anchor_vs),
        "anchor_length_m": max(v[0] for v in anchor_vs) - min(v[0] for v in anchor_vs),
        "of1_lines": of1_sp["lines"],
        "of1_pairs": of1_sp["pairs"],
        "of1_fairlead_z_m": of1_sp["fairlead_z_m"],
        "of1_anchor_radius_m": of1_sp["anchor_radius_m"],
        "havfarm_lines": hf_sp["lines"],
        "havfarm_groups": len(hf_sp["groups"]),
        "havfarm_anchor_radius_m": hf_sp["anchor_radius_m"],
    }
    return {k: (round(v, 3) if isinstance(v, float) else v) for k, v in sorted(dims.items())}


def measure(u: designs.Unit, lod: int, asset: str = "of1_class") -> dict:
    if asset == "havfarm_class":
        return _measure_havfarm(u, lod)
    if asset == "mooring_line_kit":
        return _measure_mooring_line(u)
    sides = u.sides
    every = [v for b in u.builders.values() for v in b.verts]
    # Structural height: the bottom of the feet to the top of the control
    # block and the silos (spec "overall height 68"); topsides_top_m adds
    # rails, stacks and the cranes' raised booms, air_draught_m the mast.
    no_mast = [v for key, b in u.builders.items() if key[4] not in ("Mast", "Lanterns", "RoofGear", "Cranes")
               for v in b.verts]
    dims = {
        "sides": sides,
        "draft_m": -min(v[2] for v in every),
        "height_m": max(v[2] for v in u.part_verts("Deckhouse") + u.part_verts("DeckhouseTrim") + u.part_verts("Silos"))
        - min(v[2] for v in every),
        "topsides_top_m": max(v[2] for v in no_mast),
        "air_draught_m": max(v[2] for v in every),
        "deck_z_m": designs.DECK_Z,
    }
    if lod == 2:
        cols = _columns(u.part_verts("ColumnLarge") + u.part_verts("ColumnSlim"), sides, 3.9, 15.3)
    else:
        cols = _columns(u.part_verts("ColumnLarge") + u.part_verts("ColumnSlim"), sides, -30.0, -15.0)
    dims["columns"] = len(cols)
    dims["circumradius_m"] = sum(math.hypot(c[0], c[1]) for c in cols.values()) / max(len(cols), 1)
    large = [c[2] * 2 for k, c in cols.items() if k % 2 == 0]
    slim = [c[2] * 2 for k, c in cols.items() if k % 2 == 1]
    dims["column_large_d_m"] = sum(large) / max(len(large), 1)
    dims["column_slim_d_m"] = sum(slim) / max(len(slim), 1)
    if lod == 0:
        feet = _columns(u.part_verts("Feet"), sides, designs.Z_BOTTOM, designs.Z_FOOT_CYL)
        dims["feet"] = len(feet)
        dims["foot_d_m"] = max(c[2] for c in feet.values()) * 2
        centre = [v for v in u.part_verts("ColumnCentre") if -30.0 <= v[2] <= -15.0]
        dims["column_centre_d_m"] = 2 * max(math.hypot(v[0], v[1]) for v in centre)
        a, b = designs.vertex(1, designs.Z_BR, sides=sides), designs.vertex(2, designs.Z_BR, sides=sides)
        mid = [v for v in u.part_verts("BottomRing") if abs(v[2] - designs.Z_BR) < 2.0 and
               abs(math.atan2(v[1], v[0]) - math.atan2((a[1] + b[1]) / 2, (a[0] + b[0]) / 2)) < 0.05]
        dims["bottom_ring_d_m"] = _tube_d(mid, (a, b))
        a, b = designs.vertex(1, designs.Z_MR, sides=sides), designs.vertex(2, designs.Z_MR, sides=sides)
        mid = [v for v in u.part_verts("MidRing") if abs(v[2] - designs.Z_MR) < 1.0 and
               abs(math.atan2(v[1], v[0]) - math.atan2((a[1] + b[1]) / 2, (a[0] + b[0]) / 2)) < 0.05]
        dims["mid_ring_d_m"] = _tube_d(mid, (a, b))
        a, b = designs.vertex(1, designs.Z_TR, sides=sides), designs.vertex(2, designs.Z_TR, sides=sides)
        mid = [v for v in u.part_verts("TopRing") if abs(v[2] - designs.Z_TR) < 2.0 and
               abs(math.atan2(v[1], v[0]) - math.atan2((a[1] + b[1]) / 2, (a[0] + b[0]) / 2)) < 0.05]
        dims["top_ring_d_m"] = _tube_d(mid, (a, b))
        g = [v for v in u.part_verts("Gantry") if 20.0 < abs(v[0]) < 40.0]
        dims["gantry_d_m"] = _tube_d(g, ((0.0, 0.0, designs.Z_TR), (1.0, 0.0, designs.Z_TR)))
        dims["diagonal_d_m"] = _diag_d(u, sides)
    house = u.part_verts("Deckhouse")
    walls = [v for v in house if v[2] < designs.DECK_Z + 6.0]
    trim_top = max(v[2] for v in house + u.part_verts("DeckhouseTrim"))
    dims["deckhouse_m"] = [max(v[0] for v in walls) - min(v[0] for v in walls),
                           max(v[1] for v in walls) - min(v[1] for v in walls),
                           trim_top - designs.DECK_Z]
    silos = u.part_verts("Silos")
    dims["silo_height_m"] = max(v[2] for v in silos) - designs.DECK_Z
    clusters = _clusters([v for v in silos if designs.DECK_Z + 2.0 < v[2] < designs.DECK_Z + 7.0], 0.9)
    if clusters:
        dims["silos"] = len(clusters)
        dims["silo_d_m"] = max(2 * max(math.hypot(p[0] - cx, p[1] - cy) for p in pts) for cx, cy, pts in clusters)
    reach = []
    for node in ("CraneA", "CraneB"):
        if node in u.pivots:
            px, py, _pz = u.pivots[node]
            vs = [v for key, b in u.builders.items() if key[0] == node for v in b.verts]
            reach.append(max(math.hypot(v[0] - px, v[1] - py) for v in vs))
    if reach:
        dims["crane_reach_m"] = min(reach)
    net = u.part_verts("NetSides")
    dims["net_top_m"] = max(v[2] for v in net)
    dims["net_bottom_m"] = min(v[2] for v in net)
    return {k: (round(v, 3) if isinstance(v, float) else ([round(x, 3) for x in v] if isinstance(v, list) else v))
            for k, v in sorted(dims.items())}


# --------------------------------------------------------------------------
# Build, bake, export
# --------------------------------------------------------------------------

def materials():
    out = {}
    for name, rgba, rough in ((M, (0.8, 0.8, 0.8, 1.0), 0.6), (N, kit.hex_rgba(NET_COLOUR), 0.9)):
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf is not None:
            bsdf.inputs["Base Color"].default_value = rgba
            bsdf.inputs["Roughness"].default_value = rough
        out[name] = mat
    return out


def node_of(node: str, lod: int) -> str:
    """LOD1 and LOD2 merge every solid into Hull beside Net."""
    if lod == 0 or node == "Net":
        return node
    return "Hull"


def merged_builders(u: designs.Unit, lod: int):
    """node -> (MeshBuilder with strip UVs, per-vertex groups, per-part
    triangles); a node's builders join in first-use order."""
    out: dict[str, tuple] = {}
    part_tris: dict[str, int] = {}
    group_tris = [0] * len(GROUP_NAMES)
    for (node, mat, strip, group, part), b in u.builders.items():
        if not b.faces:
            continue
        if strip is not None:
            kit.strip_uv(b, strip_v(strip), TILE_M)
        tris = b.tri_count
        part_tris[part] = part_tris.get(part, 0) + tris
        group_tris[group] += tris
        target = node_of(node, lod)
        if target not in out:
            out[target] = (kit.MeshBuilder(drop_degenerate=False), [], {})
        mb, groups, _ = out[target]
        base = len(mb.verts)
        mb.verts.extend(b.verts)
        groups.extend([group] * len(b.verts))
        for f, fm, fs, fu in zip(b.faces, b.face_mat, b.face_smooth, b.face_uv):
            mb.faces.append(tuple(i + base for i in f))
            mb.face_mat.append(mb.slot(b.slots[fm]))
            mb.face_smooth.append(fs)
            mb.face_uv.append(fu)
    return out, part_tris, group_tris


def build_objects(asset: str, lod: int, mats, sides: int):
    u = designs.Unit(sides=sides)
    designs.DESIGNS[asset](u, lod)
    dims = measure(u, lod, asset)
    merged, part_tris, group_tris = merged_builders(u, lod)
    objs = {}
    for name in sorted(merged, key=lambda n: (NODES_LOD0 + ["zz"]).index(n) if n in NODES_LOD0 else 99):
        mb, grp, _ = merged[name]
        # Nets keep their metre UVs; solids already sit on their strips.
        mb.face_uv = [uv if uv is not None else kit.box_uv(mb.verts, f, 1.0) for f, uv in zip(mb.faces, mb.face_uv)]
        obj = mb.build(name, mats)
        obj["lk_build"] = PREFIX
        attr = obj.data.attributes.new("lk_group", "INT", "POINT")
        attr.data.foreach_set("value", grp)
        if name in DENSIFY_NODES and DENSIFY_EDGE_M.get(lod):
            densify(obj, DENSIFY_EDGE_M[lod])
        if name in u.pivots:
            px, py, pz = u.pivots[name]
            obj.data.transform(mathutils.Matrix.Translation((-px, -py, -pz)))
            obj.location = (px, py, pz)
        objs[name] = obj
    return u, objs, dims, part_tris, group_tris


# Baked AO lives in the vertices: the topsides' big flat faces (house walls,
# decks, the silo deck) get split so the occlusion where they meet reads as
# a contact shadow instead of darkening a whole wall (the frame's members
# are already cut along their length). LOD1 merges into Hull.
DENSIFY_NODES = {"Topsides"}
DENSIFY_EDGE_M = {0: 2.5}


def densify(obj, max_edge: float, min_width: float = 1.2) -> None:
    """Split long edges of broad faces (shortest side >= min_width): walls,
    decks, roofs. Rails, members and silo staves stay as built."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(obj.data)

    def broad(face):
        return min(e.calc_length() for e in face.edges) >= min_width
    for _ in range(6):
        long_edges = [e for e in bm.edges if e.calc_length() > max_edge and any(broad(f) for f in e.link_faces)]
        if not long_edges:
            break
        bmesh.ops.subdivide_edges(bm, edges=long_edges, cuts=1, use_grid_fill=True)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def _set_vertex_colour(obj, value=None):
    attr = obj.data.color_attributes.get("AO") or obj.data.color_attributes.new("AO", "BYTE_COLOR", "POINT")
    obj.data.color_attributes.active_color = attr
    obj.data.color_attributes.render_color_index = obj.data.color_attributes.active_color_index
    if value is not None:
        for c in attr.data:
            c.color = value
    return attr


def split_nets(objs):
    """Net faces out of mixed objects (the Bulkhead) into a temporary object
    so the AO bake does not see them; returns {owner: net_obj}."""
    import bmesh
    out = {}
    for name, obj in objs.items():
        slots = [s.material.name for s in obj.material_slots]
        if name == "Net" or N not in slots or len(slots) == 1:
            continue
        net_index = slots.index(N)
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        keep = bmesh.new()
        keep.from_mesh(obj.data)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != net_index], context="FACES")
        bmesh.ops.delete(keep, geom=[f for f in keep.faces if f.material_index == net_index], context="FACES")
        mesh = bpy.data.meshes.new(name + "NetTmp")
        bm.to_mesh(mesh)
        bm.free()
        for s in obj.material_slots:
            mesh.materials.append(s.material)
        net_obj = bpy.data.objects.new(name + "NetTmp", mesh)
        net_obj["lk_build"] = PREFIX
        net_obj.matrix_world = obj.matrix_world
        bpy.context.scene.collection.objects.link(net_obj)
        keep.to_mesh(obj.data)
        keep.free()
        out[name] = net_obj
    return out


def bake_ao(objs) -> None:
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    if scene.world is None:
        scene.world = bpy.data.worlds.new("LK_World")
    scene.world.light_settings.distance = AO_DISTANCE
    # Every node but the net (mixed nodes lost their net faces to split_nets).
    solids = [o for o in objs if o.name != "Net"]
    others = [o for o in scene.objects if o not in solids and not o.hide_render]
    for obj in solids:
        _set_vertex_colour(obj)
    for obj in others:
        obj.hide_render = True
    bpy.ops.object.select_all(action="DESELECT")
    for obj in solids:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = solids[0]
    try:
        bpy.ops.object.bake(type="AO", target="VERTEX_COLORS", use_selected_to_active=False)
    finally:
        for obj in others:
            obj.hide_render = False


def write_groups(obj) -> None:
    """Part group per vertex (the lk_group attribute) into the second UV
    set (x = group); the attribute itself is not exported."""
    groups = [0] * len(obj.data.vertices)
    obj.data.attributes["lk_group"].data.foreach_get("value", groups)
    layer = obj.data.uv_layers.get("Group") or obj.data.uv_layers.new(name="Group")
    for poly in obj.data.polygons:
        for li in poly.loop_indices:
            layer.data[li].uv = (float(groups[obj.data.loops[li].vertex_index]), 0.0)
    obj.data.uv_layers.active_index = 0
    obj.data.attributes.remove(obj.data.attributes["lk_group"])


def assemble(objs, nets, baked: bool) -> None:
    """White vertex colour where nothing was baked, nets joined back into
    their owners, group UVs written."""
    for name, obj in objs.items():
        if not baked or name == "Net":
            _set_vertex_colour(obj, (1.0, 1.0, 1.0, 1.0))
    for name, net_obj in nets.items():
        _set_vertex_colour(net_obj, (1.0, 1.0, 1.0, 1.0))
        owner = objs[name]
        bpy.ops.object.select_all(action="DESELECT")
        owner.select_set(True)
        net_obj.select_set(True)
        bpy.context.view_layer.objects.active = owner
        bpy.ops.object.join()
    for obj in objs.values():
        write_groups(obj)


def add_empties(u: designs.Unit, objs, lod: int):
    """Beacon (mast top), W2WLanding (where the service vessel's gangway
    rests) and Cranes (parent of CraneA/CraneB), LOD0 only."""
    out = []
    if lod != 0:
        return out
    if "Beacon" in u.markers:
        beacon = bpy.data.objects.new("Beacon", None)
        beacon.location = u.markers["Beacon"]
        beacon["lk_build"] = PREFIX
        bpy.context.scene.collection.objects.link(beacon)
        out.append(beacon)
    if "W2WLanding" in u.markers:
        landing = bpy.data.objects.new("W2WLanding", None)
        landing.location = u.markers["W2WLanding"]
        landing["lk_build"] = PREFIX
        bpy.context.scene.collection.objects.link(landing)
        out.append(landing)
    if "CraneA" in objs:
        parent = bpy.data.objects.new("Cranes", None)
        parent["lk_build"] = PREFIX
        bpy.context.scene.collection.objects.link(parent)
        for name in ("CraneA", "CraneB"):
            objs[name].parent = parent
        out.append(parent)
    return out


def _godot(p):
    return [round(p[0], 3), round(p[2], 3), round(-p[1], 3)]


def export_lod(asset, lod_name, u, objs, empties, dims, part_tris, group_tris, out_dir, limit_scale, sides):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in list(objs.values()) + empties:
        obj.select_set(True)
    stem = asset if sides == designs.SIDES else f"{asset}_{sides}gon"
    path = os.path.join(out_dir, f"{stem}_{lod_name}.glb")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", use_selection=True, export_apply=False, export_yup=True,
        export_texcoords=True, export_normals=True, export_materials="EXPORT",
        export_vertex_color="ACTIVE", export_all_vertex_colors=False,
        export_cameras=False, export_lights=False, export_extras=False,
    )
    meshes = list(objs.values())
    lo, hi = kit.world_bbox(meshes)
    used = sorted({s.material.name for o in meshes for s in o.material_slots if s.material})
    node_tris = {name: kit.mesh_tris(o) for name, o in objs.items()}
    tri_count = sum(node_tris.values())
    colours = strip_colours()
    swatches = {}
    for part, (strip, spec) in sorted(SPEC_SWATCHES[asset].items()):
        swatches[part] = {"strip": strip, "hex": colours[strip], "spec": spec}
    data = {
        "asset": asset,
        "lod": lod_name,
        "tri_count": tri_count,
        "material_count": len(used),
        "materials": used,
        "surfaces": sum(len(o.material_slots) for o in meshes),
        "bbox_size": [round(hi[0] - lo[0], 4), round(hi[2] - lo[2], 4), round(hi[1] - lo[1], 4)],
        "nodes": node_tris,
        "node_order": [o.name for o in meshes] + [e.name for e in empties],
        "parts": dict(sorted(part_tris.items())),
        "groups": {GROUP_NAMES[i]: n for i, n in enumerate(group_tris)},
        "dimensions": dims,
        "swatches": swatches,
        "net_colour": NET_COLOUR,
        "markers": {k: _godot(v) for k, v in sorted(u.markers.items())},
        "pivots": {k: _godot(v) for k, v in sorted(u.pivots.items())} if lod_name == "LOD0" else {},
        "atlas": os.path.relpath(ATLAS_PATH, REPO).replace(os.sep, "/"),
        "file": os.path.basename(path),
        "blender_version": bpy.app.version_string,
    }
    if asset == "mooring_line_kit":
        data["spreads"] = designs.MOORING_SPREADS
    with open(os.path.join(out_dir, f"{stem}_{lod_name}.json"), "w") as f:
        json.dump(data, f, indent=2, sort_keys=True)
        f.write("\n")
    print(f"TRIS: {tri_count}")
    print(f"BBOX: {' '.join(str(v) for v in data['bbox_size'])}")
    print(f"MATERIALS: {len(used)}")
    print(f"OFFSHORE_EXPORT {asset} {lod_name} {path} tris={tri_count} nodes={node_tris} dims={dims}")
    limit = TRI_LIMITS[asset][LODS.index(lod_name)]
    if tri_count > limit * limit_scale:
        raise SystemExit(f"{asset} {lod_name} has {tri_count} triangles, over its {int(limit * limit_scale)} budget")
    return data


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--asset", choices=ASSETS, default=None)
    ap.add_argument("--lod", choices=LODS, default=None)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--textures", action="store_true")
    ap.add_argument("--atlas-out", default=ATLAS_PATH, help="where --textures writes the atlas")
    ap.add_argument("--out", default=None, help="output directory (default godot/assets/models/offshore)")
    ap.add_argument("--sides", type=int, default=designs.SIDES, choices=(8, 12),
                    help="polygon (12 = the owner's ruling; 8 = evidence variant only)")
    ap.add_argument("--tri-limit-scale", type=float, default=1.0, help="scale the budgets (tests use it)")
    ap.add_argument("--variant", default=None)  # accepted for the pipeline CLI
    return ap.parse_args(argv)


def main():
    args = parse_args()
    if args.textures:
        trim.write_png(args.atlas_out, atlas_rows(), ATLAS_PX)
        print(f"TEXTURE {args.atlas_out}")
    mats = materials()
    out_dir = args.out or OUT_DIR
    os.makedirs(out_dir, exist_ok=True)
    for asset in ([args.asset] if args.asset else ASSETS):
        if args.lod and args.lod not in ASSET_LODS[asset]:
            if args.asset:
                raise SystemExit(f"{asset} has no {args.lod}")
            continue
        for lod_name in ([args.lod] if args.lod else ASSET_LODS[asset]):
            lod = LODS.index(lod_name)
            kit.clear_prefixed(PREFIX)
            u, objs, dims, part_tris, group_tris = build_objects(asset, lod, mats, args.sides)
            nets = split_nets(objs)
            baked = lod_name != "LOD2" and asset != "mooring_line_kit" and not args.no_bake
            if baked:
                bake_ao(list(objs.values()))
            assemble(objs, nets, baked)
            empties = add_empties(u, objs, lod)
            export_lod(asset, lod_name, u, objs, empties, dims, part_tris, group_tris, out_dir,
                       args.tri_limit_scale, args.sides)
    kit.clear_prefixed(PREFIX)


if __name__ == "__main__":
    main()
