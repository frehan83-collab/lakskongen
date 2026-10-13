extends Node
class_name CompetitorFarmVisualManager

## Grey competitor farm visual manager: places greyed-out farm silhouettes
## for the AI competitors on the fjord channel's deep-water slots, filled
## from the far end (the player's pens fill them from the near end). Shows
## up to max_competitors farms; a rival whose slot the player's sites reach
## is not drawn.

@export var pen_cage_scene: PackedScene = preload("res://assets/models/pen_cage/pen_cage.tscn")
@export var max_competitors: int = 8

signal competitor_farm_added(competitor_id: String)

var _sim_bridge = null
var _fjord_env = null
var _competitor_farms: Dictionary = {}  # competitor_id -> {root, cages, label}
var _grey: StandardMaterial3D = null

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
	# GetCompetitorCompanies lives on GameBridge, not SimBridge, so this used to
	# return before drawing anything (review F744).
	if not _sim_bridge:
		return
	var pens := _fjord_env.get_node_or_null("FishPenManager") as FishPenManager
	if pens == null:
		return
	# The bridge's own farm positions (x up to ±1000 m) lie inside the fjord
	# walls, so the farms take the pen manager's channel slots instead.
	var slots := pens.get_recommended_locations()
	var player_sites := pens.get_pen_ids().size()
	var competitors: Array = _sim_bridge.get_competitor_farms()
	var active_ids: Array = []
	
	var shown: Array = competitors.slice(0, max_competitors)
	for i in shown.size():
		var comp_dict: Dictionary = shown[i]
		var comp_id: String = str(comp_dict.get("id", ""))
		var slot := farm_slot(i, player_sites, slots.size())
		if comp_id.is_empty() or slot < 0:
			continue
		
		active_ids.append(comp_id)
		
		if not _competitor_farms.has(comp_id):
			_spawn_competitor_farm(comp_id, str(comp_dict.get("name", "Unknown")), slots[slot],
				float(comp_dict.get("market_share", 0.0)))
	
	# Remove farms for competitors that no longer exist or have lost their slot
	var to_remove: Array = []
	for comp_id in _competitor_farms.keys():
		if comp_id not in active_ids:
			to_remove.append(comp_id)
	for cid in to_remove:
		_remove_competitor_farm(cid)

## The channel slot of the rival at `index`: rivals fill the slots from the
## far end and the player's sites from the near end, so a slot the player's
## sites reach is not used and a rival never sits on a player's pen. -1 when
## there is no room.
static func farm_slot(index: int, player_sites: int, slot_count: int) -> int:
	var slot := slot_count - 1 - index
	return slot if slot >= player_sites else -1

func _spawn_competitor_farm(comp_id: String, name: String, position: Vector3, market_share: float) -> void:
	# Local positions: global_position before add_child is ignored (with an
	# engine error) because the node is not in the tree yet.
	var farm_root := Node3D.new()
	farm_root.name = "CompetitorFarm_%s" % comp_id
	farm_root.position = position
	_fjord_env.add_child(farm_root)
	
	# 2-4 pens, fixed per competitor (randi_range made every run look different),
	# in a row along the channel: a 180 m ring around the centre reached the
	# fjord walls. Cages are 51 m across.
	var pen_count := 2 + absi(hash(comp_id)) % 3
	var cage_spacing := 70.0
	var cages: Array = []
	
	for i in range(pen_count):
		var cage = pen_cage_scene.instantiate()
		cage.name = "Pen_%s_%d" % [comp_id, i]
		cage.position = Vector3(0.0, 0.0, (float(i) - float(pen_count - 1) / 2.0) * cage_spacing)
		
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
	# Every mesh of the cage, nested ones included, gets the shared grey
	# material. (The cage uses ShaderMaterials, so duplicating them "as
	# StandardMaterial3D" gave null and failed on the first property.)
	for mesh_inst in cage.find_children("*", "MeshInstance3D", true, false):
		mesh_inst.material_override = _grey_material()
		mesh_inst.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF

func _grey_material() -> StandardMaterial3D:
	if _grey == null:
		_grey = StandardMaterial3D.new()
		_grey.albedo_color = Color(0.4, 0.4, 0.4, 0.6)  # grey, semi-transparent
		_grey.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		_grey.cull_mode = BaseMaterial3D.CULL_DISABLED
	return _grey

func _remove_competitor_farm(comp_id: String) -> void:
	if not _competitor_farms.has(comp_id):
		return
	var data: Dictionary = _competitor_farms[comp_id]
	if data.root:
		data.root.queue_free()
	_competitor_farms.erase(comp_id)

## Competitor ids with a farm drawn in the fjord.
func farm_ids() -> Array:
	return _competitor_farms.keys()
