extends Node

## ScenarioManager - Loads scenarios, applies starting conditions, tracks goals
## Auto-loaded singleton for managing game scenarios


# ============================================================================
# SIGNALS
# ============================================================================

signal scenario_loaded(scenario: Dictionary)
signal goal_progress_updated(progress: float, description: String)
signal goal_achieved()
signal goal_failed()

# ============================================================================
# CONSTANTS
# ============================================================================

const SCENARIOS_PATH = "res://data/scenarios.json"

# ============================================================================
# STATE
# ============================================================================

var scenarios: Array = []
var current_scenario: Dictionary = {}
var goal_tracker: Dictionary = {}

# ============================================================================
# LIFECYCLE
# ============================================================================

func _ready():
	_load_scenarios()

# ============================================================================
# SCENARIO LOADING
# ============================================================================

## Load all scenarios from JSON
func _load_scenarios() -> void:
	if not FileAccess.file_exists(SCENARIOS_PATH):
		push_error("ScenarioManager: scenarios.json not found at %s" % SCENARIOS_PATH)
		return
	
	var file = FileAccess.open(SCENARIOS_PATH, FileAccess.READ)
	if not file:
		push_error("ScenarioManager: Failed to open scenarios.json")
		return
	
	var json_text = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var parse_result = json.parse(json_text)
	
	if parse_result != OK:
		push_error("ScenarioManager: Failed to parse scenarios.json: %s" % json.get_error_message())
		return
	
	scenarios = json.data
	print("ScenarioManager: Loaded %d scenarios" % scenarios.size())

## Get all available scenarios
func get_scenarios() -> Array:
	return scenarios

## Get scenario by ID
func get_scenario(scenario_id: String) -> Dictionary:
	for scenario in scenarios:
		if scenario.get("id", "") == scenario_id:
			return scenario
	push_warning("ScenarioManager: Scenario '%s' not found" % scenario_id)
	return {}

# ============================================================================
# SCENARIO APPLICATION
# ============================================================================

## Apply scenario starting conditions to game state
## This will be called by SimBridge when starting a new game
func apply_scenario(scenario_id: String) -> bool:
	var scenario = get_scenario(scenario_id)
	if scenario.is_empty():
		return false
	
	current_scenario = scenario
	_reset_goal_tracker()
	
	print("ScenarioManager: Applying scenario '%s'" % scenario.get("name", "Unknown"))
	
	# Apply starting conditions via C# GameBridge
	# The C# side will handle actual GameState initialization
	var game_bridge_node = get_node_or_null("/root/GameBridge")
	if game_bridge_node != null:
		var scenario_json = JSON.stringify(scenario)
		game_bridge_node.ApplyScenario(scenario_json)
		print("ScenarioManager: Scenario applied via GameBridge")
	else:
		push_error("ScenarioManager: GameBridge not found - cannot apply scenario")
		return false
	
	scenario_loaded.emit(scenario)
	return true

# ============================================================================
# GOAL TRACKING
# ============================================================================

## Reset goal tracker for new scenario
func _reset_goal_tracker() -> void:
	goal_tracker = {
		"achieved": false,
		"failed": false,
		"start_day": 0,
		"current_progress": 0.0
	}

## Update goal progress based on current game state
## Called periodically by game loop or event triggers
func update_goal_progress(game_state: Dictionary) -> void:
	if current_scenario.is_empty():
		return
	
	if goal_tracker.get("achieved", false) or goal_tracker.get("failed", false):
		return  # Goal already resolved
	
	var goal = current_scenario.get("goal", {})
	var goal_type = goal.get("type", "")
	var target = goal.get("target", 0)
	var time_limit_years = goal.get("time_limit_years", 0)
	
	var current_day = game_state.get("Day", 0)
	var time_limit_days = time_limit_years * 365
	
	# Check time limit failure
	if time_limit_days > 0 and current_day > time_limit_days:
		_fail_goal()
		return
	
	# Check goal progress based on type
	var progress = 0.0
	var achieved = false
	
	match goal_type:
		"revenue":
			var total_revenue = game_state.get("total_revenue", 0)
			progress = float(total_revenue) / float(target)
			achieved = total_revenue >= target
		
		"ipo":
			var is_public = game_state.get("is_public", false)
			progress = 1.0 if is_public else 0.0
			achieved = is_public
		
		"reputation_and_profit":
			var reputation = game_state.get("reputation", 0)
			var balance = game_state.get("Balance", 0)
			var is_profitable = balance > current_scenario.get("starting_balance", 0)
			progress = float(reputation) / float(target)
			achieved = reputation >= target and is_profitable
		
		"research":
			var unlocked_count = game_state.get("UnlockedResearchIds", []).size()
			progress = float(unlocked_count) / float(target)
			achieved = unlocked_count >= target
		
		"debt_and_profit":
			var debt = game_state.get("debt", 0)
			var balance = game_state.get("Balance", 0)
			var debt_paid = debt <= 0
			var is_profitable = balance > 0
			progress = (0.7 if debt_paid else 0.0) + (0.3 if is_profitable else 0.0)
			achieved = debt_paid and is_profitable
	
	goal_tracker["current_progress"] = progress
	
	# Emit progress update
	var description = goal.get("description", "")
	goal_progress_updated.emit(progress, description)
	
	# Check achievement
	if achieved:
		_achieve_goal()

## Mark goal as achieved
func _achieve_goal() -> void:
	goal_tracker["achieved"] = true
	print("ScenarioManager: Goal achieved!")
	goal_achieved.emit()

## Mark goal as failed
func _fail_goal() -> void:
	goal_tracker["failed"] = true
	print("ScenarioManager: Goal failed - time limit exceeded")
	goal_failed.emit()

# ============================================================================
# VICTORY/FAILURE DETECTION
# ============================================================================

## Check if current scenario goal is achieved
func is_goal_achieved() -> bool:
	return goal_tracker.get("achieved", false)

## Check if current scenario goal failed
func is_goal_failed() -> bool:
	return goal_tracker.get("failed", false)

## Get current goal progress (0.0 - 1.0)
func get_goal_progress() -> float:
	return goal_tracker.get("current_progress", 0.0)

## Get current scenario info
func get_current_scenario() -> Dictionary:
	return current_scenario

## Get goal description
func get_goal_description() -> String:
	if current_scenario.is_empty():
		return ""
	var goal = current_scenario.get("goal", {})
	return goal.get("description", "")
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
