class_name CashRunwayChart
extends VBoxContainer

## The build preview's 36-month cash runway (roadmap F2-31,
## docs/FARM_TYPES_SPEC.md §6.2 step 4): the company's cash at each month's
## end with the build's payments and the median overrun (solid), down to the
## 90th-percentile overrun (the shaded band), and with the bank's pre-filled
## loan (dashed); the zero line and the first harvest are marked. Every point
## is the engine's (the preview's "economics.runway"); the chart only draws.

const PLOT_HEIGHT := 150.0

var _plot: RunwayPlot = null
var _top: Label = null
var _bottom: Label = null
var _legend: Label = null
var _low: Label = null


func _ready() -> void:
	if _plot == null:
		_build()


func _build() -> void:
	add_theme_constant_override("separation", 4)
	var head := HBoxContainer.new()
	head.add_theme_constant_override("separation", 8)
	add_child(head)
	var title := Label.new()
	title.name = "Title"
	title.text = tr("BUILD_RUNWAY_TITLE")
	title.theme_type_variation = &"OverlineMuted"
	title.uppercase = true
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(title)
	_top = Label.new()
	_top.name = "Top"
	_top.theme_type_variation = &"FigureSmall"
	head.add_child(_top)
	_plot = RunwayPlot.new()
	_plot.name = "Plot"
	_plot.custom_minimum_size = Vector2(320, PLOT_HEIGHT)
	_plot.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(_plot)
	var foot := HBoxContainer.new()
	foot.add_theme_constant_override("separation", 8)
	add_child(foot)
	_bottom = Label.new()
	_bottom.name = "Bottom"
	_bottom.theme_type_variation = &"FigureSmall"
	_bottom.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	foot.add_child(_bottom)
	_low = Label.new()
	_low.name = "Low"
	_low.theme_type_variation = &"BodySmall"
	foot.add_child(_low)
	_legend = Label.new()
	_legend.name = "Legend"
	_legend.theme_type_variation = &"SmallMuted"
	_legend.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_legend.custom_minimum_size = Vector2(300, 0)
	add_child(_legend)


## Draw `economics` (the preview's "economics").
func show_runway(economics: Dictionary) -> void:
	if _plot == null:
		_build()
	var months: Array = economics.get("runway", [])
	_plot.set_data(months, int(economics.get("first_harvest_day", -1)), float(economics.get("loan_nok", 0.0)) > 0.0)
	_top.text = SimFormatters.format_nok(_plot.top)
	_bottom.text = SimFormatters.format_nok(_plot.bottom)
	var low := float(economics.get("lowest_cash_p50_nok", 0.0))
	var low_p90 := float(economics.get("lowest_cash_p90_nok", 0.0))
	_low.text = tr("BUILD_RUNWAY_LOW") % [SimFormatters.format_nok(low), SimFormatters.format_nok(low_p90), int(economics.get("lowest_month", 0))]
	_low.theme_type_variation = &"BodySmallCrit" if low_p90 < 0.0 else &"BodySmall"
	var legend := tr("BUILD_RUNWAY_LEGEND")
	if float(economics.get("loan_nok", 0.0)) > 0.0:
		legend += " " + tr("BUILD_RUNWAY_LEGEND_LOAN") % [SimFormatters.format_nok(float(economics.get("loan_nok", 0.0))),
			SimFormatters.percent(float(economics.get("loan_rate", 0.0)), 1), int(float(economics.get("loan_term_months", 0)) / SimFormatters.MONTHS_PER_YEAR)]
	_legend.text = legend


## The plot's drawn series (tests read them).
func plot() -> RunwayPlot:
	return _plot


## The chart area: the band, the lines, zero and the first harvest.
class RunwayPlot extends Control:
	var p50: PackedFloat64Array = []
	var p90: PackedFloat64Array = []
	var loan: PackedFloat64Array = []
	var days: PackedInt32Array = []
	var harvest_day := -1
	var show_loan := false
	var top := 0.0
	var bottom := 0.0

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		resized.connect(queue_redraw)

	func set_data(months: Array, first_harvest_day: int, with_loan: bool) -> void:
		p50.clear()
		p90.clear()
		loan.clear()
		days.clear()
		for m in months:
			p50.append(float(m.get("cash_p50_nok", 0.0)))
			p90.append(float(m.get("cash_p90_nok", 0.0)))
			loan.append(float(m.get("cash_with_loan_nok", 0.0)))
			days.append(int(m.get("day", 0)))
		harvest_day = first_harvest_day
		show_loan = with_loan
		top = 0.0
		bottom = 0.0
		for arr in [p50, p90, loan if with_loan else PackedFloat64Array()]:
			for v in arr:
				top = maxf(top, v)
				bottom = minf(bottom, v)
		if is_equal_approx(top, bottom):
			top = bottom + 1.0
		queue_redraw()

	func _y(v: float) -> float:
		return 6.0 + (size.y - 12.0) * (top - v) / (top - bottom)

	func _x(i: int) -> float:
		return 8.0 + (size.x - 16.0) * float(i) / maxf(1.0, float(p50.size() - 1))

	func _draw() -> void:
		if p50.size() < 2 or size.x < 40.0:
			return
		var info := LakskongenTheme.palette(self, &"info")
		var ok := LakskongenTheme.palette(self, &"ok")
		var crit := LakskongenTheme.palette(self, &"crit")
		var line := LakskongenTheme.palette(self, &"hairline")
		draw_rect(Rect2(Vector2.ZERO, size), Color(line, 0.25), false, 1.0)
		var zero := _y(0.0)
		draw_dashed_line(Vector2(0, zero), Vector2(size.x, zero), Color(crit, 0.8), 1.5, 6.0)
		# The band month by month, as quads (a single polygon fails to
		# triangulate where the two lines meet).
		for i in p50.size() - 1:
			var h0 := _y(p90[i]) - _y(p50[i])
			var h1 := _y(p90[i + 1]) - _y(p50[i + 1])
			if h0 < 0.5 and h1 < 0.5:
				continue
			var quad := PackedVector2Array([Vector2(_x(i), _y(p50[i])), Vector2(_x(i + 1), _y(p50[i + 1])),
				Vector2(_x(i + 1), _y(p90[i + 1]) + (0.5 if h1 < 0.5 else 0.0)), Vector2(_x(i), _y(p90[i]) + (0.5 if h0 < 0.5 else 0.0))])
			draw_colored_polygon(quad, Color(info, 0.18))
		var mid := PackedVector2Array()
		for i in p50.size():
			mid.append(Vector2(_x(i), _y(p50[i])))
		draw_polyline(mid, info, 2.5, true)
		if show_loan:
			for i in loan.size() - 1:
				draw_dashed_line(Vector2(_x(i), _y(loan[i])), Vector2(_x(i + 1), _y(loan[i + 1])), ok, 2.0, 5.0)
		if harvest_day >= 0 and days.size() > 0 and harvest_day <= days[days.size() - 1]:
			var i := 0
			while i < days.size() - 1 and days[i] < harvest_day:
				i += 1
			var x := _x(i)
			draw_line(Vector2(x, 0), Vector2(x, size.y), Color(ok, 0.8), 1.5)
			draw_circle(Vector2(x, 6.0), 4.0, ok)
