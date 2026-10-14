class_name FishPenManager
extends Node3D

## Manages fish pen markers in the 3D fjord environment.
## Places pens along the fjord channel and handles visual indicators.
## Pens can be synced from SimBridge site data: one floating collar
## (TorusMesh) + net cylinder + low-poly fish school per owned site.
## Clicking a pen (Area3D) emits pen_clicked for UI detail views.

# === Configuration ===
@export_group("Pen Placement")
@export var max_pens: int = 12
@export var pen_spacing: float = 150.0  # Meters between pens
@export var min_depth: float = 15.0      # Minimum water depth for pen placement
@export var max_depth: float = 350.0    # Maximum water depth (the main channel runs 200-330 m deep)

@export_group("Models")
@export var pen_cage_scene: PackedScene = preload("res://assets/models/pen_cage/pen_cage.tscn")
## The site's service boat: the 15 x 10 m workboat catamaran (A3-01).
@export var service_boat_scene: PackedScene = preload("res://assets/models/workboat/workboat.tscn")
@export var salmon_scene: PackedScene = preload("res://assets/models/fish/salmon.tscn")

@export_group("Fish School")
@export var fish_per_pen: int = 16
@export var fish_swim_radius: float = 9.0
@export var fish_swim_speed: float = 0.6

# === Internal State ===
var _pens: Dictionary = {}  # pen_id -> {position, active, node, depth, site_id, health}
var _terrain_ref: FjordTerrain = null
var _recommended_locations: Array[Vector3] = []
var _fish_time: float = 0.0

# === Node References (auto-created if the scene lacks them) ===
var markers_parent: Node3D
var visualization: MeshInstance3D
var active_indicator: MeshInstance3D
## Feeding FX: ParticleEffects node + per-pen cooldown (seconds).
var _particles: Node = null
var _last_fed_at: Dictionary = {}
const FEED_FX_COOLDOWN := 25.0

# === Signals ===
signal pen_added(pen_id: String, position: Vector3)
signal pen_removed(pen_id: String)
signal pen_clicked(pen_id: String, position: Vector3)
signal pen_selection_changed(pen_id: String)

var selected_pen_id: String = ""

# Health ring palette (the signal ring on the water; the collar stays black
# HDPE). Same values as PenCage.HEALTH_COLORS.
const COLOR_HEALTHY := Color(0.498, 0.839, 0.608, 0.55)
const COLOR_WARNING := Color(0.949, 0.718, 0.020, 0.85)
const COLOR_CRITICAL := Color(1.0, 0.541, 0.502, 0.95)
const COLOR_DEFAULT := Color(0.624, 0.706, 0.733, 0.35)
## Regulatory lice limits (adult female lice per fish): the spring limit and
## the standing limit the sim enforces.
const LICE_WARN := 0.2
const LICE_CRIT := 0.5
## The selection ring is a flat quad on the water around the selected pen.
const SELECTION_RING_SIZE := 84.0
const SELECTION_RING_Y := 0.2
const SELECTION_RING_MATERIAL := preload("res://assets/models/pen_cage/pen_selection_ring_material.tres")
## Selected-pen ring colour: Ink text (#E8EEF0).
const SELECTION_COLOR := Color(0.910, 0.933, 0.941, 0.9)
## The workboat lies broadside to the collar this far from the centre
## (outer pipe edge at 26.8 m, half the boat's 10 m beam, the tyre fenders
## and a 1.4 m gap).
const BOAT_MOORING_OFFSET := 33.6


func _ready() -> void:
	_ensure_expected_nodes()
	# ParticleEffects is a sibling under FjordEnvironment in GameScene.
	_particles = get_node_or_null("../ParticleEffects")
	if visualization:
		visualization.visible = false
	if active_indicator:
		active_indicator.visible = false


