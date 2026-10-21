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
	# The U2-08/Au2-01 review's findings, each pinned.
	await _save_and_leave_waits_for_the_picture()
	await _every_game_starts_its_own_autosave_interval()
	await _the_end_screen_offers_only_this_games_autosave()
	await _a_volume_change_leaves_the_window_alone()
	_a_run_by_path_never_reads_the_players_settings()
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
	var normal_glass := _hud_glass_backgrounds(theme)
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
	_high_contrast_reaches_the_hud_glass(theme, chart, normal_glass)
	_apply_settings({"accessibility": {"high_contrast": false}})
	_check(not LakskongenTheme.is_high_contrast() and label.get_theme_color("font_color") == Color("#E8EEF0")
		and (theme.get_stylebox("focus", "Button") as StyleBoxFlat).border_width_left == 2,
		"settings: turning it off restores the Kyst & Stål theme exactly")
	# U2-08 review (M2): off restores the Chart paper themes and the HUD glass too.
	var chart_focus := chart.get_stylebox("focus", "Button") as StyleBoxFlat
	_check(chart.get_color("base", "Palette") == LakskongenTheme.BG and chart.get_color("text", "Palette") == LakskongenTheme.TEXT_PRIMARY
		and chart_focus != null and chart_focus.border_width_left == 2 and _hud_glass_backgrounds(theme) == normal_glass,
		"settings: turning high contrast off restores the Chart paper (base, text, a 2 px focus ring) and the HUD glass")
	label.queue_free()


## The HUD glass type variations (Ink on every surface): the top bar and the
## tab rail (Rail), glass panels, chips over the world, every toast, tooltips.
const HUD_GLASS: Array[String] = ["Rail", "Glass", "Chip", "Toast", "ToastMuted", "ToastOk", "ToastWarn",
	"ToastCrit", "ToastInfo", "ToastAccent", "TooltipPanel"]


func _hud_glass_backgrounds(theme: Theme) -> Array:
	return HUD_GLASS.map(func(v: String) -> Color:
		var sb := theme.get_stylebox("panel", v) as StyleBoxFlat
		return sb.bg_color if sb != null else Color.MAGENTA)


