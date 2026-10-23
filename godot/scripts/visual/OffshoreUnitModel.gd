class_name OffshoreUnitModel
extends Node3D

## An offshore farm unit built by blender/build_offshore.py (farm-types spec
## §5.2): the OF1-class semi-submersible (F2-26), a safety-yellow 12-gon
## 110 m across that floats at its 43 m draft with ~25 m of frame and
## topsides above the water.
##
## The scene inherits <asset>_LOD0.glb, so the LOD0 nodes are direct
## children: Hull, Columns, Bracing, Topsides, Net, Bulkhead, Cranes (with
## CraneA and CraneB on their slew axes), NavLights, and the markers Beacon
## (mast top) and W2WLanding (where a service vessel's gangway rests).
## LOD1 and LOD2 hold Hull and Net only; visibility ranges switch them by
## camera distance (spec §5.5 rule 4: units LOD0 below 500 m, LOD1 below
## 2,000 m). Only LOD0 casts shadows, and never the net or the lanterns.
## The origin is the unit's axis at the design waterline.
##
## Every solid uses LK_OffshoreKit (offshore_kit.gdshader); the net and the
## sliding bulkhead's net use LK_OffshoreNet (pen_net_shader). Unit state is
## never painted on the hull: status goes on an Ink chip (spec §5.5 rule 6).

## Which build_offshore.py asset this scene shows.
@export var asset_id: String = "of1_class"
## Column-centre circumradius, draft and structural height (m, measured).
@export var circumradius_m: float = 55.0
@export var draft_m: float = 43.0
@export var height_m: float = 68.4
## Walkway deck height above the waterline, and the mast top.
@export var deck_y_m: float = 15.4
@export var air_draught_m: float = 28.8
## Mooring spread (spec §5.2, F2-29): 8 lines at 1,100 m (OF1), 11 at 650 m (Havfarm).
@export var mooring_lines: int = 8
@export var mooring_anchor_radius_m: float = 1100.0
@export var mooring_fairlead_y_m: float = -30.0
## Camera distance (m) where LOD0 hands over to LOD1, and LOD1 to LOD2.
@export var lod_switch: Vector2 = Vector2(500.0, 2000.0)
## Past this camera distance (m) the unit is not drawn at all (0: always).
@export var max_distance: float = 0.0

const LOD_NAMES := ["", "LOD1", "LOD2"]
const LOD_HYSTERESIS := 8.0
const MOORING_LINE_LOD0 := "res://assets/models/offshore/mooring_line_kit_LOD0.glb"
const MOORING_LINE_LOD1 := "res://assets/models/offshore/mooring_line_kit_LOD1.glb"
const MOORING_REF_RADIUS_M := 1100.0
const OF1_MOORING_BEARINGS_DEG := [38.0, 52.0, 128.0, 142.0, 218.0, 232.0, 308.0, 322.0]
const HAVFARM_MOORING_BEARINGS_DEG := [128.0, 140.0, 152.0, 164.0, 174.0, 180.0, 186.0, 196.0, 208.0, 220.0, 232.0]
const HAVFARM_TURRET_X_M := -166.0
## The build part groups in yard order (offshore_kit.gdshader, UV2.x).
const PART_GROUPS := ["Pontoons", "Columns", "Rings", "Bracing", "Topsides", "Net"]
## LOD0 mesh nodes (spec §5.2), and those that never cast a shadow.
const MESH_NODES := ["Hull", "Columns", "Bracing", "Topsides", "Net", "Bulkhead", "CraneA", "CraneB", "NavLights"]
const NO_SHADOW := ["Net", "NavLights"]
## Nodes that go in with the nets (the last yard stage).
const NET_STAGE_NODES := ["Net", "Bulkhead"]

var _build_stage := 6.0
var _crane_rest := {}
var _bulkhead_rest := 0.0


func _ready() -> void:
	_configure_lods()
	for crane in ["CraneA", "CraneB"]:
		var node := get_node_or_null("Cranes/" + crane) as Node3D
		if node:
			_crane_rest[crane] = node.rotation.y
	var bulkhead := get_node_or_null("Bulkhead") as Node3D
	if bulkhead:
		_bulkhead_rest = bulkhead.rotation.y


## The meshes of one LOD level (0 = the inherited LOD0 nodes).
func get_lod_meshes(level: int) -> Array[MeshInstance3D]:
	var meshes: Array[MeshInstance3D] = []
	var holder: Node = self if level == 0 else get_node_or_null(LOD_NAMES[level])
	if holder == null:
		return meshes
	for node in holder.find_children("*", "MeshInstance3D", true, false):
		if level == 0 and _in_lod_holder(node):
			continue
		meshes.append(node as MeshInstance3D)
	return meshes


func _in_lod_holder(node: Node) -> bool:
	var p := node.get_parent()
	while p != null and p != self:
		if String(p.name) in ["LOD1", "LOD2", "MooringSpread"]:
			return true
		p = p.get_parent()
	return false