## Fire feeding particles while the sim reports active feeding at a site
## (rate-limited per pen so the 1 Hz sim sync does not spam emitters).
func _maybe_emit_feeding(pen_id: String, site: Dictionary) -> void:
	if _particles == null or not _particles.has_method("_emit_feeding"):
		return
	if not bool(site.get("is_feeding", false)):
		return
	var now := Time.get_ticks_msec() / 1000.0
	if now - float(_last_fed_at.get(pen_id, -9999.0)) < FEED_FX_COOLDOWN:
		return
	_last_fed_at[pen_id] = now
	_particles.call("_emit_feeding", {"pen_id": pen_id})


func _ensure_expected_nodes() -> void:
	markers_parent = get_node_or_null("PenMarkers") as Node3D
	if markers_parent == null:
		markers_parent = Node3D.new()
		markers_parent.name = "PenMarkers"
		add_child(markers_parent)
	# Visualization and active_indicator kept for backward compatibility
	visualization = get_node_or_null("FishPenVisualization") as MeshInstance3D
	if visualization == null:
		visualization = MeshInstance3D.new()
		visualization.name = "FishPenVisualization"
		visualization.mesh = _create_ring_mesh(COLOR_DEFAULT)
		visualization.visible = false
		add_child(visualization)
	# Selection marker: a dashed ring on the water around the selected pen
	# (it replaced a green debug sphere floating above the collar, G1-07).
	active_indicator = get_node_or_null("ActivePenIndicator") as MeshInstance3D
	if active_indicator == null:
		active_indicator = MeshInstance3D.new()
		active_indicator.name = "ActivePenIndicator"
		var plane := PlaneMesh.new()
		plane.size = Vector2(SELECTION_RING_SIZE, SELECTION_RING_SIZE)
		active_indicator.mesh = plane
		active_indicator.material_override = SELECTION_RING_MATERIAL
		active_indicator.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		active_indicator.set_instance_shader_parameter("ring_color", SELECTION_COLOR)
		active_indicator.visible = false
		add_child(active_indicator)


func _process(delta: float) -> void:
	if _pens.is_empty():
		return
	_fish_time += delta * fish_swim_speed
	for pen_id in _pens:
		var pen_data = _pens[pen_id]
		_animate_fish_school(pen_data, pen_id)


func setup_pens(terrain: FjordTerrain) -> void:
	"""Initialize pen manager with terrain reference."""
	_terrain_ref = terrain
	_generate_recommended_locations()

	# Place default pens at recommended locations
	for i in range(min(max_pens, _recommended_locations.size())):
		var pen_id = "pen_%d" % (i + 1)
		var pos = _recommended_locations[i]
		_add_pen_internal(pen_id, pos, false)


func _generate_recommended_locations() -> void:
	"""Generate recommended pen locations along the fjord channel."""
	_recommended_locations.clear()

	if _terrain_ref == null:
		# Auto-resolve: in GameScene this manager is a sibling of Terrain,
		# and setup_pens() is only called by NordlandFjordController.
		_terrain_ref = get_node_or_null("../Terrain") as FjordTerrain

	if not _terrain_ref:
		# Fallback: place pens in a line through center, collars floating.
		for i in range(max_pens):
			var z = (i - max_pens / 2.0) * pen_spacing
			_recommended_locations.append(Vector3(0, 0, z))
		return

	# Generate locations based on fjord shape
	var fjord_length = _terrain_ref.terrain_size.y
	var start_z = -fjord_length * 0.4

	for i in range(max_pens):
		# Create pens along the winding fjord centerline.
		var progress = float(i) / float(max_pens - 1)
		var z = start_z + progress * fjord_length * 0.8

		# The TRUE channel center, mirroring FjordTerrain's generator
		# (fjord_center = sin(nz*2)*0.1 in normalized coords, x = nx*2000).
		# The old sin(progress*4)*50 guess missed the channel entirely and
		# parked pens on the cliff walls.
		var fjord_center_x = sin(z / 1000.0) * 200.0

		# Try different x positions to find deep water
		var best_pos = Vector3(fjord_center_x, 0, z)
		var best_depth = -999.0

		for offset in [-250, -150, -75, 0, 75, 150, 250]:
			var test_x = fjord_center_x + offset
			var height = _terrain_ref.get_height_at(test_x, z)
			var depth = -height  # Negative height = underwater

			if depth > min_depth and depth < max_depth and depth > best_depth:
				best_depth = depth
				# The collar floats ON the surface; only the net hangs below.
				best_pos = Vector3(test_x, 0.0, z)

		_recommended_locations.append(best_pos)


