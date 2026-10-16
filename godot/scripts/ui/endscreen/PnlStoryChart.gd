class_name PnlStoryChart
extends Control

## PnlStoryChart — the P&L story of a finished game (roadmap U2-03).
##
## The cash line over the whole game (the opening cash, then the cash at the
## end of every month, filled down to zero) with the bankruptcy floor dashed
## when the cash came near it, the month's result as bars in a band beneath,
## and the three turning points numbered on the line where they began. Every
## value is the engine's end report (the ledger's own bookings). Pure _draw()
## on the surface's Palette colours; hovering a month shows its figures. The
## line draws itself from left to right once (reveal), at rest with reduced
## motion.

## Report fields this chart reads: months, start_day, end_day, opening_cash,
## bankruptcy_floor, turning_points.
var report: Dictionary = {}:
	set(v):
		report = v
		queue_redraw()
## 0-1: how much of the line is drawn (the reveal animation).
var reveal: float = 1.0:
	set(v):
		reveal = clampf(v, 0.0, 1.0)
		queue_redraw()

const PAD_LEFT := 96.0
const PAD_RIGHT := 16.0
const PAD_TOP := 14.0
const LABEL_ROW := 20.0
const BAND_SHARE := 0.26
const BAND_GAP := 12.0
const FONT_SIZE := 13
const MARKER_R := 11.0
const MIN_LABEL_GAP := 46.0
const DASH := 6.0
## Axis labels closer than this (px) would overlap: the less telling one goes.
const LABEL_CLEARANCE := 16.0
const FILL_ALPHA := 0.14
## The floor is drawn when the lowest cash came within this share of the
## distance from the opening cash to the floor.
const FLOOR_NEAR := 0.5


func _ready() -> void:
	custom_minimum_size = Vector2(420, 240)
	mouse_filter = Control.MOUSE_FILTER_PASS
	tooltip_text = " "  # enables _get_tooltip
	resized.connect(queue_redraw)


# ── Geometry (tests read it) ───────────────────────────────────────────────────

func months() -> Array:
	return report.get("months", [])


func start_day() -> int:
	return int(report.get("start_day", 0))


func end_day() -> int:
	return maxi(int(report.get("end_day", 0)), start_day() + 1)


## The cash points of the line: [Vector2(day, cash)], the opening first.
func cash_points() -> Array[Vector2]:
	var pts: Array[Vector2] = [Vector2(start_day(), float(report.get("opening_cash", 0.0)))]
	var ms := months()
	for i in ms.size():
		var last_day := end_day() if i == ms.size() - 1 else int(ms[i + 1].get("first_day", 0)) - 1
		pts.append(Vector2(clampi(last_day, start_day(), end_day()), float(ms[i].get("closing_cash", 0.0))))
	return pts


## True when the floor is drawn (the cash came near it).
func shows_floor() -> bool:
	var floor_nok := float(report.get("bankruptcy_floor", 0.0))
	var opening := float(report.get("opening_cash", 0.0))
	var lowest := opening
	for p in cash_points():
		lowest = minf(lowest, p.y)
	return lowest < 0.0 or lowest - floor_nok <= (opening - floor_nok) * FLOOR_NEAR


## The value range of the cash area: [min, max], zero always inside.
func cash_range() -> Vector2:
	var lo := 0.0
	var hi := 0.0
	for p in cash_points():
		lo = minf(lo, p.y)
		hi = maxf(hi, p.y)
	if shows_floor():
		lo = minf(lo, float(report.get("bankruptcy_floor", 0.0)))
	if is_equal_approx(lo, hi):
		hi = lo + 1.0
	var pad := (hi - lo) * 0.06
	return Vector2(lo - (pad if lo < 0.0 else 0.0), hi + pad)


func plot_rect() -> Rect2:
	var w := maxf(size.x - PAD_LEFT - PAD_RIGHT, 1.0)
	var h := maxf(size.y - PAD_TOP - LABEL_ROW, 1.0)
	var line_h := (h - BAND_GAP) * (1.0 - BAND_SHARE)
	return Rect2(PAD_LEFT, PAD_TOP, w, line_h)


func band_rect() -> Rect2:
	var plot := plot_rect()
	var h := maxf(size.y - PAD_TOP - LABEL_ROW, 1.0)
	return Rect2(PAD_LEFT, plot.end.y + BAND_GAP, plot.size.x, h - plot.size.y - BAND_GAP)


func x_of(day: float) -> float:
	var plot := plot_rect()
	return plot.position.x + plot.size.x * (day - start_day()) / float(end_day() - start_day())


func y_of(cash: float) -> float:
	var plot := plot_rect()
	var r := cash_range()
	return plot.end.y - plot.size.y * (cash - r.x) / (r.y - r.x)


