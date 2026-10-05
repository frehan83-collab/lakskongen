## ContractsPanel v2.0 — Nordic Minimalist
## Market & trading: spot/contract prices, buyers, contracts table,
## regional markets, and seasonal indicator.
## Matches market-prototype.html.

extends Control

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme: Node = null

# ── UI Refs ───────────────────────────────────────────────────────────────────
var _kpi_spot: Label
var _kpi_contract: Label
var _kpi_volume: Label
var _kpi_open: Label

var _price_chart_bars: Array[ColorRect] = []
var _price_summary_lo: Label
var _price_summary_avg: Label
var _price_summary_hi: Label
var _price_summary_trend: Label

var _buyers_vbox: VBoxContainer
var _contracts_vbox: VBoxContainer
var _regions_grid: GridContainer

# ── Colors ────────────────────────────────────────────────────────────────────
const C_BG       := Color("#ECEFF4")
const C_SURFACE  := Color("#FFFFFF")
const C_ELEVATED := Color("#F8F9FB")
const C_TEXT     := Color("#2E3440")
const C_TEXT2    := Color("#4C566A")
const C_MUTED    := Color("#7B8794")
const C_BORDER   := Color("#E5E9F0")
const C_PRIMARY  := Color("#0A4D68")
const C_SECONDARY := Color("#88C0D0")
const C_SUCCESS  := Color("#A3BE8C")
const C_SUCCESS_D := Color("#8FAD78")
const C_WARNING  := Color("#EBCB8B")
const C_WARNING_D := Color("#D4B06A")
const C_DANGER   := Color("#BF616A")
const C_ACCENT   := Color("#5E81AC")
const C_INFO     := Color("#88C0D0")

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")

	if _sim:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_refresh)

	_build_ui()
	_refresh()
	print("ContractsPanel v2.0: ready")

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ══════════════════════════════════════════════════════════════════════════════
# UI BUILD
# ══════════════════════════════════════════════════════════════════════════════

func _build_ui() -> void:
	for child in get_children():
		child.queue_free()

	var bg := Panel.new()
	var bg_sb := StyleBoxFlat.new()
	bg_sb.bg_color = C_BG
	bg.add_theme_stylebox_override("panel", bg_sb)
	bg.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	add_child(bg)

	var scroll := ScrollContainer.new()
	scroll.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	scroll.offset_left = 16; scroll.offset_top = 16
	scroll.offset_right = -16; scroll.offset_bottom = -16
	add_child(scroll)

	var root := VBoxContainer.new()
	root.size_flags_horizontal = SIZE_EXPAND_FILL
	root.add_theme_constant_override("separation", 20)
	scroll.add_child(root)

	# ── Title + Buttons ──
	var header := HBoxContainer.new()
	root.add_child(header)
	var title := Label.new()
	title.text = tr("📈 Marked & Handel")
	title.add_theme_font_size_override("font_size", 24)
	title.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_bold:
		title.add_theme_font_override("font", _theme.font_heading_bold)
	title.size_flags_horizontal = SIZE_EXPAND_FILL
	header.add_child(title)

	var btn_new := _make_btn("📝 Ny kontrakt", C_PRIMARY)
	header.add_child(btn_new)
	var btn_spot := _make_btn("📦 Selg spot", C_ELEVATED, true)
	header.add_child(btn_spot)

	# ── KPI Strip ──
	var kpi_strip := HBoxContainer.new()
	kpi_strip.add_theme_constant_override("separation", 16)
	root.add_child(kpi_strip)

	var k1 := _make_kpi("SPOTPRIS (NASDAQ)", "--", C_SUCCESS)
	_kpi_spot = k1.value; kpi_strip.add_child(k1.card)
	var k2 := _make_kpi("KONTRAKTPRIS (SNITT)", "--", C_DANGER)
	_kpi_contract = k2.value; kpi_strip.add_child(k2.card)
	var k3 := _make_kpi("TILGJENGELIG VOLUM", "--", C_ACCENT)
	_kpi_volume = k3.value; kpi_strip.add_child(k3.card)
	var k4 := _make_kpi("ÅPNE KONTRAKTER", "--", C_WARNING)
	_kpi_open = k4.value; kpi_strip.add_child(k4.card)

	# ── Mid: Price chart + Buyers ──
	var mid := HBoxContainer.new()
	mid.add_theme_constant_override("separation", 16)
	root.add_child(mid)

	var chart_card := _make_card("💹 Prisutvikling (12 uker)")
	chart_card.size_flags_horizontal = SIZE_EXPAND_FILL
	chart_card.size_flags_stretch_ratio = 2.0
	mid.add_child(chart_card)
	_build_price_chart(_card_body(chart_card))

	var buyers_card := _make_card("🏢 Kjøpere")
	buyers_card.size_flags_horizontal = SIZE_EXPAND_FILL
	mid.add_child(buyers_card)
	_buyers_vbox = _card_body(buyers_card)

	# ── Bottom: Contracts table + Regions ──
	var bot := HBoxContainer.new()
	bot.add_theme_constant_override("separation", 16)
	root.add_child(bot)

	var contracts_card := _make_card("📋 Kontrakter")
	contracts_card.size_flags_horizontal = SIZE_EXPAND_FILL
	bot.add_child(contracts_card)
	_contracts_vbox = _card_body(contracts_card)

	var regions_card := _make_card("🌍 Markedsregioner")
	regions_card.size_flags_horizontal = SIZE_EXPAND_FILL
	bot.add_child(regions_card)
	var regions_body := _card_body(regions_card)
	_regions_grid = GridContainer.new()
	_regions_grid.columns = 2
	_regions_grid.add_theme_constant_override("h_separation", 10)
	_regions_grid.add_theme_constant_override("v_separation", 10)
	regions_body.add_child(_regions_grid)

