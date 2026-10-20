class_name FacilityBuildPreview
extends VBoxContainer

## The build preview card of a Land or Hav variant on a plot (roadmap F2-31,
## docs/FARM_TYPES_SPEC.md §6.2 step 4): the capex and its payment schedule
## per phase with the p50/p90 overrun band, a 36-month cash runway, the
## payback year, the farming cost per kg HOG, the capacity cost per tonne of
## yearly HOG capacity with the licence, the first harvest; on land the
## permit timeline with refusal odds and drivers and the mitigations; at sea
## the licence value line, the consortium option and the teething deck. Every
## figure is the engine's (SimBridge.preview_farm_build); the card words and
## lays them out, and reports the player's choices back to the dialog.

signal contract_changed(contract: String)
signal consortium_toggled(on: bool)
signal mitigation_toggled(mitigation_id: String, on: bool)

## The land mitigations a plan can carry (the engine's LandPermitSystem ids).
const MITIGATIONS := ["environmental_survey", "denitrification"]

var _kpis: GridContainer = null
var _payments: GridContainer = null
var _runway: CashRunwayChart = null
var _permits: PermitTimeline = null
var _sea_box: VBoxContainer = null
var _choices: HFlowContainer = null
var _watch: Label = null
## The preview shown (the bridge's dictionary).
var preview: Dictionary = {}


func _ready() -> void:
	if _kpis == null:
		_build()


func _build() -> void:
	add_theme_constant_override("separation", 10)
	_choices = HFlowContainer.new()
	_choices.name = "Choices"
	_choices.add_theme_constant_override("h_separation", 8)
	_choices.add_theme_constant_override("v_separation", 6)
	add_child(_choices)
	_kpis = GridContainer.new()
	_kpis.name = "Kpis"
	_kpis.columns = 3
	_kpis.add_theme_constant_override("h_separation", 10)
	_kpis.add_theme_constant_override("v_separation", 10)
	add_child(_kpis)
	var pay_title := Label.new()
	pay_title.text = tr("BUILD_PAYMENTS_TITLE")
	pay_title.theme_type_variation = &"OverlineMuted"
	pay_title.uppercase = true
	add_child(pay_title)
	_payments = GridContainer.new()
	_payments.name = "Payments"
	_payments.columns = 4
	_payments.add_theme_constant_override("h_separation", 16)
	_payments.add_theme_constant_override("v_separation", 4)
	add_child(_payments)
	_runway = CashRunwayChart.new()
	_runway.name = "Runway"
	add_child(_runway)
	_permits = PermitTimeline.new()
	_permits.name = "Permits"
	add_child(_permits)
	_sea_box = VBoxContainer.new()
	_sea_box.name = "Sea"
	_sea_box.add_theme_constant_override("separation", 4)
	add_child(_sea_box)
	_watch = Label.new()
	_watch.name = "Watch"
	_watch.theme_type_variation = &"BodySmallMuted"
	_watch.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_watch.custom_minimum_size = Vector2(300, 0)
	add_child(_watch)


## Show `p` (SimBridge.preview_farm_build) with the dialog's choices.
func show_preview(p: Dictionary, contract: String, consortium: bool, mitigations: Array) -> void:
	if _kpis == null:
		_build()
	preview = p
	var e: Dictionary = p.get("economics", {})
	var family := str(p.get("family", ""))
	_fill_choices(family, contract, consortium, mitigations, p)
	_fill_kpis(e, family)
	_fill_payments(p, family)
	_runway.show_runway(e)
	_permits.visible = family == "land"
	if family == "land":
		_permits.show_permits(p.get("land", {}))
	_sea_box.visible = family == "hav"
	if family == "hav":
		_fill_sea(p.get("offshore", {}), e)
	_watch.text = tr("BUILD_WATCH_" + family.to_upper())


## A headline figure's value label by id ("Capex", "CapacityCost", "FirstHarvest", "Payback", "Farming", "Price").
func kpi(id: String) -> Label:
	return _kpis.get_node_or_null("Kpi%s/Col/Value" % id) as Label if _kpis != null else null


## The payment schedule (tests read its rows).
func payments() -> GridContainer:
	return _payments


func runway() -> CashRunwayChart:
	return _runway


func permits() -> PermitTimeline:
	return _permits


## The sea lines (licence value, consortium, teething), by name.
func sea_line(id: String) -> Label:
	return _sea_box.get_node_or_null(id) as Label if _sea_box != null else null


## A choice control by name ("Managed", "Turnkey", "Consortium", "Mitigation_<id>").
func choice(id: String) -> BaseButton:
	return _choices.get_node_or_null(id) as BaseButton if _choices != null else null


func _clear(node: Node) -> void:
	for child in node.get_children():
		node.remove_child(child)
		child.queue_free()


