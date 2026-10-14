extends Node

## Headless player-flow tests for the Godot UI layer (save/load + menu flows).
##
## Run (also CI step "Godot UI flow tests"):
##   godot --headless --path godot res://tests/UiFlowTests.tscn
##
## Drives the real autoloads (GameBridge C#, SaveLoadManager C#, SimBridge) and
## the real scenes, presses buttons through their `pressed` signals and asserts
## the visible outcome. Saves go to user://ci_ui_tests/<checkout>/saves/
## (never the player's slots). Prints `UI_FLOW_RESULT checks=N failures=M`
## and exits non-zero on any failure, so the CI wrapper can gate on it.

const SCENARIO_ID := "forste_sesong"

## One test directory per checkout: runs in parallel worktrees share user://
## (it follows the project name) and would wipe each other's saves.
var TEST_ROOT := "user://ci_ui_tests/%d/" % absi(ProjectSettings.globalize_path("res://").hash())
var TEST_SAVE_DIR := TEST_ROOT + "saves/"
var ESCAPE_SAVE := TEST_ROOT + "escape.json"

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
	# The checks read Norwegian text (the game's default language); a
	# language.cfg left on English in the shared user dir must not flip them.
	TranslationServer.set_locale("no")

	# First, while no game has started in this process (a fresh launch).
	await _test_mod_scenario_starts_on_a_fresh_launch()
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
	await _test_telemetry_oxygen_saturation_follows_the_sim()
	await _test_honest_economy_reaches_the_ui()
	await _test_pen_models_read_as_equipment()
	await _test_hero_pen_and_site_kit()
	await _test_vessel_fleet()
	_test_facade_forwards_phase5_systems()
	await _test_terrain_faces_up_and_shares_one_heightfield()
	await _test_camera_frames_sky_and_avoids_terrain()
	await _test_camera_keys_leave_focused_controls_alone()
	await _test_environment_director_owns_the_look()
	await _test_water_grid_bake_and_sea_state()
	await _test_theme_and_contrast_on_every_screen()
	_test_formatters_speak_the_locale()
	await _test_dashboard_title_names_the_company()
	await _test_young_smolts_raise_no_fcr_alarm()
	await _test_banner_and_inbox_open_the_right_tab()
	await _test_text_reads_norwegian_in_young_and_red_zone_farms()
	await _test_every_button_answers()
	await _test_screen_regions_never_overlap()
	await _test_overflow_menu_opens_hidden_tabs()
	await _test_tab_shortcuts_are_command_digits()
	await _test_dashboard_alerts_one_row_per_category_and_site()
	await _test_escape_opens_the_pause_menu()
	await _test_pause_menu_reaches_the_main_menu()
	await _test_pause_settings_and_back()
	await _test_window_close_asks_to_save()
	await _test_continue_restores_the_latest_save()
	await _test_credits_list_every_third_party_work()
	await _test_pause_save_list_buttons_answer()
	await _test_scenario_picker()

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
	_check(time_label != null and "2,8 år igjen" in time_label.text,
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
	var dir := ProjectSettings.globalize_path(TEST_ROOT + "exports")
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
	# U1-06/D15: that ESC opened the pause menu over the dashboard; ESC again
	# closes it, then the tab button takes the click.
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	_check(pause.is_open(), "esc: ESC on the dashboard opens the pause menu")
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.3).timeout
	_check(not pause.is_open() and not pause.visible, "esc: ESC in the pause menu closes it")
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
		_check(panel.scope1_label.text == "Scope 1: %s t" % SimFormatters.num(float(ledger.get("scope1_kg", 0.0)) / 1000.0, 1),
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
		if b.text.begins_with("Gjenvinn nå") and not b.is_queued_for_deletion():
			button = b
	_check(button != null, "sludge: the card offers 'Gjenvinn nå'")
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

## S1-01: the telemetry's oxygen saturation reads the sim's seawater solubility.
## The panel kept its own copy of the old freshwater table, so once the sim's
## solubility was corrected it showed 82% for fully saturated water.
func _test_telemetry_oxygen_saturation_follows_the_sim() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "telemetry: seeded scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(5)
	var sat := float(_sim.oxygen_saturation_mgl)
	var o2 := float(_sim.oxygen_mgl)
	_check(sat > 7.0 and sat < 11.5,
		"telemetry: the bridge reports seawater O2 solubility (%.2f mg/L)" % sat)
	var panel: Control = (load("res://scenes/ui/TelemetryPanel.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	panel.refresh()
	var shown := ""
	for node in panel.find_children("*", "Label", true, false):
		var label := node as Label
		if label.text == "Oksygenmetning" and label.get_parent().get_child_count() > 1:
			shown = (label.get_parent().get_child(1) as Label).text
	# The panel writes Norwegian percentages ("100 %" with a no-break space, U1-03).
	var expected := SimFormatters.percent(clampf(o2 / sat, 0.0, 1.5))
	_check(shown == expected and o2 / sat > 0.9,
		"telemetry: saturation reads %s for %.2f of %.2f mg/L (shown '%s')" % [expected, o2, sat, shown])
	panel.queue_free()
	await get_tree().process_frame

## S1-03 (owner decision D16): each scenario's bankruptcy floor and the warning
## ladder reach the game; achievements carry a medal and tutorial steps no cash
## through the signal chain (GameBridge -> SimBridge -> UI); the game-over reason
## is a translation key.
func _test_honest_economy_reaches_the_ui() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "economy: seeded scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(1)
	_check(is_equal_approx(float(_sim.bankruptcy_floor), -15000000.0),
		"economy: po3_red_sone runs with its own bankruptcy floor (%.0f)" % float(_sim.bankruptcy_floor))
	_check(_sim.start_qa_scenario("forste_sesong", 23), "economy: tutorial scenario starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	_sim.advance_days(1)
	_check(is_equal_approx(float(_sim.bankruptcy_floor), -1000000.0),
		"economy: forste_sesong keeps the default floor (%.0f)" % float(_sim.bankruptcy_floor))

	# Warning ladder: a rung becomes one condition alert naming cash and floor.
	var state: Node = _sim._state
	state.balance = -600000.0
	state.solvency_level = 2
	state._rebuild_alerts()
	var rung := ""
	for a in _sim.active_alerts:
		if str(a.get("key", "")) == "solvency:2" and str(a.get("level", "")) == "critical":
			rung = str(a.get("message", ""))
	var title := tr("UI_SOLVENCY_BANK_WARNING")
	_check(title != "UI_SOLVENCY_BANK_WARNING" and rung.begins_with(title + ": "),
		"economy: the bank-warning rung is a critical alert ('%s')" % rung)

	# Achievements carry a medal (not NOK) to the listeners and the toast.
	var bridge: Node = get_node("/root/GameBridge")
	var medals: Array = []
	var on_achievement := func(_id: String, _name: String, _desc: String, medal: String, _tier: String) -> void:
		medals.append(medal)
	_sim.achievement_unlocked.connect(on_achievement)
	bridge.emit_signal("AchievementUnlocked", "ci-medal", "CI-medalje", "CI", "silver", "Beginner")
	_sim.achievement_unlocked.disconnect(on_achievement)
	_check(medals == ["silver"], "economy: an unlock relays its medal (%s)" % str(medals))
	var toasts: Node = get_node("/root/ToastNotification")
	var toast := ""
	# Shown, queued behind a full stack, or already dismissed (U1-04 records
	# a message in _recent_messages only when it leaves the screen).
	var messages: Array = toasts._recent_messages.keys()
	for node in toasts._active_nodes:
		messages.append(String(node.get_meta("toast_message", "")))
	for queued in toasts._queue:
		messages.append(queued.message)
	for message in messages:
		if str(message).contains("CI-medalje"):
			toast = str(message)
	_check(toast.ends_with(" — " + tr("UI_MEDAL_SILVER")) and tr("UI_MEDAL_SILVER") != "UI_MEDAL_SILVER",
		"economy: the unlock toast names the medal ('%s')" % toast)
	var listed: Array = _sim.get_achievement_list()
	_check(not listed.is_empty() and listed.all(func(a: Dictionary) -> bool:
			return a.has("medal") and not a.has("reward_nok")),
		"economy: the achievement list gives medals, no reward cash (%d)" % listed.size())

	# Tutorial steps: no reward argument; the step toast names the step.
	var steps: Array = []
	var on_step := func(n: int, _title: String) -> void: steps.append(n)
	_sim.tutorial_step_completed.connect(on_step)
	bridge.emit_signal("TutorialStepCompleted", 3, "CI-steg")
	_sim.tutorial_step_completed.disconnect(on_step)
	_check(steps == [3], "economy: a completed step relays without a reward (%s)" % str(steps))
	# The card's completion toast text (the card itself is not wired in
	# GameScene yet: its @export nodes resolve to null, see the S1-03 notes).
	var tutorial: Node = scene.get_node("UILayer/TutorialUI")
	var step_toast := str(tutorial._tr_step_complete(3, "CI-steg"))
	_check(step_toast.contains("3") and step_toast.contains("CI-steg") and not step_toast.contains("NOK")
			and not step_toast.contains("Belønning") and not step_toast.contains("Reward"),
		"economy: the step toast names the step, not an amount ('%s')" % step_toast)

	# The engine ends a bankrupt game with a translation key.
	var reason := tr("GAME_OVER_BANKRUPTCY")
	_check(reason != "GAME_OVER_BANKRUPTCY", "economy: the bankruptcy reason key is translated ('%s')" % reason)
	scene.queue_free()
	await get_tree().process_frame

## F945: Phase 5.3–5.4 proxies are reachable through the SimBridge facade.
func _test_facade_forwards_phase5_systems() -> void:
	for method in ["get_sludge_status", "toggle_sludge_hub", "valorize_sludge_now",
			"plan_cohort_transfer", "execute_cohort_transfer", "get_transfer_destinations",
			"get_carbon_ledger", "get_carbon_intensity", "is_carbon_neutral_year",
			"processing_tier", "set_night_shift_policy"]:
		_check(_sim.has_method(method), "facade forwards %s" % method)

## U1-01/U1-02: the Kyst & Stål project theme (gui/theme/custom) reaches every
## Control under every CanvasLayer — no Godot-default look on any screen — and
## every visible text meets WCAG contrast (4.5:1; 3:1 large) against what is
## really drawn behind it. Each screen is judged after its fade-in.
const THEME_SCREENS_TABS := ["dashboard", "fjord", "map", "finance", "pens", "production", "research",
	"sustainability", "ras", "offshore", "analytics", "telemetry", "ai_feeding", "crew", "contracts",
	"carbon", "encyclopedia", "export"]
const THEME_SETTLE_S := 0.9

func _test_theme_and_contrast_on_every_screen() -> void:
	var project_theme := ThemeDB.get_project_theme()
	_check(project_theme != null and project_theme.resource_path == "res://theme/lakskongen_theme.tres"
		and project_theme.get_meta("surface", "") == "ink",
		"theme: gui/theme/custom is the generated Kyst & Stål Ink theme")
	var menu := await _spawn_main_menu()
	await _lint_screen("main menu")
	menu.btn_new_game.pressed.emit()
	await _lint_screen("scenario picker")
	menu.queue_free()
	await get_tree().process_frame

	var scene := await _spawn_game_scene()
	_sim.skip_tutorial()
	await get_tree().process_frame
	var ui: Control = scene.get_node("UILayer/GameUI")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	var docs: Control = ui.get_node("%PanelContainer")
	_check(docs.theme != null and docs.theme.resource_path == "res://theme/lakskongen_chart_theme.tres",
		"theme: GameUI documents sit on the Chart-paper theme")
	for tab in THEME_SCREENS_TABS:
		ui.show_tab(tab)
		center.hide_panel()
		await _lint_screen("tab " + tab)
		if tab == "telemetry":
			_check_telemetry_headers(ui.find_child("Panel_telemetry", true, false))
	center.show_panel()
	await _lint_screen("notification center")
	center.hide_panel()
	# U1-06/D15: save and load moved from a tab into the pause menu; its
	# screens are judged like the tab was (and the menu, settings and the
	# save guard besides).
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	pause.show_menu()
	await _lint_screen("pause menu")
	pause.btn_save.pressed.emit()
	await _lint_screen("pause save list")
	pause.save_load_menu._close()
	pause.btn_settings.pressed.emit()
	await _lint_screen("pause settings")
	pause.settings_menu._on_close()
	pause.request_quit()
	await _lint_screen("pause save guard")
	pause.hide_menu()
	await get_tree().create_timer(0.3).timeout
	var inbox := scene.get_node("AttentionInbox")
	inbox.open_inbox()
	await _lint_screen("attention inbox")
	inbox.visible = false
	# The genetics panel is shot standalone over the game (harness shot 07).
	var layer := CanvasLayer.new()
	layer.layer = 10
	add_child(layer)
	layer.add_child((load("res://scenes/ui/GeneticsPanel.tscn") as PackedScene).instantiate())
	await _lint_screen("genetics panel")
	layer.queue_free()
	scene.queue_free()
	await get_tree().process_frame

## Two checks per screen: theme resolution and contrast, offenders listed.
func _lint_screen(screen: String) -> void:
	await get_tree().create_timer(THEME_SETTLE_S).timeout
	var lint := preload("res://tests/ThemeLint.gd")
	var root := get_tree().root
	var unthemed: PackedStringArray = lint.unthemed(root)
	_check(unthemed.is_empty(), "theme: %s has no Godot-default controls %s" % [screen, str(unthemed.slice(0, 4))])
	var low: PackedStringArray = lint.low_contrast(root)
	_check(low.is_empty(), "contrast: %s text meets 4.5:1 (3:1 large) %s" % [screen, str(low.slice(0, 4))])
	# U1-03: the same screen reads as Norwegian — no format tokens, ids,
	# CamelCase enums, raw values, decimal points or English words.
	var wrong: PackedStringArray = TextLint.offenders(root, "nb")
	_check(wrong.is_empty(), "text: %s reads as Norwegian %s" % [screen, str(wrong.slice(0, 4))])

## Roadmap U1-01 #3: the panel title and the four section headers are drawn
## (they rendered white on white before the theme reached the CanvasLayer).
func _check_telemetry_headers(panel: Node) -> void:
	var shown := []
	if panel != null:
		for lbl: Label in panel.find_children("*", "Label", true, false):
			if lbl.is_visible_in_tree():
				shown.append(lbl.text)
	var wanted := ["Driftstelemetri", "Vann & Oksygen", "Lus & resistens", "Gjellehelse", "Fôr & FCR"]
	var missing := wanted.filter(func(w: String) -> bool: return not shown.any(func(t: String) -> bool: return t.contains(w)))
	_check(missing.is_empty(), "telemetry: title and section headers are shown (missing %s)" % str(missing))

# ── U1-03 text correctness / U1-08 no dead verbs ───────────────────────────────

## Buttons the press-everything walk leaves alone: they open the OS folder
## picker (no window in a headless run).
const BUTTONS_NOT_PRESSED := ["BrowseButton"]
const BUTTON_SETTLE_S := 0.5
const TextLint := preload("res://tests/TextLint.gd")

## Captures push_warning / push_error / printerr lines while a test runs
## (Godot's Logger; called from any thread, hence the mutex).
class _WarningLog extends Logger:
	var _lines: PackedStringArray = []
	var _mutex := Mutex.new()

	func _log_error(function: String, file: String, line: int, code: String, rationale: String,
			_editor_notify: bool, error_type: int, _script_backtraces: Array[ScriptBacktrace]) -> void:
		var kind := "warning" if error_type == ERROR_TYPE_WARNING else "error"
		_add("%s: %s (%s:%d %s)" % [kind, rationale if rationale != "" else code, file.get_file(), line, function])

	func _log_message(message: String, error: bool) -> void:
		if error:
			_add("printerr: " + message.strip_edges())

	func _add(line: String) -> void:
		_mutex.lock()
		_lines.append(line)
		_mutex.unlock()

	func take() -> PackedStringArray:
		_mutex.lock()
		var out := _lines.duplicate()
		_lines.clear()
		_mutex.unlock()
		return out

## U1-03: money, counts, percentages and dates read the Norwegian way in nb
## and the English way in en — the formatter every panel goes through.
func _test_formatters_speak_the_locale() -> void:
	var nbsp := " "
	var nok := SimFormatters.format_nok(11_490_000.0, "nb")
	_check(nok == "11,49 MNOK", "format: 11 490 000 kr reads '11,49 MNOK' in nb (%s)" % nok)
	var count := SimFormatters.integer(71_472, "nb")
	_check(count == "71" + nbsp + "472", "format: 71472 is grouped with a no-break space in nb (%s)" % count.replace(nbsp, "⍽"))
	var date := SimFormatters.format_date(2027, 1, 12, "nb")
	_check(date == "12. jan. 2027", "format: a date reads '12. jan. 2027' in nb (%s)" % date)
	var share := SimFormatters.percent(0.44, 0, "nb")
	_check(share == "44" + nbsp + "%", "format: a share reads '44 %%' in nb (%s)" % share.replace(nbsp, "⍽"))
	var tonnes := SimFormatters.fmt("%.1f t", [2.5], "nb")
	_check(tonnes == "2,5 t", "format: decimals take a comma in nb (%s)" % tonnes)
	_check(SimFormatters.format_nok(11_490_000.0, "en") == "11.49 MNOK", "format: en keeps '11.49 MNOK'")
	_check(SimFormatters.integer(71_472, "en") == "71,472", "format: en groups thousands with a comma")
	_check(SimFormatters.format_date(2027, 1, 12, "en") == "Jan 12, 2027", "format: en reads 'Jan 12, 2027'")

## U1-03: the dashboard title names the company (it printed "%s — Oversikt").
func _test_dashboard_title_names_the_company() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("dashboard")
	await get_tree().process_frame
	var dashboard := _node_with(ui.find_child("Panel_dashboard", true, false), "_title_label")
	var title: String = dashboard._title_label.text if dashboard != null else ""
	var company := str(_sim.company_name)
	_check(company != "" and title.contains(company) and not title.contains("%"),
		"dashboard: the title names the company '%s' (%s)" % [company, title])
	scene.queue_free()
	await get_tree().process_frame

## U1-08: day 12 of Første sesong — 100 g smolts eat for growth, so the feed
## factor is not judged yet and the farm is not called critical for it.
## Once the fish settle the same check runs on the calibrated target.
func _test_young_smolts_raise_no_fcr_alarm() -> void:
	_check(_sim.start_qa_scenario(SCENARIO_ID, 91), "fcr: Første sesong starts")
	_sim.set_paused(true)
	_sim.advance_days(12)
	var targets: Dictionary = _sim.get_fcr_targets()
	_check(float(targets.get("target", 0.0)) > 0.0 and float(targets.get("poor", 0.0)) > float(targets.get("target", 0.0)),
		"fcr: the bridge serves the calibrated target and poor FCR (%s)" % str(targets))
	_check(not _sim.fcr_is_settled(), "fcr: 12 days after stocking 100 g smolts the FCR is not judged yet")
	var verdict := FarmStateBanner.evaluate(_sim)
	_check(int((verdict["factors"] as Dictionary).get("fcr", -1)) == 0,
		"fcr: the farm banner raises no FCR factor on day 12 (%s, FCR %s)" % [str(verdict["factors"]), str(_sim.fcr)])
	_check(verdict["summary"] != tr("UI_FARM_STATE_CRITICAL") or str(verdict["worst_key"]) != "fcr",
		"fcr: no 'Kritisk tilstand' from the feed factor on day 12 (%s)" % str(verdict["summary"]))

## U1-08: the banner's call to action and the inbox's feed advice open the tab
## where the player can act on them.
func _test_banner_and_inbox_open_the_right_tab() -> void:
	var scene := await _spawn_game_scene()
	_sim.skip_tutorial()
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("dashboard")
	await get_tree().process_frame
	var dashboard := _node_with(ui.find_child("Panel_dashboard", true, false), "_state_banner")
	var banner: FarmStateBanner = dashboard._state_banner if dashboard != null else null
	_check(banner != null, "banner: the dashboard shows the farm-state banner")
	if banner != null:
		for factor in FarmStateBanner.ACTION_TABS:
			ui.show_tab("dashboard")
			await get_tree().process_frame
			banner.set_action(factor)
			banner._action.pressed.emit()
			await get_tree().process_frame
			var want: String = FarmStateBanner.ACTION_TABS[factor]
			_check(ui.get_active_tab() == want, "banner: the %s call to action opens '%s' (%s)" % [factor, want, ui.get_active_tab()])
	var inbox := scene.get_node("AttentionInbox")
	inbox._on_item_action("open_intervention_detail", "recipe-tune", "")
	await get_tree().process_frame
	_check(ui.get_active_tab() == "ai_feeding", "inbox: feed-recipe advice opens AI feeding (%s)" % ui.get_active_tab())
	inbox._on_item_action("open_intervention_detail", "preemptive-delouse", "")
	await get_tree().process_frame
	_check(ui.get_active_tab() == "pens", "inbox: delousing advice opens the pens (%s)" % ui.get_active_tab())
	scene.queue_free()
	await get_tree().process_frame

## U1-03: every tab of a brand-new Første sesong and of PO3 on day 12 (red
## zone, young fish, live alerts) reads as Norwegian in nb.
func _test_text_reads_norwegian_in_young_and_red_zone_farms() -> void:
	var locale := TranslationServer.get_locale()
	TranslationServer.set_locale("no")
	for run in [[SCENARIO_ID, 91, 0], ["po3_red_sone", 20261012, 12]]:
		_check(_sim.start_qa_scenario(run[0], run[1]), "text: %s starts" % run[0])
		var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
		add_child(scene)
		for i in 5:
			await get_tree().process_frame
		_sim.set_paused(true)
		_sim.skip_tutorial()
		if int(run[2]) > 0:
			_sim.advance_days(int(run[2]))
		var ui: Control = scene.get_node("UILayer/GameUI")
		var center: Control = scene.get_node("UILayer/NotificationCenter")
		var found: PackedStringArray = []
		for tab in THEME_SCREENS_TABS:
			ui.show_tab(tab)
			center.hide_panel()
			await get_tree().create_timer(THEME_SETTLE_S).timeout
			for o in TextLint.offenders(get_tree().root, "nb"):
				found.append("%s: %s" % [tab, o])
		_check(found.is_empty(), "text: every tab of %s day %d reads as Norwegian %s" % [run[0], run[2], str(found.slice(0, 4))])
		scene.queue_free()
		await get_tree().process_frame
	TranslationServer.set_locale(locale)

## U1-08 (no dead verbs): in a fresh Første sesong every enabled button on
## every tab is pressed once. None may answer with a failure toast or a
## push_warning / push_error, and every disabled button says why.
func _test_every_button_answers() -> void:
	_clear_test_saves()
	_check(_sim.start_qa_scenario(SCENARIO_ID, 91), "buttons: fresh Første sesong starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	_sim.skip_tutorial()
	await get_tree().create_timer(THEME_SETTLE_S).timeout
	var ui: Control = scene.get_node("UILayer/GameUI")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	var toasts: Array = []
	var toast_node := get_node("/root/ToastNotification")
	var on_toast := func(message: String, level: String) -> void: toasts.append([level, message])
	toast_node.toast_shown.connect(on_toast)
	var log := _WarningLog.new()
	OS.add_logger(log)
	for tab in THEME_SCREENS_TABS:
		ui.show_tab(tab)
		center.hide_panel()
		await get_tree().create_timer(BUTTON_SETTLE_S).timeout
		var panel: Node = ui.find_child("Panel_" + tab, true, false)
		if panel == null:
			_check(false, "buttons: tab %s has a panel" % tab)
			continue
		if tab == "export":
			_point_export_at_test_dir(panel)
		var silent: Array = []
		for node in panel.find_children("*", "BaseButton", true, false):
			var b := node as BaseButton
			if b.is_visible_in_tree() and b.disabled and b.tooltip_text.strip_edges() == "":
				silent.append(_button_label(b))
		_check(silent.is_empty(), "buttons: every disabled button on %s says why %s" % [tab, str(silent)])
		var failures: Array = []
		var presses := 0
		for node in panel.find_children("*", "BaseButton", true, false):
			if not is_instance_valid(node) or node.is_queued_for_deletion():
				continue
			var b := node as BaseButton
			if not b.is_visible_in_tree() or b.disabled or BUTTONS_NOT_PRESSED.has(String(b.name)):
				continue
			var label := _button_label(b)
			log.take()
			toasts.clear()
			if b.toggle_mode:
				b.button_pressed = not b.button_pressed
			b.pressed.emit()
			presses += 1
			for i in 3:
				await get_tree().process_frame
			var bad := toasts.filter(func(t: Array) -> bool: return t[0] in ["WARNING", "CRITICAL"])
			var warned := log.take()
			if not bad.is_empty() or not warned.is_empty():
				failures.append("%s → %s %s" % [label, str(bad.map(func(t: Array) -> String: return str(t[1]))), str(warned)])
			_close_popups()
			if ui.get_active_tab() != tab:
				ui.show_tab(tab)
				center.hide_panel()
				await get_tree().process_frame
		_check(failures.is_empty(), "buttons: %d presses on %s, no failure toast or warning %s" % [presses, tab, str(failures.slice(0, 3))])
	OS.remove_logger(log)
	toast_node.toast_shown.disconnect(on_toast)
	scene.queue_free()
	await get_tree().process_frame
	_clear_test_saves()

## U1-04 (screen regions): at UI scale 80/100/125 % and three window sizes the
## HUD readouts, goal chip, HUD controls, tab rail, notification drawer, toast
## stack, tutorial card and camera hint never overlap, touch or leave the
## screen. Five queued toasts show as at most three disjoint cards in the
## bottom-right corner, clear of the speed, pause and inbox buttons; the open
## drawer moves them beside it. No tab label is cut: every tab on the rail
## sits whole inside it, and the rest are in the "Mer" menu.
const OverlapLint := preload("res://tests/OverlapLint.gd")
const REGION_WINDOW_SIZES := [Vector2i(1280, 720), Vector2i(1920, 1080), Vector2i(2560, 1440)]
const REGION_UI_SCALES := [0.8, 1.0, 1.25]

func _test_screen_regions_never_overlap() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "regions: a scenario with a goal starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	_sim.skip_tutorial()
	var win := get_window()
	var old_size := win.size
	var old_scale := win.content_scale_factor
	var toasts := get_node("/root/ToastNotification")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	var ui: Control = scene.get_node("UILayer/GameUI")
	var chip: Control = scene.get_node("UILayer/HUD/MainHBox/GoalChip")
	_check(chip.is_visible_in_tree() and chip.has_goal(), "regions: the goal chip sits in the HUD bar")
	_check(chip.tooltip_text.contains(str(_sim.get_scenario_goal().get("objective", "?"))),
		"regions: the chip's tooltip holds the full objective ('%s')" % chip.tooltip_text)
	var levels := ["INFO", "WARNING", "SUCCESS", "CRITICAL", "INFO"]
	for size: Vector2i in REGION_WINDOW_SIZES:
		for scale: float in REGION_UI_SCALES:
			win.size = size
			win.content_scale_factor = scale
			var where := "%dx%d at %d %%" % [size.x, size.y, int(round(scale * 100.0))]
			toasts.clear_all()
			center.hide_panel()
			await get_tree().create_timer(0.4).timeout
			for n in 5:
				toasts.show_toast("CI region toast %d" % n, levels[n], 30.0)
			await get_tree().create_timer(1.4).timeout  # slide-ins and the two make-room exits
			var screen := get_viewport().get_visible_rect()
			var found := OverlapLint.regions(scene)
			var bad := OverlapLint.overlaps(found, screen)
			_check(bad.is_empty(), "regions: nothing overlaps at %s %s" % [where, str(bad)])
			var rects: Array = toasts.get_toast_rects()
			var corner := screen.end - Vector2(toasts.STACK_MARGIN_RIGHT, toasts.STACK_MARGIN_BOTTOM)
			var newest: Rect2 = rects[0] if not rects.is_empty() else Rect2()
			_check(rects.size() == 3 and newest.end.distance_to(corner) < 2.0,
				"regions: five toasts show as three, newest in the bottom-right corner at %s (%d, %s)" % [where, rects.size(), str(newest)])
			var controls: Rect2 = found.get("hud_controls", Rect2())
			var clear := controls.has_area()
			for r: Rect2 in rects:
				clear = clear and not r.intersects(controls.grow(8.0), true)
			_check(clear, "regions: no toast comes near the speed, pause or inbox buttons at %s" % where)
			_check(_rail_tabs_whole(ui), "regions: every tab on the rail is whole, the rest in 'Mer', at %s %s" % [where, str(ui.get_overflow_tabs())])
			center.show_panel()
			await get_tree().create_timer(0.5).timeout
			found = OverlapLint.regions(scene)
			bad = OverlapLint.overlaps(found, screen)
			_check(found.has("notification_panel") and found.has("toast_0") and bad.is_empty(),
				"regions: the open drawer moves the toasts beside it at %s %s" % [where, str(bad)])
	win.content_scale_factor = old_scale
	win.size = old_size
	# The drawer is open but the game UI is hidden (fjord views, photo shots):
	# the stack goes back to the corner instead of making room for nothing.
	scene.get_node("UILayer").visible = false
	await get_tree().create_timer(0.3).timeout
	var corner_now := get_viewport().get_visible_rect().end - Vector2(toasts.STACK_MARGIN_RIGHT, toasts.STACK_MARGIN_BOTTOM)
	var shown: Array = toasts.get_toast_rects()
	_check(not shown.is_empty() and (shown[0] as Rect2).end.distance_to(corner_now) < 2.0,
		"regions: with the game UI hidden the toasts sit in the corner, not beside a hidden drawer (%s)" % str(shown.slice(0, 1)))
	scene.get_node("UILayer").visible = true
	center.hide_panel()
	toasts.clear_all()
	await get_tree().process_frame
	scene.queue_free()
	await get_tree().process_frame

## Every visible rail tab lies whole inside the rail at its full label width,
## and the tabs on the rail plus the "Mer" menu are all the tabs.
func _rail_tabs_whole(ui: Control) -> bool:
	var rail: Control = ui.find_child("TabBarScroll", true, false)
	var bar: Control = ui.find_child("TabBar", true, false)
	var view := rail.get_global_rect().grow(0.5)
	var shown := 0
	var total := 0
	for child in bar.get_children():
		if not String(child.name).begins_with("Tab_"):
			continue
		total += 1
		var b := child as Button
		if not b.visible:
			continue
		shown += 1
		if not view.encloses(b.get_global_rect()) or b.size.x + 0.5 < b.get_combined_minimum_size().x:
			return false
	var more: Control = bar.get_node_or_null("TabMore")
	var in_menu: int = ui.get_overflow_tabs().size()
	if in_menu > 0 and (more == null or not more.visible or not view.encloses(more.get_global_rect())):
		return false
	return shown + in_menu == total

## U1-04: a tab in the "Mer" menu opens from it with real input (click the
## menu, arrow down, Enter) and takes a whole place on the rail.
func _test_overflow_menu_opens_hidden_tabs() -> void:
	var scene := await _spawn_game_scene()
	_sim.skip_tutorial()
	var win := get_window()
	var old_scale := win.content_scale_factor
	win.content_scale_factor = 1.25
	await get_tree().create_timer(0.3).timeout
	var ui: Control = scene.get_node("UILayer/GameUI")
	var hidden: Array = ui.get_overflow_tabs()
	_check(not hidden.is_empty(), "overflow: at 125 %% some tabs move into 'Mer' (%s)" % str(hidden))
	if not hidden.is_empty():
		var more: MenuButton = ui.find_child("TabMore", true, false)
		await _click_control(more)
		_check(more.get_popup().visible, "overflow: clicking 'Mer' opens the menu")
		await _press_key(KEY_DOWN)
		await _press_key(KEY_ENTER)
		await get_tree().process_frame
		var opened := String(hidden[0])
		_check(ui.get_active_tab() == opened, "overflow: the menu opens %s (%s)" % [opened, ui.get_active_tab()])
		var tab: Control = ui.find_child("Tab_" + opened, true, false)
		_check(tab.visible and _rail_tabs_whole(ui), "overflow: the opened tab takes a whole place on the rail")
	win.content_scale_factor = old_scale
	await get_tree().process_frame
	scene.queue_free()
	await get_tree().process_frame

## U1-04/D15: no function keys (F1–F12 belong to macOS); Cmd/Ctrl+digit opens
## the core tabs, and a bare digit does not.
func _test_tab_shortcuts_are_command_digits() -> void:
	var scene := await _spawn_game_scene()
	_sim.skip_tutorial()
	await get_tree().process_frame
	var ui: Control = scene.get_node("UILayer/GameUI")
	get_viewport().gui_release_focus()
	for f in [KEY_F1, KEY_F2, KEY_F3, KEY_F4, KEY_F10, KEY_F12]:
		await _press_key(f)
	_check(ui.get_active_tab() == "dashboard", "shortcuts: F-keys switch no tab (%s)" % ui.get_active_tab())
	for case in [[KEY_3, "finance"], [KEY_0, "analytics"], [KEY_2, "map"], [KEY_1, "dashboard"]]:
		await _press_command_key(case[0])
		_check(ui.get_active_tab() == case[1], "shortcuts: Cmd/Ctrl+%s opens %s (%s)" % [OS.get_keycode_string(case[0]), case[1], ui.get_active_tab()])
	get_viewport().gui_release_focus()
	await _press_key(KEY_3, 51)
	_check(ui.get_active_tab() == "dashboard", "shortcuts: a bare 3 opens no tab (%s)" % ui.get_active_tab())
	var finance_tab: Control = ui.find_child("Tab_finance", true, false)
	_check(finance_tab.tooltip_text.contains("3"), "shortcuts: the tab names its shortcut ('%s')" % finance_tab.tooltip_text)
	scene.queue_free()
	await get_tree().process_frame

## U1-04: the dashboard's alert card shows one row per (category, site). A
## storm reported on three days (and the storm event behind it) is one row
## that counts its repeats; two sites with a disease are two rows.
func _test_dashboard_alerts_one_row_per_category_and_site() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	for day in 3:
		_sim._push_alert("warning", "CI: storm i fjorden dag %d" % day, "weather")
		_sim.advance_days(1)
	_sim._push_alert("warning", "CI: sykdom på A", "disease", "ci_site_a")
	_sim._push_alert("critical", "CI: sykdom på A igjen", "disease", "ci_site_a")
	_sim._push_alert("warning", "CI: sykdom på B", "disease", "ci_site_b")
	ui.show_tab("dashboard")
	var dash: Control = ui.find_child("Panel_dashboard", true, false)
	dash._refresh_all()
	await get_tree().process_frame
	var rows: Array = dash.get_alert_rows()
	var seen := {}
	var dupes: Array = []
	for r: Dictionary in rows:
		var key := "%s|%s" % [r["category"], r["site"]]
		if seen.has(key):
			dupes.append(key)
		seen[key] = r
	_check(dupes.is_empty() and not rows.is_empty(), "alerts: one row per category and site (%d rows, dupes %s)" % [rows.size(), str(dupes)])
	_check(seen.has("weather|") and int(seen["weather|"]["count"]) >= 3,
		"alerts: three storm reports are one counted row (%s)" % str(seen.get("weather|", {}).get("count", 0)))
	_check(seen.has("disease|ci_site_a") and seen.has("disease|ci_site_b") and int(seen["disease|ci_site_a"]["count"]) == 2,
		"alerts: a disease at each of two sites is two rows, the repeat counted")
	var drawn := {}
	var drawn_rows := 0
	for row in dash.find_children("*", "HBoxContainer", true, false):
		if row.has_meta("alert_category"):
			drawn_rows += 1
			drawn["%s|%s" % [row.get_meta("alert_category"), row.get_meta("alert_site")]] = true
	_check(drawn_rows == rows.size() and drawn.size() == rows.size(), "alerts: the card draws each row once (%d drawn)" % drawn_rows)
	scene.queue_free()
	await get_tree().process_frame

## U1-06 #1: Esc on the dashboard opens the pause menu and stops the clock;
## Esc again (or Fortsett) closes it and gives the clock back its state. While
## it is open the game's own keys (Space, Cmd/Ctrl+digit) wait, and the HUD
## shows the clock paused. Esc on another tab still goes home first.
func _test_escape_opens_the_pause_menu() -> void:
	var scene := await _spawn_game_scene()
	_sim.skip_tutorial()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var hud: Control = scene.get_node("UILayer/HUD")
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	ui.show_tab("finance")
	_sim.set_paused(false)
	get_viewport().gui_release_focus()
	await _press_key(KEY_ESCAPE)
	_check(ui.get_active_tab() == "dashboard" and not pause.is_open(), "pause: Esc on a tab goes home first, no menu")
	await _press_key(KEY_ESCAPE)
	_check(pause.is_open() and pause.visible, "pause: Esc on the dashboard opens the pause menu")
	_check(_sim.is_paused(), "pause: the open menu stops the clock")
	_check(hud._is_paused, "pause: the HUD shows the clock paused")
	var pauses: Array = []
	hud.speed_changed.connect(func(_speed: float, paused: bool) -> void: pauses.append(paused))
	await _press_key(KEY_SPACE, 32)
	await _press_command_key(KEY_3)
	_check(pauses.is_empty() and ui.get_active_tab() == "dashboard",
		"pause: Space and Cmd/Ctrl+3 wait while the menu is open (%s, %s)" % [str(pauses), ui.get_active_tab()])
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.3).timeout
	_check(not pause.is_open() and not pause.visible, "pause: Esc closes the menu")
	_check(not _sim.is_paused() and not hud._is_paused, "pause: closing gives the running clock back")
	_sim.set_paused(true)
	hud.set_paused_state(true)
	await _press_key(KEY_ESCAPE)
	await _click_control(pause.btn_resume)
	await get_tree().create_timer(0.3).timeout
	_check(not pause.is_open() and _sim.is_paused(), "pause: Fortsett keeps a clock the player had paused paused")
	scene.queue_free()
	await get_tree().process_frame

## U1-06 #2: Pause → Til hovedmenyen asks to save first, then reaches a
## usable main menu; the abandoned game's clock stays stopped.
func _test_pause_menu_reaches_the_main_menu() -> void:
	var scene := await _spawn_game_scene()
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	var target := [""]
	pause.scene_change_override = func(path: String) -> void: target[0] = path
	_sim.set_paused(false)
	get_viewport().gui_release_focus()
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.3).timeout
	await _click_control(pause.btn_main_menu)
	_check(pause.guard.visible and pause.guard_title.text == "Lagre før du går til hovedmenyen?",
		"pause: Til hovedmenyen asks to save first ('%s')" % pause.guard_title.text)
	await _click_control(pause.guard_discard)
	_check(target[0] == "res://scenes/MainMenu.tscn", "pause: 'Gå uten å lagre' goes to the main menu (%s)" % target[0])
	_check(_sim.is_paused(), "pause: the game left behind does not tick")
	var menu: Control = (load(target[0]) as PackedScene).instantiate()
	add_child(menu)
	await get_tree().process_frame
	var usable: bool = menu.center.visible
	for btn: Button in [menu.btn_new_game, menu.btn_load, menu.btn_settings, menu.btn_credits, menu.btn_quit]:
		usable = usable and btn.is_visible_in_tree() and not btn.disabled
	_check(usable, "pause: the main menu it reaches is usable")
	menu.queue_free()
	scene.queue_free()
	await get_tree().process_frame

## U1-06 #3: Pause → Innstillinger → back (Esc, and the close button) returns
## to the open pause menu with focus on Innstillinger.
func _test_pause_settings_and_back() -> void:
	var scene := await _spawn_game_scene()
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	get_viewport().gui_release_focus()
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.3).timeout
	await _click_control(pause.btn_settings)
	_check(pause.settings_menu.visible and not pause.get_node("%Center").visible,
		"pause: Innstillinger opens the settings in place of the menu")
	await _press_key(KEY_ESCAPE)
	await get_tree().process_frame
	_check(not pause.settings_menu.visible and pause.get_node("%Center").visible and pause.is_open(),
		"pause: Esc in the settings returns to the pause menu")
	await get_tree().process_frame
	_check(get_viewport().gui_get_focus_owner() == pause.btn_settings, "pause: focus returns to Innstillinger")
	await _click_control(pause.btn_settings)
	await _click_control(pause.settings_menu.get_node("%CloseButton"))
	_check(not pause.settings_menu.visible and pause.get_node("%Center").visible and pause.is_open(),
		"pause: the settings' close button also returns to the pause menu")
	pause.hide_menu()
	scene.queue_free()
	await get_tree().process_frame

## U1-06 #4: the window's close request asks "Lagre før du avslutter?" over
## the pause menu instead of quitting; Avbryt goes back to the menu; "Lagre og
## avslutt" writes the quick-save slot, then quits.
func _test_window_close_asks_to_save() -> void:
	_clear_test_saves()
	var scene := await _spawn_game_scene()
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	var quits := [0]
	pause.quit_override = func() -> void: quits[0] += 1
	_check(not get_tree().auto_accept_quit, "quit: the game screen takes the window's close request")
	get_tree().root.propagate_notification(NOTIFICATION_WM_CLOSE_REQUEST)
	await get_tree().create_timer(0.3).timeout
	_check(pause.is_open() and pause.guard.visible and pause.guard_title.text == "Lagre før du avslutter?",
		"quit: closing the window asks 'Lagre før du avslutter?' ('%s')" % pause.guard_title.text)
	_check(quits[0] == 0, "quit: nothing quits before the player answers")
	await _click_control(pause.guard_cancel)
	_check(not pause.guard.visible and pause.is_open(), "quit: Avbryt returns to the pause menu")
	await _click_control(pause.btn_quit)
	_check(pause.guard.visible, "quit: Avslutt spillet asks the same question")
	await _click_control(pause.guard_save)
	_check(quits[0] == 1, "quit: 'Lagre og avslutt' quits once")
	var saved := false
	for entry in _sim.get_save_list():
		saved = saved or str(entry.get("name", "")).begins_with("quicksave")
	_check(saved, "quit: 'Lagre og avslutt' wrote the quick-save slot first")
	scene.queue_free()
	await get_tree().process_frame
	_check(get_tree().auto_accept_quit, "quit: leaving the game screen gives the close button back")
	_clear_test_saves()

## U1-06 #5: Fortsett on the main menu loads the newest save: the day and the
## balance come back exactly. With no save there is no Fortsett and Nytt spill
## is the primary action.
func _test_continue_restores_the_latest_save() -> void:
	_clear_test_saves()
	var empty := await _spawn_main_menu()
	_check(not empty.btn_continue.visible and empty.btn_new_game.theme_type_variation == &"LargePrimaryButton",
		"continue: no save, no Fortsett; Nytt spill is the primary action")
	empty.queue_free()
	# A farm with fish and costs, so the balance moves from day to day.
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "setup: a farm whose balance moves")
	_sim.set_paused(true)
	_sim.advance_days(4)
	var older_balance: float = float(_sim.balance)
	_check(_sim.save_game("ci_continue_older"), "setup: an older save")
	await get_tree().create_timer(0.05).timeout
	_sim.advance_days(9)
	var day: int = _sim.current_day
	var balance: float = float(_sim.balance)
	_check(not is_equal_approx(balance, older_balance), "setup: the balance moved between the saves (%s → %s)" % [str(older_balance), str(balance)])
	_check(_sim.save_game("ci_continue_newest"), "setup: the newest save")
	_start_paused_game()
	_sim.advance_days(2)
	_check(_sim.current_day != day and not is_equal_approx(float(_sim.balance), balance), "setup: a different game is running")
	var menu := await _spawn_main_menu()
	var entered := [false]
	menu.enter_game_override = func() -> void: entered[0] = true
	_check(menu.btn_continue.visible and menu.btn_continue.theme_type_variation == &"LargePrimaryButton",
		"continue: a save exists, Fortsett is the primary action")
	_check(menu.continue_caption.text.contains(str(day)), "continue: the caption names the save's day ('%s')" % menu.continue_caption.text)
	await _click_control(menu.btn_continue)
	_check(entered[0], "continue: Fortsett enters the game")
	_check(_sim.current_day == day and is_equal_approx(float(_sim.balance), balance),
		"continue: day %d and balance %s restored exactly (%d, %s)" % [day, str(balance), _sim.current_day, str(_sim.balance)])
	menu.queue_free()
	await get_tree().process_frame
	_clear_test_saves()

## U1-06 #6: Medvirkende, from the main menu and from the pause menu, lists
## every work in THIRD_PARTY.md with its licence; Esc closes it back to the
## menu it came from. The page is themed and readable.
func _test_credits_list_every_third_party_work() -> void:
	var inventory := _third_party_inventory()
	_check(inventory.size() >= 7, "credits: THIRD_PARTY.md lists the shipped works (%d)" % inventory.size())
	var menu := await _spawn_main_menu()
	await _click_control(menu.btn_credits)
	await get_tree().create_timer(0.5).timeout
	var credits: Control = menu.credits_screen
	_check(credits != null and credits.visible and not menu.center.visible, "credits: Medvirkende opens from the main menu")
	if credits != null:
		_check_credits_rows(credits, inventory, "main menu")
		var lint := preload("res://tests/ThemeLint.gd")
		var unthemed: PackedStringArray = lint.unthemed(credits)
		var low: PackedStringArray = lint.low_contrast(credits)
		_check(unthemed.is_empty() and low.is_empty(), "credits: themed and readable %s %s" % [str(unthemed.slice(0, 3)), str(low.slice(0, 3))])
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.4).timeout
	_check(credits != null and not credits.visible and menu.center.visible, "credits: Esc returns to the main menu")
	menu.queue_free()
	await get_tree().process_frame

	var scene := await _spawn_game_scene()
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	get_viewport().gui_release_focus()
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.3).timeout
	await _click_control(pause.btn_credits)
	await get_tree().create_timer(0.5).timeout
	var from_pause: Control = pause.credits_screen
	_check(from_pause != null and from_pause.visible, "credits: Medvirkende opens from the pause menu")
	if from_pause != null:
		_check_credits_rows(from_pause, inventory, "pause menu")
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.4).timeout
	_check(pause.is_open() and pause.get_node("%Center").visible, "credits: Esc returns to the pause menu")
	pause.hide_menu()
	scene.queue_free()
	await get_tree().process_frame

