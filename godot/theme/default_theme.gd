extends Node

## LakskongenTheme v2.0 — Nordic Minimalist / Clean-Tech
## Direction A from Style Seed: Cool teal-blue, Inter font, light theme, data-forward.
## Add as autoload "LakskongenTheme" so any node can read palette constants and use helpers.
## Call apply_to_root() from a scene's _ready() to push the theme project-wide.
##
## The palette/spacing/radius/font-size values are DERIVED at runtime from
## res://theme/tokens.json (synced from design/tokens.json by tools/sync_tokens.py).
## The public consts below are the compile-time fallback + stable public API for
## static helpers and external callers; the live `_`-prefixed fields are populated
## from tokens.json and consumed by _build_theme().

# ---------------------------------------------------------------------------
# Nordic Minimalist Color Palette (tokens.json v2.0) — fallback / public API
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
# Design tokens — runtime source of truth
# ---------------------------------------------------------------------------
# tools/sync_tokens.py copies design/tokens.json -> res://theme/tokens.json.
# _load_tokens() reads that file into the live working copies below and
# _build_theme() consumes THEM, so an art-token change flows into the runtime
# UI without editing this script. The public consts stay as fallback + API.
const TOKENS_PATH := "res://theme/tokens.json"

var _tokens_loaded := false
var _tokens_source := "embedded fallback (const palette)"

# Live palette — mirrored from tokens.json (defaults = compile-time consts)
var _primary: Color = PRIMARY
var _primary_light: Color = PRIMARY_LIGHT
var _primary_dark: Color = PRIMARY_DARK
var _secondary: Color = SECONDARY
var _accent: Color = ACCENT
var _surface: Color = SURFACE
var _surface_elevated: Color = SURFACE_ELEVATED
var _text_primary: Color = TEXT_PRIMARY
var _text_secondary: Color = TEXT_SECONDARY
var _text_muted: Color = TEXT_MUTED
var _text_inverse: Color = TEXT_INVERSE
var _border: Color = BORDER
var _border_light: Color = BORDER_LIGHT
var _success: Color = SUCCESS
var _success_dark: Color = SUCCESS_DARK
var _danger: Color = DANGER
var _danger_dark: Color = DANGER_DARK

# Live spacing / radius / font sizes
var _spacing_sm: int = SPACING_SM
var _spacing_md: int = SPACING_MD
var _radius_sm: int = RADIUS_SM
var _radius_md: int = RADIUS_MD
var _font_xs: int = FONT_XS
var _font_sm: int = FONT_SM
var _font_body: int = FONT_BODY
var _font_header: int = FONT_HEADER  # no token in v2.0; kept as scale fallback

# ---------------------------------------------------------------------------
# Theme construction
# ---------------------------------------------------------------------------
var _theme: Theme

func _ready() -> void:
	_load_fonts()
	_load_tokens()
	_theme = _build_theme()
	# Push the theme project-wide so every Control inherits consistent styling.
	# Local overrides still win, so panels that already tint themselves keep their look.
	if get_tree() != null and get_tree().root != null:
		get_tree().root.theme = _theme

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

	# Emoji fallback: Inter/JetBrains have no emoji glyphs, so labels using emoji
	# (tab bar, HUD) render tofu. Attach the OS emoji font as a fallback chain.
	var emoji := SystemFont.new()
	emoji.font_names = PackedStringArray([
		"Apple Color Emoji", "Noto Color Emoji", "Segoe UI Emoji",
		"Android Emoji", "EmojiOne Color"])
	emoji.allow_system_fallback = true
	for f in [font_heading, font_heading_bold, font_heading_semibold, font_body, font_mono]:
		if f != null:
			f.fallbacks = [emoji]

	if font_heading:
		print("LakskongenTheme: Fonts loaded (Inter + JetBrains Mono + emoji fallback)")
	else:
		push_warning("LakskongenTheme: Custom fonts not found — using Godot defaults")

# ---------------------------------------------------------------------------
# Design-token loading
# ---------------------------------------------------------------------------

