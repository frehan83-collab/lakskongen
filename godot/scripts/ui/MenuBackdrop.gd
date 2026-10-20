class_name MenuBackdrop
extends Node3D

## The main menu's live world (roadmap U1-09): the game's own
## FjordEnvironment, staged as a still late-winter afternoon in Troms (the
## owner's reference 2: calm slate water, a low sun, cool light), seen from a
## low oblique camera that drifts slowly down the fjord toward the open sea,
## past a small salmon site, while a wellboat comes in from the coast.
##
## Nothing here is a video or a painting: it is the same terrain, sky, water,
## pens and vessels the game draws, so the menu shows what the game looks
## like. The sim is not involved: the staging is the menu's own and the
## world's sim-driven managers are removed.
##
## Reduced motion (MotionPrefs) holds the camera still; the water and the
## boat keep their own small motion.

signal world_ready

const ENVIRONMENT_SCENE := preload("res://scenes/environment/FjordEnvironment.tscn")
const PEN_SCENE := preload("res://assets/models/pen_cage/pen_cage.tscn")
const WORKBOAT_SCENE := preload("res://assets/models/workboat/workboat.tscn")
const BARGE_SCENE := preload("res://assets/models/feed_barge/feed_barge.tscn")
const WELLBOAT_SCENE := preload("res://assets/models/wellboat/wellboat.tscn")

## The world's sim-driven managers: the menu has no running game to show.
const SIM_MANAGERS := ["WellboatVisualManager", "FeedBargeVisualManager", "CompetitorFarmVisualManager"]

# ── Mood ──────────────────────────────────────────────────────────────────────
## 69.6 N (Tromsø) on 3 March at half past two: the sun 10 degrees up in
## the south-west, off the left of the frame, lighting the cloud undersides
## over the fjord mouth; the light low and cool.
const LATITUDE_DEG := 69.6
const SEASON := 0.17
const HOUR := 14.5
## A calm high-pressure day, in the sky context's units (AtmosphereModel):
## thin cloud, long visibility, a breath of wind, glassy water.
const SKY := {
	"cloud_cover": 0.25, "visibility_km": 70.0, "precip_mm_h": 0.0, "snow": false,
	"weather_state": "Calm", "sea_fog": 0.0, "wind_ms": 1.2, "wave_m": 0.06,
}

# ── Camera ────────────────────────────────────────────────────────────────────
## The camera looks out of the fjord (toward -Z, the mouth and the open sea)
## and dollies between these points (world Z) and back, once per
## DRIFT_PERIOD_S, on a sine so it never stops with a jerk. 210 m over 90 s:
## under 4 m/s at its fastest.
const DRIFT_FROM_Z := 330.0
const DRIFT_TO_Z := 120.0
const DRIFT_PERIOD_S := 180.0
## A slow sway of the heading (degrees, period in seconds).
const SWAY_DEG := 2.5
const SWAY_PERIOD_S := 70.0
## Metres from the camera to the point it looks at (the rig's focus). At
## this distance the director's depth of field leaves the fjord sharp.
const LOOK_DISTANCE := 1200.0
## The rig's table pitch at LOOK_DISTANCE is about -18 degrees; this lifts
## the view to the rig's shallowest pitch (-5 degrees): a low oblique with
## the horizon two fifths down the frame, as in the reference.
const PITCH_OFFSET_DEG := 15.0
## Heading off the fjord's axis (degrees, positive turns left), so the
## fjord's mouth opens to the right of the menu column.
const HEADING_OFFSET_DEG := 9.0
## The camera's track runs this far off the channel's centreline (m, +X:
## to the right looking out to sea).
const TRACK_OFFSET_M := 30.0

