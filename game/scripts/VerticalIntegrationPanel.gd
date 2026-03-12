extends Panel

# VerticalIntegrationPanel.gd - Visual chain showing vertical integration progress

signal integration_asset_build_requested(asset_data: Dictionary)
signal integration_panel_closed()

@onready var integration_chain = $VBox/ScrollContainer/IntegrationChain
@onready var balance_label = $VBox/Header/BalanceLabel
@onready var integration_score_label = $VBox/Header/IntegrationScoreLabel
@onready var close_button = $VBox/Header/CloseButton
@onready var info_panel = $VBox/InfoPanel
@onready var selected_asset_info = $VBox/SelectedAssetInfo

var integration_data = {}
var owned_tiers = []
var available_assets = []
var selected_asset = null

# Tier colors for visual pipeline
var tier_colors = {
	"GENETICS": Color(0.9, 0.4, 0.9),      # Purple
	"HATCHERY": Color(0.4, 0.7, 0.9),      # Light Blue
	"FEED_MILL": Color(0.8, 0.6, 0.2),     # Orange
	"SEA_FARM": Color(0.2, 0.8, 0.4),      # Green
	"PROCESSING": Color(0.9, 0.3, 0.3),    # Red
	"COLD_CHAIN": Color(0.5, 0.8, 0.9),    # Ice Blue
	"DISTRIBUTION": Color(0.7, 0.7, 0.7)   # Gray
}

var tier_order = ["GENETICS", "HATCHERY", "FEED_MILL", "SEA_FARM", "PROCESSING", "COLD_CHAIN", "DISTRIBUTION"]

func _ready():
	load_integration_data()
	setup_ui()
	connect_signals()
	refresh_integration_chain()

func load_integration_data():
	var file = FileAccess.open("res://data/integration_assets.json", FileAccess.READ)
	if file:
		var json_string = file.get_as_text()
		file.close()
		
		var json = JSON.new()
		var parse_result = json.parse(json_string)
		if parse_result == OK:
			integration_data = json.data
			available_assets = integration_data.get("assets", [])
		else:
			print("Failed to parse integration assets: ", json.get_error_message())
	else:
		print("Integration assets file not found")

func setup_ui():
	# Create header info
	info_panel.add_child(create_info_text())
	
	# Set initial values
	update_balance_display()
	update_integration_score()

func create_info_text():
	var info_label = RichTextLabel.new()
	info_label.custom_minimum_size.y = 80
	info_label.bbcode_enabled = true
	info_label.text = """[b]Vertical Integration[/b]

Own the full salmon value chain to reduce costs, improve quality, and lock out competitors.

[color=green]Benefits:[/color] Cost savings, quality premiums, competitive advantages
[color=yellow]Synergies:[/color] Owning 3+ tiers unlocks "Fully Integrated" +20% EBITDA bonus"""
	return info_label

func connect_signals():
	close_button.pressed.connect(_on_close_button_pressed)

func refresh_integration_chain():
	clear_chain()
	create_visual_chain()
	update_integration_score()

func clear_chain():
	for child in integration_chain.get_children():
		child.queue_free()

func create_visual_chain():
	var chain_container = HBoxContainer.new()
	chain_container.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	
	for i in range(tier_order.size()):
		var tier = tier_order[i]
		var tier_container = create_tier_node(tier)
		chain_container.add_child(tier_container)
		
		# Add connection arrow (except for last tier)
		if i < tier_order.size() - 1:
			var arrow = create_connection_arrow(tier, tier_order[i + 1])
			chain_container.add_child(arrow)
	
	integration_chain.add_child(chain_container)

