extends Node

## TutorialTests - Automated test suite for tutorial system
## Run from command line: godot --script res://scripts/tutorial/TutorialTests.gd

var tests_passed = 0
var tests_failed = 0
var test_results = []

func _ready():
	print("\n=== TUTORIAL SYSTEM TEST SUITE ===\n")
	
	# Wait for autoloads
	await get_tree().create_timer(0.5).timeout
	
	# Run tests
	await test_json_loading()
	await test_state_machine()
	await test_progress_save_load()
	await test_step_progression()
	await test_skip_functionality()
	await test_completion_triggers()
	await test_highlight_system()
	
	# Print results
	print_results()
	
	# Exit
	get_tree().quit()

## Test: JSON Loading
func test_json_loading():
	print("TEST: JSON Loading")
	
	var file = FileAccess.open("res://data/tutorial_steps.json", FileAccess.READ)
	if file == null:
		fail("JSON file not found")
		return
	
	var json_text = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var error = json.parse(json_text)
	if error != OK:
		fail("JSON parse error: " + json.get_error_message())
		return
	
	var data = json.get_data()
	if not data.has("steps"):
		fail("JSON missing 'steps' key")
		return
	
	var steps = data["steps"]
	if steps.size() != 23:
		fail("Expected 23 steps, got " + str(steps.size()))
		return
	
	# Validate first step
	var first_step = steps[0]
	if not first_step.has("id") or first_step["id"] != 1:
		fail("First step has invalid ID")
		return
	
	if not first_step.has("title") or first_step["title"] == "":
		fail("First step missing title")
		return
	
	if not first_step.has("completion_trigger"):
		fail("First step missing completion_trigger")
		return
	
	pass_test("JSON loaded successfully with 23 valid steps")

## Test: State Machine
func test_state_machine():
	print("\nTEST: State Machine")
	
	if not has_node("/root/TutorialManager"):
		fail("TutorialManager autoload not found")
		return
	
	var manager = get_node("/root/TutorialManager")
	
	# Check initial state
	if manager.current_state != manager.State.INACTIVE:
		fail("Initial state should be INACTIVE")
		return
	
	# Start tutorial
	manager.start_tutorial()
	await get_tree().create_timer(0.1).timeout
	
	if manager.current_state != manager.State.ACTIVE:
		fail("State should be ACTIVE after start")
		return
	
	# Pause tutorial
	manager.pause_tutorial()
	if manager.current_state != manager.State.PAUSED:
		fail("State should be PAUSED after pause")
		return
	
	# Resume tutorial
	manager.resume_tutorial()
	if manager.current_state != manager.State.ACTIVE:
		fail("State should be ACTIVE after resume")
		return
	
	# Skip tutorial
	manager.skip_tutorial()
	if manager.current_state != manager.State.SKIPPED:
		fail("State should be SKIPPED after skip")
		return
	
	# Reset for other tests
	manager.current_state = manager.State.INACTIVE
	manager.current_step = 0
	
	pass_test("State machine transitions work correctly")

## Test: Progress Save/Load
func test_progress_save_load():
	print("\nTEST: Progress Save/Load")
	
	var manager = get_node("/root/TutorialManager")
	
	# Set test progress
	manager.current_step = 10
	manager.completed_steps = [1, 2, 3, 4, 5, 6, 7, 8, 9]
	manager._save_progress()
	
	# Verify file exists
	if not FileAccess.file_exists("user://tutorial_progress.json"):
		fail("Progress file not created")
		return
	
	# Clear state
	manager.current_step = 0
	manager.completed_steps.clear()
	
	# Reload
	manager._load_progress()
	
	if manager.current_step != 10:
		fail("Current step not restored (expected 10, got " + str(manager.current_step) + ")")
		return
	
	if manager.completed_steps.size() != 9:
		fail("Completed steps not restored (expected 9, got " + str(manager.completed_steps.size()) + ")")
		return
	
	# Cleanup
	DirAccess.remove_absolute("user://tutorial_progress.json")
	manager.current_step = 0
	manager.completed_steps.clear()
	
	pass_test("Progress save/load works correctly")

## Test: Step Progression
func test_step_progression():
	print("\nTEST: Step Progression")
	
	var manager = get_node("/root/TutorialManager")
	
	# Start at step 1
	manager.current_step = 1
	manager.current_state = manager.State.ACTIVE
	
	# Get current step
	var step = manager.get_current_step()
	if step.is_empty() or step["id"] != 1:
		fail("get_current_step() returned invalid data")
		return
	
	# Advance to next step
	var initial_step = manager.current_step
	manager.next_step()
	
	if manager.current_step != initial_step + 1:
		fail("next_step() did not advance step")
		return
	
	if not manager.completed_steps.has(initial_step):
		fail("Completed step not recorded")
		return
	
	# Test completion check
	manager.current_step = 5  # Step 5 requires "fish_fed"
	var result = manager.check_completion("fish_fed", 1)
	
	if not result:
		fail("check_completion() should return true for valid trigger")
		return
	
	if manager.current_step != 6:
		fail("check_completion() should advance step")
		return
	
	# Reset
	manager.current_step = 0
	manager.completed_steps.clear()
	manager.current_state = manager.State.INACTIVE
	
	pass_test("Step progression works correctly")

