extends Node

## SimBridge — GDScript autoload singleton (thin facade).
## Owns SimState / SimActions / SimProxies child nodes under scripts/bridge/
## and re-emits their signals, so ALL existing callers keep working with
## zero API change. GameBridge.cs and every caller are untouched.

# ── Signals (unchanged public API) ────────────────────────────────────────────

## Emitted every time the simulation state snapshot is refreshed.
signal state_updated()

## Emitted each simulated day with the new day number and season name.
signal day_advanced(day: int, season: String)

## Emitted when a KPI crosses an alert threshold.
signal alert_raised(level: String, message: String)

## Emitted when the player completes a tutorial step (steps pay no cash, D16).
signal tutorial_step_completed(step_number: int, title: String)

## Emitted when an audio alert is triggered (one-shot SFX).
signal audio_alert_triggered(alert_key: String)

## Emitted when the player unlocks a new achievement. medal: "bronze" | "silver"
## | "gold" | "platinum" (achievements award medals, never cash, D16).
signal achievement_unlocked(id: String, achievement_name: String, description: String, medal: String, tier: String)

## Emitted when a pen action completes (feed, harvest, delouse)
signal pen_action_completed(action: String, pen_id: String, success: bool)

## Emitted for every event the typed event bus delivers (roadmap U2-06):
## {id, type, severity "info" | "warning" | "critical", category, key, day,
## site_id, zone_id, unit_id, variant, headline, message, deadline_day,
## counter_verb, value, escalation}. The bus has already dropped repeats
## (same category and subject within three days) and marked escalations;
## listeners route by severity, never by the message text.
signal game_event_raised(event: Dictionary)

## Emitted when a game's verdict lands (roadmap U2-03): "won" when the
## scenario's goal is met (the game may go on), "lost" or "bankrupt" when the
## game ended. The end screen reads the rest from get_scenario_end_report().
signal scenario_ended(verdict: String)

## Emitted whenever the game clock changes (roadmap U2-07): speed, pause,
## holds, "Til neste hendelse" or a pacing decision. See get_clock_state().
signal clock_changed(clock: Dictionary)

# ── Internal ──────────────────────────────────────────────────────────────────

var _game_bridge: Node = null

const State = preload("res://scripts/bridge/SimState.gd")
const Actions = preload("res://scripts/bridge/SimActions.gd")
const Proxies = preload("res://scripts/bridge/SimProxies.gd")
const Formatters = preload("res://scripts/bridge/SimFormatters.gd")

var _state: State
var _actions: Actions
var _proxies: Proxies

# ── Snapshot properties (forwarded to SimState; zero API change) ─────────────

var current_day: int:
	get(): return _state.current_day
	set(v): _state.current_day = v
var balance: float:
	get(): return _state.balance
	set(v): _state.balance = v
var bankruptcy_floor: float:
	get(): return _state.bankruptcy_floor
	set(v): _state.bankruptcy_floor = v
var solvency_level: int:
	get(): return _state.solvency_level
	set(v): _state.solvency_level = v
var fish_count: int:
	get(): return _state.fish_count
	set(v): _state.fish_count = v
var fish_biomass_kg: float:
	get(): return _state.fish_biomass_kg
	set(v): _state.fish_biomass_kg = v
var temperature_c: float:
	get(): return _state.temperature_c
	set(v): _state.temperature_c = v
var market_price_nok: float:
	get(): return _state.market_price_nok
	set(v): _state.market_price_nok = v
var weekly_avg_price_nok: float:
	get(): return _state.weekly_avg_price_nok
	set(v): _state.weekly_avg_price_nok = v
var sea_lice_per_fish: float:
	get(): return _state.sea_lice_per_fish
	set(v): _state.sea_lice_per_fish = v
var fcr: float:
	get(): return _state.fcr
	set(v): _state.fcr = v
## FCR goal and failing ceiling from CalibrationRegistry (read-only).
var fcr_target: float:
	get(): return _state.fcr_target
var fcr_poor: float:
	get(): return _state.fcr_poor
var days_since_stocking: int:
	get(): return _state.days_since_stocking
var company_name: String:
	get(): return _state.company_name
var appetite: float:
	get(): return _state.appetite
	set(v): _state.appetite = v
var avg_weight_kg: float:
	get(): return _state.avg_weight_kg
	set(v): _state.avg_weight_kg = v
var mortality_rate: float:
	get(): return _state.mortality_rate
	set(v): _state.mortality_rate = v
var oxygen_mgl: float:
	get(): return _state.oxygen_mgl
	set(v): _state.oxygen_mgl = v
var oxygen_saturation_mgl: float:
	get(): return _state.oxygen_saturation_mgl
	set(v): _state.oxygen_saturation_mgl = v
var water_ph: float:
	get(): return _state.water_ph
	set(v): _state.water_ph = v
var wind_ms: float:
	get(): return _state.wind_ms
	set(v): _state.wind_ms = v
var wave_m: float:
	get(): return _state.wave_m
	set(v): _state.wave_m = v
var weather_state: String:
	get(): return _state.weather_state
	set(v): _state.weather_state = v
var season01: float:
	get(): return _state.season01
	set(v): _state.season01 = v
var min_net_integrity: float:
	get(): return _state.min_net_integrity
	set(v): _state.min_net_integrity = v
var storm_risk_3d: float:
	get(): return _state.storm_risk_3d
	set(v): _state.storm_risk_3d = v
var night_shift_policy: String:
	get(): return _state.night_shift_policy
	set(v): _state.night_shift_policy = v
var traffic_zones: Array[Dictionary]:
	get(): return _state.traffic_zones
	set(v): _state.traffic_zones = v
var wellboat_jobs: Array[Dictionary]:
	get(): return _state.wellboat_jobs
	set(v): _state.wellboat_jobs = v
var active_blooms: Array[Dictionary]:
	get(): return _state.active_blooms
	set(v): _state.active_blooms = v
## The player's sea sites and their pens as of the last refresh (read-only;
## get_all_pens() asks the bridge afresh).
var sea_sites: Array[Dictionary]:
	get(): return _state.sea_sites
var sustainability_score: int:
	get(): return _state.sustainability_score
	set(v): _state.sustainability_score = v
var sustainability_level: String:
	get(): return _state.sustainability_level
	set(v): _state.sustainability_level = v
var zone_count: int:
	get(): return _state.zone_count
	set(v): _state.zone_count = v
