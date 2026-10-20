extends Control

## SettingsMenu v3 — settings that apply (roadmap U2-08).
## Layout lives in scenes/ui/SettingsMenu.tscn; script is wiring only:
## @onready % refs, signal hookups, settings IO through SettingsStore, and
## SettingsApplier to make them take effect.
##
## What the player sees change, and when:
## - at once while the menu is open: the volumes, the quality tier, high
##   contrast and the language (the menu itself redraws in it);
## - on "Bruk" (and on closing with changes): everything is saved and applied,
##   the window too (resolution, fullscreen, VSync), the UI scale and the
##   autosave interval;
## - read where they act, whenever they act: the default speed (a new game's
##   briefing), the tutorial, pause at critical events, slow before deadlines,
##   warnings as toasts ("Varsler") and reduced motion.
## Every key in SettingsStore.DEFAULTS has a consumer; the UI flow test
## (_test_every_setting_has_an_observable_effect) proves each one.

signal settings_applied
signal close_requested

const SETTINGS_PATH := SettingsStore.PATH
## The schema (sections and defaults) is SettingsStore's.
const DEFAULT_SETTINGS := SettingsStore.DEFAULTS
const UI_SCALE_STEPS: Array[float] = [0.8, 0.9, 1.0, 1.1, 1.25]
## Graphics quality tiers (RenderQuality.NAMES), in menu order.
const QUALITY_TIERS: Array[String] = ["low", "medium", "high", "ultra"]
const QUALITY_KEYS: Array[String] = ["UI_SETTINGS_QUALITY_LOW", "UI_SETTINGS_QUALITY_MEDIUM", "UI_SETTINGS_QUALITY_HIGH", "UI_SETTINGS_QUALITY_ULTRA"]
const QUALITY_FALLBACK: Array[String] = ["Lav", "Middels", "Høy", "Ultra"]
## Autosave choices (real minutes; 0 is off) and their labels.
const AUTOSAVE_MINUTES: Array[int] = [0, 2, 5, 10, 30]
const AUTOSAVE_KEYS: Array[String] = ["UI_SETTINGS_AUTOSAVE_OFF", "UI_SETTINGS_AUTOSAVE_2MIN", "UI_SETTINGS_AUTOSAVE_5MIN", "UI_SETTINGS_AUTOSAVE_10MIN", "UI_SETTINGS_AUTOSAVE_30MIN"]
## Tab titles, in tab order (Lyd, Grafikk, Spill, Tilgjengelighet).
const TAB_KEYS: Array[String] = ["UI_SETTINGS_TAB_AUDIO", "UI_SETTINGS_TAB_GRAPHICS", "UI_SETTINGS_TAB_GAMEPLAY", "UI_SETTINGS_TAB_ACCESSIBILITY"]

var _settings: Dictionary = {}
var _dirty: bool = false

# UI refs (scene-declared)
@onready var _overlay: ColorRect = %Overlay
@onready var _panel: PanelContainer = %DialogPanel
@onready var _tab_container: TabContainer = %Tabs
@onready var _title_label: Label = %TitleLabel
@onready var _close_button: Button = %CloseButton
@onready var _master_slider: HSlider = %MasterSlider
@onready var _music_slider: HSlider = %MusicSlider
@onready var _ambient_slider: HSlider = %AmbientSlider
@onready var _sfx_slider: HSlider = %SfxSlider
@onready var _master_pct: Label = %MasterValue
@onready var _music_pct: Label = %MusicValue
@onready var _ambient_pct: Label = %AmbientValue
@onready var _sfx_pct: Label = %SfxValue
@onready var _res_option: OptionButton = %ResOption
@onready var _fs_check: CheckBox = %FullscreenCheck
@onready var _vsync_check: CheckBox = %VsyncCheck
@onready var _quality_cap: Label = %QualityCap
@onready var _quality_option: OptionButton = %QualityOption
@onready var _language_option: OptionButton = %LanguageOption
@onready var _autosave_option: OptionButton = %AutosaveOption
@onready var _tutorial_check: CheckBox = %TutorialCheck
@onready var _notifs_check: CheckBox = %NotifsCheck
@onready var _autopause_check: CheckBox = %AutoPauseCheck
## "Bremse ned før frister" (U2-07): read back through GameplayPrefs.
@onready var _autoslow_check: CheckBox = %AutoSlowCheck
## The default speed: a slider over the clock's four steps (1×, 3×, 10×, 30×).
@onready var _speed_slider: HSlider = %SpeedSlider
@onready var _speed_val: Label = %SpeedValue
@onready var _hc_check: CheckBox = %HighContrastCheck
## Redusert bevegelse (U1-09): read back through MotionPrefs.
@onready var _motion_check: CheckBox = %ReducedMotionCheck
@onready var _ui_scale_option: OptionButton = %UiScaleOption
@onready var _reset_button: Button = %ResetButton
@onready var _apply_button: Button = %ApplyButton

