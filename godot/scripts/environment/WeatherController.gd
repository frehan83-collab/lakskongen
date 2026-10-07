class_name WeatherController
extends Node3D
## Weather system for Norwegian fjord environment.
## Manages weather states with smooth transitions.
## Weather affects gameplay: storms damage pens, fog reduces visibility.

# === Weather States ===
enum WeatherState { CLEAR, OVERCAST, RAIN, SNOW, STORM, FOG }

@export var current_weather: WeatherState = WeatherState.CLEAR
@export var transition_duration: float = 10.0  # Seconds to blend between states

# === Node References ===
@export_group("Nodes")
@export var rain_particles: GPUParticles3D
@export var snow_particles: GPUParticles3D
@export var world_environment: WorldEnvironment
@export var water_material: ShaderMaterial
@export var sky_material: ShaderMaterial
@export var time_of_day: TimeOfDay

# === Weather Presets ===
# fog_density, cloud_coverage, wind_strength, rain_intensity, snow_intensity, ambient_dim
const WEATHER_PRESETS := {
	WeatherState.CLEAR:    { "fog": 0.0005, "clouds": 0.1,  "wind": 0.3, "rain": 0.0, "snow": 0.0, "dim": 1.0 },
	WeatherState.OVERCAST: { "fog": 0.002,  "clouds": 0.7,  "wind": 0.5, "rain": 0.0, "snow": 0.0, "dim": 0.7 },
	WeatherState.RAIN:     { "fog": 0.004,  "clouds": 0.85, "wind": 0.8, "rain": 1.0, "snow": 0.0, "dim": 0.5 },
	WeatherState.SNOW:     { "fog": 0.003,  "clouds": 0.8,  "wind": 0.4, "rain": 0.0, "snow": 1.0, "dim": 0.6 },
	WeatherState.STORM:    { "fog": 0.008,  "clouds": 1.0,  "wind": 2.5, "rain": 2.0, "snow": 0.0, "dim": 0.3 },
	WeatherState.FOG:      { "fog": 0.02,   "clouds": 0.5,  "wind": 0.1, "rain": 0.0, "snow": 0.0, "dim": 0.4 },
}

# Internal
var _target_preset: Dictionary = {}
var _current_values: Dictionary = {}
var _transition_progress: float = 1.0
var _start_values: Dictionary = {}

# Weather affects gameplay
signal weather_changed(state: WeatherState)
signal storm_started()
signal storm_ended()
signal fog_visibility_changed(factor: float)  # 0.0 = blind, 1.0 = clear

var _wind_direction: Vector2 = Vector2(1.0, 0.3)
var _was_storm: bool = false


func _ready() -> void:
	# Auto-find sky material from WorldEnvironment
	if not sky_material and world_environment and world_environment.environment:
		var env := world_environment.environment
		if env.sky and env.sky.sky_material is ShaderMaterial:
			sky_material = env.sky.sky_material as ShaderMaterial

	# Auto-find water material from sibling WaterPlane
	if not water_material:
		var water_node := get_parent().get_node_or_null("WaterPlane") as MeshInstance3D
		if water_node and water_node.material_override is ShaderMaterial:
			water_material = water_node.material_override as ShaderMaterial

	_current_values = WEATHER_PRESETS[WeatherState.CLEAR].duplicate()
	_target_preset = _current_values.duplicate()
	_apply_weather_values(_current_values)


func _process(delta: float) -> void:
	if _transition_progress < 1.0:
		_transition_progress = minf(_transition_progress + delta / transition_duration, 1.0)
		_interpolate_weather(_transition_progress)

	# Slowly rotate wind direction
	var wind_angle := atan2(_wind_direction.y, _wind_direction.x)
	wind_angle += sin(Time.get_ticks_msec() * 0.0001) * 0.001
	_wind_direction = Vector2(cos(wind_angle), sin(wind_angle))

	# Update particles to follow camera
	_update_particle_positions()


