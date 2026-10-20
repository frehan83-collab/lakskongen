class_name GeoRegionIcons
extends RefCounted
## The region and site levels' icon language (geo stages 2-3 plan Track D):
## the Kyst & Stål landscape colours the map draws with, candidate stars in
## their states, Havbruk til havs hexagons, the layer icons the chips and the
## key carry, and the two small Controls that draw them. Display only.

const BACKDROP := Color("#0E2A33")  # tokens landscape deep-water
const OPEN_SEA := Color("#1A4967")  # the base maps' open-sea edge
const SALMON := Color("#F2876A")
const SALMON_DEEP := Color("#D9583B")
const SNOW := Color("#F4F7FA")
const BUOY := Color("#E8B92A")
const INDEPENDENT := Color("#A9B6BC")  # slate, quieter than any band
const HALO := Color(0.043, 0.165, 0.227, 0.85)
const ICON_W := 26
const ICON_H := 16


static var _icon_blank: ImageTexture = null


## A transparent icon that reserves room for a chip's drawn legend icon.
static func icon_space() -> ImageTexture:
	if _icon_blank == null:
		var img := Image.create(ICON_W, ICON_H, false, Image.FORMAT_RGBA8)
		img.fill(Color(0, 0, 0, 0))
		_icon_blank = ImageTexture.create_from_image(img)
	return _icon_blank


## A candidate star in its state: free (outline), suggested (filled, ringed),
## yours (filled salmon with a white core) or taken (grey).
static func draw_star(ci: CanvasItem, c: Vector2, r: float, state: String) -> void:
	var pts := PackedVector2Array()
	for i in 10:
		var a := -PI / 2.0 + i * PI / 5.0
		pts.append(c + Vector2(cos(a), sin(a)) * (r if i % 2 == 0 else r * 0.45))
	var halo := PackedVector2Array()
	for i in 10:
		var a := -PI / 2.0 + i * PI / 5.0
		halo.append(c + Vector2(cos(a), sin(a)) * ((r if i % 2 == 0 else r * 0.45) + 1.8))
	ci.draw_colored_polygon(halo, HALO)
	match state:
		"yours":
			ci.draw_colored_polygon(pts, SALMON)
			ci.draw_circle(c, r * 0.22, SNOW)
		"taken":
			ci.draw_colored_polygon(pts, Color(0.55, 0.58, 0.60))
		"suggested":
			ci.draw_arc(c, r + 3.0, 0.0, TAU, 32, SNOW, 1.5, true)
			ci.draw_colored_polygon(pts, SALMON)
		_:
			ci.draw_colored_polygon(pts, Color(SALMON, 0.35))
	pts.append(pts[0])
	ci.draw_polyline(pts, SALMON if state != "taken" else Color(0.75, 0.77, 0.78), 1.5, true)


## A Havbruk til havs hexagon: buoy yellow; the one hav_1 is bound to filled.
static func draw_hexagon(ci: CanvasItem, c: Vector2, r: float, bound: bool) -> void:
	var pts := PackedVector2Array()
	for i in 6:
		var a := PI / 6.0 + i * PI / 3.0
		pts.append(c + Vector2(cos(a), sin(a)) * r)
	ci.draw_colored_polygon(pts, HALO if not bound else BUOY)
	pts.append(pts[0])
	ci.draw_polyline(pts, BUOY, 2.0, true)
	if bound:
		ci.draw_circle(c, r * 0.28, HALO)


## A layer chip: a toggle with its layer's icon drawn where the icon goes.
class ChipButton extends Button:
	var kind := ""

	func _draw() -> void:
		var sb := get_theme_stylebox("normal")
		var left := sb.get_margin(SIDE_LEFT) if sb != null else 8.0
		var r := Rect2(Vector2(left, (size.y - GeoRegionIcons.ICON_H) * 0.5), Vector2(GeoRegionIcons.ICON_W, GeoRegionIcons.ICON_H))
		var on := button_pressed
		GeoRegionIcons.draw_layer_icon(self, r, kind, on)


