extends RefCounted
## Roadmap U2-03 (end screens, failure ladder, campaign progress), checked in
## the running game with real input. UiFlowTests runs these through run(self)
## and counts them with its own _check.
##
## 1. A won game (ressursrente, seed 5, a player who harvests at 4.5 kg):
##    the end screen opens on the win with its medal, the report is the
##    engine's, the campaign records the win and opens the next scenarios;
##    "Fortsett å spille" returns to the same game.
## 2. Unlocks and medals persist: a picker read fresh from the campaign file
##    (as after a restart) shows the opened scenarios open, the medal, and
##    the still locked ones locked with what opens them.
## 3. A bankrupt game (ressursrente, seed 5, a passive player): the bank's
##    liquidity warning reaches the player >= 30 days and its bank call (a
##    critical card) >= 10 days before the bankruptcy; then only the end
##    screen takes input (keys, clicks, the camera); Tab and Enter work its
##    buttons. A game scene opened on the ended game opens on its verdict,
##    and "Prøv igjen" starts the scenario again.
## 4. A lost game (algekatastrofe, seed 1, passive): the bank call's card
##    leads to the Finance tab's bank card, whose rescue loan is taken by a
##    click; the season is lost at its end with the loan on the books; "Last
##    inn autolagring" goes back to the autosave, and the next loss offers
##    the next scenario by keyboard.
##
## A QA game replays exactly from its seed, so each game is first played
## without a game scene (a dry run) to find its days, then again with the
## scene over the last stretch: the scene sees the verdict land live.

const GAME_SCENE := "res://scenes/GameScene.tscn"
const MAIN_MENU_SCENE := "res://scenes/MainMenu.tscn"
const WIN_SCENARIO := "ressursrente"
const WIN_SEED := 5
const LOSS_SCENARIO := "algekatastrofe"
const LOSS_SEED := 1
const MAX_DAYS := 900
const HARVEST_KG := 4.5
const SETTLE_S := 0.6
## Days the game scene watches before the verdict (or the bank call).
const LEAD_DAYS := 3
## A camera that moved less than this answered no input (it still eases its
## last centimetres; a pan, a top-down switch or a fly moves metres).
const CAMERA_STILL_M := 0.5

var t: Node = null
var _scene_changes: Array[String] = []
var _events: Array[Dictionary] = []


func run(runner: Node) -> void:
	t = runner
	CampaignProgress.path_override = t.TEST_ROOT + "campaign_endscreen.json"
	CampaignProgress.reset()
	t._sim.game_event_raised.connect(_on_event)
	await _a_won_game_ends_on_its_medal()
	await _unlocks_and_medals_persist()
	await _a_bankrupt_game_is_warned_then_gated()
	await _a_lost_game_takes_the_rescue_loan_and_loads_the_autosave()
	t._sim.game_event_raised.disconnect(_on_event)
	CampaignProgress.reset()
	CampaignProgress.path_override = t.TEST_ROOT + "campaign.json"


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


func _on_event(ev: Dictionary) -> void:
	_events.append(ev)


func _frames(n: int) -> void:
	for i in n:
		await t.get_tree().process_frame


func _settle() -> void:
	await t.get_tree().create_timer(SETTLE_S).timeout


func _screen(scene: Node) -> Node:
	return scene.get_node("%ScenarioEndScreen")


func _spawn() -> Node:
	var scene: Node = await t._spawn_scene_over_current_game()
	_screen(scene).scene_change_override = func(path: String) -> void: _scene_changes.append(path)
	return scene


func _start(scenario: String, seed: int) -> bool:
	var ok: bool = t._sim.start_qa_scenario(scenario, seed)
	t._sim.set_paused(true)
	return ok


## The minimal harvest player: a site's fish go at 4.5 kg mean weight.
func _harvest_ready_sites() -> void:
	for site in t._sim.get_all_pens():
		if int(site.get("fish_count", 0)) > 0 and float(site.get("mean_weight_kg", 0.0)) >= HARVEST_KG:
			t._sim.harvest_pen(str(site.get("id", "")))