## Re-read res://theme/tokens.json and rebuild the project theme.
func reload_tokens() -> void:
	_tokens_loaded = false
	_load_tokens()
	_theme = _build_theme()
	if get_tree() != null and get_tree().root != null:
		get_tree().root.theme = _theme
	print("LakskongenTheme: theme reloaded — source: %s" % _tokens_source)

func _ensure_tokens() -> void:
	if not _tokens_loaded:
		_load_tokens()

func _load_tokens() -> void:
	var tokens := _read_tokens()
	if tokens.is_empty():
		_tokens_source = "embedded fallback (const palette)"
		push_warning("LakskongenTheme: %s unavailable — using embedded fallback palette. Run: python3 tools/sync_tokens.py" % TOKENS_PATH)
		_tokens_loaded = true
		return

	var colors: Dictionary = tokens.get("colors", {})
	var spacing: Dictionary = tokens.get("spacing", {})
	var radii: Dictionary = tokens.get("radii", {})
	var fonts: Dictionary = tokens.get("fonts", {})
	var sizes: Dictionary = fonts.get("sizes", {})

	# --- colors ---
	_primary          = _token_color(colors, "primary", _primary)
	_primary_light    = _token_color(colors, "primary-light", _primary_light)
	_primary_dark     = _token_color(colors, "primary-dark", _primary_dark)
	_secondary        = _token_color(colors, "secondary", _secondary)
	_accent           = _token_color(colors, "accent", _accent)
	_surface          = _token_color(colors, "surface", _surface)
	_surface_elevated = _token_color(colors, "surface-elevated", _surface_elevated)
	_text_primary     = _token_color(colors, "text", _text_primary)
	_text_secondary   = _token_color(colors, "text-secondary", _text_secondary)
	_text_muted       = _token_color(colors, "text-muted", _text_muted)
	_text_inverse     = _token_color(colors, "text-inverse", _text_inverse)
	_border           = _token_color(colors, "border", _border)
	_border_light     = _token_color(colors, "border-light", _border_light)
	_success          = _token_color(colors, "success", _success)
	_success_dark     = _token_color(colors, "success-dark", _success_dark)
	_danger           = _token_color(colors, "danger", _danger)
	_danger_dark      = _token_color(colors, "danger-dark", _danger_dark)

	# --- spacing ---
	_spacing_sm = _token_int(spacing, "sm", _spacing_sm)
	_spacing_md = _token_int(spacing, "md", _spacing_md)

	# --- radii ---
	_radius_sm = _token_int(radii, "sm", _radius_sm)
	_radius_md = _token_int(radii, "md", _radius_md)

	# --- font sizes ---
	_font_xs   = _token_int(sizes, "xs", _font_xs)
	_font_sm   = _token_int(sizes, "sm", _font_sm)
	_font_body = _token_int(sizes, "md", _font_body)

	_tokens_source = TOKENS_PATH
	_tokens_loaded = true
	print("LakskongenTheme: theme derived from %s (tokens v%s, '%s')" % [
		TOKENS_PATH,
		str(tokens.get("version", "?")),
		str(tokens.get("theme", "?")),
	])

func _read_tokens() -> Dictionary:
	if not FileAccess.file_exists(TOKENS_PATH):
		return {}
	var f := FileAccess.open(TOKENS_PATH, FileAccess.READ)
	if f == null:
		return {}
	var text := f.get_as_text()
	f.close()
	var parsed: Variant = JSON.parse_string(text)
	if parsed is Dictionary:
		return parsed as Dictionary
	push_warning("LakskongenTheme: invalid JSON in %s" % TOKENS_PATH)
	return {}

func _token_color(src: Dictionary, key: String, fallback: Color) -> Color:
	if not src.has(key):
		return fallback
	var raw: Variant = src[key]
	if raw is String and not (raw as String).is_empty():
		return Color(raw as String)
	return fallback

func _token_int(src: Dictionary, key: String, fallback: int) -> int:
	if not src.has(key):
		return fallback
	var raw: Variant = src[key]
	if raw is int or raw is float:
		return int(raw)
	if raw is String and (raw as String).is_valid_int():
		return int(raw as String)
	return fallback

func get_theme() -> Theme:
	_ensure_tokens()
	if _theme == null:
		_theme = _build_theme()
	return _theme

