class_name FishPenManager
extends Node3D

## The player's salmon sites in the 3D fjord (roadmap G2-04). Every sea site
## the sim reports (GameBridge.GetSeaSiteDetails) is a FarmSite: as many
## pens as the engine gives the site ("pen_count"), in a frame-mooring grid
## laid along the fjord channel, with its feed barge's berth on one side, a
## wellboat berth on the other and the site's workboat at the grid's
## up-fjord end. The sites take channel slots from the fjord mouth; a big
## site takes several (rival farms fill the slots from the head).
##
## The UI names a site by its id and calls it a "pen" (get_pen_ids,
## get_pen_position, select_pen, pen_clicked), so those keep taking site
## ids; each pen of a site has an index (0-based, numbered from 1 on the
## water). Clicking any pen selects its site and that pen.

# === Configuration ===
@export_group("Pen Placement")
## Channel slots along the basin (each site takes one or more).
@export var max_pens: int = 12
@export var pen_spacing: float = 150.0  # Meters between slots without terrain
@export var min_depth: float = 15.0      # Minimum water depth under a grid
@export var max_depth: float = 350.0    # Maximum water depth (the main channel runs 200-330 m deep)

@export_group("Models")
## The site's service boat: the 15 x 10 m workboat catamaran (A3-01).
@export var service_boat_scene: PackedScene = preload("res://assets/models/workboat/workboat.tscn")
@export var salmon_scene: PackedScene = preload("res://assets/models/fish/salmon.tscn")

@export_group("Fish School")
@export var fish_per_pen: int = 16
@export var fish_swim_radius: float = 9.0
@export var fish_swim_speed: float = 0.6

# === Internal State ===
## site_id -> {position, active, node, cage, cages, site, from_sim, depth,
## boat, fish_school, fish_schools, mooring, grid_yaw, barge_side,
## pen_count, slots}. "node"/"cage"/"fish_school" are pen 1's.
var _pens: Dictionary = {}
var _terrain_ref: FjordTerrain = null
var _recommended_locations: Array[Vector3] = []
var _fish_time: float = 0.0

# === Node References (auto-created if the scene lacks them) ===
var markers_parent: Node3D
var visualization: MeshInstance3D
var active_indicator: MeshInstance3D
## Rings round the other pens of the selected site (one MultiMesh).
var site_indicator: MultiMeshInstance3D
## Feeding FX: ParticleEffects node + per-site cooldown (seconds).
var _particles: Node = null
var _last_fed_at: Dictionary = {}
const FEED_FX_COOLDOWN := 25.0

# === Signals ===
signal pen_added(pen_id: String, position: Vector3)
signal pen_removed(pen_id: String)
signal pen_clicked(pen_id: String, position: Vector3)
signal pen_selection_changed(pen_id: String)
## A pen of a site was selected (index 0-based; -1 for the whole site).
signal cage_selected(site_id: String, pen_index: int)

var selected_pen_id: String = ""
var selected_pen_index: int = -1

# Health ring palette (the signal ring on the water; the collar stays black
# HDPE). Same values as PenCage.HEALTH_COLORS.
const COLOR_HEALTHY := Color(0.498, 0.839, 0.608, 0.32)
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
## Selected-pen ring colour: Ink text (#E8EEF0); the site's other pens get
## the same ring, quieter.
const SELECTION_COLOR := Color(0.910, 0.933, 0.941, 0.9)
const SITE_SELECTION_COLOR := Color(0.910, 0.933, 0.941, 0.42)
## The workboat lies broadside to the collar of the up-fjord pen of the
## first column this far from its centre (outer pipe edge at 26.8 m, half
## the boat's 10 m beam, the tyre fenders and a 1.4 m gap), inside its cell.
const BOAT_MOORING_OFFSET := 33.6

