extends Control

## SettingsMenu v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/SettingsMenu.tscn; script is wiring only:
## @onready % refs, signal hookups, settings IO, localization, audio apply.
## No scene dependencies beyond its own .tscn. No programmatic layout.

signal settings_applied
signal close_requested

const SETTINGS_PATH := "user://settings.json"
const DEFAULT_SETTINGS := {
	"audio": {"master": 1.0, "music": 0.7, "ambient": 0.5, "sfx": 0.8},
	"graphics": {"resolution": "1920x1080", "fullscreen": false, "vsync": true},
	"gameplay": {"autosave_minutes": 5, "tutorial_enabled": true, "notifications_enabled": true, "default_speed": 1.0, "auto_pause_critical": true},
	"accessibility": {"high_contrast": false, "ui_scale": 1.0, "reduced_motion": false},
}
const UI_SCALE_STEPS: Array[float] = [0.8, 0.9, 1.0, 1.1, 1.25]

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
@onready var _autosave_option: OptionButton = %AutosaveOption
@onready var _tutorial_check: CheckBox = %TutorialCheck
@onready var _notifs_check: CheckBox = %NotifsCheck
@onready var _autopause_check: CheckBox = %AutoPauseCheck
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
	_apply_audio(_settings.get("audio", {}))
	_apply_window_scale(float(_settings.get("accessibility", {}).get("ui_scale", 1.0)))
	hide()

func _apply_localization() -> void:
	_title_label.text = _tr_or("UI_SETTINGS_TITLE", "Innstillinger")
	_reset_button.text = _tr_or("UI_SETTINGS_RESET", "Tilbakestill")
	_apply_button.text = _tr_or("UI_SETTINGS_APPLY", "Bruk")

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
	# Closing keeps a changed motion preference (the menu reads it at once).
	if _motion_check:
		_motion_check.toggled.connect(func(_on: bool) -> void: _dirty = true)

func _populate_options() -> void:
	_res_option.clear()
	for r in ["1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160"]:
		_res_option.add_item(r)
	_autosave_option.clear()
	for m in ["Av", "2 min", "5 min", "10 min", "30 min"]:
		_autosave_option.add_item(m)
	if _ui_scale_option:
		_ui_scale_option.clear()
		for pct in ["80%", "90%", "100%", "110%", "125%"]:
			_ui_scale_option.add_item(pct)

# ── Settings IO ─────────────────────────────────────────────────────────
func _load_settings() -> Dictionary:
	if FileAccess.file_exists(SETTINGS_PATH):
		var file = FileAccess.open(SETTINGS_PATH, FileAccess.READ)
		var json = JSON.new()
		if json.parse(file.get_as_text()) == OK:
			return json.data
	return DEFAULT_SETTINGS.duplicate(true)

func _save_settings(data: Dictionary) -> void:
	var file = FileAccess.open(SETTINGS_PATH, FileAccess.WRITE)
	file.store_string(JSON.stringify(data, "\t"))

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
		},
		"gameplay": {
			"autosave_minutes": [0, 2, 5, 10, 30][_autosave_option.selected] if _autosave_option else 5,
			"tutorial_enabled": _tutorial_check.button_pressed if _tutorial_check else true,
			"notifications_enabled": _notifs_check.button_pressed if _notifs_check else true,
			"default_speed": _speed_slider.value if _speed_slider else 1.0,
			"auto_pause_critical": _autopause_check.button_pressed if _autopause_check else true,
		},
		"accessibility": {
			"high_contrast": _hc_check.button_pressed if _hc_check else false,
			"ui_scale": _ui_scale(),
			"reduced_motion": _motion_check.button_pressed if _motion_check else false,
		},
	}

func _ui_scale() -> float:
	if _ui_scale_option == null:
		return 1.0
	return UI_SCALE_STEPS[clampi(_ui_scale_option.selected, 0, UI_SCALE_STEPS.size() - 1)]

