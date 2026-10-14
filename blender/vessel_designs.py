"""Vessel designs for build_vessels.py: what goes where on each boat.

All dimensions in metres from public specifications (see build_vessels.py
for the references). Blender axes: X starboard, Y forward, Z up, z = 0 the
waterline. Each design fills a lk_hull.Parts with the parts
Hull, Deck, Superstructure, Windows, Machinery, Rubber, Crane, Safety.

`d` is the detail level: 2 = LOD0 (everything), 1 = LOD1 (no railings,
mullions or small gear), 0 = LOD2 (silhouette blocks).

The offshore fleet (farm-types spec §5.2, item F2-27) adds the DP
offshore service vessel with its walk-to-work gangway (part Gangway), the
crew transfer catamaran, and the subsea ROV and AUV; the anchor tug is the
service vessel's mesh with a red hull swatch (no geometry of its own).

Licence: part of the Lakskongen repository (proprietary); no third-party code.
"""

from __future__ import annotations

import math

import lk_hull as hull
import lk_meshkit as kit
from lk_hull import HullSpec, Parts
from lk_meshkit import TAU, v_add, v_cross, v_mul, v_norm, v_sub

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


# --------------------------------------------------------------------------
# RIB: 8.5 x 3.0 m work RIB (site tender)
# --------------------------------------------------------------------------

