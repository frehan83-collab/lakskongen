"""Geometry of the land-based farm kit (farm-types spec §5.1, item F2-25).

Every asset is a function design(b, lod) that fills a Build with named
parts. A part is one MeshBuilder pointed at one trim-atlas strip
(build_land_base.STRIPS): the strip is the part's colour and surface, and
the kit's single material (LK_FacilityKit) reads it. Parts are joined into
one mesh per LOD at export, so a part survives as its strip (and in the
sidecar as its triangle count and swatch).

Blender axes: X right, Y forward (glTF/Godot -Z), Z up. Origins sit on the
ground (the pad top, +3.6 m) at the footprint centre unless an asset says
otherwise; an asset's front (seaward, Godot +Z) faces Blender -Y. Sizes are
metres, sourced in the spec's table (artist estimates where it says so).
The real campus is Indre Harøy-type hybrid flow-through (refs L1, L2):
pale grey tanks with a darker lower band and dark covered lids ringed by a
cantilevered walkway and handrail, long low pale process halls with roof
vents and a ribbon of windows, a dark harvest hall, an orange truss pipe
and conveyor bridge on legs from a concrete quay.

Licence: in-house geometry (proprietary, part of the Lakskongen repository).
"""

from __future__ import annotations

import math

import lk_meshkit as kit
from land_parts import (M, Build, beam, block, box, grid, railing, revolve, ring_band, ring_deck, roof_unit,  # noqa: F401
                        shed, tube, uniform)
from lk_meshkit import TAU

# --------------------------------------------------------------------------
# Tanks
# --------------------------------------------------------------------------

TANK_N = {"tank_growout": (40, 24, 12), "tank_pregrow": (28, 16, 10)}


