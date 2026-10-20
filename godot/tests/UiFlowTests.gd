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
	# U2-03: this checkout's own campaign file (never the player's), fresh.
	CampaignProgress.path_override = TEST_ROOT + "campaign.json"
	CampaignProgress.reset()
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
	await _test_zones_report_their_production_area_water()
	await _test_pens_report_their_size_spread_and_ration()
	await _test_harvest_pipeline_reaches_the_ui()
	await _test_honest_economy_reaches_the_ui()
	_test_mortality_ledger_reaches_the_bridge()
	_test_capacity_reaches_the_bridge()
	await _test_pens_reach_the_stocking_card()
	await _test_pen_models_read_as_equipment()
	await _test_hero_pen_and_site_kit()
	await _test_vessel_fleet()
	await _test_multi_pen_sites()
	await _test_fish_schools_and_night_lights()
	_test_facade_forwards_phase5_systems()
	await _test_terrain_faces_up_and_shares_one_heightfield()
	await _test_camera_frames_sky_and_avoids_terrain()
	await _test_far_field_surrounds_the_world()
	await _test_camera_keys_leave_focused_controls_alone()
	await _test_environment_director_owns_the_look()
	await _test_sky_latitude_follows_a_northern_scenario()
	await _test_terrain_material_and_vegetation()
	await _test_water_grid_bake_and_sea_state()
	await _test_quality_tiers_and_covered_world()
	await _test_water_mirror_wakes_and_spray()
	# G3-05: moods, weather drama and the Nordic night (WeatherDramaChecks.gd).
	await WeatherDramaChecks.new().run(self)
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
	await _test_main_menu_shows_the_live_fjord()
	await _test_main_menu_column_reads_over_every_mood()
	await _test_main_menu_reduced_motion_holds_the_camera()
	await _test_charts_draw_the_sims_series_over_sixty_days()
	await _test_share_bars_are_the_ledger_shares()
	await _test_dashboard_site_cards_match_the_sites()
	await _test_skipped_days_date_their_alerts()
	await _test_map_labels_never_collide()
	await _test_pen_detail_fits_and_pads_its_cards()
	await _test_market_and_crew_show_the_sims_own_numbers()
	await _test_event_bus_one_channel_per_severity()
	await _test_repeated_condition_makes_no_duplicates()
	await _test_critical_shake_moves_the_camera_offset()
	await _test_critical_card_actions()
	await _test_inbox_lists_what_happened()
	await _test_toasts_keep_clear_of_actions_and_inbox_header()
	# U2-07: pacing (briefing, calendar HUD, speeds).
	await _test_hud_shows_the_sim_calendar()
	await _test_speed_keys_and_buttons_drive_the_clock()
	await _test_next_event_runs_to_the_next_warning()
	await _test_deadline_slows_then_stops_the_clock()
	await _test_a_met_order_stops_slowing_the_clock()
	await _test_critical_card_holds_the_clock()
	await _test_new_game_opens_paused_on_its_briefing()
	await _test_thirty_times_refreshes_once_a_day_within_a_frame()
	await _test_audio_reads_tracks_off_the_tick()
	await _test_hud_fits_its_bar()
	# U2-04: the feeding plan.
	await _test_feed_plan_card_sets_a_plan_and_feeds_all()
	# U2-05: the harvest planner, the planned order, the report, the payoff.
	await _test_harvest_planner_orders_and_reports()
	await _test_harvest_pen_announces_once()
	_test_farm_type_graybox()
	_test_land_kit_imports()
	_test_offshore_unit_and_fleet()
	# F2-03: an offshore unit leaves the fjord's pen layout alone.
	await _test_offshore_unit_leaves_fjord_pens_in_place()
	# F2-04: the farm-type catalogue and the research gates reach the UI.
	await _test_farm_type_catalog_reaches_the_ui()
	# F2-05: the Driftsplan card sets, changes and ends standing orders.
	await _test_driftsplan_card_sets_and_runs_standing_orders()
	# F2-12/F2-14: the land lane's bridge verbs (previews, permits, construction).
	await _test_land_bridge_previews_and_applies()
	# U2-01: the expansion flow (map, build dialog, licence auction, inbox).
	await _test_expansion_builds_the_tutorials_second_site()
	await _test_auction_card_wins_a_green_zone_licence()
	await _test_every_build_refusal_reads_as_text()
	# U2-03: end screens, the liquidity ladder, the rescue loan, campaign progress.
	await preload("res://tests/EndScreenFlowChecks.gd").new().run(self)

	_clear_test_saves()
	CampaignProgress.reset()
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
## only the panel, and V opens it again. (U2-07/D15: the drawer's key moved
## from N to V, N is "Til neste hendelse"; RUN_LOG §7.)
func _test_notification_center_escape() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	ui.show_tab("finance")
	await _press_key(KEY_ESCAPE)
	_check(ui.get_active_tab() == "dashboard", "notifications: ESC reaches GameUI while the panel is closed")
	_check(center.visible, "notifications: ESC leaves the notification center in place")
	await _press_key(KEY_V)
	_check(center.panel.visible, "notifications: V opens the panel")
	ui.show_tab("finance")
	await _press_key(KEY_ESCAPE)
	await get_tree().create_timer(0.5).timeout  # slide-out
	_check(not center.panel.visible, "notifications: ESC closes the open panel")
	_check(ui.get_active_tab() == "finance", "notifications: that ESC closed only the panel")
	await _press_key(KEY_V)
	_check(center.visible and center.panel.visible, "notifications: V opens the panel again after ESC")
	await _press_key(KEY_N)
	_check(_sim.is_running_to_next_event() and center.panel.visible,
		"notifications: N runs to the next event and leaves the drawer open")
	_sim.stop_run_to_next_event()
	_sim.set_paused(true)
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
	# U2-07 (stronger, RUN_LOG §7): the HUD follows the game clock, so Space
	# toggles what the clock does; it used to toggle the HUD's own flag, which
	# had not seen the setup's set_paused(true). From a running clock: pause,
	# then resume, and the clock itself follows each press.
	_sim.set_paused(false)
	var clock_states: Array = []
	await _press_key(KEY_SPACE, 32)
	clock_states.append(_sim.is_paused())
	await _press_key(KEY_SPACE, 32)
	clock_states.append(_sim.is_paused())
	_check(pauses == [true, false] and clock_states == [true, false],
		"keys: Space pauses and resumes with nothing focused (%s, clock %s)" % [str(pauses), str(clock_states)])
	_sim.set_paused(true)

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
## U2-07/D15 (RUN_LOG §7): the drawer's key is V and N is "Til neste
## hendelse"; neither key acts while typing, and digits type instead of
## setting the speed.
func _test_typing_hotkeys_and_close_buttons() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	ui.show_tab("encyclopedia")
	await get_tree().process_frame
	var search: LineEdit = ui.find_child("Panel_encyclopedia", true, false).get_node("%SearchField")
	search.grab_focus()
	search.text = ""
	_sim.set_paused(true)
	await _press_key(KEY_V, 118)
	await _press_key(KEY_N, 110)
	await _press_key(KEY_4, 52)
	_check(search.text == "vn4", "keys: V, N and 4 type into a focused text field ('%s')" % search.text)
	_check(not center._is_open, "keys: V while typing leaves the notification panel shut")
	_check(_sim.is_paused() and not _sim.is_running_to_next_event(), "keys: N and 4 while typing leave the clock alone")
	get_viewport().gui_release_focus()
	await _press_key(KEY_V, 118)
	_check(center._is_open, "keys: V outside a text field opens the notifications")
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

