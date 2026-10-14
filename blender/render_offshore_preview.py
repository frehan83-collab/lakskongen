"""Render review frames of the offshore kit (F2-26/F2-27 asset QA).

Run headless:
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python blender/render_offshore_preview.py -- --out DIR [--offshore DIR]
      [--lod LOD0] [--samples N] [--res 1600x900] [--shots a,b] [--stem of1_class]
      [--vessel offshore_service] [--sun-elev 6] [--sun-az 215]

Imports <stem>_<LOD>.glb (default godot/assets/models/offshore/of1_class)
and, with --vessel, a fleet glb from godot/assets/models/<vessel>/ moored at
the unit's walk-to-work landing, then renders the shots below over an opaque
dark sea in low winter sun (ref O3's mood): the kit's one material rebuilt
from the trim atlas times the baked AO (roughly as
godot/shaders/offshore_kit.gdshader does), the net as a pale veil. EEVEE,
AgX. Nothing is written into the repository; the frames go to --out.
"""

from __future__ import annotations

import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(REPO, "godot", "assets", "models")
OFFSHORE = os.path.join(MODELS, "offshore")
ATLAS = os.path.join(MODELS, "facility_kit", "textures", "offshore_atlas.png")
VESSEL_DIRS = {"offshore_service": "offshore_service", "crew_boat": "crew_boat", "rov": "subsea", "auv": "subsea",
               "wellboat": "wellboat", "workboat": "workboat"}
VESSEL_COLOURS = {
    "LK_VHullRed": "#8E1F24", "LK_VHullOrange": "#C9531F", "LK_VHullSlate": "#34424B", "LK_VHullWhite": "#E9ECEA",
    "LK_VDeck": "#4C5E52", "LK_VWhite": "#E6E9E6", "LK_VGlass": "#16232B", "LK_VSteel": "#7D858B",
    "LK_VDark": "#1E2124", "LK_VCrane": "#E8B92A", "LK_VSafety": "#E0662A", "LK_VSubseaYellow": "#E6BE2E",
    "LK_VSubseaOrange": "#C9531F", "LK_VGangway": "#D7DBDD",
}

# Godot -> Blender: (x, y, z)_godot = (x, z, -y)_blender. Shots in Blender
# coordinates: camera location, look-at, lens (mm).
SHOTS = {
    # Ref O3: low oblique from the vessel's side, ring filling ~1/3 of the frame.
    "o3_oblique": ((-215.0, -105.0, 72.0), (0.0, 0.0, 6.0), 50),
    # The game's OF1 framing (220 m at -18 deg).
    "framing_220": ((-209.2, 0.0, 68.0 + 9.0), (0.0, 0.0, 9.0), 42),
    # Ref O2: high three-quarter.
    "o2_three_quarter": ((-140.0, -150.0, 95.0), (0.0, 0.0, 4.0), 40),
    "close_topsides": ((-48.0, -42.0, 34.0), (-6.0, -2.0, 18.0), 35),
    "close_frame": ((-92.0, -40.0, 14.0), (-50.0, -2.0, 4.0), 35),
    "vessel_alongside": ((-140.0, 40.0, 30.0), (-62.0, -14.0, 6.0), 40),
    "top_down": ((0.0, -1.0, 330.0), (0.0, 0.0, 0.0), 50),
    "vessel_close": ((-118.0, -52.0, 16.0), (-72.0, -8.0, 6.0), 40),
    "vessel_stern": ((-95.0, -75.0, 22.0), (-72.0, -20.0, 4.0), 35),
    "yard": ((-160.0, -150.0, 40.0), (0.0, 0.0, -14.0), 35),
}


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--offshore", default=OFFSHORE)
    ap.add_argument("--atlas", default=ATLAS)
    ap.add_argument("--stem", default="of1_class")
    ap.add_argument("--lod", default="LOD0")
    ap.add_argument("--samples", type=int, default=32)
    ap.add_argument("--res", default="1600x900")
    ap.add_argument("--shots", default=",".join(k for k in SHOTS if k != "yard"))
    ap.add_argument("--vessel", default="")
    ap.add_argument("--vessel-dir", default="")
    ap.add_argument("--sun-elev", type=float, default=7.0)
    ap.add_argument("--sun-az", type=float, default=200.0)
    ap.add_argument("--no-sea", action="store_true", help="hide the sea (the yard view)")
    ap.add_argument("--prefix", default="")
    return ap.parse_args(argv)


def srgb_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_lin(code, a=1.0):
    code = code.lstrip("#")
    return tuple(srgb_lin(int(code[i:i + 2], 16) / 255.0) for i in (0, 2, 4)) + (a,)