func create_tier_node(tier: String):
	var container = VBoxContainer.new()
	container.custom_minimum_size = Vector2(120, 150)
	
	# Tier header
	var header = Label.new()
	header.text = tier.replace("_", " ")
	header.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	header.add_theme_font_size_override("font_size", 12)
	container.add_child(header)
	
	# Status indicator (owned/available/locked)
	var status_panel = create_tier_status_panel(tier)
	container.add_child(status_panel)
	
	# Asset options for this tier
	var assets_for_tier = available_assets.filter(func(asset): return asset.tier == tier)
	for asset in assets_for_tier:
		var asset_button = create_asset_button(asset)
		container.add_child(asset_button)
	
	return container

func create_tier_status_panel(tier: String):
	var panel = Panel.new()
	panel.custom_minimum_size = Vector2(110, 60)
	
	var is_owned = tier in owned_tiers
	var tier_color = tier_colors.get(tier, Color.WHITE)
	
	if is_owned:
		panel.modulate = tier_color
		var owned_label = Label.new()
		owned_label.text = "OWNED\n✓"
		owned_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		owned_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		owned_label.add_theme_color_override("font_color", Color.WHITE)
		panel.add_child(owned_label)
	else:
		panel.modulate = tier_color * 0.3  # Dimmed
		var available_label = Label.new()
		available_label.text = "Available"
		available_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		available_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		panel.add_child(available_label)
	
	return panel

func create_asset_button(asset: Dictionary):
	var button = Button.new()
	button.text = asset.name
	button.custom_minimum_size.x = 110
	
	if asset.get("is_owned", false):
		button.disabled = true
		button.text += " (Owned)"
		button.modulate = Color.GREEN
	elif not is_asset_unlocked(asset):
		button.disabled = true
		button.text += " (Locked)"
		button.modulate = Color.GRAY
	else:
		button.pressed.connect(_on_asset_button_pressed.bind(asset))
		button.modulate = Color.WHITE
	
	# Add hover tooltip
	button.mouse_entered.connect(_on_asset_button_hover.bind(asset))
	button.mouse_exited.connect(_on_asset_button_hover_exit)
	
	return button

func create_connection_arrow(from_tier: String, to_tier: String):
	var arrow_container = VBoxContainer.new()
	arrow_container.custom_minimum_size = Vector2(30, 150)
	
	# Spacer to center arrow
	var spacer1 = Control.new()
	spacer1.custom_minimum_size.y = 60
	arrow_container.add_child(spacer1)
	
	# Arrow symbol
	var arrow_label = Label.new()
	arrow_label.text = "→"
	arrow_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	arrow_label.add_theme_font_size_override("font_size", 24)
	
	# Color arrow based on synergy
	if has_synergy_connection(from_tier, to_tier):
		arrow_label.add_theme_color_override("font_color", Color.GREEN)
	else:
		arrow_label.add_theme_color_override("font_color", Color.GRAY)
	
	arrow_container.add_child(arrow_label)
	
	return arrow_container

func has_synergy_connection(from_tier: String, to_tier: String) -> bool:
	# Check if both tiers are owned for synergy bonus
	return from_tier in owned_tiers and to_tier in owned_tiers

func is_asset_unlocked(asset: Dictionary) -> bool:
	var unlock_conditions = asset.get("unlock_conditions", {})
	
	# Check minimum production requirement
	if unlock_conditions.has("min_annual_production_kg"):
		var min_production = unlock_conditions["min_annual_production_kg"]
		var current_production = get_player_annual_production()
		if current_production < min_production:
			return false
	
	# Check required tier
	if unlock_conditions.has("required_tier"):
		var required_tier = unlock_conditions["required_tier"]
		if not required_tier in owned_tiers:
			return false
	
	# Check minimum capital
	if unlock_conditions.has("min_capital_nok"):
		var min_capital = unlock_conditions["min_capital_nok"]
		var current_capital = get_player_capital()
		if current_capital < min_capital:
			return false
	
	return true

func _on_asset_button_pressed(asset: Dictionary):
	selected_asset = asset
	show_asset_details(asset)

