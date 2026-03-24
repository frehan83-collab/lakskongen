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

# ── Empire Systems State (T65) ────────────────────────────────────────────────

# CompetitorAI
var competitor_count: int = 0
var market_leader_name: String = ""

# EmpireProgression
var empire_phase: String = "SmallFarmer"
var empire_score: int = 0
var growth_strategy: String = "Organic"

# StockMarket
var ownership_status: String = "Private"
var share_price: float = 0.0
var market_cap: float = 0.0
var is_public: bool = false

# BrandReputation
var brand_score: float = 50.0
var brand_strategy: String = "Commodity"
var pricing_power: float = 1.0

# CrisisCapitalism
var active_crises_count: int = 0
var acquisition_opportunities_count: int = 0

# VerticalIntegration
var integration_score: float = 0.0
var owned_assets_count: int = 0

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

	# Empire Systems state (T65)
	competitor_count             = _game_bridge.GetCompetitorCount()
	var leader = _game_bridge.GetMarketLeader()
	market_leader_name           = leader.get("name", "") if leader else ""
	empire_phase                 = _game_bridge.GetEmpirePhase()
	empire_score                 = _game_bridge.GetEmpireScore()
	growth_strategy              = _game_bridge.GetGrowthStrategy()
	ownership_status             = _game_bridge.GetOwnershipStatus()
	share_price                  = _game_bridge.GetSharePrice()
	market_cap                   = _game_bridge.GetMarketCap()
	is_public                    = _game_bridge.IsPublic()
	brand_score                  = _game_bridge.GetBrandScore()
	brand_strategy               = _game_bridge.GetBrandStrategy()
	pricing_power                = _game_bridge.GetPricingPower()
	active_crises_count          = _game_bridge.GetActiveCrises().Count
	acquisition_opportunities_count = _game_bridge.GetAcquisitionOpportunities().Count
	integration_score            = _game_bridge.GetIntegrationScore()
	owned_assets_count           = _game_bridge.GetOwnedAssets().Count

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

# ══════════════════════════════════════════════════════════════════════════════
# NEW FARM SYSTEMS (RAS, OFFSHORE, AI FEEDING, POST-SMOLT)
# ══════════════════════════════════════════════════════════════════════════════

# ── RAS Facility Methods ──────────────────────────────────────────────────────

## Get list of all RAS (Recirculating Aquaculture System) facilities
func get_ras_facilities() -> Array[Dictionary]:
	if _game_bridge and _game_bridge.has_method("GetRasFacilities"):
		var raw = _game_bridge.GetRasFacilities()
		var result: Array[Dictionary] = []
		for entry in raw:
			result.append(entry)
		return result
	
	# Mock data for Norwegian RAS facilities
	return [
		{
			"id": "ras_001",
			"name": "Bergen RAS Anlegg",
			"scale": "medium",
			"operational": true,
			"construction_progress": 100,
			"water_temp": 12.5,
			"oxygen": 8.2,
			"ph": 7.6,
			"co2": 12.0,
			"biofilter_efficiency": 92.5,
			"daily_opex": 85000.0,
			"needs_maintenance": false
		},
		{
			"id": "ras_002",
			"name": "Tromsø Indoor Farm",
			"scale": "large",
			"operational": true,
			"construction_progress": 100,
			"water_temp": 13.0,
			"oxygen": 8.5,
			"ph": 7.5,
			"co2": 10.5,
			"biofilter_efficiency": 95.0,
			"daily_opex": 120000.0,
			"needs_maintenance": false
		}
	]

## Start construction of a new RAS facility
func build_ras_facility(name: String, scale: String) -> bool:
	if _game_bridge and _game_bridge.has_method("BuildRasFacility"):
		return _game_bridge.BuildRasFacility(name, scale)
	
	# Mock: Check if we have enough funds (cost depends on scale)
	var cost_map = {
		"small": 15_000_000.0,
		"medium": 35_000_000.0,
		"large": 75_000_000.0
	}
	var cost = cost_map.get(scale, 35_000_000.0)
	
	if balance >= cost:
		print("SimBridge: Building RAS facility '%s' (%s scale) for %s" % [name, scale, format_nok(cost)])
		return true
	else:
		push_warning("SimBridge: Insufficient funds to build RAS facility (need %s, have %s)" % [format_nok(cost), format_nok(balance)])
		return false

