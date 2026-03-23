## AnalyticsPanel.gd
## Advanced analytics panel displaying mini-charts for the top 4 metrics.
## Uses Line2D nodes for chart lines and _draw() for axes / grid.
## Supports 30-day and 90-day view toggles with trend arrows.
##
## Node layout expected:
##   AnalyticsPanel (Control)
##     VBoxContainer
##       HBoxContainer (toolbar)
##         Label: TitleLabel
##         Button: Btn30d
##         Button: Btn90d
##       GridContainer (2 × 2 grid)
##         ChartContainer_0 … ChartContainer_3  (each a sub-scene or plain Control)
##     (each ChartContainer contains: TitleLabel, TrendLabel, Line2D, MALine2D)
##
## For rapid prototyping the script builds the layout procedurally in _ready().

extends Control

# ── Configuration ──────────────────────────────────────────────────────────────

const METRICS: Array = [
	{ "key": "fish_count",        "label": "Fish Count",        "color": Color("#5E81AC") },
	{ "key": "total_biomass_kg",  "label": "Biomass (kg)",      "color": Color("#A3BE8C") },
	{ "key": "market_price_nok",  "label": "Price (NOK/kg)",    "color": Color(1.0, 0.75, 0.2) },
	{ "key": "balance_nok",       "label": "Balance (NOK)",     "color": Color("#BF616A") },
]

const CHART_W  := 260
const CHART_H  := 120
const PADDING  := 12

# Trend arrow Unicode glyphs
const ARROW_UP   := " ▲"
const ARROW_DOWN := " ▼"
const ARROW_FLAT := " ◆"

# ── State ──────────────────────────────────────────────────────────────────────

var _days: int = 30          # active view window (30 or 90)

# Refs to per-chart nodes (built in _ready)
var _chart_nodes: Array = []  # Array of Dictionaries

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_build_layout()
	_refresh()


func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _process(_delta: float) -> void:
	# Refresh every 2 seconds so the panel stays live without hammering GameBridge.
	if Engine.get_process_frames() % 120 == 0:
		_refresh()

# ── Layout builder ─────────────────────────────────────────────────────────────

func _build_layout() -> void:
	custom_minimum_size = Vector2(560, 320)

	# ── Outer container ──
	var vbox := VBoxContainer.new()
	vbox.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(vbox)

	# ── Toolbar ──
	var toolbar := HBoxContainer.new()
	toolbar.add_theme_constant_override("separation", 8)
	vbox.add_child(toolbar)

	var title_lbl := Label.new()
	title_lbl.text = tr("ui.analytics.title")
	title_lbl.add_theme_font_size_override("font_size", 14)
	title_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_lbl.tooltip_text = tr("ui.analytics.title_tooltip")
	toolbar.add_child(title_lbl)

	var btn30 := Button.new()
	btn30.text = "30d"
	btn30.tooltip_text = "Vis data for de siste 30 dagene."
	btn30.pressed.connect(_on_30d_pressed)
	toolbar.add_child(btn30)

	var btn90 := Button.new()
	btn90.text = "90d"
	btn90.tooltip_text = "Vis data for de siste 90 dagene."
	btn90.pressed.connect(_on_90d_pressed)
	toolbar.add_child(btn90)

	# ── 2×2 chart grid ──
	var grid := GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 12)
	grid.add_theme_constant_override("v_separation", 12)
	grid.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(grid)

	for i in METRICS.size():
		var meta: Dictionary = METRICS[i]
		var chart_nodes := _build_chart_cell(grid, meta)
		_chart_nodes.append(chart_nodes)


func _build_chart_cell(parent: Node, meta: Dictionary) -> Dictionary:
	# Wrapper panel
	var panel := PanelContainer.new()
	panel.custom_minimum_size = Vector2(CHART_W, CHART_H + 36)
	parent.add_child(panel)

	var inner := VBoxContainer.new()
	inner.add_theme_constant_override("separation", 2)
	panel.add_child(inner)

	# Header row: metric name + trend arrow
	var header := HBoxContainer.new()
	inner.add_child(header)

	var name_lbl := Label.new()
	name_lbl.text = meta["label"]
	name_lbl.add_theme_font_size_override("font_size", 11)
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(name_lbl)

	var trend_lbl := Label.new()
	trend_lbl.text = ARROW_FLAT
	trend_lbl.add_theme_font_size_override("font_size", 12)
	header.add_child(trend_lbl)

	# Value label (latest)
	var value_lbl := Label.new()
	value_lbl.text = "–"
	value_lbl.add_theme_font_size_override("font_size", 10)
	value_lbl.modulate = Color("#4C566A")
	inner.add_child(value_lbl)

	# Chart area (drawn procedurally)
	var chart_ctrl := _ChartArea.new()
	chart_ctrl.custom_minimum_size = Vector2(CHART_W, CHART_H)
	chart_ctrl.size_flags_vertical = Control.SIZE_EXPAND_FILL
	chart_ctrl.line_color = meta["color"]
	chart_ctrl.ma_color   = Color(meta["color"].r, meta["color"].g, meta["color"].b, 0.45)
	inner.add_child(chart_ctrl)

	return {
		"chart":     chart_ctrl,
		"trend_lbl": trend_lbl,
		"value_lbl": value_lbl,
		"meta":      meta,
	}

# ── Data refresh ───────────────────────────────────────────────────────────────