## S2-02: each scenario's zones lie in their production area and reach the game
## with the area's water; the HUD temperature is the home area's, and in August
## Vestland's PO3 is far warmer than Finnmark's PO12 (same seed, same anomaly).
func _test_zones_report_their_production_area_water() -> void:
	_check(_sim.start_qa_scenario("krise_manager", 31), "areas: Finnmark scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(225)
	var north: Array = _sim.get_zone_traffic_lights()
	_check(north.size() == 1 and int(north[0].get("production_area", 0)) == 12,
		"areas: krise_manager's zone lies in PO12 (%s)" % [north])
	var north_temp := float(north[0].get("water_temp_c", 0.0)) if north.size() > 0 else 0.0
	_check(is_equal_approx(north_temp, float(_sim.get_current_temperature())),
		"areas: the HUD temperature is the home area's water (%.2f vs %.2f)" % [north_temp, float(_sim.get_current_temperature())])
	_check(_sim.start_qa_scenario("po3_red_sone", 31), "areas: PO3 scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(225)
	var south: Array = _sim.get_zone_traffic_lights()
	var areas: Array = south.map(func(z): return int(z.get("production_area", 0)))
	_check(areas == [3, 3], "areas: po3_red_sone's zones lie in PO3 (%s)" % [areas])
	var south_temp := float(south[0].get("water_temp_c", 0.0)) if south.size() > 0 else 0.0
	_check(south_temp > north_temp + 2.5,
		"areas: mid-August PO3 water (%.1f C) is warmer than PO12 (%.1f C)" % [south_temp, north_temp])

## S1-03 (owner decision D16): each scenario's bankruptcy floor and the warning
## ladder reach the game; achievements carry a medal and tutorial steps no cash
## through the signal chain (GameBridge -> SimBridge -> UI); the game-over reason
## is a translation key.
## S2-05: the mortality ledger reaches the facade for the P&L story — deaths
## by cause in the NVI categories that sum to the total, nb labels by key,
## the fish that left alive, and the last-N-days window.
func _test_mortality_ledger_reaches_the_bridge() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "mortality: seeded scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(60)
	var b: Dictionary = _sim.get_mortality_breakdown()
	var categories: Array = b.get("categories", [])
	var causes: Array = b.get("causes", [])
	var total := int(b.get("total_deaths", -1))
	var codes: Array = categories.map(func(r: Dictionary) -> String: return str(r.get("nvi_code", "?")))
	_check(bool(b.get("success", false)) and str(b.get("scope", "")) == "company"
			and codes == ["A", "B", "C", "D", "", "E", "F"],
		"mortality: the company breakdown lists the NVI categories (%s)" % str(codes))
	var sum_categories := 0
	for r in categories:
		sum_categories += int(r.get("fish", 0))
	var sum_causes := 0
	for r in causes:
		sum_causes += int(r.get("fish", 0))
	_check(total > 0 and sum_categories == total and sum_causes == total,
		"mortality: categories and causes sum to the deaths (%d / %d / %d)" % [sum_categories, sum_causes, total])
	var alive_rows: Array = b.get("left_alive", [])
	var untranslated: Array = []
	for r in categories + causes + alive_rows:
		var key := str(r.get("label_key", ""))
		if key.is_empty() or tr(key) == key:
			untranslated.append(key)
	_check(untranslated.is_empty(), "mortality: every label key has Norwegian text (%s)" % str(untranslated))
	var alive: Array = alive_rows.map(func(r: Dictionary) -> String: return str(r.get("id", "")))
	_check(alive == ["escape", "harvest", "forced_harvest", "transfer_out"],
		"mortality: escapes and harvests are listed as leaving alive, not as deaths (%s)" % str(alive))
	var month: Dictionary = _sim.get_mortality_breakdown("", 30)
	var day: int = _sim.current_day
	_check(str(month.get("scope", "")) == "days" and int(month.get("to_day", -1)) == day
			and int(month.get("from_day", -1)) == day - 29
			and int(month.get("total_deaths", -1)) <= total,
		"mortality: the last-30-days window ends today (%s)" % str([month.get("from_day"), month.get("to_day")]))

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
			# U2-06 (stronger than "in the corner", RUN_LOG §7): the newest toast
			# is right-aligned in the corner's column and as low as it can be
			# without covering a keep-clear control (Hurtigvalg): in the corner
			# itself, or resting just above such a control. None covers one.
			var keep_clear: Array = toasts.keep_clear_rects()
			var resting := absf(newest.end.y - corner.y) < 2.0
			for k: Rect2 in keep_clear:
				if k.position.x < newest.end.x and k.end.x > newest.position.x \
						and absf(newest.end.y + toasts.KEEP_CLEAR_GAP - k.position.y) < 2.0:
					resting = true
			var covering := rects.filter(func(r: Rect2) -> bool:
				return keep_clear.any(func(k: Rect2) -> bool: return r.intersects(k, true)))
			_check(rects.size() == 3 and absf(newest.end.x - corner.x) < 2.0 and resting and covering.is_empty(),
				"regions: five toasts show as three, newest bottom-right in the corner or just above Hurtigvalg, covering nothing, at %s (%d, %s)" % [where, rects.size(), str(newest)])
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
	# U2-07 #1: the picker's Start is a player's new game: paused on its briefing.
	_check(_sim.is_briefing_pending() and _sim.is_paused() and _sim.is_clock_held("briefing"),
		"mods: the new game waits paused on its briefing")
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
	var locked_ids: Array[String] = []
	for e: Dictionary in entries:
		var id := str(e["id"])
		var card: Button = picker.get_card(id)
		var capital: Label = card.find_child("Capital", true, false) if card != null else null
		var want := SimFormatters.format_nok(float(e.get("starting_capital", 0.0)))
		if capital == null or capital.text != want:
			wrong_capital.append("%s: '%s' ≠ '%s'" % [id, capital.text if capital else "?", want])
		var is_locked := not bool(e.get("unlocked", true))
		locked += 1 if is_locked else 0
		if is_locked:
			locked_ids.append(id)
		if card == null or (card.find_child("LockTag", true, false) != null) != is_locked:
			wrong_lock.append(id)
		if card == null or (card.find_child("ModTag", true, false) != null) != bool(e.get("is_mod", false)):
			wrong_mod.append(id)
	_check(wrong_capital.is_empty(), "picker: every card shows its starting balance formatted %s" % str(wrong_capital))
	var first: Button = picker.get_card("forste_sesong")
	var first_capital: Label = first.find_child("Capital", true, false) if first != null else null
	# S2-04 raised Familiegården's capital from 3 000 000 to 4 000 000 (scenario data, RUN_LOG §7).
	_check(first_capital != null and first_capital.text == SimFormatters.format_nok(4_000_000.0),
		"picker: Familiegården starts with 4 000 000 (it read a tenth of its capital)")
	_check(wrong_lock.is_empty(), "picker: a padlock only on a locked card %s" % str(wrong_lock))
	# U2-03 replaces U1-07's "no scenario is locked until progression lands"
	# (RUN_LOG section 7) with the campaign's rule: a fresh campaign locks
	# exactly the scenarios its graph does not open from the start, and only
	# those; a locked card's Start stays off.
	var open_from_start: Array = CampaignProgress.graph().get("open_from_start", [])
	var want_locked: Array[String] = []
	for id in CampaignProgress.order():
		if not open_from_start.has(id):
			want_locked.append(id)
	locked_ids.sort()
	want_locked.sort()
	_check(locked == want_locked.size() and locked_ids == want_locked and want_locked.size() == 7,
		"picker: a fresh campaign locks exactly its 7 later scenarios %s" % str(locked_ids))
	if not locked_ids.is_empty():
		picker.select(locked_ids[0])
		_check(picker.get_start_button().disabled and picker.get_start_button().tooltip_text != "",
			"picker: a locked scenario's Start is off and says why")
		picker.select("forste_sesong")
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
	# G2-04 replaced "the site has a pen cage" (one PenCage per site, named
	# after it): the site has as many pen cages as the engine gives it.
	var engine_pens := int(_sim.get_all_pens()[0].get("pen_count", -1)) if not _sim.get_all_pens().is_empty() else -1
	var cages := pens.get_site_cages(site_id)
	var cage: PenCage = cages[0] if not cages.is_empty() else null
	_check(cage != null and engine_pens > 1 and cages.size() == engine_pens,
		"pen models: site %s has its %d pen cages (%d drawn)" % [site_id, engine_pens, cages.size()])
	if cage == null:
		scene.queue_free()
		return

	var lids := 0
	for c in cages:
		lids += _net_lid_faces(c)
	_check(lids == 0, "pen models: no pen's net has a lid (%d top faces)" % lids)

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
	# Each pen's ring shows its own fish (S2-01b): a stocked pen the site's
	# lice state, an empty pen the no-fish state, never the site's lice.
	var site_pens: Array = []
	for s in _sim.get_all_pens():
		if str(s.get("id", "")) == site_id:
			site_pens = s.get("pens", [])
	var rings_red := true
	var empty_neutral := true
	var stocked := 0
	for k in cages.size():
		var r := cages[k].get_health_ring()
		var occupied: bool = k < site_pens.size() and bool((site_pens[k] as Dictionary).get("occupied", false))
		if occupied:
			stocked += 1
			rings_red = rings_red and r != null and r.visible and r.get_instance_shader_parameter("ring_color") == red
		else:
			empty_neutral = empty_neutral and r != null \
				and r.get_instance_shader_parameter("ring_color") == PenCage.health_color_of(PenCage.Health.UNKNOWN)
	_check(ring != null and stocked > 0 and rings_red,
		"pen models: the health ring on the water of every stocked pen of the site shows the site state (%d of %d stocked)" % [stocked, cages.size()])
	_check(site_pens.size() == cages.size() and empty_neutral,
		"pen models: an empty pen's ring shows the no-fish state, not the site's lice (%d pens)" % site_pens.size())
	_check(_delta_e76(healthy, red) >= 20.0, "pen models: healthy vs red ring dE %.1f >= 20" % _delta_e76(healthy, red))
	var collar_after := _albedo_of(cage.get_node("Collar") as MeshInstance3D)
	_check(collar_after == collar_before and _linear_luminance(collar_after) < 0.1,
		"pen models: the collar stays black HDPE (luminance %.3f) whatever the lice" % _linear_luminance(collar_after))

	# G2-04: one name per site, over its grid (it was on the site's one
	# pen), and a number on each pen.
	var label := pens.get_site_label(site_id)
	var farm := pens.get_site(site_id)
	_check(label != null and label.billboard == BaseMaterial3D.BILLBOARD_ENABLED and label.fixed_size
		and label.position.y > 3.0 and Vector2(label.position.x, label.position.z).length() < 0.01
		and farm.find_children("Label", "Label3D", true, false).size() == 1,
		"pen models: the site label is one constant-size billboard above the grid's centre")
	var numbered := true
	for k in cages.size():
		var number := cages[k].get_node_or_null("Number") as Label3D
		numbered = numbered and number != null and number.text == str(k + 1) and number.fixed_size \
			and number.visibility_range_end > 0.0
	_check(numbered, "pen models: every pen carries its number (1-%d), shown up close" % cages.size())

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
	# G2-04: a site is a grid of pens (it was one PenCage named after it).
	var cages := pens.get_site_cages(site_id)
	var cage: PenCage = cages[0] if not cages.is_empty() else null
	_check(cage != null and cages.size() == int(_sim.get_all_pens()[0].get("pen_count", -1)),
		"hero pen: site %s has its %d pens" % [site_id, cages.size()])
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
	# A pen on its own hands its LODs over by camera distance; in a site the
	# site's MultiMeshes draw LOD1 and LOD2 (SiteFlowChecks: the hand-over).
	var lone := (load("res://assets/models/pen_cage/pen_cage.tscn") as PackedScene).instantiate() as PenCage
	add_child(lone)
	var lods_chain := true
	for level in [1, 2]:
		for mesh in lone.get_lod_meshes(level):
			lods_chain = lods_chain and is_equal_approx(mesh.visibility_range_begin, PenCage.LOD_SWITCH[level - 1]) \
				and mesh.cast_shadow == GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	for mesh in lone.get_lod_meshes(0):
		lods_chain = lods_chain and is_equal_approx(mesh.visibility_range_end, PenCage.LOD_SWITCH[0])
	lone.free()
	_check(lods_chain, "hero pen: LOD0 -> LOD1 -> LOD2 hand over by camera distance, nothing past LOD0 casts a shadow")
	var collar := (cage.get_node("Collar") as MeshInstance3D).get_aabb()
	var net := (cage.get_node("Net") as MeshInstance3D).get_aabb()
	_check(absf(collar.size.x - 53.63) / 53.63 <= 0.05 and absf(collar.size.z - 53.63) / 53.63 <= 0.05,
		"hero pen: collar %.2f m across, within 5%% of the 160 m Polarcirkel collar" % collar.size.x)
	_check(-net.position.y >= 20.0 and -net.position.y <= 40.0 * 1.05,
		"hero pen: the net hangs %.1f m deep (20-40 m with a cone bottom)" % -net.position.y)

	var no_skirt := true
	for c in cages:
		no_skirt = no_skirt and not c.has_lice_skirt() and c.get_parts("LiceSkirt").all(func(m: MeshInstance3D) -> bool: return not m.visible)
	_check(no_skirt, "hero pen: no lice skirt on any pen before the site buys one")
	_check(_sim.toggle_mitigation(site_id, "skirt"), "hero pen: the site buys a lice skirt")
	_sim.state_updated.emit()
	await get_tree().process_frame
	var skirted := true
	for c in cages:
		skirted = skirted and c.has_lice_skirt() and c.get_parts("LiceSkirt").size() == 3 \
			and c.get_parts("LiceSkirt").all(func(m: MeshInstance3D) -> bool: return m.visible)
	var site_skirts: Array = []
	for level in [FarmSite.Lod.MID, FarmSite.Lod.FAR]:
		site_skirts.append(pens.get_site(site_id).get_lod_multimeshes(level).get("LiceSkirt"))
	_check(skirted and not site_skirts.has(null),
		"hero pen: the skirt variant shows on every LOD of every pen of the site (and its LOD1/LOD2 MultiMeshes)")

	# G2-04 replaced "the pen lies in one 70-100 m frame mooring cell": the
	# site's pens lie one to a cell of a grid of 70-100 m cells.
	var grid := pens.get_pen_mooring(site_id)
	var n := cages.size()
	var dims := SiteLayout.grid_dims(n)
	_check(grid != null and grid.cells_x == dims.x and grid.cells_z == dims.y and dims.x * dims.y >= n
		and MooringGrid.CELL_M >= 70.0 and MooringGrid.CELL_M <= 100.0,
		"site kit: the %d pens lie in a %d x %d frame mooring of %.0f m cells (70-100 m)" % [n, dims.x, dims.y, MooringGrid.CELL_M])
	if grid != null:
		var nodes := (dims.x + 1) * (dims.y + 1)
		_check(grid.buoy_positions.size() == nodes and grid.ropes_of("bridle").size() == 8 * n
			and grid.ropes_of("anchor").size() == 2 * (dims.x + dims.y) + 4
			and grid.ropes_of("frame").size() == dims.x * (dims.y + 1) + dims.y * (dims.x + 1),
			"site kit: %d grid buoys, %d bridles, %d anchor lines, %d frame lines" % [grid.buoy_positions.size(),
				grid.ropes_of("bridle").size(), grid.ropes_of("anchor").size(), grid.ropes_of("frame").size()])
		var buoys_drawn := not grid.get_buoy_instances().is_empty()
		for mmi in grid.get_buoy_instances():
			buoys_drawn = buoys_drawn and mmi.multimesh.instance_count == nodes
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
		var rn: int = farm.cages.size()
		var rd := SiteLayout.grid_dims(rn)
		var rg: MooringGrid = farm.get("mooring")
		rival_moorings = rival_moorings and rg != null and rn >= 2 and rg.cells_x == rd.x and rg.cells_z == rd.y \
			and rg.buoy_positions.size() == (rd.x + 1) * (rd.y + 1) and rg.ropes_of("bridle").size() == 8 * rn
	_check(rival_moorings, "site kit: every rival farm lies in its own frame mooring, one cell per pen")
	scene.queue_free()
	await get_tree().process_frame

## G2-04: multi-pen sites in a mooring grid, their feed hoses, clicks, pen
## LODs and floating gear (tests/SiteFlowChecks.gd).
func _test_multi_pen_sites() -> void:
	var checks: RefCounted = (load("res://tests/SiteFlowChecks.gd") as GDScript).new()
	await checks.run(self)

## G3-01: salmon schools on the GPU (per-fish data written once, counts by
## biomass, feeding and oxygen in the uniforms, cost) and the farm's lights
## after dark (tests/FishFlowChecks.gd).
func _test_fish_schools_and_night_lights() -> void:
	var checks: RefCounted = (load("res://tests/FishFlowChecks.gd") as GDScript).new()
	await checks.run(self)

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
		# Farm-types spec §5.2 (F2-27): the offshore fleet's surface vessels.
		"offshore_service/offshore_service": [75.0, 17.0, [30000, 6000, 800]],
		"crew_boat/crew_boat": [20.0, 7.0, [8000, 1500, 300]],
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
	var site_cages := pens.get_site_cages(site_id)
	var cage: PenCage = site_cages[0] if not site_cages.is_empty() else null
	_check(cage != null, "vessels: site %s has its pens (%d)" % [site_id, site_cages.size()])
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
	# G2-04 replaced "beside its pen (< 80 m)": the barge lies beside its
	# site's grid, its inner hull side BARGE_GRID_GAP_M outside the outer
	# frame line, square off the hose gate, feed side to the pens.
	var feed_side_ok := barge != null
	var farm := pens.get_site(site_id)
	var gap := -1.0
	if barge != null and farm != null:
		var to_site := pens.get_pen_position(site_id) - barge.global_position
		var local := farm.to_local(barge.global_position)
		gap = absf(local.x) - barge.beam_m * 0.5 - farm.half_extent().x
		feed_side_ok = barge.global_transform.basis.x.dot(to_site) > 0.0 \
			and absf(gap - FishPenManager.BARGE_GRID_GAP_M) < 1.0 \
			and absf(local.z - SiteBerths.barge_gate_z(farm.pen_count)) < 1.0
	_check(feed_side_ok, "vessels: the moored barge lies square off its site's hose gate (%.1f m off the frame), feed side to the pens" % gap)
	var tender := barge.get_node_or_null("Tender") as VesselModel if barge else null
	var tender_ok := tender != null and tender.asset_id == "rib"
	if tender_ok:
		var away := barge.to_local(pens.get_pen_position(site_id)).x * tender.position.x
		tender_ok = away < 0.0 and absf(tender.position.x) > barge.beam_m * 0.5 + tender.beam_m * 0.5
	_check(tender_ok, "vessels: the barge keeps its tender RIB alongside, on the side away from the pens")
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

## F2-18: the gray-box campus stands on the spec's plots with the spec's
## lots, nothing on a pad overlaps, and the gray box draws it the way F2-20
## will (one MultiMesh per tank type per plot, statics merged).
func _test_farm_type_graybox() -> void:
	var layout := load("res://scripts/qa/LandCampusLayout.gd")
	var builder := load("res://scripts/qa/GrayBoxBuilder.gd")
	var runner := load("res://scripts/qa/FarmTypeGrayBox.gd")
	_check(layout.tank_count("pad_a") == 64 and layout.tank_count("pad_a", "tank_growout") == 48,
		"farm types: pad_a holds 4 full lots and 4 post-smolt modules (%d tanks, 64 expected)" % layout.tank_count("pad_a"))
	_check(layout.tank_count("pad_b") == 28 and layout.tank_count("pad_delta") == 8,
		"farm types: pad_b holds 2 lots (%d tanks) and the delta 4 modules (%d tanks)" % [layout.tank_count("pad_b"), layout.tank_count("pad_delta")])
	# One full lot: two rows of 6 grow-out tanks 4 m apart (188 m) and a
	# pre-grow tank at each row end, about 208 x 90 m.
	var lo := Vector2(INF, INF)
	var hi := Vector2(-INF, -INF)
	var row_x: Array[float] = []
	for p in layout.plot_placements("pad_a"):
		if int(p["lot"]) != 0 or not str(p["asset"]).begins_with("tank_"):
			continue
		var pos: Vector3 = p["pos"]
		var r: float = (p["size"] as Vector3).x * 0.5
		lo = Vector2(minf(lo.x, pos.x - r), minf(lo.y, pos.z - r))
		hi = Vector2(maxf(hi.x, pos.x + r), maxf(hi.y, pos.z + r))
		if p["asset"] == "tank_growout" and is_equal_approx(pos.z, (layout.plot_placements("pad_a")[0]["pos"] as Vector3).z):
			row_x.append(pos.x)
	var row_len: float = (row_x.max() - row_x.min()) + 28.0 if not row_x.is_empty() else 0.0
	_check(absf(row_len - 188.0) < 0.01 and absf(hi.x - lo.x - 208.0) < 0.5 and absf(hi.y - lo.y - 90.0) < 0.5,
		"farm types: a lot is %.0f x %.0f m with a %.0f m row (208 x 90, 188)" % [hi.x - lo.x, hi.y - lo.y, row_len])
	var clashes: Array[String] = []
	var outside: Array[String] = []
	for plot_id in ["pad_a", "pad_b", "pad_delta"]:
		var placements: Array = layout.plot_placements(plot_id)
		var pad: Dictionary = layout.PLOTS[plot_id]
		var c: Vector3 = layout.plot_centre(plot_id)
		var half: Vector2 = (pad["size"] as Vector2) * 0.5
		for i in placements.size():
			var a: Dictionary = placements[i]
			var ra := _footprint(a)
			if not (str(a["asset"]) in ["quay", "intake_basin", "pipe_bridge_span"]):
				if ra.position.x < c.x - half.x - 0.01 or ra.end.x > c.x + half.x + 0.01 or ra.position.y < c.z - half.y - 0.01 or ra.end.y > c.z + half.y + 0.01:
					outside.append("%s %s" % [plot_id, a["asset"]])
			for j in range(i + 1, placements.size()):
				var b: Dictionary = placements[j]
				var pair := [str(a["asset"]), str(b["asset"])]
				if pair.has("pipe_bridge_span") and (pair.has("quay") or pair.count("pipe_bridge_span") == 2):
					continue  # the bridge starts on the quay; its spans join
				if pair.count("process_hall_bay") == 2:
					continue  # hall bays are built end to end
				if ra.grow(-0.25).intersects(_footprint(b).grow(-0.25)):
					clashes.append("%s %s/%s" % [plot_id, a["asset"], b["asset"]])
	_check(clashes.is_empty(), "farm types: no two campus pieces overlap %s" % str(clashes.slice(0, 4)))
	_check(outside.is_empty(), "farm types: every building stands on its pad %s" % str(outside.slice(0, 4)))

	var host := Node3D.new()
	add_child(host)
	var box: Node3D = builder.build(host, {"pad_a": 0})
	var names: Array[String] = []
	for child in box.get_children():
		names.append(str(child.name))
	_check(names == ["pad_a", "pad_b", "pad_delta", "off_1", "off_2"], "farm types: the gray box covers every plot %s" % str(names))
	var growout := box.get_node_or_null("pad_a/Growout") as MultiMeshInstance3D
	var pregrow := box.get_node_or_null("pad_a/Pregrow") as MultiMeshInstance3D
	var instanced := (growout.multimesh.instance_count if growout else 0) + (pregrow.multimesh.instance_count if pregrow else 0)
	_check(instanced == 63 and box.get_node_or_null("pad_a/SampleWall") != null and box.get_node_or_null("pad_a/SampleLid") != null,
		"farm types: pad_a draws 63 instanced tanks plus its sample tank (%d)" % instanced)
	var draws := {}
	var classes_ok := true
	for gi in box.find_children("*", "GeometryInstance3D", true, false):
		var plot := str(gi.get_parent().name)
		draws[plot] = int(draws.get(plot, 0)) + 1
		classes_ok = classes_ok and str(gi.get_meta("graybox_class", "")).begins_with(plot)
	_check(classes_ok, "farm types: every gray-box node names its plot's mask class")
	_check(int(draws.get("pad_b", 0)) == 4 and int(draws.get("pad_a", 0)) == 6 and int(draws.get("off_1", 0)) == 1,
		"farm types: one pad is 4 draws (pad, 2 tank MultiMeshes, merged statics), a unit 1: %s" % str(draws))
	var unit := box.get_node_or_null("off_1/Unit") as MeshInstance3D
	var aabb := unit.get_aabb() if unit else AABB()
	_check(aabb.size.x > 110.0 and aabb.size.x < 118.0 and absf(aabb.end.y - 25.0) < 1.0,
		"farm types: the OF1 ring is %.1f m across (Ø110 + columns) and tops out at %.1f m" % [aabb.size.x, aabb.end.y])
	host.queue_free()
	var shots: Array = runner.load_shots()
	_check(shots.size() >= 8 and shots.all(func(s: Dictionary) -> bool: return str(s.get("item", "")) == "F2-18"),
		"farm types: shots.json gives the gray-box scene its %d shots" % shots.size())


## F2-25: every land-kit LOD imports as one mesh with one surface that uses
## the kit's single material (the shader reads the trim atlas), within the
## spec §5.1 triangle budgets, so F2-20 can instance a campus per tank type.
func _test_land_kit_imports() -> void:
	const KIT := "res://assets/models/land_kit/"
	const BUDGETS := {
		"tank_growout": [2400, 600, 96], "tank_pregrow": [1600, 400, 64], "tank_feeder": [220, 60],
		"process_hall_bay": [900, 160, 12], "harvest_hall": [3000, 450, 24], "pipe_bridge_span": [1800, 320, 24],
		"quay": [2500, 400, 30], "intake_basin": [2200, 380, 40], "river_intake": [600, 120, 12],
		"lox_tank": [640, 120, 12], "genset_container": [320, 60, 12], "substation": [1500, 220, 12],
		"feed_silo_land": [900, 160, 12], "sludge_station": [800, 150, 12],
	}
	var kit_mat := load("res://assets/models/facility_kit/LK_FacilityKit.tres") as ShaderMaterial
	_check(kit_mat != null and kit_mat.shader != null and kit_mat.shader.resource_path == "res://shaders/facility_kit.gdshader"
		and kit_mat.get_shader_parameter("atlas") is Texture2D,
		"land kit: LK_FacilityKit is the facility-kit shader reading the trim atlas")
	var lods := 0
	var bad: Array[String] = []
	for asset in BUDGETS:
		var budgets: Array = BUDGETS[asset]
		for i in budgets.size():
			var path := "%s%s_LOD%d.glb" % [KIT, asset, i]
			var packed := load(path) as PackedScene
			if packed == null:
				bad.append("%s missing" % path)
				continue
			var root := packed.instantiate()
			var meshes := root.find_children("*", "MeshInstance3D", true, false)
			var mesh: Mesh = (meshes[0] as MeshInstance3D).mesh if meshes.size() == 1 else null
			if mesh == null or mesh.get_surface_count() != 1:
				bad.append("%s: %d meshes" % [path, meshes.size()])
			elif mesh.surface_get_material(0) == null or mesh.surface_get_material(0).resource_path != kit_mat.resource_path:
				bad.append("%s: not LK_FacilityKit" % path)
			elif _mesh_triangles(mesh) > int(budgets[i]):
				bad.append("%s: %d tris > %d" % [path, _mesh_triangles(mesh), int(budgets[i])])
			else:
				lods += 1
			root.free()
	_check(bad.is_empty() and lods == 41,
		"land kit: %d LODs are one LK_FacilityKit surface inside the spec budgets %s" % [lods, str(bad.slice(0, 3))])


## F2-26/F2-27 (farm-types spec §5.2): the OF1-class unit and the offshore
## fleet import with their nodes, budgets, LOD chains and materials, and the
## unit's yard reveal, crane slew and beacon answer.
func _test_offshore_unit_and_fleet() -> void:
	var unit := (load("res://assets/models/offshore/of1_class.tscn") as PackedScene).instantiate() as OffshoreUnitModel
	_check(unit != null, "offshore: of1_class.tscn is an OffshoreUnitModel")
	if unit == null:
		return
	add_child(unit)
	var missing: Array[String] = []
	for path in ["Hull", "Columns", "Bracing", "Topsides", "Net", "Bulkhead", "NavLights", "Cranes/CraneA",
			"Cranes/CraneB", "Beacon", "W2WLanding", "LOD1/Hull", "LOD1/Net", "LOD2/Hull", "LOD2/Net"]:
		if unit.get_node_or_null(path) == null:
			missing.append(path)
	_check(missing.is_empty(), "offshore: the unit has the spec's fixed nodes, Beacon included (missing %s)" % str(missing))
	var budgets := [60000, 12000, 1500]
	var tris: Array[int] = []
	for level in 3:
		var t := 0
		for mesh in unit.get_lod_meshes(level):
			t += _mesh_triangles(mesh.mesh)
		tris.append(t)
	_check(tris[0] <= budgets[0] and tris[1] <= budgets[1] and tris[2] <= budgets[2] and tris[0] > tris[1] and tris[1] > tris[2] and tris[2] > 0,
		"offshore: LOD0/LOD1/LOD2 inside 60k/12k/1.5k triangles, stepping down (%s)" % str(tris))
	var chain_ok := true
	var shadow_ok := true
	var mats_ok := true
	for level in 3:
		for mesh in unit.get_lod_meshes(level):
			var begin: float = [0.0, 500.0, 2000.0][level]
			var end: float = [500.0, 2000.0, 0.0][level]
			chain_ok = chain_ok and is_equal_approx(mesh.visibility_range_begin, begin) and is_equal_approx(mesh.visibility_range_end, end)
			var casts := mesh.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			shadow_ok = shadow_ok and casts == (level == 0 and not (String(mesh.name) in ["Net", "NavLights"]))
			for s in mesh.mesh.get_surface_count():
				var mat := mesh.mesh.surface_get_material(s) as ShaderMaterial
				var net := mat != null and mat.resource_name == "LK_OffshoreNet"
				var shader := "res://shaders/pen_net_shader.gdshader" if net else "res://shaders/offshore_kit.gdshader"
				mats_ok = mats_ok and mat != null and mat.shader.resource_path == shader
	_check(chain_ok, "offshore: the unit hands over LOD0 -> LOD1 -> LOD2 at 500 m and 2,000 m (spec §5.5 rule 4)")
	_check(shadow_ok, "offshore: only LOD0 solids cast shadows; the net and lanterns never do")
	_check(mats_ok, "offshore: solids use LK_OffshoreKit (offshore_kit.gdshader), nets LK_OffshoreNet (pen_net_shader)")
	var cols := (unit.get_node("Columns") as MeshInstance3D).get_aabb()
	var radius := (cols.size.x - 3.56) * 0.5
	var hull := (unit.get_node("Hull") as MeshInstance3D).get_aabb()
	_check(absf(radius - 55.0) <= 2.75 and absf(hull.position.y + 43.0) < 0.01,
		"offshore: 110 m ring (circumradius %.2f m), keel at the 43 m draft (%.2f m)" % [radius, hull.position.y])
	unit.set_build_stage(4.5)
	var hidden := not (unit.get_node("Net") as Node3D).visible and not (unit.get_node("Bulkhead") as Node3D).visible
	var param := float((unit.get_node("Hull") as GeometryInstance3D).get_instance_shader_parameter("build_stage"))
	unit.set_build_stage(6.0)
	var shown := (unit.get_node("Net") as Node3D).visible and (unit.get_node("LOD1/Net") as Node3D).visible
	_check(hidden and is_equal_approx(param, 4.5) and shown,
		"offshore: the yard reveal holds the net back until its stage and drives the kit per instance")
	var crane := unit.get_node("Cranes/CraneA") as Node3D
	var rest := crane.rotation.y
	unit.slew_crane("CraneA", 30.0)
	var slewed := is_equal_approx(crane.rotation.y - rest, deg_to_rad(30.0))
	unit.slew_crane("CraneA", 0.0)
	_check(slewed and unit.get_beacon_position().y > 27.0 and unit.get_w2w_landing().y > 15.0,
		"offshore: cranes slew on their pedestals; the beacon tops the mast; the gangway landing is on the deck")
	unit.free()

	var service := (load("res://assets/models/offshore_service/offshore_service.tscn") as PackedScene).instantiate() as VesselModel
	add_child(service)
	var gangway := service.get_node_or_null("Gangway") as MeshInstance3D
	_check(gangway != null and is_equal_approx(gangway.visibility_range_end, service.lod_switch.x)
		and service.length_m < 0.75 * 110.0,
		"offshore fleet: the %.0f m service vessel is visibly shorter than the 110 m ring and has its gangway node" % service.length_m)
	service.free()
	var tug := (load("res://assets/models/offshore_service/anchor_tug.tscn") as PackedScene).instantiate() as VesselModel
	add_child(tug)
	var red := true
	for part in tug.get_parts("Hull"):
		var m := part.get_surface_override_material(0)
		red = red and m != null and m.resource_name == "LK_VHullRed"
	_check(red and tug.get_parts("Hull").size() == 3,
		"offshore fleet: the anchor tug is the service vessel's mesh with a red hull at every LOD")
	tug.free()
	var props := {"subsea/rov": [2.6, 1.6, [1200, 300]], "subsea/auv": [4.0, 0.5, [600, 150]]}
	var props_ok := true
	var report := []
	for path in props:
		var v := (load("res://assets/models/%s.tscn" % path) as PackedScene).instantiate() as VesselModel
		add_child(v)
		var spec: Array = props[path]
		var aabb := (v.get_node("Hull") as MeshInstance3D).get_aabb()
		props_ok = props_ok and absf(aabb.size.z - spec[0]) / spec[0] <= 0.05 and absf(aabb.size.x - spec[1]) / spec[1] <= 0.05
		var t: Array[int] = []
		for level in 2:
			var n := 0
			for mesh in v.get_lod_meshes(level):
				n += _mesh_triangles(mesh.mesh)
			t.append(n)
			props_ok = props_ok and n > 0 and n <= int(spec[2][level])
		props_ok = props_ok and v.get_lod_meshes(2).is_empty()
		report.append("%s %.2fx%.2f %s" % [path.get_file(), aabb.size.z, aabb.size.x, t])
		v.free()
	_check(props_ok, "offshore fleet: ROV and AUV inside their size and two-LOD budgets (%s)" % ", ".join(report))


## Ground footprint (x, z) of a layout piece, its model axes turned by yaw.
func _footprint(p: Dictionary) -> Rect2:
	var s: Vector3 = p["size"]
	var pos: Vector3 = p["pos"]
	var turned := absf(sin(float(p["yaw"]))) > 0.5
	var ext := Vector2(s.z, s.x) if turned else Vector2(s.x, s.z)
	return Rect2(Vector2(pos.x, pos.z) - ext * 0.5, ext)


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

## P0-04 / G5-01 / G1-06: quality tiers through the EnvironmentDirector.
## The saved tier is the player's (High when none is saved); each tier's
## knobs reach the viewport (MSAA, SMAA, 3D scale), the environment (SSAO,
## glow), the camera (depth of field) and the sky (where its clouds are
## drawn); clouds composited from a reduced-resolution pass get the engine's
## sky fog taken back out with the fog the director wrote; behind an opaque
## panel the 3D renders at half scale and the sky stops updating, and the
## world on screen (or a hidden UI layer) restores both; the terrain's
## pipelines are warmed with its chunks' own vertex format on the first
## frames; the settings menu's quality list applies a tier at once.
func _test_quality_tiers_and_covered_world() -> void:
	var scene := await _spawn_game_scene()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var env: FjordEnvironment = scene.get_node("FjordEnvironment")
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var world_env: Environment = (env.get_node("WorldEnvironment") as WorldEnvironment).environment
	var sky_mat := world_env.sky.sky_material as ShaderMaterial
	var vp := get_viewport()
	cam.auto_orbit_enabled = false

	# The tier: the saved one, High when none is saved; a settings file's shape.
	_check(RenderQuality.DEFAULT_TIER == RenderQuality.Tier.HIGH and RenderQuality.tier_from_settings({}) == RenderQuality.Tier.HIGH
		and RenderQuality.tier_from_settings({"graphics": {"quality": "ultra"}}) == RenderQuality.Tier.ULTRA
		and RenderQuality.tier_from_settings({"graphics": {"quality": "nonsense"}}) == RenderQuality.Tier.HIGH
		and RenderQuality.tier_from_settings("not a dictionary") == RenderQuality.Tier.HIGH,
		"quality: High is the default tier; a saved graphics.quality picks another, anything else falls back to High")
	_check(director.get_quality_name() == RenderQuality.NAMES[RenderQuality.load_saved_tier()],
		"quality: the director starts on the saved tier (%s)" % director.get_quality_name())
	_check(scene.get_tree().get_nodes_in_group(RenderQuality.DIRECTOR_GROUP).has(director), "quality: the director joins the group the settings menu applies tiers through")

	# The terrain's pipelines were warmed on the first frames, with its chunks' vertex format.
	_check(director.prewarmed_shaders.has(FjordTerrain.TERRAIN_SHADER) and not is_instance_valid(director.prewarm),
		"quality: the terrain shader was warmed on the first frames and the warm-up has freed itself")
	for i in 600:
		if terrain.is_ready():
			break
		await get_tree().process_frame
	var chunks := terrain.get_chunk_meshes()
	var warm_format := _vertex_layout(ShaderPrewarm.triangle_mesh(ShaderPrewarm.TERRAIN_FORMAT))
	var chunk_format := _vertex_layout(chunks[0].mesh if not chunks.is_empty() else null)
	_check(warm_format == chunk_format and warm_format > 0,
		"quality: the warm-up triangle carries the terrain chunks' vertex arrays (%d vs %d)" % [warm_format, chunk_format])
	var arrived_mat := chunks[0].material_override as ShaderMaterial if not chunks.is_empty() else null
	_check(arrived_mat != null and arrived_mat.get_shader_parameter(RenderQuality.TERRAIN_DETAIL_UNIFORM) != null
		and is_equal_approx(float(arrived_mat.get_shader_parameter(RenderQuality.TERRAIN_DETAIL_UNIFORM)), float(director.get_quality_knobs()["terrain_detail"])),
		"quality: the terrain's material gets the tier's detail level as its chunks arrive")

	# Every tier reaches the viewport, the environment, the camera and the sky.
	ui.show_tab("fjord")
	await get_tree().process_frame
	_check(director.is_world_visible(), "quality: the Fjord tab shows the world")
	for tier_name in RenderQuality.NAMES:
		director.set_quality(tier_name)
		await get_tree().process_frame
		var k: Dictionary = director.get_quality_knobs()
		var attrs := cam.attributes as CameraAttributesPractical
		_check(director.get_quality_name() == tier_name and vp.msaa_3d == int(k["msaa"]) and vp.screen_space_aa == int(k["screen_aa"])
			and is_equal_approx(vp.scaling_3d_scale, float(k["scale"])) and world_env.ssao_enabled == bool(k["ssao"])
			and world_env.glow_enabled == bool(k["glow"]) and int(sky_mat.get_shader_parameter("cloud_pass")) == int(k["cloud_pass"])
			and int(sky_mat.get_shader_parameter("cloud_steps")) == int(k["cloud_steps"])
			and (bool(k["dof"]) or not attrs.dof_blur_far_enabled),
			"quality: %s reaches the viewport (MSAA %d, SMAA %d, scale %.2f), SSAO %s, glow, DOF and the sky (clouds in pass %d)" % [
				tier_name, vp.msaa_3d, vp.screen_space_aa, vp.scaling_3d_scale, str(world_env.ssao_enabled), int(k["cloud_pass"])])
		_check(vp.scaling_3d_scale >= 0.999 or vp.scaling_3d_mode == RenderQuality.upscaler(),
			"quality: %s upscales a reduced 3D scale with MetalFX or FSR, not bilinear" % tier_name)
		var terrain_mat := chunks[0].material_override as ShaderMaterial if not chunks.is_empty() else null
		_check(terrain_mat != null and is_equal_approx(float(terrain_mat.get_shader_parameter(RenderQuality.TERRAIN_DETAIL_UNIFORM)), float(k["terrain_detail"])),
			"quality: %s hands the terrain shader its detail level (%.1f)" % [tier_name, float(k["terrain_detail"])])
	director.set_quality("high")
	await get_tree().process_frame
	var high: Dictionary = director.get_quality_knobs()
	var ultra := RenderQuality.knobs_for(RenderQuality.Tier.ULTRA)
	_check(int(high["msaa"]) == Viewport.MSAA_4X and int(high["screen_aa"]) == Viewport.SCREEN_SPACE_AA_SMAA,
		"quality: High keeps MSAA 4x + SMAA (D12; the pen net resolves its threads with alpha-to-coverage)")
	_check(int(ultra["cloud_pass"]) == 0 and bool(ultra["ssao"]) and is_equal_approx(float(ultra["scale"]), 1.0),
		"quality: Ultra keeps the full-cost sky, SSAO and native scale (the reference High is judged against)")
	director.set_quality("ultra", {"cloud_pass": 1, "no_such_knob": 3})
	_check(int(director.get_quality_knobs()["cloud_pass"]) == 1 and not director.get_quality_knobs().has("no_such_knob"),
		"quality: an override sets a known knob and an unknown one is ignored")
	director.set_quality("nonsense")
	_check(director.get_quality_name() == "ultra", "quality: an unknown tier name keeps the tier in force")

	# Clouds from a reduced-resolution pass: the shader takes the engine's sky fog back out.
	director.set_quality("high")
	director.compose(0.0, true)
	var fog_k := world_env.fog_sky_affect * (1.0 - world_env.fog_aerial_perspective)
	var fog_s := world_env.fog_sky_affect * world_env.fog_sun_scatter
	_check(absf(float(sky_mat.get_shader_parameter("sky_fog_k")) - fog_k) < 1e-5 and absf(float(sky_mat.get_shader_parameter("sky_fog_scatter")) - fog_s) < 1e-5
		and (sky_mat.get_shader_parameter("haze_color") as Color).is_equal_approx(world_env.fog_light_color),
		"quality: the sky undoes exactly the fog the director wrote (k %.4f, scatter %.4f)" % [fog_k, fog_s])
	var zenith: Vector3 = sky_mat.get_shader_parameter("sky_zenith")
	_check(zenith.is_equal_approx(director.get_composed()["sky_zenith"]) and zenith.length() > 0.0,
		"quality: the sky reads the zenith colour the director measured, once a frame")

	# Behind an opaque panel: half scale, the sky holds still; back: native, live.
	ui.show_tab("finance")
	await get_tree().process_frame
	var updates := director.sky_updates
	var sky_time: float = sky_mat.get_shader_parameter("sky_time")
	for i in 10:
		await get_tree().process_frame
	_check(not director.is_world_visible() and is_equal_approx(vp.scaling_3d_scale, float(high["hidden_scale"]))
		and vp.scaling_3d_mode == Viewport.SCALING_3D_MODE_BILINEAR,
		"quality: an opaque panel drops the 3D to %.2f scale" % vp.scaling_3d_scale)
	_check(director.sky_updates == updates and is_equal_approx(float(sky_mat.get_shader_parameter("sky_time")), sky_time),
		"quality: behind an opaque panel the sky material holds still (no radiance re-render)")
	_check(not cam.input_enabled, "quality: camera input stays off behind the panel")
	var ui_layer: CanvasLayer = scene.get_node("UILayer")
	ui_layer.visible = false
	await get_tree().process_frame
	_check(director.is_world_visible() and is_equal_approx(vp.scaling_3d_scale, float(high["scale"])),
		"quality: with the UI layer hidden (the screenshot harness) the world renders at full quality")
	ui_layer.visible = true
	await get_tree().process_frame
	_check(not director.is_world_visible(), "quality: the UI layer back covers the world again")
	ui.show_tab("fjord")
	for i in 3:
		await get_tree().process_frame
	_check(director.is_world_visible() and is_equal_approx(vp.scaling_3d_scale, float(high["scale"])) and director.sky_updates > updates
		and not is_equal_approx(float(sky_mat.get_shader_parameter("sky_time")), sky_time),
		"quality: the Fjord tab restores the tier's scale and a live sky")

	# Underwater the clouds are marched per pixel (the fog replaces the sky).
	cam.look_at_position(cam.get_focus_point(), FjordCamera.ZoomLevel.UNDERWATER)
	await _settle_camera(cam)
	director.compose(0.0, true)
	_check(int(sky_mat.get_shader_parameter("cloud_pass")) == 0, "quality: underwater the sky marches its clouds itself")
	cam.set_zoom_level(FjordCamera.ZoomLevel.FJORD)
	await _settle_camera(cam)

	# The settings menu's quality list applies a tier at once (nothing saved here).
	var menu: Control = (load("res://scenes/ui/SettingsMenu.tscn") as PackedScene).instantiate()
	add_child(menu)
	await get_tree().process_frame
	var option: OptionButton = menu.get_node("%QualityOption")
	_check(option.item_count == RenderQuality.NAMES.size() and option.get_item_text(2) == "Høy" and option.get_item_text(0) == "Lav",
		"quality: the settings menu lists Lav, Middels, Høy and Ultra")
	option.select(3)
	option.item_selected.emit(3)
	await get_tree().process_frame
	_check(director.get_quality_name() == "ultra" and vp.msaa_3d == Viewport.MSAA_4X and is_equal_approx(vp.scaling_3d_scale, 1.0),
		"quality: choosing Ultra in the settings applies it to the running fjord")
	_check(str(menu._collect_from_widgets()["graphics"]["quality"]) == "ultra", "quality: the settings menu saves the chosen tier under graphics.quality")
	menu.set("_dirty", false)
	menu.queue_free()
	director.set_quality(RenderQuality.load_saved_tier())
	scene.queue_free()
	await get_tree().process_frame
	_check(is_equal_approx(vp.scaling_3d_scale, 1.0), "quality: leaving the game leaves the viewport at native 3D scale")


## G2-03 water v2. The planar mirror: the camera mirrored in the water plane
## is a proper rotation, and anything above the water lands in the mirror
## exactly where the water point that reflects it does; the water reads the
## very texture and matrix the mirror rendered; it updates every frame at
## FJORD and at 30 Hz at COAST on High, skips FARM distances on High (not on
## Ultra), and falls back to the sky's radiance on Low, underwater, behind a
## panel and in a storm. It leaves out the water, what hangs under it and
## world labels, lights the mirror with its own shadowless sun and moon (the
## player's camera never sees them), and draws its sky in its quarter pass.
## Wakes: a moving boat lays a trail whose Kelvin arms spread at 19.47 deg
## and fade; spray flies at the pens only in a gale.
func _test_water_mirror_wakes_and_spray() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "mirror: seeded scenario starts")
	_sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: FjordEnvironment = scene.get_node("FjordEnvironment")
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	var mirror: PlanarReflection = env.get_node("PlanarReflection")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var grid: WaterGrid = env.get_node("WaterGrid")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var weather: WeatherController = env.get_node("WeatherSystem")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var ui: Control = scene.get_node("UILayer/GameUI")
	var world_env := (env.get_node("WorldEnvironment") as WorldEnvironment).environment
	var mat := director.water_material()
	cam.auto_orbit_enabled = false
	vista.sim_weather_enabled = false
	weather.transition_duration = 0.0
	ui.show_tab("fjord")
	for i in 600:
		if terrain.is_ready():
			break
		await get_tree().process_frame
	var calm := {"weather_state": "Calm", "wind_ms": 1.5, "cloud_cover": 0.15, "visibility_km": 50.0}
	env.apply_sky_context(calm)
	director.set_quality("high")
	var focus: Vector3 = pens.get_pen_position(pens.get_pen_ids()[0])
	cam.look_at_position(focus, FjordCamera.ZoomLevel.FJORD)
	await _settle_camera(cam)
	for i in 3:
		await get_tree().process_frame
	_check(director.planar_reflection == mirror and mirror.camera == cam and mirror.water_grid == grid,
		"mirror: the director, the player's camera and the water are wired to the planar mirror")

	# The geometry: a proper rotation that looks along the mirrored view, and an
	# object above the water lands in the mirror where its reflection is.
	var proper := true
	var worst := 0.0
	var proj := Projection.create_perspective(45.0, 16.0 / 9.0, 0.05, 4000.0)
	for i in 24:
		var pos := Vector3(sin(i * 1.3) * 800.0, 30.0 + 70.0 * float(i % 5), cos(i * 0.9) * 800.0)
		var basis := Basis.from_euler(Vector3(deg_to_rad(-4.0 - float(i % 6) * 6.0), float(i) * 0.7, 0.0))
		var t := Transform3D(basis, pos)
		var m := PlanarReflection.mirror_transform(t, 0.0)
		var f := -t.basis.z
		proper = proper and m.basis.determinant() > 0.999 and is_equal_approx(m.origin.y, -pos.y) \
			and (-m.basis.z).is_equal_approx(Vector3(f.x, -f.y, f.z))
		var vp := proj * Projection(m.affine_inverse())
		var ahead := Vector3(f.x, 0.0, f.z).normalized()
		var q := pos + ahead * (500.0 + 90.0 * float(i % 4)) + Vector3(0.0, 120.0 + 40.0 * float(i % 3), 0.0) - Vector3(0.0, pos.y, 0.0)
		# The water point that mirrors q: where the line from the camera to q's
		# mirror image crosses the water.
		var q_image := Vector3(q.x, -q.y, q.z)
		var p := pos + (q_image - pos) * (pos.y / (pos.y - q_image.y))
		worst = maxf(worst, PlanarReflection.uv_of(vp, p).distance_to(PlanarReflection.uv_of(vp, q)))
	_check(proper and worst < 1e-3,
		"mirror: the mirrored camera is a proper rotation along the mirrored view, and an object above the water lands where the water mirrors it (worst %.6f uv)" % worst)
	var mirror_cam := mirror.get_mirror_camera()
	var live_vp := mirror_cam.get_camera_projection() * Projection(mirror_cam.get_camera_transform().affine_inverse())
	var bound_vp: Projection = mat.get_shader_parameter(PlanarReflection.VIEW_PROJ_UNIFORM)
	_check(mirror.renders > 0 and mat.get_shader_parameter(PlanarReflection.TEXTURE_UNIFORM) == mirror.get_texture()
		and _projection_close(bound_vp, mirror.view_proj) and _projection_close(mirror.view_proj, live_vp)
		and mirror_cam.global_position.is_equal_approx(PlanarReflection.mirror_transform(cam.global_transform, 0.0).origin),
		"mirror: the water reads the texture and the view-projection the mirror rendered with (%d passes)" % mirror.renders)
	var win := get_window().size
	_check(mirror.get_viewport_size().y <= int(ceil(win.y * float(director.get_quality_knobs()["mirror_scale"]))) + 1
		or mirror.get_viewport_size().y == PlanarReflection.MIN_HEIGHT_PX,
		"mirror: High renders it at %.2f of the window (%s)" % [float(director.get_quality_knobs()["mirror_scale"]), str(mirror.get_viewport_size())])

	# The cadence: every frame at FJORD, 30 Hz at COAST (High); none at FARM on
	# High, every frame there on Ultra.
	mirror.set_process(false)
	var at_fjord := _mirror_passes(mirror, 60)
	cam.look_at_position(focus, FjordCamera.ZoomLevel.COAST)
	await _settle_camera(cam)
	var at_coast := _mirror_passes(mirror, 60)
	_check(at_fjord == 60 and at_coast == 30, "mirror: High updates it every frame at FJORD (%d of 60) and at 30 Hz at COAST (%d of 60)" % [at_fjord, at_coast])
	cam.look_at_position(focus, FjordCamera.ZoomLevel.FARM)
	await _settle_camera(cam)
	var high_farm := _mirror_passes(mirror, 30)
	director.set_quality("ultra")
	var ultra_farm := _mirror_passes(mirror, 30)
	_check(high_farm == 0 and ultra_farm == 30 and is_zero_approx(float(RenderQuality.knobs_for(RenderQuality.Tier.ULTRA)["mirror_from_m"])),
		"mirror: High leaves FARM distances to the sky's radiance (%d passes), Ultra mirrors there too (%d)" % [high_farm, ultra_farm])
	director.set_quality("low")
	# 40 frames: the mirror fades out over 0.6 s on its last texture.
	var low_passes := _mirror_passes(mirror, 40)
	_check(low_passes == 0 and is_zero_approx(float(mat.get_shader_parameter(PlanarReflection.MIX_UNIFORM))),
		"mirror: Low has no mirror (the water falls back to the sky's radiance and the probe)")
	director.set_quality("high")
	cam.look_at_position(focus, FjordCamera.ZoomLevel.FJORD)
	await _settle_camera(cam)
	_check(_mirror_passes(mirror, 10) == 10, "mirror: back on High at FJORD it renders again")
	director.set_world_visible(false)
	var covered := _mirror_passes(mirror, 30)
	director.set_world_visible(true)
	env.apply_sky_context({"weather_state": "Storm", "wind_ms": 16.0, "cloud_cover": 1.0, "visibility_km": 2.0})
	director.compose(1.0 / 60.0)
	var storm_passes := _mirror_passes(mirror, 30)
	var storm_wanted: bool = director.get_composed().get("mirror_wanted", true)
	env.apply_sky_context(calm)
	director.compose(1.0 / 60.0)
	_check(covered == 0 and storm_passes == 0 and not storm_wanted and bool(director.get_composed().get("mirror_wanted", false)),
		"mirror: none behind an opaque panel or in a storm (no water calm enough to mirror), back in the calm")
	cam.look_at_position(focus, FjordCamera.ZoomLevel.UNDERWATER)
	await _settle_camera(cam)
	var under_passes := _mirror_passes(mirror, 40)
	_check(under_passes == 0 and is_zero_approx(float(mat.get_shader_parameter(PlanarReflection.MIX_UNIFORM))),
		"mirror: none with the camera under the water")
	cam.look_at_position(focus, FjordCamera.ZoomLevel.FJORD)
	await _settle_camera(cam)
	mirror.set_process(true)

	# What it sees and what lights it.
	var sun: DirectionalLight3D = env.get_node("Sun")
	var moon: DirectionalLight3D = env.get_node("Moon")
	_check(mirror_cam.cull_mask == PlanarReflection.mirror_cull_mask() and mirror_cam.cull_mask & WaterGrid.RENDER_LAYER == 0
		and mirror_cam.cull_mask & PlanarReflection.NO_MIRROR_LAYER == 0 and mirror_cam.cull_mask & PlanarReflection.MIRROR_ONLY_LAYER != 0
		and cam.cull_mask & PlanarReflection.MIRROR_ONLY_LAYER == 0 and cam.cull_mask & PlanarReflection.NO_MIRROR_LAYER != 0,
		"mirror: its camera leaves out the water and what hangs under it; the player's camera never sees the mirror's lights")
	director.compose(0.0, true)
	_check(sun.layers == PlanarReflection.NO_MIRROR_LAYER and moon.layers == PlanarReflection.NO_MIRROR_LAYER
		and mirror.sun.layers == PlanarReflection.MIRROR_ONLY_LAYER and not mirror.sun.shadow_enabled and not mirror.moon.shadow_enabled
		and mirror.sun.global_transform.basis.z.is_equal_approx(sun.global_transform.basis.z)
		and is_equal_approx(mirror.sun.light_energy, sun.light_energy) and mirror.sun.light_color.is_equal_approx(sun.light_color)
		and is_equal_approx(mirror.moon.light_energy, moon.light_energy),
		"mirror: lit by its own shadowless copy of the sun and the moon")
	var m_env := mirror.environment
	var main_sky := world_env.sky.sky_material as ShaderMaterial
	var mirror_sky := m_env.sky.sky_material as ShaderMaterial if m_env.sky != null else null
	_check(m_env.tonemap_mode == Environment.TONE_MAPPER_LINEAR and is_equal_approx(m_env.tonemap_exposure, 1.0) and not m_env.glow_enabled
		and is_equal_approx(m_env.fog_density, world_env.fog_density * EnvironmentDirector.MIRROR_FOG_SHARE)
		and m_env.fog_light_color.is_equal_approx(world_env.fog_light_color),
		"mirror: scene radiance (linear, exposure 1, no glow) with the main fog's colour and half its density")
	_check(mirror_sky != null and mirror_sky != main_sky and int(mirror_sky.get_shader_parameter("sky_pass")) == EnvironmentDirector.MIRROR_SKY_PASS
		and bool(mirror_sky.get_shader_parameter("sky_pass_aurora")) and int(main_sky.get_shader_parameter("sky_pass")) == 0
		and is_equal_approx(float(mirror_sky.get_shader_parameter("sky_time")), float(main_sky.get_shader_parameter("sky_time"))),
		"mirror: its own sky, the main one's uniforms, marched in the quarter pass (the main sky stays per pixel)")
	# Every pen of every site (G2-04 grids), at every LOD: the pen's own
	# meshes (LOD0 shown or handed to the site, LOD1/LOD2) and the site's
	# MultiMeshes of its LOD1/LOD2 parts. What hangs under the water is out of
	# the mirror, the collar and the gear above it are in.
	var underwater_ok := true
	var above_ok := true
	var lod0_ok := true
	var cages_seen := 0
	var site_parts := 0
	var mirror_mask := PlanarReflection.mirror_cull_mask()
	for id in pens.get_pen_ids():
		var farm := pens.get_site(id)
		for cage in pens.get_site_cages(id):
			cages_seen += 1
			for level in PenCage.LOD_NAMES.size():
				for mesh in cage.get_lod_meshes(level):
					var under := String(mesh.name) in PenCage.NO_MIRROR_PARTS
					var hidden := level == 0 and not cage.is_lod0_visible() and mesh.layers == 0
					if under:
						underwater_ok = underwater_ok and (hidden or mesh.layers == PlanarReflection.NO_MIRROR_LAYER)
					elif not hidden:
						above_ok = above_ok and mesh.layers & mirror_mask != 0
			# The site's LOD hand-over hides and shows LOD0 by its layers: it
			# must hand each part its own layers back.
			var was_shown := cage.is_lod0_visible()
			cage.set_lod0_visible(false)
			cage.set_lod0_visible(true)
			for mesh in cage.get_lod_meshes(0):
				lod0_ok = lod0_ok and mesh.layers == (PlanarReflection.NO_MIRROR_LAYER if String(mesh.name) in PenCage.NO_MIRROR_PARTS else PenCage.LOD0_LAYERS)
			cage.set_lod0_visible(was_shown)
		if farm != null:
			for level in [FarmSite.Lod.MID, FarmSite.Lod.FAR]:
				var parts: Dictionary = farm.get_lod_multimeshes(level)
				for part in parts:
					site_parts += 1
					var mmi := parts[part] as MultiMeshInstance3D
					if String(part) in PenCage.NO_MIRROR_PARTS:
						underwater_ok = underwater_ok and mmi.layers == PlanarReflection.NO_MIRROR_LAYER
					else:
						above_ok = above_ok and mmi.layers & mirror_mask != 0
	var schools := env.find_children("FishSchool*", "MultiMeshInstance3D", true, false)
	var shoals := env.find_children("Shoal", "MultiMeshInstance3D", true, false)
	var ropes := env.find_children("Ropes", "MultiMeshInstance3D", true, false)
	var labels := env.find_children("Label", "Label3D", true, false) + env.find_children("Number", "Label3D", true, false)
	var glows := env.find_children("LightGlow", "MultiMeshInstance3D", true, false) + env.find_children(VesselLights.LIGHTS_NAME, "MultiMeshInstance3D", true, false)
	for node in schools + shoals + ropes + labels + glows:
		underwater_ok = underwater_ok and (node as VisualInstance3D).layers == PlanarReflection.NO_MIRROR_LAYER
	var gear := env.find_children("Hoses", "MeshInstance3D", true, false) + env.find_children("Buoy_LOD0", "MultiMeshInstance3D", true, false) \
		+ env.find_children("HoseFloats_LOD0", "MultiMeshInstance3D", true, false)
	for node in gear:
		above_ok = above_ok and (node as VisualInstance3D).layers & mirror_mask != 0
	_check(underwater_ok and cages_seen >= pens.get_pen_ids().size() and site_parts > 0 and schools.size() == cages_seen
		and shoals.size() == cages_seen and not ropes.is_empty() and not labels.is_empty() and not glows.is_empty(),
		"mirror: nets, sinker tubes, lice skirts (every pen, every LOD and the sites' LOD MultiMeshes), fish schools and shoals, mooring ropes, site and pen labels and the light glows (which draw their own reflection) stay out of it (%d pens, %d schools)" % [cages_seen, schools.size()])
	_check(above_ok and lod0_ok and not gear.is_empty(),
		"mirror: collars, walkways, hoses, buoys and hose floats stay in it, and the site's LOD hand-over gives each pen part its own layers back")

	# Wakes: a boat run down the fjord, driven by hand.
	var wakes: VesselWakes = env.get_node("VesselWakes")
	wakes.set_process(false)
	var boat := (load("res://assets/models/workboat/workboat.tscn") as PackedScene).instantiate() as VesselModel
	env.add_child(boat)
	var start := Vector3(terrain.channel_center_x(-400.0), 0.0, -400.0)
	boat.global_position = start
	boat.rotation.y = PI  # bow along +Z
	wakes._process(0.6)
	for i in 40:
		boat.global_position += Vector3(0.0, 0.0, 5.0)
		wakes._process(1.0)
	var trail: Dictionary = wakes.trails.get(boat.get_instance_id(), {})
	var points: Array = trail.get("points", [])
	var spacing_ok := points.size() >= 15
	for i in range(1, points.size()):
		var gap := (points[i]["p"] as Vector3).distance_to(points[i - 1]["p"])
		spacing_ok = spacing_ok and gap >= VesselWakes.SEGMENT_MIN_M - 0.01 and gap <= VesselWakes.SEGMENT_MAX_M + 5.01
	var oldest: Dictionary = points[0] if not points.is_empty() else {}
	var expected := 0.5 * boat.beam_m + tan(deg_to_rad(19.47)) * float(oldest.get("speed", 0.0)) * (wakes._time - float(oldest.get("t", 0.0)))
	var stern := VesselWakes.stern_of(boat)
	var newest: Dictionary = points[points.size() - 1] if not points.is_empty() else {}
	_check(spacing_ok and absf(VesselWakes.wedge_half_width(oldest, wakes._time) - expected) < 0.05
		and absf(float(newest.get("speed", 0.0)) - 5.0) < 0.6 and (newest.get("p", Vector3.INF) as Vector3).distance_to(stern) < VesselWakes.SEGMENT_MAX_M,
		"wakes: a boat at 5 m/s lays a trail behind its stern (%d points), its Kelvin arms 19.47 deg off the track" % points.size())
	var mesh_node: MeshInstance3D = trail.get("mesh")
	var mesh := mesh_node.mesh as ArrayMesh if mesh_node else null
	_check(mesh != null and mesh.surface_get_array_len(0) == (points.size() - 1) * 3 * 4 and mesh_node.layers == PlanarReflection.NO_MIRROR_LAYER
		and mesh_node.material_override == wakes.material,
		"wakes: the trail is one mesh of three ribbons (the wash and both arms), out of the mirror")
	var alpha_new := VesselWakes.point_alpha(newest, wakes._time)
	boat.queue_free()
	await get_tree().process_frame
	for i in 240:
		wakes._process(1.0)
	_check(alpha_new > 0.3 and wakes.trails.is_empty(), "wakes: a fresh wake shows (alpha %.2f) and a gone boat's wake fades away" % alpha_new)
	wakes.set_sea_state(0.06)
	var gale_wake := wakes.sea_visibility
	wakes.set_sea_state(0.0)
	_check(is_zero_approx(gale_wake) and is_equal_approx(wakes.sea_visibility, 1.0), "wakes: a gale's own foam hides them, a calm shows them")
	wakes.set_process(true)

	# Spray at the pens: only in a gale.
	var spray: PenSpray = env.get_node("PenSpray")
	_check(is_zero_approx(PenSpray.spray_level(0.6, 6.0)) and PenSpray.spray_level(0.3, 16.0) > 0.4 and PenSpray.spray_level(0.9, 16.0) > PenSpray.spray_level(0.3, 16.0),
		"spray: none in a breeze, plenty in a gale, more where the sea at the pen is higher")
	env.apply_sky_context({"weather_state": "Storm", "wind_ms": 16.0, "cloud_cover": 1.0, "visibility_km": 2.0})
	director.compose(1.0 / 60.0)
	spray._refresh()
	# One emitter per pen of every (non-ghost) site, at that pen's collar.
	var pen_total := 0
	var at_collars := true
	for id in pens.get_pen_ids():
		var farm := pens.get_site(id)
		if farm != null and farm.is_ghost():
			continue
		var site_cages := pens.get_site_cages(id)
		for k in site_cages.size():
			pen_total += 1
			var em: GPUParticles3D = spray.emitters.get("%s#%d" % [id, k + 1])
			var c := site_cages[k].global_position
			at_collars = at_collars and em != null and Vector2(em.global_position.x - c.x, em.global_position.z - c.z).length() < 0.01
	var emitting := spray.emitters.size() == pen_total and pen_total >= pens.get_pen_ids().size() and at_collars
	for id in spray.emitters:
		emitting = emitting and (spray.emitters[id] as GPUParticles3D).emitting and (spray.emitters[id] as GPUParticles3D).layers == PlanarReflection.NO_MIRROR_LAYER
	env.apply_sky_context(calm)
	director.compose(1.0 / 60.0)
	spray._refresh()
	var quiet := true
	for id in spray.emitters:
		quiet = quiet and not (spray.emitters[id] as GPUParticles3D).emitting
	_check(emitting and quiet, "spray: every pen of every site sprays at its own collar in a 16 m/s gale (%d pens) and none in the calm" % pen_total)
	_check(absf(EnvironmentDirector.whitecap_threshold(0.031) - 2.72) < 0.03 and absf(EnvironmentDirector.whitecap_threshold(0.105) - 1.96) < 0.03,
		"whitecaps: the threshold mapping holds the top-down calibration (W 3.1 % -> 2.72, 10.5 % -> 1.96)")
	scene.queue_free()
	await get_tree().process_frame


## Mirror passes rendered over `frames` hand-driven 60 fps frames.
func _mirror_passes(mirror: PlanarReflection, frames: int) -> int:
	var before := mirror.renders
	for i in frames:
		mirror._process(1.0 / 60.0)
	return mirror.renders - before


func _projection_close(a: Projection, b: Projection) -> bool:
	for c in 4:
		for r in 4:
			if absf(a[c][r] - b[c][r]) > 1e-3 * maxf(1.0, absf(b[c][r])):
				return false
	return true


## The vertex arrays a mesh's first surface carries (Mesh.ARRAY_FORMAT_* bits
## for vertex, normal, tangent, colour, UV, UV2, index).
func _vertex_layout(mesh: Mesh) -> int:
	if mesh == null or not (mesh is ArrayMesh):
		return -1
	var mask := Mesh.ARRAY_FORMAT_VERTEX | Mesh.ARRAY_FORMAT_NORMAL | Mesh.ARRAY_FORMAT_TANGENT | Mesh.ARRAY_FORMAT_COLOR \
		| Mesh.ARRAY_FORMAT_TEX_UV | Mesh.ARRAY_FORMAT_TEX_UV2 | Mesh.ARRAY_FORMAT_INDEX
	return (mesh as ArrayMesh).surface_get_format(0) & mask

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
	# S2-02 moved po3_red_sone from Trøndelag to Vestland (PO3, Karmøy til
	# Sotra, lies in Vestland/Rogaland): its sky is Bergen's 60.39, the sim's
	# own latitude for the scenario. Vestland is also the no-scenario
	# fallback, so _test_sky_latitude_follows_a_northern_scenario checks a
	# Trøndelag scenario's 63.43 on the same path.
	_check(absf(ts.latitude_deg - 60.39) < 0.01 and absf(ts.latitude_deg - float(ctx.get("latitude", 0.0))) < 1e-6
		and str(ctx.get("region", "")) == "Vestland",
		"environment: the sky's latitude is the scenario region's (po3_red_sone: Vestland 60.39, got %.2f)" % ts.latitude_deg)
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

	# Depth of field and shadows by zoom: G1-06's table, in full on Ultra.
	var attrs := cam.attributes as CameraAttributesPractical
	director.set_quality("ultra")
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
	# High (P0-04): the same shadows; depth of field where it shows, at FARM,
	# and skipped by COAST, where the haze softens the far fjord as much.
	director.set_quality("high")
	director.compose(0.0, true)
	_check(not attrs.dof_blur_far_enabled and attrs.dof_blur_amount == 0.0 and absf(sun.directional_shadow_max_distance - 4500.0) < 5.0,
		"environment: on High COAST skips the depth-of-field pass and keeps its shadows (%.0f m)" % sun.directional_shadow_max_distance)
	cam.look_at_position(first, FjordCamera.ZoomLevel.FARM)
	await _settle_camera(cam)
	director.compose(0.0, true)
	_check(attrs.dof_blur_far_enabled and absf(attrs.dof_blur_amount - 0.06) < 0.005 and not attrs.dof_blur_near_enabled and absf(sun.directional_shadow_max_distance - 350.0) < 5.0,
		"environment: on High FARM keeps its far blur (DOF %.3f), without the near blur no FARM view reaches" % attrs.dof_blur_amount)
	director.set_quality(RenderQuality.load_saved_tier())
	cam.look_at_position(first, FjordCamera.ZoomLevel.COAST)
	await _settle_camera(cam)
	director.compose(0.0, true)
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

	# G2-04: a site is a grid of pens; every one of them rides its own ring
	# (this checked the site's one pen, sampled at the site's position).
	var cages := pens.get_site_cages(site_id)
	var farm := pens.get_site(site_id)
	var cage: PenCage = cages[0] if not cages.is_empty() else null
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
		for k in cages.size():
			var c := cages[k]
			c.ride_waves()
			var rest := farm.pen_rest_position(k)
			# The ring's least-squares plane, sampled point by point.
			var mean := 0.0
			var xh := 0.0
			var zh := 0.0
			var xx := 0.0
			var zz := 0.0
			for j in RIDE_RING_POINTS:
				var a := TAU * float(j) / RIDE_RING_POINTS
				var dx := cos(a) * PenCage.RING_RADIUS
				var dz := sin(a) * PenCage.RING_RADIUS
				var h := field.height_at(rest.x + dx, rest.z + dz)
				mean += h
				xh += dx * h
				zh += dz * h
				xx += dx * dx
				zz += dz * dz
			mean /= RIDE_RING_POINTS
			worst_heave = maxf(worst_heave, absf(c.global_position.y - mean))
			var up := c.global_transform.basis.y
			worst_tilt = maxf(worst_tilt, maxf(absf(-up.x / up.y - xh / xx), absf(-up.z / up.y - zh / zz)))
			if k == 0:
				heaves.append(c.global_position.y)
			# The collar's inner and outer pipe (build_pen.py R_IN, R_OUT) round the ring.
			for j in RIDE_RING_POINTS * 2:
				var a := TAU * (floorf(float(j) * 0.5) + 0.5 * float(j % 2)) / RIDE_RING_POINTS
				var radius := 25.465 if j % 2 == 0 else 26.565
				var waterline := c.global_transform * Vector3(cos(a) * radius, 0.0, sin(a) * radius)
				var top := c.global_transform * Vector3(cos(a) * radius, collar_top, sin(a) * radius)
				var water := field.height_at(top.x, top.z)
				worst_rise = maxf(worst_rise, water - waterline.y)
				least_margin = minf(least_margin, top.y - water)
	var heave_range := 0.0
	if not heaves.is_empty():
		heave_range = Array(heaves).max() - Array(heaves).min()
	_check(cage != null and cages.size() > 1 and worst_heave <= 0.1 and heave_range > 0.05 and worst_tilt <= 0.005,
		"water: in a 25 m/s storm each of the site's %d pens rides its ring's mean surface and slope (|y - mean| <= %.3f m, slope within %.4f, over %d moments, heave range %.2f m)"
			% [cages.size(), worst_heave, worst_tilt, RIDE_SAMPLES, heave_range])
	_check(collar != null and worst_rise > 0.0 and least_margin > 0.0,
		"water: ... and the water round every riding collar rises at most %.2f m above its waterline, under its %.2f m top (least margin %.2f m)"
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

# ── U1-09: the live main menu ─────────────────────────────────────────────────

## G2-01: a far field surrounds the 4 km world out to 16 km. Its ring of
## chunks goes up after the terrain with the terrain's material (the band
## next to the world casts shadows, the outer bands do not); height queries
## outside the world answer from it; at COAST the camera may leave the world
## while the focus stays inside it; no COAST view, looking any way, shows
## the world's end (edge-of-world mask 0; the same views measured with the
## land cut at the 4 km edge show the old island); and the farm-type plots
## (farm-types spec §4) are reserved: three shore pads in the world, three
## offshore slots in open sea beyond it.
func _test_far_field_surrounds_the_world() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "far field: seeded scenario starts")
	_sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	var env: Node = scene.get_node("FjordEnvironment")
	var cam: FjordCamera = env.get_node("FjordCamera")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var pens: FishPenManager = env.get_node("FishPenManager")
	cam.auto_orbit_enabled = false
	for i in 3000:
		if terrain.is_far_field_ready():
			break
		await get_tree().process_frame
	_check(terrain.is_ready() and terrain.is_far_field_ready(), "far field: the ring goes up after the terrain")
	var chunks: Array = terrain.far_field.get_children() if terrain.far_field != null else []
	var shadowed := 0
	var shared_material := chunks.size() > 0
	for chunk in chunks:
		var mi := chunk as MeshInstance3D
		shared_material = shared_material and mi != null and mi.material_override == terrain.get_material()
		if mi != null and mi.cast_shadow != GeometryInstance3D.SHADOW_CASTING_SETTING_OFF:
			shadowed += 1
	_check(chunks.size() == 48 and shadowed == 16,
		"far field: 48 chunks to %.0f km, the 16 next to the world cast shadows (%d chunks, %d shadowed)" % [
			terrain.far_field_half_extent() / 1000.0, chunks.size(), shadowed])
	_check(shared_material, "far field: the ring wears the terrain's own material")
	_check(terrain.max_upload_frame_ms < 50.0,
		"far field: no main-thread upload step over 50 ms, ring included (%.1f ms)" % terrain.max_upload_frame_ms)
	var half := terrain.terrain_size * 0.5
	var outside := Vector2(half.x + 2600.0, 300.0)
	_check(terrain.far_field_half_extent() >= 16000.0
		and absf(terrain.get_height_at(outside.x, outside.y) - terrain.far_field_height_at(outside.x, outside.y)) < 0.01
		and terrain.get_height_at(outside.x, outside.y) > 200.0,
		"far field: outside the world the height is the far field's (%.0f m on the plateau 2.6 km beyond the edge)" % terrain.get_height_at(outside.x, outside.y))

	# At COAST, looking up the fjord from the sea, the camera leaves the world;
	# the focus stays EDGE_MARGIN inside it.
	var first := pens.get_pen_position(pens.get_pen_ids()[0])
	cam.home_focus = first
	cam.look_at_position(first, FjordCamera.ZoomLevel.COAST)
	cam.rotate_view(PI)
	await _settle_camera(cam)
	var focus := cam.get_focus_point()
	_check(absf(cam.global_position.x) > half.x or absf(cam.global_position.z) > half.y,
		"far field: at COAST the camera leaves the 4 km world (%s)" % str(cam.global_position.round()))
	_check(absf(focus.x) <= half.x - FjordCamera.EDGE_MARGIN + 0.01 and absf(focus.z) <= half.y - FjordCamera.EDGE_MARGIN + 0.01,
		"far field: the focus stays inside the world (%s)" % str(focus.round()))

	# Edge-of-world mask at COAST, eight headings round the first pen and round
	# the fjord head.
	var worst := 0.0
	var island := 0.0
	var head := Vector3(terrain.channel_center_x(1000.0), 0.0, 1000.0)
	for target in [first, head]:
		for k in 8:
			cam.look_at_position(target, FjordCamera.ZoomLevel.COAST)
			cam._user_rotated = true
			cam._yaw_target = cam.home_yaw(target) + k * PI / 4.0
			await _settle_camera(cam)
			worst = maxf(worst, _edge_of_world_fraction(cam, terrain, terrain.far_field_half_extent()))
			island = maxf(island, _edge_of_world_fraction(cam, terrain, half.x))
	_check(worst == 0.0, "far field: no COAST view shows the world's end (edge-of-world mask %.3f)" % worst)
	_check(island > 0.02, "far field: with the land cut at the 4 km edge the same views would (mask %.3f)" % island)

	# The farm-type plots (spec §4).
	var pads: Array = terrain.land_pad_slots()
	var pad_ids: Array = pads.map(func(p: Dictionary) -> String: return str(p["id"]))
	var pads_ok := pad_ids == ["pad_a", "pad_b", "pad_delta"]
	for pad in pads:
		var c: Vector3 = pad["centre"]
		var size: Vector2 = pad["size"]
		pads_ok = pads_ok and absf(c.x) + size.x * 0.5 < half.x and absf(c.z) + size.y * 0.5 < half.y \
			and is_equal_approx(c.y, 3.6) and float(pad["intake_reach_m"]) > 0.0
	_check(pads_ok, "far field: the land pads %s lie in the world with a computed intake" % str(pad_ids))
	var slots: Array = terrain.offshore_slots()
	var slot_ids: Array = slots.map(func(o: Dictionary) -> String: return str(o["id"]))
	var slots_ok := slot_ids == ["off_1", "off_2", "hav_1"]
	for slot in slots:
		var at: Vector3 = slot["position"]
		slots_ok = slots_ok and (absf(at.x) > half.x or absf(at.z) > half.y) and float(slot["exposure"]) == 1.0 \
			and float(slot["water_depth_m"]) > 100.0 and terrain.get_height_at(at.x, at.z) < -100.0
	_check(slots_ok, "far field: the offshore slots %s lie in open sea beyond the world" % str(slot_ids))
	cam.set_zoom_level(FjordCamera.ZoomLevel.FJORD)
	scene.queue_free()
	await get_tree().process_frame

## G2-02: the terrain wears its layer arrays, the season reaches the ground
## through the director (snow line, birch leaves, fresh snow, wet ground),
## and every tree the vegetation stands up lies inside the masks: above the
## splash zone, below the 450-550 m treeline, gentler than 55 degrees,
## spruce low and gentle, on the ground it was planted on.
func _test_terrain_material_and_vegetation() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "terrain material: seeded scenario starts")
	_sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	var env: FjordEnvironment = scene.get_node("FjordEnvironment")
	var director: EnvironmentDirector = env.get_node("EnvironmentDirector")
	var weather: WeatherController = env.get_node("WeatherSystem")
	var terrain: FjordTerrain = env.get_node("Terrain")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	for i in 900:
		if terrain.is_ready() and terrain.vegetation != null and terrain.vegetation.call("is_ready"):
			break
		await get_tree().process_frame
	_sim.set_paused(true)
	vista.sim_weather_enabled = false
	(env.get_node("TimeOfDay") as TimeOfDay).paused = true
	weather.transition_duration = 0.0

	# The material: two Texture2DArrays, 7 layers and 7 + 2 noise slices.
	var mat := terrain.get_material()
	var albedo = mat.get_shader_parameter("albedo_height") if mat != null else null
	var nra = mat.get_shader_parameter("normal_rough_ao") if mat != null else null
	# Imported arrays are CompressedTexture2DArray: a TextureLayered, not a Texture2DArray.
	_check(albedo is TextureLayered and (albedo as TextureLayered).get_layers() == 7
		and (albedo as TextureLayered).get_layered_type() == TextureLayered.LAYERED_TYPE_2D_ARRAY,
		"terrain material: albedo + height array with 7 layers")
	_check(nra is TextureLayered and (nra as TextureLayered).get_layers() == 9
		and (nra as TextureLayered).get_layered_type() == TextureLayered.LAYERED_TYPE_2D_ARRAY,
		"terrain material: normal + roughness + AO array with 7 layers and 2 noise slices")
	_check(mat != null and float(mat.get_shader_parameter("use_textures")) == 1.0,
		"terrain material: textured (not the palette fallback)")

	# The season from the calendar at 63.43 N (GroundSeasonModel). The
	# scenario's own region sets the latitude since S2-02 (po3 is Vestland,
	# 60.39 N), so the checks below pin the latitude they are written for.
	env.set_latitude(63.43)
	env.set_weather(WeatherController.WeatherState.CLEAR)
	env.set_season(50.0 / 365.0)
	director.compose(0.0, true)
	var winter: Dictionary = director.get_composed().duplicate()
	_check(float(winter.get("snow_line_m", 9999.0)) <= 150.0 and float(winter.get("foliage", 1.0)) == 0.0,
		"ground: mid-February brings snow to the shore (line %.0f m) and bare birch" % float(winter.get("snow_line_m", -1.0)))
	# (The globals themselves are the renderer's: the headless dummy keeps no
	# values, so tests/test_terrain_material.py pins the single writer and the
	# names, and the probe renders show them.)
	env.set_season(182.0 / 365.0)
	director.compose(0.0, true)
	var summer: Dictionary = director.get_composed().duplicate()
	_check(float(summer["snow_line_m"]) >= 1000.0 and float(summer["foliage"]) >= 0.99,
		"ground: midsummer lifts the snow line above the summits (%.0f m) and leafs out the birch" % float(summer["snow_line_m"]))
	env.set_season(270.0 / 365.0)
	director.compose(0.0, true)
	var autumn_north := float(director.get_composed()["autumn"])
	_check(autumn_north > 0.4,
		"ground: late September turns the birch yellow (%.2f)" % autumn_north)
	# Autumn comes later in the south: the same day at Vestland's 60.39 N.
	env.set_latitude(60.39)
	director.compose(0.0, true)
	var autumn_south := float(director.get_composed()["autumn"])
	_check(autumn_south < autumn_north,
		"ground: the birch turns later at 60.39 N than at 63.43 N (%.2f < %.2f)" % [autumn_south, autumn_north])
	env.set_latitude(63.43)

	# Weather: a snowfall lowers the line now, rain wets the ground.
	env.set_season(320.0 / 365.0)
	env.set_weather(WeatherController.WeatherState.SNOW)
	director.compose(0.0, true)
	var snowing: Dictionary = director.get_composed().duplicate()
	_check(float(snowing["fresh_snow"]) > 0.0 and float(snowing["snow_line_m"]) < float(snowing["season_snow_line_m"]),
		"ground: falling snow pulls the line down (%.0f m, season %.0f m)" % [float(snowing["snow_line_m"]), float(snowing["season_snow_line_m"])])
	env.set_weather(WeatherController.WeatherState.RAIN)
	director.compose(0.0, true)
	_check(float(director.get_composed()["ground_wetness"]) > 0.3,
		"ground: rain wets the ground (%.2f)" % float(director.get_composed()["ground_wetness"]))
	env.set_weather(WeatherController.WeatherState.CLEAR)
	director.compose(0.0, true)
	_check(float(director.get_composed()["ground_wetness"]) < 0.05 and float(director.get_composed()["fresh_snow"]) < 0.05,
		"ground: a clear sky leaves the ground dry and the line on the season's")

	# The trees: every one inside the masks, on its ground.
	var veg = terrain.vegetation
	_check(veg != null and veg.call("is_ready") and int(veg.get("instance_count")) == terrain.tree_count and terrain.tree_count > 10000,
		"vegetation: %d trees stand in %d cells" % [terrain.tree_count, (veg.get("cells_lod0") as Array).size() if veg != null else 0])
	var outside := 0
	var floating := 0
	var steep := 0
	var spruce_bad := 0
	var checked := 0
	# The scatter's own slope stencil: half a grid cell either side.
	var e := terrain.terrain_size.x / float(terrain.terrain_resolution - 1) * 0.5
	for cell: Dictionary in terrain.vegetation_chunks:
		var buf: PackedFloat32Array = cell["buffer"]
		var spruce := int(cell["species"]) == 0
		for i in range(0, int(cell["count"]), 7):
			var o := i * 16
			var x := buf[o + 3]
			var z := buf[o + 11]
			var base := buf[o + 15]
			var ground := terrain.get_height_at(x, z)
			var slope := Vector2((terrain.get_height_at(x + e, z) - terrain.get_height_at(x - e, z)) / (2.0 * e),
				(terrain.get_height_at(x, z + e) - terrain.get_height_at(x, z - e)) / (2.0 * e)).length()
			checked += 1
			if absf(base - ground) > 0.25 or buf[o + 7] > ground:
				floating += 1
			if base < 3.0 or base >= 550.0:
				outside += 1
			if slope >= 1.4291:
				steep += 1
			if spruce and (base >= 340.0 or slope >= 0.7823):
				spruce_bad += 1
	_check(checked > 1000 and outside == 0, "vegetation: no tree below the splash zone or above the treeline (%d checked)" % checked)
	_check(steep == 0, "vegetation: no tree on a slope steeper than 55 degrees (%d)" % steep)
	_check(spruce_bad == 0, "vegetation: spruce only low and on gentle ground (%d out)" % spruce_bad)
	_check(floating == 0, "vegetation: every tree's base sits on, and its trunk sinks into, the ground (%d off)" % floating)

	# The shore buildings: naust rows and a quay, on dry ground, doors to the water.
	var props: Array = terrain.shore_props
	var node: Node = terrain.shore_props_node
	var naust := 0
	var quays := 0
	var facing_water := 0
	for prop: Dictionary in props:
		var pos: Vector3 = prop["position"]
		var yaw := float(prop["yaw"])
		var ahead := pos + Vector3(-sin(yaw), 0.0, -cos(yaw)) * 40.0
		if terrain.get_height_at(ahead.x, ahead.z) < 0.0:
			facing_water += 1
		if int(prop["kind"]) == 0:
			naust += 1
		else:
			quays += 1
	_check(naust >= 2 and quays == 1 and facing_water == props.size(),
		"shore: %d naust and %d quay stand facing the water (%d of %d)" % [naust, quays, facing_water, props.size()])
	_check(node != null and node.get_child_count() == props.size(),
		"shore: every building is in the scene (%d)" % (node.get_child_count() if node != null else -1))
	scene.queue_free()
	await get_tree().process_frame