## One sim day of the scripted player: harvest at 4.5 kg, or take an open
## rescue offer. Returns true when the bank had called the company in that
## day (before any answer).
func _day(harvest: bool, rescue: bool) -> bool:
	t._sim.advance_days(1)
	if harvest:
		_harvest_ready_sites()
	var status: Dictionary = t._sim.get_liquidity_status()
	if rescue and str((status.get("offer", {}) as Dictionary).get("status", "")) == "open":
		t._sim.accept_rescue_loan()
	return str(status.get("stage", "")) == "bank_call"


func _name_of(id: String) -> String:
	for entry in t._sim.get_available_scenarios():
		if str(entry.get("id", "")) == id:
			return str(entry.get("name", id))
	return id


## The critical card at the bank's call: other cards ahead of it are
## dismissed with their Fortsett.
func _card_at_bank_call(card: Control) -> bool:
	var guard := 0
	while card.is_open() and str(card.current_event().get("type", "")) != "bank_call" and guard < 20:
		card.continue_button.pressed.emit()
		guard += 1
	return card.is_open() and str(card.current_event().get("type", "")) == "bank_call"


## Play a QA game without a scene to its verdict: {verdict, end_day, call_day}
## (call_day: the first bank call, -1 when none).
func _dry_run(scenario: String, seed: int, harvest: bool, rescue: bool) -> Dictionary:
	_start(scenario, seed)
	var call_day := -1
	for i in MAX_DAYS:
		if t._sim.get_current_verdict() != "running":
			break
		if _day(harvest, rescue) and call_day < 0:
			call_day = int(t._sim.current_day)
	return {"verdict": t._sim.get_current_verdict(), "end_day": int(t._sim.current_day), "call_day": call_day}


## Replay without a scene up to `day`.
func _replay_to(day: int, harvest: bool, rescue: bool) -> void:
	while int(t._sim.current_day) < day and t._sim.get_current_verdict() == "running":
		_day(harvest, rescue)


## With the scene up: day by day until the end screen opens or `stop` holds.
func _play(screen: Node, harvest: bool, stop: Callable = Callable()) -> int:
	for i in MAX_DAYS:
		if screen.is_open() or (stop.is_valid() and stop.call()):
			break
		_day(harvest and not screen.is_open(), false)
	return int(t._sim.current_day)


# ── 1. Won ─────────────────────────────────────────────────────────────────────

func _a_won_game_ends_on_its_medal() -> void:
	var dry := _dry_run(WIN_SCENARIO, WIN_SEED, true, false)
	_check(str(dry["verdict"]) == "won", "end screen: %s (seed %d) is won by harvesting at 4.5 kg (day %d)" % [
		WIN_SCENARIO, WIN_SEED, int(dry["end_day"])])
	_start(WIN_SCENARIO, WIN_SEED)
	_replay_to(int(dry["end_day"]) - LEAD_DAYS, true, false)
	var scene := await _spawn()
	var screen := _screen(scene)
	var day := _play(screen, true)
	await _settle()
	var report: Dictionary = t._sim.get_scenario_end_report()
	_check(screen.is_open() and screen.verdict() == "won" and screen.shown_count == 1 and day == int(dry["end_day"]),
		"end screen: the win opens the end screen once, on the day it lands (day %d)" % day)
	_check(str(report.get("verdict", "")) == "won" and t._sim.is_clock_held("end_screen"),
		"end screen: the won game's clock waits behind it")
	var layer := scene.get_node("UILayer") as CanvasLayer
	_check(not layer.visible and screen.layer == 50, "end screen: on CanvasLayer 50 with the game UI stepped aside")
	var title: Label = screen.get_node("%VerdictTitle")
	_check(title.text == t.tr("UI_ENDSCREEN_VERDICT_WON"), "end screen: the verdict reads '%s'" % title.text)
	var medal := str(report.get("medal", "none"))
	var met := (report.get("distinctions", []) as Array).filter(func(d: Dictionary) -> bool: return bool(d.get("met", false))).size()
	var badge: MedalBadge = screen.get_node("%MedalBadge")
	_check(medal != "none" and badge.medal == medal and badge.stars == met
			and (screen.get_node("%MedalTitle") as Label).text == t.tr("UI_ENDSCREEN_MEDAL_" + medal.to_upper()),
		"end screen: the medal is the report's (%s, %d of 3 distinctions)" % [medal, met])
	_check((screen.get_node("%Distinctions") as Node).get_child_count() == 3, "end screen: the three distinctions are listed")
	_check_report_sheet(screen, report)

	var outcome: Dictionary = screen.campaign_outcome()
	var opened: PackedStringArray = outcome.get("newly_unlocked", PackedStringArray())
	_check(bool(outcome.get("recorded", false)) and opened.has("laksebaronene") and opened.has("po3_red_sone")
			and CampaignProgress.wins(WIN_SCENARIO) == 1 and CampaignProgress.best_medal(WIN_SCENARIO) == medal,
		"end screen: the campaign records the win and opens Laksebaronene and PO3 %s" % str(opened))
	_check((screen.get_node("%CampaignNote") as Control).visible, "end screen: it says which scenarios opened")
	_check(screen.buttons()[0] == screen.next_button and not screen.next_button.disabled
			and screen.next_button.text.contains(_name_of("laksebaronene")),
		"end screen: the next scenario is the primary action ('%s')" % screen.next_button.text)
	_check(t.get_viewport().gui_get_focus_owner() == screen.next_button, "end screen: the primary action has the focus")
	await t._lint_screen("end screen (won)")

	await t._click_control(screen.continue_button)
	await _frames(2)
	_check(not screen.is_open() and layer.visible and not t._sim.is_clock_held("end_screen"),
		"end screen: 'Fortsett å spille' returns to the same game, the clock released")
	_check(t._sim.get_current_verdict() == "won" and _scene_changes.is_empty(),
		"end screen: continuing changes no scene and keeps the won game")
	scene.queue_free()
	await _frames(2)


