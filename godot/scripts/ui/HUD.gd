extends Control

## HUD — the top bar (layout in scenes/ui/HUD.tscn; this script is wiring).
##
##   DAG 95                 SALDO      FISK    SJØTEMP  SPOTPRIS   VARSLER  VÆR
##   Uke 14 · april · År 1  7,65 MNOK  106,4k  7,6 °C   88 kr/kg   0        Storm
##                     [goal]   [Bremset · frist 12. april] (bell) [|| 1× 3× 10× 30× >|]
##
## The calendar is the sim's (GameCalendar, ISO weeks). The time controls draw
## the game clock (SimBridge.clock_changed, roadmap U2-07): pause, the four
## speeds on the keys 1–4, "Til neste hendelse" on N, and a chip that says
## why the clock slowed or stopped by itself (a deadline, the next event).
## Readouts are captioned in words, not emoji (D14); the bell is drawn.

# Readout tones resolve on the HUD's Ink surface (theme variations, D13).
const TONE_OK := &"Ok"
const TONE_WARN := &"Warn"
const TONE_CRIT := &"Crit"
const TONE_TEXT := &""
const TONE_MUTED := &"Muted"

## The four speeds (GameSpeed.Steps) and their keys 1–4 (D15).
const SPEEDS: Array[int] = [1, 3, 10, 30]
const SPEED_ACTIONS: Array[StringName] = [&"speed_1", &"speed_2", &"speed_3", &"speed_4"]
const SPEED_TIP_KEYS := ["UI_HUD_SPEED_TIP_1", "UI_HUD_SPEED_TIP_3", "UI_HUD_SPEED_TIP_10", "UI_HUD_SPEED_TIP_30"]

## Readouts that give way, in this order, when the bar is too narrow for all
## of them (1280 px at UI scale 125 %): the weather, then the spot price.
const FIT_ORDER := [&"WeatherItem", &"PriceItem", &"TempItem"]

## SimFormatters.season_name() ids → translation keys (tooltip).
const SEASON_KEYS := {"Vinter": "UI_HUD_SEASON_WINTER", "Vår": "UI_HUD_SEASON_SPRING",
	"Sommer": "UI_HUD_SEASON_SUMMER", "Høst": "UI_HUD_SEASON_AUTUMN"}

# ── State ──────────────────────────────────────────────────────────────────────
var _sim: Node = null
var _current_speed: float = 1.0
## The clock stands (the player's pause or a hold): mirrors the game clock.
var _is_paused: bool = false
var _clock: Dictionary = {}
var _prev_balance: float = 0.0
var _balance_init: bool = false
## The bell's unread badge (U2-06): warnings and criticals not yet seen.
var _badge: Label = null
var _badge_panel: PanelContainer = null
var _unread: int = 0
var _fit_queued: bool = false
## The last readout values drawn (refreshes that change nothing draw nothing).
var _drawn: Dictionary = {}

## The player changed the clock from the HUD (speed, pause, Space, 1–4, N).
signal speed_changed(speed: float, paused: bool)

# ── UI Refs (declared in HUD.tscn) ─────────────────────────────────────────────
@onready var calendar_item: Control = %CalendarItem
@onready var day_caption: Label = %DayCaption
@onready var calendar_value: Label = %CalendarValue
@onready var balance_value: Label = %BalanceValue
@onready var fish_value: Label = %FishValue
@onready var temp_value: Label = %TempValue
@onready var price_value: Label = %PriceValue
@onready var alerts_value: Label = %AlertsValue
@onready var weather_value: Label = %WeatherValue
@onready var pause_btn: Button = %PauseBtn
@onready var pause_glyph: TimeGlyph = %PauseGlyph
@onready var inbox_btn: Button = %InboxBtn
@onready var speed_1x: Button = %Speed1x
@onready var speed_3x: Button = %Speed3x
@onready var speed_10x: Button = %Speed10x
@onready var speed_30x: Button = %Speed30x
@onready var next_event_btn: Button = %NextEventBtn
@onready var time_controls: PanelContainer = %TimeControls
@onready var pacing_chip: PanelContainer = %PacingChip
@onready var pacing_label: Label = %PacingLabel

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	if _sim:
		_sim.state_updated.connect(_on_state_updated)
		_sim.clock_changed.connect(_on_clock_changed)
	_style_time_controls()
	_connect_buttons()
	_build_badge()
	inbox_btn.tooltip_text = tr("UI_HUD_TIP_NOTIFICATIONS") % 0
	# The inbox (NotificationCenter) is a later sibling: wire it once it is ready.
	_connect_inbox.call_deferred()
	resized.connect(_queue_fit)
	_update_all()
	_on_clock_changed(_sim.get_clock_state() if _sim else {})
	_queue_fit()

