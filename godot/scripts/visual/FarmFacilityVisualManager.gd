class_name FarmFacilityVisualManager
extends Node3D
## The farm-type presenter (roadmap F2-20; farm-types spec §4, §5, §6.3): the
## company's land sites and offshore units in the 3D world, drawn from the
## ENGINE's units (SimBridge.get_facility_visuals, a pure read of
## SimulationEngine.FacilityVisualViews) on every state refresh.
##
## - A land site is a LandCampusBuilder on its carved pad (the terrain's own
##   pad, FjordTerrain.land_pad_slots); an offshore unit an OffshoreUnitVisual
##   on its slot. Nothing else draws them: the fjord's pens and feed barges
##   come from the fjord sites only (GetSeaSiteDetails leaves offshore units
##   out, land sites are no sea sites), so no pen grid or barge ever stands at
##   a land or offshore unit.
## - The sync diffs by unit id: a new unit is built, a known one only takes
##   its new state (construction phase, ready tanks, feeding), a unit whose
##   layout changed (a lot more, a pilot grown into a full lot) is laid out
##   again, a unit that is gone is freed, and nothing else moves.
## - The pads: a land unit's pad is painted by its blast (the terrain's
##   pad_state, heath to blasted granite, limited to the campus's ground); a
##   pad nobody holds goes back to heath.
## - Lights (spec §5.4, presentation clock D20): the kits' windows, lid
##   work-lights, roof lights and deck lights light at dusk and night; the
##   offshore lanterns flash at night.
## - Clicking a tank or a unit opens its anchored card (FacilityWorldCard) and
##   frames it: a tank its lot at the one-lot framing (380 m, -40 deg), the
##   rest of a campus the full-campus framing (520 m, -40 deg), an offshore
##   unit the OF1 framing (220 m, -18 deg) (spec §4, the F2-18 read gate).
##   Every unit is a FjordCamera focus target, so the clamps let the camera
##   reach a unit beyond the world's edge.
## - F2-21 (spec §4 "Camera"): every land pad and offshore slot is a focus
##   target too ("plot:<id>"), so the camera reaches each plot built or not;
##   a campus is framed at -40 deg from as near as shows it whole (fit_distance),
##   an offshore unit or slot at 220 m and -18 deg looking in toward the
##   fjord mouth (the coast on the horizon, ref O3). The bookmarks: Land is
##   the company's first land unit, else the hero pad; Hav its first offshore
##   unit, else the first slot (G cycles Fjord -> Land -> Hav, FjordCamera).
## - F2-21 (spec §4 "Wave driver"): the sea of the offshore unit nearest the
##   camera's focus (the engine's split of its plot's sea) drives the open
##   sea the water draws (EnvironmentDirector.set_target_sea); each unit's
##   chip shows its sea (OffshoreUnitVisual.set_sea).

signal facility_clicked(unit_id: String, tank_index: int, at: Vector3)
## F2-23: a unit the engine had under construction came into operation
## (`first`: the company's first of its variant; a loaded or staged unit that
## starts in operation is no completion). FirstSightDirector plays on it.
signal unit_completed(unit_id: String, variant: String, first: bool)
## F2-23: the views now belong to another game (a new game, a save loaded in
## the running scene): nothing in them came into operation now.
signal new_game_seen()

## The presenter's group (the camera hint names a bookmark's place through it).
const GROUP := "farm_facilities"

