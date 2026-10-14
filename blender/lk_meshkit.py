"""Small deterministic mesh-building kit for the Lakskongen Blender scripts.

Geometry is generated from numbers (no modifiers, no randomness), so a build
script produces the same .glb byte for byte on every run. Units are metres,
Blender axes: X right, Y forward, Z up (the glTF exporter turns +Z into
Godot +Y and +Y into Godot -Z).

Licence: part of the Lakskongen repository (proprietary); no third-party code.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

import bpy

TAU = math.tau


# --------------------------------------------------------------------------
# Vector helpers (plain tuples keep this importable without mathutils tricks)
# --------------------------------------------------------------------------

def v_add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def v_sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def v_mul(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def v_dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def v_cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def v_len(a):
    return math.sqrt(v_dot(a, a))


def v_norm(a):
    n = v_len(a)
    return (a[0] / n, a[1] / n, a[2] / n) if n > 1e-12 else (0.0, 0.0, 1.0)


def polar(r, theta, z):
    return (r * math.cos(theta), r * math.sin(theta), z)


def any_perp(d):
    """A unit vector perpendicular to d."""
    ref = (0.0, 0.0, 1.0) if abs(d[2]) < 0.9 else (1.0, 0.0, 0.0)
    return v_norm(v_cross(d, ref))


# --------------------------------------------------------------------------
# Mesh builder
# --------------------------------------------------------------------------

@dataclass
class MeshBuilder:
    """Collects vertices and faces, then makes one Blender mesh object.

    Faces carry a material slot index, a smooth flag and optional per-corner
    UVs. Material slots are names; the object gets them in first-use order
    unless `slots` fixes the order up front.
    """

    slots: list[str] = field(default_factory=list)
    verts: list[tuple] = field(default_factory=list)
    faces: list[tuple] = field(default_factory=list)
    face_mat: list[int] = field(default_factory=list)
    face_smooth: list[bool] = field(default_factory=list)
    face_uv: list[tuple | None] = field(default_factory=list)

    def vert(self, co) -> int:
        self.verts.append((float(co[0]), float(co[1]), float(co[2])))
        return len(self.verts) - 1

    def slot(self, name: str) -> int:
        if name not in self.slots:
            self.slots.append(name)
        return self.slots.index(name)

    def face(self, idx, mat: str, smooth: bool = True, uv=None) -> None:
        self.faces.append(tuple(idx))
        self.face_mat.append(self.slot(mat))
        self.face_smooth.append(smooth)
        self.face_uv.append(tuple(uv) if uv is not None else None)

    @property
    def tri_count(self) -> int:
        return sum(len(f) - 2 for f in self.faces)

    def build(self, name: str, materials: dict) -> bpy.types.Object:
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.verts, [], self.faces)
        mesh.validate(clean_customdata=False)
        for slot_name in self.slots:
            mesh.materials.append(materials[slot_name])
        mesh.polygons.foreach_set("material_index", self.face_mat)
        mesh.polygons.foreach_set("use_smooth", self.face_smooth)
        if any(uv is not None for uv in self.face_uv):
            layer = mesh.uv_layers.new(name="UVMap")
            for poly, uvs in zip(mesh.polygons, self.face_uv):
                for k, loop_index in enumerate(poly.loop_indices):
                    layer.data[loop_index].uv = uvs[k] if uvs is not None else (0.0, 0.0)
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# --------------------------------------------------------------------------
# Primitive generators. All append into a MeshBuilder.
# --------------------------------------------------------------------------

def ring_tube(mb, R, z, r, n_around, n_sides, mat, phi_range=None, closed_phi=True,
              wobble=None, uv_scale=None):
    """A torus around the Z axis: pipe centreline radius R at height z, pipe
    radius r. phi_range=(a, b) keeps only part of the cross-section (an open
    ribbon, e.g. a stripe on top of a pipe). wobble(theta) -> (dR, dz)."""
    if phi_range is None:
        phis = [TAU * j / n_sides for j in range(n_sides)]
        wrap = True
    else:
        a, b = phi_range
        phis = [a + (b - a) * j / (n_sides - 1) for j in range(n_sides)]
        wrap = False
    rows = []
    for i in range(n_around):
        th = TAU * i / n_around
        dR, dz = wobble(th) if wobble else (0.0, 0.0)
        row = []
        for ph in phis:
            rr = R + dR + r * math.cos(ph)
            row.append(mb.vert(polar(rr, th, z + dz + r * math.sin(ph))))
        rows.append(row)
    ncols = len(phis) if wrap else len(phis) - 1
    for i in range(n_around):
        i2 = (i + 1) % n_around
        for j in range(ncols):
            j2 = (j + 1) % len(phis)
            uv = None
            if uv_scale:
                u0 = R * TAU * i / n_around * uv_scale
                u1 = R * TAU * (i + 1) / n_around * uv_scale
                v0, v1 = j / max(ncols, 1), (j + 1) / max(ncols, 1)
                uv = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
            mb.face((rows[i][j], rows[i2][j], rows[i2][j2], rows[i][j2]), mat, True, uv)


def path_frames(points, closed=False):
    """Parallel-transport frames (tangent, normal, binormal) along a polyline."""
    n = len(points)
    tangents = []
    for i in range(n):
        if closed:
            a, b = points[i - 1], points[(i + 1) % n]
        else:
            a = points[max(i - 1, 0)]
            b = points[min(i + 1, n - 1)]
        tangents.append(v_norm(v_sub(b, a)))
    normals = [any_perp(tangents[0])]
    for i in range(1, n):
        t0, t1 = tangents[i - 1], tangents[i]
        nrm = normals[-1]
        # Project the previous normal onto the plane of the new tangent.
        nrm = v_norm(v_sub(nrm, v_mul(t1, v_dot(nrm, t1))))
        normals.append(nrm)
    binormals = [v_cross(t, nn) for t, nn in zip(tangents, normals)]
    return tangents, normals, binormals


def path_tube(mb, points, r, n_sides, mat, closed=False, caps=True, smooth=True,
              radius_fn=None, uv_scale=None):
    """A tube of radius r along a polyline (ropes, hoses, poles)."""
    tangents, normals, binormals = path_frames(points, closed)
    rows = []
    for i, p in enumerate(points):
        rr = radius_fn(i) if radius_fn else r
        row = []
        for j in range(n_sides):
            ph = TAU * j / n_sides
            off = v_add(v_mul(normals[i], rr * math.cos(ph)), v_mul(binormals[i], rr * math.sin(ph)))
            row.append(mb.vert(v_add(p, off)))
        rows.append(row)
    seg = len(points) if closed else len(points) - 1
    dist = [0.0]
    for i in range(1, len(points)):
        dist.append(dist[-1] + v_len(v_sub(points[i], points[i - 1])))
    for i in range(seg):
        i2 = (i + 1) % len(points)
        for j in range(n_sides):
            j2 = (j + 1) % n_sides
            uv = None
            if uv_scale:
                d1 = dist[i2] if i2 > i else dist[-1] + v_len(v_sub(points[0], points[-1]))
                uv = [(dist[i] * uv_scale, j / n_sides), (dist[i] * uv_scale, (j + 1) / n_sides),
                      (d1 * uv_scale, (j + 1) / n_sides), (d1 * uv_scale, j / n_sides)]
            # Winding: round the tube first, then along it -> outward normals.
            mb.face((rows[i][j], rows[i][j2], rows[i2][j2], rows[i2][j]), mat, smooth, uv)
    if caps and not closed:
        mb.face(tuple(reversed(rows[0])), mat, False)
        mb.face(tuple(rows[-1]), mat, False)


def cylinder(mb, p0, p1, r, n_sides, mat, caps=True, smooth=True):
    path_tube(mb, [p0, p1], r, n_sides, mat, closed=False, caps=caps, smooth=smooth)


def box(mb, center, ax, ay, az, hx, hy, hz, mat, skip_bottom=False):
    """An oriented box: unit axes ax/ay/az (az = local up), half sizes."""
    corners = []
    for sz in (-1, 1):
        for sy in (-1, 1):
            for sx in (-1, 1):
                p = v_add(center, v_add(v_mul(ax, sx * hx), v_add(v_mul(ay, sy * hy), v_mul(az, sz * hz))))
                corners.append(mb.vert(p))
    c = corners
    quads = [
        (c[4], c[5], c[7], c[6]),  # top (+z)
        (c[0], c[1], c[5], c[4]),  # -y
        (c[2], c[6], c[7], c[3]),  # +y
        (c[0], c[4], c[6], c[2]),  # -x
        (c[1], c[3], c[7], c[5]),  # +x
    ]
    if not skip_bottom:
        quads.append((c[0], c[2], c[3], c[1]))  # bottom (-z)
    for q in quads:
        mb.face(q, mat, False)


def annulus_sector(mb, r0, r1, th0, th1, z, n_seg, mat, thickness=0.0, uv_scale=None):
    """A flat (optionally thick) ring sector at height z, top face up."""
    top_in, top_out, bot_in, bot_out = [], [], [], []
    for i in range(n_seg + 1):
        th = th0 + (th1 - th0) * i / n_seg
        top_in.append(mb.vert(polar(r0, th, z)))
        top_out.append(mb.vert(polar(r1, th, z)))
        if thickness > 0.0:
            bot_in.append(mb.vert(polar(r0, th, z - thickness)))
            bot_out.append(mb.vert(polar(r1, th, z - thickness)))
    rm = 0.5 * (r0 + r1)
    for i in range(n_seg):
        uv = None
        if uv_scale:
            u0 = rm * (th0 + (th1 - th0) * i / n_seg) * uv_scale
            u1 = rm * (th0 + (th1 - th0) * (i + 1) / n_seg) * uv_scale
            uv = [(u0, 0.0), (u0, (r1 - r0) * uv_scale), (u1, (r1 - r0) * uv_scale), (u1, 0.0)]
        mb.face((top_in[i], top_out[i], top_out[i + 1], top_in[i + 1]), mat, False, uv)
        if thickness > 0.0:
            mb.face((bot_in[i], bot_in[i + 1], bot_out[i + 1], bot_out[i]), mat, False)
            mb.face((top_out[i], bot_out[i], bot_out[i + 1], top_out[i + 1]), mat, False)
            mb.face((bot_in[i], top_in[i], top_in[i + 1], bot_in[i + 1]), mat, False)
    if thickness > 0.0:
        mb.face((top_in[0], bot_in[0], bot_out[0], top_out[0]), mat, False)
        mb.face((top_out[-1], bot_out[-1], bot_in[-1], top_in[-1]), mat, False)


def surface_of_revolution(mb, profile, n_around, mat, uv_mode="arc", split_mats=None,
                          wobble=None, smooth=True, close_top=False, close_bottom=False):
    """Revolve a (r, z) profile (top to bottom) around Z.

    uv_mode "arc": u = arc length round the widest radius, v = length down
    the profile (metres) — a tiling texture keeps its scale on the wall.
    split_mats(z_mid) -> material name per profile segment.
    wobble(theta, k) -> dr for profile row k (gentle irregularity).
    """
    r_ref = max(p[0] for p in profile)
    rows = []
    for i in range(n_around):
        th = TAU * i / n_around
        row = []
        for k, (r, z) in enumerate(profile):
            dr = wobble(th, k) if wobble else 0.0
            row.append(mb.vert(polar(max(r + dr, 0.0), th, z)))
        rows.append(row)
    vlen = [0.0]
    for k in range(1, len(profile)):
        (r0, z0), (r1, z1) = profile[k - 1], profile[k]
        vlen.append(vlen[-1] + math.hypot(r1 - r0, z1 - z0))
    for i in range(n_around):
        i2 = (i + 1) % n_around
        u0 = r_ref * TAU * i / n_around
        u1 = r_ref * TAU * (i + 1) / n_around
        for k in range(len(profile) - 1):
            zmid = 0.5 * (profile[k][1] + profile[k + 1][1])
            m = split_mats(zmid) if split_mats else mat
            uv = [(u0, vlen[k]), (u0, vlen[k + 1]), (u1, vlen[k + 1]), (u1, vlen[k])]
            mb.face((rows[i][k], rows[i][k + 1], rows[i2][k + 1], rows[i2][k]), m, smooth, uv)
    if close_bottom and profile[-1][0] > 0.0:
        c = mb.vert((0.0, 0.0, profile[-1][1]))
        for i in range(n_around):
            mb.face((rows[i][-1], c, rows[(i + 1) % n_around][-1]), mat, smooth)
    if close_top and profile[0][0] > 0.0:
        c = mb.vert((0.0, 0.0, profile[0][1]))
        for i in range(n_around):
            mb.face((rows[(i + 1) % n_around][0], c, rows[i][0]), mat, smooth)


# --------------------------------------------------------------------------
# Scene + export helpers
# --------------------------------------------------------------------------

def clear_prefixed(prefix: str) -> None:
    """Remove every object/mesh/material whose name starts with prefix."""
    for obj in list(bpy.data.objects):
        if obj.name.startswith(prefix) or obj.get("lk_build") == prefix:
            bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def make_material(name: str, rgba, roughness: float, metallic: float = 0.0,
                  alpha: float = 1.0, emission=None) -> bpy.types.Material:
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Alpha"].default_value = alpha
        if emission is not None and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = emission
            bsdf.inputs["Emission Strength"].default_value = 1.0
    mat.diffuse_color = rgba
    return mat


def srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_rgba(code: str, alpha: float = 1.0):
    code = code.lstrip("#")
    srgb = [int(code[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    return (*[srgb_to_linear(c) for c in srgb], alpha)


def world_bbox(objs):
    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    for obj in objs:
        for v in obj.data.vertices:
            co = obj.matrix_world @ v.co
            for k in range(3):
                lo[k] = min(lo[k], co[k])
                hi[k] = max(hi[k], co[k])
    return lo, hi


def mesh_tris(obj) -> int:
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)
