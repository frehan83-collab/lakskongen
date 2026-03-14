## ContractsPanel.gd
## Market depth and contract negotiation UI panel.
## Shows active contracts, available offers, export markets, and spot vs contract price comparison.

extends PanelContainer
class_name ContractsPanel

# UI References
@onready var active_contracts_container: VBoxContainer
@onready var available_offers_container: VBoxContainer
@onready var export_markets_container: VBoxContainer
@onready var spot_price_label: Label
@onready var spot_vs_contract_label: Label

# Color scheme
const COLOR_GREEN = Color(0.3, 0.9, 0.3)
const COLOR_YELLOW = Color(0.9, 0.9, 0.3)
const COLOR_RED = Color(0.9, 0.3, 0.3)
const COLOR_TEXT = "#E6EEF3"
const COLOR_MUTED = "#9AA7B2"

func _ready():
	# Create UI elements if not assigned
	if not active_contracts_container:
		_create_ui()
	
	# Connect to SimBridge for live data updates
	if has_node("/root/SimBridge"):
		var sim_bridge = get_node("/root/SimBridge")
		sim_bridge.state_updated.connect(_on_state_updated)
		print("ContractsPanel: connected to SimBridge")
	else:
		push_warning("ContractsPanel: SimBridge not found")
	
	# Initial refresh
	_refresh_from_bridge()

func _create_ui():
	"""Dynamically create UI elements if not set in editor"""
	
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_right", 16)
	add_child(margin)
	
	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 16)
	margin.add_child(vbox)
	
	# Header
	var header = Label.new()
	header.text = "📜 Contracts & Market Depth"
	header.add_theme_font_size_override("font_size", 20)
	header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	header.tooltip_text = "Oversikt over aktive kontrakter, tilgjengelige tilbud og eksportmarkeder."
	vbox.add_child(header)
	
	# Spot price vs contract price comparison
	var spot_container = HBoxContainer.new()
	spot_container.add_theme_constant_override("separation", 8)
	vbox.add_child(spot_container)
	
	spot_price_label = Label.new()
	spot_price_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	spot_price_label.tooltip_text = "Nåværende spotpris (markedspris) for laks per kg."
	spot_container.add_child(spot_price_label)
	
	spot_vs_contract_label = Label.new()
	spot_vs_contract_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	spot_vs_contract_label.tooltip_text = "Sammenligning mellom gjennomsnittlig kontraktspris og spotpris."
	spot_container.add_child(spot_vs_contract_label)
	
	# Active contracts section
	var active_header = Label.new()
	active_header.text = "Active Contracts"
	active_header.add_theme_font_size_override("font_size", 16)
	active_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(active_header)
	
	active_contracts_container = VBoxContainer.new()
	active_contracts_container.add_theme_constant_override("separation", 4)
	vbox.add_child(active_contracts_container)
	
	# Available offers section
	var offers_header = Label.new()
	offers_header.text = "Available Offers"
	offers_header.add_theme_font_size_override("font_size", 16)
	offers_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(offers_header)
	
	available_offers_container = VBoxContainer.new()
	available_offers_container.add_theme_constant_override("separation", 4)
	vbox.add_child(available_offers_container)
	
	# Export markets section
	var export_header = Label.new()
	export_header.text = "Export Markets"
	export_header.add_theme_font_size_override("font_size", 16)
	export_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(export_header)
	
	export_markets_container = VBoxContainer.new()
	export_markets_container.add_theme_constant_override("separation", 4)
	vbox.add_child(export_markets_container)
	
	# Refresh button (optional)
	var refresh_button = Button.new()
	refresh_button.text = "Refresh"
	refresh_button.tooltip_text = "Oppdater kontrakts- og markedsdata."
	refresh_button.pressed.connect(_refresh_from_bridge)
	vbox.add_child(refresh_button)

func _on_state_updated():
	_refresh_from_bridge()

func _refresh_from_bridge():
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	
	# Spot price
	var spot_price = sim_bridge.market_price_nok
	spot_price_label.text = "Spot price: %s NOK/kg" % sim_bridge.format_nok(spot_price)
	
	# Fetch data from bridge
	var active_contracts = sim_bridge.get_active_contracts()
	var available_offers = sim_bridge.get_available_offers()
	var export_markets = sim_bridge.get_export_markets()
	
	# Update UI sections
	_update_active_contracts(active_contracts, spot_price)
	_update_available_offers(available_offers, spot_price)
	_update_export_markets(export_markets)
	
	# Spot vs contract price comparison (average of active contracts)
	var avg_contract_price = _compute_average_contract_price(active_contracts)
	if avg_contract_price > 0:
		var diff_pct = (avg_contract_price - spot_price) / spot_price * 100.0
		var color = _profit_color(diff_pct)
		spot_vs_contract_label.text = "Avg contract: %s NOK/kg (%+.1f%%)" % [sim_bridge.format_nok(avg_contract_price), diff_pct]
		spot_vs_contract_label.add_theme_color_override("font_color", color)
	else:
		spot_vs_contract_label.text = "No active contracts"

