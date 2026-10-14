extends Node

## LakskongenTheme v3.0 — "Kyst & Stål" (roadmap D13/D14).
##
## The UI theme is no longer assembled at runtime. tools/sync_tokens.py builds
## two resources from res://theme/tokens.json (synced from design/tokens.json):
##   * res://theme/lakskongen_theme.tres       Ink glass — the PROJECT theme
##     (project.godot [gui] theme/custom), so every Control under every
##     CanvasLayer resolves it: the HUD, the tab rail, toasts, menus, dialogs.
##   * res://theme/lakskongen_chart_theme.tres Chart paper — assigned to the
##     subtree that hosts document panels (GameUI's panel area).
##
## This autoload is the facade: palette constants (the stable public API that
## tools/drift_check.py keeps equal to the tokens), the font resources, and
## helpers that pick theme type variations instead of per-node overrides:
##   LakskongenTheme.set_role(label, &"Heading")      # size + weight
##   LakskongenTheme.set_tone(label, &"Crit")         # surface-correct colour
##   LakskongenTheme.palette(node, &"ok")             # Color on node's surface

const Builder := preload("res://theme/theme_builder.gd")

# ---------------------------------------------------------------------------
# Kyst & Stål palette (tokens.json v3) — Ink glass (HUD, rail, toasts, menus)
# ---------------------------------------------------------------------------
const INK               := Color("#0B2A3A")
const INK_RAISED        := Color("#13394B")
const INK_SUNKEN        := Color("#071F2B")
const INK_HAIRLINE      := Color("#2C4650")
const INK_STROKE        := Color("#5B7D8C")
const INK_TEXT          := Color("#E8EEF0")
const INK_MUTED         := Color("#9FB4BB")
const INK_OK            := Color("#7FD69B")
const INK_WARN          := Color("#F2B705")
const INK_CRIT          := Color("#FF8A80")
const INK_INFO          := Color("#8CC7DA")
const INK_ACCENT        := Color("#F2876A")

# Chart paper (document panels)
const CHART             := Color("#F4F1EA")
const CHART_RAISED      := Color("#FBFAF6")
const CHART_SUNKEN      := Color("#EDE9E0")
const CHART_HAIRLINE    := Color("#D6CFBF")
const CHART_STROKE      := Color("#8C8578")
const CHART_TEXT        := Color("#1C1E20")
const CHART_MUTED       := Color("#5A6168")
const CHART_OK          := Color("#2E6B45")
const CHART_WARN        := Color("#8A5A00")
const CHART_CRIT        := Color("#B3172F")
const CHART_INFO        := Color("#1F5E73")
const CHART_ACCENT      := Color("#B5452B")

# Brand
const SALMON            := Color("#F2876A")
const SALMON_DEEP       := Color("#D9583B")
const ON_SALMON         := Color("#0B2A3A")

# ---------------------------------------------------------------------------
# Pre-v3 names, re-pointed. Panels that still read them sit on Chart paper,
# so the light-surface names resolve to Chart and the dark-surface names
# (PANEL, TEXT, MUTED, BG_DARK …) to Ink. New code: palette()/set_tone().
# ---------------------------------------------------------------------------
const PRIMARY           := Color("#1F5E73")   # chart info: deep fjord teal
const PRIMARY_LIGHT     := Color("#2A7690")
const PRIMARY_DARK      := Color("#174A5B")
const SECONDARY         := Color("#8CC7DA")   # ink info: ice blue
const SECONDARY_LIGHT   := Color("#B5DBE7")
const ACCENT            := Color("#1F5E73")
const ACCENT_LIGHT      := Color("#2A7690")
const BG                := Color("#F4F1EA")
const SURFACE           := Color("#FBFAF6")
const SURFACE_ELEVATED  := Color("#F4F1EA")
const TEXT_PRIMARY      := Color("#1C1E20")
const TEXT_SECONDARY    := Color("#5A6168")
const TEXT_MUTED        := Color("#5A6168")
const TEXT_INVERSE      := Color("#E8EEF0")
const BORDER            := Color("#D6CFBF")
const BORDER_LIGHT      := Color("#E7E2D7")
const SUCCESS           := Color("#2E6B45")
const SUCCESS_DARK      := Color("#245638")
const WARNING           := Color("#8A5A00")
const WARNING_DARK      := Color("#6E4800")
const DANGER            := Color("#B3172F")
const DANGER_DARK       := Color("#8F1226")
const INFO              := Color("#1F5E73")
const WATER_BLUE        := Color("#3F7F95")
const SALMON_PINK       := Color("#F2876A")
const BG_DARK           := Color("#0B2A3A")
const BG_MEDIUM         := Color("#13394B")
const BG_LIGHT          := Color("#2C4650")
const ACCENT_PRIMARY    := Color("#1F5E73")
const ACCENT_SECONDARY  := Color("#8CC7DA")
const WARNING_COLOR     := Color("#8A5A00")
const DANGER_COLOR      := Color("#B3172F")
const SUCCESS_COLOR     := Color("#2E6B45")
const TEXT_ACCENT       := Color("#1F5E73")
# Dark ("Ink") surfaces used by the older dark-panel helpers below.
const PANEL             := Color("#13394B")
const PANEL_ALT         := Color("#0B2A3A")
const PANEL_HOVER       := Color("#1A4559")
const TEXT              := Color("#E8EEF0")
const MUTED             := Color("#9FB4BB")
const GOLD              := Color("#F2B705")
const TEAL              := Color("#8CC7DA")
const KELP              := Color("#7FD69B")
const KELP_LIGHT        := Color("#7FD69B")
const AURORA_GREEN      := Color("#7FD69B")
const AURORA_PURPLE     := Color("#B9A3D6")
const ACCENT2           := Color("#8CC7DA")
const COLOR_BACKGROUND_DARK := Color("#0B2A3A")
const COLOR_GREEN       := Color("#2E6B45")
const COLOR_RED         := Color("#B3172F")
const COLOR_YELLOW      := Color("#8A5A00")