## Spec §4 framings (distance m, pitch deg).
const LAND_FRAMING := Vector2(520.0, -40.0)
const LOT_FRAMING := Vector2(380.0, -40.0)
const OF1_FRAMING := Vector2(220.0, -18.0)
## F2-21: the Havfarm-class framing (spec §4 gives 700 m at -25 deg, which
## puts the horizon 5 % above the frame's top against §4's own 10-25 % rule
## for the offshore framings; -16 deg sets it 16 % from the top with the
## 385 m hull a third of the frame wide). The hull is F2-29's; the camera
## can frame its plot already.
const HAVFARM_FRAMING := Vector2(700.0, -16.0)
const HAVFARM_DISC_M := 400.0
## F2-21: a campus or pad is framed from as near as shows it whole: this
## share of the frame kept clear on each side, the distance searched from
## the one-lot framing out to FIT_MAX_M, in FIT_STEP_M steps, for a frame of
## FIT_ASPECT (the game's 16:9).
const FIT_MARGIN := 0.06
const FIT_MAX_M := 1400.0
const FIT_STEP_M := 10.0
const FIT_ASPECT := 16.0 / 9.0
## Height (m over the pad) a campus's framing keeps in frame: its tallest pieces.
const CAMPUS_HEIGHT_M := 20.0
## A land framing looks in from off the quay edge, turned this far (rad)
## along the shore (land_yaw).
const LAND_YAW_SKEW := 0.45
## F2-21 accept 2: the land framing looks -35 to -45 deg down.
const LAND_PITCH_BAND := Vector2(-45.0, -35.0)
## The hero plots the bookmarks fly to when the company has no unit there.
const HERO_PAD := "pad_a"
const HERO_SLOT := "off_1"
## A plot's focus target id.
const PLOT_TARGET := "plot:%s"
## An offshore unit's or slot's focus disc: this many circumradii (the
## camera may orbit it).
const OFFSHORE_DISC := 4.0
const OF1_CIRCUMRADIUS_M := 55.0
## The deck height (m) an offshore framing looks at.
const OFFSHORE_FOCUS_Y := 9.0
## F2-23: the service vessel stands this far (deg) off the camera's line to
## the unit, on its left (unit_heading).
const VESSEL_SIDE_DEG := 84.0
## The fjord mouth an offshore framing looks in toward (world z of the sill,
## spec §4: the mouth at z = -1300).
const MOUTH_Z := -1300.0
## F2-21: how often (s) the units' seas are re-read once the day changed,
## and the open sea's target re-chosen.
const SEA_REFRESH_S := 0.5
const TARGET_REFRESH_S := 0.25
## Sun elevations (deg) over which the kits' lights come on at dusk.
const LIGHTS_ON_DEG := Vector2(6.0, -2.0)
const FACILITY_KIT := preload("res://assets/models/facility_kit/LK_FacilityKit.tres")
const FACILITY_KIT_BUILD := preload("res://assets/models/facility_kit/LK_FacilityKitBuild.tres")
const OFFSHORE_KIT := preload("res://assets/models/facility_kit/LK_OffshoreKit.tres")
## Night-light level of the windows and work-lights at full night.
const NIGHT_LIGHT := 0.8
## F2-23: the night lights keep their screen brightness as the eye adapts:
## their emission is scaled by this exposure over the eye's (at most 1,
## never under NIGHT_COMP_MIN), the way the site's lamps divide by it.
const NIGHT_EXPOSURE_REF := 1.6
const NIGHT_COMP_MIN := 0.08
## F2-23: by day an offshore unit's hull keeps this share of its yellow
## through the distance fog (offshore_kit.gdshader landmark_lift; spec §5.5
## rule 1: OF1 reads as a yellow sliver from the home view).
const LANDMARK_LIFT := 1.5
## F2-23: the edge of a first-sight sequence's light wave (m).
const LIGHT_WAVE_WIDTH_M := 70.0
## The card's canvas layer: over the world and the HUD, under the event cards.
const CARD_LAYER := 3
## A pad nobody's campus stands on (a migrated save's overflow pad): a
## generic frame round the plot's centre (m).
const GENERIC_PAD := Vector2(260.0, 180.0)

@export var terrain_path: NodePath = ^"../Terrain"
@export var camera_path: NodePath = ^"../FjordCamera"
## The see-through Fjord tab's bridge: the card closes when the world is covered.
@export var vista_path: NodePath = ^"../../FjordVistaBridge"

var terrain: Node = null
var camera: Node = null
## unit id -> LandCampusBuilder or OffshoreUnitVisual
var visuals: Dictionary = {}
## unit id -> its last view row
var views: Dictionary = {}
## Plot id -> its name (the plots' place names, from the engine's plot lists).
var plot_names: Dictionary = {}
var card: FacilityWorldCard = null
var _card_layer: CanvasLayer = null
var _pads_held: Dictionary = {}
var _director: Node = null
var _light_level := -1.0
## F2-23: a first-sight sequence holds the kits' light level (-1: the dusk's own).
var light_override := -1.0
var _night_comp := -1.0
var _fog_density := -1.0
## F2-23: the first-sight sequence of a variant's first completion.
var first_sight: FirstSightDirector = null
## F2-23: the teaser (Første sesong, once after day 150; presentation only).
var teaser: TeaserDirector = null
## Units with no model yet (the Havfarm-class hull, F2-29), warned once.
var _unsupported: Dictionary = {}
## Main-thread time (ms) of the latest sync and the slowest this session:
## building a campus is a frame the player waits on (the perf harness
## reports them against the P0-04 spike line).
var last_sync_ms := 0.0
var max_sync_ms := 0.0
## F2-21: offshore unit id -> its sea (SimBridge.get_sea_state_forecast).
var seas: Dictionary = {}
## F2-21: the unit whose plot's sea the open sea draws now ("" none).
var sea_target_unit := ""
## QA staging (ShowcaseCapture): a staged open sea is not replaced by the engine's.
var sea_target_locked := false
var _seas_dirty := true
## F2-21: focus target id -> the place's name (the terrain's plot names).
var _target_names: Dictionary = {}
var _sea_timer := 0.0
var _target_timer := 0.0
## F2-23: the game the views belong to (SimBridge.get_game_generation, which
## every new and loaded game changes; -1 before the first sync).
var _generation := -1


