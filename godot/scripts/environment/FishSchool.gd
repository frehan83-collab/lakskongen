class_name FishSchool
extends RefCounted
## A pen's salmon school (one MultiMeshInstance3D per pen, the Blender
## Atlantic salmon): how many fish it shows and how big they are, which mesh
## LOD it draws and how it swims. FishPenManager owns the schools; roadmap
## G3-01 will move the swim onto the GPU here.

## Blender-built Atlantic salmon (blender/build_salmon.py, 0.9 m fork length,
## nose along -Z): LOD0 (~1.5k triangles) close to the camera, LOD1 (~300)
## for a school seen from the farm camera. Falls back to a plain box if the
## import is missing.
const SALMON_LOD_SCENES := ["res://assets/models/fish/salmon_LOD0.glb", "res://assets/models/fish/salmon_LOD1.glb"]
## Camera distance (m) from a pen where its school swaps to the far mesh.
const LOD_SWITCH := 70.0
## Past this camera distance (m) a school is not drawn (it swims under a
## net metres down) and not animated.
const VISIBLE_M := 450.0
## Visible fish scale with the sim: count follows stocking (one fish shown
## per FISH_PER_INSTANCE), body length follows mean weight
## (L(cm) = (100 W_g / 1.1)^(1/3), the body mesh is 0.9 m). The MultiMesh is
## allocated once at MAX_INSTANCES; only visible_instance_count moves
## (changing instance_count resets transforms).
const MAX_INSTANCES := 240
const FISH_PER_INSTANCE := 250
const MESH_LENGTH_M := 0.9

static var _salmon_meshes: Dictionary = {}  # lod -> Mesh


static func salmon_mesh(lod: int = 1) -> Mesh:
	if _salmon_meshes.has(lod):
		return _salmon_meshes[lod]
	var mesh: Mesh = null
	var packed: PackedScene = load(SALMON_LOD_SCENES[clampi(lod, 0, 1)]) as PackedScene
	if packed != null:
		var inst: Node = packed.instantiate()
		var mi := inst.find_child("Salmon", true, false) as MeshInstance3D
		if mi and mi.mesh:
			mesh = mi.mesh
		inst.free()
	if mesh == null:
		var body := BoxMesh.new()
		body.size = Vector3(0.25, 0.3, MESH_LENGTH_M)
		var mat := StandardMaterial3D.new()
		mat.albedo_color = Color(0.5, 0.58, 0.65, 1.0)
		mat.roughness = 0.6
		body.material = mat
		mesh = body
	_salmon_meshes[lod] = mesh
	return mesh


## A new school (no fish shown yet), drawn only near the camera.
static func create(school_name: String) -> MultiMeshInstance3D:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = salmon_mesh(1)
	mm.instance_count = MAX_INSTANCES
	mm.visible_instance_count = 0
	var school := MultiMeshInstance3D.new()
	school.name = school_name
	school.multimesh = mm
	school.visibility_range_end = VISIBLE_M
	return school


## Close schools get the detailed salmon, far ones the light mesh (only the
## mesh changes, the transforms stay).
static func update_lod(school: MultiMeshInstance3D, cam: Camera3D) -> void:
	if cam == null or school.multimesh == null:
		return
	var lod := 0 if cam.global_position.distance_to(school.global_position) < LOD_SWITCH else 1
	var mesh := salmon_mesh(lod)
	if school.multimesh.mesh != mesh:
		school.multimesh.mesh = mesh


## Show `fish_count` fish of `mean_weight_kg` (the pen's numbers).
static func apply_density(school: MultiMeshInstance3D, fish_count: int, mean_weight_kg: float) -> void:
	if school == null or school.multimesh == null:
		return
	school.multimesh.visible_instance_count = clampi(fish_count / FISH_PER_INSTANCE, 0, MAX_INSTANCES)
	# Body length from weight; clamp to sane bounds (smolt .. harvest).
	var length_m := 0.2
	if mean_weight_kg > 0.0:
		length_m = clampf(pow(100.0 * mean_weight_kg * 1000.0 / 1.1, 1.0 / 3.0) / 100.0, 0.15, 1.0)
	school.set_meta("fish_scale", length_m / MESH_LENGTH_M)


## Swim the school round its pen at `time` (seconds of swim), unless it is
## hidden or past VISIBLE_M from the camera.
static func animate(school: MultiMeshInstance3D, time: float, key: String, swim_radius: float, cam: Camera3D) -> void:
	if school == null or school.multimesh == null or not school.visible:
		return
	if cam != null and cam.global_position.distance_squared_to(school.global_position) > VISIBLE_M * VISIBLE_M:
		return
	update_lod(school, cam)
	var count: int = school.multimesh.visible_instance_count
	var fish_scale: float = school.get_meta("fish_scale", 0.5)
	var phase := float(abs(hash(key)) % 1000) / 1000.0 * TAU
	for i in range(count):
		var t := time + phase + float(i) * 0.35
		var r := swim_radius * (0.55 + 0.45 * float(i % 3) / 2.0)
		var depth := -3.0 - float(i % 4) * 2.0
		var pos := Vector3(cos(t) * r, depth + sin(t * 1.7) * 0.6, sin(t) * r)
		# Tangential heading: velocity of the circle path; the salmon model's
		# nose points along local -Z (Blender +Y -> Godot -Z on import).
		var vel := Vector3(-sin(t), 0.0, cos(t))
		var basis := Basis.looking_at(vel, Vector3.UP).scaled(Vector3.ONE * fish_scale)
		school.multimesh.set_instance_transform(i, Transform3D(basis, pos))