## The cash on `day` along the drawn line (linear between the points).
func cash_at(day: float) -> float:
	var pts := cash_points()
	for i in range(1, pts.size()):
		if day <= pts[i].x:
			var a := pts[i - 1]
			var b := pts[i]
			return a.y if is_equal_approx(a.x, b.x) else lerpf(a.y, b.y, (day - a.x) / (b.x - a.x))
	return pts[-1].y


## Each turning point's marker centre, in the report's order: on the cash
## line where the run began, lifted clear of a marker it would cover.
func marker_centres() -> Array[Vector2]:
	var out: Array[Vector2] = []
	var top := plot_rect().position.y + MARKER_R
	for tp in report.get("turning_points", []):
		var day := float(tp.get("first_day", 0))
		var c := Vector2(x_of(day), y_of(cash_at(day)))
		var guard := 0
		while out.any(func(o: Vector2) -> bool: return o.distance_to(c) < MARKER_R * 2.0 + 2.0) and guard < 4:
			c.y = c.y - (MARKER_R * 2.0 + 4.0) if c.y - MARKER_R * 3.0 >= top else c.y + MARKER_R * 2.0 + 4.0
			guard += 1
		out.append(c)
	return out


## Each month's result bar: [{month, rect, result}].
func bar_rects() -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	var ms := months()
	if ms.is_empty():
		return out
	var band := band_rect()
	var peak := 0.0
	for m in ms:
		peak = maxf(peak, absf(float(m.get("result", 0.0))))
	var mid := band.position.y + band.size.y * 0.5
	for i in ms.size():
		var first := float(ms[i].get("first_day", start_day()))
		var last := float(end_day()) if i == ms.size() - 1 else float(ms[i + 1].get("first_day", 0))
		var x0 := x_of(first)
		var x1 := x_of(last)
		var bw := maxf((x1 - x0) * 0.62, 2.0)
		var result := float(ms[i].get("result", 0.0))
		var bh := 0.0 if peak <= 0.0 else band.size.y * 0.5 * absf(result) / peak
		var top := mid - bh if result >= 0.0 else mid
		out.append({"month": int(ms[i].get("month", 0)), "rect": Rect2((x0 + x1 - bw) * 0.5, top, bw, bh), "result": result})
	return out


# ── Drawing ────────────────────────────────────────────────────────────────────

func _draw() -> void:
	var font := get_theme_default_font()
	var plot := plot_rect()
	if months().is_empty():
		draw_string(font, Vector2(PAD_LEFT, PAD_TOP + 20.0), tr("UI_CHART_NO_DATA"), HORIZONTAL_ALIGNMENT_LEFT, -1,
			FONT_SIZE, _pal(&"muted"))
		return
	_draw_axes(font, plot)
	_draw_cash(plot)
	_draw_band()
	_draw_month_labels(font)
	_draw_markers(font)


## The axis labels: the top of the range, zero and the floor (or the bottom),
## each dropped when it would overlap a more telling one (the floor first).
func axis_labels() -> Array[float]:
	var r := cash_range()
	var out: Array[float] = []
	var wanted: Array[float] = []
	if shows_floor():
		wanted.append(float(report.get("bankruptcy_floor", 0.0)))
	elif r.x < 0.0:
		wanted.append(r.x)
	wanted.append_array([0.0, r.y])
	for v in wanted:
		if out.all(func(o: float) -> bool: return absf(y_of(o) - y_of(v)) >= LABEL_CLEARANCE):
			out.append(v)
	return out


func _draw_axes(font: Font, plot: Rect2) -> void:
	draw_line(Vector2(plot.position.x, y_of(0.0)), Vector2(plot.end.x, y_of(0.0)), _pal(&"stroke"), 1.0)
	var floor_nok := float(report.get("bankruptcy_floor", 0.0))
	for v in axis_labels():
		_axis_label(font, v, _pal(&"crit") if shows_floor() and is_equal_approx(v, floor_nok) else Color(0, 0, 0, 0))
	if shows_floor():
		var fy := y_of(floor_nok)
		var x := plot.position.x
		while x < plot.end.x:
			draw_line(Vector2(x, fy), Vector2(minf(x + DASH, plot.end.x), fy), _pal(&"crit"), 1.5)
			x += DASH * 2.0
		# The caption sits over the dashes at the start, clear of the line's end.
		draw_string(font, Vector2(plot.position.x + 6.0, fy - 5.0), tr("UI_ENDSCREEN_CHART_FLOOR"),
			HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE, _pal(&"crit"))