func _fill_choices(family: String, contract: String, consortium: bool, mitigations: Array, p: Dictionary) -> void:
	_clear(_choices)
	var group := ButtonGroup.new()
	for c in ["managed", "turnkey"]:
		var b := Button.new()
		b.name = c.capitalize()
		b.text = tr("LAND_CONTRACT_" + c.to_upper())
		b.toggle_mode = true
		b.button_group = group
		b.theme_type_variation = &"SmallButton" if c == contract else &"SmallGhostButton"
		b.set_pressed_no_signal(c == contract)
		b.tooltip_text = tr("BUILD_CONTRACT_TIP_" + c.to_upper())
		b.pressed.connect(func() -> void: contract_changed.emit(c))
		_choices.add_child(b)
	if family == "hav":
		var share := float((p.get("offshore", {}) as Dictionary).get("consortium_equity_share", 0.0))
		var box := CheckBox.new()
		box.name = "Consortium"
		box.text = tr("BUILD_CONSORTIUM_TOGGLE") % SimFormatters.percent(share)
		box.set_pressed_no_signal(consortium)
		box.toggled.connect(func(on: bool) -> void: consortium_toggled.emit(on))
		_choices.add_child(box)
	elif family == "land":
		for m in MITIGATIONS:
			var box := CheckBox.new()
			box.name = "Mitigation_" + m
			box.text = tr("LAND_MITIGATION_" + m.to_upper())
			box.set_pressed_no_signal(mitigations.has(m))
			box.toggled.connect(func(on: bool) -> void: mitigation_toggled.emit(m, on))
			_choices.add_child(box)
	UIPolish.link_horizontal_focus(_choices)


func _kpi_tile(id: String, key: String, value: String, note: String, variation: StringName = &"FigureLarge") -> void:
	var tile := PanelContainer.new()
	tile.name = "Kpi" + id
	tile.theme_type_variation = &"Tile"
	tile.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var col := VBoxContainer.new()
	col.name = "Col"
	col.add_theme_constant_override("separation", 2)
	tile.add_child(col)
	var k := Label.new()
	k.name = "Key"
	k.text = tr(key)
	k.theme_type_variation = &"OverlineMuted"
	k.uppercase = true
	k.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	k.custom_minimum_size = Vector2(150, 0)
	col.add_child(k)
	var v := Label.new()
	v.name = "Value"
	v.text = value
	v.theme_type_variation = variation
	v.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.custom_minimum_size = Vector2(150, 0)
	col.add_child(v)
	if note != "":
		var n := Label.new()
		n.name = "Note"
		n.text = note
		n.theme_type_variation = &"SmallMuted"
		n.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		n.custom_minimum_size = Vector2(150, 0)
		col.add_child(n)
	_kpis.add_child(tile)


func _fill_kpis(e: Dictionary, family: String) -> void:
	_clear(_kpis)
	var capex := float(e.get("capex_nok", 0.0))
	_kpi_tile("Capex", "BUILD_KPI_CAPEX", SimFormatters.format_nok(capex),
		tr("BUILD_KPI_CAPEX_NOTE") % [SimFormatters.format_nok(float(e.get("overrun_p50_nok", 0.0))), SimFormatters.format_nok(float(e.get("overrun_p90_nok", 0.0)))])
	var licence := float(e.get("licence_nok", 0.0))
	_kpi_tile("CapacityCost", "BUILD_KPI_CAPACITY_COST",
		SimFormatters.format_nok(float(e.get("capacity_cost_nok_per_t_year", 0.0))) + "/t/år",
		(tr("BUILD_KPI_CAPACITY_NOTE_LICENCE") % [SimFormatters.num(float(e.get("capacity_t_per_year", 0.0)), 0), SimFormatters.format_nok(licence)])
			if licence > 0.0 else tr("BUILD_KPI_CAPACITY_NOTE") % SimFormatters.num(float(e.get("capacity_t_per_year", 0.0)), 0))
	var wait := int(e.get("permit_wait_days", 0))
	_kpi_tile("FirstHarvest", "BUILD_KPI_FIRST_HARVEST", SimFormatters.format_day(int(e.get("first_harvest_day", 0))),
		tr("BUILD_KPI_FIRST_HARVEST_PERMITS") % SimFormatters.num(float(wait) * SimFormatters.MONTHS_PER_YEAR / SimFormatters.DAYS_PER_YEAR, 0)
			if wait > 0 else tr("BUILD_KPI_FIRST_HARVEST_NOW"))
	var year := int(e.get("payback_year", -1))
	var margin := float(e.get("yearly_margin_nok", 0.0))
	_kpi_tile("Payback", "BUILD_KPI_PAYBACK", str(year) if year > 0 else tr("BUILD_KPI_PAYBACK_NEVER"),
		tr("BUILD_KPI_PAYBACK_NOTE") % SimFormatters.format_nok(margin) if margin > 0.0 else tr("BUILD_KPI_PAYBACK_NO_MARGIN_" + family.to_upper()),
		&"FigureLarge" if year > 0 else &"BodyStrongWarn")
	var cost: Variant = e.get("farming_cost_nok_per_kg")
	_kpi_tile("Farming", "BUILD_KPI_FARMING_COST", "–" if cost == null else SimFormatters.num(float(cost), 1) + " kr/kg",
		tr("BUILD_KPI_FARMING_NOTE") if cost != null else tr("BUILD_NOTE_NO_PUBLISHED_FIGURE"))
	_kpi_tile("Price", "BUILD_KPI_PRICE", SimFormatters.num(float(e.get("price_nok_per_kg", 0.0)), 1) + " kr/kg",
		tr("BUILD_KPI_PRICE_NOTE"))


