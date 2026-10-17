class_name SimActions
extends Node

## SimActions — game-flow, pen and persistence actions extracted from SimBridge.
## Emits pen_action_completed; the SimBridge facade re-emits it unchanged.

# ── Signals ───────────────────────────────────────────────────────────────────

## Emitted when a pen action completes (feed, harvest, delouse, place_pen)
signal pen_action_completed(action: String, pen_id: String, success: bool)

const State = preload("res://scripts/bridge/SimState.gd")

# ── Internal ──────────────────────────────────────────────────────────────────

## C# GameBridge node, injected by the SimBridge facade.
var game_bridge: Node = null

## Snapshot owner, injected by the SimBridge facade (for post-action refresh).
var state: State = null

# ══════════════════════════════════════════════════════════════════════════════
# CORE GAMEPLAY API (START NEW GAME, TIME CONTROL, PEN MANAGEMENT)
# ══════════════════════════════════════════════════════════════════════════════

## Start a new game. Player-first signature: every caller passes
## (player_name, starting_capital); scenario_id selects a scenario start.
func start_new_game(player_name: String = "Player", starting_capital: int = 10_000_000, scenario_id: String = "default") -> bool:
	if not game_bridge:
		push_error("SimBridge: Cannot start game - GameBridge not available")
		return false

	# A named scenario starts as itself or not at all: an unknown or locked id
	# returns false (the picker says so) instead of quietly starting the
	# default game in its place (review F692).
	if scenario_id != "default":
		if game_bridge.StartScenario(scenario_id):
			if state != null:
				state._refresh_state()
			return true
		push_warning("SimBridge: scenario '%s' could not be started" % scenario_id)
		return false

	# Start default game
	game_bridge.StartNewGame(player_name, starting_capital)
	if state != null:
		state._refresh_state()
	return true


## QA-only: deterministic new game (fixed seed) for headless runs.
func start_qa_new_game(player_name: String, starting_capital: int, seed: int) -> bool:
	if not game_bridge or not game_bridge.has_method("StartQaNewGame"):
		push_error("SimBridge: StartQaNewGame unavailable")
		return false
	game_bridge.StartQaNewGame(player_name, starting_capital, seed)
	if state != null:
		state._refresh_state()
	return true

## QA-only: deterministic scenario start (fixed seed) for screenshot/headless
## runs. Falls back to the unseeded path when the QA bridge is unavailable.
func start_qa_scenario(scenario_id: String, seed: int) -> bool:
	if not game_bridge:
		push_error("SimBridge: Cannot start QA scenario - GameBridge not available")
		return false
	if game_bridge.has_method("StartQaScenario"):
		var ok: bool = game_bridge.StartQaScenario(scenario_id, seed)
		if ok and state != null:
			state._refresh_state()
		return ok
	push_warning("SimBridge: StartQaScenario unavailable — falling back to start_new_game")
	return start_new_game("Player", 10_000_000, scenario_id)


## QA-only: inject an algal bloom at a site for the living-fjord visual state.
func spawn_qa_bloom(site_id: String, severity: float = 0.7, duration_days: int = 21) -> Dictionary:
	if game_bridge and game_bridge.has_method("SpawnQaBloom"):
		return game_bridge.SpawnQaBloom(site_id, severity, duration_days)
	return {"success": false, "message": "qa_bridge_unavailable"}


## QA-only: set pen lice load for the living-fjord collar-tint state.
func set_qa_pen_lice(site_id: String, lice_per_fish: float) -> Dictionary:
	if game_bridge and game_bridge.has_method("SetQaPenLice"):
		return game_bridge.SetQaPenLice(site_id, lice_per_fish)
	return {"success": false, "message": "qa_bridge_unavailable"}


## QA-only: clear all active blooms (paired control for the bloom readout).
func clear_qa_blooms() -> Dictionary:
	if game_bridge and game_bridge.has_method("ClearQaBlooms"):
		return game_bridge.ClearQaBlooms()
	return {"success": false, "message": "qa_bridge_unavailable"}


## Advance simulation by exactly 1 day
func advance_day() -> void:
	if game_bridge:
		game_bridge.AdvanceDay()


## Advance simulation by N days (for skip buttons)
func advance_days(days: int) -> void:
	if game_bridge:
		game_bridge.AdvanceDays(days)