func apply_to_root() -> void:
	_ensure_tokens()
	if _theme == null:
		_theme = _build_theme()
	get_tree().root.theme = _theme

func _build_theme() -> Theme:
	var t := Theme.new()

	# Default font
	if font_body:
		t.set_default_font(font_body)
	t.set_default_font_size(_font_body)

	# ═══════════════════════════════════════════════════════════════════════
	# PANELS — soft surface, subtle border, gentle drop shadow
	# ═══════════════════════════════════════════════════════════════════════
	var panel_sb := _make_card(_surface, _border_light, _radius_md, _spacing_md)
	t.set_stylebox("panel", "Panel", panel_sb)
	t.set_stylebox("panel", "PanelContainer", panel_sb)

	# ═══════════════════════════════════════════════════════════════════════
	# BUTTONS — base (primary call-to-action), with focus ring
	# ═══════════════════════════════════════════════════════════════════════
	t.set_stylebox("normal", "Button", _make_button_style(_primary))
	t.set_stylebox("hover", "Button", _make_button_style(_primary_light))
	t.set_stylebox("pressed", "Button", _make_button_style(_primary_dark))
	t.set_stylebox("disabled", "Button", _make_button_style(_border))
	t.set_stylebox("focus", "Button", _make_focus_ring(_accent))
	t.set_color("font_color", "Button", _text_inverse)
	t.set_color("font_hover_color", "Button", _text_inverse)
	t.set_color("font_pressed_color", "Button", _text_inverse)
	t.set_color("font_disabled_color", "Button", _text_muted)
	t.set_font_size("font_size", "Button", _font_sm)
	if font_heading_semibold:
		t.set_font("font", "Button", font_heading_semibold)

	# ═══════════════════════════════════════════════════════════════════════
	# LABELS
	# ═══════════════════════════════════════════════════════════════════════
	t.set_color("font_color", "Label", _text_primary)
	t.set_font_size("font_size", "Label", _font_body)

	# ═══════════════════════════════════════════════════════════════════════
	# INPUTS — LineEdit / TextEdit / SpinBox
	# ═══════════════════════════════════════════════════════════════════════
	var input_sb := StyleBoxFlat.new()
	input_sb.bg_color = _surface
	input_sb.border_color = _border
	input_sb.set_border_width_all(1)
	input_sb.set_corner_radius_all(_radius_sm)
	input_sb.set_content_margin_all(8)
	t.set_stylebox("normal", "LineEdit", input_sb)
	t.set_stylebox("focus", "LineEdit", _make_focus_ring(_accent, input_sb))
	t.set_color("font_color", "LineEdit", _text_primary)
	t.set_color("font_placeholder_color", "LineEdit", _text_muted)
	t.set_color("caret_color", "LineEdit", _primary)
	t.set_color("selection_color", "LineEdit", _secondary)
	t.set_font_size("font_size", "LineEdit", _font_body)

	t.set_stylebox("normal", "TextEdit", input_sb)
	t.set_stylebox("focus", "TextEdit", _make_focus_ring(_accent, input_sb))
	t.set_color("font_color", "TextEdit", _text_primary)
	t.set_color("caret_color", "TextEdit", _primary)
	t.set_font_size("font_size", "TextEdit", _font_body)

	# ═══════════════════════════════════════════════════════════════════════
	# OPTION BUTTON (dropdown)
	# ═══════════════════════════════════════════════════════════════════════
	t.set_stylebox("normal", "OptionButton", input_sb)
	t.set_stylebox("hover", "OptionButton", _make_button_style(_surface_elevated))
	t.set_stylebox("pressed", "OptionButton", _make_button_style(_secondary))
	t.set_stylebox("focus", "OptionButton", _make_focus_ring(_accent, input_sb))
	t.set_color("font_color", "OptionButton", _text_primary)
	t.set_color("font_hover_color", "OptionButton", _text_primary)
	t.set_color("font_pressed_color", "OptionButton", _text_primary)
	t.set_font_size("font_size", "OptionButton", _font_body)

	# PopupMenu (dropdown list)
	var popup_sb := _make_card(_surface, _border, _radius_sm, _spacing_sm)
	t.set_stylebox("panel", "PopupMenu", popup_sb)
	t.set_stylebox("hover", "PopupMenu", _make_button_style(_secondary))
	t.set_color("font_color", "PopupMenu", _text_primary)
	t.set_color("font_hover_color", "PopupMenu", _text_primary)
	t.set_font_size("font_size", "PopupMenu", _font_body)

	# ═══════════════════════════════════════════════════════════════════════
	# PROGRESS BAR — rounded track + fill
	# ═══════════════════════════════════════════════════════════════════════
	var pb_bg := StyleBoxFlat.new()
	pb_bg.bg_color = _border_light
	pb_bg.set_corner_radius_all(_radius_sm)
	var pb_fill := StyleBoxFlat.new()
	pb_fill.bg_color = _primary
	pb_fill.set_corner_radius_all(_radius_sm)
	t.set_stylebox("background", "ProgressBar", pb_bg)
	t.set_stylebox("fill", "ProgressBar", pb_fill)
	t.set_color("font_color", "ProgressBar", _text_primary)
	t.set_font_size("font_size", "ProgressBar", _font_xs)

	# ═══════════════════════════════════════════════════════════════════════
	# CHECKBOX / RADIO
	# ═══════════════════════════════════════════════════════════════════════
	t.set_color("font_color", "CheckBox", _text_primary)
	t.set_color("font_hover_color", "CheckBox", _text_primary)
	t.set_color("font_pressed_color", "CheckBox", _text_primary)
	t.set_font_size("font_size", "CheckBox", _font_body)
	t.set_stylebox("focus", "CheckBox", _make_focus_ring(_accent))

	# ═══════════════════════════════════════════════════════════════════════
	# TAB CONTAINER — clean tabs with accent underline on active
	# ═══════════════════════════════════════════════════════════════════════
	var tab_selected := StyleBoxFlat.new()
	tab_selected.bg_color = _surface
	tab_selected.border_color = _primary
	tab_selected.border_width_bottom = 3
	tab_selected.set_corner_radius_all(_radius_sm)
	tab_selected.content_margin_left = _spacing_md
	tab_selected.content_margin_right = _spacing_md
	tab_selected.content_margin_top = _spacing_sm
	tab_selected.content_margin_bottom = _spacing_sm
	var tab_unselected := StyleBoxFlat.new()
	tab_unselected.bg_color = _surface_elevated
	tab_unselected.set_corner_radius_all(_radius_sm)
	tab_unselected.content_margin_left = _spacing_md
	tab_unselected.content_margin_right = _spacing_md
	tab_unselected.content_margin_top = _spacing_sm
	tab_unselected.content_margin_bottom = _spacing_sm
	var tab_hover := StyleBoxFlat.new()
	tab_hover.bg_color = _secondary
	tab_hover.set_corner_radius_all(_radius_sm)
	tab_hover.content_margin_left = _spacing_md
	tab_hover.content_margin_right = _spacing_md
	tab_hover.content_margin_top = _spacing_sm
	tab_hover.content_margin_bottom = _spacing_sm
	t.set_stylebox("tab_selected", "TabContainer", tab_selected)
	t.set_stylebox("tab_unselected", "TabContainer", tab_unselected)
	t.set_stylebox("tab_hovered", "TabContainer", tab_hover)
	t.set_stylebox("panel", "TabContainer", _make_card(_surface, _border_light, _radius_md, _spacing_md))
	t.set_color("font_selected_color", "TabContainer", _primary)
	t.set_color("font_unselected_color", "TabContainer", _text_secondary)
	t.set_color("font_hovered_color", "TabContainer", _text_primary)
	t.set_font_size("font_size", "TabContainer", _font_sm)

	# ═══════════════════════════════════════════════════════════════════════
	# SEPARATORS
	# ═══════════════════════════════════════════════════════════════════════
	var hsep := StyleBoxFlat.new()
	hsep.bg_color = _border_light
	hsep.content_margin_top = 4
	hsep.content_margin_bottom = 4
	t.set_stylebox("separator", "HSeparator", hsep)
	var vsep := StyleBoxFlat.new()
	vsep.bg_color = _border_light
	vsep.content_margin_left = 4
	vsep.content_margin_right = 4
	t.set_stylebox("separator", "VSeparator", vsep)

	# ═══════════════════════════════════════════════════════════════════════
	# SCROLLBARS — slim, rounded, unobtrusive
	# ═══════════════════════════════════════════════════════════════════════
	var scroll_bg := StyleBoxFlat.new()
	scroll_bg.bg_color = Color(0, 0, 0, 0) # transparent track
	t.set_stylebox("scroll", "VScrollBar", scroll_bg)
	var scroll_grab := StyleBoxFlat.new()
	scroll_grab.bg_color = _border
	scroll_grab.set_corner_radius_all(_radius_sm)
	scroll_grab.content_margin_left = 3
	scroll_grab.content_margin_right = 3
	t.set_stylebox("grabber", "VScrollBar", scroll_grab)
	var scroll_grab_hl := StyleBoxFlat.new()
	scroll_grab_hl.bg_color = _text_muted
	scroll_grab_hl.set_corner_radius_all(_radius_sm)
	t.set_stylebox("grabber_highlight", "VScrollBar", scroll_grab_hl)
	t.set_stylebox("grabber_pressed", "VScrollBar", scroll_grab_hl)

	# ═══════════════════════════════════════════════════════════════════════
	# SCROLL CONTAINER / TREE / ITEMLIST
	# ═══════════════════════════════════════════════════════════════════════
	t.set_color("font_color", "ScrollContainer", _text_primary)
	t.set_stylebox("panel", "Tree", _make_card(_surface, _border_light, _radius_sm, _spacing_sm))
	t.set_color("font_color", "Tree", _text_primary)
	t.set_color("font_selected_color", "Tree", _text_inverse)
	t.set_stylebox("selected", "Tree", _make_button_style(_primary))
	t.set_font_size("font_size", "Tree", _font_sm)
	t.set_stylebox("panel", "ItemList", _make_card(_surface, _border_light, _radius_sm, _spacing_sm))
	t.set_color("font_color", "ItemList", _text_primary)
	t.set_stylebox("selected", "ItemList", _make_button_style(_secondary))
	t.set_font_size("font_size", "ItemList", _font_sm)

	# ═══════════════════════════════════════════════════════════════════════
	# TOOLTIP / RICH TEXT
	# ═══════════════════════════════════════════════════════════════════════
	var tooltip_sb := _make_card(_primary_dark, _primary, _radius_sm, _spacing_sm)
	t.set_stylebox("panel", "TooltipPanel", tooltip_sb)
	t.set_color("font_color", "TooltipLabel", _text_inverse)
	t.set_font_size("font_size", "TooltipLabel", _font_xs)
	t.set_color("default_color", "RichTextLabel", _text_primary)
	t.set_font_size("normal_font_size", "RichTextLabel", _font_body)

	# ═══════════════════════════════════════════════════════════════════════
	# TYPE VARIATIONS — semantic buttons (use via theme_type_variation)
	# ═══════════════════════════════════════════════════════════════════════
	_register_type_variations(t)

	return t


