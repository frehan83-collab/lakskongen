extends Control

## HUD v2.1 — Nordic Minimalist top bar (scene-based).
## Layout lives in scenes/ui/HUD.tscn; this script is wiring only:
## SimBridge hookups, value updates, speed controls, localization.

# ── Palette ────────────────────────────────────────────────────────────────────
const C_BG      := Color("#0A4D68")   # Primary dark teal
const C_BG2     := Color("#083D53")   # Darker variant
const C_TEXT    := Color("#ECEFF4")   # White-ish
const C_MUTED   := Color("#A3D1DE")   # Secondary light
const C_SUCCESS := Color("#A3BE8C")
const C_WARNING := Color("#EBCB8B")
const C_DANGER  := Color("#BF616A")
const C_NEUTRAL := Color("#D8DEE9")

const SEASON_ICONS := {"Vinter": "❄️", "Vår": "🌱", "Sommer": "☀️", "Høst": "🍂"}

# ── State ──────────────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme_node: Node = null
var _current_speed: float = 1.0
var _is_paused: bool = false
var _prev_balance: float = 0.0
var _balance_init: bool = false

signal speed_changed(speed: float, paused: bool)

# ── UI Refs (declared in HUD.tscn) ─────────────────────────────────────────────
@onready var day_value: Label = %DayValue
@onready var season_icon: Label = %SeasonIcon
@onready var season_value: Label = %SeasonValue
@onready var balance_value: Label = %BalanceValue
@onready var fish_value: Label = %FishValue
@onready var temp_value: Label = %TempValue
@onready var price_value: Label = %PriceValue
@onready var alerts_value: Label = %AlertsValue
@onready var weather_value: Label = %WeatherValue
@onready var pause_btn: Button = %PauseBtn
@onready var speed_1x: Button = %Speed1x
@onready var speed_2x: Button = %Speed2x
@onready var speed_5x: Button = %Speed5x

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme_node = get_node_or_null("/root/LakskongenTheme")

	if _sim:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)
		if _sim.has_signal("day_advanced"):
			_sim.day_advanced.connect(_on_day_advanced)

	_apply_mono_font()
	_connect_buttons()
	_update_all()
	_refresh_speed_buttons()

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_accept"):
		_toggle_pause()
		get_viewport().set_input_as_handled()
	elif event.is_action_pressed("ui_cancel"):
		# Toggle pause menu through signal
		var game_ui = get_node_or_null("/root/GameUI")
		if game_ui and game_ui.has_method("toggle_pause"):
			game_ui.toggle_pause()
			get_viewport().set_input_as_handled()

# ── Wiring ─────────────────────────────────────────────────────────────────────

func _apply_mono_font() -> void:
	if not (_theme_node and _theme_node.font_mono):
		return
	for lbl in [day_value, balance_value, fish_value, temp_value, price_value, alerts_value, weather_value]:
		if lbl:
			lbl.add_theme_font_override("font", _theme_node.font_mono)

func _connect_buttons() -> void:
	if not (pause_btn and speed_1x and speed_2x and speed_5x):
		return
	pause_btn.pressed.connect(_on_pause_pressed)
	speed_1x.pressed.connect(_set_speed.bind(1.0))
	speed_2x.pressed.connect(_set_speed.bind(2.0))
	speed_5x.pressed.connect(_set_speed.bind(5.0))

# ── Update helpers ─────────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_update_all()

func _on_day_advanced(day: int, season: String) -> void:
	if day_value:
		day_value.text = str(day)
	_update_season(season)

func _update_all() -> void:
	if not _sim:
		return
	if not (day_value and season_icon and season_value and balance_value \
			and fish_value and temp_value and price_value and alerts_value \
			and weather_value):
		return

	day_value.text = str(_sim.current_day)
	_update_season(_sim._season_name(_sim.current_day))

	balance_value.text = _sim.format_nok(_sim.balance)
	balance_value.add_theme_color_override("font_color",
		C_SUCCESS if _sim.balance >= 0 else C_DANGER)
	# Juice: count-up tween on balance (display only, no sim changes).
	_count_up_balance(float(_sim.balance))

	fish_value.text = _sim.format_fish(_sim.fish_count)
	fish_value.add_theme_color_override("font_color",
		C_TEXT if _sim.fish_count > 0 else C_WARNING)

	temp_value.text = _tr_or_format("UI_HUD_TEMP_FORMAT", "%.1f°C", _sim.temperature_c)
	temp_value.add_theme_color_override("font_color", _temp_color(_sim.temperature_c))

	price_value.text = _tr_or_format("UI_HUD_PRICE_FORMAT", "%.0f kr/kg", _sim.market_price_nok)
	price_value.add_theme_color_override("font_color", _price_color(_sim.market_price_nok))

	var alert_count = _sim.active_alerts.size() if _sim.get("active_alerts") else 0
	alerts_value.text = str(alert_count)
	alerts_value.add_theme_color_override("font_color",
		C_DANGER if alert_count > 0 else C_NEUTRAL)

	weather_value.text = _sim.get_current_weather_state()

func _update_season(season: String) -> void:
	if season_icon:
		season_icon.text = SEASON_ICONS.get(season, "?")
	if season_value:
		season_value.text = season

## Translated single-value format with fallback: tr() returns the key when
## untranslated, and formatting the raw key aborts on missing placeholders.
func _tr_or_format(key: String, fallback_fmt: String, value: float) -> String:
	var fmt := tr(key)
	if fmt == key or not ("%" in fmt):
		return fallback_fmt % value
	return fmt % value

# ── Count-up juice (display only) ────────────────────────────────────────────

## Tween the balance label toward its new value. First call snaps (no
## previous value yet); later calls animate over 0.6 s.
func _count_up_balance(to_val: float) -> void:
	if not _balance_init:
		_prev_balance = to_val
		_balance_init = true
		return
	var from_val := _prev_balance
	_prev_balance = to_val
	if is_equal_approx(from_val, to_val):
		return
	var tween := balance_value.create_tween()
	tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	tween.tween_method(_apply_balance_text, from_val, to_val, 0.6)

func _apply_balance_text(v: float) -> void:
	if _sim:
		balance_value.text = _sim.format_nok(v)

# ── Color logic ───────────────────────────────────────────────────────────────
func _temp_color(t: float) -> Color:
	if t < 4.0 or t > 18.0: return C_DANGER
	if t < 8.0 or t > 16.0: return C_WARNING
	return C_SUCCESS

func _price_color(p: float) -> Color:
	if p >= 75.0: return C_SUCCESS
	if p >= 60.0: return C_WARNING
	return C_DANGER

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

func _on_pause_pressed() -> void:
	_toggle_pause()

func _refresh_speed_buttons() -> void:
	if not (speed_1x and speed_2x and speed_5x and pause_btn):
		return
	speed_1x.button_pressed = (not _is_paused and _current_speed == 1.0)
	speed_2x.button_pressed = (not _is_paused and _current_speed == 2.0)
	speed_5x.button_pressed = (not _is_paused and _current_speed == 5.0)
	pause_btn.text = "▶️" if _is_paused else "⏸️"
	pause_btn.tooltip_text = "Resume" if _is_paused else "Pause"
