"""Geometry of the slaughterhouse kit (GRAPHICS_HANDOVER §2 Task A).

A land slaughterhouse is drawn from these pieces plus the land kit's reused
`harvest_hall` (the Large tier) and `quay` (the wellboat berth), all on the
land kit's one material (LK_FacilityKit) and trim atlas rows 0-27 (no new
rows). Every asset is design(b, lod) filling a Build with named parts, as in
land_designs.py. Blender axes: X right, Y forward (glTF/Godot -Z), Z up; the
seaward side is -Y. Halls, reception and loading bays stand on the pad top;
the intake stands on the quay deck.

  slaughter_hall_s     Small tier (10 t/day): 40 x 24 x 9 m dark duo-pitch hall.
  slaughter_hall_m     Medium tier (75 t/day): 80 x 40 x 12 m flat-roofed hall.
  reception_house      Live-fish reception / pump house, 18 x 12 x 8 m.
  chilled_loading_bay  Refrigerated dispatch module, 14 x 12 x 7.5 m, 2 docks.
  wellboat_intake      Wellboat discharge station: frame tower, dewatering box,
                       slewing hose crane (10 m reach), fish pipe landward.

No names on any model: the halls carry a blank logo panel (D17).

Licence: in-house geometry (proprietary, part of the Lakskongen repository).
"""

from __future__ import annotations

from land_designs import ROOF_LIGHT_LIFT, _roof_lights
from land_parts import beam, block, box, grid, railing, roof_unit, shed, tube, uniform

DARK = ("Walls", "harvest_clad")


def slaughter_hall_s(b, lod):
    """Small tier: one line, a cold store and staff rooms under one dark
    duo-pitch roof; pale fascia, blank logo, two sea-side doors to the
    reception, an end door for dispatch, two roof units, roof lights."""
    x0, x1, y0, y1, eave, ridge = -20.0, 20.0, -12.0, 12.0, 8.2, 9.0
    shed(b, x0, x1, y0, y1, eave, ridge, lod, wall=DARK, step=4.0,
         window=(5.6, 7.0, 4.0, 3.0), doors_front=((22.0, 27.0, 6.0), (30.0, 35.0, 6.0)),
         doors_ends=((9.0, 15.0, 4.5),))
    if lod < 2:
        box(b, "Fascia", "hall_clad", (x0 - 0.06, y0 - 0.06, eave - 1.0), (x1 + 0.06, y1 + 0.06, eave - 0.02))
        box(b, "Logo", "panel_blank", (x0 + 3.0, y0 - 0.25, 2.4), (x0 + 13.0, y0 + 0.05, 5.2), bottom=True)
        for x in (-8.0, 8.0):
            roof_unit(b, x, 3.0, ridge - 0.4, 2.4, 2.0, 1.3, lod)
        _roof_lights(b, x0, x1, y0, y1, eave, ridge)
    b.dims.update(length_m=x1 - x0, width_m=y1 - y0, height_m=ridge)


