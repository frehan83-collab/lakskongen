extends Node

## GameController — Handles player input for time advancement and game lifecycle.
##
## Attach this script to a persistent node in Main.tscn (or its own
## GameController scene).  It owns the "next day / week / month" buttons
## and delegates all simulation calls to SimBridge.
##
## Button wiring: use %UniqueNodeName references so the script works
## regardless of where in the scene tree the buttons live.

# ── Config ────────────────────────────────────────────────────────────────────

## Player display name used when starting a new game.
@export var player_name: String = "Spiller"

## Starting capital in NOK (overrides SimulationEngine default).
@export var starting_capital: int = 10_000_000

## If true, a new game is started automatically in _ready().
@export var auto_start: bool = true

# ── UI node references (optional — graceful if missing) ───────────────────────

## Step buttons (must have unique names in the scene if used)
@onready var btn_next_day:   Button = _get_button("NextDayButton")
@onready var btn_next_week:  Button = _get_button("NextWeekButton")
@onready var btn_next_month: Button = _get_button("NextMonthButton")
@onready var btn_pause:      Button = _get_button("PauseButton")

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted when the pause state toggles, so HUD can sync its button.
signal pause_toggled(is_paused: bool)

## Emitted after every advance call, so other UI can react.
signal advance_completed(days_advanced: int)

# ── Internal state ────────────────────────────────────────────────────────────

var _sim: Node = null
var _is_paused: bool = false

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	# Grab SimBridge autoload
	if has_node("/root/SimBridge"):
		_sim = get_node("/root/SimBridge")
	else:
		push_error("GameController: /root/SimBridge not found — cannot control simulation")

	# Wire advance buttons
	_connect_button(btn_next_day,   _on_next_day)
	_connect_button(btn_next_week,  _on_next_week)
	_connect_button(btn_next_month, _on_next_month)
	_connect_button(btn_pause,      _on_toggle_pause)

	# Auto-start a new game if configured
	if auto_start and _sim:
		_sim.start_new_game(player_name, starting_capital)

	print("GameController: ready (player='%s', capital=%d NOK)" % [player_name, starting_capital])

# ── Button handlers ───────────────────────────────────────────────────────────

func _on_next_day() -> void:
	_advance(1)

func _on_next_week() -> void:
	_advance(7)

func _on_next_month() -> void:
	_advance(30)

func _on_toggle_pause() -> void:
	_is_paused = not _is_paused

	if _sim:
		_sim.set_paused(_is_paused)

	if btn_pause:
		btn_pause.text = "▶ Fortsett" if _is_paused else "⏸ Pause"

	pause_toggled.emit(_is_paused)
	print("GameController: %s" % ("paused" if _is_paused else "resumed"))

# Keyboard: Space pause lives in HUD (toggle_pause). The D/W/M step keys were
# removed: D and W are the fjord camera's pan keys, and W/M called methods
# that do not exist.

# ── Public API ────────────────────────────────────────────────────────────────

## Start a fresh game (can be called from MainMenu).
func start_game(p_player_name: String = "", p_starting_capital: int = 0) -> void:
	if p_player_name != "":
		player_name = p_player_name
	if p_starting_capital > 0:
		starting_capital = p_starting_capital

	if _sim:
		_sim.start_new_game(player_name, starting_capital)

## Set the auto-tick time speed (1.0x, 7.0x, 30.0x).
func set_speed(speed: float) -> void:
	if _sim:
		_sim.set_time_speed(speed)
	# Make sure we are unpaused when speed is selected
	if _is_paused:
		_is_paused = false
		pause_toggled.emit(false)

# ── Helpers ───────────────────────────────────────────────────────────────────

func _advance(days: int) -> void:
	if not _sim:
		return
	# If we are running in auto-tick mode, a manual advance still works;
	# the engine handles concurrent calls safely.
	_sim.advance_days(days)
	advance_completed.emit(days)

## Safe button lookup — searches the scene tree by unique name (%) first,
## then falls back to a direct child search.  Returns null if not found.
## The buttons are optional (HUD owns speed control in GameScene), so a miss
## is silent by design — callers must null-check.
func _get_button(node_name: String) -> Button:
	# Try %UniqueNodeName lookup (requires the node to be marked unique in scene)
	var unique_path := "%" + node_name
	if has_node(unique_path):
		var n := get_node(unique_path)
		if n is Button:
			return n as Button
	return null

func _connect_button(btn: Button, callable: Callable) -> void:
	if btn and not btn.pressed.is_connected(callable):
		btn.pressed.connect(callable)
