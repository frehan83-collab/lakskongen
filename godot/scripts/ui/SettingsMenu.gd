extends Control

## SettingsMenu — full tabbed settings panel for Lakskongen.
## Tabs: Graphics, Audio, Gameplay.
## Settings are persisted to user://settings.json and reloaded on startup.
## Connects volume sliders directly to AudioServer buses (via AudioManager or
## direct AudioServer calls so it works standalone).
##
## Expected node layout (set up in .tscn):
##   SettingsMenu (Control, full-screen anchor, modal)
##   └─ Overlay (ColorRect, semi-transparent background)
##   └─ Panel (PanelContainer, centred)
##      ├─ VBoxContainer
##      │  ├─ HeaderRow (HBoxContainer)
##      │  │  ├─ TitleLabel (Label) "Innstillinger"
##      │  │  └─ CloseButton (Button) "✕"
##      │  ├─ TabContainer
##      │  │  ├─ Audio  (VBoxContainer, tab name "Lyd")
##      │  │  │  ├─ MasterRow    (HBoxContainer) ← MasterLabel + MasterSlider + MasterPct
##      │  │  │  ├─ MusicRow     (HBoxContainer) ← MusicLabel  + MusicSlider  + MusicPct
##      │  │  │  ├─ AmbientRow   (HBoxContainer) ← AmbientLabel+ AmbientSlider+ AmbientPct
##      │  │  │  └─ SFXRow       (HBoxContainer) ← SFXLabel    + SFXSlider    + SFXPct
##      │  │  ├─ Graphics (VBoxContainer, tab name "Grafikk")
##      │  │  │  ├─ ResolutionRow  (HBoxContainer) ← ResLabel + ResOption
##      │  │  │  ├─ FullscreenRow  (HBoxContainer) ← FSLabel  + FSCheck
##      │  │  │  └─ VSyncRow       (HBoxContainer) ← VSLabel  + VSCheck
##      │  │  └─ Gameplay (VBoxContainer, tab name "Spill")
##      │  │     ├─ AutosaveRow   (HBoxContainer) ← ASLabel + ASOption
##      │  │     ├─ TutorialRow   (HBoxContainer) ← TutLabel + TutCheck
##      │  │     ├─ NotifsRow     (HBoxContainer) ← NotiLabel + NotiCheck
##      │  │     └─ SpeedRow      (HBoxContainer) ← SpeedLabel + SpeedSlider + SpeedVal
##      │  └─ ButtonRow (HBoxContainer)
##      │     ├─ ResetButton (Button) "Tilbakestill"
##      │     └─ ApplyButton (Button) "Bruk"

signal settings_applied
signal close_requested

# ── Constants ──────────────────────────────────────────────────────────────────

const SETTINGS_PATH := "user://settings.json"

const RESOLUTIONS := [
	"1280x720",
	"1600x900",
	"1920x1080",
	"2560x1440",
	"3840x2160",
]

const AUTOSAVE_OPTIONS := [
	"Av",
	"2 min",
	"5 min",
	"10 min",
	"30 min",
]
const AUTOSAVE_MINUTES := [0, 2, 5, 10, 30]

const DEFAULT_SETTINGS := {
	"audio": {
		"master":  1.0,
		"music":   0.7,
		"ambient": 0.5,
		"sfx":     0.8,
	},
	"graphics": {
		"resolution": "1920x1080",
		"fullscreen": false,
		"vsync":      true,
	},
	"gameplay": {
		"autosave_minutes":      5,
		"tutorial_enabled":      true,
		"notifications_enabled": true,
		"default_speed":         1.0,
	},
}

# ── Node references ────────────────────────────────────────────────────────────

@onready var close_button:   Button        = $Panel/VBoxContainer/HeaderRow/CloseButton
@onready var tab_container:  TabContainer  = $Panel/VBoxContainer/TabContainer
@onready var apply_button:   Button        = $Panel/VBoxContainer/ButtonRow/ApplyButton
@onready var reset_button:   Button        = $Panel/VBoxContainer/ButtonRow/ResetButton