## Register semantic Theme Type Variations so UI can request
## `theme_type_variation = "PrimaryButton"` etc. without local overrides.
func _register_type_variations(t: Theme) -> void:
	# SecondaryButton — neutral surface button for less prominent actions.
	t.set_type_variation("SecondaryButton", "Button")
	t.set_stylebox("normal", "SecondaryButton", _make_button_style(_surface, _border))
	t.set_stylebox("hover", "SecondaryButton", _make_button_style(_surface_elevated, _accent))
	t.set_stylebox("pressed", "SecondaryButton", _make_button_style(_border, _accent))
	t.set_stylebox("disabled", "SecondaryButton", _make_button_style(_surface_elevated, _border_light))
	t.set_stylebox("focus", "SecondaryButton", _make_focus_ring(_accent))
	t.set_color("font_color", "SecondaryButton", _text_primary)
	t.set_color("font_hover_color", "SecondaryButton", _text_primary)
	t.set_color("font_pressed_color", "SecondaryButton", _text_primary)
	t.set_color("font_disabled_color", "SecondaryButton", _text_muted)

	# DangerButton — destructive actions (sell, demolish, emergency).
	t.set_type_variation("DangerButton", "Button")
	t.set_stylebox("normal", "DangerButton", _make_button_style(_danger))
	t.set_stylebox("hover", "DangerButton", _make_button_style(_danger.lightened(0.08)))
	t.set_stylebox("pressed", "DangerButton", _make_button_style(_danger_dark))
	t.set_stylebox("focus", "DangerButton", _make_focus_ring(_danger))
	t.set_color("font_color", "DangerButton", _text_inverse)
	t.set_color("font_hover_color", "DangerButton", _text_inverse)

	# SuccessButton — positive confirmation.
	t.set_type_variation("SuccessButton", "Button")
	t.set_stylebox("normal", "SuccessButton", _make_button_style(_success_dark))
	t.set_stylebox("hover", "SuccessButton", _make_button_style(_success))
	t.set_stylebox("pressed", "SuccessButton", _make_button_style(_success_dark.darkened(0.1)))
	t.set_stylebox("focus", "SuccessButton", _make_focus_ring(_success))
	t.set_color("font_color", "SuccessButton", _text_inverse)
	t.set_color("font_hover_color", "SuccessButton", _text_inverse)

	# GhostButton — borderless, for icon/close/tertiary actions.
	t.set_type_variation("GhostButton", "Button")
	var ghost := StyleBoxFlat.new()
	ghost.bg_color = Color(0, 0, 0, 0)
	ghost.set_corner_radius_all(_radius_sm)
	ghost.set_content_margin_all(6)
	var ghost_hover := StyleBoxFlat.new()
	ghost_hover.bg_color = Color(1, 1, 1, 0.10)
	ghost_hover.set_corner_radius_all(_radius_sm)
	ghost_hover.set_content_margin_all(6)
	t.set_stylebox("normal", "GhostButton", ghost)
	t.set_stylebox("hover", "GhostButton", ghost_hover)
	t.set_stylebox("pressed", "GhostButton", ghost_hover)
	t.set_stylebox("focus", "GhostButton", _make_focus_ring(_accent))
	t.set_color("font_color", "GhostButton", _text_secondary)
	t.set_color("font_hover_color", "GhostButton", _text_primary)

	# SectionTitle — uppercase muted section header label variation.
	t.set_type_variation("SectionTitle", "Label")
	t.set_font_size("font_size", "SectionTitle", _font_sm)
	t.set_color("font_color", "SectionTitle", _accent)

	# MutedLabel — secondary/help text.
	t.set_type_variation("MutedLabel", "Label")
	t.set_font_size("font_size", "MutedLabel", _font_xs)
	t.set_color("font_color", "MutedLabel", _text_muted)

	# KpiValue — large bold metric value.
	t.set_type_variation("KpiValue", "Label")
	t.set_font_size("font_size", "KpiValue", _font_header)
	t.set_color("font_color", "KpiValue", _text_primary)