## The sheet shows the engine's report: the figures, the months, the three
## turning points (amounts as booked) and the mortality causes.
func _check_report_sheet(screen: Node, report: Dictionary) -> void:
	var built: Dictionary = screen.built()
	var kpis: Dictionary = built.get("kpis", {})
	var result: Label = kpis.get("result")
	_check(result != null and result.text == SimFormatters.format_nok(float(report.get("net_result", 0.0))),
		"end screen: the result figure is the report's net result (%s)" % (result.text if result else "?"))
	var chart: PnlStoryChart = built.get("chart")
	_check(chart != null and chart.months().size() == (report.get("months", []) as Array).size() and chart.months().size() >= 6,
		"end screen: the P&L chart draws every month of the story (%d)" % (chart.months().size() if chart else 0))
	var tps: Array = report.get("turning_points", [])
	var cards: Array = built.get("points", [])
	var wrong := []
	for i in cards.size():
		var amount: Label = (cards[i] as Node).find_child("Amount", true, false)
		var want := float(tps[i].get("amount", 0.0))
		if amount == null or amount.text != ("+" if want > 0.0 else "") + SimFormatters.format_nok(want):
			wrong.append(i + 1)
	_check(tps.size() == 3 and cards.size() == 3 and wrong.is_empty() and chart != null and chart.marker_centres().size() == 3,
		"end screen: three turning points, each with the ledger's amount, numbered on the chart %s" % str(wrong))
	if chart != null:
		var plot := chart.plot_rect().grow(1.0)
		_check(chart.marker_centres().all(func(c: Vector2) -> bool: return plot.has_point(c)),
			"end screen: every turning point's marker sits on the cash line inside the plot")
	var causes: Array = (t._sim.get_mortality_breakdown().get("categories", []) as Array).filter(
		func(c: Dictionary) -> bool: return int(c.get("fish", 0)) > 0)
	_check((built.get("mortality_rows", []) as Array).size() == mini(causes.size(), EndScreenReport.MORTALITY_ROWS),
		"end screen: one row per cause of death from the mortality ledger (%d)" % causes.size())
	_check(not (built.get("log_rows", []) as Array).is_empty(), "end screen: the event log lists the game's events")


# ── 2. Persistence ─────────────────────────────────────────────────────────────

