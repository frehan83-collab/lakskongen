extends Control

## SettingsMenu v2.0 — Nordic Minimalist, fully programmatic.
## No scene dependencies. Builds all UI in _ready().

signal settings_applied
signal close_requested

const SETTINGS_PATH := "user://settings.json"
const DEFAULT_SETTINGS := {
	"audio": {"master": 1.0, "music": 0.7, "ambient": 0.5, "sfx": 0.8},
	"graphics": {"resolution": "1920x1080", "fullscreen": false, "vsync": true},
	"gameplay": {"autosave_minutes": 5, "tutorial_enabled": true, "notifications_enabled": true, "default_speed": 1.0},
	"accessibility": {"high_contrast": false},
}

var _settings: Dictionary = {}
var _dirty: bool = false

# UI refs
var _overlay: ColorRect
var _panel: PanelContainer
var _tab_container: TabContainer
var _master_slider: HSlider
var _music_slider: HSlider
var _ambient_slider: HSlider
var _sfx_slider: HSlider
var _master_pct: Label
var _music_pct: Label
var _ambient_pct: Label
var _sfx_pct: Label
var _res_option: OptionButton
var _fs_check: CheckBox
var _vsync_check: CheckBox
var _autosave_option: OptionButton
var _tutorial_check: CheckBox
var _notifs_check: CheckBox
var _speed_slider: HSlider
var _speed_val: Label
var _hc_check: CheckBox

func _ready() -> void:
	_settings = _load_settings()
	_build_ui()
	_apply_to_widgets(_settings)
	_apply_audio(_settings.get("audio", {}))
	hide()

func _build_ui() -> void:
	for child in get_children():
		child.queue_free()

	# Overlay
	_overlay = ColorRect.new()
	_overlay.color = Color(0.18, 0.20, 0.25, 0.65)
	_overlay.set_anchors_and_offsets_preset(PRESET_FULL_RECT)
	_overlay.mouse_filter = MOUSE_FILTER_STOP
	add_child(_overlay)

	# Panel
	_panel = PanelContainer.new()
	_panel.custom_minimum_size = Vector2(700, 500)
	_panel.anchor_left = 0.5; _panel.anchor_right = 0.5
	_panel.anchor_top = 0.5; _panel.anchor_bottom = 0.5
	_panel.offset_left = -350; _panel.offset_right = 350
	_panel.offset_top = -250; _panel.offset_bottom = 250
	var panel_sb = StyleBoxFlat.new()
	panel_sb.bg_color = Color("#FFFFFF")
	panel_sb.set_corner_radius_all(12)
	panel_sb.set_content_margin_all(20)
	_panel.add_theme_stylebox_override("panel", panel_sb)
	add_child(_panel)

	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 12)
	_panel.add_child(vbox)

	# Header
	var header = HBoxContainer.new()
	vbox.add_child(header)
	var title = Label.new()
	title.text = "Innstillinger"
	title.add_theme_font_size_override("font_size", 22)
	title.add_theme_color_override("font_color", Color("#2E3440"))
	title.size_flags_horizontal = SIZE_EXPAND_FILL
	header.add_child(title)
	var close_btn = Button.new()
	close_btn.text = "✕"
	close_btn.pressed.connect(_on_close)
	header.add_child(close_btn)

	# Tabs
	_tab_container = TabContainer.new()
	_tab_container.size_flags_vertical = SIZE_EXPAND_FILL
	vbox.add_child(_tab_container)

	_build_audio_tab()
	_build_graphics_tab()
	_build_gameplay_tab()
	_build_accessibility_tab()

	# Buttons
	var btn_row = HBoxContainer.new()
	btn_row.alignment = BoxContainer.ALIGNMENT_END
	btn_row.add_theme_constant_override("separation", 8)
	vbox.add_child(btn_row)
	var reset_btn = Button.new()
	reset_btn.text = "Tilbakestill"
	reset_btn.pressed.connect(_on_reset)
	btn_row.add_child(reset_btn)
	var apply_btn = Button.new()
	apply_btn.text = "Bruk"
	var apply_sb = StyleBoxFlat.new()
	apply_sb.bg_color = Color("#0A4D68")
	apply_sb.set_corner_radius_all(4)
	apply_sb.set_content_margin_all(8)
	apply_btn.add_theme_stylebox_override("normal", apply_sb)
	apply_btn.add_theme_color_override("font_color", Color.WHITE)
	apply_btn.pressed.connect(_on_apply)
	btn_row.add_child(apply_btn)

