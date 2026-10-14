class_name SimState
extends Node

## SimState — simulation snapshot owned by SimBridge.
## Holds every snapshot var plus _refresh_state() and _check_alerts().

const Formatters = preload("res://scripts/bridge/SimFormatters.gd")
const Labels = preload("res://scripts/bridge/SimLabels.gd")

## The FCR alarm waits until the fish have settled (roadmap U1-08): smolts
## below this mean weight, or stocked fewer days ago than FCR_SETTLE_DAYS,
## convert feed poorly by nature and are not a feeding problem.
const FCR_SETTLE_WEIGHT_KG := 0.5
const FCR_SETTLE_DAYS := 30
## Lice limits mirror the engine: treatment considered at 0.2, legal limit 0.5.
const LICE_TREATMENT_LIMIT := 0.2
const LICE_LEGAL_LIMIT := 0.5

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted every time the simulation state snapshot is refreshed.
signal state_updated()

## Emitted each simulated day with the new day number and season name.
signal day_advanced(day: int, season: String)

## Emitted when a KPI crosses an alert threshold.
signal alert_raised(level: String, message: String)

## Emitted when the player completes a tutorial step.
signal tutorial_step_completed(step_number: int, title: String)

## Emitted when an audio alert is triggered (one-shot SFX).
signal audio_alert_triggered(alert_key: String)

## Emitted when the player unlocks a new achievement.
signal achievement_unlocked(id: String, achievement_name: String, description: String, medal: String, tier: String)

# ── Simulation snapshot (refreshed on each tick) ──────────────────────────────

var current_day: int = 0
## Sim weather regime: Calm / Moderate / Storm / Severe (drives 3D sky + waves).
var weather_state: String = "Calm"
## Season 0=winter, 0.25=spring, 0.5=summer, 0.75=autumn (drives 3D snow line).
var season01: float = 0.0
var balance: float = 0.0
## Bankruptcy floor of the active scenario (NOK, negative) and the warning rung
## today's cash sits on: 0 solvent, 1 overdraft, 2 bank warning, 3 final (S1-03).
var bankruptcy_floor: float = -1_000_000.0
var solvency_level: int = 0
var fish_count: int = 0
var fish_biomass_kg: float = 0.0
var temperature_c: float = 0.0
var market_price_nok: float = 70.0
var weekly_avg_price_nok: float = 70.0
var sea_lice_per_fish: float = 0.0
var fcr: float = 0.0
## FCR goal and failing ceiling from CalibrationRegistry (one target, U1-03).
var fcr_target: float = 0.0
var fcr_poor: float = 0.0
## Days since the latest stocking (-1 unknown); see fcr_is_settled().
var days_since_stocking: int = -1
## Site id -> the name the player reads.
var site_names: Dictionary = {}
## The player's company (dashboard title).
var company_name: String = ""
var appetite: float = 1.0
var avg_weight_kg: float = 0.0
var mortality_rate: float = 0.0
var oxygen_mgl: float = 0.0
## mg/L the first zone's water holds at 100% saturation (sim's seawater solubility).
var oxygen_saturation_mgl: float = 0.0
var water_ph: float = 7.8
var wind_ms: float = 0.0
var wave_m: float = 0.0
var min_net_integrity: float = 100.0
var storm_risk_3d: float = 0.0
var red_zone_count: int = 0
## Vaccination coverage 0-1 + October window days left (-1 closed).
var vaccine_coverage: float = 0.0
var vaccine_window_left: int = -1
## Night-shift ROC policy (Phase 5.2).
var night_shift_policy: String = "Standard"
## Active blooms (id/kind/site_id/severity/days_left).
var active_blooms: Array[Dictionary] = []
## Pending wellboat jobs (id/site_id/kind/method/eta_days).
var wellboat_jobs: Array[Dictionary] = []
## Live traffic-light status per zone (zone/color/mab_factor/capacity_tonnes).
var traffic_zones: Array[Dictionary] = []
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

## Alerts in force: event alerts (newest first), then the conditions that
## hold now. Each entry: {key, level, message} (+ day for events).
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
var tutorial_total_steps: int = 0

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