var site_count: int:
	get(): return _state.site_count
	set(v): _state.site_count = v
var external_warning_count: int:
	get(): return _state.external_warning_count
	set(v): _state.external_warning_count = v
var external_composite_risk: float:
	get(): return _state.external_composite_risk
	set(v): _state.external_composite_risk = v
var external_crisis_active: bool:
	get(): return _state.external_crisis_active
	set(v): _state.external_crisis_active = v
var external_crisis_label: String:
	get(): return _state.external_crisis_label
	set(v): _state.external_crisis_label = v
var external_snapshot_source: String:
	get(): return _state.external_snapshot_source
	set(v): _state.external_snapshot_source = v
var external_snapshot_freshness_days: int:
	get(): return _state.external_snapshot_freshness_days
	set(v): _state.external_snapshot_freshness_days = v
var recent_events: Array[Dictionary]:
	get(): return _state.recent_events
	set(v): _state.recent_events = v
var active_alerts: Array[Dictionary]:
	get(): return _state.active_alerts
	set(v): _state.active_alerts = v
var achievement_unlocked_count: int:
	get(): return _state.achievement_unlocked_count
	set(v): _state.achievement_unlocked_count = v
var achievement_total_count: int:
	get(): return _state.achievement_total_count
	set(v): _state.achievement_total_count = v
var latest_achievement_name: String:
	get(): return _state.latest_achievement_name
	set(v): _state.latest_achievement_name = v
var latest_achievement_description: String:
	get(): return _state.latest_achievement_description
	set(v): _state.latest_achievement_description = v
var audio_base_track: String:
	get(): return _state.audio_base_track
	set(v): _state.audio_base_track = v
var audio_weather_layer: String:
	get(): return _state.audio_weather_layer
	set(v): _state.audio_weather_layer = v
var audio_alert_layer: String:
	get(): return _state.audio_alert_layer
	set(v): _state.audio_alert_layer = v
var audio_volume: float:
	get(): return _state.audio_volume
	set(v): _state.audio_volume = v
var audio_seasonal_track: String:
	get(): return _state.audio_seasonal_track
	set(v): _state.audio_seasonal_track = v
var audio_weather_state: String:
	get(): return _state.audio_weather_state
	set(v): _state.audio_weather_state = v
var tutorial_step_number: int:
	get(): return _state.tutorial_step_number
	set(v): _state.tutorial_step_number = v
var tutorial_step_title: String:
	get(): return _state.tutorial_step_title
	set(v): _state.tutorial_step_title = v
var tutorial_description: String:
	get(): return _state.tutorial_description
	set(v): _state.tutorial_description = v
var tutorial_hint: String:
	get(): return _state.tutorial_hint
	set(v): _state.tutorial_hint = v
var tutorial_is_complete: bool:
	get(): return _state.tutorial_is_complete
	set(v): _state.tutorial_is_complete = v
var tutorial_completed_count: int:
	get(): return _state.tutorial_completed_count
	set(v): _state.tutorial_completed_count = v
var tutorial_total_steps: int:
	get(): return _state.tutorial_total_steps
	set(v): _state.tutorial_total_steps = v
var competitor_count: int:
	get(): return _state.competitor_count
	set(v): _state.competitor_count = v
var market_leader_name: String:
	get(): return _state.market_leader_name
	set(v): _state.market_leader_name = v
var empire_phase: String:
	get(): return _state.empire_phase
	set(v): _state.empire_phase = v
var empire_score: int:
	get(): return _state.empire_score
	set(v): _state.empire_score = v
var growth_strategy: String:
	get(): return _state.growth_strategy
	set(v): _state.growth_strategy = v
var ownership_status: String:
	get(): return _state.ownership_status
	set(v): _state.ownership_status = v
var share_price: float:
	get(): return _state.share_price
	set(v): _state.share_price = v
var market_cap: float:
	get(): return _state.market_cap
	set(v): _state.market_cap = v
var is_public: bool:
	get(): return _state.is_public
	set(v): _state.is_public = v
var brand_score: float:
	get(): return _state.brand_score
	set(v): _state.brand_score = v
var brand_strategy: String:
	get(): return _state.brand_strategy
	set(v): _state.brand_strategy = v
var pricing_power: float:
	get(): return _state.pricing_power
	set(v): _state.pricing_power = v
var active_crises_count: int:
	get(): return _state.active_crises_count
	set(v): _state.active_crises_count = v
var acquisition_opportunities_count: int:
	get(): return _state.acquisition_opportunities_count
	set(v): _state.acquisition_opportunities_count = v
var integration_score: float:
	get(): return _state.integration_score
	set(v): _state.integration_score = v
var owned_assets_count: int:
	get(): return _state.owned_assets_count
	set(v): _state.owned_assets_count = v

# ── Godot lifecycle ───────────────────────────────────────────────────────────

func _ready() -> void:
	_state = State.new()
	_actions = Actions.new()
	_proxies = Proxies.new()
	add_child(_state)
	add_child(_actions)
	add_child(_proxies)
	_actions.state = _state
	_state.state_updated.connect(func() -> void: state_updated.emit())
	_state.day_advanced.connect(func(day: int, season: String) -> void: day_advanced.emit(day, season))
	_state.alert_raised.connect(func(level: String, message: String) -> void: alert_raised.emit(level, message))
	_state.tutorial_step_completed.connect(func(n: int, t: String) -> void: tutorial_step_completed.emit(n, t))
	_state.audio_alert_triggered.connect(func(k: String) -> void: audio_alert_triggered.emit(k))
	_state.achievement_unlocked.connect(func(id: String, an: String, d: String, m: String, tier: String) -> void: achievement_unlocked.emit(id, an, d, m, tier))
	_actions.pen_action_completed.connect(func(a: String, p: String, s: bool) -> void: pen_action_completed.emit(a, p, s))
	if has_node("/root/GameBridge"):
		_game_bridge = get_node("/root/GameBridge")
		_game_bridge.GameStateUpdated.connect(_on_game_state_updated)
		_game_bridge.SimulationTicked.connect(_on_simulation_ticked)
		_game_bridge.GameStarted.connect(_on_game_started)
		_game_bridge.GameEnded.connect(_on_game_ended)
		_game_bridge.TutorialStepCompleted.connect(_on_tutorial_step_completed)
		_game_bridge.AudioAlertTriggered.connect(_on_audio_alert_triggered)
		_game_bridge.AchievementUnlocked.connect(_on_achievement_unlocked)
		_game_bridge.WeatherEvent.connect(_on_weather_event)
		_game_bridge.DiseaseOutbreak.connect(_on_disease_outbreak)
		_game_bridge.GameEventRaised.connect(func(ev: Dictionary) -> void: game_event_raised.emit(ev))
		_game_bridge.ClockChanged.connect(func(clock: Dictionary) -> void: clock_changed.emit(clock))
		_game_bridge.ScenarioWon.connect(func(_id: String, _name: String) -> void: scenario_ended.emit("won"))
	else:
		push_warning("SimBridge: /root/GameBridge not found — stub mode active")
	_state.game_bridge = _game_bridge
	_actions.game_bridge = _game_bridge
	_proxies.game_bridge = _game_bridge

