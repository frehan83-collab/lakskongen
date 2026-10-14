class_name MotionPrefs
extends RefCounted

## Reduced motion (roadmap U1-09, accessibility). On when the player ticks
## "Redusert bevegelse" (Innstillinger > Tilgjengelighet, saved as
## accessibility.reduced_motion in user://settings.json) or when the
## operating system asks apps to reduce animation (macOS "Reduce motion",
## Windows "Show animations" off).
##
## What honours it: the main menu's drifting camera and its slide-ins hold
## still, and its fades shorten to a quick cross-fade; button hover pops stay
## at their size. Opacity changes stay (they do not move anything on screen).

const SETTINGS_PATH := "user://settings.json"

## Test seam: -1 follows the setting and the OS, 0 forces off, 1 forces on.
static var _forced: int = -1


static func reduced_motion() -> bool:
	if _forced >= 0:
		return _forced == 1
	if setting_enabled():
		return true
	return DisplayServer.accessibility_should_reduce_animation() == 1


## The player's own setting (false when the settings file is missing or broken).
static func setting_enabled() -> bool:
	if not FileAccess.file_exists(SETTINGS_PATH):
		return false
	var file := FileAccess.open(SETTINGS_PATH, FileAccess.READ)
	if file == null:
		return false
	var data: Variant = JSON.parse_string(file.get_as_text())
	if not (data is Dictionary):
		return false
	var acc: Variant = (data as Dictionary).get("accessibility", {})
	return acc is Dictionary and bool((acc as Dictionary).get("reduced_motion", false))


## Tests pin the preference (true/false) or hand it back to the player (null).
static func force(value: Variant) -> void:
	_forced = -1 if value == null else (1 if bool(value) else 0)