# ---------------------------------------------------------------------------
# Type scale (tokens fonts.sizes; 13 px is the floor at the 1080p base)
# ---------------------------------------------------------------------------
const FONT_XS     := 13   # caption
const FONT_SMALL  := 13   # caption (legacy alias)
const FONT_SM     := 14   # small
const FONT_BODY   := 15   # body
const FONT_MD     := 15   # body (legacy alias)
const FONT_LG     := 18   # lg / subheading
const FONT_HEADER := 22   # heading
const FONT_XL     := 22   # heading (legacy alias)
const FONT_TITLE  := 28   # title
const FONT_XXL    := 28   # title (legacy alias)
const FONT_KPI    := 32   # kpi
const FONT_HERO   := 48   # display

# Spacing & radius (tokens spacing / radii)
const SPACING_XS  := 4
const SPACING_SM  := 8
const SPACING_MD  := 16
const SPACING_LG  := 24
const SPACING_XL  := 32
const SPACE_XS := SPACING_XS
const SPACE_SM := SPACING_SM
const SPACE_MD := SPACING_MD
const SPACE_LG := SPACING_LG
const SPACE_XL := SPACING_XL
const RADIUS_SM   := 4
const RADIUS_MD   := 6
const RADIUS_LG   := 10

## Label role for a pixel size: the nearest step of the type scale at or above
## it, so legacy sizes never shrink below the 13 px floor.
const SIZE_ROLES := [[13, &"Small"], [14, &"BodySmall"], [16, &"Body"], [19, &"Subheading"],
	[23, &"Heading"], [26, &"KpiSmall"], [30, &"Title"], [40, &"Kpi"], [9999, &"Display"]]
const TONE_SUFFIXES := [&"Muted", &"Ok", &"Warn", &"Crit", &"Info", &"Accent"]

# ---------------------------------------------------------------------------
# Fonts (generated FontVariations: Inter with tabular figures, display face)
# ---------------------------------------------------------------------------
var font_body: Font = null
var font_medium: Font = null
var font_heading_semibold: Font = null
var font_heading_bold: Font = null
var font_display: Font = null
## Pre-v3 name for the regular UI face.
var font_heading: Font = null

var chart_theme: Theme = null
var _theme: Theme = null
var _tokens_source := "generated theme resources"


func _ready() -> void:
	_load_fonts()
	_theme = ThemeDB.get_project_theme()
	if ResourceLoader.exists(Builder.CHART_THEME_PATH):
		chart_theme = load(Builder.CHART_THEME_PATH) as Theme
	if _theme == null or chart_theme == null:
		# gui/theme/custom missing (or the resources were deleted): build in
		# memory so the game still renders the intended look, and say so.
		push_warning("LakskongenTheme: generated theme missing — building in memory. Run: python3 tools/sync_tokens.py --write")
		_build_in_memory()
	_apply_saved_ui_scale()


func _load_fonts() -> void:
	var dir := Builder.FONT_DIR
	font_body = _load_font(dir + "/ui_regular.tres")
	font_heading = font_body
	font_medium = _load_font(dir + "/ui_medium.tres")
	font_heading_semibold = _load_font(dir + "/ui_semibold.tres")
	font_heading_bold = _load_font(dir + "/ui_bold.tres")
	font_display = _load_font(dir + "/display.tres")


