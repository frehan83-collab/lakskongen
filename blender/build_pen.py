"""Build the hero salmon pen (AKVA Polarcirkel 500-class) and export glTF LODs.

Run headless (reproducible, no UI):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/build_pen.py -- [--lod LOD0|LOD1|LOD2] [--no-bake]
      [--out DIR] [--textures]

Without --lod it builds all three LODs. Each LOD is written to
godot/assets/models/pen_cage/pen_cage_<LOD>.glb with a sidecar
pen_cage_<LOD>.json (tri_count, material_count, bbox_size, per-part tris and
the measured key dimensions) that blender_asset_pipeline.py --verify-only
and tests/test_pen_asset.py read. --textures also regenerates the tiling net
and walkway textures in godot/assets/models/pen_cage/textures/.

Reference (AKVA group product page "AKVA Polarcirkel 500RS", retrieved
2026-10-14): floating pipes 500 mm, 110 cm between pipe centres, walkway
1205 mm, brackets every 2.7 m, 160 mm uprights, 140 mm handrail, standard
circumference 160-260 m, net hooks on the uprights, bird-net pole sockets,
recessed sinker-tube suspension. Roadmap A2-01 adds: stanchions ~1.0 m,
bird-net poles 6-8 m, net 20-40 m deep with a cone bottom, lice-skirt
variant 6-15 m deep in #C9CCC6.

Two bird-net styles (owner references 2 and 4: a dark tent from a centre
mast; reference 5: a green net on blue poles round the rim, over a low
centre pole); a site shows one of them. The feed hose runs from the grey
rotor spreader on the centre float, over the collar on its hose guide, out
to the pen's hose port PORT_R from the centre, where the site's floating
hose from the feed barge takes over (Godot SiteLayout.PORT_R, roadmap G2-04).

Node names are the Godot API (PenCage.gd, FishPenManager.gd look them up):
Collar, Handrail, Walkway, Band, Net, BirdNet, BirdNetPoles, BirdNetRim,
BirdNetRimPoles, FeedPipe, SinkerTube, LiceSkirt. Material names map to
Godot .tres files through the .glb.import "use_external" settings:
  LK_HDPE          black HDPE #1C1E20 (pipes, brackets, uprights, poles, ropes)
  LK_Walkway       grey anti-slip decking
  LK_Net           submerged net (drawn before the water)
  LK_NetAbove      jump net above the waterline (drawn after the water)
  LK_BirdNet       bird net (tent)
  LK_BirdNetGreen  bird net on rim poles, rope/net green #2E8B57
  LK_PoleBlue      rim bird-net poles, pole blue #2F6DB5
  LK_Band          owner colour band (rival farms)
  LK_Skirt         lice skirt tarpaulin #C9CCC6
  LK_Marker        buoy-yellow floats #E8B92A
  LK_Spreader      the light-grey rotor spreader on the centre float
  LK_Hose          the white feed hose #EDEFEF (the site hoses' colour)

Licence: in-house geometry and textures, no third-party assets.
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
from lk_meshkit import TAU, polar  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(REPO, "godot", "assets", "models", "pen_cage")
TEX_DIR = os.path.join(OUT_DIR, "textures")
PREFIX = "LK_Pen"

# --------------------------------------------------------------------------
# Specification (metres). Pen origin = centre at the waterline.
# --------------------------------------------------------------------------

CIRCUMFERENCE = 160.0
R_IN = CIRCUMFERENCE / TAU          # inner pipe centreline, 25.465 m
PIPE_SPACING = 1.10                 # centre to centre
R_OUT = R_IN + PIPE_SPACING         # outer pipe centreline
PIPE_R = 0.25                       # 500 mm HDPE
PIPE_Z = 0.10                       # centreline above the waterline
N_BRACKETS = 60                     # 2.67 m spacing
BEAM_TOP = 0.46
BEAM_THICK = 0.08
BEAM_HALF_W = 0.11
BEAM_R0 = R_IN - 0.32
BEAM_R1 = R_OUT + 0.32
CLAMP_R = 0.30
CLAMP_HALF_LEN = 0.10
WALK_W = 1.205
WALK_MID = 0.5 * (R_IN + R_OUT)
WALK_R0 = WALK_MID - WALK_W / 2
WALK_R1 = WALK_MID + WALK_W / 2
WALK_THICK = 0.045
WALK_TOP = BEAM_TOP + WALK_THICK
R_STANCH = R_IN - 0.17              # uprights on the inner end of the bracket
STANCH_R = 0.08                     # 160 mm
RAIL_R = 0.07                       # 140 mm
RAIL_Z = WALK_TOP + 0.95            # handrail ~1.0 m above the deck
N_BIRD_POLES = 20
BIRD_POLE_R = 0.045
BIRD_POLE_TOP = 3.6
MAST_TOP = 7.5                      # central bird-net mast, 6-8 m spec
FLOAT_R = 0.9
NET_R = 24.95                       # net wall radius at the waterline
NET_WALL_DEPTH = 30.0
NET_CONE_DEPTH = 8.0                # cone bottom, tip at -38 m
SINKER_R = 25.20
SINKER_Z = -NET_WALL_DEPTH - 0.40
SINKER_PIPE_R = 0.17
SKIRT_R = 25.10
SKIRT_TOP = 1.0
SKIRT_DEPTH = 10.0                  # 6-15 m spec
HOSE_R = 0.055                      # 110 mm feed hose
PORT_R = 30.0                       # hose port: the site hose takes over here
# Rim bird net (reference 5): blue poles on the handrail uprights, leaning
# in, a green net from the handrail up over the pole tops and across, held
# up in the middle by a low centre pole.
N_RIM_POLES = 16
RIM_POLE_R = 0.05
RIM_POLE_TOP = 6.6                  # above the waterline, 6-8 m spec
RIM_LEAN = 0.6                      # the pole tops lean in this far
RIM_SCALLOP = 0.8                   # the net sags this much between poles
RIM_CENTRE_POLE_TOP = 5.4
RIM_ROOF_SAG = 0.45

# glTF material names -> (base colour hex, roughness, metallic, alpha)
MATERIALS = {
    "LK_HDPE": ("#1C1E20", 0.6, 0.0, 1.0),
    "LK_Walkway": ("#5E6468", 0.85, 0.0, 1.0),
    "LK_Net": ("#1A2124", 0.9, 0.0, 0.4),
    "LK_NetAbove": ("#1A2124", 0.9, 0.0, 0.4),
    "LK_BirdNet": ("#2A3033", 0.9, 0.0, 0.2),
    "LK_Band": ("#E8B92A", 0.5, 0.0, 1.0),
    "LK_Skirt": ("#C9CCC6", 0.8, 0.0, 1.0),
    "LK_Marker": ("#E8B92A", 0.45, 0.0, 1.0),
    "LK_BirdNetGreen": ("#2E8B57", 0.9, 0.0, 0.25),
    "LK_PoleBlue": ("#2F6DB5", 0.5, 0.0, 1.0),
    "LK_Spreader": ("#A9AFB2", 0.55, 0.0, 1.0),
    "LK_Hose": ("#EDEFEF", 0.42, 0.0, 1.0),
}

# Per-LOD level of detail. Every LOD carries every material, so the asset
# manifest's material count holds for all of them.
LODS = {
    "LOD0": dict(pipe_sides=16, ring_segs=240, rail_sides=10, rail_segs=240,
                 brackets="full", stanch_sides=10, walk="panels", band_rows=4,
                 net_around=96, net_wall_rows=10, net_cone_rows=7,
                 bird_around=96, bird_rows=12, pole_sides=8, sinker_sides=10,
                 sinker_segs=120, ropes=16, rope_sides=6, hose_sides=8,
                 hose_pts=110, skirt_around=180, skirt_rows=5, mooring_collars=True,
                 bollards=True, deadfish=True, rim_around=96, rim_side_rows=4,
                 rim_roof_rows=8, rim_poles=N_RIM_POLES, rim_pole_sides=8,
                 spreader_sides=20),
    "LOD1": dict(pipe_sides=8, ring_segs=120, rail_sides=6, rail_segs=120,
                 brackets="boxes", stanch_sides=4, walk="ring", band_rows=2,
                 net_around=48, net_wall_rows=5, net_cone_rows=3,
                 bird_around=48, bird_rows=6, pole_sides=4, sinker_sides=6,
                 sinker_segs=60, ropes=0, rope_sides=4, hose_sides=5,
                 hose_pts=36, skirt_around=96, skirt_rows=3, mooring_collars=False,
                 bollards=False, deadfish=True, rim_around=48, rim_side_rows=2,
                 rim_roof_rows=4, rim_poles=N_RIM_POLES, rim_pole_sides=4,
                 spreader_sides=10),
    "LOD2": dict(pipe_sides=4, ring_segs=40, rail_sides=3, rail_segs=40,
                 brackets="none", stanch_sides=0, walk="flat", band_rows=1,
                 net_around=24, net_wall_rows=2, net_cone_rows=1,
                 bird_around=16, bird_rows=2, pole_sides=0, sinker_sides=3,
                 sinker_segs=12, ropes=0, rope_sides=0, hose_sides=3,
                 hose_pts=6, skirt_around=24, skirt_rows=1, mooring_collars=False,
                 bollards=False, deadfish=False, rim_around=16, rim_side_rows=1,
                 rim_roof_rows=2, rim_poles=8, rim_pole_sides=3,
                 spreader_sides=6),
}

TRI_LIMITS = {"LOD0": 60000, "LOD2": 2000}


def bracket_angle(i: int) -> float:
    return TAU * (i + 0.5) / N_BRACKETS


def radial_axes(th: float):
    """(radial, tangential, up) unit vectors at angle th."""
    return (math.cos(th), math.sin(th), 0.0), (-math.sin(th), math.cos(th), 0.0), (0.0, 0.0, 1.0)


# --------------------------------------------------------------------------
# Parts
# --------------------------------------------------------------------------

def build_collar(p) -> kit.MeshBuilder:
    mb = kit.MeshBuilder()
    for R in (R_IN, R_OUT):
        kit.ring_tube(mb, R, PIPE_Z, PIPE_R, p["ring_segs"], p["pipe_sides"], "LK_HDPE")
    for i in range(N_BRACKETS if p["brackets"] != "none" else 0):
        th = bracket_angle(i)
        rad, tan, up = radial_axes(th)
        beam_mid = 0.5 * (BEAM_R0 + BEAM_R1)
        center = polar(beam_mid, th, BEAM_TOP - BEAM_THICK / 2)
        if p["brackets"] == "full":
            kit.box(mb, center, rad, tan, up, (BEAM_R1 - BEAM_R0) / 2, BEAM_HALF_W, BEAM_THICK / 2, "LK_HDPE")
            for R in (R_IN, R_OUT):
                c = polar(R, th, PIPE_Z)
                kit.cylinder(mb, kit.v_sub(c, kit.v_mul(tan, CLAMP_HALF_LEN)),
                             kit.v_add(c, kit.v_mul(tan, CLAMP_HALF_LEN)), CLAMP_R, 12, "LK_HDPE")
                # Web from the clamp up to the beam.
                web_c = polar(R, th, 0.5 * (PIPE_Z + CLAMP_R + BEAM_TOP - BEAM_THICK))
                web_h = max((BEAM_TOP - BEAM_THICK) - (PIPE_Z + CLAMP_R * 0.7), 0.02) / 2 + 0.03
                kit.box(mb, web_c, rad, tan, up, 0.16, CLAMP_HALF_LEN * 0.9, web_h, "LK_HDPE", skip_bottom=True)
        else:  # boxes: one beam and two blocky clamps
            kit.box(mb, center, rad, tan, up, (BEAM_R1 - BEAM_R0) / 2, BEAM_HALF_W, BEAM_THICK / 2,
                    "LK_HDPE", skip_bottom=True)
            for R in (R_IN, R_OUT):
                kit.box(mb, polar(R, th, PIPE_Z + 0.05), rad, tan, up, CLAMP_R, CLAMP_HALF_LEN,
                        CLAMP_R * 0.85, "LK_HDPE", skip_bottom=True)
    if p["mooring_collars"]:
        # Mooring collars on the outer pipe: the 12 bridle attachment points.
        for k in range(12):
            th = TAU * k / 12
            rad, tan, up = radial_axes(th)
            c = polar(R_OUT, th, PIPE_Z)
            kit.cylinder(mb, kit.v_sub(c, kit.v_mul(tan, 0.14)), kit.v_add(c, kit.v_mul(tan, 0.14)),
                         PIPE_R + 0.055, 16, "LK_HDPE")
            kit.box(mb, polar(R_OUT + PIPE_R + 0.12, th, PIPE_Z), rad, tan, up, 0.10, 0.025, 0.09, "LK_HDPE")
    if p["bollards"]:
        for i in range(0, N_BRACKETS, 6):
            th = bracket_angle(i)
            base = polar(BEAM_R1 - 0.12, th, BEAM_TOP)
            top = polar(BEAM_R1 - 0.12, th, BEAM_TOP + 0.30)
            kit.cylinder(mb, base, top, 0.08, 10, "LK_HDPE")
            kit.cylinder(mb, top, kit.v_add(top, (0, 0, 0.04)), 0.12, 10, "LK_HDPE")
    return mb


def build_handrail(p) -> kit.MeshBuilder:
    mb = kit.MeshBuilder()
    kit.ring_tube(mb, R_STANCH, RAIL_Z, RAIL_R, p["rail_segs"], p["rail_sides"], "LK_HDPE")
    if p["stanch_sides"] <= 0:
        return mb
    for i in range(N_BRACKETS):
        th = bracket_angle(i)
        rad, tan, up = radial_axes(th)
        if p["brackets"] == "full":
            # Socket on the bracket, upright, and the T-sleeve round the rail.
            kit.cylinder(mb, polar(R_STANCH, th, BEAM_TOP - 0.02), polar(R_STANCH, th, BEAM_TOP + 0.28),
                         STANCH_R + 0.03, 8, "LK_HDPE")
            kit.cylinder(mb, polar(R_STANCH, th, BEAM_TOP), polar(R_STANCH, th, RAIL_Z - 0.04),
                         STANCH_R, p["stanch_sides"], "LK_HDPE", caps=False)
            c = polar(R_STANCH, th, RAIL_Z)
            kit.cylinder(mb, kit.v_sub(c, kit.v_mul(tan, 0.16)), kit.v_add(c, kit.v_mul(tan, 0.16)),
                         RAIL_R + 0.025, 8, "LK_HDPE")
        else:
            kit.cylinder(mb, polar(R_STANCH, th, BEAM_TOP), polar(R_STANCH, th, RAIL_Z),
                         STANCH_R, p["stanch_sides"], "LK_HDPE", caps=False, smooth=False)
    return mb


def build_walkway(p) -> kit.MeshBuilder:
    mb = kit.MeshBuilder()
    if p["walk"] == "panels":
        gap = 0.03 / R_IN
        for i in range(N_BRACKETS):
            th0 = bracket_angle(i) + gap
            th1 = bracket_angle(i + 1) - gap
            kit.annulus_sector(mb, WALK_R0, WALK_R1, th0, th1, WALK_TOP, 4, "LK_Walkway",
                               thickness=WALK_THICK, uv_scale=1.0)
    elif p["walk"] == "ring":
        kit.annulus_sector(mb, WALK_R0, WALK_R1, 0.0, TAU, WALK_TOP, p["ring_segs"], "LK_Walkway",
                           thickness=WALK_THICK, uv_scale=1.0)
    else:
        kit.annulus_sector(mb, WALK_R0, WALK_R1, 0.0, TAU, WALK_TOP, p["ring_segs"], "LK_Walkway",
                           uv_scale=1.0)
    return mb


def build_band(p) -> kit.MeshBuilder:
    """Owner colour stripe along the top of the outer pipe."""
    mb = kit.MeshBuilder()
    rows = p["band_rows"] + 1
    kit.ring_tube(mb, R_OUT, PIPE_Z, PIPE_R + 0.008, p["ring_segs"], rows, "LK_Band",
                  phi_range=(math.radians(35.0), math.radians(115.0)))
    return mb


def net_wobble(th: float, k: int, rows_total: int) -> float:
    """Gentle, deterministic irregularity: current pushes the wall in."""
    depth_t = min(k / max(rows_total - 1, 1), 1.0)
    return -0.35 * depth_t * (0.6 + 0.4 * math.sin(th * 3.0 + 0.7)) + 0.08 * math.sin(th * 7.0) * depth_t


def build_net(p) -> kit.MeshBuilder:
    mb = kit.MeshBuilder()
    profile = [(R_STANCH - 0.08, RAIL_Z - 0.05), (NET_R + 0.12, 0.6), (NET_R, 0.0)]
    for k in range(1, p["net_wall_rows"] + 1):
        profile.append((NET_R, -NET_WALL_DEPTH * k / p["net_wall_rows"]))
    for k in range(1, p["net_cone_rows"] + 1):
        t = k / p["net_cone_rows"]
        # Slightly bellied cone down to a small lift-up collector at the tip.
        r = NET_R * (1.0 - t) + 0.35 * t
        z = -NET_WALL_DEPTH - NET_CONE_DEPTH * (t ** 0.9)
        profile.append((r, z))
    rows = len(profile)
    kit.surface_of_revolution(
        mb, profile, p["net_around"], "LK_Net",
        split_mats=lambda zmid: "LK_NetAbove" if zmid > 0.0 else "LK_Net",
        wobble=lambda th, k: net_wobble(th, k, rows) if 3 <= k else 0.0)
    return mb


def build_bird_net(p) -> kit.MeshBuilder:
    """Bird net: a shallow tent from the mast top to the rim poles, draped
    down to the handrail, scalloped between the poles."""
    mb = kit.MeshBuilder()
    n_around, n_rows = p["bird_around"], p["bird_rows"]
    rim_r = R_STANCH + 0.03

    def rim_z(th):
        # Sags between poles (N_BIRD_POLES peaks).
        s = abs(math.sin(th * N_BIRD_POLES / 2.0))
        return BIRD_POLE_TOP - 0.45 * s

    grid = []
    for i in range(n_around):
        th = TAU * i / n_around
        row = []
        for k in range(n_rows + 1):
            t = k / n_rows
            r = 0.12 + (rim_r - 0.12) * t
            z_lin = (MAST_TOP - 0.1) * (1 - t) + rim_z(th) * t
            z = z_lin - 1.1 * math.sin(math.pi * t)
            row.append(mb.vert(polar(r, th, z)))
        # Drape: from the rim down to the handrail.
        row.append(mb.vert(polar(rim_r, th, RAIL_Z + 0.05)))
        grid.append(row)
    for i in range(n_around):
        i2 = (i + 1) % n_around
        u0 = rim_r * TAU * i / n_around
        u1 = rim_r * TAU * (i + 1) / n_around
        for k in range(n_rows + 1):
            v0 = k * rim_r / n_rows
            v1 = (k + 1) * rim_r / n_rows
            mb.face((grid[i][k], grid[i][k + 1], grid[i2][k + 1], grid[i2][k]), "LK_BirdNet", True,
                    [(u0, v0), (u0, v1), (u1, v1), (u1, v0)])
    return mb


def build_bird_poles(p) -> kit.MeshBuilder:
    mb = kit.MeshBuilder()
    # Central float (HDPE drum with rounded edges) and the mast.
    float_profile = [(0.0, 0.42), (FLOAT_R * 0.8, 0.40), (FLOAT_R, 0.30), (FLOAT_R, -0.45),
                     (FLOAT_R * 0.8, -0.58), (0.0, -0.60)]
    if p["pole_sides"] <= 0:
        float_profile = [(0.0, 0.40), (FLOAT_R, 0.35), (FLOAT_R, -0.5), (0.0, -0.55)]
    kit.surface_of_revolution(mb, float_profile, 24 if p["pole_sides"] > 0 else 8, "LK_HDPE")
    kit.cylinder(mb, (0, 0, 0.35), (0, 0, MAST_TOP), 0.07, max(p["pole_sides"], 4), "LK_HDPE")
    if p["pole_sides"] > 0:
        for k in range(N_BIRD_POLES):
            th = TAU * (k * 3 + 0.5) / N_BRACKETS
            kit.cylinder(mb, polar(R_STANCH + 0.11, th, BEAM_TOP), polar(R_STANCH + 0.11, th, BIRD_POLE_TOP),
                         BIRD_POLE_R, p["pole_sides"], "LK_HDPE")
    return mb


def build_bird_net_rim(p) -> kit.MeshBuilder:
    """Rim bird net (reference 5): from the handrail up the outside of the
    blue poles, scalloped between their tops, then across as a roof that
    sags a little on its way to the low centre pole."""
    mb = kit.MeshBuilder()
    n_around = p["rim_around"]
    side_rows, roof_rows = p["rim_side_rows"], p["rim_roof_rows"]
    base_r = R_STANCH + 0.03
    top_r = R_STANCH + 0.11 - RIM_LEAN

    def rim_z(th):
        s = abs(math.sin(th * N_RIM_POLES / 2.0))
        return RIM_POLE_TOP - RIM_SCALLOP * s

    grid = []
    for i in range(n_around):
        th = TAU * (i + 0.5) / n_around
        top = rim_z(th - TAU * 0.5 / N_RIM_POLES)
        row = []
        for k in range(side_rows + 1):
            t = k / side_rows
            r = base_r + (top_r - base_r) * t + 0.25 * math.sin(math.pi * t)
            row.append(mb.vert(polar(r, th, (RAIL_Z + 0.05) * (1 - t) + top * t)))
        for k in range(1, roof_rows + 1):
            t = k / roof_rows
            r = top_r + (0.15 - top_r) * t
            z = top * (1 - t) + RIM_CENTRE_POLE_TOP * t - RIM_ROOF_SAG * math.sin(math.pi * t)
            row.append(mb.vert(polar(r, th, z)))
        grid.append(row)
    rows = side_rows + roof_rows + 1
    for i in range(n_around):
        i2 = (i + 1) % n_around
        u0 = base_r * TAU * i / n_around
        u1 = base_r * TAU * (i + 1) / n_around
        for k in range(rows - 1):
            v0 = k * 2.0
            v1 = (k + 1) * 2.0
            mb.face((grid[i][k], grid[i][k + 1], grid[i2][k + 1], grid[i2][k]), "LK_BirdNetGreen", True,
                    [(u0, v0), (u0, v1), (u1, v1), (u1, v0)])
    return mb


def build_bird_rim_poles(p) -> kit.MeshBuilder:
    """Blue rim poles on the handrail uprights, the centre float and its low
    blue pole."""
    mb = kit.MeshBuilder()
    sides = p["rim_pole_sides"]
    float_profile = [(0.0, 0.42), (FLOAT_R * 0.8, 0.40), (FLOAT_R, 0.30), (FLOAT_R, -0.45),
                     (FLOAT_R * 0.8, -0.58), (0.0, -0.60)]
    if sides <= 3:
        float_profile = [(0.0, 0.40), (FLOAT_R, 0.35), (FLOAT_R, -0.5), (0.0, -0.55)]
    kit.surface_of_revolution(mb, float_profile, 24 if sides > 4 else 6, "LK_HDPE")
    kit.cylinder(mb, (0, 0, 0.35), (0, 0, RIM_CENTRE_POLE_TOP), 0.06, max(sides, 3), "LK_PoleBlue")
    # Between the hose guide (at angle 0) and the poles: half a pitch off.
    for k in range(p["rim_poles"]):
        th = TAU * (k + 0.5) / p["rim_poles"]
        kit.cylinder(mb, polar(R_STANCH + 0.11, th, BEAM_TOP),
                     polar(R_STANCH + 0.11 - RIM_LEAN, th, RIM_POLE_TOP), RIM_POLE_R, max(sides, 3), "LK_PoleBlue")
    return mb


def hose_path(n_pts: int):
    """Feed hose: centre spreader -> across the pen on the water -> over the
    collar on a hose guide -> out on the water to the hose port, where the
    site's floating hose from the feed barge takes over."""
    key = [
        (FLOAT_R + 0.1, 0.0, 0.35), (4.0, 0.25, 0.02), (12.0, 0.9, 0.02), (20.0, 0.4, 0.02),
        (NET_R - 0.4, 0.0, 0.03), (R_STANCH - 0.25, 0.0, RAIL_Z - 0.2), (R_STANCH, 0.0, RAIL_Z + 0.12),
        (R_OUT, 0.0, RAIL_Z - 0.15), (R_OUT + 0.9, 0.0, 0.25), (PORT_R - 1.2, 0.0, 0.03), (PORT_R, 0.0, 0.03),
    ]
    return smooth_path(key, n_pts)