func _unlocks_and_medals_persist() -> void:
	# As after a restart: nothing in memory, everything read back from disk.
	CampaignProgress._graph = {}
	var raw: Variant = JSON.parse_string(FileAccess.get_file_as_string(CampaignProgress.path()))
	var entry: Dictionary = (raw.get("scenarios", {}) as Dictionary).get(WIN_SCENARIO, {}) if raw is Dictionary else {}
	var medal := str(entry.get("best_medal", "none"))
	_check(int(entry.get("wins", 0)) == 1 and medal != "none", "campaign: the file keeps the win and its medal (%s)" % medal)
	var menu: Control = await t._spawn_main_menu()
	await t._click_control(menu.btn_new_game)
	await t.get_tree().create_timer(0.4).timeout
	var picker: Control = menu.scenario_panel
	var open_now := []
	var locked_now := []
	for e: Dictionary in picker.get_scenarios():
		var id := str(e.get("id", ""))
		var card: Button = picker.get_card(id)
		if card != null and card.find_child("LockTag", true, false) != null:
			locked_now.append(id)
		elif CampaignProgress.is_campaign_scenario(id):
			open_now.append(id)
	_check(open_now.has("laksebaronene") and open_now.has("po3_red_sone") and open_now.has(WIN_SCENARIO),
		"campaign: after a restart the won scenario and its unlocks are open %s" % str(open_now))
	locked_now.sort()
	# Lofoten opens with a win of Første sesong or Bærekraftsmesteren, not this one.
	_check(locked_now == ["algekatastrofe", "isa_utbrudd", "krise_manager", "lofoten_eventyr"],
		"campaign: the rest stay locked %s" % str(locked_now))
	var won_card: Button = picker.get_card(WIN_SCENARIO)
	var tag: Label = won_card.find_child("MedalTag", true, false) if won_card != null else null
	_check(tag != null and tag.text == t.tr("UI_SCENARIO_MEDAL_" + medal.to_upper()), "campaign: the won card shows its medal")
	picker.select("krise_manager")
	var line: Label = picker.find_child("Campaign", true, false)
	_check(picker.get_start_button().disabled and line != null and line.visible and line.text.contains(_name_of("laksebaronene"))
			and line.text.contains(_name_of("po3_red_sone")),
		"campaign: a locked scenario cannot start and says which wins open it ('%s')" % (line.text if line else "?"))
	picker.select("laksebaronene")
	_check(not picker.get_start_button().disabled, "campaign: an opened scenario can start")
	await t._lint_screen("scenario picker with a campaign")
	menu.queue_free()
	await _frames(2)


# ── 3. Bankrupt ────────────────────────────────────────────────────────────────

