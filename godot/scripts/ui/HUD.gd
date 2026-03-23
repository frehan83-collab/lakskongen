extends Control

## HUD v2.0 — Nordic Minimalist top bar
## Always-visible strip: Day | Season | Balance | Fish | Temp | Price | Alerts | Weather | Speed
## Builds UI programmatically — no .tscn node dependencies.

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

signal speed_changed(speed: float, paused: bool)

# ── UI Refs ────────────────────────────────────────────────────────────────────
var day_value: Label
var season_icon: Label
var season_value: Label
var balance_value: Label
var fish_value: Label
var temp_value: Label
var price_value: Label
var alerts_value: Label
var weather_value: Label
var pause_btn: Button
var speed_1x: Button
var speed_2x: Button
var speed_5x: Button

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme_node = get_node_or_null("/root/LakskongenTheme")

	if _sim:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)
		if _sim.has_signal("day_advanced"):
			_sim.day_advanced.connect(_on_day_advanced)

	_build_ui()
	_update_all()
	print("HUD v2.0: ready (Nordic Minimalist)")

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_accept"):
		_toggle_pause()
		get_viewport().set_input_as_handled()

# ── Build UI ───────────────────────────────────────────────────────────────────

func _build_ui() -> void:
	for child in get_children():
		child.queue_free()

	# Background panel
	var bg := Panel.new()
	var bg_sb := StyleBoxFlat.new()
	bg_sb.bg_color = C_BG
	bg_sb.set_content_margin_all(0)
	bg.add_theme_stylebox_override("panel", bg_sb)
	bg.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	add_child(bg)

	# Main HBox
	var hbox := HBoxContainer.new()
	hbox.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	hbox.offset_left = 16
	hbox.offset_right = -16
	hbox.add_theme_constant_override("separation", 4)
	add_child(hbox)

	# Left section — data items
	var left := HBoxContainer.new()
	left.size_flags_horizontal = SIZE_EXPAND_FILL
	left.add_theme_constant_override("separation", 8)
	hbox.add_child(left)

	# Each data item: icon/label + value
	var day_item = _make_hud_item(left, "📅", "Dag")
	day_value = day_item.value

	var season_item = _make_hud_item_dual(left, "")
	season_icon = season_item.icon
	season_value = season_item.value

	_add_separator(left)

	var bal_item = _make_hud_item(left, "💰", "Saldo")
	balance_value = bal_item.value

	_add_separator(left)

	var fish_item = _make_hud_item(left, "🐟", "Fisk")
	fish_value = fish_item.value

	_add_separator(left)

	var temp_item = _make_hud_item(left, "🌡️", "Temp")
	temp_value = temp_item.value

	var price_item = _make_hud_item(left, "📈", "Pris")
	price_value = price_item.value

	_add_separator(left)

	var alert_item = _make_hud_item(left, "⚠️", "Varsler")
	alerts_value = alert_item.value

	var weather_item = _make_hud_item(left, "🌤️", "Vær")
	weather_value = weather_item.value

	# Right section — speed controls
	var right := HBoxContainer.new()
	right.add_theme_constant_override("separation", 4)
	right.alignment = BoxContainer.ALIGNMENT_END
	hbox.add_child(right)

	pause_btn = _make_speed_btn("⏸", "Pause/Resume (Space)")
	right.add_child(pause_btn)
	pause_btn.pressed.connect(_on_pause_pressed)

	speed_1x = _make_speed_btn("1x", "Normal hastighet")
	right.add_child(speed_1x)
	speed_1x.pressed.connect(func(): _set_speed(1.0))

	speed_2x = _make_speed_btn("2x", "Dobbel hastighet")
	right.add_child(speed_2x)
	speed_2x.pressed.connect(func(): _set_speed(2.0))

	speed_5x = _make_speed_btn("5x", "5x hastighet")
	right.add_child(speed_5x)
	speed_5x.pressed.connect(func(): _set_speed(5.0))

	_refresh_speed_buttons()

func _make_hud_item(parent: HBoxContainer, icon: String, tooltip: String) -> Dictionary:
	var container := HBoxContainer.new()
	container.add_theme_constant_override("separation", 4)
	container.tooltip_text = tooltip
	parent.add_child(container)

	var icon_lbl := Label.new()
	icon_lbl.text = icon
	icon_lbl.add_theme_font_size_override("font_size", 14)
	container.add_child(icon_lbl)

	var val := Label.new()
	val.text = "--"
	val.add_theme_font_size_override("font_size", 13)
	val.add_theme_color_override("font_color", C_TEXT)
	if _theme_node and _theme_node.font_mono:
		val.add_theme_font_override("font", _theme_node.font_mono)
	container.add_child(val)

	return {"container": container, "value": val}

