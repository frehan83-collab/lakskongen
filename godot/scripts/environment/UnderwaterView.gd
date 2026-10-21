class_name UnderwaterView
extends Node3D
## The world under the surface (roadmap G3-04): what the camera finds when the
## player zooms past the pen into the water. A presenter: it reads the
## camera, the pens and the sim's water (FjordVistaBridge hands it each
## site's details) and hands the EnvironmentDirector the water the camera is
## in; the director composes the fog, the light and the surface's underside
## from it. It writes no environment, sky, light or water uniform itself.
##
##   dive_anchor()   FjordCamera.underwater_anchor: the pen nearest a point,
##                   so the UNDERWATER view stands inside that pen's net;
##   site waters     GetSeaSiteDetails' water_visibility_m and
##                   bloom_chlorophyll_ug_l per site (the sim's
##                   WaterClarity): the water at the camera is its site's;
##   the lens        UnderwaterCanvas/UnderwaterRect (underwater_lens
##                   .gdshader): a soft vignette under water and a brief
##                   veil as the camera crosses the surface. It reads no
##                   screen texture (it replaced underwater_post, a blur,
##                   tint and caustic pattern painted over the screen).

## The underwater view dives into the pen nearest the focus when it is this
## close (m): the focus on a pen, or on its site's grid between pens.
const ANCHOR_REACH_M := 120.0
## The net's radius (m): the school swims inside it (fish_school.gdshaderinc
## FISH_NET_R, the A2-01 pen's 25.5 m collar less the net's offset).
const NET_RADIUS_M := 24.95
## A site's water reaches this far from its grid's centre (m); beyond it
## the camera is in open water and takes the nearest site's within
## OPEN_WATER_REACH_M (a fjord site's, or an offshore unit's round its
## plot's slot: wave4b int review, the units took the fallback 5-83 km out),
## else UnderwaterOptics.FALLBACK_VISIBILITY_M. The offshore units' water is
## read from the sim (GameBridge.GetAllSeaSiteDetails) only while the view
## is under water away from the fjord's sites, at most every
## OFFSHORE_REFRESH_S (the fjord's comes with the vista's daily sync).
const OPEN_WATER_REACH_M := 1500.0
const OFFSHORE_REFRESH_S := 1.0
## The veil as the camera crosses the surface: up in a moment, gone in
## VEIL_FADE_S. On the way up it comes up before the crossing: over the last
## VEIL_BAND_M below the depth where the grid stops wearing the underside
## material (EnvironmentDirector.UNDERSIDE_FROM_M), where the water material's
## own underside shows a flat bright blue (wave4b int review). Its colour is
## the water's (the fog's last colour under the surface), both ways.
const VEIL_FADE_S := 0.45
const VEIL_BAND_M := 0.8
## The dive passes through the bird net over the pen (wave4b int review: the
## roof's threads filled the frame as the camera dropped through them).
## While the view dives, is under water or is on its way out
## (DIVE_LINGER_S after it was last under), the bird net of the pen it
## dives into (the camera's own anchor) dissolves with the camera's place:
## gone within BIRD_NET_NEAR_M.x of the pen's centre and under
## BIRD_NET_FADE_M.x over the water (over the tent's 7.5 m mast top), whole
## beyond BIRD_NET_NEAR_M.y or over BIRD_NET_FADE_M.y, smooth between, the
## same way down and up (the pen's far LODs, drawn by the site, never fade:
## it is whole again before the camera is that far).
const BIRD_NET_NEAR_M := Vector2(30.0, 45.0)
const BIRD_NET_FADE_M := Vector2(8.5, 25.0)
const DIVE_LINGER_S := 3.0
## The lens's vignette under water (share of the corner darkened).
const VIGNETTE := 0.32