## Space pauses and resumes, 1–4 pick a speed, N runs to the next event,
## anywhere except while typing (D15). Enter stays with the GUI, so the
## focused button activates. A card or menu that waits for the player takes
## these keys first (they are blocked while it is open).
func _input(event: InputEvent) -> void:
	if not (event is InputEventKey) or not event.is_pressed() or event.is_echo():
		return
	if UIPolish.is_typing(self):
		return
	if event.is_action_pressed("toggle_pause"):
		_toggle_pause()
		get_viewport().set_input_as_handled()
		return
	# Exact match: Cmd/Ctrl+digit opens a tab and Shift+digit is a lens (D15).
	for i in SPEED_ACTIONS.size():
		if event.is_action_pressed(SPEED_ACTIONS[i], false, true):
			_set_speed(float(SPEEDS[i]))
			get_viewport().set_input_as_handled()
			return
	if event.is_action_pressed("next_event", false, true):
		_on_next_event_pressed()
		get_viewport().set_input_as_handled()

# ── Wiring ─────────────────────────────────────────────────────────────────────

## The four speeds and the two glyph buttons sit in one sunken strip: a
## segmented control, the pressed segment lit with the rail's salmon line.
func _style_time_controls() -> void:
	var strip := StyleBoxFlat.new()
	strip.bg_color = LakskongenTheme.INK_SUNKEN
	strip.border_color = LakskongenTheme.INK_HAIRLINE
	strip.set_border_width_all(1)
	strip.set_corner_radius_all(6)
	strip.set_content_margin_all(1)
	time_controls.add_theme_stylebox_override("panel", strip)
	for i in SPEEDS.size():
		_speed_buttons()[i].tooltip_text = tr(SPEED_TIP_KEYS[i])
	next_event_btn.tooltip_text = tr("UI_HUD_NEXT_EVENT_TIP")

## U2-02 highlight targets: the clock strip and the bell.
func clock_controls() -> Control:
	return time_controls

func inbox_button() -> Button:
	return inbox_btn

func _speed_buttons() -> Array[Button]:
	return [speed_1x, speed_3x, speed_10x, speed_30x]

func _connect_buttons() -> void:
	pause_btn.pressed.connect(_on_pause_pressed)
	for i in SPEEDS.size():
		_speed_buttons()[i].pressed.connect(_set_speed.bind(float(SPEEDS[i])))
	next_event_btn.pressed.connect(_on_next_event_pressed)
	inbox_btn.pressed.connect(_on_inbox_pressed)
	pacing_chip.gui_input.connect(_on_pacing_chip_input)
	for btn: Button in [pause_btn, speed_1x, speed_3x, speed_10x, speed_30x, next_event_btn, inbox_btn]:
		UIPolish.attach_press_feedback(btn)
		# Buttons in the bar never keep keyboard focus: Space is the pause key.
		btn.focus_mode = Control.FOCUS_NONE

## The bell opens the event inbox (the notification drawer, also V); the
## attention list stays on I.
func _on_inbox_pressed() -> void:
	var inbox := get_tree().get_first_node_in_group(&"event_inbox")
	if inbox != null and inbox.has_method("toggle"):
		inbox.toggle()
	elif _sim and _sim.has_method("open_attention_inbox"):
		_sim.open_attention_inbox()

func _connect_inbox() -> void:
	var inbox := get_tree().get_first_node_in_group(&"event_inbox")
	if inbox == null or not inbox.has_signal("unread_changed"):
		return
	if not inbox.unread_changed.is_connected(_on_unread_changed):
		inbox.unread_changed.connect(_on_unread_changed)
	_on_unread_changed(inbox.unread_count(), inbox.has_unread_critical())

