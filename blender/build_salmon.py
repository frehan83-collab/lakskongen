"""Build the Atlantic salmon (Salmo salar) and export glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_salmon.py -- [--lod LOD0|LOD1|LOD2] [--out DIR] [--textures]

Without --lod it builds every LOD: godot/assets/models/fish/salmon_<LOD>.glb
plus a JSON sidecar (tri_count, materials, bbox, measured proportions, UV
bounds) for blender_asset_pipeline.py and tests/test_salmon_asset.py.
--textures also paints the skin atlas godot/assets/models/fish/textures/
salmon_skin.png (1024 x 512): the countershaded body in the top 74%
(u along the fish, v from the dorsal line down to the belly, both flanks
mirrored onto it), the fins in the bottom strip.

Shape (roadmap G3-01: proper silhouette, LOD0 ~1.5k triangles, LOD1 ~300):
a farmed Atlantic salmon at 0.90 m fork length, measured as fractions of
the fork length (FL) from standard descriptions of the species: body depth
~0.24 FL at ~0.42 FL (farmed fish are deep), width ~0.55 of depth, head
~0.21 FL with the eye at ~0.07 FL, terminal mouth reaching below the eye,
dorsal fin from 0.40 FL, adipose fin at ~0.72 FL, anal fin at ~0.70 FL,
pelvic fins under the dorsal's rear, pectoral fins low behind the gill
cover, a narrow caudal peduncle (~0.075 FL deep) and a shallowly forked
tail. Countershading: dark steel-blue back with black X-shaped spots above
the lateral line, bright silver flanks, white belly.

Conventions (must match the Godot side): metres; Blender X right, Y
forward (nose at +Y), Z up. glTF turns +Y into Godot -Z, so the nose
points along Godot -Z (the MultiMesh school heads the fish with
Basis.looking_at). Origin at the middle of the fork length on the
body's axis. One object "Salmon", one material "LK_SalmonSkin" (one draw
call per school); Godot maps it to salmon_skin_material.tres.

Licence: in-house geometry and generated texture, no third-party assets.
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
OUT_DIR = os.path.join(REPO, "godot", "assets", "models", "fish")
TEX_DIR = os.path.join(OUT_DIR, "textures")
PREFIX = "LK_Salmon"
MAT = "LK_SalmonSkin"

FL = 0.90            # fork length (m)
Y0 = FL / 2          # the snout sits at +Y0, the fork at -Y0
T_BODY_END = 0.935   # the body thins into a blade inside the caudal fin base

# Dorsal (top) and ventral (bottom) profile heights and the half width, in
# metres, at fractions t of the fork length from the snout.
PROFILE = [
    # t      top     bottom   half-width
    (0.000, 0.004, -0.006, 0.003),
    (0.015, 0.016, -0.017, 0.010),
    (0.040, 0.032, -0.032, 0.021),
    (0.080, 0.052, -0.050, 0.033),
    (0.140, 0.074, -0.070, 0.045),
    (0.210, 0.091, -0.087, 0.053),
    (0.300, 0.105, -0.101, 0.059),
    (0.400, 0.112, -0.107, 0.060),
    (0.480, 0.110, -0.104, 0.057),
    (0.560, 0.102, -0.096, 0.051),
    (0.640, 0.088, -0.083, 0.043),
    (0.720, 0.069, -0.064, 0.033),
    (0.790, 0.050, -0.046, 0.024),
    (0.840, 0.039, -0.036, 0.015),
    (0.880, 0.034, -0.031, 0.009),
    (0.915, 0.031, -0.029, 0.005),
    (0.935, 0.030, -0.028, 0.0025),
]

# Atlas layout (Blender UV, v up): body v in [BODY_V0, 1], fins below.
BODY_V0 = 0.26
FIN_DARK_U = (0.0, 0.48)    # dorsal, caudal, adipose
FIN_PALE_U = (0.52, 1.0)    # pectoral, pelvic, anal
FIN_V = (0.01, 0.24)

LODS = {
    "LOD0": dict(rings=35, around=20, fin_edges=True, paired=True, small_fins=True),
    "LOD1": dict(rings=13, around=10, fin_edges=False, paired=True, small_fins=False),
    # The school seen through the water surface (G3-01): thousands per pen,
    # a few pixels long each, so a four-sided body and the tail only.
    "LOD2": dict(rings=5, around=4, fin_edges=False, paired=False, small_fins=False, dorsal=False),
}
TRI_LIMITS = {"LOD0": (1200, 1800), "LOD1": (200, 400), "LOD2": (30, 64)}


def smooth_interp(t: float, k: int) -> float:
    """Profile column k at t (cosine-eased between the control points)."""
    for i in range(len(PROFILE) - 1):
        t0, t1 = PROFILE[i][0], PROFILE[i + 1][0]
        if t0 <= t <= t1:
            u = (t - t0) / (t1 - t0)
            s = 0.5 - 0.5 * math.cos(u * math.pi)
            return PROFILE[i][k] + (PROFILE[i + 1][k] - PROFILE[i][k]) * s
    return PROFILE[-1][k] if t > PROFILE[-1][0] else PROFILE[0][k]


def section(t: float):
    return smooth_interp(t, 1), smooth_interp(t, 2), smooth_interp(t, 3)


def y_of(t: float) -> float:
    return Y0 - t * FL


def ring_point(t: float, theta: float):
    """Body surface at t, angle theta (0 = dorsal line, pi = belly).
    A superellipse section, rounder on the back, flatter on the belly."""
    top, bot, w = section(t)
    zc = 0.5 * (top + bot)
    c, s = math.cos(theta), math.sin(theta)
    e_up, e_dn = 2.2, 2.6
    if c >= 0.0:
        z = zc + (top - zc) * (abs(c) ** (2.0 / e_up))
        x = w * math.copysign(abs(s) ** (2.0 / e_up), s)
    else:
        z = zc - (zc - bot) * (abs(c) ** (2.0 / e_dn))
        x = w * math.copysign(abs(s) ** (2.0 / e_dn), s)
    return (x, y_of(t), z)


def body_uv(t: float, theta: float):
    """Both flanks share one texture half: v from the dorsal line (top of
    the body region) to the belly line (its bottom)."""
    a = theta if theta <= math.pi else TAU - theta
    return (min(t / T_BODY_END, 1.0), 1.0 - (1.0 - BODY_V0) * (a / math.pi))


def ring_ts(n: int) -> list[float]:
    # Denser at the head (shape changes fast) and at the peduncle.
    out = []
    for i in range(n):
        u = i / (n - 1)
        out.append(T_BODY_END * (0.5 - 0.5 * math.cos(math.pi * u)) * 0.35 + T_BODY_END * u * 0.65)
    out[0] = 0.0
    return out


def build_body(mb, p) -> None:
    n_around = p["around"]
    ts = ring_ts(p["rings"])
    rows = []
    for t in ts[1:]:
        row = []
        for j in range(n_around + 1):     # duplicate the seam column (UV wrap)
            th = TAU * j / n_around
            row.append((mb.vert(ring_point(t, th)), body_uv(t, th)))
        rows.append(row)
    for i in range(len(rows) - 1):
        a, b = rows[i], rows[i + 1]
        for j in range(n_around):
            # Around the fish (+theta turns from the back to starboard), then
            # back along it: outward normals.
            q = (a[j], b[j], b[j + 1], a[j + 1])
            mb.face(tuple(v for v, _ in q), MAT, True, [uv for _, uv in q])
    # Snout: a fan to the tip; tail end: a fan to the axis (inside the fin).
    snout = mb.vert((0.0, Y0 + 0.002, 0.5 * (PROFILE[0][1] + PROFILE[0][2])))
    first = rows[0]
    for j in range(n_around):
        mb.face((snout, first[j][0], first[j + 1][0]), MAT, True,
                [(0.0, 1.0 - (1.0 - BODY_V0) * 0.5), first[j][1], first[j + 1][1]])
    last = rows[-1]
    top, bot, _ = section(ts[-1])
    tail = mb.vert((0.0, y_of(ts[-1]) - 0.004, 0.5 * (top + bot)))
    for j in range(n_around):
        mb.face((tail, last[j + 1][0], last[j][0]), MAT, True,
                [(1.0, 1.0 - (1.0 - BODY_V0) * 0.5), last[j + 1][1], last[j][1]])


def fin(mb, outline, normal, thickness, u_range, edges, smooth=False):
    """A thin fin: the outline (3D points in the fin's plane, star-shaped
    round outline[0]... a fan from their centroid) as a front and a back
    face `thickness` apart, optionally closed round the edge. UVs map the
    outline's bounding box into the fin strip u_range x FIN_V."""
    n = kit.v_norm(normal)
    cx = sum(p[0] for p in outline) / len(outline)
    cy = sum(p[1] for p in outline) / len(outline)
    cz = sum(p[2] for p in outline) / len(outline)
    centre = (cx, cy, cz)
    # In-plane axes: along the fish (-Y projected) and the rest.
    ax = kit.v_norm(kit.v_sub((0.0, -1.0, 0.0), kit.v_mul(n, kit.v_dot((0.0, -1.0, 0.0), n))))
    ay = kit.v_cross(n, ax)
    coords = [(kit.v_dot(kit.v_sub(p, centre), ax), kit.v_dot(kit.v_sub(p, centre), ay)) for p in outline]
    a0, a1 = min(c[0] for c in coords), max(c[0] for c in coords)
    b0, b1 = min(c[1] for c in coords), max(c[1] for c in coords)

    def uv(c):
        fu = (c[0] - a0) / max(a1 - a0, 1e-6)
        fv = (c[1] - b0) / max(b1 - b0, 1e-6)
        return (u_range[0] + (u_range[1] - u_range[0]) * fu, FIN_V[0] + (FIN_V[1] - FIN_V[0]) * fv)

    half = kit.v_mul(n, thickness / 2)
    front = [mb.vert(kit.v_add(p, half)) for p in outline]
    back = [mb.vert(kit.v_sub(p, half)) for p in outline]
    fc = mb.vert(kit.v_add(centre, half))
    bc = mb.vert(kit.v_sub(centre, half))
    cuv = uv((0.0, 0.0))
    k = len(outline)
    # Winding: CCW seen from +n for the front face (outline order is checked
    # by the caller via `normal`).
    for i in range(k):
        i2 = (i + 1) % k
        mb.face((fc, front[i], front[i2]), MAT, smooth, [cuv, uv(coords[i]), uv(coords[i2])])
        mb.face((bc, back[i2], back[i]), MAT, smooth, [cuv, uv(coords[i2]), uv(coords[i])])
        if edges:
            mb.face((front[i], back[i], back[i2], front[i2]), MAT, False,
                    [uv(coords[i]), uv(coords[i]), uv(coords[i2]), uv(coords[i2])])