## C# GameBridge node, injected by the SimBridge facade.
var game_bridge: Node = null

## Event alerts (weather, disease, escape) stay listed this many days.
const EVENT_ALERT_TTL_DAYS := 3
## Level of each condition alert at the previous refresh, by key.
var _alert_levels: Dictionary = {}
## Levels raised during the refresh in progress.
var _alert_levels_now: Dictionary = {}
## Event alerts, oldest first; a refresh keeps them until their TTL.
var _event_alerts: Array[Dictionary] = []

# ── GameBridge signal handlers (invoked by the SimBridge facade) ─────────────

func _on_game_started() -> void:
	# A new or loaded game announces its conditions afresh.
	_alert_levels = {}
	_event_alerts.clear()
	_refresh_state()


func _on_game_state_updated() -> void:
	_refresh_state()


func _on_simulation_ticked(day: int) -> void:
	current_day = day
	_refresh_state()


func _on_game_ended(reason: String) -> void:
	# reason is a translation key when the engine ended the game (S1-03);
	# tr() leaves any other text as it is.
	alert_raised.emit("critical", tr("ALERT_GAME_OVER") % tr(reason))


func _on_tutorial_step_completed(step_number: int, title: String) -> void:
	tutorial_step_completed.emit(step_number, title)


func _on_audio_alert_triggered(alert_key: String) -> void:
	audio_alert_layer = alert_key
	audio_alert_triggered.emit(alert_key)


func _on_achievement_unlocked(id: String, achievement_name: String, description: String, medal: String, tier: String) -> void:
	# The sim names achievements in English (achievements_config.json): show
	# them in the UI language (ACHIEVEMENT_<ID>_NAME / _DESC).
	var key := "ACHIEVEMENT_" + SimLabels.upper_snake(id)
	achievement_name = _tr_or(key + "_NAME", achievement_name)
	description = _tr_or(key + "_DESC", description)
	latest_achievement_name = achievement_name
	latest_achievement_description = description
	achievement_unlocked_count = game_bridge.AchievementUnlockedCount
	achievement_unlocked.emit(id, achievement_name, description, medal, tier)

# ── State refresh ─────────────────────────────────────────────────────────────

