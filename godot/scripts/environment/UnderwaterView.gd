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
## OPEN_WATER_REACH_M, else UnderwaterOptics.FALLBACK_VISIBILITY_M.
const OPEN_WATER_REACH_M := 1500.0
## The veil as the camera crosses the surface: up in a moment, gone in
## VEIL_FADE_S.
const VEIL_FADE_S := 0.45
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
var _pens: FishPenManager = null
var _was_under := false
var _veil := 0.0
var _shaft_time := 0.0
var _shaft_centre := Vector3.INF
## What the director was last handed (tests read it).
var water_now := {"visibility_m": UnderwaterOptics.FALLBACK_VISIBILITY_M, "chlorophyll_ug_l": 0.0, "site_id": ""}
## The light shafts and the marine snow (made at _ready; tests read them).
var shafts: MultiMeshInstance3D = null
var snow: GPUParticles3D = null


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
	if under or camera.is_underwater_view():
		_hand_water(water_at(camera.global_position))
	_update_lens(under)
	_update_effects(under, delta)


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
	return {"visibility_m": UnderwaterOptics.FALLBACK_VISIBILITY_M, "chlorophyll_ug_l": 0.0, "site_id": ""}


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
	if director != null:
		var fog: Color = director.get_composed().get("fog_color", Color(0.05, 0.12, 0.12))
		mat.set_shader_parameter("veil_color", fog)


## The shafts and the snow follow the director's composition of the water
## at the camera (EnvironmentDirector c["uw"]): shown only under the surface.
func _update_effects(under: bool, delta: float) -> void:
	var composed: Dictionary = director.get_composed() if director != null else {}
	var uw: Dictionary = composed.get("uw", {})
	var shown := under and not uw.is_empty()
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