func _apply_to_widgets(data: Dictionary) -> void:
	var audio = data.get("audio", {})
	if _master_slider: _master_slider.value = audio.get("master", 1.0)
	if _music_slider: _music_slider.value = audio.get("music", 0.7)
	if _ambient_slider: _ambient_slider.value = audio.get("ambient", 0.5)
	if _sfx_slider: _sfx_slider.value = audio.get("sfx", 0.8)
	_update_pct_labels()

	var gfx = data.get("graphics", {})
	if _res_option:
		var res_str = gfx.get("resolution", "1920x1080")
		for i in _res_option.item_count:
			if _res_option.get_item_text(i) == res_str:
				_res_option.selected = i
				break
	if _fs_check: _fs_check.button_pressed = gfx.get("fullscreen", false)
	if _vsync_check: _vsync_check.button_pressed = gfx.get("vsync", true)

	var gp = data.get("gameplay", {})
	if _autosave_option:
		var mins = gp.get("autosave_minutes", 5)
		var idx = [0, 2, 5, 10, 30].find(mins)
		if idx >= 0: _autosave_option.selected = idx
	if _tutorial_check: _tutorial_check.button_pressed = gp.get("tutorial_enabled", true)
	if _notifs_check: _notifs_check.button_pressed = gp.get("notifications_enabled", true)
	if _autopause_check: _autopause_check.button_pressed = gp.get("auto_pause_critical", true)
	if _speed_slider: _speed_slider.value = gp.get("default_speed", 1.0)
	_update_speed_label()

	var acc = data.get("accessibility", {})
	if _hc_check: _hc_check.button_pressed = acc.get("high_contrast", false)
	if _motion_check: _motion_check.set_pressed_no_signal(bool(acc.get("reduced_motion", false)))
	if _ui_scale_option:
		var scale_idx := UI_SCALE_STEPS.find(float(acc.get("ui_scale", 1.0)))
		_ui_scale_option.selected = scale_idx if scale_idx >= 0 else 2

func _update_pct_labels() -> void:
	if _master_pct and _master_slider: _master_pct.text = "%d%%" % int(_master_slider.value * 100)
	if _music_pct and _music_slider: _music_pct.text = "%d%%" % int(_music_slider.value * 100)
	if _ambient_pct and _ambient_slider: _ambient_pct.text = "%d%%" % int(_ambient_slider.value * 100)
	if _sfx_pct and _sfx_slider: _sfx_pct.text = "%d%%" % int(_sfx_slider.value * 100)

func _update_speed_label() -> void:
	if _speed_val and _speed_slider: _speed_val.text = "%.1f" % _speed_slider.value

func _apply_audio(audio: Dictionary) -> void:
	var bus_map = {"Master": "master", "Music": "music", "Ambient": "ambient", "SFX": "sfx"}
	for bus_name in bus_map:
		var idx = AudioServer.get_bus_index(bus_name)
		if idx >= 0:
			var vol = audio.get(bus_map[bus_name], 1.0)
			AudioServer.set_bus_volume_db(idx, linear_to_db(vol))

func _apply_audio_live() -> void:
	_apply_audio(_collect_from_widgets()["audio"])
	_dirty = true

func _on_apply() -> void:
	_settings = _collect_from_widgets()
	_save_settings(_settings)
	_apply_audio(_settings["audio"])
	_apply_window_scale(float(_settings.get("accessibility", {}).get("ui_scale", 1.0)))
	settings_applied.emit()
	_dirty = false

## Apply UI scale to the root window (canvas_items stretch honours this).
func _apply_window_scale(scale: float) -> void:
	var win := get_window()
	if win:
		win.content_scale_factor = scale

func _on_reset() -> void:
	_settings = DEFAULT_SETTINGS.duplicate(true)
	_apply_to_widgets(_settings)
	_apply_audio(_settings["audio"])
	_dirty = true

func _on_close() -> void:
	if _dirty:
		_settings = _collect_from_widgets()
		_save_settings(_settings)
		_apply_window_scale(float(_settings.get("accessibility", {}).get("ui_scale", 1.0)))
		_dirty = false
	close_requested.emit()
	hide()

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
