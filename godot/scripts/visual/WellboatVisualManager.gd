extends Node
class_name WellboatVisualManager

## Visual wellboat manager: animates wellboats sailing along the fjord
## channel between a harbour and target sites. Driven by the simulation's
## WellboatJob queue (SimBridge.get_wellboat_jobs: site, kind, eta_days): a
## boat sails out when a job is booked, waits alongside the pen while the job
## is queued and sails home once the job has been carried out and left the
## queue.
##
## Harvests (roadmap U2-05): a planned harvest order (SimBridge.get_harvest_orders)
## sends the large harvest carrier out when it is placed; it lies alongside
## with "Slakting · om N d" until its slot, "Slakting · pågår" while the
## slaughterhouse takes the batches, and sails home when the order is done,
## its chip showing the harvest's net ("Slakting · ferdig · +8,05 MNOK"). An
## express harvest's spot wellboat comes out, loads and leaves. A land farm's
## own hall needs no wellboat.

const ChannelBoat := preload("res://scripts/visual/ChannelBoat.gd")

## Job colours: the D13 Ink signal tokens (ok #7FD69B, warn #F2B705, info
## #8CC7DA). They colour the boat's job chip, never its hull (A3-01).
const JOB_COLOURS := {
	"harvest": Color(0.498, 0.839, 0.608, 1.0),
	"delouse": Color(0.949, 0.718, 0.020, 1.0),
	"other": Color(0.549, 0.780, 0.855, 1.0),
}
## Job ids of harvest boats: an order's ("harvest_order:<order id>") and an
## express harvest's ("harvest_express:<report id>").
const ORDER_PREFIX := "harvest_order:"
const EXPRESS_PREFIX := "harvest_express:"
## Routes that send no wellboat (a land farm's own hall).
const HALL_ROUTE := "OnSiteHall"

## Chip text per job kind: translation key and Norwegian fallback.
const JOB_LABELS := {
	"harvest": ["UI_VESSEL_JOB_HARVEST", "Slakting"],
	"delouse": ["UI_VESSEL_JOB_DELOUSE", "Avlusing"],
	"rov": ["UI_VESSEL_JOB_ROV", "ROV-inspeksjon"],
	"freshwater": ["UI_VESSEL_JOB_FRESHWATER", "Ferskvannsbad"],
	"other": ["UI_VESSEL_JOB_OTHER", "Brønnbåtoppdrag"],
}

## The Ronja-class 77 x 17.8 m wellboat; harvest jobs send the 116 x 23 m
## Ronja Storm-class carrier.
@export var wellboat_scene: PackedScene = preload("res://assets/models/wellboat/wellboat.tscn")
@export var harvest_wellboat_scene: PackedScene = preload("res://assets/models/wellboat/wellboat_large.tscn")
## The harbour: a point on the fjord channel, as a fraction of the fjord's
## length (-0.5 and 0.5 are its ends). The player's pens fill the channel
## slots from the -0.5 end and the rival farms from the other, so boats from
## here pass only the player's own pens.
@export_range(-0.5, 0.5) var harbour_along: float = -0.47
## A boat lies at its site's wellboat berth (FishPenManager.get_berth):
## alongside the pen on the fairway side, clear of the mooring frame's grid
## buoys (35 m out) for the widest wellboat. The feed barge lies on the
## other side.
@export var return_delay: float = 2.0  # seconds a boat lingers after its job is done
@export var sail_seconds: float = 4.0  # one way, harbour to site

signal wellboat_arrived(pen_id: String)
signal wellboat_returned(pen_id: String)

var _sim_bridge = null
var _terrain = null
var _fjord_env = null
var _active_wellboats: Dictionary = {}  # job_id -> {boat, path, follow, site_id, state, tween, ...}
## Harvest report ids already seen (an express harvest's report sends its boat once).
var _seen_reports: Dictionary = {}
var _reports_primed := false

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
	queued.append_array(_sync_harvests(sim_day))
	# A job that left the queue has been carried out: its boat sails home.
	# An express harvest's boat turns home once it has loaded.
	for job_id in _active_wellboats.keys():
		if job_id not in queued and not str(job_id).begins_with(EXPRESS_PREFIX):
			_start_return(job_id)


## U2-05: the harvest orders' carriers, and an express harvest's spot boat.
## Returns the job ids that stay out (the open orders).
func _sync_harvests(sim_day: int) -> Array:
	var out: Array = []
	if not _sim_bridge.has_method("get_harvest_orders"):
		return out
	for o in _sim_bridge.get_harvest_orders():
		if str(o.get("route", "")) == HALL_ROUTE:
			continue
		var job_id := ORDER_PREFIX + str(o.get("id", ""))
		var site_id := str(o.get("holding_id", ""))
		var slot := int(o.get("slot_day", sim_day))
		out.append(job_id)
		if not _active_wellboats.has(job_id):
			_spawn_wellboat(job_id, site_id, "harvest", slot, "")
		if _active_wellboats.has(job_id):
			_active_wellboats[job_id]["harvesting"] = str(o.get("status", "")) == "Harvesting"
			_update_wellboat_progress(job_id, sim_day, slot)
	for r in _sim_bridge.get_harvest_reports():
		var id := str(r.get("id", ""))
		if _seen_reports.has(id):
			continue
		_seen_reports[id] = true
		# Reports that were there when the scene came up are history.
		if not _reports_primed or str(r.get("mode", "")) != "Express" or str(r.get("route", "")) == HALL_ROUTE:
			continue
		var job_id := EXPRESS_PREFIX + id
		_spawn_wellboat(job_id, str(r.get("holding_id", "")), "harvest", sim_day, "")
		if _active_wellboats.has(job_id):
			_active_wellboats[job_id]["express"] = true
			_active_wellboats[job_id]["payoff"] = _payoff_text(id)
	_reports_primed = true
	return out