func _a_bankrupt_game_is_warned_then_gated() -> void:
	var dry := _dry_run(WIN_SCENARIO, WIN_SEED, false, false)
	_check(str(dry["verdict"]) == "bankrupt" and int(dry["call_day"]) > 0,
		"bankruptcy: passive %s (seed %d) goes bankrupt on day %d after a bank call on day %d" % [
			WIN_SCENARIO, WIN_SEED, int(dry["end_day"]), int(dry["call_day"])])
	_events.clear()
	_start(WIN_SCENARIO, WIN_SEED)
	var call_day := int(dry["call_day"])
	_replay_to(call_day - LEAD_DAYS, false, false)
	var scene := await _spawn()
	var screen := _screen(scene)
	var card: Control = scene.get_node("%CriticalEventCard")
	var call_card := [false]
	var watch := func() -> bool:
		if _card_at_bank_call(card):
			call_card[0] = true
			card.continue_button.pressed.emit()
		return false
	var day := _play(screen, false, watch)
	await _settle()
	var report: Dictionary = t._sim.get_scenario_end_report()
	var bankrupt_day := int(report.get("end_day", -1))
	_check(screen.is_open() and screen.verdict() == "bankrupt", "bankruptcy: the end screen opens on the bankruptcy (day %d)" % day)
	var warn := _events.filter(func(e: Dictionary) -> bool: return str(e.get("type", "")) == "liquidity_warning")
	var calls := _events.filter(func(e: Dictionary) -> bool: return str(e.get("type", "")) == "bank_call")
	var warn_day := int(warn[-1].get("day", 9999)) if not warn.is_empty() else 9999
	var called := int(calls[-1].get("day", 9999)) if not calls.is_empty() else 9999
	_check(not warn.is_empty() and str(warn[-1].get("severity", "")) == "warning" and bankrupt_day - warn_day >= 30,
		"bankruptcy: the bank's liquidity warning reached the player %d days before (>= 30)" % (bankrupt_day - warn_day))
	_check(not calls.is_empty() and str(calls[-1].get("severity", "")) == "critical" and bankrupt_day - called >= 10 and call_card[0],
		"bankruptcy: the bank call reached the player as a critical card %d days before (>= 10)" % (bankrupt_day - called))
	var title: Label = screen.get_node("%VerdictTitle")
	_check(title.text == t.tr("UI_ENDSCREEN_VERDICT_BANKRUPT") and screen.continue_button.disabled
			and screen.continue_button.tooltip_text != "" and screen.buttons()[0] == screen.retry_button,
		"bankruptcy: 'Konkurs', no way on in the same game, 'Prøv igjen' first")
	_check((screen.get_node("%VerdictSub") as Label).text.contains(SimFormatters.format_nok(float(report.get("bankruptcy_floor", 0.0)))),
		"bankruptcy: the reason names the credit limit it fell through")
	await t._lint_screen("end screen (bankrupt)")
	await _only_the_end_screen_takes_input(scene, screen)

	scene.queue_free()
	await _frames(2)

	# The same bankruptcy again (the seed replays it): recorded once, and
	# "Prøv igjen" starts the scenario afresh.
	var results_before := _results(WIN_SCENARIO)
	_start(WIN_SCENARIO, WIN_SEED)
	_replay_to(int(dry["end_day"]) - LEAD_DAYS, false, false)
	scene = await _spawn()
	screen = _screen(scene)
	_play(screen, false)
	await _settle()
	_check(screen.is_open() and screen.verdict() == "bankrupt" and _results(WIN_SCENARIO) == results_before,
		"bankruptcy: the same end shown again is recorded once")
	_scene_changes.clear()
	await t._click_control(screen.retry_button)
	_check(_scene_changes == [GAME_SCENE] and t._sim.get_active_scenario_id() == WIN_SCENARIO
			and t._sim.get_current_verdict() == "running" and int(t._sim.current_day) == 0,
		"bankruptcy: 'Prøv igjen' starts %s from day 0 (%s)" % [WIN_SCENARIO, str(_scene_changes)])
	t._sim.set_paused(true)
	scene.queue_free()
	await _frames(2)


## The camera at rest (a fly from the scene's start may still be landing).
func _settled_camera(cam: Node3D) -> Vector3:
	var last := Vector3.INF
	for i in 600:
		await t.get_tree().process_frame
		if cam.global_position.distance_to(last) < 0.0005:
			break
		last = cam.global_position
	return cam.global_position


## A real double click (the camera flies to a double-clicked spot of the fjord).
func _double_click_at(pos: Vector2) -> void:
	await t._click_at(pos)
	for pressed in [true, false]:
		var ev := InputEventMouseButton.new()
		ev.position = pos
		ev.global_position = pos
		ev.button_index = MOUSE_BUTTON_LEFT
		ev.pressed = pressed
		ev.double_click = pressed
		t.get_viewport().push_input(ev)
	await t.get_tree().process_frame


func _results(id: String) -> int:
	var p := CampaignProgress.load_progress()
	return int(((p.get("scenarios", {}) as Dictionary).get(id, {}) as Dictionary).get("results", 0))