func _fill_payments(p: Dictionary, family: String) -> void:
	_clear(_payments)
	for key in ["BUILD_PAY_PHASE", "BUILD_PAY_STARTS", "BUILD_PAY_AMOUNT", "BUILD_PAY_OVERRUN"]:
		var h := Label.new()
		h.text = tr(key)
		h.theme_type_variation = &"SmallMuted"
		_payments.add_child(h)
	var wait := int((p.get("economics", {}) as Dictionary).get("permit_wait_days", 0))
	var rows: Array = []
	if family == "land":
		for pay in (p.get("land", {}) as Dictionary).get("payments", []):
			rows.append([tr(str(pay.get("label_key", ""))), int(pay.get("start_day", 0)) + wait, float(pay.get("payment_nok", 0.0)),
				float(pay.get("overrun_p50_nok", 0.0)), float(pay.get("overrun_p90_nok", 0.0))])
	else:
		for pay in (p.get("offshore", {}) as Dictionary).get("payments", []):
			rows.append([tr("BUILD_SEA_PHASE_" + str(pay.get("phase", "")).to_upper()), int(pay.get("planned_start_day", 0)),
				float(pay.get("payment_nok", 0.0)), float(pay.get("overrun_median_nok", 0.0)), float(pay.get("overrun_p90_nok", 0.0))])
	var i := 0
	for r in rows:
		var cells := [r[0], SimFormatters.format_day(int(r[1])), SimFormatters.format_nok(float(r[2])),
			"–" if float(r[4]) <= 0.0 else "%s – %s" % [SimFormatters.format_nok(float(r[3])), SimFormatters.format_nok(float(r[4]))]]
		for c in cells.size():
			var l := Label.new()
			l.name = "Row%d_%d" % [i, c]
			l.text = str(cells[c])
			l.theme_type_variation = &"BodySmall" if c == 0 else &"FigureSmall"
			_payments.add_child(l)
		i += 1


func _fill_sea(sea: Dictionary, e: Dictionary) -> void:
	_clear(_sea_box)
	var licence := Label.new()
	licence.name = "LicenceValue"
	licence.text = tr("BUILD_SEA_LICENCE_LINE") % [int(sea.get("licences", 0)), SimFormatters.num(float(sea.get("licence_tonnes_mtb", 0.0)) / maxf(1.0, float(sea.get("licences", 1))), 0),
		SimFormatters.format_nok(float(sea.get("licence_market_value_nok", 0.0))), SimFormatters.format_nok(float(sea.get("licence_price_nok_per_tonne", 0.0))),
		SimFormatters.format_nok(float(sea.get("conversion_fee_nok", 0.0)))]
	licence.theme_type_variation = &"BodySmallStrong"
	licence.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	licence.custom_minimum_size = Vector2(300, 0)
	_sea_box.add_child(licence)
	var consortium := Label.new()
	consortium.name = "ConsortiumLine"
	consortium.text = tr("BUILD_SEA_CONSORTIUM_LINE") % [SimFormatters.percent(float(sea.get("consortium_equity_share", 0.0))),
		SimFormatters.format_nok(float(sea.get("consortium_company_capex_nok", 0.0))), SimFormatters.num(float(sea.get("buyout_multiple", 0.0)), 1)]
	consortium.theme_type_variation = &"BodySmall"
	consortium.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	consortium.custom_minimum_size = Vector2(300, 0)
	_sea_box.add_child(consortium)
	var teething := Label.new()
	teething.name = "Teething"
	var shakedowns: Array = sea.get("shakedown_options_days", [])
	var longest := 0
	for d in shakedowns:
		longest = maxi(longest, int(d))
	teething.text = tr("BUILD_SEA_TEETHING_LINE") % [int(sea.get("teething_defects_min", 0)), int(sea.get("teething_defects_max", 0)),
		SimFormatters.format_nok(float(sea.get("teething_budget_nok", 0.0))), longest]
	teething.theme_type_variation = &"BodySmall"
	teething.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	teething.custom_minimum_size = Vector2(300, 0)
	_sea_box.add_child(teething)
	var tow := Label.new()
	tow.name = "Tow"
	tow.text = tr("BUILD_SEA_TOW_LINE") % SimFormatters.format_day(int(sea.get("first_stocking_day", 0)))
	tow.theme_type_variation = &"BodySmallMuted"
	tow.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	tow.custom_minimum_size = Vector2(300, 0)
	_sea_box.add_child(tow)