func _refresh_state() -> void:
	if not game_bridge:
		return

	current_day          = game_bridge.CurrentDay
	if game_bridge.has_method("GetCurrentWeatherState"):
		weather_state = str(game_bridge.GetCurrentWeatherState())
	# Day 0 = Jan 1: winter 0, spring 0.25, summer 0.5, autumn 0.75.
	season01 = fmod(float(current_day) / 365.0, 1.0)
	balance              = game_bridge.Balance
	bankruptcy_floor     = game_bridge.BankruptcyFloorNok
	solvency_level       = game_bridge.SolvencyWarningLevel
	fish_count           = game_bridge.TotalFishCount
	fish_biomass_kg      = game_bridge.TotalBiomassKg
	temperature_c        = game_bridge.CurrentTemperatureC
	market_price_nok     = game_bridge.MarketPricePerKg
	weekly_avg_price_nok = game_bridge.WeeklyAvgPricePerKg
	sea_lice_per_fish    = game_bridge.SeaLicePerFish
	fcr                  = game_bridge.CurrentFCR
	var fcr_targets: Dictionary = game_bridge.GetFcrTargets()
	fcr_target           = float(fcr_targets.get("target", 0.0))
	fcr_poor             = float(fcr_targets.get("poor", 0.0))
	days_since_stocking  = game_bridge.DaysSinceStocking
	site_names           = game_bridge.GetSiteNames()
	company_name         = game_bridge.CompanyName
	appetite             = game_bridge.Appetite01
	avg_weight_kg        = game_bridge.AvgWeightKg
	mortality_rate       = game_bridge.MortalityRateToday
	oxygen_mgl           = game_bridge.ZoneOxygenMgL
	oxygen_saturation_mgl = game_bridge.ZoneOxygenSaturationMgL
	water_ph             = game_bridge.ZonePh
	wind_ms              = game_bridge.WindSpeedMs
	wave_m               = game_bridge.WaveHeightM
	min_net_integrity    = game_bridge.MinNetIntegrity
	storm_risk_3d        = game_bridge.StormRisk3Days
	red_zone_count       = game_bridge.RedZoneCount
	vaccine_coverage     = game_bridge.VaccineCoverage
	vaccine_window_left  = game_bridge.VaccineWindowDaysLeft
	night_shift_policy   = game_bridge.NightShiftPolicy
	sustainability_score = game_bridge.SustainabilityScore
	sustainability_level = game_bridge.SustainabilityLevel
	zone_count           = game_bridge.ZoneCount
	site_count           = game_bridge.SiteCount

	external_warning_count           = game_bridge.ExternalWarningCount
	external_composite_risk          = game_bridge.ExternalCompositeRisk
	external_crisis_active           = game_bridge.ExternalCrisisActive
	external_crisis_label            = game_bridge.ExternalCrisisLabel
	external_snapshot_source         = game_bridge.ExternalSnapshotSource
	external_snapshot_freshness_days = game_bridge.ExternalSnapshotFreshnessDays

	# Tutorial state
	tutorial_step_number    = game_bridge.TutorialStepNumber
	tutorial_step_title     = game_bridge.TutorialStepTitle
	tutorial_description    = game_bridge.TutorialDescription
	tutorial_hint           = game_bridge.TutorialHint
	tutorial_is_complete    = game_bridge.TutorialIsComplete
	tutorial_completed_count = game_bridge.TutorialCompletedCount
	tutorial_total_steps     = game_bridge.TutorialTotalSteps

	# Fetch latest events from C#
	var raw = game_bridge.GetRecentEvents(10)
	recent_events.clear()
	for entry in raw:
		recent_events.append(entry)

	# Traffic-light zones (small list; refreshed with state)
	traffic_zones.clear()
	for z in game_bridge.GetZoneTrafficLights():
		traffic_zones.append(z)

	# Wellboat queue (small list; refreshed with state)
	wellboat_jobs.clear()
	for j in game_bridge.GetWellboatJobs():
		wellboat_jobs.append(j)

	# Active blooms (small list; refreshed with state)
	active_blooms.clear()
	for b in game_bridge.GetActiveBlooms():
		active_blooms.append(b)

	_rebuild_alerts()

	# Achievement state
	achievement_unlocked_count = game_bridge.AchievementUnlockedCount
	achievement_total_count    = game_bridge.AchievementTotalCount

	# Audio state
	audio_base_track     = game_bridge.AudioBaseTrack
	audio_weather_layer  = game_bridge.AudioWeatherLayer
	audio_alert_layer    = game_bridge.AudioAlertLayer
	audio_volume         = game_bridge.AudioVolume
	audio_seasonal_track = game_bridge.AudioSeasonalTrack
	audio_weather_state  = game_bridge.CurrentWeatherState

	# Empire Systems state (T65)
	competitor_count             = game_bridge.GetCompetitorCount()
	var leader = game_bridge.GetMarketLeader()
	market_leader_name           = leader.get("name", "") if leader else ""
	empire_phase                 = game_bridge.GetEmpirePhase()
	empire_score                 = game_bridge.GetEmpireScore()
	growth_strategy              = game_bridge.GetGrowthStrategy()
	ownership_status             = game_bridge.GetOwnershipStatus()
	share_price                  = game_bridge.GetSharePrice()
	market_cap                   = game_bridge.GetMarketCap()
	is_public                    = game_bridge.IsPublic()
	brand_score                  = game_bridge.GetBrandScore()
	brand_strategy               = game_bridge.GetBrandStrategy()
	pricing_power                = game_bridge.GetPricingPower()
	active_crises_count          = game_bridge.GetActiveCrises().size()
	acquisition_opportunities_count = game_bridge.GetAcquisitionOpportunities().size()
	integration_score            = game_bridge.GetIntegrationScore()
	owned_assets_count           = game_bridge.GetOwnedAssets().size()

	state_updated.emit()
	day_advanced.emit(current_day, Formatters.season_name(current_day))