def smooth_path(key, n_pts: int):
    """A Catmull-Rom spline through the key points, resampled evenly by arc
    length (hoses bend smoothly instead of kinking at the key points)."""
    def cr(p0, p1, p2, p3, t):
        return tuple(0.5 * ((2 * p1[k]) + (-p0[k] + p2[k]) * t + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t * t
                            + (-p0[k] + 3 * p1[k] - 3 * p2[k] + p3[k]) * t ** 3) for k in range(3))
    dense = []
    pts = [key[0]] + key + [key[-1]]
    for i in range(1, len(pts) - 2):
        for s in range(12):
            dense.append(cr(pts[i - 1], pts[i], pts[i + 1], pts[i + 2], s / 12))
    dense.append(key[-1])
    lengths = [0.0]
    for i in range(1, len(dense)):
        lengths.append(lengths[-1] + kit.v_len(kit.v_sub(dense[i], dense[i - 1])))
    out = []
    for j in range(n_pts):
        target = lengths[-1] * j / (n_pts - 1)
        k = 1
        while k < len(lengths) - 1 and lengths[k] < target:
            k += 1
        a, b = dense[k - 1], dense[k]
        seg = lengths[k] - lengths[k - 1]
        t = 0.0 if seg <= 0 else (target - lengths[k - 1]) / seg
        out.append(kit.v_add(a, kit.v_mul(kit.v_sub(b, a), t)))
    return out


