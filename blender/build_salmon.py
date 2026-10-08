"""Build a low-poly Atlantic salmon and export it for Godot.

Run inside Blender via the MCP server:
  python3 tools/blender_mcp_call.py execute_blender_code --file blender/build_salmon.py

Conventions (must match the Godot side):
- Unit fish: 0.9 m nose-to-tail-base, fins to ~1.1 m total.
- Blender axes: X right, Y length (nose at +Y, tail at -Y), Z up.
  Blender -Y maps to Godot +Z on glTF import (verified from the binary:
  tail-fin tips built at Blender y=+0.60 land at glTF z=-0.60), matching
  salmon.tscn (tail at +Z) and the MultiMesh tangential-heading code
  (nose along local -Z).
- Leaves the user's scene untouched: only LK_* datablocks are created,
  and all of them are removed after export.

Output: godot/assets/models/fish/salmon_v2.glb
"""

import math
import os

import bpy
import bmesh

# --------------------------------------------------------------------------
# Config
# --------------------------------------------------------------------------

REPO = "/Users/fredrik.j.hansen/lakskongen"
EXPORT_PATH = os.path.join(REPO, "godot", "assets", "models", "fish", "salmon_v2.glb")
PREFIX = "LK_Salmon"

BODY_HALF_LEN = 0.40  # nose -0.40 .. tail base +0.40 (0.8 m body)
RINGS = 13            # segments along the body (12 quads)
RADIAL = 10           # verts per ring

# (t, half_width_x, half_height_z) control points, nose t=0 -> tail t=1.
PROFILE = [
    (0.00, 0.010, 0.012),
    (0.12, 0.052, 0.075),
    (0.35, 0.062, 0.100),
    (0.55, 0.060, 0.105),
    (0.75, 0.042, 0.070),
    (0.90, 0.018, 0.032),
    (1.00, 0.010, 0.018),
]

SKIN = (0.42, 0.52, 0.60, 1.0)
SKIN_METALLIC = 0.55
SKIN_ROUGH = 0.38
BELLY = (0.82, 0.86, 0.89, 1.0)
FIN = (0.25, 0.30, 0.35, 1.0)

FIN_HALF_THICK = 0.004

# Tail fin outline in the YZ plane (y along body, z up), forked.
# Tail toward -Y (maps to Godot +Z).
TAIL_OUTLINE = [
    (-0.36, 0.030),
    (-0.60, 0.150),
    (-0.50, 0.015),
    (-0.60, -0.150),
    (-0.36, -0.030),
]

# Dorsal fin: small prism on the back, (y0, y1, z_base, z_apex).
DORSAL = (-0.26, -0.02, 0.095, 0.175)


# --------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------

def _profile_at(t):
    for k in range(len(PROFILE) - 1):
        t0, w0, h0 = PROFILE[k]
        t1, w1, h1 = PROFILE[k + 1]
        if t0 <= t <= t1:
            u = (t - t0) / (t1 - t0)
            s = 0.5 - 0.5 * math.cos(u * math.pi)  # smoothstep
            return (w0 + (w1 - w0) * s, h0 + (h1 - h0) * s)
    return (PROFILE[-1][1], PROFILE[-1][2])


def _make_material(name, color, metallic, roughness):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    principled = mat.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = color
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    return mat