# === Site layout ===
# A site's frame mooring is laid along the channel (its local +Z follows the
# fjord's centreline at the site). The feed barge lies on one side of the
# grid with its hoses to every pen, and a wellboat comes alongside on the
# other side; both lie outside the grid buoys. The wellboat takes the side
# facing the fairway (the open water a 116 m hull needs; on the centreline,
# the deeper side), the barge the other.
## Metres from the grid's outer frame line to the barge's inner hull side:
## the hoses leave the hull, drop to the water and float in at the grid.
const BARGE_GRID_GAP_M := 16.0
## ... and to a wellboat's inner hull side: 3.5 m outside the grid buoys.
const WELLBOAT_GRID_GAP_M := 3.5
## What a site keeps clear for passing boats, beyond the grid: the widest
## wellboat (23 m beam) on its side, the large barge (13 m) with its tender
## RIB (3 m and a 0.5 m gap) on the other, over at least this half-length
## along the grid axis (a 116 m hull alongside, plus room to turn in).
const WELLBOAT_RESERVE_BEAM_M := 23.0
const BARGE_RESERVE_BEAM_M := 16.5
const SITE_RESERVE_HALF_LENGTH_M := 80.0
const SITE_RESERVE_END_M := 12.0
## A boat heading for a berth sees that side of its site clear from this far
## inside its own inner hull side, more than ChannelBoat's 4 m lane
## clearance, so the berth's own lane stays open all the way in.
const OPEN_BERTH_SLACK_M := 5.0
## Water a berth side needs under its footprint (m: the 116 m carrier's
## 8.5 m draught plus ChannelBoat's 2 m keel clearance, and a margin), and
## the footprint's along-axis reach either side of the site centre (m).
const BERTH_MIN_DEPTH_M := 12.0
const BERTH_SAMPLE_REACH_M := 60.0
## The channel's heading at a site is the centreline's slope over +-this (m).
const CHANNEL_HEADING_SPAN_M := 20.0
## Lateral offsets (m) tried for a site's grid centre, and the water every
## grid corner needs under it (a net hangs 38 m: the seabed comes first).
const SITE_X_OFFSETS := [-250.0, -150.0, -75.0, 0.0, 75.0, 150.0, 250.0]
const GRID_MIN_DEPTH_M := 30.0


func _ready() -> void:
	_ensure_expected_nodes()
	# ParticleEffects is a sibling under FjordEnvironment in GameScene.
	_particles = get_node_or_null("../ParticleEffects")
	if visualization:
		visualization.visible = false
	if active_indicator:
		active_indicator.visible = false


## Fire feeding particles at a site's pens while the sim reports active
## feeding (rate-limited per site so the 1 Hz sim sync does not spam
## emitters).
func _maybe_emit_feeding(pen_id: String, site: Dictionary) -> void:
	if _particles == null or not _particles.has_method("_emit_feeding"):
		return
	if not bool(site.get("is_feeding", false)) or not (pen_id in _pens):
		return
	var now := Time.get_ticks_msec() / 1000.0
	if now - float(_last_fed_at.get(pen_id, -9999.0)) < FEED_FX_COOLDOWN:
		return
	_last_fed_at[pen_id] = now
	var farm: FarmSite = _pens[pen_id]["site"]
	for k in farm.pen_count:
		_particles.call("_emit_feeding", {"pen_id": "%s#%d" % [pen_id, k + 1], "position": farm.pen_rest_position(k)})


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
		active_indicator.mesh = _selection_plane()
		active_indicator.material_override = SELECTION_RING_MATERIAL
		active_indicator.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		active_indicator.set_instance_shader_parameter("ring_color", SELECTION_COLOR)
		active_indicator.visible = false
		add_child(active_indicator)
	site_indicator = get_node_or_null("SiteIndicator") as MultiMeshInstance3D
	if site_indicator == null:
		site_indicator = MultiMeshInstance3D.new()
		site_indicator.name = "SiteIndicator"
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.mesh = _selection_plane()
		site_indicator.multimesh = mm
		site_indicator.material_override = SELECTION_RING_MATERIAL
		site_indicator.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		site_indicator.set_instance_shader_parameter("ring_color", SITE_SELECTION_COLOR)
		site_indicator.extra_cull_margin = 4.0
		site_indicator.visible = false
		add_child(site_indicator)


static func _selection_plane() -> PlaneMesh:
	var plane := PlaneMesh.new()
	plane.size = Vector2(SELECTION_RING_SIZE, SELECTION_RING_SIZE)
	return plane


func _process(delta: float) -> void:
	if _pens.is_empty():
		return
	# The floating gear samples the water bake the water does.
	if _terrain_ref != null and _terrain_ref.water_bake_texture != SiteFloat.bake_texture:
		SiteFloat.bake_texture = _terrain_ref.water_bake_texture
	SiteFloat.sync()
	_fish_time += delta * fish_swim_speed
	var cam := get_viewport().get_camera_3d() if is_inside_tree() else null
	for pen_id in _pens:
		var pen_data: Dictionary = _pens[pen_id]
		for k in pen_data["fish_schools"].size():
			_animate_fish_school(pen_data["fish_schools"][k], pen_data, "%s#%d" % [pen_id, k], cam)
	_follow_selected_pen()


## The selection rings lie on the water round the selected pens, so they
## rise and fall with the pens as they ride the waves.
func _follow_selected_pen() -> void:
	if not (selected_pen_id in _pens):
		return
	var farm: FarmSite = _pens[selected_pen_id]["site"]
	if active_indicator != null and active_indicator.visible:
		var cage := farm.get_cage(maxi(selected_pen_index, 0))
		if cage != null:
			active_indicator.global_position.y = cage.global_position.y + SELECTION_RING_Y
	if site_indicator != null and site_indicator.visible:
		_place_site_rings(farm)


