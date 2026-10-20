extends RefCounted
## Roadmap U2-02 (learn-by-doing first season), checked in the running game
## with real input. UiFlowTests runs these through run(self) and counts them
## with its own _check.
##
## 1. The card shows on a tutorial step (its @export nodes are wired), one
##    progress dot per step, the pointer on the step's control, the clock
##    held while a "do" step waits; a click on the pointed-at tab is step 1.
## 2. po3_red_sone, ISA and the algae scenario: every tab open on day 0, no
##    card, no hold; the research tab opens by its shortcut.
## 3. "Hopp over" by real clicks: the tutorial is skipped, every tab opens,
##    and Første sesong is not won (the Tutorial win condition is false).
## 4. Acceptance 2: a seeded player that only clicks what the pointer points
##    at (and presses the keys the card names) wins Første sesong. Nothing
##    calls a bridge verb: every step is a click, a key or the clock running.
##    On the way: every "hold" step stops the clock when it comes up, the
##    "slow" steps drop it to 1×, the pointer's ring encloses the control.

const GAME_SCENE := "res://scenes/GameScene.tscn"
const WIN_SEED := 9
## Real seconds the input player may take for the whole season.
const WIN_TIMEOUT_S := 300.0
const SETTLE_S := 0.25
## Wheel ticks per scroll attempt.
const WHEEL_TICKS := 3
const NO_EVENT_SCENARIOS := ["po3_red_sone", "isa_utbrudd", "algekatastrofe"]

var t: Node = null


func run(runner: Node) -> void:
	t = runner
	GameplayPrefs.force_tutorial(true)
	GameplayPrefs.force_auto_pause(true)
	MotionPrefs.force(true)
	await _the_card_shows_with_a_dot_per_step_and_points_at_the_tab()
	await _every_tab_is_open_outside_the_first_season()
	await _a_skip_by_input_is_no_win()
	await _a_player_using_only_input_wins_the_first_season()
	MotionPrefs.force(null)
	GameplayPrefs.force_auto_pause(null)
	GameplayPrefs.force_tutorial(null)


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


func _frames(n: int) -> void:
	for i in n:
		await t.get_tree().process_frame


func _settle(s: float = SETTLE_S) -> void:
	await t.get_tree().create_timer(s).timeout


func _spawn() -> Node:
	var scene: Node = (load(GAME_SCENE) as PackedScene).instantiate()
	t.add_child(scene)
	await _frames(8)
	return scene


func _free(scene: Node) -> void:
	scene.queue_free()
	await _frames(2)


func _card(scene: Node) -> Control:
	return scene.get_node("UILayer/TutorialUI") as Control


## A real click on a control's centre (embedded dialogs: its window's offset).
func _click(control: Control) -> void:
	if control == null or not control.is_visible_in_tree():
		return
	var at := control.get_global_rect().get_center()
	var window := control.get_window()
	if window != null and window != t.get_window() and window.is_embedded():
		at += Vector2(window.position)
	await t._click_at(at)


# ── 1. The card ───────────────────────────────────────────────────────────────

func _the_card_shows_with_a_dot_per_step_and_points_at_the_tab() -> void:
	_check(t._sim.start_qa_scenario("forste_sesong", 41), "first season: Første sesong starts")
	var scene := await _spawn()
	var card := _card(scene)
	var ui: Control = scene.get_node("UILayer/GameUI")
	await _settle(0.4)
	var wired := card.step_label != null and card.title_label != null and card.description_label != null \
		and card.hint_label != null and card.progress_dots_container != null and card.action_box != null \
		and card.show_me_button != null and card.skip_button != null
	_check(wired, "first season: the card's nodes are wired (node_paths in TutorialUI.tscn)")
	_check(card.is_visible_in_tree() and card.title_label.text == tr("TUTORIAL_OPEN_PENS_TITLE")
			and card.hint_label.text == tr("TUTORIAL_OPEN_PENS_DO"),
		"first season: the card shows step 1 ('%s')" % card.title_label.text)
	var total := int(t._sim.tutorial_total_steps)
	_check(total == 18 and card.dot_count() == total,
		"first season: one progress dot per step (%d dots, %d steps)" % [card.dot_count(), total])
	_check(card.step_label.text == tr("TUTORIAL_CARD_OVERLINE") % [1, total],
		"first season: the overline counts the steps ('%s')" % card.step_label.text)
	var tab: Control = ui.tab_route("pens")
	var ring: Rect2 = card.highlight().ring_rect()
	_check(card.highlight().target() == tab and ring.encloses(tab.get_global_rect()),
		"first season: the pointer rings the Merder tab (%s around %s)" % [str(ring), str(tab.get_global_rect())])
	_check(t._sim.is_clock_held("tutorial") and card.is_holding_clock(), "first season: a 'do' step holds the clock")
	var day := int(t._sim.current_day)
	t.get_viewport().gui_release_focus()
	await t._press_key(KEY_SPACE, 32)
	await _settle(1.4)
	_check(int(t._sim.current_day) == day, "first season: Space cannot run the clock past a waiting step (day %d)" % int(t._sim.current_day))
	await t._lint_screen("first season card")
	await _click(tab)
	await _settle()
	_check(int(t._sim.tutorial_step_number) == 2 and ui.get_active_tab() == "pens",
		"first season: a click on the ringed tab completes step 1 (step %d)" % int(t._sim.tutorial_step_number))
	var pens: Control = ui.panel_of("pens")
	_check(card.highlight().target() == pens.feed_plan_card() and card.title_label.text == tr("TUTORIAL_FEED_PLAN_TITLE"),
		"first season: step 2 rings the feeding plan card")
	# Locked tabs say which step opens them.
	var research_tip: String = (ui._tab_buttons["research"] as Button).tooltip_text
	_check((ui._tab_buttons["research"] as Button).disabled and research_tip == tr("TUTORIAL_TAB_LOCKED_TIP") % 11,
		"first season: the research tab is locked until step 11 and says so ('%s')" % research_tip)
	await _free(scene)
	_check(not t._sim.is_clock_held("tutorial"), "first season: a freed scene gives the clock back")