## Acceptance 2: after GameEnded only the end screen accepts input. The
## game's keys (pause, speeds, next event, inbox, drawer, Esc, a tab
## shortcut, quick save), the camera's keys and clicks where the HUD and the
## fjord were change nothing; Tab and Enter work the screen's own buttons.
func _only_the_end_screen_takes_input(scene: Node, screen: Node) -> void:
	var clock: Dictionary = t._sim.get_clock_state()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var tab: String = ui.get_active_tab()
	var pause_menu: Node = scene.get_node("%PauseMenu")
	var inbox: CanvasLayer = scene.get_node("AttentionInbox")
	var drawer: Node = scene.get_node("UILayer/NotificationCenter")
	var cam: Node3D = scene.find_child("FjordCamera", true, false)
	cam.set("auto_orbit_enabled", false)
	var cam_at := await _settled_camera(cam)
	var saves := (t._sim.get_save_list() as Array).size()
	t.get_viewport().gui_release_focus()
	for key in [KEY_SPACE, KEY_1, KEY_3, KEY_4, KEY_N, KEY_V, KEY_I, KEY_ESCAPE, KEY_T, KEY_HOME]:
		await t._press_key(key, 0)
	await t._press_command_key(KEY_2)
	await t._press_command_key(KEY_S)
	var w := InputEventKey.new()
	w.physical_keycode = KEY_W
	w.keycode = KEY_W
	w.pressed = true
	Input.parse_input_event(w)
	await t.get_tree().create_timer(0.3).timeout
	w.pressed = false
	Input.parse_input_event(w)
	var centre := t.get_viewport().get_visible_rect().get_center()
	await t._click_at(Vector2(centre.x, 24.0))
	await _double_click_at(centre + Vector2(200, 0))
	await t.get_tree().create_timer(0.3).timeout
	var after: Dictionary = t._sim.get_clock_state()
	_check(after.get("player_paused") == clock.get("player_paused") and after.get("speed") == clock.get("speed")
			and after.get("running") == clock.get("running") and after.get("next_event") == clock.get("next_event"),
		"input gate: Space, 1-4 and N leave the clock as it was")
	_check(ui.get_active_tab() == tab and not pause_menu.is_open() and not inbox.visible and not drawer.is_open(),
		"input gate: Cmd+2, Esc, I and V open nothing behind the end screen")
	_check((t._sim.get_save_list() as Array).size() == saves, "input gate: Cmd+S saves nothing")
	_check(cam.global_position.distance_to(cam_at) < CAMERA_STILL_M, "input gate: W, T, Home and clicks leave the camera still")
	_check(screen.is_open(), "input gate: the end screen stays open through all of it")
	# Its own buttons answer the keyboard: Tab walks them, Enter presses.
	screen.retry_button.grab_focus()
	await t._press_key(KEY_TAB, 0)
	var focused: Control = t.get_viewport().gui_get_focus_owner()
	_check(focused is Button and screen.buttons().has(focused) and focused != screen.retry_button and not (focused as Button).disabled,
		"input gate: Tab moves to the next enabled end-screen button ('%s')" % (focused.name if focused else "none"))
	screen.menu_button.grab_focus()
	_scene_changes.clear()
	await t._press_key(KEY_ENTER, 0)
	_check(_scene_changes == [MAIN_MENU_SCENE] and t._sim.is_paused(), "input gate: Enter on 'Hovedmeny' leaves for the main menu")


# ── 4. Lost, with the rescue loan ──────────────────────────────────────────────