func setup_pens(terrain: FjordTerrain) -> void:
	"""Initialize pen manager with terrain reference: a ghost one-pen site
	on every free channel slot (placeholders, not farms)."""
	_terrain_ref = terrain
	_generate_recommended_locations()
	for i in range(min(max_pens, _recommended_locations.size())):
		var pen_id = "pen_%d" % (i + 1)
		_add_site(pen_id, 1, Vector2i(i, 1), false, {})


func _generate_recommended_locations() -> void:
	"""Generate the channel slots along the fjord's deep water."""
	_recommended_locations.clear()

	if _terrain_ref == null:
		# Auto-resolve: in GameScene this manager is a sibling of Terrain.
		_terrain_ref = get_node_or_null("../Terrain") as FjordTerrain

	if not _terrain_ref:
		# Fallback: place slots in a line through center, collars floating.
		for i in range(max_pens):
			var z = (i - max_pens / 2.0) * pen_spacing
			_recommended_locations.append(Vector3(0, 0, z))
		return

	# Slots run along the basin, inside the sill and short of the head delta.
	var span: Vector2 = _terrain_ref.pen_slot_range()

	for i in range(max_pens):
		var progress = float(i) / float(max_pens - 1)
		var z = lerpf(span.x, span.y, progress)
		# The channel centreline comes from the terrain generator itself, so
		# the slots follow the fjord wherever it winds.
		var fjord_center_x = _terrain_ref.channel_center_x(z)
		var best_pos = Vector3(fjord_center_x, 0, z)
		var best_depth = -999.0
		for offset in [-250, -150, -75, 0, 75, 150, 250]:
			var test_x = fjord_center_x + offset
			var depth = -_terrain_ref.get_height_at(test_x, z)
			if depth > min_depth and depth < max_depth and depth > best_depth:
				best_depth = depth
				# The collar floats ON the surface; only the net hangs below.
				best_pos = Vector3(test_x, 0.0, z)
		_recommended_locations.append(best_pos)


## Spacing (m) between neighbouring channel slots.
func slot_spacing() -> float:
	var slots := get_recommended_locations()
	if slots.size() < 2:
		return pen_spacing
	return absf(slots[1].z - slots[0].z)


## Channel slots the player's sites take, from the mouth (rival farms take
## the rest from the head).
func slots_used() -> int:
	var used := 0
	for pen_id in _pens:
		if _pens[pen_id].get("from_sim", false):
			var slots: Vector2i = _pens[pen_id]["slots"]
			used = maxi(used, slots.x + slots.y)
	return used


## Where a site of `pen_count` pens over slots [start, start + count) lies:
## its centre level with the middle of the slots, in the deepest water
## across the channel whose whole grid floats over at least
## GRID_MIN_DEPTH_M (the deepest point when none does).
func _site_position(slots: Vector2i, pen_count: int) -> Vector3:
	var locs := get_recommended_locations()
	var first := mini(slots.x, locs.size() - 1)
	var last := mini(slots.x + slots.y - 1, locs.size() - 1)
	if locs.is_empty():
		return Vector3.ZERO
	var z := 0.5 * (locs[first].z + locs[last].z)
	if slots.x + slots.y - 1 >= locs.size():
		z = locs[first].z + (float(slots.y) - 1.0) * 0.5 * slot_spacing()
	if _terrain_ref == null:
		return Vector3(locs[first].x, 0.0, z)
	var yaw := channel_yaw_at(z)
	var half := SiteLayout.half_extent(pen_count)
	var cx := _terrain_ref.channel_center_x(z)
	var best := Vector3(cx, 0.0, z)
	var best_score := -INF
	for offset in SITE_X_OFFSETS:
		var p := Vector3(cx + float(offset), 0.0, z)
		var depth := -_terrain_ref.get_height_at(p.x, p.z)
		var shallowest := depth
		for corner in [Vector2(-1, -1), Vector2(1, -1), Vector2(-1, 1), Vector2(1, 1)]:
			var q := p + Basis(Vector3.UP, yaw) * Vector3(corner.x * half.x, 0.0, corner.y * half.y)
			shallowest = minf(shallowest, -_terrain_ref.get_height_at(q.x, q.z))
		if depth < min_depth or depth > max_depth:
			continue
		# Whole grid over deep water first, then the deepest centre.
		var score := depth + (10000.0 if shallowest >= GRID_MIN_DEPTH_M else 0.0)
		if score > best_score:
			best_score = score
			best = p
	return best


# === Sim sync ===

