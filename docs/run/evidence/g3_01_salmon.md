# G3-01 (asset part) evidence: the Atlantic salmon

Roadmap item G3-01, asset part only: `blender/build_salmon.py` (UVs, LOD0
about 1.5k triangles, LOD1 about 300, countershaded silver material, nose
along -Z) with the existing CPU school code kept working. The GPU school
(FishSchool.gd, per-fish data in MultiMesh custom data, sim-driven depth and
speed) is the rest of G3-01 and is not in this change. Branch `wave1/art-2`.

## Shape

A farmed Atlantic salmon at 0.90 m fork length, proportions as fractions of
the fork length (FL) from standard descriptions of *Salmo salar*; measured by
the build script into `salmon_<LOD>.json` and checked by
`tests/test_salmon_asset.py`.

| Feature | Target | Model |
|---|---|---|
| Fork length | 0.90 m (the school scales by L/0.9 from the weight) | 0.90 m, fork notch at exactly 1 FL |
| Total length | 1.02-1.08 FL (shallow fork) | 0.94 m = 1.046 FL (LOD0) |
| Body depth | 0.20-0.28 FL (farmed fish are deep) | 0.219 m = 0.243 FL at ~0.42 FL |
| Width | 0.45-0.65 of depth | 0.120 m = 0.55 of depth |
| Fins | dorsal from ~0.40 FL, adipose ~0.72, anal ~0.70, pelvics under the dorsal's rear, pectorals low behind the gill cover | as listed; dorsal a trapezoid with a concave trailing edge, caudal a shallow fork from a laterally compressed peduncle |
| Orientation | nose along Godot -Z | snout at z = -0.452 m, fork at +0.45 m |
| Triangles | LOD0 ~1.5k, LOD1 ~300 | 1,564 / 270 (ceilings 1,800 / 400) |

## Material

One material (`LK_SalmonSkin` -> `salmon_skin_material.tres`,
`shaders/salmon_skin.gdshader`): one draw call per school. The 1024 x 512
atlas `textures/salmon_skin.png` is painted by the build script: the body in
the top 74% (u along the fish, v from the dorsal line to the belly, both
flanks mirrored onto it), the fins in the bottom strip. RGB is the albedo:
dark steel-blue back (#2E4250), silver flank (#C3CCD3) over a wandering
silver line, white belly (#EDF0F1), black X-shaped and round spots above the
lateral line and on the head, a faint lateral line, gill-cover arc, mouth
line and eye. A is the silver mask: metallic 0.15-0.85, roughness
0.55-0.26 (the flank is a mirror, back and fins are not). The vertex shader
bends the body in a swim wave (amplitude growing to the tail, per-fish phase
from the instance id) with matching normals; G3-01 can drive its uniforms.

## In the game

`FishPenManager` loads `salmon_LOD1.glb` for each pen's school and swaps the
MultiMesh mesh to `salmon_LOD0.glb` when the camera is within 70 m of the
pen; the instance transforms and the visible count stay (UI test). The
retired `salmon_v2.glb` (220 triangles, three flat materials) is deleted.

## Frames

- `g3_01/blender_side_lod0.jpg`, `g3_01/blender_three_quarter_lod0.jpg`,
  `g3_01/blender_three_quarter_lod1.jpg`: EEVEE review renders
  (`blender/render_vessel_preview.py --fish --texture ...`).
- `g3_01/blender_first_pass.jpg`: the first pass (shark-like dorsal fin,
  bottle-neck peduncle, winged pectorals) that the final shape corrected.
- `g3_01/game_salmon_close.jpg`: three LOD0 fish and one LOD1 fish in the
  game's lighting, swim wave visible.
- `g3_01/skin_atlas.jpg`: the atlas.