def _tank(b, lod, d, h, n, name):
    """Shell, flat insulated lid, cantilevered rim walkway with handrail,
    ring stiffener, lid hatches and work-lights (spec §5.1). The dark lid
    keeps the full shell diameter: the walkway rings the shell outside it
    (the F2-18 read gate: a 40 px lid ellipse at the land framing)."""
    r = d * 0.5
    band = 4.0
    coping = h - 0.3
    b.dims.update(shell_diameter_m=d, shell_height_m=h, water_depth_m=8.0, band_height_m=band)
    if lod == 0:
        revolve(b, "Plinth", "concrete", [(r, 0.35), (r + 0.3, 0.35), (r + 0.3, 0.0)], n, smooth=False)
        revolve(b, "Band", "tank_band", [(r, band), (r, 1.2), (r, 0.35)], n)
        stiff = 0.62 * h
        revolve(b, "Wall", "tank_wall", [(r, coping), (r, stiff + 0.1), (r + 0.1, stiff), (r, stiff - 0.1), (r, band)], n)
    else:
        revolve(b, "Band", "tank_band", [(r, band), (r, 0.0)], n)
        revolve(b, "Wall", "tank_wall", [(r, coping), (r, band)], n)
    if lod < 2:
        # Coping ring: the shell's top edge, a little proud of the lid.
        revolve(b, "Wall", "tank_wall", [(r - 0.25, h), (r + 0.1, h), (r + 0.1, coping)], n, smooth=False)
    # Lid: shallow insulated cone from the coping to the feeder pad.
    rise = 0.022 * d
    if lod == 0:
        prof = [(1.0, h + rise), (0.35 * r, h + rise * 0.72), (0.7 * r, h + rise * 0.36), (r - 0.25, h)]
    elif lod == 1:
        prof = [(1.0, h + rise), (0.55 * r, h + rise * 0.5), (r - 0.25, h)]
    else:
        prof = [(r, h)]  # a flat fan: n triangles
    lid = b.part("Lid", "tank_lid")
    first = len(lid.faces)
    revolve(b, "Lid", "tank_lid", prof, n, close_top=True)
    # Panel joints keep their ~1.6 m spacing on every ring of the lid
    # instead of converging on the centre (rings of panels, as built).
    r_ref = max(p[0] for p in prof)
    for i in range(first, len(lid.faces)):
        uv = lid.face_uv[i]
        if uv is None:
            continue
        rm = sum(math.hypot(*lid.verts[v][:2]) for v in lid.faces[i]) / len(lid.faces[i])
        lid.face_uv[i] = tuple((u * rm / r_ref, w) for u, w in uv)
    if lod == 2:
        # The feeder as a stub on the lid (spec: "in tank LOD2").
        revolve(b, "Feeder", "feeder", [(0.3, h + 1.2), (0.8, h + 0.3)], 4, close_top=True, smooth=False)
        return
    # Walkway: 1.2 m grating cantilevered outside the shell, 0.25 m under the rim.
    deck = coping + 0.05
    ring_deck(b, "Walkway", "grating", r + 0.1, r + 1.3, deck, 0.18, n, outer=True, under=(lod == 0))
    rail_r = r + 1.25
    if lod == 0:
        kit.ring_tube(b.part("Rail", "steel"), rail_r, deck + 1.0, 0.035, n, 3, M)
        ring_band(b, "Rail", "steel", rail_r, deck + 0.47, deck + 0.53, n, both=True)
        for i in range(0, n, 2):
            th = TAU * i / n
            beam(b, "Rail", "steel", kit.polar(rail_r, th, deck), kit.polar(rail_r, th, deck + 1.0), 0.05, sides=4)
        ring_band(b, "Rail", "steel", rail_r, deck, deck + 0.12, n, both=False)  # toe board
    else:
        ring_band(b, "Rail", "steel", rail_r, deck + 0.9, deck + 1.0, n, both=True)
    # Lid hatches (one each side) and the radial footbridge to the feeder.
    for k, th in enumerate((0.55, 0.55 + math.pi)):
        cx, cy = 0.52 * r * math.cos(th), 0.52 * r * math.sin(th)
        z = h + rise * 0.48
        if lod == 0:
            box(b, "Hatch", "steel", (cx - 0.8, cy - 0.55, z), (cx + 0.8, cy + 0.55, z + 0.22))
        else:
            grid(b, uniform("Hatch", "steel"), (cx - 0.8, cy - 0.55, z + 0.1), (1, 0, 0), (0, 1, 0), [0.0, 1.6], [0.0, 1.1])
    if lod == 0:
        w = 0.5
        z0, z1 = h + rise + 0.05, h + 0.1
        mb = b.part("Walkway", "grating")
        p = [mb.vert(v) for v in ((1.0, -w, z0), (r - 0.25, -w, z1), (r - 0.25, w, z1), (1.0, w, z0))]
        mb.face((p[0], p[1], p[2], p[3]), M, False)
        for s in (-w, w):
            beam(b, "Rail", "steel", (1.0, s, z0 + 1.0), (r - 0.25, s, z1 + 1.0), 0.04, sides=2)
            beam(b, "Rail", "steel", (r * 0.5, s, (z0 + z1) * 0.5), (r * 0.5, s, (z0 + z1) * 0.5 + 1.0), 0.04, sides=4)
    # Work-lights on four rail posts (they light at dusk and night).
    for k in range(4):
        th = TAU * (k + 0.125) / 4
        lx, ly = kit.polar(rail_r, th, 0)[:2]
        if lod == 0:
            box(b, "Lamp", "lamp", (lx - 0.18, ly - 0.18, deck + 1.05), (lx + 0.18, ly + 0.18, deck + 1.3))
        else:
            grid(b, uniform("Lamp", "lamp"), (lx - 0.25, ly - 0.25, deck + 1.2), (1, 0, 0), (0, 1, 0), [0.0, 0.5], [0.0, 0.5])
    # Caged ladder from the ground to the walkway (on -X).
    lr = r + 0.7
    for s in (-0.35, 0.35):
        beam(b, "Ladder", "steel", (-lr, s, 0.35), (-lr, s, deck + 1.0), 0.07, sides=4 if lod == 0 else 2)
    if lod == 0:
        for z in (2.6, 5.0, 7.4, 9.8, 12.2):
            if z < deck:
                beam(b, "Ladder", "steel", (-lr, -0.38, z), (-lr - 0.75, -0.38, z), 0.05, sides=2)
                beam(b, "Ladder", "steel", (-lr - 0.75, -0.38, z), (-lr - 0.75, 0.38, z), 0.05, sides=2)
                beam(b, "Ladder", "steel", (-lr - 0.75, 0.38, z), (-lr, 0.38, z), 0.05, sides=2)
    # Inlet and outlet pipes into the ground on +Y (towards the lot's hall).
    sides = 10 if lod == 0 else 4
    pr = 0.45 if d > 20 else 0.3
    tube(b, "Pipe", "pipe", [(0.0, r - 0.05, 1.4), (0.0, r + 2.2, 1.4), (0.0, r + 3.0, 0.9), (0.0, r + 3.0, -0.5)], pr, sides)
    if lod == 0:
        tube(b, "Pipe", "pipe", [(2.5, r - 0.6, 0.6), (2.5, r + 2.0, 0.6), (2.5, r + 2.0, -0.4)], 0.25, 8)
        box(b, "Pipe", "pipe", (-0.5, r + 1.2, 0.85), (0.5, r + 1.7, 1.95))


def tank_growout(b, lod):
    _tank(b, lod, 28.0, 13.0, TANK_N["tank_growout"][lod], "tank_growout")


def tank_pregrow(b, lod):
    _tank(b, lod, 16.0, 9.0, TANK_N["tank_pregrow"][lod], "tank_pregrow")


