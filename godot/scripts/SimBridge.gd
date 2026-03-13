extends Node

## SimBridge — GDScript autoload singleton.
## Wraps the C# GameBridge autoload and exposes all simulation state
## as typed, GDScript-friendly properties.  UI nodes should read from
## SimBridge rather than calling the C# layer directly.
##
## Registration: project.godot → [autoload] SimBridge = "res://scripts/SimBridge.gd"

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
## alertKey: disease_outbreak | market_crash | achievement | harvest | lice_warning
signal audio_alert_triggered(alert_key: String)

## Emitted when the player unlocks a new achievement.
signal achievement_unlocked(achievement_name: String, description: String, reward_nok: float, tier: String)

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

# "?"? External Analytics "?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?"?
var external_warning_count: int = 0
var external_composite_risk: float = 0.0
var external_crisis_active: bool = false
var external_crisis_label: String = "normal"
var external_snapshot_source: String = "Offline"
var external_snapshot_freshness_days: int = -1

## Most recent events (up to 10), each a Dictionary with keys:
## "day", "name", "category", "description", "balance_change"
var recent_events: Array[Dictionary] = []

## Latest alert messages (cleared when state_updated fires again)
var active_alerts: Array[Dictionary] = []

# ── Achievement state (refreshed on each tick) ────────────────────────────────

var achievement_unlocked_count: int = 0
var achievement_total_count: int = 0
var latest_achievement_name: String = ""
var latest_achievement_description: String = ""

# ── Audio state (refreshed on each tick) ──────────────────────────────────────

## Primary ambient base track key: calm_sea | storm | wind | harbor | processing_plant
var audio_base_track: String = "calm_sea"

## Weather overlay layer key: "" | wind | rain | storm
var audio_weather_layer: String = ""

## Pending one-shot alert key: "" | disease_outbreak | market_crash | achievement | harvest | lice_warning
var audio_alert_layer: String = ""

## Master audio volume scalar 0–1
var audio_volume: float = 1.0

## Seasonal background music track name: spring_theme | summer_theme | autumn_theme | winter_theme
var audio_seasonal_track: String = "main_theme"

## Current weather key (calm | wind | storm) — convenience alias for weather audio routing
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

func _on_achievement_unlocked(achievement_name: String, description: String, reward_nok: float, tier: String) -> void:
	latest_achievement_name = achievement_name
	latest_achievement_description = description
	achievement_unlocked_count = _game_bridge.AchievementUnlockedCount
	achievement_unlocked.emit(achievement_name, description, reward_nok, tier)

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

	# Fetch latest events from C# (returns Array[Dictionary])
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

# ── Public control API ────────────────────────────────────────────────────────

## Start a new game. GameController calls this on scene ready.
func start_new_game(player_name: String = "Spiller", starting_capital: int = 10_000_000) -> void:
	if _game_bridge:
		_game_bridge.StartNewGame(player_name, starting_capital)
	else:
		push_warning("SimBridge.start_new_game: no GameBridge available")

## Advance simulation by 1 day.
func advance_day() -> void:
	if _game_bridge:
		_game_bridge.AdvanceDays(1)

## Advance simulation by 7 days.
func advance_week() -> void:
	if _game_bridge:
		_game_bridge.AdvanceDays(7)

## Advance simulation by 30 days.
func advance_month() -> void:
	if _game_bridge:
		_game_bridge.AdvanceDays(30)

## Pause or resume the auto-tick timer.
func set_paused(paused: bool) -> void:
	if _game_bridge:
		_game_bridge.SetPaused(paused)

## Set auto-tick speed multiplier (1.0 = 1 day/second).
func set_time_speed(speed: float) -> void:
	if _game_bridge:
		_game_bridge.SetTimeSpeed(speed)

# ── Formatting helpers (UI convenience) ──────────────────────────────────────

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

## Return the season name for a given simulation day (Norwegian calendar).
## Spring: Mar–May (day 60–151), Summer: Jun–Aug (152–243),
## Autumn: Sep–Nov (244–334), Winter: Dec–Feb (0–59, 335+)
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
		_:                      return Color(0.9, 0.2, 0.1) # NonCompliant

## Return a color for a sea lice level.
func lice_color(lice_per_fish: float) -> Color:
	if lice_per_fish > 5.0:  return Color.RED
	elif lice_per_fish > 2.0: return Color.ORANGE
	elif lice_per_fish > 0.5: return Color.YELLOW
	return Color.GREEN

## Return a color for balance (red if negative).
func balance_color(amount: float) -> Color:
	if amount < 0.0:     return Color.RED
	elif amount < 500_000.0: return Color.YELLOW
	return Color.WHITE

# ── Production Chain API ──────────────────────────────────────────────────────

