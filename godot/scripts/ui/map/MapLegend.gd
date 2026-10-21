class_name MapLegend
extends PanelContainer
## The national map's key (geo stages 2-3 plan, Track C): each glyph drawn
## by the very code that draws it on the map, with its name. The header
## folds the key away (and back) so it never has to cover the coast.

## [glyph kind, translation key], in reading order.
const ROWS: Array = [
	["sea", "MAP_GEO_LEGEND_SEA"],
	["land", "MAP_GEO_LEGEND_LAND"],
	["offshore", "MAP_GEO_LEGEND_OFFSHORE"],
	["other", "MAP_GEO_LEGEND_OTHER"],
	["cluster", "MAP_GEO_LEGEND_CLUSTER"],
	["player", "MAP_GEO_LEGEND_PLAYER"],
	["star", "MAP_GEO_LEGEND_CANDIDATES"],
	["hexagon", "MAP_GEO_LEGEND_HEXAGON"],
	["offshore_area", "MAP_GEO_LEGEND_OFFSHORE_AREA"],
	["fjord", "MAP_GEO_LEGEND_FJORD"],
	["light", "MAP_GEO_LEGEND_LIGHT"],
]

var _toggle: Button = null
var _grid: GridContainer = null


func _ready() -> void:
	name = "MapLegend"
	theme_type_variation = &"CardCompact"
	mouse_filter = Control.MOUSE_FILTER_STOP
	var col := VBoxContainer.new()
	col.add_theme_constant_override("separation", 6)
	add_child(col)
	_toggle = Button.new()
	_toggle.name = "LegendToggle"
	_toggle.theme_type_variation = &"SmallGhostButton"
	_toggle.toggle_mode = true
	_toggle.button_pressed = true
	_toggle.alignment = HORIZONTAL_ALIGNMENT_LEFT
	_toggle.text = tr("MAP_GEO_LEGEND_TITLE")
	_toggle.tooltip_text = tr("MAP_GEO_LEGEND_TIP")
	_toggle.toggled.connect(set_open)
	col.add_child(_toggle)
	_grid = GridContainer.new()
	_grid.name = "Rows"
	_grid.columns = 2
	_grid.add_theme_constant_override("h_separation", 8)
	_grid.add_theme_constant_override("v_separation", 3)
	col.add_child(_grid)
	for row: Array in ROWS:
		var glyph := Glyph.new()
		glyph.kind = str(row[0])
		glyph.name = "Glyph_" + glyph.kind
		glyph.custom_minimum_size = Vector2(22, 18)
		_grid.add_child(glyph)
		var label := Label.new()
		label.name = "Label_" + glyph.kind
		label.theme_type_variation = &"Small"
		label.text = tr(str(row[1]))
		_grid.add_child(label)


## Show or fold the rows.
func set_open(on: bool) -> void:
	if _grid != null:
		_grid.visible = on
	if _toggle != null and _toggle.button_pressed != on:
		_toggle.set_pressed_no_signal(on)
	reset_size()


func is_open() -> bool:
	return _grid != null and _grid.visible


func toggle_button() -> Button:
	return _toggle


## The glyph kinds shown, in order.
func kinds() -> PackedStringArray:
	var out := PackedStringArray()
	for row: Array in ROWS:
		out.append(str(row[0]))
	return out


## One legend glyph, drawn on a deep-water swatch like the map behind it.
class Glyph extends Control:
	var kind := ""

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		texture_repeat = CanvasItem.TEXTURE_REPEAT_ENABLED

	func _draw() -> void:
		var deep := MapPalette.deep_water()
		draw_rect(Rect2(Vector2.ZERO, size), deep)
		var c := size * 0.5
		match kind:
			"sea":
				GeoSiteLayer.draw_site_icon(self, c, GeoBridgeAdapter.CLASS_SEA, Color.WHITE, false)
			"land":
				GeoSiteLayer.draw_site_icon(self, c, GeoBridgeAdapter.CLASS_LAND, Color.WHITE, false)
			"offshore":
				GeoSiteLayer.draw_site_icon(self, c, GeoBridgeAdapter.CLASS_OFFSHORE, Color.WHITE, false)
			"other":
				GeoSiteLayer.draw_site_icon(self, c, GeoBridgeAdapter.CLASS_OTHER, Color.WHITE, false)
			"cluster":
				draw_circle(c, 7.0, Color(deep.lightened(0.15), 1.0))
				draw_arc(c, 6.0, 0.0, TAU, 24, MapPalette.snow(), 1.6, true)
				draw_arc(c, 8.6, -PI * 0.5, PI * 0.4, 12, CompetitorFarmVisualManager.BAND_PALETTE[1], 2.0, true)
			"player":
				draw_circle(c, 7.0, MapPalette.snow())
				draw_circle(c, 5.2, MapPalette.salmon())
				draw_circle(c, 1.8, deep)
			"star":
				draw_colored_polygon(GeoOverlayLayer._star(c, 6.6, 2.8), MapPalette.salmon())
			"hexagon":
				draw_colored_polygon(GeoSiteLayer._ngon(c, 5.0, 6), MapPalette.buoy())
			"offshore_area":
				var r := Rect2(c - Vector2(8, 5), Vector2(16, 10))
				var ring := PackedVector2Array([r.position, Vector2(r.end.x, r.position.y), r.end,
					Vector2(r.position.x, r.end.y), r.position])
				draw_multiline(GeoOverlayLayer._dashed(ring, 3.0, 2.5), MapPalette.buoy(), 1.4)
			"fjord":
				var r := Rect2(c - Vector2(8, 5), Vector2(16, 10))
				var hatch := GeoOverlayLayer._hatch_texture()
				draw_texture_rect(hatch, r, true, Color(MapPalette.fjord_red().lightened(0.25), 0.9))
				draw_rect(r, Color(MapPalette.fjord_red().lightened(0.2), 0.9), false, 1.0)
			"light":
				var lights := [MapPalette.chart("ok"), MapPalette.chart("warn"), MapPalette.chart("crit")]
				for i in 3:
					var p := c + Vector2((i - 1) * 6.5, 0.0)
					draw_circle(p, 3.2, MapPalette.chart("raised"))
					draw_circle(p, 2.4, lights[i])