## A small solid tone tag on the bell's corner: the unread count in Ink on
## warn, on crit when one of them is critical (D13's "Ink on a tone"; a
## tinted pill read 4.4:1). Hidden at zero.
func _build_badge() -> void:
	_badge_panel = PanelContainer.new()
	_badge_panel.name = "UnreadBadgePanel"
	_badge_panel.theme_type_variation = &"SwatchWarn"
	_badge_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_badge_panel.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	_badge_panel.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_badge_panel.offset_right = 4.0
	_badge_panel.offset_top = 1.0
	_badge_panel.visible = false
	var pad := MarginContainer.new()
	pad.mouse_filter = Control.MOUSE_FILTER_IGNORE
	pad.add_theme_constant_override("margin_left", 4)
	pad.add_theme_constant_override("margin_right", 4)
	_badge_panel.add_child(pad)
	_badge = Label.new()
	_badge.name = "UnreadBadge"
	_badge.theme_type_variation = &"Caption"
	_badge.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
	pad.add_child(_badge)
	inbox_btn.add_child(_badge_panel)
	_badge.add_theme_color_override("font_color", get_theme_color("on_salmon", "Palette"))

## Badge = unread warnings and criticals in the inbox.
func _on_unread_changed(count: int, has_critical: bool) -> void:
	_unread = count
	if _badge != null:
		_badge_panel.visible = count > 0
		_badge.text = str(count) if count < 100 else "99+"
		_badge_panel.theme_type_variation = &"SwatchCrit" if has_critical else &"SwatchWarn"
	inbox_btn.tooltip_text = tr("UI_HUD_TIP_NOTIFICATIONS") % count

## Unread warnings and criticals the badge shows (QA and tests).
func unread_badge_count() -> int:
	return _unread if _badge_panel != null and _badge_panel.visible else 0

# ── Readouts ───────────────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_update_all()

## Draw the readouts. Called on every snapshot refresh (two per sim day): a
## value that did not change is not set again, so at 30× the bar costs nothing
## between days (U2-07: the UI side stays within a 60 fps frame).
func _update_all() -> void:
	if not _sim:
		return
	var day: int = int(_sim.current_day)
	if _changed(&"day", day):
		day_caption.text = tr("UI_DAY_N") % day
		calendar_value.text = GameCalendar.hud_label(day)
		calendar_item.tooltip_text = tr("UI_HUD_TIP_CALENDAR") % [GameCalendar.long_date(day), _season_text(day)]

	var balance := float(_sim.balance)
	if _changed(&"balance", balance):
		balance_value.text = _sim.format_nok(balance)
		LakskongenTheme.set_tone(balance_value, TONE_OK if balance >= 0 else TONE_CRIT)
		# Juice: count-up tween on balance (display only, no sim changes).
		_count_up_balance(balance)
		balance_value.tooltip_text = tr("UI_HUD_TIP_BALANCE") % _sim.format_nok(balance)

	var fish := int(_sim.fish_count)
	if _changed(&"fish", fish):
		fish_value.text = _sim.format_fish(fish)
		LakskongenTheme.set_tone(fish_value, TONE_TEXT if fish > 0 else TONE_WARN)
		fish_value.tooltip_text = tr("UI_HUD_TIP_FISH") % SimFormatters.integer(fish)

	var temp := snappedf(float(_sim.temperature_c), 0.1)
	if _changed(&"temp", temp):
		temp_value.text = tr("UI_HUD_TEMP_FORMAT") % SimFormatters.num(temp, 1)
		LakskongenTheme.set_tone(temp_value, _temp_tone(temp))
		temp_value.tooltip_text = tr("UI_HUD_TIP_TEMP") % SimFormatters.num(temp, 1)

	var price := roundf(float(_sim.market_price_nok))
	if _changed(&"price", price):
		price_value.text = tr("UI_HUD_PRICE_FORMAT") % SimFormatters.num(price, 0)
		LakskongenTheme.set_tone(price_value, _price_tone(price))
		price_value.tooltip_text = tr("UI_HUD_TIP_PRICE") % SimFormatters.num(price, 0)

	var alert_count: int = _sim.active_alerts.size() if _sim.get("active_alerts") != null else 0
	if _changed(&"alerts", alert_count):
		alerts_value.text = str(alert_count)
		LakskongenTheme.set_tone(alerts_value, TONE_CRIT if alert_count > 0 else TONE_MUTED)
		alerts_value.tooltip_text = tr("UI_HUD_TIP_ALERTS") % alert_count

	var weather := str(_sim.get_current_weather_state())
	if _changed(&"weather", weather):
		weather_value.text = SimLabels.weather(weather)
		weather_value.tooltip_text = tr("UI_HUD_TIP_WEATHER")
		_queue_fit()

