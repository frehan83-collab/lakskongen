extends Node

## Test script for scenario system
## Run from Godot editor console or attach to test scene

func _ready():
	print("=== SCENARIO SYSTEM TEST SUITE ===")
	test_scenario_loading()
	test_scenario_data_structure()
	test_goal_tracking()
	test_scenario_application()
	print("=== ALL TESTS COMPLETE ===")

# ============================================================================
# TEST: Scenario Loading
# ============================================================================

func test_scenario_loading():
	print("\n[TEST] Scenario Loading")
	
	var scenario_manager = get_node_or_null("/root/ScenarioManager")
	if not scenario_manager:
		print("❌ FAIL: ScenarioManager not found in autoload")
		return
	
	var scenarios = scenario_manager.get_scenarios()
	
	if scenarios.size() != 5:
		print("❌ FAIL: Expected 5 scenarios, got %d" % scenarios.size())
		return
	
	print("✅ PASS: Loaded 5 scenarios")
	
	# Verify each scenario has required fields
	var required_fields = ["id", "name", "difficulty", "starting_balance", "goal"]
	for scenario in scenarios:
		for field in required_fields:
			if not scenario.has(field):
				print("❌ FAIL: Scenario '%s' missing field '%s'" % [scenario.get("id", "unknown"), field])
				return
	
	print("✅ PASS: All scenarios have required fields")

# ============================================================================
# TEST: Scenario Data Structure
# ============================================================================

func test_scenario_data_structure():
	print("\n[TEST] Scenario Data Structure")
	
	var scenario_manager = get_node_or_null("/root/ScenarioManager")
	var scenarios = scenario_manager.get_scenarios()
	
	# Test Small Family Farm
	var small_farm = scenario_manager.get_scenario("small_farm")
	if small_farm.is_empty():
		print("❌ FAIL: Could not find 'small_farm' scenario")
		return
	
	if small_farm.get("starting_balance") != 5000000:
		print("❌ FAIL: Small farm starting balance incorrect")
		return
	
	if small_farm.get("difficulty") != "Easy":
		print("❌ FAIL: Small farm difficulty incorrect")
		return
	
	print("✅ PASS: 'small_farm' data structure correct")
	
	# Test Industrial Giant
	var industrial = scenario_manager.get_scenario("industrial_giant")
	if industrial.get("starting_sites", []).size() != 3:
		print("❌ FAIL: Industrial giant should have 3 starting sites")
		return
	
	print("✅ PASS: 'industrial_giant' data structure correct")
	
	# Test Organic Specialist restrictions
	var organic = scenario_manager.get_scenario("organic_specialist")
	var restrictions = organic.get("restrictions", [])
	if not ("no_chemicals" in restrictions):
		print("❌ FAIL: Organic specialist missing 'no_chemicals' restriction")
		return
	
	print("✅ PASS: 'organic_specialist' restrictions correct")
	
	# Test Research Station buildings
	var research = scenario_manager.get_scenario("research_station")
	var buildings = research.get("starting_buildings", [])
	if not ("hatchery" in buildings):
		print("❌ FAIL: Research station missing 'hatchery' building")
		return
	
	print("✅ PASS: 'research_station' buildings correct")
	
	# Test Inheritance debt
	var inheritance = scenario_manager.get_scenario("inheritance")
	if inheritance.get("starting_debt") != 30000000:
		print("❌ FAIL: Inheritance debt incorrect")
		return
	
	print("✅ PASS: 'inheritance' debt correct")

# ============================================================================
# TEST: Goal Tracking Logic
# ============================================================================