def _ccw(outline, normal):
    """Order an outline counter-clockwise when seen from +normal."""
    n = kit.v_norm(normal)
    area = (0.0, 0.0, 0.0)
    for i in range(len(outline)):
        area = kit.v_add(area, kit.v_cross(outline[i], outline[(i + 1) % len(outline)]))
    return outline if kit.v_dot(area, n) > 0 else list(reversed(outline))


def build_fins(mb, p) -> None:
    edges = p["fin_edges"]
    # Median fins in the YZ plane (normal +X).
    def median(points, ref, u_range, thick):
        pts = []
        for t, dz in points:
            top, bot, _ = section(min(t, T_BODY_END))
            base = top if ref == "top" else bot if ref == "bot" else 0.5 * (top + bot)
            pts.append((0.0, y_of(t), base + dz))
        fin(mb, _ccw(pts, (1, 0, 0)), (1, 0, 0), thick, u_range, edges)

    # Dorsal: a trapezoid about as tall as its base is long, raked back,
    # with a concave trailing edge (not a shark's spike).
    if p.get("dorsal", True):
        median([(0.395, -0.012), (0.405, 0.040), (0.420, 0.064), (0.440, 0.070), (0.458, 0.066),
                (0.478, 0.044), (0.505, 0.018), (0.528, -0.001), (0.522, -0.012)] if edges else
               [(0.395, -0.012), (0.425, 0.068), (0.458, 0.064), (0.528, -0.001)], "top", FIN_DARK_U, 0.005)
    # Caudal: broad from the peduncle, a shallow fork, the fork notch at
    # exactly one fork length, lobe tips softly rounded.
    caudal = [(0.855, 0.030), (0.905, 0.050), (0.960, 0.082), (1.012, 0.110), (1.034, 0.118), (1.044, 0.108),
              (1.024, 0.055), (1.000, 0.002), (1.024, -0.052), (1.044, -0.104), (1.034, -0.114),
              (1.012, -0.106), (0.960, -0.078), (0.905, -0.047), (0.855, -0.028)]
    if not edges:
        caudal = [(0.86, 0.030), (1.036, 0.116), (1.000, 0.002), (1.036, -0.112), (0.86, -0.028)]
    median(caudal, "mid", FIN_DARK_U, 0.005)
    if p["small_fins"]:
        median([(0.700, -0.004), (0.712, 0.018), (0.732, 0.024), (0.748, 0.006), (0.744, -0.004)],
               "top", FIN_DARK_U, 0.006)
        median([(0.672, 0.006), (0.690, -0.048), (0.704, -0.062), (0.726, -0.056), (0.758, -0.016),
                (0.766, 0.004)], "bot", FIN_PALE_U, 0.004)
    if not p["paired"]:
        return
    # Paired fins: pectorals low behind the gill cover, pelvics under the
    # dorsal's rear; both sweep back and out, tilted down.
    for side in (1.0, -1.0):
        for t0, t1, th, span, drop, out in ((0.205, 0.235, 2.20, 0.10, 0.045, 0.030),
                                            (0.480, 0.505, 2.60, 0.07, 0.030, 0.018)):
            if not p["small_fins"] and t0 > 0.3:
                continue
            a = ring_point(t0, th if side > 0 else TAU - th)
            b = ring_point(t1, th if side > 0 else TAU - th)
            tip_t = t1 + span / FL
            mid = ring_point(0.5 * (t0 + t1), th if side > 0 else TAU - th)
            tip = (mid[0] + side * out, y_of(tip_t), mid[2] - drop)
            tip2 = (mid[0] + side * out * 0.75, y_of(tip_t - 0.025), mid[2] - drop * 1.6)
            pts = [a, b, tip2, tip] if edges else [a, b, tip]
            # The fin plane's normal points up and out.
            normal = kit.v_norm(kit.v_cross(kit.v_sub(b, a), kit.v_sub(tip, a)))
            if normal[2] < 0:
                normal = kit.v_mul(normal, -1.0)
            fin(mb, _ccw(pts, normal), normal, 0.003, FIN_PALE_U, edges)


