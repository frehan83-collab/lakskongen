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

	# If scenario_id is provided, try to start scenario
	if scenario_id != "default":
		if game_bridge.StartScenario(scenario_id):
			if state != null:
				state._refresh_state()
			return true
		else:
			push_warning("SimBridge: Scenario '%s' not found, starting default game" % scenario_id)

	# Start default game
	game_bridge.StartNewGame(player_name, starting_capital)
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


## Set time speed multiplier (1x, 2x, 5x, etc.)
func set_time_speed(speed: float) -> void:
	if game_bridge:
		game_bridge.SetTimeSpeed(speed)


## Skip the tutorial silently (no rewards). From TutorialUI skip button.
func skip_tutorial() -> void:
	if game_bridge and game_bridge.has_method("SkipTutorial"):
		game_bridge.SkipTutorial()
		if state != null:
			state._refresh_state()


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


## Feed fish in a specific pen.
## The engine has no per-pen manual feed API (daily feeding is automatic
## via FeedSystem during Advance) — refuse loudly instead of faking success.
func feed_pen(pen_id: String, amount_kg: float) -> bool:
	if not game_bridge:
		return false
	push_warning("SimBridge: feed_pen('%s', %.1f kg) not supported by GameBridge — no feed applied" % [pen_id, amount_kg])
	pen_action_completed.emit("feed", pen_id, false)
	return false


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


## Set feed recipe sliders (Phase 4.4).
func set_feed_recipe(protein: float, replace: float, astax: float) -> void:
	if not game_bridge:
		return
	game_bridge.SetFeedRecipe(protein, replace, astax)
	state._refresh_state()


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
