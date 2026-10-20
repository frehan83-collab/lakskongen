class_name GameplayPrefs
extends RefCounted

## Gameplay settings the event channels and the game clock read (roadmap
## U2-06, U2-07), from user://settings.json (SettingsMenu > Spill).
##
## "Pause ved kritiske hendelser" (gameplay.auto_pause_critical, on by
## default): a critical event's card pauses the game while it is open.
## "Bremse ned før frister" (gameplay.auto_slow_deadlines, on by default): the
## deadline rule slows the clock to 1× a week before an order's deadline and
## stops it the day before.
## "Vis opplæring" (gameplay.tutorial_enabled, on by default): the first
## season's guide card (U2-02).
## "Standard hastighet" (gameplay.default_speed, 1×): the speed a new game's
## briefing starts the clock at (1, 3, 10 or 30; older settings snap).

const SETTINGS_PATH := "user://settings.json"
## The game clock's speeds (GameSpeed.Steps).
const SPEEDS: Array[int] = [1, 3, 10, 30]

## Test seams: -1 follows the setting, 0 forces off, 1 forces on.
static var _forced_auto_pause: int = -1
static var _forced_auto_slow: int = -1
static var _forced_tutorial: int = -1


## "Vis opplæring" (gameplay.tutorial_enabled, on by default): the first
## season's card and highlight show (U2-02). Off, the steps still advance on
## the player's actions, but nothing guides or holds the clock.
static func tutorial_enabled() -> bool:
	if _forced_tutorial >= 0:
		return _forced_tutorial == 1
	return bool(_gameplay().get("tutorial_enabled", true))


static func auto_pause_critical() -> bool:
	if _forced_auto_pause >= 0:
		return _forced_auto_pause == 1
	return bool(_gameplay().get("auto_pause_critical", true))


static func auto_slow_deadlines() -> bool:
	if _forced_auto_slow >= 0:
		return _forced_auto_slow == 1
	return bool(_gameplay().get("auto_slow_deadlines", true))


## The speed a new game starts at: the saved default snapped to 1/3/10/30
## (the old slider went 0.5–5 in steps of 0.5).
static func default_speed() -> int:
	return snap_speed(float(_gameplay().get("default_speed", 1.0)))


## The step nearest to `multiplier` by ratio (GameSpeed.Snap).
static func snap_speed(multiplier: float) -> int:
	if is_nan(multiplier) or multiplier <= 0.0:
		return SPEEDS[0]
	var best := SPEEDS[0]
	for s in SPEEDS:
		if absf(log(multiplier / s)) < absf(log(multiplier / best)) - 1e-9:
			best = s
	return best


## Tests pin the setting (true/false) or hand it back to the player (null).
static func force_auto_pause(value: Variant) -> void:
	_forced_auto_pause = -1 if value == null else (1 if bool(value) else 0)


static func force_auto_slow(value: Variant) -> void:
	_forced_auto_slow = -1 if value == null else (1 if bool(value) else 0)


static func force_tutorial(value: Variant) -> void:
	_forced_tutorial = -1 if value == null else (1 if bool(value) else 0)


## "Varsler" (gameplay.notifications_enabled, on by default, U2-08): a
## warning shows its toast and cue. Off, warnings go to the inbox and the
## bell's badge only; critical events still pause on their card.
static func notifications_enabled() -> bool:
	return bool(_gameplay().get("notifications_enabled", true))


## The real minutes between autosaves (gameplay.autosave_minutes: 0 off, 2,
## 5, 10 or 30; U2-08). SaveLoadManager saves at the first day boundary after.
static func autosave_minutes() -> int:
	return maxi(int(_gameplay().get("autosave_minutes", 5)), 0)


## (SettingsStore: the player's file, or a test's own through path_override.)
static func _gameplay() -> Dictionary:
	var gameplay: Variant = SettingsStore.load_raw().get("gameplay", {})
	return gameplay if gameplay is Dictionary else {}
