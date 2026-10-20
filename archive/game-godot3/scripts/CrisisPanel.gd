extends Control

# Crisis Capitalism & Distressed Asset Panel
# Displays active crises and available distressed assets for acquisition

signal asset_purchase_requested(asset_id: String)
signal crisis_details_requested(crisis_id: String)

# UI References
@onready var crisis_list: VBoxContainer = $VBoxContainer/CrisisSection/CrisisScrollContainer/CrisisList
@onready var asset_list: VBoxContainer = $VBoxContainer/AssetSection/AssetScrollContainer/AssetList
@onready var reputation_warning: Label = $VBoxContainer/ReputationSection/ReputationWarning
@onready var portfolio_value: Label = $VBoxContainer/PortfolioSection/PortfolioValue
@onready var vulture_meter: ProgressBar = $VBoxContainer/ReputationSection/VultureMeter

# Data
var active_crises: Array = []
var available_assets: Array = []
var player_cash: float = 0.0
var vulture_reputation: float = 0.0 # 0-100, higher = more negative reputation

class CrisisData:
	var id: String
	var type: String
	var affected_competitor: String
	var severity: float
	var discount_multiplier: float
	var duration_months: int
	var is_active: bool
	
	func _init(p_id: String, p_type: String, p_competitor: String, p_severity: float, p_discount: float, p_duration: int):
		id = p_id
		type = p_type
		affected_competitor = p_competitor
		severity = p_severity
		discount_multiplier = p_discount
		duration_months = p_duration
		is_active = true

class DistressedAssetData:
	var id: String
	var asset_type: String
	var name: String
	var description: String
	var location: String
	var original_value: float
	var discounted_price: float
	var time_remaining: int
	var risk_score: float
	var crisis_id: String
	var owning_competitor: String
	
	func get_discount_percentage() -> float:
		if original_value > 0:
			return (original_value - discounted_price) / original_value
		return 0.0
	
	func get_potential_savings() -> float:
		return original_value - discounted_price
	
	func _init(p_id: String, p_type: String, p_name: String, p_desc: String, p_location: String, 
			   p_original: float, p_discounted: float, p_time: int, p_risk: float, p_crisis_id: String, p_competitor: String):
		id = p_id
		asset_type = p_type
		name = p_name
		description = p_desc
		location = p_location
		original_value = p_original
		discounted_price = p_discounted
		time_remaining = p_time
		risk_score = p_risk
		crisis_id = p_crisis_id
		owning_competitor = p_competitor

func _ready():
	# Initialize UI
	update_reputation_warning()
	setup_crisis_monitoring()
	refresh_display()

func setup_crisis_monitoring():
	# Connect to C# crisis capitalism system signals
	# In a real implementation, this would connect to the backend system
	pass

func update_crises(crises_data: Array):
	"""Update the list of active crises"""
	active_crises.clear()
	for crisis in crises_data:
		active_crises.append(crisis)
	refresh_crisis_list()

func update_assets(assets_data: Array):
	"""Update the list of available distressed assets"""
	available_assets.clear()
	for asset in assets_data:
		available_assets.append(asset)
	refresh_asset_list()

func set_player_cash(cash: float):
	"""Update player's available cash"""
	player_cash = cash
	refresh_asset_list() # Refresh to update affordability indicators

func refresh_display():
	"""Refresh the entire display"""
	refresh_crisis_list()
	refresh_asset_list()
	update_portfolio_display()
	update_reputation_warning()

func refresh_crisis_list():
	"""Rebuild the crisis list UI"""
	# Clear existing crisis items
	for child in crisis_list.get_children():
		child.queue_free()
	
	if active_crises.is_empty():
		var no_crisis_label = Label.new()
		no_crisis_label.text = "No active crises. Market conditions stable."
		no_crisis_label.add_theme_color_override("font_color", Color.GREEN)
		crisis_list.add_child(no_crisis_label)
		return
	
	# Add crisis items
	for crisis in active_crises:
		var crisis_item = create_crisis_item(crisis)
		crisis_list.add_child(crisis_item)

func create_crisis_item(crisis: CrisisData) -> Control:
	"""Create UI element for a crisis event"""
	var crisis_container = VBoxContainer.new()
	crisis_container.add_theme_constant_override("separation", 8)
	
	# Header with crisis type and severity
	var header = HBoxContainer.new()
	
	var type_label = Label.new()
	type_label.text = format_crisis_type(crisis.type)
	type_label.add_theme_color_override("font_color", get_crisis_color(crisis.type))
	type_label.add_theme_font_size_override("font_size", 16)
	header.add_child(type_label)
	
	var severity_bar = ProgressBar.new()
	severity_bar.max_value = 1.0
	severity_bar.value = crisis.severity
	severity_bar.custom_minimum_size = Vector2(100, 20)
	severity_bar.add_theme_color_override("font_color", Color.WHITE)
	header.add_child(severity_bar)
	
	var severity_label = Label.new()
	severity_label.text = "%.0f%%" % (crisis.severity * 100)
	header.add_child(severity_label)
	
	crisis_container.add_child(header)
	
	# Details
	var details_label = Label.new()
	details_label.text = "%s affected - %d months remaining, %.0f%% asset discount" % [
		crisis.affected_competitor,
		crisis.duration_months,
		crisis.discount_multiplier * 100
	]
	details_label.add_theme_font_size_override("font_size", 12)
	crisis_container.add_child(details_label)
	
	# Separator
	var separator = HSeparator.new()
	crisis_container.add_child(separator)
	
	return crisis_container