func _make_hud_item_dual(parent: HBoxContainer, tooltip: String) -> Dictionary:
	var container := HBoxContainer.new()
	container.add_theme_constant_override("separation", 4)
	container.tooltip_text = tooltip
	parent.add_child(container)

	var icon_lbl := Label.new()
	icon_lbl.text = "?"
	icon_lbl.add_theme_font_size_override("font_size", 14)
	container.add_child(icon_lbl)

	var val := Label.new()
	val.text = "---"
	val.add_theme_font_size_override("font_size", 13)
	val.add_theme_color_override("font_color", C_MUTED)
	container.add_child(val)

	return {"container": container, "icon": icon_lbl, "value": val}

func _add_separator(parent: HBoxContainer) -> void:
	var sep := VSeparator.new()
	var sep_sb := StyleBoxFlat.new()
	sep_sb.bg_color = Color(1, 1, 1, 0.12)
	sep_sb.set_content_margin(SIDE_LEFT, 4)
	sep_sb.set_content_margin(SIDE_RIGHT, 4)
	sep.add_theme_stylebox_override("separator", sep_sb)
	parent.add_child(sep)

func _make_speed_btn(text: String, tooltip: String) -> Button:
	var btn := Button.new()
	btn.text = text
	btn.tooltip_text = tooltip
	btn.toggle_mode = true
	btn.focus_mode = Control.FOCUS_ALL
	btn.custom_minimum_size = Vector2(36, 0)

	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(1, 1, 1, 0.08)
	sb.set_corner_radius_all(4)
	sb.set_content_margin_all(4)
	btn.add_theme_stylebox_override("normal", sb)

	var hover_sb := StyleBoxFlat.new()
	hover_sb.bg_color = Color(1, 1, 1, 0.15)
	hover_sb.set_corner_radius_all(4)
	hover_sb.set_content_margin_all(4)
	btn.add_theme_stylebox_override("hover", hover_sb)

	var pressed_sb := StyleBoxFlat.new()
	pressed_sb.bg_color = Color("#88C0D0")
	pressed_sb.set_corner_radius_all(4)
	pressed_sb.set_content_margin_all(4)
	btn.add_theme_stylebox_override("pressed", pressed_sb)

	btn.add_theme_color_override("font_color", C_TEXT)
	btn.add_theme_color_override("font_hover_color", Color.WHITE)
	btn.add_theme_color_override("font_pressed_color", Color("#2E3440"))
	btn.add_theme_font_size_override("font_size", 12)

	return btn

# ── Update helpers ─────────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_update_all()

func _on_day_advanced(day: int, season: String) -> void:
	day_value.text = str(day)
	_update_season(season)

func _update_all() -> void:
	if not _sim:
		return

	day_value.text = str(_sim.current_day)
	_update_season(_sim._season_name(_sim.current_day))

	balance_value.text = _sim.format_nok(_sim.balance)
	balance_value.add_theme_color_override("font_color",
		C_SUCCESS if _sim.balance >= 0 else C_DANGER)

	fish_value.text = _sim.format_fish(_sim.fish_count)
	fish_value.add_theme_color_override("font_color",
		C_TEXT if _sim.fish_count > 0 else C_WARNING)

	temp_value.text = "%.1f°C" % _sim.temperature_c
	temp_value.add_theme_color_override("font_color", _temp_color(_sim.temperature_c))

	price_value.text = "%.0f kr/kg" % _sim.market_price_nok
	price_value.add_theme_color_override("font_color", _price_color(_sim.market_price_nok))

	var alert_count = _sim.active_alerts.size() if _sim.get("active_alerts") else 0
	alerts_value.text = str(alert_count)
	alerts_value.add_theme_color_override("font_color",
		C_DANGER if alert_count > 0 else C_NEUTRAL)

	weather_value.text = _sim.get_current_weather_state()

func _update_season(season: String) -> void:
	season_icon.text = SEASON_ICONS.get(season, "?")
	season_value.text = season

# ── Color logic ────────────────────────────────────────────────────────────────

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
	speed_1x.button_pressed = (not _is_paused and _current_speed == 1.0)
	speed_2x.button_pressed = (not _is_paused and _current_speed == 2.0)
	speed_5x.button_pressed = (not _is_paused and _current_speed == 5.0)
	pause_btn.text = "▶️" if _is_paused else "⏸️"
	pause_btn.tooltip_text = "Resume" if _is_paused else "Pause"
