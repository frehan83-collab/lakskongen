"""Geometry of the construction kit (farm-types spec §5.3, item F2-28).

Every asset is a function design(b, lod) that fills a Build with named parts,
as in land_designs.py: a part is one MeshBuilder pointed at one trim-atlas
strip (build_land_base.STRIPS), the kit shares the land kit's single material
(LK_FacilityKit) and atlas, and the parts are joined into one mesh per LOD at
export, so a part survives as its strip and, in the sidecar, as its triangle
count and swatch.

Blender axes: X right, Y forward (glTF/Godot -Z), Z up. Origins sit on the
ground at the footprint centre; an asset's front (Godot +Z) faces Blender -Y.
Sizes are metres, from the spec's table (artist estimates where it says so).

  tower_crane    Lattice-mast tower crane: mast 50 m, jib 50 m (+X), counter
                 jib 17 m (-X), cathead with pendants, cab, trolley and hook.
                 Four-fold symmetric below the slewing unit, so yawing the
                 whole model slews the jib. The temporary saturation of a
                 construction site (#E6BE2E): it leaves with the works.
  scaffold_ring  Ø29.5 x 13 m ring scaffold around a rising Ø28 tank: it
                 clears the tank's plinth (R 14.3 m) and stands 13 m tall,
                 so the tank wall's build progress can reveal it.
  excavator      10 x 3.5 x 3.5 m tracked excavator in a low digging pose,
                 boom toward the front (-Y).
  blast_mats     A 12 x 8 m blanket of black rubber blasting mats laid on the
                 rock, a second layer crossing the first at the centre.

Licence: in-house geometry (proprietary, part of the Lakskongen repository).
"""

from __future__ import annotations

import math

import lk_meshkit as kit
from land_parts import M, Build, beam, box, grid, ring_band, ring_deck, tube, uniform  # noqa: F401
from lk_meshkit import TAU, polar

YELLOW = "crane_yellow"


def rib(b, part, strip, p0, p1, w=0.14):
    """A flat, double-faced brace between two points: a ribbon `w` wide whose
    two faces (4 triangles) stand 4 cm apart, each visible from its own side."""
    beam(b, part, strip, p0, p1, 0.04, w, sides=2)


def quad(b, part, strip, p0, p1, p2, p3):
    """One flat face; the corners run counter-clockwise seen from the front."""
    mb = b.part(part, strip)
    mb.face(tuple(mb.vert(p) for p in (p0, p1, p2, p3)), M, False)


def tri(b, part, strip, p0, p1, p2):
    """One flat triangle; the corners run counter-clockwise seen from the front."""
    mb = b.part(part, strip)
    mb.face(tuple(mb.vert(p) for p in (p0, p1, p2)), M, False)


def prism_x(b, part, strip, x0, x1, section):
    """Three faces of a triangular prism along X (6 triangles, open ends and
    open bottom as seen from above): section = (left, right, top) corners as
    (y, z); the bottom face is added too, so the prism is closed along its
    length. Faces point outward for a section given left, right, top."""
    (yl, zl), (yr, zr), (yt, zt) = section
    mb = b.part(part, strip)
    a0, a1 = mb.vert((x0, yl, zl)), mb.vert((x1, yl, zl))
    c0, c1 = mb.vert((x0, yr, zr)), mb.vert((x1, yr, zr))
    t0, t1 = mb.vert((x0, yt, zt)), mb.vert((x1, yt, zt))
    mb.face((a0, a1, t1, t0), M, False)   # left slope, normal -Y and up
    mb.face((t0, t1, c1, c0), M, False)   # right slope, normal +Y and up
    mb.face((a0, c0, c1, a1), M, False)   # underside


# --------------------------------------------------------------------------
# Tower crane
# --------------------------------------------------------------------------

MAST_H = 50.0          # top of the lattice mast (spec: mast 50 m)
MAST_HALF = 0.85       # chord centres at +-0.85 m: a 1.7 m square lattice
CHORD = 0.22
BASE_H = 0.7           # the concrete base slab
JIB_L = 50.0           # mast axis to the jib tip (spec: jib 50 m)
COUNTER_L = 17.0
JIB_Z0, JIB_Z1 = 51.0, 52.7        # jib bottom chords / top chord
JIB_HALF = 0.6
CATHEAD_TOP = 60.0
TROLLEY_X = 32.0
HOOK_TOP, HOOK_BOTTOM = 26.0, 23.9


