extends RefCounted

## Kyst & Stål design tokens: paths, the variation matrix, palettes and fonts.
## Base of the theme builder chain (tokens <- style <- controls <- builder).

const TOKENS_PATH := "res://theme/tokens.json"
const PROJECT_THEME_PATH := "res://theme/lakskongen_theme.tres"
const CHART_THEME_PATH := "res://theme/lakskongen_chart_theme.tres"
const FONT_DIR := "res://theme/fonts"
const BUILDER_PATH := "res://theme/theme_builder.gd"

## The high-contrast variant of each surface (roadmap D21, U2-08): opaque,
## every text colour at least 7:1, borders and focus rings that stand out.
## Saved beside the normal themes; LakskongenTheme.set_high_contrast() merges
## them into the live themes.
const HC_THEME_PATH := "res://theme/lakskongen_theme_hc.tres"
const HC_CHART_THEME_PATH := "res://theme/lakskongen_chart_theme_hc.tres"

const SURFACE_INK := "ink"
const SURFACE_CHART := "chart"
const SURFACE_INK_HC := "ink-hc"
const SURFACE_CHART_HC := "chart-hc"
const SURFACES: Array[String] = ["ink", "chart", "ink-hc", "chart-hc"]
## Focus ring width (px): the high-contrast surfaces draw a wider ring.
const FOCUS_RING_PX := 2
const FOCUS_RING_HC_PX := 3

## Metadata keys stamped into the generated themes (checked by sync_tokens.py).
const META_TOKENS_SHA := "tokens_sha256"
const META_BUILDER_SHA := "builder_sha256"
const META_SURFACE := "surface"

const PALETTE_TYPE := "Palette"

## Tone suffix -> surface colour key.
const TONE_KEYS := {
	"": "text", "Muted": "muted", "Ok": "ok", "Warn": "warn",
	"Crit": "crit", "Info": "info", "Accent": "accent",
}
const TONES: Array[String] = ["", "Muted", "Ok", "Warn", "Crit", "Info", "Accent"]

## Label roles: variation prefix -> [font-size token, font key].
## "Body" is the base Label (its tones are BodyMuted, BodyOk, ...).
const LABEL_ROLES := {
	"Body": ["body", "ui_regular"],
	"Small": ["caption", "ui_regular"],
	"Caption": ["caption", "ui_medium"],
	"BodySmall": ["small", "ui_regular"],
	"BodyStrong": ["body", "ui_semibold"],
	"BodySmallStrong": ["small", "ui_semibold"],
	"Subheading": ["lg", "ui_semibold"],
	"Heading": ["heading", "display"],
	"Title": ["title", "display"],
	"KpiSmall": ["kpi-small", "display"],
	"Kpi": ["kpi", "display"],
	"Display": ["display", "display"],
	"Figure": ["body", "ui_medium"],
	"Overline": ["caption", "ui_caps"],
	"FigureSmall": ["caption", "ui_medium"],
	"FigureLarge": ["lg", "ui_semibold"],
}

## Pre-Kyst & Stål variation names (and Godot's built-in header variations)
## kept as aliases: name -> [role, tone].
const LABEL_ALIASES := {
	"SectionTitle": ["Caption", "Info"],
	"MutedLabel": ["Small", "Muted"],
	"KpiValue": ["Kpi", ""],
	"HeaderSmall": ["Subheading", ""],
	"HeaderMedium": ["Heading", ""],
	"HeaderLarge": ["Title", ""],
}

const BUTTON_KINDS: Array[String] = ["", "Primary", "Secondary", "Danger", "Success", "Ghost"]
## Size prefix -> font-size token.
const CONTROL_SIZES := {"": "body", "Small": "caption", "Large": "lg"}
## Classes that get Small/Large size variations ("SmallOptionButton", ...).
const SIZED_CLASSES: Array[String] = ["OptionButton", "MenuButton", "CheckBox", "CheckButton", "LineEdit", "LinkButton"]

## Font key -> token file key (display falls back to Inter when Barlow is absent).
const FONT_FILES := {
	"ui_regular": "ui-regular",
	"ui_medium": "ui-medium",
	"ui_semibold": "ui-semibold",
	"ui_bold": "ui-bold",
	"display": "display-semibold",
	"display_medium": "display-medium",
}
const EMOJI_FONT_NAMES := ["Apple Color Emoji", "Noto Color Emoji", "Segoe UI Emoji", "Android Emoji", "EmojiOne Color"]
## Faux-italic slant for RichTextLabel [i] (Inter ships no italic file here).
const ITALIC_SKEW := 0.2
## Letter spacing (px) of the "Overline" small-caps headers.
const CAPS_TRACKING := 1


static func read_tokens(path: String = TOKENS_PATH) -> Dictionary:
	if not FileAccess.file_exists(path):
		return {}
	var f := FileAccess.open(path, FileAccess.READ)
	if f == null:
		return {}
	var parsed: Variant = JSON.parse_string(f.get_as_text())
	f.close()
	return parsed as Dictionary if parsed is Dictionary else {}