func _a_lost_game_takes_the_rescue_loan_and_loads_the_autosave() -> void:
	var dry := _dry_run(LOSS_SCENARIO, LOSS_SEED, false, true)
	_check(str(dry["verdict"]) == "lost" and int(dry["call_day"]) > 0,
		"loss: passive %s (seed %d) with the rescue loan is lost on day %d (bank call day %d)" % [
			LOSS_SCENARIO, LOSS_SEED, int(dry["end_day"]), int(dry["call_day"])])
	_start(LOSS_SCENARIO, LOSS_SEED)
	_replay_to(int(dry["call_day"]) - LEAD_DAYS, false, false)
	var scene := await _spawn()
	var screen := _screen(scene)
	var card: Control = scene.get_node("%CriticalEventCard")
	var at_call := func() -> bool: return _card_at_bank_call(card)
	_play(screen, false, at_call)
	var status: Dictionary = t._sim.get_liquidity_status()
	var offer: Dictionary = status.get("offer", {})
	_check(at_call.call() and str(status.get("stage", "")) == "bank_call" and str(offer.get("status", "")) == "open",
		"loss: the bank call arrives as a critical card with a rescue offer (day %d, %s)" % [
			int(t._sim.current_day), SimFormatters.format_nok(float(offer.get("amount", 0.0)))])
	await _settle()  # the card has slid in
	await t._click_control(card.verb_button)
	await _frames(3)
	var ui: Control = scene.get_node("UILayer/GameUI")
	var bank: BankCard = ui.find_child("BankCard", true, false)
	_check(ui.get_active_tab() == "finance" and bank != null and bank.is_visible_in_tree(),
		"loss: the card's 'Skaff kapital' opens the Finance tab on the bank's card")
	await t._drain_cards(card)
	if bank == null:
		scene.queue_free()
		return
	var text: Label = bank.find_child("OfferText", true, false)
	_check(text != null and text.text.begins_with(SimFormatters.format_nok(float(offer.get("amount", 0.0)))),
		"loss: the bank card shows the offer's terms ('%s')" % (text.text if text else "?"))
	await t._lint_screen("bank card")
	var cash := float(t._sim.balance)
	await t._click_control(bank.accept_button)
	status = t._sim.get_liquidity_status()
	_check(str((status.get("offer", {}) as Dictionary).get("status", "")) == "accepted" and int(status.get("rescue_loans_taken", 0)) == 1
			and float(t._sim.balance) > cash, "loss: a click takes the rescue loan (cash %s -> %s)" % [
				SimFormatters.format_nok(cash), SimFormatters.format_nok(float(t._sim.balance))])
	scene.queue_free()
	await _frames(2)
	_replay_to(int(dry["call_day"]) + 5, false, false)
	_check(bool(t.get_node("/root/SaveLoadManager").AutoSave()), "loss: an autosave of the rescued season")
	var autosave_day := int(t._sim.current_day)
	_replay_to(int(dry["end_day"]) - LEAD_DAYS, false, false)
	scene = await _spawn()
	screen = _screen(scene)
	var day := _play(screen, false)
	await _settle()
	var report: Dictionary = t._sim.get_scenario_end_report()
	_check(screen.is_open() and screen.verdict() == "lost" and int(report.get("rescue_loans", 0)) == 1 and day == int(dry["end_day"]),
		"loss: the season is lost at its end, the rescue loan on the books (day %d)" % day)
	var sub: Label = screen.get_node("%VerdictSub")
	_check(str(report.get("loss_reason", "")) != "" and sub.text.begins_with(str(report.get("loss_reason", ""))),
		"loss: the reason is the scenario's own ('%s')" % sub.text)
	var finance := (report.get("distinctions", []) as Array).filter(func(d: Dictionary) -> bool: return str(d.get("id", "")) == "finance")
	_check(not finance.is_empty() and not bool(finance[0].get("met", true)), "loss: the rescue loan costs the finance distinction")
	await t._lint_screen("end screen (lost)")
	_check(not screen.autosave_button.disabled, "loss: the autosave from before the end can be loaded")
	_scene_changes.clear()
	await t._click_control(screen.autosave_button)
	_check(_scene_changes == [GAME_SCENE] and int(t._sim.current_day) == autosave_day and t._sim.get_current_verdict() == "running",
		"loss: 'Last inn autolagring' goes back to day %d of the running season" % autosave_day)
	scene.queue_free()
	await _frames(2)

	# The same season lost again: the next scenario, by keyboard.
	_replay_to(int(dry["end_day"]) - LEAD_DAYS, false, false)
	scene = await _spawn()
	screen = _screen(scene)
	_play(screen, false)
	await _settle()
	var next_id := CampaignProgress.next_scenario(LOSS_SCENARIO)
	_check(screen.is_open() and next_id != "" and not screen.next_button.disabled, "loss: the next open scenario is offered ('%s')" % next_id)
	screen.next_button.grab_focus()
	_scene_changes.clear()
	await t._press_key(KEY_ENTER, 0)
	_check(_scene_changes == [GAME_SCENE] and t._sim.get_active_scenario_id() == next_id and int(t._sim.current_day) == 0,
		"loss: Enter on 'Neste' starts %s" % next_id)
	t._sim.set_paused(true)
	scene.queue_free()
	await _frames(2)
