extends RefCounted

## U2-08 (settings that apply, richer save slots) and Au2-01 (the audio
## floor), driven by the UI flow runner (UiFlowTests.gd):
##   * every persisted setting (SettingsStore.all_keys()) has an observable
##     effect, written to this checkout's own settings file and read back by
##     its real consumer (no force seams);
##   * the settings menu applies what its widgets say (language, high
##     contrast, Bruk writes and applies);
##   * save slots show the scenario, an ISO timestamp and a picture;
##   * every Button clicks on the UI bus (one global hook), a button made later
##     too, and a real mouse click plays it;
##   * the verbs play their cue once (feed plan and delouse by the player's
##     actions, stocking through both of its signals, the storm and the
##     auction from the event bus, the harvest's stinger);
##   * the ambience bed: every layer a playing loop of at least 30 s, its
##     gains the weather's on screen, the engines on the boats.

const SCENARIO := "po3_red_sone"
const SEED := 91
## The clock-override base for the autosave interval checks (ms).
const CLOCK_T0 := 10_000_000

var t: Node = null
var _store_before := ""


## A window that records what it is told (headless has no real one to ask).
class _RecPort:
	var calls: Array = []
	var usable := Vector2i(2560, 1440)
	func usable_size() -> Vector2i:
		return usable
	func set_mode(mode: int) -> void:
		calls.append(["mode", mode])
	func set_size(size: Vector2i) -> void:
		calls.append(["size", size])
	func center() -> void:
		calls.append(["center", null])
	func set_vsync(mode: int) -> void:
		calls.append(["vsync", mode])
	func last(kind: String) -> Variant:
		for i in range(calls.size() - 1, -1, -1):
			if calls[i][0] == kind:
				return calls[i][1]
		return null
	func has(kind: String) -> bool:
		return calls.any(func(c: Array) -> bool: return c[0] == kind)


func run(runner: Node) -> void:
	t = runner
	_store_before = SettingsStore.path_override
	SettingsStore.path_override = t.TEST_ROOT + "settings_u208.json"
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SettingsStore.path()))
	_applier().apply_all(SettingsStore.load_all(), false)
	await _every_setting_has_an_observable_effect()
	await _the_settings_menu_applies_through_its_widgets()
	await _save_slots_show_scenario_time_and_picture()
	await _every_button_clicks()
	await _verbs_play_their_cue_once()
	await _the_ambience_bed_follows_the_weather()
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SettingsStore.path()))
	SettingsStore.path_override = _store_before
	_applier().display_port = null
	_applier().apply_all(SettingsStore.load_all(), false)


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


func _applier() -> Node:
	return t.get_node("/root/SettingsApplier")


## The defaults with `changes` on top, written as the settings file and applied.
func _apply_settings(changes: Dictionary, port: Object = null) -> Dictionary:
	DirAccess.remove_absolute(ProjectSettings.globalize_path(SettingsStore.path()))
	SettingsStore.save_all(changes)
	_applier().display_port = port
	return _applier().apply_all(SettingsStore.load_all(), port != null)


# ── U2-08 acceptance 1: every persisted setting has an observable effect ─────

