class_name OffshoreUnitVisual
extends Node3D
## One offshore unit in the 3D sea (roadmap F2-20; farm-types spec §4, §5.2,
## §5.4, §6.3): the OF1-class semi-submersible built by
## blender/build_offshore.py (OffshoreUnitModel, of1_class.tscn) on its slot,
## with the offshore fleet (blender/build_vessels.py) for its construction
## phase, all from the ENGINE's view of the unit (SimBridge.get_facility_visuals:
## the sea lane's F2-07 construction project, phase by phase).
##
## Phases (the project's keys, OffshoreProfiles):
## - yard_contract: the unit is still paper; four yellow survey buoys mark
##   the slot (the yard picture-in-picture is UI);
## - hull: the hull rises at the yard; at the slot the moorings are pre-laid,
##   their eight pick-up buoys in four pairs on the anchor bearings;
## - voyage: the finished unit approaches from the open sea, behind one tug;
## - tow: three anchor tugs bring it the last 600 m (while the project waits
##   for the player's "Start slep nå" or shelters, they hold it there);
## - mooring: on station, the three tugs at the anchor bearings;
## - commissioning: on station, the crew boat at the boat landing;
## - operational: the orange DP service vessel alongside its walk-to-work
##   landing.
## The unit heaves and rolls a little on the drawn water (WaveField.hull over
## its 110 m, damped: a semi-submersible at a 43 m draught barely moves).
##
## Mirror pass (G2-03): the net hangs under the water, so it lives on
## PlanarReflection.NO_MIRROR_LAYER at every LOD (layers_for(), PenCage's
## rule); the solid frame lays its submerged part on the water plane in
## offshore_kit.gdshader. Picking: one Area3D cylinder round the ring.
##
## The sea chip (F2-21, spec §4 "Weather and exposure display", §5.5 rules 2
## and 6): an Ink chip over the unit with the engine's sea at its plot
## (SimBridge.get_sea_state_forecast: "Hs 2,4 m · Tp 10 s · i morgen 3,1 m",
## and when no fish can be handled today, when the next handling window
## comes) and a status glyph, colour plus shape: a circle while a standard
## vessel can work, a triangle while only a DP vessel can, a square when
## none can. It shows near the unit (CHIP_NEAR_M), and from anywhere when the
## unit is lost in the weather: the fog's transmittance to it under
## CHIP_FOG_TRANSMITTANCE, or the sea-fog layer up and the unit beyond its
## visibility (rule 2, F2-18). Far off it shortens to the Hs. Its colours
## never come near the hull's safety yellow (rule 6: STATUS_COLORS, delta E
## >= 20 against HULL_YELLOW, tests/test_camera_bookmarks.py).

signal clicked(at: Vector3)

const UNIT_SCENE := preload("res://assets/models/offshore/of1_class.tscn")
const FLEET := {
	"offshore_service": "res://assets/models/offshore_service/offshore_service.tscn",
	"anchor_tug": "res://assets/models/offshore_service/anchor_tug.tscn",
	"crew_boat": "res://assets/models/crew_boat/crew_boat.tscn",
}
const BUOY_MESH := "res://assets/models/site_kit/mooring_buoy_LOD0.glb"
const BUOY_MATERIAL := preload("res://assets/models/site_kit/mooring_buoy_material.tres")
const HAVFARM_SCENE := preload("res://assets/models/offshore/havfarm_class.tscn")
## The sea lane's construction phases in build order (OffshoreProfiles).
const PHASES := ["yard_contract", "hull", "voyage", "tow", "mooring", "commissioning"]
## Parts that hang under the water (PenCage.NO_MIRROR_PARTS' rule).
const NO_MIRROR_PARTS := ["Net"]
## Where the unit comes from on its voyage: the open sea, away from the coast (-Z).
const APPROACH_DIR := Vector3(0.0, 0.0, -1.0)
## Voyage: from this far out to the tow's start; the tow brings it in (m).
const VOYAGE_FROM_M := 2600.0
const TOW_FROM_M := 600.0
## Anchor tugs stand this far off on station during the mooring (m).
const MOORING_TUG_M := 260.0
## Survey buoys round the slot, and the moorings' pick-up buoys (m from its
## centre; OF1's eight lines run in four pairs to anchors 1,100 m out, [O:R6]).
const SURVEY_RADIUS_M := 70.0
const PICKUP_RADIUS_M := 170.0
const PICKUP_PAIR_DEG := 7.0
## Buoy scale on the site kit's mooring buoy (survey markers, pick-up buoys).
const BUOY_SCALE := 2.0
## Heave and roll on the drawn water, damped for a semi-submersible.
const HEAVE_GAIN := 0.3
const TILT_GAIN := 0.25
## The sea chip (F2-21): shown within this distance (m) of the camera, or
## from anywhere once the weather hides the unit (spec §5.5 rule 2).
const CHIP_NEAR_M := 1000.0
const CHIP_FOG_TRANSMITTANCE := 0.4
const CHIP_SEA_FOG := 0.5
## Past this camera distance (m) the chip fades out even in weather.
const CHIP_MAX_M := 9000.0
## The hull's swatch (spec §5.2) and the chip's status colours (Ink surface
## ok and crit; the warning a deeper amber than Ink's #F2B705, which lies
## delta E 11 from the hull): ok, warn, crit.
const HULL_YELLOW := Color("#E6BE2E")
const STATUS_COLORS := [Color("#7FD69B"), Color("#F29A3C"), Color("#FF8A80")]
## The forecast rows that decide the glyph: a standard vessel's work, a DP
## vessel's, and fish handling (the window the chip counts down to).
const ROW_STANDARD := "resupply"
const ROW_DP := "resupply_dp"
const ROW_HANDLING := "delousing_harvest"
## A day counts as a handling window from this probability (spec §6.5: P >= 0.8 is solid green).
const WINDOW_P := 0.8