# === Sim sync ===

func sync_pens_from_sim(sites: Array) -> void:
	"""Reconcile 3D pens with player-owned sites from SimBridge.
	Sites are Dictionaries; id/name/biomass/health keys are read
	tolerantly since the C# payload shape may vary."""
	var wanted: Dictionary = {}
	for i in range(sites.size()):
		var site = sites[i]
		if not (site is Dictionary):
			continue
		var sid := _site_string(site, ["id", "site_id", "zone_id", "pen_id"], "site_%d" % i)
		wanted[sid] = {"index": i, "data": site}
		if not (sid in _pens):
			var pos := _slot_position(i)
			_add_pen_internal(sid, pos, true)
		_apply_site_data(sid, wanted[sid]["data"], i)
	# Remove pens whose site is gone
	for pen_id in _pens.keys():
		if _pens[pen_id].get("from_sim", false) and not (pen_id in wanted):
			remove_pen(pen_id)


func _slot_position(index: int) -> Vector3:
	if _recommended_locations.is_empty():
		_generate_recommended_locations()
	if index < _recommended_locations.size():
		return _recommended_locations[index]
	var z := (float(index) - float(max_pens) / 2.0) * pen_spacing
	return Vector3(0, 0, z)


func _apply_site_data(pen_id: String, site: Dictionary, index: int) -> void:
	if not (pen_id in _pens):
		return
	var pen_data: Dictionary = _pens[pen_id]
	pen_data["site_id"] = _site_string(site, ["id", "site_id", "zone_id"], pen_id)
	pen_data["from_sim"] = true
	_restore_stocked_style(pen_id)
	var cage = pen_data["cage"]
	if cage:
		var label = cage.get_node_or_null("Label") as Label3D
		if not label:
			label = Label3D.new()
			label.name = "Label"
			label.position.y = PenCage.LABEL_HEIGHT
			PenCage.style_label(label, Color(0.910, 0.933, 0.941))
			cage.add_child(label)
		label.text = _site_string(site, ["name", "pen_name", "id"], pen_id)
		if cage is PenCage:
			(cage as PenCage).set_lice_skirt(bool(site.get("has_skirt", false)))
	_set_health(pen_id, health_state_for(site))
	_apply_fish_density(pen_id, site)
	_maybe_emit_feeding(pen_id, site)


## Site health for the ring, from the keys GameBridge.GetSeaSiteDetails
## really sends: an empty site is UNKNOWN (neutral), otherwise the lice level
## against the sim's regulatory limits (green below the 0.2 spring limit,
## amber up to the 0.5 adult-female limit, red at or above it). The legacy
## "health"/"health_score" keys are honoured for other callers.
static func health_state_for(site: Dictionary) -> int:
	if site.has("fish_count") and _site_float(site, ["fish_count"], 0.0) <= 0.0:
		return PenCage.Health.UNKNOWN
	var lice := _site_float(site, ["lice_per_fish", "sea_lice", "lice"], -1.0)
	if lice >= 0.0:
		if lice >= LICE_CRIT:
			return PenCage.Health.CRIT
		if lice >= LICE_WARN:
			return PenCage.Health.WARN
		return PenCage.Health.OK
	var health := _site_float(site, ["health", "health_score", "welfare"], -1.0)
	if health >= 0.0:
		if health < 0.4:
			return PenCage.Health.CRIT
		if health < 0.7:
			return PenCage.Health.WARN
		return PenCage.Health.OK
	return PenCage.Health.UNKNOWN


static func health_color_for(site: Dictionary) -> Color:
	return PenCage.health_color_of(health_state_for(site))