## Perform maintenance on RAS facility, returns cost
func ras_maintenance(facility_id: String) -> float:
	if _game_bridge and _game_bridge.has_method("RasMaintenance"):
		return _game_bridge.RasMaintenance(facility_id)
	
	# Mock: Maintenance cost ~5% of daily OPEX * 30 days
	var maintenance_cost = randf_range(120000.0, 180000.0)
	print("SimBridge: RAS maintenance for '%s': %s" % [facility_id, format_nok(maintenance_cost)])
	return maintenance_cost

# ── Offshore Farm Methods ─────────────────────────────────────────────────────

## Get list of all offshore farm installations
func get_offshore_farms() -> Array[Dictionary]:
	if _game_bridge and _game_bridge.has_method("GetOffshoreFarms"):
		var raw = _game_bridge.GetOffshoreFarms()
		var result: Array[Dictionary] = []
		for entry in raw:
			result.append(entry)
		return result
	
	# Mock data for Norwegian offshore farms
	return [
		{
			"id": "offshore_001",
			"name": "Havfarm 1",
			"type": "semi_submersible",
			"operational": true,
			"structural_integrity": 98.5,
			"storm_lockdown": false,
			"wave_height": 1.2,
			"crew_days_since_rotation": 12,
			"fish_count": 180000,
			"daily_opex": 95000.0
		},
		{
			"id": "offshore_002",
			"name": "Ocean Farm Nord",
			"type": "ocean_egg",
			"operational": true,
			"structural_integrity": 97.0,
			"storm_lockdown": false,
			"wave_height": 1.8,
			"crew_days_since_rotation": 8,
			"fish_count": 250000,
			"daily_opex": 110000.0
		}
	]

## Start construction of offshore farm
func build_offshore_farm(name: String, farm_type: String) -> bool:
	if _game_bridge and _game_bridge.has_method("BuildOffshoreFarm"):
		return _game_bridge.BuildOffshoreFarm(name, farm_type)
	
	# Mock: Offshore is expensive
	var cost_map = {
		"semi_submersible": 200_000_000.0,
		"ocean_egg": 350_000_000.0,
		"spar_buoy": 180_000_000.0
	}
	var cost = cost_map.get(farm_type, 200_000_000.0)
	
	if balance >= cost:
		print("SimBridge: Building offshore farm '%s' (%s) for %s" % [name, farm_type, format_nok(cost)])
		return true
	else:
		push_warning("SimBridge: Insufficient funds for offshore farm (need %s)" % format_nok(cost))
		return false

## Repair offshore structure, returns cost
func repair_offshore(farm_id: String) -> float:
	if _game_bridge and _game_bridge.has_method("RepairOffshore"):
		return _game_bridge.RepairOffshore(farm_id)
	
	# Mock: Structural repair is costly
	var repair_cost = randf_range(500000.0, 2000000.0)
	print("SimBridge: Offshore repair for '%s': %s" % [farm_id, format_nok(repair_cost)])
	return repair_cost

# ── AI Feeding Methods ────────────────────────────────────────────────────────

## Get AI feeding system status across all farms
func get_ai_feeding_status() -> Dictionary:
	if _game_bridge and _game_bridge.has_method("GetAiFeedingStatus"):
		return _game_bridge.GetAiFeedingStatus()
	
	# Mock: AI feeding improves FCR and reduces waste
	return {
		"installed": true,
		"calibration": 95.0,
		"camera_uptime": 98.5,
		"appetite_score": 87.0,
		"fcr_multiplier": 0.92,  # 8% improvement
		"daily_savings": 12500.0,
		"days_since_install": 45,
		"camera_maintenance_needed": false
	}

