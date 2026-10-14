@tool
class_name FjordTerrain
extends Node3D
## Norwegian fjord terrain: a U-shaped glacial trough in world metres.
##
## The heightfield comes from ONE generator, LakskongenSim's C#
## TerrainGenerator, reached through TerrainGeneratorBridge.cs. Headless and
## windowed runs take the same path, so they build the same heightfield
## (heightfield_hash). The grid and every render array are built on a worker
## thread; the main thread only hands the arrays to the GPU and the physics
## server. Height queries made before the grid lands are answered by the
## generator's analytic height function, so pens and boat routes placed
## during the first frames sit on the same fjord.

signal terrain_ready

@export_group("Terrain Size")
@export var terrain_size: Vector2 = Vector2(4000.0, 4000.0)
## Vertices per side: 512 gives 7.8 m spacing over 4 km.
@export var terrain_resolution: int = 512
@export var water_level: float = 0.0
## Seed for the generator's noise fields (the fjord's layout stays the same).
@export var terrain_seed: int = 42

@export_group("Generate")
@export var regenerate: bool = false:
	set(value):
		if value:
			generate_terrain()
			regenerate = false

const BRIDGE_PATH := "res://scripts/environment/TerrainGeneratorBridge.cs"
const TERRAIN_SHADER := "res://shaders/terrain_shader.gdshader"
## Main-thread time per frame for chunk uploads (a chunk costs 0.2-1 ms on an
## idle M1 Pro, more under load).
const CHUNK_FRAME_BUDGET_MS := 4.0
## Detail textures for the terrain shader (luminance and normal detail only).
const DETAIL_TEXTURES := {
	"rock_tex": "res://assets/textures/terrain_rock.png",
	"rock_normal_tex": "res://assets/textures/terrain_rock_normal.png",
	"moss_tex": "res://assets/textures/terrain_moss.png",
	"moss_normal_tex": "res://assets/textures/terrain_moss_normal.png",
	"snow_normal_tex": "res://assets/textures/terrain_snow_normal.png",
}

## Heightfield fingerprint (FNV-1a 64 over the height bits), set when built.
var heightfield_hash: String = ""
## Main-thread time of the latest terrain upload step, and the slowest one.
var last_main_thread_ms: float = 0.0
var max_upload_frame_ms: float = 0.0
## Every main-thread upload step of the latest build (ms), in order.
var upload_steps_ms := PackedFloat32Array()
## Worker-thread time for the heightfield and the render arrays.
var last_generation_ms: float = 0.0
## G1-05: what the opaque water knows about the ground under it, one texel
## per vertex (RGBA half floats): R depth below sea level (m, negative over
## land), G signed distance to the shoreline (m, positive over water),
## B exposure to the open sea (0 basin, 1 open coast), A seabed slope.
## Null until the build lands.
var water_bake_texture: ImageTexture = null
## Effective fetch (m) of the sheltered basin for the prevailing sea, and
## the direction (x, z) that sea travels.
var sheltered_fetch_m: float = 3000.0
var sea_direction: Vector2 = Vector2(0.3, 1.0).normalized()

var _generator: RefCounted = null
var _heights := PackedFloat32Array()
var _spacing: float = 0.0
var _chunk_root: Node3D
var _pending_chunks: Array = []
var _build_result: Dictionary = {}
var _collision_shape: CollisionShape3D
var _material: ShaderMaterial
var _season: float = 0.5
var _gen_thread: Thread = null
var _water_bake_image: Image = null
var _requested: Array[String] = []


func _ready() -> void:
	set_process(false)
	if _chunk_root == null:
		generate_terrain()


func _exit_tree() -> void:
	_finish_thread()


## Start (or restart) generation on a worker thread. The terrain_ready signal
## fires on the main thread once the mesh and collider are in place.
func generate_terrain() -> void:
	_finish_thread()
	if not _ensure_generator():
		return
	_request_resources()
	_gen_thread = Thread.new()
	var err := _gen_thread.start(_thread_build)
	if err != OK:
		push_warning("[FjordTerrain] worker thread failed to start (%s); building on the main thread" % err)
		_gen_thread = null
		_apply_build(_generator.call("Build"))


