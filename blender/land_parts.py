"""Parts and building blocks of the land-based farm kit (farm-types F2-25).

A Build collects one asset LOD as named parts; each part is a MeshBuilder
pointed at one trim-atlas strip (build_land_base.STRIPS). The helpers add
boxes, members, tubes, revolved shells, ring decks, planar wall grids and
whole buildings (duo-pitch sheds, flat-roofed blocks with parapets) into
parts. Blender axes: X right, Y forward (glTF/Godot -Z), Z up.

Licence: in-house code (proprietary, part of the Lakskongen repository).
"""

from __future__ import annotations

import math

import lk_meshkit as kit
from lk_meshkit import TAU

M = "LK_FacilityKit"


class Build:
    """Parts of one asset LOD: name -> (MeshBuilder, strip)."""

    def __init__(self):
        self.parts: dict[str, tuple[kit.MeshBuilder, str]] = {}
        self.dims: dict[str, float] = {}

    def part(self, name: str, strip: str) -> kit.MeshBuilder:
        if name not in self.parts:
            self.parts[name] = (kit.MeshBuilder(drop_degenerate=True), strip)
        mb, s = self.parts[name]
        if s != strip:
            raise ValueError(f"part {name} is on strip {s}, not {strip}")
        return mb


# --------------------------------------------------------------------------
# Primitive helpers (all append into parts)
# --------------------------------------------------------------------------

def box(b, part, strip, lo, hi, bottom=False):
    """Axis-aligned box between corners lo and hi."""
    c = tuple((lo[k] + hi[k]) * 0.5 for k in range(3))
    h = tuple((hi[k] - lo[k]) * 0.5 for k in range(3))
    kit.box(b.part(part, strip), c, (1, 0, 0), (0, 1, 0), (0, 0, 1), h[0], h[1], h[2], M, skip_bottom=not bottom)


def beam(b, part, strip, p0, p1, w, h=None, sides=4):
    """A square (or flat w x h) member between two points, no end caps."""
    h = w if h is None else h
    d = kit.v_sub(p1, p0)
    length = kit.v_len(d)
    if length < 1e-6:
        return
    az = kit.v_norm(d)
    ref = (0.0, 0.0, 1.0) if abs(az[2]) < 0.95 else (1.0, 0.0, 0.0)
    ax = kit.v_norm(kit.v_cross(ref, az))
    ay = kit.v_cross(az, ax)
    mb = b.part(part, strip)
    c = kit.v_mul(kit.v_add(p0, p1), 0.5)
    hx, hy, hz = w * 0.5, h * 0.5, length * 0.5
    pts = []
    for sz in (-1, 1):
        for sy in (-1, 1):
            for sx in (-1, 1):
                pts.append(mb.vert(kit.v_add(c, kit.v_add(kit.v_mul(ax, sx * hx), kit.v_add(kit.v_mul(ay, sy * hy), kit.v_mul(az, sz * hz))))))
    p = pts
    quads = [(p[0], p[4], p[6], p[2]), (p[1], p[3], p[7], p[5]), (p[0], p[1], p[5], p[4]), (p[2], p[6], p[7], p[3])]
    for q in quads[:sides]:
        mb.face(q, M, False)


def tube(b, part, strip, pts, r, n, caps=False, smooth=True):
    kit.path_tube(b.part(part, strip), pts, r, n, M, closed=False, caps=caps, smooth=smooth)


def revolve(b, part, strip, profile, n, close_top=False, close_bottom=False, smooth=True, centre=(0.0, 0.0)):
    """kit.surface_of_revolution around a vertical axis through centre."""
    mb = b.part(part, strip)
    start = len(mb.verts)
    kit.surface_of_revolution(mb, profile, n, M, close_top=close_top, close_bottom=close_bottom, smooth=smooth)
    if centre != (0.0, 0.0):
        for i in range(start, len(mb.verts)):
            x, y, z = mb.verts[i]
            mb.verts[i] = (x + centre[0], y + centre[1], z)


