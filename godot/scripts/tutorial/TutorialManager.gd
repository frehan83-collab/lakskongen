extends Node

## TutorialManager - State machine and progression controller for tutorial system
## Singleton autoload that manages 23-step tutorial flow

# Enums for state machine
enum State {
	INACTIVE,    # Tutorial not started
	ACTIVE,      # Tutorial running
	PAUSED,      # Tutorial temporarily paused
	COMPLETE,    # All steps completed
	SKIPPED      # User skipped tutorial
}

# State
var current_state: State = State.INACTIVE
var current_step: int = 0
var completed_steps: Array[int] = []
var tutorial_data: Dictionary = {}
var step_definitions: Array = []

# Settings
var tutorial_enabled: bool = true
var can_skip: bool = true

# File paths
const STEPS_FILE = "res://data/tutorial_steps.json"
const PROGRESS_FILE = "user://tutorial_progress.json"

# Signals
signal step_changed(step_id: int)
signal step_completed(step_id: int)
signal tutorial_started()
signal tutorial_completed()
signal tutorial_skipped()
signal tutorial_paused()
signal tutorial_resumed()

func _ready():
	_load_tutorial_steps()
	_load_progress()
	_connect_game_signals()
	print("TutorialManager: Initialized with %d steps" % step_definitions.size())

## Load tutorial step definitions from JSON
func _load_tutorial_steps():
	var file = FileAccess.open(STEPS_FILE, FileAccess.READ)
	if file == null:
		push_error("TutorialManager: Failed to load tutorial_steps.json")
		return
	
	var json_text = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var error = json.parse(json_text)
	if error != OK:
		push_error("TutorialManager: JSON parse error: %s" % json.get_error_message())
		return
	
	var data = json.get_data()
	if data.has("steps"):
		step_definitions = data["steps"]
		print("TutorialManager: Loaded %d tutorial steps" % step_definitions.size())

## Load saved progress
func _load_progress():
	if not FileAccess.file_exists(PROGRESS_FILE):
		print("TutorialManager: No saved progress found")
		return
	
	var file = FileAccess.open(PROGRESS_FILE, FileAccess.READ)
	if file == null:
		return
	
	var json_text = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var error = json.parse(json_text)
	if error != OK:
		return
	
	tutorial_data = json.get_data()
	
	# Restore state
	if tutorial_data.has("current_step"):
		current_step = tutorial_data["current_step"]
	if tutorial_data.has("completed_steps"):
		completed_steps = Array(tutorial_data["completed_steps"])
	if tutorial_data.has("skipped") and tutorial_data["skipped"]:
		current_state = State.SKIPPED
	elif current_step > 0 and current_step <= step_definitions.size():
		current_state = State.PAUSED  # Resume on user action
	
	print("TutorialManager: Loaded progress - Step %d, %d completed" % [current_step, completed_steps.size()])

## Save current progress
func _save_progress():
	var save_data = {
		"current_step": current_step,
		"completed_steps": completed_steps,
		"skipped": current_state == State.SKIPPED,
		"started_at": tutorial_data.get("started_at", Time.get_datetime_string_from_system()),
		"last_updated": Time.get_datetime_string_from_system()
	}
	
	var json_text = JSON.stringify(save_data, "\t")
	var file = FileAccess.open(PROGRESS_FILE, FileAccess.WRITE)
	if file == null:
		push_error("TutorialManager: Failed to save progress")
		return
	
	file.store_string(json_text)
	file.close()

## Connect to GameBridge signals
func _connect_game_signals():
	# Check if GameBridge exists (C# bridge)
	if has_node("/root/GameBridge"):
		var bridge = get_node("/root/GameBridge")
		# Connect to tutorial-relevant signals
		if bridge.has_signal("OnFishFed"):
			bridge.OnFishFed.connect(_on_fish_fed)
		if bridge.has_signal("OnPenHarvested"):
			bridge.OnPenHarvested.connect(_on_pen_harvested)
		if bridge.has_signal("OnDayAdvanced"):
			bridge.OnDayAdvanced.connect(_on_day_advanced)
		print("TutorialManager: Connected to GameBridge signals")

