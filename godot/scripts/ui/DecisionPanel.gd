extends CanvasLayer

## DecisionPanel — Modal popup for critical decisions
## Shows trade-offs, timer, and 2-4 choice buttons
## Connects to DecisionManager.cs signals

var _current_decision_id: String = ""
var _decision_manager: Node = null

func _ready():
	visible = false
	_decision_manager = get_node("/root/DecisionManager")
	
	if _decision_manager:
		_decision_manager.DecisionTriggered.connect(_on_decision_triggered)
		_decision_manager.DecisionResolved.connect(_on_decision_resolved)
	
	# Build UI structure
	_build_ui()

func _build_ui():
	# Dark overlay
	var overlay := ColorRect.new()
	overlay.color = Color(0, 0, 0, 0.8)
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(overlay)
	
	# Center container
	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)
	
	# Main panel
	var panel := LakskongenTheme.create_styled_panel()
	panel.custom_minimum_size = Vector2(700, 500)
	center.add_child(panel)
	
	# Content VBox
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)
	panel.add_child(vbox)
	
	# Header
	var header := LakskongenTheme.create_label("CRITICAL DECISION", LakskongenTheme.FONT_XL, LakskongenTheme.ACCENT)
	header.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	header.name = "Header"
	vbox.add_child(header)
	
	# Timer label
	var timer := LakskongenTheme.create_label("", LakskongenTheme.FONT_MD, LakskongenTheme.WARNING)
	timer.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	timer.name = "Timer"
	vbox.add_child(timer)
	
	# Divider
	vbox.add_child(LakskongenTheme.create_divider())
	
	# Description
	var desc := LakskongenTheme.create_label("", LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
	desc.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc.name = "Description"
	vbox.add_child(desc)
	
	# Spacer
	var spacer := Control.new()
	spacer.custom_minimum_size.y = LakskongenTheme.SPACE_MD
	vbox.add_child(spacer)
	
	# Choices container
	var choices_vbox := VBoxContainer.new()
	choices_vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	choices_vbox.name = "ChoicesVBox"
	vbox.add_child(choices_vbox)
	
	# Spacer
	var spacer2 := Control.new()
	spacer2.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(spacer2)

func _on_decision_triggered(decision_data: Dictionary):
	_current_decision_id = decision_data.get("id", "")
	
	# Update UI
	var header = $CenterContainer/PanelContainer/VBoxContainer/Header as Label
	var timer = $CenterContainer/PanelContainer/VBoxContainer/Timer as Label
	var desc = $CenterContainer/PanelContainer/VBoxContainer/Description as Label
	var choices_vbox = $CenterContainer/PanelContainer/VBoxContainer/ChoicesVBox as VBoxContainer
	
	header.text = decision_data.get("title", "DECISION")
	
	var days_remaining = decision_data.get("days_remaining", 7)
	timer.text = "⏱️ %d days to decide" % days_remaining
	
	desc.text = decision_data.get("description", "")
	
	# Clear old choices
	for child in choices_vbox.get_children():
		child.queue_free()
	
	# Add choice buttons
	var choices = decision_data.get("choices", [])
	for i in range(choices.size()):
		var choice = choices[i]
		var choice_panel := _create_choice_button(choice.get("label", "Option %d" % (i+1)), choice.get("trade_off", ""), i)
		choices_vbox.add_child(choice_panel)
	
	# Show modal
	visible = true
	
	# Play sound effect
	if has_node("/root/GameAudio"):
		get_node("/root/GameAudio").play_sfx("decision_alert")

func _create_choice_button(label_text: String, trade_off: String, choice_index: int) -> PanelContainer:
	# Choice panel with hover effect
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = LakskongenTheme.PANEL_ALT
	style.corner_radius_top_left = LakskongenTheme.RADIUS_SM
	style.corner_radius_top_right = LakskongenTheme.RADIUS_SM
	style.corner_radius_bottom_left = LakskongenTheme.RADIUS_SM
	style.corner_radius_bottom_right = LakskongenTheme.RADIUS_SM
	style.content_margin_left = LakskongenTheme.SPACE_MD
	style.content_margin_right = LakskongenTheme.SPACE_MD
	style.content_margin_top = LakskongenTheme.SPACE_SM
	style.content_margin_bottom = LakskongenTheme.SPACE_SM
	style.border_width_left = 2
	style.border_width_right = 2
	style.border_width_top = 2
	style.border_width_bottom = 2
	style.border_color = LakskongenTheme.ACCENT2.darkened(0.5)
	panel.add_theme_stylebox_override("panel", style)
	
	# Button
	var btn := Button.new()
	btn.custom_minimum_size.y = 80
	btn.mouse_filter = Control.MOUSE_FILTER_STOP
	
	# Button style (transparent, only text)
	var btn_style := StyleBoxFlat.new()
	btn_style.bg_color = Color(0, 0, 0, 0)
	btn.add_theme_stylebox_override("normal", btn_style)
	btn.add_theme_stylebox_override("hover", btn_style)
	btn.add_theme_stylebox_override("pressed", btn_style)
	
	# VBox for label + trade-off
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
	vbox.mouse_filter = Control.MOUSE_FILTER_IGNORE
	
	# Choice label
	var choice_label := LakskongenTheme.create_label(label_text, LakskongenTheme.FONT_LG, LakskongenTheme.ACCENT)
	choice_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox.add_child(choice_label)
	
	# Trade-off text
	var trade_off_label := LakskongenTheme.create_label(trade_off, LakskongenTheme.FONT_SM, LakskongenTheme.MUTED)
	trade_off_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	trade_off_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox.add_child(trade_off_label)
	
	btn.add_child(vbox)
	panel.add_child(btn)
	
	# Connect button press
	btn.pressed.connect(_on_choice_selected.bind(choice_index))
	
	# Hover effects
	btn.mouse_entered.connect(func():
		var hover_style := style.duplicate()
		hover_style.bg_color = LakskongenTheme.PANEL_HOVER
		hover_style.border_color = LakskongenTheme.ACCENT
		panel.add_theme_stylebox_override("panel", hover_style)
	)
	
	btn.mouse_exited.connect(func():
		panel.add_theme_stylebox_override("panel", style)
	)
	
	return panel

func _on_choice_selected(choice_index: int):
	if _decision_manager and _current_decision_id != "":
		_decision_manager.ResolveDecision(_current_decision_id, choice_index)
	
	# Play confirm sound
	if has_node("/root/GameAudio"):
		get_node("/root/GameAudio").play_sfx("button_click")

func _on_decision_resolved(decision_id: String, choice_index: int):
	# Hide panel
	visible = false
	_current_decision_id = ""

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

