extends Control

## Dashboard v2.0 — Nordic Minimalist / Clean-Tech
## Matches the HTML prototype: KPI strip, pen grid, water quality,
## weather, finance, alerts, and quick actions.
## Builds the entire UI programmatically from LakskongenTheme v2.0 tokens.

# ── Autoload refs ─────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme: Node = null
var _juice: Node = null

# ── UI refs (created in _build_ui) ────────────────────────────────────────────
var _kpi_biomass_value: Label
var _kpi_biomass_change: Label
var _kpi_revenue_value: Label
var _kpi_revenue_change: Label
var _kpi_health_value: Label
var _kpi_health_change: Label
var _kpi_fcr_value: Label
var _kpi_fcr_change: Label

var _pen_cards: Array[Dictionary] = []
var _water_items: Dictionary = {}
var _weather_temp: Label
var _weather_desc: Label
var _weather_icon: Label
var _weather_details: Dictionary = {}
var _alerts_container: VBoxContainer

var _fin_bars_revenue: Array[Control] = []
var _fin_bars_cost: Array[Control] = []

# ── Previous values for animations ────────────────────────────────────────────
var _prev_balance: float = 0.0
var _prev_price: float = 0.0

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	# Grab autoloads
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")
	_juice = get_node_or_null("/root/JuiceEffects")

	if _sim:
		if _sim.has_signal("day_advanced"):
			_sim.day_advanced.connect(_on_day_advanced)
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)

	_build_ui()
	_refresh_all()
	print("Dashboard v2.0: ready (Nordic Minimalist)")

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Signal handlers ───────────────────────────────────────────────────────────

func _on_day_advanced(_day: int, _season: String) -> void:
	_refresh_all()

func _on_state_updated() -> void:
	_refresh_all()

# ══════════════════════════════════════════════════════════════════════════════
# UI CONSTRUCTION
# ══════════════════════════════════════════════════════════════════════════════

func _build_ui() -> void:
	# Clear any existing children from the .tscn
	for child in get_children():
		child.queue_free()

	# Root background
	var bg := Panel.new()
	bg.name = "Background"
	var bg_sb := StyleBoxFlat.new()
	bg_sb.bg_color = Color("#ECEFF4")
	bg.add_theme_stylebox_override("panel", bg_sb)
	bg.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	add_child(bg)

	# Main scroll
	var scroll := ScrollContainer.new()
	scroll.name = "MainScroll"
	scroll.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	scroll.offset_left = 16
	scroll.offset_top = 16
	scroll.offset_right = -16
	scroll.offset_bottom = -16
	add_child(scroll)

	var root_vbox := VBoxContainer.new()
	root_vbox.name = "RootVBox"
	root_vbox.size_flags_horizontal = SIZE_EXPAND_FILL
	root_vbox.add_theme_constant_override("separation", 16)
	scroll.add_child(root_vbox)

	# ── Title bar ──
	var title_bar := _build_title_bar()
	root_vbox.add_child(title_bar)

	# ── KPI Strip ──
	var kpi_strip := _build_kpi_strip()
	root_vbox.add_child(kpi_strip)

	# ── Row 1: Pens + Water/Weather ──
	var row1 := HBoxContainer.new()
	row1.add_theme_constant_override("separation", 16)
	root_vbox.add_child(row1)

	var pens_card := _build_pens_card()
	pens_card.size_flags_horizontal = SIZE_EXPAND_FILL
	pens_card.size_flags_stretch_ratio = 2.0
	row1.add_child(pens_card)

	var right_col := VBoxContainer.new()
	right_col.size_flags_horizontal = SIZE_EXPAND_FILL
	right_col.size_flags_stretch_ratio = 1.0
	right_col.add_theme_constant_override("separation", 16)
	row1.add_child(right_col)

	right_col.add_child(_build_water_card())
	right_col.add_child(_build_weather_card())

	# ── Row 2: Finance + Alerts + Actions ──
	var row2 := HBoxContainer.new()
	row2.add_theme_constant_override("separation", 16)
	root_vbox.add_child(row2)

	var fin_card := _build_finance_card()
	fin_card.size_flags_horizontal = SIZE_EXPAND_FILL
	row2.add_child(fin_card)

	var alerts_card := _build_alerts_card()
	alerts_card.size_flags_horizontal = SIZE_EXPAND_FILL
	row2.add_child(alerts_card)

	var actions_card := _build_actions_card()
	actions_card.size_flags_horizontal = SIZE_EXPAND_FILL
	row2.add_child(actions_card)


