"""Geometry of the closed-pen kit (farm-types spec §5.3, item F2-30).

Four assets, built like the construction kit (construction_designs.py): every
asset is a function design(b, lod) that fills a Build with named parts, a part
is one MeshBuilder pointed at one strip of the shared trim atlas
(build_land_base.STRIPS), and the parts are joined into one LK_FacilityKit mesh
per LOD at export. This kit appends two strips to the atlas (rows 28 and 29,
`donut_shell` and `skirt_tarp`); everything else uses the land kit's rows.

Blender axes: X right, Y forward (glTF/Godot -Z), Z up. These pens float, like
the pen kit: the origin is on the pen's axis at the design waterline (z = 0),
not on a pad. Sizes are metres, from the spec table and the research dossier
(artist estimates where it says so).

  closed_donut      Marine Donut class [O:R46]: a closed HDPE torus holding
                    22,000 m3. V = 2 pi^2 R r^2 gives R = 17, r = 8.1
                    (V = 22,017 m3), outer diameter 2 (R + r) = 50.2 m. The
                    shell stands 3 m above the water: tube centre z = -5.1,
                    shell from -13.2 to +3.0. A grating walkway rides the
                    crown with handrails, six manway hatches, four lamps.
  closed_globe      FishGLOBE class [O:R47]: a pale composite sphere of
                    3,500 m3. V = 4/3 pi r^3 gives r = 9.4 (V = 3,479 m3),
                    diameter 18.8 m, 5 m above the water: centre z = -4.4. A
                    service walkway rings the waist on brackets, with a
                    handrail; a manway hatch and a lamp mast on the top.
  semi_closed_ring  Aquatraz class [O:R44]: a steel collar of 40 m outer
                    diameter (R 20.0 out, 18.2 in, z -0.8 .. +1.4), walkway,
                    handrails, four mooring lugs; a 13 m tarpaulin skirt from
                    its underside to z = -13.0 (double-faced, ballast chain);
                    an open net cone below it down to z = -28.
  pump_module       Deepwater intake [O:R46, O:R47]: a 6 x 3 x 3 m pump house
                    (base on z = 1.4, the top of the semi-closed collar), two
                    orange pump motors, a manifold, and a 0.7 m intake hose
                    down the +X end to a bell-mouth strainer at z = -25 (the
                    spec band is 15-40 m; 25 m is the middle).

Licence: in-house geometry (proprietary, part of the Lakskongen repository).
"""

from __future__ import annotations

import math

import lk_meshkit as kit
from land_parts import M, Build, beam, box, railing, revolve, ring_deck, tube  # noqa: F401
from lk_meshkit import TAU, polar

# --------------------------------------------------------------------------
# Dimensions
# --------------------------------------------------------------------------

# closed_donut
DONUT_R, DONUT_r = 17.0, 8.1
DONUT_FREEBOARD = 3.0
DONUT_ZC = DONUT_FREEBOARD - DONUT_r          # -5.1
DONUT_WALK_HALF = 1.6                          # walkway 3.2 m wide on the crown
DONUT_DECK_Z = 3.12                            # walkway top
RAIL_H = 1.1

# closed_globe
GLOBE_r = 9.4
GLOBE_TOP = 5.0
GLOBE_ZC = GLOBE_TOP - GLOBE_r                 # -4.4
GLOBE_DECK_Z = 1.2
GLOBE_DECK_R0, GLOBE_DECK_R1 = 7.5, 11.0   # 3.5 m wide: the 3 m pump skid stands on it

# semi_closed_ring
RING_R_OUT, RING_R_IN = 20.0, 18.2
RING_Z0, RING_Z1 = -0.8, 1.4
SKIRT_R_TOP, SKIRT_R_BOT, SKIRT_Z_BOT = 18.4, 18.0, -13.0
NET_Z_BOT = -28.0

# pump_module
SKID_Z0 = 1.4
SKID_L, SKID_W, SKID_H = 6.0, 3.0, 3.0
HOSE_X, HOSE_R = 3.9, 0.35
HOSE_TOP_Z, HOSE_BOT_Z = 2.6, -24.0
STRAINER_Z = -25.0


# --------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------

def rib(b, part, strip, p0, p1, w=0.14):
    """A flat, double-faced member between two points (4 triangles)."""
    beam(b, part, strip, p0, p1, 0.04, w, sides=2)