## U2-08 review: Glass, Rail, Chip, the toasts and the tooltip were built from
## the normal Ink palette in both high-contrast themes (0.9-alpha glass, a
## 1.5:1 hairline), so the HUD never changed.
func _high_contrast_reaches_the_hud_glass(theme: Theme, chart: Theme, normal_glass: Array) -> void:
	var base := theme.get_color("base", "Palette")
	var faults: Array[String] = []
	for surf: Theme in [theme, chart]:
		for v in HUD_GLASS:
			var sb := surf.get_stylebox("panel", v) as StyleBoxFlat
			if sb == null or sb.bg_color.a < 1.0 or sb.border_color.a < 1.0 or _contrast(sb.border_color, base) < 3.0:
				faults.append("%s/%s" % [str(surf.get_meta(&"surface", "?")), v])
	_check(faults.is_empty() and _hud_glass_backgrounds(theme) != normal_glass,
		"settings: high contrast reaches the HUD glass: rail, glass, chips, every toast and the tooltip opaque with an edge >= 3:1, on both surfaces (faults: %s)" % str(faults))
	var worst := 99.0
	for surf: Theme in [theme, chart]:
		var fg := surf.get_color("font_color", "PrimaryButton")
		for state in ["normal", "hover", "pressed"]:
			worst = minf(worst, _contrast(fg, (surf.get_stylebox(state, "PrimaryButton") as StyleBoxFlat).bg_color))
	_check(worst >= 7.0, "settings: high contrast: a primary button's text is at least 7:1 on every salmon state (worst %.1f:1)" % worst)


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
	# The menus around it redraw too when the language changes.
	var main: Control = await t._spawn_main_menu()
	var pause: Control = (load("res://scenes/PauseMenu.tscn") as PackedScene).instantiate()
	t.add_child(pause)
	await t.get_tree().process_frame
	_apply_settings({"general": {"language": "en"}})
	await t.get_tree().process_frame
	var en_ok: bool = main.btn_settings.text == "Settings" and pause.btn_resume.text == "Resume"
	_apply_settings({"general": {"language": "nb"}})
	await t.get_tree().process_frame
	_check(en_ok and main.btn_settings.text == "Innstillinger" and pause.btn_resume.text == "Fortsett",
		"settings: a language change redraws the main menu and the pause menu at once")
	pause.queue_free()
	main.queue_free()
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
	# U2-08 review: the local time comes from C# by the zone's rules for the
	# save's own instant (today's offset would shift saves across a DST change).
	# (A time_local that today's offset could not produce from its time_unix.)
	var fixed := {"time_local": "2026-07-01 12:34", "time_unix": 0}
	_check(details != null and str(entry.get("time_local", "")) == details.text.substr(0, 16)
		and menu.slot_time(fixed) == "2026-07-01 12:34"
		and menu.slot_time({"time_unix": 1_782_900_000}) == menu.iso_local(1_782_900_000)
		and menu.slot_time({}) == "",
		"save slots: the slot's time is the save list's time_local ('%s'), today's offset only without it" % str(entry.get("time_local", "")))
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
	# U2-08 review: with "Varsler" off a warning has no toast and no sound, the
	# storm's thunder included (the event still reaches the inbox).
	var tap: RefCounted = t._tap_events()
	_apply_settings({"gameplay": {"notifications_enabled": false}})
	audio._verb_last_ms.clear()
	t._sim.publish_condition_event("storm_forecast", "warning", "", "CI: storm G", "ci-u208-g")
	var quiet_storm: int = plays.call("storm")
	var routed_storm: int = tap.channel_count("toast", "storm_forecast")
	_apply_settings({})
	t._untap_events(tap)
	_check(quiet_storm == 1 and routed_storm == 1
		and VerbSounds.for_event({"type": "storm_forecast", "severity": "warning", "day": 13}, 13, false) == ""
		and VerbSounds.for_event({"type": "storm_damage", "severity": "critical", "day": 13}, 13, false) == "storm"
		and VerbSounds.for_event({"type": "licence_auction_won", "severity": "info", "day": 13}, 13, false) == "auction",
		"audio: with notifications off a storm warning reaches the inbox without the thunder; critical and info cues still play")
	audio._verb_last_ms.clear()
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
	# U2-08 review (M1): the sea state on screen and the camera under the
	# surface reach the bed too (the storm-sea layer and the underwater one).
	if weather != null:
		var blend_s := weather.transition_duration
		weather.transition_duration = 0.0
		weather.apply_sky_context({"weather_state": "Storm", "wind_ms": 18.0, "wave_m": 3.2, "precip_mm_h": 6.0, "cloud_cover": 1.0})
		mixer.sense(t.get_tree(), t._sim)
		mixer.settle()
		var rough := mixer.last_inputs
		_check(is_equal_approx(weather.get_wave_height_m(), 3.2) and is_equal_approx(float(rough.get("wave_m", -1)), 3.2)
			and mixer.gain_of("storm") == 1.0 and mixer.gain_of("water_lap") == 0.0 and not bool(rough.get("underwater", true)),
			"ambience: the bed reads the sea on screen: a 3.2 m storm sea plays the breaking seas, not the laps (wave %.2f m, storm %.2f)" % [
				float(rough.get("wave_m", -1)), mixer.gain_of("storm")])
		weather.transition_duration = blend_s
	else:
		_check(false, "ambience: the fjord has a weather controller (sea-state check)")
	var fjord_cam := cam as FjordCamera
	if fjord_cam != null:
		# wave4b int (gfx-polish-2 review): the bed follows the camera itself
		# under the surface, not the view's name: the UNDERWATER view hovers
		# in the air over its pen before it dives, and a framing takes the
		# camera out while the name may still say otherwise.
		var was_processing := fjord_cam.is_processing()
		var at := fjord_cam.global_position
		var zoom_before := fjord_cam.current_zoom
		fjord_cam.set_process(false)
		fjord_cam.global_position = Vector3(at.x, -5.0, at.z)
		mixer.sense(t.get_tree(), t._sim)
		mixer.settle()
		var under_ok := bool(mixer.last_inputs.get("underwater", false)) and is_equal_approx(mixer.gain_of("underwater"), 1.0) \
			and AmbienceMixer.LAYERS.keys().all(func(l: String) -> bool: return l == "underwater" or mixer.gain_of(l) == 0.0)
		fjord_cam.global_position = Vector3(at.x, 7.0, at.z)
		fjord_cam.current_zoom = FjordCamera.ZoomLevel.UNDERWATER
		mixer.sense(t.get_tree(), t._sim)
		mixer.settle()
		var hover_ok := not bool(mixer.last_inputs.get("underwater", true)) and mixer.gain_of("underwater") == 0.0
		fjord_cam.current_zoom = zoom_before
		fjord_cam.global_position = at
		fjord_cam.set_process(was_processing)
		mixer.sense(t.get_tree(), t._sim)
		mixer.settle()
		_check(under_ok and hover_ok and mixer.gain_of("underwater") == 0.0,
			"ambience: with the camera under the surface only the underwater bed plays; none while the UNDERWATER view hovers in the air, and it stops on the way up")
	else:
		_check(false, "ambience: the game camera is a FjordCamera (underwater check)")
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


# ── The U2-08/Au2-01 review ───────────────────────────────────────────────────

