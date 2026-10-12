extends Node
class_name CompetitorFarmVisualManager

## Grey competitor farm visual manager: places greyed-out farm silhouettes
## at fixed positions around the fjord to represent AI competitors.
## Shows up to 8 competitor farms with greyed-out materials.

@export var pen_cage_scene: PackedScene = preload("res://assets/models/pen_cage/pen_cage.tscn")
@export var max_competitors: int = 8

signal competitor_farm_added(competitor_id: String)

var _sim_bridge = null
var _fjord_env = null
var _competitor_farms: Dictionary = {}  # competitor_id -> {root, cages, label}

func _ready() -> void:
	_fjord_env = get_parent()
	var sim = get_node_or_null("/root/SimBridge")
	if sim:
		bind_simulation(sim)
	print("[CompetitorFarmVisualManager] Ready")

func bind_simulation(sim_bridge) -> void:
	_sim_bridge = sim_bridge
	if _sim_bridge:
		_sim_bridge.state_updated.connect(_on_state_updated)

func _on_state_updated() -> void:
	if not _sim_bridge:
		return
	_sync_competitor_farms()

func _sync_competitor_farms() -> void:
	if not _sim_bridge or not _sim_bridge.has_method("GetCompetitorCompanies"):
		return
	var competitors: Array = _sim_bridge.GetCompetitorCompanies()
	if not competitors:
		return
	
	var active_ids: Array = []
	
	for comp_dict in competitors:
		var comp_id: String = str(comp_dict.get("id", ""))
		var name: String = str(comp_dict.get("name", "Unknown"))
		var pos_x: float = float(comp_dict.get("position_x", 0.0))
		var pos_z: float = float(comp_dict.get("position_z", 0.0))
		var market_share: float = float(comp_dict.get("market_share", 0.0))
		
		if comp_id.is_empty():
			continue
		
		active_ids.append(comp_id)
		
		if not _competitor_farms.has(comp_id):
			_spawn_competitor_farm(comp_id, name, Vector3(pos_x, 0.0, pos_z), market_share)
	
	# Remove farms for competitors that no longer exist
	var to_remove: Array = []
	for comp_id in _competitor_farms.keys():
		if comp_id not in active_ids:
			to_remove.append(comp_id)
	for cid in to_remove:
		_remove_competitor_farm(cid)

func _spawn_competitor_farm(comp_id: String, name: String, position: Vector3, market_share: float) -> void:
	var farm_root := Node3D.new()
	farm_root.name = "CompetitorFarm_%s" % comp_id
	farm_root.global_position = position
	_fjord_env.add_child(farm_root)
	
	# Create 2-4 pens for this competitor farm
	var pen_count = randi_range(2, 4)
	var cage_spacing = 180.0  # meters between pens
	var cages: Array = []
	
	for i in range(pen_count):
		var cage = pen_cage_scene.instantiate()
		cage.name = "Pen_%s_%d" % [comp_id, i]
		
		# Offset around center
		var angle = float(i) / float(pen_count) * TAU
		var offset = Vector3(cos(angle) * cage_spacing, 0.0, sin(angle) * cage_spacing)
		cage.global_position = position + offset
		
		farm_root.add_child(cage)
		cages.append(cage)
		
		# Apply grey competitor material
		_apply_competitor_material(cage)
	
	# Add name label
	var label := Label3D.new()
	label.name = "Label"
	label.text = name
	label.font_size = 72
	label.modulate = Color(0.5, 0.5, 0.5, 0.8)
	label.position = Vector3(0, 30, 0)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	farm_root.add_child(label)
	
	_competitor_farms[comp_id] = {
		"root": farm_root,
		"cages": cages,
		"label": label,
	}
	
	competitor_farm_added.emit(comp_id)

func _apply_competitor_material(cage: Node3D) -> void:
	# Apply greyed-out material to all mesh instances in the cage
	for child in cage.get_children():
		if child is MeshInstance3D:
			var mesh_inst: MeshInstance3D = child as MeshInstance3D
			if mesh_inst.material_override:
				var mat = mesh_inst.material_override.duplicate() as StandardMaterial3D
				mat.albedo_color = Color(0.4, 0.4, 0.4, 0.6)  # grey, semi-transparent
				mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
				mat.cull_mode = BaseMaterial3D.CULL_DISABLED
				mesh_inst.material_override = mat
			mesh_inst.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF

func _remove_competitor_farm(comp_id: String) -> void:
	if not _competitor_farms.has(comp_id):
		return
	var data: Dictionary = _competitor_farms[comp_id]
	if data.root:
		data.root.queue_free()
	_competitor_farms.erase(comp_id)