def tank_feeder(b, lod):
    """Rotor feeder at the lid centre: hopper, motor, the spinning spreader
    below (its own 'rotor' strip, so the shader can spin it), feed hose."""
    n = 12 if lod == 0 else 8
    b.dims.update(feeder_diameter_m=1.6, feeder_height_m=1.2)
    revolve(b, "Feeder", "feeder", [(0.8, 0.75), (0.8, 0.3)], n)
    revolve(b, "Feeder", "feeder", [(0.3, 1.2), (0.8, 0.75)], n, close_top=True)
    box(b, "Feeder", "feeder", (-0.2, 0.55, 0.85), (0.2, 0.95, 1.1))
    if lod == 0:
        for k in range(4):
            th = TAU * (k + 0.5) / 4
            x, y = 0.6 * math.cos(th), 0.6 * math.sin(th)
            beam(b, "Feeder", "feeder", (x, y, 0.0), (x, y, 0.32), 0.07, sides=4)
        revolve(b, "Rotor", "rotor", [(0.45, 0.22), (0.45, 0.18)], 8, close_top=True, smooth=False)
        for k in range(3):
            th = TAU * k / 3
            beam(b, "Rotor", "rotor", (0.0, 0.0, 0.17), (0.42 * math.cos(th), 0.42 * math.sin(th), 0.17), 0.06, 0.08, sides=4)
        tube(b, "Hose", "pipe", [(0.75, 0.0, 0.9), (1.6, 0.0, 0.9), (3.0, 0.0, 0.5)], 0.08, 6)
    b.dims["feeder_diameter_m"] = 1.6


# --------------------------------------------------------------------------
# Halls
# --------------------------------------------------------------------------

def process_hall_bay(b, lod):
    """One 30 x 20 x 10 m bay of the process hall between the tank rows:
    pale sandwich cladding, a ribbon of windows on both long sides, a low
    duo-pitch roof with two vents; bays line up end to end along X."""
    x0, x1, y0, y1 = -15.0, 15.0, -10.0, 10.0
    eave, ridge = 9.2, 10.0
    win = (6.4, 7.9, 6.0, 4.8)
    shed(b, x0, x1, y0, y1, eave, ridge, lod, window=win, step=3.0,
         doors_front=((1.5, 2.6, 2.3),), doors_ends=((7.0, 11.0, 4.5),))
    if lod < 2:
        for x in (-7.5, 7.5):
            roof_unit(b, x, 0.0, ridge - 0.1, 2.2, 2.2, 1.3, lod)
    if lod == 0:
        for x in (-14.4, 14.4):
            beam(b, "Trim", "steel", (x, y0 - 0.45, 0.2), (x, y0 - 0.45, eave - 0.2), 0.14, sides=4)
            beam(b, "Trim", "steel", (x, y1 + 0.45, 0.2), (x, y1 + 0.45, eave - 0.2), 0.14, sides=4)
    b.dims.update(length_m=x1 - x0, width_m=y1 - y0, height_m=ridge)


def harvest_hall(b, lod):
    """The big dark harvest and processing hall (120 x 60 x 15): dark
    cladding with a pale fascia band, a blank logo panel, loading doors on
    the quay side, a pale dispatch annex with truck docks on the land side,
    plant on the roof and an office corner with windows."""
    x0, x1, y0, y1, h = -60.0, 60.0, -30.0, 30.0, 15.0
    doors = ((20.0, 25.0, 6.0), (40.0, 45.0, 6.0), (90.0, 95.0, 6.0))
    block(b, x0, x1, y0, y1, h, lod, wall=("Walls", "harvest_clad"), roof=("HallRoof", "harvest_clad"),
          step=6.0, rows=(0.6, 3.0, 7.5),
          window=(9.0, 11.0, 4.0, 3.0) if lod < 2 else None, doors_front=doors,
          fascia=(13.4, "Fascia", "hall_clad"), window_sides=("front", "left"))
    if lod < 2:
        # Blank logo panel on the seaward wall (no names in the game, D17).
        box(b, "Logo", "panel_blank", (-52.0, y0 - 0.25, 8.5), (-34.0, y0 + 0.05, 12.0), bottom=True)
        # Dispatch annex on the land side: pale, 60 x 14 x 8, six docks.
        docks = tuple((4.0 + 9.0 * k, 7.5 + 9.0 * k, 4.0) for k in range(6))
        block(b, -40.0, 20.0, y1, y1 + 14.0, 8.0, lod, wall=("Annex", "hall_clad"), roof=("Roof", "hall_roof"),
              step=5.0, rows=(0.6, 2.4), doors_back=docks, parapet=0.6)
        for k, x in enumerate((-40.0, -20.0, 0.0, 20.0, 40.0)):
            roof_unit(b, x, -12.0 + 6.0 * (k % 2), h - 1.0, 5.0, 3.0, 1.8, lod)
        if lod == 0:
            for x in (-48.0, -8.0, 28.0, 52.0):
                tube(b, "Roofgear", "steel", [(x, 18.0, h - 1.0), (x, 18.0, h + 3.0)], 0.35, 8, caps=True)
            # Stair tower at the office corner.
            box(b, "Tower", "harvest_clad", (x0 - 5.0, y0 + 4.0, 0.0), (x0, y0 + 10.0, h + 2.0))
            box(b, "TowerGlass", "window", (x0 - 5.05, y0 + 5.0, 2.0), (x0 - 4.95, y0 + 9.0, h))
    else:
        box(b, "Annex", "hall_clad", (-40.0, y1, 0.0), (20.0, y1 + 14.0, 8.0))
    b.dims.update(length_m=x1 - x0, width_m=y1 - y0, height_m=h)


# --------------------------------------------------------------------------
# Pipe bridge
# --------------------------------------------------------------------------

