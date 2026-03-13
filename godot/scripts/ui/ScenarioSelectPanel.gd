extends Control
## Scenario selection UI for Lakskongen campaign mode
## Displays 5 Norwegian-themed scenario cards with difficulty, description, and start button

signal scenario_selected(scenario_id: String)

# Scenario data matching ScenarioSystem.cs
const SCENARIOS = [
	{
		"id": "første-sesong",
		"name": "Første Sesong",
		"difficulty": 1,
		"description": "Din første sesong som laksebonde. Lær grunnleggende drift med et enkelt anlegg, rolig vær og stabile markeder.",
		"capital": "500,000 NOK",
		"region": "Vestland",
		"icon": "🐟",
		"color": Color(0.4, 0.7, 1.0) # Light blue
	},
	{
		"id": "lofoten-eventyr",
		"name": "Lofoten Eventyr",
		"difficulty": 3,
		"description": "Start et oppdrettsanlegg i det værharde Nordland. Håndter brutale vinterstormer og premium sashimi-priser.",
		"capital": "750,000 NOK",
		"region": "Nordland",
		"icon": "❄️",
		"color": Color(0.6, 0.8, 1.0) # Arctic blue
	},
	{
		"id": "laksebaronene",
		"name": "Laksebaronene",
		"difficulty": 4,
		"description": "Konkurranse-scenario: Bli den første til å nå 10M NOK i netto fortjeneste før 3 AI-konkurrenter.",
		"capital": "1,000,000 NOK",
		"region": "Trøndelag",
		"icon": "👑",
		"color": Color(1.0, 0.85, 0.3) # Gold
	},
	{
		"id": "bærekraft-mester",
		"name": "Bærekraft Mester",
		"difficulty": 4,
		"description": "Vinn ved å oppnå ASC Sustainability Leader-sertifisering. Balanser lønnsomhet med miljøvern.",
		"capital": "800,000 NOK",
		"region": "Vestland",
		"icon": "🌿",
		"color": Color(0.4, 0.9, 0.5) # Green
	},
	{
		"id": "krise-manager",
		"name": "Krise Manager",
		"difficulty": 5,
		"description": "Overta en mislykket bedrift: ILA-utbrudd, 2M NOK gjeld, og Mattilsynet varsel. Overlev krisen!",
		"capital": "-2,000,000 NOK",
		"region": "Rogaland",
		"icon": "🚨",
		"color": Color(1.0, 0.4, 0.4) # Red
	}
]

@onready var scenario_container = $MarginContainer/VBoxContainer/ScrollContainer/ScenarioGrid
@onready var title_label = $MarginContainer/VBoxContainer/TitlePanel/TitleLabel

func _ready():
	title_label.text = "VELG SCENARIO"
	_setup_norwegian_theme()
	_populate_scenarios()

func _setup_norwegian_theme():
	# Norwegian fjord-inspired gradient background
	var bg = ColorRect.new()
	bg.color = Color(0.12, 0.18, 0.28) # Deep fjord blue
	bg.z_index = -1
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	move_child(bg, 0)

func _populate_scenarios():
	for scenario in SCENARIOS:
		var card = _create_scenario_card(scenario)
		scenario_container.add_child(card)

func _create_scenario_card(scenario: Dictionary) -> PanelContainer:
	var card = PanelContainer.new()
	card.custom_minimum_size = Vector2(380, 280)
	
	# Norwegian-themed card styling
	var style = StyleBoxFlat.new()
	style.bg_color = Color(0.18, 0.22, 0.32) # Dark slate
	style.border_width_all = 3
	style.border_color = scenario["color"]
	style.corner_radius_all = 12
	style.shadow_size = 8
	style.shadow_color = Color(0, 0, 0, 0.4)
	card.add_theme_stylebox_override("panel", style)
	
	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 10)
	card.add_child(vbox)
	
	# Header with icon and name
	var header = HBoxContainer.new()
	vbox.add_child(header)
	
	var icon = Label.new()
	icon.text = scenario["icon"]
	icon.add_theme_font_size_override("font_size", 48)
	header.add_child(icon)
	
	var name_label = Label.new()
	name_label.text = scenario["name"]
	name_label.add_theme_font_size_override("font_size", 24)
	name_label.add_theme_color_override("font_color", scenario["color"])
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header.add_child(name_label)
	
	# Difficulty stars
	var stars = HBoxContainer.new()
	vbox.add_child(stars)
	
	var difficulty_label = Label.new()
	difficulty_label.text = "Vanskelighet: "
	difficulty_label.add_theme_font_size_override("font_size", 14)
	stars.add_child(difficulty_label)
	
	for i in range(5):
		var star = Label.new()
		star.text = "⭐" if i < scenario["difficulty"] else "☆"
		star.add_theme_font_size_override("font_size", 18)
		stars.add_child(star)
	
	# Description
	var desc = Label.new()
	desc.text = scenario["description"]
	desc.add_theme_font_size_override("font_size", 14)
	desc.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(desc)
	
	# Info grid
	var info = VBoxContainer.new()
	vbox.add_child(info)
	
	var region_label = Label.new()
	region_label.text = "📍 Region: " + scenario["region"]
	region_label.add_theme_font_size_override("font_size", 13)
	info.add_child(region_label)
	
	var capital_label = Label.new()
	capital_label.text = "💰 Startkapital: " + scenario["capital"]
	capital_label.add_theme_font_size_override("font_size", 13)
	var capital_color = Color.GREEN if not scenario["capital"].begins_with("-") else Color.RED
	capital_label.add_theme_color_override("font_color", capital_color)
	info.add_child(capital_label)
	
	# Start button
	var start_btn = Button.new()
	start_btn.text = "START SCENARIO"
	start_btn.custom_minimum_size = Vector2(0, 45)
	start_btn.add_theme_font_size_override("font_size", 16)
	
	var btn_style = StyleBoxFlat.new()
	btn_style.bg_color = scenario["color"]
	btn_style.corner_radius_all = 8
	start_btn.add_theme_stylebox_override("normal", btn_style)
	
	var btn_hover = btn_style.duplicate()
	btn_hover.bg_color = scenario["color"].lightened(0.2)
	start_btn.add_theme_stylebox_override("hover", btn_hover)
	
	var btn_pressed = btn_style.duplicate()
	btn_pressed.bg_color = scenario["color"].darkened(0.2)
	start_btn.add_theme_stylebox_override("pressed", btn_pressed)
	
	start_btn.pressed.connect(_on_scenario_start_pressed.bind(scenario["id"]))
	vbox.add_child(start_btn)
	
	return card

func _on_scenario_start_pressed(scenario_id: String):
	print("🎮 Starting scenario: ", scenario_id)
	scenario_selected.emit(scenario_id)
	
	# Optional: Fade out transition
	var tween = create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.4)
	tween.finished.connect(func(): queue_free())