# ── Site ──────────────────────────────────────────────────────────────────────
## Three pens in a row along the fjord ahead of the camera, a feed barge at
## the seaward end of the row and the workboat alongside the nearest pen.
const SITE_Z := -270.0
const SITE_OFFSET_M := 55.0
const SITE_PENS := 3
const BUOY_YELLOW := Color(0.910, 0.725, 0.165)  # D13 buoy yellow #E8B92A
# ── Cost ──────────────────────────────────────────────────────────────────────
## The menu's low camera sees far more sky than the game's views, and the sky
## is the costliest pixel in the frame (the atmosphere and cloud march). The
## world is drawn at most at this share of the screen's resolution and
## brought up by the quality tier's upscaler (the menu's text stays at full
## resolution), so the menu costs no more than the game's fjord view
## (measured in-run, RUN_LOG U1-09). The menu's director applies it as a cap
## (EnvironmentDirector.set_render_scale_cap): the quality tiers are the only
## writer of the render settings (P0-04), and a tier already below it (High,
## 0.75) keeps its own.
const RENDER_SCALE := 0.85

## The wellboat's lane (m off the centreline, left looking out) and speed
## (m/s, ~8 kn): it comes in from the coast and passes the camera.
const WELLBOAT_LANE_M := -110.0
const WELLBOAT_SPEED := 4.0

var environment: FjordEnvironment = null
var camera: FjordCamera = null
var terrain: FjordTerrain = null
## True while the camera holds still (MotionPrefs, or a test).
var reduced_motion: bool = false
## Seconds of drift so far (reduced motion stops the clock).
var drift_time: float = 0.0

var _ready_emitted := false
var _wellboat: Node3D = null
var _wellboat_from_z := 0.0
var _wellboat_to_z := 0.0


func _ready() -> void:
	environment = ENVIRONMENT_SCENE.instantiate() as FjordEnvironment
	environment.name = "FjordEnvironment"
	add_child(environment)
	if environment.director != null:
		environment.director.set_render_scale_cap(RENDER_SCALE)
		# The hero view looks along the horizon: its clouds one pass finer.
		environment.director.set_hero_sky(true)
	camera = environment.camera
	terrain = environment.terrain
	for manager in SIM_MANAGERS:
		var node := environment.get_node_or_null(manager)
		if node != null:
			node.queue_free()
	reduced_motion = MotionPrefs.reduced_motion()
	_stage_mood()
	_build_site()
	_launch_wellboat()
	if camera != null:
		camera.auto_orbit_enabled = false
		camera.input_enabled = false
		_apply_view(true)
	if terrain != null and not terrain.is_ready():
		terrain.terrain_ready.connect(_on_terrain_ready, CONNECT_ONE_SHOT)
	else:
		_on_terrain_ready.call_deferred()


func _process(delta: float) -> void:
	if not reduced_motion:
		drift_time += delta
	_apply_view(false)
	_sail_wellboat(delta)


## True once the terrain, its collider and the first reflection are in.
func is_world_ready() -> bool:
	return _ready_emitted


## Hold the camera still (true) or let it drift (false), e.g. after the
## player changes Redusert bevegelse in the menu's settings.
func set_reduced_motion(on: bool) -> void:
	reduced_motion = on


## Where the camera stands on its track at `t` seconds: (x, z) on the water
## and the heading it looks along (radians, the rig's yaw convention).
func view_at(t: float) -> Dictionary:
	var phase := 0.5 - 0.5 * cos(TAU * t / DRIFT_PERIOD_S)
	var z := lerpf(DRIFT_FROM_Z, DRIFT_TO_Z, phase)
	var pos := _channel_point(z, TRACK_OFFSET_M)
	var ahead := _channel_point(z - LOOK_DISTANCE, TRACK_OFFSET_M)
	var fwd := Vector2(ahead.x - pos.x, ahead.z - pos.z).normalized()
	var sway := deg_to_rad(SWAY_DEG) * sin(TAU * t / SWAY_PERIOD_S)
	fwd = fwd.rotated(-deg_to_rad(HEADING_OFFSET_DEG) - sway)
	var focus := pos + Vector3(fwd.x, 0.0, fwd.y) * LOOK_DISTANCE
	# The rig's yaw points from the focus back to the camera.
	return {"position": pos, "focus": focus, "yaw": atan2(-fwd.x, -fwd.y)}


func _apply_view(snap: bool) -> void:
	if camera == null:
		return
	var v := view_at(drift_time)
	camera.hold_view(v["focus"], v["yaw"], LOOK_DISTANCE, PITCH_OFFSET_DEG, snap)


