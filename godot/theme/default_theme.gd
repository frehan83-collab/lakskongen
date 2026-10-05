extends Node

## LakskongenTheme v2.0 — Nordic Minimalist / Clean-Tech
## Direction A from Style Seed: Cool teal-blue, Inter font, light theme, data-forward.
## Add as autoload "LakskongenTheme" so any node can read palette constants and use helpers.
## Call apply_to_root() from a scene's _ready() to push the theme project-wide.

# ---------------------------------------------------------------------------
# Nordic Minimalist Color Palette (tokens.json v2.0)
# ---------------------------------------------------------------------------
# Primary — Deep fjord teal
const PRIMARY           := Color("#0A4D68")
const PRIMARY_LIGHT     := Color("#0D5F80")
const PRIMARY_DARK      := Color("#083D53")

# Secondary — Nordic ice blue
const SECONDARY         := Color("#88C0D0")
const SECONDARY_LIGHT   := Color("#A3D1DE")

# Accent — Muted steel blue
const ACCENT            := Color("#5E81AC")
const ACCENT_LIGHT      := Color("#7B9BC0")

# Backgrounds
const BG                := Color("#ECEFF4")   # Light gray-blue
const SURFACE           := Color("#FFFFFF")   # Cards, panels
const SURFACE_ELEVATED  := Color("#F8F9FB")   # Elevated/hover

# Text
const TEXT_PRIMARY      := Color("#2E3440")   # Near-black
const TEXT_SECONDARY    := Color("#4C566A")   # Dark gray
const TEXT_MUTED        := Color("#7B8794")   # Light gray
const TEXT_INVERSE      := Color("#ECEFF4")   # White-ish (on dark bg)

# Borders
const BORDER            := Color("#D8DEE9")
const BORDER_LIGHT      := Color("#E5E9F0")

# Status
const SUCCESS           := Color("#A3BE8C")
const SUCCESS_DARK      := Color("#8FAD78")
const WARNING           := Color("#EBCB8B")
const WARNING_DARK      := Color("#D4B06A")
const DANGER            := Color("#BF616A")
const DANGER_DARK       := Color("#A54E56")
const INFO              := Color("#88C0D0")

# Domain-specific
const WATER_BLUE        := Color("#5B8FA3")
const SALMON_PINK       := Color("#E5A09D")

# ----------- Legacy aliases (backward compat for old scripts) ---------------
const BG_DARK           := Color("#2E3440")
const BG_MEDIUM         := Color("#3B4252")
const BG_LIGHT          := Color("#4C566A")
const ACCENT_PRIMARY    := Color("#0A4D68")
const ACCENT_SECONDARY  := Color("#88C0D0")
const WARNING_COLOR     := Color("#EBCB8B")
const DANGER_COLOR      := Color("#BF616A")
const SUCCESS_COLOR     := Color("#A3BE8C")
const TEXT_ACCENT       := Color("#88C0D0")

# ---------------------------------------------------------------------------
# Font Size Constants
# ---------------------------------------------------------------------------
const FONT_XS     := 11
const FONT_SM     := 13
const FONT_BODY   := 15
const FONT_LG     := 18
const FONT_HEADER := 22
const FONT_TITLE  := 28
const FONT_HERO   := 48
const FONT_SMALL  := 12  # Legacy alias

# ---------------------------------------------------------------------------
# Spacing & Radius Constants
# ---------------------------------------------------------------------------
const SPACING_XS  := 4
const SPACING_SM  := 8
const SPACING_MD  := 16
const SPACING_LG  := 24
const SPACING_XL  := 32

const RADIUS_SM   := 4
const RADIUS_MD   := 8
const RADIUS_LG   := 12

# ---------------------------------------------------------------------------
# Font Resources
# ---------------------------------------------------------------------------
var font_heading: Font = null
var font_heading_bold: Font = null
var font_heading_semibold: Font = null
var font_body: Font = null
var font_mono: Font = null

# ---------------------------------------------------------------------------
# Theme construction
# ---------------------------------------------------------------------------
var _theme: Theme

func _ready() -> void:
	_load_fonts()
	_theme = _build_theme()