func _every_setting_has_an_observable_effect() -> void:
	var covered: Array[String] = []
	var port := _RecPort.new()
	MotionPrefs.force(null)
	GameplayPrefs.force_auto_pause(null)
	GameplayPrefs.force_auto_slow(null)
	GameplayPrefs.force_tutorial(null)

	# Audio: each slider's bus.
	var buses := {"master": "Master", "music": "Music", "ambient": "Ambient", "sfx": "SFX"}
	for key in buses:
		_apply_settings({"audio": {key: 0.25}})
		var db := AudioServer.get_bus_volume_db(AudioServer.get_bus_index(buses[key]))
		_check(absf(db - linear_to_db(0.25)) < 0.01, "settings: audio.%s at 25 %% sets the %s bus to %.1f dB" % [key, buses[key], db])
		covered.append("audio." + key)
	_apply_settings({})
	_check(is_zero_approx(AudioServer.get_bus_volume_db(AudioServer.get_bus_index("Master"))),
		"settings: the default master volume is full again")

	# The window: size, mode, VSync (a recording port, then the real window).
	_apply_settings({"graphics": {"resolution": "1280x720", "fullscreen": false}}, port)
	_check(port.last("size") == Vector2i(1280, 720) and port.last("mode") == Window.MODE_WINDOWED and port.has("center"),
		"settings: graphics.resolution 1280x720 makes a centred 1280x720 window (%s)" % str(port.last("size")))
	port.usable = Vector2i(1600, 900)
	_apply_settings({"graphics": {"resolution": "2560x1440"}}, port)
	_check(port.last("size") == Vector2i(1600, 900), "settings: a size larger than the screen is clamped to its usable area")
	var win: Window = t.get_window()
	var size_before := win.size
	DisplaySettings.apply({"resolution": "1280x720", "fullscreen": false}, win)
	_check(win.size == Vector2i(1280, 720), "settings: the real window takes the size (%s)" % str(win.size))
	win.size = size_before
	covered.append("graphics.resolution")
	port.calls.clear()
	_apply_settings({"graphics": {"fullscreen": true}}, port)
	_check(port.last("mode") == Window.MODE_FULLSCREEN and not port.has("size"),
		"settings: graphics.fullscreen makes the window fullscreen")
	covered.append("graphics.fullscreen")
	_apply_settings({"graphics": {"vsync": false}}, port)
	var off: Variant = port.last("vsync")
	_apply_settings({"graphics": {"vsync": true}}, port)
	_check(off == DisplayServer.VSYNC_DISABLED and port.last("vsync") == DisplayServer.VSYNC_ENABLED,
		"settings: graphics.vsync turns VSync off and on")
	covered.append("graphics.vsync")
	_check(DisplaySettings.parse_resolution("nonsense") == DisplaySettings.DEFAULT_RESOLUTION
		and DisplaySettings.parse_resolution("640x480") == DisplaySettings.DEFAULT_RESOLUTION,
		"settings: an unreadable or too small resolution falls back to 1920x1080")
	_apply_settings({})

	# Quality: a fjord opened later reads it, and a change reaches a live one.
	_apply_settings({"graphics": {"quality": "low"}})
	var scene: Node = await t._spawn_game_scene()
	var director: EnvironmentDirector = scene.get_node("FjordEnvironment/EnvironmentDirector")
	var vp: Viewport = t.get_viewport()
	_check(director.get_quality_name() == "low" and vp.msaa_3d == Viewport.MSAA_DISABLED,
		"settings: graphics.quality low: a fjord opened later starts on Low (MSAA off)")
	_apply_settings({"graphics": {"quality": "ultra"}})
	_check(director.get_quality_name() == "ultra" and vp.msaa_3d == Viewport.MSAA_4X
		and is_equal_approx(float(director.get_quality_knobs()["scale"]), 1.0),
		"settings: graphics.quality ultra reaches the live fjord at once (MSAA 4x, full scale)")
	covered.append("graphics.quality")

	# Reduced motion: the camera shake refuses, then shakes again.
	_apply_settings({"accessibility": {"reduced_motion": true}})
	var refused := not CameraShake.shake(vp, 0.5)
	_apply_settings({"accessibility": {"reduced_motion": false}})
	var shook := CameraShake.shake(vp, 0.5) or MotionPrefs.reduced_motion()
	_check(refused and shook, "settings: accessibility.reduced_motion stops the camera shake (and only it does)")
	covered.append("accessibility.reduced_motion")
	_apply_settings({})
	scene.queue_free()
	await t.get_tree().process_frame

	# UI scale.
	_apply_settings({"accessibility": {"ui_scale": 1.25}})
	_check(is_equal_approx(t.get_window().content_scale_factor, 1.25), "settings: accessibility.ui_scale 125 % scales the UI")
	_apply_settings({})
	_check(is_equal_approx(t.get_window().content_scale_factor, 1.0), "settings: and 100 % again")
	covered.append("accessibility.ui_scale")

	# High contrast: the live themes swap, every text colour at least 7:1.
	await _high_contrast_swaps_the_live_themes()
	covered.append("accessibility.high_contrast")

	# Language: the game speaks English, then Norwegian again.
	_apply_settings({"general": {"language": "en"}})
	var en_ok := TranslationServer.get_locale().begins_with("en") and t.tr("UI_SETTINGS_TITLE") == "Settings" \
		and SimFormatters.num(11.49, 2) == "11.49"
	_apply_settings({"general": {"language": "nb"}})
	_check(en_ok and t.tr("UI_SETTINGS_TITLE") == "Innstillinger" and SimFormatters.num(11.49, 2) == "11,49",
		"settings: general.language en speaks English (title, decimal point), nb Norwegian")
	_check(LocalizationManager.normalize("no") == "nb" and LocalizationManager.normalize("en_GB") == "en"
		and LocalizationManager.normalize("de") == "" and LocalizationManager.SUPPORTED_LOCALES == ["nb", "en"],
		"settings: the languages are nb and en ('no' reads as nb, others are refused)")
	covered.append("general.language")
	TranslationServer.set_locale("no")

	# Autosave: a save every N real minutes of play, none when off.
	await _autosave_follows_the_interval()
	covered.append("gameplay.autosave_minutes")

	# Gameplay settings read where they act.
	await _gameplay_settings_reach_their_consumers()
	covered.append_array(["gameplay.default_speed", "gameplay.auto_pause_critical", "gameplay.auto_slow_deadlines",
		"gameplay.notifications_enabled", "gameplay.tutorial_enabled"])

	var missing := SettingsStore.all_keys().filter(func(k: String) -> bool: return not covered.has(k))
	_check(missing.is_empty() and SettingsStore.all_keys().size() == 18,
		"settings: every persisted setting (%d) has a consumer test (missing: %s)" % [SettingsStore.all_keys().size(), str(missing)])
	_apply_settings({})


