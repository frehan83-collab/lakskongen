
extends Control

## Dashboard — real-time KPI overview panel.
## Reads all data from SimBridge (GDScript autoload) and refreshes
## on every state_updated signal. Shows 6 prominent KPIs in a top bar.

# ── Autoload refs ─────────────────────────────────────────────────────────────
var _sim: Node = null
var _lakskongen_theme: Node = null # Reference to our custom theme
var _juice_effects: Node = null    # Reference to juice effects

# ── KPI Nodes ─────────────────────────────────────────────────────────────────
var _kpi_cash_balance: Dictionary = {}
var _kpi_fish_count: Dictionary = {}
var _kpi_biomass: Dictionary = {}
var _kpi_spot_price: Dictionary = {}
var _kpi_day_season: Dictionary = {}
var _kpi_esg_score: Dictionary = {}

var _prev_cash_balance: float = 0.0
var _prev_spot_price: float = 0.0
var _prev_esg_score: int = 0

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	if has_node("/root/SimBridge"):
		_sim = get_node("/root/SimBridge")
		_sim.state_updated.connect(_on_state_updated)
	else:
		push_warning("Dashboard: SimBridge not found — display will be empty")

	if has_node("/root/LakskongenTheme"):
		_lakskongen_theme = get_node("/root/LakskongenTheme")
	else:
		push_warning("Dashboard: LakskongenTheme not found — UI colors may be off")

	if has_node("/root/JuiceEffects"):
		_juice_effects = get_node("/root/JuiceEffects")
	else:
		push_warning("Dashboard: JuiceEffects not found — animations will be disabled")

	_setup_kpi_dashboard()
	_refresh_kpis()
	print("Dashboard: ready")

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Setup KPI Dashboard ───────────────────────────────────────────────────────

func _setup_kpi_dashboard() -> void:
	# Clear existing children if any (e.g. from editor)
	for child in get_children():
		child.queue_free()

	var kpi_bar = HBoxContainer.new()
	kpi_bar.name = "KpiBar"
	kpi_bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	kpi_bar.add_theme_constant_override("separation", 10) # Spacing between cards
	add_child(kpi_bar)

	_kpi_cash_balance = _create_kpi_card(kpi_bar, "res://assets/icons/coin.png", "Cash Balance")
	_kpi_fish_count   = _create_kpi_card(kpi_bar, "res://assets/icons/fish.png", "Fish Count")
	_kpi_biomass      = _create_kpi_card(kpi_bar, "res://assets/icons/weight.png", "Biomass")
	_kpi_spot_price   = _create_kpi_card(kpi_bar, "res://assets/icons/price.png", "Spot Price")
	_kpi_day_season   = _create_kpi_card(kpi_bar, "res://assets/icons/calendar.png", "Day / Season")
	_kpi_esg_score    = _create_kpi_card(kpi_bar, "res://assets/icons/leaf.png", "ESG Score") # Placeholder icon, replace with specific ESG icon if available

	# Initialize previous values
	_prev_cash_balance = _sim.balance if _sim else 0.0
	_prev_spot_price = _sim.market_price_nok if _sim else 0.0
	_prev_esg_score = _sim.sustainability_score if _sim else 0


func _create_kpi_card(parent: HBoxContainer, icon_path: String, label_text: String) -> Dictionary:
	var card = PanelContainer.new()
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	card.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	if _lakskongen_theme:
		card.add_theme_stylebox_override("panel", _lakskongen_theme.get_panel_style(_lakskongen_theme.BG_LIGHT, _lakskongen_theme.ACCENT_PRIMARY, 1, 8, 15))
	parent.add_child(card)

	var vbox = VBoxContainer.new()
	vbox.alignment = BoxContainer.ALIGNMENT_CENTER
	vbox.add_theme_constant_override("separation", 5)
	card.add_child(vbox)

	var icon = TextureRect.new()
	icon.texture = load(icon_path) if icon_path else null
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_CENTERED
	icon.custom_minimum_size = Vector2(24, 24)
	icon.modulate = _lakskongen_theme.ACCENT_SECONDARY if _lakskongen_theme else Color.WHITE
	vbox.add_child(icon)

	var value_label = Label.new()
	value_label.name = "ValueLabel"
	if _lakskongen_theme:
		value_label.add_theme_font_size_override("font_size", _lakskongen_theme.FONT_HEADER)
		value_label.add_theme_color_override("font_color", _lakskongen_theme.TEXT_PRIMARY)
	value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(value_label)

	var label = Label.new()
	label.name = "Label"
	label.text = label_text
	if _lakskongen_theme:
		label.add_theme_font_size_override("font_size", _lakskongen_theme.FONT_SMALL)
		label.add_theme_color_override("font_color", _lakskongen_theme.TEXT_SECONDARY)
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(label)

	return {"card": card, "icon": icon, "value": value_label, "label": label}

