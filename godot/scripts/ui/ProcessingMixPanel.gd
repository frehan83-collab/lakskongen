extends PanelContainer
## ProcessingMixPanel - Product mix optimization UI
## Displays 17 products with optimizer recommendations

@onready var product_list = $MarginContainer/VBoxContainer/ScrollContainer/ProductList
@onready var strategy_dropdown = $MarginContainer/VBoxContainer/Controls/StrategyDropdown
@onready var apply_button = $MarginContainer/VBoxContainer/Controls/ApplyButton
@onready var recommendation_label = $MarginContainer/VBoxContainer/Header/RecommendationLabel

var products_data: Array = []
var enabled_products: Dictionary = {}  # ProductType -> bool
var current_recommendation: Dictionary = {}

# Optimization strategies from C# ProductMixOptimizer
const STRATEGIES = [
	"MaximizeProfit",
	"MaximizeVolume",
	"MaximizeMargin",
	"Balanced",
	"MinimizeRisk",
	"PremiumFocus"
]

func _ready():
	_setup_strategy_dropdown()
	apply_button.pressed.connect(_on_apply_button_pressed)
	strategy_dropdown.item_selected.connect(_on_strategy_selected)
	
	_load_product_catalog()
	
	# Connect to GameBridge for updates
	if GameBridge:
		if GameBridge.has_signal("ProcessingDataUpdated"):
			GameBridge.ProcessingDataUpdated.connect(_on_processing_data_updated)

func _setup_strategy_dropdown():
	strategy_dropdown.clear()
	for i in range(STRATEGIES.size()):
		strategy_dropdown.add_item(STRATEGIES[i], i)
	strategy_dropdown.select(3)  # Default to "Balanced"

func _load_product_catalog():
	if not SimBridge:
		push_error("SimBridge not found - cannot load products")
		return
	
	SimBridge.call_deferred("GetProductCatalog")

func _on_processing_data_updated(products_json: String):
	var json = JSON.new()
	var parse_result = json.parse(products_json)
	
	if parse_result != OK:
		push_error("Failed to parse products JSON")
		return
	
	products_data = json.data
	_populate_product_list()
	_request_recommendation()

func _populate_product_list():
	# Clear existing
	for child in product_list.get_children():
		if child.name != "ProductTemplate":
			child.queue_free()
	
	for product in products_data:
		var product_item = _create_product_item(product)
		product_list.add_child(product_item)

func _create_product_item(product: Dictionary) -> Control:
	var item = PanelContainer.new()
	var style = StyleBoxFlat.new()
	style.bg_color = Color("#0F1720")
	style.corner_radius_top_left = 4
	style.corner_radius_top_right = 4
	style.corner_radius_bottom_left = 4
	style.corner_radius_bottom_right = 4
	item.add_theme_stylebox_override("panel", style)
	
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 8)
	margin.add_theme_constant_override("margin_right", 8)
	margin.add_theme_constant_override("margin_top", 6)
	margin.add_theme_constant_override("margin_bottom", 6)
	item.add_child(margin)
	
	var hbox = HBoxContainer.new()
	margin.add_child(hbox)
	
	# Checkbox
	var checkbox = CheckBox.new()
	var product_type = product.get("type", "Unknown")
	checkbox.button_pressed = enabled_products.get(product_type, false)
	checkbox.toggled.connect(_on_product_toggled.bind(product_type))
	hbox.add_child(checkbox)
	
	# Product info
	var info_vbox = VBoxContainer.new()
	info_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(info_vbox)
	
	# Product name
	var name_label = Label.new()
	name_label.text = product.get("name", "Unknown Product")
	name_label.add_theme_color_override("font_color", Color("#E6EEF3"))
	name_label.add_theme_font_size_override("font_size", 14)
	info_vbox.add_child(name_label)
	
	# Yield and margin
	var metrics_label = Label.new()
	var yield_pct = product.get("baseYieldPercent", 0.0) * 100
	var margin_pct = product.get("marginPercent", 0.0) * 100
	metrics_label.text = "Yield: %.1f%% | Margin: %.1f%%" % [yield_pct, margin_pct]
	metrics_label.add_theme_color_override("font_color", Color("#B0B8BF"))
	metrics_label.add_theme_font_size_override("font_size", 11)
	info_vbox.add_child(metrics_label)
	
	# Projected profit (from recommendation if available)
	var profit_label = Label.new()
	var expected_profit = _get_expected_profit(product_type)
	if expected_profit > 0:
		profit_label.text = "Est. Profit: NOK %.0f/day" % expected_profit
		profit_label.add_theme_color_override("font_color", Color("#2AB7B5"))
	else:
		profit_label.text = tr("Not recommended")
		profit_label.add_theme_color_override("font_color", Color("#666666"))
	profit_label.add_theme_font_size_override("font_size", 11)
	info_vbox.add_child(profit_label)
	
	# Demand indicator
	var demand_label = Label.new()
	var demand = product.get("demandLevel", "Normal")
	var demand_color = Color("#2AB7B5") if demand == "High" else Color("#E6EEF3")
	demand_label.text = "Demand: " + demand
	demand_label.add_theme_color_override("font_color", demand_color)
	demand_label.add_theme_font_size_override("font_size", 10)
	info_vbox.add_child(demand_label)
	
	return item

