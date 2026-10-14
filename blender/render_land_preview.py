"""Render review frames of the land kit (F2-25 asset QA).

Run headless:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/render_land_preview.py -- --out DIR [--kit DIR] [--lod LOD0]
      [--samples N] [--res 1600x900] [--dusk]

Imports the exported <asset>_<LOD>.glb files (default godot/assets/models/
land_kit) and lays out one full hybrid flow-through lot as spec §5.1 has it
(two rows of six Ø28 m tanks 4 m apart, the process hall between the rows,
a pre-grow tank at each row end), the harvest hall, the pipe bridge from the
quay, the intake basin and the utilities, on a blasted-granite pad over dark
water. The kit's one material is rebuilt from the trim atlas (base colour,
roughness from its alpha, the strip emission at --dusk) times the baked AO,
roughly as godot/shaders/facility_kit.gdshader does. EEVEE, AgX, a low Nordic
sun. Nothing is written into the repository; the frames go to --out.
"""

from __future__ import annotations

import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KIT = os.path.join(REPO, "godot", "assets", "models", "land_kit")
ATLAS = os.path.join(REPO, "godot", "assets", "models", "facility_kit", "textures", "facility_atlas.png")

# Layout of one lot and its site pieces (Blender metres; Godot z = -y).
LOT = []
for row in (-31.0, 31.0):
    for k in range(6):
        LOT.append(("tank_growout", (-90.0 + 32.0 * k, row, 0.0), 0.0 if row < 0 else math.pi))
    LOT.append(("tank_pregrow", (96.0, row, 0.0), 0.0 if row < 0 else math.pi))
for k in range(6):
    LOT.append(("process_hall_bay", (-75.0 + 30.0 * k, 0.0, 0.0), 0.0))
SITE = [
    # Model fronts (Blender -Y) turn seaward (+X) with a +90 deg yaw.
    ("harvest_hall", (200.0, 20.0, 0.0), math.pi / 2),
    ("quay", (292.5, 0.0, -0.6), math.pi / 2),
    ("intake_basin", (280.0, -150.0, 0.0), math.pi / 2),
    ("feed_silo_land", (140.0, -80.0, 0.0), 0.0),
    ("lox_tank", (130.0, 75.0, 0.0), 0.0),
    ("lox_tank", (136.0, 75.0, 0.0), 0.0),
    ("genset_container", (100.0, 80.0, 0.0), 0.0),
    ("genset_container", (100.0, 85.0, 0.0), 0.0),
    ("substation", (70.0, 85.0, 0.0), 0.0),
    ("sludge_station", (165.0, 85.0, 0.0), 0.0),
]
for k in range(7):
    SITE.append(("pipe_bridge_span", (290.0 - 25.0 * (k + 0.5), -50.0, 10.0), 0.0))
FEEDERS = [p for p in LOT if p[0].startswith("tank_")]

