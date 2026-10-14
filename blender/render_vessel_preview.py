"""Render review frames of an exported vessel or salmon (asset QA).

Run headless:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/render_vessel_preview.py -- --glb PATH --out DIR [--samples N]
      [--shots bow,stern,side,top,deck] [--fish]

Imports one .glb, lays it on a calm fjord-coloured water plane under a low
Nordic sun (AgX) and renders review angles scaled to the model's length
with EEVEE. --fish renders a salmon against a neutral underwater-blue
backdrop instead (no water plane). Nothing is written into the repository;
the frames go to --out.
"""

from __future__ import annotations

import argparse
import math
import os
import sys

import bpy
from mathutils import Vector


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--glb", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--samples", type=int, default=32)
    ap.add_argument("--shots", default="bow,stern,side,top,deck")
    ap.add_argument("--fish", action="store_true")
    ap.add_argument("--res", default="1600x900")
    return ap.parse_args(argv)


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def setup(samples, res, fish):
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    if hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = samples
    w, h = (int(v) for v in res.split("x"))
    scene.render.resolution_x = w
    scene.render.resolution_y = h
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("LK_Preview")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    if fish:
        bg.inputs["Color"].default_value = (0.02, 0.07, 0.09, 1.0)
        bg.inputs["Strength"].default_value = 1.0
    else:
        sky = world.node_tree.nodes.new("ShaderNodeTexSky")
        for value in ("MULTIPLE_SCATTERING", "NISHITA"):
            try:
                sky.sky_type = value
                break
            except TypeError:
                continue
        sky.sun_elevation = math.radians(24.0)
        sky.sun_rotation = math.radians(210.0)
        bg.inputs["Strength"].default_value = 0.35
        world.node_tree.links.new(sky.outputs["Color"], bg.inputs["Color"])
    sun = bpy.data.lights.new("LK_Sun", "SUN")
    sun.energy = 3.2 if not fish else 4.0
    sun.angle = math.radians(1.5)
    sun.color = (1.0, 0.93, 0.84)
    sun_obj = bpy.data.objects.new("LK_Sun", sun)
    sun_obj.rotation_euler = (math.radians(62.0), 0.0, math.radians(35.0))
    scene.collection.objects.link(sun_obj)
    if not fish:
        bpy.ops.mesh.primitive_plane_add(size=2000.0, location=(0, 0, 0))
        water = bpy.context.active_object
        mat = bpy.data.materials.new("LK_Water")
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (0.006, 0.026, 0.032, 1.0)  # #0E2A33
        bsdf.inputs["Roughness"].default_value = 0.08
        water.data.materials.append(mat)


def vertex_ao(objs):
    """Multiply the baked AO (COLOR_0) into each material, like the game."""
    done = set()
    for obj in objs:
        for slot in obj.material_slots:
            mat = slot.material
            if mat is None or mat.name in done or not mat.use_nodes:
                continue
            done.add(mat.name)
            nt = mat.node_tree
            bsdf = nt.nodes.get("Principled BSDF")
            if bsdf is None:
                continue
            layers = obj.data.color_attributes
            if not layers:
                continue
            attr = nt.nodes.new("ShaderNodeVertexColor")
            attr.layer_name = layers[0].name
            mul = nt.nodes.new("ShaderNodeMix")
            mul.data_type = "RGBA"
            mul.blend_type = "MULTIPLY"
            sockets = [s for s in mul.inputs if s.type == "RGBA"]
            mul.inputs[0].default_value = 1.0
            base = bsdf.inputs["Base Color"]
            if base.links:
                nt.links.new(base.links[0].from_socket, sockets[0])
            else:
                sockets[0].default_value = base.default_value
            nt.links.new(attr.outputs["Color"], sockets[1])
            out = [s for s in mul.outputs if s.type == "RGBA"][0]
            nt.links.new(out, base)


def look_at(cam, target):
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def main():
    args = parse()
    os.makedirs(args.out, exist_ok=True)
    clear_scene()
    setup(args.samples, args.res, args.fish)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=args.glb)
    objs = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    vertex_ao(objs)
    lo = Vector((min(v[0] for o in objs for v in o.bound_box), min(v[1] for o in objs for v in o.bound_box),
                 min(v[2] for o in objs for v in o.bound_box)))
    hi = Vector((max(v[0] for o in objs for v in o.bound_box), max(v[1] for o in objs for v in o.bound_box),
                 max(v[2] for o in objs for v in o.bound_box)))
    length = hi.y - lo.y
    centre = (lo + hi) / 2
    cam_data = bpy.data.cameras.new("LK_Cam")
    cam = bpy.data.objects.new("LK_Cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    cam_data.clip_start = 0.01
    cam_data.clip_end = 5000.0
    L = max(length, 0.1) * (1.0 if length > 30.0 else 1.7)
    shots = {
        "bow": (Vector((0.75 * L, 0.85 * L, 0.32 * L)), Vector((0, 0.05 * L, 0.05 * L)), 50),
        "stern": (Vector((-0.8 * L, -0.85 * L, 0.3 * L)), Vector((0, -0.05 * L, 0.05 * L)), 50),
        "side": (Vector((1.35 * L, 0.0, 0.08 * L)), Vector((0, 0, 0.08 * L)), 50),
        "top": (Vector((0.01, 0.0, 1.5 * L)), Vector((0, 0, 0)), 50),
        "deck": (Vector((0.35 * L, -0.55 * L, 0.18 * L)), Vector((0, 0.1 * L, 0.04 * L)), 35),
        "low": (Vector((0.55 * L, 0.45 * L, 0.03 * L)), Vector((0, 0, 0.06 * L)), 40),
    }
    if args.fish:
        shots = {
            "side": (Vector((1.6 * L, 0.0, 0.0)), centre, 50),
            "top": (Vector((0.0, 0.0, 1.8 * L)), centre, 50),
            "three_quarter": (Vector((0.9 * L, 0.9 * L, 0.45 * L)), centre, 50),
            "front": (Vector((0.25 * L, 1.6 * L, 0.1 * L)), centre, 50),
            "below": (Vector((0.8 * L, -0.4 * L, -0.9 * L)), centre, 50),
        }
    for name in args.shots.split(","):
        if name not in shots:
            continue
        loc, target, lens = shots[name]
        cam.location = loc
        cam_data.lens = lens
        look_at(cam, target)
        path = os.path.join(args.out, f"{name}.png")
        bpy.context.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print(f"PREVIEW {path}")


if __name__ == "__main__":
    main()