# ── Title Bar ─────────────────────────────────────────────────────────────────

func _build_title_bar() -> HBoxContainer:
	var hbox := HBoxContainer.new()

	var title := Label.new()
	title.text = tr("UI_DASHBOARD_TITLE") if tr("UI_DASHBOARD_TITLE") != "ui.dashboard.title" else "Fjordlaks AS — Oversikt"
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", Color("#2E3440"))
	if _theme and _theme.font_heading_bold:
		title.add_theme_font_override("font", _theme.font_heading_bold)
	title.size_flags_horizontal = SIZE_EXPAND_FILL
	hbox.add_child(title)

	return hbox


# ── KPI Strip ─────────────────────────────────────────────────────────────────

func _build_kpi_strip() -> HBoxContainer:
	var strip := HBoxContainer.new()
	strip.add_theme_constant_override("separation", 16)

	var kpi1 = _build_kpi_card("TOTAL BIOMASSE", "-- tonn", "", Color("#0A4D68"))
	_kpi_biomass_value = kpi1.value_label
	_kpi_biomass_change = kpi1.change_label
	strip.add_child(kpi1.card)

	var kpi2 = _build_kpi_card("INNTEKTER (YTD)", "-- MNOK", "", Color("#A3BE8C"))
	_kpi_revenue_value = kpi2.value_label
	_kpi_revenue_change = kpi2.change_label
	strip.add_child(kpi2.card)

	var kpi3 = _build_kpi_card("FISKEHELSE", "--%", "", Color("#88C0D0"))
	_kpi_health_value = kpi3.value_label
	_kpi_health_change = kpi3.change_label
	strip.add_child(kpi3.card)

	var kpi4 = _build_kpi_card("FÔRFAKTOR (FCR)", "--", "", Color("#5E81AC"))
	_kpi_fcr_value = kpi4.value_label
	_kpi_fcr_change = kpi4.change_label
	strip.add_child(kpi4.card)

	return strip

func _build_kpi_card(title: String, initial_value: String, change_text: String, stripe_color: Color) -> Dictionary:
	var card := PanelContainer.new()
	card.size_flags_horizontal = SIZE_EXPAND_FILL

	var sb := StyleBoxFlat.new()
	sb.bg_color = Color.WHITE
	sb.border_color = Color("#E5E9F0")
	sb.set_border_width_all(1)
	sb.border_width_top = 4
	sb.border_color = stripe_color  # Note: Godot uses single border_color for all sides
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(16)
	# Workaround: use a more specific approach for top stripe
	card.add_theme_stylebox_override("panel", sb)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	card.add_child(vbox)

	var lbl := Label.new()
	lbl.text = title
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.add_theme_color_override("font_color", Color("#7B8794"))
	vbox.add_child(lbl)

	var value_lbl := Label.new()
	value_lbl.text = initial_value
	value_lbl.add_theme_font_size_override("font_size", 28)
	value_lbl.add_theme_color_override("font_color", Color("#2E3440"))
	if _theme and _theme.font_heading_bold:
		value_lbl.add_theme_font_override("font", _theme.font_heading_bold)
	vbox.add_child(value_lbl)

	var change_lbl := Label.new()
	change_lbl.text = change_text
	change_lbl.add_theme_font_size_override("font_size", 12)
	change_lbl.add_theme_color_override("font_color", Color("#8FAD78"))
	vbox.add_child(change_lbl)

	return {"card": card, "value_label": value_lbl, "change_label": change_lbl}


# ── Pens Card ─────────────────────────────────────────────────────────────────

func _build_pens_card() -> PanelContainer:
	var card := _make_card("🏗️ Merdoversikt")

	var grid := GridContainer.new()
	grid.columns = 3
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 12)
	card.get_child(0).get_child(1).add_child(grid)  # VBox -> Body container

	# Build 6 pen mini-cards
	for i in range(6):
		var pen_data := _build_pen_mini_card(i + 1)
		grid.add_child(pen_data.panel)
		_pen_cards.append(pen_data)

	return card

