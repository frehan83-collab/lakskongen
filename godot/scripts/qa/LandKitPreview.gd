extends "res://scripts/qa/QaWorldCapture.gd"
## F2-25 land-kit preview: the kit built by blender/build_land_base.py
## stands on the land pads of the real game world, laid out by
## LandCampusLayout (spec §5.1 lots) and drawn the way F2-20 will draw it:
## one MultiMesh per tank type per LOD band for the whole plot, the feeders
## likewise, and every static piece of a lot merged into one single-surface
## mesh per LOD band (LK_FacilityKit). LOD bands follow spec §5.5 rule 4
## (tanks 350 / 1,200 m, other pieces 500 / 2,000 m) from the shot's camera;
## LOD2 casts no shadow, and of LOD1 only the tanks do (F2-18). The pads are
## a placeholder slab of blasted granite until F2-19 carves them.
##
## The shots in godot/qa/shots.json for this scene (39_landkit_*) are
## captured with, per shot: the sample tank's lid ellipse and pale wall from
## a strip mask, the lid / wall / band L* in the beauty frame, the triangles
## the campus draws, and with "perf" the draw calls it adds (A/B). Output:
## res://screenshots/game/39_landkit_*.png and 39_landkit_metrics.json.
## Run: tools/capture_screenshots.sh --scene res://qa/scenes/LandKitPreview.tscn

const LAYOUT := preload("res://scripts/qa/LandCampusLayout.gd")
const MATERIAL := preload("res://assets/models/facility_kit/LK_FacilityKit.tres")
const MASK_SHADER := preload("res://qa/shaders/kit_strip_mask.gdshader")
const SCENE_PATH := "res://qa/scenes/LandKitPreview.tscn"
const KIT_DIR := "res://assets/models/land_kit/"
const METRICS_FILE := "39_landkit_metrics.json"
const TANK_LOD_M := Vector2(350.0, 1200.0)
const UNIT_LOD_M := Vector2(500.0, 2000.0)
const LODS := ["LOD0", "LOD1", "LOD2"]
## Lid apex above the tank base (shell + the lid's 2.2% rise): the feeder.
const FEEDER_Y := {"tank_growout": 13.0 + 0.022 * 28.0, "tank_pregrow": 9.0 + 0.022 * 16.0}
## Mask colours (kit_strip_mask.gdshader).
const MASK_LID := Color(0, 1, 1)
const MASK_WALL := Color(1, 0, 0)
const MASK_BAND := Color(1, 0, 1)

@export var auto_run := true
@export var plots: PackedStringArray = ["pad_a", "pad_b", "pad_delta"]

var _campus: Node3D = null
var _meshes := {}
var _results := {}
var _tri_drawn := 0
var _pad_material: StandardMaterial3D = null


func _ready() -> void:
	if auto_run:
		start()


func scene_path() -> String:
	return SCENE_PATH


## The single mesh of a kit glb (each LOD is one mesh, one surface).
func kit_mesh(asset: String, lod: String) -> Mesh:
	var key := asset + "_" + lod
	if not _meshes.has(key):
		var scene: Node = (load(KIT_DIR + key + ".glb") as PackedScene).instantiate()
		var mi := scene.find_children("*", "MeshInstance3D", true, false)[0] as MeshInstance3D
		_meshes[key] = mi.mesh
		scene.free()
	return _meshes[key]


func run_shots(shots: Array) -> void:
	for shot in shots:
		var sid := str(shot["id"])
		print("[LandKit] shot %s" % sid)
		stage_environment(shot)
		var cam_spec: Dictionary = shot.get("camera", {"mode": "home"})
		var focus := LAYOUT.plot_centre(str(cam_spec.get("target", "pad_a")))
		if cam_spec.has("offset"):
			focus += Vector3(float(cam_spec["offset"][0]), 0.0, float(cam_spec["offset"][1]))
		focus = stage_camera(cam_spec, focus)
		MATERIAL.set_shader_parameter("night_light", float(shot.get("night_light", 0.0)))
		MATERIAL.set_shader_parameter("lid_variant", float(shot.get("lid_variant", 0.0)))
		await wait(0.3)
		var sample := _nearest_tank("pad_a", focus if str(cam_spec.get("mode", "home")) == "focus" else _cam.global_position)
		build(_cam.global_position, sample)
		await wait(2.5)
		var beauty := await grab()
		save(beauty, sid + ".png")
		var mask := await _mask(sample)
		save(mask, sid + "_mask.png")
		var rec := {
			"camera": {"spec": cam_spec, "position": [_cam.global_position.x, _cam.global_position.y, _cam.global_position.z],
				"fov_deg": snappedf(_cam.fov, 0.01)},
			"environment": {"sun_elevation_deg": snappedf(float(_director.call("get_composed").get("sun_elevation", 0.0)), 0.01),
				"night_light": float(shot.get("night_light", 0.0))},
			"sample_tank": _measure(beauty, mask, sample),
			"triangles_drawn": _tri_drawn,
		}
		if bool(shot.get("perf", false)):
			rec["perf"] = await _perf()
		_results[sid] = rec
	MATERIAL.set_shader_parameter("night_light", 0.0)
	MATERIAL.set_shader_parameter("lid_variant", 0.0)
	var size := get_viewport().get_visible_rect().size
	var path := write_json(METRICS_FILE, {
		"item": "F2-25", "generated_by": "res://scripts/qa/LandKitPreview.gd",
		"resolution": [int(size.x), int(size.y)], "shots": _results,
	})
	print("[LandKit] metrics: %s" % path)
	print("LANDKIT_RESULT shots=%d" % _results.size())
	get_tree().quit(0)