def build_feed(p) -> kit.MeshBuilder:
    mb = kit.MeshBuilder()
    kit.path_tube(mb, hose_path(p["hose_pts"]), HOSE_R, p["hose_sides"], "LK_Hose", caps=False)
    # The rotor spreader on the centre float (reference 5): a light-grey
    # hopper cone on a neck, with a yellow ring round its rim.
    spreader = [(0.0, 1.30), (0.50, 1.28), (0.62, 1.20), (0.62, 1.02), (0.30, 0.66), (0.20, 0.55),
                (0.20, 0.42), (0.0, 0.42)]
    kit.surface_of_revolution(mb, spreader, p["spreader_sides"], "LK_Spreader")
    ring = [(0.0, 1.31), (0.64, 1.31), (0.66, 1.25), (0.0, 1.25)]
    kit.surface_of_revolution(mb, ring, p["spreader_sides"], "LK_Marker")
    return mb


def build_sinker(p) -> kit.MeshBuilder:
    mb = kit.MeshBuilder()
    kit.ring_tube(mb, SINKER_R, SINKER_Z, SINKER_PIPE_R, p["sinker_segs"], p["sinker_sides"], "LK_HDPE")
    for k in range(p["ropes"]):
        th = TAU * (k + 0.25) / p["ropes"]
        kit.cylinder(mb, polar(SINKER_R, th, PIPE_Z), polar(SINKER_R, th, SINKER_Z + SINKER_PIPE_R),
                     0.025, p["rope_sides"], "LK_HDPE", caps=False, smooth=False)
    if p["deadfish"]:
        tip = -NET_WALL_DEPTH - NET_CONE_DEPTH
        th = math.radians(200.0)
        kit.cylinder(mb, (0, 0, tip - 0.5), (0, 0, tip + 0.4), 0.35, 10 if p["hose_sides"] >= 8 else 5, "LK_HDPE")
        # Dead-fish lift hose: from the collector at the cone tip, up along
        # the net and over the collar.
        key = [(0.0, 0.0, tip + 0.4), polar(8.0, th, tip + 1.5), polar(18.0, th, -20.0),
               polar(23.5, th, -6.0), polar(NET_R - 0.3, th, -0.6), polar(NET_R - 0.15, th, 0.3),
               polar(R_STANCH - 0.2, th, RAIL_Z - 0.1)]
        kit.path_tube(mb, smooth_path(key, 40 if p["hose_sides"] >= 8 else 12), 0.06, p["hose_sides"],
                      "LK_HDPE", caps=False)
    return mb


