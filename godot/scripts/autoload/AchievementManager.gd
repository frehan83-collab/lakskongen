extends Node

## AchievementManager - Achievement tracking and unlock system
## Auto-loaded singleton for managing player achievements

# ============================================================================
# SIGNALS
# ============================================================================

signal achievement_unlocked(achievement_id: String, achievement_data: Dictionary)
signal achievement_progress_updated(achievement_id: String, progress: float)

# ============================================================================
# CONSTANTS
# ============================================================================

const ACHIEVEMENTS_PATH = "res://data/achievements.json"
const SAVE_PATH = "user://achievements.json"

# ============================================================================
# STATE
# ============================================================================

var achievements: Array = []
var unlocked_achievements: Dictionary = {} # {achievement_id: unlock_timestamp}
var achievement_progress: Dictionary = {} # {achievement_id: current_value}

# ============================================================================
# LIFECYCLE
# ============================================================================

func _ready():
	_load_achievements()
	_load_unlocked_achievements()

# ============================================================================
# ACHIEVEMENT LOADING
# ============================================================================

## Load all achievement definitions from JSON
func _load_achievements() -> void:
	if not FileAccess.file_exists(ACHIEVEMENTS_PATH):
		push_error("AchievementManager: achievements.json not found at %s" % ACHIEVEMENTS_PATH)
		return
	
	var file = FileAccess.open(ACHIEVEMENTS_PATH, FileAccess.READ)
	if not file:
		push_error("AchievementManager: Failed to open achievements.json")
		return
	
	var json_text = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var parse_result = json.parse(json_text)
	
	if parse_result != OK:
		push_error("AchievementManager: Failed to parse achievements.json: %s" % json.get_error_message())
		return
	
	achievements = json.data
	print("AchievementManager: Loaded %d achievement definitions" % achievements.size())

## Load unlocked achievements from save file
func _load_unlocked_achievements() -> void:
	if not FileAccess.file_exists(SAVE_PATH):
		print("AchievementManager: No saved achievements found, starting fresh")
		unlocked_achievements = {}
		return
	
	var file = FileAccess.open(SAVE_PATH, FileAccess.READ)
	if not file:
		push_error("AchievementManager: Failed to open achievement save file")
		unlocked_achievements = {}
		return
	
	var json_text = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var parse_result = json.parse(json_text)
	
	if parse_result != OK:
		push_error("AchievementManager: Failed to parse achievement save file")
		unlocked_achievements = {}
		return
	
	unlocked_achievements = json.data
	print("AchievementManager: Loaded %d unlocked achievements" % unlocked_achievements.size())

## Save unlocked achievements to file
func _save_unlocked_achievements() -> void:
	var file = FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if not file:
		push_error("AchievementManager: Failed to save achievements")
		return
	
	file.store_string(JSON.stringify(unlocked_achievements, "\t"))
	file.close()
	print("AchievementManager: Saved %d unlocked achievements" % unlocked_achievements.size())

# ============================================================================
# ACHIEVEMENT QUERIES
# ============================================================================

## Get all achievement definitions
func get_all_achievements() -> Array:
	return achievements

## Get achievement by ID
func get_achievement(achievement_id: String) -> Dictionary:
	for achievement in achievements:
		if achievement.get("id", "") == achievement_id:
			return achievement
	return {}

## Check if achievement is unlocked
func is_unlocked(achievement_id: String) -> bool:
	return achievement_id in unlocked_achievements

## Get unlock timestamp for achievement (0 if not unlocked)
func get_unlock_time(achievement_id: String) -> int:
	return unlocked_achievements.get(achievement_id, 0)

## Get progress for achievement (0.0 to 1.0)
func get_progress(achievement_id: String) -> float:
	if is_unlocked(achievement_id):
		return 1.0
	
	var achievement = get_achievement(achievement_id)
	if achievement.is_empty():
		return 0.0
	
	var condition = achievement.get("condition", {})
	var target_value = condition.get("value", 1)
	var current_value = achievement_progress.get(achievement_id, 0)
	
	if target_value <= 0:
		return 0.0
	
	return clamp(float(current_value) / float(target_value), 0.0, 1.0)

## Get current progress value for achievement
func get_progress_value(achievement_id: String) -> int:
	return achievement_progress.get(achievement_id, 0)

## Get all unlocked achievement IDs
func get_unlocked_ids() -> Array:
	return unlocked_achievements.keys()

## Get unlock percentage (0-100)
func get_completion_percentage() -> float:
	if achievements.size() == 0:
		return 0.0
	return (float(unlocked_achievements.size()) / float(achievements.size())) * 100.0

# ============================================================================
# ACHIEVEMENT UNLOCKING
# ============================================================================