func _build_pen_mini_card(pen_num: int) -> Dictionary:
	var panel := PanelContainer.new()
	panel.size_flags_horizontal = SIZE_EXPAND_FILL
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color("#F8F9FB")
	sb.border_color = Color("#D8DEE9")
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(12)
	panel.add_theme_stylebox_override("panel", sb)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 6)
	panel.add_child(vbox)

	# Header row
	var header := HBoxContainer.new()
	vbox.add_child(header)
	var name_lbl := Label.new()
	name_lbl.text = tr("UI_DASHBOARD_PEN_NAME") % pen_num if tr("UI_DASHBOARD_PEN_NAME") != "ui.dashboard.pen_name" else "Merd %d" % pen_num
	name_lbl.add_theme_font_size_override("font_size", 14)
	name_lbl.add_theme_color_override("font_color", Color("#2E3440"))
	if _theme and _theme.font_heading_semibold:
		name_lbl.add_theme_font_override("font", _theme.font_heading_semibold)
	name_lbl.size_flags_horizontal = SIZE_EXPAND_FILL
	header.add_child(name_lbl)

	var status_dot := ColorRect.new()
	status_dot.custom_minimum_size = Vector2(10, 10)
	status_dot.color = Color("#A3BE8C")  # Default healthy
	header.add_child(status_dot)

	# Stats
	var count_row := _make_pen_stat("Antall", "--")
	vbox.add_child(count_row.container)
	var weight_row := _make_pen_stat("Snitt vekt", "--")
	vbox.add_child(weight_row.container)
	var temp_row := _make_pen_stat("Temp", "--")
	vbox.add_child(temp_row.container)

	return {
		"panel": panel,
		"status_dot": status_dot,
		"count_value": count_row.value,
		"weight_value": weight_row.value,
		"temp_value": temp_row.value
	}

func _make_pen_stat(label_text: String, value_text: String) -> Dictionary:
	var hbox := HBoxContainer.new()
	var lbl := Label.new()
	lbl.text = label_text
	lbl.add_theme_font_size_override("font_size", 12)
	lbl.add_theme_color_override("font_color", Color("#7B8794"))
	lbl.size_flags_horizontal = SIZE_EXPAND_FILL
	hbox.add_child(lbl)

	var val := Label.new()
	val.text = value_text
	val.add_theme_font_size_override("font_size", 12)
	val.add_theme_color_override("font_color", Color("#2E3440"))
	if _theme and _theme.font_mono:
		val.add_theme_font_override("font", _theme.font_mono)
	hbox.add_child(val)

	return {"container": hbox, "value": val}


# ── Water Quality Card ────────────────────────────────────────────────────────

func _build_water_card() -> PanelContainer:
	var card := _make_card("💧 Vannkvalitet")
	var body = card.get_child(0).get_child(1)

	var grid := GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 12)
	body.add_child(grid)

	_water_items["temp"] = _build_water_item(grid, "TEMPERATUR", "--°C")
	_water_items["oxygen"] = _build_water_item(grid, "OKSYGEN", "--%")
	_water_items["salinity"] = _build_water_item(grid, "SALINITET", "--‰")
	_water_items["ph"] = _build_water_item(grid, "PH", "--")

	return card

func _build_water_item(parent: Control, title: String, initial_value: String) -> Label:
	var panel := PanelContainer.new()
	panel.size_flags_horizontal = SIZE_EXPAND_FILL
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color("#F8F9FB")
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(12)
	panel.add_theme_stylebox_override("panel", sb)
	parent.add_child(panel)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	panel.add_child(vbox)

	var lbl := Label.new()
	lbl.text = title
	lbl.add_theme_font_size_override("font_size", 10)
	lbl.add_theme_color_override("font_color", Color("#7B8794"))
	lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(lbl)

	var val := Label.new()
	val.text = initial_value
	val.add_theme_font_size_override("font_size", 20)
	val.add_theme_color_override("font_color", Color("#2E3440"))
	val.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	if _theme and _theme.font_mono:
		val.add_theme_font_override("font", _theme.font_mono)
	vbox.add_child(val)

	return val


# ── Weather Card ──────────────────────────────────────────────────────────────