func _ready() -> void:
	add_to_group(GROUP)
	terrain = get_node_or_null(terrain_path)
	camera = get_node_or_null(camera_path)
	var env := get_parent()
	if env != null and "director" in env:
		_director = env.get("director")
	_card_layer = CanvasLayer.new()
	_card_layer.name = "FacilityCardLayer"
	_card_layer.layer = CARD_LAYER
	add_child(_card_layer)
	card = FacilityWorldCard.new()
	_card_layer.add_child(card)
	first_sight = FirstSightDirector.new()
	first_sight.manager = self
	add_child(first_sight)
	teaser = TeaserDirector.new()
	teaser.manager = self
	add_child(teaser)
	# The land kit (and the construction kit's props, F2-28) loads in the
	# background, so the first campus built (a player's build, a loaded save)
	# does not stall a frame reading it. A glb whose mesh the kit already holds
	# is never loaded again.
	for path: String in LandCampusBuilder.kit_paths() + ConstructionKitVisual.paths():
		if not LandCampusBuilder.has_kit_mesh(path) and not ResourceLoader.has_cached(path) \
				and ResourceLoader.load_threaded_get_status(path) == ResourceLoader.THREAD_LOAD_INVALID_RESOURCE:
			ResourceLoader.load_threaded_request(path)
	var sim := get_node_or_null("/root/SimBridge")
	if sim != null and sim.has_signal("state_updated"):
		sim.state_updated.connect(_on_state_updated)
	_register_plot_targets.call_deferred()
	var vista := get_node_or_null(vista_path)
	if vista != null and vista.has_signal("world_view_changed"):
		# A panel over the world (Esc back to the dashboard, another tab) closes the card.
		vista.world_view_changed.connect(func(world_visible: bool) -> void:
			if not world_visible and card != null:
				card.close())
	_on_state_updated.call_deferred()


func _on_state_updated() -> void:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_facility_visuals"):
		return
	var rows: Array = sim.get_facility_visuals()
	# The plots' names are read once a unit stands on a plot not yet named
	# (a game without farm-type units pays one read a day, not three).
	for row: Dictionary in rows:
		if not plot_names.has(str(row.get("plot_id", ""))):
			_refresh_plot_names(sim)
			break
	# A new or loaded game: its first sync is a baseline (wave4b int review: a
	# save loaded from the pause menu replayed the first sight of a unit long
	# in operation, its id the same as a unit the old game was building).
	var generation: int = sim.get_game_generation() if sim.has_method("get_game_generation") else _generation
	var baseline := generation != _generation
	_generation = generation
	sync_units(rows, baseline)
	_seas_dirty = true


func _refresh_plot_names(sim: Node) -> void:
	for getter in ["get_land_plots", "get_offshore_plots"]:
		if not sim.has_method(getter):
			continue
		for plot: Dictionary in sim.call(getter):
			plot_names[str(plot.get("id", ""))] = str(plot.get("name", ""))


## Bring the 3D world in line with the engine's units (rows of
## get_facility_visuals): build the new, update the known, free the gone.
## `baseline`: the rows are another game's (new or loaded); none of its units
## came into operation now.
func sync_units(rows: Array, baseline: bool = false) -> void:
	var started := Time.get_ticks_usec()
	if baseline:
		new_game_seen.emit()
	var seen := {}
	# F2-23: units that came into operation since the last sync, and how many
	# of each variant were in operation before it (a variant's first is the
	# first sight, a repeat the toast).
	var completed: Array = []
	var running := {}
	for id: String in views:
		if bool(views[id].get("operational", false)):
			running[str(views[id].get("variant", ""))] = int(running.get(str(views[id].get("variant", "")), 0)) + 1
	for row: Dictionary in rows:
		var id := str(row.get("unit_id", ""))
		if id == "":
			continue
		seen[id] = true
		if visuals.has(id) and layout_key(views.get(id, {})) != layout_key(row):
			# The unit grew (a lot or a module more) or changed shape: lay it out again.
			_remove(id)
		var was: Dictionary = views.get(id, {})
		if not baseline and not was.is_empty() and not bool(was.get("operational", false)) and bool(row.get("operational", false)):
			completed.append(id)
		views[id] = row
		if _unsupported.has(id):
			continue
		if not visuals.has(id):
			_create(row)
		else:
			visuals[id].call("apply_state", row)
	for id: String in views.keys():
		if not seen.has(id):
			if visuals.has(id):
				_remove(id)
			elif _unsupported.has(id) and camera != null and camera.has_method("unregister_focus_target"):
				camera.unregister_focus_target(id)
			views.erase(id)
			_unsupported.erase(id)
	_paint_pads()
	_update_bookmarks()
	if card != null and card.is_open() and views.has(card.unit_id):
		card.refresh(views[card.unit_id], plot_names.get(str(views[card.unit_id].get("plot_id", "")), ""))
	elif card != null and card.is_open():
		card.close()
	last_sync_ms = (Time.get_ticks_usec() - started) / 1000.0
	max_sync_ms = maxf(max_sync_ms, last_sync_ms)
	for id: String in completed:
		var variant := str(views[id].get("variant", ""))
		var first := int(running.get(variant, 0)) == 0
		running[variant] = int(running.get(variant, 0)) + 1
		unit_completed.emit(id, variant, first)


