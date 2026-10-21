class_name DisplaySettings
extends RefCounted

## The window settings (roadmap U2-08): graphics.resolution, graphics.fullscreen
## and graphics.vsync from SettingsStore, applied to the game's window.
##
## plan() is the pure rule (tests call it); apply() hands the plan to a port,
## the real window by default (Port) or a test's recording one. Windowed, the
## window takes the chosen size, never larger than the screen's usable area,
## and is centred; fullscreen covers the screen (the size is kept for when the
## player goes back to a window). The UI stretches (canvas_items) either way.

## The sizes the settings menu offers (16:9).
const RESOLUTIONS: Array[String] = ["1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160"]
const DEFAULT_RESOLUTION := Vector2i(1920, 1080)
## The smallest window the UI is laid out for (the HUD fits 1280 px).
const MIN_SIZE := Vector2i(1280, 720)


## The graphics keys that set the window (the quality tier does not).
const WINDOW_KEYS: Array[String] = ["resolution", "fullscreen", "vsync"]


## True when the window settings differ between two graphics sections (a
## missing key reads as its default): the settings menu re-applies the window
## only then, so a player's own move or resize survives a volume change.
static func window_changed(before: Dictionary, after: Dictionary) -> bool:
	var defaults: Dictionary = SettingsStore.DEFAULTS["graphics"]
	for key in WINDOW_KEYS:
		if str(before.get(key, defaults[key])) != str(after.get(key, defaults[key])):
			return true
	return false


## "1920x1080" -> Vector2i; anything else -> the default.
static func parse_resolution(text: Variant) -> Vector2i:
	var parts := str(text).to_lower().split("x")
	if parts.size() != 2 or not parts[0].is_valid_int() or not parts[1].is_valid_int():
		return DEFAULT_RESOLUTION
	var size := Vector2i(int(parts[0]), int(parts[1]))
	return size if size.x >= MIN_SIZE.x and size.y >= MIN_SIZE.y else DEFAULT_RESOLUTION


## What the window should be for these graphics settings on a screen whose
## usable area is `usable` (Vector2i.ZERO: unknown, no clamp).
static func plan(graphics: Dictionary, usable: Vector2i) -> Dictionary:
	var size := parse_resolution(graphics.get("resolution", "1920x1080"))
	if usable.x > 0 and usable.y > 0:
		size = Vector2i(mini(size.x, usable.x), mini(size.y, usable.y))
	return {
		"mode": Window.MODE_FULLSCREEN if bool(graphics.get("fullscreen", false)) else Window.MODE_WINDOWED,
		"size": size,
		"vsync": DisplayServer.VSYNC_ENABLED if bool(graphics.get("vsync", true)) else DisplayServer.VSYNC_DISABLED,
	}


## Apply the graphics settings through `port` (null: the real window).
## Returns the plan it applied.
static func apply(graphics: Dictionary, window: Window, port: Object = null) -> Dictionary:
	var p: Object = port if port != null else Port.new(window)
	var applied := plan(graphics, p.usable_size())
	p.set_vsync(int(applied["vsync"]))
	if int(applied["mode"]) == Window.MODE_WINDOWED:
		p.set_mode(Window.MODE_WINDOWED)
		p.set_size(applied["size"])
		p.center()
	else:
		p.set_mode(int(applied["mode"]))
	return applied


## The real window.
class Port:
	var _window: Window

	func _init(window: Window) -> void:
		_window = window

	func usable_size() -> Vector2i:
		if DisplayServer.get_name() == "headless":
			return Vector2i.ZERO
		return DisplayServer.screen_get_usable_rect(_window.current_screen if _window != null else 0).size

	func set_mode(mode: int) -> void:
		if _window != null and _window.mode != mode:
			_window.mode = mode as Window.Mode

	func set_size(size: Vector2i) -> void:
		if _window != null:
			_window.size = size

	func center() -> void:
		if _window != null and DisplayServer.get_name() != "headless":
			_window.move_to_center()

	func set_vsync(mode: int) -> void:
		DisplayServer.window_set_vsync_mode(mode as DisplayServer.VSyncMode)