static func _site_string(site: Dictionary, keys: Array, fallback: String) -> String:
	for key in keys:
		if site.has(key) and str(site[key]) != "":
			return str(site[key])
	return fallback


static func _site_float(site: Dictionary, keys: Array, fallback: float) -> float:
	for key in keys:
		if site.has(key) and (site[key] is float or site[key] is int):
			return float(site[key])
	return fallback


func _add_pen_internal(pen_id: String, position: Vector3, active: bool) -> void:
	"""Internal method to add a pen marker using the PenCage scene."""

	# Instantiate the PenCage scene. NOTE: add_child() BEFORE touching
	# global_position — setting it on a node outside the tree errors and
	# silently leaves the pen at the origin.
	var cage = pen_cage_scene.instantiate()
	cage.name = pen_id
	markers_parent.add_child(cage)
	cage.global_position = position
	# Clicks on the cage report here. PenCage cannot reach this manager through
	# its parent (PenMarkers has no pen_clicked signal), so the click used to drop.
	var click_area := cage.get_node_or_null("ClickArea") as Area3D
	if click_area:
		click_area.input_event.connect(_on_pen_input_event.bind(pen_id))

	# Service boat moored alongside the collar, broadside to it, between the
	# frame corners (the bridles run to the corners) and away from the feed
	# hose (+X); the workboat's origin is its waterline.
	var boat = service_boat_scene.instantiate()
	boat.name = "%s_boat" % pen_id
	markers_parent.add_child(boat)
	boat.global_position = position + Vector3(0.0, 0.0, BOAT_MOORING_OFFSET)
	boat.rotation.y = PI * 0.5

	# The site's frame mooring: one 70 m cell round the pen, grid buoys,
	# bridles, anchor lines and the synchronised corner lights.
	var mooring := MooringGrid.new()
	mooring.name = "%s_mooring" % pen_id
	markers_parent.add_child(mooring)
	mooring.global_position = position
	mooring.build(1, 1, [Vector2i(0, 0)], MooringGrid.BUOY_GREY, _seabed_lookup())

	# Fish school using MultiMesh
	var school := MultiMeshInstance3D.new()
	school.name = "FishSchool"
	school.multimesh = _create_fish_multimesh()
	markers_parent.add_child(school)
	school.global_position = position

	# Store pen data (keep the stock material and shadow setting of every pen
	# mesh so stocked pens can be restored after ghosting).
	var stock_mats := {}
	for mesh in _pen_meshes(cage):
		stock_mats[mesh] = [mesh.material_override, mesh.cast_shadow]
	_pens[pen_id] = {
		"position": position,
		"active": active,
		"node": cage,
		"from_sim": false,
		"depth": -_terrain_ref.get_height_at(position.x, position.z) if _terrain_ref else 30.0,
		"cage": cage,
		"boat": boat,
		"fish_school": school,
		"mooring": mooring,
		"stock_mats": stock_mats,
	}
	if not active:
		_apply_ghost_style(pen_id)

	pen_added.emit(pen_id, position)

## Seabed height lookup for mooring anchor lines (world x, z -> y), or an
## empty Callable when there is no terrain.
func _seabed_lookup() -> Callable:
	if _terrain_ref == null:
		_terrain_ref = get_node_or_null("../Terrain") as FjordTerrain
	if _terrain_ref == null:
		return Callable()
	var terrain := _terrain_ref
	return func(x: float, z: float) -> float: return terrain.get_height_at(x, z)

## The frame mooring laid round a pen (null for an unknown pen).
func get_pen_mooring(pen_id: String) -> MooringGrid:
	if pen_id in _pens:
		return _pens[pen_id].get("mooring") as MooringGrid
	return null

## Shared ghost material for unstocked (available-site) pens: faded ring,
## no boat, no net — a placeholder, not a farm.
static var _ghost_mat: StandardMaterial3D