## What a unit's layout depends on: its plot, kind and lots. A row whose key
## changes is built again; any other change is only state.
static func layout_key(row: Dictionary) -> String:
	if str(row.get("family", "")) == "land":
		return LandCampusBuilder.layout_key(row)
	return "%s|%s" % [row.get("plot_id", ""), row.get("variant", "")]


func _create(row: Dictionary) -> void:
	var id := str(row["unit_id"])
	if str(row.get("family", "")) == "land":
		var campus := LandCampusBuilder.new()
		add_child(campus)
		var pad := _pad_for(row)
		campus.build(row, pad)
		campus.clicked.connect(_on_campus_clicked.bind(id))
		visuals[id] = campus
		if campus.lights != null:
			campus.lights.set_level(maxf(_light_level, 0.0))
		_register_campus_target(id, campus, pad)
	elif str(row.get("variant", "")) != "of1":
		# The Havfarm-class hull is stage 3 art (F2-29): nothing to draw yet.
		push_warning("[FarmFacilityVisualManager] no model for %s (%s) yet" % [id, row.get("variant", "")])
		_unsupported[id] = true
		# F2-21: the camera frames its plot all the same.
		var at := Vector3(float(row.get("plot_x", 0.0)), OFFSHORE_FOCUS_Y, float(row.get("plot_z", 0.0)))
		_register_target(id, at, HAVFARM_FRAMING, HAVFARM_DISC_M, offshore_yaw(at))
	else:
		var unit := OffshoreUnitVisual.new()
		add_child(unit)
		var at := Vector3(float(row.get("plot_x", 0.0)), OFFSHORE_FOCUS_Y, float(row.get("plot_z", 0.0)))
		unit.build(row, unit_heading(offshore_yaw(at)))
		unit.clicked.connect(_on_unit_clicked.bind(id))
		visuals[id] = unit
		_register_target(id, unit.focus_point(), OF1_FRAMING, unit.unit.circumradius_m * OFFSHORE_DISC,
			offshore_yaw(unit.focus_point()))
		if seas.has(id):
			unit.set_sea(seas[id])


func _remove(id: String) -> void:
	var node: Node = visuals[id]
	visuals.erase(id)
	remove_child(node)
	node.queue_free()
	if camera != null and camera.has_method("unregister_focus_target"):
		camera.unregister_focus_target(id)
	if card != null and card.unit_id == id:
		card.close()


## The terrain's pad a land unit stands on (centre, size, quay; the
## terrain computes its pads once); a pad the terrain does not carve (a
## migrated save's overflow pad) gets a frame round the plot's centre that
## the unit's campus fits in (LandCampusBuilder.frame_size_for).
func _pad_for(row: Dictionary) -> Dictionary:
	var plot_id := str(row.get("plot_id", ""))
	if terrain != null and terrain.has_method("land_pad_slots"):
		for pad: Dictionary in terrain.call("land_pad_slots"):
			if str(pad.get("id", "")) == plot_id:
				return pad
	var c := Vector3(float(row.get("plot_x", 0.0)), LandCampusBuilder.PAD_TOP_Y, float(row.get("plot_z", 0.0)))
	var size := LandCampusBuilder.frame_size_for(row)
	return {"id": plot_id, "centre": c, "size": size, "quay": c + Vector3(size.x * 0.5, 0.0, 0.0)}


func _register_target(id: String, pos: Vector3, framing: Vector2, radius: float, yaw: float = NAN) -> void:
	if camera != null and camera.has_method("register_focus_target"):
		camera.register_focus_target(id, pos, framing.x, framing.y, radius, yaw)


