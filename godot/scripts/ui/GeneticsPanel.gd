extends PanelContainer

## Genetics Breeding UI Panel (Crusader Kings meets aquaculture)
## Displays genetic traits, breeding values, heritability, and inbreeding warnings.

@onready var trait_list: VBoxContainer = %TraitList
@onready var inbreeding_label: Label = %InbreedingLabel
@ontml:parameter name="content">@onready var inbreeding_warning: Panel = %InbreedingWarning
@onready var breed_button: Button = %BreedButton

var _sim_bridge: Node = null

func _ready() -> void:
	if has_node("/root/SimBridge"):
		_sim_bridge = get_node("/root/SimBridge")
		_sim_bridge.state_updated.connect(_on_state_updated)
	else:
		push_warning("GeneticsPanel: SimBridge not found")
	
	if breed_button:
		breed_button.pressed.connect(_on_breed_pressed)
	
	_refresh_ui()

func _on_state_updated() -> void:
	_refresh_ui()

func _refresh_ui() -> void:
	if not _sim_bridge:
		return
	
	_update_traits()
	_update_inbreeding()

func _update_traits() -> void:
	if not trait_list:
		return
	
	# Clear existing trait rows
	for child in trait_list.get_children():
		child.queue_free()
	
	var traits = _sim_bridge.get_genetic_traits()
	
	for trait_data in traits:
		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		
		# Trait name
		var name_label = Label.new()
		name_label.text = trait_data.get("name", "Unknown")
		name_label.custom_minimum_size = Vector2(150, 0)
		row.add_child(name_label)
		
		# Heritability indicator
		var h2_label = Label.new()
		var h2 = trait_data.get("heritability", 0.0)
		h2_label.text = "h² %.2f" % h2
		h2_label.custom_minimum_size = Vector2(80, 0)
		h2_label.add_theme_color_override("font_color", _heritability_color(h2))
		row.add_child(h2_label)
		
		# Breeding value with color coding
		var bv_label = Label.new()
		var bv = trait_data.get("breeding_value", 0.0)
		bv_label.text = "%+.2f" % bv if bv != 0.0 else "0.00"
		bv_label.custom_minimum_size = Vector2(80, 0)
		bv_label.add_theme_color_override("font_color", _breeding_value_color(bv))
		row.add_child(bv_label)
		
		# Current value
		var cv_label = Label.new()
		cv_label.text = "%.2f" % trait_data.get("current_value", 0.0)
		cv_label.custom_minimum_size = Vector2(80, 0)
		row.add_child(cv_label)
		
		trait_list.add_child(row)

func _update_inbreeding() -> void:
	if not _sim_bridge:
		return
	
	var coef = _sim_bridge.get_inbreeding_coefficient()
	
	if inbreeding_label:
		inbreeding_label.text = "Inbreeding: %.1f%%" % (coef * 100.0)
	
	if inbreeding_warning:
		inbreeding_warning.visible = coef > 0.15
		if coef > 0.25:
			inbreeding_warning.modulate = Color.RED
		elif coef > 0.15:
			inbreeding_warning.modulate = Color.ORANGE

func _breeding_value_color(bv: float) -> Color:
	if bv > 0.5:
		return Color(0.2, 0.9, 0.2)  # Bright green
	elif bv > 0.0:
		return Color(0.6, 1.0, 0.6)  # Light green
	elif bv < -0.5:
		return Color(0.9, 0.2, 0.2)  # Bright red
	elif bv < 0.0:
		return Color(1.0, 0.6, 0.6)  # Light red
	else:
		return Color.WHITE

func _heritability_color(h2: float) -> Color:
	if h2 > 0.3:
		return Color(0.4, 0.8, 1.0)  # Blue (high heritability = good for selection)
	elif h2 > 0.15:
		return Color(0.8, 0.8, 0.8)  # Gray
	else:
		return Color(0.6, 0.6, 0.6)  # Dim gray

func _on_breed_pressed() -> void:
	# Placeholder for breeding action
	print("GeneticsPanel: Breed button pressed (not yet implemented)")