def slaughter_hall_m(b, lod):
    """Medium tier: the harvest hall's look at two thirds of its size, flat
    roof with parapet, three sea-side doors, a stair tower at the office
    corner, roof units and roof-light bands."""
    x0, x1, y0, y1, h = -40.0, 40.0, -20.0, 20.0, 12.0
    block(b, x0, x1, y0, y1, h, lod, wall=DARK, roof=("HallRoof", "harvest_clad"), step=6.0, rows=(0.6, 3.0, 6.0),
          window=(7.0, 9.0, 4.0, 3.0) if lod < 2 else None,
          doors_front=((14.0, 19.0, 6.0), (28.0, 33.0, 6.0), (60.0, 65.0, 6.0)),
          fascia=(10.6, "Fascia", "hall_clad"), window_sides=("front", "left"))
    if lod < 2:
        box(b, "Logo", "panel_blank", (-36.0, y0 - 0.25, 6.6), (-22.0, y0 + 0.05, 9.6), bottom=True)
        for k, x in enumerate((-24.0, 0.0, 24.0)):
            roof_unit(b, x, -6.0 + 8.0 * (k % 2), h - 1.0, 4.0, 3.0, 1.6, lod)
        for xc in (-30.0, -10.0, 10.0, 30.0):
            grid(b, uniform("Skylights", "skylight"), (xc + 1.2, y0 + 3.0, h - 1.0 + ROOF_LIGHT_LIFT), (0, 1, 0),
                 (-1, 0, 0), [0.0, y1 - y0 - 6.0], [0.0, 2.4])
        if lod == 0:
            box(b, "Tower", "harvest_clad", (x0 - 5.0, y0 + 4.0, 0.0), (x0, y0 + 10.0, h + 2.0))
            box(b, "TowerGlass", "window", (x0 - 5.05, y0 + 5.0, 2.0), (x0 - 4.95, y0 + 9.0, h))
    b.dims.update(length_m=x1 - x0, width_m=y1 - y0, height_m=h)


def reception_house(b, lod):
    """Live-fish reception and pump house at the hall's sea end: a blank dark
    box over the dewatering and holding-tank room, two fish pipes up from the
    quay, two vent stacks, a man door."""
    x0, x1, y0, y1, h = -9.0, 9.0, -6.0, 6.0, 8.0
    block(b, x0, x1, y0, y1, h, lod, wall=DARK, roof=("Roof", "hall_roof"), step=3.0, rows=(0.6, 2.4),
          doors_left=((4.5, 5.7, 2.3),), fascia=(7.0, "Fascia", "hall_clad"), parapet=0.6)
    if lod == 0:
        for x in (-3.0, 3.0):
            tube(b, "Pipes", "pipe", [(x, y0 - 8.0, -1.5), (x, y0 - 3.0, 1.5), (x, y0, 1.5)], 0.2, 8)
        for x in (-5.0, 5.0):
            tube(b, "Roofgear", "steel", [(x, 2.0, h - 0.6), (x, 2.0, h + 2.2)], 0.3, 8, caps=True)
    elif lod == 1:
        for x in (-3.0, 3.0):
            beam(b, "Pipes", "pipe", (x, y0 - 8.0, 1.5), (x, y0, 1.5), 0.4, sides=2)
        for x in (-5.0, 5.0):
            beam(b, "Roofgear", "steel", (x, 2.0, h - 0.6), (x, 2.0, h + 2.2), 0.6, sides=2)
    b.dims.update(length_m=x1 - x0, width_m=y1 - y0, height_m=h)


def chilled_loading_bay(b, lod):
    """Refrigerated dispatch module that repeats along a hall wall: pale
    walls, two 3.0 m dock doors on the land side (+Y) with black shelters
    and bumpers, a canopy, roof condensers and dock lamps."""
    x0, x1, y0, y1, h = -7.0, 7.0, -6.0, 6.0, 7.5
    block(b, x0, x1, y0, y1, h, lod, wall=("Walls", "hall_clad"), roof=("Roof", "hall_roof"), step=3.5,
          rows=(0.6, 2.4), doors_back=((2.5, 5.5, 4.4), (8.5, 11.5, 4.4)), parapet=0.6)
    if lod < 2:
        # Door u runs along -X on the back wall: u 2.5-5.5 -> x 1.5..4.5, u 8.5-11.5 -> x -4.5..-1.5.
        for xa in (1.5, -4.5):
            xb = xa + 3.0
            box(b, "Shelters", "fender", (xa - 0.5, y1, 4.4), (xb + 0.5, y1 + 0.6, 4.9))
            if lod == 0:
                box(b, "Shelters", "fender", (xa - 0.5, y1, 0.8), (xa, y1 + 0.6, 4.4))
                box(b, "Shelters", "fender", (xb, y1, 0.8), (xb + 0.5, y1 + 0.6, 4.4))
                box(b, "Lamps", "lamp", (xa + 1.2, y1, 5.6), (xb - 1.2, y1 + 0.3, 5.85), bottom=True)
                for x in (xa + 0.2, xb - 0.6):
                    box(b, "Bumpers", "fender", (x, y1, 0.9), (x + 0.4, y1 + 0.25, 1.3))
        box(b, "Canopy", "hall_roof", (x0, y1, 5.2), (x1, y1 + 3.0, 5.45), bottom=lod == 0)
        if lod == 0:
            box(b, "Kerb", "safety", (x0, y1 + 2.8, 0.0), (x1, y1 + 3.0, 0.15))
            for x in (-3.5, 3.5):
                roof_unit(b, x, -1.5, h - 0.6, 3.0, 2.0, 1.4, lod, part="Condensers")
        else:
            roof_unit(b, 0.0, -1.5, h - 0.6, 10.0, 2.0, 1.4, lod, part="Condensers")
    b.dims.update(length_m=x1 - x0, width_m=y1 - y0, height_m=h)