func _axis_label(font: Font, value: float, colour: Color = Color(0, 0, 0, 0)) -> void:
	var text := SimFormatters.format_nok(value)
	var tw := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE).x
	var y := clampf(y_of(value) + 4.0, PAD_TOP + 10.0, plot_rect().end.y)
	draw_string(font, Vector2(PAD_LEFT - 8.0 - tw, y), text, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE,
		colour if colour.a > 0.0 else _pal(&"muted"))


func _draw_cash(plot: Rect2) -> void:
	var pts := cash_points()
	var reveal_x := plot.position.x + plot.size.x * reveal
	var line := PackedVector2Array()
	for p in pts:
		var at := Vector2(x_of(p.x), y_of(p.y))
		if at.x > reveal_x and not line.is_empty():
			var prev := line[line.size() - 1]
			line.append(prev.lerp(at, (reveal_x - prev.x) / maxf(at.x - prev.x, 0.001)))
			break
		line.append(at)
	if line.size() < 2:
		return
	var zero_y := y_of(0.0)
	var fill := PackedVector2Array(line)
	fill.append(Vector2(line[line.size() - 1].x, zero_y))
	fill.append(Vector2(line[0].x, zero_y))
	if Geometry2D.triangulate_polygon(fill).size() > 0:
		draw_colored_polygon(fill, Color(_pal(&"info"), FILL_ALPHA))
	draw_polyline(line, _pal(&"info"), 2.5, true)
	draw_circle(line[line.size() - 1], 3.5, _pal(&"info"))


func _draw_band() -> void:
	var band := band_rect()
	var mid := band.position.y + band.size.y * 0.5
	draw_line(Vector2(band.position.x, mid), Vector2(band.end.x, mid), Color(_pal(&"hairline"), 0.9), 1.0)
	var reveal_x := plot_rect().position.x + plot_rect().size.x * reveal
	for bar in bar_rects():
		var rect: Rect2 = bar["rect"]
		if rect.position.x > reveal_x or rect.size.y < 1.0:
			continue
		draw_rect(rect, _pal(&"ok") if float(bar["result"]) >= 0.0 else _pal(&"crit"))


func _draw_month_labels(font: Font) -> void:
	var y := size.y - 5.0
	var last_x := -INF
	for m in months():
		var day := int(m.get("first_day", 0))
		var date := SimFormatters.date_for_day(day)
		var x := x_of(maxf(day, start_day()))
		if x - last_x < MIN_LABEL_GAP:
			continue
		var text := SimFormatters.month_name(int(date["month"]))
		if int(date["month"]) == 1 or last_x == -INF:
			text += " " + str(date["year"])
		var tw := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE).x
		if x + tw > size.x:
			break
		draw_string(font, Vector2(x, y), text, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE, _pal(&"muted"))
		last_x = x + tw


func _draw_markers(font: Font) -> void:
	var tps: Array = report.get("turning_points", [])
	var centres := marker_centres()
	var reveal_x := plot_rect().position.x + plot_rect().size.x * reveal
	for i in centres.size():
		var c := centres[i]
		if c.x > reveal_x + 0.5:
			continue
		var tone := _pal(&"ok") if bool(tps[i].get("income", false)) else _pal(&"crit")
		draw_line(Vector2(c.x, c.y), Vector2(c.x, plot_rect().end.y), Color(tone, 0.45), 1.0)
		draw_circle(c, MARKER_R + 2.0, _pal(&"raised"))
		draw_circle(c, MARKER_R, tone)
		var digit := str(i + 1)
		var ds := font.get_string_size(digit, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE)
		draw_string(font, Vector2(c.x - ds.x * 0.5, c.y + ds.y * 0.32), digit, HORIZONTAL_ALIGNMENT_LEFT, -1,
			FONT_SIZE, _pal(&"raised"))


func _get_tooltip(at_position: Vector2) -> String:
	var ms := months()
	if ms.is_empty():
		return ""
	var day := start_day() + (at_position.x - PAD_LEFT) / maxf(plot_rect().size.x, 1.0) * (end_day() - start_day())
	var pick := 0
	for i in ms.size():
		if day >= float(ms[i].get("first_day", 0)):
			pick = i
	var m: Dictionary = ms[pick]
	var date := SimFormatters.date_for_day(int(m.get("first_day", 0)))
	return tr("UI_ENDSCREEN_CHART_TIP") % [GameCalendar.month_name(int(date["month"])), int(date["year"]),
		SimFormatters.format_nok(float(m.get("revenue", 0.0))), SimFormatters.format_nok(float(m.get("costs", 0.0))),
		SimFormatters.format_nok(float(m.get("result", 0.0))), SimFormatters.format_nok(float(m.get("closing_cash", 0.0)))]


## A palette colour on the surface this chart is drawn on (Chart paper).
func _pal(key: StringName) -> Color:
	return get_theme_color(key, &"Palette")