## WCAG 2.1 contrast: relative luminance of the linearised sRGB channels
## (Color.get_luminance alone weighs the gamma-encoded values).
func _contrast(a: Color, b: Color) -> float:
	var la := a.srgb_to_linear().get_luminance()
	var lb := b.srgb_to_linear().get_luminance()
	return (maxf(la, lb) + 0.05) / (minf(la, lb) + 0.05)


func _high_contrast_swaps_the_live_themes() -> void:
	var theme: Theme = LakskongenTheme.get_theme()
	var chart: Theme = LakskongenTheme.chart_theme
	var label := Label.new()
	label.text = "CI"
	t.add_child(label)
	var normal_text := label.get_theme_color("font_color")
	var normal_alpha := (theme.get_stylebox("panel", "PanelContainer") as StyleBoxFlat).bg_color.a if theme.get_stylebox("panel", "PanelContainer") is StyleBoxFlat else 0.0
	_apply_settings({"accessibility": {"high_contrast": true}})
	var hc_text := label.get_theme_color("font_color")
	var focus := theme.get_stylebox("focus", "Button") as StyleBoxFlat
	var worst := 99.0
	for surf: Theme in [theme, chart]:
		var base := surf.get_color("base", "Palette")
		for key in ["text", "muted", "ok", "warn", "crit", "info", "accent"]:
			worst = minf(worst, _contrast(surf.get_color(key, "Palette"), base))
	var panel := theme.get_stylebox("panel", "PanelContainer")
	_check(LakskongenTheme.is_high_contrast() and hc_text == Color("#FFFFFF") and normal_text == Color("#E8EEF0"),
		"settings: accessibility.high_contrast turns a live label's text from #E8EEF0 to white")
	_check(focus != null and focus.border_width_left == 3 and worst >= 7.0,
		"settings: high contrast draws a 3 px focus ring and every text colour at >= 7:1 (worst %.1f:1)" % worst)
	_check(not (panel is StyleBoxFlat) or (panel as StyleBoxFlat).bg_color.a >= normal_alpha,
		"settings: high-contrast panels are at least as opaque as Ink glass")
	_apply_settings({"accessibility": {"high_contrast": false}})
	_check(not LakskongenTheme.is_high_contrast() and label.get_theme_color("font_color") == Color("#E8EEF0")
		and (theme.get_stylebox("focus", "Button") as StyleBoxFlat).border_width_left == 2,
		"settings: turning it off restores the Kyst & Stål theme exactly")
	label.queue_free()


func _autosave_follows_the_interval() -> void:
	var mgr: Node = t.get_node("/root/SaveLoadManager")
	_apply_settings({"gameplay": {"autosave_minutes": 2}})
	_check(int(mgr.AutoSaveIntervalMinutes) == 2, "settings: gameplay.autosave_minutes 2 reaches the save manager")
	t._start_paused_game()
	t._clear_test_saves()
	mgr.ClockOverrideMs = CLOCK_T0
	mgr.RestartAutoSaveInterval()
	t._sim.advance_days(1)
	mgr.ClockOverrideMs = CLOCK_T0 + 119_000
	t._sim.advance_days(1)
	var early: bool = (t._find_save("autosave_latest") as Dictionary).is_empty()
	mgr.ClockOverrideMs = CLOCK_T0 + 121_000
	var day: int = t._sim.current_day
	t._sim.advance_days(1)
	var saved: Dictionary = t._find_save("autosave_latest")
	_check(early and int(saved.get("day", -1)) == day + 1,
		"settings: with 2 min the autosave comes at the first day after 2 real minutes, not before (day %d)" % int(saved.get("day", -1)))
	_apply_settings({"gameplay": {"autosave_minutes": 0}})
	t._clear_test_saves()
	mgr.ClockOverrideMs = CLOCK_T0 + 121_000 + 3_600_000
	t._sim.advance_days(3)
	_check(int(mgr.AutoSaveIntervalMinutes) == 0 and t._find_save("autosave_latest").is_empty() and int(mgr.AutoSaveDueInMs()) == -1,
		"settings: autosave off: an hour of play writes no autosave")
	mgr.ClockOverrideMs = -1
	_apply_settings({})
	mgr.RestartAutoSaveInterval()