# ── 2. D19: no tutorial and no gate elsewhere ─────────────────────────────────

func _every_tab_is_open_outside_the_first_season() -> void:
	for scenario: String in NO_EVENT_SCENARIOS:
		_check(t._sim.start_qa_scenario(scenario, 7), "no tutorial: %s starts" % scenario)
		var scene := await _spawn()
		var ui: Control = scene.get_node("UILayer/GameUI")
		var card := _card(scene)
		var locked: Array[String] = []
		for id: String in ui._tab_buttons:
			if (ui._tab_buttons[id] as Button).disabled or not ui._is_tab_unlocked(id):
				locked.append(id)
		_check(locked.is_empty(), "no tutorial: every tab is open on day 0 in %s %s" % [scenario, str(locked)])
		_check(not card.visible and not t._sim.is_clock_held("tutorial") and not bool(t._sim.tutorial_active),
			"no tutorial: %s shows no card and holds no clock" % scenario)
		t.get_viewport().gui_release_focus()
		await t._press_command_key(KEY_6)
		await _frames(3)
		_check(ui.get_active_tab() == "research", "no tutorial: Cmd+6 opens research in %s (%s)" % [scenario, ui.get_active_tab()])
		await _free(scene)


# ── 3. A skip is no win ───────────────────────────────────────────────────────

func _a_skip_by_input_is_no_win() -> void:
	_check(t._sim.start_qa_scenario("forste_sesong", 23), "skip: Første sesong starts")
	var scene := await _spawn()
	var card := _card(scene)
	var ui: Control = scene.get_node("UILayer/GameUI")
	await _click(card.skip_button)
	await _settle()
	var dialog: ConfirmationDialog = card.skip_dialog()
	_check(dialog != null and dialog.visible and dialog.dialog_text == tr("TUTORIAL_CARD_SKIP_CONFIRM"),
		"skip: 'Hopp over' asks first, and says a skip is no win")
	if dialog != null:
		await _click(dialog.get_ok_button())
		await _settle()
	_check(bool(t._sim.tutorial_skipped) and bool(t._sim.tutorial_is_complete) and not card.visible
			and not t._sim.is_clock_held("tutorial"),
		"skip: the confirm skips the tutorial, hides the card and gives the clock back")
	var locked := 0
	for id: String in ui._tab_buttons:
		locked += 1 if (ui._tab_buttons[id] as Button).disabled else 0
	_check(locked == 0, "skip: every tab opens (%d locked)" % locked)
	t._sim.set_paused(false)
	var day := int(t._sim.current_day)
	await t._wait_until(func() -> bool: return int(t._sim.current_day) > day + 1, 6.0)
	t._sim.set_paused(true)
	var goal: Dictionary = t._sim.get_scenario_goal()
	var status: Dictionary = t._sim.get_tutorial_status()
	_check(not bool(goal.get("won", true)) and not bool(status.get("earned", true)) and float(goal.get("progress", 1.0)) == 0.0,
		"skip: after a skip the Tutorial win condition is false (won %s, progress %.2f)" % [str(goal.get("won")), float(goal.get("progress", -1.0))])
	await _free(scene)


# ── 4. The season, by input only ──────────────────────────────────────────────

