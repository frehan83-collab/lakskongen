class_name TimeOfDay
extends Node
## Day/night cycle controller with Norwegian seasonal variations.
## Drives sun position, sky colors, and ambient lighting.

# === Time Settings ===
@export_group("Time")
## Current time of day (0.0 = midnight, 0.5 = noon, 1.0 = midnight)
@export_range(0.0, 1.0) var time_of_day: float = 0.35
## Minutes per real second (game time speed)
@export var time_scale: float = 2.0
## Pause the cycle
@export var paused: bool = false

# === Season ===
@export_group("Season")
## 0.0=winter solstice, 0.25=spring equinox, 0.5=summer solstice, 0.75=autumn equinox
@export_range(0.0, 1.0) var season: float = 0.5

# === Node References ===
@export_group("Nodes")
@export var sun_light: DirectionalLight3D
@export var world_environment: WorldEnvironment
@export var sky_material: ShaderMaterial

# === Sun Color Temperatures ===
const SUN_COLORS := {
	"noon":     Color("#FFF4E6"),  # Warm white
	"golden":   Color("#FFB347"),  # Golden hour
	"sunset":   Color("#FF6B4A"),  # Sunset orange
	"twilight": Color("#6B7FA8"),  # Blue twilight
	"night":    Color("#1A2744"),  # Dark blue (moonlight)
	"moon":     Color("#C7D7E8"),  # Cool white moon
}

# Daylight hours by season (Norway: 69°N latitude approx)
# Winter solstice: ~0h, Summer solstice: ~24h (midnight sun)
# Using simplified model for gameplay
const DAYLIGHT_HOURS := {
	0.0:   6.0,   # Winter: 6h daylight
	0.25: 12.0,   # Spring: 12h
	0.5:  20.0,   # Summer: 20h (midnight sun region)
	0.75: 12.0,   # Autumn: 12h
	1.0:   6.0,   # Back to winter
}

# Internal
var _sun_energy_base: float = 1.0
var _current_hour: float = 0.0  # 0-24

signal time_changed(hour: float)
signal new_day()
signal sunrise()
signal sunset()

var _was_day: bool = true


func _ready() -> void:
	if sun_light:
		_sun_energy_base = sun_light.light_energy
	# Auto-find sky material from WorldEnvironment
	if not sky_material and world_environment and world_environment.environment:
		var env := world_environment.environment
		if env.sky and env.sky.sky_material is ShaderMaterial:
			sky_material = env.sky.sky_material as ShaderMaterial


func _process(delta: float) -> void:
	if paused:
		return

	# Advance time
	var minutes_elapsed := time_scale * delta
	var day_fraction := minutes_elapsed / (24.0 * 60.0)
	time_of_day += day_fraction

	if time_of_day >= 1.0:
		time_of_day -= 1.0
		new_day.emit()

	_current_hour = time_of_day * 24.0
	time_changed.emit(_current_hour)

	_update_sun()
	_update_sky()
	_update_environment()
	_check_day_night_transitions()


func _get_daylight_hours() -> float:
	# Interpolate between seasonal daylight values
	var keys := DAYLIGHT_HOURS.keys()
	keys.sort()
	var s := fmod(season, 1.0)

	for i in range(keys.size() - 1):
		if s >= keys[i] and s <= keys[i + 1]:
			var t: float = (s - keys[i]) / (keys[i + 1] - keys[i])
			return lerpf(DAYLIGHT_HOURS[keys[i]], DAYLIGHT_HOURS[keys[i + 1]], t)

	return 12.0


func _get_sunrise_sunset() -> Dictionary:
	var daylight := _get_daylight_hours()
	var sunrise_hour := 12.0 - daylight / 2.0
	var sunset_hour := 12.0 + daylight / 2.0
	return { "sunrise": sunrise_hour, "sunset": sunset_hour }