func _build_weather_card() -> PanelContainer:
	var card := _make_card("🌤️ Vær")
	var body = card.get_child(0).get_child(1)

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 16)
	body.add_child(hbox)

	_weather_icon = Label.new()
	_weather_icon.text = tr("⛅")
	_weather_icon.add_theme_font_size_override("font_size", 36)
	hbox.add_child(_weather_icon)

	var info_vbox := VBoxContainer.new()
	hbox.add_child(info_vbox)

	_weather_temp = Label.new()
	_weather_temp.text = tr("--°C")
	_weather_temp.add_theme_font_size_override("font_size", 28)
	_weather_temp.add_theme_color_override("font_color", Color("#2E3440"))
	if _theme and _theme.font_heading_bold:
		_weather_temp.add_theme_font_override("font", _theme.font_heading_bold)
	info_vbox.add_child(_weather_temp)

	_weather_desc = Label.new()
	_weather_desc.text = "--"
	_weather_desc.add_theme_font_size_override("font_size", 13)
	_weather_desc.add_theme_color_override("font_color", Color("#4C566A"))
	info_vbox.add_child(_weather_desc)

	# Details grid
	var details_grid := GridContainer.new()
	details_grid.columns = 2
	details_grid.add_theme_constant_override("h_separation", 16)
	details_grid.add_theme_constant_override("v_separation", 4)
	body.add_child(details_grid)

	_weather_details["wind"] = _add_weather_detail(details_grid, "Vind:", "--")
	_weather_details["wave"] = _add_weather_detail(details_grid, "Bølge:", "--")
	_weather_details["visibility"] = _add_weather_detail(details_grid, "Sikt:", "--")
	_weather_details["humidity"] = _add_weather_detail(details_grid, "Fukt:", "--")

	return card

func _add_weather_detail(parent: Control, title: String, value: String) -> Label:
	var lbl := Label.new()
	lbl.text = title
	lbl.add_theme_font_size_override("font_size", 12)
	lbl.add_theme_color_override("font_color", Color("#4C566A"))
	parent.add_child(lbl)

	var val := Label.new()
	val.text = value
	val.add_theme_font_size_override("font_size", 12)
	val.add_theme_color_override("font_color", Color("#2E3440"))
	parent.add_child(val)

	return val


# ── Finance Card ──────────────────────────────────────────────────────────────

func _build_finance_card() -> PanelContainer:
	var card := _make_card("💰 Økonomi (6 mnd)")
	var body = card.get_child(0).get_child(1)

	var chart_hbox := HBoxContainer.new()
	chart_hbox.custom_minimum_size.y = 120
	chart_hbox.add_theme_constant_override("separation", 8)
	chart_hbox.alignment = BoxContainer.ALIGNMENT_END
	body.add_child(chart_hbox)

	_fin_bars_revenue.clear()
	_fin_bars_cost.clear()

	var months := ["Okt", "Nov", "Des", "Jan", "Feb", "Mar"]
	for i in range(6):
		var month_vbox := VBoxContainer.new()
		month_vbox.size_flags_horizontal = SIZE_EXPAND_FILL
		month_vbox.alignment = BoxContainer.ALIGNMENT_END
		chart_hbox.add_child(month_vbox)

		var bars_hbox := HBoxContainer.new()
		bars_hbox.add_theme_constant_override("separation", 2)
		bars_hbox.alignment = BoxContainer.ALIGNMENT_CENTER
		month_vbox.add_child(bars_hbox)

		var rev_bar := ColorRect.new()
		rev_bar.color = Color("#0A4D68")
		rev_bar.custom_minimum_size = Vector2(12, 30 + i * 10)
		bars_hbox.add_child(rev_bar)
		_fin_bars_revenue.append(rev_bar)

		var cost_bar := ColorRect.new()
		cost_bar.color = Color("#88C0D0")
		cost_bar.custom_minimum_size = Vector2(12, 20 + i * 5)
		bars_hbox.add_child(cost_bar)
		_fin_bars_cost.append(cost_bar)

		var month_lbl := Label.new()
		month_lbl.text = months[i]
		month_lbl.add_theme_font_size_override("font_size", 10)
		month_lbl.add_theme_color_override("font_color", Color("#7B8794"))
		month_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		month_vbox.add_child(month_lbl)

	# Legend
	var legend := HBoxContainer.new()
	legend.add_theme_constant_override("separation", 16)
	body.add_child(legend)

	var rev_legend := _make_legend_item(Color("#0A4D68"), "Inntekt")
	legend.add_child(rev_legend)
	var cost_legend := _make_legend_item(Color("#88C0D0"), "Kostnad")
	legend.add_child(cost_legend)

	return card

