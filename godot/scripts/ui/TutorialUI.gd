extends PanelContainer

## TutorialUI — the first season's guide card (roadmap U2-02).
##
## Første sesong teaches one verb a step: the card shows why (the text), then
## asks the player to do it (the action box), points at the control with the
## pulsing ring (TutorialHighlight, via TutorialTargets) and paces the clock:
## a "hold" step stops it until the player has done it, a "slow" step drops it
## to 1× once, a "run" step leaves it to the player (the "start the clock"
## step pauses it first so the player starts it). A step completes on the real
## event in the engine (TutorialSystem): the card only shows it, asks the
## engine to check after the player's commands, and never completes a step
## itself. One dot per step (the engine's count). No tutorial outside
## Første sesong (D19): the card stays hidden there.
##
## Node structure (scenes/ui/TutorialUI.tscn, wired with node_paths):
##   TutorialUI (PanelContainer, Glass)
##   └── VBox
##       ├── Header: StepLabel · SkipButton · CloseButton
##       ├── ProgressDots
##       ├── TitleLabel
##       ├── DescriptionLabel
##       ├── ActionBox: ActionCaption · HintLabel · GoalBox (GoalBar, GoalLabel) · Footer (PaceLabel, ShowMeButton)
##       ├── RewardLabel (a completed step, for a moment)
##       └── DoneButton (the finished season)

## The hold this card puts on the clock while a step waits for the player.
const CLOCK_HOLD := "tutorial"
## How often the card asks the engine to check the step (s) while it waits.
const CHECK_INTERVAL_S := 0.25
const DOT_SIZE := Vector2(12, 6)
const DOT_CURRENT_SIZE := Vector2(20, 6)
## How long a "Steg n fullført" line stays.
const STEP_DONE_S := 2.4
## Distance from the screen edge, and below the tab rail.
const EDGE_MARGIN := 16.0
const RAIL_GAP := 8.0
## Where the card may sit, in order of preference: it moves to the next
## corner when it would cover the control the step points at (or the open
## notification drawer).
const DOCKS := ["bottom_left", "bottom_right", "top_left", "top_right"]

@export var step_label: Label
@export var close_button: Button
@export var skip_button: Button
@export var title_label: Label
@export var description_label: Label
@export var hint_label: Label
@export var reward_label: Label
@export var progress_dots_container: HBoxContainer
@export var action_box: PanelContainer
@export var action_caption: Label
@export var goal_box: Control
@export var goal_bar: ProgressBar
@export var goal_label: Label
@export var pace_label: Label
@export var show_me_button: Button
@export var done_button: Button

## The engine's current step ({} before the first read).
var status: Dictionary = {}

var _sim: Node = null
var _steps: Array = []
var _held := false
var _paced_step := -1
var _minimised := false
var _closed_done := false
var _check_timer := 0.0
var _reward_timer := 0.0
var _highlight: TutorialHighlight = null
var _targets: TutorialTargets = null
var _skip_dialog: ConfirmationDialog = null
var _dock := "bottom_left"

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	visible = false
	# Placed in code (_place): never over the control the step points at.
	set_anchors_preset(Control.PRESET_TOP_LEFT)
	# The toast stack rests above the card when the card sits in its corner.
	add_to_group(&"toast_keep_clear")
	_sim = get_node_or_null("/root/SimBridge")
	if _sim != null:
		_sim.state_updated.connect(_on_state_updated)
		_sim.tutorial_step_completed.connect(_on_step_completed)
	close_button.pressed.connect(func() -> void: _set_minimised(not _minimised))
	skip_button.pressed.connect(_on_skip_pressed)
	show_me_button.pressed.connect(show_me)
	done_button.pressed.connect(_on_done_pressed)
	skip_button.text = tr("TUTORIAL_CARD_SKIP")
	skip_button.tooltip_text = tr("TUTORIAL_CARD_SKIP_TIP")
	show_me_button.text = tr("TUTORIAL_CARD_SHOW_ME")
	show_me_button.tooltip_text = tr("TUTORIAL_CARD_SHOW_ME_TIP")
	done_button.text = tr("TUTORIAL_CARD_HIDE")
	for b: Button in [close_button, skip_button, show_me_button, done_button]:
		b.focus_mode = Control.FOCUS_NONE   # Space stays the pause key
	_setup_highlight.call_deferred()
	refresh()


func _exit_tree() -> void:
	_release_clock()
	if _highlight != null and is_instance_valid(_highlight):
		_highlight.queue_free()


