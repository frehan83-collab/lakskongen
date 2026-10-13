@tool
class_name FjordTerrain
extends Node3D
## Procedural Norwegian fjord terrain generator.
## Creates heightmap-based terrain with cliff walls, water channels, islands, and beaches.

@export_group("Terrain Size")
@export var terrain_size: Vector2 = Vector2(4000.0, 4000.0)
@export var terrain_resolution: int = 256  # Vertices per side
@export var max_height: float = 400.0
@export var water_level: float = 0.0

@export_group("Fjord Shape")
## How deep the main fjord channel cuts
@export_range(0.0, 1.0) var fjord_depth: float = 0.8
## Width of main fjord (fraction of terrain)
@export_range(0.0, 0.5) var fjord_width: float = 0.15
## Steepness of cliff walls
@export_range(1.0, 10.0) var cliff_steepness: float = 2.0
## Minimum fairway depth in heightfield units (0.10 ~= 40 m at max_height
## 400: clears the 40 m pen nets with margin).
@export_range(0.02, 0.3) var channel_floor: float = 0.10
## Number of scattered islands
@export_range(0, 10) var island_count: int = 4
## Random seed for terrain generation
@export var terrain_seed: int = 42

@export_group("Generate")
@export var regenerate: bool = false:
	set(value):
		if value:
			generate_terrain()
			regenerate = false

## C# bridge (TerrainGeneratorBridge.cs) exposing the seeded LakskongenSim
## ridged-fractal + domain-warp generator. Loaded dynamically so headless or
## non-.NET runs fall back to the GDScript ridged path below.
const BRIDGE_PATH := "res://scripts/environment/TerrainGeneratorBridge.cs"

var _mesh_instance: MeshInstance3D
var _collision_shape: CollisionShape3D
var _heightmap: PackedFloat32Array
var _gen_thread: Thread = null
var _pending_heightmap: PackedFloat32Array = PackedFloat32Array()


func _ready() -> void:
	if _mesh_instance == null:
		generate_terrain()


func _exit_tree() -> void:
	_finish_thread()


## Entry point. Headless (QA, screenshots prep) runs synchronously so the
## heightmap is ready on return; interactive sessions generate on a worker
## thread and build the mesh deferred on the main thread.
func generate_terrain() -> void:
	if DisplayServer.get_name() == "headless":
		_heightmap = _build_heightmap_sync()
		_build_terrain_mesh()
	else:
		generate_terrain_async()


## Threaded generation for interactive sessions. The worker thread only
## touches a local FastNoiseLite instance and local math (never the scene
## tree); the mesh is built deferred on the main thread.
func generate_terrain_async() -> void:
	_finish_thread()
	_gen_thread = Thread.new()
	var params := {
		"resolution": terrain_resolution,
		"seed": terrain_seed,
		"fjord_width": fjord_width,
		"fjord_depth": fjord_depth,
		"cliff_steepness": cliff_steepness,
		"channel_floor": channel_floor,
		"island_count": island_count,
	}
	var err := _gen_thread.start(_thread_build_heightmap.bind(params))
	if err != OK:
		push_warning("[FjordTerrain] Thread start failed (%s), falling back to sync" % err)
		_gen_thread = null
		_heightmap = _build_heightmap_sync()
		_build_terrain_mesh()


func _thread_build_heightmap(params: Dictionary) -> void:
	var noise := _make_ridged_noise(int(params["seed"]))
	var hm := _generate_heightmap_gd(
		noise,
		int(params["resolution"]),
		float(params["fjord_width"]),
		float(params["fjord_depth"]),
		float(params["cliff_steepness"]),
		float(params["channel_floor"]),
		int(params["island_count"]))
	_pending_heightmap = hm
	call_deferred("_on_heightmap_ready")


func _on_heightmap_ready() -> void:
	_finish_thread()
	if _pending_heightmap.is_empty():
		return
	_heightmap = _pending_heightmap
	_pending_heightmap = PackedFloat32Array()
	_build_terrain_mesh()


func _finish_thread() -> void:
	if _gen_thread != null:
		if _gen_thread.is_started():
			_gen_thread.wait_to_finish()
		_gen_thread = null


## Synchronous heightfield builder. Prefers the C# TerrainGenerator bridge
## (seeded ridged fractal + domain warp, internally parallel); falls back to
## the GDScript ridged + domain-warp implementation when .NET is missing.
func _build_heightmap_sync() -> PackedFloat32Array:
	var hm := _try_csharp_heightmap(terrain_resolution)
	if not hm.is_empty():
		return hm
	var noise := _make_ridged_noise(terrain_seed)
	return _generate_heightmap_gd(
		noise, terrain_resolution, fjord_width, fjord_depth,
		cliff_steepness, channel_floor, island_count)


