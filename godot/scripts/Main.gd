extends Control

## Main.gd - Master controller for the Lakskongen game scene.
## Initializes SimBridge, starts the simulation, and coordinates UI updates.

# Node references
@onready var _sim: Node = get_node_or_null("/root/SimBridge")
@onready var _hud: Control = %HUD
@onready var _game_ui: Control = %GameUI
@onready var _dashboard: Control = null  # Dashboard is now loaded dynamically by GameUI
## The pause menu (Esc, roadmap U1-06): on its own layer above the game UI.
@onready var _pause_menu: Control = get_node_or_null("%PauseMenu")

# Configuration
const STARTING_CAPITAL: int = 10_000_000
const DEFAULT_PLAYER_NAME: String = "Player"

# State
var _game_started: bool = false
# Last critical message that triggered auto-pause (dedupe: conditions repeat daily).
var _last_auto_pause_msg: String = ""

func _ready() -> void:
	print("Main: Initializing...")

	# Verify SimBridge is available
	if not _sim:
		push_error("Main: SimBridge not found! Make sure it is set as an autoload.")
		return

	# Connect to SimBridge signals
	_sim.state_updated.connect(_on_state_updated)
	_sim.day_advanced.connect(_on_day_advanced)
	_sim.alert_raised.connect(_on_alert_raised)

	# Connect HUD speed change signal
	if _hud:
		_hud.speed_changed.connect(_on_speed_changed)

	# The window's close button asks "Lagre før du avslutter?" (U1-06): the
	# game screen takes the close request instead of quitting outright.
	get_tree().set_auto_accept_quit(false)
	if _pause_menu:
		_pause_menu.opened.connect(_sync_hud_pause)
		_pause_menu.closed.connect(_sync_hud_pause)

	# Start the game after a short delay to ensure everything is ready
	call_deferred("_start_game")

	print("Main: Ready")

func _start_game() -> void:
	# Start a new game with default settings
	if _game_started:
		return

	# The main menu already started a scenario or loaded a save before switching
	# to this scene — keep that game instead of resetting it (F056).
	if _sim.is_game_running():
		_game_started = true
		print("Main: Keeping the game started from the main menu (day %d)" % _sim.current_day)
		return

	print("Main: Starting new game...")

	# Start the simulation via SimBridge
	var success = _sim.start_new_game(DEFAULT_PLAYER_NAME)

	if success:
		_game_started = true
		print("Main: Game started successfully with %d NOK capital" % STARTING_CAPITAL)

		# Ensure simulation is running (not paused)
		_sim.set_paused(false)
		_sim.set_time_speed(1.0)
	else:
		push_error("Main: Failed to start game!")

func _exit_tree() -> void:
	get_tree().set_auto_accept_quit(true)

## The window's close button (and Cmd+Q on macOS) ask before leaving.
func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST:
		request_quit()

## Ask "Lagre før du avslutter?" (over the pause menu); quits outright only
## when the scene has no pause menu.
func request_quit() -> void:
	if _pause_menu == null:
		get_tree().quit()
		return
	var inbox := get_node_or_null("AttentionInbox")
	if inbox != null:
		inbox.visible = false
	_pause_menu.request_quit()

## Ctrl/Cmd+S quicksave (project input action `quick_save`). Esc that nothing
## else on the screen took (an open panel, popup or tab closes first; the
## dashboard is home) opens the pause menu.
func _unhandled_input(event: InputEvent) -> void:
	if _game_started and event.is_action_pressed("quick_save"):
		quick_save()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("ui_cancel") and _pause_menu != null and not _pause_menu.is_open():
		_pause_menu.show_menu()
		get_viewport().set_input_as_handled()

## The HUD's pause button follows the clock while the pause menu holds it.
func _sync_hud_pause() -> void:
	if _hud and _hud.has_method("set_paused_state") and _sim:
		_hud.set_paused_state(_sim.is_paused())

func quick_save() -> bool:
	var ok: bool = _sim.quick_save()
	var toast := get_node_or_null("/root/ToastNotification")
	if toast:
		if ok:
			toast.show_toast(_tr_or("UI_TOAST_QUICKSAVED", "💾 Hurtiglagret"), "SUCCESS", 3.0)
		else:
			toast.show_toast(_tr_or("UI_TOAST_SAVE_FAILED", "⚠️ Lagring feilet: %s") % _sim.get_last_save_error(), "ERROR", 5.0)
	return ok

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _on_state_updated() -> void:
	# UI components auto-update via their own SimBridge connections
	pass

func _on_day_advanced(day: int, season: String) -> void:
	print("Main: Day %d (%s)" % [day, season])

func _on_alert_raised(level: String, message: String) -> void:
	print("Main: Alert [%s] %s" % [level, message])
	# Auto-pause on NEW critical alerts (settings: gameplay.auto_pause_critical).
	if level != "critical" or message == _last_auto_pause_msg:
		return
	if not _should_auto_pause():
		return
	_last_auto_pause_msg = message
	set_paused(true)
	if _hud and _hud.has_method("set_paused_state"):
		_hud.set_paused_state(true)

## Auto-pause toggle from user://settings.json (default: on).
func _should_auto_pause() -> bool:
	if not FileAccess.file_exists("user://settings.json"):
		return true
	var file := FileAccess.open("user://settings.json", FileAccess.READ)
	if file == null:
		return true
	var json := JSON.new()
	if json.parse(file.get_as_text()) != OK or not (json.data is Dictionary):
		return true
	var gameplay: Dictionary = json.data.get("gameplay", {})
	return bool(gameplay.get("auto_pause_critical", true))

func _on_speed_changed(speed: float, paused: bool) -> void:
	if _sim:
		_sim.set_time_speed(speed)
		_sim.set_paused(paused)

func advance_one_day() -> void:
	if _sim:
		_sim.advance_day()

func set_paused(paused: bool) -> void:
	if _sim:
		_sim.set_paused(paused)

func get_current_day() -> int:
	if _sim:
		return _sim.current_day
	return 0

func is_game_started() -> bool:
	return _game_started

func is_pause_menu_open() -> bool:
	return _pause_menu != null and _pause_menu.is_open()