func refresh_asset_list():
	"""Rebuild the asset list UI"""
	# Clear existing asset items
	for child in asset_list.get_children():
		child.queue_free()
	
	if available_assets.is_empty():
		var no_assets_label = Label.new()
		no_assets_label.text = "No distressed assets available for acquisition."
		asset_list.add_child(no_assets_label)
		return
	
	# Sort assets by discount percentage (highest first)
	var sorted_assets = available_assets.duplicate()
	sorted_assets.sort_custom(func(a, b): return a.get_discount_percentage() > b.get_discount_percentage())
	
	# Add asset items
	for asset in sorted_assets:
		var asset_item = create_asset_item(asset)
		asset_list.add_child(asset_item)

func create_asset_item(asset: DistressedAssetData) -> Control:
	"""Create UI element for a distressed asset"""
	var asset_container = VBoxContainer.new()
	asset_container.add_theme_constant_override("separation", 8)
	
	# Header with asset name and discount
	var header = HBoxContainer.new()
	
	var name_label = Label.new()
	name_label.text = asset.name
	name_label.add_theme_font_size_override("font_size", 14)
	header.add_child(name_label)
	
	var spacer = Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(spacer)
	
	var discount_label = Label.new()
	discount_label.text = "%.0f%% OFF" % (asset.get_discount_percentage() * 100)
	discount_label.add_theme_color_override("font_color", Color.RED)
	discount_label.add_theme_font_size_override("font_size", 14)
	header.add_child(discount_label)
	
	asset_container.add_child(header)
	
	# Details row
	var details = HBoxContainer.new()
	
	var type_location = Label.new()
	type_location.text = "%s • %s" % [asset.asset_type, asset.location]
	type_location.add_theme_font_size_override("font_size", 11)
	details.add_child(type_location)
	
	var spacer2 = Control.new()
	spacer2.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	details.add_child(spacer2)
	
	var time_label = Label.new()
	time_label.text = "%d months left" % asset.time_remaining
	time_label.add_theme_color_override("font_color", Color.YELLOW if asset.time_remaining <= 3 else Color.WHITE)
	time_label.add_theme_font_size_override("font_size", 11)
	details.add_child(time_label)
	
	asset_container.add_child(details)
	
	# Price and risk row
	var price_risk = HBoxContainer.new()
	
	var price_container = VBoxContainer.new()
	var original_price = Label.new()
	original_price.text = "Was: %s NOK" % format_currency(asset.original_value)
	original_price.add_theme_color_override("font_color", Color.GRAY)
	original_price.add_theme_font_size_override("font_size", 10)
	price_container.add_child(original_price)
	
	var current_price = Label.new()
	current_price.text = "Now: %s NOK" % format_currency(asset.discounted_price)
	current_price.add_theme_font_size_override("font_size", 13)
	current_price.add_theme_color_override("font_color", Color.GREEN)
	price_container.add_child(current_price)
	
	price_risk.add_child(price_container)
	
	var spacer3 = Control.new()
	spacer3.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	price_risk.add_child(spacer3)
	
	# Risk indicator
	var risk_container = VBoxContainer.new()
	var risk_label = Label.new()
	risk_label.text = "Integration Risk"
	risk_label.add_theme_font_size_override("font_size", 10)
	risk_container.add_child(risk_label)
	
	var risk_bar = ProgressBar.new()
	risk_bar.max_value = 1.0
	risk_bar.value = asset.risk_score
	risk_bar.custom_minimum_size = Vector2(80, 16)
	risk_bar.add_theme_color_override("fill", get_risk_color(asset.risk_score))
	risk_container.add_child(risk_bar)
	
	var risk_percent = Label.new()
	risk_percent.text = "%.0f%%" % (asset.risk_score * 100)
	risk_percent.add_theme_font_size_override("font_size", 10)
	risk_container.add_child(risk_percent)
	
	price_risk.add_child(risk_container)
	
	asset_container.add_child(price_risk)
	
	# Description
	var desc_label = Label.new()
	desc_label.text = asset.description
	desc_label.add_theme_font_size_override("font_size", 11)
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	asset_container.add_child(desc_label)
	
	# Purchase button
	var button_container = HBoxContainer.new()
	
	var purchase_button = Button.new()
	purchase_button.text = "ACQUIRE ASSET"
	purchase_button.add_theme_color_override("font_color", Color.WHITE)
	
	# Check affordability
	var can_afford = player_cash >= asset.discounted_price
	if can_afford:
		purchase_button.add_theme_color_override("font_color", Color.GREEN)
		purchase_button.disabled = false
	else:
		purchase_button.add_theme_color_override("font_color", Color.RED)
		purchase_button.disabled = true
		purchase_button.text = "INSUFFICIENT FUNDS"
	
	purchase_button.pressed.connect(_on_purchase_button_pressed.bind(asset.id))
	button_container.add_child(purchase_button)
	
	var spacer4 = Control.new()
	spacer4.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	button_container.add_child(spacer4)
	
	# Savings indicator
	var savings_label = Label.new()
	savings_label.text = "Save: %s NOK" % format_currency(asset.get_potential_savings())
	savings_label.add_theme_color_override("font_color", Color.CYAN)
	button_container.add_child(savings_label)
	
	asset_container.add_child(button_container)
	
	# Final separator
	var separator = HSeparator.new()
	asset_container.add_child(separator)
	
	return asset_container