## A campus's target: its blasted ground at -40 deg, from as near as shows
## all of it (no nearer than the one-lot framing).
func _register_campus_target(id: String, campus: LandCampusBuilder, pad: Dictionary) -> void:
	var focus := campus.focus_point()
	var framing := land_framing(pad, focus, box_corners(campus.blast_rect(), LandCampusBuilder.PAD_TOP_Y, CAMPUS_HEIGHT_M))
	campus.set_meta("framing_yaw", framing.y)
	_register_target(id, focus, Vector2(framing.x, LAND_FRAMING.y), (pad.get("size", GENERIC_PAD) as Vector2).length() * 0.5 + 60.0, framing.y)


## F2-21: every land pad and offshore slot the terrain reserves is a focus
## target ("plot:<id>"): a pad framed whole at -40 deg, a slot at the OF1
## framing looking in toward the mouth. The units' own targets come on top.
func _register_plot_targets() -> void:
	if terrain == null or camera == null or not camera.has_method("register_focus_target"):
		return
	if terrain.has_method("land_pad_slots"):
		for pad: Dictionary in terrain.call("land_pad_slots"):
			var c: Vector3 = pad["centre"]
			var size: Vector2 = pad["size"]
			var focus := Vector3(c.x, LandCampusBuilder.PAD_TOP_Y, c.z)
			var rect := Vector4(c.x - size.x * 0.5, c.z - size.y * 0.5, c.x + size.x * 0.5, c.z + size.y * 0.5)
			var framing := land_framing(pad, focus, box_corners(rect, LandCampusBuilder.PAD_TOP_Y, 0.0))
			_register_target(PLOT_TARGET % str(pad["id"]), focus, Vector2(framing.x, LAND_FRAMING.y), size.length() * 0.5 + 60.0, framing.y)
			_target_names[PLOT_TARGET % str(pad["id"])] = str(pad.get("name", pad["id"]))
	if terrain.has_method("offshore_slots"):
		for slot: Dictionary in terrain.call("offshore_slots"):
			var focus: Vector3 = (slot["position"] as Vector3) + Vector3(0.0, OFFSHORE_FOCUS_Y, 0.0)
			_register_target(PLOT_TARGET % str(slot["id"]), focus, OF1_FRAMING, OF1_CIRCUMRADIUS_M * OFFSHORE_DISC, offshore_yaw(focus))
			_target_names[PLOT_TARGET % str(slot["id"])] = str(slot.get("name", slot["id"]))
	_update_bookmarks()


## F2-21: the place a focus target frames: a plot's name, or the name of the
## plot a unit stands on.
func target_name(id: String) -> String:
	if _target_names.has(id):
		return str(_target_names[id])
	if views.has(id):
		var plot := str(views[id].get("plot_id", ""))
		return str(plot_names.get(plot, _target_names.get(PLOT_TARGET % plot, plot)))
	return id


## F2-21 / F2-23 (spec §4, §6.4): a campus's or a pad's framing, (distance,
## yaw): -40 deg from the fjord's mouth side, looking up the fjord over the
## campus to the pen grid behind it, from as near as shows it whole. Where
## the terrain would lift the camera off -35..-45 deg there (the rig's
## avoidance), from off the pad's quay edge (land_yaw), then from up the
## fjord looking out to the mouth.
func land_framing(pad: Dictionary, focus: Vector3, corners: Array[Vector3]) -> Vector2:
	var down_fjord := _yaw_for(focus)
	for yaw: float in [down_fjord + PI, land_yaw(pad), down_fjord]:
		var d := fit_distance(corners, focus, yaw, LAND_FRAMING.y)
		var pitch := solved_pitch(focus, yaw, d, LAND_FRAMING.y)
		if pitch >= LAND_PITCH_BAND.x and pitch <= LAND_PITCH_BAND.y:
			return Vector2(d, wrapf(yaw, -PI, PI))
	return Vector2(fit_distance(corners, focus, land_yaw(pad), LAND_FRAMING.y), land_yaw(pad))


## The camera's heading down the fjord at `focus` (its home_yaw: the camera
## up the fjord, looking out to the mouth).
func _yaw_for(focus: Vector3) -> float:
	return float(camera.call("home_yaw", focus)) if camera != null and camera.has_method("home_yaw") else 0.0


## The pitch (deg) the camera rig actually stands at for a framing: its
## terrain avoidance may lift it steeper than asked.
func solved_pitch(focus: Vector3, yaw: float, distance: float, pitch_deg: float) -> float:
	if camera == null or not camera.has_method("solve_rig"):
		return pitch_deg
	var flat := Vector3(focus.x, 0.0, focus.z)
	var pos: Vector3 = camera.call("solve_rig", flat, yaw, distance, pitch_deg)
	return rad_to_deg(asin((flat - pos).normalized().y))