# ── Price Chart ───────────────────────────────────────────────────────────────

func _build_price_chart(body: VBoxContainer) -> void:
	var chart := HBoxContainer.new()
	chart.custom_minimum_size.y = 140
	chart.add_theme_constant_override("separation", 4)
	chart.alignment = BoxContainer.ALIGNMENT_END
	body.add_child(chart)

	_price_chart_bars.clear()
	# 12 bars
	var heights := [55, 52, 58, 60, 54, 48, 45, 50, 62, 68, 72, 82]
	for i in range(12):
		var bar := ColorRect.new()
		bar.size_flags_horizontal = SIZE_EXPAND_FILL
		var h_pct: float = float(heights[i])
		bar.custom_minimum_size = Vector2(0, h_pct * 1.5)
		bar.color = C_SUCCESS if h_pct > 65 else (C_DANGER if h_pct < 48 else C_SECONDARY)
		chart.add_child(bar)
		_price_chart_bars.append(bar)

	# Labels
	var labels := HBoxContainer.new()
	labels.add_theme_constant_override("separation", 4)
	body.add_child(labels)
	for i in range(12):
		var l := Label.new()
		l.text = tr("U%d") % (i + 1)
		l.add_theme_font_size_override("font_size", 9)
		l.add_theme_color_override("font_color", C_MUTED)
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		l.size_flags_horizontal = SIZE_EXPAND_FILL
		labels.add_child(l)

	# Summary row
	var summary := HBoxContainer.new()
	summary.add_theme_constant_override("separation", 24)
	body.add_child(summary)

	_price_summary_lo = _add_summary_item(summary, "LAV (12U)", "--")
	_price_summary_avg = _add_summary_item(summary, "SNITT (12U)", "--")
	_price_summary_hi = _add_summary_item(summary, "HØY (12U)", "--")
	_price_summary_trend = _add_summary_item(summary, "TREND", "--")

func _add_summary_item(parent: HBoxContainer, title: String, value: String) -> Label:
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 2)
	parent.add_child(vbox)

	var lbl := Label.new()
	lbl.text = title
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.add_theme_color_override("font_color", C_MUTED)
	lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(lbl)

	var val := Label.new()
	val.text = value
	val.add_theme_font_size_override("font_size", 16)
	val.add_theme_color_override("font_color", C_TEXT)
	val.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	if _theme and _theme.font_mono:
		val.add_theme_font_override("font", _theme.font_mono)
	vbox.add_child(val)

	return val