func test_goal_tracking():
	print("\n[TEST] Goal Tracking Logic")
	
	var scenario_manager = get_node_or_null("/root/ScenarioManager")
	
	# Apply small farm scenario
	scenario_manager.apply_scenario("small_farm")
	
	# Simulate game state updates
	var test_state_1 = {
		"Day": 365,  # 1 year
		"Balance": 10000000,
		"total_revenue": 10000000,
		"UnlockedResearchIds": []
	}
	
	scenario_manager.update_goal_progress(test_state_1)
	var progress_1 = scenario_manager.get_goal_progress()
	
	# Should be 10M / 50M = 0.2 (20%)
	if abs(progress_1 - 0.2) > 0.01:
		print("❌ FAIL: Progress calculation incorrect (expected 0.2, got %.2f)" % progress_1)
		return
	
	print("✅ PASS: Revenue goal progress tracking works")
	
	# Test goal achievement
	var test_state_2 = {
		"Day": 1000,
		"Balance": 60000000,
		"total_revenue": 55000000,
		"UnlockedResearchIds": []
	}
	
	scenario_manager.update_goal_progress(test_state_2)
	
	if not scenario_manager.is_goal_achieved():
		print("❌ FAIL: Goal should be achieved at 55M revenue")
		return
	
	print("✅ PASS: Goal achievement detection works")
	
	# Reset and test time limit failure
	scenario_manager.apply_scenario("small_farm")
	
	var test_state_3 = {
		"Day": 1900,  # Over 5 years (1825 days)
		"Balance": 20000000,
		"total_revenue": 20000000,
		"UnlockedResearchIds": []
	}
	
	scenario_manager.update_goal_progress(test_state_3)
	
	if not scenario_manager.is_goal_failed():
		print("❌ FAIL: Goal should fail after time limit")
		return
	
	print("✅ PASS: Time limit failure detection works")

# ============================================================================
# TEST: Scenario Application
# ============================================================================

func test_scenario_application():
	print("\n[TEST] Scenario Application")
	
	var scenario_manager = get_node_or_null("/root/ScenarioManager")
	
	# Test applying each scenario
	var scenario_ids = ["small_farm", "industrial_giant", "organic_specialist", "research_station", "inheritance"]
	
	for scenario_id in scenario_ids:
		var success = scenario_manager.apply_scenario(scenario_id)
		if not success:
			print("❌ FAIL: Could not apply scenario '%s'" % scenario_id)
			return
		
		var current = scenario_manager.get_current_scenario()
		if current.get("id", "") != scenario_id:
			print("❌ FAIL: Current scenario mismatch after applying '%s'" % scenario_id)
			return
	
	print("✅ PASS: All scenarios can be applied")

# ============================================================================
# MANUAL TEST HELPERS
# ============================================================================

## Print all scenarios (for manual inspection)
func print_all_scenarios():
	var scenario_manager = get_node_or_null("/root/ScenarioManager")
	if not scenario_manager:
		return
	
	var scenarios = scenario_manager.get_scenarios()
	print("\n=== ALL SCENARIOS ===")
	for scenario in scenarios:
		print("\n%s (%s)" % [scenario.get("name"), scenario.get("difficulty")])
		print("  ID: %s" % scenario.get("id"))
		print("  Balance: %s NOK" % scenario.get("starting_balance"))
		print("  Sites: %d" % scenario.get("starting_sites", []).size())
		print("  Goal: %s" % scenario.get("goal", {}).get("description", ""))

## Get scenario summary (useful for debugging)
func get_scenario_summary(scenario_id: String) -> String:
	var scenario_manager = get_node_or_null("/root/ScenarioManager")
	var scenario = scenario_manager.get_scenario(scenario_id)
	
	if scenario.is_empty():
		return "Scenario not found"
	
	var summary = "%s (%s)\n" % [scenario.get("name"), scenario.get("difficulty")]
	summary += "Balance: %.1f M NOK\n" % (scenario.get("starting_balance") / 1000000.0)
	summary += "Debt: %.1f M NOK\n" % (scenario.get("starting_debt", 0) / 1000000.0)
	summary += "Sites: %d\n" % scenario.get("starting_sites", []).size()
	summary += "Goal: %s\n" % scenario.get("goal", {}).get("description", "")
	
	return summary
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
