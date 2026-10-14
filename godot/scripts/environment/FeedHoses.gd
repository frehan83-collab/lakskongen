class_name FeedHoses
extends Node3D
## A site's white floating feed hoses (roadmap G2-04, owner reference 1):
## one hose from a feed-barge outlet to every pen's hose port, with yellow
## floats along them. Every hose of the site is one ribbon mesh (one draw
## call); its shader (feed_hose.gdshader) rides each point on the drawn
## water and keeps the hose at least a pixel or two wide, so the hoses read
## as the thin white lines of the aerial photos. The floats are a MultiMesh
## per LOD. Local space is the site's (FarmSite): y = 0 is the waterline.

const HOSE_MATERIAL := preload("res://assets/models/site_kit/feed_hose_material.tres")
const FLOAT_SCENES := [
	"res://assets/models/site_kit/hose_float_LOD0.glb",
	"res://assets/models/site_kit/hose_float_LOD1.glb",
	"res://assets/models/site_kit/hose_float_LOD2.glb",
]
## Camera distance (m) where each float LOD hands over to the next; past
## the last the floats are under a pixel.
const FLOAT_LOD_SWITCH := [260.0, 900.0, 2600.0]
## The hoses climb to the barge's deck at the outlet: this high over the
## water (m), down to the water over SiteLayout.OUTLET_DROP_M.
const DECK_LIFT_M := 1.7
## Floats are drawn in the buoy yellow (D13 #E8B92A).
const FLOAT_COLOR := Color(0.910, 0.725, 0.165)
## The ribbon opens up to a couple of metres on screen-size grounds and
## rides the waves: its culling box grows by this much.
const CULL_MARGIN_M := 8.0

## Hose centrelines in the site frame (x, z), one per pen.
var hoses: Array[PackedVector2Array] = []
## Float positions in the site frame (x, z).
var floats := PackedVector2Array()

var _mesh_instance: MeshInstance3D = null
var _float_instances: Array[MultiMeshInstance3D] = []
static var _float_meshes: Dictionary = {}


## Lay the hoses along `centrelines` (site frame, each from its barge outlet
## to its pen's port) and put floats on them.
func build(centrelines: Array[PackedVector2Array]) -> void:
	clear()
	hoses = centrelines
	floats = SiteLayout.hose_floats(hoses)
	_mesh_instance = MeshInstance3D.new()
	_mesh_instance.name = "Hoses"
	_mesh_instance.mesh = _ribbon_mesh(hoses)
	_mesh_instance.material_override = HOSE_MATERIAL
	_mesh_instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_mesh_instance.extra_cull_margin = CULL_MARGIN_M
	add_child(_mesh_instance)
	_float_instances = _spawn_floats(floats)


func clear() -> void:
	for child in get_children():
		child.queue_free()
	_mesh_instance = null
	_float_instances.clear()
	hoses.clear()
	floats = PackedVector2Array()


func get_hose_instance() -> MeshInstance3D:
	return _mesh_instance


func get_float_instances() -> Array[MultiMeshInstance3D]:
	return _float_instances


## Total hose length (m).
func total_length() -> float:
	var total := 0.0
	for hose in hoses:
		for i in range(1, hose.size()):
			total += hose[i].distance_to(hose[i - 1])
	return total


## One ribbon per hose: two vertices per centreline point (the shader opens
## them to the sides), NORMAL the hose direction (the shader lights the
## ribbon as a tube itself), UV (side, metres along), UV2.x the lift towards
## the barge deck.
static func _ribbon_mesh(lines: Array[PackedVector2Array]) -> ArrayMesh:
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var uv2s := PackedVector2Array()
	var indices := PackedInt32Array()
	for line in lines:
		if line.size() < 2:
			continue
		var base := verts.size()
		var run := 0.0
		for i in line.size():
			if i > 0:
				run += line[i].distance_to(line[i - 1])
			var a := line[maxi(i - 1, 0)]
			var b := line[mini(i + 1, line.size() - 1)]
			var dir := (b - a).normalized()
			var lift := DECK_LIFT_M * (1.0 - smoothstep(0.0, SiteLayout.OUTLET_DROP_M, run))
			for side in [-1.0, 1.0]:
				verts.append(Vector3(line[i].x, 0.0, line[i].y))
				normals.append(Vector3(dir.x, 0.0, dir.y))
				uvs.append(Vector2(side, run))
				uv2s.append(Vector2(lift, 0.0))
			if i > 0:
				var p := base + (i - 1) * 2
				indices.append_array([p, p + 2, p + 1, p + 1, p + 2, p + 3])
	var mesh := ArrayMesh.new()
	if verts.is_empty():
		return mesh
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_TEX_UV2] = uv2s
	arrays[Mesh.ARRAY_INDEX] = indices
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	return mesh


func _spawn_floats(points: PackedVector2Array) -> Array[MultiMeshInstance3D]:
	var out: Array[MultiMeshInstance3D] = []
	if points.is_empty():
		return out
	for level in FLOAT_SCENES.size():
		var mesh := _float_mesh(level)
		if mesh == null:
			continue
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.use_colors = true
		mm.mesh = mesh
		var xforms: Array[Transform3D] = []
		var tints: Array = []
		for k in points.size():
			var yaw := float(k * 53 % 180) * PI / 180.0
			xforms.append(Transform3D(Basis(Vector3.UP, yaw), Vector3(points[k].x, 0.0, points[k].y)))
			tints.append(FLOAT_COLOR.srgb_to_linear())
		MultiMeshPack.upload(mm, xforms, tints)
		var mmi := MultiMeshInstance3D.new()
		mmi.name = "HoseFloats_LOD%d" % level
		mmi.multimesh = mm
		mmi.visibility_range_begin = 0.0 if level == 0 else FLOAT_LOD_SWITCH[level - 1]
		mmi.visibility_range_end = FLOAT_LOD_SWITCH[level]
		mmi.extra_cull_margin = MooringGrid.FLOAT_CULL_MARGIN
		if level > 0:
			mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mmi)
		out.append(mmi)
	return out


## The float mesh of a LOD (build_site_kit.py hose_float; the grid buoy at
## half size if the float is missing).
static func _float_mesh(level: int) -> Mesh:
	if _float_meshes.has(level):
		return _float_meshes[level]
	var mesh: Mesh = null
	var packed := load(FLOAT_SCENES[level]) as PackedScene if ResourceLoader.exists(FLOAT_SCENES[level]) else null
	if packed != null:
		var inst := packed.instantiate()
		var mi := inst.find_child("HoseFloat", true, false) as MeshInstance3D
		if mi != null:
			mesh = mi.mesh
		inst.free()
	_float_meshes[level] = mesh
	return mesh
