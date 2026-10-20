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
##
## G2-01: a far field surrounds the 4 km world out to 16 km (the generator's
## FarFieldHeightAt: the same fjord system continuing into a coast with
## neighbouring fjords, inland massifs and islands): a ring of chunks drawn
## with the terrain material, zipped to the heightfield's border so no seam
## opens, uploaded after the world under the same frame budget. Height
## queries outside the world answer from the far field.

signal terrain_ready
## The far-field ring is uploaded (it follows terrain_ready by a few frames).
signal far_field_ready

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
## G2-02: the terrain material's two Texture2DArrays (tools/gen_terrain_textures.py):
## seven layers of albedo + height and of normal + roughness + AO.
const DETAIL_TEXTURES := {
	"albedo_height": "res://assets/textures/terrain/terrain_albedo_height.png",
	"normal_rough_ao": "res://assets/textures/terrain/terrain_normal_rough_ao.png",
}
## Birch and spruce on the slopes (G2-02): MultiMesh cells built from the
## same masks the shader paints the woods with.
const VEGETATION_SCENE := "res://scenes/environment/Vegetation.tscn"
## Naust boathouses and a quay on sheltered shore (G2-02, ShorePropPlacer).
const SHORE_PROPS_SCENE := "res://scenes/environment/ShoreProps.tscn"

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
## G2-02: the trees as MultiMesh-ready cells (TerrainGeneratorBridge.VegetationChunks),
## how many there are, and the mean treeline the masks used.
var vegetation_chunks: Array = []
var tree_count: int = 0
var treeline_m: float = 500.0
## The Vegetation node (scenes/environment/Vegetation.tscn), once the terrain is up.
var vegetation: Node = null
## G2-01: the far-field ring's chunks (a Node3D, once the upload starts) and
## the far field's half-extent round the origin (m).
var far_field: Node3D = null
var far_field_extent: float = 0.0
## Worker-thread time of the far field (part of last_generation_ms).
var far_field_generation_ms: float = 0.0
## Shore buildings from the build ({kind, position, yaw, variant}) and their node.
var shore_props: Array = []
var shore_props_node: Node = null

var _generator: RefCounted = null
var _heights := PackedFloat32Array()
var _spacing: float = 0.0
var _chunk_root: Node3D
var _pending_chunks: Array = []
var _pending_far: Array = []
var _build_result: Dictionary = {}
var _collision_shape: CollisionShape3D
var _material: ShaderMaterial
var _gen_thread: Thread = null
var _water_bake_image: Image = null
var _requested: Array[String] = []
## F2-19: pad id -> state (0 heath, 1 blasted granite) for the shader's pad
## splat; pads never set stay heath.
var _pad_states: Dictionary = {}
var _pad_cache: Array = []
var _pad_blasts: Dictionary = {}
## Most pads the terrain shader paints (its pad_rects[] size).
const MAX_SHADER_PADS := 4


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
	_pad_cache = []
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
	vegetation_chunks = result.get("vegetation", [])
	tree_count = int(result.get("tree_count", 0))
	treeline_m = float(result.get("treeline_m", treeline_m))
	shore_props = result.get("shore_props", [])
	vegetation = null
	shore_props_node = null
	far_field = null
	far_field_extent = float(result.get("far_field_extent", 0.0))
	far_field_generation_ms = float(result.get("far_field_ms", 0.0))
	_pending_far = (result.get("far_field", []) as Array).duplicate()

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
	if _pending_chunks.is_empty() and _pending_far.is_empty():
		set_process(false)
		return
	if _pending_chunks.is_empty():
		_upload_far_field()
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
		set_process(not _pending_far.is_empty())
		print("[FjordTerrain] %dx%d fjord, %.1f m grid, hash %s, %d trees (worker %.0f ms, main-thread steps %s ms)" % [
			terrain_resolution, terrain_resolution, _spacing, heightfield_hash, tree_count, last_generation_ms,
			", ".join(Array(upload_steps_ms).map(func(ms: float) -> String: return "%.1f" % ms))])
		_add_vegetation()
		_add_shore_props()
		terrain_ready.emit()


