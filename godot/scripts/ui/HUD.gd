extends Control

## HUD v2.1 — Nordic Minimalist top bar (scene-based).
## Layout lives in scenes/ui/HUD.tscn; this script is wiring only:
## SimBridge hookups, value updates, speed controls, localization.

# ── Palette ────────────────────────────────────────────────────────────────────
# Readout tones resolve on the HUD's Ink surface (theme variations, D13).
const TONE_OK := &"Ok"
const TONE_WARN := &"Warn"
const TONE_CRIT := &"Crit"
const TONE_TEXT := &""
const TONE_MUTED := &"Muted"

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
@onready var inbox_btn: Button = %InboxBtn
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

## Space (toggle_pause) pauses/resumes anywhere except while typing. Enter
## stays with the GUI, so the focused button activates; this handler used to
## take ui_accept (Space and Enter) before any control saw it.
func _input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_pause") and not UIPolish.is_typing(self):
		_toggle_pause()
		get_viewport().set_input_as_handled()

# ── Wiring ─────────────────────────────────────────────────────────────────────

## Readouts use the Figure role: Inter Medium with tabular figures, so the
## numbers keep their width as they tick (no monospace face, roadmap D14).
func _apply_mono_font() -> void:
	for lbl in [day_value, balance_value, fish_value, temp_value, price_value, alerts_value, weather_value]:
		if lbl:
			LakskongenTheme.set_role(lbl, &"Figure")

func _connect_buttons() -> void:
	if not (pause_btn and speed_1x and speed_2x and speed_5x):
		return
	pause_btn.pressed.connect(_on_pause_pressed)
	speed_1x.pressed.connect(_set_speed.bind(1.0))
	speed_2x.pressed.connect(_set_speed.bind(2.0))
	speed_5x.pressed.connect(_set_speed.bind(5.0))
	if inbox_btn:
		inbox_btn.pressed.connect(_on_inbox_pressed)

func _on_inbox_pressed() -> void:
	if _sim and _sim.has_method("open_attention_inbox"):
		_sim.open_attention_inbox()

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
	LakskongenTheme.set_tone(balance_value, TONE_OK if _sim.balance >= 0 else TONE_CRIT)
	# Juice: count-up tween on balance (display only, no sim changes).
	_count_up_balance(float(_sim.balance))

	fish_value.text = _sim.format_fish(_sim.fish_count)
	LakskongenTheme.set_tone(fish_value, TONE_TEXT if _sim.fish_count > 0 else TONE_WARN)

	temp_value.text = _tr_or_format("UI_HUD_TEMP_FORMAT", "%.1f°C", _sim.temperature_c)
	LakskongenTheme.set_tone(temp_value, _temp_tone(_sim.temperature_c))

	price_value.text = _tr_or_format("UI_HUD_PRICE_FORMAT", "%.0f kr/kg", _sim.market_price_nok)
	LakskongenTheme.set_tone(price_value, _price_tone(_sim.market_price_nok))

	var alert_count = _sim.active_alerts.size() if _sim.get("active_alerts") else 0
	alerts_value.text = str(alert_count)
	LakskongenTheme.set_tone(alerts_value, TONE_CRIT if alert_count > 0 else TONE_MUTED)

	weather_value.text = _sim.get_current_weather_state()

	_update_tooltips(alert_count)

## "Why is this number what it is?" explanations for every HUD KPI.
func _update_tooltips(alert_count: int) -> void:
	balance_value.tooltip_text = (_tr_or("UI_HUD_TIP_BALANCE",
		"Cash on hand — all income minus costs, loans and investments.\nCurrent: %s")
		% _sim.format_nok(_sim.balance))
	fish_value.tooltip_text = (_tr_or("UI_HUD_TIP_FISH",
		"Total fish across all active pens.\nCurrent: %s")
		% _sim.format_fish(_sim.fish_count))
	temp_value.tooltip_text = (_tr_or("UI_HUD_TIP_TEMP",
		"Sea temperature — drives growth rate and lice reproduction.\nCurrent: %.1f°C")
		% _sim.temperature_c)
	price_value.tooltip_text = (_tr_or("UI_HUD_TIP_PRICE",
		"Spot price per kg — moves with market events, season and demand.\nCurrent: %.0f kr/kg")
		% _sim.market_price_nok)
	alerts_value.tooltip_text = (_tr_or("UI_HUD_TIP_ALERTS",
		"Active alerts — press I or click 🔔 to open the Attention Inbox.\n%d active")
		% alert_count)
	weather_value.tooltip_text = _tr_or("UI_HUD_TIP_WEATHER",
		"Current weather state — storms can damage pens and halt operations.")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

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
func _temp_tone(t: float) -> StringName:
	if t < 4.0 or t > 18.0: return TONE_CRIT
	if t < 8.0 or t > 16.0: return TONE_WARN
	return TONE_OK

func _price_tone(p: float) -> StringName:
	if p >= 75.0: return TONE_OK
	if p >= 60.0: return TONE_WARN
	return TONE_CRIT

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

## Sync HUD with an external pause change (e.g. auto-pause on critical alerts).
func set_paused_state(paused: bool) -> void:
	_is_paused = paused
	_refresh_speed_buttons()