func _on_terrain_ready() -> void:
	# Two frames for the chunk uploads and the reflection probe to land.
	await get_tree().process_frame
	await get_tree().process_frame
	_ready_emitted = true
	world_ready.emit()


func _stage_mood() -> void:
	var ts := environment.time_system
	if ts != null:
		ts.paused = true
		ts.set_calendar(LATITUDE_DEG, int(round(SEASON * 365.0)))
	var weather := environment.weather_system
	if weather != null:
		weather.transition_duration = 0.0
	environment.apply_sky_context(SKY)
	environment.set_season(SEASON)
	environment.set_time(HOUR)


func _channel_point(z: float, offset: float) -> Vector3:
	var x := terrain.channel_center_x(z) if terrain != null else 0.0
	return Vector3(x + offset, 0.0, z)


# ── Set dressing ──────────────────────────────────────────────────────────────

func _build_site() -> void:
	var site := Node3D.new()
	site.name = "Site"
	add_child(site)
	var origin := _channel_point(SITE_Z, SITE_OFFSET_M)
	var ahead := _channel_point(SITE_Z + 100.0, SITE_OFFSET_M)
	site.position = origin
	site.rotation.y = atan2(ahead.x - origin.x, ahead.z - origin.z)
	var span := float(SITE_PENS) * MooringGrid.CELL_M
	var pens: Array = []
	for i in SITE_PENS:
		var pen := PEN_SCENE.instantiate() as PenCage
		pen.name = "Pen%d" % (i + 1)
		site.add_child(pen)
		pen.position = Vector3(0.0, 0.0, (float(i) + 0.5) * MooringGrid.CELL_M - span * 0.5)
		pen.show_health_ring(false)
		var click := pen.get_click_area()
		if click != null:
			click.input_ray_pickable = false
		pens.append(Vector2i(0, i))
	var mooring := MooringGrid.new()
	mooring.name = "Mooring"
	site.add_child(mooring)
	var floor_at := func(x: float, z: float) -> float:
		return terrain.get_height_at(x, z) if terrain != null else NAN
	mooring.build(1, SITE_PENS, pens, BUOY_YELLOW, floor_at)
	var barge := BARGE_SCENE.instantiate() as Node3D
	barge.name = "FeedBarge"
	site.add_child(barge)
	barge.position = Vector3(8.0, 0.0, -span * 0.5 - 46.0)
	barge.rotation.y = PI * 0.5
	var boat := WORKBOAT_SCENE.instantiate() as Node3D
	boat.name = "Workboat"
	site.add_child(boat)
	boat.position = Vector3(33.6, 0.0, span * 0.5 - MooringGrid.CELL_M * 0.5)


func _launch_wellboat() -> void:
	_wellboat = WELLBOAT_SCENE.instantiate() as Node3D
	_wellboat.name = "Wellboat"
	add_child(_wellboat)
	var span := terrain.mouth_and_head() if terrain != null else Vector2(-1300.0, 1450.0)
	_wellboat_from_z = span.x + 150.0
	_wellboat_to_z = DRIFT_FROM_Z + 300.0
	_place_wellboat(lerpf(_wellboat_from_z, _wellboat_to_z, 0.3))


## The wellboat sails in from the coast along its lane; once past the camera
## it starts again at the fjord mouth, a speck on the horizon 1.5 km away.
func _sail_wellboat(delta: float) -> void:
	if _wellboat == null:
		return
	var z := _wellboat.position.z + WELLBOAT_SPEED * delta
	if z > _wellboat_to_z:
		z = _wellboat_from_z
	_place_wellboat(z)


func _place_wellboat(z: float) -> void:
	var here := _channel_point(z, WELLBOAT_LANE_M)
	var next := _channel_point(z + 20.0, WELLBOAT_LANE_M)
	_wellboat.position = here
	# The bow points along -Z in the model: face the way it sails.
	_wellboat.rotation.y = atan2(-(next.x - here.x), -(next.z - here.z))
