# SustainabilityPanel.gd
# UI for displaying sustainability score (0-100) and certification level
# Godot 4 GDScript

extends PanelContainer
class_name SustainabilityPanel

# UI References (assign in editor or create dynamically)
@onready var score_label: Label
@onready var certification_label: Label
@onready var price_multiplier_label: Label

# Category breakdown display
@onready var category_container: VBoxContainer

# Color scheme from design tokens
const COLOR_BG = "#0F1720"
const COLOR_PANEL = "#121826"
const COLOR_TEXT = "#E6EEF3"
const COLOR_MUTED = "#9AA7B2"
const COLOR_ACCENT = "#2AB7B5"
const COLOR_ACCENT_2 = "#1F7A8C"
const COLOR_DANGER = "#E06666"

# Certification level colors
const CERT_COLORS = {
	"NonCompliant": "#E06666",        # Red
	"BasicCompliance": "#D4AF37",     # Gold
	"ASCCertified": "#2AB7B5",        # Cyan (accent)
	"OrganicCertified": "#4CAF50",    # Green
	"SustainabilityLeader": "#00E676" # Bright green
}

# Current data
var current_score: int = 0
var current_certification: String = "NonCompliant"
var category_scores: Dictionary = {}

signal score_updated(score: int, certification: String)

func _ready():
	# Create UI elements if not assigned
	if not score_label:
		_create_ui()
	
	# Connect to SimBridge for live data updates
	if has_node("/root/SimBridge"):
		var sim_bridge = get_node("/root/SimBridge")
		sim_bridge.state_updated.connect(_on_state_updated)
		print("SustainabilityPanel: connected to SimBridge")
	else:
		push_warning("SustainabilityPanel: SimBridge not found")
	
	# Initial display with live data
	_refresh_from_bridge()

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _create_ui():
	"""Dynamically create UI elements if not set in editor"""
	
	# Main container
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_right", 16)
	add_child(margin)
	
	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 12)
	margin.add_child(vbox)
	
	# Header
	var header = Label.new()
	header.text = "🌱 Sustainability Score"
	header.add_theme_font_size_override("font_size", 20)
	header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	header.tooltip_text = "Bærekraftsscore (0-100) og sertifiseringsnivå basert på miljøpåvirkning og praksis."
	vbox.add_child(header)
	
	# Score display (large)
	score_label = Label.new()
	score_label.add_theme_font_size_override("font_size", 48)
	score_label.add_theme_color_override("font_color", Color(COLOR_ACCENT))
	score_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(score_label)
	
	# Certification level
	certification_label = Label.new()
	certification_label.add_theme_font_size_override("font_size", 18)
	certification_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(certification_label)
	
	# Price multiplier
	price_multiplier_label = Label.new()
	price_multiplier_label.add_theme_font_size_override("font_size", 14)
	price_multiplier_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	price_multiplier_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	vbox.add_child(price_multiplier_label)
	
	# Separator
	var separator = HSeparator.new()
	vbox.add_child(separator)
	
	# Category breakdown header
	var breakdown_header = Label.new()
	breakdown_header.text = "Category Breakdown"
	breakdown_header.add_theme_font_size_override("font_size", 16)
	breakdown_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(breakdown_header)
	
	# Category container
	category_container = VBoxContainer.new()
	category_container.add_theme_constant_override("separation", 8)
	vbox.add_child(category_container)

func update_display(score: int, certification: String, categories: Dictionary):
	"""Update all UI elements with new sustainability data"""
	current_score = score
	current_certification = certification
	category_scores = categories
	
	# Update score label
	if score_label:
		score_label.text = "%d/100" % score
		score_label.add_theme_color_override("font_color", _get_score_color(score))
	
	# Update certification label
	if certification_label:
		var cert_display = _format_certification(certification)
		certification_label.text = cert_display
		certification_label.add_theme_color_override("font_color", Color(CERT_COLORS.get(certification, COLOR_MUTED)))
	
	# Update price multiplier
	if price_multiplier_label:
		var multiplier = _get_price_multiplier(certification)
		var bonus_text = ""
		if multiplier > 1.0:
			bonus_text = "Price Bonus: +%.0f%%" % ((multiplier - 1.0) * 100)
		elif multiplier < 1.0:
			bonus_text = "Price Penalty: %.0f%%" % ((multiplier - 1.0) * 100)
		else:
			bonus_text = "Standard Pricing"
		price_multiplier_label.text = bonus_text
	
	# Update category breakdown
	_update_categories(categories)
	
	emit_signal("score_updated", score, certification)

func _update_categories(categories: Dictionary):
	"""Update category breakdown bars"""
	if not category_container:
		return
	
	# Clear existing children
	for child in category_container.get_children():
		child.queue_free()
	
	# Category display order and max points
	var category_order = [
		{"name": "SeaLiceManagement", "display": "Sea Lice Management", "max": 25},
		{"name": "EscapePrevention", "display": "Escape Prevention", "max": 20},
		{"name": "FeedSustainability", "display": "Feed Sustainability", "max": 20},
		{"name": "CarbonFootprint", "display": "Carbon Footprint", "max": 15},
		{"name": "WelfareStandards", "display": "Welfare Standards", "max": 10},
		{"name": "EcosystemImpact", "display": "Ecosystem Impact", "max": 10}
	]
	
	for cat in category_order:
		var score = categories.get(cat.name, 0)
		var max_score = cat.max
		_create_category_bar(cat.display, score, max_score)

