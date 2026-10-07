## AnalyticsPanel.gd
## Advanced analytics panel displaying real-time line charts for key metrics.
## Uses custom CanvasItem drawing for smooth charts with diegetic tablet styling.
## Supports 7d/30d/90d/All view toggles, swipe/zoom, and hover tooltips.
## Data from SimBridge via get_analytics_history() / GetAnalyticsData().

extends Control

# ── Configuration ──────────────────────────────────────────────────────────────

const METRICS: Array[Dictionary] = [
	{ "key": "total_biomass_kg",  "label": "Biomass (t)",       "unit": "t",      "color": Color("#A3BE8C"), "decimals": 1, "divisor": 1000.0 },
	{ "key": "profit_loss_nok",   "label": "Profit/Loss (MNOK)", "unit": "MNOK",   "color": Color("#BF616A"), "decimals": 2, "divisor": 1_000_000.0 },
	{ "key": "dissolved_oxygen",  "label": "Dissolved O₂ (mg/L)", "unit": "mg/L",   "color": Color("#88C0D0"), "decimals": 1, "divisor": 1.0 },
	{ "key": "lice_per_fish",     "label": "Lice Count",         "unit": "/fish",  "color": Color("#EBCB8B"), "decimals": 2, "divisor": 1.0 },
	{ "key": "feed_cost_nok",     "label": "Feed Cost (MNOK)",   "unit": "MNOK",   "color": Color("#B48EAD"), "decimals": 2, "divisor": 1_000_000.0 },
]

const CHART_MIN_W := 280
const CHART_MIN_H := 180
const TOOLTIP_RADIUS := 60.0

enum Period { PERIOD_7D = 7, PERIOD_30D = 30, PERIOD_90D = 90, PERIOD_ALL = -1 }

# ── State ──────────────────────────────────────────────────────────────────────

var _active_period: int = 30
var _chart_areas: Dictionary = {}  # key -> _ChartArea
var _hovered_chart: _ChartArea = null
var _hover_point_idx: int = -1
var _hover_point_pos: Vector2 = Vector2.ZERO
var _zoom_level: float = 1.0
var _pan_offset: Vector2 = Vector2.ZERO
var _is_panning: bool = false
var _pan_start: Vector2 = Vector2.ZERO
var _drag_threshold: float = 8.0

# ── Node refs (from scene) ─────────────────────────────────────────────────────

@onready var tablet_overlay: PanelContainer = %TabletOverlay
@onready var outer_vbox: VBoxContainer = %OuterVBox
@onready var toolbar: HBoxContainer = %Toolbar
@onready var title_label: Label = %TitleLabel
@onready var period_segment: HBoxContainer = %PeriodSegment
@onready var btn_7d: Button = %Btn7d
@onready var btn_30d: Button = %Btn30d
@onready var btn_90d: Button = %Btn90d
@onready var btn_all: Button = %BtnAll
@onready var charts_grid: GridContainer = %ChartsGrid
@onready var chart_biomass: PanelContainer = %ChartBiomass
@onready var chart_profit: PanelContainer = %ChartProfit
@onready var chart_do: PanelContainer = %ChartDO
@onready var chart_lice: PanelContainer = %ChartLice
@onready var chart_feed: PanelContainer = %ChartFeed

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_apply_tablet_theme()
	_connect_period_buttons()
	_build_charts()
	_refresh()
	
	# Live refresh every 2 seconds
	var timer := Timer.new()
	timer.wait_time = 2.0
	timer.autostart = true
	timer.one_shot = false
	timer.timeout.connect(_refresh)
	add_child(timer)

func _input(event: InputEvent) -> void:
	if not visible:
		return
	
	# Handle pan/zoom on chart areas
	if event is InputEventMouseButton:
		_handle_mouse_button(event)
	elif event is InputEventMouseMotion:
		_handle_mouse_motion(event)
	elif event is InputEventScreenTouch:
		_handle_touch(event)
	elif event is InputEventKey and event.pressed:
		if event.keycode == KEY_ESCAPE:
			_reset_view()

# ── Theme & Styling ────────────────────────────────────────────────────────────

func _apply_tablet_theme() -> void:
	# Tablet overlay uses scene StyleBoxFlat_tablet (dark, rounded)
	# Ensure it's properly themed for diegetic look
	tablet_overlay.add_theme_color_override("bg_color", Color("#1E222A"))
	tablet_overlay.add_theme_color_override("border_color", Color("#3A4150"))