def pipe_bridge_span(b, lod):
    """A 25 m truss span, 3.5 x 3.5 m square section, carrying two Ø0.9 m
    pipes and a covered conveyor, with its leg frame at the -X joint. The
    origin is the deck underside at mid span; the legs reach 10 m down (the
    reference height) and sit on the 'bridge_leg' strip, so the shader can
    stretch them to the 6-14 m the terrain needs (INSTANCE_CUSTOM.b)."""
    L, S = 25.0, 3.5
    w = 0.32 if lod == 0 else 0.4
    x0, x1 = -L / 2, L / 2
    # Chord centrelines sit half a member inside, so the section is 3.5 m
    # outside to outside.
    y0, y1 = -S / 2 + w / 2, S / 2 - w / 2
    b.dims.update(span_m=L, section_m=S, leg_height_m=10.0)
    if lod == 2:
        box(b, "Truss", "orange", (x0, -S / 2, 0.0), (x1, S / 2, S), bottom=True)
        box(b, "Legs", "bridge_leg", (x0 - 0.3, -S / 2, -10.0), (x0 + 0.3, S / 2, 0.0))
        return
    sides = 4 if lod == 0 else 2
    for y in (y0, y1):
        for z in (w / 2, S - w / 2):
            beam(b, "Truss", "orange", (x0, y, z), (x1, y, z), w, sides=4)
    zl, zh = w / 2, S - w / 2  # chord centrelines
    panels = 8 if lod == 0 else 4
    for k in range(panels + 1):
        x = x0 + L * k / panels
        for y in (y0, y1):
            beam(b, "Truss", "orange", (x, y, 0.0), (x, y, S), w * 0.75, sides=sides)
        beam(b, "Truss", "orange", (x, y0, zh), (x, y1, zh), w * 0.75, sides=sides)
        if lod == 0:
            beam(b, "Truss", "orange", (x, y0, zl), (x, y1, zl), w * 0.75, sides=sides)
    for k in range(panels):
        xa, xb = x0 + L * k / panels, x0 + L * (k + 1) / panels
        flip = k % 2 == 0
        for y in (y0, y1):
            beam(b, "Truss", "orange", (xa, y, zl if flip else zh), (xb, y, zh if flip else zl), w * 0.6, sides=sides)
        if lod == 0:
            beam(b, "Truss", "orange", (xa, y0, zh), (xb, y1, zh), w * 0.5, sides=2)
    # Two process pipes low in the section, the covered conveyor above them.
    n = 12 if lod == 0 else 6
    for y in (-0.85, 0.85):
        tube(b, "Pipes", "pipe", [(x0, y, 0.75), (x1, y, 0.75)], 0.45, n)
    box(b, "Conveyor", "conveyor", (x0, -0.7, 1.75), (x1, 0.7, 2.55), bottom=(lod == 0))
    if lod == 0:
        # Maintenance walkway on the bottom chords with a handrail.
        mb = b.part("Walkway", "grating")
        p = [mb.vert(v) for v in ((x0, y0 + 0.2, 0.18), (x1, y0 + 0.2, 0.18), (x1, y0 + 0.75, 0.18), (x0, y0 + 0.75, 0.18))]
        mb.face((p[0], p[1], p[2], p[3]), M, False)
    # Leg frame at the -X joint: two columns, bracing, footings.
    lw = 0.55 if lod == 0 else 0.6
    for y in (y0 + 0.1, y1 - 0.1):
        beam(b, "Legs", "bridge_leg", (x0, y, -10.0), (x0, y, 0.0), lw, sides=4)
        if lod == 0:
            box(b, "Footings", "concrete", (x0 - 0.8, y - 0.8, -10.0), (x0 + 0.8, y + 0.8, -9.4))
    if lod == 0:
        for z0, z1 in ((-9.0, -5.0), (-5.0, -1.0)):
            beam(b, "Legs", "bridge_leg", (x0, y0 + 0.1, z0), (x0, y1 - 0.1, z1), 0.22, sides=4)
            beam(b, "Legs", "bridge_leg", (x0, y1 - 0.1, z0), (x0, y0 + 0.1, z1), 0.22, sides=4)
        beam(b, "Legs", "bridge_leg", (x0, y0 - 0.4, -0.3), (x0, y1 + 0.4, -0.3), 0.45, sides=4)


# --------------------------------------------------------------------------
# Marine and water works
# --------------------------------------------------------------------------