## The colour set of one surface plus the brand colours, as Colors.
static func palette(tokens: Dictionary, surface: String) -> Dictionary:
	var surfaces: Dictionary = tokens.get("surfaces", {})
	var src: Dictionary = surfaces.get(surface, {})
	var brand: Dictionary = tokens.get("brand", {})
	var p := {}
	for key in src:
		if src[key] is String:
			p[key] = Color(src[key] as String)
	p["glass"] = Color(p.get("base", Color.BLACK), float(src.get("base-alpha", 1.0)))
	p["salmon"] = Color(str(brand.get("salmon", "#F2876A")))
	p["salmon_hover"] = Color(str(brand.get("salmon-hover", "#F59C83")))
	p["salmon_pressed"] = Color(str(brand.get("salmon-pressed", "#E97A5E")))
	p["salmon_deep"] = Color(str(brand.get("salmon-deep", "#D9583B")))
	# A surface may name its own text on salmon (the high-contrast ones: black,
	# at least 7:1 on every salmon state).
	p["on_salmon"] = Color(str(src.get("on-salmon", brand.get("on-salmon", "#0B2A3A"))))
	return p


## The Ink palette that goes with `surface`: the HUD glass, rails, chips,
## toasts and tooltips are Ink on every surface, and high-contrast Ink when the
## surface is a high-contrast one (U2-08 review: they stayed translucent).
static func ink_palette_for(tokens: Dictionary, surface: String) -> Dictionary:
	return palette(tokens, SURFACE_INK_HC if surface.ends_with("-hc") else SURFACE_INK)


static func font_size(tokens: Dictionary, key: String) -> int:
	var fonts: Dictionary = tokens.get("fonts", {})
	var sizes: Dictionary = fonts.get("sizes", {})
	return int(sizes.get(key, 15))


static func _spacing(tokens: Dictionary, key: String) -> int:
	var spacing: Dictionary = tokens.get("spacing", {})
	return int(spacing.get(key, 8))


static func _radius(tokens: Dictionary, key: String) -> int:
	var radii: Dictionary = tokens.get("radii", {})
	return int(radii.get(key, 4))


## Builds the font set: Inter (tabular figures) for the UI, Barlow Condensed for
## display when its files are present (Inter SemiBold otherwise). Every font
## carries the OS emoji font as a fallback until U3-02 replaces emoji icons.
static func make_fonts(tokens: Dictionary) -> Dictionary:
	var fonts_tok: Dictionary = tokens.get("fonts", {})
	var files: Dictionary = fonts_tok.get("files", {})
	var features := {}
	var ts := TextServerManager.get_primary_interface()
	for tag in fonts_tok.get("opentype-features", []):
		features[ts.name_to_tag(str(tag))] = 1
	var emoji := SystemFont.new()
	emoji.font_names = PackedStringArray(EMOJI_FONT_NAMES)
	emoji.allow_system_fallback = true
	var out := {}
	for key in FONT_FILES:
		var path := str(files.get(FONT_FILES[key], ""))
		if key.begins_with("display") and not ResourceLoader.exists(path):
			path = str(files.get("display-fallback", ""))
		if not ResourceLoader.exists(path):
			path = str(files.get("ui-regular", ""))
		var base: Font = load(path) as Font
		var fv := FontVariation.new()
		fv.resource_name = key
		fv.base_font = base
		fv.opentype_features = features
		fv.fallbacks = [emoji]
		out[key] = fv
	var italic := FontVariation.new()
	italic.resource_name = "ui_italic"
	italic.base_font = (out["ui_regular"] as FontVariation).base_font
	italic.opentype_features = features
	italic.fallbacks = [emoji]
	italic.variation_transform = Transform2D(Vector2(1.0, 0.0), Vector2(ITALIC_SKEW, 1.0), Vector2.ZERO)
	out["ui_italic"] = italic
	# Small capitals headers (KPI titles, column heads): semibold, tracked out.
	var caps := FontVariation.new()
	caps.resource_name = "ui_caps"
	caps.base_font = (out["ui_semibold"] as FontVariation).base_font
	caps.opentype_features = features
	caps.fallbacks = [emoji]
	caps.spacing_glyph = CAPS_TRACKING
	out["ui_caps"] = caps
	return out


## True when the Barlow Condensed display files are bundled.
static func has_display_font(tokens: Dictionary) -> bool:
	var files: Dictionary = (tokens.get("fonts", {}) as Dictionary).get("files", {})
	return ResourceLoader.exists(str(files.get("display-semibold", "")))


## Per-surface working set shared by the section builders.
class _Ctx:
	var tokens: Dictionary
	var surface: String
	var fonts: Dictionary
	var p: Dictionary
	var ink: bool
	var high_contrast: bool

	func _init(p_tokens: Dictionary, p_surface: String, p_fonts: Dictionary, p_palette: Dictionary) -> void:
		tokens = p_tokens
		surface = p_surface
		fonts = p_fonts
		p = p_palette
		ink = p_surface.begins_with("ink")
		high_contrast = p_surface.ends_with("-hc")

	func col(key: String) -> Color:
		return p.get(key, Color.MAGENTA)

	func size(key: String) -> int:
		var fonts_tok: Dictionary = tokens.get("fonts", {})
		var sizes: Dictionary = fonts_tok.get("sizes", {})
		return int(sizes.get(key, 15))

	func space(key: String) -> int:
		var spacing: Dictionary = tokens.get("spacing", {})
		return int(spacing.get(key, 8))

	func radius(key: String) -> int:
		var radii: Dictionary = tokens.get("radii", {})
		return int(radii.get(key, 4))

	func elev(key: String, fallback: float) -> float:
		var e: Dictionary = tokens.get("elevation", {})
		return float(e.get(key, fallback))

	func shadow_color() -> Color:
		var e: Dictionary = tokens.get("elevation", {})
		return Color(str(e.get("card-shadow", "#0B2A3A")))

	func font(key: String) -> Font:
		return fonts.get(key, fonts["ui_regular"])