## Every inventory row has one drawn row with its name and licence.
func _check_credits_rows(credits: Control, inventory: Dictionary, where: String) -> void:
	var drawn := {}
	for row in credits.find_children("*", "PanelContainer", true, false):
		if not row.has_meta("credit_name"):
			continue
		var name_label: Label = row.find_child("WorkName", true, false)
		var licence_label: Label = row.find_child("Licence", true, false)
		drawn[str(row.get_meta("credit_name"))] = {"licence": str(row.get_meta("credit_licence")),
			"shown": name_label != null and name_label.is_visible_in_tree() and name_label.text != ""
				and licence_label != null and licence_label.text != ""}
	var missing: Array = []
	for component in inventory:
		var row: Dictionary = drawn.get(component, {})
		if row.is_empty() or row["licence"] != inventory[component] or not row["shown"]:
			missing.append(component)
	_check(missing.is_empty() and drawn.size() == inventory.size(),
		"credits: every THIRD_PARTY.md work and licence is listed from the %s %s" % [where, str(missing)])

## Component -> licence from the THIRD_PARTY.md table at the repository root.
func _third_party_inventory() -> Dictionary:
	var path := ProjectSettings.globalize_path("res://").path_join("../THIRD_PARTY.md").simplify_path()
	var text := FileAccess.get_file_as_string(path)
	var rows := {}
	var header: PackedStringArray = []
	for line in text.split("\n"):
		if not line.begins_with("|"):
			continue
		var cells: PackedStringArray = []
		for cell in line.strip_edges().trim_prefix("|").trim_suffix("|").split("|"):
			cells.append(cell.strip_edges())
		if header.is_empty():
			header = cells
			continue
		if cells[0].replace("-", "").replace(":", "") == "":
			continue
		rows[cells[header.find("Component")]] = cells[header.find("Licence")]
	return rows

