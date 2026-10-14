"""Vessel designs for build_vessels.py: what goes where on each boat.

All dimensions in metres from public specifications (see build_vessels.py
for the references). Blender axes: X starboard, Y forward, Z up, z = 0 the
waterline. Each design fills a lk_hull.Parts with the parts
Hull, Deck, Superstructure, Windows, Machinery, Rubber, Crane, Safety.

`d` is the detail level: 2 = LOD0 (everything), 1 = LOD1 (no railings,
mullions or small gear), 0 = LOD2 (silhouette blocks).

Licence: part of the Lakskongen repository (proprietary); no third-party code.
"""

from __future__ import annotations

import math

import lk_hull as hull
from lk_hull import HullSpec, Parts

LEVELS = {
    2: [0.0, 0.03, 0.08, 0.15, 0.24, 0.34, 0.45, 0.56, 0.66, 0.75, 0.83, 0.9, 0.95, 1.0],
    1: [0.0, 0.08, 0.2, 0.4, 0.65, 0.85, 1.0],
    0: [0.0, 0.3, 0.75, 1.0],
}
STATIONS = {2: 56, 1: 22, 0: 9}


def y_where(spec: HullSpec, z: float, half_needed: float, forward=True) -> float:
    """The furthest y (forward or aft) where the hull is at least
    half_needed wide at height z."""
    us = [i / 400.0 for i in range(401)]
    if forward:
        us = list(reversed(us))
    for u in us:
        if hull.hull_half_beam(spec, u, z) >= half_needed:
            return hull.hull_y(spec, u, z)
    return 0.0


def _rail_rect(parts, x, y0, y1, z, height, d, sides=4, closed=True):
    if d < 2:
        return
    pts = [(x, y0, z), (x, y1, z), (-x, y1, z), (-x, y0, z)]
    hull.railing(parts["Machinery"], pts, height, closed=closed, sides=sides)


def _hull_rail(parts, spec, z_of_u, inset, height, d, u0=0.0, u1=1.0, n=40, side_only=False):
    """Railing along the deck edge (barges: no solid bulwark)."""
    if d < 2:
        return
    us = [u0 + (u1 - u0) * i / n for i in range(n + 1)]
    for side in (1.0, -1.0):
        pts = []
        for u in us:
            z = z_of_u(u)
            x = hull.hull_half_beam(spec, u, z) - inset
            pts.append((side * x, hull.hull_y(spec, u, z), z))
        hull.railing(parts["Machinery"], pts, height, step=1.8, sides=4)


def _house_with_bands(parts, x_half, y0, y1, z0, h, band, faces, d, **kw):
    mullion = kw.pop("mullion", 0.0)
    hull.deckhouse(parts["Superstructure"], parts["Windows"], x_half, y0, y1, z0, h,
                   band=band, faces=faces, mullion=mullion if d >= 2 else 0.0,
                   inset=0.07 if d >= 1 else 0.0, **kw)


def _visor(parts, x_half, y0, y1, z, d):
    hull.slab(parts["Superstructure"], x_half, y0, y1, z, 0.16 if d else 0.1)


def _doors(parts, x, y_list, z0, d, facing=1.0):
    if d < 2:
        return
    for y in y_list:
        hull.block(parts["Rubber"], (x + facing * 0.03, y, z0 + 1.0), 0.04, 0.42, 1.0)


def _fender_strake(parts, spec, z, u0, u1, d, r=0.2):
    if d < 1:
        return
    n = 24 if d >= 2 else 8
    for side in (1.0, -1.0):
        pts = []
        for i in range(n + 1):
            u = u0 + (u1 - u0) * i / n
            x = hull.hull_half_beam(spec, u, z)
            pts.append((side * (x + r * 0.6), hull.hull_y(spec, u, z), z))
        hull.tube(parts["Rubber"], pts, r, 6 if d >= 2 else 4)


# --------------------------------------------------------------------------
# Workboat: 15 x 10 m aquaculture service catamaran
# --------------------------------------------------------------------------