func _ready() -> void:
	_settings = _load_settings()
	_populate_options()
	_apply_localization()
	_connect_signals()
	_apply_to_widgets(_settings)
	# Opened again (from the other menu, or after a change elsewhere): show
	# what is saved now.
	visibility_changed.connect(func() -> void:
		if visible and not _dirty:
			refresh_from_store())
	hide()

func _notification(what: int) -> void:
	# The language changed (here or anywhere): redraw the text built in code.
	if what == NOTIFICATION_TRANSLATION_CHANGED and is_node_ready():
		_apply_localization()

func _apply_localization() -> void:
	_title_label.text = _tr_or("UI_SETTINGS_TITLE", "Innstillinger")
	_reset_button.text = _tr_or("UI_SETTINGS_RESET", "Tilbakestill")
	_apply_button.text = _tr_or("UI_SETTINGS_APPLY", "Bruk")
	if _quality_cap:
		_quality_cap.text = _tr_or("UI_SETTINGS_QUALITY_LABEL", "Grafikkvalitet")
	for i in mini(TAB_KEYS.size(), _tab_container.get_tab_count()):
		_tab_container.set_tab_title(i, _tr_or(TAB_KEYS[i], _tab_container.get_tab_title(i)))
	if _quality_option:
		for i in mini(QUALITY_TIERS.size(), _quality_option.item_count):
			_quality_option.set_item_text(i, _tr_or(QUALITY_KEYS[i], QUALITY_FALLBACK[i]))
	if _autosave_option:
		for i in mini(AUTOSAVE_KEYS.size(), _autosave_option.item_count):
			_autosave_option.set_item_text(i, _tr_or(AUTOSAVE_KEYS[i], str(AUTOSAVE_MINUTES[i])))

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _connect_signals() -> void:
	_close_button.pressed.connect(_on_close)
	_reset_button.pressed.connect(_on_reset)
	_apply_button.pressed.connect(_on_apply)
	_master_slider.value_changed.connect(func(v): _master_pct.text = "%d%%" % int(v * 100); _apply_audio_live())
	_music_slider.value_changed.connect(func(v): _music_pct.text = "%d%%" % int(v * 100); _apply_audio_live())
	_ambient_slider.value_changed.connect(func(v): _ambient_pct.text = "%d%%" % int(v * 100); _apply_audio_live())
	_sfx_slider.value_changed.connect(func(v): _sfx_pct.text = "%d%%" % int(v * 100); _apply_audio_live())
	# The default speed shows its step ("10×") as it moves; closing keeps it.
	if _speed_slider:
		_speed_slider.value_changed.connect(func(_v: float) -> void: _update_speed_label(); _dirty = true)
	for check: BaseButton in [_autoslow_check, _motion_check, _fs_check, _vsync_check, _tutorial_check, _notifs_check, _autopause_check]:
		if check:
			check.toggled.connect(func(_on: bool) -> void: _dirty = true)
	if _quality_option:
		_quality_option.item_selected.connect(func(_i): _applier_call("apply_quality", [_quality_name()]); _dirty = true)
	if _hc_check:
		_hc_check.toggled.connect(func(on: bool) -> void: _applier_call("apply_high_contrast", [on]); _dirty = true)
	if _language_option:
		_language_option.item_selected.connect(func(_i): _applier_call("apply_language", [_language_code()]); _dirty = true)
	for option: OptionButton in [_res_option, _autosave_option, _ui_scale_option]:
		if option:
			option.item_selected.connect(func(_i): _dirty = true)

func _populate_options() -> void:
	_res_option.clear()
	for r in DisplaySettings.RESOLUTIONS:
		_res_option.add_item(r)
	_autosave_option.clear()
	for i in AUTOSAVE_MINUTES.size():
		_autosave_option.add_item(_tr_or(AUTOSAVE_KEYS[i], str(AUTOSAVE_MINUTES[i])))
	if _ui_scale_option:
		_ui_scale_option.clear()
		for pct in ["80%", "90%", "100%", "110%", "125%"]:
			_ui_scale_option.add_item(pct)
	if _quality_option:
		_quality_option.clear()
		for i in QUALITY_TIERS.size():
			_quality_option.add_item(_tr_or(QUALITY_KEYS[i], QUALITY_FALLBACK[i]))
	if _language_option:
		_language_option.clear()
		# Each language by its own name, in every language.
		for code in LocalizationManager.SUPPORTED_LOCALES:
			_language_option.add_item(LocalizationManager.LOCALE_NAMES[code])
			_language_option.set_item_metadata(_language_option.item_count - 1, code)

