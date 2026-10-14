class_name FjordVegetation
extends Node3D
## Birch and spruce on the fjord slopes (G2-02).
##
## The trees come from the terrain build (TerrainGeneratorBridge.VegetationChunks:
## VegetationScatter on the worker thread, inside the slope, treeline, shore
## and exposure masks the terrain shader paints the woods with), grouped in
## 250 m cells per species as ready MultiMesh buffers. Each cell gets two
## MultiMeshInstance3Ds: the full tree (LOD0, casts shadows) up to
## LOD_DISTANCE_M from the camera, the one-cone/one-mass silhouette (LOD1,
## no shadow) beyond it. Cells go up a few per frame under FRAME_BUDGET_MS.

signal vegetation_ready

const SHADER := "res://shaders/vegetation_shader.gdshader"
## [species][lod] mesh scenes (blender/build_vegetation.py); species 0 spruce, 1 birch.
const MESHES := [
	["res://assets/models/vegetation/spruce_LOD0.glb", "res://assets/models/vegetation/spruce_LOD1.glb"],
	["res://assets/models/vegetation/birch_LOD0.glb", "res://assets/models/vegetation/birch_LOD1.glb"],
]
## Full trees nearer than this (m from the camera to a cell's centre), silhouettes beyond.
const LOD_DISTANCE_M := 650.0
## Hysteresis around the switch, so a cell does not flicker on its edge.
const LOD_MARGIN_M := 60.0
## Main-thread time per frame for cell uploads.
const FRAME_BUDGET_MS := 3.0
const FLOATS_PER_TREE := 16

## Trees placed so far, and MultiMeshInstance3Ds per LOD.
var instance_count := 0
var cells_lod0: Array[MultiMeshInstance3D] = []
var cells_lod1: Array[MultiMeshInstance3D] = []

var _pending: Array = []
var _meshes: Array = []
var _materials: Array[ShaderMaterial] = []


func _ready() -> void:
	set_process(not _pending.is_empty())


## Queue the cells (Array of {species, center, count, buffer}) for upload.
func build(chunks: Array) -> void:
	for child in get_children():
		child.queue_free()
	cells_lod0.clear()
	cells_lod1.clear()
	instance_count = 0
	if not _load_resources():
		return
	_pending = chunks.duplicate()
	set_process(true)


## True once every queued cell is up.
func is_ready() -> bool:
	return _pending.is_empty() and not cells_lod0.is_empty()


func _process(_delta: float) -> void:
	var started := Time.get_ticks_usec()
	while not _pending.is_empty():
		_add_cell(_pending.pop_front())
		if Time.get_ticks_usec() - started > FRAME_BUDGET_MS * 1000.0:
			break
	if _pending.is_empty():
		set_process(false)
		print("[Vegetation] %d trees in %d cells" % [instance_count, cells_lod0.size()])
		vegetation_ready.emit()


func _load_resources() -> bool:
	var shader := load(SHADER) as Shader
	if shader == null:
		push_error("[Vegetation] %s is missing" % SHADER)
		return false
	_meshes.clear()
	_materials.clear()
	for species in MESHES.size():
		var lods: Array[Mesh] = []
		for path: String in MESHES[species]:
			var mesh := _mesh_from(path)
			if mesh == null:
				push_error("[Vegetation] %s holds no mesh" % path)
				return false
			lods.append(mesh)
		_meshes.append(lods)
		var material := ShaderMaterial.new()
		material.shader = shader
		material.set_shader_parameter("species", species)
		_materials.append(material)
	return true


static func _mesh_from(path: String) -> Mesh:
	var scene := load(path) as PackedScene
	if scene == null:
		return null
	var root := scene.instantiate()
	var found: Mesh = null
	var stack: Array[Node] = [root]
	while not stack.is_empty() and found == null:
		var node: Node = stack.pop_back()
		if node is MeshInstance3D:
			found = (node as MeshInstance3D).mesh
		stack.append_array(node.get_children())
	root.free()
	return found


func _add_cell(cell: Dictionary) -> void:
	var species := int(cell["species"])
	var count := int(cell["count"])
	var buffer: PackedFloat32Array = cell["buffer"]
	if count <= 0 or buffer.size() != count * FLOATS_PER_TREE:
		push_warning("[Vegetation] cell with %d trees and %d floats skipped" % [count, buffer.size()])
		return
	for lod in 2:
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.use_custom_data = true
		mm.mesh = _meshes[species][lod]
		mm.instance_count = count
		mm.buffer = buffer
		var mmi := MultiMeshInstance3D.new()
		mmi.name = "%s_%d_%d_LOD%d" % ["Birch" if species == 1 else "Spruce", int(cell["center"].x), int(cell["center"].z), lod]
		mmi.multimesh = mm
		mmi.material_override = _materials[species]
		if lod == 0:
			mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
			mmi.visibility_range_end = LOD_DISTANCE_M
			mmi.visibility_range_end_margin = LOD_MARGIN_M
			cells_lod0.append(mmi)
		else:
			mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			mmi.visibility_range_begin = LOD_DISTANCE_M
			mmi.visibility_range_begin_margin = LOD_MARGIN_M
			cells_lod1.append(mmi)
		add_child(mmi)
	instance_count += count


## World positions of every tree (tests and the harness read them).
func tree_positions() -> PackedVector3Array:
	var out := PackedVector3Array()
	for mmi in cells_lod0:
		var mm := mmi.multimesh
		for i in mm.instance_count:
			out.append(mm.get_instance_transform(i).origin)
	return out


## Base altitude and species of every tree, in tree_positions() order.
func tree_records() -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	for mmi in cells_lod0:
		var mm := mmi.multimesh
		var birch := mmi.name.begins_with("Birch")
		for i in mm.instance_count:
			var custom := mm.get_instance_custom_data(i)
			out.append({"origin": mm.get_instance_transform(i).origin, "base": custom.a, "scale": custom.b, "birch": birch})
	return out