func _a_player_using_only_input_wins_the_first_season() -> void:
	_check(t._sim.start_qa_scenario("forste_sesong", WIN_SEED), "input season: Første sesong starts (seed %d)" % WIN_SEED)
	var scene := await _spawn()
	var card := _card(scene)
	var crit: Control = scene.get_node("%CriticalEventCard")
	var end_screen: Node = scene.get_node("%ScenarioEndScreen")
	end_screen.scene_change_override = func(_path: String) -> void: pass
	var seen := {}            # step number -> what the step looked like when it came up
	var unreached := []       # steps whose pointer did not ring a visible control
	var deadline := Time.get_ticks_msec() + int(WIN_TIMEOUT_S * 1000.0)
	var actions := 0
	while Time.get_ticks_msec() < deadline:
		if bool(t._sim.get_scenario_goal().get("won", false)) or end_screen.is_open():
			break
		if crit.is_open():
			await _click(crit.continue_button)
			continue
		var report := HarvestScreens.report(t.get_tree())
		if report != null and report.is_open():
			await _settle(0.3)
			await _click(report.close_button)
			continue
		var status: Dictionary = t._sim.get_tutorial_status()
		if not bool(status.get("active", false)):
			await _settle()
			continue
		var step := int(status.get("step", 0))
		if not seen.has(step):
			await _frames(3)
			seen[step] = _snapshot(card, status)
			if str(status.get("kind", "")) == "do" and not bool(seen[step]["ringed"]):
				unreached.append(step)
		actions += 1
		await _act(scene, card, status)
	var won := bool(t._sim.get_scenario_goal().get("won", false))
	await _settle(0.6)
	_check(won and end_screen.is_open() and str(end_screen.verdict()) == "won",
		"input season: a player using only clicks and keys wins Første sesong on day %d (%d actions, seed %d)" % [int(t._sim.current_day), actions, WIN_SEED])
	_check(int(t._sim.tutorial_completed_count) == 18 and bool(t._sim.get_tutorial_status().get("earned", false)),
		"input season: all 18 steps were done by play (%d)" % int(t._sim.tutorial_completed_count))
	var held_ok := true
	var slow_ok := true
	var holds := []
	for step: int in seen:
		var s: Dictionary = seen[step]
		if s["pace"] == "hold":
			held_ok = held_ok and bool(s["held"])
			holds.append(step)
		if s["pace"] == "slow":
			slow_ok = slow_ok and int(s["speed"]) == 1
	_check(seen.size() == 18 and held_ok and holds.size() >= 12,
		"input season: every 'hold' step stopped the clock when it came up (%d steps seen, holds at %s)" % [seen.size(), str(holds)])
	_check(slow_ok, "input season: the 'slow' steps (the harvest order, the funding) dropped the clock to 1×")
	_check(unreached.is_empty(), "input season: every 'do' step's pointer ringed a control on screen %s" % str(unreached))
	_check(not card.visible or card.done_button.visible, "input season: the card ends on the finished season")
	print("[U2-02] input season: won=%s day=%d actions=%d steps=%s" % [str(won), int(t._sim.current_day), actions, str(seen)])
	await _free(scene)


## What a step looked like when it came up: its pace, whether the clock was
## held, the speed, whether the pointer ringed a control.
func _snapshot(card: Control, status: Dictionary) -> Dictionary:
	var clock: Dictionary = t._sim.get_clock_state()
	var h: TutorialHighlight = card.highlight()
	var target := h.target() if h != null else null
	var ringed := target != null and h.ring_rect().encloses(target.get_global_rect().intersection(h.ring_rect()))
	return {"id": str(status.get("id", "")), "pace": str(status.get("pace", "")), "held": t._sim.is_clock_held("tutorial"),
		"speed": int(clock.get("speed", 0)), "day": int(t._sim.current_day), "ringed": ringed,
		"target": target.name if target != null else ""}


## Do what the current step asks, by input only.
func _act(scene: Node, card: Control, status: Dictionary) -> void:
	match str(status.get("id", "")):
		"start_clock":
			await _press(KEY_4, 52)
		"grow", "sale":
			await _keep_clock_running()
		"feed_plan":
			await _feed_plan_by_input(scene, card)
		"funding":
			var progress: Dictionary = status.get("progress", {})
			if float(progress.get("value", 0.0)) >= float(progress.get("goal", 1.0)):
				await _keep_clock_running()
			else:
				await _follow_pointer(card)
		"research":
			var ui: Control = scene.get_node("UILayer/GameUI")
			var tree: Control = ui.panel_of("research")
			if tree != null and tree.is_visible_in_tree() and tree.is_confirming():
				await _press(KEY_ENTER, 0)
			else:
				await _follow_pointer(card)
		_:
			await _follow_pointer(card)
	await _settle()


