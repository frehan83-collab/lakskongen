extends Panel
## Cost Breakdown Dashboard Panel
## Wired to CostModel for detailed OPEX/CAPEX analysis.

# UI references
@onready var opex_container: VBoxContainer = $VBoxContainer/OpexContainer
@onready var capex_container: VBoxContainer = $VBoxContainer/CapexContainer
@onready var opex_total_label: Label = $VBoxContainer/OpexTotalLabel
@onready var capex_total_label: Label = $VBoxContainer/CapexTotalLabel
@onready var cost_ratio_label: Label = $VBoxContainer/CostRatioLabel
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
		push_error("CostBreakdownPanel: GameBridge not found")
		return
	
	var cost_data: Dictionary = game_bridge.GetCostBreakdown()
	
	if cost_data.is_empty():
		push_warning("CostBreakdownPanel: No cost data available")
		return
	
	_update_display(cost_data)

func _update_display(cost_data: Dictionary) -> void:
	# OPEX Breakdown
	_clear_container(opex_container)
	
	var opex_feed: float = cost_data.get("opex_feed", 0.0)
	var opex_labor: float = cost_data.get("opex_labor", 0.0)
	var opex_maintenance: float = cost_data.get("opex_maintenance", 0.0)
	var opex_licenses: float = cost_data.get("opex_licenses", 0.0)
	var opex_delousing: float = cost_data.get("opex_delousing", 0.0)
	var opex_other: float = cost_data.get("opex_other", 0.0)
	var opex_total: float = cost_data.get("opex_total", 0.0)
	
	_add_cost_row(opex_container, "Feed", opex_feed, opex_total)
	_add_cost_row(opex_container, "Labor", opex_labor, opex_total)
	_add_cost_row(opex_container, "Maintenance", opex_maintenance, opex_total)
	_add_cost_row(opex_container, "Licenses", opex_licenses, opex_total)
	_add_cost_row(opex_container, "Delousing", opex_delousing, opex_total)
	_add_cost_row(opex_container, "Other", opex_other, opex_total)
	
	# CAPEX Breakdown
	_clear_container(capex_container)
	
	var capex_licenses: float = cost_data.get("capex_licenses", 0.0)
	var capex_pens: float = cost_data.get("capex_pens", 0.0)
	var capex_equipment: float = cost_data.get("capex_equipment", 0.0)
	var capex_ras: float = cost_data.get("capex_ras", 0.0)
	var capex_offshore: float = cost_data.get("capex_offshore", 0.0)
	var capex_total: float = cost_data.get("capex_total", 0.0)
	
	_add_cost_row(capex_container, "Licenses", capex_licenses, capex_total)
	_add_cost_row(capex_container, "Pens", capex_pens, capex_total)
	_add_cost_row(capex_container, "Equipment", capex_equipment, capex_total)
	_add_cost_row(capex_container, "RAS", capex_ras, capex_total)
	_add_cost_row(capex_container, "Offshore", capex_offshore, capex_total)
	
	# Totals
	if opex_total_label:
		opex_total_label.text = "Total OPEX: %s NOK/month" % _format_currency(opex_total)
	
	if capex_total_label:
		capex_total_label.text = "Total CAPEX: %s NOK" % _format_currency(capex_total)
	
	if cost_ratio_label:
		var ratio: float = 0.0
		if capex_total > 0:
			ratio = opex_total / capex_total * 100.0
		cost_ratio_label.text = "OPEX/CAPEX Ratio: %.2f%%" % ratio

func _clear_container(container: VBoxContainer) -> void:
	if not container:
		return
	for child in container.get_children():
		child.queue_free()

func _add_cost_row(container: VBoxContainer, label_text: String, cost: float, total: float) -> void:
	if not container:
		return
	
	var row: HBoxContainer = HBoxContainer.new()
	
	# Label
	var label: Label = Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(120, 0)
	row.add_child(label)
	
	# Cost amount
	var cost_label: Label = Label.new()
	cost_label.text = _format_currency(cost)
	cost_label.custom_minimum_size = Vector2(100, 0)
	row.add_child(cost_label)
	
	# Percentage bar
	var progress: ProgressBar = ProgressBar.new()
	progress.custom_minimum_size = Vector2(150, 20)
	progress.max_value = 100.0
	var pct: float = 0.0
	if total > 0:
		pct = (cost / total) * 100.0
	progress.value = pct
	row.add_child(progress)
	
	# Percentage label
	var pct_label: Label = Label.new()
	pct_label.text = "%.1f%%" % pct
	pct_label.custom_minimum_size = Vector2(60, 0)
	row.add_child(pct_label)
	
	container.add_child(row)

func _format_currency(value: float) -> String:
	if value >= 1_000_000_000:
		return "%.2fB" % (value / 1_000_000_000.0)
	elif value >= 1_000_000:
		return "%.1fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.0fK" % (value / 1_000.0)
	else:
		return "%.0f" % value