## Set game paused/unpaused
func set_paused(paused: bool) -> void:
	if game_bridge:
		game_bridge.SetPaused(paused)


## True while the game clock is paused.
func is_paused() -> bool:
	return game_bridge.IsPaused() if game_bridge else true


## Set the clock's speed: 1, 3, 10 or 30 × normal (other values snap to the
## nearest, roadmap U2-07). The player taking the speed ends "Til neste hendelse".
func set_time_speed(speed: float) -> void:
	if game_bridge:
		game_bridge.SetTimeSpeed(speed)


## The clock's speed (1, 3, 10 or 30).
func get_time_speed() -> float:
	return float(game_bridge.TimeSpeed) if game_bridge else 1.0


## True while the player's own pause is on (holds such as an open card aside).
func is_player_paused() -> bool:
	return game_bridge.IsPlayerPaused() if game_bridge else true


## Stop the clock while `reason` waits for the player ("briefing",
## "critical_event", "pause_menu"); release_clock lets go of it.
func hold_clock(reason: String) -> void:
	if game_bridge:
		game_bridge.HoldClock(reason)


func release_clock(reason: String) -> void:
	if game_bridge:
		game_bridge.ReleaseClock(reason)


## True while `reason` ("" = anything) holds the clock.
func is_clock_held(reason: String = "") -> bool:
	return game_bridge.IsClockHeld(reason) if game_bridge else false


## "Til neste hendelse" (N): 30× until the next warning or critical event, a
## deadline's slow-down or 60 quiet days, then paused. False when it cannot run.
func run_to_next_event() -> bool:
	return game_bridge.RunToNextEvent() if game_bridge else false


func stop_run_to_next_event() -> void:
	if game_bridge:
		game_bridge.StopRunToNextEvent()


func is_running_to_next_event() -> bool:
	return game_bridge.IsRunningToNextEvent if game_bridge else false


## The clock as the HUD draws it (GameBridge.GetClockState: speed, paused,
## holds, next_event, pacing, next_deadline, …).
func get_clock_state() -> Dictionary:
	return game_bridge.GetClockState() if game_bridge else {}


## True from a player's new game until its briefing card is answered.
func is_briefing_pending() -> bool:
	return game_bridge.BriefingPending if game_bridge else false


## The briefing's "Start": the clock runs at `speed`.
func begin_play(speed: float) -> void:
	if game_bridge:
		game_bridge.BeginPlay(speed)


## The briefing's "Se deg rundt først": the card goes, the game stays paused.
func dismiss_briefing() -> void:
	if game_bridge:
		game_bridge.DismissBriefing()


## "Bremse ned før frister": may the deadline rule slow and stop the clock.
func set_auto_slow_deadlines(enabled: bool) -> void:
	if game_bridge:
		game_bridge.AutoSlowDeadlines = enabled


## Watch a deadline (the registration API, roadmap U2-07 / F2-34): slow to 1×
## at `slow_at_days_left`, stop at `pause_at_days_left` unless resolved first.
func register_pacing_deadline(key: String, deadline_day: int, headline: String = "", site_id: String = "",
		counter_verb: String = "none", slow_at_days_left: int = 7, pause_at_days_left: int = 1) -> bool:
	if not game_bridge:
		return false
	return game_bridge.RegisterPacingDeadline(key, deadline_day, headline, site_id, counter_verb,
		slow_at_days_left, pause_at_days_left)


## The deadline was answered or its cause is gone.
func resolve_pacing_deadline(key: String) -> bool:
	return game_bridge.ResolvePacingDeadline(key) if game_bridge else false


## The watched deadlines, soonest first.
func get_pacing_deadlines() -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	if game_bridge:
		for d in game_bridge.GetPacingDeadlines():
			out.append(d)
	return out


## Skip the tutorial (no rewards, no win: D19). From TutorialUI's skip button.
func skip_tutorial() -> void:
	if game_bridge and game_bridge.has_method("SkipTutorial"):
		game_bridge.SkipTutorial()
		if state != null:
			state._refresh_state()


## U2-02: a UI event the player caused ("tab:pens", "inbox", "clock",
## "harvest_report", "map_area_home"). True when it completed the step that
## waited for it; ignored outside the tutorial and by any other step.
func tutorial_mark(mark: String) -> bool:
	if not game_bridge:
		return false
	var done: bool = game_bridge.TutorialMark(mark)
	if done and state != null:
		state._refresh_state()
	return done