def obox(b, part, strip, centre, theta, hx, hy, hz, bottom=False):
    """A box turned to the radial direction theta: hx along the tangent, hy
    along the radius, hz up (half sizes), centred on `centre`."""
    ax = (-math.sin(theta), math.cos(theta), 0.0)
    ay = (math.cos(theta), math.sin(theta), 0.0)
    kit.box(b.part(part, strip), centre, ax, ay, (0.0, 0.0, 1.0), hx, hy, hz, M, skip_bottom=not bottom)


def circle(r, z, n, phase=0.0):
    return [polar(r, phase + TAU * i / n, z) for i in range(n)]


# Every closed kit reserves its front (-Y in Blender, +Z in Godot) for the
# pump skid. A continuous handrail through the skid looked acceptable in a
# blockout but failed the close look-dev render, so both rails stop at this
# service sector. The open arc has the same segment and triangle counts as
# the full ring it replaces.
PUMP_SERVICE_ANGLE = 1.5 * math.pi


def service_arc(r, z, n, half_gap):
    start = PUMP_SERVICE_ANGLE + half_gap
    span = TAU - 2.0 * half_gap
    return [polar(r, start + span * i / n, z) for i in range(n + 1)]


def band_path(b, part, strip, pts, z0, z1):
    """The LOD1 two-sided rail ribbon on an open service-sector arc."""
    mb = b.part(part, strip)
    lo = [mb.vert((p[0], p[1], z0)) for p in pts]
    hi = [mb.vert((p[0], p[1], z1)) for p in pts]
    for i in range(len(pts) - 1):
        j = i + 1
        mb.face((hi[i], lo[i], lo[j], hi[j]), M, False)
        mb.face((hi[j], lo[j], lo[i], hi[i]), M, False)


def service_railing(b, r, z, segs, half_gap, lod):
    pts = service_arc(r, z, segs, half_gap)
    if lod == 0:
        railing(b, pts, RAIL_H, 0, posts_every=2, part="Rail", strip="steel", closed=False)
    else:
        band_path(b, "Rail", "steel", pts, z + RAIL_H - 0.12, z + RAIL_H)


def torus(b, part, strip, big_r, small_r, zc, n_around, n_tube):
    """A closed torus: n_tube segments round the tube (the top at j = 0, going
    down the outside), n_around round the axis; arc-length UVs; faces point
    outward. Vertices wrap, so the shading has no seam."""
    mb = b.part(part, strip)
    rows = []
    for i in range(n_around):
        th = TAU * i / n_around
        row = []
        for j in range(n_tube):
            phi = math.pi / 2 - TAU * j / n_tube
            row.append(mb.vert(polar(big_r + small_r * math.cos(phi), th, zc + small_r * math.sin(phi))))
        rows.append(row)
    r_ref = big_r + small_r
    for i in range(n_around):
        i2 = (i + 1) % n_around
        u0, u1 = r_ref * TAU * i / n_around, r_ref * TAU * (i + 1) / n_around
        for j in range(n_tube):
            j2 = (j + 1) % n_tube
            v0, v1 = small_r * TAU * j / n_tube, small_r * TAU * (j + 1) / n_tube
            mb.face((rows[i][j], rows[i][j2], rows[i2][j2], rows[i2][j]), M, True,
                    [(u0, v0), (u0, v1), (u1, v1), (u1, v0)])


def sphere(b, part, strip, r, zc, n_around, n_lat):
    """A closed sphere with single pole vertices; faces point outward."""
    mb = b.part(part, strip)
    top = mb.vert((0.0, 0.0, zc + r))
    bottom = mb.vert((0.0, 0.0, zc - r))
    rings = []
    for k in range(1, n_lat):
        phi = math.pi / 2 - math.pi * k / n_lat
        rho, z = r * math.cos(phi), zc + r * math.sin(phi)
        rings.append([mb.vert(polar(rho, TAU * i / n_around, z)) for i in range(n_around)])
    for i in range(n_around):
        i2 = (i + 1) % n_around
        mb.face((top, rings[0][i], rings[0][i2]), M, True)
        for k in range(len(rings) - 1):
            mb.face((rings[k][i], rings[k + 1][i], rings[k + 1][i2], rings[k][i2]), M, True)
        mb.face((bottom, rings[-1][i2], rings[-1][i]), M, True)



# --------------------------------------------------------------------------
# closed_donut
# --------------------------------------------------------------------------

DONUT_AROUND = (96, 48, 18)
DONUT_TUBE = (32, 16, 10)
DONUT_RAIL_SEGS = (64, 48, 0)
DONUT_HATCHES = 6
DONUT_LAMPS = 4
# The far shell has only 396 triangles including its walkway. A 3% tube
# envelope compensation (major radius reduced to keep the outer diameter)
# keeps its actual inscribed mesh volume inside the 22,000 m3 ±5% band.
DONUT_LOD2_TUBE_SCALE = 1.03