## The save list behind the pause menu's Lagre spill: every enabled button
## answers without a failure toast or a warning, every disabled one says why
## (the coverage the removed "Lagre/last inn" tab had in _test_every_button_answers).
func _test_pause_save_list_buttons_answer() -> void:
	_clear_test_saves()
	_check(_sim.start_qa_scenario(SCENARIO_ID, 91), "buttons: fresh Første sesong starts (pause save list)")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	_sim.skip_tutorial()
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	pause.show_menu()
	pause.btn_save.pressed.emit()
	await get_tree().create_timer(BUTTON_SETTLE_S).timeout
	var list: Control = pause.save_load_menu
	var toasts: Array = []
	var toast_node := get_node("/root/ToastNotification")
	var on_toast := func(message: String, level: String) -> void: toasts.append([level, message])
	toast_node.toast_shown.connect(on_toast)
	var log := _WarningLog.new()
	OS.add_logger(log)
	var silent: Array = []
	for node in list.find_children("*", "BaseButton", true, false):
		var b := node as BaseButton
		if b.is_visible_in_tree() and b.disabled and b.tooltip_text.strip_edges() == "":
			silent.append(_button_label(b))
	_check(silent.is_empty(), "buttons: every disabled button on the pause save list says why %s" % str(silent))
	var failures: Array = []
	var presses := 0
	for node in list.find_children("*", "BaseButton", true, false):
		if not is_instance_valid(node) or node.is_queued_for_deletion():
			continue
		var b := node as BaseButton
		if not b.is_visible_in_tree() or b.disabled or b.name == "CloseButton":
			continue
		log.take()
		toasts.clear()
		b.pressed.emit()
		presses += 1
		for i in 3:
			await get_tree().process_frame
		var bad := toasts.filter(func(t: Array) -> bool: return t[0] in ["WARNING", "CRITICAL"])
		var warned := log.take()
		if not bad.is_empty() or not warned.is_empty():
			failures.append("%s → %s %s" % [_button_label(b), str(bad), str(warned)])
		if not list.visible:
			pause.btn_save.pressed.emit()
			await get_tree().process_frame
	_check(presses > 0 and failures.is_empty(), "buttons: %d presses on the pause save list, no failure toast or warning %s" % [presses, str(failures.slice(0, 3))])
	OS.remove_logger(log)
	toast_node.toast_shown.disconnect(on_toast)
	pause.hide_menu()
	scene.queue_free()
	await get_tree().process_frame
	_clear_test_saves()