## F2-21: a land framing's fallback yaw (FjordCamera's convention): from over
## the water off the pad's quay edge (the pad faces the channel), turned
## LAND_YAW_SKEW along the shore so the lot rows run across the frame.
static func land_yaw(pad: Dictionary) -> float:
	var c: Vector3 = pad.get("centre", Vector3.ZERO)
	var q: Vector3 = pad.get("quay", c + Vector3.RIGHT)
	var out := Vector2(q.x - c.x, q.z - c.z)
	out = out.normalized() if out.length() > 0.01 else Vector2(1.0, 0.0)
	return atan2(out.x, out.y) + LAND_YAW_SKEW


## F2-21: an offshore framing's yaw (FjordCamera's convention: the camera
## stands at focus + (sin, 0, cos) * distance): from the open sea, looking in
## toward the fjord mouth, so the coast and its hills stand on the horizon
## behind the unit (ref O3).
func offshore_yaw(focus: Vector3) -> float:
	var mouth_x := float(terrain.call("channel_center_x", MOUTH_Z)) if terrain != null and terrain.has_method("channel_center_x") else 0.0
	var ahead := Vector2(mouth_x - focus.x, MOUTH_Z - focus.z)
	if ahead.length() < 1.0:
		ahead = Vector2(0.0, 1.0)
	ahead = ahead.normalized()
	return atan2(-ahead.x, -ahead.y)


## F2-23: an offshore unit's heading for a framing at `yaw`: its
## walk-to-work landing (the model's -X side) turned VESSEL_SIDE_DEG from
## the camera's line to its left, so the service vessel moored there stands
## beside the ring, in view (refs O2, O3).
static func unit_heading(yaw: float) -> float:
	var to_camera := Vector2(sin(yaw), cos(yaw))
	var left := Vector2(-cos(yaw), sin(yaw))
	var side := deg_to_rad(VESSEL_SIDE_DEG)
	var landing := (to_camera * cos(side) + left * sin(side)).normalized()
	return atan2(landing.y, -landing.x)


## The eight corners of the box over a world rect (x0, z0, x1, z1) from
## height `y` up `height` metres.
static func box_corners(rect: Vector4, y: float, height: float) -> Array[Vector3]:
	var out: Array[Vector3] = []
	for x: float in [rect.x, rect.z]:
		for z: float in [rect.y, rect.w]:
			for h: float in [0.0, height]:
				out.append(Vector3(x, y + h, z))
	return out


## F2-21: the nearest framing distance (m) at which every one of `points`
## stands inside a 16:9 frame (FIT_MARGIN clear of each edge) for a camera
## at `pitch_deg` and `yaw` round `focus` (FjordCamera's rig, its field of
## view from the distance table), searched from the one-lot framing out;
## FIT_MAX_M if none.
static func fit_distance(points: Array[Vector3], focus: Vector3, yaw: float, pitch_deg: float) -> float:
	var d := LOT_FRAMING.x
	while d < FIT_MAX_M:
		if frames_all(points, focus, yaw, pitch_deg, d, FIT_ASPECT, FIT_MARGIN):
			return d
		d += FIT_STEP_M
	return FIT_MAX_M


## True when a camera at `distance`, `pitch_deg` and `yaw` round `focus`,
## looking at it with the distance table's field of view and `aspect`, sees
## every point inside the frame `margin` clear of its edges.
static func frames_all(points: Array[Vector3], focus: Vector3, yaw: float, pitch_deg: float, distance: float,
		aspect: float, margin: float) -> bool:
	var pitch := deg_to_rad(pitch_deg)
	var cam := focus + Vector3(sin(yaw), 0.0, cos(yaw)) * distance * cos(pitch) + Vector3(0.0, -distance * sin(pitch), 0.0)
	var fwd := (focus - cam).normalized()
	var right := fwd.cross(Vector3.UP).normalized()
	var up := right.cross(fwd)
	var half_v := tan(deg_to_rad(FjordCamera.view_for_distance(distance).y) * 0.5)
	var half_h := half_v * aspect
	for p: Vector3 in points:
		var v := p - cam
		var z := v.dot(fwd)
		if z <= 1.0:
			return false
		if absf(v.dot(right) / (z * half_h)) > 1.0 - margin * 2.0 or absf(v.dot(up) / (z * half_v)) > 1.0 - margin * 2.0:
			return false
	return true


