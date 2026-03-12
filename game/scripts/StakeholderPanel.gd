extends Control

# StakeholderPanel.gd - UI for managing faction relationships and demands

signal demand_accepted(demand_id: int)
signal demand_ignored(demand_id: int)

# UI node references
@onready var faction_container: VBoxContainer = $ScrollContainer/VBoxContainer/FactionContainer
@onready var demands_container: VBoxContainer = $ScrollContainer/VBoxContainer/DemandsContainer
@onready var overall_reputation_bar: ProgressBar = $HeaderContainer/OverallReputationBar
@onready var overall_reputation_label: Label = $HeaderContainer/OverallReputationLabel
@onready var reputation_trend_label: Label = $HeaderContainer/ReputationTrendLabel

# Faction data
var factions: Dictionary = {}
var active_demands: Array = []
var reputation_history: Array = []
var faction_colors: Dictionary = {
	"MATTILSYNET": Color.BLUE,
	"FISKERIDIREKTORATET": Color.DARK_BLUE, 
	"ENVIRONMENTAL_NGOS": Color.GREEN,
	"LOCAL_COMMUNITIES": Color.ORANGE,
	"INDUSTRY_ASSOCIATION": Color.PURPLE,
	"FINANCIAL_SECTOR": Color.GOLD
}

# Faction display names in Norwegian
var faction_names: Dictionary = {
	"MATTILSYNET": "Mattilsynet",
	"FISKERIDIREKTORATET": "Fiskeridirektoratet",
	"ENVIRONMENTAL_NGOS": "Miljøorganisasjoner",
	"LOCAL_COMMUNITIES": "Lokalsamfunn",
	"INDUSTRY_ASSOCIATION": "Oppdrettsnæringen", 
	"FINANCIAL_SECTOR": "Finanssektoren"
}

func _ready():
	setup_ui()
	# Connect to game signals if needed
	# GameManager.stakeholder_system_updated.connect(_on_stakeholder_system_updated)

func setup_ui():
	# Initialize reputation history
	reputation_history.clear()
	
	# Create faction relationship bars
	create_faction_bars()
	
	# Update demands list
	update_demands_list()

func create_faction_bars():
	# Clear existing faction UI
	for child in faction_container.get_children():
		child.queue_free()
	
	# Create UI for each faction
	for faction_type in faction_names.keys():
		create_faction_bar(faction_type)

func create_faction_bar(faction_type: String):
	var faction_panel = preload("res://ui/components/FactionPanel.tscn").instantiate()
	
	# Set faction info
	var faction_data = factions.get(faction_type, {})
	var relationship_score = faction_data.get("relationship_score", 0.0)
	var influence = faction_data.get("influence", 0.5)
	
	# Configure the panel
	faction_panel.setup_faction(
		faction_names[faction_type],
		relationship_score,
		influence,
		faction_colors[faction_type]
	)
	
	faction_container.add_child(faction_panel)

# Alternative implementation if we can't use preloaded scenes
func create_faction_bar_simple(faction_type: String):
	var faction_panel = VBoxContainer.new()
	faction_panel.name = faction_type + "_Panel"
	
	# Faction name label
	var name_label = Label.new()
	name_label.text = faction_names[faction_type]
	name_label.add_theme_font_size_override("font_size", 14)
	faction_panel.add_child(name_label)
	
	# Reputation bar container
	var bar_container = HBoxContainer.new()
	
	# Reputation bar
	var reputation_bar = ProgressBar.new()
	reputation_bar.min_value = -1.0
	reputation_bar.max_value = 1.0
	reputation_bar.step = 0.01
	
	var faction_data = factions.get(faction_type, {})
	var relationship_score = faction_data.get("relationship_score", 0.0)
	var influence = faction_data.get("influence", 0.5)
	
	reputation_bar.value = relationship_score
	reputation_bar.custom_minimum_size = Vector2(200, 20)
	
	# Color the bar based on relationship
	var bar_color = get_relationship_color(relationship_score)
	reputation_bar.add_theme_stylebox_override("fill", create_colored_stylebox(bar_color))
	
	bar_container.add_child(reputation_bar)
	
	# Reputation value label
	var value_label = Label.new()
	value_label.text = "%+.2f" % relationship_score
	value_label.custom_minimum_size = Vector2(50, 20)
	bar_container.add_child(value_label)
	
	# Influence indicator
	var influence_label = Label.new()
	influence_label.text = "Innflytelse: %.0f%%" % (influence * 100)
	influence_label.add_theme_font_size_override("font_size", 10)
	influence_label.modulate = Color.GRAY
	
	faction_panel.add_child(bar_container)
	faction_panel.add_child(influence_label)
	
	# Add separator
	var separator = HSeparator.new()
	faction_panel.add_child(separator)
	
	faction_container.add_child(faction_panel)

