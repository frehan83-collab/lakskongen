class_name StormSway
extends Node
## The camera sways in a storm (G3-05): a slow, gusty drift of the view, as
## if the viewpoint rode the wind, from a strong breeze up. Additive and
## small: like CameraShake (U2-06) it moves only Camera3D.h_offset and
## v_offset (a sideways and upward shift of the view that leaves the
## camera's transform, FjordCamera's rig, alone) and takes back exactly what
## it added, so it composes with a shake. Its size is a share of the view
## height at the camera's distance, the same on screen at FARM and COAST.
##
## Reduced motion ("Redusert bevegelse", or the operating system's own
## setting) turns it off: the view holds still and any sway in progress
## settles back at once. EnvironmentDirector hands it the wind every frame.

const NODE_NAME := "StormSway"
## No sway below this wind (m/s); full sway at FULL_WIND_MS.
const FROM_WIND_MS := 11.0
const FULL_WIND_MS := 24.0
## Peak offset at full sway, as a share of the view height (about 5 px at
## 1080p: felt more than seen).
const MAX_SCREEN_FRACTION := 0.0045
## The sway's own motion (noise units per second) and the gusts' (slower).
const SWAY_SPEED := 0.35
const GUST_SPEED := 0.06
## How fast the sway follows the wind and settles (1/s).
const SETTLE_RATE := 1.5
const FALLBACK_DISTANCE := 400.0
const SETTINGS_PATH := "user://settings.json"
const MOTION_PREFS_PATH := "res://scripts/ui/MotionPrefs.gd"

var wind_ms := 0.0
## Sway level 0-1 (follows the wind smoothly) and the offset applied now (m).
var level := 0.0
var applied := Vector2.ZERO
## Largest offset (m) applied so far (QA and tests).
var peak_offset := 0.0
## Test seam: -1 asks the player's settings, 0 forces motion on, 1 forces
## reduced motion (takes effect at the next preference read; tests call
## refresh_prefs()).
static var forced_reduced := -1

## The reduced-motion preference is read again this often (s).
const PREFS_REFRESH_S := 1.0

var _time := 0.0
var _noise := FastNoiseLite.new()
var _reduced := false
var _prefs_age := PREFS_REFRESH_S


## The sway node on a camera, created on first use.
static func ensure(cam: Camera3D) -> StormSway:
	if cam == null:
		return null
	var node := cam.get_node_or_null(NODE_NAME) as StormSway
	if node == null:
		node = StormSway.new()
		node.name = NODE_NAME
		cam.add_child(node)
	return node


func _ready() -> void:
	_noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_noise.seed = 3301
	_noise.frequency = 1.0


func set_wind(speed_ms: float) -> void:
	wind_ms = maxf(speed_ms, 0.0)


## Read the reduced-motion preference now.
func refresh_prefs() -> void:
	_prefs_age = PREFS_REFRESH_S


## The sway's target level for a wind (0-1).
static func level_for(speed_ms: float) -> float:
	return smoothstep(FROM_WIND_MS, FULL_WIND_MS, speed_ms)


func _process(delta: float) -> void:
	var cam := get_parent() as Camera3D
	if cam == null:
		return
	_prefs_age += delta
	if _prefs_age >= PREFS_REFRESH_S:
		_prefs_age = 0.0
		_reduced = reduced_motion()
	if _reduced:
		level = 0.0
	else:
		level = lerpf(level, level_for(wind_ms), 1.0 - exp(-SETTLE_RATE * delta))
	_time += delta
	var offset := Vector2.ZERO
	if level > 0.0:
		var gust := 0.55 + 0.45 * _noise.get_noise_2d(_time * GUST_SPEED, 7.3)
		var amplitude := _view_height(cam) * MAX_SCREEN_FRACTION * level * gust
		offset = Vector2(_noise.get_noise_2d(_time * SWAY_SPEED, 0.0),
			_noise.get_noise_2d(31.0, _time * SWAY_SPEED * 0.8) * 0.6) * amplitude
	cam.h_offset += offset.x - applied.x
	cam.v_offset += offset.y - applied.y
	applied = offset
	peak_offset = maxf(peak_offset, offset.length())


func _exit_tree() -> void:
	var cam := get_parent() as Camera3D
	if cam != null:
		cam.h_offset -= applied.x
		cam.v_offset -= applied.y
	applied = Vector2.ZERO


## Reduced motion: MotionPrefs when the UI has it (U1-09), else the
## player's accessibility.reduced_motion setting or the OS's.
static func reduced_motion() -> bool:
	if forced_reduced >= 0:
		return forced_reduced == 1
	if ResourceLoader.exists(MOTION_PREFS_PATH):
		var prefs: Script = load(MOTION_PREFS_PATH)
		if prefs != null and prefs.get_script_method_list().any(func(m: Dictionary) -> bool: return m["name"] == "reduced_motion"):
			return bool(prefs.call("reduced_motion"))
	if FileAccess.file_exists(SETTINGS_PATH):
		var file := FileAccess.open(SETTINGS_PATH, FileAccess.READ)
		var data: Variant = JSON.parse_string(file.get_as_text()) if file != null else null
		if data is Dictionary:
			var acc: Variant = (data as Dictionary).get("accessibility", {})
			if acc is Dictionary and bool((acc as Dictionary).get("reduced_motion", false)):
				return true
	return DisplayServer.accessibility_should_reduce_animation() == 1


## Height of the view (m) at the distance the camera looks at.
func _view_height(cam: Camera3D) -> float:
	var distance := FALLBACK_DISTANCE
	if cam.has_method("get_current_distance"):
		distance = maxf(float(cam.call("get_current_distance")), 1.0)
	return 2.0 * distance * tan(deg_to_rad(cam.fov * 0.5))
