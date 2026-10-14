class_name Sparkline
extends Control

## Sparkline — compact headless-safe trend line for KPI cards and panels.
## Pure _draw(); no textures, no shaders. Robust to empty / 1-point / NaN / inf / flat data.
##
## Usage (KPI card):
##     var spark := Sparkline.new()
##     spark.values = [12.0, 13.5, 12.8, 15.2, 16.0]
##     spark.unit_fmt = "%.1f"
##     spark.baseline = 13.0
##     kpi_row.add_child(spark)

## Sample values, oldest → newest. Non-finite samples are skipped (gaps left unconnected).
var values: Array[float] = []:
	set(v): values = v; queue_redraw()
## Trend line color (defaults to the Nordic ACCENT).
var line_color: Color = LakskongenTheme.ACCENT:
	set(v): line_color = v; queue_redraw()
## Translucent area under the curve. Set alpha 0 to disable the fill.
var fill_color: Color = Color(LakskongenTheme.ACCENT, 0.12):
	set(v): fill_color = v; queue_redraw()
## Optional horizontal reference line. Use NAN (default) to disable.
var baseline: float = NAN:
	set(v): baseline = v; queue_redraw()
var show_last_dot: bool = true:
	set(v): show_last_dot = v; queue_redraw()
## printf-style format for the last value; empty string hides the label.
var unit_fmt: String = "":
	set(v): unit_fmt = v; queue_redraw()

const LINE_WIDTH := 2.0
const DOT_RADIUS := 2.5
const LABEL_SIZE := 13  ## the 13 px floor (roadmap U1-02 font lint)

func _ready() -> void:
	custom_minimum_size = Vector2(80, 28)
	mouse_filter = Control.MOUSE_FILTER_IGNORE

## Convenience setter matching LineChart.set_series().
func set_values(v: Array[float]) -> void:
	values = v

func _draw() -> void:
	var n := values.size()
	var w := size.x
	var h := size.y
	if n <= 0 or w <= 0.0 or h <= 0.0:
		return

	# Min/max over finite samples (plus the baseline when enabled).
	var lo := INF
	var hi := -INF
	for v in values:
		if is_nan(v) or is_inf(v):
			continue
		lo = min(lo, v)
		hi = max(hi, v)
	if is_finite(baseline):
		lo = min(lo, baseline)
		hi = max(hi, baseline)
	if not is_finite(lo) or not is_finite(hi):
		return

	# Flat series: expand to a unit band so the line is visible and centred.
	if is_equal_approx(hi, lo):
		lo -= 0.5
		hi += 0.5
	else:
		var pad := (hi - lo) * 0.12
		lo -= pad
		hi += pad

	var denom := maxf(1.0, float(n - 1))
	var to_y := func(v: float) -> float:
		return h * (1.0 - (v - lo) / (hi - lo))
	var to_p := func(i: int, v: float) -> Vector2:
		var x := w * 0.5 if n == 1 else w * (float(i) / denom)
		return Vector2(x, to_y.call(v))

	# Continuous polyline over the finite samples.
	var pts := PackedVector2Array()
	for i in range(n):
		var v := values[i]
		if is_nan(v) or is_inf(v):
			continue
		pts.append(to_p.call(i, v))

	if pts.size() < 2:
		if pts.size() == 1:
			draw_circle(pts[0], DOT_RADIUS + 0.5, line_color)
		return

	# Soft area fill under the curve.
	if fill_color.a > 0.0:
		var poly := PackedVector2Array(pts)
		poly.append(Vector2(pts[pts.size() - 1].x, h))
		poly.append(Vector2(pts[0].x, h))
		draw_colored_polygon(poly, fill_color)

	# Baseline reference (drawn under the trend line).
	if is_finite(baseline):
		var by: float = to_y.call(baseline)
		draw_line(Vector2(0.0, by), Vector2(w, by), Color(get_theme_color(&"muted", &"Palette"), 0.4), 1.0)

	# Trend line with rounded caps.
	draw_polyline(pts, line_color, LINE_WIDTH, true)

	# Last finite point + optional label.
	var last_i := -1
	var last_v := 0.0
	for i in range(n - 1, -1, -1):
		var v := values[i]
		if is_nan(v) or is_inf(v):
			continue
		last_i = i
		last_v = v
		break
	if last_i < 0:
		return
	var lp: Vector2 = to_p.call(last_i, last_v)
	if show_last_dot:
		draw_circle(lp, DOT_RADIUS, line_color)
	if not unit_fmt.is_empty():
		_draw_label(unit_fmt % last_v, lp, w, h)

func _draw_label(text: String, lp: Vector2, w: float, h: float) -> void:
	var font := get_theme_default_font()
	var tw := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_SIZE).x
	var tx := lp.x + 5.0
	if tx + tw > w:
		tx = lp.x - 5.0 - tw
	tx = clampf(tx, 0.0, maxf(0.0, w - tw))
	var ty := clampf(lp.y - 3.0, float(LABEL_SIZE), h)
	draw_string(font, Vector2(tx, ty), text,
		HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_SIZE, get_theme_color(&"text", &"Palette"))