func _gameplay_settings_reach_their_consumers() -> void:
	# notifications_enabled: a warning makes a toast, or only the inbox.
	var scene: Node = await t._spawn_po3_scene(SEED)
	var card: Control = scene.get_node("%CriticalEventCard")
	await t._drain_cards(card)
	var tap: RefCounted = t._tap_events()
	_apply_settings({"gameplay": {"notifications_enabled": false}})
	t._sim.publish_condition_event("algal_bloom", "warning", "", "CI: algeoppblomstring A", "ci-u208-a")
	await t.get_tree().create_timer(0.3).timeout
	var quiet: int = tap.toasts_containing("CI: algeoppblomstring A")
	var routed: int = tap.channel_count("toast", "algal_bloom")
	_apply_settings({"gameplay": {"notifications_enabled": true}})
	t._sim.publish_condition_event("lice_rising", "warning", "", "CI: lus B", "ci-u208-b")
	await t.get_tree().create_timer(0.3).timeout
	_check(quiet == 0 and routed == 1 and tap.toasts_containing("CI: lus B") == 1,
		"settings: gameplay.notifications_enabled off keeps a warning to the inbox (no toast), on shows it")
	t._untap_events(tap)

	# auto_slow_deadlines: the clock's deadline rule follows at once.
	_apply_settings({"gameplay": {"auto_slow_deadlines": false}})
	var slow_off := not bool(t._sim.get_clock_state().get("auto_slow", true))
	_apply_settings({"gameplay": {"auto_slow_deadlines": true}})
	_check(slow_off and bool(t._sim.get_clock_state().get("auto_slow", false)),
		"settings: gameplay.auto_slow_deadlines switches the clock's deadline rule off and on")
	scene.queue_free()
	await t.get_tree().process_frame

	# default_speed and auto_pause_critical: a player's new game.
	_apply_settings({"gameplay": {"default_speed": 10, "auto_pause_critical": false}})
	_check(t._sim.start_new_game("Player", 10_000_000, SCENARIO), "settings: a player's game starts on its briefing")
	scene = (load("res://scenes/GameScene.tscn") as PackedScene).instantiate()
	t.add_child(scene)
	for i in 4:
		await t.get_tree().process_frame
	var briefing: Control = scene.get_node("%BriefingCard")
	card = scene.get_node("%CriticalEventCard")
	briefing.start_button.pressed.emit()
	await t.get_tree().process_frame
	_check(int(t._sim.get_time_speed()) == 10 and briefing.start_button.tooltip_text.contains("10×"),
		"settings: gameplay.default_speed 10: the briefing starts the clock at 10×")
	await t._drain_cards(card)
	t._sim.set_paused(false)
	t._sim.publish_condition_event("oxygen_critical", "critical", "", "CI: oksygen C", "ci-u208-c")
	var held_off: bool = t._sim.is_clock_held("critical_event")
	await t._drain_cards(card)
	_apply_settings({"gameplay": {"auto_pause_critical": true}})
	t._sim.set_paused(false)
	t._sim.publish_condition_event("nets_critical", "critical", "", "CI: nøter D", "ci-u208-d")
	_check(not held_off and t._sim.is_clock_held("critical_event"),
		"settings: gameplay.auto_pause_critical off lets the card run the clock, on holds it")
	await t._drain_cards(card)
	t._sim.set_paused(true)
	scene.queue_free()
	await t.get_tree().process_frame

	# tutorial_enabled: the first season's gate (TutorialState reads it each second).
	_apply_settings({"gameplay": {"tutorial_enabled": true}})
	_check(t._sim.start_new_game("Player", 10_000_000, "forste_sesong"), "settings: the first season starts")
	t._sim.set_paused(true)
	var tut: Node = t.get_node("/root/TutorialState")
	tut._read_guide_pref()
	tut._refresh()
	var gated := ""
	for panel_id in tut._unlock_step:
		if not tut.is_panel_unlocked(panel_id):
			gated = panel_id
			break
	_apply_settings({"gameplay": {"tutorial_enabled": false}})
	await t.get_tree().create_timer(TutorialState.GUIDE_PREF_INTERVAL_S + 0.2).timeout
	_check(gated != "" and tut.is_panel_unlocked(gated),
		"settings: gameplay.tutorial_enabled off lifts the first season's gate on '%s'" % gated)
	_apply_settings({})