## Light shafts (shaders/underwater/light_shaft.gdshader): beams leaving the
## surface on a jittered world grid of SHAFT_CELL_M cells (one shaft a cell,
## where the cell's hash puts it, so a shaft stays put while the camera
## moves), drawn for the cells within SHAFT_RADIUS_M of the camera (the
## water hides the rest), SHAFT_WIDTH_M wide and SHAFT_LENGTH_M long, laid
## along the sunlight in the water. A shaft's core adds SHAFT_CONTRAST times
## the water's own horizontal light at its source, dimmed by the water
## between it and the camera at SHAFT_VEIL of the fog's attenuation (a
## bright beam, like Snell's window, keeps its shape through the forward
## scattering that hides a dark object), with a sun above SHAFT_SUN_DEG;
## the shafts fade as the cloud closes (SHAFT_CLOUD). Art direction over
## strict optics: a beam 5 m off in 12 m water would hold 6 % contrast.
const SHAFT_CELL_M := 3.0
const SHAFT_RADIUS_M := 16.0
const SHAFT_COUNT := 96
const SHAFT_WIDTH_M := Vector2(0.2, 1.8)
const SHAFT_LENGTH_M := Vector2(8.0, 22.0)
const SHAFT_CONTRAST := 5.0
const SHAFT_VEIL := 0.45
const SHAFT_SUN_DEG := Vector2(4.0, 18.0)
const SHAFT_CLOUD := Vector2(0.35, 0.85)
const SHAFT_SEED := 20261021
## Marine snow (shaders/underwater/marine_snow.gdshader): flakes in a box
## round the camera, sinking slowly and drifting with the water; more of
## them in a bloom. They scatter SNOW_SHARE of the light at their depth.
const SNOW_AMOUNT := 4000
const SNOW_BOX := Vector3(8.0, 5.0, 8.0)
const SNOW_LIFETIME_S := 12.0
const SNOW_SIZE_M := 0.03
const SNOW_SHARE := 0.16
const SNOW_CLEAR_RATIO := 0.55
const SHAFT_SHADER := preload("res://shaders/underwater/light_shaft.gdshader")
const SNOW_SHADER := preload("res://shaders/underwater/marine_snow.gdshader")

@export var camera: FjordCamera
@export var director: EnvironmentDirector
## The full-screen lens overlay (its material is underwater_lens.gdshader).
@export var lens_rect: ColorRect

## site id -> {"visibility_m", "chlorophyll_ug_l"} (set_site_waters).
var _site_waters: Dictionary = {}
## Offshore unit id -> {"plot", "visibility_m", "chlorophyll_ug_l"} (the
## sim's rows of kind "Offshore", _read_offshore); the plots' slot
## positions, looked up once (_slot_positions).
var _units: Dictionary = {}
var _slots: Dictionary = {}
var _offshore_read_at := -INF
var _pens: FishPenManager = null
var _was_under := false
var _veil := 0.0
## The water's colour for the veil (the fog the director composed last under
## the surface).
var _water_color := Color(0.05, 0.12, 0.12)
## The pen whose bird net the dive fades, and how much of it shows (0-1).
var bird_net_cage: PenCage = null
var bird_net_fade := 1.0
var _dive_linger := 0.0
var _shaft_time := 0.0
var _shaft_centre := Vector3.INF
## What the director was last handed (tests read it).
var water_now := {"visibility_m": UnderwaterOptics.FALLBACK_VISIBILITY_M, "chlorophyll_ug_l": 0.0, "site_id": ""}
## The light shafts and the marine snow (made at _ready; tests read them).
var shafts: MultiMeshInstance3D = null
var snow: GPUParticles3D = null
## Staging (the harness's caustics measurement): the shafts and the snow held
## off (stage_effects).
var effects_staged_off := false


func _ready() -> void:
	if camera != null:
		camera.underwater_anchor = dive_anchor
	shafts = _make_shafts()
	add_child(shafts)
	snow = _make_snow()
	add_child(snow)


func _process(delta: float) -> void:
	if camera == null:
		return
	var under := is_camera_under()
	if under != _was_under:
		_was_under = under
		_veil = 1.0
		if under and snow != null:
			# Fill the water round the camera at once (the snow's preprocess).
			snow.restart()
	_veil = move_toward(_veil, 0.0, delta / VEIL_FADE_S)
	if under:
		var depth := _water_level() - camera.global_position.y
		_veil = maxf(_veil, 1.0 - smoothstep(EnvironmentDirector.UNDERSIDE_FROM_M,
			EnvironmentDirector.UNDERSIDE_FROM_M + VEIL_BAND_M, depth))
	if under or camera.is_underwater_view():
		var at := camera.global_position
		if dive_anchor(at).is_empty() and _nearest_site(at) == "":
			_read_offshore()
		_hand_water(water_at(at))
	_track_water_color()
	_update_lens(under)
	_update_effects(under, delta)
	_update_bird_net(under, delta)


