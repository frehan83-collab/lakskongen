extends CanvasLayer

## DecisionPanel v2.1 — Modal popup for critical decisions (scene-based).
## Layout lives in scenes/ui/DecisionPanel.tscn; script is wiring only:
## @onready % refs, signal hookups, localization, dynamic choice buttons.
## Shows trade-offs, timer, and 2-4 choice buttons.
## Connects to DecisionManager signals.

@onready var _dialog_panel: PanelContainer = %DialogPanel
@onready var _header: Label = %Header
@onready var _timer_label: Label = %TimerLabel
@onready var _description: Label = %Description
@onready var _choices_box: VBoxContainer = %ChoicesBox

var _current_decision_id: String = ""
var _decision_manager: Node = null

func _ready():
	visible = false
	_decision_manager = get_node_or_null("/root/DecisionManager")

	if _decision_manager:
		if _decision_manager.has_signal("DecisionTriggered"):
			_decision_manager.DecisionTriggered.connect(_on_decision_triggered)
		if _decision_manager.has_signal("DecisionResolved"):
			_decision_manager.DecisionResolved.connect(_on_decision_resolved)

	_apply_localization()

func _apply_localization() -> void:
	_header.text = _tr_or("UI_DECISION_TITLE", "KRITISK BESLUTNING")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _on_decision_triggered(decision_data: Dictionary):
	_current_decision_id = decision_data.get("id", "")

	_header.text = str(decision_data.get("title", _tr_or("UI_DECISION_TITLE", "KRITISK BESLUTNING")))

	var days_remaining = decision_data.get("days_remaining", 7)
	_timer_label.text = _tr_or("UI_DECISION_DAYS_FORMAT", "⏱️ %d dager igjen å bestemme") % days_remaining

	_description.text = str(decision_data.get("description", ""))

	# Clear old choices
	for child in _choices_box.get_children():
		child.queue_free()

	# Add choice buttons
	var choices = decision_data.get("choices", [])
	for i in range(choices.size()):
		var choice = choices[i]
		var choice_panel := _create_choice_button(choice.get("label", "Alternativ %d" % (i + 1)), choice.get("trade_off", ""), i)
		_choices_box.add_child(choice_panel)

	# Show modal
	visible = true

	# Play sound effect
	if has_node("/root/AudioController"):
		get_node("/root/AudioController").play_ui("notification")

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
	if has_node("/root/AudioController"):
		get_node("/root/AudioController").play_ui("button_click")

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