## U1-07 #5 (review F692): on a fresh launch — no game started yet in this
## process, so the engine has no roster — a mod's scenario that the picker
## lists starts as itself when it is clicked and started, instead of the
## default game. Runs first in _run(). The mod is the repo's example pack,
## copied into this checkout's test folder (never the player's user://mods).
const MOD_SCENARIO_ID := "scenario_fjord_pioneer"
const EXAMPLE_MOD := "res://mods/example_norwegian_pack"

func _test_mod_scenario_starts_on_a_fresh_launch() -> void:
	var bridge := get_node("/root/GameBridge")
	_check(not _sim.is_game_running(), "mods: a fresh launch, no game started yet")
	var mods_dir := TEST_ROOT + "mods/"
	_copy_dir(EXAMPLE_MOD, mods_dir + EXAMPLE_MOD.get_file())
	var loaded: int = bridge.UseModsDirectory(ProjectSettings.globalize_path(mods_dir))
	_check(loaded == 1, "mods: the example pack loads from the test mods folder (%d)" % loaded)
	var menu := await _spawn_main_menu()
	var entered := [false]
	menu.enter_game_override = func() -> void: entered[0] = true
	await _click_control(menu.btn_new_game)
	await get_tree().create_timer(0.4).timeout
	var picker: Control = menu.scenario_panel
	var card: Button = picker.get_card(MOD_SCENARIO_ID) if picker != null else null
	_check(card != null, "mods: the picker lists the mod's scenario")
	if card != null:
		await _scroll_into_view(picker, card)
		await _click_control(card)
		_check(picker.get_selected_id() == MOD_SCENARIO_ID, "mods: a click selects the mod's card")
		await _click_control(picker.get_start_button())
	_check(entered[0], "mods: Start enters the game")
	_check(_sim.get_active_scenario_id() == MOD_SCENARIO_ID,
		"mods: the mod's scenario is the running game ('%s')" % _sim.get_active_scenario_id())
	_check(is_equal_approx(float(_sim.balance), 5_000_000.0), "mods: with the mod's starting capital (%s)" % str(_sim.balance))
	_sim.set_paused(true)
	menu.queue_free()
	await get_tree().process_frame
	bridge.UseModsDirectory("")
	_remove_dir(mods_dir)

## U1-07 #1–#4: the picker shows a card for every scenario in the roster (the
## nine built-in plus mods), each with its capital formatted from the data; a
## padlock shows only on a locked card (none are locked until U2-03); at
## 1280 and 1920 px windows and UI scale 100/125 % every card lies whole
## inside the panel once scrolled to; a click selects a card into the detail
## pane; a real Esc returns to a usable menu.
const PICKER_WINDOWS := [Vector2i(1280, 720), Vector2i(1920, 1080)]
const PICKER_SCALES := [1.0, 1.25]

func _test_scenario_picker() -> void:
	var menu := await _spawn_main_menu()
	await _click_control(menu.btn_new_game)
	await get_tree().create_timer(0.4).timeout
	var picker: Control = menu.scenario_panel
	var roster: Array = _sim.get_available_scenarios()
	var entries: Array = picker.get_scenarios()
	_check(entries.size() == roster.size() and entries.size() >= 9,
		"picker: a card for every scenario in the roster (%d cards, %d in the roster)" % [entries.size(), roster.size()])
	var wrong_capital: Array = []
	var wrong_lock: Array = []
	var wrong_mod: Array = []
	var locked := 0
	for e: Dictionary in entries:
		var id := str(e["id"])
		var card: Button = picker.get_card(id)
		var capital: Label = card.find_child("Capital", true, false) if card != null else null
		var want := SimFormatters.format_nok(float(e.get("starting_capital", 0.0)))
		if capital == null or capital.text != want:
			wrong_capital.append("%s: '%s' ≠ '%s'" % [id, capital.text if capital else "?", want])
		var is_locked := not bool(e.get("unlocked", true))
		locked += 1 if is_locked else 0
		if card == null or (card.find_child("LockTag", true, false) != null) != is_locked:
			wrong_lock.append(id)
		if card == null or (card.find_child("ModTag", true, false) != null) != bool(e.get("is_mod", false)):
			wrong_mod.append(id)
	_check(wrong_capital.is_empty(), "picker: every card shows its starting balance formatted %s" % str(wrong_capital))
	var first: Button = picker.get_card("forste_sesong")
	var first_capital: Label = first.find_child("Capital", true, false) if first != null else null
	_check(first_capital != null and first_capital.text == SimFormatters.format_nok(3_000_000.0),
		"picker: Familiegården starts with 3 000 000 (it read 300 000)")
	_check(wrong_lock.is_empty(), "picker: a padlock only on a locked card %s" % str(wrong_lock))
	_check(locked == 0, "picker: no scenario is locked until progression lands (%d locked)" % locked)
	_check(wrong_mod.is_empty(), "picker: exactly the mods' scenarios carry the Mod tag %s" % str(wrong_mod))
	# The roadmap's text faults: the goal is equity (ressursrente showed 85 MNOK
	# "overskudd" for a 25 MNOK goal) and Lakskongen starts in Vestland.
	for e: Dictionary in entries:
		if str(e["id"]) == "ressursrente":
			_check(str(e["win"]).contains("egenkapital") and str(e["win"]).contains("85 MNOK"),
				"picker: the ressursrente goal names its equity target ('%s')" % str(e["win"]))
		if str(e["id"]) == "barekraft_mester":
			_check(not str(e["description"]).contains("hvor som helst") and str(e["description"]).contains("Vestland"),
				"picker: Lakskongen no longer promises a free choice of region")

	var win := get_window()
	var old_size := win.size
	var old_scale := win.content_scale_factor
	for size: Vector2i in PICKER_WINDOWS:
		for scale: float in PICKER_SCALES:
			win.size = size
			win.content_scale_factor = scale
			await get_tree().create_timer(0.3).timeout
			var where := "%dx%d at %d %%" % [size.x, size.y, int(round(scale * 100.0))]
			var screen := get_viewport().get_visible_rect()
			var view: Rect2 = picker.find_child("CardScroll", true, false).get_global_rect().grow(0.5)
			var outside: Array = []
			for e: Dictionary in entries:
				var card: Button = picker.get_card(str(e["id"]))
				await _scroll_into_view(picker, card)
				var r := card.get_global_rect()
				if not view.encloses(r) or not picker.get_global_rect().encloses(r):
					outside.append(str(e["id"]))
			_check(outside.is_empty(), "picker: every card lies inside the panel once scrolled to at %s %s" % [where, str(outside)])
			var detail: Control = picker.find_child("Detail", true, false)
			_check(screen.encloses(detail.get_global_rect()) and screen.encloses(picker.get_start_button().get_global_rect()),
				"picker: the detail pane and Start fit the screen at %s" % where)
	win.content_scale_factor = old_scale
	win.size = old_size
	await get_tree().process_frame

	var other: Button = picker.get_card("lofoten_eventyr")
	await _scroll_into_view(picker, other)
	await _click_control(other)
	var shown: Label = null
	for lbl: Label in picker.find_child("Detail", true, false).find_children("*", "Label", true, false):
		if lbl.theme_type_variation == &"Title":
			shown = lbl
	var want_name := ""
	for e: Dictionary in entries:
		if str(e["id"]) == "lofoten_eventyr":
			want_name = str(e["name"])
	_check(picker.get_selected_id() == "lofoten_eventyr" and shown != null and shown.text == want_name,
		"picker: a click on a card shows it in the detail pane ('%s')" % (shown.text if shown else "?"))

	await _press_key(KEY_ESCAPE)
	await get_tree().process_frame
	var usable: bool = menu.center.visible and (menu.scenario_panel == null)
	for btn: Button in [menu.btn_new_game, menu.btn_load, menu.btn_settings, menu.btn_credits, menu.btn_quit]:
		usable = usable and btn.is_visible_in_tree() and not btn.disabled
	_check(usable, "picker: a real Esc returns to a usable menu")
	await get_tree().process_frame
	var focus := get_viewport().gui_get_focus_owner()
	_check(focus == menu.btn_new_game or focus == menu.btn_continue, "picker: focus is back on the menu's primary action")
	menu.queue_free()
	await get_tree().process_frame

## Scroll the picker's card grid so a card is on screen.
func _scroll_into_view(picker: Control, card: Control) -> void:
	var scroll: ScrollContainer = picker.find_child("CardScroll", true, false)
	if scroll != null and card != null:
		scroll.ensure_control_visible(card)
	await get_tree().process_frame
	await get_tree().process_frame

func _copy_dir(from: String, to: String) -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(to))
	for file in DirAccess.get_files_at(from):
		var data := FileAccess.get_file_as_bytes(from.path_join(file))
		var out := FileAccess.open(to.path_join(file), FileAccess.WRITE)
		if out != null:
			out.store_buffer(data)
			out.close()

func _remove_dir(path: String) -> void:
	var dir := DirAccess.open(path)
	if dir == null:
		return
	for sub in dir.get_directories():
		_remove_dir(path.path_join(sub))
	for file in dir.get_files():
		dir.remove(file)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(path))

func _point_export_at_test_dir(panel: Node) -> void:
	var dir := ProjectSettings.globalize_path(TEST_ROOT + "exports")
	DirAccess.make_dir_recursive_absolute(dir)
	var export_panel := _node_with(panel, "_export_directory")
	if export_panel != null:
		export_panel._path_display.text = dir
		export_panel._export_directory = dir

func _button_label(b: BaseButton) -> String:
	var text := str(b.call("get_text")) if b.has_method("get_text") else ""
	return "%s '%s'" % [b.name, text.replace("\n", " ").left(40)]

func _close_popups() -> void:
	for w in get_tree().root.find_children("*", "Window", true, false):
		if (w is AcceptDialog or w is PopupMenu) and (w as Window).visible:
			(w as Window).hide()

## The node at or under `root` whose script declares `property`.
func _node_with(root: Node, property: String) -> Node:
	if root == null:
		return null
	if property in root:
		return root
	for n in root.find_children("*", "", true, false):
		if property in n:
			return n
	return null

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

## A real Cmd+key (macOS) / Ctrl+key (elsewhere) press and release.
func _press_command_key(keycode: Key) -> void:
	for pressed in [true, false]:
		var ev := InputEventKey.new()
		ev.keycode = keycode
		ev.physical_keycode = keycode
		ev.command_or_control_autoremap = true
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
	# G1-07 replaced the grey 60% override: a rival cage is the player's pen
	# asset with its owner's colour band, a quieter net and no health ring,
	# and every material is the shared stock one (the old check only asked
	# for "grey"; this one pins the asset, the band and the net alpha).
	var banded := not rivals.farm_ids().is_empty()
	var quiet_nets := not rivals.farm_ids().is_empty()
	var stock_materials := not rivals.farm_ids().is_empty()
	for id in rivals.farm_ids():
		var band: Color = CompetitorFarmVisualManager.band_color_for(id)
		for cage in rivals._competitor_farms[id].cages:
			var pen := cage as PenCage
			banded = banded and pen != null and pen.get_owner_band() == band \
				and pen.get_node("Band").visible \
				and pen.get_node("Band").get_instance_shader_parameter("band_color") == band \
				and not pen.get_health_ring().visible
			quiet_nets = quiet_nets and pen != null and not pen.get_net_nodes().is_empty()
			for net in (pen.get_net_nodes() if pen != null else []):
				quiet_nets = quiet_nets and float(net.get_instance_shader_parameter("net_opacity")) <= 0.55
			for mesh in (cage as Node).find_children("*", "MeshInstance3D", true, false):
				var mat := (mesh as MeshInstance3D).material_override
				stock_materials = stock_materials and (mat == null or not mat.resource_path.is_empty())
	_check(banded, "rival farms: every rival cage carries its owner's colour band and no health ring")
	_check(quiet_nets, "rival farms: every rival net is drawn at alpha <= 0.55")
	_check(stock_materials, "rival farms: no rival mesh gets a one-off material override")
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
	var barges_chipped := not barge_sites.is_empty()
	for id in barge_sites:
		barges_afloat = barges_afloat and _route_on_water(barges._active_barges[id].path, terrain)
		# A3-01 replaced the yellow deck tint: the barge is the feed-barge
		# asset, its state shows on a chip, and no mesh gets a one-off paint.
		var barge := barges.barge_of(id) as VesselModel
		var chip: VesselChip = barges._active_barges[id].chip
		barges_chipped = barges_chipped and barge != null and barge.asset_id.begins_with("feed_barge") \
			and chip != null and chip.get_text().begins_with("Fôrflåte") \
			and chip.get_accent() in [FeedBargeVisualManager.FEED_YELLOW, FeedBargeVisualManager.IDLE_GREY] \
			and _no_material_overrides(barge)
	_check(barges_afloat, "feed barge: every route point is at sea level on open water")
	_check(barges_chipped, "feed barge: the barge asset shows its state on a chip, its paint stays the shared material")

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
	# A3-01: job state is a UI chip, never hull colour (this replaced the
	# deck-tint checks: the chip names the job and carries its colour).
	var wb_chip := boats.job_chip(job_id)
	_check(wb_chip != null and _no_material_overrides(boats._active_wellboats[job_id].boat),
		"wellboat: the job shows on a chip; no mesh of the boat gets a one-off material")
	_check(wb_chip != null and wb_chip.get_text().begins_with("ROV-inspeksjon")
		and wb_chip.get_accent() == WellboatVisualManager.JOB_COLOURS["other"],
		"wellboat: an ROV boat's chip reads '%s' with the info-blue dot" % (wb_chip.get_text() if wb_chip else ""))
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

