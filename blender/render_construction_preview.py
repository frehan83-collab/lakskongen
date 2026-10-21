"""Render review frames of the construction kit (F2-28 asset QA).

Run headless:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/render_construction_preview.py -- --out DIR [--kit DIR]
      [--lod LOD0] [--samples N] [--res 1600x900] [--dusk] [--shots a,b]

Imports the exported <asset>_<LOD>.glb files (default godot/assets/models/
construction_kit, with the land kit's tank, hall and bridge beside them) and
lays out a building site on a blasted-granite pad over dark water: a row of
three Ø28 tanks 32 m apart, the middle one inside its scaffold ring, the tower
crane at the end of the row with its jib over the first tank, process hall
bays behind, and the excavator working on a blanket of blast mats. The kit's
one material is rebuilt from the trim atlas exactly as render_land_preview.py
does (base colour, roughness from the atlas alpha, the strip emission at
--dusk) times the baked AO. Nothing is written into the repository; the frames
go to --out.

Licence: in-house code (proprietary, part of the Lakskongen repository).
"""

from __future__ import annotations

import argparse
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_land_preview as scene_kit  # noqa: E402

REPO = scene_kit.REPO
KIT = os.path.join(REPO, "godot", "assets", "models", "construction_kit")
LAND = scene_kit.KIT

# Blender metres, pad top at z = 0; model fronts (Blender -Y) face the camera.
LAND_PIECES = [
    ("tank_growout", (-32.0, 0.0, 0.0), 0.0),
    ("tank_growout", (0.0, 0.0, 0.0), 0.0),
    ("tank_growout", (32.0, 0.0, 0.0), 0.0),
    ("process_hall_bay", (-30.0, 30.0, 0.0), 0.0),
    ("process_hall_bay", (0.0, 30.0, 0.0), 0.0),
    ("process_hall_bay", (30.0, 30.0, 0.0), 0.0),
]
KIT_PIECES = [
    ("scaffold_ring", (0.0, 0.0, 0.0), 0.0),
    ("tower_crane", (-64.0, 8.0, 0.0), 0.0),
    ("blast_mats", (62.0, -4.0, 0.0), 0.0),
    ("excavator", (62.0, 8.0, 0.0), 0.0),
]

SHOTS = {
    # name: (camera location, look-at target, lens mm)
    "site_overview": ((20.0, -250.0, 140.0), (-5.0, 5.0, 22.0), 40),
    "site_oblique": ((-150.0, -125.0, 72.0), (-8.0, 6.0, 26.0), 32),
    "crane_close": ((-125.0, -70.0, 44.0), (-62.0, 8.0, 31.0), 35),
    "crane_head": ((-48.0, -24.0, 58.0), (-30.0, 6.0, 53.0), 45),
    "scaffold_fit": ((0.0, -62.0, 20.0), (0.0, 0.0, 7.0), 35),
    "scaffold_top": ((0.0, -2.0, 90.0), (0.0, 0.0, 0.0), 40),
    "excavator_close": ((80.0, -22.0, 10.0), (60.0, 4.0, 2.5), 40),
    "mats_top": ((62.0, -5.0, 44.0), (62.0, -4.0, 0.0), 35),
}


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--kit", default=KIT)
    ap.add_argument("--lod", default="LOD0")
    ap.add_argument("--samples", type=int, default=32)
    ap.add_argument("--res", default="1600x900")
    ap.add_argument("--dusk", action="store_true")
    ap.add_argument("--shots", default=",".join(SHOTS))
    return ap.parse_args(argv)


def main():
    args = parse()
    os.makedirs(args.out, exist_ok=True)
    scene_kit.clear_scene()
    scene_kit.setup(args.samples, args.res, args.dusk)
    mat = scene_kit.kit_material(args.dusk)
    # The crane has a LOD2 but the scaffold, excavator and mats stop at LOD1.
    scene_kit.place(LAND, args.lod, mat, LAND_PIECES)
    for asset, loc, yaw in KIT_PIECES:
        lod = args.lod if args.lod != "LOD2" or asset == "tower_crane" else "LOD1"
        scene_kit.place(args.kit, lod, mat, [(asset, loc, yaw)])
    cam_data = bpy.data.cameras.new("LK_Cam")
    cam = bpy.data.objects.new("LK_Cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    for name in args.shots.split(","):
        loc, target, lens = SHOTS[name]
        cam.location = loc
        cam_data.lens = lens
        cam_data.clip_end = 5000.0
        scene_kit.look_at(cam, target)
        bpy.context.scene.render.filepath = os.path.join(args.out, f"{name}{'_dusk' if args.dusk else ''}_{args.lod}.png")
        bpy.ops.render.render(write_still=True)
        print(f"RENDERED {bpy.context.scene.render.filepath}")


if __name__ == "__main__":
    main()