def _cleanup_prefix():
    for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.materials):
        for x in list(coll):
            if x.name.startswith(PREFIX):
                coll.remove(x, do_unlink=True)


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def build():
    _cleanup_prefix()

    bm = bmesh.new()

    # --- Body rings -------------------------------------------------------
    ring_verts = []
    for i in range(RINGS):
        t = i / (RINGS - 1)
        y = BODY_HALF_LEN - 2.0 * BODY_HALF_LEN * t
        w, h = _profile_at(t)
        ring = []
        for j in range(RADIAL):
            a = 2.0 * math.pi * j / RADIAL
            v = bm.verts.new((w * math.cos(a), y, h * math.sin(a) + 0.005))
            ring.append(v)
        ring_verts.append(ring)
    bm.verts.ensure_lookup_table()

    def quad(v0, v1, v2, v3, mat):
        # Winding chosen so recalc_face_normals resolves outward.
        f = bm.faces.new((v0, v1, v2, v3))
        f.material_index = mat
        f.smooth = True
        return f

    def tri(v0, v1, v2, mat):
        f = bm.faces.new((v0, v1, v2))
        f.material_index = mat
        f.smooth = True
        return f

    SKIN_I, BELLY_I, FIN_I = 0, 1, 2

    for i in range(RINGS - 1):
        for j in range(RADIAL):
            j2 = (j + 1) % RADIAL
            # Belly faces (lower half, y in Blender-model local Z < 0.005).
            a_mid = 2.0 * math.pi * (j + 0.5) / RADIAL
            mat = BELLY_I if math.sin(a_mid) < 0.0 else SKIN_I
            quad(ring_verts[i][j], ring_verts[i][j2],
                 ring_verts[i + 1][j2], ring_verts[i + 1][j], mat)

    # Nose + tail caps (fans to center points).
    nose_c = bm.verts.new((0.0, BODY_HALF_LEN + 0.015, 0.005))
    for j in range(RADIAL):
        tri(nose_c, ring_verts[0][(j + 1) % RADIAL], ring_verts[0][j], SKIN_I)
    tail_c = bm.verts.new((0.0, -BODY_HALF_LEN - 0.005, 0.005))
    for j in range(RADIAL):
        tri(tail_c, ring_verts[-1][j], ring_verts[-1][(j + 1) % RADIAL], SKIN_I)

    # --- Tail fin (forked, thin prism) ------------------------------------
    def fin_layer(x_off):
        return [bm.verts.new((x_off, y, z)) for (y, z) in TAIL_OUTLINE]

    front = fin_layer(-FIN_HALF_THICK)
    back = fin_layer(+FIN_HALF_THICK)
    n = len(TAIL_OUTLINE)
    for k in range(1, n - 1):
        tri(front[0], front[k], front[k + 1], FIN_I)
        tri(back[0], back[k + 1], back[k], FIN_I)
    for k in range(n):
        k2 = (k + 1) % n
        quad(front[k], front[k2], back[k2], back[k], FIN_I)

    # --- Dorsal fin (small prism) -----------------------------------------
    y0, y1, zb, za = DORSAL
    ym = 0.5 * (y0 + y1)
    d_front = [bm.verts.new((-FIN_HALF_THICK, y, z))
               for (y, z) in ((y0, zb), (y1, zb), (ym, za))]
    d_back = [bm.verts.new((+FIN_HALF_THICK, y, z))
              for (y, z) in ((y0, zb), (y1, zb), (ym, za))]
    tri(d_front[0], d_front[2], d_front[1], FIN_I)
    tri(d_back[0], d_back[1], d_back[2], FIN_I)
    for k in range(3):
        k2 = (k + 1) % 3
        quad(d_front[k], d_front[k2], d_back[k2], d_back[k], FIN_I)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(PREFIX + "Mesh")
    bm.to_mesh(mesh)
    bm.free()

    skin = _make_material(PREFIX + "_Skin", SKIN, SKIN_METALLIC, SKIN_ROUGH)
    belly = _make_material(PREFIX + "_Belly", BELLY, 0.35, 0.40)
    fin = _make_material(PREFIX + "_Fin", FIN, 0.20, 0.60)
    mesh.materials.append(skin)
    mesh.materials.append(belly)
    mesh.materials.append(fin)

    obj = bpy.data.objects.new(PREFIX, mesh)
    bpy.context.scene.collection.objects.link(obj)

    tris = sum(p.loop_total - 2 for p in mesh.polygons)
    print("SALMON_TRIS:", tris)
    return obj


def export(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    os.makedirs(os.path.dirname(EXPORT_PATH), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=EXPORT_PATH,
        export_format="GLB",
        use_selection=True,
        export_materials="EXPORT",
        export_cameras=False,
        export_lights=False,
        export_yup=True,
    )
    print("SALMON_EXPORTED:", EXPORT_PATH, os.path.getsize(EXPORT_PATH))


def main():
    obj = build()
    try:
        export(obj)
    finally:
        # LK_KEEP=1 leaves the model in the scene (for viewport renders);
        # the caller must clean up afterwards.
        if os.environ.get("LK_KEEP") != "1":
            _cleanup_prefix()
    print("SALMON_DONE")


main()