def ring_deck(b, part, strip, r0, r1, z, thick, n, outer=True, under=False):
    """A full flat ring (walkway deck): top, outer edge, optional underside."""
    mb = b.part(part, strip)
    ti, to, bi, bo = [], [], [], []
    for i in range(n):
        th = TAU * i / n
        ti.append(mb.vert(kit.polar(r0, th, z)))
        to.append(mb.vert(kit.polar(r1, th, z)))
        bo.append(mb.vert(kit.polar(r1, th, z - thick)))
        bi.append(mb.vert(kit.polar(r0, th, z - thick)))
    for i in range(n):
        j = (i + 1) % n
        u0, u1 = r1 * TAU * i / n, r1 * TAU * (i + 1) / n
        mb.face((ti[i], to[i], to[j], ti[j]), M, False, [(u0, 0), (u0, 1), (u1, 1), (u1, 0)])
        if outer:
            mb.face((to[i], bo[i], bo[j], to[j]), M, False, [(u0, 0), (u0, 1), (u1, 1), (u1, 0)])
        if under:
            mb.face((bi[i], bi[j], bo[j], bo[i]), M, False, [(u0, 0), (u1, 0), (u1, 1), (u0, 1)])


def ring_band(b, part, strip, r, z0, z1, n, both=True):
    """A thin vertical ribbon ring (handrail at a distance): outer and
    inner faces between z0 and z1."""
    mb = b.part(part, strip)
    lo, hi = [], []
    for i in range(n):
        th = TAU * i / n
        lo.append(mb.vert(kit.polar(r, th, z0)))
        hi.append(mb.vert(kit.polar(r, th, z1)))
    for i in range(n):
        j = (i + 1) % n
        u0, u1 = r * TAU * i / n, r * TAU * (i + 1) / n
        mb.face((hi[i], lo[i], lo[j], hi[j]), M, False, [(u0, 1), (u0, 0), (u1, 0), (u1, 1)])
        if both:
            mb.face((hi[j], lo[j], lo[i], hi[i]), M, False, [(u1, 1), (u1, 0), (u0, 0), (u0, 1)])


def grid(b, cell, origin, u_dir, v_dir, ub, vb):
    """A planar wall split at ub x vb (metres along u_dir, v_dir); normal is
    u_dir x v_dir. cell(i, j, u0, u1, v0, v1) -> (part, strip) per cell, or
    None to leave the cell open."""
    pts = kit.grid_rect(origin, u_dir, v_dir, ub, vb)
    for j in range(len(vb) - 1):
        for i in range(len(ub) - 1):
            ps = cell(i, j, ub[i], ub[i + 1], vb[j], vb[j + 1])
            if ps is None:
                continue
            mb = b.part(*ps)
            q = [pts[j][i], pts[j][i + 1], pts[j + 1][i + 1], pts[j + 1][i]]
            idx = tuple(mb.vert(p) for p in q)
            mb.face(idx, M, False, [(ub[i], vb[j]), (ub[i + 1], vb[j]), (ub[i + 1], vb[j + 1]), (ub[i], vb[j + 1])])


def gable_tri(b, part, strip, p0, p1, p2):
    mb = b.part(part, strip)
    mb.face((mb.vert(p0), mb.vert(p1), mb.vert(p2)), M, False)


def uniform(part, strip):
    return lambda i, j, u0, u1, v0, v1: (part, strip)


# --------------------------------------------------------------------------
# Buildings
# --------------------------------------------------------------------------

def facade_cells(wall, plinth_h, window=None, doors=()):
    """Cell rule for a clad wall: concrete plinth row, window ribbon cells
    (window=(v0, v1, pitch, width)), door rectangles (u0, u1, v1)."""
    wall_part, wall_strip = wall

    def cell(i, j, u0, u1, v0, v1):
        um = 0.5 * (u0 + u1)
        vm = 0.5 * (v0 + v1)
        for du0, du1, dv1 in doors:
            if du0 <= um <= du1 and vm <= dv1:
                return ("Doors", "door")
        if vm < plinth_h:
            return ("Plinth", "concrete")
        if window and window[0] <= vm <= window[1]:
            pitch, width = window[2], window[3]
            k = (um % pitch)
            if k < width:
                return ("Windows", "window")
        return (wall_part, wall_strip)
    return cell


def wall_breaks(length, step, window=None, doors=()):
    """Cut a facade at the column step, the window edges and door edges."""
    extra = []
    if window:
        pitch, width = window[2], window[3]
        u = 0.0
        while u < length:
            extra += [u, u + width]
            u += pitch
    for d0, d1, _ in doors:
        extra += [d0, d1]
    return kit.breaks(length, step, extra)