## PUBLIC API

## Start tutorial from beginning or resume from saved step
func start_tutorial():
	if current_state == State.SKIPPED:
		print("TutorialManager: Tutorial was skipped, not restarting")
		return
	
	if current_step == 0:
		current_step = 1
		tutorial_data["started_at"] = Time.get_datetime_string_from_system()
	
	current_state = State.ACTIVE
	tutorial_started.emit()
	_show_current_step()
	_save_progress()
	print("TutorialManager: Tutorial started at step %d" % current_step)

## Resume paused tutorial
func resume_tutorial():
	if current_state == State.PAUSED:
		current_state = State.ACTIVE
		tutorial_resumed.emit()
		_show_current_step()
		print("TutorialManager: Tutorial resumed at step %d" % current_step)

## Pause tutorial
func pause_tutorial():
	if current_state == State.ACTIVE:
		current_state = State.PAUSED
		tutorial_paused.emit()
		_save_progress()

## Skip tutorial entirely
func skip_tutorial():
	if not can_skip:
		print("TutorialManager: Skipping disabled")
		return
	
	current_state = State.SKIPPED
	tutorial_skipped.emit()
	_save_progress()
	print("TutorialManager: Tutorial skipped by user")

## Advance to next step
func next_step():
	if current_state != State.ACTIVE:
		return
	
	var step = _get_step(current_step)
	if step == null:
		return
	
	# Mark current step as completed
	if not completed_steps.has(current_step):
		completed_steps.append(current_step)
		step_completed.emit(current_step)
	
	# Move to next step
	if step.has("next_step") and step["next_step"] != null:
		current_step = step["next_step"]
		step_changed.emit(current_step)
		_show_current_step()
		_save_progress()
	else:
		# Tutorial complete!
		_complete_tutorial()

## Check if step completion condition is met
func check_completion(trigger_type: String, data: Variant = null) -> bool:
	if current_state != State.ACTIVE:
		return false
	
	var step = _get_step(current_step)
	if step == null:
		return false
	
	if step["completion_trigger"] == trigger_type:
		next_step()
		return true
	
	return false

## Get current step data
func get_current_step() -> Dictionary:
	return _get_step(current_step)

## Check if tutorial is active
func is_active() -> bool:
	return current_state == State.ACTIVE

## Check if tutorial is completed
func is_completed() -> bool:
	return current_state == State.COMPLETE

## Check if tutorial was skipped
func is_skipped() -> bool:
	return current_state == State.SKIPPED

## Get progress percentage
func get_progress_percent() -> float:
	if step_definitions.size() == 0:
		return 0.0
	return (float(completed_steps.size()) / float(step_definitions.size())) * 100.0

## INTERNAL METHODS

## Get step definition by ID
func _get_step(step_id: int) -> Dictionary:
	for step in step_definitions:
		if step["id"] == step_id:
			return step
	return {}

## Show current step to UI
func _show_current_step():
	var step = _get_step(current_step)
	if step.is_empty():
		push_error("TutorialManager: Invalid step %d" % current_step)
		return
	
	step_changed.emit(current_step)
	print("TutorialManager: Showing step %d - %s" % [current_step, step["title"]])

## Complete tutorial
func _complete_tutorial():
	current_state = State.COMPLETE
	tutorial_completed.emit()
	_save_progress()
	print("TutorialManager: Tutorial completed! %d steps finished" % completed_steps.size())

## SIGNAL HANDLERS (from GameBridge)

func _on_fish_fed(pen_id: int):
	check_completion("fish_fed", pen_id)

func _on_pen_harvested(pen_id: int):
	check_completion("pen_harvested", pen_id)

func _on_day_advanced(day: int):
	check_completion("days_passed", day)

## UI Event handlers (called by TutorialOverlay)
func on_continue_clicked():
	check_completion("button_clicked")

func on_skip_clicked():
	skip_tutorial()

func on_pen_clicked(pen_id: int):
	check_completion("pen_clicked", pen_id)

func on_action_completed(action_type: String, data: Variant = null):
	check_completion(action_type, data)
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
