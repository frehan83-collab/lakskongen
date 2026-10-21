class_name FjordEnvironment
extends Node3D
## Main fjord environment controller.
## Coordinates terrain, water, camera, lighting, and weather systems.
## Entry point for gameplay to interact with the 3D environment.

@export_group("Systems")
@export var terrain: FjordTerrain
@export var camera: FjordCamera
@export var time_system: TimeOfDay
@export var weather_system: WeatherController
@export var particle_effects: ParticleEffects
## The only writer of fog, sky, light, tonemap and the water's wave uniforms.
@export var director: EnvironmentDirector

@export_group("Water")
## The camera-snapped LOD water grid (G1-05), out to the horizon.
@export var water_grid: WaterGrid
## The world under the surface (G3-04): the pen the camera dives into, the
## sim's water at the camera, the lens (UnderwaterView).
@export var underwater_view: UnderwaterView

@export_group("Debug")
@export var debug_overlay: bool = false

var _initialized: bool = false


func _ready() -> void:
	_connect_signals()

	if director == null:
		director = get_node_or_null("EnvironmentDirector") as EnvironmentDirector
	if underwater_view == null:
		underwater_view = get_node_or_null("UnderwaterView") as UnderwaterView

	if particle_effects:
		var pen_mgr = get_node_or_null("/root/FishPenManager")
		if pen_mgr == null:
			pen_mgr = get_node_or_null("../FishPenManager")
		particle_effects.fish_pen_manager = pen_mgr

	_initialized = true
	print("[FjordEnvironment] Initialized — Norwegian fjord renderer ready")


func _connect_signals() -> void:
	if time_system:
		time_system.sunrise.connect(_on_sunrise)
		time_system.sunset.connect(_on_sunset)

	if weather_system:
		weather_system.storm_started.connect(_on_storm_started)
		weather_system.storm_ended.connect(_on_storm_ended)
		weather_system.fog_visibility_changed.connect(_on_fog_visibility_changed)

	if camera:
		camera.entered_underwater.connect(_on_entered_underwater)
		camera.exited_underwater.connect(_on_exited_underwater)
		camera.zoom_level_changed.connect(_on_zoom_changed)


func _on_sunrise() -> void:
	print("[FjordEnvironment] Sunrise at %s" % time_system.get_time_string())


func _on_sunset() -> void:
	print("[FjordEnvironment] Sunset at %s" % time_system.get_time_string())


func _on_storm_started() -> void:
	print("[FjordEnvironment] Storm started")
	# Gameplay hook: notify GameManager about storm damage risk


func _on_storm_ended() -> void:
	print("[FjordEnvironment] Storm ended")


func _on_fog_visibility_changed(factor: float) -> void:
	# Gameplay hook: reduced visibility affects operations
	pass


## The underwater fog is the director's: it recomposes the environment every
## frame, so leaving the water restores the weather's fog by itself.
func _on_entered_underwater() -> void:
	print("[FjordEnvironment] Entered underwater view")


func _on_exited_underwater() -> void:
	print("[FjordEnvironment] Exited underwater view")


func _on_zoom_changed(level: FjordCamera.ZoomLevel) -> void:
	var level_names := ["Coast", "Fjord", "Farm", "Underwater"]
	print("[FjordEnvironment] Camera: %s view" % level_names[level])


## === Public API for Gameplay ===

## Set weather state (staging; the sim drives it through apply_sky_context).
func set_weather(state: WeatherController.WeatherState) -> void:
	if weather_system:
		weather_system.set_weather(state)


## The sim's sky context (GameBridge.GetSkyContext): calendar, latitude and
## the atmosphere the sim day implies.
func apply_sky_context(ctx: Dictionary) -> void:
	if ctx.is_empty():
		return
	if time_system:
		time_system.set_calendar(float(ctx.get("latitude", time_system.latitude_deg)), int(ctx.get("sim_day", time_system.sim_day)))
	if weather_system:
		weather_system.apply_sky_context(ctx)


## Sim water conditions for the water shader (through the director).
func set_water_state(temperature_c: float, algae01: float) -> void:
	if director:
		director.set_water_state(temperature_c, algae01)


## The sites' water as the sim reports it (GetSeaSiteDetails rows with
## water_visibility_m, bloom_chlorophyll_ug_l): the underwater view's fog
## and colour at each site (G3-04).
func set_site_waters(sites: Array) -> void:
	if underwater_view:
		underwater_view.set_site_waters(sites)


## Set time of day (0-24 hours). Photo mode and the harness pin the hour.
func set_time(hour: float) -> void:
	if time_system:
		time_system.set_hour(hour)
	_snap_director()


## Set season as a fraction of the year from 1 January (0 = winter,
## 0.5 = summer): moves the calendar day, so the sky, the snow line and the
## birch leaves follow (the director composes the ground from the calendar).
func set_season(s: float) -> void:
	if time_system:
		time_system.season = s
	_snap_director()


## Latitude (deg N) for the sky: polar night, midnight sun, aurora.
func set_latitude(latitude: float) -> void:
	if time_system:
		time_system.set_calendar(latitude, time_system.sim_day)
	_snap_director()


func _snap_director() -> void:
	if director and director.is_inside_tree():
		director.compose(0.0, true)


## Move camera to look at world position
func focus_camera_on(world_pos: Vector3, zoom: FjordCamera.ZoomLevel = FjordCamera.ZoomLevel.FARM) -> void:
	if camera:
		camera.look_at_position(world_pos, zoom)


## Get terrain height at position (for placing objects)
func get_ground_height(world_x: float, world_z: float) -> float:
	if terrain:
		return terrain.get_height_at(world_x, world_z)
	return 0.0


## Is it currently stormy? (gameplay check)
func is_storm_active() -> bool:
	return weather_system != null and weather_system.is_stormy()


## Get current visibility factor
func get_visibility() -> float:
	if weather_system:
		return weather_system.get_visibility()
	return 1.0


## Get wind vector for physics
func get_wind() -> Vector2:
	if weather_system:
		return weather_system.get_wind()
	return Vector2.ZERO


## Get current time string
func get_time_string() -> String:
	if time_system:
		return time_system.get_time_string()
	return "??:??"