func _try_csharp_heightmap(size: int) -> PackedFloat32Array:
	if not ResourceLoader.exists(BRIDGE_PATH):
		return PackedFloat32Array()
	var scr := load(BRIDGE_PATH)
	if scr == null or not scr.can_instantiate():
		return PackedFloat32Array()
	# NOTE: dynamic dispatch (var + call) — the C# type is invisible to the
	# GDScript compiler, so := inference and static calls fail parsing.
	var bridge = scr.new()
	var result: PackedFloat32Array = PackedFloat32Array(
		bridge.call(
			"GenerateHeightmap",
			size, terrain_seed, fjord_width, fjord_depth,
			cliff_steepness, island_count, channel_floor))
	bridge.free()
	return result


## Ridged massifs + domain warp: sharp crests for fjord walls, warped domain
## so ridges wander like real glacial valleys instead of lining up on axes.
func _make_ridged_noise(seed_value: int) -> FastNoiseLite:
	var noise := FastNoiseLite.new()
	noise.seed = seed_value
	noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise.fractal_type = FastNoiseLite.FRACTAL_RIDGED
	noise.fractal_octaves = 4
	noise.frequency = 1.0
	noise.domain_warp_enabled = true
	noise.domain_warp_type = FastNoiseLite.DOMAIN_WARP_SIMPLEX
	noise.domain_warp_amplitude = 30.0
	noise.domain_warp_frequency = 0.5
	return noise


func _build_terrain_mesh() -> void:
	# Clean up old mesh
	for child in get_children():
		child.queue_free()

	# Create mesh from heightmap
	var mesh := _create_mesh_from_heightmap(_heightmap)

	# Create MeshInstance3D
	_mesh_instance = MeshInstance3D.new()
	_mesh_instance.mesh = mesh
	_mesh_instance.name = "TerrainMesh"

	# Apply terrain material
	var mat := _create_terrain_material()
	_mesh_instance.material_override = mat

	# Enable shadows
	_mesh_instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON

	add_child(_mesh_instance)

	# Create static body for collision
	var body := StaticBody3D.new()
	body.name = "TerrainBody"
	_collision_shape = CollisionShape3D.new()
	var shape := ConcavePolygonShape3D.new()
	shape.set_faces(mesh.get_faces())
	_collision_shape.shape = shape
	body.add_child(_collision_shape)
	add_child(body)

	print("[FjordTerrain] Generated %dx%d terrain" % [terrain_resolution, terrain_resolution])


## GDScript heightfield builder. Pure function of its arguments plus the
## passed noise resource: safe to run on a worker thread (the noise instance
## must be thread-local — see _make_ridged_noise — and callers must not
## touch the scene tree). Mirrors LakskongenSim TerrainGenerator so the C#
## and GDScript paths agree on channel, cliffs, inlets and islands.
func _generate_heightmap_gd(
	noise: FastNoiseLite,
	size: int,
	w: float,
	depth: float,
	steepness: float,
	floor_depth: float,
	islands: int,
) -> PackedFloat32Array:
	var heightmap := PackedFloat32Array()
	heightmap.resize(size * size)

	for z in range(size):
		for x in range(size):
			var nx := (float(x) / float(size) - 0.5) * 2.0  # -1 to 1
			var nz := (float(z) / float(size) - 0.5) * 2.0  # -1 to 1

			var height := 0.0

			# Base landmass (raised around edges, low in center for fjord)
			var edge_dist := maxf(absf(nx), absf(nz))
			height += smoothstep(0.3, 0.9, edge_dist) * 0.7

			# Main fjord channel (runs roughly through center, Z-axis)
			var fjord_center := sin(nz * 2.0) * 0.1  # Slightly winding
			var dist_to_fjord := absf(nx - fjord_center)
			var fjord_factor := smoothstep(w, w * 0.3, dist_to_fjord)
			height -= fjord_factor * depth

			# Cliff walls (steep transition over a wider valley: the outer
			# edge sits at 1.6x channel half-width so distant walls read as
			# varied massifs instead of thin uniform streaks at FJORD zoom).
			var cliff_zone := smoothstep(w * 0.3, w * 1.6, dist_to_fjord)
			height += pow(cliff_zone, 1.0 / steepness) * 0.3

			# Secondary inlet (branching fjord)
			var inlet_x := nz * 0.5 + 0.2
			var inlet_dist := absf(nx - inlet_x) + absf(nz - 0.3) * 0.3
			var inlet_factor := smoothstep(0.15, 0.05, inlet_dist)
			height -= inlet_factor * depth * 0.6

			# Ridged + domain-warped variation (get_noise_2d applies the
			# warp internally when domain_warp_enabled). Ridged output is
			# non-negative, so recenter to zero-mean like the old FBM; the
			# channel cut (-depth at center) still dominates at waterline.
			var noise_val := noise.get_noise_2d(nx * 3.0, nz * 3.0)
			height += (noise_val - 0.5) * 2.0 * 0.15

			# Islands (pure functions of the index — no RNG state, so
			# threaded generation stays allocation-free and deterministic).
			for i in range(islands):
				var island_x := sin(float(i) * 2.3 + 0.5) * 0.4
				var island_z := cos(float(i) * 1.7 + 0.3) * 0.4
				var island_dist := Vector2(nx - island_x, nz - island_z).length()
				var island_radius := 0.05 + float(i % 3) * 0.02
				var island_height := smoothstep(island_radius, island_radius * 0.3, island_dist) * 0.3
				height += island_height

			# Fairway floor: the navigable channel core must stay deep enough
			# for 40 m pen nets. Noise bumps mid-channel would otherwise form
			# awash skerries that paint thin foam streaks across open water.
			# (Beaches live on the flanks outside this band, unaffected.)
			if dist_to_fjord < w * 0.35:
				height = minf(height, -floor_depth)

			# Beaches (gentle slope near water level)
			if height > -0.05 and height < 0.1:
				height = lerpf(height, 0.02, 0.3)

			heightmap[z * size + x] = height

	return heightmap


