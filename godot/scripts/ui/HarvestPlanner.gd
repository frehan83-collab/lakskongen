class_name HarvestPlanner
extends Control

## The harvest planner (roadmap U2-05): what slaughtering a site's chosen
## pens brings, before the player commits. The engine projects a planned
## order (SimulationEngine.ProjectHarvest, twin-lite: the fish fast ahead of
## their turn, the wellboat comes on its slot, the slaughterhouse takes its
## daily capacity, every batch is priced as the sale books it); the sheet
## shows the weight histogram in gutted kilos with each size class's price,
## the grades, spot against contract, the wellboat slot and the slaughter
## days, the settlement (gross, wellboat, slaughter, net, the feed until the
## fast) and, side by side, ordering now or waiting two, four or eight weeks.
## "Bestill slakting" places the planned order (the default "Høst"); "Ekspress
## i dag" sells today with the surcharge (the secondary verb) and opens the
## report. The clock waits while the sheet is open.
##
## Successful sims put the sale's preview where the decision is: Farming
## Simulator's price screen shows each outlet's price and its trend before a
## trip; the planner shows each size class's price and what waiting would do.

signal planner_closed(action: String)

const CLOCK_HOLD := "harvest_planner"
## Shares of each pen's fish the planner offers.
const SHARES := [1.0, 0.75, 0.5, 0.25]
## Mean live weight under which the sheet warns that the fish are small.
const SMALL_FISH_KG := 3.5
const FADE_IN_S := 0.2
## The sheet's header, footer and screen margin (px): the body gets the rest.
const SHEET_CHROME_PX := 220.0
## The smallest body height (px) and the slack under its content.
const MIN_BODY_PX := 240.0
const BODY_SLACK_PX := 28.0

@onready var _card: PanelContainer = %Card
@onready var _scroll: ScrollContainer = %Scroll
@onready var _overline: Label = %Overline
@onready var _title: Label = %TitleLabel
@onready var _subtitle: Label = %SubtitleLabel
@onready var _status: Label = %StatusTag
@onready var report_button: Button = %ReportButton
@onready var close_button: Button = %CloseButton
@onready var _pens_caption: Label = %PensCaption
@onready var _pen_chips: HFlowContainer = %PenChips
@onready var _share_caption: Label = %ShareCaption
@onready var share_option: OptionButton = %ShareOption
@onready var _warning: Label = %Warning
@onready var _order_panel: PanelContainer = %OrderPanel
@onready var _order_title: Label = %OrderTitle
@onready var _order_detail: Label = %OrderDetail
@onready var cancel_order_button: Button = %CancelOrderButton
@onready var _columns: HBoxContainer = %Columns
@onready var _chart_title: Label = %ChartTitle
@onready var _shown_tag: Label = %ShownTag
@onready var size_chart: SizeClassChart = %SizeChart
@onready var _chart_note: Label = %ChartNote
@onready var _grade_caption: Label = %GradeCaption
@onready var grade_bar: GradeBar = %GradeBar
@onready var _grade_legend: Label = %GradeLegend
@onready var _price_title: Label = %PriceTitle
@onready var _price_grid: GridContainer = %PriceGrid
@onready var _logistics_title: Label = %LogisticsTitle
@onready var _logistics_grid: GridContainer = %LogisticsGrid
@onready var _settle_title: Label = %SettleTitle
@onready var _settle_grid: GridContainer = %SettleGrid
@onready var _wait_title: Label = %WaitTitle
@onready var _wait_grid: GridContainer = %WaitGrid
@onready var _wait_note: Label = %WaitNote
@onready var _footer_note: Label = %FooterNote
@onready var express_button: Button = %ExpressButton
@onready var order_button: Button = %OrderButton