## G1-07: the placeholder pen and boat models read as equipment. The net has
## no lid, no unshaded debug sphere floats over the selection, the collar is
## black HDPE whatever the fish health, health shows on the ring on the
## water (healthy vs red-zone lice differ by CIE76 dE >= 20), the site label
## is a constant-size billboard and the boat hulls lie flat.
func _test_pen_models_read_as_equipment() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "pen models: seeded two-site scenario starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var site_id := str(_sim.get_all_pens()[0].get("id", "")) if not _sim.get_all_pens().is_empty() else ""
	var cage := pens.get_node_or_null("PenMarkers/" + site_id) as PenCage
	_check(cage != null, "pen models: site %s has a pen cage" % site_id)
	if cage == null:
		scene.queue_free()
		return

	_check(_net_lid_faces(cage) == 0, "pen models: the net has no lid (%d top faces)" % _net_lid_faces(cage))

	pens.select_pen(site_id)
	var spheres := 0
	for mesh in env.find_children("*", "MeshInstance3D", true, false):
		if (mesh as MeshInstance3D).mesh is SphereMesh:
			spheres += 1
	_check(spheres == 0, "pen models: no sphere mesh anywhere in the fjord (%d)" % spheres)
	var marker := pens.active_indicator
	_check(marker != null and marker.visible and not (marker.mesh is SphereMesh)
		and absf(marker.global_position.y - cage.global_position.y) < 1.0,
		"pen models: the selection marker is a ring on the water around the pen")

	var gb := get_node("/root/GameBridge")
	gb.SetQaPenLice(site_id, 0.05)
	_sim.state_updated.emit()
	await get_tree().process_frame
	var healthy := pens.get_pen_health_color(site_id)
	var collar_before := _albedo_of(cage.get_node("Collar") as MeshInstance3D)
	gb.SetQaPenLice(site_id, 0.6)
	_sim.state_updated.emit()
	await get_tree().process_frame
	var red := pens.get_pen_health_color(site_id)
	var ring := cage.get_health_ring()
	_check(healthy == FishPenManager.COLOR_HEALTHY and red == FishPenManager.COLOR_CRITICAL,
		"pen models: lice 0.05 -> healthy ring, 0.6 -> critical ring (%s, %s)" % [healthy, red])
	_check(ring != null and ring.visible and ring.get_instance_shader_parameter("ring_color") == red,
		"pen models: the health ring on the water shows the site state")
	_check(_delta_e76(healthy, red) >= 20.0, "pen models: healthy vs red ring dE %.1f >= 20" % _delta_e76(healthy, red))
	var collar_after := _albedo_of(cage.get_node("Collar") as MeshInstance3D)
	_check(collar_after == collar_before and _linear_luminance(collar_after) < 0.1,
		"pen models: the collar stays black HDPE (luminance %.3f) whatever the lice" % _linear_luminance(collar_after))

	var label := cage.get_node_or_null("Label") as Label3D
	_check(label != null and label.billboard == BaseMaterial3D.BILLBOARD_ENABLED and label.fixed_size
		and label.position.y > 3.0,
		"pen models: the site label is a constant-size billboard above the pen")

	# A3-01 retired the capsule service boat: the site's boat is the 15 x 10 m
	# workboat catamaran. Its twin hulls must still lie flat along the boat
	# (longer than wide, wider than tall), now at the real dimensions.
	var boat := pens.get_node_or_null("PenMarkers/%s_boat" % site_id) as VesselModel
	var flat := boat != null and boat.asset_id == "workboat"
	if flat:
		var hull := boat.get_node("Hull") as MeshInstance3D
		var box: AABB = hull.transform * hull.get_aabb()
		flat = box.size.z > box.size.x and box.size.x > box.size.y \
			and absf(box.size.z - 15.0) / 15.0 <= 0.05 and absf(box.size.x - 10.0) / 10.0 <= 0.05
	_check(flat, "pen models: the site's workboat hulls lie flat along the boat, 15 x 10 m")
	scene.queue_free()
	await get_tree().process_frame

## A2-01: the pen in the game is the Blender hero asset with hand-made LODs
## inside their triangle budgets and at Polarcirkel 500 dimensions; a site's
## lice skirt shows on its pen; every site lies in a frame mooring with grid
## buoys, bridles, anchor lines and four synchronised corner lights.
func _test_hero_pen_and_site_kit() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "hero pen: seeded two-site scenario starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var site_id := str(_sim.get_all_pens()[0].get("id", "")) if not _sim.get_all_pens().is_empty() else ""
	var cage := pens.get_node_or_null("PenMarkers/" + site_id) as PenCage
	_check(cage != null, "hero pen: site %s has a pen" % site_id)
	if cage == null:
		scene.queue_free()
		return

	var lod_tris: Array[int] = []
	for level in 3:
		var tris := 0
		for mesh in cage.get_lod_meshes(level):
			tris += _mesh_triangles(mesh.mesh)
		lod_tris.append(tris)
	_check(lod_tris[0] > 30000 and lod_tris[0] <= 60000 and lod_tris[2] > 0 and lod_tris[2] <= 2000,
		"hero pen: LOD0 %d tris <= 60k, LOD2 %d tris <= 2k" % [lod_tris[0], lod_tris[2]])
	var lods_chain := true
	for level in [1, 2]:
		for mesh in cage.get_lod_meshes(level):
			lods_chain = lods_chain and is_equal_approx(mesh.visibility_range_begin, PenCage.LOD_SWITCH[level - 1])
	for mesh in cage.get_lod_meshes(0):
		lods_chain = lods_chain and is_equal_approx(mesh.visibility_range_end, PenCage.LOD_SWITCH[0])
	_check(lods_chain, "hero pen: LOD0 -> LOD1 -> LOD2 hand over by camera distance")
	var collar := (cage.get_node("Collar") as MeshInstance3D).get_aabb()
	var net := (cage.get_node("Net") as MeshInstance3D).get_aabb()
	_check(absf(collar.size.x - 53.63) / 53.63 <= 0.05 and absf(collar.size.z - 53.63) / 53.63 <= 0.05,
		"hero pen: collar %.2f m across, within 5%% of the 160 m Polarcirkel collar" % collar.size.x)
	_check(-net.position.y >= 20.0 and -net.position.y <= 40.0 * 1.05,
		"hero pen: the net hangs %.1f m deep (20-40 m with a cone bottom)" % -net.position.y)

	_check(not cage.has_lice_skirt() and cage.get_parts("LiceSkirt").all(func(m: MeshInstance3D) -> bool: return not m.visible),
		"hero pen: no lice skirt before the site buys one")
	_check(_sim.toggle_mitigation(site_id, "skirt"), "hero pen: the site buys a lice skirt")
	_sim.state_updated.emit()
	await get_tree().process_frame
	_check(cage.has_lice_skirt() and cage.get_parts("LiceSkirt").size() == 3
		and cage.get_parts("LiceSkirt").all(func(m: MeshInstance3D) -> bool: return m.visible),
		"hero pen: the skirt variant shows on every LOD of the pen")

	var grid := pens.get_pen_mooring(site_id)
	_check(grid != null and grid.cells_x == 1 and grid.cells_z == 1
		and MooringGrid.CELL_M >= 70.0 and MooringGrid.CELL_M <= 100.0,
		"site kit: the pen lies in one 70-100 m frame mooring cell")
	if grid != null:
		_check(grid.buoy_positions.size() == 4 and grid.ropes_of("bridle").size() == 8
			and grid.ropes_of("anchor").size() == 8 and grid.ropes_of("frame").size() == 4,
			"site kit: 4 grid buoys, 8 bridles, 8 anchor lines, 4 frame lines")
		var buoys_drawn := not grid.get_buoy_instances().is_empty()
		for mmi in grid.get_buoy_instances():
			buoys_drawn = buoys_drawn and mmi.multimesh.instance_count == 4
		_check(buoys_drawn and grid.get_buoy_instances().size() == 3, "site kit: buoys drawn at all three LODs")
		var lamps_ok := grid.light_positions.size() == 4 and grid.get_light_instances().size() == 3
		for mmi in grid.get_light_instances():
			var lamp := _surface_material(mmi.multimesh.mesh, "mooring_lamp_material")
			lamps_ok = lamps_ok and lamp != null \
				and (lamp.get_shader_parameter("lamp_color") as Color).is_equal_approx(Color("#FFC23A"))
		_check(lamps_ok, "site kit: four corner lights, all on the one #FFC23A flashing lamp material")

	var rivals: CompetitorFarmVisualManager = env.get_node("CompetitorFarmVisualManager")
	_sim.state_updated.emit()
	await get_tree().process_frame
	var rival_moorings := not rivals.farm_ids().is_empty()
	for id in rivals.farm_ids():
		var farm: Dictionary = rivals._competitor_farms[id]
		var n: int = farm.cages.size()
		var rg: MooringGrid = farm.get("mooring")
		rival_moorings = rival_moorings and rg != null and rg.cells_z == n \
			and rg.buoy_positions.size() == 2 * (n + 1) and rg.ropes_of("bridle").size() == 8 * n
	_check(rival_moorings, "site kit: every rival farm lies in its own frame mooring, one cell per pen")
	scene.queue_free()
	await get_tree().process_frame

## A3-01: the fleet is the Blender vessel set at real dimensions (barges 37 x
## 11 m and 64.8 x 13 m, wellboats 77 x 17.8 m and 116 x 23 m, workboat
## 15 x 10 m) inside its triangle budgets with an LOD hand-over chain; the
## wellboat is longer than a pen is wide; a harvest sends the big carrier;
## a feed barge is towed out and moors with its feed lines towards its pen;
## job state is a chip, never paint.
func _test_vessel_fleet() -> void:
	# Hull length/beam spec and the LOD0/LOD1/LOD2 triangle ceilings
	# (blender/asset_manifest.json tri_max).
	var fleet := {
		"workboat/workboat": [15.0, 10.0, [20000, 5000, 1000]],
		"feed_barge/feed_barge": [37.0, 11.0, [30000, 8000, 1500]],
		"feed_barge/feed_barge_large": [64.8, 13.0, [40000, 10000, 2000]],
		"wellboat/wellboat": [77.0, 17.8, [45000, 12000, 2000]],
		"wellboat/wellboat_large": [116.0, 23.0, [60000, 15000, 2500]],
		"rib/rib": [8.5, 3.0, [6000, 1500, 400]],
	}
	var dims_ok := true
	var budgets_ok := true
	var chains_ok := true
	var report := []
	for path in fleet:
		var v := (load("res://assets/models/%s.tscn" % path) as PackedScene).instantiate() as VesselModel
		add_child(v)
		var spec: Array = fleet[path]
		var hull := (v.get_node("Hull") as MeshInstance3D).get_aabb()
		dims_ok = dims_ok and absf(hull.size.z - spec[0]) / spec[0] <= 0.05 and absf(hull.size.x - spec[1]) / spec[1] <= 0.05
		var tris: Array[int] = []
		for level in 3:
			var t := 0
			for mesh in v.get_lod_meshes(level):
				t += _mesh_triangles(mesh.mesh)
			tris.append(t)
			budgets_ok = budgets_ok and t > 0 and t <= int(spec[2][level])
		budgets_ok = budgets_ok and tris[0] > tris[1] and tris[1] > tris[2]
		for mesh in v.get_lod_meshes(0):
			chains_ok = chains_ok and is_equal_approx(mesh.visibility_range_end, v.lod_switch.x)
		for mesh in v.get_lod_meshes(1):
			chains_ok = chains_ok and is_equal_approx(mesh.visibility_range_begin, v.lod_switch.x) \
				and is_equal_approx(mesh.visibility_range_end, v.lod_switch.y)
		for mesh in v.get_lod_meshes(2):
			chains_ok = chains_ok and is_equal_approx(mesh.visibility_range_begin, v.lod_switch.y) \
				and is_equal_approx(mesh.visibility_range_end, v.max_distance) \
				and mesh.cast_shadow == GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		report.append("%s %.1fx%.1f %s" % [path.get_file(), hull.size.z, hull.size.x, tris])
		v.free()
	_check(dims_ok, "vessels: hull length and beam within 5%% of spec (%s)" % ", ".join(report))
	_check(budgets_ok, "vessels: every LOD inside its triangle ceiling, LOD0 > LOD1 > LOD2")
	_check(chains_ok, "vessels: LOD0 -> LOD1 -> LOD2 hand over by camera distance, LOD2 casts no shadow")

	_check(_sim.start_qa_scenario("po3_red_sone", 91), "vessels: seeded two-site scenario starts")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var boats: WellboatVisualManager = env.get_node("WellboatVisualManager")
	var barges: FeedBargeVisualManager = env.get_node("FeedBargeVisualManager")
	boats.sail_seconds = 0.2
	barges.tow_seconds = 0.2
	_sim.state_updated.emit()
	await get_tree().process_frame
	var on_station := not barges.active_site_ids().is_empty()
	var states := []
	for id in barges.active_site_ids():
		on_station = on_station and barges.barge_state(id) == "moored"
		states.append("%s %s" % [id, barges.barge_state(id)])
	_check(on_station, "vessels: sites stocked when the game starts have their barge on station, no tow-in (%s)" % ", ".join(states))
	var sites: Array = _sim.get_all_pens()
	var site_id := str(sites[0].get("id", "")) if not sites.is_empty() else ""
	var cage := pens.get_node_or_null("PenMarkers/" + site_id) as PenCage
	_check(cage != null, "vessels: site %s has a pen" % site_id)
	if cage == null:
		scene.queue_free()
		return

	_check(_sim.request_wellboat(site_id, "harvest"), "vessels: a harvest wellboat is booked for %s" % site_id)
	_sim.state_updated.emit()
	await get_tree().create_timer(0.5).timeout
	var job_id := ""
	for id in boats.active_job_ids():
		if boats._active_wellboats[id].kind == "harvest":
			job_id = id
	var carrier := boats._active_wellboats[job_id].boat as VesselModel if job_id != "" else null
	_check(carrier != null and carrier.asset_id == "wellboat_large",
		"vessels: a harvest sends the 116 m carrier (%s)" % (carrier.asset_id if carrier else "none"))
	var collar := (cage.get_node("Collar") as MeshInstance3D).get_aabb()
	var wb := (load("res://assets/models/wellboat/wellboat.tscn") as PackedScene).instantiate() as VesselModel
	var wb_len := (wb.get_node("Hull") as MeshInstance3D).get_aabb().size.z
	wb.free()
	_check(wb_len > collar.size.x * 1.3,
		"vessels: the wellboat (%.1f m) is visibly longer than the pen is wide (%.1f m)" % [wb_len, collar.size.x])
	var chip := boats.job_chip(job_id) if job_id != "" else null
	var days_left := int(boats._active_wellboats[job_id].due_day) - int(_sim.current_day) if job_id != "" else -1
	_check(chip != null and chip.get_text() == WellboatVisualManager.chip_text("harvest", days_left, false)
		and chip.get_text().begins_with("Slakting") and chip.get_accent() == WellboatVisualManager.JOB_COLOURS["harvest"]
		and chip.get_size_px().x > 120.0,
		"vessels: the harvest chip reads '%s' with the ok-green dot" % (chip.get_text() if chip else ""))
	var carrier_mid := carrier.global_position if carrier else Vector3.ZERO
	var buoy_clear := carrier != null
	var grid := pens.get_pen_mooring(site_id)
	if grid != null and carrier != null:
		for b in grid.buoy_positions:
			var local := carrier.to_local(grid.to_global(b))
			buoy_clear = buoy_clear and (absf(local.x) > carrier.beam_m * 0.5 + 0.5 or absf(local.z) > carrier.length_m * 0.5 + 0.5)
	_check(buoy_clear, "vessels: the waiting carrier lies clear of the mooring buoys (at %s)" % carrier_mid)

	_check(barges.barge_state(site_id) == "moored", "vessels: the feed barge lies moored on station (%s)" % barges.barge_state(site_id))
	var barge := barges.barge_of(site_id) as VesselModel
	var feed_side_ok := barge != null
	if barge != null:
		var to_pen := cage.global_position - barge.global_position
		feed_side_ok = barge.global_transform.basis.x.dot(to_pen) > 0.0 and to_pen.length() < 80.0
	_check(feed_side_ok, "vessels: the moored barge lies beside its pen with its feed lines towards it")
	var tender := barge.get_node_or_null("Tender") as VesselModel if barge else null
	var tender_ok := tender != null and tender.asset_id == "rib"
	if tender_ok:
		var away := barge.to_local(cage.global_position).x * tender.position.x
		tender_ok = away < 0.0 and absf(tender.position.x) > barge.beam_m * 0.5 + tender.beam_m * 0.5
	_check(tender_ok, "vessels: the barge keeps its tender RIB alongside, on the side away from the pen")
	_check(_no_material_overrides(barge) and _no_material_overrides(carrier) and barge.get_node("Hull").get_active_material(0)
		== load("res://assets/models/vessel_kit/vessel_hull_slate_material.tres"),
		"vessels: barge and wellboat hulls keep the shared vessel paint, no per-boat material")
	barges._start_return(site_id)
	await get_tree().create_timer(0.5).timeout
	_check(barges.barge_state(site_id) == "" and not barges.active_site_ids().has(site_id),
		"vessels: a barge sent home leaves the fjord")
	_sim.state_updated.emit()
	await get_tree().process_frame
	var towing := barges.barge_state(site_id)
	await get_tree().create_timer(0.5).timeout
	_check(towing == "towing" and barges.barge_state(site_id) == "moored"
		and _route_on_water(barges._active_barges[site_id].path, (env.get_node("Terrain") as FjordTerrain)),
		"vessels: a site that needs a barge again sees it towed out along the channel and moored (%s -> %s)"
			% [towing, barges.barge_state(site_id)])
	await _check_salmon_school(pens, site_id)
	scene.queue_free()
	await get_tree().process_frame

