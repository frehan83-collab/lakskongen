## ContractsPanel v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/ContractsPanel.tscn; this script is wiring only:
## @onready % refs, localization, theme fonts, data refresh.
## Buyer and contract rows are data-driven and populated in code.
##
## U1-05: every number is the sim's. The price chart is the market price
## series (LineChart) over the days the game has (up to PRICE_WINDOW_DAYS),
## its low/average/high and trend come from the same series, the contract
## price is the signed contracts' volume-weighted price and the volume card
## is the volume under contract. The regional prices (invented from the spot
## price with fixed rates and demand) are gone until the market model has
## regions (S3-01); "Ny kontrakt" (a placeholder dialog) and "Selg spot"
## (U2-05) are gone until they do something: offers are signed in Kjøpere.

extends Control

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme: Node = null

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _title_label: Label = %TitleLabel

@onready var _kpi_spot: Label = %KpiSpotValue
@onready var _kpi_contract: Label = %KpiContractValue
@onready var _kpi_volume: Label = %KpiVolumeValue
@onready var _kpi_open: Label = %KpiOpenValue

@onready var _chart_title: Label = %ChartTitle
@onready var _buyers_title: Label = %BuyersTitle
@onready var _contracts_title: Label = %ContractsTitle

@onready var _price_summary_lo: Label = %SumLo
@onready var _price_summary_avg: Label = %SumAvg
@onready var _price_summary_hi: Label = %SumHi
@onready var _price_summary_trend: Label = %SumTrend

@onready var _buyers_container: VBoxContainer = %BuyersContainer
@onready var _contracts_container: VBoxContainer = %ContractsContainer
@onready var _price_chart: LineChart = %PriceChart

## The price chart reads at most this many days back (12 weeks).
const PRICE_WINDOW_DAYS := 84

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
	pass

func _apply_localization() -> void:
	_title_label.text = "📈 " + tr("UI_CONTRACTS_TITLE")
	_chart_title.text = "💹 " + tr("UI_CONTRACTS_CHART_TITLE")
	_buyers_title.text = "🏢 " + tr("UI_CONTRACTS_BUYERS")
	_contracts_title.text = "📋 " + tr("UI_CONTRACTS_ACTIVE")

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

# ══════════════════════════════════════════════════════════════════════════════
# DATA REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _refresh() -> void:
	if not _sim:
		return

	var spot: float = float(_sim.market_price_nok)
	_kpi_spot.text = SimFormatters.format_price_kg(spot)
	var contracts: Array = _sim.get_active_contracts() if _sim.has_method("get_active_contracts") else []
	var terms := contract_terms(contracts)
	_kpi_contract.text = SimFormatters.format_price_kg(terms["price"]) if terms["kg"] > 0.0 else "–"
	_kpi_volume.text = tr("UI_CONTRACTS_TONNES") % SimFormatters.num(terms["kg"] / 1000.0, 0)
	_kpi_open.text = SimFormatters.integer(contracts.size())
	_refresh_price_history()
	_refresh_buyers()
	_refresh_contracts()


## The signed contracts' volume under contract (kg) and its
## volume-weighted price (NOK/kg).
static func contract_terms(contracts: Array) -> Dictionary:
	var kg := 0.0
	var value := 0.0
	for c in contracts:
		var q := float(c.get("quantity_kg", 0.0))
		kg += q
		value += q * float(c.get("price_per_kg", 0.0))
	return {"kg": kg, "price": value / kg if kg > 0.0 else 0.0}


## The spot price over the days the game has, with its low, average and high
## and the sim's own 14-day trend.
func _refresh_price_history() -> void:
	var series: Array[float] = _sim.get_analytics_data("market_price_nok", PRICE_WINDOW_DAYS) \
		if _sim.has_method("get_analytics_data") else []
	_chart_title.text = "💹 " + tr("UI_CONTRACTS_CHART_TITLE_DAYS") % series.size()
	if _price_chart != null:
		_price_chart.title = ""
		_price_chart.unit_fmt = "%.2f"
		_price_chart.line_color = C_PRIMARY
		_price_chart.set_series(series)
	var stats := price_stats(series)
	var has := not series.is_empty()
	_price_summary_lo.text = SimFormatters.num(stats["low"], 2) if has else "–"
	LakskongenTheme.set_text_color(_price_summary_lo, C_DANGER)
	_price_summary_avg.text = SimFormatters.num(stats["avg"], 2) if has else "–"
	_price_summary_hi.text = SimFormatters.num(stats["high"], 2) if has else "–"
	LakskongenTheme.set_text_color(_price_summary_hi, C_SUCCESS_D)
	var trend: String = _sim.get_analytics_trend("market_price_nok", 14) if _sim.has_method("get_analytics_trend") else "stable"
	match trend:
		"up":
			_price_summary_trend.text = "▲ " + tr("UI_TREND_UP")
			LakskongenTheme.set_text_color(_price_summary_trend, C_SUCCESS_D)
		"down":
			_price_summary_trend.text = "▼ " + tr("UI_TREND_DOWN")
			LakskongenTheme.set_text_color(_price_summary_trend, C_DANGER)
		_:
			_price_summary_trend.text = "─ " + tr("UI_TREND_STABLE")
			LakskongenTheme.set_text_color(_price_summary_trend, C_MUTED)


## Low, average and high of a price series ({low, avg, high}; zeros when empty).
static func price_stats(series: Array) -> Dictionary:
	if series.is_empty():
		return {"low": 0.0, "avg": 0.0, "high": 0.0}
	var lo := INF
	var hi := -INF
	var total := 0.0
	for v in series:
		lo = minf(lo, float(v))
		hi = maxf(hi, float(v))
		total += float(v)
	return {"low": lo, "avg": total / series.size(), "high": hi}

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