def shed(b, x0, x1, y0, y1, eave, ridge, lod, wall=("Walls", "hall_clad"), roof=("Roof", "hall_roof"),
         step=3.0, rows=(0.6, 2.2), window=None, doors_front=(), doors_back=(), doors_ends=(), ends=True,
         plinth=0.6, gutter=True):
    """A duo-pitch shed along X: long walls at y0 (front) and y1 (back),
    gable ends at x0, x1, ridge along X. LOD 0 cuts walls on a `step` grid
    with AO rows near the ground; LOD 1 a coarse grid; LOD 2 bare planes."""
    L, W = x1 - x0, y1 - y0
    if lod == 2:
        step, rows, window, doors_front, doors_back, doors_ends = max(L, W), (), None, (), (), ()
    elif lod == 1:
        # One continuous window ribbon, no plinth row, a coarse grid.
        step, rows = max(step * 3.0, 9.0), ()
        window = (window[0], window[1], 2.0 * (L + W), 2.0 * (L + W)) if window else None
    if lod == 1:  # personnel doors vanish at LOD 1; roller doors stay
        doors_front, doors_back = (), ()
    win = window
    pl = plinth if lod == 0 else -1.0
    base_rows = list(rows) + ([plinth] if lod == 0 else [])

    def vrows(doors, w):
        return kit.breaks(eave, eave, base_rows + ([w[0], w[1]] if w else []) + [d[2] for d in doors])
    # Front (-Y) wall: u along +X, normal -Y.
    grid(b, facade_cells(wall, pl, win, doors_front), (x0, y0, 0.0), (1, 0, 0), (0, 0, 1),
         wall_breaks(L, step, win, doors_front), vrows(doors_front, win))
    # Back (+Y) wall: u along -X.
    grid(b, facade_cells(wall, pl, win, doors_back), (x1, y1, 0.0), (-1, 0, 0), (0, 0, 1),
         wall_breaks(L, step, win, doors_back), vrows(doors_back, win))
    if ends:
        cell = facade_cells(wall, pl, None, doors_ends)
        eb = kit.breaks(W, step if lod == 0 else W, [d for dd in doors_ends for d in dd[:2]])
        vb = vrows(doors_ends, None)
        grid(b, cell, (x0, y1, 0.0), (0, -1, 0), (0, 0, 1), eb, vb)
        grid(b, cell, (x1, y0, 0.0), (0, 1, 0), (0, 0, 1), eb, vb)
        ym = 0.5 * (y0 + y1)
        if lod < 2:  # LOD 2 (12 triangles) leaves the 0.8 m gables open
            gable_tri(b, wall[0], wall[1], (x0, y1, eave), (x0, y0, eave), (x0, ym, ridge))
            gable_tri(b, wall[0], wall[1], (x1, y0, eave), (x1, y1, eave), (x1, ym, ridge))
    # Roof slopes (with 0.3 m overhang).
    ym = 0.5 * (y0 + y1)
    o = 0.3 if lod < 2 else 0.0
    rb = kit.breaks(L + 2 * o, step if lod == 0 else (L + 2 * o))
    slope = math.hypot(ym - y0 + o, ridge - eave)
    vb_roof = [0.0, slope] if lod > 0 else [0.0, slope * 0.5, slope]
    d_front = kit.v_norm((0.0, ym - (y0 - o), ridge - eave))
    d_back = kit.v_norm((0.0, ym - (y1 + o), ridge - eave))
    grid(b, uniform(*roof), (x0 - o, y0 - o, eave - 0.02), (1, 0, 0), d_front, rb, vb_roof)
    grid(b, uniform(*roof), (x1 + o, y1 + o, eave - 0.02), (-1, 0, 0), d_back, rb, vb_roof)
    if gutter and lod == 0:
        box(b, "Trim", "steel", (x0 - o, y0 - o - 0.18, eave - 0.25), (x1 + o, y0 - o, eave))
        box(b, "Trim", "steel", (x0 - o, y1 + o, eave - 0.25), (x1 + o, y1 + o + 0.18, eave))


