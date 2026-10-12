extends Node
class_name FeedBargeVisualManager

## Visual feed barge manager: animates feed barges travelling between a
## feed depot and active farming sites along spline paths. Runs continuously
## while sites have fish (they need daily feeding).

@export var feed_barge_scene: PackedScene = preload("res://assets/models/boat/service_boat.tscn")
@export var depot_position: Vector3 = Vector3(500.0, 0.0, 500.0)
@export var travel_speed: float = 60.0  # m/s visual (accelerated for visibility)
@export var return_delay: float = 1.5  # seconds at destination before return
@export var max_concurrent_barges: int = 4

signal feed_barge_arrived(site_id: String)
signal feed_barge_returned(site_id: String)

var _sim_bridge = null
var _terrain = null
var _fjord_env = null
var _active_barges: Dictionary = {}  # site_id -> {boat, path, follow, tween, state}
var _spawn_queue: Array = []

func _ready() -> void:
	_fjord_env = get_parent()
	_terrain = _fjord_env.get_node_or_null("Terrain")
	var sim = get_node_or_null("/root/SimBridge")
	if sim:
		bind_simulation(sim)
	print("[FeedBargeVisualManager] Ready")

func bind_simulation(sim_bridge) -> void:
	_sim_bridge = sim_bridge
	if _sim_bridge:
		_sim_bridge.state_updated.connect(_on_state_updated)

func _on_state_updated() -> void:
	if not _sim_bridge:
		return
	_sync_barges()

func _sync_barges() -> void:
	if not _sim_bridge or not _sim_bridge.has_method("get_all_pens"):
		return
	var sites: Array = _sim_bridge.get_all_pens()
	if not sites:
		return
	
	var active_site_ids: Array = []
	
	for site_dict in sites:
		var site_id: String = str(site_dict.get("id", ""))
		var biomass: float = float(site_dict.get("biomass_kg", 0.0))
		var fish_count: int = int(site_dict.get("fish_count", 0))
		
		if site_id.is_empty() or biomass <= 0.0 or fish_count <= 0:
			continue
		
		active_site_ids.append(site_id)
		
		if not _active_barges.has(site_id):
			# Check if we can spawn a new barge (limit concurrent)
			if _active_barges.size() >= max_concurrent_barges:
				continue
			_spawn_barge(site_id, site_dict)
	
	# Remove barges for sites that no longer have fish
	var to_remove: Array = []
	for site_id in _active_barges.keys():
		if site_id not in active_site_ids:
			to_remove.append(site_id)
	for sid in to_remove:
		_remove_barge(sid)

func _spawn_barge(site_id: String, site_data: Dictionary) -> void:
	if not _terrain:
		return
	
	var pen_mgr: FishPenManager = _fjord_env.get_node_or_null("FishPenManager")
	if not pen_mgr or not pen_mgr.has_method("get_pen_position"):
		return
	var target_pos: Vector3 = pen_mgr.get_pen_position(site_id)
	if target_pos == Vector3.ZERO:
		return
	
	# Check if barge already exists for this site
	if _active_barges.has(site_id):
		return
	
	var boat := feed_barge_scene.instantiate()  # reuse service boat model
	boat.name = "FeedBarge_%s" % site_id
	_fjord_env.add_child(boat)
	boat.global_position = depot_position
	
	var path3d := Path3D.new()
	path3d.name = "FeedBargePath_%s" % site_id
	_fjord_env.add_child(path3d)
	
	var curve := Curve3D.new()
	var mid_point := (depot_position + target_pos) * 0.5 + Vector3(0, 50.0, 0)
	curve.add_point(depot_position)
	curve.add_point(mid_point)
	curve.add_point(target_pos)
	path3d.curve = curve
	
	var follow := PathFollow3D.new()
	follow.name = "Follow"
	path3d.add_child(follow)
	follow.progress_ratio = 0.0
	follow.rotation_mode = PathFollow3D.ROTATION_Y
	
	boat.reparent(follow)
	boat.global_position = depot_position
	
	# Feed barge color (yellow/orange for feed)
	_apply_feed_colour(boat)
	
	_active_barges[site_id] = {
		"boat": boat,
		"path": path3d,
		"follow": follow,
		"target_pos": target_pos,
		"state": "outbound",
	}
	
	_animate_barge(site_id)

func _apply_feed_colour(boat: Node3D) -> void:
	var hull := boat.get_node_or_null("Hull")
	if not hull:
		return
	var deck := hull.get_node_or_null("Deck")
	if not deck:
		return
	var mat: BaseMaterial3D = deck.material_override
	if not mat:
		return
	# Feed barge yellow
	mat.albedo_color = Color(0.9, 0.7, 0.1, 1.0)

func _animate_barge(site_id: String) -> void:
	if not _active_barges.has(site_id):
		return
	var data: Dictionary = _active_barges[site_id]
	var follow: PathFollow3D = data.follow
	var tween := create_tween()
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.set_trans(Tween.TRANS_QUART)
	tween.tween_property(follow, "progress_ratio", 1.0, 3.0)
	tween.tween_callback(_on_barge_arrived.bind(site_id))
	tween.tween_callback(_start_return.bind(site_id)).set_delay(2.0 + return_delay)

func _on_barge_arrived(site_id: String) -> void:
	if not _active_barges.has(site_id):
		return
	var data: Dictionary = _active_barges[site_id]
	data.state = "feeding"
	feed_barge_arrived.emit(site_id)
	# Trigger feeding particles at the site
	var pen_mgr: FishPenManager = _fjord_env.get_node_or_null("FishPenManager")
	if pen_mgr and pen_mgr.has_method("_maybe_emit_feeding"):
		pen_mgr._maybe_emit_feeding(site_id, {"is_feeding": true})

func _start_return(site_id: String) -> void:
	if not _active_barges.has(site_id):
		return
	var data: Dictionary = _active_barges[site_id]
	data.state = "return"
	var follow: PathFollow3D = data.follow
	var tween := create_tween()
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.set_trans(Tween.TRANS_QUART)
	tween.tween_property(follow, "progress_ratio", 0.0, 3.0)
	tween.tween_callback(_on_barge_returned.bind(site_id))

func _on_barge_returned(site_id: String) -> void:
	if not _active_barges.has(site_id):
		return
	feed_barge_returned.emit(site_id)
	_remove_barge(site_id)

func _remove_barge(site_id: String) -> void:
	if not _active_barges.has(site_id):
		return
	var data: Dictionary = _active_barges[site_id]
	if data.boat:
		data.boat.queue_free()
	if data.path:
		data.path.queue_free()
	_active_barges.erase(site_id)