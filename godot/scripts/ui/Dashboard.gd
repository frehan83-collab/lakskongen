extends Control

## Dashboard component for displaying key performance indicators
## Shows farm stats, production metrics, health info, and market data

# Farm Section UI Elements (nullable - safe access via helper methods)
@onready var farm_count_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/FarmsSection/FarmCount/FarmCountValue")
@onready var total_capacity_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/FarmsSection/TotalCapacity/TotalCapacityValue")
@onready var current_stock_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/FarmsSection/CurrentStock/CurrentStockValue")

# Production Section UI Elements (nullable - safe access via helper methods)
@onready var avg_growth_rate_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/ProductionSection/AvgGrowthRate/AvgGrowthRateValue")
@onready var avg_weight_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/ProductionSection/AvgWeight/AvgWeightValue")
@onready var est_harvest_value_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/ProductionSection/EstHarvestValue/EstHarvestValueValue")

# Health Section UI Elements (nullable - safe access via helper methods)
@onready var overall_health_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/HealthSection/OverallHealth/OverallHealthValue")
@onready var avg_lice_count_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/HealthSection/AvgLiceCount/AvgLiceCountValue")
@onready var mortality_rate_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/HealthSection/MortalityRate/MortalityRateValue")

# Market Section UI Elements (nullable - safe access via helper methods)
@onready var current_price_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/MarketSection/CurrentPrice/CurrentPriceValue")
@onready var price_trend_value: Label = _safe_get_node("ScrollContainer/VBoxContainer/MarketSection/PriceTrend/PriceTrendValue")

# References to autoloads
var game_bridge: Node
var event_bus: Node

# Cached data for calculations
var cached_farms_data = {}
var last_update_day: int = -1

# Safe node access helpers
func _safe_get_node(node_path: String) -> Node:
	"""Safely get a child node, returning null if not found instead of crashing"""
	if has_node(node_path):
		return get_node(node_path)
	else:
		push_warning("Dashboard: UI node not found: %s - graceful degradation enabled" % node_path)
		return null

func _safe_set_text(label: Label, text: String) -> void:
	"""Safely set label text, skipping if label is null"""
	if label != null:
		label.text = text

func _safe_set_modulate(label: Label, color: Color) -> void:
	"""Safely set label modulate color, skipping if label is null"""
	if label != null:
		label.modulate = color

func _ready():
	# Get autoload references with safe fallback
	if has_node("/root/GameBridge"):
		game_bridge = get_node("/root/GameBridge")
	else:
		push_warning("Dashboard: GameBridge autoload not found - limited functionality")
	
	if has_node("/root/EventBus"):
		event_bus = get_node("/root/EventBus")
	else:
		push_warning("Dashboard: EventBus autoload not found - live updates disabled")
	
	# Connect to EventBus signals for live updates
	if event_bus:
		event_bus.day_advanced.connect(_on_day_advanced)
		event_bus.finance_updated.connect(_on_finance_updated)
		event_bus.growth_report.connect(_on_growth_report)
		event_bus.disease_outbreak.connect(_on_disease_outbreak)
		event_bus.lice_warning.connect(_on_lice_warning)
		event_bus.mortality_report.connect(_on_mortality_report)
		event_bus.market_price_changed.connect(_on_market_price_changed)
		event_bus.harvest_completed.connect(_on_harvest_completed)
		event_bus.fish_stocked.connect(_on_fish_stocked)
		event_bus.farm_built.connect(_on_farm_built)
	
	# Connect to GameBridge signals
	if game_bridge:
		game_bridge.game_state_updated.connect(_on_game_state_updated)
		game_bridge.simulation_ticked.connect(_on_simulation_ticked)
	
	# Initialize display
	_update_all_kpis()
	print("Dashboard initialized")

func _update_all_kpis():
	"""Update all KPI displays with current game state"""
	if not game_bridge or not game_bridge.GameState:
		_reset_displays()
		return
	
	var game_state = game_bridge.GameState
	
	# Update all sections
	_update_farm_stats(game_state)
	_update_production_stats(game_state)
	_update_health_stats(game_state)
	_update_market_stats(game_state)

