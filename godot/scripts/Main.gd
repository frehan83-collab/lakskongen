extends Control

## Main.gd - Master controller for the Lakskongen game scene.
## Initializes SimBridge, starts the simulation, and coordinates UI updates.

# Node references
@onready var _sim: Node = get_node_or_null("/root/SimBridge")
@onready var _hud: Control = %HUD
@onready var _game_ui: Control = %GameUI
@onready var _dashboard: Control = null  # Dashboard is now loaded dynamically by GameUI

# Configuration
const STARTING_CAPITAL: int = 10_000_000
const DEFAULT_PLAYER_NAME: String = "Player"

# State
var _game_started: bool = false

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

	# Start the game after a short delay to ensure everything is ready
	call_deferred("_start_game")

	print("Main: Ready")

func _start_game() -> void:
	# Start a new game with default settings
	if _game_started:
		return

	print("Main: Starting new game...")

	# Start the simulation via SimBridge
	var success = _sim.start_new_game("default", DEFAULT_PLAYER_NAME)

	if success:
		_game_started = true
		print("Main: Game started successfully with %d NOK capital" % STARTING_CAPITAL)

		# Ensure simulation is running (not paused)
		_sim.set_paused(false)
		_sim.set_time_speed(1.0)
	else:
		push_error("Main: Failed to start game!")

func _on_state_updated() -> void:
	# UI components auto-update via their own SimBridge connections
	pass

func _on_day_advanced(day: int, season: String) -> void:
	print("Main: Day %d (%s)" % [day, season])

func _on_alert_raised(level: String, message: String) -> void:
	print("Main: Alert [%s] %s" % [level, message])

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

func _toggle_pause_menu() -> void:
	if not _pause_menu:
		return
	
	if _pause_menu.visible:
		_pause_menu.hide_menu()
	else:
		_pause_menu.show_menu()

func _on_resume_requested() -> void:
	# Resume is handled by PauseMenu itself
	pass

func _on_quit_to_menu() -> void:
	# This is handled by PauseMenu, but we could add cleanup here if needed
	pass
y() -> void:
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
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