# ── U2-08: the menu applies what its widgets say ──────────────────────────────

func _the_settings_menu_applies_through_its_widgets() -> void:
	_apply_settings({})
	var menu: Control = (load("res://scenes/ui/SettingsMenu.tscn") as PackedScene).instantiate()
	t.add_child(menu)
	menu.show()
	await t.get_tree().process_frame
	var lang: OptionButton = menu.get_node("%LanguageOption")
	var title: Label = menu.get_node("%TitleLabel")
	_check(lang.item_count == 2 and lang.get_item_text(0) == "Norsk (bokmål)" and lang.get_item_text(1) == "English",
		"settings menu: the language picker offers Norsk (bokmål) and English")
	lang.select(1)
	lang.item_selected.emit(1)
	await t.get_tree().process_frame
	_check(title.text == "Settings" and menu._tab_container.get_tab_title(0) == "Audio",
		"settings menu: picking English redraws the menu in English ('%s', '%s')" % [title.text, menu._tab_container.get_tab_title(0)])
	var hc: CheckBox = menu.get_node("%HighContrastCheck")
	hc.button_pressed = true
	_check(LakskongenTheme.is_high_contrast(), "settings menu: ticking high contrast applies it at once")
	(menu.get_node("%AutosaveOption") as OptionButton).select(1)
	(menu.get_node("%ApplyButton") as Button).pressed.emit()
	var saved := SettingsStore.load_raw()
	_check(str(saved.get("general", {}).get("language", "")) == "en" and bool(saved.get("accessibility", {}).get("high_contrast", false))
		and int(saved.get("gameplay", {}).get("autosave_minutes", -1)) == 2
		and int(t.get_node("/root/SaveLoadManager").AutoSaveIntervalMinutes) == 2,
		"settings menu: Bruk writes the choices and applies them (language, contrast, autosave 2 min)")
	(menu.get_node("%ResetButton") as Button).pressed.emit()
	await t.get_tree().process_frame
	_check(not LakskongenTheme.is_high_contrast() and title.text == "Innstillinger",
		"settings menu: Tilbakestill brings back the defaults (Norwegian, normal contrast)")
	(menu.get_node("%ApplyButton") as Button).pressed.emit()
	menu.queue_free()
	await t.get_tree().process_frame
	TranslationServer.set_locale("no")
	_apply_settings({})


# ── U2-08 acceptance 2: save slots show the scenario, an ISO time, a picture ──