func _process(delta: float) -> void:
	if _reward_timer > 0.0:
		_reward_timer -= delta
		if _reward_timer <= 0.0:
			reward_label.visible = false
	if visible:
		_place()
	if not _is_active():
		return
	# The engine checks the step at the end of every day; while the clock
	# stands (a hold step) the card asks it after the player's commands.
	_check_timer -= delta
	if _check_timer <= 0.0:
		_check_timer = CHECK_INTERVAL_S
		if _sim != null and _sim.tutorial_check():
			return   # state_updated refreshed the card
		var step := int(status.get("step", 0))
		status = _sim.get_tutorial_status()
		if int(status.get("step", 0)) != step or not bool(status.get("active", false)):
			refresh()
			return
		_refresh_goal()
	_update_highlight()

# ── The card ──────────────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	refresh()


## Read the engine's step and show it (also applies the step's pace).
func refresh() -> void:
	if _sim == null:
		visible = false
		return
	status = _sim.get_tutorial_status()
	var active := bool(status.get("active", false))
	var earned := bool(status.get("earned", false)) and bool(status.get("in_scenario", false))
	if not GameplayPrefs.tutorial_enabled() or not (active or (earned and not _closed_done)):
		visible = false
		_release_clock()
		_clear_highlight()
		return
	if _steps.size() != int(status.get("total", 0)):
		_steps = _sim.get_tutorial_steps()
		_build_dots()
	visible = true
	if earned:
		_show_done()
		return
	_show_step()
	_apply_pace()


func _show_step() -> void:
	var step := int(status.get("step", 1))
	var total := int(status.get("total", 0))
	step_label.text = tr("TUTORIAL_CARD_OVERLINE") % [step, total]
	title_label.text = tr(str(status.get("title_key", "")))
	description_label.text = tr(str(status.get("text_key", "")))
	hint_label.text = tr(str(status.get("do_key", "")))
	var waits := str(status.get("kind", "")) == "wait"
	action_caption.text = tr("TUTORIAL_CARD_WAIT") if waits else tr("TUTORIAL_CARD_DO")
	action_caption.theme_type_variation = &"OverlineInfo" if waits else &"Overline"
	action_box.theme_type_variation = &"CalloutInfo" if waits else &"CalloutAccent"
	pace_label.text = _pace_text(str(status.get("pace", "")))
	show_me_button.visible = not str(status.get("target", "")).is_empty() and not waits
	done_button.visible = false
	action_box.visible = true
	_refresh_goal()
	_update_dots(int(status.get("completed", 0)), step)
	_apply_minimised()


func _pace_text(pace: String) -> String:
	match pace:
		"hold":
			return tr("TUTORIAL_CARD_CLOCK_HELD")
		"slow":
			return tr("TUTORIAL_CARD_CLOCK_SLOW")
		_:
			return tr("TUTORIAL_CARD_CLOCK_RUNS")


## The goal line of a step that has one (the growth step's weight, the funding step's cash).
func _refresh_goal() -> void:
	var progress: Dictionary = status.get("progress", {})
	goal_box.visible = not progress.is_empty()
	if progress.is_empty():
		return
	var value := float(progress.get("value", 0.0))
	var goal := float(progress.get("goal", 1.0))
	goal_bar.value = clampf(value / goal, 0.0, 1.0) if goal > 0.0 else 0.0
	if str(progress.get("unit", "")) == "kg":
		goal_label.text = tr("TUTORIAL_CARD_PROGRESS_KG") % [SimFormatters.num(value, 2), SimFormatters.num(goal, 1)]
	else:
		goal_label.text = tr("TUTORIAL_CARD_PROGRESS_NOK") % [SimFormatters.format_nok(value), SimFormatters.format_nok(goal)]


func _show_done() -> void:
	var total := int(status.get("total", 0))
	step_label.text = tr("TUTORIAL_CARD_OVERLINE") % [total, total]
	title_label.text = tr("TUTORIAL_CARD_DONE_TITLE")
	description_label.text = tr("TUTORIAL_CARD_DONE_TEXT")
	action_box.visible = false
	done_button.visible = true
	skip_button.visible = false
	_update_dots(total, total + 1)
	_release_clock()
	_clear_highlight()


func _on_done_pressed() -> void:
	_closed_done = true
	visible = false


