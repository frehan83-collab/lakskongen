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

signal facility_clicked(unit_id: String, tank_index: int, at: Vector3)

## Spec §4 framings (distance m, pitch deg).
const LAND_FRAMING := Vector2(520.0, -40.0)
const LOT_FRAMING := Vector2(380.0, -40.0)
const OF1_FRAMING := Vector2(220.0, -18.0)
## Sun elevations (deg) over which the kits' lights come on at dusk.
const LIGHTS_ON_DEG := Vector2(6.0, -2.0)
const FACILITY_KIT := preload("res://assets/models/facility_kit/LK_FacilityKit.tres")
const FACILITY_KIT_BUILD := preload("res://assets/models/facility_kit/LK_FacilityKitBuild.tres")
const OFFSHORE_KIT := preload("res://assets/models/facility_kit/LK_OffshoreKit.tres")
## Night-light level of the windows and work-lights at full night.
const NIGHT_LIGHT := 0.8
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
## Units with no model yet (the Havfarm-class hull, F2-29), warned once.
var _unsupported: Dictionary = {}
## Main-thread time (ms) of the latest sync and the slowest this session:
## building a campus is a frame the player waits on (the perf harness
## reports them against the P0-04 spike line).
var last_sync_ms := 0.0
var max_sync_ms := 0.0


func _ready() -> void:
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
	# The land kit loads in the background, so the first campus built (a
	# player's build, a loaded save) does not stall a frame reading it.
	# A glb whose mesh the kit already holds is never loaded again.
	for path: String in LandCampusBuilder.kit_paths():
		if not LandCampusBuilder.has_kit_mesh(path) and not ResourceLoader.has_cached(path) \
				and ResourceLoader.load_threaded_get_status(path) == ResourceLoader.THREAD_LOAD_INVALID_RESOURCE:
			ResourceLoader.load_threaded_request(path)
	var sim := get_node_or_null("/root/SimBridge")
	if sim != null and sim.has_signal("state_updated"):
		sim.state_updated.connect(_on_state_updated)
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
	sync_units(rows)


func _refresh_plot_names(sim: Node) -> void:
	for getter in ["get_land_plots", "get_offshore_plots"]:
		if not sim.has_method(getter):
			continue
		for plot: Dictionary in sim.call(getter):
			plot_names[str(plot.get("id", ""))] = str(plot.get("name", ""))


## Bring the 3D world in line with the engine's units (rows of
## get_facility_visuals): build the new, update the known, free the gone.
func sync_units(rows: Array) -> void:
	var started := Time.get_ticks_usec()
	var seen := {}
	for row: Dictionary in rows:
		var id := str(row.get("unit_id", ""))
		if id == "":
			continue
		seen[id] = true
		if visuals.has(id) and layout_key(views.get(id, {})) != layout_key(row):
			# The unit grew (a lot or a module more) or changed shape: lay it out again.
			_remove(id)
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
			views.erase(id)
			_unsupported.erase(id)
	_paint_pads()
	if card != null and card.is_open() and views.has(card.unit_id):
		card.refresh(views[card.unit_id], plot_names.get(str(views[card.unit_id].get("plot_id", "")), ""))
	elif card != null and card.is_open():
		card.close()
	last_sync_ms = (Time.get_ticks_usec() - started) / 1000.0
	max_sync_ms = maxf(max_sync_ms, last_sync_ms)


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
		_register_target(id, campus.focus_point(), LAND_FRAMING, (pad.get("size", GENERIC_PAD) as Vector2).length() * 0.5 + 60.0)
	elif str(row.get("variant", "")) != "of1":
		# The Havfarm-class hull is stage 3 art (F2-29): nothing to draw yet.
		push_warning("[FarmFacilityVisualManager] no model for %s (%s) yet" % [id, row.get("variant", "")])
		_unsupported[id] = true
	else:
		var unit := OffshoreUnitVisual.new()
		add_child(unit)
		unit.build(row)
		unit.clicked.connect(_on_unit_clicked.bind(id))
		visuals[id] = unit
		_register_target(id, unit.focus_point(), OF1_FRAMING, unit.unit.circumradius_m * 4.0)


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


func _register_target(id: String, pos: Vector3, framing: Vector2, radius: float) -> void:
	if camera != null and camera.has_method("register_focus_target"):
		camera.register_focus_target(id, pos, framing.x, framing.y, radius)


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
			camera.frame_position(campus.lot_centre(campus.tank_lot(tank_index)), LOT_FRAMING.x, LOT_FRAMING.y)
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


func _process(_delta: float) -> void:
	_update_lights()


## Windows, lid work-lights, roof lights and deck lights from dusk to dawn;
## the offshore lanterns at night (presentation clock).
func _update_lights() -> void:
	if _director == null or not _director.has_method("get_composed"):
		return
	var sun := float(_director.call("get_composed").get("sun_elevation", 30.0))
	var level := clampf(inverse_lerp(LIGHTS_ON_DEG.x, LIGHTS_ON_DEG.y, sun), 0.0, 1.0)
	if is_equal_approx(level, _light_level):
		return
	_light_level = level
	FACILITY_KIT.set_shader_parameter("night_light", level * NIGHT_LIGHT)
	FACILITY_KIT_BUILD.set_shader_parameter("night_light", level * NIGHT_LIGHT)
	OFFSHORE_KIT.set_shader_parameter("night_light", level * NIGHT_LIGHT)
	OFFSHORE_KIT.set_shader_parameter("nav_lights", 1.0 if level > 0.5 else 0.0)


## The light level the kits are lit at now (0 day .. 1 night).
func light_level() -> float:
	return maxf(_light_level, 0.0)


## The campus or unit node of a unit id (null when not drawn).
func visual_of(id: String) -> Node3D:
	return visuals.get(id)