## G2-01: the far-field chunks go up after the world, a few per frame under
## the same budget. The band next to the world casts shadows (its mountains
## can shade the fjord in a low sun); the outer bands lie past every shadow
## distance.
func _upload_far_field() -> void:
	if far_field == null:
		far_field = Node3D.new()
		far_field.name = "FarField"
		add_child(far_field)
	var started := Time.get_ticks_usec()
	while not _pending_far.is_empty():
		var chunk: Dictionary = _pending_far.pop_front()
		var instance := _chunk_instance(chunk, "Far_%d_%d" % [int(chunk["first_column"]), int(chunk["first_row"])])
		if int(chunk.get("band", 0)) > 0:
			instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		far_field.add_child(instance)
		if Time.get_ticks_usec() - started > CHUNK_FRAME_BUDGET_MS * 1000.0:
			break
	_note_upload_frame(started)
	if _pending_far.is_empty():
		set_process(false)
		print("[FjordTerrain] far field: %d chunks to %.0f km (worker %.0f ms)" % [far_field.get_child_count(), far_field_extent / 1000.0, far_field_generation_ms])
		far_field_ready.emit()


## True once the far-field ring is in place.
func is_far_field_ready() -> bool:
	return far_field != null and _pending_far.is_empty()


## The shore buildings go up with the trees (a handful of instances).
func _add_shore_props() -> void:
	if shore_props.is_empty() or not ResourceLoader.exists(SHORE_PROPS_SCENE):
		return
	var scene := load(SHORE_PROPS_SCENE) as PackedScene
	if scene == null:
		return
	shore_props_node = scene.instantiate()
	shore_props_node.name = "ShoreProps"
	add_child(shore_props_node)
	shore_props_node.call("build", shore_props)


## The trees go up after the terrain, a few cells per frame (Vegetation.gd).
func _add_vegetation() -> void:
	if vegetation_chunks.is_empty() or not ResourceLoader.exists(VEGETATION_SCENE):
		return
	var scene := load(VEGETATION_SCENE) as PackedScene
	if scene == null:
		return
	vegetation = scene.instantiate()
	vegetation.name = "Vegetation"
	add_child(vegetation)
	vegetation.call("build", vegetation_chunks)


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
	_chunk_root.add_child(_chunk_instance(chunk, "Chunk_%d_%d" % [int(chunk["first_column"]), int(chunk["first_row"])]))


func _chunk_instance(chunk: Dictionary, instance_name: String) -> MeshInstance3D:
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
	instance.name = instance_name
	instance.mesh = mesh
	instance.material_override = _material
	instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	return instance


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
	_material.set_shader_parameter("treeline_m", treeline_m)
	var missing: Array[String] = []
	for uniform_name in DETAIL_TEXTURES:
		var texture := _take_resource(DETAIL_TEXTURES[uniform_name]) as Texture
		if texture == null:
			missing.append(DETAIL_TEXTURES[uniform_name])
			continue
		_material.set_shader_parameter(uniform_name, texture)
	if missing.is_empty():
		_material.set_shader_parameter("use_textures", 1.0)
	else:
		push_warning("[FjordTerrain] Missing terrain textures: %s — palette only" % ", ".join(missing))
		_material.set_shader_parameter("use_textures", 0.0)
	_apply_pads()
	return _material


