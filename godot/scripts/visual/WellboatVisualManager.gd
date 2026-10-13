extends Node
class_name WellboatVisualManager

## Visual wellboat manager: animates wellboats sailing along the fjord
## channel between a harbour and target sites. Driven by the simulation's
## WellboatJob queue (SimBridge.get_wellboat_jobs: site, kind, eta_days): a
## boat sails out when a job is booked, waits alongside the pen while the job
## is queued and sails home once the job has been carried out and left the
## queue.

const ChannelBoat := preload("res://scripts/visual/ChannelBoat.gd")

const JOB_COLOURS := {
	"harvest": Color(0.3, 0.6, 0.3, 1.0),
	"delouse": Color(0.8, 0.4, 0.2, 1.0),
	"other": Color(0.2, 0.4, 0.8, 1.0),
}

@export var wellboat_scene: PackedScene = preload("res://assets/models/boat/service_boat.tscn")
## The harbour: a point on the fjord channel, as a fraction of the fjord's
## length (-0.5 and 0.5 are its ends). The player's pens fill the channel
## slots from the -0.5 end and the rival farms from the other, so boats from
## here pass only the player's own pens.
@export_range(-0.5, 0.5) var harbour_along: float = -0.47
## Metres beside the channel centreline the boats sail (the pens sit on it).
@export var lane_offset: float = 45.0
@export var return_delay: float = 2.0  # seconds a boat lingers after its job is done
@export var sail_seconds: float = 4.0  # one way, harbour to site

signal wellboat_arrived(pen_id: String)
signal wellboat_returned(pen_id: String)

var _sim_bridge = null
var _terrain = null
var _fjord_env = null
var _active_wellboats: Dictionary = {}  # job_id -> {boat, path, follow, site_id, state, tween, ...}

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
	# This used to ask for get_all_wellboat_jobs, which exists nowhere, so no
	# boat ever sailed (review F744).
	if not _sim_bridge:
		return
	var jobs: Array = _sim_bridge.get_wellboat_jobs()
	var sim_day: int = int(_sim_bridge.current_day)
	var queued: Array = []
	for job_dict in jobs:
		var job_id: String = str(job_dict.get("id", ""))
		var site_id: String = str(job_dict.get("site_id", ""))
		if job_id.is_empty() or site_id.is_empty():
			continue
		queued.append(job_id)
		# The bridge reports days left; the boat keeps the absolute due day.
		var due_day: int = sim_day + int(job_dict.get("eta_days", 0))
		if _active_wellboats.has(job_id):
			_update_wellboat_progress(job_id, sim_day, due_day)
		else:
			_spawn_wellboat(job_id, site_id, str(job_dict.get("kind", "harvest")), due_day,
				str(job_dict.get("method", "")))
	# A job that left the queue has been carried out: its boat sails home.
	for job_id in _active_wellboats.keys():
		if job_id not in queued:
			_start_return(job_id)

## Job ids with a boat on the water (outbound, on site or returning).
func active_job_ids() -> Array:
	return _active_wellboats.keys()

## Where a job's boat is: "outbound", "at_destination" or "return" ("" if none).
func job_state(job_id: String) -> String:
	return str(_active_wellboats[job_id].state) if _active_wellboats.has(job_id) else ""

func _spawn_wellboat(job_id: String, site_id: String, kind: String, due_day: int, method: String) -> void:
	var terrain := _terrain as FjordTerrain
	if terrain == null:
		return
	var pen_mgr: FishPenManager = _fjord_env.get_node_or_null("FishPenManager")
	if not pen_mgr or not pen_mgr.has_method("get_pen_position"):
		return
	var target_pos: Vector3 = pen_mgr.get_pen_position(site_id)
	if target_pos == Vector3.ZERO:
		return
	
	# Along the channel at sea level to alongside the pen. The old route left
	# from inside the fjord wall and arced 80 m over the terrain.
	var route := terrain.channel_route(harbour_along * terrain.terrain_size.y, target_pos.z, lane_offset)
	var parts := ChannelBoat.launch(_fjord_env, wellboat_scene, route, "Wellboat_%s" % job_id)
	if not ChannelBoat.tint_deck(parts.boat, JOB_COLOURS.get(kind, JOB_COLOURS["other"])):
		push_warning("[WellboatVisualManager] boat scene has no tintable Deck")
	
	_active_wellboats[job_id] = {
		"boat": parts.boat,
		"path": parts.path,
		"follow": parts.follow,
		"site_id": site_id,
		"target_pos": target_pos,
		"kind": kind,
		"method": method,
		"due_day": due_day,
		"state": "outbound",
		"tween": null,
	}
	
	_animate_wellboat(job_id)

func _animate_wellboat(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	var data: Dictionary = _active_wellboats[job_id]
	var tween := create_tween()
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.set_trans(Tween.TRANS_QUART)
	tween.tween_property(data.follow, "progress_ratio", 1.0, sail_seconds)
	tween.tween_callback(_on_wellboat_arrived.bind(job_id))
	data.tween = tween

func _on_wellboat_arrived(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	var data: Dictionary = _active_wellboats[job_id]
	data.state = "at_destination"
	wellboat_arrived.emit(data.site_id)

func _start_return(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	var data: Dictionary = _active_wellboats[job_id]
	if data.state == "return":
		return
	data.state = "return"
	if data.tween:
		data.tween.kill()
	ChannelBoat.face_home(data.boat)
	var tween := create_tween()
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.set_trans(Tween.TRANS_QUART)
	tween.tween_interval(return_delay)
	tween.tween_property(data.follow, "progress_ratio", 0.0, sail_seconds)
	tween.tween_callback(_on_wellboat_returned.bind(job_id))
	data.tween = tween

func _on_wellboat_returned(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	wellboat_returned.emit(_active_wellboats[job_id].site_id)
	_remove_wellboat(job_id)

func _update_wellboat_progress(job_id: String, sim_day: int, due_day: int) -> void:
	if not _active_wellboats.has(job_id):
		return
	_active_wellboats[job_id].due_day = due_day

func _remove_wellboat(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	var data: Dictionary = _active_wellboats[job_id]
	if data.path:
		data.path.queue_free()  # the boat rides on the path's follower
	_active_wellboats.erase(job_id)