# Audio tab
@onready var master_slider:  HSlider = $Panel/VBoxContainer/TabContainer/Audio/MasterRow/MasterSlider
@onready var master_pct:     Label   = $Panel/VBoxContainer/TabContainer/Audio/MasterRow/MasterPct
@onready var music_slider:   HSlider = $Panel/VBoxContainer/TabContainer/Audio/MusicRow/MusicSlider
@onready var music_pct:      Label   = $Panel/VBoxContainer/TabContainer/Audio/MusicRow/MusicPct
@onready var ambient_slider: HSlider = $Panel/VBoxContainer/TabContainer/Audio/AmbientRow/AmbientSlider
@onready var ambient_pct:    Label   = $Panel/VBoxContainer/TabContainer/Audio/AmbientRow/AmbientPct
@onready var sfx_slider:     HSlider = $Panel/VBoxContainer/TabContainer/Audio/SFXRow/SFXSlider
@onready var sfx_pct:        Label   = $Panel/VBoxContainer/TabContainer/Audio/SFXRow/SFXPct

# Graphics tab
@onready var res_option:    OptionButton = $Panel/VBoxContainer/TabContainer/Graphics/ResolutionRow/ResOption
@onready var fs_check:      CheckBox     = $Panel/VBoxContainer/TabContainer/Graphics/FullscreenRow/FSCheck
@onready var vsync_check:   CheckBox     = $Panel/VBoxContainer/TabContainer/Graphics/VSyncRow/VSCheck

# Gameplay tab
@onready var autosave_option: OptionButton = $Panel/VBoxContainer/TabContainer/Gameplay/AutosaveRow/ASOption
@onready var tutorial_check:  CheckBox     = $Panel/VBoxContainer/TabContainer/Gameplay/TutorialRow/TutCheck
@onready var notifs_check:    CheckBox     = $Panel/VBoxContainer/TabContainer/Gameplay/NotifsRow/NotiCheck
@onready var speed_slider:    HSlider      = $Panel/VBoxContainer/TabContainer/Gameplay/SpeedRow/SpeedSlider
@onready var speed_val:       Label        = $Panel/VBoxContainer/TabContainer/Gameplay/SpeedRow/SpeedVal

# ── State ──────────────────────────────────────────────────────────────────────

var _audio_manager: Node = null
var _settings: Dictionary = {}
var _dirty: bool = false       # true when unsaved changes exist

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_audio_manager = get_node_or_null("/root/AudioManager")

	_populate_options()
	_settings = _load_settings()
	_apply_to_widgets(_settings)
	_connect_signals()
	_apply_audio(_settings["audio"])
	_apply_graphics(_settings["graphics"])


func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		_on_close_pressed()
		get_viewport().set_input_as_handled()

# ── Option population ─────────────────────────────────────────────────────────

func _populate_options() -> void:
	for res in RESOLUTIONS:
		res_option.add_item(res)

	for opt in AUTOSAVE_OPTIONS:
		autosave_option.add_item(opt)

# ── Load / Save ───────────────────────────────────────────────────────────────

func _load_settings() -> Dictionary:
	if not FileAccess.file_exists(SETTINGS_PATH):
		return DEFAULT_SETTINGS.duplicate(true)

	var file := FileAccess.open(SETTINGS_PATH, FileAccess.READ)
	if file == null:
		push_warning("SettingsMenu: could not open %s" % SETTINGS_PATH)
		return DEFAULT_SETTINGS.duplicate(true)

	var json_text := file.get_as_text()
	file.close()

	var parsed = JSON.parse_string(json_text)
	if typeof(parsed) != TYPE_DICTIONARY:
		push_warning("SettingsMenu: malformed settings file, using defaults")
		return DEFAULT_SETTINGS.duplicate(true)

	# Merge with defaults so missing keys are filled in
	var merged := DEFAULT_SETTINGS.duplicate(true)
	_deep_merge(merged, parsed)
	return merged