func sync_pens_from_sim(sites: Array) -> void:
	"""Reconcile 3D sites with the player's sea sites from SimBridge.
	Sites are Dictionaries; id/name/biomass/health keys are read
	tolerantly since the C# payload shape may vary."""
	var wanted: Dictionary = {}
	var next_slot := 0
	var spacing := slot_spacing()
	for i in range(sites.size()):
		var site = sites[i]
		if not (site is Dictionary):
			continue
		var sid := _site_string(site, ["id", "site_id", "zone_id", "pen_id"], "site_%d" % i)
		var count := maxi(1, int(_site_float(site, ["pen_count", "pens"], 1.0)))
		var slots := Vector2i(next_slot, SiteLayout.slot_count(count, spacing))
		next_slot += slots.y
		wanted[sid] = true
		if sid in _pens and (int(_pens[sid]["pen_count"]) != count or _pens[sid]["slots"] != slots):
			remove_pen(sid)  # re-laid: the engine changed the site's pens
		if not (sid in _pens):
			_add_site(sid, count, slots, true, site)
		_apply_site_data(sid, site)
	# Remove sites the sim no longer has
	for pen_id in _pens.keys():
		if _pens[pen_id].get("from_sim", false) and not (pen_id in wanted):
			remove_pen(pen_id)


## A site's pen k as the sim reports it: the per-pen entry when the payload
## carries pens (S2-01b), otherwise the site's values with its fish shared
## evenly between its pens.
static func pen_payload(site: Dictionary, k: int, pen_count: int) -> Dictionary:
	var pens = site.get("pens")
	if pens is Array and k < pens.size() and pens[k] is Dictionary:
		return pens[k]
	var out := site.duplicate()
	if site.has("fish_count"):
		out["fish_count"] = _site_float(site, ["fish_count"], 0.0) / float(maxi(pen_count, 1))
	return out


func _apply_site_data(pen_id: String, site: Dictionary) -> void:
	if not (pen_id in _pens):
		return
	var pen_data: Dictionary = _pens[pen_id]
	pen_data["site_id"] = _site_string(site, ["id", "site_id", "zone_id"], pen_id)
	pen_data["from_sim"] = true
	var farm: FarmSite = pen_data["site"]
	if farm.is_ghost():
		farm.set_ghost(false)
		_set_site_extras_visible(pen_id, true)
	farm.label.text = _site_string(site, ["name", "pen_name", "id"], pen_id)
	farm.set_lice_skirt(bool(site.get("has_skirt", false)))
	var states := []
	for k in farm.pen_count:
		var pen := pen_payload(site, k, farm.pen_count)
		var state := health_state_for(pen)
		farm.get_cage(k).set_health_state(state)
		states.append(state)
		_apply_fish_density(pen_data["fish_schools"][k], pen_data, pen)
	pen_data["health_state"] = health_state_for(site)
	pen_data["pen_states"] = states
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


## A site's bird net: a centre-mast tent or blue rim poles with a green net
## (owner references 2/4 and 5), fixed per site id across runs.
static func bird_net_for(site_id: String) -> int:
	return PenCage.BirdNet.RIM if absi(hash(site_id)) % 2 == 1 else PenCage.BirdNet.TENT


func _add_site(pen_id: String, pen_count: int, slots: Vector2i, active: bool, site: Dictionary) -> void:
	"""Lay a site's grid of pens, its mooring, workboat and fish schools."""
	if _recommended_locations.is_empty():
		_generate_recommended_locations()
	var position := _site_position(slots, pen_count)
	var grid_yaw := channel_yaw_at(position.z)
	var half := SiteLayout.half_extent(pen_count)
	var barge_side := _choose_barge_side(position, grid_yaw, half.x)
	# NOTE: add_child() BEFORE touching global_position: a node outside the
	# tree cannot be placed (and the anchor lines look the seabed up).
	var farm := FarmSite.new()
	farm.name = pen_id
	markers_parent.add_child(farm)
	farm.global_position = position
	farm.rotation.y = grid_yaw
	farm.build(pen_id, pen_count, {
		"barge_side": barge_side,
		"anchor_floor": _seabed_lookup(),
		"bird_net": bird_net_for(pen_id),
		"label": _site_string(site, ["name", "pen_name", "id"], pen_id),
	})
	farm.pen_pressed.connect(_on_farm_pen_pressed)

	# The workboat lies broadside to the up-fjord pen of the first column,
	# inside its cell (the bridles run to the cell corners); its origin is
	# its waterline.
	var dims := SiteLayout.grid_dims(pen_count)
	var last_k := (dims.y - 1) * dims.x
	var boat_local: Vector3 = farm.pen_local_positions()[mini(last_k, pen_count - 1)] + Vector3(0.0, 0.0, BOAT_MOORING_OFFSET)
	var boat = service_boat_scene.instantiate()
	boat.name = "%s_boat" % pen_id
	markers_parent.add_child(boat)
	boat.global_position = farm.to_global(boat_local)
	boat.rotation.y = grid_yaw + PI * 0.5

	# One fish school per pen.
	var schools: Array[MultiMeshInstance3D] = []
	for k in pen_count:
		var school := MultiMeshInstance3D.new()
		school.name = "FishSchool%d" % (k + 1)
		school.multimesh = _create_fish_multimesh()
		school.visibility_range_end = FISH_VISIBLE_M
		farm.add_child(school)
		school.position = farm.pen_local_positions()[k]
		schools.append(school)

	_pens[pen_id] = {
		"position": position,
		"active": active,
		"node": farm.get_cage(0),
		"cage": farm.get_cage(0),
		"cages": farm.cages,
		"site": farm,
		"from_sim": false,
		"depth": -_terrain_ref.get_height_at(position.x, position.z) if _terrain_ref else 30.0,
		"boat": boat,
		"fish_school": schools[0],
		"fish_schools": schools,
		"mooring": farm.grid,
		"grid_yaw": grid_yaw,
		"barge_side": barge_side,
		"pen_count": pen_count,
		"slots": slots,
	}
	if not active:
		farm.set_ghost(true)
		_set_site_extras_visible(pen_id, false)
	pen_added.emit(pen_id, position)


