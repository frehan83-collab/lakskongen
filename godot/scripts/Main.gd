extends Node

## Main — Core game loop controller.
## Manages time progression, UI coordination, and game state.
## Works with GameUI for panel navigation and HUD for top-level status.

# ── UI References ─────────────────────────────────────────────────────────────
@onready var hud: Control = %HUD if has_node("%HUD") else null
@onready var game_ui: Control = %GameUI if has_node("%GameUI") else null
@onready var notification_center: Control = %NotificationCenter if has_node("%NotificationCenter") else null
@onready var game_controller: Node = %GameController if has_node("%GameController") else null
@onready var audio_controller: Node = %AudioController if has_node("%AudioController") else null

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
	
	# Setup auto-save
	_auto_save_timer = Timer.new()
	_auto_save_timer.wait_time = 300.0 # Auto-save every 5 minutes
	_auto_save_timer.timeout.connect(_on_auto_save)
	_auto_save_timer.autostart = true
	add_child(_auto_save_timer)
	
	# Start game (GameController handles actual initialization)
	if game_controller and game_controller.auto_start:
		print("Main: GameController will auto-start")
	else:
		_sim.start_new_game("default", "Player")
	
	print("Main: Ready")

func _input(event: InputEvent) -> void:
	# Global keyboard shortcuts (not handled by GameUI)
	
	# Pause toggle (Space bar)
	if event.is_action_pressed("ui_pause") or (event is InputEventKey and event.keycode == KEY_SPACE and event.pressed):
		_toggle_pause()
		get_viewport().set_input_as_handled()
		return
	
	# Speed controls (1, 2, 3 keys for 1x, 2x, 5x)
	if event is InputEventKey and event.pressed and not event.echo:
		match event.keycode:
			KEY_1:
				_set_time_speed(1.0)
				get_viewport().set_input_as_handled()
			KEY_2:
				_set_time_speed(2.0)
				get_viewport().set_input_as_handled()
			KEY_3:
				_set_time_speed(5.0)
				get_viewport().set_input_as_handled()
	
	# Quick save/load (F5/F9)
	if event.is_action_pressed("quick_save") or (event is InputEventKey and event.keycode == KEY_F5 and event.pressed):
		_quick_save()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("quick_load") or (event is InputEventKey and event.keycode == KEY_F12 and event.pressed):
		_quick_load()
		get_viewport().set_input_as_handled()

# ── Signal Handlers ───────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_update_ui()

func _on_day_advanced(day: int, season: String) -> void:
	_day_counter = day
	_update_day_display()
	
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
	
	# Update HUD pause indicator if available
	if hud and hud.has_method("set_paused_state"):
		hud.call("set_paused_state", _is_paused)
	
	_show_notification("info", "⏸ Paused" if _is_paused else "▶ Resumed")
	print("Main: %s" % ("Paused" if _is_paused else "Resumed"))

func _set_time_speed(speed: float) -> void:
	_current_speed = speed
	_sim.set_time_speed(speed)
	
	# Update HUD speed indicator if available
	if hud and hud.has_method("set_speed"):
		hud.call("set_speed", speed)
	
	_show_notification("info", "Speed: %.0fx" % speed)
	print("Main: Speed set to %.1fx" % speed)

# ── UI Updates ────────────────────────────────────────────────────────────────

func _update_ui() -> void:
	_update_day_display()
	
	# Notify HUD to refresh if it has update method
	if hud and hud.has_method("update_display"):
		hud.call("update_display")

func _update_day_display() -> void:
	if hud and hud.has_method("set_day"):
		var season = _get_season(_day_counter)
		hud.call("set_day", _day_counter, season)

func _get_season(day: int) -> String:
	var day_of_year = day % 365
	if day_of_year < 60 or day_of_year >= 335:
		return "Vinter"
	elif day_of_year < 152:
		return "Vår"
	elif day_of_year < 244:
		return "Sommer"
	return "Høst"

# ── Save/Load ─────────────────────────────────────────────────────────────────

func _auto_save() -> void:
	var success = _sim.save_game("autosave")
	if success:
		_show_notification("success", "💾 Auto-lagret")
	else:
		_show_notification("warning", "⚠ Auto-lagring feilet")

func _quick_save() -> void:
	var timestamp = Time.get_datetime_string_from_system().replace(":", "-")
	var save_name = "quicksave_%s" % timestamp
	var success = _sim.save_game(save_name)
	if success:
		_show_notification("success", "💾 Hurtiglagret!")
	else:
		_show_notification("error", "❌ Hurtiglagring feilet")

func _quick_load() -> void:
	# Try to load most recent autosave
	var success = _sim.load_game("autosave")
	if success:
		_show_notification("success", "📂 Hurtiglastet!")
		# Refresh all UI
		_update_ui()
	else:
		_show_notification("warning", "⚠ Ingen lagring funnet")

# ── Notifications ─────────────────────────────────────────────────────────────

func _show_notification(level: String, message: String) -> void:
	# Try notification center first
	if notification_center and notification_center.has_method("show_notification"):
		notification_center.call("show_notification", level, message)
	else:
		# Fallback to console
		print("Main: [%s] %s" % [level.to_upper(), message])

# ── Public API ────────────────────────────────────────────────────────────────

## Show a specific UI panel by name.
func show_panel(panel_name: String) -> void:
	if game_ui and game_ui.has_method("show_tab"):
		game_ui.call("show_tab", panel_name)

## Get current game day.
func get_current_day() -> int:
	return _day_counter

## Get current game speed multiplier.
func get_time_speed() -> float:
	return _current_speed

## Check if game is paused.
func is_paused() -> bool:
	return _is_paused