static func _get_ghost_mat() -> StandardMaterial3D:
	if _ghost_mat == null:
		_ghost_mat = StandardMaterial3D.new()
		_ghost_mat.albedo_color = Color(0.55, 0.65, 0.72, 0.30)
		_ghost_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		_ghost_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		_ghost_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
		_ghost_mat.no_depth_test = false
	return _ghost_mat

## The pen's structure meshes (everything but the signal ring), the ones the
## ghost style fades. Nets are handled apart: a ghost pen has none.
static func _pen_meshes(cage: Node) -> Array[MeshInstance3D]:
	var meshes: Array[MeshInstance3D] = []
	if cage == null:
		return meshes
	for node in cage.find_children("*", "MeshInstance3D", true, false):
		if node.name != "HealthRing":
			meshes.append(node as MeshInstance3D)
	return meshes

func _apply_ghost_style(pen_id: String) -> void:
	if not (pen_id in _pens):
		return
	var pen_data: Dictionary = _pens[pen_id]
	var cage = pen_data.get("cage")
	for mesh in _pen_meshes(cage):
		mesh.material_override = _get_ghost_mat()
		mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	if cage is PenCage:
		for net in (cage as PenCage).get_net_nodes():
			net.visible = false
		(cage as PenCage).show_health_ring(false)
	var boat = pen_data.get("boat")
	if boat is Node3D:
		(boat as Node3D).visible = false
	var mooring = pen_data.get("mooring")
	if mooring is Node3D:
		(mooring as Node3D).visible = false

func _restore_stocked_style(pen_id: String) -> void:
	if not (pen_id in _pens):
		return
	var pen_data: Dictionary = _pens[pen_id]
	var cage = pen_data.get("cage")
	var stock_mats: Dictionary = pen_data.get("stock_mats", {})
	for mesh in _pen_meshes(cage):
		if mesh in stock_mats:
			mesh.material_override = stock_mats[mesh][0]
			mesh.cast_shadow = stock_mats[mesh][1]
	if cage is PenCage:
		for net in (cage as PenCage).get_net_nodes():
			net.visible = true
		(cage as PenCage).show_health_ring(true)
	var boat = pen_data.get("boat")
	if boat is Node3D:
		(boat as Node3D).visible = true
	var mooring = pen_data.get("mooring")
	if mooring is Node3D:
		(mooring as Node3D).visible = true


func _create_ring_mesh(color: Color) -> Mesh:
	"""Create a torus mesh for pen visualization."""
	var torus = TorusMesh.new()
	torus.inner_radius = 12.0
	torus.outer_radius = 14.0

	var material = StandardMaterial3D.new()
	material.albedo_color = color
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	torus.material = material

	return torus


func _create_net_mesh() -> Mesh:
	"""Semi-transparent cylinder representing the pen net."""
	var cyl := CylinderMesh.new()
	cyl.top_radius = 12.0
	cyl.bottom_radius = 12.0
	cyl.height = 12.0
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.15, 0.2, 0.25, 0.28)
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	material.cull_mode = BaseMaterial3D.CULL_DISABLED
	cyl.material = material
	return cyl


## Blender-built salmon (blender/build_salmon.py -> salmon_v2.glb).
## Falls back to a plain box if the import is missing.
var _salmon_mesh: Mesh = null

func _get_salmon_mesh() -> Mesh:
	if _salmon_mesh:
		return _salmon_mesh
	var packed: PackedScene = load("res://assets/models/fish/salmon_v2.glb") as PackedScene
	if packed != null:
		var inst: Node = packed.instantiate()
		var mi := inst.find_child("LK_Salmon", true, false) as MeshInstance3D
		if mi and mi.mesh:
			_salmon_mesh = mi.mesh
		inst.queue_free()
	if _salmon_mesh == null:
		var body := BoxMesh.new()
		body.size = Vector3(0.9, 0.3, 0.25)
		var mat := StandardMaterial3D.new()
		mat.albedo_color = Color(0.5, 0.58, 0.65, 1.0)
		mat.roughness = 0.6
		body.material = mat
		_salmon_mesh = body
	return _salmon_mesh


