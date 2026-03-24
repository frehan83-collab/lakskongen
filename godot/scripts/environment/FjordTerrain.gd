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
@export_range(1.0, 10.0) var cliff_steepness: float = 5.0
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

var _mesh_instance: MeshInstance3D
var _collision_shape: CollisionShape3D
var _heightmap: PackedFloat32Array


func _ready() -> void:
	if _mesh_instance == null:
		generate_terrain()


func generate_terrain() -> void:
	# Clean up old mesh
	for child in get_children():
		child.queue_free()

	var rng := RandomNumberGenerator.new()
	rng.seed = terrain_seed

	# Generate heightmap
	_heightmap = _generate_heightmap(rng)

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


func _generate_heightmap(rng: RandomNumberGenerator) -> PackedFloat32Array:
	var size := terrain_resolution
	var heightmap := PackedFloat32Array()
	heightmap.resize(size * size)

	var half_size := float(size) / 2.0

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
			var fjord_factor := smoothstep(fjord_width, fjord_width * 0.3, dist_to_fjord)
			height -= fjord_factor * fjord_depth

			# Cliff walls (steep transition)
			var cliff_zone := smoothstep(fjord_width * 0.3, fjord_width, dist_to_fjord)
			height += pow(cliff_zone, 1.0 / cliff_steepness) * 0.3

			# Secondary inlet (branching fjord)
			var inlet_x := nz * 0.5 + 0.2
			var inlet_dist := absf(nx - inlet_x) + absf(nz - 0.3) * 0.3
			var inlet_factor := smoothstep(0.15, 0.05, inlet_dist)
			height -= inlet_factor * fjord_depth * 0.6

			# Noise for natural variation
			var noise_val := _fbm(nx * 3.0 + float(terrain_seed), nz * 3.0, 4, rng)
			height += noise_val * 0.15

			# Islands
			for i in range(island_count):
				var island_x := rng.randf_range(-0.5, 0.5)
				var island_z := rng.randf_range(-0.5, 0.5)
				rng.seed = terrain_seed + i * 1000  # Deterministic per island
				island_x = sin(float(i) * 2.3 + 0.5) * 0.4
				island_z = cos(float(i) * 1.7 + 0.3) * 0.4
				var island_dist := Vector2(nx - island_x, nz - island_z).length()
				var island_radius := 0.05 + float(i % 3) * 0.02
				var island_height := smoothstep(island_radius, island_radius * 0.3, island_dist) * 0.3
				height += island_height

			# Beaches (gentle slope near water level)
			if height > -0.05 and height < 0.1:
				height = lerpf(height, 0.02, 0.3)

			heightmap[z * size + x] = height

	return heightmap


func _fbm(x: float, z: float, octaves: int, _rng: RandomNumberGenerator) -> float:
	var value := 0.0
	var amplitude := 0.5
	var frequency := 1.0

	for _i in range(octaves):
		value += sin(x * frequency * 7.13 + z * frequency * 3.97) * amplitude
		value += cos(x * frequency * 5.31 - z * frequency * 8.21) * amplitude * 0.5
		amplitude *= 0.5
		frequency *= 2.0

	return value


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
		return mat

	# Fallback to basic material
	var fallback := StandardMaterial3D.new()
	fallback.albedo_color = Color("#6B7280")
	fallback.roughness = 0.85
	return fallback


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
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