func _create_mesh_from_heightmap(heightmap: PackedFloat32Array) -> ArrayMesh:
	var size := terrain_resolution
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)

	var step_x := terrain_size.x / float(size - 1)
	var step_z := terrain_size.y / float(size - 1)
	var offset_x := terrain_size.x / 2.0
	var offset_z := terrain_size.y / 2.0

	# Create vertices
	for z in range(size):
		for x in range(size):
			var height := heightmap[z * size + x] * max_height
			var pos := Vector3(
				float(x) * step_x - offset_x,
				height,
				float(z) * step_z - offset_z
			)

			# UV for texturing
			var uv := Vector2(float(x) / float(size - 1), float(z) / float(size - 1))

			# Calculate normal from neighbors
			var normal := _calculate_normal(heightmap, x, z, size, step_x, step_z)

			st.set_uv(uv)
			st.set_normal(normal)
			st.add_vertex(pos)

	# Create triangles
	for z in range(size - 1):
		for x in range(size - 1):
			var i := z * size + x
			# Triangle 1
			st.add_index(i)
			st.add_index(i + size)
			st.add_index(i + 1)
			# Triangle 2
			st.add_index(i + 1)
			st.add_index(i + size)
			st.add_index(i + size + 1)

	st.generate_tangents()
	return st.commit()


func _calculate_normal(heightmap: PackedFloat32Array, x: int, z: int, size: int, step_x: float, step_z: float) -> Vector3:
	var h := heightmap[z * size + x] * max_height
	var hx := heightmap[z * size + mini(x + 1, size - 1)] * max_height
	var hz := heightmap[mini(z + 1, size - 1) * size + x] * max_height

	var dx := Vector3(step_x, hx - h, 0.0)
	var dz := Vector3(0.0, hz - h, step_z)
	return dz.cross(dx).normalized()


func _create_terrain_material() -> Material:
	# Try to load the terrain shader for multi-layer texturing
	var shader := load("res://shaders/terrain_shader.gdshader") as Shader
	if shader:
		var mat := ShaderMaterial.new()
		mat.shader = shader
		mat.set_shader_parameter("water_level", water_level)
		mat.set_shader_parameter("season", 0.5)
		# Snow only on the highest peaks, green heath far up the slopes:
		# the generated heightmap's median sits near 300 m, so the old
		# snow_height=200 painted ~77% of the land white.
		mat.set_shader_parameter("snow_height", 400.0)
		mat.set_shader_parameter("snow_blend", 45.0)
		mat.set_shader_parameter("grass_height", 240.0)
		mat.set_shader_parameter("cliff_slope", 0.38)
		mat.set_shader_parameter("beach_height", 4.0)
		mat.set_shader_parameter("texture_scale", 1.0 / 24.0)
		mat.set_shader_parameter("normal_strength", 1.0)
		_apply_terrain_textures(mat)
		return mat

	# Fallback to basic material
	var fallback := StandardMaterial3D.new()
	fallback.albedo_color = Color("#6B7280")
	fallback.roughness = 0.85
	return fallback


