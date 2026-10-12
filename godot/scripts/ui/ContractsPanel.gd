## ContractsPanel v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/ContractsPanel.tscn; this script is wiring only:
## @onready % refs, localization, theme fonts, data refresh.
## Buyer / contract / region rows are data-driven and still populated in code.

extends Control

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme: Node = null

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _title_label: Label = %TitleLabel
@onready var _btn_new_contract: Button = %BtnNewContract
@onready var _btn_sell_spot: Button = %BtnSellSpot

@onready var _kpi_spot: Label = %KpiSpotValue
@onready var _kpi_contract: Label = %KpiContractValue
@onready var _kpi_volume: Label = %KpiVolumeValue
@onready var _kpi_open: Label = %KpiOpenValue

@onready var _chart_title: Label = %ChartTitle
@onready var _buyers_title: Label = %BuyersTitle
@onready var _contracts_title: Label = %ContractsTitle
@onready var _regions_title: Label = %RegionsTitle

@onready var _price_summary_lo: Label = %SumLo
@onready var _price_summary_avg: Label = %SumAvg
@onready var _price_summary_hi: Label = %SumHi
@onready var _price_summary_trend: Label = %SumTrend

@onready var _buyers_container: VBoxContainer = %BuyersContainer
@onready var _contracts_container: VBoxContainer = %ContractsContainer
@onready var _regions_grid: GridContainer = %RegionsGrid

var _price_chart_bars: Array[ColorRect] = []

# ── Colors ────────────────────────────────────────────────────────────────────
const C_BG := Color("#ECEFF4")
const C_SURFACE := Color("#FFFFFF")
const C_ELEVATED := Color("#F8F9FB")
const C_TEXT := Color("#2E3440")
const C_TEXT2 := Color("#4C566A")
const C_MUTED := Color("#7B8794")
const C_BORDER := Color("#E5E9F0")
const C_PRIMARY := Color("#0A4D68")
const C_SECONDARY := Color("#88C0D0")
const C_SUCCESS := Color("#A3BE8C")
const C_SUCCESS_D := Color("#8FAD78")
const C_WARNING := Color("#EBCB8B")
const C_WARNING_D := Color("#D4B06A")
const C_DANGER := Color("#BF616A")
const C_ACCENT := Color("#5E81AC")

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")

	if _sim:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_refresh)

	_collect_scene_refs()
	_apply_localization()
	_apply_theme_fonts()
	_refresh()

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _collect_scene_refs() -> void:
	_price_chart_bars.clear()
	for i in range(1, 13):
		_price_chart_bars.append(get_node_or_null("%PriceBar" + str(i)) as ColorRect)

func _apply_localization() -> void:
	_title_label.text = tr("📈 Marked & Handel")
	_btn_new_contract.text = tr("📝 Ny kontrakt")
	_btn_sell_spot.text = tr("📦 Selg spot")
	_chart_title.text = tr("💹 Prisutvikling (12 uker)")
	_buyers_title.text = tr("🏢 Kjøpere")
	_contracts_title.text = tr("📋 Kontrakter")
	_regions_title.text = tr("🌍 Markedsregioner")

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	if _theme.font_heading_bold:
		_title_label.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_spot.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_contract.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_volume.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_open.add_theme_font_override("font", _theme.font_heading_bold)
	if _theme.font_heading_semibold:
		_chart_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_buyers_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_contracts_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_regions_title.add_theme_font_override("font", _theme.font_heading_semibold)
	if _theme.font_mono:
		_price_summary_lo.add_theme_font_override("font", _theme.font_mono)
		_price_summary_avg.add_theme_font_override("font", _theme.font_mono)
		_price_summary_hi.add_theme_font_override("font", _theme.font_mono)
		_price_summary_trend.add_theme_font_override("font", _theme.font_mono)
		_kpi_spot.add_theme_font_override("font", _theme.font_mono)
		_kpi_contract.add_theme_font_override("font", _theme.font_mono)