func show_asset_details(asset: Dictionary):
	# Clear previous info
	for child in selected_asset_info.get_children():
		child.queue_free()
	
	# Asset name and tier
	var title_label = Label.new()
	title_label.text = asset.name + " (" + asset.tier.replace("_", " ") + ")"
	title_label.add_theme_font_size_override("font_size", 16)
	selected_asset_info.add_child(title_label)
	
	# Cost information
	var cost_info = create_cost_info_panel(asset)
	selected_asset_info.add_child(cost_info)
	
	# Benefits information
	var benefits_info = create_benefits_info_panel(asset)
	selected_asset_info.add_child(benefits_info)
	
	# Build button
	var build_button = Button.new()
	build_button.text = "Build for NOK " + format_number(asset.capex_nok)
	build_button.custom_minimum_size = Vector2(200, 40)
	
	if get_player_capital() >= asset.capex_nok:
		build_button.pressed.connect(_on_build_button_pressed.bind(asset))
	else:
		build_button.disabled = true
		build_button.text += " (Insufficient Funds)"
	
	selected_asset_info.add_child(build_button)

func create_cost_info_panel(asset: Dictionary):
	var container = VBoxContainer.new()
	
	var cost_label = Label.new()
	cost_label.text = "Investment Details:"
	cost_label.add_theme_font_size_override("font_size", 14)
	container.add_child(cost_label)
	
	var info_text = RichTextLabel.new()
	info_text.custom_minimum_size.y = 80
	info_text.bbcode_enabled = true
	
	var payback_years = calculate_payback_period(asset)
	var payback_text = "∞ years" if payback_years == INF else str(payback_years) + " years"
	
	info_text.text = """[b]CAPEX:[/b] NOK %s
[b]Annual OPEX:[/b] NOK %s
[b]Construction Time:[/b] %d months
[b]Payback Period:[/b] %s
[b]Capacity:[/b] %s""" % [
		format_number(asset.capex_nok),
		format_number(asset.annual_opex_nok),
		asset.construction_time_months,
		payback_text,
		format_capacity(asset.capacity, asset.tier)
	]
	
	container.add_child(info_text)
	return container

func create_benefits_info_panel(asset: Dictionary):
	var container = VBoxContainer.new()
	
	var benefits_label = Label.new()
	benefits_label.text = "Competitive Advantages:"
	benefits_label.add_theme_font_size_override("font_size", 14)
	container.add_child(benefits_label)
	
	var benefits_text = RichTextLabel.new()
	benefits_text.custom_minimum_size.y = 100
	benefits_text.bbcode_enabled = true
	
	benefits_text.text = """[color=green]Cost Reduction:[/color] %.1f%% per kg
[color=blue]Quality Premium:[/color] +%.1f%% price
[color=red]Market Lockout:[/color] %.0f km radius
[color=yellow]Synergies:[/color] %s""" % [
		asset.cost_saving_pct_per_kg * 100,
		asset.quality_bonus * 100,
		asset.competitor_lockout_radius,
		calculate_synergy_text(asset)
	]
	
	container.add_child(benefits_text)
	return container

func calculate_payback_period(asset: Dictionary) -> float:
	var annual_production = get_player_annual_production()
	var annual_savings = annual_production * asset.cost_saving_pct_per_kg
	var annual_quality_premium = annual_production * asset.quality_bonus * 85.0  # NOK 85/kg base
	var total_annual_benefit = annual_savings + annual_quality_premium - asset.annual_opex_nok
	
	if total_annual_benefit <= 0:
		return INF
	
	return asset.capex_nok / total_annual_benefit

func calculate_synergy_text(asset: Dictionary) -> String:
	var synergies = []
	
	# Check for adjacent tier synergies
	var adjacent_tiers = get_adjacent_tiers(asset.tier)
	for adj_tier in adjacent_tiers:
		if adj_tier in owned_tiers:
			synergies.append(adj_tier.replace("_", " "))
	
	if synergies.is_empty():
		return "None available"
	else:
		return "+" + str(synergies.join(", "))