func _load_fonts() -> void:
	# Inter for headings
	if ResourceLoader.exists("res://assets/fonts/Inter-Regular.otf"):
		font_heading = load("res://assets/fonts/Inter-Regular.otf")
	if ResourceLoader.exists("res://assets/fonts/Inter-Bold.otf"):
		font_heading_bold = load("res://assets/fonts/Inter-Bold.otf")
	if ResourceLoader.exists("res://assets/fonts/Inter-SemiBold.otf"):
		font_heading_semibold = load("res://assets/fonts/Inter-SemiBold.otf")
	# Body — use Inter Medium if no IBM Plex Sans
	if ResourceLoader.exists("res://assets/fonts/IBMPlexSans-Regular.ttf"):
		font_body = load("res://assets/fonts/IBMPlexSans-Regular.ttf")
	elif ResourceLoader.exists("res://assets/fonts/Inter-Medium.otf"):
		font_body = load("res://assets/fonts/Inter-Medium.otf")
	# Mono
	if ResourceLoader.exists("res://assets/fonts/JetBrainsMono-Regular.ttf"):
		font_mono = load("res://assets/fonts/JetBrainsMono-Regular.ttf")
	
	if font_heading:
		print("LakskongenTheme: Fonts loaded (Inter + JetBrains Mono)")
	else:
		push_warning("LakskongenTheme: Custom fonts not found — using Godot defaults")

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

	# Default font
	if font_body:
		t.set_default_font(font_body)
	t.set_default_font_size(FONT_BODY)

	# --- Panel Styles (light background) ---
	var panel_sb := StyleBoxFlat.new()
	panel_sb.bg_color = SURFACE
	panel_sb.border_color = BORDER_LIGHT
	panel_sb.set_border_width_all(1)
	panel_sb.set_corner_radius_all(RADIUS_MD)
	panel_sb.set_content_margin_all(SPACING_MD)
	t.set_stylebox("panel", "Panel", panel_sb)
	t.set_stylebox("panel", "PanelContainer", panel_sb)

	# --- Button Styles ---
	# Normal
	var btn_normal := StyleBoxFlat.new()
	btn_normal.bg_color = PRIMARY
	btn_normal.set_corner_radius_all(RADIUS_SM)
	btn_normal.set_content_margin_all(12)
	t.set_stylebox("normal", "Button", btn_normal)
	
	# Hover
	var btn_hover := StyleBoxFlat.new()
	btn_hover.bg_color = PRIMARY_LIGHT
	btn_hover.set_corner_radius_all(RADIUS_SM)
	btn_hover.set_content_margin_all(12)
	t.set_stylebox("hover", "Button", btn_hover)
	
	# Pressed
	var btn_pressed := StyleBoxFlat.new()
	btn_pressed.bg_color = PRIMARY_DARK
	btn_pressed.set_corner_radius_all(RADIUS_SM)
	btn_pressed.set_content_margin_all(12)
	t.set_stylebox("pressed", "Button", btn_pressed)
	
	# Disabled
	var btn_disabled := StyleBoxFlat.new()
	btn_disabled.bg_color = BORDER
	btn_disabled.set_corner_radius_all(RADIUS_SM)
	btn_disabled.set_content_margin_all(12)
	t.set_stylebox("disabled", "Button", btn_disabled)

	t.set_color("font_color", "Button", TEXT_INVERSE)
	t.set_color("font_hover_color", "Button", TEXT_INVERSE)
	t.set_color("font_pressed_color", "Button", TEXT_INVERSE)
	t.set_color("font_disabled_color", "Button", TEXT_MUTED)
	t.set_font_size("font_size", "Button", FONT_SM)
	if font_heading_semibold:
		t.set_font("font", "Button", font_heading_semibold)

	# --- Label Styles ---
	t.set_color("font_color", "Label", TEXT_PRIMARY)
	t.set_font_size("font_size", "Label", FONT_BODY)

	# --- LineEdit ---
	var line_edit_sb := StyleBoxFlat.new()
	line_edit_sb.bg_color = SURFACE
	line_edit_sb.border_color = BORDER
	line_edit_sb.set_border_width_all(1)
	line_edit_sb.set_corner_radius_all(RADIUS_SM)
	line_edit_sb.set_content_margin_all(8)
	t.set_stylebox("normal", "LineEdit", line_edit_sb)
	t.set_color("font_color", "LineEdit", TEXT_PRIMARY)
	t.set_color("caret_color", "LineEdit", PRIMARY)
	t.set_font_size("font_size", "LineEdit", FONT_BODY)

	# --- ProgressBar ---
	var pb_bg := StyleBoxFlat.new()
	pb_bg.bg_color = BORDER_LIGHT
	pb_bg.set_corner_radius_all(RADIUS_SM)
	var pb_fill := StyleBoxFlat.new()
	pb_fill.bg_color = PRIMARY
	pb_fill.set_corner_radius_all(RADIUS_SM)
	t.set_stylebox("background", "ProgressBar", pb_bg)
	t.set_stylebox("fill", "ProgressBar", pb_fill)
	t.set_color("font_color", "ProgressBar", TEXT_PRIMARY)

	# --- CheckBox / OptionButton ---
	t.set_color("font_color", "CheckBox", TEXT_PRIMARY)
	t.set_color("font_color", "OptionButton", TEXT_PRIMARY)
	t.set_font_size("font_size", "CheckBox", FONT_BODY)
	t.set_font_size("font_size", "OptionButton", FONT_BODY)

	# --- HSeparator ---
	var sep_sb := StyleBoxFlat.new()
	sep_sb.bg_color = BORDER_LIGHT
	sep_sb.set_content_margin(SIDE_TOP, 4)
	sep_sb.set_content_margin(SIDE_BOTTOM, 4)
	t.set_stylebox("separator", "HSeparator", sep_sb)

	# --- ScrollContainer ---
	t.set_color("font_color", "ScrollContainer", TEXT_PRIMARY)

	return t