## Build a card StyleBox with optional soft drop shadow.
func _make_card(bg: Color, border: Color, radius: int, margin: int, shadow: bool = true) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg
	sb.border_color = border
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(radius)
	sb.set_content_margin_all(margin)
	if shadow:
		sb.shadow_color = Color(0.18, 0.20, 0.25, 0.06)
		sb.shadow_size = 4
		sb.shadow_offset = Vector2(0, 2)
	return sb


## Button StyleBox with consistent padding and radius.
func _make_button_style(bg: Color, border: Color = Color(0, 0, 0, 0)) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg
	if border.a > 0.0:
		sb.border_color = border
		sb.set_border_width_all(1)
	sb.set_corner_radius_all(_radius_sm)
	sb.content_margin_left = 14
	sb.content_margin_right = 14
	sb.content_margin_top = 8
	sb.content_margin_bottom = 8
	return sb


## Transparent 2px focus ring drawn over a base stylebox (accessibility).
func _make_focus_ring(color: Color, base: StyleBoxFlat = null) -> StyleBoxFlat:
	var sb := StyleBoxFlat.new()
	if base != null:
		sb = base.duplicate()
	sb.bg_color = base.bg_color if base != null else Color(0, 0, 0, 0)
	sb.draw_center = base != null
	sb.border_color = color
	sb.set_border_width_all(2)
	sb.set_corner_radius_all(_radius_sm)
	return sb

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
