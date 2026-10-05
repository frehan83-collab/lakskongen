extends Node

## Test script for TutorialManager
## Run this in Godot editor to verify tutorial system works

func _ready():
	print("\n=== Tutorial System Test ===\n")
	
	# Test 1: Load tutorial steps
	print("Test 1: Loading tutorial steps...")
	var manager = TutorialManager.new()
	manager._load_tutorial_steps()
	
	if manager.step_definitions.size() == 23:
		print("✅ PASS: Loaded 23 steps")
	else:
		print("❌ FAIL: Expected 23 steps, got %d" % manager.step_definitions.size())
	
	# Test 2: Verify step format
	print("\nTest 2: Verifying step format...")
	var step1 = manager.step_definitions[0]
	if step1.has("id") and step1.has("title") and step1.has("text"):
		print("✅ PASS: Step format correct")
		print("   Step 1: %s" % step1["title"])
	else:
		print("❌ FAIL: Step format incorrect")
	
	# Test 3: State transitions
	print("\nTest 3: Testing state transitions...")
	manager.current_step = 0
	manager.start_tutorial()
	
	if manager.is_active() and manager.current_step == 1:
		print("✅ PASS: Tutorial started at step 1")
	else:
		print("❌ FAIL: Tutorial start failed")
	
	# Test 4: Step progression
	print("\nTest 4: Testing step progression...")
	manager.next_step()
	
	if manager.current_step == 2:
		print("✅ PASS: Advanced to step 2")
	else:
		print("❌ FAIL: Step progression failed")
	
	# Test 5: Skip functionality
	print("\nTest 5: Testing skip...")
	manager.skip_tutorial()
	
	if manager.is_skipped():
		print("✅ PASS: Tutorial skipped")
	else:
		print("❌ FAIL: Skip failed")
	
	# Test 6: Completion
	print("\nTest 6: Testing completion...")
	manager.current_state = TutorialManager.State.INACTIVE
	manager.current_step = 0
	manager.start_tutorial()
	
	# Fast-forward to last step
	for i in range(22):
		manager.next_step()
	
	# One more to complete
	manager.next_step()
	
	if manager.is_completed():
		print("✅ PASS: Tutorial completed")
	else:
		print("❌ FAIL: Completion failed")
	
	# Test 7: Progress counter
	print("\nTest 7: Testing progress counter...")
	manager.current_step = 5
	var progress = manager.get_progress()
	
	if progress == "5/23":
		print("✅ PASS: Progress counter correct: %s" % progress)
	else:
		print("❌ FAIL: Expected '5/23', got '%s'" % progress)
	
	print("\n=== Test Complete ===\n")
	manager.queue_free()