## True once the grid, every mesh chunk and the collider are in place.
func is_ready() -> bool:
	return not _heights.is_empty() and _pending_chunks.is_empty() and _build_result.is_empty() \
		and _collision_shape != null and is_instance_valid(_collision_shape)


func _ensure_generator() -> bool:
	if _generator == null:
		var script := load(BRIDGE_PATH) if ResourceLoader.exists(BRIDGE_PATH) else null
		if script == null or not script.can_instantiate():
			push_error("[FjordTerrain] TerrainGeneratorBridge.cs is unavailable (is the C# assembly built?)")
			return false
		_generator = script.new()
	# Dynamic dispatch: the C# type is invisible to the GDScript compiler.
	_generator.call("Configure", terrain_seed, terrain_size.x, terrain_resolution)
	return true


## Worker thread: pure C# compute, no scene-tree access.
func _thread_build() -> void:
	var result: Dictionary = _generator.call("Build")
	_on_build_ready.call_deferred(result)


func _on_build_ready(result: Dictionary) -> void:
	_finish_thread()
	_apply_build(result)


func _finish_thread() -> void:
	if _gen_thread != null:
		if _gen_thread.is_started():
			_gen_thread.wait_to_finish()
		_gen_thread = null


func _apply_build(result: Dictionary) -> void:
	var started := Time.get_ticks_usec()
	_heights = result["heights"]
	_spacing = float(result["spacing"])
	heightfield_hash = str(result["hash"])
	last_generation_ms = float(result["build_ms"])
	_build_result = result
	max_upload_frame_ms = 0.0
	upload_steps_ms = PackedFloat32Array()
	_apply_water_bake(result)

	for child in get_children():
		child.queue_free()
	_chunk_root = Node3D.new()
	_chunk_root.name = "TerrainChunks"
	add_child(_chunk_root)
	_pending_chunks = (result["chunks"] as Array).duplicate()
	_material = null
	_note_upload_frame(started)
	set_process(true)


## Main thread: hand finished chunk arrays to the GPU, a few per frame, so
## no frame spends more than CHUNK_FRAME_BUDGET_MS on the terrain. The
## collider goes in once every chunk is up, then terrain_ready fires.
func _process(_delta: float) -> void:
	if _pending_chunks.is_empty():
		set_process(false)
		return
	# The material waits for the loader threads instead of blocking on them.
	if _material == null:
		if not _resources_loaded():
			return
		var material_started := Time.get_ticks_usec()
		_create_terrain_material()
		_note_upload_frame(material_started)
		return
	var started := Time.get_ticks_usec()
	while not _pending_chunks.is_empty():
		_add_chunk(_pending_chunks.pop_front())
		if Time.get_ticks_usec() - started > CHUNK_FRAME_BUDGET_MS * 1000.0:
			break
	_note_upload_frame(started)
	if _pending_chunks.is_empty():
		var collider_started := Time.get_ticks_usec()
		_add_collider()
		_note_upload_frame(collider_started)
		_build_result = {}
		set_process(false)
		print("[FjordTerrain] %dx%d fjord, %.1f m grid, hash %s (worker %.0f ms, main-thread steps %s ms)" % [
			terrain_resolution, terrain_resolution, _spacing, heightfield_hash, last_generation_ms,
			", ".join(Array(upload_steps_ms).map(func(ms: float) -> String: return "%.1f" % ms))])
		terrain_ready.emit()


func _apply_water_bake(result: Dictionary) -> void:
	if not result.has("water_bake"):
		return
	var n := int(result["resolution"])
	_water_bake_image = Image.create_from_data(n, n, false, Image.FORMAT_RGBAH, result["water_bake"])
	water_bake_texture = ImageTexture.create_from_image(_water_bake_image)
	sheltered_fetch_m = float(result["sheltered_fetch_m"])
	sea_direction = result["sea_direction"]