var view: Dictionary = {}
## F2-23: the unit's heading on its moorings (rad about +Y): its walk-to-work
## landing, and the service vessel there, turned toward the camera's side of
## its framing (the presenter), so the vessel stands beside the ring in view.
var heading := 0.0
var unit: OffshoreUnitModel = null
var fleet: Node3D = null
var buoys: MultiMeshInstance3D = null
var station := Vector3.ZERO
var _area: Area3D = null
var _phase_key := ""
## How far out (m) the unit stands along its approach (-1: not placed yet).
var _offset := -1.0
var _fleet_key := ""
var _buoy_mesh: Mesh = null
## F2-21: the engine's sea at the unit (get_sea_state_forecast) and its chip.
var sea: Dictionary = {}
var chip: VesselChip = null
var _chip_key := ""
var _director: Node = null
## F2-23: a cinematic over the unit holds its chip back.
var chip_suppressed := false


## The render layers an offshore part is drawn on: what hangs under the
## water stays out of the planar mirror's pass.
static func layers_for(part: String) -> int:
	return PlanarReflection.NO_MIRROR_LAYER if part in NO_MIRROR_PARTS else 1


func build(unit_view: Dictionary, unit_heading: float = 0.0) -> void:
	view = unit_view
	heading = unit_heading
	name = "Offshore_%s" % str(view.get("unit_id", "unit")).validate_node_name()
	station = Vector3(float(view.get("plot_x", 0.0)), 0.0, float(view.get("plot_z", 0.0)))
	unit = UNIT_SCENE.instantiate() as OffshoreUnitModel
	unit.name = "Unit"
	add_child(unit)
	for level in 3:
		for mesh in unit.get_lod_meshes(level):
			mesh.layers = layers_for(String(mesh.name))
	fleet = Node3D.new()
	fleet.name = "Fleet"
	add_child(fleet)
	_build_buoys()
	_build_area()
	chip = VesselChip.new(CHIP_MAX_M)
	chip.name = "SeaChip"
	chip.visible = false
	add_child(chip)
	apply_state(unit_view)


func _build_buoys() -> void:
	if str(view.get("variant", "")) == "havfarm":
		_swap_to_havfarm()
	var scene := load(BUOY_MESH) as PackedScene
	if scene != null:
		var root := scene.instantiate()
		_buoy_mesh = (root.find_children("*", "MeshInstance3D", true, false)[0] as MeshInstance3D).mesh
		root.free()
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.mesh = _buoy_mesh
	buoys = MultiMeshInstance3D.new()
	buoys.name = "SlotBuoys"
	buoys.multimesh = mm
	buoys.material_override = BUOY_MATERIAL
	buoys.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	buoys.visible = false
	add_child(buoys)