func _save_settings(data: Dictionary) -> void:
	var file := FileAccess.open(SETTINGS_PATH, FileAccess.WRITE)
	if file == null:
		push_warning("SettingsMenu: could not write %s" % SETTINGS_PATH)
		return
	file.store_string(JSON.stringify(data, "\t"))
	file.close()


## Recursively merge src into dst (dst wins for non-dict leaf values, src overwrites).
func _deep_merge(dst: Dictionary, src: Dictionary) -> void:
	for key in src:
		if dst.has(key) and typeof(dst[key]) == TYPE_DICTIONARY and typeof(src[key]) == TYPE_DICTIONARY:
			_deep_merge(dst[key], src[key])
		else:
			dst[key] = src[key]

# ── Apply settings to widgets ─────────────────────────────────────────────────

func _apply_to_widgets(data: Dictionary) -> void:
	var audio    : Dictionary = data.get("audio",    DEFAULT_SETTINGS["audio"])
	var graphics : Dictionary = data.get("graphics", DEFAULT_SETTINGS["graphics"])
	var gameplay : Dictionary = data.get("gameplay", DEFAULT_SETTINGS["gameplay"])

	# Audio sliders
	master_slider.value  = audio.get("master",  1.0) * 100.0
	music_slider.value   = audio.get("music",   0.7) * 100.0
	ambient_slider.value = audio.get("ambient", 0.5) * 100.0
	sfx_slider.value     = audio.get("sfx",     0.8) * 100.0
	_update_pct_labels()

	# Graphics
	var res: String = graphics.get("resolution", "1920x1080")
	var res_idx := RESOLUTIONS.find(res)
	res_option.selected = max(res_idx, 0)
	fs_check.button_pressed    = graphics.get("fullscreen", false)
	vsync_check.button_pressed = graphics.get("vsync", true)

	# Gameplay
	var autosave_min: int = gameplay.get("autosave_minutes", 5)
	var as_idx := AUTOSAVE_MINUTES.find(autosave_min)
	autosave_option.selected = max(as_idx, 0)
	tutorial_check.button_pressed = gameplay.get("tutorial_enabled",      true)
	notifs_check.button_pressed   = gameplay.get("notifications_enabled", true)
	speed_slider.value            = gameplay.get("default_speed", 1.0)
	_update_speed_label()


func _update_pct_labels() -> void:
	master_pct.text  = "%d%%" % int(master_slider.value)
	music_pct.text   = "%d%%" % int(music_slider.value)
	ambient_pct.text = "%d%%" % int(ambient_slider.value)
	sfx_pct.text     = "%d%%" % int(sfx_slider.value)


func _update_speed_label() -> void:
	speed_val.text = "%.1fx" % speed_slider.value

# ── Apply settings to engine ──────────────────────────────────────────────────

func _apply_audio(audio: Dictionary) -> void:
	_set_bus_volume("Master",  audio.get("master",  1.0))
	_set_bus_volume("Music",   audio.get("music",   0.7))
	_set_bus_volume("Ambient", audio.get("ambient", 0.5))
	_set_bus_volume("SFX",     audio.get("sfx",     0.8))


func _set_bus_volume(bus_name: String, linear: float) -> void:
	if _audio_manager and _audio_manager.has_method("set_bus_volume"):
		_audio_manager.set_bus_volume(bus_name, linear)
		return
	# Fallback: drive AudioServer directly
	var idx := AudioServer.get_bus_index(bus_name)
	if idx >= 0:
		var db := linear_to_db(linear) if linear > 0.0 else -80.0
		AudioServer.set_bus_volume_db(idx, db)


func _apply_graphics(graphics: Dictionary) -> void:
	var fullscreen: bool = graphics.get("fullscreen", false)
	if fullscreen:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)
	else:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_WINDOWED)

	DisplayServer.window_set_vsync_mode(
		DisplayServer.VSYNC_ENABLED if graphics.get("vsync", true)
		else DisplayServer.VSYNC_DISABLED
	)

	var res_str: String = graphics.get("resolution", "1920x1080")
	var parts := res_str.split("x")
	if parts.size() == 2:
		var w := int(parts[0])
		var h := int(parts[1])
		if w > 0 and h > 0:
			DisplayServer.window_set_size(Vector2i(w, h))