## G2-01 edge-of-world mask: the share of a 32x18 grid of screen rays below
## the horizon whose first hit of the rendered surface (land within `extent`
## of the origin, sea everywhere) is open water where the land goes on (the
## far field is above the sea there): land cut off by the world's end. With
## the far field's extent this is what the player sees; with the world's own
## half-size it is the old 4 km island. Rays that meet nothing before the
## camera's far plane are the haze band just under the horizon (the sea
## there lies beyond 20 km, with or without a far field) and count for
## neither side.
func _edge_of_world_fraction(cam: Camera3D, terrain: FjordTerrain, extent: float) -> float:
	var size := get_viewport().get_visible_rect().size
	var below := 0
	var edge := 0
	for gy in 18:
		for gx in 32:
			var p := Vector2((gx + 0.5) / 32.0 * size.x, (gy + 0.5) / 18.0 * size.y)
			var dir := cam.project_ray_normal(p)
			if dir.y >= 0.0:
				continue
			var hit := _march_rendered_surface(cam.project_ray_origin(p), dir, terrain, extent, cam.far)
			if not hit.is_finite():
				continue
			below += 1
			if hit.y <= terrain.water_level + 0.01 and terrain.far_field_height_at(hit.x, hit.z) > terrain.water_level + 1.0:
				edge += 1
	return float(edge) / float(maxi(below, 1))

## First point where a ray meets the rendered surface (land within `extent`
## of the origin, the sea everywhere), or INF before `far` metres.
func _march_rendered_surface(origin: Vector3, dir: Vector3, terrain: FjordTerrain, extent: float, far: float) -> Vector3:
	var travelled := 0.0
	var p := origin
	while travelled < far:
		var ground := terrain.water_level
		if absf(p.x) <= extent and absf(p.z) <= extent:
			ground = maxf(terrain.get_height_at(p.x, p.z), ground)
		var gap := p.y - ground
		if gap <= 0.5:
			return Vector3(p.x, ground, p.z)
		var step := clampf(gap * 0.5, 2.0, 400.0)
		p += dir * step
		travelled += step
	return Vector3.INF

## Wait (bounded) until the menu's world is in and its veil has lifted.
func _await_menu_reveal(menu: Control, timeout_s: float = 15.0) -> float:
	var started := Time.get_ticks_msec()
	while not bool(menu.revealed) and Time.get_ticks_msec() - started < int(timeout_s * 1000.0):
		await get_tree().process_frame
	return (Time.get_ticks_msec() - started) / 1000.0

