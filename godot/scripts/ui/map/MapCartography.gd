class_name MapCartography
extends RefCounted
## The national map's cartographic furniture (geo stages 2-3 plan, Track C),
## two small canvas items GeoMapView lays out on a resize only:
## - Graticule: parallels every 2° and meridians every 5° through the map's
##   own projection, with the latitudes along the right edge;
## - Furniture: the eight city names, clear of the plates and anchors (and of
##   the cluster badges where they can be), a scale bar and a north arrow.
## Text drawn here is translated (MAP_GEO_*) and never an id.

const CITY_FONT := 13


## The chart's graticule: parallels every 2° and meridians every 5° through
## the same projection as the map, faint over the image and across the deep
## water around it, with the parallels' latitudes along the right edge.
class Graticule extends Control:
	const PARALLELS: Array[int] = [58, 60, 62, 64, 66, 68, 70]
	const MERIDIANS: Array[int] = [5, 10, 15, 20, 25, 30]
	const LABEL_FONT := 12
	var _lines: Array[PackedVector2Array] = []
	var _labels: Array = []  # [text, baseline position]

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE

	func layout_lines(view: GeoMapView, keep_clear: Array[Rect2]) -> void:
		_lines.clear()
		_labels.clear()
		var font := get_theme_font(&"font", &"BodySmall")
		for lat in PARALLELS:
			var line := PackedVector2Array()
			for k in 81:
				var lon := -20.0 + 1.0 * k
				var xy := GeoProjection.utm33(lon, float(lat))
				line.append(view.utm_to_local(xy[0], xy[1]))
			_lines.append(line)
			var text := tr("MAP_GEO_LAT") % lat
			var w := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT).x if font != null else 40.0
			var x := size.x - w - 10.0
			var y := _y_at(line, size.x - 6.0)
			var r := Rect2(Vector2(x - 2.0, y - LABEL_FONT - 2.0), Vector2(w + 4.0, LABEL_FONT + 6.0))
			if y > 20.0 and y < size.y - 6.0 and not keep_clear.any(func(o: Rect2) -> bool: return o.intersects(r)):
				_labels.append([text, Vector2(x, y - 3.0)])
		for lon in MERIDIANS:
			var line := PackedVector2Array()
			for k in 41:
				var xy := GeoProjection.utm33(float(lon), 55.0 + 0.45 * k)
				line.append(view.utm_to_local(xy[0], xy[1]))
			_lines.append(line)
		queue_redraw()

	## The y of a polyline where it crosses x (its last sample before x).
	static func _y_at(line: PackedVector2Array, x: float) -> float:
		for i in range(line.size() - 1):
			var a := line[i]
			var b := line[i + 1]
			if (a.x - x) * (b.x - x) <= 0.0 and absf(b.x - a.x) > 0.001:
				return lerpf(a.y, b.y, (x - a.x) / (b.x - a.x))
		return -1.0

	func _draw() -> void:
		var snow := MapPalette.snow()
		for line in _lines:
			draw_polyline(line, Color(snow, 0.11), 1.0, true)
		var font := get_theme_font(&"font", &"BodySmall")
		if font == null:
			return
		for l: Array in _labels:
			draw_string_outline(font, l[1], str(l[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT, 3,
				Color(MapPalette.deep_water(), 0.9))
			draw_string(font, l[1], str(l[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT, Color(snow, 0.6))


## The chart's furniture: city names that keep clear of the plates, a scale
## bar and a north arrow. One small canvas item, recorded on layout only.
class Furniture extends Control:
	## The city name rects drawn (GeoMapView.city_label_rects).
	var placed: Array[Rect2] = []
	var _cities: Array = []  # [name, anchor, label rect]
	var _scale_rect := Rect2()
	var _north_rect := Rect2()
	var _metres_per_px := 1.0
	var _km := 100

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE

	## Scale bar (bottom left) and north arrow (top right), in the deep-water
	## frame beside a portrait map, so neither sits on the coast.
	func layout_frame(drawn: Rect2, metres_per_px: float) -> void:
		_metres_per_px = metres_per_px
		_km = 200 if 200000.0 / metres_per_px <= drawn.size.x * 0.28 else 100
		var bar_px := float(_km) * 1000.0 / metres_per_px
		_scale_rect = Rect2(Vector2(14.0, size.y - 34.0), Vector2(bar_px + 46.0, 22.0))
		_north_rect = Rect2(Vector2(size.x - 40.0, 10.0), Vector2(26.0, 38.0))
		queue_redraw()

	func frame_rects() -> Array[Rect2]:
		return [_scale_rect.grow(3.0), _north_rect.grow(3.0)]

	## Place the city names: east, west, above or below the city, else left out.
	func layout_cities(cities: Array, bounds: Rect2, taken: Array[Rect2], label_only: Array[Rect2] = []) -> void:
		var font := get_theme_font(&"font", &"BodySmallStrong")
		var anchors := PackedVector2Array()
		var sizes := PackedVector2Array()
		var usable: Array = []
		var busy: Array[Rect2] = taken.duplicate()
		for c: Array in cities:
			var dot := Rect2(c[1] - Vector2(3.5, 3.5), Vector2(7.0, 7.0))
			if not bounds.encloses(dot) or _hits(dot, busy):
				continue
			usable.append(c)
			anchors.append(c[1])
			var sz := font.get_string_size(str(c[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT) if font != null else Vector2(60, 14)
			sizes.append(Vector2(ceilf(sz.x) + 2.0, float(CITY_FONT) + 5.0))
		for c: Array in usable:
			busy.append(Rect2(c[1] - Vector2(3.5, 3.5), Vector2(7.0, 7.0)))
		var rects := MapLabelPlacer.place_optional(anchors, sizes, 6.0, bounds, busy, label_only)
		placed.clear()
		_cities.clear()
		for i in usable.size():
			if rects[i].size == Vector2.ZERO:
				continue
			placed.append(rects[i])
			_cities.append([usable[i][0], usable[i][1], rects[i]])
		queue_redraw()

	static func _hits(r: Rect2, rects: Array[Rect2]) -> bool:
		for o in rects:
			if o.intersects(r):
				return true
		return false

	func _draw() -> void:
		var font := get_theme_font(&"font", &"BodySmallStrong")
		var deep := MapPalette.deep_water()
		var snow := MapPalette.snow()
		for c: Array in _cities:
			var p: Vector2 = c[1]
			draw_circle(p, 3.5, deep)
			draw_circle(p, 2.2, snow)
			if font != null:
				var r: Rect2 = c[2]
				var at := Vector2(r.position.x + 1.0, r.position.y + font.get_ascent(CITY_FONT) + 2.0)
				draw_string_outline(font, at, str(c[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, 4, Color(deep, 0.92))
				draw_string(font, at, str(c[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, snow)
		_draw_scale(font, deep, snow)
		_draw_north(font, deep, snow)

	func _draw_scale(font: Font, deep: Color, snow: Color) -> void:
		if _scale_rect.size == Vector2.ZERO:
			return
		var bar_px := float(_km) * 1000.0 / _metres_per_px
		var y := _scale_rect.end.y - 6.0
		var x0 := _scale_rect.position.x
		draw_rect(Rect2(x0 - 1.0, y - 4.0, bar_px + 2.0, 6.0), Color(deep, 0.85))
		draw_rect(Rect2(x0, y - 3.0, bar_px * 0.5, 4.0), snow)
		draw_rect(Rect2(x0 + bar_px * 0.5, y - 3.0, bar_px * 0.5, 4.0), Color(snow, 0.45))
		if font != null:
			var text := tr("MAP_GEO_SCALE_KM") % _km
			var at := Vector2(x0 + bar_px + 7.0, y + 2.0)
			draw_string_outline(font, at, text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, 4, Color(deep, 0.92))
			draw_string(font, at, text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, snow)

	func _draw_north(font: Font, deep: Color, snow: Color) -> void:
		if _north_rect.size == Vector2.ZERO:
			return
		var c := _north_rect.position + Vector2(_north_rect.size.x * 0.5, 24.0)
		var tip := c + Vector2(0.0, -14.0)
		var arrow := PackedVector2Array([tip, c + Vector2(6.0, 6.0), c + Vector2(0.0, 2.0), c + Vector2(-6.0, 6.0)])
		draw_colored_polygon(arrow, Color(deep, 0.85))
		draw_colored_polygon(PackedVector2Array([tip, c + Vector2(0.0, 2.0), c + Vector2(-6.0, 6.0)]), snow)
		draw_polyline(PackedVector2Array([tip, c + Vector2(6.0, 6.0), c + Vector2(0.0, 2.0), c + Vector2(-6.0, 6.0), tip]),
			snow, 1.0, true)
		if font != null:
			var text := tr("MAP_GEO_NORTH")
			var w := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT).x
			var at := Vector2(c.x - w * 0.5, _north_rect.position.y + 7.0)
			draw_string_outline(font, at, text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, 4, Color(deep, 0.92))
			draw_string(font, at, text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, snow)
