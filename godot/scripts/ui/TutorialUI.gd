extends PanelContainer

## TutorialUI — Step-based onboarding panel for Lakskongen.
##
## Displays the current tutorial step, objective description, and contextual
## hints. Listens to SimBridge for state updates and step-completion events.
##
## Node structure expected in scene:
##   TutorialUI (PanelContainer)
##   └── VBoxContainer
##       ├── HBoxContainer (header)
##       │   ├── Label          (step_label)     e.g. "Steg 2 / 12"
##       │   ├── ProgressBar    (progress_bar)
##       │   └── Button         (close_button)   "✕"
##       ├── Label              (title_label)
##       ├── HSeparator
##       ├── Label              (description_label)
##       ├── HSeparator
##       ├── HBoxContainer (hint row)
##       │   ├── Label          (hint_icon)      "💡"
##       │   └── Label          (hint_label)
##       └── Label              (reward_label)   shows reward if > 0

# ── Node references (assign in scene or via @export) ──────────────────────────

@export var step_label: Label
@export var progress_bar: ProgressBar
@export var close_button: Button
@export var skip_button: Button  # NEW: Skip tutorial button
@export var title_label: Label
@export var description_label: Label
@export var hint_label: Label
@export var reward_label: Label
@export var progress_dots_container: HBoxContainer  # NEW: Visual progress indicator

# ── Internal ──────────────────────────────────────────────────────────────────

var _tween: Tween = null
var _minimised: bool = false
var _tutorial_skipped: bool = false  # Track if user skipped tutorial

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	# Connect to SimBridge signals
	if has_node("/root/SimBridge"):
		var sb: Node = get_node("/root/SimBridge")
		sb.state_updated.connect(_on_state_updated)
		sb.tutorial_step_completed.connect(_on_step_completed)
	else:
		push_warning("TutorialUI: /root/SimBridge not found")

	if close_button:
		close_button.pressed.connect(_on_close_pressed)
		close_button.tooltip_text = "Minimer/utvid tutorial-panelet"
	
	if skip_button:
		skip_button.pressed.connect(_on_skip_pressed)
		skip_button.tooltip_text = "Hopp over tutorial og aktiver fri spilling"

	_setup_progress_dots()
	_refresh_display()

# ── Signal handlers ───────────────────────────────────────────────────────────

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _on_state_updated() -> void:
	_refresh_display()

func _on_step_completed(step_number: int, title: String, reward_nok: float) -> void:
	_play_completion_animation(step_number, title, reward_nok)
	# Re-expand if the player had minimised the panel
	if _minimised:
		_set_minimised(false)

func _on_close_pressed() -> void:
	_set_minimised(!_minimised)

func _on_skip_pressed() -> void:
	# Confirm skip dialog
	var confirm = ConfirmationDialog.new()
	confirm.dialog_text = "Er du sikker på at du vil hoppe over tutorial?\n\nDu kan alltid starte en ny tutorial fra menyvalget 'Hjelp → Vis tutorial'."
	confirm.ok_button_text = "Hopp over"
	confirm.cancel_button_text = "Avbryt"
	add_child(confirm)
	confirm.popup_centered()
	
	confirm.confirmed.connect(func():
		_tutorial_skipped = true
		visible = false
		# Notify SimBridge that tutorial was skipped
		if has_node("/root/SimBridge"):
			var sb: Node = get_node("/root/SimBridge")
			if sb.has_method("skip_tutorial"):
				sb.skip_tutorial()
		confirm.queue_free()
	)
	
	confirm.canceled.connect(func():
		confirm.queue_free()
	)

# ── Progress dots setup ───────────────────────────────────────────────────────

func _setup_progress_dots() -> void:
	if not progress_dots_container:
		return
	
	# Clear existing dots
	for child in progress_dots_container.get_children():
		child.queue_free()
	
	# Create 23 progress dots (one per tutorial step)
	const TOTAL_STEPS = 23
	for i in range(TOTAL_STEPS):
		var dot = ColorRect.new()
		dot.custom_minimum_size = Vector2(8, 8)
		dot.color = Color(0.85, 0.87, 0.91, 0.5)  # Gray inactive
		dot.tooltip_text = "Steg %d" % (i + 1)
		progress_dots_container.add_child(dot)

func _update_progress_dots(current_step: int, completed_count: int) -> void:
	if not progress_dots_container:
		return
	
	var dots = progress_dots_container.get_children()
	for i in range(dots.size()):
		var dot = dots[i] as ColorRect
		if not dot:
			continue
		
		if i < completed_count:
			# Completed steps — green
			dot.color = Color("#A3BE8C")
		elif i == current_step - 1:
			# Current step — yellow/orange
			dot.color = Color(1.0, 0.8, 0.2, 1.0)
		else:
			# Incomplete steps — gray
			dot.color = Color(0.85, 0.87, 0.91, 0.5)