func create_colored_stylebox(color: Color) -> StyleBoxFlat:
	var stylebox = StyleBoxFlat.new()
	stylebox.bg_color = color
	stylebox.corner_radius_top_left = 4
	stylebox.corner_radius_top_right = 4
	stylebox.corner_radius_bottom_left = 4
	stylebox.corner_radius_bottom_right = 4
	return stylebox

func get_relationship_color(score: float) -> Color:
	if score > 0.5:
		return Color.GREEN
	elif score > 0.0:
		return Color.YELLOW
	elif score > -0.5:
		return Color.ORANGE
	else:
		return Color.RED

func update_demands_list():
	# Clear existing demands UI
	for child in demands_container.get_children():
		child.queue_free()
	
	if active_demands.is_empty():
		var no_demands_label = Label.new()
		no_demands_label.text = "Ingen aktive krav"
		no_demands_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		no_demands_label.modulate = Color.GRAY
		demands_container.add_child(no_demands_label)
		return
	
	# Create demand cards
	for demand in active_demands:
		create_demand_card(demand)

func create_demand_card(demand: Dictionary):
	var demand_card = PanelContainer.new()
	demand_card.name = "Demand_" + str(demand.get("id", 0))
	
	# Add some styling
	var stylebox = StyleBoxFlat.new()
	stylebox.bg_color = Color(0.2, 0.2, 0.2, 0.8)
	stylebox.corner_radius_top_left = 8
	stylebox.corner_radius_top_right = 8
	stylebox.corner_radius_bottom_left = 8
	stylebox.corner_radius_bottom_right = 8
	stylebox.content_margin_left = 12
	stylebox.content_margin_right = 12
	stylebox.content_margin_top = 8
	stylebox.content_margin_bottom = 8
	demand_card.add_theme_stylebox_override("panel", stylebox)
	
	var content = VBoxContainer.new()
	
	# Header with faction and expiry
	var header = HBoxContainer.new()
	
	var faction_label = Label.new()
	var faction_type = demand.get("faction_type", "")
	faction_label.text = faction_names.get(faction_type, faction_type)
	faction_label.add_theme_color_override("font_color", faction_colors.get(faction_type, Color.WHITE))
	faction_label.add_theme_font_size_override("font_size", 12)
	header.add_child(faction_label)
	
	header.add_child(VSeparator.new())
	
	var expiry_label = Label.new()
	var duration_months = demand.get("duration_months", 1)
	expiry_label.text = "Frist: %d måneder" % duration_months
	expiry_label.add_theme_color_override("font_color", Color.YELLOW)
	expiry_label.add_theme_font_size_override("font_size", 10)
	expiry_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	expiry_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	header.add_child(expiry_label)
	
	content.add_child(header)
	
	# Description
	var description_label = Label.new()
	description_label.text = demand.get("description", "Ingen beskrivelse")
	description_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description_label.add_theme_font_size_override("font_size", 11)
	content.add_child(description_label)
	
	# Cost and consequence
	var details = VBoxContainer.new()
	
	var cost_label = Label.new()
	var cost_nok = demand.get("cost_nok", 0)
	cost_label.text = "Kostnad: %s NOK" % format_currency(cost_nok)
	cost_label.add_theme_color_override("font_color", Color.LIGHT_CORAL)
	cost_label.add_theme_font_size_override("font_size", 10)
	details.add_child(cost_label)
	
	var consequence_label = Label.new()
	consequence_label.text = "Konsekvens ved ignorering: " + demand.get("consequence_if_ignored", "Ukjent")
	consequence_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	consequence_label.add_theme_color_override("font_color", Color.LIGHT_GRAY)
	consequence_label.add_theme_font_size_override("font_size", 9)
	details.add_child(consequence_label)
	
	content.add_child(details)
	
	# Action buttons
	var button_container = HBoxContainer.new()
	button_container.alignment = BoxContainer.ALIGNMENT_END
	
	var ignore_button = Button.new()
	ignore_button.text = "Ignorer"
	ignore_button.add_theme_color_override("font_color", Color.ORANGE_RED)
	ignore_button.pressed.connect(_on_demand_ignored.bind(demand.get("id", 0)))
	button_container.add_child(ignore_button)
	
	var accept_button = Button.new()
	accept_button.text = "Oppfyll"
	accept_button.add_theme_color_override("font_color", Color.LIGHT_GREEN)
	accept_button.pressed.connect(_on_demand_accepted.bind(demand.get("id", 0)))
	button_container.add_child(accept_button)
	
	content.add_child(button_container)
	
	demand_card.add_child(content)
	demands_container.add_child(demand_card)

