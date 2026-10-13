extends Node

## Headless player-flow tests for the Godot UI layer (save/load + menu flows).
##
## Run (also CI step "Godot UI flow tests"):
##   godot --headless --path godot res://tests/UiFlowTests.tscn
##
## Drives the real autoloads (GameBridge C#, SaveLoadManager C#, SimBridge) and
## the real scenes, presses buttons through their `pressed` signals and asserts
## the visible outcome. Saves go to user://ci_ui_tests/saves/ (never the
## player's slots). Prints `UI_FLOW_RESULT checks=N failures=M` and exits
## non-zero on any failure, so the CI wrapper can gate on it.

const TEST_SAVE_DIR := "user://ci_ui_tests/saves/"
const SCENARIO_ID := "forste_sesong"
const ESCAPE_SAVE := "user://ci_ui_tests/escape.json"

var _checks: int = 0
var _failures: PackedStringArray = []
var _sim: Node = null

func _ready() -> void:
	_run.call_deferred()

func _run() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	var mgr := get_node_or_null("/root/SaveLoadManager")
	_check(_sim != null, "SimBridge autoload present")
	_check(mgr != null, "SaveLoadManager autoload present")
	if _sim == null or mgr == null:
		_finish()
		return
	mgr.UseSaveDirectory(TEST_SAVE_DIR)
	_clear_test_saves()

	await _test_bridge_save_load_roundtrip()
	await _test_save_load_menu_tab()
	await _test_main_menu_load_and_settings()
	await _test_main_menu_cancel_scenario_reenables_buttons()
	await _test_main_menu_load_enters_game()
	await _test_game_scene_keeps_menu_started_scenario()
	await _test_dashboard_night_shift_policy()
	await _test_tech_tree_lays_out_every_node()
	await _test_goal_hud_shows_engine_progress()
	await _test_ras_panel_stocks_and_harvests()
	await _test_finance_panel_takes_and_repays_loans()
	await _test_analytics_panel_charts_every_metric()
	await _test_data_export_writes_csv()
	await _test_fjord_pen_click_opens_details()
	await _test_mod_toggle_takes_clicks()
	await _test_notification_center_escape()
	await _test_escape_returns_tabs_to_the_dashboard()
	await _test_space_and_enter_reach_the_gui()
	await _test_alerts_announce_transitions_once()
	await _test_pen_details_show_site_lice()
	await _test_carbon_tab_shows_the_ledger()
	_test_scenario_start_replaces_the_running_game()
	await _test_offshore_rows_are_legible()
	await _test_offshore_sludge_button_sells_sludge()
	_test_load_resets_the_forecast_cache()
	await _test_escape_spares_toasts_and_closes_the_save_list()
	await _test_typing_hotkeys_and_close_buttons()
	await _test_tutorial_card_leaves_escape_to_tabs()
	await _test_event_alerts_and_vaccine_window()
	await _test_fjord_shows_rival_farms_and_wellboats()
	_test_facade_forwards_phase5_systems()

	_clear_test_saves()
	_finish()

# ── Tests ──────────────────────────────────────────────────────────────────────

## F928: the bridge save/list/load/delete verbs reach the C# manager.
func _test_bridge_save_load_roundtrip() -> void:
	_start_paused_game()
	_sim.advance_days(5)
	var saved_day: int = _sim.current_day

	_check(_sim.save_game("ci_roundtrip"), "save_game returns true for a running game")
	var entry := _find_save("ci_roundtrip")
	_check(not entry.is_empty(), "get_save_list lists the new save")
	_check(int(entry.get("day", -1)) == saved_day, "listed save carries the saved day (%d)" % saved_day)

	_sim.advance_days(3)
	_check(_sim.current_day == saved_day + 3, "sim advanced past the save")
	_check(_sim.load_game(String(entry.get("file", ""))), "load_game returns true")
	_check(_sim.current_day == saved_day, "load restores the saved day")

	_check(not _sim.load_game("does_not_exist.json"), "loading a missing file returns false")
	_check(_sim.get_last_save_error() != "", "a failed load reports a reason")
	# A valid save planted one level ABOVE the save dir must stay unreachable.
	var planted := DirAccess.copy_absolute(
		ProjectSettings.globalize_path(TEST_SAVE_DIR + String(entry.get("file", ""))),
		ProjectSettings.globalize_path(ESCAPE_SAVE))
	_check(planted == OK, "setup: planted a valid save outside the save dir")
	_check(not _sim.load_game("../" + ESCAPE_SAVE.get_file()), "path traversal cannot load a save outside the save dir")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(ESCAPE_SAVE))

	_check(_sim.delete_save(String(entry.get("file", ""))), "delete_save removes the save")
	_check(_find_save("ci_roundtrip").is_empty(), "deleted save no longer listed")
	await get_tree().process_frame

## F928: the GameUI tab (on_panel_shown) lists slots, saves and loads.
func _test_save_load_menu_tab() -> void:
	_start_paused_game()
	var menu: Control = (load("res://scenes/ui/SaveLoadMenu.tscn") as PackedScene).instantiate()
	add_child(menu)
	await get_tree().process_frame
	menu.show()
	menu.on_panel_shown()
	await get_tree().process_frame

	var new_save: Button = menu.get_node("%NewSaveButton")
	var slots: VBoxContainer = menu.get_node("%SlotsContainer")
	_check(new_save.visible, "tab: 'new save' offered while a game runs")
	_check(_count_slots(slots) == 0, "tab: no slots before the first save")

	new_save.pressed.emit()
	await get_tree().process_frame
	_check(_count_slots(slots) == 1, "tab: pressing 'new save' adds a slot")

	var saved_day: int = _sim.current_day
	_sim.advance_days(2)
	var load_btn := _first_slot_button(slots, "LoadButton")
	_check(load_btn != null, "tab: slot has a load button")
	if load_btn:
		load_btn.pressed.emit()
		await get_tree().process_frame
		_check(_sim.current_day == saved_day, "tab: slot load button restores the save")
	_check(menu.visible, "tab: panel stays open after loading")

	var delete_btn := _first_slot_button(slots, "DeleteButton")
	if delete_btn:
		delete_btn.pressed.emit()  # arm
		delete_btn.pressed.emit()  # confirm
		await get_tree().process_frame
	_check(_count_slots(slots) == 0, "tab: two-step delete removes the slot")
	menu.queue_free()
	await get_tree().process_frame

## F616: main-menu Load and Settings open the embedded menus and come back.
func _test_main_menu_load_and_settings() -> void:
	var menu := await _spawn_main_menu()
	menu.btn_load.pressed.emit()
	await get_tree().process_frame
	_check(menu.save_load_menu.visible, "main menu: Load shows the save list")
	_check(not menu.center.visible, "main menu: button column hidden behind the save list")
	menu.save_load_menu.get_node("%CloseButton").pressed.emit()
	await get_tree().process_frame
	_check(not menu.save_load_menu.visible and menu.center.visible, "main menu: closing the save list restores the menu")

	menu.btn_settings.pressed.emit()
	await get_tree().process_frame
	_check(menu.settings_menu.visible, "main menu: Settings shows the settings menu")
	menu.settings_menu._on_close()  # the menu's own close path (button/ESC)
	await get_tree().process_frame
	_check(menu.center.visible, "main menu: closing settings restores the menu")
	menu.queue_free()
	await get_tree().process_frame