def closed_donut(b: Build, lod: int):
    tube_r = DONUT_r * (DONUT_LOD2_TUBE_SCALE if lod == 2 else 1.0)
    big_r = DONUT_R + DONUT_r - tube_r if lod == 2 else DONUT_R
    zc = DONUT_FREEBOARD - tube_r
    torus(b, "Shell", "donut_shell", big_r, tube_r, zc, DONUT_AROUND[lod], DONUT_TUBE[lod])
    h = DONUT_WALK_HALF
    n = DONUT_AROUND[lod]
    if lod == 2:
        ring_deck(b, "Walkway", "grating", big_r - h, big_r + h, DONUT_DECK_Z, 0.12, n, outer=False)
        return
    ring_deck(b, "Walkway", "grating", big_r - h, big_r + h, DONUT_DECK_Z, 0.12, n, outer=(lod == 0))
    # Six manways, none in the pump's front service sector.
    for k in range(DONUT_HATCHES):
        th = TAU * k / DONUT_HATCHES
        c = polar(big_r, th, DONUT_DECK_Z)
        obox(b, "Hatches", "tank_lid", (c[0], c[1], DONUT_DECK_Z + 0.2), th, 1.0, 1.0, 0.2)
        if lod == 0:
            obox(b, "Hatches", "tank_lid", (c[0], c[1], DONUT_DECK_Z + 0.45), th, 0.8, 0.8, 0.05)
    # Both rails stop at the pump mount instead of passing through its walls.
    segs = DONUT_RAIL_SEGS[lod]
    for rr in (big_r - h + 0.05, big_r + h - 0.05):
        service_railing(b, rr, DONUT_DECK_Z, segs, 0.22, lod)
    if lod == 0:
        for k in range(DONUT_LAMPS):
            th = TAU * (0.5, 2.5, 3.5, 5.5)[k] / DONUT_HATCHES
            c = polar(big_r + h - 0.4, th, DONUT_DECK_Z)
            obox(b, "LampPosts", "steel", (c[0], c[1], DONUT_DECK_Z + 1.3), th, 0.05, 0.05, 1.3)
            obox(b, "Lamps", "lamp", (c[0], c[1], DONUT_DECK_Z + 2.7), th, 0.14, 0.14, 0.14, bottom=True)


# --------------------------------------------------------------------------
# closed_globe
# --------------------------------------------------------------------------

GLOBE_AROUND = (64, 32, 12)
GLOBE_LAT = (32, 16, 8)
GLOBE_RAIL_SEGS = (32, 32, 0)
GLOBE_BRACKETS = (12, 6, 0)
# A 2% far-LOD envelope keeps the measured low-poly volume in the spec band;
# diameter and freeboard remain within their own ±5% limits.
GLOBE_LOD2_SCALE = 1.02


def _globe_rs(z: float) -> float:
    """Radius of the sphere at height z."""
    return math.sqrt(GLOBE_r ** 2 - (z - GLOBE_ZC) ** 2)


def closed_globe(b: Build, lod: int):
    shell_r = GLOBE_r * (GLOBE_LOD2_SCALE if lod == 2 else 1.0)
    sphere(b, "Shell", "lox", shell_r, GLOBE_ZC, GLOBE_AROUND[lod], GLOBE_LAT[lod])
    n = GLOBE_AROUND[lod]
    ring_deck(b, "Walkway", "grating", GLOBE_DECK_R0, GLOBE_DECK_R1, GLOBE_DECK_Z, 0.12, n, outer=(lod == 0))
    if lod == 2:
        return
    # Brackets from the shell below the waist out to the walkway's edge.
    nb = GLOBE_BRACKETS[lod]
    z_low = -0.8
    for k in range(nb):
        th = TAU * k / nb
        p0 = polar(_globe_rs(z_low) - 0.05, th, z_low)
        p1 = polar(GLOBE_DECK_R1 - 0.1, th, GLOBE_DECK_Z - 0.12)
        beam(b, "Brackets", "steel", p0, p1, 0.18, sides=4 if lod == 0 else 2)
    segs = GLOBE_RAIL_SEGS[lod]
    rr = GLOBE_DECK_R1 - 0.1
    service_railing(b, rr, GLOBE_DECK_Z, segs, 0.32, lod)
    # Manway hatch on the crown and a lamp mast beside it.
    box(b, "Hatches", "tank_lid", (-1.2, -1.2, GLOBE_TOP - 0.1), (1.2, 1.2, GLOBE_TOP + 0.25))
    if lod == 0:
        box(b, "Hatches", "tank_lid", (-0.9, -0.9, GLOBE_TOP + 0.25), (0.9, 0.9, GLOBE_TOP + 0.4))
        box(b, "LampPosts", "steel", (1.6, -0.05, GLOBE_TOP - 0.1), (1.7, 0.05, GLOBE_TOP + 2.4))
        box(b, "Lamps", "lamp", (1.5, -0.15, GLOBE_TOP + 2.4), (1.8, 0.15, GLOBE_TOP + 2.7), bottom=True)