def quay(b, lod):
    """Concrete quay, 120 x 25 m, deck +3.0 m: origin at the deck top
    centre (the layout puts it at +3.0 m), berth face on -Y (seaward), black
    fenders, bollards, ladders and deck lights along the edge."""
    L, W, depth = 120.0, 25.0, 9.0
    x0, x1, y0, y1 = -L / 2, L / 2, -W / 2, W / 2
    b.dims.update(length_m=L, width_m=W, deck_height_m=3.0)
    if lod == 2:
        box(b, "Deck", "concrete", (x0, y0, -depth), (x1, y1, 0.0))
        return
    step = 6.0 if lod == 0 else 30.0
    grid(b, uniform("Deck", "concrete"), (x0, y0, 0.0), (1, 0, 0), (0, 1, 0), kit.breaks(L, step), kit.breaks(W, W / (3 if lod == 0 else 1)))
    vb = [-depth, -4.0, -3.0, -1.5, 0.0] if lod == 0 else [-depth, 0.0]
    grid(b, uniform("Deck", "concrete"), (x0, y0, -depth), (1, 0, 0), (0, 0, 1), kit.breaks(L, step), [v + depth for v in vb])
    grid(b, uniform("Deck", "concrete"), (x1, y1, -depth), (-1, 0, 0), (0, 0, 1), kit.breaks(L, step * 2), [0.0, depth])
    grid(b, uniform("Deck", "concrete"), (x0, y1, -depth), (0, -1, 0), (0, 0, 1), [0.0, W], [0.0, depth])
    grid(b, uniform("Deck", "concrete"), (x1, y0, -depth), (0, 1, 0), (0, 0, 1), [0.0, W], [0.0, depth])
    # Edge kerb in safety yellow along the berth.
    box(b, "Kerb", "safety", (x0, y0, 0.0), (x1, y0 + 0.3, 0.25))
    nf = 15
    for k in range(nf):
        x = x0 + L * (k + 0.5) / nf
        box(b, "Fenders", "fender", (x - 0.8, y0 - 0.7, -3.6), (x + 0.8, y0, -0.2), bottom=True)
    for k in range(12):
        x = x0 + L * (k + 0.5) / 12
        if lod == 0:
            revolve(b, "Bollards", "steel", [(0.32, 0.75), (0.22, 0.6), (0.22, 0.0)], 8, close_top=True, centre=(x, y0 + 1.0))
        else:
            beam(b, "Bollards", "steel", (x, y0 + 1.0, 0.0), (x, y0 + 1.0, 0.6), 0.5, sides=2)
    if lod == 0:
        for x in (-45.0, -15.0, 15.0, 45.0):
            beam(b, "Ladders", "steel", (x - 0.25, y0 - 0.05, -3.5), (x - 0.25, y0 - 0.05, 0.9), 0.07, sides=4)
            beam(b, "Ladders", "steel", (x + 0.25, y0 - 0.05, -3.5), (x + 0.25, y0 - 0.05, 0.9), 0.07, sides=4)
    for x in (-50.0, -16.7, 16.7, 50.0):
        beam(b, "Lights", "steel", (x, y1 - 2.0, 0.0), (x, y1 - 2.0, 10.0), 0.22 if lod == 0 else 0.3, sides=4)
        box(b, "Lamps", "lamp", (x - 0.4, y1 - 2.6, 10.0), (x + 0.4, y1 - 1.4, 10.35), bottom=True)


def intake_basin(b, lod):
    """Seawater intake: a curved concrete basin wall (arc R 45 m over 140°,
    2 m thick, top +3.0 m) opening seaward around the -Y axis, with the pump
    house (22 x 12 x 9) on the land behind it and the intake pipes. Origin
    on the pad top (+3.6 m): the wall top sits at -0.6, its foot at -11.6."""
    R, span, t = 45.0, math.radians(140.0), 2.0
    top, foot, water = -0.6, -11.6, -3.6
    n = {0: 28, 1: 14, 2: 6}[lod]
    b.dims.update(arc_radius_m=R, arc_degrees=140.0, wall_thickness_m=t, pump_house_m=22.0)
    a0 = -math.pi / 2 - span / 2

    def at(r, k, z):
        a = a0 + span * k / n
        return (r * math.cos(a), r * math.sin(a), z)
    zs = [top, water + 1.0, water - 0.5, foot] if lod == 0 else [top, foot]
    mb = b.part("Wall", "concrete")
    for k in range(n):
        u0, u1 = R * span * k / n, R * span * (k + 1) / n
        for rr, outward in ((R + t / 2, True), (R - t / 2, False)):
            for j in range(len(zs) - 1):
                if lod == 2 and not outward:
                    continue
                p = [mb.vert(at(rr, k, zs[j])), mb.vert(at(rr, k, zs[j + 1])), mb.vert(at(rr, k + 1, zs[j + 1])), mb.vert(at(rr, k + 1, zs[j]))]
                uv = [(u0, 0), (u0, 1), (u1, 1), (u1, 0)]
                if outward:
                    mb.face(tuple(p), M, False, uv)
                else:
                    mb.face(tuple(reversed(p)), M, False, list(reversed(uv)))
        p = [mb.vert(at(R - t / 2, k, top)), mb.vert(at(R + t / 2, k, top)), mb.vert(at(R + t / 2, k + 1, top)), mb.vert(at(R - t / 2, k + 1, top))]
        mb.face(tuple(p), M, False, [(u0, 0), (u0, 1), (u1, 1), (u1, 0)])
    if lod < 2:
        for k in (0, n):
            p = [mb.vert(at(R - t / 2, k, top)), mb.vert(at(R - t / 2, k, foot)), mb.vert(at(R + t / 2, k, foot)), mb.vert(at(R + t / 2, k, top))]
            mb.face(tuple(p) if k == 0 else tuple(reversed(p)), M, False)
    if lod == 0:
        pts = [at(R - t / 2 + 0.3, k, top) for k in range(n + 1)]
        railing(b, pts, 1.1, lod, posts_every=2)
    # Pump house behind the arc, on the pad, and the two intake pipes.
    ph = (-11.0, 11.0, 8.0, 20.0)
    shed(b, ph[0], ph[1], ph[2], ph[3], 8.2, 9.0, lod, step=3.0, window=(5.0, 6.5, 3.0, 1.8),
         doors_front=((8.5, 13.5, 4.5),))
    if lod < 2:
        roof_unit(b, 0.0, 14.0, 8.9, 2.0, 2.0, 1.2, lod)
        n_p = 12 if lod == 0 else 6
        for x in (-5.0, 5.0):
            path = [(x, 8.0, 1.5), (x, -2.0, 1.5), (x * 0.4, -12.0, -2.0), (x * 0.3, -38.0, -6.0)]
            tube(b, "Pipes", "pipe", path if lod == 0 else [path[0], path[1], path[3]], 1.0, n_p)