## U1-09: the main menu shows the live fjord: the game's own FjordEnvironment
## (no video, no painting), staged as a calm late-winter afternoon in Troms,
## under a low oblique camera that drifts slowly and keeps the horizon in
## frame. The world fades in from an Ink veil; leaving hands the viewport's
## render scale back to the game.
func _test_main_menu_shows_the_live_fjord() -> void:
	MotionPrefs.force(false)
	var scale_before := get_viewport().scaling_3d_scale
	var menu := await _spawn_main_menu()
	var backdrop: MenuBackdrop = menu.backdrop
	var env: FjordEnvironment = backdrop.environment
	_check(env != null and env.get_parent() == backdrop
		and env.scene_file_path == "res://scenes/environment/FjordEnvironment.tscn",
		"menu: the backdrop is the game's own FjordEnvironment")
	_check(menu.veil.visible and menu.veil.color.a > 0.99, "menu: an Ink veil covers the world while it loads")
	var waited := await _await_menu_reveal(menu)
	_check(backdrop.is_world_ready() and menu.revealed, "menu: the world comes in and the veil lifts (%.1f s)" % waited)
	await get_tree().create_timer(menu.VEIL_FADE_S + 0.3).timeout
	_check(not menu.veil.visible, "menu: the veil has faded out")
	_check(get_viewport().get_camera_3d() == backdrop.camera, "menu: the fjord's camera draws the menu's world")
	var managers := 0
	for manager in MenuBackdrop.SIM_MANAGERS:
		managers += 1 if env.get_node_or_null(manager) != null else 0
	_check(managers == 0, "menu: no sim-driven vessels or rival farms in the menu's world (%d left)" % managers)
	var site := backdrop.get_node_or_null("Site")
	_check(site != null and site.find_children("*", "PenCage", true, false).size() == MenuBackdrop.SITE_PENS,
		"menu: a site of %d pens lies in the fjord" % MenuBackdrop.SITE_PENS)
	# The mood: calm water, late winter in Northern Norway, a low sun.
	var ws := env.weather_system
	_check(ws.get_wind_speed_ms() <= 2.0 and ws.get_wave_height_m() <= 0.1 and ws.get_rain() == 0.0 and ws.get_snow() == 0.0,
		"menu: calm weather (wind %.1f m/s, waves %.2f m)" % [ws.get_wind_speed_ms(), ws.get_wave_height_m()])
	var ts := env.time_system
	_check(ts.paused and absf(ts.get_hour() - MenuBackdrop.HOUR) < 0.01, "menu: the hour is pinned (%.2f)" % ts.get_hour())
	_check(ts.season < 0.25 and ts.latitude_deg > 66.0, "menu: late winter north of the Arctic Circle (season %.2f, %.1f N)" % [ts.season, ts.latitude_deg])
	var sun := ts.get_sun_elevation_deg()
	_check(sun > 3.0 and sun < 20.0, "menu: a low sun (%.1f deg)" % sun)
	# The camera: a low oblique view with the horizon in frame.
	var cam: FjordCamera = backdrop.camera
	var forward := -cam.global_transform.basis.z
	var pitch := rad_to_deg(asin(clampf(forward.y, -1.0, 1.0)))
	_check(pitch <= -3.0 and pitch >= -10.0, "menu: a low oblique view (pitch %.1f deg)" % pitch)
	_check(cam.global_position.y > 40.0 and cam.global_position.y < 200.0, "menu: the camera flies low (%.0f m)" % cam.global_position.y)
	var horizon := _horizon_row(cam)
	_check(horizon > 0.2 and horizon < 0.6, "menu: the horizon sits in the upper half of the frame (%.2f)" % horizon)
	var sky := _sky_fraction(cam)
	_check(sky > 0.15 and sky < 0.6, "menu: sky and fjord share the frame (sky %.0f %%)" % (sky * 100.0))
	# The drift: the camera moves, slowly, and its whole track is over water.
	var p0 := cam.global_position
	await get_tree().create_timer(1.5).timeout
	var moved := cam.global_position.distance_to(p0)
	_check(moved > 0.3 and moved < 15.0, "menu: the camera drifts slowly (%.1f m in 1.5 s)" % moved)
	var over_water := true
	var terrain := backdrop.terrain
	for i in 36:
		var v: Dictionary = backdrop.view_at(MenuBackdrop.DRIFT_PERIOD_S * float(i) / 36.0)
		var at: Vector3 = v["position"]
		over_water = over_water and terrain.get_height_at(at.x, at.z) < terrain.water_level
	_check(over_water, "menu: the drift stays over the water")
	_check(get_viewport().scaling_3d_scale < 1.0, "menu: the world draws at the menu's render scale (%.2f)" % get_viewport().scaling_3d_scale)
	var audio := get_node_or_null("/root/AudioController")
	_check(audio != null and audio.get_current_music() == MusicLibrary.music(audio.MENU_MUSIC_KEY),
		"menu: the main theme plays from the start (%s)" % (audio.get_current_music() if audio else "no audio"))
	menu.queue_free()
	await get_tree().process_frame
	_check(is_equal_approx(get_viewport().scaling_3d_scale, scale_before), "menu: leaving restores the game's render scale")
	MotionPrefs.force(null)

## U1-09: the column is Ink glass (D13 Ink at 90 %) whose text reads over
## every mood: ThemeLint judges each line over a white and a black world.
## The logo is the brand wordmark in the display face, never an emoji, and
## the version reads from the project's version.
func _test_main_menu_column_reads_over_every_mood() -> void:
	var menu := await _spawn_main_menu()
	await get_tree().create_timer(THEME_SETTLE_S).timeout
	var glass: ColorRect = menu.glass
	var shader := (glass.material as ShaderMaterial).shader if glass.material is ShaderMaterial else null
	_check(glass.color.is_equal_approx(Color(LakskongenTheme.INK, 0.9)) and shader != null
		and shader.resource_path == "res://shaders/menu_glass.gdshader", "menu: the column is Ink glass at 90 %")
	var lint := preload("res://tests/ThemeLint.gd")
	var judged := 0
	for c in menu.center.find_children("*", "Control", true, false):
		var control := c as Control
		if control.is_visible_in_tree() and (control is Label or control is Button) and str(control.get("text")) != "":
			judged += 1
	_check(judged >= 8, "menu: the column shows its lockup, items and version (%d texts)" % judged)
	var low: PackedStringArray = lint.low_contrast(menu.center)
	_check(low.is_empty(), "menu: every line of the column reads over a white or a black world %s" % str(low.slice(0, 4)))
	var emoji := PackedStringArray()
	for entry in TextLint.dump(menu):
		var text := str(entry["text"])
		for i in text.length():
			var u := text.unicode_at(i)
			if (u >= 0x1F000 and u <= 0x1FAFF) or (u >= 0x2600 and u <= 0x27BF) or u == 0xFE0F:
				emoji.append("%s '%s'" % [entry["path"], text])
				break
	_check(emoji.is_empty(), "menu: no emoji on the main menu %s" % str(emoji))
	_check(menu.wordmark.texture != null and menu.wordmark.texture.resource_path == "res://assets/brand/lakskongen_wordmark.svg"
		and menu.brand_mark.texture != null, "menu: the logo is the brand lockup (mark and wordmark)")
	var version := str(ProjectSettings.get_setting("application/config/version", ""))
	_check(version != "" and menu.version_label.text.contains(version.get_slice("-", 0))
		and not menu.version_label.text.contains("Nordic") and not tr("UI_VERSION_TEXT").contains("Nordic"),
		"menu: the version reads the project's version ('%s')" % menu.version_label.text)
	menu.queue_free()
	await get_tree().process_frame

## U1-09: Redusert bevegelse holds the camera, keeps the column in place and
## shortens the veil's fade; the settings offer it with the accessibility
## options (without writing the player's settings file here).
func _test_main_menu_reduced_motion_holds_the_camera() -> void:
	MotionPrefs.force(true)
	var menu := await _spawn_main_menu()
	var backdrop: MenuBackdrop = menu.backdrop
	_check(menu.reduced_motion and backdrop.reduced_motion, "reduced motion: the menu and its world take the preference")
	var margin := menu.menu_vbox.get_parent() as Control
	_check(is_zero_approx(margin.position.x), "reduced motion: the column does not slide in")
	await _await_menu_reveal(menu)
	await get_tree().create_timer(menu.VEIL_FADE_REDUCED_S + 0.3).timeout
	_check(not menu.veil.visible, "reduced motion: the veil is gone within %.2f s of the world" % (menu.VEIL_FADE_REDUCED_S + 0.3))
	var cam: FjordCamera = backdrop.camera
	var p0 := cam.global_position
	var t0: float = backdrop.drift_time
	await get_tree().create_timer(1.5).timeout
	_check(cam.global_position.distance_to(p0) < 0.05 and backdrop.drift_time == t0,
		"reduced motion: the camera holds still (%.3f m)" % cam.global_position.distance_to(p0))
	# The settings: a Redusert bevegelse box that lands in the accessibility settings.
	var settings: Control = menu.settings_menu
	var box := settings.get_node_or_null("%ReducedMotionCheck") as CheckBox
	_check(box != null and box.get_parent().name == "Tilgjengelighet" and tr(box.text) == "Redusert bevegelse",
		"settings: Tilgjengelighet offers Redusert bevegelse")
	if box != null:
		box.button_pressed = true
		var acc: Dictionary = settings._collect_from_widgets()["accessibility"]
		_check(bool(acc.get("reduced_motion", false)), "settings: Redusert bevegelse is kept with the accessibility settings")
		box.button_pressed = false
	menu.queue_free()
	await get_tree().process_frame
	MotionPrefs.force(false)
	var moving := await _spawn_main_menu()
	_check(not moving.reduced_motion and moving.menu_vbox.get_parent().position.x < 0.0,
		"motion: without the preference the column slides in from the left")
	moving.queue_free()
	await get_tree().process_frame
	MotionPrefs.force(null)

# ── U1-05: truthful numbers ───────────────────────────────────────────────────

const DASHBOARD_FIN_DAYS := 30

## A seeded po3_red_sone game (two sites) in a GameScene, paused.
func _spawn_po3_scene(seed: int) -> Node:
	_check(_sim.start_qa_scenario("po3_red_sone", seed), "setup: po3_red_sone starts (seed %d)" % seed)
	_sim.skip_tutorial()
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	return scene

## The tab's panel, opened (tab panels are built anew on every switch).
func _open_panel(ui: Control, tab: String) -> Control:
	ui.show_tab(tab)
	await get_tree().process_frame
	await get_tree().process_frame
	return ui.find_child("Panel_" + tab, true, false)

## What each chart shows now next to the bridge series it claims to show.
func _chart_snapshot(ui: Control) -> Dictionary:
	var out := {}
	var day := int(_sim.current_day)
	var dash := await _open_panel(ui, "dashboard")
	dash._refresh_all()
	var spark = dash._kpi_sparks["revenue"]["node"]
	out["dashboard revenue line"] = {"shown": Array(spark.values), "bridge": Array(_sim.get_analytics_data("revenue_nok", 30))}
	var rev: Array[float] = _sim.get_analytics_data("revenue_nok", DASHBOARD_FIN_DAYS + 1)
	var cost: Array[float] = _sim.get_analytics_data("expenses_nok", DASHBOARD_FIN_DAYS + 1)
	var expect := PeriodBarChart.periods(rev, cost, day, 5, 6, rev.size() < DASHBOARD_FIN_DAYS + 1)
	var chart: PeriodBarChart = dash.get_node("%FinChart")
	out["dashboard income/cost bars"] = {"shown": [Array(chart.labels), Array(chart.income), Array(chart.costs)],
		"bridge": [Array(expect["labels"]), Array(expect["income"]), Array(expect["costs"])]}
	var fin := await _open_panel(ui, "finance")
	var month_rev: Array[float] = _sim.get_analytics_data("revenue_nok", 186)
	var month_cost: Array[float] = _sim.get_analytics_data("expenses_nok", 186)
	var months := PeriodBarChart.periods(month_rev, month_cost, day, 0, 6, month_rev.size() < 186)
	var fchart: PeriodBarChart = fin.get_node("%FinChart")
	out["finance monthly bars"] = {"shown": [Array(fchart.labels), Array(fchart.income), Array(fchart.costs)],
		"bridge": [Array(months["labels"]), Array(months["income"]), Array(months["costs"])]}
	var story: Dictionary = _sim.get_profit_loss_story(99)
	var shown_lines := {}
	for line in fin.get_statement_lines():
		shown_lines[str(line["label"])] = snappedf(float(line["amount"]), 0.01)
	var ledger_lines := {}
	var costs: Array = story.get("top_costs", [])
	for i in mini(costs.size(), 7):
		ledger_lines[SimLabels.ledger_category(str(costs[i].get("category", "")))] = snappedf(float(costs[i].get("amount", 0.0)), 0.01)
	var listed := {}
	for k in ledger_lines:
		listed[k] = shown_lines.get(k, -1.0)
	out["income statement"] = {"shown": listed, "bridge": ledger_lines}
	var con := await _open_panel(ui, "contracts")
	var price: LineChart = con.get_node("%PriceChart")
	out["market price line"] = {"shown": Array(price.series), "bridge": Array(_sim.get_analytics_data("market_price_nok", 84))}
	return out

## U1-05 #1: in a scripted 60-day run, every chart shows the bridge series it
## claims to show, and what it shows on day 60 differs from day 1.
func _test_charts_draw_the_sims_series_over_sixty_days() -> void:
	var scene := await _spawn_po3_scene(91)
	var ui: Control = scene.get_node("UILayer/GameUI")
	_sim.advance_days(1)
	var day1 := await _chart_snapshot(ui)
	_sim.advance_days(59)
	var day60 := await _chart_snapshot(ui)
	for key in day60:
		_check(day60[key]["shown"] == day60[key]["bridge"], "charts: the %s shows the sim's series on day 60" % key)
		_check(day60[key]["shown"] != day1[key]["shown"], "charts: the %s moved between day 1 and day 60" % key)
	# The bars add up to the books: the costs over the dashboard's window are
	# the year-to-date rise over the same days.
	var dash := await _open_panel(ui, "dashboard")
	await get_tree().process_frame
	var chart: PeriodBarChart = dash.get_node("%FinChart")
	var cost: Array[float] = _sim.get_analytics_data("expenses_nok", DASHBOARD_FIN_DAYS + 1)
	var shown_costs := 0.0
	for v in chart.costs:
		shown_costs += v
	var booked := cost[cost.size() - 1] - cost[0]
	_check(absf(shown_costs - booked) < 1.0,
		"charts: the 30-day cost bars sum to the costs booked in those days (%.0f vs %.0f)" % [shown_costs, booked])
	var hi := chart.scale_max()
	var rects := chart.bar_rects()
	var plot_h := chart.size.y - PeriodBarChart.PAD_TOP - PeriodBarChart.LABEL_ROW
	var heights_follow := hi > 0.0 and not rects.is_empty()
	for i in rects.size():
		heights_follow = heights_follow and absf((rects[i]["cost"] as Rect2).size.y - plot_h * chart.costs[i] / hi) < 0.5
	_check(heights_follow, "charts: every cost bar's height is its amount on the chart's scale")
	# The monthly bars hold every krone of the year so far, the first day's too.
	var fin := await _open_panel(ui, "finance")
	var fchart: PeriodBarChart = fin.get_node("%FinChart")
	var month_costs := 0.0
	for v in fchart.costs:
		month_costs += v
	var ytd: Array[float] = _sim.get_analytics_data("expenses_nok", 1)
	_check(not ytd.is_empty() and absf(month_costs - ytd[0]) < 1.0,
		"charts: the monthly cost bars sum to the year's costs (%.0f vs %.0f)" % [month_costs, ytd[0] if not ytd.is_empty() else -1.0])
	scene.queue_free()
	await get_tree().process_frame

## U1-05 #2: the "Hvorfor gikk det slik?" bars are each item's share of the
## whole: fill width / track width = share within 2 px, and a larger cost
## draws a longer bar.
func _test_share_bars_are_the_ledger_shares() -> void:
	var scene := await _spawn_po3_scene(20261012)
	var ui: Control = scene.get_node("UILayer/GameUI")
	_sim.advance_days(12)
	var fin := await _open_panel(ui, "finance")
	await get_tree().process_frame
	var bars: Array = fin.find_children("ShareBar", "Control", true, false)
	var story: Dictionary = _sim.get_profit_loss_story(3)
	var costs: Array = story.get("top_costs", [])
	_check(bars.size() >= costs.size() and costs.size() >= 2,
		"share bars: one bar per cost driver (%d bars, %d costs)" % [bars.size(), costs.size()])
	var within := true
	var widths: Array[float] = []
	for i in mini(bars.size(), costs.size()):
		var bar: ShareBar = bars[i]
		var expected := float(costs[i].get("share", 0.0)) * bar.size.x
		within = within and absf(bar.fill_width() - expected) <= 2.0 and bar.size.x > 40.0
		widths.append(bar.fill_width())
	_check(within and not widths.is_empty(), "share bars: each fill is its share of the track within 2 px %s" % str(widths))
	var ordered := widths.size() >= 2
	for i in range(1, widths.size()):
		ordered = ordered and widths[i] <= widths[i - 1] + 0.01
	_check(ordered and widths[0] > widths[widths.size() - 1] + 2.0, "share bars: a larger cost draws a longer bar %s" % str(widths))
	scene.queue_free()
	await get_tree().process_frame

## U1-05 #4: one site card per site the player owns — for 1, 3 and 9 sites
## and for the sim's own two-site game — each card showing its own site.
func _test_dashboard_site_cards_match_the_sites() -> void:
	var scene := await _spawn_po3_scene(20261012)
	var ui: Control = scene.get_node("UILayer/GameUI")
	var dash := await _open_panel(ui, "dashboard")
	dash._refresh_all()
	await get_tree().process_frame
	var sites: Array = _sim.get_all_pens()
	var cards: Array = dash.get_site_cards()
	_check(cards.size() == sites.size() and sites.size() == 2, "site cards: the two-site game shows two cards (%d)" % cards.size())
	for n in [1, 3, 9]:
		var fake: Array = []
		for i in n:
			fake.append({"id": "ci_site_%d" % i, "name": "CI anlegg %d" % (i + 1), "fish_count": 1000 * (i + 1),
				"biomass_kg": 500.0 * (i + 1), "lice_per_fish": 0.05 * i, "max_biomass": 780000.0})
		dash.show_sites(fake)
		await get_tree().process_frame
		cards = dash.get_site_cards()
		var in_order := true
		for i in cards.size():
			in_order = in_order and str(cards[i].get_meta(&"site_id")) == "ci_site_%d" % i
		_check(cards.size() == n and in_order, "site cards: %d sites show %d cards, in order (%d shown)" % [n, n, cards.size()])
	dash.show_sites([{"id": "ci_red", "name": "CI rød", "fish_count": 5000, "biomass_kg": 9000.0,
		"lice_per_fish": 0.62, "max_biomass": 780000.0}])
	await get_tree().process_frame
	cards = dash.get_site_cards()
	var texts := ""
	for l in cards[0].find_children("*", "Label", true, false):
		texts += (l as Label).text + "|"
	_check(cards.size() == 1 and texts.contains(SimFormatters.num(0.62, 2)), "site cards: a card shows its own site's lice (%s)" % texts)
	scene.queue_free()
	await get_tree().process_frame

## U1-05: a twelve-day skip with storms is dated events, not a pile of "Nå"
## alerts: event alerts carry the day they happened and expire on time, and
## a storm and its "Stormskade" record count once.
func _test_skipped_days_date_their_alerts() -> void:
	var scene := await _spawn_po3_scene(20261012)
	var ui: Control = scene.get_node("UILayer/GameUI")
	var toasts := get_node("/root/ToastNotification")
	toasts.clear_all()
	_sim.advance_days(12)
	var storm_toasts := []
	for n in toasts._active_nodes:
		for l in (n as Node).find_children("*", "Label", true, false):
			if (l as Label).text.contains("Stormen gir") or (l as Label).text.contains("Ekstremvær"):
				storm_toasts.append((l as Label).text)
	for q in toasts._queue:
		if str(q.message).contains("Stormen gir") or str(q.message).contains("Ekstremvær"):
			storm_toasts.append(str(q.message))
	_check(storm_toasts.is_empty(), "alerts: storms from skipped days do not toast as news %s" % str(storm_toasts))
	var today := int(_sim.current_day)
	var stale := 0
	for a in _sim.active_alerts:
		if a.has("day") and today - int(a["day"]) >= 3:
			stale += 1
	_check(stale == 0, "alerts: no event alert outlives its three days after a skip (%d stale)" % stale)
	var storm_days := {}
	for e in _sim.recent_events:
		if str(e.get("category", "")).to_lower() == "weather":
			storm_days[int(e.get("day", 0))] = true
	for a in _sim.active_alerts:
		if str(a.get("category", "")) == "weather":
			storm_days[int(a.get("day", today))] = true
	var dash := await _open_panel(ui, "dashboard")
	dash._refresh_all()
	var weather: Dictionary = {}
	for r in dash.get_alert_rows():
		if r["category"] == "weather":
			weather = r
	_check(storm_days.size() >= 2 and int(weather.get("count", 0)) == storm_days.size(),
		"alerts: the weather row counts each storm day once (%d shown, %d storm days)" % [int(weather.get("count", 0)), storm_days.size()])
	_check(str(weather.get("time", "")) != tr("UI_TIME_NOW") or storm_days.has(today),
		"alerts: an old storm is dated, not 'Nå' ('%s')" % str(weather.get("time", "")))
	# Day by day to the next storm: its alert ("Nå") and its "Stormskade"
	# record are the same storm, counted once.
	var fresh := false
	for i in 40:
		_sim.advance_days(1)
		for a in _sim.active_alerts:
			if str(a.get("category", "")) == "weather" and int(a.get("day", -1)) == int(_sim.current_day):
				fresh = true
		if fresh:
			break
	storm_days.clear()
	for e in _sim.recent_events:
		if str(e.get("category", "")).to_lower() == "weather":
			storm_days[int(e.get("day", 0))] = true
	for a in _sim.active_alerts:
		if str(a.get("category", "")) == "weather":
			storm_days[int(a.get("day", 0))] = true
	dash._refresh_all()
	for r in dash.get_alert_rows():
		if r["category"] == "weather":
			weather = r
	_check(fresh and int(weather.get("count", 0)) == storm_days.size() and str(weather.get("time", "")) == tr("UI_TIME_NOW"),
		"alerts: today's storm reads 'Nå' and its log record is not a second storm (%d shown, %d storm days)" % [
			int(weather.get("count", 0)), storm_days.size()])
	scene.queue_free()
	await get_tree().process_frame

## U1-05: the map's names never sit on each other: city names keep clear of
## the region markers, of each other and of the grid labels.
func _test_map_labels_never_collide() -> void:
	var scene := await _spawn_po3_scene(20261012)
	var ui: Control = scene.get_node("UILayer/GameUI")
	var map := await _open_panel(ui, "map")
	if map != null and map.has_method("show_panel"):
		map.show_panel()
	for i in 4:
		await get_tree().process_frame
	var regions: Array[Rect2] = map.region_label_rects()
	var cities: Array[Rect2] = map.city_label_rects()
	var hits := []
	for c in cities:
		for r in regions:
			if c.intersects(r):
				hits.append([c, r])
	for i in cities.size():
		for j in range(i + 1, cities.size()):
			if cities[i].intersects(cities[j]):
				hits.append([cities[i], cities[j]])
	_check(regions.size() >= 26 and cities.size() >= 3,
		"map: markers and names are laid out (%d marker rects, %d names and grid labels)" % [regions.size(), cities.size()])
	_check(hits.is_empty(), "map: no name sits on a marker or another name %s" % str(hits.slice(0, 3)))
	scene.queue_free()
	await get_tree().process_frame

## U1-05: the pen detail fits the screen (the fourth KPI card was cut off at
## 1920 px) and its card bodies keep their padding.
func _test_pen_detail_fits_and_pads_its_cards() -> void:
	var scene := await _spawn_po3_scene(20261012)
	var ui: Control = scene.get_node("UILayer/GameUI")
	var pens := await _open_panel(ui, "pens")
	for i in 3:
		await get_tree().process_frame
	var view := get_viewport().get_visible_rect()
	var scroll: ScrollContainer = pens.get_node("%DetailScroll")
	var fcr: Control = pens.find_child("FcrCard", true, false)
	_check(fcr != null and scroll.visible and scroll.get_global_rect().encloses(fcr.get_global_rect())
		and view.encloses(fcr.get_global_rect()),
		"pens: the fourth KPI card lies inside the screen (%s)" % (str(fcr.get_global_rect()) if fcr else "missing"))
	_check(scroll.horizontal_scroll_mode == ScrollContainer.SCROLL_MODE_DISABLED, "pens: the detail never scrolls sideways")
	var padded := true
	for body_name in ["WaterBody", "HealthBody", "GeneBody"]:
		var body: Control = pens.find_child(body_name, true, false)
		var card: Control = body.get_parent().get_parent().get_parent() if body else null
		padded = padded and body != null and body.get_global_rect().position.x - card.get_global_rect().position.x >= 12.0
	_check(padded, "pens: card bodies sit 12 px or more inside their cards")
	scene.queue_free()
	await get_tree().process_frame

## U1-05: the market and crew panels show only what the sim knows.
func _test_market_and_crew_show_the_sims_own_numbers() -> void:
	var scene := await _spawn_po3_scene(20261012)
	var ui: Control = scene.get_node("UILayer/GameUI")
	_sim.advance_days(12)
	var con := await _open_panel(ui, "contracts")
	_check(con.find_child("RegionsCard", true, false) == null and con.find_child("BtnSellSpot", true, false) == null
		and con.find_child("BtnNewContract", true, false) == null, "market: no invented regional prices and no dead buttons")
	var series: Array[float] = _sim.get_analytics_data("market_price_nok", 84)
	var stats: Dictionary = con.price_stats(series)
	_check(series.size() > 0 and con.get_node("%SumLo").text == SimFormatters.num(stats["low"], 2)
		and con.get_node("%SumAvg").text == SimFormatters.num(stats["avg"], 2)
		and con.get_node("%SumHi").text == SimFormatters.num(stats["high"], 2),
		"market: low, average and high are the price series' own (%s / %s / %s)" % [
			con.get_node("%SumLo").text, con.get_node("%SumAvg").text, con.get_node("%SumHi").text])
	var terms: Dictionary = con.contract_terms(_sim.get_active_contracts())
	_check(con.get_node("%KpiVolumeValue").text == tr("UI_CONTRACTS_TONNES") % SimFormatters.num(terms["kg"] / 1000.0, 0),
		"market: the volume card is the volume under contract")
	var crew := await _open_panel(ui, "crew")
	var effects: Array = _sim.get_crew_role_effects()
	var tiles: Array = []
	for c in crew.get_node("%SkillsGrid").get_children():
		if c.has_meta(&"role") and not c.is_queued_for_deletion():
			tiles.append(c)
	_check(effects.size() == 3 and tiles.size() == 3, "crew: one tile per role the sim knows (%d)" % tiles.size())
	var covered := 0
	for e in effects:
		covered += 1 if bool(e["hired"]) else 0
	_check(crew.get_node("%KpiEfficiencyValue").text == tr("UI_CREW_ROLES_COVERED") % [covered, 3],
		"crew: the fourth card counts the roles covered ('%s')" % crew.get_node("%KpiEfficiencyValue").text)
	var storm_line := ""
	for e in effects:
		if e["role"] == "roekter":
			storm_line = Array(crew.role_effect_lines(e))[0]
			_check(storm_line == tr("UI_CREW_EFFECT_STORM") % SimFormatters.percent(1.0 - float(e["storm_loss"])),
				"crew: the røkter tile states the sim's storm-loss effect ('%s')" % storm_line)
	scene.queue_free()
	await get_tree().process_frame

# ── U2-06: typed event bus, one channel per severity ──────────────────────────

## Routed events by channel, plus the toasts shown, while a test listens.
class _EventTap:
	var routed: Array = []   # [event, channel]
	var toasts: Array = []   # [level, message]
	func on_routed(event: Dictionary, channel: String) -> void:
		routed.append([event, channel])
	func on_toast(message: String, level: String) -> void:
		toasts.append([level, message])
	func channel_count(channel: String, type_id: String = "") -> int:
		return routed.filter(func(r: Array) -> bool:
			return r[1] == channel and (type_id == "" or str(r[0].get("type", "")) == type_id)).size()
	func toasts_containing(text: String, level: String = "") -> int:
		return toasts.filter(func(t: Array) -> bool:
			return str(t[1]).contains(text) and (level == "" or t[0] == level)).size()

func _tap_events() -> _EventTap:
	var tap := _EventTap.new()
	get_node("/root/ConsequenceDirector").event_routed.connect(tap.on_routed)
	get_node("/root/ToastNotification").toast_shown.connect(tap.on_toast)
	return tap

func _untap_events(tap: _EventTap) -> void:
	get_node("/root/ConsequenceDirector").event_routed.disconnect(tap.on_routed)
	get_node("/root/ToastNotification").toast_shown.disconnect(tap.on_toast)

## Close every open critical card (Fortsett), so a test leaves no modal behind.
func _drain_cards(card: Control) -> void:
	var guard := 0
	while card.is_open() and guard < 50:
		card.continue_button.pressed.emit()
		guard += 1
	await get_tree().process_frame

## Acceptance 2: critical = one modal card each (and the pause), warning =
## exactly one toast plus an inbox entry and the badge, info = the log only.
## The routing reads the typed severity: the same scary words as info make
## no noise.
func _test_event_bus_one_channel_per_severity() -> void:
	GameplayPrefs.force_auto_pause(true)
	MotionPrefs.force(false)
	var scene := await _spawn_po3_scene(91)
	var card: Control = scene.get_node("%CriticalEventCard")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	var hud: Control = scene.get_node("UILayer/HUD")
	await _drain_cards(card)
	get_node("/root/ToastNotification").clear_all()
	center.clear_all()
	var tap := _tap_events()
	var shown_before: int = card.shown_count

	# Critical: the card opens at once and stops a running clock.
	_sim.set_paused(false)
	_sim.publish_condition_event("oxygen_critical", "critical", "", "CI: oksygen kritisk lavt 3,1 mg/L")
	_check(card.is_open() and card.shown_count == shown_before + 1, "events: a critical event opens one card")
	_check(_sim.is_paused(), "events: the card pauses the running game")
	_check(tap.toasts_containing("CI: oksygen") == 0, "events: a critical event is a card, not a toast")
	# A second critical while the card is open waits its turn: one card each.
	_sim.publish_condition_event("nets_critical", "critical", "", "CI: nøter kritisk slitt 31 %")
	_check(card.shown_count == shown_before + 1 and card.queued_count() == 1, "events: a second critical waits behind the open card")
	card.continue_button.pressed.emit()
	_check(card.is_open() and card.shown_count == shown_before + 2
		and str(card.current_event().get("type", "")) == "nets_critical", "events: Fortsett shows the waiting card")
	card.continue_button.pressed.emit()
	await get_tree().process_frame
	_check(not card.is_open() and not _sim.is_paused(), "events: after the last card the clock runs again")
	_sim.set_paused(true)
	_check(tap.channel_count("modal") == 2 and card.shown_count == shown_before + 2,
		"events: exactly one card per critical event (%d routed, %d shown)" % [tap.channel_count("modal"), card.shown_count - shown_before])

	# Warning: exactly one toast, an unread inbox entry and the badge.
	var badge_before: int = hud.unread_badge_count()
	_sim.publish_condition_event("storm_forecast", "warning", "", "CI: stormfare 64 %")
	_sim.publish_condition_event("storm_forecast", "warning", "", "CI: stormfare 64 %")
	await get_tree().create_timer(0.4).timeout
	_check(tap.toasts_containing("CI: stormfare", "WARNING") == 1, "events: a warning is exactly one toast (%d)" % tap.toasts_containing("CI: stormfare"))
	var storm_entries: Array = center._entries.filter(func(e: Dictionary) -> bool: return e["type"] == "storm_forecast")
	_check(storm_entries.size() == 1 and bool(storm_entries[0]["unread"]), "events: a warning is one unread inbox entry")
	_check(hud.unread_badge_count() == badge_before + 1, "events: the bell's badge counts it (%d -> %d)" % [badge_before, hud.unread_badge_count()])

	# Info: the log only, even with words that once meant alarm.
	_sim.publish_condition_event("wellboat_scheduled", "info", "po3_sor", "CI: storm rømming sykdom KRITISK", "delouse")
	await get_tree().create_timer(0.4).timeout
	_check(tap.toasts_containing("CI: storm rømming") == 0 and card.shown_count == shown_before + 2,
		"events: an info event makes no toast and no card, whatever its text says")
	_check(center._entries.any(func(e: Dictionary) -> bool: return e["type"] == "wellboat_scheduled" and not bool(e["unread"])),
		"events: an info event is logged in the inbox, read")
	_check(hud.unread_badge_count() == badge_before + 1, "events: info does not touch the badge")

	# Opening the inbox reads it: the badge goes.
	center.show_panel()
	await get_tree().process_frame
	_check(hud.unread_badge_count() == 0, "events: opening the inbox clears the badge")
	center.hide_panel()
	await get_tree().create_timer(0.3).timeout

	# Escalation: a warning turning critical is one entry and one card.
	_sim.publish_condition_event("lice_rising", "warning", "", "CI: lus forhøyet 0,31")
	_sim.publish_condition_event("lice_above_limit", "critical", "", "CI: lus KRITISK 0,61")
	_check(card.is_open() and bool(card.current_event().get("escalation", false)), "events: a warning turning critical opens the card as an escalation")
	await _drain_cards(card)
	var lice_entries: Array = center._entries.filter(func(e: Dictionary) -> bool: return e["key"] == "lice||")
	_check(lice_entries.size() == 1 and lice_entries[0]["severity"] == "critical",
		"events: the escalation raises the one inbox entry (%d entries)" % lice_entries.size())

	# History from days skipped at once is logged, not announced.
	var today := int(get_node("/root/GameBridge").CurrentDay)
	_check(ConsequenceDirector.route_for({"severity": "critical", "day": today - 3}, today) == "log"
		and ConsequenceDirector.route_for({"severity": "critical", "day": today - 2}, today) == "modal"
		and ConsequenceDirector.route_for({"severity": "warning", "day": today}, today) == "toast"
		and ConsequenceDirector.route_for({"severity": "info", "day": today}, today) == "log",
		"events: the routing rule is severity and age only")
	_untap_events(tap)
	GameplayPrefs.force_auto_pause(null)
	MotionPrefs.force(null)
	scene.queue_free()
	await get_tree().process_frame

