class_name LineChart
extends Control

## LineChart — lightweight headless-safe time-series chart (Phase 1.1).
## Pure _draw(); no textures, no shaders. Set via set_series().
## threshold < 0 disables the limit line.

var title: String = "":
	set(v): title = v; queue_redraw()
var series: Array[float] = []:
	set(v): series = v; queue_redraw()
var moving_avg: Array[float] = []:
	set(v): moving_avg = v; queue_redraw()
## PROJECTION continuation (Phase 4.5): drawn dashed from the last point.
var future: Array[float] = []:
	set(v): future = v; queue_redraw()
var threshold: float = -1.0:
	set(v): threshold = v; queue_redraw()
var unit_fmt: String = "%.1f":
	set(v): unit_fmt = v; queue_redraw()
var line_color: Color = LakskongenTheme.ACCENT:
	set(v): line_color = v; queue_redraw()
## Shown when the series has no drawable data.
var empty_text: String = "Ingen data ennå":
	set(v): empty_text = v; queue_redraw()

const PAD_LEFT := 8.0
const PAD_RIGHT := 8.0
const PAD_TOP := 22.0
const PAD_BOTTOM := 8.0
const GRID_LINES := 4
const VALUE_FONT_SIZE := 13
## Chart text follows the theme: the UI face and the 13 px floor.
const TITLE_FONT_SIZE := 13

func set_series(values: Array[float], ma: Array[float] = []) -> void:
	series = values
	moving_avg = ma

func set_future(values: Array[float]) -> void:
	future = values

func _ready() -> void:
	custom_minimum_size = Vector2(220, 120)
	mouse_filter = Control.MOUSE_FILTER_IGNORE

func _draw() -> void:
	var w := size.x - PAD_LEFT - PAD_RIGHT
	var h := size.y - PAD_TOP - PAD_BOTTOM
	if w <= 0.0 or h <= 0.0:
		return
	_draw_title()
	if series.size() < 2:
		_draw_empty(w)
		return
	var lo := INF
	var hi := -INF
	for v in series:
		if is_nan(v) or is_inf(v):
			continue
		lo = min(lo, v)
		hi = max(hi, v)
	for v in future:
		if is_nan(v) or is_inf(v):
			continue
		lo = min(lo, v)
		hi = max(hi, v)
	if threshold >= 0.0:
		lo = min(lo, threshold)
		hi = max(hi, threshold)
	if not is_finite(lo) or not is_finite(hi):
		_draw_empty(w)
		return
	if is_equal_approx(hi, lo):
		hi = lo + 1.0
	# Frame + light horizontal gridlines.
	draw_rect(Rect2(PAD_LEFT, PAD_TOP, w, h), Color(_pal(&"hairline"), 0.9), false, 1.0)
	for i in range(1, GRID_LINES):
		var gy: float = PAD_TOP + h * (float(i) / float(GRID_LINES))
		draw_line(Vector2(PAD_LEFT, gy), Vector2(PAD_LEFT + w, gy), Color(_pal(&"hairline"), 0.55), 1.0)
	var map := func(v: float, i: int) -> Vector2:
		var span := float(maxi(1, series.size() - 1 + future.size()))
		var x := PAD_LEFT + w * (float(i) / span)
		var y := PAD_TOP + h * (1.0 - (v - lo) / (hi - lo))
		return Vector2(x, y)
	# Threshold line
	if threshold >= 0.0:
		var ty: float = PAD_TOP + h * (1.0 - (threshold - lo) / (hi - lo))
		draw_line(Vector2(PAD_LEFT, ty), Vector2(PAD_LEFT + w, ty), _pal(&"crit"), 1.0)
	# Moving average (dashed feel: shorter segments)
	if moving_avg.size() == series.size():
		var prev := Vector2.ZERO
		var started := false
		for i in range(series.size()):
			if is_nan(moving_avg[i]) or is_inf(moving_avg[i]):
				started = false
				continue
			var p: Vector2 = map.call(moving_avg[i], i)
			if started and i % 2 == 0:
				draw_line(prev, p, Color(line_color, 0.45), 1.0)
			prev = p
			started = true
	# Main series
	var pts := PackedVector2Array()
	for i in range(series.size()):
		if is_nan(series[i]) or is_inf(series[i]):
			continue
		pts.append(map.call(series[i], i))
	if pts.size() >= 2:
		# Subtle area fill under the curve.
		var poly := PackedVector2Array(pts)
		poly.append(Vector2(pts[pts.size() - 1].x, PAD_TOP + h))
		poly.append(Vector2(pts[0].x, PAD_TOP + h))
		draw_colored_polygon(poly, Color(line_color, 0.12))
		draw_polyline(pts, line_color, 2.0, true)
	# Last-value dot + label
	var last := series[series.size() - 1]
	if is_finite(last):
		var lp: Vector2 = map.call(last, series.size() - 1)
		draw_circle(lp, 3.0, line_color)
		_draw_value_pill(unit_fmt % last, lp, w)
	# PROJECTION: dashed continuation into the future (same scale).
	if future.size() >= 2 and is_finite(last):
		var prev2: Vector2 = map.call(last, series.size() - 1)
		for i in range(future.size()):
			if is_nan(future[i]) or is_inf(future[i]):
				continue
			var fp: Vector2 = map.call(future[i], series.size() + i)
			if i % 2 == 0:
				draw_line(prev2, fp, Color(line_color, 0.55), 1.5)
			prev2 = fp

func _draw_title() -> void:
	if title.is_empty():
		return
	draw_string(get_theme_default_font(), Vector2(PAD_LEFT, 14), title,
		HORIZONTAL_ALIGNMENT_LEFT, -1, TITLE_FONT_SIZE, _pal(&"text"))

## Last-value label drawn on a small pill for contrast against the plot.
func _draw_value_pill(text: String, lp: Vector2, w: float) -> void:
	if text.is_empty():
		return
	var font := get_theme_default_font()
	var tw := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, VALUE_FONT_SIZE).x
	var th := float(VALUE_FONT_SIZE) + 6.0
	var pad := 4.0
	var origin := lp + Vector2(6.0, -th - 2.0)
	if origin.x + tw + pad * 2.0 > PAD_LEFT + w:
		origin.x = lp.x - 6.0 - (tw + pad * 2.0)
	origin.x = clampf(origin.x, PAD_LEFT, maxf(PAD_LEFT, PAD_LEFT + w - (tw + pad * 2.0)))
	origin.y = clampf(origin.y, 0.0, maxf(0.0, size.y - th))
	var rect := Rect2(origin, Vector2(tw + pad * 2.0, th))
	draw_rect(rect, Color(_pal(&"raised"), 0.95), true)
	draw_rect(rect, _pal(&"hairline"), false, 1.0)
	draw_string(font, Vector2(rect.position.x + pad, rect.position.y + float(VALUE_FONT_SIZE) + 1.0), text,
		HORIZONTAL_ALIGNMENT_LEFT, -1, VALUE_FONT_SIZE, _pal(&"text"))

func _draw_empty(w: float) -> void:
	draw_string(get_theme_default_font(), Vector2(PAD_LEFT, PAD_TOP + 20), empty_text,
		HORIZONTAL_ALIGNMENT_LEFT, -1, TITLE_FONT_SIZE, _pal(&"muted"))


## A palette colour on the surface this chart is drawn on (Ink or Chart).
func _pal(key: StringName) -> Color:
	return get_theme_color(key, &"Palette")
