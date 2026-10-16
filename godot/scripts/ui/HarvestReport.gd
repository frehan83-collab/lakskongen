class_name HarvestReport
extends Control

## The harvest report (roadmap U2-05): the payoff sheet of a harvest, with
## every figure as the ledger booked it. A planned order's batches (one per
## slaughter day) are summed; an express, hired-wellboat or forced harvest is
## its one batch. The net leads (counting up to its value, like the coin
## tally of a harvest payday in Farming Simulator or Anno's trade income),
## then the fish (count, live and gutted kilos, mean weight, superior share),
## the weight histogram priced per size class, the grades, and the settlement
## line by line: contracts, spot, the processing premium, gross, wellboat,
## slaughter, any express surcharge, net. The clock waits while it is open.
##
## Opened by the harvest's one toast ("Se rapporten"), by the planner after an
## express harvest, and from the planner's "Siste slakterapport".

signal report_closed

const CLOCK_HOLD := "harvest_report"
## Seconds the net counts up (none under reduced motion).
const COUNT_UP_S := 0.9
const FADE_IN_S := 0.2
## Seconds the histogram's bars take to grow in, light classes first.
const BARS_GROW_S := 0.8
## The net's settle: a small swell once the tally lands (scale, seconds).
const NET_SETTLE_SCALE := 1.06
const NET_SETTLE_S := 0.22

@onready var _card: PanelContainer = %Card
@onready var _overline: Label = %Overline
@onready var _title: Label = %TitleLabel
@onready var _subtitle: Label = %SubtitleLabel
@onready var _mode_tag: Label = %ModeTag
@onready var _net_caption: Label = %NetCaption
@onready var _net_value: Label = %NetValue
@onready var _net_detail: Label = %NetDetail
@onready var _kpis: GridContainer = %Kpis
@onready var _chart_title: Label = %ChartTitle
@onready var _size_chart: SizeClassChart = %SizeChart
@onready var _grade_caption: Label = %GradeCaption
@onready var _grade_bar: GradeBar = %GradeBar
@onready var _grade_legend: Label = %GradeLegend
@onready var _settle_title: Label = %SettleTitle
@onready var _settle_grid: GridContainer = %SettleGrid
@onready var _ledger_note: Label = %LedgerNote
@onready var _footer_note: Label = %FooterNote
@onready var close_button: Button = %CloseButton

var _sim: Node = null
var _options: Dictionary = {}
var _tween: Tween = null
var _held := false
## The report shown (the summed batches): tests read it.
var report: Dictionary = {}
## The figures as shown, by row key (gross, contract, spot, transport,
## slaughter, surcharge, cost, net): the label texts, for the ledger check.
var shown: Dictionary = {}
## Times the sheet was opened (tests).
var shown_count: int = 0


func _ready() -> void:
	add_to_group(HarvestScreens.REPORT_GROUP)
	_card.add_to_group(&"toast_keep_clear")
	_sim = get_node_or_null("/root/SimBridge")
	close_button.pressed.connect(close)
	UIPolish.attach_press_feedback(close_button)
	_overline.text = tr("HARVESTREPORT_OVERLINE")
	_net_caption.text = tr("HARVESTREPORT_NET")
	_chart_title.text = tr("HARVESTPLAN_CHART_TITLE")
	_grade_caption.text = tr("HARVESTPLAN_GRADES")
	_settle_title.text = tr("HARVESTPLAN_SETTLEMENT")
	close_button.text = tr("HARVESTREPORT_CLOSE")
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	hide()


func is_open() -> bool:
	return visible


func _input(event: InputEvent) -> void:
	if visible and (event.is_action_pressed("ui_cancel") or event.is_action_pressed("ui_accept")):
		close()
		get_viewport().set_input_as_handled()


## Show the harvest `key` names: an order id (its batches summed) or one
## report id. False when no report has it.
func show_harvest(key: String) -> bool:
	if _sim == null or key.is_empty():
		return false
	var batches: Array = []
	for r in _sim.get_harvest_reports():
		if str(r.get("order_id", "")) == key or str(r.get("id", "")) == key:
			batches.append(r)
	if batches.is_empty():
		return false
	if _options.is_empty():
		_options = _sim.get_harvest_planner_options()
	report = sum_batches(batches)
	_fill()
	_open()
	return true


