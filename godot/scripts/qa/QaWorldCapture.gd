extends Node
## Shared base of the farm-types QA capture scenes (F2-18 gray box, F2-25
## land-kit preview): boots the real game world, stages a godot/qa/shots.json
## shot (season, hour, weather, camera) and grabs frames, and isolates a set
## of nodes on a plain, unlit camera environment for class masks.
##
## A subclass sets SCENE_PATH-like data through scene_path(), then calls
## start() from _ready(); start() boots and hands over to run_shots().

const GAME_SCENE := "res://scenes/GameScene.tscn"
const SHOTS_PATH := "res://qa/shots.json"
const OUT_DIR := "res://screenshots/game"
## WeatherController.WeatherState ordinals.
const WEATHER := {"clear": 0, "overcast": 1, "rain": 2, "snow": 3, "storm": 4, "fog": 5}
const ZOOM_FJORD := 1

var _game: Node = null
var _env: Node = null
var _cam: Camera3D = null
var _director: Node = null
var _only := ""
var _srgb_lut := PackedFloat32Array()


## The res:// path of the capture scene; shots.json shots name it.
func scene_path() -> String:
	return ""


## Override: capture every shot, then quit.
func run_shots(_shots: Array) -> void:
	get_tree().quit(0)


## Checks the display, reads `--only <prefix>`, then boots and runs.
func start() -> void:
	if DisplayServer.get_name() == "headless":
		push_error("%s: no display; headless renders nothing. Run it windowed." % name)
		get_tree().quit(1)
		return
	var args := OS.get_cmdline_user_args()
	var i := args.find("--only")
	if i >= 0 and i + 1 < args.size():
		_only = args[i + 1]
	for k in 256:
		var c := float(k) / 255.0
		_srgb_lut.append(c / 12.92 if c <= 0.04045 else pow((c + 0.055) / 1.055, 2.4))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	_start.call_deferred()


func _start() -> void:
	var shots := shots_for(scene_path())
	if not await boot(shots):
		get_tree().quit(1)
		return
	var todo: Array = []
	for s in shots:
		if _only == "" or str(s["id"]).begins_with(_only):
			todo.append(s)
	await run_shots(todo)


## The shots in shots.json that a scene captures.
static func shots_for(path: String) -> Array:
	var data = JSON.parse_string(FileAccess.get_file_as_string(SHOTS_PATH))
	if not (data is Dictionary) or not (data.get("shots") is Array):
		return []
	var out: Array = []
	for s in data["shots"]:
		if s is Dictionary and str(s.get("scene", "")) == path:
			out.append(s)
	return out


# ── Boot ─────────────────────────────────────────────────────────────────────

func boot(shots: Array) -> bool:
	_game = load(GAME_SCENE).instantiate()
	add_child(_game)
	await wait(2.0)
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("start_qa_scenario"):
		push_error("[%s] SimBridge unavailable" % name)
		return false
	var first: Dictionary = shots[0] if not shots.is_empty() else {}
	var scenario := str(first.get("scenario", "po3_red_sone"))
	if not sim.start_qa_scenario(scenario, int(first.get("seed", 20261012))):
		push_error("[%s] scenario %s did not start" % [name, scenario])
		return false
	if sim.has_method("skip_tutorial"):
		sim.skip_tutorial()
	_env = _game.find_child("FjordEnvironment", true, false)
	_cam = _env.get("camera") if _env else null
	_director = _env.get("director") if _env else null
	if _cam == null or _director == null:
		push_error("[%s] camera or director missing" % name)
		return false
	# Pens arrive with the state ticks a few frames after the start; the
	# vista then makes the first pen the home focus, as in a real game.
	var pm := _game.find_child("FishPenManager", true, false)
	for i in 50:
		if pm and not pm.get_pen_ids().is_empty():
			break
		await wait(0.2)
	var vista := _game.find_child("FjordVistaBridge", true, false)
	if vista:
		vista.set("sim_weather_enabled", false)
		vista.call("_focus_first_pen")
	sim.set_paused(true)
	var ts = _env.get("time_system")
	if ts:
		ts.set("paused", true)
	var ws = _env.get("weather_system")
	if ws:
		ws.set("transition_duration", 0.15)
	_cam.set("auto_orbit_enabled", false)
	hide_overlays()
	await wait(1.0)
	return true


func hide_overlays() -> void:
	var ui := _game.find_child("UILayer", true, false)
	if ui:
		ui.visible = false
	var toasts := get_node_or_null("/root/ToastNotification") as CanvasLayer
	if toasts:
		toasts.visible = false


# ── Staging ──────────────────────────────────────────────────────────────────

## Season, hour and weather of a shot (the presentation clock is paused).
func stage_environment(shot: Dictionary) -> void:
	hide_overlays()
	_env.call("set_season", float(shot.get("season", 0.5)))
	_env.call("set_time", float(shot.get("hour", 12.0)))
	_env.call("set_weather", int(WEATHER.get(str(shot.get("weather", "clear")), 0)))


