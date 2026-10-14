class_name GameplayPrefs
extends RefCounted

## Gameplay settings the event channels read (roadmap U2-06), from
## user://settings.json (SettingsMenu > Spill).
##
## "Pause ved kritiske hendelser" (gameplay.auto_pause_critical, on by
## default): a critical event's card pauses the game while it is open.

const SETTINGS_PATH := "user://settings.json"

## Test seam: -1 follows the setting, 0 forces off, 1 forces on.
static var _forced_auto_pause: int = -1


static func auto_pause_critical() -> bool:
	if _forced_auto_pause >= 0:
		return _forced_auto_pause == 1
	if not FileAccess.file_exists(SETTINGS_PATH):
		return true
	var file := FileAccess.open(SETTINGS_PATH, FileAccess.READ)
	if file == null:
		return true
	var data: Variant = JSON.parse_string(file.get_as_text())
	if not (data is Dictionary):
		return true
	var gameplay: Variant = (data as Dictionary).get("gameplay", {})
	return not (gameplay is Dictionary) or bool((gameplay as Dictionary).get("auto_pause_critical", true))


## Tests pin the setting (true/false) or hand it back to the player (null).
static func force_auto_pause(value: Variant) -> void:
	_forced_auto_pause = -1 if value == null else (1 if bool(value) else 0)
