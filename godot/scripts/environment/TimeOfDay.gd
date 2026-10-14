class_name TimeOfDay
extends Node
## Presentation clock and the sky's geometry (G1-04, D20).
##
## - The hour of day is a presentation clock: `time_scale` game minutes per
##   real second (a 1-second sim day cannot drive a visible day). Photo mode
##   and the screenshot harness pin it with set_hour() or `paused`.
## - The date and the latitude come from the sim calendar (set_calendar(),
##   fed by FjordVistaBridge), never from a table of daylight hours.
## - Sun, moon and aurora come from CelestialBridge.cs, which wraps the sim's
##   SolarGeometry and AuroraModel (unit-tested in LakskongenSim.Tests).
##
## This node writes nothing in the scene: EnvironmentDirector reads the
## positions below and is the only writer of the light, sky and fog.

const CELESTIAL_BRIDGE_PATH := "res://scripts/environment/CelestialBridge.cs"

# === Time Settings ===
@export_group("Time")
## Current time of day (0.0 = midnight, 0.5 = noon, 1.0 = midnight). A game
## opens at late morning: most scenarios start on 1 January, when the
## northern sun is up for a few hours around noon at most.
@export_range(0.0, 1.0) var time_of_day: float = 0.45
## Minutes per real second (game time speed)
@export var time_scale: float = 2.0
## Pause the cycle
@export var paused: bool = false

# === Calendar (from the sim) ===
@export_group("Calendar")
## Latitude in degrees north: the scenario region's (Vestland by default).
@export var latitude_deg: float = 60.39
## Sim day (0 = 1 January); the season follows it.
@export var sim_day: int = 172

signal time_changed(hour: float)
signal new_day()
signal sunrise()
signal sunset()

var _celestial: RefCounted = null
var _sky: Dictionary = {}
var _was_day: bool = true

## Season as a fraction of the year from 1 January (0 winter, 0.5 summer),
## like SimState.season01. Setting it moves the calendar day.
var season: float:
	get:
		return float(sim_day % 365) / 365.0
	set(value):
		sim_day = int(round(fposmod(value, 1.0) * 365.0)) % 365
		_refresh()


func _ready() -> void:
	_refresh()
	_was_day = is_daytime()


func _process(delta: float) -> void:
	if not paused:
		time_of_day += time_scale * delta / (24.0 * 60.0)
		if time_of_day >= 1.0:
			time_of_day -= 1.0
			new_day.emit()
	_refresh()
	time_changed.emit(get_hour())
	_check_day_night_transitions()


## Calendar from the sim: latitude (deg N) and the sim day.
func set_calendar(latitude: float, day: int) -> void:
	latitude_deg = clampf(latitude, -89.0, 89.0)
	sim_day = maxi(day, 0)
	_refresh()


## Set time directly (0-24 hours)
func set_hour(hour: float) -> void:
	time_of_day = fposmod(hour / 24.0, 1.0)
	_refresh()


func get_hour() -> float:
	return time_of_day * 24.0


func get_day_of_year() -> int:
	return int(_sky.get("day_of_year", sim_day % 365 + 1))


## Unit vector toward the sun in world space (north +X, up +Y, east +Z).
func get_sun_direction() -> Vector3:
	return _sky.get("sun_direction", Vector3(0.0, 0.5, -0.866)).normalized()


func get_sun_elevation_deg() -> float:
	return float(_sky.get("sun_elevation", 30.0))


func get_moon_direction() -> Vector3:
	return _sky.get("moon_direction", Vector3.DOWN).normalized()


func get_moon_elevation_deg() -> float:
	return float(_sky.get("moon_elevation", -30.0))


## Lit fraction of the moon's disc (0 new, 1 full).
func get_moon_illumination() -> float:
	return float(_sky.get("moon_illumination", 0.0))


## The night's aurora before darkness and cloud: brightness 0-1, Kp, and the
## ground distance (km) to the brightest curtains, positive to the north.
func get_aurora() -> Dictionary:
	return {
		"brightness": float(_sky.get("aurora_brightness", 0.0)),
		"kp": float(_sky.get("aurora_kp", 0.0)),
		"offset_km": float(_sky.get("aurora_offset_km", 400.0)),
	}


## Get time as formatted string (HH:MM)
func get_time_string() -> String:
	var hour := get_hour()
	var hours := int(hour)
	var minutes := int(fmod(hour * 60.0, 60.0))
	return "%02d:%02d" % [hours, minutes]


## Daytime: the sun's centre is above the horizon.
func is_daytime() -> bool:
	return get_sun_elevation_deg() > 0.0


## Normalized sun height (0 = horizon or below, 1 = zenith).
func get_sun_height() -> float:
	return maxf(get_sun_direction().y, 0.0)


func _refresh() -> void:
	if _celestial == null:
		var script: Resource = load(CELESTIAL_BRIDGE_PATH) if ResourceLoader.exists(CELESTIAL_BRIDGE_PATH) else null
		if script == null or not script.can_instantiate():
			push_error("[TimeOfDay] CelestialBridge.cs is unavailable (is the C# assembly built?)")
			return
		_celestial = script.new()
	# The hour moves the sun, the moon and the aurora inside the sim day.
	_sky = _celestial.call("Sky", latitude_deg, sim_day, get_hour())


func _check_day_night_transitions() -> void:
	var is_day := is_daytime()
	if is_day and not _was_day:
		sunrise.emit()
	elif not is_day and _was_day:
		sunset.emit()
	_was_day = is_day
