"""Geometry of the offshore kit (farm-types spec §5.2): the OF1-class
semi-submersible unit (item F2-26), the Havfarm-class ship-shaped unit and
the mooring-line kit (item F2-29).

The owner's polygon ruling (RUN_LOG §7, roadmap §9a): a regular 12-gon of
circumradius 55 m (Ø110, 28.5 m sides) as built [O:R6] and as photographed
in ref O3; the 8-sided concept render O2 is a simplification. `of1_class(u,
lod, sides=12)` also builds the 8-gon for the evidence side-by-side only.

Vertical layout (Blender Z up, z = 0 the design waterline, origin on the
unit's axis), overall height 68 m at the 43 m operating draft [O:R6]:

  -43.0  bottom of the six cone-ended pontoon feet and the centre pontoon
  -35.0  bottom ring (Ø2.05), bottom radial beams (Ø1.75) to the centre
  -10.5  mid ring (Ø1.0); one Ø1.0 diagonal per bay zig-zags through it
  +14.0  top ring (Ø2.29), the cross gantry (Ø2.05) and the top spoke
  +15.4  walkway decks, the centre platform; +25.2 the control block roof,
         +25.4 the silo tops (structural height 68.4 m), +28.2 the mast top

Above the water stand 14 m of frame with the net (EcoNet, pale PET) inside
it, the zig-zag's upper thirds, the walkway ring with its handrails, and the
topsides: the white control/accommodation block (14 x 14 x 9.8) on the
centre platform with its stair tower, mast, radar and the Beacon; four
silos (Ø4 x 10) on a cantilevered deck with the feed blower house; a grey
power house; two knuckle cranes reaching 18 m; the harvest chute (the white
tube of ref O3); a boat landing and a walk-to-work landing; lifebuoys, life
rafts, deck lights, feed pipes and the six nav lanterns.

A Unit collects geometry per node (the Godot API: Hull, Columns, Bracing,
Topsides, Net, Bulkhead, Cranes/CraneA, Cranes/CraneB, NavLights and the
Beacon marker). Every face sits on one trim-atlas strip (build_offshore.
STRIPS) and every vertex carries its build part group (Pontoons -> Columns
-> Rings -> Bracing -> Topsides -> Net) for the yard picture-in-picture and
the First Sight timelapse. Sizes in metres from [O:R6] and the research
dossier §6.1; "artist est." where the sources give none.

Licence: in-house geometry (proprietary, part of the Lakskongen repository).
"""

from __future__ import annotations

import math

import lk_hull as hull
import lk_meshkit as kit
from lk_meshkit import TAU, v_add, v_cross, v_len, v_mul, v_norm, v_sub

M = "LK_OffshoreKit"
N = "LK_OffshoreNet"

# Build part groups, in yard order (vertex colour G = group / 8).
PONTOONS, COLUMNS, RINGS, BRACING, TOPSIDES, NET = range(6)
GROUP_NAMES = ["Pontoons", "Columns", "Rings", "Bracing", "Topsides", "Net"]

# --------------------------------------------------------------------------
# Dimensions
# --------------------------------------------------------------------------

R = 55.0                     # circumradius of the column centres [O:R6]
SIDES = 12                   # owner ruling: 12-gon [O:R6]
D_LARGE, D_SLIM, D_CENTRE = 3.56, 2.80, 3.56   # columns [O:R6]
D_TOP_RING, D_BOT_RING, D_MID_RING = 2.29, 2.05, 1.0
D_DIAG, D_RADIAL, D_GANTRY = 1.0, 1.75, 2.05
D_SPOKE = 1.6                # top spoke over the fixed bulkhead (artist est.)
Z_BOTTOM = -43.0             # operating draft [O:R6]
Z_FOOT_CYL = -40.5           # pontoon foot: Ø10 drum, then a cone (artist est., ref O3)
Z_FOOT_CONE = -35.6
FOOT_R = 5.0
Z_BR, Z_MR, Z_TR = -35.0, -10.5, 14.0
DECK_Z = 15.4                # walkway and platform deck top
DECK_T = 0.22
WALK_W = 2.6                 # ring walkway width (artist est.)
GANTRY_WALK_W = 2.0          # spec: 2 m walkway on the cross gantry
SPOKE_WALK_W = 1.4
RAIL_H = 1.1                 # handrails 1.1 m (spec, artist est.)
SPLASH = (-2.5, 4.0)         # splash-zone paint band (spec 0-4 m, plus 2.5 m under for heave)
NET_INSET = 2.2              # net plane inside the column centres
NET_TOP, NET_BOTTOM = 14.6, -34.0
PLATFORM_HALF = 11.0         # centre platform 22 x 22
HOUSE_HALF = 7.0             # control/accommodation block 14 x 14 x 9.8 [O:R6, artist est.]
HOUSE_TOP = DECK_Z + 9.8
MAST_TOP = HOUSE_TOP + 3.0
SILO_R, SILO_H = 2.0, 10.0   # 4 silos Ø4 x 10 (spec, artist est.)
CRANE_REACH = 18.0           # 2 knuckle cranes reaching 18 m (spec)


def apothem(sides: int = SIDES) -> float:
    return R * math.cos(math.pi / sides)


def vertex_angle(k: int, sides: int = SIDES) -> float:
    return TAU * k / sides


def vertex(k: int, z: float, r: float = R, sides: int = SIDES):
    th = vertex_angle(k % sides, sides)
    return (r * math.cos(th), r * math.sin(th), z)


def poly_r(apo: float, sides: int = SIDES) -> float:
    """Circumradius of the polygon whose sides sit at apothem `apo`."""
    return apo / math.cos(math.pi / sides)


def is_large(k: int) -> bool:
    return k % 2 == 0


# --------------------------------------------------------------------------
# Unit: per-node mesh builders keyed by strip, group and part
# --------------------------------------------------------------------------

class Unit:
    """Geometry of one asset LOD.

    mb(node, strip, group, part) returns the MeshBuilder for that key (one
    per key, in first-use order). build_offshore.py points each builder's
    faces at its strip (net builders keep metre UVs for pen_net_shader),
    merges a node's builders into one mesh, and writes each vertex's group
    into the vertex colour. `part` names what the sidecar measures and
    counts (Feet, ColumnLarge, TopRing, Deckhouse...)."""

    def __init__(self, sides: int = SIDES):
        self.sides = sides
        self.builders: dict[tuple, kit.MeshBuilder] = {}
        self.markers: dict[str, tuple] = {}
        self.pivots: dict[str, tuple] = {}
        self.dims: dict[str, float] = {}

    def mb(self, node: str, strip: str | None, group: int, part: str, mat: str = M) -> kit.MeshBuilder:
        key = (node, mat, strip, group, part)
        if key not in self.builders:
            self.builders[key] = kit.MeshBuilder(drop_degenerate=True)
        return self.builders[key]

    def nodes(self) -> list[str]:
        out = []
        for key, b in self.builders.items():
            if b.faces and key[0] not in out:
                out.append(key[0])
        return out

    def part_verts(self, part: str, node: str | None = None):
        return [v for key, b in self.builders.items() if key[4] == part and (node is None or key[0] == node)
                for v in b.verts]


# --------------------------------------------------------------------------
# Primitive helpers
# --------------------------------------------------------------------------