func _set_site_extras_visible(pen_id: String, on: bool) -> void:
	var boat = _pens[pen_id].get("boat")
	if boat is Node3D:
		(boat as Node3D).visible = on
	for school in _pens[pen_id].get("fish_schools", []):
		(school as Node3D).visible = on


## Seabed height lookup for mooring anchor lines (world x, z -> y), or an
## empty Callable when there is no terrain.
func _seabed_lookup() -> Callable:
	if _terrain_ref == null:
		_terrain_ref = get_node_or_null("../Terrain") as FjordTerrain
	if _terrain_ref == null:
		return Callable()
	var terrain := _terrain_ref
	return func(x: float, z: float) -> float: return terrain.get_height_at(x, z)


## The frame mooring of a site (null for an unknown site).
func get_pen_mooring(pen_id: String) -> MooringGrid:
	if pen_id in _pens:
		return _pens[pen_id].get("mooring") as MooringGrid
	return null


## A site's FarmSite (null for an unknown site).
func get_site(pen_id: String) -> FarmSite:
	return _pens[pen_id].get("site") as FarmSite if pen_id in _pens else null


## A site's pens, pen 1 first (empty for an unknown site).
func get_site_cages(pen_id: String) -> Array[PenCage]:
	var out: Array[PenCage] = []
	var farm := get_site(pen_id)
	if farm != null:
		out.assign(farm.cages)
	return out


## The site's name label over its grid (null for an unknown site).
func get_site_label(pen_id: String) -> Label3D:
	var farm := get_site(pen_id)
	return farm.label if farm != null else null


## The feed barge is moored at its berth: run its hoses to every pen.
func attach_feed_barge(pen_id: String, barge: Node3D) -> void:
	var farm := get_site(pen_id)
	if farm == null or barge == null:
		return
	var model := barge as VesselModel
	farm.attach_hoses(barge.global_position, model.length_m if model else 37.0, model.beam_m if model else 11.0)


## The feed barge leaves: its hoses go with it.
func detach_feed_barge(pen_id: String) -> void:
	var farm := get_site(pen_id)
	if farm != null:
		farm.detach_hoses()


# === Site layout: grid heading, berths, room for passing boats ===

## Heading (yaw about +Y) of the fjord channel at world z: the direction a
## site's mooring grid is laid in and its boats lie (0 without terrain).
func channel_yaw_at(z: float) -> float:
	if _terrain_ref == null:
		_terrain_ref = get_node_or_null("../Terrain") as FjordTerrain
	if _terrain_ref == null:
		return 0.0
	var dx := _terrain_ref.channel_center_x(z + CHANNEL_HEADING_SPAN_M) - _terrain_ref.channel_center_x(z - CHANNEL_HEADING_SPAN_M)
	return atan2(dx, 2.0 * CHANNEL_HEADING_SPAN_M)


## The world-x side (+1 or -1) a site's feed barge moors on; its wellboat
## berth is the other side.
func _choose_barge_side(position: Vector3, grid_yaw: float, half_w: float) -> float:
	if _terrain_ref == null:
		_terrain_ref = get_node_or_null("../Terrain") as FjordTerrain
	if _terrain_ref == null:
		return 1.0
	var to_fairway := _terrain_ref.channel_center_x(position.z) - position.x
	var wellboat_side := signf(to_fairway)
	if absf(to_fairway) < 1.0:
		wellboat_side = 1.0 if _berth_depth(position, grid_yaw, 1.0, half_w) >= _berth_depth(position, grid_yaw, -1.0, half_w) else -1.0
	var barge_side := -wellboat_side
	if _berth_depth(position, grid_yaw, barge_side, half_w) < BERTH_MIN_DEPTH_M \
			and _berth_depth(position, grid_yaw, wellboat_side, half_w) >= BERTH_MIN_DEPTH_M:
		barge_side = wellboat_side
	return barge_side