def block(b, x0, x1, y0, y1, h, lod, wall=("Walls", "harvest_clad"), roof=("Roof", "hall_roof"),
          step=6.0, rows=(0.6, 2.4), window=None, doors_front=(), doors_back=(), doors_left=(), doors_right=(),
          parapet=1.0, fascia=None, plinth=0.6, window_sides=("front", "back", "left", "right")):
    """A flat-roofed box with a parapet: walls to h, the roof deck `parapet`
    below the top, parapet inner faces and cap. fascia=(v0, part, strip)
    re-strips the wall above v0 (a pale band at the top)."""
    L, W = x1 - x0, y1 - y0
    if lod == 2:
        step, rows, window, parapet = max(L, W), (), None, 0.0
        doors_front = doors_back = doors_left = doors_right = ()
    elif lod == 1:
        # No window cells, no plinth row: doors and the fascia carry it.
        step, rows, window = max(step * 2.5, 12.0), (), None
    extra = list(rows) + ([plinth] if lod == 0 else [])
    if window:
        extra += [window[0], window[1]]
    if fascia and lod < 2:
        extra.append(fascia[0])
    alldoors = tuple(doors_front) + tuple(doors_back) + tuple(doors_left) + tuple(doors_right)
    vb = kit.breaks(h, h, extra + [d[2] for d in alldoors])

    def cells(doors, win):
        base = facade_cells(wall, plinth if lod == 0 else -1, win, doors)
        if not fascia or lod == 2:
            return base

        def cell(i, j, u0, u1, v0, v1):
            if 0.5 * (v0 + v1) > fascia[0]:
                return (fascia[1], fascia[2])
            return base(i, j, u0, u1, v0, v1)
        return cell
    def win(side):
        return window if lod < 2 and side in window_sides else None
    grid(b, cells(doors_front, win("front")), (x0, y0, 0.0), (1, 0, 0), (0, 0, 1),
         wall_breaks(L, step, win("front"), doors_front), vb)
    grid(b, cells(doors_back, win("back")), (x1, y1, 0.0), (-1, 0, 0), (0, 0, 1),
         wall_breaks(L, step, win("back"), doors_back), vb)
    grid(b, cells(doors_left, win("left")), (x0, y1, 0.0), (0, -1, 0), (0, 0, 1),
         wall_breaks(W, step, win("left"), doors_left), vb)
    grid(b, cells(doors_right, win("right")), (x1, y0, 0.0), (0, 1, 0), (0, 0, 1),
         wall_breaks(W, step, win("right"), doors_right), vb)
    zr = h - parapet
    rs = step * 2.0 if lod == 0 else max(L, W)
    grid(b, uniform(*roof), (x0, y0, zr), (1, 0, 0), (0, 1, 0), kit.breaks(L, rs), kit.breaks(W, rs))
    if parapet > 0.0:
        t = 0.3
        # Inner faces of the parapet (facing the roof) and its cap.
        pb = [0.0, parapet]
        grid(b, uniform(*wall), (x1 - t, y0 + t, zr), (-1, 0, 0), (0, 0, 1), kit.breaks(L - 2 * t, L), pb)
        grid(b, uniform(*wall), (x0 + t, y1 - t, zr), (1, 0, 0), (0, 0, 1), kit.breaks(L - 2 * t, L), pb)
        grid(b, uniform(*wall), (x0 + t, y0 + t, zr), (0, 1, 0), (0, 0, 1), kit.breaks(W - 2 * t, W), pb)
        grid(b, uniform(*wall), (x1 - t, y1 - t, zr), (0, -1, 0), (0, 0, 1), kit.breaks(W - 2 * t, W), pb)
        cap = ("Trim", "steel")
        box(b, *cap, (x0, y0, h - 0.01), (x1, y0 + t, h + 0.08))
        box(b, *cap, (x0, y1 - t, h - 0.01), (x1, y1, h + 0.08))
        box(b, *cap, (x0, y0 + t, h - 0.01), (x0 + t, y1 - t, h + 0.08))
        box(b, *cap, (x1 - t, y0 + t, h - 0.01), (x1, y1 - t, h + 0.08))


def roof_unit(b, x, y, z, sx, sy, sz, lod, part="Roofgear", strip="steel"):
    """A box on a roof (vent, HVAC unit) with a cap plate at LOD 0."""
    box(b, part, strip, (x - sx / 2, y - sy / 2, z), (x + sx / 2, y + sy / 2, z + sz))
    if lod == 0:
        box(b, part, strip, (x - sx / 2 - 0.15, y - sy / 2 - 0.15, z + sz), (x + sx / 2 + 0.15, y + sy / 2 + 0.15, z + sz + 0.12))


def railing(b, pts, h, lod, posts_every=2, part="Rail", strip="steel", closed=False):
    """A handrail along a polyline: posts and a top rail (LOD 0 adds a knee
    rail); LOD 1 is a single top rail ribbon."""
    n = len(pts)
    segs = n if closed else n - 1
    for i in range(segs):
        a = pts[i]
        c = pts[(i + 1) % n]
        top_a, top_c = (a[0], a[1], a[2] + h), (c[0], c[1], c[2] + h)
        beam(b, part, strip, top_a, top_c, 0.06 if lod == 0 else 0.05, sides=4 if lod == 0 else 2)
        if lod == 0:
            beam(b, part, strip, (a[0], a[1], a[2] + h * 0.5), (c[0], c[1], c[2] + h * 0.5), 0.05, sides=2)
            if i % posts_every == 0:
                beam(b, part, strip, a, top_a, 0.06, sides=4)
