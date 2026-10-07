extends Control

## ScenarioGoalHUD — top-right tracker for the active scenario goal.
## Reads the active goal from ScenarioManager (autoload) and falls back to the
## SimBridge scenario roster when a game was started outside ScenarioManager.
## Refreshes on scenario_loaded / goal_progress_updated and on SimBridge ticks.
## All node lookups are null-guarded so a partially-authored scene still loads.

@onready var _goal_label: Label = get_node_or_null("PanelContainer/VBoxContainer/GoalLabel")
@onready var _progress_bar: ProgressBar = get_node_or_null("PanelContainer/VBoxContainer/ProgressBar")
@onready var _time_label: Label = get_node_or_null("PanelContainer/VBoxContainer/TimeLabel")

func _ready() -> void:
	_connect_signals()
	_refresh()

func _connect_signals() -> void:
	var manager = get_node_or_null("/root/ScenarioManager")
	if manager != null:
		if manager.has_signal("scenario_loaded"):
			manager.scenario_loaded.connect(_on_scenario_loaded)
		if manager.has_signal("goal_progress_updated"):
			manager.goal_progress_updated.connect(_on_goal_progress_updated)
		if manager.has_signal("goal_achieved"):
			manager.goal_achieved.connect(_on_goal_resolved)
		if manager.has_signal("goal_failed"):
			manager.goal_failed.connect(_on_goal_resolved)

	var sim = get_node_or_null("/root/SimBridge")
	if sim != null and sim.has_signal("state_updated"):
		sim.state_updated.connect(_on_state_updated)

# ── Signal handlers ────────────────────────────────────────────────────────────

func _on_scenario_loaded(_scenario: Dictionary) -> void:
	_refresh()

func _on_goal_progress_updated(progress: float, description: String) -> void:
	_apply(progress, description, false, false)

func _on_goal_resolved() -> void:
	_refresh()

func _on_state_updated() -> void:
	_refresh()

# ── Rendering ──────────────────────────────────────────────────────────────────

func _refresh() -> void:
	var manager = get_node_or_null("/root/ScenarioManager")
	var scenario: Dictionary = {}
	var progress := 0.0
	var achieved := false
	var failed := false

	if manager != null:
		if manager.has_method("get_current_scenario"):
			scenario = manager.get_current_scenario()
		if manager.has_method("get_goal_progress"):
			progress = float(manager.get_goal_progress())
		if manager.has_method("is_goal_achieved"):
			achieved = bool(manager.is_goal_achieved())
		if manager.has_method("is_goal_failed"):
			failed = bool(manager.is_goal_failed())

	if scenario.is_empty():
		scenario = _active_scenario_from_sim()

	var description := _describe_goal(scenario)
	_apply(progress, description, achieved, failed)
	_set_time(scenario, progress)

func _apply(progress: float, description: String, achieved: bool, failed: bool) -> void:
	var percent := int(round(clampf(progress, 0.0, 1.0) * 100.0))
	var text := description if not description.is_empty() else "Goal"
	if achieved:
		text = "✓ %s — %d%%" % [text, percent]
	elif failed:
		text = "✗ %s — mislyktes" % text
	else:
		text = "Goal: %s" % text

	if _goal_label != null:
		_goal_label.text = text
	if _progress_bar != null:
		_progress_bar.value = percent

func _set_time(scenario: Dictionary, progress: float) -> void:
	if _time_label == null:
		return
	var percent := int(round(clampf(progress, 0.0, 1.0) * 100.0))

	var goal = scenario.get("goal", {})
	var limit_years := 0.0
	if goal is Dictionary:
		limit_years = float(goal.get("time_limit_years", 0))
	if limit_years <= 0.0:
		limit_years = float(scenario.get("time_limit_days", 0)) / 365.0

	if limit_years <= 0.0:
		_time_label.text = "%d%%" % percent
		return

	var day := 0
	var sim = get_node_or_null("/root/SimBridge")
	if sim != null and "current_day" in sim:
		day = int(sim.current_day)
	var years_left := maxf(limit_years - float(day) / 365.0, 0.0)
	_time_label.text = "%d%% — ⏰ %.1f år igjen" % [percent, years_left]

# ── Helpers ────────────────────────────────────────────────────────────────────

func _describe_goal(scenario: Dictionary) -> String:
	if scenario.is_empty():
		return ""
	var goal = scenario.get("goal", {})
	if goal is Dictionary and not str(goal.get("description", "")).is_empty():
		return str(goal.get("description"))
	for key in ["description", "win", "name"]:
		var value := str(scenario.get(key, ""))
		if not value.is_empty():
			return value
	return ""

## Fallback: resolve the active scenario dict from the SimBridge roster.
func _active_scenario_from_sim() -> Dictionary:
	var sim = get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_active_scenario_id"):
		return {}
	var id := str(sim.get_active_scenario_id())
	if id.is_empty():
		return {}
	if sim.has_method("get_available_scenarios"):
		for entry in sim.get_available_scenarios():
			if entry is Dictionary and str(entry.get("id", "")) == id:
				return entry
	return {"id": id}