## True (and remembered) when `value` differs from the one drawn for `key`.
func _changed(key: StringName, value: Variant) -> bool:
	if _drawn.has(key) and _drawn[key] == value:
		return false
	_drawn[key] = value
	return true

func _season_text(day: int) -> String:
	var season := SimFormatters.season_name(day)
	return tr(str(SEASON_KEYS[season])) if SEASON_KEYS.has(season) else season

# ── Count-up juice (display only) ────────────────────────────────────────────

## Tween the balance label toward its new value. First call snaps (no
## previous value yet); later calls animate over 0.6 s, one tween at a time,
## and not at 10× or 30× (a new day every other frame) or with reduced motion.
var _balance_tween: Tween = null

func _count_up_balance(to_val: float) -> void:
	if not _balance_init:
		_prev_balance = to_val
		_balance_init = true
		return
	var from_val := _prev_balance
	_prev_balance = to_val
	if _balance_tween != null and _balance_tween.is_valid():
		_balance_tween.kill()
	if is_equal_approx(from_val, to_val) or MotionPrefs.reduced_motion() \
			or (not _is_paused and _current_speed >= 10.0):
		return
	_balance_tween = balance_value.create_tween()
	_balance_tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	_balance_tween.tween_method(_apply_balance_text, from_val, to_val, 0.6)

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

# ── The clock ──────────────────────────────────────────────────────────────────

func _set_speed(speed: float) -> void:
	if _sim:
		_sim.set_time_speed(speed)
		_sim.set_paused(false)
		_sim.tutorial_mark("clock")
	speed_changed.emit(speed, false)

func _toggle_pause() -> void:
	if _sim == null:
		return
	var pause := not bool(_sim.is_paused())
	if not pause and _sim.is_clock_held(""):
		return   # a card or a menu holds the clock: it gives it back itself
	_sim.set_paused(pause)
	if not pause:
		_sim.tutorial_mark("clock")
	speed_changed.emit(_current_speed, pause)

func _on_pause_pressed() -> void:
	_toggle_pause()

## N: run to the next event, or stop a run that is going (back to the speed
## before it, still running).
func _on_next_event_pressed() -> void:
	if _sim == null:
		return
	if _sim.is_running_to_next_event():
		_sim.stop_run_to_next_event()
	elif _sim.run_to_next_event():
		_sim.tutorial_mark("clock")
	_on_clock_changed(_sim.get_clock_state())

func _on_clock_changed(clock: Dictionary) -> void:
	_clock = clock
	_current_speed = float(clock.get("speed", _current_speed))
	_is_paused = bool(clock.get("paused", _is_paused))
	_refresh_speed_buttons()
	_refresh_pacing_chip()

func _refresh_speed_buttons() -> void:
	var running_next := bool(_clock.get("next_event", false))
	for i in SPEEDS.size():
		var btn: Button = _speed_buttons()[i]
		btn.set_pressed_no_signal(not _is_paused and not running_next and int(_current_speed) == SPEEDS[i])
	next_event_btn.set_pressed_no_signal(running_next and not _is_paused)
	pause_btn.set_pressed_no_signal(_is_paused)
	pause_glyph.kind = TimeGlyph.Kind.PLAY if _is_paused else TimeGlyph.Kind.PAUSE
	pause_btn.tooltip_text = tr("UI_HUD_PAUSE_RESUME_TOOLTIP") if _is_paused else tr("UI_HUD_PAUSE_PAUSE_TOOLTIP")

## Why the clock slowed or stopped by itself, or that it stands: one chip.
func _refresh_pacing_chip() -> void:
	var view := pacing_view(_clock)
	pacing_chip.visible = not view.is_empty()
	if view.is_empty():
		_queue_fit()
		return
	pacing_label.text = str(view["text"])
	pacing_chip.tooltip_text = str(view["tip"])
	pacing_chip.theme_type_variation = StringName("Swatch" + String(view["tone"]))
	# Ink text on a solid tone (D13): the swatch is the signal, the text reads.
	pacing_label.add_theme_color_override("font_color", get_theme_color("on_salmon", "Palette"))
	_queue_fit()

