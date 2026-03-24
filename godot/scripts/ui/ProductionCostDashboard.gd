extends Panel
## Production Cost Dashboard Panel
## Wired to CostModel for per-kg cost analysis and benchmarking.

# UI references
@onready var total_cost_label: Label = $VBoxContainer/TotalCostLabel
@onready var feed_cost_label: Label = $VBoxContainer/FeedCostLabel
@onready var labor_cost_label: Label = $VBoxContainer/LaborCostLabel
@onready var maintenance_cost_label: Label = $VBoxContainer/MaintenanceCostLabel
@onready var other_cost_label: Label = $VBoxContainer/OtherCostLabel
@onready var biomass_label: Label = $VBoxContainer/BiomassLabel
@onready var fcr_label: Label = $VBoxContainer/FcrLabel
@onready var benchmark_label: Label = $VBoxContainer/BenchmarkLabel
@onready var vs_benchmark_label: Label = $VBoxContainer/VsBenchmarkLabel
@onready var efficiency_label: Label = $VBoxContainer/EfficiencyLabel
@onready var refresh_button: Button = $VBoxContainer/RefreshButton

var game_bridge: Node

func _ready() -> void:
	game_bridge = get_node("/root/GameBridge")
	
	if refresh_button:
		refresh_button.pressed.connect(_on_refresh_pressed)
	
	if game_bridge:
		game_bridge.game_state_updated.connect(_on_game_state_updated)
	
	_refresh_data()

func _on_game_state_updated() -> void:
	_refresh_data()

func _on_refresh_pressed() -> void:
	_refresh_data()

func _refresh_data() -> void:
	if not game_bridge:
		push_error("ProductionCostDashboard: GameBridge not found")
		return
	
	var cost_data: Dictionary = game_bridge.GetProductionCosts()
	
	if cost_data.is_empty():
		push_warning("ProductionCostDashboard: No production cost data available")
		return
	
	_update_display(cost_data)

func _update_display(cost_data: Dictionary) -> void:
	var total_cost_per_kg: float = cost_data.get("total_cost_per_kg", 0.0)
	var feed_cost_per_kg: float = cost_data.get("feed_cost_per_kg", 0.0)
	var labor_cost_per_kg: float = cost_data.get("labor_cost_per_kg", 0.0)
	var maintenance_cost_per_kg: float = cost_data.get("maintenance_cost_per_kg", 0.0)
	var other_cost_per_kg: float = cost_data.get("other_cost_per_kg", 0.0)
	var total_biomass_kg: float = cost_data.get("total_biomass_kg", 0.0)
	var fcr: float = cost_data.get("fcr", 1.2)
	var benchmark_cost_per_kg: float = cost_data.get("benchmark_cost_per_kg", 55.0)
	var vs_benchmark_pct: float = cost_data.get("vs_benchmark_pct", 0.0)
	
	if total_cost_label:
		total_cost_label.text = "Total Cost per kg: %.2f NOK" % total_cost_per_kg
		total_cost_label.add_theme_color_override("font_color", _get_cost_efficiency_color(total_cost_per_kg, benchmark_cost_per_kg))
	
	if feed_cost_label:
		feed_cost_label.text = "Feed: %.2f NOK/kg (%.1f%%)" % [feed_cost_per_kg, _get_pct(feed_cost_per_kg, total_cost_per_kg)]
	
	if labor_cost_label:
		labor_cost_label.text = "Labor: %.2f NOK/kg (%.1f%%)" % [labor_cost_per_kg, _get_pct(labor_cost_per_kg, total_cost_per_kg)]
	
	if maintenance_cost_label:
		maintenance_cost_label.text = "Maintenance: %.2f NOK/kg (%.1f%%)" % [maintenance_cost_per_kg, _get_pct(maintenance_cost_per_kg, total_cost_per_kg)]
	
	if other_cost_label:
		other_cost_label.text = "Other: %.2f NOK/kg (%.1f%%)" % [other_cost_per_kg, _get_pct(other_cost_per_kg, total_cost_per_kg)]
	
	if biomass_label:
		biomass_label.text = "Total Biomass: %s kg" % _format_number(total_biomass_kg)
	
	if fcr_label:
		fcr_label.text = "FCR: %.2f" % fcr
		fcr_label.add_theme_color_override("font_color", _get_fcr_color(fcr))
	
	if benchmark_label:
		benchmark_label.text = "Industry Benchmark: %.2f NOK/kg" % benchmark_cost_per_kg
	
	if vs_benchmark_label:
		var comparison: String = "Above" if vs_benchmark_pct > 0 else "Below"
		vs_benchmark_label.text = "vs Benchmark: %.1f%% %s" % [abs(vs_benchmark_pct), comparison]
		vs_benchmark_label.add_theme_color_override("font_color", _get_benchmark_color(vs_benchmark_pct))
	
	if efficiency_label:
		efficiency_label.text = _get_efficiency_verdict(total_cost_per_kg, benchmark_cost_per_kg, fcr)

func _get_pct(part: float, total: float) -> float:
	return (part / total) * 100.0 if total > 0 else 0.0

func _get_efficiency_verdict(cost: float, benchmark: float, fcr: float) -> String:
	if cost < benchmark * 0.85 and fcr < 1.1:
		return "✅ Excellent Efficiency - Top quartile performance"
	elif cost < benchmark and fcr < 1.2:
		return "✅ Good Efficiency - Above industry average"
	elif cost < benchmark * 1.1:
		return "⚠️ Average Efficiency - Room for improvement"
	elif cost < benchmark * 1.25:
		return "⚠️ Below Average - Cost optimization needed"
	else:
		return "🚨 Poor Efficiency - Urgent action required"

func _format_number(value: float) -> String:
	if value >= 1_000_000:
		return "%.2fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.1fK" % (value / 1_000.0)
	else:
		return "%.0f" % value

func _get_cost_efficiency_color(cost: float, benchmark: float) -> Color:
	var ratio: float = cost / benchmark if benchmark > 0 else 1.0
	
	if ratio < 0.85:
		return Color(0.2, 0.8, 0.2) # Excellent
	elif ratio < 1.0:
		return Color(0.6, 0.8, 0.4) # Good
	elif ratio < 1.1:
		return Color(0.8, 0.8, 0.2) # Average
	elif ratio < 1.25:
		return Color(0.8, 0.6, 0.2) # Below average
	else:
		return Color(0.8, 0.2, 0.2) # Poor

func _get_fcr_color(fcr: float) -> Color:
	if fcr < 1.1:
		return Color(0.2, 0.8, 0.2) # Excellent
	elif fcr < 1.2:
		return Color(0.6, 0.8, 0.4) # Good
	elif fcr < 1.3:
		return Color(0.8, 0.8, 0.2) # Average
	else:
		return Color(0.8, 0.2, 0.2) # Poor

func _get_benchmark_color(vs_benchmark_pct: float) -> Color:
	if vs_benchmark_pct < -15.0:
		return Color(0.2, 0.8, 0.2) # Well below (good)
	elif vs_benchmark_pct < 0:
		return Color(0.6, 0.8, 0.4) # Below (good)
	elif vs_benchmark_pct < 10.0:
		return Color(0.8, 0.8, 0.2) # Slightly above (caution)
	else:
		return Color(0.8, 0.2, 0.2) # Well above (poor)
