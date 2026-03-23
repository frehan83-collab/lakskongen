extends Node
## test_achievements.gd - Achievement system test suite
## Run this scene to test achievement functionality

func _ready():
	print("\n========== ACHIEVEMENT SYSTEM TESTS ==========\n")
	
	# Wait for autoloads to be ready
	await get_tree().process_frame
	
	if not AchievementManager:
		push_error("AchievementManager not found! Check autoload configuration.")
		return
	
	# Run tests
	test_load_achievements()
	test_unlock_achievement()
	test_progress_tracking()
	test_check_achievements()
	test_save_load()
	test_queries()
	
	print("\n========== TESTS COMPLETE ==========\n")

# ============================================================================
# TEST CASES
# ============================================================================

func test_load_achievements():
	print("TEST: Load Achievements")
	var achievements = AchievementManager.get_all_achievements()
	print("  Loaded %d achievements" % achievements.size())
	assert(achievements.size() >= 38, "Should have at least 38 achievements")
	print("  ✓ PASSED\n")

func test_unlock_achievement():
	print("TEST: Unlock Achievement")
	
	# Reset first
	AchievementManager.reset_all_achievements()
	
	# Unlock
	var success = AchievementManager.unlock("first_harvest")
	assert(success, "Should unlock successfully")
	
	# Check unlocked
	var is_unlocked = AchievementManager.is_unlocked("first_harvest")
	assert(is_unlocked, "Should be unlocked")
	
	# Try unlock again (should fail)
	var second_unlock = AchievementManager.unlock("first_harvest")
	assert(not second_unlock, "Should not unlock twice")
	
	print("  ✓ PASSED\n")

func test_progress_tracking():
	print("TEST: Progress Tracking")
	
	AchievementManager.reset_all_achievements()
	
	# Update progress
	AchievementManager.update_progress("harvest_veteran", 5)
	
	var progress = AchievementManager.get_progress("harvest_veteran")
	var progress_value = AchievementManager.get_progress_value("harvest_veteran")
	
	print("  Progress: %.1f%% (%d/10)" % [progress * 100, progress_value])
	assert(progress_value == 5, "Progress value should be 5")
	assert(abs(progress - 0.5) < 0.01, "Progress should be 50%")
	
	# Update to completion
	AchievementManager.update_progress("harvest_veteran", 10)
	
	var is_unlocked = AchievementManager.is_unlocked("harvest_veteran")
	assert(is_unlocked, "Should auto-unlock at 100%")
	
	print("  ✓ PASSED\n")

func test_check_achievements():
	print("TEST: Check Achievements from Game State")
	
	AchievementManager.reset_all_achievements()
	
	# Simulate game state
	var game_state = {
		"harvest_count": 1,
		"balance": 1500000.0,
		"total_fish_produced": 15000,
		"reputation": 60.0,
		"research_unlocked_count": 7,
		"days_elapsed": 3650, # 10 years
	}
	
	AchievementManager.check_achievements(game_state)
	
	# Wait for signals to process
	await get_tree().process_frame
	
	# Check expected unlocks
	assert(AchievementManager.is_unlocked("first_harvest"), "Should unlock first_harvest")
	assert(AchievementManager.is_unlocked("millionaire"), "Should unlock millionaire")
	assert(AchievementManager.is_unlocked("small_producer"), "Should unlock small_producer")
	assert(AchievementManager.is_unlocked("reputable"), "Should unlock reputable")
	assert(AchievementManager.is_unlocked("tech_novice"), "Should unlock tech_novice")
	assert(AchievementManager.is_unlocked("decade_survivor"), "Should unlock decade_survivor")
	
	var unlocked = AchievementManager.get_unlocked_ids()
	print("  Unlocked %d achievements from game state" % unlocked.size())
	
	print("  ✓ PASSED\n")

func test_save_load():
	print("TEST: Save/Load Persistence")
	
	# Clear and unlock some achievements
	AchievementManager.reset_all_achievements()
	AchievementManager.unlock("first_harvest")
	AchievementManager.unlock("millionaire")
	
	var before_count = AchievementManager.get_unlocked_ids().size()
	print("  Unlocked before save: %d" % before_count)
	
	# Force save
	AchievementManager._save_unlocked_achievements()
	
	# Simulate reload
	AchievementManager.unlocked_achievements.clear()
	AchievementManager._load_unlocked_achievements()
	
	var after_count = AchievementManager.get_unlocked_ids().size()
	print("  Unlocked after load: %d" % after_count)
	
	assert(before_count == after_count, "Should persist across save/load")
	assert(AchievementManager.is_unlocked("first_harvest"), "first_harvest should persist")
	assert(AchievementManager.is_unlocked("millionaire"), "millionaire should persist")
	
	print("  ✓ PASSED\n")

func test_queries():
	print("TEST: Query Methods")
	
	AchievementManager.reset_all_achievements()
	AchievementManager.unlock("first_harvest")
	AchievementManager.unlock("millionaire")
	
	# Test completion percentage
	var completion = AchievementManager.get_completion_percentage()
	var total = AchievementManager.get_all_achievements().size()
	var expected_completion = (2.0 / total) * 100.0
	
	print("  Completion: %.1f%% (2/%d)" % [completion, total])
	assert(abs(completion - expected_completion) < 0.1, "Completion percentage should match")
	
	# Test get_achievement
	var achievement = AchievementManager.get_achievement("millionaire")
	assert(achievement.get("name") == "Millionaire", "Should retrieve correct achievement")
	
	# Test unlock time
	var unlock_time = AchievementManager.get_unlock_time("first_harvest")
	assert(unlock_time > 0, "Should have valid unlock timestamp")
	
	print("  ✓ PASSED\n")

# ============================================================================
# DEMO: Achievement unlocking
# ============================================================================

func demo_unlock_sequence():
	print("\n========== DEMO: Achievement Unlock Sequence ==========\n")
	
	AchievementManager.reset_all_achievements()
	
	# Connect to signal
	AchievementManager.achievement_unlocked.connect(_on_demo_achievement_unlocked)
	
	# Simulate game progression
	var demo_states = [
		{"harvest_count": 1, "balance": 100000.0},
		{"harvest_count": 10, "balance": 1200000.0},
		{"harvest_count": 10, "balance": 1200000.0, "total_fish_produced": 12000},
		{"harvest_count": 10, "balance": 1200000.0, "total_fish_produced": 12000, "reputation": 55.0},
	]
	
	for i in range(demo_states.size()):
		print("Game State %d:" % (i + 1))
		AchievementManager.check_achievements(demo_states[i])
		await get_tree().create_timer(0.5).timeout
	
	print("\n========== DEMO COMPLETE ==========\n")

func _on_demo_achievement_unlocked(achievement_id: String, achievement_data: Dictionary):
	print("  🏆 UNLOCKED: %s - %s" % [achievement_data.get("name"), achievement_data.get("description")])