## True while the camera is under the surface.
func is_camera_under() -> bool:
	return camera != null and camera.global_position.y < _water_level()


## The pen nearest a world point within ANCHOR_REACH_M: {"center" (its
## centre on the water), "radius" (the net's), "site_id", "pen" (0-based)},
## or {} when none is near or no pens are drawn.
func dive_anchor(point: Vector3) -> Dictionary:
	var pens := _pen_manager()
	if pens == null:
		return {}
	var best := {}
	var best_d := ANCHOR_REACH_M
	for site_id in pens.get_pen_ids():
		var farm := pens.get_site(site_id)
		if farm == null or farm.is_ghost():
			continue
		var k := farm.nearest_pen(point)
		var centre := farm.pen_rest_position(k)
		var d := Vector2(centre.x - point.x, centre.z - point.z).length()
		if d < best_d:
			best_d = d
			best = {"center": Vector3(centre.x, 0.0, centre.z), "radius": NET_RADIUS_M, "site_id": site_id, "pen": k}
	return best


## Each site's water from the sim (FjordVistaBridge, once a sim day): the
## sites' details (GetSeaSiteDetails rows: id, water_visibility_m,
## bloom_chlorophyll_ug_l). Rows without the numbers are skipped.
func set_site_waters(sites: Array) -> void:
	var waters := {}
	for row in sites:
		if not (row is Dictionary) or not row.has("water_visibility_m"):
			continue
		waters[str(row.get("id", ""))] = {
			"visibility_m": float(row["water_visibility_m"]),
			"chlorophyll_ug_l": float(row.get("bloom_chlorophyll_ug_l", 0.0)),
		}
	_site_waters = waters


## The offshore units' water from the sim's rows (GetAllSeaSiteDetails, or
## the rows a test hands in): kind "Offshore" with a plot.
func set_offshore_waters(rows: Array) -> void:
	var units := {}
	for row in rows:
		if not (row is Dictionary) or not row.has("water_visibility_m"):
			continue
		if str(row.get("kind", "")) != "Offshore" or str(row.get("plot_id", "")) == "":
			continue
		units[str(row.get("id", ""))] = {"plot": str(row["plot_id"]),
			"visibility_m": float(row["water_visibility_m"]),
			"chlorophyll_ug_l": float(row.get("bloom_chlorophyll_ug_l", 0.0))}
	_units = units


## The water at a world point: its pen's site's, else the nearest site's
## within OPEN_WATER_REACH_M, else the fallback.
func water_at(point: Vector3) -> Dictionary:
	var anchor := dive_anchor(point)
	var site_id := str(anchor.get("site_id", ""))
	if site_id == "" or not _site_waters.has(site_id):
		site_id = _nearest_site(point)
	if site_id != "" and _site_waters.has(site_id):
		var w: Dictionary = _site_waters[site_id]
		return {"visibility_m": w["visibility_m"], "chlorophyll_ug_l": w["chlorophyll_ug_l"], "site_id": site_id}
	var unit_id := _nearest_unit(point)
	if unit_id != "":
		var u: Dictionary = _units[unit_id]
		return {"visibility_m": u["visibility_m"], "chlorophyll_ug_l": u["chlorophyll_ug_l"], "site_id": unit_id}
	return {"visibility_m": UnderwaterOptics.FALLBACK_VISIBILITY_M, "chlorophyll_ug_l": 0.0, "site_id": ""}


## The offshore unit whose plot's slot is nearest a point within
## OPEN_WATER_REACH_M ("" for none).
func _nearest_unit(point: Vector3) -> String:
	var slots := _slot_positions()
	var best := ""
	var best_d := OPEN_WATER_REACH_M
	for unit_id in _units:
		var plot := str(_units[unit_id]["plot"])
		if not slots.has(plot):
			continue
		var at: Vector3 = slots[plot]
		var d := Vector2(at.x - point.x, at.z - point.z).length()
		if d < best_d:
			best_d = d
			best = str(unit_id)
	return best


