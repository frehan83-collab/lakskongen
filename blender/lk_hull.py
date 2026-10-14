"""Vessel parts for the Lakskongen Blender scripts (build_vessels.py).

Deterministic generators on top of lk_meshkit: a lofted ship hull with deck
and bulwarks, deck houses with recessed window bands, knuckle-boom cranes,
masts, railings, fenders and deck gear. Units are metres; Blender axes:
X starboard, Y forward (the bow), Z up; z = 0 is the waterline. The glTF
exporter turns +Y into Godot -Z, so the bow points along Godot -Z, the way
a PathFollow3D faces.

Every generator appends into a `Parts` object: one MeshBuilder per part
name, each part drawn with a single material (one draw call per part).

Licence: part of the Lakskongen repository (proprietary); no third-party code.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Callable

import lk_meshkit as kit
from lk_meshkit import TAU, v_add, v_cross, v_mul, v_norm, v_sub


class Parts:
    """One MeshBuilder per part; each part has exactly one material."""

    def __init__(self, materials: dict[str, str], aliases: dict[str, str] | None = None):
        self.materials = dict(materials)
        self.aliases = dict(aliases or {})
        self.builders = {name: kit.MeshBuilder(drop_degenerate=True) for name in materials}

    def __getitem__(self, part: str):
        part = self.aliases.get(part, part)
        return self.builders[part], self.materials[part]

    def used(self):
        return [(n, b) for n, b in self.builders.items() if b.faces]


# --------------------------------------------------------------------------
# Hull
# --------------------------------------------------------------------------

@dataclass(frozen=True)
class HullSpec:
    """A lofted monohull. u runs 0 (stern) .. 1 (bow); l runs 0 (keel) .. 1
    (bulwark top) at each station."""

    length: float
    beam: float
    draught: float
    deck_z: Callable[[float], float]      # deck height above the waterline at u
    bulwark: float = 0.0                  # solid bulwark above the deck (0 = none)
    bulwark_t: float = 0.22
    bow_rake: float = 6.0                 # stem: deck end is this far ahead of the forefoot
    stern_rake: float = 2.0               # transom/stern cut-up at the keel
    rake_power: tuple = (1.7, 2.2)        # curvature of the stem and the stern cut-up
    pmb: tuple = (0.25, 0.62)             # parallel midbody (u0, u1)
    bow_fullness: tuple = (1.6, 2.6)      # waterplane exponent at keel, at the top (flare)
    bow_end: float = 0.0                  # half-beam fraction left at the bow end (barges)
    stern_frac: tuple = (0.35, 0.92)      # half-beam fraction at the stern (keel, top)
    stern_power: float = 2.4
    bilge_r: float = 2.0
    vee: float = 0.0                      # > 0: V bottom this tall instead of a flat bottom
    camber: float = 0.0                   # deck crown at the centreline


def _level_z(spec: HullSpec, u: float, l: float) -> float:
    zk = -spec.draught
    zt = spec.deck_z(u) + spec.bulwark
    return zk + (zt - zk) * l


def hull_point(spec: HullSpec, u: float, z: float, side: float, z_top_max: float):
    """Point on the hull skin at station u and height z, side +1 starboard."""
    zk = -spec.draught
    # The stem rakes up to the bow's top and the stern to the stern's top,
    # so both ends of the sheer sit exactly at -L/2 and +L/2.
    z_bow = spec.deck_z(1.0) + spec.bulwark
    z_aft = spec.deck_z(0.0) + spec.bulwark
    s = min(max((z - zk) / (z_bow - zk), 0.0), 1.0)
    s_aft = min(max((z - zk) / (z_aft - zk), 0.0), 1.0)
    y_fwd = spec.length / 2 - spec.bow_rake * (1.0 - s) ** spec.rake_power[0]
    y_aft = -spec.length / 2 + spec.stern_rake * (1.0 - s_aft) ** spec.rake_power[1]
    y = y_aft + u * (y_fwd - y_aft)
    u0, u1 = spec.pmb
    if u > u1:
        t = (u - u1) / (1.0 - u1)
        p = spec.bow_fullness[0] + (spec.bow_fullness[1] - spec.bow_fullness[0]) * s
        plan = spec.bow_end + (1.0 - spec.bow_end) * (1.0 - t ** p)
    elif u < u0:
        t = (u0 - u) / u0
        frac = spec.stern_frac[0] + (spec.stern_frac[1] - spec.stern_frac[0]) * s
        plan = 1.0 - (1.0 - frac) * t ** spec.stern_power
    else:
        plan = 1.0
    half = spec.beam / 2
    if spec.vee > 0.0:
        sect = min(1.0, (z - zk) / spec.vee) ** 0.8 if z > zk else 0.0
        sect = max(sect, 0.02)
    else:
        r = min(spec.bilge_r, half * 0.9)
        dz = zk + r - z
        sect = ((half - r) + math.sqrt(max(r * r - dz * dz, 0.0))) / half if dz > 0.0 else 1.0
    return (side * half * plan * sect, y, z)


def loft_hull(spec: HullSpec, stations, levels, hull, deck, bulwark_part=None, x_offset=0.0):
    """Hull skin + end caps into `hull`, deck plating into `deck` (None: no
    deck, e.g. a catamaran's demihull under its platform), bulwark rim and
    inner walls into `bulwark_part` (default: the hull part). x_offset
    moves the hull sideways (a catamaran's demihulls).

    stations: u values 0..1 (ascending). levels: l values 0..1 (ascending).
    """
    mb0, mat = hull
    mb = _Shifted(mb0, x_offset)
    dmb, dmat = (_Shifted(deck[0], x_offset), deck[1]) if deck is not None else (None, None)
    bmb, bmat = bulwark_part or hull
    bmb = _Shifted(bmb, x_offset)
    z_top_max = max(spec.deck_z(u) + spec.bulwark for u in stations)
    # Section loop per station: starboard top -> keel -> port top.
    loops = []
    for u in stations:
        stb = [hull_point(spec, u, _level_z(spec, u, l), +1.0, z_top_max) for l in reversed(levels)]
        prt = [hull_point(spec, u, _level_z(spec, u, l), -1.0, z_top_max) for l in levels]
        if spec.vee > 0.0:
            loop = stb[:-1] + [(0.0, stb[-1][1], stb[-1][2])] + prt[1:]
        else:
            loop = stb + prt
        loops.append([mb.vert(p) for p in loop])
    nk = len(loops[0])
    for i in range(len(stations) - 1):
        a, b = loops[i], loops[i + 1]
        for k in range(nk - 1):
            mb.face((a[k], a[k + 1], b[k + 1], b[k]), mat, True)
    # End caps (transom; a barge's bow end) as strips across the section.
    for loop, flip in ((loops[0], False), (loops[-1], True)):
        width = abs(mb.verts[loop[0]][0] - mb.verts[loop[-1]][0])
        if width < 0.05:
            continue
        for k in range(nk // 2):
            q = (loop[k], loop[nk - 1 - k], loop[nk - 2 - k], loop[k + 1])
            mb.face(tuple(reversed(q)) if flip else q, mat, False)
    # Deck, bulwark rim and inner walls.
    t = spec.bulwark_t if spec.bulwark > 0.0 else 0.0
    inner = []
    for u in stations:
        zt = spec.deck_z(u) + spec.bulwark
        o = hull_point(spec, u, zt, +1.0, z_top_max)
        # Inside the skin all the way down to the deck (a flared bow is
        # narrower there than at the bulwark top).
        zd = spec.deck_z(u)
        skin = min(hull_point(spec, u, zd + (zt - zd) * f, +1.0, z_top_max)[0] for f in (0.0, 0.25, 0.5, 0.75))
        xi = max(min(o[0], skin) - t - (0.03 if t > 0.0 else 0.0), 0.0)
        inner.append((o, xi, spec.deck_z(u)))
    if spec.bulwark > 0.0:
        for side in (+1.0, -1.0):
            o_row = [bmb.vert((side * o[0], o[1], o[2])) for o, _, _ in inner]
            i_row = [bmb.vert((side * xi, o[1], o[2])) for o, xi, _ in inner]
            d_row = [bmb.vert((side * xi, o[1], zd)) for o, xi, zd in inner]
            for i in range(len(stations) - 1):
                if side > 0:
                    bmb.face((o_row[i], o_row[i + 1], i_row[i + 1], i_row[i]), bmat, False)
                    bmb.face((i_row[i], i_row[i + 1], d_row[i + 1], d_row[i]), bmat, False)
                else:
                    bmb.face((o_row[i], i_row[i], i_row[i + 1], o_row[i + 1]), bmat, False)
                    bmb.face((i_row[i], d_row[i], d_row[i + 1], i_row[i + 1]), bmat, False)
        # Bulwark across the stern: rim and inner face.
        o, xi, zd = inner[0]
        y_in = o[1] + t
        q = [bmb.vert(p) for p in ((o[0], o[1], o[2]), (-o[0], o[1], o[2]), (-xi, y_in, o[2]), (xi, y_in, o[2]))]
        bmb.face(q, bmat, False)
        w = [bmb.vert(p) for p in ((xi, y_in, o[2]), (-xi, y_in, o[2]), (-xi, y_in, zd), (xi, y_in, zd))]
        bmb.face(w, bmat, False)
    if dmb is None:
        return
    rows = []
    for o, xi, zd in inner:
        y = o[1] + (t if o is inner[0][0] else 0.0)
        rows.append((dmb.vert((xi, y, zd)), dmb.vert((0.0, y, zd + spec.camber)), dmb.vert((-xi, y, zd))))
    for i in range(len(rows) - 1):
        s0, c0, p0 = rows[i]
        s1, c1, p1 = rows[i + 1]
        dmb.face((s0, s1, c1, c0), dmat, False)
        dmb.face((c0, c1, p1, p0), dmat, False)


class _Shifted:
    """A MeshBuilder view that adds x_offset to every new vertex."""

    def __init__(self, mb, dx):
        self.mb, self.dx = mb, dx

    def vert(self, co):
        return self.mb.vert((co[0] + self.dx, co[1], co[2]))

    def face(self, *a, **k):
        self.mb.face(*a, **k)

    @property
    def verts(self):
        return self.mb.verts


def hull_half_beam(spec: HullSpec, u: float, z: float) -> float:
    zt = max(spec.deck_z(v / 50.0) + spec.bulwark for v in range(51))
    return hull_point(spec, u, z, 1.0, zt)[0]


def hull_y(spec: HullSpec, u: float, z: float) -> float:
    zt = max(spec.deck_z(v / 50.0) + spec.bulwark for v in range(51))
    return hull_point(spec, u, z, 1.0, zt)[1]


def stations(n: int, dense=()) -> list[float]:
    """n stations, cosine-spaced (denser at the ends), plus extra stations
    either side of each u in `dense` (a deck step needs a near-vertical cut)."""
    out = {round(0.5 - 0.5 * math.cos(math.pi * i / (n - 1)), 6) for i in range(n)}
    for d in dense:
        out.update({round(d - 0.004, 6), round(d + 0.004, 6)})
    return sorted(out)


# --------------------------------------------------------------------------
# Deck houses
# --------------------------------------------------------------------------

def _lerp(a, b, f):
    return (a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f)


def wall(paint, glass, p00, p10, p01, p11, band=None, inset=0.06, mullion=0.0):
    """A wall quad (p00 bottom-left, p10 bottom-right, p01 top-left, p11
    top-right, seen from outside) with an optional recessed window band
    between height fractions band=(f0, f1) and mullions every `mullion` m."""
    mb, mat = paint
    if band is None or glass is None:
        mb.face((mb.vert(p00), mb.vert(p10), mb.vert(p11), mb.vert(p01)), mat, False)
        return
    f0, f1 = band
    bl, br = _lerp(p00, p01, f0), _lerp(p10, p11, f0)
    tl, tr = _lerp(p00, p01, f1), _lerp(p10, p11, f1)
    for q in ((p00, p10, br, bl), (tl, tr, p11, p01)):
        if abs(q[2][2] - q[1][2]) > 1e-4 or abs(q[3][2] - q[0][2]) > 1e-4:
            mb.face(tuple(mb.vert(p) for p in q), mat, False)
    r = v_sub(p10, p00)
    up = v_sub(p01, p00)
    n = v_norm(v_cross(r, up))
    gmb, gmat = glass
    if inset <= 0.0:
        gmb.face((gmb.vert(bl), gmb.vert(br), gmb.vert(tr), gmb.vert(tl)), gmat, False)
        return
    d = v_mul(n, -inset)
    bli, bri, tli, tri = (v_add(p, d) for p in (bl, br, tl, tr))
    gmb.face((gmb.vert(bli), gmb.vert(bri), gmb.vert(tri), gmb.vert(tli)), gmat, False)
    for q in ((bl, br, bri, bli), (tl, tli, tri, tr), (bl, bli, tli, tl), (br, tr, tri, bri)):
        mb.face(tuple(mb.vert(p) for p in q), mat, False)
    width = kit.v_len(r)
    if mullion > 0.0 and width > mullion * 1.2:
        count = int(width / mullion)
        rn = v_norm(r)
        un = v_norm(up)
        hgt = kit.v_len(v_sub(tl, bl))
        for k in range(1, count):
            f = k / count
            c = v_add(_lerp(bl, br, f), v_add(v_mul(un, hgt / 2), v_mul(n, -inset / 2)))
            kit.box(mb, c, rn, v_mul(n, -1.0), un, 0.05, inset / 2, hgt / 2, mat)


def deckhouse(paint, glass, x_half, y0, y1, z0, h, band=None, faces="fblr",
              front_rake=0.0, back_rake=0.0, inset=0.06, mullion=0.0, roof=True, taper=0.0):
    """A box deck house on z0, height h, from y0 (aft) to y1 (forward).

    front_rake > 0 leans the front wall's top forward (bridge windows);
    taper narrows the top by that many metres per side. Window band on the
    faces listed in `faces` (f front, b back, l port, r starboard)."""
    xb, xt = x_half, x_half - taper
    z1 = z0 + h
    yb0, yb1 = y0, y1
    yt0, yt1 = y0 - back_rake, y1 + front_rake
    # corners: (x, y, z)
    B = {"fl": (-xb, yb1, z0), "fr": (xb, yb1, z0), "bl": (-xb, yb0, z0), "br": (xb, yb0, z0)}
    T = {"fl": (-xt, yt1, z1), "fr": (xt, yt1, z1), "bl": (-xt, yt0, z1), "br": (xt, yt0, z1)}
    walls = {
        "f": ("fr", "fl"),   # seen from the front: left is starboard
        "b": ("bl", "br"),
        "l": ("fl", "bl"),
        "r": ("br", "fr"),
    }
    for key, (a, b) in walls.items():
        wall(paint, glass, B[a], B[b], T[a], T[b], band if key in faces else None, inset,
             mullion if key in faces else 0.0)
    if roof:
        mb, mat = paint
        mb.face((mb.vert(T["bl"]), mb.vert(T["br"]), mb.vert(T["fr"]), mb.vert(T["fl"])), mat, False)


def slab(part, x_half, y0, y1, z0, thick, skip_bottom=False):
    """A flat axis-aligned slab (roof visor, platform, hatch lid)."""
    mb, mat = part
    kit.box(mb, (0.0, (y0 + y1) / 2, z0 + thick / 2), (1, 0, 0), (0, 1, 0), (0, 0, 1),
            x_half, (y1 - y0) / 2, thick / 2, mat, skip_bottom=skip_bottom)


def block(part, center, hx, hy, hz, skip_bottom=True):
    mb, mat = part
    kit.box(mb, center, (1, 0, 0), (0, 1, 0), (0, 0, 1), hx, hy, hz, mat, skip_bottom=skip_bottom)


# --------------------------------------------------------------------------
# Deck gear
# --------------------------------------------------------------------------

def cyl(part, p0, p1, r, sides, caps=True, smooth=True):
    mb, mat = part
    kit.cylinder(mb, p0, p1, r, sides, mat, caps=caps, smooth=smooth)


def tube(part, pts, r, sides, caps=True):
    mb, mat = part
    kit.path_tube(mb, pts, r, sides, mat, caps=caps)


def torus(part, center, axis, R, r, n_around, n_sides):
    """A torus round `axis` through `center` (tyres, lifebuoys, coils)."""
    mb, mat = part
    a = v_norm(axis)
    e1 = kit.any_perp(a)
    e2 = v_cross(a, e1)
    rows = []
    for i in range(n_around):
        th = TAU * i / n_around
        radial = v_add(v_mul(e1, math.cos(th)), v_mul(e2, math.sin(th)))
        ring_c = v_add(center, v_mul(radial, R))
        row = []
        for j in range(n_sides):
            ph = TAU * j / n_sides
            off = v_add(v_mul(radial, r * math.cos(ph)), v_mul(a, r * math.sin(ph)))
            row.append(mb.vert(v_add(ring_c, off)))
        rows.append(row)
    for i in range(n_around):
        i2 = (i + 1) % n_around
        for j in range(n_sides):
            j2 = (j + 1) % n_sides
            mb.face((rows[i][j], rows[i2][j], rows[i2][j2], rows[i][j2]), mat, True)


def railing(part, pts, height, step=1.6, rails=2, r_post=0.025, r_rail=0.022, sides=4, closed=False):
    """Posts every `step` m along a deck-level polyline, with `rails`
    horizontal rails (the top one at `height`)."""
    if sides <= 0 or len(pts) < 2:
        return
    path = list(pts) + ([pts[0]] if closed else [])
    posts = []
    for a, b in zip(path[:-1], path[1:]):
        seg = kit.v_len(v_sub(b, a))
        n = max(1, int(round(seg / step)))
        for k in range(n):
            posts.append(_lerp(a, b, k / n))
    if not closed:
        posts.append(path[-1])
    for p in posts:
        cyl(part, p, v_add(p, (0, 0, height)), r_post, sides, caps=False, smooth=False)
    for k in range(rails):
        hz = height * (k + 1) / rails
        line = [v_add(p, (0, 0, hz)) for p in path]
        tube(part, line, r_rail, sides, caps=not closed)


def tyre_fenders(part, pts_side, normal, z, spacing, R=0.42, r=0.16, n_around=10, n_sides=6):
    """Tyre fenders hung flat against a hull side (normal points out)."""
    for p in pts_side:
        c = (p[0] + normal[0] * (r + 0.02), p[1], z)
        torus(part, c, normal, R, r, n_around, n_sides)


def liferaft(white, steel, center, axis_x=True, length=1.3, r=0.33, sides=10):
    a = (length / 2, 0, 0) if axis_x else (0, length / 2, 0)
    cyl(white, v_sub(center, a), v_add(center, a), r, sides)
    mb, mat = steel
    for f in (-0.35, 0.35):
        o = v_mul(a, f * 2)
        kit.box(mb, v_add(center, (o[0], o[1], -r * 0.7)), (1, 0, 0), (0, 1, 0), (0, 0, 1),
                0.05 if axis_x else r * 1.05, r * 1.05 if axis_x else 0.05, r * 0.35, mat)


def bollard_pair(part, center, along=(0, 1, 0), r=0.16, h=0.5, gap=0.55, sides=10):
    a = v_norm(along)
    for s in (-1, 1):
        c = v_add(center, v_mul(a, s * gap / 2))
        cyl(part, c, v_add(c, (0, 0, h)), r, sides)
        cyl(part, v_add(c, (0, 0, h)), v_add(c, (0, 0, h + 0.05)), r * 1.35, sides)
    mb, mat = part
    kit.box(mb, v_add(center, (0, 0, 0.04)), a, v_cross((0, 0, 1), a), (0, 0, 1),
            gap / 2 + r * 1.4, r * 1.4, 0.04, mat)


def capstan(part, center, r=0.32, h=0.7, sides=12):
    cyl(part, center, v_add(center, (0, 0, 0.12)), r * 1.5, sides)
    # Waisted drum: a short profile of revolution.
    mb, mat = part
    prof = [(r * 1.05, h), (r * 0.8, h * 0.6), (r * 0.82, h * 0.35), (r * 1.1, 0.12)]
    rows = []
    for i in range(sides):
        th = TAU * i / sides
        rows.append([mb.vert((center[0] + rr * math.cos(th), center[1] + rr * math.sin(th), center[2] + zz))
                     for rr, zz in prof])
    for i in range(sides):
        i2 = (i + 1) % sides
        for k in range(len(prof) - 1):
            mb.face((rows[i][k], rows[i][k + 1], rows[i2][k + 1], rows[i2][k]), mat, True)
    c = mb.vert((center[0], center[1], center[2] + h))
    for i in range(sides):
        mb.face((rows[(i + 1) % sides][0], c, rows[i][0]), mat, True)


def winch(steel, dark, center, axis=(1, 0, 0), width=2.0, r_drum=0.45, sides=12):
    a = v_norm(axis)
    half = v_mul(a, width / 2)
    cyl(dark, v_sub(center, half), v_add(center, half), r_drum, sides)
    for s in (-1, 1):
        c = v_add(center, v_mul(a, s * (width / 2 + 0.06)))
        cyl(steel, v_sub(c, v_mul(a, 0.06)), v_add(c, v_mul(a, 0.06)), r_drum * 1.45, sides)
    mb, mat = steel
    kit.box(mb, v_add(center, (0, 0, -r_drum * 0.9)), a, v_cross((0, 0, 1), a), (0, 0, 1),
            width / 2 + 0.2, r_drum * 1.1, 0.12, mat)


def hatch(deck, steel, center, hx, hy, coaming=0.6, lid=0.25):
    """A cargo-well hatch: coaming box with a domed lid and cleats."""
    block(deck, v_add(center, (0, 0, coaming / 2)), hx, hy, coaming / 2)
    block(steel, v_add(center, (0, 0, coaming + lid / 2)), hx * 0.96, hy * 0.96, lid / 2)
    block(steel, v_add(center, (0, 0, coaming + lid + 0.04)), hx * 0.75, hy * 0.75, 0.04)


# --------------------------------------------------------------------------
# Crane and mast
# --------------------------------------------------------------------------

def knuckle_crane(crane, steel, dark, base, heading_deg, reach=1.0, sides=10, detail=True):
    """A folded marine knuckle-boom crane on a pedestal. reach scales it
    (1.0 ~ a 10 m crane on a workboat); heading turns the boom (0 = +Y)."""
    s = reach
    hd = math.radians(heading_deg)
    fwd = (math.sin(hd), math.cos(hd), 0.0)
    side = (math.cos(hd), -math.sin(hd), 0.0)
    up = (0.0, 0.0, 1.0)
    ped_top = v_add(base, (0, 0, 1.1 * s))
    cyl(steel, base, ped_top, 0.42 * s, sides)
    cyl(crane, ped_top, v_add(ped_top, (0, 0, 0.35 * s)), 0.55 * s, sides)
    col_c = v_add(ped_top, (0, 0, 1.2 * s))
    mb, mat = crane
    kit.box(mb, col_c, side, fwd, up, 0.42 * s, 0.5 * s, 0.85 * s, mat)
    # Main boom up at ~62 deg, knuckle, jib folded back down along it.
    pivot = v_add(col_c, v_add(v_mul(up, 0.7 * s), v_mul(fwd, 0.2 * s)))
    a1 = math.radians(68.0)
    boom_dir = v_add(v_mul(fwd, math.cos(a1)), v_mul(up, math.sin(a1)))
    knuckle = v_add(pivot, v_mul(boom_dir, 4.2 * s))
    _beam(crane, pivot, knuckle, side, 0.3 * s, 0.38 * s)
    a2 = math.radians(-72.0)
    jib_dir = v_add(v_mul(fwd, math.cos(a2)), v_mul(up, math.sin(a2)))
    jib_end = v_add(knuckle, v_mul(jib_dir, 3.6 * s))
    _beam(crane, knuckle, jib_end, side, 0.25 * s, 0.3 * s)
    cyl(steel, v_add(knuckle, v_mul(side, -0.32 * s)), v_add(knuckle, v_mul(side, 0.32 * s)), 0.22 * s, sides)
    # Hydraulic rams (steel rods in dark barrels).
    ram0 = v_add(col_c, v_add(v_mul(fwd, 0.45 * s), v_mul(up, -0.3 * s)))
    ram1 = v_add(pivot, v_mul(boom_dir, 1.9 * s))
    cyl(dark, ram0, _lerp(ram0, ram1, 0.6), 0.13 * s, 8 if detail else 4)
    cyl(steel, _lerp(ram0, ram1, 0.6), ram1, 0.07 * s, 8 if detail else 4)
    if detail:
        r2a = v_add(pivot, v_add(v_mul(boom_dir, 3.0 * s), v_mul(fwd, -0.3 * s)))
        r2b = v_add(knuckle, v_add(v_mul(jib_dir, 1.2 * s), v_mul(up, 0.25 * s)))
        cyl(dark, r2a, _lerp(r2a, r2b, 0.55), 0.1 * s, 8)
        cyl(steel, _lerp(r2a, r2b, 0.55), r2b, 0.055 * s, 8)
        # Hook block hanging from the jib tip.
        hook_top = v_add(jib_end, (0, 0, -0.2 * s))
        cyl(steel, jib_end, v_add(hook_top, (0, 0, -0.9 * s)), 0.02 * s, 4, caps=False)
        hb, hm = dark
        kit.box(hb, v_add(hook_top, (0, 0, -1.1 * s)), side, fwd, up, 0.14 * s, 0.1 * s, 0.22 * s, hm)
        # Operator's control stand.
        cs = v_add(ped_top, v_add(v_mul(side, 0.9 * s), (0, 0, -0.1 * s)))
        kit.box(mb, cs, side, fwd, up, 0.18 * s, 0.25 * s, 0.4 * s, mat, skip_bottom=True)


def _beam(part, a, b, side, hw, hh):
    """A box beam from a to b; `side` is the beam's width axis."""
    mb, mat = part
    d = v_sub(b, a)
    length = kit.v_len(d)
    ax = v_norm(d)
    s = v_norm(side)
    kit.box(mb, _lerp(a, b, 0.5), s, ax, v_norm(v_cross(s, ax)), hw, length / 2, hh, mat)


def mast(white, steel, dark, base, height, radar=3.0, sides=8, detail=True):
    """A pole mast with a yard, lights, antennas and a radar scanner."""
    top = v_add(base, (0, 0, height))
    cyl(white, base, top, 0.14, sides)
    yard_z = v_add(base, (0, 0, height * 0.72))
    cyl(white, v_add(yard_z, (-1.4, 0, 0)), v_add(yard_z, (1.4, 0, 0)), 0.06, 6)
    # Radar on a platform half way up.
    if radar > 0.0:
        rz = v_add(base, (0, 0, height * 0.45))
        cyl(white, rz, v_add(rz, (0, 0, 0.25)), 0.35, sides)
        mb, mat = dark
        kit.box(mb, v_add(rz, (0, 0, 0.42)), (1, 0, 0), (0, 1, 0), (0, 0, 1), radar / 2, 0.12, 0.1, mat)
    # Masthead light and antennas.
    cyl(white, top, v_add(top, (0, 0, 0.3)), 0.11, 6)
    if detail:
        for dx in (-1.2, 1.2):
            p = v_add(yard_z, (dx, 0, 0))
            cyl(dark, p, v_add(p, (0, 0, 1.6)), 0.025, 4, caps=False)
        for dx in (-0.6, 0.6):
            p = v_add(top, (dx * 0.3, 0, 0.3))
            cyl(steel, p, v_add(p, (0, 0, 0.9)), 0.02, 4, caps=False)
