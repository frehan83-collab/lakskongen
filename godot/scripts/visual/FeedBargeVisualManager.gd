extends Node
class_name FeedBargeVisualManager

## Feed barges: every stocked site keeps one moored on station beside its
## pen, the way Norwegian farms store and blow their feed (roadmap A3-01).
## A barge is towed out along the fjord channel at sea level from the feed
## depot when a site is stocked, lies moored with its feed lines towards the
## pen while the site holds fish, and is towed home once the site is
## emptied; sites already stocked when the fjord first syncs (a new game or
## a loaded save) have theirs on station from the start. Sites licensed for
## a large biomass get the 64.8 m exposed-site barge instead of the 37 m one.
##
## Each barge carries a VesselChip (FARM zoom only): "Fôrflåte · fôrer" in
## feed yellow while the sim reports feeding, "Fôrflåte · i ro" otherwise.
## Nothing is painted on the hull.

const ChannelBoat := preload("res://scripts/visual/ChannelBoat.gd")
## D13 buoy yellow (#E8B92A): the chip's accent while the barge feeds.
const FEED_YELLOW := Color(0.910, 0.725, 0.165, 1.0)
## Ink secondary (#9FB4BB): the chip's accent while it idles.
const IDLE_GREY := Color(0.624, 0.706, 0.733, 1.0)
## Barge chips show at FARM zoom only (metres from the camera).
const CHIP_DISTANCE := 700.0

@export var feed_barge_scene: PackedScene = preload("res://assets/models/feed_barge/feed_barge.tscn")
@export var large_feed_barge_scene: PackedScene = preload("res://assets/models/feed_barge/feed_barge_large.tscn")
## Sites licensed for at least this much biomass (kg) get the large barge.
@export var large_barge_min_biomass_kg: float = 6000000.0
## The depot: a point on the fjord channel, as a fraction of the fjord's
## length (-0.5 and 0.5 are its ends), at the player's end like the wellboat
## harbour.
@export_range(-0.5, 0.5) var depot_along: float = -0.45
## Metres beside the channel centreline: the side where the pen's feed hose
## runs out to its hose-end float (40 m from the pen centre), clear of the
## mooring frame's corner buoys. Wellboats use the other side.
@export var lane_offset: float = 52.0
@export var tow_seconds: float = 6.0  # one way, depot to station
@export var max_concurrent_barges: int = 12

signal feed_barge_arrived(site_id: String)
signal feed_barge_returned(site_id: String)

var _sim_bridge = null
var _terrain = null
var _fjord_env = null
var _active_barges: Dictionary = {}  # site_id -> {boat, path, follow, target_pos, state, tween, chip}
var _first_sync_done := false
var _on_station_at_start: Dictionary = {}  # site ids stocked when the fjord first synced

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
	var stocked: Array = []
	for site_dict in sites:
		var site_id: String = str(site_dict.get("id", ""))
		var biomass: float = float(site_dict.get("biomass_kg", 0.0))
		var fish_count: int = int(site_dict.get("fish_count", 0))
		if site_id.is_empty() or biomass <= 0.0 or fish_count <= 0:
			continue
		stocked.append(site_id)
	# Sites already stocked when the fjord first sees the farm (a new game or
	# a loaded save) have their barge on station; later stockings see it
	# towed out.
	if not _first_sync_done and not sites.is_empty():
		_first_sync_done = true
		for site_id in stocked:
			_on_station_at_start[site_id] = true
	for site_dict in sites:
		var site_id: String = str(site_dict.get("id", ""))
		if site_id not in stocked:
			continue
		if not _active_barges.has(site_id):
			if _active_barges.size() >= max_concurrent_barges:
				continue
			_spawn_barge(site_id, site_dict, _on_station_at_start.has(site_id))
		elif _active_barges[site_id].state == "return":
			_tow_out(site_id)  # restocked while it was being towed home
		_update_chip(site_id, bool(site_dict.get("is_feeding", false)))
	# A site that no longer holds fish sends its barge home.
	for site_id in _active_barges.keys():
		if site_id not in stocked:
			_start_return(site_id)

## The barge model a site gets.
func barge_scene_for(site: Dictionary) -> PackedScene:
	if float(site.get("max_biomass", 0.0)) >= large_barge_min_biomass_kg and large_feed_barge_scene:
		return large_feed_barge_scene
	return feed_barge_scene

