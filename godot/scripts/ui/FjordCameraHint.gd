## FjordCameraHint v2.1 — Nordic Minimalist (scene-based).
## Fjord camera hint + "fly to farm" button overlay.
## Lives in GameScene UILayer above the 3D vista, below panels.
## Layout lives in scenes/ui/FjordCameraHint.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization only.
## All strings Norwegian; styling follows LakskongenTheme palette via shared
## StyleBox sub-resources in the scene.

extends Control

@export var bridge_path: NodePath

@onready var _hint_label: Label = %HintLabel
@onready var _fly_button: Button = %FlyToFarmButton

var _bridge: Node = null


func _ready() -> void:
	_bridge = get_node_or_null(bridge_path)
	_apply_localization()
	if _fly_button:
		_fly_button.pressed.connect(_on_fly_pressed)
	if _bridge:
		if _bridge.pen_manager:
			_bridge.pen_manager.pen_selection_changed.connect(_on_selection_changed)
		if _bridge.has_signal("world_view_changed"):
			_bridge.world_view_changed.connect(_on_world_view_changed)
			visible = _bridge.world_visible
		set_process(true)
	_update_button()


func _apply_localization() -> void:
	if _hint_label:
		_hint_label.text = tr("UI_FJORD_HINT")
	if _fly_button:
		_fly_button.text = tr("UI_FJORD_FLY")


func _process(_delta: float) -> void:
	_update_button()


func _update_button() -> void:
	if _fly_button == null:
		return
	var has_sel: bool = _bridge != null and _bridge.has_selection()
	_fly_button.disabled = not has_sel
	_fly_button.tooltip_text = tr("UI_FJORD_FLY_HINT") if has_sel else tr("UI_FJORD_FLY_NONE")


func _on_selection_changed(_pen_id: String) -> void:
	_update_button()


func _on_fly_pressed() -> void:
	if _bridge:
		_bridge.focus_selected_pen()


## Hidden while an opaque panel covers the world.
func _on_world_view_changed(world_visible: bool) -> void:
	visible = world_visible