func _swap_to_havfarm() -> void:
	var old := unit
	unit = HAVFARM_SCENE.instantiate() as OffshoreUnitModel
	unit.name = "Unit"
	for child in old.get_children():
		if String(child.name) in ["Hull", "Columns", "Bracing", "Topsides", "Net", "Bulkhead", "Cranes",
				"NavLights", "Beacon", "W2WLanding", "LOD1", "LOD2"]:
			continue
		old.remove_child(child)
		unit.add_child(child)
	remove_child(old)
	old.free()
	add_child(unit)
	move_child(unit, 0)
	for level in 3:
		for mesh in unit.get_lod_meshes(level):
			mesh.layers = layers_for(String(mesh.name))


## Survey buoys (4 round the slot) or the moorings' pick-up buoys (8 for OF1, 11 for Havfarm).
func _place_buoys(kind: String) -> void:
	var xforms: Array[Transform3D] = []
	var tints: Array = []
	var basis := Basis.IDENTITY.scaled(Vector3.ONE * BUOY_SCALE)
	if kind == "survey":
		for k in 4:
			var a := PI * 0.25 + k * PI * 0.5
			xforms.append(Transform3D(basis, station + Vector3(cos(a), 0.0, sin(a)) * SURVEY_RADIUS_M))
	elif kind == "pickup":
		if str(view.get("variant", "")) == "havfarm":
			for item in unit.get_mooring_spread():
				var a := deg_to_rad(float(item["bearing_deg"]))
				xforms.append(Transform3D(basis, station + Vector3(cos(a), 0.0, sin(a)) * PICKUP_RADIUS_M))
		else:
			for k in 4:
				for side: float in [-1.0, 1.0]:
					var a := PI * 0.25 + k * PI * 0.5 + deg_to_rad(PICKUP_PAIR_DEG) * side
					xforms.append(Transform3D(basis, station + Vector3(cos(a), 0.0, sin(a)) * PICKUP_RADIUS_M))
	for i in xforms.size():
		tints.append(MooringGrid.BUOY_YELLOW.srgb_to_linear())
	MultiMeshPack.upload(buoys.multimesh, xforms, tints)
	buoys.visible = not xforms.is_empty()


func _build_area() -> void:
	_area = Area3D.new()
	_area.name = "UnitClickArea"
	var shape := CollisionShape3D.new()
	var cyl := CylinderShape3D.new()
	cyl.radius = unit.circumradius_m + 6.0
	cyl.height = unit.air_draught_m + 4.0
	shape.shape = cyl
	shape.position = Vector3(0.0, cyl.height * 0.5 - 2.0, 0.0)
	_area.add_child(shape)
	unit.add_child(_area)
	_area.input_event.connect(func(_c: Node, event: InputEvent, at: Vector3, _n: Vector3, _i: int) -> void:
		if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
			clicked.emit(at))


## The phase key a row stands in ("operational" once built).
static func phase_key_of(row: Dictionary) -> String:
	if bool(row.get("operational", false)):
		return "operational"
	var key := str((row.get("build", {}) as Dictionary).get("phase_key", row.get("phase_key", "")))
	return key if key in PHASES else "operational"


## The unit's construction phase (spec §6.3) and operation.
func apply_state(unit_view: Dictionary) -> void:
	view = unit_view
	var build: Dictionary = view.get("build", {})
	var key := phase_key_of(view)
	var p := clampf(float(build.get("phase_progress", view.get("phase_progress", 0.0))), 0.0, 1.0)
	var waiting := str(build.get("status", "")) == "waiting"
	_phase_key = key
	var at := PHASES.find(key) if key != "operational" else PHASES.size()
	unit.visible = at >= PHASES.find("voyage")
	var offset := 0.0
	if key == "voyage":
		offset = lerpf(VOYAGE_FROM_M, TOW_FROM_M, p)
	elif key == "tow":
		offset = TOW_FROM_M if waiting else lerpf(TOW_FROM_M, 0.0, p)
	# Moved only when its place changes (the heave and roll are _process's).
	if not is_equal_approx(offset, _offset):
		_offset = offset
		unit.position = station + APPROACH_DIR * offset
		unit.rotation = Vector3(0.0, heading, 0.0)
	if not is_equal_approx(unit.get_build_stage(), 6.0):
		unit.set_build_stage(6.0)
	var fleet_key := key
	if key == "voyage" or key == "tow":
		fleet_key += ":%d:%s" % [int(offset / 25.0), waiting]
	if fleet_key != _fleet_key:
		_fleet_key = fleet_key
		_place_fleet(key)
		_place_buoys("survey" if key == "yard_contract" else ("pickup" if key in ["hull", "voyage", "tow"] else ""))