# ── Signal handlers ───────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_refresh_kpis()

# ── Full KPI refresh ──────────────────────────────────────────────────────────

func _refresh_kpis() -> void:
	if not _sim:
		_show_no_data()
		return

	_update_cash_balance_kpi()
	_update_fish_count_kpi()
	_update_biomass_kpi()
	_update_spot_price_kpi()
	_update_day_season_kpi()
	_update_esg_score_kpi()

func _show_no_data() -> void:
	_kpi_cash_balance.value.text = "--"
	_kpi_fish_count.value.text = "--"
	_kpi_biomass.value.text = "--"
	_kpi_spot_price.value.text = "--"
	_kpi_day_season.value.text = "--"
	_kpi_esg_score.value.text = "--"

# ── KPI Update Functions ──────────────────────────────────────────────────────

func _update_cash_balance_kpi() -> void:
	var current_balance = _sim.balance
	var balance_label = _kpi_cash_balance.value

	if _juice_effects and current_balance != _prev_cash_balance:
		var color = _sim.balance_color(current_balance)
		_juice_effects.number_lerp(balance_label, _prev_cash_balance, current_balance, 0.8, "NOK %s", 0)
		_juice_effects.flash_color(_kpi_cash_balance.card, color, 0.4)
		balance_label.add_theme_color_override("font_color", color) # Ensure color matches immediately
	else:
		balance_label.text = _sim.format_nok(current_balance)
		balance_label.add_theme_color_override("font_color", _sim.balance_color(current_balance))

	_prev_cash_balance = current_balance

func _update_fish_count_kpi() -> void:
	var fish_count_label = _kpi_fish_count.value
	fish_count_label.text = _sim.format_fish(_sim.fish_count)

func _update_biomass_kpi() -> void:
	var biomass_label = _kpi_biomass.value
	biomass_label.text = _sim.format_biomass(_sim.fish_biomass_kg)

func _update_spot_price_kpi() -> void:
	var current_price = _sim.market_price_nok
	var price_label = _kpi_spot_price.value

	var trend_arrow = ""
	var color = _lakskongen_theme.TEXT_PRIMARY if _lakskongen_theme else Color.WHITE

	if current_price > _prev_spot_price:
		trend_arrow = "▲ "
		color = _lakskongen_theme.SUCCESS_COLOR if _lakskongen_theme else Color.GREEN
	elif current_price < _prev_spot_price:
		trend_arrow = "▼ "
		color = _lakskongen_theme.DANGER_COLOR if _lakskongen_theme else Color.RED

	price_label.text = "%s%.2f NOK/kg" % [trend_arrow, current_price]
	price_label.add_theme_color_override("font_color", color)
	_prev_spot_price = current_price

func _update_day_season_kpi() -> void:
	var day_season_label = _kpi_day_season.value
	var day_num = _sim.current_day
	var season_name = _sim._season_name(day_num)
	var season_icon = ""

	match season_name:
		"Vinter": season_icon = "❄️"
		"Vår":    season_icon = "🌸"
		"Sommer": season_icon = "☀️"
		"Høst":   season_icon = "🍂"
		_:        season_icon = ""

	day_season_label.text = "Dag %d (%s) %s" % [day_num, season_name, season_icon]

func _update_esg_score_kpi() -> void:
	var current_score = _sim.sustainability_score
	var esg_label = _kpi_esg_score.value
	var esg_card = _kpi_esg_score.card

	var color = _lakskongen_theme.TEXT_PRIMARY if _lakskongen_theme else Color.WHITE
	if current_score > 70:
		color = _lakskongen_theme.SUCCESS_COLOR if _lakskongen_theme else Color.GREEN
	elif current_score >= 40:
		color = _lakskongen_theme.WARNING_COLOR if _lakskongen_theme else Color.YELLOW
	else:
		color = _lakskongen_theme.DANGER_COLOR if _lakskongen_theme else Color.RED

	esg_label.text = "%d / 100" % current_score
	esg_label.add_theme_color_override("font_color", color)

	# Flash card if score changes significantly
	if _juice_effects and abs(current_score - _prev_esg_score) >= 5:
		_juice_effects.flash_color(esg_card, color.lightened(0.5), 0.3)
	_prev_esg_score = current_score

# ── Helpers ───────────────────────────────────────────────────────────────────

# _get, _set_text, _set_color, _set_visible, _format_level are no longer needed
# due to direct node references and new KPI card structure.
