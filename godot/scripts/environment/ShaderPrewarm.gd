class_name ShaderPrewarm
extends Node3D
## Compiles the render pipelines of materials that arrive late, while the
## scene loads (P0-04). The terrain's chunks reach the GPU a few frames
## after the scene starts (FjordTerrain builds them on a worker thread), and
## their first frames used to stall on compiling the terrain shader and its
## pipelines (a 0.9 s frame, then 260, 110 and 60 ms ones). Drawing one tiny
## triangle per material in front of the camera on the first frames compiles
## the same pipelines up front: the colour, depth and shadow passes, with
## the mesh's own vertex format (a pipeline is per shader, pass and vertex
## layout, not per material values), at the viewport's anti-aliasing.
##
## The triangle is a millimetre across, 2 m in front of the camera: inside
## the view and the first shadow cascade, smaller than a pixel. The node
## frees itself after `frames` drawn frames.

## Vertex arrays every warmed mesh carries, as FjordTerrain's chunks do.
const TERRAIN_FORMAT := [Mesh.ARRAY_VERTEX, Mesh.ARRAY_NORMAL, Mesh.ARRAY_TANGENT, Mesh.ARRAY_TEX_UV, Mesh.ARRAY_COLOR, Mesh.ARRAY_INDEX]
## Size of the warm-up triangle (m) and its distance from the camera.
const SIZE_M := 0.001
const DISTANCE_M := 2.0

var _camera: Camera3D = null
var _frames_left := 2
## Materials warmed (tests read it).
var warmed: Array[Material] = []


## Warm `materials` (each drawn with `arrays_format`, a list of
## Mesh.ARRAY_* slots) in front of `camera` for `frames` drawn frames.
static func warm(parent: Node, camera: Camera3D, materials: Array[Material], arrays_format: Array = TERRAIN_FORMAT, frames: int = 2) -> ShaderPrewarm:
	var node := ShaderPrewarm.new()
	node.name = "ShaderPrewarm"
	node._camera = camera
	node._frames_left = frames
	var mesh := triangle_mesh(arrays_format)
	for material in materials:
		if material == null:
			continue
		var instance := MeshInstance3D.new()
		instance.mesh = mesh
		instance.material_override = material
		instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		# Never culled by distance or LOD: it must be drawn to compile.
		instance.extra_cull_margin = 1.0
		node.add_child(instance)
		node.warmed.append(material)
	parent.add_child(node)
	node._follow()
	return node


## One triangle with the requested vertex arrays.
static func triangle_mesh(arrays_format: Array) -> ArrayMesh:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	for slot: int in arrays_format:
		match slot:
			Mesh.ARRAY_VERTEX:
				arrays[slot] = PackedVector3Array([Vector3(0.0, 0.0, 0.0), Vector3(SIZE_M, 0.0, 0.0), Vector3(0.0, SIZE_M, 0.0)])
			Mesh.ARRAY_NORMAL:
				arrays[slot] = PackedVector3Array([Vector3.BACK, Vector3.BACK, Vector3.BACK])
			Mesh.ARRAY_TANGENT:
				arrays[slot] = PackedFloat32Array([1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0])
			Mesh.ARRAY_TEX_UV:
				arrays[slot] = PackedVector2Array([Vector2.ZERO, Vector2.RIGHT, Vector2.DOWN])
			Mesh.ARRAY_COLOR:
				arrays[slot] = PackedColorArray([Color.WHITE, Color.WHITE, Color.WHITE])
			Mesh.ARRAY_INDEX:
				arrays[slot] = PackedInt32Array([0, 1, 2])
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _process(_delta: float) -> void:
	_follow()
	_frames_left -= 1
	if _frames_left < 0:
		queue_free()


func _follow() -> void:
	var cam := _camera if _camera != null and is_instance_valid(_camera) else get_viewport().get_camera_3d()
	if cam == null:
		return
	var basis := cam.global_transform.basis
	global_transform = Transform3D(basis, cam.global_position - basis.z * DISTANCE_M)