# ── GameBridge signal handlers (delegated to SimState) ───────────────────────

func _on_game_started() -> void: _state._on_game_started()
func _on_game_state_updated() -> void: _state._on_game_state_updated()
func _on_simulation_ticked(day: int) -> void: _state._on_simulation_ticked(day)
func _on_game_ended(reason: String) -> void:
	_state._on_game_ended(reason)
	# U2-03: the verdict the engine reached; a game ended by an error has none.
	var verdict := get_current_verdict()
	if verdict != "running":
		scenario_ended.emit(verdict)
func _on_tutorial_step_completed(step_number: int, title: String) -> void: _state._on_tutorial_step_completed(step_number, title)
func _on_audio_alert_triggered(alert_key: String) -> void: _state._on_audio_alert_triggered(alert_key)
func _on_achievement_unlocked(id: String, achievement_name: String, description: String, medal: String, tier: String) -> void: _state._on_achievement_unlocked(id, achievement_name, description, medal, tier)
func _on_weather_event(event_type: String, severity: float, description: String) -> void:
	_state._push_alert("critical" if severity >= 0.8 else "warning", tr("ALERT_WEATHER") % description, "weather")
func _on_disease_outbreak(disease_type: String, site_id: String, severity: float, treatment_cost: float) -> void:
	if disease_type == "Escape":
		_state._push_alert("critical", tr("ALERT_ESCAPE") % [_state.site_name(site_id), Formatters.format_nok(treatment_cost)],
			"escape", site_id)
	else:
		_state._push_alert("critical" if severity >= 0.5 else "warning", tr("ALERT_DISEASE") % [
			SimLabels.disease(disease_type), _state.site_name(site_id), Formatters.format_nok(treatment_cost)],
			"disease", site_id)

# ── Internal passthroughs (preserved for compatibility) ─────────────────────

func _refresh_state() -> void: _state._refresh_state()
func _check_alerts() -> void: _state._rebuild_alerts()
func _push_alert(level: String, message: String, category: String = "event", site: String = "") -> void: _state._push_alert(level, message, category, site)
func _season_name(day: int) -> String: return Formatters.season_name(day)

# ── Display helpers (roadmap U1-03 / U1-08) ───────────────────────────────────

## The name the player reads for a site id.
func site_name(site_id: String) -> String: return _state.site_name(site_id)
## False while the FCR says nothing about feeding (fresh smolts, no fish).
func fcr_is_settled() -> bool: return _state.fcr_is_settled()
func get_fcr_targets() -> Dictionary: return _proxies.get_fcr_targets()

# ── Actions (delegated to SimActions) ─────────────────────────────────────────

