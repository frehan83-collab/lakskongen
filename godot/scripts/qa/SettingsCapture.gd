extends "res://tests/UiFlowTests.gd"
## U2-08 frames: the settings that apply and the richer save slots, captured
## from the real GameScene and main menu (po3_red_sone, seed 91). The shots in
## godot/qa/shots.json for this scene (44_*) name the moment; the frames go to
## res://screenshots/game/44_*.png. Saves, settings and the campaign are the
## UI tests' own files, never the player's. Windowed, the save slots carry
## real pictures (SaveThumbnails renders the world without the UI).
## Run: tools/capture_screenshots.sh --scene res://qa/scenes/SettingsCapture.tscn
## Headless (no image data) it walks the same moments and only names them.

const SCENE_PATH := "res://qa/scenes/SettingsCapture.tscn"
const SHOTS_FILE := "res://qa/shots.json"
const OUT_DIR := "res://screenshots/game/"
const SETTLE_S := 0.6

var _shot_of_moment: Dictionary = {}
var _captured: Array[String] = []
var _capture_failures := 0


func _ready() -> void:
	_go.call_deferred()


func _go() -> void:
	_sim = get_node("/root/SimBridge")
	var mgr := get_node_or_null("/root/SaveLoadManager")
	mgr.UseSaveDirectory(TEST_SAVE_DIR)
	_clear_test_saves()
	CampaignProgress.path_override = TEST_ROOT + "campaign.json"
	SettingsStore.path_override = TEST_ROOT + "settings_capture.json"
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SettingsStore.path()))
	var applier := get_node("/root/SettingsApplier")
	applier.apply_all(SettingsStore.load_all(), false)
	TranslationServer.set_locale("no")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_DIR))
	for shot: Dictionary in _shots():
		_shot_of_moment[str(shot.get("state", ""))] = shot
	if not _shot_of_moment.is_empty() and not _headless():
		var res: Array = (_shot_of_moment.values()[0] as Dictionary).get("resolution", [1920, 1080])
		get_window().size = Vector2i(int(res[0]), int(res[1]))
	MotionPrefs.force(true)

	# A game with a few saves, each with its picture of the world.
	var scene := await _spawn_po3_scene(91)
	var cam := get_viewport().get_camera_3d() as FjordCamera
	await get_tree().create_timer(2.5).timeout
	_sim.advance_days(40)
	await get_tree().create_timer(1.0).timeout
	await _save_with_picture("Vårutsett 2027")
	if cam != null:
		var pens := scene.get_node("FjordEnvironment/FishPenManager") as FishPenManager
		var ids: Array = pens.get_pen_ids()
		if not ids.is_empty():
			cam.auto_orbit_enabled = false
			cam.fly_to_position(pens.get_pen_position(str(ids[0])), FjordCamera.ZoomLevel.FARM, 0.05)
			await get_tree().create_timer(2.0).timeout
	_sim.advance_days(60)
	await get_tree().create_timer(1.0).timeout
	await _save_with_picture("Før lusetelling")
	mgr.AutoSave()
	await _wait_picture()

	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	pause.show_menu()
	await get_tree().create_timer(0.3).timeout
	pause.btn_save.pressed.emit()
	await _moment("save_slots")
	pause.save_load_menu.hide()
	pause._on_submenu_closed()
	pause.btn_settings.pressed.emit()
	var settings: Control = pause.settings_menu
	var tabs: TabContainer = settings.get_node("%Tabs")
	tabs.current_tab = 2
	await _moment("settings_gameplay_nb")
	var lang: OptionButton = settings.get_node("%LanguageOption")
	lang.select(1)
	lang.item_selected.emit(1)
	await _moment("settings_gameplay_en")
	lang.select(0)
	lang.item_selected.emit(0)
	tabs.current_tab = 3
	(settings.get_node("%HighContrastCheck") as CheckBox).button_pressed = true
	await _moment("settings_high_contrast")
	(settings.get_node("%ApplyButton") as Button).pressed.emit()
	settings.hide()
	pause._on_submenu_closed()
	pause.hide_menu()
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("dashboard")
	await _moment("hud_high_contrast")
	applier.apply_high_contrast(false)
	scene.queue_free()
	await get_tree().process_frame

	var menu := await _spawn_main_menu()
	await get_tree().create_timer(2.0).timeout
	menu.btn_load.pressed.emit()
	await _moment("main_menu_load_slots")
	menu.queue_free()

	MotionPrefs.force(null)
	_clear_test_saves()
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SettingsStore.path()))
	SettingsStore.path_override = ""
	CampaignProgress.reset()
	var missing := _shot_of_moment.values().map(func(s: Dictionary) -> String: return str(s["id"])) \
		.filter(func(id: String) -> bool: return not _captured.has(id))
	if not missing.is_empty():
		push_error("[SettingsCapture] moments never reached: %s" % str(missing))
	print("[SettingsCapture] done: %d of %d frames, %d capture failure(s)" % [_captured.size(), _shot_of_moment.size(), _capture_failures])
	get_tree().quit(1 if _capture_failures > 0 or not missing.is_empty() else 0)


func _save_with_picture(name: String) -> void:
	_check(_sim.save_game(name), "capture: saved '%s'" % name)
	await _wait_picture()


func _wait_picture() -> void:
	if _headless():
		return
	var thumbs := get_node("/root/SaveThumbnails")
	var before: int = thumbs.written
	var guard := 0
	while thumbs.written == before and guard < 120:
		await get_tree().process_frame
		guard += 1


func _shots() -> Array:
	var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(SHOTS_FILE))
	if not (data is Dictionary):
		push_error("[SettingsCapture] %s does not parse" % SHOTS_FILE)
		return []
	return ((data as Dictionary).get("shots", []) as Array).filter(
		func(s: Dictionary) -> bool: return str(s.get("scene", "")) == SCENE_PATH)


func _moment(moment: String) -> void:
	await get_tree().create_timer(SETTLE_S).timeout
	if not _shot_of_moment.has(moment):
		return
	await _capture(str(_shot_of_moment[moment]["id"]))


func _headless() -> bool:
	return DisplayServer.get_name() == "headless"


func _capture(shot_id: String) -> void:
	if _headless():
		print("[SettingsCapture] headless, no image for %s" % shot_id)
		_captured.append(shot_id)
		return
	await RenderingServer.frame_post_draw
	var img := get_viewport().get_texture().get_image()
	var path := ProjectSettings.globalize_path(OUT_DIR + shot_id + ".png")
	if img == null or img.save_png(path) != OK:
		push_error("[SettingsCapture] could not save %s" % shot_id)
		_capture_failures += 1
		return
	_captured.append(shot_id)
	print("[SettingsCapture] captured %s -> %s" % [shot_id, path])