# --------------------------------------------------------------------------
# semi_closed_ring
# --------------------------------------------------------------------------

RING_AROUND = (96, 48, 32)
RING_RAIL_SEGS = (64, 48, 0)
SKIRT_AROUND = (96, 48, 32)
SKIRT_STEPS = (4, 2, 1)
NET_RADIALS = (48, 16, 0)
NET_RING_ZS = (-16.0, -19.0, -22.0, -25.0)


def _skirt_profile(steps: int):
    pts = []
    for k in range(steps + 1):
        t = k / steps
        pts.append((SKIRT_R_TOP + (SKIRT_R_BOT - SKIRT_R_TOP) * t, RING_Z0 + (SKIRT_Z_BOT - RING_Z0) * t))
    return pts


def semi_closed_ring(b: Build, lod: int):
    n = RING_AROUND[lod]
    skirt = _skirt_profile(SKIRT_STEPS[lod])
    if lod == 2:
        ring_deck(b, "Collar", "steel", RING_R_IN, RING_R_OUT, RING_Z1, RING_Z1 - RING_Z0, n, outer=True)
        revolve(b, "Skirt", "skirt_tarp", skirt, SKIRT_AROUND[2], smooth=False)
        return
    # The collar: a rectangular steel section, revolved (outside, underside,
    # inside, top).
    profile = [(RING_R_OUT, RING_Z1), (RING_R_OUT, RING_Z0), (RING_R_IN, RING_Z0), (RING_R_IN, RING_Z1), (RING_R_OUT, RING_Z1)]
    revolve(b, "Collar", "steel", profile, n, smooth=False)
    ring_deck(b, "Walkway", "grating", RING_R_IN + 0.2, RING_R_OUT - 0.2, RING_Z1 + 0.04, 0.04, n, outer=False)
    # Handrails on both edges, broken at the front pump service sector.
    segs = RING_RAIL_SEGS[lod]
    for rr in (RING_R_OUT - 0.1, RING_R_IN + 0.1):
        service_railing(b, rr, RING_Z1 + 0.04, segs, 0.21, lod)
    # Four mooring lugs on the outside, 90 degrees apart.
    for k in range(4):
        th = TAU * k / 4
        c = polar(RING_R_OUT + 0.35, th, 0.3)
        obox(b, "Lugs", "steel", c, th, 0.3, 0.35, 0.3)
    # The skirt: a tarpaulin hanging from the collar's underside, double-faced
    # so it reads from outside and from the net chamber.
    # The inner face stands 4 cm inside the outer one (as `rib` does), so the
    # AO bake does not read the other face at zero distance.
    revolve(b, "Skirt", "skirt_tarp", skirt, SKIRT_AROUND[lod], smooth=False)
    revolve(b, "Skirt", "skirt_tarp", [(r - 0.04, z) for r, z in reversed(skirt)], SKIRT_AROUND[lod], smooth=False)
    if lod == 0:
        # Ballast chain along the skirt's hem.
        kit.path_tube(b.part("Ballast", "steel"), circle(SKIRT_R_BOT, SKIRT_Z_BOT, 64), 0.22, 6, M, closed=True, caps=False)
    # The open net cone below the skirt: radial ribs to the apex and rings.
    nr = NET_RADIALS[lod]
    apex = (0.0, 0.0, NET_Z_BOT)
    for k in range(nr):
        th = TAU * k / nr
        rib(b, "Net", "pipe", polar(SKIRT_R_BOT, th, SKIRT_Z_BOT), apex)
    rings = NET_RING_ZS if lod == 0 else NET_RING_ZS[1::2]
    for z in rings:
        rr = SKIRT_R_BOT * (z - NET_Z_BOT) / (SKIRT_Z_BOT - NET_Z_BOT)
        pts = circle(rr, z, nr)
        for i in range(nr):
            rib(b, "Net", "pipe", pts[i], pts[(i + 1) % nr])


# --------------------------------------------------------------------------
# pump_module
# --------------------------------------------------------------------------