## Get count of fish at a specific life stage (Egg, Fry, Smolt, PostSmolt, Adult, Broodstock, Harvested)
func get_fish_count_by_stage(stage_name: String) -> int:
	return _game_bridge.GetFishCountByStage(stage_name)

## Get biomass (kg) at a specific life stage
func get_biomass_by_stage(stage_name: String) -> float:
	return _game_bridge.GetBiomassByStage(stage_name)

## Whether the player owns a hatchery
func owns_hatchery() -> bool:
	return _game_bridge.OwnsHatchery

## Whether the player owns a processing plant
func owns_processing_plant() -> bool:
	return _game_bridge.OwnsProcessingPlant

## Number of export markets unlocked
func get_export_market_count() -> int:
	return _game_bridge.ExportMarketCount

## Total processing throughput (kg)
func get_processing_throughput_kg() -> float:
	return _game_bridge.ProcessingThroughputKg

# ── Contract & Market API ─────────────────────────────────────────────────────

## Get list of active contracts.
## Returns Array[Dictionary] with keys: "id", "buyer", "type", "price_per_kg", "quantity_kg", "quality", "penalty_pct", "fulfilled_kg", "fulfilled_percent"
func get_active_contracts() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetActiveContracts()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get list of available contract offers (generated from market conditions).
## Returns Array[Dictionary] with keys: "buyer", "type", "price_per_kg", "quantity_kg", "quality", "penalty_pct"
func get_available_offers() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAvailableOffers()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get list of export markets with status and prices.
## Returns Array[Dictionary] with keys: "region", "name", "base_price_nok", "premium_multiplier", "player_has_access", "logistics_cost_pct", "tariff_rate", "min_reputation_score", "required_certifications"
func get_export_markets() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetExportMarkets()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

# ── Analytics API ─────────────────────────────────────────────────────────────

## Get time-series data for a metric over N days (returns Array[double]).
## Valid metrics: fish_count, total_biomass_kg, market_price_nok, balance_nok,
## temperature_c, fcr, lice_per_fish, sustainability_score, mortality_rate, avg_weight_kg
func get_analytics_data(metric: String, days: int = 30) -> Array[float]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAnalyticsData(metric, days)
	var result: Array[float] = []
	for v in raw:
		result.append(v)
	return result

## Get moving average of a metric over N days with a rolling window.
func get_analytics_moving_average(metric: String, days: int = 30, window_days: int = 7) -> Array[float]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAnalyticsMovingAverage(metric, days, window_days)
	var result: Array[float] = []
	for v in raw:
		result.append(v)
	return result

## Get trend for a metric over N days. Returns "up", "down", or "stable".
func get_analytics_trend(metric: String, days: int = 14) -> String:
	if not _game_bridge: return "stable"
	return _game_bridge.GetAnalyticsTrend(metric, days)

## Get all-time high value for a metric.
func get_analytics_all_time_high(metric: String) -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.GetAnalyticsAllTimeHigh(metric)

## Get all-time low value for a metric.
func get_analytics_all_time_low(metric: String) -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.GetAnalyticsAllTimeLow(metric)

# ── Research API ──────────────────────────────────────────────────────────────