## U2-02: check the current step against the game now (after a command
## while the clock stands). True when a step completed.
func tutorial_check() -> bool:
	if not game_bridge:
		return false
	var done: bool = game_bridge.TutorialCheck()
	if done and state != null:
		state._refresh_state()
	return done


## Get current game state as JSON (for saving)
func get_game_state_json() -> String:
	if not game_bridge or not game_bridge.SimulationEngine:
		return "{}"

	# The C# GameState doesn't have ToJson() yet, so we'll use SaveLoadManager
	# For now, return a minimal JSON representation
	var snapshot = {
		"day": state.current_day if state != null else 0,
		"balance": state.balance if state != null else 0.0,
		"fish_count": state.fish_count if state != null else 0,
		"biomass_kg": state.fish_biomass_kg if state != null else 0.0
	}
	return JSON.stringify(snapshot)


## Place a new pen in a zone
func place_pen(zone_id: String, pen_name: String = "New Pen") -> bool:
	if not game_bridge:
		return false

	# Use BuildSeaSite from GameBridge
	var success = game_bridge.BuildSeaSite(zone_id, pen_name)
	if success:
		pen_action_completed.emit("place_pen", zone_id, true)
	else:
		push_warning("SimBridge: Failed to place pen in zone '%s'" % zone_id)
		pen_action_completed.emit("place_pen", zone_id, false)

	return success


## A site's feeding plan (U2-04): ration (% of appetite), recipe id
## ("standard" | "high_energy" | "economy") and window (days, 0 = until
## changed). Returns the bridge's result ({success, message, error_key, plan}).
func set_feed_plan(site_id: String, ration_pct: float, recipe: String, window_days: int) -> Dictionary:
	if not game_bridge:
		return {"success": false, "error_key": "engine_not_ready", "message": "Not initialized"}
	var result: Dictionary = game_bridge.SetFeedPlan(site_id, ration_pct, recipe, window_days)
	pen_action_completed.emit("feed_plan", site_id, bool(result.get("success", false)))
	return result


## "Fôr alle": the same plan for every site ({success, message, error_key, site_count}).
func set_feed_plan_all(ration_pct: float, recipe: String, window_days: int) -> Dictionary:
	if not game_bridge:
		return {"success": false, "error_key": "engine_not_ready", "message": "Not initialized"}
	var result: Dictionary = game_bridge.SetFeedPlanForAll(ration_pct, recipe, window_days)
	pen_action_completed.emit("feed_plan", "all_sites", bool(result.get("success", false)))
	return result


## Harvest a pen
func harvest_pen(pen_id: String) -> float:
	if not game_bridge:
		return 0.0

	var biomass = game_bridge.HarvestSite(pen_id)
	if biomass > 0:
		pen_action_completed.emit("harvest", pen_id, true)
	else:
		push_warning("SimBridge: Harvest failed for pen '%s'" % pen_id)
		pen_action_completed.emit("harvest", pen_id, false)

	return biomass


## Delouse a pen
func delouse_pen(pen_id: String, method: String = "mechanical") -> bool:
	if not game_bridge:
		return false

	var success = game_bridge.OrderDelousing(pen_id, method)
	if success:
		pen_action_completed.emit("delouse", pen_id, true)
	else:
		push_warning("SimBridge: Delousing failed for pen '%s'" % pen_id)
		pen_action_completed.emit("delouse", pen_id, false)

	return success


## Repair nets at a site (Phase 1.2). Cost scales with damage.
func repair_nets(site_id: String) -> bool:
	if not game_bridge:
		return false

	var success = game_bridge.RepairNets(site_id)
	pen_action_completed.emit("repair_nets", site_id, success)
	if not success:
		push_warning("SimBridge: Net repair failed for site '%s'" % site_id)

	return success


## Stock smolts at a site (Phase 1.4 + P5 quality tier). Fixed 10k batch;
## strain and quality tier from the dialog. quality_tier is the enum value
## (0=Standard, 1=Large, 2=SuperSmolt) matching get_smolt_quality_tiers() order.
func stock_smolts(site_id: String, count: int = 10000, strain_id: String = "standard", quality_tier: int = 0) -> bool:
	if not game_bridge:
		return false

	var success := false
	if game_bridge.has_method("StockSmolts"):
		# 4-arg overload stamps the cohort's carryover quality tier.
		success = game_bridge.StockSmolts(site_id, count, strain_id, quality_tier)
	else:
		push_warning("SimBridge: StockSmolts unavailable — stocking refused")
	pen_action_completed.emit("stock_smolts", site_id, success)
	if not success:
		push_warning("SimBridge: Stocking failed for site '%s'" % site_id)
	else:
		state._refresh_state()

	return success


