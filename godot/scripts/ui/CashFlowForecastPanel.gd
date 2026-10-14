extends Panel
## Cash Flow Forecast Dashboard Panel
## Displays 12-month cash flow projections from MarketCycleSimulator.

# UI references
@onready var chart_container: VBoxContainer = $VBoxContainer/ChartContainer
@onready var forecast_grid: GridContainer = $VBoxContainer/ForecastGrid
@onready var cumulative_label: Label = $VBoxContainer/CumulativeCashLabel
@onready var runway_label: Label = $VBoxContainer/RunwayLabel
@onready var refresh_button: Button = $VBoxContainer/RefreshButton

# GameBridge reference
var game_bridge: Node

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
		push_error("CashFlowForecastPanel: GameBridge not found")
		return
	
	# Get cash flow forecast from GameBridge
	var forecast: Array = game_bridge.GetCashFlowForecast()
	
	if forecast.is_empty():
		push_warning("CashFlowForecastPanel: No forecast data available")
		return
	
	_update_forecast_grid(forecast)
	_update_summary(forecast)

func _update_forecast_grid(forecast: Array) -> void:
	if not forecast_grid:
		return
	
	# Clear existing rows
	for child in forecast_grid.get_children():
		child.queue_free()
	
	# Header row
	_add_grid_label("Month", true)
	_add_grid_label("Operating", true)
	_add_grid_label("Investing", true)
	_add_grid_label("Financing", true)
	_add_grid_label("Net Cash", true)
	_add_grid_label("Cumulative", true)
	
	# Data rows (show first 6 months for readability)
	var display_months: int = min(6, forecast.size())
	for i in range(display_months):
		var month_data: Dictionary = forecast[i]
		
		_add_grid_label("M%d" % month_data.get("month", i+1))
		_add_grid_label(_format_currency(month_data.get("operating_cash", 0.0)))
		_add_grid_label(_format_currency(month_data.get("investing_cash", 0.0)))
		_add_grid_label(_format_currency(month_data.get("financing_cash", 0.0)))
		
		var net_cash: float = month_data.get("net_cash", 0.0)
		var net_label: Label = _add_grid_label(_format_currency(net_cash))
		LakskongenTheme.set_text_color(net_label, _get_cash_color(net_cash))
		
		var cumulative: float = month_data.get("cumulative_cash", 0.0)
		var cum_label: Label = _add_grid_label(_format_currency(cumulative))
		LakskongenTheme.set_text_color(cum_label, _get_cash_color(cumulative))

func _update_summary(forecast: Array) -> void:
	if forecast.is_empty():
		return
	
	# Final cumulative cash (month 12)
	var final_month: Dictionary = forecast[forecast.size() - 1]
	var final_cumulative: float = final_month.get("cumulative_cash", 0.0)
	
	if cumulative_label:
		cumulative_label.text = "12-Month Cumulative: %s NOK" % _format_currency(final_cumulative)
		LakskongenTheme.set_text_color(cumulative_label, _get_cash_color(final_cumulative))
	
	# Calculate runway (months until cash runs out)
	if runway_label:
		var runway_months: int = _calculate_runway(forecast)
		if runway_months > 12:
			runway_label.text = "Cash Runway: >12 months ✅"
			LakskongenTheme.set_text_color(runway_label, LakskongenTheme.CHART_OK)
		elif runway_months > 6:
			runway_label.text = "Cash Runway: %d months ⚠️" % runway_months
			LakskongenTheme.set_text_color(runway_label, LakskongenTheme.CHART_WARN)
		else:
			runway_label.text = "Cash Runway: %d months 🚨" % runway_months
			LakskongenTheme.set_text_color(runway_label, LakskongenTheme.CHART_CRIT)

func _calculate_runway(forecast: Array) -> int:
	var runway: int = 12
	for i in range(forecast.size()):
		var month_data: Dictionary = forecast[i]
		if month_data.get("cumulative_cash", 0.0) < 0:
			runway = i
			break
	return runway

func _add_grid_label(text: String, is_header: bool = false) -> Label:
	var label: Label = Label.new()
	label.text = text
	if is_header:
		LakskongenTheme.set_size(label, 14)
		LakskongenTheme.set_text_color(label, Color(0.9, 0.9, 0.9))
	forecast_grid.add_child(label)
	return label

func _format_currency(value: float) -> String:
	if value >= 1_000_000_000:
		return "%.2fB" % (value / 1_000_000_000.0)
	elif value >= 1_000_000:
		return "%.1fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.0fK" % (value / 1_000.0)
	else:
		return "%.0f" % value

func _get_cash_color(value: float) -> Color:
	if value > 10_000_000:
		return LakskongenTheme.CHART_OK # Green (healthy)
	elif value > 1_000_000:
		return LakskongenTheme.CHART_WARN # Yellow (caution)
	elif value > 0:
		return LakskongenTheme.CHART_WARN # Orange (warning)
	else:
		return LakskongenTheme.CHART_CRIT # Red (critical)
