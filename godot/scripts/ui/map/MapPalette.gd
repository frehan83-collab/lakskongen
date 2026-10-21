class_name MapPalette
extends RefCounted
## The national map's colours (geo stages 2-3 plan, Track C): Kyst & Stål's
## design tokens, read from res://theme/tokens.json through theme_tokens.gd's
## read_tokens, once per process. The base maps were rendered from the same
## "landscape" tokens, so the icons, plates and frame sit in one palette.
##
## Signal colours keep their saturation (GEODATA §7.3): buoy yellow for
## offshore, salmon for candidates and the player's own farms, an orange for
## land farms mixed from buoy yellow and deep salmon (no new hex value).
## Nothing here is hard-coded but the fallbacks for a missing token file.

const Tokens := preload("res://theme/theme_tokens.gd")

## Fallbacks, equal to tokens.json v3, for a token file that does not load.
const FALLBACK_LANDSCAPE := {
	"granite": "#6B7378", "wet-rock": "#3E4448", "spruce": "#2F4A3A", "heath": "#8F9A5A",
	"snow": "#F4F7FA", "deep-water": "#0E2A33", "hdpe-black": "#1C1E20", "buoy-yellow": "#E8B92A",
}
const FALLBACK_BRAND := {"salmon": "#F2876A", "salmon-deep": "#D9583B", "on-salmon": "#0B2A3A"}

static var _landscape: Dictionary = {}
static var _brand: Dictionary = {}
static var _chart: Dictionary = {}


## A landscape token as a Color: "deep-water" or "deep_water", "buoy-yellow",
## "snow", "granite", ... Unknown keys are magenta, so a typo shows on screen.
static func landscape(key: String) -> Color:
	_load()
	return _color(_landscape, key)


## A brand token ("salmon", "salmon-deep", "on-salmon").
static func brand(key: String) -> Color:
	_load()
	return _color(_brand, key)


## A Chart-paper surface token ("raised", "hairline", "text", "ok", "crit" ...).
static func chart(key: String) -> Color:
	_load()
	return _color(_chart, key)


# ── The map's signal colours ──────────────────────────────────────────────────

## The sea behind and around the base map.
static func deep_water() -> Color:
	return landscape("deep-water")


## Offshore: areas, offshore farms and the offshore candidate hexagons.
static func buoy() -> Color:
	return landscape("buoy-yellow")


## The player's own farms and the open-pen candidate stars.
static func salmon() -> Color:
	return brand("salmon")


## Land farms (tanks): buoy yellow towards deep salmon, an equipment orange.
static func land_orange() -> Color:
	return buoy().lerp(brand("salmon-deep"), 0.55)


## Sea farms of independent owners, and the map's light ink (names, dots).
static func snow() -> Color:
	return landscape("snow")


## Other species: quiet granite, lightened to read on the dark sea.
static func other_species() -> Color:
	return landscape("granite").lerp(snow(), 0.35)


## National salmon fjords: the Chart surface's critical red.
static func fjord_red() -> Color:
	return chart("crit")


## Independent owners' band (the majors use CompetitorFarmVisualManager.band_color_for).
static func independent_band() -> Color:
	return landscape("granite").lerp(snow(), 0.55)


static func _load() -> void:
	if not _landscape.is_empty():
		return
	var tokens: Dictionary = Tokens.read_tokens()
	_landscape = _strings(tokens.get("landscape", {}), FALLBACK_LANDSCAPE)
	_brand = _strings(tokens.get("brand", {}), FALLBACK_BRAND)
	_chart = _strings((tokens.get("surfaces", {}) as Dictionary).get("chart", {}), {})


static func _strings(src: Variant, fallback: Dictionary) -> Dictionary:
	var out := fallback.duplicate()
	if src is Dictionary:
		for key: Variant in src:
			if src[key] is String:
				out[str(key)] = src[key]
	return out


static func _color(table: Dictionary, key: String) -> Color:
	var value: Variant = table.get(key, table.get(key.replace("_", "-")))
	return Color(value) if value is String else Color.MAGENTA