# ══════════════════════════════════════════════════════════════════════════════
# DATA REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _refresh() -> void:
	if not _sim:
		return

	var spot: float = float(_sim.market_price_nok) if _sim else 75.0
	var weekly: float = float(_sim.weekly_avg_price_nok) if _sim else 75.0

	_kpi_spot.text = tr("%.2f NOK/kg") % spot
	_kpi_contract.text = tr("%.2f NOK/kg") % (weekly * 0.93)  # Contract ~7% below spot
	_kpi_volume.text = tr("%.0f tonn") % (_sim.fish_biomass_kg / 1000.0 * 0.2)  # 20% harvestable
	_kpi_open.text = "4"  # Mock

	# Price summary
	_price_summary_lo.text = "%.2f" % (spot * 0.83)
	_price_summary_lo.add_theme_color_override("font_color", C_DANGER)
	_price_summary_avg.text = "%.2f" % (spot * 0.91)
	_price_summary_hi.text = "%.2f" % spot
	_price_summary_hi.add_theme_color_override("font_color", C_SUCCESS_D)
	_price_summary_trend.text = tr("▲ Stigende")
	_price_summary_trend.add_theme_color_override("font_color", C_SUCCESS_D if spot > weekly else C_DANGER)

	_refresh_buyers()
	_refresh_contracts()
	_refresh_regions()

func _refresh_buyers() -> void:
	if not _buyers_vbox:
		return
	for child in _buyers_vbox.get_children():
		child.queue_free()

	var spot: float = float(_sim.market_price_nok) if _sim else 75.0
	var buyers := [
		{"name": "Nordsjø Sjømat", "type": "Fast kontrakt · EU", "price": spot * 0.95, "vol": "200 t/mnd"},
		{"name": "Marine Harvest Trading", "type": "Spot · Asia", "price": spot, "vol": "Spot"},
		{"name": "Bergen Fish Co", "type": "Fast kontrakt · UK", "price": spot * 0.91, "vol": "150 t/mnd"},
		{"name": "SalMar Direct", "type": "Forespørsel · Ny", "price": spot * 0.98, "vol": "100 t/mnd"},
	]

	for buyer in buyers:
		var row := PanelContainer.new()
		row.size_flags_horizontal = SIZE_EXPAND_FILL
		var sb := StyleBoxFlat.new()
		sb.bg_color = C_ELEVATED
		sb.border_color = C_BORDER
		sb.set_border_width_all(1)
		sb.set_corner_radius_all(4)
		sb.set_content_margin_all(10)
		row.add_theme_stylebox_override("panel", sb)
		_buyers_vbox.add_child(row)

		var hbox := HBoxContainer.new()
		row.add_child(hbox)

		var left := VBoxContainer.new()
		left.size_flags_horizontal = SIZE_EXPAND_FILL
		hbox.add_child(left)

		var name_lbl := Label.new()
		name_lbl.text = buyer.name
		name_lbl.add_theme_font_size_override("font_size", 14)
		name_lbl.add_theme_color_override("font_color", C_TEXT)
		if _theme and _theme.font_heading_semibold:
			name_lbl.add_theme_font_override("font", _theme.font_heading_semibold)
		left.add_child(name_lbl)

		var type_lbl := Label.new()
		type_lbl.text = buyer.type
		type_lbl.add_theme_font_size_override("font_size", 12)
		type_lbl.add_theme_color_override("font_color", C_MUTED)
		left.add_child(type_lbl)

		var right := VBoxContainer.new()
		hbox.add_child(right)

		var price_lbl := Label.new()
		price_lbl.text = "%.2f" % buyer.price
		price_lbl.add_theme_font_size_override("font_size", 16)
		price_lbl.add_theme_color_override("font_color", C_PRIMARY)
		if _theme and _theme.font_mono:
			price_lbl.add_theme_font_override("font", _theme.font_mono)
		price_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		right.add_child(price_lbl)

		var vol_lbl := Label.new()
		vol_lbl.text = buyer.vol
		vol_lbl.add_theme_font_size_override("font_size", 11)
		vol_lbl.add_theme_color_override("font_color", C_MUTED)
		vol_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		right.add_child(vol_lbl)