## The shallowest water (m) under a berth side's footprint.
func _berth_depth(position: Vector3, grid_yaw: float, side: float, half_w: float) -> float:
	var across := Vector3(cos(grid_yaw), 0.0, -sin(grid_yaw)) * side
	var along := Vector3(sin(grid_yaw), 0.0, cos(grid_yaw))
	var shallowest := INF
	for reach in [half_w + WELLBOAT_GRID_GAP_M, half_w + BARGE_GRID_GAP_M + BARGE_RESERVE_BEAM_M * 0.5,
			half_w + WELLBOAT_GRID_GAP_M + WELLBOAT_RESERVE_BEAM_M]:
		for t in [-1.0, 0.0, 1.0]:
			var q: Vector3 = position + across * float(reach) + along * (BERTH_SAMPLE_REACH_M * float(t))
			shallowest = minf(shallowest, -_terrain_ref.get_height_at(q.x, q.z))
	return shallowest


## Where a vessel of `beam_m` lies at a site: role "barge" (beside the grid,
## its hoses to every pen) or "wellboat" (alongside, the other side).
## {position: a world point level with the site centre in z, so it can end
## a channel route; axis: the grid's +Z, the way the vessel lies; side: +1
## or -1 in world x}. Empty for an unknown site.
func get_berth(pen_id: String, role: String, beam_m: float) -> Dictionary:
	if not (pen_id in _pens):
		return {}
	var data: Dictionary = _pens[pen_id]
	var yaw := float(data.get("grid_yaw", 0.0))
	var barge_side := float(data.get("barge_side", 1.0))
	var half_w := SiteLayout.half_extent(int(data.get("pen_count", 1))).x
	var side := barge_side if role == "barge" else -barge_side
	var hull_side := half_w + (BARGE_GRID_GAP_M if role == "barge" else WELLBOAT_GRID_GAP_M)
	# The perpendicular distance to the hull's centreline, laid off along x.
	var reach := (hull_side + beam_m * 0.5) / cos(yaw)
	var at: Vector3 = data["position"]
	return {
		"position": Vector3(at.x + side * reach, 0.0, at.z),
		"axis": Vector3(sin(yaw), 0.0, cos(yaw)),
		"side": side,
	}


## The water every site keeps clear for passing boats: its mooring grid and
## both berths (the wellboat's for the widest hull), as a band of world x at
## the site's z that shifts with the grid's heading along z. Pass a site and
## a role to leave that berth open (a boat heading for it).
## [{z, x_per_z, lo, hi, half_z}]
func site_zones(open_pen_id: String = "", open_role: String = "") -> Array:
	var zones: Array = []
	for pen_id in _pens:
		var data: Dictionary = _pens[pen_id]
		var at: Vector3 = data["position"]
		var yaw := float(data.get("grid_yaw", 0.0))
		var barge_side := float(data.get("barge_side", 1.0))
		var half := SiteLayout.half_extent(int(data.get("pen_count", 1)))
		var barge_reach := half.x + BARGE_GRID_GAP_M + BARGE_RESERVE_BEAM_M
		var wellboat_reach := half.x + WELLBOAT_GRID_GAP_M + WELLBOAT_RESERVE_BEAM_M
		if pen_id == open_pen_id and open_role == "barge":
			barge_reach = half.x + BARGE_GRID_GAP_M - OPEN_BERTH_SLACK_M
		if pen_id == open_pen_id and open_role == "wellboat":
			wellboat_reach = half.x + WELLBOAT_GRID_GAP_M - OPEN_BERTH_SLACK_M
		var plus := (barge_reach if barge_side > 0.0 else wellboat_reach) / cos(yaw)
		var minus := (wellboat_reach if barge_side > 0.0 else barge_reach) / cos(yaw)
		zones.append({
			"z": at.z,
			"x_per_z": tan(yaw),
			"lo": at.x - minus,
			"hi": at.x + plus,
			"half_z": maxf(SITE_RESERVE_HALF_LENGTH_M, half.y + SITE_RESERVE_END_M),
		})
	return zones


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


## Blender-built Atlantic salmon (blender/build_salmon.py, 0.9 m fork length,
## nose along -Z): LOD0 (~1.5k triangles) close to the camera, LOD1 (~300)
## for a school seen from the farm camera. Falls back to a plain box if the
## import is missing.
const SALMON_LOD_SCENES := ["res://assets/models/fish/salmon_LOD0.glb", "res://assets/models/fish/salmon_LOD1.glb"]
## Camera distance (m) from a pen where its school swaps to the far mesh.
const FISH_LOD_SWITCH := 70.0
## Past this camera distance (m) a school is not drawn (it swims under a
## net metres down) and not animated.
const FISH_VISIBLE_M := 450.0
var _salmon_meshes: Dictionary = {}  # lod -> Mesh

