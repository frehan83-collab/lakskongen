class_name NorwayCoastline
extends RefCounted
## Real Norwegian coastline geometry (Natural Earth 50m, clipped + simplified).
## Shared by MapPanel (light nautical chart) and MiniMap (dark theme).
## Coordinates are normalised 0..1 (x = east, y = DOWN = north-up); project()
## maps lon/lat into the same space using the embedded projection meta.

const DATA_PATH := "res://data/norway_coastline.json"

static var _cache: Dictionary = {}

static func load_data() -> Dictionary:
	if not _cache.is_empty():
		return _cache
	if not FileAccess.file_exists(DATA_PATH):
		push_warning("NorwayCoastline: missing " + DATA_PATH)
		return {}
	var text: String = FileAccess.get_file_as_string(DATA_PATH)
	var parsed: Variant = JSON.parse_string(text)
	if typeof(parsed) != TYPE_DICTIONARY:
		push_warning("NorwayCoastline: corrupt JSON")
		return {}
	_cache = parsed
	return _cache

## Normalised map position for a lon/lat (matches the stored rings).
static func project(lon: float, lat: float) -> Vector2:
	var data := load_data()
	if data.is_empty():
		return Vector2(0.5, 0.5)
	var p: Dictionary = data["projection"]
	var x: float = ((lon - float(p["lon0"])) * float(p["kx"]) - float(p["mnx"])) / float(p["w"])
	var y: float = ((float(p["lat1"]) - lat - float(p["mny"]))) / float(p["h"])
	return Vector2(x, y)

static func rings() -> Array:
	var data := load_data()
	if data.is_empty():
		return []
	return data.get("rings", [])

## Open mainland polyline (first point != last) vs closed island rings.
static func is_open_ring(ring: Array) -> bool:
	if ring.size() < 2:
		return false
	var a: Array = ring[0]
	var b: Array = ring[ring.size() - 1]
	return absf(float(a[0]) - float(b[0])) > 0.0005 or absf(float(a[1]) - float(b[1])) > 0.0005

## Draw the landmass fill onto a CanvasItem. Islands are closed polygons;
## the open mainland polyline is filled with per-segment quads to the eastern
## map edge — fjord in-and-out traces make the closed polygon self-touching
## (triangulation fails), while same-colour quad overdraw is harmless.
static func draw_landmass(canvas: CanvasItem, size: Vector2, fill: Color) -> void:
	var data := load_data()
	if data.is_empty():
		return
	var east := size.x * 1.08
	for ring in rings():
		if is_open_ring(ring):
			var prev := Vector2.ZERO
			var first := true
			for p in ring:
				var a: Array = p
				var cur := Vector2(float(a[0]) * size.x, float(a[1]) * size.y)
				if not first:
					canvas.draw_colored_polygon(PackedVector2Array(
						[prev, cur, Vector2(east, cur.y), Vector2(east, prev.y)]), fill)
				prev = cur
				first = false
		else:
			var poly := PackedVector2Array()
			for p in ring:
				var a: Array = p
				poly.append(Vector2(float(a[0]) * size.x, float(a[1]) * size.y))
			if poly.size() >= 3:
				canvas.draw_colored_polygon(poly, fill)

## Major coastal cities (lon, lat, label).
static func cities() -> Array:
	return [
		[5.73, 58.97, "Stavanger"],
		[5.32, 60.39, "Bergen"],
		[6.15, 62.47, "Ålesund"],
		[10.39, 63.43, "Trondheim"],
		[14.39, 67.28, "Bodø"],
		[18.95, 69.65, "Tromsø"],
		[23.68, 70.66, "Hammerfest"],
		[31.11, 70.37, "Vardø"],
	]