func _nearest_tank(plot_id: String, ref: Vector3) -> Dictionary:
	var best := {}
	var best_d := INF
	for p in LAYOUT.plot_placements(plot_id):
		if p["asset"] == "tank_growout":
			var d: float = (p["pos"] as Vector3).distance_to(ref)
			if d < best_d:
				best_d = d
				best = p
	return best


# ── Building the campus ──────────────────────────────────────────────────────

static func _band(d: float, bands: Vector2) -> int:
	return 0 if d < bands.x else (1 if d < bands.y else 2)


static func _xform(p: Dictionary) -> Transform3D:
	var pos: Vector3 = p["pos"]
	if p["asset"] == "pipe_bridge_span":
		pos.y += (p["size"] as Vector3).y  # the span's origin is its deck underside
	return Transform3D(Basis(Vector3.UP, float(p["yaw"])), pos)


## Build every plot for a camera at cam_pos; `sample` (a layout placement)
## is drawn on its own so the mask pass can find it.
func build(cam_pos: Vector3, sample: Dictionary) -> void:
	if _campus and is_instance_valid(_campus):
		_campus.get_parent().remove_child(_campus)
		_campus.free()
	_campus = Node3D.new()
	_campus.name = "LandKitCampus"
	_env.add_child(_campus)
	_tri_drawn = 0
	for plot_id in plots:
		var node := Node3D.new()
		node.name = plot_id
		_campus.add_child(node)
		_pad(node, plot_id)
		var tanks := {}
		var statics := {}
		for p in LAYOUT.plot_placements(plot_id):
			var asset: String = p["asset"]
			var xf := _xform(p)
			var d := cam_pos.distance_to(xf.origin)
			if asset.begins_with("tank_"):
				var band := _band(d, TANK_LOD_M)
				if not sample.is_empty() and p["pos"] == sample["pos"] and asset == sample["asset"]:
					_single(node, "SampleTank", asset, band, xf)
				else:
					_push(tanks, "%s|%d" % [asset, band], xf)
				if band < 2:
					var fxf := Transform3D(xf.basis, xf.origin + Vector3(0.0, FEEDER_Y[asset], 0.0))
					_push(tanks, "tank_feeder|%d" % band, fxf)
				continue
			var band := _band(d, UNIT_LOD_M)
			_push(statics, "%d|%d" % [int(p["lot"]), band], [asset, xf])
		for key in tanks:
			var parts: PackedStringArray = str(key).split("|")
			_multimesh(node, parts[0], int(parts[1]), tanks[key])
		for key in statics:
			var parts: PackedStringArray = str(key).split("|")
			_merged(node, "Lot%s_LOD%s" % [parts[0], parts[1]], int(parts[1]), statics[key])


static func _push(groups: Dictionary, key: String, value) -> void:
	if not groups.has(key):
		groups[key] = []
	groups[key].append(value)


## Tanks cast shadows through LOD1, everything else at LOD0 only (F2-18).
static func _shadow(asset: String, band: int) -> GeometryInstance3D.ShadowCastingSetting:
	var casts := band == 0 or (band == 1 and asset.begins_with("tank_") and asset != "tank_feeder")
	return GeometryInstance3D.SHADOW_CASTING_SETTING_ON if casts else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF


func _multimesh(parent: Node3D, asset: String, band: int, xforms: Array) -> void:
	var mesh := kit_mesh(asset, LODS[band])
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = mesh
	mm.instance_count = xforms.size()
	for i in xforms.size():
		mm.set_instance_transform(i, xforms[i])
	var mmi := MultiMeshInstance3D.new()
	mmi.name = "%s_LOD%d" % [asset, band]
	mmi.multimesh = mm
	mmi.material_override = MATERIAL
	mmi.cast_shadow = _shadow(asset, band)
	parent.add_child(mmi)
	_tri_drawn += _tris(mesh) * xforms.size()


