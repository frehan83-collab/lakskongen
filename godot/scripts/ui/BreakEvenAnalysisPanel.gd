extends Panel
## Break-Even Analysis Dashboard Panel
## Wired to CostModel for break-even calculation and sensitivity analysis.

# UI references
@onready var fixed_costs_label: Label = $VBoxContainer/FixedCostsLabel
@onready var variable_costs_label: Label = $VBoxContainer/VariableCostsLabel
@onready var unit_price_label: Label = $VBoxContainer/UnitPriceLabel
@onready var break_even_volume_label: Label = $VBoxContainer/BreakEvenVolumeLabel
@onready var break_even_revenue_label: Label = $VBoxContainer/BreakEvenRevenueLabel
@onready var current_volume_label: Label = $VBoxContainer/CurrentVolumeLabel
@onready var margin_of_safety_label: Label = $VBoxContainer/MarginOfSafetyLabel
@onready var contribution_margin_label: Label = $VBoxContainer/ContributionMarginLabel
@onready var status_label: Label = $VBoxContainer/StatusLabel
@onready var refresh_button: Button = $VBoxContainer/RefreshButton

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
		push_error("BreakEvenAnalysisPanel: GameBridge not found")
		return
	
	# Get cost breakdown and production data
	var cost_data: Dictionary = game_bridge.GetCostBreakdown()
	var prod_data: Dictionary = game_bridge.GetProductionCosts()
	
	if cost_data.is_empty() or prod_data.is_empty():
		push_warning("BreakEvenAnalysisPanel: No cost data available")
		return
	
	_calculate_break_even(cost_data, prod_data)

func _calculate_break_even(cost_data: Dictionary, prod_data: Dictionary) -> void:
	# Fixed costs (CAPEX amortized + fixed OPEX)
	var fixed_costs: float = cost_data.get("opex_licenses", 0.0) + cost_data.get("opex_maintenance", 0.0)
	fixed_costs += cost_data.get("capex_total", 0.0) * 0.1 / 12.0 # 10% annual depreciation
	
	# Variable costs per kg
	var variable_cost_per_kg: float = prod_data.get("feed_cost_per_kg", 0.0)
	variable_cost_per_kg += prod_data.get("labor_cost_per_kg", 0.0)
	variable_cost_per_kg += prod_data.get("other_cost_per_kg", 0.0)
	
	# Current market price
	var price_per_kg: float = game_bridge.MarketPricePerKg if game_bridge else 70.0
	
	# Contribution margin
	var contribution_margin: float = price_per_kg - variable_cost_per_kg
	
	# Break-even volume (kg)
	var break_even_kg: float = 0.0
	if contribution_margin > 0:
		break_even_kg = fixed_costs / contribution_margin
	
	# Break-even revenue
	var break_even_revenue: float = break_even_kg * price_per_kg
	
	# Current production volume
	var current_volume: float = prod_data.get("total_biomass_kg", 0.0)
	
	# Margin of safety
	var margin_of_safety_pct: float = 0.0
	if break_even_kg > 0:
		margin_of_safety_pct = ((current_volume - break_even_kg) / current_volume) * 100.0
	
	_update_display(fixed_costs, variable_cost_per_kg, price_per_kg, contribution_margin,
					break_even_kg, break_even_revenue, current_volume, margin_of_safety_pct)

func _update_display(fixed_costs: float, variable_cost_per_kg: float, price_per_kg: float,
					contribution_margin: float, break_even_kg: float, break_even_revenue: float,
					current_volume: float, margin_of_safety_pct: float) -> void:
	
	if fixed_costs_label:
		fixed_costs_label.text = "Fixed Costs: %s NOK/month" % _format_currency(fixed_costs)
	
	if variable_costs_label:
		variable_costs_label.text = "Variable Costs: %.2f NOK/kg" % variable_cost_per_kg
	
	if unit_price_label:
		unit_price_label.text = "Market Price: %.2f NOK/kg" % price_per_kg
	
	if contribution_margin_label:
		contribution_margin_label.text = "Contribution Margin: %.2f NOK/kg" % contribution_margin
		LakskongenTheme.set_text_color(contribution_margin_label, _get_profit_color(contribution_margin))
	
	if break_even_volume_label:
		break_even_volume_label.text = "Break-Even Volume: %s kg" % _format_number(break_even_kg)
	
	if break_even_revenue_label:
		break_even_revenue_label.text = "Break-Even Revenue: %s NOK" % _format_currency(break_even_revenue)
	
	if current_volume_label:
		current_volume_label.text = "Current Volume: %s kg" % _format_number(current_volume)
	
	if margin_of_safety_label:
		margin_of_safety_label.text = "Margin of Safety: %.1f%%" % margin_of_safety_pct
		LakskongenTheme.set_text_color(margin_of_safety_label, _get_safety_color(margin_of_safety_pct))
	
	if status_label:
		status_label.text = _get_break_even_status(current_volume, break_even_kg, margin_of_safety_pct)

func _get_break_even_status(current_volume: float, break_even_kg: float, margin_pct: float) -> String:
	if current_volume > break_even_kg * 1.5:
		return "✅ Well Above Break-Even (%.0f%% safety margin)" % margin_pct
	elif current_volume > break_even_kg:
		return "✅ Above Break-Even (%.0f%% safety margin)" % margin_pct
	elif current_volume > break_even_kg * 0.9:
		return "⚠️ Near Break-Even (%.0f%% safety margin)" % margin_pct
	else:
		return "🚨 Below Break-Even - Operating at a loss!"

func _format_currency(value: float) -> String:
	if value >= 1_000_000_000:
		return "%.2fB" % (value / 1_000_000_000.0)
	elif value >= 1_000_000:
		return "%.1fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.0fK" % (value / 1_000.0)
	else:
		return "%.0f" % value

func _format_number(value: float) -> String:
	if value >= 1_000_000:
		return "%.2fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.1fK" % (value / 1_000.0)
	else:
		return "%.0f" % value

func _get_profit_color(value: float) -> Color:
	return LakskongenTheme.CHART_OK if value > 0 else LakskongenTheme.CHART_CRIT

func _get_safety_color(margin_pct: float) -> Color:
	if margin_pct > 30.0:
		return LakskongenTheme.CHART_OK # Healthy
	elif margin_pct > 15.0:
		return LakskongenTheme.CHART_OK # Good
	elif margin_pct > 0:
		return LakskongenTheme.CHART_WARN # Caution
	else:
		return LakskongenTheme.CHART_CRIT # Critical
