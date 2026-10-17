class_name CampaignProgress
extends RefCounted

## CampaignProgress — the player's campaign across games (roadmap U2-03).
##
## Which scenarios are open and the best medal won in each, kept in
## user://campaign.json so they survive a restart. The unlock graph is data
## (res://data/campaign.json): a scenario is open from the start, once won,
## or when any scenario that lists it under "unlocks" is won. Medals come from the
## engine's end report (bronze for a win, silver with two of its three
## distinctions, gold with all three; D16: medals, never cash). Mod scenarios
## are outside the campaign and always open.
##
## The progress file is the player's own: tests and QA captures point
## path_override at a file of their own. Writes go to a temporary file first
## and replace the old one, and a file that does not parse is kept beside it
## (campaign.json.bad) instead of being overwritten.

const DATA_PATH := "res://data/campaign.json"
const DEFAULT_PATH := "user://campaign.json"
const FORMAT_VERSION := 1
## Medal keys from worst to best (the engine's ScenarioMedals.Key).
const MEDALS := ["none", "bronze", "silver", "gold"]
const VERDICT_WON := "won"

## Tests and QA captures keep their progress apart from the player's.
static var path_override: String = ""
static var _graph: Dictionary = {}


static func path() -> String:
	return path_override if path_override != "" else DEFAULT_PATH


# ── The graph (data) ───────────────────────────────────────────────────────────

## {order: [...], open_from_start: [...], unlocks: {id: [...]}} from DATA_PATH.
static func graph() -> Dictionary:
	if _graph.is_empty():
		var parsed: Variant = _read_json(DATA_PATH)
		if parsed is Dictionary:
			_graph = parsed
		else:
			push_error("CampaignProgress: %s is missing or invalid" % DATA_PATH)
			_graph = {"order": [], "open_from_start": [], "unlocks": {}}
	return _graph


## Scenarios in the campaign, in reading order.
static func order() -> PackedStringArray:
	return PackedStringArray(graph().get("order", []))


static func is_campaign_scenario(scenario_id: String) -> bool:
	return order().has(scenario_id)


## The scenarios whose win opens `scenario_id` (empty for an open one).
static func unlocked_by(scenario_id: String) -> PackedStringArray:
	var out := PackedStringArray()
	var unlocks: Dictionary = graph().get("unlocks", {})
	for source in order():
		if (unlocks.get(source, []) as Array).has(scenario_id):
			out.append(source)
	return out


# ── Progress (the player's file) ───────────────────────────────────────────────

## The saved progress: {version, scenarios: {id: {best_medal, wins, results, best_result_nok}}}.
static func load_progress() -> Dictionary:
	var empty := {"version": FORMAT_VERSION, "scenarios": {}}
	if not FileAccess.file_exists(path()):
		return empty
	var parsed: Variant = _read_json(path())
	if not (parsed is Dictionary) or not ((parsed as Dictionary).get("scenarios") is Dictionary):
		push_warning("CampaignProgress: %s does not parse; kept as %s.bad" % [path(), path()])
		DirAccess.rename_absolute(path(), path() + ".bad")
		return empty
	return parsed


static func is_unlocked(scenario_id: String, progress: Dictionary = {}) -> bool:
	if not is_campaign_scenario(scenario_id):
		return true
	if (graph().get("open_from_start", []) as Array).has(scenario_id):
		return true
	var p := progress if not progress.is_empty() else load_progress()
	if wins(scenario_id, p) > 0:
		return true
	for source in unlocked_by(scenario_id):
		if wins(source, p) > 0:
			return true
	return false


static func best_medal(scenario_id: String, progress: Dictionary = {}) -> String:
	var p := progress if not progress.is_empty() else load_progress()
	var entry: Dictionary = (p.get("scenarios", {}) as Dictionary).get(scenario_id, {})
	var medal := str(entry.get("best_medal", "none"))
	return medal if MEDALS.has(medal) else "none"


static func wins(scenario_id: String, progress: Dictionary = {}) -> int:
	var p := progress if not progress.is_empty() else load_progress()
	var entry: Dictionary = (p.get("scenarios", {}) as Dictionary).get(scenario_id, {})
	return int(entry.get("wins", 0))