# ── Read current widget state into a dict ─────────────────────────────────────

func _collect_from_widgets() -> Dictionary:
	var autosave_idx := autosave_option.selected
	var autosave_min := AUTOSAVE_MINUTES[autosave_idx] if autosave_idx < AUTOSAVE_MINUTES.size() else 5

	return {
		"audio": {
			"master":  master_slider.value  / 100.0,
			"music":   music_slider.value   / 100.0,
			"ambient": ambient_slider.value / 100.0,
			"sfx":     sfx_slider.value     / 100.0,
		},
		"graphics": {
			"resolution": RESOLUTIONS[res_option.selected] if res_option.selected < RESOLUTIONS.size() else "1920x1080",
			"fullscreen": fs_check.button_pressed,
			"vsync":      vsync_check.button_pressed,
		},
		"gameplay": {
			"autosave_minutes":      autosave_min,
			"tutorial_enabled":      tutorial_check.button_pressed,
			"notifications_enabled": notifs_check.button_pressed,
			"default_speed":         speed_slider.value,
		},
	}

# ── Signal connections ─────────────────────────────────────────────────────────

func _connect_signals() -> void:
	close_button.pressed.connect(_on_close_pressed)
	apply_button.pressed.connect(_on_apply_pressed)
	reset_button.pressed.connect(_on_reset_pressed)

	master_slider.value_changed.connect(func(_v): _on_audio_slider_changed())
	music_slider.value_changed.connect(func(_v): _on_audio_slider_changed())
	ambient_slider.value_changed.connect(func(_v): _on_audio_slider_changed())
	sfx_slider.value_changed.connect(func(_v): _on_audio_slider_changed())

	fs_check.toggled.connect(func(_t): _dirty = true)
	vsync_check.toggled.connect(func(_t): _dirty = true)
	res_option.item_selected.connect(func(_i): _dirty = true)

	autosave_option.item_selected.connect(func(_i): _dirty = true)
	tutorial_check.toggled.connect(func(_t): _dirty = true)
	notifs_check.toggled.connect(func(_t): _dirty = true)
	speed_slider.value_changed.connect(func(_v): _on_speed_slider_changed())

# ── Signal handlers ────────────────────────────────────────────────────────────

func _on_audio_slider_changed() -> void:
	_update_pct_labels()
	# Live-preview audio changes
	var data := _collect_from_widgets()
	_apply_audio(data["audio"])
	_dirty = true


func _on_speed_slider_changed() -> void:
	_update_speed_label()
	_dirty = true


func _on_apply_pressed() -> void:
	_settings = _collect_from_widgets()
	_apply_audio(_settings["audio"])
	_apply_graphics(_settings["graphics"])
	_save_settings(_settings)
	_dirty = false
	settings_applied.emit()
	# Animate the button briefly to give feedback
	apply_button.text = "Lagret ✓"
	var tween := create_tween()
	tween.tween_interval(1.2)
	tween.tween_callback(func(): apply_button.text = "Bruk")


func _on_reset_pressed() -> void:
	_settings = DEFAULT_SETTINGS.duplicate(true)
	_apply_to_widgets(_settings)
	_apply_audio(_settings["audio"])
	_apply_graphics(_settings["graphics"])
	_save_settings(_settings)
	_dirty = false


func _on_close_pressed() -> void:
	if _dirty:
		# Auto-save on close so live audio previews are persisted
		_settings = _collect_from_widgets()
		_save_settings(_settings)
		_dirty = false
	close_requested.emit()
	hide()

# ── Public API ─────────────────────────────────────────────────────────────────

## Call this from the game's autoload bootstrap to apply settings at startup.
func load_and_apply() -> void:
	_settings = _load_settings()
	_apply_audio(_settings["audio"])
	_apply_graphics(_settings["graphics"])


## Returns the current gameplay settings dict for other systems to read.
func get_gameplay_settings() -> Dictionary:
	return _settings.get("gameplay", DEFAULT_SETTINGS["gameplay"].duplicate())