func start_new_game(player_name: String = "Player", starting_capital: int = 10_000_000, scenario_id: String = "default") -> bool: return _actions.start_new_game(player_name, starting_capital, scenario_id)
func start_qa_scenario(scenario_id: String, seed: int) -> bool: return _actions.start_qa_scenario(scenario_id, seed)
func start_qa_new_game(player_name: String, starting_capital: int, seed: int) -> bool: return _actions.start_qa_new_game(player_name, starting_capital, seed)
func spawn_qa_bloom(site_id: String, severity: float = 0.7, duration_days: int = 21) -> Dictionary: return _actions.spawn_qa_bloom(site_id, severity, duration_days)
func set_qa_pen_lice(site_id: String, lice_per_fish: float) -> Dictionary: return _actions.set_qa_pen_lice(site_id, lice_per_fish)
func clear_qa_blooms() -> Dictionary: return _actions.clear_qa_blooms()
func advance_day() -> void: _actions.advance_day()
func advance_days(days: int) -> void: _actions.advance_days(days)
func set_paused(paused: bool) -> void: _actions.set_paused(paused)
func is_paused() -> bool: return _actions.is_paused()
func set_time_speed(speed: float) -> void: _actions.set_time_speed(speed)
## The game clock (roadmap U2-07): speeds, holds, "Til neste hendelse", the
## briefing, and the deadline registration API (see GameBridge.Clock.cs).
func get_time_speed() -> float: return _actions.get_time_speed()
func is_player_paused() -> bool: return _actions.is_player_paused()
func hold_clock(reason: String) -> void: _actions.hold_clock(reason)
func release_clock(reason: String) -> void: _actions.release_clock(reason)
func is_clock_held(reason: String = "") -> bool: return _actions.is_clock_held(reason)
func run_to_next_event() -> bool: return _actions.run_to_next_event()
func stop_run_to_next_event() -> void: _actions.stop_run_to_next_event()
func is_running_to_next_event() -> bool: return _actions.is_running_to_next_event()
func get_clock_state() -> Dictionary: return _actions.get_clock_state()
func is_briefing_pending() -> bool: return _actions.is_briefing_pending()
func begin_play(speed: float) -> void: _actions.begin_play(speed)
func dismiss_briefing() -> void: _actions.dismiss_briefing()
func set_auto_slow_deadlines(enabled: bool) -> void: _actions.set_auto_slow_deadlines(enabled)
func register_pacing_deadline(key: String, deadline_day: int, headline: String = "", site_id: String = "", counter_verb: String = "none", slow_at_days_left: int = 7, pause_at_days_left: int = 1) -> bool: return _actions.register_pacing_deadline(key, deadline_day, headline, site_id, counter_verb, slow_at_days_left, pause_at_days_left)
func resolve_pacing_deadline(key: String) -> bool: return _actions.resolve_pacing_deadline(key)
func get_pacing_deadlines() -> Array[Dictionary]: return _actions.get_pacing_deadlines()
func skip_tutorial() -> void: _actions.skip_tutorial()
func get_game_state_json() -> String: return _actions.get_game_state_json()
func place_pen(zone_id: String, pen_name: String = "New Pen") -> bool: return _actions.place_pen(zone_id, pen_name)
func set_feed_plan(site_id: String, ration_pct: float, recipe: String, window_days: int) -> Dictionary: return _actions.set_feed_plan(site_id, ration_pct, recipe, window_days)
func set_feed_plan_all(ration_pct: float, recipe: String, window_days: int) -> Dictionary: return _actions.set_feed_plan_all(ration_pct, recipe, window_days)
func harvest_pen(pen_id: String) -> float: return _actions.harvest_pen(pen_id)
func delouse_pen(pen_id: String, method: String = "mechanical") -> bool: return _actions.delouse_pen(pen_id, method)
func repair_nets(site_id: String) -> bool: return _actions.repair_nets(site_id)
func stock_smolts(site_id: String, count: int = 10000, strain_id: String = "standard", quality_tier: int = 0) -> bool: return _actions.stock_smolts(site_id, count, strain_id, quality_tier)
func toggle_mitigation(site_id: String, kind: String) -> bool: return _actions.toggle_mitigation(site_id, kind)
func get_smolt_strains() -> Array[Dictionary]: return _proxies.get_smolt_strains()
func get_smolt_quality_tiers() -> Array[Dictionary]: return _proxies.get_smolt_quality_tiers()
func get_treatment_profiles() -> Array[Dictionary]: return _proxies.get_treatment_profiles()
func get_resistance_levels() -> Dictionary: return _proxies.get_resistance_levels()
func get_wellboat_jobs() -> Array[Dictionary]: return _proxies.get_wellboat_jobs()
func get_containment_levels() -> Array[Dictionary]: return _proxies.get_containment_levels()
func get_attention_budget() -> Dictionary: return _proxies.get_attention_budget()
func get_active_blooms() -> Array[Dictionary]: return _proxies.get_active_blooms()
func get_bloom_mitigations() -> Array[Dictionary]: return _proxies.get_bloom_mitigations()
func install_bloom_mitigation(site_id: String, kind: String) -> bool: return _proxies.install_bloom_mitigation(site_id, kind)
func get_feed_plan(site_id: String) -> Dictionary: return _proxies.get_feed_plan(site_id)
func get_feed_plans() -> Array: return _proxies.get_feed_plans()
func get_feed_plan_options() -> Dictionary: return _proxies.get_feed_plan_options()
func preview_feed_plan(site_id: String, ration_pct: float, recipe: String, days: int = 0) -> Dictionary: return _proxies.preview_feed_plan(site_id, ration_pct, recipe, days)
func get_forecast() -> Dictionary: return _proxies.get_forecast()
func get_night_shift_status() -> Dictionary: return _proxies.get_night_shift_status()
func set_night_shift_policy(policy: String) -> bool: return _actions.set_night_shift_policy(policy)
func get_intervention_ranking() -> Array[Dictionary]: return _proxies.get_intervention_ranking()
func get_attention_items() -> Array[Dictionary]: return _proxies.get_attention_items()
## The typed event bus (U2-06): the delivered log (newest first), the type
## catalog, a condition report (SimState uses it) and the repeats absorbed.
func get_game_event_log(max_count: int = 50) -> Array[Dictionary]: return _proxies.get_game_event_log(max_count)
func get_game_event_catalog() -> Array[Dictionary]: return _proxies.get_game_event_catalog()
func publish_condition_event(type_id: String, severity: String, site_id: String, message: String, variant: String = "", deadline_day: int = -1) -> Dictionary: return _proxies.publish_condition_event(type_id, severity, site_id, message, variant, deadline_day)
func game_events_suppressed() -> int: return _proxies.game_events_suppressed()
func open_attention_inbox() -> void: _proxies.open_attention_inbox()
func deploy_countermeasure(site_id: String, kind: String) -> bool: return _actions.deploy_countermeasure(site_id, kind)
func vaccinate_site(site_id: String) -> bool: return _actions.vaccinate_site(site_id)
func set_watch_mode(site_id: String, mode: String) -> bool: return _actions.set_watch_mode(site_id, mode)
func request_wellboat(site_id: String, kind: String, method: String = "") -> bool: return _actions.request_wellboat(site_id, kind, method)
func upgrade_containment(site_id: String, level_name: String) -> bool: return _actions.upgrade_containment(site_id, level_name)
func get_all_pens() -> Array[Dictionary]: return _actions.get_all_pens()
func save_game(slot_name: String) -> bool: return _actions.save_game(slot_name)
func load_game(file_name: String) -> bool: return _actions.load_game(file_name)
func quick_save() -> bool: return _actions.quick_save()
func delete_save(file_name: String) -> bool: return _actions.delete_save(file_name)
func get_save_list() -> Array: return _actions.get_save_list()
func get_last_save_error() -> String: return _actions.get_last_save_error()
func is_game_running() -> bool: return _actions.is_game_running()

# ── Formatters (delegated to SimFormatters) ───────────────────────────────────

func format_nok(amount: float) -> String: return Formatters.format_nok(amount)
func format_biomass(kg: float) -> String: return Formatters.format_biomass(kg)
func format_fish(count: int) -> String: return Formatters.format_fish(count)
func sustainability_color(level: String) -> Color: return Formatters.sustainability_color(level)
func lice_color(lice_per_fish: float) -> Color: return Formatters.lice_color(lice_per_fish)
func balance_color(amount: float) -> Color: return Formatters.balance_color(amount)

# ── Proxies: core fish / site ─────────────────────────────────────────────────

func get_fish_count_by_stage(stage_name: String) -> int: return _proxies.get_fish_count_by_stage(stage_name)
func get_biomass_by_stage(stage_name: String) -> float: return _proxies.get_biomass_by_stage(stage_name)
func owns_hatchery() -> bool: return _proxies.owns_hatchery()
func owns_processing_plant() -> bool: return _proxies.owns_processing_plant()
func buy_processing_plant(tier: String) -> bool: return _proxies.buy_processing_plant(tier)
func get_export_market_count() -> int: return _proxies.get_export_market_count()
func get_processing_throughput_kg() -> float: return _proxies.get_processing_throughput_kg()
func get_active_contracts() -> Array[Dictionary]: return _proxies.get_active_contracts()
func get_available_offers() -> Array[Dictionary]: return _proxies.get_available_offers()
func accept_offer(index: int) -> String: return _proxies.accept_offer(index)
func get_export_markets() -> Array[Dictionary]: return _proxies.get_export_markets()