func _make_legend_item(color: Color, text: String) -> HBoxContainer:
	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 6)

	var dot := ColorRect.new()
	dot.custom_minimum_size = Vector2(10, 10)
	dot.color = color
	hbox.add_child(dot)

	var lbl := Label.new()
	lbl.text = text
	lbl.add_theme_font_size_override("font_size", 12)
	lbl.add_theme_color_override("font_color", Color("#4C566A"))
	hbox.add_child(lbl)

	return hbox


# ── Alerts Card ───────────────────────────────────────────────────────────────

func _build_alerts_card() -> PanelContainer:
	var card := _make_card("⚠️ Varsler")
	_alerts_container = card.get_child(0).get_child(1) as VBoxContainer
	# Will be populated in _refresh_alerts()
	return card


# ── Actions Card ──────────────────────────────────────────────────────────────

func _build_actions_card() -> PanelContainer:
	var card := _make_card("⚡ Hurtigvalg")
	var body = card.get_child(0).get_child(1)

	var grid := GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 10)
	grid.add_theme_constant_override("v_separation", 10)
	body.add_child(grid)

	var actions := [
		{"text": "🐟 Fôr alle", "color": Color("#0A4D68"), "signal": "feed_all"},
		{"text": "📦 Høst", "color": Color("#A3BE8C"), "signal": "harvest"},
		{"text": "🧪 Avlus", "color": Color("#EBCB8B"), "signal": "delouse"},
		{"text": "📊 Rapporter", "color": Color("#F8F9FB"), "signal": "reports"},
		{"text": "🔬 Forskning", "color": Color("#F8F9FB"), "signal": "research"},
		{"text": "🗺️ Kart", "color": Color("#F8F9FB"), "signal": "map"},
		{"text": "👷 Ansett", "color": Color("#F8F9FB"), "signal": "hire"},
		{"text": "📋 Kontrakter", "color": Color("#F8F9FB"), "signal": "contracts"},
	]

	for action in actions:
		var btn := Button.new()
		btn.text = action.text
		btn.size_flags_horizontal = SIZE_EXPAND_FILL
		var is_light = action.color.v > 0.9
		var btn_sb := StyleBoxFlat.new()
		btn_sb.bg_color = action.color
		btn_sb.set_corner_radius_all(4)
		btn_sb.set_content_margin_all(10)
		if is_light:
			btn_sb.border_color = Color("#D8DEE9")
			btn_sb.set_border_width_all(1)
		btn.add_theme_stylebox_override("normal", btn_sb)
		var hover_sb := btn_sb.duplicate()
		hover_sb.bg_color = action.color.darkened(0.1) if not is_light else Color("#E5E9F0")
		btn.add_theme_stylebox_override("hover", hover_sb)
		if is_light:
			btn.add_theme_color_override("font_color", Color("#2E3440"))
			btn.add_theme_color_override("font_hover_color", Color("#2E3440"))
		else:
			btn.add_theme_color_override("font_color", Color.WHITE)
			btn.add_theme_color_override("font_hover_color", Color.WHITE)
		btn.add_theme_font_size_override("font_size", 13)
		grid.add_child(btn)

	return card


# ══════════════════════════════════════════════════════════════════════════════
# CARD BUILDER HELPER
# ══════════════════════════════════════════════════════════════════════════════