SHOTS = {
    # name: (camera location, look-at target, lens mm)
    "land_framing": ((60.0, -420.0, 345.0), (60.0, -20.0, 0.0), 42),
    "three_quarter": ((-170.0, -185.0, 95.0), (-60.0, -31.0, 6.0), 35),
    "tank_close": ((-128.0, -78.0, 26.0), (-90.0, -31.0, 8.0), 35),
    "bridge_quay": ((345.0, -150.0, 45.0), (230.0, -20.0, 8.0), 35),
    "hall_end": ((130.0, -40.0, 14.0), (80.0, 0.0, 6.0), 30),
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


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def kit_material(dusk: bool):
    mat = bpy.data.materials.new("LK_FacilityKit_Preview")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(ATLAS)
    tex.interpolation = "Linear"
    vcol = nt.nodes.new("ShaderNodeVertexColor")
    ao = nt.nodes.new("ShaderNodeMix")
    ao.data_type = "RGBA"
    ao.blend_type = "MULTIPLY"
    ao.inputs[0].default_value = 0.8
    cols = [s for s in ao.inputs if s.type == "RGBA"]
    nt.links.new(tex.outputs["Color"], cols[0])
    nt.links.new(vcol.outputs["Color"], cols[1])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Roughness"])
    # Emissive strips have alpha 1.0: dark glass by day, lit at dusk.
    gate = nt.nodes.new("ShaderNodeMath")
    gate.operation = "GREATER_THAN"
    gate.inputs[1].default_value = 0.985
    nt.links.new(tex.outputs["Alpha"], gate.inputs[0])
    glass = nt.nodes.new("ShaderNodeMix")
    glass.data_type = "RGBA"
    gcols = [s for s in glass.inputs if s.type == "RGBA"]
    nt.links.new(gate.outputs["Value"], glass.inputs[0])
    nt.links.new([s for s in ao.outputs if s.type == "RGBA"][0], gcols[0])
    gcols[1].default_value = (0.010, 0.016, 0.020, 1.0)
    nt.links.new([s for s in glass.outputs if s.type == "RGBA"][0], bsdf.inputs["Base Color"])
    if "Emission Color" in bsdf.inputs:
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
        strength = nt.nodes.new("ShaderNodeMath")
        strength.operation = "MULTIPLY"
        strength.inputs[1].default_value = 6.0 if dusk else 0.0
        nt.links.new(gate.outputs["Value"], strength.inputs[0])
        nt.links.new(strength.outputs["Value"], bsdf.inputs["Emission Strength"])
    return mat


def setup(samples, res, dusk):
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    if hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = samples
        for attr in ("use_shadows", "use_raytracing"):
            if hasattr(scene.eevee, attr):
                setattr(scene.eevee, attr, True)
    w, h = (int(v) for v in res.split("x"))
    scene.render.resolution_x = w
    scene.render.resolution_y = h
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("LK_Preview")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    sky = world.node_tree.nodes.new("ShaderNodeTexSky")
    for value in ("MULTIPLE_SCATTERING", "NISHITA"):
        try:
            sky.sky_type = value
            break
        except TypeError:
            continue
    elev = 3.0 if dusk else 28.0
    sky.sun_elevation = math.radians(elev)
    sky.sun_rotation = math.radians(220.0)
    bg.inputs["Strength"].default_value = 0.35 if not dusk else 0.5
    world.node_tree.links.new(sky.outputs["Color"], bg.inputs["Color"])
    sun = bpy.data.lights.new("LK_Sun", "SUN")
    sun.energy = 3.0 if not dusk else 1.2
    sun.angle = math.radians(1.5)
    sun.color = (1.0, 0.93, 0.84) if not dusk else (1.0, 0.62, 0.45)
    sun_obj = bpy.data.objects.new("LK_Sun", sun)
    sun_obj.rotation_euler = (math.radians(90.0 - elev), 0.0, math.radians(220.0 - 180.0))
    scene.collection.objects.link(sun_obj)
    # Dark fjord water at -3.6 m and the blasted-granite pad at 0.
    for name, size, z, rgb, rough in (("LK_Water", 4000.0, -3.6, (0.006, 0.026, 0.032), 0.08),
                                      ("LK_Pad", 1.0, 0.0, (0.15, 0.17, 0.18), 0.95)):
        if name == "LK_Pad":
            bpy.ops.mesh.primitive_cube_add(size=1.0, location=(57.5, -10.0, -10.0))
            obj = bpy.context.active_object
            obj.scale = (445.0, 330.0, 20.0)
        else:
            bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, z))
            obj = bpy.context.active_object
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        b = mat.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*rgb, 1.0)
        b.inputs["Roughness"].default_value = rough
        obj.data.materials.append(mat)


def import_asset(kit_dir, asset, lod):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(kit_dir, f"{asset}_{lod}.glb"))
    return [o for o in bpy.data.objects if o not in before][0]


def place(kit_dir, lod, mat, placements):
    cache = {}
    for asset, loc, yaw in placements:
        if asset not in cache:
            src = import_asset(kit_dir, asset, lod)
            src.data.materials.clear()
            src.data.materials.append(mat)
            cache[asset] = src
            obj = src
        else:
            obj = cache[asset].copy()
            bpy.context.scene.collection.objects.link(obj)
        obj.location = loc
        obj.rotation_mode = "XYZ"
        obj.rotation_euler = (0.0, 0.0, yaw)  # the importer converts Y-up to Z-up in the data


def look_at(cam, target):
    direction = Vector(target) - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def main():
    args = parse()
    os.makedirs(args.out, exist_ok=True)
    clear_scene()
    setup(args.samples, args.res, args.dusk)
    mat = kit_material(args.dusk)
    place(args.kit, args.lod, mat, LOT + SITE)
    feeder_lod = "LOD1" if args.lod == "LOD1" else "LOD0"
    if args.lod != "LOD2":
        place(args.kit, feeder_lod, mat, [("tank_feeder", (p[1][0], p[1][1], (13.62 if p[0] == "tank_growout" else 9.35)), 0.0)
                                          for p in FEEDERS])
    cam_data = bpy.data.cameras.new("LK_Cam")
    cam = bpy.data.objects.new("LK_Cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    for name in args.shots.split(","):
        loc, target, lens = SHOTS[name]
        cam.location = loc
        cam_data.lens = lens
        cam_data.clip_end = 5000.0
        look_at(cam, target)
        bpy.context.scene.render.filepath = os.path.join(args.out, f"{name}{'_dusk' if args.dusk else ''}_{args.lod}.png")
        bpy.ops.render.render(write_still=True)
        print(f"RENDERED {bpy.context.scene.render.filepath}")


if __name__ == "__main__":
    main()
