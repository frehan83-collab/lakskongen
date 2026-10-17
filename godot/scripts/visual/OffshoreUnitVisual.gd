class_name OffshoreUnitVisual
extends Node3D
## One offshore unit in the 3D sea (roadmap F2-20; farm-types spec §4, §5.2,
## §5.4, §6.3): the OF1-class semi-submersible built by
## blender/build_offshore.py (OffshoreUnitModel, of1_class.tscn) on its slot,
## with the offshore fleet (blender/build_vessels.py) for its construction
## phase, all from the ENGINE's view of the unit (SimBridge.get_facility_visuals).
##
## Phases (spec §6.3 "Offshore phase"; FacilityConstructionPhases.Offshore):
## - yard: the unit is at the yard (the picture-in-picture is UI): four
##   yellow survey buoys mark the slot;
## - voyage: the finished unit approaches from the open sea on the horizon,
##   behind one tug;
## - tow: three anchor tugs bring it the last 600 m;
## - mooring: on station, the three tugs at the anchor bearings;
## - commissioning: on station, the crew boat at the boat landing, the nav
##   lanterns lit;
## - operational: the orange DP service vessel alongside its walk-to-work
##   landing.
## The unit heaves and rolls a little on the drawn water (WaveField.hull over
## its 110 m, damped: a semi-submersible at a 43 m draught barely moves).
##
## Mirror pass (G2-03): the net hangs under the water, so it lives on
## PlanarReflection.NO_MIRROR_LAYER at every LOD (layers_for(), PenCage's
## rule); the solid frame lays its submerged part on the water plane in
## offshore_kit.gdshader. Picking: one Area3D cylinder round the ring.

signal clicked(at: Vector3)

const UNIT_SCENE := preload("res://assets/models/offshore/of1_class.tscn")
const FLEET := {
	"offshore_service": "res://assets/models/offshore_service/offshore_service.tscn",
	"anchor_tug": "res://assets/models/offshore_service/anchor_tug.tscn",
	"crew_boat": "res://assets/models/crew_boat/crew_boat.tscn",
}
const BUOY_MESH := "res://assets/models/site_kit/mooring_buoy_LOD0.glb"
const BUOY_MATERIAL := preload("res://assets/models/site_kit/mooring_buoy_material.tres")
## Parts that hang under the water (PenCage.NO_MIRROR_PARTS' rule).
const NO_MIRROR_PARTS := ["Net"]
## Where the unit comes from on its voyage: the open sea, away from the coast (-Z).
const APPROACH_DIR := Vector3(0.0, 0.0, -1.0)
## Voyage: from this far out to the tow's start; the tow brings it in (m).
const VOYAGE_FROM_M := 2600.0
const TOW_FROM_M := 600.0
## Anchor tugs stand this far off on station during the mooring (m).
const MOORING_TUG_M := 260.0
## Survey buoys round the slot (m from its centre).
const SURVEY_RADIUS_M := 70.0
## Heave and roll on the drawn water, damped for a semi-submersible.
const HEAVE_GAIN := 0.3
const TILT_GAIN := 0.25

var view: Dictionary = {}
var unit: OffshoreUnitModel = null
var fleet: Node3D = null
var survey: MultiMeshInstance3D = null
var station := Vector3.ZERO
var _area: Area3D = null
var _phase := -1
var _fleet_key := ""


## The render layers an offshore part is drawn on: what hangs under the
## water stays out of the planar mirror's pass.
static func layers_for(part: String) -> int:
	return PlanarReflection.NO_MIRROR_LAYER if part in NO_MIRROR_PARTS else 1


func build(unit_view: Dictionary) -> void:
	view = unit_view
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
	_build_survey()
	_build_area()
	apply_state(unit_view)


func _build_survey() -> void:
	var scene := load(BUOY_MESH) as PackedScene
	if scene == null:
		return
	var root := scene.instantiate()
	var mi := root.find_children("*", "MeshInstance3D", true, false)[0] as MeshInstance3D
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_colors = true
	mm.mesh = mi.mesh
	root.free()
	var xforms: Array[Transform3D] = []
	var tints: Array = []
	for k in 4:
		var a := PI * 0.25 + k * PI * 0.5
		xforms.append(Transform3D(Basis.IDENTITY.scaled(Vector3.ONE * 2.0), station + Vector3(cos(a), 0.0, sin(a)) * SURVEY_RADIUS_M))
		tints.append(MooringGrid.BUOY_YELLOW.srgb_to_linear())
	MultiMeshPack.upload(mm, xforms, tints)
	survey = MultiMeshInstance3D.new()
	survey.name = "SurveyBuoys"
	survey.multimesh = mm
	survey.material_override = BUOY_MATERIAL
	survey.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(survey)


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