## G3-01 (asset part): the pen's school draws the Blender Atlantic salmon in
## its countershaded skin, nose along -Z, ~300 triangles from the farm
## camera and ~1.5k close up; the swap keeps every fish where it was.
func _check_salmon_school(pens: FishPenManager, site_id: String) -> void:
	var school: MultiMeshInstance3D = pens._pens[site_id]["fish_school"] if pens._pens.has(site_id) else null
	var mesh: Mesh = school.multimesh.mesh if school else null
	var skin := load("res://assets/models/fish/salmon_skin_material.tres")
	_check(mesh != null and mesh.get_surface_count() == 1 and mesh.surface_get_material(0) == skin,
		"salmon: the school draws the Blender salmon in its one countershaded skin material")
	var box: AABB = mesh.get_aabb() if mesh else AABB()
	_check(absf(box.position.z + 0.452) < 0.01 and box.end.z > 0.45 and box.size.z > box.size.y * 2.5,
		"salmon: the nose leads along -Z, 0.9 m fork length (%.3f .. %.3f)" % [box.position.z, box.end.z])
	var cam := Camera3D.new()
	add_child(cam)
	cam.current = true
	cam.global_position = school.global_position + Vector3(0, 300, 300) if school else Vector3.ZERO
	await get_tree().process_frame
	var far_tris := _mesh_triangles(school.multimesh.mesh) if school else 0
	var before: Transform3D = school.multimesh.get_instance_transform(3) if school else Transform3D()
	var count_before: int = school.multimesh.visible_instance_count if school else -1
	pens.set_process(false)  # freeze the swim so the swap alone is measured
	cam.global_position = school.global_position + Vector3(0, -4, 12) if school else Vector3.ZERO
	if school:
		pens._update_fish_lod(school)
	var near_tris := _mesh_triangles(school.multimesh.mesh) if school else 0
	_check(far_tris >= 200 and far_tris <= 400 and near_tris >= 1200 and near_tris <= 1800,
		"salmon: %d triangles from the farm camera, %d close up (about 300 and 1.5k)" % [far_tris, near_tris])
	_check(school != null and school.multimesh.get_instance_transform(3).is_equal_approx(before)
		and school.multimesh.visible_instance_count == count_before,
		"salmon: the close-up swap keeps every fish where it was (%d fish)" % count_before)
	pens.set_process(true)
	cam.queue_free()
	await get_tree().process_frame

## True when no mesh under `root` has a material override (job state or
## owner colour never gets painted onto one boat).
func _no_material_overrides(root: Node) -> bool:
	if root == null:
		return false
	for mesh in root.find_children("*", "MeshInstance3D", true, false):
		if (mesh as MeshInstance3D).material_override != null and not ((mesh as MeshInstance3D).get_parent() is VesselChip):
			return false
	return true

## Triangles in a mesh (all surfaces).
func _mesh_triangles(mesh: Mesh) -> int:
	if mesh == null:
		return 0
	var tris := 0
	for s in mesh.get_surface_count():
		var count: int = mesh.surface_get_array_index_len(s)
		if count <= 0:
			count = mesh.surface_get_array_len(s)
		tris += count / 3
	return tris

## The surface material of a mesh whose resource path contains `name`.
func _surface_material(mesh: Mesh, name: String) -> ShaderMaterial:
	if mesh == null:
		return null
	for s in mesh.get_surface_count():
		var mat := mesh.surface_get_material(s)
		if mat is ShaderMaterial and name in mat.resource_path:
			return mat as ShaderMaterial
	return null

## Triangles of a pen's net meshes lying flat at the net's top edge (a lid).
func _net_lid_faces(cage: PenCage) -> int:
	var lid := 0
	for net in cage.get_net_nodes():
		var faces: PackedVector3Array = net.mesh.get_faces() if net.mesh else PackedVector3Array()
		var xf: Transform3D = cage.global_transform.affine_inverse() * net.global_transform
		var top := -INF
		for v in faces:
			top = maxf(top, (xf * v).y)
		for t in range(0, faces.size(), 3):
			if (xf * faces[t]).y > top - 0.01 and (xf * faces[t + 1]).y > top - 0.01 and (xf * faces[t + 2]).y > top - 0.01:
				lid += 1
	return lid

## The albedo a pen mesh renders with (shader parameter or StandardMaterial).
func _albedo_of(mesh: MeshInstance3D) -> Color:
	var mat: Material = mesh.material_override if mesh.material_override else mesh.get_active_material(0)
	if mat is ShaderMaterial:
		var c = (mat as ShaderMaterial).get_shader_parameter("albedo_color")
		return c if c is Color else Color(c.x, c.y, c.z) if c is Vector3 else Color.WHITE
	if mat is BaseMaterial3D:
		return (mat as BaseMaterial3D).albedo_color
	return Color.WHITE

## WCAG relative luminance of an sRGB colour (0 black .. 1 white).
func _linear_luminance(c: Color) -> float:
	var l := c.srgb_to_linear()
	return 0.2126 * l.r + 0.7152 * l.g + 0.0722 * l.b

## CIE76 colour difference between two sRGB colours (D65 white).
func _delta_e76(a: Color, b: Color) -> float:
	var la := _lab(a)
	var lb := _lab(b)
	return la.distance_to(lb)

func _lab(c: Color) -> Vector3:
	var l := c.srgb_to_linear()
	var x := (0.4124 * l.r + 0.3576 * l.g + 0.1805 * l.b) / 0.95047
	var y := 0.2126 * l.r + 0.7152 * l.g + 0.0722 * l.b
	var z := (0.0193 * l.r + 0.1192 * l.g + 0.9505 * l.b) / 1.08883
	var f := func(t: float) -> float: return pow(t, 1.0 / 3.0) if t > 0.008856 else 7.787 * t + 16.0 / 116.0
	var fx: float = f.call(x)
	var fy: float = f.call(y)
	var fz: float = f.call(z)
	return Vector3(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz))

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

## Golden heightfield hash of the game's fjord (seed 42, 512², 4 km): the
## same value TerrainGeneratorTests.GoldenHashSeed42 pins in xUnit, so the
## headless path, the windowed path (FjordTerrain logs it) and the .NET test
## process all build one heightfield.
const TERRAIN_GOLDEN_HASH := "5a84dfe1202f180d"

## G1-01: the fjord is one heightfield, wound so every triangle faces up,
## with a HeightMapShape3D collider, uploaded without a long main-thread frame.
func _test_terrain_faces_up_and_shares_one_heightfield() -> void:
	var terrain := FjordTerrain.new()
	add_child(terrain)
	var analytic := terrain.get_height_at(150.0, 600.0)
	for i in 600:
		if terrain.is_ready():
			break
		await get_tree().process_frame
	_check(terrain.is_ready(), "terrain: the worker thread builds the fjord")
	if not terrain.is_ready():
		terrain.queue_free()
		return
	_check(terrain.heightfield_hash == TERRAIN_GOLDEN_HASH,
		"terrain: the heightfield hash matches the xUnit golden (%s)" % terrain.heightfield_hash)
	_check(terrain.max_upload_frame_ms < 50.0,
		"terrain: no main-thread upload step over 50 ms (%.1f ms)" % terrain.max_upload_frame_ms)
	_check(absf(terrain.get_height_at(150.0, 600.0) - analytic) < 3.0,
		"terrain: grid heights match the generator queried before the grid existed")

	var faces := 0
	var up := true
	var agree := true
	for mesh_instance in terrain.get_chunk_meshes():
		var arrays := mesh_instance.mesh.surface_get_arrays(0)
		var verts: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
		var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
		for t in range(0, indices.size(), 3):
			var a := indices[t]
			var b := indices[t + 1]
			var c := indices[t + 2]
			var n := Plane(verts[a], verts[b], verts[c]).normal
			up = up and n.y > 0.0
			agree = agree and n.dot(normals[a]) > 0.0 and n.dot(normals[b]) > 0.0 and n.dot(normals[c]) > 0.0
			faces += 1
	var n_side := terrain.terrain_resolution - 1
	_check(faces == n_side * n_side * 2, "terrain: the chunks cover the grid (%d triangles)" % faces)
	_check(up, "terrain: every triangle's Plane normal points up (clockwise winding)")
	_check(agree, "terrain: every triangle agrees with its vertex normals")

	# The collider is a HeightMapShape3D scaled to the grid: a ray finds the
	# same ground the height query reports.
	var shape_node := terrain.get_node("TerrainBody").get_child(0) as CollisionShape3D
	_check(shape_node != null and shape_node.shape is HeightMapShape3D, "terrain: the collider is a HeightMapShape3D")
	await get_tree().physics_frame
	await get_tree().physics_frame
	var space := terrain.get_world_3d().direct_space_state
	var hits_match := true
	for probe in [Vector2(900.0, 300.0), Vector2(-1100.0, -200.0), Vector2(0.0, 1700.0), Vector2(-60.0, 0.0)]:
		var query := PhysicsRayQueryParameters3D.create(Vector3(probe.x, 3000.0, probe.y), Vector3(probe.x, -800.0, probe.y))
		var hit := space.intersect_ray(query)
		hits_match = hits_match and not hit.is_empty() \
			and absf((hit["position"] as Vector3).y - terrain.get_height_at(probe.x, probe.y)) < 1.5
	_check(hits_match, "terrain: physics rays hit the collider at the queried heights")
	terrain.queue_free()
	await get_tree().process_frame

## G1-03: at the FJORD and COAST defaults the horizon sits 10-25 % from the
## top with >= 10 % sky; 200 seeded orbits per zoom keep the camera >= 20 m
## over the ground with the focus in view; a 10 s orbit and zoom sweep never
## moves the camera more than 2 % of its distance in a frame; UNDERWATER
## goes below the surface.
func _test_camera_frames_sky_and_avoids_terrain() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "camera: seeded scenario starts")
	_sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var pens: FishPenManager = env.get_node("FishPenManager")
	cam.auto_orbit_enabled = false
	var first: Vector3 = pens.get_pen_position(pens.get_pen_ids()[0])
	for zoom in [FjordCamera.ZoomLevel.FJORD, FjordCamera.ZoomLevel.COAST]:
		cam.home_focus = first
		cam.recentre()
		cam.look_at_position(first, zoom)
		await _settle_camera(cam)
		var label: String = "FJORD" if zoom == FjordCamera.ZoomLevel.FJORD else "COAST"
		var row := _horizon_row(cam)
		_check(row >= 0.10 and row <= 0.25, "camera: %s horizon row %.3f of the frame height" % [label, row])
		var sky := _sky_fraction(cam)
		_check(sky >= 0.10, "camera: %s shows %.0f %% sky" % [label, sky * 100.0])

	var rng := RandomNumberGenerator.new()
	rng.seed = 1301
	var slots := pens.get_recommended_locations()
	for zoom in [FjordCamera.ZoomLevel.FARM, FjordCamera.ZoomLevel.FJORD, FjordCamera.ZoomLevel.COAST]:
		var distance: float = FjordCamera.ZOOM_DISTANCE[zoom]
		var pitch: float = FjordCamera.view_for_distance(distance).x
		var clear := 0
		var seen := 0
		for i in 200:
			var focus: Vector3 = slots[rng.randi_range(0, slots.size() - 1)] + Vector3(rng.randf_range(-60.0, 60.0), 0.0, rng.randf_range(-60.0, 60.0))
			var pos := cam.solve_rig(focus, rng.randf_range(-PI, PI), distance, pitch)
			if pos.y - maxf(terrain.get_height_at(pos.x, pos.z), terrain.water_level) >= 20.0:
				clear += 1
			if _line_clears_terrain(terrain, pos, focus):
				seen += 1
		_check(clear >= 198, "camera: zoom %d sits >= 20 m above the ground in %d/200 orbits" % [zoom, clear])
		_check(seen >= 198, "camera: zoom %d sees the focus in %d/200 orbits" % [zoom, seen])

	# 10 s orbit and zoom sweep at 60 fps, stepped by hand.
	cam.set_process(false)
	cam.look_at_position(first, FjordCamera.ZoomLevel.FJORD)
	for i in 240:
		cam._process(1.0 / 60.0)
	var last := cam.global_position
	var worst := 0.0
	var centre := get_viewport().get_visible_rect().size * 0.5
	for frame in 600:
		cam._yaw_target += cam.rotate_speed / 60.0
		if frame % 30 == 0:
			cam._zoom_by(FjordCamera.ZOOM_STEP if (frame / 180) % 2 == 0 else 1.0 / FjordCamera.ZOOM_STEP, centre)
		cam._process(1.0 / 60.0)
		worst = maxf(worst, cam.global_position.distance_to(last) / cam.get_current_distance())
		last = cam.global_position
	_check(worst <= 0.02, "camera: the largest per-frame move in a 10 s orbit and zoom sweep is %.2f %% of the distance" % (worst * 100.0))
	cam.set_process(true)

	cam.look_at_position(first, FjordCamera.ZoomLevel.UNDERWATER)
	await _settle_camera(cam)
	_check(cam.global_position.y < 0.0, "camera: UNDERWATER goes below the surface (y = %.1f)" % cam.global_position.y)
	cam.set_zoom_level(FjordCamera.ZoomLevel.FJORD)
	scene.queue_free()
	await get_tree().process_frame

## G1-03: camera keys never steal input from a focused Control: arrows on the
## tab bar and WASD in a text field leave the camera still; with nothing
## focused the same keys pan it.
func _test_camera_keys_leave_focused_controls_alone() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var cam: FjordCamera = scene.get_node("FjordEnvironment/FjordCamera")
	cam.auto_orbit_enabled = false
	ui.show_tab("fjord")
	await get_tree().process_frame
	_check(cam.input_enabled, "camera keys: the Fjord tab turns camera input on")
	var tab: Button = ui.find_child("Tab_fjord", true, false)
	tab.grab_focus()
	var before := cam.get_focus_point()
	await _hold_key(KEY_RIGHT, 20)
	_check(cam.get_focus_point() == before, "camera keys: the right arrow on the tab bar leaves the camera still")
	_check(get_viewport().gui_get_focus_owner() != null, "camera keys: the arrow walked the tab bar instead")

	var field := LineEdit.new()
	scene.get_node("UILayer").add_child(field)
	field.grab_focus()
	before = cam.get_focus_point()
	await _hold_key(KEY_W, 20)
	_check(cam.get_focus_point() == before, "camera keys: W typed into a text field leaves the camera still")
	_check(field.text.to_lower().contains("w"), "camera keys: the text field got the W ('%s')" % field.text)

	get_viewport().gui_release_focus()
	before = cam.get_focus_point()
	await _hold_key(KEY_W, 20)
	_check(cam.get_focus_point().distance_to(before) > 1.0, "camera keys: with nothing focused W pans the camera")
	ui.show_tab("dashboard")
	await get_tree().process_frame
	_check(not cam.input_enabled, "camera keys: an opaque panel turns camera input off")
	var hint: Control = scene.get_node("UILayer/CameraHint")
	_check(not hint.visible, "camera keys: the camera hint hides behind an opaque panel")
	before = cam.get_focus_point()
	await _hold_key(KEY_W, 20)
	_check(cam.get_focus_point() == before, "camera keys: W behind an opaque panel leaves the camera still")
	scene.queue_free()
	await get_tree().process_frame