func _update_active_contracts(contracts: Array[Dictionary], spot_price: float):
	_clear_container(active_contracts_container)
	
	if contracts.is_empty():
		var label = Label.new()
		label.text = "No active contracts"
		label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		active_contracts_container.add_child(label)
		return
	
	for contract in contracts:
		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		
		var buyer_label = Label.new()
		buyer_label.text = contract.get("buyer", "Unknown")
		buyer_label.add_theme_color_override("font_color", Color(COLOR_TEXT))
		buyer_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(buyer_label)
		
		var quantity_label = Label.new()
		var quantity_kg = contract.get("quantity_kg", 0.0)
		var fulfilled_kg = contract.get("fulfilled_kg", 0.0)
		var fulfilled_percent = contract.get("fulfilled_percent", 0.0)
		quantity_label.text = "%s/%s kg (%d%%)" % [
			_format_float(fulfilled_kg),
			_format_float(quantity_kg),
			int(fulfilled_percent)
		]
		quantity_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		row.add_child(quantity_label)
		
		var price_label = Label.new()
		var price_per_kg = contract.get("price_per_kg", 0.0)
		var diff_pct = (price_per_kg - spot_price) / spot_price * 100.0
		price_label.text = "%s NOK/kg (%+.1f%%)" % [_format_float(price_per_kg), diff_pct]
		price_label.add_theme_color_override("font_color", _profit_color(diff_pct))
		row.add_child(price_label)
		
		active_contracts_container.add_child(row)

func _update_available_offers(offers: Array[Dictionary], spot_price: float):
	_clear_container(available_offers_container)
	
	if offers.is_empty():
		var label = Label.new()
		label.text = "No offers available"
		label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		available_offers_container.add_child(label)
		return
	
	for offer in offers:
		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		
		var buyer_label = Label.new()
		buyer_label.text = offer.get("buyer", "Unknown")
		buyer_label.add_theme_color_override("font_color", Color(COLOR_TEXT))
		buyer_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(buyer_label)
		
		var quantity_label = Label.new()
		var quantity_kg = offer.get("quantity_kg", 0.0)
		quantity_label.text = "%s kg" % _format_float(quantity_kg)
		quantity_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		row.add_child(quantity_label)
		
		var price_label = Label.new()
		var price_per_kg = offer.get("price_per_kg", 0.0)
		var diff_pct = (price_per_kg - spot_price) / spot_price * 100.0
		price_label.text = "%s NOK/kg (%+.1f%%)" % [_format_float(price_per_kg), diff_pct]
		price_label.add_theme_color_override("font_color", _profit_color(diff_pct))
		row.add_child(price_label)
		
		var accept_button = Button.new()
		accept_button.text = "Accept"
		accept_button.tooltip_text = "Aksepter dette kontraktstilbudet."
		accept_button.disabled = true  # TODO: implement contract acceptance
		row.add_child(accept_button)
		
		available_offers_container.add_child(row)

func _update_export_markets(markets: Array[Dictionary]):
	_clear_container(export_markets_container)
	
	if markets.is_empty():
		var label = Label.new()
		label.text = "No export markets data"
		label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		export_markets_container.add_child(label)
		return
	
	for market in markets:
		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		
		var region_label = Label.new()
		region_label.text = market.get("region", "Unknown")
		region_label.add_theme_color_override("font_color", Color(COLOR_TEXT))
		region_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		row.add_child(region_label)
		
		var price_label = Label.new()
		var base_price = market.get("base_price_nok", 0.0)
		var premium = market.get("premium_multiplier", 1.0)
		var effective_price = base_price * premium
		price_label.text = "%s NOK/kg (x%.2f)" % [_format_float(effective_price), premium]
		price_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		row.add_child(price_label)
		
		var access_label = Label.new()
		var has_access = market.get("player_has_access", false)
		access_label.text = "✓" if has_access else "✗"
		access_label.add_theme_color_override("font_color", COLOR_GREEN if has_access else COLOR_RED)
		row.add_child(access_label)
		
		export_markets_container.add_child(row)

func _clear_container(container: VBoxContainer):
	for child in container.get_children():
		child.queue_free()

func _compute_average_contract_price(contracts: Array[Dictionary]) -> float:
	if contracts.is_empty():
		return 0.0
	var total = 0.0
	for contract in contracts:
		total += contract.get("price_per_kg", 0.0)
	return total / contracts.size()

func _profit_color(diff_pct: float) -> Color:
	if diff_pct > 5.0:
		return COLOR_GREEN
	elif diff_pct > -5.0:
		return COLOR_YELLOW
	else:
		return COLOR_RED

func _format_float(value: float) -> String:
	if value >= 1000000.0:
		return "%.1fM" % (value / 1000000.0)
	elif value >= 1000.0:
		return "%.1fK" % (value / 1000.0)
	else:
		return "%.0f" % value