def river_intake(b, lod):
    """River intake for the delta pad: a concrete weir across the channel
    with a trash rack and a small screen house at one end (30 x 8 x 5).
    Origin on the pad top; the weir crest sits 1.1 m lower, at +2.5 m."""
    b.dims.update(length_m=30.0, width_m=8.0, height_m=5.0)
    if lod == 2:
        grid(b, uniform("Weir", "concrete"), (-15.0, -4.0, -1.1), (1, 0, 0), (0, 1, 0), [0.0, 20.0], [0.0, 8.0])
        box(b, "House", "hall_clad", (5.0, -4.0, 0.0), (15.0, 4.0, 5.0))
        return
    step = 2.5 if lod == 0 else 20.0
    grid(b, uniform("Weir", "concrete"), (-15.0, -4.0, -1.1), (1, 0, 0), (0, 1, 0), kit.breaks(20.0, step), kit.breaks(8.0, 8.0))
    grid(b, uniform("Weir", "concrete"), (-15.0, -4.0, -3.0), (1, 0, 0), (0, 0, 1), kit.breaks(20.0, step), [0.0, 1.9])
    grid(b, uniform("Weir", "concrete"), (5.0, 4.0, -3.0), (-1, 0, 0), (0, 0, 1), kit.breaks(20.0, step), [0.0, 1.9])
    # Piers carrying the trash rack and its walkway.
    for x in (-15.0, -10.0, -5.0, 0.0):
        box(b, "Weir", "concrete", (x - 0.4, -4.0, -1.1), (x + 0.4, 4.0, 1.2))
    box(b, "Rack", "grating", (-15.0, -0.6, 1.2), (5.0, 0.6, 1.45), bottom=True)
    if lod == 0:
        for k in range(40):
            x = -15.0 + 20.0 * (k + 0.5) / 40
            beam(b, "Bars", "steel", (x, 1.4, -2.8), (x, 0.6, 1.2), 0.05, sides=2)
        railing(b, [(-15.0, -0.55, 1.45), (5.0, -0.55, 1.45)], 1.1, lod)
        railing(b, [(-15.0, 0.55, 1.45), (5.0, 0.55, 1.45)], 1.1, lod)
    shed(b, 5.0, 15.0, -4.0, 4.0, 4.4, 5.0, lod, step=2.5, doors_ends=((2.0, 5.5, 3.2),))


# --------------------------------------------------------------------------
# Utilities
# --------------------------------------------------------------------------

