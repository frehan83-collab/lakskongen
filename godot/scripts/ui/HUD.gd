extends Control

## HUD component for top bar display and time controls
## Shows day counter, cash display, and time speed buttons (1x, 7x, 30x, pause)

# UI Elements
@onready var day_value: Label = $HBoxContainer/LeftSection/DayContainer/DayValue
@onready var cash_value: Label = $HBoxContainer/LeftSection/CashContainer/CashValue
@onready var net_worth_value: Label = $HBoxContainer/LeftSection/NetWorthContainer/NetWorthValue

@onready var pause_button: Button = $HBoxContainer/RightSection/SpeedButtons/PauseButton
@onready var speed_1x: Button = $HBoxContainer/RightSection/SpeedButtons/Speed1x
@onready var speed_7x: Button = $HBoxContainer/RightSection/SpeedButtons/Speed7x
@onready var speed_30x: Button = $HBoxContainer/RightSection/SpeedButtons/Speed30x

# Speed button group for toggle behavior
var speed_buttons: Array[Button]
var current_speed: float = 1.0
var is_paused: bool = false

# References to autoloads
var game_bridge: Node
var event_bus: Node

func _ready():
	# Get autoload references
	game_bridge = get_node("/root/GameBridge")
	event_bus = get_node("/root/EventBus")
	
	# Set up speed button group behavior
	speed_buttons = [speed_1x, speed_7x, speed_30x]
	for button in speed_buttons:
		button.button_group = null  # We'll handle toggling manually
	
	# Connect button signals
	pause_button.pressed.connect(_on_pause_pressed)
	speed_1x.pressed.connect(_on_speed_1x_pressed)
	speed_7x.pressed.connect(_on_speed_7x_pressed)
	speed_30x.pressed.connect(_on_speed_30x_pressed)
	
	# Connect to EventBus signals
	if event_bus:
		event_bus.day_advanced.connect(_on_day_advanced)
		event_bus.finance_updated.connect(_on_finance_updated)
		event_bus.game_paused.connect(_on_game_paused)
		event_bus.time_speed_changed.connect(_on_time_speed_changed)
	
	# Connect to GameBridge signals
	if game_bridge:
		game_bridge.game_state_updated.connect(_on_game_state_updated)
		game_bridge.simulation_ticked.connect(_on_simulation_ticked)
	
	# Initialize display
	_update_display()
	print("HUD initialized")

func _update_display():
	"""Update all HUD displays with current game state"""
	if not game_bridge or not game_bridge.GameState:
		day_value.text = "0"
		cash_value.text = "$0"
		net_worth_value.text = "$0"
		return
	
	var game_state = game_bridge.GameState
	
	# Update day display
	day_value.text = str(game_state.CurrentDay)
	
	# Update financial display
	cash_value.text = _format_currency(game_state.Cash)
	
	# Calculate net worth (cash + assets - debts)
	var net_worth = game_state.Cash
	# TODO: Add farm values, equipment values, subtract loans when available
	net_worth_value.text = _format_currency(net_worth)
	
	# Update button states
	_update_speed_buttons()

func _format_currency(amount: int) -> String:
	"""Format currency with appropriate suffixes for large amounts"""
	var abs_amount = abs(amount)
	var sign = "-" if amount < 0 else ""
	
	if abs_amount >= 1000000000:
		return sign + "$%.1fB" % (abs_amount / 1000000000.0)
	elif abs_amount >= 1000000:
		return sign + "$%.1fM" % (abs_amount / 1000000.0)
	elif abs_amount >= 1000:
		return sign + "$%.0fK" % (abs_amount / 1000.0)
	else:
		return sign + "$%d" % abs_amount

func _update_speed_buttons():
	"""Update speed button visual states"""
	# Reset all speed buttons
	for button in speed_buttons:
		button.button_pressed = false
	
	# Set current speed button
	if not is_paused:
		if current_speed == 1.0:
			speed_1x.button_pressed = true
		elif current_speed == 7.0:
			speed_7x.button_pressed = true
		elif current_speed == 30.0:
			speed_30x.button_pressed = true
	
	# Update pause button
	pause_button.text = ">" if is_paused else "||"
	pause_button.tooltip_text = "Resume" if is_paused else "Pause"

func _set_speed(speed: float):
	"""Set the game time speed"""
	if speed != current_speed:
		current_speed = speed
		is_paused = false
		
		if game_bridge:
			game_bridge.SetTimeSpeed(speed)
			game_bridge.SetPaused(false)
		
		_update_speed_buttons()
		print("Speed set to: ", speed, "x")

func _toggle_pause():
	"""Toggle game pause state"""
	is_paused = not is_paused
	
	if game_bridge:
		game_bridge.SetPaused(is_paused)
	
	_update_speed_buttons()
	print("Game ", "paused" if is_paused else "resumed")

# === Button Signal Handlers ===

func _on_pause_pressed():
	_toggle_pause()

func _on_speed_1x_pressed():
	_set_speed(1.0)

func _on_speed_7x_pressed():
	_set_speed(7.0)

func _on_speed_30x_pressed():
	_set_speed(30.0)

# === EventBus Signal Handlers ===

func _on_day_advanced(day: int, season: String):
	day_value.text = str(day)

func _on_finance_updated(cash: int, net_worth: int):
	cash_value.text = _format_currency(cash)
	net_worth_value.text = _format_currency(net_worth)

func _on_game_paused(paused: bool):
	is_paused = paused
	_update_speed_buttons()

func _on_time_speed_changed(speed: float):
	current_speed = speed
	_update_speed_buttons()

# === GameBridge Signal Handlers ===

func _on_game_state_updated():
	_update_display()

func _on_simulation_ticked(day: int):
	day_value.text = str(day)

# === Input Handling ===

func _input(event):
	"""Handle keyboard shortcuts for speed control"""
	if event.is_action_pressed("ui_space", false):  # Spacebar for pause/resume
		_toggle_pause()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("speed_1x", false):
		_set_speed(1.0)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("speed_7x", false):
		_set_speed(7.0)
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("speed_30x", false):
		_set_speed(30.0)
		get_viewport().set_input_as_handled()