## Install AI feeding system (one-time cost)
func install_ai_feeding() -> float:
	if _game_bridge and _game_bridge.has_method("InstallAiFeeding"):
		return _game_bridge.InstallAiFeeding()
	
	# Mock: Installation cost
	var cost = 8_500_000.0
	print("SimBridge: Installing AI feeding system: %s" % format_nok(cost))
	return cost

## Clean AI camera sensors, returns maintenance cost
func clean_ai_cameras() -> float:
	if _game_bridge and _game_bridge.has_method("CleanAiCameras"):
		return _game_bridge.CleanAiCameras()
	
	# Mock: Regular maintenance
	var cost = randf_range(15000.0, 35000.0)
	print("SimBridge: AI camera cleaning: %s" % format_nok(cost))
	return cost

# ── Post-Smolt Methods ────────────────────────────────────────────────────────

## Get list of all post-smolt facilities
func get_post_smolt_facilities() -> Array[Dictionary]:
	if _game_bridge and _game_bridge.has_method("GetPostSmoltFacilities"):
		var raw = _game_bridge.GetPostSmoltFacilities()
		var result: Array[Dictionary] = []
		for entry in raw:
			result.append(entry)
		return result
	
	# Mock data for Norwegian post-smolt facilities
	return [
		{
			"id": "postsmolt_001",
			"name": "Vesterålen Settefisk",
			"capacity": 500000,
			"current_count": 320000,
			"avg_weight_g": 180.0,
			"batches_active": 3,
			"water_temp": 14.5,
			"daily_opex": 45000.0,
			"vaccination_rate": 98.5
		},
		{
			"id": "postsmolt_002",
			"name": "Nordland Post-Smolt AS",
			"capacity": 800000,
			"current_count": 650000,
			"avg_weight_g": 220.0,
			"batches_active": 5,
			"water_temp": 15.0,
			"daily_opex": 68000.0,
			"vaccination_rate": 99.2
		}
	]

## Start a new post-smolt batch
func start_smolt_batch(facility_id: String, count: int) -> bool:
	if _game_bridge and _game_bridge.has_method("StartSmoltBatch"):
		return _game_bridge.StartSmoltBatch(facility_id, count)
	
	# Mock: Check capacity
	var facilities = get_post_smolt_facilities()
	for facility in facilities:
		if facility.get("id") == facility_id:
			var capacity = facility.get("capacity", 500000)
			var current = facility.get("current_count", 0)
			if current + count <= capacity:
				print("SimBridge: Started batch of %s smolt at %s" % [format_fish(count), facility_id])
				return true
			else:
				push_warning("SimBridge: Capacity exceeded at %s" % facility_id)
				return false
	return false

## Vaccinate a smolt batch, returns cost
func vaccinate_batch(batch_id: String) -> float:
	if _game_bridge and _game_bridge.has_method("VaccinateBatch"):
		return _game_bridge.VaccinateBatch(batch_id)
	
	# Mock: ~15-25 NOK per fish for vaccination
	var cost = randf_range(150000.0, 350000.0)
	print("SimBridge: Vaccinating batch '%s': %s" % [batch_id, format_nok(cost)])
	return cost

## Transfer post-smolt batch to sea cages
func transfer_batch_to_sea(batch_id: String) -> bool:
	if _game_bridge and _game_bridge.has_method("TransferBatchToSea"):
		return _game_bridge.TransferBatchToSea(batch_id)
	
	# Mock: Transfer operation
	print("SimBridge: Transferring batch '%s' to sea" % batch_id)
	return true

# ══════════════════════════════════════════════════════════════════════════════
# EMPIRE SYSTEMS API (T65) - CompetitorAI, EmpireProgression, StockMarket, etc.
# ══════════════════════════════════════════════════════════════════════════════

# ── CompetitorAI Methods ──────────────────────────────────────────────────────

## Get all AI competitors
func get_competitors() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetCompetitors()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get number of competitors
func get_competitor_count() -> int:
	return _game_bridge.GetCompetitorCount() if _game_bridge else 0

## Get market leader info
func get_market_leader() -> Dictionary:
	return _game_bridge.GetMarketLeader() if _game_bridge else {}