func _reset_displays():
	"""Reset all displays to default values"""
	# Farm stats
	_safe_set_text(farm_count_value, "0")
	_safe_set_text(total_capacity_value, "0")
	_safe_set_text(current_stock_value, "0")
	
	# Production stats
	_safe_set_text(avg_growth_rate_value, "0%")
	_safe_set_text(avg_weight_value, "0.0kg")
	_safe_set_text(est_harvest_value_value, "$0")
	
	# Health stats
	_safe_set_text(overall_health_value, "Unknown")
	_safe_set_text(avg_lice_count_value, "0.0")
	_safe_set_text(mortality_rate_value, "0%")
	
	# Market stats
	_safe_set_text(current_price_value, "$0/kg")
	_safe_set_text(price_trend_value, "Unknown")

func _update_farm_stats(game_state):
	"""Update farm-related statistics"""
	var farm_count = 0
	var total_capacity = 0
	var current_stock = 0
	
	# TODO: Replace with actual farm data from game_state when available
	# For now, use placeholder logic
	if game_state and game_state.Farms and game_state.Farms.size() > 0:
		farm_count = game_state.Farms.size()
		
		for farm in game_state.Farms:
			total_capacity += farm.Capacity if farm.has("Capacity") else 0
			current_stock += farm.CurrentStock if farm.has("CurrentStock") else 0
	
	_safe_set_text(farm_count_value, str(farm_count))
	_safe_set_text(total_capacity_value, _format_number(total_capacity))
	_safe_set_text(current_stock_value, _format_number(current_stock))

func _update_production_stats(game_state):
	"""Update production-related statistics"""
	var avg_growth_rate = 0.0
	var avg_weight = 0.0
	var est_harvest_value = 0
	
	# TODO: Calculate actual production stats from game_state
	# For now, use placeholder calculations
	
	_safe_set_text(avg_growth_rate_value, "%.1f%%" % avg_growth_rate)
	_safe_set_text(avg_weight_value, "%.1fkg" % avg_weight)
	_safe_set_text(est_harvest_value_value, _format_currency(est_harvest_value))

func _update_health_stats(game_state):
	"""Update health-related statistics"""
	var overall_health = "Good"
	var avg_lice_count = 0.0
	var mortality_rate = 0.0
	
	# TODO: Calculate actual health stats from game_state
	# For now, use placeholder logic
	
	# Color-code health status
	var health_color = Color.WHITE
	match overall_health:
		"Excellent":
			health_color = Color.GREEN
		"Good":
			health_color = Color.LIGHT_GREEN
		"Fair":
			health_color = Color.YELLOW
		"Poor":
			health_color = Color.ORANGE
		"Critical":
			health_color = Color.RED
	
	_safe_set_modulate(overall_health_value, health_color)
	_safe_set_text(overall_health_value, overall_health)
	
	# Color-code lice count
	var lice_color = Color.WHITE
	if avg_lice_count > 5.0:
		lice_color = Color.RED
	elif avg_lice_count > 2.0:
		lice_color = Color.ORANGE
	
	_safe_set_modulate(avg_lice_count_value, lice_color)
	_safe_set_text(avg_lice_count_value, "%.1f" % avg_lice_count)
	
	# Color-code mortality rate
	var mortality_color = Color.WHITE
	if mortality_rate > 5.0:
		mortality_color = Color.RED
	elif mortality_rate > 2.0:
		mortality_color = Color.ORANGE
	
	_safe_set_modulate(mortality_rate_value, mortality_color)
	_safe_set_text(mortality_rate_value, "%.1f%%" % mortality_rate)

