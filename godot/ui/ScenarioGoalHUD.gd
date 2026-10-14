extends Control

## ScenarioGoalHUD — top-right tracker for the active scenario goal.
## Reads progress from the engine (SimBridge.get_scenario_goal, measured the way
## the win check judges it; review F949: the ScenarioManager tracker it used to
## read was never initialised in the player flow and sat at 0%). Falls back to
## ScenarioManager when no engine goal is available.
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
	var sim = get_node_or_null("/root/SimBridge")
	if sim != null and sim.has_method("get_scenario_goal"):
		var goal: Dictionary = sim.get_scenario_goal()
		if not goal.is_empty():
			var progress := float(goal.get("progress", 0.0))
			_apply(progress, str(goal.get("objective", "")), bool(goal.get("won", false)), bool(goal.get("lost", false)))
			_set_days_left(int(goal.get("days_left", -1)), progress)
			return

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
	var text := description if not description.is_empty() else tr("UI_GOAL_FREE_PLAY")
	if achieved:
		text = "✓ %s — %s" % [text, SimFormatters.percent(percent / 100.0)]
	elif failed:
		text = "✗ " + tr("UI_GOAL_FAILED") % text
	else:
		text = tr("UI_GOAL_PREFIX") % text

	if _goal_label != null:
		_goal_label.text = text
	if _progress_bar != null:
		_progress_bar.value = percent

## Time left from the engine (days since the scenario started; -1 = no limit).
func _set_days_left(days_left: int, progress: float) -> void:
	if _time_label == null:
		return
	var percent := int(round(clampf(progress, 0.0, 1.0) * 100.0))
	if days_left < 0:
		_time_label.text = SimFormatters.percent(percent / 100.0)
	else:
		_time_label.text = "%s — ⏰ %s" % [SimFormatters.percent(percent / 100.0),
			tr("UI_GOAL_YEARS_LEFT") % SimFormatters.num(float(days_left) / 365.0, 1)]

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
		_time_label.text = SimFormatters.percent(percent / 100.0)
		return

	var day := 0
	var sim = get_node_or_null("/root/SimBridge")
	if sim != null and "current_day" in sim:
		day = int(sim.current_day)
	var years_left := maxf(limit_years - float(day) / 365.0, 0.0)
	_time_label.text = "%s — ⏰ %s" % [SimFormatters.percent(percent / 100.0),
		tr("UI_GOAL_YEARS_LEFT") % SimFormatters.num(years_left, 1)]

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