## A step completed: the line under the card says which, for a moment.
func _on_step_completed(step_number: int, title_key: String) -> void:
	if not visible:
		return
	reward_label.text = _tr_step_complete(step_number, title_key)
	reward_label.visible = true
	_reward_timer = STEP_DONE_S
	if _minimised:
		_set_minimised(false)


## "Steg n fullført: <tittel>" — the step's number and title, never an amount (D16).
func _tr_step_complete(step_number: int, title_key: String) -> String:
	return tr("TUTORIAL_CARD_STEP_DONE") % [step_number, tr(title_key)]

# ── Progress dots: one per step ───────────────────────────────────────────────

func _build_dots() -> void:
	for child in progress_dots_container.get_children():
		progress_dots_container.remove_child(child)
		child.queue_free()
	for step in _steps:
		var dot := ColorRect.new()
		dot.custom_minimum_size = DOT_SIZE
		dot.mouse_filter = Control.MOUSE_FILTER_PASS
		dot.tooltip_text = tr("TUTORIAL_CARD_DOT_TIP") % [int(step.get("number", 0)), tr(str(step.get("title_key", "")))]
		progress_dots_container.add_child(dot)


func _update_dots(completed: int, current_step: int) -> void:
	var dots := progress_dots_container.get_children()
	for i in dots.size():
		var dot := dots[i] as ColorRect
		if i < completed:
			dot.color = LakskongenTheme.INK_OK
			dot.custom_minimum_size = DOT_SIZE
		elif i == current_step - 1:
			dot.color = LakskongenTheme.INK_ACCENT
			dot.custom_minimum_size = DOT_CURRENT_SIZE
		else:
			dot.color = Color(LakskongenTheme.INK_STROKE, 0.6)
			dot.custom_minimum_size = DOT_SIZE


## Number of progress dots (tests: equals the engine's step count).
func dot_count() -> int:
	return progress_dots_container.get_child_count()

# ── Pacing: the clock waits while a step waits for the player ─────────────────

func _apply_pace() -> void:
	var step := int(status.get("step", 0))
	var pace := str(status.get("pace", ""))
	var first_time := step != _paced_step
	_paced_step = step
	if pace == "hold":
		_hold_clock()
		return
	_release_clock()
	if not first_time:
		return
	if pace == "slow":
		_sim.set_time_speed(1.0)
	elif str(status.get("kind", "")) == "do":
		# A "do" step that needs the clock (start it): it waits paused for the player.
		_sim.set_paused(true)


func _hold_clock() -> void:
	if not _held and _sim != null:
		_sim.hold_clock(CLOCK_HOLD)
		_held = true


func _release_clock() -> void:
	if _held and _sim != null:
		_sim.release_clock(CLOCK_HOLD)
	_held = false

# ── The pointer ───────────────────────────────────────────────────────────────

func _setup_highlight() -> void:
	var scene := _game_scene()
	if scene == null:
		return
	_targets = TutorialTargets.new(scene)
	_highlight = TutorialHighlight.new()
	var layer := scene.get_node_or_null("EventLayer")
	(layer if layer != null else scene).add_child(_highlight)


## The game scene this card sits in (GameScene: UILayer/TutorialUI).
func _game_scene() -> Node:
	var layer := get_parent()
	return layer.get_parent() if layer != null else null


func _update_highlight() -> void:
	if _highlight == null or _targets == null:
		return
	# Last in the event layer: above the planner and the report sheets.
	var parent := _highlight.get_parent()
	if parent != null and _highlight.get_index() != parent.get_child_count() - 1:
		parent.move_child(_highlight, -1)
	if not visible or _modal_open():
		_highlight.clear()
		return
	var found := _targets.resolve(str(status.get("target", "")))
	_highlight.point_at(found.get("control"), bool(found.get("route", false)))


func _clear_highlight() -> void:
	if _highlight != null:
		_highlight.clear()


## A card or menu that takes the screen: the pointer waits behind it.
func _modal_open() -> bool:
	var scene := _game_scene()
	if scene == null:
		return false
	for path in ["%BriefingCard", "%CriticalEventCard", "%PauseMenu", "%ScenarioEndScreen"]:
		var node := scene.get_node_or_null(path)
		if node != null and bool(node.is_open()):
			return true
	return _skip_dialog != null and _skip_dialog.visible