## The water bake at a world position (nearest texel): r depth (m), g shore
## distance (m), b open-sea exposure, a seabed slope. Black before the build.
func water_bake_at(world_x: float, world_z: float) -> Color:
	if _water_bake_image == null:
		return Color(0.0, 0.0, 0.0, 0.0)
	var n := _water_bake_image.get_width()
	var spacing := terrain_size.x / float(n - 1)
	var c := clampi(roundi((world_x + terrain_size.x * 0.5) / spacing), 0, n - 1)
	var r := clampi(roundi((world_z + terrain_size.y * 0.5) / spacing), 0, n - 1)
	return _water_bake_image.get_pixel(c, r)


## The water bake image the texture was made from (null before the build):
## WaveField samples it on the CPU exactly as the water shader does.
func get_water_bake_image() -> Image:
	return _water_bake_image


## World-space rectangle the water bake covers (x, z, width, depth).
func get_water_bake_rect() -> Rect2:
	return Rect2(-terrain_size * 0.5, terrain_size)


func _note_upload_frame(started_usec: int) -> void:
	last_main_thread_ms = float(Time.get_ticks_usec() - started_usec) / 1000.0
	max_upload_frame_ms = maxf(max_upload_frame_ms, last_main_thread_ms)
	upload_steps_ms.append(last_main_thread_ms)


func _add_chunk(chunk: Dictionary) -> void:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = chunk["vertices"]
	arrays[Mesh.ARRAY_NORMAL] = chunk["normals"]
	arrays[Mesh.ARRAY_TANGENT] = chunk["tangents"]
	arrays[Mesh.ARRAY_TEX_UV] = chunk["uvs"]
	arrays[Mesh.ARRAY_COLOR] = chunk["colors"]
	arrays[Mesh.ARRAY_INDEX] = chunk["indices"]
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	var instance := MeshInstance3D.new()
	instance.name = "Chunk_%d_%d" % [int(chunk["first_column"]), int(chunk["first_row"])]
	instance.mesh = mesh
	instance.material_override = _material
	instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	_chunk_root.add_child(instance)


## HeightMapShape3D spaces its samples 1 unit apart, centred on the origin:
## scale the shape uniformly by the grid spacing (the C# side already divided
## the heights by it) instead of a 500k-face concave mesh.
func _add_collider() -> void:
	var shape := HeightMapShape3D.new()
	var n := int(_build_result["resolution"])
	shape.map_width = n
	shape.map_depth = n
	shape.map_data = _build_result["collider_heights"]
	var body := StaticBody3D.new()
	body.name = "TerrainBody"
	_collision_shape = CollisionShape3D.new()
	_collision_shape.shape = shape
	_collision_shape.scale = Vector3.ONE * _spacing
	body.add_child(_collision_shape)
	add_child(body)


## Every terrain chunk mesh (empty until the upload finishes).
func get_chunk_meshes() -> Array[MeshInstance3D]:
	var meshes: Array[MeshInstance3D] = []
	if _chunk_root != null and is_instance_valid(_chunk_root):
		for child in _chunk_root.get_children():
			if child is MeshInstance3D:
				meshes.append(child)
	return meshes


func _create_terrain_material() -> Material:
	var shader := _take_resource(TERRAIN_SHADER) as Shader
	if shader == null:
		push_error("[FjordTerrain] %s is missing" % TERRAIN_SHADER)
		shader = Shader.new()
	_material = ShaderMaterial.new()
	_material.shader = shader
	_material.set_shader_parameter("water_level", water_level)
	_material.set_shader_parameter("season", _season)
	var missing: Array[String] = []
	for uniform_name in DETAIL_TEXTURES:
		var texture := _take_resource(DETAIL_TEXTURES[uniform_name]) as Texture2D
		if texture == null:
			missing.append(DETAIL_TEXTURES[uniform_name])
			continue
		_material.set_shader_parameter(uniform_name, texture)
	if missing.is_empty():
		_material.set_shader_parameter("use_textures", 1.0)
	else:
		push_warning("[FjordTerrain] Missing terrain textures: %s — palette only" % ", ".join(missing))
		_material.set_shader_parameter("use_textures", 0.0)
	return _material