## A layer's icon (the chips and the key): heat, restrictions, sites,
## candidates, hatch, site, hex. Dimmed when the layer is off.
static func draw_layer_icon(ci: CanvasItem, r: Rect2, kind: String, on: bool = true) -> void:
	var fade := 1.0 if on else 0.35
	var c := r.get_center()
	ci.draw_rect(r, Color(BACKDROP, 0.9 if on else 0.45))
	match kind:
		"heat":
			var w := int(r.size.x)
			for i in w:
				var col := GeoRegionMath.ramp(lerpf(GeoRegionMath.RAMP_STOPS[0], 1.0, float(i) / maxf(w - 1.0, 1.0)))
				ci.draw_rect(Rect2(r.position + Vector2(i, 3), Vector2(1, r.size.y - 6)), Color(col, fade))
		"restrictions":
			var pts := PackedVector2Array([r.position + Vector2(2, 3), r.position + Vector2(14, 2),
				r.position + Vector2(15, r.size.y - 3), r.position + Vector2(3, r.size.y - 2), r.position + Vector2(2, 3)])
			ci.draw_colored_polygon(pts, Color(GeoRegionOverlay.STYLE["salmon_fjord"]["fill"], 0.5 * fade))
			ci.draw_polyline(pts, Color(GeoRegionOverlay.STYLE["salmon_fjord"]["line"], fade), 1.5, true)
			for i in 8:
				var a := TAU * i / 8.0
				ci.draw_arc(r.position + Vector2(20, r.size.y * 0.5), 5.0, a, a + TAU / 16.0, 3,
					Color(GeoRegionOverlay.STYLE["anadromous_river"]["line"], fade), 1.5, true)
		"sites":
			ci.draw_rect(Rect2(r.position + Vector2(2, 4), Vector2(9, 8)), Color(GeoRegionIcons.SNOW, 0.8 * fade), false, 1.5)
			ci.draw_circle(r.position + Vector2(19, r.size.y * 0.5), 5.0, GeoRegionIcons.HALO)
			ci.draw_arc(r.position + Vector2(19, r.size.y * 0.5), 3.5, 0.0, TAU, 16,
				Color(CompetitorFarmVisualManager.BAND_PALETTE[1], fade), 2.0, true)
		"candidates":
			GeoRegionIcons.draw_star(ci, r.position + Vector2(8, r.size.y * 0.5), 7.0, "suggested" if on else "taken")
			GeoRegionIcons.draw_hexagon(ci, r.position + Vector2(20, r.size.y * 0.5), 5.5, false)
		"hatch":
			ci.draw_rect(Rect2(r.position + Vector2(1, 2), r.size - Vector2(2, 4)), Color(GeoRegionIcons.HALO, 0.35))
			var i := -int(r.size.y)
			while i < int(r.size.x):
				var a := r.position + Vector2(i, r.size.y - 2.0)
				var b := r.position + Vector2(i + r.size.y - 4.0, 2.0)
				ci.draw_line(Vector2(maxf(a.x, r.position.x + 1), a.y - maxf(r.position.x + 1 - a.x, 0.0)),
					Vector2(minf(b.x, r.end.x - 1), b.y + maxf(b.x - r.end.x + 1, 0.0)), Color(GeoRegionMath.HATCH_COLOR, 0.95), 1.0)
				i += 4
		"site":
			ci.draw_circle(c, 7.0, GeoRegionIcons.HALO)
			ci.draw_circle(c, 4.5, GeoRegionIcons.SALMON)
			ci.draw_arc(c, 6.0, 0.0, TAU, 24, GeoRegionIcons.SNOW, 1.5, true)
		"hex":
			GeoRegionIcons.draw_hexagon(ci, c, 7.0, false)


## A legend swatch drawn in the map's own icon language.
class LegendIcon extends Control:
	var kind := ""

	func _draw() -> void:
		GeoRegionIcons.draw_layer_icon(self, Rect2(Vector2.ZERO, size), kind)