func _place_fleet(key: String) -> void:
	for child in fleet.get_children():
		fleet.remove_child(child)
		child.queue_free()
	match key:
		"voyage":
			_tow_line(1)
		"tow":
			_tow_line(3)
		"mooring":
			for k in 3:
				var a := -PI * 0.5 + (k - 1) * 2.1
				var t := _vessel("anchor_tug", "Tug%d" % k)
				t.position = station + Vector3(cos(a), 0.0, sin(a)) * MOORING_TUG_M
				t.look_at(station, Vector3.UP)
		"commissioning":
			_moor_crew_boat()
		"operational":
			_moor_service_vessel()


## Tugs ahead of the unit on its way in (bows toward the station).
func _tow_line(count: int) -> void:
	for k in count:
		var t := _vessel("anchor_tug", "Tug%d" % k)
		var lateral := Vector3(1.0, 0.0, 0.0) * (k - (count - 1) * 0.5) * 60.0
		var at := unit.position - APPROACH_DIR * (unit.circumradius_m + 160.0) + lateral
		t.position = Vector3(at.x, 0.0, at.z)
		t.look_at(t.position - APPROACH_DIR, Vector3.UP)


## The service vessel alongside the walk-to-work landing, bow along the ring
## (its 17 m beam 6 m off the columns, the gangway spanning the gap).
func _moor_service_vessel() -> void:
	var landing := unit.position + Basis(Vector3.UP, heading) * unit.get_w2w_landing()
	var out := landing - unit.position
	out.y = 0.0
	out = out.normalized()
	var along := out.cross(Vector3.UP)
	var service := _vessel("offshore_service", "ServiceVessel")
	service.position = Vector3(landing.x, 0.0, landing.z) + out * (6.0 + 8.5) + along * 6.0
	service.look_at(service.position - along, Vector3.UP)


## The crew boat nosed into the boat landing (commissioning).
func _moor_crew_boat() -> void:
	var landing := unit.position + Basis(Vector3.UP, heading) * Vector3(-unit.circumradius_m, 0.0, 0.0)
	var out := (landing - unit.position).normalized()
	var crew := _vessel("crew_boat", "CrewBoat")
	crew.position = landing + out * 15.0
	crew.look_at(crew.position - out, Vector3.UP)


func _vessel(asset: String, node_name: String) -> Node3D:
	var v: Node3D = null
	if ResourceLoader.exists(FLEET[asset]):
		v = (load(FLEET[asset]) as PackedScene).instantiate() as Node3D
	else:
		v = Node3D.new()
	v.name = node_name
	fleet.add_child(v)
	return v


## The phase drawn now (a PHASES key or "operational").
func get_phase_key() -> String:
	return _phase_key


## Vessels of the fleet drawn now, by asset.
func fleet_assets() -> Array[String]:
	var out: Array[String] = []
	for child in fleet.get_children():
		out.append(str(child.get("asset_id")) if child.get("asset_id") != null else String(child.name))
	return out


## Buoys drawn at the slot now.
func buoy_count() -> int:
	return buoys.multimesh.instance_count if buoys.visible else 0


## The unit's draw nodes with their LOD band and whether they cast a shadow
## (spec §5: LOD2 casts none, nor does a unit from LOD1 on): [{name, band, casts}].
func shadow_report() -> Array:
	var out: Array = []
	for level in 3:
		for mesh in unit.get_lod_meshes(level):
			out.append({"name": String(mesh.name), "band": level, "kind": "unit",
				"casts": mesh.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_OFF})
	return out


## Where the camera frames the unit (its station at the deck's height).
func focus_point() -> Vector3:
	return global_transform * (station + Vector3(0.0, 9.0, 0.0))


## F2-21: the engine's sea at the unit (SimBridge.get_sea_state_forecast).
func set_sea(forecast: Dictionary) -> void:
	sea = forecast
	_refresh_chip(_camera_distance())