func _make_card(title: String) -> PanelContainer:
	var card := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color.WHITE
	sb.border_color = Color("#E5E9F0")
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	card.add_theme_stylebox_override("panel", sb)

	var outer_vbox := VBoxContainer.new()
	outer_vbox.add_theme_constant_override("separation", 0)
	card.add_child(outer_vbox)

	# Header
	var header := PanelContainer.new()
	var header_sb := StyleBoxFlat.new()
	header_sb.bg_color = Color.WHITE
	header_sb.border_color = Color("#E5E9F0")
	header_sb.border_width_bottom = 1
	header_sb.set_content_margin(SIDE_LEFT, 20)
	header_sb.set_content_margin(SIDE_RIGHT, 20)
	header_sb.set_content_margin(SIDE_TOP, 14)
	header_sb.set_content_margin(SIDE_BOTTOM, 14)
	header.add_theme_stylebox_override("panel", header_sb)
	outer_vbox.add_child(header)

	var title_lbl := Label.new()
	title_lbl.text = title
	title_lbl.add_theme_font_size_override("font_size", 15)
	title_lbl.add_theme_color_override("font_color", Color("#2E3440"))
	if _theme and _theme.font_heading_semibold:
		title_lbl.add_theme_font_override("font", _theme.font_heading_semibold)
	header.add_child(title_lbl)

	# Body container
	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 12)
	var body_margin := MarginContainer.new()
	body_margin.add_theme_constant_override("margin_left", 20)
	body_margin.add_theme_constant_override("margin_right", 20)
	body_margin.add_theme_constant_override("margin_top", 16)
	body_margin.add_theme_constant_override("margin_bottom", 16)
	body_margin.add_child(body)
	outer_vbox.add_child(body_margin)

	# Return card, but body is nested: card -> outer_vbox -> [header, body_margin -> body]
	# Override body access: store reference on the outer vbox for easier access
	# Actually, let's keep it consistent: body is at index 1 of outer_vbox's child (margin->body)
	# Caller accesses body via: card.get_child(0).get_child(1).get_child(0)
	# This is ugly — let's restructure to be: outer_vbox has header and body directly
	
	# Remove margin approach, just add margins to body directly
	body_margin.queue_free()
	outer_vbox.remove_child(body_margin)
	
	var body2 := VBoxContainer.new()
	body2.add_theme_constant_override("separation", 12)
	outer_vbox.add_child(body2)
	
	# We return the card; caller accesses body via card.get_child(0).get_child(1)
	return card


# ══════════════════════════════════════════════════════════════════════════════
# DATA REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _refresh_all() -> void:
	_refresh_kpis()
	_refresh_pens()
	_refresh_water()
	_refresh_weather()
	_refresh_alerts()

func _refresh_kpis() -> void:
	if not _sim:
		return

	# Biomass
	var biomass_tonnes := _sim.fish_biomass_kg / 1000.0
	var biomass_fmt = tr("UI_DASHBOARD_BIOMASS_FORMAT") if tr("UI_DASHBOARD_BIOMASS_FORMAT") != "ui.dashboard.biomass_format" else "%s tonn"
	_kpi_biomass_value.text = biomass_fmt % _format_number(biomass_tonnes)

	# Revenue
	var revenue := _sim.get_revenue()
	var revenue_m := revenue / 1_000_000.0
	var revenue_fmt = tr("UI_DASHBOARD_REVENUE_FORMAT") if tr("UI_DASHBOARD_REVENUE_FORMAT") != "ui.dashboard.revenue_format" else "%.1f MNOK"
	_kpi_revenue_value.text = revenue_fmt % revenue_m

	# Health (use sustainability score as proxy)
	var health := _sim.sustainability_score
	_kpi_health_value.text = tr("%d%%") % health
	if health >= 90:
		_kpi_health_change.text = tr("UI_DASHBOARD_HEALTH_GOOD") if tr("UI_DASHBOARD_HEALTH_GOOD") != "ui.dashboard.health_good" else "▲ God status"
		_kpi_health_change.add_theme_color_override("font_color", Color("#8FAD78"))
	elif health >= 70:
		_kpi_health_change.text = tr("UI_DASHBOARD_HEALTH_STABLE") if tr("UI_DASHBOARD_HEALTH_STABLE") != "ui.dashboard.health_stable" else "─ Stabil"
		_kpi_health_change.add_theme_color_override("font_color", Color("#D4B06A"))
	else:
		_kpi_health_change.text = tr("UI_DASHBOARD_HEALTH_LOW") if tr("UI_DASHBOARD_HEALTH_LOW") != "ui.dashboard.health_low" else "▼ Lav"
		_kpi_health_change.add_theme_color_override("font_color", Color("#A54E56"))

	# FCR
	# Mock - real FCR would come from feed/growth tracking
	_kpi_fcr_value.text = tr("1.12")
	_kpi_fcr_change.text = tr("UI_DASHBOARD_FCR_BETTER") if tr("UI_DASHBOARD_FCR_BETTER") != "ui.dashboard.fcr_better" else "▲ Bedre enn mål (1.15)"