LOUVRES = 8


def pump_module(b: Build, lod: int):
    x0, x1, y0, y1 = -SKID_L / 2, SKID_L / 2, -SKID_W / 2, SKID_W / 2
    z0, z1 = SKID_Z0, SKID_Z0 + SKID_H
    # A 120 mm mounting plate bridges the collar/deck and the skid. Its
    # underside sits on the support when the presenter offsets the asset's
    # housing base to that deck; it also carries the slight collar overhang.
    box(b, "Mount", "steel", (x0, y0, z0 - 0.12), (x1, y1, z0), bottom=True)
    box(b, "Housing", "steel", (x0, y0, z0), (x1, y1, z1))
    sides = 12 if lod == 0 else 8
    # Two vertical pump motors on the roof.
    for x in (-1.6, 1.6):
        if lod == 0:
            profile = [(0.5, z1 + 1.3), (0.5, z1 + 0.25), (0.62, z1 + 0.25), (0.62, z1)]
            revolve(b, "Pumps", "orange", profile, 16, close_top=True, smooth=True, centre=(x, 0.0))
        else:
            box(b, "Pumps", "orange", (x - 0.5, -0.5, z1), (x + 0.5, 0.5, z1 + 1.3))
    if lod == 0:
        # Louvres on the front wall (-Y), a door and lifting eyes on the roof.
        for k in range(LOUVRES):
            x = x0 + 0.5 + k * 0.45
            box(b, "Louvres", "steel", (x, y0 - 0.05, z0 + 1.2), (x + 0.32, y0, z0 + 2.6))
        box(b, "Door", "tank_lid", (x1 - 1.6, y0 - 0.05, z0 + 0.1), (x1 - 0.4, y0, z0 + 2.2))
        for x in (-2.6, 2.6):
            for y in (-1.2, 1.2):
                box(b, "Eyes", "orange", (x - 0.12, y - 0.12, z1), (x + 0.12, y + 0.12, z1 + 0.3))
        # Roof handrail around the pump deck.
        corners = [(x0 + 0.15, y0 + 0.15, z1), (x1 - 0.15, y0 + 0.15, z1), (x1 - 0.15, y1 - 0.15, z1), (x0 + 0.15, y1 - 0.15, z1)]
        railing(b, corners, 0.9, 0, posts_every=1, part="Rail", strip="steel", closed=True)
    # Manifold: a header along the roof into the +X end, then out to the hose.
    tube(b, "Manifold", "pipe", [(-1.6, 0.0, z1 + 0.25), (-1.6, 0.0, z1 + 0.55), (1.6, 0.0, z1 + 0.55), (1.6, 0.0, z1 + 0.25)], 0.2, sides, caps=True)
    tube(b, "Manifold", "pipe", [(1.6, 0.0, z1 + 0.55), (x1 + 0.1, 0.0, z1 + 0.55), (HOSE_X, 0.0, z1 + 0.55),
                                 (HOSE_X, 0.0, HOSE_TOP_Z)], 0.25, sides, caps=False)
    # Brackets from the housing end to the riser.
    for z in (z0 + 0.6, z0 - 0.6) if lod == 0 else (z0 + 0.6,):
        box(b, "Brackets", "steel", (x1, -0.15, z - 0.1), (HOSE_X + 0.1, 0.15, z + 0.1))
    # The intake hose, 0.7 m black HDPE, straight down to the strainer.
    steps = 8 if lod == 0 else 2
    zs = [HOSE_TOP_Z + (HOSE_BOT_Z - HOSE_TOP_Z) * k / steps for k in range(steps + 1)]
    tube(b, "Hose", "fender", [(HOSE_X, 0.0, z) for z in zs], HOSE_R, sides, caps=False)
    if lod == 0:
        for z in zs[1:-1]:
            revolve(b, "Clamps", "steel", [(HOSE_R + 0.07, z + 0.1), (HOSE_R + 0.07, z - 0.1)], 12, centre=(HOSE_X, 0.0))
    # The bell-mouth strainer on the hose end.
    profile = [(HOSE_R, HOSE_BOT_Z), (0.9, STRAINER_Z + 0.4), (0.9, STRAINER_Z)]
    revolve(b, "Strainer", "orange", profile, 16 if lod == 0 else 8, close_bottom=True, smooth=lod == 0, centre=(HOSE_X, 0.0))


DESIGNS = {
    "closed_donut": closed_donut,
    "closed_globe": closed_globe,
    "semi_closed_ring": semi_closed_ring,
    "pump_module": pump_module,
}