## The latest harvest of `holding_id` (its order's batches summed). False
## when the holding has no report.
func show_latest_for(holding_id: String) -> bool:
	var key := latest_key_for(_sim, holding_id)
	return show_harvest(key) if not key.is_empty() else false


## The key of a holding's latest harvest: its order id, or its report id.
static func latest_key_for(sim: Node, holding_id: String) -> String:
	if sim == null:
		return ""
	var reports: Array = sim.get_harvest_reports()
	for i in range(reports.size() - 1, -1, -1):
		var r: Dictionary = reports[i]
		if str(r.get("holding_id", "")) == holding_id:
			var order_id := str(r.get("order_id", ""))
			return order_id if not order_id.is_empty() else str(r.get("id", ""))
	return ""


## One harvest from its batches: fish, kilos, money and the size classes
## summed; the shares weighted by gutted kilos; the first and last day.
static func sum_batches(batches: Array) -> Dictionary:
	var sum := {"fish": 0, "live_kg": 0.0, "hog_kg": 0.0, "reference_kg": 0.0, "contract_hog_kg": 0.0,
		"contract_nok": 0.0, "gross_nok": 0.0, "transport_nok": 0.0, "slaughter_nok": 0.0,
		"surcharge_nok": 0.0, "cost_nok": 0.0, "net_nok": 0.0, "superior": 0.0, "ordinary": 0.0,
		"production": 0.0, "first_day": 1 << 30, "last_day": -1, "batches": batches.size(), "vap": 1.0,
		"price_multiplier": 1.0}
	var classes: Array = []
	for r in batches:
		var hog := float(r.get("hog_kg", 0.0))
		for key in ["live_kg", "hog_kg", "reference_kg", "contract_hog_kg", "contract_nok", "gross_nok",
				"transport_nok", "slaughter_nok", "surcharge_nok", "cost_nok", "net_nok"]:
			sum[key] = float(sum[key]) + float(r.get(key, 0.0))
		sum["fish"] = int(sum["fish"]) + int(r.get("fish", 0))
		for g in ["superior", "ordinary", "production"]:
			sum[g] = float(sum[g]) + float(r.get(g, 0.0)) * hog
		sum["first_day"] = mini(int(sum["first_day"]), int(r.get("day", 0)))
		sum["last_day"] = maxi(int(sum["last_day"]), int(r.get("day", 0)))
		var rc: Array = r.get("size_classes", [])
		if classes.is_empty():
			classes.resize(rc.size())
			classes.fill(0.0)
		for c in mini(classes.size(), rc.size()):
			classes[c] = float(classes[c]) + float(rc[c])
		for k in ["holding_id", "holding_label", "order_id", "mode", "route"]:
			sum[k] = r.get(k, "")
		sum["vap"] = float(r.get("vap", 1.0))
		sum["price_multiplier"] = float(r.get("price_multiplier", 1.0))
	var total_hog := float(sum["hog_kg"])
	for g in ["superior", "ordinary", "production"]:
		sum[g] = float(sum[g]) / total_hog if total_hog > 0.0 else 0.0
	sum["size_classes"] = classes
	sum["mean_kg"] = float(sum["live_kg"]) / int(sum["fish"]) if int(sum["fish"]) > 0 else 0.0
	return sum


func close() -> void:
	if not visible:
		return
	if _tween != null and _tween.is_valid():
		_tween.kill()
	_net_value.scale = Vector2.ONE
	_size_chart.grow = 1.0
	hide()
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	if _held and _sim != null:
		_sim.release_clock(CLOCK_HOLD)
	_held = false
	report_closed.emit()


# ── Filling ────────────────────────────────────────────────────────────────────