func _get_salmon_mesh(lod: int = 1) -> Mesh:
	if _salmon_meshes.has(lod):
		return _salmon_meshes[lod]
	var mesh: Mesh = null
	var packed: PackedScene = load(SALMON_LOD_SCENES[clampi(lod, 0, 1)]) as PackedScene
	if packed != null:
		var inst: Node = packed.instantiate()
		var mi := inst.find_child("Salmon", true, false) as MeshInstance3D
		if mi and mi.mesh:
			mesh = mi.mesh
		inst.free()
	if mesh == null:
		var body := BoxMesh.new()
		body.size = Vector3(0.25, 0.3, 0.9)
		var mat := StandardMaterial3D.new()
		mat.albedo_color = Color(0.5, 0.58, 0.65, 1.0)
		mat.roughness = 0.6
		body.material = mat
		mesh = body
	_salmon_meshes[lod] = mesh
	return mesh


## Close schools get the detailed salmon, far ones the light mesh (one
## MultiMesh per pen: only its mesh changes, the transforms stay).
func _update_fish_lod(school: MultiMeshInstance3D, cam: Camera3D = null) -> void:
	if cam == null and is_inside_tree():
		cam = get_viewport().get_camera_3d()
	if cam == null or school.multimesh == null:
		return
	var lod := 0 if cam.global_position.distance_to(school.global_position) < FISH_LOD_SWITCH else 1
	var mesh := _get_salmon_mesh(lod)
	if school.multimesh.mesh != mesh:
		school.multimesh.mesh = mesh


func _create_fish_multimesh() -> MultiMesh:
	"""One shared low-poly fish school mesh per pen (modest count)."""
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = _get_salmon_mesh(1)
	# Allocated once at max; _apply_fish_density drives visible count.
	mm.instance_count = FISH_MAX_INSTANCES
	mm.visible_instance_count = 0
	return mm


## Visible fish scale with the sim: count follows stocking, body length
## follows mean weight (L(cm) = (100*W_g/1.1)^(1/3), body mesh is 0.9 m).
## The MultiMesh is allocated once at FISH_MAX_INSTANCES; only
## visible_instance_count moves (changing instance_count resets transforms).
const FISH_MAX_INSTANCES := 240

func _apply_fish_density(school: MultiMeshInstance3D, pen_data: Dictionary, pen: Dictionary) -> void:
	if not school or not school.multimesh:
		return
	var fish_count := int(_site_float(pen, ["fish_count"], 0.0))
	var mean_w := _site_float(pen, ["mean_weight_kg"], 0.0)
	school.multimesh.visible_instance_count = clampi(fish_count / 250, 0, FISH_MAX_INSTANCES)
	# Body length from weight; clamp to sane bounds (smolt .. harvest).
	var length_m := 0.2
	if mean_w > 0.0:
		length_m = clampf(pow(100.0 * mean_w * 1000.0 / 1.1, 1.0 / 3.0) / 100.0, 0.15, 1.0)
	pen_data["fish_scale"] = length_m / 0.9


func _animate_fish_school(school: MultiMeshInstance3D, pen_data: Dictionary, key: String, cam: Camera3D) -> void:
	if not school or not school.multimesh or not school.visible:
		return
	if cam != null and cam.global_position.distance_squared_to(school.global_position) > FISH_VISIBLE_M * FISH_VISIBLE_M:
		return
	_update_fish_lod(school, cam)
	var count: int = school.multimesh.visible_instance_count
	var fish_scale: float = pen_data.get("fish_scale", 0.5)
	var phase := float(abs(hash(key)) % 1000) / 1000.0 * TAU
	for i in range(count):
		var t := _fish_time + phase + float(i) * 0.35
		var r := fish_swim_radius * (0.55 + 0.45 * float(i % 3) / 2.0)
		var depth := -3.0 - float(i % 4) * 2.0
		var pos := Vector3(cos(t) * r, depth + sin(t * 1.7) * 0.6, sin(t) * r)
		# Tangential heading: velocity of the circle path; the salmon model's
		# nose points along local -Z (Blender +Y -> Godot -Z on import).
		var vel := Vector3(-sin(t), 0.0, cos(t))
		var basis := Basis.looking_at(vel, Vector3.UP).scaled(Vector3.ONE * fish_scale)
		school.multimesh.set_instance_transform(i, Transform3D(basis, pos))


## The colour a site's health ring shows: pen 1's ("" or unknown site:
## COLOR_DEFAULT).
func get_pen_health_color(pen_id: String) -> Color:
	if pen_id in _pens:
		var cage = _pens[pen_id]["cage"]
		if cage is PenCage:
			return (cage as PenCage).get_health_color()
	return COLOR_DEFAULT