func get_adjacent_tiers(tier: String) -> Array:
	var tier_index = tier_order.find(tier)
	var adjacent = []
	
	if tier_index > 0:
		adjacent.append(tier_order[tier_index - 1])
	if tier_index < tier_order.size() - 1:
		adjacent.append(tier_order[tier_index + 1])
	
	return adjacent

func _on_build_button_pressed(asset: Dictionary):
	# Show confirmation dialog
	var confirmation = ConfirmationDialog.new()
	confirmation.title = "Confirm Investment"
	confirmation.dialog_text = "Invest NOK %s in %s?\n\nThis will start %d months of construction." % [
		format_number(asset.capex_nok),
		asset.name,
		asset.construction_time_months
	]
	
	confirmation.confirmed.connect(_on_build_confirmed.bind(asset))
	add_child(confirmation)
	confirmation.popup_centered()

func _on_build_confirmed(asset: Dictionary):
	# Emit signal to game logic
	integration_asset_build_requested.emit(asset)
	
	# Update UI to reflect purchase
	asset["is_owned"] = true
	owned_tiers.append(asset.tier)
	refresh_integration_chain()
	update_balance_display()

func _on_asset_button_hover(asset: Dictionary):
	# Could show quick tooltip with key stats
	pass

func _on_asset_button_hover_exit():
	# Hide tooltip
	pass

func update_balance_display():
	var balance = get_player_capital()
	balance_label.text = "Available Capital: NOK " + format_number(balance)

func update_integration_score():
	var score = calculate_integration_score()
	integration_score_label.text = "Integration Score: " + str(int(score * 100)) + "%"
	
	# Color based on score
	if score >= 0.7:
		integration_score_label.add_theme_color_override("font_color", Color.GREEN)
	elif score >= 0.4:
		integration_score_label.add_theme_color_override("font_color", Color.YELLOW)
	else:
		integration_score_label.add_theme_color_override("font_color", Color.RED)

func calculate_integration_score() -> float:
	var unique_owned_tiers = []
	for tier in owned_tiers:
		if not tier in unique_owned_tiers:
			unique_owned_tiers.append(tier)
	
	var base_score = unique_owned_tiers.size() * 0.15  # 15% per tier
	
	# Bonus for full integration (3+ tiers)
	if unique_owned_tiers.size() >= 3:
		base_score += 0.25
	
	return min(1.0, base_score)

func format_number(value: float) -> String:
	if value >= 1000000000:
		return "%.1fB" % (value / 1000000000)
	elif value >= 1000000:
		return "%.1fM" % (value / 1000000)
	elif value >= 1000:
		return "%.0fK" % (value / 1000)
	else:
		return str(int(value))

func format_capacity(capacity: int, tier: String) -> String:
	match tier:
		"GENETICS":
			return str(capacity) + " breeding pairs"
		"HATCHERY":
			return str(capacity) + " smolt/year"
		"FEED_MILL":
			return str(capacity) + " tonnes/year"
		"SEA_FARM":
			return str(capacity) + " tonnes/cycle"
		"PROCESSING":
			return str(capacity) + " tonnes/year"
		"COLD_CHAIN":
			return str(capacity) + " tonnes storage"
		"DISTRIBUTION":
			return str(capacity) + " delivery routes"
		_:
			return str(capacity) + " units"

func get_player_capital() -> float:
	# Placeholder - in real game, get from finance manager
	return 2500000000.0  # NOK 2.5B

func get_player_annual_production() -> float:
	# Placeholder - in real game, get from farm manager
	return 10000.0  # 10,000 kg/year

func _on_close_button_pressed():
	integration_panel_closed.emit()
	hide()

# Public method to update owned tiers from game state
func update_owned_tiers(tiers: Array):
	owned_tiers = tiers
	refresh_integration_chain()