## The net of the harvest `key` names (an order id or a report id): "+8,05 MNOK".
func _payoff_text(key: String) -> String:
	var net := 0.0
	var found := false
	for r in _sim_bridge.get_harvest_reports():
		if str(r.get("order_id", "")) == key or str(r.get("id", "")) == key:
			net += float(r.get("net_nok", 0.0))
			found = true
	return ("+" if net >= 0.0 else "") + SimFormatters.format_nok(net) if found else ""

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
	
	# Along the channel at sea level, past the other sites, to alongside the
	# pen. The old route left from inside the fjord wall and arced 80 m over
	# the terrain.
	var scene := harvest_wellboat_scene if kind == "harvest" and harvest_wellboat_scene else wellboat_scene
	var node := scene.instantiate()
	var wellboat := node as VesselModel
	if wellboat == null:
		node.free()
		return
	var berth := pen_mgr.get_berth(site_id, "wellboat", wellboat.beam_m)
	var route := ChannelBoat.berth_route(terrain, harbour_along * terrain.terrain_size.y, berth,
		pen_mgr.site_zones(site_id, "wellboat"), wellboat)
	var parts := ChannelBoat.launch_boat(_fjord_env, wellboat, route, "Wellboat_%s" % job_id)
	# The job shows on a chip above the boat; the hull keeps its paint.
	var chip := VesselChip.new()
	parts.boat.add_child(chip)
	chip.position = parts.boat.get_chip_anchor() if parts.boat.has_method("get_chip_anchor") else Vector3(0, 24, 0)
	# Au2-01: the main engine, propeller and wash ride with the boat.
	var audio := get_node_or_null("/root/AudioController")
	if audio != null and audio.has_method("attach_loop"):
		audio.attach_loop(parts.boat, "boat_engine")

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
		"chip": chip,
	}
	_update_chip(job_id, int(_sim_bridge.current_day) if _sim_bridge else due_day)

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
	# An express harvest's spot boat loads and leaves (its job never queues).
	if bool(data.get("express", false)):
		_start_return(job_id)

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
	if str(job_id).begins_with(ORDER_PREFIX) and _sim_bridge != null:
		data["payoff"] = _payoff_text(str(job_id).trim_prefix(ORDER_PREFIX))
	_update_chip(job_id, int(data.due_day))
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
	_update_chip(job_id, sim_day)

## The job chip a boat carries (null when the job has no boat).
func job_chip(job_id: String) -> VesselChip:
	return _active_wellboats[job_id].chip if _active_wellboats.has(job_id) else null

## The text on job `job_id`'s chip ("" when it has no boat).
func chip_text_of(job_id: String) -> String:
	var chip := job_chip(job_id)
	return chip.get_text() if chip != null and chip.has_method("get_text") else ""

## "Avlusing · om 2 d" while the job waits, "Avlusing · ferdig" on the way home.
static func chip_text(kind: String, days_left: int, done: bool) -> String:
	var entry: Array = JOB_LABELS.get(kind, JOB_LABELS["other"])
	var job := _tr_or(entry[0], entry[1])
	if done:
		return _tr_or("UI_VESSEL_JOB_DONE", "%s · ferdig") % job
	if days_left <= 0:
		return _tr_or("UI_VESSEL_JOB_TODAY", "%s · i dag") % job
	return _tr_or("UI_VESSEL_JOB_ETA", "%s · om %d d") % [job, days_left]

func _update_chip(job_id: String, sim_day: int) -> void:
	var data: Dictionary = _active_wellboats[job_id]
	var chip: VesselChip = data.get("chip")
	if chip == null:
		return
	var colour: Color = JOB_COLOURS.get(data.kind, JOB_COLOURS["other"])
	var text := chip_text(data.kind, int(data.due_day) - sim_day, data.state == "return")
	if data.state != "return" and bool(data.get("harvesting", false)):
		text = _tr_or("UI_VESSEL_JOB_UNDER_WAY", "%s · pågår") % _tr_or(JOB_LABELS["harvest"][0], JOB_LABELS["harvest"][1])
	if data.state == "return" and str(data.get("payoff", "")) != "":
		text += " · " + str(data.payoff)
	chip.set_chip(text, colour)

static func _tr_or(key: String, fallback: String) -> String:
	var text := TranslationServer.translate(key)
	return fallback if text == key else text

func _remove_wellboat(job_id: String) -> void:
	if not _active_wellboats.has(job_id):
		return
	var data: Dictionary = _active_wellboats[job_id]
	if data.path:
		data.path.queue_free()  # the boat rides on the path's follower
	_active_wellboats.erase(job_id)