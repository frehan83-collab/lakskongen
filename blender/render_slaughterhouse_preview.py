"""Preview renders of the slaughterhouse kit: the three tiers side by side on
one pad, each at its quay with its reception, loading bays and wellboat
intake (Small: slaughter_hall_s; Medium: slaughter_hall_m; Large: the land
kit's harvest_hall). Lighting, sky, water and the kit material come from
render_land_preview.py. Nothing is written into the repository.

  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \\
      --python blender/render_slaughterhouse_preview.py -- --out DIR [--lod LOD0] [--samples N]
"""

from __future__ import annotations

import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_land_preview as base  # noqa: E402

KIT = os.path.join(base.REPO, "godot", "assets", "models", "slaughterhouse_kit")
LAND = base.KIT
FRONT = math.pi / 2  # base preview: a +90 deg yaw turns a model front seaward (+X)

# Each tier: hall centre y; the sea edge is x = 270 (the quay's land edge).
TIERS = [
    # (hall asset, kit dir, hall half-depth along x, y centre, receptions, bays, intakes)
    ("slaughter_hall_s", KIT, 12.0, -135.0, 1, 1, 1),
    ("slaughter_hall_m", KIT, 20.0, -30.0, 1, 2, 1),
    ("harvest_hall", LAND, 30.0, 140.0, 2, 1, 2),
]


def placements():
    land, kit = [], []
    for hall, hall_dir, half, yc, n_rec, n_bay, n_in in TIERS:
        hx = 250.0 - 18.0 - half  # reception (12 deep) + clearance between hall and quay
        (land if hall_dir == LAND else kit).append((hall, (hx, yc, 0.0), FRONT))
        for k in range(n_rec):
            kit.append(("reception_house", (hx + half + 6.5, yc - 9.0 * (n_rec - 1) + 18.0 * k, 0.0), FRONT))
        for k in range(n_bay):
            kit.append(("chilled_loading_bay", (hx - half - 6.0, yc - 7.0 * (n_bay - 1) + 14.0 * k, 0.0), FRONT))
        land.append(("quay", (262.5, yc, -0.6), FRONT))
        for k in range(n_in):
            kit.append(("wellboat_intake", (266.0, yc - 15.0 * (n_in - 1) + 30.0 * k, -0.6), FRONT))
    return land, kit


SHOTS = {
    "tiers_overview": ((520.0, -420.0, 330.0), (200.0, -20.0, 0.0), 40),
    "small_close": ((330.0, -250.0, 60.0), (240.0, -170.0, 5.0), 35),
    "medium_close": ((340.0, -110.0, 70.0), (230.0, -30.0, 6.0), 35),
    "large_close": ((360.0, 40.0, 90.0), (230.0, 140.0, 8.0), 35),
}


def main():
    args = base.parse()
    os.makedirs(args.out, exist_ok=True)
    base.clear_scene()
    base.setup(args.samples, args.res, args.dusk)
    mat = base.kit_material(args.dusk)
    land, kit = placements()
    base.place(LAND, args.lod, mat, land)
    base.place(KIT, args.lod, mat, kit)
    cam_data = bpy.data.cameras.new("LK_Cam")
    cam = bpy.data.objects.new("LK_Cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    for name, (loc, target, lens) in SHOTS.items():
        cam.location = loc
        cam_data.lens = lens
        cam_data.clip_end = 5000.0
        base.look_at(cam, target)
        bpy.context.scene.render.filepath = os.path.join(args.out, f"{name}_{args.lod}.png")
        bpy.ops.render.render(write_still=True)
        print(f"RENDERED {bpy.context.scene.render.filepath}")


if __name__ == "__main__":
    main()
