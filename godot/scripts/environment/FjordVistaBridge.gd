class_name FjordVistaBridge
extends Node

## Wires the 3D fjord vista into GameScene without touching simulation code.
## - Syncs FishPenManager pens from SimBridge site data on state updates.
## - Opens the pen detail tab when a 3D pen is clicked.
## - Exposes focus_selected_pen() for the "fly to farm" camera button.

@export var pen_manager_path: NodePath
@export var camera_path: NodePath
@export var game_ui_path: NodePath

var pen_manager: FishPenManager = null
var vista_camera: FjordCamera = null
var game_ui: Control = null
var selected_pen_id: String = ""
## When false, sim weather/waves/season do NOT touch the 3D environment.
## Used by the screenshot harness, which stages each weather regime itself —
## otherwise every sim tick overrides the staged shot weather.
var sim_weather_enabled: bool = true

signal farm_focused(pen_id: String)


func _ready() -> void:
	pen_manager = get_node_or_null(pen_manager_path) as FishPenManager
	vista_camera = get_node_or_null(camera_path) as FjordCamera
	game_ui = get_node_or_null(game_ui_path)
	if pen_manager:
		pen_manager.pen_clicked.connect(_on_pen_clicked)
		call_deferred("_initial_sync")
	var sim := get_node_or_null("/root/SimBridge")
	if sim and sim.has_signal("state_updated"):
		sim.state_updated.connect(_on_state_updated)


func has_selection() -> bool:
	return selected_pen_id != "" and pen_manager != null and selected_pen_id in pen_manager.get_pen_ids()


func focus_selected_pen() -> bool:
	if not has_selection() or vista_camera == null:
		return false
	_fly_to(pen_manager.get_pen_position(selected_pen_id))
	farm_focused.emit(selected_pen_id)
	return true


func show_pen_detail(pen_id: String) -> bool:
	if game_ui == null or not game_ui.has_method("show_tab"):
		return false
	game_ui.show_tab("pens")
	var panel := _find_panel("Panel_pens")
	if panel and panel.has_method("select_pen"):
		panel.select_pen(pen_id)
		return true
	return false


func _find_panel(panel_name: String) -> Control:
	if game_ui == null:
		return null
	var container := game_ui.get_node_or_null("%PanelContainer")
	if container == null:
		container = game_ui
	return container.find_child(panel_name, true, false) as Control


func _initial_sync() -> void:
	_on_state_updated()


func _on_state_updated() -> void:
	if pen_manager == null:
		return
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_all_pens"):
		return
	pen_manager.sync_pens_from_sim(sim.get_all_pens())
	_sync_environment(sim)


## Living-fjord sync (~1 Hz via state_updated): sim weather drives the 3D
## sky, sim waves/wind drive the water shader, sim season drives the clock.
## Everything is guarded — a missing node skips quietly, never errors.
func _sync_environment(sim: Node) -> void:
	if not sim_weather_enabled:
		return
	var env := pen_manager.get_parent() as Node
	if env == null or not env.has_method("set_weather"):
		return
	# Weather regime: Calm -> CLEAR, Moderate -> OVERCAST, Storm/Severe -> STORM.
	var w := str(sim.get("weather_state") if "weather_state" in sim else "Calm")
	var wstate := 0 # WeatherController.WeatherState.CLEAR
	if w == "Moderate":
		wstate = 1 # OVERCAST
	elif w == "Storm" or w == "Severe":
		wstate = 4 # STORM
	env.call("set_weather", wstate)
	# Season drives the day/night clock's seasonal track.
	if env.has_method("set_season") and "season01" in sim:
		env.call("set_season", float(sim.get("season01")))
	# Waves + wind on the water shader.
	var water := env.get("water_plane") as MeshInstance3D
	if water != null and water.material_override is ShaderMaterial:
		var wmat := water.material_override as ShaderMaterial
		var wave_m := float(sim.get("wave_m")) if "wave_m" in sim else 0.0
		var wind_ms := float(sim.get("wind_ms")) if "wind_ms" in sim else 0.0
		wmat.set_shader_parameter("wave_amplitude", clampf(wave_m * 0.5, 0.05, 2.0))
		wmat.set_shader_parameter("wind_strength", clampf(wind_ms / 8.0, 0.0, 3.0))
		var temp_c := float(sim.get("temperature_c")) if "temperature_c" in sim else 8.0
		wmat.set_shader_parameter("temperature_factor", clampf((temp_c - 2.0) / 18.0, 0.0, 1.0))


func _on_pen_clicked(pen_id: String, position: Vector3) -> void:
	selected_pen_id = pen_id
	show_pen_detail(pen_id)
	if vista_camera:
		_fly_to(position)


## Eased fly-to-farm (no instant snaps). Falls back to the legacy instant
## look_at_position if the camera lacks the smooth variant.
func _fly_to(position: Vector3) -> void:
	if vista_camera.has_method("fly_to_position"):
		vista_camera.fly_to_position(position, FjordCamera.ZoomLevel.FARM)
	else:
		vista_camera.look_at_position(position, FjordCamera.ZoomLevel.FARM)
