"""Render review frames of the closed-pen kit (F2-30 asset QA).

Run headless:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/render_closed_pen_preview.py -- --out DIR [--kit DIR]
      [--lod LOD0] [--samples N] [--res 1600x900] [--dusk] [--shots a,b]

Imports the exported <asset>_<LOD>.glb files (default godot/assets/models/
closed_pen) and floats them on dark fjord water: the Donut, the Globe and the
semi-closed ring with a pump module on its collar, side by side along X. The
pens float, so the waterline is z = 0; shots named *_under hide the water
plane (the game's water is opaque) to show the skirt, the nets and the intake
hose. The kit's one material is rebuilt from the trim atlas exactly as
render_land_preview.py does. Nothing is written into the repository; the
frames go to --out.

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
KIT = os.path.join(REPO, "godot", "assets", "models", "closed_pen")

# Blender metres, waterline at z = 0; model fronts (Blender -Y) face the camera.
RING_X = 40.0
PIECES = [
    ("closed_donut", (-60.0, 0.0, 0.0), 0.0),
    ("closed_globe", (-8.0, 0.0, 0.0), 0.0),
    ("semi_closed_ring", (RING_X, 0.0, 0.0), 0.0),
    # Pump mounting-plate undersides (local z=1.28) sit on the actual decks:
    # donut 3.12, globe 1.20, semi-closed collar 1.40. At the front (-Y) each
    # model has a matching gap in both handrails. The semi-closed hose radius
    # is 19.5 m, outboard of its 18.4 m skirt.
    ("pump_module", (-60.0, -17.0, 1.84), 0.0),
    ("pump_module", (-8.0, -9.25, -0.08), 0.0),
    ("pump_module", (RING_X, -19.1, 0.12), 0.0),
]
# name: (camera location, look-at target, lens mm, water visible)
SHOTS = {
    "kit_overview": ((-10.0, -165.0, 62.0), (-8.0, 0.0, -2.0), 38, True),
    "kit_oblique": ((-120.0, -105.0, 38.0), (-8.0, 0.0, 0.0), 32, True),
    "donut_top": ((-60.0, -2.0, 130.0), (-60.0, 0.0, 0.0), 40, True),
    "donut_close": ((-60.0, -72.0, 20.0), (-60.0, -10.0, 2.0), 36, True),
    "globe_close": ((-8.0, -40.0, 12.0), (-8.0, 0.0, 1.0), 36, True),
    "ring_close": ((RING_X, -62.0, 22.0), (RING_X, 0.0, 0.0), 35, True),
    "ring_under": ((RING_X + 8.0, -78.0, -12.0), (RING_X, 0.0, -13.0), 32, False),
    "pump_close": ((RING_X + 12.0, -34.0, 8.0), (RING_X, -19.1, 2.5), 40, True),
    "pump_under": ((RING_X + 30.0, -48.0, -10.0), (RING_X, -19.1, -12.0), 40, False),
    "section_under": ((-10.0, -150.0, -14.0), (-8.0, 0.0, -9.0), 40, False),
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
    # The pens float: drop the land pad, put the water on the waterline.
    water = bpy.data.objects["Plane"] if "Plane" in bpy.data.objects else None
    for obj in list(bpy.data.objects):
        if obj.type == "MESH" and obj.data.materials and obj.data.materials[0].name == "LK_Pad":
            bpy.data.objects.remove(obj, do_unlink=True)
        elif obj.type == "MESH" and obj.data.materials and obj.data.materials[0].name == "LK_Water":
            water = obj
    water.location.z = 0.0
    mat = scene_kit.kit_material(args.dusk)
    for asset, loc, yaw in PIECES:
        lod = args.lod if args.lod != "LOD2" or asset != "pump_module" else "LOD1"
        scene_kit.place(args.kit, lod, mat, [(asset, loc, yaw)])
    cam_data = bpy.data.cameras.new("LK_Cam")
    cam = bpy.data.objects.new("LK_Cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    for name in args.shots.split(","):
        loc, target, lens, wet = SHOTS[name]
        water.hide_render = not wet
        cam.location = loc
        cam_data.lens = lens
        cam_data.clip_end = 5000.0
        scene_kit.look_at(cam, target)
        bpy.context.scene.render.filepath = os.path.join(args.out, f"{name}{'_dusk' if args.dusk else ''}_{args.lod}.png")
        bpy.ops.render.render(write_still=True)
        print(f"RENDERED {bpy.context.scene.render.filepath}")


if __name__ == "__main__":
    main()