func _refresh_contracts() -> void:
	if not _contracts_vbox:
		return
	for child in _contracts_vbox.get_children():
		child.queue_free()

	var spot: float = float(_sim.market_price_nok) if _sim else 75.0
	var contracts := [
		{"buyer": "Nordsjø Sjømat", "vol": "80 t", "price": spot * 0.95, "deadline": "28. mar", "status": "Aktiv"},
		{"buyer": "Bergen Fish Co", "vol": "60 t", "price": spot * 0.91, "deadline": "5. apr", "status": "Aktiv"},
		{"buyer": "SalMar Direct", "vol": "100 t", "price": spot * 0.98, "deadline": "15. apr", "status": "Tilbud"},
		{"buyer": "EuroFish GmbH", "vol": "120 t", "price": spot * 0.86, "deadline": "1. mar", "status": "Utløpt"},
	]

	# Table header
	var hdr := HBoxContainer.new()
	hdr.add_theme_constant_override("separation", 8)
	_contracts_vbox.add_child(hdr)
	for col in ["Kjøper", "Volum", "Pris", "Frist", "Status"]:
		var l := Label.new()
		l.text = col
		l.add_theme_font_size_override("font_size", 11)
		l.add_theme_color_override("font_color", C_MUTED)
		l.size_flags_horizontal = SIZE_EXPAND_FILL
		hdr.add_child(l)

	for c in contracts:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_contracts_vbox.add_child(row)

		_add_table_cell(row, c.buyer, C_TEXT, true)
		_add_table_cell(row, c.vol, C_TEXT)
		_add_table_cell(row, "%.2f" % c.price, C_TEXT, false, true)
		_add_table_cell(row, c.deadline, C_TEXT)

		var status_color: Color
		match c.status:
			"Aktiv": status_color = C_SUCCESS_D
			"Tilbud": status_color = C_WARNING_D
			"Utløpt": status_color = C_DANGER
			_: status_color = C_MUTED
		_add_table_cell(row, c.status, status_color)

func _add_table_cell(row: HBoxContainer, text: String, color: Color, bold: bool = false, mono: bool = false) -> void:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", 13)
	l.add_theme_color_override("font_color", color)
	l.size_flags_horizontal = SIZE_EXPAND_FILL
	if mono and _theme and _theme.font_mono:
		l.add_theme_font_override("font", _theme.font_mono)
	if bold and _theme and _theme.font_heading_semibold:
		l.add_theme_font_override("font", _theme.font_heading_semibold)
	row.add_child(l)

func _refresh_regions() -> void:
	if not _regions_grid:
		return
	for child in _regions_grid.get_children():
		child.queue_free()

	var spot: float = float(_sim.market_price_nok) if _sim else 75.0
	var regions := [
		{"flag": "🇪🇺", "name": "EU (Frankrike/Spania)", "price": "€%.2f/kg" % (spot * 0.09), "demand": "Høy", "level": "high"},
		{"flag": "🇬🇧", "name": "Storbritannia", "price": "£%.2f/kg" % (spot * 0.085), "demand": "Middels", "level": "medium"},
		{"flag": "🇯🇵", "name": "Japan", "price": "¥%.0f/kg" % (spot * 15.8), "demand": "Høy", "level": "high"},
		{"flag": "🇺🇸", "name": "USA", "price": "$%.2f/kg" % (spot * 0.099), "demand": "Middels", "level": "medium"},
	]

	for r in regions:
		var panel := PanelContainer.new()
		panel.size_flags_horizontal = SIZE_EXPAND_FILL
		var sb := StyleBoxFlat.new()
		sb.bg_color = C_ELEVATED
		sb.border_color = C_BORDER
		sb.set_border_width_all(1)
		sb.set_corner_radius_all(4)
		sb.set_content_margin_all(12)
		panel.add_theme_stylebox_override("panel", sb)
		_regions_grid.add_child(panel)

		var vbox := VBoxContainer.new()
		vbox.add_theme_constant_override("separation", 4)
		panel.add_child(vbox)

		var flag := Label.new()
		flag.text = r.flag
		flag.add_theme_font_size_override("font_size", 20)
		vbox.add_child(flag)

		var name_lbl := Label.new()
		name_lbl.text = r.name
		name_lbl.add_theme_font_size_override("font_size", 13)
		name_lbl.add_theme_color_override("font_color", C_TEXT)
		if _theme and _theme.font_heading_semibold:
			name_lbl.add_theme_font_override("font", _theme.font_heading_semibold)
		vbox.add_child(name_lbl)

		var price_lbl := Label.new()
		price_lbl.text = r.price
		price_lbl.add_theme_font_size_override("font_size", 15)
		price_lbl.add_theme_color_override("font_color", C_TEXT)
		if _theme and _theme.font_mono:
			price_lbl.add_theme_font_override("font", _theme.font_mono)
		vbox.add_child(price_lbl)

		var demand := Label.new()
		demand.text = "● %s etterspørsel" % r.demand
		demand.add_theme_font_size_override("font_size", 11)
		match r.level:
			"high": demand.add_theme_color_override("font_color", C_SUCCESS_D)
			"medium": demand.add_theme_color_override("font_color", C_WARNING_D)
			_: demand.add_theme_color_override("font_color", C_DANGER)
		vbox.add_child(demand)