## The next scenario to play after `scenario_id`: the first open, unwon one
## after it in the campaign's order, else the first open, unwon one at all
## ("" when every open scenario is won or `scenario_id` is not a campaign one).
static func next_scenario(scenario_id: String, progress: Dictionary = {}) -> String:
	var p := progress if not progress.is_empty() else load_progress()
	var ids := order()
	var start := ids.find(scenario_id)
	if start < 0:
		return ""
	for k in range(1, ids.size() + 1):
		var id := ids[(start + k) % ids.size()]
		if id != scenario_id and is_unlocked(id, p) and wins(id, p) == 0:
			return id
	return ""


## Record a game's verdict. Returns {recorded, best_medal, improved,
## newly_unlocked: [...]}: what the end screen tells the player. A game
## outside the campaign (a sandbox game, a mod) records nothing, and the same
## end (`game_key`: scenario, verdict, start and end day) is recorded once,
## however often its screen is shown (a game scene opened on an ended game).
static func record_result(scenario_id: String, verdict: String, medal: String, net_result_nok: float,
		game_key: String = "") -> Dictionary:
	var outcome := {"recorded": false, "best_medal": "none", "improved": false, "newly_unlocked": PackedStringArray()}
	if not is_campaign_scenario(scenario_id):
		return outcome
	var before := load_progress()
	if game_key != "" and str(before.get("last_result", "")) == game_key:
		outcome["best_medal"] = best_medal(scenario_id, before)
		return outcome
	var locked_before := PackedStringArray()
	for id in order():
		if not is_unlocked(id, before):
			locked_before.append(id)

	var scenarios: Dictionary = (before.get("scenarios", {}) as Dictionary).duplicate(true)
	var entry: Dictionary = (scenarios.get(scenario_id, {}) as Dictionary).duplicate(true)
	var old_medal := best_medal(scenario_id, before)
	var won := verdict == VERDICT_WON
	var new_medal := medal if won and MEDALS.has(medal) and medal != "none" else "none"
	var best := new_medal if MEDALS.find(new_medal) > MEDALS.find(old_medal) else old_medal
	entry["best_medal"] = best
	entry["wins"] = int(entry.get("wins", 0)) + (1 if won else 0)
	entry["results"] = int(entry.get("results", 0)) + 1
	entry["last_verdict"] = verdict
	if won:
		entry["best_result_nok"] = maxf(float(entry.get("best_result_nok", net_result_nok)), net_result_nok)
	scenarios[scenario_id] = entry
	var after := {"version": FORMAT_VERSION, "scenarios": scenarios, "last_result": game_key}
	if not save_progress(after):
		return outcome

	var unlocked := PackedStringArray()
	for id in locked_before:
		if id != scenario_id and is_unlocked(id, after):
			unlocked.append(id)
	return {"recorded": true, "best_medal": best, "improved": best != old_medal, "newly_unlocked": unlocked}


## The campaign at a glance: {won, open, total} scenarios.
static func summary(progress: Dictionary = {}) -> Dictionary:
	var p := progress if not progress.is_empty() else load_progress()
	var won := 0
	var open := 0
	for id in order():
		won += 1 if wins(id, p) > 0 else 0
		open += 1 if is_unlocked(id, p) else 0
	return {"won": won, "open": open, "total": order().size()}


## Write the progress atomically (a temporary file, then a rename).
static func save_progress(progress: Dictionary) -> bool:
	var target := path()
	DirAccess.make_dir_recursive_absolute(target.get_base_dir())
	var tmp := target + ".tmp"
	var file := FileAccess.open(tmp, FileAccess.WRITE)
	if file == null:
		push_warning("CampaignProgress: cannot write %s (%s)" % [tmp, error_string(FileAccess.get_open_error())])
		return false
	file.store_string(JSON.stringify(progress, "\t", true))
	file.close()
	if FileAccess.file_exists(target):
		DirAccess.remove_absolute(target)
	var err := DirAccess.rename_absolute(tmp, target)
	if err != OK:
		push_warning("CampaignProgress: cannot replace %s (%s)" % [target, error_string(err)])
		return false
	return true


## Forget the progress (tests; a "new campaign" option later).
static func reset() -> void:
	for p in [path(), path() + ".tmp", path() + ".bad"]:
		if FileAccess.file_exists(p):
			DirAccess.remove_absolute(p)


static func _read_json(file_path: String) -> Variant:
	if not FileAccess.file_exists(file_path):
		return null
	var text := FileAccess.get_file_as_string(file_path)
	var json := JSON.new()
	if json.parse(text) != OK:
		return null
	return json.data
