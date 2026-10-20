extends Node

## Applies the player's settings (roadmap U2-08: settings that apply).
##
## At launch, and whenever the settings menu applies or closes, every setting
## that lives in an engine object is pushed to it from SettingsStore:
##   audio.*                      the Master / Music / Ambient / SFX bus volumes
##   graphics.resolution/fullscreen/vsync   the window (DisplaySettings)
##   graphics.quality             every live EnvironmentDirector (a fjord
##                                opened later reads it: RenderQuality.load_saved_tier)
##   accessibility.ui_scale       the window's content scale
##   accessibility.high_contrast  LakskongenTheme's high-contrast variant
##   general.language             LocalizationManager
##   gameplay.autosave_minutes    SaveLoadManager's real-time interval
##   gameplay.auto_slow_deadlines the game clock's deadline rule (SimBridge)
## The rest are read where they act, every time (so they need no push):
## gameplay.default_speed (the briefing's start), tutorial_enabled
## (TutorialState, polled every second), auto_pause_critical (the critical
## card), notifications_enabled (the warning toasts) through GameplayPrefs,
## and accessibility.reduced_motion (MotionPrefs).
##
## The window settings apply when the game is launched as the game; a test
## or QA scene launched by path keeps its own window (tests apply them through
## a recording port, DisplaySettings.apply).

signal settings_applied(settings: Dictionary)

## What the last apply set (tests and the debug overlay read it).
var last_applied: Dictionary = {}
## Test seam for the window (null: the real window, DisplaySettings.Port).
var display_port: Object = null

const BUS_KEYS := {"Master": "master", "Music": "music", "Ambient": "ambient", "SFX": "sfx"}


func _ready() -> void:
	apply_all(SettingsStore.load_all(), launched_as_game())


## True when the project runs its own main scene (not a test or QA scene by path).
static func launched_as_game() -> bool:
	var main := str(ProjectSettings.get_setting("application/run/main_scene", ""))
	for arg in OS.get_cmdline_args():
		if arg.ends_with(".tscn") and arg != main:
			return false
	return true


## Push every setting in `settings` (SettingsStore.load_all() shape) to its
## consumer. `with_window` false leaves the window alone.
func apply_all(settings: Dictionary, with_window: bool = true) -> Dictionary:
	var audio: Dictionary = settings.get("audio", {})
	var graphics: Dictionary = settings.get("graphics", {})
	var gameplay: Dictionary = settings.get("gameplay", {})
	var acc: Dictionary = settings.get("accessibility", {})
	var general: Dictionary = settings.get("general", {})
	var out := {}
	out["audio"] = apply_audio(audio)
	if with_window:
		out["window"] = DisplaySettings.apply(graphics, get_window(), display_port)
	out["quality"] = apply_quality(str(graphics.get("quality", "high")))
	out["ui_scale"] = apply_ui_scale(float(acc.get("ui_scale", 1.0)))
	out["high_contrast"] = apply_high_contrast(bool(acc.get("high_contrast", false)))
	out["language"] = apply_language(str(general.get("language", "nb")))
	out["autosave_minutes"] = apply_autosave(int(gameplay.get("autosave_minutes", 5)))
	out["auto_slow_deadlines"] = apply_auto_slow(bool(gameplay.get("auto_slow_deadlines", true)))
	last_applied = out
	settings_applied.emit(settings)
	return out


## Bus volumes (linear 0-1 -> dB; 0 is silence).
func apply_audio(audio: Dictionary) -> Dictionary:
	var set_db := {}
	for bus in BUS_KEYS:
		var idx := AudioServer.get_bus_index(bus)
		if idx < 0:
			continue
		var v := clampf(float(audio.get(BUS_KEYS[bus], 1.0)), 0.0, 1.0)
		var db := -80.0 if v <= 0.0001 else linear_to_db(v)
		AudioServer.set_bus_volume_db(idx, db)
		set_db[bus] = db
	return set_db


## The quality tier, now, on every live fjord.
func apply_quality(tier: String) -> String:
	if RenderQuality.tier_of(tier) < 0:
		tier = RenderQuality.NAMES[RenderQuality.DEFAULT_TIER]
	if is_inside_tree():
		get_tree().call_group(RenderQuality.DIRECTOR_GROUP, "set_quality", tier)
	return tier


func apply_ui_scale(scale: float) -> float:
	var s := scale if scale > 0.0 else 1.0
	var win := get_window()
	if win != null:
		win.content_scale_factor = s
	return s


func apply_high_contrast(on: bool) -> bool:
	var theme := get_node_or_null("/root/LakskongenTheme")
	if theme != null and theme.has_method("set_high_contrast"):
		theme.set_high_contrast(on)
		return bool(theme.is_high_contrast())
	return false


func apply_language(locale: String) -> String:
	var loc := get_node_or_null("/root/LocalizationManager")
	if loc != null and loc.has_method("apply_language"):
		return str(loc.apply_language(locale))
	return ""


## The clock's deadline rule, now (Main also reads it when a game scene starts
## and when the pause menu closes).
func apply_auto_slow(on: bool) -> bool:
	var sim := get_node_or_null("/root/SimBridge")
	if sim != null and sim.has_method("set_auto_slow_deadlines"):
		sim.set_auto_slow_deadlines(on)
	return on


func apply_autosave(minutes: int) -> int:
	var mgr := get_node_or_null("/root/SaveLoadManager")
	var m := maxi(minutes, 0)
	if mgr != null:
		mgr.AutoSaveIntervalMinutes = m
	return m