## Click the control the pointer rings; "Vis meg" first when it is scrolled away.
func _follow_pointer(card: Control) -> void:
	var h: TutorialHighlight = card.highlight()
	if h == null or not h.is_pointing():
		await _frames(2)
		return
	if h.is_scrolled_away():
		await _click(card.show_me_button)
		await _frames(2)
		return
	if await _wheel_into_view(h.target()):
		return
	await _click(h.target())


## Scroll the list around `control` with the mouse wheel until the control is
## whole on screen. True when it scrolled (the caller looks again).
func _wheel_into_view(control: Control) -> bool:
	var node := control.get_parent()
	while node != null and not (node is ScrollContainer):
		node = node.get_parent()
	if node == null:
		return false
	var scroll := node as ScrollContainer
	var view := scroll.get_global_rect()
	var r := control.get_global_rect()
	if view.encloses(r) or r.size.y > view.size.y:
		return false
	var down := r.end.y > view.end.y
	var before := scroll.scroll_vertical
	# A wheel tick is a press and a release, as the OS sends it: a press
	# alone stays in the viewport's mouse-focus mask and every later click
	# goes to the control under the wheel.
	for i in WHEEL_TICKS:
		for pressed: bool in [true, false]:
			var wheel := InputEventMouseButton.new()
			wheel.button_index = MOUSE_BUTTON_WHEEL_DOWN if down else MOUSE_BUTTON_WHEEL_UP
			wheel.pressed = pressed
			wheel.factor = 1.0
			wheel.position = view.get_center()
			wheel.global_position = wheel.position
			t.get_viewport().push_input(wheel, true)
	await _frames(2)
	return scroll.scroll_vertical != before   # at the end of the list: click what shows


## The feeding plan: the card's window list (a real click on it and on the
## "30 dager" row of its menu), then the ringed "Bruk på …".
func _feed_plan_by_input(scene: Node, card: Control) -> void:
	var pens: Control = (scene.get_node("UILayer/GameUI") as Control).panel_of("pens")
	var plan: FeedPlanCard = pens.feed_plan_card() if pens != null else null
	if plan == null or not plan.is_visible_in_tree():
		await _follow_pointer(card)
		return
	var h: TutorialHighlight = card.highlight()
	if h.is_scrolled_away():
		await _click(card.show_me_button)
		return
	if not plan.apply_button().disabled:
		await _follow_pointer(card)   # the pointer has moved to "Bruk på …"
		return
	var option: OptionButton = plan._window_option
	if await _wheel_into_view(option):
		return
	await _pick_from_option(option, plan.WINDOWS.find(30))


## A dropdown the way a mouse picks from it: press on the button (the menu
## opens), move to the row, release there.
func _pick_from_option(option: OptionButton, row: int) -> void:
	if row < 0:
		return
	var vp := t.get_viewport()
	var press := InputEventMouseButton.new()
	press.button_index = MOUSE_BUTTON_LEFT
	press.pressed = true
	press.position = option.get_global_rect().get_center()
	press.global_position = press.position
	vp.push_input(press, true)
	await _frames(3)
	var popup := option.get_popup()
	if not popup.visible:
		return
	var top := popup.get_theme_stylebox("panel").get_margin(SIDE_TOP)
	var bottom := popup.get_theme_stylebox("panel").get_margin(SIDE_BOTTOM)
	var item_h := (float(popup.size.y) - top - bottom) / float(popup.item_count)
	var at := Vector2(popup.position) + Vector2(popup.size.x * 0.5, top + item_h * (row + 0.5))
	var move := InputEventMouseMotion.new()
	move.position = at
	move.global_position = at
	vp.push_input(move, true)
	await _frames(2)
	var release := InputEventMouseButton.new()
	release.button_index = MOUSE_BUTTON_LEFT
	release.pressed = false
	release.position = at
	release.global_position = at
	vp.push_input(release, true)
	await _frames(3)
	# The menu took the release; the game window gets it too (as from the OS),
	# so the button that opened the menu lets go of the mouse.
	if not popup.visible:
		vp.push_input(release, true)
		await _frames(2)



func _keep_clock_running() -> void:
	var clock: Dictionary = t._sim.get_clock_state()
	if bool(clock.get("running", false)) and int(clock.get("speed", 0)) == 30:
		await _settle(0.2)
		return
	if (clock.get("holds", []) as Array).is_empty():
		await _press(KEY_4, 52)
	else:
		await _settle(0.2)


func _press(key: Key, unicode: int) -> void:
	t.get_viewport().gui_release_focus()
	await t._press_key(key, unicode)
