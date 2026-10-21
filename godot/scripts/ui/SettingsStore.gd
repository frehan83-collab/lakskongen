class_name SettingsStore
extends RefCounted

## The player's settings file (user://settings.json) and its defaults, read
## and written in one place (roadmap U2-08). Every persisted setting has a
## consumer that reads it here (SettingsApplier pushes the ones that live in
## the engine; GameplayPrefs, MotionPrefs and RenderQuality read the rest on
## demand), and tests point path_override at their own file instead of the
## player's.
##
## Sections and keys (DEFAULTS is the whole schema):
##   audio          master, music, ambient, sfx (0-1)       -> the audio buses
##   graphics       resolution "WxH", fullscreen, vsync      -> the window (DisplaySettings)
##                  quality low|medium|high|ultra             -> EnvironmentDirector (RenderQuality)
##   gameplay       autosave_minutes 0|2|5|10|30             -> SaveLoadManager
##                  tutorial_enabled, auto_pause_critical,
##                  auto_slow_deadlines, default_speed       -> GameplayPrefs
##                  notifications_enabled                    -> warning toasts (ConsequenceDirector)
##   accessibility  high_contrast                            -> LakskongenTheme
##                  ui_scale                                  -> the window's content scale
##                  reduced_motion                            -> MotionPrefs
##   general        language nb|en                           -> LocalizationManager

const PATH := "user://settings.json"
const DEFAULTS := {
	"audio": {"master": 1.0, "music": 0.7, "ambient": 0.5, "sfx": 0.8},
	"graphics": {"resolution": "1920x1080", "fullscreen": false, "vsync": true, "quality": "high"},
	"gameplay": {"autosave_minutes": 5, "tutorial_enabled": true, "notifications_enabled": true, "default_speed": 1,
		"auto_pause_critical": true, "auto_slow_deadlines": true},
	"accessibility": {"high_contrast": false, "ui_scale": 1.0, "reduced_motion": false},
	"general": {"language": "nb"},
}

## A run launched by a scene path (the UI tests, the QA capture scenes, the
## screenshot and perf harnesses) reads and writes this file instead of the
## player's, and SettingsApplier deletes it at boot: the player's high
## contrast, language or quality never reach a baseline frame or a
## measurement (U2-08 review). The game launched as the game uses PATH.
const BY_PATH_PATH := "user://settings_by_path.json"

## Tests (and QA scenes) point this at their own file; "" is the default:
## the player's, or BY_PATH_PATH in a run launched by a scene path.
static var path_override := ""
## Cached launched_by_path() (-1: not read yet).
static var _by_path := -1


static func path() -> String:
	if not path_override.is_empty():
		return path_override
	return BY_PATH_PATH if launched_by_path() else PATH


## True when this process runs a scene named on its command line (a test or a
## QA scene) instead of the project's main scene.
static func launched_by_path() -> bool:
	if _by_path < 0:
		_by_path = 0
		var main := str(ProjectSettings.get_setting("application/run/main_scene", ""))
		for arg in OS.get_cmdline_args():
			if arg.ends_with(".tscn") and arg != main:
				_by_path = 1
				break
	return _by_path == 1


static func has_file() -> bool:
	return FileAccess.file_exists(path())


## The file as written (no defaults filled in); {} when missing or broken.
static func load_raw() -> Dictionary:
	if not has_file():
		return {}
	var file := FileAccess.open(path(), FileAccess.READ)
	if file == null:
		return {}
	var data: Variant = JSON.parse_string(file.get_as_text())
	return data as Dictionary if data is Dictionary else {}


## Every setting: the file's values over the defaults, section by section (a
## missing or broken section or key reads as its default).
static func load_all() -> Dictionary:
	var raw := load_raw()
	var out := {}
	for section in DEFAULTS:
		var merged: Dictionary = (DEFAULTS[section] as Dictionary).duplicate(true)
		var have: Variant = raw.get(section, {})
		if have is Dictionary:
			for key in have:
				merged[key] = have[key]
		out[section] = merged
	return out


static func section(name: String) -> Dictionary:
	return load_all().get(name, {})


static func value(section_name: String, key: String) -> Variant:
	return section(section_name).get(key, (DEFAULTS.get(section_name, {}) as Dictionary).get(key))


## Write every setting (sections not in `data` keep their stored values).
static func save_all(data: Dictionary) -> bool:
	var merged := load_all()
	for section in data:
		if data[section] is Dictionary:
			var s: Dictionary = merged.get(section, {})
			for key in data[section]:
				s[key] = data[section][key]
			merged[section] = s
	var file := FileAccess.open(path(), FileAccess.WRITE)
	if file == null:
		push_warning("SettingsStore: could not write %s" % path())
		return false
	file.store_string(JSON.stringify(merged, "\t"))
	return true


## Set one setting and write the file.
static func set_value(section_name: String, key: String, v: Variant) -> bool:
	return save_all({section_name: {key: v}})


## Every persisted key as "section.key" (the U2-08 consumer test covers each).
static func all_keys() -> Array[String]:
	var keys: Array[String] = []
	for section in DEFAULTS:
		for key in DEFAULTS[section]:
			keys.append("%s.%s" % [section, key])
	return keys
