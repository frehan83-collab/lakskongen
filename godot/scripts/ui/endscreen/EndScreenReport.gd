class_name EndScreenReport
extends RefCounted

## EndScreenReport — the report sheet of the end screen (roadmap U2-03), on
## Chart paper: the key figures, the P&L story (PnlStoryChart) with its three
## turning points, where the money went (the largest ledger lines), where the
## fish went (the S2-05 mortality ledger) and the game's event log. Built from the engine's end report and mortality
## breakdown; every amount is the ledger's own (the turning points reconcile
## to the bookings, ScenarioReportTests). Returns the nodes tests read.

const LOG_ROWS := 40
const MORTALITY_ROWS := 5
const LINE_ROWS := 5
const KPI_MIN_W := 150.0
const POINT_MIN_W := 180.0


## Fill `parent` with the report. Returns {chart, points: [PanelContainer],
## kpis: {id: Label}, money_rows, mortality_rows, log_rows: [Control]}.
static func build(parent: VBoxContainer, report: Dictionary, mortality: Dictionary) -> Dictionary:
	for child in parent.get_children():
		child.queue_free()
	var out := {"chart": null, "points": [], "kpis": {}, "money_rows": [], "mortality_rows": [], "log_rows": []}
	parent.add_child(_header(report))
	parent.add_child(_kpis(report, out["kpis"]))
	parent.add_child(_section_title("UI_ENDSCREEN_STORY_TITLE", "UI_ENDSCREEN_STORY_HINT"))
	var chart := PnlStoryChart.new()
	chart.name = "PnlStoryChart"
	chart.custom_minimum_size = Vector2(420, 250)
	chart.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	chart.report = report
	parent.add_child(chart)
	out["chart"] = chart
	parent.add_child(_turning_points(report, out["points"]))
	var lower := HBoxContainer.new()
	lower.name = "Lower"
	lower.add_theme_constant_override("separation", 28)
	parent.add_child(lower)
	lower.add_child(_money(report, out["money_rows"]))
	lower.add_child(_mortality(report, mortality, out["mortality_rows"]))
	lower.add_child(_log(report, out["log_rows"]))
	return out


# ── Header and key figures ─────────────────────────────────────────────────────

static func _header(report: Dictionary) -> Control:
	var box := VBoxContainer.new()
	box.name = "Header"
	box.add_theme_constant_override("separation", 2)
	box.add_child(_label(tr_key("UI_ENDSCREEN_SHEET_OVERLINE"), &"OverlineMuted", "Overline", true))
	var start := int(report.get("start_day", 0))
	var end := int(report.get("end_day", 0))
	box.add_child(_label(TranslationServer.translate("UI_ENDSCREEN_SHEET_PERIOD") % [
		SimFormatters.format_day(start), SimFormatters.format_day(end), SimFormatters.integer(maxi(end - start, 0))],
		&"BodySmallMuted", "Period"))
	return box


