class_name VesselWakes
extends Node3D
## Wakes behind moving boats (G2-03): the white wash of the hull and
## propeller down the track, and the two arms of the Kelvin wedge spreading
## at 19.47 degrees either side of it.
##
## Every boat in the fjord is a VesselModel moved by its owner (a
## PathFollow3D along ChannelBoat's route, a tween). This node finds them
## (a scan every SCAN_INTERVAL_S), follows their stern, and every segment
## of track lays a point: where, which way is sideways, when, how fast. A
## boat's points make one mesh of three ribbons (the wash, the left arm,
## the right arm) drawn with wake_shader.gdshader, which widens and fades
## them by their age; the mesh is rebuilt only when a point is laid or a
## faded one dropped. The ribbons float LIFT over the still water, above a
## calm sea's ripples; a rougher sea hides wakes in its own foam, so they
## fade with the whitecap cover (sea_visibility). The planar mirror leaves
## them out.

const WAKE_SHADER := preload("res://shaders/water/wake_shader.gdshader")
const FOAM_TEXTURE := preload("res://assets/textures/water_foam.png")

## Seconds between scans for boats (new, gone).
const SCAN_INTERVAL_S := 0.5
## Below this speed (m/s) a boat leaves no new wake.
const MIN_SPEED_MS := 0.6
## The speed (m/s) at which a wake is at its full strength.
const FULL_SPEED_MS := 5.0
## Mirrored in wake_shader.gdshader: the Kelvin wedge's half-angle (tan
## 19.47 deg), the wash's life and spread, the arms' fade and strength.
const KELVIN_TAN := 0.35355
const FOAM_LIFE_S := 24.0
const FOAM_SPREAD := 0.45
const KELVIN_FADE_M := 200.0
const KELVIN_ALPHA := 0.3
## A point is dropped once both its ribbons have faded below this.
const MIN_ALPHA := 0.015
## Track between points, as a share of the beam, kept between these (m).
const SEGMENT_BEAMS := 0.9
const SEGMENT_MIN_M := 4.0
const SEGMENT_MAX_M := 14.0
## Most points per boat (a long run drops its oldest first).
const MAX_POINTS := 90
## Kinds of ribbon (the shader's UV.y).
const KIND_WASH := 0
const KIND_LEFT_ARM := 1
const KIND_RIGHT_ARM := 2

## The director whose sea state (whitecap cover) fades the wakes.
@export var director: EnvironmentDirector

## Wake strength from the sea state (1 calm, 0 in a gale's own foam).
var sea_visibility := 1.0
## Boats followed: instance id -> {boat, last, drop, speed, along}.
var tracked := {}
## Trails: instance id -> {points: Array[Dictionary], mesh: MeshInstance3D}
## (a trail outlives its boat until it has faded).
var trails := {}

var material := ShaderMaterial.new()
var _scan_left := 0.0
var _time := 0.0


func _ready() -> void:
	# Ribbons are built in world space: the node itself never moves.
	top_level = true
	global_transform = Transform3D.IDENTITY
	material.shader = WAKE_SHADER
	material.set_shader_parameter("foam_texture", FOAM_TEXTURE)


## The sea's whitecap cover (Monahan fraction): wakes fade out as it grows.
func set_sea_state(whitecaps: float) -> void:
	sea_visibility = 1.0 - smoothstep(0.004, 0.04, whitecaps)


func _process(delta: float) -> void:
	_time += delta
	_scan_left -= delta
	if _scan_left <= 0.0:
		_scan_left = SCAN_INTERVAL_S
		_scan()
		if director != null and director.sea_state.has("whitecaps"):
			set_sea_state(float(director.sea_state["whitecaps"]))
		_drop_faded()
	for id in tracked.keys():
		var entry: Dictionary = tracked[id]
		# Untyped: a boat its owner freed is a freed instance until erased here.
		var boat: Variant = entry["boat"]
		if not is_instance_valid(boat) or not (boat as Node).is_inside_tree():
			tracked.erase(id)
			continue
		_follow(id, entry, delta)
	material.set_shader_parameter("now", _time)
	material.set_shader_parameter("sea_visibility", sea_visibility)


## Every VesselModel under this node's parent (the fjord environment),
## scanned now and then: boats come and go with jobs. A boat carried by
## another (the barge's tender) is part of its carrier.
func _scan() -> void:
	var root := get_parent()
	if root == null:
		return
	var seen := {}
	for node in root.find_children("*", "Node3D", true, false):
		if node is VesselModel and not ((node as VesselModel).get_parent() is VesselModel):
			var boat := node as VesselModel
			var id := boat.get_instance_id()
			seen[id] = true
			if not tracked.has(id):
				var stern := stern_of(boat)
				tracked[id] = {"boat": boat, "last": stern, "drop": stern, "speed": 0.0, "along": 0.0}
	for id in tracked.keys():
		if not seen.has(id):
			tracked.erase(id)


## The middle of a boat's stern at the waterline (its calm-water pose: the
## bow points along -Z).
static func stern_of(boat: VesselModel) -> Vector3:
	var rest := boat.rest_transform()
	var p := rest.origin + rest.basis.z.normalized() * boat.length_m * 0.5
	return Vector3(p.x, 0.0, p.z)


