extends CanvasLayer

@onready var panel = $"Panel"
@onready var achievement_icon = $"Panel/HBoxContainer/AchievementIcon"
@onready var achievement_title = $"Panel/HBoxContainer/VBoxContainer/AchievementTitle"
@onready var achievement_description = $"Panel/HBoxContainer/VBoxContainer/AchievementDescription"

const DISPLAY_DURATION := 4.0   # seconds before auto-dismiss
const FADE_DURATION    := 0.5   # seconds for fade-in / fade-out
const SLIDE_DISTANCE   := 100.0 # px - notification slides up on entry

var _tween: Tween = null
var _timer: float = 0.0
var _visible_flag: bool = false

func _ready() -> void:
	panel.modulate.a = 0.0
	panel.visible = false

	if has_node("/root/SimBridge"):
		get_node("/root/SimBridge").achievement_unlocked.connect(show_notification)
		print("AchievementNotification: connected to SimBridge")

func _process(delta: float) -> void:
	if not _visible_flag:
		return
	_timer -= delta
	if _timer <= 0.0:
		_dismiss()

func show_notification(id: String, name: String, description: String, reward_nok: float, tier: String) -> void:
	# Cancel any running dismiss tween so back-to-back unlocks work cleanly
	if _tween and _tween.is_valid():
		_tween.kill()

	achievement_title.text = name
	achievement_description.text = description
	# TODO: Use achievement icon based on ID, for now, a generic trophy
	achievement_icon.text = "🏆"

	panel.visible = true
	_timer = DISPLAY_DURATION
	_visible_flag = true

	# Slide-up + fade-in from bottom-right
	var initial_y = panel.position.y + SLIDE_DISTANCE
	panel.position.y = initial_y

	_tween = create_tween().set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	_tween.tween_property(panel, "modulate:a", 1.0, FADE_DURATION)
	_tween.parallel().tween_property(panel, "position:y", initial_y - SLIDE_DISTANCE, FADE_DURATION)

func _dismiss() -> void:
	_visible_flag = false
	_timer = 0.0

	if _tween and _tween.is_valid():
		_tween.kill()

	_tween = create_tween().set_ease(Tween.EASE_IN).set_trans(Tween.TRANS_CUBIC)
	_tween.tween_property(panel, "modulate:a", 0.0, FADE_DURATION)
	_tween.tween_callback(func(): panel.visible = false)

func _on_dismiss_button_pressed() -> void:
	_dismiss()
