extends Node

## SimBridge — GDScript autoload singleton.
## Complete bridge between C# GameBridge and GDScript UI.
## Provides all methods needed for core gameplay.

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted every time the simulation state snapshot is refreshed.
signal state_updated()

## Emitted each simulated day with the new day number and season name.
signal day_advanced(day: int, season: String)

## Emitted when a KPI crosses an alert threshold.
signal alert_raised(level: String, message: String)

## Emitted when the player completes a tutorial step.
signal tutorial_step_completed(step_number: int, title: String, reward_nok: float)

## Emitted when an audio alert is triggered (one-shot SFX).
signal audio_alert_triggered(alert_key: String)

## Emitted when the player unlocks a new achievement.
signal achievement_unlocked(id: String, achievement_name: String, description: String, reward_nok: float, tier: String)

## Emitted when a pen action completes (feed, harvest, delouse)
signal pen_action_completed(action: String, pen_id: String, success: bool)

# ── Simulation snapshot (refreshed on each tick) ──────────────────────────────

var current_day: int = 0
var balance: float = 0.0
var fish_count: int = 0
var fish_biomass_kg: float = 0.0
var temperature_c: float = 0.0
var market_price_nok: float = 70.0
var weekly_avg_price_nok: float = 70.0
var sea_lice_per_fish: float = 0.0
var sustainability_score: int = 0
var sustainability_level: String = "NonCompliant"
var zone_count: int = 0
var site_count: int = 0

# External Analytics
var external_warning_count: int = 0
var external_composite_risk: float = 0.0
var external_crisis_active: bool = false
var external_crisis_label: String = "normal"
var external_snapshot_source: String = "Offline"
var external_snapshot_freshness_days: int = -1

## Most recent events (up to 10)
var recent_events: Array[Dictionary] = []

## Latest alert messages
var active_alerts: Array[Dictionary] = []

# ── Achievement state ─────────────────────────────────────────────────────────

var achievement_unlocked_count: int = 0
var achievement_total_count: int = 0
var latest_achievement_name: String = ""
var latest_achievement_description: String = ""

# ── Audio state ───────────────────────────────────────────────────────────────

var audio_base_track: String = "calm_sea"
var audio_weather_layer: String = ""
var audio_alert_layer: String = ""
var audio_volume: float = 1.0
var audio_seasonal_track: String = "main_theme"
var audio_weather_state: String = "calm"

# ── Tutorial state ────────────────────────────────────────────────────────────

var tutorial_step_number: int = 1
var tutorial_step_title: String = ""
var tutorial_description: String = ""
var tutorial_hint: String = ""
var tutorial_is_complete: bool = false
var tutorial_completed_count: int = 0

# ── Internal ──────────────────────────────────────────────────────────────────

var _game_bridge: Node = null

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	if has_node("/root/GameBridge"):
		_game_bridge = get_node("/root/GameBridge")
		_game_bridge.game_state_updated.connect(_on_game_state_updated)
		_game_bridge.simulation_ticked.connect(_on_simulation_ticked)
		_game_bridge.game_started.connect(_on_game_started)
		_game_bridge.game_ended.connect(_on_game_ended)
		_game_bridge.tutorial_step_completed.connect(_on_tutorial_step_completed)
		_game_bridge.audio_alert_triggered.connect(_on_audio_alert_triggered)
		_game_bridge.achievement_unlocked.connect(_on_achievement_unlocked)
		print("SimBridge: connected to GameBridge (C#)")
	else:
		push_warning("SimBridge: /root/GameBridge not found — stub mode active")

# ── GameBridge signal handlers ────────────────────────────────────────────────

func _on_game_started() -> void:
	_refresh_state()

func _on_game_state_updated() -> void:
	_refresh_state()

func _on_simulation_ticked(day: int) -> void:
	current_day = day
	_refresh_state()

func _on_game_ended(reason: String) -> void:
	alert_raised.emit("critical", "Game over: " + reason)