## Acceptance 3: a real condition (lice over the limit) that holds for days,
## clears for a day and returns is one card; gone for longer than three days
## and back, it is news again.
func _test_repeated_condition_makes_no_duplicates() -> void:
	GameplayPrefs.force_auto_pause(true)
	var scene := await _spawn_po3_scene(91)
	var card: Control = scene.get_node("%CriticalEventCard")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	await _drain_cards(card)
	var tap := _tap_events()
	var lice_cards := func() -> int: return tap.channel_count("modal", "lice_above_limit")
	_sim.set_qa_pen_lice("po3_sor", 2.0)
	for day in 4:
		_sim.advance_days(1)
		await _drain_cards(card)
	_check(_sim.sea_lice_per_fish > 0.5, "repeats: lice stay over the limit (%.2f)" % _sim.sea_lice_per_fish)
	_check(lice_cards.call() == 1, "repeats: four days over the limit are one card (%d)" % lice_cards.call())
	_sim.set_qa_pen_lice("po3_sor", 0.0)
	_sim.advance_days(1)
	_sim.set_qa_pen_lice("po3_sor", 2.0)
	_sim.advance_days(1)
	await _drain_cards(card)
	_check(lice_cards.call() == 1, "repeats: cleared for a day and back is no new card (%d)" % lice_cards.call())
	var entries: Array = center._entries.filter(func(e: Dictionary) -> bool: return e["key"] == "lice||")
	_check(entries.size() == 1, "repeats: one lice entry in the inbox (%d)" % entries.size())
	_sim.set_qa_pen_lice("po3_sor", 0.0)
	for day in 5:
		_sim.advance_days(1)
	_sim.set_qa_pen_lice("po3_sor", 2.0)
	_sim.advance_days(1)
	await _drain_cards(card)
	_check(lice_cards.call() == 2, "repeats: gone for five days and back is news again (%d)" % lice_cards.call())
	_check(int(_sim.game_events_suppressed()) > 0, "repeats: the bus absorbed the repeats (%d)" % int(_sim.game_events_suppressed()))
	_untap_events(tap)
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## Acceptance 4: a critical event shakes the 3D camera through its view
## offset (h_offset / v_offset) and the shake settles back exactly; with
## reduced motion nothing moves.
func _test_critical_shake_moves_the_camera_offset() -> void:
	GameplayPrefs.force_auto_pause(true)
	MotionPrefs.force(false)
	var scene := await _spawn_po3_scene(91)
	var card: Control = scene.get_node("%CriticalEventCard")
	await _drain_cards(card)
	var cam := get_viewport().get_camera_3d()
	_check(cam is FjordCamera, "shake: the game's camera is the fjord camera")
	var base := Vector2(cam.h_offset, cam.v_offset)
	_sim.publish_condition_event("gill_health_critical", "critical", "po3_nord", "CI: gjellene kritisk")
	var moved := 0.0
	for i in 20:
		await get_tree().process_frame
		moved = maxf(moved, Vector2(cam.h_offset, cam.v_offset).distance_to(base))
	var shaker := cam.get_node_or_null("CameraShake") as CameraShake
	_check(moved > 0.01 and shaker != null and shaker.peak_offset > 0.01,
		"shake: a critical event moves the camera's view offset (%.3f m)" % moved)
	await get_tree().create_timer(1.2).timeout
	_check(Vector2(cam.h_offset, cam.v_offset).distance_to(base) < 0.0001, "shake: the offset settles back exactly")
	await _drain_cards(card)

	# Reduced motion: no shake through the router or directly; a running shake stops.
	MotionPrefs.force(true)
	_sim.publish_condition_event("oxygen_critical", "critical", "", "CI: oksygen kritisk")
	var still := 0.0
	for i in 20:
		await get_tree().process_frame
		still = maxf(still, Vector2(cam.h_offset, cam.v_offset).distance_to(base))
	_check(card.is_open() and still == 0.0, "shake: with reduced motion a critical event moves nothing (%.4f m)" % still)
	_check(not CameraShake.shake(get_viewport(), 1.0), "shake: reduced motion refuses a shake")
	MotionPrefs.force(false)
	CameraShake.shake(get_viewport(), 1.0)
	await get_tree().process_frame
	await get_tree().process_frame
	MotionPrefs.force(true)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(Vector2(cam.h_offset, cam.v_offset).distance_to(base) < 0.0001, "shake: turning reduced motion on stops a running shake")
	await _drain_cards(card)
	MotionPrefs.force(null)
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## The critical card: its verb opens the place it lives (the game stays paused
## for the decision), "Vis lokaliteten" flies to the site, Esc continues
## (not the pause menu) and the game's keys wait while it is open.
func _test_critical_card_actions() -> void:
	GameplayPrefs.force_auto_pause(true)
	var scene := await _spawn_po3_scene(91)
	var card: Control = scene.get_node("%CriticalEventCard")
	var ui: Control = scene.get_node("UILayer/GameUI")
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	await _drain_cards(card)
	_sim.set_paused(false)
	var lice_text := "Lus KRITISK: 0,90 / fisk (grense 0,5 — pålegg om tiltak)"
	_sim.publish_condition_event("lice_above_limit", "critical", "", lice_text)
	await get_tree().process_frame
	_check(card.verb_button.visible and card.verb_button.text == tr("UI_EVENT_VERB_DELOUSE"),
		"card: the lice card offers 'Planlegg avlusing' ('%s')" % card.verb_button.text)
	_check(card._title.text == tr("GAME_EVENT_LICE_ABOVE_LIMIT") and card._message.text == lice_text
		and card._meta.text.contains(tr("UI_EVENT_SUBJECT_COMPANY")), "card: title, subject and message are shown")
	# The card is Ink glass over the frosted game: themed, readable, Norwegian.
	await _lint_screen("critical event card")
	await _press_key(KEY_SPACE)
	_check(_sim.is_paused() and card.is_open(), "card: Space waits while the card is open")
	card.verb_button.pressed.emit()
	await get_tree().process_frame
	var pens: Control = ui.find_child("Panel_pens", true, false)
	_check(not card.is_open() and ui.get_active_tab() == "pens" and pens != null,
		"card: the verb opens the pen view (%s)" % ui.get_active_tab())
	_check(_sim.is_paused(), "card: after acting the game stays paused for the decision")
	# A site event: "Vis lokaliteten" goes to the fjord view.
	_sim.publish_condition_event("gill_health_critical", "critical", "po3_nord", "CI: gjeller")
	await get_tree().process_frame
	_check(card.site_button.visible, "card: a site's card offers 'Vis lokaliteten'")
	card.site_button.pressed.emit()
	await get_tree().process_frame
	_check(ui.get_active_tab() == "fjord" and not card.is_open(), "card: 'Vis lokaliteten' shows the fjord (%s)" % ui.get_active_tab())
	ui.show_tab("dashboard")
	# Esc continues: it never opens the pause menu behind the card.
	_sim.publish_condition_event("oxygen_critical", "critical", "", "CI: oksygen")
	await get_tree().process_frame
	await _press_key(KEY_ESCAPE)
	await get_tree().process_frame
	_check(not card.is_open() and not pause.is_open(), "card: Esc continues and leaves the pause menu shut")
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## The inbox shows what happened: every entry has its title, subject, day and
## message on screen (the old drawer listed rows that only said "Advarsel"),
## grouped with counts, and a warning's verb button acts.
func _test_inbox_lists_what_happened() -> void:
	var scene := await _spawn_po3_scene(91)
	var card: Control = scene.get_node("%CriticalEventCard")
	var ui: Control = scene.get_node("UILayer/GameUI")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	await _drain_cards(card)
	center.clear_all()
	_sim.publish_condition_event("mortality_high", "warning", "", "Dødelighet 1,4 % siste døgn")
	_sim.publish_condition_event("wellboat_scheduled", "info", "po3_sor", "Brønnbåt (avlusing) til PO3 Sør om 3 d", "delouse")
	center.show_panel()
	await get_tree().create_timer(0.4).timeout
	# The drawer is Ink glass: its entries are themed, readable and Norwegian.
	await _lint_screen("event inbox")
	var bad: Array = []
	var rows := 0
	for node in center.entry_list.get_children():
		if not String(node.name).begins_with("Entry_"):
			continue
		rows += 1
		var title: Label = node.find_child("Title", true, false)
		var desc: Label = node.find_child("Desc", true, false)
		var day: Label = node.find_child("Day", true, false)
		if title == null or title.text in ["", tr("UI_NOTIFY_GROUP_WARNING"), tr("UI_NOTIFY_GROUP_INFO"), tr("UI_NOTIFY_GROUP_CRITICAL")] \
				or desc == null or not desc.is_visible_in_tree() or desc.text == "" or day == null:
			bad.append(String(node.name))
	_check(rows == 2 and bad.is_empty(), "inbox: every entry shows its title, message and day (%d rows, bad %s)" % [rows, str(bad)])
	var header: Label = center.entry_list.find_child("Group_warning", true, false)
	_check(header != null and header.text == "%s · 1" % tr("UI_NOTIFY_GROUP_WARNING"), "inbox: group headers count their entries")
	var verb: Button = center.entry_list.find_child("VerbButton", true, false)
	_check(verb != null and verb.text == tr("UI_EVENT_VERB_CHECK_WATER"), "inbox: the warning offers its verb")
	if verb != null:
		verb.pressed.emit()
		await get_tree().create_timer(0.4).timeout
	_check(ui.get_active_tab() == "telemetry" and not center.is_open(), "inbox: the verb opens telemetry (%s)" % ui.get_active_tab())
	# A new game starts with an empty inbox, and keeps its own first events
	# even when they arrive before GameStarted reaches the inbox.
	_sim.start_qa_scenario("po3_red_sone", 91)
	await get_tree().process_frame
	_check(center._entries.all(func(e: Dictionary) -> bool: return int(e["day"]) <= 1 and not str(e["description"]).begins_with("CI:")),
		"inbox: a new game starts without the old game's events")
	_sim.publish_condition_event("temperature_high", "warning", "", "CI: ny sjøtemperatur 18,6 °C")
	get_node("/root/GameBridge").emit_signal("GameStarted")
	await get_tree().process_frame
	_check(center._entries.any(func(e: Dictionary) -> bool: return str(e["description"]) == "CI: ny sjøtemperatur 18,6 °C"),
		"inbox: the new game's own events stay through its GameStarted")
	scene.queue_free()
	await get_tree().process_frame

## Art review (wave 1): toasts covered the dashboard's Hurtigvalg actions and
## the notification header. Three toasts over the dashboard, at three window
## sizes, stay clear of both; with nothing to avoid they sit in the corner.
func _test_toasts_keep_clear_of_actions_and_inbox_header() -> void:
	var scene := await _spawn_po3_scene(91)
	var card: Control = scene.get_node("%CriticalEventCard")
	var ui: Control = scene.get_node("UILayer/GameUI")
	var center: Control = scene.get_node("UILayer/NotificationCenter")
	await _drain_cards(card)
	var toasts := get_node("/root/ToastNotification")
	var win := get_window()
	var old_size := win.size
	var dash := await _open_panel(ui, "dashboard")
	var actions: Control = dash.get_node("%ActionsCard")
	var header: Control = center.get_node("%TitleLabel").get_parent()
	for size: Vector2i in REGION_WINDOW_SIZES:
		win.size = size
		center.hide_panel()
		toasts.clear_all()
		await get_tree().create_timer(0.3).timeout
		for n in 3:
			toasts.show_toast("CI keep-clear toast %d" % n, "WARNING", 30.0)
		await get_tree().create_timer(1.2).timeout
		# The case that matters: Hurtigvalg sits where the corner stack would be.
		var screen := get_viewport().get_visible_rect()
		var corner_stack := Rect2(screen.end.x - toasts.STACK_MARGIN_RIGHT - toasts.TOAST_WIDTH,
			screen.end.y - toasts.STACK_MARGIN_BOTTOM - 3.0 * 83.0, toasts.TOAST_WIDTH, 3.0 * 83.0)
		_check(actions.is_visible_in_tree() and actions.get_global_rect().intersects(corner_stack),
			"toasts: Hurtigvalg sits in the toasts' corner at %dx%d (%s)" % [size.x, size.y, str(actions.get_global_rect())])
		var rects: Array = toasts.get_toast_rects()
		var over_actions := rects.filter(func(r: Rect2) -> bool: return r.intersects(actions.get_global_rect(), true))
		_check(rects.size() == 3 and over_actions.is_empty(),
			"toasts: none covers Hurtigvalg at %dx%d (%d over)" % [size.x, size.y, over_actions.size()])
		center.show_panel()
		await get_tree().create_timer(0.5).timeout
		rects = toasts.get_toast_rects()
		var over_header := rects.filter(func(r: Rect2) -> bool: return r.intersects(header.get_global_rect(), true) or r.intersects(center.panel.get_global_rect(), true))
		_check(not rects.is_empty() and over_header.is_empty(), "toasts: none covers the inbox or its header at %dx%d" % [size.x, size.y])
	# Nothing to keep clear (the fjord view): the corner.
	center.hide_panel()
	ui.show_tab("fjord")
	toasts.clear_all()
	await get_tree().create_timer(0.3).timeout
	toasts.show_toast("CI corner toast", "INFO", 30.0)
	await get_tree().create_timer(0.8).timeout
	var corner := get_viewport().get_visible_rect().end - Vector2(toasts.STACK_MARGIN_RIGHT, toasts.STACK_MARGIN_BOTTOM)
	var shown: Array = toasts.get_toast_rects()
	_check(not shown.is_empty() and (shown[0] as Rect2).end.distance_to(corner) < 2.0 and is_zero_approx(toasts.keep_clear_lift()),
		"toasts: with nothing to keep clear they sit in the corner")
	toasts.clear_all()
	win.size = old_size
	scene.queue_free()
	await get_tree().process_frame

# ── U2-07: pacing (briefing, calendar HUD, speeds) ─────────────────────────────

## Wait (real time, at most `timeout_s`) until `condition` holds; true if it did.
func _wait_until(condition: Callable, timeout_s: float) -> bool:
	var end := Time.get_ticks_msec() + int(timeout_s * 1000.0)
	while Time.get_ticks_msec() < end:
		if condition.call():
			return true
		await get_tree().process_frame
	return bool(condition.call())

## A real Shift+key press and release.
func _press_shift_key(keycode: Key) -> void:
	for pressed in [true, false]:
		var ev := InputEventKey.new()
		ev.keycode = keycode
		ev.physical_keycode = keycode
		ev.shift_pressed = true
		ev.pressed = pressed
		get_viewport().push_input(ev)
	await get_tree().process_frame

## Acceptance 2: the HUD shows the sim's calendar in ISO weeks: "Uke 14 ·
## april · År 1" on Monday 5 April 2027 (day 94), the day number above it and
## the long date in its tooltip; English says "Week 14 · April · Year 1"; the
## second year counts on. The bar is themed, readable and Norwegian.
func _test_hud_shows_the_sim_calendar() -> void:
	var scene := await _spawn_po3_scene(91)
	await _drain_cards(scene.get_node("%CriticalEventCard"))
	var hud: Control = scene.get_node("UILayer/HUD")
	var anchors := {0: 53, 2: 53, 3: 1, 94: 14, 364: 52, 365: 52, 367: 1, 400: 5}
	var wrong := []
	for d: int in anchors:
		if GameCalendar.iso_week(d) != int(anchors[d]):
			wrong.append("%d: %d" % [d, GameCalendar.iso_week(d)])
	_check(wrong.is_empty(), "calendar: ISO weeks of the anchor days (wrong %s)" % str(wrong))
	_check(GameCalendar.long_date(0) == "fredag 1. januar 2027" and GameCalendar.short_date(94) == "5. april",
		"calendar: day 0 is Friday 1 January 2027 ('%s')" % GameCalendar.long_date(0))
	_sim.advance_days(94 - int(_sim.current_day))
	await get_tree().process_frame
	_check(hud.calendar_value.text == "Uke 14 · april · År 1",
		"calendar: the HUD reads 'Uke 14 · april · År 1' on day 94 ('%s')" % hud.calendar_value.text)
	_check(hud.day_caption.text == "Dag 94", "calendar: the day number sits above it ('%s')" % hud.day_caption.text)
	_check(hud.calendar_item.tooltip_text.begins_with("mandag 5. april 2027"),
		"calendar: the tooltip has the long date ('%s')" % hud.calendar_item.tooltip_text)
	await _lint_screen("HUD with the calendar and time controls")
	TranslationServer.set_locale("en")
	_check(GameCalendar.hud_label(94) == "Week 14 · April · Year 1", "calendar: in English '%s'" % GameCalendar.hud_label(94))
	TranslationServer.set_locale("no")
	_sim.advance_days(400 - int(_sim.current_day))
	await get_tree().process_frame
	_check(hud.calendar_value.text == "Uke 5 · februar · År 2",
		"calendar: the second year counts on ('%s')" % hud.calendar_value.text)
	scene.queue_free()
	await get_tree().process_frame

## Acceptance 3 (speeds): the keys 1–4 run the clock at 1×, 3×, 10× and 30×
## and the HUD's segments follow it; a click on a segment does the same; Space
## pauses (the play glyph shows) and resumes at the same speed; a digit with
## Shift (a lens, D15) or Cmd/Ctrl (a tab) leaves the speed alone. The speeds
## are real: at 30× a second runs weeks, at 1× it runs at most a day.
func _test_speed_keys_and_buttons_drive_the_clock() -> void:
	GameplayPrefs.force_auto_pause(true)
	var scene := await _spawn_game_scene()
	var hud: Control = scene.get_node("UILayer/HUD")
	var card: Control = scene.get_node("%CriticalEventCard")
	await _drain_cards(card)
	get_viewport().gui_release_focus()
	var keys := [KEY_1, KEY_2, KEY_3, KEY_4]
	var speeds := [1, 3, 10, 30]
	var segments: Array = [hud.speed_1x, hud.speed_3x, hud.speed_10x, hud.speed_30x]
	for i in 4:
		_sim.set_paused(true)
		await _press_key(keys[i], 49 + i)
		var lit := segments.map(func(b: Button) -> bool: return b.button_pressed)
		var want := [i == 0, i == 1, i == 2, i == 3]
		_check(int(_sim.get_time_speed()) == speeds[i] and not _sim.is_paused() and lit == want,
			"speeds: key %d runs the clock at %d× and lights its segment (%s×, paused %s, %s)" % [
				i + 1, speeds[i], str(_sim.get_time_speed()), str(_sim.is_paused()), str(lit)])
	_check(hud.speed_30x.tooltip_text == "30×: en måned i sekundet (4)", "speeds: the 30× tooltip says what it means")
	await _press_key(KEY_SPACE, 32)
	_check(_sim.is_paused() and hud.pause_glyph.kind == TimeGlyph.Kind.PLAY and hud.pause_btn.button_pressed,
		"speeds: Space pauses and the pause button shows play")
	await _press_shift_key(KEY_1)
	await _press_command_key(KEY_2)
	_check(_sim.is_paused() and int(_sim.get_time_speed()) == 30,
		"speeds: Shift+1 and Cmd/Ctrl+2 leave the clock alone (%s×, paused %s)" % [str(_sim.get_time_speed()), str(_sim.is_paused())])
	await _press_key(KEY_SPACE, 32)
	_check(not _sim.is_paused() and int(_sim.get_time_speed()) == 30, "speeds: Space resumes at the same speed")
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("dashboard")
	await _click_control(hud.speed_10x)
	_check(int(_sim.get_time_speed()) == 10 and not _sim.is_paused() and hud.speed_10x.button_pressed,
		"speeds: a click on 10× runs the clock at 10×")
	# Real time: 30× runs about a month a second; 1× at most a day.
	_sim.set_time_speed(30.0)
	_sim.set_paused(false)
	var day0: int = _sim.current_day
	await get_tree().create_timer(0.8).timeout
	var fast := int(_sim.current_day) - day0
	_sim.set_time_speed(1.0)
	var day1: int = _sim.current_day
	await get_tree().create_timer(0.8).timeout
	var slow := int(_sim.current_day) - day1
	_sim.set_paused(true)
	_check(fast >= 8 and slow <= 1, "speeds: 0.8 s runs %d days at 30× and %d at 1×" % [fast, slow])
	await _drain_cards(card)
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## Acceptance 3 ("next event"): N runs the clock at 30× and the chip says so;
## information does not stop it; the next warning stops it, paused, at the
## speed the player had before, and the chip names the event; N during a run
## ends it and keeps the clock going at the speed before.
func _test_next_event_runs_to_the_next_warning() -> void:
	var scene := await _spawn_game_scene()
	var hud: Control = scene.get_node("UILayer/HUD")
	await _drain_cards(scene.get_node("%CriticalEventCard"))
	get_viewport().gui_release_focus()
	_sim.set_time_speed(3.0)
	_sim.set_paused(true)
	await _press_key(KEY_N, 110)
	_check(_sim.is_running_to_next_event() and int(_sim.get_time_speed()) == 30 and not _sim.is_paused(),
		"next event: N runs the clock at 30× (%s×)" % str(_sim.get_time_speed()))
	_check(hud.next_event_btn.button_pressed and hud.pacing_chip.visible and hud.pacing_label.text == "Spoler til neste hendelse",
		"next event: the button is lit and the chip says the clock runs ahead ('%s')" % hud.pacing_label.text)
	_sim.publish_condition_event("storm_damage", "", "", "CI: storm i loggen", "ci-next-info")
	_check(_sim.is_running_to_next_event(), "next event: information does not stop the run")
	_sim.publish_condition_event("oxygen_low", "warning", "", "CI: oksygen 6,1 mg/L", "ci-next")
	await get_tree().process_frame
	_check(not _sim.is_running_to_next_event() and _sim.is_paused() and int(_sim.get_time_speed()) == 3,
		"next event: a warning stops it, paused, at the player's 3× (%s×)" % str(_sim.get_time_speed()))
	_check(hud.pacing_label.text == "Stoppet · " + tr("GAME_EVENT_OXYGEN_LOW") and not hud.next_event_btn.button_pressed,
		"next event: the chip names what stopped it ('%s')" % hud.pacing_label.text)
	await _press_key(KEY_N, 110)
	await _press_key(KEY_N, 110)
	_check(not _sim.is_running_to_next_event() and not _sim.is_paused() and int(_sim.get_time_speed()) == 3,
		"next event: N during a run ends it and the clock keeps its 3× (%s×)" % str(_sim.get_time_speed()))
	_sim.set_paused(true)
	scene.queue_free()
	await get_tree().process_frame

## Acceptance 3 (auto-slow, auto-pause): the farm-types deadline rule on the
## real clock. A counterable warning with a deadline nine days out slows a 30×
## clock to 1× at seven days left (the chip names the deadline); picked up to
## 30× again it stops on the day before the deadline. With "Bremse ned før
## frister" off a deadline never touches the clock. The deadline comes
## through the typed event bus, as an order from the engine does.
func _test_deadline_slows_then_stops_the_clock() -> void:
	GameplayPrefs.force_auto_pause(true)
	var scene := await _spawn_game_scene()
	var hud: Control = scene.get_node("UILayer/HUD")
	var card: Control = scene.get_node("%CriticalEventCard")
	await _drain_cards(card)
	_sim.set_auto_slow_deadlines(true)
	var due: int = int(_sim.current_day) + 9
	_sim.set_time_speed(30.0)
	_sim.set_paused(false)
	_sim.publish_condition_event("lice_treatment_order", "warning", "ci_site", "CI: pålegg om avlusing", "", due)
	var slowed: bool = await _wait_until(func() -> bool: return int(_sim.get_time_speed()) == 1 or _sim.is_paused(), 6.0)
	_check(slowed and int(_sim.get_time_speed()) == 1 and not _sim.is_paused() and due - int(_sim.current_day) == 7,
		"deadline: 30× drops to 1× at seven days left (day %d, due %d, %s×)" % [_sim.current_day, due, str(_sim.get_time_speed())])
	_check(hud.pacing_chip.visible and hud.pacing_label.text == "Bremset · frist " + GameCalendar.short_date(due),
		"deadline: the chip says the clock slowed for the deadline ('%s')" % hud.pacing_label.text)
	_check(hud.pacing_chip.tooltip_text.contains(tr("GAME_EVENT_LICE_TREATMENT_ORDER")),
		"deadline: its tooltip names the order")
	_sim.set_time_speed(30.0)
	var stopped: bool = await _wait_until(func() -> bool: return _sim.is_paused(), 6.0)
	_check(stopped and int(_sim.current_day) == due - 1,
		"deadline: back at 30× it stops on the day before the deadline (day %d, due %d)" % [_sim.current_day, due])
	_check(hud.pacing_label.text == "Stoppet · frist " + GameCalendar.short_date(due) and hud.pause_btn.button_pressed,
		"deadline: the chip says why it stopped ('%s')" % hud.pacing_label.text)
	var deadlines: Array = _sim.get_pacing_deadlines()
	_check(deadlines.any(func(d: Dictionary) -> bool: return str(d.get("type", "")) == "lice_treatment_order" and int(d.get("deadline_day", -1)) == due),
		"deadline: the clock lists the deadline it watches")
	_sim.set_auto_slow_deadlines(false)
	var due2: int = int(_sim.current_day) + 4
	_sim.publish_condition_event("lice_treatment_order", "warning", "ci_site_2", "CI: pålegg 2", "", due2)
	_sim.set_time_speed(30.0)
	_sim.set_paused(false)
	var ran: bool = await _wait_until(func() -> bool: return int(_sim.current_day) > due2 or _sim.is_paused(), 6.0)
	_check(ran and int(_sim.current_day) > due2 and not _sim.is_paused() and int(_sim.get_time_speed()) == 30,
		"deadline: with 'Bremse ned før frister' off the clock runs past it (day %d, due %d)" % [_sim.current_day, due2])
	_sim.set_paused(true)
	_sim.set_auto_slow_deadlines(true)
	await _drain_cards(card)
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## An order the engine does not have (met, lifted or enforced) stops being
## watched on the next day's report, the chip that named it goes and the
## clock is not stopped for it. (A real site, so the engine's standing-order
## sweep sees there is no order.)
func _test_a_met_order_stops_slowing_the_clock() -> void:
	GameplayPrefs.force_auto_pause(false)
	var scene := await _spawn_po3_scene(91)
	var hud: Control = scene.get_node("UILayer/HUD")
	var card: Control = scene.get_node("%CriticalEventCard")
	await _drain_cards(card)
	_sim.set_auto_slow_deadlines(true)
	_sim.set_time_speed(30.0)
	_sim.set_paused(false)
	var due: int = int(_sim.current_day) + 5
	_sim.publish_condition_event("lice_treatment_order", "warning", "po3_nord", "CI: et pålegg som ikke står", "", due)
	_check(int(_sim.get_time_speed()) == 1 and hud.pacing_chip.visible and hud.pacing_label.text.begins_with("Bremset"),
		"orders: an order five days out slows the clock at once ('%s')" % hud.pacing_label.text)
	var day: int = _sim.current_day
	var gone: bool = await _wait_until(func() -> bool: return int(_sim.current_day) > day and not hud.pacing_chip.visible, 4.0)
	var watched: bool = _sim.get_pacing_deadlines().any(func(d: Dictionary) -> bool: return str(d.get("site_id", "")) == "po3_nord")
	_check(gone and not watched and not _sim.is_paused(),
		"orders: an order the engine does not have stops being watched, its chip goes and the clock runs (day %d)" % _sim.current_day)
	_sim.set_paused(true)
	await _drain_cards(card)
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## U2-06's critical channel through the clock (one owner): a critical's card
## holds the clock and Fortsett lets go of it at the speed it had; with "Pause
## ved kritiske hendelser" off a critical drops a fast clock to 1×; a critical
## during "Til neste hendelse" ends the run, and after the card the game waits
## paused at the player's speed.
func _test_critical_card_holds_the_clock() -> void:
	GameplayPrefs.force_auto_pause(true)
	var scene := await _spawn_po3_scene(91)
	var hud: Control = scene.get_node("UILayer/HUD")
	var card: Control = scene.get_node("%CriticalEventCard")
	await _drain_cards(card)
	_sim.set_time_speed(10.0)
	_sim.set_paused(false)
	_sim.publish_condition_event("nets_critical", "critical", "", "CI: nøtene er kritisk slitt 31 %", "ci-hold")
	await get_tree().process_frame
	_check(card.is_open() and _sim.is_clock_held("critical_event") and _sim.is_paused() and not _sim.is_player_paused(),
		"clock: the card holds the clock")
	_check(hud._is_paused and not hud.pacing_chip.visible, "clock: the HUD shows it stopped, with no chip over the card")
	card.continue_button.pressed.emit()
	await get_tree().process_frame
	_check(not _sim.is_paused() and int(_sim.get_time_speed()) == 10 and not _sim.is_clock_held(""),
		"clock: Fortsett gives it back at 10×")
	GameplayPrefs.force_auto_pause(false)
	_sim.set_time_speed(30.0)
	_sim.publish_condition_event("oxygen_critical", "critical", "", "CI: oksygen 3,9 mg/L", "ci-slow")
	await get_tree().process_frame
	_check(card.is_open() and not _sim.is_paused() and int(_sim.get_time_speed()) == 1,
		"clock: without auto-pause a critical drops 30× to 1× (%s×)" % str(_sim.get_time_speed()))
	_sim.set_paused(true)
	await _drain_cards(card)
	GameplayPrefs.force_auto_pause(true)
	_sim.set_time_speed(3.0)
	_check(_sim.run_to_next_event(), "clock: a run to the next event starts")
	_sim.publish_condition_event("gill_health_critical", "critical", "po3_nord", "CI: gjellene", "ci-run")
	await get_tree().process_frame
	_check(card.is_open() and not _sim.is_running_to_next_event(), "clock: a critical ends the run behind its card")
	card.continue_button.pressed.emit()
	await get_tree().process_frame
	_check(_sim.is_paused() and _sim.is_player_paused() and int(_sim.get_time_speed()) == 3,
		"clock: after the card the game waits, paused at the player's 3× (%s×)" % str(_sim.get_time_speed()))
	await _drain_cards(card)
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## Acceptance 1: a player's new game opens paused on its briefing card, over
## the fjord (the game UI steps aside): the scenario, its date in the sim
## calendar, the goal, the time limit with its last day, the capital, the
## farm and the clock's keys. The game's keys wait while it is open, and a
## critical that comes meanwhile waits behind it. Enter starts the season at
## the default speed and brings the game UI back; Esc closes it with the game
## still paused, and Space then starts the clock. QA starts and loaded games
## open without one, and a loaded game opens paused.
func _test_new_game_opens_paused_on_its_briefing() -> void:
	GameplayPrefs.force_auto_pause(true)
	MotionPrefs.force(true)
	_check(_sim.start_new_game("Player", 10_000_000, "po3_red_sone"), "briefing: a player's scenario starts")
	_check(_sim.is_briefing_pending() and _sim.is_paused() and _sim.is_clock_held("briefing"),
		"briefing: the new game starts paused, held by its briefing")
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 4:
		await get_tree().process_frame
	var briefing: Control = scene.get_node("%BriefingCard")
	_check(briefing.is_open() and briefing.visible and not scene.get_node("UILayer").visible,
		"briefing: the card opens over the fjord, the game UI steps aside")
	var entry := {}
	for e in _sim.get_available_scenarios():
		if str(e.get("id", "")) == "po3_red_sone":
			entry = e
	_check(briefing._title.text == str(entry.get("name", "?")) and briefing._story.text == str(entry.get("story", "?")),
		"briefing: the scenario's name and story ('%s')" % briefing._title.text)
	_check(briefing._date.text == "Fredag 1. januar 2027 · Uke 53 · År 1", "briefing: the start date ('%s')" % briefing._date.text)
	var facts := {}
	for lbl: Label in briefing._facts.get_children():
		facts[String(lbl.name)] = lbl.text
	_check(str(facts.get("Fact_goal", "")) == str(entry.get("win", "?")), "briefing: the goal ('%s')" % str(facts.get("Fact_goal", "")))
	_check(str(facts.get("Fact_time", "")) == "3 år (til søndag 31. desember 2029)", "briefing: the time limit and its last day ('%s')" % str(facts.get("Fact_time", "")))
	_check(str(facts.get("Fact_capital", "")) == SimFormatters.format_nok(float(_sim.balance)), "briefing: the capital ('%s')" % str(facts.get("Fact_capital", "")))
	var fish := 0
	for site in _sim.get_all_pens():
		fish += int(site.get("fish_count", 0))
	_check(str(facts.get("Fact_farm", "")) == "2 lokaliteter · %s fisk" % SimFormatters.integer(fish), "briefing: the farm ('%s')" % str(facts.get("Fact_farm", "")))
	_check(briefing._keys.get_child_count() == 3, "briefing: the clock's three keys are shown")
	await _lint_screen("briefing card")
	var day: int = _sim.current_day
	await _press_key(KEY_SPACE, 32)
	await _press_key(KEY_4, 52)
	await _press_key(KEY_N, 110)
	await get_tree().create_timer(0.3).timeout
	_check(_sim.is_paused() and int(_sim.current_day) == day and briefing.is_open() and not _sim.is_running_to_next_event(),
		"briefing: Space, 4 and N wait while it is open")
	var card: Control = scene.get_node("%CriticalEventCard")
	_sim.publish_condition_event("nets_critical", "critical", "", "CI: nøtene er kritisk slitt 30 %", "ci-briefing")
	_check(not card.is_open() and card.queued_count() >= 1, "briefing: a critical waits behind the briefing")
	_check(briefing.start_button.has_focus(), "briefing: 'Start sesongen' has the focus")
	await _press_key(KEY_ENTER)
	await get_tree().process_frame
	_check(not briefing.is_open() and not _sim.is_briefing_pending() and scene.get_node("UILayer").visible,
		"briefing: Enter starts the season and the game UI comes back")
	_check(int(_sim.get_time_speed()) == GameplayPrefs.default_speed() and not _sim.is_clock_held("briefing"),
		"briefing: the clock runs at the default speed (%s×)" % str(_sim.get_time_speed()))
	_check(card.is_open() and _sim.is_clock_held("critical_event"), "briefing: the waiting critical opens after it and holds the clock")
	await _drain_cards(card)
	_check(not _sim.is_paused(), "briefing: then the clock runs")
	_sim.set_paused(true)
	scene.queue_free()
	await get_tree().process_frame

	_check(_sim.start_new_game("Player", 10_000_000, "po3_red_sone"), "briefing: a second new game")
	var scene2: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene2)
	for i in 4:
		await get_tree().process_frame
	var briefing2: Control = scene2.get_node("%BriefingCard")
	var card2: Control = scene2.get_node("%CriticalEventCard")
	await _press_key(KEY_ESCAPE)
	await get_tree().process_frame
	var pause: Control = scene2.get_node("MenuLayer/PauseMenu")
	_check(not briefing2.is_open() and _sim.is_paused() and not _sim.is_clock_held("") and not pause.is_open()
		and scene2.get_node("UILayer").visible, "briefing: Esc looks around first: closed, the game paused, no pause menu")
	# Criticals the start raised waited behind the card; answer them first.
	await _drain_cards(card2)
	get_viewport().gui_release_focus()
	await _press_key(KEY_SPACE, 32)
	_check(not _sim.is_paused(), "briefing: then Space starts the clock")
	_sim.set_paused(true)
	_check(_sim.save_game("ci_briefing_load"), "briefing: setup save")
	_sim.set_paused(false)
	var file := str(_find_save("ci_briefing_load").get("file", ""))
	_check(_sim.load_game(file) and _sim.is_paused() and not _sim.is_briefing_pending(),
		"briefing: a loaded game opens paused, without a briefing")
	await get_tree().process_frame
	_check(not briefing2.is_open(), "briefing: no card for a loaded game")
	_sim.delete_save(file)
	scene2.queue_free()
	await get_tree().process_frame
	_check(_sim.start_qa_scenario("po3_red_sone", 91) and not _sim.is_briefing_pending(), "briefing: a QA start has none")
	_sim.set_paused(true)
	MotionPrefs.force(null)
	GameplayPrefs.force_auto_pause(null)