# ── Period Button Handling ─────────────────────────────────────────────────────

func _connect_period_buttons() -> void:
	btn_7d.pressed.connect(_on_period_pressed.bind(7))
	btn_30d.pressed.connect(_on_period_pressed.bind(30))
	btn_90d.pressed.connect(_on_period_pressed.bind(90))
	btn_all.pressed.connect(_on_period_pressed.bind(-1))
	_update_period_button_states()

func _on_period_pressed(days: int) -> void:
	_active_period = days
	_zoom_level = 1.0
	_pan_offset = Vector2.ZERO
	_update_period_button_states()
	_refresh()

func _update_period_button_states() -> void:
	var buttons: Dictionary = { 7: btn_7d, 30: btn_30d, 90: btn_90d, -1: btn_all }
	# Normal style comes from the scene; active is derived by recoloring a duplicate
	# (keeps a single source of truth for radii/borders defined in the .tscn).
	var normal_sb := btn_7d.get_theme_stylebox("normal") as StyleBoxFlat
	var active_sb := normal_sb.duplicate() as StyleBoxFlat
	active_sb.bg_color = Color(0.039, 0.302, 0.408)
	active_sb.border_color = Color(0.533, 0.753, 0.816)
	
	for days in buttons:
		var btn = buttons[days]
		var is_active = (days == _active_period)
		btn.add_theme_stylebox_override("normal", active_sb if is_active else normal_sb)

# ── Chart Building ─────────────────────────────────────────────────────────────

func _build_charts() -> void:
	var chart_containers: Dictionary = {
		"total_biomass_kg": chart_biomass,
		"profit_loss_nok": chart_profit,
		"dissolved_oxygen": chart_do,
		"lice_per_fish": chart_lice,
		"feed_cost_nok": chart_feed,
	}
	
	for meta in METRICS:
		var key = meta["key"]
		var container = chart_containers[key]
		if not container:
			continue
		
		var chart_area = _ChartArea.new()
		chart_area.name = "ChartArea_" + key
		chart_area.custom_minimum_size = Vector2(CHART_MIN_W, CHART_MIN_H)
		chart_area.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		chart_area.size_flags_vertical = Control.SIZE_EXPAND_FILL
		chart_area.metric_meta = meta
		chart_area.panel_ref = self
		container.add_child(chart_area)
		_chart_areas[key] = chart_area
		
		# Add header inside container
		var header = HBoxContainer.new()
		header.add_theme_constant_override("separation", 8)
		container.add_child(header)
		container.move_child(header, 0)
		
		var name_lbl = Label.new()
		name_lbl.text = meta["label"]
		name_lbl.add_theme_font_size_override("font_size", 14)
		name_lbl.add_theme_color_override("font_color", Color("#ECEFF4"))
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		header.add_child(name_lbl)
		
		var trend_lbl = Label.new()
		trend_lbl.text = " ◆"
		trend_lbl.add_theme_font_size_override("font_size", 16)
		trend_lbl.name = "TrendLabel"
		header.add_child(trend_lbl)
		
		var value_lbl = Label.new()
		value_lbl.text = "–"
		value_lbl.add_theme_font_size_override("font_size", 12)
		value_lbl.add_theme_color_override("font_color", Color("#7B8794"))
		value_lbl.name = "ValueLabel"
		value_lbl.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		header.add_child(value_lbl)

# ── Data Refresh ───────────────────────────────────────────────────────────────