## Queue the shader and detail textures on the loader threads while the
## heightfield builds, so the main thread never waits on disk.
func _request_resources() -> void:
	var paths: Array = [TERRAIN_SHADER]
	paths.append_array(DETAIL_TEXTURES.values())
	for path: String in paths:
		if ResourceLoader.exists(path) and not ResourceLoader.has_cached(path) and not (path in _requested):
			ResourceLoader.load_threaded_request(path)
			_requested.append(path)


## True once every queued shader/texture load has finished (or failed).
func _resources_loaded() -> bool:
	for path in _requested:
		if ResourceLoader.load_threaded_get_status(path) == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
			return false
	return true


func _take_resource(path: String) -> Resource:
	if path in _requested:
		_requested.erase(path)
		return ResourceLoader.load_threaded_get(path)
	if not ResourceLoader.exists(path):
		return null
	return load(path)


## Season for the terrain material (0 = winter, 0.5 = summer); kept until
## the material exists.
func set_season(s: float) -> void:
	_season = s
	if _material != null:
		_material.set_shader_parameter("season", s)


## Terrain height in metres at a world position: bilinear over the built
## grid, or the generator's analytic height before the grid exists.
func get_height_at(world_x: float, world_z: float) -> float:
	if _heights.is_empty():
		if _generator == null and not _ensure_generator():
			return 0.0
		return float(_generator.call("HeightAt", world_x, world_z))
	var size := terrain_resolution
	var fx := clampf((world_x + terrain_size.x / 2.0) / _spacing, 0.0, float(size - 1))
	var fz := clampf((world_z + terrain_size.y / 2.0) / _spacing, 0.0, float(size - 1))
	var ix := mini(int(fx), size - 2)
	var iz := mini(int(fz), size - 2)
	var tx := fx - float(ix)
	var tz := fz - float(iz)
	var h00 := _heights[iz * size + ix]
	var h10 := _heights[iz * size + ix + 1]
	var h01 := _heights[(iz + 1) * size + ix]
	var h11 := _heights[(iz + 1) * size + ix + 1]
	return lerpf(lerpf(h00, h10, tx), lerpf(h01, h11, tx), tz)


## World X of the fjord channel's centreline at world Z.
func channel_center_x(world_z: float) -> float:
	if _generator == null and not _ensure_generator():
		return 0.0
	return float(_generator.call("CenterX", world_z))


## Half-width of the fairway kept open along the centreline, in metres.
func fairway_half_width() -> float:
	if _generator == null and not _ensure_generator():
		return 0.0
	return float(_generator.call("FairwayHalfWidth"))


## World Z range (x = start, y = end) of the pen slots along the basin.
func pen_slot_range() -> Vector2:
	if _generator == null and not _ensure_generator():
		return Vector2(-terrain_size.y * 0.4, terrain_size.y * 0.4)
	return _generator.call("PenSlotRange")


## World Z of the sill at the fjord mouth (x) and of the fjord head (y).
func mouth_and_head() -> Vector2:
	if _generator == null and not _ensure_generator():
		return Vector2(-terrain_size.y * 0.5, terrain_size.y * 0.5)
	return _generator.call("MouthAndHead")


## Sea-level waypoints along the channel from from_z to to_z, `lane` metres
## beside the centreline (clamped to the fairway) and every `step` metres. A
## boat that follows them stays on open water and passes beside the pens
## anchored on the centreline.
func channel_route(from_z: float, to_z: float, lane: float, step: float = 100.0) -> PackedVector3Array:
	var half := fairway_half_width()
	var offset := clampf(lane, -half, half)
	var count := maxi(1, ceili(absf(to_z - from_z) / step))
	var points := PackedVector3Array()
	for i in range(count + 1):
		var z := lerpf(from_z, to_z, float(i) / float(count))
		points.append(Vector3(channel_center_x(z) + offset, 0.0, z))
	return points


## Pen anchor positions along the fjord channel, in world metres (Y = 0).
func get_recommended_pen_positions(count: int = 12) -> Array[Vector3]:
	var positions: Array[Vector3] = []
	if _generator == null and not _ensure_generator():
		return positions
	positions.assign(_generator.call("GetRecommendedPenPositions", count))
	return positions
