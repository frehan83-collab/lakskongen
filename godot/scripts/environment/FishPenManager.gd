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

# === Internal State ===
## site_id -> {position, active, node, cage, cages, site, from_sim, depth,
## boat, fish_school, fish_schools, mooring, grid_yaw, barge_side,
## pen_count, slots, is_feeding}. "node"/"cage"/"fish_school" are pen 1's.
var _pens: Dictionary = {}
var _terrain_ref: FjordTerrain = null
var _recommended_locations: Array[Vector3] = []
## The feed barge feeds a site's pens in turn (G3-01): FEED_SLOT_S seconds a
## pen, in daylight, while the sim feeds the site.
const FEED_SLOT_S := 24.0
const FEED_MIN_SUN_DEG := 2.0
var _feed_clock: float = 0.0
## Set false to hide every school (the harness's fish A/B).
var fish_shown := true

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
## A site was laid out again (the engine changed its pens): its berths moved.
signal site_relaid(site_id: String)

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
const SITE_SELECTION_COLOR := Color(0.910, 0.933, 0.941, 0.3)
## The workboat lies broadside to the collar of the up-fjord pen of the
## first column this far from its centre (outer pipe edge at 26.8 m, half
## the boat's 10 m beam, the tyre fenders and a 1.4 m gap), inside its cell.
const BOAT_MOORING_OFFSET := 33.6

# === Site layout === (berths and boat lanes: SiteBerths.gd)
const BARGE_GRID_GAP_M := SiteBerths.BARGE_GRID_GAP_M
const WELLBOAT_GRID_GAP_M := SiteBerths.WELLBOAT_GRID_GAP_M
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
	# The site's and the boats' lights follow the dusk and the eye's exposure.
	var director := get_node_or_null("../EnvironmentDirector") as EnvironmentDirector
	SiteLights.sync(director, get_tree())
	if _pens.is_empty():
		return
	# The floating gear samples the water bake the water does.
	if _terrain_ref != null and _terrain_ref.water_bake_texture != SiteFloat.bake_texture:
		SiteFloat.bake_texture = _terrain_ref.water_bake_texture
	SiteFloat.sync()
	FishSchool.sync(director.water_material() if director else null, SiteLights.fog_density(director))
	_feed_clock += delta
	var daylight := director == null or float(director.get_composed().get("sun_elevation", 30.0)) > FEED_MIN_SUN_DEG
	var cam := get_viewport().get_camera_3d() if is_inside_tree() else null
	for pen_id in _pens:
		var schools: Array = _pens[pen_id]["fish_schools"]
		var fed := feeding_pen(pen_id) if daylight else -1
		for k in schools.size():
			FishSchool.set_feeding(schools[k], k == fed)
			FishSchool.animate(schools[k], delta, cam)
	_follow_selected_pen()


## The pen of a site the feed barge is feeding now (0-based), or -1: the
## sim feeds the site ("is_feeding") and the barge serves its pens in turn,
## FEED_SLOT_S seconds each.
func feeding_pen(pen_id: String) -> int:
	if not (pen_id in _pens) or not bool(_pens[pen_id].get("is_feeding", false)):
		return -1
	return int(_feed_clock / FEED_SLOT_S) % maxi(int(_pens[pen_id]["pen_count"]), 1)


## Show or hide every school (the harness's fish A/B).
func set_fish_shown(on: bool) -> void:
	fish_shown = on
	for pen_id in _pens:
		var farm: FarmSite = _pens[pen_id]["site"]
		for school in _pens[pen_id]["fish_schools"]:
			(school as Node3D).visible = on and not farm.is_ghost()


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
		var relaid := false
		if sid in _pens and (int(_pens[sid]["pen_count"]) != count or _pens[sid]["slots"] != slots):
			remove_pen(sid)  # re-laid: the engine changed the site's pens
			relaid = true
		if not (sid in _pens):
			_add_site(sid, count, slots, true, site)
			if relaid:
				site_relaid.emit(sid)
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


## What a pen's school needs from the sim: the pen's fish, weight and so its
## biomass, the site's oxygen and lice skirt.
static func school_payload(site: Dictionary, pen: Dictionary) -> Dictionary:
	var count := _site_float(pen, ["fish_count"], 0.0)
	var weight := _site_float(pen, ["mean_weight_kg"], _site_float(site, ["mean_weight_kg"], 0.0))
	return {
		"fish_count": count,
		"mean_weight_kg": weight,
		"biomass_kg": count * weight,
		"oxygen_mgl": _site_float(pen, ["oxygen_mgl"], _site_float(site, ["oxygen_mgl"], NAN)),
		"has_skirt": bool(site.get("has_skirt", false)),
	}


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
		FishSchool.apply_pen(pen_data["fish_schools"][k], school_payload(site, pen))
	pen_data["health_state"] = health_state_for(site)
	pen_data["pen_states"] = states
	pen_data["is_feeding"] = bool(site.get("is_feeding", false))
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
	var barge_side := SiteBerths.choose_barge_side(_terrain(), position, grid_yaw, half.x)
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

	# One fish school per pen, riding the water with its collar.
	var schools: Array[MultiMeshInstance3D] = []
	for k in pen_count:
		var school := FishSchool.create("FishSchool%d" % (k + 1), "%s#%d" % [pen_id, k])
		farm.get_cage(k).add_child(school)
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
		(school as Node3D).visible = on and fish_shown


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

func _terrain() -> FjordTerrain:
	if _terrain_ref == null:
		_terrain_ref = get_node_or_null("../Terrain") as FjordTerrain
	return _terrain_ref


## Heading (yaw about +Y) of the fjord channel at world z: the direction a
## site's mooring grid is laid in and its boats lie (0 without terrain).
func channel_yaw_at(z: float) -> float:
	return SiteBerths.channel_yaw(_terrain(), z)


## Where a vessel of `beam_m` lies at a site (SiteBerths.berth): role
## "barge" or "wellboat". Empty for an unknown site.
func get_berth(pen_id: String, role: String, beam_m: float) -> Dictionary:
	if not (pen_id in _pens):
		return {}
	return SiteBerths.berth(_pens[pen_id], role, beam_m)


## The water every site keeps clear for passing boats (SiteBerths.zone);
## pass a site and a role to leave that berth open (a boat heading for it).
## [{z, x_per_z, lo, hi, half_z}]
func site_zones(open_pen_id: String = "", open_role: String = "") -> Array:
	var zones: Array = []
	for pen_id in _pens:
		zones.append(SiteBerths.zone(_pens[pen_id], open_role if pen_id == open_pen_id else ""))
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


## A school's mesh LOD for the camera (the UI-flow tests drive it directly).
func _update_fish_lod(school: MultiMeshInstance3D, cam: Camera3D = null) -> void:
	if cam == null and is_inside_tree():
		cam = get_viewport().get_camera_3d()
	FishSchool.update_lod(school, cam)


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