var _sim: Node = null
var _options: Dictionary = {}
var _held := false
var _site_id := ""
var _site_name := ""
## Pen ids the player picked ("site/p03"); every free pen with fish at first.
var _picked: Array[String] = []
## The site's pens with fish ({id, number, fish_count, mean_weight_kg, in_order}).
var pens: Array = []
## The projections, one per wait (index = _options.waits), and the express quote.
var projections: Array = []
var express_quote: Dictionary = {}
## The open order holding pens of this site ({} without one).
var open_order: Dictionary = {}
## Index of the wait whose details the sheet shows (0 = now).
var shown_wait := 0
## The wait columns' header buttons (tests click them).
var wait_buttons: Array[Button] = []
## Times the sheet was opened (tests).
var shown_count: int = 0


func _ready() -> void:
	add_to_group(HarvestScreens.PLANNER_GROUP)
	_card.add_to_group(&"toast_keep_clear")
	_sim = get_node_or_null("/root/SimBridge")
	close_button.pressed.connect(close.bind("close"))
	order_button.pressed.connect(_on_order)
	express_button.pressed.connect(_on_express)
	cancel_order_button.pressed.connect(_on_cancel_order)
	report_button.pressed.connect(_on_report)
	share_option.item_selected.connect(func(_i: int) -> void: refresh())
	for b in [close_button, order_button, express_button, cancel_order_button, report_button]:
		UIPolish.attach_press_feedback(b)
	_apply_texts()
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	hide()


func _apply_texts() -> void:
	_overline.text = tr("HARVESTPLAN_OVERLINE")
	close_button.text = tr("HARVESTPLAN_CLOSE")
	report_button.text = tr("HARVESTPLAN_LAST_REPORT")
	_pens_caption.text = tr("HARVESTPLAN_PENS")
	_share_caption.text = tr("HARVESTPLAN_SHARE")
	share_option.clear()
	for s in SHARES:
		share_option.add_item(tr("HARVESTPLAN_SHARE_ITEM") % SimFormatters.percent(s))
	share_option.tooltip_text = tr("HARVESTPLAN_SHARE_TIP")
	_chart_title.text = tr("HARVESTPLAN_CHART_TITLE")
	_chart_note.text = tr("HARVESTPLAN_CHART_NOTE")
	_grade_caption.text = tr("HARVESTPLAN_GRADES")
	_price_title.text = tr("HARVESTPLAN_PRICE")
	_logistics_title.text = tr("HARVESTPLAN_LOGISTICS")
	_settle_title.text = tr("HARVESTPLAN_SETTLEMENT")
	_wait_title.text = tr("HARVESTPLAN_WAIT_TITLE")
	cancel_order_button.text = tr("HARVESTPLAN_CANCEL_ORDER")


func is_open() -> bool:
	return visible


func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		close("close")
		get_viewport().set_input_as_handled()


## Open the sheet for `site_id`: every free pen with fish picked, the whole of
## each pen's fish, the details of ordering now.
func open_for(site_id: String, site_name: String) -> void:
	if _sim == null:
		return
	if _options.is_empty():
		_options = _sim.get_harvest_planner_options()
	_site_id = site_id
	_site_name = site_name
	_title.text = site_name
	shown_wait = 0
	share_option.select(0)
	_load_pens()
	_picked.clear()
	for p in pens:
		if not bool(p.get("in_order", false)):
			_picked.append(str(p.get("id", "")))
	_build_pen_chips()
	refresh()
	_open()


func close(action: String = "close") -> void:
	if not visible:
		return
	hide()
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	if _held and _sim != null:
		_sim.release_clock(CLOCK_HOLD)
	_held = false
	planner_closed.emit(action)


func _open() -> void:
	shown_count += 1
	mouse_filter = Control.MOUSE_FILTER_STOP
	show()
	if not _held:
		_sim.hold_clock(CLOCK_HOLD)
		_held = true
	_refit()
	if not MotionPrefs.reduced_motion():
		_card.modulate.a = 0.0
		create_tween().tween_property(_card, "modulate:a", 1.0, FADE_IN_S)
	(order_button if not order_button.disabled else close_button).grab_focus.call_deferred()


