extends Node
class_name WellboatVisualManager

## Visual wellboat manager: animates wellboats travelling between a
## harbour and target sites along spline paths. Driven by the simulation's
## WellboatJob queue (due day, site, kind).

@export var wellboat_scene: PackedScene = preload("res://assets/models/boat/service_boat.tscn")
@export var harbour_position: Vector3 = Vector3(-500.0, 0.0, -500.0)
@export var travel_speed: float = 80.0  # m/s visual (accelerated for visibility)
@export var return_delay: float = 2.0  # seconds at destination before return

signal wellboat_arrived(pen_id: String)
signal wellboat_returned(pen_id: String)

var _sim_bridge = null
var _terrain = null
var _fjord_env = null
var _active_wellboats: Dictionary = {}  # job_id -> {boat, path, follow, tween, job_data}

func _ready() -> void:
	_fjord_env = get_parent()
	_terrain = _fjord_env.get_node_or_null("Terrain")
	var sim = get_node_or_null("/root/SimBridge")
	if sim:
		bind_simulation(sim)
	print("[WellboatVisualManager] Ready")

func bind_simulation(sim_bridge) -> void:
	_sim_bridge = sim_bridge
	if _sim_bridge:
		_sim_bridge.state_updated.connect(_on_state_updated)
		_sim_bridge.day_advanced.connect(_on_day_advanced)

func _on_state_updated() -> void:
	if not _sim_bridge:
		return
	_sync_wellboats()

func _on_day_advanced(day: int, season: String) -> void:
	_sync_wellboats()

func _sync_wellboats() -> void:
	if not _sim_bridge or not _sim_bridge.has_method("get_all_wellboat_jobs"):
		return
	var jobs: Array = _sim_bridge.get_all_wellboat_jobs()
	if not jobs:
		return
	
	var sim_day: int = _sim_bridge.get("day", 0) if _sim_bridge.has("day") else 0
	
	var active_job_ids: Array = []
	
	for job_dict in jobs:
		var job_id: String = str(job_dict.get("id", ""))
		var site_id: String = str(job_dict.get("site_id", ""))
		var kind: String = str(job_dict.get("kind", "harvest"))
		var due_day: int = int(job_dict.get("due_day", 0))
		var method: String = str(job_dict.get("method", ""))
		
		if job_id.is_empty() or site_id.is_empty():
			continue
		
		active_job_ids.append(job_id)
		
		if _active_wellboats.has(job_id):
			_update_wellboat_progress(job_id, sim_day, due_day)
		else:
			_spawn_wellboat(job_id, site_id, kind, due_day, method)
	
	var to_remove: Array = []
	for job_id in _active_wellboats.keys():
		if job_id not in active_job_ids:
			to_remove.append(job_id)
	for jid in to_remove:
		_remove_wellboat(jid)

func _spawn_wellboat(job_id: String, site_id: String, kind: String, due_day: int, method: String) -> void:
	if not _terrain:
		return
	
	var pen_mgr: FishPenManager = _fjord_env.get_node_or_null("FishPenManager")
	if not pen_mgr or not pen_mgr.has_method("get_pen_position"):
		return
	var target_pos: Vector3 = pen_mgr.get_pen_position(site_id)
	if target_pos == Vector3.ZERO:
		return
	
	var boat := wellboat_scene.instantiate()
	boat.name = "Wellboat_%s" % job_id
	_fjord_env.add_child(boat)
	boat.global_position = harbour_position
	
	var path3d := Path3D.new()
	path3d.name = "WellboatPath_%s" % job_id
	_fjord_env.add_child(path3d)
	
	var curve := Curve3D.new()
	var mid_point := (harbour_position + target_pos) * 0.5 + Vector3(0, 80.0, 0)
	curve.add_point(harbour_position)
	curve.add_point(mid_point)
	curve.add_point(target_pos)
	path3d.curve = curve
	
	var follow := PathFollow3D.new()
	follow.name = "Follow"
	path3d.add_child(follow)
	follow.progress_ratio = 0.0
	follow.rotation_mode = PathFollow3D.ROTATION_Y
	
	boat.reparent(follow)
	boat.global_position = harbour_position
	
	_apply_job_colour(boat, kind)
	
	_active_wellboats[job_id] = {
		"boat": boat,
		"path": path3d,
		"follow": follow,
		"target_pos": target_pos,
		"kind": kind,
		"method": method,
		"due_day": due_day,
		"state": "outbound",
		"arrival_time": 0.0,
	}
	
	_animate_wellboat(job_id)

func _apply_job_colour(boat: Node3D, kind: String) -> void:
	var hull := boat.get_node_or_null("Hull")
	if not hull:
		return
	var deck := hull.get_node_or_null("Deck")
	if not deck:
		return
	var mat: BaseMaterial3D = deck.material_override
	if not mat:
		return
	if kind == "harvest":
		mat.albedo_color = Color(0.3, 0.6, 0.3, 1.0)
	elif kind == "delouse":
		mat.albedo_color = Color(0.8, 0.4, 0.2, 1.0)
	else:
		mat.albedo_color = Color(0.2, 0.4, 0.8, 1.0)

func _animate_wellboat(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	var data: Dictionary = _active_wellboats[job_id]
	var follow: PathFollow3D = data.follow
	var tween := create_tween()
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.set_trans(Tween.TRANS_QUART)
	tween.tween_property(follow, "progress_ratio", 1.0, 4.0)
	tween.tween_callback(_on_wellboat_arrived.bind(job_id))
	tween.tween_callback(_start_return.bind(job_id)).set_delay(4.0 + return_delay)

func _on_wellboat_arrived(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	var data: Dictionary = _active_wellboats[job_id]
	data.state = "at_destination"
	data.arrival_time = Time.get_time_dict_from_system()["epoch"]
	wellboat_arrived.emit(data.target_pos.x.to_string())

func _start_return(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	var data: Dictionary = _active_wellboats[job_id]
	data.state = "return"
	var follow: PathFollow3D = data.follow
	var tween := create_tween()
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.set_trans(Tween.TRANS_QUART)
	tween.tween_property(follow, "progress_ratio", 0.0, 4.0)
	tween.tween_callback(_on_wellboat_returned.bind(job_id))

func _on_wellboat_returned(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	wellboat_returned.emit(job_id)
	_remove_wellboat(job_id)

func _update_wellboat_progress(job_id: String, sim_day: int, due_day: int) -> void:
	if not _active_wellboats.has(job_id):
		return
	_active_wellboats[job_id].due_day = due_day

func _remove_wellboat(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	var data: Dictionary = _active_wellboats[job_id]
	if data.boat:
		data.boat.queue_free()
	if data.path:
		data.path.queue_free()
	_active_wellboats.erase(job_id)