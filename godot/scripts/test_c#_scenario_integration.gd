extends Node

## Test C# GameBridge scenario integration
## Run from Godot editor console or attach to test scene

func _ready():
	print("=== C# SCENARIO INTEGRATION TEST ===")
	test_apply_scenario()
	test_game_state_tracking()
	print("=== C# INTEGRATION TEST COMPLETE ===")

# ============================================================================
# TEST: ApplyScenario C# Method
# ============================================================================

func test_apply_scenario():
	print("\n[TEST] C# ApplyScenario Method")
	
	# Check if GameBridge exists
	if not GameBridge:
		print("❌ FAIL: GameBridge not found in autoload")
		return
	
	# Load scenario JSON
	var scenario_manager = get_node_or_null("/root/ScenarioManager")
	if not scenario_manager:
		print("❌ FAIL: ScenarioManager not found")
		return
	
	var scenario = scenario_manager.get_scenario("small_farm")
	if scenario.is_empty():
		print("❌ FAIL: Could not load small_farm scenario")
		return
	
	# Convert to JSON
	var scenario_json = JSON.stringify(scenario)
	print("Scenario JSON length: %d characters" % scenario_json.length())
	
	# Call C# ApplyScenario
	print("Calling GameBridge.ApplyScenario()...")
	GameBridge.ApplyScenario(scenario_json)
	
	print("✅ PASS: ApplyScenario called without errors")

# ============================================================================
# TEST: GetGameStateForGoalTracking C# Method
# ============================================================================

func test_game_state_tracking():
	print("\n[TEST] C# GetGameStateForGoalTracking Method")
	
	if not GameBridge:
		print("❌ FAIL: GameBridge not found")
		return
	
	# Call C# GetGameStateForGoalTracking
	print("Calling GameBridge.GetGameStateForGoalTracking()...")
	GameBridge.GetGameStateForGoalTracking()
	
	print("✅ PASS: GetGameStateForGoalTracking called without errors")

# ============================================================================
# SIGNAL HANDLERS (for integration testing)
# ============================================================================

func _on_scenario_applied(scenario_id: String):
	print("✅ Signal Received: ScenarioApplied(%s)" % scenario_id)

func _on_game_state_snapshot(state_json: String):
	print("✅ Signal Received: GameStateSnapshot")
	print("  JSON: %s" % state_json)
	
	# Parse and validate
	var json = JSON.new()
	var parse_result = json.parse(state_json)
	
	if parse_result != OK:
		print("❌ FAIL: Could not parse game state JSON")
		return
	
	var state = json.data
	
	# Check required fields
	var required_fields = ["Day", "Balance", "total_revenue", "is_public", "reputation", "UnlockedResearchIds", "debt"]
	for field in required_fields:
		if not state.has(field):
			print("❌ FAIL: Game state missing field '%s'" % field)
			return
	
	print("✅ PASS: Game state JSON structure valid")

# ============================================================================
# MANUAL TEST FUNCTIONS
# ============================================================================

## Test all 5 scenarios
func test_all_scenarios():
	print("\n=== TESTING ALL 5 SCENARIOS ===")
	
	var scenario_manager = get_node_or_null("/root/ScenarioManager")
	var scenario_ids = ["small_farm", "industrial_giant", "organic_specialist", "research_station", "inheritance"]
	
	for scenario_id in scenario_ids:
		var scenario = scenario_manager.get_scenario(scenario_id)
		var scenario_json = JSON.stringify(scenario)
		
		print("\n--- Testing: %s ---" % scenario.get("name"))
		print("Sites: %d" % scenario.get("starting_sites", []).size())
		print("Balance: %.1f M NOK" % (scenario.get("starting_balance") / 1000000.0))
		print("Debt: %.1f M NOK" % (scenario.get("starting_debt", 0) / 1000000.0))
		
		GameBridge.ApplyScenario(scenario_json)

## Connect to C# signals for manual testing
func connect_signals():
	if GameBridge:
		GameBridge.ScenarioApplied.connect(_on_scenario_applied)
		GameBridge.GameStateSnapshot.connect(_on_game_state_snapshot)
		print("✅ Signals connected")