## The sheet as tall as its content, up to the screen less the header and
## footer. A wrapping label reports the height it needs at the width it had
## at the last layout (none before the first), so the fit runs again one
## frame later, when every label has its real width.
func _refit() -> void:
	_fit_height()
	await get_tree().process_frame
	if is_inside_tree() and visible:
		_fit_height()


func _fit_height() -> void:
	var cap := get_viewport_rect().size.y - SHEET_CHROME_PX
	_scroll.custom_minimum_size.y = clampf(%Body.get_combined_minimum_size().y + BODY_SLACK_PX, MIN_BODY_PX, maxf(MIN_BODY_PX, cap))


# ── Pens ───────────────────────────────────────────────────────────────────────

func _load_pens() -> void:
	pens.clear()
	open_order = {}
	var held := {}
	for o in _sim.get_harvest_orders():
		if str(o.get("holding_id", "")) != _site_id:
			continue
		open_order = o
		for id in o.get("pen_ids", []):
			held[str(id)] = true
	for p in _sim.get_site_pens(_site_id):
		if not bool(p.get("occupied", false)) or int(p.get("fish_count", 0)) <= 0:
			continue
		var row: Dictionary = p.duplicate()
		row["in_order"] = held.has(str(p.get("id", ""))) or held.has(str(p.get("population_id", "")))
		pens.append(row)


func _build_pen_chips() -> void:
	for child in _pen_chips.get_children():
		_pen_chips.remove_child(child)
		child.queue_free()
	for p in pens:
		var id := str(p.get("id", ""))
		var chip := Button.new()
		chip.theme_type_variation = &"SmallButton"
		chip.toggle_mode = true
		chip.focus_mode = Control.FOCUS_ALL
		chip.text = tr("HARVESTPLAN_PEN_CHIP") % [int(p.get("number", 0)),
			SimFormatters.integer(int(p.get("fish_count", 0))),
			SimFormatters.format_fish_weight(float(p.get("mean_weight_kg", 0.0)))]
		chip.set_pressed_no_signal(_picked.has(id))
		if bool(p.get("in_order", false)):
			chip.disabled = true
			chip.tooltip_text = tr("HARVESTPLAN_PEN_IN_ORDER")
		else:
			chip.tooltip_text = tr("HARVESTPLAN_PEN_TIP")
			chip.toggled.connect(_on_pen_toggled.bind(id))
		chip.set_meta(&"pen_id", id)
		_pen_chips.add_child(chip)


func _on_pen_toggled(on: bool, id: String) -> void:
	if on and not _picked.has(id):
		_picked.append(id)
	elif not on:
		_picked.erase(id)
	refresh()


## The pen chips (tests click them).
func pen_chips() -> Array[Button]:
	var out: Array[Button] = []
	for child in _pen_chips.get_children():
		if child is Button and not child.is_queued_for_deletion():
			out.append(child)
	return out


func _picked_ids() -> Array[String]:
	var out: Array[String] = []
	out.assign(_picked)
	return out


func _share() -> float:
	return float(SHARES[clampi(share_option.selected, 0, SHARES.size() - 1)])


# ── Refresh ────────────────────────────────────────────────────────────────────

## Re-project every wait for the picked pens and share, and redraw.
func refresh() -> void:
	if _sim == null or _site_id.is_empty():
		return
	projections.clear()
	express_quote = {}
	var ids := _picked_ids()
	if not ids.is_empty():
		for wait in _options.get("waits", [0]):
			projections.append(_sim.project_harvest(_site_id, ids, _share(), int(wait)))
		express_quote = _sim.quote_harvest(_site_id, ids, _share(), true)
	_fill_header()
	_fill_order_panel()
	var ok := not projections.is_empty() and bool(projections[0].get("success", false))
	_columns.visible = ok
	if ok:
		shown_wait = clampi(shown_wait, 0, projections.size() - 1)
		_fill_details(projections[shown_wait])
		_fill_waits()
	_fill_footer(ok)
	if visible:
		_refit()