def _crane_base(b, lod):
    if lod == 2:
        # The footprint as one flat plane.
        grid(b, uniform("Base", "concrete"), (-3.5, -3.5, BASE_H), (1, 0, 0), (0, 1, 0), [0.0, 7.0], [0.0, 7.0])
        return
    box(b, "Base", "concrete", (-3.5, -3.5, 0.0), (3.5, 3.5, BASE_H))
    if lod == 0:
        for sx in (-1, 1):
            for sy in (-1, 1):
                x, y = 2.5 * sx, 2.5 * sy
                box(b, "Ballast", "concrete", (x - 1.0, y - 1.0, BASE_H), (x + 1.0, y + 1.0, BASE_H + 1.1))


def _crane_mast(b, lod):
    hs = MAST_HALF
    if lod == 2:
        beam(b, "Mast", YELLOW, (0.0, 0.0, BASE_H), (0.0, 0.0, MAST_H), 2 * hs, sides=4)
        return
    corners = [(-hs, -hs), (hs, -hs), (hs, hs), (-hs, hs)]
    for x, y in corners:
        beam(b, "Mast", YELLOW, (x, y, BASE_H), (x, y, MAST_H), CHORD, sides=4)
    faces = [(corners[i], corners[(i + 1) % 4]) for i in range(4)]
    n = 20 if lod == 0 else 3
    dz = (MAST_H - BASE_H) / n
    if lod == 0:
        for k in range(n + 1):  # a horizontal ring at every bay joint
            z = min(BASE_H + k * dz, MAST_H - 0.07)  # the top ring stays under the 50 m line
            for (xa, ya), (xc, yc) in faces:
                rib(b, "Mast", YELLOW, (xa, ya, z), (xc, yc, z))
    for k in range(n):  # zig-zag diagonals, the faces out of step
        z0, z1 = BASE_H + k * dz, BASE_H + (k + 1) * dz
        for f, ((xa, ya), (xc, yc)) in enumerate(faces):
            if (k + f) % 2 == 0:
                rib(b, "Mast", YELLOW, (xa, ya, z0), (xc, yc, z1))
            else:
                rib(b, "Mast", YELLOW, (xc, yc, z0), (xa, ya, z1))


def _crane_head(b, lod):
    """Slewing unit, operator cab, machinery house and cathead."""
    zs = MAST_H
    box(b, "Slewing", YELLOW, (-1.3, -1.3, zs), (1.3, 1.3, zs + 0.95))
    if lod == 2:
        # The cathead as one flat triangle, seen from both sides.
        foot_l, foot_r, apex = (-0.7, 0.0, zs + 0.95), (0.7, 0.0, zs + 0.95), (0.0, 0.0, CATHEAD_TOP)
        tri(b, "Cathead", YELLOW, foot_l, foot_r, apex)
        tri(b, "Cathead", YELLOW, foot_r, foot_l, apex)
        return
    # Cab beside the slewing unit on the -Y side, glazed toward the jib.
    cx0, cx1, cy0, cy1, cz0, cz1 = -0.2, 1.9, -3.2, -1.3, zs + 0.5, zs + 2.8
    box(b, "Cab", YELLOW, (cx0, cy0, cz0), (cx1, cy1, cz1))
    quad(b, "CabGlass", "conveyor", (cx1 + 0.02, cy0 + 0.15, cz0 + 0.5), (cx1 + 0.02, cy1 - 0.15, cz0 + 0.5),
         (cx1 + 0.02, cy1 - 0.15, cz1 - 0.2), (cx1 + 0.02, cy0 + 0.15, cz1 - 0.2))
    quad(b, "CabGlass", "conveyor", (cx0 + 0.2, cy0 - 0.02, cz0 + 0.5), (cx1 - 0.2, cy0 - 0.02, cz0 + 0.5),
         (cx1 - 0.2, cy0 - 0.02, cz1 - 0.2), (cx0 + 0.2, cy0 - 0.02, cz1 - 0.2))
    # Machinery house on the counter jib.
    box(b, "Cab", YELLOW, (-6.0, -0.95, JIB_Z0), (-2.2, 0.95, JIB_Z0 + 1.9))
    # Cathead: a four-legged pyramid frame to the apex (an A-frame at LOD1).
    if lod == 0:
        for sx in (-1, 1):
            for sy in (-1, 1):
                beam(b, "Cathead", YELLOW, (0.55 * sx, 0.55 * sy, zs + 0.95), (0.0, 0.0, CATHEAD_TOP), 0.2, sides=4)
    else:
        beam(b, "Cathead", YELLOW, (0.0, 0.0, zs + 0.95), (0.0, 0.0, CATHEAD_TOP), 0.5, sides=4)