func _on_farm_pen_pressed(farm: FarmSite, pen_index: int, _at: Vector3) -> void:
	var pen_id := farm.site_id
	if not _pens.has(pen_id):
		return  # a removed site's queued click
	select_pen(pen_id, pen_index)
	pen_clicked.emit(pen_id, farm.pen_rest_position(pen_index))


func select_pen(pen_id: String, pen_index: int = -1) -> void:
	"""Select a site (and one of its pens): the pen gets the bright ring on
	the water, the site's other pens a quiet one; listeners are told."""
	if not (pen_id in _pens):
		return
	var farm: FarmSite = _pens[pen_id]["site"]
	selected_pen_id = pen_id
	selected_pen_index = clampi(pen_index, -1, farm.pen_count - 1)
	if active_indicator:
		# Flat on the water around the collar, not floating above it.
		var cage := farm.get_cage(maxi(selected_pen_index, 0))
		var at := farm.pen_rest_position(maxi(selected_pen_index, 0))
		active_indicator.visible = true
		active_indicator.global_position = Vector3(at.x, cage.global_position.y + SELECTION_RING_Y, at.z)
	if site_indicator:
		site_indicator.visible = farm.pen_count > 1
		_place_site_rings(farm)
	pen_selection_changed.emit(pen_id)
	cage_selected.emit(pen_id, selected_pen_index)


## Quiet rings round the selected site's pens, all but the selected one
## (collapsed), on the water as it rides.
func _place_site_rings(farm: FarmSite) -> void:
	var xforms: Array[Transform3D] = []
	for k in farm.pen_count:
		var cage := farm.get_cage(k)
		var at := farm.pen_rest_position(k)
		var shown := k != maxi(selected_pen_index, 0)
		var basis := Basis.IDENTITY if shown else Basis.IDENTITY.scaled(Vector3.ZERO)
		xforms.append(Transform3D(basis, Vector3(at.x, cage.global_position.y + SELECTION_RING_Y, at.z)))
	MultiMeshPack.upload(site_indicator.multimesh, xforms)


# === Public API ===

func add_pen(pen_id: String, position: Vector3) -> bool:
	"""Add a one-pen placeholder site at the specified position."""
	if _pens.size() >= max_pens:
		push_warning("[FishPenManager] Maximum pens (%d) reached" % max_pens)
		return false

	if pen_id in _pens:
		push_warning("[FishPenManager] Pen %s already exists" % pen_id)
		return false

	_add_site(pen_id, 1, Vector2i(_pens.size(), 1), true, {})
	_pens[pen_id]["site"].global_position = position
	_pens[pen_id]["position"] = position
	return true


func remove_pen(pen_id: String) -> bool:
	"""Remove a site from the scene."""
	if not pen_id in _pens:
		return false

	var pen_data = _pens[pen_id]
	if pen_data["site"]:
		pen_data["site"].queue_free()
	if pen_data["boat"]:
		pen_data["boat"].queue_free()
	_pens.erase(pen_id)
	if pen_id == selected_pen_id:
		selected_pen_id = ""
		selected_pen_index = -1
		if active_indicator:
			active_indicator.visible = false
		if site_indicator:
			site_indicator.visible = false

	pen_removed.emit(pen_id)
	return true


func get_pen_position(pen_id: String) -> Vector3:
	"""The world position of a site: the centre of its grid."""
	if pen_id in _pens:
		return _pens[pen_id]["position"]
	return Vector3.ZERO


func get_active_pen_count() -> int:
	"""Get number of active sites."""
	var count = 0
	for pen_data in _pens.values():
		if pen_data.active:
			count += 1
	return count


func get_pen_ids() -> Array[String]:
	"""Get list of all site IDs."""
	var ids: Array[String] = []
	for pen_id in _pens.keys():
		ids.append(pen_id)
	return ids


func get_recommended_locations() -> Array[Vector3]:
	"""Deep-water slots along the fjord channel (generated on first use).
	The player's sites fill them from the start; rival farms from the end."""
	if _recommended_locations.is_empty():
		_generate_recommended_locations()
	return _recommended_locations.duplicate()


func get_available_slots() -> int:
	"""Get number of free channel slots."""
	return max_pens - slots_used()


func refresh_pen_positions() -> void:
	"""Update the sites' depth record after terrain regeneration (the
	collars float on the water; only the nets hang below)."""
	if not _terrain_ref:
		return
	for pen_id in _pens:
		var pen_data = _pens[pen_id]
		var pos: Vector3 = pen_data["position"]
		pen_data["depth"] = -_terrain_ref.get_height_at(pos.x, pos.z)


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
	"""Get detailed information about a site."""
	if not pen_id in _pens:
		return {}

	var pen_data = _pens[pen_id]
	return {
		"id": pen_id,
		"position": pen_data["position"],
		"depth": pen_data["depth"],
		"active": pen_data["active"],
		"pen_count": pen_data["pen_count"],
	}