## Unlock achievement by ID
func unlock(achievement_id: String) -> bool:
	if is_unlocked(achievement_id):
		return false # Already unlocked
	
	var achievement = get_achievement(achievement_id)
	if achievement.is_empty():
		push_warning("AchievementManager: Tried to unlock unknown achievement '%s'" % achievement_id)
		return false
	
	# Record unlock
	unlocked_achievements[achievement_id] = Time.get_unix_time_from_system()
	_save_unlocked_achievements()
	
	print("AchievementManager: Achievement unlocked - %s" % achievement.get("name", achievement_id))
	
	# Emit signal
	achievement_unlocked.emit(achievement_id, achievement)
	
	return true

## Update progress and check if achievement should unlock
func update_progress(achievement_id: String, current_value: int) -> void:
	var achievement = get_achievement(achievement_id)
	if achievement.is_empty():
		return
	
	if is_unlocked(achievement_id):
		return # Already unlocked
	
	# Update progress
	var old_value = achievement_progress.get(achievement_id, 0)
	achievement_progress[achievement_id] = current_value
	
	# Emit progress update
	if old_value != current_value:
		achievement_progress_updated.emit(achievement_id, get_progress(achievement_id))
	
	# Check if should unlock
	var condition = achievement.get("condition", {})
	var target_value = condition.get("value", 1)
	
	if current_value >= target_value:
		unlock(achievement_id)

# ============================================================================
# CONDITION CHECKING
# ============================================================================

## Check all achievements against current game state
func check_achievements(game_state: Dictionary) -> void:
	for achievement in achievements:
		var achievement_id = achievement.get("id", "")
		if achievement_id.is_empty() or is_unlocked(achievement_id):
			continue
		
		_check_achievement_condition(achievement, game_state)

## Check single achievement condition
func _check_achievement_condition(achievement: Dictionary, game_state: Dictionary) -> void:
	var achievement_id = achievement.get("id", "")
	var condition = achievement.get("condition", {})
	var condition_type = condition.get("type", "")
	var target_value = condition.get("value", 1)
	
	var current_value = 0
	
	# Extract current value from game state based on condition type
	match condition_type:
		"harvest_count":
			current_value = game_state.get("harvest_count", 0)
		"balance":
			current_value = int(game_state.get("balance", 0.0))
		"total_fish_produced":
			current_value = game_state.get("total_fish_produced", 0)
		"reputation":
			current_value = int(game_state.get("reputation", 0.0))
		"no_chemicals_duration":
			current_value = game_state.get("no_chemicals_days", 0)
		"high_sustainability_duration":
			current_value = game_state.get("high_sustainability_days", 0)
		"research_unlocked":
			current_value = game_state.get("research_unlocked_count", 0)
		"contracts_signed":
			current_value = game_state.get("contracts_signed", 0)
		"ipo_completed":
			current_value = 1 if game_state.get("is_public", false) else 0
		"acquisitions_completed":
			current_value = game_state.get("acquisitions_completed", 0)
		"years_survived":
			current_value = int(game_state.get("days_elapsed", 0) / 365)
		"organic_certified":
			current_value = 1 if game_state.get("organic_certified", false) else 0
		"organic_certification_duration":
			current_value = game_state.get("organic_certified_days", 0)
		"max_genetic_quality":
			current_value = int(game_state.get("max_genetic_quality", 0.0))
		"disease_free_duration":
			current_value = game_state.get("disease_free_days", 0)
		"disease_outbreaks_survived":
			current_value = game_state.get("disease_outbreaks_survived", 0)
		"premium_pricing_duration":
			current_value = game_state.get("premium_pricing_days", 0)
		"export_countries":
			current_value = game_state.get("export_countries_count", 0)
		"fcr_efficiency_duration":
			current_value = game_state.get("fcr_efficiency_days", 0)
		"zero_mortality_duration":
			current_value = game_state.get("zero_mortality_days", 0)
		"active_regions":
			current_value = game_state.get("active_regions_count", 0)
		_:
			push_warning("AchievementManager: Unknown condition type '%s' for achievement '%s'" % [condition_type, achievement_id])
			return
	
	# Update progress
	update_progress(achievement_id, current_value)

# ============================================================================
# RESET & DEBUG
# ============================================================================

## Reset all achievements (debug only)
func reset_all_achievements() -> void:
	unlocked_achievements.clear()
	achievement_progress.clear()
	_save_unlocked_achievements()
	print("AchievementManager: All achievements reset")

## Unlock all achievements (debug only)
func unlock_all_achievements() -> void:
	for achievement in achievements:
		var achievement_id = achievement.get("id", "")
		if not achievement_id.is_empty():
			unlock(achievement_id)
	print("AchievementManager: All achievements unlocked")
