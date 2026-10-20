extends CanvasLayer

## TutorialOverlay - LEGACY UI controller for the retired GDScript click-through
## tutorial. The active onboarding UI is `scripts/ui/TutorialUI.gd`, driven by
## the C# TutorialSystem via SimBridge. This overlay is no longer instanced in
## any scene; it is kept parse-safe so legacy `.tscn` references do not break.

@onready var step_label: Label = %StepLabel
@onready var title_label: Label = %TitleLabel
@onready var text_label: Label = %TextLabel
@onready var next_button: Button = %NextButton
@onready var skip_button: Button = %SkipButton

var tutorial_manager: Node = null

func _ready():
	# Get TutorialManager singleton (now a deprecated no-op).
	tutorial_manager = get_node_or_null("/root/TutorialManager")
	if tutorial_manager == null:
		push_warning("TutorialOverlay: TutorialManager not found — overlay disabled.")
		hide()
		return

	# Connect signals (TutorialManager keeps them declared for compatibility).
	if tutorial_manager.has_signal("tutorial_started"):
		tutorial_manager.tutorial_started.connect(_on_tutorial_started)
	if tutorial_manager.has_signal("step_changed"):
		tutorial_manager.step_changed.connect(_on_step_changed)
	if tutorial_manager.has_signal("tutorial_completed"):
		tutorial_manager.tutorial_completed.connect(_on_tutorial_completed)
	if tutorial_manager.has_signal("tutorial_skipped"):
		tutorial_manager.tutorial_skipped.connect(_on_tutorial_skipped)

	# Connect button signals
	if next_button:
		next_button.pressed.connect(_on_next_pressed)
	if skip_button:
		skip_button.pressed.connect(_on_skip_pressed)

	# Hide initially
	hide()
	print("TutorialOverlay: Ready (legacy overlay).")

## Show overlay with current step
func _on_tutorial_started():
	show()
	print("TutorialOverlay: Tutorial started, showing overlay")

## Update UI when step changes. NOTE: `step_changed` emits a single arg.
func _on_step_changed(step_id: int):
	if not visible:
		show()

	var step_data: Dictionary = {}
	if tutorial_manager and tutorial_manager.has_method("get_current_step"):
		step_data = tutorial_manager.get_current_step()

	# Progress counter — use the API that actually exists.
	if step_label:
		var percent: float = 0.0
		if tutorial_manager and tutorial_manager.has_method("get_progress_percent"):
			percent = tutorial_manager.get_progress_percent()
		step_label.text = "Step %d (%.0f%%)" % [step_id, percent]

	if title_label:
		title_label.text = step_data.get("title", "Untitled")
	if text_label:
		text_label.text = step_data.get("text", "")

	# Button text for the last step — derive total from the manager, no
	# missing `total_steps` property.
	var total_steps: int = 0
	if tutorial_manager:
		var defs: Variant = tutorial_manager.get("step_definitions")
		if defs != null and defs is Array:
			total_steps = defs.size()
	if next_button:
		next_button.text = "Finish" if (total_steps > 0 and step_id >= total_steps) else "Next"

	print("TutorialOverlay: Updated to step %d - %s" % [step_id, step_data.get("title", "")])

## Hide overlay when tutorial completes
func _on_tutorial_completed():
	hide()
	print("TutorialOverlay: Tutorial completed, hiding overlay")

## Hide overlay when tutorial is skipped
func _on_tutorial_skipped():
	hide()
	print("TutorialOverlay: Tutorial skipped, hiding overlay")

## Handle Next button press
func _on_next_pressed():
	if tutorial_manager and tutorial_manager.has_method("next_step"):
		tutorial_manager.next_step()

## Handle Skip button press
func _on_skip_pressed():
	# Show confirmation dialog
	var confirm = ConfirmationDialog.new()
	add_child(confirm)
	confirm.dialog_text = "Are you sure you want to skip the tutorial? You can always restart it from the menu."
	confirm.confirmed.connect(func():
		if tutorial_manager and tutorial_manager.has_method("skip_tutorial"):
			tutorial_manager.skip_tutorial()
		confirm.queue_free()
	)
	confirm.canceled.connect(func(): confirm.queue_free())
	confirm.popup_centered()