func set_weather(state: WeatherState) -> void:
	if state == current_weather and _transition_progress >= 1.0:
		return

	_start_values = _current_values.duplicate()
	_target_preset = WEATHER_PRESETS[state].duplicate()
	_transition_progress = 0.0
	current_weather = state

	weather_changed.emit(state)

	# Storm events
	if state == WeatherState.STORM and not _was_storm:
		storm_started.emit()
	elif state != WeatherState.STORM and _was_storm:
		storm_ended.emit()
	_was_storm = (state == WeatherState.STORM)


func _interpolate_weather(t: float) -> void:
	# Smooth easing
	var eased := t * t * (3.0 - 2.0 * t)  # Smoothstep

	for key in _target_preset:
		_current_values[key] = lerpf(_start_values.get(key, 0.0), _target_preset[key], eased)

	_apply_weather_values(_current_values)


func _apply_weather_values(values: Dictionary) -> void:
	# Fog
	if world_environment and world_environment.environment:
		var env := world_environment.environment
		env.fog_density = values.get("fog", 0.001)
		env.fog_enabled = values.get("fog", 0.0) > 0.001

	# Clouds in sky shader
	if sky_material:
		sky_material.set_shader_parameter("cloud_coverage", values.get("clouds", 0.3))

	# Wind affects water
	if water_material:
		water_material.set_shader_parameter("wind_strength", values.get("wind", 1.0))
		water_material.set_shader_parameter("wind_direction", _wind_direction)
		# Storm = bigger waves
		var wave_amp := remap(values.get("wind", 1.0), 0.0, 3.0, 0.15, 0.8)
		water_material.set_shader_parameter("wave_amplitude", wave_amp)

	# Rain particles
	if rain_particles:
		var rain_amount: float = values.get("rain", 0.0)
		rain_particles.emitting = rain_amount > 0.01
		rain_particles.amount = int(clampf(rain_amount * 2000, 0, 8000))

	# Snow particles
	if snow_particles:
		var snow_amount: float = values.get("snow", 0.0)
		snow_particles.emitting = snow_amount > 0.01
		snow_particles.amount = int(clampf(snow_amount * 1500, 0, 4000))

	# Visibility
	var vis_fog := 1.0 - clampf(values.get("fog", 0.0) * 100.0, 0.0, 0.8)
	var vis_rain := 1.0 - clampf(values.get("rain", 0.0) * 0.3, 0.0, 0.3)
	var vis_snow := 1.0 - clampf(values.get("snow", 0.0) * 0.2, 0.0, 0.2)
	fog_visibility_changed.emit(vis_fog * vis_rain * vis_snow)


func _update_particle_positions() -> void:
	# Move particle emitters to follow camera so rain/snow always covers view
	var cam := get_viewport().get_camera_3d()
	if not cam:
		return

	var cam_pos := cam.global_position
	if rain_particles:
		rain_particles.global_position = Vector3(cam_pos.x, cam_pos.y + 50.0, cam_pos.z)
	if snow_particles:
		snow_particles.global_position = Vector3(cam_pos.x, cam_pos.y + 60.0, cam_pos.z)


## Get current wind as Vector2 (direction * strength)
func get_wind() -> Vector2:
	return _wind_direction * _current_values.get("wind", 1.0)


## Get visibility factor (0-1, used by gameplay)
func get_visibility() -> float:
	var values := _current_values
	var vis := 1.0 - clampf(values.get("fog", 0.0) * 100.0, 0.0, 0.8)
	vis *= 1.0 - clampf(values.get("rain", 0.0) * 0.3, 0.0, 0.3)
	return vis


## Is it currently stormy? (gameplay: damage pens)
func is_stormy() -> bool:
	return current_weather == WeatherState.STORM


## Get ambient dimming factor (for lighting)
func get_ambient_dim() -> float:
	return _current_values.get("dim", 1.0)


## Get weather state name
static func weather_name(state: WeatherState) -> String:
	match state:
		WeatherState.CLEAR: return "Clear"
		WeatherState.OVERCAST: return "Overcast"
		WeatherState.RAIN: return "Rain"
		WeatherState.SNOW: return "Snow"
		WeatherState.STORM: return "Storm"
		WeatherState.FOG: return "Fog"
	return "Unknown"
