extends Control

## PriceGraph — Displays salmon spot price history as a line graph

var price_history: Array = []
var max_history: int = 365
var graph_rect: Rect2
const BREAK_EVEN_PRICE := 63.0
const MARGIN_LEFT := 60.0
const MARGIN_RIGHT := 20.0
const MARGIN_TOP := 30.0
const MARGIN_BOTTOM := 20.0

func _ready():
	custom_minimum_size = Vector2(600, 200)
	_recalc_rect()

func _recalc_rect():
	var s := size if size.x > 0 else custom_minimum_size
	graph_rect = Rect2(
		MARGIN_LEFT, MARGIN_TOP,
		s.x - MARGIN_LEFT - MARGIN_RIGHT,
		s.y - MARGIN_TOP - MARGIN_BOTTOM
	)

func add_price(price: float):
	price_history.append(price)
	if price_history.size() > max_history:
		price_history.pop_front()
	queue_redraw()

func _draw():
	_recalc_rect()
	if price_history.size() < 2:
		return
	
	var font := get_theme_font("font", "Label")
	
	# Background
	draw_rect(graph_rect, LakskongenTheme.PANEL)
	
	# Calculate price range
	var min_price := 999.0
	var max_price := 0.0
	for p in price_history:
		if p < min_price: min_price = p
		if p > max_price: max_price = p
	var padding := (max_price - min_price) * 0.1
	if padding < 5.0: padding = 5.0
	min_price -= padding
	max_price += padding
	var price_range := max_price - min_price
	if price_range < 1.0: price_range = 1.0
	
	# Grid lines at NOK 60, 80, 100
	for grid_price in [60.0, 80.0, 100.0]:
		if grid_price >= min_price and grid_price <= max_price:
			var y: float = graph_rect.position.y + graph_rect.size.y * (1.0 - (grid_price - min_price) / price_range)
			var grid_color := Color(LakskongenTheme.MUTED, 0.3)
			draw_line(Vector2(graph_rect.position.x, y), Vector2(graph_rect.end.x, y), grid_color, 1.0)
			draw_string(font, Vector2(graph_rect.position.x - 55, y + 4), SimFormatters.integer(int(grid_price)) + " NOK", HORIZONTAL_ALIGNMENT_LEFT, -1, LakskongenTheme.FONT_SM, LakskongenTheme.MUTED)
	
	# Break-even line (red)
	var be_y := graph_rect.position.y + graph_rect.size.y * (1.0 - (BREAK_EVEN_PRICE - min_price) / price_range)
	if be_y >= graph_rect.position.y and be_y <= graph_rect.end.y:
		draw_line(Vector2(graph_rect.position.x, be_y), Vector2(graph_rect.end.x, be_y), LakskongenTheme.DANGER, 1.5)
		draw_string(font, Vector2(graph_rect.end.x - 120, be_y - 4), tr("UI_PRICE_BREAK_EVEN") % SimFormatters.integer(63), HORIZONTAL_ALIGNMENT_LEFT, -1, LakskongenTheme.FONT_SM, LakskongenTheme.DANGER)
	
	# Price curve
	var point_count := price_history.size()
	var x_step: float = graph_rect.size.x / max(1, point_count - 1)
	for i in range(point_count - 1):
		var p1: float = float(price_history[i])
		var p2: float = float(price_history[i + 1])
		var x1: float = graph_rect.position.x + i * x_step
		var x2: float = graph_rect.position.x + (i + 1) * x_step
		var y1: float = graph_rect.position.y + graph_rect.size.y * (1.0 - (p1 - min_price) / price_range)
		var y2: float = graph_rect.position.y + graph_rect.size.y * (1.0 - (p2 - min_price) / price_range)
		draw_line(Vector2(x1, y1), Vector2(x2, y2), LakskongenTheme.ACCENT, 2.0)
	
	# Current price dot
	if point_count > 0:
		var last_p: float = float(price_history[-1])
		var last_x: float = graph_rect.position.x + (point_count - 1) * x_step
		var last_y: float = graph_rect.position.y + graph_rect.size.y * (1.0 - (last_p - min_price) / price_range)
		draw_circle(Vector2(last_x, last_y), 5.0, LakskongenTheme.ACCENT)
	
	# Title
	draw_string(font, Vector2(graph_rect.position.x, graph_rect.position.y - 10), tr("UI_PRICE_GRAPH_TITLE"), HORIZONTAL_ALIGNMENT_LEFT, -1, LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
	
	# Axes
	draw_line(Vector2(graph_rect.position.x, graph_rect.end.y), Vector2(graph_rect.end.x, graph_rect.end.y), LakskongenTheme.MUTED, 1.0)
	draw_line(Vector2(graph_rect.position.x, graph_rect.position.y), Vector2(graph_rect.position.x, graph_rect.end.y), LakskongenTheme.MUTED, 1.0)