## What the chip says for a clock state: {} (no chip) or {text, tip, tone}.
## Static so tests can read the rule without a scene.
static func pacing_view(clock: Dictionary) -> Dictionary:
	if clock.is_empty() or bool(clock.get("briefing_pending", false)):
		return {}
	var pacing: Dictionary = clock.get("pacing", {})
	var holds: Array = clock.get("holds", [])
	var today := int(clock.get("day", 0))
	if bool(clock.get("next_event", false)) and not bool(clock.get("paused", false)):
		return {"text": TranslationServer.translate("UI_PACING_RUNNING"),
			"tip": TranslationServer.translate("UI_HUD_NEXT_EVENT_TIP"), "tone": "Info"}
	if not pacing.is_empty():
		var reason := str(pacing.get("reason", ""))
		var title := _pacing_title(pacing)
		match reason:
			"deadline_approaching", "deadline_imminent":
				var due := int(pacing.get("deadline_day", today))
				var left := maxi(due - today, 0)
				var key := "UI_PACING_SLOWED" if reason == "deadline_approaching" else "UI_PACING_STOPPED_DEADLINE"
				return {"text": str(TranslationServer.translate(key)) % GameCalendar.short_date(due),
					"tip": str(TranslationServer.translate("UI_PACING_TIP_DEADLINE")) % [title, GameCalendar.long_date(due), left],
					"tone": "Warn" if reason == "deadline_approaching" else "Crit"}
			"next_event_reached":
				return {"text": str(TranslationServer.translate("UI_PACING_STOPPED_EVENT")) % title,
					"tip": str(TranslationServer.translate("UI_PACING_TIP_EVENT")) % title,
					"tone": "Crit" if str(pacing.get("severity", "")) == "critical" else "Info"}
			"next_event_quiet":
				return {"text": str(TranslationServer.translate("UI_PACING_STOPPED_QUIET")) % PACING_QUIET_DAYS,
					"tip": TranslationServer.translate("UI_PACING_TIP_QUIET"), "tone": "Info"}
	if bool(clock.get("paused", false)) and holds.is_empty():
		return {"text": TranslationServer.translate("UI_PACING_PAUSED"),
			"tip": TranslationServer.translate("UI_PACING_TIP_PAUSED"), "tone": "Muted"}
	return {}

## "Til neste hendelse" stops after this many quiet days (PacingRules.NextEventMaxDays).
const PACING_QUIET_DAYS := 60

## The title of what a pacing decision is about: its own headline or its type's.
static func _pacing_title(pacing: Dictionary) -> String:
	var headline := str(pacing.get("headline", ""))
	if headline != "":
		return headline
	var type_id := str(pacing.get("type", ""))
	if type_id == "":
		return str(TranslationServer.translate("UI_PACING_DEADLINE_FALLBACK"))
	return GameEventText.title({"type": type_id})

## A click on the chip opens the event inbox, where the event and its verb are.
func _on_pacing_chip_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_on_inbox_pressed()

## Sync with an external pause change (kept for callers; the clock signal
## does this on its own).
func set_paused_state(paused: bool) -> void:
	_is_paused = paused
	_refresh_speed_buttons()

## The clock state the HUD drew last (tests).
func clock_state() -> Dictionary:
	return _clock

# ── Fitting the bar ───────────────────────────────────────────────────────────

func _queue_fit() -> void:
	if _fit_queued:
		return
	_fit_queued = true
	_fit.call_deferred()

## Show every readout that fits: when the bar's content is wider than the
## window (1280 px at UI scale 125 %), the weather, the spot price and the
## sea temperature give way in that order (their values stay in the panels).
func _fit() -> void:
	_fit_queued = false
	var box := get_node_or_null("MainHBox") as Control
	if box == null:
		return
	for item_name: StringName in FIT_ORDER:
		(get_node("%" + String(item_name)) as Control).visible = true
	(get_node("%Sep2") as Control).visible = true
	# The room the bar has: the HUD's width inside the box's margins (the box
	# itself grows to its content, so its own size says nothing).
	var available := bar_width()
	for item_name: StringName in FIT_ORDER:
		if box.get_combined_minimum_size().x <= available:
			break
		(get_node("%" + String(item_name)) as Control).visible = false

## The width the bar's content may take: the HUD's inside its margins.
func bar_width() -> float:
	var box := get_node("MainHBox") as Control
	return size.x - box.offset_left + box.offset_right

## Readouts hidden to fit the bar (tests).
func hidden_readouts() -> Array[String]:
	var out: Array[String] = []
	for item_name: StringName in FIT_ORDER:
		if not (get_node("%" + String(item_name)) as Control).visible:
			out.append(String(item_name))
	return out