## F2-19: the land pads' rectangles and states into the terrain shader (the
## generator carves the same pads into the heightfield).
func _apply_pads() -> void:
	if _material == null:
		return
	# The pads are fixed for the generator's settings (and computing their
	# intake reach walks the grid): read them once.
	if _pad_cache.is_empty():
		_pad_cache = land_pad_slots()
	var pads := _pad_cache
	var rects := PackedVector4Array()
	var states := PackedFloat32Array()
	var blasts := PackedVector4Array()
	var rim := 30.0
	for pad: Dictionary in pads.slice(0, MAX_SHADER_PADS):
		var c: Vector3 = pad["centre"]
		var size: Vector2 = pad["size"]
		rects.append(Vector4(c.x, c.z, size.x * 0.5, size.y * 0.5))
		states.append(float(_pad_states.get(str(pad["id"]), 0.0)))
		blasts.append(_pad_blasts.get(str(pad["id"]), Vector4.ZERO))
		rim = float(pad.get("rim_m", rim))
	while rects.size() < MAX_SHADER_PADS:
		rects.append(Vector4.ZERO)
		states.append(0.0)
		blasts.append(Vector4.ZERO)
	_material.set_shader_parameter("pad_rects", rects)
	_material.set_shader_parameter("pad_state", states)
	_material.set_shader_parameter("pad_blast", blasts)
	_material.set_shader_parameter("pad_count", mini(pads.size(), MAX_SHADER_PADS))
	_material.set_shader_parameter("pad_rim_m", rim)


## F2-19: paint a land pad as heath (0, unbuilt), blasted granite (1) or the
## blast spreading across it (between). F2-20: `blast` (x0, z0, x1, z1, world
## metres) limits the granite to the ground a campus builds on; zero blasts
## the whole pad. The heightfield never changes.
func set_pad_state(pad_id: String, state: float, blast: Vector4 = Vector4.ZERO) -> void:
	var s := clampf(state, 0.0, 1.0)
	if _pad_states.has(pad_id) and is_equal_approx(float(_pad_states[pad_id]), s) \
			and (_pad_blasts.get(pad_id, Vector4.ZERO) as Vector4).is_equal_approx(blast):
		return
	_pad_states[pad_id] = s
	_pad_blasts[pad_id] = blast
	_apply_pads()


## The state a pad is painted with (0 heath .. 1 blasted granite).
func get_pad_state(pad_id: String) -> float:
	return float(_pad_states.get(pad_id, 0.0))


## The rectangle a pad's blast is limited to (zero: the whole pad).
func get_pad_blast(pad_id: String) -> Vector4:
	return _pad_blasts.get(pad_id, Vector4.ZERO)


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


## The terrain's ShaderMaterial (null until the textures have loaded).
func get_material() -> ShaderMaterial:
	return _material


## Terrain height in metres at a world position: bilinear over the built
## grid, or the generator's analytic height before the grid exists; outside
## the world, the far field's (G2-01).
func get_height_at(world_x: float, world_z: float) -> float:
	if absf(world_x) > terrain_size.x * 0.5 or absf(world_z) > terrain_size.y * 0.5:
		return far_field_height_at(world_x, world_z)
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


## Height (m) at any world position out to the far field: the world's own
## inside it (analytic), the region beyond it.
func far_field_height_at(world_x: float, world_z: float) -> float:
	if _generator == null and not _ensure_generator():
		return 0.0
	return float(_generator.call("FarFieldHeightAt", world_x, world_z))


## Half-extent (m) of the far field round the origin (known before the build).
func far_field_half_extent() -> float:
	if far_field_extent <= 0.0 and (_generator != null or _ensure_generator()):
		far_field_extent = float(_generator.call("FarFieldExtent"))
	return far_field_extent


## The land pads reserved for land-based farms (farm-types spec §4): an Array
## of {id, name, centre, size, lots, quay, intake_reach_m}.
func land_pad_slots() -> Array:
	if _generator == null and not _ensure_generator():
		return []
	return _generator.call("LandPadSlots")


## The open-sea slots reserved for offshore units (spec §4): an Array of
## {id, name, position, exposure, water_depth_m}.
func offshore_slots() -> Array:
	if _generator == null and not _ensure_generator():
		return []
	return _generator.call("OffshoreSlots")


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


## Pen anchor positions along the fjord channel, in world metres (Y = 0).
func get_recommended_pen_positions(count: int = 12) -> Array[Vector3]:
	var positions: Array[Vector3] = []
	if _generator == null and not _ensure_generator():
		return positions
	positions.assign(_generator.call("GetRecommendedPenPositions", count))
	return positions