## Get all research nodes as Array[Dictionary].
## Keys: id, name, description, branch, tier, cost_nok, research_days,
##       prerequisites (Array[String]), unlocked (bool), is_active (bool),
##       progress_days (int), can_research (bool), effects (Dictionary)
func get_research_nodes() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetResearchNodes()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Start researching a node by ID. Returns true on success.
func start_research(node_id: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.StartResearch(node_id)

## Cancel the active research (no cost refund).
func cancel_research() -> void:
	if _game_bridge:
		_game_bridge.CancelResearch()

## Check if a research node is unlocked.
func is_research_unlocked(node_id: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.IsResearchUnlocked(node_id)

## Get combined effect value for a key across all unlocked research.
func get_research_effect(effect_key: String) -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.GetResearchEffect(effect_key)

## ID of the research currently in progress (empty string if none).
func get_active_research_id() -> String:
	if not _game_bridge: return ""
	return _game_bridge.ActiveResearchId

## Days elapsed on the active research.
func get_research_progress() -> int:
	if not _game_bridge: return 0
	return _game_bridge.ResearchProgress

## Days remaining on the active research.
func get_research_days_remaining() -> int:
	if not _game_bridge: return 0
	return _game_bridge.ResearchDaysRemaining

# ── Scenario System API ───────────────────────────────────────────────────────

## Get all available scenarios as Array[Dictionary].
## Keys: id, name, description, difficulty, region, starting_capital
func get_available_scenarios() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAvailableScenarios()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Load and start a scenario by ID. Returns true on success.
func start_scenario(scenario_id: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.StartScenario(scenario_id)

## Get the ID of the currently active scenario.
func get_active_scenario_id() -> String:
	if not _game_bridge: return ""
	return _game_bridge.GetActiveScenarioId()

# ── Sustainability API (expanded) ─────────────────────────────────────────────

## Get detailed sustainability breakdown as Dictionary.
## Keys: sea_lice, escape, feed, carbon, welfare, ecosystem (all float)
func get_sustainability_breakdown() -> Dictionary:
	if not _game_bridge: return {}
	return _game_bridge.GetSustainabilityBreakdown()

## Get the current certification level as a string.
## Values: NonCompliant, Compliant, ASCCertified, OrganicCertified
func get_certification_level() -> String:
	if not _game_bridge: return "NonCompliant"
	return _game_bridge.GetCertificationLevel()

## Get the current price multiplier from certification (e.g. 1.0 = no bonus, 1.15 = +15%).
func get_price_multiplier() -> float:
	if not _game_bridge: return 1.0
	return _game_bridge.GetPriceMultiplier()

# ── Finance API ───────────────────────────────────────────────────────────────

## Total revenue accumulated this period.
func get_revenue() -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.Revenue

## Total expenses accumulated this period.
func get_expenses() -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.Expenses

## Current profit (Revenue - Expenses).
func get_profit() -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.Profit

## Get financial history for the last N days as Array[Dictionary].
## Keys: day (int), revenue (float), expenses (float), balance (float)
func get_financial_history(days: int) -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetFinancialHistory(days)
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get loan information as Dictionary.
## Keys: has_loan (bool), amount (float), interest_rate (float), remaining (float)
func get_loan_info() -> Dictionary:
	if not _game_bridge: return {}
	return _game_bridge.GetLoanInfo()

## Take out a loan for the specified amount. Returns true on success.
func take_loan(amount: float) -> bool:
	if not _game_bridge: return false
	return _game_bridge.TakeLoan(amount)

## Repay a portion of the active loan. Returns true on success.
func repay_loan(amount: float) -> bool:
	if not _game_bridge: return false
	return _game_bridge.RepayLoan(amount)

# ── Farm Management API ───────────────────────────────────────────────────────

## Stock smolts at a specific sea site. Returns true on success.
func stock_smolts(site_id: String, count: int) -> bool:
	if not _game_bridge: return false
	return _game_bridge.StockSmolts(site_id, count)

## Harvest all fish at a specific sea site. Returns biomass harvested in kg.
func harvest_site(site_id: String) -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.HarvestSite(site_id)

## Build a new sea site in the specified zone. Returns true on success.
func build_sea_site(zone_id: String, name: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.BuildSeaSite(zone_id, name)

## Get detailed information about all sea sites as Array[Dictionary].
## Keys: id, name, zone, fish_count (int), biomass_kg (float), max_biomass (float), escapes_this_year (int)
func get_sea_site_details() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetSeaSiteDetails()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Order a delousing treatment for a specific sea site.
## Valid methods: mechanical, thermal, chemical, biological. Returns true on success.
func order_delousing(site_id: String, method: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.OrderDelousing(site_id, method)

# ── Genetics API ──────────────────────────────────────────────────────────────

## Get all broodstock genomes as Array[Dictionary].
## Keys: id, generation (int), traits (Dictionary), has_mutation (bool), summary (String)
func get_breeding_candidates() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetBreedingCandidates()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get genetic trait definitions as Array[Dictionary].
## Keys: name (String), heritability (float), current_value (float), breeding_value (float)
func get_genetic_traits() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetGeneticTraits()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Get current inbreeding coefficient (0.0-1.0).
func get_inbreeding_coefficient() -> float:
	if not _game_bridge: return 0.0
	return _game_bridge.GetInbreedingCoefficient()

# ── Worker/Crew API ───────────────────────────────────────────────────────────

## Get the crew roster as Array[Dictionary].
## Keys: id, name, role, salary_nok (float), skill_level (int), hire_date (int)
func get_crew_list() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetCrewList()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result

## Hire a new worker with the specified role. Returns true on success.
## Valid roles: manager, technician, veterinarian, diver, feeder
func hire_worker(role: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.HireWorker(role)

## Fire a worker by ID. Returns true on success.
func fire_worker(worker_id: String) -> bool:
	if not _game_bridge: return false
	return _game_bridge.FireWorker(worker_id)

# ── Achievement API ───────────────────────────────────────────────────────────

## Get all achievements as Array[Dictionary].
## Keys: id, name, description, tier, reward_nok (float), unlocked (bool)
func get_achievement_list() -> Array[Dictionary]:
	if not _game_bridge: return []
	var raw = _game_bridge.GetAchievementList()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result