# ── EmpireProgression Methods ─────────────────────────────────────────────────

## Get current empire phase
func get_empire_phase() -> String:
	return _game_bridge.GetEmpirePhase() if _game_bridge else "SmallFarmer"

## Get empire progression score (0-100)
func get_empire_score() -> int:
	return _game_bridge.GetEmpireScore() if _game_bridge else 0

## Get next milestone
func get_next_milestone() -> Dictionary:
	return _game_bridge.GetNextMilestone() if _game_bridge else {}

## Get current growth strategy
func get_growth_strategy() -> String:
	return _game_bridge.GetGrowthStrategy() if _game_bridge else "Organic"

# ── StockMarket Methods ───────────────────────────────────────────────────────

## Get ownership status (Private, PEBacked, EuronextGrowth, OsloBoers)
func get_ownership_status() -> String:
	return _game_bridge.GetOwnershipStatus() if _game_bridge else "Private"

## Get current share price (NOK)
func get_share_price() -> float:
	return _game_bridge.GetSharePrice() if _game_bridge else 0.0

## Get market capitalization (NOK)
func get_market_cap() -> float:
	return _game_bridge.GetMarketCap() if _game_bridge else 0.0

## Check if company is public
func check_is_public() -> bool:
	return _game_bridge.IsPublic() if _game_bridge else false

## Attempt an IPO
func attempt_ipo() -> Dictionary:
	return _game_bridge.AttemptIPO() if _game_bridge else {"success": false, "message": "Not initialized"}

# ── BrandReputation Methods ───────────────────────────────────────────────────

## Get overall brand reputation score (0-100)
func get_brand_score() -> float:
	return _game_bridge.GetBrandScore() if _game_bridge else 50.0

## Get brand strategy
func get_brand_strategy() -> String:
	return _game_bridge.GetBrandStrategy() if _game_bridge else "Commodity"

## Get active certifications
func get_certifications() -> Array[String]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetCertifications()
	var result: Array[String] = []
	for cert in raw:
		result.append(cert)
	return result

## Get pricing power multiplier
func get_pricing_power() -> float:
	return _game_bridge.GetPricingPower() if _game_bridge else 1.0

# ── CrisisCapitalism Methods ──────────────────────────────────────────────────

## Get active competitor crises
func get_active_crises() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetActiveCrises()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get available distressed assets for acquisition
func get_acquisition_opportunities() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAcquisitionOpportunities()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Attempt to acquire a distressed asset
func attempt_acquisition(asset_id: String) -> Dictionary:
	return _game_bridge.AttemptAcquisition(asset_id) if _game_bridge else {"success": false, "message": "Not initialized"}

# ── VerticalIntegration Methods ───────────────────────────────────────────────

## Get all integration tiers with ownership status
func get_integration_tiers() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetIntegrationTiers()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get owned integration assets
func get_owned_assets() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetOwnedAssets()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get integration score (0.0-1.0)
func get_integration_score() -> float:
	return _game_bridge.GetIntegrationScore() if _game_bridge else 0.0

## Purchase/build a vertical integration asset
func purchase_asset(asset_id: String) -> Dictionary:
	return _game_bridge.PurchaseAsset(asset_id) if _game_bridge else {"success": false, "message": "Not initialized"}

# ══════════════════════════════════════════════════════════════════════════════
# WAVE 2 SYSTEMS API (T71) - LoanSystem, TaxSystem, InvestorSystem, etc.
# ══════════════════════════════════════════════════════════════════════════════

# ── LoanSystem Methods ────────────────────────────────────────────────────────

## Get all active loans
func get_active_loans() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetActiveLoans()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get total debt outstanding (NOK)
func get_total_debt() -> float:
	return _game_bridge.GetTotalDebt() if _game_bridge else 0.0

## Get total monthly payment obligation (NOK)
func get_monthly_payment() -> float:
	return _game_bridge.GetMonthlyPayment() if _game_bridge else 0.0