func _fill_header() -> void:
	var fish := 0
	var kg := 0.0
	for p in pens:
		fish += int(p.get("fish_count", 0))
		kg += float(p.get("mean_weight_kg", 0.0)) * int(p.get("fish_count", 0))
	var mean := kg / fish if fish > 0 else 0.0
	_subtitle.text = tr("HARVESTPLAN_SUBTITLE") % [pens.size(), SimFormatters.integer(fish), SimFormatters.format_fish_weight(mean)]
	if not open_order.is_empty():
		_status.text = tr("HARVESTPLAN_STATUS_ORDERED")
		_status.theme_type_variation = &"TagInfo"
	elif fish == 0:
		_status.text = tr("HARVESTPLAN_STATUS_EMPTY")
		_status.theme_type_variation = &"TagMuted"
	elif mean < SMALL_FISH_KG:
		_status.text = tr("HARVESTPLAN_STATUS_SMALL")
		_status.theme_type_variation = &"TagWarn"
	else:
		_status.text = tr("HARVESTPLAN_STATUS_READY")
		_status.theme_type_variation = &"TagOk"
	_warning.visible = fish > 0 and mean < SMALL_FISH_KG
	if _warning.visible:
		_warning.text = tr("HARVESTPLAN_WARN_SMALL") % [SimFormatters.format_fish_weight(mean), SimFormatters.num(SMALL_FISH_KG, 1)]
	var has_report := not HarvestReport.latest_key_for(_sim, _site_id).is_empty()
	report_button.visible = has_report


func _fill_order_panel() -> void:
	_order_panel.visible = not open_order.is_empty()
	if open_order.is_empty():
		return
	var slot := int(open_order.get("slot_day", 0))
	var today := int(_sim.current_day)
	var status := str(open_order.get("status", "Fasting"))
	_order_title.text = tr("HARVESTPLAN_ORDER_" + status.to_upper()) % SimFormatters.format_day(slot)
	_order_detail.text = tr("HARVESTPLAN_ORDER_DETAIL") % [
		SimFormatters.integer(int(open_order.get("fish_harvested", 0))),
		SimFormatters.integer(int(open_order.get("fish_planned", 0))),
		maxi(0, slot - today)]