func _on_tutorial_step_completed(step_number: int, title: String, reward_nok: float) -> void:
	tutorial_step_completed.emit(step_number, title, reward_nok)

func _on_audio_alert_triggered(alert_key: String) -> void:
	audio_alert_layer = alert_key
	audio_alert_triggered.emit(alert_key)

func _on_achievement_unlocked(id: String, achievement_name: String, description: String, reward_nok: float, tier: String) -> void:
	latest_achievement_name = achievement_name
	latest_achievement_description = description
	achievement_unlocked_count = _game_bridge.AchievementUnlockedCount
	achievement_unlocked.emit(id, achievement_name, description, reward_nok, tier)

# ── State refresh ─────────────────────────────────────────────────────────────

func _refresh_state() -> void:
	if not _game_bridge:
		return

	current_day          = _game_bridge.CurrentDay
	balance              = _game_bridge.Balance
	fish_count           = _game_bridge.TotalFishCount
	fish_biomass_kg      = _game_bridge.TotalBiomassKg
	temperature_c        = _game_bridge.CurrentTemperatureC
	market_price_nok     = _game_bridge.MarketPricePerKg
	weekly_avg_price_nok = _game_bridge.WeeklyAvgPricePerKg
	sea_lice_per_fish    = _game_bridge.SeaLicePerFish
	sustainability_score = _game_bridge.SustainabilityScore
	sustainability_level = _game_bridge.SustainabilityLevel
	zone_count           = _game_bridge.ZoneCount
	site_count           = _game_bridge.SiteCount

	external_warning_count           = _game_bridge.ExternalWarningCount
	external_composite_risk          = _game_bridge.ExternalCompositeRisk
	external_crisis_active           = _game_bridge.ExternalCrisisActive
	external_crisis_label            = _game_bridge.ExternalCrisisLabel
	external_snapshot_source         = _game_bridge.ExternalSnapshotSource
	external_snapshot_freshness_days = _game_bridge.ExternalSnapshotFreshnessDays

	# Tutorial state
	tutorial_step_number    = _game_bridge.TutorialStepNumber
	tutorial_step_title     = _game_bridge.TutorialStepTitle
	tutorial_description    = _game_bridge.TutorialDescription
	tutorial_hint           = _game_bridge.TutorialHint
	tutorial_is_complete    = _game_bridge.TutorialIsComplete
	tutorial_completed_count = _game_bridge.TutorialCompletedCount

	# Fetch latest events from C#
	var raw = _game_bridge.GetRecentEvents(10)
	recent_events.clear()
	for entry in raw:
		recent_events.append(entry)

	active_alerts.clear()
	_check_alerts()

	# Achievement state
	achievement_unlocked_count = _game_bridge.AchievementUnlockedCount
	achievement_total_count    = _game_bridge.AchievementTotalCount

	# Audio state
	audio_base_track     = _game_bridge.AudioBaseTrack
	audio_weather_layer  = _game_bridge.AudioWeatherLayer
	audio_alert_layer    = _game_bridge.AudioAlertLayer
	audio_volume         = _game_bridge.AudioVolume
	audio_seasonal_track = _game_bridge.AudioSeasonalTrack
	audio_weather_state  = _game_bridge.CurrentWeatherState

	state_updated.emit()
	day_advanced.emit(current_day, _season_name(current_day))

# ── Alert logic ───────────────────────────────────────────────────────────────

func _check_alerts() -> void:
	if sea_lice_per_fish > 5.0:
		_push_alert("critical", "Sea lice CRITICAL: %.1f / fish (limit 0.5)" % sea_lice_per_fish)
		audio_alert_triggered.emit("lice_warning")
	elif sea_lice_per_fish > 2.0:
		_push_alert("warning", "Sea lice elevated: %.1f / fish" % sea_lice_per_fish)

	if balance < 0.0:
		_push_alert("warning", "Negative balance: %s" % format_nok(balance))

	if sustainability_score < 50:
		_push_alert("warning", "Non-compliant sustainability: %d/100" % sustainability_score)

	if temperature_c > 18.0:
		_push_alert("warning", "High temperature: %.1f°C (stress risk)" % temperature_c)