func _refresh_pens() -> void:
	if not _sim:
		return

	var site_count := _sim.site_count
	var fish_per_pen := _sim.fish_count / max(site_count, 1)
	var avg_weight := _sim.fish_biomass_kg / max(_sim.fish_count, 1)
	var temp := _sim.temperature_c

	for i in range(min(_pen_cards.size(), 6)):
		var pen = _pen_cards[i]
		if i < site_count:
			# Distribute fish somewhat randomly per pen
			var variation := 0.85 + randf() * 0.3
			pen.count_value.text = _format_number(int(fish_per_pen * variation))
			pen.weight_value.text = tr("%.1f kg") % (avg_weight * (0.9 + randf() * 0.2))
			pen.temp_value.text = "%.1f°C" % (temp + randf() * 0.5 - 0.25)
			pen.status_dot.color = Color("#A3BE8C")  # Healthy
		else:
count_value.text = tr("Tom")
			pen.weight_value.text = "--"
			pen.temp_value.text = "--"
			pen.status_dot.color = Color("#D8DEE9")

func _refresh_water() -> void:
	if not _sim:
		return

	var temp := _sim.temperature_c
	_water_items["temp"].text = "%.1f°C" % temp
	_water_items["oxygen"].text = "92%"  # Mock
	_water_items["salinity"].text = "33‰"  # Mock
	_water_items["ph"].text = "7.8"  # Mock

func _refresh_weather() -> void:
	if not _sim:
		return

	var temp := _sim.temperature_c
	_weather_temp.text = "%d°C" % int(temp)

	var weather_state = _sim.get_current_weather_state()
	match weather_state:
		"Calm":
			_weather_icon.text = tr("☀️")
			_weather_desc.text = tr("Klart")
		"Windy":
			_weather_icon.text = tr("💨")
			_weather_desc.text = tr("Vind")
		"Storm":
			_weather_icon.text = tr("⛈️")
			_weather_desc.text = tr("Storm")
		_:
			_weather_icon.text = tr("⛅")
			_weather_desc.text = tr("Delvis skyet")

func _refresh_alerts() -> void:
	if not _alerts_container:
		return

	# Clear existing alerts
	for child in _alerts_container.get_children():
		child.queue_free()

	# Mock alerts — in production, pull from EventBus or SimBridge
	var alerts := [
		{"text": "Lusetall stigende — nærmer seg grenseverdi", "level": "warning", "time": "12 min siden"},
		{"text": "Oksygennivå lavt i Merd 4 — vurder tiltak", "level": "danger", "time": "43 min siden"},
		{"text": "Kontraktsfrist: Nordsjø Sjømat innen 28. mar", "level": "info", "time": "2 timer siden"},
	]

	for alert in alerts:
		var hbox := HBoxContainer.new()
		hbox.add_theme_constant_override("separation", 12)
		_alerts_container.add_child(hbox)

		var dot := ColorRect.new()
		dot.custom_minimum_size = Vector2(8, 8)
		match alert.level:
			"warning": dot.color = Color("#EBCB8B")
			"danger": dot.color = Color("#BF616A")
			"info": dot.color = Color("#88C0D0")
			_: dot.color = Color("#7B8794")
		hbox.add_child(dot)

		var text_vbox := VBoxContainer.new()
		text_vbox.add_theme_constant_override("separation", 2)
		hbox.add_child(text_vbox)

		var text_lbl := Label.new()
		text_lbl.text = alert.text
		text_lbl.add_theme_font_size_override("font_size", 13)
		text_lbl.add_theme_color_override("font_color", Color("#2E3440"))
		text_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		text_vbox.add_child(text_lbl)

		var time_lbl := Label.new()
		time_lbl.text = alert.time
		time_lbl.add_theme_font_size_override("font_size", 11)
		time_lbl.add_theme_color_override("font_color", Color("#7B8794"))
		text_vbox.add_child(time_lbl)


# ══════════════════════════════════════════════════════════════════════════════
# UTILITIES
# ══════════════════════════════════════════════════════════════════════════════

func _format_number(n: int) -> String:
	var s := str(abs(n))
	var result := ""
	var count := 0
	for i in range(s.length() - 1, -1, -1):
		if count > 0 and count % 3 == 0:
			result = " " + result
		result = s[i] + result
		count += 1
	if n < 0:
		result = "-" + result
	return result
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
    if event.is_action_pressed("ui_cancel"):
        get_tree().call_group_flags(2, "tooltips", "hide")