func _load_font(path: String) -> Font:
	return load(path) as Font if ResourceLoader.exists(path) else null


func _build_in_memory() -> void:
	var tokens := Builder.read_tokens(Builder.TOKENS_PATH)
	if tokens.is_empty():
		push_error("LakskongenTheme: %s unavailable — UI falls back to Godot defaults" % Builder.TOKENS_PATH)
		return
	var fonts := Builder.make_fonts(tokens)
	if _theme == null:
		_theme = Builder.build(tokens, Builder.SURFACE_INK, fonts)
		if get_tree() != null and get_tree().root != null:
			get_tree().root.theme = _theme
	if chart_theme == null:
		chart_theme = Builder.build(tokens, Builder.SURFACE_CHART, fonts)
	_tokens_source = "%s (built in memory)" % Builder.TOKENS_PATH


## Restore the accessibility UI scale (user://settings.json) at boot, so it
## survives restarts even though SettingsMenu only exists in the main menu.
func _apply_saved_ui_scale() -> void:
	if not FileAccess.file_exists("user://settings.json"):
		return
	var file := FileAccess.open("user://settings.json", FileAccess.READ)
	if file == null:
		return
	var json := JSON.new()
	if json.parse(file.get_as_text()) != OK or not (json.data is Dictionary):
		return
	var acc: Variant = json.data.get("accessibility", {})
	if not (acc is Dictionary):
		return
	var scale := float(acc.get("ui_scale", 1.0))
	var win := get_window()
	if win != null and scale > 0.0:
		win.content_scale_factor = scale


## Rebuild both themes from res://theme/tokens.json and merge them into the
## live resources (design iteration without a restart).
func reload_tokens() -> void:
	var tokens := Builder.read_tokens(Builder.TOKENS_PATH)
	if tokens.is_empty():
		push_warning("LakskongenTheme: %s unavailable — reload skipped" % Builder.TOKENS_PATH)
		return
	var fonts := Builder.make_fonts(tokens)
	if _theme != null:
		_theme.merge_with(Builder.build(tokens, Builder.SURFACE_INK, fonts))
	if chart_theme != null:
		chart_theme.merge_with(Builder.build(tokens, Builder.SURFACE_CHART, fonts))
	print("LakskongenTheme: theme reloaded — source: %s" % Builder.TOKENS_PATH)


func get_theme() -> Theme:
	return _theme


## Kept for callers from v2: the project theme already reaches every Control.
func apply_to_root() -> void:
	pass


## Give a subtree the Chart-paper surface (document panels).
func apply_chart(control: Control) -> void:
	if control != null and chart_theme != null:
		control.theme = chart_theme


# ---------------------------------------------------------------------------
# Variation helpers
# ---------------------------------------------------------------------------

## The colour `key` ("text", "muted", "ok", "warn", "crit", "info", "accent",
## "raised", "hairline", "series_1" …) on the surface `node` sits on.
static func palette(node: Control, key: StringName) -> Color:
	if node == null:
		return CHART_TEXT
	return node.get_theme_color(key, &"Palette")


## Label role for a legacy pixel size.
static func role_for_size(size_px: int) -> StringName:
	for step in SIZE_ROLES:
		if size_px <= int(step[0]):
			return step[1]
	return &"Display"


## Sets the typographic role, keeping the node's current tone. Labels take
## "<Role><Tone>"; buttons and other sized controls take Small/""/Large.
static func set_role(node: Control, role: StringName) -> void:
	if node == null:
		return
	if node is Label:
		var tone := _tone_of(node.theme_type_variation)
		node.theme_type_variation = _label_name(role, tone)
		return
	var prefix := ""
	if role in [&"Small", &"Caption", &"FigureSmall", &"BodySmall"]:
		prefix = "Small"
	elif role in [&"Subheading", &"Heading", &"Title", &"Kpi", &"KpiSmall", &"Display", &"FigureLarge"]:
		prefix = "Large"
	var cls := node.get_class()
	if node is Button and not (node is CheckBox or node is CheckButton or node is OptionButton or node is MenuButton or node is LinkButton or node is ColorPickerButton):
		var kind := _button_kind_of(node.theme_type_variation)
		node.theme_type_variation = StringName(prefix + kind + "Button") if prefix + kind != "" else &""
	elif cls in ["OptionButton", "MenuButton", "CheckBox", "CheckButton", "LineEdit"]:
		node.theme_type_variation = StringName(prefix + cls) if prefix != "" else &""


