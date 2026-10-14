class_name WeatherController
extends Node3D
## Presentation weather for the fjord (G1-02).
##
## Holds the weather the world shows and blends it smoothly: cloud cover and
## thickness, visibility, rain, snow, sea fog, wind and waves. The targets
## come from the sim (apply_sky_context(), fed by FjordVistaBridge from
## AtmosphereModel) or, for staging, from a named preset (set_weather()).
## Owns its rain and snow particles. Writes nothing else: EnvironmentDirector
## reads the blended values and is the only writer of fog, sky, light and
## the water's wave uniforms.

# === Weather States ===
enum WeatherState { CLEAR, OVERCAST, RAIN, SNOW, STORM, FOG }

@export var current_weather: WeatherState = WeatherState.CLEAR
@export var transition_duration: float = 10.0  # Seconds to blend between states

# === Node References ===
@export_group("Nodes")
@export var rain_particles: GPUParticles3D
@export var snow_particles: GPUParticles3D

## Staging presets, in the same physical units AtmosphereModel derives from
## the sim: cloud cover and thickness (0-1), visibility (km), rain and snow
## (mm/h water equivalent), sea fog (0-1), wind (m/s), significant wave
## height (m). The visibilities sit inside AtmosphereModel's bands.
const WEATHER_PRESETS := {
	WeatherState.CLEAR:    { "cloud": 0.18, "density": 0.15, "visibility_km": 60.0, "rain": 0.0, "snow": 0.0, "sea_fog": 0.0, "wind_ms": 4.0,  "wave_m": 0.3 },
	WeatherState.OVERCAST: { "cloud": 0.88, "density": 0.45, "visibility_km": 20.0, "rain": 0.0, "snow": 0.0, "sea_fog": 0.0, "wind_ms": 7.0,  "wave_m": 0.9 },
	WeatherState.RAIN:     { "cloud": 0.96, "density": 0.7,  "visibility_km": 4.5,  "rain": 2.5, "snow": 0.0, "sea_fog": 0.0, "wind_ms": 9.0,  "wave_m": 1.2 },
	WeatherState.SNOW:     { "cloud": 0.94, "density": 0.55, "visibility_km": 2.5,  "rain": 0.0, "snow": 0.8, "sea_fog": 0.0, "wind_ms": 6.0,  "wave_m": 0.8 },
	WeatherState.STORM:    { "cloud": 1.0,  "density": 1.0,  "visibility_km": 2.0,  "rain": 7.0, "snow": 0.0, "sea_fog": 0.0, "wind_ms": 16.0, "wave_m": 3.0 },
	WeatherState.FOG:      { "cloud": 0.35, "density": 0.3,  "visibility_km": 0.4,  "rain": 0.0, "snow": 0.0, "sea_fog": 1.0, "wind_ms": 2.0,  "wave_m": 0.2 },
}

## Particle counts at 1 mm/h (rain) and 1 mm/h water equivalent (snow).
const RAIN_PER_MM := 900.0
const SNOW_PER_MM := 5000.0

var _target: Dictionary = {}
var _current: Dictionary = {}
var _start: Dictionary = {}
var _transition_progress: float = 1.0
var _wind_direction: Vector2 = Vector2(1.0, 0.3).normalized()
var _was_storm: bool = false

# Weather affects gameplay
signal weather_changed(state: WeatherState)
signal storm_started()
signal storm_ended()
signal fog_visibility_changed(factor: float)  # 0.0 = blind, 1.0 = clear


func _ready() -> void:
	_current = WEATHER_PRESETS[current_weather].duplicate()
	_target = _current.duplicate()
	_start = _current.duplicate()
	_apply_particles()


func _process(delta: float) -> void:
	if _transition_progress < 1.0:
		_transition_progress = minf(_transition_progress + delta / maxf(transition_duration, 0.001), 1.0)
		_blend(_transition_progress)
		_apply_particles()
	# The wind veers slowly (presentation only).
	var wind_angle := atan2(_wind_direction.y, _wind_direction.x)
	wind_angle += sin(Time.get_ticks_msec() * 0.0001) * 0.001
	_wind_direction = Vector2(cos(wind_angle), sin(wind_angle))
	_update_particle_positions()


## Stage a named weather (presets above).
func set_weather(state: WeatherState) -> void:
	if state == current_weather and _transition_progress >= 1.0 and _target == WEATHER_PRESETS[state]:
		return
	_retarget(WEATHER_PRESETS[state].duplicate())
	_set_state(state)