## The shipped home view, or a focus framing on `focus` (distance, pitch,
## yaw; the rig is held still so it cannot clamp an offshore focus).
func stage_camera(spec: Dictionary, focus: Vector3) -> Vector3:
	if str(spec.get("mode", "home")) == "home":
		_cam.set_process(true)
		_cam.call("set_top_down", false)
		_cam.call("set_pitch_offset", 0.0)
		_cam.set("_user_rotated", false)
		_cam.call("look_at_position", _cam.get("home_focus"), ZOOM_FJORD)
		_cam.call("_snap")
		return _cam.get("home_focus")
	if spec.has("focus_y"):
		focus.y = float(spec["focus_y"])
	var d := float(spec.get("distance_m", 520.0))
	var pitch := deg_to_rad(float(spec.get("pitch_deg", -40.0)))
	var yaw: float = deg_to_rad(float(spec["yaw_deg"])) if spec.has("yaw_deg") else float(_cam.call("home_yaw", focus))
	# The director reads the framing distance for shadows and depth of field.
	_cam.set_process(false)
	_cam.set("_distance", d)
	_cam.set("_distance_target", d)
	var horiz := Vector3(sin(yaw), 0.0, cos(yaw))
	_cam.global_position = focus + horiz * d * cos(pitch) + Vector3(0.0, -d * sin(pitch), 0.0)
	_cam.look_at(focus, Vector3.UP)
	_cam.fov = FjordCamera.view_for_distance(d).y
	return focus


# ── Capture ──────────────────────────────────────────────────────────────────

func grab() -> Image:
	DisplayServer.window_move_to_foreground()
	for i in 3:
		await RenderingServer.frame_post_draw
	var img := get_viewport().get_texture().get_image()
	img.convert(Image.FORMAT_RGBA8)
	return img


func save(img: Image, file_name: String) -> void:
	img.save_png(ProjectSettings.globalize_path(OUT_DIR).path_join(file_name))


## Hide every visual of the game except those under `keep`, put the camera
## on its own plain environment (no light, sky, fog or glow; a new
## Environment already tonemaps linearly, and the world's look stays the
## director's) and hold the director. Returns what end_plain() restores.
func begin_plain(keep: Array) -> Dictionary:
	var plain := Environment.new()
	plain.background_mode = Environment.BG_COLOR
	plain.background_color = Color.BLACK
	plain.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	plain.ambient_light_color = Color.BLACK
	plain.ambient_light_energy = 0.0
	plain.reflected_light_source = Environment.REFLECTION_SOURCE_DISABLED
	var hidden: Array[Node3D] = []
	for node in _game.find_children("*", "VisualInstance3D", true, false):
		if not node.visible:
			continue
		var kept := false
		for k in keep:
			if k != null and (node == k or k.is_ancestor_of(node)):
				kept = true
				break
		if not kept:
			node.visible = false
			hidden.append(node)
	var saved := {"env": _cam.environment, "attrs": _cam.attributes, "hidden": hidden}
	_director.set_process(false)
	_cam.environment = plain
	_cam.attributes = CameraAttributesPractical.new()
	return saved


func end_plain(saved: Dictionary) -> void:
	_cam.environment = saved["env"]
	_cam.attributes = saved["attrs"]
	for node in saved["hidden"]:
		node.visible = true
	_director.set_process(true)


static func unshaded(c: Color) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	m.albedo_color = c
	return m


func write_json(file_name: String, data: Dictionary) -> String:
	var path := ProjectSettings.globalize_path(OUT_DIR).path_join(file_name)
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_string(JSON.stringify(data, "  ", true) + "\n")
	f.close()
	return path


# ── Colour ───────────────────────────────────────────────────────────────────

## CIE L*a*b* (D65) of an sRGB8 pixel at byte offset o.
func lab(d: PackedByteArray, o: int) -> Vector3:
	var r := _srgb_lut[d[o]]
	var g := _srgb_lut[d[o + 1]]
	var b := _srgb_lut[d[o + 2]]
	var fx := _lab_f((0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047)
	var fy := _lab_f(0.2126 * r + 0.7152 * g + 0.0722 * b)
	var fz := _lab_f((0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883)
	return Vector3(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz))


static func _lab_f(t: float) -> float:
	return pow(t, 1.0 / 3.0) if t > 0.008856 else 7.787 * t + 16.0 / 116.0


func lstar(d: PackedByteArray, o: int) -> float:
	var y := 0.2126 * _srgb_lut[d[o]] + 0.7152 * _srgb_lut[d[o + 1]] + 0.0722 * _srgb_lut[d[o + 2]]
	return 116.0 * pow(y, 1.0 / 3.0) - 16.0 if y > 0.008856 else 903.3 * y


func wait(seconds: float) -> void:
	await get_tree().create_timer(seconds).timeout
