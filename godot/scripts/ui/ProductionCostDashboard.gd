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
		game_bridge.GameStateUpdated.connect(_on_game_state_updated)
	
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
	var fcr: float = cost_data.get("fcr", 0.0)
	var benchmark_cost_per_kg: float = cost_data.get("benchmark_cost_per_kg", 55.0)
	var vs_benchmark_pct: float = cost_data.get("vs_benchmark_pct", 0.0)
	
	if total_cost_label:
		total_cost_label.text = tr("UI_PCD_TOTAL_COST") % SimFormatters.format_price_kg(total_cost_per_kg)
		LakskongenTheme.set_text_color(total_cost_label, _get_cost_efficiency_color(total_cost_per_kg, benchmark_cost_per_kg))
	
	if feed_cost_label:
		feed_cost_label.text = tr("UI_PCD_FEED") % [SimFormatters.format_price_kg(feed_cost_per_kg), SimFormatters.percent(_get_pct(feed_cost_per_kg, total_cost_per_kg) / 100.0, 1)]
	
	if labor_cost_label:
		labor_cost_label.text = tr("UI_PCD_LABOR") % [SimFormatters.format_price_kg(labor_cost_per_kg), SimFormatters.percent(_get_pct(labor_cost_per_kg, total_cost_per_kg) / 100.0, 1)]
	
	if maintenance_cost_label:
		maintenance_cost_label.text = tr("UI_PCD_MAINTENANCE") % [SimFormatters.format_price_kg(maintenance_cost_per_kg), SimFormatters.percent(_get_pct(maintenance_cost_per_kg, total_cost_per_kg) / 100.0, 1)]
	
	if other_cost_label:
		other_cost_label.text = tr("UI_PCD_OTHER") % [SimFormatters.format_price_kg(other_cost_per_kg), SimFormatters.percent(_get_pct(other_cost_per_kg, total_cost_per_kg) / 100.0, 1)]
	
	if biomass_label:
		biomass_label.text = tr("UI_PCD_BIOMASS") % SimFormatters.format_biomass(total_biomass_kg)
	
	if fcr_label:
		fcr_label.text = tr("UI_PCD_FCR") % (SimFormatters.num(fcr, 2) if fcr > 0.0 else "–")
		LakskongenTheme.set_text_color(fcr_label, _get_fcr_color(fcr))
	
	if benchmark_label:
		benchmark_label.text = tr("UI_PCD_BENCHMARK") % SimFormatters.format_price_kg(benchmark_cost_per_kg)
	
	if vs_benchmark_label:
		var comparison_key := "UI_PCD_VS_ABOVE" if vs_benchmark_pct > 0 else "UI_PCD_VS_BELOW"
		vs_benchmark_label.text = tr(comparison_key) % SimFormatters.percent(absf(vs_benchmark_pct) / 100.0, 1)
		LakskongenTheme.set_text_color(vs_benchmark_label, _get_benchmark_color(vs_benchmark_pct))
	
	if efficiency_label:
		efficiency_label.text = _get_efficiency_verdict(total_cost_per_kg, benchmark_cost_per_kg, fcr)

func _get_pct(part: float, total: float) -> float:
	return (part / total) * 100.0 if total > 0 else 0.0

## FCR bands come from the sim's CalibrationRegistry through the bridge
## (target / poor), never from numbers kept in this file (U1-03).
func _fcr_targets() -> Dictionary:
	var sim := get_node_or_null("/root/SimBridge")
	if sim != null and sim.has_method("get_fcr_targets"):
		return sim.get_fcr_targets()
	return {}

func _get_efficiency_verdict(cost: float, benchmark: float, fcr: float) -> String:
	var targets := _fcr_targets()
	var target := float(targets.get("target", 0.0))
	var poor := float(targets.get("poor", 0.0))
	var fcr_good := target > 0.0 and fcr > 0.0 and fcr <= target
	var fcr_ok := poor > 0.0 and fcr > 0.0 and fcr < poor
	if cost < benchmark * 0.85 and fcr_good:
		return tr("UI_PCD_VERDICT_EXCELLENT")
	elif cost < benchmark and fcr_ok:
		return tr("UI_PCD_VERDICT_GOOD")
	elif cost < benchmark * 1.1:
		return tr("UI_PCD_VERDICT_AVERAGE")
	elif cost < benchmark * 1.25:
		return tr("UI_PCD_VERDICT_BELOW")
	else:
		return tr("UI_PCD_VERDICT_POOR")

func _get_cost_efficiency_color(cost: float, benchmark: float) -> Color:
	var ratio: float = cost / benchmark if benchmark > 0 else 1.0
	
	if ratio < 0.85:
		return LakskongenTheme.CHART_OK # Excellent
	elif ratio < 1.0:
		return LakskongenTheme.CHART_OK # Good
	elif ratio < 1.1:
		return LakskongenTheme.CHART_WARN # Average
	elif ratio < 1.25:
		return LakskongenTheme.CHART_WARN # Below average
	else:
		return LakskongenTheme.CHART_CRIT # Poor

func _get_fcr_color(fcr: float) -> Color:
	var targets := _fcr_targets()
	var target := float(targets.get("target", 0.0))
	var poor := float(targets.get("poor", 0.0))
	if fcr <= 0.0 or target <= 0.0:
		return LakskongenTheme.TEXT_MUTED
	if fcr <= target:
		return LakskongenTheme.CHART_OK
	elif fcr < poor:
		return LakskongenTheme.CHART_WARN
	else:
		return LakskongenTheme.CHART_CRIT

func _get_benchmark_color(vs_benchmark_pct: float) -> Color:
	if vs_benchmark_pct < -15.0:
		return LakskongenTheme.CHART_OK # Well below (good)
	elif vs_benchmark_pct < 0:
		return LakskongenTheme.CHART_OK # Below (good)
	elif vs_benchmark_pct < 10.0:
		return LakskongenTheme.CHART_WARN # Slightly above (caution)
	else:
		return LakskongenTheme.CHART_CRIT # Well above (poor)
