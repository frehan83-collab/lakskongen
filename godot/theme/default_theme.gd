
extends Node

## LakskongenTheme — Premium Norwegian Fjord aesthetic.
## Add as autoload "LakskongenTheme" so any node can read palette constants and use helpers.
## Call apply_to_root() from a scene's _ready() to push the theme project-wide.


# ---------------------------------------------------------------------------
# Norwegian Fjord Color Palette
# ---------------------------------------------------------------------------
const BG_DARK           := Color("#0d1b2a") # Deep night ocean
const BG_MEDIUM         := Color("#1a2744") # Ocean blue
const BG_LIGHT          := Color("#243b5e") # Lighter panel blue
const ACCENT_PRIMARY    := Color("#2d8a7e") # Fjord teal
const ACCENT_SECONDARY  := Color("#45b5aa") # Light teal
const WARNING_COLOR     := Color("#e8a838") # Amber
const DANGER_COLOR      := Color("#e87461") # Salmon pink/red
const SUCCESS_COLOR     := Color("#4caf50") # Green
const TEXT_PRIMARY      := Color("#e8e8e8") # Clean white
const TEXT_SECONDARY    := Color("#8899aa") # Muted blue-gray
const TEXT_ACCENT       := Color("#45b5aa") # Teal highlights

# ---------------------------------------------------------------------------
# Font Size Constants
# ---------------------------------------------------------------------------
const FONT_TITLE  := 28
const FONT_HEADER := 22
const FONT_BODY   := 16
const FONT_SMALL  := 12

# ---------------------------------------------------------------------------
# Theme construction and helpers
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

	# --- Default Font --- (Assuming a default font is set in Project Settings or loaded dynamically)
	# var default_font = preload("res://assets/fonts/your_default_font.tres") # Example
	# t.set_font("font", "Label", default_font)
	# t.set_font("font", "Button", default_font)

	# --- Panel Styles ---
	t.set_stylebox("panel", "Panel", get_panel_style())
	t.set_stylebox("panel", "PanelContainer", get_panel_style())

	# --- Button Styles ---
	t.set_stylebox("normal", "Button", get_button_style(BG_MEDIUM, ACCENT_PRIMARY, TEXT_PRIMARY).normal)
	t.set_stylebox("hover", "Button", get_button_style(ACCENT_PRIMARY, ACCENT_SECONDARY, BG_DARK).hover)
	t.set_stylebox("pressed", "Button", get_button_style(ACCENT_SECONDARY, ACCENT_PRIMARY, BG_DARK).pressed)
	t.set_stylebox("disabled", "Button", get_button_style(BG_DARK.lightened(0.1), TEXT_SECONDARY, TEXT_SECONDARY).disabled)

	t.set_color("font_color", "Button", TEXT_PRIMARY)
	t.set_color("font_hover_color", "Button", BG_DARK)
	t.set_color("font_pressed_color", "Button", BG_DARK)
	t.set_color("font_disabled_color", "Button", TEXT_SECONDARY)
	t.set_font_size("font_size", "Button", FONT_BODY)

	# --- Label Styles ---
	t.set_color("font_color", "Label", TEXT_PRIMARY)
	t.set_font_size("font_size", "Label", FONT_BODY)

	# --- Other common controls (optional, extend as needed) ---
	# LineEdit
	var line_edit_style := get_panel_style(BG_LIGHT, ACCENT_PRIMARY, 1, 6, 8) # Smaller radius for inputs
	t.set_stylebox("normal", "LineEdit", line_edit_style)
	t.set_color("font_color", "LineEdit", TEXT_PRIMARY)
	t.set_color("caret_color", "LineEdit", ACCENT_SECONDARY)
	t.set_font_size("font_size", "LineEdit", FONT_BODY)

	# ProgressBar
	var pb_bg := get_panel_style(BG_DARK.lightened(0.1), BG_MEDIUM, 1, 6) # Smaller radius
	var pb_fill := StyleBoxFlat.new()
	pb_fill.bg_color = ACCENT_PRIMARY
	pb_fill.set_corner_radius_all(6)
	t.set_stylebox("background", "ProgressBar", pb_bg)
	t.set_stylebox("fill", "ProgressBar", pb_fill)
	t.set_color("font_color", "ProgressBar", TEXT_PRIMARY)

	# CheckBox
	t.set_color("font_color", "CheckBox", TEXT_PRIMARY)
	t.set_font_size("font_size", "CheckBox", FONT_BODY)

	# OptionButton (Dropdown)
	t.set_color("font_color", "OptionButton", TEXT_PRIMARY)
	t.set_font_size("font_size", "OptionButton", FONT_BODY)
	var op_normal_style := get_button_style(BG_MEDIUM, ACCENT_PRIMARY, TEXT_PRIMARY).normal
	var op_hover_style := get_button_style(ACCENT_PRIMARY, ACCENT_SECONDARY, BG_DARK).hover
	t.set_stylebox("normal", "OptionButton", op_normal_style)
	t.set_stylebox("hover", "OptionButton", op_hover_style)
	t.set_stylebox("pressed", "OptionButton", op_normal_style) # Same as normal for pressed

	return t


