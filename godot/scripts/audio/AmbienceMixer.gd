class_name AmbienceMixer
extends Node

## The fjord's ambience bed (roadmap Au2-01): every layer is a 60 s loop
## (tools/audio/gen_audio.py) playing all the time on the Ambient bus, each at
## a gain the world on screen asks for, eased so nothing jumps:
##   water_lap      small waves at the floats; fades as the sea rises
##   storm          breaking waves and the sea's roar, with the waves' height
##   wind_light     a breeze, with the wind
##   wind_strong    a gale, above ~10 m/s
##   rain_light / rain_heavy   with the rain the weather draws (mm/h)
##   gulls          on calm, dry days, louder near the water
##   underwater     alone, when the camera is below the surface
## The inputs are what the player sees: the live fjord's WeatherController
## (the EnvironmentDirector's weather_system; rain, wind, waves) and the
## camera (its height above the water, under the surface). Without a fjord
## on screen it falls back to the sim's weather (SimBridge wind and state).
## gains_for() is the pure rule (tests call it); the players only follow.

## Layer -> MusicLibrary.AMBIENT_BASE key.
const LAYERS := {
	"water_lap": "water_lap",
	"storm": "storm",
	"wind_light": "fjord_wind_light",
	"wind_strong": "fjord_wind_strong",
	"rain_light": "rain_light",
	"rain_heavy": "rain_heavy",
	"gulls": "gulls",
	"underwater": "underwater",
}
## Each layer's level at full presence (linear, before the Ambient bus): the
## files are all -24 LUFS, so these set the bed's balance.
const LAYER_LEVEL := {
	"water_lap": 0.75, "storm": 0.9, "wind_light": 0.5, "wind_strong": 0.8,
	"rain_light": 0.55, "rain_heavy": 0.75, "gulls": 0.45, "underwater": 0.85,
}
## How fast a layer's gain follows its target (linear units per second).
const EASE_PER_S := 0.35
## Wind (m/s): a breeze is full at WIND_LIGHT_FULL; the gale layer comes in
## from WIND_GALE_FROM and is full at WIND_GALE_FULL (Beaufort 6 is
## 10.8-13.8 m/s, 8 is 17.2-20.7 m/s).
const WIND_LIGHT_FULL := 7.0
const WIND_GALE_FROM := 9.0
const WIND_GALE_FULL := 18.0
## Significant wave height (m): the calm water's laps fade out by
## SEA_ROUGH_FULL, where the storm layer is full.
const SEA_ROUGH_FROM := 0.8
const SEA_ROUGH_FULL := 3.0
## Rain rate (mm/h, the weather's own measure). Light rain is under 2.5 mm/h,
## moderate 2.5-7.6, heavy above 7.6 (AMS Glossary of Meteorology, "rain"):
## the light layer is full at RAIN_LIGHT_FULL, the heavy one at RAIN_HEAVY_FULL.
const RAIN_LIGHT_FULL := 2.5
const RAIN_HEAVY_FULL := 7.6
## Gulls go quiet in rain heavier than this (mm/h).
const GULLS_RAIN_MAX := 1.0
## Gulls go quiet above this wind (m/s) and in rain.
const GULLS_WIND_MAX := 14.0
## Camera height above the water (m) at which the near layers (laps,
## gulls) have fallen to their floor.
const NEAR_LAYER_FAR_M := 600.0
const SILENT_DB := -80.0

var _players: Dictionary = {}       # layer -> AudioStreamPlayer
var _gain: Dictionary = {}          # layer -> current linear gain
var _target: Dictionary = {}        # layer -> target linear gain
var _files: AudioStreamCache = null
var _bus := "Ambient"
## The last inputs (tests and the debug overlay read them).
var last_inputs: Dictionary = {}


func setup(files: AudioStreamCache, bus: String) -> void:
	_files = files
	_bus = bus
	for layer in LAYERS:
		var p := AudioStreamPlayer.new()
		p.name = "Amb_" + layer
		p.bus = bus
		p.volume_db = SILENT_DB
		add_child(p)
		_players[layer] = p
		_gain[layer] = 0.0
		_target[layer] = 0.0
		var path := MusicLibrary.ambient_base(LAYERS[layer])
		if _files.exists(path):
			_files.later(path, _start_layer.bind(layer))
		else:
			push_warning("AmbienceMixer: missing loop " + path)