## "Lagre og gå til hovedmenyen" / "Lagre og avslutt": the save's picture is
## of the game's world, so the menu waits for it before the scene changes or
## the game quits (it used to change at once: the slot showed the main menu's
## fjord, or quitting left no picture). A picture whose world left the tree is
## given up, never written.
func _save_and_leave_waits_for_the_picture() -> void:
	var thumbs: Node = t.get_node("/root/SaveThumbnails")
	var pic := Image.create(SaveThumbnails.SIZE.x, SaveThumbnails.SIZE.y, false, Image.FORMAT_RGB8)
	pic.fill(Color("#0E2A33"))
	thumbs.image_source = func() -> Image: return pic
	t._clear_test_saves()
	var scene: Node = await t._spawn_game_scene()
	var pause: Control = scene.get_node("MenuLayer/PauseMenu")
	var at_leave := {"menu": -1, "quit": -1}
	pause.scene_change_override = func(_path: String) -> void: at_leave["menu"] = thumbs.written
	pause.quit_override = func() -> void: at_leave["quit"] = thumbs.written
	var pictures: Array[String] = []
	var on_picture := func(path: String) -> void: pictures.append(path)
	thumbs.thumbnail_saved.connect(on_picture)
	var before: int = thumbs.written
	pause.show_menu()
	await t.get_tree().process_frame
	pause.btn_main_menu.pressed.emit()
	pause.guard_save.pressed.emit()
	var guard := 0
	while int(at_leave["menu"]) < 0 and guard < 120:
		await t.get_tree().process_frame
		guard += 1
	var menu_pic: String = pictures.back() if not pictures.is_empty() else ""
	_check(int(at_leave["menu"]) == before + 1 and menu_pic.get_file().begins_with("quicksave") and FileAccess.file_exists(menu_pic),
		"save and leave: the quick save's picture is written before the main menu replaces the game (%d of 1 at the change)" % (int(at_leave["menu"]) - before))
	await thumbs.wait_until_written()
	before = thumbs.written
	pause.show_menu()
	await t.get_tree().process_frame
	pause.btn_quit.pressed.emit()
	pause.guard_save.pressed.emit()
	guard = 0
	while int(at_leave["quit"]) < 0 and guard < 120:
		await t.get_tree().process_frame
		guard += 1
	var quit_pic: String = pictures.back() if not pictures.is_empty() else ""
	_check(int(at_leave["quit"]) == before + 1 and quit_pic.get_file().begins_with("quicksave") and FileAccess.file_exists(quit_pic) and not thumbs.is_busy(),
		"save and quit: the quick save's picture is written before the game quits (%d of 1 at the quit)" % (int(at_leave["quit"]) - before))
	thumbs.thumbnail_saved.disconnect(on_picture)
	# A camera that leaves the tree before its picture is read: nothing written.
	var gone := Camera3D.new()
	t.add_child(gone)
	var gone_id := gone.get_instance_id()
	gone.free()
	var orphan: String = t.TEST_ROOT + "ci_orphan.thumb.png"
	DirAccess.remove_absolute(ProjectSettings.globalize_path(orphan))
	var given_up: int = thumbs.abandoned
	var wrote: bool = await thumbs.write_for(orphan, gone_id)
	_check(not wrote and thumbs.abandoned == given_up + 1 and not FileAccess.file_exists(orphan),
		"save pictures: a picture whose world left the tree is given up, never written (wrote %s, given up %d, file %s)" % [
			str(wrote), thumbs.abandoned - given_up, str(FileAccess.file_exists(orphan))])
	thumbs.image_source = Callable()
	pause.hide_menu()
	scene.queue_free()
	t._clear_test_saves()
	await t.get_tree().process_frame


## Every game starts its own autosave interval (GameBridge.GameStarted): a new
## game, a scenario, a load. The engine is one object for every game, so the
## old engine-identity rule restarted it only on the first tick after launch.
func _every_game_starts_its_own_autosave_interval() -> void:
	var mgr: Node = t.get_node("/root/SaveLoadManager")
	_apply_settings({})
	mgr.ClockOverrideMs = CLOCK_T0
	t._start_paused_game()
	t._sim.advance_days(1)
	mgr.ClockOverrideMs = CLOCK_T0 + 200_000
	var mid := int(mgr.AutoSaveDueInMs())
	_check(t._sim.start_qa_scenario("isa_utbrudd", 7), "autosave: a second game starts (isa_utbrudd)")
	t._sim.set_paused(true)
	var fresh := int(mgr.AutoSaveDueInMs())
	_check(mid == 100_000 and fresh == 5 * 60_000,
		"autosave: a new game starts its own 5-minute interval (due in %d ms, was %d ms into the old game's)" % [fresh, mid])
	mgr.ClockOverrideMs = -1
	mgr.RestartAutoSaveInterval()