## "Vis meg": bring the target into view (scroll its list) and send a ring in.
func show_me() -> void:
	_update_highlight()
	if _highlight == null or not _highlight.is_pointing():
		return
	var target := _highlight.target()
	var node := target.get_parent()
	while node != null:
		if node is ScrollContainer:
			(node as ScrollContainer).ensure_control_visible(target)
			break
		node = node.get_parent()
	_highlight.emphasize()


## The pointer (UI flow tests read where it points).
func highlight() -> TutorialHighlight:
	return _highlight


## True while the card holds the clock for a step.
func is_holding_clock() -> bool:
	return _held

# ── Placement: never over what the step points at ─────────────────────────────

## Put the card in its corner; move to the next corner when it would cover
## the pointed-at control or the open notification drawer.
func _place() -> void:
	var screen := get_viewport().get_visible_rect().size
	var card_size := get_combined_minimum_size()
	var avoid: Array[Rect2] = []
	if _highlight != null and _highlight.is_pointing():
		avoid.append(_highlight.ring_rect().grow(RING_CLEARANCE))
	var drawer := _drawer_rect()
	if drawer.has_area():
		avoid.append(drawer)
	var keep := _dock
	if _blocked(_dock_rect(_dock, screen, card_size), avoid):
		for dock: String in DOCKS:
			if not _blocked(_dock_rect(dock, screen, card_size), avoid):
				keep = dock
				break
	_dock = keep
	var rect := _dock_rect(_dock, screen, card_size)
	if not position.is_equal_approx(rect.position) or not size.is_equal_approx(rect.size):
		position = rect.position
		size = rect.size


## Clearance kept between the card and the pointer's ring (px).
const RING_CLEARANCE := 12.0


func _dock_rect(dock: String, screen: Vector2, card_size: Vector2) -> Rect2:
	var top := _rail_bottom() + RAIL_GAP
	var x := EDGE_MARGIN if dock.ends_with("left") else screen.x - EDGE_MARGIN - card_size.x
	var y := screen.y - EDGE_MARGIN - card_size.y if dock.begins_with("bottom") else top
	return Rect2(Vector2(x, y), card_size)


static func _blocked(rect: Rect2, avoid: Array[Rect2]) -> bool:
	for r in avoid:
		if rect.intersects(r):
			return true
	return false


func _rail_bottom() -> float:
	var scene := _game_scene()
	var rail: Control = scene.find_child("RailPanel", true, false) as Control if scene != null else null
	return rail.get_global_rect().end.y if rail != null and rail.is_visible_in_tree() else 104.0


func _drawer_rect() -> Rect2:
	var scene := _game_scene()
	var panel: Control = scene.get_node_or_null("UILayer/NotificationCenter/Panel") as Control if scene != null else null
	return panel.get_global_rect() if panel != null and panel.is_visible_in_tree() else Rect2()


## Which corner the card sits in ("bottom_left", "bottom_right", "top_left", "top_right").
func dock() -> String:
	return _dock

# ── Skip, minimise ────────────────────────────────────────────────────────────

func _on_skip_pressed() -> void:
	if _skip_dialog == null:
		_skip_dialog = ConfirmationDialog.new()
		_skip_dialog.name = "SkipDialog"
		_skip_dialog.dialog_text = tr("TUTORIAL_CARD_SKIP_CONFIRM")
		_skip_dialog.ok_button_text = tr("TUTORIAL_CARD_SKIP_OK")
		_skip_dialog.cancel_button_text = tr("TUTORIAL_CARD_SKIP_CANCEL")
		_skip_dialog.dialog_autowrap = true
		_skip_dialog.min_size = Vector2i(420, 0)
		_skip_dialog.confirmed.connect(_on_skip_confirmed)
		add_child(_skip_dialog)
	_skip_dialog.popup_centered()


func _on_skip_confirmed() -> void:
	if _sim != null:
		_sim.skip_tutorial()
	refresh()


## The skip confirmation (UI flow tests).
func skip_dialog() -> ConfirmationDialog:
	return _skip_dialog


func _set_minimised(value: bool) -> void:
	_minimised = value
	_apply_minimised()


func _apply_minimised() -> void:
	var open := not _minimised
	description_label.visible = open
	action_box.visible = open and not done_button.visible
	progress_dots_container.visible = open
	close_button.text = "+" if _minimised else "–"
	close_button.tooltip_text = tr("TUTORIAL_CARD_EXPAND_TIP") if _minimised else tr("TUTORIAL_CARD_MINIMIZE_TIP")
	reset_size()


func _is_active() -> bool:
	return visible and bool(status.get("active", false))