func update_portfolio_display():
	"""Update portfolio value display"""
	var total_potential_savings = 0.0
	for asset in available_assets:
		total_potential_savings += asset.get_potential_savings()
	
	portfolio_value.text = "Potential Savings Available: %s NOK" % format_currency(total_potential_savings)

func update_reputation_warning():
	"""Update vulture capitalism reputation warning"""
	vulture_meter.value = vulture_reputation
	
	var warning_text = ""
	var warning_color = Color.WHITE
	
	if vulture_reputation < 20:
		warning_text = "Reputation: Clean - No concerns about business practices"
		warning_color = Color.GREEN
	elif vulture_reputation < 40:
		warning_text = "Reputation: Opportunistic - Some questions about ethics"
		warning_color = Color.YELLOW
	elif vulture_reputation < 60:
		warning_text = "Reputation: Questionable - Known for exploiting crises"
		warning_color = Color.ORANGE
	elif vulture_reputation < 80:
		warning_text = "Reputation: Predatory - Widely criticized for vulture tactics"
		warning_color = Color.RED
	else:
		warning_text = "Reputation: VULTURE CAPITALIST - Industry pariah"
		warning_color = Color.DARK_RED
	
	reputation_warning.text = warning_text
	reputation_warning.add_theme_color_override("font_color", warning_color)

func format_crisis_type(crisis_type: String) -> String:
	"""Format crisis type for display"""
	return crisis_type.replace("_", " ").capitalize()

func get_crisis_color(crisis_type: String) -> Color:
	"""Get color for crisis type"""
	match crisis_type:
		"DISEASE_OUTBREAK":
			return Color.RED
		"BANKRUPTCY":
			return Color.DARK_RED
		"REGULATORY_SHUTDOWN":
			return Color.ORANGE
		"MARKET_CRASH":
			return Color.PURPLE
		"ENVIRONMENTAL_SCANDAL":
			return Color.BROWN
		_:
			return Color.WHITE

func get_risk_color(risk_score: float) -> Color:
	"""Get color based on risk level"""
	if risk_score < 0.3:
		return Color.GREEN
	elif risk_score < 0.6:
		return Color.YELLOW
	else:
		return Color.RED

func format_currency(value: float) -> String:
	"""Format currency with appropriate suffixes"""
	if value >= 1_000_000_000:
		return "%.1fB" % (value / 1_000_000_000)
	elif value >= 1_000_000:
		return "%.1fM" % (value / 1_000_000)
	elif value >= 1_000:
		return "%.0fK" % (value / 1_000)
	else:
		return "%.0f" % value

func set_vulture_reputation(reputation: float):
	"""Update vulture capitalism reputation (0-100)"""
	vulture_reputation = clamp(reputation, 0.0, 100.0)
	update_reputation_warning()

func _on_purchase_button_pressed(asset_id: String):
	"""Handle asset purchase button press"""
	asset_purchase_requested.emit(asset_id)

# Public API for external systems

func add_crisis(id: String, type: String, competitor: String, severity: float, discount: float, duration: int):
	"""Add a new crisis to the display"""
	var crisis = CrisisData.new(id, type, competitor, severity, discount, duration)
	active_crises.append(crisis)
	refresh_crisis_list()

func add_distressed_asset(id: String, asset_type: String, name: String, description: String, location: String,
						  original_value: float, discounted_price: float, time_remaining: int, risk_score: float,
						  crisis_id: String, owning_competitor: String):
	"""Add a new distressed asset to the display"""
	var asset = DistressedAssetData.new(id, asset_type, name, description, location, 
										original_value, discounted_price, time_remaining, 
										risk_score, crisis_id, owning_competitor)
	available_assets.append(asset)
	refresh_asset_list()
	update_portfolio_display()

func remove_crisis(crisis_id: String):
	"""Remove a crisis from the display"""
	for i in range(active_crises.size() - 1, -1, -1):
		if active_crises[i].id == crisis_id:
			active_crises.remove_at(i)
			break
	refresh_crisis_list()

func remove_asset(asset_id: String):
	"""Remove an asset from the display"""
	for i in range(available_assets.size() - 1, -1, -1):
		if available_assets[i].id == asset_id:
			available_assets.remove_at(i)
			break
	refresh_asset_list()
	update_portfolio_display()

func clear_all():
	"""Clear all crises and assets"""
	active_crises.clear()
	available_assets.clear()
	refresh_display()