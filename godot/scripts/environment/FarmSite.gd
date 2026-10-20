class_name FarmSite
extends Node3D
## One salmon site on the fjord (roadmap G2-04): a grid of round pens in a
## frame mooring (SiteLayout plans it), the white feed hoses from its feed
## barge to every pen, yellow floats, the site's name and a number on each
## pen. The player's sites (FishPenManager) and the rival farms
## (CompetitorFarmVisualManager) are the same FarmSite with different
## colours: rivals get an owner band on the collars, orange buoys, quieter
## nets and no health rings.
##
## Draw calls: every pen is a PenCage (its LOD0 meshes, health ring and click
## area), but past LOD0 the site draws all its pens through one MultiMesh
## per pen part and LOD (MultiMesh for repeated parts, roadmap P0-04): a pen
## hands over from its own LOD0 meshes to the site's LOD1 MultiMeshes at
## PenCage.LOD_SWITCH[0] and to the LOD2 ones at LOD_SWITCH[1], by its own
## distance, with PenCage.LOD_HYSTERESIS. Nothing past LOD0 casts a shadow.
## The grid, hoses and floats are MultiMeshes or one mesh each.
##
## Local frame: x across the channel, z along it, the grid centred on this
## node, y = 0 the waterline. The owner places and turns the node.

## A pen was clicked (its own click area or the site's, nearest pen).
signal pen_pressed(site: FarmSite, pen_index: int, at: Vector3)

const PEN_SCENE := preload("res://assets/models/pen_cage/pen_cage.tscn")
## The site's name floats this high over the grid centre (m), above the
## bird-net masts.
const LABEL_HEIGHT := 14.0
## Pen numbers float over each pen at FARM zoom only.
const PEN_NUMBER_HEIGHT := 9.5
const PEN_NUMBER_RANGE := 420.0
const PEN_NUMBER_COLOR := Color(0.788, 0.839, 0.855)  # light Ink secondary
## The site's click box covers the grid and this margin (m), and this deep.
const CLICK_MARGIN_M := 6.0
const CLICK_HEIGHT_M := 4.0
## Half-height of the site's culling box for its pen MultiMeshes: the nets
## hang 38 m, the masts stand 7.5 m (m).
const PEN_BOX_DEPTH_M := 45.0
const PEN_BOX_HEIGHT_M := 15.0
## Pen LOD states.
enum Lod { NEAR, MID, FAR }

var site_id := ""
var pen_count := 0
var dims := Vector2i.ONE
## Which side of the grid (site-frame x sign) the feed barge lies on.
var barge_side := 1.0
var cages: Array[PenCage] = []
var grid: MooringGrid = null
var hoses: FeedHoses = null
var label: Label3D = null
var click_area: Area3D = null
## Per pen: Lod.NEAR (its own LOD0 meshes), MID or FAR (the site's MultiMeshes).
var lod_state := PackedInt32Array()

var _pens_root: Node3D = null
var _rest: Array[Vector3] = []
var _lod_parts := {}   # level (1, 2) -> {part name: MultiMeshInstance3D}
var _lod_buffers := {}  # level -> last uploaded PackedFloat32Array
var _rides_uploaded := -1  # the pens' ride counts the uploaded poses saw
var _ghost := false
var _band := Color(0, 0, 0, 0)
var _net_opacity := 1.0
var _skirt := false
var _bird_net: int = PenCage.BirdNet.TENT
var _health_rings := true
static var _ghost_mat: StandardMaterial3D = null


func _init() -> void:
	# After the pens: they ride the waves in their own _process first.
	process_priority = 1