## S2-01b: stock smolts in one pen of a site (the pen's id or number). The
## result carries "success", "reason_key", "label_key" (translation key of a
## refusal), "message", "fish", "site_id" and "pen_id".
func stock_smolts_in_pen(site_id: String, pen_id: String, count: int, strain_id: String = "standard", quality_tier: int = 0) -> Dictionary:
	if not game_bridge:
		return {"success": false, "message": "Not initialized"}

	var result: Dictionary = game_bridge.StockSmoltsInPen(site_id, pen_id, count, strain_id, quality_tier)
	var success := bool(result.get("success", false))
	pen_action_completed.emit("stock_smolts", site_id, success)
	if not success:
		push_warning("SimBridge: Stocking failed for pen '%s' at '%s' (%s)" % [pen_id, site_id, str(result.get("reason_key", ""))])
	else:
		state._refresh_state()
	return result


## U2-03: take the bank's open rescue offer (a loan against the fish).
## Returns {success, error_key, message, amount, loan_id}.
func accept_rescue_loan() -> Dictionary:
	if not game_bridge:
		return {"success": false, "error_key": "engine_not_ready", "message": "Not initialized"}
	var result: Dictionary = game_bridge.AcceptRescueLoan()
	if bool(result.get("success", false)):
		state._refresh_state()
	return result


## U2-03: say no to the bank's open rescue offer. Returns {success, error_key, message}.
func decline_rescue_loan() -> Dictionary:
	if not game_bridge:
		return {"success": false, "error_key": "engine_not_ready", "message": "Not initialized"}
	var result: Dictionary = game_bridge.DeclineRescueLoan()
	if bool(result.get("success", false)):
		state._refresh_state()
	return result


## S2-01b: delouse chosen pens of a site (pen ids or numbers; empty = the whole site).
## U2-01: build a sea site in a zone from the map. Returns the bridge's
## dictionary (success, reason_key, label_key, site_id, site_label).
func build_expansion_site(zone_id: String) -> Dictionary:
	if not game_bridge:
		return {"success": false, "reason_key": "engine_not_ready", "label_key": "EXPANSION_REASON_ENGINE_NOT_READY"}
	var result: Dictionary = game_bridge.BuildExpansionSite(zone_id)
	if bool(result.get("success", false)):
		state._refresh_state()
	return result


## U2-01: open the growth-licence auction for a zone (opening is free).
func open_licence_auction(zone_id: String) -> Dictionary:
	if not game_bridge:
		return {"success": false, "reason_key": "engine_not_ready", "label_key": "EXPANSION_REASON_ENGINE_NOT_READY"}
	var result: Dictionary = game_bridge.OpenLicenceAuction(zone_id)
	if bool(result.get("success", false)):
		state._refresh_state()
	return result


## U2-01: bid in a zone's running licence auction (NOK).
func bid_on_licence_auction(zone_id: String, amount_nok: float) -> Dictionary:
	if not game_bridge:
		return {"success": false, "reason_key": "engine_not_ready", "label_key": "EXPANSION_REASON_ENGINE_NOT_READY"}
	var result: Dictionary = game_bridge.BidOnLicenceAuction(zone_id, amount_nok)
	if bool(result.get("success", false)):
		state._refresh_state()
	return result


func delouse_pens(site_id: String, method: String, pen_ids: Array[String] = []) -> bool:
	if not game_bridge:
		return false

	var success: bool = game_bridge.DelousePens(site_id, method, pen_ids)
	pen_action_completed.emit("delouse", site_id, success)
	if not success:
		push_warning("SimBridge: Delousing failed for pens %s at '%s'" % [str(pen_ids), site_id])
	else:
		state._refresh_state()
	return success


## Toggle a preventive mitigation: kind "skirt" or "deterrent" (Phase 1.5).
func toggle_mitigation(site_id: String, kind: String) -> bool:
	if not game_bridge:
		return false

	var success = game_bridge.ToggleMitigation(site_id, kind)
	pen_action_completed.emit("mitigation", site_id, success)
	if success:
		state._refresh_state()

	return success