def kit_material(atlas):
    mat = bpy.data.materials.new("LK_OffshoreKit_Preview")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(atlas)
    tex.interpolation = "Linear"
    uvn = nt.nodes.new("ShaderNodeUVMap")
    uvn.uv_map = "UVMap"
    nt.links.new(uvn.outputs["UV"], tex.inputs["Vector"])
    attr = nt.nodes.new("ShaderNodeVertexColor")
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs[0].default_value = 0.85
    cols = [s for s in mul.inputs if s.type == "RGBA"]
    nt.links.new(tex.outputs["Color"], cols[0])
    nt.links.new(attr.outputs["Color"], cols[1])
    nt.links.new([s for s in mul.outputs if s.type == "RGBA"][0], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Roughness"])
    return mat


def net_material():
    mat = bpy.data.materials.new("LK_OffshoreNet_Preview")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = hex_lin("#A8ADB0")
    bsdf.inputs["Roughness"].default_value = 0.9
    bsdf.inputs["Alpha"].default_value = 0.42
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "DITHERED"
    mat.use_backface_culling = False
    return mat


def vessel_material(name):
    mat = bpy.data.materials.new(name + "_Preview")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    base = hex_lin(VESSEL_COLOURS.get(name, "#808080"))
    attr = nt.nodes.new("ShaderNodeVertexColor")
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs[0].default_value = 0.8
    cols = [s for s in mul.inputs if s.type == "RGBA"]
    cols[0].default_value = base
    nt.links.new(attr.outputs["Color"], cols[1])
    nt.links.new([s for s in mul.outputs if s.type == "RGBA"][0], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.1 if name == "LK_VGlass" else 0.55
    return mat


def import_glb(path, kit_mat, net_mat, vessel=False):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    vmats = {}
    for obj in new:
        if obj.type != "MESH":
            continue
        for slot in obj.material_slots:
            if slot.material is None:
                continue
            n = slot.material.name.split(".")[0]
            if vessel:
                vmats.setdefault(n, vessel_material(n))
                slot.material = vmats[n]
            else:
                slot.material = net_mat if n.startswith("LK_OffshoreNet") else kit_mat
    return new


def sea(z=0.0):
    bpy.ops.mesh.primitive_plane_add(size=6000.0, location=(0.0, 0.0, z))
    plane = bpy.context.active_object
    mat = bpy.data.materials.new("Sea")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = hex_lin("#122838")
    bsdf.inputs["Roughness"].default_value = 0.16
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 0.35
    noise.inputs["Detail"].default_value = 6.0
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (0.25, 0.6, 1.0)
    nt.links.new(tc.outputs["Object"], mapping.inputs["Vector"])
    nt.links.new(mapping.outputs["Vector"], noise.inputs["Vector"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.25
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    plane.data.materials.append(mat)
    return plane


def world_and_sun(elev, az):
    scene = bpy.context.scene
    world = bpy.data.worlds.new("Sky") if scene.world is None else scene.world
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes["Background"]
    sky = nt.nodes.new("ShaderNodeTexSky")
    for t in ("NISHITA", "MULTIPLE_SCATTERING", "SINGLE_SCATTERING", "HOSEK_WILKIE"):
        try:
            sky.sky_type = t
            break
        except TypeError:
            continue
    if hasattr(sky, "sun_elevation"):
        sky.sun_elevation = math.radians(elev)
        sky.sun_rotation = math.radians(az)
    nt.links.new(sky.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = 0.35
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.2
    sun.color = (1.0, 0.86, 0.68)
    sun.angle = math.radians(0.6)
    obj = bpy.data.objects.new("Sun", sun)
    scene.collection.objects.link(obj)
    el, a = math.radians(elev), math.radians(az)
    d = Vector((math.cos(el) * math.sin(a), math.cos(el) * math.cos(a), math.sin(el)))
    obj.rotation_euler = d.to_track_quat("Z", "Y").to_euler()


def camera(loc, target, lens):
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = lens
    cam_data.clip_end = 8000.0
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = cam
    return cam


def main():
    a = parse()
    os.makedirs(a.out, exist_ok=True)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    scene = bpy.context.scene
    for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            scene.render.engine = eng
            break
        except TypeError:
            continue
    if hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = a.samples
        if hasattr(scene.eevee, "use_shadows"):
            scene.eevee.use_shadows = True
    w, h = (int(v) for v in a.res.split("x"))
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.view_settings.view_transform = "AgX"
    kit_mat, net_mat = kit_material(a.atlas), net_material()
    import_glb(os.path.join(a.offshore, f"{a.stem}_{a.lod}.glb"), kit_mat, net_mat)
    if a.vessel:
        vdir = a.vessel_dir or os.path.join(MODELS, VESSEL_DIRS.get(a.vessel, a.vessel))
        objs = import_glb(os.path.join(vdir, f"{a.vessel}_{a.lod}.glb"), kit_mat, net_mat, vessel=True)
        root = [o for o in objs if o.parent is None]
        # Alongside the walk-to-work landing (Blender -X side), bow to +Y.
        for o in root:
            o.location = (o.location.x - 72.0, o.location.y - 8.0, o.location.z)
    if not a.no_sea:
        sea()
    world_and_sun(a.sun_elev, a.sun_az)
    for name in a.shots.split(","):
        loc, target, lens = SHOTS[name]
        camera(loc, target, lens)
        scene.render.filepath = os.path.join(a.out, f"{a.prefix}{name}.png")
        bpy.ops.render.render(write_still=True)
        print(f"PREVIEW {scene.render.filepath}")


if __name__ == "__main__":
    main()