## Lay out `count` pens. The node must be in the tree and placed (the anchor
## lines look the seabed up in world space). Options: barge_side (+1/-1),
## buoy_color, anchor_floor (Callable(x, z) -> seabed y), band_color,
## net_opacity, health_rings (bool), pen_numbers (bool), bird_net
## (PenCage.BirdNet), label (text), label_color, label_range (m).
func build(id: String, count: int, options: Dictionary = {}) -> void:
	site_id = id
	pen_count = maxi(count, 1)
	dims = SiteLayout.grid_dims(pen_count)
	barge_side = float(options.get("barge_side", 1.0))
	_band = options.get("band_color", Color(0, 0, 0, 0))
	_net_opacity = float(options.get("net_opacity", 1.0))
	_health_rings = bool(options.get("health_rings", true))
	_bird_net = int(options.get("bird_net", PenCage.BirdNet.TENT))
	for child in get_children():
		child.free()
	cages.clear()
	_lod_parts.clear()
	_lod_buffers.clear()
	_pens_root = Node3D.new()
	_pens_root.name = "Pens"
	add_child(_pens_root)
	_rest = SiteLayout.pen_positions(pen_count)
	var positions := _rest
	var sides := SiteLayout.port_sides(pen_count, barge_side)
	for k in pen_count:
		var cage := PEN_SCENE.instantiate() as PenCage
		cage.name = "Pen%d" % (k + 1)
		_pens_root.add_child(cage)
		cage.position = positions[k]
		cage.rotation.y = SiteLayout.port_yaw(sides[k])
		cage.set_site_drawn(true)
		cage.set_owner_band(_band)
		cage.set_net_opacity(_net_opacity)
		cage.set_bird_net(_bird_net)
		cage.set_lice_skirt(_skirt)
		cage.show_health_ring(_health_rings)
		var area := cage.get_click_area()
		if area != null:
			area.input_event.connect(_on_input_event)
		# A one-pen site's name says it all: numbers from two pens up.
		if bool(options.get("pen_numbers", true)) and pen_count > 1:
			_add_pen_number(cage, k)
		cages.append(cage)
	lod_state.resize(pen_count)
	lod_state.fill(Lod.NEAR)
	grid = MooringGrid.new()
	grid.name = "Mooring"
	add_child(grid)
	grid.build(dims.x, dims.y, SiteLayout.pen_cells(pen_count), options.get("buoy_color", MooringGrid.BUOY_YELLOW),
		options.get("anchor_floor", Callable()))
	hoses = FeedHoses.new()
	hoses.name = "FeedHoses"
	add_child(hoses)
	_add_label(options)
	_add_click_area()
	_build_lod_multimeshes()


## Pen centre k at rest (world, on the waterline).
func pen_rest_position(k: int) -> Vector3:
	return to_global(_rest[clampi(k, 0, pen_count - 1)])


## Pen centres at rest in the site frame, pen 1 first.
func pen_local_positions() -> Array[Vector3]:
	return _rest.duplicate()


## The pen nearest a world point.
func nearest_pen(at: Vector3) -> int:
	var local := to_local(at)
	var best := 0
	var best_d := INF
	for k in pen_count:
		var d := Vector2(local.x - _rest[k].x, local.z - _rest[k].z).length_squared()
		if d < best_d:
			best_d = d
			best = k
	return best


func get_cage(k: int) -> PenCage:
	return cages[k] if k >= 0 and k < cages.size() else null


## Half the grid's extent across (x) and along (z), metres.
func half_extent() -> Vector2:
	return SiteLayout.half_extent(pen_count)


# === Variants (per site: every pen of a site is the same kit) ===

func set_lice_skirt(on: bool) -> void:
	if on == _skirt:
		return  # the sim repeats it every day; every part already shows it
	_skirt = on
	for cage in cages:
		cage.set_lice_skirt(on)
	_apply_part_visibility()

func has_lice_skirt() -> bool:
	return _skirt

func set_bird_net(style: int) -> void:
	_bird_net = style
	for cage in cages:
		cage.set_bird_net(style)
	_apply_part_visibility()

func get_bird_net() -> int:
	return _bird_net

func set_owner_band(color: Color) -> void:
	_band = color
	for cage in cages:
		cage.set_owner_band(color)
	_apply_part_visibility()

func get_owner_band() -> Color:
	return _band

func set_net_opacity(value: float) -> void:
	_net_opacity = value
	for cage in cages:
		cage.set_net_opacity(value)
	_apply_part_visibility()

## A ghost site (an empty slot shown as a placeholder): faded pens, no nets,
## no gear, no hoses.
func set_ghost(on: bool) -> void:
	_ghost = on
	var mat: Material = _get_ghost_mat() if on else null
	for cage in cages:
		for mesh in cage.get_lod_meshes(0):
			mesh.material_override = mat
			if on:
				mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			else:
				mesh.cast_shadow = PenCage.shadow_for(String(mesh.name), 0)
		for net in cage.get_net_nodes():
			net.visible = not on
		cage.show_health_ring(_health_rings and not on)
		if not on:
			cage.set_bird_net(_bird_net)
	for level in _lod_parts:
		for part in _lod_parts[level]:
			(_lod_parts[level][part] as MultiMeshInstance3D).material_override = mat
	if grid:
		grid.visible = not on
	if hoses:
		hoses.visible = not on
	_apply_part_visibility()