## Acceptance 4: at 30× the dashboard refreshes at most once per sim day (a
## day used to refresh it four times: two snapshot refreshes, each announcing
## a state update and a day), its alert rows are rebuilt only when they
## change, and the UI side of every day (the bridge's tick minus the engine's
## own day: the events, the snapshot and every listener, the dashboard's
## refresh in the frame after it included) stays within 16.7 ms at p95.
func _test_thirty_times_refreshes_once_a_day_within_a_frame() -> void:
	GameplayPrefs.force_auto_pause(false)
	var scene := await _spawn_po3_scene(91)
	var card: Control = scene.get_node("%CriticalEventCard")
	await _drain_cards(card)
	_sim.set_auto_slow_deadlines(false)
	var bridge := get_node("/root/GameBridge")
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("dashboard")
	await get_tree().process_frame
	var dash: Control = ui.find_child("Panel_dashboard", true, false)
	_sim.set_time_speed(30.0)
	_sim.set_paused(false)
	# Steady state: the first month carries the one-off costs (the first
	# tick's warm-up, the day-30 autosave, which then waits a real minute).
	var warm_end := Time.get_ticks_msec() + 4000
	while Time.get_ticks_msec() < warm_end and int(_sim.current_day) < 32:
		await get_tree().process_frame
		if card.is_open():
			card.continue_button.pressed.emit()
		if int(_sim.get_time_speed()) != 30:
			_sim.set_time_speed(30.0)
	var refreshes0: int = dash.refresh_count
	var rebuilds0: int = dash.alert_rebuilds
	var day0: int = _sim.current_day
	# Wave 3 ui-a: one snapshot a day (the tick's GameStateUpdated no longer
	# refreshes it a second time), and the 3D sites touch only what changed.
	var snapshots := [0]
	var count_snapshot := func() -> void: snapshots[0] += 1
	_sim.state_updated.connect(count_snapshot)
	var body_writes0: int = FishSchool.body_writes
	var pen_count := 0
	for site in _sim.get_all_pens():
		pen_count += int(site.get("pen_count", 0))
	var ui_ms: Array[float] = []
	var engine_ms: Array[float] = []
	var ticks0: int = bridge.TickCount
	var last_tick: int = ticks0
	var deferred_ms := 0.0
	var end := Time.get_ticks_msec() + 2500
	while Time.get_ticks_msec() < end:
		var refresh_before: int = dash.refresh_count
		var t0 := Time.get_ticks_usec()
		await get_tree().process_frame
		# The dashboard refreshes deferred, in the frame after its day.
		if dash.refresh_count != refresh_before:
			deferred_ms = dash.last_refresh_ms
		if int(bridge.TickCount) != last_tick:
			last_tick = int(bridge.TickCount)
			ui_ms.append(float(bridge.LastTickMs) - float(bridge.LastTickEngineMs) + deferred_ms)
			engine_ms.append(float(bridge.LastTickEngineMs))
			deferred_ms = 0.0
		if card.is_open():
			card.continue_button.pressed.emit()
		if int(_sim.get_time_speed()) != 30:
			_sim.set_time_speed(30.0)
	_sim.set_paused(true)
	await get_tree().process_frame
	_sim.state_updated.disconnect(count_snapshot)
	var days: int = int(_sim.current_day) - day0
	var refreshes: int = dash.refresh_count - refreshes0
	var rebuilds: int = dash.alert_rebuilds - rebuilds0
	var body_writes: int = FishSchool.body_writes - body_writes0
	_check(days >= 20, "30×: the clock ran %d days in 2.5 s" % days)
	_check(snapshots[0] <= days + 2, "30×: the sim snapshot refreshed %d times for %d days (once a day, not twice)" % [snapshots[0], days])
	_check(pen_count >= 10 and body_writes * 3 <= pen_count * days,
		"30×: the schools' uniforms were rewritten %d times over %d pen-days (only visible changes)" % [body_writes, pen_count * days])
	_check(refreshes <= days + 1, "30×: the dashboard refreshed %d times for %d days (at most once a day)" % [refreshes, days])
	_check(rebuilds < refreshes, "30×: alert rows were rebuilt %d times in %d refreshes (only on change)" % [rebuilds, refreshes])
	ui_ms.sort()
	engine_ms.sort()
	var p95: float = ui_ms[mini(int(ui_ms.size() * 0.95), ui_ms.size() - 1)] if not ui_ms.is_empty() else 999.0
	_check(p95 <= 16.7, "30×: p95 of the UI side of a day %.1f ms <= 16.7 ms (%d days)" % [p95, ui_ms.size()])
	if not ui_ms.is_empty():
		print("  info 30× UI side ms per day: p50 %.2f p95 %.2f max %.2f; engine p50 %.2f p95 %.2f max %.2f (%d days)" % [
			ui_ms[ui_ms.size() / 2], p95, ui_ms.back(), engine_ms[engine_ms.size() / 2],
			engine_ms[mini(int(engine_ms.size() * 0.95), engine_ms.size() - 1)], engine_ms.back(), ui_ms.size()])
	_sim.set_auto_slow_deadlines(true)
	await _drain_cards(card)
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## U2-04: the Fôringsplan card on the Pens tab, driven by clicks. A site
## starts on the standard plan; the "Begrenset" stop moves the slider to the
## best-conversion ration and the projection shows less growth, a better FCR
## and a lower feed cost a day than full appetite; "Bruk" sets the site's plan
## (every pen's ration, the recipe, the window) and leaves the other site
## alone; "Fôr alle" gives every site the plan (acceptance 3); the plan comes
## back with a saved game (acceptance 4) and "Tilbake til standard" clears it.
func _test_feed_plan_card_sets_a_plan_and_feeds_all() -> void:
	var scene := await _spawn_po3_scene(91)
	await _drain_cards(scene.get_node("%CriticalEventCard"))
	var ui: Control = scene.get_node("UILayer/GameUI")
	var panel := await _open_panel(ui, "pens")
	var sites: Array = _sim.get_all_pens()
	var site_id := str(sites[0].get("id", "")) if sites.size() > 1 else ""
	var other_id := str(sites[1].get("id", "")) if sites.size() > 1 else ""
	panel.select_pen(site_id)
	await get_tree().process_frame
	var card: FeedPlanCard = panel.get_node("%FeedPlanCard")
	var scroll: ScrollContainer = panel.get_node("%DetailScroll")
	scroll.ensure_control_visible(card._btn_apply)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(card.is_visible_in_tree() and card._status.text == tr("FEEDPLAN_STATUS_STANDARD")
			and is_equal_approx(card._ration_slider.value, 100.0) and card._btn_apply.disabled and card._btn_reset.disabled,
		"feed plan: a site starts on the standard plan, nothing to apply or reset (%s, %.0f %%)" % [card._status.text, card._ration_slider.value])
	_check(card._recipe_option.item_count == 3 and card._window_option.item_count == card.WINDOWS.size()
			and card._recipe_option.get_item_text(0).contains("NOK/kg"),
		"feed plan: three feed types with their price and %d windows to choose from (%s)" % [card.WINDOWS.size(), card._recipe_option.get_item_text(0)])

	await _click_control(card._stop_buttons[0])
	var plan: Dictionary = card.last_preview.get("plan", {})
	var base: Dictionary = card.last_preview.get("baseline", {})
	_check(is_equal_approx(card._ration_slider.value, 70.0) and card._stop_buttons[0].button_pressed
			and not card._stop_buttons[2].button_pressed and card._ration_value.text.begins_with("70"),
		"feed plan: the Begrenset stop sets 70 %% of appetite (%s)" % card._ration_value.text)
	_check(bool(plan.get("ok", false)) and float(plan.get("growth_kg_per_day", 0.0)) < float(base.get("growth_kg_per_day", 0.0))
			and float(plan.get("fcr", 9.0)) < float(base.get("fcr", 0.0))
			and float(plan.get("cost_nok_per_day", 9e9)) < float(base.get("cost_nok_per_day", 0.0)),
		"feed plan: at 70 %% the projection grows less (%.0f vs %.0f kg/day), converts better (%.3f vs %.3f) and costs less a day (%.0f vs %.0f NOK)" % [
			float(plan.get("growth_kg_per_day", 0.0)), float(base.get("growth_kg_per_day", 0.0)), float(plan.get("fcr", 0.0)),
			float(base.get("fcr", 0.0)), float(plan.get("cost_nok_per_day", 0.0)), float(base.get("cost_nok_per_day", 0.0))])
	var growth_change: String = card._rows["growth"][2].text
	var cost_change: String = card._rows["cost"][2].text
	_check(growth_change.begins_with("−") and cost_change.begins_with("−") and not card._btn_apply.disabled,
		"feed plan: the card shows the change against full appetite (growth %s, cost %s) and offers to apply it" % [growth_change, cost_change])

	card._recipe_option.select(1)
	card._recipe_option.item_selected.emit(1)
	card._window_option.select(2)
	card._window_option.item_selected.emit(2)
	await get_tree().process_frame
	await _click_control(card._btn_apply)
	var set_plan: Dictionary = _sim.get_feed_plan(site_id)
	var site_pens: Array = _sim.get_site_pens(site_id)
	var stocked := site_pens.filter(func(p: Dictionary) -> bool: return bool(p.get("occupied", false)))
	_check(not bool(set_plan.get("standard", true)) and is_equal_approx(float(set_plan.get("ration_pct", 0.0)), 70.0)
			and str(set_plan.get("recipe", "")) == "high_energy" and int(set_plan.get("days_left", -1)) == 30
			and not stocked.is_empty() and stocked.all(func(p: Dictionary) -> bool: return is_equal_approx(float(p.get("ration", 0.0)), 0.7)),
		"feed plan: Bruk sets the site's plan: 70 %%, high energy, 30 days, every stocked pen fed 0.70 (%s)" % str(set_plan))
	_check(card._status.text == tr("FEEDPLAN_STATUS_DAYS_LEFT") % 30 and bool(_sim.get_feed_plan(other_id).get("standard", false)),
		"feed plan: the card says %s and the other site keeps the standard plan" % card._status.text)
	_check(card._btn_apply.disabled and card._btn_apply.tooltip_text == tr("FEEDPLAN_WHY_IN_USE") and not card._btn_reset.disabled,
		"feed plan: the plan just applied is the one in use (Bruk waits, Tilbake til standard is offered)")

	await _click_control(card._btn_apply_all)
	var every := true
	for s in _sim.get_all_pens():
		var sp: Dictionary = _sim.get_feed_plan(str(s.get("id", "")))
		every = every and str(sp.get("recipe", "")) == "high_energy" and is_equal_approx(float(sp.get("ration_pct", 0.0)), 70.0)
		for p in _sim.get_site_pens(str(s.get("id", ""))):
			if bool(p.get("occupied", false)):
				every = every and is_equal_approx(float(p.get("ration", 0.0)), 0.7)
	_check(every and sites.size() >= 2, "feed plan: Fôr alle gives every site (%d) the plan and every stocked pen its ration" % sites.size())

	_check(_sim.save_game("ci_feed_plan"), "feed plan: setup: the game saves")
	await _click_control(card._btn_reset)
	_check(bool(_sim.get_feed_plan(site_id).get("standard", false)) and card._status.text == tr("FEEDPLAN_STATUS_STANDARD"),
		"feed plan: Tilbake til standard clears the site's plan (%s)" % card._status.text)
	var file := str(_find_save("ci_feed_plan").get("file", ""))
	_check(_sim.load_game(file), "feed plan: setup: the save loads")
	_sim.set_paused(true)
	var loaded: Dictionary = _sim.get_feed_plan(site_id)
	_check(not bool(loaded.get("standard", true)) and str(loaded.get("recipe", "")) == "high_energy"
			and is_equal_approx(float(loaded.get("ration_pct", 0.0)), 70.0) and int(loaded.get("days_left", -1)) == 30,
		"feed plan: the plan comes back with the saved game (%s)" % str(loaded))
	_sim.delete_save(file)
	scene.queue_free()
	await get_tree().process_frame

## U2-05: the harvest planner and the harvest report, driven by real clicks.
## "Høst" opens the planner and the clock waits; its histogram is the
## projection's gutted kilos per size class with each class's price, the
## columns compare ordering now with waiting 2, 4 and 8 weeks, and picking
## pens re-projects. "Bestill slakting" (the default verb) places the planned
## order, closes the sheet and sends the harvest carrier out; the order's
## batches are one harvest: exactly one toast, and its report shows the
## ledger's own entries. "Ekspress i dag" (the secondary verb) sells today at
## the planner's figure, with one toast, the report at once, and a spot
## wellboat that comes out and leaves.
func _test_harvest_planner_orders_and_reports() -> void:
	MotionPrefs.force(true)
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "harvest planner: setup: po3_red_sone starts")
	_sim.skip_tutorial()
	_sim.set_paused(true)
	var site_id := _site_at_weight(4.5)
	for i in 60:
		if site_id != "":
			break
		_sim.advance_days(7)
		site_id = _site_at_weight(4.5)
	_check(site_id != "", "harvest planner: setup: a site reaches 4.5 kg (day %d)" % int(_sim.current_day))
	var other_id := ""
	for s in _sim.get_all_pens():
		if str(s.get("id", "")) != site_id and int(s.get("fish_count", 0)) > 0:
			other_id = str(s.get("id", ""))
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	await _drain_cards(scene.get_node("%CriticalEventCard"))
	var boats: WellboatVisualManager = scene.get_node("FjordEnvironment/WellboatVisualManager")
	boats.sail_seconds = 0.2
	boats.return_delay = 0.1
	_sim.state_updated.emit()
	await get_tree().process_frame
	var ui: Control = scene.get_node("UILayer/GameUI")
	var panel := await _open_panel(ui, "pens")
	panel.select_pen(site_id)
	await get_tree().process_frame
	(panel.get_node("%DetailScroll") as ScrollContainer).ensure_control_visible(panel._btn_harvest)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(not panel._btn_harvest.disabled and panel._btn_harvest.text.ends_with(tr("UI_PEN_HARVEST")),
		"harvest planner: the site's Høst button is offered (%s)" % panel._btn_harvest.text)
	await _click_control(panel._btn_harvest)
	var planner := HarvestScreens.planner(get_tree()) as HarvestPlanner
	_check(planner != null and planner.is_open() and _sim.is_clock_held(HarvestPlanner.CLOCK_HOLD),
		"harvest planner: Høst opens the planner and the clock waits")
	if planner == null or not planner.is_open():
		scene.queue_free()
		MotionPrefs.force(null)
		return
	await get_tree().process_frame
	await get_tree().process_frame
	var planner_body: Control = planner.get_node("%Body")
	var planner_slack: float = planner._scroll.custom_minimum_size.y - planner_body.get_combined_minimum_size().y
	_check(planner_slack <= HarvestPlanner.BODY_SLACK_PX + 1.0,
		"harvest planner: the sheet is as tall as its content, no empty band (%.0f px under it)" % planner_slack)

	# What the sheet shows is the engine's projection.
	var site_pens: Array = _sim.get_site_pens(site_id).filter(func(p: Dictionary) -> bool: return bool(p.get("occupied", false)))
	_check(planner.pen_chips().size() == site_pens.size() and planner.pen_chips().all(func(b: Button) -> bool: return b.button_pressed),
		"harvest planner: one chip per pen with fish, all picked (%d)" % planner.pen_chips().size())
	_check(planner.projections.size() == 4 and planner.projections.all(func(p: Dictionary) -> bool: return bool(p.get("success", false))),
		"harvest planner: now and waiting 2, 4 and 8 weeks are projected (%d)" % planner.projections.size())
	var now: Dictionary = planner.projections[0]
	var chart := planner.size_chart
	var shown_kg := 0.0
	for v in chart.hog_kg:
		shown_kg += float(v)
	var top := 0
	for i in chart.hog_kg.size():
		if float(chart.hog_kg[i]) > float(chart.hog_kg[top]):
			top = i
	var proportional := chart.hog_kg.size() == 8
	for i in chart.hog_kg.size():
		proportional = proportional and absf(chart.bar_height(i) / chart.bar_height(top) - float(chart.hog_kg[i]) / float(chart.hog_kg[top])) < 0.02 \
			or (float(chart.hog_kg[i]) > 0.0 and chart.bar_height(i) == SizeClassChart.MIN_BAR_PX)
	_check(is_equal_approx(shown_kg, float(now.get("hog_kg", 0.0))) and proportional and chart.labels.size() == 8,
		"harvest planner: the histogram is the projection's %.0f kg gutted over 8 size classes, bars in proportion" % shown_kg)
	_check(chart.tone_for(float(chart.factors[2])) == chart.tone_for(0.5) and chart.tone_for(float(chart.factors[4])) == chart.tone_for(1.0),
		"harvest planner: a 2-3 kg class reads as a discount, the 4-5 kg reference does not")
	var vb := planner.wait_buttons
	_check(vb.size() == 4 and vb[0].button_pressed and planner.best_wait() >= 0 and vb[planner.best_wait()].text.contains(tr("HARVESTPLAN_BEST")),
		"harvest planner: four wait columns, 'Nå' shown, the best net after feed tagged (%s)" % (vb[planner.best_wait()].text.replace("\n", " ") if not vb.is_empty() else ""))
	_check(int(planner.projections[2].get("order_day", 0)) == int(_sim.current_day) + 28
			and float(planner.projections[2].get("feed_nok", 0.0)) > float(now.get("feed_nok", 0.0)),
		"harvest planner: waiting four weeks orders on day %d and eats more feed (%.0f vs %.0f NOK)" % [
			int(planner.projections[2].get("order_day", 0)), float(planner.projections[2].get("feed_nok", 0.0)), float(now.get("feed_nok", 0.0))])
	await _click_control(vb[2])
	_check(planner.shown_wait == 2 and is_equal_approx(_sum(chart.hog_kg), float(planner.projections[2].get("hog_kg", 0.0))),
		"harvest planner: a click on 'Om 4 uker' shows its fish (%.0f kg gutted)" % _sum(chart.hog_kg))
	await _click_control(vb[0])
	var express_net := float(planner.express_quote.get("net_nok", 0.0))
	_check(planner.order_button.text.contains(SimFormatters.format_day(int(now.get("slot_day", 0)))) and not planner.order_button.disabled
			and not planner.express_button.disabled and planner.express_button.text.contains(SimFormatters.format_nok(express_net)),
		"harvest planner: 'Bestill slakting' names the wellboat's day and 'Ekspress i dag' its net (%s | %s)" % [
			planner.order_button.text, planner.express_button.text])
	var chips := planner.pen_chips()
	await _click_control(chips[chips.size() - 1])
	_check(planner.projections[0].get("fish", 0) < now.get("fish", 0) and planner.pen_chips().size() == chips.size(),
		"harvest planner: leaving a pen out re-projects (%d fish, was %d)" % [int(planner.projections[0].get("fish", 0)), int(now.get("fish", 0))])
	await _click_control(planner.pen_chips()[chips.size() - 1])

	# "Bestill slakting": the planned order, the boat sails out.
	var toast_node := get_node("/root/ToastNotification")
	var director := get_node("/root/ConsequenceDirector")
	var toasts: Array[String] = []
	var announced: Array[String] = []
	var on_toast := func(message: String, _level: String) -> void: toasts.append(message)
	var on_harvest := func(_holding: String, key: String) -> void: announced.append(key)
	toast_node.toast_shown.connect(on_toast)
	director.harvest_announced.connect(on_harvest)
	toast_node.clear_all()
	var ledger0: Dictionary = _sim.get_ledger_totals()
	var projected: Dictionary = planner.projections[0]
	await _click_control(planner.order_button)
	var orders: Array = _sim.get_harvest_orders().filter(func(o: Dictionary) -> bool: return str(o.get("holding_id", "")) == site_id)
	_check(not planner.is_open() and not _sim.is_clock_held(HarvestPlanner.CLOCK_HOLD) and orders.size() == 1
			and int(orders[0].get("pens", 0)) == site_pens.size(),
		"harvest planner: 'Bestill slakting' places the planned order for every pen and gives the clock back (%d orders)" % orders.size())
	var order_id := str(orders[0].get("id", "")) if not orders.is_empty() else ""
	var job := WellboatVisualManager.ORDER_PREFIX + order_id
	_sim.state_updated.emit()
	await get_tree().create_timer(0.5).timeout
	_check(boats.job_state(job) == "at_destination" and boats.chip_text_of(job).begins_with(tr("UI_VESSEL_JOB_HARVEST")),
		"harvest planner: the harvest carrier sailed out and lies alongside (%s, '%s')" % [boats.job_state(job), boats.chip_text_of(job)])
	panel._refresh()
	_check(panel._btn_harvest.text.ends_with(tr("HARVESTPLAN_BUTTON_ORDERED")),
		"harvest planner: the site's button now reads '%s'" % panel._btn_harvest.text)

	boats.return_delay = 3.0 # the carrier lingers at the pen long enough to read its chip
	for d in 40:
		if _sim.get_harvest_orders().any(func(o: Dictionary) -> bool: return str(o.get("id", "")) == order_id):
			_sim.advance_days(1)
	await get_tree().create_timer(0.8).timeout
	await _drain_cards(scene.get_node("%CriticalEventCard"))
	var batches: Array = _sim.get_harvest_reports().filter(func(r: Dictionary) -> bool: return str(r.get("order_id", "")) == order_id)
	var harvest_toasts := _harvest_toasts(toasts, toast_node)
	_check(batches.size() >= 1 and announced == [order_id] and harvest_toasts.size() == 1,
		"harvest planner: %d slaughter day(s), exactly one harvest toast (%s) and one announcement (%s)" % [batches.size(), str(harvest_toasts), str(announced)])
	_check(boats.job_state(job) == "return" and boats.chip_text_of(job).contains(SimFormatters.format_nok(_sum_key(batches, "net_nok")))
			and boats.chip_text_of(job).contains(WellboatVisualManager.chip_text("harvest", 0, true)),
		"harvest planner: the carrier sails home with the harvest's net on its chip ('%s')" % boats.chip_text_of(job))
	var ledger1: Dictionary = _sim.get_ledger_totals()
	var revenue := float(ledger1["revenue"].get("harvest", 0.0)) - float(ledger0["revenue"].get("harvest", 0.0))
	var cost := float(ledger1["cost"].get("harvest", 0.0)) - float(ledger0["cost"].get("harvest", 0.0))
	var realized_gross := _sum_key(batches, "gross_nok")
	_check(absf(float(projected.get("gross_nok", 0.0)) / maxf(1.0, realized_gross) - 1.0) <= 0.05,
		"harvest planner: the projection was within 5 %% of the realized sale (%.0f vs %.0f NOK)" % [float(projected.get("gross_nok", 0.0)), realized_gross])
	# The toast is the way to the report: a click on its body opens this
	# harvest's report (a toast still queued behind warnings runs the same
	# action when it is clicked later; the check names which case ran).
	var harvest_msg: String = harvest_toasts[0] if harvest_toasts.size() == 1 else ""
	var toast_box: Control = null
	for i in 80: # a full stack drains one slide-out at a time
		toast_box = toast_node._visible_toast_for(harvest_msg) if harvest_msg != "" else null
		if toast_box != null or harvest_msg == "":
			break
		await get_tree().create_timer(0.1).timeout
	if toast_box != null:
		await get_tree().create_timer(0.3).timeout # past its slide-in
		await _click_at(toast_box.get_global_rect().get_center())
	else:
		for queued in toast_node._queue:
			if str(queued.message) == harvest_msg and queued.action.is_valid():
				queued.action.call()
	var from_toast := HarvestScreens.report(get_tree()) as HarvestReport
	_check(from_toast != null and from_toast.is_open() and str(from_toast.report.get("order_id", "")) == order_id
			and int(from_toast.report.get("batches", 0)) == batches.size(),
		"harvest planner: the toast opens this harvest's report (%s)" % ("clicked" if toast_box != null else "queued, its action"))
	if from_toast != null and from_toast.is_open():
		from_toast.close()

	# The report: opened from the planner's "Siste slakterapport", the ledger's figures.
	panel = await _open_panel(ui, "pens")
	panel.select_pen(site_id)
	await get_tree().process_frame
	(panel.get_node("%DetailScroll") as ScrollContainer).ensure_control_visible(panel._btn_harvest_report)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(panel._btn_harvest.disabled and panel._btn_harvest_report.visible and not panel._btn_harvest_report.disabled,
		"harvest report: the emptied site offers its last report (Høst waits for fish)")
	await _click_control(panel._btn_harvest_report)
	var report := HarvestScreens.report(get_tree()) as HarvestReport
	_check(report != null and report.is_open() and _sim.is_clock_held(HarvestReport.CLOCK_HOLD),
		"harvest report: 'Siste slakterapport' opens the report and the clock waits")
	if report != null and report.is_open():
		_check(report.shown.get("gross", "") == HarvestReport._nok(revenue) and report.shown.get("cost", "") == HarvestReport._nok(cost)
				and report.shown.get("net", "") == HarvestReport._nok(revenue - cost) and int(report.report.get("batches", 0)) == batches.size(),
			"harvest report: gross %s, cost %s, net %s are the ledger's harvest entries (%s, %s)" % [
				report.shown.get("gross", ""), report.shown.get("cost", ""), report.shown.get("net", ""), HarvestReport._nok(revenue), HarvestReport._nok(cost)])
		_check(int(report.report.get("fish", 0)) == int(_sum_key(batches, "fish")) and report._size_chart.hog_kg.size() == 8
				and is_equal_approx(_sum(report._size_chart.hog_kg), _sum_key(batches, "hog_kg")),
			"harvest report: %d fish and the histogram of the %.0f kg gutted" % [int(report.report.get("fish", 0)), _sum(report._size_chart.hog_kg)])
		await _click_control(report.close_button)
		_check(not report.is_open() and not _sim.is_clock_held(HarvestReport.CLOCK_HOLD), "harvest report: Fortsett closes it and the clock runs")
		# The payoff beat with motion on: the net tallies up from zero and the
		# bars grow in, then both land on the booked figures; reduced motion
		# shows them whole at once.
		MotionPrefs.force(false)
		report.show_harvest(order_id)
		await get_tree().process_frame
		var net_text := "+" + HarvestReport._nok(revenue - cost)
		var rising: bool = report._size_chart.grow < 1.0 and report._net_value.text != net_text
		await get_tree().create_timer(HarvestReport.COUNT_UP_S + HarvestReport.NET_SETTLE_S + 0.3).timeout
		_check(rising and is_equal_approx(report._size_chart.grow, 1.0) and report._net_value.text == net_text
				and report._net_value.scale.is_equal_approx(Vector2.ONE),
			"harvest report: with motion the net tallies up and the bars grow in, landing on %s" % report._net_value.text)
		report.close()
		MotionPrefs.force(true)
		report.show_harvest(order_id)
		_check(is_equal_approx(report._size_chart.grow, 1.0) and report._net_value.text == net_text,
			"harvest report: reduced motion shows the net and the bars whole at once")
		report.close()

	# "Ekspress i dag" on the other site: sold today at the planner's figure.
	if other_id != "":
		panel.select_pen(other_id)
		await get_tree().process_frame
		(panel.get_node("%DetailScroll") as ScrollContainer).ensure_control_visible(panel._btn_harvest)
		await get_tree().process_frame
		await _click_control(panel._btn_harvest)
		toasts.clear()
		announced.clear()
		toast_node.clear_all()
		boats.return_delay = 0.1
		var returned: Array[String] = []
		var on_return := func(site: String) -> void: returned.append(site)
		boats.wellboat_returned.connect(on_return)
		ledger0 = _sim.get_ledger_totals()
		var quoted := float(planner.express_quote.get("net_nok", 0.0))
		await _click_control(planner.express_button)
		await get_tree().create_timer(0.6).timeout
		var express: Array = _sim.get_harvest_reports().filter(func(r: Dictionary) -> bool:
			return str(r.get("holding_id", "")) == other_id and str(r.get("mode", "")) == "Express")
		var express_report: Dictionary = express[-1] if not express.is_empty() else {}
		ledger1 = _sim.get_ledger_totals()
		revenue = float(ledger1["revenue"].get("harvest", 0.0)) - float(ledger0["revenue"].get("harvest", 0.0))
		cost = float(ledger1["cost"].get("harvest", 0.0)) - float(ledger0["cost"].get("harvest", 0.0))
		_check(not planner.is_open() and report.is_open() and str(report.report.get("mode", "")) == "Express"
				and report.shown.get("net", "") == HarvestReport._nok(revenue - cost),
			"harvest express: the report opens at once with the ledger's net (%s)" % report.shown.get("net", ""))
		_check(absf(float(express_report.get("net_nok", 0.0)) - quoted) < 1.0,
			"harvest express: the planner's same-day figure is what was booked (%.0f vs %.0f NOK)" % [quoted, float(express_report.get("net_nok", 0.0))])
		var express_toasts := _harvest_toasts(toasts, toast_node)
		_check(express_toasts.size() == 1 and announced.size() == 1,
			"harvest express: exactly one toast (%s) and one announcement" % str(express_toasts))
		var spot := WellboatVisualManager.EXPRESS_PREFIX + str(express_report.get("id", ""))
		await get_tree().create_timer(1.0).timeout
		_check(returned.has(other_id) and not boats.active_job_ids().has(spot),
			"harvest express: the spot wellboat came out to %s, loaded and left (%s)" % [other_id, str(returned)])
		boats.wellboat_returned.disconnect(on_return)
		await _click_control(report.close_button)
	toast_node.toast_shown.disconnect(on_toast)
	director.harvest_announced.disconnect(on_harvest)
	MotionPrefs.force(null)
	scene.queue_free()
	await get_tree().process_frame