## F617: cancelling the scenario picker hands back an enabled menu.
func _test_main_menu_cancel_scenario_reenables_buttons() -> void:
	var menu := await _spawn_main_menu()
	menu.btn_new_game.pressed.emit()
	await get_tree().process_frame
	_check(menu.scenario_panel != null, "main menu: New Game opens the scenario picker")
	menu._on_scenario_close()
	await get_tree().process_frame
	var all_enabled := true
	for btn: Button in [menu.btn_new_game, menu.btn_load, menu.btn_settings, menu.btn_quit]:
		all_enabled = all_enabled and not btn.disabled
	_check(all_enabled, "main menu: all buttons enabled after cancelling the picker")
	_check(menu.center.visible, "main menu: button column visible after cancelling the picker")
	menu.queue_free()
	await get_tree().process_frame

## F616 + F928: loading a slot from the main menu enters the game.
func _test_main_menu_load_enters_game() -> void:
	_start_paused_game()
	_sim.advance_days(4)
	var saved_day: int = _sim.current_day
	_check(_sim.save_game("ci_menu_load"), "setup: save for the menu-load flow")
	_start_paused_game()  # fresh game; the menu must bring the save back

	var menu := await _spawn_main_menu()
	var entered := [false]
	menu.enter_game_override = func() -> void: entered[0] = true
	menu.btn_load.pressed.emit()
	await get_tree().process_frame
	var slots: VBoxContainer = menu.save_load_menu.get_node("%SlotsContainer")
	_check(menu.save_load_menu.get_node("%NewSaveButton").visible == false, "main menu: load mode offers no 'new save'")
	var load_btn := _first_slot_button(slots, "LoadButton")
	_check(load_btn != null, "main menu: save list shows the slot")
	if load_btn:
		load_btn.pressed.emit()
		await get_tree().process_frame
	_check(entered[0], "main menu: loading a slot enters the game scene")
	_check(_sim.current_day == saved_day, "main menu: the loaded save is the running game")
	menu.queue_free()
	await get_tree().process_frame
	_sim.delete_save("ci_menu_load.json")

## F056: GameScene keeps the scenario the main menu started.
func _test_game_scene_keeps_menu_started_scenario() -> void:
	_check(_sim.start_new_game("Player", 10_000_000, SCENARIO_ID), "setup: scenario start succeeds")
	_sim.set_paused(true)
	_check(_sim.get_active_scenario_id() == SCENARIO_ID, "setup: scenario %s active" % SCENARIO_ID)
	_sim.advance_days(3)
	var day: int = _sim.current_day

	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 3:
		await get_tree().process_frame  # Main._start_game runs deferred
	_check(_sim.get_active_scenario_id() == SCENARIO_ID, "GameScene keeps the menu-started scenario")
	_check(_sim.current_day == day, "GameScene does not restart the clock (day %d)" % day)
	scene.queue_free()
	await get_tree().process_frame

## F945 + dashboard: the night-shift dropdown is populated and drives the engine.
func _test_dashboard_night_shift_policy() -> void:
	_start_paused_game()
	var dash: Control = (load("res://scenes/ui/Dashboard.tscn") as PackedScene).instantiate()
	add_child(dash)
	await get_tree().process_frame
	var option: OptionButton = dash.get_node("%PolicyOption")
	_check(option.item_count == 3, "dashboard: night-shift dropdown lists 3 policies")
	option.select(2)
	option.item_selected.emit(2)
	await get_tree().process_frame
	_check(String(_sim.get_night_shift_status().get("policy", "")) == "AutomatedROC",
		"dashboard: choosing a policy sets it in the engine")
	dash.queue_free()
	await get_tree().process_frame

## F1004/F1001: every research node gets its own visible panel (the panel
## dropped nodes on unknown branches and stacked same-tier nodes on top of each
## other), and effects are labelled by kind (points vs percent).
func _test_tech_tree_lays_out_every_node() -> void:
	_start_paused_game()
	var tree: Control = (load("res://scripts/ui/TechTreePanel.gd") as GDScript).new()
	add_child(tree)
	await get_tree().process_frame
	tree.on_panel_shown()
	var nodes: Array = Array(_sim.get_research_nodes())
	_check(nodes.size() == 25, "tech tree: bridge lists the 25 shipped research nodes (%d)" % nodes.size())
	_check(tree._node_panels.size() == nodes.size(),
		"tech tree: every node has a panel (%d of %d)" % [tree._node_panels.size(), nodes.size()])
	var rects: Array = []
	for panel in tree._node_panels.values():
		rects.append(Rect2(panel.position, Vector2(tree.NODE_W, tree.NODE_H)))
	var overlaps := 0
	for i in rects.size():
		for j in range(i + 1, rects.size()):
			if rects[i].intersects(rects[j]):
				overlaps += 1
	_check(overlaps == 0, "tech tree: no two node panels overlap (%d overlaps)" % overlaps)
	_check(tree.format_effect("sustainability_score_bonus", 8.0) == "Bærekraftscore +8",
		"tech tree: score effects read as points, not +800%")
	_check(tree.format_effect("mortality_reduction", 0.05) == "Dødelighet −5 %",
		"tech tree: reductions read as minus percent")
	tree.queue_free()
	await get_tree().process_frame