func _create_fish_multimesh() -> MultiMesh:
	"""One shared low-poly fish school mesh per pen (modest count)."""
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = _get_salmon_mesh()
	# Allocated once at max; _apply_fish_density drives visible count.
	mm.instance_count = FISH_MAX_INSTANCES
	mm.visible_instance_count = 0
	return mm


## Visible fish scale with the sim: count follows stocking, body length
## follows mean weight (L(cm) = (100*W_g/1.1)^(1/3), body mesh is 0.9 m).
## The MultiMesh is allocated once at FISH_MAX_INSTANCES; only
## visible_instance_count moves (changing instance_count resets transforms).
const FISH_MAX_INSTANCES := 240

func _apply_fish_density(pen_id: String, site: Dictionary) -> void:
	if not (pen_id in _pens):
		return
	var pen_data: Dictionary = _pens[pen_id]
	var school: MultiMeshInstance3D = pen_data["fish_school"]
	if not school or not school.multimesh:
		return
	var fish_count := int(_site_float(site, ["fish_count"], 0.0))
	var mean_w := _site_float(site, ["mean_weight_kg"], 0.0)
	var visible := clampi(fish_count / 250, 0, FISH_MAX_INSTANCES)
	school.multimesh.visible_instance_count = visible
	# Body length from weight; clamp to sane bounds (smolt .. harvest).
	var length_m := 0.2
	if mean_w > 0.0:
		length_m = clampf(pow(100.0 * mean_w * 1000.0 / 1.1, 1.0 / 3.0) / 100.0, 0.15, 1.0)
	pen_data["fish_scale"] = length_m / 0.9


func _animate_fish_school(pen_data: Dictionary, pen_id: String) -> void:
	var school: MultiMeshInstance3D = pen_data["fish_school"]
	if not school or not school.multimesh:
		return
	var count: int = school.multimesh.visible_instance_count
	var fish_scale: float = pen_data.get("fish_scale", 0.5)
	var phase := float(abs(hash(pen_id)) % 1000) / 1000.0 * TAU
	for i in range(count):
		var t := _fish_time + phase + float(i) * 0.35
		var r := fish_swim_radius * (0.55 + 0.45 * float(i % 3) / 2.0)
		var depth := -3.0 - float(i % 4) * 2.0
		var pos := Vector3(cos(t) * r, depth + sin(t * 1.7) * 0.6, sin(t) * r)
		# Tangential heading: velocity of the circle path; the salmon model's
		# nose points along local -Z (Blender -Y -> Godot -Z on import).
		var vel := Vector3(-sin(t), 0.0, cos(t))
		var basis := Basis.looking_at(vel, Vector3.UP).scaled(Vector3.ONE * fish_scale)
		school.multimesh.set_instance_transform(i, Transform3D(basis, pos))


## Show a site's health on its pen's signal ring (an instance parameter on
## the shared ring material; the collar albedo stays black HDPE).
func _set_health(pen_id: String, state: int) -> void:
	if not (pen_id in _pens):
		return
	_pens[pen_id]["ring_color"] = PenCage.health_color_of(state)
	_pens[pen_id]["health_state"] = state
	var cage = _pens[pen_id]["cage"]
	if cage is PenCage:
		(cage as PenCage).set_health_state(state)


## The colour a pen's health ring shows ("" or unknown pen: COLOR_DEFAULT).
func get_pen_health_color(pen_id: String) -> Color:
	if pen_id in _pens:
		var cage = _pens[pen_id]["cage"]
		if cage is PenCage:
			return (cage as PenCage).get_health_color()
	return COLOR_DEFAULT


func _on_pen_input_event(_camera: Node, event: InputEvent, _position: Vector3, _normal: Vector3, _shape_idx: int, pen_id: String) -> void:
	"""Handle click events on pen markers."""
	if not _pens.has(pen_id):
		return  # a removed pen's queued click
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
			select_pen(pen_id)
			pen_clicked.emit(pen_id, _pens[pen_id].position)