def build_skirt(p) -> kit.MeshBuilder:
    mb = kit.MeshBuilder()
    n_rows = p["skirt_rows"]
    profile = [(SKIRT_R, SKIRT_TOP)]
    for k in range(1, n_rows + 1):
        profile.append((SKIRT_R, SKIRT_TOP - (SKIRT_TOP + SKIRT_DEPTH) * k / n_rows))
    folds = 90 if p["skirt_around"] >= 180 else 0

    def wobble(th, k):
        fold = 0.06 * math.sin(th * folds) if folds else 0.0
        # The skirt hangs in a little at the bottom, unevenly.
        t = k / n_rows
        return fold - 0.25 * t * t * (0.6 + 0.4 * math.sin(th * 2.0 + 1.3))

    kit.surface_of_revolution(mb, profile, p["skirt_around"], "LK_Skirt", wobble=wobble)
    return mb


PARTS = [
    ("Collar", build_collar),
    ("Handrail", build_handrail),
    ("Walkway", build_walkway),
    ("Band", build_band),
    ("Net", build_net),
    ("BirdNet", build_bird_net),
    ("BirdNetPoles", build_bird_poles),
    ("BirdNetRim", build_bird_net_rim),
    ("BirdNetRimPoles", build_bird_rim_poles),
    ("FeedPipe", build_feed),
    ("SinkerTube", build_sinker),
    ("LiceSkirt", build_skirt),
]