# ── Proxies: analytics ────────────────────────────────────────────────────────

func get_analytics_data(metric: String, days: int = 30) -> Array[float]: return _proxies.get_analytics_data(metric, days)
func export_daily_snapshots(path: String) -> int: return _proxies.export_daily_snapshots(path)
func export_financial_report(path: String) -> int: return _proxies.export_financial_report(path)
func export_population_data(path: String) -> int: return _proxies.export_population_data(path)
func export_event_history(path: String) -> int: return _proxies.export_event_history(path)
func get_analytics_moving_average(metric: String, days: int = 30, window_days: int = 7) -> Array[float]: return _proxies.get_analytics_moving_average(metric, days, window_days)
func get_analytics_trend(metric: String, days: int = 14) -> String: return _proxies.get_analytics_trend(metric, days)
func get_analytics_all_time_high(metric: String) -> float: return _proxies.get_analytics_all_time_high(metric)
func get_analytics_all_time_low(metric: String) -> float: return _proxies.get_analytics_all_time_low(metric)

# ── Proxies: research / scenarios ─────────────────────────────────────────────

func get_research_nodes() -> Array[Dictionary]: return _proxies.get_research_nodes()
func start_research(node_id: String) -> bool: return _proxies.start_research(node_id)
func cancel_research() -> void: _proxies.cancel_research()
func is_research_unlocked(node_id: String) -> bool: return _proxies.is_research_unlocked(node_id)
func get_research_effect(effect_key: String) -> float: return _proxies.get_research_effect(effect_key)
func get_active_research_id() -> String: return _proxies.get_active_research_id()
func get_research_progress() -> int: return _proxies.get_research_progress()
func get_research_days_remaining() -> int: return _proxies.get_research_days_remaining()
func get_available_scenarios() -> Array[Dictionary]: return _proxies.get_available_scenarios()
func get_active_scenario_id() -> String: return _proxies.get_active_scenario_id()
func get_scenario_goal() -> Dictionary: return _proxies.get_scenario_goal()

# ── Proxies: finance / sustainability ─────────────────────────────────────────

func get_sustainability_breakdown() -> Dictionary: return _proxies.get_sustainability_breakdown()
func get_certification_level() -> String: return _proxies.get_certification_level()
func get_price_multiplier() -> float: return _proxies.get_price_multiplier()
func get_revenue() -> float: return _proxies.get_revenue()
func get_expenses() -> float: return _proxies.get_expenses()
func get_profit() -> float: return _proxies.get_profit()
func get_insurance_status() -> Dictionary: return _proxies.get_insurance_status()
func set_insurance_policy(kind: String, enabled: bool, coverage: float, deductible: float) -> bool: return _proxies.set_insurance_policy(kind, enabled, coverage, deductible)
func get_pen_price() -> float: return _proxies.get_pen_price()
func get_resource_rent() -> Dictionary: return _proxies.get_resource_rent()
func get_gill_risk(site_id: String) -> Dictionary: return _proxies.get_gill_risk(site_id)
func qualifies_flexibility(site_id: String) -> bool: return _proxies.qualifies_flexibility(site_id)
func get_player_supply_pressure() -> float: return _proxies.get_player_supply_pressure()
func get_zone_traffic_lights() -> Array[Dictionary]: return _proxies.get_zone_traffic_lights()

# ── Proxies: genetics / crew / achievements / weather ─────────────────────────

func get_breeding_candidates() -> Array[Dictionary]: return _proxies.get_breeding_candidates()
func get_genetic_traits() -> Array[Dictionary]: return _proxies.get_genetic_traits()
func get_inbreeding_coefficient() -> float: return _proxies.get_inbreeding_coefficient()
func get_genomic_selection_preview() -> Array[Dictionary]: return _proxies.get_genomic_selection_preview()
func get_inbreeding_status() -> Dictionary: return _proxies.get_inbreeding_status()
func get_ge_traits() -> Array[Dictionary]: return _proxies.get_ge_traits()
func get_crew_list() -> Array[Dictionary]: return _proxies.get_crew_list()
func get_crew_role_effects() -> Array[Dictionary]: return _proxies.get_crew_role_effects()
func hire_worker(role: String) -> bool: return _proxies.hire_worker(role)
func fire_worker(worker_id: String) -> bool: return _proxies.fire_worker(worker_id)
func get_achievement_list() -> Array[Dictionary]: return _proxies.get_achievement_list()
func get_current_weather_state() -> String: return _proxies.get_current_weather_state()
## G1-02/G1-04: latitude, calendar day and the atmosphere the sim day implies.
func get_sky_context() -> Dictionary: return _proxies.get_sky_context()
func get_scene_latitude() -> float: return _proxies.get_scene_latitude()
func get_current_temperature() -> float: return _proxies.get_current_temperature()
func get_current_day() -> int: return _proxies.get_current_day()

# ── Proxies: farm systems ─────────────────────────────────────────────────────