func _fill() -> void:
	var r := report
	_title.text = str(r.get("holding_label", r.get("holding_id", "")))
	var first := int(r.get("first_day", 0))
	var last := int(r.get("last_day", 0))
	var days := tr("HARVESTREPORT_ONE_DAY") % SimFormatters.format_day(first) if first == last \
		else tr("HARVESTREPORT_DAYS") % [last - first + 1, SimFormatters.format_day(first), SimFormatters.format_day(last)]
	_subtitle.text = days
	var mode := str(r.get("mode", "Planned"))
	_mode_tag.text = tr("HARVESTREPORT_MODE_" + mode.to_upper())
	_mode_tag.theme_type_variation = &"TagWarn" if mode == "Forced" else &"TagInfo"

	var net := float(r.get("net_nok", 0.0))
	_net_value.theme_type_variation = &"DisplayOk" if net >= 0.0 else &"DisplayCrit"
	_net_detail.text = tr("HARVESTREPORT_NET_DETAIL") % [_nok(float(r.get("gross_nok", 0.0))), _nok(float(r.get("cost_nok", 0.0)))]
	_count_up(net)

	_fill_kpis()
	_size_chart.show_classes(r.get("size_classes", []), _options.get("size_labels", []),
		_options.get("size_factors", []), int(_options.get("reference_class", 4)))
	_grade_bar.shares = [float(r.get("superior", 0.0)), float(r.get("ordinary", 0.0)), float(r.get("production", 0.0))]
	_grade_legend.text = tr("HARVESTPLAN_GRADE_LEGEND") % [SimFormatters.percent(float(r.get("superior", 0.0))),
		SimFormatters.percent(float(r.get("ordinary", 0.0))), SimFormatters.percent(float(r.get("production", 0.0)))]
	_fill_settlement()
	_ledger_note.text = tr("HARVESTREPORT_LEDGER") % [_nok(float(r.get("gross_nok", 0.0))), _nok(float(r.get("cost_nok", 0.0)))]
	var price := float(r.get("gross_nok", 0.0)) / float(r.get("hog_kg", 1.0)) if float(r.get("hog_kg", 0.0)) > 0.0 else 0.0
	_footer_note.text = tr("HARVESTREPORT_FOOTER") % [SimFormatters.format_price_kg(price),
		SimFormatters.num(float(r.get("reference_kg", 0.0)) / maxf(1.0, float(r.get("hog_kg", 1.0))), 2)]


func _fill_kpis() -> void:
	_clear(_kpis)
	var r := report
	var items := [
		["HARVESTREPORT_KPI_FISH", SimFormatters.integer(int(r.get("fish", 0)))],
		["HARVESTREPORT_KPI_LIVE", SimFormatters.format_biomass(float(r.get("live_kg", 0.0)))],
		["HARVESTREPORT_KPI_HOG", SimFormatters.format_biomass(float(r.get("hog_kg", 0.0)))],
		["HARVESTREPORT_KPI_MEAN", SimFormatters.format_fish_weight(float(r.get("mean_kg", 0.0)))],
		["HARVESTREPORT_KPI_SUPERIOR", SimFormatters.percent(float(r.get("superior", 0.0)))],
	]
	for item in items:
		var caption := Label.new()
		caption.theme_type_variation = &"CaptionMuted"
		caption.text = tr(item[0])
		_kpis.add_child(caption)
	for item in items:
		var value := Label.new()
		value.theme_type_variation = &"FigureLarge"
		value.text = item[1]
		_kpis.add_child(value)


func _fill_settlement() -> void:
	_clear(_settle_grid)
	shown.clear()
	var r := report
	var contract_kg := float(r.get("contract_hog_kg", 0.0))
	var contract := float(r.get("contract_nok", 0.0))
	var gross := float(r.get("gross_nok", 0.0))
	if contract_kg > 0.0:
		_row("contract", tr("HARVESTPLAN_ROW_CONTRACT") % SimFormatters.format_biomass(contract_kg), _nok(contract), &"Figure")
		_row("spot", tr("HARVESTPLAN_ROW_SPOT") % SimFormatters.format_biomass(float(r.get("hog_kg", 0.0)) - contract_kg),
			_nok(gross - contract), &"Figure")
	var vap := float(r.get("vap", 1.0))
	if absf(vap - 1.0) > 1e-6:
		_row("vap", tr("HARVESTPLAN_ROW_VAP"), "×" + SimFormatters.num(vap, 2), &"FigureMuted")
	var penalty := float(r.get("price_multiplier", 1.0))
	if absf(penalty - 1.0) > 1e-6:
		_row("penalty", tr("HARVESTREPORT_ROW_PENALTY"), "×" + SimFormatters.num(penalty, 2), &"FigureWarn")
	_row("gross", tr("HARVESTPLAN_ROW_GROSS"), _nok(gross), &"FigureLarge")
	_row("transport", tr("HARVESTPLAN_ROW_TRANSPORT"), _nok(-float(r.get("transport_nok", 0.0))), &"Figure")
	_row("slaughter", tr("HARVESTPLAN_ROW_SLAUGHTER"), _nok(-float(r.get("slaughter_nok", 0.0))), &"Figure")
	if float(r.get("surcharge_nok", 0.0)) > 0.0:
		_row("surcharge", tr("HARVESTPLAN_ROW_SURCHARGE"), _nok(-float(r.get("surcharge_nok", 0.0))), &"FigureWarn")
	shown["cost"] = _nok(float(r.get("cost_nok", 0.0)))
	_row("net", tr("HARVESTPLAN_ROW_NET"), _nok(float(r.get("net_nok", 0.0))),
		&"FigureLargeOk" if float(r.get("net_nok", 0.0)) >= 0.0 else &"FigureLargeCrit")