def workboat(parts: Parts, d: int, L=15.0, B=10.0):
    demi_b = 2.7
    cx = B / 2 - demi_b / 2
    deck = 1.55
    spec = HullSpec(length=L, beam=demi_b, draught=1.1, deck_z=lambda u: deck + 0.35 * max(u - 0.6, 0.0) / 0.4,
                    bulwark=0.0, bow_rake=2.3, stern_rake=0.15, rake_power=(1.5, 1.0), pmb=(0.15, 0.5),
                    bow_fullness=(1.35, 2.2), stern_frac=(0.55, 0.95), stern_power=2.0, vee=0.95)
    st = hull.stations(STATIONS[d] // 2 + 6)
    for side in (1.0, -1.0):
        hull.loft_hull(spec, st, LEVELS[d], parts["Hull"], parts["Deck"], x_offset=side * cx)
    y_aft, y_fwd = -L / 2 + 0.05, L / 2 - 2.2
    wet = 0.95
    # Bridging platform: hull-painted sides, deck plating on top.
    hb, hm = parts["Hull"]
    hull.kit.box(hb, (0.0, (y_aft + y_fwd) / 2, (wet + deck) / 2), (1, 0, 0), (0, 1, 0), (0, 0, 1),
                 B / 2, (y_fwd - y_aft) / 2, (deck - wet) / 2, hm)
    hull.slab(parts["Deck"], B / 2 - 0.12, y_aft + 0.1, y_fwd - 0.1, deck, 0.03)
    # Bulwarks: hull-coloured sides aft, open stern with a roller.
    for side in (1.0, -1.0):
        hull.block(parts["Hull"], (side * (B / 2 - 0.06), (y_aft + 0.5) / 2 - 0.0, deck + 0.4),
                   0.06, (0.5 - y_aft) / 2, 0.4, skip_bottom=True)
        hull.block(parts["Hull"], (side * (B / 2 - 1.2), y_aft + 0.06, deck + 0.4), 1.2, 0.06, 0.4)
    hull.cyl(parts["Rubber"], (-1.6, y_aft + 0.15, deck + 0.25), (1.6, y_aft + 0.15, deck + 0.25),
             0.22, 12 if d else 6)
    # Push knees on both bows (black rubber), the aquaculture boat's signature.
    stem = hull.hull_y(spec, 1.0, 1.3)
    for side in (1.0, -1.0):
        x = side * cx
        hull.block(parts["Rubber"], (x, stem - 0.15, 1.3), 0.38, 0.32, 1.05, skip_bottom=False)
    # Wheelhouse forward on a short plinth, with a sun visor.
    wh_y0, wh_y1, wz = 0.6, 4.9, deck + 0.35
    hull.block(parts["Superstructure"], (0.0, (wh_y0 + wh_y1) / 2, deck + 0.175), 2.7, (wh_y1 - wh_y0) / 2, 0.175)
    _house_with_bands(parts, 2.55, wh_y0, wh_y1, wz, 2.45, (0.4, 0.92), "fblr", d,
                      front_rake=0.35, taper=0.1, mullion=0.85)
    roof = wz + 2.45
    _visor(parts, 2.75, wh_y0 - 0.1, wh_y1 + 0.75, roof, d)
    roof += 0.16 if d else 0.1
    if d >= 1:
        hull.mast(parts["Superstructure"], parts["Machinery"], parts["Rubber"], (0.0, 2.2, roof), 2.6,
                  radar=1.3, detail=d >= 2)
    if d >= 2:
        for x in (-1.6, 1.6):
            hull.cyl(parts["Machinery"], (x, 4.6, roof), (x, 4.75, roof + 0.25), 0.16, 8)
        hull.railing(parts["Machinery"], [(-2.5, wh_y0, roof), (2.5, wh_y0, roof), (2.5, wh_y1, roof),
                                           (-2.5, wh_y1, roof)], 0.6, closed=True)
        hull.liferaft(parts["Superstructure"], parts["Machinery"], (-1.2, 1.2, roof + 0.4))
        for x in (-2.0, 2.0):
            hull.torus(parts["Safety"], (x, wh_y0 - 0.12, wz + 1.4), (0, 1, 0), 0.32, 0.06, 14, 6)
        _doors(parts, 2.55, [1.6], wz, d)
    # Big knuckle crane behind the wheelhouse, a small one aft.
    if d >= 1:
        hull.knuckle_crane(parts["Crane"], parts["Machinery"], parts["Rubber"], (1.9, -0.6, deck), 180.0,
                           reach=0.95, sides=10 if d >= 2 else 6, detail=d >= 2)
        hull.knuckle_crane(parts["Crane"], parts["Machinery"], parts["Rubber"], (-3.7, -6.3, deck), 90.0,
                           reach=0.55, sides=8 if d >= 2 else 5, detail=d >= 2)
    else:
        hull.block(parts["Crane"], (1.9, -1.0, deck + 1.8), 0.5, 1.6, 1.8)
    if d >= 2:
        for x in (-2.9, 2.9):
            hull.capstan(parts["Machinery"], (x, -5.4, deck))
        hull.winch(parts["Machinery"], parts["Rubber"], (-0.6, -3.3, deck + 0.55), width=2.2)
        for x, y in ((-4.3, 4.2), (4.3, 4.2), (-4.3, -6.6), (4.3, -6.6)):
            hull.bollard_pair(parts["Machinery"], (x, y, deck))
        hull.torus(parts["Rubber"], (2.4, -5.9, deck + 0.12), (0, 0, 1), 0.55, 0.11, 16, 6)
        hull.block(parts["Machinery"], (-2.6, -1.4, deck + 0.45), 0.7, 0.55, 0.45)
        # Tyre fenders along the outer sides.
        for side in (1.0, -1.0):
            pts = [(side * B / 2, y, 0.0) for y in (-5.6, -3.2, -0.8, 1.6, 4.0)]
            hull.tyre_fenders(parts["Rubber"], pts, (side, 0, 0), 1.2, 0)
        hull.railing(parts["Machinery"], [(-B / 2 + 0.1, y_fwd - 0.1, deck), (-B / 2 + 0.1, 0.6, deck)], 0.9)
        hull.railing(parts["Machinery"], [(B / 2 - 0.1, y_fwd - 0.1, deck), (B / 2 - 0.1, 0.6, deck)], 0.9)
    return spec


# --------------------------------------------------------------------------
# Feed barge: square steel barge, centre control room (Feeder 500 class)
# --------------------------------------------------------------------------

def feed_barge(parts: Parts, d: int, L=37.0, B=11.2):
    deck = 2.1
    spec = HullSpec(length=L, beam=B, draught=2.6, deck_z=lambda u: deck, bulwark=0.0, bow_rake=2.2,
                    stern_rake=2.2, rake_power=(1.0, 1.0), pmb=(0.07, 0.93), bow_fullness=(5.0, 5.0),
                    bow_end=0.84, stern_frac=(0.84, 0.84), stern_power=5.0, bilge_r=0.5, camber=0.06)
    hull.loft_hull(spec, hull.stations(STATIONS[d] // 2 + 4), LEVELS[d], parts["Hull"], parts["Deck"])
    _fender_strake(parts, spec, 0.45, 0.02, 0.98, d, r=0.16)
    # Silo house over most of the deck; the crew quarters in the middle
    # with windows on the long sides.
    hx, h, z0 = B / 2 - 0.75, 3.5, deck
    ys = (-L / 2 + 2.0, -4.8, 4.8, L / 2 - 2.0)
    sup, win = parts["Superstructure"], parts["Windows"]
    top = z0 + h
    for k in range(3):
        y0, y1 = ys[k], ys[k + 1]
        band = (0.42, 0.72) if k == 1 and d >= 1 else None
        for side in (1.0, -1.0):
            if side > 0:
                hull.wall(sup, win, (hx, y0, z0), (hx, y1, z0), (hx, y0, top), (hx, y1, top), band, 0.07,
                          1.5 if d >= 2 else 0.0)
            else:
                hull.wall(sup, win, (-hx, y1, z0), (-hx, y0, z0), (-hx, y1, top), (-hx, y0, top), band, 0.07,
                          1.5 if d >= 2 else 0.0)
    hull.wall(sup, None, (hx, ys[3], z0), (-hx, ys[3], z0), (hx, ys[3], top), (-hx, ys[3], top))
    hull.wall(sup, None, (-hx, ys[0], z0), (hx, ys[0], z0), (-hx, ys[0], top), (hx, ys[0], top))
    smb, smat = sup
    smb.face((smb.vert((-hx, ys[0], top)), smb.vert((hx, ys[0], top)), smb.vert((hx, ys[3], top)),
              smb.vert((-hx, ys[3], top))), smat, False)
    # Control room on the roof, windows all round, and its mast.
    _house_with_bands(parts, 3.3, -3.0, 3.0, top, 2.6, (0.36, 0.92), "fblr", d, front_rake=0.0, mullion=1.1)
    croof = top + 2.6
    _visor(parts, 3.55, -3.25, 3.25, croof, d)
    if d >= 1:
        hull.mast(sup, parts["Machinery"], parts["Rubber"], (0.0, -1.2, croof + 0.16), 4.0, radar=1.8,
                  detail=d >= 2)
    # Silo fill hatches (2 x 2 each side of the control room) and fill pipes.
    if d >= 1:
        for y in (-13.0, -8.5, 8.5, 13.0):
            for x in (-2.4, 2.4):
                hull.cyl(parts["Machinery"], (x, y, top), (x, y, top + 0.35), 0.75, 16 if d >= 2 else 8)
                if d >= 2:
                    hull.cyl(parts["Machinery"], (x, y, top + 0.35), (x, y, top + 0.42), 0.55, 16)
                    hull.tube(parts["Machinery"], [(x * 1.5, y + 0.9, top), (x * 1.5, y + 0.9, top + 0.8),
                                                   (x * 1.15, y + 0.9, top + 1.1)], 0.09, 6)
    # Crane on the forward roof, exhausts aft.
    if d >= 1:
        hull.knuckle_crane(parts["Crane"], parts["Machinery"], parts["Rubber"], (-3.2, 15.3, top), 90.0,
                           reach=0.9, sides=10 if d >= 2 else 6, detail=d >= 2)
        for x in (2.6, 3.4):
            hull.cyl(parts["Rubber"], (x, -15.6, top), (x, -15.6, top + 2.2), 0.18, 8 if d >= 2 else 4)
    else:
        hull.block(parts["Crane"], (-3.2, 15.3, top + 1.6), 0.6, 0.6, 1.6)
    # Feed line outlets on the starboard (feeding) side: hoses over the edge.
    n_lines = 8 if d >= 2 else (4 if d >= 1 else 0)
    for k in range(n_lines):
        y = -10.5 + 21.0 * k / max(n_lines - 1, 1)
        x0 = B / 2
        # Out of the selector valves, over the side with a bend restrictor,
        # then floating away towards the pens.
        hull.tube(parts["Rubber"], [(hx, y, deck + 0.6), (x0 - 0.1, y, deck + 0.6), (x0 + 0.45, y, deck + 0.1),
                                    (x0 + 0.85, y, 1.0), (x0 + 1.3, y, 0.15), (x0 + 2.4, y + 0.3, 0.02),
                                    (x0 + 5.5, y + 1.2, 0.0), (x0 + 6.5, y + 1.5, -0.3)], 0.07, 6 if d >= 2 else 4)
    if d >= 2:
        hull.block(parts["Machinery"], (hx + 0.25, 0.0, deck + 0.5), 0.2, 11.0, 0.12)
        _hull_rail(parts, spec, lambda u: deck, 0.12, 1.0, d)
        hull.railing(parts["Machinery"], [(hx - 0.1, ys[0] + 0.1, top), (hx - 0.1, ys[3] - 0.1, top),
                                          (-hx + 0.1, ys[3] - 0.1, top), (-hx + 0.1, ys[0] + 0.1, top)],
                     1.0, closed=True)
        for y in (-L / 2 + 1.0, -7.0, 7.0, L / 2 - 1.0):
            for x in (-B / 2 + 0.7, B / 2 - 0.7):
                hull.bollard_pair(parts["Machinery"], (x, y, deck))
        for side in (1.0, -1.0):
            pts = [(side * B / 2, y, 0.0) for y in [-15.0 + 3.0 * k for k in range(11)]
                   if side < 0 or abs(y + 0.3) > 12.0]
            hull.tyre_fenders(parts["Rubber"], pts, (side, 0, 0), 1.3, 0)
        _doors(parts, hx, [-5.6, 5.6], deck, d)
        _doors(parts, -hx, [-5.6, 5.6], deck, d, facing=-1.0)
        # Stiffener pilasters break up the long silo-house walls.
        for side in (1.0, -1.0):
            for y in [ys[0] + 0.1 + 3.0 * k for k in range(12)]:
                if -6.4 < y < 6.4 or y > ys[3] - 0.2:
                    continue
                hull.block(sup, (side * (hx + 0.05), y, deck + h / 2), 0.05, 0.12, h / 2)
        for y in (-1.0, 1.0):
            hull.liferaft(sup, parts["Machinery"], (-4.3, y, top + 0.4), axis_x=False)
        for y in (-10.0, 10.0):
            hull.torus(parts["Safety"], (hx + 0.08, y, deck + 1.6), (1, 0, 0), 0.32, 0.06, 14, 6)
        # Ladder up the aft end wall.
        for x in (-0.25, 0.25):
            hull.cyl(parts["Machinery"], (x + 2.0, ys[0] - 0.2, deck), (x + 2.0, ys[0] - 0.2, top + 1.0), 0.03, 4)
    return spec


def feed_barge_large(parts: Parts, d: int, L=64.8, B=13.0):
    """Ship-shaped exposed-site barge (AC800-class): raised bow with the
    control house, long silo house, a workboat ramp in the stern."""
    main, fc_u = 3.2, 0.76

    def deck_z(u):
        if u <= fc_u - 0.004:
            return main
        if u >= fc_u + 0.004:
            t = (u - fc_u) / (1 - fc_u)
            return main + 2.4 + 0.8 * t * t
        return main + 2.4 * (u - fc_u + 0.004) / 0.008

    spec = HullSpec(length=L, beam=B, draught=4.2, deck_z=deck_z, bulwark=1.1, bulwark_t=0.22, bow_rake=5.5,
                    stern_rake=1.2, rake_power=(1.7, 2.0), pmb=(0.2, 0.62), bow_fullness=(1.7, 3.0),
                    stern_frac=(0.55, 0.96), stern_power=2.5, bilge_r=1.6, camber=0.1)
    hull.loft_hull(spec, hull.stations(STATIONS[d], dense=(fc_u,)), LEVELS[d], parts["Hull"], parts["Deck"],
                   parts["Superstructure"])
    _fender_strake(parts, spec, 1.4, 0.05, 0.7, d)
    sup, win = parts["Superstructure"], parts["Windows"]
    y_fc = hull.hull_y(spec, fc_u + 0.01, main + 2.4)
    fz = main + 2.4
    # Forward accommodation and the control room on top.
    y_front = y_where(spec, fz, 5.3) - 0.6
    _house_with_bands(parts, 5.3, y_fc + 0.8, y_front, fz, 2.9, (0.45, 0.75), "fblr", d, mullion=1.4)
    _house_with_bands(parts, 4.0, y_fc + 2.0, y_front - 1.0, fz + 2.9, 2.7, (0.36, 0.92), "fblr", d,
                      front_rake=0.45, mullion=1.0)
    croof = fz + 5.6
    _visor(parts, 4.25, y_fc + 1.8, y_front - 0.4, croof, d)
    if d >= 1:
        hull.mast(sup, parts["Machinery"], parts["Rubber"], (0.0, y_fc + 4.0, croof + 0.16), 5.0, radar=2.4,
                  detail=d >= 2)
    # Long silo house amidships.
    sy0, sy1, sh = -15.5, y_fc - 0.8, 4.4
    hull.deckhouse(sup, win, 5.2, sy0, sy1, main, sh, band=None, faces="")
    top = main + sh
    if d >= 1:
        n = int((sy1 - sy0) / 4.4)
        for k in range(n):
            y = sy0 + 2.2 + k * (sy1 - sy0 - 4.4) / max(n - 1, 1)
            for x in (-2.6, 2.6):
                hull.cyl(parts["Machinery"], (x, y, top), (x, y, top + 0.35), 0.8, 16 if d >= 2 else 8)
        hull.knuckle_crane(parts["Crane"], parts["Machinery"], parts["Rubber"], (3.6, sy0 + 2.0, top), 180.0,
                           reach=1.1, sides=10 if d >= 2 else 6, detail=d >= 2)
        # Workboat ramp down the stern.
        hull.kit.box(parts["Deck"][0], (0.0, -L / 2 + 4.5, main * 0.5 + 0.2), (1, 0, 0),
                     hull.kit.v_norm((0, 1, 0.33)), hull.kit.v_norm((0, -0.33, 1)), 2.6, 5.0, 0.12,
                     parts["Deck"][1])
        hull.knuckle_crane(parts["Crane"], parts["Machinery"], parts["Rubber"], (-4.4, -L / 2 + 9.5, main), 0.0,
                           reach=0.8, sides=10 if d >= 2 else 6, detail=d >= 2)
    else:
        hull.block(parts["Crane"], (3.6, sy0 + 2.0, top + 1.8), 0.6, 0.6, 1.8)
    n_lines = 10 if d >= 2 else (4 if d >= 1 else 0)
    for k in range(n_lines):
        y = sy0 + 3.0 + (sy1 - sy0 - 6.0) * k / max(n_lines - 1, 1)
        x0 = hull.hull_half_beam(spec, 0.4, main)
        hull.tube(parts["Rubber"], [(5.2, y, main + 0.6), (x0 - 0.4, y, main + 1.5), (x0 + 0.3, y, main + 1.6),
                                    (x0 + 0.8, y, 1.4), (x0 + 1.3, y, 0.15), (x0 + 2.4, y + 0.3, 0.02),
                                    (x0 + 5.5, y + 1.2, 0.0), (x0 + 6.5, y + 1.5, -0.3)], 0.08, 6 if d >= 2 else 4)
    if d >= 2:
        hull.railing(parts["Machinery"], [(5.1, sy0 + 0.1, top), (5.1, sy1 - 0.1, top), (-5.1, sy1 - 0.1, top),
                                          (-5.1, sy0 + 0.1, top)], 1.0, closed=True)
        _rail_rect(parts, 4.0, y_fc + 2.1, y_front - 1.1, croof + 0.16, 0.9, d)
        for y in (sy0 + 3.0, sy1 - 3.0):
            hull.liferaft(sup, parts["Machinery"], (-4.4, y, top + 0.4), axis_x=False)
        _doors(parts, 5.2, [sy0 + 2.0, sy1 - 2.0], main, d)
        _doors(parts, -5.2, [sy0 + 2.0, sy1 - 2.0], main, d, facing=-1.0)
        for x in (-1.2, 1.2):
            hull.bollard_pair(parts["Machinery"], (x * 4.5, y_front + 2.0, fz))
        hull.winch(parts["Machinery"], parts["Rubber"], (0.0, y_front + 3.4, fz + 0.5), width=2.4)
    return spec


# --------------------------------------------------------------------------
# Wellboat: Ronja-class live-fish carrier
# --------------------------------------------------------------------------

def wellboat(parts: Parts, d: int, L=76.96, B=17.8, T=4.6, tanks=2, tiers=2):
    """Forward superstructure (accommodation tiers + wheelhouse with
    bridge wings), a shelter-deck pump house behind it, the well hatches
    amidships, four cranes, an aft control house and hose reels."""
    main, fc_u, fc_h = 1.3, 0.6, 2.8
    k = L / 76.96

    def deck_z(u):
        if u <= fc_u - 0.004:
            return main
        if u >= fc_u + 0.004:
            t = (u - fc_u) / (1 - fc_u)
            return main + fc_h + 1.4 * t ** 2.5
        return main + fc_h * (u - fc_u + 0.004) / 0.008

    spec = HullSpec(length=L, beam=B, draught=T, deck_z=deck_z, bulwark=1.3, bulwark_t=0.25,
                    bow_rake=0.085 * L, stern_rake=0.05 * L, rake_power=(1.8, 2.0), pmb=(0.2, 0.68),
                    bow_fullness=(1.7, 4.2), stern_frac=(0.22, 0.9), stern_power=2.2, bilge_r=0.13 * B,
                    camber=0.15)
    hull.loft_hull(spec, hull.stations(STATIONS[d], dense=(fc_u,)), LEVELS[d], parts["Hull"], parts["Deck"],
                   parts["Superstructure"])
    _fender_strake(parts, spec, main + 0.15, 0.12, fc_u - 0.01, d, r=0.22)
    sup, win = parts["Superstructure"], parts["Windows"]
    fz = main + fc_h
    y_fc = hull.hull_y(spec, fc_u + 0.006, fz)
    # Accommodation tiers stepping in, each with a deck-edge lip.
    x1 = B / 2 - 1.0
    y_back = y_fc + 0.9
    y_front = y_where(spec, fz, x1 + 0.5) - 0.3
    z = fz
    for t in range(tiers):
        xt = x1 - 0.5 * t
        ty0, ty1 = y_back + 0.6 * t, y_front - 1.4 * t
        _house_with_bands(parts, xt, ty0, ty1, z, 2.9, (0.42, 0.74), "fblr", d, mullion=1.5)
        _doors(parts, xt, [ty0 + 1.6], z, d)
        _doors(parts, -xt, [ty0 + 1.6], z, d, facing=-1.0)
        z += 2.9
        hull.slab(sup, xt + 0.35, ty0 - 0.35, ty1 + 0.35, z, 0.18)
        z += 0.18
    wy0, wy1 = y_back + 3.2 * k, y_front - 1.4 * tiers + 0.6
    wx = x1 - 0.5 * tiers - 0.2
    _house_with_bands(parts, wx, wy0, wy1, z, 3.0, (0.34, 0.93), "fblr", d, front_rake=0.7, mullion=1.0)
    # Bridge wings out to the ship's side, with railings.
    hull.slab(sup, B / 2 - 0.15, wy1 - 4.2 * k, wy1 - 0.3, z - 0.22, 0.22)
    _rail_rect(parts, B / 2 - 0.25, wy1 - 4.1 * k, wy1 - 0.4, z, 1.0, d)
    roof = z + 3.0
    _visor(parts, wx + 0.35, wy0 - 0.2, wy1 + 1.15, roof, d)
    roof += 0.16 if d else 0.1
    if d >= 1:
        hull.mast(sup, parts["Machinery"], parts["Rubber"], (0.0, wy0 + 2.2 * k, roof), 6.0 * k, radar=3.8,
                  detail=d >= 2)
        hull.cyl(sup, (0.0, wy1 - 0.8, roof), (0.0, wy1 - 0.8, roof + 0.9), 0.35, 10)
        _rail_rect(parts, wx, wy0, wy1 + 0.5, roof, 0.9, d)
    # Exhaust casing at the back of the top tier, black top, twin pipes.
    ex_y = y_back + 1.6 * k
    ez = fz + 3.08 * tiers
    hull.block(sup, (0.0, ex_y, ez + 2.0), 1.5 * k, 1.2 * k, 2.0)
    hull.block(parts["Rubber"], (0.0, ex_y, ez + 4.12), 1.52 * k, 1.22 * k, 0.12)
    if d >= 1:
        for x in (-0.55, 0.55):
            hull.cyl(parts["Rubber"], (x * k, ex_y, ez + 4.2), (x * k, ex_y, ez + 5.0), 0.22 * k, 8 if d >= 2 else 4)
    # Forecastle gear: windlass, bollards, foremast.
    if d >= 2:
        fy = y_front + 3.2 * k
        bz = deck_z(0.93)
        hull.winch(parts["Machinery"], parts["Rubber"], (0.0, fy, bz + 0.75), width=3.0)
        for x in (-3.5, 3.5):
            hull.bollard_pair(parts["Machinery"], (x, fy - 1.0, bz))
        hull.cyl(sup, (0.0, fy + 3.0 * k, bz), (0.0, fy + 3.0 * k, bz + 4.5), 0.12, 8)
        for side in (1.0, -1.0):
            for y in (y_back + 1.5, y_back + 3.2):
                hull.liferaft(sup, parts["Machinery"], (side * (x1 - 0.2), y, fz + 3.08 + 0.4), axis_x=False)
        # Rescue boat on its davit, starboard, on the first tier roof.
        rb = (x1 - 1.5, y_back + 6.5 * k, fz + 3.08 + 0.75)
        hull.block(parts["Safety"], rb, 0.95, 2.5, 0.42, skip_bottom=False)
        hull.block(parts["Superstructure"], (rb[0], rb[1] + 0.6, rb[2] + 0.55), 0.55, 0.7, 0.2)
        hull.cyl(parts["Machinery"], (x1 + 0.1, rb[1], fz + 3.08), (x1 + 0.1, rb[1], fz + 3.08 + 2.8), 0.12, 8)
    # Shelter-deck pump house on the main deck behind the forecastle.
    sh_y0 = y_fc - 10.0 * k
    hull.deckhouse(sup, win, B / 2 - 0.5, sh_y0, y_fc - 0.05, main, 2.7, band=(0.5, 0.75) if d >= 1 else None,
                   faces="lr", mullion=2.2 if d >= 2 else 0.0, inset=0.07 if d else 0.0)
    shz = main + 2.7
    if d >= 2:
        _rail_rect(parts, B / 2 - 0.6, sh_y0 + 0.1, y_fc - 0.2, shz, 1.0, d, closed=False)
        for x in (-2.0, 2.0):
            hull.block(parts["Machinery"], (x * k, sh_y0 + 5.0 * k, shz + 0.6), 1.2 * k, 1.6 * k, 0.6)
    # Wells: a hatch pair per tank on the main deck, manifold between.
    aft_y = -L / 2 + 9.5 * k
    tank_len = (sh_y0 - (aft_y + 9.0 * k)) / tanks
    for t in range(tanks):
        ty = sh_y0 - (t + 0.5) * tank_len
        for x in (-3.4 * k, 3.4 * k):
            hull.hatch(parts["Deck"], parts["Machinery"], (x, ty, main), 2.7 * k, min(3.6 * k, tank_len * 0.34),
                       coaming=0.7)
        if d >= 2:
            hull.tube(parts["Machinery"], [(-7.0 * k, ty + tank_len * 0.42, main + 0.55),
                                           (7.0 * k, ty + tank_len * 0.42, main + 0.55)], 0.3 * k, 10)
            hull.block(parts["Machinery"], (0.0, ty + tank_len * 0.42, main + 0.7), 0.8, 0.8, 0.7)
    # Cranes: two on the pump house roof, two aft.
    if d >= 1:
        for x, y, hd, r, bz in ((B / 2 - 2.5, sh_y0 + 2.6 * k, 200.0, 1.25, shz),
                                (-(B / 2 - 2.5), sh_y0 + 2.6 * k, 160.0, 1.25, shz),
                                (B / 2 - 2.2, aft_y + 1.6 * k, 20.0, 1.1, main),
                                (-(B / 2 - 2.2), aft_y + 1.6 * k, -20.0, 1.1, main)):
            hull.knuckle_crane(parts["Crane"], parts["Machinery"], parts["Rubber"], (x, y, bz), hd,
                               reach=r * k, sides=10 if d >= 2 else 6, detail=d >= 2)
    else:
        for x in (-(B / 2 - 2.5), B / 2 - 2.5):
            hull.block(parts["Crane"], (x, sh_y0 + 1.5, shz + 2.2), 0.7, 1.4, 2.2)
    # Pipe runs along both sides (loading and delousing lines), hose reels aft.
    if d >= 2:
        for side in (1.0, -1.0):
            x = side * (B / 2 - 1.2)
            hull.tube(parts["Machinery"], [(x, sh_y0 - 0.3, main + 0.45), (x, aft_y + 6.0 * k, main + 0.45),
                                           (x * 0.75, aft_y + 4.6 * k, main + 0.45)], 0.32 * k, 10)
            hull.winch(parts["Machinery"], parts["Rubber"], (side * 3.8 * k, aft_y - 1.8 * k, main + 0.95),
                       width=2.4 * k, r_drum=0.85 * k)
        for x in (-6.0 * k, 6.0 * k):
            hull.bollard_pair(parts["Machinery"], (x, -L / 2 + 2.4 * k, main))
            hull.capstan(parts["Machinery"], (x * 0.5, -L / 2 + 3.2 * k, main))
        _rail_rect(parts, 2.6 * k, aft_y + 3.8 * k, aft_y + 7.6 * k, main + 2.6, 0.9, d)
    # Aft control house overlooking the stern.
    hull.deckhouse(sup, win, 2.6 * k, aft_y + 3.8 * k, aft_y + 7.6 * k, main, 2.6,
                   band=(0.4, 0.9) if d >= 1 else None, faces="blr", mullion=1.0 if d >= 2 else 0.0,
                   inset=0.07 if d else 0.0)
    return spec


def wellboat_large(parts: Parts, d: int):
    """Ronja Storm class: 116 x 23 m, four wells, three accommodation tiers."""
    return wellboat(parts, d, L=116.0, B=23.0, T=6.2, tanks=4, tiers=3)


DESIGNS = {
    "workboat": workboat,
    "feed_barge": feed_barge,
    "feed_barge_large": feed_barge_large,
    "wellboat": wellboat,
    "wellboat_large": wellboat_large,
}