## Under water away from the fjord's sites: the offshore units' water from
## the sim, at most every OFFSHORE_REFRESH_S.
func _read_offshore() -> void:
	var now := Time.get_ticks_msec() / 1000.0
	if now - _offshore_read_at < OFFSHORE_REFRESH_S:
		return
	_offshore_read_at = now
	var gb := get_node_or_null("/root/GameBridge")
	if gb != null and gb.has_method("GetAllSeaSiteDetails"):
		set_offshore_waters(gb.GetAllSeaSiteDetails())


func _nearest_site(point: Vector3) -> String:
	var pens := _pen_manager()
	if pens == null:
		return ""
	var best := ""
	var best_d := OPEN_WATER_REACH_M
	for site_id in pens.get_pen_ids():
		if not _site_waters.has(site_id):
			continue
		var at := pens.get_pen_position(site_id)
		var d := Vector2(at.x - point.x, at.z - point.z).length()
		if d < best_d:
			best_d = d
			best = site_id
	return best


## The offshore slots' positions (FjordTerrain.offshore_slots), plot id ->
## world position, looked up once.
func _slot_positions() -> Dictionary:
	if _slots.is_empty() and not _units.is_empty():
		var env := get_parent()
		var terrain: FjordTerrain = env.get("terrain") if env != null and "terrain" in env else null
		if terrain != null:
			for slot: Dictionary in terrain.offshore_slots():
				_slots[str(slot.get("id", ""))] = slot.get("position", Vector3.ZERO)
	return _slots


func _hand_water(water: Dictionary) -> void:
	water_now = water
	if director != null:
		director.set_underwater_water(float(water["visibility_m"]), float(water["chlorophyll_ug_l"]))


## The lens: the vignette under water and the veil of a surface crossing,
## in the water's own colour (the fog the director composed).
func _update_lens(under: bool) -> void:
	if lens_rect == null or not (lens_rect.material is ShaderMaterial):
		return
	var vignette := VIGNETTE if under else 0.0
	var shown := vignette > 0.0 or _veil > 0.001
	lens_rect.visible = shown
	if not shown:
		return
	var mat := lens_rect.material as ShaderMaterial
	mat.set_shader_parameter("vignette", vignette)
	mat.set_shader_parameter("veil", _veil * _veil)
	mat.set_shader_parameter("veil_color", _water_color)


## The water's colour for the veil: the fog the director composed, while it
## composes the water (above the surface it keeps the last).
func _track_water_color() -> void:
	if director == null:
		return
	var composed := director.get_composed()
	if not (composed.get("uw", {}) as Dictionary).is_empty():
		_water_color = composed.get("fog_color", _water_color)


## The bird net of the pen the dive goes into (see BIRD_NET_FADE_M), by the
## camera's place, while the dive lasts; given back whole after it.
func _update_bird_net(under: bool, delta: float) -> void:
	_dive_linger = DIVE_LINGER_S if under or camera.is_underwater_view() else maxf(_dive_linger - delta, 0.0)
	var cage: PenCage = null
	var want := 1.0
	var pens := _pen_manager()
	if _dive_linger > 0.0 and pens != null:
		var anchor := camera.underwater_anchor_for(camera.get_focus_point())
		if anchor.has("site_id"):
			var farm := pens.get_site(String(anchor["site_id"]))
			cage = farm.get_cage(int(anchor["pen"])) if farm != null else null
			want = bird_net_shown(camera.global_position - (anchor["center"] as Vector3), _water_level())
	if cage != bird_net_cage:
		_release_bird_net()
		bird_net_cage = cage
	if bird_net_cage == null or not is_instance_valid(bird_net_cage):
		bird_net_cage = null
		bird_net_fade = 1.0
		return
	bird_net_fade = want
	bird_net_cage.set_bird_net_fade(want)
	if want >= 1.0 and _dive_linger <= 0.0:
		_release_bird_net()


## How much of the dive's pen's bird net shows with the camera at `offset`
## from the pen's centre (on the water) and the water at `water_y`.
static func bird_net_shown(offset: Vector3, water_y: float) -> float:
	var near := 1.0 - smoothstep(BIRD_NET_NEAR_M.x, BIRD_NET_NEAR_M.y, Vector2(offset.x, offset.z).length())
	return 1.0 - near * (1.0 - smoothstep(BIRD_NET_FADE_M.x, BIRD_NET_FADE_M.y, offset.y - water_y))