## Reveal the build part groups in yard order for the yard picture-in-
## picture and the First Sight timelapse (spec §5.3): `stage` 0..6, groups
## below floor(stage) complete, the current one rising, 6 the finished unit.
func set_build_stage(stage: float) -> void:
	_build_stage = clampf(stage, 0.0, float(PART_GROUPS.size()))
	for level in LOD_NAMES.size():
		for mesh in get_lod_meshes(level):
			mesh.set_instance_shader_parameter("build_stage", _build_stage)
			if String(mesh.name) in NET_STAGE_NODES:
				mesh.visible = _build_stage > float(PART_GROUPS.size() - 1)


func get_build_stage() -> float:
	return _build_stage


## Slide the bulkhead round the unit's axis (degrees from its rest).
func set_bulkhead_angle(degrees: float) -> void:
	var bulkhead := get_node_or_null("Bulkhead") as Node3D
	if bulkhead:
		bulkhead.rotation.y = _bulkhead_rest + deg_to_rad(degrees)


## Slew a crane ("CraneA" or "CraneB") about its pedestal (degrees from rest).
func slew_crane(crane: String, degrees: float) -> void:
	var node := get_node_or_null("Cranes/" + crane) as Node3D
	if node and _crane_rest.has(crane):
		node.rotation.y = float(_crane_rest[crane]) + deg_to_rad(degrees)


## Local position of the mast-top beacon (the night landmark).
func get_beacon_position() -> Vector3:
	var beacon := get_node_or_null("Beacon") as Node3D
	return beacon.position if beacon else Vector3(0.0, air_draught_m, 0.0)


## Local position where a service vessel's gangway rests on the ring.
func get_w2w_landing() -> Vector3:
	var landing := get_node_or_null("W2WLanding") as Node3D
	return landing.position if landing else Vector3(-circumradius_m, deck_y_m, 0.0)


## Where a status chip sits above the unit, in local space.
func get_chip_anchor() -> Vector3:
	return Vector3(0.0, air_draught_m + 8.0, 0.0)


## Mooring spread descriptor (spec §5.2, F2-29): 8 lines in 4 pairs at 1,100 m
## radius for OF1-class; 11 lines at 650 m radius from the bow turret for
## Havfarm-class.
func get_mooring_spread() -> Array[Dictionary]:
	var is_havfarm := asset_id == "havfarm_class" or mooring_lines == 11
	var bearings: Array = HAVFARM_MOORING_BEARINGS_DEG if is_havfarm else OF1_MOORING_BEARINGS_DEG
	var origin := Vector3(HAVFARM_TURRET_X_M, 0.0, 0.0) if is_havfarm else Vector3.ZERO
	var fairlead_r := 7.0 if is_havfarm else circumradius_m
	var out: Array[Dictionary] = []
	for deg in bearings:
		var rad := deg_to_rad(float(deg))
		var dir := Vector3(cos(rad), 0.0, -sin(rad))
		out.append({
			"bearing_deg": float(deg),
			"fairlead": origin + dir * fairlead_r + Vector3(0.0, mooring_fairlead_y_m, 0.0),
			"anchor": origin + dir * mooring_anchor_radius_m + Vector3(0.0, -150.0, 0.0),
			"radius_m": mooring_anchor_radius_m,
		})
	return out


## Build or toggle the underwater mooring-line instances (mooring_line_kit).
func set_mooring_visible(vis: bool) -> void:
	var holder := get_node_or_null("MooringSpread") as Node3D
	if holder == null and vis:
		holder = _build_mooring_spread()
	if holder != null:
		holder.visible = vis


func _build_mooring_spread() -> Node3D:
	var holder := Node3D.new()
	holder.name = "MooringSpread"
	add_child(holder)
	var scene0 := load(MOORING_LINE_LOD0) as PackedScene if ResourceLoader.exists(MOORING_LINE_LOD0) else null
	if scene0 == null:
		return holder
	var is_havfarm := asset_id == "havfarm_class" or mooring_lines == 11
	var origin := Vector3(HAVFARM_TURRET_X_M, 0.0, 0.0) if is_havfarm else Vector3.ZERO
	var r_scale := mooring_anchor_radius_m / MOORING_REF_RADIUS_M
	var spread := get_mooring_spread()
	for i in spread.size():
		var item: Dictionary = spread[i]
		var line := scene0.instantiate() as Node3D
		line.name = "MooringLine_%02d" % i
		line.position = origin
		line.rotation.y = deg_to_rad(float(item["bearing_deg"]))
		line.scale = Vector3(r_scale, 1.0, 1.0)
		for mesh in line.find_children("*", "MeshInstance3D", true, false):
			var mi := mesh as MeshInstance3D
			mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			mi.layers = PlanarReflection.NO_MIRROR_LAYER
		holder.add_child(line)
	return holder


func _configure_lods() -> void:
	for level in LOD_NAMES.size():
		var begin: float = 0.0 if level == 0 else lod_switch[level - 1]
		var end: float = lod_switch[level] if level < 2 else max_distance
		for mesh in get_lod_meshes(level):
			mesh.visibility_range_begin = begin
			mesh.visibility_range_end = end
			mesh.visibility_range_begin_margin = LOD_HYSTERESIS if begin > 0.0 else 0.0
			mesh.visibility_range_end_margin = LOD_HYSTERESIS if end > 0.0 else 0.0
			mesh.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_DISABLED
			var casts := level == 0 and not (String(mesh.name) in NO_SHADOW)
			mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON if casts \
				else GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