# ── Settings IO ─────────────────────────────────────────────────────────
func _load_settings() -> Dictionary:
	return SettingsStore.load_all()

func _save_settings(data: Dictionary) -> void:
	SettingsStore.save_all(data)

func _collect_from_widgets() -> Dictionary:
	return {
		"audio": {
			"master": _master_slider.value if _master_slider else 1.0,
			"music": _music_slider.value if _music_slider else 0.7,
			"ambient": _ambient_slider.value if _ambient_slider else 0.5,
			"sfx": _sfx_slider.value if _sfx_slider else 0.8,
		},
		"graphics": {
			"resolution": _res_option.get_item_text(_res_option.selected) if _res_option else "1920x1080",
			"fullscreen": _fs_check.button_pressed if _fs_check else false,
			"vsync": _vsync_check.button_pressed if _vsync_check else true,
			"quality": _quality_name(),
		},
		"gameplay": {
			"autosave_minutes": AUTOSAVE_MINUTES[clampi(_autosave_option.selected, 0, AUTOSAVE_MINUTES.size() - 1)] if _autosave_option else 5,
			"tutorial_enabled": _tutorial_check.button_pressed if _tutorial_check else true,
			"notifications_enabled": _notifs_check.button_pressed if _notifs_check else true,
			"default_speed": _speed_from_slider(),
			"auto_pause_critical": _autopause_check.button_pressed if _autopause_check else true,
			"auto_slow_deadlines": _autoslow_check.button_pressed if _autoslow_check else true,
		},
		"accessibility": {
			"high_contrast": _hc_check.button_pressed if _hc_check else false,
			"ui_scale": _ui_scale(),
			"reduced_motion": _motion_check.button_pressed if _motion_check else false,
		},
		"general": {
			"language": _language_code(),
		},
	}

func _quality_name() -> String:
	if _quality_option == null or _quality_option.selected < 0:
		return "high"
	return QUALITY_TIERS[clampi(_quality_option.selected, 0, QUALITY_TIERS.size() - 1)]

func _language_code() -> String:
	if _language_option == null or _language_option.selected < 0:
		return LocalizationManager.SUPPORTED_LOCALES[0]
	return str(_language_option.get_item_metadata(_language_option.selected))

func _ui_scale() -> float:
	if _ui_scale_option == null:
		return 1.0
	return UI_SCALE_STEPS[clampi(_ui_scale_option.selected, 0, UI_SCALE_STEPS.size() - 1)]

func _apply_to_widgets(data: Dictionary) -> void:
	var audio = data.get("audio", {})
	if _master_slider: _master_slider.set_value_no_signal(audio.get("master", 1.0))
	if _music_slider: _music_slider.set_value_no_signal(audio.get("music", 0.7))
	if _ambient_slider: _ambient_slider.set_value_no_signal(audio.get("ambient", 0.5))
	if _sfx_slider: _sfx_slider.set_value_no_signal(audio.get("sfx", 0.8))
	_update_pct_labels()

	var gfx = data.get("graphics", {})
	if _res_option:
		var res_str = str(gfx.get("resolution", "1920x1080"))
		var res_idx := DisplaySettings.RESOLUTIONS.find(res_str)
		_res_option.selected = res_idx if res_idx >= 0 else DisplaySettings.RESOLUTIONS.find("1920x1080")
	if _fs_check: _fs_check.set_pressed_no_signal(bool(gfx.get("fullscreen", false)))
	if _vsync_check: _vsync_check.set_pressed_no_signal(bool(gfx.get("vsync", true)))
	if _quality_option:
		var q := QUALITY_TIERS.find(str(gfx.get("quality", "high")))
		_quality_option.selected = q if q >= 0 else QUALITY_TIERS.find("high")

	var gp = data.get("gameplay", {})
	if _autosave_option:
		var idx = AUTOSAVE_MINUTES.find(int(gp.get("autosave_minutes", 5)))
		_autosave_option.selected = idx if idx >= 0 else AUTOSAVE_MINUTES.find(5)
	if _tutorial_check: _tutorial_check.set_pressed_no_signal(bool(gp.get("tutorial_enabled", true)))
	if _notifs_check: _notifs_check.set_pressed_no_signal(bool(gp.get("notifications_enabled", true)))
	if _autopause_check: _autopause_check.set_pressed_no_signal(bool(gp.get("auto_pause_critical", true)))
	if _autoslow_check: _autoslow_check.set_pressed_no_signal(bool(gp.get("auto_slow_deadlines", true)))
	# An older setting (0.5–5×) snaps to the nearest of the four speeds.
	if _speed_slider:
		_speed_slider.set_value_no_signal(GameplayPrefs.SPEEDS.find(GameplayPrefs.snap_speed(float(gp.get("default_speed", 1.0)))))
	_update_speed_label()

	var acc = data.get("accessibility", {})
	if _hc_check: _hc_check.set_pressed_no_signal(bool(acc.get("high_contrast", false)))
	if _motion_check: _motion_check.set_pressed_no_signal(bool(acc.get("reduced_motion", false)))
	if _ui_scale_option:
		var scale_idx := UI_SCALE_STEPS.find(float(acc.get("ui_scale", 1.0)))
		_ui_scale_option.selected = scale_idx if scale_idx >= 0 else 2

	var general = data.get("general", {})
	if _language_option:
		var lang := LocalizationManager.normalize(str(general.get("language", "nb")))
		var lang_idx := LocalizationManager.SUPPORTED_LOCALES.find(lang)
		_language_option.selected = lang_idx if lang_idx >= 0 else 0