func _update_market_stats(game_state):
	"""Update market-related statistics"""
	var current_price = 0.0
	var price_trend = "Stable"
	
	# TODO: Get actual market data from game_state
	# For now, use placeholder values
	if game_state and game_state.has("MarketPrice"):
		current_price = game_state.MarketPrice
	
	_safe_set_text(current_price_value, "$%.2f/kg" % current_price)
	
	# Color-code price trend
	var trend_color = Color.WHITE
	match price_trend:
		"Rising", "Strong Up":
			trend_color = Color.GREEN
		"Falling", "Strong Down":
			trend_color = Color.RED
		"Volatile":
			trend_color = Color.ORANGE
	
	_safe_set_modulate(price_trend_value, trend_color)
	_safe_set_text(price_trend_value, price_trend)

func _format_number(number: int) -> String:
	"""Format large numbers with suffixes"""
	if number >= 1000000:
		return "%.1fM" % (number / 1000000.0)
	elif number >= 1000:
		return "%.1fK" % (number / 1000.0)
	else:
		return str(number)

func _format_currency(amount: int) -> String:
	"""Format currency with appropriate suffixes"""
	var abs_amount = abs(amount)
	var sign = "-" if amount < 0 else ""
	
	if abs_amount >= 1000000000:
		return sign + "$%.1fB" % (abs_amount / 1000000000.0)
	elif abs_amount >= 1000000:
		return sign + "$%.1fM" % (abs_amount / 1000000.0)
	elif abs_amount >= 1000:
		return sign + "$%.0fK" % (abs_amount / 1000.0)
	else:
		return sign + "$%d" % abs_amount

# === EventBus Signal Handlers ===

func _on_day_advanced(day: int, season: String):
	# Update displays periodically, not every day for performance
	if day % 7 == 0:  # Update weekly
		_update_all_kpis()

func _on_finance_updated(cash: int, net_worth: int):
	# Finance changes might affect harvest value estimates
	_update_production_stats(game_bridge.GameState if game_bridge else null)

func _on_growth_report(farm_id: int, avg_growth_rate: float, total_fish: int):
	# Update production stats when growth is reported
	_update_production_stats(game_bridge.GameState if game_bridge else null)

func _on_disease_outbreak(farm_id: int, disease_name: String, affected_fish: int):
	# Disease affects health stats
	_update_health_stats(game_bridge.GameState if game_bridge else null)

func _on_lice_warning(farm_id: int, lice_count: float, severity: String):
	# Lice affects health stats
	_update_health_stats(game_bridge.GameState if game_bridge else null)

func _on_mortality_report(farm_id: int, deaths: int, cause: String):
	# Mortality affects health and production stats
	_update_health_stats(game_bridge.GameState if game_bridge else null)
	_update_production_stats(game_bridge.GameState if game_bridge else null)

func _on_market_price_changed(price_per_kg: float, trend: String):
	# Update market display immediately
	_safe_set_text(current_price_value, "$%.2f/kg" % price_per_kg)
	_safe_set_text(price_trend_value, trend)
	
	# Update price trend color
	var trend_color = Color.WHITE
	match trend:
		"Rising", "Strong Up":
			trend_color = Color.GREEN
		"Falling", "Strong Down":
			trend_color = Color.RED
		"Volatile":
			trend_color = Color.ORANGE
	
	_safe_set_modulate(price_trend_value, trend_color)

func _on_harvest_completed(farm_id: int, fish_count: int, avg_weight: float, revenue: int):
	# Harvest affects farm and production stats
	_update_farm_stats(game_bridge.GameState if game_bridge else null)
	_update_production_stats(game_bridge.GameState if game_bridge else null)

func _on_fish_stocked(farm_id: int, fish_count: int, avg_weight: float):
	# Stocking affects farm stats
	_update_farm_stats(game_bridge.GameState if game_bridge else null)

func _on_farm_built(farm_id: int, location: String, cost: int):
	# New farm affects farm stats
	_update_farm_stats(game_bridge.GameState if game_bridge else null)

# === GameBridge Signal Handlers ===

func _on_game_state_updated():
	_update_all_kpis()

func _on_simulation_ticked(day: int):
	# Store day for periodic updates
	last_update_day = day