def _crane_jib(b, lod):
    hw, x0, x1 = JIB_HALF, 0.5, JIB_L
    if lod == 2:
        prism_x(b, "Jib", YELLOW, x0, x1, ((-hw, JIB_Z0), (hw, JIB_Z0), (0.0, JIB_Z1)))
        return
    for y, z in ((-hw, JIB_Z0), (hw, JIB_Z0), (0.0, JIB_Z1)):
        beam(b, "Jib", YELLOW, (x0, y, z), (x1, y, z), 0.16, sides=4)
    n = 20 if lod == 0 else 6
    dx = (x1 - x0) / n
    for s in (-1, 1):  # the two sloping sides, zig-zag between top and bottom chords
        for i in range(n):
            xa, xb = x0 + i * dx, x0 + (i + 1) * dx
            if i % 2 == 0:
                rib(b, "Jib", YELLOW, (xa, s * hw, JIB_Z0), (xb, 0.0, JIB_Z1))
            else:
                rib(b, "Jib", YELLOW, (xa, 0.0, JIB_Z1), (xb, s * hw, JIB_Z0))
    if lod == 0:
        for i in range(n + 1):  # triangular cross frames
            x = x0 + i * dx
            rib(b, "Jib", YELLOW, (x, -hw, JIB_Z0), (x, hw, JIB_Z0))
            rib(b, "Jib", YELLOW, (x, -hw, JIB_Z0), (x, 0.0, JIB_Z1))
            rib(b, "Jib", YELLOW, (x, hw, JIB_Z0), (x, 0.0, JIB_Z1))


def _crane_counter(b, lod):
    hw, x0, x1 = JIB_HALF, -0.5, -COUNTER_L
    if lod >= 1:
        prism_x(b, "CounterJib", YELLOW, x1, x0, ((-hw, JIB_Z0), (hw, JIB_Z0), (0.0, JIB_Z0 + 1.0)))
        if lod == 1:  # the ballast stack as one block
            box(b, "Ballast", "concrete", (-16.0, -0.65, JIB_Z0 + 1.08), (-13.0, 0.65, JIB_Z0 + 2.18))
        return
    z0, z1 = JIB_Z0, JIB_Z0 + 1.0
    for y in (-hw, hw):
        for z in (z0, z1):
            beam(b, "CounterJib", YELLOW, (x0, y, z), (x1, y, z), 0.16, sides=4)
    n = 8
    dx = (x1 - x0) / n
    for s in (-1, 1):
        for i in range(n):
            xa, xb = x0 + i * dx, x0 + (i + 1) * dx
            if i % 2 == 0:
                rib(b, "CounterJib", YELLOW, (xa, s * hw, z0), (xb, s * hw, z1))
            else:
                rib(b, "CounterJib", YELLOW, (xa, s * hw, z1), (xb, s * hw, z0))
    for i in range(n + 1):  # rectangular cross frames
        x = x0 + i * dx
        rib(b, "CounterJib", YELLOW, (x, -hw, z0), (x, hw, z0))
        rib(b, "CounterJib", YELLOW, (x, -hw, z1), (x, hw, z1))
        rib(b, "CounterJib", YELLOW, (x, -hw, z0), (x, -hw, z1))
        rib(b, "CounterJib", YELLOW, (x, hw, z0), (x, hw, z1))
    # Concrete counterweight blocks on top of the counter jib.
    for k in range(4):
        xa = -16.0 + k * 1.0
        box(b, "Ballast", "concrete", (xa, -0.65, z1 + 0.08), (xa + 1.0, 0.65, z1 + 1.18))