## Same as set_role, from a legacy pixel size (keeps a set_strong weight).
static func set_size(node: Control, size_px: int) -> void:
	var role := role_for_size(size_px)
	if node != null and node.has_meta(&"lk_strong"):
		role = _strong_of(role)
	set_role(node, role)


## Semibold weight for a Label's role (replaces the v2 per-node font
## overrides with font_heading_bold / font_heading_semibold).
static func set_strong(node: Control) -> void:
	if node == null or not (node is Label):
		return
	node.set_meta(&"lk_strong", true)
	set_role(node, _strong_of(_role_of(node.theme_type_variation)))


static func _strong_of(role: StringName) -> StringName:
	match role:
		&"Body", &"Figure": return &"BodyStrong"
		&"BodySmall": return &"BodySmallStrong"
		&"Small", &"FigureSmall": return &"Caption"
	return role


## Sets the semantic colour of a Label ("", "Muted", "Ok", "Warn", "Crit",
## "Info", "Accent"), keeping its role. Resolves per surface (Ink or Chart).
static func set_tone(node: Control, tone: StringName) -> void:
	if node == null:
		return
	if node.has_theme_color_override("font_color"):
		node.remove_theme_color_override("font_color")
	if node is Label:
		node.theme_type_variation = _label_name(_role_of(node.theme_type_variation), tone)
	else:
		var key := String(tone).to_lower() if tone != &"" else "text"
		node.add_theme_color_override("font_color", palette(node, StringName(key)))


static func _label_name(role: StringName, tone: StringName) -> StringName:
	var r := String(role) if role != &"" else "Body"
	if r == "Body" and tone == &"":
		return &""
	return StringName(r + String(tone))


static func _tone_of(variation: StringName) -> StringName:
	var v := String(variation)
	for tone in TONE_SUFFIXES:
		if v.ends_with(String(tone)) and v != String(tone):
			return tone
	return &""


static func _role_of(variation: StringName) -> StringName:
	var v := String(variation)
	if v == "":
		return &"Body"
	var tone := _tone_of(variation)
	if tone != &"":
		v = v.substr(0, v.length() - String(tone).length())
	return StringName(v) if v != "" else &"Body"


static func _button_kind_of(variation: StringName) -> String:
	var v := String(variation)
	for kind in ["Primary", "Secondary", "Danger", "Success", "Ghost"]:
		if v.contains(kind):
			return kind
	return ""


## Text colours of the v2 "Nordic Minimalist" palette and of Kyst & Stål,
## by the semantic tone they meant (hex, upper case, no alpha).
const _TONE_BY_HEX := {
	# text
	"2E3440": &"", "3B4252": &"", "434C5E": &"", "1C1E20": &"", "E8EEF0": &"",
	# secondary / muted
	"4C566A": &"Muted", "7B8794": &"Muted", "616E88": &"Muted", "5A6168": &"Muted", "9FB4BB": &"Muted",
	# ok
	"A3BE8C": &"Ok", "8FAD78": &"Ok", "8FBC8F": &"Ok", "2E6B45": &"Ok", "245638": &"Ok", "7FD69B": &"Ok",
	# warning
	"EBCB8B": &"Warn", "D4B06A": &"Warn", "D08770": &"Warn", "9A7B1F": &"Warn", "7A6118": &"Warn",
	"8A5A00": &"Warn", "6E4800": &"Warn", "F2B705": &"Warn",
	# critical
	"BF616A": &"Crit", "A54E56": &"Crit", "9A444C": &"Crit", "B3172F": &"Crit", "8F1226": &"Crit", "FF8A80": &"Crit",
	# info
	"0A4D68": &"Info", "0D5F80": &"Info", "083D53": &"Info", "88C0D0": &"Info", "81A1C1": &"Info",
	"5E81AC": &"Info", "33526F": &"Info", "1F5E73": &"Info", "2A7690": &"Info", "8CC7DA": &"Info",
	"A3D1DE": &"Info",
	# accent
	"F2876A": &"Accent", "B5452B": &"Accent", "D9583B": &"Accent",
}


## The tone a text colour means, or &"?" for a colour outside both palettes.
static func tone_for_color(color: Color) -> StringName:
	return _TONE_BY_HEX.get(color.to_html(false).to_upper(), &"?")