func _release_bird_net() -> void:
	if bird_net_cage != null and is_instance_valid(bird_net_cage):
		bird_net_cage.set_bird_net_fade(1.0)
	bird_net_cage = null
	bird_net_fade = 1.0


## Staging (UnderwaterShots, G3-04): hold the shafts and the snow off (or
## give them back) while the net's caustics are measured.
func stage_effects(on: bool) -> void:
	effects_staged_off = not on


## The shafts and the snow follow the director's composition of the water
## at the camera (EnvironmentDirector c["uw"]): shown only under the surface.
func _update_effects(under: bool, delta: float) -> void:
	var composed: Dictionary = director.get_composed() if director != null else {}
	var uw: Dictionary = composed.get("uw", {})
	var shown := under and not uw.is_empty() and not effects_staged_off
	if shafts != null:
		_shaft_time += delta
		var strength := shaft_strength(float(composed.get("sun_elevation", 0.0)), float(composed.get("cloud_cover", 1.0)))
		shafts.visible = shown and strength > 0.001
		if shafts.visible:
			_place_shafts()
			var mat := shafts.material_override as ShaderMaterial
			mat.set_shader_parameter("shaft_dir", -(uw["sun_dir"] as Vector3))
			mat.set_shader_parameter("shaft_light", (uw["horizontal"] as Vector3) * SHAFT_CONTRAST * strength)
			mat.set_shader_parameter("shaft_time", _shaft_time)
			mat.set_shader_parameter("attenuation", float(uw["fog_density"]) * SHAFT_VEIL)
			mat.set_shader_parameter("depth_attenuation", UnderwaterOptics.luminance(uw["sigma"]))
	if snow != null:
		snow.visible = shown
		snow.emitting = shown
		if shown:
			snow.global_position = camera.global_position
			snow.amount_ratio = lerpf(SNOW_CLEAR_RATIO, 1.0, float(uw.get("bloom", 0.0)))
			var mat := snow.draw_pass_1.surface_get_material(0) as ShaderMaterial
			mat.set_shader_parameter("snow_light", (uw["down"] as Vector3) * SNOW_SHARE)
			mat.set_shader_parameter("attenuation", float(uw["fog_density"]))


## How strong the shafts are for a sun this high (degrees) and this much
## cloud: none with the sun low or behind a closed deck.
static func shaft_strength(sun_elevation_deg: float, cloud_cover: float) -> float:
	return smoothstep(SHAFT_SUN_DEG.x, SHAFT_SUN_DEG.y, sun_elevation_deg) \
		* (1.0 - smoothstep(SHAFT_CLOUD.x, SHAFT_CLOUD.y, cloud_cover))


## The shafts of the world grid's cells round the camera, laid out again
## when the camera has moved a cell's width.
func _place_shafts() -> void:
	var at := camera.global_position
	var cell := Vector2i(floori(at.x / SHAFT_CELL_M), floori(at.z / SHAFT_CELL_M))
	var key := Vector3(cell.x, 0.0, cell.y)
	if key == _shaft_centre:
		return
	_shaft_centre = key
	var mm := shafts.multimesh
	var reach := ceili(SHAFT_RADIUS_M / SHAFT_CELL_M)
	var water_y := _water_level() - 0.15
	# The node stands at the camera's cell (its culling box is local); the
	# shafts are placed relative to it.
	var origin := Vector3(float(cell.x) * SHAFT_CELL_M, 0.0, float(cell.y) * SHAFT_CELL_M)
	shafts.global_position = origin
	var n := 0
	for dz in range(-reach, reach + 1):
		for dx in range(-reach, reach + 1):
			if n >= mm.instance_count:
				break
			var c := Vector2i(cell.x + dx, cell.y + dz)
			var shaft := shaft_in_cell(c)
			var top := Vector3(shaft["x"], water_y, shaft["z"])
			if Vector2(top.x - at.x, top.z - at.z).length() > SHAFT_RADIUS_M:
				continue
			mm.set_instance_transform(n, Transform3D(Basis.IDENTITY, top - origin))
			mm.set_instance_custom_data(n, Color(shaft["width"], shaft["length"], shaft["phase"], shaft["strength"]))
			n += 1
	mm.visible_instance_count = n


