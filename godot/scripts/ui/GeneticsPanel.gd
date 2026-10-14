extends PanelContainer

## Genetics Breeding UI Panel (Crusader Kings meets aquaculture)
## Displays genetic traits, breeding values, heritability, and inbreeding warnings.

@onready var trait_list: VBoxContainer = %TraitList
@onready var inbreeding_label: Label = %InbreedingLabel
@onready var inbreeding_warning: Panel = %InbreedingWarning
@onready var breed_button: Button = %BreedButton
@onready var inbreeding_detail_label: Label = %InbreedingDetailLabel
@onready var gain_list: VBoxContainer = %GainList
@onready var ge_list: VBoxContainer = %GeList

var _sim_bridge: Node = null

func _ready() -> void:
	if has_node("/root/SimBridge"):
		_sim_bridge = get_node("/root/SimBridge")
		_sim_bridge.state_updated.connect(_on_state_updated)
	else:
		push_warning("GeneticsPanel: SimBridge not found")
	
	if breed_button:
		breed_button.pressed.connect(_on_breed_pressed)
		# No breeding programme yet (roadmap D6 folds strain choice into
		# stocking): a disabled control that says why, not a dead verb.
		breed_button.text = tr("UI_GENETICS_BREED")
		breed_button.disabled = true
		breed_button.tooltip_text = tr("UI_GENETICS_BREED_DISABLED_TIP")
	_apply_localization()
	
	_refresh_ui()

	var audio := get_node_or_null("/root/AudioController")
	if audio and audio.has_method("attach_ui_sounds"):
		audio.attach_ui_sounds(self)

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

func _apply_localization() -> void:
	for pair in [["TitleLabel", "UI_GENETICS_TITLE"], ["TraitNameLabel", "UI_GENETICS_COL_TRAIT"],
			["HeritabilityLabel", "UI_GENETICS_COL_HERITABILITY"], ["BreedingValueLabel", "UI_GENETICS_COL_BREEDING_VALUE"],
			["CurrentValueLabel", "UI_GENETICS_COL_CURRENT"], ["WarningLabel", "UI_GENETICS_INBREEDING_WARNING"],
			["GainHeader", "UI_GENETICS_GAIN_HEADER"], ["GeHeader", "UI_GENETICS_GE_HEADER"]]:
		var lbl := find_child(pair[0], true, false) as Label
		if lbl:
			lbl.text = tr(pair[1])

func _on_state_updated() -> void:
	_refresh_ui()

func _refresh_ui() -> void:
	if not _sim_bridge:
		return
	
	_update_traits()
	_update_inbreeding()
	_update_genomic_preview()
	_update_inbreeding_status()
	_update_ge_traits()

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
		name_label.text = SimLabels.genetic_trait(str(trait_data.get("name", "")))
		name_label.custom_minimum_size = Vector2(150, 0)
		name_label.tooltip_text = tr("UI_GENETICS_TRAIT_TIP")
		row.add_child(name_label)
		
		# Heritability indicator
		var h2_label = Label.new()
		var h2 = trait_data.get("heritability", 0.0)
		h2_label.text = SimFormatters.num(float(h2), 2)  # the column header says "Arvbarhet"
		h2_label.custom_minimum_size = Vector2(80, 0)
		LakskongenTheme.set_text_color(h2_label, _heritability_color(h2))
		h2_label.tooltip_text = tr("UI_GENETICS_H2_TIP")
		row.add_child(h2_label)
		
		# Breeding value with color coding
		var bv_label = Label.new()
		var bv = trait_data.get("breeding_value", 0.0)
		bv_label.text = SimFormatters.num(float(bv), 2)
		bv_label.custom_minimum_size = Vector2(80, 0)
		LakskongenTheme.set_text_color(bv_label, _breeding_value_color(bv))
		bv_label.tooltip_text = tr("UI_GENETICS_BV_TIP")
		row.add_child(bv_label)
		
		# Current value
		var cv_label = Label.new()
		cv_label.text = SimFormatters.num(float(trait_data.get("current_value", 0.0)), 1)
		cv_label.custom_minimum_size = Vector2(80, 0)
		cv_label.tooltip_text = tr("UI_GENETICS_CV_TIP")
		row.add_child(cv_label)
		
		trait_list.add_child(row)

func _update_inbreeding() -> void:
	if not _sim_bridge:
		return
	
	var coef = _sim_bridge.get_inbreeding_coefficient()
	
	if inbreeding_label:
		inbreeding_label.text = tr("UI_GENETICS_INBREEDING") % SimFormatters.percent(float(coef), 1)
	inbreeding_label.tooltip_text = tr("UI_GENETICS_INBREEDING_TIP")
	
	if inbreeding_warning:
		inbreeding_warning.visible = coef > 0.15
		inbreeding_warning.tooltip_text = tr("UI_GENETICS_INBREEDING_WARNING_TIP")
		if coef > 0.25:
			inbreeding_warning.theme_type_variation = &"CalloutCrit"
		elif coef > 0.15:
			inbreeding_warning.theme_type_variation = &"CalloutWarn"