func _single(parent: Node3D, node_name: String, asset: String, band: int, xf: Transform3D) -> void:
	var mi := MeshInstance3D.new()
	mi.name = node_name
	mi.mesh = kit_mesh(asset, LODS[band])
	mi.transform = xf
	mi.material_override = MATERIAL
	mi.cast_shadow = _shadow(asset, band)
	parent.add_child(mi)
	_tri_drawn += _tris(mi.mesh)


## Every static piece of a lot in one band, merged into one surface.
func _merged(parent: Node3D, node_name: String, band: int, pieces: Array) -> void:
	var st := SurfaceTool.new()
	var count := 0
	for piece in pieces:
		var mesh := kit_mesh(str(piece[0]), LODS[band])
		st.append_from(mesh, 0, piece[1])
		count += _tris(mesh)
	st.set_material(MATERIAL)
	var mi := MeshInstance3D.new()
	mi.name = node_name
	mi.mesh = st.commit()
	mi.material_override = MATERIAL
	mi.cast_shadow = _shadow("static", band)
	parent.add_child(mi)
	_tri_drawn += count


static func _tris(mesh: Mesh) -> int:
	var n := 0
	for s in mesh.get_surface_count():
		var idx: int = mesh.surface_get_array_index_len(s)
		n += (idx if idx > 0 else mesh.surface_get_array_len(s)) / 3
	return n


## Placeholder blasted-granite pad (#6B7378, wet #3E4448 on the cut faces)
## standing in for F2-19's carved terrain.
func _pad(parent: Node3D, plot_id: String) -> void:
	if _pad_material == null:
		var noise := FastNoiseLite.new()
		noise.seed = 6
		noise.frequency = 0.05
		noise.fractal_octaves = 5
		noise.fractal_gain = 0.6
		var tex := NoiseTexture2D.new()
		tex.noise = noise
		tex.seamless = true
		tex.width = 512
		tex.height = 512
		var ramp := Gradient.new()
		ramp.set_color(0, Color("#596166"))
		ramp.set_color(1, Color("#6E767B"))
		tex.color_ramp = ramp
		_pad_material = StandardMaterial3D.new()
		_pad_material.albedo_texture = tex
		_pad_material.albedo_color = Color(0.82, 0.84, 0.85)
		_pad_material.uv1_triplanar = true
		_pad_material.uv1_world_triplanar = true
		_pad_material.uv1_scale = Vector3(0.012, 0.012, 0.012)
		_pad_material.roughness = 0.95
		_carve_terrain()
	var c := LAYOUT.plot_centre(plot_id)
	var size: Vector2 = LAYOUT.PLOTS[plot_id]["size"]
	var mi := MeshInstance3D.new()
	mi.name = "Pad"
	var box := BoxMesh.new()
	box.size = Vector3(size.x, 30.0, size.y)
	mi.mesh = box
	mi.position = Vector3(c.x, c.y - 15.0, c.z)
	mi.material_override = _pad_material
	parent.add_child(mi)


## Preview only: stand in for F2-19's pad carving by pressing the terrain
## mesh under each pad (plus a 4 m margin) below the slab top, so hills
## inside a pad leave a cut face at its edge instead of poking through the
## slab. The heightfield, the collider and the terrain code are untouched.
func _carve_terrain() -> void:
	var terrain := _env.get_node_or_null("Terrain")
	if terrain == null or not terrain.has_method("get_chunk_meshes"):
		return
	var rects: Array[Rect2] = []
	for plot_id in plots:
		var c := LAYOUT.plot_centre(plot_id)
		var size: Vector2 = LAYOUT.PLOTS[plot_id]["size"]
		rects.append(Rect2(Vector2(c.x, c.z) - size * 0.5 - Vector2(4.0, 4.0), size + Vector2(8.0, 8.0)))
	var floor_y := LAYOUT.PAD_TOP_Y - 0.6
	for node in terrain.call("get_chunk_meshes"):
		var mi := node as MeshInstance3D
		var carved := carve_chunk(mi.mesh as ArrayMesh, mi.transform, rects, floor_y)
		if carved != null:
			mi.mesh = carved


## A terrain chunk's mesh (placed by `xform`, a translation) with every vertex
## inside `rects` (world XZ) and above `floor_y` (world) pressed down to it, or
## null when no vertex moves. The chunk's custom channels keep their formats:
## its CUSTOM0 is RGBA float since gfx-polish-1, and a rebuild without them
## failed ("Invalid array format for surface"), so the chunk drew nothing in
## the 39_landkit_* frames (found at the wave-4b close).
static func carve_chunk(mesh: ArrayMesh, xform: Transform3D, rects: Array[Rect2], floor_y: float) -> ArrayMesh:
	var arrays := mesh.surface_get_arrays(0)
	var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var changed := false
	for i in verts.size():
		var v := xform * verts[i]
		if v.y <= floor_y:
			continue
		for r in rects:
			if r.has_point(Vector2(v.x, v.z)):
				verts[i].y = floor_y - xform.origin.y
				changed = true
				break
	if not changed:
		return null
	arrays[Mesh.ARRAY_VERTEX] = verts
	var carved := ArrayMesh.new()
	carved.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays, [], {},
		custom_formats(mesh.surface_get_format(0)))
	return carved