func _build_audio_tab() -> void:
	var tab = VBoxContainer.new()
	tab.name = "Lyd"
	tab.add_theme_constant_override("separation", 12)
	_tab_container.add_child(tab)
	var r1 = _make_slider_row(tab, "Master", 0.0, 1.0, 0.01)
	_master_slider = r1.slider; _master_pct = r1.label
	_master_slider.value_changed.connect(func(v): _master_pct.text = "%d%%" % int(v*100); _apply_audio_live())
	var r2 = _make_slider_row(tab, "Musikk", 0.0, 1.0, 0.01)
	_music_slider = r2.slider; _music_pct = r2.label
	_music_slider.value_changed.connect(func(v): _music_pct.text = "%d%%" % int(v*100); _apply_audio_live())
	var r3 = _make_slider_row(tab, "Ambient", 0.0, 1.0, 0.01)
	_ambient_slider = r3.slider; _ambient_pct = r3.label
	_ambient_slider.value_changed.connect(func(v): _ambient_pct.text = "%d%%" % int(v*100); _apply_audio_live())
	var r4 = _make_slider_row(tab, "Effekter", 0.0, 1.0, 0.01)
	_sfx_slider = r4.slider; _sfx_pct = r4.label
	_sfx_slider.value_changed.connect(func(v): _sfx_pct.text = "%d%%" % int(v*100); _apply_audio_live())

func _build_graphics_tab() -> void:
	var tab = VBoxContainer.new()
	tab.name = "Grafikk"
	tab.add_theme_constant_override("separation", 12)
	_tab_container.add_child(tab)
	# Resolution
	var res_row = HBoxContainer.new()
	tab.add_child(res_row)
	var res_lbl = Label.new()
	res_lbl.text = "Oppløsning"
	res_lbl.size_flags_horizontal = SIZE_EXPAND_FILL
	res_row.add_child(res_lbl)
	_res_option = OptionButton.new()
	for r in ["1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160"]:
		_res_option.add_item(r)
	res_row.add_child(_res_option)
	# Fullscreen
	_fs_check = _make_check_row(tab, "Fullskjerm")
	_vsync_check = _make_check_row(tab, "VSync")

func _build_gameplay_tab() -> void:
	var tab = VBoxContainer.new()
	tab.name = "Spill"
	tab.add_theme_constant_override("separation", 12)
	_tab_container.add_child(tab)
	var as_row = HBoxContainer.new()
	tab.add_child(as_row)
	var as_lbl = Label.new()
	as_lbl.text = "Autolagring"
	as_lbl.size_flags_horizontal = SIZE_EXPAND_FILL
	as_row.add_child(as_lbl)
	_autosave_option = OptionButton.new()
	for m in ["Av", "2 min", "5 min", "10 min", "30 min"]:
		_autosave_option.add_item(m)
	as_row.add_child(_autosave_option)
	_tutorial_check = _make_check_row(tab, "Vis tutorial")
	_notifs_check = _make_check_row(tab, "Varsler")
	var spd = _make_slider_row(tab, "Standard hastighet", 0.5, 5.0, 0.5)
	_speed_slider = spd.slider; _speed_val = spd.label

func _build_accessibility_tab() -> void:
	var tab = VBoxContainer.new()
	tab.name = "Tilgjengelighet"
	tab.add_theme_constant_override("separation", 12)
	_tab_container.add_child(tab)
	_hc_check = _make_check_row(tab, "Høykontrast")

func _make_slider_row(parent: VBoxContainer, label_text: String, min_val: float, max_val: float, step: float) -> Dictionary:
	var row = HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	parent.add_child(row)
	var lbl = Label.new()
	lbl.text = label_text
	lbl.custom_minimum_size.x = 120
	row.add_child(lbl)
	var slider = HSlider.new()
	slider.min_value = min_val
	slider.max_value = max_val
	slider.step = step
	slider.size_flags_horizontal = SIZE_EXPAND_FILL
	row.add_child(slider)
	var val_lbl = Label.new()
	val_lbl.custom_minimum_size.x = 50
	val_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	row.add_child(val_lbl)
	return {"slider": slider, "label": val_lbl}

func _make_check_row(parent: VBoxContainer, label_text: String) -> CheckBox:
	var check = CheckBox.new()
	check.text = label_text
	parent.add_child(check)
	return check

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
		},
		"accessibility": {
			"high_contrast": _hc_check.button_pressed if _hc_check else false,
		},
	}

func _apply_to_widgets(data: Dictionary) -> void:
	var audio = data.get("audio", {})
	if _master_slider: _master_slider.value = audio.get("master", 1.0)
	if _music_slider: _music_slider.value = audio.get("music", 0.7)
	if _ambient_slider: _ambient_slider.value = audio.get("ambient", 0.5)
	if _sfx_slider: _sfx_slider.value = audio.get("sfx", 0.8)

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
	if _speed_slider: _speed_slider.value = gp.get("default_speed", 1.0)

	var acc = data.get("accessibility", {})
	if _hc_check: _hc_check.button_pressed = acc.get("high_contrast", false)

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
	settings_applied.emit()
	_dirty = false

func _on_reset() -> void:
	_settings = DEFAULT_SETTINGS.duplicate(true)
	_apply_to_widgets(_settings)
	_apply_audio(_settings["audio"])
	_dirty = true

func _on_close() -> void:
	if _dirty:
		_settings = _collect_from_widgets()
		_save_settings(_settings)
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
