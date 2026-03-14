extends Control

## HUD — always-visible top bar for Lakskongen.
## Displays: Day, Season, Balance (NOK), Fish count, Temperature,
## Market price, and Speed controls (1x / 2x / 5x / pause).
## All simulation data is read from the SimBridge autoload.
##
## Expected node layout (set up in corresponding .tscn):
##   HUD (Control, full-width anchor top)
##   └─ HBoxContainer
##      ├─ LeftSection (HBoxContainer)
##      │  ├─ DayContainer / DayLabel  + DayValue (Label)
##      │  ├─ SeasonContainer / SeasonIcon (Label) + SeasonValue (Label)
##      │  ├─ BalanceContainer / BalanceLabel + BalanceValue (Label)
##      │  ├─ FishContainer / FishLabel + FishValue (Label)
##      │  ├─ TempContainer / TempLabel + TempValue (Label)
##      │  └─ PriceContainer / PriceLabel + PriceValue (Label)
##      └─ RightSection (HBoxContainer)
##         └─ SpeedButtons (HBoxContainer)
##            ├─ PauseButton (Button)
##            ├─ Speed1x    (Button)
##            ├─ Speed2x    (Button)
##            └─ Speed5x    (Button)

# ── Color palette ──────────────────────────────────────────────────────────────

const COLOR_GOOD    := Color(0.20, 0.85, 0.30)   # green
const COLOR_WARN    := Color(1.00, 0.80, 0.10)   # yellow
const COLOR_DANGER  := Color(0.95, 0.20, 0.15)   # red
const COLOR_NEUTRAL := Color(0.90, 0.90, 0.90)   # near-white

# Season emoji/icon strings (Label text)
const SEASON_ICONS := {
	"Vinter": "❄",
	"Vår":    "🌱",
	"Sommer": "☀",
	"Høst":   "🍂",
}

# ── Node references ────────────────────────────────────────────────────────────

@onready var day_value:     Label  = $HBoxContainer/LeftSection/DayContainer/DayValue
@onready var season_icon:   Label  = $HBoxContainer/LeftSection/SeasonContainer/SeasonIcon
@onready var season_value:  Label  = $HBoxContainer/LeftSection/SeasonContainer/SeasonValue
@onready var balance_value: Label  = $HBoxContainer/LeftSection/BalanceContainer/BalanceValue
@onready var fish_value:    Label  = $HBoxContainer/LeftSection/FishContainer/FishValue
@onready var temp_value:    Label  = $HBoxContainer/LeftSection/TempContainer/TempValue
@onready var price_value:   Label  = $HBoxContainer/LeftSection/PriceContainer/PriceValue

@onready var pause_btn:  Button = $HBoxContainer/RightSection/SpeedButtons/PauseButton
@onready var speed_1x:   Button = $HBoxContainer/RightSection/SpeedButtons/Speed1x
@onready var speed_2x:   Button = $HBoxContainer/RightSection/SpeedButtons/Speed2x
@onready var speed_5x:   Button = $HBoxContainer/RightSection/SpeedButtons/Speed5x

# ── State ──────────────────────────────────────────────────────────────────────

var _sim: Node = null         # SimBridge reference
var _current_speed: float = 1.0
var _is_paused: bool = false

# ── Signals ────────────────────────────────────────────────────────────────────

## Emitted when the player changes speed or pauses, so other nodes can react.
signal speed_changed(speed: float, paused: bool)

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	if _sim:
		_sim.state_updated.connect(_on_state_updated)
		_sim.day_advanced.connect(_on_day_advanced)
	else:
		push_warning("HUD: SimBridge not found — display will be static")

	pause_btn.pressed.connect(_on_pause_pressed)
	speed_1x.pressed.connect(func(): _set_speed(1.0))
	speed_2x.pressed.connect(func(): _set_speed(2.0))
	speed_5x.pressed.connect(func(): _set_speed(5.0))

	_setup_tooltips()
	_setup_focus_navigation()
	_update_all()
	print("HUD: ready")

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_accept"):          # Space
		_toggle_pause()
		get_viewport().set_input_as_handled()

# ── Update helpers ─────────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_update_all()

func _on_day_advanced(day: int, season: String) -> void:
	day_value.text = str(day)
	_update_season(season)

func _update_all() -> void:
	if not _sim:
		_set_placeholders()
		return

	# Day
	day_value.text = str(_sim.current_day)

	# Season
	_update_season(_sim._season_name(_sim.current_day))

	# Balance — color-coded via SimBridge helper
	balance_value.text = _sim.format_nok(_sim.balance)
	balance_value.add_theme_color_override("font_color", _sim.balance_color(_sim.balance))

	# Fish count
	fish_value.text = _sim.format_fish(_sim.fish_count)
	fish_value.add_theme_color_override("font_color",
		COLOR_GOOD if _sim.fish_count > 0 else COLOR_WARN)

	# Temperature
	temp_value.text = "%.1f%s" % [_sim.temperature_c, tr("ui.hud.temp_c")]
	temp_value.add_theme_color_override("font_color", _temp_color(_sim.temperature_c))

	# Market price
	price_value.text = "%.0f %s" % [_sim.market_price_nok, tr("ui.hud.price_kg")]
	price_value.add_theme_color_override("font_color", _price_color(_sim.market_price_nok))

