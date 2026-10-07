extends MeshInstance3D
## Animated water surface using vertex displacement with sine waves
## Creates realistic wave motion for fjord water

@export var wave_speed: float = 1.0
@export var wave_height: float = 0.3
@export var wave_frequency: float = 0.5

## Throttle for the CPU mesh rebuild (SurfaceTool commit every frame is
## too expensive). The vertex waves update at this interval instead.
@export var rebuild_interval: float = 0.25

var time: float = 0.0
var original_vertices: PackedVector3Array = []
var _rebuild_accum: float = 0.0

func _ready() -> void:
	_cache_original_vertices()

func _process(delta: float) -> void:
	time += delta * wave_speed
	_rebuild_accum += delta
	if _rebuild_accum >= rebuild_interval:
		_rebuild_accum = 0.0
		_animate_water()

func _cache_original_vertices() -> void:
	if mesh == null:
		return
	
	var plane_mesh := mesh as PlaneMesh
	if plane_mesh == null:
		return
	
	var size := plane_mesh.size
	var subdivide_w := plane_mesh.subdivide_width
	var subdivide_d := plane_mesh.subdivide_depth
	
	original_vertices.clear()
	
	var width_step := size.x / float(subdivide_w)
	var depth_step := size.y / float(subdivide_d)
	
	for z in range(subdivide_d + 1):
		for x in range(subdivide_w + 1):
			var pos_x := -size.x / 2.0 + x * width_step
			var pos_z := -size.y / 2.0 + z * depth_step
			original_vertices.append(Vector3(pos_x, 0, pos_z))

func _animate_water() -> void:
	if mesh == null or original_vertices.is_empty():
		return
	
	var plane_mesh := mesh as PlaneMesh
	if plane_mesh == null:
		return
	
	var subdivide_w := plane_mesh.subdivide_width
	var subdivide_d := plane_mesh.subdivide_depth
	
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	
	var animated_vertices: Array[Vector3] = []
	
	# Apply sine wave displacement to vertices
	for vertex in original_vertices:
		var offset_y := _calculate_wave_offset(vertex.x, vertex.z, time)
		animated_vertices.append(Vector3(vertex.x, offset_y, vertex.z))
	
	# Build triangles with animated vertices
	for z in range(subdivide_d):
		for x in range(subdivide_w):
			var i0 := z * (subdivide_w + 1) + x
			var i1 := i0 + 1
			var i2 := (z + 1) * (subdivide_w + 1) + x
			var i3 := i2 + 1
			
			# Triangle 1
			st.set_normal(_calculate_normal(animated_vertices[i0], animated_vertices[i1], animated_vertices[i2]))
			st.set_uv(Vector2(float(x) / subdivide_w, float(z) / subdivide_d))
			st.add_vertex(animated_vertices[i0])
			
			st.set_uv(Vector2(float(x + 1) / subdivide_w, float(z) / subdivide_d))
			st.add_vertex(animated_vertices[i1])
			
			st.set_uv(Vector2(float(x) / subdivide_w, float(z + 1) / subdivide_d))
			st.add_vertex(animated_vertices[i2])
			
			# Triangle 2
			st.set_normal(_calculate_normal(animated_vertices[i1], animated_vertices[i3], animated_vertices[i2]))
			st.set_uv(Vector2(float(x + 1) / subdivide_w, float(z) / subdivide_d))
			st.add_vertex(animated_vertices[i1])
			
			st.set_uv(Vector2(float(x + 1) / subdivide_w, float(z + 1) / subdivide_d))
			st.add_vertex(animated_vertices[i3])
			
			st.set_uv(Vector2(float(x) / subdivide_w, float(z + 1) / subdivide_d))
			st.add_vertex(animated_vertices[i2])
	
	st.generate_normals()
	mesh = st.commit()
	
	# Preserve material override
	if get_active_material(0) == null and get_surface_override_material(0) != null:
		set_surface_override_material(0, get_surface_override_material(0))

func _calculate_wave_offset(x: float, z: float, t: float) -> float:
	# Multiple sine waves for natural water motion
	var wave1 := sin(x * wave_frequency + t * 2.0) * wave_height
	var wave2 := sin(z * wave_frequency * 0.7 + t * 1.5) * wave_height * 0.6
	var wave3 := sin((x + z) * wave_frequency * 0.5 + t) * wave_height * 0.4
	
	return wave1 + wave2 + wave3

func _calculate_normal(v1: Vector3, v2: Vector3, v3: Vector3) -> Vector3:
	var edge1 := v2 - v1
	var edge2 := v3 - v1
	return edge1.cross(edge2).normalized()