## The end screen's "Last inn autolagring" is this game's own autosave (same
## scenario, same seed) or none: with the real-time interval a short game may
## have no autosave, and another game's must not stand in for it.
func _the_end_screen_offers_only_this_games_autosave() -> void:
	var mgr: Node = t.get_node("/root/SaveLoadManager")
	t._clear_test_saves()
	_check(t._sim.start_qa_scenario(SCENARIO, SEED), "end screen autosave: game A (%s, seed %d)" % [SCENARIO, SEED])
	t._sim.set_paused(true)
	t._sim.advance_days(2)
	_check(bool(mgr.AutoSave()), "end screen autosave: game A's autosave")
	var a_entry: Dictionary = t._find_save("autosave_latest")
	var a_report: Dictionary = t._sim.get_scenario_end_report()
	_check(str(a_entry.get("scenario", "")) == SCENARIO and int(a_entry.get("seed", 0)) == SEED and int(a_report.get("game_seed", 0)) == SEED,
		"end screen autosave: the save list and the end report both name the game (scenario %s, seed %d / %d)" % [
			str(a_entry.get("scenario", "")), int(a_entry.get("seed", 0)), int(a_report.get("game_seed", 0))])
	_check(t._sim.start_qa_scenario("isa_utbrudd", 7), "end screen autosave: game B (isa_utbrudd, seed 7)")
	t._sim.set_paused(true)
	t._sim.advance_days(5)
	var screen: Node = load("res://scripts/ui/endscreen/ScenarioEndScreen.gd").new()
	screen._sim = t._sim
	screen._verdict = "lost"
	var b_report: Dictionary = t._sim.get_scenario_end_report()
	b_report["end_day"] = int(t._sim.current_day) + 1
	screen._report = b_report
	var offered_b: String = screen._find_autosave()
	a_report["end_day"] = 100
	screen._report = a_report
	var offered_a: String = screen._find_autosave()
	var same_scenario_other_seed: bool = screen.is_same_game(a_entry, {"scenario_id": SCENARIO, "game_seed": SEED + 1})
	screen.free()
	_check(offered_b == "" and offered_a == str(a_entry.get("file", "")) and not same_scenario_other_seed,
		"end screen autosave: a lost game B is offered none of game A's autosaves, A its own ('%s' / '%s')" % [offered_b, offered_a])
	t._sim.set_paused(true)
	t._clear_test_saves()


## Changing only a volume and closing the settings must not snap a window the
## player moved or resized back (the menu re-applies the window only when its
## settings change).
func _a_volume_change_leaves_the_window_alone() -> void:
	_apply_settings({})
	var port := _RecPort.new()
	_applier().display_port = port
	var menu: Control = (load("res://scenes/ui/SettingsMenu.tscn") as PackedScene).instantiate()
	t.add_child(menu)
	menu.show()
	await t.get_tree().process_frame
	(menu.get_node("%MusicSlider") as Range).value = 0.3
	(menu.get_node("%CloseButton") as Button).pressed.emit()
	var untouched := port.calls.is_empty() and is_equal_approx(float(SettingsStore.value("audio", "music")), 0.3)
	menu.show()
	await t.get_tree().process_frame
	var res: OptionButton = menu.get_node("%ResOption")
	res.select(DisplaySettings.RESOLUTIONS.find("1280x720"))
	(menu.get_node("%ApplyButton") as Button).pressed.emit()
	_check(untouched and port.last("size") == Vector2i(1280, 720) and port.has("center"),
		"settings menu: a volume change leaves the window alone; a new resolution applies it (%s)" % str(port.last("size")))
	_check(not DisplaySettings.window_changed({}, {"resolution": "1920x1080", "fullscreen": false, "vsync": true, "quality": "low"})
		and DisplaySettings.window_changed({}, {"vsync": false}),
		"settings: only the resolution, fullscreen and VSync count as a window change (not the quality)")
	menu.queue_free()
	await t.get_tree().process_frame
	_apply_settings({})


## A run launched by a scene path (this suite, the QA captures, the screenshot
## and perf harnesses) never reads the player's user://settings.json: with no
## override it uses a file of its own, reset at boot.
func _a_run_by_path_never_reads_the_players_settings() -> void:
	var own := SettingsStore.path_override
	SettingsStore.path_override = ""
	var by_path := SettingsStore.path()
	SettingsStore.path_override = own
	_check(SettingsStore.launched_by_path() and not SettingsApplier.launched_as_game()
		and by_path == SettingsStore.BY_PATH_PATH and by_path != SettingsStore.PATH,
		"settings: a run by scene path reads %s, never the player's %s" % [by_path, SettingsStore.PATH])