## G1-02 / G1-04 / G1-06: the EnvironmentDirector composes fog, light, sky
## and the image settings from its inputs. The calendar and latitude come
## from the sim; fog is Koschmieder's (3.912 / visibility): a clear day
## leaves the FJORD focus >= 85 % visible, a storm <= 35 %; leaving the water
## restores the day fog; the sky's LIGHT0 is the sun light; the sun's energy
## never jumps > 10 % in a frame through a day and a storm; the aurora needs
## a dark, clear northern night; depth of field and shadow distance follow
## the zoom; the reflection probe is captured once the terrain is built.
func _test_environment_director_owns_the_look() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "environment: seeded scenario starts")
	_sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: FjordEnvironment = scene.get_node("FjordEnvironment")
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	var ts: TimeOfDay = env.get_node("TimeOfDay")
	var weather: WeatherController = env.get_node("WeatherSystem")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	var world_env: Environment = (env.get_node("WorldEnvironment") as WorldEnvironment).environment
	var sun: DirectionalLight3D = env.get_node("Sun")
	var moon: DirectionalLight3D = env.get_node("Moon")
	cam.auto_orbit_enabled = false

	# The calendar and the latitude are the sim's (D20).
	var ctx: Dictionary = _sim.get_sky_context()
	weather.transition_duration = 0.0
	vista._on_state_updated()
	await get_tree().process_frame
	_check(absf(ts.latitude_deg - 63.43) < 0.01, "environment: the sky's latitude is the scenario region's (Trøndelag 63.43, got %.2f)" % ts.latitude_deg)
	_check(ts.sim_day == int(ctx.get("sim_day", -1)), "environment: the sky's date is the sim day (%d)" % ts.sim_day)
	_check(absf(weather.get_visibility_m() - float(ctx.get("visibility_km", 0.0)) * 1000.0) < 1.0
		and is_equal_approx(weather.get_cloud_cover(), float(ctx.get("cloud_cover", -1.0))),
		"environment: the weather shows the sim's visibility (%.1f km) and cloud cover" % (weather.get_visibility_m() / 1000.0))

	# Stage the rest: the sim stops driving the sky.
	vista.sim_weather_enabled = false
	ts.paused = true
	weather.transition_duration = 0.0
	env.set_season(0.5)
	env.set_time(12.0)
	var focus_m: float = FjordCamera.ZOOM_DISTANCE[FjordCamera.ZoomLevel.FJORD]
	env.set_weather(WeatherController.WeatherState.CLEAR)
	director.compose(0.0, true)
	_check(absf(world_env.fog_density - 3.912 / weather.get_visibility_m()) < 1e-9,
		"environment: fog density is Koschmieder's 3.912 / visibility (%.6f)" % world_env.fog_density)
	var t_clear := director.transmittance(focus_m)
	_check(t_clear >= 0.85, "environment: a clear day leaves %.0f %% of the FJORD focus visible" % (t_clear * 100.0))
	var sky_mat := world_env.sky.sky_material as ShaderMaterial
	_check(is_equal_approx(float(sky_mat.get_shader_parameter("cloud_coverage")), weather.get_cloud_cover()),
		"environment: the sky's cloud cover is the weather's")
	var clear_energy: float = director.get_composed()["sun_energy"]
	env.set_weather(WeatherController.WeatherState.STORM)
	director.compose(0.0, true)
	var t_storm := director.transmittance(focus_m)
	_check(t_storm <= 0.35, "environment: a storm leaves %.0f %% of the FJORD focus visible" % (t_storm * 100.0))
	var storm_energy: float = director.get_composed()["sun_energy"]
	_check(storm_energy <= clear_energy * 0.2, "environment: storm clouds dim the sun (%.2f -> %.2f)" % [clear_energy, storm_energy])
	_check(sun.shadow_opacity < 0.5 and sun.light_angular_distance > 1.5 and sun.light_specular < 0.01, "environment: storm clouds soften the sun's shadows and take its glint off the water")

	# Underwater on entry, the day's fog back on exit.
	env.set_weather(WeatherController.WeatherState.CLEAR)
	director.compose(0.0, true)
	var day_color := world_env.fog_light_color
	var day_density := world_env.fog_density
	var first: Vector3 = pens.get_pen_position(pens.get_pen_ids()[0])
	cam.look_at_position(first, FjordCamera.ZoomLevel.UNDERWATER)
	await _settle_camera(cam)
	director.compose(0.0, true)
	_check(cam.global_position.y < 0.0 and world_env.fog_density > day_density * 20.0,
		"environment: under the surface the water's fog applies (%.4f)" % world_env.fog_density)
	cam.set_zoom_level(FjordCamera.ZoomLevel.FJORD)
	await _settle_camera(cam)
	director.compose(0.0, true)
	_check(cam.global_position.y > 0.0 and world_env.fog_light_color.is_equal_approx(day_color) and is_equal_approx(world_env.fog_density, day_density),
		"environment: leaving the water restores the day fog colour")

	# The sky's LIGHT0 is the sun light, pointed from the sky's sun.
	var angle := rad_to_deg((-sun.global_transform.basis.z).angle_to(-ts.get_sun_direction()))
	_check(angle < 0.5, "environment: the sun light shines from the sky's sun (%.3f deg apart)" % angle)
	_check(sun.sky_mode == DirectionalLight3D.SKY_MODE_LIGHT_AND_SKY and moon.sky_mode == DirectionalLight3D.SKY_MODE_LIGHT_ONLY,
		"environment: the sun is the sky's LIGHT0 (the moon light stays out of the sky)")

	# A day at 10 game minutes per second, 60 fps, a storm rolling in at noon.
	weather.transition_duration = 10.0
	ts.time_scale = 10.0
	ts.paused = false
	ts.set_hour(0.0)
	director.set_process(false)
	ts.set_process(false)
	weather.set_process(false)
	director.compose(0.0, true)
	var prev := sun.light_energy
	var floor_energy := EnvironmentDirector.SUN_ENERGY * 0.1
	var worst := 0.0
	var max_energy := 0.0
	for frame in 8640:
		if frame == 4320:
			env.set_weather(WeatherController.WeatherState.STORM)
		ts._process(1.0 / 60.0)
		weather._process(1.0 / 60.0)
		director.compose(1.0 / 60.0)
		var e := sun.light_energy
		worst = maxf(worst, absf(e - prev) / maxf(prev, floor_energy))
		max_energy = maxf(max_energy, e)
		prev = e
	director.set_process(true)
	ts.set_process(true)
	weather.set_process(true)
	ts.paused = true
	weather.transition_duration = 0.0
	_check(max_energy > 1.0 and worst <= 0.10,
		"environment: through a day and a storm the sun's energy never steps more than 10 %% in a frame (worst %.1f %%)" % (worst * 100.0))

	# The aurora: a clear winter night in Nordland, gone under cloud and by day.
	env.set_latitude(68.2)
	env.set_season(15.0 / 365.0)
	env.set_time(22.0)
	env.set_weather(WeatherController.WeatherState.CLEAR)
	director.compose(0.0, true)
	var night_aurora: float = director.get_composed()["aurora_intensity"]
	_check(night_aurora > 0.2, "environment: a clear Nordland winter night has its aurora (%.2f)" % night_aurora)
	env.set_weather(WeatherController.WeatherState.OVERCAST)
	director.compose(0.0, true)
	_check(director.get_composed()["aurora_intensity"] == 0.0, "environment: an overcast sky hides the aurora")
	env.set_weather(WeatherController.WeatherState.CLEAR)
	env.set_time(12.0)
	env.set_season(0.5)
	director.compose(0.0, true)
	_check(director.get_composed()["aurora_intensity"] == 0.0, "environment: no aurora in daylight")

	# Depth of field and shadows by zoom.
	var attrs := cam.attributes as CameraAttributesPractical
	cam.look_at_position(first, FjordCamera.ZoomLevel.FARM)
	await _settle_camera(cam)
	director.compose(0.0, true)
	_check(attrs != null and attrs.dof_blur_far_enabled and absf(attrs.dof_blur_amount - 0.06) < 0.005 and absf(sun.directional_shadow_max_distance - 350.0) < 5.0,
		"environment: FARM focuses near (DOF %.3f, shadows %.0f m)" % [attrs.dof_blur_amount if attrs else -1.0, sun.directional_shadow_max_distance])
	cam.look_at_position(first, FjordCamera.ZoomLevel.COAST)
	await _settle_camera(cam)
	director.compose(0.0, true)
	_check(absf(attrs.dof_blur_amount - 0.14) < 0.005 and absf(sun.directional_shadow_max_distance - 4500.0) < 5.0 and absf(world_env.ssao_radius - 8.0) < 0.05,
		"environment: COAST softens the far fjord (DOF %.3f, shadows %.0f m, SSAO %.1f m)" % [attrs.dof_blur_amount, sun.directional_shadow_max_distance, world_env.ssao_radius])
	_check(world_env.tonemap_mode == Environment.TONE_MAPPER_AGX and is_equal_approx(world_env.tonemap_agx_white, 6.0) and not world_env.sdfgi_enabled,
		"environment: AgX tonemapping, white 6, no SDFGI")

	# The reflection probe is captured once the terrain is built.
	for i in 600:
		if terrain.is_ready():
			break
		await get_tree().process_frame
	for i in 60:
		await get_tree().process_frame
	_check(terrain.is_ready() and director.probe_captures >= 1,
		"environment: the reflection probe is captured after the terrain build (%d captures)" % director.probe_captures)
	scene.queue_free()
	await get_tree().process_frame

## G1-05: the water is a grid snapped to the camera, 2 m apart within 256 m
## of it; the terrain's bake reaches the water and puts the pens over deep,
## sheltered water; the sea comes from the wind and the fetch (a 3 m/s
## breeze leaves the basin near flat, a 20 m/s gale rolls 2-4 m seas on the
## open coast, wind never scales a fixed wave), its phases run on, and in a
## 25 m/s storm the pens and boats ride the water they float in (G2-03,
## _check_waves_carry_pens_and_boats). The reflection probe never mirrors
## the water.
func _test_water_grid_bake_and_sea_state() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "water: seeded scenario starts")
	_sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: FjordEnvironment = scene.get_node("FjordEnvironment")
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	var grid: WaterGrid = env.get_node("WaterGrid")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var weather: WeatherController = env.get_node("WeatherSystem")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	var probe: ReflectionProbe = env.get_node("ReflectionProbe")
	cam.auto_orbit_enabled = false
	for i in 600:
		if terrain.is_ready():
			break
		await get_tree().process_frame
	_check(terrain.is_ready(), "water: the terrain (and its water bake) is built")
	var first: Vector3 = pens.get_pen_position(pens.get_pen_ids()[0])
	cam.look_at_position(first, FjordCamera.ZoomLevel.FARM)
	await _settle_camera(cam)
	await get_tree().process_frame

	# The grid: centred on the camera in whole 2 m steps, 2 m apart within 256 m.
	var step := grid.base_spacing
	var gp := grid.global_position
	var cp := cam.global_position
	_check(absf(gp.x - cp.x) <= step * 0.5 + 1e-3 and absf(gp.z - cp.z) <= step * 0.5 + 1e-3
		and is_zero_approx(fposmod(gp.x, step)) and is_zero_approx(fposmod(gp.z, step)),
		"water: the grid is centred on the camera in whole %.0f m steps" % step)
	var fine := true
	for i in 400:
		var a := float(i) * 2.399963
		var r := 256.0 * sqrt(float(i) / 400.0)
		fine = fine and grid.spacing_at(cp + Vector3(cos(a) * r, 0.0, sin(a) * r)) <= 2.0
	var level0: Array[MeshInstance3D] = []
	for piece in grid.get_pieces():
		if piece.name.begins_with("L0_"):
			level0.append(piece)
	var covered := AABB()
	var spaced := level0.size() == 4
	for piece in level0:
		var plane := piece.mesh as PlaneMesh
		var world_box: AABB = piece.global_transform * plane.get_aabb()
		covered = world_box if covered.size == Vector3.ZERO else covered.merge(world_box)
		# Unit quads (size / (subdivisions + 1) = 1) scaled by the level's spacing.
		var unit_quads := is_equal_approx(plane.size.x / float(plane.subdivide_width + 1), 1.0)
		spaced = spaced and unit_quads and is_equal_approx(piece.global_transform.basis.get_scale().x, 2.0)
	var reach := minf(minf(cp.x - covered.position.x, covered.end.x - cp.x), minf(cp.z - covered.position.z, covered.end.z - cp.z))
	_check(fine and spaced and reach >= 256.0 - step,
		"water: vertices 2 m apart within 256 m of the camera (level 0 reaches %.0f m)" % reach)
	cam.look_at_position(first + Vector3(37.0, 0.0, -53.0), FjordCamera.ZoomLevel.FARM)
	await _settle_camera(cam)
	await get_tree().process_frame
	_check(grid.global_position.distance_to(Vector3(cam.global_position.x, 0.0, cam.global_position.z)) <= step,
		"water: the grid follows the camera")

	# The bake.
	var mat := director.water_material()
	_check(mat != null and is_equal_approx(float(mat.get_shader_parameter("has_bake")), 1.0)
		and mat.get_shader_parameter("water_bake") == terrain.water_bake_texture,
		"water: the terrain's bathymetry bake reaches the water")
	var at_pen := terrain.water_bake_at(first.x, first.z)
	_check(at_pen.r > 15.0 and at_pen.g > 30.0 and at_pen.b < 0.25,
		"water: the pens float over deep, sheltered water (depth %.0f m, shore %.0f m, exposure %.2f)" % [at_pen.r, at_pen.g, at_pen.b])
	_check(absf(terrain.water_bake_at(150.0, 600.0).r + terrain.get_height_at(150.0, 600.0)) < 2.0,
		"water: the baked depth is the terrain's depth below sea level")

	# The sea from the wind and the fetch.
	vista.sim_weather_enabled = false
	weather.transition_duration = 0.0
	env.apply_sky_context({"weather_state": "Calm", "wind_ms": 3.0, "cloud_cover": 0.2, "visibility_km": 40.0})
	director.compose(1.0 / 60.0)
	var calm: Dictionary = director.sea_state.duplicate()
	_check(float(calm["sheltered_hs"]) >= 0.03 and float(calm["sheltered_hs"]) <= 0.15,
		"water: a 3 m/s breeze leaves the sheltered basin near flat (Hs %.2f m over %.0f m of fetch)" % [calm["sheltered_hs"], calm["fetch_m"]])
	env.apply_sky_context({"weather_state": "Storm", "wind_ms": 20.0, "cloud_cover": 0.97, "visibility_km": 4.0})
	director.compose(1.0 / 60.0)
	var gale: Dictionary = director.sea_state.duplicate()
	_check(float(gale["exposed_hs"]) >= 2.0 and float(gale["exposed_hs"]) <= 4.0 and float(gale["sheltered_hs"]) < float(gale["exposed_hs"]) * 0.5,
		"water: a 20 m/s gale rolls %.1f m seas on the open coast, %.2f m in the basin" % [gale["exposed_hs"], gale["sheltered_hs"]])
	_check(float(gale["whitecaps"]) > float(calm["whitecaps"]) * 100.0,
		"water: whitecaps follow the wind (Monahan %.4f -> %.3f)" % [calm["whitecaps"], gale["whitecaps"]])
	var amps: PackedVector4Array = mat.get_shader_parameter("wave_amp")
	var sum_sq := 0.0
	for amp in amps:
		sum_sq += amp.x * amp.x
	_check(absf(sqrt(8.0 * sum_sq) - float(gale["sheltered_hs"])) < 0.01,
		"water: the drawn waves carry the basin's significant height (sum a^2 = Hs^2 / 8)")
	var phase_before := amps[0].z
	director.compose(0.25)
	var phase_after: float = (mat.get_shader_parameter("wave_amp") as PackedVector4Array)[0].z
	_check(not is_equal_approx(phase_before, phase_after), "water: the wave phases run on")

	# G2-03 replaced the G1-05 freeboard check ("a 25 m/s storm's highest
	# crest at the pen stays under the collar top", which held only for a
	# fixed collar "until G2-03 makes the pens bob"): the pens and boats now
	# ride the water, so the checks follow the water they float in.
	env.apply_sky_context({"weather_state": "Severe", "wind_ms": 25.0, "cloud_cover": 1.0, "visibility_km": 2.0})
	director.compose(1.0 / 60.0)
	await _check_waves_carry_pens_and_boats(director, pens, grid, terrain, pens.get_pen_ids()[0])

	_check(probe.reflection_mask & WaterGrid.RENDER_LAYER == 0 and grid.get_pieces()[0].layers == WaterGrid.RENDER_LAYER,
		"water: the reflection probe leaves the water's layer out")
	scene.queue_free()
	await get_tree().process_frame

## Sea time between samples of the riding checks (s): incommensurate with
## every component's period, so the samples sweep their relative phases.
const RIDE_SAMPLE_STEP_S := 0.137
const RIDE_SAMPLES := 160
const RIDE_RING_POINTS := 16
const OPEN_COAST_SAMPLES := 40