func _start_layer(stream: AudioStream, layer: String) -> void:
	var p: AudioStreamPlayer = _players[layer]
	p.stream = stream
	p.play()


## The target gain of every layer for these inputs (pure):
## wind_ms, rain (mm/h), wave_m (significant height), camera_height_m,
## underwater (bool).
static func gains_for(inputs: Dictionary) -> Dictionary:
	var wind := float(inputs.get("wind_ms", 4.0))
	var rain := maxf(float(inputs.get("rain", 0.0)), 0.0)
	var wave := float(inputs.get("wave_m", 0.3))
	var height := maxf(float(inputs.get("camera_height_m", 120.0)), 0.0)
	var out := {}
	for layer in LAYERS:
		out[layer] = 0.0
	if bool(inputs.get("underwater", false)):
		out["underwater"] = 1.0
		return out
	var near := clampf(1.0 - height / NEAR_LAYER_FAR_M, 0.3, 1.0)
	var rough := clampf((wave - SEA_ROUGH_FROM) / (SEA_ROUGH_FULL - SEA_ROUGH_FROM), 0.0, 1.0)
	var gale := clampf((wind - WIND_GALE_FROM) / (WIND_GALE_FULL - WIND_GALE_FROM), 0.0, 1.0)
	var heavy := clampf((rain - RAIN_LIGHT_FULL) / (RAIN_HEAVY_FULL - RAIN_LIGHT_FULL), 0.0, 1.0)
	out["water_lap"] = (1.0 - rough) * near
	out["storm"] = rough
	out["wind_light"] = clampf(wind / WIND_LIGHT_FULL, 0.15, 1.0) * (1.0 - 0.7 * gale)
	out["wind_strong"] = gale
	out["rain_light"] = clampf(rain / RAIN_LIGHT_FULL, 0.0, 1.0) * (1.0 - 0.6 * heavy)
	out["rain_heavy"] = heavy
	out["gulls"] = clampf(1.0 - wind / GULLS_WIND_MAX, 0.0, 1.0) * (1.0 - clampf(rain / GULLS_RAIN_MAX, 0.0, 1.0)) * near
	return out


## Read the world on screen (or the sim) and set the targets.
func sense(tree: SceneTree, sim: Node) -> void:
	var inputs := {}
	var director := tree.get_first_node_in_group(RenderQuality.DIRECTOR_GROUP) if tree != null else null
	var weather: Variant = director.get("weather_system") if director != null else null
	if weather != null and is_instance_valid(weather):
		inputs["wind_ms"] = weather.get_wind_speed_ms()
		inputs["rain"] = weather.get_rain()
		inputs["wave_m"] = weather.get_wave_height_m()
	elif sim != null:
		inputs["wind_ms"] = float(sim.get("wind_ms")) if sim.get("wind_ms") != null else 4.0
		var state := str(sim.get("audio_weather_state")).to_lower()
		inputs["wave_m"] = 3.0 if state in ["storm", "severe"] else (1.2 if state in ["wind", "moderate"] else 0.3)
	var cam: Camera3D = tree.root.get_viewport().get_camera_3d() if tree != null else null
	if cam != null:
		inputs["camera_height_m"] = cam.global_position.y
		# The camera itself under the surface (G3-04: the UNDERWATER view
		# hovers over its pen before it dives; a framing takes it out).
		inputs["underwater"] = cam is FjordCamera and (cam as FjordCamera).is_below_surface()
	last_inputs = inputs
	set_targets(gains_for(inputs))


func set_targets(targets: Dictionary) -> void:
	for layer in targets:
		if _target.has(layer):
			_target[layer] = float(targets[layer])


## Ease every layer toward its target and set its volume.
func step(delta: float) -> void:
	for layer in _players:
		var g := move_toward(float(_gain[layer]), float(_target[layer]), EASE_PER_S * delta)
		_gain[layer] = g
		var level := g * float(LAYER_LEVEL[layer])
		(_players[layer] as AudioStreamPlayer).volume_db = SILENT_DB if level <= 0.0005 else linear_to_db(level)


## Jump to the targets at once (a load, a cut to another view, tests).
func settle() -> void:
	for layer in _target:
		_gain[layer] = _target[layer]
	step(0.0)


func gain_of(layer: String) -> float:
	return float(_gain.get(layer, 0.0))


func player_of(layer: String) -> AudioStreamPlayer:
	return _players.get(layer)
