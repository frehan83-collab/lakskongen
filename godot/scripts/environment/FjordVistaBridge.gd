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
## When false, the sim's calendar, latitude and weather do NOT touch the 3D
## sky. Used by the screenshot harness, which stages each regime itself —
## otherwise every sim tick overrides the staged shot. The water state
## (temperature, bloom) always follows the sim.
var sim_weather_enabled: bool = true

signal farm_focused(pen_id: String)
## The world is on screen (the see-through Fjord tab) or covered by a panel.
signal world_view_changed(world_visible: bool)

## True while the world is on screen; camera input and the camera hint follow it.
var world_visible: bool = true

## UI lists (the Pens tab) select through this group, so a pen chosen in a
## list is selected in 3D too ("Fly to pen" needs a selection).
const GROUP := "fjord_vista"


func _ready() -> void:
	add_to_group(GROUP)
	pen_manager = get_node_or_null(pen_manager_path) as FishPenManager
	vista_camera = get_node_or_null(camera_path) as FjordCamera
	game_ui = get_node_or_null(game_ui_path)
	if game_ui and game_ui.has_signal("world_view_changed"):
		game_ui.world_view_changed.connect(_on_world_view_changed)
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
	call_deferred("_focus_first_pen")


func _focus_first_pen() -> void:
	var pen_ids := pen_manager.get_pen_ids()
	if pen_ids.is_empty() or vista_camera == null:
		return
	vista_camera.home_focus = pen_manager.get_pen_position(pen_ids[0])
	vista_camera.fly_to_position(vista_camera.home_focus, FjordCamera.ZoomLevel.FJORD)


## Camera keys, wheel and drags act only while the world is on screen; the
## camera hint and "Fly to pen" hide behind opaque panels.
func _on_world_view_changed(visible_now: bool) -> void:
	world_visible = visible_now
	if vista_camera:
		vista_camera.input_enabled = visible_now
	world_view_changed.emit(visible_now)


func _on_state_updated() -> void:
	if pen_manager == null:
		return
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_all_pens"):
		return
	pen_manager.sync_pens_from_sim(sim.get_all_pens())
	_sync_environment(sim)


## Living-fjord sync (~1 Hz via state_updated): the sim's calendar, latitude
## and atmosphere (GameBridge.GetSkyContext: Koschmieder visibility, cloud,
## rain or snow, sea fog, wind, waves) drive the sky and weather; the sea
## temperature and bloom severity drive the water. The environment's
## EnvironmentDirector is the only writer of the resulting fog, sky, light
## and wave uniforms; this presenter only hands it the inputs.
## Everything is guarded — a missing node skips quietly, never errors.
func _sync_environment(sim: Node) -> void:
	var env := pen_manager.get_parent() as Node
	if env == null or not env.has_method("apply_sky_context"):
		return
	if sim_weather_enabled and sim.has_method("get_sky_context"):
		env.call("apply_sky_context", sim.get_sky_context())
	# The water state is the farm's, not the weather's: always synced.
	var temp_c := float(sim.get("temperature_c")) if "temperature_c" in sim else 8.0
	var algae := 0.0
	var gb := get_node_or_null("/root/GameBridge")
	if gb != null and gb.has_method("GetMaxBloomSeverity"):
		algae = float(gb.GetMaxBloomSeverity())
	env.call("set_water_state", temp_c, algae)


func _on_pen_clicked(pen_id: String, position: Vector3) -> void:
	selected_pen_id = pen_id
	show_pen_detail(pen_id)
	if vista_camera:
		_fly_to(position)


## A pen chosen in a UI list: select it in 3D without opening a tab or moving
## the camera.
func select_pen_from_ui(pen_id: String) -> void:
	if pen_manager == null or not (pen_id in pen_manager.get_pen_ids()):
		return
	selected_pen_id = pen_id
	if pen_manager.selected_pen_id != pen_id:
		pen_manager.select_pen(pen_id)


## Eased fly-to-farm (no instant snaps). Falls back to the legacy instant
## look_at_position if the camera lacks the smooth variant.
func _fly_to(position: Vector3) -> void:
	if vista_camera.has_method("fly_to_position"):
		vista_camera.fly_to_position(position, FjordCamera.ZoomLevel.FARM)
	else:
		vista_camera.look_at_position(position, FjordCamera.ZoomLevel.FARM)