func _refresh() -> void:
	if not Engine.has_singleton("GameBridge"):
		return

	var gb = Engine.get_singleton("GameBridge")

	for nodes: Dictionary in _chart_nodes:
		var key: String  = nodes["meta"]["key"]

		# Raw series
		var series: Array = Array(gb.GetAnalyticsData(key, _days))
		# 7-day moving average
		var ma_series: Array = Array(gb.GetAnalyticsMovingAverage(key, _days, 7))
		# Trend
		var trend: String = gb.GetAnalyticsTrend(key, min(_days, 14))

		# Push data into chart area
		var chart: _ChartArea = nodes["chart"]
		chart.set_series(series, ma_series)
		chart.queue_redraw()

		# Trend arrow
		var trend_lbl: Label = nodes["trend_lbl"]
		match trend:
			"up":
				trend_lbl.text    = ARROW_UP
				trend_lbl.modulate = Color("#A3BE8C")
			"down":
				trend_lbl.text    = ARROW_DOWN
				trend_lbl.modulate = Color("#BF616A")
			_:
				trend_lbl.text    = ARROW_FLAT
				trend_lbl.modulate = Color("#4C566A")

		# Latest value
		var value_lbl: Label = nodes["value_lbl"]
		if series.size() > 0:
			var last_val: float = series[-1]
			value_lbl.text = _format_value(key, last_val)
		else:
			value_lbl.text = "–"

# ── Formatting helper ──────────────────────────────────────────────────────────

func _format_value(key: String, value: float) -> String:
	match key:
		"fish_count":
			return "%d fish" % int(value)
		"total_biomass_kg":
			if value >= 1000.0:
				return "%.1f t" % (value / 1000.0)
			return "%.0f kg" % value
		"market_price_nok":
			return "%.2f NOK/kg" % value
		"balance_nok":
			if absf(value) >= 1_000_000.0:
				return "%.2f M NOK" % (value / 1_000_000.0)
			if absf(value) >= 1_000.0:
				return "%.1f k NOK" % (value / 1_000.0)
			return "%.0f NOK" % value
		"temperature_c":
			return "%.1f °C" % value
		"fcr":
			return "FCR %.2f" % value
		"lice_per_fish":
			return "%.2f lice/fish" % value
		"sustainability_score":
			return "%d / 100" % int(value)
		"mortality_rate":
			return "%.3f%%" % (value * 100.0)
		"avg_weight_kg":
			return "%.2f kg" % value
		_:
			return "%.2f" % value

# ── Button handlers ────────────────────────────────────────────────────────────

func _on_30d_pressed() -> void:
	_days = 30
	_refresh()

func _on_90d_pressed() -> void:
	_days = 90
	_refresh()

# ── Inner class: chart drawing area ───────────────────────────────────────────

class _ChartArea extends Control:
	var _series: Array    = []
	var _ma_series: Array = []
	var line_color: Color = Color.WHITE
	var ma_color:   Color = Color(1, 1, 1, 0.4)

	func set_series(series: Array, ma_series: Array) -> void:
		_series    = series
		_ma_series = ma_series

	func _draw() -> void:
		if _series.size() < 2:
			return

		var w := size.x
		var h := size.y
		var pad := 4.0

		# ── Determine Y range ──
		var y_min: float = _series.min()
		var y_max: float = _series.max()
		# Include MA in range
		if _ma_series.size() > 0:
			y_min = min(y_min, _ma_series.min())
			y_max = max(y_max, _ma_series.max())

		var y_range: float = y_max - y_min
		if y_range < 1e-9:
			y_range = 1.0  # avoid divide-by-zero for flat lines

		# ── Grid lines (3 horizontal) ──
		var grid_color := Color(0.85, 0.87, 0.91, 0.5)
		for gi in 3:
			var gy: float = pad + (h - 2 * pad) * gi / 2.0
			draw_line(Vector2(pad, gy), Vector2(w - pad, gy), grid_color, 0.5)

		# ── Helper closure: map data coords to screen ──
		var n := _series.size()

		# ── Draw MA series ──
		if _ma_series.size() >= 2:
			var ma_n := _ma_series.size()
			var prev_pt := _data_to_screen(_ma_series[0], 0, ma_n, y_min, y_range, w, h, pad)
			for si in range(1, ma_n):
				var pt := _data_to_screen(_ma_series[si], si, ma_n, y_min, y_range, w, h, pad)
				draw_line(prev_pt, pt, ma_color, 1.5)
				prev_pt = pt

		# ── Draw main series ──
		var prev_pt := _data_to_screen(_series[0], 0, n, y_min, y_range, w, h, pad)
		for si in range(1, n):
			var pt := _data_to_screen(_series[si], si, n, y_min, y_range, w, h, pad)
			draw_line(prev_pt, pt, line_color, 2.0)
			prev_pt = pt

		# ── Y-axis min/max labels ──
		var label_color := Color("#7B8794")
		draw_string(ThemeDB.fallback_font, Vector2(2, h - 2),
			_short_num(y_min), HORIZONTAL_ALIGNMENT_LEFT, -1, 8, label_color)
		draw_string(ThemeDB.fallback_font, Vector2(2, pad + 10),
			_short_num(y_max), HORIZONTAL_ALIGNMENT_LEFT, -1, 8, label_color)

	func _data_to_screen(
		value: float, idx: int, total: int,
		y_min: float, y_range: float,
		w: float, h: float, pad: float
	) -> Vector2:
		var x: float = pad + (w - 2 * pad) * float(idx) / float(total - 1)
		var y: float = (h - pad) - (h - 2 * pad) * (value - y_min) / y_range
		return Vector2(x, y)

	func _short_num(v: float) -> String:
		if absf(v) >= 1_000_000.0:
			return "%.1fM" % (v / 1_000_000.0)
		if absf(v) >= 1_000.0:
			return "%.0fk" % (v / 1_000.0)
		return "%.1f" % v