def lox_tank(b, lod):
    """Liquid-oxygen tank: a vertical vacuum-insulated vessel (Ø3.2 m,
    10.5 m overall) on four legs, with a ladder, a top platform and the
    vaporiser fins beside it."""
    r, n = 1.6, {0: 16, 1: 8, 2: 4}[lod]
    b.dims.update(diameter_m=3.2, height_m=10.5)
    if lod == 2:
        revolve(b, "Vessel", "lox", [(r, 10.5), (r, 1.4)], n, close_top=True, smooth=False)
        return
    if lod == 0:
        revolve(b, "Vessel", "lox", [(0.6, 10.5), (1.25, 10.3), (r, 9.9), (r, 6.0), (r, 2.0), (1.25, 1.55), (0.5, 1.4)], n,
                close_top=True, close_bottom=True)
    else:
        revolve(b, "Vessel", "lox", [(0.9, 10.5), (r, 9.9), (r, 1.9), (0.9, 1.4)], n, close_top=True, close_bottom=True)
    for k in range(4):
        th = TAU * (k + 0.5) / 4
        x, y = 1.25 * math.cos(th), 1.25 * math.sin(th)
        beam(b, "Legs", "steel", (x, y, 0.0), (x, y, 2.2), 0.18, sides=4 if lod == 0 else 2)
    ls = 4 if lod == 0 else 2
    for s in (-0.3, 0.3):
        beam(b, "Ladder", "steel", (-r - 0.35, s, 0.0), (-r - 0.35, s, 11.4), 0.06, sides=ls)
    if lod == 0:
        ring_deck(b, "Platform", "grating", 0.0001, 1.2, 10.55, 0.1, 8, outer=True)
        ring_band(b, "PlatformRail", "steel", 1.15, 11.45, 11.55, 8)
        for k in range(6):
            x = r + 0.7 + (k % 3) * 0.55
            y = -0.6 + (k // 3) * 1.2
            box(b, "Vaporiser", "steel", (x - 0.12, y - 0.4, 0.0), (x + 0.12, y + 0.4, 4.5))
        tube(b, "Pipes", "pipe", [(r, 0.0, 1.0), (r + 0.7, 0.0, 1.0), (r + 0.7, 0.0, 0.2)], 0.06, 6)


def genset_container(b, lod):
    """Backup generator in a 40 ft container (12.19 x 2.44 x 2.90 m):
    corrugated walls, the door end, a radiator louvre and an exhaust
    silencer with its stack on the roof. The long axis runs along X."""
    L, W, H = 12.19, 2.44, 2.90
    x0, x1, y0, y1 = -L / 2, L / 2, -W / 2, W / 2
    b.dims.update(length_m=L, width_m=W, height_m=H)
    if lod == 2:
        box(b, "Box", "genset", (x0, y0, 0.0), (x1, y1, H))
        return
    step = 3.05 if lod == 0 else L
    vb = [0.0, 0.5, H] if lod == 0 else [0.0, H]
    grid(b, uniform("Box", "genset"), (x0, y0, 0.0), (1, 0, 0), (0, 0, 1), kit.breaks(L, step), vb)
    grid(b, uniform("Box", "genset"), (x1, y1, 0.0), (-1, 0, 0), (0, 0, 1), kit.breaks(L, step), vb)
    grid(b, uniform("Box", "genset"), (x0, y0, H), (1, 0, 0), (0, 1, 0), kit.breaks(L, step), [0.0, W])
    grid(b, uniform("Doors", "door"), (x0, y1, 0.0), (0, -1, 0), (0, 0, 1), [0.0, W], [0.0, H])
    grid(b, uniform("Louvre", "grating"), (x1, y0, 0.0), (0, 1, 0), (0, 0, 1), [0.0, W], [0.0, H])
    tube(b, "Exhaust", "steel", [(3.5, 0.0, H), (3.5, 0.0, H + 2.1)], 0.22, 8 if lod == 0 else 4, caps=True)
    if lod == 0:
        tube(b, "Exhaust", "steel", [(1.3, 0.0, H + 0.45), (3.6, 0.0, H + 0.45)], 0.4, 8, caps=True)
        box(b, "Roofbox", "genset", (-5.5, -0.9, H), (-3.5, 0.9, H + 0.6))


def substation(b, lod):
    """Transformer yard (18 x 10 x 6 m): a concrete pad with two orange
    transformers (radiator fins, bushings), an orange switchgear kiosk and
    a grey security fence."""
    x0, x1, y0, y1 = -9.0, 9.0, -5.0, 5.0
    b.dims.update(length_m=18.0, width_m=10.0, height_m=6.0)
    if lod == 2:
        box(b, "Transformers", "orange", (-9.0, -5.0, 0.0), (9.0, 5.0, 3.6))
        return
    box(b, "Pad", "concrete", (x0, y0, 0.0), (x1, y1, 0.25))
    for cx in (-4.5, 0.5):
        box(b, "Transformers", "orange", (cx - 1.5, -1.6, 0.25), (cx + 1.5, 1.6, 3.2))
        box(b, "Transformers", "orange", (cx - 1.4, -1.5, 3.2), (cx + 1.4, 1.5, 3.6))
        if lod == 0:
            for k in range(6):
                x = cx - 1.25 + 0.5 * k
                box(b, "Transformers", "orange", (x - 0.05, -2.3, 0.6), (x + 0.05, -1.6, 2.9))
                box(b, "Transformers", "orange", (x - 0.05, 1.6, 0.6), (x + 0.05, 2.3, 2.9))
            for k in range(3):
                x = cx - 0.8 + 0.8 * k
                tube(b, "Bushings", "steel", [(x, 0.0, 3.6), (x, 0.0, 4.9)], 0.12, 6, caps=True)
    # Busbar gantry over the transformers (the yard's 6 m top).
    for x in (-7.8, 2.2):
        beam(b, "Gantry", "steel", (x, 0.0, 0.25), (x, 0.0, 5.94), 0.25, sides=4 if lod == 0 else 2)
    beam(b, "Gantry", "steel", (-7.8, 0.0, 5.82), (2.2, 0.0, 5.82), 0.25, sides=4 if lod == 0 else 2)
    box(b, "Kiosk", "orange", (4.0, -2.5, 0.25), (8.4, 2.5, 3.4))
    box(b, "KioskDoor", "door", (4.6, -2.56, 0.25), (5.8, -2.5, 2.5), bottom=True)
    fence = [(x0, y0, 0.0), (x1, y0, 0.0), (x1, y1, 0.0), (x0, y1, 0.0)]
    if lod == 0:
        for i in range(4):
            a, c = fence[i], fence[(i + 1) % 4]
            n = max(1, int(round(math.dist(a, c) / 3.0)))
            for k in range(n):
                p = tuple(a[j] + (c[j] - a[j]) * k / n for j in range(3))
                beam(b, "Fence", "steel", p, (p[0], p[1], 2.4), 0.07, sides=4)
            for z in (0.15, 2.3):
                beam(b, "Fence", "steel", (a[0], a[1], z), (c[0], c[1], z), 0.05, sides=4)
    else:
        for i in range(4):
            a, c = fence[i], fence[(i + 1) % 4]
            beam(b, "Fence", "steel", (a[0], a[1], 2.3), (c[0], c[1], 2.3), 0.05, 0.12, sides=2)


def feed_silo_land(b, lod):
    """Four feed silos (Ø4 x 14 m) on a 5 m grid: cone bottoms on legs, a
    roof cone, a shared catwalk across the tops and a ladder."""
    n = {0: 16, 1: 8, 2: 4}[lod]
    b.dims.update(silo_diameter_m=4.0, silo_height_m=14.0, silo_count=4)
    for k in range(4):
        cx, cy = (k % 2) * 5.0 - 2.5, (k // 2) * 5.0 - 2.5
        if lod == 2:
            if k == 0:  # the cluster as one block (12 triangles)
                box(b, "Silos", "silo", (-4.5, -4.5, 2.0), (4.5, 4.5, 14.0))
            continue
        if lod == 0:
            prof = [(0.35, 14.0), (2.0, 12.8), (2.0, 8.0), (2.0, 4.2), (0.45, 1.6)]
            revolve(b, "Silos", "silo", prof, n, close_top=True, close_bottom=True, centre=(cx, cy))
        else:
            revolve(b, "Silos", "silo", [(0.4, 14.0), (2.0, 12.8), (2.0, 4.2), (0.6, 0.0)], 5, close_top=True, centre=(cx, cy))
            continue
        for j in range(4):
            th = TAU * (j + 0.5) / (4 if lod == 0 else 2)
            x, y = cx + 1.75 * math.cos(th), cy + 1.75 * math.sin(th)
            beam(b, "Legs", "steel", (x, y, 0.0), (x, y, 4.3), 0.2, sides=4)
    if lod < 2:
        box(b, "Catwalk", "grating", (-5.0, -0.5, 14.0), (5.0, 0.5, 14.2), bottom=True)
        for s in (-0.3, 0.3):
            if lod == 0:
                beam(b, "Ladder", "steel", (-4.9, s, 0.0), (-4.9, s, 15.2), 0.06, sides=4)
        if lod == 0:
            railing(b, [(-5.0, -0.5, 14.2), (5.0, -0.5, 14.2)], 1.0, lod)
            railing(b, [(-5.0, 0.5, 14.2), (5.0, 0.5, 14.2)], 1.0, lod)
            for k in range(2):
                tube(b, "Pipes", "pipe", [(-2.5 + 5.0 * k, -2.5, 13.6), (-2.5 + 5.0 * k, -5.5, 13.6), (-2.5 + 5.0 * k, -5.5, 1.0)], 0.1, 6)


def sludge_station(b, lod):
    """Sludge dewatering: a pale house (12 x 12 x 8 m) with a roller door
    and a roof vent, beside a covered bay with two skip containers."""
    b.dims.update(length_m=20.0, width_m=12.0, height_m=8.0)
    if lod == 2:
        box(b, "House", "hall_clad", (-10.0, -6.0, 0.0), (10.0, 6.0, 8.0))
        return
    shed(b, -10.0, 2.0, -6.0, 6.0, 7.3, 8.0, lod, wall=("House", "hall_clad"), step=3.0,
         window=(4.6, 5.8, 3.0, 1.6), doors_ends=((3.0, 7.5, 5.0),))
    if lod < 2:
        roof_unit(b, -4.0, 0.0, 7.9, 1.6, 1.6, 1.1, lod)
    for y0 in (-5.0, 0.5):
        box(b, "Skips", "sludge", (2.6, y0, 0.0), (9.6, y0 + 4.5, 2.4))
    for x in (2.3, 9.9):
        for y in (-5.7, 5.7):
            beam(b, "Canopy", "steel", (x, y, 0.0), (x, y, 5.2), 0.2, sides=4 if lod == 0 else 2)
    box(b, "CanopyRoof", "hall_roof", (2.0, -6.0, 5.2), (10.2, 6.0, 5.45), bottom=True)


DESIGNS = {
    "tank_growout": tank_growout,
    "tank_pregrow": tank_pregrow,
    "tank_feeder": tank_feeder,
    "process_hall_bay": process_hall_bay,
    "harvest_hall": harvest_hall,
    "pipe_bridge_span": pipe_bridge_span,
    "quay": quay,
    "intake_basin": intake_basin,
    "river_intake": river_intake,
    "lox_tank": lox_tank,
    "genset_container": genset_container,
    "substation": substation,
    "feed_silo_land": feed_silo_land,
    "sludge_station": sludge_station,
}