# ══════════════════════════════════════════════════════════════════════════════
# DATA REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _refresh() -> void:
	if not _sim:
		return

	var spot: float = float(_sim.market_price_nok) if _sim else 75.0
	var weekly: float = float(_sim.weekly_avg_price_nok) if _sim else 75.0

	_kpi_spot.text = "%.2f NOK/kg" % spot
	_kpi_contract.text = "%.2f NOK/kg" % (weekly * 0.93)  # Contract ~7% below spot
	_kpi_volume.text = "%.0f tonn" % (_sim.fish_biomass_kg / 1000.0 * 0.2)  # 20% harvestable
	_kpi_open.text = str(_sim.get_active_contracts().size()) if _sim.has_method("get_active_contracts") else "0"

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
	if not _buyers_container:
		return
	for child in _buyers_container.get_children():
		child.queue_free()

	# Real offers from ContractSystem (cached per day). Each row gets an
	# Accept button that signs the contract at the locked price.
	var offers: Array = _sim.get_available_offers() if _sim.has_method("get_available_offers") else []
	if offers.is_empty():
		UIPolish.show_empty_state(_buyers_container, "Ingen tilbud i dag — kom tilbake i morgen", 13, "", _theme)
		return

	for offer in offers:
		var idx := int(offer.get("index", -1))
		var row := PanelContainer.new()
		row.size_flags_horizontal = SIZE_EXPAND_FILL
		var sb := StyleBoxFlat.new()
		sb.bg_color = C_ELEVATED
		sb.border_color = C_BORDER
		sb.set_border_width_all(1)
		sb.set_corner_radius_all(4)
		sb.set_content_margin_all(10)
		row.add_theme_stylebox_override("panel", sb)
		_buyers_container.add_child(row)

		var hbox := HBoxContainer.new()
		row.add_child(hbox)

		var left := VBoxContainer.new()
		left.size_flags_horizontal = SIZE_EXPAND_FILL
		hbox.add_child(left)

		var name_lbl := Label.new()
		name_lbl.text = str(offer.get("buyer", "?"))
		name_lbl.add_theme_font_size_override("font_size", 14)
		name_lbl.add_theme_color_override("font_color", C_TEXT)
		if _theme and _theme.font_heading_semibold:
			name_lbl.add_theme_font_override("font", _theme.font_heading_semibold)
		left.add_child(name_lbl)

		var type_lbl := Label.new()
		type_lbl.text = "%s · %d uker · bot %.0f%%" % [
			str(offer.get("type", "?")), int(offer.get("duration_weeks", 0)),
			float(offer.get("penalty_pct", 0.0)) * 100.0]
		type_lbl.add_theme_font_size_override("font_size", 12)
		type_lbl.add_theme_color_override("font_color", C_MUTED)
		left.add_child(type_lbl)

		var right := VBoxContainer.new()
		hbox.add_child(right)

		var price_lbl := Label.new()
		price_lbl.text = "%.2f" % float(offer.get("price_per_kg", 0.0))
		price_lbl.add_theme_font_size_override("font_size", 16)
		price_lbl.add_theme_color_override("font_color", C_PRIMARY)
		if _theme and _theme.font_mono:
			price_lbl.add_theme_font_override("font", _theme.font_mono)
		price_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		right.add_child(price_lbl)

		var vol_lbl := Label.new()
		vol_lbl.text = "%.0f t" % (float(offer.get("quantity_kg", 0.0)) / 1000.0)
		vol_lbl.add_theme_font_size_override("font_size", 11)
		vol_lbl.add_theme_color_override("font_color", C_MUTED)
		vol_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		right.add_child(vol_lbl)

		var accept := Button.new()
		accept.text = "Signer"
		accept.pressed.connect(_on_accept_offer.bind(idx))
		hbox.add_child(accept)

	# UIPolish: vertical keyboard navigation for buyers list
	UIPolish.link_vertical_focus(_buyers_container)


func _on_accept_offer(index: int) -> void:
	if not _sim or not _sim.has_method("accept_offer"):
		return
	var contract_id: String = _sim.accept_offer(index)
	if not contract_id.is_empty():
		_refresh()

func _refresh_contracts() -> void:
	if not _contracts_container:
		return
	for child in _contracts_container.get_children():
		child.queue_free()

	# Real signed contracts (oldest first). Harvest fulfills them at locked prices.
	var contracts: Array = _sim.get_active_contracts() if _sim.has_method("get_active_contracts") else []

	# Table header
	var hdr := HBoxContainer.new()
	hdr.add_theme_constant_override("separation", 8)
	_contracts_container.add_child(hdr)
	for col in ["Kjøper", "Volum", "Pris", "Oppfylt", "Status"]:
		var l := Label.new()
		l.text = col
		l.add_theme_font_size_override("font_size", 11)
		l.add_theme_color_override("font_color", C_MUTED)
		l.size_flags_horizontal = SIZE_EXPAND_FILL
		hdr.add_child(l)

	if contracts.is_empty():
		_add_table_cell(hdr, "", C_MUTED)
		UIPolish.show_empty_state(_contracts_container, "Ingen aktive kontrakter — signer et tilbud over", 12, "", _theme)
		return

	for c in contracts:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_contracts_container.add_child(row)

		_add_table_cell(row, str(c.get("buyer", "?")), C_TEXT, true)
		_add_table_cell(row, "%.0f t" % (float(c.get("quantity_kg", 0.0)) / 1000.0), C_TEXT)
		_add_table_cell(row, "%.2f" % float(c.get("price_per_kg", 0.0)), C_TEXT, false, true)
		_add_table_cell(row, "%.0f%%" % float(c.get("fulfilled_percent", 0.0)), C_TEXT)
		_add_table_cell(row, "Aktiv", C_SUCCESS_D)

	# UIPolish: vertical keyboard navigation for contracts list
	UIPolish.link_vertical_focus(_contracts_container)

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