def _crane_rigging(b, lod):
    """Pendants from the cathead, the trolley, the hoist rope and the hook."""
    if lod == 2:
        return
    apex = (0.0, 0.0, CATHEAD_TOP)
    pendants = [(22.0, JIB_Z1), (44.0, JIB_Z1), (-6.0, JIB_Z0 + 1.0), (-15.0, JIB_Z0 + 1.0)]
    for x, z in pendants:
        if lod == 0:
            beam(b, "Ties", "steel", apex, (x, 0.0, z), 0.07, sides=4)
        else:
            rib(b, "Ties", "steel", apex, (x, 0.0, z), 0.12)
    tx = TROLLEY_X
    box(b, "Trolley", YELLOW, (tx - 0.9, -0.8, JIB_Z0 - 0.8), (tx + 0.9, 0.8, JIB_Z0 - 0.05), bottom=True)
    if lod == 0:
        beam(b, "Hook", "steel", (tx, 0.0, JIB_Z0 - 0.8), (tx, 0.0, HOOK_TOP), 0.05, sides=4)
        box(b, "Hook", "steel", (tx - 0.45, -0.3, HOOK_TOP - 1.2), (tx + 0.45, 0.3, HOOK_TOP), bottom=True)
        box(b, "Hook", "steel", (tx - 0.12, -0.12, HOOK_BOTTOM), (tx + 0.12, 0.12, HOOK_TOP - 1.2), bottom=True)
    else:
        rib(b, "Hook", "steel", (tx, 0.0, JIB_Z0 - 0.8), (tx, 0.0, HOOK_TOP), 0.12)
        box(b, "Hook", "steel", (tx - 0.45, -0.3, HOOK_TOP - 1.2), (tx + 0.45, 0.3, HOOK_TOP), bottom=True)
    # Aviation lights: the apex and the jib tip glow at night.
    if lod == 0:
        box(b, "Lights", "lamp", (-0.15, -0.15, CATHEAD_TOP), (0.15, 0.15, CATHEAD_TOP + 0.35), bottom=True)
        box(b, "Lights", "lamp", (JIB_L, -0.15, JIB_Z1 - 0.15), (JIB_L + 0.3, 0.15, JIB_Z1 + 0.15), bottom=True)
    else:
        box(b, "Lights", "lamp", (-0.2, -0.2, CATHEAD_TOP), (0.2, 0.2, CATHEAD_TOP + 0.4))


def tower_crane(b, lod):
    _crane_base(b, lod)
    _crane_mast(b, lod)
    _crane_head(b, lod)
    _crane_jib(b, lod)
    _crane_counter(b, lod)
    _crane_rigging(b, lod)


# --------------------------------------------------------------------------
# Scaffold ring
# --------------------------------------------------------------------------

R_STD = 14.70          # centre line of the outer standards
R_IN, R_OUT = 14.48, 14.75   # the working platform: 0.27 m, clear of the plinth (R 14.3)
SCAFFOLD_H = 13.0
# LOD1 is drawn from 350 m out, where a pixel is a quarter of a metre: its
# members are thickened (same triangles) so the cage still reads against the
# tank wall: standards 0.5 m wide, a 0.6 m top band, platforms 0.3 m wider.
LOD1_STANDARD_W = 0.5
LOD1_RAIL_H = 0.6
LOD1_BOARD_OUT = 0.3


def _vribbon(b, part, strip, r, theta, z0, z1, width):
    """A vertical two-faced ribbon at (r, theta) facing out and in (LOD1 standard)."""
    mb = b.part(part, strip)
    tx, ty = -math.sin(theta), math.cos(theta)
    cx, cy = r * math.cos(theta), r * math.sin(theta)
    h = width * 0.5
    p0 = mb.vert((cx - tx * h, cy - ty * h, z0))
    p1 = mb.vert((cx + tx * h, cy + ty * h, z0))
    p2 = mb.vert((cx + tx * h, cy + ty * h, z1))
    p3 = mb.vert((cx - tx * h, cy - ty * h, z1))
    mb.face((p0, p1, p2, p3), M, False)      # normal: tangent x up = outward
    mb.face((p1, p0, p3, p2), M, False)      # and the inward face