# ---------------------------------------------------------------------------
# Helper Functions
# ---------------------------------------------------------------------------

## Get a styled panel StyleBox with configurable colors
static func make_panel(bg: Color = SURFACE, border: Color = BORDER_LIGHT, border_w: int = 1, radius: int = RADIUS_MD, margin: int = SPACING_MD) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg
	sb.border_color = border
	sb.set_border_width_all(border_w)
	sb.set_corner_radius_all(radius)
	sb.set_content_margin_all(margin)
	return sb

## Get a KPI card panel (white with colored top stripe)
static func make_kpi_panel(stripe_color: Color = PRIMARY) -> StyleBoxFlat:
	var sb := make_panel()
	sb.border_color = BORDER_LIGHT
	sb.set_border_width_all(1)
	# Top stripe via border
	sb.border_width_top = 4
	sb.border_color = stripe_color
	return sb

## Status colors helper
static func status_color(level: String) -> Color:
	match level.to_lower():
		"healthy", "good", "success": return SUCCESS
		"warning", "elevated": return WARNING
		"critical", "danger", "bad": return DANGER
		"info": return INFO
		_: return TEXT_MUTED

## Balance color (financial)
static func balance_color(amount: float) -> Color:
	if amount > 0:
		return SUCCESS_DARK
	elif amount < 0:
		return DANGER
	return TEXT_PRIMARY

# Legacy compat
static func get_panel_style(bg_color: Color = SURFACE, border_color: Color = BORDER_LIGHT, border_width: int = 1, corner_radius: int = 8, content_margin: int = 10) -> StyleBoxFlat:
	return make_panel(bg_color, border_color, border_width, corner_radius, content_margin)

static func get_severity_color(level: String) -> Color:
	return status_color(level)

static func get_button_style(normal_bg: Color, hover_bg: Color, text_color: Color) -> Dictionary:
	var styles := {}
	var normal_sb := make_panel(normal_bg, PRIMARY, 1, RADIUS_SM, 12)
	styles.normal = normal_sb
	var hover_sb := make_panel(hover_bg, SECONDARY, 1, RADIUS_SM, 12)
	styles.hover = hover_sb
	var pressed_sb := make_panel(hover_bg.darkened(0.1), PRIMARY, 1, RADIUS_SM, 12)
	styles.pressed = pressed_sb
	var disabled_sb := make_panel(BORDER, TEXT_MUTED, 1, RADIUS_SM, 12)
	styles.disabled = disabled_sb
	return styles

static func get_label_style(font_size: int = FONT_BODY, font_color: Color = TEXT_PRIMARY) -> Dictionary:
	var label_settings := LabelSettings.new()
	label_settings.font_size = font_size
	label_settings.font_color = font_color
	return {"settings": label_settings}