## The chip's status: 0 a standard vessel can work today, 1 only a DP
## vessel, 2 none (the forecast's own limits).
func sea_status() -> int:
	var today: Dictionary = sea.get("today", {})
	var limits: Dictionary = sea.get("limits", {})
	var hs := float(today.get("hs", 0.0))
	if hs <= float(limits.get(ROW_STANDARD, INF)):
		return 0
	if hs <= float(limits.get(ROW_DP, INF)):
		return 1
	return 2


## The chip's text: the sea today, tomorrow's median and, when no fish can be
## handled today, the next handling window; `short` the Hs only.
func chip_text(short: bool = false) -> String:
	var today: Dictionary = sea.get("today", {})
	var hs := SimFormatters.num(float(today.get("hs", 0.0)), 1)
	if short:
		return tr("FACILITY_CHIP_SEA_SHORT") % hs
	var days: Array = sea.get("days", [])
	var tomorrow := SimFormatters.num(float((days[0] as Dictionary).get("hs_p50", 0.0)), 1) if not days.is_empty() else "-"
	var text: String = tr("FACILITY_CHIP_SEA") % [hs, SimFormatters.num(float(today.get("tp", 0.0)), 0), tomorrow]
	var limits: Dictionary = sea.get("limits", {})
	if float(today.get("hs", 0.0)) > float(limits.get(ROW_HANDLING, INF)):
		var lead := -1
		for day: Dictionary in days:
			if float((day.get("p_ok", {}) as Dictionary).get(ROW_HANDLING, 0.0)) >= WINDOW_P:
				lead = int(day.get("lead", -1))
				break
		text += " · " + (tr("FACILITY_CHIP_WINDOW_IN") % lead if lead > 0 else tr("FACILITY_CHIP_WINDOW_NONE") % days.size())
	return text


## True when the chip should show at camera distance `distance` (spec §5.5
## rule 2 for the far case).
func chip_wanted(distance: float) -> bool:
	if sea.is_empty() or not bool(sea.get("ok", false)):
		return false
	if distance <= CHIP_NEAR_M:
		return true
	if _director == null and is_inside_tree():
		_director = get_tree().get_first_node_in_group(RenderQuality.DIRECTOR_GROUP)
	if _director == null:
		return false
	if float(_director.call("transmittance", distance)) < CHIP_FOG_TRANSMITTANCE:
		return true
	var weather: Node = _director.get("weather_system")
	if weather != null and weather.has_method("get_sea_fog") and float(weather.call("get_sea_fog")) >= CHIP_SEA_FOG:
		return distance > float(weather.call("get_visibility_m"))
	return false


func _refresh_chip(distance: float) -> void:
	if chip == null:
		return
	var wanted := chip_wanted(distance) and not chip_suppressed
	chip.visible = wanted
	if not wanted:
		return
	var short := distance > CHIP_NEAR_M
	var status := sea_status()
	# The text is built only when the sea, the status or the near/far form changes.
	var key := "%s|%s|%d|%s" % [sea.get("issued_day", -1), (sea.get("today", {}) as Dictionary).get("hs", 0.0), status, short]
	if key != _chip_key:
		_chip_key = key
		chip.set_chip(chip_text(short), STATUS_COLORS[status], status as VesselChip.Shape)
	var on_station := unit != null and unit.visible
	chip.position = (unit.position + unit.get_chip_anchor()) if on_station else station + Vector3(0.0, unit.air_draught_m + 8.0, 0.0)


func _camera_distance() -> float:
	if not is_inside_tree():
		return INF
	var cam := get_viewport().get_camera_3d()
	return cam.global_position.distance_to(global_transform * station) if cam != null else INF


func _process(_delta: float) -> void:
	_refresh_chip(_camera_distance())
	var field := WaveField.active
	if field == null or unit == null or not unit.visible:
		return
	var rest := Vector3(unit.position.x, 0.0, unit.position.z)
	var r := unit.circumradius_m
	# The unit's own axes on the water: +Z along, +X across, turned by its heading.
	var along := Vector2(sin(heading), cos(heading))
	var across := Vector2(cos(heading), -sin(heading))
	var plane := field.hull(rest.x, rest.z, along, across, r, r)
	unit.position.y = plane.x * HEAVE_GAIN
	unit.rotation = Vector3(-atan(plane.y) * TILT_GAIN, heading, atan(plane.z) * TILT_GAIN)