## Test: Skip Functionality
func test_skip_functionality():
	print("\nTEST: Skip Functionality")
	
	var manager = get_node("/root/TutorialManager")
	
	# Enable skipping
	manager.can_skip = true
	manager.current_state = manager.State.ACTIVE
	
	# Skip tutorial
	manager.skip_tutorial()
	
	if manager.current_state != manager.State.SKIPPED:
		fail("Tutorial should be in SKIPPED state")
		return
	
	# Try to start skipped tutorial
	manager.start_tutorial()
	await get_tree().create_timer(0.1).timeout
	
	if manager.current_state == manager.State.ACTIVE:
		fail("Skipped tutorial should not restart")
		return
	
	# Test skip prevention
	manager.can_skip = false
	manager.current_state = manager.State.ACTIVE
	manager.skip_tutorial()
	
	if manager.current_state == manager.State.SKIPPED:
		fail("Tutorial should not skip when can_skip is false")
		return
	
	# Reset
	manager.can_skip = true
	manager.current_state = manager.State.INACTIVE
	
	pass_test("Skip functionality works correctly")

## Test: Completion Triggers
func test_completion_triggers():
	print("\nTEST: Completion Triggers")
	
	var manager = get_node("/root/TutorialManager")
	
	# Test button_clicked trigger
	manager.current_step = 1  # Step 1 uses button_clicked
	manager.current_state = manager.State.ACTIVE
	
	manager.on_continue_clicked()
	
	if manager.current_step != 2:
		fail("button_clicked trigger did not advance step")
		return
	
	# Test fish_fed trigger
	manager.current_step = 5  # Step 5 uses fish_fed
	
	manager._on_fish_fed(1)
	
	if manager.current_step != 6:
		fail("fish_fed trigger did not advance step")
		return
	
	# Test pen_harvested trigger
	manager.current_step = 10  # Step 10 uses pen_harvested
	
	manager._on_pen_harvested(1)
	
	if manager.current_step != 11:
		fail("pen_harvested trigger did not advance step")
		return
	
	# Reset
	manager.current_step = 0
	manager.current_state = manager.State.INACTIVE
	
	pass_test("Completion triggers work correctly")

## Test: Highlight System
func test_highlight_system():
	print("\nTEST: Highlight System")
	
	if not has_node("/root/TutorialHighlight"):
		fail("TutorialHighlight autoload not found")
		return
	
	var highlight = get_node("/root/TutorialHighlight")
	
	# Check initial state
	if highlight.is_active():
		fail("Highlight should be inactive initially")
		return
	
	# Create test element
	var test_element = Control.new()
	test_element.name = "TestElement"
	test_element.position = Vector2(100, 100)
	test_element.size = Vector2(200, 100)
	add_child(test_element)
	
	await get_tree().create_timer(0.1).timeout
	
	# Highlight element
	highlight.highlight_element(test_element, "rectangle")
	
	if not highlight.is_active():
		fail("Highlight should be active after highlight_element()")
		test_element.queue_free()
		return
	
	if highlight.target_element != test_element:
		fail("Target element not set correctly")
		test_element.queue_free()
		return
	
	# Test hiding
	highlight.hide_highlight()
	
	if highlight.is_active():
		fail("Highlight should be inactive after hide_highlight()")
		test_element.queue_free()
		return
	
	# Cleanup
	test_element.queue_free()
	
	pass_test("Highlight system works correctly")

## Helper: Pass test
func pass_test(message: String):
	tests_passed += 1
	test_results.append("✓ PASS: " + message)
	print("  ✓ PASS: " + message)

## Helper: Fail test
func fail(message: String):
	tests_failed += 1
	test_results.append("✗ FAIL: " + message)
	print("  ✗ FAIL: " + message)

## Print final results
func print_results():
	print("\n=== TEST RESULTS ===")
	print("Passed: " + str(tests_passed))
	print("Failed: " + str(tests_failed))
	print("Total:  " + str(tests_passed + tests_failed))
	
	if tests_failed == 0:
		print("\n✓ ALL TESTS PASSED!")
	else:
		print("\n✗ SOME TESTS FAILED")
		print("\nFailed tests:")
		for result in test_results:
			if result.begins_with("✗"):
				print("  " + result)
	
	print("\n=== END TEST SUITE ===\n")
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