func _fill_details(p: Dictionary) -> void:
	var waits: Array = _options.get("waits", [0])
	var wait := int(waits[shown_wait]) if shown_wait < waits.size() else 0
	_shown_tag.text = _wait_label(wait)
	_shown_tag.theme_type_variation = &"TagInfo" if wait == 0 else &"TagMuted"
	size_chart.show_classes(p.get("size_classes", []), _options.get("size_labels", []),
		_options.get("size_factors", []), int(_options.get("reference_class", 4)))
	grade_bar.shares = [float(p.get("superior", 0.0)), float(p.get("ordinary", 0.0)), float(p.get("production", 0.0))]
	_grade_legend.text = tr("HARVESTPLAN_GRADE_LEGEND") % [SimFormatters.percent(float(p.get("superior", 0.0))),
		SimFormatters.percent(float(p.get("ordinary", 0.0))), SimFormatters.percent(float(p.get("production", 0.0)))]

	_clear(_price_grid)
	_pair(_price_grid, tr("HARVESTPLAN_ROW_SPOT_TODAY"), SimFormatters.format_price_kg(float(p.get("spot_today", 0.0))))
	_pair(_price_grid, tr("HARVESTPLAN_ROW_EXPECTED"), SimFormatters.format_price_kg(float(p.get("expected_price", 0.0))), &"FigureInfo")
	_pair(_price_grid, tr("HARVESTPLAN_ROW_FACTOR"),
		"×" + SimFormatters.num(float(p.get("size_factor", 1.0)) * float(p.get("grade_factor", 1.0)), 2))
	var contract_kg := float(p.get("contract_hog_kg", 0.0))
	if contract_kg > 0.0:
		_pair(_price_grid, tr("HARVESTPLAN_ROW_CONTRACT") % SimFormatters.format_biomass(contract_kg), _nok(float(p.get("contract_nok", 0.0))))
	_pair(_price_grid, tr("HARVESTPLAN_ROW_SPOT") % SimFormatters.format_biomass(float(p.get("spot_hog_kg", 0.0))), _nok(float(p.get("spot_nok", 0.0))))
	if absf(float(p.get("vap", 1.0)) - 1.0) > 1e-6:
		_pair(_price_grid, tr("HARVESTPLAN_ROW_VAP"), "×" + SimFormatters.num(float(p.get("vap", 1.0)), 2))

	_clear(_logistics_grid)
	var first := int(p.get("first_day", 0))
	var last := int(p.get("last_day", 0))
	var today := int(_sim.current_day)
	_pair(_logistics_grid, tr("HARVESTPLAN_ROW_ORDER_DAY"), _day_text(int(p.get("order_day", today)), today))
	_pair(_logistics_grid, tr("HARVESTPLAN_ROW_FASTING"), tr("HARVESTPLAN_FASTING_VALUE") % [int(p.get("fasting_days", 0)),
		SimFormatters.num(float(_options.get("fasting_degree_days", 30.0)), 0)])
	_pair(_logistics_grid, tr("HARVESTPLAN_ROW_WELLBOAT"), _day_text(int(p.get("slot_day", today)), today), &"FigureInfo")
	_pair(_logistics_grid, tr("HARVESTPLAN_ROW_SLAUGHTER_DAYS"), SimFormatters.format_day(first) if first == last
		else tr("HARVESTPLAN_DAY_RANGE") % [SimFormatters.format_day(first), SimFormatters.format_day(last)])
	_pair(_logistics_grid, tr("HARVESTPLAN_ROW_CAPACITY"), tr("HARVESTPLAN_CAPACITY_VALUE") % SimFormatters.num(float(_options.get("capacity_t", 150.0)), 0))

	_clear(_settle_grid)
	_pair(_settle_grid, tr("HARVESTPLAN_ROW_FISH"), SimFormatters.integer(int(p.get("fish", 0))) + " · "
		+ SimFormatters.format_fish_weight(float(p.get("mean_kg", 0.0))))
	_pair(_settle_grid, tr("HARVESTPLAN_ROW_HOG"), SimFormatters.format_biomass(float(p.get("hog_kg", 0.0))))
	_pair(_settle_grid, tr("HARVESTPLAN_ROW_GROSS"), _nok(float(p.get("gross_nok", 0.0))), &"FigureLarge")
	_pair(_settle_grid, tr("HARVESTPLAN_ROW_TRANSPORT"), _nok(-float(p.get("transport_nok", 0.0))))
	_pair(_settle_grid, tr("HARVESTPLAN_ROW_SLAUGHTER"), _nok(-float(p.get("slaughter_nok", 0.0))))
	_pair(_settle_grid, tr("HARVESTPLAN_ROW_NET"), _nok(float(p.get("net_nok", 0.0))), &"FigureLargeOk")
	_pair(_settle_grid, tr("HARVESTPLAN_ROW_FEED"), _nok(-float(p.get("feed_nok", 0.0))))
	_pair(_settle_grid, tr("HARVESTPLAN_ROW_NET_AFTER_FEED"), _nok(float(p.get("net_after_feed_nok", 0.0))), &"FigureLarge")