def scaffold_ring(b, lod):
    n = 24 if lod == 0 else 16
    lifts = [2.0 * k for k in range(1, 7)] if lod == 0 else [6.0, 12.0]
    b.dims.update(sides=n, lifts=len(lifts))
    for i in range(n):
        th = TAU * i / n
        if lod == 0:
            beam(b, "Standards", "steel", polar(R_STD, th, 0.0), polar(R_STD, th, SCAFFOLD_H), 0.10, sides=4)
        else:
            _vribbon(b, "Standards", "steel", R_STD, th, 0.0, SCAFFOLD_H, LOD1_STANDARD_W)
    for z in lifts:
        ring_deck(b, "Boards", "grating", R_IN, R_OUT + (LOD1_BOARD_OUT if lod == 1 else 0.0), z, 0.04, n,
                  outer=False, under=False)
    if lod == 0:
        for z in lifts:  # a guard rail 1 m above every platform, the top one at 13 m
            ring_band(b, "Rails", "steel", R_STD, z + 0.9, z + 1.0, n, both=True)
        # Cross bracing over two lifts on every third bay, three tiers high.
        for j in range(0, n, 3):
            a, c = TAU * j / n, TAU * (j + 1) / n
            for z0 in (0.05, 4.0, 8.0):  # the first tier starts above the ground plane (the ribbon corners dip 3 cm)
                rib(b, "Braces", "steel", polar(R_STD, a, z0), polar(R_STD, c, z0 + 4.0), 0.09)
                rib(b, "Braces", "steel", polar(R_STD, c, z0), polar(R_STD, a, z0 + 4.0), 0.09)
        # Access ladder on the seaward (-Y) side: two stiles and a rung at every lift.
        th = TAU * 0.75
        for s in (-0.25, 0.25):
            x, y = polar(R_STD - 0.12, th, 0.0)[:2]
            beam(b, "Ladder", "steel", (x + s, y, 0.0), (x + s, y, SCAFFOLD_H), 0.07, sides=4)
        for z in lifts:
            x, y = polar(R_STD - 0.12, th, 0.0)[:2]
            rib(b, "Ladder", "steel", (x - 0.25, y, z - 0.6), (x + 0.25, y, z - 0.6), 0.05)
    else:
        ring_band(b, "Rails", "steel", R_STD, SCAFFOLD_H - LOD1_RAIL_H, SCAFFOLD_H, n, both=True)


# --------------------------------------------------------------------------
# Excavator
# --------------------------------------------------------------------------

TRACK_X = 1.30
TRACK_W = 0.80
BOOM_PIVOT = (0.55, -1.4, 1.95)
KNUCKLE = (0.55, -4.9, 2.95)
STICK_END = (0.55, -7.0, 1.05)


def excavator(b, lod):
    # Tracks (rubber) with ribbed shoes on LOD0.
    for s in (-1, 1):
        xc = s * TRACK_X
        box(b, "Tracks", "fender", (xc - TRACK_W / 2, -2.1, 0.0), (xc + TRACK_W / 2, 2.1, 0.95))
        if lod == 0:
            for i in range(10):
                y = -1.9 + i * (3.8 / 9)
                box(b, "Tracks", "fender", (xc - TRACK_W / 2, y - 0.06, 0.95), (xc + TRACK_W / 2, y + 0.06, 1.03))
    if lod == 0:
        for s in (-1, 1):  # sprocket and idler at the ends, two rollers between, on the outer face
            xo = s * (TRACK_X + TRACK_W / 2)
            for y in (-1.75, -0.55, 0.55, 1.75):
                tube(b, "Wheels", "steel", [(xo, y, 0.5), (xo + s * 0.05, y, 0.5)], 0.38 if abs(y) > 1.0 else 0.26, 8, caps=True)
    box(b, "Undercarriage", "steel", (-1.2, -1.6, 0.55), (1.2, 1.6, 1.25))
    box(b, "Undercarriage", "steel", (-0.9, -0.9, 1.25), (0.9, 0.9, 1.45))
    # Upper structure: deck, engine hood, counterweight, cab.
    box(b, "House", YELLOW, (-1.5, -1.55, 1.45), (1.5, 2.05, 2.35))
    box(b, "House", YELLOW, (-0.9, 0.2, 2.35), (1.5, 2.0, 2.9))
    box(b, "House", YELLOW, (-1.45, 1.9, 1.5), (1.45, 2.2, 2.7))
    box(b, "Cab", YELLOW, (-1.5, -1.55, 2.35), (-0.15, -0.25, 3.45))
    box(b, "Cab", YELLOW, (-1.55, -1.6, 3.45), (-0.1, -0.2, 3.5))
    quad(b, "CabGlass", "conveyor", (-1.4, -1.57, 2.5), (-0.25, -1.57, 2.5), (-0.25, -1.57, 3.35), (-1.4, -1.57, 3.35))
    quad(b, "CabGlass", "conveyor", (-1.52, -1.45, 2.5), (-1.52, -0.35, 2.5), (-1.52, -0.35, 3.35), (-1.52, -1.45, 3.35))
    quad(b, "CabGlass", "conveyor", (-0.13, -0.35, 2.5), (-0.13, -1.45, 2.5), (-0.13, -1.45, 3.35), (-0.13, -0.35, 3.35))
    # Boom, stick and bucket in a low digging pose.
    beam(b, "Boom", YELLOW, BOOM_PIVOT, KNUCKLE, 0.5, 0.55, sides=4)
    beam(b, "Stick", YELLOW, KNUCKLE, STICK_END, 0.4, 0.42, sides=4)
    if lod == 0:
        # An open scoop resting on the ground: floor, back plate and two sides.
        box(b, "Bucket", "steel", (0.0, -7.75, 0.0), (1.1, -6.9, 0.22))
        box(b, "Bucket", "steel", (0.0, -7.1, 0.22), (1.1, -6.9, 1.05))
        box(b, "Bucket", "steel", (0.0, -7.75, 0.22), (0.08, -6.9, 0.9))
        box(b, "Bucket", "steel", (1.02, -7.75, 0.22), (1.1, -6.9, 0.9))
        for k in range(3):  # bucket teeth: the tip is at y = -7.8
            x = 0.12 + 0.34 * k
            box(b, "Bucket", "steel", (x, -7.8, 0.0), (x + 0.22, -7.75, 0.22))
    else:
        box(b, "Bucket", "steel", (0.0, -7.8, 0.0), (1.1, -6.9, 1.05))
    if lod == 0:
        for p0, p1 in (((0.55, -0.9, 2.2), (0.55, -3.2, 2.7)),     # boom cylinder
                       ((0.55, -3.6, 3.1), (0.55, -5.9, 2.45)),    # stick cylinder
                       ((0.55, -5.4, 2.4), (0.55, -6.9, 1.55))):   # bucket cylinder
            tube(b, "Cylinders", "steel", [p0, p1], 0.11, 8)
        beam(b, "Exhaust", "steel", (1.2, 1.2, 2.9), (1.2, 1.2, 3.35), 0.14, sides=4)
    else:
        for p0, p1 in (((0.55, -0.9, 2.2), (0.55, -3.2, 2.7)), ((0.55, -3.6, 3.1), (0.55, -5.9, 2.45))):
            rib(b, "Cylinders", "steel", p0, p1, 0.2)


