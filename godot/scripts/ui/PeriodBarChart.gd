class_name PeriodBarChart
extends Control

## PeriodBarChart — income and costs per period as paired bars (roadmap U1-05).
##
## Every bar is money the sim booked: the periods are built by periods() from
## the year-to-date revenue and cost series (AnalyticsMetric revenue_nok and
## expenses_nok), so a game twelve days old shows twelve days, never six
## invented months. Pure _draw() on the surface's Palette colours; the text
## is the UI face at the 13 px floor. Hovering a period shows its amounts.

var labels: PackedStringArray = PackedStringArray():
	set(v): labels = v; queue_redraw()
var income: PackedFloat64Array = PackedFloat64Array():
	set(v): income = v; queue_redraw()
var costs: PackedFloat64Array = PackedFloat64Array():
	set(v): costs = v; queue_redraw()
## Shown when there is no period to draw.
var empty_text: String = "UI_CHART_NO_DATA":
	set(v): empty_text = v; queue_redraw()

const PAD_LEFT := 8.0
const PAD_RIGHT := 8.0
const PAD_TOP := 26.0
const LABEL_ROW := 20.0
const FONT_SIZE := 13
const BAR_GAP := 3.0
## A bar never grows wider than this share of its period's slot.
const BAR_SHARE := 0.32
const MIN_VISIBLE_PX := 1.0


func _ready() -> void:
	custom_minimum_size = Vector2(240, 140)
	mouse_filter = Control.MOUSE_FILTER_PASS
	tooltip_text = " "  # enables _get_tooltip


func set_periods(period_labels: PackedStringArray, period_income: PackedFloat64Array, period_costs: PackedFloat64Array) -> void:
	labels = period_labels
	income = period_income
	costs = period_costs


## The tallest bar's amount (the chart's scale).
func scale_max() -> float:
	var hi := 0.0
	for v in income:
		hi = maxf(hi, v)
	for v in costs:
		hi = maxf(hi, v)
	return hi


## The drawn rectangles, period by period: [{income: Rect2, cost: Rect2}].
## Tests read them to check that every height follows its amount.
func bar_rects() -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	var n := labels.size()
	if n == 0:
		return out
	var w := size.x - PAD_LEFT - PAD_RIGHT
	var h := size.y - PAD_TOP - LABEL_ROW
	var hi := scale_max()
	var slot := w / float(n)
	var bar_w := maxf(2.0, minf(slot * BAR_SHARE, 28.0))
	var base_y := PAD_TOP + h
	for i in n:
		var centre := PAD_LEFT + slot * (float(i) + 0.5)
		var rh := 0.0 if hi <= 0.0 else h * clampf(income[i] / hi, 0.0, 1.0)
		var ch := 0.0 if hi <= 0.0 else h * clampf(costs[i] / hi, 0.0, 1.0)
		out.append({
			"income": Rect2(centre - bar_w - BAR_GAP * 0.5, base_y - rh, bar_w, rh),
			"cost": Rect2(centre + BAR_GAP * 0.5, base_y - ch, bar_w, ch),
		})
	return out


func _draw() -> void:
	var font := get_theme_default_font()
	var w := size.x - PAD_LEFT - PAD_RIGHT
	var h := size.y - PAD_TOP - LABEL_ROW
	if w <= 0.0 or h <= 0.0:
		return
	_draw_legend(font)
	if labels.is_empty():
		draw_string(font, Vector2(PAD_LEFT, PAD_TOP + 20.0), tr(empty_text), HORIZONTAL_ALIGNMENT_LEFT, -1,
			FONT_SIZE, _pal(&"muted"))
		return
	var base_y := PAD_TOP + h
	# Light gridlines at the quarters of the scale, the baseline darker.
	for q in range(1, 4):
		var gy := PAD_TOP + h * (1.0 - float(q) / 4.0)
		draw_line(Vector2(PAD_LEFT, gy), Vector2(PAD_LEFT + w, gy), Color(_pal(&"hairline"), 0.6), 1.0)
	draw_line(Vector2(PAD_LEFT, base_y), Vector2(PAD_LEFT + w, base_y), _pal(&"stroke"), 1.0)
	var slot := w / float(labels.size())
	var rects := bar_rects()
	for i in rects.size():
		var r: Dictionary = rects[i]
		_draw_bar(r["income"], _pal(&"ok"))
		_draw_bar(r["cost"], _pal(&"crit"))
		var label := labels[i]
		var lw := font.get_string_size(label, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE).x
		var lx := PAD_LEFT + slot * (float(i) + 0.5) - lw * 0.5
		draw_string(font, Vector2(lx, base_y + 15.0), label, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE, _pal(&"muted"))
	# The scale: the tallest bar's amount at the top-left.
	var hi := scale_max()
	if hi > 0.0:
		draw_string(font, Vector2(PAD_LEFT, PAD_TOP - 6.0), SimFormatters.format_nok(hi),
			HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE, _pal(&"muted"))