def _lerp(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def member(mb, p0, p1, r, sides, seg=None, caps=False, smooth=True):
    """A round member p0 -> p1, cut every `seg` metres (baked AO and
    vertex lighting vary along it)."""
    length = v_len(v_sub(p1, p0))
    n = 1 if not seg else max(1, int(math.ceil(length / seg - 1e-9)))
    pts = [_lerp(p0, p1, i / n) for i in range(n + 1)]
    kit.path_tube(mb, pts, r, sides, M, closed=False, caps=caps, smooth=smooth)


def zoned_member(u: Unit, node, group, part, p0, p1, r, sides, seg=None, caps=False,
                 base="hull_yellow", zone="hull_splash", band=SPLASH, z_min=None):
    """A yellow member whose stretch inside the splash band is painted the
    splash-zone ochre; z_min clips it (LOD2 keeps the part above water)."""
    if z_min is not None:
        lo, hi = (p0, p1) if p0[2] <= p1[2] else (p1, p0)
        if hi[2] <= z_min:
            return
        if lo[2] < z_min:
            lo = _lerp(lo, hi, (z_min - lo[2]) / (hi[2] - lo[2]))
        p0, p1 = lo, hi
    cuts = [0.0, 1.0]
    dz = p1[2] - p0[2]
    if abs(dz) > 1e-9:
        for zb in band:
            t = (zb - p0[2]) / dz
            if 1e-6 < t < 1 - 1e-6:
                cuts.append(t)
    cuts.sort()
    for a, b in zip(cuts[:-1], cuts[1:]):
        q0, q1 = _lerp(p0, p1, a), _lerp(p0, p1, b)
        zm = 0.5 * (q0[2] + q1[2])
        strip = zone if band[0] < zm < band[1] else base
        piece = v_len(v_sub(q1, q0))
        sseg = seg if seg else piece
        member(u.mb(node, strip, group, part), q0, q1, r, sides, seg=min(sseg, piece) if seg else None, caps=False)
    if caps:
        mb = u.mb(node, base, group, part)
        disc(mb, p0, v_sub(p0, p1), r, sides)
        disc(mb, p1, v_sub(p1, p0), r, sides)


def disc(mb, p, out, r, sides):
    """A flat cap at p facing `out`."""
    d = v_norm(out)
    e1 = kit.any_perp(d)
    e2 = v_cross(d, e1)
    ring = [mb.vert(v_add(p, v_add(v_mul(e1, r * math.cos(TAU * j / sides)), v_mul(e2, r * math.sin(TAU * j / sides)))))
            for j in range(sides)]
    mb.face(tuple(ring), M, False)


def revolve_at(mb, profile, n, centre, close_top=False, close_bottom=False, smooth=True):
    start = len(mb.verts)
    kit.surface_of_revolution(mb, profile, n, M, close_top=close_top, close_bottom=close_bottom, smooth=smooth)
    for i in range(start, len(mb.verts)):
        x, y, z = mb.verts[i]
        mb.verts[i] = (x + centre[0], y + centre[1], z)


def obox(mb, center, ax, ay, hx, hy, hz, skip_bottom=False):
    """A box turned in plan: ax is its local X (horizontal), Z stays up."""
    ax = v_norm((ax[0], ax[1], 0.0))
    ay = v_norm(v_cross((0.0, 0.0, 1.0), ax)) if ay is None else ay
    kit.box(mb, center, ax, ay, (0.0, 0.0, 1.0), hx, hy, hz, M, skip_bottom=skip_bottom)


def bar(mb, p0, p1, w, h=None, sides=4):
    """A square member (posts, rails, struts), no end caps."""
    h = w if h is None else h
    d = v_sub(p1, p0)
    length = v_len(d)
    if length < 1e-6:
        return
    az = v_norm(d)
    ref = (0.0, 0.0, 1.0) if abs(az[2]) < 0.95 else (1.0, 0.0, 0.0)
    ax = v_norm(v_cross(ref, az))
    ay = v_cross(az, ax)
    c = v_mul(v_add(p0, p1), 0.5)
    hx, hy, hz = w * 0.5, h * 0.5, length * 0.5
    p = []
    for sz in (-1, 1):
        for sy in (-1, 1):
            for sx in (-1, 1):
                p.append(mb.vert(v_add(c, v_add(v_mul(ax, sx * hx), v_add(v_mul(ay, sy * hy), v_mul(az, sz * hz))))))
    quads = [(p[0], p[4], p[6], p[2]), (p[1], p[3], p[7], p[5]), (p[0], p[1], p[5], p[4]), (p[2], p[6], p[7], p[3])]
    for q in quads[:sides]:
        mb.face(q, M, False)


def quad(mb, a, b, c, d, uv=None, mat=M):
    mb.face((mb.vert(a), mb.vert(b), mb.vert(c), mb.vert(d)), mat, False, uv)


def slab_path(mb, pts_in, pts_out, z, thick, edges=True, underside=False, closed=False, steps=1):
    """A deck strip between an inner and an outer polyline at deck top z:
    top face (normal up), outer and inner edges (edges=True, False or an
    (inner, outer) pair), optional underside. Each segment is cut into
    `steps` pieces (baked AO)."""
    e_in, e_out = (edges, edges) if isinstance(edges, bool) else edges
    n = len(pts_in)
    segs = n if closed else n - 1
    for i in range(segs):
        j = (i + 1) % n
        for s in range(steps):
            t0, t1 = s / steps, (s + 1) / steps
            a_in, b_in = _lerp(pts_in[i], pts_in[j], t0), _lerp(pts_in[i], pts_in[j], t1)
            a_out, b_out = _lerp(pts_out[i], pts_out[j], t0), _lerp(pts_out[i], pts_out[j], t1)
            ti, to, tj, tk = ((a_in[0], a_in[1], z), (a_out[0], a_out[1], z), (b_out[0], b_out[1], z),
                              (b_in[0], b_in[1], z))
            quad(mb, ti, to, tj, tk)
            lo = z - thick
            if e_out:
                quad(mb, to, (a_out[0], a_out[1], lo), (b_out[0], b_out[1], lo), tj)
            if e_in:
                quad(mb, tk, (b_in[0], b_in[1], lo), (a_in[0], a_in[1], lo), ti)
            if underside:
                lo = z - thick
                quad(mb, (a_in[0], a_in[1], lo), (b_in[0], b_in[1], lo), (b_out[0], b_out[1], lo),
                     (a_out[0], a_out[1], lo))


def walk_deck(u: Unit, node, group, part, left, right, lod, closed=False, steps=1, kick=0.32):
    """A walkway deck between two polylines (left = inner): galvanised
    grating in the middle, yellow kick plates along both edges, yellow edge
    faces; LOD0 adds the underside."""
    if lod == 2:
        slab_path(u.mb(node, "hull_yellow", group, part), left, right, DECK_Z, DECK_T, edges=False, closed=closed)
        return

    def toward(a, b, f):
        return [_lerp(p, q, f) for p, q in zip(a, b)]
    width = v_len(v_sub(right[0], left[0]))
    f = kick / max(width, 1e-6)
    l_in, r_in = toward(left, right, f), toward(right, left, f)
    yel = u.mb(node, "hull_yellow", group, part)
    slab_path(yel, left, l_in, DECK_Z, DECK_T, edges=(True, False), underside=lod == 0, closed=closed, steps=steps)
    slab_path(yel, r_in, right, DECK_Z, DECK_T, edges=(False, True), underside=lod == 0, closed=closed, steps=steps)
    slab_path(u.mb(node, "grating", group, part), l_in, r_in, DECK_Z, DECK_T, edges=False, underside=lod == 0,
              closed=closed, steps=steps)


def rail(u: Unit, node, group, part, pts, d, closed=False, posts=1.9, height=RAIL_H, toe=True):
    """A handrail along a deck-level polyline: posts, top and knee rails and
    a toe board at LOD0; nothing further out (sub-pixel there)."""
    if d < 2 or len(pts) < 2:
        return
    mb = u.mb(node, "steel", group, part)
    path = list(pts) + ([pts[0]] if closed else [])
    stands = []
    for a, b in zip(path[:-1], path[1:]):
        n = max(1, int(round(v_len(v_sub(b, a)) / posts)))
        stands += [_lerp(a, b, k / n) for k in range(n)]
    if not closed:
        stands.append(path[-1])
    for p in stands:
        bar(mb, p, (p[0], p[1], p[2] + height), 0.06)
    for a, b in zip(path[:-1], path[1:]):
        for hz, w in ((height, 0.06), (height * 0.5, 0.045)):
            bar(mb, (a[0], a[1], a[2] + hz), (b[0], b[1], b[2] + hz), w)
        if toe:
            bar(mb, (a[0], a[1], a[2] + 0.06), (b[0], b[1], b[2] + 0.06), 0.02, 0.12, sides=2)


def ladder(mb, p0, p1, width=0.5, rung=0.3, sides=4):
    """Two stiles and rungs from p0 (bottom) to p1, facing horizontally."""
    d = v_norm(v_sub(p1, p0))
    side = v_norm(v_cross(d, (0.0, 0.0, 1.0))) if abs(d[2]) < 0.99 else (1.0, 0.0, 0.0)
    for s in (-1, 1):
        o = v_mul(side, s * width / 2)
        bar(mb, v_add(p0, o), v_add(p1, o), 0.07, sides=sides)
    length = v_len(v_sub(p1, p0))
    for k in range(1, int(length / rung)):
        c = _lerp(p0, p1, k * rung / length)
        bar(mb, v_add(c, v_mul(side, -width / 2)), v_add(c, v_mul(side, width / 2)), 0.04, sides=2)


# --------------------------------------------------------------------------
# Structure
# --------------------------------------------------------------------------

def _detail(lod):
    """Sides and cut lengths per LOD (0 = LOD0)."""
    return {
        0: dict(col=20, ring=14, mid=10, diag=10, radial=10, seg=3.6, ring_seg=6, foot=28),
        1: dict(col=10, ring=8, mid=6, diag=6, radial=6, seg=14.0, ring_seg=1, foot=12),
        2: dict(col=6, ring=6, mid=4, diag=4, radial=4, seg=None, ring_seg=1, foot=6),
    }[lod]


def structure(u: Unit, lod: int) -> None:
    n = u.sides
    det = _detail(lod)
    z_min = -3.0 if lod == 2 else None   # LOD2: what stands above the water
    hn = "Hull"
    # Pontoon feet under the large columns and the centre pontoon.
    if lod < 2:
        for k in range(0, n, 2):
            x, y, _ = vertex(k, 0.0, sides=n)
            prof = [(D_LARGE / 2, Z_FOOT_CONE), (FOOT_R, Z_FOOT_CYL), (FOOT_R, Z_BOTTOM)]
            if lod == 0:
                prof = [(D_LARGE / 2, Z_FOOT_CONE), (D_LARGE / 2 + 0.9, Z_FOOT_CONE - 1.1),
                        (FOOT_R - 0.4, Z_FOOT_CYL + 0.5), (FOOT_R, Z_FOOT_CYL), (FOOT_R, Z_FOOT_CYL - 1.25),
                        (FOOT_R, Z_BOTTOM + 0.3), (FOOT_R - 0.3, Z_BOTTOM)]
            revolve_at(u.mb(hn, "hull_yellow", PONTOONS, "Feet"), prof, det["foot"], (x, y), close_bottom=True)
        prof = [(D_CENTRE / 2, Z_BOTTOM + 6.0), (4.0, Z_BOTTOM + 4.0), (4.0, Z_BOTTOM)]
        revolve_at(u.mb(hn, "hull_yellow", PONTOONS, "CentrePontoon"), prof, det["foot"], (0.0, 0.0),
                   close_bottom=True)
    # Side columns and the centre column.
    col_bottom = Z_FOOT_CONE
    for k in range(n):
        x, y, _ = vertex(k, 0.0, sides=n)
        dia = D_LARGE if is_large(k) else D_SLIM
        part = "ColumnLarge" if is_large(k) else "ColumnSlim"
        z0 = col_bottom if is_large(k) else Z_BR - D_BOT_RING / 2
        zoned_member(u, "Columns", COLUMNS, part, (x, y, z0), (x, y, DECK_Z - DECK_T), dia / 2, det["col"],
                     seg=det["seg"], z_min=z_min)
        if not is_large(k) and lod < 2:
            # Slim columns stand on the bottom ring: a cap underneath.
            mb = u.mb("Columns", "hull_yellow", COLUMNS, part)
            revolve_at(mb, [(dia / 2, z0), (dia / 2 - 0.4, z0 - 0.5)], det["col"], (x, y), close_bottom=True)
        if lod == 0:
            # Fabricated joint collars where the rings meet the columns.
            for zc, h in ((Z_TR, 3.0), (Z_BR, 2.6)):
                mb = u.mb("Columns", "hull_yellow", COLUMNS, "Collars")
                rr = dia / 2 + 0.22
                revolve_at(mb, [(dia / 2, zc + h / 2 + 0.3), (rr, zc + h / 2), (rr, zc - h / 2),
                                (dia / 2, zc - h / 2 - 0.3)], det["col"], (x, y))
    zoned_member(u, "Columns", COLUMNS, "ColumnCentre", (0.0, 0.0, Z_BOTTOM + 6.0), (0.0, 0.0, DECK_Z - 0.5),
                 D_CENTRE / 2, det["col"], seg=det["seg"], z_min=z_min)
    # Rings: top, mid and bottom, along the polygon through the column axes.
    for zr, dia, part, sides_key in ((Z_TR, D_TOP_RING, "TopRing", "ring"), (Z_MR, D_MID_RING, "MidRing", "mid"),
                                     (Z_BR, D_BOT_RING, "BottomRing", "ring")):
        if z_min is not None and zr < z_min:
            continue
        for k in range(n):
            a, b = vertex(k, zr, sides=n), vertex(k + 1, zr, sides=n)
            seg = v_len(v_sub(b, a)) / det["ring_seg"]
            zoned_member(u, hn, RINGS, part, a, b, dia / 2, det[sides_key], seg=seg)
    # Bottom radial beams to the centre column.
    if lod < 2:
        for k in range(n):
            a = vertex(k, Z_BR, sides=n)
            member(u.mb(hn, "hull_yellow", RINGS, "RadialBeams"), (a[0] * 0.03, a[1] * 0.03, Z_BR), a,
                   D_RADIAL / 2, det["radial"], seg=det["seg"] and det["seg"] * 1.5)
    # Zig-zag diagonals: apex at the top of each large column, at the
    # bottom of each slim one (ref O3), through the mid ring at mid-bay.
    for k in range(n):
        top_k, bot_k = (k, k + 1) if is_large(k) else (k + 1, k)
        p_top = vertex(top_k, Z_TR - 1.2, sides=n)
        p_bot = vertex(bot_k, Z_BR + 1.1, sides=n)
        # Ends land on the column surfaces, not their axes.
        d = v_norm(v_sub(p_bot, p_top))
        p_top = v_add(p_top, v_mul(d, (D_LARGE if is_large(top_k) else D_SLIM) / 2))
        p_bot = v_sub(p_bot, v_mul(d, (D_LARGE if is_large(bot_k) else D_SLIM) / 2))
        zoned_member(u, "Bracing", BRACING, "Diagonals", p_bot, p_top, D_DIAG / 2, det["diag"],
                     seg=det["seg"], z_min=z_min)
    # Top cross gantry (through vertices 0 and n/2) and the top spoke over
    # the fixed bulkhead at vertex n/3.
    g0, g1 = vertex(0, Z_TR, sides=n), vertex(n // 2, Z_TR, sides=n)
    member(u.mb(hn, "hull_yellow", RINGS, "Gantry"), g0, g1, D_GANTRY / 2, det["ring"],
           seg=det["seg"] and det["seg"] * 1.5)
    s1 = vertex(n // 3, Z_TR, sides=n)
    member(u.mb(hn, "hull_yellow", RINGS, "Spoke"), (s1[0] * 0.03, s1[1] * 0.03, Z_TR), s1, D_SPOKE / 2,
           det["mid"], seg=det["seg"] and det["seg"] * 1.5)
    # Platform struts from the centre column to the platform corners.
    if lod < 2:
        for cx, cy in ((1, 1), (-1, 1), (-1, -1), (1, -1)):
            p = (cx * (PLATFORM_HALF - 1.0), cy * (PLATFORM_HALF - 1.0), DECK_Z - 0.6)
            member(u.mb("Bracing", "hull_yellow", BRACING, "PlatformStruts"), (cx * 1.2, cy * 1.2, Z_TR - 8.0), p,
                   0.45, 8 if lod == 0 else 4)


# --------------------------------------------------------------------------
# Decks, walkways, handrails
# --------------------------------------------------------------------------

def _ring_lines(n, apo_in, apo_out):
    ri, ro = poly_r(apo_in, n), poly_r(apo_out, n)
    pin = [(ri * math.cos(vertex_angle(k, n)), ri * math.sin(vertex_angle(k, n)), DECK_Z) for k in range(n)]
    pout = [(ro * math.cos(vertex_angle(k, n)), ro * math.sin(vertex_angle(k, n)), DECK_Z) for k in range(n)]
    return pin, pout


def _radial_walk(u: Unit, node, group, part, angle, r0, r1, width, lod, rails=True):
    """A straight walkway deck along `angle` from r0 to r1 (top at DECK_Z)."""
    c, s = math.cos(angle), math.sin(angle)
    nx, ny = -s * width / 2, c * width / 2
    a0, a1 = (c * r0, s * r0), (c * r1, s * r1)
    left = [(a0[0] + nx, a0[1] + ny, DECK_Z), (a1[0] + nx, a1[1] + ny, DECK_Z)]
    right = [(a0[0] - nx, a0[1] - ny, DECK_Z), (a1[0] - nx, a1[1] - ny, DECK_Z)]
    walk_deck(u, node, group, part, left, right, lod, steps=max(1, int((r1 - r0) / 4.0)) if lod == 0 else 1)
    if rails:
        rail(u, node, group, part + "Rail", left, lod_to_d(lod))
        rail(u, node, group, part + "Rail", right, lod_to_d(lod))


def lod_to_d(lod: int) -> int:
    return 2 - lod


def decks(u: Unit, lod: int) -> None:
    n = u.sides
    d = lod_to_d(lod)
    apo = apothem(n)
    tn = "Topsides"
    # Ring walkway on top of the top ring.
    pin, pout = _ring_lines(n, apo - WALK_W / 2, apo + WALK_W / 2)
    walk_deck(u, tn, TOPSIDES, "RingWalkway", pin, pout, lod, closed=True, steps=6 if lod == 0 else 1)
    if lod == 0:
        # Brackets from the ring tube up to the deck every ~4.75 m.
        mb = u.mb(tn, "hull_yellow", TOPSIDES, "RingWalkway")
        for k in range(n):
            for s in range(1, 6):
                p = _lerp(vertex(k, Z_TR, sides=n), vertex(k + 1, Z_TR, sides=n), s / 6)
                obox(mb, (p[0], p[1], DECK_Z - DECK_T - 0.07), (p[0], p[1], 0.0), None, WALK_W / 2 - 0.1, 0.12, 0.07)
    # Outer rail all round; inner rail per bay with gaps at the corners
    # (where the gantry, spoke, cranes and lanterns meet the ring).
    rail(u, tn, TOPSIDES, "RingRail", [(p[0], p[1], DECK_Z) for p in pout], d, closed=True)
    for k in range(n):
        a, b = pin[k], pin[(k + 1) % n]
        rail(u, tn, TOPSIDES, "RingRail", [_lerp(a, b, 0.07), _lerp(a, b, 0.93)], d)
    # Gantry walkway, the spoke walkway, both from the platform to the ring.
    r_ring_in = poly_r(apo - WALK_W / 2, n) - 0.4
    for k in (0, n // 2):
        _radial_walk(u, tn, TOPSIDES, "GantryWalk", vertex_angle(k, n), PLATFORM_HALF, r_ring_in, GANTRY_WALK_W, lod,
                     rails=d == 2)
    _radial_walk(u, tn, TOPSIDES, "SpokeWalk", vertex_angle(n // 3, n), PLATFORM_HALF * 1.05, r_ring_in,
                 SPOKE_WALK_W, lod, rails=d == 2)
    # Centre platform: steel deck plate, yellow edge girders, handrail.
    h = PLATFORM_HALF
    sq_in = [(-h + 0.01, -h + 0.01, DECK_Z), (h - 0.01, -h + 0.01, DECK_Z), (h - 0.01, h - 0.01, DECK_Z),
             (-h + 0.01, h - 0.01, DECK_Z)]
    mb = u.mb(tn, "deck_plate", TOPSIDES, "Platform")
    steps = 4 if lod == 0 else 1
    for i in range(steps):
        for j in range(steps):
            x0, x1 = -h + 2 * h * i / steps, -h + 2 * h * (i + 1) / steps
            y0, y1 = -h + 2 * h * j / steps, -h + 2 * h * (j + 1) / steps
            quad(mb, (x0, y0, DECK_Z), (x1, y0, DECK_Z), (x1, y1, DECK_Z), (x0, y1, DECK_Z))
    girder = u.mb(tn, "hull_yellow", TOPSIDES, "Platform")
    for a, b in zip(sq_in, sq_in[1:] + sq_in[:1]):
        # Outward-facing edge girder 0.9 m deep under the plate.
        dx, dy = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dy)
        nx, ny = dy / ln, -dx / ln
        a_o, b_o = (a[0] + nx * 0.01, a[1] + ny * 0.01), (b[0] + nx * 0.01, b[1] + ny * 0.01)
        quad(girder, (a_o[0], a_o[1], DECK_Z - 0.9), (b_o[0], b_o[1], DECK_Z - 0.9), (b_o[0], b_o[1], DECK_Z),
             (a_o[0], a_o[1], DECK_Z))
    if lod == 0:
        # Net-lifting winches on the inner edge of every bay but those by the
        # gantry, spoke and bulkhead corners.
        busy = {0, n // 2, n // 3, 2 * n // 3}
        for k in range(n):
            if k in busy or (k + 1) % n in busy:
                continue
            p = _poly_point(k + 0.5, apo - WALK_W / 2 + 0.75, DECK_Z, n)
            er, et = _tangent_frame(TAU * (k + 0.5) / n)
            obox(u.mb(tn, "grey", TOPSIDES, "NetWinches"), (p[0], p[1], DECK_Z + 0.45), et, None, 0.75, 0.5, 0.45,
                 skip_bottom=True)
            drum = v_add((p[0], p[1], DECK_Z + 0.55), v_mul(er, -0.65))
            member(u.mb(tn, "dark", TOPSIDES, "NetWinches"), v_add(drum, v_mul(et, -0.6)), v_add(drum, v_mul(et, 0.6)),
                   0.32, 10, caps=True)
        mbu = u.mb(tn, "hull_yellow", TOPSIDES, "Platform")
        quad(mbu, (-h, -h, DECK_Z - 0.9), (-h, h, DECK_Z - 0.9), (h, h, DECK_Z - 0.9), (h, -h, DECK_Z - 0.9))
        # Platform rail with openings where the walkways land.
        for a, b in zip(sq_in, sq_in[1:] + sq_in[:1]):
            rail(u, tn, TOPSIDES, "PlatformRail", [_lerp(a, b, 0.0), _lerp(a, b, 0.38)], d)
            rail(u, tn, TOPSIDES, "PlatformRail", [_lerp(a, b, 0.62), _lerp(a, b, 1.0)], d)


# --------------------------------------------------------------------------
# Topsides
# --------------------------------------------------------------------------

def _storey(u: Unit, lod: int, half, z0, h, band, mullion, faces="fblr", rake=0.0):
    paint = (u.mb("Topsides", "white", TOPSIDES, "Deckhouse"), M)
    glass = (u.mb("Topsides", "window", TOPSIDES, "DeckhouseWindows"), M)
    hull.deckhouse(paint, glass if lod < 2 else None, half, -half, half, z0, h,
                   band=band if lod < 2 else None, faces=faces, front_rake=rake,
                   mullion=mullion if lod == 0 else 0.0, inset=0.08 if lod == 0 else 0.0, roof=False)


def deckhouse(u: Unit, lod: int) -> None:
    """Control/accommodation block on the centre platform: two accommodation
    storeys and the control room on top with its visor, a stair tower, roof
    gear, the mast with radar, AIS and antennas, and the Beacon."""
    tn = "Topsides"
    z = DECK_Z
    if lod == 2:
        obox(u.mb(tn, "white", TOPSIDES, "Deckhouse"), (0.0, 0.0, (z + HOUSE_TOP) / 2), (1, 0, 0), None,
             HOUSE_HALF, HOUSE_HALF, (HOUSE_TOP - z) / 2, skip_bottom=True)
        u.markers["Beacon"] = (0.0, -1.5, MAST_TOP)
        return
    _storey(u, lod, HOUSE_HALF, z, 3.1, (0.36, 0.78), 1.6)
    _storey(u, lod, HOUSE_HALF, z + 3.1, 3.1, (0.36, 0.78), 1.6)
    # Floor lip between the storeys, the roof deck round the control room.
    lip = u.mb(tn, "white", TOPSIDES, "DeckhouseTrim")
    obox(lip, (0.0, 0.0, z + 3.1), (1, 0, 0), None, HOUSE_HALF + 0.25, HOUSE_HALF + 0.25, 0.12)
    obox(lip, (0.0, 0.0, z + 6.2 - 0.12), (1, 0, 0), None, HOUSE_HALF + 0.35, HOUSE_HALF + 0.35, 0.12)
    deck2 = u.mb(tn, "roof", TOPSIDES, "DeckhouseTrim")
    e2 = HOUSE_HALF + 0.3
    quad(deck2, (-e2, -e2, z + 6.21), (e2, -e2, z + 6.21), (e2, e2, z + 6.21), (-e2, e2, z + 6.21))
    if lod == 0:
        e = HOUSE_HALF + 0.1
        rail(u, tn, TOPSIDES, "RoofRail", [(-e, -e, z + 6.21), (e, -e, z + 6.21), (e, e, z + 6.21), (-e, e, z + 6.21)], 2,
             closed=True, height=1.0, toe=False)
    # Control room set back on top (10 x 10): tall windows all round.
    _storey(u, lod, 5.0, z + 6.21, 3.2, (0.2, 0.93), 1.1, rake=0.0)
    roof_z = HOUSE_TOP - 0.4
    roof = u.mb(tn, "roof", TOPSIDES, "DeckhouseTrim")
    hh = 5.0 + 0.3
    quad(roof, (-hh, -hh, roof_z), (hh, -hh, roof_z), (hh, hh, roof_z), (-hh, hh, roof_z))
    # Parapet / visor band to the house top.
    vis = u.mb(tn, "white", TOPSIDES, "DeckhouseTrim")
    for a, b in (((-hh, -hh), (hh, -hh)), ((hh, -hh), (hh, hh)), ((hh, hh), (-hh, hh)), ((-hh, hh), (-hh, -hh))):
        dx, dy = b[0] - a[0], b[1] - a[1]
        ln = math.hypot(dx, dy)
        nx, ny = dy / ln * 0.5, -dx / ln * 0.5
        quad(vis, (a[0] + nx, a[1] + ny, roof_z), (b[0] + nx, b[1] + ny, roof_z), (b[0] + nx, b[1] + ny, HOUSE_TOP),
             (a[0] + nx, a[1] + ny, HOUSE_TOP))
        quad(vis, (a[0], a[1], roof_z - 0.05), (b[0], b[1], roof_z - 0.05), (b[0] + nx, b[1] + ny, roof_z - 0.05),
             (a[0] + nx, a[1] + ny, roof_z - 0.05))
        if lod == 0:
            quad(vis, (b[0], b[1], roof_z), (a[0], a[1], roof_z), (a[0], a[1], HOUSE_TOP), (b[0], b[1], HOUSE_TOP))
            quad(vis, (a[0], a[1], HOUSE_TOP), (a[0] + nx, a[1] + ny, HOUSE_TOP), (b[0] + nx, b[1] + ny, HOUSE_TOP),
                 (b[0], b[1], HOUSE_TOP))
    # Stair tower on +Y, grey, with a door at each level.
    grey = u.mb(tn, "grey", TOPSIDES, "StairTower")
    st_top = z + 6.21 + 2.6
    obox(grey, (-3.0, HOUSE_HALF + 1.7, (z + st_top) / 2), (1, 0, 0), None, 1.9, 1.7, (st_top - z) / 2,
         skip_bottom=True)
    if lod == 0:
        door = u.mb(tn, "door", TOPSIDES, "StairTower")
        for lv in range(3):
            obox(door, (-3.0 + 1.91, HOUSE_HALF + 1.7, z + 1.05 + 3.1 * lv), (0, 1, 0), None, 0.5, 0.04, 1.0)
        obox(door, (2.5, -HOUSE_HALF - 0.04, z + 1.05), (1, 0, 0), None, 0.55, 0.05, 1.05)
        obox(door, (HOUSE_HALF + 0.04, 2.0, z + 1.05), (0, 1, 0), None, 0.55, 0.05, 1.05)
    # Roof gear: HVAC units, exhaust, mast with radar, AIS dome, antennas.
    gear = u.mb(tn, "grey", TOPSIDES, "RoofGear")
    z2 = z + 6.21
    obox(gear, (6.1, 3.6, z2 + 0.6), (0, 1, 0), None, 1.6, 0.6, 0.6, skip_bottom=True)
    obox(gear, (-6.1, -3.8, z2 + 0.45), (0, 1, 0), None, 1.4, 0.55, 0.45, skip_bottom=True)
    member(gear, (6.2, -6.2, z2), (6.2, -6.2, z2 + 3.6), 0.35, 10 if lod == 0 else 6, caps=False)
    member(u.mb(tn, "dark", TOPSIDES, "RoofGear"), (6.2, -6.2, z2 + 3.6), (6.2, -6.2, z2 + 3.9), 0.36,
           10 if lod == 0 else 6, caps=True)
    mast = u.mb(tn, "white", TOPSIDES, "Mast")
    mx, my = 0.0, -1.5
    member(mast, (mx, my, HOUSE_TOP - 0.4), (mx, my, MAST_TOP - 0.5), 0.16, 8 if lod == 0 else 5, caps=False)
    bar(mast, (mx - 1.8, my, MAST_TOP - 1.4), (mx + 1.8, my, MAST_TOP - 1.4), 0.12)
    member(mast, (mx, my - 0.6, HOUSE_TOP), (mx, my - 0.6, HOUSE_TOP + 1.0), 0.28, 8 if lod == 0 else 5, caps=True)
    radar = u.mb(tn, "dark", TOPSIDES, "Mast")
    obox(radar, (mx, my - 0.6, HOUSE_TOP + 1.2), (1, 0, 0), None, 1.5, 0.14, 0.12)
    if lod == 0:
        # AIS / broadband domes and whip antennas.
        for dx in (-1.5, 1.5):
            revolve_at(u.mb(tn, "white", TOPSIDES, "Mast"), [(0.02, MAST_TOP - 0.85), (0.32, MAST_TOP - 1.05),
                                                             (0.32, MAST_TOP - 1.35)], 10, (mx + dx, my),
                       close_top=False)
        steel = u.mb(tn, "steel", TOPSIDES, "Mast")
        for dx, h in ((-0.9, 2.2), (0.9, 1.8), (1.4, 1.4)):
            member(steel, (mx + dx, my + 0.4, HOUSE_TOP), (mx + dx, my + 0.4, HOUSE_TOP + h), 0.03, 4)
        rail(u, tn, TOPSIDES, "RoofRail", [(-hh + 0.3, -hh + 0.3, HOUSE_TOP), (hh - 0.3, -hh + 0.3, HOUSE_TOP),
                                          (hh - 0.3, hh - 0.3, HOUSE_TOP), (-hh + 0.3, hh - 0.3, HOUSE_TOP)], 2,
             closed=True, height=0.9, toe=False)
    u.markers["Beacon"] = (mx, my, MAST_TOP)


def _ring_point(k: float, r: float, z: float, n: int):
    th = TAU * k / n
    return (r * math.cos(th), r * math.sin(th), z)


def _poly_point(f: float, apo_off: float, z: float, n: int):
    """The point at fractional vertex index f on the polygon whose sides sit
    at apothem apo_off (on the walkway lines, not on a circle)."""
    k = math.floor(f)
    a, b = _ring_point(k, poly_r(apo_off, n), z, n), _ring_point(k + 1, poly_r(apo_off, n), z, n)
    return _lerp(a, b, f - k)


def _tangent_frame(angle):
    """(radial, tangential) unit vectors in plan at `angle`."""
    return (math.cos(angle), math.sin(angle), 0.0), (-math.sin(angle), math.cos(angle), 0.0)


def _cantilever_deck(u: Unit, lod, k, r_c, half_t, half_r, part, strip="deck_plate"):
    """A deck cantilevered outward at vertex k (centre at radius r_c),
    half_t along the ring, half_r radially, with knee braces to the column.
    Returns (centre, radial, tangential)."""
    n = u.sides
    ang = vertex_angle(k, n)
    er, et = _tangent_frame(ang)
    c = (er[0] * r_c, er[1] * r_c, DECK_Z)
    top = DECK_Z + 0.04   # clear of the ring walkway it meets
    mb = u.mb("Topsides", strip, TOPSIDES, part)
    corners = [v_add((c[0], c[1], top), v_add(v_mul(er, sr * half_r), v_mul(et, st * half_t))) for sr, st in
               ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    quad(mb, *corners)
    edge = u.mb("Topsides", "hull_yellow", TOPSIDES, part)
    for a, b in zip(corners, corners[1:] + corners[:1]):
        quad(edge, (a[0], a[1], DECK_Z - 0.7), (b[0], b[1], DECK_Z - 0.7), b, a)
    if lod == 0:
        quad(edge, *[(p[0], p[1], DECK_Z - 0.7) for p in reversed(corners)])
    if lod < 2:
        col = vertex(k, Z_TR - 7.0, sides=n)
        for st in (-1, 1):
            tip = v_add(c, v_add(v_mul(er, half_r - 0.5), v_mul(et, st * (half_t - 0.6))))
            member(u.mb("Topsides", "hull_yellow", TOPSIDES, part), col, (tip[0], tip[1], DECK_Z - 0.7), 0.35,
                   8 if lod == 0 else 4)
    return c, er, et


def silos(u: Unit, lod: int) -> None:
    """Four Ø4 x 10 silos (2 x 2) and the feed blower house on a deck
    cantilevered outside the ring at vertex n/6, feed pipes to two zones."""
    n = u.sides
    k = max(1, n // 6)
    c, er, et = _cantilever_deck(u, lod, k, R + 6.6, 8.0, 5.1, "SiloDeck")
    sides = 20 if lod == 0 else (12 if lod == 1 else 6)
    tops = []
    for sr in (-1, 1):
        for st in (-1, 1):
            p = v_add(c, v_add(v_mul(er, sr * 2.55), v_mul(et, -3.4 + st * 2.55)))
            if lod == 2:
                obox(u.mb("Topsides", "silo", TOPSIDES, "Silos"), (p[0], p[1], DECK_Z + SILO_H / 2), er, None,
                     SILO_R * 0.9, SILO_R * 0.9, SILO_H / 2, skip_bottom=True)
                continue
            mb = u.mb("Topsides", "silo", TOPSIDES, "Silos")
            z0, z1 = DECK_Z + 0.8, DECK_Z + SILO_H - 1.3
            prof = [(0.35, DECK_Z + SILO_H), (SILO_R, z1), (SILO_R, z0 + 0.6), (SILO_R - 0.05, z0)]
            if lod == 0:
                prof = [(0.35, DECK_Z + SILO_H), (SILO_R - 0.08, z1 + 0.05), (SILO_R, z1), (SILO_R, z1 - 2.5),
                        (SILO_R, z0 + 2.5), (SILO_R, z0 + 0.6), (SILO_R - 0.05, z0)]
            revolve_at(mb, prof, sides, (p[0], p[1]), close_top=True)
            skirt = u.mb("Topsides", "grey", TOPSIDES, "SiloFittings")
            revolve_at(skirt, [(SILO_R - 0.05, z0), (SILO_R - 0.3, DECK_Z)], sides, (p[0], p[1]))
            if lod == 0:
                band = u.mb("Topsides", "steel", TOPSIDES, "SiloFittings")
                for zb in (z0 + 2.4, z1 - 2.4):
                    revolve_at(band, [(SILO_R + 0.06, zb + 0.1), (SILO_R + 0.06, zb - 0.1)], sides, (p[0], p[1]))
                # Fill pipe up the outer side.
                fp = v_add(p, v_mul(er, sr * (SILO_R + 0.25)))
                member(band, (fp[0], fp[1], DECK_Z), (fp[0], fp[1], z1 + 0.2), 0.11, 6)
            tops.append((p[0], p[1], DECK_Z + SILO_H - 0.6))
    # Blower house on the deck's other end.
    bh = v_add(c, v_mul(et, 5.2))
    obox(u.mb("Topsides", "grey", TOPSIDES, "BlowerHouse"), (bh[0], bh[1], DECK_Z + 1.5), et, None, 2.3, 4.0, 1.5,
         skip_bottom=True)
    if lod == 0:
        obox(u.mb("Topsides", "door", TOPSIDES, "BlowerHouse"), v_add((bh[0], bh[1], DECK_Z + 1.05),
                                                                       v_mul(et, -2.32)), et, None, 0.03, 0.6, 1.05)
        obox(u.mb("Topsides", "roof", TOPSIDES, "BlowerHouse"), (bh[0], bh[1], DECK_Z + 3.05), et, None, 2.45, 4.15,
             0.06)
        # Catwalk over the silo tops with a ladder and rails.
        cw = u.mb("Topsides", "grating", TOPSIDES, "SiloDeck")
        a = v_add(c, v_mul(et, -3.4 - 2.55))
        b = v_add(c, v_mul(et, -3.4 + 2.55))
        zc = DECK_Z + SILO_H - 0.5
        obox(cw, ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, zc), et, None, 3.6, 0.6, 0.06)
        rail(u, "Topsides", TOPSIDES, "SiloRail", [v_add((a[0], a[1], zc), v_mul(er, 0.6)),
                                                    v_add((b[0], b[1], zc), v_mul(er, 0.6))], 2, toe=False)
        lad = v_add(c, v_add(v_mul(er, -0.3), v_mul(et, -3.4 - 5.3)))
        ladder(u.mb("Topsides", "steel", TOPSIDES, "SiloDeck"), (lad[0], lad[1], DECK_Z), (lad[0], lad[1], zc))
        # Feed pipes from the blower house along the ring deck to two zones,
        # then down through the net to the water.
        pipe = u.mb("Topsides", "steel", TOPSIDES, "FeedPipes")
        apo = apothem(n)
        a_pipe = apo + WALK_W / 2 - 0.3
        for step in (-1, 1):
            start = v_add((bh[0], bh[1], DECK_Z + 0.45), v_mul(et, -2.0 * step))
            pts = [start, _poly_point(k + 0.3 * step, a_pipe, DECK_Z + 0.45, n)]
            pts += [_poly_point(k + step * j, a_pipe, DECK_Z + 0.45, n) for j in (1, 2)]
            pts.append(_poly_point(k + step * 2.4, a_pipe, DECK_Z + 0.45, n))
            end_in = _poly_point(k + step * 2.4, apo - WALK_W / 2 - 1.5, DECK_Z + 0.45, n)
            pts += [end_in, (end_in[0], end_in[1], 0.6)]
            kit.path_tube(pipe, pts, 0.14, 6, M, caps=False)


def power_house(u: Unit, lod: int) -> None:
    """Grey power/handling house on a deck cantilevered at vertex 5n/6."""
    n = u.sides
    k = n - max(1, n // 6)
    c, er, et = _cantilever_deck(u, lod, k, R + 6.5, 6.0, 5.0, "PowerDeck")
    h = 9.0
    obox(u.mb("Topsides", "grey", TOPSIDES, "PowerHouse"), (c[0], c[1], DECK_Z + h / 2), er, None, 4.0, 5.2, h / 2,
         skip_bottom=True)
    if lod == 2:
        return
    obox(u.mb("Topsides", "roof", TOPSIDES, "PowerHouse"), (c[0], c[1], DECK_Z + h + 0.06), er, None, 4.15, 5.35, 0.08)
    if lod == 0:
        door = u.mb("Topsides", "door", TOPSIDES, "PowerHouse")
        p = v_add(c, v_mul(er, -4.03))
        obox(door, (p[0], p[1], DECK_Z + 1.8), er, None, 0.04, 1.6, 1.8)
        for st in (-1, 1):
            q = v_add(c, v_add(v_mul(er, 4.03), v_mul(et, st * 2.4)))
            obox(door, (q[0], q[1], DECK_Z + 6.2), er, None, 0.04, 1.4, 0.9)
        win = u.mb("Topsides", "window", TOPSIDES, "PowerHouse")
        for st in (-1, 1):
            q = v_add(c, v_mul(et, st * 5.23))
            obox(win, (q[0], q[1], DECK_Z + 6.4), et, None, 0.03, 2.4, 0.5)
        stack = u.mb("Topsides", "grey", TOPSIDES, "PowerHouse")
        for st in (-1, 1):
            q = v_add(c, v_add(v_mul(er, 1.8), v_mul(et, st * 1.2)))
            member(stack, (q[0], q[1], DECK_Z + h), (q[0], q[1], DECK_Z + h + 2.2), 0.3, 10)
            member(u.mb("Topsides", "dark", TOPSIDES, "PowerHouse"), (q[0], q[1], DECK_Z + h + 2.2),
                   (q[0], q[1], DECK_Z + h + 2.45), 0.31, 10, caps=True)
        ladder(u.mb("Topsides", "steel", TOPSIDES, "PowerHouse"),
               v_add((c[0], c[1], DECK_Z), v_add(v_mul(er, 4.25), v_mul(et, -4.0))),
               v_add((c[0], c[1], DECK_Z + h + 1.0), v_add(v_mul(er, 4.25), v_mul(et, -4.0))))


def harvest_chute(u: Unit, lod: int) -> None:
    """The white harvest/transfer tube of ref O3: from the control block
    down to the ring at vertex 7n/12, on a light truss."""
    n = u.sides
    ang = vertex_angle(7 * n // 12, n)
    er, _et = _tangent_frame(ang)
    p0 = (er[0] * (HOUSE_HALF + 0.6), er[1] * (HOUSE_HALF + 0.6), DECK_Z + 6.0)
    r_end = poly_r(apothem(n) - WALK_W / 2, n) - 1.0
    p1 = (er[0] * r_end, er[1] * r_end, DECK_Z + 2.2)
    sides = 14 if lod == 0 else (8 if lod == 1 else 4)
    member(u.mb("Topsides", "white", TOPSIDES, "Chute"), p0, p1, 0.75, sides, seg=4.0 if lod == 0 else None,
           caps=True)
    if lod == 2:
        return
    # Under-truss chord and zig-zag struts.
    chord0, chord1 = v_add(p0, (0, 0, -2.2)), v_add(p1, (0, 0, -2.2))
    tr = u.mb("Topsides", "steel", TOPSIDES, "Chute")
    bar(tr, chord0, chord1, 0.25)
    if lod == 0:
        m = 10
        for i in range(m):
            a = _lerp(p0, p1, i / m)
            b = _lerp(chord0, chord1, (i + 1) / m)
            bar(tr, v_add(a, (0, 0, -0.7)), b, 0.14)
            bar(tr, _lerp(chord0, chord1, i / m), v_add(_lerp(p0, p1, i / m), (0, 0, -0.7)), 0.14)
        # Hopper box where it lands on the ring.
        obox(u.mb("Topsides", "white", TOPSIDES, "Chute"), (p1[0], p1[1], DECK_Z + 1.3), er, None, 1.4, 1.2, 1.3,
             skip_bottom=True)


def landings(u: Unit, lod: int) -> None:
    """Boat landing outside the large column at vertex n/2 (ladder,
    fender posts, a landing platform) for crew boats, and the walk-to-work
    landing on the ring walkway at mid-bay 2n/3..2n/3+1, clear of it, where
    a service vessel lying alongside rests its gangway."""
    if lod == 2:
        return
    n = u.sides
    k = n // 2
    ang = vertex_angle(k, n)
    er, et = _tangent_frame(ang)
    base = vertex(k, 0.0, sides=n)
    off = D_LARGE / 2 + 0.8
    lad_p = v_add(base, v_mul(er, off))
    if lod == 0:
        ladder(u.mb("Topsides", "hull_yellow", TOPSIDES, "BoatLanding"), (lad_p[0], lad_p[1], -3.0),
               (lad_p[0], lad_p[1], DECK_Z + 1.0), width=0.6)
    fender = u.mb("Topsides", "dark", TOPSIDES, "BoatLanding")
    for st in (-1, 1):
        f = v_add(base, v_add(v_mul(er, off + 0.9), v_mul(et, st * 1.4)))
        member(fender, (f[0], f[1], -3.5), (f[0], f[1], 6.0), 0.3, 10 if lod == 0 else 5, caps=True)
        frame = u.mb("Topsides", "hull_yellow", TOPSIDES, "BoatLanding")
        for zz in (-1.5, 2.0, 5.5):
            col = v_add(base, v_mul(et, st * 1.2))
            bar(frame, (col[0], col[1], zz), (f[0], f[1], zz), 0.3)
    plat = v_add(base, v_mul(er, off + 0.4))
    obox(u.mb("Topsides", "grating", TOPSIDES, "BoatLanding"), (plat[0], plat[1], 3.4), er, None, 1.2, 1.8, 0.1)
    # Walk-to-work landing: a grating apron out from the ring walkway at
    # mid-bay k..k+1 with a gate, where the service vessel's gangway rests.
    kw = 2 * n // 3
    mid = _lerp(vertex(kw, DECK_Z, sides=n), vertex(kw + 1, DECK_Z, sides=n), 0.5)
    nrm = v_norm((mid[0], mid[1], 0.0))
    tan = v_norm(v_sub(vertex(kw + 1, 0.0, sides=n), vertex(kw, 0.0, sides=n)))
    c = v_add(mid, v_mul(nrm, WALK_W / 2 + 1.6))
    obox(u.mb("Topsides", "grating", TOPSIDES, "W2WLanding"), (c[0], c[1], DECK_Z - 0.11), tan, None, 2.6, 1.6, 0.11)
    if lod == 0:
        pts = [v_add(c, v_add(v_mul(tan, -2.6), v_mul(nrm, -1.6))), v_add(c, v_add(v_mul(tan, -2.6), v_mul(nrm, 1.6))),
               v_add(c, v_add(v_mul(tan, 2.6), v_mul(nrm, 1.6))), v_add(c, v_add(v_mul(tan, 2.6), v_mul(nrm, -1.6)))]
        rail(u, "Topsides", TOPSIDES, "W2WLanding", [(p[0], p[1], DECK_Z) for p in pts[:2]], 2)
        rail(u, "Topsides", TOPSIDES, "W2WLanding", [(p[0], p[1], DECK_Z) for p in pts[2:]], 2)
        rail(u, "Topsides", TOPSIDES, "W2WLanding", [(pts[1][0], pts[1][1], DECK_Z), _lerp((pts[1][0], pts[1][1], DECK_Z),
                                                                                         (pts[2][0], pts[2][1], DECK_Z), 0.3)], 2)
        u.markers["W2WLanding"] = (c[0], c[1], DECK_Z)


def outfitting(u: Unit, lod: int) -> None:
    """LOD0 small gear: deck lights at the corners, lifebuoys on the outer
    rail, life-raft canisters, the rescue boat on its davit, lantern posts."""
    n = u.sides
    apo = apothem(n)
    if lod == 0:
        for k in range(n):
            p = _ring_point(k, poly_r(apo - WALK_W / 2 + 0.25, n), DECK_Z, n)
            post = u.mb("Topsides", "steel", TOPSIDES, "DeckLights")
            member(post, p, (p[0], p[1], DECK_Z + 3.4), 0.06, 5)
            er, _ = _tangent_frame(vertex_angle(k, n))
            lamp = v_add((p[0], p[1], DECK_Z + 3.35), v_mul(er, -0.35))
            obox(u.mb("Topsides", "window", TOPSIDES, "DeckLights"), lamp, er, None, 0.3, 0.2, 0.12)
        for k in range(1, n, 2):
            p = _poly_point(k + 0.5, apo + WALK_W / 2 + 0.06, DECK_Z + 0.7, n)
            er, _ = _tangent_frame(TAU * (k + 0.5) / n)
            hull.torus((u.mb("Topsides", "safety", TOPSIDES, "Lifebuoys"), M), p, er, 0.33, 0.07, 12, 5)
        for k in (2, 8):
            if k >= n:
                continue
            p = _poly_point(k + 0.5, apo + 0.5, DECK_Z + 0.55, n)
            _, et = _tangent_frame(TAU * (k + 0.5) / n)
            member(u.mb("Topsides", "white", TOPSIDES, "LifeRafts"), v_add(p, v_mul(et, -0.7)), v_add(p, v_mul(et, 0.7)),
                   0.38, 10, caps=True)
        # Rescue boat in its cradle with a davit, platform corner.
        boat = u.mb("Topsides", "safety", TOPSIDES, "RescueBoat")
        bc = (PLATFORM_HALF - 2.4, PLATFORM_HALF - 3.8, DECK_Z + 1.0)
        obox(boat, bc, (0, 1, 0), None, 2.6, 1.0, 0.55, skip_bottom=False)
        obox(u.mb("Topsides", "white", TOPSIDES, "RescueBoat"), v_add(bc, (0.0, 0.5, 0.75)), (0, 1, 0), None, 0.8, 0.6,
             0.25)
        dav = u.mb("Topsides", "white", TOPSIDES, "RescueBoat")
        kit.path_tube(dav, [(bc[0] - 1.4, bc[1], DECK_Z), (bc[0] - 1.4, bc[1], DECK_Z + 3.4),
                            (bc[0] + 0.2, bc[1], DECK_Z + 3.9), (bc[0] + 1.3, bc[1], DECK_Z + 3.4)], 0.13, 8, M)
    # Lantern posts on the large columns (the lanterns go in NavLights).
    lanterns = []
    for k in range(0, n, 2):
        ang = vertex_angle(k, n)
        p = _ring_point(k, poly_r(apo + WALK_W / 2 - 0.25, n), DECK_Z, n)
        if lod < 2:
            member(u.mb("Topsides", "steel", TOPSIDES, "LanternPosts"), p, (p[0], p[1], DECK_Z + 1.4), 0.07,
                   6 if lod == 0 else 4)
        lanterns.append((p[0], p[1], DECK_Z + 1.4, ang))
    if lod < 2:
        nav = u.mb("NavLights", "nav_light", TOPSIDES, "Lanterns")
        for x, y, z, _a in lanterns:
            revolve_at(nav, [(0.05, z + 0.62), (0.2, z + 0.55), (0.22, z + 0.12), (0.12, z)], 10 if lod == 0 else 6,
                       (x, y), close_top=True)
        bx, by, bz = u.markers["Beacon"]
        revolve_at(nav, [(0.05, bz + 0.6), (0.24, bz + 0.52), (0.26, bz + 0.1), (0.14, bz)], 10 if lod == 0 else 6,
                   (bx, by), close_top=True)


# Working gear (lookdev, wave 4): the OF1 photos show the ring's columns
# crowded with white containers, cable reels and cabinets, the scale cue a
# bare ring lacks at the 220 m framing. Equipment decks go outside the free
# slim columns of the 12-gon (crane A sits on 5, crane B on 11; the chute
# lands inside 7, the walk-to-work apron is at mid-bay 8-9 where the service
# vessel lies, so 9 stays clear of its hull).
GEAR_COLUMNS = (1, 3, 7)
GEAR_DECK_R = R + 3.9          # deck centre radius; it spans R + 1.4 .. R + 6.4
GEAR_HALF_T, GEAR_HALF_R = 4.2, 2.5
CONTAINER = (6.06, 2.44, 2.59)  # ISO 20 ft, metres
REEL_R, REEL_W = 0.95, 1.1


def _reel(u: Unit, lod: int, c, axis, side):
    """A cable/rope reel on an A-frame stand: orange flanges, dark drum."""
    z = DECK_Z + 0.04 + REEL_R + 0.12
    hub = (c[0], c[1], z)
    a0, a1 = v_add(hub, v_mul(axis, -REEL_W / 2)), v_add(hub, v_mul(axis, REEL_W / 2))
    sides = 16 if lod == 0 else 6
    flange = u.mb("Topsides", "safety", TOPSIDES, "CableReels")
    for p in (a0, a1):
        member(flange, v_add(p, v_mul(axis, -0.04)), v_add(p, v_mul(axis, 0.04)), REEL_R, sides, caps=True)
    member(u.mb("Topsides", "dark", TOPSIDES, "CableReels"), a0, a1, REEL_R * 0.62, max(6, sides - 4))
    if lod == 0:
        stand = u.mb("Topsides", "steel", TOPSIDES, "CableReels")
        for p in (a0, a1):
            o = v_mul(axis, 0.09 if p is a1 else -0.09)
            top = v_add(p, o)
            for s in (-1, 1):
                foot = v_add(top, v_add(v_mul(side, s * 0.75), (0.0, 0.0, -(z - DECK_Z - 0.04))))
                bar(stand, foot, top, 0.08)


def deck_gear(u: Unit, lod: int) -> None:
    """Equipment decks cantilevered outside the free slim columns, each
    with a 20 ft container (white, or grey for the workshop), a cable reel
    and a cabinet, railed on its open sides (LOD0). LOD1 keeps the decks and
    the containers; LOD2 nothing. The 8-gon evidence variant has none."""
    n = u.sides
    if n != SIDES or lod == 2:
        return
    for idx, k in enumerate(GEAR_COLUMNS):
        c, er, et = _cantilever_deck(u, lod, k, GEAR_DECK_R, GEAR_HALF_T, GEAR_HALF_R, "GearDeck")
        top = DECK_Z + 0.04
        cl, cw, ch = CONTAINER
        # The container on the outer half, along the ring; doors face +et.
        cc = v_add(c, v_add(v_mul(er, GEAR_HALF_R - cw / 2 - 0.25), v_mul(et, -GEAR_HALF_T + cl / 2 + 0.3)))
        strip = "grey" if idx == 2 else "white"
        obox(u.mb("Topsides", strip, TOPSIDES, "Containers"), (cc[0], cc[1], top + ch / 2), et, None, cl / 2, cw / 2,
             ch / 2, skip_bottom=True)
        if lod == 1:
            continue
        doors = v_add(cc, v_mul(et, cl / 2 + 0.02))
        obox(u.mb("Topsides", "door", TOPSIDES, "Containers"), (doors[0], doors[1], top + ch / 2), et, None, 0.03,
             cw / 2 - 0.08, ch / 2 - 0.1)
        # Corner posts and the roof rim: the container's outline at a glance.
        for sl in (-1, 1):
            for sw in (-1, 1):
                p = v_add(cc, v_add(v_mul(et, sl * (cl / 2 - 0.08)), v_mul(er, sw * (cw / 2 - 0.08))))
                bar(u.mb("Topsides", "grey", TOPSIDES, "Containers"), (p[0], p[1], top), (p[0], p[1], top + ch + 0.02),
                    0.18)
        # Reel beside the container's doors, a cabinet on the inner half.
        rc = v_add(c, v_add(v_mul(er, 0.15), v_mul(et, GEAR_HALF_T - REEL_W / 2 - 0.6)))
        _reel(u, lod, rc, et, er)
        cab = v_add(c, v_add(v_mul(er, -GEAR_HALF_R + 0.7), v_mul(et, -1.2 + 1.6 * idx)))
        obox(u.mb("Topsides", "grey", TOPSIDES, "Cabinets"), (cab[0], cab[1], top + 0.85), et, None, 0.6, 0.35, 0.85,
             skip_bottom=True)
        # Rail on the open sides (the inner side meets the ring walkway).
        corner = [v_add((c[0], c[1], DECK_Z), v_add(v_mul(er, sr * GEAR_HALF_R), v_mul(et, st * GEAR_HALF_T)))
                  for sr, st in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        rail(u, "Topsides", TOPSIDES, "GearRail", corner, 2)


# --------------------------------------------------------------------------
# Cranes
# --------------------------------------------------------------------------

def crane(u: Unit, node: str, base, heading: float, lod: int) -> None:
    """Knuckle-boom offshore crane on a pedestal, boom raised, jib reaching
    CRANE_REACH from the slew axis. The node's origin is the slew axis at
    deck level (u.pivots), so Godot can slew it by rotating the node."""
    mb = u.mb(node, "crane", TOPSIDES, "Cranes")
    steel = u.mb(node, "steel", TOPSIDES, "Cranes")
    dark = u.mb(node, "dark", TOPSIDES, "Cranes")
    u.pivots[node] = base
    hd = math.radians(heading)
    fwd = (math.cos(hd), math.sin(hd), 0.0)
    side = (-math.sin(hd), math.cos(hd), 0.0)
    up = (0.0, 0.0, 1.0)
    bx, by, bz = base
    sides = 14 if lod == 0 else (8 if lod == 1 else 4)
    ped_top = (bx, by, bz + 2.6)
    member(mb, (bx, by, bz), ped_top, 0.95, sides, caps=False)
    member(mb, ped_top, v_add(ped_top, (0, 0, 0.45)), 1.3, sides, caps=True)
    house_c = v_add(ped_top, (0, 0, 1.6))
    kit.box(mb, v_add(house_c, v_mul(fwd, -0.5)), fwd, side, up, 1.8, 1.35, 1.3, M)
    pivot = v_add(house_c, v_add(v_mul(up, 1.0), v_mul(fwd, 0.7)))
    # Main boom raised 32 deg, 11.5 m; jib out at -8 deg, 8.0 m: reach ~18.4 m.
    a1 = math.radians(32.0)
    b_dir = v_add(v_mul(fwd, math.cos(a1)), v_mul(up, math.sin(a1)))
    knuckle = v_add(pivot, v_mul(b_dir, 11.5))
    a2 = math.radians(-8.0)
    j_dir = v_add(v_mul(fwd, math.cos(a2)), v_mul(up, math.sin(a2)))
    tip = v_add(knuckle, v_mul(j_dir, 8.0))
    _box_beam(mb, pivot, knuckle, side, 0.66, 0.9)
    _box_beam(mb, knuckle, tip, side, 0.5, 0.66)
    if lod == 2:
        return
    member(steel, v_add(knuckle, v_mul(side, -0.55)), v_add(knuckle, v_mul(side, 0.55)), 0.35, 8 if lod == 0 else 4,
           caps=True)
    ram0 = v_add(house_c, v_add(v_mul(fwd, 1.0), v_mul(up, -0.6)))
    ram1 = v_add(pivot, v_mul(b_dir, 5.0))
    member(dark, ram0, _lerp(ram0, ram1, 0.6), 0.22, 8 if lod == 0 else 4, caps=True)
    member(steel, _lerp(ram0, ram1, 0.6), ram1, 0.12, 8 if lod == 0 else 4)
    if lod == 0:
        r2a = v_add(pivot, v_add(v_mul(b_dir, 8.0), v_mul(up, 0.5)))
        r2b = v_add(knuckle, v_add(v_mul(j_dir, 2.4), v_mul(up, 0.45)))
        member(dark, r2a, _lerp(r2a, r2b, 0.55), 0.16, 8, caps=True)
        member(steel, _lerp(r2a, r2b, 0.55), r2b, 0.09, 8)
        # Hook block on its wire, the cab window, a work light.
        member(steel, v_add(tip, (0, 0, -0.3)), v_add(tip, (0, 0, -5.5)), 0.025, 4)
        kit.box(dark, v_add(tip, (0, 0, -6.0)), fwd, side, up, 0.25, 0.18, 0.45, M)
        cab = v_add(house_c, v_add(v_mul(side, 1.4), v_mul(fwd, 0.2)))
        kit.box(u.mb(node, "window", TOPSIDES, "Cranes"), v_add(cab, (0, 0, 0.3)), fwd, side, up, 0.7, 0.04, 0.35, M)


def _box_beam(mb, a, b, side, hw, hh):
    d = v_sub(b, a)
    ax = v_norm(d)
    s = v_norm(side)
    kit.box(mb, _lerp(a, b, 0.5), s, ax, v_norm(v_cross(s, ax)), hw, v_len(d) / 2, hh, M)


def cranes(u: Unit, lod: int) -> None:
    n = u.sides
    ka, kb = 5 * n // 12, 11 * n // 12
    if kb == n - max(1, n // 6):   # the 8-gon puts the power house there
        kb -= 1
    for node, k, out in (("CraneA", ka, 0.35), ("CraneB", kb, 0.0)):
        p = vertex(k, DECK_Z, sides=n)
        ang = vertex_angle(k, n)
        # A swings out over the side towards the service vessel; B over its zone.
        heading = math.degrees(ang) + (20.0 if out else 180.0 - 25.0)
        crane(u, node, p, heading, lod)


# --------------------------------------------------------------------------
# Net and bulkheads
# --------------------------------------------------------------------------

def _net_quad(mb, a, b, c, d, u0, u1, v0, v1):
    """A net panel with metre UVs (u along, v up) for pen_net_shader."""
    mb.face((mb.vert(a), mb.vert(b), mb.vert(c), mb.vert(d)), N, False, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])


def net(u: Unit, lod: int) -> None:
    """EcoNet sides inside the frame from the top ring to the bottom ring,
    the flat bottom on the radial beams, and the two fixed bulkheads."""
    n = u.sides
    rn = poly_r(apothem(n) - NET_INSET, n)
    corners = [_ring_point(k, rn, 0.0, n) for k in range(n)]
    zs = [NET_TOP, 0.0, NET_BOTTOM] if lod < 2 else [NET_TOP, -3.0]
    if lod == 0:
        zs = [NET_TOP, 7.0, 0.0, -4.0, -12.0, -23.0, NET_BOTTOM]
    mb = u.mb("Net", None, NET, "NetSides", mat=N)
    side = v_len(v_sub(corners[1], corners[0]))
    for k in range(n):
        a, b = corners[k], corners[(k + 1) % n]
        for z0, z1 in zip(zs[1:], zs[:-1]):
            _net_quad(mb, (a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1),
                      k * side, (k + 1) * side, z0, z1)
    if lod == 2:
        return
    bottom = u.mb("Net", None, NET, "NetBottom", mat=N)
    rings = [0.06, 0.5, 1.0] if lod == 0 else [0.06, 1.0]
    for k in range(n):
        a, b = corners[k], corners[(k + 1) % n]
        for f0, f1 in zip(rings[:-1], rings[1:]):
            p = [(a[0] * f, a[1] * f, NET_BOTTOM) for f in (f0, f1)] + [(b[0] * f, b[1] * f, NET_BOTTOM) for f in (f1, f0)]
            _net_quad(bottom, p[0], p[1], p[2], p[3], f0 * rn, f1 * rn, k * side * f0, (k + 1) * side * f0)
    walls = u.mb("Net", None, NET, "NetBulkheads", mat=N)
    for k in (0, n // 3):
        c = corners[k]
        _net_quad(walls, (c[0] * 0.05, c[1] * 0.05, NET_BOTTOM), (c[0], c[1], NET_BOTTOM), (c[0], c[1], NET_TOP),
                  (c[0] * 0.05, c[1] * 0.05, NET_TOP), 0.0, rn, NET_BOTTOM, NET_TOP)


def bulkhead(u: Unit, lod: int) -> None:
    """The sliding bulkhead (fish-handling zone): a radial steel frame from
    the centre column to the ring at vertex 2n/3 with its own net panel and
    walkway, pivoting on the unit's axis (Godot rotates the node)."""
    n = u.sides
    node = "Bulkhead" if lod == 0 else "Hull"
    net_node = "Bulkhead" if lod == 0 else "Net"
    ang = vertex_angle(2 * n // 3, n)
    er, et = _tangent_frame(ang)
    rn = poly_r(apothem(n) - NET_INSET, n)
    r0, r1 = 2.6, rn - 0.6
    sides = 10 if lod == 0 else 6
    # The sliding bulkhead goes in with the nets (yard order): group NET.
    mb = u.mb(node, "hull_yellow", NET, "Bulkhead")
    top0, top1 = v_mul(er, r0), v_mul(er, r1)
    if lod < 2:
        member(mb, (top0[0], top0[1], Z_TR), (top1[0], top1[1], Z_TR), D_SPOKE / 2, sides, seg=6.0 if lod == 0 else None)
        member(mb, (top1[0], top1[1], Z_TR), (top1[0], top1[1], NET_BOTTOM - 0.4), 0.5, sides,
               seg=6.0 if lod == 0 else None)
        member(mb, (top0[0], top0[1], NET_BOTTOM - 0.4), (top1[0], top1[1], NET_BOTTOM - 0.4), 0.5, sides)
        # Trolley on the ring rail.
        obox(mb, (top1[0] + er[0] * 1.2, top1[1] + er[1] * 1.2, Z_TR + 1.6), er, None, 1.4, 1.1, 0.7, skip_bottom=False)
    if lod == 0:
        for f in (0.33, 0.66):
            p = v_mul(er, r0 + (r1 - r0) * f)
            member(mb, (p[0], p[1], Z_TR), (p[0], p[1], NET_BOTTOM - 0.4), 0.3, 8, seg=8.0)
        _radial_walk(u, "Bulkhead", NET, "BulkheadWalk", ang, PLATFORM_HALF * 1.1, r1 - 0.2, 1.2, lod)
    if lod == 2:
        return
    nb = u.mb(net_node, None, NET, "BulkheadNet", mat=N)
    zs = [NET_TOP, 0.0, NET_BOTTOM] if lod == 1 else [NET_TOP, 7.0, 0.0, -4.0, -12.0, -23.0, NET_BOTTOM]
    for z0, z1 in zip(zs[1:], zs[:-1]):
        _net_quad(nb, (top0[0], top0[1], z0), (top1[0], top1[1], z0), (top1[0], top1[1], z1), (top0[0], top0[1], z1),
                  r0, r1, z0, z1)


# --------------------------------------------------------------------------
# Havfarm-class ship-shaped unit and mooring-line kit (F2-29, spec §5.2)
# --------------------------------------------------------------------------

HF_LENGTH = 385.0
HF_HALF_L = HF_LENGTH / 2.0      # 192.5 m (-192.5 bow stem .. +192.5 stern transom)
HF_BEAM = 59.5
HF_HALF_B = HF_BEAM / 2.0        # 29.75 m
HF_MOULDED_DEPTH = 37.75         # [O:R23]
HF_FREEBOARD = 11.0              # main deck z = +11.0 m (spec, artist est.)
HF_DRAFT = HF_MOULDED_DEPTH - HF_FREEBOARD  # 26.75 m -> keel at z = -26.75 m
HF_Z_KEEL = -HF_DRAFT            # -26.75 m
HF_Z_PONTOON_TOP = -18.5         # top of submerged side pontoons
HF_Z_GIRDER_BOT = -2.5           # bottom of upper side box girders (inside splash band)
HF_DECK_Z = HF_FREEBOARD         # +11.0 m
HF_WELLS = 6                     # six 47 x 47 m wells in a row [O:R23, O:R25]
HF_WELL_SIZE = 47.0
HF_WELL_HALF = HF_WELL_SIZE / 2.0  # 23.5 m (side hull girder width = 6.25 m each side)
HF_WELL_GAP = 2.0                # transverse well-dividing box beam width
HF_WELL_PITCH = HF_WELL_SIZE + HF_WELL_GAP  # 49.0 m centre-to-centre
HF_WELL_X0 = -146.5              # Well 0 starts at -146.5, Well 5 ends at +145.5 (292 m span)
HF_TURRET_X = -166.0             # bow turret centre on X axis
HF_TURRET_D = 14.0               # spec: Bow turret Ø14 m
HF_TURRET_R = HF_TURRET_D / 2.0
HF_SUPER_LEN = 40.0              # spec: Aft superstructure 40 x 59.5 x 18 m
HF_SUPER_W = 59.5
HF_SUPER_H = 18.0
HF_SUPER_X0 = HF_HALF_L - HF_SUPER_LEN  # +152.5 .. +192.5 m
HF_SUPER_TOP = HF_DECK_Z + HF_SUPER_H   # +29.0 m
HF_MAST_TOP = HF_SUPER_TOP + 5.5        # +34.5 m
HF_SILO_X = 148.5                # row of 8 silos forward of the aft superstructure
HF_SILO_YS = (-21.0, -15.0, -9.0, -3.0, 3.0, 9.0, 15.0, 21.0)
HF_NET_TOP = 10.2
HF_NET_WALL_BOT = -38.0
HF_NET_BOTTOM = -56.0            # spec: nets to 56 m

MOORING_FAIRLEAD_Z = -30.0
MOORING_SEABED_Z = -150.0
MOORING_REF_RADIUS = 1100.0
MOORING_CHAIN_D = 0.24
MOORING_FIBRE_D = 0.28
MOORING_ANCHOR_LEN = 6.0

MOORING_SPREADS = {
    "of1_class": {
        "lines": 8,
        "pairs": 4,
        "groups": [2, 2, 2, 2],
        "fairlead_radius_m": R,
        "fairlead_z_m": -30.0,
        "anchor_radius_m": 1100.0,
        "seabed_z_m": MOORING_SEABED_Z,
        "bearings_deg": [38.0, 52.0, 128.0, 142.0, 218.0, 232.0, 308.0, 322.0],
    },
    "havfarm_class": {
        "lines": 11,
        "pairs": 0,
        "groups": [4, 3, 4],
        "turret_x_m": HF_TURRET_X,
        "fairlead_radius_m": HF_TURRET_R,
        "fairlead_z_m": HF_Z_KEEL,
        "anchor_radius_m": 650.0,
        "seabed_z_m": MOORING_SEABED_Z,
        "bearings_deg": [128.0, 140.0, 152.0, 164.0, 174.0, 180.0, 186.0, 196.0, 208.0, 220.0, 232.0],
    },
}


def _hf_well_bounds(w: int) -> tuple[float, float, float, float]:
    """(x0, x1, y0, y1) of well w (0..5): each 47.0 x 47.0 m."""
    x0 = HF_WELL_X0 + w * HF_WELL_PITCH
    return (x0, x0 + HF_WELL_SIZE, -HF_WELL_HALF, HF_WELL_HALF)


def _hf_box_strip(mb, x0: float, x1: float, y0: float, y1: float, z0: float, z1: float,
                  steps_x: int = 1, skip_bottom: bool = False, skip_top: bool = False,
                  skip_x0: bool = False, skip_x1: bool = False) -> None:
    """Axis-aligned box segmented into `steps_x` bays along X for vertex AO."""
    for i in range(steps_x):
        xa = x0 + (x1 - x0) * i / steps_x
        xb = x0 + (x1 - x0) * (i + 1) / steps_x
        if not skip_top:
            quad(mb, (xa, y0, z1), (xb, y0, z1), (xb, y1, z1), (xa, y1, z1))
        if not skip_bottom:
            quad(mb, (xa, y1, z0), (xb, y1, z0), (xb, y0, z0), (xa, y0, z0))
        quad(mb, (xa, y0, z0), (xb, y0, z0), (xb, y0, z1), (xa, y0, z1))
        quad(mb, (xb, y1, z0), (xa, y1, z0), (xa, y1, z1), (xb, y1, z1))
    if not skip_x0:
        quad(mb, (x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1))
    if not skip_x1:
        quad(mb, (x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1))


def _hf_hull_and_wells(u: Unit, lod: int) -> None:
    """385 x 59.5 m ship-shaped steel hull with moulded depth 37.75 m,
    11 m freeboard, six 47 x 47 m square wells in a row, and Ø14 m bow turret."""
    hull_mb = u.mb("Hull", "hull_slate", RINGS, "Hull")
    well_mb = u.mb("Hull", "hull_slate", RINGS, "WellBeams")
    pont_mb = u.mb("Hull", "hull_slate", PONTOONS, "Pontoons")
    turret_mb = u.mb("Hull", "hull_slate", COLUMNS, "Turret")

    sx = 12 if lod == 0 else (4 if lod == 1 else 1)
    x_bay0 = HF_WELL_X0 - HF_WELL_GAP
    x_bay1 = HF_WELL_X0 + HF_WELLS * HF_WELL_PITCH
    for sy in (-1, 1):
        y0, y1 = (-HF_HALF_B, -HF_WELL_HALF) if sy < 0 else (HF_WELL_HALF, HF_HALF_B)
        _hf_box_strip(hull_mb, x_bay0, x_bay1, y0, y1, HF_Z_GIRDER_BOT, HF_DECK_Z,
                      steps_x=sx, skip_bottom=lod == 2, skip_x0=True, skip_x1=True)

    # 2. Seven transverse well walls/beams (forward wall, 5 inter-well dividers, aft wall)
    # forming the six 47.0 x 47.0 m square well openings.
    for k in range(HF_WELLS + 1):
        bx1 = HF_WELL_X0 + k * HF_WELL_PITCH if k < HF_WELLS else x_bay1
        bx0 = bx1 - HF_WELL_GAP
        _hf_box_strip(well_mb, bx0, bx1, -HF_WELL_HALF, HF_WELL_HALF, HF_Z_GIRDER_BOT, HF_DECK_Z,
                      steps_x=2 if lod == 0 else 1, skip_bottom=lod == 2)

    # Deck plate strips on top of the port/starboard side girders and bow/stern decks (LOD0/LOD1)
    if lod < 2:
        deck_mb = u.mb("Hull", "deck_plate", RINGS, "HullDeck")
        for sy in (-1, 1):
            y0, y1 = (-HF_HALF_B + 0.35, -HF_WELL_HALF - 0.35) if sy < 0 else (HF_WELL_HALF + 0.35, HF_HALF_B - 0.35)
            quad(deck_mb, (x_bay0, y0, HF_DECK_Z + 0.02), (x_bay1, y0, HF_DECK_Z + 0.02),
                 (x_bay1, y1, HF_DECK_Z + 0.02), (x_bay0, y1, HF_DECK_Z + 0.02))
        # Bow wave breakwater behind the turret on the forecastle
        bw_mb = u.mb("Hull", "hull_yellow", RINGS, "Breakwater")
        obox(bw_mb, (-152.5, 0.0, HF_DECK_Z + 1.1), (1, 0, 0), None, 0.45, 18.0, 1.1, skip_bottom=True)

    bow_stations = [
        (-HF_HALF_L, 0.8, 0.0, HF_DECK_Z + 2.4),
        (-186.0, 9.5, HF_Z_GIRDER_BOT, HF_DECK_Z + 1.8),
        (-174.0, 21.0, HF_Z_GIRDER_BOT, HF_DECK_Z + 1.0),
        (-160.0, 27.8, HF_Z_GIRDER_BOT, HF_DECK_Z + 0.4),
        (x_bay0, HF_HALF_B, HF_Z_GIRDER_BOT, HF_DECK_Z),
    ]
    if lod == 2:
        bow_stations = [(-HF_HALF_L, 0.8, -2.5, HF_DECK_Z + 2.0), (-170.0, 23.0, -2.5, HF_DECK_Z),
                        (x_bay0, HF_HALF_B, -2.5, HF_DECK_Z)]
    for (xa, ya, za0, za1), (xb, yb, zb0, zb1) in zip(bow_stations[:-1], bow_stations[1:]):
        quad(hull_mb, (xa, -ya, za0), (xb, -yb, zb0), (xb, -yb, zb1), (xa, -ya, za1))
        quad(hull_mb, (xb, yb, zb0), (xa, ya, za0), (xa, ya, za1), (xb, yb, zb1))
        quad(hull_mb, (xa, -ya, za1), (xb, -yb, zb1), (xb, yb, zb1), (xa, ya, za1))
        if lod < 2:
            quad(hull_mb, (xa, ya, za0), (xb, yb, zb0), (xb, -yb, zb0), (xa, -ya, za0))
    xs, ys, zs0, zs1 = bow_stations[0]
    quad(hull_mb, (xs, ys, zs0), (xs, -ys, zs0), (xs, -ys, zs1), (xs, ys, zs1))

    _hf_box_strip(hull_mb, x_bay1, HF_HALF_L, -HF_HALF_B, HF_HALF_B, HF_Z_GIRDER_BOT, HF_DECK_Z,
                  steps_x=3 if lod == 0 else 1, skip_bottom=lod == 2, skip_x0=True)

    t_sides = 24 if lod == 0 else (12 if lod == 1 else 8)
    tz_bot = HF_Z_KEEL if lod < 2 else -2.5
    member(turret_mb, (HF_TURRET_X, 0.0, tz_bot), (HF_TURRET_X, 0.0, HF_DECK_Z + 2.2),
           HF_TURRET_R, t_sides, seg=8.0 if lod == 0 else None, caps=True)
    if lod < 2:
        member(u.mb("Hull", "steel", PONTOONS, "TurretCollar"),
               (HF_TURRET_X, 0.0, HF_Z_KEEL), (HF_TURRET_X, 0.0, HF_Z_KEEL + 2.2),
               HF_TURRET_R, t_sides, caps=True)
        for sy in (-1, 1):
            y0, y1 = (-HF_HALF_B, -HF_WELL_HALF) if sy < 0 else (HF_WELL_HALF, HF_HALF_B)
            _hf_box_strip(pont_mb, -162.0, 178.0, y0, y1, HF_Z_KEEL, HF_Z_PONTOON_TOP,
                          steps_x=8 if lod == 0 else 2)
        for k in range(HF_WELLS + 1):
            bx = HF_WELL_X0 + k * HF_WELL_PITCH - 0.5 * HF_WELL_GAP
            _hf_box_strip(pont_mb, bx - 1.4, bx + 1.4, -HF_WELL_HALF, HF_WELL_HALF,
                          HF_Z_KEEL, HF_Z_KEEL + 5.0, steps_x=1, skip_x0=True, skip_x1=True)


def _hf_columns_and_bracing(u: Unit, lod: int) -> None:
    """Vertical side hull columns and underwater diagonal tubular bracing,
    plus two rail-mounted service gantry bridges above the well deck."""
    col_mb = u.mb("Columns", "hull_slate", COLUMNS, "Columns")
    brace_mb = u.mb("Bracing", "hull_slate", BRACING, "Bracing")
    gantry_mb = u.mb("Bracing", "hull_yellow", BRACING, "Gantry")

    c_sides = 14 if lod == 0 else (8 if lod == 1 else 6)
    b_sides = 10 if lod == 0 else (6 if lod == 1 else 4)

    for k in range(HF_WELLS + 1):
        cx = HF_WELL_X0 + k * HF_WELL_PITCH - 0.5 * HF_WELL_GAP
        for sy in (-1, 1):
            cy = sy * (HF_WELL_HALF + 3.125)
            if lod < 2:
                member(col_mb, (cx, cy, HF_Z_PONTOON_TOP), (cx, cy, HF_Z_GIRDER_BOT),
                       1.8, c_sides, seg=5.0 if lod == 0 else None)
            member(col_mb, (cx, sy * (HF_HALF_B - 0.25), -2.0), (cx, sy * (HF_HALF_B - 0.25), HF_DECK_Z - 0.2),
                   0.55, max(4, c_sides // 2))
        if lod < 2:
            member(col_mb, (cx, 0.0, HF_Z_KEEL + 5.0), (cx, 0.0, HF_Z_GIRDER_BOT), 1.4, c_sides)

    if lod < 2:
        for w in range(HF_WELLS):
            x0, x1, _y0, _y1 = _hf_well_bounds(w)
            xm = 0.5 * (x0 + x1)
            for sy in (-1, 1):
                cy = sy * (HF_WELL_HALF + 3.125)
                member(col_mb, (xm, cy, HF_Z_PONTOON_TOP), (xm, cy, HF_Z_GIRDER_BOT), 1.4, c_sides)
                member(brace_mb, (xm, cy, HF_Z_PONTOON_TOP + 0.5), (x0, cy, HF_Z_GIRDER_BOT - 0.5), 0.75, b_sides)
                member(brace_mb, (xm, cy, HF_Z_PONTOON_TOP + 0.5), (x1, cy, HF_Z_GIRDER_BOT - 0.5), 0.75, b_sides)

    for gx in (HF_WELL_X0 + 1 * HF_WELL_PITCH - 1.0, HF_WELL_X0 + 3 * HF_WELL_PITCH - 1.0):
        gz = HF_DECK_Z + 5.2
        for sy in (-1, 1):
            gy = sy * (HF_HALF_B - 2.2)
            bar(gantry_mb, (gx - 1.2, gy, HF_DECK_Z), (gx - 0.6, gy, gz), 0.65)
            bar(gantry_mb, (gx + 1.2, gy, HF_DECK_Z), (gx + 0.6, gy, gz), 0.65)
        bar(gantry_mb, (gx, -HF_HALF_B + 1.8, gz), (gx, HF_HALF_B - 1.8, gz), 1.2, 1.1)
        if lod == 0:
            walk = u.mb("Bracing", "grating", BRACING, "GantryWalk")
            obox(walk, (gx, 0.0, gz + 0.62), (1, 0, 0), None, 1.1, HF_HALF_B - 2.2, 0.08)


def _hf_topsides(u: Unit, lod: int) -> None:
    """Aft superstructure (40 x 59.5 x 18 m, #E9ECEA), row of 8 silos,
    safety-yellow rails (#E6BE2E), walkways, winches, landings, cranes, and edge nav lights."""
    tn = "Topsides"
    sx_c = HF_SUPER_X0 + HF_SUPER_LEN * 0.5
    paint = u.mb(tn, "white", TOPSIDES, "Deckhouse")
    trim_mb = u.mb(tn, "white", TOPSIDES, "DeckhouseTrim")
    glass_mb = u.mb(tn, "window", TOPSIDES, "DeckhouseWindows")
    roof_mb = u.mb(tn, "roof", TOPSIDES, "DeckhouseTrim")

    if lod == 2:
        obox(paint, (sx_c, 0.0, HF_DECK_Z + HF_SUPER_H * 0.5), (1, 0, 0), None,
             HF_SUPER_LEN * 0.5, HF_SUPER_W * 0.5, HF_SUPER_H * 0.5, skip_bottom=True)
    else:
        _hf_box_strip(paint, HF_SUPER_X0, HF_HALF_L, -HF_HALF_B, HF_HALF_B,
                      HF_DECK_Z, HF_DECK_Z + 7.5, steps_x=4 if lod == 0 else 1, skip_bottom=True)
        _hf_box_strip(paint, HF_SUPER_X0 + 2.0, HF_HALF_L - 4.0, -24.0, 24.0,
                      HF_DECK_Z + 7.5, HF_DECK_Z + 13.0, steps_x=3 if lod == 0 else 1, skip_bottom=True)
        _hf_box_strip(paint, HF_SUPER_X0 + 4.0, HF_SUPER_X0 + 28.0, -HF_HALF_B, HF_HALF_B,
                      HF_DECK_Z + 13.0, HF_SUPER_TOP - 0.4, steps_x=3 if lod == 0 else 1, skip_bottom=True)
        obox(trim_mb, (HF_SUPER_X0 + 16.0, 0.0, HF_SUPER_TOP - 0.2), (1, 0, 0), None,
             12.0, HF_HALF_B, 0.2, skip_bottom=True)
        obox(roof_mb, (sx_c, 0.0, HF_DECK_Z + 7.52), (1, 0, 0), None,
             HF_SUPER_LEN * 0.5 - 0.2, HF_HALF_B - 0.2, 0.04, skip_bottom=True)
        for wz, wh, wy in ((HF_DECK_Z + 9.2, 1.4, 22.0), (HF_DECK_Z + 11.4, 1.4, 22.0),
                           (HF_DECK_Z + 15.8, 1.8, HF_HALF_B - 1.2)):
            wx = (HF_SUPER_X0 + 1.96) if wz < HF_DECK_Z + 13.0 else (HF_SUPER_X0 + 3.96)
            quad(glass_mb, (wx, wy, wz), (wx, -wy, wz), (wx, -wy, wz + wh), (wx, wy, wz + wh))
            for sy in (-1, 1):
                wy_s = sy * (24.04 if wz < HF_DECK_Z + 13.0 else (HF_HALF_B + 0.04))
                if sy < 0:
                    quad(glass_mb, (wx, wy_s, wz), (wx + 18.0, wy_s, wz), (wx + 18.0, wy_s, wz + wh), (wx, wy_s, wz + wh))
                else:
                    quad(glass_mb, (wx + 18.0, wy_s, wz), (wx, wy_s, wz), (wx, wy_s, wz + wh), (wx + 18.0, wy_s, wz + wh))

    mast_mb = u.mb(tn, "steel", TOPSIDES, "Mast")
    mx, my = HF_SUPER_X0 + 16.0, 0.0
    if lod < 2:
        member(mast_mb, (mx, my, HF_SUPER_TOP), (mx, my, HF_MAST_TOP), 0.28, 8 if lod == 0 else 4, caps=True)
        bar(mast_mb, (mx, -3.2, HF_MAST_TOP - 1.6), (mx, 3.2, HF_MAST_TOP - 1.6), 0.14)
    u.markers["Beacon"] = (mx, my, HF_MAST_TOP)

    silo_mb = u.mb(tn, "silo", TOPSIDES, "Silos")
    s_sides = 16 if lod == 0 else (10 if lod == 1 else 8)
    for sy in HF_SILO_YS:
        revolve_at(silo_mb, [
            (0.45, HF_DECK_Z + 0.3),
            (SILO_R, HF_DECK_Z + 2.0),
            (SILO_R, HF_DECK_Z + 5.5),
            (SILO_R, HF_DECK_Z + SILO_H - 1.0),
            (0.28, HF_DECK_Z + SILO_H),
        ], s_sides, (HF_SILO_X, sy), close_top=True, close_bottom=True)
        if lod == 0:
            leg_mb = u.mb(tn, "steel", TOPSIDES, "SiloFittings")
            for a in range(4):
                th = TAU * (a + 0.5) / 4
                lx = HF_SILO_X + (SILO_R - 0.18) * math.cos(th)
                ly = sy + (SILO_R - 0.18) * math.sin(th)
                bar(leg_mb, (lx, ly, HF_DECK_Z), (lx, ly, HF_DECK_Z + 2.1), 0.14)
    if lod == 0:
        # Catwalk over the 8 silos, twin exhaust stacks and lifeboats on aft superstructure
        obox(u.mb(tn, "grating", TOPSIDES, "SiloDeck"),
             (HF_SILO_X, 0.0, HF_DECK_Z + SILO_H - 0.45), (1, 0, 0), None, 0.7, 22.5, 0.06)
        stack_mb = u.mb(tn, "grey", TOPSIDES, "DeckhouseTrim")
        cap_mb = u.mb(tn, "dark", TOPSIDES, "Cranes")
        for sy in (-8.5, 8.5):
            member(stack_mb, (HF_SUPER_X0 + 30.0, sy, HF_DECK_Z + 13.0),
                   (HF_SUPER_X0 + 30.0, sy, HF_SUPER_TOP - 0.2), 0.85, 12, caps=True)
            member(cap_mb, (HF_SUPER_X0 + 30.0, sy, HF_SUPER_TOP - 0.8),
                   (HF_SUPER_X0 + 30.0, sy, HF_SUPER_TOP - 0.1), 0.90, 12, caps=True)
            obox(u.mb(tn, "safety", TOPSIDES, "RescueBoat"),
                 (HF_SUPER_X0 + 22.0, sy * 3.1, HF_DECK_Z + 9.2), (1, 0, 0), None, 3.8, 1.4, 0.9)
    if lod < 2:
        obox(u.mb(tn, "grey", TOPSIDES, "BlowerHouse"), (HF_SILO_X, -25.8, HF_DECK_Z + 1.8),
             (1, 0, 0), None, 2.2, 2.4, 1.8, skip_bottom=True)
        if lod == 0:
            pipe_mb = u.mb(tn, "steel", TOPSIDES, "FeedPipes")
            for sy in (-1, 1):
                py = sy * (HF_WELL_HALF + 1.2)
                kit.path_tube(pipe_mb, [(HF_SILO_X, py, HF_DECK_Z + 0.45),
                                        (HF_WELL_X0, py, HF_DECK_Z + 0.45)], 0.16, 6, M, caps=False)


    walk_mb = u.mb(tn, "grating", TOPSIDES, "RingWalkway")
    if lod < 2:
        for sy in (-1, 1):
            wy0 = sy * (HF_WELL_HALF + 0.8)
            wy1 = sy * (HF_WELL_HALF + 3.4)
            ylo, yhi = (wy1, wy0) if sy < 0 else (wy0, wy1)
            quad(walk_mb, (HF_WELL_X0, ylo, HF_DECK_Z + 0.04), (HF_SUPER_X0, ylo, HF_DECK_Z + 0.04),
                 (HF_SUPER_X0, yhi, HF_DECK_Z + 0.04), (HF_WELL_X0, yhi, HF_DECK_Z + 0.04))
        for k in range(1, HF_WELLS):
            bx = HF_WELL_X0 + k * HF_WELL_PITCH - 0.5 * HF_WELL_GAP
            quad(walk_mb, (bx - 0.8, -HF_WELL_HALF, HF_DECK_Z + 0.04), (bx + 0.8, -HF_WELL_HALF, HF_DECK_Z + 0.04),
                 (bx + 0.8, HF_WELL_HALF, HF_DECK_Z + 0.04), (bx - 0.8, HF_WELL_HALF, HF_DECK_Z + 0.04))

    rail_mb = u.mb(tn, "hull_yellow", TOPSIDES, "Rails")
    for sy in (-1, 1):
        ry_out = sy * (HF_HALF_B - 0.25)
        bar(rail_mb, (HF_WELL_X0 - 2.0, ry_out, HF_DECK_Z + RAIL_H),
            (HF_SUPER_X0, ry_out, HF_DECK_Z + RAIL_H), 0.12)
        if lod < 2:
            bar(rail_mb, (HF_WELL_X0 - 2.0, ry_out, HF_DECK_Z + 0.55),
                (HF_SUPER_X0, ry_out, HF_DECK_Z + 0.55), 0.09)
            n_posts = 40 if lod == 0 else 14
            for i in range(n_posts + 1):
                px = (HF_WELL_X0 - 2.0) + (HF_SUPER_X0 - (HF_WELL_X0 - 2.0)) * i / n_posts
                bar(rail_mb, (px, ry_out, HF_DECK_Z), (px, ry_out, HF_DECK_Z + RAIL_H), 0.09)
    for w in range(HF_WELLS):
        wx0, wx1, wy0, wy1 = _hf_well_bounds(w)
        corners = [(wx0, wy0, HF_DECK_Z + RAIL_H), (wx1, wy0, HF_DECK_Z + RAIL_H),
                   (wx1, wy1, HF_DECK_Z + RAIL_H), (wx0, wy1, HF_DECK_Z + RAIL_H)]
        for a, b in zip(corners, corners[1:] + corners[:1]):
            bar(rail_mb, a, b, 0.10)
            if lod == 0:
                for s in range(1, 8):
                    p = _lerp(a, b, s / 8)
                    bar(rail_mb, (p[0], p[1], HF_DECK_Z), p, 0.07)

    u.markers["W2WLanding"] = (0.0, -HF_HALF_B, HF_DECK_Z)
    if lod < 2:
        obox(u.mb(tn, "grating", TOPSIDES, "W2WLanding"), (0.0, -HF_HALF_B - 1.2, HF_DECK_Z - 0.1),
             (1, 0, 0), None, 3.0, 1.2, 0.1)
        if lod == 0:
            ladder(u.mb(tn, "hull_yellow", TOPSIDES, "BoatLanding"),
                   (-12.0, -HF_HALF_B - 0.3, -2.5), (-12.0, -HF_HALF_B - 0.3, HF_DECK_Z + 0.8), width=0.6)
            for w in range(HF_WELLS):
                wx0, wx1, _wy0, _wy1 = _hf_well_bounds(w)
                wm = 0.5 * (wx0 + wx1)
                for sy in (-1, 1):
                    obox(u.mb(tn, "grey", TOPSIDES, "NetWinches"), (wm, sy * (HF_WELL_HALF + 2.2), HF_DECK_Z + 0.5),
                         (1, 0, 0), None, 0.8, 0.55, 0.5, skip_bottom=True)

    crane(u, "CraneA", (HF_WELL_X0 + 1 * HF_WELL_PITCH - 1.0, -HF_HALF_B + 3.2, HF_DECK_Z), -70.0, lod)
    crane(u, "CraneB", (HF_WELL_X0 + 4 * HF_WELL_PITCH - 1.0, HF_HALF_B - 3.2, HF_DECK_Z), 110.0, lod)

    if lod < 2:
        nav = u.mb("NavLights", "nav_light", TOPSIDES, "Lanterns")
        post = u.mb(tn, "steel", TOPSIDES, "LanternPosts")
        edge_pts = [
            (-HF_HALF_L + 1.2, 0.0, HF_DECK_Z + 2.4),
            (HF_WELL_X0, -HF_HALF_B + 0.4, HF_DECK_Z),
            (HF_WELL_X0, HF_HALF_B - 0.4, HF_DECK_Z),
            (-50.0, -HF_HALF_B + 0.4, HF_DECK_Z),
            (-50.0, HF_HALF_B - 0.4, HF_DECK_Z),
            (50.0, -HF_HALF_B + 0.4, HF_DECK_Z),
            (50.0, HF_HALF_B - 0.4, HF_DECK_Z),
            (HF_HALF_L - 0.8, -HF_HALF_B + 0.4, HF_SUPER_TOP),
            (HF_HALF_L - 0.8, HF_HALF_B - 0.4, HF_SUPER_TOP),
        ]
        for lx, ly, lz in edge_pts:
            member(post, (lx, ly, lz), (lx, ly, lz + 1.4), 0.07, 6 if lod == 0 else 4)
            revolve_at(nav, [(0.05, lz + 2.02), (0.2, lz + 1.95), (0.22, lz + 1.52), (0.12, lz + 1.4)],
                       10 if lod == 0 else 6, (lx, ly), close_top=True)
        bx, by, bz = u.markers["Beacon"]
        revolve_at(nav, [(0.05, bz + 0.6), (0.24, bz + 0.52), (0.26, bz + 0.1), (0.14, bz)],
                   10 if lod == 0 else 6, (bx, by), close_top=True)


def _hf_nets_and_bulkhead(u: Unit, lod: int) -> None:
    """Six 47 x 47 m square well nets in a row hanging to -56 m (clipped at
    -3 m at LOD2), plus a crowding bulkhead frame and net in Well 0."""
    mb = u.mb("Net", None, NET, "NetSides", mat=N)
    bottom = u.mb("Net", None, NET, "NetBottom", mat=N)
    zs = [HF_NET_TOP, 0.0, HF_NET_WALL_BOT] if lod == 1 else (
        [HF_NET_TOP, 0.0, -12.0, -25.0, HF_NET_WALL_BOT] if lod == 0 else [HF_NET_TOP, -3.0]
    )
    for w in range(HF_WELLS):
        x0, x1, y0, y1 = _hf_well_bounds(w)
        corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
        for k in range(4):
            a, b = corners[k], corners[(k + 1) % 4]
            for z_hi, z_lo in zip(zs[:-1], zs[1:]):
                _net_quad(mb, (a[0], a[1], z_hi), (b[0], b[1], z_hi),
                          (b[0], b[1], z_lo), (a[0], a[1], z_lo),
                          0.0, HF_WELL_SIZE, z_hi, z_lo)
        if lod < 2:
            cx, cy = 0.5 * (x0 + x1), 0.5 * (y0 + y1)
            in_r = 2.0
            inner = [(cx - in_r, cy - in_r), (cx + in_r, cy - in_r), (cx + in_r, cy + in_r), (cx - in_r, cy + in_r)]
            for k in range(4):
                a, b = corners[k], corners[(k + 1) % 4]
                ia, ib = inner[k], inner[(k + 1) % 4]
                _net_quad(bottom, (a[0], a[1], HF_NET_WALL_BOT), (b[0], b[1], HF_NET_WALL_BOT),
                          (ib[0], ib[1], HF_NET_BOTTOM), (ia[0], ia[1], HF_NET_BOTTOM),
                          0.0, HF_WELL_SIZE, HF_NET_WALL_BOT, HF_NET_BOTTOM)
            _net_quad(bottom, (inner[0][0], inner[0][1], HF_NET_BOTTOM), (inner[1][0], inner[1][1], HF_NET_BOTTOM),
                      (inner[2][0], inner[2][1], HF_NET_BOTTOM), (inner[3][0], inner[3][1], HF_NET_BOTTOM),
                      0.0, 2 * in_r, 0.0, 2 * in_r)

    node = "Bulkhead" if lod == 0 else "Hull"
    net_node = "Bulkhead" if lod == 0 else "Net"
    wx0, wx1, wy0, wy1 = _hf_well_bounds(0)
    bx = 0.5 * (wx0 + wx1)
    b_mb = u.mb(node, "hull_yellow", NET, "Bulkhead")
    if lod < 2:
        bar(b_mb, (bx, wy0, HF_DECK_Z), (bx, wy1, HF_DECK_Z), 0.65)
        bar(b_mb, (bx, wy0, HF_NET_WALL_BOT), (bx, wy0, HF_DECK_Z), 0.45)
        bar(b_mb, (bx, wy1, HF_NET_WALL_BOT), (bx, wy1, HF_DECK_Z), 0.45)
        bar(b_mb, (bx, wy0, HF_NET_WALL_BOT), (bx, wy1, HF_NET_WALL_BOT), 0.45)
        nb = u.mb(net_node, None, NET, "BulkheadNet", mat=N)
        _net_quad(nb, (bx, wy0, HF_NET_TOP), (bx, wy1, HF_NET_TOP),
                  (bx, wy1, HF_NET_WALL_BOT), (bx, wy0, HF_NET_WALL_BOT),
                  0.0, HF_WELL_SIZE, HF_NET_TOP, HF_NET_WALL_BOT)
    else:
        bar(b_mb, (bx, wy0, HF_DECK_Z), (bx, wy1, HF_DECK_Z), 0.65)


def havfarm_class(u: Unit, lod: int) -> None:
    """Ship-shaped offshore unit (Nordlaks Havfarm 1 'Jostein Albert' class,
    item F2-29): 385 x 59.5 m hull, moulded depth 37.75 m, freeboard 11 m,
    six 47 x 47 m wells in a row with nets to 56 m, Ø14 m bow turret,
    40 x 59.5 x 18 m aft superstructure, silo row, safety-yellow rails and
    edge nav lights."""
    _hf_hull_and_wells(u, lod)
    _hf_columns_and_bracing(u, lod)
    _hf_topsides(u, lod)
    _hf_nets_and_bulkhead(u, lod)


def _catenary_points(x0: float, z0: float, x1: float, z1: float, sag: float, steps: int) -> list[tuple[float, float, float]]:
    pts = []
    for i in range(steps + 1):
        t = i / steps
        x = x0 + (x1 - x0) * t
        z = z0 + (z1 - z0) * t - 4.0 * sag * t * (1.0 - t)
        pts.append((x, 0.0, z))
    return pts


def mooring_line_kit(u: Unit, lod: int) -> None:
    """One offshore mooring line (spec §5.2, item F2-29): upper fairlead chain
    catenary (#3A3F44), synthetic fibre mid-segment (#C8C2B0), lower ground
    chain (#3A3F44) and drag-embedment anchor (#3A3F44), spanning from the
    fairlead at (0, 0, -30 m) to the anchor at (1100 m, 0, -150 m). Instanced
    8 times in 4 pairs at 1,100 m radius for OF1-class and 11 times at 650 m
    radius from the bow turret for Havfarm-class (underwater and in the lens
    only; <= 400 / 60 triangles per line)."""
    node = "Hull"
    chain_mb = u.mb(node, "chain", PONTOONS, "Chain")
    fibre_mb = u.mb(node, "fibre", PONTOONS, "Fibre")
    anchor_mb = u.mb(node, "chain", PONTOONS, "Anchor")

    sides = 6 if lod == 0 else 3
    n_upper = 5 if lod == 0 else 1
    n_fibre = 10 if lod == 0 else 2
    n_lower = 3 if lod == 0 else 1

    upper_pts = _catenary_points(0.0, MOORING_FAIRLEAD_Z, 180.0, -84.0, 8.0, n_upper)
    kit.path_tube(chain_mb, upper_pts, MOORING_CHAIN_D * 0.5, sides, M, closed=False, caps=True)

    fibre_pts = _catenary_points(180.0, -84.0, 920.0, -146.0, 14.0, n_fibre)
    kit.path_tube(fibre_mb, fibre_pts, MOORING_FIBRE_D * 0.5, sides, M, closed=False, caps=True)

    lower_pts = _catenary_points(920.0, -146.0, MOORING_REF_RADIUS - MOORING_ANCHOR_LEN, MOORING_SEABED_Z, 1.5, n_lower)
    kit.path_tube(chain_mb, lower_pts, MOORING_CHAIN_D * 0.5, sides, M, closed=False, caps=True)

    if lod == 0:
        for jx, jz in ((180.0, -84.0), (920.0, -146.0)):
            obox(chain_mb, (jx, 0.0, jz), (1, 0, 0), None, 0.55, 0.28, 0.28)
        for bx, bz in ((340.0, -108.0), (680.0, -135.0)):
            member(fibre_mb, (bx - 0.9, 0.0, bz), (bx + 0.9, 0.0, bz), 0.42, 6, caps=True)

    ax0 = MOORING_REF_RADIUS - MOORING_ANCHOR_LEN
    obox(anchor_mb, (ax0 + 3.6, 0.0, MOORING_SEABED_Z + 0.35), (1, 0, 0), None, 2.4, 1.8, 0.35)
    bar(anchor_mb, (ax0, 0.0, MOORING_SEABED_Z + 0.25), (ax0 + 4.4, 0.0, MOORING_SEABED_Z + 1.4), 0.35, 0.45,
        sides=4 if lod == 0 else 2)
    if lod == 0:
        for sy in (-1, 1):
            obox(anchor_mb, (ax0 + 4.8, sy * 1.4, MOORING_SEABED_Z + 0.65), (1, 0, 0), None, 1.1, 0.25, 0.45)


# --------------------------------------------------------------------------
# The assets
# --------------------------------------------------------------------------

def of1_class(u: Unit, lod: int) -> None:
    """lod 0 = LOD0 (every node), 1 = LOD1 (solids merged into Hull, Net),
    2 = LOD2 (what stands above the water: Hull, Net)."""
    structure(u, lod)
    decks(u, lod)
    deckhouse(u, lod)
    silos(u, lod)
    power_house(u, lod)
    harvest_chute(u, lod)
    landings(u, lod)
    outfitting(u, lod)
    deck_gear(u, lod)
    cranes(u, lod)
    net(u, lod)
    bulkhead(u, lod)


DESIGNS = {
    "of1_class": of1_class,
    "havfarm_class": havfarm_class,
    "mooring_line_kit": mooring_line_kit,
}