func _spawn_barge(site_id: String, site_data: Dictionary, on_station: bool = false) -> void:
	var terrain := _terrain as FjordTerrain
	if terrain == null:
		return
	var pen_mgr: FishPenManager = _fjord_env.get_node_or_null("FishPenManager")
	if not pen_mgr or not pen_mgr.has_method("get_pen_position"):
		return
	var target_pos: Vector3 = pen_mgr.get_pen_position(site_id)
	if target_pos == Vector3.ZERO:
		return
	# Towed along the channel at sea level, never over the terrain.
	var route := terrain.channel_route(depot_along * terrain.terrain_size.y, target_pos.z, lane_offset)
	var parts := ChannelBoat.launch(_fjord_env, barge_scene_for(site_data), route, "FeedBarge_%s" % site_id)
	var chip := VesselChip.new(CHIP_DISTANCE)
	parts.boat.add_child(chip)
	chip.position = parts.boat.get_chip_anchor() if parts.boat.has_method("get_chip_anchor") else Vector3(0, 16, 0)
	_active_barges[site_id] = {
		"boat": parts.boat,
		"path": parts.path,
		"follow": parts.follow,
		"target_pos": target_pos,
		"state": "towing",
		"tween": null,
		"chip": chip,
	}
	_update_chip(site_id, bool(site_data.get("is_feeding", false)))
	if on_station:
		(parts.follow as PathFollow3D).progress_ratio = 1.0
		_on_barge_arrived(site_id)
	else:
		_tow_out(site_id)

func _tow_out(site_id: String) -> void:
	var data: Dictionary = _active_barges[site_id]
	data.state = "towing"
	if data.tween:
		data.tween.kill()
	(data.boat as Node3D).rotation.y = 0.0
	var tween := create_tween()
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.set_trans(Tween.TRANS_QUART)
	tween.tween_property(data.follow, "progress_ratio", 1.0, tow_seconds * (1.0 - (data.follow as PathFollow3D).progress_ratio))
	tween.tween_callback(_on_barge_arrived.bind(site_id))
	data.tween = tween

func _on_barge_arrived(site_id: String) -> void:
	if not _active_barges.has(site_id):
		return
	var data: Dictionary = _active_barges[site_id]
	data.state = "moored"
	_face_feed_side_to_pen(data)
	feed_barge_arrived.emit(site_id)
	var pen_mgr: FishPenManager = _fjord_env.get_node_or_null("FishPenManager")
	if pen_mgr and pen_mgr.has_method("_maybe_emit_feeding"):
		pen_mgr._maybe_emit_feeding(site_id, {"is_feeding": true})

## Moored, the barge turns its feeding side (starboard, local +X: the feed
## lines float out that way) towards its pen.
func _face_feed_side_to_pen(data: Dictionary) -> void:
	var boat: Node3D = data.boat
	boat.rotation.y = 0.0
	var to_pen: Vector3 = data.target_pos - boat.global_position
	if boat.global_transform.basis.x.dot(to_pen) < 0.0:
		boat.rotation.y = PI

func _start_return(site_id: String) -> void:
	if not _active_barges.has(site_id):
		return
	var data: Dictionary = _active_barges[site_id]
	if data.state == "return":
		return
	data.state = "return"
	_on_station_at_start.erase(site_id)  # a restocked site sees its barge towed out
	if data.tween:
		data.tween.kill()
	ChannelBoat.face_home(data.boat)
	var follow: PathFollow3D = data.follow
	var tween := create_tween()
	tween.set_ease(Tween.EASE_IN_OUT)
	tween.set_trans(Tween.TRANS_QUART)
	tween.tween_property(follow, "progress_ratio", 0.0, maxf(tow_seconds * follow.progress_ratio, 0.05))
	tween.tween_callback(_on_barge_returned.bind(site_id))
	data.tween = tween

func _on_barge_returned(site_id: String) -> void:
	if not _active_barges.has(site_id) or _active_barges[site_id].state != "return":
		return
	feed_barge_returned.emit(site_id)
	_remove_barge(site_id)

func _remove_barge(site_id: String) -> void:
	if not _active_barges.has(site_id):
		return
	var data: Dictionary = _active_barges[site_id]
	if data.tween:
		data.tween.kill()
	if data.path:
		data.path.queue_free()  # the boat rides on the path's follower
	_active_barges.erase(site_id)

func _update_chip(site_id: String, feeding: bool) -> void:
	if not _active_barges.has(site_id):
		return  # no barge yet (the terrain or pen was not ready)
	var chip: VesselChip = _active_barges[site_id].get("chip")
	if chip == null:
		return
	var barge := _tr_or("UI_VESSEL_FEED_BARGE", "Fôrflåte")
	if feeding:
		chip.set_chip(_tr_or("UI_VESSEL_BARGE_FEEDING", "%s · fôrer") % barge, FEED_YELLOW)
	else:
		chip.set_chip(_tr_or("UI_VESSEL_BARGE_IDLE", "%s · i ro") % barge, IDLE_GREY)

static func _tr_or(key: String, fallback: String) -> String:
	var text := TranslationServer.translate(key)
	return fallback if text == key else text

## Site ids with a barge on the water (towed out, moored or towed home).
func active_site_ids() -> Array:
	return _active_barges.keys()

## Where a site's barge is: "towing", "moored" or "return" ("" if none).
func barge_state(site_id: String) -> String:
	return str(_active_barges[site_id].state) if _active_barges.has(site_id) else ""

## A site's barge model (null if none).
func barge_of(site_id: String) -> Node3D:
	return _active_barges[site_id].boat if _active_barges.has(site_id) else null