func _update_genomic_preview() -> void:
	if not gain_list or not _sim_bridge:
		return
	for child in gain_list.get_children():
		child.queue_free()
	if not _sim_bridge.has_method("get_genomic_selection_preview"):
		gain_list.add_child(_note_label(tr("UI_GENETICS_GAIN_UNAVAILABLE")))
		return

	var preview: Array = _sim_bridge.get_genomic_selection_preview()
	if preview.is_empty():
		gain_list.add_child(_note_label(tr("UI_GENETICS_NO_BROODSTOCK")))
		return

	for entry in preview:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		var name_label := Label.new()
		name_label.text = SimLabels.genetic_trait(str(entry.get("trait", "")))
		name_label.custom_minimum_size = Vector2(150, 0)
		name_label.tooltip_text = tr("UI_GENETICS_GAIN_TIP")
		row.add_child(name_label)
		var gain_label := Label.new()
		var gain := float(entry.get("gain", 0.0))
		gain_label.text = ("+" if gain > 0.0 else "") + SimFormatters.num(gain, 3)
		gain_label.custom_minimum_size = Vector2(80, 0)
		LakskongenTheme.set_tone(gain_label, &"Ok" if gain > 0.0 else &"")
		row.add_child(gain_label)
		gain_list.add_child(row)

func _update_inbreeding_status() -> void:
	if not inbreeding_detail_label or not _sim_bridge:
		return
	if not _sim_bridge.has_method("get_inbreeding_status"):
		inbreeding_detail_label.text = tr("UI_GENETICS_INBREEDING_UNAVAILABLE")
		return

	var status: Dictionary = _sim_bridge.get_inbreeding_status()
	var coef := float(status.get("coefficient", 0.0))
	var penalty_pct := float(status.get("penalty_percent", 0.0))
	var level := str(status.get("level", "none"))
	inbreeding_detail_label.text = tr("UI_GENETICS_INBREEDING_DETAIL") % [SimFormatters.percent(coef, 1),
		SimFormatters.percent(penalty_pct / 100.0, 1)]
	inbreeding_detail_label.tooltip_text = tr("UI_GENETICS_INBREEDING_DETAIL_TIP")
	match level:
		"high":
			LakskongenTheme.set_tone(inbreeding_detail_label, &"Crit")
		"elevated":
			LakskongenTheme.set_tone(inbreeding_detail_label, &"Warn")
		_:
			LakskongenTheme.set_tone(inbreeding_detail_label, &"")

func _update_ge_traits() -> void:
	if not ge_list or not _sim_bridge:
		return
	for child in ge_list.get_children():
		child.queue_free()
	if not _sim_bridge.has_method("get_ge_traits"):
		ge_list.add_child(_note_label(tr("UI_GENETICS_GE_UNAVAILABLE")))
		return

	var traits: Array = _sim_bridge.get_ge_traits()
	if traits.is_empty():
		ge_list.add_child(_note_label(tr("UI_GENETICS_GE_NONE")))
		return

	for entry in traits:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		var name_label := Label.new()
		name_label.text = SimLabels.ge_trait(str(entry.get("id", entry.get("name", ""))))
		name_label.custom_minimum_size = Vector2(220, 0)
		name_label.tooltip_text = tr("UI_GENETICS_GE_TIP")
		row.add_child(name_label)
		var status_label := Label.new()
		var region_status := str(entry.get("region_status", "NotSubmitted"))
		status_label.text = tr("UI_GENETICS_GE_STATUS") % [SimLabels.ge_status(region_status),
			SimFormatters.percent(float(entry.get("reputation_cost", 0.0)))]
		status_label.custom_minimum_size = Vector2(150, 0)
		LakskongenTheme.set_text_color(status_label, _ge_status_color(region_status))
		row.add_child(status_label)
		ge_list.add_child(row)

func _ge_status_color(region_status: String) -> Color:
	match region_status:
		"Approved":
			return Color("#2E6B45")
		"Banned":
			return Color("#B3172F")
		"UnderReview":
			return LakskongenTheme.CHART_WARN
		_:
			return Color("#5A6168")

func _note_label(text: String) -> Label:
	var label := Label.new()
	label.text = text
	LakskongenTheme.set_tone(label, &"Muted")
	return label

func _breeding_value_color(bv: float) -> Color:
	if bv > 0.5:
		return Color("#2E6B45")  # Bright green
	elif bv > 0.0:
		return Color("#2E6B45")  # Light green
	elif bv < -0.5:
		return Color("#B3172F")  # Bright red
	elif bv < 0.0:
		return LakskongenTheme.CHART_CRIT  # Light red
	else:
		return LakskongenTheme.CHART_TEXT  # neutral: the surface text tone

func _heritability_color(h2: float) -> Color:
	if h2 > 0.3:
		return Color("#1F5E73")  # Blue (high heritability = good for selection)
	elif h2 > 0.15:
		return Color("#5A6168")  # Gray
	else:
		return Color("#5A6168")  # Dim gray

func _on_breed_pressed() -> void:
	pass  # Disabled until a breeding programme exists (see _ready).
