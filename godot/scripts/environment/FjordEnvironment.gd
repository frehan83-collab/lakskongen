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
@export var visual_adapter: VisualAdapter3D
@export var particle_effects: ParticleEffects

@export_group("Water")
@export var water_plane: MeshInstance3D

@export_group("Post-Process")
## Full-screen underwater overlay (blue tint + blur + caustics).
@export var underwater_rect: ColorRect
## Water surface height used to detect an underwater camera.
@export var underwater_water_level: float = 0.0

@export_group("Debug")
@export var debug_overlay: bool = false

var _initialized: bool = false
var _underwater_target: float = 0.0
var _underwater_current: float = 0.0


func _ready() -> void:
	_connect_signals()

	if underwater_rect == null:
		underwater_rect = get_node_or_null("UnderwaterCanvas/UnderwaterRect") as ColorRect
	
	if visual_adapter:
		visual_adapter.bind_simulation(get_node_or_null("/root/SimBridge"))
	
	if particle_effects:
		var pen_mgr = get_node_or_null("/root/FishPenManager")
		if pen_mgr == null:
			pen_mgr = get_node_or_null("../FishPenManager")
		particle_effects.fish_pen_manager = pen_mgr
	
	_initialized = true
	print("[FjordEnvironment] Initialized — Norwegian fjord renderer ready")


func _process(delta: float) -> void:
	_update_underwater_post(delta)


## Fade the underwater post-process overlay based on camera depth.
## Fully guarded: no-ops when the overlay or camera is missing.
func _update_underwater_post(delta: float) -> void:
	if underwater_rect == null or not (underwater_rect.material is ShaderMaterial):
		return

	var target := 0.0
	if camera:
		var level := underwater_water_level
		if terrain:
			level = terrain.water_level
		if camera.global_position.y < level:
			target = 1.0

	_underwater_target = target
	_underwater_current = move_toward(_underwater_current, _underwater_target, delta * 2.5)
	(underwater_rect.material as ShaderMaterial).set_shader_parameter("active", _underwater_current)


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
	print("[FjordEnvironment] ☀️ Sunrise at %s" % time_system.get_time_string())


func _on_sunset() -> void:
	print("[FjordEnvironment] 🌙 Sunset at %s" % time_system.get_time_string())


func _on_storm_started() -> void:
	print("[FjordEnvironment] ⛈️ Storm started!")
	# Gameplay hook: notify GameManager about storm damage risk


func _on_storm_ended() -> void:
	print("[FjordEnvironment] Storm ended")


func _on_fog_visibility_changed(factor: float) -> void:
	# Gameplay hook: reduced visibility affects operations
	pass


func _on_entered_underwater() -> void:
	print("[FjordEnvironment] 🐟 Entered underwater view")
	# Adjust fog for underwater look
	if weather_system and weather_system.world_environment:
		var env := weather_system.world_environment.environment
		env.fog_enabled = true
		env.fog_density = 0.015
		env.fog_light_color = Color("#0F3A4A")


func _on_exited_underwater() -> void:
	print("[FjordEnvironment] Exited underwater view")
	# Restore normal fog
	if weather_system:
		weather_system.set_weather(weather_system.current_weather)


func _on_zoom_changed(level: FjordCamera.ZoomLevel) -> void:
	var level_names := ["Coast", "Fjord", "Farm", "Underwater"]
	print("[FjordEnvironment] Camera: %s view" % level_names[level])


## === Public API for Gameplay ===

## Set weather state
func set_weather(state: WeatherController.WeatherState) -> void:
	if weather_system:
		weather_system.set_weather(state)


## Set time of day (0-24 hours)
func set_time(hour: float) -> void:
	if time_system:
		time_system.set_hour(hour)


## Set season (0.0=winter, 0.25=spring, 0.5=summer, 0.75=autumn)
func set_season(s: float) -> void:
	if time_system:
		time_system.season = s


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