## F2-21 bookmarks: Land the first land unit drawn (by id), else the hero
## pad; Hav the first offshore unit drawn, else the hero slot.
func _update_bookmarks() -> void:
	if camera == null or not camera.has_method("set_bookmark"):
		return
	var land := ""
	var smolt := ""
	var hav := ""
	var ids := visuals.keys()
	ids.sort()
	for id: String in ids:
		if visuals[id] is LandCampusBuilder:
			# A grow-out campus before a post-smolt site: the hero is the campus.
			if str(views.get(id, {}).get("variant", "")) == "post_smolt":
				smolt = id if smolt == "" else smolt
			elif land == "":
				land = id
		elif hav == "" and visuals[id] is OffshoreUnitVisual:
			hav = id
	land = land if land != "" else smolt
	camera.call("set_bookmark", "land", land if land != "" else PLOT_TARGET % HERO_PAD)
	camera.call("set_bookmark", "hav", hav if hav != "" else PLOT_TARGET % HERO_SLOT)


## Each held pad by its campus's blast (0 heath .. 1 granite) over the
## campus's ground; free pads heath.
func _paint_pads() -> void:
	if terrain == null or not terrain.has_method("set_pad_state"):
		return
	var held := {}
	var blasts := {}
	for id: String in visuals:
		var campus := visuals[id] as LandCampusBuilder
		if campus == null:
			continue
		var plot := str(views[id].get("plot_id", ""))
		held[plot] = maxf(campus.blast_state(), float(held.get(plot, 0.0)))
		var r := campus.blast_rect()
		blasts[plot] = _merge_rects(blasts[plot], r) if blasts.has(plot) else r
	for plot: String in held:
		terrain.call("set_pad_state", plot, held[plot], blasts.get(plot, Vector4.ZERO))
	for plot: String in _pads_held:
		if not held.has(plot):
			terrain.call("set_pad_state", plot, 0.0, Vector4.ZERO)
	_pads_held = held


static func _merge_rects(a: Vector4, b: Vector4) -> Vector4:
	return Vector4(minf(a.x, b.x), minf(a.y, b.y), maxf(a.z, b.z), maxf(a.w, b.w))


func _on_campus_clicked(tank_index: int, at: Vector3, id: String) -> void:
	var campus: LandCampusBuilder = visuals.get(id)
	if campus == null:
		return
	var anchor := campus.tank_position(tank_index) + Vector3(0.0, LandCampusBuilder.GROWOUT_H, 0.0) if tank_index >= 0 else at
	open_card(id, tank_index, anchor)
	if camera != null and camera.has_method("frame_position"):
		if tank_index >= 0:
			camera.frame_position(campus.lot_centre(campus.tank_lot(tank_index)), LOT_FRAMING.x, LOT_FRAMING.y, 1.2,
				float(campus.get_meta("framing_yaw", land_yaw(campus.pad))))
		else:
			camera.focus_target(id)
	facility_clicked.emit(id, tank_index, at)


func _on_unit_clicked(at: Vector3, id: String) -> void:
	var unit: OffshoreUnitVisual = visuals.get(id)
	if unit == null:
		return
	open_card(id, -1, unit.unit.global_position + Vector3(0.0, unit.unit.air_draught_m, 0.0))
	if camera != null and camera.has_method("focus_target"):
		camera.focus_target(id)
	facility_clicked.emit(id, -1, at)


## Open the anchored card of unit `id` (tank -1: the unit) hanging from `anchor`.
func open_card(id: String, tank_index: int, anchor: Vector3) -> void:
	if card == null or not views.has(id):
		return
	var row: Dictionary = views[id]
	var tank := {}
	var campus := visuals.get(id) as LandCampusBuilder
	if campus != null and tank_index >= 0 and tank_index < campus.tanks.size():
		tank = campus.tanks[tank_index]
	card.open(row, tank_index, tank, plot_names.get(str(row.get("plot_id", "")), ""), anchor)


func _process(delta: float) -> void:
	_update_lights()
	_sea_timer -= delta
	if _seas_dirty and _sea_timer <= 0.0:
		_sea_timer = SEA_REFRESH_S
		_refresh_seas()
	_target_timer -= delta
	if _target_timer <= 0.0:
		_target_timer = TARGET_REFRESH_S
		_update_sea_target()


## F2-21: each offshore unit's sea today and its forecast (the engine's
## cached read, once per plot and day), handed to its chip.
func _refresh_seas() -> void:
	if sea_target_locked:
		return
	_seas_dirty = false
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_sea_state_forecast"):
		return
	var fresh := {}
	for id: String in visuals:
		var unit := visuals[id] as OffshoreUnitVisual
		if unit == null:
			continue
		var forecast: Dictionary = sim.get_sea_state_forecast(id)
		if not bool(forecast.get("ok", false)):
			continue
		fresh[id] = forecast
		unit.set_sea(forecast)
	seas = fresh