# ── Display update ────────────────────────────────────────────────────────────

func _refresh_display() -> void:
	if _tutorial_skipped:
		visible = false
		return
	if not has_node("/root/SimBridge"):
		return
	var sb: Node = get_node("/root/SimBridge")

	var is_complete: bool = sb.tutorial_is_complete
	var step_num: int     = sb.tutorial_step_number
	var total: int        = 23  # Updated to 23 steps

	if is_complete:
		_show_completion_screen()
		return

	# Step counter
	if step_label:
		step_label.text = tr("ui.tutorial.step_format") % [step_num, total]
		step_label.tooltip_text = "Tutorial-stegnummer. Fullført %d av %d." % [sb.tutorial_completed_count, total]

	# Progress bar
	if progress_bar:
		progress_bar.max_value = total
		progress_bar.value     = sb.tutorial_completed_count
	
	# Update progress dots
	_update_progress_dots(step_num, sb.tutorial_completed_count)
		progress_bar.tooltip_text = "Fremdrift i tutorial: %d av %d steg fullført." % [sb.tutorial_completed_count, total]

	# Title
	if title_label:
		title_label.text = sb.tutorial_step_title

	# Description (wrap long text)
	if description_label:
		description_label.text           = sb.tutorial_description
		description_label.autowrap_mode  = TextServer.AUTOWRAP_WORD_SMART

	# Hint
	if hint_label:
		hint_label.text          = sb.tutorial_hint
		hint_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART

	# Reward label — hide if no reward for this step
	if reward_label:
		reward_label.visible = false  # rewards shown in completion animation

# ── Completion animation ──────────────────────────────────────────────────────

func _play_completion_animation(step_number: int, title: String, reward_nok: float) -> void:
	if _tween:
		_tween.kill()
	_tween = create_tween()

	# Flash the panel border green
	var style: StyleBoxFlat = get_theme_stylebox("panel") as StyleBoxFlat
	if style:
		var original_color: Color = style.border_color
		_tween.tween_property(style, "border_color", Color.GREEN, 0.15)
		_tween.tween_interval(0.5)
		_tween.tween_property(style, "border_color", original_color, 0.3)

	# Show reward toast if applicable
	if reward_label and reward_nok > 0.0:
		reward_label.text    = tr("ui.tutorial.step_complete") % [step_number, _format_nok(reward_nok)]
		reward_label.visible = true
		reward_label.modulate = Color("#A3BE8C")
		_tween.tween_interval(0.1)
		_tween.tween_property(reward_label, "modulate:a", 0.0, 2.5).set_delay(2.0)
		await _tween.finished
		reward_label.visible = false
	else:
		if reward_label:
			reward_label.text    = "✅ %s" % title
			reward_label.visible = true
			_tween.tween_interval(0.1)
			_tween.tween_property(reward_label, "modulate:a", 0.0, 2.5).set_delay(1.5)
			await _tween.finished
			reward_label.visible = false

func _show_completion_screen() -> void:
	if title_label:
		title_label.text = "🏆 Tutorial fullført!"
	if description_label:
		description_label.text = (
			"Gratulerer — du er nå en ekte oppdrettskonge!\n\n" +
			"Fri spilling er aktivert. Alle systemer er tilgjengelige.\n" +
			"Mål: 10 000 tonn årlig produksjon med Sustainability Leader-sertifisering."
		)
	if hint_label:
		hint_label.text = tr("ui.tutorial.hint_next")
	if step_label:
		step_label.text = tr("ui.tutorial.completed")
	if progress_bar:
		progress_bar.value = 23
	
	# Mark all dots as complete
	_update_progress_dots(23, 23)

# ── Minimise / expand ─────────────────────────────────────────────────────────

func _set_minimised(value: bool) -> void:
	_minimised = value
	if _tween:
		_tween.kill()
	_tween = create_tween()

	if _minimised:
		_tween.tween_property(self, "custom_minimum_size:y", 32.0, 0.2)
		if description_label: description_label.visible = false
		if hint_label:        hint_label.visible        = false
		if close_button:      close_button.text         = "▼"
	else:
		_tween.tween_property(self, "custom_minimum_size:y", 0.0, 0.2)
		if description_label: description_label.visible = true
		if hint_label:        hint_label.visible        = true
		if close_button:      close_button.text         = "✕"

# ── Helpers ───────────────────────────────────────────────────────────────────

func _format_nok(amount: float) -> String:
	if has_node("/root/SimBridge"):
		return get_node("/root/SimBridge").format_nok(amount)
	if amount >= 1_000_000.0:
		return "%.2f MNOK" % (amount / 1_000_000.0)
	elif amount >= 1_000.0:
		return "%.0f kNOK" % (amount / 1_000.0)
	return "%.0f NOK" % amount