# Parts that get baked AO in the vertex colour (the rest use no AO).
AO_PARTS = {"Collar", "Handrail", "Walkway", "Band", "BirdNetPoles", "BirdNetRimPoles", "FeedPipe"}


# --------------------------------------------------------------------------
# Textures (tiling, generated in code)
# --------------------------------------------------------------------------

def _save_image(name, path, rgba_rows, size_x, size_y, non_color=False):
    import numpy as np
    img = bpy.data.images.new(name, size_x, size_y, alpha=True, float_buffer=False)
    if non_color:
        img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(np.asarray(rgba_rows, dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def build_textures() -> list[str]:
    """Net mesh alpha tile and walkway decking albedo/normal tiles."""
    import numpy as np
    os.makedirs(TEX_DIR, exist_ok=True)
    written = []
    # Net: knotless diamond mesh, 2x2 diamonds per tile, 4x4 supersampled.
    n, ss = 256, 4
    g = (np.arange(n * ss) + 0.5) / (n * ss)
    x, y = np.meshgrid(g, g)
    d1 = np.abs(np.mod((x + y) * 2.0, 1.0) - 0.5) * 2.0
    d2 = np.abs(np.mod((x - y) * 2.0, 1.0) - 0.5) * 2.0
    thread = 0.075
    cover = np.maximum(d1 < thread, d2 < thread).astype(np.float32)
    # Knots: slightly thicker where the thread families cross.
    knot = (d1 < thread * 1.8) & (d2 < thread * 1.8)
    cover = np.maximum(cover, knot.astype(np.float32))
    cover = cover.reshape(n, ss, n, ss).mean(axis=(1, 3))
    rgba = np.ones((n, n, 4), dtype=np.float32)
    rgba[..., 3] = cover
    path = os.path.join(TEX_DIR, "net_mesh.png")
    _save_image("LK_net_mesh", path, rgba[::-1], n, n)
    written.append(path)

    # Walkway decking, 1 m x 1 m tile: anti-slip ribs across the walkway,
    # panel seams, low-frequency grime and wear.
    n = 256
    rng = np.random.default_rng(500)
    u = (np.arange(n) + 0.5) / n
    uu, vv = np.meshgrid(u, u)
    ribs = 0.5 + 0.5 * np.cos(uu * TAU * 40.0)        # 25 mm ribs
    seam = np.exp(-((np.mod(vv * 4.0, 1.0) - 0.5) / 0.03) ** 2)  # plank seams every 0.25 m
    height = 0.6 * ribs - 1.2 * seam

    def tiled_noise(cells, seed):
        r = np.random.default_rng(seed).random((cells, cells))
        idx = (np.arange(n) * cells / n)
        i0 = np.floor(idx).astype(int) % cells
        i1 = (i0 + 1) % cells
        f = idx - np.floor(idx)
        f = f * f * (3 - 2 * f)
        a = r[i0][:, i0] * (1 - f)[None, :] + r[i0][:, i1] * f[None, :]
        b = r[i1][:, i0] * (1 - f)[None, :] + r[i1][:, i1] * f[None, :]
        return a * (1 - f)[:, None] + b * f[:, None]

    grime = 0.6 * tiled_noise(4, 11) + 0.4 * tiled_noise(16, 12)
    height = height + 0.3 * tiled_noise(32, 13)
    # Normal from the tiling height field (central differences with wrap).
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    strength = 1.6
    nx, ny, nz = -dx * strength, -dy * strength, np.ones_like(dx)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    normal = np.stack([nx / ln * 0.5 + 0.5, ny / ln * 0.5 + 0.5, nz / ln * 0.5 + 0.5, np.ones_like(dx)], axis=-1)
    path = os.path.join(TEX_DIR, "walkway_normal.png")
    _save_image("LK_walkway_normal", path, normal[::-1], n, n, non_color=True)
    written.append(path)
    base = np.array([0.37, 0.39, 0.41])            # #5E6468 in sRGB
    shade = 0.82 + 0.22 * grime - 0.18 * seam + 0.05 * (ribs - 0.5)
    speck = (rng.random((n, n)) > 0.9985).astype(np.float32) * 0.5
    albedo = np.clip(base[None, None, :] * shade[..., None] + speck[..., None], 0, 1)
    rgba = np.concatenate([albedo, np.ones((n, n, 1))], axis=-1)
    path = os.path.join(TEX_DIR, "walkway_albedo.png")
    _save_image("LK_walkway_albedo", path, rgba[::-1], n, n)
    written.append(path)
    return written


# --------------------------------------------------------------------------
# Build, bake, export
# --------------------------------------------------------------------------

def materials() -> dict:
    out = {}
    for name, (hexcode, rough, metal, alpha) in MATERIALS.items():
        out[name] = kit.make_material(name, kit.hex_rgba(hexcode, alpha), rough, metal, alpha)
    return out


def build_lod(lod: str, mats: dict) -> list:
    p = LODS[lod]
    objs = []
    for name, fn in PARTS:
        mb = fn(p)
        obj = mb.build(name, mats)
        obj["lk_build"] = PREFIX
        objs.append(obj)
    return objs


def bake_ao(objs) -> None:
    """Cycles AO bake into a per-vertex colour (COLOR_0 in the glTF)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    if scene.world is None:
        scene.world = bpy.data.worlds.new("LK_World")
    scene.world.light_settings.distance = 1.2
    for obj in objs:
        attr = obj.data.color_attributes.new("AO", "BYTE_COLOR", "POINT")
        obj.data.color_attributes.active_color = attr
        obj.data.color_attributes.render_color_index = obj.data.color_attributes.active_color_index
    # Only the solid pen parts occlude: the nets and skirt (see-through or
    # optional) and anything else in the scene (the factory cube, or the
    # user's own objects in a live session) are hidden from the bake and
    # restored afterwards.
    targets = [o for o in objs if o.name.split(".")[0] in AO_PARTS]
    others = [o for o in scene.objects if o not in targets and not o.hide_render]
    for obj in others:
        obj.hide_render = True
    bpy.ops.object.select_all(action="DESELECT")
    for obj in targets:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    try:
        bpy.ops.object.bake(type="AO", target="VERTEX_COLORS", use_selected_to_active=False)
    finally:
        for obj in others:
            obj.hide_render = False
    # Non-AO parts keep white (no occlusion).
    for obj in objs:
        if obj not in targets:
            attr = obj.data.color_attributes["AO"]
            for d in attr.data:
                d.color = (1.0, 1.0, 1.0, 1.0)


def export_lod(lod: str, objs, out_dir: str) -> dict:
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objs:
        obj.select_set(True)
    path = os.path.join(out_dir, f"pen_cage_{lod}.glb")
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=False,
        export_yup=True,
        export_texcoords=True,
        export_normals=True,
        export_materials="EXPORT",
        export_vertex_color="ACTIVE",
        export_all_vertex_colors=False,
        export_cameras=False,
        export_lights=False,
        export_extras=False,
    )
    tris = {o.name.split(".")[0]: kit.mesh_tris(o) for o in objs}
    used = sorted({slot.material.name for o in objs for slot in o.material_slots if slot.material})
    lo, hi = kit.world_bbox(objs)
    collar = [o for o in objs if o.name.split(".")[0] == "Collar"]
    clo, chi = kit.world_bbox(collar)
    net = [o for o in objs if o.name.split(".")[0] == "Net"]
    nlo, _ = kit.world_bbox(net)
    # Blender Z-up -> glTF/Godot Y-up: size (x, z, y).
    bbox_godot = [round(hi[0] - lo[0], 4), round(hi[2] - lo[2], 4), round(hi[1] - lo[1], 4)]
    data = {
        "asset": "pen_cage",
        "lod": lod,
        "tri_count": sum(tris.values()),
        "tri_count_without_skirt": sum(v for k, v in tris.items() if k != "LiceSkirt"),
        "material_count": len(used),
        "materials": used,
        "bbox_size": bbox_godot,
        "parts": tris,
        "dimensions": {
            "collar_outer_diameter_m": round(chi[0] - clo[0], 4),
            "inner_pipe_circumference_m": round(CIRCUMFERENCE, 4),
            "pipe_centre_spacing_m": PIPE_SPACING,
            "pipe_diameter_m": 2 * PIPE_R,
            "bracket_spacing_m": round(TAU * R_IN / N_BRACKETS, 4),
            "walkway_width_m": WALK_W,
            "handrail_height_above_deck_m": round(RAIL_Z - WALK_TOP, 4),
            "upright_diameter_m": 2 * STANCH_R,
            "handrail_diameter_m": 2 * RAIL_R,
            "bird_net_mast_m": MAST_TOP,
            "bird_net_rim_pole_m": RIM_POLE_TOP,
            "hose_port_m": PORT_R,
            "net_depth_m": round(-nlo[2], 4),
            "lice_skirt_depth_m": SKIRT_DEPTH,
        },
        "file": os.path.basename(path),
        "blender_version": bpy.app.version_string,
    }
    with open(os.path.join(out_dir, f"pen_cage_{lod}.json"), "w") as f:
        json.dump(data, f, indent=2, sort_keys=True)
        f.write("\n")
    print(f"TRIS: {data['tri_count']}")
    print(f"BBOX: {' '.join(str(v) for v in bbox_godot)}")
    print(f"MATERIALS: {len(used)}")
    print(f"PEN_EXPORT {lod} {path} tris={data['tri_count']} parts={tris}")
    limit = TRI_LIMITS.get(lod)
    if limit and data["tri_count"] > limit:
        raise SystemExit(f"{lod} has {data['tri_count']} triangles, over its {limit} budget")
    return data


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--lod", choices=list(LODS), default=None)
    ap.add_argument("--no-bake", action="store_true")
    ap.add_argument("--textures", action="store_true")
    ap.add_argument("--out", default=OUT_DIR)
    ap.add_argument("--variant", default=None)  # accepted for the pipeline CLI
    return ap.parse_args(argv)


def main():
    args = parse_args()
    os.makedirs(args.out, exist_ok=True)
    if args.textures:
        for path in build_textures():
            print(f"TEXTURE {path}")
    mats = materials()
    lods = [args.lod] if args.lod else list(LODS)
    for lod in lods:
        kit.clear_prefixed(PREFIX)
        objs = build_lod(lod, mats)
        if not args.no_bake and lod != "LOD2":
            bake_ao(objs)
        export_lod(lod, objs, args.out)
    kit.clear_prefixed(PREFIX)


if __name__ == "__main__":
    main()