func get_ras_facilities() -> Array[Dictionary]: return _proxies.get_ras_facilities()
func build_ras_facility(rname: String, scale: String) -> bool: return _proxies.build_ras_facility(rname, scale)
func ras_maintenance(facility_id: String) -> float: return _proxies.ras_maintenance(facility_id)
func stock_ras_facility(facility_id: String, count: int, strain_id: String = "standard") -> Dictionary: return _proxies.stock_ras_facility(facility_id, count, strain_id)
func harvest_ras_facility(facility_id: String) -> Dictionary: return _proxies.harvest_ras_facility(facility_id)
func get_build_costs() -> Dictionary: return _proxies.get_build_costs()
func stock_offshore_farm(farm_id: String, count: int, strain_id: String = "standard") -> Dictionary: return _proxies.stock_offshore_farm(farm_id, count, strain_id)
func harvest_offshore_farm(farm_id: String) -> Dictionary: return _proxies.harvest_offshore_farm(farm_id)
func quote_harvest(holding_id: String, pen_ids: Array[String] = [], fraction: float = 1.0, express: bool = false) -> Dictionary: return _proxies.quote_harvest(holding_id, pen_ids, fraction, express)
func order_harvest(holding_id: String, pen_ids: Array[String] = [], fraction: float = 1.0) -> Dictionary: return _proxies.order_harvest(holding_id, pen_ids, fraction)
func cancel_harvest_order(order_id: String) -> Dictionary: return _proxies.cancel_harvest_order(order_id)
func get_harvest_orders() -> Array[Dictionary]: return _proxies.get_harvest_orders()
func get_harvest_reports() -> Array[Dictionary]: return _proxies.get_harvest_reports()
func project_harvest(holding_id: String, pen_ids: Array[String] = [], fraction: float = 1.0, wait_days: int = 0) -> Dictionary: return _proxies.project_harvest(holding_id, pen_ids, fraction, wait_days)
func get_harvest_planner_options() -> Dictionary: return _proxies.get_harvest_planner_options()
func harvest_express(holding_id: String, pen_ids: Array[String] = [], fraction: float = 1.0) -> Dictionary: return _proxies.harvest_express(holding_id, pen_ids, fraction)
func get_ledger_totals() -> Dictionary: return _proxies.get_ledger_totals()
func get_offshore_farms() -> Array[Dictionary]: return _proxies.get_offshore_farms()
func build_offshore_farm(offname: String, farm_type: String) -> bool: return _proxies.build_offshore_farm(offname, farm_type)
func repair_offshore(farm_id: String) -> float: return _proxies.repair_offshore(farm_id)
func get_ai_feeding_status() -> Dictionary: return _proxies.get_ai_feeding_status()
func install_ai_feeding() -> float: return _proxies.install_ai_feeding()
func clean_ai_cameras() -> float: return _proxies.clean_ai_cameras()
func get_post_smolt_facilities() -> Array[Dictionary]: return _proxies.get_post_smolt_facilities()
func start_smolt_batch(facility_id: String, count: int) -> bool: return _proxies.start_smolt_batch(facility_id, count)
func vaccinate_batch(batch_id: String) -> float: return _proxies.vaccinate_batch(batch_id)
func transfer_batch_to_sea(batch_id: String) -> bool: return _proxies.transfer_batch_to_sea(batch_id)

# ── Proxies: empire systems ───────────────────────────────────────────────────

func get_competitors() -> Array[Dictionary]: return _proxies.get_competitors()
func get_competitor_count() -> int: return _proxies.get_competitor_count()
func get_competitor_farms() -> Array[Dictionary]: return _proxies.get_competitor_farms()
func get_market_leader() -> Dictionary: return _proxies.get_market_leader()
func get_empire_phase() -> String: return _proxies.get_empire_phase()
func get_empire_score() -> int: return _proxies.get_empire_score()
func get_next_milestone() -> Dictionary: return _proxies.get_next_milestone()
func get_growth_strategy() -> String: return _proxies.get_growth_strategy()
func get_ownership_status() -> String: return _proxies.get_ownership_status()
func get_share_price() -> float: return _proxies.get_share_price()
func get_market_cap() -> float: return _proxies.get_market_cap()
func check_is_public() -> bool: return _proxies.check_is_public()
func attempt_ipo() -> Dictionary: return _proxies.attempt_ipo()
func get_brand_score() -> float: return _proxies.get_brand_score()
func get_brand_strategy() -> String: return _proxies.get_brand_strategy()
func get_certifications() -> Array[String]: return _proxies.get_certifications()
func get_pricing_power() -> float: return _proxies.get_pricing_power()
func get_active_crises() -> Array[Dictionary]: return _proxies.get_active_crises()
func get_acquisition_opportunities() -> Array[Dictionary]: return _proxies.get_acquisition_opportunities()
func attempt_acquisition(asset_id: String) -> Dictionary: return _proxies.attempt_acquisition(asset_id)
func get_integration_tiers() -> Array[Dictionary]: return _proxies.get_integration_tiers()
func get_owned_assets() -> Array[Dictionary]: return _proxies.get_owned_assets()
func get_integration_score() -> float: return _proxies.get_integration_score()
func purchase_asset(asset_id: String) -> Dictionary: return _proxies.purchase_asset(asset_id)

# ── Proxies: wave 2 systems ───────────────────────────────────────────────────