def build_lod(lod: str, mats: dict):
    p = LODS[lod]
    mb = kit.MeshBuilder()
    build_body(mb, p)
    build_fins(mb, p)
    obj = mb.build("Salmon", mats)
    obj["lk_build"] = PREFIX
    return obj


# --------------------------------------------------------------------------
# Skin atlas
# --------------------------------------------------------------------------

def _srgb(code):
    code = code.lstrip("#")
    return [int(code[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]


def build_texture() -> str:
    """Countershaded skin: RGB albedo (sRGB), A = silver (metallic) mask."""
    import numpy as np
    W, H = 1024, 512
    u = (np.arange(W) + 0.5) / W
    v = (np.arange(H) + 0.5) / H
    uu, vv = np.meshgrid(u, v)                 # vv up (row 0 = bottom)
    rng = np.random.default_rng(1758)          # Salmo salar, Linnaeus 1758
    img = np.zeros((H, W, 4), dtype=np.float64)

    # Body region: t along the fish, a = 0 dorsal line .. 1 belly line.
    body = vv >= BODY_V0
    t = uu * T_BODY_END
    a = np.clip((1.0 - vv) / (1.0 - BODY_V0), 0.0, 1.0)
    back = np.array(_srgb("#2E4250"))
    back_hi = np.array(_srgb("#3F5868"))
    flank = np.array(_srgb("#C3CCD3"))
    belly = np.array(_srgb("#EDF0F1"))

    def smooth(e0, e1, x):
        s = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
        return s * s * (3 - 2 * s)

    # The silver line wanders a little along the body.
    wave = 0.018 * np.sin(t * 23.0) + 0.012 * np.sin(t * 61.0 + 1.3)
    k_flank = smooth(0.24 + wave, 0.40 + wave, a)
    k_belly = smooth(0.70, 0.86, a)
    col = back[None, None, :] * (1 - smooth(0.0, 0.2, a))[..., None] + back_hi[None, None, :] * smooth(0.0, 0.2, a)[..., None]
    col = col * (1 - k_flank)[..., None] + flank[None, None, :] * k_flank[..., None]
    col = col * (1 - k_belly)[..., None] + belly[None, None, :] * k_belly[..., None]
    # Head: darker crown, slightly bronze gill cover.
    head = smooth(0.24, 0.16, t)
    col = col * (1 - 0.08 * head * (1 - k_belly))[..., None]
    silver = 0.35 + 0.6 * k_flank * (1 - 0.45 * k_belly)

    # Metric coordinates for round marks: metres along and round the body.
    def half_circ(tt):
        top = np.interp(tt, [r[0] for r in PROFILE], [r[1] for r in PROFILE])
        bot = np.interp(tt, [r[0] for r in PROFILE], [r[2] for r in PROFILE])
        w = np.interp(tt, [r[0] for r in PROFILE], [r[3] for r in PROFILE])
        return math.pi * 0.5 * (0.5 * (top - bot) + w) * 1.05

    hc = half_circ(t)
    mx = t * FL
    my = a * hc

    # Lateral line: a faint dark thread just under the silver edge.
    lat = np.exp(-((a - (0.46 + 0.02 * np.sin(t * 9.0))) / 0.006) ** 2) * smooth(0.18, 0.26, t) * smooth(0.9, 0.8, t)
    col = col * (1 - 0.22 * lat)[..., None]
    # Gill cover edge: an arc behind the eye.
    gill = np.exp(-(((t - (0.185 + 0.05 * (a - 0.5) ** 2)) * FL) / 0.0025) ** 2) * smooth(0.95, 0.75, a)
    col = col * (1 - 0.10 * gill)[..., None]
    # Mouth: from the snout tip back to below the eye.
    mouth_line = np.exp(-(((a - (0.52 + 0.25 * t)) * hc) / 0.0018) ** 2) * smooth(0.10, 0.07, t)
    col = col * (1 - 0.6 * mouth_line)[..., None]
    # Eye: silver iris, black pupil, a dark rim.
    ex, ey = 0.068 * FL, 0.33
    eh = float(half_circ(np.array(0.068)))
    d_eye = np.sqrt((mx - ex) ** 2 + ((a - ey) * eh) ** 2)
    iris = smooth(0.0110, 0.0092, d_eye)
    pupil = smooth(0.0068, 0.0055, d_eye)
    rim = np.exp(-((d_eye - 0.0128) / 0.0012) ** 2)
    col = col * (1 - 0.6 * rim)[..., None]
    col = col * (1 - iris)[..., None] + np.array(_srgb("#7F8C8A"))[None, None, :] * iris[..., None]
    col = col * (1 - pupil)[..., None]
    silver = silver * (1 - pupil) + 0.9 * (iris - pupil)
    # Black spots: X-shaped and round, mostly above the lateral line and on
    # the head, sparser on the flank, none on the belly.
    n_spots = 95
    sx = rng.uniform(0.03, 0.80, n_spots)
    sa = np.abs(rng.normal(0.0, 0.22, n_spots)) + 0.06
    keep = (sa < 0.56) & ~((sx < 0.10) & (np.abs(sa - ey) < 0.12))
    spots = np.zeros_like(t)
    for x0, a0, r, rot in zip(sx[keep], sa[keep], rng.uniform(0.0022, 0.0042, n_spots)[keep],
                              rng.uniform(0, math.pi, n_spots)[keep]):
        dx = mx - x0 * FL
        dy = (a - a0) * hc
        near = (np.abs(dx) < 0.02) & (np.abs(dy) < 0.02)
        if not near.any():
            continue
        cr, sr = math.cos(rot), math.sin(rot)
        px, py = dx * cr + dy * sr, -dx * sr + dy * cr
        cross = np.minimum(np.abs(px + py), np.abs(px - py)) / math.sqrt(2)
        radial = np.sqrt(px * px + py * py)
        shape = np.where(rng.random() < 0.6, (cross < r * 0.32) & (radial < r * 1.45), radial < r)
        spots = np.maximum(spots, shape.astype(np.float64) * smooth(0.6, 0.45, a))
    col = col * (1 - 0.85 * spots)[..., None]
    silver = silver * (1 - 0.8 * spots)
    # Fine scale glint: tiny value noise in the silver.
    glint = rng.random((H, W)) * 0.06
    col = np.clip(col * (0.97 + glint[..., None] * (0.4 + k_flank[..., None])), 0, 1)

    img[..., :3] = np.where(body[..., None], col, 0.0)
    img[..., 3] = np.where(body, np.clip(silver, 0, 1), 0.0)

    # Fins: dark grey rays (dorsal, caudal, adipose) and pale ones (paired, anal).
    fins = vv < BODY_V0
    fv = np.clip((vv - FIN_V[0]) / (FIN_V[1] - FIN_V[0]), 0, 1)
    rays = 0.5 + 0.5 * np.cos(uu * W * 0.5)
    dark = np.array(_srgb("#3A4752")) * (0.85 + 0.15 * rays[..., None]) * (0.8 + 0.2 * fv[..., None])
    pale = np.array(_srgb("#8E9AA3")) * (0.88 + 0.12 * rays[..., None]) * (1.0 - 0.25 * fv[..., None])
    fin_col = np.where((uu < 0.5)[..., None], dark, pale)
    img[..., :3] = np.where(fins[..., None], fin_col, img[..., :3])
    img[..., 3] = np.where(fins, np.where(uu < 0.5, 0.15, 0.3), img[..., 3])

    os.makedirs(TEX_DIR, exist_ok=True)
    path = os.path.join(TEX_DIR, "salmon_skin.png")
    im = bpy.data.images.new("LK_salmon_skin", W, H, alpha=True, float_buffer=False)
    im.colorspace_settings.name = "sRGB"
    im.alpha_mode = "STRAIGHT"
    im.pixels.foreach_set(np.asarray(img, dtype=np.float32).ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)
    return path


# --------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------

def export_lod(lod: str, obj, out_dir: str) -> dict:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    path = os.path.join(out_dir, f"salmon_{lod}.glb")
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=False,
        export_yup=True,
        export_texcoords=True,
        export_normals=True,
        export_materials="EXPORT",
        export_vertex_color="NONE",
        export_cameras=False,
        export_lights=False,
        export_extras=False,
    )
    tris = kit.mesh_tris(obj)
    lo, hi = kit.world_bbox([obj])
    uvs = obj.data.uv_layers[0].data
    us = [d.uv[0] for d in uvs]
    vs = [d.uv[1] for d in uvs]
    depth = max(section(t / 100.0)[0] - section(t / 100.0)[1] for t in range(0, 92))
    width = 2 * max(section(t / 100.0)[2] for t in range(0, 92))
    data = {
        "asset": "salmon",
        "lod": lod,
        "tri_count": tris,
        "material_count": 1,
        "materials": [MAT],
        # Blender Z-up -> glTF/Godot Y-up: size (x, z, y).
        "bbox_size": [round(hi[0] - lo[0], 4), round(hi[2] - lo[2], 4), round(hi[1] - lo[1], 4)],
        "dimensions": {
            "fork_length_m": FL,
            "total_length_m": round(hi[1] - lo[1], 4),
            "nose_y_m": round(hi[1], 4),
            "max_depth_m": round(depth, 4),
            "max_width_m": round(width, 4),
            "depth_over_fork_length": round(depth / FL, 4),
            "width_over_depth": round(width / depth, 4),
        },
        "uv_bounds": [round(min(us), 4), round(min(vs), 4), round(max(us), 4), round(max(vs), 4)],
        "file": os.path.basename(path),
        "blender_version": bpy.app.version_string,
    }
    with open(os.path.join(out_dir, f"salmon_{lod}.json"), "w") as f:
        json.dump(data, f, indent=2, sort_keys=True)
        f.write("\n")
    print(f"TRIS: {tris}")
    print(f"BBOX: {' '.join(str(v) for v in data['bbox_size'])}")
    print(f"MATERIALS: 1")
    print(f"SALMON_EXPORT {lod} {path} tris={tris} dims={data['dimensions']}")
    lo_t, hi_t = TRI_LIMITS[lod]
    if not lo_t <= tris <= hi_t:
        raise SystemExit(f"salmon {lod} has {tris} triangles, outside {lo_t}-{hi_t}")
    return data


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--lod", choices=list(LODS), default=None)
    ap.add_argument("--textures", action="store_true")
    ap.add_argument("--out", default=OUT_DIR)
    ap.add_argument("--variant", default=None)  # accepted for the pipeline CLI
    return ap.parse_args(argv)


def main():
    args = parse_args()
    os.makedirs(args.out, exist_ok=True)
    if args.textures:
        print(f"TEXTURE {build_texture()}")
    mats = {MAT: kit.make_material(MAT, kit.hex_rgba("#C3CCD3"), 0.32, 0.7)}
    for lod in [args.lod] if args.lod else list(LODS):
        kit.clear_prefixed(PREFIX)
        obj = build_lod(lod, mats)
        export_lod(lod, obj, args.out)
    kit.clear_prefixed(PREFIX)


if __name__ == "__main__":
    main()