## The custom channels' format bits of a surface format, for a surface
## rebuilt from its arrays (add_surface_from_arrays reads a custom array as
## bytes unless its flags say otherwise).
static func custom_formats(surface_format: int) -> int:
	var mask := 0
	for shift: int in [Mesh.ARRAY_FORMAT_CUSTOM0_SHIFT, Mesh.ARRAY_FORMAT_CUSTOM1_SHIFT,
			Mesh.ARRAY_FORMAT_CUSTOM2_SHIFT, Mesh.ARRAY_FORMAT_CUSTOM3_SHIFT]:
		mask |= Mesh.ARRAY_FORMAT_CUSTOM_MASK << shift
	return surface_format & mask


# ── Measuring ────────────────────────────────────────────────────────────────

func _mask(sample: Dictionary) -> Image:
	var saved := begin_plain([_env.get_node_or_null("WaterGrid"), _env.get_node_or_null("Terrain"), _campus])
	var black := unshaded(Color.BLACK)
	var strip := ShaderMaterial.new()
	strip.shader = MASK_SHADER
	for gi in _campus.find_children("*", "GeometryInstance3D", true, false):
		gi.material_override = strip if str(gi.name) == "SampleTank" else black
	var img := await grab()
	for gi in _campus.find_children("*", "GeometryInstance3D", true, false):
		gi.material_override = _pad_material if str(gi.name) == "Pad" else MATERIAL
	end_plain(saved)
	return img


func _measure(beauty: Image, mask: Image, sample: Dictionary) -> Dictionary:
	var w := mask.get_width()
	var h := mask.get_height()
	var md := mask.get_data()
	var bd := beauty.get_data()
	var cls := [MASK_LID, MASK_WALL, MASK_BAND]
	var names := ["lid", "wall", "band"]
	var stats := {}
	for n in names:
		stats[n] = {"pixels": 0, "x0": w, "y0": h, "x1": -1, "y1": -1, "l": 0.0}
	for y in h:
		for x in w:
			var o := (y * w + x) * 4
			if maxi(md[o], maxi(md[o + 1], md[o + 2])) < 200:
				continue
			for k in 3:
				var c: Color = cls[k]
				if absi(md[o] - c.r8) + absi(md[o + 1] - c.g8) + absi(md[o + 2] - c.b8) < 60:
					var s: Dictionary = stats[names[k]]
					s["pixels"] += 1
					s["x0"] = mini(s["x0"], x)
					s["y0"] = mini(s["y0"], y)
					s["x1"] = maxi(s["x1"], x)
					s["y1"] = maxi(s["y1"], y)
					s["l"] += lstar(bd, o)
					break
	var out := {"position": [sample["pos"].x, sample["pos"].y, sample["pos"].z],
		"distance_m": snappedf(_cam.global_position.distance_to(sample["pos"]), 0.1)}
	for n in names:
		var s: Dictionary = stats[n]
		if s["pixels"] == 0:
			out[n] = {"pixels": 0}
			continue
		out[n] = {"pixels": s["pixels"], "width_px": s["x1"] - s["x0"] + 1, "height_px": s["y1"] - s["y0"] + 1,
			"l_mean": snappedf(s["l"] / s["pixels"], 0.01)}
	if out["lid"].has("l_mean") and out["wall"].has("l_mean"):
		out["delta_l_lid_wall"] = snappedf(absf(float(out["wall"]["l_mean"]) - float(out["lid"]["l_mean"])), 0.01)
	return out


## Draw calls the campus adds (opaque and shadow), off / on.
func _perf() -> Dictionary:
	var vp := get_viewport()
	var out := {}
	for mode in ["off", "on"]:
		_campus.visible = mode == "on"
		for i in 8:
			await RenderingServer.frame_post_draw
		out[mode] = {
			"opaque_draws": vp.get_render_info(Viewport.RENDER_INFO_TYPE_VISIBLE, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME),
			"shadow_draws": vp.get_render_info(Viewport.RENDER_INFO_TYPE_SHADOW, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME),
		}
	_campus.visible = true
	out["added_opaque_draws"] = int(out["on"]["opaque_draws"]) - int(out["off"]["opaque_draws"])
	out["added_shadow_draws"] = int(out["on"]["shadow_draws"]) - int(out["off"]["shadow_draws"])
	return out