## Deploy a bloom countermeasure: kind "bubble" or "diffuser" (Phase 4.3).
func deploy_countermeasure(site_id: String, kind: String) -> bool:
	if not game_bridge:
		return false

	var success = game_bridge.DeployCountermeasure(site_id, kind)
	pen_action_completed.emit("countermeasure", site_id, success)
	if success:
		state._refresh_state()

	return success


## Vaccinate a site, October window only (Phase 5.1).
func vaccinate_site(site_id: String) -> bool:
	if not game_bridge:
		return false

	var success = game_bridge.VaccinateSite(site_id)
	pen_action_completed.emit("vaccinate", site_id, success)
	if success:
		state._refresh_state()

	return success


## Request a spot-hire wellboat: kind "harvest"/"delouse" (Phase 2.4).
func request_wellboat(site_id: String, kind: String, method: String = "") -> bool:
	if not game_bridge:
		return false

	var success = game_bridge.RequestWellboat(site_id, kind, method)
	pen_action_completed.emit("wellboat", site_id, success)
	if success:
		state._refresh_state()

	return success


## Upgrade site containment one rung (Phase 4.1, no downgrade).
func upgrade_containment(site_id: String, level_name: String) -> bool:
	if not game_bridge:
		return false

	var success = game_bridge.UpgradeContainment(site_id, level_name)
	pen_action_completed.emit("upgrade_containment", site_id, success)
	if success:
		state._refresh_state()

	return success


## Set ROC watch mode: "watch"/"scan"/"auto" (Phase 4.2, budget-gated).
func set_watch_mode(site_id: String, mode: String) -> bool:
	if not game_bridge:
		return false

	var success = game_bridge.SetWatchMode(site_id, mode)
	if success:
		state._refresh_state()
	else:
		push_warning("SimBridge: Watch mode refused for '%s' (over budget?)" % site_id)

	return success



## Set night-shift ROC policy: "Standard"/"ScanOnly"/"AutomatedROC" (Phase 5.2).
func set_night_shift_policy(policy: String) -> bool:
	if not game_bridge:
		return false

	var success = game_bridge.SetNightShiftPolicy(policy)
	if success:
		state._refresh_state()
	else:
		push_warning("SimBridge: Night shift policy refused for '%s'" % policy)

	return success


## Get list of all pens with details
func get_all_pens() -> Array[Dictionary]:
	if not game_bridge:
		return []

	var raw = game_bridge.GetSeaSiteDetails()
	var result: Array[Dictionary] = []
	for entry in raw:
		result.append(entry)
	return result


## C# SaveLoadManager autoload (PascalCase API), or null when unavailable.
func _save_mgr() -> Node:
	var mgr := get_node_or_null("/root/SaveLoadManager")
	if mgr == null:
		push_error("SimBridge: SaveLoadManager not found")
	return mgr


## Save the running game as `slot_name` (written to <sanitized name>.json).
func save_game(slot_name: String) -> bool:
	var mgr := _save_mgr()
	return mgr != null and bool(mgr.SaveGame(slot_name))


## Load the save file `file_name` (the "file" key of get_save_list()).
func load_game(file_name: String) -> bool:
	var mgr := _save_mgr()
	if mgr == null or not bool(mgr.LoadGame(file_name)):
		return false
	if state != null:
		state._refresh_state()
	return true


## Timestamped quicksave of the running game.
func quick_save() -> bool:
	var mgr := _save_mgr()
	return mgr != null and bool(mgr.QuickSave())


## Delete the save file `file_name` (and its backup).
func delete_save(file_name: String) -> bool:
	var mgr := _save_mgr()
	return mgr != null and bool(mgr.DeleteSave(file_name))


## Save slots, newest first: [{file, name, day, cash, time}, ...].
func get_save_list() -> Array:
	var mgr := _save_mgr()
	return mgr.GetSaveList() if mgr != null else []


## Reason the last save/load failed ("" after a success).
func get_last_save_error() -> String:
	var mgr := _save_mgr()
	return String(mgr.LastError) if mgr != null else "SaveLoadManager not found"


## True once a game is started or loaded (GameScene keeps it instead of
## starting a fresh default game).
func is_game_running() -> bool:
	return game_bridge != null and bool(game_bridge.IsGameRunning)
