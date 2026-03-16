extends Control

## Main — Core game loop controller.
## Manages time progression, UI panels, and game state.

# ── UI References ─────────────────────────────────────────────────────────────
@onready var dashboard: Control = $Dashboard
@onready var pen_panel: Control = $PenManagement if has_node("PenManagement") else null
@onready var time_controls: HBoxContainer = $TimeControls

# Time control buttons
@onready var btn_pause: Button = $TimeControls/PauseButton
@onready var btn_1x: Button = $TimeControls/Speed1xButton
@onready var btn_2x: Button = $TimeControls/Speed2xButton
@onready var btn_5x: Button = $TimeControls/Speed5xButton
@onready var day_label: Label = $TimeControls/DayLabel

# ── State ─────────────────────────────────────────────────────────────────────
var _sim: Node = null
var _is_paused: bool = false
var _current_speed: float = 1.0
var _auto_save_timer: Timer
var _day_counter: int = 0

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	# Get SimBridge reference
	if has_node("/root/SimBridge"):
		_sim = get_node("/root/SimBridge")
		_sim.state_updated.connect(_on_state_updated)
		_sim.day_advanced.connect(_on_day_advanced)
		_sim.alert_raised.connect(_on_alert_raised)
	else:
		push_error("Main: SimBridge not found!")
		return
	
	# Connect time controls
	if btn_pause:
		btn_pause.pressed.connect(_on_pause_pressed)
	if btn_1x:
		btn_1x.pressed.connect(func(): _set_time_speed(1.0))
	if btn_2x:
		btn_2x.pressed.connect(func(): _set_time_speed(2.0))
	if btn_5x:
		btn_5x.pressed.connect(func(): _set_time_speed(5.0))
	
	# Setup auto-save
	_auto_save_timer = Timer.new()
	_auto_save_timer.wait_time = 300.0 # Auto-save every 5 minutes
	_auto_save_timer.timeout.connect(_on_auto_save)
	_auto_save_timer.autostart = true
	add_child(_auto_save_timer)
	
	# Start game
	_sim.start_new_game("default", "Player")
	
	print("Main: Ready")

func _input(event: InputEvent) -> void:
	# Keyboard shortcuts
	if event.is_action_pressed("ui_pause"):
		_toggle_pause()
		get_viewport().set_input_as_handled()
	
	# Speed controls
	if event.is_action_pressed("speed_1x"):
		_set_time_speed(1.0)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("speed_2x"):
		_set_time_speed(2.0)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("speed_5x"):
		_set_time_speed(5.0)
		get_viewport().set_input_as_handled()
	
	# Quick save/load
	if event.is_action_pressed("quick_save"):
		_quick_save()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("quick_load"):
		_quick_load()
		get_viewport().set_input_as_handled()

# ── Signal Handlers ───────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_update_ui()

func _on_day_advanced(day: int, season: String) -> void:
	_day_counter = day
	_update_day_label()
	
	# Auto-save every 10 days
	if day % 10 == 0:
		_auto_save()

func _on_alert_raised(level: String, message: String) -> void:
	_show_notification(level, message)

func _on_auto_save() -> void:
	_auto_save()

# ── Time Controls ─────────────────────────────────────────────────────────────

func _toggle_pause() -> void:
	_is_paused = !_is_paused
	_sim.set_paused(_is_paused)
	
	if btn_pause:
		btn_pause.text = "▶" if _is_paused else "⏸"
	
	print("Main: %s" % ("Paused" if _is_paused else "Resumed"))

func _on_pause_pressed() -> void:
	_toggle_pause()

func _set_time_speed(speed: float) -> void:
	_current_speed = speed
	_sim.set_time_speed(speed)
	
	# Highlight active button
	if btn_1x:
		btn_1x.button_pressed = (speed == 1.0)
	if btn_2x:
		btn_2x.button_pressed = (speed == 2.0)
	if btn_5x:
		btn_5x.button_pressed = (speed == 5.0)
	
	print("Main: Speed set to %.1fx" % speed)

# ── UI Updates ────────────────────────────────────────────────────────────────

func _update_ui() -> void:
	_update_day_label()

func _update_day_label() -> void:
	if day_label:
		var season = _get_season(_day_counter)
		day_label.text = "Day %d (%s)" % [_day_counter, season]

func _get_season(day: int) -> String:
	var day_of_year = day % 365
	if day_of_year < 60 or day_of_year >= 335:
		return "Winter"
	elif day_of_year < 152:
		return "Spring"
	elif day_of_year < 244:
		return "Summer"
	return "Autumn"

# ── Save/Load ─────────────────────────────────────────────────────────────────

func _auto_save() -> void:
	var success = _sim.save_game("autosave")
	if success:
		_show_notification("info", "Auto-saved")
	else:
		_show_notification("warning", "Auto-save failed")

func _quick_save() -> void:
	var timestamp = Time.get_datetime_string_from_system()
	var save_name = "quicksave_%s" % timestamp.replace(":", "-")
	var success = _sim.save_game(save_name)
	if success:
		_show_notification("success", "Quick saved!")
	else:
		_show_notification("error", "Quick save failed")

func _quick_load() -> void:
	var success = _sim.load_game("quicksave_latest")
	if success:
		_show_notification("success", "Quick loaded!")
	else:
		_show_notification("warning", "No quick save found")

# ── Notifications ─────────────────────────────────────────────────────────────

func _show_notification(level: String, message: String) -> void:
	# TODO: Implement toast notification system
	print("Main: [%s] %s" % [level.to_upper(), message])