## The comparison: one column per wait, the best net after feed tagged.
func _fill_waits() -> void:
	_clear(_wait_grid)
	wait_buttons.clear()
	var waits: Array = _options.get("waits", [0])
	_wait_grid.columns = waits.size() + 1
	var best := best_wait()
	_cell(_wait_grid, "", &"CaptionMuted")
	for i in waits.size():
		var b := Button.new()
		b.theme_type_variation = &"SmallButton"
		b.toggle_mode = true
		b.text = _wait_label(int(waits[i])) + ("\n" + tr("HARVESTPLAN_BEST") if i == best else "")
		b.set_pressed_no_signal(i == shown_wait)
		b.tooltip_text = tr("HARVESTPLAN_WAIT_TIP")
		b.custom_minimum_size.x = 76.0
		b.pressed.connect(_on_wait_pressed.bind(i))
		_wait_grid.add_child(b)
		wait_buttons.append(b)
	var rows := [
		["HARVESTPLAN_WROW_DAYS", func(p: Dictionary) -> String: return _short_day(int(p.get("first_day", 0)))],
		["HARVESTPLAN_WROW_MEAN", func(p: Dictionary) -> String: return SimFormatters.format_fish_weight(float(p.get("mean_kg", 0.0)))],
		["HARVESTPLAN_WROW_HOG", func(p: Dictionary) -> String: return SimFormatters.format_biomass(float(p.get("hog_kg", 0.0)))],
		["HARVESTPLAN_WROW_PRICE", func(p: Dictionary) -> String: return SimFormatters.num(float(p.get("expected_price", 0.0)), 1)],
		["HARVESTPLAN_WROW_NET", func(p: Dictionary) -> String: return SimFormatters.format_nok(float(p.get("net_nok", 0.0)))],
		["HARVESTPLAN_WROW_FEED", func(p: Dictionary) -> String: return SimFormatters.format_nok(-float(p.get("feed_nok", 0.0)))],
		["HARVESTPLAN_WROW_RESULT", func(p: Dictionary) -> String: return SimFormatters.format_nok(float(p.get("net_after_feed_nok", 0.0)))],
	]
	for row in rows:
		_cell(_wait_grid, tr(row[0]), &"BodySmall")
		for i in projections.size():
			var p: Dictionary = projections[i]
			var text: String = (row[1] as Callable).call(p) if bool(p.get("success", false)) else "–"
			var strong: bool = row[0] == "HARVESTPLAN_WROW_RESULT"
			var variation: StringName = (&"FigureOk" if i == best else &"Figure") if strong else &"FigureSmall"
			_cell(_wait_grid, text, variation, HORIZONTAL_ALIGNMENT_RIGHT)
	var p0: Dictionary = projections[0]
	_wait_note.text = tr("HARVESTPLAN_WAIT_NOTE") % SimFormatters.percent(float(p0.get("daily_mortality", 0.0)), 3)


## Index of the wait with the best net after the feed (0 = now).
func best_wait() -> int:
	var best := 0
	for i in projections.size():
		if bool(projections[i].get("success", false)) and \
				float(projections[i].get("net_after_feed_nok", 0.0)) > float(projections[best].get("net_after_feed_nok", 0.0)):
			best = i
	return best


func _on_wait_pressed(i: int) -> void:
	shown_wait = i
	for j in wait_buttons.size():
		wait_buttons[j].set_pressed_no_signal(j == i)
	if i < projections.size() and bool(projections[i].get("success", false)):
		_fill_details(projections[i])
	_fill_footer(true)


func _fill_footer(ok: bool) -> void:
	var now: Dictionary = projections[0] if not projections.is_empty() else {}
	var why := ""
	if _picked.is_empty():
		why = tr("HARVESTPLAN_WHY_NO_PENS")
	elif not ok:
		var key := "HARVESTPLAN_ERROR_" + str(now.get("error", "")).to_upper()
		why = tr(key) if tr(key) != key else str(now.get("message", ""))
	if ok:
		order_button.text = tr("HARVESTPLAN_ORDER") % SimFormatters.format_day(int(now.get("slot_day", 0)))
		order_button.set_meta(&"enabled_tooltip", tr("HARVESTPLAN_ORDER_TIP") % SimFormatters.format_nok(float(now.get("net_nok", 0.0))))
	else:
		order_button.text = tr("HARVESTPLAN_ORDER") % "–"
	UIPolish.set_available(order_button, why)
	var express_ok := bool(express_quote.get("success", false))
	express_button.text = tr("HARVESTPLAN_EXPRESS") % SimFormatters.format_nok(float(express_quote.get("net_nok", 0.0))) \
		if express_ok else tr("HARVESTPLAN_EXPRESS") % "–"
	express_button.set_meta(&"enabled_tooltip", tr("HARVESTPLAN_EXPRESS_TIP") % [
		SimFormatters.format_price_kg(float(express_quote.get("reference_price", 0.0))),
		SimFormatters.format_nok(float(express_quote.get("surcharge_nok", 0.0)))])
	UIPolish.set_available(express_button, why if why != "" else ("" if express_ok else str(express_quote.get("message", ""))))
	var waits: Array = _options.get("waits", [0])
	var shown_days := int(waits[shown_wait]) if shown_wait < waits.size() else 0
	_footer_note.text = tr("HARVESTPLAN_FOOTER") if shown_days == 0 else tr("HARVESTPLAN_FOOTER_WAIT") % (shown_days / 7)