def wellboat_intake(b, lod):
    """Wellboat discharge station on the quay deck (origin at the deck top):
    a four-legged frame tower with a grated platform at 6 m, a dewatering box
    on it, an orange slewing crane holding the black fish hose 10 m out over
    the berth (-Y), and the fish pipe down to the deck and landward."""
    px, py, top = 3.6, 2.6, 6.25
    tip = (2.8, -12.0, 7.0)
    if lod == 2:
        box(b, "Platform", "grating", (-px, -py, 0.0), (px, py, top))
        box(b, "Dewater", "steel", (-1.5, -1.2, top), (1.5, 1.2, 8.2))
        beam(b, "Crane", "orange", (2.8, -2.4, 9.0), tip, 0.5, sides=2)
        b.dims.update(platform_length_m=2 * px, platform_width_m=2 * py, platform_height_m=top)
        return
    n = 8 if lod == 0 else 4
    for x in (-3.0, 3.0):
        for y in (-2.0, 2.0):
            beam(b, "Frame", "steel", (x, y, 0.0), (x, y, 6.0), 0.3)
    if lod == 0:
        for x in (-3.0, 3.0):
            beam(b, "Frame", "steel", (x, -2.0, 0.3), (x, 2.0, 5.7), 0.14, sides=2)
        for y in (-2.0, 2.0):
            beam(b, "Frame", "steel", (-3.0, y, 0.3), (3.0, y, 5.7), 0.14, sides=2)
        railing(b, [(-px, -py, top), (px, -py, top), (px, py, top), (-px, py, top)], 1.1, lod,
                posts_every=1, part="Rail", strip="safety", closed=True)
    box(b, "Platform", "grating", (-px, -py, 6.0), (px, py, top), bottom=True)
    box(b, "Dewater", "steel", (-1.5, -1.2, top), (1.5, 1.2, 8.2))
    beam(b, "Crane", "orange", (2.8, -2.4, top), (2.8, -2.4, 9.0), 0.5)
    beam(b, "Crane", "orange", (2.8, -2.4, 9.0), tip, 0.35)
    tube(b, "Hose", "fender", [tip, (2.8, -12.3, 3.0), (2.0, -12.6, -2.5)], 0.25, n)
    tube(b, "Pipes", "pipe", [(0.0, 1.2, 6.6), (0.0, 3.2, 5.8), (0.0, 4.2, 0.6), (0.0, 20.0, 0.6)], 0.2, n)
    b.dims.update(platform_length_m=2 * px, platform_width_m=2 * py, platform_height_m=top)


DESIGNS = {
    "slaughter_hall_s": slaughter_hall_s,
    "slaughter_hall_m": slaughter_hall_m,
    "reception_house": reception_house,
    "chilled_loading_bay": chilled_loading_bay,
    "wellboat_intake": wellboat_intake,
}