func _save_slots_show_scenario_time_and_picture() -> void:
	var thumbs: Node = t.get_node("/root/SaveThumbnails")
	var mgr: Node = t.get_node("/root/SaveLoadManager")
	var pic := Image.create(SaveThumbnails.SIZE.x * 2, SaveThumbnails.SIZE.y * 2, false, Image.FORMAT_RGB8)
	pic.fill(Color("#0E2A33"))
	pic.fill_rect(Rect2i(0, 0, pic.get_width(), pic.get_height() / 3), Color("#8CC7DA"))
	thumbs.image_source = func() -> Image: return pic
	t._clear_test_saves()
	_check(t._sim.start_qa_scenario(SCENARIO, SEED), "save slots: a scenario game to save")
	t._sim.set_paused(true)
	var written_before: int = thumbs.written
	var before := int(Time.get_unix_time_from_system())
	# The local wall clock's minute as ISO text, read independently of the menu.
	var local_minutes := [Time.get_datetime_string_from_system(false, true).substr(0, 16)]
	_check(t._sim.save_game("ci_slot_picture"), "save slots: the game saves")
	local_minutes.append(Time.get_datetime_string_from_system(false, true).substr(0, 16))
	var guard := 0
	while thumbs.written == written_before and guard < 60:
		await t.get_tree().process_frame
		guard += 1
	var entry: Dictionary = t._find_save("ci_slot_picture")
	var thumb := str(entry.get("thumbnail", ""))
	var img := Image.load_from_file(ProjectSettings.globalize_path(thumb)) if thumb != "" else null
	_check(thumb == str(mgr.ThumbnailPathFor(str(entry.get("file", "")))) and img != null and img.get_size() == SaveThumbnails.SIZE,
		"save slots: the save writes its picture beside it (%s, %s)" % [thumb.get_file(), str(img.get_size() if img else Vector2i.ZERO)])
	_check(str(entry.get("scenario", "")) == SCENARIO and absi(int(entry.get("time_unix", 0)) - before) <= 5,
		"save slots: the list knows the scenario and the save time (%s)" % str(entry.get("scenario", "")))
	thumbs.image_source = Callable()
	_check(t._sim.save_game("ci_slot_no_picture"), "save slots: a second save (headless: no picture)")
	await t.get_tree().process_frame

	var menu: Control = (load("res://scenes/ui/SaveLoadMenu.tscn") as PackedScene).instantiate()
	t.add_child(menu)
	menu.show_load()
	await t.get_tree().process_frame
	var slot: Control = menu.get_node("%SlotsContainer").find_child("Slot_ci_slot_picture", false, false)
	var pictured: Control = slot.find_child("Thumbnail", true, false) if slot else null
	var scenario_lbl: Label = slot.find_child("Scenario", true, false) if slot else null
	var details: Label = slot.find_child("Details", true, false) if slot else null
	var iso := RegEx.create_from_string("^\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}  ·  ")
	_check(pictured is TextureRect and (pictured as TextureRect).texture != null
		and (pictured as TextureRect).texture.get_size() == Vector2(SaveThumbnails.SIZE),
		"save slots: the slot shows the save's picture")
	var scenario_name := str((t.get_node("/root/ScenarioManager").get_scenario(SCENARIO) as Dictionary).get("name", "?"))
	_check(scenario_lbl != null and scenario_lbl.text == scenario_name and menu.scenario_title("") == t.tr("UI_SAVELOAD_NO_SCENARIO")
		and menu.scenario_title("forste_sesong") == t.tr("SCENARIO_FORSTE_SESONG_NAME"),
		"save slots: the slot names the scenario ('%s')" % (scenario_lbl.text if scenario_lbl else ""))
	_check(details != null and iso.search(details.text) != null and details.text.substr(0, 16) in local_minutes,
		"save slots: the slot shows the save's local ISO date and time first ('%s')" % (details.text if details else ""))
	var bare: Control = menu.get_node("%SlotsContainer").find_child("Slot_ci_slot_no_picture", false, false)
	var tile: Control = bare.find_child("Thumbnail", true, false) if bare else null
	_check(tile != null and bool(tile.get_meta(&"empty", false)) and tile.custom_minimum_size == menu.THUMB_SIZE,
		"save slots: a save without a picture keeps the same tile, empty")
	_check(t._sim.delete_save(str(entry.get("file", ""))) and not FileAccess.file_exists(thumb),
		"save slots: deleting a save deletes its picture")
	menu.queue_free()
	t._clear_test_saves()
	await t.get_tree().process_frame


# ── Au2-01: every Button clicks, through one hook ────────────────────────────

func _every_button_clicks() -> void:
	var audio: Node = t.get_node("/root/AudioController")
	var scene: Node = await t._spawn_game_scene()
	var menu: Control = await t._spawn_main_menu()
	var buttons: Array[BaseButton] = []
	var stack: Array[Node] = [t.get_tree().root]
	while not stack.is_empty():
		var n: Node = stack.pop_back()
		if n is BaseButton:
			buttons.append(n)
		stack.append_array(n.get_children())
	var silent := buttons.filter(func(b: BaseButton) -> bool: return not audio.clicks(b))
	_check(buttons.size() > 50 and silent.is_empty(),
		"audio: every one of %d buttons (game, main menu, settings, saves) clicks (silent: %s)" % [buttons.size(),
			str(silent.slice(0, 5).map(func(b: BaseButton) -> String: return str(b.get_path())))])
	var bus := AudioServer.get_bus_index(audio.click_bus())
	_check(audio.click_bus() == "UI" and AudioServer.get_bus_send(bus) == "SFX",
		"audio: the click plays on the UI bus, which the effects slider governs (sends to SFX)")
	var late := Button.new()
	late.text = "CI"
	late.position = Vector2(40, 40)
	late.size = Vector2(120, 40)
	var layer := CanvasLayer.new()
	layer.layer = 120
	layer.add_child(late)
	t.add_child(layer)
	await t.get_tree().process_frame
	var before: int = audio.ui_clicks_played
	await t._click_control(late)
	await t.get_tree().create_timer(0.1).timeout
	var player: AudioStreamPlayer = audio.get_node("UiClick")
	_check(audio.clicks(late) and audio.ui_clicks_played == before + 1 and player.stream != null
		and player.stream.resource_path == MusicLibrary.ui_sfx("button_click"),
		"audio: a button made later clicks too, and a mouse click on it plays the click once (%d)" % (audio.ui_clicks_played - before))
	layer.queue_free()
	menu.queue_free()
	scene.queue_free()
	await t.get_tree().process_frame