func is_ghost() -> bool:
	return _ghost

static func _get_ghost_mat() -> StandardMaterial3D:
	if _ghost_mat == null:
		_ghost_mat = StandardMaterial3D.new()
		_ghost_mat.albedo_color = Color(0.55, 0.65, 0.72, 0.30)
		_ghost_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		_ghost_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		_ghost_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	return _ghost_mat


# === Feed hoses ===

## Run a hose from the feed barge to every pen: the barge lies at
## `barge_world` (its centre), `length` x `beam` metres, along the grid.
func attach_hoses(barge_world: Vector3, length: float, beam: float) -> void:
	var local := to_local(barge_world)
	var side := signf(local.x) if absf(local.x) > 0.01 else barge_side
	var keys := SiteLayout.hose_key_points(pen_count, Vector2(local.x, local.z), side, length, beam)
	var lines: Array[PackedVector2Array] = []
	for k in keys.size():
		lines.append(SiteLayout.smooth_hose(keys[k], absi(hash(site_id)) + k * 7919))
	hoses.build(lines)
	hoses.visible = not _ghost

func detach_hoses() -> void:
	if hoses:
		hoses.clear()

func has_hoses() -> bool:
	return hoses != null and not hoses.hoses.is_empty()


# === Pen LODs ===

func _process(_delta: float) -> void:
	SiteFloat.sync()
	var cam := get_viewport().get_camera_3d() if is_inside_tree() else null
	if cam == null or cages.is_empty():
		return
	update_lods(cam.global_position)


## Hand every pen to the LOD its camera distance asks for and upload the
## site's MultiMesh transforms (the tests call it with a probe position).
func update_lods(camera_position: Vector3) -> void:
	var used := [false, false, false]
	var moved := 0
	var changed := false
	for k in cages.size():
		var cage := cages[k]
		moved += cage.ride_count
		var state := next_lod(lod_state[k], camera_position.distance_to(cage.global_position))
		if state != lod_state[k]:
			lod_state[k] = state
			cage.set_lod0_visible(state == Lod.NEAR)
			changed = true
		used[state] = true
	# The MultiMesh poses only change when a pen rode or changed LOD (far
	# pens ride every few frames).
	var stale := changed or moved != _rides_uploaded
	_rides_uploaded = moved
	for level in [Lod.MID, Lod.FAR]:
		var parts: Dictionary = _lod_parts.get(level, {})
		if not used[level]:
			for part in parts:
				(parts[part] as MultiMeshInstance3D).visible = false
			continue
		var fresh := false
		if stale or not _lod_buffers.has(level):
			var buf := _lod_buffer(level)
			fresh = not _lod_buffers.has(level) or _lod_buffers[level] != buf
			if fresh:
				_lod_buffers[level] = buf
		for part in parts:
			var mmi := parts[part] as MultiMeshInstance3D
			mmi.visible = _part_shown(part)
			if fresh:
				mmi.multimesh.buffer = _lod_buffers[level]


## The LOD a pen at `distance` takes, coming from `state` (hysteresis).
static func next_lod(state: int, distance: float) -> int:
	var near: float = PenCage.LOD_SWITCH[0]
	var far: float = PenCage.LOD_SWITCH[1]
	var h := PenCage.LOD_HYSTERESIS
	match state:
		Lod.NEAR:
			if distance < near + h:
				return Lod.NEAR
		Lod.MID:
			if distance > near - h and distance < far + h:
				return Lod.MID
		Lod.FAR:
			if distance > far - h:
				return Lod.FAR
	if distance < near:
		return Lod.NEAR
	return Lod.MID if distance < far else Lod.FAR


## The MultiMesh transforms of one LOD: each pen in that LOD where it rides,
## the others collapsed to nothing. 12 floats a pen, rows of the 3 x 4.
func _lod_buffer(level: int) -> PackedFloat32Array:
	var buf := PackedFloat32Array()
	buf.resize(cages.size() * 12)
	for k in cages.size():
		if lod_state[k] == level:
			MultiMeshPack.write_transform(buf, k * 12, cages[k].transform)
	return buf