## The unit's construction phase (spec §6.3) and operation.
func apply_state(unit_view: Dictionary) -> void:
	view = unit_view
	var phase := int(view.get("phase_index", 0))
	var p := clampf(float(view.get("phase_progress", 0.0)), 0.0, 1.0)
	if bool(view.get("operational", false)):
		phase = int(view.get("phase_count", 5))
	_phase = phase
	var at_station := phase >= 3
	unit.visible = phase >= 1
	survey.visible = phase == 0
	var offset := 0.0
	if phase == 1:
		offset = lerpf(VOYAGE_FROM_M, TOW_FROM_M, p)
	elif phase == 2:
		offset = lerpf(TOW_FROM_M, 0.0, p)
	unit.position = station + APPROACH_DIR * offset
	unit.rotation = Vector3.ZERO
	# The nav lanterns are lit from commissioning on (the material's own
	# nav_lights carries the night; the unit is dark before it is handed over).
	unit.set_build_stage(6.0 if phase >= 1 else 0.0)
	var key := "%d" % phase
	if phase == 1 or phase == 2:
		key += ":%d" % int(offset / 25.0)
	if key != _fleet_key:
		_fleet_key = key
		_place_fleet(phase, at_station)


func _place_fleet(phase: int, at_station: bool) -> void:
	for child in fleet.get_children():
		fleet.remove_child(child)
		child.queue_free()
	match phase:
		1:
			_tow_line(1)
		2:
			_tow_line(3)
		3:
			for k in 3:
				var a := -PI * 0.5 + (k - 1) * 2.1
				var t := _vessel("anchor_tug", "Tug%d" % k)
				t.global_position = station + Vector3(cos(a), 0.0, sin(a)) * MOORING_TUG_M
				t.look_at(station, Vector3.UP)
		4:
			_moor_crew_boat()
		_:
			if at_station and phase >= 5:
				_moor_service_vessel()


## Tugs ahead of the unit on its way in (bows toward the station).
func _tow_line(count: int) -> void:
	for k in count:
		var t := _vessel("anchor_tug", "Tug%d" % k)
		var lateral := Vector3(1.0, 0.0, 0.0) * (k - (count - 1) * 0.5) * 60.0
		t.global_position = unit.global_position - APPROACH_DIR * (unit.circumradius_m + 160.0) + lateral
		t.global_position.y = 0.0
		t.look_at(t.global_position - APPROACH_DIR, Vector3.UP)


## The service vessel alongside the walk-to-work landing, bow along the ring
## (its 17 m beam 6 m off the columns, the gangway spanning the gap).
func _moor_service_vessel() -> void:
	var landing := unit.to_global(unit.get_w2w_landing())
	var out := landing - unit.global_position
	out.y = 0.0
	out = out.normalized()
	var along := out.cross(Vector3.UP)
	var service := _vessel("offshore_service", "ServiceVessel")
	service.global_position = Vector3(landing.x, 0.0, landing.z) + out * (6.0 + 8.5) + along * 6.0
	service.look_at(service.global_position - along, Vector3.UP)


## The crew boat nosed into the boat landing (commissioning).
func _moor_crew_boat() -> void:
	var landing := unit.global_position + Vector3(-unit.circumradius_m, 0.0, 0.0)
	var out := (landing - unit.global_position).normalized()
	var crew := _vessel("crew_boat", "CrewBoat")
	crew.global_position = landing + out * 15.0
	crew.look_at(crew.global_position - out, Vector3.UP)


func _vessel(asset: String, node_name: String) -> Node3D:
	var v: Node3D = null
	if ResourceLoader.exists(FLEET[asset]):
		v = (load(FLEET[asset]) as PackedScene).instantiate() as Node3D
	else:
		v = Node3D.new()
	v.name = node_name
	fleet.add_child(v)
	return v


## The phase drawn now (0 yard ... 5 operational).
func get_phase() -> int:
	return _phase


## Vessels of the fleet drawn now, by asset.
func fleet_assets() -> Array[String]:
	var out: Array[String] = []
	for child in fleet.get_children():
		out.append(str(child.get("asset_id")) if child.get("asset_id") != null else String(child.name))
	return out


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
	return station + Vector3(0.0, 9.0, 0.0)


func _process(_delta: float) -> void:
	var field := WaveField.active
	if field == null or unit == null or not unit.visible:
		return
	var rest := Vector3(unit.position.x, 0.0, unit.position.z)
	var r := unit.circumradius_m
	var plane := field.hull(rest.x, rest.z, Vector2(0.0, 1.0), Vector2(1.0, 0.0), r, r)
	unit.position.y = plane.x * HEAVE_GAIN
	unit.rotation = Vector3(-atan(plane.y) * TILT_GAIN, 0.0, atan(plane.z) * TILT_GAIN)
