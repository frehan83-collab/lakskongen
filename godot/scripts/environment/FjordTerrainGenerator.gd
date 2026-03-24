@tool
extends MeshInstance3D
## Procedural fjord terrain generator using noise-based heightmaps
## Creates mountain ridges on sides with flat water channel in center

@export var fjord_width: float = 40.0:
	set(value):
		fjord_width = value
		if Engine.is_editor_hint():
			_generate_terrain()

@export var mountain_height: float = 25.0:
	set(value):
		mountain_height = value
		if Engine.is_editor_hint():
			_generate_terrain()

@export var water_level: float = 0.0:
	set(value):
		water_level = value
		if Engine.is_editor_hint():
			_generate_terrain()

@export var regenerate: bool = false:
	set(value):
		if value and Engine.is_editor_hint():
			_generate_terrain()
			regenerate = false

var noise: FastNoiseLite

func _ready() -> void:
	if not Engine.is_editor_hint():
		_generate_terrain()

func _generate_terrain() -> void:
	if mesh == null:
		return
	
	# Initialize noise generator
	noise = FastNoiseLite.new()
	noise.seed = randi()
	noise.noise_type = FastNoiseLite.TYPE_PERLIN
	noise.frequency = 0.03
	noise.fractal_octaves = 4
	noise.fractal_lacunarity = 2.0
	noise.fractal_gain = 0.5
	
	var plane_mesh := mesh as PlaneMesh
	if plane_mesh == null:
		return
	
	var size := plane_mesh.size
	var subdivide_w := plane_mesh.subdivide_width
	var subdivide_d := plane_mesh.subdivide_depth
	
	# Create surface tool for mesh generation
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	
	var vertices: Array[Vector3] = []
	var width_step := size.x / float(subdivide_w)
	var depth_step := size.y / float(subdivide_d)
	
	# Generate heightmap vertices
	for z in range(subdivide_d + 1):
		for x in range(subdivide_w + 1):
			var pos_x := -size.x / 2.0 + x * width_step
			var pos_z := -size.y / 2.0 + z * depth_step
			
			var height := _calculate_height(pos_x, pos_z)
			vertices.append(Vector3(pos_x, height, pos_z))
	
	# Build triangles
	for z in range(subdivide_d):
		for x in range(subdivide_w):
			var i0 := z * (subdivide_w + 1) + x
			var i1 := i0 + 1
			var i2 := (z + 1) * (subdivide_w + 1) + x
			var i3 := i2 + 1
			
			# Triangle 1
			st.set_normal(_calculate_normal(vertices[i0], vertices[i1], vertices[i2]))
			st.set_uv(Vector2(float(x) / subdivide_w, float(z) / subdivide_d))
			st.add_vertex(vertices[i0])
			
			st.set_uv(Vector2(float(x + 1) / subdivide_w, float(z) / subdivide_d))
			st.add_vertex(vertices[i1])
			
			st.set_uv(Vector2(float(x) / subdivide_w, float(z + 1) / subdivide_d))
			st.add_vertex(vertices[i2])
			
			# Triangle 2
			st.set_normal(_calculate_normal(vertices[i1], vertices[i3], vertices[i2]))
			st.set_uv(Vector2(float(x + 1) / subdivide_w, float(z) / subdivide_d))
			st.add_vertex(vertices[i1])
			
			st.set_uv(Vector2(float(x + 1) / subdivide_w, float(z + 1) / subdivide_d))
			st.add_vertex(vertices[i3])
			
			st.set_uv(Vector2(float(x) / subdivide_w, float(z + 1) / subdivide_d))
			st.add_vertex(vertices[i2])
	
	st.generate_normals()
	mesh = st.commit()

func _calculate_height(x: float, z: float) -> float:
	# Distance from center line (fjord channel)
	var dist_from_center := abs(x)
	
	# Fjord channel is flat at water level
	if dist_from_center < fjord_width / 2.0:
		return water_level
	
	# Mountain rise based on distance from fjord edge
	var mountain_dist := dist_from_center - fjord_width / 2.0
	var mountain_factor := clamp(mountain_dist / 30.0, 0.0, 1.0)
	
	# Add noise for terrain variation
	var noise_value := noise.get_noise_2d(x, z)
	var base_height := mountain_factor * mountain_height
	var variation := noise_value * mountain_height * 0.3
	
	return water_level + base_height + variation

func _calculate_normal(v1: Vector3, v2: Vector3, v3: Vector3) -> Vector3:
	var edge1 := v2 - v1
	var edge2 := v3 - v1
	return edge1.cross(edge2).normalized()
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