func _create_category_bar(label_text: String, score: int, max_score: int):
	"""Create a single category progress bar"""
	var container = HBoxContainer.new()
	container.add_theme_constant_override("separation", 8)
	category_container.add_child(container)
	
	# Label
	var label = Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(200, 0)
	label.add_theme_font_size_override("font_size", 12)
	label.add_theme_color_override("font_color", Color(COLOR_TEXT))
	container.add_child(label)
	
	# Progress bar
	var progress = ProgressBar.new()
	progress.max_value = max_score
	progress.value = score
	progress.custom_minimum_size = Vector2(150, 20)
	progress.show_percentage = false
	container.add_child(progress)
	
	# Score text
	var score_text = Label.new()
	score_text.text = "%d/%d" % [score, max_score]
	score_text.custom_minimum_size = Vector2(50, 0)
	score_text.add_theme_font_size_override("font_size", 12)
	score_text.add_theme_color_override("font_color", Color(COLOR_MUTED))
	container.add_child(score_text)

func _get_score_color(score: int) -> Color:
	"""Get color based on score (red -> yellow -> green gradient)"""
	if score >= 85:
		return Color("#00E676")  # Bright green
	elif score >= 70:
		return Color("#4CAF50")  # Green
	elif score >= 50:
		return Color("#D4AF37")  # Gold
	else:
		return Color("#E06666")  # Red/danger

func _format_certification(cert: String) -> String:
	"""Format certification level for display"""
	match cert:
		"NonCompliant":
			return "⚠️ Non-Compliant"
		"BasicCompliance":
			return "✓ Basic Compliance"
		"ASCCertified":
			return "⭐ ASC Certified"
		"OrganicCertified":
			return "🌿 Organic Certified"
		"SustainabilityLeader":
			return "🏆 Sustainability Leader"
		_:
			return cert

func _get_price_multiplier(cert: String) -> float:
	"""Get price multiplier from certification level (from bridge if available, otherwise fallback)"""
	if has_node("/root/SimBridge"):
		var sim_bridge = get_node("/root/SimBridge")
		return sim_bridge.get_price_multiplier()
	
	# Fallback to local calculation
	match cert:
		"NonCompliant":
			return 0.85
		"BasicCompliance":
			return 1.00
		"ASCCertified":
			return 1.08
		"OrganicCertified":
			return 1.15
		"SustainabilityLeader":
			return 1.15
		_:
			return 1.00

# === EXTERNAL API ===
# Called from C# via signals or direct method calls

func on_score_changed(score: int, certification: String):
	"""Handler for C# SustainabilityManager.OnScoreChanged event"""
	# Fetch latest category breakdown from game state
	# For now, just update with current data
	update_display(score, certification, category_scores)

func set_category_scores(categories: Dictionary):
	"""Set category breakdown (called from game manager)"""
	category_scores = categories
	update_display(current_score, current_certification, categories)

# === TEST/DEBUG ===

func _simulate_score_change():
	"""Test function to simulate score changes"""
	var test_scores = [30, 55, 75, 88, 97]
	var test_certs = ["NonCompliant", "BasicCompliance", "ASCCertified", "OrganicCertified", "SustainabilityLeader"]
	
	var test_categories = {
		"SeaLiceManagement": 20,
		"EscapePrevention": 15,
		"FeedSustainability": 18,
		"CarbonFootprint": 12,
		"WelfareStandards": 8,
		"EcosystemImpact": 7
	}
	
	for i in range(test_scores.size()):
		await get_tree().create_timer(2.0).timeout
		update_display(test_scores[i], test_certs[i], test_categories)

# === BRIDGE DATA REFRESH ===

func _on_state_updated():
	"""Called when SimBridge state updates"""
	_refresh_from_bridge()

func _refresh_from_bridge():
	"""Pull live data from SimBridge and update display"""
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	
	# Get sustainability score and certification level
	var score = sim_bridge.sustainability_score
	var cert = sim_bridge.get_certification_level()
	
	# Get detailed breakdown
	var breakdown = sim_bridge.get_sustainability_breakdown()
	
	# Convert breakdown to category scores (normalized to 0-25 scale)
	var categories = {
		"SeaLiceManagement": breakdown.get("sea_lice", 0.0) * 25.0 / 100.0,
		"EscapePrevention": breakdown.get("escape", 0.0) * 20.0 / 100.0,
		"FeedSustainability": breakdown.get("feed", 0.0) * 20.0 / 100.0,
		"CarbonFootprint": breakdown.get("carbon", 0.0) * 15.0 / 100.0,
		"WelfareStandards": breakdown.get("welfare", 0.0) * 10.0 / 100.0,
		"EcosystemImpact": breakdown.get("ecosystem", 0.0) * 10.0 / 100.0
	}
	
	# Update display with live data
	update_display(score, cert, categories)

# Uncomment to test on load:
# func _ready():
# 	super._ready()
# 	_simulate_score_change()