func _refresh() -> void:
	if not Engine.has_singleton("SimBridge"):
		return
	
	var sim = Engine.get_singleton("SimBridge")
	
	for meta in METRICS:
		var key = meta["key"]
		var chart_area = _chart_areas.get(key)
		if not chart_area:
			continue
		
		# Get raw series
		var days_param = _active_period if _active_period > 0 else 365
		var series: Array[float] = Array(sim.GetAnalyticsData(key, days_param))
		var ma_series: Array[float] = Array(sim.GetAnalyticsMovingAverage(key, days_param, 7))
		var trend: String = sim.GetAnalyticsTrend(key, min(days_param, 14))
		
		# Apply divisor for display
		var divisor = meta["divisor"]
		if divisor != 1.0:
			for i in range(series.size()):
				series[i] /= divisor
			for i in range(ma_series.size()):
				ma_series[i] /= divisor
		
		chart_area.set_series(series, ma_series)
		chart_area.set_trend(trend)
		chart_area.queue_redraw()
		
		# Update header labels
		var container = _get_chart_container(key)
		if container:
			var trend_lbl = container.get_node_or_null("TrendLabel")
			var value_lbl = container.get_node_or_null("ValueLabel")
			
			if trend_lbl:
				match trend:
					"up":
						trend_lbl.text = " ▲"
						trend_lbl.add_theme_color_override("font_color", Color("#A3BE8C"))
					"down":
						trend_lbl.text = " ▼"
						trend_lbl.add_theme_color_override("font_color", Color("#BF616A"))
					_:
						trend_lbl.text = " ◆"
						trend_lbl.add_theme_color_override("font_color", Color("#7B8794"))
			
			if value_lbl and series.size() > 0:
				var last_val = series[-1]
				value_lbl.text = _format_value(meta, last_val)
				value_lbl.add_theme_color_override("font_color", meta["color"])
			elif value_lbl:
				value_lbl.text = "–"

func _get_chart_container(key: String) -> PanelContainer:
	match key:
		"total_biomass_kg": return chart_biomass
		"profit_loss_nok": return chart_profit
		"dissolved_oxygen": return chart_do
		"lice_per_fish": return chart_lice
		"feed_cost_nok": return chart_feed
		_: return null

func _format_value(meta: Dictionary, value: float) -> String:
	var fmt = "%." + str(meta["decimals"]) + "f"
	var formatted = fmt % value
	return formatted + " " + meta["unit"]

# ── Input Handling (Pan/Zoom) ──────────────────────────────────────────────────

func _handle_mouse_button(event: InputEventMouseButton) -> void:
	var chart_area = _get_chart_area_at_position(event.position)
	if not chart_area:
		return
	
	if event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			_is_panning = true
			_pan_start = event.position - _pan_offset
			chart_area._drag_start_pos = event.position
		else:
			_is_panning = false
	elif event.button_index == MOUSE_BUTTON_RIGHT and event.pressed:
		_reset_view()
	elif event.button_index == MOUSE_BUTTON_WHEEL_UP:
		_zoom_at(chart_area, event.position, 1.2)
	elif event.button_index == MOUSE_BUTTON_WHEEL_DOWN:
		_zoom_at(chart_area, event.position, 0.833)

func _handle_mouse_motion(event: InputEventMouseMotion) -> void:
	var chart_area = _get_chart_area_at_position(event.position)
	
	if _is_panning and chart_area:
		_pan_offset = event.position - _pan_start
		for ca in _chart_areas.values():
			ca._pan_offset = _pan_offset
			ca.queue_redraw()
		return
	
	# Hover tooltip
	if chart_area:
		_hovered_chart = chart_area
		var pt_idx = chart_area._get_closest_point_index(event.position)
		if pt_idx >= 0:
			_hover_point_idx = pt_idx
			_hover_point_pos = chart_area._data_to_screen(chart_area._series[pt_idx], pt_idx, chart_area._series.size(), chart_area.size.x, chart_area.size.y, 40.0)
			_hover_point_pos += chart_area.position + _pan_offset
		else:
			_hover_point_idx = -1
	else:
		_hovered_chart = null
		_hover_point_idx = -1

func _handle_touch(event: InputEventScreenTouch) -> void:
	# Basic touch support for pan
	var chart_area = _get_chart_area_at_position(event.position)
	if not chart_area:
		return
	
	if event.pressed:
		_is_panning = true
		_pan_start = event.position - _pan_offset
	else:
		_is_panning = false

func _get_chart_area_at_position(pos: Vector2) -> _ChartArea:
	for ca in _chart_areas.values():
		var rect = Rect2(ca.get_global_position(), ca.size)
		if rect.has_point(pos):
			return ca
	return null

func _zoom_at(chart_area: _ChartArea, pivot: Vector2, factor: float) -> void:
	_zoom_level = clamp(_zoom_level * factor, 0.5, 5.0)
	# Adjust pan to zoom toward pivot
	var local_pivot = pivot - chart_area.get_global_position() - _pan_offset
	_pan_offset = pivot - chart_area.get_global_position() - local_pivot * factor
	for ca in _chart_areas.values():
		ca._zoom_level = _zoom_level
		ca._pan_offset = _pan_offset
		ca.queue_redraw()