## The shaft of a world grid cell: where it leaves the surface (x, z, m),
## its width and length (m), phase (0-1) and strength (0-1), all from the
## cell's hash: the same every time the camera comes back.
static func shaft_in_cell(cell: Vector2i) -> Dictionary:
	var rng := RandomNumberGenerator.new()
	rng.seed = hash(Vector3i(cell.x, cell.y, SHAFT_SEED))
	var x := (float(cell.x) + rng.randf()) * SHAFT_CELL_M
	var z := (float(cell.y) + rng.randf()) * SHAFT_CELL_M
	var width := SHAFT_WIDTH_M.x * pow(SHAFT_WIDTH_M.y / SHAFT_WIDTH_M.x, rng.randf())
	return {"x": x, "z": z, "width": width, "length": rng.randf_range(SHAFT_LENGTH_M.x, SHAFT_LENGTH_M.y),
		"phase": rng.randf(), "strength": rng.randf_range(0.2, 1.0)}


func _make_shafts() -> MultiMeshInstance3D:
	var quad := QuadMesh.new()
	quad.size = Vector2(1.0, 1.0)
	quad.center_offset = Vector3(0.0, -0.5, 0.0)
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.mesh = quad
	mm.instance_count = SHAFT_COUNT
	var node := MultiMeshInstance3D.new()
	node.name = "LightShafts"
	node.multimesh = mm
	var mat := ShaderMaterial.new()
	mat.shader = SHAFT_SHADER
	node.material_override = mat
	node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# The node follows the camera's cell (_place_shafts): the culling box is
	# the neighbourhood the shafts are laid out in, not the unit quad's.
	node.custom_aabb = AABB(Vector3(-SHAFT_RADIUS_M - 30.0, -40.0, -SHAFT_RADIUS_M - 30.0),
		Vector3(2.0 * SHAFT_RADIUS_M + 60.0, 45.0, 2.0 * SHAFT_RADIUS_M + 60.0))
	node.top_level = true
	node.layers = PlanarReflection.NO_MIRROR_LAYER
	node.visible = false
	return node


func _make_snow() -> GPUParticles3D:
	var process := ParticleProcessMaterial.new()
	process.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	process.emission_box_extents = SNOW_BOX
	process.direction = Vector3(0.0, -1.0, 0.0)
	process.spread = 180.0
	process.initial_velocity_min = 0.01
	process.initial_velocity_max = 0.05
	process.gravity = Vector3(0.0, -0.012, 0.0)
	process.scale_min = 0.35
	process.scale_max = 1.0
	process.turbulence_enabled = true
	process.turbulence_noise_strength = 0.12
	process.turbulence_noise_scale = 6.0
	process.turbulence_noise_speed = Vector3(0.03, 0.01, 0.03)
	process.turbulence_influence_min = 0.02
	process.turbulence_influence_max = 0.08
	var quad := QuadMesh.new()
	quad.size = Vector2(SNOW_SIZE_M, SNOW_SIZE_M)
	var mat := ShaderMaterial.new()
	mat.shader = SNOW_SHADER
	quad.material = mat
	var node := GPUParticles3D.new()
	node.name = "MarineSnow"
	node.amount = SNOW_AMOUNT
	node.lifetime = SNOW_LIFETIME_S
	node.preprocess = SNOW_LIFETIME_S
	node.local_coords = false
	node.process_material = process
	node.draw_pass_1 = quad
	node.visibility_aabb = AABB(-SNOW_BOX * 1.5, SNOW_BOX * 3.0)
	node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	node.layers = PlanarReflection.NO_MIRROR_LAYER
	node.emitting = false
	node.visible = false
	return node


func _pen_manager() -> FishPenManager:
	if _pens == null or not is_instance_valid(_pens):
		_pens = get_node_or_null("../FishPenManager") as FishPenManager
	return _pens


func _water_level() -> float:
	var env := get_parent()
	var terrain: FjordTerrain = env.get("terrain") if env != null and "terrain" in env else null
	return terrain.water_level if terrain != null else 0.0