## A point's strongest ribbon now (the shader's fade, mirrored).
static func point_alpha(point: Dictionary, now: float) -> float:
	var age := maxf(now - float(point["t"]), 0.0)
	var behind := float(point["speed"]) * age
	var strength: float = point["strength"]
	var wash := strength * exp(-age / FOAM_LIFE_S)
	var arms := strength * KELVIN_ALPHA * exp(-behind / KELVIN_FADE_M) * smoothstep(0.0, 8.0, behind)
	return maxf(wash, arms)


## Half-width (m) the wedge spans a point's age after the boat passed it.
static func wedge_half_width(point: Dictionary, now: float) -> float:
	var behind := float(point["speed"]) * maxf(now - float(point["t"]), 0.0)
	return 0.5 * float(point["beam"]) + KELVIN_TAN * behind


func _follow(id: int, entry: Dictionary, delta: float) -> void:
	var boat: VesselModel = entry["boat"]
	var stern := stern_of(boat)
	var last: Vector3 = entry["last"]
	var moved := stern.distance_to(last)
	entry["last"] = stern
	if delta > 0.0:
		# Smoothed speed (m/s): a boat that jumps (a new route) leaves no wake.
		var raw := moved / delta if moved < 50.0 else 0.0
		entry["speed"] = lerpf(float(entry["speed"]), raw, 1.0 - exp(-4.0 * delta))
	var speed: float = entry["speed"]
	var drop: Vector3 = entry["drop"]
	var step := clampf(boat.beam_m * SEGMENT_BEAMS, SEGMENT_MIN_M, SEGMENT_MAX_M)
	var run := stern.distance_to(drop)
	if run > 3.0 * SEGMENT_MAX_M + 50.0:
		# A jump: start a new trail line here.
		entry["drop"] = stern
		_lay_point(id, stern, stern - drop, boat, 0.0, true)
		return
	if run < step:
		return
	entry["drop"] = stern
	if speed < MIN_SPEED_MS:
		return
	entry["along"] = float(entry["along"]) + run
	_lay_point(id, stern, stern - drop, boat, speed, false)


func _lay_point(id: int, at: Vector3, heading: Vector3, boat: VesselModel, speed: float, break_line: bool) -> void:
	var dir := Vector3(heading.x, 0.0, heading.z)
	if dir.length() < 1e-3:
		return
	dir = dir.normalized()
	var trail: Dictionary = trails.get(id, {})
	if trail.is_empty():
		var mesh := MeshInstance3D.new()
		mesh.name = "Wake_%d" % id
		mesh.material_override = material
		mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		mesh.layers = PlanarReflection.NO_MIRROR_LAYER
		add_child(mesh)
		trail = {"points": [] as Array[Dictionary], "mesh": mesh}
		trails[id] = trail
	var points: Array[Dictionary] = trail["points"]
	var along: float = (tracked[id]["along"] if tracked.has(id) else 0.0)
	points.append({
		"p": at, "right": Vector3.UP.cross(dir).normalized() * -1.0, "t": _time, "speed": speed,
		"beam": boat.beam_m, "strength": smoothstep(MIN_SPEED_MS, FULL_SPEED_MS, speed) if not break_line else 0.0,
		"along": along, "break": break_line,
	})
	while points.size() > MAX_POINTS:
		points.pop_front()
	_rebuild(trail)


func _drop_faded() -> void:
	for id in trails.keys():
		var trail: Dictionary = trails[id]
		var points: Array[Dictionary] = trail["points"]
		var before := points.size()
		# Keep one faded point ahead of the first live one, so its quad stays.
		while points.size() >= 2 and point_alpha(points[0], _time) < MIN_ALPHA and point_alpha(points[1], _time) < MIN_ALPHA:
			points.pop_front()
		var alive := false
		for point in points:
			if point_alpha(point, _time) >= MIN_ALPHA:
				alive = true
				break
		if not alive and not tracked.has(id):
			(trail["mesh"] as MeshInstance3D).queue_free()
			trails.erase(id)
		elif points.size() != before:
			_rebuild(trail)


## The trail's mesh: for each pair of neighbouring points (not across a
## break), a quad per ribbon from its inner to its outer edge.
func _rebuild(trail: Dictionary) -> void:
	var points: Array[Dictionary] = trail["points"]
	var mesh_node: MeshInstance3D = trail["mesh"]
	var verts := PackedVector3Array()
	var normals := PackedVector3Array()
	var uvs := PackedVector2Array()
	var uv2s := PackedVector2Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()
	var reach := 0.0
	for i in range(points.size() - 1):
		var a: Dictionary = points[i]
		var b: Dictionary = points[i + 1]
		if bool(b["break"]):
			continue
		reach = maxf(reach, wedge_half_width(a, _time + 120.0))
		for kind in [KIND_WASH, KIND_LEFT_ARM, KIND_RIGHT_ARM]:
			var base := verts.size()
			for point: Dictionary in [a, b]:
				for edge in [-1.0, 1.0]:
					verts.append(point["p"])
					normals.append(point["right"])
					uvs.append(Vector2(edge, float(kind)))
					uv2s.append(Vector2(point["t"], point["speed"]))
					colors.append(Color(float(point["beam"]) / 100.0, point["strength"], float(point["along"]) / 1000.0, 1.0))
			indices.append_array([base, base + 1, base + 2, base + 1, base + 3, base + 2])
	if indices.is_empty():
		mesh_node.mesh = null
		return
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_TEX_UV2] = uv2s
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	# The shader moves the ribbons sideways by up to the wedge's width.
	var box := mesh.get_aabb()
	mesh.custom_aabb = box.grow(reach + 20.0)
	mesh_node.mesh = mesh
