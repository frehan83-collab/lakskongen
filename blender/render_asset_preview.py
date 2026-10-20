"""Render review frames of the exported pen and site kit (asset QA).

Run headless:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/render_asset_preview.py -- --out DIR [--samples N]

Imports godot/assets/models/pen_cage/pen_cage_LOD0.glb (+ a lit lice-skirt
copy) and the mooring kit, lays them on a calm fjord-coloured water plane
under a low Nordic sun, and renders a few fixed review angles with EEVEE.
The net textures come from godot/assets/models/pen_cage/textures/. Nothing
is written into the repository; the frames go to --out.
"""

from __future__ import annotations

import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PEN = os.path.join(REPO, "godot", "assets", "models", "pen_cage")
KIT = os.path.join(REPO, "godot", "assets", "models", "site_kit")

SHOTS = {
    # name: (camera location, look-at target, lens mm)
    "hero_three_quarter": ((52.0, -46.0, 17.0), (0.0, 0.0, -1.0), 35),
    "walkway_detail": ((27.6, -6.5, 2.4), (24.0, 4.0, 0.8), 24),
    "low_waterline": ((40.0, -18.0, 1.2), (0.0, 0.0, 1.0), 30),
    "top_down": ((0.0, 0.0, 120.0), (0.0, 0.0, 0.0), 35),
    "corner_buoy": ((44.0, -40.0, 3.0), (35.0, -35.0, 0.8), 35),
}


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--samples", type=int, default=32)
    ap.add_argument("--skirt", action="store_true")
    return ap.parse_args(argv)


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def import_glb(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def net_material(mat, tile_m, strength):
    """Textured, alpha-blended net (the Godot shader's look, roughly)."""
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(PEN, "textures", "net_mesh.png"), check_existing=True)
    tex.interpolation = "Cubic"
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (1.0 / tile_m, 1.0 / tile_m, 1.0)
    coord = nt.nodes.new("ShaderNodeTexCoord")
    nt.links.new(coord.outputs["UV"], mapping.inputs["Vector"])
    nt.links.new(mapping.outputs["Vector"], tex.inputs["Vector"])
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = strength
    nt.links.new(tex.outputs["Alpha"], mul.inputs[0])
    nt.links.new(mul.outputs[0], bsdf.inputs["Alpha"])
    bsdf.inputs["Base Color"].default_value = (0.012, 0.018, 0.021, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.9
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "BLENDED"


def setup_world(samples):
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    if hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = samples
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("LK_Preview")
    scene.world = world
    world.use_nodes = True
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    for attr, value in (("sky_type", "MULTIPLE_SCATTERING"), ("sky_type", "NISHITA")):
        try:
            setattr(sky, attr, value)
            break
        except TypeError:
            continue
    sky.sun_elevation = math.radians(22.0)
    sky.sun_rotation = math.radians(210.0)
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Strength"].default_value = 0.35
    world.node_tree.links.new(sky.outputs["Color"], bg.inputs["Color"])
    sun = bpy.data.lights.new("LK_Sun", "SUN")
    sun.energy = 3.2
    sun.angle = math.radians(1.5)
    sun.color = (1.0, 0.93, 0.84)
    sun_obj = bpy.data.objects.new("LK_Sun", sun)
    sun_obj.rotation_euler = (math.radians(68.0), 0.0, math.radians(30.0))
    scene.collection.objects.link(sun_obj)
    # Water: deep fjord green-blue, glossy, opaque (the eye stops at the surface).
    bpy.ops.mesh.primitive_plane_add(size=900.0, location=(0, 0, 0))
    water = bpy.context.active_object
    mat = bpy.data.materials.new("LK_Water")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.006, 0.026, 0.032, 1.0)  # #0E2A33
    bsdf.inputs["Roughness"].default_value = 0.08
    water.data.materials.append(mat)


def look_at(cam, target):
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def main():
    args = parse()
    os.makedirs(args.out, exist_ok=True)
    clear_scene()
    setup_world(args.samples)
    pen = import_glb(os.path.join(PEN, "pen_cage_LOD0.glb"))
    for obj in pen:
        if obj.type != "MESH":
            continue
        name = obj.name.split(".")[0]
        if name == "Band" or (name == "LiceSkirt" and not args.skirt):
            obj.hide_render = True
        for slot in obj.material_slots:
            if slot.material and slot.material.name.startswith(("LK_Net", "LK_BirdNet")):
                if not slot.material.get("lk_preview"):
                    bird = slot.material.name.startswith("LK_BirdNet")
                    net_material(slot.material, 0.6 if bird else 0.25, 0.5 if bird else 1.0)
                    slot.material["lk_preview"] = True
    # Four corner buoys with lights on a 70 m cell.
    buoy = import_glb(os.path.join(KIT, "mooring_buoy_LOD0.glb"))[0]
    light = import_glb(os.path.join(KIT, "mooring_light_LOD0.glb"))[0]
    for k, (x, y) in enumerate(((35, 35), (-35, 35), (35, -35), (-35, -35))):
        b = buoy if k == 0 else buoy.copy()
        l_obj = light if k == 0 else light.copy()
        if k:
            bpy.context.scene.collection.objects.link(b)
            bpy.context.scene.collection.objects.link(l_obj)
        b.location = (x, y, 0.0)
        l_obj.location = (x, y, 0.0)
    cam_data = bpy.data.cameras.new("LK_Cam")
    cam = bpy.data.objects.new("LK_Cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    for name, (loc, target, lens) in SHOTS.items():
        cam.location = loc
        cam_data.lens = lens
        cam_data.clip_end = 2000.0
        look_at(cam, target)
        path = os.path.join(args.out, f"{name}{'_skirt' if args.skirt else ''}.png")
        bpy.context.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print(f"PREVIEW {path}")


if __name__ == "__main__":
    main()