# ── Alert logic ───────────────────────────────────────────────────────────────

func _check_alerts() -> void:
	var lice_txt := Formatters.num(sea_lice_per_fish, 2)
	if sea_lice_per_fish > LICE_LEGAL_LIMIT:
		if _raise("lice", "critical", tr("ALERT_LICE_CRITICAL") % [lice_txt, Formatters.num(LICE_LEGAL_LIMIT, 1)]):
			audio_alert_triggered.emit("lice_warning")
	elif sea_lice_per_fish > LICE_TREATMENT_LIMIT:
		_raise("lice", "warning", tr("ALERT_LICE_HIGH") % [lice_txt, Formatters.num(LICE_LEGAL_LIMIT, 1)])

	if fcr_poor > 0.0 and fcr > fcr_poor and fcr_is_settled():
		_raise("fcr", "warning", tr("ALERT_FCR_POOR") % [Formatters.num(fcr, 2), Formatters.num(fcr_target, 2)])

	if appetite < 0.6:
		_raise("appetite", "warning", tr("ALERT_APPETITE_LOW") % Formatters.percent(appetite))

	if mortality_rate > 0.01:
		_raise("mortality", "warning", tr("ALERT_MORTALITY") % Formatters.percent(mortality_rate, 1))

	if oxygen_mgl > 0.0 and oxygen_mgl < 5.0:
		_raise("oxygen", "critical", tr("ALERT_OXYGEN_CRITICAL") % Formatters.num(oxygen_mgl, 1))
	elif oxygen_mgl >= 5.0 and oxygen_mgl < 6.5:
		_raise("oxygen", "warning", tr("ALERT_OXYGEN_LOW") % Formatters.num(oxygen_mgl, 1))

	if min_net_integrity < 40.0:
		_raise("nets", "critical", tr("ALERT_NETS_CRITICAL") % Formatters.percent(min_net_integrity / 100.0))
	elif min_net_integrity < 70.0:
		_raise("nets", "warning", tr("ALERT_NETS_WORN") % Formatters.percent(min_net_integrity / 100.0))

	if storm_risk_3d > 0.5:
		_raise("storm_risk", "warning", tr("ALERT_STORM_RISK") % Formatters.percent(storm_risk_3d))

	if red_zone_count > 0:
		_raise("red_zones", "warning", tr("ALERT_RED_ZONES") % red_zone_count)

	if vaccine_window_left >= 0:
		if vaccine_coverage < 0.9:
			_raise("vaccine_window", "warning", tr("ALERT_VACCINE_WINDOW") % [vaccine_window_left, Formatters.percent(vaccine_coverage)])
	elif vaccine_coverage < 0.5 and current_day > 300:
		_raise("vaccine_winter", "info", tr("ALERT_VACCINE_WINTER") % Formatters.percent(vaccine_coverage))

	for j in wellboat_jobs:
		_raise("wellboat:%s:%s" % [str(j.get("site_id", "?")), str(j.get("kind", "?"))], "info",
			"🚢 " + tr("ALERT_WELLBOAT") % [Labels.wellboat_job(str(j.get("kind", ""))),
				site_name(str(j.get("site_id", ""))), int(j.get("eta_days", 0))])

	for b in active_blooms:
		var kind_icon: String = {"jellyfish": "🪼", "algae": "🟤", "hypoxia": "💨"}.get(str(b.get("kind", "")), "⚠️")
		_raise("bloom:%s:%s" % [str(b.get("site_id", "?")), str(b.get("kind", "?"))],
			"critical" if float(b.get("severity", 0.0)) > 0.7 else "warning",
			"%s %s" % [kind_icon, tr("ALERT_BLOOM") % [Labels.bloom(str(b.get("kind", ""))),
				site_name(str(b.get("site_id", ""))), int(b.get("days_left", 0))]])

	# Bankruptcy warning ladder (S1-03): one alert per rung, announced when the
	# rung is reached; the engine owns the floor and the verdict.
	if solvency_level > 0:
		_raise("solvency:%d" % solvency_level, "critical" if solvency_level >= 2 else "warning",
			_solvency_message(solvency_level))

	if sustainability_score < 50:
		_raise("sustainability", "warning", tr("ALERT_SUSTAINABILITY_LOW") % sustainability_score)

	if temperature_c > 18.0:
		_raise("temperature", "warning", tr("ALERT_TEMPERATURE_HIGH") % (Formatters.num(temperature_c, 1) + " °C"))


