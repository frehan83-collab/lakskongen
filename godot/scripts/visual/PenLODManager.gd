extends Node3D
class_name PenLODManager

## Pen LOD Manager - handles level-of-detail switching for pen cages based on camera distance.

@export_group("LOD Distances")
@export var lod0_distance: float = 50.0   # Full detail (pen_cage.tscn)
@export var lod1_distance: float = 150.0  # Medium (simplified cage)
@export var lod2_distance: float = 400.0  # Low (billboard/ring only)
@export var lod3_distance: float = 1000.0 # Culled

@export_group("References")
@export var camera: Camera3D
@export var pen_cage_scene: PackedScene = preload("res://assets/models/pen_cage/pen_cage.tscn")
@export var pen_ring_mesh: Mesh = preload("res://assets/models/pen_cage/pen_collar_mesh.res")
@export var pen_ring_material: Material

var _pen_instances: Dictionary = {}  # pen_id -> {lod0, lod1, lod2, current_lod, position}
var _camera_pos: Vector3 = Vector3.ZERO

func _ready() -> void:
	if not camera:
		camera = get_viewport().get_camera_3d()
	
	# Create simplified LOD meshes
	_create_lod_meshes()
	
	# Connect to pen manager signals if available
	var pen_manager = get_node_or_null("/root/FishPenManager") or get_node_or_null("../FishPenManager")
	if pen_manager:
		pen_manager.pen_added.connect(_on_pen_added)
		pen_manager.pen_removed.connect(_on_pen_removed)
		# Sync existing pens
		for pen_id in pen_manager.get_pen_ids():
			var pos = pen_manager.get_pen_position(pen_id)
			_add_pen_lod(pen_id, pos)

func _create_lod_meshes() -> void:
	# LOD1: Simplified cage (ring + net only, no walkway/handrail/feedpipe)
	# LOD2: Just the collar ring
	# LOD3: Billboard (handled by visibility culling)
	pass

func _on_pen_added(pen_id: String, position: Vector3) -> void:
	_add_pen_lod(pen_id, position)

func _on_pen_removed(pen_id: String) -> void:
	if _pen_instances.has(pen_id):
		var inst = _pen_instances[pen_id]
		if inst.lod0: inst.lod0.queue_free()
		if inst.lod1: inst.lod1.queue_free()
		if inst.lod2: inst.lod2.queue_free()
		_pen_instances.erase(pen_id)

func _add_pen_lod(pen_id: String, position: Vector3) -> void:
	# LOD0: Full pen cage
	var lod0 = pen_cage_scene.instantiate()
	lod0.name = "%s_lod0" % pen_id
	lod0.global_position = position
	add_child(lod0)
	
	# LOD1: Simplified (just ring + net)
	var lod1 = MeshInstance3D.new()
	lod1.name = "%s_lod1" % pen_id
	var ring = MeshInstance3D.new()
	ring.mesh = pen_ring_mesh
	if pen_ring_material:
		ring.material_override = pen_ring_material
	ring.position.y = 0.5
	lod1.add_child(ring)
	
	# Simple net cylinder
	var net = MeshInstance3D.new()
	var cyl = CylinderMesh.new()
	cyl.top_radius = 12.0
	cyl.bottom_radius = 12.0
	cyl.height = 12.0
	var net_mat = StandardMaterial3D.new()
	net_mat.albedo_color = Color(0.15, 0.2, 0.25, 0.2)
	net_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	net_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	net.mesh = cyl
	net.material_override = net_mat
	net.position.y = -6.0
	lod1.add_child(net)
	lod1.global_position = position
	lod1.visible = false
	add_child(lod1)
	
	# LOD2: Just ring (billboard-style)
	var lod2 = MeshInstance3D.new()
	lod2.name = "%s_lod2" % pen_id
	var ring2 = MeshInstance3D.new()
	ring2.mesh = pen_ring_mesh
	if pen_ring_material:
		ring2.material_override = pen_ring_material
	ring2.position.y = 0.5
	lod2.add_child(ring2)
	lod2.global_position = position
	lod2.visible = false
	add_child(lod2)
	
	_pen_instances[pen_id] = {
		"lod0": lod0,
		"lod1": lod1,
		"lod2": lod2,
		"current_lod": 0,
		"position": position
	}

func _process(delta: float) -> void:
	if not camera:
		return
	
	_camera_pos = camera.global_position
	
	for pen_id in _pen_instances:
		var inst = _pen_instances[pen_id]
		var dist = _camera_pos.distance_to(inst.position)
		
		var new_lod = 0
		if dist > lod0_distance:
			new_lod = 1
		if dist > lod1_distance:
			new_lod = 2
		if dist > lod2_distance:
			new_lod = 3
		
		if new_lod != inst.current_lod:
			_switch_lod(inst, new_lod)

func _switch_lod(inst: Dictionary, new_lod: int) -> void:
	# Hide all
	if inst.lod0: inst.lod0.visible = false
	if inst.lod1: inst.lod1.visible = false
	if inst.lod2: inst.lod2.visible = false
	
	# Show appropriate LOD
	match new_lod:
		0:
			if inst.lod0: inst.lod0.visible = true
		1:
			if inst.lod1: inst.lod1.visible = true
		2:
			if inst.lod2: inst.lod2.visible = true
		3:
			pass  # Culled
	
	inst.current_lod = new_lod

func get_draw_call_stats() -> Dictionary:
	var stats = {"lod0": 0, "lod1": 0, "lod2": 0, "culled": 0}
	for inst in _pen_instances.values():
		match inst.current_lod:
			0: stats.lod0 += 1
			1: stats.lod1 += 1
			2: stats.lod2 += 1
			3: stats.culled += 1
	return stats