# ── Actions ────────────────────────────────────────────────────────────────────

## "Bestill slakting": the planned order for the picked pens and share. The
## sheet closes and the wellboat sails out to its slot.
func _on_order() -> void:
	var result: Dictionary = _sim.order_harvest(_site_id, _picked_ids(), _share())
	if bool(result.get("success", false)):
		close("order")
	else:
		_toast_error(result)
		refresh()


## "Ekspress i dag": sold today with the surcharge; the report opens.
func _on_express() -> void:
	var result: Dictionary = _sim.harvest_express(_site_id, _picked_ids(), _share())
	if not bool(result.get("success", false)):
		_toast_error(result)
		refresh()
		return
	close("express")
	var sheet := HarvestScreens.report(get_tree())
	if sheet != null:
		sheet.show_harvest(str(result.get("report_id", "")))


func _on_cancel_order() -> void:
	if open_order.is_empty():
		return
	_sim.cancel_harvest_order(str(open_order.get("id", "")))
	_load_pens()
	_picked.clear()
	for p in pens:
		if not bool(p.get("in_order", false)):
			_picked.append(str(p.get("id", "")))
	_build_pen_chips()
	refresh()


func _on_report() -> void:
	var sheet := HarvestScreens.report(get_tree())
	if sheet != null:
		sheet.show_latest_for(_site_id)


func _toast_error(result: Dictionary) -> void:
	var key := "HARVESTPLAN_ERROR_" + str(result.get("error", "")).to_upper()
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr != null:
		mgr.show_toast(tr(key) if tr(key) != key else str(result.get("message", "")), "WARNING", 5.0)


# ── Cells ──────────────────────────────────────────────────────────────────────

func _wait_label(days: int) -> String:
	return tr("HARVESTPLAN_WAIT_NOW") if days == 0 else tr("HARVESTPLAN_WAIT_WEEKS") % (days / 7)


func _day_text(day: int, today: int) -> String:
	if day <= today:
		return tr("HARVESTPLAN_TODAY")
	return tr("HARVESTPLAN_DAY_IN") % [SimFormatters.format_day(day), day - today]


## "12. nov." without the year, for the narrow comparison columns.
func _short_day(day: int) -> String:
	var d := SimFormatters.date_for_day(day)
	return "%d. %s" % [int(d["day"]), SimFormatters.month_name(int(d["month"]))]


func _clear(grid: GridContainer) -> void:
	for child in grid.get_children():
		grid.remove_child(child)
		child.queue_free()


func _pair(grid: GridContainer, caption: String, value: String, variation: StringName = &"Figure") -> void:
	_cell(grid, caption, &"BodySmall")
	_cell(grid, value, variation, HORIZONTAL_ALIGNMENT_RIGHT)


func _cell(grid: GridContainer, text: String, variation: StringName, align := HORIZONTAL_ALIGNMENT_LEFT) -> Label:
	var label := Label.new()
	label.text = text
	label.theme_type_variation = variation
	label.horizontal_alignment = align
	if align == HORIZONTAL_ALIGNMENT_LEFT:
		label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	grid.add_child(label)
	return label


static func _nok(amount: float) -> String:
	return SimFormatters.num(roundf(amount), 0) + " NOK"