func get_active_loans() -> Array[Dictionary]: return _proxies.get_active_loans()
func get_total_debt() -> float: return _proxies.get_total_debt()
func get_monthly_payment() -> float: return _proxies.get_monthly_payment()
func take_loan(amount: float, term_months: int, rate: float) -> Dictionary: return _proxies.take_loan(amount, term_months, rate)
func get_loan_info() -> Dictionary: return _proxies.get_loan_info()
func repay_loan(amount: float) -> Dictionary: return _proxies.repay_loan(amount)
func get_tax_rate() -> float: return _proxies.get_tax_rate()
func get_last_tax_paid() -> float: return _proxies.get_last_tax_paid()
func get_annual_tax_burden() -> float: return _proxies.get_annual_tax_burden()
func get_investors() -> Array[Dictionary]: return _proxies.get_investors()
func get_total_investment() -> float: return _proxies.get_total_investment()
func get_equity_dilution() -> float: return _proxies.get_equity_dilution()
func get_available_targets() -> Array[Dictionary]: return _proxies.get_available_targets()
func get_owned_acquisitions() -> Array[Dictionary]: return _proxies.get_owned_acquisitions()
func attempt_acquisition_target(target_id: String) -> Dictionary: return _proxies.attempt_acquisition_target(target_id)
func get_active_licenses() -> Array[Dictionary]: return _proxies.get_active_licenses()
func get_license_cost() -> float: return _proxies.get_license_cost()
func apply_for_license(zone_id: String) -> Dictionary: return _proxies.apply_for_license(zone_id)
func place_license_bid(zone_id: String, amount_nok: float) -> Dictionary: return _proxies.place_license_bid(zone_id, amount_nok)
func get_license_auctions() -> Array[Dictionary]: return _proxies.get_license_auctions()
func has_growth_license(zone_id: String) -> bool: return _proxies.has_growth_license(zone_id)
func can_build_sea_site(zone_id: String) -> Dictionary: return _proxies.can_build_sea_site(zone_id)
func get_distressed_assets() -> Array[Dictionary]: return _proxies.get_distressed_assets()
func acquire_distressed_asset(asset_id: String) -> Dictionary: return _proxies.acquire_distressed_asset(asset_id)
func get_factions() -> Array[Dictionary]: return _proxies.get_factions()
func get_faction_relation(faction_id: String) -> float: return _proxies.get_faction_relation(faction_id)
func get_active_demands() -> Array[Dictionary]: return _proxies.get_active_demands()
func get_market_report() -> Dictionary: return _proxies.get_market_report()
func get_profit_loss_story(top_n: int = 3) -> Dictionary: return _proxies.get_profit_loss_story(top_n)
func get_mortality_breakdown(site_id: String = "", last_days: int = 0) -> Dictionary: return _proxies.get_mortality_breakdown(site_id, last_days)
func get_capacity_overview() -> Dictionary: return _proxies.get_capacity_overview()
func preview_stocking(site_id: String, count: int, strain_id: String = "standard") -> Dictionary: return _proxies.preview_stocking(site_id, count, strain_id)
func preview_build_sea_site(zone_id: String) -> Dictionary: return _proxies.preview_build_sea_site(zone_id)
func get_expansion_overview() -> Dictionary: return _proxies.get_expansion_overview()
func plan_site_build(zone_id: String) -> Dictionary: return _proxies.plan_site_build(zone_id)
func preview_licence_application(zone_id: String) -> Dictionary: return _proxies.preview_licence_application(zone_id)
func get_licence_auction(zone_id: String) -> Dictionary: return _proxies.get_licence_auction(zone_id)
func build_expansion_site(zone_id: String) -> Dictionary: return _actions.build_expansion_site(zone_id)
func open_licence_auction(zone_id: String) -> Dictionary: return _actions.open_licence_auction(zone_id)
func bid_on_licence_auction(zone_id: String, amount_nok: float) -> Dictionary: return _actions.bid_on_licence_auction(zone_id, amount_nok)
func get_scenario_end_report() -> Dictionary: return _proxies.get_scenario_end_report()
func get_current_verdict() -> String: return _proxies.get_current_verdict()
func get_liquidity_status() -> Dictionary: return _proxies.get_liquidity_status()
func accept_rescue_loan() -> Dictionary: return _actions.accept_rescue_loan()
func decline_rescue_loan() -> Dictionary: return _actions.decline_rescue_loan()
func get_site_pens(site_id: String) -> Array[Dictionary]: return _proxies.get_site_pens(site_id)
func preview_pen_stocking(site_id: String, pen_id: String, count: int, strain_id: String = "standard", quality_tier: int = 0) -> Dictionary: return _proxies.preview_pen_stocking(site_id, pen_id, count, strain_id, quality_tier)
func stock_smolts_in_pen(site_id: String, pen_id: String, count: int, strain_id: String = "standard", quality_tier: int = 0) -> Dictionary: return _actions.stock_smolts_in_pen(site_id, pen_id, count, strain_id, quality_tier)
func delouse_pens(site_id: String, method: String, pen_ids: Array[String] = []) -> bool: return _actions.delouse_pens(site_id, method, pen_ids)
func get_competitor_analysis() -> Array[Dictionary]: return _proxies.get_competitor_analysis()
func get_price_forecast() -> Array[Dictionary]: return _proxies.get_price_forecast()
func get_welfare_score() -> float: return _proxies.get_welfare_score()
func get_welfare_breakdown() -> Dictionary: return _proxies.get_welfare_breakdown()
func get_welfare_alerts() -> Array[String]: return _proxies.get_welfare_alerts()
func get_achieved_milestones() -> Array[Dictionary]: return _proxies.get_achieved_milestones()
func get_next_milestone_info() -> Dictionary: return _proxies.get_next_milestone_info()
func get_milestone_progress() -> float: return _proxies.get_milestone_progress()

# ── Proxies: Phase 5.3–5.4 farm systems (sludge, transfers, carbon, processing) ─
# Every public SimProxies/SimActions func needs a forwarder here; callers use
# has_method() on /root/SimBridge, so a missing line silently disables the UI
# (F945). tests/test_bridge_contract.py::test_simbridge_facade_parity guards it.

func processing_tier() -> String: return _proxies.processing_tier()
func get_sludge_status() -> Array[Dictionary]: return _proxies.get_sludge_status()
func toggle_sludge_hub(site_id: String) -> bool: return _proxies.toggle_sludge_hub(site_id)
func valorize_sludge_now(site_id: String) -> float: return _proxies.valorize_sludge_now(site_id)
func plan_cohort_transfer(source_site_id: String, dest_site_id: String, fish_count: int, mean_weight_kg: float, reason: String) -> Dictionary: return _proxies.plan_cohort_transfer(source_site_id, dest_site_id, fish_count, mean_weight_kg, reason)
func execute_cohort_transfer(source_site_id: String, dest_site_id: String, fish_count: int, mean_weight_kg: float, reason: String) -> Dictionary: return _proxies.execute_cohort_transfer(source_site_id, dest_site_id, fish_count, mean_weight_kg, reason)
func get_transfer_destinations(source_site_id: String, incoming_biomass_kg: float) -> Array[Dictionary]: return _proxies.get_transfer_destinations(source_site_id, incoming_biomass_kg)
func get_carbon_ledger() -> Dictionary: return _proxies.get_carbon_ledger()
func get_carbon_intensity() -> float: return _proxies.get_carbon_intensity()
func is_carbon_neutral_year() -> bool: return _proxies.is_carbon_neutral_year()

# ── F2-04: the farm-type catalogue (sim·core) ─────────────────────────────────
func get_farm_type_catalog() -> Dictionary: return _proxies.get_farm_type_catalog()

# ── F2-05: the Driftsplan and the notification meter (sim·core) ──────────────
func get_driftsplan_catalog() -> Array[Dictionary]: return _proxies.get_driftsplan_catalog()
func get_driftsplan(unit_id: String) -> Array[Dictionary]: return _proxies.get_driftsplan(unit_id)
func set_driftsplan(unit_id: String, policy_kind: String, settings: Dictionary) -> Dictionary: return _proxies.set_driftsplan(unit_id, policy_kind, settings)
func clear_driftsplan(unit_id: String, policy_kind: String) -> Dictionary: return _proxies.clear_driftsplan(unit_id, policy_kind)
func get_next_planned(unit_id: String) -> Dictionary: return _proxies.get_next_planned(unit_id)
func get_notification_meter() -> Array[Dictionary]: return _proxies.get_notification_meter()
func get_notification_budget() -> Dictionary: return _proxies.get_notification_budget()