func select_pen(pen_id: String) -> void:
	"""Select a pen: highlight ring, move indicator, notify listeners."""
	if not (pen_id in _pens):
		return
	selected_pen_id = pen_id
	if active_indicator:
		# Flat on the water around the collar, not floating above it.
		var at: Vector3 = _pens[pen_id]["node"].global_position
		active_indicator.visible = true
		active_indicator.global_position = Vector3(at.x, at.y + SELECTION_RING_Y, at.z)
	pen_selection_changed.emit(pen_id)


# === Public API ===

func add_pen(pen_id: String, position: Vector3) -> bool:
	"""Add a new pen at the specified position."""
	if _pens.size() >= max_pens:
		push_warning("[FishPenManager] Maximum pens (%d) reached" % max_pens)
		return false

	if pen_id in _pens:
		push_warning("[FishPenManager] Pen %s already exists" % pen_id)
		return false

	_add_pen_internal(pen_id, position, true)
	pen_added.emit(pen_id, position)
	return true


func remove_pen(pen_id: String) -> bool:
	"""Remove a pen from the scene."""
	if not pen_id in _pens:
		return false

	var pen_data = _pens[pen_id]
	if pen_data["cage"]:
		pen_data["cage"].queue_free()
	if pen_data["boat"]:
		pen_data["boat"].queue_free()
	if pen_data["fish_school"]:
		pen_data["fish_school"].queue_free()
	if pen_data.get("mooring"):
		pen_data["mooring"].queue_free()
	_pens.erase(pen_id)
	if pen_id == selected_pen_id:
		selected_pen_id = ""
		if active_indicator:
			active_indicator.visible = false

	pen_removed.emit(pen_id)
	return true


func get_pen_position(pen_id: String) -> Vector3:
	"""Get the world position of a pen."""
	if pen_id in _pens:
		return _pens[pen_id]["position"]
	return Vector3.ZERO


func get_active_pen_count() -> int:
	"""Get number of active pens."""
	var count = 0
	for pen_data in _pens.values():
		if pen_data.active:
			count += 1
	return count


func get_pen_ids() -> Array[String]:
	"""Get list of all pen IDs."""
	var ids: Array[String] = []
	for pen_id in _pens.keys():
		ids.append(pen_id)
	return ids


func get_recommended_locations() -> Array[Vector3]:
	"""Deep-water slots along the fjord channel (generated on first use).
	The player's pens fill them from the start; rival farms from the end."""
	if _recommended_locations.is_empty():
		_generate_recommended_locations()
	return _recommended_locations.duplicate()


func get_available_slots() -> int:
	"""Get number of available pen slots."""
	return max_pens - _pens.size()


func refresh_pen_positions() -> void:
	"""Update pen positions after terrain regeneration."""
	if not _terrain_ref:
		return

	for pen_id in _pens:
		var pen_data = _pens[pen_id]
		var pos = pen_data["position"]
		var new_height = _terrain_ref.get_height_at(pos.x, pos.z)

		# Keep pen at appropriate depth
		if new_height > -5.0:  # Too shallow
			pen_data["cage"].position.y = -10.0
		else:
			pen_data["cage"].position.y = new_height * 0.3  # Float at ~30% depth

		pen_data["position"] = pen_data["cage"].global_position
		pen_data["depth"] = -new_height


func show_pen_visualization(pen_id: String = "") -> void:
	"""Show visual indicator for pen placement preview."""
	if visualization:
		visualization.visible = true
		if pen_id and pen_id in _pens:
			visualization.position = _pens[pen_id].position


func hide_pen_visualization() -> void:
	"""Hide pen placement preview."""
	if visualization:
		visualization.visible = false


func get_pen_info(pen_id: String) -> Dictionary:
	"""Get detailed information about a pen."""
	if not pen_id in _pens:
		return {}

	var pen_data = _pens[pen_id]
	return {
		"id": pen_id,
		"position": pen_data["position"],
		"depth": pen_data["depth"],
		"active": pen_data["active"]
	}