func _update_pct_labels() -> void:
	if _master_pct and _master_slider: _master_pct.text = "%d%%" % int(_master_slider.value * 100)
	if _music_pct and _music_slider: _music_pct.text = "%d%%" % int(_music_slider.value * 100)
	if _ambient_pct and _ambient_slider: _ambient_pct.text = "%d%%" % int(_ambient_slider.value * 100)
	if _sfx_pct and _sfx_slider: _sfx_pct.text = "%d%%" % int(_sfx_slider.value * 100)

func _update_speed_label() -> void:
	if _speed_val and _speed_slider: _speed_val.text = "%d×" % _speed_from_slider()

## The speed the slider stands on (1, 3, 10 or 30).
func _speed_from_slider() -> int:
	if _speed_slider == null:
		return GameplayPrefs.SPEEDS[0]
	return GameplayPrefs.SPEEDS[clampi(int(_speed_slider.value), 0, GameplayPrefs.SPEEDS.size() - 1)]

## The applier autoload (null outside the game, e.g. a scene opened alone).
func _applier() -> Node:
	return get_node_or_null("/root/SettingsApplier")

func _applier_call(method: String, args: Array) -> void:
	var applier := _applier()
	if applier != null:
		applier.callv(method, args)

func _apply_audio_live() -> void:
	_applier_call("apply_audio", [_collect_from_widgets()["audio"]])
	_dirty = true

## Save and apply everything (the window too).
func _commit() -> void:
	_settings = _collect_from_widgets()
	_save_settings(_settings)
	var applier := _applier()
	if applier != null:
		applier.apply_all(SettingsStore.load_all(), true)
	_dirty = false

func _on_apply() -> void:
	_commit()
	settings_applied.emit()

func _on_reset() -> void:
	_settings = DEFAULT_SETTINGS.duplicate(true)
	_apply_to_widgets(_settings)
	_applier_call("apply_audio", [_settings["audio"]])
	_applier_call("apply_quality", [_quality_name()])
	_applier_call("apply_high_contrast", [false])
	_applier_call("apply_language", [_language_code()])
	_dirty = true

func _on_close() -> void:
	if _dirty:
		_commit()
	close_requested.emit()
	hide()

## Re-read the saved settings into the widgets (the menu is opened again).
func refresh_from_store() -> void:
	_settings = _load_settings()
	_apply_to_widgets(_settings)
	_dirty = false

func get_audio_settings() -> Dictionary:
	return _settings.get("audio", DEFAULT_SETTINGS["audio"].duplicate())

func get_graphics_settings() -> Dictionary:
	return _settings.get("graphics", DEFAULT_SETTINGS["graphics"].duplicate())

func get_gameplay_settings() -> Dictionary:
	return _settings.get("gameplay", DEFAULT_SETTINGS["gameplay"].duplicate())

func get_accessibility_settings() -> Dictionary:
	return _settings.get("accessibility", DEFAULT_SETTINGS["accessibility"].duplicate())

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		_on_close()
		get_viewport().set_input_as_handled()