## Number of pens a LOD draws now.
func pens_in_lod(level: int) -> int:
	var n := 0
	for s in lod_state:
		if s == level:
			n += 1
	return n


func get_lod_multimeshes(level: int) -> Dictionary:
	return _lod_parts.get(level, {})


## One MultiMesh per pen part for LOD1 and LOD2, from the pen scene's own
## LOD meshes (and so their materials); per-site state as instance uniforms.
func _build_lod_multimeshes() -> void:
	if cages.is_empty():
		return
	var half := half_extent()
	var box := AABB(Vector3(-half.x - CLICK_MARGIN_M - 30.0, -PEN_BOX_DEPTH_M, -half.y - CLICK_MARGIN_M - 30.0),
		Vector3(2.0 * (half.x + CLICK_MARGIN_M + 30.0), PEN_BOX_DEPTH_M + PEN_BOX_HEIGHT_M,
			2.0 * (half.y + CLICK_MARGIN_M + 30.0)))
	for level in [Lod.MID, Lod.FAR]:
		var parts := {}
		for mesh in cages[0].get_lod_meshes(level):
			var mm := MultiMesh.new()
			mm.transform_format = MultiMesh.TRANSFORM_3D
			mm.mesh = mesh.mesh
			mm.instance_count = cages.size()
			# Every instance starts collapsed (no pen in this LOD yet).
			var zero := PackedFloat32Array()
			zero.resize(cages.size() * 12)
			mm.buffer = zero
			mm.custom_aabb = box
			var mmi := MultiMeshInstance3D.new()
			mmi.name = "PenLOD%d_%s" % [level, mesh.name]
			mmi.multimesh = mm
			mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			# The parts under the water stay out of the planar mirror (G2-03).
			mmi.layers = PenCage.layers_for(String(mesh.name))
			mmi.visible = false
			add_child(mmi)
			parts[String(mesh.name)] = mmi
		_lod_parts[level] = parts
	_apply_part_visibility()


func _part_shown(part: String) -> bool:
	if _ghost and part in PenCage.NET_PARTS:
		return false
	return cages[0].part_shown(part) if not cages.is_empty() else true


func _apply_part_visibility() -> void:
	for level in _lod_parts:
		for part in _lod_parts[level]:
			var mmi := _lod_parts[level][part] as MultiMeshInstance3D
			if part == "Band":
				mmi.set_instance_shader_parameter("band_color", _band)
			if part in PenCage.NET_PARTS:
				mmi.set_instance_shader_parameter("net_opacity", _net_opacity)
			if not _part_shown(part):
				mmi.visible = false
			elif pens_in_lod(level) > 0:
				mmi.visible = true


# === Labels and clicks ===

func _add_label(options: Dictionary) -> void:
	label = Label3D.new()
	label.name = "Label"
	label.position = Vector3(0.0, LABEL_HEIGHT, 0.0)
	PenCage.style_label(label, options.get("label_color", Color(0.910, 0.933, 0.941)),
		float(options.get("label_range", 1600.0)))
	label.text = str(options.get("label", site_id))
	add_child(label)


func _add_pen_number(cage: PenCage, k: int) -> void:
	var number := Label3D.new()
	number.name = "Number"
	number.position = Vector3(0.0, PEN_NUMBER_HEIGHT, 0.0)
	PenCage.style_label(number, PEN_NUMBER_COLOR, PEN_NUMBER_RANGE)
	number.font_size = 30
	number.outline_size = 10
	number.text = str(k + 1)
	cage.add_child(number)


func _add_click_area() -> void:
	var half := half_extent()
	click_area = Area3D.new()
	click_area.name = "SiteClickArea"
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(2.0 * (half.x + CLICK_MARGIN_M), CLICK_HEIGHT_M, 2.0 * (half.y + CLICK_MARGIN_M))
	shape.shape = box
	click_area.add_child(shape)
	add_child(click_area)
	click_area.input_event.connect(_on_input_event)


func _on_input_event(_camera: Node, event: InputEvent, at: Vector3, _normal: Vector3, _shape_idx: int) -> void:
	if _ghost:
		return
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		pen_pressed.emit(self, nearest_pen(at), at)
