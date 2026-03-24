extends Node
## SimBridgeAchievements - Integration helper for C# SimBridge
## Connects C# game state updates to achievement system

# ============================================================================
# CONFIGURATION
# ============================================================================

## How often to check achievements (seconds)
## Set to 0 to disable automatic checking
@export var auto_check_interval: float = 1.0

var time_since_last_check: float = 0.0

# ============================================================================
# LIFECYCLE
# ============================================================================

func _ready():
	# Connect to SimBridge signals if available
	if SimBridge:
		# Example signal connections (adjust based on your SimBridge API)
		# SimBridge.harvest_completed.connect(_on_harvest_completed)
		# SimBridge.day_advanced.connect(_on_day_advanced)
		# SimBridge.balance_changed.connect(_on_balance_changed)
		# SimBridge.research_unlocked.connect(_on_research_unlocked)
		pass
	else:
		push_warning("SimBridge not found - achievement tracking disabled")

func _process(delta):
	if auto_check_interval > 0:
		time_since_last_check += delta
		if time_since_last_check >= auto_check_interval:
			time_since_last_check = 0.0
			_check_achievements_from_game_state()

# ============================================================================
# GAME STATE EXTRACTION
# ============================================================================

## Extract game state from C# SimBridge and check achievements
func _check_achievements_from_game_state():
	if not SimBridge or not AchievementManager:
		return
	
	var game_state = _extract_game_state_from_simbridge()
	AchievementManager.check_achievements(game_state)

## Extract current game state from SimBridge
## Adjust based on your actual SimBridge API
func _extract_game_state_from_simbridge() -> Dictionary:
	var state = {}
	
	# Example: Query C# GameState through SimBridge
	# Replace these with your actual SimBridge API calls
	
	# Method 1: If SimBridge has direct property accessors
	# state["balance"] = SimBridge.GetBalance()
	# state["reputation"] = SimBridge.GetReputation()
	
	# Method 2: If SimBridge returns JSON
	# var game_data_json = SimBridge.GetGameStateJSON()
	# var json = JSON.new()
	# if json.parse(game_data_json) == OK:
	#     var game_data = json.data
	#     state["balance"] = game_data.get("balance", 0.0)
	#     state["reputation"] = game_data.get("reputation", 0.0)
	
	# Method 3: Manual extraction (template)
	state = {
		# Harvest
		"harvest_count": _get_stat("harvest_count", 0),
		
		# Financial
		"balance": _get_stat("balance", 0.0),
		
		# Production
		"total_fish_produced": _get_stat("total_fish_produced", 0),
		
		# Reputation
		"reputation": _get_stat("reputation", 0.0),
		
		# Sustainability
		"no_chemicals_days": _get_stat("no_chemicals_days", 0),
		"high_sustainability_days": _get_stat("high_sustainability_days", 0),
		"zero_mortality_days": _get_stat("zero_mortality_days", 0),
		
		# Technology
		"research_unlocked_count": _get_stat("research_unlocked_count", 0),
		
		# Business
		"contracts_signed": _get_stat("contracts_signed", 0),
		"is_public": _get_stat("is_public", false),
		"acquisitions_completed": _get_stat("acquisitions_completed", 0),
		"export_countries_count": _get_stat("export_countries_count", 0),
		"premium_pricing_days": _get_stat("premium_pricing_days", 0),
		
		# Time
		"days_elapsed": _get_stat("days_elapsed", 0),
		
		# Certifications
		"organic_certified": _get_stat("organic_certified", false),
		"organic_certified_days": _get_stat("organic_certified_days", 0),
		
		# Quality
		"max_genetic_quality": _get_stat("max_genetic_quality", 0.0),
		
		# Health
		"disease_free_days": _get_stat("disease_free_days", 0),
		"disease_outbreaks_survived": _get_stat("disease_outbreaks_survived", 0),
		
		# Performance
		"fcr_efficiency_days": _get_stat("fcr_efficiency_days", 0),
		
		# Expansion
		"active_regions_count": _get_stat("active_regions_count", 0),
	}
	
	return state

## Helper: Get stat from SimBridge (replace with actual API calls)
func _get_stat(stat_name: String, default_value):
	# TODO: Replace with actual SimBridge API calls
	# Example:
	# if SimBridge.has_method("Get" + stat_name.capitalize()):
	#     return SimBridge.call("Get" + stat_name.capitalize())
	
	return default_value

# ============================================================================
# EVENT HANDLERS (Connect to SimBridge signals)
# ============================================================================

## Example: Called when harvest completes
func _on_harvest_completed():
	_check_achievements_from_game_state()

## Example: Called when game day advances
func _on_day_advanced(_day: int):
	_check_achievements_from_game_state()

## Example: Called when balance changes
func _on_balance_changed(_new_balance: float):
	_check_achievements_from_game_state()

## Example: Called when research is unlocked
func _on_research_unlocked(_research_id: String):
	_check_achievements_from_game_state()

## Example: Called when contract is signed
func _on_contract_signed():
	_check_achievements_from_game_state()

# ============================================================================
# MANUAL TRIGGERING
# ============================================================================

## Manually trigger achievement check (for testing or explicit calls)
func check_now():
	_check_achievements_from_game_state()

# ============================================================================
# DEBUG
# ============================================================================

## Print current game state (for debugging)
func debug_print_game_state():
	var state = _extract_game_state_from_simbridge()
	print("========== CURRENT GAME STATE ==========")
	for key in state.keys():
		print("  %s: %s" % [key, state[key]])
	print("========================================")
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