func _update_sun() -> void:
	if not sun_light:
		return

	var times := _get_sunrise_sunset()
	var hour := _current_hour

	# Sun angle: rises in east, peaks at noon, sets in west
	# Map hour to angle: sunrise=0°, noon=90° (overhead), sunset=180°
	var sun_progress: float
	if hour >= times["sunrise"] and hour <= times["sunset"]:
		sun_progress = (hour - times["sunrise"]) / (times["sunset"] - times["sunrise"])
	elif hour < times["sunrise"]:
		sun_progress = -0.1  # Below horizon
	else:
		sun_progress = 1.1  # Below horizon

	var sun_angle := lerpf(-10.0, 190.0, sun_progress)
	var sun_height := sin(deg_to_rad(clampf(sun_angle, 0.0, 180.0)))

	# Rotate sun
	sun_light.rotation_degrees = Vector3(
		-lerpf(5.0, 80.0, sun_height),  # Pitch (height)
		lerpf(-90.0, 90.0, sun_progress),  # Yaw (east to west)
		0.0
	)

	# Sun color based on height
	var sun_color: Color
	if sun_height > 0.5:
		sun_color = SUN_COLORS["noon"]
	elif sun_height > 0.15:
		sun_color = SUN_COLORS["noon"].lerp(SUN_COLORS["golden"], inverse_lerp(0.5, 0.15, sun_height))
	elif sun_height > 0.0:
		sun_color = SUN_COLORS["golden"].lerp(SUN_COLORS["sunset"], inverse_lerp(0.15, 0.0, sun_height))
	else:
		sun_color = SUN_COLORS["twilight"].lerp(SUN_COLORS["night"], clampf(-sun_height * 5.0, 0.0, 1.0))

	sun_light.light_color = sun_color

	# Sun energy (dim at horizon, off below)
	var energy := clampf(sun_height * 2.0, 0.0, 1.0) * _sun_energy_base
	sun_light.light_energy = energy

	# Shadow quality adjustment
	sun_light.shadow_enabled = sun_height > 0.0


func _update_sky() -> void:
	if not sky_material:
		return

	sky_material.set_shader_parameter("time_of_day", time_of_day)
	sky_material.set_shader_parameter("season", season)
	sky_material.set_shader_parameter("cloud_coverage", 0.3)  # Will be driven by WeatherController


func _update_environment() -> void:
	if not world_environment or not world_environment.environment:
		return

	var env := world_environment.environment
	var times := _get_sunrise_sunset()
	var hour := _current_hour
	var sun_height := 0.0

	if hour >= times["sunrise"] and hour <= times["sunset"]:
		var progress: float = (hour - times["sunrise"]) / (times["sunset"] - times["sunrise"])
		sun_height = sin(progress * PI)

	# Ambient light
	var ambient_energy := lerpf(0.1, 0.5, clampf(sun_height * 2.0, 0.0, 1.0))
	env.ambient_light_energy = ambient_energy

	# Ambient color
	var ambient_color := Color("#1A2744").lerp(Color("#87CEEB"), clampf(sun_height * 3.0, 0.0, 1.0))
	env.ambient_light_color = ambient_color

	# Fog density (thicker at night, dawn, dusk)
	var fog_density := lerpf(0.005, 0.001, clampf(sun_height * 3.0, 0.0, 1.0))
	env.fog_density = fog_density


func _check_day_night_transitions() -> void:
	var times := _get_sunrise_sunset()
	var is_day: bool = _current_hour >= times["sunrise"] and _current_hour <= times["sunset"]

	if is_day and not _was_day:
		sunrise.emit()
	elif not is_day and _was_day:
		sunset.emit()

	_was_day = is_day


## Get time as formatted string (HH:MM)
func get_time_string() -> String:
	var hours := int(_current_hour)
	var minutes := int(fmod(_current_hour * 60.0, 60.0))
	return "%02d:%02d" % [hours, minutes]


## Check if currently daytime
func is_daytime() -> bool:
	var times := _get_sunrise_sunset()
	return _current_hour >= times["sunrise"] and _current_hour <= times["sunset"]


## Get normalized sun height (0=horizon, 1=zenith)
func get_sun_height() -> float:
	var times := _get_sunrise_sunset()
	if _current_hour < times["sunrise"] or _current_hour > times["sunset"]:
		return 0.0
	var progress: float = (_current_hour - times["sunrise"]) / (times["sunset"] - times["sunrise"])
	return sin(progress * PI)


## Set time directly (0-24 hours)
func set_hour(hour: float) -> void:
	time_of_day = fmod(hour / 24.0, 1.0)