# ── Panel/UI compatibility layer ──────────────────────────────────────────
# Added to satisfy the UI scripts built against the LakskongenTheme API
# (dark-fjord panels, SPACE_* rhythm, create_* builders, formatters).
# Palette values are tunable; semantics below are what callers rely on.

# Dark panel surfaces (UI is dark; light BG_* above are legacy)
const PANEL            := Color("#3B4252")
const PANEL_ALT        := Color("#434C5E")
const PANEL_HOVER      := Color("#4C566A")
# Text on dark surfaces
const TEXT             := Color("#ECEFF4")
const MUTED            := Color("#7B8794")
# Status accents (DANGER/WARNING/SUCCESS already declared above)
const GOLD             := Color("#EBCB8B")
const TEAL             := Color("#88C0D0")
const KELP             := Color("#8FBC8F")
const KELP_LIGHT       := Color("#A3BE8C")
const AURORA_GREEN     := Color("#A3BE8C")
const AURORA_PURPLE    := Color("#B48EAD")
const ACCENT2          := Color("#81A1C1")
const COLOR_BACKGROUND_DARK := Color("#2E3440")
const COLOR_GREEN      := Color("#A3BE8C")
const COLOR_RED        := Color("#BF616A")
const COLOR_YELLOW     := Color("#EBCB8B")
# Font sizes (px); FONT_* are ints everywhere they are used as sizes.
# (FONT_XS/SM/BODY/LG/HEADER/TITLE/HERO/SMALL already declared above.)
const FONT_MD    := 15
const FONT_XL    := 24
const FONT_XXL   := 32
# Spacing rhythm aliases
const SPACE_XS := SPACING_XS
const SPACE_SM := SPACING_SM
const SPACE_MD := SPACING_MD
const SPACE_LG := SPACING_LG
const SPACE_XL := SPACING_XL

static func create_styled_panel() -> PanelContainer:
	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", make_panel(PANEL, BORDER, 1, RADIUS_MD, SPACING_MD))
	return panel

static func create_label(text: String, font_size: int = FONT_BODY, color: Color = TEXT_PRIMARY) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", font_size)
	label.add_theme_color_override("font_color", color)
	return label

static func create_section_header(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.add_theme_font_size_override("font_size", FONT_LG)
	label.add_theme_color_override("font_color", ACCENT)
	return label

static func create_divider(color: Color = BORDER) -> HSeparator:
	var sep := HSeparator.new()
	sep.add_theme_constant_override("separation", SPACING_SM)
	var style := StyleBoxFlat.new()
	style.bg_color = color
	sep.add_theme_stylebox_override("separator", style)
	return sep

static func create_stat(label_text: String, value_text: String, color: Color = TEXT_PRIMARY) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", SPACING_SM)
	var name_label := create_label(label_text, FONT_SM, TEXT_SECONDARY)
	var value_label := create_label(value_text, FONT_SM, color)
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(name_label)
	row.add_child(value_label)
	return row

static func create_button(text: String, a: Variant = true, b: Variant = false) -> Button:
	# Two historical call shapes:
	#   (text, primary: bool, danger: bool)
	#   (text, font_size: int, color: Color)
	var btn := Button.new()
	btn.text = text
	var primary := true
	var danger := false
	var font_size: int = FONT_MD
	var color: Color = ACCENT
	if a is bool:
		primary = a
		if b is bool:
			danger = b
	elif a is int:
		font_size = a
		if b is Color:
			color = b
	var bg := PRIMARY if primary else BORDER
	if danger:
		bg = DANGER
	btn.add_theme_font_size_override("font_size", font_size)
	var styles := get_button_style(bg, bg.lightened(0.1), TEXT_INVERSE if primary or danger else TEXT_PRIMARY)
	for state_name in styles:
		btn.add_theme_stylebox_override(state_name, styles[state_name])
	btn.add_theme_color_override("font_color", color if not primary and not danger else TEXT_INVERSE)
	btn.add_theme_color_override("font_hover_color", color if not primary and not danger else TEXT_INVERSE)
	return btn

static func format_number(value: Variant, decimals: int = 0) -> String:
	var num := float(value)
	if decimals <= 0:
		return "%d" % int(round(num))
	return ("%." + str(decimals) + "f") % num

static func format_currency(value: Variant, decimals: int = 2) -> String:
	return format_number(value, decimals) + " kr"