static func _kpis(report: Dictionary, kpis: Dictionary) -> Control:
	var grid := HFlowContainer.new()
	grid.name = "Kpis"
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 12)
	var net := float(report.get("net_result", 0.0))
	var closing := float(report.get("closing_cash", 0.0))
	var mortality := float(report.get("mortality_share", 0.0))
	var calls := int(report.get("bank_calls", 0))
	var warnings := int(report.get("liquidity_warnings", 0))
	_kpi(grid, kpis, "result", "UI_ENDSCREEN_KPI_RESULT", SimFormatters.format_nok(net), &"Ok" if net >= 0.0 else &"Crit",
		tr_key("UI_ENDSCREEN_KPI_RESULT_SUB") % [SimFormatters.format_nok(float(report.get("revenue", 0.0))),
			SimFormatters.format_nok(float(report.get("costs", 0.0)))])
	_kpi(grid, kpis, "cash", "UI_ENDSCREEN_KPI_CASH", SimFormatters.format_nok(closing), &"" if closing >= 0.0 else &"Crit",
		tr_key("UI_ENDSCREEN_KPI_CASH_SUB") % [SimFormatters.format_nok(float(report.get("lowest_cash", 0.0))),
			SimFormatters.integer(int(report.get("lowest_cash_day", 0)))])
	_kpi(grid, kpis, "harvest", "UI_ENDSCREEN_KPI_HARVEST",
		SimFormatters.format_biomass(float(report.get("harvested_tonnes_hog", 0.0)) * 1000.0), &"",
		tr_key("UI_ENDSCREEN_KPI_HARVEST_SUB") % SimFormatters.integer(int(report.get("harvest_batches", 0))))
	_kpi(grid, kpis, "mortality", "UI_ENDSCREEN_KPI_MORTALITY", SimFormatters.percent(mortality, 1),
		&"Crit" if mortality > 0.25 else (&"Warn" if mortality > 0.15 else &""),
		tr_key("UI_ENDSCREEN_KPI_MORTALITY_SUB") % SimFormatters.integer(int(report.get("fish_died", 0))))
	var bank_value := tr_key("UI_ENDSCREEN_KPI_BANK_CALM") if calls == 0 and warnings == 0 \
		else "%s · %s" % [count_text(warnings, "UI_ENDSCREEN_KPI_WARNING"), count_text(calls, "UI_ENDSCREEN_KPI_CALL")]
	_kpi(grid, kpis, "bank", "UI_ENDSCREEN_KPI_BANK", bank_value, &"Crit" if calls > 0 else (&"Warn" if warnings > 0 else &""),
		tr_key("UI_ENDSCREEN_KPI_BANK_SUB") % SimFormatters.format_nok(float(report.get("overdraft_interest", 0.0))))
	return grid


static func _kpi(grid: Control, kpis: Dictionary, id: String, caption_key: String, value: String, tone: StringName, sub: String) -> void:
	var card := PanelContainer.new()
	card.name = "Kpi_" + id
	card.theme_type_variation = &"CardFlat"
	card.custom_minimum_size = Vector2(KPI_MIN_W, 0)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 2)
	card.add_child(box)
	box.add_child(_label(tr_key(caption_key), &"OverlineMuted", "Caption", true))
	var v := _label(value, StringName("KpiSmall" + String(tone)), "Value")
	box.add_child(v)
	var s := _label(sub, &"SmallMuted", "Sub")
	box.add_child(s)
	grid.add_child(card)
	kpis[id] = v


# ── Turning points ─────────────────────────────────────────────────────────────

static func _turning_points(report: Dictionary, cards: Array) -> Control:
	var box := VBoxContainer.new()
	box.name = "TurningPoints"
	box.add_theme_constant_override("separation", 8)
	box.add_child(_section_title("UI_ENDSCREEN_POINTS_TITLE", ""))
	var row := HBoxContainer.new()
	row.name = "Row"
	row.add_theme_constant_override("separation", 12)
	box.add_child(row)
	var tps: Array = report.get("turning_points", [])
	if tps.is_empty():
		row.add_child(_label(tr_key("UI_ENDSCREEN_POINTS_NONE"), &"BodySmallMuted", "None"))
	for i in tps.size():
		var card := _point_card(i + 1, tps[i])
		row.add_child(card)
		cards.append(card)
	return box


static func _point_card(number: int, tp: Dictionary) -> PanelContainer:
	var income := bool(tp.get("income", false))
	var card := PanelContainer.new()
	card.name = "Point_%d" % number
	card.theme_type_variation = &"CardFlat"
	card.custom_minimum_size = Vector2(POINT_MIN_W, 0)
	card.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 10)
	card.add_child(row)
	var badge := PanelContainer.new()
	badge.theme_type_variation = &"BadgeOk" if income else &"BadgeCrit"
	badge.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	badge.add_child(_label(str(number), &"BodyStrong", "Number"))
	row.add_child(badge)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 1)
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(box)
	var first := int(tp.get("first_day", 0))
	var last := int(tp.get("last_day", first))
	var when := tr_key("UI_ENDSCREEN_POINT_DAY") % [first, SimFormatters.format_day(first)] if first == last \
		else tr_key("UI_ENDSCREEN_POINT_DAYS") % [first, last, SimFormatters.format_day(first)]
	box.add_child(_label(when, &"SmallMuted", "When"))
	box.add_child(_label(SimLabels.ledger_category(str(tp.get("category", "other"))), &"BodyStrong", "Category"))
	var amount := float(tp.get("amount", 0.0))
	box.add_child(_label(signed_nok(amount), &"KpiSmallOk" if income else &"KpiSmallCrit", "Amount"))
	var revenue := float(tp.get("revenue", 0.0))
	var costs := float(tp.get("costs", 0.0))
	var detail := count_text(int(tp.get("bookings", 0)), "UI_ENDSCREEN_POINT_BOOKING")
	if revenue > 0.0 and costs > 0.0:
		detail = tr_key("UI_ENDSCREEN_POINT_PARTS") % [SimFormatters.format_nok(revenue), SimFormatters.format_nok(costs)] + " · " + detail
	box.add_child(_label(detail, &"SmallMuted", "Bookings", false, true))
	return card