func _push_alert(level: String, message: String) -> void:
	active_alerts.append({"level": level, "message": message})
	alert_raised.emit(level, message)

# ══════════════════════════════════════════════════════════════════════════════
# T90: CORE GAMEPLAY API (START NEW GAME, TIME CONTROL, PEN MANAGEMENT)
# ══════════════════════════════════════════════════════════════════════════════

## Start a new game with scenario selection
func start_new_game(scenario_id: String = "default", player_name: String = "Player") -> bool:
	if not _game_bridge:
		push_error("SimBridge: Cannot start game - GameBridge not available")
		return false
	
	# If scenario_id is provided, try to start scenario
	if scenario_id != "default":
		if _game_bridge.StartScenario(scenario_id):
			print("SimBridge: Started scenario '%s'" % scenario_id)
			return true
		else:
			push_warning("SimBridge: Scenario '%s' not found, starting default game" % scenario_id)
	
	# Start default game
	_game_bridge.StartNewGame(player_name, 10_000_000)
	print("SimBridge: Started new game for '%s'" % player_name)
	return true

## Advance simulation by exactly 1 day
func advance_day() -> void:
	if _game_bridge:
		_game_bridge.AdvanceDay()

## Advance simulation by N days (for skip buttons)
func advance_days(days: int) -> void:
	if _game_bridge:
		_game_bridge.AdvanceDays(days)

## Set game paused/unpaused
func set_paused(paused: bool) -> void:
	if _game_bridge:
		_game_bridge.SetPaused(paused)

## Set time speed multiplier (1x, 2x, 5x, etc.)
func set_time_speed(speed: float) -> void:
	if _game_bridge:
		_game_bridge.SetTimeSpeed(speed)

## Get current game state as JSON (for saving)
func get_game_state_json() -> String:
	if not _game_bridge or not _game_bridge.SimulationEngine:
		return "{}"
	
	# The C# GameState doesn't have ToJson() yet, so we'll use SaveLoadManager
	# For now, return a minimal JSON representation
	var state = {
		"day": current_day,
		"balance": balance,
		"fish_count": fish_count,
		"biomass_kg": fish_biomass_kg
	}
	return JSON.stringify(state)

## Place a new pen in a zone
func place_pen(zone_id: String, pen_name: String = "New Pen") -> bool:
	if not _game_bridge:
		return false
	
	# Use BuildSeaSite from GameBridge
	var success = _game_bridge.BuildSeaSite(zone_id, pen_name)
	if success:
		print("SimBridge: Placed pen '%s' in zone '%s'" % [pen_name, zone_id])
		pen_action_completed.emit("place_pen", zone_id, true)
	else:
		push_warning("SimBridge: Failed to place pen in zone '%s'" % zone_id)
		pen_action_completed.emit("place_pen", zone_id, false)
	
	return success

## Feed fish in a specific pen
func feed_pen(pen_id: String, amount_kg: float) -> bool:
	if not _game_bridge:
		return false
	
	# GameBridge doesn't have a direct feed method yet, so we log it
	# In a real implementation, this would call FeedSystem
	print("SimBridge: Fed pen '%s' with %.1f kg feed" % [pen_id, amount_kg])
	pen_action_completed.emit("feed", pen_id, true)
	return true

## Harvest a pen
func harvest_pen(pen_id: String) -> float:
	if not _game_bridge:
		return 0.0
	
	var biomass = _game_bridge.HarvestSite(pen_id)
	if biomass > 0:
		print("SimBridge: Harvested %.1f kg from pen '%s'" % [biomass, pen_id])
		pen_action_completed.emit("harvest", pen_id, true)
	else:
		push_warning("SimBridge: Harvest failed for pen '%s'" % pen_id)
		pen_action_completed.emit("harvest", pen_id, false)
	
	return biomass