## U2-05: the legacy express verb (SimBridge.harvest_pen) is heard twice, as
## the action's pen_action_completed and the bridge's FarmActionCompleted; the
## harvest is still announced once: one toast, one stinger, one report key.
func _test_harvest_pen_announces_once() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "harvest once: setup: po3_red_sone starts")
	_sim.skip_tutorial()
	_sim.set_paused(true)
	var site_id := ""
	for s in _sim.get_all_pens():
		if site_id == "" and int(s.get("fish_count", 0)) > 0:
			site_id = str(s.get("id", ""))
	var toast_node := get_node("/root/ToastNotification")
	var director := get_node("/root/ConsequenceDirector")
	var toasts: Array[String] = []
	var announced: Array[String] = []
	var on_toast := func(message: String, _level: String) -> void: toasts.append(message)
	var on_harvest := func(_holding: String, key: String) -> void: announced.append(key)
	toast_node.clear_all()
	toast_node.toast_shown.connect(on_toast)
	director.harvest_announced.connect(on_harvest)
	var kg: float = _sim.harvest_pen(site_id)
	for i in 3:
		await get_tree().process_frame
	var reports: Array = _sim.get_harvest_reports().filter(func(r: Dictionary) -> bool: return str(r.get("holding_id", "")) == site_id)
	var key := str(reports[-1].get("id", "")) if not reports.is_empty() else "?"
	var harvest_toasts := _harvest_toasts(toasts, toast_node)
	_check(kg > 0.0 and announced == [key] and harvest_toasts.size() == 1,
		"harvest once: harvest_pen on %s is announced once (%s) with one toast (%s)" % [site_id, str(announced), str(harvest_toasts)])
	toast_node.toast_shown.disconnect(on_toast)
	director.harvest_announced.disconnect(on_harvest)
	toast_node.clear_all()


## The harvest toasts shown, plus any still waiting in the toast queue (a
## stack full of warnings shows them first): each is one harvest's payoff.
## Both harvest texts count (the payoff with its figures, and the plain
## "Høsting fullført" a harvest without a report gets), with or without an
## icon in front, so a second toast from any path is caught.
func _harvest_toasts(shown: Array[String], toast_node: Node) -> Array[String]:
	var prefixes: Array[String] = [tr("UI_TOAST_HARVEST_DONE").get_slice("%s", 0), tr("UI_TOAST_HARVEST_OK").get_slice("%s", 0)]
	var is_harvest := func(m: String) -> bool:
		return prefixes.any(func(p: String) -> bool: return m.contains(p))
	var out: Array[String] = []
	for m in shown:
		if is_harvest.call(m):
			out.append(m)
	for queued in toast_node._queue:
		var m := str(queued.message)
		if is_harvest.call(m) and not out.has(m):
			out.append(m)
	return out


## The id of a site whose fish have reached `kg` mean live weight ("" when none).
func _site_at_weight(kg: float) -> String:
	for s in _sim.get_all_pens():
		var fish := int(s.get("fish_count", 0))
		if fish > 0 and float(s.get("biomass_kg", 0.0)) / fish >= kg:
			return str(s.get("id", ""))
	return ""


func _sum(values: Array) -> float:
	var total := 0.0
	for v in values:
		total += float(v)
	return total


func _sum_key(rows: Array, key: String) -> float:
	var total := 0.0
	for r in rows:
		total += float(r.get(key, 0.0))
	return total

## Wave 3 ui-a: no audio file is read from disk inside a sim tick (at 30×
## a season's music or a weather layer read there cost 9-11 ms, a cue up to
## 9 ms). A track is read on a worker thread and starts when it arrives; a
## cue is read once and kept; a missing file is refused, never waited for.
func _test_audio_reads_tracks_off_the_tick() -> void:
	var cache := AudioStreamCache.new()
	var path := MusicLibrary.music("autumn_theme")
	var got: Array = []
	var queued := cache.later(path, func(stream: AudioStream) -> void: got.append(stream))
	_check(queued and got.is_empty() and cache.is_loading(path),
		"audio: a track is asked for on the caller's frame and read on a worker thread")
	var frames := 0
	while cache.is_loading(path) and frames < 600:
		await get_tree().process_frame
		cache.poll()
		frames += 1
	_check(got.size() == 1 and got[0] is AudioStream and cache.now(path) == got[0],
		"audio: the worker's stream reaches the caller once and stays cached (%d frames)" % frames)
	var cue := MusicLibrary.ui_sfx("notification")
	var first := cache.now(cue)
	_check(first != null and cache.now(cue) == first, "audio: a cue is read once and kept")
	_check(not cache.later("res://assets/audio/music/ci_missing.ogg", func(_s: AudioStream) -> void: pass)
			and not cache.is_loading() and not cache.exists(""),
		"audio: a missing file is refused, never waited for")
	# The controller names the new track at once and plays it when it is read.
	var audio := get_node_or_null("/root/AudioController")
	var track := MusicLibrary.music("spring_theme")
	if audio.get_current_music() == track:
		track = MusicLibrary.music("winter_theme")
	audio._update_bgm_direct(track)
	var named: bool = audio.get_current_music() == track
	frames = 0
	var playing_path := ""
	while frames < 600:
		await get_tree().process_frame
		frames += 1
		for player: AudioStreamPlayer in [audio._bgm_active, audio._bgm_pending]:
			if player.stream != null and player.stream.resource_path == track:
				playing_path = track
		if playing_path != "":
			break
	_check(named and playing_path == track,
		"audio: a new season's music is named at once and on a player %d frames later" % frames)

## The HUD fits its bar at 1280 px and UI scale 125 %: readouts give way in
## order (weather, spot price, sea temperature) rather than leave the screen,
## and all of them come back at 1920 px. (The overlap lint checks the regions.)
func _test_hud_fits_its_bar() -> void:
	var scene := await _spawn_po3_scene(91)
	var hud: Control = scene.get_node("UILayer/HUD")
	var win := get_window()
	var old_size := win.size
	var old_scale := win.content_scale_factor
	win.size = Vector2i(1280, 720)
	win.content_scale_factor = 1.25
	_sim.set_time_speed(3.0)
	await get_tree().create_timer(0.3).timeout
	var bar: Control = hud.get_node("MainHBox")
	var right: Control = hud.get_node("MainHBox/Right")
	var screen := get_viewport().get_visible_rect()
	_check(screen.encloses(right.get_global_rect()) and bar.get_combined_minimum_size().x <= hud.bar_width() + 0.5,
		"HUD: fits 1280 px at 125 %% (needs %d of %d px, hidden %s)" % [
			int(bar.get_combined_minimum_size().x), int(hud.bar_width()), str(hud.hidden_readouts())])
	win.size = Vector2i(1920, 1080)
	win.content_scale_factor = 1.0
	await get_tree().create_timer(0.3).timeout
	_check(hud.hidden_readouts().is_empty(), "HUD: every readout shows at 1920 px (hidden %s)" % str(hud.hidden_readouts()))
	var small: Array = []
	for btn: Button in [hud.pause_btn, hud.speed_1x, hud.speed_3x, hud.speed_10x, hud.speed_30x, hud.next_event_btn, hud.inbox_btn]:
		if btn.size.x < 44.0 or btn.size.y < 40.0:
			small.append("%s %s" % [btn.name, str(btn.size)])
	_check(small.is_empty(), "HUD: the clock's buttons and the bell are at least 44 x 40 px (%s)" % str(small))
	win.size = old_size
	win.content_scale_factor = old_scale
	_sim.set_paused(true)
	scene.queue_free()
	await get_tree().process_frame

## S2-03: the scenario farm's fish went to sea in the last release window (an
## autumn S0 generation in Vestland), grow on the TGC law, and their size spread
## and the pen's ration reach the game through the bridge.
func _test_pens_report_their_size_spread_and_ration() -> void:
	_check(_sim.start_qa_scenario("forste_sesong", 17), "growth: tutorial scenario starts")
	_sim.set_paused(true)
	var start: Array = _sim.get_all_pens()
	var w0 := float(start[0].get("mean_weight_kg", 0.0)) if start.size() > 0 else 0.0
	_check(start.size() == 1 and w0 > 0.3 and w0 < 0.7,
		"growth: the inherited fish are post-smolts on day 0 (%.2f kg)" % w0)
	_sim.advance_days(120)
	var pens: Array = _sim.get_all_pens()
	var cv := float(pens[0].get("weight_cv", 0.0)) if pens.size() > 0 else 0.0
	var ration := float(pens[0].get("ration", 0.0)) if pens.size() > 0 else 0.0
	_check(cv > 0.15 and cv < 0.35 and is_equal_approx(ration, 1.0),
		"growth: the site reports its size spread (CV %.3f) and full-appetite ration (%.2f)" % [cv, ration])

## S2-04: the harvest pipeline reaches the game through the bridge: a quote in
## gutted kilos, size classes and grades; an order that fasts the fish, waits for
## its wellboat slot and is slaughtered at the price of the day; one report per
## batch that reconciles with the cash; a partial harvest; the express verb.
func _test_harvest_pipeline_reaches_the_ui() -> void:
	_check(_sim.start_qa_scenario("forste_sesong", 29), "harvest: tutorial scenario starts")
	_sim.set_paused(true)
	_sim.advance_days(200)
	var site_id := str(_sim.get_all_pens()[0].get("id", ""))
	var q: Dictionary = _sim.quote_harvest(site_id)
	var live := float(q.get("live_kg", 0.0))
	var classes: Array = q.get("size_classes", [])
	_check(bool(q.get("success", false)) and live > 0.0 and is_equal_approx(float(q.get("hog_kg", 0.0)), live * 0.84),
		"harvest: the quote sells gutted kilos, 84%% of %.0f kg live (%s)" % [live, str(q.get("hog_kg"))])
	var shares := float(q.get("superior", 0.0)) + float(q.get("ordinary", 0.0)) + float(q.get("production", 0.0))
	_check(classes.size() == 8 and is_equal_approx(shares, 1.0) and float(q.get("superior", 0.0)) > 0.5,
		"harvest: the quote splits the fish into 8 size classes and three grades (%.3f)" % shares)
	_check(int(q.get("first_day", 0)) >= _sim.current_day + 5 and float(q.get("surcharge_nok", -1.0)) == 0.0,
		"harvest: a planned harvest waits for the wellboat slot and pays no surcharge (day %d)" % int(q.get("first_day", 0)))
	var all_pens: Array[String] = []
	var express: Dictionary = _sim.quote_harvest(site_id, all_pens, 1.0, true)
	_check(float(express.get("surcharge_nok", 0.0)) > 0.0 and int(express.get("first_day", -1)) == _sim.current_day,
		"harvest: an express harvest is today, with the surcharge")

	var order: Dictionary = _sim.order_harvest(site_id)
	var order_id := str(order.get("id", ""))
	_check(bool(order.get("success", false)) and order_id != "" and _sim.get_harvest_orders().size() == 1,
		"harvest: the order is placed (%s)" % order_id)
	_check(str(_sim.order_harvest(site_id).get("error", "")) == "pen_in_order",
		"harvest: the same pens cannot be ordered twice")
	_check(bool(_sim.cancel_harvest_order(order_id).get("success", false)) and _sim.get_harvest_orders().is_empty(),
		"harvest: the order can be called off")

	var cash0 := float(_sim.balance)
	order = _sim.order_harvest(site_id, all_pens, 0.5)
	order_id = str(order.get("id", ""))
	_sim.advance_days(12)
	var reports: Array = _sim.get_harvest_reports().filter(func(r: Dictionary) -> bool: return str(r.get("order_id", "")) == order_id)
	var fish := 0
	var net := 0.0
	var reconciled := true
	for r in reports:
		fish += int(r.get("fish", 0))
		net += float(r.get("net_nok", 0.0))
		reconciled = reconciled and is_equal_approx(float(r.get("net_nok", 0.0)), float(r.get("gross_nok", 0.0)) - float(r.get("cost_nok", 0.0)))
	_check(_sim.get_harvest_orders().is_empty() and not reports.is_empty() and reconciled,
		"harvest: the half harvest was slaughtered and every report reconciles (%d reports)" % reports.size())
	var left := int(_sim.get_all_pens()[0].get("fish_count", 0))
	_check(fish > 0 and left > 0 and absi(fish - left) <= maxi(1, (fish + left) / 50),
		"harvest: half the fish went (%d), half stayed (%d)" % [fish, left])
	_check(net > 0.0 and float(_sim.balance) > cash0,
		"harvest: the sale reached the cash (+%.0f net)" % net)

	var kg: float = _sim.harvest_pen(site_id)
	_check(kg > 0.0 and int(_sim.get_all_pens()[0].get("fish_count", 0)) == 0,
		"harvest: the express harvest verb still empties the site (%.0f kg)" % kg)

## S2-01a: capacity v1 reaches the facade for U2-01's expansion flow — the
## licence MTB pooled in PO3, each site's locality MTB, the plan's peak; a build
## adds a 3,120 t locality and 0 t of MTB; stocking past the plan's room is
## refused with a reason that has Norwegian text, and the room itself is offered.
func _test_capacity_reaches_the_bridge() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "capacity: seeded scenario starts")
	_sim.set_paused(true)
	var o: Dictionary = _sim.get_capacity_overview()
	var areas: Array = o.get("areas", [])
	var sites: Array = o.get("sites", [])
	_check(bool(o.get("success", false)) and str(o.get("regime", "")) == "licences_v1",
		"capacity: a scenario runs under the licence register (%s)" % str(o.get("regime")))
	_check(areas.size() == 1 and str(areas[0].get("code", "")) == "PO3" and int(areas[0].get("licences", 0)) == 2
			and is_equal_approx(float(areas[0].get("licence_mtb_t", 0.0)), 1560.0),
		"capacity: two 780 t licences pool in PO3 (%s)" % str(areas))
	var peak := float(areas[0].get("projected_peak_t", 0.0)) if areas.size() == 1 else 0.0
	var standing := float(areas[0].get("standing_t", 0.0)) if areas.size() == 1 else 0.0
	_check(peak > standing and peak < 1560.0, "capacity: the plan's peak lies above today's biomass and under the cap (%.1f t)" % peak)
	var localities: Array = sites.map(func(r: Dictionary) -> float: return float(r.get("locality_mtb_t", 0.0)))
	_check(localities == [780.0, 780.0], "capacity: each site keeps a 780 t locality (%s)" % str(localities))
	var build: Dictionary = _sim.preview_build_sea_site("po3_nord")
	_check(bool(build.get("allowed", false)) and is_zero_approx(float(build.get("licence_mtb_added_t", -1.0)))
			and is_equal_approx(float(build.get("locality_mtb_t", 0.0)), 3120.0),
		"capacity: a build adds a 3 120 t locality and 0 t of MTB (%s)" % str(build))
	var room: Dictionary = _sim.preview_stocking("po3_nord", 0)
	var max_count := int(room.get("max_count", 0))
	var over: Dictionary = _sim.preview_stocking("po3_nord", max_count + 1)
	var label := str(over.get("label_key", ""))
	_check(max_count > 0 and bool(room.get("allowed", false)) and not bool(over.get("allowed", true))
			and label.begins_with("CAPACITY_REASON_") and tr(label) != label,
		"capacity: the plan's room (%d smolts) is offered and one more is refused with Norwegian text (%s)" % [max_count, label])
	_check(not _sim.stock_smolts("po3_nord", max_count + 1), "capacity: stocking past the room is refused through the action")
	_check(_sim.stock_smolts("po3_nord", max_count), "capacity: stocking the room is accepted through the action")

## S2-01b: a site's real pens reach the game. The site payload carries its pen
## count and each pen (the fjord lays out pen_count pens); the bridge refuses
## 200 001 fish in one pen with Norwegian text; the Pens tab shows the pens,
## offers the empty ones, names why a count does not fit (and disables the
## button), and stocks the chosen count in the chosen pen.
func _test_pens_reach_the_stocking_card() -> void:
	_check(_sim.start_qa_scenario("forste_sesong", 37), "pens: tutorial scenario starts")
	_sim.set_paused(true)
	var sites: Array = _sim.get_all_pens()
	var site: Dictionary = sites[0] if sites.size() > 0 else {}
	var site_id := str(site.get("id", ""))
	var pens: Array = site.get("pens", [])
	_check(int(site.get("pen_count", 0)) == 6 and pens.size() == 6,
		"pens: the family farm reports its six pens (%s, %d)" % [str(site.get("pen_count")), pens.size()])
	var fish: Array = pens.map(func(p: Dictionary) -> int: return int(p.get("fish_count", -1)))
	var numbers: Array = pens.map(func(p: Dictionary) -> int: return int(p.get("number", 0)))
	_check(numbers == [1, 2, 3, 4, 5, 6] and fish == [36000, 0, 0, 0, 0, 0],
		"pens: pen 1 holds the inherited 36 000 fish and pens 2-6 lie empty (%s)" % str(fish))
	var volume := float(pens[1].get("volume_m3", 0.0)) if pens.size() > 1 else 0.0
	_check(absf(volume - 26738.0) < 1.0 and str(pens[1].get("id", "")) == site_id + "/p02",
		"pens: pen 2 is a 120 m family-farm pen of %.0f m3 (%s)" % [volume, str(pens[1].get("id"))])
	var refused: Dictionary = _sim.stock_smolts_in_pen(site_id, "2", 200001)
	var label := str(refused.get("label_key", ""))
	_check(not bool(refused.get("success", true)) and str(refused.get("reason_key", "")) == "pen_fish_limit"
			and tr(label) != label,
		"pens: 200 001 fish in one pen are refused with Norwegian text (%s)" % str(refused.get("reason_key")))

	var panel: Control = (load("res://scenes/ui/PenManagement.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	panel.select_pen(site_id)
	await get_tree().process_frame
	var chips: Node = panel.get_node("%PenChips")
	var picker: OptionButton = panel.get_node("%PenOption")
	var spin: SpinBox = panel.get_node("%CountSpin")
	var stock: Button = panel.get_node("%BtnStock")
	var preview: Label = panel.get_node("%StockPreview")
	_check(chips.get_child_count() == 6 and picker.item_count == 5 and str(picker.get_item_metadata(picker.selected)) == site_id + "/p02",
		"pens: the card shows six pens and offers the five empty ones, pen 2 first (%d, %d)" % [chips.get_child_count(), picker.item_count])
	var room := int(_sim.preview_pen_stocking(site_id, "2", 0).get("max_count", 0))
	var why := str(_sim.preview_pen_stocking(site_id, "2", 190000).get("label_key", ""))
	spin.value = 190000
	await get_tree().process_frame
	_check(room > 50000 and room < 190000 and stock.disabled and not why.is_empty() and tr(why) != why
			and preview.text.ends_with(" · " + tr(why)),
		"pens: %d smolts do not fit (room %d): the card says why and the button waits (%s)" % [int(spin.value), room, preview.text])
	spin.value = 10000
	await get_tree().process_frame
	_check(not stock.disabled, "pens: 10 000 smolts fit in pen 2 (%s)" % preview.text)
	await _click_control(stock)
	await get_tree().process_frame
	var after: Array = _sim.get_site_pens(site_id)
	var pen2 := int(after[1].get("fish_count", 0)) if after.size() > 1 else 0
	_check(pen2 == 10000 and bool(after[1].get("occupied", false)),
		"pens: the stock button put 10 000 smolts in pen 2 (%d)" % pen2)
	_check(picker.item_count == 4, "pens: pen 2 left the empty-pen picker (%d)" % picker.item_count)
	panel.queue_free()
	await get_tree().process_frame

## D20 / G1-04: the 3D sky's latitude is the active scenario's region, read
## from the sim through the vista bridge: a Trøndelag scenario (laksebaronene)
## puts Trondheim's 63.43 on the TimeOfDay, not the Vestland fallback.
func _test_sky_latitude_follows_a_northern_scenario() -> void:
	_check(_sim.start_qa_scenario("laksebaronene", 91), "sky latitude: the Trøndelag scenario starts")
	_sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var ts: TimeOfDay = env.get_node("TimeOfDay")
	var vista: FjordVistaBridge = scene.get_node("FjordVistaBridge")
	var ctx: Dictionary = _sim.get_sky_context()
	vista._on_state_updated()
	await get_tree().process_frame
	_check(str(ctx.get("region", "")) == "Trøndelag" and absf(float(ctx.get("latitude", 0.0)) - 63.43) < 0.01,
		"sky latitude: the sim's sky context is the scenario's region (%s %.2f)" % [str(ctx.get("region", "")), float(ctx.get("latitude", 0.0))])
	_check(absf(ts.latitude_deg - 63.43) < 0.01, "environment: the sky's latitude is the scenario region's (Trøndelag 63.43, got %.2f)" % ts.latitude_deg)
	scene.queue_free()
	await get_tree().process_frame

# ── U2-01: the expansion flow ──────────────────────────────────────────────────

## A running GameScene over the current game, paused, with no card up.
func _spawn_scene_over_current_game() -> Node:
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	await _drain_cards(scene.get_node("%CriticalEventCard"))
	return scene

## Cmd/Ctrl+2 (the map tab's shortcut), then the map panel once it is laid out.
func _open_map_by_key(ui: Control) -> Control:
	get_viewport().gui_release_focus()
	await _press_command_key(KEY_2)
	for i in 4:
		await get_tree().process_frame
	return ui.find_child("Panel_map", true, false)

## The clock runs one sim day by real input: Space resumes, Space pauses.
func _run_one_day_by_key() -> void:
	get_viewport().gui_release_focus()
	var day: int = _sim.current_day
	await _press_key(KEY_SPACE, 32)
	await _wait_until(func() -> bool: return int(_sim.current_day) > day, 8.0)
	if not _sim.is_paused():
		await _press_key(KEY_SPACE, 32)

## The minimal player of ScenarioCampaignTests (delouse above 0.4 adult females,
## harvest at 4.5 kg, start the cheapest research once a harvest paid) plays the
## first season through the bridge's verbs until the tutorial waits at step 16
## with 6.5 MNOK in the bank. Returns the day it got there (-1 when it did not).
func _play_first_season_to_step_16(gb: Node) -> int:
	var harvested := false
	var research := false
	var last_delouse := -100
	for i in 1500:
		if int(gb.TutorialStepNumber) == 16 and float(_sim.balance) > 6_500_000.0:
			return int(_sim.current_day)
		_sim.advance_days(1)
		var site: Dictionary = _sim.get_all_pens()[0]
		var site_id := str(site.get("id", ""))
		if int(site.get("fish_count", 0)) > 0:
			if float(site.get("lice_per_fish", 0.0)) > 0.4 and int(_sim.current_day) - last_delouse >= 10:
				last_delouse = int(_sim.current_day)
				_sim.delouse_pen(site_id, "hydrolicer")
			if float(site.get("mean_weight_kg", 0.0)) >= 4.5:
				harvested = _sim.harvest_pen(site_id) > 0.0 or harvested
		if harvested and not research:
			var cheapest: Dictionary = {}
			for node in _sim.get_research_nodes():
				if bool(node.get("can_research", false)) and (cheapest.is_empty() or float(node.get("cost_nok", 0.0)) < float(cheapest.get("cost_nok", 0.0))):
					cheapest = node
			if not cheapest.is_empty() and float(_sim.balance) > float(cheapest.get("cost_nok", 0.0)) + 200_000.0:
				research = _sim.start_research(str(cheapest.get("id", "")))
	return -1

## U2-01 acceptance 1: a new Første sesong reaches tutorial step 16 ("Utvid med
## ny lokalitet"); the expansion is then done by real input only: Cmd+2 opens
## the map, a click on the PO 3 marker shows the area, the home zone's card says
## the build is allowed, its "Bygg lokalitet" opens the BuildSiteDialog whose
## preview equals the engine's plan (cost, cash after, running cost, the
## locality's 3 120 t, +0 t licence MTB, 8 pens drawn), the confirm builds the
## second site, and one sim day by the Space key completes step 16.
## The season before step 16 is played by the bridge's verbs (setup; U2-02 owns
## the season played by UI input alone).
func _test_expansion_builds_the_tutorials_second_site() -> void:
	GameplayPrefs.force_auto_pause(true)
	var gb := get_node("/root/GameBridge")
	_check(_sim.start_qa_scenario("forste_sesong", 4), "expansion: Første sesong starts (seed 4)")
	_sim.set_paused(true)
	var day := _play_first_season_to_step_16(gb)
	_check(day > 0 and int(gb.TutorialStepNumber) == 16 and int(gb.TutorialCompletedCount) == 15,
		"expansion: the first season waits at tutorial step 16 with the build's cash (day %d, step %d, %s)" % [
			day, int(gb.TutorialStepNumber), SimFormatters.format_nok(float(_sim.balance))])
	var scene := await _spawn_scene_over_current_game()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var map := await _open_map_by_key(ui)
	_check(map != null and map.is_visible_in_tree() and ui.get_active_tab() == "map", "expansion: Cmd+2 opens the map")
	if map == null:
		scene.queue_free()
		return
	await _click_control(map._region_nodes["po03"])
	var section: RegionExpansionSection = map.expansion_section()
	_check(int(section.area().get("production_area", 0)) == 3, "expansion: a click on the PO 3 marker shows production area 3")
	var home := section.zone_card("fjord_west")
	_check(home != null and home.is_visible_in_tree(), "expansion: the home zone has a card")
	if home == null:
		scene.queue_free()
		return
	var verdict: Label = home.get_node("Body/Build/Reason")
	var cost := float(_sim.plan_site_build("fjord_west").get("cost_nok", 0.0))
	_check(verdict.text == tr("EXPANSION_BUILD_OK") % SimFormatters.format_nok(cost),
		"expansion: the home zone says the build is allowed ('%s')" % verdict.text)
	await _click_control(home.get_node("Body/Build/BuildBtn"))
	var dialog: BuildSiteDialog = map.build_dialog()
	_check(dialog.visible and dialog.zone_id() == "fjord_west", "expansion: 'Bygg lokalitet' opens the build dialog for the zone")
	var plan: Dictionary = _sim.plan_site_build("fjord_west")
	var ledger: Node = dialog.get_node("%Ledger")
	_check((ledger.get_node("ValueCost") as Label).text == SimFormatters.format_nok(float(plan["cost_nok"]))
			and (ledger.get_node("ValueCash") as Label).text.ends_with(SimFormatters.format_nok(float(plan["balance_nok"]) - float(plan["cost_nok"]))),
		"expansion: the dialog shows the cost and the cash after it (%s)" % (ledger.get_node("ValueCash") as Label).text)
	_check(int(plan["pens"]) == 8 and float(plan["licence_mtb_added_t"]) == 0.0 and float(plan["locality_mtb_t"]) == 3120.0
			and (ledger.get_node("ValueLicence") as Label).text.ends_with("(+0 t)")
			and (ledger.get_node("ValuePens") as Label).text.begins_with("8 × "),
		"expansion: 8 pens, a 3 120 t locality and +0 t licence MTB (%s)" % (ledger.get_node("ValueLicence") as Label).text)
	_check(float(plan["running_cost_per_month_nok"]) > 0.0
			and (ledger.get_node("ValueRunning") as Label).text.contains(SimFormatters.format_nok(float(plan["running_cost_per_month_nok"]))),
		"expansion: the running cost per month is previewed (%s)" % (ledger.get_node("ValueRunning") as Label).text)
	var families: Node = dialog.get_node("%FamilyRow")
	var land: Node = families.get_node_or_null("Family_land")
	var offshore: Node = families.get_node_or_null("Family_offshore")
	_check(families.get_child_count() == 3 and families.get_child(0).name == "Family_sea"
			and land != null and land.find_child("OpenTab", true, false) != null
			and offshore != null and offshore.find_child("OpenTab", true, false) != null,
		"expansion: the farm type comes first: Sjø chosen, Land and Hav point to their tabs")
	# Land and Hav say where those builds live today and invent no figures.
	var digits := RegEx.create_from_string("[0-9]")
	var figures := []
	for card in [land, offshore]:
		if card == null:
			continue
		for l in card.find_children("*", "Label", true, false):
			if digits.search((l as Label).text) != null:
				figures.append((l as Label).text)
	_check(figures.is_empty(), "expansion: the Land and Hav cards show no numbers %s" % str(figures))
	await _lint_screen("build dialog")
	var confirm: Button = dialog.get_node("%ConfirmBtn")
	_check(not confirm.disabled, "expansion: the confirm is enabled")
	await _click_control(confirm)
	_check(_sim.get_all_pens().size() == 2, "expansion: the confirm built the second site (%d sites)" % _sim.get_all_pens().size())
	_check(dialog.get_node("%ResultBox").visible and confirm.text == tr("EXPANSION_BUILD_VIEW_BUTTON"),
		"expansion: the dialog says the site is built and offers to show it")
	await _click_control(dialog.get_node("%CancelBtn"))
	_check(not dialog.visible and section.zone_card("fjord_west") != null,
		"expansion: closing the dialog returns to the map")
	await _run_one_day_by_key()
	await _drain_cards(scene.get_node("%CriticalEventCard"))
	_check(int(gb.TutorialCompletedCount) >= 16 and int(gb.TutorialStepNumber) >= 17,
		"expansion: tutorial step 16 completes (step %d, %d done)" % [int(gb.TutorialStepNumber), int(gb.TutorialCompletedCount)])
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## U2-01 acceptance 2: in a green zone the player opens a licence auction from
## the map, sees a rival's live bid, outbids it by real input and wins at the
## close; the inbox lists the running auction and its action opens the card;
## the licence's 780 t pool in PO 4 and the won zone can then be built.
func _test_auction_card_wins_a_green_zone_licence() -> void:
	GameplayPrefs.force_auto_pause(true)
	_check(_sim.start_qa_new_game("Player", 600_000_000, 41), "auction: a seeded game with 600 MNOK starts")
	var scene := await _spawn_scene_over_current_game()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var map := await _open_map_by_key(ui)
	if map == null:
		_check(false, "auction: the map opens")
		scene.queue_free()
		return
	await _click_control(map._region_nodes["po04"])
	var section: RegionExpansionSection = map.expansion_section()
	var zone := section.zone_card("po04_solund")
	_check(zone != null and str(section.area().get("traffic_light", "")) == "green"
			and (section.get_node("AreaChips/TrafficLight") as Label).text == tr("EXPANSION_LIGHT_GREEN"),
		"auction: PO 4 is green on the map and its growth zone Solund has a card")
	if zone == null:
		scene.queue_free()
		return
	await _click_control(zone.get_node("Body/Licence/LicenceBtn"))
	var card: LicenceAuctionCard = map.auction_card()
	_check(card.visible and card.mode() == "preview" and card.zone_id() == "po04_solund", "auction: 'Søk vekstlisens' opens the card's preview")
	var preview: Dictionary = _sim.preview_licence_application("po04_solund")
	_check((card.get_node("%ReserveAmount") as Label).text == SimFormatters.format_nok(float(preview["reserve_nok"]))
			and (card.get_node("%ReservePerTonne") as Label).text == tr("EXPANSION_PER_TONNE") % SimFormatters.num(float(preview["reserve_per_tonne_nok"])),
		"auction: the preview shows the reserve and its price per tonne (%s)" % (card.get_node("%ReservePerTonne") as Label).text)
	await _lint_screen("auction preview")
	await _click_control(card.get_node("%PrimaryBtn"))
	var a: Dictionary = _sim.get_licence_auction("po04_solund")
	_check(card.mode() == "live" and str(a.get("status", "")) == "open", "auction: 'Åpne auksjonen' opens the auction")
	# The opening bid at the reserve (the bid field starts at the minimum bid).
	await _click_control(card.get_node("%PrimaryBtn"))
	a = _sim.get_licence_auction("po04_solund")
	_check(str(a.get("leader_kind", "")) == "player", "auction: the reserve bid leads (%s)" % str(a.get("leader_kind", "")))
	# The clock runs at 30× (key 4) until a rival outbids the company.
	get_viewport().gui_release_focus()
	await _press_key(KEY_4, 52)
	var outbid: bool = await _wait_until(func() -> bool:
		return str(_sim.get_licence_auction("po04_solund").get("leader_kind", "")) == "rival", 20.0)
	if not _sim.is_paused():
		await _press_key(KEY_SPACE, 32)
	await get_tree().process_frame
	a = _sim.get_licence_auction("po04_solund")
	_check(outbid and int(a.get("rival_bids", 0)) >= 1, "auction: a rival bid live (%d rival bids)" % int(a.get("rival_bids", 0)))
	_check((card.get_node("%LeaderLabel") as Label).text == tr("EXPANSION_AUCTION_RIVAL_LEADS") % str(a.get("leader_name", "")),
		"auction: the card names the leading rival ('%s')" % (card.get_node("%LeaderLabel") as Label).text)
	var rows: Node = card.get_node("%HistoryList")
	_check(rows.get_child_count() == (a.get("bids", []) as Array).size()
			and (rows.get_child(0).get_node("Bidder") as Label).text == str(a.get("leader_name", "")),
		"auction: the bid list shows every bid, the rival's newest first")
	await _lint_screen("auction live")
	# The inbox lists the auction; its action opens this card.
	card.close()
	var inbox: CanvasLayer = scene.find_child("AttentionInbox", true, false) as CanvasLayer
	if inbox != null:
		inbox.open_inbox()
		await get_tree().process_frame
		var list: Node = inbox.get_node("%ListContainer")
		var titles := list.find_children("*", "Label", true, false).map(func(l: Node) -> String: return (l as Label).text)
		_check(titles.has(tr("UI_ATTENTION_AUCTION_OUTBID").format(["Solund"])),
			"auction: the inbox lists the outbid auction")
		var action: Button = null
		for b in list.find_children("*", "Button", true, false):
			action = b as Button
		if action != null:
			action.pressed.emit()
			await get_tree().process_frame
		_check(action != null and card.visible and card.zone_id() == "po04_solund" and ui.get_active_tab() == "map",
			"auction: the inbox's action opens the auction card on the map")
	else:
		_check(false, "auction: the game scene has the attention inbox")
	# Outbid the rival field by typing 300 MNOK, above every rival's ceiling.
	var spin: SpinBox = card.get_node("%BidSpin")
	var line := spin.get_line_edit()
	await _click_control(line)
	line.select_all()
	for ch in "300":
		await _press_key(KEY_0 + int(ch), ch.unicode_at(0))
	await _press_key(KEY_ENTER, 0)
	_check(int(spin.value) == 300, "auction: the bid field takes 300 MNOK by typing (%d)" % int(spin.value))
	await _click_control(card.get_node("%PrimaryBtn"))
	a = _sim.get_licence_auction("po04_solund")
	_check(str(a.get("leader_kind", "")) == "player" and is_equal_approx(float(a.get("current_bid_nok", 0.0)), 300_000_000.0),
		"auction: the 300 MNOK bid leads")
	get_viewport().gui_release_focus()
	await _press_key(KEY_4, 52)
	var settled: bool = await _wait_until(func() -> bool:
		return str(_sim.get_licence_auction("po04_solund").get("status", "")) != "open" or _sim.is_paused(), 30.0)
	if str(_sim.get_licence_auction("po04_solund").get("status", "")) == "open":
		# The clock stopped the day before the close (the company leads, so no
		# stop is due: this only catches a card that held it); run on by key.
		await _press_key(KEY_SPACE, 32)
		settled = await _wait_until(func() -> bool:
			return str(_sim.get_licence_auction("po04_solund").get("status", "")) != "open", 30.0)
	_sim.set_paused(true)
	await get_tree().process_frame
	a = _sim.get_licence_auction("po04_solund")
	_check(settled and str(a.get("status", "")) == "won_player" and int(a.get("rival_bids", 0)) >= 1,
		"auction: the company wins the licence against %d rival bids (%s)" % [int(a.get("rival_bids", 0)), str(a.get("status", ""))])
	var area4: Dictionary = {}
	for row in _sim.get_expansion_overview().get("areas", []):
		if int(row.get("production_area", 0)) == 4:
			area4 = row
	_check(int(area4.get("licences", 0)) == 1 and float(area4.get("licence_mtb_t", 0.0)) == 780.0,
		"auction: the licence's 780 t pool in production area 4")
	var ready_title := tr("UI_ATTENTION_LICENCE_READY").format(["Solund"])
	var inbox_titles := func() -> Array:
		var found := []
		for item in _sim.get_attention_items():
			found.append(str(item.get("title", "")))
		return found
	_check(inbox_titles.call().has(ready_title), "auction: the inbox says the won licence is ready ('%s')" % ready_title)
	map.open_auction("po04_solund")
	await get_tree().process_frame
	_check(card.mode() == "result" and (card.get_node("%ResultTitle") as Label).text == tr("EXPANSION_AUCTION_WON_TITLE"),
		"auction: the card shows the win")
	await _click_control(card.get_node("%PrimaryBtn"))
	var dialog: BuildSiteDialog = map.build_dialog()
	_check(dialog.visible and dialog.zone_id() == "po04_solund" and bool(dialog.plan().get("allowed", false)),
		"auction: the won licence leads on to a buildable locality in Solund")
	await _click_control(dialog.get_node("%ConfirmBtn"))
	var built: Array = _sim.get_all_pens().filter(func(p: Dictionary) -> bool: return str(p.get("zone", "")) == "po04_solund")
	_check(built.size() == 1, "auction: the locality is built in the won zone")
	_check(not inbox_titles.call().has(ready_title), "auction: the used licence leaves the inbox")
	dialog.close()
	GameplayPrefs.force_auto_pause(null)
	scene.queue_free()
	await get_tree().process_frame

## U2-01 acceptance 3: every reason the build gate refuses for is rendered as
## text, never as a key: a new zone without a licence, too little cash, a zone
## that does not exist; and every reason key has text in both languages. The
## licence refusals the map shows read as text too (a zone already farmed, a
## red area), and a disabled button says why in its tooltip.
func _test_every_build_refusal_reads_as_text() -> void:
	_check(_sim.start_qa_scenario("forste_sesong", 91), "refusals: Første sesong starts")
	var scene := await _spawn_scene_over_current_game()
	var ui: Control = scene.get_node("UILayer/GameUI")
	var map := await _open_map_by_key(ui)
	if map == null:
		_check(false, "refusals: the map opens")
		scene.queue_free()
		return
	map.select_area(3)
	await get_tree().process_frame
	var section: RegionExpansionSection = map.expansion_section()
	var shown := {}
	for zone_id in ["fjord_west", "po03_bomlo"]:
		var card := section.zone_card(zone_id)
		for which in ["Build", "Licence"]:
			var reason: Label = card.get_node("Body/" + which + "/Reason")
			var key := str(reason.get_meta(&"label_key", ""))
			if key != "":
				shown[key] = reason.text
	# The 4 MNOK family farm cannot pay a 6 MNOK build in its own zone yet.
	_check(str(shown.get("EXPANSION_REASON_INSUFFICIENT_FUNDS", "")).begins_with(tr("EXPANSION_REASON_INSUFFICIENT_FUNDS")),
		"refusals: too little cash reads as text ('%s')" % str(shown.get("EXPANSION_REASON_INSUFFICIENT_FUNDS", "")))
	_check(str(shown.get("EXPANSION_REASON_GROWTH_LICENCE_REQUIRED", "")) == tr("EXPANSION_REASON_GROWTH_LICENCE_REQUIRED"),
		"refusals: a new zone without a licence reads as text ('%s')" % str(shown.get("EXPANSION_REASON_GROWTH_LICENCE_REQUIRED", "")))
	_check(str(shown.get("EXPANSION_REASON_ALREADY_OPERATING", "")) == tr("EXPANSION_REASON_ALREADY_OPERATING"),
		"refusals: a farmed zone's licence refusal reads as text")
	var new_zone_licence: Button = section.zone_card("po03_bomlo").get_node("Body/Licence/LicenceBtn")
	_check(not new_zone_licence.disabled, "refusals: the new zone's licence can be applied for")
	var farmed_licence: Button = section.zone_card("fjord_west").get_node("Body/Licence/LicenceBtn")
	_check(farmed_licence.disabled and farmed_licence.tooltip_text == tr("EXPANSION_REASON_ALREADY_OPERATING"),
		"refusals: a disabled button says why in its tooltip")
	# A zone that does not exist, through the dialog.
	map.open_build("nowhere")
	await get_tree().process_frame
	var dialog: BuildSiteDialog = map.build_dialog()
	var verdict: Label = dialog.get_node("%VerdictLabel")
	_check(verdict.visible and verdict.text == tr("EXPANSION_REASON_ZONE_NOT_FOUND") and (dialog.get_node("%ConfirmBtn") as Button).disabled,
		"refusals: a zone that does not exist reads as text and the confirm waits ('%s')" % verdict.text)
	dialog.close()
	# Every reason key the engine can give has text in both languages.
	var missing := []
	for key in ["engine_not_ready", "zone_not_found", "growth_licence_required", "insufficient_funds", "zone_red",
			"already_operating", "licence_held", "auction_open", "bid_too_low", "auction_closed", "auction_not_found"]:
		var tkey: StringName = StringName("EXPANSION_REASON_" + key.to_upper())
		for locale in ["nb", "en"]:
			var t := TranslationServer.get_translation_object(locale)
			var text := String(t.get_message(tkey)) if t != null else ""
			if text.strip_edges() == "" or text == String(tkey):
				missing.append("%s/%s" % [tkey, locale])
	_check(missing.is_empty(), "refusals: every reason has Norwegian and English text %s" % str(missing))
	# A red area (lice over the red limit at the weekly assessment): the growth
	# zone's licence refusal reads as text.
	_sim.set_qa_pen_lice(str(_sim.get_all_pens()[0].get("id", "")), 3.0)
	_sim.advance_days(8)
	map.select_area(3)
	await get_tree().process_frame
	var red: Label = map.expansion_section().zone_card("po03_bomlo").get_node("Body/Licence/Reason")
	_check(str(_sim.get_expansion_overview()["areas"][2].get("traffic_light", "")) == "red"
			and red.text == tr("EXPANSION_REASON_ZONE_RED"),
		"refusals: a red area's growth zone reads 'Rødt trafikklys' ('%s')" % red.text)
	await _lint_screen("map expansion")
	scene.queue_free()
	await get_tree().process_frame

## F2-03 (FARM_TYPES_SPEC §7.1 acceptance 6): an offshore unit is a sea site
## in the engine, but the fjord's layout never sees it. FishPenManager lays
## the fjord sites into channel slots by their index in GetSeaSiteDetails,
## and the feed barges follow the same list; the bridge lists the fjord sites
## unless asked. Building (and paying for) an OF1-class unit beside
## po3_red_sone's two sites leaves every fjord site node, pen and barge where
## it was, the unit stands in its own list on off_1 with its 3 zones, and the
## 3D fjord draws no fjord site for it, today and a day later.
func _test_offshore_unit_leaves_fjord_pens_in_place() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 91), "offshore unit: seeded scenario starts")
	_sim.set_paused(true)
	var scene: Node = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	add_child(scene)
	for i in 5:
		await get_tree().process_frame
	_sim.set_paused(true)
	var env: Node = scene.get_node("FjordEnvironment")
	var pens: FishPenManager = env.get_node("FishPenManager")
	var barges: FeedBargeVisualManager = env.get_node("FeedBargeVisualManager")
	# The barges come with the first state sync (stocked at the start: on station).
	_sim.state_updated.emit()
	for i in 3:
		await get_tree().process_frame
	var fjord_ids: Array = _site_ids(_sim.get_all_pens())
	var before := _fjord_layout(pens, barges)
	_check(fjord_ids.size() == 2 and (before.get("sites", {}) as Dictionary).size() == 2
		and (before.get("barges", []) as Array).size() == 2,
		"offshore unit: the fjord draws po3_red_sone's two sites and their barges (%s)" % str(fjord_ids))

	var bridge := get_node("/root/GameBridge")
	var cash: float = float(_sim.balance)
	_check(bool(bridge.QaGrantCash(1_000_000_000.0)) and float(_sim.balance) > cash + 999_000_000.0,
		"offshore unit: a QA grant pays for the build")
	_check(_sim.build_offshore_farm("Havmerd UI", "semi_submersible"), "offshore unit: an OF1-class unit is built")
	_sim.state_updated.emit()
	for i in 3:
		await get_tree().process_frame
	var units: Array = _sim.get_offshore_units()
	var unit: Dictionary = units[0] if units.size() == 1 else {}
	_check(str(unit.get("plot_id", "")) == "off_1" and int(unit.get("zones", 0)) == 3
		and (unit.get("pens", []) as Array).size() == 3,
		"offshore unit: it stands in its own list on off_1 with its 3 zones (%s)" % str(units.map(func(u: Dictionary) -> String: return "%s@%s" % [str(u.get("id", "")), str(u.get("plot_id", ""))])))
	var unit_id := str(unit.get("id", ""))
	var free_slots: Array = _sim.get_offshore_plots().filter(func(p: Dictionary) -> bool: return bool(p.get("free", false)))
	_check(free_slots.size() == 2 and _site_ids(free_slots) == ["off_2", "hav_1"],
		"offshore unit: two of the three offshore slots stay free (%s)" % str(_site_ids(free_slots)))
	_check(_site_ids(_sim.get_all_pens()) == fjord_ids, "offshore unit: the fjord's site list is unchanged (%s)" % str(_site_ids(_sim.get_all_pens())))
	_check(unit_id != "" and not (unit_id in pens.get_pen_ids()), "offshore unit: the 3D fjord draws no fjord site for it")
	var after := _fjord_layout(pens, barges)
	_check(after == before, "offshore unit: every fjord site, pen and feed barge stands where it stood (%s)" % _layout_change(before, after))

	_sim.advance_days(1)
	_sim.set_paused(true)
	_sim.state_updated.emit()
	for i in 3:
		await get_tree().process_frame
	var next_day := _fjord_layout(pens, barges)
	_check(next_day == before, "offshore unit: a day later the fjord layout is still the same (%s)" % _layout_change(before, next_day))
	scene.queue_free()
	await get_tree().process_frame