# ── Where the money went ───────────────────────────────────────────────────────

static func _money(report: Dictionary, rows_out: Array) -> Control:
	var box := VBoxContainer.new()
	box.name = "Money"
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	box.add_theme_constant_override("separation", 6)
	box.add_child(_section_title("UI_ENDSCREEN_MONEY_TITLE", ""))
	var lines: Array = report.get("lines", [])
	var peak := 0.0
	for l in lines:
		peak = maxf(peak, absf(float(l.get("amount", 0.0))))
	if lines.is_empty():
		box.add_child(_label(tr_key("UI_ENDSCREEN_MONEY_NONE"), &"BodySmallMuted", "None"))
	var shown: Array = lines.slice(0, LINE_ROWS)
	for l in shown:
		var income := bool(l.get("income", false))
		var amount := float(l.get("amount", 0.0))
		var row := VBoxContainer.new()
		row.name = "Line_%s_%s" % [str(l.get("category", "")).validate_node_name(), "in" if income else "out"]
		row.add_theme_constant_override("separation", 2)
		var head := HBoxContainer.new()
		var name := _label(line_label(str(l.get("category", "other")), income, shown), &"BodySmall", "Name")
		name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name.clip_text = true
		head.add_child(name)
		head.add_child(_label(signed_nok(amount), &"FigureSmall", "Amount"))
		row.add_child(head)
		var bar := ShareBar.new()
		bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		bar.share = absf(amount) / peak if peak > 0.0 else 0.0
		row.add_child(bar)
		bar.fill_color = bar.get_theme_color(&"ok" if income else &"crit", &"Palette")
		box.add_child(row)
		rows_out.append(row)
	return box


# ── Where the fish went ────────────────────────────────────────────────────────

static func _mortality(report: Dictionary, breakdown: Dictionary, rows_out: Array) -> Control:
	var box := VBoxContainer.new()
	box.name = "Mortality"
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	box.add_theme_constant_override("separation", 6)
	box.add_child(_section_title("UI_ENDSCREEN_FISH_TITLE", ""))
	var died := int(report.get("fish_died", 0))
	var harvested := 0
	for r in breakdown.get("left_alive", []):
		if str(r.get("id", "")) in ["harvest", "forced_harvest"]:
			harvested += int(r.get("fish", 0))
	box.add_child(_label(tr_key("UI_ENDSCREEN_FISH_SUMMARY") % [SimFormatters.integer(died),
		SimFormatters.percent(float(report.get("mortality_share", 0.0)), 1), SimFormatters.integer(harvested),
		SimFormatters.integer(int(report.get("fish_in_sea", 0)))], &"BodySmall", "Summary", false, true))
	var cats: Array = (breakdown.get("categories", []) as Array).filter(func(c: Dictionary) -> bool: return int(c.get("fish", 0)) > 0)
	cats.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return int(a.get("fish", 0)) > int(b.get("fish", 0)))
	if cats.is_empty():
		box.add_child(_label(tr_key("UI_ENDSCREEN_FISH_NONE"), &"BodySmallMuted", "None"))
	for c in cats.slice(0, MORTALITY_ROWS):
		var row := HBoxContainer.new()
		row.name = "Cause_" + str(c.get("id", "")).validate_node_name()
		row.add_theme_constant_override("separation", 8)
		var name := _label(TranslationServer.translate(str(c.get("label_key", ""))), &"BodySmall", "Name")
		name.custom_minimum_size = Vector2(150, 0)
		name.clip_text = true
		row.add_child(name)
		var bar := ShareBar.new()
		bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		bar.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		bar.share = float(c.get("share", 0.0))
		row.add_child(bar)
		bar.fill_color = bar.get_theme_color(&"crit", &"Palette")
		var fish := _label(SimFormatters.integer(int(c.get("fish", 0))), &"FigureSmall", "Fish")
		fish.custom_minimum_size = Vector2(70, 0)
		fish.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		row.add_child(fish)
		box.add_child(row)
		rows_out.append(row)
	return box