func _get_expected_profit(product_type: String) -> float:
	if current_recommendation.is_empty():
		return 0.0
	
	var recommendations = current_recommendation.get("recommendations", [])
	for rec in recommendations:
		if rec.get("product", "") == product_type:
			return rec.get("expectedProfit", 0.0)
	
	return 0.0

func _on_product_toggled(pressed: bool, product_type: String):
	enabled_products[product_type] = pressed

func _on_strategy_selected(index: int):
	_request_recommendation()

func _request_recommendation():
	if not SimBridge:
		return
	
	var strategy = STRATEGIES[strategy_dropdown.selected]
	SimBridge.call_deferred("GetProductMixRecommendation", strategy)

func _on_recommendation_received(recommendation_json: String):
	var json = JSON.new()
	var parse_result = json.parse(recommendation_json)
	
	if parse_result != OK:
		push_error("Failed to parse recommendation JSON")
		return
	
	current_recommendation = json.data
	_update_recommendation_display()
	_populate_product_list()  # Refresh with new profit estimates

func _update_recommendation_display():
	var strategy = current_recommendation.get("strategy", "Unknown")
	var total_profit = current_recommendation.get("totalExpectedProfit", 0.0)
	var avg_margin = current_recommendation.get("weightedAverageMargin", 0.0) * 100
	var risk_score = current_recommendation.get("portfolioRiskScore", 0.0)
	
	var risk_text = "Low" if risk_score < 0.3 else ("Medium" if risk_score < 0.7 else "High")
	
	recommendation_label.text = "%s: NOK %.0fk profit/day | Avg margin: %.1f%% | Risk: %s" % [
		strategy, total_profit / 1000.0, avg_margin, risk_text
	]

func _on_apply_button_pressed():
	if not SimBridge:
		return
	
	# Collect enabled product IDs
	var enabled_list: Array[String] = []
	for product_type in enabled_products.keys():
		if enabled_products[product_type]:
			enabled_list.append(product_type)
	
	if enabled_list.is_empty():
		push_warning("No products selected!")
		return
	
	# Send to C#
	var products_json = JSON.stringify(enabled_list)
	SimBridge.call_deferred("SetProductMix", products_json)
	
	# Show confirmation
	var confirm = AcceptDialog.new()
	confirm.dialog_text = "Product mix updated! %d products enabled." % enabled_list.size()
	confirm.title = "Success"
	add_child(confirm)
	confirm.popup_centered()

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
    if event.is_action_pressed("ui_cancel"):
        get_tree().call_group_flags(2, "tooltips", "hide")