## The ids of a list of site or plot dictionaries, in order.
func _site_ids(rows: Array) -> Array:
	return rows.map(func(r: Dictionary) -> String: return str(r.get("id", "")))


## Where the fjord's sites are drawn: per site its node, centre, pen count and
## every pen's rest position (rounded to the millimetre); the feed barges'
## sites and their states.
func _fjord_layout(pens: FishPenManager, barges: FeedBargeVisualManager) -> Dictionary:
	var sites := {}
	for sid in pens.get_pen_ids():
		var farm := pens.get_site(sid)
		if farm == null:
			continue
		var rest := []
		for k in farm.pen_count:
			rest.append((farm.pen_rest_position(k) * 1000.0).round())
		sites[sid] = [farm.get_instance_id(), (pens.get_pen_position(sid) * 1000.0).round(), farm.pen_count, rest]
	var boats := []
	var ids: Array = barges.active_site_ids()
	ids.sort()
	for sid in ids:
		boats.append("%s:%s" % [str(sid), barges.barge_state(str(sid))])
	return {"sites": sites, "barges": boats}


## What moved between two layouts, for a failure message.
func _layout_change(before: Dictionary, after: Dictionary) -> String:
	if before == after:
		return "nothing moved"
	var changes := []
	var a: Dictionary = before.get("sites", {})
	var b: Dictionary = after.get("sites", {})
	for sid in a.keys() + b.keys():
		if a.get(sid) != b.get(sid) and not changes.has(str(sid)):
			changes.append(str(sid))
	if before.get("barges") != after.get("barges"):
		changes.append("barges %s -> %s" % [str(before.get("barges")), str(after.get("barges"))])
	return ", ".join(changes)


## F2-04: the farm-type catalogue reaches the UI through SimBridge, the one
## source of every farm-type number on a card: three families, seven variants
## with the same axes, each value with its source keys (a null value names its
## reason), every unlock gate with its status (po3_red_sone starts with
## deep-sea-farming researched: the OF1-class research gate is met, the unit is
## still locked). The tech tree's tooltip, reached with a real mouse hover,
## names the unit kinds a narrowed node reaches and the farm types it opens.
func _test_farm_type_catalog_reaches_the_ui() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 93), "farm types: seeded scenario starts")
	_sim.set_paused(true)
	var cat: Dictionary = _sim.get_farm_type_catalog()
	var axes: Array = cat.get("axes", [])
	var families: Array = cat.get("families", [])
	var family_ids: Array = families.map(func(f: Dictionary) -> String: return str(f.get("id", "")))
	_check(family_ids == ["sjo", "land", "hav"], "farm types: three families (%s)" % str(family_ids))
	var variants := {}
	for f in families:
		for v in f.get("variants", []):
			variants[str(v.get("id", ""))] = v
	_check(variants.keys() == ["open_pen", "closed_pen", "post_smolt", "hfs", "ras", "of1", "havfarm"],
		"farm types: seven variants in catalogue order (%s)" % str(variants.keys()))
	var problems: Array = []
	for id in variants:
		var va: Dictionary = variants[id].get("axes", {})
		if va.size() != axes.size():
			problems.append("%s has %d axes" % [id, va.size()])
		for a in axes:
			var e: Dictionary = va.get(a, {})
			if (e.get("sources", []) as Array).is_empty():
				problems.append("%s.%s has no source" % [id, a])
			if e.get("median") == null and str(e.get("note", "")) == "":
				problems.append("%s.%s is empty without a reason" % [id, a])
	_check(axes.size() == 7 and problems.is_empty(), "farm types: every card has the 7 axes with sources (%s)" % str(problems))
	var untranslated: Array = []
	for f in families:
		if tr(str(f.get("name_key", ""))) == str(f.get("name_key", "")):
			untranslated.append(f.get("name_key"))
	for id in variants:
		var key := str(variants[id].get("name_key", ""))
		if tr(key) == key:
			untranslated.append(key)
	_check(untranslated.is_empty() and tr("FARM_VARIANT_OF1") == "Havmerd" and tr("FARM_FAMILY_SJO") == "Sjø",
		"farm types: every family and variant has a Norwegian name (%s)" % str(untranslated))
	var of1_capex := float(variants["of1"]["axes"]["capex_nok_per_t_year"].get("median", 0.0))
	_check(of1_capex > 131000.0 and of1_capex < 133000.0,
		"farm types: the OF1-class capacity cost comes through (%.0f NOK per t a year)" % of1_capex)
	var research_met := false
	for g in variants["of1"].get("gates", []):
		if str(g.get("kind", "")) == "Research" and str(g.get("subject", "")) == "deep-sea-farming":
			research_met = bool(g.get("met", false))
	_check(bool(variants["open_pen"].get("unlocked", false)) and research_met and not bool(variants["of1"].get("unlocked", true)),
		"farm types: the open pen is open, OF1's research gate met, the unit still locked")

	# The tech tree: hover land-based-ras with the mouse.
	_start_paused_game()
	var tree: Control = (load("res://scripts/ui/TechTreePanel.gd") as GDScript).new()
	add_child(tree)
	await get_tree().process_frame
	tree.on_panel_shown()
	await get_tree().process_frame
	var node_panel: Control = tree._node_panels.get("land-based-ras")
	_check(node_panel != null, "farm types: the tech tree shows land-based-ras")
	if node_panel == null:
		tree.queue_free()
		return
	# Bring the node to the middle of the window, then move the cursor onto it.
	var vp := get_viewport()
	var centre := node_panel.get_global_rect().get_center()
	tree.position += vp.get_visible_rect().get_center() - centre
	await get_tree().process_frame
	centre = node_panel.get_global_rect().get_center()
	vp.notification(Node.NOTIFICATION_VP_MOUSE_ENTER)
	for p in [centre + Vector2(-40, 0), centre]:
		var ev := InputEventMouseMotion.new()
		ev.position = p
		ev.global_position = p
		vp.push_input(ev, true)
		await get_tree().process_frame
	var text: String = tree._tooltip_label.text
	_check(tr("UI_TECH_APPLIES_TO") % tr("RESEARCH_SCOPE_LAND") in text
			and tr("UI_TECH_UNLOCKS") % ", ".join([tr("FARM_VARIANT_POST_SMOLT"), tr("FARM_VARIANT_HFS")]) in text,
		"farm types: the tooltip names land tanks and the two land types it opens (%s)" % text.replace("\n", " | "))
	tree.queue_free()
	await get_tree().process_frame


## F2-05: a fjord site's Driftsplan card. Real clicks turn the two fjord
## orders on and off, a changed setting reaches the engine's order, the
## defaults and ranges are the engine's, the card leads with the engine's
## "Neste planlagt" and its status lines are the engine's (what the order
## waits for, what it last did); the order runs when the day runs. A farm-type
## event names its unit in the notification text.
func _test_driftsplan_card_sets_and_runs_standing_orders() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 41), "driftsplan: po3_red_sone starts")
	_sim.set_paused(true)
	var site_id := "po3_nord"
	var panel: Control = (load("res://scenes/ui/PenManagement.tscn") as PackedScene).instantiate()
	add_child(panel)
	await get_tree().process_frame
	panel.select_pen(site_id)
	await get_tree().process_frame
	var card: DriftsplanCard = panel.driftsplan_card()
	_check(card != null and card.is_visible_in_tree(), "driftsplan: the pen view shows the card")
	if card == null:
		panel.queue_free()
		return
	var scroll: ScrollContainer = panel.get_node("Margin/MainSplit/DetailScroll")
	var net := card.policy_check("net_cleaning")
	var lice := card.policy_check("delouse_on_count")
	_check(net != null and lice != null and not net.button_pressed and not lice.button_pressed
			and net.text == tr("DRIFTSPLAN_POLICY_NET_CLEANING"),
		"driftsplan: both fjord orders are listed, off, with Norwegian names (%s)" % (net.text if net else "none"))
	_check(card.next_planned_label().text == tr("DRIFTSPLAN_NEXT_NONE")
			and card.policy_status("net_cleaning").text == tr("DRIFTSPLAN_STATUS_OFF"),
		"driftsplan: nothing planned yet (%s)" % card.next_planned_label().text)
	var interval := card.setting_control("net_cleaning", "interval_days") as SpinBox
	var share := card.setting_control("delouse_on_count", "share_of_limit") as SpinBox
	_check(interval != null and int(interval.value) == 14 and int(interval.min_value) == 7 and int(interval.max_value) == 60
			and share != null and is_equal_approx(share.value, 80.0) and is_equal_approx(share.min_value, 50.0),
		"driftsplan: the defaults and ranges are the engine's (%d d in 7-60, %.0f %%)" % [int(interval.value), share.value])

	scroll.ensure_control_visible(net)
	await get_tree().process_frame
	await _click_control(net)
	var orders: Array = _sim.get_driftsplan(site_id)
	_check(orders.size() == 1 and str(orders[0].get("policy", "")) == "net_cleaning"
			and int(orders[0].get("numbers", {}).get("interval_days", 0)) == 14,
		"driftsplan: a click on net care sets the order with the card's values (%s)" % str(orders))
	_check(card.next_planned_label().text == tr("DRIFTSPLAN_NEXT") % [tr("DRIFTSPLAN_POLICY_NET_CLEANING"), tr("DRIFTSPLAN_WHEN_TODAY")],
		"driftsplan: the card leads with today's net care (%s)" % card.next_planned_label().text)
	interval.value = 21
	await get_tree().process_frame
	_check(int(_sim.get_driftsplan(site_id)[0].get("numbers", {}).get("interval_days", 0)) == 21,
		"driftsplan: a changed interval reaches the engine's order")

	_sim.advance_day()
	await get_tree().process_frame
	card.refresh()
	var entry: Dictionary = _sim.get_driftsplan(site_id)[0]
	var expected_status := "%s · %s" % [tr("DRIFTSPLAN_WHEN") % [GameCalendar.short_date(21), 20],
		tr("DRIFTSPLAN_LAST") % [GameCalendar.short_date(0), tr("DRIFTSPLAN_OUTCOME_OK")]]
	_check(int(entry.get("issued_count", 0)) == 1 and int(entry.get("last_issued_day", -1)) == 0
			and card.policy_status("net_cleaning").text == expected_status,
		"driftsplan: the day ran the order, and the card says when it runs next and what it did (%s)" % card.policy_status("net_cleaning").text)

	# A value prepared while the order is off survives the day's refresh and is what the switch sets.
	share.value = 70.0
	_sim.advance_day()
	await get_tree().process_frame
	card.refresh()
	_check(is_equal_approx(share.value, 70.0) and _sim.get_driftsplan(site_id).size() == 1,
		"driftsplan: a share prepared while the order is off stays (%.0f %%) and sets nothing yet" % share.value)
	scroll.ensure_control_visible(lice)
	await get_tree().process_frame
	await _click_control(lice)
	var prepared := 0.0
	for e in _sim.get_driftsplan(site_id):
		if str(e.get("policy", "")) == "delouse_on_count":
			prepared = float(e.get("numbers", {}).get("share_of_limit", 0.0))
	_check(is_equal_approx(prepared, 0.7), "driftsplan: the switch sets the prepared share (%.2f)" % prepared)
	var method := card.setting_control("delouse_on_count", "method") as OptionButton
	var thermal := -1
	for i in method.item_count:
		if str(method.get_item_metadata(i)) == "thermolicer":
			thermal = i
	method.select(thermal)
	method.item_selected.emit(thermal)
	await get_tree().process_frame
	var lice_order: Dictionary = {}
	for e in _sim.get_driftsplan(site_id):
		if str(e.get("policy", "")) == "delouse_on_count":
			lice_order = e
	var level := float(lice_order.get("next", {}).get("trigger_value", 0.0))
	_check(str(lice_order.get("choices", {}).get("method", "")) == "thermolicer" and method.get_item_text(thermal) == tr("DRIFTSPLAN_CHOICE_THERMOLICER"),
		"driftsplan: the delousing order takes the chosen method (%s)" % str(lice_order.get("choices")))
	_check(level > 0.1 and card.policy_status("delouse_on_count").text == tr("DRIFTSPLAN_WAIT_LICE_COUNT_ABOVE") % SimFormatters.num(level, 2),
		"driftsplan: the order says it waits for a count over its level (%s)" % card.policy_status("delouse_on_count").text)

	scroll.ensure_control_visible(net)
	await get_tree().process_frame
	await _click_control(net)
	var left: Array = _sim.get_driftsplan(site_id).map(func(e: Dictionary) -> String: return str(e.get("policy", "")))
	_check(left == ["delouse_on_count"] and not net.button_pressed
			and card.policy_status("net_cleaning").text == tr("DRIFTSPLAN_STATUS_OFF"),
		"driftsplan: a second click ends net care (%s)" % str(left))

	var unit_event := {"type": "silo_low", "severity": "warning", "unit_id": "offshore_1", "unit_name": "Havmerd Vest", "site_id": "offshore_1"}
	_check(GameEventText.subject(unit_event, _sim) == "Havmerd Vest"
			and GameEventText.has_verb({"counter_verb": "open_driftsplan"}) and tr("GAME_EVENT_DRIFTSPLAN_COMMAND_FAILED") != "GAME_EVENT_DRIFTSPLAN_COMMAND_FAILED",
		"driftsplan: a farm-type event names its unit, and a failed order has its verb and title")
	panel.queue_free()
	await get_tree().process_frame


## F2-12 / F2-14 (wave4/sim-land-1): the land lane's bridge verbs marshal from
## GDScript and read as text: a build preview with its payment schedule and
## permit timeline (refusal odds and drivers, each with a Norwegian label), a
## refusal as a reason key with its label, a zoning application that files a
## case the pad's case list shows, and an empty construction list.
func _test_land_bridge_previews_and_applies() -> void:
	_check(_sim.start_qa_scenario("po3_red_sone", 94), "land bridge: seeded scenario starts")
	_sim.set_paused(true)
	var locked: Dictionary = _sim.preview_land_build("pad_a", "hfs_lot", "managed", [])
	_check(not bool(locked.get("allowed", true)) and str(locked.get("reason_key", "")) == "not_unlocked"
			and tr(str(locked.get("label_key", ""))) != str(locked.get("label_key", "")),
		"land bridge: a locked build reads as text (%s)" % tr(str(locked.get("label_key", ""))))
	var module: Dictionary = _sim.preview_land_build("pad_delta", "post_smolt_module", "turnkey", ["environmental_survey"])
	var payments: Array = module.get("payments", [])
	var paid := 0.0
	for p in payments:
		paid += float(p.get("payment_nok", 0.0))
	var budget := float(module.get("budget_nok", 0.0))
	_check(payments.size() == 5 and budget > 0.0 and absf(paid - budget - float(module.get("premium_nok", 0.0))) < 1.0,
		"land bridge: the module's payments add up to its budget and the turnkey premium (%d phases, %.0f of %.0f)" % [payments.size(), paid, budget])
	var phase_labels: Array = payments.filter(func(p: Dictionary) -> bool: return tr(str(p.get("label_key", ""))) == str(p.get("label_key", "")))
	_check(phase_labels.is_empty(), "land bridge: every phase has a Norwegian name (%s)" % str(phase_labels))
	var permits: Array = module.get("permits", [])
	var statuses: Array = permits.map(func(p: Dictionary) -> String: return str(p.get("status", "")))
	_check(permits.size() == 5 and statuses[0] == "pre_zoned" and statuses.slice(1).all(func(s: String) -> bool: return s == "needed"),
		"land bridge: the module's permit timeline (%s)" % str(statuses))
	var untranslated: Array = []
	for p in permits:
		var odds := 0.0
		for d in p.get("drivers", []):
			odds += float(d.get("effect", 0.0))
			if tr(str(d.get("label_key", ""))) == str(d.get("label_key", "")):
				untranslated.append(d.get("label_key"))
		if absf(odds - float(p.get("refusal_probability", -1.0))) > 1e-9:
			untranslated.append("%s odds" % str(p.get("kind")))
		if tr(str(p.get("label_key", ""))) == str(p.get("label_key", "")) or tr(str(p.get("status_label_key", ""))) == str(p.get("status_label_key", "")):
			untranslated.append(p.get("label_key"))
	_check(untranslated.is_empty(), "land bridge: each permit's drivers add up to its odds and read as text (%s)" % str(untranslated))
	_check(int(module.get("first_harvest_day", 0)) > int(module.get("first_tanks_day", 0)) and int(module.get("first_tanks_day", 0)) > int(module.get("start_day", 0)),
		"land bridge: the first harvest follows the first tanks (%d, %d)" % [int(module.get("first_tanks_day", 0)), int(module.get("first_harvest_day", 0))])
	var zoning: Dictionary = _sim.apply_for_zoning("pad_a", ["environmental_survey"])
	var cases: Array = _sim.get_land_permit_cases("pad_a")
	_check(bool(zoning.get("success", false)) and cases.size() == 1 and str(cases[0].get("kind", "")) == "Zoning"
			and tr(str(cases[0].get("label_key", ""))) == "Reguleringsplan",
		"land bridge: a zoning application files a case on the pad (%s)" % str(cases))
	var again: Dictionary = _sim.apply_for_zoning("pad_a", [])
	_check(str(again.get("reason_key", "")) == "already_pending" and tr(str(again.get("label_key", ""))) != str(again.get("label_key", "")),
		"land bridge: a second application is refused as text (%s)" % tr(str(again.get("label_key", ""))))
	_check(_sim.get_land_construction().is_empty() and str(_sim.get_land_site_detail("ras_9").get("reason_key", "")) == "not_found",
		"land bridge: no construction yet, and an unknown site is not found")
