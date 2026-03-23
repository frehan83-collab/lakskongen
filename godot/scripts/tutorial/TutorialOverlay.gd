extends CanvasLayer

## TutorialOverlay - UI controller for tutorial display
## Shows tutorial steps with title, text, progress counter, and navigation buttons

@onready var step_label: Label = %StepLabel
@onready var title_label: Label = %TitleLabel
@onready var text_label: Label = %TextLabel
@onready var next_button: Button = %NextButton
@onready var skip_button: Button = %SkipButton

var tutorial_manager: Node = null

func _ready():
	# Get TutorialManager singleton
	tutorial_manager = get_node_or_null("/root/TutorialManager")
	if tutorial_manager == null:
		push_error("TutorialOverlay: TutorialManager not found! Make sure it's registered as autoload.")
		return
	
	# Connect signals
	tutorial_manager.tutorial_started.connect(_on_tutorial_started)
	tutorial_manager.step_changed.connect(_on_step_changed)
	tutorial_manager.tutorial_completed.connect(_on_tutorial_completed)
	tutorial_manager.tutorial_skipped.connect(_on_tutorial_skipped)
	
	# Connect button signals
	next_button.pressed.connect(_on_next_pressed)
	skip_button.pressed.connect(_on_skip_pressed)
	
	# Hide initially
	hide()
	print("TutorialOverlay: Ready and connected to TutorialManager")

## Show overlay with current step
func _on_tutorial_started():
	show()
	print("TutorialOverlay: Tutorial started, showing overlay")

## Update UI when step changes
func _on_step_changed(step_id: int, step_data: Dictionary):
	if not visible:
		show()
	
	# Update progress counter
	var progress = tutorial_manager.get_progress()
	step_label.text = "Step %s" % progress
	
	# Update title
	title_label.text = step_data.get("title", "Untitled")
	
	# Update text
	text_label.text = step_data.get("text", "")
	
	# Update button text for last step
	var total_steps = tutorial_manager.total_steps
	if step_id == total_steps:
		next_button.text = "Finish"
	else:
		next_button.text = "Next"
	
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
	if tutorial_manager:
		tutorial_manager.next_step()

## Handle Skip button press
func _on_skip_pressed():
	# Show confirmation dialog
	var confirm = ConfirmationDialog.new()
	add_child(confirm)
	confirm.dialog_text = "Are you sure you want to skip the tutorial? You can always restart it from the menu."
	confirm.confirmed.connect(func(): 
		if tutorial_manager:
			tutorial_manager.skip_tutorial()
		confirm.queue_free()
	)
	confirm.canceled.connect(func(): confirm.queue_free())
	confirm.popup_centered()