## Weather from the sim (GameBridge.GetSkyContext): the regime picks the
## named state for particles and gameplay signals; the numbers drive the look.
func apply_sky_context(ctx: Dictionary) -> void:
	if ctx.is_empty():
		return
	var precip := float(ctx.get("precip_mm_h", 0.0))
	var snow := bool(ctx.get("snow", false))
	var regime := str(ctx.get("weather_state", "Calm"))
	var stormy := regime == "Storm" or regime == "Severe"
	var target := {
		"cloud": float(ctx.get("cloud_cover", 0.2)),
		"density": 1.0 if regime == "Severe" else (0.9 if stormy else (0.6 if precip > 0.0 else 0.3)),
		"visibility_km": float(ctx.get("visibility_km", 40.0)),
		"rain": 0.0 if snow else precip,
		"snow": precip if snow else 0.0,
		"sea_fog": float(ctx.get("sea_fog", 0.0)),
		"wind_ms": float(ctx.get("wind_ms", 4.0)),
		"wave_m": float(ctx.get("wave_m", 0.3)),
	}
	var state := WeatherState.CLEAR
	if stormy:
		state = WeatherState.STORM
	elif target["sea_fog"] > 0.5:
		state = WeatherState.FOG
	elif snow and precip > 0.0:
		state = WeatherState.SNOW
	elif precip > 0.0:
		state = WeatherState.RAIN
	elif target["cloud"] > 0.7:
		state = WeatherState.OVERCAST
	if _target == target:
		return
	_retarget(target)
	_set_state(state)


func _retarget(target: Dictionary) -> void:
	_start = _current.duplicate()
	_target = target
	_transition_progress = 0.0 if transition_duration > 0.0 else 1.0
	if _transition_progress >= 1.0:
		_blend(1.0)
		_apply_particles()


func _set_state(state: WeatherState) -> void:
	var changed := state != current_weather
	current_weather = state
	if changed:
		weather_changed.emit(state)
	var storm := state == WeatherState.STORM
	if storm and not _was_storm:
		storm_started.emit()
	elif not storm and _was_storm:
		storm_ended.emit()
	_was_storm = storm


func _blend(t: float) -> void:
	var eased := t * t * (3.0 - 2.0 * t)
	for key in _target:
		var a: float = _start.get(key, _target[key])
		var b: float = _target[key]
		if key == "visibility_km":
			# Blend the extinction (1/visibility), which is what the eye sees.
			_current[key] = 1.0 / lerpf(1.0 / maxf(a, 0.01), 1.0 / maxf(b, 0.01), eased)
		else:
			_current[key] = lerpf(a, b, eased)
	fog_visibility_changed.emit(get_visibility())


func _apply_particles() -> void:
	var rain: float = _current.get("rain", 0.0)
	if rain_particles:
		rain_particles.emitting = rain > 0.05
		_set_amount(rain_particles, int(clampf(rain * RAIN_PER_MM, 1.0, 8000.0)))
	var snow: float = _current.get("snow", 0.0)
	if snow_particles:
		snow_particles.emitting = snow > 0.02
		_set_amount(snow_particles, int(clampf(snow * SNOW_PER_MM, 1.0, 6000.0)))


## Changing `amount` restarts the emitter, so a blend that set it every
## frame left the sky empty: only steps of 20 % or more take effect.
static func _set_amount(particles: GPUParticles3D, amount: int) -> void:
	if absi(amount - particles.amount) > maxi(1, int(particles.amount * 0.2)):
		particles.amount = amount


func _update_particle_positions() -> void:
	# Rain and snow fall around the camera so they always cover the view.
	var cam := get_viewport().get_camera_3d() if is_inside_tree() else null
	if not cam:
		return
	var cam_pos := cam.global_position
	if rain_particles:
		rain_particles.global_position = Vector3(cam_pos.x, cam_pos.y + 30.0, cam_pos.z)
	if snow_particles:
		snow_particles.global_position = Vector3(cam_pos.x, cam_pos.y + 10.0, cam_pos.z)


## Blended values, for the director.
func get_cloud_cover() -> float:
	return _current.get("cloud", 0.2)


## Cloud thickness: 0 fair weather, 1 storm.
func get_cloud_density() -> float:
	return _current.get("density", 0.2)


## Meteorological optical range in metres.
func get_visibility_m() -> float:
	return _current.get("visibility_km", 40.0) * 1000.0


## 1 while a sea-fog layer lies on the water.
func get_sea_fog() -> float:
	return _current.get("sea_fog", 0.0)


## Rain and snow rates (mm/h water equivalent).
func get_rain() -> float:
	return _current.get("rain", 0.0)


func get_snow() -> float:
	return _current.get("snow", 0.0)


func get_wind_speed_ms() -> float:
	return _current.get("wind_ms", 4.0)


func get_wave_height_m() -> float:
	return _current.get("wave_m", 0.3)


## Unit wind direction (x, z) the water and clouds drift with.
func get_wind_direction() -> Vector2:
	return _wind_direction


## Wind as a vector: direction * speed (m/s).
func get_wind() -> Vector2:
	return _wind_direction * get_wind_speed_ms()


## Visibility factor for gameplay (0 blind, 1 clear): 1 at 20 km and beyond.
func get_visibility() -> float:
	return clampf(get_visibility_m() / 20000.0, 0.0, 1.0)


## Is it currently stormy? (gameplay: damage pens)
func is_stormy() -> bool:
	return current_weather == WeatherState.STORM


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
