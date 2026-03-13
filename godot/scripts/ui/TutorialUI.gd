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
@export var title_label: Label
@export var description_label: Label
@export var hint_label: Label
@export var reward_label: Label

# ── Internal ──────────────────────────────────────────────────────────────────

var _tween: Tween = null
var _minimised: bool = false

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

	_refresh_display()

# ── Signal handlers ───────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_refresh_display()

func _on_step_completed(step_number: int, title: String, reward_nok: float) -> void:
	_play_completion_animation(step_number, title, reward_nok)
	# Re-expand if the player had minimised the panel
	if _minimised:
		_set_minimised(false)

func _on_close_pressed() -> void:
	_set_minimised(!_minimised)

# ── Display update ────────────────────────────────────────────────────────────

func _refresh_display() -> void:
	if not has_node("/root/SimBridge"):
		return
	var sb: Node = get_node("/root/SimBridge")

	var is_complete: bool = sb.tutorial_is_complete
	var step_num: int     = sb.tutorial_step_number
	var total: int        = 12

	if is_complete:
		_show_completion_screen()
		return

	# Step counter
	if step_label:
		step_label.text = "Steg %d / %d" % [step_num, total]

	# Progress bar
	if progress_bar:
		progress_bar.max_value = total
		progress_bar.value     = sb.tutorial_completed_count

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
		reward_label.text    = "✅ Steg %d fullført!  +%s NOK" % [step_number, _format_nok(reward_nok)]
		reward_label.visible = true
		reward_label.modulate = Color(0.2, 1.0, 0.4, 1.0)
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
		hint_label.text = "Utforsk Expansion, Genetics og Politics for å vokse videre."
	if step_label:
		step_label.text = "Fullført ✓"
	if progress_bar:
		progress_bar.value = 12

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