## F949: the goal HUD shows the engine's progress (it read a GDScript tracker
## that was never initialised and sat at 0%), with time left since the start.
func _test_goal_hud_shows_engine_progress() -> void:
	_check(_sim.start_new_game("Player", 12_000_000, "po3_red_sone"), "goal: survival scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(73)
	var goal: Dictionary = _sim.get_scenario_goal()
	_check(str(goal.get("scenario_id", "")) == "po3_red_sone", "goal: engine reports the active scenario")
	_check(absf(float(goal.get("progress", -1.0)) - 73.0 / 1095.0) < 0.01,
		"goal: survival progress is days survived / limit (%s)" % str(goal.get("progress")))
	_check(int(goal.get("days_left", -1)) == 1022, "goal: 1022 days left (%s)" % str(goal.get("days_left")))
	var hud: Control = (load("res://ui/ScenarioGoalHUD.tscn") as PackedScene).instantiate()
	add_child(hud)
	await get_tree().process_frame
	var bar: ProgressBar = hud.get_node_or_null("PanelContainer/VBoxContainer/ProgressBar")
	var time_label: Label = hud.get_node_or_null("PanelContainer/VBoxContainer/TimeLabel")
	_check(bar != null and int(bar.value) == 7, "goal HUD: progress bar at 7%% (%s)" % str(bar.value if bar else "missing"))
	_check(time_label != null and "2.8 år igjen" in time_label.text,
		"goal HUD: time left counts from the scenario start (%s)" % (time_label.text if time_label else "missing"))
	hud.queue_free()
	await get_tree().process_frame

## F383: a RAS facility holds fish — the panel's buttons stock it once it is
## operational and harvest it, through the engine verbs.
func _test_ras_panel_stocks_and_harvests() -> void:
	# Seeded: storyteller events (e.g. an ISA cull) must not decide the outcome.
	_check(_sim.start_qa_new_game("Player", 2_000_000_000, 4242), "ras: seeded game starts")
	_sim.set_paused(true)
	_check(_sim.build_ras_facility("UI RAS", "small"), "ras: a small facility can be built")
	var panel: Control = (load("res://scenes/ui/RASPanel.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	panel.refresh()
	await get_tree().process_frame
	var facility: Dictionary = _sim.get_ras_facilities()[0]
	var row: Node = panel.find_child("Facility_%s" % facility["id"], true, false)
	_check(row != null, "ras: the facility has a row")
	if row == null:
		panel.queue_free()
		return
	_check(row.get_node("StockButton").disabled, "ras: stocking is disabled under construction")
	_sim.advance_days(735)
	panel.refresh()
	await get_tree().process_frame
	row = panel.find_child("Facility_%s" % facility["id"], true, false)
	var stock_btn: Button = row.get_node("StockButton")
	_check(not stock_btn.disabled, "ras: stocking is enabled once operational")
	stock_btn.pressed.emit()
	await get_tree().process_frame
	_check(int(_sim.get_ras_facilities()[0].get("fish_count", 0)) == 100000,
		"ras: 'Sett ut' stocks 100 000 smolts (%s)" % str(_sim.get_ras_facilities()[0].get("fish_count")))
	_sim.advance_days(300)
	panel.refresh()
	await get_tree().process_frame
	row = panel.find_child("Facility_%s" % facility["id"], true, false)
	_check(int(_sim.get_ras_facilities()[0].get("fish_count", 0)) > 0, "ras: the tanks hold fish before the harvest")
	var cash_before: float = float(_sim.balance)
	(row.get_node("HarvestButton") as Button).pressed.emit()
	await get_tree().process_frame
	_check(int(_sim.get_ras_facilities()[0].get("fish_count", -1)) == 0, "ras: 'Høst' empties the tanks")
	_check(float(_sim.balance) > cash_before, "ras: the harvest is sold")
	panel.queue_free()
	await get_tree().process_frame

## F943: the loan card takes a loan (all arguments), shows it, and repays it.
func _test_finance_panel_takes_and_repays_loans() -> void:
	_check(_sim.start_qa_new_game("Player", 20_000_000, 77), "loan: seeded game starts")
	_sim.set_paused(true)
	var panel: Control = (load("res://scenes/ui/FinancePanel.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	var take: Button = panel.get_node("%BtnTakeLoan")
	var repay: Button = panel.get_node("%BtnRepayLoan")
	panel._refresh()
	_check(repay.disabled, "loan: repay is disabled without a loan")
	take.pressed.emit()
	await get_tree().process_frame
	_check(absf(float(_sim.get_total_debt()) - 5_000_000.0) < 1.0,
		"loan: 'Ta lån' borrows 5 MNOK (%s)" % str(_sim.get_total_debt()))
	_check(str(panel.get_node("%LoanStatus").text).begins_with("Aktiv"), "loan: the card shows an active loan")
	_check(str(panel.get_node("%LoanRemaining").text) != "--", "loan: the card shows the remaining principal")
	_check(not repay.disabled, "loan: repay is enabled with a loan")
	repay.pressed.emit()
	await get_tree().process_frame
	_check(absf(float(_sim.get_total_debt()) - 4_000_000.0) < 1.0,
		"loan: 'Betal tilbake' repays 1 MNOK (%s)" % str(_sim.get_total_debt()))
	_check(float(_sim.get_monthly_payment()) > 0.0, "loan: a monthly payment is quoted")
	panel.queue_free()
	await get_tree().process_frame

## F944: every analytics chart gets data (the panel gated on an Engine
## singleton that never exists and three of its five metrics were never recorded).
func _test_analytics_panel_charts_every_metric() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "analytics: seeded scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(20)
	var panel: Control = (load("res://scenes/ui/AnalyticsPanel.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	panel._refresh()
	for key in ["total_biomass_kg", "profit_loss_nok", "dissolved_oxygen", "lice_per_fish", "feed_cost_nok"]:
		var chart: Node = panel._chart_areas.get(key)
		var points: int = chart._series.size() if chart != null else 0
		_check(points >= 20, "analytics: %s has a series (%d points)" % [key, points])
		var container: Node = panel._get_chart_container(key)
		var value_lbl: Label = container.find_child("ValueLabel", true, false) if container else null
		_check(value_lbl != null and value_lbl.text != "–", "analytics: %s shows a latest value" % key)
	panel.queue_free()
	await get_tree().process_frame

## F944 pattern: the export panel looked GameBridge up as an Engine singleton
## and called four methods that did not exist; every export failed.
func _test_data_export_writes_csv() -> void:
	var dir := ProjectSettings.globalize_path("user://ci_ui_tests/exports")
	DirAccess.make_dir_recursive_absolute(dir)
	for file in DirAccess.get_files_at(dir):
		DirAccess.remove_absolute(dir.path_join(file))
	var panel: Control = (load("res://scenes/ui/DataExportPanel.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	panel._path_display.text = dir
	await panel._on_export_single_pressed("daily_snapshots")
	var csv := ""
	for file in DirAccess.get_files_at(dir):
		if file.begins_with("daglig_oversikt") and file.ends_with(".csv"):
			csv = dir.path_join(file)
	_check(not csv.is_empty(), "export: the daily snapshot CSV is written")
	if not csv.is_empty():
		var lines := FileAccess.get_file_as_string(csv).strip_edges().split("\n")
		_check(lines.size() >= 2, "export: the CSV has a header and data rows (%d lines)" % lines.size())
	panel.queue_free()
	await get_tree().process_frame

## F738: on the see-through Fjord tab a real click on a 3D pen reaches it
## (physics picking), selects it, opens its details and enables "Fly to pen";
## every other tab keeps the click. F647: the Pens tab shows its pen list, and
## choosing a pen there selects it in 3D.
func _test_fjord_pen_click_opens_details() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "pen click: seeded scenario starts")
	_sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var pens: FishPenManager = scene.get_node("FjordEnvironment/FishPenManager")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	var ui: Control = scene.get_node("UILayer/GameUI")
	var fly: Button = scene.get_node("UILayer/CameraHint").get_node("%FlyToFarmButton")
	var ids := pens.get_pen_ids()
	_check(ids.size() == 2, "pen click: both sites have a 3D pen (%s)" % str(ids))
	if ids.size() != 2:
		scene.queue_free()
		return
	var target := "po3_sor"
	await _frame_pen(vista.vista_camera, pens.get_pen_position(target))

	await _click_at(_pen_on_screen(vista.vista_camera, pens, target))
	_check(pens.selected_pen_id.is_empty(), "pen click: the dashboard keeps the click")

	await _click_control(ui.find_child("Tab_fjord", true, false))
	_check(ui.get_active_tab() == "fjord", "pen click: the Fjord tab opens")
	await _click_at(_pen_on_screen(vista.vista_camera, pens, target))
	_check(pens.selected_pen_id == target, "pen click: clicking the 3D pen selects it ('%s')" % pens.selected_pen_id)
	_check(ui.get_active_tab() == "pens", "pen click: the pen's details open (%s)" % ui.get_active_tab())
	var panel: Control = ui.find_child("Panel_pens", true, false)
	_check(panel != null and panel.get_selected_pen_id() == target, "pen click: the details show the clicked pen")
	_check(not fly.disabled, "pen click: 'Fly to pen' is enabled for the selection")

	await _click_control(ui.find_child("Tab_fjord", true, false))
	var focused: Array = []
	vista.farm_focused.connect(func(id: String) -> void: focused.append(id))
	await _click_control(fly)
	_check(focused == [target], "pen click: 'Fly to pen' focuses the selected pen (%s)" % str(focused))

	await _click_control(ui.find_child("Tab_pens", true, false))
	panel = ui.find_child("Panel_pens", true, false)
	_check(panel != null and ui.get_active_tab() == "pens", "pen list: the Pens tab is open")
	if panel == null:
		scene.queue_free()
		return
	var list: Control = panel.get_node("%PenList")
	var scroll := list.get_parent() as Control
	_check(scroll.size.y >= 120.0, "pen list: the list has room (%.0f px)" % scroll.size.y)
	var other := "po3_nord"
	var item: Button = panel._pen_list_items.get(other)
	_check(item != null and scroll.get_global_rect().encloses(item.get_global_rect()),
		"pen list: %s is listed inside the visible list" % other)
	if item != null:
		await _click_control(item)
	_check(panel.get_selected_pen_id() == other, "pen list: clicking a pen selects it")
	_check(pens.selected_pen_id == other, "pen list: the 3D view follows the list selection")

	await _click_control(ui.find_child("Tab_dashboard", true, false))
	pens.selected_pen_id = ""
	await _frame_pen(vista.vista_camera, pens.get_pen_position(target))
	await _click_at(_pen_on_screen(vista.vista_camera, pens, target))
	_check(pens.selected_pen_id.is_empty(), "pen click: after the Fjord tab the dashboard keeps clicks again")
	scene.queue_free()
	await get_tree().process_frame

## F620: a mod row's toggle takes its own click (a full-row Button overlay
## swallowed it), and a click on the row's text selects the row.
func _test_mod_toggle_takes_clicks() -> void:
	var panel: Control = (load("res://scenes/ui/ModManagerPanel.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	panel._mods = [
		{"id": "ci_mod_a", "name": "CI Mod A", "version": "1.0", "author": "CI", "enabled": false, "errors": []},
		{"id": "ci_mod_b", "name": "CI Mod B", "version": "1.0", "author": "CI", "enabled": true, "errors": []},
	]
	panel._rebuild_list()
	await get_tree().process_frame
	var toggle: CheckBox = _mod_row(panel, 0).find_children("*", "CheckBox", true, false)[0]
	await _click_control(toggle)
	_check(bool(panel._mods[0].get("enabled")), "mods: clicking the toggle enables the mod")
	await get_tree().process_frame
	var name_label: Label = null
	for lbl: Label in _mod_row(panel, 1).find_children("*", "Label", true, false):
		if lbl.text == "CI Mod B":
			name_label = lbl
	await _click_control(name_label)
	_check(panel._selected_index == 1, "mods: clicking a row selects it (%d)" % panel._selected_index)
	_check(panel._detail_name.text == "CI Mod B", "mods: the detail pane shows the selected mod")
	_check(bool(panel._mods[1].get("enabled")), "mods: selecting a row does not toggle it")
	panel.queue_free()
	await get_tree().process_frame

## F641: ESC with the notification panel closed passes on (GameUI returns to
## the dashboard) and never hides the center; with the panel open ESC closes
## only the panel, and N opens it again.
func _test_notification_center_escape() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	ui.show_tab("finance")
	await _press_key(KEY_ESCAPE)
	_check(ui.get_active_tab() == "dashboard", "notifications: ESC reaches GameUI while the panel is closed")
	_check(center.visible, "notifications: ESC leaves the notification center in place")
	await _press_key(KEY_N)
	_check(center.panel.visible, "notifications: N opens the panel")
	ui.show_tab("finance")
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.5).timeout  # slide-out
	_check(not center.panel.visible, "notifications: ESC closes the open panel")
	_check(ui.get_active_tab() == "finance", "notifications: that ESC closed only the panel")
	await _press_key(KEY_N)
	_check(center.visible and center.panel.visible, "notifications: N opens the panel again after ESC")
	scene.queue_free()
	await get_tree().process_frame

## F593: a tab panel that hides itself on ESC returns GameUI to the dashboard
## (it left a blank tab that its own button could not reopen), and a cached,
## hidden tab no longer swallows ESC for the rest of the session.
func _test_escape_returns_tabs_to_the_dashboard() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("map")
	ui.show_tab("ai_feeding")  # cached and hidden after the next switch
	ui.show_tab("map")
	await get_tree().process_frame
	await _press_key(KEY_ESCAPE)
	await get_tree().process_frame
	var dashboard: Control = ui.find_child("Panel_dashboard", true, false)
	_check(ui.get_active_tab() == "dashboard", "esc: ESC closes a tab to the dashboard (%s)" % ui.get_active_tab())
	_check(dashboard != null and dashboard.visible, "esc: the dashboard is shown")
	await _press_key(KEY_ESCAPE)
	await get_tree().process_frame
	_check(dashboard != null and dashboard.visible, "esc: ESC on the dashboard keeps it shown")
	await _click_control(ui.find_child("Tab_map", true, false))
	var map: Control = ui.find_child("Panel_map", true, false)
	_check(ui.get_active_tab() == "map" and map != null and map.visible, "esc: the closed tab opens again from its button")
	scene.queue_free()
	await get_tree().process_frame

## F618: Space pauses unless a text field has focus (then it types), and Enter
## activates the focused button instead of pausing. F1061: D and W, the fjord
## camera's pan keys, no longer step the game.
func _test_space_and_enter_reach_the_gui() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var hud: Control = scene.get_node("UILayer/HUD")
	var pauses: Array = []
	hud.speed_changed.connect(func(_speed: float, paused: bool) -> void: pauses.append(paused))
	get_viewport().gui_release_focus()
	await _press_key(KEY_SPACE, 32)
	await _press_key(KEY_SPACE, 32)
	_check(pauses == [true, false], "keys: Space pauses and resumes with nothing focused (%s)" % str(pauses))

	ui.show_tab("encyclopedia")
	await get_tree().process_frame
	var search: LineEdit = ui.find_child("Panel_encyclopedia", true, false).get_node("%SearchField")
	search.grab_focus()
	search.text = "laks"
	search.caret_column = 4
	await _press_key(KEY_SPACE, 32)
	_check(search.text == "laks ", "keys: Space types into a focused text field ('%s')" % search.text)

	var finance_tab: Button = ui.find_child("Tab_finance", true, false)
	finance_tab.grab_focus()
	await _press_key(KEY_ENTER)
	_check(ui.get_active_tab() == "finance", "keys: Enter activates the focused button (%s)" % ui.get_active_tab())
	_check(pauses.size() == 2, "keys: typing a space and Enter do not pause (%s)" % str(pauses))

	_sim.set_paused(true)
	get_viewport().gui_release_focus()
	var day: int = _sim.current_day
	await _press_key(KEY_D, 100)
	await _press_key(KEY_W, 119)
	_check(_sim.current_day == day, "keys: D/W pan the camera without stepping the game (day %d -> %d)" % [day, _sim.current_day])
	scene.queue_free()
	await get_tree().process_frame

## F762: a lasting condition is announced once, not on every refresh (two per
## tick, each with that day's value in the text): one notification entry, one
## lice SFX cue; it is announced again only when it returns. A weather alert
## stays listed for three days instead of being wiped by the next refresh.
func _test_alerts_announce_transitions_once() -> void:
	_check(_sim.start_qa_scenario("forste_sesong", 23), "alerts: seeded scenario starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	var site := str(_sim.get_all_pens()[0].get("id", ""))
	var announced: Array = []
	var cues: Array = []
	var on_alert := func(_level: String, message: String) -> void:
		if message.begins_with("Lus KRITISK"):
			announced.append(message)
	var on_cue := func(key: String) -> void:
		if key == "lice_warning":
			cues.append(key)
	_sim.alert_raised.connect(on_alert)
	_sim.audio_alert_triggered.connect(on_cue)

	_check(bool(_sim.set_qa_pen_lice(site, 1.5).get("success", false)), "alerts: lice set above the limit at %s" % site)
	var listed_every_day := true
	for day in 6:
		_sim.advance_days(1)
		listed_every_day = listed_every_day and _count_alerts("lice", "critical") == 1
	_check(_sim.sea_lice_per_fish > 0.5, "alerts: lice stay above the limit (%.2f)" % _sim.sea_lice_per_fish)
	_check(announced.size() == 1, "alerts: six days over the limit are announced once (%d)" % announced.size())
	_check(cues.size() == 1, "alerts: the lice SFX cue fires once (%d)" % cues.size())
	_check(listed_every_day, "alerts: the condition stays listed while it holds")
	var entries: Array = center._entries.filter(func(e: Dictionary) -> bool: return str(e.get("description", "")).begins_with("Lus KRITISK"))
	_check(entries.size() == 1, "alerts: the notification center has one lice entry (%d)" % entries.size())

	_sim.set_qa_pen_lice(site, 0.0)
	_sim.advance_days(1)
	_check(_count_alerts("lice", "critical") == 0, "alerts: the condition clears")
	_sim.set_qa_pen_lice(site, 1.5)
	_sim.advance_days(1)
	_check(announced.size() == 2 and cues.size() == 2,
		"alerts: a returning condition is announced again (%d, %d)" % [announced.size(), cues.size()])

	_sim._on_weather_event("storm", 0.9, "CI-storm")
	_sim.advance_days(1)
	_check(_has_alert("Vær: CI-storm"), "alerts: a weather alert outlives the next refresh")
	_sim.advance_days(3)
	_check(not _has_alert("Vær: CI-storm"), "alerts: it expires after three days")
	_sim.alert_raised.disconnect(on_alert)
	_sim.audio_alert_triggered.disconnect(on_cue)
	scene.queue_free()
	await get_tree().process_frame

## The Pens tab showed the company lice average for every site; each site
## now shows its own count (the value its 3D collar is tinted by).
func _test_pen_details_show_site_lice() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "site lice: two-site scenario starts")
	_sim.set_paused(true)
	_sim.set_qa_pen_lice("po3_sor", 1.2)
	_sim.set_qa_pen_lice("po3_nord", 0.05)
	_sim.advance_days(1)
	var panel: Control = (load("res://scenes/ui/PenManagement.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	var shown := {}
	for id in ["po3_sor", "po3_nord"]:
		panel.select_pen(id)
		await get_tree().process_frame
		shown[id] = str(panel.get_node("%DetLice").text).to_float()
	_check(shown["po3_sor"] > 0.9 and shown["po3_sor"] < 1.5, "site lice: po3_sor shows its own lice (%.2f)" % shown["po3_sor"])
	_check(shown["po3_nord"] < 0.3, "site lice: po3_nord shows its own lice (%.2f)" % shown["po3_nord"])
	panel.queue_free()
	await get_tree().process_frame

## The carbon ledger panel (Scope 1-3, credits, per-site rows) existed but was
## never a tab, so players could not open it (the harness's "carbon" shot fell
## back to the Offshore tab and the baseline enshrined that).
func _test_carbon_tab_shows_the_ledger() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "carbon: seeded scenario starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	_sim.advance_days(30)
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("map")
	await get_tree().process_frame
	await _click_control(ui.find_child("Tab_carbon", true, false))
	var panel: Control = ui.find_child("Panel_carbon", true, false)
	_check(ui.get_active_tab() == "carbon" and panel != null and panel.visible, "carbon: the Karbon tab opens the ledger")
	if panel != null:
		var ledger: Dictionary = _sim.get_carbon_ledger()
		_check(float(ledger.get("scope1_kg", 0.0)) > 0.0, "carbon: the farm has emitted (scope 1 %.0f kg)" % float(ledger.get("scope1_kg", 0.0)))
		_check(panel.scope1_label.text == "Scope 1: %.1f t" % (float(ledger.get("scope1_kg", 0.0)) / 1000.0),
			"carbon: scope 1 shows the ledger (%s)" % panel.scope1_label.text)
		var dark := func(lbl: Label) -> bool:
			return lbl.has_theme_color_override("font_color") and lbl.get_theme_color("font_color").get_luminance() < 0.5
		_check(dark.call(panel.scope1_label), "carbon: the scope values are dark text on the light cards")
		var row_labels: Array = []
		for child in panel.sites_container.get_children():
			if child is HBoxContainer and not child.is_queued_for_deletion():
				row_labels.append(child.get_child(0))
		_check(not row_labels.is_empty() and row_labels.all(func(l: Label) -> bool: return dark.call(l)),
			"carbon: the site rows are dark text on the light cards")
		var rows := 0
		for child in panel.sites_container.get_children():
			if child is HBoxContainer and not child.is_queued_for_deletion():
				rows += 1
		var site_ids: Array = (ledger.get("sites", []) as Array).map(func(e: Dictionary) -> String: return str(e.get("id", "")))
		_check(site_ids == ["po3_nord", "po3_sor"] and rows == 2, "carbon: one row per sea site (%d rows, %s)" % [rows, str(site_ids)])
	ui.show_tab("map")  # the ledger is now a cached, hidden tab after the map panel
	await get_tree().process_frame
	await _press_key(KEY_ESCAPE)
	await get_tree().process_frame
	_check(ui.get_active_tab() == "dashboard", "carbon: the hidden ledger does not swallow ESC (%s)" % ui.get_active_tab())
	scene.queue_free()
	await get_tree().process_frame

## A scenario start during a running game layered the scenario's sites onto
## the old game (and ignored the QA seed): a scenario always starts afresh.
func _test_scenario_start_replaces_the_running_game() -> void:
	var fingerprints: Array = []
	for run in 2:
		_check(_sim.start_qa_scenario("forste_sesong", 5), "restart: first scenario starts")
		_sim.set_paused(true)
		_sim.advance_days(10)
		_check(_sim.start_qa_scenario("po3_red_sone", 7), "restart: second scenario starts over it")
		_sim.set_paused(true)
		var ids: Array = _sim.get_all_pens().map(func(p: Dictionary) -> String: return str(p.get("id", "")))
		_check(ids == ["po3_nord", "po3_sor"], "restart: only the new scenario's sites (%s)" % str(ids))
		_check(_sim.current_day == 0, "restart: the clock starts at day 0 (%d)" % _sim.current_day)
		_sim.advance_days(20)
		fingerprints.append("%.2f|%d|%.6f" % [float(_sim.balance), int(_sim.fish_count), float(_sim.sea_lice_per_fish)])
	_check(fingerprints[0] == fingerprints[1], "restart: the QA seed replays the same game (%s)" % str(fingerprints))

## Visual QA (shot 07d): the Offshore tab's containment and sludge rows showed
## buttons with no site names (default light label text on the white cards).
func _test_offshore_rows_are_legible() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "offshore: seeded scenario starts")
	_sim.set_paused(true)
	var panel: Control = (load("res://scenes/ui/OffshorePanel.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	for list_name in ["ContainList", "SludgeList"]:
		var names: Array = []
		var legible := true
		for row in panel.get_node("%" + list_name).get_children():
			if row is HBoxContainer and not row.is_queued_for_deletion():
				var lbl: Label = row.get_child(0)
				names.append(lbl.text)
				legible = legible and lbl.has_theme_color_override("font_color") \
					and lbl.get_theme_color("font_color").get_luminance() < 0.5
		_check(names.size() == 2 and legible, "offshore: %s names each site in dark text (%s)" % [list_name, str(names)])
	panel.queue_free()
	await get_tree().process_frame

## F947: ValorizeSludgeNow returned decimal, which Godot cannot marshal, so the
## method was never registered and "Valorisér nå" did nothing.
func _test_offshore_sludge_button_sells_sludge() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "sludge: seeded scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(120)  # ~140 kg DS: above the 100 kg button, below the 500 kg weekly sale
	var site := {}
	for s in _sim.get_sludge_status():
		if float(s.get("accumulated_kg_ds", 0.0)) >= 100.0:
			site = s
	_check(not site.is_empty(), "sludge: a site has 100 kg of sludge to sell (%s)" % str(_sim.get_sludge_status().map(func(s: Dictionary) -> String: return "%s=%.1f" % [str(s.get("id", "")), float(s.get("accumulated_kg_ds", 0.0))])))
	if site.is_empty():
		return
	var panel: Control = (load("res://scenes/ui/OffshorePanel.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	var button: Button = null
	for b: Button in panel.get_node("%SludgeList").find_children("*", "Button", true, false):
		if b.text.begins_with("Valorisér nå") and not b.is_queued_for_deletion():
			button = b
	_check(button != null, "sludge: the card offers 'Valorisér nå'")
	if button != null:
		var cash: float = float(_sim.balance)
		button.pressed.emit()
		await get_tree().process_frame
		var after := {}
		for s in _sim.get_sludge_status():
			if str(s.get("id", "")) == str(site.get("id", "")):
				after = s
		_check(float(after.get("accumulated_kg_ds", -1.0)) == 0.0, "sludge: the button sells the site's sludge")
		_check(float(_sim.balance) > cash, "sludge: the sale is paid (%.0f -> %.0f)" % [cash, float(_sim.balance)])
	panel.queue_free()
	await get_tree().process_frame

## F064: a load resets per-game bridge state. The forecast cache was keyed by
## day number only, so loading a save made the same day served the projection
## of the game being left.
func _test_load_resets_the_forecast_cache() -> void:
	_check(_sim.start_qa_scenario("forste_sesong", 37), "forecast: seeded scenario starts")
	_sim.set_paused(true)
	_check(_sim.save_game("ci_forecast"), "forecast: saved with fish in the pen")
	var site_id := str(_sim.get_all_pens()[0].get("id", ""))
	_sim.harvest_pen(site_id)
	var emptied: Array = _sim.get_forecast().get("biomass_kg", [])
	_check(not emptied.is_empty() and float(emptied[0]) == 0.0, "forecast: no biomass after the harvest (%s)" % str(emptied.slice(0, 1)))
	var entry := _find_save("ci_forecast")
	_check(not entry.is_empty() and _sim.load_game(String(entry.get("file", ""))), "forecast: the save loads")
	var reloaded: Array = _sim.get_forecast().get("biomass_kg", [])
	_check(not reloaded.is_empty() and float(reloaded[0]) > 0.0,
		"forecast: after the load it shows the saved fish again (%s)" % str(reloaded.slice(0, 1)))

## F665: the toast layer consumed every ESC and hid itself for good, so one
## ESC on the main menu silenced all toasts and the save list's ESC never ran.
func _test_escape_spares_toasts_and_closes_the_save_list() -> void:
	var toasts: CanvasLayer = get_node("/root/ToastNotification")
	var menu := await _spawn_main_menu()
	await _press_key(KEY_ESCAPE)
	_check(toasts.visible, "toasts: an ESC on the main menu leaves the toast layer up")
	toasts.show_toast("CI toast", "INFO", 1.0)
	await get_tree().process_frame
	_check(toasts.visible and not toasts._active_nodes.is_empty(), "toasts: a toast still appears after that ESC")
	menu.btn_load.pressed.emit()
	await get_tree().process_frame
	_check(menu.save_load_menu.visible, "toasts: 'Last Spill' opens the save list")
	await _press_key(KEY_ESCAPE)
	await get_tree().process_frame
	_check(not menu.save_load_menu.visible and menu.center.visible, "toasts: ESC closes the save list back to the menu")
	menu.queue_free()
	await get_tree().process_frame

## Re-review: N opened the notification panel while typing (and ate the "n");
## the Export tab's ✕ only emitted a signal GameUI never connected.
func _test_typing_hotkeys_and_close_buttons() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	ui.show_tab("encyclopedia")
	await get_tree().process_frame
	var search: LineEdit = ui.find_child("Panel_encyclopedia", true, false).get_node("%SearchField")
	search.grab_focus()
	search.text = ""
	await _press_key(KEY_N, 110)
	_check(search.text == "n", "keys: N types into a focused text field ('%s')" % search.text)
	_check(not center._is_open, "keys: N while typing leaves the notification panel shut")
	get_viewport().gui_release_focus()
	await _press_key(KEY_N, 110)
	_check(center._is_open, "keys: N outside a text field opens the notifications")
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.5).timeout

	await _click_control(ui.find_child("Tab_export", true, false))
	_check(ui.get_active_tab() == "export", "close: the Export tab opens")
	var export_panel: Control = ui.find_child("Panel_export", true, false)
	get_node("/root/ToastNotification").clear_all()  # toasts sit over the top-right ✕
	await get_tree().process_frame
	await _click_control(export_panel.get_node("%CloseButton"))
	await get_tree().process_frame
	_check(ui.get_active_tab() == "dashboard", "close: the Export tab's ✕ returns to the dashboard (%s)" % ui.get_active_tab())
	scene.queue_free()
	await get_tree().process_frame

## Re-review: the tutorial card (later in the tree than GameUI) took every ESC
## while shown and hid itself until the next refresh, so ESC never closed a tab.
func _test_tutorial_card_leaves_escape_to_tabs() -> void:
	_check(_sim.start_qa_scenario("forste_sesong", 41), "tutorial esc: tutorial scenario starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var ui: Control = scene.get_node("UILayer/GameUI")
	var card: Control = scene.get_node("UILayer/TutorialUI")
	card.visible = true  # shown as for a first-run player (the test profile may disable it)
	ui.show_tab("finance")
	await get_tree().process_frame
	await _press_key(KEY_ESCAPE)
	await get_tree().process_frame
	_check(ui.get_active_tab() == "dashboard", "tutorial esc: ESC closes the tab (%s)" % ui.get_active_tab())
	_check(card.visible, "tutorial esc: the card stays")
	scene.queue_free()
	await get_tree().process_frame

## Re-review: a weather alert pushed before the tick's refresh was stamped with
## the previous day (a 2-day TTL), and the vaccination alert claimed an open
## window from January (F763).
func _test_event_alerts_and_vaccine_window() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "alert days: seeded scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(10)
	_check(_count_alerts("vaccine_window", "warning") == 0, "alert days: no open-window alert in January")
	var today: int = _sim.current_day
	_sim.current_day = today - 1  # a weather event arrives before the refresh
	_sim._on_weather_event("storm", 0.9, "CI-stale-storm")
	_sim.advance_days(2)
	_check(_has_alert("Vær: CI-stale-storm"), "alert days: a weather alert pushed before the refresh lasts three days")
	_sim.advance_days(270)  # day 282: inside the October window
	_check(_count_alerts("vaccine_window", "warning") == 1, "alert days: the open-window alert shows in October")

func _count_alerts(key: String, level: String) -> int:
	var n := 0
	for a in _sim.active_alerts:
		if str(a.get("key", "")) == key and str(a.get("level", "")) == level:
			n += 1
	return n

func _has_alert(message: String) -> bool:
	for a in _sim.active_alerts:
		if str(a.get("message", "")) == message:
			return true
	return false

## F945: Phase 5.3–5.4 proxies are reachable through the SimBridge facade.
func _test_facade_forwards_phase5_systems() -> void:
	for method in ["get_sludge_status", "toggle_sludge_hub", "valorize_sludge_now",
			"plan_cohort_transfer", "execute_cohort_transfer", "get_transfer_destinations",
			"get_carbon_ledger", "get_carbon_intensity", "is_carbon_neutral_year",
			"processing_tier", "set_night_shift_policy"]:
		_check(_sim.has_method(method), "facade forwards %s" % method)

# ── Helpers ────────────────────────────────────────────────────────────────────

func _start_paused_game() -> void:
	_sim.start_new_game("Player")
	_sim.set_paused(true)

func _spawn_main_menu() -> Control:
	var menu: Control = (load("res://scenes/MainMenu.tscn") as PackedScene).instantiate()
	add_child(menu)
	await get_tree().process_frame
	return menu

func _find_save(save_name: String) -> Dictionary:
	for entry in _sim.get_save_list():
		if String(entry.get("name", "")) == save_name:
			return entry
	return {}

func _count_slots(container: Node) -> int:
	var n := 0
	for child in container.get_children():
		if child is PanelContainer and String(child.name).begins_with("Slot_"):
			n += 1
	return n

func _first_slot_button(container: Node, button_name: String) -> Button:
	for child in container.get_children():
		if child is PanelContainer and String(child.name).begins_with("Slot_"):
			var btn := child.find_child(button_name, true, false)
			if btn is Button:
				return btn
	return null

## A real left click at a viewport position: through _input, the GUI,
## _unhandled_input and physics picking. Headless Godot never sends the
## window's mouse-enter, and physics picking drops events while the cursor is
## outside the viewport, so announce the cursor first.
func _click_at(pos: Vector2) -> void:
	var vp := get_viewport()
	vp.notification(Node.NOTIFICATION_VP_MOUSE_ENTER)
	for pressed in [true, false]:
		var ev := InputEventMouseButton.new()
		ev.position = pos
		ev.global_position = pos
		ev.button_index = MOUSE_BUTTON_LEFT
		ev.pressed = pressed
		vp.push_input(ev, true)
	await get_tree().physics_frame
	await get_tree().physics_frame
	await get_tree().process_frame

## A real key press and release (unicode for typed characters).
func _press_key(keycode: Key, unicode: int = 0) -> void:
	for pressed in [true, false]:
		var ev := InputEventKey.new()
		ev.keycode = keycode
		ev.physical_keycode = keycode
		ev.unicode = unicode if pressed else 0
		ev.pressed = pressed
		get_viewport().push_input(ev)
	await get_tree().process_frame

## F744: the fjord shows the AI rivals' farms and the player's wellboats.
## Both managers asked SimBridge for methods it does not have (one of them a
## GameBridge method), so neither had ever drawn anything.
func _test_fjord_shows_rival_farms_and_wellboats() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "fjord visuals: seeded two-site scenario starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var rivals: CompetitorFarmVisualManager = env.get_node("CompetitorFarmVisualManager")
	var boats: WellboatVisualManager = env.get_node("WellboatVisualManager")
	boats.sail_seconds = 0.2
	boats.return_delay = 0.0
	_sim.state_updated.emit()
	await get_tree().process_frame

	var expected := mini(_sim.get_competitor_farms().size(), rivals.max_competitors)
	_check(expected > 0, "rival farms: the game has AI rivals (%d)" % expected)
	_check(rivals.farm_ids().size() == expected,
		"rival farms: one farm per rival (%d of %d)" % [rivals.farm_ids().size(), expected])
	var greyed := not rivals.farm_ids().is_empty()
	for id in rivals.farm_ids():
		var meshes: Array = (rivals._competitor_farms[id].root as Node).find_children("*", "MeshInstance3D", true, false)
		greyed = greyed and not meshes.is_empty()
		for mesh in meshes:
			greyed = greyed and (mesh as MeshInstance3D).material_override == rivals._grey_material()
	_check(greyed, "rival farms: every rival cage is drawn grey")
	# The bridge's fixed farm positions (x up to ±1000 m) lie inside the
	# fjord walls; the farms take the pen manager's channel slots instead.
	var terrain: FjordTerrain = env.get_node("Terrain")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var afloat := not rivals.farm_ids().is_empty()
	var clear := true
	for id in rivals.farm_ids():
		for cage in rivals._competitor_farms[id].cages:
			var at: Vector3 = (cage as Node3D).global_position
			afloat = afloat and terrain.get_height_at(at.x, at.z) < 0.0
			for pen_id in pens.get_pen_ids():
				clear = clear and at.distance_to(pens.get_pen_position(pen_id)) > 60.0
	_check(afloat, "rival farms: every rival cage floats on open water")
	_check(clear, "rival farms: no rival cage overlaps a player pen")
	_check(CompetitorFarmVisualManager.farm_slot(5, 6, 12) == 6 and CompetitorFarmVisualManager.farm_slot(5, 7, 12) == -1,
		"rival farms: a rival gives up its slot once the player's sites reach it")

	# Feed barges sail the channel at sea level; they used to leave from
	# inside the fjord wall and arc 50 m over the terrain.
	var barges: FeedBargeVisualManager = env.get_node("FeedBargeVisualManager")
	var barge_sites: Array = barges.active_site_ids()
	_check(not barge_sites.is_empty(), "feed barge: a barge sails to a stocked site (%d)" % barge_sites.size())
	var barges_afloat := not barge_sites.is_empty()
	var barges_yellow := not barge_sites.is_empty()
	for id in barge_sites:
		barges_afloat = barges_afloat and _route_on_water(barges._active_barges[id].path, terrain)
		var barge_deck := (barges._active_barges[id].boat as Node).get_node("Deck") as MeshInstance3D
		var barge_mat := barge_deck.material_override as ShaderMaterial
		barges_yellow = barges_yellow and barge_mat.resource_path.is_empty() \
			and barge_mat.get_shader_parameter("albedo_color") == FeedBargeVisualManager.FEED_YELLOW
	_check(barges_afloat, "feed barge: every route point is at sea level on open water")
	_check(barges_yellow, "feed barge: tinted yellow on its own copy of the deck material")

	var sites: Array = _sim.get_all_pens()
	_check(not sites.is_empty(), "wellboat: the player has a site to send a boat to")
	if sites.is_empty():
		scene.queue_free()
		return
	var site_id := str(sites[0].get("id", ""))
	_check(_sim.request_wellboat(site_id, "rov"), "wellboat: an ROV inspection is booked for %s" % site_id)
	_sim.state_updated.emit()
	await get_tree().process_frame
	var jobs: Array = boats.active_job_ids()
	_check(jobs.size() == 1, "wellboat: a boat sails for the booked job (%d)" % jobs.size())
	if jobs.size() != 1:
		scene.queue_free()
		return
	var job_id: String = jobs[0]
	var deck := (boats._active_wellboats[job_id].boat as Node).get_node("Deck") as MeshInstance3D
	var deck_mat := deck.material_override as ShaderMaterial
	_check(deck_mat.resource_path.is_empty(), "wellboat: the tint goes on the boat's own copy of the deck material")
	_check(deck_mat.get_shader_parameter("albedo_color") == WellboatVisualManager.JOB_COLOURS["other"],
		"wellboat: an ROV boat is tinted blue")
	var boat_path: Path3D = boats._active_wellboats[job_id].path
	_check(_route_on_water(boat_path, terrain), "wellboat: every route point is at sea level on open water")
	await get_tree().create_timer(0.5).timeout
	_check(boats.job_state(job_id) == "at_destination", "wellboat: the boat waits on site (%s)" % boats.job_state(job_id))
	# The boat sits at the end of its route, level with its pen.
	var route_end := boat_path.to_global(boat_path.curve.get_point_position(boat_path.curve.point_count - 1))
	var boat_node: Node3D = boats._active_wellboats[job_id].boat
	_check(boat_node.global_position.distance_to(route_end) < 1.0 and absf(route_end.z - pens.get_pen_position(site_id).z) < 1.0,
		"wellboat: the waiting boat lies at the end of its route, level with its pen")

	_sim.advance_days(3)
	_sim.state_updated.emit()
	await get_tree().process_frame
	_check(_sim.get_wellboat_jobs().is_empty(), "wellboat: the ROV job is carried out")
	_check(boats.job_state(job_id) == "return", "wellboat: the boat heads home once the job is done (%s)" % boats.job_state(job_id))
	_check(is_equal_approx(boat_node.rotation.y, PI), "wellboat: the boat turns round for the trip home")
	await get_tree().create_timer(0.5).timeout
	_check(boats.active_job_ids().is_empty(), "wellboat: the boat leaves the fjord after the trip home")
	scene.queue_free()
	await get_tree().process_frame

## True when every baked point of a boat's path is at sea level over water.
func _route_on_water(path: Path3D, terrain: FjordTerrain) -> bool:
	var points := path.curve.get_baked_points() if path != null and path.curve != null else PackedVector3Array()
	if points.size() < 2:
		return false
	for point in points:
		var at := path.to_global(point)
		if absf(at.y) > 0.01 or terrain.get_height_at(at.x, at.z) >= 0.0:
			return false
	return true

## A running GameScene over a seeded game, paused.
func _spawn_game_scene() -> Node:
	_check(_sim.start_qa_new_game("Player", 20_000_000, 77), "setup: seeded game starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	return scene

func _mod_row(panel: Control, index: int) -> Control:
	return panel._list_container.get_child(index)

func _click_control(control: Control) -> void:
	_check(control != null and control.is_visible_in_tree(), "click target %s is on screen" % (control.name if control else "missing"))
	if control != null:
		await _click_at(control.get_global_rect().get_center())

## Settle the fjord camera over a pen (no auto-orbit drift between a click's
## screen position and the physics step that picks it).
func _frame_pen(cam: FjordCamera, pos: Vector3) -> void:
	cam.auto_orbit_enabled = false
	cam.fly_to_position(pos, FjordCamera.ZoomLevel.FARM, 0.05)
	await get_tree().create_timer(0.2).timeout
	var last := Vector3.INF
	for i in 3000:
		await get_tree().process_frame
		if cam.global_position.distance_to(last) < 0.001:
			return
		last = cam.global_position

func _pen_on_screen(cam: Camera3D, pens: FishPenManager, pen_id: String) -> Vector2:
	return cam.unproject_position(pens.get_pen_position(pen_id))

func _clear_test_saves() -> void:
	var dir := DirAccess.open(TEST_SAVE_DIR)
	if dir == null:
		return
	for file in dir.get_files():
		dir.remove(file)

func _check(condition: bool, label: String) -> void:
	_checks += 1
	if condition:
		print("  ok   %s" % label)
	else:
		_failures.append(label)
		printerr("  FAIL %s" % label)

func _finish() -> void:
	print("UI_FLOW_RESULT checks=%d failures=%d" % [_checks, _failures.size()])
	for f in _failures:
		print("  failed: %s" % f)
	get_tree().quit(1 if not _failures.is_empty() else 0)
