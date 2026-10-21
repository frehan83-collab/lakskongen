class_name MapTempBar
extends Control
## The detail column's sea-temperature bar (moved out of MapPanel.gd, geo
## stages 2-3 plan Track C): the area's monthly range from the sim's
## climatology over a 0-22 °C track, the salmon's optimum (12-16 °C) marked.

const SCALE_MAX_C := 22.0
const OPTIMAL_C := Vector2(12.0, 16.0)
const WARM_C := 17.0
const HOT_C := 19.0
const TICKS_C: Array[int] = [5, 10, 12, 15, 16, 20]

var _temp_min: float = 5.0
var _temp_max: float = 15.0


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func set_range(tmin: float, tmax: float) -> void:
	_temp_min = tmin
	_temp_max = tmax
	queue_redraw()


func temp_range() -> Vector2:
	return Vector2(_temp_min, _temp_max)


func _draw() -> void:
	var w := size.x
	var h := size.y
	if w <= 1.0 or h <= 1.0:
		return
	var info := MapPalette.chart("info")
	var ok := MapPalette.chart("ok")
	draw_rect(Rect2(0, h * 0.3, w, h * 0.4), Color(info, 0.35), true)
	var opt_start := OPTIMAL_C.x / SCALE_MAX_C * w
	var opt_end := OPTIMAL_C.y / SCALE_MAX_C * w
	draw_rect(Rect2(opt_start, h * 0.1, opt_end - opt_start, h * 0.8), Color(ok, 0.35), true)
	draw_rect(Rect2(opt_start, h * 0.1, opt_end - opt_start, h * 0.8), ok, false, 1.0)
	var range_color := info
	if _temp_max >= HOT_C:
		range_color = MapPalette.chart("crit")
	elif _temp_max >= WARM_C:
		range_color = MapPalette.chart("warn")
	var range_start := clampf(_temp_min, 0.0, SCALE_MAX_C) / SCALE_MAX_C * w
	var range_end := clampf(_temp_max, 0.0, SCALE_MAX_C) / SCALE_MAX_C * w
	draw_rect(Rect2(range_start, h * 0.25, range_end - range_start, h * 0.5), range_color, true)
	for t in TICKS_C:
		var tx := float(t) / SCALE_MAX_C * w
		draw_line(Vector2(tx, h * 0.8), Vector2(tx, h), Color(MapPalette.chart("stroke"), 0.6), 1.0)