func _update_season(season: String) -> void:
	season_icon.text  = SEASON_ICONS.get(season, "?")
	season_value.text = season

func _set_placeholders() -> void:
	var no_data = tr("ui.hud.no_data")
	day_value.text     = no_data
	season_icon.text   = "?"
	season_value.text  = "---"
	balance_value.text = "%s NOK" % no_data
	fish_value.text    = no_data
	temp_value.text    = "%s%s" % [no_data, tr("ui.hud.temp_c")]
	price_value.text   = "%s %s" % [no_data, tr("ui.hud.price_kg")]

# ── Color logic ────────────────────────────────────────────────────────────────

func _temp_color(t: float) -> Color:
	if t < 4.0 or t > 18.0:
		return COLOR_DANGER
	elif t < 8.0 or t > 16.0:
		return COLOR_WARN
	return COLOR_GOOD

func _price_color(price: float) -> Color:
	if price >= 75.0:
		return COLOR_GOOD
	elif price >= 60.0:
		return COLOR_WARN
	return COLOR_DANGER

# ── Speed controls ─────────────────────────────────────────────────────────────

func _set_speed(speed: float) -> void:
	_current_speed = speed
	_is_paused = false
	if _sim:
		_sim.set_time_speed(speed)
		_sim.set_paused(false)
	_refresh_speed_buttons()
	speed_changed.emit(_current_speed, _is_paused)

func _toggle_pause() -> void:
	_is_paused = not _is_paused
	if _sim:
		_sim.set_paused(_is_paused)
	_refresh_speed_buttons()
	speed_changed.emit(_current_speed, _is_paused)

func _refresh_speed_buttons() -> void:
	speed_1x.button_pressed = (not _is_paused and _current_speed == 1.0)
	speed_2x.button_pressed = (not _is_paused and _current_speed == 2.0)
	speed_5x.button_pressed = (not _is_paused and _current_speed == 5.0)
	pause_btn.text         = "▶" if _is_paused else "⏸"
	pause_btn.tooltip_text = "Resume" if _is_paused else "Pause"

func _on_pause_pressed() -> void:
	_toggle_pause()

# ── Accessibility ──────────────────────────────────────────────────────────────

func _setup_tooltips() -> void:
	# Speed control tooltips with keyboard hints
	pause_btn.tooltip_text = "Pause/Resume (Space)"
	speed_1x.tooltip_text = "Normal hastighet (1x)"
	speed_2x.tooltip_text = "Dobbel hastighet (2x)"
	speed_5x.tooltip_text = "5x hastighet"
	
	# Data display tooltips
	if day_value.get_parent().has_method("set_tooltip_text"):
		day_value.get_parent().tooltip_text = "Gjeldende dag i simulasjonen"
	if season_value.get_parent().has_method("set_tooltip_text"):
		season_value.get_parent().tooltip_text = "Årstid påvirker vekst og helse"
	if balance_value.get_parent().has_method("set_tooltip_text"):
		balance_value.get_parent().tooltip_text = "Saldo i Norske kroner"
	if fish_value.get_parent().has_method("set_tooltip_text"):
		fish_value.get_parent().tooltip_text = "Totalt antall fisk"
	if temp_value.get_parent().has_method("set_tooltip_text"):
		temp_value.get_parent().tooltip_text = "Vanntemperatur (optimal: 8-16°C)"
	if price_value.get_parent().has_method("set_tooltip_text"):
		price_value.get_parent().tooltip_text = "Markedspris per kilogram"


func _setup_focus_navigation() -> void:
	# Set up keyboard navigation for speed controls
	pause_btn.focus_neighbor_right = NodePath("../Speed1x")
	speed_1x.focus_neighbor_left = NodePath("../PauseButton")
	speed_1x.focus_neighbor_right = NodePath("../Speed2x")
	speed_2x.focus_neighbor_left = NodePath("../Speed1x")
	speed_2x.focus_neighbor_right = NodePath("../Speed5x")
	speed_5x.focus_neighbor_left = NodePath("../Speed2x")
	
	# Enable focus mode on buttons
	pause_btn.focus_mode = Control.FOCUS_ALL
	speed_1x.focus_mode = Control.FOCUS_ALL
	speed_2x.focus_mode = Control.FOCUS_ALL
	speed_5x.focus_mode = Control.FOCUS_ALL