# ── Au2-01: the verbs play their cue, once ────────────────────────────────────

func _cue_on_pool(audio: Node, cue: String) -> bool:
	var path := MusicLibrary.verb_sfx(cue)
	for p: AudioStreamPlayer in audio._sfx_pool:
		if p.stream != null and p.stream.resource_path == path:
			return true
	return false


func _verbs_play_their_cue_once() -> void:
	var audio: Node = t.get_node("/root/AudioController")
	var scene: Node = await t._spawn_po3_scene(SEED)
	var card: Control = scene.get_node("%CriticalEventCard")
	await t._drain_cards(card)
	var site := str((t._sim.sea_sites[0] as Dictionary).get("id", ""))
	var counts: Dictionary = audio.verb_cues_played.duplicate()
	var plays := func(cue: String) -> int: return int(audio.verb_cues_played.get(cue, 0)) - int(counts.get(cue, 0))

	var plan: Dictionary = t._sim.set_feed_plan(site, 90.0, "standard", 30)
	_check(bool(plan.get("success", false)) and plays.call("feed_plan") == 1 and _cue_on_pool(audio, "feed_plan"),
		"audio: a feeding plan set plays its cue once")
	audio._verb_last_ms.clear()
	var deloused: bool = t._sim.delouse_pens(site, "mechanical")
	_check(plays.call("delouse") == (1 if deloused else 0) and (not deloused or _cue_on_pool(audio, "delouse")),
		"audio: a delousing plays its cue once when it starts (%s)" % ("started" if deloused else "refused: no cue"))
	# Stocking arrives through the facade and the bridge: one cue.
	t._sim.pen_action_completed.emit("stock_smolts", site, true)
	t.get_node("/root/GameBridge").emit_signal("FarmActionCompleted", "stock_smolts", site, true)
	t._sim.pen_action_completed.emit("stock_smolts", site, false)
	_check(plays.call("stock") == 1 and _cue_on_pool(audio, "stock"),
		"audio: stocking heard through both signals plays its cue once, a failed one none (%d)" % plays.call("stock"))
	t._sim.publish_condition_event("storm_forecast", "warning", "", "CI: storm E", "ci-au201-e")
	_check(plays.call("storm") == 1 and _cue_on_pool(audio, "storm"), "audio: a storm warning on the event bus plays the thunder")
	t._sim.publish_condition_event("licence_auction_won", "info", "", "CI: lisens F", "ci-au201-f")
	_check(plays.call("auction") == 1 and _cue_on_pool(audio, "auction"), "audio: an auction's verdict plays the gavel")
	var stinger := MusicLibrary.verb_sfx("harvest")
	t.get_node("/root/ConsequenceDirector")._on_farm_action_completed("harvest", site, true)
	var stingers: int = audio._sfx_pool.filter(func(p: AudioStreamPlayer) -> bool:
		return p.stream != null and p.stream.resource_path == stinger).size()
	_check(stinger == MusicLibrary.alert_sfx("harvest") and stingers == 1 and VerbSounds.for_action("harvest", true) == "",
		"audio: a harvest plays its stinger once (the ConsequenceDirector's, not doubled)")
	_check(VerbSounds.for_event({"type": "storm_forecast", "day": 10}, 13) == "" and VerbSounds.for_event({"type": "storm_forecast", "day": 12}, 13) == "storm"
		and VerbSounds.for_event({"type": "lice_rising", "day": 13}, 13) == "",
		"audio: a stale event (3 days or older) and an event without a verb play nothing")
	await t._drain_cards(card)
	scene.queue_free()
	await t.get_tree().process_frame


# ── Au2-01: the ambience bed ──────────────────────────────────────────────────

