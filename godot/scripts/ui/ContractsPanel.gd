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
const C_BG := Color("#F4F1EA")
const C_SURFACE := Color("#FFFFFF")
const C_ELEVATED := Color("#EDE9E0")
const C_TEXT := Color("#1C1E20")
const C_TEXT2 := Color("#5A6168")
const C_MUTED := Color("#5A6168")
const C_BORDER := Color("#E7E2D7")
const C_PRIMARY := Color("#1F5E73")
const C_SECONDARY := Color("#1F5E73")
const C_SUCCESS := Color("#2E6B45")
const C_SUCCESS_D := Color("#2E6B45")
const C_WARNING := Color("#8A5A00")
const C_WARNING_D := Color("#8A5A00")
const C_DANGER := Color("#B3172F")
const C_ACCENT := Color("#1F5E73")

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
	_title_label.text = "📈 " + tr("UI_CONTRACTS_TITLE")
	_btn_new_contract.text = "📝 " + tr("UI_CONTRACTS_NEW")
	_btn_sell_spot.text = "📦 " + tr("UI_CONTRACTS_SELL_SPOT")
	_chart_title.text = "💹 " + tr("UI_CONTRACTS_CHART_TITLE")
	_buyers_title.text = "🏢 " + tr("UI_CONTRACTS_BUYERS")
	_contracts_title.text = "📋 " + tr("UI_CONTRACTS_ACTIVE")
	_regions_title.text = "🌍 " + tr("UI_CONTRACTS_REGIONS")

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	LakskongenTheme.set_strong(_title_label)
	LakskongenTheme.set_strong(_kpi_spot)
	LakskongenTheme.set_strong(_kpi_contract)
	LakskongenTheme.set_strong(_kpi_volume)
	LakskongenTheme.set_strong(_kpi_open)
	LakskongenTheme.set_strong(_chart_title)
	LakskongenTheme.set_strong(_buyers_title)
	LakskongenTheme.set_strong(_contracts_title)
	LakskongenTheme.set_strong(_regions_title)

# ══════════════════════════════════════════════════════════════════════════════
# DATA REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _refresh() -> void:
	if not _sim:
		return

	var spot: float = float(_sim.market_price_nok) if _sim else 75.0
	var weekly: float = float(_sim.weekly_avg_price_nok) if _sim else 75.0

	_kpi_spot.text = SimFormatters.format_price_kg(spot)
	_kpi_contract.text = SimFormatters.format_price_kg(weekly * 0.93)  # Contract ~7% below spot
	_kpi_volume.text = tr("UI_CONTRACTS_TONNES") % SimFormatters.num(_sim.fish_biomass_kg / 1000.0 * 0.2, 0)  # 20% harvestable
	_kpi_open.text = str(_sim.get_active_contracts().size()) if _sim.has_method("get_active_contracts") else "0"

	# Price summary
	_price_summary_lo.text = SimFormatters.num(spot * 0.83, 2)
	LakskongenTheme.set_text_color(_price_summary_lo, C_DANGER)
	_price_summary_avg.text = SimFormatters.num(spot * 0.91, 2)
	_price_summary_hi.text = SimFormatters.num(spot, 2)
	LakskongenTheme.set_text_color(_price_summary_hi, C_SUCCESS_D)
	_price_summary_trend.text = ("▲ " + tr("UI_TREND_UP")) if spot > weekly else ("▼ " + tr("UI_TREND_DOWN"))
	LakskongenTheme.set_text_color(_price_summary_trend, C_SUCCESS_D if spot > weekly else C_DANGER)

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
		UIPolish.show_empty_state(_buyers_container, tr("UI_CONTRACTS_NO_OFFERS"), 13, "", _theme)
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
		LakskongenTheme.set_size(name_lbl, 14)
		LakskongenTheme.set_text_color(name_lbl, C_TEXT)
		LakskongenTheme.set_strong(name_lbl)
		left.add_child(name_lbl)

		var type_lbl := Label.new()
		type_lbl.text = tr("UI_CONTRACTS_OFFER_TERMS") % [
			SimLabels.contract_type(str(offer.get("type", ""))), int(offer.get("duration_weeks", 0)),
			SimFormatters.percent(float(offer.get("penalty_pct", 0.0)))]
		LakskongenTheme.set_size(type_lbl, 12)
		LakskongenTheme.set_text_color(type_lbl, C_MUTED)
		left.add_child(type_lbl)

		var right := VBoxContainer.new()
		hbox.add_child(right)

		var price_lbl := Label.new()
		price_lbl.text = SimFormatters.num(float(offer.get("price_per_kg", 0.0)), 2)
		LakskongenTheme.set_size(price_lbl, 16)
		LakskongenTheme.set_text_color(price_lbl, C_PRIMARY)
		price_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		right.add_child(price_lbl)

		var vol_lbl := Label.new()
		vol_lbl.text = SimFormatters.num(float(offer.get("quantity_kg", 0.0)) / 1000.0, 0) + " t"
		LakskongenTheme.set_size(vol_lbl, 11)
		LakskongenTheme.set_text_color(vol_lbl, C_MUTED)
		vol_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		right.add_child(vol_lbl)

		var accept := Button.new()
		accept.text = tr("UI_CONTRACTS_SIGN")
		accept.tooltip_text = tr("UI_CONTRACTS_SIGN_TIP")
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
	for col in ["UI_CONTRACTS_COL_BUYER", "UI_CONTRACTS_COL_VOLUME", "UI_CONTRACTS_COL_PRICE",
			"UI_CONTRACTS_COL_FULFILLED", "UI_CONTRACTS_COL_STATUS"]:
		var l := Label.new()
		l.text = tr(col)
		LakskongenTheme.set_size(l, 11)
		LakskongenTheme.set_text_color(l, C_MUTED)
		l.size_flags_horizontal = SIZE_EXPAND_FILL
		hdr.add_child(l)

	if contracts.is_empty():
		_add_table_cell(hdr, "", C_MUTED)
		UIPolish.show_empty_state(_contracts_container, tr("UI_CONTRACTS_NONE_ACTIVE"), 12, "", _theme)
		return

	for c in contracts:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_contracts_container.add_child(row)

		_add_table_cell(row, str(c.get("buyer", "?")), C_TEXT, true)
		_add_table_cell(row, SimFormatters.num(float(c.get("quantity_kg", 0.0)) / 1000.0, 0) + " t", C_TEXT)
		_add_table_cell(row, SimFormatters.num(float(c.get("price_per_kg", 0.0)), 2), C_TEXT, false, true)
		_add_table_cell(row, SimFormatters.percent(float(c.get("fulfilled_percent", 0.0)) / 100.0), C_TEXT)
		_add_table_cell(row, tr("UI_CONTRACTS_STATUS_ACTIVE"), C_SUCCESS_D)

	# UIPolish: vertical keyboard navigation for contracts list
	UIPolish.link_vertical_focus(_contracts_container)