# ---------------------------------------------------------------------------
# Static Helper Functions
# ---------------------------------------------------------------------------

static func get_panel_style(bg_color: Color = BG_MEDIUM, border_color: Color = ACCENT_PRIMARY, border_width: int = 1, corner_radius: int = 8, content_margin: int = 10) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg_color
	sb.border_color = border_color
	sb.set_border_width_all(border_width)
	sb.set_corner_radius_all(corner_radius)
	sb.set_content_margin_all(content_margin)
	return sb

static func get_button_style(normal_bg: Color, hover_bg: Color, text_color: Color) -> Dictionary:
	var styles := {}

	# Normal
	var normal_sb := get_panel_style(normal_bg, ACCENT_PRIMARY, 1, 8, 12)
	styles.normal = normal_sb

	# Hover
	var hover_sb := get_panel_style(hover_bg, TEXT_ACCENT, 1, 8, 12)
	styles.hover = hover_sb

	# Pressed
	var pressed_sb := get_panel_style(hover_bg.darkened(0.1), ACCENT_PRIMARY, 1, 8, 12)
	styles.pressed = pressed_sb

	# Disabled
	var disabled_sb := get_panel_style(BG_DARK.lightened(0.1), TEXT_SECONDARY, 1, 8, 12)
	styles.disabled = disabled_sb

	return styles

static func get_label_style(font_size: int = FONT_BODY, font_color: Color = TEXT_PRIMARY) -> Dictionary:
	var label_settings := LabelSettings.new()
	label_settings.font_size = font_size
	label_settings.font_color = font_color
	return {"settings": label_settings}


# ---------------------------------------------------------------------------
# Color-coded Severity Helpers
# ---------------------------------------------------------------------------
static func get_severity_color(level: String) -> Color:
	match level.to_lower():
		"info": return BG_LIGHT # Use a lighter background blue for info
		"warning": return WARNING_COLOR
		"critical": return DANGER_COLOR
		"success": return SUCCESS_COLOR
		_: return TEXT_SECONDARY # Default for unknown levels


# ---------------------------------------------------------------------------
# Example Usage in other scenes:
# var LakskongenTheme = get_node("/root/LakskongenTheme") # If autoloaded
#
# func _ready():
#    # Apply theme globally to the root of the scene tree
#    LakskongenTheme.apply_to_root()
#
#    # Get a specific panel style
#    $MyPanel.add_theme_stylebox_override("panel", LakskongenTheme.get_panel_style())
#
#    # Get a specific button style (e.g., for a custom button)
#    $MyButton.add_theme_stylebox_override("normal", LakskongenTheme.get_button_style(LakskongenTheme.ACCENT_PRIMARY, LakskongenTheme.ACCENT_SECONDARY, LakskongenTheme.TEXT_PRIMARY).normal)
#
#    # Get a label setting (e.g., for a dynamically created label)
#    var my_label = Label.new()
#    my_label.label_settings = LakskongenTheme.get_label_style(LakskongenTheme.FONT_HEADER).settings
#    add_child(my_label)
#
#    # Get a severity color
#    $StatusLabel.modulate = LakskongenTheme.get_severity_color("warning")

