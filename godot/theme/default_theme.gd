extends Node

## LakskongenTheme — Norwegian-inspired colour palette and UI style.
## Add as autoload "LakskongenTheme" so any node can read palette constants.
## Call apply_to_root() from a scene's _ready() to push the theme project-wide.

# ---------------------------------------------------------------------------
# Norwegian palette
# ---------------------------------------------------------------------------
const OCEAN_BLUE   := Color(0.063, 0.267, 0.463, 1.0)   # #104478 – deep fjord water
const OCEAN_LIGHT  := Color(0.345, 0.612, 0.816, 1.0)   # #589CD0 – sunlit sea surface
const SALMON_PINK  := Color(0.898, 0.447, 0.341, 1.0)   # #E57257 – fresh Atlantic salmon
const SALMON_DARK  := Color(0.694, 0.247, 0.145, 1.0)   # #B13F25 – cured salmon
const FJORD_GREEN  := Color(0.184, 0.392, 0.294, 1.0)   # #2F644B – fjord forest
const FJORD_LIGHT  := Color(0.357, 0.627, 0.467, 1.0)   # #5BA077 – summer grass
const SNOW_WHITE   := Color(0.945, 0.957, 0.976, 1.0)   # #F1F4F9 – mountain snow
const CLOUD_GREY   := Color(0.694, 0.729, 0.776, 1.0)   # #B1BAC6 – overcast sky
const DEEP_SEA     := Color(0.027, 0.078, 0.18, 1.0)    # #07142E – night water
const AMBER_WARN   := Color(0.929, 0.698, 0.169, 1.0)   # #EDB22B – warning buoy
const DANGER_RED   := Color(0.839, 0.180, 0.180, 1.0)   # #D62E2E – danger alert
const SUCCESS_TEAL := Color(0.129, 0.710, 0.561, 1.0)   # #21B58F – healthy fish


# ---------------------------------------------------------------------------
# Semantic aliases
# ---------------------------------------------------------------------------
const COLOR_BACKGROUND  := DEEP_SEA
const COLOR_PANEL       := Color(0.055, 0.145, 0.278, 1.0)   # #0E254B
const COLOR_PANEL_LIGHT := Color(0.094, 0.212, 0.388, 1.0)   # #183663
const COLOR_TEXT        := SNOW_WHITE
const COLOR_TEXT_DIM    := CLOUD_GREY
const COLOR_ACCENT      := OCEAN_LIGHT
const COLOR_PRIMARY     := OCEAN_BLUE
const COLOR_SUCCESS     := SUCCESS_TEAL
const COLOR_WARNING     := AMBER_WARN
const COLOR_DANGER      := DANGER_RED
const COLOR_HIGHLIGHT   := SALMON_PINK


# ---------------------------------------------------------------------------
# Theme construction
# ---------------------------------------------------------------------------
var _theme: Theme

func _ready() -> void:
	_theme = _build_theme()

func get_theme() -> Theme:
	if _theme == null:
		_theme = _build_theme()
	return _theme

func apply_to_root() -> void:
	if _theme == null:
		_theme = _build_theme()
	get_tree().root.theme = _theme

func _build_theme() -> Theme:
	var t := Theme.new()

	# --- Panel ---
	var panel_sb := StyleBoxFlat.new()
	panel_sb.bg_color = COLOR_PANEL
	panel_sb.border_color = OCEAN_BLUE
	panel_sb.set_border_width_all(1)
	panel_sb.set_corner_radius_all(6)
	t.set_stylebox("panel", "Panel", panel_sb)
	t.set_stylebox("panel", "PanelContainer", panel_sb)

	# --- Button normal ---
	var btn_normal := StyleBoxFlat.new()
	btn_normal.bg_color = OCEAN_BLUE
	btn_normal.border_color = OCEAN_LIGHT
	btn_normal.set_border_width_all(1)
	btn_normal.set_corner_radius_all(5)
	btn_normal.set_content_margin_all(10)

	var btn_hover := StyleBoxFlat.new()
	btn_hover.bg_color = OCEAN_LIGHT
	btn_hover.border_color = SNOW_WHITE
	btn_hover.set_border_width_all(1)
	btn_hover.set_corner_radius_all(5)
	btn_hover.set_content_margin_all(10)

	var btn_pressed := StyleBoxFlat.new()
	btn_pressed.bg_color = SALMON_PINK
	btn_pressed.border_color = SALMON_DARK
	btn_pressed.set_border_width_all(1)
	btn_pressed.set_corner_radius_all(5)
	btn_pressed.set_content_margin_all(10)

	var btn_disabled := StyleBoxFlat.new()
	btn_disabled.bg_color = Color(0.15, 0.22, 0.32, 1.0)
	btn_disabled.border_color = CLOUD_GREY
	btn_disabled.set_border_width_all(1)
	btn_disabled.set_corner_radius_all(5)
	btn_disabled.set_content_margin_all(10)

	t.set_stylebox("normal",   "Button", btn_normal)
	t.set_stylebox("hover",    "Button", btn_hover)
	t.set_stylebox("pressed",  "Button", btn_pressed)
	t.set_stylebox("disabled", "Button", btn_disabled)
	t.set_color("font_color",          "Button", SNOW_WHITE)
	t.set_color("font_hover_color",    "Button", DEEP_SEA)
	t.set_color("font_pressed_color",  "Button", SNOW_WHITE)
	t.set_color("font_disabled_color", "Button", CLOUD_GREY)

	# --- Label ---
	t.set_color("font_color", "Label", SNOW_WHITE)

	# --- ProgressBar ---
	var pb_bg := StyleBoxFlat.new()
	pb_bg.bg_color = COLOR_PANEL_LIGHT
	pb_bg.set_corner_radius_all(3)

	var pb_fill := StyleBoxFlat.new()
	pb_fill.bg_color = FJORD_GREEN
	pb_fill.set_corner_radius_all(3)

	t.set_stylebox("background", "ProgressBar", pb_bg)
	t.set_stylebox("fill",       "ProgressBar", pb_fill)

	# --- LineEdit ---
	var le_sb := StyleBoxFlat.new()
	le_sb.bg_color = COLOR_PANEL_LIGHT
	le_sb.border_color = OCEAN_LIGHT
	le_sb.set_border_width_all(1)
	le_sb.set_corner_radius_all(4)
	le_sb.set_content_margin_all(8)
	t.set_stylebox("normal", "LineEdit", le_sb)
	t.set_color("font_color", "LineEdit", SNOW_WHITE)
	t.set_color("caret_color", "LineEdit", SALMON_PINK)

	# --- TabContainer ---
	t.set_color("font_selected_color",   "TabContainer", SNOW_WHITE)
	t.set_color("font_unselected_color", "TabContainer", CLOUD_GREY)

	return t