func _add_table_cell(row: HBoxContainer, text: String, color: Color, bold: bool = false, mono: bool = false) -> void:
	var l := Label.new()
	l.text = text
	LakskongenTheme.set_size(l, 13)
	LakskongenTheme.set_text_color(l, color)
	l.size_flags_horizontal = SIZE_EXPAND_FILL
	if mono:
		LakskongenTheme.set_role(l, &"FigureSmall")
	if bold:
		LakskongenTheme.set_strong(l)
	row.add_child(l)

func _refresh_regions() -> void:
	if not _regions_grid:
		return
	for child in _regions_grid.get_children():
		child.queue_free()

	var spot: float = float(_sim.market_price_nok) if _sim else 75.0
	var regions := [
		{"flag": "🇪🇺", "name": tr("UI_REGION_EU"), "price": "€%s/kg" % SimFormatters.num(spot * 0.09, 2), "level": "high"},
		{"flag": "🇬🇧", "name": tr("UI_REGION_UK"), "price": "£%s/kg" % SimFormatters.num(spot * 0.085, 2), "level": "medium"},
		{"flag": "🇯🇵", "name": tr("UI_REGION_JAPAN"), "price": "¥%s/kg" % SimFormatters.num(spot * 15.8, 0), "level": "high"},
		{"flag": "🇺🇸", "name": tr("UI_REGION_USA"), "price": "$%s/kg" % SimFormatters.num(spot * 0.099, 2), "level": "medium"},
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
		LakskongenTheme.set_size(flag, 20)
		vbox.add_child(flag)

		var name_lbl := Label.new()
		name_lbl.text = r.name
		LakskongenTheme.set_size(name_lbl, 13)
		LakskongenTheme.set_text_color(name_lbl, C_TEXT)
		LakskongenTheme.set_strong(name_lbl)
		vbox.add_child(name_lbl)

		var price_lbl := Label.new()
		price_lbl.text = r.price
		LakskongenTheme.set_size(price_lbl, 15)
		LakskongenTheme.set_text_color(price_lbl, C_TEXT)
		vbox.add_child(price_lbl)

		var demand := Label.new()
		demand.text = "● " + tr("UI_DEMAND_" + str(r.level).to_upper())
		LakskongenTheme.set_size(demand, 11)
		match r.level:
			"high": LakskongenTheme.set_text_color(demand, C_SUCCESS_D)
			"medium": LakskongenTheme.set_text_color(demand, C_WARNING_D)
			_: LakskongenTheme.set_text_color(demand, C_DANGER)
		vbox.add_child(demand)
