extends Panel
## Market Price Chart Dashboard Panel
## Wired to MarketCycleSimulator for real-time and historical price data.

# UI references
@onready var chart_display: Control = $VBoxContainer/ChartDisplay
@onready var current_price_label: Label = $VBoxContainer/CurrentPriceLabel
@onready var weekly_avg_label: Label = $VBoxContainer/WeeklyAvgLabel
@onready var price_trend_label: Label = $VBoxContainer/PriceTrendLabel
@onready var volume_label: Label = $VBoxContainer/VolumeLabel
@onready var refresh_button: Button = $VBoxContainer/RefreshButton

var game_bridge: Node
var price_history: Array = []

func _ready() -> void:
	game_bridge = get_node("/root/GameBridge")
	
	if refresh_button:
		refresh_button.pressed.connect(_on_refresh_pressed)
	
	if game_bridge:
		game_bridge.GameStateUpdated.connect(_on_game_state_updated)
	
	_refresh_data()

func _on_game_state_updated() -> void:
	_refresh_data()

func _on_refresh_pressed() -> void:
	_refresh_data()

func _refresh_data() -> void:
	if not game_bridge:
		push_error("MarketPriceChartPanel: GameBridge not found")
		return
	
	# Get market price data from GameBridge
	price_history = game_bridge.GetMarketPrices()
	
	if price_history.is_empty():
		push_warning("MarketPriceChartPanel: No market price data available")
		return
	
	_update_display()
	_draw_chart()

func _update_display() -> void:
	if price_history.is_empty():
		return
	
	# Latest price data
	var latest: Dictionary = price_history[price_history.size() - 1]
	var current_price: float = latest.get("price_per_kg", 70.0)
	var weekly_avg: float = latest.get("weekly_avg", 70.0)
	var volume: float = latest.get("volume_tonnes", 0.0)
	
	if current_price_label:
		current_price_label.text = "Current Price: %.2f NOK/kg" % current_price
		current_price_label.add_theme_color_override("font_color", _get_price_color(current_price))
	
	if weekly_avg_label:
		weekly_avg_label.text = "Weekly Average: %.2f NOK/kg" % weekly_avg
	
	if volume_label:
		volume_label.text = "Daily Volume: %.0f tonnes" % volume
	
	# Calculate price trend
	if price_trend_label and price_history.size() >= 7:
		var trend: String = _calculate_trend()
		price_trend_label.text = "7-Day Trend: %s" % trend
		price_trend_label.add_theme_color_override("font_color", _get_trend_color(trend))

func _calculate_trend() -> String:
	if price_history.size() < 7:
		return "Insufficient data"
	
	var week_ago_idx: int = max(0, price_history.size() - 7)
	var week_ago_price: float = price_history[week_ago_idx].get("price_per_kg", 70.0)
	var current_price: float = price_history[price_history.size() - 1].get("price_per_kg", 70.0)
	
	var change_pct: float = ((current_price - week_ago_price) / week_ago_price) * 100.0
	
	if change_pct > 5.0:
		return "Strong Uptrend (%.1f%%)" % change_pct
	elif change_pct > 2.0:
		return "Uptrend (%.1f%%)" % change_pct
	elif change_pct > -2.0:
		return "Stable (%.1f%%)" % change_pct
	elif change_pct > -5.0:
		return "Downtrend (%.1f%%)" % change_pct
	else:
		return "Strong Downtrend (%.1f%%)" % change_pct

func _draw_chart() -> void:
	if not chart_display or price_history.is_empty():
		return
	
	# Clear previous drawing
	queue_redraw()

func _draw() -> void:
	if not chart_display or price_history.is_empty():
		return
	
	var chart_size: Vector2 = chart_display.size
	if chart_size.x < 10 or chart_size.y < 10:
		return
	
	# Find min/max prices for scaling
	var min_price: float = 999999.0
	var max_price: float = 0.0
	
	for data in price_history:
		var price: float = data.get("price_per_kg", 70.0)
		min_price = min(min_price, price)
		max_price = max(max_price, price)
	
	var price_range: float = max_price - min_price
	if price_range < 1.0:
		price_range = 10.0 # Prevent division by zero
	
	# Draw price line
	var point_spacing: float = chart_size.x / float(price_history.size() - 1)
	var prev_point: Vector2 = Vector2.ZERO
	
	for i in range(price_history.size()):
		var data: Dictionary = price_history[i]
		var price: float = data.get("price_per_kg", 70.0)
		
		var x: float = i * point_spacing
		var y: float = chart_size.y - ((price - min_price) / price_range) * chart_size.y
		
		var point: Vector2 = Vector2(x, y)
		
		if i > 0:
			chart_display.draw_line(prev_point, point, Color(0.2, 0.6, 0.8), 2.0)
		
		# Draw point marker
		chart_display.draw_circle(point, 3.0, Color(0.1, 0.5, 0.9))
		
		prev_point = point
	
	# Draw weekly average line
	if price_history.size() > 0:
		var weekly_avg: float = price_history[price_history.size() - 1].get("weekly_avg", 70.0)
		var avg_y: float = chart_size.y - ((weekly_avg - min_price) / price_range) * chart_size.y
		chart_display.draw_line(Vector2(0, avg_y), Vector2(chart_size.x, avg_y), Color(0.8, 0.4, 0.2), 1.5, true)

func _get_price_color(price: float) -> Color:
	if price > 75.0:
		return Color(0.2, 0.8, 0.2) # High price (good)
	elif price > 70.0:
		return Color(0.6, 0.8, 0.4) # Above average
	elif price > 65.0:
		return Color(0.8, 0.8, 0.2) # Average
	else:
		return Color(0.8, 0.2, 0.2) # Low price (bad)

func _get_trend_color(trend: String) -> Color:
	if "Uptrend" in trend or "Strong Uptrend" in trend:
		return Color(0.2, 0.8, 0.2)
	elif "Downtrend" in trend or "Strong Downtrend" in trend:
		return Color(0.8, 0.2, 0.2)
	else:
		return Color(0.8, 0.8, 0.2)