## Load the generated terrain texture set into the shader material.
## Guarded: if any texture is missing, `use_textures` is disabled and the
## shader falls back to its original procedural palette.
func _apply_terrain_textures(mat: ShaderMaterial) -> void:
	const BASE := "res://assets/textures/"
	var layers := {
		"grass": "terrain_grass",
		"rock": "terrain_rock",
		"snow": "terrain_snow",
		"sand": "terrain_sand",
		"moss": "terrain_moss",
	}
	var missing: Array[String] = []
	for layer in layers:
		var albedo_path := "%s%s.png" % [BASE, layers[layer]]
		var normal_path := "%s%s_normal.png" % [BASE, layers[layer]]
		var albedo := _load_texture(albedo_path)
		var normal := _load_texture(normal_path)
		if albedo == null or normal == null:
			missing.append(layer)
			continue
		mat.set_shader_parameter("%s_tex" % layer, albedo)
		mat.set_shader_parameter("%s_normal_tex" % layer, normal)

	if missing.is_empty():
		mat.set_shader_parameter("use_textures", 1.0)
	else:
		push_warning("[FjordTerrain] Missing terrain textures: %s — using procedural fallback" % ", ".join(missing))
		mat.set_shader_parameter("use_textures", 0.0)


func _load_texture(path: String) -> Texture2D:
	if not ResourceLoader.exists(path):
		return null
	return load(path) as Texture2D


## Smoothstep helper
func smoothstep(edge0: float, edge1: float, x: float) -> float:
	var t := clampf((x - edge0) / (edge1 - edge0), 0.0, 1.0)
	return t * t * (3.0 - 2.0 * t)


## Get terrain height at world position
func get_height_at(world_x: float, world_z: float) -> float:
	if _heightmap.is_empty():
		return 0.0

	var size := terrain_resolution
	var nx := (world_x + terrain_size.x / 2.0) / terrain_size.x
	var nz := (world_z + terrain_size.y / 2.0) / terrain_size.y

	nx = clampf(nx, 0.0, 1.0)
	nz = clampf(nz, 0.0, 1.0)

	var fx := nx * float(size - 1)
	var fz := nz * float(size - 1)
	var ix := int(fx)
	var iz := int(fz)
	ix = mini(ix, size - 2)
	iz = mini(iz, size - 2)

	# Bilinear interpolation
	var tx := fx - float(ix)
	var tz := fz - float(iz)

	var h00 := _heightmap[iz * size + ix]
	var h10 := _heightmap[iz * size + ix + 1]
	var h01 := _heightmap[(iz + 1) * size + ix]
	var h11 := _heightmap[(iz + 1) * size + ix + 1]

	var h := lerpf(lerpf(h00, h10, tx), lerpf(h01, h11, tx), tz)
	return h * max_height


## World X of the fjord channel's centreline at world Z (the generator's
## fjord_center). The fairway, at least channel_floor deep, runs
## fairway_half_width() metres either side of it.
func channel_center_x(world_z: float) -> float:
	var nz := (world_z + terrain_size.y / 2.0) / terrain_size.y * 2.0 - 1.0
	return sin(nz * 2.0) * 0.1 * terrain_size.x / 2.0


## Half-width of the deep fairway around the channel centreline, in metres.
func fairway_half_width() -> float:
	return fjord_width * 0.35 * terrain_size.x / 2.0


## Sea-level waypoints along the channel from from_z to to_z, `lane` metres
## beside the centreline (clamped to the fairway) and every `step` metres. A
## boat that follows them stays on open water and passes beside the pens
## anchored on the centreline; boats used to leave from inside the fjord
## wall and arc 50-80 m over the terrain.
func channel_route(from_z: float, to_z: float, lane: float, step: float = 100.0) -> PackedVector3Array:
	var offset := clampf(lane, -fairway_half_width(), fairway_half_width())
	var count := maxi(1, ceili(absf(to_z - from_z) / step))
	var points := PackedVector3Array()
	for i in range(count + 1):
		var z := lerpf(from_z, to_z, float(i) / float(count))
		points.append(Vector3(channel_center_x(z) + offset, 0.0, z))
	return points


## Pen anchor positions along the fjord channel, in world metres (Y = 0).
## Prefers the C# TerrainGenerator bridge so sim-side and visual placement
## agree; falls back to the GDScript channel-center mirror (same math as
## FishPenManager's TRUE channel center).
func get_recommended_pen_positions(count: int = 12) -> Array[Vector3]:
	if ResourceLoader.exists(BRIDGE_PATH):
		var scr := load(BRIDGE_PATH)
		if scr != null and scr.can_instantiate():
			var bridge = scr.new()
			var result: Array[Vector3] = []
			result.assign(bridge.call(
				"GetRecommendedPenPositions",
				count, terrain_seed, terrain_size.x, terrain_size.y))
			bridge.free()
			if not result.is_empty():
				return result
	var positions: Array[Vector3] = []
	var start_z := -terrain_size.y * 0.4
	for i in range(count):
		var progress := float(i) / float(maxi(count - 1, 1))
		var z := start_z + progress * terrain_size.y * 0.8
		var nz := (z + terrain_size.y / 2.0) / terrain_size.y * 2.0 - 1.0
		var fjord_center := sin(nz * 2.0) * 0.1
		positions.append(Vector3(fjord_center * terrain_size.x / 2.0, 0.0, z))
	return positions