func _reset_view() -> void:
	_zoom_level = 1.0
	_pan_offset = Vector2.ZERO
	for ca in _chart_areas.values():
		ca._zoom_level = 1.0
		ca._pan_offset = Vector2.ZERO
		ca.queue_redraw()

# ── Inner Class: Chart Drawing Area ────────────────────────────────────────────

class _ChartArea extends Control:
	var _series: Array[float] = []
	var _ma_series: Array[float] = []
	var _trend: String = "stable"
	var metric_meta: Dictionary = {}
	var panel_ref: Object = null
	var _zoom_level: float = 1.0
	var _pan_offset: Vector2 = Vector2.ZERO
	var _drag_start_pos: Vector2 = Vector2.ZERO
	var _y_min: float = 0.0
	var _y_max: float = 1.0
	var _y_range: float = 1.0

	func set_series(series: Array[float], ma_series: Array[float]) -> void:
		_series = series
		_ma_series = ma_series
		_compute_y_range()

	func set_trend(trend: String) -> void:
		_trend = trend

	func _compute_y_range() -> void:
		var all_values: Array[float] = _series.duplicate()
		all_values.append_array(_ma_series)
		
		if all_values.is_empty():
			_y_min = 0.0
			_y_max = 1.0
			_y_range = 1.0
			return
		
		_y_min = all_values.min()
		_y_max = all_values.max()
		_y_range = _y_max - _y_min
		
		if _y_range < 1e-9:
			_y_range = 1.0
		
		# Add 10% padding
		var padding = _y_range * 0.1
		_y_min -= padding
		_y_max += padding
		_y_range = _y_max - _y_min

	func _draw() -> void:
		if _series.size() < 2:
			_draw_empty_state()
			return
		
		var w = size.x
		var h = size.y
		var pad = 40.0  # Extra padding for labels
		
		# Draw background grid
		_draw_grid(w, h, pad)
		
		# Draw MA series (behind main)
		if _ma_series.size() >= 2:
			_draw_series(_ma_series, metric_meta["color"] * Color(1, 1, 1, 0.35), 1.5, w, h, pad)
		
		# Draw main series
		_draw_series(_series, metric_meta["color"], 2.5, w, h, pad)
		
		# Draw Y-axis labels
		_draw_y_labels(w, h, pad)
		
		# Draw X-axis labels (dates)
		_draw_x_labels(w, h, pad)
		
		# Draw hover tooltip
		if panel_ref and panel_ref._hovered_chart == self and panel_ref._hover_point_idx >= 0:
			_draw_tooltip(w, h, pad)

	func _draw_empty_state() -> void:
		var w = size.x
		var h = size.y
		draw_string(ThemeDB.fallback_font, Vector2(w/2, h/2), "No data", HORIZONTAL_ALIGNMENT_CENTER, -1, 14, Color("#7B8794"))

	func _draw_grid(w: float, h: float, pad: float) -> void:
		var grid_color = Color(0.3, 0.35, 0.42, 0.5)
		var axis_color = Color(0.4, 0.45, 0.52, 0.8)
		
		# Horizontal grid lines (5)
		for i in 5:
			var gy = pad + (h - 2 * pad) * i / 4.0
			draw_line(Vector2(pad, gy), Vector2(w - pad, gy), grid_color, 0.5)
		
		# Vertical grid lines (6)
		for i in 6:
			var gx = pad + (w - 2 * pad) * i / 5.0
			draw_line(Vector2(gx, pad), Vector2(gx, h - pad), grid_color, 0.5)
		
		# Zero line if in range
		if _y_min < 0 and _y_max > 0:
			var zero_y = (h - pad) - (h - 2 * pad) * (0.0 - _y_min) / _y_range
			draw_line(Vector2(pad, zero_y), Vector2(w - pad, zero_y), axis_color, 1.0)

	func _draw_series(series: Array[float], color: Color, width: float, w: float, h: float, pad: float) -> void:
		var n = series.size()
		var prev_pt = _data_to_screen(series[0], 0, n, w, h, pad)
		
		for si in range(1, n):
			var pt = _data_to_screen(series[si], si, n, w, h, pad)
			draw_line(prev_pt, pt, color, width * _zoom_level)
			prev_pt = pt

	func _data_to_screen(value: float, idx: int, total: int, w: float, h: float, pad: float) -> Vector2:
		var x: float = pad + (w - 2 * pad) * float(idx) / float(max(1, total - 1))
		var y: float = (h - pad) - (h - 2 * pad) * (value - _y_min) / _y_range
		
		# Apply zoom and pan
		var center = Vector2(w/2, h/2)
		x = center.x + (x - center.x) * _zoom_level + _pan_offset.x
		y = center.y + (y - center.y) * _zoom_level + _pan_offset.y
		
		return Vector2(x, y)

	func _draw_y_labels(w: float, h: float, pad: float) -> void:
		var label_color = Color("#7B8794")
		var font_size = 10
		
		for i in 5:
			var val = _y_min + _y_range * (4 - i) / 4.0
			var gy = pad + (h - 2 * pad) * i / 4.0
			var text = _short_num(val)
			draw_string(ThemeDB.fallback_font, Vector2(4, gy + font_size/2), text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, label_color)

	func _draw_x_labels(w: float, h: float, pad: float) -> void:
		var label_color = Color("#7B8794")
		var font_size = 10
		var n = _series.size()
		
		if n < 2:
			return
		
		# Show 4-5 date labels
		var step = max(1, (n - 1) / 4)
		for i in range(0, n, step):
			var pt = _data_to_screen(_series[i], i, n, w, h, pad)
			var day_offset = n - 1 - i
			var day_text = _format_day_offset(day_offset)
			draw_string(ThemeDB.fallback_font, Vector2(pt.x, h - pad + 18), day_text, HORIZONTAL_ALIGNMENT_CENTER, -1, font_size, label_color)

	func _format_day_offset(days_ago: int) -> String:
		if days_ago == 0:
			return "Today"
		elif days_ago == 1:
			return "1d"
		elif days_ago < 7:
			return "%dd" % days_ago
		else:
			return "%dw" % (days_ago / 7)

	func _draw_tooltip(w: float, h: float, pad: float) -> void:
		var idx = panel_ref._hover_point_idx
		if idx < 0 or idx >= _series.size():
			return
		
		var value = _series[idx]
		var ma_value = _ma_series[idx] if idx < _ma_series.size() else 0.0
		
		var pt = _data_to_screen(value, idx, _series.size(), w, h, pad)
		var tooltip_x = pt.x + 12
		var tooltip_y = pt.y - 60
		
		# Keep tooltip on screen
		tooltip_x = clamp(tooltip_x, 10, w - 160)
		tooltip_y = clamp(tooltip_y, 10, h - 80)
		
		var bg_rect = Rect2(tooltip_x, tooltip_y, 150, 70)
		var tooltip_bg = Color(0.1, 0.12, 0.15, 0.95)
		var tooltip_border = metric_meta["color"]
		
		draw_rect(bg_rect, tooltip_bg, true)
		draw_rect(bg_rect, tooltip_border, false, 1.5, 6)
		
		var text_color = Color("#ECEFF4")
		var font_size = 11
		var y_off = tooltip_y + 14
		
		draw_string(ThemeDB.fallback_font, Vector2(tooltip_x + 10, y_off), metric_meta["label"], HORIZONTAL_ALIGNMENT_LEFT, -1, font_size + 1, text_color)
		y_off += 18
		draw_string(ThemeDB.fallback_font, Vector2(tooltip_x + 10, y_off), "Current: %s" % _format_value_full(value), HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, text_color)
		y_off += 16
		if _ma_series.size() > idx:
			draw_string(ThemeDB.fallback_font, Vector2(tooltip_x + 10, y_off), "7d Avg: %s" % _format_value_full(ma_value), HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color("#88C0D0"))

	func _format_value_full(value: float) -> String:
		var meta = metric_meta
		var fmt = "%." + str(meta["decimals"]) + "f"
		return (fmt % value) + " " + meta["unit"]

	func _short_num(v: float) -> String:
		if absf(v) >= 1_000_000.0:
			return "%.1fM" % (v / 1_000_000.0)
		if absf(v) >= 1_000.0:
			return "%.0fk" % (v / 1_000.0)
		return "%.1f" % v

	func _get_closest_point_index(pos: Vector2) -> int:
		if _series.size() < 2:
			return -1
		
		var w = size.x
		var h = size.y
		var pad = 40.0
		
		var best_idx = -1
		var best_dist = TOOLTIP_RADIUS
		
		for i in range(_series.size()):
			var pt = _data_to_screen(_series[i], i, _series.size(), w, h, pad)
			var dist = pos.distance_to(pt + position + _pan_offset)
			if dist < best_dist:
				best_dist = dist
				best_idx = i
		
		return best_idx