## F2-21 (spec §4 "Wave driver"): the open sea draws the sea of the offshore
## unit nearest the camera's focus; none, the wind model's open coast.
func _update_sea_target() -> void:
	if _director == null or not _director.has_method("set_target_sea") or sea_target_locked:
		return
	var focus: Vector3 = camera.call("get_focus_point") if camera != null and camera.has_method("get_focus_point") else Vector3.ZERO
	var best := ""
	var best_d := INF
	for id: String in seas:
		var unit := visuals.get(id) as OffshoreUnitVisual
		if unit == null:
			continue
		var d := Vector2(unit.station.x - focus.x, unit.station.z - focus.z).length()
		if d < best_d:
			best_d = d
			best = id
	sea_target_unit = best
	if best == "":
		_director.call("set_target_sea", {})
		return
	var sea: Dictionary = (seas[best].get("today", {}) as Dictionary).duplicate()
	sea["plot_id"] = str(seas[best].get("plot_id", ""))
	sea["issued_day"] = int(seas[best].get("issued_day", -1))
	_director.call("set_target_sea", sea)


## Windows, lid work-lights, roof lights and deck lights from dusk to dawn;
## the offshore lanterns at night (presentation clock); F2-23 the yard's
## lamps and pools of light (CampusLights). A first-sight sequence may hold
## the level (light_override >= 0: the lid work-lights come on as its camera
## passes).
func _update_lights() -> void:
	if _director == null or not _director.has_method("get_composed"):
		return
	var sun := float(_director.call("get_composed").get("sun_elevation", 30.0))
	var level := clampf(inverse_lerp(LIGHTS_ON_DEG.x, LIGHTS_ON_DEG.y, sun), 0.0, 1.0)
	if light_override >= 0.0:
		level = light_override
	var exposure := maxf(SiteLights.exposure, 1e-3)
	var fog := SiteLights.fog_density(_director as EnvironmentDirector)
	CampusLights.set_frame(level, exposure, fog)
	if not is_equal_approx(fog, _fog_density):
		_fog_density = fog
		for mat: ShaderMaterial in [OFFSHORE_KIT, FACILITY_KIT, FACILITY_KIT_BUILD]:
			mat.set_shader_parameter("fog_density", fog)
	var comp := clampf(NIGHT_EXPOSURE_REF / exposure, NIGHT_COMP_MIN, 1.0)
	if not is_equal_approx(comp, _night_comp):
		_night_comp = comp
		for mat: ShaderMaterial in [FACILITY_KIT, FACILITY_KIT_BUILD]:
			mat.set_shader_parameter("night_comp", comp)
	if is_equal_approx(level, _light_level):
		return
	_light_level = level
	for id: String in visuals:
		var campus := visuals[id] as LandCampusBuilder
		if campus != null and campus.lights != null:
			campus.lights.set_level(level)
	FACILITY_KIT.set_shader_parameter("night_light", level * NIGHT_LIGHT)
	FACILITY_KIT_BUILD.set_shader_parameter("night_light", level * NIGHT_LIGHT)
	OFFSHORE_KIT.set_shader_parameter("night_light", level * NIGHT_LIGHT)
	for mat: ShaderMaterial in [OFFSHORE_KIT, FACILITY_KIT, FACILITY_KIT_BUILD]:
		mat.set_shader_parameter("landmark_lift", LANDMARK_LIFT * (1.0 - level))
	OFFSHORE_KIT.set_shader_parameter("nav_lights", 1.0 if level > 0.5 else 0.0)


## The light level the kits are lit at now (0 day .. 1 night).
func light_level() -> float:
	return maxf(_light_level, 0.0)


## F2-23: a first-sight sequence switches the campus lights on in a wave:
## everything within `radius` m of `origin` (world x/z) is lit; a radius
## of 1e7 (the default) lights it all.
func set_light_wave(origin: Vector2, radius: float) -> void:
	for mat: ShaderMaterial in [FACILITY_KIT, FACILITY_KIT_BUILD, CampusLights.shared_material()]:
		mat.set_shader_parameter("light_wave_origin", origin)
		mat.set_shader_parameter("light_wave_radius", radius)
		mat.set_shader_parameter("light_wave_width", LIGHT_WAVE_WIDTH_M)
	for id: String in visuals:
		var campus := visuals[id] as LandCampusBuilder
		if campus != null and campus.lights != null:
			campus.lights.set_wave(origin, radius, LIGHT_WAVE_WIDTH_M)


## The campus or unit node of a unit id (null when not drawn).
func visual_of(id: String) -> Node3D:
	return visuals.get(id)
