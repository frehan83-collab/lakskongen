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
	var ui: Control = scene.get_node("UILayer/GameUI")
	ui.show_tab("fjord")
	await get_tree().create_timer(2.5).timeout
	_sim.advance_days(40)
	await get_tree().create_timer(1.0).timeout
	# The world on screen as the first save is made (its picture should match).
	await _moment("world_at_save")
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
	# In-run A/B: frame times without and with a save picture being rendered.
	if not _headless():
		var idle: Array = await _frame_times(24)
		var thumbs := get_node("/root/SaveThumbnails")
		var before: int = thumbs.written
		_check(_sim.save_game("Før lusetelling"), "capture: saved 'Før lusetelling'")
		var during: Array = []
		var last := Time.get_ticks_usec()
		while thumbs.written == before and during.size() < 120:
			await get_tree().process_frame
			var now := Time.get_ticks_usec()
			during.append((now - last) / 1000.0)
			last = now
		print("[SettingsCapture] perf: idle frames p50 %.1f max %.1f ms; with the picture (%d frames incl. the save) p50 %.1f max %.1f ms" % [
			_pct(idle, 0.5), _pct(idle, 1.0), during.size(), _pct(during, 0.5), _pct(during, 1.0)])
	else:
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

	# The real window takes the window settings (never fullscreen here: on
	# macOS that opens a new Space on the owner's desktop).
	if not _headless():
		var win := get_window()
		var before := win.size
		var applied := DisplaySettings.apply({"resolution": "1600x900", "fullscreen": false, "vsync": false}, win)
		await get_tree().create_timer(0.6).timeout
		print("[SettingsCapture] window: asked %s, got %s, mode %d, vsync %d (usable screen %s)" % [str(applied["size"]),
			str(win.size), win.mode, DisplayServer.window_get_vsync_mode(), str(DisplayServer.screen_get_usable_rect().size)])
		DisplaySettings.apply({"resolution": "%dx%d" % [before.x, before.y], "fullscreen": false, "vsync": true}, win)
		await get_tree().create_timer(0.6).timeout
		print("[SettingsCapture] window: back to %s, vsync %d" % [str(win.size), DisplayServer.window_get_vsync_mode()])

	MotionPrefs.force(null)
	# Keep the slots' pictures beside the frames (compared with 44_world_at_save).
	if not _headless():
		for entry: Dictionary in _sim.get_save_list():
			var thumb := str(entry.get("thumbnail", ""))
			if thumb != "":
				DirAccess.copy_absolute(ProjectSettings.globalize_path(thumb),
					ProjectSettings.globalize_path(OUT_DIR + "44_picture_" + str(entry.get("file", "")).get_basename().validate_filename() + ".png"))
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


func _frame_times(n: int) -> Array:
	var out: Array = []
	var last := Time.get_ticks_usec()
	for i in n:
		await get_tree().process_frame
		var now := Time.get_ticks_usec()
		out.append((now - last) / 1000.0)
		last = now
	return out


func _pct(values: Array, q: float) -> float:
	if values.is_empty():
		return 0.0
	var s := values.duplicate()
	s.sort()
	return float(s[clampi(int(round(q * (s.size() - 1))), 0, s.size() - 1)])


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