## False while the FCR says nothing about feeding (roadmap U1-08): no fish,
## smolts under FCR_SETTLE_WEIGHT_KG, or a stocking within FCR_SETTLE_DAYS.
## The day-12 dashboard showed "Kritisk tilstand" for an FCR of 2.4 on
## 0.1 kg smolts.
func fcr_is_settled() -> bool:
	if fish_count <= 0 or avg_weight_kg < FCR_SETTLE_WEIGHT_KG:
		return false
	return days_since_stocking < 0 or days_since_stocking >= FCR_SETTLE_DAYS


## The name the player reads for a site id ("all_sites" → "alle lokaliteter").
func site_name(site_id: String) -> String:
	if site_id == "all_sites":
		return tr("SITE_ALL")
	if site_names.has(site_id):
		return str(site_names[site_id])
	return Labels.humanize(site_id)


## Norwegian ladder message: rung, today's cash and the scenario's floor.
func _solvency_message(level: int) -> String:
	var key: String = ["UI_SOLVENCY_OVERDRAFT", "UI_SOLVENCY_BANK_WARNING", "UI_SOLVENCY_FINAL_WARNING"][clampi(level, 1, 3) - 1]
	var title: String = tr(key)
	if title == key:
		title = ["Overtrukket konto", "Bankvarsel", "Siste varsel fra banken"][clampi(level, 1, 3) - 1]
	var fmt: String = tr("UI_SOLVENCY_DESC")
	if fmt == "UI_SOLVENCY_DESC" or fmt.count("%s") < 2:
		fmt = "Kontanter %s. Konkurs under %s."
	return "%s: %s" % [title, fmt % [Formatters.format_nok(balance), Formatters.format_nok(bankruptcy_floor)]]


## Rebuild the alert list: event alerts still within their TTL, then the
## conditions that hold now.
func _rebuild_alerts() -> void:
	_event_alerts = _event_alerts.filter(
		func(a: Dictionary) -> bool: return current_day - int(a["day"]) < EVENT_ALERT_TTL_DAYS)
	active_alerts.clear()
	for i in range(_event_alerts.size() - 1, -1, -1):
		active_alerts.append(_event_alerts[i])
	_alert_levels_now = {}
	_check_alerts()
	_alert_levels = _alert_levels_now


## A condition alert: listed while it holds, announced (alert_raised) only
## when it starts or changes level. Every refresh (two per tick) used to
## announce every live condition, and the messages carry daily values, so
## the notification center flooded, the lice SFX replayed and auto-pause
## re-fired. Returns true when announced.
func _raise(key: String, level: String, message: String) -> bool:
	active_alerts.append({"key": key, "level": level, "message": message})
	var announce: bool = _alert_levels.get(key, "") != level
	_alert_levels_now[key] = level
	if announce:
		alert_raised.emit(level, message)
	return announce


## An event alert (weather, disease, escape): announced once, listed for
## EVENT_ALERT_TTL_DAYS (a refresh used to wipe it at once). category and
## site say what it is about, so lists can show one row per (category, site).
func _push_alert(level: String, message: String, category: String = "event", site: String = "") -> void:
	# The engine's day: weather events arrive before the tick's refresh, when
	# current_day still holds yesterday, which cut the TTL to two days.
	var day: int = int(game_bridge.CurrentDay) if game_bridge else current_day
	var alert := {"key": "event:%d:%s" % [day, message], "level": level,
		"message": message, "day": day, "category": category, "site": site}
	_event_alerts.append(alert)
	active_alerts.push_front(alert)
	alert_raised.emit(level, message)


func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback
