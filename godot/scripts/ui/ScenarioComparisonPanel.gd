extends Panel
## Scenario Comparison Dashboard Panel
## Wired to EconomicEngine for strategic scenario analysis and zone comparisons.

# UI references
@onready var scenario_grid: GridContainer = $VBoxContainer/ScenarioGrid
@onready var best_scenario_label: Label = $VBoxContainer/BestScenarioLabel
@onready var recommendation_label: Label = $VBoxContainer/RecommendationLabel
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
		push_error("ScenarioComparisonPanel: GameBridge not found")
		return
	
	var scenarios: Array = game_bridge.GetScenarioComparison()
	
	if scenarios.is_empty():
		push_warning("ScenarioComparisonPanel: No scenario data available")
		return
	
	_update_display(scenarios)

func _update_display(scenarios: Array) -> void:
	_clear_grid()
	_populate_grid(scenarios)
	_update_recommendations(scenarios)

func _clear_grid() -> void:
	if not scenario_grid:
		return
	for child in scenario_grid.get_children():
		child.queue_free()

func _populate_grid(scenarios: Array) -> void:
	if not scenario_grid:
		return
	
	# Header row
	_add_grid_label("Scenario", true)
	_add_grid_label("Revenue", true)
	_add_grid_label("Cost", true)
	_add_grid_label("Profit", true)
	_add_grid_label("ROI %", true)
	_add_grid_label("Risk", true)
	_add_grid_label("Score", true)
	
	# Data rows
	var best_score: float = -999999.0
	var best_scenario_name: String = ""
	
	for scenario_data in scenarios:
		var name: String = scenario_data.get("name", "Unknown")
		var revenue: float = scenario_data.get("revenue", 0.0)
		var cost: float = scenario_data.get("cost", 0.0)
		var profit: float = scenario_data.get("profit", 0.0)
		var roi_pct: float = scenario_data.get("roi_pct", 0.0)
		var risk_score: float = scenario_data.get("risk_score", 5.0)
		
		# Calculate composite score (profit / risk)
		var composite_score: float = profit / max(risk_score, 1.0)
		
		if composite_score > best_score:
			best_score = composite_score
			best_scenario_name = name
		
		_add_grid_label(name)
		_add_grid_label(_format_currency(revenue))
		_add_grid_label(_format_currency(cost))
		
		var profit_label: Label = _add_grid_label(_format_currency(profit))
		LakskongenTheme.set_text_color(profit_label, _get_profit_color(profit))
		
		var roi_label: Label = _add_grid_label("%.1f%%" % roi_pct)
		LakskongenTheme.set_text_color(roi_label, _get_roi_color(roi_pct))
		
		var risk_label: Label = _add_grid_label("%.1f" % risk_score)
		LakskongenTheme.set_text_color(risk_label, _get_risk_color(risk_score))
		
		var score_label: Label = _add_grid_label("%.0f" % composite_score)
		LakskongenTheme.set_text_color(score_label, _get_score_color(composite_score, best_score))
	
	# Highlight best scenario
	if best_scenario_label:
		best_scenario_label.text = "Recommended Strategy: %s" % best_scenario_name
		LakskongenTheme.set_text_color(best_scenario_label, LakskongenTheme.CHART_OK)

func _update_recommendations(scenarios: Array) -> void:
	if not recommendation_label or scenarios.is_empty():
		return
	
	var current_scenario: Dictionary = scenarios[0] # Assume first is current
	var current_profit: float = current_scenario.get("profit", 0.0)
	var current_roi: float = current_scenario.get("roi_pct", 0.0)
	
	var best_profit: float = current_profit
	var best_roi: float = current_roi
	var best_name: String = ""
	
	for i in range(1, scenarios.size()):
		var scenario: Dictionary = scenarios[i]
		var profit: float = scenario.get("profit", 0.0)
		var roi: float = scenario.get("roi_pct", 0.0)
		
		if profit > best_profit:
			best_profit = profit
			best_roi = roi
			best_name = scenario.get("name", "")
	
	if best_profit > current_profit * 1.2:
		recommendation_label.text = "💡 Consider '%s' for %.0f%% profit improvement" % [best_name, ((best_profit - current_profit) / current_profit) * 100.0]
	elif current_roi < 10.0:
		recommendation_label.text = "⚠️ Current operations below target ROI - optimization needed"
	else:
		recommendation_label.text = "✅ Current strategy is optimal for your risk profile"

func _add_grid_label(text: String, is_header: bool = false) -> Label:
	var label: Label = Label.new()
	label.text = text
	if is_header:
		LakskongenTheme.set_size(label, 14)
		LakskongenTheme.set_text_color(label, Color(0.9, 0.9, 0.9))
	label.custom_minimum_size = Vector2(100, 0)
	scenario_grid.add_child(label)
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

func _get_profit_color(value: float) -> Color:
	return LakskongenTheme.CHART_OK if value > 0 else LakskongenTheme.CHART_CRIT

func _get_roi_color(roi_pct: float) -> Color:
	if roi_pct > 20.0:
		return LakskongenTheme.CHART_OK
	elif roi_pct > 15.0:
		return LakskongenTheme.CHART_OK
	elif roi_pct > 10.0:
		return LakskongenTheme.CHART_WARN
	else:
		return LakskongenTheme.CHART_CRIT

func _get_risk_color(risk: float) -> Color:
	if risk < 3.0:
		return LakskongenTheme.CHART_OK # Low risk (good)
	elif risk < 5.0:
		return LakskongenTheme.CHART_WARN # Medium risk
	else:
		return LakskongenTheme.CHART_CRIT # High risk (bad)

func _get_score_color(score: float, best_score: float) -> Color:
	if score >= best_score * 0.95:
		return LakskongenTheme.CHART_OK # Best or near-best
	elif score >= best_score * 0.8:
		return LakskongenTheme.CHART_OK # Good
	else:
		return LakskongenTheme.CHART_WARN # Suboptimal