## v2 call sites set text colours directly; this keeps them surface-correct:
## a palette colour becomes the matching tone (resolved on the node's
## surface, Ink or Chart), any other colour stays an explicit override
## (e.g. light text on a dark fill inside a document).
static func set_text_color(node: Control, color: Color) -> void:
	if node == null:
		return
	var tone := tone_for_color(color)
	if tone == &"?" or color.a < 0.99:
		node.add_theme_color_override("font_color", color)
		return
	set_tone(node, tone)


# ---------------------------------------------------------------------------
# Builders kept for v2 callers (now variation-based)
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

## Get a KPI card panel (raised paper with a coloured top stripe)
static func make_kpi_panel(stripe_color: Color = PRIMARY) -> StyleBoxFlat:
	var sb := make_panel()
	sb.border_color = BORDER_LIGHT
	sb.set_border_width_all(1)
	sb.border_width_top = 3
	sb.border_color = stripe_color
	return sb

## Status colors helper (Chart surface)
static func status_color(level: String) -> Color:
	match level.to_lower():
		"healthy", "good", "success": return SUCCESS
		"warning", "elevated": return WARNING
		"critical", "danger", "bad": return DANGER
		"info": return INFO
		_: return TEXT_MUTED

## Balance color (financial, Chart surface)
static func balance_color(amount: float) -> Color:
	if amount > 0:
		return SUCCESS
	elif amount < 0:
		return DANGER
	return TEXT_PRIMARY

# Legacy compat
static func get_panel_style(bg_color: Color = SURFACE, border_color: Color = BORDER_LIGHT, border_width: int = 1, corner_radius: int = RADIUS_MD, content_margin: int = 10) -> StyleBoxFlat:
	return make_panel(bg_color, border_color, border_width, corner_radius, content_margin)

static func get_severity_color(level: String) -> Color:
	return status_color(level)

static func get_button_style(normal_bg: Color, hover_bg: Color, _text_color: Color) -> Dictionary:
	var styles := {}
	styles.normal = make_panel(normal_bg, normal_bg, 1, RADIUS_SM, 12)
	styles.hover = make_panel(hover_bg, hover_bg, 1, RADIUS_SM, 12)
	styles.pressed = make_panel(hover_bg.darkened(0.1), hover_bg, 1, RADIUS_SM, 12)
	styles.disabled = make_panel(BORDER_LIGHT, BORDER, 1, RADIUS_SM, 12)
	return styles

static func get_label_style(font_size: int = FONT_BODY, font_color: Color = TEXT_PRIMARY) -> Dictionary:
	var label_settings := LabelSettings.new()
	label_settings.font_size = font_size
	label_settings.font_color = font_color
	return {"settings": label_settings}

static func create_styled_panel() -> PanelContainer:
	var panel := PanelContainer.new()
	panel.theme_type_variation = &"Card"
	return panel

## A label in the role for `font_size`; a palette colour becomes the matching
## tone (resolved per surface), any other colour stays an explicit override.
static func create_label(text: String, font_size: int = FONT_BODY, color: Color = TEXT_PRIMARY) -> Label:
	var label := Label.new()
	label.text = text
	var tone := tone_for_color(color)
	label.theme_type_variation = _label_name(role_for_size(font_size), tone if tone != &"?" else &"")
	if tone == &"?":
		label.add_theme_color_override("font_color", color)
	return label

static func create_section_header(text: String) -> Label:
	var label := Label.new()
	label.text = text
	label.theme_type_variation = &"Subheading"
	return label

static func create_divider(_color: Color = BORDER) -> HSeparator:
	var sep := HSeparator.new()
	return sep

static func create_stat(label_text: String, value_text: String, color: Color = TEXT_PRIMARY) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", SPACING_SM)
	var name_label := create_label(label_text, FONT_SM, TEXT_SECONDARY)
	var value_label := create_label(value_text, FONT_SM, color)
	value_label.theme_type_variation = _label_name(&"Figure", _tone_of(value_label.theme_type_variation))
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
	var size_px: int = FONT_MD
	if a is bool:
		primary = a
		if b is bool:
			danger = b
	elif a is int:
		size_px = a
		primary = false
	var kind := "Danger" if danger else ("Primary" if primary else "")
	var prefix := "Small" if size_px <= FONT_SM else ("Large" if size_px >= FONT_LG else "")
	btn.theme_type_variation = StringName(prefix + kind + "Button") if prefix + kind != "" else &""
	return btn

static func format_number(value: Variant, decimals: int = 0) -> String:
	var num := float(value)
	if decimals <= 0:
		return "%d" % int(round(num))
	return ("%." + str(decimals) + "f") % num

static func format_currency(value: Variant, decimals: int = 2) -> String:
	return format_number(value, decimals) + " kr"
