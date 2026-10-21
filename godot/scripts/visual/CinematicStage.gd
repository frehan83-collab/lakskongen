class_name CinematicStage
extends RefCounted
## What a cinematic borrows from the game and gives back (roadmap F2-23: the
## first sight and the teaser, FirstSightDirector and TeaserDirector).
##
## enter() holds the sim clock (SimBridge.hold_clock under the cinematic's
## own reason), pins the presentation hour to the plan's sun (D20: the
## presentation clock, never the sim's), stages clear weather (the sim's
## weather stops driving the sky until leave()), leans the mood
## (EnvironmentDirector.set_mood_hook), hides the game's UI and its toasts,
## closes the facility card and stops the camera rig; leave() gives every one
## of them back as it was and releases the clock. None of it is engine state:
## the sim day and every stream stand where they stood (UiFlowTests).

const CELESTIAL_BRIDGE_PATH := "res://scripts/environment/CelestialBridge.cs"

var manager: FarmFacilityVisualManager = null
## The SimBridge clock-hold reason ("first_sight", "teaser").
var hold_reason := ""
var active := false

var _saved: Dictionary = {}
var _celestial: RefCounted = null


func _init(presenter: FarmFacilityVisualManager, reason: String) -> void:
	manager = presenter
	hold_reason = reason


## Borrow the world for a plan: {"sun_elevation_deg", "sun_rising", "mood",
## "mood_strength"}.
func enter(plan: Dictionary) -> void:
	var sim := _sim()
	if sim != null and sim.has_method("hold_clock"):
		sim.hold_clock(hold_reason)
	var ts := time_system()
	var ws := weather_system()
	var vista := _vista()
	_saved = {
		"hour": ts.get_hour() if ts != null else 12.0,
		"time_paused": ts.paused if ts != null else false,
		"sim_weather": vista.get("sim_weather_enabled") if vista != null else true,
		"transition": ws.transition_duration if ws != null else 10.0,
		"ui_visible": ui_layer().visible if ui_layer() != null else true,
		"toasts_visible": _toast_layer().visible if _toast_layer() != null else true,
		"cam_process": manager.camera.is_processing() if manager.camera != null else true,
		"cam_input": manager.camera.get("input_enabled") if manager.camera != null else true,
	}
	if ts != null:
		ts.paused = true
		ts.set_hour(hour_for_sun(ts.latitude_deg, ts.sim_day, float(plan.get("sun_elevation_deg", -3.0)),
			bool(plan.get("sun_rising", false))))
	if vista != null:
		vista.set("sim_weather_enabled", false)
	if ws != null:
		ws.transition_duration = 0.0
		ws.set_weather(WeatherController.WeatherState.CLEAR)
	var director := environment_director()
	if director != null:
		director.set_mood_hook(str(plan.get("mood", "")), float(plan.get("mood_strength", 1.0)))
		director.compose(0.0, true)
	if ui_layer() != null:
		ui_layer().visible = false
	if _toast_layer() != null:
		_toast_layer().visible = false
	if manager.card != null:
		manager.card.close()
	if manager.camera != null:
		# The rig stands still and takes no keys (G, Home, T) while the
		# cinematic places the camera itself.
		manager.camera.set_process(false)
		manager.camera.set("input_enabled", false)
	active = true


## Give everything back: the hour, the weather (the sim's again), the mood,
## the UI, the camera rig's processing, the clock. The camera's pose is the
## cinematic's to set before it calls this.
func leave() -> void:
	if not active:
		return
	active = false
	var ts := time_system()
	var ws := weather_system()
	var vista := _vista()
	var director := environment_director()
	if director != null:
		director.set_mood_hook("", 0.0)
	if ts != null:
		ts.set_hour(float(_saved.get("hour", 12.0)))
		ts.paused = bool(_saved.get("time_paused", false))
	if ws != null:
		ws.transition_duration = float(_saved.get("transition", 10.0))
	if vista != null:
		vista.set("sim_weather_enabled", bool(_saved.get("sim_weather", true)))
		var sim := _sim()
		if sim != null and vista.has_method("_sync_environment"):
			vista.call("_sync_environment", sim)
	if director != null:
		director.compose(0.0, true)
	if ui_layer() != null:
		ui_layer().visible = bool(_saved.get("ui_visible", true))
	if _toast_layer() != null:
		_toast_layer().visible = bool(_saved.get("toasts_visible", true))
	if manager.camera != null:
		manager.camera.set_process(bool(_saved.get("cam_process", true)))
		manager.camera.set("input_enabled", bool(_saved.get("cam_input", true)))
	var sim := _sim()
	if sim != null and sim.has_method("release_clock"):
		sim.release_clock(hold_reason)


## The hour of today (presentation clock) when the sun, going down after
## noon, stands at `elevation_deg` (the dusk's blue hour, the afternoon's low
## winter sun), or with `rising` the morning's hour going up to it (the
## teaser's sun at the camera's back, looking out west to sea); a day whose
## sun never gets there takes the hour it comes closest (polar night: noon;
## midnight sun: midnight).
func hour_for_sun(latitude: float, day: int, elevation_deg: float, rising := false) -> float:
	if _celestial == null:
		var script: Resource = load(CELESTIAL_BRIDGE_PATH) if ResourceLoader.exists(CELESTIAL_BRIDGE_PATH) else null
		if script == null or not (script as Script).can_instantiate():
			return 18.0
		_celestial = script.new()
	var start := 0.0 if rising else 12.0
	var best_h := 12.0
	var best_d := INF
	var prev := _sun_elevation(latitude, day, start)
	# From midnight up (rising) or from noon down: the first crossing.
	for k in range(1, 121 if rising else 145):
		var h := start + float(k) * 0.1
		var e := _sun_elevation(latitude, day, fmod(h, 24.0))
		if not rising and prev >= elevation_deg and e < elevation_deg:
			return fmod(h - 0.1 * (elevation_deg - e) / maxf(prev - e, 1e-4), 24.0)
		if rising and prev < elevation_deg and e >= elevation_deg:
			return h - 0.1 * (e - elevation_deg) / maxf(e - prev, 1e-4)
		var d := absf(e - elevation_deg)
		if d < best_d:
			best_d = d
			best_h = fmod(h, 24.0)
		prev = e
	return best_h


func _sun_elevation(latitude: float, day: int, hour: float) -> float:
	return float((_celestial.call("Sky", latitude, day, hour) as Dictionary).get("sun_elevation", 0.0))


# ── Nodes of the game scene ──────────────────────────────────────────────────

func time_system() -> TimeOfDay:
	var env := manager.get_parent()
	return env.get("time_system") if env != null else null


func weather_system() -> WeatherController:
	var env := manager.get_parent()
	return env.get("weather_system") if env != null else null


func environment_director() -> EnvironmentDirector:
	var env := manager.get_parent()
	return env.get("director") if env != null and "director" in env else null


func scene_root() -> Node:
	return manager.owner


func ui_layer() -> CanvasLayer:
	var root := scene_root()
	return root.get_node_or_null("UILayer") as CanvasLayer if root != null else null


func game_ui() -> Node:
	var root := scene_root()
	return root.get_node_or_null("UILayer/GameUI") if root != null else null


func _toast_layer() -> CanvasLayer:
	return manager.get_node_or_null("/root/ToastNotification") as CanvasLayer


func _vista() -> Node:
	var root := scene_root()
	return root.get_node_or_null("FjordVistaBridge") if root != null else null


func _sim() -> Node:
	return manager.get_node_or_null("/root/SimBridge")