# ── F2-03: farm types (land sites, offshore units, their plots) ───────────────
# Each lane forwards only inside its own block (FARM_TYPES_SPEC §7.6).

# ═══ BEGIN sim·land lane block (F2-03 list verbs; F2-12 … F2-16 append below) ═══
func get_land_sites() -> Array[Dictionary]: return _proxies.get_land_sites()
func get_land_plots() -> Array[Dictionary]: return _proxies.get_land_plots()
func get_land_site_detail(site_id: String) -> Dictionary: return _proxies.get_land_site_detail(site_id)
func preview_land_build(plot_id: String, template: String, contract: String, mitigations: Array) -> Dictionary: return _proxies.preview_land_build(plot_id, template, contract, mitigations)
func preview_land_expansion(site_id: String, contract: String) -> Dictionary: return _proxies.preview_land_expansion(site_id, contract)
func apply_land_permits(plot_id: String, template: String, lots: int, mitigations: Array) -> Dictionary: return _proxies.apply_land_permits(plot_id, template, lots, mitigations)
func apply_for_zoning(plot_id: String, mitigations: Array) -> Dictionary: return _proxies.apply_for_zoning(plot_id, mitigations)
func appeal_land_permit(case_id: String) -> Dictionary: return _proxies.appeal_land_permit(case_id)
func reapply_land_permit(case_id: String, mitigations: Array) -> Dictionary: return _proxies.reapply_land_permit(case_id, mitigations)
func start_grid_project(plot_id: String, kind: String) -> Dictionary: return _proxies.start_grid_project(plot_id, kind)
func build_land_site(plot_id: String, template: String, contract: String, site_name: String) -> Dictionary: return _proxies.build_land_site(plot_id, template, contract, site_name)
func expand_land_site(site_id: String, contract: String) -> Dictionary: return _proxies.expand_land_site(site_id, contract)
func accelerate_land_construction(project_id: String) -> Dictionary: return _proxies.accelerate_land_construction(project_id)
func cancel_land_construction(project_id: String) -> Dictionary: return _proxies.cancel_land_construction(project_id)
func get_land_construction() -> Array[Dictionary]: return _proxies.get_land_construction()
func get_land_permit_cases(plot_id: String) -> Array[Dictionary]: return _proxies.get_land_permit_cases(plot_id)
func preview_land_stocking(site_id: String, tank_id: String, count: int, strain_id: String) -> Dictionary: return _proxies.preview_land_stocking(site_id, tank_id, count, strain_id)
func stock_land_tank(site_id: String, tank_id: String, count: int, strain_id: String) -> Dictionary: return _proxies.stock_land_tank(site_id, tank_id, count, strain_id)
func grade_tank(site_id: String, from_tank_id: String, to_tank_id: String, share: float) -> Dictionary: return _proxies.grade_tank(site_id, from_tank_id, to_tank_id, share)
func harvest_land_tank(site_id: String, tank_id: String) -> Dictionary: return _proxies.harvest_land_tank(site_id, tank_id)
func set_vannstrategi(site_id: String, reuse: float, deep_intake_share: float, setpoint_c: float, blend: bool) -> Dictionary: return _proxies.set_vannstrategi(site_id, reuse, deep_intake_share, setpoint_c, blend)
func install_heat_recovery(lot_id: String) -> Dictionary: return _proxies.install_heat_recovery(lot_id)
# ═══ END sim·land lane block ═══

# ═══ BEGIN sim·sea lane block (F2-03 list verbs; F2-06 … F2-11 append below) ═══
func get_offshore_units() -> Array[Dictionary]: return _proxies.get_offshore_units()
func get_offshore_plots() -> Array[Dictionary]: return _proxies.get_offshore_plots()
func get_sea_state_forecast(site_id: String) -> Dictionary: return _proxies.get_sea_state_forecast(site_id)
func preview_offshore_build(plot_id: String, farm_type: String, contract: String, equity_share: float) -> Dictionary: return _proxies.preview_offshore_build(plot_id, farm_type, contract, equity_share)
func build_offshore_unit(plot_id: String, farm_type: String, contract: String, equity_share: float) -> Dictionary: return _proxies.build_offshore_unit(plot_id, farm_type, contract, equity_share)
func get_offshore_construction(site_id: String) -> Dictionary: return _proxies.get_offshore_construction(site_id)
func get_offshore_unit_state(site_id: String) -> Dictionary: return _proxies.get_offshore_unit_state(site_id)
func preview_offshore_booking(site_id: String, op: String, day: int, vessel: String) -> Dictionary: return _proxies.preview_offshore_booking(site_id, op, day, vessel)
func book_offshore_operation(site_id: String, op: String, day: int, vessel: String, count: int = 0, detail: String = "") -> Dictionary: return _proxies.book_offshore_operation(site_id, op, day, vessel, count, detail)
func book_offshore_first_window(site_id: String, op: String, vessel: String, count: int = 0, detail: String = "") -> Dictionary: return _proxies.book_offshore_first_window(site_id, op, vessel, count, detail)
func cancel_offshore_booking(site_id: String, booking_id: String) -> Dictionary: return _proxies.cancel_offshore_booking(site_id, booking_id)
func preview_offshore_tow(site_id: String) -> Dictionary: return _proxies.preview_offshore_tow(site_id)
func start_offshore_tow(site_id: String) -> Dictionary: return _proxies.start_offshore_tow(site_id)
func choose_offshore_shakedown(site_id: String, days: int) -> Dictionary: return _proxies.choose_offshore_shakedown(site_id, days)
func fix_teething_defect(site_id: String, defect_id: String) -> Dictionary: return _proxies.fix_teething_defect(site_id, defect_id)
func install_offshore_sensors(site_id: String, kind: String) -> Dictionary: return _proxies.install_offshore_sensors(site_id, kind)
func service_offshore_sensors(site_id: String) -> Dictionary: return _proxies.service_offshore_sensors(site_id)
func repair_offshore_unit(site_id: String, scope: String) -> Dictionary: return _proxies.repair_offshore_unit(site_id, scope)
func buy_out_offshore_partner(site_id: String) -> Dictionary: return _proxies.buy_out_offshore_partner(site_id)
# ═══ END sim·sea lane block ═══
