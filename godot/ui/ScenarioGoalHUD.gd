extends Control

## ScenarioGoalHUD — the scenario goal as a compact chip in the HUD bar.
##
## Screen region (roadmap U1-04): the chip lives inside the HUD's top bar,
## between the readouts and the speed controls, so it can never cover the tab
## rail or the speed/pause/inbox buttons. It shows progress and time left; the
## full objective is in its tooltip, and a click unfolds a details card (a
## popup, closed by a click outside or Esc).
##
## Reads progress from the engine (SimBridge.get_scenario_goal, measured the way
## the win check judges it; review F949: the ScenarioManager tracker it used to
## read was never initialised in the player flow and sat at 0%). Falls back to
## ScenarioManager when no engine goal is available. With no goal at all (a
## free game) the chip hides.
## Refreshes on scenario_loaded / goal_progress_updated and on SimBridge ticks.
## All node lookups are null-guarded so a partially-authored scene still loads.

## Days left under which a running goal is shown in the warning tone.
const DEADLINE_WARN_DAYS := 60
## Width of the details card that unfolds under the chip.
const DETAILS_WIDTH := 360

@onready var _progress_bar: ProgressBar = get_node_or_null("PanelContainer/VBoxContainer/ProgressBar")
@onready var _time_label: Label = get_node_or_null("PanelContainer/VBoxContainer/TimeLabel")
@onready var _details: PopupPanel = get_node_or_null("%Details")
@onready var _goal_label: Label = get_node_or_null("%GoalLabel")
@onready var _details_title: Label = get_node_or_null("%DetailsTitle")
@onready var _details_progress: Label = get_node_or_null("%DetailsProgress")

var _objective: String = ""
var _has_goal: bool = false

func _ready() -> void:
	if _details_title != null:
		_details_title.text = tr("UI_GOAL_DETAILS_TITLE")
	gui_input.connect(_on_gui_input)
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

# ── Public API ─────────────────────────────────────────────────────────────────

## True while a scenario goal is shown (the chip hides in a free game).
func has_goal() -> bool:
	return _has_goal

## The full objective text (also the chip's tooltip).
func get_objective() -> String:
	return _objective

## Unfold the details card under the chip (a click on the chip does this).
func show_details() -> void:
	if _details == null or not _has_goal:
		return
	var rect := get_global_rect()
	var pos := Vector2(rect.end.x - DETAILS_WIDTH, rect.end.y + 8.0)
	_details.popup(Rect2i(Vector2i(pos), Vector2i(DETAILS_WIDTH, 1)))
	_fit_details.call_deferred()

## The card is as tall as its wrapped text once the text has its width (a
## wrapped label measures its height at the width it was last laid out at):
## after one layout pass, ask for 1 px and let the popup clamp to its content.
func _fit_details() -> void:
	await get_tree().process_frame
	if _details == null or not _details.visible:
		return
	_details.size = Vector2i(DETAILS_WIDTH, 1)

# ── Signal handlers ────────────────────────────────────────────────────────────

func _on_gui_input(event: InputEvent) -> void:
	var mb := event as InputEventMouseButton
	if mb != null and mb.pressed and mb.button_index == MOUSE_BUTTON_LEFT:
		show_details()
		accept_event()

func _on_scenario_loaded(_scenario: Dictionary) -> void:
	_refresh()

func _on_goal_progress_updated(progress: float, description: String) -> void:
	_apply(progress, description, false, false, -1)

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
			_apply(float(goal.get("progress", 0.0)), str(goal.get("objective", "")),
				bool(goal.get("won", false)), bool(goal.get("lost", false)), int(goal.get("days_left", -1)))
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

	_apply(progress, _describe_goal(scenario), achieved, failed, _days_left(scenario))

## Chip text, bar and tone; details card and tooltip carry the objective.
func _apply(progress: float, description: String, achieved: bool, failed: bool, days_left: int) -> void:
	_has_goal = not description.is_empty()
	visible = _has_goal
	_objective = description
	var fraction := clampf(progress, 0.0, 1.0)
	var percent := SimFormatters.percent(fraction)

	var status := tr("UI_GOAL_CHIP") % percent
	var tone := &"Info"
	if achieved:
		status = tr("UI_GOAL_CHIP_WON")
		tone = &"Ok"
	elif failed:
		status = tr("UI_GOAL_CHIP_LOST")
		tone = &"Crit"
	elif days_left >= 0:
		status += " · " + tr("UI_GOAL_YEARS_LEFT") % SimFormatters.num(float(days_left) / 365.0, 1)
		if days_left < DEADLINE_WARN_DAYS:
			tone = &"Warn"

	if _time_label != null:
		_time_label.text = status
		_time_label.theme_type_variation = &"Caption" if tone == &"Info" else StringName("Caption" + String(tone))
	if _progress_bar != null:
		_progress_bar.value = int(round(fraction * 100.0))
		_progress_bar.theme_type_variation = StringName("ProgressBar" + String(tone))
	tooltip_text = tr("UI_GOAL_PREFIX") % description if _has_goal else ""
	if _goal_label != null:
		_goal_label.text = description
	if _details_progress != null:
		_details_progress.text = status

## Days left of a scenario dict's time limit from the sim day (-1 = no limit).
func _days_left(scenario: Dictionary) -> int:
	var goal = scenario.get("goal", {})
	var limit_days := 0.0
	if goal is Dictionary:
		limit_days = float(goal.get("time_limit_years", 0)) * 365.0
	if limit_days <= 0.0:
		limit_days = float(scenario.get("time_limit_days", 0))
	if limit_days <= 0.0:
		return -1
	var day := 0
	var sim = get_node_or_null("/root/SimBridge")
	if sim != null and "current_day" in sim:
		day = int(sim.current_day)
	return maxi(int(limit_days) - day, 0)

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