def rib(parts: Parts, d: int, L=8.5, B=3.0):
    """Deep-V hard hull (dark) inside an orange buoyancy tube, a centre
    console with windscreen and a radar arch, jockey seats, twin outboards.
    On a RIB the tube is the hull's outline: the Hull part is the tube (in
    the orange safety paint), the hard hull goes in the dark Rubber part."""
    hull_b, tube_r, deck = 2.1, 0.27, 0.62
    spec = HullSpec(length=L - 0.55, beam=hull_b, draught=0.5, deck_z=lambda u: deck + 0.25 * max(u - 0.55, 0.0) / 0.45,
                    bulwark=0.0, bow_rake=1.7, stern_rake=0.05, rake_power=(1.4, 1.0), pmb=(0.12, 0.42),
                    bow_fullness=(1.3, 2.0), stern_frac=(0.82, 0.95), stern_power=2.0, vee=0.42)
    st = hull.stations(STATIONS[d] // 2 + 4)
    hull.loft_hull(spec, st, LEVELS[d], parts["Rubber"], parts["Deck"], x_offset=0.0)
    # The tube: round the gunwale from the stern on one side, round the bow,
    # back to the stern on the other, its ends sticking out past the transom.
    n = 9 if d >= 2 else (6 if d >= 1 else 3)
    z = deck + 0.12
    side = []  # starboard, stern to the start of the bow arc
    for i in range(n + 1):
        u = 0.02 + 0.86 * i / n
        side.append((hull.hull_half_beam(spec, u, z) + tube_r * 0.55, hull.hull_y(spec, u, z),
                     z + 0.05 * max(u - 0.6, 0.0) / 0.28))
    # The bow: a half circle joining the two sides, its tip just ahead of
    # the stem.
    r_bow = side[-1][0]
    tip = hull.hull_y(spec, 1.0, z) + tube_r * 0.4
    yc = max(tip - r_bow, side[-1][1])
    k = 7 if d >= 2 else (5 if d >= 1 else 3)
    arc = [(r_bow * math.cos(math.pi * j / k), yc + r_bow * math.sin(math.pi * j / k), side[-1][2] + 0.02)
           for j in range(k + 1)]
    stern_y = -L / 2 + 0.02
    path = ([(-side[0][0], stern_y, z - 0.02)] + [(-p[0], p[1], p[2]) for p in reversed(side)]
            + list(reversed(arc))[1:-1] + side + [(side[0][0], stern_y, z - 0.02)])
    # Soften the corners (Chaikin corner cutting, ends kept).
    for _ in range(1 if d >= 1 else 0):
        cut = [path[0]]
        for a, b in zip(path[:-1], path[1:]):
            cut.append(hull._lerp(a, b, 0.25))
            cut.append(hull._lerp(a, b, 0.75))
        path = cut + [path[-1]]
    hull.tube(parts["Hull"], path, tube_r, 10 if d >= 2 else (7 if d >= 1 else 5))
    # Rubbing strake and lifeline loops on the tube (dark).
    if d >= 2:
        hull.tube(parts["Rubber"], [(p[0], p[1], p[2] - 0.14) for p in path[1:-1:2]], 0.04, 4, caps=False)
    # Centre console with a windscreen, radar arch and seats.
    cy = 0.2
    hull.block(parts["Superstructure"], (0.0, cy, deck + 0.55), 0.48, 0.42, 0.55)
    if d >= 1:
        gb, gm = parts["Windows"]
        a, b2 = (-0.46, cy + 0.42, deck + 1.1), (0.46, cy + 0.42, deck + 1.1)
        c, d2 = (0.42, cy + 0.12, deck + 1.55), (-0.42, cy + 0.12, deck + 1.55)
        gb.face((gb.vert(b2), gb.vert(a), gb.vert(d2), gb.vert(c)), gm, False)
        gb.face((gb.vert(a), gb.vert(b2), gb.vert(c), gb.vert(d2)), gm, False)
        for x in (-0.75, 0.75):
            hull.tube(parts["Machinery"], [(x, cy - 0.9, deck), (x, cy - 0.85, deck + 1.9), (x * 0.6, cy - 0.8, deck + 2.25)],
                      0.03, 6)
        hull.tube(parts["Machinery"], [(-0.45, cy - 0.8, deck + 2.25), (0.45, cy - 0.8, deck + 2.25)], 0.03, 6)
        hull.block(parts["Rubber"], (0.0, cy - 0.8, deck + 2.32), 0.4, 0.1, 0.07)
        for y in (cy - 0.9, cy - 1.7):
            hull.block(parts["Rubber"], (0.0, y, deck + 0.45), 0.22, 0.28, 0.45)
    # Twin outboards on the transom.
    for x in (-0.42, 0.42):
        ty = -spec.length / 2 - 0.2
        hull.block(parts["Rubber"], (x, ty, deck + 0.5), 0.24, 0.3, 0.42)
        if d >= 1:
            hull.block(parts["Machinery"], (x, ty + 0.03, deck - 0.3), 0.08, 0.14, 0.45)
            hull.cyl(parts["Rubber"], (x, ty + 0.03, -0.6), (x, ty - 0.15, -0.6), 0.12, 8)
    if d >= 2:
        hull.bollard_pair(parts["Machinery"], (0.0, L / 2 - 1.2, deck + 0.2), along=(1, 0, 0), r=0.06, h=0.18, gap=0.3)
        hull.cyl(parts["Machinery"], (0.0, -L / 2 + 0.8, deck), (0.0, -L / 2 + 0.8, deck + 1.1), 0.025, 4)
    return spec


# --------------------------------------------------------------------------
# Offshore fleet (farm-types spec §5.2, item F2-27)
# --------------------------------------------------------------------------

def _gangway(parts, base, d, length=26.0, heading=180.0, deck_z=0.0):
    """Motion-compensated walk-to-work gangway: a pedestal tower, a slewing
    head and a telescopic box-truss gangway stowed along the deck. Part
    `Gangway` at LOD0 (the node F2-22 slews); folded into the
    superstructure further out."""
    g = parts["Gangway"]
    bx, by, bz = base
    tower_top = bz + 7.5
    hull.cyl(g, (bx, by, bz), (bx, by, tower_top), 1.25, 16 if d >= 2 else 8)
    hull.cyl(g, (bx, by, tower_top), (bx, by, tower_top + 0.5), 1.7, 16 if d >= 2 else 8)
    hd = math.radians(heading)
    fwd = (math.sin(hd), math.cos(hd), 0.0)
    side = (math.cos(hd), -math.sin(hd), 0.0)
    head_c = (bx, by, tower_top + 1.6)
    mb, mat = g
    kit.box(mb, head_c, side, fwd, (0, 0, 1), 1.4, 1.6, 1.1, mat)
    # The gangway: two side trusses (top and bottom chords, posts) and a deck.
    z0 = tower_top + 1.2
    start = v_add(head_c, v_mul(fwd, 1.2))
    end = v_add(start, v_mul(fwd, length))
    hw, h = 0.75, 1.3
    for s in (-1, 1):
        o = v_mul(side, s * hw)
        a, b = v_add((start[0], start[1], z0), o), v_add((end[0], end[1], z0), o)
        hull.tube(g, [a, b], 0.09, 6 if d >= 2 else 4)
        hull.tube(g, [v_add(a, (0, 0, h)), v_add(b, (0, 0, h))], 0.08, 6 if d >= 2 else 4)
        if d >= 2:
            n = int(length / 1.6)
            for k in range(n + 1):
                p = hull._lerp(a, b, k / n)
                hull.cyl(g, p, v_add(p, (0, 0, h)), 0.05, 4, caps=False)
                if k < n:
                    q = hull._lerp(a, b, (k + 1) / n)
                    hull.cyl(g, p, v_add(q, (0, 0, h)), 0.04, 4, caps=False)
    deck_c = v_add(hull._lerp((start[0], start[1], z0), (end[0], end[1], z0), 0.5), (0, 0, -0.05))
    kit.box(mb, deck_c, side, fwd, (0, 0, 1), hw, length / 2, 0.08, mat)
    # Telescope joint and the landing foot at the tip.
    kit.box(mb, v_add(deck_c, v_mul(fwd, length * 0.12)), side, fwd, (0, 0, 1), hw + 0.1, 0.25, h * 0.6, mat)
    kit.box(mb, (end[0], end[1], z0 - 0.25), side, fwd, (0, 0, 1), hw + 0.15, 0.6, 0.18, mat)
    # Rest cradle on the deck under the tip.
    if deck_z:
        cr = hull._lerp(start, end, 0.86)
        hull.cyl(parts["Machinery"], (cr[0], cr[1], deck_z), (cr[0], cr[1], z0 - 0.15), 0.18, 8 if d >= 2 else 4)


def _a_frame(parts, y, z, width, height, d):
    """Stern A-frame (yellow) leaning aft."""
    c = parts["Crane"]
    for s in (-1, 1):
        a = (s * width / 2, y + 1.2, z)
        b = (s * width / 2 * 0.92, y - 1.6, z + height)
        hull.tube(c, [a, b], 0.38, 10 if d >= 2 else 5)
    hull.tube(c, [(-width / 2 * 0.92, y - 1.6, z + height), (width / 2 * 0.92, y - 1.6, z + height)], 0.42,
              10 if d >= 2 else 5)
    if d >= 2:
        for s in (-1, 1):
            hull.cyl(parts["Rubber"], (s * width / 2, y + 2.6, z), (s * width / 2 * 0.95, y + 0.4, z + height * 0.55),
                     0.16, 8)
        hull.block(parts["Machinery"], (0.0, y - 1.6, z + height - 0.6), 0.5, 0.3, 0.6)


def offshore_service(parts: Parts, d: int, L=75.0, B=17.0, T=6.0):
    """DP offshore service vessel (spec §5.2, ref O3's orange vessel): a
    forward superstructure with the bridge and wings, a motion-compensated
    walk-to-work gangway amidships to starboard, the long aft cargo deck
    with cargo rails, an offshore knuckle crane and a stern A-frame."""
    main, fc_u, fc_h = 2.5, 0.6, 2.7

    def deck_z(u):
        if u <= fc_u - 0.004:
            return main
        if u >= fc_u + 0.004:
            t = (u - fc_u) / (1 - fc_u)
            return main + fc_h + 1.2 * t ** 2.2
        return main + fc_h * (u - fc_u + 0.004) / 0.008

    spec = HullSpec(length=L, beam=B, draught=T, deck_z=deck_z, bulwark=1.3, bulwark_t=0.25,
                    bow_rake=0.11 * L, stern_rake=0.02 * L, rake_power=(1.6, 2.0), pmb=(0.12, 0.58),
                    bow_fullness=(1.9, 3.8), stern_frac=(0.78, 0.97), stern_power=2.6, bilge_r=0.11 * B,
                    camber=0.15)
    hull.loft_hull(spec, hull.stations(STATIONS[d], dense=(fc_u,)), LEVELS[d], parts["Hull"], parts["Deck"],
                   parts["Superstructure"])
    _fender_strake(parts, spec, main + 0.2, 0.05, fc_u - 0.01, d, r=0.24)
    sup = parts["Superstructure"]
    fz = main + fc_h
    y_fc = hull.hull_y(spec, fc_u + 0.006, fz)
    x1 = B / 2 - 0.9
    y_back = y_fc + 0.8
    y_front = y_where(spec, fz, x1 + 0.4) - 0.4
    # Accommodation: two tiers stepping in, then the bridge with a raked front.
    z = fz
    for t in range(2):
        xt = x1 - 0.6 * t
        ty0, ty1 = y_back + 0.5 * t, y_front - 1.2 * t
        _house_with_bands(parts, xt, ty0, ty1, z, 2.9, (0.42, 0.75), "fblr", d, mullion=1.5)
        _doors(parts, xt, [ty0 + 1.8], z, d)
        _doors(parts, -xt, [ty0 + 1.8], z, d, facing=-1.0)
        z += 2.9
        hull.slab(sup, xt + 0.3, ty0 - 0.3, ty1 + 0.3, z, 0.18)
        z += 0.18
    wx = x1 - 1.4
    wy0, wy1 = y_back + 2.2, y_front - 2.2
    _house_with_bands(parts, wx, wy0, wy1, z, 3.1, (0.3, 0.93), "fblr", d, front_rake=0.8, mullion=1.0)
    # Bridge wings out to the side, and an aft-facing bridge window band (DP
    # vessels steer from the aft bridge too).
    hull.slab(sup, B / 2 - 0.2, wy1 - 4.6, wy1 - 0.4, z - 0.22, 0.22)
    _rail_rect(parts, B / 2 - 0.3, wy1 - 4.5, wy1 - 0.5, z, 1.0, d)
    roof = z + 3.1
    _visor(parts, wx + 0.4, wy0 - 0.3, wy1 + 1.2, roof, d)
    roof += 0.16 if d else 0.1
    if d >= 1:
        hull.mast(sup, parts["Machinery"], parts["Rubber"], (0.0, wy0 + 2.4, roof), 6.0, radar=4.0,
                  detail=d >= 2)
        hull.cyl(sup, (2.2, wy0 + 1.0, roof), (2.2, wy0 + 1.0, roof + 1.1), 0.45, 12 if d >= 2 else 6)
        hull.cyl(sup, (-2.2, wy0 + 1.0, roof), (-2.2, wy0 + 1.0, roof + 0.9), 0.38, 12 if d >= 2 else 6)
        _rail_rect(parts, wx, wy0, wy1 + 0.5, roof, 0.9, d)
    # Twin exhaust casings behind the bridge, black tops.
    ez = fz + 2 * 3.08
    for s in (-1, 1):
        hull.block(sup, (s * 2.6, y_back + 1.2, ez + 2.4), 0.9, 1.0, 2.4)
        hull.block(parts["Rubber"], (s * 2.6, y_back + 1.2, ez + 4.9), 0.92, 1.02, 0.12)
    # Foredeck: windlass, bollards, foremast.
    if d >= 2:
        fy = y_front + 3.4
        bz = deck_z(0.94)
        hull.winch(parts["Machinery"], parts["Rubber"], (0.0, fy, bz + 0.75), width=3.2)
        for x in (-3.6, 3.6):
            hull.bollard_pair(parts["Machinery"], (x, fy - 1.0, bz))
        hull.cyl(sup, (0.0, fy + 3.0, bz), (0.0, fy + 3.0, bz + 4.0), 0.12, 8)
        for s in (1.0, -1.0):
            for y in (y_back + 1.5, y_back + 3.4):
                hull.liferaft(sup, parts["Machinery"], (s * (x1 - 0.2), y, fz + 3.08 + 0.4), axis_x=False)
        rb = (-(x1 - 1.6), y_back + 6.0, fz + 3.08 + 0.75)
        hull.block(parts["Safety"], rb, 0.95, 2.6, 0.42, skip_bottom=False)
        hull.block(parts["Superstructure"], (rb[0], rb[1] + 0.6, rb[2] + 0.55), 0.55, 0.7, 0.2)
    # Aft cargo deck: cargo rails along both sides, tugger winches, the
    # offshore crane to port, the A-frame at the stern.
    aft_y = -L / 2
    deck_y0, deck_y1 = aft_y + 3.5, y_fc - 1.0
    if d >= 1:
        for s in (1.0, -1.0):
            x = s * (B / 2 - 1.1)
            n = int((deck_y1 - deck_y0) / 3.0) if d >= 2 else 2
            for k in range(n + 1):
                y = deck_y0 + (deck_y1 - deck_y0) * k / n
                hull.block(parts["Machinery"], (x, y, main + 0.75), 0.12, 0.12, 0.75)
            hull.block(parts["Machinery"], (x, (deck_y0 + deck_y1) / 2, main + 1.5), 0.15, (deck_y1 - deck_y0) / 2, 0.1)
        hull.knuckle_crane(parts["Crane"], parts["Machinery"], parts["Rubber"], (-(B / 2 - 2.6), y_fc - 6.0, main),
                           200.0, reach=1.9, sides=10 if d >= 2 else 6, detail=d >= 2)
        _a_frame(parts, aft_y + 2.0, main, B * 0.62, 8.5, d)
    else:
        hull.block(parts["Crane"], (-(B / 2 - 2.6), y_fc - 6.0, main + 3.2), 0.9, 0.9, 3.2)
        hull.block(parts["Crane"], (0.0, aft_y + 1.0, main + 4.0), B * 0.3, 0.4, 4.0)
    if d >= 2:
        for x in (-3.0, 3.0):
            hull.winch(parts["Machinery"], parts["Rubber"], (x, y_fc - 3.0, main + 0.75), width=2.0)
        for x in (-6.0, 6.0):
            hull.bollard_pair(parts["Machinery"], (x, aft_y + 3.0, main))
        hull.cyl(parts["Rubber"], (-2.8, aft_y + 0.6, main + 0.3), (2.8, aft_y + 0.6, main + 0.3), 0.55, 14)
    # The walk-to-work gangway: tower to starboard just aft of the house,
    # gangway stowed aft along the cargo deck on a rest.
    _gangway(parts, (B / 2 - 2.8, y_fc - 2.6, main), d, length=26.0, heading=180.0, deck_z=main)
    return spec


def crew_boat(parts: Parts, d: int, L=20.0, B=7.0):
    """Crew transfer catamaran: white demihulls with orange fender strakes,
    a big bow fender for pushing onto a boat landing, a dark passenger cabin
    forward with the wheelhouse on top, an aft deck with a small crane and
    twin waterjets."""
    demi_b = 2.1
    cx = B / 2 - demi_b / 2
    deck = 1.75
    spec = HullSpec(length=L, beam=demi_b, draught=1.15, deck_z=lambda u: deck + 0.3 * max(u - 0.65, 0.0) / 0.35,
                    bulwark=0.0, bow_rake=3.2, stern_rake=0.12, rake_power=(1.6, 1.0), pmb=(0.1, 0.45),
                    bow_fullness=(1.5, 2.6), stern_frac=(0.7, 0.95), stern_power=2.0, vee=0.85)
    st = hull.stations({2: STATIONS[2] // 2 + 6, 1: 9, 0: 6}[d])
    for side in (1.0, -1.0):
        hull.loft_hull(spec, st, LEVELS[d], parts["Hull"], parts["Deck"], x_offset=side * cx)
    y_aft, y_fwd = -L / 2 + 0.1, L / 2 - 2.6
    wet = 1.15
    hb, hm = parts["Hull"]
    hull.kit.box(hb, (0.0, (y_aft + y_fwd) / 2, (wet + deck) / 2), (1, 0, 0), (0, 1, 0), (0, 0, 1),
                 B / 2, (y_fwd - y_aft) / 2, (deck - wet) / 2, hm)
    hull.slab(parts["Deck"], B / 2 - 0.1, y_aft + 0.1, y_fwd - 0.1, deck, 0.03)
    # Orange trim: a rubbing strake along each outer side; the bow fender
    # across both bows (the transfer push-on fender). Not at LOD2, where
    # they would fold into the hull's outline.
    stem = hull.hull_y(spec, 1.0, deck)
    if d >= 1:
        for side in (1.0, -1.0):
            hull.tube(parts["Safety"], [(side * (B / 2 + 0.06), y_aft + 0.4, deck - 0.15),
                                        (side * (B / 2 + 0.06), y_fwd - 0.2, deck - 0.15)], 0.13,
                      8 if d >= 2 else 4)
            hull.block(parts["Rubber"], (side * cx, stem - 0.25, 1.5), 0.55, 0.42, 0.75, skip_bottom=False)
        hull.tube(parts["Rubber"], [(-cx - 0.5, stem - 0.1, 2.05), (cx + 0.5, stem - 0.1, 2.05)], 0.28,
                  10 if d >= 2 else 6)
    # Passenger cabin with big windows, the wheelhouse on top.
    cy0, cy1, cz = -1.6, 6.2, deck
    _house_with_bands(parts, B / 2 - 0.55, cy0, cy1, cz, 2.3, (0.35, 0.86), "fblr", d, front_rake=0.45,
                      mullion=1.2)
    croof = cz + 2.3
    hull.slab(parts["Superstructure"], B / 2 - 0.4, cy0 - 0.2, cy1 + 0.6, croof, 0.12)
    _house_with_bands(parts, 1.7, 1.2, 4.6, croof + 0.12, 1.9, (0.3, 0.92), "fblr", d, front_rake=0.4,
                      mullion=0.9)
    wroof = croof + 0.12 + 1.9
    if d >= 1:
        _visor(parts, 1.85, 1.0, 5.2, wroof, d)
    if d >= 1:
        hull.mast(parts["Superstructure"], parts["Machinery"], parts["Rubber"], (0.0, 2.2, wroof + 0.16), 2.6,
                  radar=1.4, detail=d >= 2)
    # Aft deck: a small knuckle crane, rails, twin waterjets on the transoms.
    if d >= 1:
        hull.knuckle_crane(parts["Crane"], parts["Machinery"], parts["Rubber"], (-2.2, -6.4, deck), 30.0,
                           reach=0.5, sides=8 if d >= 2 else 5, detail=d >= 2)
    else:
        hull.block(parts["Crane"], (-2.2, -6.4, deck + 1.0), 0.3, 0.3, 1.0)
    if d >= 2:
        hull.railing(parts["Machinery"], [(B / 2 - 0.15, cy0 - 0.4, deck), (B / 2 - 0.15, y_aft + 0.3, deck),
                                          (-(B / 2 - 0.15), y_aft + 0.3, deck), (-(B / 2 - 0.15), cy0 - 0.4, deck)],
                     1.0)
        for x in (-2.6, 2.6):
            hull.bollard_pair(parts["Machinery"], (x, -8.6, deck), r=0.1, h=0.35, gap=0.4)
        hull.liferaft(parts["Superstructure"], parts["Machinery"], (1.2, 3.0, croof + 0.5))
    for side in (1.0, -1.0):
        ty = -L / 2 + 0.05
        hull.cyl(parts["Machinery"], (side * cx, ty + 0.4, 0.15), (side * cx, ty - 0.35, 0.15), 0.32,
                 10 if d >= 1 else 4)
    return spec


def _loft_y(part, prof, n, smooth=True, cap_tail=False):
    """Revolve a (radius, y) profile (nose first) about the Y axis into a
    part; cap_tail closes the last ring with a point."""
    mb, mat = part
    rows = [[mb.vert((rr * math.cos(TAU * i / n), y, rr * math.sin(TAU * i / n))) for i in range(n)]
            for rr, y in prof]
    for k in range(len(rows) - 1):
        for i in range(n):
            j = (i + 1) % n
            mb.face((rows[k][i], rows[k][j], rows[k + 1][j], rows[k + 1][i]), mat, smooth)
    if cap_tail:
        c = mb.vert((0.0, prof[-1][1] - 0.01, 0.0))
        for i in range(n):
            mb.face((rows[-1][i], rows[-1][(i + 1) % n], c), mat, smooth)


def rov(parts: Parts, d: int, L=2.6, W=1.6, H=1.5):
    """Work-class ROV (spec §5.2, ref O1): a yellow syntactic-foam buoyancy
    block (two blocks, a seam between) over a dark open frame on skids, four
    vectored horizontal thrusters in nozzles under the foam, two vertical
    ones through it, a camera and light bar and a bumper forward, a folded
    manipulator, grab rails, a lifting point and a sonar head on top.
    Origin at the centre; forward along +Y."""
    foam_z0, foam_z1 = 0.05, H / 2 - 0.1
    hb, hm = parts["Hull"]
    # Foam block with chamfered top edges.
    c = 0.14
    pts_bot = [(-W / 2, -L / 2, foam_z0), (W / 2, -L / 2, foam_z0), (W / 2, L / 2, foam_z0), (-W / 2, L / 2, foam_z0)]
    pts_mid = [(p[0], p[1], foam_z1 - c) for p in pts_bot]
    pts_top = [(-W / 2 + c, -L / 2 + c, foam_z1), (W / 2 - c, -L / 2 + c, foam_z1), (W / 2 - c, L / 2 - c, foam_z1),
               (-W / 2 + c, L / 2 - c, foam_z1)]
    vb = [hb.vert(p) for p in pts_bot]
    vm = [hb.vert(p) for p in pts_mid]
    vt = [hb.vert(p) for p in pts_top]
    for i in range(4):
        j = (i + 1) % 4
        hb.face((vb[i], vb[j], vm[j], vm[i]), hm, False)
        hb.face((vm[i], vm[j], vt[j], vt[i]), hm, False)
    hb.face((vt[0], vt[1], vt[2], vt[3]), hm, False)
    hb.face((vb[3], vb[2], vb[1], vb[0]), hm, False)
    dk = parts["Rubber"]
    sides = 10 if d >= 2 else 5
    # Dark frame: corner posts, skids, the lower deck.
    for x in (-W / 2 + 0.06, W / 2 - 0.06):
        if d >= 2:
            for y in (-L / 2 + 0.06, L / 2 - 0.06):
                hull.block(dk, (x, y, (-H / 2 + foam_z0) / 2), 0.05, 0.05, (foam_z0 + H / 2) / 2, skip_bottom=False)
        hull.block(dk, (x, 0.0, -H / 2 + 0.05), 0.06, L / 2, 0.05, skip_bottom=False)
    hull.block(dk, (0.0, 0.0, (-H / 2 + 0.1 + foam_z0) / 2 if d < 2 else -0.18), W / 2 - 0.08, L / 2 - 0.25,
               (foam_z0 + H / 2 - 0.1) / 2 if d < 2 else 0.04, skip_bottom=False)
    # Thrusters: four vectored at the corners (nozzles round the props), two
    # vertical through the foam.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p = (sx * (W / 2 - 0.3), sy * (L / 2 - 0.42), -0.32)
            a = v_norm((sx * 0.7, sy * 0.7, 0.0))
            if d >= 2:
                hull.torus(dk, p, a, 0.17, 0.04, 12, 4)
                hull.cyl(parts["Machinery"], v_sub(p, v_mul(a, 0.22)), p, 0.07, 8)
            else:
                hull.cyl(dk, v_sub(p, v_mul(a, 0.15)), v_add(p, v_mul(a, 0.15)), 0.17, sides)
    for sy in (-0.45, 0.45):
        hull.cyl(dk, (0.0, sy, foam_z1 - 0.05), (0.0, sy, foam_z1 + 0.04), 0.2, sides)
    if d < 2:
        return None
    # Foam seam, grab rails, lifting point, sonar head on top.
    hull.block(dk, (0.0, 0.0, (foam_z0 + foam_z1) / 2 + 0.01), W / 2 + 0.012, 0.02, (foam_z1 - foam_z0) / 2 - 0.02,
               skip_bottom=True)
    st = parts["Machinery"]
    for x in (-0.55, 0.55):
        hull.tube(st, [(x, -0.9, foam_z1), (x, -0.9, foam_z1 + 0.07), (x, -0.2, foam_z1 + 0.07),
                       (x, -0.2, foam_z1)], 0.025, 5, caps=False)
    hull.cyl(st, (0.0, 0.0, foam_z1), (0.0, 0.0, foam_z1 + 0.06), 0.09, 8)
    hull.cyl(dk, (0.45, L / 2 - 0.3, foam_z1), (0.45, L / 2 - 0.3, foam_z1 + 0.08), 0.09, 8)
    # Forward: bumper bar, a camera and light bar under the foam, the
    # manipulator folded on the right.
    hull.block(dk, (0.0, L / 2 + 0.04, -0.55), W / 2 - 0.05, 0.04, 0.05, skip_bottom=False)
    hull.block(st, (0.0, L / 2 - 0.08, -0.05), 0.55, 0.06, 0.05, skip_bottom=False)
    gl = parts["Windows"]
    hull.cyl(gl, (0.0, L / 2 - 0.12, -0.12), (0.0, L / 2 + 0.04, -0.12), 0.1, 10)
    for x in (-0.45, -0.25, 0.25, 0.45):
        hull.cyl(st, (x, L / 2 - 0.05, -0.05), (x, L / 2 + 0.03, -0.05), 0.05, 6)
        hull.cyl(gl, (x, L / 2 + 0.03, -0.05), (x, L / 2 + 0.045, -0.05), 0.04, 6)
    hull.tube(st, [(0.5, L / 2 - 0.5, -0.4), (0.5, L / 2 - 0.05, -0.4), (0.38, L / 2 + 0.12, -0.52),
                   (0.3, L / 2 + 0.1, -0.62)], 0.045, 6)
    hull.block(dk, (0.3, L / 2 + 0.08, -0.66), 0.05, 0.06, 0.05, skip_bottom=False)
    hull.tube(dk, [(0.0, -0.2, foam_z1), (0.0, -0.2, foam_z1 + 0.08)], 0.07, 6)
    return None


def auv(parts: Parts, d: int, L=4.0, D=0.5):
    """Autonomous underwater vehicle (spec §5.2, ref O1): an orange torpedo
    body with a dark nose cap and a dark sensor band, a tapered tail, four
    dark control fins in an X and a ducted propeller, a short antenna mast.
    Origin at the centre; forward along +Y."""
    r = D / 2
    n = 12 if d >= 2 else 8
    dk = parts["Rubber"]
    if d >= 2:
        nose = [(0.0, L / 2), (r * 0.55, L / 2 - 0.08)]
        body = [(r * 0.55, L / 2 - 0.08), (r * 0.85, L / 2 - 0.22), (r, L / 2 - 0.5), (r, 0.55), (r * 1.02, 0.55),
                (r * 1.02, 0.4), (r, 0.4),
                (r, -L / 2 + 0.9), (r * 0.7, -L / 2 + 0.35), (r * 0.42, -L / 2 + 0.05)]
        _loft_y(dk, nose, n)
        _loft_y(parts["Hull"], body[:4], n)
        _loft_y(dk, body[3:7], n)
        _loft_y(parts["Hull"], body[6:], n, cap_tail=True)
    else:
        _loft_y(parts["Hull"], [(0.0, L / 2), (r * 0.8, L / 2 - 0.2), (r, L / 2 - 0.5), (r, -L / 2 + 0.9),
                                (r * 0.42, -L / 2 + 0.05)], n, cap_tail=True)
    fy = -L / 2 + 0.55
    for a in range(4):
        th = TAU * a / 4 + TAU / 8
        e = (math.cos(th), 0.0, math.sin(th))
        root = v_mul(e, r * 0.75)
        tip = v_mul(e, r + 0.2)
        kit.box(dk[0], v_add((0.0, fy, 0.0), v_mul(v_add(root, tip), 0.5)), e, (0, 1, 0), v_norm(v_cross(e, (0, 1, 0))),
                (r + 0.2 - r * 0.75) / 2, 0.16, 0.012, dk[1])
    if d >= 2:
        hull.torus(dk, (0.0, -L / 2 - 0.02, 0.0), (0, 1, 0), r * 0.62, 0.035, 14, 4)
        hull.cyl(dk, (0.0, 0.6, r - 0.02), (0.0, 0.6, r + 0.22), 0.025, 6)
    return None

DESIGNS = {
    "workboat": workboat,
    "feed_barge": feed_barge,
    "feed_barge_large": feed_barge_large,
    "wellboat": wellboat,
    "wellboat_large": wellboat_large,
    "rib": rib,
    "offshore_service": offshore_service,
    "crew_boat": crew_boat,
    "rov": rov,
    "auv": auv,
}