# ══════════════════════════════════════════════════════════════════════════════
# HELPERS
# ══════════════════════════════════════════════════════════════════════════════

func _make_kpi(title: String, initial: String, stripe: Color) -> Dictionary:
	var card := PanelContainer.new()
	card.size_flags_horizontal = SIZE_EXPAND_FILL
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = stripe
	sb.set_border_width_all(1)
	sb.border_width_top = 4
	sb.set_corner_radius_all(8)
	sb.set_content_margin_all(16)
	card.add_theme_stylebox_override("panel", sb)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	card.add_child(vbox)

	var lbl := Label.new()
	lbl.text = title
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.add_theme_color_override("font_color", C_MUTED)
	vbox.add_child(lbl)

	var val := Label.new()
	val.text = initial
	val.add_theme_font_size_override("font_size", 22)
	val.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_bold:
		val.add_theme_font_override("font", _theme.font_heading_bold)
	vbox.add_child(val)

	return {"card": card, "value": val}

func _make_card(title: String) -> PanelContainer:
	var card := PanelContainer.new()
	var sb := StyleBoxFlat.new()
	sb.bg_color = C_SURFACE
	sb.border_color = C_BORDER
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	card.add_theme_stylebox_override("panel", sb)

	var outer := VBoxContainer.new()
	outer.add_theme_constant_override("separation", 0)
	card.add_child(outer)

	var header := PanelContainer.new()
	var h_sb := StyleBoxFlat.new()
	h_sb.bg_color = C_SURFACE
	h_sb.border_color = C_BORDER
	h_sb.border_width_bottom = 1
	h_sb.set_content_margin(SIDE_LEFT, 20)
	h_sb.set_content_margin(SIDE_RIGHT, 20)
	h_sb.set_content_margin(SIDE_TOP, 14)
	h_sb.set_content_margin(SIDE_BOTTOM, 14)
	header.add_theme_stylebox_override("panel", h_sb)
	outer.add_child(header)

	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 15)
	t.add_theme_color_override("font_color", C_TEXT)
	if _theme and _theme.font_heading_semibold:
		t.add_theme_font_override("font", _theme.font_heading_semibold)
	header.add_child(t)

	var body := VBoxContainer.new()
	body.add_theme_constant_override("separation", 10)
	outer.add_child(body)

	return card

func _card_body(card: PanelContainer) -> VBoxContainer:
	return card.get_child(0).get_child(1) as VBoxContainer

func _make_btn(text: String, color: Color, outline: bool = false) -> Button:
	var btn := Button.new()
	btn.text = text
	var sb := StyleBoxFlat.new()
	sb.bg_color = color
	sb.set_corner_radius_all(4)
	sb.set_content_margin_all(10)
	if outline:
		sb.border_color = C_BORDER
		sb.set_border_width_all(1)
	btn.add_theme_stylebox_override("normal", sb)
	btn.add_theme_color_override("font_color", Color.WHITE if not outline else C_TEXT)
	btn.add_theme_font_size_override("font_size", 13)
	return btn