func _row(key: String, caption: String, value: String, variation: StringName) -> void:
	var name_label := Label.new()
	name_label.theme_type_variation = &"BodySmall"
	name_label.text = caption
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_settle_grid.add_child(name_label)
	var value_label := Label.new()
	value_label.theme_type_variation = variation
	value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	value_label.custom_minimum_size.x = 150.0
	value_label.text = value
	_settle_grid.add_child(value_label)
	shown[key] = value


## Empty a grid now (a queued child would still count in this frame's layout).
func _clear(grid: Container) -> void:
	for child in grid.get_children():
		grid.remove_child(child)
		child.queue_free()


## Whole kroner with grouping, as the ledger books them: "8 605 884 NOK".
static func _nok(amount: float) -> String:
	return SimFormatters.num(roundf(amount), 0) + " NOK"


# ── The payoff ─────────────────────────────────────────────────────────────────

func _open() -> void:
	shown_count += 1
	mouse_filter = Control.MOUSE_FILTER_STOP
	show()
	if _sim != null and not _held:
		_sim.hold_clock(CLOCK_HOLD)
		_held = true
	var still := MotionPrefs.reduced_motion()
	_card.modulate.a = 1.0 if still else 0.0
	if not still:
		var fade := create_tween()
		fade.tween_property(_card, "modulate:a", 1.0, FADE_IN_S)
	close_button.grab_focus.call_deferred()


## The payoff beat: the net counts up to its value (a payday tally) and
## swells once as it lands, while the histogram's bars grow in class by class.
## Reduced motion shows everything at once. Only transforms, colour and the
## chart's own drawing move; nothing re-lays out.
func _count_up(net: float) -> void:
	if _tween != null and _tween.is_valid():
		_tween.kill()
	_net_value.scale = Vector2.ONE
	if MotionPrefs.reduced_motion() or not is_inside_tree():
		_set_net(net)
		_size_chart.grow = 1.0
		return
	_set_net(0.0)
	_size_chart.grow = 0.0
	_tween = create_tween()
	_tween.set_parallel(true)
	_tween.tween_method(_set_net, 0.0, net, COUNT_UP_S).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	_tween.tween_property(_size_chart, "grow", 1.0, BARS_GROW_S).set_ease(Tween.EASE_IN_OUT).set_trans(Tween.TRANS_SINE)
	# The swell grows from the figure's left edge (it is left-aligned), at
	# its laid-out size: the pivot is taken when the tally lands.
	_tween.chain().tween_callback(func() -> void: _net_value.pivot_offset = Vector2(0.0, _net_value.size.y * 0.5))
	_tween.tween_property(_net_value, "scale", Vector2.ONE * NET_SETTLE_SCALE, NET_SETTLE_S * 0.5) \
		.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_QUAD)
	_tween.chain().tween_property(_net_value, "scale", Vector2.ONE, NET_SETTLE_S * 0.5) \
		.set_ease(Tween.EASE_IN).set_trans(Tween.TRANS_QUAD)


func _set_net(value: float) -> void:
	_net_value.text = ("+" if value >= 0.0 else "") + _nok(value)
