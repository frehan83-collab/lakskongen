extends PanelContainer

## Genetics Breeding UI Panel (Crusader Kings meets aquaculture)
## Displays genetic traits, breeding values, heritability, and inbreeding warnings.

@onready var trait_list: VBoxContainer = %TraitList
@onready var inbreeding_label: Label = %InbreedingLabel
@onready var inbreeding_warning: Panel = %InbreedingWarning
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
		breed_button.tooltip_text = "Start et nytt avlsprogram for å forbedre genetiske egenskaper."
	
	_refresh_ui()

func _unhandled_input(event: InputEvent) -> void:
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()
		return
	
	if breed_button and breed_button.visible and breed_button.has_focus() and event.is_action_pressed("ui_accept"):
		_on_breed_pressed()
		get_viewport().set_input_as_handled()

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
		name_label.tooltip_text = "Navn på genetisk egenskap."
		row.add_child(name_label)
		
		# Heritability indicator
		var h2_label = Label.new()
		var h2 = trait_data.get("heritability", 0.0)
		h2_label.text = tr("UI_GENETICS_H2_FORMAT") % h2
		h2_label.custom_minimum_size = Vector2(80, 0)
		h2_label.add_theme_color_override("font_color", _heritability_color(h2))
		h2_label.tooltip_text = "Arvelighet (h²): Hvor stor del av variasjonen i egenskapen som skyldes genetikk. Høyere h² betyr mer effektiv avl."
		row.add_child(h2_label)
		
		# Breeding value with color coding
		var bv_label = Label.new()
		var bv = trait_data.get("breeding_value", 0.0)
		bv_label.text = tr("UI_GENETICS_BV_FORMAT") % bv if bv != 0.0 else "0.00"
		bv_label.custom_minimum_size = Vector2(80, 0)
		bv_label.add_theme_color_override("font_color", _breeding_value_color(bv))
		bv_label.tooltip_text = "Avlverdi: Estimert genetisk verdi for egenskapen. Positive verdier er bedre, negative dårligere."
		row.add_child(bv_label)
		
		# Current value
		var cv_label = Label.new()
		cv_label.text = tr("UI_GENETICS_CV_FORMAT") % trait_data.get("current_value", 0.0)
		cv_label.custom_minimum_size = Vector2(80, 0)
		cv_label.tooltip_text = "Nåværende gjennomsnittsverdi for denne egenskapen i din populasjon."
		row.add_child(cv_label)
		
		trait_list.add_child(row)

func _update_inbreeding() -> void:
	if not _sim_bridge:
		return
	
	var coef = _sim_bridge.get_inbreeding_coefficient()
	
	if inbreeding_label:
		inbreeding_label.text = tr("UI_GENETICS_INBREEDING") % (coef * 100.0)
	inbreeding_label.tooltip_text = "Innavelskoeffisient: Hvor høy grad av innavl det er i fiskepopulasjonen. Høye verdier kan føre til redusert helse og vekst."
	
	if inbreeding_warning:
		inbreeding_warning.visible = coef > 0.15
		inbreeding_warning.tooltip_text = "Advarsel: Innavelsnivået er høyt. Vurder å introdusere ny stamfisk for å unngå negative effekter."
		if coef > 0.25:
			inbreeding_warning.modulate = Color.RED
		elif coef > 0.15:
			inbreeding_warning.modulate = Color.ORANGE

func _breeding_value_color(bv: float) -> Color:
	if bv > 0.5:
		return Color("#A3BE8C")  # Bright green
	elif bv > 0.0:
		return Color("#A3BE8C")  # Light green
	elif bv < -0.5:
		return Color("#BF616A")  # Bright red
	elif bv < 0.0:
		return Color(1.0, 0.6, 0.6)  # Light red
	else:
		return Color.WHITE

func _heritability_color(h2: float) -> Color:
	if h2 > 0.3:
		return Color("#88C0D0")  # Blue (high heritability = good for selection)
	elif h2 > 0.15:
		return Color("#4C566A")  # Gray
	else:
		return Color("#7B8794")  # Dim gray

func _on_breed_pressed() -> void:
	# Placeholder for breeding action
	print("GeneticsPanel: Breed button pressed (not yet implemented)")