# ── Event log ──────────────────────────────────────────────────────────────────

static func _log(report: Dictionary, rows_out: Array) -> Control:
	var box := VBoxContainer.new()
	box.name = "EventLog"
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	box.add_theme_constant_override("separation", 6)
	box.add_child(_section_title("UI_ENDSCREEN_LOG_TITLE", ""))
	var scroll := ScrollContainer.new()
	scroll.name = "LogScroll"
	scroll.custom_minimum_size = Vector2(0, 200)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	box.add_child(scroll)
	var list := VBoxContainer.new()
	list.name = "LogList"
	list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	list.add_theme_constant_override("separation", 8)
	scroll.add_child(list)
	var entries: Array = report.get("log", [])
	if entries.is_empty():
		list.add_child(_label(tr_key("UI_ENDSCREEN_LOG_NONE"), &"BodySmallMuted", "None"))
	for e in entries.slice(0, LOG_ROWS):
		var row := VBoxContainer.new()
		row.add_theme_constant_override("separation", 0)
		var day := int(e.get("day", 0))
		row.add_child(_label(tr_key("UI_ENDSCREEN_LOG_DAY") % [day, SimFormatters.format_day(day)], &"SmallMuted", "Day"))
		row.add_child(_label(str(e.get("name", "")), &"BodySmallStrong", "Name"))
		var desc := _label(str(e.get("description", "")), &"BodySmall", "Description", false, true)
		desc.max_lines_visible = 3
		desc.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		row.add_child(desc)
		list.add_child(row)
		rows_out.append(row)
	return box


# ── Helpers ────────────────────────────────────────────────────────────────────

static func _section_title(title_key: String, hint_key: String) -> Control:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 0)
	box.add_child(_label(tr_key(title_key), &"Subheading", "Title"))
	if hint_key != "":
		box.add_child(_label(tr_key(hint_key), &"BodySmallMuted", "Hint", false, true))
	return box


static func _label(text: String, variation: StringName, node_name: String, upper: bool = false, wrap: bool = false) -> Label:
	var l := Label.new()
	l.name = node_name
	l.text = text
	l.theme_type_variation = variation
	l.uppercase = upper
	if wrap:
		l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		l.custom_minimum_size = Vector2(240, 0)
	return l


static func tr_key(key: String) -> String:
	return TranslationServer.translate(key)


## A ledger line's name: a cost line with its own name (LEDGER_HARVEST_COST,
## "Slakting og transport"), else the line's name, marked income or costs
## when both sides of the line are listed.
static func line_label(category: String, income: bool, lines: Array) -> String:
	var own := "LEDGER_%s_%s" % [category.to_upper(), "INCOME" if income else "COST"]
	if tr_key(own) != own:
		return tr_key(own)
	# The other side named apart already tells the two lines apart.
	var other := "LEDGER_%s_%s" % [category.to_upper(), "COST" if income else "INCOME"]
	var both := tr_key(other) == other \
		and lines.filter(func(l: Dictionary) -> bool: return str(l.get("category", "")) == category).size() > 1
	var base := SimLabels.ledger_category(category)
	return tr_key("UI_ENDSCREEN_LINE_INCOME" if income else "UI_ENDSCREEN_LINE_COST") % base if both else base


## "1 postering" / "3 posteringer": `stem`_ONE or `stem`_MANY with the count.
static func count_text(n: int, stem: String) -> String:
	return tr_key(stem + ("_ONE" if n == 1 else "_MANY")) % SimFormatters.integer(n)


## An amount with its sign: "+71,13 MNOK", "-4,64 MNOK".
static func signed_nok(amount: float) -> String:
	return ("+" if amount > 0.0 else "") + SimFormatters.format_nok(amount)