## G2-03: what the water draws carries the pens and boats.
## 1. WaveField is the shader's wave: it holds the very wave, grid and bake
##    uniforms the director writes to the water material, and the vertex
##    displacement transcribed from water_shader.gdshader and fed those
##    uniforms lands within 1 cm of WaveField.height_at at 96 points.
## 2. In a 25 m/s storm, over RIDE_SAMPLES moments of the sea, the pen sits
##    on its ring's mean surface (|y - mean| <= 0.1 m) and lies on its slope
##    (within 0.005 of the least-squares plane), both sampled point by point
##    round the ring, and the water at the collar ring stays under the
##    riding collar's top (the old check asked this of a fixed collar and a
##    worst-case crest).
## 3. The barge, its tender RIB, the site's workboat and a waiting wellboat
##    sit on the mean surface under their waterplanes (|y - mean| <= 0.1 m)
##    and lie on its slope (pitch and roll within 0.01 of the sampled
##    least-squares plane).
## 4. On the open coast (exposure 1, Hs 4 m) the closed-form plane of a
##    116 m hull stays within 0.1 m and 0.01 of the sampled one.
func _check_waves_carry_pens_and_boats(director: EnvironmentDirector, pens: FishPenManager, grid: WaterGrid,
		terrain: FjordTerrain, site_id: String) -> void:
	var field := director.wave_field
	var mat := director.water_material()
	var image := terrain.get_water_bake_image()
	var uniforms_ok: bool = WaveField.active == field and mat != null and image != null \
		and field.wave_k.size() == 16 and field.wave_k == mat.get_shader_parameter("wave_k") \
		and field.wave_amp == mat.get_shader_parameter("wave_amp") \
		and is_equal_approx(field.grid_half_units, float(mat.get_shader_parameter("grid_half_units"))) \
		and is_equal_approx(field.grid_base_spacing, float(mat.get_shader_parameter("grid_base_spacing"))) \
		and is_equal_approx(field.displaced_extent, float(mat.get_shader_parameter("displaced_extent"))) \
		and field.bake_origin == mat.get_shader_parameter("bake_origin") \
		and is_equal_approx(field.bake_spacing, float(mat.get_shader_parameter("bake_spacing"))) \
		and is_equal_approx(field.bake_texels, float(mat.get_shader_parameter("bake_texels"))) \
		and is_equal_approx(field.has_bake, float(mat.get_shader_parameter("has_bake"))) \
		and field.grid_origin() == grid.global_position
	var pen_at := pens.get_pen_position(site_id)
	var worst_vertex := 0.0
	var drawn_height := 0.0
	if uniforms_ok:
		for i in 96:
			# Out to 600 m: the 2, 4 and 8 m grid levels and their band limits.
			var a := float(i) * 2.399963
			var r := 600.0 * sqrt((float(i) + 0.5) / 96.0)
			var p0 := Vector2(pen_at.x + cos(a) * r, pen_at.z + sin(a) * r)
			var d := _shader_vertex(mat, image, grid.global_position, p0)
			drawn_height = maxf(drawn_height, absf(d.y))
			worst_vertex = maxf(worst_vertex, absf(field.height_at(p0.x + d.x, p0.y + d.z) - (grid.global_position.y + d.y)))
	_check(uniforms_ok and drawn_height > 0.1 and worst_vertex <= 0.01,
		"water: the CPU wave carries the shader's own wave, grid and bake uniforms and lands within %.6f m of its vertex displacement at 96 points (drawn heights to %.2f m)"
			% [worst_vertex, drawn_height])

	var cage := pens._pens[site_id]["node"] as PenCage
	var collar := cage.get_node_or_null("Collar") as MeshInstance3D if cage != null else null
	var collar_top := (collar.transform * collar.mesh.get_aabb()).end.y if collar != null else 0.0
	var worst_heave := 0.0
	var worst_tilt := 0.0
	var worst_rise := -INF
	var least_margin := INF
	var heaves := PackedFloat32Array()
	for step in RIDE_SAMPLES:
		director.compose(RIDE_SAMPLE_STEP_S)
		if cage == null:
			break
		cage.ride_waves()
		# The ring's least-squares plane, sampled point by point.
		var mean := 0.0
		var xh := 0.0
		var zh := 0.0
		var xx := 0.0
		var zz := 0.0
		for k in RIDE_RING_POINTS:
			var a := TAU * float(k) / RIDE_RING_POINTS
			var dx := cos(a) * PenCage.RING_RADIUS
			var dz := sin(a) * PenCage.RING_RADIUS
			var h := field.height_at(pen_at.x + dx, pen_at.z + dz)
			mean += h
			xh += dx * h
			zh += dz * h
			xx += dx * dx
			zz += dz * dz
		mean /= RIDE_RING_POINTS
		worst_heave = maxf(worst_heave, absf(cage.global_position.y - mean))
		var up := cage.global_transform.basis.y
		worst_tilt = maxf(worst_tilt, maxf(absf(-up.x / up.y - xh / xx), absf(-up.z / up.y - zh / zz)))
		heaves.append(cage.global_position.y)
		# The collar's inner and outer pipe (build_pen.py R_IN, R_OUT) round the ring.
		for k in RIDE_RING_POINTS * 2:
			var a := TAU * (floorf(float(k) * 0.5) + 0.5 * float(k % 2)) / RIDE_RING_POINTS
			var radius := 25.465 if k % 2 == 0 else 26.565
			var waterline := cage.global_transform * Vector3(cos(a) * radius, 0.0, sin(a) * radius)
			var top := cage.global_transform * Vector3(cos(a) * radius, collar_top, sin(a) * radius)
			var water := field.height_at(top.x, top.z)
			worst_rise = maxf(worst_rise, water - waterline.y)
			least_margin = minf(least_margin, top.y - water)
	var heave_range := 0.0
	if not heaves.is_empty():
		heave_range = Array(heaves).max() - Array(heaves).min()
	_check(cage != null and worst_heave <= 0.1 and heave_range > 0.05 and worst_tilt <= 0.005,
		"water: in a 25 m/s storm the pen rides its ring's mean surface and slope (|y - mean| <= %.3f m, slope within %.4f, over %d moments, heave range %.2f m)"
			% [worst_heave, worst_tilt, RIDE_SAMPLES, heave_range])
	_check(collar != null and worst_rise > 0.0 and least_margin > 0.0,
		"water: ... and the water round the riding collar rises at most %.2f m above its waterline, under its %.2f m top (least margin %.2f m)"
			% [worst_rise, collar_top, least_margin])

	var boats: WellboatVisualManager = pens.get_parent().get_node("WellboatVisualManager")
	var barges: FeedBargeVisualManager = pens.get_parent().get_node("FeedBargeVisualManager")
	boats.sail_seconds = 0.2
	_sim.request_wellboat(site_id, "harvest")
	_sim.state_updated.emit()
	await get_tree().create_timer(0.5).timeout
	var barge := barges.barge_of(site_id) as VesselModel
	var riders: Array[VesselModel] = []
	if barge != null:
		riders.append(barge)
		riders.append(barge.get_node_or_null("Tender") as VesselModel)
	riders.append(pens.get_node_or_null("PenMarkers/%s_boat" % site_id) as VesselModel)
	for id in boats.active_job_ids():
		if boats._active_wellboats[id].site_id == site_id and boats.job_state(id) == "at_destination":
			riders.append(boats._active_wellboats[id].boat as VesselModel)
	var riders_ok := riders.size() == 4 and not riders.has(null)
	var report := []
	for boat in (riders if riders_ok else []):
		var worst_y := 0.0
		var worst_slope := 0.0
		var tilt := 0.0
		for step in 24:
			director.compose(RIDE_SAMPLE_STEP_S * 3.0)
			if boat.get_parent() is VesselModel:
				(boat.get_parent() as VesselModel).ride_waves()
			boat.ride_waves()
			var fit := _waterplane_fit(field, boat.rest_transform(), boat.length_m * 0.5, boat.beam_m * 0.5)
			var b := boat.global_transform.basis
			var along := b.z.y / Vector2(b.z.x, b.z.z).length()
			var across := b.x.y / Vector2(b.x.x, b.x.z).length()
			worst_y = maxf(worst_y, absf(boat.global_position.y - fit.x))
			worst_slope = maxf(worst_slope, maxf(absf(along - fit.y), absf(across - fit.z)))
			tilt = maxf(tilt, rad_to_deg(maxf(absf(atan(along)), absf(atan(across)))))
		riders_ok = riders_ok and worst_y <= 0.1 and worst_slope <= 0.01
		report.append("%s dy %.3f m, slope %.4f, tilt to %.1f deg" % [boat.asset_id, worst_y, worst_slope, tilt])
	_check(riders_ok,
		"water: barge, tender, workboat and wellboat sit on the mean surface under their waterplanes and lie on its slope (%s)"
			% ", ".join(report))

	# The open coast, where boats sail out from the harbour: past the bake
	# the sea is the exposed one (100 km fetch, Hs 4 m at 25 m/s), its long
	# steep waves the hardest case for the closed forms (second order).
	var coast := Vector3(0.0, 0.0, -2150.0)
	var hull_rest := Transform3D(Basis(Vector3.UP, 0.3), coast)
	# Framed at FARM zoom, so the grid draws the coast's waves down to 26 m.
	var cam: FjordCamera = pens.get_parent().get_node("FjordCamera")
	cam.look_at_position(coast, FjordCamera.ZoomLevel.FARM)
	await _settle_camera(cam)
	var coast_y := 0.0
	var coast_slope := 0.0
	var coast_heave := 0.0
	var coast_sea := 0.0
	for step in OPEN_COAST_SAMPLES:
		director.compose(RIDE_SAMPLE_STEP_S * 5.0)
		var plane := field.hull(coast.x, coast.z, Vector2(hull_rest.basis.z.x, hull_rest.basis.z.z),
			Vector2(hull_rest.basis.x.x, hull_rest.basis.x.z), 58.0, 11.5)
		var fit := _waterplane_fit(field, hull_rest, 58.0, 11.5)
		coast_y = maxf(coast_y, absf(plane.x - fit.x))
		coast_slope = maxf(coast_slope, maxf(absf(plane.y - fit.y), absf(plane.z - fit.z)))
		coast_heave = maxf(coast_heave, absf(fit.x))
		coast_sea = maxf(coast_sea, absf(field.height_at(coast.x, coast.z)))
	_check(field.bake_at(coast.x, coast.z).b > 0.99 and coast_sea > 0.5 and coast_y <= 0.1 and coast_slope <= 0.01,
		"water: on the open coast in the 25 m/s gale a 116 m hull's plane stays within %.3f m and %.4f of the sampled water (sea to %.2f m, hull heave to %.2f m, %d moments)"
			% [coast_y, coast_slope, coast_sea, coast_heave, OPEN_COAST_SAMPLES])


## Point spacing (m) of the sampled waterplane: under half the shortest wave
## the 2 m grid draws (6 m), so the reference does not alias.
const WATERPLANE_SAMPLE_M := 2.9

## The least-squares plane of the drawn water under a waterplane, sampled
## every WATERPLANE_SAMPLE_M over its length and beam: Vector3(mean, slope
## along +Z, slope along +X).
func _waterplane_fit(field: WaveField, rest: Transform3D, half_length: float, half_beam: float) -> Vector3:
	var n_along := maxi(3, ceili(2.0 * half_length / WATERPLANE_SAMPLE_M) + 1)
	var n_across := maxi(3, ceili(2.0 * half_beam / WATERPLANE_SAMPLE_M) + 1)
	var sum := 0.0
	var along := 0.0
	var across := 0.0
	var along_sq := 0.0
	var across_sq := 0.0
	for i in n_along:
		for j in n_across:
			var u := (float(i) / float(n_along - 1) * 2.0 - 1.0) * half_length
			var v := (float(j) / float(n_across - 1) * 2.0 - 1.0) * half_beam
			var p := rest * Vector3(v, 0.0, u)
			var h := field.height_at(p.x, p.z)
			sum += h
			along += u * h
			across += v * h
			along_sq += u * u
			across_sq += v * v
	return Vector3(sum / float(n_along * n_across), along / along_sq, across / across_sq)


## water_shader.gdshader vertex(): the displacement of the grid vertex at
## p0, transcribed from the shader source and fed the material's uniforms
## (an independent path to compare WaveField with).
func _shader_vertex(mat: ShaderMaterial, image: Image, origin: Vector3, p0: Vector2) -> Vector3:
	var wave_k: PackedVector4Array = mat.get_shader_parameter("wave_k")
	var wave_amp: PackedVector4Array = mat.get_shader_parameter("wave_amp")
	var half_units := float(mat.get_shader_parameter("grid_half_units"))
	var base := float(mat.get_shader_parameter("grid_base_spacing"))
	var extent := float(mat.get_shader_parameter("displaced_extent"))
	var bake_origin: Vector2 = mat.get_shader_parameter("bake_origin")
	var bake_spacing := float(mat.get_shader_parameter("bake_spacing"))
	var texels := float(mat.get_shader_parameter("bake_texels"))
	# bake_at(): linear filter between texel centres, clamped to the edge,
	# faded to the open ocean over the last 1.5 % of the bake.
	var uv := ((p0 - bake_origin) / bake_spacing + Vector2(0.5, 0.5)) / texels
	var n := image.get_width()
	var t := (uv * texels - Vector2(0.5, 0.5)).clamp(Vector2.ZERO, Vector2(n - 1, n - 1))
	var c0 := Vector2i(mini(int(t.x), n - 2), mini(int(t.y), n - 2))
	var f := t - Vector2(c0)
	var bake := image.get_pixel(c0.x, c0.y).lerp(image.get_pixel(c0.x + 1, c0.y), f.x).lerp(
		image.get_pixel(c0.x, c0.y + 1).lerp(image.get_pixel(c0.x + 1, c0.y + 1), f.x), f.y)
	var edge := minf(minf(uv.x, 1.0 - uv.x), minf(uv.y, 1.0 - uv.y))
	bake = Color(200.0, 8000.0, 1.0, 0.0).lerp(bake, smoothstep(0.0, 0.015, edge) * float(mat.get_shader_parameter("has_bake")))
	var d_grid := maxf(absf(p0.x - origin.x), absf(p0.y - origin.z))
	var spacing := base * maxf(1.0, 2.0 * d_grid / (half_units * base))
	var d := Vector3.ZERO
	for i in wave_k.size():
		var wk := wave_k[i]
		var wa := wave_amp[i]
		if wa.w < 3.0 * spacing:
			break
		var a := lerpf(wa.x, wa.y, bake.b)
		if bake.r < 40.0:
			var depth := maxf(bake.r, 0.0)
			a *= depth / (depth + 0.08 * wa.w + 0.5)
		a *= smoothstep(3.0 * spacing, 6.0 * spacing, wa.w)
		if a < 1e-4:
			continue
		var ph := fposmod(wk.x * p0.x + wk.y * p0.y - wa.z, TAU)
		d.y += a * sin(ph)
		d.x += wk.w * a * (wk.x / wk.z) * cos(ph)
		d.z += wk.w * a * (wk.y / wk.z) * cos(ph)
	return d * (1.0 - smoothstep(0.75 * extent, extent, d_grid))


## Hold a key down through the Input singleton for `frames` frames.
func _hold_key(keycode: Key, frames: int) -> void:
	var down := InputEventKey.new()
	down.keycode = keycode
	down.physical_keycode = keycode
	down.unicode = keycode + 32 if keycode >= KEY_A and keycode <= KEY_Z else 0
	down.pressed = true
	Input.parse_input_event(down)
	for i in frames:
		await get_tree().process_frame
	var up := InputEventKey.new()
	up.keycode = keycode
	up.physical_keycode = keycode
	up.pressed = false
	Input.parse_input_event(up)
	await get_tree().process_frame
	await get_tree().process_frame

func _settle_camera(cam: FjordCamera) -> void:
	var last := Vector3.INF
	for i in 3000:
		await get_tree().process_frame
		if cam.global_position.distance_to(last) < 0.001:
			return
		last = cam.global_position

## Where the horizon (the horizontal through the camera) crosses the screen,
## as a fraction of the frame height from the top.
func _horizon_row(cam: Camera3D) -> float:
	var forward := -cam.global_transform.basis.z
	forward.y = 0.0
	var far_point := cam.global_position + forward.normalized() * 100000.0
	return cam.unproject_position(far_point).y / get_viewport().get_visible_rect().size.y

## Fraction of a 48x27 grid of screen rays that rise into open sky.
func _sky_fraction(cam: FjordCamera) -> float:
	var size := get_viewport().get_visible_rect().size
	var sky := 0
	for gy in 27:
		for gx in 48:
			var p := Vector2((gx + 0.5) / 48.0 * size.x, (gy + 0.5) / 27.0 * size.y)
			if cam.project_ray_normal(p).y > 0.0 and not cam.pick_ground(p).is_finite():
				sky += 1
	return float(sky) / float(48 * 27)

## True when the straight line from the camera to the focus passes over the
## terrain all the way (water never hides the focus).
func _line_clears_terrain(terrain: FjordTerrain, from: Vector3, to: Vector3) -> bool:
	for i in range(1, 100):
		var p := from.lerp(to, float(i) / 100.0)
		var ground := terrain.get_height_at(p.x, p.z)
		if ground > terrain.water_level and p.y < ground:
			return false
	return true
