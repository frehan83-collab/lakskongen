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
## F2-21: G's caption ("Land · Storholmen") while the camera flies there.
@onready var _bookmark_chip: PanelContainer = %BookmarkChip
@onready var _bookmark_label: Label = %BookmarkLabel

## How long (s) the bookmark caption shows, and its fade out.
const BOOKMARK_SHOW_S := 2.5
const BOOKMARK_FADE_S := 0.4

var _bridge: Node = null
var _bookmark_tween: Tween = null


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
		var cam: Node = _bridge.get("vista_camera")
		if cam != null and cam.has_signal("bookmark_changed"):
			cam.bookmark_changed.connect(_on_bookmark_changed)
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


## F2-21: name the bookmark G flew to ("Fjord · hjemmevisning", "Land ·
## Storholmen", "Hav · Ytre kyst") for a moment; the presenter names the
## target (its plot's place name).
func _on_bookmark_changed(slot: String, target_id: String) -> void:
	if _bookmark_chip == null:
		return
	var place := ""
	var presenter := get_tree().get_first_node_in_group(FarmFacilityVisualManager.GROUP)
	if presenter != null and target_id != "":
		place = str(presenter.call("target_name", target_id))
	match slot:
		"land":
			_bookmark_label.text = tr("CAMERA_BOOKMARK_LAND") % place
		"hav":
			_bookmark_label.text = tr("CAMERA_BOOKMARK_HAV") % place
		_:
			_bookmark_label.text = tr("CAMERA_BOOKMARK_FJORD")
	_bookmark_chip.visible = true
	_bookmark_chip.modulate.a = 1.0
	if _bookmark_tween != null and _bookmark_tween.is_valid():
		_bookmark_tween.kill()
	_bookmark_tween = create_tween()
	_bookmark_tween.tween_interval(BOOKMARK_SHOW_S)
	_bookmark_tween.tween_property(_bookmark_chip, "modulate:a", 0.0, BOOKMARK_FADE_S)
	_bookmark_tween.tween_callback(func() -> void: _bookmark_chip.visible = false)


## The caption shown now ("" when hidden).
func bookmark_caption() -> String:
	return _bookmark_label.text if _bookmark_chip != null and _bookmark_chip.visible else ""