func format_currency(amount: float) -> String:
	if amount >= 1000000:
		return "%.1fM" % (amount / 1000000.0)
	elif amount >= 1000:
		return "%.0fK" % (amount / 1000.0)
	else:
		return "%.0f" % amount

func update_overall_reputation(reputation: float):
	overall_reputation_bar.value = (reputation + 1.0) / 2.0  # Convert from -1..1 to 0..1
	overall_reputation_label.text = "Omdømme: %.1f%%" % ((reputation + 1.0) * 50.0)
	
	# Update trend
	reputation_history.append(reputation)
	if reputation_history.size() > 10:
		reputation_history.pop_front()
	
	update_reputation_trend()

func update_reputation_trend():
	if reputation_history.size() < 2:
		reputation_trend_label.text = ""
		return
	
	var recent = reputation_history[-1]
	var previous = reputation_history[-2]
	var change = recent - previous
	
	if abs(change) < 0.01:
		reputation_trend_label.text = "→"
		reputation_trend_label.modulate = Color.WHITE
	elif change > 0:
		reputation_trend_label.text = "↗ +%.2f" % change
		reputation_trend_label.modulate = Color.GREEN
	else:
		reputation_trend_label.text = "↘ %.2f" % change
		reputation_trend_label.modulate = Color.RED

# Called from C# backend
func update_faction_data(faction_data: Dictionary):
	factions = faction_data
	create_faction_bars()

# Called from C# backend
func update_demands(demands_data: Array):
	active_demands = demands_data
	update_demands_list()

func show_consequence_preview(demand_id: int, action: String) -> Dictionary:
	# This would ideally call back to C# to get consequence preview
	# For now, return basic preview
	var consequence = {
		"relationship_change": 0.0,
		"cost_nok": 0.0,
		"description": "",
		"sanctions": [],
		"bonuses": []
	}
	
	var demand = null
	for d in active_demands:
		if d.get("id") == demand_id:
			demand = d
			break
	
	if demand == null:
		return consequence
	
	if action == "accept":
		consequence.relationship_change = 0.1
		consequence.cost_nok = demand.get("cost_nok", 0)
		consequence.description = "Forbedret forhold med " + faction_names.get(demand.get("faction_type", ""), "ukjent")
	else:  # ignore
		consequence.relationship_change = -0.15
		consequence.description = "Forverret forhold med " + faction_names.get(demand.get("faction_type", ""), "ukjent")
	
	return consequence

# Signal handlers
func _on_demand_accepted(demand_id: int):
	# Show preview before confirming
	var preview = show_consequence_preview(demand_id, "accept")
	# Could show confirmation dialog here
	
	demand_accepted.emit(demand_id)

func _on_demand_ignored(demand_id: int):
	# Show preview before confirming
	var preview = show_consequence_preview(demand_id, "ignore")
	# Could show confirmation dialog here
	
	demand_ignored.emit(demand_id)

# Called from game manager when stakeholder system is updated
func _on_stakeholder_system_updated(system_data: Dictionary):
	update_faction_data(system_data.get("factions", {}))
	update_demands(system_data.get("active_demands", []))
	update_overall_reputation(system_data.get("overall_reputation", 0.0))