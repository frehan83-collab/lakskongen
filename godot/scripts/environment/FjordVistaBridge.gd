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