func _the_ambience_bed_follows_the_weather() -> void:
	var calm := AmbienceMixer.gains_for({"wind_ms": 4.0, "rain": 0.0, "wave_m": 0.3, "camera_height_m": 80.0})
	var storm := AmbienceMixer.gains_for({"wind_ms": 16.0, "rain": 7.0, "wave_m": 3.0, "camera_height_m": 80.0})
	var under := AmbienceMixer.gains_for({"underwater": true})
	_check(calm["water_lap"] > 0.8 and calm["gulls"] > 0.5 and calm["storm"] == 0.0 and calm["wind_strong"] == 0.0
		and calm["rain_light"] == 0.0 and calm["wind_light"] > 0.5,
		"ambience: a calm day is lapping water, a breeze and gulls")
	_check(storm["storm"] == 1.0 and storm["water_lap"] == 0.0 and storm["gulls"] == 0.0
		and absf(storm["wind_strong"] - 7.0 / 9.0) < 0.01 and absf(storm["rain_heavy"] - 4.5 / 5.1) < 0.01,
		"ambience: a storm is breaking seas, a gale and heavy rain, no gulls")
	_check(under["underwater"] == 1.0 and under.values().reduce(func(a: float, b: float) -> float: return a + b, 0.0) == 1.0,
		"ambience: under the surface only the underwater layer plays")
	var high := AmbienceMixer.gains_for({"wind_ms": 4.0, "camera_height_m": 900.0})
	_check(high["water_lap"] < calm["water_lap"] and high["gulls"] < calm["gulls"],
		"ambience: high above the water the laps and the gulls fall back")

	var audio: Node = t.get_node("/root/AudioController")
	var mixer: AmbienceMixer = audio.ambience()
	var scene: Node = await t._spawn_po3_scene(SEED)
	var guard := 0
	while guard < 300 and AmbienceMixer.LAYERS.keys().any(func(l: String) -> bool: return mixer.player_of(l).stream == null):
		await t.get_tree().process_frame
		guard += 1
	var short := AmbienceMixer.LAYERS.keys().filter(func(l: String) -> bool:
		var p := mixer.player_of(l)
		return p.stream == null or not p.playing or p.stream.get_length() < 30.0)
	_check(short.is_empty(), "ambience: all %d layers play 30 s+ loops (not: %s)" % [AmbienceMixer.LAYERS.size(), str(short)])
	await t.get_tree().process_frame
	var director: EnvironmentDirector = scene.get_node("FjordEnvironment/EnvironmentDirector")
	var weather: WeatherController = director.weather_system
	mixer.sense(t.get_tree(), t._sim)
	var seen := mixer.last_inputs
	var cam := t.get_viewport().get_camera_3d()
	_check(weather != null and is_equal_approx(float(seen.get("wind_ms", -1)), weather.get_wind_speed_ms())
		and is_equal_approx(float(seen.get("rain", -1)), weather.get_rain())
		and cam != null and is_equal_approx(float(seen.get("camera_height_m", -1)), cam.global_position.y),
		"ambience: the bed reads the weather and the camera on screen (wind %.1f m/s, rain %.1f mm/h, camera %.0f m)" % [
			float(seen.get("wind_ms", -1)), float(seen.get("rain", -1)), float(seen.get("camera_height_m", -1))])
	mixer.settle()
	var want := AmbienceMixer.gains_for(seen)
	_check(AmbienceMixer.LAYERS.keys().all(func(l: String) -> bool: return absf(mixer.gain_of(l) - float(want[l])) < 0.001),
		"ambience: each layer's gain is the rule's for that weather")
	var barges: FeedBargeVisualManager = scene.get_node("FjordEnvironment/FeedBargeVisualManager")
	t._sim.advance_days(1)   # a state update: the barges sync to the stocked sites
	guard = 0
	while barges.active_site_ids().is_empty() and guard < 240:
		await t.get_tree().process_frame
		guard += 1
	var engines := 0
	for id in barges.active_site_ids():
		var emitter := barges.barge_of(id).get_node_or_null(audio.EMITTER_NAME) as AudioStreamPlayer3D
		if emitter != null and str(emitter.get_meta(&"loop_key", "")) == "feed_barge" and emitter.bus == "SFX":
			engines += 1
	_check(engines >= 1 and engines == barges.active_site_ids().size(),
		"ambience: every feed barge carries its engine loop as a 3D emitter (%d of %d)" % [engines, barges.active_site_ids().size()])
	scene.queue_free()
	await t.get_tree().process_frame