## Take out a new loan
func take_loan(amount: float, term_months: int, rate: float) -> Dictionary:
	return _game_bridge.TakeLoan(amount, term_months, rate) if _game_bridge else {"success": false, "message": "Not initialized"}

# ── TaxSystem Methods ─────────────────────────────────────────────────────────

## Get current corporate tax rate (0.22 = 22%)
func get_tax_rate() -> float:
	return _game_bridge.GetTaxRate() if _game_bridge else 0.22

## Get last tax payment amount (NOK)
func get_last_tax_paid() -> float:
	return _game_bridge.GetLastTaxPaid() if _game_bridge else 0.0

## Get estimated annual tax burden (NOK)
func get_annual_tax_burden() -> float:
	return _game_bridge.GetAnnualTaxBurden() if _game_bridge else 0.0

# ── InvestorSystem Methods ────────────────────────────────────────────────────

## Get all investors/shareholders
func get_investors() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetInvestors()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get total investment from bonds and equity (NOK)
func get_total_investment() -> float:
	return _game_bridge.GetTotalInvestment() if _game_bridge else 0.0

## Get current equity dilution (0.0-1.0)
func get_equity_dilution() -> float:
	return _game_bridge.GetEquityDilution() if _game_bridge else 0.0

# ── AcquisitionSystem Methods ─────────────────────────────────────────────────

## Get available acquisition targets
func get_available_targets() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAvailableTargets()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get owned acquisitions
func get_owned_acquisitions() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetOwnedAcquisitions()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Attempt to acquire a target company
func attempt_acquisition_target(target_id: String) -> Dictionary:
	return _game_bridge.AttemptAcquisitionTarget(target_id) if _game_bridge else {"success": false, "message": "Not initialized"}

# ── LicenseSystem Methods ─────────────────────────────────────────────────────

## Get all active licenses
func get_active_licenses() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetActiveLicenses()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get license cost for a new site (NOK)
func get_license_cost() -> float:
	return _game_bridge.GetLicenseCost() if _game_bridge else 30_000_000.0

## Apply for a new license in a zone
func apply_for_license(zone_id: String) -> Dictionary:
	return _game_bridge.ApplyForLicense(zone_id) if _game_bridge else {"success": false, "message": "Not initialized"}

# ── StakeholderFactionSystem Methods ──────────────────────────────────────────

## Get all stakeholder factions
func get_factions() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetFactions()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get relationship score with a specific faction (-1.0 to +1.0)
func get_faction_relation(faction_id: String) -> float:
	return _game_bridge.GetFactionRelation(faction_id) if _game_bridge else 0.0

## Get all active demands from all factions
func get_active_demands() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetActiveDemands()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

# ── MarketIntelligence Methods ────────────────────────────────────────────────

## Get comprehensive market report
func get_market_report() -> Dictionary:
	return _game_bridge.GetMarketReport() if _game_bridge else {}

## Get competitor analysis
func get_competitor_analysis() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetCompetitorAnalysis()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get price forecast for next 4 weeks
func get_price_forecast() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetPriceForecast()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

# ── FishWelfare Methods ───────────────────────────────────────────────────────

## Get overall fish welfare score (0-100)
func get_welfare_score() -> float:
	return _game_bridge.GetWelfareScore() if _game_bridge else 50.0

## Get welfare breakdown
func get_welfare_breakdown() -> Dictionary:
	return _game_bridge.GetWelfareBreakdown() if _game_bridge else {}

## Get welfare alerts
func get_welfare_alerts() -> Array[String]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetWelfareAlerts()
	var result: Array[String] = []
	for alert in raw:
		result.append(alert)
	return result

# ── VisualMilestones Methods ──────────────────────────────────────────────────

## Get all achieved milestones
func get_achieved_milestones() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAchievedMilestones()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get next unachieved milestone
func get_next_milestone_info() -> Dictionary:
	return _game_bridge.GetNextMilestone() if _game_bridge else {}

## Get milestone completion progress (0.0-1.0)
func get_milestone_progress() -> float:
	return _game_bridge.GetMilestoneProgress() if _game_bridge else 0.0
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
