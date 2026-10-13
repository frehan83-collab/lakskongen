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