# --------------------------------------------------------------------------
# Blast mats
# --------------------------------------------------------------------------

MAT_T = 0.12


def _mat(b, part, strip, cx, cy, sx, sy, z):
    box(b, part, strip, (cx - sx / 2, cy - sy / 2, z), (cx + sx / 2, cy + sy / 2, z + MAT_T))


def blast_mats(b, lod):
    rows = (-3.0, -1.0, 1.0, 3.0)
    if lod == 1:
        for y in rows:  # four long slabs and the crossing second layer as one
            _mat(b, "Mats", "fender", 0.0, y, 12.0, 2.0, 0.0)
        _mat(b, "MatsTop", "conveyor", 0.0, 0.0, 6.0, 6.0, MAT_T)
        b.dims.update(mats=5)  # what this LOD models: four slabs and the crossing block
        return
    count = 0
    for r, y in enumerate(rows):
        if r % 2 == 0:
            xs = [(-4.5, 3.0), (-1.5, 3.0), (1.5, 3.0), (4.5, 3.0)]
        else:
            xs = [(-5.25, 1.5), (-3.0, 3.0), (0.0, 3.0), (3.0, 3.0), (5.25, 1.5)]
        for x, sx in xs:
            _mat(b, "Mats", "fender", x, y, sx, 2.0, 0.0)
            count += 1
    for x in (-2.0, 0.0, 2.0):  # the second layer, rotated: 2 x 3 m mats
        for y in (-1.5, 1.5):
            _mat(b, "MatsTop", "conveyor", x, y, 2.0, 3.0, MAT_T)
            count += 1
    for y in (-2.0, 0.0, 2.0):  # steel lashings along the row seams, where the top layer leaves them bare
        for x0, x1 in ((-5.9, -3.15), (3.15, 5.9)):
            rib(b, "Lashings", "steel", (x0, y, MAT_T + 0.004), (x1, y, MAT_T + 0.004), 0.07)
    b.dims.update(mats=count)


DESIGNS = {
    "tower_crane": tower_crane,
    "scaffold_ring": scaffold_ring,
    "excavator": excavator,
    "blast_mats": blast_mats,
}