func _draw_bar(rect: Rect2, colour: Color) -> void:
	if rect.size.y >= MIN_VISIBLE_PX:
		draw_rect(rect, colour)


## Two swatches at the top-right: Inntekter (ok green), Kostnader (crit red).
func _draw_legend(font: Font) -> void:
	var items := [[tr("UI_CHART_INCOME"), _pal(&"ok")], [tr("UI_CHART_COSTS"), _pal(&"crit")]]
	var x := size.x - PAD_RIGHT
	for idx in range(items.size() - 1, -1, -1):
		var text: String = items[idx][0]
		var tw := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE).x
		x -= tw
		draw_string(font, Vector2(x, 14.0), text, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE, _pal(&"text"))
		x -= 14.0
		draw_rect(Rect2(x, 5.0, 10.0, 10.0), items[idx][1])
		x -= 14.0


func _get_tooltip(at_position: Vector2) -> String:
	if labels.is_empty():
		return ""
	var w := size.x - PAD_LEFT - PAD_RIGHT
	var i := clampi(int((at_position.x - PAD_LEFT) / (w / float(labels.size()))), 0, labels.size() - 1)
	return "%s\n%s: %s\n%s: %s" % [labels[i], tr("UI_CHART_INCOME"), SimFormatters.format_nok(income[i]),
		tr("UI_CHART_COSTS"), SimFormatters.format_nok(costs[i])]


## A palette colour on the surface this chart is drawn on (Ink or Chart).
func _pal(key: StringName) -> Color:
	return get_theme_color(key, &"Palette")


# ── Data ──────────────────────────────────────────────────────────────────────

## Money booked each day from a year-to-date series: the rise from the day
## before; a fall is the annual close resetting the year, so that day's
## amount is its own value. The first value has no day before it unless the
## series starts with the game (from_start), when it rose from nothing.
static func daily_amounts(ytd: Array, from_start: bool = false) -> PackedFloat64Array:
	var out := PackedFloat64Array()
	if from_start and not ytd.is_empty():
		out.append(float(ytd[0]))
	for i in range(1, ytd.size()):
		var prev := float(ytd[i - 1])
		var now := float(ytd[i])
		out.append(now - prev if now >= prev else now)
	return out


## Income and costs per period from the two year-to-date series (oldest
## first, the newest value on `last_day`). block_days > 0 groups whole blocks
## of that many days ending on last_day ("Dag 8–12"); block_days == 0 groups
## calendar months ("jan.", "feb."). At most max_periods, the newest last.
## from_start: the series begin with the game (shorter than asked for), so
## their first values are the first day's bookings.
## Returns {labels, income, costs}.
static func periods(revenue_ytd: Array, cost_ytd: Array, last_day: int, block_days: int, max_periods: int,
		from_start: bool = false) -> Dictionary:
	var rev := daily_amounts(revenue_ytd, from_start)
	var cost := daily_amounts(cost_ytd, from_start)
	var n := mini(rev.size(), cost.size())
	var keys: Array = []
	var by_key: Dictionary = {}
	for i in n:
		# Amount i is the rise into series value i + 1, on that value's day.
		var day := last_day - (n - 1 - i)
		var key: Variant
		var label: String
		if block_days > 0:
			var block := (last_day - day) / block_days
			key = block
			var hi_day := last_day - block * block_days
			var lo_day := maxi(hi_day - block_days + 1, last_day - n + 1)
			label = TranslationServer.translate("UI_DAY_N") % hi_day if lo_day == hi_day \
				else TranslationServer.translate("UI_CHART_DAY_RANGE") % [lo_day, hi_day]
		else:
			var date := SimFormatters.date_for_day(maxi(day, 0))
			key = int(date["year"]) * 12 + int(date["month"])
			label = SimFormatters.month_name(int(date["month"]))
		if not by_key.has(key):
			by_key[key] = {"label": label, "income": 0.0, "costs": 0.0}
			keys.append(key)
		by_key[key]["income"] += rev[i]
		by_key[key]["costs"] += cost[i]
	# Keys arrive oldest first for months and newest-block-last for blocks.
	if block_days > 0:
		keys.sort()
		keys.reverse()
	var kept := keys.slice(maxi(keys.size() - max_periods, 0))
	var out := {"labels": PackedStringArray(), "income": PackedFloat64Array(), "costs": PackedFloat64Array()}
	for key in kept:
		out["labels"].append(by_key[key]["label"])
		out["income"].append(by_key[key]["income"])
		out["costs"].append(by_key[key]["costs"])
	return out
