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

## Most recent events (up to 10), each a Dictionary with keys:
## "day", "name", "category", "description", "balance_change"
var recent_events: Array[Dictionary] = []

## Latest alert messages (cleared when state_updated fires again)
var active_alerts: Array[Dictionary] = []

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