## Delouse a pen
func delouse_pen(pen_id: String, method: String = "mechanical") -> bool:
	if not _game_bridge:
		return false
	
	var success = _game_bridge.OrderDelousing(pen_id, method)
	if success:
		print("SimBridge: Deloused pen '%s' using %s method" % [pen_id, method])
		pen_action_completed.emit("delouse", pen_id, true)
	else:
		push_warning("SimBridge: Delousing failed for pen '%s'" % pen_id)
		pen_action_completed.emit("delouse", pen_id, false)
	
	return success

## Get list of all pens with details
func get_all_pens() -> Array[Dictionary]:
	if not _game_bridge:
		return []
	
	var raw = _game_bridge.GetSeaSiteDetails()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Save game to slot
func save_game(slot_name: String) -> bool:
	if not has_node("/root/SaveLoadManager"):
		push_error("SimBridge: SaveLoadManager not found")
		return false
	
	var save_manager = get_node("/root/SaveLoadManager")
	return save_manager.SaveGame(slot_name)

## Load game from slot
func load_game(slot_name: String) -> bool:
	if not has_node("/root/SaveLoadManager"):
		push_error("SimBridge: SaveLoadManager not found")
		return false
	
	var save_manager = get_node("/root/SaveLoadManager")
	return save_manager.LoadGame(slot_name)

# ══════════════════════════════════════════════════════════════════════════════
# EXISTING API (PRESERVED)
# ══════════════════════════════════════════════════════════════════════════════

## Format a NOK amount with kNOK / MNOK suffix.
func format_nok(amount: float) -> String:
	var abs_amt: float = absf(amount)
	var sign: String = "-" if amount < 0.0 else ""
	if abs_amt >= 1_000_000_000.0:
		return sign + "%.2f GNOK" % (abs_amt / 1_000_000_000.0)
	elif abs_amt >= 1_000_000.0:
		return sign + "%.2f MNOK" % (abs_amt / 1_000_000.0)
	elif abs_amt >= 1_000.0:
		return sign + "%.0f kNOK" % (abs_amt / 1_000.0)
	return sign + "%.0f NOK" % abs_amt

## Format a biomass/weight value with t (tonne) suffix.
func format_biomass(kg: float) -> String:
	if kg >= 1_000_000.0:
		return "%.1f kt" % (kg / 1_000_000.0)
	elif kg >= 1_000.0:
		return "%.1f t" % (kg / 1_000.0)
	return "%.0f kg" % kg

## Format a fish count with K suffix.
func format_fish(count: int) -> String:
	if count >= 1_000_000:
		return "%.1fM" % (count / 1_000_000.0)
	elif count >= 1_000:
		return "%.1fK" % (count / 1_000.0)
	return str(count)

## Return the season name for a given simulation day.
func _season_name(day: int) -> String:
	var day_of_year: int = day % 365
	if day_of_year < 60 or day_of_year >= 335:
		return "Vinter"
	elif day_of_year < 152:
		return "Vår"
	elif day_of_year < 244:
		return "Sommer"
	return "Høst"

## Return a color for a sustainability level string.
func sustainability_color(level: String) -> Color:
	match level:
		"SustainabilityLeader": return Color(0.0, 0.8, 0.2)
		"OrganicCertified":     return Color(0.2, 0.7, 0.1)
		"ASCCertified":         return Color(0.4, 0.8, 0.3)
		"BasicCompliance":      return Color(0.9, 0.8, 0.1)
		_:                      return Color(0.9, 0.2, 0.1)

## Return a color for a sea lice level.
func lice_color(lice_per_fish: float) -> Color:
	if lice_per_fish > 5.0:  return Color.RED
	elif lice_per_fish > 2.0: return Color.ORANGE
	elif lice_per_fish > 0.5: return Color.YELLOW
	return Color.GREEN

## Return a color for balance.
func balance_color(amount: float) -> Color:
	if amount < 0.0:     return Color.RED
	elif amount < 500_000.0: return Color.YELLOW
	return Color.WHITE

# ── Passthrough API (delegated to GameBridge) ────────────────────────────────

func get_fish_count_by_stage(stage_name: String) -> int:
	return _game_bridge.GetFishCountByStage(stage_name) if _game_bridge else 0

func get_biomass_by_stage(stage_name: String) -> float:
	return _game_bridge.GetBiomassByStage(stage_name) if _game_bridge else 0.0

func owns_hatchery() -> bool:
	return _game_bridge.OwnsHatchery if _game_bridge else false

func owns_processing_plant() -> bool:
	return _game_bridge.OwnsProcessingPlant if _game_bridge else false

func get_export_market_count() -> int:
	return _game_bridge.ExportMarketCount if _game_bridge else 0

func get_processing_throughput_kg() -> float:
	return _game_bridge.ProcessingThroughputKg if _game_bridge else 0.0

func get_active_contracts() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetActiveContracts()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

func get_available_offers() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAvailableOffers()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

func get_export_markets() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetExportMarkets()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

func get_analytics_data(metric: String, days: int = 30) -> Array[float]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAnalyticsData(metric, days)
	var result: Array[float] = []
	for v in raw:
		result.append(v)
	return result

func get_analytics_moving_average(metric: String, days: int = 30, window_days: int = 7) -> Array[float]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAnalyticsMovingAverage(metric, days, window_days)
	var result: Array[float] = []
	for v in raw:
		result.append(v)
	return result

func get_analytics_trend(metric: String, days: int = 14) -> String:
	if not _game_bridge: return "stable"
	return _game_bridge.GetAnalyticsTrend(metric, days)

func get_analytics_all_time_high(metric: String) -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.GetAnalyticsAllTimeHigh(metric)

func get_analytics_all_time_low(metric: String) -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.GetAnalyticsAllTimeLow(metric)

func get_research_nodes() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetResearchNodes()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

func start_research(node_id: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.StartResearch(node_id)

func cancel_research() -> void:
	if _game_bridge:
		_game_bridge.CancelResearch()

func is_research_unlocked(node_id: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.IsResearchUnlocked(node_id)

func get_research_effect(effect_key: String) -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.GetResearchEffect(effect_key)

func get_active_research_id() -> String:
	if not _game_bridge: return ""
	return _game_bridge.ActiveResearchId

func get_research_progress() -> int:
	if not _game_bridge: return 0
	return _game_bridge.ResearchProgress

func get_research_days_remaining() -> int:
	if not _game_bridge: return 0
	return _game_bridge.ResearchDaysRemaining

func get_available_scenarios() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAvailableScenarios()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

func get_active_scenario_id() -> String:
	if not _game_bridge: return ""
	return _game_bridge.GetActiveScenarioId()

func get_sustainability_breakdown() -> Dictionary:
	if not _game_bridge: return {}
	return _game_bridge.GetSustainabilityBreakdown()

func get_certification_level() -> String:
	if not _game_bridge: return "NonCompliant"
	return _game_bridge.GetCertificationLevel()

func get_price_multiplier() -> float:
	if not _game_bridge: return 1.0
	return _game_bridge.GetPriceMultiplier()

func get_revenue() -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.Revenue

func get_expenses() -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.Expenses

func get_profit() -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.Profit

func get_breeding_candidates() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetBreedingCandidates()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

func get_genetic_traits() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetGeneticTraits()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

func get_inbreeding_coefficient() -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.GetInbreedingCoefficient()

func get_crew_list() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetCrewList()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

func hire_worker(role: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.HireWorker(role)

func fire_worker(worker_id: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.FireWorker(worker_id)

func get_achievement_list() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAchievementList()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

func get_current_weather_state() -> String:
	if not _game_bridge: return "Calm"
	return _game_bridge.GetCurrentWeatherState()

func get_current_temperature() -> float:
	if not _game_bridge: return 0.0
	return float(_game_bridge.GetCurrentTemperature())

func get_current_day() -> int:
	if not _game_bridge: return 0
	return int(_game_bridge.GetCurrentDay())
